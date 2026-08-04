using System;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

public enum MAEntityLogicFramePhase
{
    BaseAndBuffs = 0,
    NavigationSync = 1,
    Brain = 2,
    Targeting = 3,
    Projectile = 4,
    Attack = 5,
    DamageResolve = 6,
    MoveIntent = 7,
    MoveResolve = 8,
    MoveCommit = 9,
    PostUpdate = 10,
    Count = 11,
}

public interface ILogicFrameEntity : IEntityContext
{
    bool IsLogicActive { get; }
    int NavigationAgentTypeId { get; }
    bool AllowsZeroCollisionRadius { get; }
    bool HasPreparedLogicMove { get; }
    ulong PreparedLogicFrame { get; }
    bool PreparedCollisionMovable { get; }
    bool PreparedNavigationConstraintEnabled { get; }
    bool PreparedPreserveSpeedOnStaticSlide { get; }
    uint AgentCollisionMask { get; }
    FixVector2 PreparedResolvedHorizontalDisplacement { get; }
    void BeginLogicFrame(Fix64 deltaTime);
    void ExecuteLogicFramePhase(MAEntityLogicFramePhase phase, Fix64 deltaTime);
    void CompleteLogicFrame(Fix64 deltaTime);
}

public static class MAEntityLogicFrameSystem
{
    private sealed class PhaseListener : ILogicFrameUpdate, ILogicFrameStableOrder
    {
        public int LogicFrameOrder => 0;
        public long LogicFrameStableKey => long.MinValue;

        public void OnLogicFrameUpdate(Fix64 deltaTime)
        {
            ExecuteFrame(deltaTime);
        }
    }

    private static readonly PhaseListener s_Listener = new PhaseListener();
    private static readonly List<ILogicFrameEntity> s_FrameEntities = new List<ILogicFrameEntity>();
    private static readonly List<MAEntity> s_CoordinatedViews = new List<MAEntity>();

    public static bool IsActive { get; private set; }
    public static ulong LastCompletedFrame { get; private set; }
    public static int LastFrameEntityCount { get; private set; }
    public static MAEntityLogicFramePhase LastCompletedPhase { get; private set; }
    public static int LastFramePhaseExecutionCount { get; private set; }

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("MAEntityLogicFrameSystem.BeginTimeline failed: system is already active.");
        if (!LogicFrameRuntime.IsActive)
            throw new InvalidOperationException("MAEntityLogicFrameSystem.BeginTimeline failed: logic frame runtime is not active.");
        if (!LogicEntityFrameSnapshotService.IsActive)
            throw new InvalidOperationException("MAEntityLogicFrameSystem.BeginTimeline failed: frame snapshot service is not active.");

        ClearFrameState();
        LogicAgentCollisionShadowService.Clear();
        LogicDamageEventService.BeginTimeline();
        LogicProjectileService.BeginTimeline();
        LogicFrameRuntime.Register(s_Listener);
        IsActive = true;
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        if (LogicFrameRuntime.IsTicking)
            throw new InvalidOperationException("MAEntityLogicFrameSystem.ResetForWorldTransition failed: a logic frame is running.");
        ClearFrameState();
        LogicAgentCollisionShadowService.Clear();
        LogicDamageEventService.ResetForWorldTransition();
        LogicProjectileService.ResetForWorldTransition();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        if (LogicFrameRuntime.IsTicking)
            throw new InvalidOperationException("MAEntityLogicFrameSystem.EndTimeline failed: a logic frame is running.");

        LogicFrameRuntime.Unregister(s_Listener);
        ClearFrameState();
        LogicAgentCollisionShadowService.Clear();
        LogicProjectileService.EndTimeline();
        LogicDamageEventService.EndTimeline();
        IsActive = false;
    }

    private static void ExecuteFrame(Fix64 deltaTime)
    {
        EnsureActive();
        ulong frame = LogicFrameRuntime.CurrentFrame;
        LogicEntityFrameSnapshot snapshot = LogicEntityFrameSnapshotService.Current;
        if (snapshot == null || snapshot.FrameId != frame)
        {
            throw new InvalidOperationException(
                $"MAEntityLogicFrameSystem.ExecuteFrame failed: frame snapshot mismatch. logicFrame={frame}, snapshot={snapshot?.FrameId ?? 0}.");
        }

        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long setupStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        CollectFrameEntities(snapshot);
        CollectCoordinatedViews();
        for (int i = 0; i < s_CoordinatedViews.Count; i++)
            s_CoordinatedViews[i].BeginCoordinatedLogicFrameUpdate();

        for (int i = 0; i < s_FrameEntities.Count; i++)
            s_FrameEntities[i].BeginLogicFrame(deltaTime);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.LogicEntityFrameSetup,
                System.Diagnostics.Stopwatch.GetTimestamp() - setupStartTicks);
        }

        int phaseExecutionCount = 0;
        LogicDamageEventService.BeginFrame(frame);
        for (int phaseValue = 0; phaseValue < (int)MAEntityLogicFramePhase.Count; phaseValue++)
        {
            MAEntityLogicFramePhase phase = (MAEntityLogicFramePhase)phaseValue;
            long phaseStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            for (int entityIndex = 0; entityIndex < s_FrameEntities.Count; entityIndex++)
            {
                s_FrameEntities[entityIndex].ExecuteLogicFramePhase(phase, deltaTime);
                phaseExecutionCount++;
            }
            if (phase == MAEntityLogicFramePhase.Projectile)
                LogicProjectileService.AdvanceFrame(frame, deltaTime);
            if (phase == MAEntityLogicFramePhase.DamageResolve)
                LogicDamageEventService.ApplyFrame(frame);
            if (phase == MAEntityLogicFramePhase.MoveResolve)
                LogicAgentCollisionShadowService.SolveFrame(frame, snapshot, s_FrameEntities);
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    ResolvePhasePerfScope(phase),
                    System.Diagnostics.Stopwatch.GetTimestamp() - phaseStartTicks);
            }
            LastCompletedPhase = phase;
        }

        long completeStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        for (int i = 0; i < s_FrameEntities.Count; i++)
            s_FrameEntities[i].CompleteLogicFrame(deltaTime);
        for (int i = 0; i < s_CoordinatedViews.Count; i++)
            s_CoordinatedViews[i].CompleteCoordinatedLogicFrameUpdate();
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.LogicEntityFrameComplete,
                System.Diagnostics.Stopwatch.GetTimestamp() - completeStartTicks);
        }

        LastFrameEntityCount = s_FrameEntities.Count;
        LastFramePhaseExecutionCount = phaseExecutionCount;
        LastCompletedFrame = frame;
    }

    private static MainThreadPerfScope ResolvePhasePerfScope(MAEntityLogicFramePhase phase)
    {
        return phase switch
        {
            MAEntityLogicFramePhase.BaseAndBuffs => MainThreadPerfScope.LogicEntityBaseAndBuffs,
            MAEntityLogicFramePhase.NavigationSync => MainThreadPerfScope.LogicEntityNavigationSync,
            MAEntityLogicFramePhase.Brain => MainThreadPerfScope.LogicEntityBrain,
            MAEntityLogicFramePhase.Targeting => MainThreadPerfScope.LogicEntityTargeting,
            MAEntityLogicFramePhase.Projectile => MainThreadPerfScope.LogicEntityProjectile,
            MAEntityLogicFramePhase.Attack => MainThreadPerfScope.LogicEntityAttack,
            MAEntityLogicFramePhase.DamageResolve => MainThreadPerfScope.LogicEntityDamageResolve,
            MAEntityLogicFramePhase.MoveIntent => MainThreadPerfScope.LogicEntityMoveIntent,
            MAEntityLogicFramePhase.MoveResolve => MainThreadPerfScope.LogicEntityMoveResolve,
            MAEntityLogicFramePhase.MoveCommit => MainThreadPerfScope.LogicEntityMoveCommit,
            MAEntityLogicFramePhase.PostUpdate => MainThreadPerfScope.LogicEntityPostUpdate,
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown logic entity phase."),
        };
    }

    private static void CollectFrameEntities(LogicEntityFrameSnapshot snapshot)
    {
        s_FrameEntities.Clear();
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        if (snapshot.States.Count != entities.Count)
        {
            throw new InvalidOperationException(
                $"MAEntityLogicFrameSystem.CollectFrameEntities failed: snapshot/registry count mismatch. snapshot={snapshot.States.Count}, registry={entities.Count}.");
        }

        int previousEntityId = 0;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!(entities[i] is ILogicFrameEntity entity))
            {
                throw new InvalidOperationException(
                    $"MAEntityLogicFrameSystem.CollectFrameEntities failed: registry entity {entities[i].LogicEntityId.Value} does not implement ILogicFrameEntity.");
            }
            if (!entity.IsLogicActive)
            {
                throw new InvalidOperationException(
                    $"MAEntityLogicFrameSystem.CollectFrameEntities failed: entity {entity.LogicEntityId.Value} is not logic-active.");
            }
            if (entity.LogicEntityId.Value <= previousEntityId)
            {
                throw new InvalidOperationException(
                    $"MAEntityLogicFrameSystem.CollectFrameEntities failed: registry order is not strictly increasing at entity {entity.LogicEntityId.Value}.");
            }

            LogicEntityFrameState frameState = snapshot.States[i];
            if (frameState.EntityId != entity.LogicEntityId)
            {
                throw new InvalidOperationException(
                    $"MAEntityLogicFrameSystem.CollectFrameEntities failed: snapshot/registry identity mismatch at index {i}. " +
                    $"snapshot={frameState.EntityId.Value}, registry={entity.LogicEntityId.Value}.");
            }

            previousEntityId = entity.LogicEntityId.Value;
            s_FrameEntities.Add(entity);
        }
    }

    private static void CollectCoordinatedViews()
    {
        s_CoordinatedViews.Clear();
        if (!LogicEntityLifecycleService.IsActive)
            return;

        for (int i = 0; i < s_FrameEntities.Count; i++)
        {
            ILogicFrameEntity entity = s_FrameEntities[i];
            if (LogicEntityLifecycleService.TryGetBoundView(entity.LogicEntityId, out MAEntity view))
                s_CoordinatedViews.Add(view);
        }
    }

    private static void ClearFrameState()
    {
        s_FrameEntities.Clear();
        s_CoordinatedViews.Clear();
        LastCompletedFrame = 0;
        LastFrameEntityCount = 0;
        LastCompletedPhase = default;
        LastFramePhaseExecutionCount = 0;
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("MAEntityLogicFrameSystem operation failed: system is not active.");
        if (!LogicFrameRuntime.IsActive)
            throw new InvalidOperationException("MAEntityLogicFrameSystem operation failed: logic frame runtime is not active.");
    }
}
