using UnityEngine;

/// <summary>
/// 科技效果定义。
/// Activate 负责把效果注册到运行时系统；
/// 若该效果会给新出生单位附加初始 Buff，则通过 CreateUnitInitialBuff 提供新的 BuffData 实例。
/// </summary>
public abstract class TechEffectSO : ScriptableObject
{
    public abstract void Activate(TechEffectContext context);

    public virtual BuffData CreateUnitInitialBuff(TechData techData, UnitType unitType, string techId)
    {
        return null;
    }

    /// <summary>
    /// Per-building scope 用的初始 Buff 工厂。
    /// 由 GlobalBuffManager.GetBuffsForBuilding 在单位出生时调用。
    /// 不需要 UnitType，作用域已经靠 BuildingInstanceId 限定。
    /// </summary>
    public virtual BuffData CreateBuildingScopedBuff(TechData techData, string techId)
    {
        return null;
    }
}
