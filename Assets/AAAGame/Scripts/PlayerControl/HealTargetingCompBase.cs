using System;
using System.Collections.Generic;

public abstract class HealTargetingCompBase : TargetingCompBase, ITargetingComp, IMultiTargetingComp, ILogicDeterministicStateContributor
{
    private readonly struct HealCandidate
    {
        public HealCandidate(IEntityContext target, Fix64 healthRatio, Fix64 distance)
        {
            Target = target;
            HealthRatio = healthRatio;
            Distance = distance;
        }

        public IEntityContext Target { get; }
        public Fix64 HealthRatio { get; }
        public Fix64 Distance { get; }
    }

    private static readonly Fix64 ScanInterval = Fix64.FromRaw(820);
    private readonly List<IEntityContext> m_CurrentTargets = new List<IEntityContext>();
    private readonly List<HealCandidate> m_InRange = new List<HealCandidate>();
    private readonly List<HealCandidate> m_OutOfRange = new List<HealCandidate>();
    private IEntityContext m_Context;
    private Fix64 m_ScanTimer;

    protected IEntityContext Context => m_Context;
    public IEntityContext CurrentTarget { get; set; }
    public IEntityContext AggroTarget => null;
    public IReadOnlyList<IEntityContext> CurrentTargets => m_CurrentTargets;

    protected abstract bool SupportsOwner(IEntityContext owner);
    protected abstract string OwnerKind { get; }
    protected abstract Fix64 GetScanRange(Fix64 attackRange);
    protected abstract Fix64 GetRetentionRange(Fix64 attackRange);

    public void Init(IEntityContext ctx)
    {
        if (ctx == null)
            throw new ArgumentNullException(nameof(ctx));
        if (!SupportsOwner(ctx))
        {
            throw new InvalidOperationException(
                $"{GetType().Name} requires a {OwnerKind} owner. entity={ctx.LogicEntityId.Value}.");
        }
        if (!WeaponTargetRules.IsHealingWeapon(ctx.WeaponComp?.Data?.Type ?? WeaponType.None))
            throw new InvalidOperationException($"{GetType().Name} requires a healing weapon. entity={ctx.LogicEntityId.Value}.");

        m_Context = ctx;
        CurrentTarget = null;
        m_CurrentTargets.Clear();
        m_ScanTimer = Fix64.Zero;
        OnInitialized();
    }

    public void UpdateTargeting(Fix64 deltaTime)
    {
        if (m_Context == null)
            throw new InvalidOperationException($"{GetType().Name}.UpdateTargeting called before Init.");
        if (deltaTime <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));

        Fix64 attackRange = GetRequiredAttackRange(m_Context);
        Fix64 retentionRange = GetRetentionRange(attackRange);
        if (CurrentTarget != null
            && (!WeaponTargetRules.IsValidHealTarget(m_Context, CurrentTarget, true)
                || m_Context.LogicFrameDistanceToTargetSurfaceFixed(CurrentTarget) > retentionRange))
        {
            CurrentTarget = null;
        }
        for (int i = m_CurrentTargets.Count - 1; i >= 0; i--)
        {
            IEntityContext target = m_CurrentTargets[i];
            if (!WeaponTargetRules.IsValidHealTarget(m_Context, target, true)
                || m_Context.LogicFrameDistanceToTargetSurfaceFixed(target) > retentionRange)
            {
                m_CurrentTargets.RemoveAt(i);
            }
        }

        m_ScanTimer += deltaTime;
        if (m_ScanTimer >= ScanInterval)
        {
            m_ScanTimer = Fix64.Zero;
            RebuildTargets(attackRange);
        }
        AfterTargetingUpdate();
    }

    protected virtual void OnInitialized() { }
    protected virtual void AfterTargetingUpdate() { }
    protected virtual void ClearRoleState() { }
    protected virtual void WriteRoleState(LogicStateHasher hasher) { }

    private void RebuildTargets(Fix64 attackRange)
    {
        Fix64 scanRange = GetScanRange(attackRange);
        int count = Math.Max(1, (int)(m_Context.WeaponComp?.Data?.ProjectileCount ?? Fix64.One));
        m_InRange.Clear();
        m_OutOfRange.Clear();
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext candidate = entities[i]
                ?? throw new InvalidOperationException($"{GetType().Name} found a null registry entity at index {i}.");
            if (!WeaponTargetRules.IsValidHealTarget(m_Context, candidate, true))
                continue;
            Fix64 distance = m_Context.LogicFrameDistanceToTargetSurfaceFixed(candidate);
            if (distance > scanRange)
                continue;
            Insert(
                distance <= attackRange ? m_InRange : m_OutOfRange,
                new HealCandidate(candidate, candidate.HealthRatioFixed(), distance),
                count);
        }

        List<HealCandidate> selected = m_InRange.Count > 0 ? m_InRange : m_OutOfRange;
        m_CurrentTargets.Clear();
        for (int i = 0; i < selected.Count; i++)
            m_CurrentTargets.Add(selected[i].Target);
        CurrentTarget = m_CurrentTargets.Count > 0 ? m_CurrentTargets[0] : null;
    }

    private static void Insert(List<HealCandidate> list, HealCandidate candidate, int count)
    {
        int index = 0;
        while (index < list.Count && !IsBetter(candidate, list[index]))
            index++;
        if (index >= count)
            return;
        list.Insert(index, candidate);
        if (list.Count > count)
            list.RemoveAt(list.Count - 1);
    }

    private static bool IsBetter(HealCandidate candidate, HealCandidate current)
    {
        return candidate.HealthRatio < current.HealthRatio
               || candidate.HealthRatio == current.HealthRatio
               && (candidate.Distance < current.Distance
                   || candidate.Distance == current.Distance
                   && candidate.Target.LogicEntityId < current.Target.LogicEntityId);
    }

    public void ClearAggro()
    {
        CurrentTarget = null;
        m_CurrentTargets.Clear();
        ClearRoleState();
    }

    public void ShutDown() => ClearAggro();
    public void Resume() { }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));
        hasher.Add(m_ScanTimer.RawValue);
        hasher.Add(m_CurrentTargets.Count);
        for (int i = 0; i < m_CurrentTargets.Count; i++)
            hasher.Add(m_CurrentTargets[i].LogicEntityId.Value);
        WriteRoleState(hasher);
    }
}
