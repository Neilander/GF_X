using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_InterviewRoom_Lv2_Opt1
/// ScopeType: SelfBuil
/// 策划描述: 兵力+<val1>
/// UniqueValues: 5
/// </summary>
public class TechBuilInterviewRoomLv2Opt1EffectSO : HybridBuildingTechEffectSO
{
    protected override void ApplyExtraProps(BuildingExtraProps extra, TechData td)
    {
        if (td?.UniqueValues != null && td.UniqueValues.Length > 0)
        {
            extra.ArmyForce += td.UniqueValues[0];
        }
    }
}
