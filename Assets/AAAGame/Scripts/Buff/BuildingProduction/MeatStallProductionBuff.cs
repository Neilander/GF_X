using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 肉摊产出Buff
/// 按前一天击杀敌人计数增加产出，上限2
/// </summary>
public class MeatStallProductionBuff : BuffCallback
{
    private const int KillsPerBonus = 8; // 每击杀8名敌人+1产出
    private const int MaxBonus = 2;      // 上限2
    
    private int _previousDayKills = 0;
    
    public override void OnAdd()
    {
        var building = hostEntity as BuildingEntity;
        if (building?.buildingData?.Identifier == "Buil_MeatStall")
        {
            // 设置产出类型
            building.SetProductionType(ProductionType.ByKillCount);
            building.SetProductionCap(MaxBonus);
            
            // 初始更新一次产出
            UpdateKillCountProduction();
        }
    }
    
    public override void OnUpdate(float deltaTime)
    {
        // 每帧更新击杀计数和产出计算
        UpdateKillCountProduction();
    }
    
    private void UpdateKillCountProduction()
    {
        var building = hostEntity as BuildingEntity;
        if (building == null || building.buildingData?.Identifier != "Buil_MeatStall") 
            return;
        
        // 获取前一天的击杀数
        int killCount = GetPreviousDayKillCount(building);
        building.SetConditionCount(killCount);
        
        // 计算产出加成
        int bonus = Mathf.Min(killCount / KillsPerBonus, MaxBonus);
        building.SetDynamicProduction(bonus);
        
        Debug.Log($"[MeatStall] 前一天击杀: {killCount}, 加成: +{bonus}, 上限: {MaxBonus}");
    }
    
    private int GetPreviousDayKillCount(BuildingEntity building)
    {
        // 获取建筑所在的据点
        var stronghold = building.CurrentStronghold;
        if (stronghold == null) return 0;
        
        // 使用统计管理器获取前一天击杀数
        var manager = GameEntry.GetComponent<ProductionConditionManager>();
        if (manager != null)
        {
            int currentDay = GetCurrentDay();
            int previousDay = Mathf.Max(1, currentDay - 1);
            return manager.GetKillCountForStronghold(stronghold, previousDay);
        }
        
        // 如果管理器不存在，返回模拟值
        return 8; // 模拟8个击杀
    }
    
    private int GetCurrentDay()
    {
        // 这里需要实现获取当前天数的逻辑
        // 暂时返回一个模拟值用于测试
        return 1; // 模拟第1天
    }
}