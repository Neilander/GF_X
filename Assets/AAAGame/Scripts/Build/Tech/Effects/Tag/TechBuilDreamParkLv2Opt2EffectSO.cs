using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_DreamPark_Lv2_Opt2
/// ScopeType: Tag
/// 策划描述: 我方远程单位射程+<val1>
/// UniqueValues: 100
/// TagScope: UnitTag.Ranged
/// </summary>
public class TechBuilDreamParkLv2Opt2EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.UnitTypes.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilDreamParkLv2Opt2EffectSO)}] ResolvedScope 无 UnitTypes, techId={context.TechId}");
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

        // 所有策划字段均已实现

        var modules = new List<BuffCallback>
        {
            new RangeBonusBuff(v0),
        };

        return BuffData.Create(
            id: $"unit_tech_{techId}_{unitType}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
