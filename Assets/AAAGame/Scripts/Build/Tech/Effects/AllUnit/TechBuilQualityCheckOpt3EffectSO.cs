using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_QualityCheck_Opt3
/// ScopeType: AllUnit
/// 策划描述: 我方单位生命上限提升<val1>%，但移动速度下降<val2>%
/// UniqueValues: 25,20
/// </summary>
public class TechBuilQualityCheckOpt3EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.UnitTypes.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilQualityCheckOpt3EffectSO)}] ResolvedScope 无 UnitTypes, techId={context.TechId}");
            return;
        }

        foreach (var unitType in context.ResolvedScope.UnitTypes)
        {
            context.GlobalBuffManager.RegisterUnitBuff(unitType, context.OwnerFactionId, context.TechId, this, context.TechData);
        }
    }

    public override BuffData CreateUnitInitialBuff(TechData techData, UnitType unitType, string techId)
    {
        Fix64 v0 = (techData?.UniqueValues != null && techData.UniqueValues.Length > 0) ? techData.UniqueValues[0] : Fix64.Zero;
        Fix64 v1 = (techData?.UniqueValues != null && techData.UniqueValues.Length > 1) ? techData.UniqueValues[1] : Fix64.Zero;

        var modules = new List<BuffCallback>
        {
            new PercentHealthBonusBuff(v0 / (Fix64)100),          // +25% HP
            new PercentMoveSpeedBonusBuff(-(v1 / (Fix64)100)),    // -20% Speed
        };

        return BuffData.Create(
            id: $"pct_hp_speed_{techId}_{unitType}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
