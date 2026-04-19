using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_QualityCheck_Opt2
/// ScopeType: AllUnit
/// 策划描述: 获得额外<val1>人口上限，但我方单位护甲降低<val2>
/// UniqueValues: 10,1
/// </summary>
public class TechBuilQualityCheckOpt2EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.UnitTypes.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilQualityCheckOpt2EffectSO)}] ResolvedScope 无 UnitTypes, techId={context.TechId}");
            return;
        }

        foreach (var unitType in context.ResolvedScope.UnitTypes)
        {
            context.GlobalBuffManager.RegisterUnitBuff(unitType, context.OwnerFactionId, context.TechId, this, context.TechData);
        }
    }

    public override BuffData CreateUnitInitialBuff(TechData techData, UnitType unitType, string techId)
    {
        Fix64 v1 = (techData?.UniqueValues != null && techData.UniqueValues.Length > 1) ? techData.UniqueValues[1] : Fix64.Zero;

        Debug.LogWarning($"[{nameof(TechBuilQualityCheckOpt2EffectSO)}] 未实现字段: 人口上限+<val1>（全局资源属性，未实现） (techId={techId})");

        var modules = new List<BuffCallback>
        {
            new FlatDefBonusBuff(-v1),
        };

        return BuffData.Create(
            id: $"unit_tech_{techId}_{unitType}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
