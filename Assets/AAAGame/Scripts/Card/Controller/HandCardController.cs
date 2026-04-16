using UnityEngine;
using System;

namespace AAAGame.Card
{
    /// <summary>
    /// 手牌控制器
    /// 负责手牌的抽取、移除等逻辑
    /// </summary>
    public class HandCardController
    {
        private PlayerHandModel m_HandModel;

        // 事件回调
        public event Action<CardModel> OnCardDrawn;
        public event Action<CardModel> OnCardRemoved;

        public HandCardController(PlayerHandModel handModel)
        {
            m_HandModel = handModel;
        }

        /// <summary>
        /// 抽取卡牌
        /// </summary>
        public bool DrawCard(ICardDataProvider cardData, BuildingEntity sourceBuilding = null)
        {
            if (cardData == null)
            {
                Debug.LogError("[Card] Card data is null.");
                return false;
            }

            if (m_HandModel.IsFull)
            {
                Debug.LogWarning("[Card] Hand is full, cannot draw more cards.");
                return false;
            }

            // 创建卡牌模型
            CardModel cardModel = new CardModel(cardData, sourceBuilding);

            // 添加到手牌
            if (!m_HandModel.AddCard(cardModel))
            {
                return false;
            }

            // 触发抽卡事件
            OnCardDrawn?.Invoke(cardModel);

            Debug.Log($"[Card] Drew card: {cardData.CardName}");
            return true;
        }

        /// <summary>
        /// 移除卡牌
        /// </summary>
        public bool RemoveCard(CardModel cardModel)
        {
            if (cardModel == null)
            {
                Debug.LogError("[Card] CardModel is null.");
                return false;
            }

            if (!m_HandModel.RemoveCard(cardModel))
            {
                Debug.LogWarning($"[Card] Failed to remove card: {cardModel.GetCardName()}");
                return false;
            }

            // 触发移除事件
            OnCardRemoved?.Invoke(cardModel);

            Debug.Log($"[Card] Removed card: {cardModel.GetCardName()}");
            return true;
        }

        /// <summary>
        /// 根据索引移除卡牌
        /// </summary>
        public CardModel RemoveCardAt(int index)
        {
            CardModel cardModel = m_HandModel.RemoveCardAt(index);

            if (cardModel != null)
            {
                OnCardRemoved?.Invoke(cardModel);
                Debug.Log($"[Card] Removed card at index {index}: {cardModel.GetCardName()}");
            }

            return cardModel;
        }

        /// <summary>
        /// 获取指定索引的卡牌
        /// </summary>
        public CardModel GetCard(int index)
        {
            return m_HandModel.GetCard(index);
        }

        /// <summary>
        /// 获取手牌数量
        /// </summary>
        public int GetCardCount()
        {
            return m_HandModel.CardCount;
        }

        /// <summary>
        /// 手牌是否已满
        /// </summary>
        public bool IsHandFull()
        {
            return m_HandModel.IsFull;
        }

        /// <summary>
        /// 清空手牌
        /// </summary>
        public void ClearHand()
        {
            m_HandModel.Clear();
            Debug.Log("[Card] Hand cleared.");
        }
    }
}
