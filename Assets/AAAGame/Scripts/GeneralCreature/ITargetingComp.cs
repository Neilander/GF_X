using UnityEngine;

public interface ITargetingComp:ICapability
{
    void Init(IEntityContext ctx);
    void UpdateTargeting(float deltaTime);

    // 供外部（如 AI Brain 或 UI）读取的当前目标
    IEntityContext CurrentTarget { get; set; }
    IEntityContext FollowTarget { get; }

    // 索敌和遗忘半径
    float AggroRange { get; set; }
    float ForgetRange { get; set; }

    float FollowSearchRange { get; set; }
}