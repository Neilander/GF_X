using System.Collections.Generic;
using AAAGame.MiniMap.FOG3;
using UnityEngine;
using System;
using UnityGameFramework.Runtime;

namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌系统主控制器
    /// 负责协调各个子控制器和模型
    /// </summary>
    public class CardSystemController
    {
        private const string DiscardResourceConversionRateConfigKey = "DiscardResourceConversionRate";

        private sealed class Card
        {
            public ICardDataProvider CardData { get; }
            public BuildingEntity SourceBuilding { get; }

            public Card(ICardDataProvider cardData, BuildingEntity sourceBuilding)
            {
                CardData = cardData;
                SourceBuilding = sourceBuilding;
            }
        }

        public sealed class DeckPreviewCard
        {
            public ICardDataProvider CardData { get; }
            public BuildingEntity SourceBuilding { get; }

            internal DeckPreviewCard(ICardDataProvider cardData, BuildingEntity sourceBuilding)
            {
                CardData = cardData;
                SourceBuilding = sourceBuilding;
            }
        }

        private PlayerHandModel m_HandModel;
        private HandCardController m_HandCardController;
        private CardPlacementController m_PlacementController;
        private AreaDetectionController m_AreaDetectionController;
        private EnemyBuildingForbiddenZoneController m_EnemyBuildingForbiddenZoneController;
        private bool m_IsIngameValueSubscribed;

        private List<ICardDataProvider> m_CardPool;
        private readonly List<Card> m_DeckCards = new List<Card>();
        private readonly List<ICardDataProvider> m_OwnedPlaceableCardProviders = new List<ICardDataProvider>();
        private readonly HashSet<string> m_OwnedPlaceableCardProviderKeys = new HashSet<string>();

        // 事件回调
        public event Action<int, int> OnHandChanged; // (cardCount, maxCards)
        public event Action<CardModel> OnCardDrawn;
        public event Action<CardModel> OnCardPlayed;
        public event Action<CardModel> OnCardDiscarded;

        /// <summary>
        /// 初始化
        /// </summary>
        public void Initialize()
        {
            // 初始化模型
            m_HandModel = new PlayerHandModel();

            // 初始化控制器
            m_HandCardController = new HandCardController(m_HandModel);
            m_PlacementController = new CardPlacementController();
            m_AreaDetectionController = new AreaDetectionController();
            m_EnemyBuildingForbiddenZoneController = new EnemyBuildingForbiddenZoneController();
            m_PlacementController.SetAdditionalForbiddenChecker((position, radius) =>
                IsBlockedByConfiguredInvalidArea(position, radius)
                || (m_EnemyBuildingForbiddenZoneController != null
                    && m_EnemyBuildingForbiddenZoneController.IsPositionBlocked(position, radius)));

            // 订阅子控制器事件
            m_HandCardController.OnCardDrawn += (card) =>
            {
                OnCardDrawn?.Invoke(card);

                if (card != null)
                {
                    GameFramework.Event.GameEventArgs cardEvent = CardDrawnEventArgs.Create(card);
                    GF.Event.Fire(this, cardEvent);
                }
            };
            m_HandCardController.OnCardRemoved += (card) => OnHandChanged?.Invoke(m_HandModel.CardCount, m_HandModel.MaxCards);

            // 初始化卡牌池
            m_CardPool = new List<ICardDataProvider>();
            m_DeckCards.Clear();


            Debug.Log("[Card] CardSystemController initialized.");
        }

        /// <summary>
        /// 设置卡牌池
        /// </summary>
        public void SetCardPool(List<ICardDataProvider> cardPool)
        {
            if (cardPool == null)
            {
                Debug.LogError("[Card] Card pool is null.");
                return;
            }

            m_CardPool = cardPool;
            Debug.Log($"[Card] Card pool set with {m_CardPool.Count} cards.");
        }

        /// <summary>
        /// 重置为空卡组和空手牌。
        /// </summary>
        public void ResetDeckAndHand()
        {
            m_DeckCards.Clear();
            m_OwnedPlaceableCardProviders.Clear();
            m_OwnedPlaceableCardProviderKeys.Clear();
            m_HandModel?.Clear();
            OnHandChanged?.Invoke(m_HandModel != null ? m_HandModel.CardCount : 0, m_HandModel != null ? m_HandModel.MaxCards : CardConst.MaxHandCards);
        }

        /// <summary>
        /// 按单位类型向卡组加入一张卡，并记录来源建筑。
        /// </summary>
        public bool AddCardToDeck(BuildingEntity sourceBuilding)
        {
            if (sourceBuilding == null || sourceBuilding.buildingData == null)
            {
                Debug.LogWarning("[Card] Skip adding card: source building is invalid.");
                return false;
            }

            if (sourceBuilding.GetArmyForce() <= 0)
            {
                Debug.LogWarning($"[Card] Skip army building '{sourceBuilding.buildingData.Identifier}': army force is 0.");
                return false;
            }

            // 每个部队建筑生成对应的卡牌
            if (!UnitTypeHelper.TryParseUnitType(sourceBuilding.buildingData.UnitID, out var unitType))
            {
                Debug.LogWarning($"Skip army building '{sourceBuilding.buildingData.Identifier}': invalid UnitID '{sourceBuilding.buildingData.UnitID}'.");
                return false;
            }

            ICardDataProvider cardData = FindCardDataByUnitType(unitType);
            if (cardData == null)
            {
                Debug.LogWarning($"[Card] No card configured for unit type '{unitType}'.");
                return false;
            }

            m_DeckCards.Add(new Card(cardData, sourceBuilding));
            RememberOwnedPlaceableCard(cardData);
            Log.Info($"[CardGame] 卡牌入组: unitType={unitType}, source={sourceBuilding?.BuildingInstanceId ?? "None"}");
            return true;
        }

        /// <summary>
        /// 直接按 CardData 向卡组加入一张卡，主要供调试和 Inspector 测试使用。
        /// </summary>
        public bool AddCardToDeck(CardData cardData)
        {
            if (cardData == null)
            {
                Debug.LogWarning("[Card] CardData is null, cannot add to deck.");
                return false;
            }

            var provider = new CardDataAdapter(cardData);
            m_DeckCards.Add(new Card(provider, null));
            RememberOwnedPlaceableCard(provider);
            Log.Info($"[CardGame] 调试卡牌入组: cardId={provider.CardId}, soldierIndex={provider.SoldierIndex}");
            return true;
        }

        /// <summary>
        /// 若手牌未满且卡组非空，自动抽一张。
        /// </summary>
        public bool TryAutoDrawOneCardFromDeck()
        {
            if (m_HandModel == null || m_HandModel.IsFull)
            {
                return false;
            }

            if (m_DeckCards.Count <= 0)
            {
                return false;
            }

            return DrawCard();
        }

        /// <summary>
        /// 获取当前玩家拥有、可进入抽牌和放置流程的卡牌类型。
        /// 这里合并运行时卡组与当前手牌，用于展示玩家可用牌池。
        /// </summary>
        public List<ICardDataProvider> GetOwnedPlaceableCardProviders()
        {
            List<ICardDataProvider> result = new List<ICardDataProvider>();
            HashSet<string> addedKeys = new HashSet<string>();

            AddOwnedProviders(result, addedKeys);
            AddDeckProviders(result, addedKeys);
            AddHandProviders(result, addedKeys);

            return result;
        }

        public List<DeckPreviewCard> GetOrderedDeckPreviewCards()
        {
            List<DeckPreviewCard> result = new List<DeckPreviewCard>();
            List<int> orderedIndices = GetDeckCardIndicesByDrawPriority();

            for (int i = 0; i < orderedIndices.Count; i++)
            {
                int deckIndex = orderedIndices[i];
                if (deckIndex < 0 || deckIndex >= m_DeckCards.Count)
                {
                    continue;
                }

                Card entry = m_DeckCards[deckIndex];
                if (entry == null || entry.CardData == null)
                {
                    continue;
                }

                result.Add(new DeckPreviewCard(entry.CardData, entry.SourceBuilding));
            }

            return result;
        }

        private void AddOwnedProviders(List<ICardDataProvider> result, HashSet<string> addedKeys)
        {
            for (int i = 0; i < m_OwnedPlaceableCardProviders.Count; i++)
            {
                AddUniqueProvider(result, addedKeys, m_OwnedPlaceableCardProviders[i]);
            }
        }

        private void AddDeckProviders(List<ICardDataProvider> result, HashSet<string> addedKeys)
        {
            for (int i = 0; i < m_DeckCards.Count; i++)
            {
                Card entry = m_DeckCards[i];
                AddUniqueProvider(result, addedKeys, entry != null ? entry.CardData : null);
            }
        }

        private void AddHandProviders(List<ICardDataProvider> result, HashSet<string> addedKeys)
        {
            if (m_HandModel == null)
            {
                return;
            }

            List<CardModel> handCards = m_HandModel.GetAllCards();
            for (int i = 0; i < handCards.Count; i++)
            {
                CardModel cardModel = handCards[i];
                AddUniqueProvider(result, addedKeys, cardModel != null ? cardModel.DataProvider : null);
            }
        }

        private static void AddUniqueProvider(List<ICardDataProvider> result, HashSet<string> addedKeys, ICardDataProvider provider)
        {
            if (provider == null)
            {
                return;
            }

            string key = GetProviderKey(provider);

            if (addedKeys.Add(key))
            {
                result.Add(provider);
            }
        }

        private void RememberOwnedPlaceableCard(ICardDataProvider provider)
        {
            if (provider == null)
            {
                return;
            }

            if (m_OwnedPlaceableCardProviderKeys.Add(GetProviderKey(provider)))
            {
                m_OwnedPlaceableCardProviders.Add(provider);
            }
        }

        private static string GetProviderKey(ICardDataProvider provider)
        {
            string cardId = provider.CardId;
            return !string.IsNullOrWhiteSpace(cardId)
                ? cardId
                : $"{provider.CardName}_{provider.SoldierIndex}";
        }

        /// <summary>
        /// 设置区域对象
        /// </summary>
        public void SetAreaObjects(GameObject validAreaObject, GameObject invalidAreaObject)
        {
            m_AreaDetectionController.SetAreaObjects(validAreaObject, invalidAreaObject);
        }

        /// <summary>
        /// 抽取卡牌
        /// </summary>
        public void DrawCards(int count)
        {
            for (int i = 0; i < count; i++)
            {
                DrawCard();
            }
        }

        /// <summary>
        /// 抽取单张卡牌
        /// </summary>
        public bool DrawCard()
        {
            if (m_HandModel.IsFull)
            {
                Debug.LogWarning("[Card] Hand is full, cannot draw more cards.");
                return false;
            }

            if (m_DeckCards.Count <= 0)
            {
                Debug.LogWarning("[Card] Deck is empty, cannot draw a card.");
                return false;
            }

            Card entry = SelectNearestDeckCard();
            if (entry == null || entry.CardData == null)
            {
                Debug.LogWarning("[Card] Failed to select a card from deck.");
                return false;
            }

            m_DeckCards.Remove(entry);

            bool success = m_HandCardController.DrawCard(entry.CardData, entry.SourceBuilding);
            if (success)
            {
                OnHandChanged?.Invoke(m_HandModel.CardCount, m_HandModel.MaxCards);
            }
            else
            {
                m_DeckCards.Insert(0, entry);
            }

            return success;
        }

        /// <summary>
        /// 根据单位类型查找卡牌模板。
        /// </summary>
        private ICardDataProvider FindCardDataByUnitType(UnitType unitType)
        {
            if (m_CardPool == null)
            {
                return null;
            }

            foreach (var card in m_CardPool)
            {
                if (card != null && card.SoldierIndex == unitType)
                {
                    return card;
                }
            }

            return null;
        }

        /// <summary>
        /// 从卡组中选择与当前英雄距离最近的卡。
        /// </summary>
        private Card SelectNearestDeckCard()
        {
            if (m_DeckCards.Count <= 0)
            {
                return null;
            }

            List<int> orderedIndices = GetDeckCardIndicesByDrawPriority();
            if (orderedIndices.Count <= 0)
            {
                return m_DeckCards[0];
            }

            int bestIndex = orderedIndices[0];
            return bestIndex >= 0 && bestIndex < m_DeckCards.Count
                ? m_DeckCards[bestIndex]
                : m_DeckCards[0];
        }

        private List<int> GetDeckCardIndicesByDrawPriority()
        {
            List<int> indices = new List<int>(m_DeckCards.Count);

            for (int i = 0; i < m_DeckCards.Count; i++)
            {
                indices.Add(i);
            }

            if (!TryGetHeroPosition(out var heroPosition))
            {
                return indices;
            }

            indices.Sort((leftIndex, rightIndex) =>
            {
                Card left = leftIndex >= 0 && leftIndex < m_DeckCards.Count ? m_DeckCards[leftIndex] : null;
                Card right = rightIndex >= 0 && rightIndex < m_DeckCards.Count ? m_DeckCards[rightIndex] : null;

                float leftDistanceSqr = GetDeckCardDistanceSqr(left, heroPosition);
                float rightDistanceSqr = GetDeckCardDistanceSqr(right, heroPosition);
                int compare = leftDistanceSqr.CompareTo(rightDistanceSqr);
                if (compare != 0)
                {
                    return compare;
                }

                return leftIndex.CompareTo(rightIndex);
            });

            return indices;
        }

        private static float GetDeckCardDistanceSqr(Card entry, Vector3 heroPosition)
        {
            if (entry == null || entry.SourceBuilding == null)
            {
                return float.MaxValue;
            }

            return (entry.SourceBuilding.transform.position - heroPosition).sqrMagnitude;
        }

        private static bool TryGetHeroPosition(out Vector3 heroPosition)
        {
            if (EntityRegistry.Player != null)
            {
                heroPosition = EntityRegistry.Player.Position;
                return true;
            }

            heroPosition = Vector3.zero;
            return false;
        }

        /// <summary>
        /// 打出卡牌
        /// </summary>
        public bool PlayCard(CardModel cardModel)
        {
            if (cardModel == null)
            {
                Debug.LogError("[Card] CardModel is null.");
                return false;
            }

            int occupiedSupply = cardModel.GetOccupiedSupply();
            if (!HasEnoughPopulation(occupiedSupply))
            {
                Debug.LogWarning($"[Card] Cannot play card {cardModel.GetCardName()}: not enough population.");
                return false;
            }

            // 开始放置流程
            StartPlacement(cardModel);
            return true;
        }

        /// <summary>
        /// 开始放置卡牌
        /// </summary>
        public void StartPlacement(CardModel cardModel)
        {
            if (cardModel == null)
            {
                Debug.LogError("[Card] CardModel is null.");
                return;
            }

            float previewRadius = 0.5f;
            if (cardModel.DataProvider != null)
            {
                previewRadius = Mathf.Max(previewRadius, cardModel.DataProvider.SpawnRadius);
            }

            m_PlacementController.SetDetectionRadius(previewRadius);
            m_PlacementController.StartPlacement(cardModel);
            m_EnemyBuildingForbiddenZoneController?.BeginPlacement();
        }

        /// <summary>
        /// 更新放置（每帧调用）
        /// </summary>
        public void UpdatePlacement()
        {
            m_EnemyBuildingForbiddenZoneController?.RefreshZones();
            m_PlacementController.UpdatePlacement();
        }

        /// <summary>
        /// 确认放置
        /// </summary>
        public bool ConfirmPlacement(CardModel cardModel, Vector2? releaseScreenPosition = null)
        {
            if (cardModel == null)
            {
                Debug.LogError("[Card] Cannot confirm placement: CardModel is null.");
                return false;
            }

            if (m_HandModel == null || !m_HandModel.Contains(cardModel))
            {
                Debug.LogWarning($"[Card] Cannot confirm placement: card is no longer in hand. card={cardModel.GetCardName()}");
                return false;
            }

            int occupiedSupply = cardModel != null ? cardModel.GetOccupiedSupply() : 0;
            if (!HasEnoughPopulation(occupiedSupply))
            {
                Debug.LogWarning("[Card] Cannot confirm placement: not enough population.");
                return false;
            }

            if (!m_PlacementController.ConfirmPlacement(cardModel, releaseScreenPosition))
            {
                return false;
            }

            m_EnemyBuildingForbiddenZoneController?.EndPlacement();

            InGameDataModel.RefreshCurrentSupplyFromFriendlyUnits(true);

            // 从手牌移除
            m_HandCardController.RemoveCard(cardModel);

            // 触发卡牌打出事件（C# 事件）
            OnCardPlayed?.Invoke(cardModel);

            // UI 需要在当前帧立刻移除手牌，避免事件队列晚一帧导致残留可交互卡牌。
            GameFramework.Event.GameEventArgs cardEvent = CardPlayedEventArgs.Create(cardModel);
            GF.Event.FireNow(this, cardEvent);

            Debug.Log($"[Card] Card played: {cardModel.GetCardName()}, Population: {InGameDataModel.GetCurrentSupply()}/{InGameDataModel.GetMaxSupply()}");
            return true;
        }

        /// <summary>
        /// 取消放置
        /// </summary>
        public void CancelPlacement()
        {
            m_PlacementController.CancelPlacement();
            m_EnemyBuildingForbiddenZoneController?.EndPlacement();
        }

        /// <summary>
        /// 丢弃卡牌
        /// </summary>
        public bool DiscardCard(CardModel cardModel, Vector2? discardScreenPosition = null)
        {
            if (cardModel == null)
            {
                Debug.LogError("[Card] CardModel is null.");
                return false;
            }

            // 从手牌移除
            if (!m_HandCardController.RemoveCard(cardModel))
            {
                return false;
            }

            ApplyDiscardResourceReward(cardModel, discardScreenPosition);

            // 触发丢弃事件（C# 委托 + GF.Event）
            OnCardDiscarded?.Invoke(cardModel);

            var cardEvent = CardDiscardedEventArgs.Create(cardModel);
            GF.Event.Fire(this, cardEvent);
            Debug.Log($"[DISCARD-BUFF] CardSystemController.DiscardCard 已 Fire CardDiscardedEventArgs");

            return true;
        }

        private void ApplyDiscardResourceReward(CardModel cardModel, Vector2? discardScreenPosition)
        {
            if (GF.Config == null)
            {
                Log.Error("[Card] Discard reward skipped: GF.Config is not ready.");
                return;
            }

            int occupiedSupply = Mathf.Max(0, cardModel.GetOccupiedSupply());
            int conversionRate = GF.Config.GetInt(DiscardResourceConversionRateConfigKey);
            if (conversionRate <= 0)
            {
                Log.Error("[Card] Discard reward config invalid. key={0}, value={1}", DiscardResourceConversionRateConfigKey, conversionRate);
                return;
            }

            int gainedCoin = occupiedSupply / conversionRate;
            Log.Info("[Card] Discard reward calc. card={0}, occupiedSupply={1}, rate={2}, gainedCoin={3}",
                cardModel.GetCardName(), occupiedSupply, conversionRate, gainedCoin);

            if (gainedCoin <= 0)
                return;

            RewardManager.HandleCardDiscardReward(cardModel, gainedCoin, discardScreenPosition);
        }


        /// <summary>
        /// 获取手牌模型
        /// </summary>
        public PlayerHandModel GetHandModel()
        {
            return m_HandModel;
        }

        /// <summary>
        /// 获取放置控制器
        /// </summary>
        public CardPlacementController GetPlacementController()
        {
            return m_PlacementController;
        }

        /// <summary>
        /// 获取当前屏幕点对应的放置预览结果。
        /// </summary>
        public bool TryGetPlacementPreview(Vector2 screenPosition, out Vector3 worldPosition, out bool isValid)
        {
            return TryGetPlacementPreview(screenPosition, out worldPosition, out isValid, null);
        }

        /// <summary>
        /// 获取当前屏幕点对应的放置预览结果，并返回预生成士兵点位。
        /// </summary>
        public bool TryGetPlacementPreview(Vector2 screenPosition, out Vector3 worldPosition, out bool isValid, List<Vector3> previewSpawnPositions)
        {
            worldPosition = Vector3.zero;
            isValid = false;
            previewSpawnPositions?.Clear();

            if (m_PlacementController == null)
            {
                return false;
            }

            return m_PlacementController.TryGetPlacementPreview(screenPosition, out worldPosition, out isValid, previewSpawnPositions);
        }

        /// <summary>
        /// 获取当前放置检测半径。
        /// </summary>
        public float GetCurrentPlacementRadius()
        {
            return m_PlacementController != null ? m_PlacementController.GetDetectionRadius() : 0.5f;
        }

        /// <summary>
        /// 获取区域检测控制器
        /// </summary>
        public AreaDetectionController GetAreaDetectionController()
        {
            return m_AreaDetectionController;
        }

        /// <summary>
        /// 检查位置是否在禁止区域
        /// </summary>
        public bool IsInForbiddenArea(Vector3 worldPosition)
        {
            bool inStaticForbiddenArea = IsBlockedByConfiguredInvalidArea(worldPosition, 0f);
            bool inEnemyBuildingForbiddenArea = m_EnemyBuildingForbiddenZoneController != null
                && m_EnemyBuildingForbiddenZoneController.IsPositionBlocked(worldPosition, 0f);
            bool inInvisibleFogArea = !IsPositionInVisibleArea(worldPosition);
            return inStaticForbiddenArea || inEnemyBuildingForbiddenArea || inInvisibleFogArea;
        }

        private bool IsBlockedByConfiguredInvalidArea(Vector3 worldPosition, float radius)
        {
            return m_AreaDetectionController != null
                && m_AreaDetectionController.IsPositionBlockedByInvalidArea(worldPosition, radius);
        }

        private static bool IsPositionInVisibleArea(Vector3 worldPosition)
        {
            Fog3Manager fogManager = Fog3Manager.Instance;
            if (fogManager == null || !fogManager.IsInitialized || fogManager.MapData == null)
            {
                return false;
            }

            return fogManager.IsPositionVisible(worldPosition);
        }

        /// <summary>
        /// 清理
        /// </summary>
        public void Shutdown()
        {
            m_PlacementController?.Shutdown();
            m_EnemyBuildingForbiddenZoneController?.Shutdown();
            m_AreaDetectionController?.Shutdown();
            m_HandModel?.Clear();
            m_CardPool?.Clear();
            m_DeckCards.Clear();
            m_OwnedPlaceableCardProviders.Clear();
            m_OwnedPlaceableCardProviderKeys.Clear();

            Debug.Log("[Card] CardSystemController shutdown.");
        }


        private bool HasEnoughPopulation(int requiredPopulation)
        {
            if (requiredPopulation <= 0)
                return true;

            return InGameDataModel.HasEnoughSupplyFor(requiredPopulation);
        }
    }
}
