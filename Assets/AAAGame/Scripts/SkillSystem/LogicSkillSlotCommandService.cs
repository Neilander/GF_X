using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public readonly struct LogicSkillSlotCommand
{
    public LogicSkillSlotCommand(ulong effectiveFrame, ulong sequence, int fromIndex, int toIndex)
    {
        EffectiveFrame = effectiveFrame;
        Sequence = sequence;
        FromIndex = fromIndex;
        ToIndex = toIndex;
    }

    public ulong EffectiveFrame { get; }
    public ulong Sequence { get; }
    public int FromIndex { get; }
    public int ToIndex { get; }
}

public static class LogicSkillSlotCommandService
{
    private static readonly Action<LogicSkillSlotCommand> s_RuntimeSink = SkillRuntimeDataModel.ApplyScheduledSlotSwap;
    private static readonly Comparison<LogicSkillSlotCommand> s_CommandComparison = CompareCommands;
    private static readonly List<LogicSkillSlotCommand> s_History = new List<LogicSkillSlotCommand>();
    private static readonly ReadOnlyCollection<LogicSkillSlotCommand> s_ReadOnlyHistory = s_History.AsReadOnly();
    private static readonly List<LogicSkillSlotCommand> s_Pending = new List<LogicSkillSlotCommand>();
    private static readonly List<LogicSkillSlotCommand> s_Due = new List<LogicSkillSlotCommand>();
    private static ulong s_LastSequence;
    private static ulong s_AppliedHistoryHash;
    private static int s_AppliedCount;

    public static bool IsActive { get; private set; }
    public static bool IsApplyingFrame { get; private set; }
    public static ulong LastAppliedFrame { get; private set; }
    public static int PendingCount => s_Pending.Count;
    public static int AppliedCount => s_AppliedCount;
    public static IReadOnlyList<LogicSkillSlotCommand> History => s_ReadOnlyHistory;
    public static event Action<LogicSkillSlotCommand> CommandRecorded;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicSkillSlotCommandService.BeginTimeline failed: service is already active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicSkillSlotCommandService.BeginTimeline failed: logic time control is not active.");

        IsActive = true;
        ClearState();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicSkillSlotCommandService.EndTimeline failed: a command frame is being applied.");

        IsActive = false;
        ClearState();
    }

    public static LogicSkillSlotCommand ScheduleForNextFrame(int fromIndex, int toIndex)
    {
        EnsureActive();
        ValidateIndices(fromIndex, toIndex);

        var command = new LogicSkillSlotCommand(
            checked(LogicTimeControlService.CurrentFrame + 1),
            checked(s_LastSequence + 1),
            fromIndex,
            toIndex);
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
    public static void ApplyFrameForTests(ulong frameId, Action<LogicSkillSlotCommand> sink)
    {
        ApplyFrame(frameId, sink);
    }
#endif

    private static void ApplyFrame(ulong frameId, Action<LogicSkillSlotCommand> sink)
    {
        EnsureActive();
        if (sink == null)
            throw new ArgumentNullException(nameof(sink));
        if (frameId != LogicTimeControlService.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicSkillSlotCommandService.ApplyFrame failed: time-control frame mismatch. time={LogicTimeControlService.CurrentFrame}, requested={frameId}.");
        }
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicSkillSlotCommandService.ApplyFrame failed: nested command frame detected.");

        IsApplyingFrame = true;
        try
        {
            s_Due.Clear();
            for (int i = 0; i < s_Pending.Count; i++)
            {
                LogicSkillSlotCommand command = s_Pending[i];
                if (command.EffectiveFrame < frameId)
                {
                    throw new InvalidOperationException(
                        $"LogicSkillSlotCommandService.ApplyFrame failed: command missed its frame. sequence={command.Sequence}, effective={command.EffectiveFrame}, current={frameId}.");
                }
                if (command.EffectiveFrame == frameId)
                    s_Due.Add(command);
            }

            s_Due.Sort(s_CommandComparison);
            for (int i = 0; i < s_Due.Count; i++)
            {
                LogicSkillSlotCommand command = s_Due[i];
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

    private static int CompareCommands(LogicSkillSlotCommand left, LogicSkillSlotCommand right)
    {
        return left.Sequence.CompareTo(right.Sequence);
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        if (!LogicTimeControlService.IsPaused)
            throw new InvalidOperationException("LogicSkillSlotCommandService.ResetForWorldTransition failed: logic time is not paused.");
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicSkillSlotCommandService.ResetForWorldTransition failed: a command frame is being applied.");

        ClearState();
    }

    public static void ResetFrameTimeline()
    {
        EnsureActive();
        if (LogicTimeControlService.CurrentFrame != 0)
            throw new InvalidOperationException("LogicSkillSlotCommandService.ResetFrameTimeline failed: time-control frame is not zero.");
        if (s_Pending.Count != 0)
            throw new InvalidOperationException($"LogicSkillSlotCommandService.ResetFrameTimeline failed: {s_Pending.Count} pending commands remain.");

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

    private static void RecordApplied(LogicSkillSlotCommand command)
    {
        var hasher = new LogicStateHasher();
        hasher.Add(0x534B494C4C534C54UL);
        hasher.Add(s_AppliedHistoryHash);
        AddCommand(hasher, command);
        s_AppliedHistoryHash = hasher.Hash;
        s_AppliedCount = checked(s_AppliedCount + 1);
    }

    private static void AddCommand(LogicStateHasher hasher, LogicSkillSlotCommand command)
    {
        hasher.Add(command.EffectiveFrame);
        hasher.Add(command.Sequence);
        hasher.Add(command.FromIndex);
        hasher.Add(command.ToIndex);
    }

    private static void ValidateIndices(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= SkillInputRuntime.MaxSkillCount)
            throw new ArgumentOutOfRangeException(nameof(fromIndex), fromIndex, "Invalid skill slot index.");
        if (toIndex < 0 || toIndex >= SkillInputRuntime.MaxSkillCount)
            throw new ArgumentOutOfRangeException(nameof(toIndex), toIndex, "Invalid skill slot index.");
        if (fromIndex == toIndex)
            throw new ArgumentException("Skill slot command requires two different indices.", nameof(toIndex));
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicSkillSlotCommandService operation failed: service is not active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicSkillSlotCommandService operation failed: logic time control is not active.");
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
