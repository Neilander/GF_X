using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public readonly struct LogicInGameValueCommand
{
    public LogicInGameValueCommand(ulong effectiveFrame, ulong sequence, IngameValueType valueType, int delta)
    {
        EffectiveFrame = effectiveFrame;
        Sequence = sequence;
        ValueType = valueType;
        Delta = delta;
    }

    public ulong EffectiveFrame { get; }
    public ulong Sequence { get; }
    public IngameValueType ValueType { get; }
    public int Delta { get; }
}

public static class LogicInGameValueCommandService
{
    private static readonly Action<LogicInGameValueCommand> s_RuntimeSink = ApplyRuntimeCommand;
    private static readonly Comparison<LogicInGameValueCommand> s_CommandComparison = CompareCommands;
    private static readonly List<LogicInGameValueCommand> s_History = new List<LogicInGameValueCommand>();
    private static readonly ReadOnlyCollection<LogicInGameValueCommand> s_ReadOnlyHistory = s_History.AsReadOnly();
    private static readonly List<LogicInGameValueCommand> s_Pending = new List<LogicInGameValueCommand>();
    private static readonly List<LogicInGameValueCommand> s_Due = new List<LogicInGameValueCommand>();
    private static ulong s_LastSequence;
    private static ulong s_AppliedHistoryHash;
    private static int s_AppliedCount;

    public static bool IsActive { get; private set; }
    public static bool IsApplyingFrame { get; private set; }
    public static ulong LastAppliedFrame { get; private set; }
    public static int PendingCount => s_Pending.Count;
    public static int AppliedCount => s_AppliedCount;
    public static IReadOnlyList<LogicInGameValueCommand> History => s_ReadOnlyHistory;
    public static event Action<LogicInGameValueCommand> CommandRecorded;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicInGameValueCommandService.BeginTimeline failed: service is already active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicInGameValueCommandService.BeginTimeline failed: logic time control is not active.");

        IsActive = true;
        ClearState();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicInGameValueCommandService.EndTimeline failed: a command frame is being applied.");

        IsActive = false;
        ClearState();
    }

    public static LogicInGameValueCommand ScheduleDeltaForNextFrame(IngameValueType valueType, int delta)
    {
        EnsureActive();
        ValidateCommand(valueType, delta);

        var command = new LogicInGameValueCommand(
            checked(LogicTimeControlService.CurrentFrame + 1),
            checked(s_LastSequence + 1),
            valueType,
            delta);
        s_LastSequence = command.Sequence;
        s_Pending.Add(command);
        s_History.Add(command);
        CommandRecorded?.Invoke(command);
        return command;
    }

    public static void ApplyFrame(ulong frameId)
    {
        ApplyFrame(frameId, s_RuntimeSink);
    }

#if UNITY_EDITOR
    public static void ApplyFrameForTests(ulong frameId, Action<LogicInGameValueCommand> sink)
    {
        ApplyFrame(frameId, sink);
    }
#endif

    private static void ApplyFrame(ulong frameId, Action<LogicInGameValueCommand> sink)
    {
        EnsureActive();
        if (sink == null)
            throw new ArgumentNullException(nameof(sink));
        if (frameId != LogicTimeControlService.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicInGameValueCommandService.ApplyFrame failed: time-control frame mismatch. time={LogicTimeControlService.CurrentFrame}, requested={frameId}.");
        }
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicInGameValueCommandService.ApplyFrame failed: nested command frame detected.");

        IsApplyingFrame = true;
        try
        {
            s_Due.Clear();
            for (int i = 0; i < s_Pending.Count; i++)
            {
                LogicInGameValueCommand command = s_Pending[i];
                if (command.EffectiveFrame < frameId)
                {
                    throw new InvalidOperationException(
                        $"LogicInGameValueCommandService.ApplyFrame failed: command missed its frame. sequence={command.Sequence}, effective={command.EffectiveFrame}, current={frameId}.");
                }
                if (command.EffectiveFrame == frameId)
                    s_Due.Add(command);
            }

            s_Due.Sort(s_CommandComparison);
            for (int i = 0; i < s_Due.Count; i++)
            {
                LogicInGameValueCommand command = s_Due[i];
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
            throw new InvalidOperationException("LogicInGameValueCommandService.ResetForWorldTransition failed: logic time is not paused.");
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicInGameValueCommandService.ResetForWorldTransition failed: a command frame is being applied.");

        ClearState();
    }

    public static void ResetFrameTimeline()
    {
        EnsureActive();
        if (LogicTimeControlService.CurrentFrame != 0)
            throw new InvalidOperationException("LogicInGameValueCommandService.ResetFrameTimeline failed: time-control frame is not zero.");
        if (s_Pending.Count != 0)
            throw new InvalidOperationException($"LogicInGameValueCommandService.ResetFrameTimeline failed: {s_Pending.Count} pending commands remain.");

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

    private static void ApplyRuntimeCommand(LogicInGameValueCommand command)
    {
        if (!InGameDataModel.TryModifyValue(command.ValueType, command.Delta, true))
        {
            throw new InvalidOperationException(
                $"LogicInGameValueCommandService failed to apply command. sequence={command.Sequence}, type={command.ValueType}, delta={command.Delta}.");
        }
    }

    private static void RecordApplied(LogicInGameValueCommand command)
    {
        var hasher = new LogicStateHasher();
        hasher.Add(0x494E47414D455641UL);
        hasher.Add(s_AppliedHistoryHash);
        AddCommand(hasher, command);
        s_AppliedHistoryHash = hasher.Hash;
        s_AppliedCount = checked(s_AppliedCount + 1);
    }

    private static void AddCommand(LogicStateHasher hasher, LogicInGameValueCommand command)
    {
        hasher.Add(command.EffectiveFrame);
        hasher.Add(command.Sequence);
        hasher.Add((int)command.ValueType);
        hasher.Add(command.Delta);
    }

    private static int CompareCommands(LogicInGameValueCommand left, LogicInGameValueCommand right)
    {
        return left.Sequence.CompareTo(right.Sequence);
    }

    private static void ValidateCommand(IngameValueType valueType, int delta)
    {
        if (!Enum.IsDefined(typeof(IngameValueType), valueType))
            throw new ArgumentOutOfRangeException(nameof(valueType), valueType, "Invalid in-game value type.");
        if (valueType == IngameValueType.Phase)
            throw new ArgumentException("Phase changes must use LogicPhaseCommandService.", nameof(valueType));
        if (delta == 0)
            throw new ArgumentOutOfRangeException(nameof(delta), delta, "In-game value command delta must be non-zero.");
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicInGameValueCommandService operation failed: service is not active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicInGameValueCommandService operation failed: logic time control is not active.");
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
