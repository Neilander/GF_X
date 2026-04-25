using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 纪念品摊产出Buff
/// 按同据点内兵力计数增加产出，上限3
/// </summary>
public class SouvenirStandProductionBuff : BuffCallback
{
    private const int BonusPerTroop = 5; // 每5兵力+1产出
    private const int MaxBonus = 3;      // 上限3
    
    public override void OnAdd()
    {
        var building = hostEntity as BuildingEntity;
        if (building?.buildingData?.Identifier == "Buil_SouvenirStand")
        {
            // 设置产出类型
            building.SetProductionType(ProductionType.ByTroopCount);
            building.SetProductionCap(MaxBonus);
            
            // 初始更新一次产出
            UpdateTroopCountProduction();
        }
    }
    
    public override void OnUpdate(float deltaTime)
    {
        // 每帧更新兵力计数和产出计算
        UpdateTroopCountProduction();
    }
    
    private void UpdateTroopCountProduction()
    {
        var building = hostEntity as BuildingEntity;
        if (building == null || building.buildingData?.Identifier != "Buil_SouvenirStand") 
            return;
        
        // 计算同据点内兵力
        int troopCount = CalculateTroopCountInStronghold(building);
        building.SetConditionCount(troopCount);
        
        // 计算产出加成
        int bonus = Mathf.Min(troopCount / BonusPerTroop, MaxBonus);
        building.SetDynamicProduction(bonus);
        
        Debug.Log($"[SouvenirStand] 兵力: {troopCount}, 加成: +{bonus}, 上限: {MaxBonus}");
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
        
        // 如果管理器不存在，返回模拟值
        return 10; // 模拟10个兵力
    }
}