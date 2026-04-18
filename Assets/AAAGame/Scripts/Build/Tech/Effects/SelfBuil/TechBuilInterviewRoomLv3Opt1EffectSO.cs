using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_InterviewRoom_Lv3_Opt1
/// ScopeType: SelfBuil
/// 策划描述: 寿命-<val1>秒，攻击+<val2>
/// UniqueValues: 15,10
/// </summary>
public class TechBuilInterviewRoomLv3Opt1EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.BuildingInstanceIds.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilInterviewRoomLv3Opt1EffectSO)}] ResolvedScope 无 BuildingInstanceIds, techId={context.TechId}");
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

        // TODO: 未实现字段 -> 寿命-<val1>秒
        Debug.LogWarning($"[{nameof(TechBuilInterviewRoomLv3Opt1EffectSO)}] 未实现字段: 寿命-<val1>秒 (techId={techId})");

        var modules = new List<BuffCallback>
        {
            new FlatAttackBonusBuff(v1),
        };

        return BuffData.Create(
            id: $"building_tech_{techId}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
