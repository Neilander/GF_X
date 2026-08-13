namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌数据提供者接口 - 抽象数据来源
    /// 当前使用 ScriptableObject，将来可切换到 DataTable
    /// </summary>
    public interface ICardDataProvider
    {
        /// <summary>
        /// 卡牌ID
        /// </summary>
        string CardId { get; }

        /// <summary>
        /// 卡牌名称
        /// </summary>
        string CardName { get; }

        /// <summary>
        /// 卡牌图标
        /// </summary>
        UnityEngine.Sprite CardSprite { get; }

        /// <summary>
        /// 人口消耗
        /// </summary>
        int PopulationCost { get; }

        /// <summary>
        /// 生成士兵数量
        /// </summary>
        int SoldierCount { get; }

        /// <summary>
        /// 士兵名称
        /// </summary>
        string SoldierName { get; }

        /// <summary>
        /// 士兵预制体
        /// </summary>
        UnitType SoldierIndex { get; }

        /// <summary>
        /// 适用建筑等级（1/2/3...）。CardSystemController 按 (SoldierIndex, RequiredLv) 匹配建筑。
        /// </summary>
        int RequiredLv { get; }

        /// <summary>
        /// 获取显示信息
        /// </summary>
        string GetDisplayInfo();
    }
}
