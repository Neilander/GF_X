using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_BallPit_Lv3_Opt1
/// ScopeType: SelfBuil
/// 策划描述: 兵力+<val1>，不听话的触发间隔-<val1>秒
/// UniqueValues: 3,1
/// </summary>
public class TechBuilBallPitLv3Opt1EffectSO : HybridBuildingTechEffectSO
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
        Debug.LogWarning($"[{nameof(TechBuilBallPitLv3Opt1EffectSO)}] 未实现字段: 不听话的触发间隔-<val1>秒 (techId={techId})");
        return null;
    }
}
