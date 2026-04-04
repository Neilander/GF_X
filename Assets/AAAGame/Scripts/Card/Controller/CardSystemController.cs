using System.Collections.Generic;
using UnityEngine;
using System;

namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌系统主控制器
    /// 负责协调各个子控制器和模型
    /// </summary>
    public class CardSystemController
    {
        private PopulationModel m_PopulationModel;
        private PlayerHandModel m_HandModel;
        private HandCardController m_HandCardController;
        private CardPlacementController m_PlacementController;
        private AreaDetectionController m_AreaDetectionController;

        private List<ICardDataProvider> m_CardPool;
        private System.Random m_Random;

        // 事件回调
        public event Action<int, int, int> OnPopulationChanged; // (current, max, cost)
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
            m_PopulationModel = new PopulationModel();
            m_HandModel = new PlayerHandModel();

            // 初始化控制器
            m_HandCardController = new HandCardController(m_HandModel, m_PopulationModel);
            m_PlacementController = new CardPlacementController();
            m_AreaDetectionController = new AreaDetectionController();

            // 订阅子控制器事件
            m_HandCardController.OnCardDrawn += (card) => OnCardDrawn?.Invoke(card);
            m_HandCardController.OnCardRemoved += (card) => OnHandChanged?.Invoke(m_HandModel.CardCount, m_HandModel.MaxCards);

            // 初始化卡牌池
            m_CardPool = new List<ICardDataProvider>();
            m_Random = new System.Random();

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
        /// 设置最大人口
        /// </summary>
        public void SetMaxPopulation(int maxPopulation)
        {
            m_PopulationModel.SetMaxPopulation(maxPopulation);
            
            // 触发人口变化事件
            OnPopulationChanged?.Invoke(
                m_PopulationModel.CurrentPopulation,
                m_PopulationModel.MaxPopulation,
                0);
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

            if (m_CardPool == null || m_CardPool.Count == 0)
            {
                Debug.LogError("[Card] Card pool is empty.");
                return false;
            }

            // 根据权重随机抽取卡牌
            ICardDataProvider cardData = DrawRandomCardByWeight();
            if (cardData == null)
            {
                return false;
            }

            bool success = m_HandCardController.DrawCard(cardData);
            if (success)
            {
                OnHandChanged?.Invoke(m_HandModel.CardCount, m_HandModel.MaxCards);
            }

            return success;
        }

        /// <summary>
        /// 根据权重随机抽取卡牌
        /// </summary>
        private ICardDataProvider DrawRandomCardByWeight()
        {
            if (m_CardPool.Count == 0) return null;

            // 计算总权重
            int totalWeight = 0;
            foreach (var card in m_CardPool)
            {
                totalWeight += card.DropWeight;
            }

            if (totalWeight <= 0)
            {
                // 如果没有权重，随机选择
                return m_CardPool[m_Random.Next(m_CardPool.Count)];
            }

            // 根据权重随机
            int randomValue = m_Random.Next(totalWeight);
            int currentWeight = 0;

            foreach (var card in m_CardPool)
            {
                currentWeight += card.DropWeight;
                if (randomValue < currentWeight)
                {
                    return card;
                }
            }

            return m_CardPool[0];
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

            if (!cardModel.CanPlay())
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
            m_PlacementController.StartPlacement(cardModel);
        }

        /// <summary>
        /// 更新放置（每帧调用）
        /// </summary>
        public void UpdatePlacement()
        {
            m_PlacementController.UpdatePlacement();
        }

        /// <summary>
        /// 确认放置
        /// </summary>
        public bool ConfirmPlacement(CardModel cardModel)
        {
            if (!m_PlacementController.ConfirmPlacement(cardModel))
            {
                return false;
            }

            // 消耗人口
            int populationCost = cardModel.GetPopulationCost();
            if (!m_PopulationModel.ConsumePopulation(populationCost))
            {
                Debug.LogError("[Card] Failed to consume population.");
                return false;
            }

            // 触发人口变化事件
            OnPopulationChanged?.Invoke(
                m_PopulationModel.CurrentPopulation,
                m_PopulationModel.MaxPopulation,
                populationCost);

            // 从手牌移除
            m_HandCardController.RemoveCard(cardModel);

            // 触发卡牌打出事件（C# 事件）
            OnCardPlayed?.Invoke(cardModel);
            
            // 触发卡牌打出事件（GameFramework 事件系统）
            GameFramework.Event.GameEventArgs e = CardPlayedEventArgs.Create(cardModel);
            GF.Event.Fire(this, e);
            GameFramework.ReferencePool.Release(e);

            Debug.Log($"[Card] Card played: {cardModel.GetCardName()}");
            return true;
        }

        /// <summary>
        /// 取消放置
        /// </summary>
        public void CancelPlacement()
        {
            m_PlacementController.CancelPlacement();
        }

        /// <summary>
        /// 丢弃卡牌
        /// </summary>
        public bool DiscardCard(CardModel cardModel)
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

            // 触发丢弃事件
            OnCardDiscarded?.Invoke(cardModel);

            return true;
        }

        /// <summary>
        /// 获取人口模型
        /// </summary>
        public PopulationModel GetPopulationModel()
        {
            return m_PopulationModel;
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
            return m_AreaDetectionController.IsPositionInInvalidArea(worldPosition);
        }

        /// <summary>
        /// 清理
        /// </summary>
        public void Shutdown()
        {
            m_PlacementController?.Shutdown();
            m_AreaDetectionController?.Shutdown();
            m_HandModel?.Clear();
            m_CardPool?.Clear();

            Debug.Log("[Card] CardSystemController shutdown.");
        }
    }
}
