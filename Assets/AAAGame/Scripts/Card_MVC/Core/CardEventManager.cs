using System;

namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌系统事件管理器
    /// 使用 C# Event 替代 GameFramework 事件系统
    /// </summary>
    public class CardEventManager
    {
        // 人口变化事件 (currentPopulation, maxPopulation, costAmount)
        public event Action<int, int, int> OnPopulationChanged;

        // 手牌变化事件 (cardCount, maxCards)
        public event Action<int, int> OnHandChanged;

        // 卡牌抽取事件 (cardModel)
        public event Action<CardModel> OnCardDrawn;

        // 卡牌打出事件 (cardModel)
        public event Action<CardModel> OnCardPlayed;

        // 卡牌丢弃事件 (cardModel)
        public event Action<CardModel> OnCardDiscarded;

        // 放置开始事件 (cardModel)
        public event Action<CardModel> OnPlacementStarted;

        // 放置取消事件
        public event Action OnPlacementCancelled;

        // 放置确认事件 (cardModel, position)
        public event Action<CardModel, UnityEngine.Vector3> OnPlacementConfirmed;

        /// <summary>
        /// 触发人口变化事件
        /// </summary>
        public void FirePopulationChanged(int current, int max, int cost)
        {
            OnPopulationChanged?.Invoke(current, max, cost);
        }

        /// <summary>
        /// 触发手牌变化事件
        /// </summary>
        public void FireHandChanged(int cardCount, int maxCards)
        {
            OnHandChanged?.Invoke(cardCount, maxCards);
        }

        /// <summary>
        /// 触发卡牌抽取事件
        /// </summary>
        public void FireCardDrawn(CardModel cardModel)
        {
            OnCardDrawn?.Invoke(cardModel);
        }

        /// <summary>
        /// 触发卡牌打出事件
        /// </summary>
        public void FireCardPlayed(CardModel cardModel)
        {
            OnCardPlayed?.Invoke(cardModel);
        }

        /// <summary>
        /// 触发卡牌丢弃事件
        /// </summary>
        public void FireCardDiscarded(CardModel cardModel)
        {
            OnCardDiscarded?.Invoke(cardModel);
        }

        /// <summary>
        /// 触发放置开始事件
        /// </summary>
        public void FirePlacementStarted(CardModel cardModel)
        {
            OnPlacementStarted?.Invoke(cardModel);
        }

        /// <summary>
        /// 触发放置取消事件
        /// </summary>
        public void FirePlacementCancelled()
        {
            OnPlacementCancelled?.Invoke();
        }

        /// <summary>
        /// 触发放置确认事件
        /// </summary>
        public void FirePlacementConfirmed(CardModel cardModel, UnityEngine.Vector3 position)
        {
            OnPlacementConfirmed?.Invoke(cardModel, position);
        }

        /// <summary>
        /// 清空所有事件订阅
        /// </summary>
        public void ClearAllEvents()
        {
            OnPopulationChanged = null;
            OnHandChanged = null;
            OnCardDrawn = null;
            OnCardPlayed = null;
            OnCardDiscarded = null;
            OnPlacementStarted = null;
            OnPlacementCancelled = null;
            OnPlacementConfirmed = null;
        }
    }
}
