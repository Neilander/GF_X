using System.Collections.Generic;
using UnityEngine;

public interface ITargetingComp:ICapability
{
    void Init(IEntityContext ctx);
    void UpdateTargeting(Fix64 deltaTime);

    // 供外部（如 AI Brain 或 UI）读取的当前目标
    IEntityContext CurrentTarget { get; set; }
    IEntityContext FollowTarget { get; }

    Fix64 AggroRangeFixed { get; set; }
    Fix64 ForgetRangeFixed { get; set; }
    Fix64 FollowSearchRangeFixed { get; set; }
    /// <summary>友军告警传播半径：自己扫描到敌人时通知此半径内同阵营的友军。</summary>
    Fix64 AlertRadiusFixed { get; set; }

    /// <summary>
    /// 受击通知：用于"视线外仇恨"——记下打我的人，scan 找不到敌人时 fallback 到这个 attacker。
    /// 已锁定一个 attacker 后再次通知会被忽略（坚持追第一次的）。
    /// </summary>
    void NotifyDamageTaken(IEntityContext attacker);

    /// <summary>友军告警通知：友军扫描到敌人，把信息传给我。规则同受击仇恨（已锁定不覆盖；自己 scan 到就清）。</summary>
    void NotifyAllyFoundEnemy(IEntityContext enemy);

    /// <summary>清掉受击/告警仇恨记忆（如：进入返航、目标丢失等）。</summary>
    void ClearAggro();
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
