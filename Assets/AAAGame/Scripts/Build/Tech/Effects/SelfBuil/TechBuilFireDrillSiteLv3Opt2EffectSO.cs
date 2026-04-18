using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_FireDrillSite_Lv3_Opt2
/// ScopeType: SelfBuil
/// 策划描述: 攻击速度+<val1>，溅射半径-<val2>码
/// UniqueValues: 90,75
/// </summary>
public class TechBuilFireDrillSiteLv3Opt2EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.BuildingInstanceIds.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilFireDrillSiteLv3Opt2EffectSO)}] ResolvedScope 无 BuildingInstanceIds, techId={context.TechId}");
            return;
        }

        foreach (var buildingInstanceId in context.ResolvedScope.BuildingInstanceIds)
        {
            context.GlobalBuffManager.RegisterBuildingBuff(buildingInstanceId, context.OwnerFactionId, context.TechId, this, context.TechData);
        }
    }

    public override BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        Fix64 v0 = (techData?.UniqueValues != null && techData.UniqueValues.Length > 0) ? techData.UniqueValues[0] : Fix64.Zero;

        // TODO: 未实现字段 -> 溅射半径-<val2>码（武器 SplashRadius）
        Debug.LogWarning($"[{nameof(TechBuilFireDrillSiteLv3Opt2EffectSO)}] 未实现字段: 溅射半径-<val2>码（武器 SplashRadius） (techId={techId})");

        var modules = new List<BuffCallback>
        {
            new AttackSpeedBonusBuff(v0),
        };

        return BuffData.Create(
            id: $"building_tech_{techId}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
