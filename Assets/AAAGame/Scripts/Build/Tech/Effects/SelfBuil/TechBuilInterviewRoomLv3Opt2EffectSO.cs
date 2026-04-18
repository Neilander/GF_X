using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_InterviewRoom_Lv3_Opt2
/// ScopeType: SelfBuil
/// 策划描述: 兵力+<val1>，寿命+<val2>秒
/// UniqueValues: 6,20
/// </summary>
public class TechBuilInterviewRoomLv3Opt2EffectSO : HybridBuildingTechEffectSO
{
    protected override void ApplyExtraProps(BuildingExtraProps extra, TechData td)
    {
        if (td?.UniqueValues != null && td.UniqueValues.Length > 0)
        {
            extra.ArmyForce += td.UniqueValues[0];
        }
    }

    public override BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        Debug.LogWarning($"[{nameof(TechBuilInterviewRoomLv3Opt2EffectSO)}] 未实现字段: 寿命+<val2>秒 (techId={techId})");
        return null;
    }
}
