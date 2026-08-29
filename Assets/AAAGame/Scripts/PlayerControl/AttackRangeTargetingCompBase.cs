using System;
using System.Collections.Generic;

public abstract class AttackRangeTargetingCompBase : TargetingCompBase, ITargetingComp, ILogicDeterministicStateContributor
{
    private static readonly Fix64 ScanInterval = Fix64.FromRaw(820);
    private IEntityContext m_Context;
    private Fix64 m_ScanTimer;
    public IEntityContext CurrentTarget { get; set; }
    public IEntityContext AggroTarget => CurrentTarget;

    protected abstract bool SupportsOwner(IEntityContext owner);
    protected abstract string OwnerKind { get; }
    protected virtual bool PropagatesAggroCandidate => false;

    public void Init(IEntityContext ctx)
    {
        if (ctx == null)
            throw new ArgumentNullException(nameof(ctx));
        if (!SupportsOwner(ctx))
        {
            throw new InvalidOperationException(
                $"{GetType().Name} requires a {OwnerKind} owner. entity={ctx.LogicEntityId.Value}, type={ctx.GetType().FullName}.");
        }

        m_Context = ctx;
        CurrentTarget = null;
        m_ScanTimer = Fix64.Zero;
    }

    public void UpdateTargeting(Fix64 deltaTime)
    {
        if (m_Context == null)
            throw new InvalidOperationException($"{GetType().Name}.UpdateTargeting called before Init.");
        if (deltaTime <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));

        Fix64 attackRange = GetRequiredAttackRange(m_Context);
        bool hadAggroCandidate = CurrentTarget != null;
        bool requiresImmediateScan = false;
        if (CurrentTarget != null
            && (!CurrentTarget.IsRegisteredInLogicWorld()
                || !IsEligible(CurrentTarget, attackRange)))
        {
            CurrentTarget = null;
            requiresImmediateScan = true;
        }

        m_ScanTimer += deltaTime;
        if (m_ScanTimer < ScanInterval && !requiresImmediateScan)
            return;

        m_ScanTimer = Fix64.Zero;
        CurrentTarget = FindBestTarget(attackRange);
        if (PropagatesAggroCandidate && !hadAggroCandidate && CurrentTarget != null)
            LogicFactionVisionService.HandleAggroAcquired(m_Context, CurrentTarget);
    }

    private IEntityContext FindBestTarget(Fix64 attackRange)
    {
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        IEntityContext best = null;
        TargetPriority bestPriority = default;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext candidate = entities[i]
                ?? throw new InvalidOperationException($"{GetType().Name} found a null registry entity at index {i}.");
            if (ReferenceEquals(candidate, m_Context) || !IsEligible(candidate, attackRange))
                continue;

            Fix64 distance = m_Context.LogicFrameDistanceToTargetSurfaceFixed(candidate);
            TargetPriority priority = TargetPriorityUtility.Create(
                m_Context,
                candidate,
                distance,
                attackRange,
                CurrentTarget,
                false);
            if (best == null || priority.CompareTo(bestPriority) > 0)
            {
                best = candidate;
                bestPriority = priority;
            }
        }

        return best;
    }

    private bool IsEligible(IEntityContext candidate, Fix64 attackRange)
    {
        return IsVisibleEnemyTarget(m_Context, candidate)
               && m_Context.LogicFrameDistanceToTargetSurfaceFixed(candidate) <= attackRange;
    }

    public void ClearAggro() { CurrentTarget = null; }
    public void ShutDown() { CurrentTarget = null; }
    public void Resume() { }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));
        hasher.Add(m_ScanTimer.RawValue);
        hasher.Add(CurrentTarget != null && CurrentTarget.LogicEntityId.IsValid
            ? CurrentTarget.LogicEntityId.Value
            : 0);
    }
}
