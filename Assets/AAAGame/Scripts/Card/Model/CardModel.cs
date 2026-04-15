namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌运行时数据模型
    /// 封装卡牌的运行时状态和数据访问
    /// </summary>
    public class CardModel
    {
        private ICardDataProvider m_DataProvider;
        private PopulationModel m_PopulationModel;
        private BuildingEntity m_SourceBuilding;

        /// <summary>
        /// 数据提供者
        /// </summary>
        public ICardDataProvider DataProvider => m_DataProvider;

        /// <summary>
        /// 卡牌来源建筑
        /// </summary>
        public BuildingEntity SourceBuilding => m_SourceBuilding;

        public CardModel(ICardDataProvider dataProvider, PopulationModel populationModel, BuildingEntity sourceBuilding = null)
        {
            m_DataProvider = dataProvider;
            m_PopulationModel = populationModel;
            m_SourceBuilding = sourceBuilding;
        }

        /// <summary>
        /// 是否可以打出（人口是否足够）
        /// </summary>
        public bool CanPlay()
        {
            if (m_DataProvider == null || m_PopulationModel == null)
            {
                return false;
            }

            return m_PopulationModel.HasEnoughPopulation(m_DataProvider.PopulationCost);
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
            return m_DataProvider?.PopulationCost ?? 0;
        }

        /// <summary>
        /// 获取来源建筑实例ID
        /// </summary>
        public string GetSourceBuildingInstanceId()
        {
            return m_SourceBuilding != null ? m_SourceBuilding.BuildingInstanceId : string.Empty;
        }
    }
}
