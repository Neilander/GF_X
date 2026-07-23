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
    public float AggroRange { get => (float)AggroRangeFixed; set => AggroRangeFixed = LogicTargetingRange.FromFloat(value, nameof(AggroRange)); }
    public float ForgetRange { get => (float)ForgetRangeFixed; set => ForgetRangeFixed = LogicTargetingRange.FromFloat(value, nameof(ForgetRange)); }
    public float FollowSearchRange { get => (float)FollowSearchRangeFixed; set => FollowSearchRangeFixed = LogicTargetingRange.FromFloat(value, nameof(FollowSearchRange)); }
    public float AlertRadius { get => (float)AlertRadiusFixed; set => AlertRadiusFixed = LogicTargetingRange.FromFloat(value, nameof(AlertRadius)); }

    // 接口方法留空
    public void Init(IEntityContext ctx) { }

    public void UpdateTargeting(Fix64 deltaTime) { }

    public void ShutDown() { }

    public void Resume() { }

    public void NotifyDamageTaken(IEntityContext attacker) { }

    public void NotifyAllyFoundEnemy(IEntityContext enemy) { }

    public void ClearAggro() { }
}
