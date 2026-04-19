using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_Slaughterhouse_Lv3_Opt2
/// ScopeType: SelfBuil
/// 策划描述: 生命上限+<val1>，杀敌回复比例+<val2>
/// UniqueValues: 100,2
/// </summary>
public class TechBuilSlaughterhouseLv3Opt2EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.BuildingInstanceIds.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilSlaughterhouseLv3Opt2EffectSO)}] ResolvedScope 无 BuildingInstanceIds, techId={context.TechId}");
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

        // 杀敌回复: 新建一个独立 id 的 OnKillHealBuff 挂上，和单位默认的 "on_kill_heal"
        // 用不同 id 独立共存；每次 OnKill 各自触发各自的百分比回血。
        var killHealBuff = new OnKillHealBuff();
        killHealBuff.SetHealPercent((float)v1);

        var modules = new List<BuffCallback>
        {
            new FlatHealthBonusBuff(v0),   // HP +100
            killHealBuff,                  // 杀敌回复 +2%
        };

        return BuffData.Create(
            id: $"building_tech_{techId}",  // id 独立，不会和默认 "on_kill_heal" 冲突
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
