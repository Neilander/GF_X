using GameFramework.DataTable;

namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌数据表行定义（未来使用）
    /// 对应 DataTable 中的卡牌数据
    /// </summary>
    public class DRCard : IDataRow
    {
        private int m_Id;

        /// <summary>
        /// 卡牌ID
        /// </summary>
        public int Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 卡牌名称
        /// </summary>
        public string CardName { get; private set; }

        /// <summary>
        /// 卡牌图标路径
        /// </summary>
        public string CardSpritePath { get; private set; }

        /// <summary>
        /// 人口消耗
        /// </summary>
        public int PopulationCost { get; private set; }

        /// <summary>
        /// 生成士兵数量
        /// </summary>
        public int SoldierCount { get; private set; }

        /// <summary>
        /// 士兵名称
        /// </summary>
        public string SoldierName { get; private set; }

        /// <summary>
        /// 士兵预制体路径
        /// </summary>
        public string SoldierPrefabPath { get; private set; }

        /// <summary>
        /// 卡牌颜色（十六进制字符串）
        /// </summary>
        public string CardColorHex { get; private set; }

        /// <summary>
        /// 抽卡权重
        /// </summary>
        public int DropWeight { get; private set; }

        /// <summary>
        /// 解析数据行
        /// </summary>
        public bool ParseDataRow(string dataRowString, object userData)
        {
            // TODO: 实现数据解析逻辑
            // 格式示例：id,cardName,spritePath,populationCost,soldierCount,soldierName,prefabPath,colorHex,dropWeight
            string[] columns = dataRowString.Split('\t');
            
            if (columns.Length < 9)
            {
                return false;
            }

            int index = 0;
            m_Id = int.Parse(columns[index++]);
            CardName = columns[index++];
            CardSpritePath = columns[index++];
            PopulationCost = int.Parse(columns[index++]);
            SoldierCount = int.Parse(columns[index++]);
            SoldierName = columns[index++];
            SoldierPrefabPath = columns[index++];
            CardColorHex = columns[index++];
            DropWeight = int.Parse(columns[index++]);

            return true;
        }

        public bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
        {
            throw new System.NotImplementedException();
        }
    }
}
