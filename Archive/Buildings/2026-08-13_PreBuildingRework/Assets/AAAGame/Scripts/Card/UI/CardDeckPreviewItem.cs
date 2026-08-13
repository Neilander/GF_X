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

        public void SetData(ICardDataProvider cardData, string sourceBuildingInstanceId)
        {
            if (cardData == null)
            {
                gameObject.SetActive(false);
                return;
            }

            IBuildingLogicContext sourceBuilding = string.IsNullOrWhiteSpace(sourceBuildingInstanceId)
                ? null
                : LogicBuildingQueryService.GetRequiredByInstanceId(sourceBuildingInstanceId);
            ApplyData(
                cardData.CardSprite,
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

            SetData(new CardDataAdapter(cardData), null);
        }

        private void ApplyData(
            Sprite cardSprite,
            string cardName,
            int populationCost,
            int soldierCount,
            string soldierName)
        {
            if (cardBackImage != null)
            {
                if (cardSprite != null)
                {
                    cardBackImage.sprite = cardSprite;
                }

                cardBackImage.color = Color.white;
            }

            if (cardImage != null)
            {
                cardImage.enabled = false;
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
