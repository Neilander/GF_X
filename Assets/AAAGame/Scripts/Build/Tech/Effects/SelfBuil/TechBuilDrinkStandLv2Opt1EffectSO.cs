using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_DrinkStand_Lv2_Opt1
/// ScopeType: SelfBuil
/// 策划描述: 攻击+<val1>，生命上限+<val2>
/// UniqueValues: 3,25
/// </summary>
public class TechBuilDrinkStandLv2Opt1EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.BuildingInstanceIds.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilDrinkStandLv2Opt1EffectSO)}] ResolvedScope 无 BuildingInstanceIds, techId={context.TechId}");
            return;
        }

        foreach (var buildingInstanceId in context.ResolvedScope.BuildingInstanceIds)
        {
            context.GlobalBuffManager.RegisterBuildingBuff(buildingInstanceId, context.OwnerFactionId, context.TechId, this, context.TechData);
        }
    }

    public override BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        Fix64 atkBonus = Fix64.Zero;
        Fix64 hpBonus = Fix64.Zero;
        if (techData?.UniqueValues != null)
        {
            if (techData.UniqueValues.Length > 0) atkBonus = techData.UniqueValues[0];
            if (techData.UniqueValues.Length > 1) hpBonus = techData.UniqueValues[1];
        }

        return BuffData.Create(
            id: $"building_tech_{techId}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: new List<BuffCallback>
            {
                new FlatAttackBonusBuff(atkBonus),
                new FlatHealthBonusBuff(hpBonus),
            });
    }
}
