using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 当前默认科技效果：为命中的单位类型注册“出生时额外 +400 血量”的初始 Buff。
/// 若 TechData.UniqueValues[0] 有值，则优先使用该值。
/// </summary>
[CreateAssetMenu(menuName = "AAAGame/Tech Effects/Flat Health Spawn Buff")]
public class FlatHealthSpawnBuffEffectSO : TechEffectSO
{
    [SerializeField] private float defaultBonusHealth = 400f;

    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        // 正确逻辑：只对科技 scope 命中的单位类型生效。
        // 切回时，把下面这个 foreach 放开，并注释掉后面的“全体单位测试逻辑”。
        // if (context.ResolvedScope == null)
        //     return;
        //
        // foreach (var unitType in context.ResolvedScope.UnitTypes)
        // {
        //     context.GlobalBuffManager.RegisterUnitBuff(unitType, context.OwnerFactionId, context.TechId, this, context.TechData);
        // }

        // 全体单位测试逻辑：当前用于快速验证 buff 生效链路。
        foreach (UnitType unitType in System.Enum.GetValues(typeof(UnitType)))
        {
            context.GlobalBuffManager.RegisterUnitBuff(unitType, context.OwnerFactionId, context.TechId, this, context.TechData);
        }
    }

    public override BuffData CreateUnitInitialBuff(TechData techData, UnitType unitType, string techId)
    {
        Fix64 bonusHealth = ResolveBonusHealth(techData);
        return BuffData.Create(
            id: $"global_tech_hp_{techId}_{unitType}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: new List<BuffCallback> { new FlatHealthBonusBuff(bonusHealth) });
    }

    private Fix64 ResolveBonusHealth(TechData techData)
    {
        if (techData?.UniqueValues != null && techData.UniqueValues.Length > 0)
            return techData.UniqueValues[0];

        return (Fix64)defaultBonusHealth;
    }
}
