using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_MiningRig_Lv2
/// ScopeType: SelfBuil
/// 策划描述: 日产出+<val1>
/// UniqueValues: 2
/// </summary>
public class TechBuilMiningRigLv2EffectSO : HybridBuildingTechEffectSO
{
    protected override void ApplyExtraProps(BuildingExtraProps extra, TechData td)
    {
        if (td?.UniqueValues != null && td.UniqueValues.Length > 0)
        {
            extra.Production += td.UniqueValues[0];
        }
    }
}
