using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AAAGame.Card
{
    public class CardDeckPreviewItem : MonoBehaviour
    {
        [Header("Card Preview")]
        [SerializeField] private Image cardBackImage;
        [SerializeField] private Image cardImage;
        [SerializeField] private TextMeshProUGUI cardNameText;
        [SerializeField] private TextMeshProUGUI populationText;
        [SerializeField] private TextMeshProUGUI soldierCountText;
        [SerializeField] private TextMeshProUGUI soldierNameText;

        public void SetData(ICardDataProvider cardData, BuildingEntity sourceBuilding)
        {
            if (cardData == null)
            {
                gameObject.SetActive(false);
                return;
            }

            ApplyData(
                cardData.CardBackSprite,
                cardData.CardSprite,
                cardData.CardColor,
                cardData.CardName,
                sourceBuilding != null ? sourceBuilding.GetArmyOccupiedSupply() : cardData.PopulationCost,
                sourceBuilding != null ? sourceBuilding.GetArmyForce() : cardData.SoldierCount,
                cardData.SoldierName);
        }

        public void SetData(CardData cardData)
        {
            if (cardData == null)
            {
                gameObject.SetActive(false);
                return;
            }

            ApplyData(
                cardData.cardBackSprite,
                cardData.cardSprite,
                cardData.cardColor,
                cardData.cardName,
                cardData.populationCost,
                cardData.soldierCount,
                cardData.soldierName);
        }

        private void ApplyData(
            Sprite cardBackSprite,
            Sprite cardSprite,
            Color cardColor,
            string cardName,
            int populationCost,
            int soldierCount,
            string soldierName)
        {
            if (cardBackImage != null)
            {
                if (cardBackSprite != null)
                {
                    cardBackImage.sprite = cardBackSprite;
                    cardBackImage.color = Color.white;
                }
                else
                {
                    cardBackImage.color = cardColor;
                }
            }

            if (cardImage != null)
            {
                if (cardSprite != null)
                {
                    cardImage.sprite = cardSprite;
                    cardImage.color = Color.white;
                }
                else
                {
                    cardImage.color = cardColor;
                }
            }

            if (cardNameText != null)
            {
                cardNameText.text = cardName;
            }

            if (populationText != null)
            {
                populationText.text = populationCost.ToString();
            }

            if (soldierCountText != null)
            {
                soldierCountText.text = soldierCount.ToString();
            }

            if (soldierNameText != null)
            {
                soldierNameText.text = soldierName;
            }

            gameObject.SetActive(true);
        }
    }
}
