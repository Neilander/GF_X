using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 纪念品摊动态产出科技效果
/// 激活纪念品摊的兵力计数产出机制
/// </summary>
public class TechBuilSouvenirStandDynamicEffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        Debug.Log($"[TechBuilSouvenirStandDynamicEffectSO] 激活纪念品摊动态产出机制");
    }

    public override BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        var modules = new List<BuffCallback>
        {
            new SouvenirStandProductionBuff()
        };

        return BuffData.Create(
            id: $"building_dynamic_souvenirstand_{techId}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}