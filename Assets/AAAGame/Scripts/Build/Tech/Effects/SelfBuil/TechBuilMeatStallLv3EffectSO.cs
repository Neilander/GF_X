using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_MeatStall_Lv3
/// ScopeType: SelfBuil
/// 策划描述: 日产出+<val1>，上限+<val2>
/// UniqueValues: 1,1
/// </summary>
public class TechBuilMeatStallLv3EffectSO : HybridBuildingTechEffectSO
{
    protected override void ApplyExtraProps(BuildingExtraProps extra, TechData td)
    {
        if (td?.UniqueValues != null && td.UniqueValues.Length > 0)
        {
            extra.Production += td.UniqueValues[0];
        }
    }

    public override BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        Debug.LogWarning($"[{nameof(TechBuilMeatStallLv3EffectSO)}] 未实现字段: 上限+<val2>（建筑属性） (techId={techId})");
        return null;
    }
}
