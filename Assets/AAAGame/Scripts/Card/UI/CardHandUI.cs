using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;
using AAAGame.Card;

namespace AAAGame.Card.UI
{
    /// <summary>
    /// 单张手牌 UI - 完全独立版本
    /// 负责单张卡牌的显示和交互
    /// </summary>
    public class CardHandUI : UIItemBase, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("UI 组件")]
        [SerializeField] private Image cardImage;
        [SerializeField] private TextMeshProUGUI cardNameText;
        [SerializeField] private TextMeshProUGUI populationCostText;
        [SerializeField] private TextMeshProUGUI soldierCountText;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("拖拽设置")]
        [SerializeField] private float dragScale = 1.2f;
        [SerializeField] private float hoverScale = 1.1f;
        [SerializeField] private float normalScale = 1.0f;

        private CardData cardData;
        private RectTransform rectTransform;
        private Vector3 originalPosition;
        private Transform originalParent;
        private int originalSiblingIndex;
        private Canvas canvas;
        
        private bool isDragging = false;
        private bool canDrag = true;
        private bool isMovingToHand = false;
        private bool isSelected = false;

        private Tween scaleTween;
        private Tween moveTween;

        public CardData CardData => cardData;
        public bool IsMovingToHand => isMovingToHand;
        public bool IsDragging => isDragging;

        void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            
            // 先尝试获取 CanvasGroup，如果没有再添加
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
                Debug.Log("[Card] CardHandUI: Added CanvasGroup component");
            }
            
            canvas = GetComponentInParent<Canvas>();
            
            Debug.Log($"[Card] CardHandUI.Awake: rectTransform={rectTransform != null}, canvasGroup={canvasGroup != null}, canvas={canvas != null}");
        }

        /// <summary>
        /// 设置卡牌数据
        /// </summary>
        public void SetCardData(CardData data)
        {
            cardData = data;
            if (cardData == null)
            {
                Debug.LogError("[Card] CardHandUI.SetCardData: cardData is null");
                return;
            }

            UpdateCardDisplay();
            UpdateDragability();
        }

        /// <summary>
        /// 更新卡牌显示
        /// </summary>
        private void UpdateCardDisplay()
        {
            if (cardData == null) return;

            // 设置卡牌名称
            if (cardNameText != null)
            {
                cardNameText.text = cardData.cardName;
            }

            // 设置卡面图片或颜色
            if (cardImage != null)
            {
                if (cardData.cardSprite != null)
                {
                    cardImage.sprite = cardData.cardSprite;
                    cardImage.color = Color.white;
                }
                else
                {
                    cardImage.sprite = null;
                    cardImage.color = cardData.cardColor;
                }
            }

            // 设置人口消耗
            if (populationCostText != null)
            {
                populationCostText.text = cardData.populationCost.ToString();
            }

            // 设置士兵数量
            if (soldierCountText != null)
            {
                soldierCountText.text = cardData.soldierCount.ToString();
            }

            Debug.Log($"[Card] CardHandUI updated: {cardData.cardName}");
        }

        /// <summary>
        /// 更新可拖拽状态
        /// </summary>
        public void UpdateDragability()
        {
            if (cardData == null) return;

            // 检查人口是否足够
            if (PopulationManager.Instance != null)
            {
                canDrag = PopulationManager.Instance.HasEnoughPopulation(cardData.populationCost);
            }
            else
            {
                canDrag = true; // 如果没有 PopulationManager，默认可拖拽
            }

            // 人口不足时置灰
            if (canvasGroup != null)
            {
                canvasGroup.alpha = canDrag ? 1f : 0.5f;
            }
        }

        /// <summary>
        /// 设置选中状态
        /// </summary>
        public void SetSelected(bool selected)
        {
            isSelected = selected;
            
            if (isSelected)
            {
                ScaleTo(dragScale);
            }
            else
            {
                ScaleTo(normalScale);
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
                ScaleTo(normalScale);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (isMovingToHand || !canDrag)
            {
                Debug.LogWarning($"[Card] Cannot drag card: {cardData?.cardName}");
                return;
            }

            isDragging = true;
            originalPosition = rectTransform.position;
            originalParent = transform.parent;
            originalSiblingIndex = transform.GetSiblingIndex();

            // 移到 Canvas 顶层
            if (canvas != null)
            {
                transform.SetParent(canvas.transform);
                transform.SetAsLastSibling();
            }

            canvasGroup.blocksRaycasts = false;
            ScaleTo(dragScale);

            // 通知 UI 管理器
            CardUISystem.Instance?.OnCardBeginDrag(this);

            Debug.Log($"[Card] Begin drag: {cardData.cardName}");
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isDragging) return;

            // 跟随鼠标
            rectTransform.position = eventData.position;

            // 通知 UI 管理器
            CardUISystem.Instance?.OnCardDragging(this, eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!isDragging) return;

            isDragging = false;
            canvasGroup.blocksRaycasts = true;

            // 通知 UI 管理器
            bool success = CardUISystem.Instance?.OnCardEndDrag(this, eventData.position) ?? false;

            if (!success)
            {
                // 返回原位
                ReturnToOriginalPosition();
            }
            else
            {
                // 成功打出，销毁 UI
                Destroy(gameObject);
            }

            Debug.Log($"[Card] End drag: {cardData.cardName}, success: {success}");
        }

        #endregion

        /// <summary>
        /// 返回原位
        /// </summary>
        private void ReturnToOriginalPosition()
        {
            if (originalParent != null)
            {
                transform.SetParent(originalParent);
                transform.SetSiblingIndex(originalSiblingIndex);
            }

            moveTween?.Kill();
            moveTween = rectTransform.DOMove(originalPosition, 0.3f).SetEase(Ease.OutBack);
            ScaleTo(normalScale);

            Debug.Log($"[Card] Card returned to hand: {cardData.cardName}");
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
        /// 从屏幕位置移动到手牌区
        /// </summary>
        public void MoveToHandFromScreenPosition(Vector2 startScreenPosition, float duration = 0.5f)
        {
            // 确保组件已初始化
            if (rectTransform == null)
            {
                rectTransform = GetComponent<RectTransform>();
            }

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                    Debug.Log("[Card] CardHandUI.MoveToHandFromScreenPosition: Added CanvasGroup component");
                }
            }

            // 再次检查
            if (rectTransform == null)
            {
                Debug.LogError("[Card] CardHandUI.MoveToHandFromScreenPosition: rectTransform is null after GetComponent");
                return;
            }

            if (canvasGroup == null)
            {
                Debug.LogError("[Card] CardHandUI.MoveToHandFromScreenPosition: canvasGroup is null after AddComponent");
                return;
            }

            isMovingToHand = true;
            canvasGroup.blocksRaycasts = false;

            // 记录目标位置
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
                    Debug.Log($"[Card] Card moved to hand: {cardData?.cardName}");
                });

            Debug.Log($"[Card] Start moving card to hand: {cardData?.cardName}");
        }

        void OnDestroy()
        {
            scaleTween?.Kill();
            moveTween?.Kill();
        }
    }
}
