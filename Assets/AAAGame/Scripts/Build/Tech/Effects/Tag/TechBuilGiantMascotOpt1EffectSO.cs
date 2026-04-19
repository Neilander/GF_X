using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_GiantMascot_Opt1
/// ScopeType: Tag
/// 策划描述: 我方远程单位攻击距离增加<val1>%
/// UniqueValues: 15
/// TagScope: UnitTag.Ranged
/// </summary>
public class TechBuilGiantMascotOpt1EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.UnitTypes.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilGiantMascotOpt1EffectSO)}] ResolvedScope 无 UnitTypes, techId={context.TechId}");
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

        var modules = new List<BuffCallback>
        {
            new PercentRangeBonusBuff(v0 / (Fix64)100),  // +15% Range
        };

        return BuffData.Create(
            id: $"pct_range_{techId}_{unitType}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
