using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_DeliveryHub_Lv3_Opt2
/// ScopeType: SelfBuil
/// 策划描述: 兵力-<val1>，攻击造成分裂效果（切换为武器2）
/// UniqueValues: 1
/// </summary>
public class TechBuilDeliveryHubLv3Opt2EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.BuildingInstanceIds.Count == 0)
        {
            Debug.LogWarning($"[{nameof(TechBuilDeliveryHubLv3Opt2EffectSO)}] ResolvedScope 无 BuildingInstanceIds, techId={context.TechId}");
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

        // TODO: 未实现字段 -> 兵力-<val1>（建筑属性）
        // TODO: 未实现字段 -> 攻击造成分裂效果（武器切换）
        Debug.LogWarning($"[{nameof(TechBuilDeliveryHubLv3Opt2EffectSO)}] 未实现字段: 兵力-<val1>（建筑属性） (techId={techId})");
        Debug.LogWarning($"[{nameof(TechBuilDeliveryHubLv3Opt2EffectSO)}] 未实现字段: 攻击造成分裂效果（武器切换） (techId={techId})");

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
