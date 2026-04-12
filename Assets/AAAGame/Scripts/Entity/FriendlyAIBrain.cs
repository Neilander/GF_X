using AAAGame.Scripts.Entity;
using UnityEngine;

public class FriendlyAIBrain : IControlBrain, ITickBrain
{
    public Vector2 Move => Vector2.zero;
    public bool Attack { get; private set; }
    public bool Skill1 { get; private set; }
    public bool Skill2 { get; private set; }
    public bool Skill3 { get; private set; }

    public float AttackRange = 1.6f;
    public float FollowDistance = 2.2f;

    // === 跟随延迟参数 ===
    public float FollowUpdateInterval = 0.3f;
    private float _followTimer = 0f;

    public void Tick(IEntityContext self, float dt)
    {
        Attack = false;
        Skill1 = Skill2 = Skill3 = false;

        var target = self.TargetComp?.CurrentTarget;
        var followTarget = self.TargetComp?.FollowTarget;

        // 1. 优先处理战斗
        if (target != null)
        {
            float distToEnemy = self.DistanceToTargetSurface(target);

            if (distToEnemy > AttackRange)
            {
                // ClusterCalculator 需要 Transform，仅真实实体使用
                Vector3 atkPos = target.Position;
                if (target is CompCreature targetCC && self is MAEntity selfMA)
                    atkPos = ClusterCalculator.GetClusteredPosition(targetCC.transform, selfMA, AttackRange * 0.8f);

                self.MoveComp.MoveTo(atkPos);
            }
            else
            {
                self.MoveComp.StopMove();
                Attack = true;
            }
            return;
        }

        // 2. 延迟跟随决策
        if (followTarget != null)
        {
            _followTimer += dt;

            if (_followTimer >= FollowUpdateInterval)
            {
                _followTimer = 0f;

                Vector3 followPos = followTarget.Position;
                if (followTarget is CompCreature followCC && self is MAEntity selfMA)
                    followPos = ClusterCalculator.GetClusteredPosition(followCC.transform, selfMA, FollowDistance);

                self.MoveComp.MoveTo(followPos);
            }
            return;
        }

        // 3. 没敌人也没玩家，原地挂机并重置计时器
        _followTimer = FollowUpdateInterval;
        self.MoveComp.StopMove();
    }
}
