using UnityEngine;
using GameFramework;

namespace AAAGame.Card
{
    /// <summary>
    /// DataTable 适配器（未来实现）
    /// 从 GameFramework DataTable 读取卡牌数据
    /// </summary>
    public class CardDataTableAdapter : ICardDataProvider
    {
        private readonly DRCard m_DataRow;
        private Sprite m_CachedSprite;
        private GameObject m_CachedPrefab;

        public CardDataTableAdapter(DRCard dataRow)
        {
            m_DataRow = dataRow;
        }

        public string CardId => m_DataRow.Id.ToString();
        public string CardName => m_DataRow.CardName;
        
        public Sprite CardSprite
        {
            get
            {
                if (m_CachedSprite == null && !string.IsNullOrEmpty(m_DataRow.CardSpritePath))
                {
                    // TODO: 通过 ResourceComponent 加载
                    // m_CachedSprite = GF.Resource.LoadAsset<Sprite>(m_DataRow.CardSpritePath);
                }
                return m_CachedSprite;
            }
        }

        public Sprite CardBackSprite => null;

        public int PopulationCost => m_DataRow.PopulationCost;
        public int SoldierCount => m_DataRow.SoldierCount;
        public string SoldierName => m_DataRow.SoldierName;
        
        public UnitType SoldierIndex
        {
            get
            {
                Debug.LogError("使用了CardDataTableAdapter，但是这东西没写完");
                return default;
            }
        }

        // TODO: 等 DataTable 真正接入后从 m_DataRow 读，目前默认 1
        public int RequiredLv => 1;

        public float SpawnRadius => m_DataRow.SpawnRadius;
        
        public Color CardColor
        {
            get
            {
                // 从字符串解析颜色 (格式: "R,G,B,A")
                if (ColorUtility.TryParseHtmlString(m_DataRow.CardColorHex, out Color color))
                {
                    return color;
                }
                return Color.white;
            }
        }

        public int DropWeight => m_DataRow.DropWeight;

        public string GetDisplayInfo()
        {
            return Utility.Text.Format("{0}\n人口:{1}\n士兵:{2}", CardName, PopulationCost, SoldierCount);
        }
    }
}
