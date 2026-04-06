using UnityEngine;
using System;

namespace AAAGame.Card
{
    /// <summary>
    /// 人口管理器 - 完全独立版本
    /// 不依赖任何 Temp_script 代码
    /// </summary>
    public class PopulationManager : MonoBehaviour
{
    private static PopulationManager instance;
    public static PopulationManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<PopulationManager>();
            }
            return instance;
        }
    }

    [Header("人口设置")]
    [SerializeField] private int maxPopulation = 20;
    [SerializeField] private int currentPopulation = 0;

    private CardSystemController cardSystemController;

    public int MaxPopulation => maxPopulation;
    public int CurrentPopulation => currentPopulation;
    public int AvailablePopulation => maxPopulation - currentPopulation;

    // 人口变化事件（兼容 Temp_script）
    public event Action<int, int> OnPopulationChanged; // (current, max)

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
        // 初始化时触发一次事件
        OnPopulationChanged?.Invoke(currentPopulation, maxPopulation);
        Debug.Log($"[Card] 人口管理器初始化：{currentPopulation}/{maxPopulation}");
    }

    /// <summary>
    /// 设置卡牌系统控制器（由 CardGameManager 调用）
    /// </summary>
    public void SetCardSystemController(CardSystemController controller)
    {
        cardSystemController = controller;
        
        // 同步人口上限到 Controller
        if (cardSystemController != null)
        {
            cardSystemController.SetMaxPopulation(maxPopulation);
        }
    }

    /// <summary>
    /// 检查是否有足够人口
    /// </summary>
    public bool HasEnoughPopulation(int cost)
    {
        return currentPopulation + cost <= maxPopulation;
    }

    /// <summary>
    /// 占用人口
    /// </summary>
    public bool OccupyPopulation(int cost)
    {
        if (!HasEnoughPopulation(cost))
        {
            Debug.LogWarning($"[Card] 人口不足！需要 {cost}，当前可用 {AvailablePopulation}");
            return false;
        }

        currentPopulation += cost;
        OnPopulationChanged?.Invoke(currentPopulation, maxPopulation);
        Debug.Log($"[Card] 占用人口 {cost}，当前：{currentPopulation}/{maxPopulation}");
        return true;
    }

    /// <summary>
    /// 释放人口
    /// </summary>
    public void ReleasePopulation(int cost)
    {
        currentPopulation = Mathf.Max(0, currentPopulation - cost);
        OnPopulationChanged?.Invoke(currentPopulation, maxPopulation);
        Debug.Log($"[Card] 释放人口 {cost}，当前：{currentPopulation}/{maxPopulation}");
    }

    /// <summary>
    /// 设置人口上限
    /// </summary>
    public void SetMaxPopulation(int max)
    {
        maxPopulation = max;
        OnPopulationChanged?.Invoke(currentPopulation, maxPopulation);
        
        // 同步到 Controller
        if (cardSystemController != null)
        {
            cardSystemController.SetMaxPopulation(maxPopulation);
        }
    }

    /// <summary>
    /// 重置人口
    /// </summary>
    public void ResetPopulation()
    {
        currentPopulation = 0;
        OnPopulationChanged?.Invoke(currentPopulation, maxPopulation);
        Debug.Log("[Card] 人口已重置");
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
