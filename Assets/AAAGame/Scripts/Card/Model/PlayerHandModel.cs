using System.Collections.Generic;
using UnityEngine;

namespace AAAGame.Card
{
    /// <summary>
    /// 玩家手牌数据模型
    /// 管理玩家手中的卡牌
    /// </summary>
    public class PlayerHandModel
    {
        private List<CardModel> m_Cards;
        private int m_MaxCards;

        public PlayerHandModel(int maxCards = CardConst.MaxHandCards)
        {
            m_MaxCards = maxCards;
            m_Cards = new List<CardModel>(maxCards);
        }

        /// <summary>
        /// 当前手牌数量
        /// </summary>
        public int CardCount => m_Cards.Count;

        /// <summary>
        /// 最大手牌数量
        /// </summary>
        public int MaxCards => m_MaxCards;

        /// <summary>
        /// 是否已满
        /// </summary>
        public bool IsFull => m_Cards.Count >= m_MaxCards;

        /// <summary>
        /// 添加卡牌
        /// </summary>
        public bool AddCard(CardModel card)
        {
            if (card == null)
            {
                Debug.LogError("[Card] Card is null.");
                return false;
            }

            if (IsFull)
            {
                Debug.LogWarning("[Card] Hand is full, cannot add more cards.");
                return false;
            }

            m_Cards.Add(card);
            return true;
        }

        /// <summary>
        /// 移除卡牌
        /// </summary>
        public bool RemoveCard(CardModel card)
        {
            if (card == null)
            {
                Debug.LogError("[Card] Card is null.");
                return false;
            }

            return m_Cards.Remove(card);
        }

        /// <summary>
        /// 根据索引移除卡牌
        /// </summary>
        public CardModel RemoveCardAt(int index)
        {
            if (index < 0 || index >= m_Cards.Count)
            {
                Debug.LogError($"[Card] Invalid card index: {index}");
                return null;
            }

            CardModel card = m_Cards[index];
            m_Cards.RemoveAt(index);
            return card;
        }

        /// <summary>
        /// 获取指定索引的卡牌
        /// </summary>
        public CardModel GetCard(int index)
        {
            if (index < 0 || index >= m_Cards.Count)
            {
                return null;
            }

            return m_Cards[index];
        }

        /// <summary>
        /// 获取所有卡牌
        /// </summary>
        public List<CardModel> GetAllCards()
        {
            return new List<CardModel>(m_Cards);
        }

        /// <summary>
        /// 清空手牌
        /// </summary>
        public void Clear()
        {
            m_Cards.Clear();
        }

        /// <summary>
        /// 是否包含指定卡牌
        /// </summary>
        public bool Contains(CardModel card)
        {
            return m_Cards.Contains(card);
        }

        /// <summary>
        /// 获取卡牌索引
        /// </summary>
        public int IndexOf(CardModel card)
        {
            return m_Cards.IndexOf(card);
        }
    }
}
