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
        // 无需读取 UniqueValues

        Debug.LogWarning($"[{nameof(TechBuilServerRoomOpt2EffectSO)}] 未实现字段: 实习生和码农寿命+<val1>%（寿命机制未实现） (techId={techId})");

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
