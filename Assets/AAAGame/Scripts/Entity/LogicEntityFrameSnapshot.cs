using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public readonly struct LogicEntityFrameState
{
    public LogicEntityFrameState(
        LogicEntityId entityId,
        FixVector2 position,
        FixVector2 forward,
        Fix64 collisionRadius,
        LogicCombatShape combatShape,
        SideType side,
        bool alive)
    {
        EntityId = entityId;
        Position = position;
        Forward = forward;
        CollisionRadius = collisionRadius;
        CombatShape = combatShape;
        Side = side;
        Alive = alive;
    }

    public LogicEntityId EntityId { get; }
    public FixVector2 Position { get; }
    public FixVector2 Forward { get; }
    public Fix64 CollisionRadius { get; }
    public LogicCombatShape CombatShape { get; }
    public SideType Side { get; }
    public bool Alive { get; }
}

public sealed class LogicEntityFrameSnapshot
{
    private readonly List<LogicEntityFrameState> m_States;
    private readonly ReadOnlyCollection<LogicEntityFrameState> m_ReadOnlyStates;
    private readonly Dictionary<int, int> m_StateIndexByEntityId;

    internal LogicEntityFrameSnapshot(ulong frameId, List<LogicEntityFrameState> states)
    {
        FrameId = frameId;
        m_States = states ?? throw new ArgumentNullException(nameof(states));
        m_ReadOnlyStates = m_States.AsReadOnly();
        m_StateIndexByEntityId = new Dictionary<int, int>(m_States.Count);
        for (int i = 0; i < m_States.Count; i++)
            m_StateIndexByEntityId.Add(m_States[i].EntityId.Value, i);
    }

    public ulong FrameId { get; }
    public IReadOnlyList<LogicEntityFrameState> States => m_ReadOnlyStates;

    public bool TryGet(LogicEntityId entityId, out LogicEntityFrameState state)
    {
        if (!entityId.IsValid)
            throw new ArgumentException("Frame snapshot lookup requires a valid logic entity id.", nameof(entityId));

        if (m_StateIndexByEntityId.TryGetValue(entityId.Value, out int index))
        {
            state = m_States[index];
            return true;
        }

        state = default;
        return false;
    }

    public LogicEntityFrameState GetRequired(LogicEntityId entityId)
    {
        if (!TryGet(entityId, out LogicEntityFrameState state))
            throw new InvalidOperationException($"LogicEntityFrameSnapshot does not contain entity {entityId.Value} at frame {FrameId}.");
        return state;
    }

    public LogicEntityFrameState GetRequired(IEntityContext entity)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));
        return GetRequired(entity.LogicEntityId);
    }
}

public static class LogicEntityFrameSnapshotBuilder
{
    public static LogicEntityFrameSnapshot Build(ulong frameId, IList<IEntityContext> entities)
    {
        if (frameId == 0)
            throw new ArgumentOutOfRangeException(nameof(frameId), "Frame snapshot id must be positive.");
        if (entities == null)
            throw new ArgumentNullException(nameof(entities));

        var states = new List<LogicEntityFrameState>(entities.Count);
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity == null)
                throw new InvalidOperationException($"LogicEntityFrameSnapshotBuilder.Build failed: entity at index {i} is null.");
            if (!entity.LogicEntityId.IsValid)
                throw new InvalidOperationException($"LogicEntityFrameSnapshotBuilder.Build failed: entity at index {i} has an invalid logic id.");

            FixVector2 position = entity.PositionFixed;

            Fix64 collisionRadius = DistanceUnitConverter.ConvertToWorld(
                entity.GetProperty(CreatureMainProperty.CollisionRadius));
            if (collisionRadius < Fix64.Zero)
            {
                throw new InvalidOperationException(
                    $"LogicEntityFrameSnapshotBuilder.Build failed: entity {entity.LogicEntityId.Value} has a negative collision radius.");
            }

            FixVector2 forward = entity.ForwardFixed;
            if (FixVector2.SqrMagnitude(forward) == Fix64.Zero)
            {
                throw new InvalidOperationException(
                    $"LogicEntityFrameSnapshotBuilder.Build failed: entity {entity.LogicEntityId.Value} has a zero forward vector.");
            }

            LogicCombatShape combatShape = entity.CombatShape;

            states.Add(new LogicEntityFrameState(
                entity.LogicEntityId,
                position,
                forward,
                collisionRadius,
                combatShape,
                entity.Side,
                entity.Alive));
        }

        states.Sort((left, right) => left.EntityId.CompareTo(right.EntityId));
        for (int i = 1; i < states.Count; i++)
        {
            if (states[i - 1].EntityId == states[i].EntityId)
            {
                throw new InvalidOperationException(
                    $"LogicEntityFrameSnapshotBuilder.Build failed: duplicate logic entity id {states[i].EntityId.Value}.");
            }
        }

        return new LogicEntityFrameSnapshot(frameId, states);
    }

}

public static class LogicEntityFrameSnapshotService
{
    private sealed class SnapshotCaptureListener : ILogicFrameUpdate
    {
        public int LogicFrameOrder => int.MinValue;

        public void OnLogicFrameUpdate(Fix64 deltaTime)
        {
            if (deltaTime != LogicFrameRuntime.FixedDeltaTime)
                throw new InvalidOperationException("LogicEntityFrameSnapshotService received a non-fixed logic delta.");
            if (LogicFrameRuntime.CurrentFrame == 0)
                throw new InvalidOperationException("LogicEntityFrameSnapshotService cannot capture frame zero.");

            Current = LogicEntityFrameSnapshotBuilder.Build(
                LogicFrameRuntime.CurrentFrame,
                EntityRegistry.AllEntities);
        }
    }

    private static readonly SnapshotCaptureListener s_Listener = new SnapshotCaptureListener();

    public static bool IsActive { get; private set; }
    public static LogicEntityFrameSnapshot Current { get; private set; }
    public static ulong CapturedFrame => Current?.FrameId ?? 0;
    public static int CapturedEntityCount => Current?.States.Count ?? 0;

    public static LogicEntityFrameState GetRequiredCurrent(IEntityContext entity)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));
        EnsureCurrentFrame();
        return Current.GetRequired(entity);
    }

    public static FixVector2 GetRequiredPosition(IEntityContext entity)
    {
        return GetRequiredCurrent(entity).Position;
    }

    public static FixVector2 GetRequiredForward(IEntityContext entity)
    {
        return GetRequiredCurrent(entity).Forward;
    }

    public static Fix64 GetRequiredCenterDistance(IEntityContext first, IEntityContext second)
    {
        LogicEntityFrameState firstState = GetRequiredCurrent(first);
        LogicEntityFrameState secondState = GetRequiredCurrent(second);
        return FixVector2.Distance(firstState.Position, secondState.Position);
    }

    public static Fix64 GetRequiredTargetSurfaceDistance(IEntityContext self, IEntityContext target)
    {
        LogicEntityFrameState selfState = GetRequiredCurrent(self);
        LogicEntityFrameState targetState = GetRequiredCurrent(target);
        return targetState.CombatShape.DistanceToSurface(selfState.Position);
    }

    public static FixVector2 GetRequiredTargetClosestPoint(IEntityContext self, IEntityContext target)
    {
        LogicEntityFrameState selfState = GetRequiredCurrent(self);
        LogicEntityFrameState targetState = GetRequiredCurrent(target);
        return targetState.CombatShape.ClosestPoint(selfState.Position);
    }

    public static Fix64 GetRequiredTargetSurfaceDistanceFromPoint(IEntityContext target, FixVector2 point)
    {
        return GetRequiredCurrent(target).CombatShape.DistanceToSurface(point);
    }

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicEntityFrameSnapshotService.BeginTimeline failed: service is already active.");
        if (!LogicFrameRuntime.IsActive)
            throw new InvalidOperationException("LogicEntityFrameSnapshotService.BeginTimeline failed: logic frame runtime is not active.");

        Current = null;
        LogicFrameRuntime.Register(s_Listener);
        IsActive = true;
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        if (LogicFrameRuntime.IsTicking)
            throw new InvalidOperationException("LogicEntityFrameSnapshotService.ResetForWorldTransition failed: a logic frame is running.");
        Current = null;
    }

    public static void EndTimeline()
    {
        EnsureActive();
        if (LogicFrameRuntime.IsTicking)
            throw new InvalidOperationException("LogicEntityFrameSnapshotService.EndTimeline failed: a logic frame is running.");

        LogicFrameRuntime.Unregister(s_Listener);
        Current = null;
        IsActive = false;
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicEntityFrameSnapshotService operation failed: service is not active.");
        if (!LogicFrameRuntime.IsActive)
            throw new InvalidOperationException("LogicEntityFrameSnapshotService operation failed: logic frame runtime is not active.");
    }

    private static void EnsureCurrentFrame()
    {
        EnsureActive();
        if (!LogicFrameRuntime.IsTicking)
            throw new InvalidOperationException("LogicEntityFrameSnapshotService read failed: no logic frame is running.");
        if (Current == null || Current.FrameId != LogicFrameRuntime.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicEntityFrameSnapshotService read failed: current frame mismatch. logicFrame={LogicFrameRuntime.CurrentFrame}, snapshot={Current?.FrameId ?? 0}.");
        }
    }
}

public static class LogicEntityFrameReadExtensions
{
    public static FixVector2 LogicFramePositionFixed(this IEntityContext entity)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));
        if (!LogicFrameRuntime.IsTicking)
            return entity.PositionFixed;

        return LogicEntityFrameSnapshotService.GetRequiredPosition(entity);
    }

    public static Vector3 LogicFramePosition(this IEntityContext entity)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));
        if (!LogicFrameRuntime.IsTicking)
            return entity.Position;

        FixVector2 position = entity.LogicFramePositionFixed();
        return new Vector3((float)position.x, entity.Position.y, (float)position.y);
    }

    public static FixVector2 LogicFrameTargetClosestPointFixed(this IEntityContext self, IEntityContext target)
    {
        if (self == null)
            throw new ArgumentNullException(nameof(self));
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (LogicFrameRuntime.IsTicking)
            return LogicEntityFrameSnapshotService.GetRequiredTargetClosestPoint(self, target);

        FixVector2 origin = self.LogicFramePositionFixed();
        return target.CombatShape.ClosestPoint(origin);
    }

    public static Fix64 LogicFrameDistanceFromPointToSurfaceFixed(this IEntityContext target, FixVector2 point)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (LogicFrameRuntime.IsTicking)
            return LogicEntityFrameSnapshotService.GetRequiredTargetSurfaceDistanceFromPoint(target, point);

        return target.CombatShape.DistanceToSurface(point);
    }

    public static float LogicFrameCenterDistance(this IEntityContext self, IEntityContext target)
    {
        return (float)LogicFrameCenterDistanceFixed(self, target);
    }

    public static Fix64 LogicFrameCenterDistanceFixed(this IEntityContext self, IEntityContext target)
    {
        if (self == null || target == null)
            return Fix64.FromRaw(long.MaxValue);
        if (!LogicFrameRuntime.IsTicking)
        {
            return FixVector2.Distance(self.PositionFixed, target.PositionFixed);
        }
        return LogicEntityFrameSnapshotService.GetRequiredCenterDistance(self, target);
    }

    public static float LogicFrameDistanceToTargetSurface(this IEntityContext self, IEntityContext target)
    {
        return (float)LogicFrameDistanceToTargetSurfaceFixed(self, target);
    }

    public static Fix64 LogicFrameDistanceToTargetSurfaceFixed(this IEntityContext self, IEntityContext target)
    {
        if (self == null || target == null)
            return Fix64.FromRaw(long.MaxValue);
        if (!LogicFrameRuntime.IsTicking)
            return (Fix64)self.DistanceToTargetSurface(target);

        return LogicEntityFrameSnapshotService.GetRequiredTargetSurfaceDistance(self, target);
    }
}
