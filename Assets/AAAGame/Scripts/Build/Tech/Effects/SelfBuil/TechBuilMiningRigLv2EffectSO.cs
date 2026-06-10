using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_MiningRig_Lv2
/// ScopeType: SelfBuil
/// 策划描述: 等级基础产量提升
/// UniqueValues:
/// </summary>
public class TechBuilMiningRigLv2EffectSO : HybridBuildingTechEffectSO
{
    protected override void ApplyExtraProps(BuildingExtraProps extra, TechData td)
    {
        // 基础产量由升级后的 BuildingData.Production 提供。
    }
}
