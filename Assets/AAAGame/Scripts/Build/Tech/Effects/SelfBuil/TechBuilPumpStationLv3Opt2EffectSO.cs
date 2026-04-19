using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_PumpStation_Lv3_Opt2
/// ScopeType: SelfBuil
/// 策划描述: 分裂角度+<val1>°，射程和分裂距离+<val2>码
/// UniqueValues: 30150
/// </summary>
public class TechBuilPumpStationLv3Opt2EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.BuildingInstanceIds.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilPumpStationLv3Opt2EffectSO)}] ResolvedScope 无 BuildingInstanceIds, techId={context.TechId}");
            return;
        }

        foreach (var buildingInstanceId in context.ResolvedScope.BuildingInstanceIds)
        {
            context.GlobalBuffManager.RegisterBuildingBuff(buildingInstanceId, context.OwnerFactionId, context.TechId, this, context.TechData);
        }
    }

    public override BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        // 无需读取 UniqueValues

        // TODO: 未实现字段 -> 分裂角度+<val1>°（武器 SplitAngle）
        // TODO: 未实现字段 -> 射程和分裂距离+<val2>码（UniqueValues 表里疑似配置错误:30150）
        Debug.LogWarning($"[{nameof(TechBuilPumpStationLv3Opt2EffectSO)}] 未实现字段: 分裂角度+<val1>°（武器 SplitAngle） (techId={techId})");
        Debug.LogWarning($"[{nameof(TechBuilPumpStationLv3Opt2EffectSO)}] 未实现字段: 射程和分裂距离+<val2>码（UniqueValues 表里疑似配置错误:30150） (techId={techId})");

        var modules = new List<BuffCallback>
        {
            // 无可挂载的单位 Buff（字段全部属于未实现类别）
        };

        return BuffData.Create(
            id: $"building_tech_{techId}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
