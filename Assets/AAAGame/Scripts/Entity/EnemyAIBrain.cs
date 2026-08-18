using UnityEngine;

public class EnemyAIBrain : IControlBrain, ITickBrain
{
    public Vector2 Move => MoveFixed;
    public FixVector2 MoveFixed { get; private set; }
    public bool Attack { get; private set; }
    public bool Skill1 { get; private set; }
    public bool Skill2 { get; private set; }
    public bool Skill3 { get; private set; }
    public bool Skill4 { get; private set; }
    public bool Skill5 { get; private set; }

    public Fix64 AttackRange = Fix64.FromRaw(6554);

    // === 分离力参数 ===
    public Fix64 SeparationRadius = Fix64.FromRaw(6144);
    public Fix64 SeparationWeight = Fix64.FromRaw(4916);

    private IEntityContext _target;

    public void Tick(IEntityContext self, Fix64 dt)
    {
        Attack = false;
        Skill1 = Skill2 = Skill3 = Skill4 = Skill5 = false;

        _target = self.TargetComp?.CurrentTarget;

        FixVector2 desiredMove = FixVector2.Zero;

        if (_target != null)
        {
            FixVector2 to = LogicEntityFrameSnapshotService.GetRequiredPosition(_target)
                            - LogicEntityFrameSnapshotService.GetRequiredPosition(self);
            Fix64 d2 = FixVector2.SqrMagnitude(to);
            if (d2 > AttackRange * AttackRange)
                desiredMove = to.GetNormalized();
            else
                Attack = true;
        }
        else if (self.TargetComp is ILastSeenTargetingComp lastSeen && lastSeen.HasLastSeenPursuit)
        {
            FixVector2 to = lastSeen.LastSeenPursuitDestinationFixed
                            - LogicEntityFrameSnapshotService.GetRequiredPosition(self);
            if (FixVector2.SqrMagnitude(to) > Fix64.FromRaw(41))
                desiredMove = to.GetNormalized();
        }

        FixVector2 separation = ResolveSeparation(self, SeparationRadius);
        FixVector2 finalMove = desiredMove + separation * SeparationWeight;

        MoveFixed = FixVector2.SqrMagnitude(finalMove) > Fix64.FromRaw(41) && !Attack
            ? finalMove.GetNormalized()
            : FixVector2.Zero;
    }

    private static FixVector2 ResolveSeparation(IEntityContext self, Fix64 radius)
    {
        if (radius <= Fix64.Zero)
            return FixVector2.Zero;

        FixVector2 selfPosition = LogicEntityFrameSnapshotService.GetRequiredPosition(self);
        FixVector2 force = FixVector2.Zero;
        int count = 0;
        for (int i = 0; i < EntityRegistry.AllEntities.Count; i++)
        {
            IEntityContext other = EntityRegistry.AllEntities[i];
            if (other == self || !other.Alive || other.Side != self.Side)
                continue;

            FixVector2 difference = selfPosition - LogicEntityFrameSnapshotService.GetRequiredPosition(other);
            Fix64 distanceSquared = FixVector2.SqrMagnitude(difference);
            if (distanceSquared > radius * radius)
                continue;

            if (distanceSquared == Fix64.Zero)
            {
                force += ResolveStableOverlapDirection(self.LogicEntityId.Value, other.LogicEntityId.Value);
            }
            else
            {
                Fix64 distance = Fix64.Sqrt(distanceSquared);
                force += difference / distance * (Fix64.One - distance / radius);
            }
            count++;
        }

        return count > 0 ? force / (Fix64)count : FixVector2.Zero;
    }

    private static FixVector2 ResolveStableOverlapDirection(int selfId, int otherId)
    {
        int key = unchecked(selfId * 397) ^ otherId;
        switch (key & 3)
        {
            case 0: return new FixVector2(Fix64.One, Fix64.Zero);
            case 1: return new FixVector2(Fix64.Zero, Fix64.One);
            case 2: return new FixVector2(-Fix64.One, Fix64.Zero);
            default: return new FixVector2(Fix64.Zero, -Fix64.One);
        }
    }
}
