using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct LogicAgentCollisionShadowState
{
    public LogicAgentCollisionShadowState(
        LogicEntityId entityId,
        FixVector2 proposedPosition,
        FixVector2 pairResolvedPosition,
        FixVector2 staticResolvedPosition,
        bool staticProjectionAvailable,
        bool staticProjectionSucceeded)
    {
        EntityId = entityId;
        ProposedPosition = proposedPosition;
        PairResolvedPosition = pairResolvedPosition;
        StaticResolvedPosition = staticResolvedPosition;
        StaticProjectionAvailable = staticProjectionAvailable;
        StaticProjectionSucceeded = staticProjectionSucceeded;
    }

    public LogicEntityId EntityId { get; }
    public FixVector2 ProposedPosition { get; }
    public FixVector2 PairResolvedPosition { get; }
    public FixVector2 StaticResolvedPosition { get; }
    public FixVector2 PairCorrection => PairResolvedPosition - ProposedPosition;
    public FixVector2 StaticCorrection => StaticResolvedPosition - PairResolvedPosition;
    public bool StaticProjectionAvailable { get; }
    public bool StaticProjectionSucceeded { get; }
}

public static class LogicAgentCollisionShadowService
{
    private const int SolverIterationCount = 8;
    private const int PairStaticProjectionPassCount = 4;
    private static readonly Fix64 PenetrationEpsilon = Fix64.FromRaw(1);
    private const uint UnitCategory = 1u;
    private const uint UnitMask = 1u;

    private static readonly List<LogicAgentCollisionBody> s_Bodies = new List<LogicAgentCollisionBody>();
    private static readonly List<ILogicFrameEntity> s_BodyEntities = new List<ILogicFrameEntity>();
    private static readonly List<FixVector2> s_BodyFrameStartPositions = new List<FixVector2>();
    private static readonly List<LogicAgentCollisionShadowState> s_States = new List<LogicAgentCollisionShadowState>();
    private static readonly List<FixVector2> s_ProposedPositions = new List<FixVector2>();
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
                UnitCategory,
                entity.AgentCollisionMask));
            s_BodyEntities.Add(entity);
            s_BodyFrameStartPositions.Add(state.Position);
            s_ProposedPositions.Add(proposedPosition);
        }

        LogicAgentCollisionSolveResult result = null;
        for (int pass = 0; pass < PairStaticProjectionPassCount; pass++)
        {
            result = DeterministicAgentCollisionSolver.Solve(
                s_Bodies,
                SolverIterationCount,
                PenetrationEpsilon);
            ResolveStaticProjection(result, pass == PairStaticProjectionPassCount - 1);
            if (pass < PairStaticProjectionPassCount - 1)
                RebuildBodiesFromResolvedPositions();
        }

        LastCompletedFrame = frameId;
        LastFrameEntityCount = entities.Count;
        LastBodyCount = s_Bodies.Count;
        LastCandidatePairCount = result.CandidatePairCount;
        LastResidualOverlapCount = result.ResidualOverlapCount;
        LastMaxResidualPenetration = result.MaxResidualPenetration;
        if (LastPairCorrectedBodyCount > 0)
            FramesWithPairCorrection = checked(FramesWithPairCorrection + 1);
        TotalPairCorrectedBodyCount = checked(TotalPairCorrectedBodyCount + (ulong)LastPairCorrectedBodyCount);
        if (LastMaxPairCorrection > MaxObservedPairCorrection)
            MaxObservedPairCorrection = LastMaxPairCorrection;
        TotalStaticProjectionChangedCount = checked(
            TotalStaticProjectionChangedCount + (ulong)LastStaticProjectionChangedCount);
        TotalStaticProjectionFailureCount = checked(
            TotalStaticProjectionFailureCount + (ulong)LastStaticProjectionFailureCount);
    }

    private static void ResolveStaticProjection(LogicAgentCollisionSolveResult result, bool recordFinalState)
    {
        if (result.States.Count != s_Bodies.Count
            || s_BodyEntities.Count != s_Bodies.Count
            || s_BodyFrameStartPositions.Count != s_Bodies.Count
            || s_ProposedPositions.Count != s_Bodies.Count)
        {
            throw new InvalidOperationException(
                "LogicAgentCollisionShadowService.ResolveStaticProjection failed: body/result count mismatch.");
        }

        LastPairCorrectedBodyCount = 0;
        LastMaxPairCorrection = Fix64.Zero;
        LastStaticProjectionAvailableCount = 0;
        LastStaticProjectionChangedCount = 0;
        LastStaticProjectionFailureCount = 0;
        for (int i = 0; i < result.States.Count; i++)
        {
            LogicAgentCollisionBody body = s_Bodies[i];
            LogicAgentCollisionState pairState = result.States[i];
            if (pairState.EntityId != body.EntityId)
                throw new InvalidOperationException("LogicAgentCollisionShadowService.ResolveStaticProjection failed: result identity mismatch.");

            Fix64 pairCorrectionMagnitude = FixVector2.Magnitude(pairState.Position - s_ProposedPositions[i]);
            if (pairCorrectionMagnitude > Fix64.Zero)
            {
                LastPairCorrectedBodyCount++;
                if (pairCorrectionMagnitude > LastMaxPairCorrection)
                    LastMaxPairCorrection = pairCorrectionMagnitude;
            }

            FixVector2 frameStart = s_BodyFrameStartPositions[i];
            FixVector2 pairDisplacement = pairState.Position - frameStart;
            ILogicFrameEntity entity = s_BodyEntities[i];
            bool staticAvailable = LogicStaticCollisionShadowService.TrySolveFixed(
                entity.NavigationAgentTypeId,
                frameStart,
                pairDisplacement,
                body.Radius,
                out LogicStaticCollisionShadowResult staticResult);

            if (!staticAvailable)
            {
                throw new InvalidOperationException(
                    $"LogicAgentCollisionShadowService.ResolveStaticProjection failed: no static collision world for entity {body.EntityId.Value}, agentType={entity.NavigationAgentTypeId}.");
            }

            FixVector2 staticPosition = pairState.Position;
            bool staticSucceeded = false;
            LastStaticProjectionAvailableCount++;
            staticSucceeded = staticResult.SolveResult.Success;
            if (staticSucceeded)
            {
                staticPosition = staticResult.SolveResult.Start + staticResult.SolveResult.ResolvedDisplacement;
                if (staticPosition != pairState.Position)
                    LastStaticProjectionChangedCount++;
            }
            else
            {
                LastStaticProjectionFailureCount++;
                throw new InvalidOperationException(
                    $"LogicAgentCollisionShadowService.ResolveStaticProjection failed: static projection failed for entity {body.EntityId.Value}. " +
                    $"failure={staticResult.SolveResult.Failure}, frame={LogicFrameRuntime.CurrentFrame}.");
            }

            s_ResolvedPositions[body.EntityId.Value] = staticPosition;

            if (recordFinalState)
            {
                s_States.Add(new LogicAgentCollisionShadowState(
                    body.EntityId,
                    s_ProposedPositions[i],
                    pairState.Position,
                    staticPosition,
                    staticAvailable,
                    staticSucceeded));
            }
        }
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

    public static void Clear()
    {
        s_Bodies.Clear();
        s_BodyEntities.Clear();
        s_BodyFrameStartPositions.Clear();
        s_ProposedPositions.Clear();
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
        FramesWithPairCorrection = 0;
        TotalPairCorrectedBodyCount = 0;
        MaxObservedPairCorrection = Fix64.Zero;
        TotalStaticProjectionChangedCount = 0;
        TotalStaticProjectionFailureCount = 0;
    }
}
