using UnityEngine;

public class NoTargetingComp : ITargetingComp
{
    // 永远没有目标
    public IEntityContext CurrentTarget { get; set; }
    public IEntityContext FollowTarget => null;

    // 范围属性给 0 即可
    public float AggroRange { get; set; } = 0f;
    public float ForgetRange { get; set; } = 0f;
    public float FollowSearchRange { get; set; } = 0f;
    public float AlertRadius { get; set; } = 0f;

    // 接口方法留空
    public void Init(IEntityContext ctx) { }

    public void UpdateTargeting(float deltaTime) { }

    public void ShutDown() { }

    public void Resume() { }

    public void NotifyDamageTaken(IEntityContext attacker) { }

    public void NotifyAllyFoundEnemy(IEntityContext enemy) { }

    public void ClearAggro() { }
}
