using System;
using System.Collections.Generic;
using GameFramework;
using GameFramework.Event;
using TMPro;
using UnityEngine;
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
        [SerializeField] private Transform handCardContainer;
        [SerializeField] private RectTransform handCardArea;
        [SerializeField] private GameObject trashBin;
        [SerializeField] private TextMeshProUGUI trashBinHintText;

        [Header("预制体")]
        [SerializeField] private GameObject handCardItemPrefab;

        [Header("场景放置反馈")]
        [SerializeField] private CardFx.CardAreaMaterialOverlay areaMaterialOverlay;

        [Header("抽卡动画")]
        [SerializeField] private RectTransform cardDeckTransform;
        [SerializeField] private float cardMoveToHandDuration = 0.5f;

        [Header("目标拖拽表现")]
        [SerializeField] [InspectorName("准星图片")] private Sprite targetingReticleSprite;
        [SerializeField] [InspectorName("准星尺寸")] private Vector2 targetingReticleSize = new Vector2(72f, 72f);
        [SerializeField] [InspectorName("连线颜色")] private Color targetingCurveColor = new Color(0.6f, 1f, 0.75f, 0.92f);
        [SerializeField] [InspectorName("连线粗细")] private float targetingCurveThickness = 14f;
        [SerializeField] [InspectorName("连线弯曲高度")] private float targetingCurveHeight = 120f;
        [SerializeField] [InspectorName("连线分段数")] [Range(4, 64)] private int targetingCurveSegments = 24;

        [Header("快捷键")]
        [SerializeField] private KeyCode toggleUIKey = KeyCode.Tab;

        private readonly List<UIItemObject> m_HandCardItemObjects = new List<UIItemObject>();
        private readonly List<Vector3> m_PreviewSpawnPositions = new List<Vector3>();

        private CardSystemController m_CardSystemController;
        private HandCardItem m_DraggingCard;
        private RectTransform m_TrashBinRect;
        private bool m_IsUIVisible = true;

        private Canvas m_FormCanvas;
        private RectTransform m_FormRectTransform;
        private Camera m_UICamera;

        private CardFx.CardTargetingCurveGraphic m_TargetingCurveGraphic;
        private RectTransform m_TargetingReticleRect;
        private Image m_TargetingReticleImage;
        private Sprite m_RuntimeFallbackReticleSprite;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            m_FormCanvas = GetComponent<Canvas>();
            m_FormRectTransform = transform as RectTransform;
            m_UICamera = ResolveCanvasCamera();

            ResolveAreaMaterialOverlay();
            EnsureTargetingVisuals();
            HideTargetingVisuals();

            if (trashBin != null)
            {
                m_TrashBinRect = trashBin.GetComponent<RectTransform>();
                trashBin.SetActive(false);
            }
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

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

            RefreshHandCards();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
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

            ClearHandCards();
            HideTargetingVisuals();
            areaMaterialOverlay?.HideAreaEffect();
        }

        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);

            if (Input.GetKeyDown(toggleUIKey))
            {
                ToggleUIVisibility();
            }

            HandleHotkeys();
        }

        private void RefreshHandCardLayout()
        {
            Canvas.ForceUpdateCanvases();

            RectTransform containerRect = handCardContainer as RectTransform;
            if (containerRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
            }

            if (handCardArea != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(handCardArea);
            }

            if (containerRect != null && containerRect.parent is RectTransform parentRect)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
            }

            Canvas.ForceUpdateCanvases();
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

        private void ToggleUIVisibility()
        {
            m_IsUIVisible = !m_IsUIVisible;
            gameObject.SetActive(m_IsUIVisible);
            Log.Info(Utility.Text.Format("Card UI {0}", m_IsUIVisible ? "显示" : "隐藏"));
        }

        private void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                PlayCardByIndex(0);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                PlayCardByIndex(1);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                PlayCardByIndex(2);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                PlayCardByIndex(3);
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

        private void CreateHandCardItem(CardModel cardModel, bool playAnimation = true)
        {
            if (handCardItemPrefab == null || handCardContainer == null)
            {
                Log.Error("HandCardItemPrefab or HandCardContainer is null.");
                return;
            }

            UIItemObject itemObject = SpawnItem<UIItemObject>(handCardItemPrefab, handCardContainer);
            if (itemObject == null)
            {
                Log.Error("Failed to spawn HandCardItem from object pool.");
                return;
            }

            HandCardItem cardItem = itemObject.gameObject.GetComponent<HandCardItem>();
            if (cardItem == null)
            {
                Log.Error("HandCardItem component not found on spawned object.");
                return;
            }

            RectTransform itemRectTransform = itemObject.gameObject.GetComponent<RectTransform>();
            if (itemRectTransform != null)
            {
                itemRectTransform.SetParent(handCardContainer, false);
                itemRectTransform.localScale = Vector3.one;
                itemRectTransform.SetAsLastSibling();
            }

            cardItem.Initialize(cardModel, this);
            m_HandCardItemObjects.Add(itemObject);
            RefreshHandCardLayout();

            if (playAnimation && cardDeckTransform != null)
            {
                cardItem.MoveToHandFromScreenPosition(cardDeckTransform.position, cardMoveToHandDuration);
            }
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
                m_HandCardItemObjects.Remove(itemToRemove);
                UnspawnItem<UIItemObject>(handCardItemPrefab, itemToRemove);
                RefreshHandCardLayout();
            }
        }

        private void RemoveHandCardItem(CardModel cardModel)
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
                m_HandCardItemObjects.Remove(itemToRemove);
                UnspawnItem<UIItemObject>(handCardItemPrefab, itemToRemove);
                RefreshHandCardLayout();
            }
        }

        private void ClearHandCards()
        {
            UnspawnAllItem<UIItemObject>(handCardItemPrefab);
            m_HandCardItemObjects.Clear();
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
                trashBin.SetActive(true);
            }

            m_CardSystemController.StartPlacement(cardItem.GetCardModel());
        }

        public void OnCardDragging(HandCardItem cardItem, Vector2 screenPosition)
        {
            UpdateTrashBinHint(screenPosition);
            UpdateAreaMaterialEffect(cardItem, screenPosition);
        }

        public bool OnCardEndDrag(HandCardItem cardItem, Vector2 screenPosition)
        {
            m_DraggingCard = null;
            RefreshHandCardInteractionVisuals();

            if (trashBin != null)
            {
                trashBin.SetActive(false);
            }

            HideTargetingVisuals();
            areaMaterialOverlay?.HideAreaEffect();

            bool isInTrash = IsInTrashBin(screenPosition, true);
            if (isInTrash)
            {
                m_CardSystemController.CancelPlacement();
                cardItem.OnDiscardSuccess();
                RemoveHandCardItemDirect(cardItem.GetCardModel());

                bool discarded = m_CardSystemController.DiscardCard(cardItem.GetCardModel());
                if (!discarded)
                {
                    Log.Error("[CardUI] Failed to discard card in controller.");
                }

                return true;
            }

            bool isOverHand = IsOverHandCardArea(screenPosition, true);
            if (isOverHand)
            {
                m_CardSystemController.CancelPlacement();
                return false;
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

        private void UpdateTrashBinHint(Vector2 screenPosition)
        {
            if (m_TrashBinRect == null || trashBinHintText == null)
            {
                return;
            }

            bool isOver = RectTransformUtility.RectangleContainsScreenPoint(
                m_TrashBinRect,
                screenPosition,
                GetUICamera());

            trashBinHintText.gameObject.SetActive(isOver);
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
            RectTransform targetRect = null;
            if (handCardArea != null)
            {
                targetRect = handCardArea;
            }
            else if (handCardContainer != null && handCardContainer.parent != null)
            {
                targetRect = handCardContainer.parent.GetComponent<RectTransform>();
            }
            else if (handCardContainer != null)
            {
                targetRect = handCardContainer.GetComponent<RectTransform>();
            }

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

            Canvas canvas = m_TrashBinRect.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                if (logResult)
                {
                    Log.Warning("[CardUI] Cannot find Canvas for trash bin.");
                }

                return false;
            }

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

            CardDrawnEventArgs args = (CardDrawnEventArgs)e;
            if (ContainsCardItem(args.CardModel))
            {
                return;
            }

            CreateHandCardItem(args.CardModel, true);
        }

        private void OnCardPlayed(object sender, GameEventArgs e)
        {
            if (!ReferenceEquals(sender, m_CardSystemController))
            {
                return;
            }

            CardPlayedEventArgs args = (CardPlayedEventArgs)e;
            RemoveHandCardItem(args.CardModel);
        }

        private void OnCardDiscarded(object sender, GameEventArgs e)
        {
            if (!ReferenceEquals(sender, m_CardSystemController))
            {
                return;
            }

            CardDiscardedEventArgs args = (CardDiscardedEventArgs)e;
            RemoveHandCardItem(args.CardModel);
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

        public void RedrawCards()
        {
            if (m_CardSystemController == null)
            {
                return;
            }

            PlayerHandModel handModel = m_CardSystemController.GetHandModel();
            if (handModel == null)
            {
                return;
            }

            int cardCount = handModel.CardCount;
            handModel.Clear();
            m_CardSystemController.DrawCards(cardCount);
        }

        protected override void OnRecycle()
        {
            base.OnRecycle();
        }
    }
}
