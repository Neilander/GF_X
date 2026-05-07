using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AAAGame.Card
{
    /// <summary>
    /// 卡组预览中的单张卡牌条目。UI 由预制体配置，代码只填充 CardData 数据。
    /// </summary>
    public class CardDeckPreviewItem : MonoBehaviour
    {
        [Header("卡牌预览组件")]
        [SerializeField] [InspectorName("卡牌背景图")] private Image cardBackImage;
        [SerializeField] [InspectorName("卡面立绘")] private Image cardImage;
        [SerializeField] [InspectorName("卡牌名称文本")] private TextMeshProUGUI cardNameText;
        [SerializeField] [InspectorName("人口消耗文本")] private TextMeshProUGUI populationText;
        [SerializeField] [InspectorName("士兵数量文本")] private TextMeshProUGUI soldierCountText;
        [SerializeField] [InspectorName("士兵名称文本")] private TextMeshProUGUI soldierNameText;

        public void SetData(CardData cardData)
        {
            if (cardData == null)
            {
                gameObject.SetActive(false);
                return;
            }

            if (cardBackImage != null)
            {
                if (cardData.cardBackSprite != null)
                {
                    cardBackImage.sprite = cardData.cardBackSprite;
                    cardBackImage.color = Color.white;
                }
                else
                {
                    cardBackImage.color = cardData.cardColor;
                }
            }

            if (cardImage != null)
            {
                if (cardData.cardSprite != null)
                {
                    cardImage.enabled = true;
                    cardImage.sprite = cardData.cardSprite;
                    cardImage.color = Color.white;
                }
                else
                {
                    cardImage.enabled = false;
                }
            }

            if (cardNameText != null)
            {
                cardNameText.text = cardData.cardName;
            }

            if (populationText != null)
            {
                populationText.text = cardData.populationCost.ToString();
            }

            if (soldierCountText != null)
            {
                soldierCountText.text = cardData.soldierCount.ToString();
            }

            if (soldierNameText != null)
            {
                soldierNameText.text = cardData.soldierName;
            }

            gameObject.SetActive(true);
        }
    }
}
