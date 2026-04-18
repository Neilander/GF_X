using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_BallPit_Lv3_Opt2
/// ScopeType: SelfBuil
/// 策划描述: 不听话的触发间隔+<val1>秒，生命上限+<val2>
/// UniqueValues: 3,80
/// </summary>
public class TechBuilBallPitLv3Opt2EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.BuildingInstanceIds.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilBallPitLv3Opt2EffectSO)}] ResolvedScope 无 BuildingInstanceIds, techId={context.TechId}");
            return;
        }

        foreach (var buildingInstanceId in context.ResolvedScope.BuildingInstanceIds)
        {
            context.GlobalBuffManager.RegisterBuildingBuff(buildingInstanceId, context.OwnerFactionId, context.TechId, this, context.TechData);
        }
    }

    public override BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        Fix64 v1 = (techData?.UniqueValues != null && techData.UniqueValues.Length > 1) ? techData.UniqueValues[1] : Fix64.Zero;

        // TODO: 未实现字段 -> 不听话的触发间隔+<val1>秒
        Debug.LogWarning($"[{nameof(TechBuilBallPitLv3Opt2EffectSO)}] 未实现字段: 不听话的触发间隔+<val1>秒 (techId={techId})");

        var modules = new List<BuffCallback>
        {
            new FlatHealthBonusBuff(v1),
        };

        return BuffData.Create(
            id: $"building_tech_{techId}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
