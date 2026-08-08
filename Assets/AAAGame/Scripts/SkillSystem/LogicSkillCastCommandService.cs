using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public readonly struct LogicSkillCastCommand
{
    public LogicSkillCastCommand(
        ulong effectiveFrame,
        ulong sequence,
        LogicEntityId casterId,
        int slotIndex,
        FixVector2 requestedWorldPosition)
    {
        EffectiveFrame = effectiveFrame;
        Sequence = sequence;
        CasterId = casterId;
        SlotIndex = slotIndex;
        RequestedWorldPosition = requestedWorldPosition;
    }

    public ulong EffectiveFrame { get; }
    public ulong Sequence { get; }
    public LogicEntityId CasterId { get; }
    public int SlotIndex { get; }
    public FixVector2 RequestedWorldPosition { get; }
}

public interface ILogicSkillCastCommandConsumer
{
    void AcceptSkillCastCommand(LogicSkillCastCommand command);
}

public interface ILogicPausedSkillCastCommandConsumer
{
    void ResolvePausedSkillCastCommand();
}

public static class LogicSkillCastCommandService
{
    private static readonly Comparison<LogicSkillCastCommand> s_CommandComparison = CompareCommands;
    private static readonly List<LogicSkillCastCommand> s_History = new();
    private static readonly ReadOnlyCollection<LogicSkillCastCommand> s_ReadOnlyHistory = s_History.AsReadOnly();
    private static readonly List<LogicSkillCastCommand> s_Pending = new();
    private static readonly List<LogicSkillCastCommand> s_Due = new();
    private static ulong s_LastSequence;
    private static ulong s_AppliedHistoryHash;
    private static int s_AppliedCount;

    public static bool IsActive { get; private set; }
    public static bool IsApplyingFrame { get; private set; }
    public static ulong LastAppliedFrame { get; private set; }
    public static int PendingCount => s_Pending.Count;
    public static int AppliedCount => s_AppliedCount;
    public static IReadOnlyList<LogicSkillCastCommand> History => s_ReadOnlyHistory;
    public static event Action<LogicSkillCastCommand> CommandRecorded;

    public static bool HasPendingForCaster(LogicEntityId casterId)
    {
        EnsureActive();
        if (!casterId.IsValid)
            throw new ArgumentException("Skill cast pending lookup requires a valid caster id.", nameof(casterId));
        for (int i = 0; i < s_Pending.Count; i++)
        {
            if (s_Pending[i].CasterId == casterId)
                return true;
        }
        return false;
    }

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicSkillCastCommandService.BeginTimeline failed: service is already active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicSkillCastCommandService.BeginTimeline failed: logic time control is not active.");

        IsActive = true;
        ClearState();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicSkillCastCommandService.EndTimeline failed: a command frame is being applied.");

        IsActive = false;
        ClearState();
    }

    public static LogicSkillCastCommand ScheduleForNextFrame(
        LogicEntityId casterId,
        int slotIndex,
        FixVector2 requestedWorldPosition)
    {
        EnsureActive();
        if (!casterId.IsValid)
            throw new ArgumentException("Skill cast command requires a valid caster id.", nameof(casterId));
        if (slotIndex < 0 || slotIndex >= SkillInputRuntime.MaxSkillCount)
            throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Invalid skill slot index.");
        if (HasPendingForCaster(casterId))
            throw new InvalidOperationException($"Skill cast command is already pending. caster={casterId.Value}.");

        return RecordCommand(
            checked(LogicTimeControlService.CurrentFrame + 1),
            casterId,
            slotIndex,
            requestedWorldPosition,
            true);
    }

    public static LogicSkillCastCommand Submit(
        LogicEntityId casterId,
        int slotIndex,
        FixVector2 requestedWorldPosition)
    {
        if (!LogicPausedOperationService.CanResolveImmediately)
            return ScheduleForNextFrame(casterId, slotIndex, requestedWorldPosition);

        return SubmitImmediate(casterId, slotIndex, requestedWorldPosition, ApplyRuntimeCommand);
    }

    private static LogicSkillCastCommand SubmitImmediate(
        LogicEntityId casterId,
        int slotIndex,
        FixVector2 requestedWorldPosition,
        Action<LogicSkillCastCommand> sink)
    {
        return LogicPausedOperationService.Execute(() =>
        {
            LogicSkillCastCommand command = RecordCommand(
                LogicTimeControlService.CurrentFrame,
                casterId,
                slotIndex,
                requestedWorldPosition,
                false);
            ApplyImmediate(command, sink);
            return command;
        });
    }

    private static LogicSkillCastCommand RecordCommand(
        ulong effectiveFrame,
        LogicEntityId casterId,
        int slotIndex,
        FixVector2 requestedWorldPosition,
        bool pending)
    {
        EnsureActive();
        if (!casterId.IsValid)
            throw new ArgumentException("Skill cast command requires a valid caster id.", nameof(casterId));
        if (slotIndex < 0 || slotIndex >= SkillInputRuntime.MaxSkillCount)
            throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Invalid skill slot index.");
        if (HasPendingForCaster(casterId))
            throw new InvalidOperationException($"Skill cast command is already pending. caster={casterId.Value}.");

        var command = new LogicSkillCastCommand(
            effectiveFrame,
            checked(s_LastSequence + 1),
            casterId,
            slotIndex,
            requestedWorldPosition);
        s_LastSequence = command.Sequence;
        if (pending)
            s_Pending.Add(command);
        s_History.Add(command);
        CommandRecorded?.Invoke(command);
        return command;
    }

    private static void ApplyImmediate(LogicSkillCastCommand command, Action<LogicSkillCastCommand> sink)
    {
        if (!LogicPausedOperationService.IsExecuting)
            throw new InvalidOperationException("Immediate skill application requires a paused-operation settlement.");
        if (sink == null)
            throw new ArgumentNullException(nameof(sink));
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicSkillCastCommandService.ApplyImmediate failed: nested command application detected.");

        IsApplyingFrame = true;
        try
        {
            sink(command);
            RecordApplied(command);
            LastAppliedFrame = command.EffectiveFrame;
        }
        finally
        {
            IsApplyingFrame = false;
        }
    }

#if UNITY_EDITOR
    public static LogicSkillCastCommand SubmitForTests(
        LogicEntityId casterId,
        int slotIndex,
        FixVector2 requestedWorldPosition,
        Action<LogicSkillCastCommand> sink)
    {
        if (!LogicPausedOperationService.CanResolveImmediately)
            return ScheduleForNextFrame(casterId, slotIndex, requestedWorldPosition);
        return SubmitImmediate(casterId, slotIndex, requestedWorldPosition, sink);
    }
#endif

    public static void ApplyFrame(ulong frameId)
    {
        ApplyFrame(frameId, ApplyRuntimeCommand);
    }

#if UNITY_EDITOR
    public static void ApplyFrameForTests(ulong frameId, Action<LogicSkillCastCommand> sink)
    {
        ApplyFrame(frameId, sink);
    }
#endif

    private static void ApplyFrame(ulong frameId, Action<LogicSkillCastCommand> sink)
    {
        EnsureActive();
        if (sink == null)
            throw new ArgumentNullException(nameof(sink));
        if (frameId != LogicTimeControlService.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicSkillCastCommandService.ApplyFrame failed: time-control frame mismatch. time={LogicTimeControlService.CurrentFrame}, requested={frameId}.");
        }
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicSkillCastCommandService.ApplyFrame failed: nested command frame detected.");

        IsApplyingFrame = true;
        try
        {
            s_Due.Clear();
            for (int i = 0; i < s_Pending.Count; i++)
            {
                LogicSkillCastCommand command = s_Pending[i];
                if (command.EffectiveFrame < frameId)
                {
                    throw new InvalidOperationException(
                        $"LogicSkillCastCommandService.ApplyFrame failed: command missed its frame. sequence={command.Sequence}, effective={command.EffectiveFrame}, current={frameId}.");
                }
                if (command.EffectiveFrame == frameId)
                    s_Due.Add(command);
            }

            s_Due.Sort(s_CommandComparison);
            for (int i = 0; i < s_Due.Count; i++)
            {
                LogicSkillCastCommand command = s_Due[i];
                sink(command);
                RecordApplied(command);
            }

            for (int i = s_Pending.Count - 1; i >= 0; i--)
            {
                if (s_Pending[i].EffectiveFrame == frameId)
                    s_Pending.RemoveAt(i);
            }
            LastAppliedFrame = frameId;
        }
        finally
        {
            IsApplyingFrame = false;
            s_Due.Clear();
        }
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        if (!LogicTimeControlService.IsPaused)
            throw new InvalidOperationException("LogicSkillCastCommandService.ResetForWorldTransition failed: logic time is not paused.");
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicSkillCastCommandService.ResetForWorldTransition failed: a command frame is being applied.");
        ClearState();
    }

    public static void ResetFrameTimeline()
    {
        EnsureActive();
        if (LogicTimeControlService.CurrentFrame != 0)
            throw new InvalidOperationException("LogicSkillCastCommandService.ResetFrameTimeline failed: time-control frame is not zero.");
        if (s_Pending.Count != 0)
            throw new InvalidOperationException($"LogicSkillCastCommandService.ResetFrameTimeline failed: {s_Pending.Count} pending commands remain.");
        LastAppliedFrame = 0;
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        EnsureActive();
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add(s_LastSequence);
        hasher.Add(LastAppliedFrame);
        hasher.Add(s_AppliedCount);
        hasher.Add(s_AppliedHistoryHash);
        hasher.Add(s_Pending.Count);
        for (int i = 0; i < s_Pending.Count; i++)
            AddCommand(hasher, s_Pending[i]);
    }

    private static void ApplyRuntimeCommand(LogicSkillCastCommand command)
    {
        if (!EntityRegistry.TryGet(command.CasterId, out IEntityContext caster))
            throw new InvalidOperationException($"Skill cast command caster is not registered. caster={command.CasterId.Value}.");
        if (caster is not ISkillCompHost host || host.skillComp is not ILogicSkillCastCommandConsumer consumer)
        {
            throw new InvalidOperationException(
                $"Skill cast command caster has no command consumer. caster={command.CasterId.Value}, type={caster.GetType().FullName}.");
        }
        consumer.AcceptSkillCastCommand(command);
        if (!LogicPausedOperationService.IsExecuting)
            return;
        if (consumer is not ILogicPausedSkillCastCommandConsumer pausedConsumer)
        {
            throw new InvalidOperationException(
                $"Skill cast command consumer cannot resolve a paused cast. caster={command.CasterId.Value}, type={consumer.GetType().FullName}.");
        }
        pausedConsumer.ResolvePausedSkillCastCommand();
    }

    private static int CompareCommands(LogicSkillCastCommand left, LogicSkillCastCommand right) =>
        left.Sequence.CompareTo(right.Sequence);

    private static void RecordApplied(LogicSkillCastCommand command)
    {
        var hasher = new LogicStateHasher();
        hasher.Add(0x534B494C4C434153UL);
        hasher.Add(s_AppliedHistoryHash);
        AddCommand(hasher, command);
        s_AppliedHistoryHash = hasher.Hash;
        s_AppliedCount = checked(s_AppliedCount + 1);
    }

    private static void AddCommand(LogicStateHasher hasher, LogicSkillCastCommand command)
    {
        hasher.Add(command.EffectiveFrame);
        hasher.Add(command.Sequence);
        hasher.Add(command.CasterId.Value);
        hasher.Add(command.SlotIndex);
        hasher.Add(command.RequestedWorldPosition.x.RawValue);
        hasher.Add(command.RequestedWorldPosition.y.RawValue);
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicSkillCastCommandService operation failed: service is not active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicSkillCastCommandService operation failed: logic time control is not active.");
    }

    private static void ClearState()
    {
        IsApplyingFrame = false;
        LastAppliedFrame = 0;
        s_LastSequence = 0;
        s_AppliedHistoryHash = 0;
        s_AppliedCount = 0;
        s_History.Clear();
        s_Pending.Clear();
        s_Due.Clear();
    }
}
