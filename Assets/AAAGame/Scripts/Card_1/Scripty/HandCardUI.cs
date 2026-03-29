using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;
using UnityGameFramework.Runtime;

/// <summary>
/// 手牌UI - 单张卡牌的显示和交互
/// </summary>
public class HandCardUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI组件")]
    [SerializeField] private Image cardImage;
    [SerializeField] private TextMeshProUGUI populationText;
    [SerializeField] private TextMeshProUGUI soldierCountText;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("拖拽设置")]
    [SerializeField] private float dragScale = 1.2f;
    [SerializeField] private float hoverScale = 1.1f;

    private CardData cardData;
    private RectTransform rectTransform;
    private Vector3 originalPosition;
    private Transform originalParent;
    private int originalSiblingIndex;
    private Canvas canvas;
    private bool isDragging = false;
    private bool canDrag = true; // 人口是否足够
    private bool isMovingToHand = false; // 是否正在移动到手牌
    private bool isSelected = false; // 是否被快捷键选中

    private Tween scaleTween;
    private Tween moveTween;

    public CardData CardData => cardData;
    public bool IsMovingToHand => isMovingToHand;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        canvas = GetComponentInParent<Canvas>();
    }

    /// <summary>
    /// 设置选中状态
    /// </summary>
    public void SetSelected(bool selected)
    {
        isSelected = selected;
        if (isSelected)
        {
            // 选中时放大到 1.2 倍
            ScaleTo(1.2f);
        }
        else
        {
            // 取消选中时恢复原大小
            ScaleTo(1f);
        }
    }

    /// <summary>
    /// 设置卡牌数据
    /// </summary>
    public void SetCardData(CardData data)
    {
        cardData = data?.GetCardData();
        if (cardData == null) return;

        // 更新UI显示
        UpdateCardDisplay();

        // 检查人口是否足够
        UpdateDragability();
    }

    /// <summary>
    /// 更新卡牌显示
    /// </summary>
    private void UpdateCardDisplay()
    {
        if (cardData == null)
        {
            GF.LogError("UpdateCardDisplay: cardData 为 null");
            return;
        }

        GF.Log($"更新卡牌显示：{cardData.cardName}, 人口:{cardData.populationCost}, 士兵:{cardData.soldierCount}");

        // 设置卡面（如果有sprite就用sprite，否则用纯色）
        if (cardImage != null)
        {
            if (cardData.cardSprite != null)
            {
                cardImage.sprite = cardData.cardSprite;
                cardImage.color = Color.white;
                GF.Log($"设置卡牌图片：{cardData.cardSprite.name}");
            }
            else
            {
                cardImage.sprite = null;
                cardImage.color = cardData.cardColor;
                GF.Log($"设置卡牌颜色：{cardData.cardColor}");
            }
        }
        else
        {
            GF.LogWarning("cardImage 引用为 null，请在预制体中配置");
        }

        // 设置人口消耗
        if (populationText != null)
        {
            populationText.text = cardData.populationCost.ToString();
            GF.Log($"设置人口消耗文本：{cardData.populationCost}");
        }
        else
        {
            GF.LogWarning("populationText 引用为 null，请在预制体中配置");
        }

        // 设置士兵数量
        if (soldierCountText != null)
        {
            soldierCountText.text = cardData.soldierCount.ToString();
            GF.Log($"设置士兵数量文本：{cardData.soldierCount}");
        }
        else
        {
            GF.LogWarning("soldierCountText 引用为 null，请在预制体中配置");
        }
    }

    /// <summary>
    /// 更新可拖拽状态
    /// </summary>
    public void UpdateDragability()
    {
        if (cardData == null) return;

        canDrag = PopulationManager.Instance != null &&
                  PopulationManager.Instance.HasEnoughPopulation(cardData.populationCost);

        // 人口不足时置灰
        if (canvasGroup != null)
        {
            canvasGroup.alpha = canDrag ? 1f : 0.5f;
        }
    }

    #region 鼠标事件

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isDragging && !isSelected && canDrag)
        {
            ScaleTo(hoverScale);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isDragging && !isSelected)
        {
            ScaleTo(1f);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // 移动中不可拖拽
        if (isMovingToHand)
        {
            return;
        }

        if (!canDrag)
        {
            GF.LogWarning($"人口不足，无法打出 {cardData.cardName}");
            return;
        }

        isDragging = true;
        originalPosition = rectTransform.position;
        originalParent = transform.parent;
        originalSiblingIndex = transform.GetSiblingIndex();

        // 移到Canvas顶层
        transform.SetParent(canvas.transform);
        transform.SetAsLastSibling();

        canvasGroup.blocksRaycasts = false;
        ScaleTo(dragScale);

        // 通知UI管理器开始拖拽
        CardUIManager.Instance?.OnCardBeginDrag(this);

        GF.Log($"开始拖拽：{cardData.cardName}");
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging) return;

        // 跟随鼠标
        rectTransform.position = eventData.position;

        // 通知UI管理器更新拖拽状态
        CardUIManager.Instance?.OnCardDragging(this, eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging) return;

        isDragging = false;
        canvasGroup.blocksRaycasts = true;

        // 通知UI管理器结束拖拽
        bool success = CardUIManager.Instance?.OnCardEndDrag(this, eventData.position) ?? false;

        if (!success)
        {
            // 返回原位
            ReturnToOriginalPosition();
        }
        else
        {
            // 成功打出，销毁UI
            Destroy(gameObject);
        }
    }

    #endregion

    /// <summary>
    /// 返回原位
    /// </summary>
    private void ReturnToOriginalPosition()
    {
        transform.SetParent(originalParent);
        transform.SetSiblingIndex(originalSiblingIndex);

        rectTransform.DOMove(originalPosition, 0.3f).SetEase(Ease.OutBack);
        ScaleTo(1f);

        GF.Log($"卡牌返回手牌区：{cardData.cardName}");
    }

    /// <summary>
    /// 缩放动画
    /// </summary>
    private void ScaleTo(float scale)
    {
        scaleTween?.Kill();
        scaleTween = transform.DOScale(Vector3.one * scale, 0.2f).SetEase(Ease.OutBack);
    }

    /// <summary>
    /// 从指定位置移动到手牌区
    /// </summary>
    public void MoveToHandFromPosition(Vector3 startWorldPosition, float duration = 0.5f, System.Action onComplete = null)
    {
        isMovingToHand = true;
        canvasGroup.blocksRaycasts = false;

        // 设置起始位置
        rectTransform.position = startWorldPosition;

        // 记录目标位置
        Vector3 targetPosition = rectTransform.position;

        // 先移回起始位置
        rectTransform.position = startWorldPosition;

        // 移动到目标位置
        moveTween?.Kill();
        moveTween = rectTransform.DOMove(targetPosition, duration)
            .SetEase(Ease.OutCubic)
            .OnComplete(() =>
            {
                isMovingToHand = false;
                canvasGroup.blocksRaycasts = true;
                onComplete?.Invoke();
                GF.Log($"卡牌 {cardData?.cardName} 移动到手牌完成");
            });

        GF.Log($"卡牌 {cardData?.cardName} 开始移动到手牌");
    }

    /// <summary>
    /// 从屏幕位置移动到手牌区
    /// </summary>
    public void MoveToHandFromScreenPosition(Vector2 startScreenPosition, float duration = 0.5f, System.Action onComplete = null)
    {
        isMovingToHand = true;
        canvasGroup.blocksRaycasts = false;

        // 记录目标位置（当前在手牌容器中的位置）
        Vector3 targetPosition = rectTransform.position;

        // 设置起始位置
        rectTransform.position = startScreenPosition;

        // 移动到目标位置
        moveTween?.Kill();
        moveTween = rectTransform.DOMove(targetPosition, duration)
            .SetEase(Ease.OutCubic)
            .OnComplete(() =>
            {
                isMovingToHand = false;
                canvasGroup.blocksRaycasts = true;
                onComplete?.Invoke();
                GF.Log($"卡牌 {cardData?.cardName} 移动到手牌完成");
            });

        GF.Log($"卡牌 {cardData?.cardName} 开始从屏幕位置移动到手牌");
    }

    void OnDestroy()
    {
        scaleTween?.Kill();
        moveTween?.Kill();
    }
}
