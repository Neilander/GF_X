using System.Collections.Generic;
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
            public string SourceBuildingInstanceId { get; }

            public Card(ICardDataProvider cardData, IBuildingLogicContext sourceBuilding)
            {
                CardData = cardData;
                SourceBuilding = ResolveBuildingView(sourceBuilding);
                SourceBuildingInstanceId = sourceBuilding != null
                    ? sourceBuilding.BuildingInstanceId
                    : string.Empty;
            }

            private static BuildingEntity ResolveBuildingView(IBuildingLogicContext sourceBuilding)
            {
                if (sourceBuilding == null)
                    return null;
                if (sourceBuilding is BuildingEntity view)
                    return view;
                if (LogicEntityLifecycleService.TryGetBoundView(sourceBuilding.LogicEntityId, out MAEntity boundView))
                    return boundView as BuildingEntity;
                return null;
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
        private EnemyBuildingForbiddenZoneController m_EnemyBuildingForbiddenZoneController;
        private bool m_IsIngameValueSubscribed;

        private List<ICardDataProvider> m_CardPool;
        private readonly List<Card> m_DeckCards = new List<Card>();
        private readonly List<ICardDataProvider> m_OwnedPlaceableCardProviders = new List<ICardDataProvider>();
        private readonly HashSet<string> m_OwnedPlaceableCardProviderKeys = new HashSet<string>();
        private ulong m_LastCardRuntimeId;

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
            m_HandModel = new PlayerHandModel(LevelTagRuntime.ModifyMaxHandCards(CardConst.MaxHandCards));

            // 初始化控制器
            m_HandCardController = new HandCardController(m_HandModel);
            m_PlacementController = new CardPlacementController();
            m_EnemyBuildingForbiddenZoneController = new EnemyBuildingForbiddenZoneController();

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
            m_LastCardRuntimeId = 0;


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
            m_LastCardRuntimeId = 0;
            m_OwnedPlaceableCardProviders.Clear();
            m_OwnedPlaceableCardProviderKeys.Clear();
            m_HandModel?.Clear();
            OnHandChanged?.Invoke(m_HandModel != null ? m_HandModel.CardCount : 0, m_HandModel != null ? m_HandModel.MaxCards : CardConst.MaxHandCards);
        }

        /// <summary>
        /// 按单位类型向卡组加入一张卡，并记录来源建筑。
        /// </summary>
        public bool AddCardToDeck(IBuildingLogicContext sourceBuilding)
        {
            if (sourceBuilding == null || sourceBuilding.BuildingData == null)
            {
                throw new ArgumentException("Card source logic building is invalid.", nameof(sourceBuilding));
            }

            if (sourceBuilding.GetArmyForce() <= 0)
            {
                Debug.LogWarning($"[Card] Skip army building '{sourceBuilding.BuildingData.Identifier}': army force is 0.");
                return false;
            }

            // 每个部队建筑生成对应的卡牌
            if (!UnitTypeHelper.TryParseUnitType(sourceBuilding.BuildingData.UnitID, out var unitType))
            {
                Debug.LogWarning($"Skip army building '{sourceBuilding.BuildingData.Identifier}': invalid UnitID '{sourceBuilding.BuildingData.UnitID}'.");
                return false;
            }

            int lv = sourceBuilding.BuildingData.Lv;
            ICardDataProvider cardData = FindCardData(unitType, lv);
            if (cardData == null)
            {
                Debug.LogWarning($"[Card] No card configured for unit type '{unitType}' lv={lv}.");
                return false;
            }

            m_DeckCards.Add(new Card(cardData, sourceBuilding));
            RememberOwnedPlaceableCard(cardData);
            Log.Info($"[CardGame] 卡牌入组: unitType={unitType}, lv={lv}, source={sourceBuilding.BuildingInstanceId}");
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

            ulong runtimeId = checked(m_LastCardRuntimeId + 1);
            bool success = m_HandCardController.DrawCard(
                runtimeId,
                entry.CardData,
                entry.SourceBuildingInstanceId,
                entry.SourceBuilding);
            if (success)
            {
                m_LastCardRuntimeId = runtimeId;
                OnHandChanged?.Invoke(m_HandModel.CardCount, m_HandModel.MaxCards);
            }
            else
            {
                m_DeckCards.Insert(0, entry);
            }

            return success;
        }

        /// <summary>
        /// 根据单位类型 + 建筑等级查找卡牌模板。
        /// 优先精确匹配 (unitType, lv)；如果没找到，回退到该 unitType 下任意 lv 的第一张卡（避免完全没卡）。
        /// </summary>
        private ICardDataProvider FindCardData(UnitType unitType, int lv)
        {
            if (m_CardPool == null)
            {
                return null;
            }

            ICardDataProvider fallback = null;
            foreach (var card in m_CardPool)
            {
                if (card == null || card.SoldierIndex != unitType)
                {
                    continue;
                }

                if (card.RequiredLv == lv)
                {
                    return card; // 精确匹配
                }

                if (fallback == null)
                {
                    fallback = card; // 暂存同 unitType 的回退
                }
            }

            if (fallback != null)
            {
                Debug.LogWarning($"[Card] Lv={lv} 没找到对应卡，回退到 unitType={unitType} 的第一张 (lv={fallback.RequiredLv})");
            }
            return fallback;
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

            if (!TryGetHeroPositionFixed(out FixVector2 heroPosition))
            {
                return indices;
            }

            indices.Sort((leftIndex, rightIndex) =>
            {
                Card left = leftIndex >= 0 && leftIndex < m_DeckCards.Count ? m_DeckCards[leftIndex] : null;
                Card right = rightIndex >= 0 && rightIndex < m_DeckCards.Count ? m_DeckCards[rightIndex] : null;

                Fix64 leftDistanceSqr = GetDeckCardDistanceSquaredFixed(left, heroPosition);
                Fix64 rightDistanceSqr = GetDeckCardDistanceSquaredFixed(right, heroPosition);
                int compare = leftDistanceSqr.CompareTo(rightDistanceSqr);
                if (compare != 0)
                {
                    return compare;
                }

                return leftIndex.CompareTo(rightIndex);
            });

            return indices;
        }

        private static Fix64 GetDeckCardDistanceSquaredFixed(Card entry, FixVector2 heroPosition)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.SourceBuildingInstanceId))
                return Fix64.FromRaw(long.MaxValue);

            IBuildingLogicContext source = LogicBuildingQueryService.GetRequiredByInstanceId(
                entry.SourceBuildingInstanceId);
            return FixVector2.SqrMagnitude(source.PositionFixed - heroPosition);
        }

        private static bool TryGetHeroPositionFixed(out FixVector2 heroPosition)
        {
            if (EntityRegistry.Player != null)
            {
                heroPosition = EntityRegistry.Player.PositionFixed;
                return true;
            }

            heroPosition = FixVector2.Zero;
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

            float previewRadius = ClusterSpawnSystem.CalculateAutoSpawnRadius(cardModel.GetTroopCount());

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

            if (!m_PlacementController.TryCreatePlayCommandPayload(
                    cardModel,
                    releaseScreenPosition,
                    out FixVector2 selectedPosition))
            {
                return false;
            }

            LogicCardCommandService.SchedulePlayForNextFrame(cardModel.RuntimeId, selectedPosition);
            m_EnemyBuildingForbiddenZoneController?.EndPlacement();
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

            if (!m_HandModel.Contains(cardModel))
                return false;
            LogicCardCommandService.ScheduleDiscardForNextFrame(cardModel.RuntimeId);
            return true;
        }

        public void ApplyLogicCardCommand(LogicCardCommand command)
        {
            if (!LogicCardCommandService.IsApplyingFrame)
                throw new InvalidOperationException("Card commands may only be applied by LogicCardCommandService.");
            CardModel cardModel = m_HandModel.GetRequiredByRuntimeId(command.CardRuntimeId);
            switch (command.Kind)
            {
                case LogicCardCommandKind.Play:
                    ApplyPlayCommand(cardModel, command.SelectedPosition);
                    break;
                case LogicCardCommandKind.Discard:
                    ApplyDiscardCommand(cardModel);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown card command kind.");
            }
        }

        private void ApplyPlayCommand(CardModel cardModel, FixVector2 selectedPosition)
        {
            int occupiedSupply = cardModel.GetOccupiedSupply();
            if (!HasEnoughPopulation(occupiedSupply))
            {
                Log.Warning("[Card] Play command rejected at logic frame: insufficient supply. card={0}, runtimeId={1}.",
                    cardModel.GetCardId(), cardModel.RuntimeId);
                return;
            }

            ICardDataProvider dataProvider = cardModel.DataProvider
                                             ?? throw new InvalidOperationException($"Card {cardModel.RuntimeId} has no data provider.");
            int soldierCount = cardModel.GetTroopCount();
            if (soldierCount <= 0)
                throw new InvalidOperationException($"Card {cardModel.RuntimeId} resolved a non-positive soldier count {soldierCount}.");
            string sourceBuildingInstanceId = cardModel.GetSourceBuildingInstanceId();
            if (string.IsNullOrWhiteSpace(sourceBuildingInstanceId))
                sourceBuildingInstanceId = null;

            Fix64 spawnRadius = ClusterSpawnSystem.CalculateAutoSpawnRadiusFixed(soldierCount);
            LogicCardPlacementInvalidReason placementReason = LogicCardPlacementAuthority.Evaluate(
                selectedPosition,
                spawnRadius);
            if (placementReason != LogicCardPlacementInvalidReason.None)
            {
                Log.Warning(
                    "[Card] Play command rejected at logic frame: placement permission changed. card={0}, runtimeId={1}, reason={2}, xRaw={3}, yRaw={4}.",
                    cardModel.GetCardId(),
                    cardModel.RuntimeId,
                    placementReason,
                    selectedPosition.x.RawValue,
                    selectedPosition.y.RawValue);
                return;
            }

            bool spawned = ClusterSpawnSystem.SpawnClusterFixed(
                selectedPosition,
                soldierCount,
                spawnRadius,
                (Fix64)2,
                dataProvider.SoldierIndex,
                SideType.PlayerSide,
                BrainType.SoldierAI,
                sourceBuildingInstanceId,
                null,
                true,
                dataProvider.RequiredLv);
            if (!spawned)
            {
                Log.Warning(
                    "[Card] Play command rejected at logic frame: deterministic spawn failed. card={0}, runtimeId={1}, xRaw={2}, yRaw={3}.",
                    cardModel.GetCardId(),
                    cardModel.RuntimeId,
                    selectedPosition.x.RawValue,
                    selectedPosition.y.RawValue);
                return;
            }

            if (!InGameDataModel.TryModifyValue(IngameValueType.CurrentSupply, occupiedSupply, true))
                throw new InvalidOperationException($"Card {cardModel.RuntimeId} lost its validated supply capacity before commit.");
            if (!m_HandCardController.RemoveCard(cardModel))
                throw new InvalidOperationException($"Card {cardModel.RuntimeId} disappeared during play commit.");

            m_PlacementController.NotifyPlacementApplied(cardModel, selectedPosition);
            LogicCardCommandService.PublishResolvedCard(
                LogicCardCommandKind.Play,
                cardModel.RuntimeId,
                sourceBuildingInstanceId);
            OnCardPlayed?.Invoke(cardModel);
            GF.Event.FireNow(this, CardPlayedEventArgs.Create(cardModel));
            if (AudioManager.Instance != null)
                AudioManager.Instance.Play("createUnit");
            Debug.Log($"[Card] Card played on logic frame: {cardModel.GetCardName()}, frame={LogicTimeControlService.CurrentFrame}");
        }

        private void ApplyDiscardCommand(CardModel cardModel)
        {
            if (!m_HandCardController.RemoveCard(cardModel))
                throw new InvalidOperationException($"Card {cardModel.RuntimeId} disappeared during discard commit.");
            ApplyDiscardResourceReward(cardModel, null);
            LogicCardCommandService.PublishResolvedCard(
                LogicCardCommandKind.Discard,
                cardModel.RuntimeId,
                cardModel.GetSourceBuildingInstanceId());
            OnCardDiscarded?.Invoke(cardModel);
            GF.Event.Fire(this, CardDiscardedEventArgs.Create(cardModel));
            if (AudioManager.Instance != null)
                AudioManager.Instance.Play("discardCard");
            Debug.Log($"[Card] Card discarded on logic frame: {cardModel.GetCardName()}, frame={LogicTimeControlService.CurrentFrame}");
        }

        private void ApplyDiscardResourceReward(CardModel cardModel, Vector2? discardScreenPosition)
        {
            if (GF.Config == null)
            {
                Log.Error("[Card] Discard reward skipped: GF.Config is not ready.");
                return;
            }

            int occupiedSupply = Mathf.Max(0, cardModel.GetOccupiedSupply());
            int baseConversionRate = GF.Config.GetInt(DiscardResourceConversionRateConfigKey);
            if (baseConversionRate <= 0)
            {
                Log.Error("[Card] Discard reward config invalid. key={0}, value={1}", DiscardResourceConversionRateConfigKey, baseConversionRate);
                return;
            }

            int conversionRate = DiscardRewardModifierService.CalculateConversionRate(baseConversionRate);
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

        public void WriteDeterministicState(LogicStateHasher hasher)
        {
            if (hasher == null)
                throw new ArgumentNullException(nameof(hasher));
            if (m_HandModel == null)
                throw new InvalidOperationException("Card runtime state is not initialized.");

            hasher.Add(m_LastCardRuntimeId);
            hasher.Add(m_DeckCards.Count);
            for (int i = 0; i < m_DeckCards.Count; i++)
            {
                Card entry = m_DeckCards[i]
                             ?? throw new InvalidOperationException($"Card deck contains null at index {i}.");
                AddCardData(hasher, entry.CardData, entry.SourceBuildingInstanceId);
            }
            m_HandModel.WriteDeterministicState(hasher);
        }

        private static void AddCardData(
            LogicStateHasher hasher,
            ICardDataProvider cardData,
            string sourceBuildingInstanceId)
        {
            if (cardData == null)
                throw new InvalidOperationException("Card deck contains an entry without card data.");
            hasher.Add(cardData.CardId);
            hasher.Add((int)cardData.SoldierIndex);
            hasher.Add(cardData.RequiredLv);
            hasher.Add(cardData.PopulationCost);
            hasher.Add(cardData.SoldierCount);
            hasher.Add(sourceBuildingInstanceId);
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
        /// 检查位置是否在禁止区域
        /// </summary>
        public bool IsInForbiddenArea(Vector3 worldPosition)
        {
            LogicCardPlacementInvalidReason reason = LogicCardPlacementAuthority.Evaluate(
                new FixVector2((Fix64)worldPosition.x, (Fix64)worldPosition.z),
                Fix64.Zero);
            return reason != LogicCardPlacementInvalidReason.None;
        }

        /// <summary>
        /// 清理
        /// </summary>
        public void Shutdown()
        {
            m_PlacementController?.Shutdown();
            m_EnemyBuildingForbiddenZoneController?.Shutdown();
            m_HandModel?.Clear();
            m_CardPool?.Clear();
            m_DeckCards.Clear();
            m_LastCardRuntimeId = 0;
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
