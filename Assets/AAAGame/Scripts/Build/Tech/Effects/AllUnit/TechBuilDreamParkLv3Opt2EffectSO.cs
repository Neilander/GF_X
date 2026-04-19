using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_DreamPark_Lv3_Opt2
/// ScopeType: AllUnit
/// 策划描述: 每次丢弃卡牌时，使本次战斗阶段期间我方单位攻击速度+<val1>
/// UniqueValues: 10
/// </summary>
public class TechBuilDreamParkLv3Opt2EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.UnitTypes.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilDreamParkLv3Opt2EffectSO)}] ResolvedScope 无 UnitTypes, techId={context.TechId}");
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

        Debug.LogWarning($"[{nameof(TechBuilDreamParkLv3Opt2EffectSO)}] 未实现字段: 丢弃卡牌时本阶段攻速+<val1>（条件触发机制未实现） (techId={techId})");

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
