using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public enum LogicObstacleCommandKind
{
    AddOrUpdateBox = 0,
    AddOrUpdateCircle = 1,
    Remove = 2,
}

public readonly struct LogicObstacleCommand
{
    public LogicObstacleCommand(
        ulong effectiveFrame,
        ulong sequence,
        LogicObstacleCommandKind kind,
        int stableObstacleId,
        FixVector2 center,
        FixVector2 halfExtents,
        Fix64 radius)
    {
        EffectiveFrame = effectiveFrame;
        Sequence = sequence;
        Kind = kind;
        StableObstacleId = stableObstacleId;
        Center = center;
        HalfExtents = halfExtents;
        Radius = radius;
    }

    public ulong EffectiveFrame { get; }
    public ulong Sequence { get; }
    public LogicObstacleCommandKind Kind { get; }
    public int StableObstacleId { get; }
    public FixVector2 Center { get; }
    public FixVector2 HalfExtents { get; }
    public Fix64 Radius { get; }

    public LogicObstacleCommand WithEffectiveFrame(ulong effectiveFrame)
    {
        return new LogicObstacleCommand(
            effectiveFrame,
            Sequence,
            Kind,
            StableObstacleId,
            Center,
            HalfExtents,
            Radius);
    }
}

public sealed class LogicObstacleCommandSnapshot
{
    internal LogicObstacleCommandSnapshot(
        ulong lastSequence,
        ulong lastAppliedFrame,
        bool isWorldTransitionActive,
        LogicObstacleCommand[] history,
        LogicObstacleCommand[] pending,
        LogicObstacleCommand[] active)
    {
        LastSequence = lastSequence;
        LastAppliedFrame = lastAppliedFrame;
        IsWorldTransitionActive = isWorldTransitionActive;
        History = (LogicObstacleCommand[])history.Clone();
        Pending = (LogicObstacleCommand[])pending.Clone();
        Active = (LogicObstacleCommand[])active.Clone();
    }

    public ulong LastSequence { get; }
    public ulong LastAppliedFrame { get; }
    public bool IsWorldTransitionActive { get; }
    internal LogicObstacleCommand[] History { get; }
    internal LogicObstacleCommand[] Pending { get; }
    internal LogicObstacleCommand[] Active { get; }
}

public static class LogicObstacleCommandService
{
    private static readonly Action<LogicObstacleCommand> s_RuntimeSink = ApplyToFlowRuntime;
    private sealed class CommandComparer : IComparer<LogicObstacleCommand>
    {
        public int Compare(LogicObstacleCommand x, LogicObstacleCommand y)
        {
            int idComparison = x.StableObstacleId.CompareTo(y.StableObstacleId);
            return idComparison != 0 ? idComparison : x.Sequence.CompareTo(y.Sequence);
        }
    }

    private sealed class PendingCommandComparer : IComparer<LogicObstacleCommand>
    {
        public int Compare(LogicObstacleCommand x, LogicObstacleCommand y)
        {
            int result = x.EffectiveFrame.CompareTo(y.EffectiveFrame);
            if (result != 0)
                return result;
            result = x.StableObstacleId.CompareTo(y.StableObstacleId);
            return result != 0 ? result : x.Sequence.CompareTo(y.Sequence);
        }
    }

    private static readonly List<LogicObstacleCommand> s_History = new List<LogicObstacleCommand>();
    private static readonly ReadOnlyCollection<LogicObstacleCommand> s_ReadOnlyHistory = s_History.AsReadOnly();
    private static readonly List<LogicObstacleCommand> s_Pending = new List<LogicObstacleCommand>();
    private static readonly List<LogicObstacleCommand> s_Due = new List<LogicObstacleCommand>();
    private static readonly Dictionary<int, LogicObstacleCommand> s_Active = new Dictionary<int, LogicObstacleCommand>();
    private static readonly CommandComparer s_CommandComparer = new CommandComparer();
    private static readonly PendingCommandComparer s_PendingCommandComparer = new PendingCommandComparer();
    private static readonly Comparison<LogicObstacleCommand> s_CommandComparison =
        (left, right) => s_CommandComparer.Compare(left, right);
    private static readonly Comparison<LogicObstacleCommand> s_PendingCommandComparison =
        (left, right) => s_PendingCommandComparer.Compare(left, right);
    private static readonly List<LogicObstacleCommand> s_DeterministicActive = new List<LogicObstacleCommand>();
    private static readonly List<LogicObstacleCommand> s_DeterministicPending = new List<LogicObstacleCommand>();
    private static ulong s_LastSequence;

    public static bool IsActive { get; private set; }
    public static bool IsApplyingFrame { get; private set; }
    public static bool IsWorldTransitionActive { get; private set; }
    public static ulong LastAppliedFrame { get; private set; }
    public static int PendingCount => s_Pending.Count;
    public static int ActiveObstacleCount => s_Active.Count;
    public static IReadOnlyList<LogicObstacleCommand> History => s_ReadOnlyHistory;
    public static event Action<LogicObstacleCommand> CommandRecorded;

    public static void WriteDeterministicState(LogicStateHasher hasher, ulong frame)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));
        EnsureActive();
        if (IsApplyingFrame || LastAppliedFrame != frame)
        {
            throw new InvalidOperationException(
                $"LogicObstacleCommandService deterministic frame mismatch. requested={frame}, applied={LastAppliedFrame}, applying={IsApplyingFrame}.");
        }

        s_DeterministicActive.Clear();
        foreach (LogicObstacleCommand command in s_Active.Values)
            s_DeterministicActive.Add(command);
        s_DeterministicActive.Sort(s_CommandComparison);
        s_DeterministicPending.Clear();
        s_DeterministicPending.AddRange(s_Pending);
        s_DeterministicPending.Sort(s_PendingCommandComparison);
        try
        {
            hasher.Add(s_LastSequence);
            hasher.Add(s_DeterministicActive.Count);
            for (int i = 0; i < s_DeterministicActive.Count; i++)
                WriteCommandState(hasher, s_DeterministicActive[i], false);
            hasher.Add(s_DeterministicPending.Count);
            for (int i = 0; i < s_DeterministicPending.Count; i++)
                WriteCommandState(hasher, s_DeterministicPending[i], true);
        }
        finally
        {
            s_DeterministicActive.Clear();
            s_DeterministicPending.Clear();
        }
    }

    private static void WriteCommandState(
        LogicStateHasher hasher,
        LogicObstacleCommand command,
        bool includeTimeline)
    {
        if (includeTimeline)
        {
            hasher.Add(command.EffectiveFrame);
            hasher.Add(command.Sequence);
        }
        hasher.Add((int)command.Kind);
        hasher.Add(command.StableObstacleId);
        hasher.Add(command.Center.x.RawValue);
        hasher.Add(command.Center.y.RawValue);
        hasher.Add(command.HalfExtents.x.RawValue);
        hasher.Add(command.HalfExtents.y.RawValue);
        hasher.Add(command.Radius.RawValue);
    }

    public static LogicObstacleCommandSnapshot CaptureSnapshot()
    {
        EnsureActive();
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicObstacleCommandService.CaptureSnapshot failed: a command frame is being applied.");

        var active = new List<LogicObstacleCommand>(s_Active.Values);
        active.Sort(s_CommandComparer);
        return new LogicObstacleCommandSnapshot(
            s_LastSequence,
            LastAppliedFrame,
            IsWorldTransitionActive,
            s_History.ToArray(),
            s_Pending.ToArray(),
            active.ToArray());
    }

    public static void RestoreSnapshot(LogicObstacleCommandSnapshot snapshot)
    {
        RestoreSnapshot(snapshot, ApplyToFlowRuntime);
    }

#if UNITY_EDITOR
    public static void RestoreSnapshotForTests(LogicObstacleCommandSnapshot snapshot, Action<LogicObstacleCommand> sink)
    {
        RestoreSnapshot(snapshot, sink);
    }
#endif

    internal static void RestoreSnapshot(LogicObstacleCommandSnapshot snapshot, Action<LogicObstacleCommand> sink)
    {
        EnsureActive();
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        if (sink == null)
            throw new ArgumentNullException(nameof(sink));
        if (IsApplyingFrame || LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("LogicObstacleCommandService.RestoreSnapshot failed: a logic or command frame is running.");

        var currentIds = new List<int>(s_Active.Keys);
        currentIds.Sort();
        for (int i = 0; i < currentIds.Count; i++)
        {
            int id = currentIds[i];
            sink(new LogicObstacleCommand(
                snapshot.LastAppliedFrame,
                0,
                LogicObstacleCommandKind.Remove,
                id,
                FixVector2.Zero,
                FixVector2.Zero,
                Fix64.Zero));
        }

        s_History.Clear();
        s_History.AddRange(snapshot.History);
        s_Pending.Clear();
        s_Pending.AddRange(snapshot.Pending);
        s_Active.Clear();
        for (int i = 0; i < snapshot.Active.Length; i++)
        {
            LogicObstacleCommand command = snapshot.Active[i];
            if (command.Kind == LogicObstacleCommandKind.Remove || s_Active.ContainsKey(command.StableObstacleId))
                throw new InvalidOperationException($"LogicObstacleCommandService.RestoreSnapshot failed: invalid active obstacle {command.StableObstacleId}.");
            s_Active.Add(command.StableObstacleId, command);
            sink(command);
        }

        s_LastSequence = snapshot.LastSequence;
        LastAppliedFrame = snapshot.LastAppliedFrame;
        IsWorldTransitionActive = snapshot.IsWorldTransitionActive;
        IsApplyingFrame = false;
        s_Due.Clear();
    }

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicObstacleCommandService.BeginTimeline failed: service is already active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicObstacleCommandService.BeginTimeline failed: logic time control is not active.");

        IsActive = true;
        ClearState();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicObstacleCommandService.EndTimeline failed: a command frame is being applied.");

        IsActive = false;
        ClearState();
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        if (!LogicTimeControlService.IsPaused)
            throw new InvalidOperationException("LogicObstacleCommandService.ResetForWorldTransition failed: logic time is not paused.");
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicObstacleCommandService.ResetForWorldTransition failed: a command frame is being applied.");

        ClearState();
        IsWorldTransitionActive = true;
    }

    public static void ResetFrameTimelinePreservingCommands()
    {
        EnsureActive();
        if (LogicTimeControlService.CurrentFrame != 0)
            throw new InvalidOperationException("LogicObstacleCommandService.ResetFrameTimelinePreservingCommands failed: time-control frame is not zero.");
        if (s_Active.Count != 0)
            throw new InvalidOperationException($"LogicObstacleCommandService.ResetFrameTimelinePreservingCommands failed: {s_Active.Count} active obstacles remain from the previous world.");

        for (int i = 0; i < s_Pending.Count; i++)
        {
            LogicObstacleCommand command = s_Pending[i].WithEffectiveFrame(1);
            s_Pending[i] = command;
            for (int historyIndex = 0; historyIndex < s_History.Count; historyIndex++)
            {
                if (s_History[historyIndex].Sequence != command.Sequence)
                    continue;

                s_History[historyIndex] = command;
                break;
            }
        }

        LastAppliedFrame = 0;
        IsWorldTransitionActive = false;
    }

    public static void ScheduleBoxForNextFrame(int stableObstacleId, FixVector2 center, FixVector2 halfExtents)
    {
        ScheduleBox(checked(LogicTimeControlService.CurrentFrame + 1), stableObstacleId, center, halfExtents);
    }

    public static void RegisterInitialBoxObstacle(int stableObstacleId, FixVector2 center, FixVector2 halfExtents)
    {
        RegisterInitialBoxObstacle(stableObstacleId, center, halfExtents, s_RuntimeSink);
    }

#if UNITY_EDITOR
    public static void RegisterInitialBoxObstacleForTests(
        int stableObstacleId,
        FixVector2 center,
        FixVector2 halfExtents,
        Action<LogicObstacleCommand> sink)
    {
        RegisterInitialBoxObstacle(stableObstacleId, center, halfExtents, sink);
    }
#endif

    public static void ScheduleCircleForNextFrame(int stableObstacleId, FixVector2 center, Fix64 radius)
    {
        ScheduleCircle(checked(LogicTimeControlService.CurrentFrame + 1), stableObstacleId, center, radius);
    }

    public static void ScheduleRemoveForNextFrame(int stableObstacleId)
    {
        ScheduleRemove(checked(LogicTimeControlService.CurrentFrame + 1), stableObstacleId);
    }

    public static void ScheduleBoxForCurrentLifecycleFrame(int stableObstacleId, FixVector2 center, FixVector2 halfExtents)
    {
        EnsureLifecycleFrameWindow();
        ScheduleBox(LogicTimeControlService.CurrentFrame, stableObstacleId, center, halfExtents);
    }

    public static void ScheduleCircleForCurrentLifecycleFrame(int stableObstacleId, FixVector2 center, Fix64 radius)
    {
        EnsureLifecycleFrameWindow();
        ScheduleCircle(LogicTimeControlService.CurrentFrame, stableObstacleId, center, radius);
    }

    public static void ScheduleRemoveForCurrentLifecycleFrame(int stableObstacleId)
    {
        EnsureLifecycleFrameWindow();
        ScheduleRemove(LogicTimeControlService.CurrentFrame, stableObstacleId);
    }

    public static void ApplyFrame(ulong frameId)
    {
        ApplyFrame(frameId, s_RuntimeSink);
    }

#if UNITY_EDITOR
    public static void ApplyFrameForTests(ulong frameId, Action<LogicObstacleCommand> sink)
    {
        ApplyFrame(frameId, sink);
    }
#endif

    private static void ApplyFrame(ulong frameId, Action<LogicObstacleCommand> sink)
    {
        EnsureActive();
        if (sink == null)
            throw new ArgumentNullException(nameof(sink));
        if (frameId != LogicTimeControlService.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicObstacleCommandService.ApplyFrame failed: time-control frame mismatch. time={LogicTimeControlService.CurrentFrame}, requested={frameId}.");
        }
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicObstacleCommandService.ApplyFrame failed: nested command frame detected.");

        IsApplyingFrame = true;
        try
        {
            s_Due.Clear();
            for (int i = 0; i < s_Pending.Count; i++)
            {
                LogicObstacleCommand command = s_Pending[i];
                if (command.EffectiveFrame < frameId)
                {
                    throw new InvalidOperationException(
                        $"LogicObstacleCommandService.ApplyFrame failed: command missed its frame. obstacle={command.StableObstacleId}, effective={command.EffectiveFrame}, current={frameId}.");
                }
                if (command.EffectiveFrame == frameId)
                    s_Due.Add(command);
            }

            s_Due.Sort(s_CommandComparer);
            for (int i = 0; i < s_Due.Count; i++)
            {
                LogicObstacleCommand command = s_Due[i];
                if (command.Kind == LogicObstacleCommandKind.Remove)
                {
                    if (!s_Active.ContainsKey(command.StableObstacleId))
                    {
                        throw new InvalidOperationException(
                            $"LogicObstacleCommandService.ApplyFrame failed: remove targets an inactive obstacle. obstacle={command.StableObstacleId}, sequence={command.Sequence}.");
                    }

                    sink(command);
                    s_Active.Remove(command.StableObstacleId);
                }
                else
                {
                    sink(command);
                    s_Active[command.StableObstacleId] = command;
                }
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

    private static void ScheduleBox(ulong effectiveFrame, int stableObstacleId, FixVector2 center, FixVector2 halfExtents)
    {
        if (halfExtents.x <= Fix64.Zero || halfExtents.y <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(halfExtents), "Box half extents must be positive.");

        Record(new LogicObstacleCommand(
            effectiveFrame,
            NextSequence(),
            LogicObstacleCommandKind.AddOrUpdateBox,
            ValidateObstacleId(stableObstacleId),
            center,
            halfExtents,
            Fix64.Zero));
    }

    private static void RegisterInitialBoxObstacle(
        int stableObstacleId,
        FixVector2 center,
        FixVector2 halfExtents,
        Action<LogicObstacleCommand> sink)
    {
        EnsureActive();
        if (LogicTimeControlService.CurrentFrame != 0 || LogicFrameRuntime.IsExecutingFrame || IsApplyingFrame)
            throw new InvalidOperationException("Initial obstacles can only be registered before the first logic frame.");
        if (sink == null)
            throw new ArgumentNullException(nameof(sink));
        if (halfExtents.x <= Fix64.Zero || halfExtents.y <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(halfExtents), "Box half extents must be positive.");

        int obstacleId = ValidateObstacleId(stableObstacleId);
        if (s_Active.ContainsKey(obstacleId))
            throw new InvalidOperationException($"Initial obstacle {obstacleId} is already active.");

        var command = new LogicObstacleCommand(
            0,
            0,
            LogicObstacleCommandKind.AddOrUpdateBox,
            obstacleId,
            center,
            halfExtents,
            Fix64.Zero);
        sink(command);
        s_Active.Add(obstacleId, command);
    }

    private static void ScheduleCircle(ulong effectiveFrame, int stableObstacleId, FixVector2 center, Fix64 radius)
    {
        if (radius <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "Circle radius must be positive.");

        Record(new LogicObstacleCommand(
            effectiveFrame,
            NextSequence(),
            LogicObstacleCommandKind.AddOrUpdateCircle,
            ValidateObstacleId(stableObstacleId),
            center,
            FixVector2.Zero,
            radius));
    }

    private static void ScheduleRemove(ulong effectiveFrame, int stableObstacleId)
    {
        Record(new LogicObstacleCommand(
            effectiveFrame,
            NextSequence(),
            LogicObstacleCommandKind.Remove,
            ValidateObstacleId(stableObstacleId),
            FixVector2.Zero,
            FixVector2.Zero,
            Fix64.Zero));
    }

    private static void Record(LogicObstacleCommand command)
    {
        EnsureActive();
        if (command.EffectiveFrame == 0 || command.EffectiveFrame < LogicTimeControlService.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicObstacleCommandService.Record failed: invalid effective frame. effective={command.EffectiveFrame}, current={LogicTimeControlService.CurrentFrame}.");
        }

        s_Pending.Add(command);
        s_History.Add(command);
        CommandRecorded?.Invoke(command);
    }

    private static ulong NextSequence()
    {
        EnsureActive();
        s_LastSequence = checked(s_LastSequence + 1);
        return s_LastSequence;
    }

    private static int ValidateObstacleId(int stableObstacleId)
    {
        if (stableObstacleId == 0)
            throw new ArgumentOutOfRangeException(nameof(stableObstacleId), "Stable obstacle id must be non-zero.");
        return stableObstacleId;
    }

    private static void EnsureLifecycleFrameWindow()
    {
        EnsureActive();
        if (!LogicEntityLifecycleService.IsApplyingFrame)
        {
            throw new InvalidOperationException(
                "LogicObstacleCommandService current-frame scheduling is only allowed while lifecycle commands are being applied.");
        }
    }

    private static void ApplyToFlowRuntime(LogicObstacleCommand command)
    {
        switch (command.Kind)
        {
            case LogicObstacleCommandKind.AddOrUpdateBox:
                FlowFieldCrowdMovementSystem.RegisterBoxObstacleFixed(
                    command.StableObstacleId,
                    command.Center,
                    command.HalfExtents);
                break;
            case LogicObstacleCommandKind.AddOrUpdateCircle:
                FlowFieldCrowdMovementSystem.RegisterCircleObstacleFixed(
                    command.StableObstacleId,
                    command.Center,
                    command.Radius);
                break;
            case LogicObstacleCommandKind.Remove:
                FlowFieldCrowdMovementSystem.UnregisterObstacle(command.StableObstacleId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command.Kind), command.Kind, "Unknown obstacle command kind.");
        }
    }

#if UNITY_EDITOR
    public static void ApplyToFlowRuntimeForTests(LogicObstacleCommand command)
    {
        ApplyToFlowRuntime(command);
    }
#endif

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicObstacleCommandService operation failed: service is not active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicObstacleCommandService operation failed: logic time control is not active.");
    }

    private static void ClearState()
    {
        IsApplyingFrame = false;
        IsWorldTransitionActive = false;
        LastAppliedFrame = 0;
        s_LastSequence = 0;
        s_History.Clear();
        s_Pending.Clear();
        s_Due.Clear();
        s_Active.Clear();
    }
}
