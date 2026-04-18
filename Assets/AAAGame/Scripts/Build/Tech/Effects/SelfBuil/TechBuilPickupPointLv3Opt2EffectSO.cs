using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_PickupPoint_Lv3_Opt2
/// ScopeType: SelfBuil
/// 策划描述: 兵力-<val1>，生命上限+<val2>
/// UniqueValues: 2,60
/// </summary>
public class TechBuilPickupPointLv3Opt2EffectSO : HybridBuildingTechEffectSO
{
    protected override bool HasUnitBuff => true;

    protected override void ApplyExtraProps(BuildingExtraProps extra, TechData td)
    {
        if (td?.UniqueValues != null && td.UniqueValues.Length > 0)
        {
            extra.ArmyForce += -td.UniqueValues[0];
        }
    }

    public override BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        Fix64 v1 = (techData?.UniqueValues != null && techData.UniqueValues.Length > 1) ? techData.UniqueValues[1] : Fix64.Zero;

        // 所有单位 Buff 字段均已实现

        var modules = new System.Collections.Generic.List<BuffCallback>
        {
            new FlatHealthBonusBuff(v1),
        };

        return BuffData.Create(
            id: $"building_tech_{techId}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
