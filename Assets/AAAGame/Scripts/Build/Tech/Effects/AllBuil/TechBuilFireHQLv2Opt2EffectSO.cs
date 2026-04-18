using UnityEngine;

/// <summary>
/// TechID: Tech_Buil_FireHQ_Lv2_Opt2
/// ScopeType: AllBuil
/// 策划描述: 我方建筑在每个防御阶段开始时获得<val1>护盾
/// UniqueValues: 50
/// </summary>
public class TechBuilFireHQLv2Opt2EffectSO : TechEffectSO
{
    public override void Activate(TechEffectContext context)
    {
        UnityEngine.Debug.LogWarning("[TechBuilFireHQLv2Opt2EffectSO] AllBuil scope 尚未实现 Manager/Resolver 通道。");
        // TODO: implement once AllBuil pipeline is in place
    }
}
