using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public enum LogicInteractionActionKind
{
    ConstructBuilding = 0,
    UpgradeBuilding = 1,
    ResearchTech = 2,
    RecycleBuilding = 3,
}

public readonly struct LogicInteractionCommand
{
    public LogicInteractionCommand(
        ulong effectiveFrame,
        ulong sequence,
        LogicInteractionActionKind actionKind,
        LogicEntityId targetEntityId,
        string targetBuildingInstanceId,
        string primaryId,
        string secondaryId)
    {
        EffectiveFrame = effectiveFrame;
        Sequence = sequence;
        ActionKind = actionKind;
        TargetEntityId = targetEntityId;
        TargetBuildingInstanceId = targetBuildingInstanceId;
        PrimaryId = primaryId;
        SecondaryId = secondaryId;
    }

    public ulong EffectiveFrame { get; }
    public ulong Sequence { get; }
    public LogicInteractionActionKind ActionKind { get; }
    public LogicEntityId TargetEntityId { get; }
    public string TargetBuildingInstanceId { get; }
    public string PrimaryId { get; }
    public string SecondaryId { get; }
}

public static class LogicInteractionCommandService
{
    private static readonly Action<LogicInteractionCommand> s_RuntimeSink = PublishCommandApplying;
    private static readonly Comparison<LogicInteractionCommand> s_CommandComparison = CompareCommands;
    private static readonly List<LogicInteractionCommand> s_History = new List<LogicInteractionCommand>();
    private static readonly ReadOnlyCollection<LogicInteractionCommand> s_ReadOnlyHistory = s_History.AsReadOnly();
    private static readonly List<LogicInteractionCommand> s_Pending = new List<LogicInteractionCommand>();
    private static readonly List<LogicInteractionCommand> s_Due = new List<LogicInteractionCommand>();
    private static readonly Queue<LogicInteractionCommand> s_PendingAppliedPresentation =
        new Queue<LogicInteractionCommand>();
    private static ulong s_LastSequence;
    private static ulong s_AppliedHistoryHash;
    private static int s_AppliedCount;

    public static bool IsActive { get; private set; }
    public static bool IsApplyingFrame { get; private set; }
    public static ulong LastAppliedFrame { get; private set; }
    public static int PendingCount => s_Pending.Count;
    public static int PendingAppliedPresentationCount => s_PendingAppliedPresentation.Count;
    public static int AppliedCount => s_AppliedCount;
    public static IReadOnlyList<LogicInteractionCommand> History => s_ReadOnlyHistory;
    public static event Action<LogicInteractionCommand> CommandRecorded;
    public static event Action<LogicInteractionCommand> CommandApplying;
    public static event Action<LogicInteractionCommand> CommandAppliedPresentation;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicInteractionCommandService.BeginTimeline failed: service is already active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicInteractionCommandService.BeginTimeline failed: logic time control is not active.");

        IsActive = true;
        ClearState();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicInteractionCommandService.EndTimeline failed: a command frame is being applied.");

        IsActive = false;
        ClearState();
    }

    public static LogicInteractionCommand ScheduleForNextFrame(
        LogicInteractionActionKind actionKind,
        LogicEntityId targetEntityId,
        string targetBuildingInstanceId,
        string primaryId = null,
        string secondaryId = null)
    {
        EnsureActive();
        ValidatePayload(actionKind, targetEntityId, targetBuildingInstanceId, primaryId, secondaryId);
        if (HasPendingForTarget(targetEntityId))
        {
            throw new InvalidOperationException(
                $"Logic interaction target already has a pending command. entity={targetEntityId.Value}.");
        }

        var command = new LogicInteractionCommand(
            checked(LogicTimeControlService.CurrentFrame + 1),
            checked(s_LastSequence + 1),
            actionKind,
            targetEntityId,
            targetBuildingInstanceId,
            primaryId,
            secondaryId);
        s_LastSequence = command.Sequence;
        s_Pending.Add(command);
        s_History.Add(command);
        CommandRecorded?.Invoke(command);
        return command;
    }

    public static bool HasPendingForTarget(LogicEntityId targetEntityId)
    {
        EnsureActive();
        if (!targetEntityId.IsValid)
            throw new ArgumentException("Target entity id must be valid.", nameof(targetEntityId));

        for (int i = 0; i < s_Pending.Count; i++)
        {
            if (s_Pending[i].TargetEntityId == targetEntityId)
                return true;
        }
        return false;
    }

    public static void ApplyFrame(ulong frameId)
    {
        ApplyFrame(frameId, s_RuntimeSink);
    }

#if UNITY_EDITOR
    public static void ApplyFrameForTests(ulong frameId, Action<LogicInteractionCommand> sink)
    {
        ApplyFrame(frameId, sink);
    }
#endif

    private static void ApplyFrame(ulong frameId, Action<LogicInteractionCommand> sink)
    {
        EnsureActive();
        if (sink == null)
            throw new ArgumentNullException(nameof(sink));
        if (frameId != LogicTimeControlService.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicInteractionCommandService.ApplyFrame failed: time-control frame mismatch. time={LogicTimeControlService.CurrentFrame}, requested={frameId}.");
        }
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicInteractionCommandService.ApplyFrame failed: nested command frame detected.");

        IsApplyingFrame = true;
        try
        {
            s_Due.Clear();
            for (int i = 0; i < s_Pending.Count; i++)
            {
                LogicInteractionCommand command = s_Pending[i];
                if (command.EffectiveFrame < frameId)
                {
                    throw new InvalidOperationException(
                        $"LogicInteractionCommandService.ApplyFrame failed: command missed its frame. sequence={command.Sequence}, effective={command.EffectiveFrame}, current={frameId}.");
                }
                if (command.EffectiveFrame == frameId)
                    s_Due.Add(command);
            }

            s_Due.Sort(s_CommandComparison);
            for (int i = 0; i < s_Due.Count; i++)
            {
                LogicInteractionCommand command = s_Due[i];
                sink(command);
                RecordApplied(command);
                s_PendingAppliedPresentation.Enqueue(command);
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

    public static void UpdatePresentationEvents()
    {
        EnsureActive();
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("LogicInteractionCommandService presentation events cannot run inside a logic tick.");

        while (s_PendingAppliedPresentation.Count > 0)
        {
            LogicInteractionCommand command = s_PendingAppliedPresentation.Dequeue();
            CommandAppliedPresentation?.Invoke(command);
        }
    }

    private static int CompareCommands(LogicInteractionCommand left, LogicInteractionCommand right)
    {
        return left.Sequence.CompareTo(right.Sequence);
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        if (!LogicTimeControlService.IsPaused)
            throw new InvalidOperationException("LogicInteractionCommandService.ResetForWorldTransition failed: logic time is not paused.");
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicInteractionCommandService.ResetForWorldTransition failed: a command frame is being applied.");

        ClearState();
    }

    public static void ResetFrameTimeline()
    {
        EnsureActive();
        if (LogicTimeControlService.CurrentFrame != 0)
            throw new InvalidOperationException("LogicInteractionCommandService.ResetFrameTimeline failed: time-control frame is not zero.");
        if (s_Pending.Count != 0)
            throw new InvalidOperationException($"LogicInteractionCommandService.ResetFrameTimeline failed: {s_Pending.Count} pending commands remain.");

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

    private static void PublishCommandApplying(LogicInteractionCommand command)
    {
        Action<LogicInteractionCommand> handler = CommandApplying;
        if (handler == null)
            throw new InvalidOperationException("LogicInteractionCommandService.ApplyFrame failed: no runtime interaction consumer is registered.");
        if (handler.GetInvocationList().Length != 1)
            throw new InvalidOperationException("LogicInteractionCommandService.ApplyFrame failed: exactly one runtime interaction consumer is required.");

        handler(command);
    }

    private static void RecordApplied(LogicInteractionCommand command)
    {
        var hasher = new LogicStateHasher();
        hasher.Add(0x494E544552414354UL);
        hasher.Add(s_AppliedHistoryHash);
        AddCommand(hasher, command);
        s_AppliedHistoryHash = hasher.Hash;
        s_AppliedCount = checked(s_AppliedCount + 1);
    }

    private static void AddCommand(LogicStateHasher hasher, LogicInteractionCommand command)
    {
        hasher.Add(command.EffectiveFrame);
        hasher.Add(command.Sequence);
        hasher.Add((int)command.ActionKind);
        hasher.Add(command.TargetEntityId.Value);
        hasher.Add(command.TargetBuildingInstanceId);
        hasher.Add(command.PrimaryId);
        hasher.Add(command.SecondaryId);
    }

    private static void ValidatePayload(
        LogicInteractionActionKind actionKind,
        LogicEntityId targetEntityId,
        string targetBuildingInstanceId,
        string primaryId,
        string secondaryId)
    {
        if (!Enum.IsDefined(typeof(LogicInteractionActionKind), actionKind))
            throw new ArgumentOutOfRangeException(nameof(actionKind), actionKind, "Unknown interaction action kind.");
        if (!targetEntityId.IsValid)
            throw new ArgumentException("Target entity id must be valid.", nameof(targetEntityId));
        if (string.IsNullOrWhiteSpace(targetBuildingInstanceId))
            throw new ArgumentException("Target building instance id is required.", nameof(targetBuildingInstanceId));

        bool primaryRequired = actionKind != LogicInteractionActionKind.RecycleBuilding;
        bool secondaryRequired = actionKind == LogicInteractionActionKind.UpgradeBuilding;
        if (primaryRequired && string.IsNullOrWhiteSpace(primaryId))
            throw new ArgumentException("Primary action id is required.", nameof(primaryId));
        if (secondaryRequired && string.IsNullOrWhiteSpace(secondaryId))
            throw new ArgumentException("Secondary action id is required.", nameof(secondaryId));
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicInteractionCommandService operation failed: service is not active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicInteractionCommandService operation failed: logic time control is not active.");
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
        s_PendingAppliedPresentation.Clear();
    }
}
