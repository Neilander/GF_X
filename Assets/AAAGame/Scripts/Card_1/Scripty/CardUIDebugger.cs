using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityGameFramework.Runtime;

/// <summary>
/// 卡牌UI调试工具
/// 用于诊断卡牌显示问题
/// </summary>
public class CardUIDebugger : MonoBehaviour
{
    [Header("调试设置")]
    [SerializeField] private bool enableDebug = true;
    [SerializeField] private KeyCode debugKey = KeyCode.F1;

    void Update()
    {
        if (enableDebug && Input.GetKeyDown(debugKey))
        {
            DebugCardUI();
        }
    }

    /// <summary>
    /// 调试卡牌UI配置
    /// </summary>
    [ContextMenu("调试卡牌UI")]
    public void DebugCardUI()
    {
        GF.Log("========== 开始调试卡牌UI ==========");

        // 检查 CardUIManager
        if (CardUIManager.Instance == null)
        {
            GF.LogError("❌ CardUIManager.Instance 为 null");
            return;
        }
        GF.Log("✅ CardUIManager 存在");

        // 检查卡牌预制体
        var cardUIPrefab = GetCardUIPrefab();
        if (cardUIPrefab == null)
        {
            GF.LogError("❌ Card UI Prefab 未配置");
            return;
        }
        GF.Log($"✅ Card UI Prefab: {cardUIPrefab.name}");

        // 检查预制体结构
        CheckPrefabStructure(cardUIPrefab);

        // 检查场景中的卡牌
        CheckSceneCards();

        // 检查 PlayerHandManager
        CheckPlayerHandManager();

        GF.Log("========== 调试完成 ==========");
    }

    /// <summary>
    /// 获取卡牌预制体
    /// </summary>
    private GameObject GetCardUIPrefab()
    {
        var manager = CardUIManager.Instance;
        if (manager == null) return null;

        // 使用反射获取私有字段
        var field = manager.GetType().GetField("cardUIPrefab", 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance);
        
        return field?.GetValue(manager) as GameObject;
    }

    /// <summary>
    /// 检查预制体结构
    /// </summary>
    private void CheckPrefabStructure(GameObject prefab)
    {
        GF.Log("--- 检查预制体结构 ---");

        // 检查 HandCardUI 脚本
        var handCardUI = prefab.GetComponent<HandCardUI>();
        if (handCardUI == null)
        {
            GF.LogError("❌ 预制体缺少 HandCardUI 脚本");
            return;
        }
        GF.Log("✅ HandCardUI 脚本存在");

        // 检查 UI 组件引用（使用反射）
        var type = handCardUI.GetType();
        
        var cardImageField = type.GetField("cardImage", 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance);
        var cardImage = cardImageField?.GetValue(handCardUI) as Image;
        
        var populationTextField = type.GetField("populationText", 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance);
        var populationText = populationTextField?.GetValue(handCardUI) as TextMeshProUGUI;
        
        var soldierCountTextField = type.GetField("soldierCountText", 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance);
        var soldierCountText = soldierCountTextField?.GetValue(handCardUI) as TextMeshProUGUI;

        // 输出检查结果
        if (cardImage != null)
            GF.Log($"✅ Card Image: {cardImage.name}");
        else
            GF.LogError("❌ Card Image 引用为 null");

        if (populationText != null)
            GF.Log($"✅ Population Text: {populationText.name}");
        else
            GF.LogError("❌ Population Text 引用为 null");

        if (soldierCountText != null)
            GF.Log($"✅ Soldier Count Text: {soldierCountText.name}");
        else
            GF.LogError("❌ Soldier Count Text 引用为 null");

        // 检查子对象
        GF.Log($"预制体子对象数量: {prefab.transform.childCount}");
        for (int i = 0; i < prefab.transform.childCount; i++)
        {
            var child = prefab.transform.GetChild(i);
            GF.Log($"  - {child.name} ({child.GetComponent<Component>()?.GetType().Name})");
        }
    }

    /// <summary>
    /// 检查场景中的卡牌
    /// </summary>
    private void CheckSceneCards()
    {
        GF.Log("--- 检查场景中的卡牌 ---");

        var cards = FindObjectsOfType<HandCardUI>();
        GF.Log($"场景中卡牌数量: {cards.Length}");

        foreach (var card in cards)
        {
            GF.Log($"卡牌: {card.name}");
            
            if (card.CardData != null)
            {
                GF.Log($"  - 卡牌名: {card.CardData.cardName}");
                GF.Log($"  - 人口消耗: {card.CardData.populationCost}");
                GF.Log($"  - 士兵数量: {card.CardData.soldierCount}");
            }
            else
            {
                GF.LogWarning($"  - CardData 为 null");
            }
        }
    }

    /// <summary>
    /// 检查 PlayerHandManager
    /// </summary>
    private void CheckPlayerHandManager()
    {
        GF.Log("--- 检查 PlayerHandManager ---");

        if (PlayerHandManager.Instance == null)
        {
            GF.LogError("❌ PlayerHandManager.Instance 为 null");
            return;
        }
        GF.Log("✅ PlayerHandManager 存在");

        var cards = PlayerHandManager.Instance.HandCards;
        GF.Log($"手牌数量: {cards.Count}/{PlayerHandManager.Instance.MaxHandSize}");

        foreach (var card in cards)
        {
            if (card != null)
            {
                GF.Log($"  - {card.cardName} (人口:{card.populationCost}, 士兵:{card.soldierCount}, 权重:{card.dropWeight})");
            }
            else
            {
                GF.LogWarning("  - 卡牌数据为 null");
            }
        }
    }
}
