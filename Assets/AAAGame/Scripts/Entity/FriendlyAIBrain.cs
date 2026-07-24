using AAAGame.Scripts.Entity;
using UnityEngine;

public class FriendlyAIBrain : IControlBrain, ITickBrain, ILogicDeterministicStateContributor
{
    public Vector2 Move => Vector2.zero;
    public FixVector2 MoveFixed => FixVector2.Zero;
    public bool Attack { get; private set; }
    public bool Skill1 { get; private set; }
    public bool Skill2 { get; private set; }
    public bool Skill3 { get; private set; }
    public bool Skill4 { get; private set; }
    public bool Skill5 { get; private set; }

    public float AttackRange = 1.6f;
    public float FollowDistance = 2.2f;

    // === 跟随延迟参数 ===
    public float FollowUpdateInterval = 0.3f;
    private Fix64 _followTimer = Fix64.Zero;

    public void Tick(IEntityContext self, Fix64 dt)
    {
        Attack = false;
        Skill1 = Skill2 = Skill3 = Skill4 = Skill5 = false;

        var target = self.TargetComp?.CurrentTarget;
        var followTarget = self.TargetComp?.FollowTarget;

        // 1. 优先处理战斗
        if (target != null)
        {
            Fix64 distToEnemy = LogicEntityFrameSnapshotService.GetRequiredTargetSurfaceDistance(self, target);

            if (distToEnemy > (Fix64)AttackRange)
            {
                self.MoveComp.MoveToFixed(target.LogicFramePositionFixed());
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

            if (_followTimer >= (Fix64)FollowUpdateInterval)
            {
                _followTimer = Fix64.Zero;

                self.MoveComp.MoveToFixed(followTarget.LogicFramePositionFixed());
            }
            return;
        }

        // 3. 没敌人也没玩家，原地挂机并重置计时器
        _followTimer = (Fix64)FollowUpdateInterval;
        self.MoveComp.StopMove();
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new System.ArgumentNullException(nameof(hasher));
        hasher.Add(_followTimer.RawValue);
        hasher.Add(Attack);
    }
}
