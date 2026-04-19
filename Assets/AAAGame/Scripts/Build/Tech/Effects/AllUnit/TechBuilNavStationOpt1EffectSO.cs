using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_NavStation_Opt1
/// ScopeType: AllUnit
/// 策划描述: 我方单位移动速度增加<val1>%，并且能抵抗<val2>%的减速效果，不过护甲降低<val3>。
/// UniqueValues: 20,40,1
/// </summary>
public class TechBuilNavStationOpt1EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.UnitTypes.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilNavStationOpt1EffectSO)}] ResolvedScope 无 UnitTypes, techId={context.TechId}");
            return;
        }

        foreach (var unitType in context.ResolvedScope.UnitTypes)
        {
            context.GlobalBuffManager.RegisterUnitBuff(unitType, context.OwnerFactionId, context.TechId, this, context.TechData);
        }
    }

    public override BuffData CreateUnitInitialBuff(TechData techData, UnitType unitType, string techId)
    {
        Fix64 v2 = (techData?.UniqueValues != null && techData.UniqueValues.Length > 2) ? techData.UniqueValues[2] : Fix64.Zero;

        Debug.LogWarning($"[{nameof(TechBuilNavStationOpt1EffectSO)}] 未实现字段: 移动速度+<val1>%（百分比机制未实现） (techId={techId})");
        Debug.LogWarning($"[{nameof(TechBuilNavStationOpt1EffectSO)}] 未实现字段: 减速抗性+<val2>%（减速抗性机制未实现） (techId={techId})");

        var modules = new List<BuffCallback>
        {
            new FlatDefBonusBuff(-v2),
        };

        return BuffData.Create(
            id: $"unit_tech_{techId}_{unitType}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
