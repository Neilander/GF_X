using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 快递柜产出Buff
/// 按同据点内其他快递柜计数增加产出，上限2
/// </summary>
public class ParcelLockerProductionBuff : BuffCallback
{
    private const int BonusPerBuilding = 2; // 每个其他快递柜+2产出
    private const int MaxBonus = 2;         // 上限2
    
    public override void OnAdd()
    {
        var building = hostEntity as BuildingEntity;
        if (building?.buildingData?.Identifier == "Buil_ParcelLocker")
        {
            // 设置产出类型
            building.SetProductionType(ProductionType.BySameBuildingCount);
            building.SetProductionCap(MaxBonus);
            
            // 初始更新一次产出
            UpdateBuildingCountProduction();
        }
    }
    
    public override void OnUpdate(float deltaTime)
    {
        // 每帧更新建筑计数和产出计算
        UpdateBuildingCountProduction();
    }
    
    private void UpdateBuildingCountProduction()
    {
        var building = hostEntity as BuildingEntity;
        if (building == null || building.buildingData?.Identifier != "Buil_ParcelLocker") 
            return;
        
        // 计算同据点内其他快递柜数量
        int otherLockerCount = CalculateOtherParcelLockersInStronghold(building);
        building.SetConditionCount(otherLockerCount);
        
        // 计算产出加成
        int bonus = Mathf.Min(otherLockerCount * BonusPerBuilding, MaxBonus);
        building.SetDynamicProduction(bonus);
        
        Debug.Log($"[ParcelLocker] 其他快递柜: {otherLockerCount}, 加成: +{bonus}, 上限: {MaxBonus}");
    }
    
    private int CalculateOtherParcelLockersInStronghold(BuildingEntity building)
    {
        // 获取建筑所在的据点
        var stronghold = building.CurrentStronghold;
        if (stronghold == null) return 0;
        
        // 使用统计管理器获取建筑数量（排除自身）
        var manager = GameEntry.GetComponent<ProductionConditionManager>();
        if (manager != null)
        {
            int totalCount = manager.GetBuildingCountInStronghold(stronghold, "Buil_ParcelLocker");
            // 排除自身
            return Mathf.Max(0, totalCount - 1);
        }
        
        // 如果管理器不存在，返回模拟值
        return 1; // 模拟1个其他快递柜
    }
}