namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌运行时数据模型
    /// 封装卡牌的运行时状态和数据访问
    /// </summary>
    public class CardModel
    {
        private ICardDataProvider m_DataProvider;
        private readonly string m_SourceBuildingInstanceId;

        /// <summary>
        /// 数据提供者
        /// </summary>
        public ICardDataProvider DataProvider => m_DataProvider;

        public ulong RuntimeId { get; }

        public CardModel(ICardDataProvider dataProvider)
            : this(0, dataProvider, null)
        {
        }

        public CardModel(
            ulong runtimeId,
            ICardDataProvider dataProvider,
            string sourceBuildingInstanceId)
        {
            if (dataProvider == null)
                throw new System.ArgumentNullException(nameof(dataProvider));
            m_DataProvider = dataProvider;
            m_SourceBuildingInstanceId = string.IsNullOrWhiteSpace(sourceBuildingInstanceId)
                ? string.Empty
                : sourceBuildingInstanceId;
            RuntimeId = runtimeId;
        }

        /// <summary>
        /// 是否可以打出（人口是否足够）
        /// </summary>
        public bool CanPlay()
        {
            if (m_DataProvider == null)
            {
                return false;
            }

            return InGameDataModel.HasEnoughSupplyFor(GetOccupiedSupply());
        }

        /// <summary>
        /// 获取卡牌显示信息
        /// </summary>
        public string GetDisplayInfo()
        {
            if (m_DataProvider == null)
            {
                return string.Empty;
            }

            return m_DataProvider.GetDisplayInfo();
        }

        /// <summary>
        /// 获取卡牌ID
        /// </summary>
        public string GetCardId()
        {
            return m_DataProvider?.CardId ?? string.Empty;
        }

        /// <summary>
        /// 获取卡牌名称
        /// </summary>
        public string GetCardName()
        {
            return m_DataProvider?.CardName ?? string.Empty;
        }

        /// <summary>
        /// 获取人口消耗
        /// </summary>
        public int GetPopulationCost()
        {
            return GetOccupiedSupply();
        }

        public int GetTroopCount()
        {
            if (!string.IsNullOrWhiteSpace(m_SourceBuildingInstanceId))
                return LogicBuildingQueryService.GetRequiredByInstanceId(m_SourceBuildingInstanceId).GetArmyForce();

            return m_DataProvider?.SoldierCount ?? 0;
        }

        public int GetOccupiedSupply()
        {
            if (!string.IsNullOrWhiteSpace(m_SourceBuildingInstanceId))
                return LogicBuildingQueryService.GetRequiredByInstanceId(m_SourceBuildingInstanceId).GetArmyOccupiedSupply();

            return m_DataProvider?.PopulationCost ?? 0;
        }

        /// <summary>
        /// 获取来源建筑实例ID
        /// </summary>
        public string GetSourceBuildingInstanceId()
        {
            return m_SourceBuildingInstanceId;
        }

        internal void WriteDeterministicState(LogicStateHasher hasher)
        {
            if (hasher == null)
                throw new System.ArgumentNullException(nameof(hasher));
            hasher.Add(RuntimeId);
            hasher.Add(m_DataProvider.CardId);
            hasher.Add((int)m_DataProvider.SoldierIndex);
            hasher.Add(m_DataProvider.RequiredLv);
            hasher.Add(m_DataProvider.PopulationCost);
            hasher.Add(m_DataProvider.SoldierCount);
            hasher.Add(m_SourceBuildingInstanceId);
        }
    }
}
