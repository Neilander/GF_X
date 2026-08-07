using UnityEngine;

namespace AAAGame.Card
{
    /// <summary>
    /// CardData ScriptableObject 适配器
    /// 将现有的 ScriptableObject 包装为 ICardDataProvider
    /// </summary>
    public class CardDataAdapter : ICardDataProvider
    {
        private readonly string m_CardId;
        private readonly string m_CardName;
        private readonly Sprite m_CardSprite;
        private readonly int m_PopulationCost;
        private readonly int m_SoldierCount;
        private readonly UnitType m_SoldierIndex;
        private readonly int m_RequiredLv;

        public CardDataAdapter(CardData cardData)
        {
            if (cardData == null)
                throw new System.ArgumentNullException(nameof(cardData));
            m_SoldierIndex = cardData.SoldierIndex;
            m_RequiredLv = cardData.RequiredLv;
            m_CardId = cardData.CardId;
            if (string.IsNullOrWhiteSpace(m_CardId))
                throw new System.InvalidOperationException("Card runtime snapshot requires a card id.");

            CharacterDataDetail character = LogicRuntimeDataTableCache.GetCharacterRequired(m_SoldierIndex.ToString());
            m_PopulationCost = System.Math.Max(0, character.Supply);
            m_SoldierCount = cardData.SoldierCount;
            if (m_SoldierCount <= 0)
                throw new System.InvalidOperationException($"Card runtime snapshot has non-positive soldier count. card={m_CardId}.");
            m_CardName = string.IsNullOrWhiteSpace(character.NameKey)
                ? m_SoldierIndex.ToString()
                : LocalizationTextManager.GetLocalizedText(character.NameKey, false);
            m_CardSprite = cardData.CardSprite;
        }

        public string CardId => m_CardId;
        public string CardName => m_CardName;
        public Sprite CardSprite => m_CardSprite;
        public int PopulationCost => m_PopulationCost;
        public int SoldierCount => m_SoldierCount;
        public string SoldierName => m_CardName;
        public UnitType SoldierIndex => m_SoldierIndex;
        public int RequiredLv => m_RequiredLv;

        public string GetDisplayInfo()
        {
            return $"{m_CardName}\n人口:{m_PopulationCost}";
        }
    }
}
