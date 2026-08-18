using System.Collections.Generic;
using UnityEngine;

public interface ITargetingComp:ICapability
{
    void Init(IEntityContext ctx);
    void UpdateTargeting(Fix64 deltaTime);

    // CurrentTarget 仅表示当前可见并可供攻击的目标；AggroTarget 还可包含最后目击追踪中的目标。
    IEntityContext CurrentTarget { get; set; }
    IEntityContext AggroTarget { get; }
    void ClearAggro();
}

public interface ITargetSearchRangeComp
{
    Fix64 AggroRangeFixed { get; set; }
    Fix64 ForgetRangeFixed { get; set; }
}

public interface IFollowTargetingComp
{
    IEntityContext FollowTarget { get; }
    Fix64 FollowSearchRangeFixed { get; set; }
}

public interface IAlertTargetingComp
{
    void NotifyAllyFoundEnemy(IEntityContext enemy);
}

public interface IDefendTargetingModeComp
{
    void UseDefaultMode();
    void UseDefendEnemyMode(IEntityContext fallbackTarget);
}

public interface INavigationReachabilityTargetingComp
{
    void RejectNavigationUnreachableTarget(IEntityContext target);
}

public static class LogicTargetingRange
{
    public static Fix64 Require(Fix64 value, string name)
    {
        if (value < Fix64.Zero)
            throw new System.ArgumentOutOfRangeException(name, value, "Targeting range must be non-negative.");
        return value;
    }
}

public interface IMultiTargetingComp
{
    IReadOnlyList<IEntityContext> CurrentTargets { get; }
}
