using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_InsuranceOffice_Lv2
/// ScopeType: SelfBuil
/// 策划描述: 日产出+<val1>
/// UniqueValues: 1
/// </summary>
public class TechBuilInsuranceOfficeLv2EffectSO : HybridBuildingTechEffectSO
{
    protected override void ApplyExtraProps(BuildingExtraProps extra, TechData td)
    {
        // 日产出由升级后的 BuildingData.Production 读取，避免与生产 buff 重复结算。
    }
}
