using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public enum LogicEntityLifecycleCommandKind
{
    SpawnRequested = 0,
    ViewBound = 1,
    ViewUnbound = 2,
    DespawnRequested = 3,
}

public readonly struct LogicEntityLifecycleCommand
{
    public LogicEntityLifecycleCommand(
        ulong effectiveFrame,
        ulong sequence,
        LogicEntityLifecycleCommandKind kind,
        LogicEntityId entityId,
        int viewEntityId)
    {
        EffectiveFrame = effectiveFrame;
        Sequence = sequence;
        Kind = kind;
        EntityId = entityId;
        ViewEntityId = viewEntityId;
    }

    public ulong EffectiveFrame { get; }
    public ulong Sequence { get; }
    public LogicEntityLifecycleCommandKind Kind { get; }
    public LogicEntityId EntityId { get; }
    public int ViewEntityId { get; }
}

public static class LogicEntityLifecycleService
{
    private static readonly List<LogicEntityLifecycleCommand> s_Commands = new List<LogicEntityLifecycleCommand>();
    private static readonly ReadOnlyCollection<LogicEntityLifecycleCommand> s_ReadOnlyCommands = s_Commands.AsReadOnly();
    private static readonly HashSet<int> s_RequestedEntityIds = new HashSet<int>();
    private static readonly Dictionary<int, int> s_BoundViewIdsByEntityId = new Dictionary<int, int>();
    private static readonly Dictionary<int, MAEntity> s_BoundViewsByEntityId = new Dictionary<int, MAEntity>();
    private static readonly Dictionary<int, ulong> s_SpawnFramesByEntityId = new Dictionary<int, ulong>();
    private static readonly Dictionary<int, ulong> s_DespawnFramesByEntityId = new Dictionary<int, ulong>();
    private static readonly HashSet<int> s_ActivatedEntityIds = new HashSet<int>();
    private static readonly List<int> s_DueDespawnEntityIds = new List<int>();
    private static readonly List<int> s_DueSpawnEntityIds = new List<int>();
    private static ulong s_LastSequence;

    public static bool IsActive { get; private set; }
    public static bool IsApplyingFrame { get; private set; }
    public static ulong LastSequence => s_LastSequence;
    public static int RequestedEntityCount => s_RequestedEntityIds.Count;
    public static int BoundViewCount => s_BoundViewIdsByEntityId.Count;
    public static int ActiveEntityCount => s_ActivatedEntityIds.Count;
    public static IReadOnlyList<LogicEntityLifecycleCommand> Commands => s_ReadOnlyCommands;
    public static event Action<LogicEntityLifecycleCommand> CommandRecorded;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicEntityLifecycleService.BeginTimeline failed: service is already active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicEntityLifecycleService.BeginTimeline failed: logic time control is not active.");

        LogicEntityIdAllocator.BeginTimeline();
        LogicPersistentIdAllocator.BeginTimeline();
        IsActive = true;
        IsApplyingFrame = false;
        s_LastSequence = 0;
        s_Commands.Clear();
        s_RequestedEntityIds.Clear();
        s_BoundViewIdsByEntityId.Clear();
        s_BoundViewsByEntityId.Clear();
        s_SpawnFramesByEntityId.Clear();
        s_DespawnFramesByEntityId.Clear();
        s_ActivatedEntityIds.Clear();
        s_DueDespawnEntityIds.Clear();
        s_DueSpawnEntityIds.Clear();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        if (s_BoundViewIdsByEntityId.Count > 0)
            throw new InvalidOperationException($"LogicEntityLifecycleService.EndTimeline failed: {s_BoundViewIdsByEntityId.Count} entity views are still bound.");

        IsActive = false;
        IsApplyingFrame = false;
        s_Commands.Clear();
        s_RequestedEntityIds.Clear();
        s_BoundViewIdsByEntityId.Clear();
        s_BoundViewsByEntityId.Clear();
        s_SpawnFramesByEntityId.Clear();
        s_DespawnFramesByEntityId.Clear();
        s_ActivatedEntityIds.Clear();
        s_DueDespawnEntityIds.Clear();
        s_DueSpawnEntityIds.Clear();
        s_LastSequence = 0;
        LogicEntityIdAllocator.EndTimeline();
        LogicPersistentIdAllocator.EndTimeline();
    }

    public static LogicEntityId RequestSpawn()
    {
        EnsureActive();
        LogicEntityId entityId = LogicEntityIdAllocator.Allocate();
        if (!s_RequestedEntityIds.Add(entityId.Value))
            throw new InvalidOperationException($"LogicEntityLifecycleService.RequestSpawn failed: duplicate entity id {entityId.Value}.");

        ulong effectiveFrame = checked(LogicTimeControlService.CurrentFrame + 1);
        s_SpawnFramesByEntityId.Add(entityId.Value, effectiveFrame);
        Record(LogicEntityLifecycleCommandKind.SpawnRequested, entityId, 0, effectiveFrame);
        return entityId;
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        if (!LogicTimeControlService.IsPaused)
            throw new InvalidOperationException("LogicEntityLifecycleService.ResetForWorldTransition failed: logic time is not paused.");
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicEntityLifecycleService.ResetForWorldTransition failed: a lifecycle frame is being applied.");
        if (s_BoundViewIdsByEntityId.Count > 0)
            throw new InvalidOperationException($"LogicEntityLifecycleService.ResetForWorldTransition failed: {s_BoundViewIdsByEntityId.Count} entity views are still bound.");
        if (s_ActivatedEntityIds.Count > 0)
            throw new InvalidOperationException($"LogicEntityLifecycleService.ResetForWorldTransition failed: {s_ActivatedEntityIds.Count} logic entities are still active.");

        s_Commands.Clear();
        s_RequestedEntityIds.Clear();
        s_BoundViewIdsByEntityId.Clear();
        s_BoundViewsByEntityId.Clear();
        s_SpawnFramesByEntityId.Clear();
        s_DespawnFramesByEntityId.Clear();
        s_ActivatedEntityIds.Clear();
        s_DueDespawnEntityIds.Clear();
        s_DueSpawnEntityIds.Clear();
        s_LastSequence = 0;

        LogicEntityIdAllocator.EndTimeline();
        LogicPersistentIdAllocator.EndTimeline();
        LogicEntityIdAllocator.BeginTimeline();
        LogicPersistentIdAllocator.BeginTimeline();
    }

    public static void BindView(LogicEntityId entityId, int viewEntityId)
    {
        BindView(entityId, viewEntityId, null);
    }

    public static void BindView(LogicEntityId entityId, int viewEntityId, MAEntity view)
    {
        EnsureKnownEntity(entityId, nameof(BindView));
        if (viewEntityId <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewEntityId), viewEntityId, "View entity id must be positive.");

        ulong spawnFrame = s_SpawnFramesByEntityId[entityId.Value];
        if (LogicTimeControlService.CurrentFrame >= spawnFrame)
        {
            throw new InvalidOperationException(
                $"LogicEntityLifecycleService.BindView failed: view arrived after the spawn frame. entity={entityId.Value}, spawnFrame={spawnFrame}, currentFrame={LogicTimeControlService.CurrentFrame}.");
        }

        if (!s_BoundViewIdsByEntityId.TryAdd(entityId.Value, viewEntityId))
            throw new InvalidOperationException($"LogicEntityLifecycleService.BindView failed: entity {entityId.Value} is already bound.");
        if (view != null)
            s_BoundViewsByEntityId.Add(entityId.Value, view);

        Record(LogicEntityLifecycleCommandKind.ViewBound, entityId, viewEntityId);
    }

    public static void RequestDespawn(MAEntity view)
    {
        if (view == null)
            throw new ArgumentNullException(nameof(view));

        LogicEntityId entityId = view.LogicEntityId;
        EnsureKnownEntity(entityId, nameof(RequestDespawn));
        if (!s_BoundViewsByEntityId.TryGetValue(entityId.Value, out MAEntity boundView) || boundView != view)
        {
            throw new InvalidOperationException(
                $"LogicEntityLifecycleService.RequestDespawn failed: bound view mismatch. entity={entityId.Value}.");
        }
        if (!s_ActivatedEntityIds.Contains(entityId.Value))
        {
            throw new InvalidOperationException(
                $"LogicEntityLifecycleService.RequestDespawn failed: entity {entityId.Value} is not active.");
        }
        if (s_DespawnFramesByEntityId.ContainsKey(entityId.Value))
        {
            throw new InvalidOperationException(
                $"LogicEntityLifecycleService.RequestDespawn failed: entity {entityId.Value} already has a pending despawn command.");
        }

        ulong effectiveFrame = checked(LogicTimeControlService.CurrentFrame + 1);
        s_DespawnFramesByEntityId.Add(entityId.Value, effectiveFrame);
        Record(LogicEntityLifecycleCommandKind.DespawnRequested, entityId, view.Id, effectiveFrame);
    }

    public static void UnbindView(LogicEntityId entityId, int viewEntityId)
    {
        EnsureKnownEntity(entityId, nameof(UnbindView));
        if (!s_BoundViewIdsByEntityId.TryGetValue(entityId.Value, out int boundViewEntityId))
            throw new InvalidOperationException($"LogicEntityLifecycleService.UnbindView failed: entity {entityId.Value} is not bound.");
        if (boundViewEntityId != viewEntityId)
            throw new InvalidOperationException($"LogicEntityLifecycleService.UnbindView failed: view mismatch for entity {entityId.Value}. expected={boundViewEntityId}, actual={viewEntityId}.");

        s_BoundViewIdsByEntityId.Remove(entityId.Value);
        s_BoundViewsByEntityId.Remove(entityId.Value);
        s_ActivatedEntityIds.Remove(entityId.Value);
        s_SpawnFramesByEntityId.Remove(entityId.Value);
        s_DespawnFramesByEntityId.Remove(entityId.Value);
        s_RequestedEntityIds.Remove(entityId.Value);
        Record(
            LogicEntityLifecycleCommandKind.ViewUnbound,
            entityId,
            viewEntityId,
            LogicTimeControlService.CurrentFrame);
    }

    public static void ApplyFrame(ulong frameId)
    {
        EnsureActive();
        if (frameId != LogicTimeControlService.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicEntityLifecycleService.ApplyFrame failed: time-control frame mismatch. time={LogicTimeControlService.CurrentFrame}, requested={frameId}.");
        }

        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicEntityLifecycleService.ApplyFrame failed: nested lifecycle frame detected.");

        IsApplyingFrame = true;
        try
        {
            ApplyFrameCore(frameId);
        }
        finally
        {
            IsApplyingFrame = false;
        }
    }

    private static void ApplyFrameCore(ulong frameId)
    {

        s_DueDespawnEntityIds.Clear();
        foreach (KeyValuePair<int, ulong> pair in s_DespawnFramesByEntityId)
        {
            if (pair.Value < frameId)
            {
                throw new InvalidOperationException(
                    $"LogicEntityLifecycleService.ApplyFrame failed: despawn command missed its frame. entity={pair.Key}, effective={pair.Value}, current={frameId}.");
            }
            if (pair.Value == frameId)
                s_DueDespawnEntityIds.Add(pair.Key);
        }

        s_DueDespawnEntityIds.Sort();
        for (int i = 0; i < s_DueDespawnEntityIds.Count; i++)
        {
            int entityId = s_DueDespawnEntityIds[i];
            if (!s_ActivatedEntityIds.Contains(entityId))
            {
                throw new InvalidOperationException(
                    $"LogicEntityLifecycleService.ApplyFrame failed: despawn entity is not active. entity={entityId}, effective={frameId}.");
            }
            if (!s_BoundViewsByEntityId.TryGetValue(entityId, out MAEntity view) || view == null)
            {
                throw new InvalidOperationException(
                    $"LogicEntityLifecycleService.ApplyFrame failed: despawn view is not ready. entity={entityId}, effective={frameId}.");
            }
        }

        for (int i = 0; i < s_DueDespawnEntityIds.Count; i++)
        {
            int entityId = s_DueDespawnEntityIds[i];
            MAEntity view = s_BoundViewsByEntityId[entityId];
            view.DeactivateLogicParticipation();
            if (!s_ActivatedEntityIds.Remove(entityId))
            {
                throw new InvalidOperationException(
                    $"LogicEntityLifecycleService.ApplyFrame failed: despawn activation state changed unexpectedly. entity={entityId}.");
            }
            if (GF.Entity == null)
                throw new InvalidOperationException("LogicEntityLifecycleService.ApplyFrame failed: GF.Entity is null while applying despawn commands.");
            if (view.Entity == null)
                throw new InvalidOperationException($"LogicEntityLifecycleService.ApplyFrame failed: despawn view has no framework entity. entity={entityId}.");

            GF.Entity.HideEntity(view.Entity);
            if (s_BoundViewIdsByEntityId.ContainsKey(entityId))
            {
                throw new InvalidOperationException(
                    $"LogicEntityLifecycleService.ApplyFrame failed: hiding the despawn view did not unbind it. entity={entityId}.");
            }
        }

        s_DueSpawnEntityIds.Clear();
        foreach (KeyValuePair<int, ulong> pair in s_SpawnFramesByEntityId)
        {
            if (s_ActivatedEntityIds.Contains(pair.Key))
                continue;
            if (pair.Value < frameId)
            {
                throw new InvalidOperationException(
                    $"LogicEntityLifecycleService.ApplyFrame failed: spawn command missed its frame. entity={pair.Key}, effective={pair.Value}, current={frameId}.");
            }
            if (pair.Value == frameId)
                s_DueSpawnEntityIds.Add(pair.Key);
        }

        s_DueSpawnEntityIds.Sort();
        for (int i = 0; i < s_DueSpawnEntityIds.Count; i++)
        {
            int entityId = s_DueSpawnEntityIds[i];
            if (!s_BoundViewsByEntityId.TryGetValue(entityId, out MAEntity view) || view == null)
            {
                throw new InvalidOperationException(
                    $"LogicEntityLifecycleService.ApplyFrame failed: spawn view is not ready. entity={entityId}, effective={frameId}.");
            }
        }

        for (int i = 0; i < s_DueSpawnEntityIds.Count; i++)
        {
            int entityId = s_DueSpawnEntityIds[i];
            MAEntity view = s_BoundViewsByEntityId[entityId];
            view.ActivateLogicParticipation(frameId);
            s_ActivatedEntityIds.Add(entityId);
        }
    }

    public static void ResetFrameTimelinePreservingEntities()
    {
        EnsureActive();

        var pendingEntityIds = new List<int>();
        foreach (KeyValuePair<int, ulong> pair in s_SpawnFramesByEntityId)
        {
            if (!s_ActivatedEntityIds.Contains(pair.Key))
                pendingEntityIds.Add(pair.Key);
        }

        pendingEntityIds.Sort();
        for (int i = 0; i < pendingEntityIds.Count; i++)
        {
            int entityId = pendingEntityIds[i];
            s_SpawnFramesByEntityId[entityId] = 1;
            for (int commandIndex = 0; commandIndex < s_Commands.Count; commandIndex++)
            {
                LogicEntityLifecycleCommand command = s_Commands[commandIndex];
                if (command.Kind != LogicEntityLifecycleCommandKind.SpawnRequested || command.EntityId.Value != entityId)
                    continue;

                s_Commands[commandIndex] = new LogicEntityLifecycleCommand(
                    1,
                    command.Sequence,
                    command.Kind,
                    command.EntityId,
                    command.ViewEntityId);
                break;
            }
        }

        var pendingDespawnEntityIds = new List<int>(s_DespawnFramesByEntityId.Keys);
        pendingDespawnEntityIds.Sort();
        for (int i = 0; i < pendingDespawnEntityIds.Count; i++)
        {
            int entityId = pendingDespawnEntityIds[i];
            s_DespawnFramesByEntityId[entityId] = 1;
            for (int commandIndex = 0; commandIndex < s_Commands.Count; commandIndex++)
            {
                LogicEntityLifecycleCommand command = s_Commands[commandIndex];
                if (command.Kind != LogicEntityLifecycleCommandKind.DespawnRequested || command.EntityId.Value != entityId)
                    continue;

                s_Commands[commandIndex] = new LogicEntityLifecycleCommand(
                    1,
                    command.Sequence,
                    command.Kind,
                    command.EntityId,
                    command.ViewEntityId);
                break;
            }
        }
    }

    public static void DeactivateAllForShutdown()
    {
        EnsureActive();
        var entityIds = new List<int>(s_ActivatedEntityIds);
        entityIds.Sort();
        for (int i = entityIds.Count - 1; i >= 0; i--)
        {
            if (s_BoundViewsByEntityId.TryGetValue(entityIds[i], out MAEntity view) && view != null)
                view.DeactivateLogicParticipation(true);
        }
        s_ActivatedEntityIds.Clear();
    }

    private static void Record(LogicEntityLifecycleCommandKind kind, LogicEntityId entityId, int viewEntityId, ulong effectiveFrame = 0)
    {
        ulong sequence = checked(s_LastSequence + 1);
        var command = new LogicEntityLifecycleCommand(
            effectiveFrame > 0 ? effectiveFrame : checked(LogicTimeControlService.CurrentFrame + 1),
            sequence,
            kind,
            entityId,
            viewEntityId);
        s_LastSequence = sequence;
        s_Commands.Add(command);
        CommandRecorded?.Invoke(command);
    }

    private static void EnsureKnownEntity(LogicEntityId entityId, string caller)
    {
        EnsureActive();
        if (!entityId.IsValid)
            throw new ArgumentException($"LogicEntityLifecycleService.{caller} failed: entity id is invalid.", nameof(entityId));
        if (!s_RequestedEntityIds.Contains(entityId.Value))
            throw new InvalidOperationException($"LogicEntityLifecycleService.{caller} failed: entity {entityId.Value} was not requested in this timeline.");
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicEntityLifecycleService operation failed: service is not active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicEntityLifecycleService operation failed: logic time control is not active.");
    }
}
