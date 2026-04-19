using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_InterviewRoom_Lv3_Opt1
/// ScopeType: SelfBuil
/// 策划描述: 寿命-<val1>秒，攻击+<val2>
/// UniqueValues: 15,10
/// </summary>
public class TechBuilInterviewRoomLv3Opt1EffectSO : HybridBuildingTechEffectSO
{
    protected override bool HasUnitBuff => true;

    public override BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        Fix64 v0 = (techData?.UniqueValues != null && techData.UniqueValues.Length > 0) ? techData.UniqueValues[0] : Fix64.Zero;
        Fix64 v1 = (techData?.UniqueValues != null && techData.UniqueValues.Length > 1) ? techData.UniqueValues[1] : Fix64.Zero;

        var modules = new List<BuffCallback>
        {
            new LifetimeDeltaBuff(-v0),     // 寿命-15s
            new FlatAttackBonusBuff(v1),    // 攻击+10
        };

        return BuffData.Create(
            id: $"building_tech_{techId}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
