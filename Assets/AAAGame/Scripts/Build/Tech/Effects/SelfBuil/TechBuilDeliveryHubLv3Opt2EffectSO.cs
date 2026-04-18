using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_DeliveryHub_Lv3_Opt2
/// ScopeType: SelfBuil
/// 策划描述: 兵力-<val1>，攻击造成分裂效果（切换为武器2）
/// UniqueValues: 1
/// </summary>
public class TechBuilDeliveryHubLv3Opt2EffectSO : HybridBuildingTechEffectSO
{
    protected override void ApplyExtraProps(BuildingExtraProps extra, TechData td)
    {
        if (td?.UniqueValues != null && td.UniqueValues.Length > 0)
        {
            extra.ArmyForce += -td.UniqueValues[0];
        }
    }

    public override BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        Debug.LogWarning($"[{nameof(TechBuilDeliveryHubLv3Opt2EffectSO)}] 未实现字段: 攻击造成分裂效果（武器切换） (techId={techId})");
        return null;
    }
}
