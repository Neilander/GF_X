using UnityEngine;

public sealed class MeatRackTargetingComp : ITargetingComp, ILogicDeterministicStateContributor
{
    private static readonly Fix64 ScanInterval = Fix64.FromRaw(820);
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
            if (!CurrentTarget.IsRegisteredInLogicWorld()
                || !CurrentTarget.IsAttackTargetable()
                || !EntityCombatTeamHelper.IsEnemy(_ctx, CurrentTarget))
                CurrentTarget = null;
            else if (_ctx.LogicFrameDistanceToTargetSurfaceFixed(CurrentTarget) > attackRange)
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
        Fix64 threatPerLevel = TargetThreatUtility.ReadThreatPerLevel();
        Fix64 buildingExtraThreat = TargetThreatUtility.ReadBuildingExtraThreat();
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

            Fix64 score = TargetThreatUtility.CalculateSelectionThreat(
                _ctx,
                candidate,
                distance,
                threatPerLevel,
                buildingExtraThreat);
            if (TargetThreatUtility.HasHigherPriority(score, candidate, bestScore, best))
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private Fix64 GetEffectiveAttackRange()
    {
        Fix64 weaponRange = _ctx?.WeaponComp != null ? _ctx.WeaponComp.AttackRange : Fix64.FromRaw(6144);
        return weaponRange;
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

