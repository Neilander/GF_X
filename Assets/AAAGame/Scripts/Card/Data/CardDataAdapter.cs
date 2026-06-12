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

        public string CardId => m_CardData.CardId;
        public string CardName => m_CardData.DisplayName;
        public Sprite CardSprite => m_CardData.CardSprite;
        public int PopulationCost => m_CardData.PopulationCost;
        public int SoldierCount => m_CardData.SoldierCount;
        public string SoldierName => m_CardData.DisplayName;
        public UnitType SoldierIndex => m_CardData.SoldierIndex;
        public int RequiredLv => m_CardData.RequiredLv;

        public string GetDisplayInfo()
        {
            return m_CardData.GetDisplayInfo();
        }

        public CardData GetOriginalCardData()
        {
            return m_CardData;
        }
    }
}
