using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 肉摊动态产出科技效果
/// 激活肉摊的击杀计数产出机制
/// </summary>
public class TechBuilMeatStallDynamicEffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        Debug.Log($"[TechBuilMeatStallDynamicEffectSO] 激活肉摊动态产出机制");
    }

    public override BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        var modules = new List<BuffCallback>
        {
            new MeatStallProductionBuff()
        };

        return BuffData.Create(
            id: $"building_dynamic_meatstall_{techId}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}