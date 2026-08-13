using UnityEngine;
using System.Collections.Generic;

public interface ITechEffectRuntime
{
    void Activate(TechEffectContext context);
    BuffData CreateUnitInitialBuff(TechData techData, UnitType unitType, string techId);
    List<BuffCallback> CreateBuildingScopedModules(TechData techData, string techId);
}

/// <summary>
/// 科技效果定义。
/// Activate 负责把效果注册到运行时系统；
/// 若该效果会给新出生单位附加初始 Buff，则通过 CreateUnitInitialBuff 提供新的 BuffData 实例。
/// </summary>
public abstract class TechEffectSO : ScriptableObject, ITechEffectRuntime
{
    public abstract void Activate(TechEffectContext context);

    public virtual BuffData CreateUnitInitialBuff(TechData techData, UnitType unitType, string techId)
    {
        return null;
    }

    public virtual List<BuffCallback> CreateBuildingScopedModules(TechData techData, string techId)
    {
        return null;
    }
}
