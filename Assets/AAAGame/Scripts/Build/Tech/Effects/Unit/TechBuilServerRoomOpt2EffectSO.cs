using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_ServerRoom_Opt2
/// ScopeType: Unit
/// 策划描述: 实习生和码农的寿命增加<val1>%
/// UniqueValues: 100
/// UnitScope: Unit_Coder,Unit_Intern
/// </summary>
public class TechBuilServerRoomOpt2EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.UnitTypes.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilServerRoomOpt2EffectSO)}] ResolvedScope 无 UnitTypes, techId={context.TechId}");
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

        // val1=100 表示 100%，换算成 1.0
        Fix64 percent = v0 / (Fix64)100;

        var modules = new List<BuffCallback>
        {
            new LifetimePercentBuff(percent),
        };

        return BuffData.Create(
            id: $"unit_tech_{techId}_{unitType}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
