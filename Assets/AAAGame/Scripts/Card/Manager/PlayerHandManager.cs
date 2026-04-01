using UnityEngine;
using System.Collections.Generic;
using System;

namespace AAAGame.Card
{
    /// <summary>
    /// 玩家手牌管理器 - 完全独立版本
    /// 不依赖任何 Temp_script 代码
    /// </summary>
    public class PlayerHandManager : MonoBehaviour
{
    private static PlayerHandManager instance;
    public static PlayerHandManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<PlayerHandManager>();
            }
            return instance;
        }
    }

    [Header("手牌设置")]
    [SerializeField] private int maxHandSize = 4;
    
    [Header("卡牌池配置")]
    [Tooltip("所有可抽取的卡牌")]
    [SerializeField] private List<CardData> cardPool = new List<CardData>();

    private List<CardData> handCards = new List<CardData>();
    private CardSystemController cardSystemController;

    public int MaxHandSize => maxHandSize;
    public int CurrentHandSize => handCards.Count;
    public List<CardData> HandCards => new List<CardData>(handCards);

    // 手牌变化事件（兼容 Temp_script）
    public event Action<List<CardData>> OnHandChanged;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    void Start()
    {
        // 初始化手牌
        InitializeHand();
    }

    /// <summary>
    /// 设置卡牌系统控制器（由 CardGameManager 调用）
    /// </summary>
    public void SetCardSystemController(CardSystemController controller)
    {
        cardSystemController = controller;
    }

    /// <summary>
    /// 初始化手牌
    /// </summary>
    private void InitializeHand()
    {
        handCards.Clear();
        
        // 使用随机抽卡系统
        DrawRandomCards(maxHandSize);
        
        Debug.Log($"[Card] 初始化手牌：{handCards.Count} 张");
    }

    /// <summary>
    /// 随机抽取指定数量的卡牌
    /// </summary>
    public void DrawRandomCards(int count)
    {
        if (cardPool == null || cardPool.Count == 0)
        {
            Debug.LogWarning("[Card] 卡牌池为空，无法抽卡");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (handCards.Count >= maxHandSize)
            {
                Debug.LogWarning($"[Card] 手牌已满！最多 {maxHandSize} 张");
                break;
            }

            CardData drawnCard = DrawRandomCard();
            if (drawnCard != null)
            {
                handCards.Add(drawnCard);
                Debug.Log($"[Card] 抽取卡牌：{drawnCard.cardName}（权重：{drawnCard.dropWeight}）");
            }
        }

        OnHandChanged?.Invoke(handCards);
    }

    /// <summary>
    /// 根据权重随机抽取一张卡牌
    /// </summary>
    private CardData DrawRandomCard()
    {
        if (cardPool == null || cardPool.Count == 0)
        {
            return null;
        }

        // 计算总权重
        int totalWeight = 0;
        foreach (var card in cardPool)
        {
            if (card != null)
            {
                totalWeight += card.dropWeight;
            }
        }

        if (totalWeight <= 0)
        {
            Debug.LogWarning("[Card] 卡牌池总权重为0，随机选择一张");
            return cardPool[UnityEngine.Random.Range(0, cardPool.Count)];
        }

        // 随机一个值
        int randomValue = UnityEngine.Random.Range(0, totalWeight);
        
        // 根据权重选择卡牌
        int currentWeight = 0;
        foreach (var card in cardPool)
        {
            if (card == null) continue;

            currentWeight += card.dropWeight;
            if (randomValue < currentWeight)
            {
                return card;
            }
        }

        // 兜底返回第一张
        return cardPool[0];
    }

    /// <summary>
    /// 重新抽取手牌（清空当前手牌，重新随机抽取）
    /// </summary>
    public void RedrawHand()
    {
        handCards.Clear();
        OnHandChanged?.Invoke(handCards); // 先触发清空事件
        
        Debug.Log("[Card] 清空手牌，准备重新抽取");
        
        // 延迟一帧再抽取，确保UI清空
        StartCoroutine(DelayedDrawCards());
    }

    /// <summary>
    /// 延迟抽卡（确保UI清空后再抽取）
    /// </summary>
    private System.Collections.IEnumerator DelayedDrawCards()
    {
        yield return null; // 等待一帧
        DrawRandomCards(maxHandSize);
    }

    /// <summary>
    /// 添加卡牌到手牌
    /// </summary>
    public bool AddCard(CardData card)
    {
        if (card == null)
        {
            Debug.LogWarning("[Card] 尝试添加空卡牌");
            return false;
        }

        if (handCards.Count >= maxHandSize)
        {
            Debug.LogWarning($"[Card] 手牌已满！最多 {maxHandSize} 张");
            return false;
        }

        handCards.Add(card);
        OnHandChanged?.Invoke(handCards);
        Debug.Log($"[Card] 添加卡牌：{card.cardName}，当前手牌：{handCards.Count}/{maxHandSize}");
        return true;
    }

    /// <summary>
    /// 从手牌移除卡牌
    /// </summary>
    public bool RemoveCard(CardData card)
    {
        if (card == null || !handCards.Contains(card))
        {
            return false;
        }

        handCards.Remove(card);
        OnHandChanged?.Invoke(handCards);
        Debug.Log($"[Card] 移除卡牌：{card.cardName}，当前手牌：{handCards.Count}/{maxHandSize}");
        return true;
    }

    /// <summary>
    /// 根据索引移除卡牌
    /// </summary>
    public bool RemoveCardAt(int index)
    {
        if (index < 0 || index >= handCards.Count)
        {
            return false;
        }

        var card = handCards[index];
        handCards.RemoveAt(index);
        OnHandChanged?.Invoke(handCards);
        Debug.Log($"[Card] 移除卡牌：{card.cardName}（索引{index}），当前手牌：{handCards.Count}/{maxHandSize}");
        return true;
    }

    /// <summary>
    /// 获取指定索引的卡牌
    /// </summary>
    public CardData GetCard(int index)
    {
        if (index < 0 || index >= handCards.Count)
        {
            return null;
        }
        return handCards[index];
    }

    /// <summary>
    /// 清空手牌
    /// </summary>
    public void ClearHand()
    {
        handCards.Clear();
        OnHandChanged?.Invoke(handCards);
        Debug.Log("[Card] 清空手牌");
    }

    /// <summary>
    /// 重新加载初始手牌
    /// </summary>
    [ContextMenu("Reload Initial Hand")]
    public void ReloadInitialHand()
    {
        InitializeHand();
    }

        void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
