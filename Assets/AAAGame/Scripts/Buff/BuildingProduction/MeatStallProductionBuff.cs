using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 肉摊产出Buff
/// 按前一天击杀敌人计数增加产出，上限2
/// </summary>
public class MeatStallProductionBuff : BuffCallback
{
    private const int DefaultKillsPerBonus = 8; // 每击杀8名敌人+1产出
    private const int DefaultBonusPerStep = 1;
    private const int DefaultMaxBonus = 2;      // 上限2
    
    public override void OnAdd()
    {
        var building = hostEntity as BuildingEntity;
        if (IsMeatStall(building))
        {
            int maxBonus = GetConfiguredMaxBonus(building);

            // 设置产出类型
            building.SetProductionType(ProductionType.ByKillCount);
            building.SetProductionCap(maxBonus);
            
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
        if (!IsMeatStall(building))
            return;
        
        int killsPerBonus = GetConfiguredKillsPerBonus(building);
        int bonusPerStep = GetConfiguredBonusPerStep(building);
        int maxBonus = GetConfiguredMaxBonus(building);

        // 获取前一天的击杀数
        int killCount = GetPreviousDayKillCount(building);
        building.SetConditionCount(killCount);
        
        // 计算产出加成
        int bonus = Mathf.Min((killCount / Mathf.Max(1, killsPerBonus)) * Mathf.Max(1, bonusPerStep), maxBonus);
        building.SetDynamicProduction(bonus);
        
        Debug.Log($"[MeatStall] 前一天击杀: {killCount}, 阈值: {killsPerBonus}, 加成步长: {bonusPerStep}, 加成: +{bonus}, 上限: {maxBonus}");
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
        
        return 0;
    }
    
    private int GetCurrentDay()
    {
        return Mathf.Max(1, InGameDataModel.GetValue(IngameValueType.Day));
    }

    private static bool IsMeatStall(BuildingEntity building)
    {
        return building?.buildingData?.Identifier != null
               && building.buildingData.Identifier.Contains("MeatStall");
    }

    private static int GetConfiguredKillsPerBonus(BuildingEntity building)
    {
        if (building?.buildingData?.UniqueValues != null && building.buildingData.UniqueValues.Length > 0)
        {
            return Mathf.Max(1, (int)building.buildingData.UniqueValues[0]);
        }

        return DefaultKillsPerBonus;
    }

    private static int GetConfiguredBonusPerStep(BuildingEntity building)
    {
        if (building?.buildingData?.UniqueValues != null && building.buildingData.UniqueValues.Length > 1)
        {
            return Mathf.Max(1, (int)building.buildingData.UniqueValues[1]);
        }

        return DefaultBonusPerStep;
    }

    private static int GetConfiguredMaxBonus(BuildingEntity building)
    {
        if (building?.buildingData?.UniqueValues != null && building.buildingData.UniqueValues.Length > 2)
        {
            return Mathf.Max(0, (int)building.buildingData.UniqueValues[2]);
        }

        return DefaultMaxBonus;
    }
}