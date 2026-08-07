using System;

public static class LogicInteractionAuthorityService
{
    private sealed class AuthorityListener : ILogicFrameUpdate
    {
        public int LogicFrameOrder => -500;

        public void OnLogicFrameUpdate(Fix64 deltaTime)
        {
            Tick(deltaTime);
        }
    }

    private static readonly AuthorityListener s_Listener = new AuthorityListener();
    private static readonly Fix64 s_EffectiveRange = Fix64.FromRaw(8848);
    private static readonly Fix64 s_DistanceWeight =
        Fix64.FromRaw(2663) / (Fix64.FromRaw(2663) + Fix64.FromRaw(1434));
    private static readonly Fix64 s_AngleWeight =
        Fix64.FromRaw(1434) / (Fix64.FromRaw(2663) + Fix64.FromRaw(1434));
    private static readonly Fix64 s_SwitchThreshold = Fix64.FromRaw(328);
    private const int MinHoldFrames = 3;

    private static LogicEntityId s_CurrentActorId;
    private static LogicEntityId s_CurrentTargetId;
    private static ulong s_LastSwitchFrame;

    public static bool IsActive { get; private set; }
    public static LogicEntityId CurrentActorId => s_CurrentActorId;
    public static LogicEntityId CurrentTargetId => s_CurrentTargetId;
    public static ulong LastSwitchFrame => s_LastSwitchFrame;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicInteractionAuthorityService.BeginTimeline failed: service is already active.");
        if (!LogicFrameRuntime.IsActive)
            throw new InvalidOperationException("LogicInteractionAuthorityService.BeginTimeline failed: logic runtime is not active.");
        if (!LogicEntityFrameSnapshotService.IsActive)
            throw new InvalidOperationException("LogicInteractionAuthorityService.BeginTimeline failed: frame snapshot service is not active.");
        if (!LogicInteractionTargetStateService.IsActive)
            throw new InvalidOperationException("LogicInteractionAuthorityService.BeginTimeline failed: interaction dependencies are not active.");

        ClearState();
        LogicFrameRuntime.Register(s_Listener);
        IsActive = true;
    }

    public static void EndTimeline()
    {
        EnsureActive();
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("LogicInteractionAuthorityService.EndTimeline failed: a logic frame is running.");

        LogicFrameRuntime.Unregister(s_Listener);
        ClearState();
        IsActive = false;
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        if (!LogicTimeControlService.IsPaused)
            throw new InvalidOperationException("LogicInteractionAuthorityService reset requires paused logic time.");
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("LogicInteractionAuthorityService reset cannot run during a logic Tick.");

        ClearState();
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        EnsureActive();
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add(s_CurrentActorId.IsValid ? s_CurrentActorId.Value : 0);
        hasher.Add(s_CurrentTargetId.IsValid ? s_CurrentTargetId.Value : 0);
        hasher.Add(s_LastSwitchFrame);
    }

    public static bool TryComputeScore(
        LogicEntityFrameState actorState,
        LogicEntityFrameState targetState,
        Fix64 effectiveRange,
        Fix64 distanceScoreWeight,
        Fix64 angleScoreWeight,
        out Fix64 score)
    {
        if (effectiveRange <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(effectiveRange));
        if (distanceScoreWeight < Fix64.Zero || angleScoreWeight < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(distanceScoreWeight));
        Fix64 scoreWeightSum = distanceScoreWeight + angleScoreWeight;
        if (scoreWeightSum <= Fix64.Zero)
            throw new ArgumentException("Interaction score weights must have a positive sum.");
        distanceScoreWeight /= scoreWeightSum;
        angleScoreWeight /= scoreWeightSum;

        Fix64 distance = targetState.CombatShape.DistanceToSurface(actorState.Position);
        if (distance > effectiveRange)
        {
            score = -Fix64.One;
            return false;
        }

        Fix64 distanceScore = Fix64.One - Fix64.Clamp(distance / effectiveRange, Fix64.Zero, Fix64.One);
        FixVector2 toTarget = targetState.CombatShape.Center - actorState.Position;
        FixVector2 direction = FixVector2.SqrMagnitude(toTarget) > Fix64.Zero
            ? toTarget.GetNormalized()
            : actorState.Forward;
        Fix64 cosine = FixVector2.Dot(actorState.Forward, direction);
        Fix64 angleScore = Fix64.Clamp((cosine + Fix64.One) / 2, Fix64.Zero, Fix64.One);
        score = distanceScore * distanceScoreWeight + angleScore * angleScoreWeight;
        return true;
    }

    public static bool IsBetterCandidate(
        Fix64 score,
        LogicEntityId entityId,
        bool hasBest,
        Fix64 bestScore,
        LogicEntityId bestEntityId)
    {
        if (!entityId.IsValid)
            throw new ArgumentException("Candidate entity id must be valid.", nameof(entityId));
        if (!hasBest)
            return true;
        if (!bestEntityId.IsValid)
            throw new ArgumentException("Best entity id must be valid when a best candidate exists.", nameof(bestEntityId));

        return score > bestScore || (score == bestScore && entityId < bestEntityId);
    }

    private static void Tick(Fix64 deltaTime)
    {
        EnsureActive();
        if (deltaTime != LogicFrameRuntime.FixedDeltaTime)
            throw new InvalidOperationException("LogicInteractionAuthorityService received a non-fixed logic delta.");
        if (LogicEntityFrameSnapshotService.CapturedFrame != LogicFrameRuntime.CurrentFrame)
            throw new InvalidOperationException("LogicInteractionAuthorityService requires the current frame snapshot.");

        IEntityContext actor = EntityRegistry.Player;
        if (actor == null || !actor.Alive)
        {
            ClearActorTarget();
            return;
        }
        if (!actor.LogicEntityId.IsValid)
            throw new InvalidOperationException("Logic interaction player has an invalid logic entity id.");

        if (actor.LogicEntityId != s_CurrentActorId)
        {
            if (s_CurrentActorId.IsValid)
                LogicInteractionTargetStateService.RemoveActor(s_CurrentActorId);
            s_CurrentActorId = actor.LogicEntityId;
            s_CurrentTargetId = default;
            s_LastSwitchFrame = LogicFrameRuntime.CurrentFrame;
        }

        LogicEntityId targetId = ResolveBestTarget(actor, s_CurrentTargetId);
        LogicInteractionTargetStateService.SetTarget(s_CurrentActorId, targetId);
        if (targetId == s_CurrentTargetId)
            return;

        s_CurrentTargetId = targetId;
        s_LastSwitchFrame = LogicFrameRuntime.CurrentFrame;
    }

    private static LogicEntityId ResolveBestTarget(IEntityContext actor, LogicEntityId currentId)
    {
        LogicEntityFrameState actorState = LogicEntityFrameSnapshotService.GetRequiredCurrent(actor);
        IBuildingLogicContext bestBuilding = null;
        Fix64 bestScore = -Fix64.One;

        for (int i = 0; i < EntityRegistry.AllEntities.Count; i++)
        {
            if (!EntityRegistry.AllEntities[i].TryGetLogicBuilding(out IBuildingLogicContext building)
                || !building.Alive
                || !LogicInteractionOptionService.HasVisibleOptions(building)
                || !TryScore(actorState, building, out Fix64 score))
            {
                continue;
            }

            if (IsBetterCandidate(
                    score,
                    building.LogicEntityId,
                    bestBuilding != null,
                    bestScore,
                    bestBuilding != null ? bestBuilding.LogicEntityId : default))
            {
                bestBuilding = building;
                bestScore = score;
            }
        }

        LogicEntityId bestId = bestBuilding != null ? bestBuilding.LogicEntityId : default;
        if (!bestId.IsValid || !currentId.IsValid || currentId == bestId)
            return bestId;
        if (!TryResolveBuilding(currentId, out IBuildingLogicContext currentBuilding)
            || !TryScore(actorState, currentBuilding, out Fix64 currentScore))
        {
            return bestId;
        }

        ulong heldFrames = LogicFrameRuntime.CurrentFrame - s_LastSwitchFrame;
        if (heldFrames < MinHoldFrames)
            return currentId;
        return bestScore > currentScore + s_SwitchThreshold ? bestId : currentId;
    }

    private static bool TryScore(
        LogicEntityFrameState actorState,
        IBuildingLogicContext building,
        out Fix64 score)
    {
        LogicEntityFrameState targetState = LogicEntityFrameSnapshotService.Current.GetRequired(building.LogicEntityId);
        return TryComputeScore(
            actorState,
            targetState,
            s_EffectiveRange,
            s_DistanceWeight,
            s_AngleWeight,
            out score);
    }

    private static bool TryResolveBuilding(LogicEntityId entityId, out IBuildingLogicContext building)
    {
        building = null;
        if (!entityId.IsValid || !EntityRegistry.TryGet(entityId, out IEntityContext context))
            return false;
        if (!context.TryGetLogicBuilding(out building))
            return false;
        return building != null
               && building.Alive
               && LogicInteractionOptionService.HasVisibleOptions(building);
    }

    private static void ClearActorTarget()
    {
        if (s_CurrentActorId.IsValid)
            LogicInteractionTargetStateService.RemoveActor(s_CurrentActorId);
        ClearState();
    }

    private static void ClearState()
    {
        s_CurrentActorId = default;
        s_CurrentTargetId = default;
        s_LastSwitchFrame = 0;
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicInteractionAuthorityService operation failed: service is not active.");
    }
}
