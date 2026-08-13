using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public readonly struct LogicTeleportCommand
{
    public LogicTeleportCommand(
        ulong effectiveFrame,
        ulong sequence,
        LogicEntityId entityId,
        FixVector2 destination,
        string strongholdId)
    {
        EffectiveFrame = effectiveFrame;
        Sequence = sequence;
        EntityId = entityId;
        Destination = destination;
        StrongholdId = strongholdId ?? throw new ArgumentNullException(nameof(strongholdId));
    }

    public ulong EffectiveFrame { get; }
    public ulong Sequence { get; }
    public LogicEntityId EntityId { get; }
    public FixVector2 Destination { get; }
    public string StrongholdId { get; }
}

public static class LogicTeleportCommandService
{
    private static readonly Action<LogicTeleportCommand> s_RuntimeSink = ApplyRuntimeCommand;
    private static readonly Comparison<LogicTeleportCommand> s_CommandComparison = CompareCommands;
    private static readonly List<LogicTeleportCommand> s_History = new List<LogicTeleportCommand>();
    private static readonly ReadOnlyCollection<LogicTeleportCommand> s_ReadOnlyHistory = s_History.AsReadOnly();
    private static readonly List<LogicTeleportCommand> s_Pending = new List<LogicTeleportCommand>();
    private static readonly List<LogicTeleportCommand> s_Due = new List<LogicTeleportCommand>();
    private static ulong s_LastSequence;
    private static ulong s_AppliedHistoryHash;
    private static int s_AppliedCount;

    public static bool IsActive { get; private set; }
    public static bool IsApplyingFrame { get; private set; }
    public static ulong LastAppliedFrame { get; private set; }
    public static int PendingCount => s_Pending.Count;
    public static int AppliedCount => s_AppliedCount;
    public static IReadOnlyList<LogicTeleportCommand> History => s_ReadOnlyHistory;
    public static event Action<LogicTeleportCommand> CommandRecorded;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicTeleportCommandService.BeginTimeline failed: service is already active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicTeleportCommandService.BeginTimeline failed: logic time control is not active.");

        IsActive = true;
        ClearState();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicTeleportCommandService.EndTimeline failed: a command frame is being applied.");

        IsActive = false;
        ClearState();
    }

    public static LogicTeleportCommand ScheduleForNextFrame(
        LogicEntityId entityId,
        FixVector2 destination,
        string strongholdId)
    {
        EnsureActive();
        ValidateCommand(entityId, strongholdId);

        var command = new LogicTeleportCommand(
            checked(LogicTimeControlService.CurrentFrame + 1),
            checked(s_LastSequence + 1),
            entityId,
            destination,
            strongholdId);
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
    public static void ApplyFrameForTests(ulong frameId, Action<LogicTeleportCommand> sink)
    {
        ApplyFrame(frameId, sink);
    }
#endif

    private static void ApplyFrame(ulong frameId, Action<LogicTeleportCommand> sink)
    {
        EnsureActive();
        if (sink == null)
            throw new ArgumentNullException(nameof(sink));
        if (frameId != LogicTimeControlService.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicTeleportCommandService.ApplyFrame failed: time-control frame mismatch. time={LogicTimeControlService.CurrentFrame}, requested={frameId}.");
        }
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicTeleportCommandService.ApplyFrame failed: nested command frame detected.");

        IsApplyingFrame = true;
        try
        {
            s_Due.Clear();
            for (int i = 0; i < s_Pending.Count; i++)
            {
                LogicTeleportCommand command = s_Pending[i];
                if (command.EffectiveFrame < frameId)
                {
                    throw new InvalidOperationException(
                        $"LogicTeleportCommandService.ApplyFrame failed: command missed its frame. sequence={command.Sequence}, effective={command.EffectiveFrame}, current={frameId}.");
                }
                if (command.EffectiveFrame == frameId)
                    s_Due.Add(command);
            }

            s_Due.Sort(s_CommandComparison);
            for (int i = 0; i < s_Due.Count; i++)
            {
                LogicTeleportCommand command = s_Due[i];
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
            throw new InvalidOperationException("LogicTeleportCommandService.ResetForWorldTransition failed: logic time is not paused.");
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicTeleportCommandService.ResetForWorldTransition failed: a command frame is being applied.");
        ClearState();
    }

    public static void ResetFrameTimeline()
    {
        EnsureActive();
        if (LogicTimeControlService.CurrentFrame != 0)
            throw new InvalidOperationException("LogicTeleportCommandService.ResetFrameTimeline failed: time-control frame is not zero.");
        if (s_Pending.Count != 0)
            throw new InvalidOperationException($"LogicTeleportCommandService.ResetFrameTimeline failed: {s_Pending.Count} pending commands remain.");
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

    private static void ApplyRuntimeCommand(LogicTeleportCommand command)
    {
        GamePhase phase = LogicPhaseCommandService.GetRequiredCurrentPhase();
        if (!InGameDataModel.IsBuildPhase(phase))
            throw new InvalidOperationException($"Teleport command requires a build phase. phase={phase}.");
        if (LogicStrongholdMap.GetOwnerFactionIdRequired(command.StrongholdId) != EntitySideHelper.PlayerFactionId)
            throw new InvalidOperationException($"Teleport command destination stronghold is not player-owned. id={command.StrongholdId}.");
        if (!LogicStrongholdMap.TryResolveStrongholdId(command.Destination, out string destinationStrongholdId)
            || !string.Equals(destinationStrongholdId, command.StrongholdId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Teleport command destination stronghold mismatch. command={command.StrongholdId}, actual={destinationStrongholdId ?? "<none>"}, raw=({command.Destination.x.RawValue},{command.Destination.y.RawValue}).");
        }
        if (!EntityRegistry.TryGet(command.EntityId, out IEntityContext entity) || !entity.Alive)
            throw new InvalidOperationException($"Teleport command entity is unavailable. entity={command.EntityId.Value}.");
        LogicEntityState state = entity as LogicEntityState
            ?? throw new InvalidOperationException($"Teleport command entity is not a LogicEntityState. entity={command.EntityId.Value}.");
        if (!state.IsPlayerEntity || !state.IsHeroEntity)
            throw new InvalidOperationException($"Teleport command entity is not the player hero. entity={command.EntityId.Value}.");

        Fix64 clearance = state.CombatShape.Radius;
        if (!FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPointFixed(
                command.Destination,
                state.NavigationAgentTypeId,
                Fix64.Zero,
                clearance,
                out FixVector2 legalDestination)
            || legalDestination != command.Destination)
        {
            throw new InvalidOperationException(
                $"Teleport command destination is not an exact legal navigation point. entity={command.EntityId.Value}, raw=({command.Destination.x.RawValue},{command.Destination.y.RawValue}).");
        }

        state.TeleportTo(command.Destination);
    }

    private static void ValidateCommand(LogicEntityId entityId, string strongholdId)
    {
        if (!entityId.IsValid)
            throw new ArgumentException("Teleport command requires a valid entity id.", nameof(entityId));
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new ArgumentException("Teleport command requires a stronghold id.", nameof(strongholdId));
    }

    private static int CompareCommands(LogicTeleportCommand left, LogicTeleportCommand right)
    {
        int frame = left.EffectiveFrame.CompareTo(right.EffectiveFrame);
        return frame != 0 ? frame : left.Sequence.CompareTo(right.Sequence);
    }

    private static void RecordApplied(LogicTeleportCommand command)
    {
        var hasher = new LogicStateHasher();
        hasher.Add(s_AppliedHistoryHash);
        AddCommand(hasher, command);
        s_AppliedHistoryHash = hasher.Hash;
        s_AppliedCount = checked(s_AppliedCount + 1);
    }

    private static void AddCommand(LogicStateHasher hasher, LogicTeleportCommand command)
    {
        hasher.Add(command.EffectiveFrame);
        hasher.Add(command.Sequence);
        hasher.Add(command.EntityId.Value);
        hasher.Add(command.Destination.x.RawValue);
        hasher.Add(command.Destination.y.RawValue);
        hasher.Add(command.StrongholdId);
    }

    private static void ClearState()
    {
        s_LastSequence = 0;
        s_AppliedHistoryHash = 0;
        s_AppliedCount = 0;
        LastAppliedFrame = 0;
        s_History.Clear();
        s_Pending.Clear();
        s_Due.Clear();
        IsApplyingFrame = false;
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicTeleportCommandService is not active.");
    }
}
