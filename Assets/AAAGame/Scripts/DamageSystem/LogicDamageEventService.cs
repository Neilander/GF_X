using System;
using System.Collections.Generic;

public enum LogicHealthEventKind
{
    Damage = 0,
    Heal = 1,
}

public readonly struct LogicHealthEvent
{
    internal LogicHealthEvent(
        ulong frameId,
        ulong sequence,
        LogicHealthEventKind kind,
        IEntityContext attacker,
        IEntityContext target,
        ITargetable damageTarget,
        Fix64 amount,
        HealthModifyType modifyType,
        bool applyDamageHooks,
        int hitIndex,
        int totalHits)
    {
        FrameId = frameId;
        Sequence = sequence;
        Kind = kind;
        Attacker = attacker;
        Target = target;
        DamageTarget = damageTarget;
        Amount = amount;
        ModifyType = modifyType;
        ApplyDamageHooks = applyDamageHooks;
        HitIndex = hitIndex;
        TotalHits = totalHits;
    }

    public ulong FrameId { get; }
    public ulong Sequence { get; }
    public LogicHealthEventKind Kind { get; }
    public LogicEntityId AttackerId => Attacker?.LogicEntityId ?? default;
    public LogicEntityId TargetId => Target.LogicEntityId;
    public IEntityContext Attacker { get; }
    public IEntityContext Target { get; }
    internal ITargetable DamageTarget { get; }
    public Fix64 Amount { get; }
    public HealthModifyType ModifyType { get; }
    public bool ApplyDamageHooks { get; }
    public int HitIndex { get; }
    public int TotalHits { get; }
}

public static class LogicDamageEventService
{
    private static readonly List<LogicHealthEvent> s_Events = new List<LogicHealthEvent>();
    private static readonly List<LogicHealthEvent> s_LastOrderedEvents = new List<LogicHealthEvent>();
    private static ulong s_NextSequence;
    private static ulong s_CollectingFrame;

    public static bool IsActive { get; private set; }
    public static bool IsCollecting => s_CollectingFrame != 0;
    public static bool IsApplying { get; private set; }
    public static ulong LastCompletedFrame { get; private set; }
    public static int LastSubmittedCount { get; private set; }
    public static int LastAppliedCount { get; private set; }
    public static int LastSkippedDeadTargetCount { get; private set; }
    public static IReadOnlyList<LogicHealthEvent> PendingEvents => s_Events;
    public static IReadOnlyList<LogicHealthEvent> LastOrderedEvents => s_LastOrderedEvents;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicDamageEventService.BeginTimeline failed: service is already active.");
        ClearState();
        IsActive = true;
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        if (LogicFrameRuntime.IsTicking)
            throw new InvalidOperationException("LogicDamageEventService.ResetForWorldTransition failed: a logic frame is running.");
        ClearState();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        if (LogicFrameRuntime.IsTicking)
            throw new InvalidOperationException("LogicDamageEventService.EndTimeline failed: a logic frame is running.");
        ClearState();
        IsActive = false;
    }

    public static void BeginFrame(ulong frameId)
    {
        EnsureActive();
        if (!LogicFrameRuntime.IsTicking || frameId == 0 || frameId != LogicFrameRuntime.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicDamageEventService.BeginFrame failed: invalid frame. requested={frameId}, current={LogicFrameRuntime.CurrentFrame}, ticking={LogicFrameRuntime.IsTicking}.");
        }
        if (IsCollecting || IsApplying || s_Events.Count != 0)
            throw new InvalidOperationException("LogicDamageEventService.BeginFrame failed: previous frame state is still active.");

        s_CollectingFrame = frameId;
        s_LastOrderedEvents.Clear();
        LastSubmittedCount = 0;
        LastAppliedCount = 0;
        LastSkippedDeadTargetCount = 0;
    }

    public static void SubmitDamage(
        ITargetable target,
        Damage damage,
        IEntityContext attacker,
        int hitIndex,
        int totalHits)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (damage == null)
            throw new ArgumentNullException(nameof(damage));
        if (!(target is IEntityContext targetContext))
            throw new InvalidOperationException("LogicDamageEventService.SubmitDamage failed: target has no logic entity identity.");

        Submit(
            LogicHealthEventKind.Damage,
            attacker,
            targetContext,
            target,
            damage.amount,
            damage.modType,
            true,
            hitIndex,
            totalHits);
    }

    public static void SubmitDirectDamage(
        IEntityContext target,
        Fix64 amount,
        HealthModifyType modifyType,
        IEntityContext attacker = null)
    {
        Submit(
            LogicHealthEventKind.Damage,
            attacker,
            target,
            target as ITargetable,
            amount,
            modifyType,
            false,
            1,
            1);
    }

    public static void SubmitHeal(IEntityContext healer, IEntityContext target, Fix64 amount)
    {
        Submit(
            LogicHealthEventKind.Heal,
            healer,
            target,
            null,
            amount,
            HealthModifyType.empty,
            false,
            1,
            1);
    }

    public static void ApplyFrame(ulong frameId)
    {
        EnsureActive();
        if (!IsCollecting || s_CollectingFrame != frameId || frameId != LogicFrameRuntime.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicDamageEventService.ApplyFrame failed: frame mismatch. requested={frameId}, collecting={s_CollectingFrame}, current={LogicFrameRuntime.CurrentFrame}.");
        }

        s_CollectingFrame = 0;
        IsApplying = true;
        try
        {
            s_Events.Sort(CompareEvents);
            LastSubmittedCount = s_Events.Count;
            s_LastOrderedEvents.AddRange(s_Events);
            for (int i = 0; i < s_Events.Count; i++)
            {
                LogicHealthEvent healthEvent = s_Events[i];
                ValidateEventIdentity(healthEvent);
                if (!healthEvent.Target.Alive)
                {
                    LastSkippedDeadTargetCount++;
                    continue;
                }

                switch (healthEvent.Kind)
                {
                    case LogicHealthEventKind.Damage:
                        if (healthEvent.ApplyDamageHooks)
                        {
                            DamageHelper.ApplyQueuedDamage(
                                healthEvent.DamageTarget,
                                healthEvent.Amount,
                                healthEvent.ModifyType,
                                healthEvent.Attacker,
                                healthEvent.HitIndex,
                                healthEvent.TotalHits);
                        }
                        else
                        {
                            healthEvent.Target.TakeDamage(
                                healthEvent.Amount,
                                healthEvent.ModifyType,
                                healthEvent.Attacker);
                        }
                        break;
                    case LogicHealthEventKind.Heal:
                        healthEvent.Target.Heal(healthEvent.Amount);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(healthEvent.Kind), healthEvent.Kind, "Unknown health event kind.");
                }

                LastAppliedCount++;
            }

            LastCompletedFrame = frameId;
        }
        finally
        {
            s_Events.Clear();
            IsApplying = false;
        }
    }

    private static void Submit(
        LogicHealthEventKind kind,
        IEntityContext attacker,
        IEntityContext target,
        ITargetable damageTarget,
        Fix64 amount,
        HealthModifyType modifyType,
        bool applyDamageHooks,
        int hitIndex,
        int totalHits)
    {
        EnsureActive();
        if (!IsCollecting || !LogicFrameRuntime.IsTicking || s_CollectingFrame != LogicFrameRuntime.CurrentFrame)
            throw new InvalidOperationException("LogicDamageEventService.Submit failed: no attack collection window is active.");
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if ((attacker != null && !attacker.LogicEntityId.IsValid) || !target.LogicEntityId.IsValid)
            throw new InvalidOperationException("LogicDamageEventService.Submit failed: referenced entities require valid logic entity ids.");
        if (amount < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(amount), "Health event amount cannot be negative.");
        if (hitIndex <= 0 || totalHits <= 0 || hitIndex > totalHits)
            throw new ArgumentOutOfRangeException(nameof(hitIndex), "Damage hit index must be within the attack hit count.");

        s_Events.Add(new LogicHealthEvent(
            s_CollectingFrame,
            checked(++s_NextSequence),
            kind,
            attacker,
            target,
            damageTarget,
            amount,
            modifyType,
            applyDamageHooks,
            hitIndex,
            totalHits));
    }

    private static int CompareEvents(LogicHealthEvent left, LogicHealthEvent right)
    {
        int result = left.AttackerId.CompareTo(right.AttackerId);
        if (result != 0)
            return result;
        result = left.TargetId.CompareTo(right.TargetId);
        if (result != 0)
            return result;
        return left.Sequence.CompareTo(right.Sequence);
    }

    private static void ValidateEventIdentity(LogicHealthEvent healthEvent)
    {
        if (healthEvent.FrameId != LogicFrameRuntime.CurrentFrame)
            throw new InvalidOperationException("LogicDamageEventService.ApplyFrame failed: event frame changed before application.");
        if (healthEvent.Target == null)
            throw new InvalidOperationException("LogicDamageEventService.ApplyFrame failed: event target reference is missing.");
        if ((healthEvent.Attacker != null && !healthEvent.AttackerId.IsValid) || !healthEvent.TargetId.IsValid)
            throw new InvalidOperationException("LogicDamageEventService.ApplyFrame failed: event entity identity became invalid.");
    }

    private static void ClearState()
    {
        s_Events.Clear();
        s_LastOrderedEvents.Clear();
        s_NextSequence = 0;
        s_CollectingFrame = 0;
        IsApplying = false;
        LastCompletedFrame = 0;
        LastSubmittedCount = 0;
        LastAppliedCount = 0;
        LastSkippedDeadTargetCount = 0;
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicDamageEventService operation failed: service is not active.");
    }
}
