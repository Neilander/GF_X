using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 建筑产出条件统计管理器
/// 处理兵力、建筑、击杀数的动态统计
/// </summary>
public class ProductionConditionManager : GameFrameworkComponent
{
    private Dictionary<string, int> _troopCountByStronghold = new Dictionary<string, int>();
    private Dictionary<string, int> _buildingCountByStronghold = new Dictionary<string, int>();
    private Dictionary<string, int> _killCountByStronghold = new Dictionary<string, int>();
    
    /// <summary>
    /// 统计同据点兵力
    /// </summary>
    public int GetTroopCountInStronghold(Stronghold stronghold)
    {
        if (stronghold == null) return 0;
        
        string key = stronghold.strongholdData?.StrongholdId ?? "unknown";
        if (_troopCountByStronghold.ContainsKey(key))
        {
            return _troopCountByStronghold[key];
        }
        
        // 如果没有缓存，计算实际兵力
        int troopCount = CalculateActualTroopCount(stronghold);
        _troopCountByStronghold[key] = troopCount;
        return troopCount;
    }
    
    /// <summary>
    /// 统计同据点同类建筑数量
    /// </summary>
    public int GetBuildingCountInStronghold(Stronghold stronghold, string buildingType)
    {
        if (stronghold == null || string.IsNullOrEmpty(buildingType)) return 0;
        
        string key = $"{stronghold.strongholdData?.StrongholdId ?? "unknown"}_{buildingType}";
        if (_buildingCountByStronghold.ContainsKey(key))
        {
            return _buildingCountByStronghold[key];
        }
        
        // 如果没有缓存，计算实际建筑数量
        int buildingCount = CalculateActualBuildingCount(stronghold, buildingType);
        _buildingCountByStronghold[key] = buildingCount;
        return buildingCount;
    }
    
    /// <summary>
    /// 统计击杀数（按天重置）
    /// </summary>
    public int GetKillCountForStronghold(Stronghold stronghold, int day)
    {
        if (stronghold == null) return 0;
        
        string key = $"{stronghold.strongholdData?.StrongholdId ?? "unknown"}_{day}";
        if (_killCountByStronghold.ContainsKey(key))
        {
            return _killCountByStronghold[key];
        }
        
        // 如果没有缓存，返回0（需要从游戏数据中获取）
        return 0;
    }
    
    /// <summary>
    /// 在阶段切换时更新统计
    /// </summary>
    public void OnPhaseChanged(GamePhase newPhase)
    {
        if (newPhase == GamePhase.Build)
        {
            // 建造阶段开始时更新所有统计
            UpdateAllProductionConditions();
        }
    }
    
    /// <summary>
    /// 更新所有产出条件统计
    /// </summary>
    private void UpdateAllProductionConditions()
    {
        // 清空缓存，强制重新计算
        _troopCountByStronghold.Clear();
        _buildingCountByStronghold.Clear();
        
        // 这里可以添加通知所有建筑更新产出的逻辑
        Debug.Log("[ProductionConditionManager] 更新所有产出条件统计");
    }
    
    /// <summary>
    /// 计算据点的实际兵力
    /// </summary>
    private int CalculateActualTroopCount(Stronghold stronghold)
    {
        // 这里需要实现实际的兵力统计逻辑
        // 暂时返回一个模拟值用于测试
        return 10; // 模拟10个兵力
    }
    
    /// <summary>
    /// 计算据点的实际建筑数量
    /// </summary>
    private int CalculateActualBuildingCount(Stronghold stronghold, string buildingType)
    {
        // 这里需要实现实际的建筑统计逻辑
        // 暂时返回一个模拟值用于测试
        if (buildingType == "Buil_ParcelLocker")
        {
            return 1; // 模拟1个其他快递柜
        }
        return 0;
    }
    
    /// <summary>
    /// 设置击杀数（由游戏事件触发）
    /// </summary>
    public void SetKillCountForStronghold(Stronghold stronghold, int day, int killCount)
    {
        if (stronghold == null) return;
        
        string key = $"{stronghold.strongholdData?.StrongholdId ?? "unknown"}_{day}";
        _killCountByStronghold[key] = killCount;
        
        Debug.Log($"[ProductionConditionManager] 设置击杀数: 据点={stronghold.strongholdData?.StrongholdId ?? "unknown"}, 天数={day}, 击杀={killCount}");
    }
    
    /// <summary>
    /// 清空所有统计
    /// </summary>
    public void ClearAllStatistics()
    {
        _troopCountByStronghold.Clear();
        _buildingCountByStronghold.Clear();
        _killCountByStronghold.Clear();
        
        Debug.Log("[ProductionConditionManager] 清空所有统计");
    }
}