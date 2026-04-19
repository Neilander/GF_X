using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_SortingCenter_Lv3_Opt2
/// ScopeType: AllUnit
/// 策划描述: 我方单位被召唤的前<val1>秒内，移动速度+<val2>%，护甲+<val3>
/// UniqueValues: 10,25,2
/// </summary>
public class TechBuilSortingCenterLv3Opt2EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.UnitTypes.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilSortingCenterLv3Opt2EffectSO)}] ResolvedScope 无 UnitTypes, techId={context.TechId}");
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

        Debug.LogWarning($"[{nameof(TechBuilSortingCenterLv3Opt2EffectSO)}] 未实现字段: 召唤前<val1>秒内移速+<val2>%、护甲+<val3>（定时增益机制未实现；护甲已按永久挂上） (techId={techId})");

        var modules = new List<BuffCallback>
        {
            new FlatDefBonusBuff(v2),
        };

        return BuffData.Create(
            id: $"unit_tech_{techId}_{unitType}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
