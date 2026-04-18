using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_FireHQ_Lv3_Opt2
/// ScopeType: AllUnit
/// 策划描述: 我方单位的攻击命中超过<val1>个单位时，每多命中1个就使当次攻击+<val2>
/// UniqueValues: 1,1
/// </summary>
public class TechBuilFireHQLv3Opt2EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.UnitTypes.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilFireHQLv3Opt2EffectSO)}] ResolvedScope 无 UnitTypes, techId={context.TechId}");
            return;
        }

        foreach (var unitType in context.ResolvedScope.UnitTypes)
        {
            context.GlobalBuffManager.RegisterUnitBuff(unitType, context.OwnerFactionId, context.TechId, this, context.TechData);
        }
    }

    public override BuffData CreateUnitInitialBuff(TechData techData, UnitType unitType, string techId)
    {
        // 无需读取 UniqueValues

        Debug.LogWarning($"[{nameof(TechBuilFireHQLv3Opt2EffectSO)}] 未实现字段: 多目标命中额外加攻击（特殊机制未实现） (techId={techId})");

        var modules = new List<BuffCallback>
        {
            // 无可挂载的单位 Buff（字段全部属于未实现类别）
        };

        return BuffData.Create(
            id: $"unit_tech_{techId}_{unitType}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
