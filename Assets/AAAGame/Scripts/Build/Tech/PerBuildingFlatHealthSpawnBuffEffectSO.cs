using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-building 科技效果：命中 SelfBuil scope 的建筑生成的卡牌所产出的单位，
/// 出生时额外获得 +N 生命值。
/// 注册目标来自 ResolvedScope.BuildingInstanceIds（Resolver 按 SelfBuil 解析得到）。
/// 值优先取 TechData.UniqueValues[0]，否则走默认。
/// </summary>
[CreateAssetMenu(menuName = "AAAGame/Tech Effects/Per-Building Flat Health Spawn Buff")]
public class PerBuildingFlatHealthSpawnBuffEffectSO : TechEffectSO
{
    [SerializeField] private float defaultBonusHealth = 400f;

    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.BuildingInstanceIds.Count == 0)
        {
            Debug.LogWarning($"[PerBuildingFlatHealthSpawnBuffEffectSO] ResolvedScope 无 BuildingInstanceIds, techId={context.TechId}");
            return;
        }

        foreach (var buildingInstanceId in context.ResolvedScope.BuildingInstanceIds)
        {
            context.GlobalBuffManager.RegisterBuildingBuff(buildingInstanceId, context.OwnerFactionId, context.TechId, this, context.TechData);
        }
    }

    public override BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        Fix64 bonusHealth = ResolveBonusHealth(techData);
        return BuffData.Create(
            id: $"building_tech_hp_{techId}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: new List<BuffCallback> { new FlatHealthBonusBuff(bonusHealth) });
    }

    private Fix64 ResolveBonusHealth(TechData techData)
    {
        if (techData?.UniqueValues != null && techData.UniqueValues.Length > 0)
            return techData.UniqueValues[0];

        return (Fix64)defaultBonusHealth;
    }
}
