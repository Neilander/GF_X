using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;
using System;
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
        [SerializeField] [InspectorName("抽牌起始缩放")] [Range(0.05f, 1f)] private float drawStartScale = 0.18f;
        [SerializeField] [InspectorName("丢弃放大倍率")] private float discardPeakScale = 1.25f;
        [SerializeField] [InspectorName("丢弃放大时长")] private float discardPopDuration = 0.08f;
        [SerializeField] [InspectorName("丢弃缩小时长")] private float discardShrinkDuration = 0.18f;

        [Header("悬停效果")]
        [SerializeField] [InspectorName("悬停放大倍率")] private float hoverScale = 1.15f;
        [SerializeField] [InspectorName("悬停自动上移")] private bool hoverAutoLift = true;
        [SerializeField] [InspectorName("悬停上移额外偏移")] private float hoverExtraLift = 8f;
        [SerializeField] [InspectorName("悬停上移补偿倍率")] private float hoverLiftMultiplier = 1f;
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
        private GameObject m_LayoutPlaceholder;

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
        private Tweener m_MoveTween;
        private Action m_OnMoveToHandComplete;
        private Tween m_HoverLiftTween;
        private Tween m_FadeTween;
        private bool m_HoverLiftActive;
        private Vector2 m_HoverLiftBaseAnchoredPosition;

        private const int NormalSortingOrder = 0;
        private const int HoverSortingOrder = 20;
        private const int DragSortingOrder = 40;
        private const float DisabledCardAlpha = 0.5f;

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
            m_HoverLiftTween?.Kill();
            m_FadeTween?.Kill();
            ClearLayoutPlaceholder();

            m_IsDragging = false;
            m_CanPlay = false;
            m_IsSelected = false;
            m_IsTargetingMode = false;
            m_IsPointerInside = false;

            if (m_RectTransform != null)
            {
                ResetHoverLiftImmediate();
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
                float targetScale = shouldHighlight ? hoverScale : 1f;
                ScaleTo(targetScale);
                ApplyHoverLift(shouldHighlight, targetScale);
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
            RestorePlayableAlpha();

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
                ApplyHoverLift(false, 1f);
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
            ResetHoverLiftImmediate();
            m_OriginalPosition = m_RectTransform.position;
            m_OriginalParent = transform.parent;
            m_OriginalSiblingIndex = transform.GetSiblingIndex();
            CreateLayoutPlaceholder();

            // 移到Canvas顶层
            transform.SetParent(m_Canvas.transform);
            transform.SetAsLastSibling();

            SetTrashHoverTransparency(false, 255);
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
                // 已由父界面处理：可能是打出、丢弃，也可能是拖回手牌区取消。
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
            ApplyHoverLift(false, 1f);
            ApplyHoverGlow(false);
            RestorePlayableAlpha();
            UpdateRenderPriority();

            if (m_LayoutPlaceholder != null)
            {
                m_MoveTween?.Kill();
                m_MoveTween = m_RectTransform.DOMove(GetHandSlotWorldPosition(), 0.3f)
                    .SetEase(Ease.OutBack)
                    .SetLink(gameObject)
                    .OnComplete(RestoreToHandLayoutImmediately);

                ScaleTo(1f);
                return;
            }

            if (m_OriginalParent != null)
            {
                transform.SetParent(m_OriginalParent, true);
                transform.SetSiblingIndex(m_OriginalSiblingIndex);
                RefreshLayoutHierarchy(m_OriginalParent);
            }

            m_MoveTween?.Kill();
            m_MoveTween = m_RectTransform.DOMove(m_OriginalPosition, 0.3f)
                .SetEase(Ease.OutBack)
                .SetLink(gameObject);
            
            ScaleTo(1f);
        }

        public void RestoreToHandLayoutImmediately()
        {
            m_IsDragging = false;
            m_IsTargetingMode = false;
            m_IsPointerInside = false;

            m_MoveTween?.Kill();
            m_ScaleTween?.Kill();
            ResetHoverLiftImmediate();

            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = true;
                canvasGroup.interactable = true;
            }

            ApplyHoverGlow(false);
            RestorePlayableAlpha();

            if (m_OriginalParent != null)
            {
                transform.SetParent(m_OriginalParent, false);
                transform.SetSiblingIndex(GetRestoreSiblingIndex());
            }

            ClearLayoutPlaceholder();

            transform.localScale = Vector3.one;
            UpdateRenderPriority();

            if (m_OriginalParent != null)
            {
                RefreshLayoutHierarchy(m_OriginalParent);
            }
        }

        /// <summary>
        /// 缩放动画
        /// </summary>
        private void ScaleTo(float scale)
        {
            m_ScaleTween?.Kill();
            m_ScaleTween = transform.DOScale(Vector3.one * scale, animationDuration)
                .SetEase(Ease.OutBack)
                .SetLink(gameObject);
        }

        private void ApplyHoverLift(bool enabled, float scale)
        {
            if (m_RectTransform == null)
            {
                return;
            }

            if (!hoverAutoLift || scale <= 1f)
            {
                enabled = false;
            }

            if (enabled)
            {
                if (!m_HoverLiftActive)
                {
                    m_HoverLiftBaseAnchoredPosition = m_RectTransform.anchoredPosition;
                    m_HoverLiftActive = true;
                }

                Vector2 targetPosition = m_HoverLiftBaseAnchoredPosition + Vector2.up * CalculateHoverLift(scale);
                MoveHoverLiftTo(targetPosition, Ease.OutBack);
                return;
            }

            if (!m_HoverLiftActive)
            {
                return;
            }

            Vector2 basePosition = m_HoverLiftBaseAnchoredPosition;
            m_HoverLiftTween?.Kill();
            m_HoverLiftTween = m_RectTransform.DOAnchorPos(basePosition, animationDuration)
                .SetEase(Ease.OutCubic)
                .SetLink(gameObject)
                .OnComplete(() => m_HoverLiftActive = false);
        }

        private float CalculateHoverLift(float scale)
        {
            float extraHeight = m_RectTransform.rect.height * Mathf.Max(0f, scale - 1f);
            float lift = extraHeight * 0.5f + Mathf.Max(0f, hoverExtraLift);
            return Mathf.Max(0f, lift * Mathf.Max(0f, hoverLiftMultiplier));
        }

        private void MoveHoverLiftTo(Vector2 targetPosition, Ease ease)
        {
            m_HoverLiftTween?.Kill();
            m_HoverLiftTween = m_RectTransform.DOAnchorPos(targetPosition, animationDuration)
                .SetEase(ease)
                .SetLink(gameObject);
        }

        private Vector3 PrepareHoverLiftedWorldTarget(float scale, Vector3 baseWorldTarget)
        {
            if (m_RectTransform == null || !hoverAutoLift || scale <= 1f)
            {
                m_HoverLiftActive = false;
                return baseWorldTarget;
            }

            m_HoverLiftTween?.Kill();

            Vector2 baseAnchoredPosition = m_RectTransform.anchoredPosition;
            Vector3 baseWorldPosition = m_RectTransform.position;
            Vector2 liftedAnchoredPosition = baseAnchoredPosition + Vector2.up * CalculateHoverLift(scale);

            m_RectTransform.anchoredPosition = liftedAnchoredPosition;
            Vector3 liftedWorldPosition = m_RectTransform.position;
            m_RectTransform.anchoredPosition = baseAnchoredPosition;

            m_HoverLiftBaseAnchoredPosition = baseAnchoredPosition;
            m_HoverLiftActive = true;

            return baseWorldTarget + (liftedWorldPosition - baseWorldPosition);
        }

        private void ResetHoverLiftImmediate()
        {
            m_HoverLiftTween?.Kill();

            if (m_RectTransform == null || !m_HoverLiftActive)
            {
                m_HoverLiftActive = false;
                return;
            }

            m_RectTransform.anchoredPosition = m_HoverLiftBaseAnchoredPosition;
            m_HoverLiftActive = false;
        }

        public void SetTrashHoverTransparency(bool isOverTrash, int alpha255)
        {
            if (canvasGroup == null)
            {
                return;
            }

            if (isOverTrash)
            {
                canvasGroup.alpha = Mathf.Clamp(alpha255, 0, 255) / 255f;
                return;
            }

            RestorePlayableAlpha();
        }

        private void RestorePlayableAlpha()
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = m_CanPlay ? 1f : DisabledCardAlpha;
        }

        /// <summary>
        /// 从屏幕位置移动到手牌区
        /// </summary>
        public void MoveToHandFromScreenPosition(Vector2 startScreenPosition, float duration = 0.5f, Action onComplete = null)
        {
            canvasGroup.blocksRaycasts = false;
            RefreshOwningLayout();

            Vector3 targetPosition = m_RectTransform.position;
            m_RectTransform.position = startScreenPosition;
            transform.localScale = Vector3.one * Mathf.Clamp(drawStartScale, 0.05f, 1f);

            m_OnMoveToHandComplete = onComplete;
            m_MoveTween?.Kill();
            m_MoveTween = m_RectTransform.DOMove(targetPosition, duration)
                .SetEase(Ease.OutCubic)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    transform.localScale = Vector3.one;
                    canvasGroup.blocksRaycasts = true;
                    if (m_OnMoveToHandComplete != null)
                    {
                        m_OnMoveToHandComplete.Invoke();
                        m_OnMoveToHandComplete = null;
                    }
                    RefreshOwningLayout();
                });

            m_ScaleTween?.Kill();
            m_ScaleTween = transform.DOScale(Vector3.one, duration)
                .SetEase(Ease.OutBack)
                .SetLink(gameObject);
        }

        public void SyncMoveTargetToCurrentLayout()
        {
            if (m_RectTransform == null || m_MoveTween == null || !m_MoveTween.IsActive())
            {
                return;
            }

            Vector3 newTargetPosition = m_RectTransform.position;
            m_RectTransform.position = m_SavedTweenPosition;

            float remainTime = m_MoveTween.Duration() - m_MoveTween.Elapsed();
            remainTime = Mathf.Max(0.05f, remainTime);

            m_MoveTween.Kill();
            m_MoveTween = m_RectTransform.DOMove(newTargetPosition, remainTime)
                .SetEase(Ease.OutCubic)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    transform.localScale = Vector3.one;
                    canvasGroup.blocksRaycasts = true;
                    if (m_OnMoveToHandComplete != null)
                    {
                        m_OnMoveToHandComplete.Invoke();
                        m_OnMoveToHandComplete = null;
                    }
                    RefreshOwningLayout();
                });
        }

        private Vector3 m_SavedTweenPosition;

        public void SaveCurrentPosition()
        {
            if (m_RectTransform != null && m_MoveTween != null && m_MoveTween.IsActive())
            {
                m_SavedTweenPosition = m_RectTransform.position;
            }
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

        private void CreateLayoutPlaceholder()
        {
            ClearLayoutPlaceholder();

            if (m_OriginalParent == null || m_RectTransform == null)
            {
                return;
            }

            GameObject placeholder = new GameObject($"{name}_LayoutPlaceholder", typeof(RectTransform), typeof(LayoutElement));
            placeholder.hideFlags = HideFlags.DontSave;
            placeholder.transform.SetParent(m_OriginalParent, false);
            placeholder.transform.SetSiblingIndex(m_OriginalSiblingIndex);

            RectTransform placeholderRect = placeholder.GetComponent<RectTransform>();
            placeholderRect.anchorMin = m_RectTransform.anchorMin;
            placeholderRect.anchorMax = m_RectTransform.anchorMax;
            placeholderRect.pivot = m_RectTransform.pivot;
            placeholderRect.localScale = Vector3.one;

            Vector2 cardSize = m_RectTransform.rect.size;
            if (cardSize.x <= 0f || cardSize.y <= 0f)
            {
                cardSize = m_RectTransform.sizeDelta;
            }

            cardSize.x = Mathf.Max(1f, cardSize.x);
            cardSize.y = Mathf.Max(1f, cardSize.y);
            placeholderRect.sizeDelta = cardSize;

            LayoutElement layoutElement = placeholder.GetComponent<LayoutElement>();
            layoutElement.minWidth = cardSize.x;
            layoutElement.minHeight = cardSize.y;
            layoutElement.preferredWidth = cardSize.x;
            layoutElement.preferredHeight = cardSize.y;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;

            m_LayoutPlaceholder = placeholder;
            RefreshLayoutHierarchy(m_OriginalParent);
        }

        private void ClearLayoutPlaceholder()
        {
            if (m_LayoutPlaceholder == null)
            {
                return;
            }

            LayoutElement layoutElement = m_LayoutPlaceholder.GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                layoutElement.ignoreLayout = true;
            }

            m_LayoutPlaceholder.SetActive(false);
            Destroy(m_LayoutPlaceholder);
            m_LayoutPlaceholder = null;
        }

        private int GetRestoreSiblingIndex()
        {
            if (m_LayoutPlaceholder != null && m_OriginalParent != null && m_LayoutPlaceholder.transform.parent == m_OriginalParent)
            {
                return m_LayoutPlaceholder.transform.GetSiblingIndex();
            }

            return m_OriginalSiblingIndex;
        }

        private Vector3 GetHandSlotWorldPosition()
        {
            if (m_LayoutPlaceholder != null)
            {
                RectTransform placeholderRect = m_LayoutPlaceholder.transform as RectTransform;
                if (placeholderRect != null)
                {
                    return placeholderRect.position;
                }
            }

            return m_OriginalPosition;
        }

        public void EnterTargetingMode()
        {
            if (!m_IsDragging || m_IsTargetingMode)
                return;

            m_IsTargetingMode = true;
            ResetHoverLiftImmediate();
            ApplyHoverGlow(false);
            UpdateRenderPriority();

            if (m_LayoutPlaceholder != null)
            {
                if (m_Canvas != null && transform.parent != m_Canvas.transform)
                {
                    transform.SetParent(m_Canvas.transform, true);
                }

                transform.SetAsLastSibling();
                RefreshLayoutHierarchy(m_OriginalParent);
            }
            else if (m_OriginalParent != null)
            {
                transform.SetParent(m_OriginalParent, true);
                transform.SetSiblingIndex(m_OriginalSiblingIndex);
                RefreshLayoutHierarchy(m_OriginalParent);
            }

            m_MoveTween?.Kill();
            Vector3 targetPosition = PrepareHoverLiftedWorldTarget(hoverScale, GetHandSlotWorldPosition());
            m_MoveTween = m_RectTransform.DOMove(targetPosition, animationDuration)
                .SetEase(Ease.OutCubic)
                .SetLink(gameObject);

            ScaleTo(hoverScale);
        }

        public void ExitTargetingMode(Vector2 screenPosition)
        {
            if (!m_IsDragging || !m_IsTargetingMode)
                return;

            m_IsTargetingMode = false;
            ResetHoverLiftImmediate();
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
        public void OnDiscardSuccess(System.Action onComplete = null)
        {
            // 立即停止拖拽状态
            m_IsDragging = false;
            m_IsTargetingMode = false;
            m_IsPointerInside = false;
            ResetHoverLiftImmediate();
            canvasGroup.blocksRaycasts = false; // 禁用交互，防止再次拖拽
            ApplyHoverGlow(false);
            UpdateRenderPriority();

            ClearLayoutPlaceholder();

            float safePopDuration = Mathf.Max(0.01f, discardPopDuration);
            float safeShrinkDuration = Mathf.Max(0.01f, discardShrinkDuration);
            float safePeakScale = Mathf.Max(1f, discardPeakScale);

            // 播放中心缩放消失动画：先略微放大，再缩小到 0。
            m_ScaleTween?.Kill();
            m_ScaleTween = DOTween.Sequence()
                .Append(transform.DOScale(Vector3.one * safePeakScale, safePopDuration).SetEase(Ease.OutCubic))
                .Append(transform.DOScale(Vector3.zero, safeShrinkDuration).SetEase(Ease.InBack))
                .SetLink(gameObject)
                .OnComplete(() => onComplete?.Invoke());

            // 淡出效果
            if (canvasGroup != null)
            {
                m_FadeTween?.Kill();
                m_FadeTween = canvasGroup.DOFade(0f, safePopDuration + safeShrinkDuration).SetLink(gameObject);
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
            ResetHoverLiftImmediate();
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
                .SetEase(Ease.InBack)
                .SetLink(gameObject);
            
            // 淡出效果
            if (canvasGroup != null)
            {
                m_FadeTween?.Kill();
                m_FadeTween = canvasGroup.DOFade(0f, 0.2f).SetLink(gameObject);
            }
            
            Log.Info($"[HandCardItem] ✅ Card play animation started: {m_CardModel?.GetCardName()}");
        }

        public void PrepareForRecycle()
        {
            m_ScaleTween?.Kill();
            m_MoveTween?.Kill();
            m_HoverLiftTween?.Kill();
            m_FadeTween?.Kill();
            ClearLayoutPlaceholder();
            ApplyHoverGlow(false);
        }

        private void OnDestroy()
        {
            ApplyHoverGlow(false);
            m_ScaleTween?.Kill();
            m_MoveTween?.Kill();
            m_HoverLiftTween?.Kill();
            m_FadeTween?.Kill();
            ClearLayoutPlaceholder();
        }
    }
}
