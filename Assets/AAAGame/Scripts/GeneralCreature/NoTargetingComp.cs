using UnityEngine;

public class NoTargetingComp : ITargetingComp
{
    // 永远没有目标
    public IEntityContext CurrentTarget { get; set; }
    public IEntityContext FollowTarget => null;

    // 范围属性给 0 即可
    public Fix64 AggroRangeFixed { get; set; }
    public Fix64 ForgetRangeFixed { get; set; }
    public Fix64 FollowSearchRangeFixed { get; set; }
    public Fix64 AlertRadiusFixed { get; set; }
    // 接口方法留空
    public void Init(IEntityContext ctx) { }

    public void UpdateTargeting(Fix64 deltaTime) { }

    public void ShutDown() { }

    public void Resume() { }

    public void NotifyDamageTaken(IEntityContext attacker) { }

    public void NotifyAllyFoundEnemy(IEntityContext enemy) { }

    public void ClearAggro() { }
}
