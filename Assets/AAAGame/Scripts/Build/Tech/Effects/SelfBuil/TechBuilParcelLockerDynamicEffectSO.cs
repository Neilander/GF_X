using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 快递柜动态产出科技效果
/// 激活快递柜的建筑计数产出机制
/// </summary>
public class TechBuilParcelLockerDynamicEffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        Debug.Log($"[TechBuilParcelLockerDynamicEffectSO] 激活快递柜动态产出机制");
    }

    public override BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        var modules = new List<BuffCallback>
        {
            new ParcelLockerProductionBuff()
        };

        return BuffData.Create(
            id: $"building_dynamic_parcellocker_{techId}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}