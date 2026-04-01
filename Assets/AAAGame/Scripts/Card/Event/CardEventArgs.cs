using GameFramework;
using GameFramework.Event;

namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌抽取事件
    /// </summary>
    public sealed class CardDrawnEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(CardDrawnEventArgs).GetHashCode();

        public override int Id => EventId;

        public CardModel CardModel { get; private set; }

        public static CardDrawnEventArgs Create(CardModel cardModel)
        {
            CardDrawnEventArgs e = ReferencePool.Acquire<CardDrawnEventArgs>();
            e.CardModel = cardModel;
            return e;
        }

        public override void Clear()
        {
            CardModel = null;
        }
    }

    /// <summary>
    /// 卡牌打出事件
    /// </summary>
    public sealed class CardPlayedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(CardPlayedEventArgs).GetHashCode();

        public override int Id => EventId;

        public CardModel CardModel { get; private set; }

        public static CardPlayedEventArgs Create(CardModel cardModel)
        {
            CardPlayedEventArgs e = ReferencePool.Acquire<CardPlayedEventArgs>();
            e.CardModel = cardModel;
            return e;
        }

        public override void Clear()
        {
            CardModel = null;
        }
    }

    /// <summary>
    /// 卡牌丢弃事件
    /// </summary>
    public sealed class CardDiscardedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(CardDiscardedEventArgs).GetHashCode();

        public override int Id => EventId;

        public CardModel CardModel { get; private set; }

        public static CardDiscardedEventArgs Create(CardModel cardModel)
        {
            CardDiscardedEventArgs e = ReferencePool.Acquire<CardDiscardedEventArgs>();
            e.CardModel = cardModel;
            return e;
        }

        public override void Clear()
        {
            CardModel = null;
        }
    }

    /// <summary>
    /// 人口变化事件
    /// </summary>
    public sealed class PopulationChangedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(PopulationChangedEventArgs).GetHashCode();

        public override int Id => EventId;

        public int CurrentPopulation { get; private set; }
        public int MaxPopulation { get; private set; }
        public int ChangeAmount { get; private set; }

        public static PopulationChangedEventArgs Create(int current, int max, int change)
        {
            PopulationChangedEventArgs e = ReferencePool.Acquire<PopulationChangedEventArgs>();
            e.CurrentPopulation = current;
            e.MaxPopulation = max;
            e.ChangeAmount = change;
            return e;
        }

        public override void Clear()
        {
            CurrentPopulation = 0;
            MaxPopulation = 0;
            ChangeAmount = 0;
        }
    }

    /// <summary>
    /// 卡牌放置开始事件
    /// </summary>
    public sealed class CardPlacementStartEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(CardPlacementStartEventArgs).GetHashCode();

        public override int Id => EventId;

        public CardModel CardModel { get; private set; }

        public static CardPlacementStartEventArgs Create(CardModel cardModel)
        {
            CardPlacementStartEventArgs e = ReferencePool.Acquire<CardPlacementStartEventArgs>();
            e.CardModel = cardModel;
            return e;
        }

        public override void Clear()
        {
            CardModel = null;
        }
    }

    /// <summary>
    /// 卡牌放置位置更新事件
    /// </summary>
    public sealed class CardPlacementPositionUpdateEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(CardPlacementPositionUpdateEventArgs).GetHashCode();

        public override int Id => EventId;

        public UnityEngine.Vector3 Position { get; private set; }
        public bool IsValid { get; private set; }

        public static CardPlacementPositionUpdateEventArgs Create(UnityEngine.Vector3 position, bool isValid)
        {
            CardPlacementPositionUpdateEventArgs e = ReferencePool.Acquire<CardPlacementPositionUpdateEventArgs>();
            e.Position = position;
            e.IsValid = isValid;
            return e;
        }

        public override void Clear()
        {
            Position = UnityEngine.Vector3.zero;
            IsValid = false;
        }
    }

    /// <summary>
    /// 卡牌放置合法性变化事件
    /// </summary>
    public sealed class CardPlacementValidityChangeEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(CardPlacementValidityChangeEventArgs).GetHashCode();

        public override int Id => EventId;

        public bool IsValid { get; private set; }

        public static CardPlacementValidityChangeEventArgs Create(bool isValid)
        {
            CardPlacementValidityChangeEventArgs e = ReferencePool.Acquire<CardPlacementValidityChangeEventArgs>();
            e.IsValid = isValid;
            return e;
        }

        public override void Clear()
        {
            IsValid = false;
        }
    }

    /// <summary>
    /// 卡牌放置确认事件
    /// </summary>
    public sealed class CardPlacementConfirmEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(CardPlacementConfirmEventArgs).GetHashCode();

        public override int Id => EventId;

        public CardModel CardModel { get; private set; }
        public UnityEngine.Vector3 Position { get; private set; }

        public static CardPlacementConfirmEventArgs Create(CardModel cardModel, UnityEngine.Vector3 position)
        {
            CardPlacementConfirmEventArgs e = ReferencePool.Acquire<CardPlacementConfirmEventArgs>();
            e.CardModel = cardModel;
            e.Position = position;
            return e;
        }

        public override void Clear()
        {
            CardModel = null;
            Position = UnityEngine.Vector3.zero;
        }
    }

    /// <summary>
    /// 卡牌放置取消事件
    /// </summary>
    public sealed class CardPlacementCancelEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(CardPlacementCancelEventArgs).GetHashCode();

        public override int Id => EventId;

        public static CardPlacementCancelEventArgs Create()
        {
            CardPlacementCancelEventArgs e = ReferencePool.Acquire<CardPlacementCancelEventArgs>();
            return e;
        }

        public override void Clear()
        {
        }
    }

    /// <summary>
    /// 士兵生成事件
    /// </summary>
    public sealed class CardSoldiersSpawnedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(CardSoldiersSpawnedEventArgs).GetHashCode();

        public override int Id => EventId;

        public CardModel CardModel { get; private set; }
        public UnityEngine.Vector3 SpawnPosition { get; private set; }
        public int SoldierCount { get; private set; }

        public static CardSoldiersSpawnedEventArgs Create(CardModel cardModel, UnityEngine.Vector3 spawnPosition, int soldierCount)
        {
            CardSoldiersSpawnedEventArgs e = ReferencePool.Acquire<CardSoldiersSpawnedEventArgs>();
            e.CardModel = cardModel;
            e.SpawnPosition = spawnPosition;
            e.SoldierCount = soldierCount;
            return e;
        }

        public override void Clear()
        {
            CardModel = null;
            SpawnPosition = UnityEngine.Vector3.zero;
            SoldierCount = 0;
        }
    }
}
