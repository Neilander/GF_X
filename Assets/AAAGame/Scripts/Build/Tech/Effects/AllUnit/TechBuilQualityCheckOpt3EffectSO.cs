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
        // 无需读取 UniqueValues

        Debug.LogWarning($"[{nameof(TechBuilQualityCheckOpt3EffectSO)}] 未实现字段: 生命上限+<val1>%（百分比机制未实现） (techId={techId})");
        Debug.LogWarning($"[{nameof(TechBuilQualityCheckOpt3EffectSO)}] 未实现字段: 移动速度-<val2>%（百分比机制未实现） (techId={techId})");

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
