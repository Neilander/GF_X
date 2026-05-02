using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 矿卡阵列动态产出科技效果
/// 激活矿卡的递减产出机制
/// </summary>
public class TechBuilMiningRigDynamicEffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        Debug.Log($"[TechBuilMiningRigDynamicEffectSO] 激活矿卡阵列动态产出机制");
    }

    public override BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        var modules = new List<BuffCallback>
        {
            new MiningRigProductionBuff()
        };

        return BuffData.Create(
            id: $"building_dynamic_miningrig_{techId}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}