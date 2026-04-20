using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_QualityCheck_Opt4
/// ScopeType: AllUnit
/// 策划描述: 我方单位对生命值低于<val1>%的生物单位造成的攻击伤害提升<val2>点。
/// UniqueValues: 40,2
/// 行为:
///   - 给我方单位挂 ConditionalLowHpDamageBonusBuff
///   - 伤害时由 DamageHelper 的 ModifyOutgoingDamage 钩子触发条件判断
/// </summary>
public class TechBuilQualityCheckOpt4EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.UnitTypes.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilQualityCheckOpt4EffectSO)}] ResolvedScope 无 UnitTypes, techId={context.TechId}");
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
            new ConditionalLowHpDamageBonusBuff(v0, v1),
        };

        return BuffData.Create(
            id: $"unit_{techId}_{unitType}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
