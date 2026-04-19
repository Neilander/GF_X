using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_InterviewRoom_Lv3_Opt2
/// ScopeType: SelfBuil
/// 策划描述: 兵力+<val1>，寿命+<val2>秒
/// UniqueValues: 6,20
/// </summary>
public class TechBuilInterviewRoomLv3Opt2EffectSO : HybridBuildingTechEffectSO
{
    protected override bool HasUnitBuff => true;

    protected override void ApplyExtraProps(BuildingExtraProps extra, TechData td)
    {
        if (td?.UniqueValues != null && td.UniqueValues.Length > 0)
        {
            extra.ArmyForce += td.UniqueValues[0];
        }
    }

    public override BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        Fix64 v1 = (techData?.UniqueValues != null && techData.UniqueValues.Length > 1) ? techData.UniqueValues[1] : Fix64.Zero;

        var modules = new List<BuffCallback>
        {
            new LifetimeDeltaBuff(v1),  // 寿命+20s
        };

        return BuffData.Create(
            id: $"building_tech_{techId}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }
}
