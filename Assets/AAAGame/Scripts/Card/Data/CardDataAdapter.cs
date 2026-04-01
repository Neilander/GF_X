using UnityEngine;

namespace AAAGame.Card
{
    /// <summary>
    /// CardData ScriptableObject 适配器
    /// 将现有的 ScriptableObject 包装为 ICardDataProvider
    /// </summary>
    public class CardDataAdapter : ICardDataProvider
    {
        private readonly CardData m_CardData;

        public CardDataAdapter(CardData cardData)
        {
            m_CardData = cardData;
        }

        public string CardId => m_CardData.index;
        public string CardName => m_CardData.cardName;
        public Sprite CardSprite => m_CardData.cardSprite;
        public int PopulationCost => m_CardData.populationCost;
        public int SoldierCount => m_CardData.soldierCount;
        public string SoldierName => m_CardData.soldierName;
        public GameObject SoldierPrefab => m_CardData.soldierPrefab;
        public float SpawnRadius => m_CardData.spawnRadius;
        public Color CardColor => m_CardData.cardColor;
        public int DropWeight => m_CardData.dropWeight;

        public string GetDisplayInfo()
        {
            return m_CardData.GetDisplayInfo();
        }

        /// <summary>
        /// 获取原始 CardData（用于兼容性）
        /// </summary>
        public CardData GetOriginalCardData()
        {
            return m_CardData;
        }
    }
}
