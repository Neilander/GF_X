using System;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 快递柜产出Buff
/// 按同据点内其他快递柜计数增加产出，上限2
/// </summary>
public class ParcelLockerProductionBuff : BuffCallback
{
    private const int DefaultBonusPerBuilding = 2; // 每个其他快递柜+2产出
    private const int DefaultMaxBonus = 2;         // 上限2
    
    public override void OnAdd()
    {
        Debug.Log($"[ParcelLocker] OnAdd开始执行，hostEntity类型: {hostEntity?.GetType().Name}");
        
        var building = hostEntity as BuildingEntity;
        if (building?.buildingData?.Identifier != null && building.buildingData.Identifier.Contains("ParcelLocker"))
        {
            Debug.Log($"[ParcelLocker] 检测到快递柜建筑: {building.buildingData.Identifier}");
            int maxBonus = GetConfiguredMaxBonus(building);
            
            // 设置产出类型
            building.SetProductionType(ProductionType.BySameBuildingCount);
            building.SetProductionCap(maxBonus);
            Debug.Log($"[ParcelLocker] 设置产出类型和上限完成");
            
            // 初始更新一次产出
            UpdateBuildingCountProduction();
            Debug.Log($"[ParcelLocker] OnAdd执行完成");
        }
        else
        {
            Debug.LogError($"[ParcelLocker] 建筑类型不匹配或为空: {building?.buildingData?.Identifier}");
        }
    }
    
    public override void OnUpdate(float deltaTime)
    {
        // 每帧更新建筑计数和产出计算
        UpdateBuildingCountProduction();
    }
    
    private void UpdateBuildingCountProduction()
    {
        Debug.Log($"[ParcelLocker] UpdateBuildingCountProduction开始执行");
        
        var building = hostEntity as BuildingEntity;
        if (building == null || building.buildingData?.Identifier == null || !building.buildingData.Identifier.Contains("ParcelLocker")) 
        {
            Debug.LogError($"[ParcelLocker] 建筑为空或类型不匹配: {building?.buildingData?.Identifier}");
            return;
        }
        
        Debug.Log($"[ParcelLocker] 开始计算其他快递柜数量");
        int bonusPerBuilding = GetConfiguredBonusPerBuilding(building);
        int maxBonus = GetConfiguredMaxBonus(building);
        
        // 计算同据点内其他快递柜数量
        int otherLockerCount = CalculateOtherParcelLockersInStronghold(building);
        building.SetConditionCount(otherLockerCount);
        
        // 计算产出加成
        int bonus = Mathf.Min(otherLockerCount * bonusPerBuilding, maxBonus);
        building.SetDynamicProduction(bonus);
        
        Debug.Log($"[ParcelLocker] 其他快递柜: {otherLockerCount}, 单栋加成: {bonusPerBuilding}, 加成: +{bonus}, 上限: {maxBonus}");
        Debug.Log($"[ParcelLocker] UpdateBuildingCountProduction执行完成");
    }
    
    private int CalculateOtherParcelLockersInStronghold(BuildingEntity building)
    {
        Debug.Log($"[ParcelLocker] 开始计算其他快递柜数量，当前建筑: {building?.buildingData?.Identifier}");
        
        // 获取建筑所在的据点
        var stronghold = building.CurrentStronghold;
        if (stronghold == null)
        {
            Debug.Log($"[ParcelLocker] 无法获取据点，返回0");
            return 0;
        }
        
        Debug.Log($"[ParcelLocker] 据点ID: {stronghold?.strongholdData?.StrongholdId}");
        
        // 使用统计管理器获取建筑数量（排除自身）
        var manager = GameEntry.GetComponent<ProductionConditionManager>();
        if (manager != null)
        {
            // 传完整前缀，和 ProductionConditionManager 的 StartsWith 规则保持一致。
            int totalCount = manager.GetBuildingCountInStronghold(stronghold, "Buil_ParcelLocker");
            Debug.Log($"[ParcelLocker] 据点内快递柜总数: {totalCount}");
            
            // 排除自身
            int otherCount = Mathf.Max(0, totalCount - 1);
            Debug.Log($"[ParcelLocker] 其他快递柜数量: {otherCount}");
            
            return otherCount;
        }
        else
        {
            Debug.LogWarning("[ParcelLocker] ProductionConditionManager is null, fallback to stronghold list counting.");
            int totalCount = CountParcelLockersByStrongholdBuildings(stronghold);
            int otherCount = Mathf.Max(0, totalCount - 1);
            Debug.Log($"[ParcelLocker] Fallback counted parcel lockers: total={totalCount}, other={otherCount}");
            return otherCount;
        }

        return 0;
    }

    private static int CountParcelLockersByStrongholdBuildings(Stronghold stronghold)
    {
        if (stronghold?.Buildings == null || stronghold.Buildings.Count == 0)
            return 0;

        int count = 0;
        for (int i = 0; i < stronghold.Buildings.Count; i++)
        {
            var candidate = stronghold.Buildings[i];
            if (candidate == null || candidate.buildingData == null || !candidate.Alive || candidate.IsDisabled)
                continue;

            string identifier = candidate.buildingData.Identifier;
            if (string.IsNullOrWhiteSpace(identifier))
                continue;

            if (!identifier.StartsWith("Buil_ParcelLocker", StringComparison.Ordinal))
                continue;

            count++;
        }

        return count;
    }

    private static int GetConfiguredBonusPerBuilding(BuildingEntity building)
    {
        if (building?.buildingData?.UniqueValues != null && building.buildingData.UniqueValues.Length > 0)
        {
            return Mathf.Max(1, (int)building.buildingData.UniqueValues[0]);
        }

        return DefaultBonusPerBuilding;
    }

    private static int GetConfiguredMaxBonus(BuildingEntity building)
    {
        if (building?.buildingData?.UniqueValues != null && building.buildingData.UniqueValues.Length > 1)
        {
            return Mathf.Max(0, (int)building.buildingData.UniqueValues[1]);
        }

        return DefaultMaxBonus;
    }
}