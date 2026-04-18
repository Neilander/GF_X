using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_InsuranceOffice_Lv3
/// ScopeType: SelfBuil
/// 策划描述: 日产出+<val1>
/// UniqueValues: 1
/// </summary>
public class TechBuilInsuranceOfficeLv3EffectSO : HybridBuildingTechEffectSO
{
    protected override void ApplyExtraProps(BuildingExtraProps extra, TechData td)
    {
        if (td?.UniqueValues != null && td.UniqueValues.Length > 0)
        {
            extra.Production += td.UniqueValues[0];
        }
    }
}
