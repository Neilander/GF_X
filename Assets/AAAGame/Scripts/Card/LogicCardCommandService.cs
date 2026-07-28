using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public enum LogicCardCommandKind
{
    Play = 0,
    Discard = 1,
}

public readonly struct LogicCardCommand
{
    public LogicCardCommand(
        ulong effectiveFrame,
        ulong sequence,
        LogicCardCommandKind kind,
        ulong cardRuntimeId,
        FixVector2 selectedPosition)
    {
        EffectiveFrame = effectiveFrame;
        Sequence = sequence;
        Kind = kind;
        CardRuntimeId = cardRuntimeId;
        SelectedPosition = selectedPosition;
    }

    public ulong EffectiveFrame { get; }
    public ulong Sequence { get; }
    public LogicCardCommandKind Kind { get; }
    public ulong CardRuntimeId { get; }
    public FixVector2 SelectedPosition { get; }
}

public readonly struct LogicCardResolution
{
    public LogicCardResolution(
        ulong frameId,
        LogicCardCommandKind kind,
        ulong cardRuntimeId,
        string sourceBuildingInstanceId)
    {
        FrameId = frameId;
        Kind = kind;
        CardRuntimeId = cardRuntimeId;
        SourceBuildingInstanceId = sourceBuildingInstanceId;
    }

    public ulong FrameId { get; }
    public LogicCardCommandKind Kind { get; }
    public ulong CardRuntimeId { get; }
    public string SourceBuildingInstanceId { get; }
}

public static class LogicCardCommandService
{
    private static readonly Action<LogicCardCommand> s_RuntimeSink = PublishCommandApplying;
    private static readonly Comparison<LogicCardCommand> s_CommandComparison = CompareCommands;
    private static readonly List<LogicCardCommand> s_History = new List<LogicCardCommand>();
    private static readonly ReadOnlyCollection<LogicCardCommand> s_ReadOnlyHistory = s_History.AsReadOnly();
    private static readonly List<LogicCardCommand> s_Pending = new List<LogicCardCommand>();
    private static readonly List<LogicCardCommand> s_Due = new List<LogicCardCommand>();
    private static ulong s_LastSequence;
    private static ulong s_AppliedHistoryHash;
    private static int s_AppliedCount;

    public static bool IsActive { get; private set; }
    public static bool IsApplyingFrame { get; private set; }
    public static ulong LastAppliedFrame { get; private set; }
    public static int PendingCount => s_Pending.Count;
    public static int AppliedCount => s_AppliedCount;
    public static IReadOnlyList<LogicCardCommand> History => s_ReadOnlyHistory;
    public static event Action<LogicCardCommand> CommandRecorded;
    public static event Action<LogicCardCommand> CommandApplying;
    public static event Action<LogicCardResolution> CardResolved;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicCardCommandService.BeginTimeline failed: service is already active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicCardCommandService.BeginTimeline failed: logic time control is not active.");
        IsActive = true;
        ClearState();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicCardCommandService.EndTimeline failed: a command frame is being applied.");
        if (CommandApplying != null)
            throw new InvalidOperationException("LogicCardCommandService.EndTimeline failed: runtime consumer is still registered.");
        IsActive = false;
        ClearState();
    }

    public static LogicCardCommand SchedulePlayForNextFrame(ulong cardRuntimeId, FixVector2 selectedPosition)
    {
        return ScheduleForNextFrame(LogicCardCommandKind.Play, cardRuntimeId, selectedPosition);
    }

    public static LogicCardCommand ScheduleDiscardForNextFrame(ulong cardRuntimeId)
    {
        return ScheduleForNextFrame(LogicCardCommandKind.Discard, cardRuntimeId, FixVector2.Zero);
    }

    private static LogicCardCommand ScheduleForNextFrame(
        LogicCardCommandKind kind,
        ulong cardRuntimeId,
        FixVector2 selectedPosition)
    {
        EnsureActive();
        if (!Enum.IsDefined(typeof(LogicCardCommandKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (cardRuntimeId == 0)
            throw new ArgumentOutOfRangeException(nameof(cardRuntimeId));
        for (int i = 0; i < s_Pending.Count; i++)
        {
            if (s_Pending[i].CardRuntimeId == cardRuntimeId)
                throw new InvalidOperationException($"Card {cardRuntimeId} already has a pending logic command.");
        }

        var command = new LogicCardCommand(
            checked(LogicTimeControlService.CurrentFrame + 1),
            checked(s_LastSequence + 1),
            kind,
            cardRuntimeId,
            selectedPosition);
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

    public static void PublishResolvedCard(
        LogicCardCommandKind kind,
        ulong cardRuntimeId,
        string sourceBuildingInstanceId)
    {
        EnsureActive();
        if (!IsApplyingFrame)
            throw new InvalidOperationException("LogicCardCommandService.PublishResolvedCard requires the card command apply window.");
        if (kind != LogicCardCommandKind.Play && kind != LogicCardCommandKind.Discard)
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown resolved card command kind.");
        if (cardRuntimeId == 0)
            throw new ArgumentOutOfRangeException(nameof(cardRuntimeId), cardRuntimeId, "Card runtime id must be positive.");

        CardResolved?.Invoke(new LogicCardResolution(
            LogicTimeControlService.CurrentFrame,
            kind,
            cardRuntimeId,
            sourceBuildingInstanceId));
    }

#if UNITY_EDITOR
    public static void ApplyFrameForTests(ulong frameId, Action<LogicCardCommand> sink)
    {
        ApplyFrame(frameId, sink);
    }
#endif

    private static void ApplyFrame(ulong frameId, Action<LogicCardCommand> sink)
    {
        EnsureActive();
        if (sink == null)
            throw new ArgumentNullException(nameof(sink));
        if (frameId != LogicTimeControlService.CurrentFrame)
            throw new InvalidOperationException(
                $"LogicCardCommandService.ApplyFrame failed: time-control frame mismatch. time={LogicTimeControlService.CurrentFrame}, requested={frameId}.");
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicCardCommandService.ApplyFrame failed: nested command frame detected.");

        IsApplyingFrame = true;
        try
        {
            s_Due.Clear();
            for (int i = 0; i < s_Pending.Count; i++)
            {
                LogicCardCommand command = s_Pending[i];
                if (command.EffectiveFrame < frameId)
                {
                    throw new InvalidOperationException(
                        $"LogicCardCommandService.ApplyFrame failed: command missed its frame. sequence={command.Sequence}, effective={command.EffectiveFrame}, current={frameId}.");
                }
                if (command.EffectiveFrame == frameId)
                    s_Due.Add(command);
            }

            s_Due.Sort(s_CommandComparison);
            for (int i = 0; i < s_Due.Count; i++)
            {
                LogicCardCommand command = s_Due[i];
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

    private static int CompareCommands(LogicCardCommand left, LogicCardCommand right)
    {
        return left.Sequence.CompareTo(right.Sequence);
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        if (!LogicTimeControlService.IsPaused)
            throw new InvalidOperationException("LogicCardCommandService.ResetForWorldTransition failed: logic time is not paused.");
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicCardCommandService.ResetForWorldTransition failed: a command frame is being applied.");
        if (CommandApplying != null)
            throw new InvalidOperationException("LogicCardCommandService.ResetForWorldTransition failed: runtime consumer is still registered.");
        ClearState();
    }

    public static void ResetFrameTimeline()
    {
        EnsureActive();
        if (LogicTimeControlService.CurrentFrame != 0)
            throw new InvalidOperationException("LogicCardCommandService.ResetFrameTimeline failed: time-control frame is not zero.");
        if (s_Pending.Count != 0)
            throw new InvalidOperationException($"LogicCardCommandService.ResetFrameTimeline failed: {s_Pending.Count} pending commands remain.");
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

    private static void PublishCommandApplying(LogicCardCommand command)
    {
        Action<LogicCardCommand> handler = CommandApplying;
        if (handler == null || handler.GetInvocationList().Length != 1)
            throw new InvalidOperationException("LogicCardCommandService requires exactly one runtime consumer while applying a command.");
        handler(command);
    }

    private static void RecordApplied(LogicCardCommand command)
    {
        var hasher = new LogicStateHasher();
        hasher.Add(0x43415244434D4421UL);
        hasher.Add(s_AppliedHistoryHash);
        AddCommand(hasher, command);
        s_AppliedHistoryHash = hasher.Hash;
        s_AppliedCount = checked(s_AppliedCount + 1);
    }

    private static void AddCommand(LogicStateHasher hasher, LogicCardCommand command)
    {
        hasher.Add(command.EffectiveFrame);
        hasher.Add(command.Sequence);
        hasher.Add((int)command.Kind);
        hasher.Add(command.CardRuntimeId);
        hasher.Add(command.SelectedPosition.x.RawValue);
        hasher.Add(command.SelectedPosition.y.RawValue);
    }

    private static void EnsureActive()
    {
        if (!IsActive || !LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicCardCommandService operation failed: service is not active.");
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
