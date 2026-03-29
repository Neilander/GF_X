using UnityEngine;
using UnityGameFramework.Runtime;
using System;

/// <summary>
/// 人口管理器 - 管理当前人口和人口上限
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

    public int MaxPopulation => maxPopulation;
    public int CurrentPopulation => currentPopulation;
    public int AvailablePopulation => maxPopulation - currentPopulation;

    // 人口变化事件
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
        GF.Log($"人口管理器初始化：{currentPopulation}/{maxPopulation}");
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
            GF.LogWarning($"人口不足！需要 {cost}，当前可用 {AvailablePopulation}");
            return false;
        }

        currentPopulation += cost;
        OnPopulationChanged?.Invoke(currentPopulation, maxPopulation);
        GF.Log($"占用人口 {cost}，当前：{currentPopulation}/{maxPopulation}");
        return true;
    }

    /// <summary>
    /// 释放人口
    /// </summary>
    public void ReleasePopulation(int cost)
    {
        currentPopulation = Mathf.Max(0, currentPopulation - cost);
        OnPopulationChanged?.Invoke(currentPopulation, maxPopulation);
        GF.Log($"释放人口 {cost}，当前：{currentPopulation}/{maxPopulation}");
    }

    /// <summary>
    /// 设置人口上限
    /// </summary>
    public void SetMaxPopulation(int max)
    {
        maxPopulation = max;
        OnPopulationChanged?.Invoke(currentPopulation, maxPopulation);
    }

    /// <summary>
    /// 重置人口
    /// </summary>
    public void ResetPopulation()
    {
        currentPopulation = 0;
        OnPopulationChanged?.Invoke(currentPopulation, maxPopulation);
        GF.Log("人口已重置");
    }

    void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }
}
