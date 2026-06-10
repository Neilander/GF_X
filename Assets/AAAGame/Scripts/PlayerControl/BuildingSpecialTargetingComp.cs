using UnityEngine;

public sealed class MeatRackTargetingComp : ITargetingComp
{
    private const float ScanInterval = 0.2f;
    private const float TauntScoreScale = 100000f;

    private IEntityContext _ctx;
    private float _scanTimer;

    public IEntityContext CurrentTarget { get; set; }
    public IEntityContext FollowTarget => null;
    public float AggroRange { get; set; } = 6f;
    public float ForgetRange { get; set; } = 8f;
    public float FollowSearchRange { get; set; }
    public float AlertRadius { get; set; } = 5f;

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        CurrentTarget = null;
        _scanTimer = 0f;
    }

    public void UpdateTargeting(float deltaTime)
    {
        if (_ctx == null)
            return;

        float attackRange = GetEffectiveAttackRange();
        if (CurrentTarget != null)
        {
            float distance = _ctx.DistanceToTargetSurface(CurrentTarget);
            if (distance > attackRange || !CurrentTarget.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(_ctx, CurrentTarget))
                CurrentTarget = null;
        }

        _scanTimer += deltaTime;
        if (_scanTimer < ScanInterval)
            return;

        _scanTimer = 0f;
        CurrentTarget = FindBestTarget(attackRange);
    }

    private IEntityContext FindBestTarget(float scanRange)
    {
        var all = EntityRegistry.AllEntities;
        if (all == null)
            throw new System.InvalidOperationException("MeatRackTargetingComp.FindBestTarget failed: EntityRegistry.AllEntities is null.");

        IEntityContext best = null;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext candidate = all[i];
            if (candidate == null || ReferenceEquals(candidate, _ctx))
                continue;
            if (!candidate.IsAttackTargetable())
                continue;
            if (!EntityCombatTeamHelper.IsEnemy(_ctx, candidate))
                continue;

            float distance = _ctx.DistanceToTargetSurface(candidate);
            if (distance > scanRange)
                continue;

            float score = GetTauntLevel(candidate) * TauntScoreScale + distance;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private float GetEffectiveAttackRange()
    {
        Fix64 weaponRange = _ctx?.WeaponComp != null ? _ctx.WeaponComp.AttackRange : (Fix64)1.5f;
        return (float)weaponRange;
    }

    private static int GetTauntLevel(IEntityContext entity)
    {
        return entity is GeneralCreature creature ? creature.TauntLevel : 0;
    }

    public void NotifyDamageTaken(IEntityContext attacker) { }
    public void NotifyAllyFoundEnemy(IEntityContext enemy) { }
    public void ClearAggro() { }
    public void ShutDown() { CurrentTarget = null; }
    public void Resume() { }
}

public sealed class MonitorTargetingComp : ITargetingComp
{
    private const float ScanInterval = 0.1f;

    private readonly float _facingConeAngle;
    private IEntityContext _ctx;
    private float _scanTimer;

    public MonitorTargetingComp(float facingConeAngle)
    {
        _facingConeAngle = facingConeAngle;
    }

    public IEntityContext CurrentTarget { get; set; }
    public IEntityContext FollowTarget => null;
    public float AggroRange { get; set; } = 6f;
    public float ForgetRange { get; set; } = 8f;
    public float FollowSearchRange { get; set; }
    public float AlertRadius { get; set; } = 5f;

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        CurrentTarget = null;
        _scanTimer = 0f;
    }

    public void UpdateTargeting(float deltaTime)
    {
        if (_ctx == null)
            return;

        float attackRange = GetEffectiveAttackRange();
        if (CurrentTarget != null)
        {
            float distance = _ctx.DistanceToTargetSurface(CurrentTarget);
            if (distance > attackRange
                || !CurrentTarget.IsAttackTargetable()
                || !EntityCombatTeamHelper.IsEnemy(_ctx, CurrentTarget)
                || !MonitorFacingUtility.IsFacingMonitor(CurrentTarget, _ctx, _facingConeAngle))
            {
                CurrentTarget = null;
            }
        }

        _scanTimer += deltaTime;
        if (_scanTimer < ScanInterval)
            return;

        _scanTimer = 0f;
        CurrentTarget = FindFacingTarget(attackRange);
    }

    private IEntityContext FindFacingTarget(float scanRange)
    {
        var all = EntityRegistry.AllEntities;
        if (all == null)
            throw new System.InvalidOperationException("MonitorTargetingComp.FindFacingTarget failed: EntityRegistry.AllEntities is null.");

        IEntityContext best = null;
        float bestDistance = scanRange;
        int bestTaunt = -1;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext candidate = all[i];
            if (candidate == null || ReferenceEquals(candidate, _ctx))
                continue;
            if (!candidate.IsAttackTargetable())
                continue;
            if (!EntityCombatTeamHelper.IsEnemy(_ctx, candidate))
                continue;
            if (!MonitorFacingUtility.IsFacingMonitor(candidate, _ctx, _facingConeAngle))
                continue;

            float distance = _ctx.DistanceToTargetSurface(candidate);
            if (distance > scanRange)
                continue;

            int taunt = candidate is GeneralCreature creature ? creature.TauntLevel : 0;
            if (taunt > bestTaunt || (taunt == bestTaunt && distance < bestDistance))
            {
                bestTaunt = taunt;
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private float GetEffectiveAttackRange()
    {
        Fix64 weaponRange = _ctx?.WeaponComp != null ? _ctx.WeaponComp.AttackRange : (Fix64)1.5f;
        return (float)weaponRange;
    }

    public void NotifyDamageTaken(IEntityContext attacker) { }
    public void NotifyAllyFoundEnemy(IEntityContext enemy) { }
    public void ClearAggro() { }
    public void ShutDown() { CurrentTarget = null; }
    public void Resume() { }
}

public static class MonitorFacingUtility
{
    public static bool IsFacingMonitor(IEntityContext candidate, IEntityContext monitor, float coneAngle)
    {
        if (candidate == null || monitor == null)
            return false;

        Vector3 toMonitor = monitor.Position - candidate.Position;
        toMonitor.y = 0f;
        if (toMonitor.sqrMagnitude <= 0.0001f)
            return true;

        Vector3 forward = ResolveForward(candidate);
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            return false;

        float angle = Vector3.Angle(forward.normalized, toMonitor.normalized);
        return angle <= Mathf.Max(0f, coneAngle) * 0.5f;
    }

    private static Vector3 ResolveForward(IEntityContext entity)
    {
        if (entity is GeneralCreature creature)
        {
            if (creature.animator != null)
                return creature.animator.transform.forward;
            if (creature.display != null)
                return creature.display.forward;
        }

        return entity.Rotation * Vector3.forward;
    }
}
