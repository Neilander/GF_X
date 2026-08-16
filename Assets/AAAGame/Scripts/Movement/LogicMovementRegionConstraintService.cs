using System;
using AAAGame.Card;

public enum LogicMovementRegionConstraintFailure
{
    None = 0,
    EnemyStronghold = 1,
    TutorialStrongholdBoundary = 2,
    NotVisible = 3,
}

public static class LogicMovementRegionConstraintService
{
    private static string s_TutorialStrongholdId;
    private static bool s_TutorialInvadeTriggerBound;
    private static FixVector2 s_TutorialInvadeTriggerCenter;
    private static FixVector2 s_TutorialInvadeTriggerHalfExtents;
    private static bool s_TutorialInvadeTriggerConsumed;

    public static event Action<string> TutorialStrongholdBoundaryActivated;

    public static bool IsActive { get; private set; }
    public static bool HasTutorialStrongholdBoundary => !string.IsNullOrEmpty(s_TutorialStrongholdId);
    public static string TutorialStrongholdId => s_TutorialStrongholdId;
    public static bool HasTutorialInvadeTrigger => s_TutorialInvadeTriggerBound;
    public static bool TutorialInvadeTriggerConsumed => s_TutorialInvadeTriggerConsumed;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicMovementRegionConstraintService.BeginTimeline failed: service is already active.");

        IsActive = true;
        ClearState();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        ClearState();
        IsActive = false;
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        ClearState();
    }

    public static void BindTutorialInvadeTrigger(FixVector2 center, FixVector2 halfExtents)
    {
        EnsureActive();
        if (s_TutorialInvadeTriggerBound)
            throw new InvalidOperationException("LogicMovementRegionConstraintService already has a tutorial invade trigger.");
        if (halfExtents.x <= Fix64.Zero || halfExtents.y <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(halfExtents), "Tutorial invade trigger half extents must be positive.");

        s_TutorialInvadeTriggerBound = true;
        s_TutorialInvadeTriggerCenter = center;
        s_TutorialInvadeTriggerHalfExtents = halfExtents;
        s_TutorialInvadeTriggerConsumed = false;
    }

    public static void ApplyFrame(ulong frame)
    {
        EnsureActive();
        if (frame == 0 || frame != LogicTimeControlService.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicMovementRegionConstraintService.ApplyFrame failed: frame mismatch. requested={frame}, current={LogicTimeControlService.CurrentFrame}.");
        }
        if (!LogicPhaseCommandService.IsActive || !LogicPhaseCommandService.IsInitialized)
            throw new InvalidOperationException("LogicMovementRegionConstraintService.ApplyFrame requires initialized logic phase state.");

        if (HasTutorialStrongholdBoundary && LogicPhaseCommandService.CurrentPhase != GamePhase.Invade)
            s_TutorialStrongholdId = null;

        if (!s_TutorialInvadeTriggerBound
            || s_TutorialInvadeTriggerConsumed
            || LogicPhaseCommandService.CurrentPhase != GamePhase.Invade)
        {
            return;
        }

        IEntityContext player = EntityRegistry.Player;
        if (player == null)
            return;
        if (!player.Alive || player.Side != SideType.PlayerSide)
            throw new InvalidOperationException("Tutorial invade trigger requires a live player-side entity.");

        FixVector2 offset = player.PositionFixed - s_TutorialInvadeTriggerCenter;
        if (Fix64.Abs(offset.x) > s_TutorialInvadeTriggerHalfExtents.x
            || Fix64.Abs(offset.y) > s_TutorialInvadeTriggerHalfExtents.y)
        {
            return;
        }

        LogicStrongholdMap.EnsureInitialized();
        if (!LogicStrongholdMap.TryResolveStrongholdId(player.PositionFixed, out string strongholdId))
        {
            throw new InvalidOperationException(
                $"Tutorial invade trigger contains player outside every logic stronghold. raw=({player.PositionFixed.x.RawValue},{player.PositionFixed.y.RawValue}).");
        }

        s_TutorialStrongholdId = strongholdId;
        s_TutorialInvadeTriggerConsumed = true;
        TutorialStrongholdBoundaryActivated?.Invoke(strongholdId);
    }

    public static void SetTutorialStrongholdBoundary(string strongholdId)
    {
        EnsureActive();
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new ArgumentException("Tutorial stronghold id is empty.", nameof(strongholdId));

        LogicStrongholdMap.EnsureInitialized();
        LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId);
        s_TutorialStrongholdId = strongholdId;
    }

    public static void ClearTutorialStrongholdBoundary()
    {
        if (!IsActive)
        {
            s_TutorialStrongholdId = null;
            return;
        }

        s_TutorialStrongholdId = null;
    }

    public static FixVector2 ResolvePosition(
        IEntityContext entity,
        FixVector2 frameStart,
        FixVector2 candidate,
        out LogicMovementRegionConstraintFailure failure)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));

        EnsureActive();
        failure = LogicMovementRegionConstraintFailure.None;
        if (entity.Side != SideType.PlayerSide || candidate == frameStart)
            return candidate;

        if (IsTutorialStrongholdBoundaryBlocked(candidate))
        {
            failure = LogicMovementRegionConstraintFailure.TutorialStrongholdBoundary;
            return frameStart;
        }

        if (IsNonVisibleBlocked(entity, candidate))
        {
            failure = LogicMovementRegionConstraintFailure.NotVisible;
            return frameStart;
        }

        candidate = ResolveEnemyStrongholdCollision(entity, frameStart, candidate, out bool strongholdConstrained);
        if (strongholdConstrained)
            failure = LogicMovementRegionConstraintFailure.EnemyStronghold;

        return candidate;
    }

    public static bool IsPositionAllowed(
        IEntityContext entity,
        FixVector2 position,
        out LogicMovementRegionConstraintFailure failure)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));

        EnsureActive();
        failure = LogicMovementRegionConstraintFailure.None;
        if (entity.Side != SideType.PlayerSide)
            return true;

        if (IsTutorialStrongholdBoundaryBlocked(position))
        {
            failure = LogicMovementRegionConstraintFailure.TutorialStrongholdBoundary;
            return false;
        }

        if (IsNonVisibleBlocked(entity, position))
        {
            failure = LogicMovementRegionConstraintFailure.NotVisible;
            return false;
        }

        if (!LogicStrongholdMap.IsInitialized)
            return true;
        EnsureStrongholdPhaseReady();
        if (LogicPhaseCommandService.CurrentPhase == GamePhase.Invade)
            return true;

        Fix64 radius = ResolveCollisionRadius(entity);
        if (LogicStrongholdMap.IsCircleClearOfForeignStrongholds(
                position,
                radius,
                EntitySideHelper.PlayerFactionId))
        {
            return true;
        }

        failure = LogicMovementRegionConstraintFailure.EnemyStronghold;
        return false;
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add(IsActive);
        if (!IsActive)
            return;
        hasher.Add(HasTutorialStrongholdBoundary);
        if (HasTutorialStrongholdBoundary)
            hasher.Add(s_TutorialStrongholdId);
        hasher.Add(s_TutorialInvadeTriggerBound);
        if (s_TutorialInvadeTriggerBound)
        {
            hasher.Add(s_TutorialInvadeTriggerCenter.x.RawValue);
            hasher.Add(s_TutorialInvadeTriggerCenter.y.RawValue);
            hasher.Add(s_TutorialInvadeTriggerHalfExtents.x.RawValue);
            hasher.Add(s_TutorialInvadeTriggerHalfExtents.y.RawValue);
            hasher.Add(s_TutorialInvadeTriggerConsumed);
        }
    }

    private static FixVector2 ResolveEnemyStrongholdCollision(
        IEntityContext entity,
        FixVector2 frameStart,
        FixVector2 candidate,
        out bool constrained)
    {
        constrained = false;
        if (!LogicStrongholdMap.IsInitialized)
        {
            return candidate;
        }

        EnsureStrongholdPhaseReady();
        if (LogicPhaseCommandService.CurrentPhase == GamePhase.Invade)
            return candidate;

        Fix64 radius = ResolveCollisionRadius(entity);
        return LogicStrongholdMap.ResolveCircleMotionAvoidingForeignStrongholds(
            frameStart,
            candidate,
            radius,
            EntitySideHelper.PlayerFactionId,
            out constrained);
    }

    private static Fix64 ResolveCollisionRadius(IEntityContext entity)
    {
        Fix64 radius = DistanceUnitConverter.ConvertToWorld(
            entity.GetProperty(CreatureMainProperty.CollisionRadius));
        if (radius <= Fix64.Zero)
        {
            throw new InvalidOperationException(
                $"LogicMovementRegionConstraintService requires a positive collision radius for moving player entity {entity.LogicEntityId.Value}.");
        }

        return radius;
    }

    private static void EnsureStrongholdPhaseReady()
    {
        if (!LogicPhaseCommandService.IsActive || !LogicPhaseCommandService.IsInitialized)
        {
            throw new InvalidOperationException(
                "LogicMovementRegionConstraintService requires initialized logic phase state before resolving a stronghold move.");
        }
    }

    private static bool IsTutorialStrongholdBoundaryBlocked(FixVector2 candidate)
    {
        if (!HasTutorialStrongholdBoundary)
            return false;

        LogicStrongholdMap.EnsureInitialized();
        if (!LogicStrongholdMap.TryResolveStrongholdId(candidate, out string candidateStrongholdId))
            return false;
        if (string.Equals(candidateStrongholdId, s_TutorialStrongholdId, StringComparison.Ordinal))
            return false;

        return LogicStrongholdMap.GetOwnerFactionIdRequired(candidateStrongholdId)
               != EntitySideHelper.PlayerFactionId;
    }

    private static bool IsNonVisibleBlocked(IEntityContext entity, FixVector2 position)
    {
        if (entity is not IHeroLogicContext hero || !hero.IsGhostState)
            return false;

        if (LogicCardPlacementAuthority.IsActive && LogicCardPlacementAuthority.IsWorldBound)
            return !LogicCardPlacementAuthority.IsVisibleFromCurrentLogicRevealers(position);

        if (LogicFrameRuntime.IsTimelineRunning)
        {
            throw new InvalidOperationException(
                "LogicMovementRegionConstraintService requires bound fog authority while the logic timeline is running.");
        }

        return false;
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicMovementRegionConstraintService operation failed: service is not active.");
    }

    private static void ClearState()
    {
        s_TutorialStrongholdId = null;
        s_TutorialInvadeTriggerBound = false;
        s_TutorialInvadeTriggerCenter = FixVector2.Zero;
        s_TutorialInvadeTriggerHalfExtents = FixVector2.Zero;
        s_TutorialInvadeTriggerConsumed = false;
    }
}
