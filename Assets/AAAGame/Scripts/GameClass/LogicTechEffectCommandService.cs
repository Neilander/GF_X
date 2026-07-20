using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public readonly struct LogicTechEffectCommand
{
    public LogicTechEffectCommand(
        ulong effectiveFrame,
        ulong sequence,
        string techId,
        bool isStackable,
        int ownerFactionId,
        string sourceBuildingInstanceId)
    {
        EffectiveFrame = effectiveFrame;
        Sequence = sequence;
        TechId = techId;
        IsStackable = isStackable;
        OwnerFactionId = ownerFactionId;
        SourceBuildingInstanceId = sourceBuildingInstanceId;
    }

    public ulong EffectiveFrame { get; }
    public ulong Sequence { get; }
    public string TechId { get; }
    public bool IsStackable { get; }
    public int OwnerFactionId { get; }
    public string SourceBuildingInstanceId { get; }
}

public static class LogicTechEffectCommandService
{
    private static readonly List<LogicTechEffectCommand> s_History = new List<LogicTechEffectCommand>();
    private static readonly ReadOnlyCollection<LogicTechEffectCommand> s_ReadOnlyHistory = s_History.AsReadOnly();
    private static readonly List<LogicTechEffectCommand> s_Pending = new List<LogicTechEffectCommand>();
    private static readonly List<LogicTechEffectCommand> s_Due = new List<LogicTechEffectCommand>();
    private static ulong s_LastSequence;
    private static ulong s_AppliedHistoryHash;
    private static int s_AppliedCount;

    public static bool IsActive { get; private set; }
    public static bool IsApplyingFrame { get; private set; }
    public static ulong LastAppliedFrame { get; private set; }
    public static int PendingCount => s_Pending.Count;
    public static int AppliedCount => s_AppliedCount;
    public static IReadOnlyList<LogicTechEffectCommand> History => s_ReadOnlyHistory;
    public static event Action<LogicTechEffectCommand> CommandRecorded;
    public static event Action<LogicTechEffectCommand> EffectApplying;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicTechEffectCommandService.BeginTimeline failed: service is already active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicTechEffectCommandService.BeginTimeline failed: logic time control is not active.");

        IsActive = true;
        ClearState();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicTechEffectCommandService.EndTimeline failed: a command frame is being applied.");

        IsActive = false;
        ClearState();
    }

    public static LogicTechEffectCommand ScheduleForNextFrame(
        string techId,
        bool isStackable,
        int ownerFactionId,
        string sourceBuildingInstanceId)
    {
        EnsureActive();
        ValidatePayload(techId, ownerFactionId, sourceBuildingInstanceId);
        return Schedule(
            checked(LogicTimeControlService.CurrentFrame + 1),
            techId,
            isStackable,
            ownerFactionId,
            sourceBuildingInstanceId);
    }

    public static LogicTechEffectCommand ScheduleForCurrentInteractionFrame(
        string techId,
        bool isStackable,
        int ownerFactionId,
        string sourceBuildingInstanceId)
    {
        EnsureActive();
        ValidatePayload(techId, ownerFactionId, sourceBuildingInstanceId);
        if (!LogicInteractionCommandService.IsApplyingFrame)
            throw new InvalidOperationException("Current-frame tech scheduling requires the interaction command apply window.");
        if (LogicTimeControlService.CurrentFrame == 0)
            throw new InvalidOperationException("Current-frame tech scheduling requires a positive logic frame.");
        if (LastAppliedFrame >= LogicTimeControlService.CurrentFrame)
            throw new InvalidOperationException("Current-frame tech scheduling occurred after the tech command apply window.");

        return Schedule(
            LogicTimeControlService.CurrentFrame,
            techId,
            isStackable,
            ownerFactionId,
            sourceBuildingInstanceId);
    }

    private static LogicTechEffectCommand Schedule(
        ulong effectiveFrame,
        string techId,
        bool isStackable,
        int ownerFactionId,
        string sourceBuildingInstanceId)
    {

        var command = new LogicTechEffectCommand(
            effectiveFrame,
            checked(s_LastSequence + 1),
            techId,
            isStackable,
            ownerFactionId,
            sourceBuildingInstanceId);
        s_LastSequence = command.Sequence;
        s_Pending.Add(command);
        s_History.Add(command);
        CommandRecorded?.Invoke(command);
        return command;
    }

    public static bool HasPending(string techId)
    {
        EnsureActive();
        if (string.IsNullOrWhiteSpace(techId))
            throw new ArgumentException("Tech id is required.", nameof(techId));

        for (int i = 0; i < s_Pending.Count; i++)
        {
            if (string.Equals(s_Pending[i].TechId, techId, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public static bool HasPending(string techId, string sourceBuildingInstanceId)
    {
        EnsureActive();
        if (string.IsNullOrWhiteSpace(techId))
            throw new ArgumentException("Tech id is required.", nameof(techId));
        if (string.IsNullOrWhiteSpace(sourceBuildingInstanceId))
            throw new ArgumentException("Source building instance id is required.", nameof(sourceBuildingInstanceId));

        for (int i = 0; i < s_Pending.Count; i++)
        {
            LogicTechEffectCommand command = s_Pending[i];
            if (string.Equals(command.TechId, techId, StringComparison.Ordinal)
                && string.Equals(command.SourceBuildingInstanceId, sourceBuildingInstanceId, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    public static void ApplyFrame(ulong frameId)
    {
        ApplyFrame(frameId, PublishEffectApplying);
    }

#if UNITY_EDITOR
    public static void ApplyFrameForTests(ulong frameId, Action<LogicTechEffectCommand> sink)
    {
        ApplyFrame(frameId, sink);
    }
#endif

    private static void ApplyFrame(ulong frameId, Action<LogicTechEffectCommand> sink)
    {
        EnsureActive();
        if (sink == null)
            throw new ArgumentNullException(nameof(sink));
        if (frameId != LogicTimeControlService.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicTechEffectCommandService.ApplyFrame failed: time-control frame mismatch. time={LogicTimeControlService.CurrentFrame}, requested={frameId}.");
        }
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicTechEffectCommandService.ApplyFrame failed: nested command frame detected.");

        IsApplyingFrame = true;
        try
        {
            s_Due.Clear();
            for (int i = 0; i < s_Pending.Count; i++)
            {
                LogicTechEffectCommand command = s_Pending[i];
                if (command.EffectiveFrame < frameId)
                {
                    throw new InvalidOperationException(
                        $"LogicTechEffectCommandService.ApplyFrame failed: command missed its frame. sequence={command.Sequence}, effective={command.EffectiveFrame}, current={frameId}.");
                }
                if (command.EffectiveFrame == frameId)
                    s_Due.Add(command);
            }

            s_Due.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
            for (int i = 0; i < s_Due.Count; i++)
            {
                LogicTechEffectCommand command = s_Due[i];
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
            throw new InvalidOperationException("LogicTechEffectCommandService.ResetForWorldTransition failed: logic time is not paused.");
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicTechEffectCommandService.ResetForWorldTransition failed: a command frame is being applied.");

        ClearState();
    }

    public static void ResetFrameTimeline()
    {
        EnsureActive();
        if (LogicTimeControlService.CurrentFrame != 0)
            throw new InvalidOperationException("LogicTechEffectCommandService.ResetFrameTimeline failed: time-control frame is not zero.");
        if (s_Pending.Count != 0)
            throw new InvalidOperationException($"LogicTechEffectCommandService.ResetFrameTimeline failed: {s_Pending.Count} pending commands remain.");

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

    private static void PublishEffectApplying(LogicTechEffectCommand command)
    {
        if (GF.Event == null)
            throw new InvalidOperationException("LogicTechEffectCommandService.ApplyFrame failed: GF.Event is unavailable.");

        InGameDataModel.ApplyScheduledTechUnlock(command);

        Action<LogicTechEffectCommand> handler = EffectApplying;
        if (handler == null)
            throw new InvalidOperationException("LogicTechEffectCommandService.ApplyFrame failed: no runtime tech-effect consumer is registered.");
        if (handler.GetInvocationList().Length != 1)
            throw new InvalidOperationException("LogicTechEffectCommandService.ApplyFrame failed: exactly one runtime tech-effect consumer is required.");

        handler(command);
        GF.Event.Fire(
            typeof(InGameDataModel),
            TechUnlockedEventArgs.Create(command.TechId, command.OwnerFactionId, command.SourceBuildingInstanceId));
    }

    private static void RecordApplied(LogicTechEffectCommand command)
    {
        var hasher = new LogicStateHasher();
        hasher.Add(0x544543484150504CUL);
        hasher.Add(s_AppliedHistoryHash);
        AddCommand(hasher, command);
        s_AppliedHistoryHash = hasher.Hash;
        s_AppliedCount = checked(s_AppliedCount + 1);
    }

    private static void AddCommand(LogicStateHasher hasher, LogicTechEffectCommand command)
    {
        hasher.Add(command.EffectiveFrame);
        hasher.Add(command.Sequence);
        hasher.Add(command.TechId);
        hasher.Add(command.IsStackable);
        hasher.Add(command.OwnerFactionId);
        hasher.Add(command.SourceBuildingInstanceId);
    }

    private static void ValidatePayload(string techId, int ownerFactionId, string sourceBuildingInstanceId)
    {
        if (string.IsNullOrWhiteSpace(techId))
            throw new ArgumentException("Tech id is required.", nameof(techId));
        if (ownerFactionId < 0)
            throw new ArgumentOutOfRangeException(nameof(ownerFactionId), ownerFactionId, "Owner faction id cannot be negative.");
        if (string.IsNullOrWhiteSpace(sourceBuildingInstanceId))
            throw new ArgumentException("Source building instance id is required.", nameof(sourceBuildingInstanceId));
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicTechEffectCommandService operation failed: service is not active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicTechEffectCommandService operation failed: logic time control is not active.");
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
