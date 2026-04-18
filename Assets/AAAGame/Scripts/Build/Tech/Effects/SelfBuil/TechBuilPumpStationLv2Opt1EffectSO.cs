using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_PumpStation_Lv2_Opt1
/// ScopeType: SelfBuil
/// 策划描述: 射程和分裂距离+<val1>码，攻击速度+<val2>
/// UniqueValues: 150,25
/// </summary>
public class TechBuilPumpStationLv2Opt1EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.BuildingInstanceIds.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilPumpStationLv2Opt1EffectSO)}] ResolvedScope 无 BuildingInstanceIds, techId={context.TechId}");
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
        Fix64 v1 = (techData?.UniqueValues != null && techData.UniqueValues.Length > 1) ? techData.UniqueValues[1] : Fix64.Zero;

        // TODO: 未实现字段 -> 分裂距离+<val1>码（武器 SplitDist）
        Debug.LogWarning($"[{nameof(TechBuilPumpStationLv2Opt1EffectSO)}] 未实现字段: 分裂距离+<val1>码（武器 SplitDist） (techId={techId})");

        var modules = new List<BuffCallback>
        {
            new RangeBonusBuff(v0),
            new AttackSpeedBonusBuff(v1),
        };

        return BuffData.Create(
            id: $"building_tech_{techId}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
