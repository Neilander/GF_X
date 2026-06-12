using UnityEngine;
using UnityGameFramework.Runtime;
/// <summary>
/// Inspector 调试用卡牌测试器。
/// 可直接把指定 CardData 加入当前牌堆，或手动触发抽一张。
/// </summary>
public class CardTester : MonoBehaviour
{
    [SerializeField] private CardSetup cardSetup;
    [SerializeField] private CardData selectedCard;

    [ContextMenu("Add Selected Card To Deck")]
    public void AddSelectedCardToDeck()
    {
        var setup = ResolveCardSetup();
        if (setup == null)
        {
            Debug.LogWarning("[CardTester] CardSetup not found.");
            return;
        }

        if (selectedCard == null)
        {
            Debug.LogWarning("[CardTester] SelectedCard is null.");
            return;
        }

        bool success = setup.AddCardToDeck(selectedCard);
        Debug.Log(success
            ? $"[CardTester] Added card to deck: {selectedCard.DisplayName}"
            : $"[CardTester] Failed to add card to deck: {selectedCard.DisplayName}");
    }

    [ContextMenu("Draw One Card")]
    public void DrawOneCard()
    {
        var setup = ResolveCardSetup();
        if (setup == null)
        {
            Debug.LogWarning("[CardTester] CardSetup not found.");
            return;
        }

        bool success = setup.DrawCard();
        Debug.Log(success
            ? "[CardTester] Drew one card."
            : "[CardTester] Failed to draw card.");
    }

    private CardSetup ResolveCardSetup()
    {
        if (cardSetup != null)
            return cardSetup;

        cardSetup = GameEntry.GetComponent<CardSetup>();
        return cardSetup;
    }
}
