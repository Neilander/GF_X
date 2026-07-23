using UnityEngine;

public sealed class MeatRackTargetingComp : ITargetingComp, ILogicDeterministicStateContributor
{
    private static readonly Fix64 ScanInterval = (Fix64)0.2f;
    private static readonly Fix64 TauntScoreScale = (Fix64)100000;

    private IEntityContext _ctx;
    private Fix64 _scanTimer;

    public IEntityContext CurrentTarget { get; set; }
    public IEntityContext FollowTarget => null;
    private Fix64 m_AggroRange = (Fix64)6;
    private Fix64 m_ForgetRange = (Fix64)8;
    private Fix64 m_FollowSearchRange;
    private Fix64 m_AlertRadius = (Fix64)5;
    public Fix64 AggroRangeFixed { get => m_AggroRange; set => m_AggroRange = LogicTargetingRange.Require(value, nameof(AggroRangeFixed)); }
    public Fix64 ForgetRangeFixed { get => m_ForgetRange; set => m_ForgetRange = LogicTargetingRange.Require(value, nameof(ForgetRangeFixed)); }
    public Fix64 FollowSearchRangeFixed { get => m_FollowSearchRange; set => m_FollowSearchRange = LogicTargetingRange.Require(value, nameof(FollowSearchRangeFixed)); }
    public Fix64 AlertRadiusFixed { get => m_AlertRadius; set => m_AlertRadius = LogicTargetingRange.Require(value, nameof(AlertRadiusFixed)); }
    public float AggroRange { get => (float)m_AggroRange; set => AggroRangeFixed = LogicTargetingRange.FromFloat(value, nameof(AggroRange)); }
    public float ForgetRange { get => (float)m_ForgetRange; set => ForgetRangeFixed = LogicTargetingRange.FromFloat(value, nameof(ForgetRange)); }
    public float FollowSearchRange { get => (float)m_FollowSearchRange; set => FollowSearchRangeFixed = LogicTargetingRange.FromFloat(value, nameof(FollowSearchRange)); }
    public float AlertRadius { get => (float)m_AlertRadius; set => AlertRadiusFixed = LogicTargetingRange.FromFloat(value, nameof(AlertRadius)); }

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        CurrentTarget = null;
        _scanTimer = Fix64.Zero;
    }

    public void UpdateTargeting(Fix64 deltaTime)
    {
        if (_ctx == null)
            return;

        Fix64 attackRange = GetEffectiveAttackRange();
        if (CurrentTarget != null)
        {
            Fix64 distance = _ctx.LogicFrameDistanceToTargetSurfaceFixed(CurrentTarget);
            if (distance > attackRange || !CurrentTarget.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(_ctx, CurrentTarget))
                CurrentTarget = null;
        }

        _scanTimer += deltaTime;
        if (_scanTimer < ScanInterval)
            return;

        _scanTimer = Fix64.Zero;
        CurrentTarget = FindBestTarget(attackRange);
    }

    private IEntityContext FindBestTarget(Fix64 scanRange)
    {
        var all = EntityRegistry.AllEntities;
        if (all == null)
            throw new System.InvalidOperationException("MeatRackTargetingComp.FindBestTarget failed: EntityRegistry.AllEntities is null.");

        IEntityContext best = null;
        Fix64 bestScore = -Fix64.FromRaw(long.MaxValue);
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext candidate = all[i];
            if (candidate == null || ReferenceEquals(candidate, _ctx))
                continue;
            if (!candidate.IsAttackTargetable())
                continue;
            if (!EntityCombatTeamHelper.IsEnemy(_ctx, candidate))
                continue;

            Fix64 distance = _ctx.LogicFrameDistanceToTargetSurfaceFixed(candidate);
            if (distance > scanRange)
                continue;

            Fix64 score = (Fix64)GetTauntLevel(candidate) * TauntScoreScale + distance;
            if (score > bestScore
                || (score == bestScore
                    && (best == null || candidate.LogicEntityId < best.LogicEntityId)))
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private Fix64 GetEffectiveAttackRange()
    {
        Fix64 weaponRange = _ctx?.WeaponComp != null ? _ctx.WeaponComp.AttackRange : (Fix64)1.5f;
        return weaponRange;
    }

    private static int GetTauntLevel(IEntityContext entity)
    {
        return entity?.TauntLevel ?? 0;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new System.ArgumentNullException(nameof(hasher));
        hasher.Add(_scanTimer.RawValue);
        hasher.Add(m_AggroRange.RawValue);
        hasher.Add(m_ForgetRange.RawValue);
    }

    public void NotifyDamageTaken(IEntityContext attacker) { }
    public void NotifyAllyFoundEnemy(IEntityContext enemy) { }
    public void ClearAggro() { }
    public void ShutDown() { CurrentTarget = null; }
    public void Resume() { }
}

public sealed class MonitorTargetingComp : ITargetingComp, ILogicDeterministicStateContributor
{
    private static readonly Fix64 ScanInterval = (Fix64)0.1f;

    private readonly Fix64 _facingConeAngle;
    private IEntityContext _ctx;
    private Fix64 _scanTimer;

    public MonitorTargetingComp(Fix64 facingConeAngle)
    {
        _facingConeAngle = facingConeAngle;
    }

    public IEntityContext CurrentTarget { get; set; }
    public IEntityContext FollowTarget => null;
    private Fix64 m_AggroRange = (Fix64)6;
    private Fix64 m_ForgetRange = (Fix64)8;
    private Fix64 m_FollowSearchRange;
    private Fix64 m_AlertRadius = (Fix64)5;
    public Fix64 AggroRangeFixed { get => m_AggroRange; set => m_AggroRange = LogicTargetingRange.Require(value, nameof(AggroRangeFixed)); }
    public Fix64 ForgetRangeFixed { get => m_ForgetRange; set => m_ForgetRange = LogicTargetingRange.Require(value, nameof(ForgetRangeFixed)); }
    public Fix64 FollowSearchRangeFixed { get => m_FollowSearchRange; set => m_FollowSearchRange = LogicTargetingRange.Require(value, nameof(FollowSearchRangeFixed)); }
    public Fix64 AlertRadiusFixed { get => m_AlertRadius; set => m_AlertRadius = LogicTargetingRange.Require(value, nameof(AlertRadiusFixed)); }
    public float AggroRange { get => (float)m_AggroRange; set => AggroRangeFixed = LogicTargetingRange.FromFloat(value, nameof(AggroRange)); }
    public float ForgetRange { get => (float)m_ForgetRange; set => ForgetRangeFixed = LogicTargetingRange.FromFloat(value, nameof(ForgetRange)); }
    public float FollowSearchRange { get => (float)m_FollowSearchRange; set => FollowSearchRangeFixed = LogicTargetingRange.FromFloat(value, nameof(FollowSearchRange)); }
    public float AlertRadius { get => (float)m_AlertRadius; set => AlertRadiusFixed = LogicTargetingRange.FromFloat(value, nameof(AlertRadius)); }

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        CurrentTarget = null;
        _scanTimer = Fix64.Zero;
    }

    public void UpdateTargeting(Fix64 deltaTime)
    {
        if (_ctx == null)
            return;

        Fix64 attackRange = GetEffectiveAttackRange();
        if (CurrentTarget != null)
        {
            Fix64 distance = _ctx.LogicFrameDistanceToTargetSurfaceFixed(CurrentTarget);
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

        _scanTimer = Fix64.Zero;
        CurrentTarget = FindFacingTarget(attackRange);
    }

    private IEntityContext FindFacingTarget(Fix64 scanRange)
    {
        var all = EntityRegistry.AllEntities;
        if (all == null)
            throw new System.InvalidOperationException("MonitorTargetingComp.FindFacingTarget failed: EntityRegistry.AllEntities is null.");

        IEntityContext best = null;
        Fix64 bestDistance = scanRange;
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

            Fix64 distance = _ctx.LogicFrameDistanceToTargetSurfaceFixed(candidate);
            if (distance > scanRange)
                continue;

            int taunt = candidate.TauntLevel;
            if (taunt > bestTaunt
                || (taunt == bestTaunt && (distance < bestDistance
                    || (distance == bestDistance
                        && (best == null || candidate.LogicEntityId < best.LogicEntityId)))))
            {
                bestTaunt = taunt;
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private Fix64 GetEffectiveAttackRange()
    {
        Fix64 weaponRange = _ctx?.WeaponComp != null ? _ctx.WeaponComp.AttackRange : (Fix64)1.5f;
        return weaponRange;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new System.ArgumentNullException(nameof(hasher));
        hasher.Add(_scanTimer.RawValue);
        hasher.Add(_facingConeAngle.RawValue);
        hasher.Add(m_AggroRange.RawValue);
        hasher.Add(m_ForgetRange.RawValue);
    }

    public void NotifyDamageTaken(IEntityContext attacker) { }
    public void NotifyAllyFoundEnemy(IEntityContext enemy) { }
    public void ClearAggro() { }
    public void ShutDown() { CurrentTarget = null; }
    public void Resume() { }
}

public static class MonitorFacingUtility
{
    public static bool IsFacingMonitor(IEntityContext candidate, IEntityContext monitor, Fix64 coneAngle)
    {
        if (candidate == null || monitor == null)
            return false;
        if (!LogicFrameRuntime.IsTicking)
            throw new System.InvalidOperationException("MonitorFacingUtility requires an active logic frame.");
        if (coneAngle < Fix64.Zero || coneAngle > (Fix64)360)
            throw new System.ArgumentOutOfRangeException(nameof(coneAngle));

        FixVector2 toMonitor = LogicEntityFrameSnapshotService.GetRequiredPosition(monitor)
                               - LogicEntityFrameSnapshotService.GetRequiredPosition(candidate);
        Fix64 distance = FixVector2.Magnitude(toMonitor);
        if (distance == Fix64.Zero)
            return true;
        if (coneAngle >= (Fix64)360)
            return true;

        FixVector2 forward = LogicEntityFrameSnapshotService.GetRequiredForward(candidate);
        return IsDirectionWithinCone(forward, toMonitor, coneAngle);
    }

    public static bool IsDirectionWithinCone(FixVector2 forward, FixVector2 toMonitor, Fix64 coneAngle)
    {
        if (coneAngle < Fix64.Zero || coneAngle > (Fix64)360)
            throw new System.ArgumentOutOfRangeException(nameof(coneAngle));
        FixVector2 normalizedForward = forward.GetNormalized();
        if (FixVector2.SqrMagnitude(normalizedForward) == Fix64.Zero)
            throw new System.ArgumentException("Monitor forward must be non-zero.", nameof(forward));
        Fix64 distance = FixVector2.Magnitude(toMonitor);
        if (distance == Fix64.Zero || coneAngle >= (Fix64)360)
            return true;
        Fix64 halfAngleRadians = coneAngle * (Fix64)0.5f * Fix64.PIOver180;
        Fix64 minimumDot = Fix64.Cos(halfAngleRadians) * distance;
        return FixVector2.Dot(normalizedForward, toMonitor) >= minimumDot;
    }
}
