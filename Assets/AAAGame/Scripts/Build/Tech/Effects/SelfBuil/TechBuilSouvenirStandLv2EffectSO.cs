using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_SouvenirStand_Lv2
/// ScopeType: SelfBuil
/// 策划描述: 特性上限+<val1>
/// UniqueValues: 1
/// </summary>
public class TechBuilSouvenirStandLv2EffectSO : HybridBuildingTechEffectSO
{
    protected override void ApplyExtraProps(BuildingExtraProps extra, TechData td)
    {
        // 生产特性上限由生产 buff 按建筑等级直接读取 BuildingTable，避免与升级后的 BuildingData 重复结算。
    }
}
