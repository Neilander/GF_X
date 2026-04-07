using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using AAAGame.Card;


namespace AAAGame.Card.UI
{
    
    /// <summary>
    /// 卡牌 UI 系统 - 完全独立版本
    /// 管理整个卡牌 UI 系统，不依赖任何 Temp_script 代码
    /// </summary>
    public class CardUISystem : MonoBehaviour
    {
        private static CardUISystem instance;
        public static CardUISystem Instance => instance;

        [Header("UI 引用")]
        [SerializeField] private GameObject cardUIPrefab;
        [SerializeField] private Transform handCardContainer;
        [SerializeField] private RectTransform handCardArea;
        [SerializeField] private TextMeshProUGUI populationText;
        [SerializeField] private GameObject trashBin;
        [SerializeField] private TextMeshProUGUI trashBinHintText;
        [SerializeField] private GameObject cardUIRoot;

        [Header("场景引用")]
        [SerializeField] private Transform validArea;
        [SerializeField] private Transform forbiddenArea;
        [SerializeField] private CardAreaMaterialOverlay areaMaterialOverlay; // 区域材质叠加器

        [Header("快捷键")]
        [SerializeField] private KeyCode toggleUIKey = KeyCode.Tab;
        [SerializeField] private KeyCode[] cardHotkeys = new KeyCode[]
        {
            KeyCode.Alpha1,
            KeyCode.Alpha2,
            KeyCode.Alpha3,
            KeyCode.Alpha4
        };

        [Header("抽卡动画")]
        [SerializeField] private RectTransform cardDeckTransform;
        [SerializeField] private float cardMoveToHandDuration = 0.5f;

        private List<CardHandUI> handCardUIs = new List<CardHandUI>();
        private CardHandUI currentDraggingCard;
        private CardHandUI selectedCardByHotkey;
        private int selectedCardIndex = -1;
        private RectTransform trashBinRect;
        private bool isUIVisible = true;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;

            if (trashBin != null)
            {
                trashBinRect = trashBin.GetComponent<RectTransform>();
                trashBin.SetActive(false);
            }
        }

        void Start()
        {
            // 订阅事件
            if (PlayerHandManager.Instance != null)
            {
                PlayerHandManager.Instance.OnHandChanged += OnHandChanged;
            }

            if (PopulationManager.Instance != null)
            {
                PopulationManager.Instance.OnPopulationChanged += OnPopulationChanged;
            }

            // 初始化 UI
            RefreshHandUI(playAnimation: true);
            UpdatePopulationUI();

            Debug.Log("[Card] CardUISystem initialized");
        }

        void Update()
        {
            // 快捷键控制 UI 开关
            if (Input.GetKeyDown(toggleUIKey))
            {
                ToggleUI();
            }

            // 快捷键选择卡牌
            for (int i = 0; i < cardHotkeys.Length; i++)
            {
                if (Input.GetKeyDown(cardHotkeys[i]))
                {
                    SelectCardByHotkey(i);
                }
            }

            // 快捷键使用卡牌
            if (selectedCardByHotkey != null && Input.GetMouseButtonDown(0))
            {
                TryUseSelectedCard();
            }

            // ESC 取消选中
            if (Input.GetKeyDown(KeyCode.Escape) && selectedCardByHotkey != null)
            {
                DeselectCard();
            }
        }

        #region UI 显示控制

        /// <summary>
        /// 切换 UI 显示/隐藏
        /// </summary>
        public void ToggleUI()
        {
            isUIVisible = !isUIVisible;
            if (cardUIRoot != null)
            {
                cardUIRoot.SetActive(isUIVisible);
            }
            Debug.Log($"[Card] UI {(isUIVisible ? "显示" : "隐藏")}");
        }

        /// <summary>
        /// 刷新手牌 UI
        /// </summary>
        private void RefreshHandUI(bool playAnimation = false)
        {
            Debug.Log($"[Card] RefreshHandUI: playAnimation={playAnimation}, current UI count={handCardUIs.Count}");

            // 清空现有 UI
            foreach (var ui in handCardUIs)
            {
                if (ui != null) Destroy(ui.gameObject);
            }
            handCardUIs.Clear();

            // 创建新 UI
            if (PlayerHandManager.Instance == null)
            {
                Debug.LogWarning("[Card] RefreshHandUI: PlayerHandManager.Instance is null");
                return;
            }

            var cards = PlayerHandManager.Instance.HandCards;
            Debug.Log($"[Card] Creating {cards.Count} card UIs");
            
            foreach (var card in cards)
            {
                CreateCardUI(card, playAnimation);
            }
        }

        /// <summary>
        /// 创建单张卡牌 UI
        /// </summary>
        private void CreateCardUI(CardData cardData, bool playAnimation = true)
        {
            if (cardUIPrefab == null || handCardContainer == null)
            {
                Debug.LogError("[Card] CreateCardUI: cardUIPrefab or handCardContainer is null");
                return;
            }

            if (cardData == null)
            {
                Debug.LogError("[Card] CreateCardUI: cardData is null");
                return;
            }

            var cardObj = Instantiate(cardUIPrefab, handCardContainer);
            var cardUI = cardObj.GetComponent<CardHandUI>();
            
            if (cardUI != null)
            {
                cardUI.SetCardData(cardData);
                handCardUIs.Add(cardUI);

                if (playAnimation)
                {
                    StartCoroutine(PlayDrawCardAnimation(cardUI));
                }
            }
            else
            {
                Debug.LogError("[Card] CreateCardUI: CardHandUI component not found on prefab");
            }
        }

        /// <summary>
        /// 播放抽卡动画
        /// </summary>
        private IEnumerator PlayDrawCardAnimation(CardHandUI cardUI)
        {
            // 等待两帧，确保 Awake 和布局系统都完成初始化
            yield return null;
            yield return null;

            // 检查 cardUI 是否有效
            if (cardUI == null)
            {
                Debug.LogError("[Card] PlayDrawCardAnimation: cardUI is null");
                yield break;
            }

            Vector2 spawnPosition;
            if (cardDeckTransform != null)
            {
                spawnPosition = cardDeckTransform.position;
            }
            else
            {
                spawnPosition = new Vector2(Screen.width / 2f, Screen.height / 2f);
                Debug.LogWarning("[Card] cardDeckTransform is null, using screen center");
            }

            cardUI.MoveToHandFromScreenPosition(spawnPosition, cardMoveToHandDuration);
        }

        /// <summary>
        /// 更新人口 UI
        /// </summary>
        private void UpdatePopulationUI()
        {
            if (populationText == null || PopulationManager.Instance == null) return;

            int current = PopulationManager.Instance.CurrentPopulation;
            int max = PopulationManager.Instance.MaxPopulation;
            populationText.text = $"{current}/{max}";
            populationText.color = current >= max ? Color.red : Color.white;
        }

        #endregion

        #region 拖拽处理

        /// <summary>
        /// 卡牌开始拖拽
        /// </summary>
        public void OnCardBeginDrag(CardHandUI card)
        {
            currentDraggingCard = card;

            if (trashBin != null)
            {
                trashBin.SetActive(true);
            }

            Debug.Log($"[Card] Card begin drag: {card.CardData?.cardName}");
        }

        /// <summary>
        /// 卡牌拖拽中
        /// </summary>
        public void OnCardDragging(CardHandUI card, Vector2 screenPosition)
        {
            UpdateTrashBinHint(screenPosition);
            UpdateAreaMaterialEffect(screenPosition);
        }

        /// <summary>
        /// 卡牌结束拖拽
        /// </summary>
        public bool OnCardEndDrag(CardHandUI card, Vector2 screenPosition)
        {
            currentDraggingCard = null;

            if (trashBin != null)
            {
                trashBin.SetActive(false);
            }

            // 隐藏区域材质效果
            if (areaMaterialOverlay != null)
            {
                areaMaterialOverlay.HideAreaEffect();
            }

            // 检查是否拖回手牌区域
            if (IsOverHandCardArea(screenPosition))
            {
                Debug.Log($"[Card] Card dragged back to hand: {card.CardData?.cardName}");
                return false;
            }

            // 检查是否在垃圾桶上
            if (IsOverTrashBin(screenPosition))
            {
                DiscardCard(card);
                return true;
            }

            // 检查是否在场景中放置
            if (TryPlaceCardInScene(card, screenPosition))
            {
                return true;
            }

            return false;
        }

        #endregion

        #region 区域材质效果

        /// <summary>
        /// 更新区域材质效果
        /// </summary>
        private void UpdateAreaMaterialEffect(Vector2 screenPosition)
        {
            if (areaMaterialOverlay == null)
            {
                Debug.LogWarning("[Card] UpdateAreaMaterialEffect: areaMaterialOverlay is null! Please configure it in CardUISystem.");
                return;
            }

            // 检查 Camera
            if (Camera.main == null)
            {
                Debug.LogWarning("[Card] UpdateAreaMaterialEffect: Camera.main is null!");
                return;
            }

            // 发射射线
            Ray ray = Camera.main.ScreenPointToRay(screenPosition);
            Debug.Log($"[Card] UpdateAreaMaterialEffect: Raycast from {screenPosition}");
            
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                // 没有碰到任何东西，隐藏效果
                Debug.Log("[Card] UpdateAreaMaterialEffect: Raycast missed, hiding effect");
                areaMaterialOverlay.HideAreaEffect();
                return;
            }

            Vector3 worldPos = hit.point;
            Debug.Log($"[Card] UpdateAreaMaterialEffect: Hit {hit.collider.name} at {worldPos}");

            // 检查是否在禁止区域
            bool isForbidden = IsInForbiddenArea(worldPos);
            Debug.Log($"[Card] UpdateAreaMaterialEffect: isForbidden={isForbidden}");

            // 显示区域效果
            // isValid = true 表示不在禁止区域（可以放置）
            // isValid = false 表示在禁止区域（不可以放置）
            areaMaterialOverlay.ShowAreaEffect(worldPos, !isForbidden);
        }

        #endregion

        #region 手牌区域检测

        private bool IsOverHandCardArea(Vector2 screenPosition)
        {
            if (handCardArea == null)
            {
                if (handCardContainer != null && handCardContainer.parent != null)
                {
                    var parentRect = handCardContainer.parent.GetComponent<RectTransform>();
                    if (parentRect != null)
                    {
                        return RectTransformUtility.RectangleContainsScreenPoint(parentRect, screenPosition);
                    }
                }
                return false;
            }

            return RectTransformUtility.RectangleContainsScreenPoint(handCardArea, screenPosition);
        }

        #endregion

        #region 垃圾桶逻辑

        private void UpdateTrashBinHint(Vector2 screenPosition)
        {
            if (trashBinRect == null) return;

            bool isOver = RectTransformUtility.RectangleContainsScreenPoint(trashBinRect, screenPosition);

            if (trashBinHintText != null)
            {
                trashBinHintText.gameObject.SetActive(isOver);
            }
        }

        private bool IsOverTrashBin(Vector2 screenPosition)
        {
            if (trashBinRect == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(trashBinRect, screenPosition);
        }

        private void DiscardCard(CardHandUI card)
        {
            if (card == null || card.CardData == null) return;

            string cardName = card.CardData.cardName;
            PlayerHandManager.Instance?.RemoveCard(card.CardData);

            Debug.Log($"[Card] Card discarded: {cardName}");
        }

        #endregion

        #region 快捷键功能

        private void SelectCardByHotkey(int index)
        {
            if (selectedCardByHotkey != null)
            {
                selectedCardByHotkey.SetSelected(false);
            }

            if (index < 0 || index >= handCardUIs.Count)
            {
                Debug.LogWarning($"[Card] Card index {index + 1} out of range");
                selectedCardByHotkey = null;
                selectedCardIndex = -1;
                return;
            }

            selectedCardByHotkey = handCardUIs[index];
            selectedCardIndex = index;

            if (selectedCardByHotkey != null && selectedCardByHotkey.CardData != null)
            {
                if (PopulationManager.Instance != null &&
                    !PopulationManager.Instance.HasEnoughPopulation(selectedCardByHotkey.CardData.populationCost))
                {
                    Debug.LogWarning($"[Card] Not enough population for {selectedCardByHotkey.CardData.cardName}");
                    selectedCardByHotkey = null;
                    selectedCardIndex = -1;
                    return;
                }

                selectedCardByHotkey.SetSelected(true);
                Debug.Log($"[Card] Card selected: {selectedCardByHotkey.CardData.cardName}");
            }
        }

        private void DeselectCard()
        {
            if (selectedCardByHotkey != null)
            {
                selectedCardByHotkey.SetSelected(false);
                Debug.Log($"[Card] Card deselected: {selectedCardByHotkey.CardData?.cardName}");
            }
            selectedCardByHotkey = null;
            selectedCardIndex = -1;
        }

        private void TryUseSelectedCard()
        {
            if (selectedCardByHotkey == null || selectedCardByHotkey.CardData == null) return;

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                Debug.LogWarning("[Card] No valid position clicked");
                return;
            }

            Vector3 worldPos = hit.point;

            if (IsInForbiddenArea(worldPos))
            {
                Debug.LogWarning("[Card] Cannot deploy in forbidden area");
                return;
            }

            if (!PopulationManager.Instance.OccupyPopulation(selectedCardByHotkey.CardData.populationCost))
            {
                Debug.LogWarning("[Card] Not enough population");
                return;
            }

            SpawnSoldiers(selectedCardByHotkey.CardData, worldPos);
            PlayerHandManager.Instance?.RemoveCard(selectedCardByHotkey.CardData);

            Destroy(selectedCardByHotkey.gameObject);
            handCardUIs.Remove(selectedCardByHotkey);

            selectedCardByHotkey = null;
            selectedCardIndex = -1;

            Debug.Log($"[Card] Card used at position: {worldPos}");
        }

        #endregion

        #region 场景放置逻辑

        private bool TryPlaceCardInScene(CardHandUI card, Vector2 screenPosition)
        {
            if (card == null || card.CardData == null)
            {
                Debug.LogError("[Card] TryPlaceCardInScene: card or CardData is null");
                return false;
            }

            // 检查 Camera
            if (Camera.main == null)
            {
                Debug.LogError("[Card] TryPlaceCardInScene: Camera.main is null");
                return false;
            }

            // 发射射线
            Ray ray = Camera.main.ScreenPointToRay(screenPosition);
            Debug.Log($"[Card] Raycast from screen position: {screenPosition}");
            Debug.Log($"[Card] Ray origin: {ray.origin}, direction: {ray.direction}");

            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                Debug.LogWarning("[Card] Raycast failed - no collider hit. Make sure scene has ground with Collider!");
                Debug.LogWarning("[Card] Tip: Add a Plane (3D Object > Plane) to the scene");
                return false;
            }

            Debug.Log($"[Card] Raycast hit: {hit.collider.name} at position {hit.point}");
            Vector3 worldPos = hit.point;

            // 检查禁止区域
            if (IsInForbiddenArea(worldPos))
            {
                Debug.LogWarning("[Card] Cannot deploy in forbidden area");
                return false;
            }

            // 检查人口
            if (PopulationManager.Instance == null)
            {
                Debug.LogError("[Card] PopulationManager.Instance is null");
                return false;
            }

            if (!PopulationManager.Instance.OccupyPopulation(card.CardData.populationCost))
            {
                Debug.LogWarning($"[Card] Not enough population. Need: {card.CardData.populationCost}, Current: {PopulationManager.Instance.CurrentPopulation}/{PopulationManager.Instance.MaxPopulation}");
                return false;
            }

            // 移除卡牌
            PlayerHandManager.Instance?.RemoveCard(card.CardData);

            // 生成士兵
            SpawnSoldiers(card.CardData, worldPos);

            Debug.Log($"[Card] ✅ Card placed successfully at: {worldPos}");
            return true;
        }

        private bool IsInForbiddenArea(Vector3 worldPos)
        {
            if (forbiddenArea == null)
            {
                Debug.Log("[Card] ForbiddenArea not configured - all areas are valid");
                return false;
            }

            Collider[] colliders = forbiddenArea.GetComponentsInChildren<Collider>();
            
            if (colliders.Length == 0)
            {
                Debug.LogWarning("[Card] ForbiddenArea has no Colliders - cannot detect forbidden zones");
                return false;
            }

            Debug.Log($"[Card] Checking forbidden area at position: {worldPos}");
            Debug.Log($"[Card] Found {colliders.Length} colliders in ForbiddenArea");

            foreach (var collider in colliders)
            {
                Vector3 closestPoint = collider.ClosestPoint(worldPos);
                float distance = Vector3.Distance(worldPos, closestPoint);

                Debug.Log($"[Card] Collider '{collider.name}': distance = {distance:F3}");

                if (distance < 0.01f)
                {
                    Debug.LogWarning($"[Card] ❌ Position is in forbidden area (collider: {collider.name})");
                    return true;
                }
            }

            Debug.Log("[Card] ✅ Position is NOT in forbidden area");
            return false;
        }

        private void SpawnSoldiers(CardData cardData, Vector3 centerPosition)
        {
            if (cardData == null) return;

            int soldierCount = cardData.soldierCount;
            float spawnRadius = cardData.spawnRadius;

            ClusterSpawnSystem.SpawnCluster(centerPosition, soldierCount, spawnRadius, 2f, cardData.soldierIndex, SideType.PlayerSide, BrainType.SoldierAI);
            //SoldierFactory.ShowSoldier(cardData.soldierIndex,)
            
            /*
            if (soldierCount == 1)
            {
                SpawnSingleSoldier(cardData, centerPosition);
            }
            else if (soldierCount == 2)
            {
                SpawnSoldiersInLine(cardData, centerPosition, soldierCount, spawnRadius);
            }
            else if (soldierCount <= 5)
            {
                SpawnSoldiersInCircle(cardData, centerPosition, soldierCount, spawnRadius);
            }
            else
            {
                SpawnSoldiersInGrid(cardData, centerPosition, soldierCount, spawnRadius);
            }*/

            Debug.Log($"[Card] Spawned {soldierCount} soldiers: {cardData.soldierName}");
        }

        /*
        private void SpawnSingleSoldier(CardData cardData, Vector3 position)
        {
            GameObject soldier = Instantiate(cardData.soldierPrefab, position, Quaternion.identity);
            soldier.name = $"{cardData.soldierName}_1";
        }


        private void SpawnSoldiersInLine(CardData cardData, Vector3 centerPosition, int count, float spacing)
        {
            float totalWidth = (count - 1) * spacing;
            float startX = centerPosition.x - totalWidth / 2f;

            for (int i = 0; i < count; i++)
            {
                Vector3 position = new Vector3(startX + i * spacing, centerPosition.y, centerPosition.z);
                GameObject soldier = Instantiate(cardData.soldierPrefab, position, Quaternion.identity);
                soldier.name = $"{cardData.soldierName}_{i + 1}";
            }
        }

        private void SpawnSoldiersInCircle(CardData cardData, Vector3 centerPosition, int count, float radius)
        {
            float angleStep = 360f / count;

            for (int i = 0; i < count; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                Vector3 position = centerPosition + offset;
                Quaternion rotation = Quaternion.LookRotation(centerPosition - position);

                GameObject soldier = Instantiate(cardData.soldierPrefab, position, rotation);
                soldier.name = $"{cardData.soldierName}_{i + 1}";
            }
        }

        private void SpawnSoldiersInGrid(CardData cardData, Vector3 centerPosition, int count, float spacing)
        {
            int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
            int rows = Mathf.CeilToInt((float)count / cols);

            float gridWidth = (cols - 1) * spacing;
            float gridHeight = (rows - 1) * spacing;

            Vector3 startPos = centerPosition - new Vector3(gridWidth / 2f, 0, gridHeight / 2f);

            int soldierIndex = 0;
            for (int row = 0; row < rows && soldierIndex < count; row++)
            {
                for (int col = 0; col < cols && soldierIndex < count; col++)
                {
                    Vector3 position = startPos + new Vector3(col * spacing, 0, row * spacing);
                    GameObject soldier = Instantiate(cardData.soldierPrefab, position, Quaternion.identity);
                    soldier.name = $"{cardData.soldierName}_{soldierIndex + 1}";
                    soldierIndex++;
                }
            }
        }*/

        #endregion

        #region 事件回调

        private int lastHandCount = 0;
        private bool isAddingCards = false;

        private void OnHandChanged(List<CardData> cards)
        {
            int currentCount = cards.Count;
            Debug.Log($"[Card] OnHandChanged: currentCount={currentCount}, lastHandCount={lastHandCount}");

            if (currentCount == 0)
            {
                Debug.Log("[Card] Clearing hand UI (count is 0)");
                RefreshHandUI(playAnimation: false);
                lastHandCount = 0;
            }
            else if (currentCount == PlayerHandManager.Instance.MaxHandSize && lastHandCount == 0)
            {
                Debug.Log($"[Card] Redrawing full hand with animation (count={currentCount})");
                RefreshHandUI(playAnimation: true);
                lastHandCount = currentCount;
            }
            else if (currentCount > lastHandCount && !isAddingCards)
            {
                Debug.Log($"[Card] Adding cards incrementally (from {lastHandCount} to {currentCount})");
                isAddingCards = true;
                StartCoroutine(AddCardsIncremental(cards, lastHandCount, currentCount));
            }
            else if (currentCount < lastHandCount)
            {
                Debug.Log($"[Card] Removing cards (from {lastHandCount} to {currentCount})");
                RefreshHandUI(playAnimation: false);
                lastHandCount = currentCount;
            }
            else if (!isAddingCards)
            {
                Debug.Log($"[Card] Refreshing hand UI (count unchanged: {currentCount})");
                RefreshHandUI(playAnimation: false);
                lastHandCount = currentCount;
            }
        }

        private IEnumerator AddCardsIncremental(List<CardData> cards, int startIndex, int endCount)
        {
            yield return null;

            for (int i = startIndex; i < endCount; i++)
            {
                if (i < cards.Count)
                {
                    CreateCardUI(cards[i], playAnimation: true);
                }
            }

            lastHandCount = endCount;
            isAddingCards = false;
        }

        private void OnPopulationChanged(int current, int max)
        {
            UpdatePopulationUI();

            foreach (var cardUI in handCardUIs)
            {
                cardUI?.UpdateDragability();
            }
        }

        #endregion

        void OnDestroy()
        {
            if (PlayerHandManager.Instance != null)
            {
                PlayerHandManager.Instance.OnHandChanged -= OnHandChanged;
            }

            if (PopulationManager.Instance != null)
            {
                PopulationManager.Instance.OnPopulationChanged -= OnPopulationChanged;
            }

            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
