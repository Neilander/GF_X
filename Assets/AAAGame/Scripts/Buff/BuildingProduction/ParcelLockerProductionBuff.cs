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
        var building = hostEntity as BuildingEntity;
        if (building?.buildingData?.Identifier != null && building.buildingData.Identifier.Contains("ParcelLocker"))
        {
            // 设置产出类型
            building.SetProductionType(ProductionType.BySameBuildingCount);
            
            // 初始更新一次产出
            UpdateBuildingCountProduction();
            
            // 监听实体变化事件
            GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnEntityChanged);
            GF.Event.Subscribe(HideEntityCompleteEventArgs.EventId, OnEntityChanged);
            GF.Event.Subscribe(BuildingDisabledStateChangedEventArgs.EventId, OnEntityChanged);
        }
    }
    
    public override void OnRemove()
    {
        if (GF.Event != null)
        {
            GF.Event.Unsubscribe(ShowEntitySuccessEventArgs.EventId, OnEntityChanged);
            GF.Event.Unsubscribe(HideEntityCompleteEventArgs.EventId, OnEntityChanged);
            GF.Event.Unsubscribe(BuildingDisabledStateChangedEventArgs.EventId, OnEntityChanged);
        }
        base.OnRemove();
    }

    private void OnEntityChanged(object sender, GameFramework.Event.GameEventArgs e)
    {
        UpdateBuildingCountProduction();
    }
    
    private void UpdateBuildingCountProduction()
    {
        var building = hostEntity as BuildingEntity;
        if (building == null || building.buildingData?.Identifier == null || !building.buildingData.Identifier.Contains("ParcelLocker")) 
        {
            return;
        }
        
        int bonusPerBuilding = GetConfiguredBonusPerBuilding(building);
        int maxBonus = GetConfiguredMaxBonus(building);
        
        // 计算同据点内其他快递柜数量
        int otherLockerCount = CalculateOtherParcelLockersInStronghold(building);
        building.SetConditionCount(otherLockerCount);
        
        // 计算产出加成
        int bonus = Mathf.Min(otherLockerCount * bonusPerBuilding, maxBonus);
        building.SetDynamicProduction(bonus);
    }
    
    private int CalculateOtherParcelLockersInStronghold(BuildingEntity building)
    {
        // 获取建筑所在的据点
        var stronghold = building.CurrentStronghold;
        if (stronghold == null)
        {
            return 0;
        }
        
        // 使用统计管理器获取建筑数量（排除自身）
        var manager = GameEntry.GetComponent<ProductionConditionManager>();
        if (manager != null)
        {
            // 传完整前缀，和 ProductionConditionManager 的 StartsWith 规则保持一致。
            int totalCount = manager.GetBuildingCountInStronghold(stronghold, "Buil_ParcelLocker");
            
            // 排除自身
            int otherCount = Mathf.Max(0, totalCount - 1);
            
            return otherCount;
        }
        else
        {
            int totalCount = CountParcelLockersByStrongholdBuildings(stronghold);
            int otherCount = Mathf.Max(0, totalCount - 1);
            return otherCount;
        }
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
        return ProductionTraitUtility.GetUniqueInt(building, 0, DefaultBonusPerBuilding);
    }

    private static int GetConfiguredMaxBonus(BuildingEntity building)
    {
        return ProductionTraitUtility.GetUniqueInt(building, 1, DefaultMaxBonus)
               + ProductionTraitUtility.GetProdLevelTechValueSum(building, 0);
    }
}
