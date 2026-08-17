using System;
using System.Collections.Generic;
using GameFramework;
using GameFramework.Event;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using CardFx = AAAGame.Card.UI;

namespace AAAGame.Card
{
    /// <summary>
    /// GF_X 手牌界面。
    /// 负责手牌列表、拖拽放置、拖拽目标模式和场景预览反馈。
    /// </summary>
    public partial class CardUIForm : UIFormBase
    {
        [Header("UI容器")]
        private const int DeferredHandLayoutRefreshFrameCount = 4;

        [SerializeField] private Transform handCardContainer;
        [SerializeField] private RectTransform handCardArea;
        [SerializeField] private GameObject trashBin;
        [SerializeField] private TextMeshProUGUI trashBinHintText;

        [Header("垃圾桶拖拽反馈")]
        [SerializeField][InspectorName("垃圾桶关闭纹理")] private Sprite trashBinClosedSprite;
        [SerializeField][InspectorName("垃圾桶打开纹理")] private Sprite trashBinOpenSprite;
        [SerializeField][InspectorName("拖到垃圾桶卡牌透明度(0-255)")][Range(0, 255)] private int trashHoverCardAlpha = 200;

        [Header("预制体")]
        [SerializeField] private GameObject handCardItemPrefab;

        [Header("场景放置反馈")]
        [SerializeField] private CardFx.CardAreaMaterialOverlay areaMaterialOverlay;

        [Header("抽卡动画")]
        [SerializeField] private RectTransform cardDeckTransform;
        [SerializeField] private float cardMoveToHandDuration = 0.5f;

        [Header("面板动画")]
        [SerializeField] private float panelSlideDuration = 0.28f;
        [SerializeField] [Range(0.5f, 3f)] private float panelOpenDurationMultiplier = 1.45f;
        [SerializeField] private float panelSlideOffset = 260f;

        [Header("卡组预览")]
        [SerializeField][InspectorName("悬浮抽卡点显示卡组")] private bool showDeckPreviewOnHover = true;
        [SerializeField][InspectorName("卡组预览面板")] private RectTransform deckPreviewPanel;
        [SerializeField][InspectorName("卡组预览内容容器")] private RectTransform deckPreviewContent;
        [SerializeField][InspectorName("卡组预览标题文本")] private TextMeshProUGUI deckPreviewTitleText;
        [SerializeField][InspectorName("卡组为空提示文本")] private TextMeshProUGUI deckPreviewEmptyText;
        [SerializeField][InspectorName("卡组预览卡牌条目模板")] private CardDeckPreviewItem deckPreviewItemTemplate;

        [Header("目标拖拽表现")]
        [SerializeField][InspectorName("准星图片")] private Sprite targetingReticleSprite;
        [SerializeField][InspectorName("准星尺寸")] private Vector2 targetingReticleSize = new Vector2(72f, 72f);
        [SerializeField][InspectorName("连线颜色")] private Color targetingCurveColor = new Color(0.6f, 1f, 0.75f, 0.92f);
        [SerializeField][InspectorName("连线粗细")] private float targetingCurveThickness = 14f;
        [SerializeField][InspectorName("连线弯曲高度")] private float targetingCurveHeight = 120f;
        [SerializeField][InspectorName("连线分段数")][Range(4, 64)] private int targetingCurveSegments = 24;

        private readonly List<UIItemObject> m_HandCardItemObjects = new List<UIItemObject>();
        private readonly List<Vector3> m_PreviewSpawnPositions = new List<Vector3>();
        private readonly Queue<GameObject> m_PlayedCardSlotPlaceholders = new Queue<GameObject>();
        private readonly InputAction[] m_CardHotkeyActions = new InputAction[4];

        private CardSystemController m_CardSystemController;
        private HandCardItem m_DraggingCard;
        private RectTransform m_TrashBinRect;
        private RectTransform m_ResolvedHandCardAreaRect;
        private Canvas m_TrashBinCanvas;
        private Image m_TrashBinImage;
        private Sprite m_DefaultTrashBinSprite;
        private bool m_IsTrashBinOpen;
        private int m_PendingHandLayoutRefreshFrames;
        private Canvas m_FormCanvas;
        private RectTransform m_FormRectTransform;
        private Camera m_UICamera;

        private CardFx.CardTargetingCurveGraphic m_TargetingCurveGraphic;
        private RectTransform m_TargetingReticleRect;
        private Image m_TargetingReticleImage;
        private Sprite m_RuntimeFallbackReticleSprite;
        private int m_ActiveDrawAnimations;
        private Tweener m_PanelTween;
        private Vector2 m_PanelVisibleAnchoredPosition;
        private Vector2 m_PanelHiddenAnchoredPosition;
        private bool m_IsPanelOpening;
        private bool m_IsPanelReady;
        private bool m_IsPanelClosing;

        private readonly List<CardSystemController.DeckPreviewCard> m_DeckPreviewCards = new List<CardSystemController.DeckPreviewCard>();
        private int m_LastDeckPreviewHash = int.MinValue;
        private bool m_DeckPreviewConfigWarningLogged;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            m_FormCanvas = GetComponent<Canvas>();
            m_FormRectTransform = transform as RectTransform;
            m_UICamera = ResolveCanvasCamera();
            CachePanelPositions();
            SetPanelHiddenImmediate();

            ResolveAreaMaterialOverlay();
            EnsureTargetingVisuals();
            HideTargetingVisuals();

            if (trashBin != null)
            {
                m_TrashBinRect = trashBin.GetComponent<RectTransform>();
                m_TrashBinCanvas = m_TrashBinRect != null ? m_TrashBinRect.GetComponentInParent<Canvas>() : null;
                ResolveTrashBinImage();
                SetTrashBinOpen(false, true);
                trashBin.SetActive(true);
            }

            m_ResolvedHandCardAreaRect = ResolveHandCardAreaRect();
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            InitializeCardHotkeys();

            GF.Event.Subscribe(CardDrawnEventArgs.EventId, OnCardDrawn);
            GF.Event.Subscribe(CardPlayedEventArgs.EventId, OnCardPlayed);
            GF.Event.Subscribe(CardDiscardedEventArgs.EventId, OnCardDiscarded);
            GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
            GF.Event.Subscribe(ArmyBuildingCardPropertyChangedEventArgs.EventId, OnArmyBuildingCardPropertyChanged);

            UIParams uiParams = userData as UIParams;
            if (uiParams != null)
            {
                m_CardSystemController = uiParams.Get("CardSystemController") as CardSystemController;
            }

            if (m_CardSystemController == null)
            {
                Log.Error("CardSystemController is null.");
                return;
            }

            StartOpenPanelAnimation();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            m_IsPanelClosing = false;
            m_IsPanelOpening = false;
            m_IsPanelReady = false;

            if (!isShutdown)
            {
                m_PanelTween?.Kill();
                m_PanelTween = null;
            }

            if (GF.Event != null)
            {
                try
                {
                    GF.Event.Unsubscribe(CardDrawnEventArgs.EventId, OnCardDrawn);
                    GF.Event.Unsubscribe(CardPlayedEventArgs.EventId, OnCardPlayed);
                    GF.Event.Unsubscribe(CardDiscardedEventArgs.EventId, OnCardDiscarded);
                    GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
                    GF.Event.Unsubscribe(ArmyBuildingCardPropertyChangedEventArgs.EventId, OnArmyBuildingCardPropertyChanged);
                }
                catch (Exception ex)
                {
                    Log.Info("CardUIForm unsubscribe error: " + ex.Message);
                }
            }

            base.OnClose(isShutdown, userData);

            if (!isShutdown)
            {
                ClearHandCards();
            }
            HideTargetingVisuals();
            SetTrashBinDragFeedback(null, false);
            HideDeckPreviewPanel();
            areaMaterialOverlay?.HideAreaEffect();
        }

        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);

            HandleHotkeys();
            RefreshPendingHandLayout();
            UpdateDeckPreviewHover();
        }

        private void RefreshHandCardLayout()
        {
            for (int i = 0; i < m_HandCardItemObjects.Count; i++)
            {
                UIItemObject itemObject = m_HandCardItemObjects[i];
                if (itemObject != null && itemObject.gameObject != null)
                {
                    HandCardItem cardItem = itemObject.gameObject.GetComponent<HandCardItem>();
                    cardItem?.SaveCurrentPosition();
                }
            }

            Canvas.ForceUpdateCanvases();

            RectTransform containerRect = handCardContainer as RectTransform;
            if (containerRect != null)
            {
                LayoutRebuilder.MarkLayoutForRebuild(containerRect);
                LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);

                ILayoutController[] layoutControllers = containerRect.GetComponents<ILayoutController>();
                for (int i = 0; i < layoutControllers.Length; i++)
                {
                    layoutControllers[i].SetLayoutHorizontal();
                    layoutControllers[i].SetLayoutVertical();
                }
            }

            if (handCardArea != null)
            {
                LayoutRebuilder.MarkLayoutForRebuild(handCardArea);
                LayoutRebuilder.ForceRebuildLayoutImmediate(handCardArea);
            }

            if (containerRect != null && containerRect.parent is RectTransform parentRect)
            {
                LayoutRebuilder.MarkLayoutForRebuild(parentRect);
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
            }

            Canvas.ForceUpdateCanvases();

            SyncMovingCardTargets();
        }

        public bool IsReadyForAutoDraw => m_IsPanelReady && !m_IsPanelOpening && !m_IsPanelClosing;

        public void CloseCardPanelWithAnimation()
        {
            StartClosePanelAnimation();
        }

        private void SyncMovingCardTargets()
        {
            for (int i = 0; i < m_HandCardItemObjects.Count; i++)
            {
                UIItemObject itemObject = m_HandCardItemObjects[i];
                if (itemObject == null || itemObject.gameObject == null)
                {
                    continue;
                }

                HandCardItem cardItem = itemObject.gameObject.GetComponent<HandCardItem>();
                cardItem?.SyncMoveTargetToCurrentLayout();
            }
        }

        private void CachePanelPositions()
        {
            if (m_FormRectTransform == null)
            {
                return;
            }

            m_PanelVisibleAnchoredPosition = m_FormRectTransform.anchoredPosition;
            m_PanelHiddenAnchoredPosition = m_PanelVisibleAnchoredPosition + new Vector2(0f, -Mathf.Abs(panelSlideOffset));
        }

        private void SetPanelHiddenImmediate()
        {
            if (m_FormRectTransform == null)
            {
                return;
            }

            m_FormRectTransform.anchoredPosition = m_PanelHiddenAnchoredPosition;
        }

        private void StartOpenPanelAnimation()
        {
            if (m_FormRectTransform == null)
            {
                RefreshHandCards();
                m_IsPanelReady = true;
                return;
            }

            m_PanelTween?.Kill();
            m_IsPanelOpening = true;
            m_IsPanelClosing = false;
            m_IsPanelReady = false;
            Interactable = false;
            float openDuration = panelSlideDuration * Mathf.Max(0.5f, panelOpenDurationMultiplier);

            if (openDuration <= 0f)
            {
                m_FormRectTransform.anchoredPosition = m_PanelVisibleAnchoredPosition;
                OnPanelOpenAnimationComplete();
                return;
            }

            m_FormRectTransform.anchoredPosition = m_PanelHiddenAnchoredPosition;
            m_PanelTween = m_FormRectTransform.DOAnchorPos(m_PanelVisibleAnchoredPosition, openDuration)
                .SetEase(Ease.OutCubic)
                .SetLink(gameObject)
                .OnComplete(OnPanelOpenAnimationComplete);
        }

        private void StartClosePanelAnimation()
        {
            if (m_FormRectTransform == null)
            {
                CloseUIImmediately();
                return;
            }

            m_PanelTween?.Kill();
            m_IsPanelOpening = false;
            m_IsPanelReady = false;
            m_IsPanelClosing = true;
            Interactable = false;

            if (panelSlideDuration <= 0f)
            {
                m_FormRectTransform.anchoredPosition = m_PanelHiddenAnchoredPosition;
                CloseUIImmediately();
                return;
            }

            m_PanelTween = m_FormRectTransform.DOAnchorPos(m_PanelHiddenAnchoredPosition, panelSlideDuration)
                .SetEase(Ease.InCubic)
                .SetLink(gameObject)
                .OnComplete(CloseUIImmediately);
        }

        private void OnPanelOpenAnimationComplete()
        {
            m_IsPanelOpening = false;
            RefreshHandCards();
            m_IsPanelReady = true;
            Interactable = true;
        }

        private void CloseUIImmediately()
        {
            if (m_CardSystemController != null)
            {
                m_CardSystemController = null;
            }

            GF.UI.CloseUIForm(this.UIForm);
        }

        public override void OnClickClose()
        {
            StartClosePanelAnimation();
        }

        private bool CreateHandCardItem(
            CardModel cardModel,
            bool playAnimation = true,
            GameObject targetSlotPlaceholder = null)
        {
            if (handCardItemPrefab == null || handCardContainer == null)
            {
                Log.Error("HandCardItemPrefab or HandCardContainer is null.");
                return false;
            }

            int insertionIndex = 0;
            int siblingIndex = 0;
            if (targetSlotPlaceholder != null)
            {
                if (targetSlotPlaceholder.transform.parent != handCardContainer)
                {
                    throw new InvalidOperationException(
                        $"Played card slot '{targetSlotPlaceholder.name}' is no longer under the hand card container.");
                }

                siblingIndex = targetSlotPlaceholder.transform.GetSiblingIndex();
                insertionIndex = CountCardItemsBeforeSibling(siblingIndex);
            }

            UIItemObject itemObject = SpawnItem<UIItemObject>(handCardItemPrefab, handCardContainer);
            if (itemObject == null)
            {
                Log.Error("Failed to spawn HandCardItem from object pool.");
                return false;
            }

            HandCardItem cardItem = itemObject.gameObject.GetComponent<HandCardItem>();
            if (cardItem == null)
            {
                Log.Error("HandCardItem component not found on spawned object.");
                return false;
            }

            cardItem.ApplyRenderLayer(gameObject.layer);

            RectTransform itemRectTransform = itemObject.gameObject.GetComponent<RectTransform>();
            if (itemRectTransform != null)
            {
                itemRectTransform.SetParent(handCardContainer, false);
                itemRectTransform.localScale = Vector3.one;
                itemRectTransform.SetSiblingIndex(siblingIndex);
            }

            cardItem.Initialize(cardModel, this);
            m_HandCardItemObjects.Insert(insertionIndex, itemObject);
            if (targetSlotPlaceholder != null)
            {
                ReleasePlayedCardSlot(targetSlotPlaceholder);
            }
            RefreshHandCardLayout();

            if (playAnimation && cardDeckTransform != null)
            {
                m_ActiveDrawAnimations++;
                cardItem.MoveToHandFromWorldPosition(cardDeckTransform.position, cardMoveToHandDuration, () =>
                {
                    m_ActiveDrawAnimations = Mathf.Max(0, m_ActiveDrawAnimations - 1);
                });
            }

            Log.Info(
                "[CardDraw] Created card runtimeId={0}, insertionIndex={1}, siblingIndex={2}, reusedPlayedSlot={3}.",
                cardModel.RuntimeId,
                insertionIndex,
                siblingIndex,
                targetSlotPlaceholder != null);
            return true;
        }

        private int CountCardItemsBeforeSibling(int siblingIndex)
        {
            int count = 0;
            for (int i = 0; i < m_HandCardItemObjects.Count; i++)
            {
                UIItemObject itemObject = m_HandCardItemObjects[i];
                if (itemObject == null || itemObject.gameObject == null)
                {
                    throw new InvalidOperationException($"Hand card item {i} is null while resolving a played card slot.");
                }

                if (itemObject.gameObject.transform.GetSiblingIndex() < siblingIndex)
                {
                    count++;
                }
            }

            return count;
        }

        private GameObject ReservePlayedCardSlot(HandCardItem cardItem)
        {
            if (cardItem == null)
            {
                throw new ArgumentNullException(nameof(cardItem));
            }

            GameObject placeholder = cardItem.DetachLayoutPlaceholderForReplacement();
            if (placeholder == null)
            {
                RectTransform cardRect = cardItem.transform as RectTransform;
                if (cardRect == null || cardRect.parent != handCardContainer)
                {
                    throw new InvalidOperationException(
                        $"Card '{cardItem.name}' has neither a drag placeholder nor a hand-container transform.");
                }

                placeholder = CreatePlayedCardSlotPlaceholder(cardRect);
            }

            placeholder.name = $"{cardItem.name}_PlayedCardSlot";
            m_PlayedCardSlotPlaceholders.Enqueue(placeholder);
            Log.Info(
                "[CardDraw] Reserved played slot runtimeId={0}, siblingIndex={1}, worldPosition={2}.",
                cardItem.GetCardModel().RuntimeId,
                placeholder.transform.GetSiblingIndex(),
                placeholder.transform.position);
            return placeholder;
        }

        private GameObject CreatePlayedCardSlotPlaceholder(RectTransform cardRect)
        {
            GameObject placeholder = new GameObject("PlayedCardSlot", typeof(RectTransform), typeof(LayoutElement));
            placeholder.hideFlags = HideFlags.DontSave;
            placeholder.transform.SetParent(handCardContainer, false);
            placeholder.transform.SetSiblingIndex(cardRect.GetSiblingIndex());

            RectTransform placeholderRect = (RectTransform)placeholder.transform;
            placeholderRect.anchorMin = cardRect.anchorMin;
            placeholderRect.anchorMax = cardRect.anchorMax;
            placeholderRect.pivot = cardRect.pivot;
            placeholderRect.localScale = Vector3.one;

            Vector2 cardSize = cardRect.rect.size;
            if (cardSize.x <= 0f || cardSize.y <= 0f)
            {
                cardSize = cardRect.sizeDelta;
            }

            placeholderRect.sizeDelta = cardSize;

            LayoutElement layoutElement = placeholder.GetComponent<LayoutElement>();
            layoutElement.minWidth = Mathf.Max(1f, cardSize.x);
            layoutElement.minHeight = Mathf.Max(1f, cardSize.y);
            layoutElement.preferredWidth = layoutElement.minWidth;
            layoutElement.preferredHeight = layoutElement.minHeight;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;
            return placeholder;
        }

        private static void ReleasePlayedCardSlot(GameObject placeholder)
        {
            LayoutElement layoutElement = placeholder.GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                layoutElement.ignoreLayout = true;
            }

            placeholder.SetActive(false);
            Destroy(placeholder);
        }

        private void ClearPlayedCardSlots()
        {
            while (m_PlayedCardSlotPlaceholders.Count > 0)
            {
                GameObject placeholder = m_PlayedCardSlotPlaceholders.Dequeue();
                if (placeholder != null)
                {
                    ReleasePlayedCardSlot(placeholder);
                }
            }
        }

        private void RequestDeferredHandLayoutRefresh(int frameCount = DeferredHandLayoutRefreshFrameCount)
        {
            m_PendingHandLayoutRefreshFrames = Mathf.Max(m_PendingHandLayoutRefreshFrames, frameCount);
        }

        private void RefreshPendingHandLayout()
        {
            if (m_PendingHandLayoutRefreshFrames <= 0)
            {
                return;
            }

            RefreshHandCardLayout();
            m_PendingHandLayoutRefreshFrames--;
        }

        private void UpdateDeckPreviewHover()
        {
            if (!showDeckPreviewOnHover || cardDeckTransform == null || deckPreviewPanel == null || m_DraggingCard != null)
            {
                HideDeckPreviewPanel();
                return;
            }

            Vector2 mousePosition = GetMouseScreenPosition();
            Camera uiCamera = GetUICamera();
            bool isOverDeck = RectTransformUtility.RectangleContainsScreenPoint(cardDeckTransform, mousePosition, uiCamera);
            bool isOverPanel = deckPreviewPanel.gameObject.activeSelf
                && RectTransformUtility.RectangleContainsScreenPoint(deckPreviewPanel, mousePosition, uiCamera);

            if (isOverDeck || isOverPanel)
            {
                ShowDeckPreviewPanel();
                return;
            }

            HideDeckPreviewPanel();
        }

        private static Vector2 GetMouseScreenPosition()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                throw new InvalidOperationException("Card UI requires an active mouse device.");

            return mouse.position.ReadValue();
        }

        private void ShowDeckPreviewPanel()
        {
            if (!ValidateDeckPreviewConfig())
            {
                return;
            }

            deckPreviewPanel.SetAsLastSibling();
            if (!deckPreviewPanel.gameObject.activeSelf)
            {
                deckPreviewPanel.gameObject.SetActive(true);
                m_LastDeckPreviewHash = int.MinValue;
            }

            LoadDeckCardsForPreview();

            int deckHash = CalculateDeckPreviewHash();
            if (deckHash != m_LastDeckPreviewHash)
            {
                RebuildDeckPreviewPanel();
                m_LastDeckPreviewHash = deckHash;
            }
        }

        private void HideDeckPreviewPanel()
        {
            if (deckPreviewPanel != null && deckPreviewPanel.gameObject.activeSelf)
            {
                deckPreviewPanel.gameObject.SetActive(false);
            }
        }

        private bool ValidateDeckPreviewConfig()
        {
            bool valid = deckPreviewPanel != null
                && deckPreviewContent != null
                && deckPreviewItemTemplate != null;

            if (!valid && !m_DeckPreviewConfigWarningLogged)
            {
                Log.Warning("[CardUI] 卡组预览未配置完整。请在 CardUIForm 上绑定：卡组预览面板、内容容器、卡牌条目模板。");
                m_DeckPreviewConfigWarningLogged = true;
            }

            if (valid)
            {
                m_DeckPreviewConfigWarningLogged = false;
            }

            return valid;
        }

        private void LoadDeckCardsForPreview()
        {
            m_DeckPreviewCards.Clear();

            if (m_CardSystemController == null)
            {
                return;
            }

            List<CardSystemController.DeckPreviewCard> deckCards = m_CardSystemController.GetOrderedDeckPreviewCards();
            if (deckCards == null)
            {
                return;
            }

            for (int i = 0; i < deckCards.Count; i++)
            {
                CardSystemController.DeckPreviewCard previewCard = deckCards[i];
                if (previewCard != null && previewCard.CardData != null)
                {
                    m_DeckPreviewCards.Add(previewCard);
                }
            }
        }

        private void RebuildDeckPreviewPanel()
        {
            if (deckPreviewContent == null)
            {
                return;
            }

            ClearDeckPreviewContent();

            int cardCount = m_DeckPreviewCards.Count;
            if (deckPreviewTitleText != null)
            {
                deckPreviewTitleText.text = Utility.Text.Format("卡组  共 {0} 张", cardCount);
            }

            if (deckPreviewEmptyText != null)
            {
                deckPreviewEmptyText.gameObject.SetActive(cardCount <= 0);
                if (cardCount <= 0)
                {
                    deckPreviewEmptyText.text = "当前卡组没有可预览卡牌";
                }
            }

            if (deckPreviewItemTemplate != null)
            {
                deckPreviewItemTemplate.gameObject.SetActive(false);
            }

            for (int i = 0; i < m_DeckPreviewCards.Count; i++)
            {
                CardDeckPreviewItem previewItem = Instantiate(deckPreviewItemTemplate, deckPreviewContent);
                previewItem.name = Utility.Text.Format("DeckPreviewCardItem_{0}", i);
                CardSystemController.DeckPreviewCard previewCard = m_DeckPreviewCards[i];
                previewItem.SetData(previewCard.CardData, previewCard.SourceBuildingInstanceId);
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(deckPreviewContent);
        }

        private void ClearDeckPreviewContent()
        {
            if (deckPreviewContent == null)
            {
                return;
            }

            for (int i = deckPreviewContent.childCount - 1; i >= 0; i--)
            {
                Transform childTransform = deckPreviewContent.GetChild(i);
                if (deckPreviewItemTemplate != null && childTransform == deckPreviewItemTemplate.transform)
                {
                    continue;
                }

                GameObject child = childTransform.gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        private int CalculateDeckPreviewHash()
        {
            unchecked
            {
                int hash = 17;
                int count = m_DeckPreviewCards.Count;
                hash = hash * 31 + count;
                for (int i = 0; i < count; i++)
                {
                    CardSystemController.DeckPreviewCard previewCard = m_DeckPreviewCards[i];
                    ICardDataProvider cardData = previewCard != null ? previewCard.CardData : null;
                    string sourceBuildingInstanceId = previewCard != null
                        ? previewCard.SourceBuildingInstanceId
                        : string.Empty;
                    IBuildingLogicContext sourceBuilding = string.IsNullOrWhiteSpace(sourceBuildingInstanceId)
                        ? null
                        : LogicBuildingQueryService.GetRequiredByInstanceId(sourceBuildingInstanceId);
                    string cardKey = GetDeckPreviewProviderKey(cardData);
                    string sourceKey = sourceBuildingInstanceId;

                    hash = hash * 31 + (cardKey != null ? cardKey.GetHashCode() : 0);
                    hash = hash * 31 + (sourceKey != null ? sourceKey.GetHashCode() : 0);
                    hash = hash * 31 + (sourceBuilding != null ? sourceBuilding.GetArmyOccupiedSupply() : (cardData != null ? cardData.PopulationCost : 0));
                    hash = hash * 31 + (sourceBuilding != null ? sourceBuilding.GetArmyForce() : (cardData != null ? cardData.SoldierCount : 0));
                }

                return hash;
            }
        }

        private static string GetDeckPreviewProviderKey(ICardDataProvider provider)
        {
            if (provider == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(provider.CardId))
            {
                return provider.CardId;
            }

            return Utility.Text.Format("{0}_{1}", provider.CardName, provider.SoldierIndex);
        }

        private void ResolveAreaMaterialOverlay()
        {
            if (areaMaterialOverlay == null)
            {
                areaMaterialOverlay = GetComponent<CardFx.CardAreaMaterialOverlay>();
            }

            if (areaMaterialOverlay == null)
            {
                areaMaterialOverlay = gameObject.AddComponent<CardFx.CardAreaMaterialOverlay>();
            }
        }

        private void EnsureTargetingVisuals()
        {
            if (m_FormRectTransform == null)
            {
                m_FormRectTransform = transform as RectTransform;
            }

            if (m_TargetingCurveGraphic == null)
            {
                GameObject curveObject = new GameObject("CardTargetingCurve", typeof(RectTransform), typeof(CanvasRenderer), typeof(CardFx.CardTargetingCurveGraphic));
                RectTransform curveRect = curveObject.GetComponent<RectTransform>();
                curveRect.SetParent(m_FormRectTransform, false);
                curveRect.anchorMin = Vector2.zero;
                curveRect.anchorMax = Vector2.one;
                curveRect.offsetMin = Vector2.zero;
                curveRect.offsetMax = Vector2.zero;
                curveRect.SetAsLastSibling();

                m_TargetingCurveGraphic = curveObject.GetComponent<CardFx.CardTargetingCurveGraphic>();
                m_TargetingCurveGraphic.color = targetingCurveColor;
                m_TargetingCurveGraphic.Thickness = targetingCurveThickness;
                m_TargetingCurveGraphic.SegmentCount = targetingCurveSegments;
            }

            if (m_TargetingReticleRect == null)
            {
                GameObject reticleObject = new GameObject("CardTargetingReticle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                m_TargetingReticleRect = reticleObject.GetComponent<RectTransform>();
                m_TargetingReticleRect.SetParent(m_FormRectTransform, false);
                m_TargetingReticleRect.anchorMin = new Vector2(0.5f, 0.5f);
                m_TargetingReticleRect.anchorMax = new Vector2(0.5f, 0.5f);
                m_TargetingReticleRect.pivot = new Vector2(0.5f, 0.5f);
                m_TargetingReticleRect.sizeDelta = targetingReticleSize;
                m_TargetingReticleRect.SetAsLastSibling();

                m_TargetingReticleImage = reticleObject.GetComponent<Image>();
                m_TargetingReticleImage.raycastTarget = false;
                m_TargetingReticleImage.preserveAspect = true;
                m_TargetingReticleImage.color = targetingCurveColor;
                m_TargetingReticleImage.sprite = GetTargetingReticleSprite();
            }
        }

        private void HideTargetingVisuals()
        {
            if (m_TargetingCurveGraphic != null)
            {
                m_TargetingCurveGraphic.ClearCurve();
                m_TargetingCurveGraphic.enabled = false;
            }

            if (m_TargetingReticleImage != null)
            {
                m_TargetingReticleImage.enabled = false;
            }
        }

        private void UpdateTargetingVisuals(HandCardItem cardItem, Vector3 worldPosition, bool isValid)
        {
            EnsureTargetingVisuals();

            if (cardItem == null || Camera.main == null || m_FormRectTransform == null)
            {
                HideTargetingVisuals();
                return;
            }

            Vector3 reticleScreenPoint3 = Camera.main.WorldToScreenPoint(worldPosition);
            if (reticleScreenPoint3.z <= 0f)
            {
                HideTargetingVisuals();
                return;
            }

            Vector2 cardScreenPoint = cardItem.GetScreenAnchorPosition(GetUICamera());
            Vector2 reticleScreenPoint = new Vector2(reticleScreenPoint3.x, reticleScreenPoint3.y);

            if (!TryConvertScreenPointToLocal(cardScreenPoint, out Vector2 startLocalPoint)
                || !TryConvertScreenPointToLocal(reticleScreenPoint, out Vector2 endLocalPoint))
            {
                HideTargetingVisuals();
                return;
            }

            float sceneAdaptiveCurveHeight = CalculateSceneAdaptiveCurveHeight(
                startLocalPoint,
                endLocalPoint,
                reticleScreenPoint3.z);

            Color targetingColor = isValid
                ? targetingCurveColor
                : new Color(1f, 0.42f, 0.42f, 0.92f);

            m_TargetingCurveGraphic.color = targetingColor;
            m_TargetingCurveGraphic.Thickness = targetingCurveThickness;
            m_TargetingCurveGraphic.SegmentCount = targetingCurveSegments;
            m_TargetingCurveGraphic.SetCurve(startLocalPoint, endLocalPoint, sceneAdaptiveCurveHeight);
            m_TargetingCurveGraphic.enabled = true;

            m_TargetingReticleRect.sizeDelta = targetingReticleSize;
            m_TargetingReticleRect.anchoredPosition = endLocalPoint;
            m_TargetingReticleImage.sprite = GetTargetingReticleSprite();
            m_TargetingReticleImage.color = targetingColor;
            m_TargetingReticleImage.enabled = true;
        }

        private float CalculateSceneAdaptiveCurveHeight(Vector2 startLocalPoint, Vector2 endLocalPoint, float sceneDepth)
        {
            Vector2 delta = endLocalPoint - startLocalPoint;
            float screenDistance = delta.magnitude;
            float horizontalDistance = Mathf.Abs(delta.x);
            float verticalDistance = Mathf.Max(0f, delta.y);

            float distanceFactor = Mathf.InverseLerp(160f, 900f, screenDistance);
            float horizontalFactor = Mathf.InverseLerp(80f, 700f, horizontalDistance);
            float verticalFactor = Mathf.InverseLerp(40f, 420f, verticalDistance);
            float depthFactor = Mathf.InverseLerp(8f, 45f, sceneDepth);

            float adaptiveScale = 1f
                + distanceFactor * 0.3f
                + horizontalFactor * 0.25f
                + verticalFactor * 0.2f
                + depthFactor * 0.2f;

            return targetingCurveHeight * adaptiveScale;
        }

        private bool TryConvertScreenPointToLocal(Vector2 screenPoint, out Vector2 localPoint)
        {
            if (m_FormRectTransform == null)
            {
                localPoint = Vector2.zero;
                return false;
            }

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                m_FormRectTransform,
                screenPoint,
                GetUICamera(),
                out localPoint);
        }

        private Camera ResolveCanvasCamera()
        {
            if (m_FormCanvas == null)
            {
                return GFBuiltin.UICamera;
            }

            if (m_FormCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return m_FormCanvas.worldCamera != null ? m_FormCanvas.worldCamera : GFBuiltin.UICamera;
        }

        private Camera GetUICamera()
        {
            if (m_FormCanvas == null)
            {
                return GFBuiltin.UICamera;
            }

            if (m_UICamera == null && m_FormCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                m_UICamera = ResolveCanvasCamera();
            }

            return m_UICamera;
        }

        private Sprite GetTargetingReticleSprite()
        {
            if (targetingReticleSprite != null)
            {
                return targetingReticleSprite;
            }

            if (m_RuntimeFallbackReticleSprite == null)
            {
                m_RuntimeFallbackReticleSprite = CreateFallbackReticleSprite();
            }

            return m_RuntimeFallbackReticleSprite;
        }

        private static Sprite CreateFallbackReticleSprite()
        {
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.name = "RuntimeCardTargetingReticle";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;

            Color clear = new Color(0f, 0f, 0f, 0f);
            Color solid = Color.white;

            int center = size / 2;
            int outerRadius = 42;
            int innerRadius = 31;
            int armGap = 12;
            int armLength = 18;
            int thickness = 3;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int dx = x - center;
                    int dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    bool drawRing = distance >= innerRadius && distance <= outerRadius;
                    bool drawHorizontal = Mathf.Abs(dy) <= thickness && Mathf.Abs(dx) >= armGap && Mathf.Abs(dx) <= armGap + armLength;
                    bool drawVertical = Mathf.Abs(dx) <= thickness && Mathf.Abs(dy) >= armGap && Mathf.Abs(dy) <= armGap + armLength;

                    texture.SetPixel(x, y, drawRing || drawHorizontal || drawVertical ? solid : clear);
                }
            }

            texture.Apply(false, false);

            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f);
        }

        private void InitializeCardHotkeys()
        {
            InputManager inputManager = GameEntry.GetComponent<InputManager>()
                ?? throw new InvalidOperationException("Card UI requires InputManager.");
            InputActionAsset actions = inputManager.playerInput?.actions
                ?? throw new InvalidOperationException("Card UI requires PlayerInput actions.");

            for (int i = 0; i < m_CardHotkeyActions.Length; i++)
                m_CardHotkeyActions[i] = actions.FindAction($"Player/Card{i + 1}", true);
        }

        private void HandleHotkeys()
        {
            for (int i = 0; i < m_CardHotkeyActions.Length; i++)
            {
                if (!m_CardHotkeyActions[i].WasPressedThisFrame())
                    continue;

                PlayCardByIndex(i);
                return;
            }
        }

        private void PlayCardByIndex(int index)
        {
            if (index < 0 || index >= m_HandCardItemObjects.Count)
            {
                return;
            }

            UIItemObject itemObject = m_HandCardItemObjects[index];
            HandCardItem cardItem = itemObject != null ? itemObject.gameObject.GetComponent<HandCardItem>() : null;
            if (cardItem != null && cardItem.CanPlay())
            {
                cardItem.SetSelected(true);
                m_CardSystemController.PlayCard(cardItem.GetCardModel());
            }
        }

        private void RefreshHandCards()
        {
            ClearHandCards();

            PlayerHandModel handModel = m_CardSystemController.GetHandModel();
            if (handModel == null)
            {
                return;
            }

            List<CardModel> cards = handModel.GetAllCards();
            bool playInitialDealAnimation = cardDeckTransform != null;
            foreach (CardModel cardModel in cards)
            {
                CreateHandCardItem(cardModel, playInitialDealAnimation);
            }

            RefreshHandCardLayout();
        }

        private void RemoveHandCardItemDirect(CardModel cardModel)
        {
            UIItemObject itemToRemove = null;
            foreach (UIItemObject itemObject in m_HandCardItemObjects)
            {
                HandCardItem cardItem = itemObject != null ? itemObject.gameObject.GetComponent<HandCardItem>() : null;
                if (cardItem != null && cardItem.GetCardModel() == cardModel)
                {
                    itemToRemove = itemObject;
                    break;
                }
            }

            if (itemToRemove != null)
            {
                HandCardItem cardItem = itemToRemove.gameObject.GetComponent<HandCardItem>();
                if (cardItem != null)
                {
                    cardItem.PrepareForRecycle();
                }

                m_HandCardItemObjects.Remove(itemToRemove);
                UnspawnItem<UIItemObject>(handCardItemPrefab, itemToRemove);
                RefreshHandCardLayout();
            }
        }

        private void RemoveHandCardItem(CardModel cardModel, bool reservePlayedSlot)
        {
            UIItemObject itemToRemove = null;
            foreach (UIItemObject itemObject in m_HandCardItemObjects)
            {
                HandCardItem cardItem = itemObject != null ? itemObject.gameObject.GetComponent<HandCardItem>() : null;
                if (cardItem != null && cardItem.GetCardModel() == cardModel)
                {
                    itemToRemove = itemObject;
                    break;
                }
            }

            if (itemToRemove != null)
            {
                HandCardItem cardItem = itemToRemove.gameObject.GetComponent<HandCardItem>();
                if (cardItem != null)
                {
                    if (reservePlayedSlot)
                    {
                        ReservePlayedCardSlot(cardItem);
                    }

                    cardItem.PrepareForRecycle();
                }

                m_HandCardItemObjects.Remove(itemToRemove);
                UnspawnItem<UIItemObject>(handCardItemPrefab, itemToRemove);
                RefreshHandCardLayout();
            }
        }

        private void ClearHandCards()
        {
            foreach (UIItemObject itemObject in m_HandCardItemObjects)
            {
                HandCardItem cardItem = itemObject != null ? itemObject.gameObject.GetComponent<HandCardItem>() : null;
                if (cardItem != null)
                {
                    cardItem.PrepareForRecycle();
                }
            }

            UnspawnAllItem<UIItemObject>(handCardItemPrefab);
            m_HandCardItemObjects.Clear();
            ClearPlayedCardSlots();
            RefreshHandCardLayout();
        }

        public void OnCardBeginDrag(HandCardItem cardItem)
        {
            m_DraggingCard = cardItem;
            RefreshHandCardInteractionVisuals();
            EnsureTargetingVisuals();
            HideTargetingVisuals();

            if (trashBin != null)
            {
                SetTrashBinDragFeedback(cardItem, false);
            }

            if (cardItem.CanPlay())
            {
                m_CardSystemController.StartPlacement(cardItem.GetCardModel());
            }
            else
            {
                Log.Info("[CardUI] Restricted card drag started: placement disabled, discard enabled.");
            }
        }

        public bool OnCardDragging(HandCardItem cardItem, Vector2 screenPosition)
        {
            bool isOverTrash = UpdateTrashBinHint(screenPosition);
            SetTrashBinDragFeedback(cardItem, isOverTrash);

            if (!cardItem.CanPlay())
            {
                areaMaterialOverlay?.HideAreaEffect();
                HideTargetingVisuals();
                bool isOverHand = IsOverHandCardArea(screenPosition, false);
                if (!IsDragPositionAllowed(false, isOverHand, isOverTrash))
                {
                    m_DraggingCard = null;
                    RefreshHandCardInteractionVisuals();
                    SetTrashBinDragFeedback(cardItem, false);
                    m_CardSystemController.CancelPlacement();
                    Log.Info("[CardUI] Restricted card drag canceled immediately after leaving the hand area.");
                    return false;
                }

                return true;
            }

            UpdateAreaMaterialEffect(cardItem, screenPosition);
            return true;
        }

        internal static bool IsDragPositionAllowed(bool canPlay, bool isOverHand, bool isOverTrash)
        {
            return canPlay || isOverHand || isOverTrash;
        }

        public bool OnCardEndDrag(HandCardItem cardItem, Vector2 screenPosition)
        {
            m_DraggingCard = null;
            RefreshHandCardInteractionVisuals();
            SetTrashBinDragFeedback(cardItem, false);

            HideTargetingVisuals();
            areaMaterialOverlay?.HideAreaEffect();

            bool isInTrash = IsInTrashBin(screenPosition, true);
            if (isInTrash)
            {
                CardModel discardedCardModel = cardItem.GetCardModel();
                m_CardSystemController.CancelPlacement();
                bool discarded = m_CardSystemController.DiscardCard(discardedCardModel);
                if (!discarded)
                {
                    cardItem.RestoreToHandLayoutImmediately();
                    RefreshHandCardLayout();
                    RequestDeferredHandLayoutRefresh();
                    Log.Error("[CardUI] Failed to schedule discard command for the released card.");
                    return false;
                }

                return true;
            }

            bool isOverHand = IsOverHandCardArea(screenPosition, true);
            if (isOverHand)
            {
                m_CardSystemController.CancelPlacement();
                cardItem.RestoreToHandLayoutImmediately();
                RefreshHandCardLayout();
                RequestDeferredHandLayoutRefresh();
                RefreshHandCardInteractionVisuals();
                return true;
            }

            if (!cardItem.CanPlay())
            {
                m_CardSystemController.CancelPlacement();
                cardItem.RestoreToHandLayoutImmediately();
                RefreshHandCardLayout();
                RequestDeferredHandLayoutRefresh();
                Log.Info("[CardUI] Restricted card drag ended outside the hand and was returned.");
                return true;
            }

            bool placed = m_CardSystemController.ConfirmPlacement(cardItem.GetCardModel(), screenPosition);
            if (placed)
            {
                // ConfirmPlacement 会同步触发 CardPlayed 事件并回收对应 UI。
                // 这里不要再对 cardItem 做成功动画或重设父级，否则会把已回收的对象重新拽回手牌区。
            }
            else
            {
                m_CardSystemController.CancelPlacement();
            }

            return placed;
        }

        public bool HasActiveHandCardDrag(HandCardItem requester = null)
        {
            return m_DraggingCard != null && !ReferenceEquals(m_DraggingCard, requester);
        }

        private void RefreshHandCardInteractionVisuals()
        {
            foreach (UIItemObject itemObject in m_HandCardItemObjects)
            {
                HandCardItem cardItem = itemObject != null ? itemObject.gameObject.GetComponent<HandCardItem>() : null;
                if (cardItem == null)
                {
                    continue;
                }

                cardItem.RefreshInteractionVisualState();
            }
        }

        private bool UpdateTrashBinHint(Vector2 screenPosition)
        {
            bool isOver = IsInTrashBin(screenPosition, false);

            if (trashBinHintText != null)
            {
                trashBinHintText.gameObject.SetActive(isOver);
            }

            return isOver;
        }

        private void ResolveTrashBinImage()
        {
            if (trashBin == null)
            {
                return;
            }

            if (m_TrashBinImage == null)
            {
                m_TrashBinImage = trashBin.GetComponent<Image>();
            }

            if (m_TrashBinImage == null)
            {
                m_TrashBinImage = trashBin.GetComponentInChildren<Image>(true);
            }

            if (m_TrashBinImage != null && m_DefaultTrashBinSprite == null)
            {
                m_DefaultTrashBinSprite = m_TrashBinImage.sprite;
            }
        }

        private void SetTrashBinDragFeedback(HandCardItem cardItem, bool isOverTrash)
        {
            SetTrashBinOpen(isOverTrash);
            cardItem?.SetTrashHoverTransparency(isOverTrash, trashHoverCardAlpha);
        }

        private void SetTrashBinOpen(bool open, bool force = false)
        {
            if (!force && m_IsTrashBinOpen == open)
            {
                return;
            }

            m_IsTrashBinOpen = open;
            ResolveTrashBinImage();

            if (m_TrashBinImage == null)
            {
                return;
            }

            if (open && trashBinOpenSprite == null)
            {
                return;
            }

            Sprite targetSprite = open
                ? trashBinOpenSprite
                : (trashBinClosedSprite != null ? trashBinClosedSprite : m_DefaultTrashBinSprite);

            if (m_TrashBinImage.sprite != targetSprite)
            {
                m_TrashBinImage.sprite = targetSprite;
            }
        }

        private void UpdateAreaMaterialEffect(HandCardItem cardItem, Vector2 screenPosition)
        {
            if (m_CardSystemController == null || cardItem == null)
            {
                areaMaterialOverlay?.HideAreaEffect();
                HideTargetingVisuals();
                return;
            }

            bool isOverTrash = IsInTrashBin(screenPosition, false);
            bool isOverHandArea = IsOverHandCardArea(screenPosition, false);

            if (isOverTrash || isOverHandArea)
            {
                areaMaterialOverlay?.HideAreaEffect();
                HideTargetingVisuals();
                cardItem.ExitTargetingMode(screenPosition);
                return;
            }

            if (!m_CardSystemController.TryGetPlacementPreview(screenPosition, out Vector3 worldPosition, out bool isValid, m_PreviewSpawnPositions))
            {
                areaMaterialOverlay?.HideAreaEffect();
                HideTargetingVisuals();
                cardItem.EnterTargetingMode();
                return;
            }

            float previewRadius = Mathf.Max(0.1f, m_CardSystemController.GetCurrentPlacementRadius());
            areaMaterialOverlay?.ShowPlacementPreview(worldPosition, previewRadius, isValid, cardItem.GetCardModel(), m_PreviewSpawnPositions);

            cardItem.EnterTargetingMode();
            UpdateTargetingVisuals(cardItem, worldPosition, isValid);
        }

        private bool IsOverHandCardArea(Vector2 screenPosition, bool logResult = true)
        {
            RectTransform targetRect = m_ResolvedHandCardAreaRect != null
                ? m_ResolvedHandCardAreaRect
                : ResolveHandCardAreaRect();

            if (targetRect == null)
            {
                if (logResult)
                {
                    Log.Warning("[CardUI] No valid hand card area RectTransform found.");
                }

                return false;
            }

            Camera uiCamera = GetUICamera();
            bool resultWithCamera = RectTransformUtility.RectangleContainsScreenPoint(targetRect, screenPosition, uiCamera);
            bool resultWithoutCamera = RectTransformUtility.RectangleContainsScreenPoint(targetRect, screenPosition, null);
            bool result = resultWithCamera || resultWithoutCamera;

            if (logResult)
            {
                Log.Info($"[CardUI] IsOverHandCardArea={result}, target={targetRect.name}, screen={screenPosition}");
            }

            return result;
        }

        private RectTransform ResolveHandCardAreaRect()
        {
            if (handCardArea != null)
            {
                return handCardArea;
            }

            if (handCardContainer != null && handCardContainer.parent != null)
            {
                return handCardContainer.parent.GetComponent<RectTransform>();
            }

            return handCardContainer != null
                ? handCardContainer.GetComponent<RectTransform>()
                : null;
        }

        private bool IsInTrashBin(Vector2 screenPosition, bool logResult = true)
        {
            if (m_TrashBinRect == null)
            {
                if (logResult)
                {
                    Log.Warning("[CardUI] TrashBin RectTransform is null.");
                }

                return false;
            }

            Canvas canvas = m_TrashBinCanvas != null
                ? m_TrashBinCanvas
                : m_TrashBinRect.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                if (logResult)
                {
                    Log.Warning("[CardUI] Cannot find Canvas for trash bin.");
                }

                return false;
            }

            m_TrashBinCanvas = canvas;

            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : (canvas.worldCamera != null ? canvas.worldCamera : GFBuiltin.UICamera);

            bool result = RectTransformUtility.RectangleContainsScreenPoint(m_TrashBinRect, screenPosition, uiCamera);
            if (logResult)
            {
                Log.Info($"[CardUI] IsInTrashBin={result}, screen={screenPosition}");
            }

            return result;
        }

        private void OnCardDrawn(object sender, GameEventArgs e)
        {
            if (!ReferenceEquals(sender, m_CardSystemController))
            {
                return;
            }

            // The opening completion synchronizes the current model once the panel is stationary.
            if (!m_IsPanelReady)
            {
                return;
            }

            CardDrawnEventArgs args = (CardDrawnEventArgs)e;
            if (ContainsCardItem(args.CardModel))
            {
                return;
            }

            GameObject targetSlot = m_PlayedCardSlotPlaceholders.Count > 0
                ? m_PlayedCardSlotPlaceholders.Peek()
                : null;
            if (CreateHandCardItem(args.CardModel, true, targetSlot) && targetSlot != null)
            {
                m_PlayedCardSlotPlaceholders.Dequeue();
            }
        }

        private void OnCardPlayed(object sender, GameEventArgs e)
        {
            if (!ReferenceEquals(sender, m_CardSystemController))
            {
                return;
            }

            CardPlayedEventArgs args = (CardPlayedEventArgs)e;
            RemoveHandCardItem(args.CardModel, true);
            if (AudioManager.Instance != null)
                AudioManager.Instance.Play("createUnit");
        }

        private void OnCardDiscarded(object sender, GameEventArgs e)
        {
            if (!ReferenceEquals(sender, m_CardSystemController))
            {
                return;
            }

            CardDiscardedEventArgs args = (CardDiscardedEventArgs)e;
            HandCardItem cardItem = GetRequiredHandCardItem(args.CardModel);
            cardItem.OnDiscardSuccess(() => RemoveHandCardItemDirect(args.CardModel));
            if (AudioManager.Instance != null)
                AudioManager.Instance.Play("discardCard");
        }

        private HandCardItem GetRequiredHandCardItem(CardModel cardModel)
        {
            foreach (UIItemObject itemObject in m_HandCardItemObjects)
            {
                HandCardItem cardItem = itemObject != null
                    ? itemObject.gameObject.GetComponent<HandCardItem>()
                    : null;
                if (cardItem != null && cardItem.GetCardModel() == cardModel)
                    return cardItem;
            }

            throw new InvalidOperationException(
                $"Card presentation item is missing for runtimeId={cardModel?.RuntimeId}.");
        }

        private void OnIngameValueChanged(object sender, GameEventArgs e)
        {
            IngameValueChangedEventArgs args = (IngameValueChangedEventArgs)e;
            if (args.DataType != IngameValueType.CurrentSupply && args.DataType != IngameValueType.MaxSupply)
            {
                return;
            }

            foreach (UIItemObject itemObject in m_HandCardItemObjects)
            {
                HandCardItem cardItem = itemObject != null ? itemObject.gameObject.GetComponent<HandCardItem>() : null;
                if (cardItem != null)
                {
                    cardItem.RefreshView();
                }
            }
        }

        private void OnArmyBuildingCardPropertyChanged(object sender, GameEventArgs e)
        {
            ArmyBuildingCardPropertyChangedEventArgs args = (ArmyBuildingCardPropertyChangedEventArgs)e;
            if (string.IsNullOrWhiteSpace(args.BuildingInstanceId))
            {
                return;
            }

            foreach (UIItemObject itemObject in m_HandCardItemObjects)
            {
                HandCardItem cardItem = itemObject != null ? itemObject.gameObject.GetComponent<HandCardItem>() : null;
                if (cardItem == null)
                {
                    continue;
                }

                CardModel cardModel = cardItem.GetCardModel();
                if (cardModel == null)
                {
                    continue;
                }

                if (string.Equals(cardModel.GetSourceBuildingInstanceId(), args.BuildingInstanceId, StringComparison.Ordinal))
                {
                    cardItem.RefreshView();
                }
            }
        }

        private bool ContainsCardItem(CardModel cardModel)
        {
            if (cardModel == null)
            {
                return false;
            }

            for (int i = 0; i < m_HandCardItemObjects.Count; i++)
            {
                UIItemObject itemObject = m_HandCardItemObjects[i];
                if (itemObject == null || itemObject.gameObject == null)
                {
                    continue;
                }

                HandCardItem cardItem = itemObject.gameObject.GetComponent<HandCardItem>();
                if (cardItem != null && ReferenceEquals(cardItem.GetCardModel(), cardModel))
                {
                    return true;
                }
            }

            return false;
        }

        protected override void OnRecycle()
        {
            base.OnRecycle();
        }
    }
}
