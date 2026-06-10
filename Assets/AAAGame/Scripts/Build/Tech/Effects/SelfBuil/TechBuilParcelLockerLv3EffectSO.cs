using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_ParcelLocker_Lv3
/// ScopeType: SelfBuil
/// 策划描述: 上限+<val1>
/// UniqueValues: 2
/// </summary>
public class TechBuilParcelLockerLv3EffectSO : HybridBuildingTechEffectSO
{
    protected override void ApplyExtraProps(BuildingExtraProps extra, TechData techData)
    {
        // 生产特性上限由生产 buff 按建筑等级直接读取 BuildingTable。
    }
}
