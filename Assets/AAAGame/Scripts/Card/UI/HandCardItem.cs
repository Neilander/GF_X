using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;
using GameFramework;
using UnityGameFramework.Runtime;

namespace AAAGame.Card
{
    /// <summary>
    /// 手牌UI项 - 继承 UIItemBase，使用 GF_X 对象池
    /// 单张卡牌的显示和交互组件
    /// </summary>
    public class HandCardItem : UIItemBase, IBeginDragHandler, IDragHandler, IEndDragHandler, 
        IPointerEnterHandler, IPointerExitHandler
    {
        [Header("UI组件")]
        [SerializeField] private Image cardImage;
        [SerializeField] private TextMeshProUGUI populationText;
        [SerializeField] private TextMeshProUGUI soldierCountText;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("动画设置")]
        [SerializeField] private float dragScale = 1.2f;
        [SerializeField] private float hoverScale = 1.1f;
        [SerializeField] private float animationDuration = 0.2f;

        private CardModel m_CardModel;
        private CardUIForm m_ParentForm;
        private RectTransform m_RectTransform;
        private Transform m_OriginalParent;
        private Vector3 m_OriginalPosition;
        private int m_OriginalSiblingIndex;
        private Canvas m_Canvas;
        
        private bool m_IsDragging;
        private bool m_CanPlay;
        private bool m_IsSelected;
        
        private Tween m_ScaleTween;
        private Tween m_MoveTween;

        protected override void OnInit()
        {
            base.OnInit();
            
            m_RectTransform = GetComponent<RectTransform>();
            
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
            
            m_Canvas = GetComponentInParent<Canvas>();
        }

        /// <summary>
        /// 初始化
        /// </summary>
        public void Initialize(CardModel cardModel, CardUIForm parentForm)
        {
            m_CardModel = cardModel;
            m_ParentForm = parentForm;
            
            UpdateDisplay();
            UpdatePlayability();
        }

        /// <summary>
        /// 更新显示
        /// </summary>
        private void UpdateDisplay()
        {
            if (m_CardModel == null || m_CardModel.DataProvider == null)
            {
                Log.Error("CardModel or DataProvider is null.");
                return;
            }

            ICardDataProvider data = m_CardModel.DataProvider;

            // 设置卡面
            if (cardImage != null)
            {
                if (data.CardSprite != null)
                {
                    cardImage.sprite = data.CardSprite;
                    cardImage.color = Color.white;
                }
                else
                {
                    cardImage.sprite = null;
                    cardImage.color = data.CardColor;
                }
            }

            // 设置人口消耗
            if (populationText != null)
            {
                populationText.text = data.PopulationCost.ToString();
            }

            // 设置士兵数量
            if (soldierCountText != null)
            {
                soldierCountText.text = data.SoldierCount.ToString();
            }
        }

        /// <summary>
        /// 更新可打出状态
        /// </summary>
        public void UpdatePlayability()
        {
            if (m_CardModel == null) return;

            m_CanPlay = m_CardModel.CanPlay();

            // 人口不足时置灰
            if (canvasGroup != null)
            {
                canvasGroup.alpha = m_CanPlay ? 1f : 0.5f;
            }
        }

        /// <summary>
        /// 设置选中状态
        /// </summary>
        public void SetSelected(bool selected)
        {
            m_IsSelected = selected;
            ScaleTo(m_IsSelected ? 1.2f : 1f);
        }

        /// <summary>
        /// 是否可以打出
        /// </summary>
        public bool CanPlay()
        {
            return m_CanPlay;
        }

        /// <summary>
        /// 获取卡牌模型
        /// </summary>
        public CardModel GetCardModel()
        {
            return m_CardModel;
        }

        #region 拖拽事件

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!m_CanPlay)
            {
                Log.Warning(Utility.Text.Format("Population not enough to play {0}", 
                    m_CardModel.DataProvider.CardName));
                return;
            }

            m_IsDragging = true;
            m_OriginalPosition = m_RectTransform.position;
            m_OriginalParent = transform.parent;
            m_OriginalSiblingIndex = transform.GetSiblingIndex();

            // 移到Canvas顶层
            transform.SetParent(m_Canvas.transform);
            transform.SetAsLastSibling();

            canvasGroup.blocksRaycasts = false;
            ScaleTo(dragScale);

            // 通知父界面
            m_ParentForm?.OnCardBeginDrag(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!m_IsDragging) return;

            // 跟随鼠标
            m_RectTransform.position = eventData.position;

            // 通知父界面
            m_ParentForm?.OnCardDragging(this, eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!m_IsDragging) return;

            m_IsDragging = false;
            canvasGroup.blocksRaycasts = true;

            // 通知父界面
            bool success = m_ParentForm?.OnCardEndDrag(this, eventData.position) ?? false;

            if (!success)
            {
                // 返回原位
                ReturnToOriginalPosition();
            }
            else
            {
                // 成功打出或丢弃，不需要返回原位
                // 对象会被父界面回收到对象池
                Log.Info($"[HandCardItem] Card action successful, will be recycled");
            }
        }

        #endregion

        #region 鼠标悬停事件

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!m_IsDragging && !m_IsSelected && m_CanPlay)
            {
                ScaleTo(hoverScale);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!m_IsDragging && !m_IsSelected)
            {
                ScaleTo(1f);
            }
        }

        #endregion

        /// <summary>
        /// 返回原位
        /// </summary>
        private void ReturnToOriginalPosition()
        {
            transform.SetParent(m_OriginalParent);
            transform.SetSiblingIndex(m_OriginalSiblingIndex);

            m_MoveTween?.Kill();
            m_MoveTween = m_RectTransform.DOMove(m_OriginalPosition, 0.3f)
                .SetEase(Ease.OutBack);
            
            ScaleTo(1f);
        }

        /// <summary>
        /// 缩放动画
        /// </summary>
        private void ScaleTo(float scale)
        {
            m_ScaleTween?.Kill();
            m_ScaleTween = transform.DOScale(Vector3.one * scale, animationDuration)
                .SetEase(Ease.OutBack);
        }

        /// <summary>
        /// 从屏幕位置移动到手牌区
        /// </summary>
        public void MoveToHandFromScreenPosition(Vector2 startScreenPosition, float duration = 0.5f)
        {
            canvasGroup.blocksRaycasts = false;

            Vector3 targetPosition = m_RectTransform.position;
            m_RectTransform.position = startScreenPosition;

            m_MoveTween?.Kill();
            m_MoveTween = m_RectTransform.DOMove(targetPosition, duration)
                .SetEase(Ease.OutCubic)
                .OnComplete(() =>
                {
                    canvasGroup.blocksRaycasts = true;
                });
        }

        /// <summary>
        /// 丢弃成功回调（在拖拽过程中被丢弃）
        /// </summary>
        public void OnDiscardSuccess()
        {
            // 立即停止拖拽状态
            m_IsDragging = false;
            canvasGroup.blocksRaycasts = false; // 禁用交互，防止再次拖拽
            
            // 恢复父级（避免卡在 Canvas 顶层）
            if (m_OriginalParent != null)
            {
                transform.SetParent(m_OriginalParent);
            }
            
            // 播放消失动画（可选）
            m_ScaleTween?.Kill();
            m_ScaleTween = transform.DOScale(Vector3.zero, 0.2f)
                .SetEase(Ease.InBack);
            
            // 淡出效果
            if (canvasGroup != null)
            {
                canvasGroup.DOFade(0f, 0.2f);
            }
            
            Log.Info($"[HandCardItem] ✅ Card discard animation started: {m_CardModel?.GetCardName()}");
        }

        private void OnDestroy()
        {
            m_ScaleTween?.Kill();
            m_MoveTween?.Kill();
        }
    }
}
