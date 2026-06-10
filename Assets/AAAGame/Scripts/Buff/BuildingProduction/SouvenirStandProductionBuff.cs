using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 纪念品摊产出Buff
/// 按同据点内兵力计数增加产出，上限3
/// </summary>
public class SouvenirStandProductionBuff : BuffCallback
{
    private const int DefaultBonusPerTroop = 5; // 每5兵力+1产出
    private const int DefaultMaxBonus = 3;      // 上限3
    
    public override void OnAdd()
    {
        var building = hostEntity as BuildingEntity;
        if (IsSouvenirStand(building))
        {
            // 设置产出类型
            building.SetProductionType(ProductionType.ByTroopCount);
            
            // 初始更新一次产出
            UpdateTroopCountProduction();
            
            // 监听实体变化事件
            GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnEntityChanged);
            GF.Event.Subscribe(HideEntityCompleteEventArgs.EventId, OnEntityChanged);
            GF.Event.Subscribe(SoldierDeadEventArgs.EventId, OnEntityChanged);
            GF.Event.Subscribe(BuildingDisabledStateChangedEventArgs.EventId, OnEntityChanged);
        }
    }
    
    public override void OnRemove()
    {
        if (GF.Event != null)
        {
            GF.Event.Unsubscribe(ShowEntitySuccessEventArgs.EventId, OnEntityChanged);
            GF.Event.Unsubscribe(HideEntityCompleteEventArgs.EventId, OnEntityChanged);
            GF.Event.Unsubscribe(SoldierDeadEventArgs.EventId, OnEntityChanged);
            GF.Event.Unsubscribe(BuildingDisabledStateChangedEventArgs.EventId, OnEntityChanged);
        }
        base.OnRemove();
    }

    private void OnEntityChanged(object sender, GameFramework.Event.GameEventArgs e)
    {
        UpdateTroopCountProduction();
    }
    
    private void UpdateTroopCountProduction()
    {
        var building = hostEntity as BuildingEntity;
        if (!IsSouvenirStand(building))
            return;
        
        int bonusPerTroop = GetConfiguredBonusPerTroop(building);
        int maxBonus = GetConfiguredMaxBonus(building);

        // 计算同据点内兵力
        int troopCount = CalculateTroopCountInStronghold(building);
        building.SetConditionCount(troopCount);
        
        // 计算产出加成
        int bonus = Mathf.Min(troopCount / Mathf.Max(1, bonusPerTroop), maxBonus);
        building.SetDynamicProduction(bonus);
    }
    
    private int CalculateTroopCountInStronghold(BuildingEntity building)
    {
        // 获取建筑所在的据点
        var stronghold = building.CurrentStronghold;
        if (stronghold == null) return 0;
        
        // 使用统计管理器获取兵力
        var manager = GameEntry.GetComponent<ProductionConditionManager>();
        if (manager != null)
        {
            return manager.GetTroopCountInStronghold(stronghold);
        }
        
        return 0;
    }

    private static bool IsSouvenirStand(BuildingEntity building)
    {
        return ProductionTraitUtility.IsBuilding(building, "Buil_SouvenirStand");
    }

    private static int GetConfiguredBonusPerTroop(BuildingEntity building)
    {
        return ProductionTraitUtility.GetUniqueInt(building, 0, DefaultBonusPerTroop);
    }

    private static int GetConfiguredMaxBonus(BuildingEntity building)
    {
        return ProductionTraitUtility.GetUniqueInt(building, 2, DefaultMaxBonus)
               + ProductionTraitUtility.GetLevelTechValueSum(building, 0);
    }
}
