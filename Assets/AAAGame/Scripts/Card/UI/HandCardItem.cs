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
        [SerializeField] private Image cardBackImage;
        [SerializeField] private Image cardImage;
        [SerializeField] private TextMeshProUGUI populationText;
        [SerializeField] private TextMeshProUGUI soldierCountText;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TextMeshProUGUI cardNameText;

        [Header("动画设置")]
        [SerializeField] private float dragScale = 1.2f;
        [SerializeField] private float animationDuration = 0.2f;

        [Header("悬停效果")]
        [SerializeField] [InspectorName("悬停放大倍率")] private float hoverScale = 1.15f;
        [SerializeField] [InspectorName("悬停边缘泛光材质")] private Material hoverGlowMaterial;
        [SerializeField] [InspectorName("悬停泛光目标")] private Graphic hoverGlowTarget;

        private CardModel m_CardModel;
        private CardUIForm m_ParentForm;
        private RectTransform m_RectTransform;
        private Transform m_OriginalParent;
        private Vector3 m_OriginalPosition;
        private int m_OriginalSiblingIndex;
        private Canvas m_Canvas;
        private Canvas m_SortingCanvas;
        private GraphicRaycaster m_SortingRaycaster;

        private bool m_IsDragging;
        private bool m_CanPlay;
        private bool m_IsSelected;
        private bool m_IsTargetingMode;
        private bool m_IsPointerInside;

        private Sprite m_DefaultCardBackSprite;
        private Color m_DefaultCardBackColor;
        private bool m_DefaultCardBackCached;
        private Material m_DefaultHoverGlowMaterial;
        private bool m_DefaultHoverGlowCached;

        private Tween m_ScaleTween;
        private Tween m_MoveTween;

        private const int NormalSortingOrder = 0;
        private const int HoverSortingOrder = 20;
        private const int DragSortingOrder = 40;

        protected override void OnInit()
        {
            base.OnInit();
            
            m_RectTransform = GetComponent<RectTransform>();
            ResolveCardBackImage();
            
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
            
            m_Canvas = GetComponentInParent<Canvas>();
            m_SortingCanvas = GetComponent<Canvas>();
            if (m_SortingCanvas == null)
            {
                m_SortingCanvas = gameObject.AddComponent<Canvas>();
            }

            m_SortingRaycaster = GetComponent<GraphicRaycaster>();
            if (m_SortingRaycaster == null)
            {
                m_SortingRaycaster = gameObject.AddComponent<GraphicRaycaster>();
            }

            m_SortingCanvas.overrideSorting = false;
            m_SortingCanvas.sortingOrder = NormalSortingOrder;
            m_SortingCanvas.enabled = true;
            m_SortingRaycaster.enabled = true;
        }

        /// <summary>
        /// 初始化
        /// </summary>
        public void Initialize(CardModel cardModel, CardUIForm parentForm)
        {
            ResetRuntimeState();
            m_CardModel = cardModel;
            m_ParentForm = parentForm;
            
            RefreshView();
        }

        private void ResetRuntimeState()
        {
            m_ScaleTween?.Kill();
            m_MoveTween?.Kill();

            m_IsDragging = false;
            m_CanPlay = false;
            m_IsSelected = false;
            m_IsTargetingMode = false;
            m_IsPointerInside = false;

            if (m_RectTransform != null)
            {
                m_RectTransform.localScale = Vector3.one;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.blocksRaycasts = true;
                canvasGroup.interactable = true;
            }

            ApplyHoverGlow(false);
            UpdateRenderPriority();
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
            ApplyCardBackDisplay(data);

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
                populationText.text = m_CardModel.GetOccupiedSupply().ToString();
            }

            // 设置士兵数量
            if (soldierCountText != null)
            {
                soldierCountText.text = m_CardModel.GetTroopCount().ToString();
            }

            if (cardNameText != null)
            {
                cardNameText.text = data.CardName;
            }
        }

        private void ResolveCardBackImage()
        {
            if (cardBackImage == null || cardBackImage == cardImage)
            {
                Image rootImage = GetComponent<Image>();
                if (rootImage != null && rootImage != cardImage)
                    cardBackImage = rootImage;
            }

            CacheDefaultCardBackState();
            ResolveHoverGlowTarget();
        }

        private void CacheDefaultCardBackState()
        {
            if (m_DefaultCardBackCached || cardBackImage == null)
                return;

            m_DefaultCardBackSprite = cardBackImage.sprite;
            m_DefaultCardBackColor = cardBackImage.color;
            m_DefaultCardBackCached = true;
        }

        private void ApplyCardBackDisplay(ICardDataProvider data)
        {
            if (data == null || cardBackImage == null)
                return;

            CacheDefaultCardBackState();

            if (data.CardBackSprite != null)
            {
                cardBackImage.sprite = data.CardBackSprite;
                cardBackImage.color = Color.white;
                return;
            }

            cardBackImage.sprite = m_DefaultCardBackSprite;
            if (m_DefaultCardBackSprite != null)
                cardBackImage.color = m_DefaultCardBackColor;
            else
                cardBackImage.color = data.CardColor;
        }

        private Graphic ResolveHoverGlowTarget()
        {
            if (hoverGlowTarget == null && cardBackImage != null)
            {
                hoverGlowTarget = cardBackImage;
            }

            if (hoverGlowTarget == null)
            {
                hoverGlowTarget = GetComponent<Graphic>();
            }

            CacheDefaultHoverGlowMaterial();
            return hoverGlowTarget;
        }

        private void CacheDefaultHoverGlowMaterial()
        {
            if (m_DefaultHoverGlowCached)
            {
                return;
            }

            Graphic target = hoverGlowTarget != null ? hoverGlowTarget : cardBackImage;
            if (target == null)
            {
                return;
            }

            m_DefaultHoverGlowMaterial = target.material;
            m_DefaultHoverGlowCached = true;
        }

        private void RefreshHoverVisualState()
        {
            bool suppressHover = m_ParentForm != null && m_ParentForm.HasActiveHandCardDrag(this);
            bool shouldHighlight = !suppressHover && m_IsPointerInside && !m_IsDragging && !m_IsSelected && m_CanPlay;
            ApplyHoverGlow(shouldHighlight);
            UpdateRenderPriority();

            if (!m_IsDragging && !m_IsSelected)
            {
                ScaleTo(shouldHighlight ? hoverScale : 1f);
            }
        }

        private void UpdateRenderPriority()
        {
            if (m_SortingCanvas == null)
            {
                return;
            }

            bool suppressHover = m_ParentForm != null && m_ParentForm.HasActiveHandCardDrag(this);
            bool shouldPromote = m_IsDragging || m_IsTargetingMode || m_IsSelected || (!suppressHover && m_IsPointerInside && m_CanPlay);
            if (!shouldPromote)
            {
                m_SortingCanvas.overrideSorting = false;
                m_SortingCanvas.sortingOrder = NormalSortingOrder;
                return;
            }

            m_SortingCanvas.overrideSorting = true;
            m_SortingCanvas.sortingOrder = m_IsDragging || m_IsTargetingMode
                ? DragSortingOrder
                : HoverSortingOrder;
        }

        private void ApplyHoverGlow(bool enabled)
        {
            Graphic target = ResolveHoverGlowTarget();
            if (target == null)
            {
                return;
            }

            if (enabled && hoverGlowMaterial != null)
            {
                if (target.material != hoverGlowMaterial)
                {
                    target.material = hoverGlowMaterial;
                    target.SetMaterialDirty();
                }

                return;
            }

            if (target.material != m_DefaultHoverGlowMaterial)
            {
                target.material = m_DefaultHoverGlowMaterial;
                target.SetMaterialDirty();
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

            RefreshHoverVisualState();
        }

        public void RefreshView()
        {
            UpdateDisplay();
            UpdatePlayability();
        }

        public void RefreshInteractionVisualState()
        {
            RefreshHoverVisualState();
        }

        /// <summary>
        /// 设置选中状态
        /// </summary>
        public void SetSelected(bool selected)
        {
            m_IsSelected = selected;

            if (m_IsSelected)
            {
                ApplyHoverGlow(false);
                UpdateRenderPriority();
                ScaleTo(1.2f);
                return;
            }

            RefreshHoverVisualState();
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
            m_IsPointerInside = false;
            m_OriginalPosition = m_RectTransform.position;
            m_OriginalParent = transform.parent;
            m_OriginalSiblingIndex = transform.GetSiblingIndex();

            // 移到Canvas顶层
            transform.SetParent(m_Canvas.transform);
            transform.SetAsLastSibling();

            canvasGroup.blocksRaycasts = false;
            ApplyHoverGlow(false);
            UpdateRenderPriority();
            ScaleTo(dragScale);

            // 通知父界面
            m_ParentForm?.OnCardBeginDrag(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!m_IsDragging) return;

            // 只有在普通拖拽态下才让卡牌本体跟随鼠标
            if (!m_IsTargetingMode)
            {
                m_RectTransform.position = eventData.position;
            }

            // 通知父界面
            m_ParentForm?.OnCardDragging(this, eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!m_IsDragging) return;

            m_IsDragging = false;
            canvasGroup.blocksRaycasts = true;
            ApplyHoverGlow(false);
            UpdateRenderPriority();

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
            m_IsPointerInside = true;
            RefreshHoverVisualState();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            m_IsPointerInside = false;
            RefreshHoverVisualState();
        }

        #endregion

        /// <summary>
        /// 返回原位
        /// </summary>
        private void ReturnToOriginalPosition()
        {
            m_IsTargetingMode = false;
            m_IsPointerInside = false;
            ApplyHoverGlow(false);
            UpdateRenderPriority();

            if (m_OriginalParent != null)
            {
                transform.SetParent(m_OriginalParent, true);
                transform.SetSiblingIndex(m_OriginalSiblingIndex);
                RefreshLayoutHierarchy(m_OriginalParent);
            }

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
            RefreshOwningLayout();

            Vector3 targetPosition = m_RectTransform.position;
            m_RectTransform.position = startScreenPosition;

            m_MoveTween?.Kill();
            m_MoveTween = m_RectTransform.DOMove(targetPosition, duration)
                .SetEase(Ease.OutCubic)
                .OnComplete(() =>
                {
                    canvasGroup.blocksRaycasts = true;
                    RefreshOwningLayout();
                });
        }

        private void RefreshOwningLayout()
        {
            Canvas.ForceUpdateCanvases();

            Transform current = transform.parent;
            while (current != null)
            {
                RectTransform rectTransform = current as RectTransform;
                if (rectTransform != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

                current = current.parent;
            }

            Canvas.ForceUpdateCanvases();
        }

        private void RefreshLayoutHierarchy(Transform root)
        {
            Canvas.ForceUpdateCanvases();

            Transform current = root;
            while (current != null)
            {
                RectTransform rectTransform = current as RectTransform;
                if (rectTransform != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

                current = current.parent;
            }

            Canvas.ForceUpdateCanvases();
        }

        public void EnterTargetingMode()
        {
            if (!m_IsDragging || m_IsTargetingMode)
                return;

            m_IsTargetingMode = true;
            ApplyHoverGlow(false);
            UpdateRenderPriority();

            if (m_OriginalParent != null)
            {
                transform.SetParent(m_OriginalParent, true);
                transform.SetSiblingIndex(m_OriginalSiblingIndex);
                RefreshLayoutHierarchy(m_OriginalParent);
            }

            m_MoveTween?.Kill();
            m_MoveTween = m_RectTransform.DOMove(m_OriginalPosition, animationDuration)
                .SetEase(Ease.OutCubic);

            ScaleTo(hoverScale);
        }

        public void ExitTargetingMode(Vector2 screenPosition)
        {
            if (!m_IsDragging || !m_IsTargetingMode)
                return;

            m_IsTargetingMode = false;
            ApplyHoverGlow(false);
            UpdateRenderPriority();

            if (m_Canvas != null)
            {
                transform.SetParent(m_Canvas.transform, true);
                transform.SetAsLastSibling();
            }

            if (m_OriginalParent != null)
            {
                RefreshLayoutHierarchy(m_OriginalParent);
            }

            m_MoveTween?.Kill();
            m_RectTransform.position = screenPosition;
            ScaleTo(dragScale);
        }

        public Vector2 GetScreenAnchorPosition(Camera uiCamera)
        {
            if (m_RectTransform == null)
                return Vector2.zero;

            return RectTransformUtility.WorldToScreenPoint(uiCamera, m_RectTransform.position);
        }

        /// <summary>
        /// 丢弃成功回调（在拖拽过程中被丢弃）
        /// </summary>
        public void OnDiscardSuccess()
        {
            // 立即停止拖拽状态
            m_IsDragging = false;
            m_IsTargetingMode = false;
            m_IsPointerInside = false;
            canvasGroup.blocksRaycasts = false; // 禁用交互，防止再次拖拽
            ApplyHoverGlow(false);
            UpdateRenderPriority();
            
            // 恢复父级（避免卡在 Canvas 顶层）
            if (m_OriginalParent != null)
            {
                transform.SetParent(m_OriginalParent);
            }
            
            // 播放消失动画（缩放）
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

        /// <summary>
        /// 打出成功回调（卡牌成功放置到场景）
        /// </summary>
        public void OnPlaySuccess()
        {
            // 立即停止拖拽状态
            m_IsDragging = false;
            m_IsTargetingMode = false;
            m_IsPointerInside = false;
            canvasGroup.blocksRaycasts = false; // 禁用交互
            ApplyHoverGlow(false);
            UpdateRenderPriority();
            
            // 恢复父级（避免卡在 Canvas 顶层）
            if (m_OriginalParent != null)
            {
                transform.SetParent(m_OriginalParent);
            }
            
            // 播放消失动画（缩放 + 淡出）
            m_ScaleTween?.Kill();
            m_ScaleTween = transform.DOScale(Vector3.zero, 0.2f)
                .SetEase(Ease.InBack);
            
            // 淡出效果
            if (canvasGroup != null)
            {
                canvasGroup.DOFade(0f, 0.2f);
            }
            
            Log.Info($"[HandCardItem] ✅ Card play animation started: {m_CardModel?.GetCardName()}");
        }

        private void OnDestroy()
        {
            ApplyHoverGlow(false);
            m_ScaleTween?.Kill();
            m_MoveTween?.Kill();
        }
    }
}
