using UnityEngine;

public class NoTargetingComp : ITargetingComp
{
    // 永远没有目标
    public CompCreature CurrentTarget => null;
    public CompCreature FollowTarget { get; }

    // 范围属性给 0 即可
    public float AggroRange { get; set; } = 0f;
    public float ForgetRange { get; set; } = 0f;
    public float FollowSearchRange { get; set; } = 0f;

    // 接口方法留空
    public void Init(MAEntity entity) { }
    
    public void UpdateTargeting() { }
    
    public void ShutDown() { }
    
    public void Resume() { }
}