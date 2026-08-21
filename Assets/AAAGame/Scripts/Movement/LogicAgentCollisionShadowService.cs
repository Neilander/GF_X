using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct LogicAgentCollisionShadowState
{
    public LogicAgentCollisionShadowState(
        LogicEntityId entityId,
        FixVector2 proposedPosition,
        FixVector2 pairCorrection,
        FixVector2 staticCorrection,
        FixVector2 regionCorrection,
        FixVector2 finalResolvedPosition,
        bool staticProjectionAvailable,
        bool staticProjectionSucceeded,
        FixVector2 firstHitNormal,
        LogicStaticCollisionContactKind staticContactKind,
        int staticContactCellX,
        int staticContactCellY,
        int runtimeObstacleStableId,
        LogicMovementRegionConstraintFailure regionConstraintFailure)
    {
        EntityId = entityId;
        ProposedPosition = proposedPosition;
        PairCorrection = pairCorrection;
        StaticCorrection = staticCorrection;
        RegionCorrection = regionCorrection;
        FinalResolvedPosition = finalResolvedPosition;
        StaticProjectionAvailable = staticProjectionAvailable;
        StaticProjectionSucceeded = staticProjectionSucceeded;
        FirstHitNormal = firstHitNormal;
        StaticContactKind = staticContactKind;
        StaticContactCellX = staticContactCellX;
        StaticContactCellY = staticContactCellY;
        RuntimeObstacleStableId = runtimeObstacleStableId;
        RegionConstraintFailure = regionConstraintFailure;
    }

    public LogicEntityId EntityId { get; }
    public FixVector2 ProposedPosition { get; }
    public FixVector2 PairResolvedPosition => ProposedPosition + PairCorrection;
    public FixVector2 StaticResolvedPosition => PairResolvedPosition + StaticCorrection;
    public FixVector2 PairCorrection { get; }
    public FixVector2 StaticCorrection { get; }
    public FixVector2 RegionCorrection { get; }
    public FixVector2 FinalResolvedPosition { get; }
    public bool StaticProjectionAvailable { get; }
    public bool StaticProjectionSucceeded { get; }
    public FixVector2 FirstHitNormal { get; }
    public LogicStaticCollisionContactKind StaticContactKind { get; }
    public int StaticContactCellX { get; }
    public int StaticContactCellY { get; }
    public int RuntimeObstacleStableId { get; }
    public LogicMovementRegionConstraintFailure RegionConstraintFailure { get; }
}

public static class LogicAgentCollisionShadowService
{
    private const int SolverIterationCount = 8;
    private const int PairStaticProjectionPassCount = 4;
    private static readonly Fix64 PenetrationEpsilon = Fix64.FromRaw(1);
    private static readonly List<LogicAgentCollisionBody> s_Bodies = new List<LogicAgentCollisionBody>();
    private static readonly List<ILogicFrameEntity> s_BodyEntities = new List<ILogicFrameEntity>();
    private static readonly List<FixVector2> s_BodyFrameStartPositions = new List<FixVector2>();
    private static readonly List<LogicAgentCollisionShadowState> s_States = new List<LogicAgentCollisionShadowState>();
    private static readonly List<FixVector2> s_ProposedPositions = new List<FixVector2>();
    private static readonly List<FixVector2> s_PairAdjustedDisplacements = new List<FixVector2>();
    private static readonly List<FixVector2> s_PairCorrections = new List<FixVector2>();
    private static readonly List<FixVector2> s_StaticCorrections = new List<FixVector2>();
    private static readonly List<FixVector2> s_RegionCorrections = new List<FixVector2>();
    private static readonly List<LogicMovementRegionConstraintFailure> s_RegionFailures =
        new List<LogicMovementRegionConstraintFailure>();
    private static readonly List<LogicAgentCollisionState> s_SolverStates = new List<LogicAgentCollisionState>();
    private static readonly HashSet<int> s_PairCorrectedEntityIds = new HashSet<int>();
    private static readonly HashSet<int> s_StaticProjectionChangedEntityIds = new HashSet<int>();
    private static readonly HashSet<int> s_RegionConstraintChangedEntityIds = new HashSet<int>();
    private static readonly HashSet<int> s_JointConstraintRejectedEntityIds = new HashSet<int>();
    private static readonly IReadOnlyList<LogicAgentCollisionShadowState> s_ReadOnlyStates = s_States.AsReadOnly();
    private static readonly Dictionary<int, FixVector2> s_ResolvedPositions = new Dictionary<int, FixVector2>();

    public static ulong LastCompletedFrame { get; private set; }
    public static int LastFrameEntityCount { get; private set; }
    public static int LastBodyCount { get; private set; }
    public static int LastCandidatePairCount { get; private set; }
    public static int LastResidualOverlapCount { get; private set; }
    public static Fix64 LastMaxResidualPenetration { get; private set; }
    public static int LastPairCorrectedBodyCount { get; private set; }
    public static Fix64 LastMaxPairCorrection { get; private set; }
    public static int LastStaticProjectionAvailableCount { get; private set; }
    public static int LastStaticProjectionChangedCount { get; private set; }
    public static int LastStaticProjectionFailureCount { get; private set; }
    public static int LastRegionConstraintChangedCount { get; private set; }
    public static int LastJointConstraintRejectedCount { get; private set; }
    public static IReadOnlyList<LogicAgentCollisionShadowState> LastStates => s_ReadOnlyStates;
    public static ulong FramesWithPairCorrection { get; private set; }
    public static ulong TotalPairCorrectedBodyCount { get; private set; }
    public static Fix64 MaxObservedPairCorrection { get; private set; }
    public static ulong TotalStaticProjectionChangedCount { get; private set; }
    public static ulong TotalStaticProjectionFailureCount { get; private set; }

    public static void SolveFrame(
        ulong frameId,
        LogicEntityFrameSnapshot snapshot,
        IReadOnlyList<ILogicFrameEntity> entities)
    {
        bool profile = UnityGameFramework.Runtime.MainThreadFrameProfiler.LoggingEnabled;
        long prepareStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        if (!LogicFrameRuntime.IsTicking || frameId != LogicFrameRuntime.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicAgentCollisionShadowService.SolveFrame failed: frame mismatch. requested={frameId}, current={LogicFrameRuntime.CurrentFrame}.");
        }
        if (snapshot == null || snapshot.FrameId != frameId)
            throw new InvalidOperationException("LogicAgentCollisionShadowService.SolveFrame failed: frame snapshot is missing or stale.");
        if (entities == null)
            throw new ArgumentNullException(nameof(entities));
        if (snapshot.States.Count != entities.Count)
        {
            throw new InvalidOperationException(
                $"LogicAgentCollisionShadowService.SolveFrame failed: snapshot/entity count mismatch. snapshot={snapshot.States.Count}, entities={entities.Count}.");
        }

        s_Bodies.Clear();
        s_BodyEntities.Clear();
        s_BodyFrameStartPositions.Clear();
        s_ProposedPositions.Clear();
        s_PairAdjustedDisplacements.Clear();
        s_PairCorrections.Clear();
        s_StaticCorrections.Clear();
        s_RegionCorrections.Clear();
        s_RegionFailures.Clear();
        s_PairCorrectedEntityIds.Clear();
        s_StaticProjectionChangedEntityIds.Clear();
        s_RegionConstraintChangedEntityIds.Clear();
        s_JointConstraintRejectedEntityIds.Clear();
        s_States.Clear();
        s_ResolvedPositions.Clear();
        for (int i = 0; i < entities.Count; i++)
        {
            ILogicFrameEntity entity = entities[i];
            LogicEntityFrameState state = snapshot.States[i];
            if (entity == null || state.EntityId != entity.LogicEntityId)
                throw new InvalidOperationException($"LogicAgentCollisionShadowService.SolveFrame failed: identity mismatch at index {i}.");

            if (!entity.HasPreparedLogicMove || entity.PreparedLogicFrame != frameId)
            {
                throw new InvalidOperationException(
                    $"LogicAgentCollisionShadowService.SolveFrame failed: entity {state.EntityId.Value} has no prepared move for frame {frameId}.");
            }

            FixVector2 proposedPosition = state.Position + entity.PreparedResolvedHorizontalDisplacement;
            s_ResolvedPositions.Add(state.EntityId.Value, proposedPosition);
            if (!state.Alive)
                continue;
            if (state.CollisionRadius <= Fix64.Zero)
            {
                if (!entity.AllowsZeroCollisionRadius && entity.PreparedCollisionMovable)
                {
                    throw new InvalidOperationException(
                        $"LogicAgentCollisionShadowService.SolveFrame failed: movable entity {state.EntityId.Value} has no collision radius.");
                }
                continue;
            }

            s_Bodies.Add(new LogicAgentCollisionBody(
                state.EntityId,
                proposedPosition,
                state.CollisionRadius,
                entity.PreparedCollisionMovable ? Fix64.One : Fix64.Zero,
                LogicAgentCollisionFilter.ResolveCategory(state.Side),
                entity.AgentCollisionMask));
            s_BodyEntities.Add(entity);
            s_BodyFrameStartPositions.Add(state.Position);
            s_ProposedPositions.Add(proposedPosition);
            s_PairAdjustedDisplacements.Add(proposedPosition - state.Position);
            s_PairCorrections.Add(FixVector2.Zero);
            s_StaticCorrections.Add(FixVector2.Zero);
            s_RegionCorrections.Add(FixVector2.Zero);
            s_RegionFailures.Add(LogicMovementRegionConstraintFailure.None);
        }
        if (profile)
        {
            UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
                UnityGameFramework.Runtime.MainThreadPerfScope.LogicMoveResolvePrepare,
                System.Diagnostics.Stopwatch.GetTimestamp() - prepareStartTicks);
        }

        LastPairCorrectedBodyCount = 0;
        LastMaxPairCorrection = Fix64.Zero;
        LastStaticProjectionAvailableCount = 0;
        LastStaticProjectionChangedCount = 0;
        LastStaticProjectionFailureCount = 0;
        LastJointConstraintRejectedCount = 0;
        LogicAgentCollisionSolveSummary summary = default;
        for (int pass = 0; pass < PairStaticProjectionPassCount; pass++)
        {
            long pairStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            summary = DeterministicAgentCollisionSolver.SolveInto(
                s_Bodies,
                SolverIterationCount,
                PenetrationEpsilon,
                s_SolverStates);
            if (profile)
            {
                UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
                    UnityGameFramework.Runtime.MainThreadPerfScope.LogicMoveResolvePairSolver,
                    System.Diagnostics.Stopwatch.GetTimestamp() - pairStartTicks);
            }

            long projectionStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            ResolveStaticProjection(s_SolverStates, pass == PairStaticProjectionPassCount - 1, profile);
            if (profile)
            {
                UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
                    UnityGameFramework.Runtime.MainThreadPerfScope.LogicMoveResolveProjection,
                    System.Diagnostics.Stopwatch.GetTimestamp() - projectionStartTicks);
            }
            if (pass < PairStaticProjectionPassCount - 1)
            {
                long rebuildStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
                RebuildBodiesFromResolvedPositions();
                if (profile)
                {
                    UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
                        UnityGameFramework.Runtime.MainThreadPerfScope.LogicMoveResolveRebuild,
                        System.Diagnostics.Stopwatch.GetTimestamp() - rebuildStartTicks);
                }
            }
        }
        long bookkeepingStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        LastPairCorrectedBodyCount = s_PairCorrectedEntityIds.Count;
        for (int i = 0; i < s_PairCorrections.Count; i++)
        {
            Fix64 magnitude = FixVector2.Magnitude(s_PairCorrections[i]);
            if (magnitude > LastMaxPairCorrection)
                LastMaxPairCorrection = magnitude;
        }
        LastStaticProjectionChangedCount = s_StaticProjectionChangedEntityIds.Count;
        LastRegionConstraintChangedCount = s_RegionConstraintChangedEntityIds.Count;
        LastJointConstraintRejectedCount = s_JointConstraintRejectedEntityIds.Count;

        LastCompletedFrame = frameId;
        LastFrameEntityCount = entities.Count;
        LastBodyCount = s_Bodies.Count;
        LastCandidatePairCount = summary.CandidatePairCount;
        LastResidualOverlapCount = summary.ResidualOverlapCount;
        LastMaxResidualPenetration = summary.MaxResidualPenetration;
        if (LastPairCorrectedBodyCount > 0)
            FramesWithPairCorrection = checked(FramesWithPairCorrection + 1);
        TotalPairCorrectedBodyCount = checked(TotalPairCorrectedBodyCount + (ulong)LastPairCorrectedBodyCount);
        if (LastMaxPairCorrection > MaxObservedPairCorrection)
            MaxObservedPairCorrection = LastMaxPairCorrection;
        TotalStaticProjectionChangedCount = checked(
            TotalStaticProjectionChangedCount + (ulong)LastStaticProjectionChangedCount);
        TotalStaticProjectionFailureCount = checked(
            TotalStaticProjectionFailureCount + (ulong)LastStaticProjectionFailureCount);
        if (profile)
        {
            UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
                UnityGameFramework.Runtime.MainThreadPerfScope.LogicMoveResolveBookkeeping,
                System.Diagnostics.Stopwatch.GetTimestamp() - bookkeepingStartTicks);
        }
    }

    private static void ResolveStaticProjection(
        IReadOnlyList<LogicAgentCollisionState> solverStates,
        bool recordFinalState,
        bool profile)
    {
        if (solverStates.Count != s_Bodies.Count
            || s_BodyEntities.Count != s_Bodies.Count
            || s_BodyFrameStartPositions.Count != s_Bodies.Count
            || s_ProposedPositions.Count != s_Bodies.Count
            || s_PairAdjustedDisplacements.Count != s_Bodies.Count
            || s_PairCorrections.Count != s_Bodies.Count
            || s_StaticCorrections.Count != s_Bodies.Count
            || s_RegionCorrections.Count != s_Bodies.Count
            || s_RegionFailures.Count != s_Bodies.Count)
        {
            throw new InvalidOperationException(
                "LogicAgentCollisionShadowService.ResolveStaticProjection failed: body/result count mismatch.");
        }

        long staticSolverTicks = 0L;
        long regionConstraintTicks = 0L;
        for (int i = 0; i < solverStates.Count; i++)
        {
            LogicAgentCollisionBody body = s_Bodies[i];
            LogicAgentCollisionState pairState = solverStates[i];
            if (pairState.EntityId != body.EntityId)
                throw new InvalidOperationException("LogicAgentCollisionShadowService.ResolveStaticProjection failed: result identity mismatch.");

            FixVector2 pairCorrection = pairState.Position - body.Position;
            if (pairCorrection != FixVector2.Zero)
            {
                s_PairCorrectedEntityIds.Add(body.EntityId.Value);
                s_PairCorrections[i] += pairCorrection;
            }

            FixVector2 frameStart = s_BodyFrameStartPositions[i];
            s_PairAdjustedDisplacements[i] += pairCorrection;
            ILogicFrameEntity entity = s_BodyEntities[i];
            FixVector2 staticPosition = pairState.Position;
            bool staticAvailable = false;
            bool staticSucceeded = false;
            FixVector2 firstHitNormal = FixVector2.Zero;
            LogicStaticCollisionContactKind staticContactKind = LogicStaticCollisionContactKind.None;
            int staticContactCellX = -1;
            int staticContactCellY = -1;
            int runtimeObstacleStableId = 0;
            if (entity.PreparedNavigationConstraintEnabled)
            {
                LogicStaticCollisionSlideMode slideMode = entity.PreparedPreserveSpeedOnStaticSlide
                    && s_PairCorrections[i] == FixVector2.Zero
                        ? LogicStaticCollisionSlideMode.PreserveRemainingDistance
                        : LogicStaticCollisionSlideMode.PreserveTangentialComponent;
                long staticStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
                staticAvailable = LogicStaticCollisionShadowService.TrySolveFixed(
                    entity.NavigationAgentTypeId,
                    frameStart,
                    s_PairAdjustedDisplacements[i],
                    body.Radius,
                    slideMode,
                    out LogicStaticCollisionShadowResult staticResult);
                if (profile)
                    staticSolverTicks += System.Diagnostics.Stopwatch.GetTimestamp() - staticStartTicks;
                if (!staticAvailable)
                {
                    throw new InvalidOperationException(
                        $"LogicAgentCollisionShadowService.ResolveStaticProjection failed: no static collision world for entity {body.EntityId.Value}, agentType={entity.NavigationAgentTypeId}.");
                }

                if (recordFinalState)
                    LastStaticProjectionAvailableCount++;
                staticSucceeded = staticResult.SolveResult.Success;
                firstHitNormal = staticResult.SolveResult.FirstHitNormal;
                staticContactKind = staticResult.ContactKind;
                staticContactCellX = staticResult.ContactCellX;
                staticContactCellY = staticResult.ContactCellY;
                runtimeObstacleStableId = staticResult.RuntimeObstacleStableId;
                if (!staticSucceeded)
                {
                    LastStaticProjectionFailureCount++;
                    string contactTrace = BuildStaticCollisionFailureTrace(
                        entity.NavigationAgentTypeId,
                        staticResult.SolveResult);
                    throw new InvalidOperationException(
                        $"LogicAgentCollisionShadowService.ResolveStaticProjection failed: static projection failed. " +
                        $"entity={body.EntityId.Value}, character={entity.CharacterKey}, building={entity.IsBuildingEntity}, " +
                        $"frame={LogicFrameRuntime.CurrentFrame}, agentType={entity.NavigationAgentTypeId}, " +
                        $"radiusRaw={body.Radius.RawValue}, slide={slideMode}, failure={staticResult.SolveResult.Failure}, " +
                        $"startRaw=({frameStart.x.RawValue},{frameStart.y.RawValue}), " +
                        $"desiredRaw=({s_PairAdjustedDisplacements[i].x.RawValue},{s_PairAdjustedDisplacements[i].y.RawValue}), " +
                        $"recoveredRaw=({staticResult.SolveResult.RecoveredStart.x.RawValue},{staticResult.SolveResult.RecoveredStart.y.RawValue}), " +
                        $"resolvedRaw=({staticResult.SolveResult.ResolvedDisplacement.x.RawValue},{staticResult.SolveResult.ResolvedDisplacement.y.RawValue}), " +
                        $"startedOverlapping={staticResult.SolveResult.StartedOverlapping}, contacts={staticResult.SolveResult.ContactCount}, " +
                        $"firstHitKey={staticResult.SolveResult.FirstHitStableKey}, " +
                        $"firstHitNormalRaw=({staticResult.SolveResult.FirstHitNormal.x.RawValue},{staticResult.SolveResult.FirstHitNormal.y.RawValue}), " +
                        $"contactKind={staticResult.ContactKind}, cell=({staticResult.ContactCellX},{staticResult.ContactCellY}), " +
                        $"boundarySegment={staticResult.ContactBoundarySegmentIndex}, runtimeObstacle={staticResult.RuntimeObstacleStableId}. " +
                        contactTrace);
                }

                staticPosition = staticResult.SolveResult.Start + staticResult.SolveResult.ResolvedDisplacement;
                if (staticPosition != pairState.Position)
                {
                    s_StaticProjectionChangedEntityIds.Add(body.EntityId.Value);
                    s_StaticCorrections[i] += staticPosition - pairState.Position;
                }
            }

            FixVector2 wallPosition = LogicWallRuntime.ResolveMotion(
                entity,
                frameStart,
                staticPosition,
                body.Radius);
            if (wallPosition != staticPosition)
            {
                s_StaticProjectionChangedEntityIds.Add(body.EntityId.Value);
                s_StaticCorrections[i] += wallPosition - staticPosition;
                staticPosition = wallPosition;
            }

            FixVector2 resolvedPosition = staticPosition;
            LogicMovementRegionConstraintFailure regionFailure = LogicMovementRegionConstraintFailure.None;
            long regionStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            resolvedPosition = LogicMovementRegionConstraintService.ResolvePosition(
                entity,
                frameStart,
                staticPosition,
                out regionFailure);
            if (regionFailure != LogicMovementRegionConstraintFailure.None)
                s_RegionFailures[i] = regionFailure;
            if (profile)
                regionConstraintTicks += System.Diagnostics.Stopwatch.GetTimestamp() - regionStartTicks;
            if (resolvedPosition != staticPosition)
            {
                s_RegionConstraintChangedEntityIds.Add(body.EntityId.Value);
                s_RegionCorrections[i] += resolvedPosition - staticPosition;
            }

            if (entity.PreparedNavigationConstraintEnabled && resolvedPosition != staticPosition)
            {
                resolvedPosition = ResolveJointConstraintConflict(
                    entity,
                    body,
                    frameStart,
                    staticPosition,
                    resolvedPosition,
                    i);
            }

            s_ResolvedPositions[body.EntityId.Value] = resolvedPosition;

            if (recordFinalState)
            {
                s_States.Add(new LogicAgentCollisionShadowState(
                    body.EntityId,
                    s_ProposedPositions[i],
                    s_PairCorrections[i],
                    s_StaticCorrections[i],
                    s_RegionCorrections[i],
                    resolvedPosition,
                    staticAvailable,
                    staticSucceeded,
                    firstHitNormal,
                    staticContactKind,
                    staticContactCellX,
                    staticContactCellY,
                    runtimeObstacleStableId,
                    s_RegionFailures[i]));
            }
        }
        if (profile)
        {
            UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
                UnityGameFramework.Runtime.MainThreadPerfScope.LogicMoveResolveStaticSolver,
                staticSolverTicks);
            UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
                UnityGameFramework.Runtime.MainThreadPerfScope.LogicMoveResolveRegionConstraint,
                regionConstraintTicks);
        }
    }

    private static string BuildStaticCollisionFailureTrace(
        int agentTypeId,
        LogicStaticCollisionSolveResult solveResult)
    {
        var builder = new System.Text.StringBuilder(768);
        builder.Append("contactTrace=[");
        int recordedCount = Math.Min(
            solveResult.ContactCount,
            LogicStaticCollisionSolveResult.MaxRecordedContactCount);
        for (int i = 0; i < recordedCount; i++)
        {
            if (i > 0)
                builder.Append(';');
            LogicStaticCollisionContactTrace trace = solveResult.GetContactTrace(i);
            builder.Append("{iteration=").Append(trace.Iteration)
                .Append(",key=").Append(trace.StableKey)
                .Append(",timeRaw=").Append(trace.Time.RawValue)
                .Append(",normalRaw=(").Append(trace.Normal.x.RawValue).Append(',').Append(trace.Normal.y.RawValue).Append(')')
                .Append(",positionRaw=(").Append(trace.Position.x.RawValue).Append(',').Append(trace.Position.y.RawValue).Append(')')
                .Append(",incomingRaw=(").Append(trace.Incoming.x.RawValue).Append(',').Append(trace.Incoming.y.RawValue).Append(')')
                .Append(",leftoverRaw=(").Append(trace.Leftover.x.RawValue).Append(',').Append(trace.Leftover.y.RawValue).Append(')');

            if (LogicStaticCollisionShadowService.TryGetRuntimeObstacleForContact(
                    agentTypeId,
                    trace.StableKey,
                    out LogicStaticCollisionObstacle obstacle))
            {
                builder.Append(",obstacle={stableId=").Append(obstacle.StableId)
                    .Append(",kind=").Append(obstacle.Kind)
                    .Append(",centerRaw=(").Append(obstacle.Center.x.RawValue).Append(',').Append(obstacle.Center.y.RawValue).Append(')')
                    .Append(",halfRaw=(").Append(obstacle.HalfExtents.x.RawValue).Append(',').Append(obstacle.HalfExtents.y.RawValue).Append(')')
                    .Append(",radiusRaw=").Append(obstacle.Radius.RawValue);
                AppendObstacleOwner(builder, obstacle.StableId);
                builder.Append('}');
            }
            builder.Append('}');
        }
        builder.Append(']');
        return builder.ToString();
    }

    private static void AppendObstacleOwner(System.Text.StringBuilder builder, int obstacleId)
    {
        if (!LogicEntityObstacleId.TryDecodeBuildingCollider(
                obstacleId,
                out LogicEntityId ownerId,
                out int localOrdinal))
        {
            builder.Append(",owner=nonBuildingObstacle");
            return;
        }

        builder.Append(",ownerEntity=").Append(ownerId.Value)
            .Append(",localOrdinal=").Append(localOrdinal);
        if (!EntityRegistry.TryGet(ownerId, out IEntityContext owner))
        {
            builder.Append(",ownerState=unregistered");
            return;
        }

        builder.Append(",ownerCharacter=").Append(owner.CharacterKey);
        if (owner is IBuildingLogicContext building)
        {
            builder.Append(",ownerObstacleCount=").Append(building.LogicObstacleShapes.Count);
            if (localOrdinal >= 0 && localOrdinal < building.LogicObstacleShapes.Count)
            {
                LogicCombatShape shape = building.LogicObstacleShapes[localOrdinal];
                builder.Append(",ownerShapeCenterRaw=(").Append(shape.Center.x.RawValue).Append(',').Append(shape.Center.y.RawValue).Append(')')
                    .Append(",ownerShapeHalfRaw=(").Append(shape.HalfExtents.x.RawValue).Append(',').Append(shape.HalfExtents.y.RawValue).Append(')');
            }
            else
            {
                builder.Append(",ownerShapeState=ordinalOutOfRange");
            }
        }
        else
        {
            builder.Append(",ownerState=notBuildingContext");
        }
    }

    private static FixVector2 ResolveJointConstraintConflict(
        ILogicFrameEntity entity,
        LogicAgentCollisionBody body,
        FixVector2 frameStart,
        FixVector2 staticPosition,
        FixVector2 regionPosition,
        int bodyIndex)
    {
        LogicStaticCollisionShadowResult regionStaticProbe = ResolveStaticZeroDisplacement(
            entity,
            body,
            regionPosition,
            "region result");
        if (!regionStaticProbe.SolveResult.StartedOverlapping)
            return regionPosition;

        if (regionPosition == staticPosition)
        {
            throw new InvalidOperationException(
                $"LogicAgentCollisionShadowService joint constraint failed: static projection returned an overlapping position. " +
                $"entity={body.EntityId.Value}, frame={LogicFrameRuntime.CurrentFrame}, " +
                $"positionRaw=({regionPosition.x.RawValue},{regionPosition.y.RawValue}), " +
                $"recoveryRaw=({regionStaticProbe.SolveResult.ResolvedDisplacement.x.RawValue},{regionStaticProbe.SolveResult.ResolvedDisplacement.y.RawValue}).");
        }

        LogicStaticCollisionShadowResult staticProbe = ResolveStaticZeroDisplacement(
            entity,
            body,
            staticPosition,
            "static result");
        if (staticProbe.SolveResult.StartedOverlapping)
        {
            throw new InvalidOperationException(
                $"LogicAgentCollisionShadowService joint constraint failed: static result is not statically legal. " +
                $"entity={body.EntityId.Value}, frame={LogicFrameRuntime.CurrentFrame}, " +
                $"positionRaw=({staticPosition.x.RawValue},{staticPosition.y.RawValue}).");
        }

        if (LogicMovementRegionConstraintService.IsPositionAllowed(
                entity,
                staticPosition,
                out LogicMovementRegionConstraintFailure staticRegionFailure))
        {
            throw new InvalidOperationException(
                $"LogicAgentCollisionShadowService joint constraint failed: region projection introduced a static overlap without a region conflict. " +
                $"entity={body.EntityId.Value}, frame={LogicFrameRuntime.CurrentFrame}, " +
                $"staticRaw=({staticPosition.x.RawValue},{staticPosition.y.RawValue}), " +
                $"regionRaw=({regionPosition.x.RawValue},{regionPosition.y.RawValue}).");
        }

        LogicStaticCollisionShadowResult startStaticProbe = ResolveStaticZeroDisplacement(
            entity,
            body,
            frameStart,
            "frame start");
        bool startRegionAllowed = LogicMovementRegionConstraintService.IsPositionAllowed(
            entity,
            frameStart,
            out LogicMovementRegionConstraintFailure startRegionFailure);
        if (startStaticProbe.SolveResult.StartedOverlapping || !startRegionAllowed)
        {
            throw new InvalidOperationException(
                $"LogicAgentCollisionShadowService joint constraint failed: frame start is not jointly legal. " +
                $"entity={body.EntityId.Value}, frame={LogicFrameRuntime.CurrentFrame}, " +
                $"startRaw=({frameStart.x.RawValue},{frameStart.y.RawValue}), " +
                $"staticOverlap={startStaticProbe.SolveResult.StartedOverlapping}, regionFailure={startRegionFailure}, " +
                $"staticRaw=({staticPosition.x.RawValue},{staticPosition.y.RawValue}), " +
                $"regionRaw=({regionPosition.x.RawValue},{regionPosition.y.RawValue}), staticRegionFailure={staticRegionFailure}.");
        }

        s_JointConstraintRejectedEntityIds.Add(body.EntityId.Value);
        s_RegionConstraintChangedEntityIds.Add(body.EntityId.Value);
        s_RegionCorrections[bodyIndex] += frameStart - regionPosition;
        s_RegionFailures[bodyIndex] = staticRegionFailure;
        return frameStart;
    }

    private static LogicStaticCollisionShadowResult ResolveStaticZeroDisplacement(
        ILogicFrameEntity entity,
        LogicAgentCollisionBody body,
        FixVector2 position,
        string source)
    {
        if (!LogicStaticCollisionShadowService.TrySolveFixed(
                entity.NavigationAgentTypeId,
                position,
                FixVector2.Zero,
                body.Radius,
                out LogicStaticCollisionShadowResult probe))
        {
            throw new InvalidOperationException(
                $"LogicAgentCollisionShadowService joint constraint failed: static collision world is unavailable for {source}. " +
                $"entity={body.EntityId.Value}, frame={LogicFrameRuntime.CurrentFrame}, agentType={entity.NavigationAgentTypeId}.");
        }
        if (!probe.SolveResult.Success)
        {
            throw new InvalidOperationException(
                $"LogicAgentCollisionShadowService joint constraint failed: static zero-displacement probe failed for {source}. " +
                $"entity={body.EntityId.Value}, frame={LogicFrameRuntime.CurrentFrame}, failure={probe.SolveResult.Failure}, " +
                $"positionRaw=({position.x.RawValue},{position.y.RawValue}).");
        }

        return probe;
    }

    private static void RebuildBodiesFromResolvedPositions()
    {
        for (int i = 0; i < s_Bodies.Count; i++)
        {
            LogicAgentCollisionBody body = s_Bodies[i];
            if (!s_ResolvedPositions.TryGetValue(body.EntityId.Value, out FixVector2 position))
            {
                throw new InvalidOperationException(
                    $"LogicAgentCollisionShadowService.RebuildBodiesFromResolvedPositions failed: entity {body.EntityId.Value} is missing.");
            }

            s_Bodies[i] = new LogicAgentCollisionBody(
                body.EntityId,
                position,
                body.Radius,
                body.InverseMass,
                body.CollisionCategory,
                body.CollisionMask);
        }
    }

    public static FixVector2 GetRequiredResolvedPosition(LogicEntityId entityId, ulong frameId)
    {
        if (!entityId.IsValid)
            throw new ArgumentException("Resolved movement lookup requires a valid entity id.", nameof(entityId));
        if (LastCompletedFrame != frameId || frameId != LogicFrameRuntime.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicAgentCollisionShadowService.GetRequiredResolvedPosition failed: frame mismatch. requested={frameId}, completed={LastCompletedFrame}, current={LogicFrameRuntime.CurrentFrame}.");
        }
        if (!s_ResolvedPositions.TryGetValue(entityId.Value, out FixVector2 position))
        {
            throw new InvalidOperationException(
                $"LogicAgentCollisionShadowService.GetRequiredResolvedPosition failed: entity {entityId.Value} has no resolved position at frame {frameId}.");
        }
        return position;
    }

    public static LogicAgentCollisionShadowState GetRequiredState(LogicEntityId entityId, ulong frameId)
    {
        if (!entityId.IsValid)
            throw new ArgumentException("Collision state lookup requires a valid entity id.", nameof(entityId));
        if (LastCompletedFrame != frameId || frameId != LogicFrameRuntime.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicAgentCollisionShadowService.GetRequiredState failed: frame mismatch. requested={frameId}, completed={LastCompletedFrame}, current={LogicFrameRuntime.CurrentFrame}.");
        }

        for (int i = 0; i < s_States.Count; i++)
        {
            if (s_States[i].EntityId == entityId)
                return s_States[i];
        }

        throw new InvalidOperationException(
            $"LogicAgentCollisionShadowService.GetRequiredState failed: entity {entityId.Value} has no collision state at frame {frameId}.");
    }

    public static void Clear()
    {
        s_Bodies.Clear();
        s_BodyEntities.Clear();
        s_BodyFrameStartPositions.Clear();
        s_ProposedPositions.Clear();
        s_PairAdjustedDisplacements.Clear();
        s_PairCorrections.Clear();
        s_StaticCorrections.Clear();
        s_RegionCorrections.Clear();
        s_RegionFailures.Clear();
        s_SolverStates.Clear();
        s_PairCorrectedEntityIds.Clear();
        s_StaticProjectionChangedEntityIds.Clear();
        s_RegionConstraintChangedEntityIds.Clear();
        s_JointConstraintRejectedEntityIds.Clear();
        s_States.Clear();
        s_ResolvedPositions.Clear();
        LastCompletedFrame = 0;
        LastFrameEntityCount = 0;
        LastBodyCount = 0;
        LastCandidatePairCount = 0;
        LastResidualOverlapCount = 0;
        LastMaxResidualPenetration = Fix64.Zero;
        LastPairCorrectedBodyCount = 0;
        LastMaxPairCorrection = Fix64.Zero;
        LastStaticProjectionAvailableCount = 0;
        LastStaticProjectionChangedCount = 0;
        LastStaticProjectionFailureCount = 0;
        LastRegionConstraintChangedCount = 0;
        LastJointConstraintRejectedCount = 0;
        FramesWithPairCorrection = 0;
        TotalPairCorrectedBodyCount = 0;
        MaxObservedPairCorrection = Fix64.Zero;
        TotalStaticProjectionChangedCount = 0;
        TotalStaticProjectionFailureCount = 0;
    }
}
