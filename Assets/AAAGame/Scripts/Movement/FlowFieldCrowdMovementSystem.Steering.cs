using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using AAAGame.FlowPath;
using UnityEngine;
using MainThreadFrameProfiler = UnityGameFramework.Runtime.MainThreadFrameProfiler;
using MainThreadPerfScope = UnityGameFramework.Runtime.MainThreadPerfScope;

public static partial class FlowFieldCrowdMovementSystem
{
    private static FixVector2 ClampFixedVelocity(FixVector2 velocity, Fix64 maxSpeed)
    {
        if (maxSpeed <= Fix64.Zero)
            return FixVector2.Zero;

        Fix64 squaredMagnitude = FixVector2.SqrMagnitude(velocity);
        Fix64 squaredMaxSpeed = maxSpeed * maxSpeed;
        return squaredMagnitude > squaredMaxSpeed
            ? velocity / Fix64.Sqrt(squaredMagnitude) * maxSpeed
            : velocity;
    }

    private static FixVector2 ScaleFixedDirectionToSpeed(FixVector2 direction, Fix64 speed)
    {
        if (direction == FixVector2.Zero || speed <= Fix64.Zero)
            return FixVector2.Zero;

        FixVector2 velocity = direction / Fix64.Sqrt(FixVector2.SqrMagnitude(direction)) * speed;
        return ClampFixedVelocity(velocity, speed);
    }

    private static FixVector2 ResolveFixedVelocityToTargetWithinTick(
        FixVector2 position,
        FixVector2 target,
        Fix64 maxSpeed)
    {
        FixVector2 displacement = target - position;
        if (displacement == FixVector2.Zero || maxSpeed <= Fix64.Zero)
            return FixVector2.Zero;
        Fix64 deltaTime = LogicFrameRuntime.FixedDeltaTime;
        if (deltaTime <= Fix64.Zero)
            throw new InvalidOperationException($"ResolveFixedVelocityToTargetWithinTick failed: fixed delta time is invalid raw={deltaTime.RawValue}.");
        Fix64 maximumTravelDistance = maxSpeed * deltaTime;
        if (FixVector2.SqrMagnitude(displacement) <= maximumTravelDistance * maximumTravelDistance)
            return displacement / deltaTime;
        return ScaleFixedDirectionToSpeed(displacement, maxSpeed);
    }

    private static long ResolveFunnelTriangleArea2Raw(
        FixVector2 apex,
        FixVector2 first,
        FixVector2 second)
    {
        FixVector2 fromApexToFirst = first - apex;
        FixVector2 fromApexToSecond = second - apex;
        return checked(
            checked(fromApexToFirst.x.RawValue * fromApexToSecond.y.RawValue)
            - checked(fromApexToFirst.y.RawValue * fromApexToSecond.x.RawValue));
    }

    private static bool HaveSameNonZeroRawSign(Fix64 left, Fix64 right)
    {
        return left.RawValue != 0
               && right.RawValue != 0
               && (left.RawValue > 0) == (right.RawValue > 0);
    }

    private static void ResolvePortalFunnelSegmentFixed(
        PathHandle handle,
        int portalPathIndex,
        out FixVector2 left,
        out FixVector2 right)
    {
        if (handle?.SectorIds == null
            || handle.PortalIds == null
            || portalPathIndex < 0
            || portalPathIndex >= handle.PortalIds.Length
            || portalPathIndex + 1 >= handle.SectorIds.Length)
        {
            throw new InvalidOperationException(
                $"ResolvePortalFunnelSegmentFixed failed: path index is invalid index={portalPathIndex}, handle={FormatPathHandle(handle)}.");
        }

        ResolvePortalFunnelSegmentFixed(
            handle.SectorIds,
            handle.PortalIds,
            portalPathIndex,
            out left,
            out right);
    }

    private static void ResolvePortalFunnelSegmentFixed(
        ImmutableRouteSequence sectorIds,
        ImmutableRouteSequence portalIds,
        int portalPathIndex,
        out FixVector2 left,
        out FixVector2 right)
    {
        if (sectorIds == null
            || portalIds == null
            || portalPathIndex < 0
            || portalPathIndex >= portalIds.Length
            || portalPathIndex + 1 >= sectorIds.Length)
        {
            throw new InvalidOperationException(
                $"ResolvePortalFunnelSegmentFixed failed: corridor index is invalid index={portalPathIndex}, sectors={sectorIds?.Length ?? -1}, portals={portalIds?.Length ?? -1}.");
        }

        int currentSectorId = sectorIds[portalPathIndex];
        int downstreamSectorId = sectorIds[portalPathIndex + 1];
        PortalData portal = GetPortalById(_world, portalIds[portalPathIndex]);
        if (GetOppositeSectorId(portal, currentSectorId) != downstreamSectorId)
        {
            throw new InvalidOperationException(
                $"ResolvePortalFunnelSegmentFixed failed: portal does not connect path sectors portal={portal.PortalId}, current={currentSectorId}, downstream={downstreamSectorId}.");
        }

        Vector2Int[] currentCells = GetPortalCellsForSector(portal, currentSectorId);
        Vector2Int[] downstreamCells = GetPortalCellsForSector(portal, downstreamSectorId);
        if (currentCells.Length == 0 || currentCells.Length != downstreamCells.Length)
        {
            throw new InvalidOperationException(
                $"ResolvePortalFunnelSegmentFixed failed: portal sides are invalid portal={portal.PortalId}, current={currentCells.Length}, downstream={downstreamCells.Length}.");
        }

        FixVector2 first = (_world.GridToWorldCenterFixed(currentCells[0].x, currentCells[0].y)
                            + _world.GridToWorldCenterFixed(downstreamCells[0].x, downstreamCells[0].y))
                           / (Fix64)2;
        int lastIndex = currentCells.Length - 1;
        FixVector2 last = (_world.GridToWorldCenterFixed(currentCells[lastIndex].x, currentCells[lastIndex].y)
                           + _world.GridToWorldCenterFixed(downstreamCells[lastIndex].x, downstreamCells[lastIndex].y))
                          / (Fix64)2;
        FixVector2 travel = _world.GridToWorldCenterFixed(downstreamCells[0].x, downstreamCells[0].y)
                            - _world.GridToWorldCenterFixed(currentCells[0].x, currentCells[0].y);
        FixVector2 center = (first + last) / (Fix64)2;
        if (ResolveFunnelTriangleArea2Raw(FixVector2.Zero, travel, first - center) >= 0L)
        {
            left = first;
            right = last;
        }
        else
        {
            left = last;
            right = first;
        }
    }

    private static FixVector2 ResolvePortalCorridorFunnelTargetFixed(
        PathHandle handle,
        FixVector2 position,
        int portalStartPathIndex,
        int portalEndPathIndex,
        FixVector2 terminal,
        out int cornerPortalPathIndex)
    {
        if (handle == null
            || handle.CurrentSectorIndex < 0
            || handle.CurrentSectorIndex >= handle.SectorIds.Length)
        {
            throw new InvalidOperationException(
                $"ResolvePortalCorridorFunnelTargetFixed failed: handle is invalid handle={FormatPathHandle(handle)}.");
        }

        FixVector2 apex = position;
        FixVector2 funnelLeft = apex;
        FixVector2 funnelRight = apex;
        if (portalStartPathIndex < handle.CurrentSectorIndex
            || portalStartPathIndex > portalEndPathIndex
            || portalEndPathIndex > handle.PortalIds.Length)
        {
            throw new InvalidOperationException(
                $"ResolvePortalCorridorFunnelTargetFixed failed: corridor range is invalid start={portalStartPathIndex}, end={portalEndPathIndex}, handle={FormatPathHandle(handle)}.");
        }
        int leftIndex = portalStartPathIndex;
        int rightIndex = portalStartPathIndex;
        for (int portalIndex = portalStartPathIndex; portalIndex <= portalEndPathIndex; portalIndex++)
        {
            FixVector2 portalLeft;
            FixVector2 portalRight;
            if (portalIndex == portalEndPathIndex)
            {
                portalLeft = terminal;
                portalRight = portalLeft;
            }
            else
            {
                ResolvePortalFunnelSegmentFixed(handle, portalIndex, out portalLeft, out portalRight);
            }

            if (ResolveFunnelTriangleArea2Raw(apex, funnelRight, portalRight) >= 0L)
            {
                if (funnelRight == apex
                    || ResolveFunnelTriangleArea2Raw(apex, funnelLeft, portalRight) < 0L)
                {
                    funnelRight = portalRight;
                    rightIndex = portalIndex;
                }
                else
                {
                    cornerPortalPathIndex = leftIndex;
                    return funnelLeft;
                }
            }

            if (ResolveFunnelTriangleArea2Raw(apex, funnelLeft, portalLeft) <= 0L)
            {
                if (funnelLeft == apex
                    || ResolveFunnelTriangleArea2Raw(apex, funnelRight, portalLeft) > 0L)
                {
                    funnelLeft = portalLeft;
                    leftIndex = portalIndex;
                }
                else
                {
                    cornerPortalPathIndex = rightIndex;
                    return funnelRight;
                }
            }
        }

        cornerPortalPathIndex = portalEndPathIndex;
        return terminal;
    }

    private static void CommitCurrentFunnelCorridor(PathHandle handle, FlowTileCacheKey key)
    {
        if (handle == null
            || handle.SectorIds == null
            || handle.PortalIds == null
            || key.GoalKind != TileGoalKind.Portal
            || handle.CurrentSectorIndex < 0
            || handle.CurrentSectorIndex >= handle.PortalIds.Length
            || handle.SectorIds[handle.CurrentSectorIndex] != key.SectorId
            || handle.PortalIds[handle.CurrentSectorIndex] != key.GoalId)
        {
            throw new InvalidOperationException(
                $"CommitCurrentFunnelCorridor failed: path does not match current tile key={FormatTileKey(key)}, handle={FormatPathHandle(handle)}.");
        }

        int sectorCount = handle.SectorIds.Length - handle.CurrentSectorIndex;
        int portalCount = handle.PortalIds.Length - handle.CurrentSectorIndex;
        handle.CommittedCorridorGoalX = handle.GoalX;
        handle.CommittedCorridorGoalY = handle.GoalY;
        handle.CommittedCorridorSectorIds = new int[sectorCount];
        handle.CommittedCorridorPortalIds = new int[portalCount];
        handle.SectorIds.CopyTo(handle.CurrentSectorIndex, handle.CommittedCorridorSectorIds, 0, sectorCount);
        handle.PortalIds.CopyTo(handle.CurrentSectorIndex, handle.CommittedCorridorPortalIds, 0, portalCount);
        handle.CommittedFunnelView = null;
    }

    private static void ClearCommittedCurrentTileAuthority(PathHandle handle)
    {
        if (handle == null)
            throw new ArgumentNullException(nameof(handle));

        handle.HasCommittedCurrentTileKey = false;
        handle.CommittedCurrentTileKey = default;
        handle.CommittedCorridorGoalX = 0;
        handle.CommittedCorridorGoalY = 0;
        handle.CommittedCorridorSectorIds = null;
        handle.CommittedCorridorPortalIds = null;
        handle.CommittedFunnelView = null;
    }

    private static void PromoteReadyCommittedCurrentTileAuthorities()
    {
        if (_world == null)
            throw new InvalidOperationException("PromoteReadyCommittedCurrentTileAuthorities failed: world is null.");

        for (int i = 0; i < OrderedAgentIds.Count; i++)
        {
            int agentId = OrderedAgentIds[i];
            if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent) || agent == null)
                throw new InvalidOperationException($"PromoteReadyCommittedCurrentTileAuthorities failed: ordered agent is missing id={agentId}.");
            if (!IsAgentInCurrentNavigationWorld(agent))
                continue;

            PathHandle handle = agent.NavState.PathHandle;
            if (handle == null || !handle.HasCommittedCurrentTileKey || handle.WorldVersion != _world.Version)
                continue;
            if (handle.SectorIds == null
                || handle.PortalIds == null
                || handle.CurrentSectorIndex < 0
                || handle.CurrentSectorIndex >= handle.SectorIds.Length
                || handle.CurrentSectorIndex >= handle.PortalIds.Length)
            {
                throw new InvalidOperationException(
                    $"PromoteReadyCommittedCurrentTileAuthorities failed: committed path is invalid agent={agentId}, handle={FormatPathHandle(handle)}.");
            }

            FlowTileCacheKey exactKey = CreateExactTileCacheKeyForPathSegment(
                handle,
                handle.CurrentSectorIndex,
                handle.GoalX,
                handle.GoalY,
                agent.AgentTypeId,
                out TileGoalKind goalKind,
                out _);
            if (exactKey.Equals(handle.CommittedCurrentTileKey))
                continue;
            if (goalKind != TileGoalKind.Portal
                || exactKey.SectorId != handle.CommittedCurrentTileKey.SectorId
                || exactKey.GoalId != handle.CommittedCurrentTileKey.GoalId)
            {
                throw new InvalidOperationException(
                    $"PromoteReadyCommittedCurrentTileAuthorities failed: exact replacement changes the committed portal agent={agentId}, committed={FormatTileKey(handle.CommittedCurrentTileKey)}, exact={FormatTileKey(exactKey)}.");
            }
            if (!DeterministicFlowTileCache.ContainsKey(exactKey))
                continue;
            if (!FlowTileCache.ContainsKey(exactKey))
            {
                throw new InvalidOperationException(
                    $"PromoteReadyCommittedCurrentTileAuthorities failed: deterministic replacement has no diagnostic mirror key={FormatTileKey(exactKey)}.");
            }

            CommitCurrentFunnelCorridor(handle, exactKey);
            handle.HasCommittedCurrentTileKey = true;
            handle.CommittedCurrentTileKey = exactKey;
        }
    }

    private static PathHandle BuildCommittedFunnelCorridorFixed(PathHandle handle, FlowTileCacheKey key)
    {
        if (handle == null
            || !handle.HasCommittedCurrentTileKey
            || !handle.CommittedCurrentTileKey.Equals(key)
            || handle.CommittedCorridorSectorIds == null
            || handle.CommittedCorridorPortalIds == null
            || handle.CommittedCorridorSectorIds.Length != handle.CommittedCorridorPortalIds.Length + 1
            || handle.CommittedCorridorPortalIds.Length == 0
            || handle.CommittedCorridorSectorIds[0] != key.SectorId
            || handle.CommittedCorridorPortalIds[0] != key.GoalId)
        {
            throw new InvalidOperationException(
                $"BuildCommittedFunnelCorridorFixed failed: committed corridor is absent or inconsistent key={FormatTileKey(key)}, handle={FormatPathHandle(handle)}.");
        }

        if (handle.CommittedFunnelView == null)
        {
            handle.CommittedFunnelView = new PathHandle
            {
                WorldVersion = key.WorldVersion,
                GoalX = handle.CommittedCorridorGoalX,
                GoalY = handle.CommittedCorridorGoalY,
                SectorIds = handle.CommittedCorridorSectorIds,
                PortalIds = handle.CommittedCorridorPortalIds,
                CurrentSectorIndex = 0,
                BuildSource = "committedCorridor"
            };
        }
        return handle.CommittedFunnelView;
    }

    private static bool DoesPortalLookaheadRayCrossCurrentApertureFixed(
        PathHandle handle,
        FixVector2 position,
        FixVector2 target)
    {
        int portalPathIndex = handle.CurrentSectorIndex;
        ResolvePortalFunnelSegmentFixed(handle, portalPathIndex, out FixVector2 left, out FixVector2 right);
        PortalData portal = GetPortalById(_world, handle.PortalIds[portalPathIndex]);
        FixVector2 displacement = target - position;
        int currentSectorId = handle.SectorIds[portalPathIndex];
        int downstreamSectorId = handle.SectorIds[portalPathIndex + 1];
        Vector2Int[] currentCells = GetPortalCellsForSector(portal, currentSectorId);
        Vector2Int[] downstreamCells = GetPortalCellsForSector(portal, downstreamSectorId);
        FixVector2 travel = _world.GridToWorldCenterFixed(downstreamCells[0].x, downstreamCells[0].y)
                            - _world.GridToWorldCenterFixed(currentCells[0].x, currentCells[0].y);

        Fix64 normalDisplacement = portal.IsVerticalBoundary ? displacement.x : displacement.y;
        Fix64 normalTravel = portal.IsVerticalBoundary ? travel.x : travel.y;
        if (!HaveSameNonZeroRawSign(normalDisplacement, normalTravel))
            return false;

        Fix64 plane = portal.IsVerticalBoundary ? left.x : left.y;
        Fix64 normalPosition = portal.IsVerticalBoundary ? position.x : position.y;
        Fix64 planeDelta = plane - normalPosition;
        if (normalDisplacement > Fix64.Zero)
        {
            if (planeDelta < Fix64.Zero || planeDelta > normalDisplacement)
                return false;
        }
        else if (planeDelta > Fix64.Zero || planeDelta < normalDisplacement)
        {
            return false;
        }

        Fix64 tangentPosition = portal.IsVerticalBoundary ? position.y : position.x;
        Fix64 tangentDisplacement = portal.IsVerticalBoundary ? displacement.y : displacement.x;
        Fix64 leftTangent = portal.IsVerticalBoundary ? left.y : left.x;
        Fix64 rightTangent = portal.IsVerticalBoundary ? right.y : right.x;
        long intersectionNumerator = checked(
            checked((tangentPosition.RawValue - leftTangent.RawValue) * normalDisplacement.RawValue)
            + checked(tangentDisplacement.RawValue * planeDelta.RawValue));
        long rightNumerator = checked(
            (rightTangent.RawValue - leftTangent.RawValue) * normalDisplacement.RawValue);
        long minimumNumerator = Math.Min(0L, rightNumerator);
        long maximumNumerator = Math.Max(0L, rightNumerator);
        return intersectionNumerator >= minimumNumerator
               && intersectionNumerator <= maximumNumerator;
    }

    private static bool TryResolvePortalCorridorFunnelVelocityFixed(
        AgentRuntimeData agent,
        PathHandle handle,
        FlowTileCacheKey key,
        FixVector2 position,
        Fix64 maxSpeed,
        bool currentPortalReached,
        out FixVector2 velocity,
        out string diagnostic)
    {
        velocity = FixVector2.Zero;
        diagnostic = null;
        // The current portal tile is the current-side field. Funnel lookahead
        // is legal only after the caller has switched to the next sector tile.
        currentPortalReached = false;
        if (handle?.PortalIds == null
            || handle.CurrentSectorIndex < 0
            || handle.CurrentSectorIndex >= handle.PortalIds.Length)
        {
            return false;
        }
        PathHandle committedCorridor = BuildCommittedFunnelCorridorFixed(handle, key);
        int portalEndPathIndex = committedCorridor.PortalIds.Length;
        FixVector2 terminal = agent.NavState.CommittedMovingTargetId != int.MinValue
            ? agent.NavState.StableGoalWorldFixed
            : agent.NavState.LastGoalWorldFixed;

        string portalLookahead = "not-requested";
        int portalStartPathIndex = committedCorridor.CurrentSectorIndex;
        if (currentPortalReached)
        {
            FixVector2 lookaheadTarget = ResolvePortalCorridorFunnelTargetFixed(
                committedCorridor,
                position,
                committedCorridor.CurrentSectorIndex + 1,
                portalEndPathIndex,
                terminal,
                out int lookaheadCornerPortalPathIndex);
            if (DoesPortalLookaheadRayCrossCurrentApertureFixed(committedCorridor, position, lookaheadTarget))
            {
                portalStartPathIndex = committedCorridor.CurrentSectorIndex + 1;
                portalLookahead = "accepted";
            }
            else
            {
                portalLookahead = "rejected";
            }
        }

        Fix64 deltaTime = LogicFrameRuntime.FixedDeltaTime;
        Fix64 remainingDistance = maxSpeed * deltaTime;
        if (remainingDistance <= Fix64.Zero)
        {
            diagnostic = "corridor-funnel-rejected reason=non-positive-travel-distance";
            return false;
        }

        FixVector2 integratedPosition = position;
        int consumedCornerCount = 0;
        int finalCornerPortalPathIndex = portalStartPathIndex;
        while (remainingDistance > Fix64.Zero)
        {
            FixVector2 target = ResolvePortalCorridorFunnelTargetFixed(
                committedCorridor,
                integratedPosition,
                portalStartPathIndex,
                portalEndPathIndex,
                terminal,
                out int cornerPortalPathIndex);
            finalCornerPortalPathIndex = cornerPortalPathIndex;
            FixVector2 segment = target - integratedPosition;
            Fix64 segmentLengthSquared = FixVector2.SqrMagnitude(segment);
            if (segmentLengthSquared == Fix64.Zero)
            {
                if (cornerPortalPathIndex >= portalEndPathIndex)
                    break;
                portalStartPathIndex = cornerPortalPathIndex + 1;
                consumedCornerCount++;
                continue;
            }
            if (!TryValidatePortalCorridorFunnelSegmentFixed(
                    agent,
                    integratedPosition,
                    segment,
                    out string segmentFailure))
            {
                diagnostic =
                    $"corridor-funnel-rejected reason={segmentFailure} portalLookahead={portalLookahead} cornerPathIndex={cornerPortalPathIndex} " +
                    $"targetRaw=({target.x.RawValue},{target.y.RawValue})";
                return false;
            }

            Fix64 segmentLength = Fix64.Sqrt(segmentLengthSquared);
            if (segmentLength >= remainingDistance)
            {
                integratedPosition += segment / segmentLength * remainingDistance;
                remainingDistance = Fix64.Zero;
                break;
            }

            integratedPosition = target;
            remainingDistance -= segmentLength;
            consumedCornerCount++;
            if (cornerPortalPathIndex >= portalEndPathIndex)
                break;
            portalStartPathIndex = cornerPortalPathIndex + 1;
        }

        FixVector2 integratedDisplacement = integratedPosition - position;
        if (FixVector2.SqrMagnitude(integratedDisplacement) == Fix64.Zero)
        {
            diagnostic =
                $"corridor-funnel-rejected reason=consumed-terminal portalLookahead={portalLookahead} cornerPathIndex={finalCornerPortalPathIndex}";
            return false;
        }
        if (!_world.WorldToGridFixed(integratedPosition, out int prospectiveX, out int prospectiveY)
            || !_world.TryGetSectorId(prospectiveX, prospectiveY, out int prospectiveSectorId))
        {
            diagnostic = "corridor-funnel-rejected reason=prospective-position-outside-world";
            return false;
        }
        int prospectivePortalStartPathIndex = -1;
        for (int i = 0; i < committedCorridor.SectorIds.Length; i++)
        {
            if (committedCorridor.SectorIds[i] != prospectiveSectorId)
                continue;
            prospectivePortalStartPathIndex = Math.Min(i, portalEndPathIndex);
            break;
        }
        if (prospectivePortalStartPathIndex < 0)
        {
            diagnostic =
                $"corridor-funnel-rejected reason=prospective-sector-outside-corridor sector={prospectiveSectorId} " +
                $"cell=({prospectiveX},{prospectiveY})";
            return false;
        }
        if (!currentPortalReached && prospectiveSectorId == key.SectorId)
        {
            if (!DeterministicFlowTileCache.TryGetValue(key, out FlowTileCacheEntry prospectiveCurrentTile))
                throw new InvalidOperationException($"TryResolvePortalCorridorFunnelVelocityFixed failed: prospective current tile is missing key={FormatTileKey(key)}.");
            if (IndexOfGoalCell(prospectiveCurrentTile.GoalCells, prospectiveX, prospectiveY) >= 0)
            {
                diagnostic =
                    $"corridor-funnel-rejected reason=prospective-current-portal-seed-not-crossed " +
                    $"cell=({prospectiveX},{prospectiveY}) key={FormatTileKey(key)}";
                return false;
            }
        }
        FixVector2 prospectiveTarget = ResolvePortalCorridorFunnelTargetFixed(
            committedCorridor,
            integratedPosition,
            prospectivePortalStartPathIndex,
            portalEndPathIndex,
            terminal,
            out int prospectiveCornerPortalPathIndex);
        FixVector2 prospectiveSegment = prospectiveTarget - integratedPosition;
        if (FixVector2.SqrMagnitude(prospectiveSegment) > Fix64.Zero
            && !TryValidatePortalCorridorFunnelSegmentFixed(
                agent,
                integratedPosition,
                prospectiveSegment,
                out string prospectiveFailure))
        {
            diagnostic =
                $"corridor-funnel-rejected reason=prospective-{prospectiveFailure} portalLookahead={portalLookahead} " +
                $"cornerPathIndex={prospectiveCornerPortalPathIndex} targetRaw=({prospectiveTarget.x.RawValue},{prospectiveTarget.y.RawValue})";
            return false;
        }
        if (!TryValidatePortalCorridorFunnelSegmentFixed(
                agent,
                position,
                integratedDisplacement,
                out string integratedFailure))
        {
            diagnostic =
                $"corridor-funnel-rejected reason=integrated-{integratedFailure} portalLookahead={portalLookahead} cornerPathIndex={finalCornerPortalPathIndex} " +
                $"targetRaw=({integratedPosition.x.RawValue},{integratedPosition.y.RawValue})";
            return false;
        }

        velocity = integratedDisplacement / deltaTime;
        diagnostic =
            $"corridor-funnel portalLookahead={portalLookahead} cornerPathIndex={finalCornerPortalPathIndex} consumedCorners={consumedCornerCount} " +
            $"targetRaw=({integratedPosition.x.RawValue},{integratedPosition.y.RawValue}) corridor={FormatPathHandle(committedCorridor)}";
        return true;
    }

    private static bool TryValidatePortalCorridorFunnelSegmentFixed(
        AgentRuntimeData agent,
        FixVector2 position,
        FixVector2 displacement,
        out string failure)
    {
        FixVector2 target = position + displacement;
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long losStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool hasLineOfSight = HasFixedGridLineOfSight(_world, position, target, allowTargetSoftCost: true);
        if (profile)
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowSteeringFunnelGridLos, Stopwatch.GetTimestamp() - losStartTicks);
        if (!hasLineOfSight)
        {
            failure = "grid-los-failed";
            return false;
        }
        long sweepStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (!LogicStaticCollisionShadowService.TrySolveFixed(
                agent.AgentTypeId,
                position,
                displacement,
                agent.RadiusFixed,
                out LogicStaticCollisionShadowResult solveResult))
        {
            throw new InvalidOperationException(
                $"TryValidatePortalCorridorFunnelSegmentFixed failed: static collision world is unavailable for agentType={agent.AgentTypeId}.");
        }
        if (profile)
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowSteeringFunnelStaticSweep, Stopwatch.GetTimestamp() - sweepStartTicks);
        if (!solveResult.SolveResult.Success)
        {
            throw new InvalidOperationException(
                $"TryValidatePortalCorridorFunnelSegmentFixed failed: direct corridor query failed for agent={agent.Id}, failure={solveResult.SolveResult.Failure}.");
        }
        if (solveResult.SolveResult.ResolvedDisplacement != displacement)
        {
            failure =
                $"static-sweep-clipped desiredRaw=({displacement.x.RawValue},{displacement.y.RawValue}) " +
                $"resolvedRaw=({solveResult.SolveResult.ResolvedDisplacement.x.RawValue},{solveResult.SolveResult.ResolvedDisplacement.y.RawValue})";
            return false;
        }
        failure = null;
        return true;
    }

    private static FixVector2 ResolveDeterministicFlowVelocityFixed(
        IEntityContext self,
        AgentRuntimeData agent,
        Fix64 maxSpeed)
    {
        AgentNavState nav = agent.NavState;
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        int flowFrame = GetFrameCount();
        FixVector2 recentFlowVelocity = nav.LastFixedFlowFrame == flowFrame
                                        || nav.LastFixedFlowFrame == flowFrame - 1
            ? nav.LastFixedFlowVelocity
            : FixVector2.Zero;
        nav.LastFixedFlowFrame = flowFrame;
        nav.LastFixedFlowVelocity = FixVector2.Zero;
        if (!nav.HasGoal)
            throw new InvalidOperationException("ResolveDeterministicFlowVelocityFixed failed: resolved navigation goal is missing.");
        FixVector2 resolvedNavigationGoal = nav.LastGoalWorldFixed;
        if (_world == null)
            throw new InvalidOperationException("ResolveDeterministicFlowVelocityFixed failed: world is null.");
        PathHandle handle = agent.NavState.PathHandle;
        if (handle == null || handle.SectorIds == null || handle.SectorIds.Length == 0)
        {
            nav.LastFixedFlowResult = "handle-empty";
            return FixVector2.Zero;
        }
        if (handle.CurrentSectorIndex < 0 || handle.CurrentSectorIndex >= handle.SectorIds.Length)
            throw new InvalidOperationException("ResolveDeterministicFlowVelocityFixed failed: path sector index is invalid.");

        bool requiresPreparedSnapshot = RequiresPreparedNavigationSnapshot();
        FlowTileCacheKey key = !requiresPreparedSnapshot && handle.HasCommittedCurrentTileKey
            ? CreateExactTileCacheKeyForPathSegment(
                handle,
                handle.CurrentSectorIndex,
                handle.GoalX,
                handle.GoalY,
                agent.AgentTypeId,
                out _,
                out _)
            : CreateTileCacheKeyForPathSegment(
                handle,
                handle.CurrentSectorIndex,
                handle.GoalX,
                handle.GoalY,
                agent.AgentTypeId,
                out _,
                out _);
        TileGoalKind goalKind = key.GoalKind;

        FixVector2 position = agent.PositionFixed;
        if (!_world.WorldToGridFixed(position, out int worldX, out int worldY))
        {
            throw new InvalidOperationException(
                $"ResolveDeterministicFlowVelocityFixed failed: position is outside world. entity={self.LogicEntityId.Value}, raw=({position.x.RawValue},{position.y.RawValue}).");
        }

        bool hasCachedTile = DeterministicFlowTileCache.TryGetValue(key, out FlowTileCacheEntry tile);
        bool hasPendingRuntimeDirty = HasPendingRuntimeDirty(_activeWorldState);
        if (hasPendingRuntimeDirty && FixVector2.SqrMagnitude(recentFlowVelocity) == Fix64.Zero)
        {
            nav.LastFixedFlowResult = $"runtime-dirty-no-committed-flow key={FormatTileKey(key)}";
            return FixVector2.Zero;
        }
        if (hasPendingRuntimeDirty
            && !hasCachedTile
            && FixVector2.SqrMagnitude(recentFlowVelocity) > Fix64.Zero)
        {
            FixVector2 continuedVelocity = ScaleFixedDirectionToSpeed(recentFlowVelocity, maxSpeed);
            nav.LastFixedFlowVelocity = continuedVelocity;
            nav.LastFixedFlowResult = $"tile-pending-last-flow key={FormatTileKey(key)}";
            return continuedVelocity;
        }

        if (!requiresPreparedSnapshot)
        {
            CommitSteeringReadDomainFlowTilePayloadsForCurrentTick(
                agent,
                maxSpeed * LogicFrameRuntime.FixedDeltaTime);
            hasCachedTile = DeterministicFlowTileCache.TryGetValue(key, out tile);
        }

        FixVector2 toNavigationGoal = resolvedNavigationGoal - position;
        bool directFinalBindingClear = false;
        bool hasLocalMovingTargetBinding = nav.CommittedMovingTargetId != int.MinValue;
        // A moving target does not make a cross-sector portal tile a direct-goal
        // query.  The portal field is the authority until the agent reaches the
        // final-goal sector; running a full static sweep + grid LOS here would
        // duplicate MoveResolve work for every chasing agent on every Tick.
        bool isFinalGoalSector = goalKind == TileGoalKind.FinalGoal;
        if (isFinalGoalSector
            && FixVector2.SqrMagnitude(toNavigationGoal) > Fix64.Zero
            && (!hasPendingRuntimeDirty || !hasCachedTile))
        {
            directFinalBindingClear = HasDirectFinalBindingPathFixed(
                agent,
                position,
                resolvedNavigationGoal,
                recordProfiler: true);
            if (directFinalBindingClear)
            {
                FixVector2 directVelocity = ResolveFixedVelocityToTargetWithinTick(
                    position,
                    resolvedNavigationGoal,
                    maxSpeed);
                nav.LastFixedFlowVelocity = directVelocity;
                nav.LastFixedFlowResult =
                    $"direct-static-clear cell=({worldX},{worldY}) goalRaw=({resolvedNavigationGoal.x.RawValue},{resolvedNavigationGoal.y.RawValue}) key={FormatTileKey(key)}";
                return directVelocity;
            }
        }
        if (!hasCachedTile)
        {
            if (requiresPreparedSnapshot)
            {
                if (goalKind == TileGoalKind.Portal)
                {
                    FixVector2 pendingPortalVelocity = ResolvePendingPortalVelocityFixed(
                        agent,
                        key,
                        position,
                        worldX,
                        worldY,
                        maxSpeed,
                        out long portalAccessCost,
                        out int portalDirectionIndex,
                        out int portalSlotIndex,
                        out string portalTraversalDiagnostic);
                    nav.LastFixedFlowVelocity = pendingPortalVelocity;
                    nav.LastFixedFlowResult =
                        $"tile-pending-portal-access cost={portalAccessCost} direction={portalDirectionIndex} " +
                        $"slot={portalSlotIndex} key={FormatTileKey(key)} {portalTraversalDiagnostic}";
                    return pendingPortalVelocity;
                }
                nav.LastFixedFlowVelocity = FixVector2.Zero;
                nav.LastFixedFlowResult = $"pending-navigation-current-tile key={FormatTileKey(key)}";
                return FixVector2.Zero;
            }
            // The worker owns the build until it publishes an immutable tile.
            // A steering read must never synchronously complete the worker.
            nav.LastFixedFlowResult = $"pending-navigation-current-tile key={FormatTileKey(key)}";
            return FixVector2.Zero;
        }
        if (!HasDeterministicIntegrationPayload(tile)
            || tile.DeterministicFlowDirectionIndices == null
            || tile.DeterministicFlowDirectionIndices.Length != tile.Width * tile.Height)
        {
            throw new InvalidOperationException(
                $"ResolveDeterministicFlowVelocityFixed failed: deterministic tile payload is invalid key={FormatTileKey(key)}.");
        }
        if (!IsInsideSector(tile, worldX, worldY))
        {
            nav.LastFixedFlowResult = $"outside-tile cell=({worldX},{worldY}) tile=({tile.StartX},{tile.StartY},{tile.Width},{tile.Height})";
            return FixVector2.Zero;
        }

        int localIndex = tile.GetLocalIndex(worldX, worldY);
        if (GetDeterministicIntegrationCost(tile, localIndex) == int.MaxValue)
        {
            nav.LastFixedFlowResult = $"unreachable cell=({worldX},{worldY}) key={FormatTileKey(key)}";
            return FixVector2.Zero;
        }
        int recommendedPortalSlotIndexForDiagnostic = -1;
        int selectedPortalSlotIndexForDiagnostic = -1;
        if (goalKind == TileGoalKind.Portal)
        {
            if (!handle.HasCommittedCurrentTileKey || !handle.CommittedCurrentTileKey.Equals(key))
                CommitCurrentFunnelCorridor(handle, key);
            handle.HasCommittedCurrentTileKey = true;
            handle.CommittedCurrentTileKey = key;
            if (tile.DeterministicPortalTargetSlotIndices == null
                || tile.DeterministicPortalTargetSlotIndices.Length != tile.Width * tile.Height)
            {
                throw new InvalidOperationException(
                    $"ResolveDeterministicFlowVelocityFixed failed: portal target slots are invalid key={FormatTileKey(key)}.");
            }

            int recommendedPortalSlotIndex = tile.DeterministicPortalTargetSlotIndices[localIndex] - 1;
            PortalData portal = GetPortalById(_world, key.GoalId);
            Vector2Int[] oppositeCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, key.SectorId));
            if (recommendedPortalSlotIndex < 0 || recommendedPortalSlotIndex >= oppositeCells.Length)
            {
                throw new InvalidOperationException(
                    $"ResolveDeterministicFlowVelocityFixed failed: portal target slot is invalid slot={recommendedPortalSlotIndex}, key={FormatTileKey(key)}.");
            }

            long portalStateStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            int selectedPortalSlotIndex = ResolveStablePortalTraversalSlotIndex(
                nav,
                key,
                recommendedPortalSlotIndex,
                oppositeCells.Length,
                recommendationFromCommittedTile: true);
            if (profile)
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowSteeringPortalState, Stopwatch.GetTimestamp() - portalStateStartTicks);
            recommendedPortalSlotIndexForDiagnostic = recommendedPortalSlotIndex;
            selectedPortalSlotIndexForDiagnostic = selectedPortalSlotIndex;
        }
        string refinementDiagnostic = null;
        try
        {
            if (goalKind == TileGoalKind.Portal && !hasPendingRuntimeDirty)
            {
            long funnelStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            if (TryResolvePortalCorridorFunnelVelocityFixed(
                        agent,
                        handle,
                        key,
                        position,
                        maxSpeed,
                        // A portal tile owns the current-side aperture only. Reaching
                        // its seed cell is not crossing the portal plane; downstream
                        // funnel lookahead becomes authoritative after the sector
                        // transition and the next tile is selected.
                        currentPortalReached: false,
                        out FixVector2 corridorVelocity,
                        out string corridorDiagnostic))
                {
                    if (profile)
                        MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowSteeringFunnel, Stopwatch.GetTimestamp() - funnelStartTicks);
                    nav.LastFixedFlowVelocity = corridorVelocity;
                    nav.LastFixedFlowResult = corridorDiagnostic + "/steeringAuthority=committed-corridor-potential";
                    return corridorVelocity;
                }
                if (profile)
                    MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowSteeringFunnel, Stopwatch.GetTimestamp() - funnelStartTicks);
                refinementDiagnostic = corridorDiagnostic;
            }
        }
        catch (PendingNavigationTileException exception)
        {
            // A not-yet-committed lookahead tile only disables funnel refinement.
            // The committed current portal tile remains the sole local steering
            // authority until that lookahead payload is available.
            refinementDiagnostic = $"pending-navigation-corridor-tile {exception.Message}";
        }
        byte directionIndex = tile.DeterministicFlowDirectionIndices[localIndex];
        if (directionIndex == 0)
        {
            if (goalKind != TileGoalKind.FinalGoal)
            {
                FixVector2 portalHandoffVelocity = ResolvePortalGoalCellVelocityFixed(
                    tile,
                    position,
                    worldX,
                    worldY,
                    maxSpeed);
                nav.LastFixedFlowVelocity = portalHandoffVelocity;
                nav.LastFixedFlowResult = $"portal-handoff cell=({worldX},{worldY}) key={FormatTileKey(key)}";
                return portalHandoffVelocity;
            }
            FixVector2 terminalGoal = hasLocalMovingTargetBinding
                ? nav.StableGoalWorldFixed
                : resolvedNavigationGoal;
            FixVector2 toGoal = terminalGoal - position;
            FixVector2 finalGoalVelocity = FixVector2.SqrMagnitude(toGoal) > Fix64.Zero
                ? ResolveFixedVelocityToTargetWithinTick(position, terminalGoal, maxSpeed)
                : FixVector2.Zero;
            nav.LastFixedFlowVelocity = finalGoalVelocity;
            nav.LastFixedFlowResult = "final-goal";
            return finalGoalVelocity;
        }

        int offsetIndex = directionIndex - 1;
        if (offsetIndex < 0 || offsetIndex >= NeighborOffsetX.Length)
            throw new InvalidOperationException($"ResolveDeterministicFlowVelocityFixed failed: invalid direction {directionIndex}.");
        long gradientX;
        long gradientY;
        FixVector2 direction;
        FixVector2 result;
        int integrationSubsteps;
        int domainBoundarySubsteps;
        int sampledTileTransitions;
        string integrationTrace;
        try
        {
            long gradientStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            direction = ResolveSpatiallyInterpolatedDeterministicGradientFixed(
                handle,
                agent,
                tile,
                ResolveSteeringReadCorridorTileIndex(handle, agent, tile),
                position,
                worldX,
                worldY,
                localIndex,
                out gradientX,
                out gradientY);
            if (profile)
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowSteeringGradient, Stopwatch.GetTimestamp() - gradientStartTicks);
            if (direction == FixVector2.Zero)
                direction = new FixVector2(NeighborOffsetX[offsetIndex], NeighborOffsetY[offsetIndex]);
            long integrationStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            result = ResolveSpatiallyIntegratedFieldVelocityFixed(
                handle,
                agent,
                tile,
                null,
                null,
                position,
                direction,
                maxSpeed,
                out integrationSubsteps,
                out domainBoundarySubsteps,
                out sampledTileTransitions,
                out integrationTrace);
            if (profile)
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowSteeringIntegration, Stopwatch.GetTimestamp() - integrationStartTicks);
        }
        catch (PendingNavigationTileException exception)
        {
            nav.LastFixedFlowVelocity = FixVector2.Zero;
            nav.LastFixedFlowResult = $"pending-navigation-corridor-tile {exception.Message}";
            return FixVector2.Zero;
        }
        nav.LastFixedFlowVelocity = result;
        long diagnosticsStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (IsMovementDiagnosticsEnabled())
        {
            nav.LastFixedFlowResult =
                $"direction={directionIndex}/gradient=({gradientX},{gradientY})/gradientFixedRaw=({direction.x.RawValue},{direction.y.RawValue})" +
                $"/integrationSubsteps={integrationSubsteps}" +
                $"/domainBoundarySubsteps={domainBoundarySubsteps}" +
                $"/sampledTileTransitions={sampledTileTransitions}" +
                $"/integrationTrace={integrationTrace}" +
                $"/cell=({worldX},{worldY})/cost={GetDeterministicIntegrationCost(tile, localIndex)}" +
                $"/portalSlot={recommendedPortalSlotIndexForDiagnostic}:{selectedPortalSlotIndexForDiagnostic}" +
                $"/portalGoals={(goalKind == TileGoalKind.Portal ? FormatGoalCells(tile.GoalCells) : "n/a")}" +
                $"/directFinalBindingClear={directFinalBindingClear}" +
                $"/refinementAttempt={refinementDiagnostic ?? "not-requested"}" +
                "/steeringAuthority=committed-corridor-potential";
        }
        else
        {
            nav.LastFixedFlowResult = "flow-field";
        }
        if (profile)
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowSteeringDiagnostics, Stopwatch.GetTimestamp() - diagnosticsStartTicks);
        return result;
    }

    private static void CommitSteeringReadDomainFlowTilePayloadsForCurrentTick(
        AgentRuntimeData agent,
        Fix64 maximumTravelDistance)
    {
#if UNITY_EDITOR
        if (LogicFrameRuntime.IsTimelineRunning || _editorTestRequirePreparedNavigationSnapshot)
            throw new InvalidOperationException("Editor direct flow-tile commit cannot run while prepared navigation snapshots are required.");
        if (agent?.NavState?.PathHandle == null)
            throw new InvalidOperationException("CommitSteeringReadDomainFlowTilePayloadsForCurrentTick failed: agent path is missing.");
        if (maximumTravelDistance < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumTravelDistance), maximumTravelDistance, "Maximum travel distance cannot be negative.");

        EnqueueSteeringReadDomainFlowTileBuilds(agent, maximumTravelDistance);
        PathHandle handle = agent.NavState.PathHandle;
        ResolveSteeringReadCorridor(
            handle,
            out ImmutableRouteSequence sectorIds,
            out ImmutableRouteSequence portalIds,
            out int startIndex,
            out int goalX,
            out int goalY,
            out bool usesCommittedCorridor);
        int endIndex = ResolveSteeringReadDomainEndIndex(
            agent,
            sectorIds,
            portalIds,
            startIndex,
            maximumTravelDistance);
        int committedCount = 0;
        PathHandle directBuildSnapshot = null;
        long commitStartTicks = Stopwatch.GetTimestamp();
        for (int index = endIndex; index >= startIndex; index--)
        {
            FlowTileCacheKey key = CreateSteeringReadDomainTileKey(
                handle,
                sectorIds,
                portalIds,
                index,
                goalX,
                goalY,
                agent.AgentTypeId,
                usesCommittedCorridor);
            if (DeterministicFlowTileCache.ContainsKey(key))
                continue;
            if (FindPendingFlowTileBuildJob(key) == null)
            {
                directBuildSnapshot ??= CreateSteeringReadDomainBuildSnapshot(
                    handle,
                    sectorIds,
                    portalIds,
                    startIndex,
                    goalX,
                    goalY,
                    usesCommittedCorridor);
                EnqueueFlowTileBuildJob(
                    directBuildSnapshot,
                    index,
                    goalX,
                    goalY,
                    agent.AgentTypeId);
            }
            committedCount += CommitEditorDirectFlowTilePayload(key);
        }
        _perf.RequiredFlowTileCommits += committedCount;
        _perf.FlowTileQueueTicks += Stopwatch.GetTimestamp() - commitStartTicks;
        if (committedCount > 0)
        {
            PromoteReadyCommittedCurrentTileAuthorities();
            RefreshFlowTileReferenceCounts();
            TrimTileCache();
        }
#else
        throw new InvalidOperationException("Direct steering flow-tile commit is unavailable outside Editor tests.");
#endif
    }

#if UNITY_EDITOR
    private static int CommitEditorDirectFlowTilePayload(FlowTileCacheKey requiredKey)
    {
        LinkedListNode<FlowTileBuildJob> requiredNode = FindPendingFlowTileBuildJob(requiredKey);
        if (requiredNode == null)
        {
            throw new InvalidOperationException(
                $"CommitEditorDirectFlowTilePayload found neither committed nor pending tile key={FormatTileKey(requiredKey)}.");
        }

        FlowTileBuildJob requiredJob = requiredNode.Value;
        if (IsFlowTileBuildJobStale(requiredJob))
            throw new InvalidOperationException($"CommitEditorDirectFlowTilePayload found a stale tile key={FormatTileKey(requiredKey)}.");

        int remainingOperations = int.MaxValue;
        while (!AdvanceDeterministicFlowTileBuildJob(requiredJob, ref remainingOperations))
        {
            if (requiredJob.HasIntegrationDirectionsHandle)
                requiredJob.IntegrationDirectionsHandle.Complete();
            if (remainingOperations <= 0)
                throw new InvalidOperationException($"CommitEditorDirectFlowTilePayload exhausted operation range key={FormatTileKey(requiredKey)}.");
        }
        FlowTileBuildQueue.Remove(requiredNode);
        if (!PendingFlowTileBuildJobs.Remove(requiredKey))
            throw new InvalidOperationException($"CommitEditorDirectFlowTilePayload lost pending index key={FormatTileKey(requiredKey)}.");
        return 1;
    }
#endif

    private static FixVector2 ResolveSpatiallyInterpolatedDeterministicGradientFixed(
        PathHandle handle,
        AgentRuntimeData agent,
        FlowTileCacheEntry tile,
        int sectorPathIndex,
        FixVector2 position,
        int worldX,
        int worldY,
        int localIndex,
        out long currentGradientX,
        out long currentGradientY)
    {
        return ResolveSpatiallyInterpolatedDeterministicGradientFixed(
            handle,
            agent,
            tile,
            sectorPathIndex,
            position,
            worldX,
            worldY,
            localIndex,
            out currentGradientX,
            out currentGradientY,
            out _);
    }

    private static FixVector2 ResolveSpatiallyInterpolatedDeterministicGradientFixed(
        PathHandle handle,
        AgentRuntimeData agent,
        FlowTileCacheEntry tile,
        int sectorPathIndex,
        FixVector2 position,
        int worldX,
        int worldY,
        int localIndex,
        out long currentGradientX,
        out long currentGradientY,
        out string sampleTrace)
    {
        FixVector2 currentDirection = ResolveDeterministicIntegrationGradientFixed(
            tile,
            worldX,
            worldY,
            localIndex,
            false,
            0,
            0,
            0,
            out currentGradientX,
            out currentGradientY);

        ResolveSpatialGradientSampleGridFixed(
            position,
            worldX,
            worldY,
            out int x0,
            out int x1,
            out int y0,
            out int y1,
            out Fix64 tx,
            out Fix64 ty);
        Fix64 oneMinusX = Fix64.One - tx;
        Fix64 oneMinusY = Fix64.One - ty;
        FixVector2 weightedGradient = FixVector2.Zero;
        Fix64 totalWeight = Fix64.Zero;
        System.Text.StringBuilder sampleTraceBuilder = IsMovementDiagnosticsEnabled()
            ? new System.Text.StringBuilder(192)
            : null;
        AccumulateSpatialGradientSample(handle, agent, tile, sectorPathIndex, worldX, worldY, x0, y0, oneMinusX * oneMinusY, sampleTraceBuilder, ref weightedGradient, ref totalWeight);
        AccumulateSpatialGradientSample(handle, agent, tile, sectorPathIndex, worldX, worldY, x1, y0, tx * oneMinusY, sampleTraceBuilder, ref weightedGradient, ref totalWeight);
        AccumulateSpatialGradientSample(handle, agent, tile, sectorPathIndex, worldX, worldY, x0, y1, oneMinusX * ty, sampleTraceBuilder, ref weightedGradient, ref totalWeight);
        AccumulateSpatialGradientSample(handle, agent, tile, sectorPathIndex, worldX, worldY, x1, y1, tx * ty, sampleTraceBuilder, ref weightedGradient, ref totalWeight);
        sampleTrace = sampleTraceBuilder?.ToString() ?? "disabled";
        if (totalWeight <= Fix64.Zero || weightedGradient == FixVector2.Zero)
            return currentDirection;

        currentGradientX = weightedGradient.x.RawValue;
        currentGradientY = weightedGradient.y.RawValue;
        long maximumMagnitude = Math.Max(Math.Abs(currentGradientX), Math.Abs(currentGradientY));
        Fix64 scale = Fix64.FromRaw(maximumMagnitude);
        return new FixVector2(weightedGradient.x / scale, weightedGradient.y / scale);
    }

    private static void ResolveSpatialGradientSampleGridFixed(
        FixVector2 position,
        int worldX,
        int worldY,
        out int x0,
        out int x1,
        out int y0,
        out int y1,
        out Fix64 tx,
        out Fix64 ty)
    {
        long positionXGridRaw = NavigationGridFixedMath.Fix64ToGridRaw(position.x);
        long positionYGridRaw = NavigationGridFixedMath.Fix64ToGridRaw(position.y);
        long centerXGridRaw = checked(_world.OriginXGridRaw + checked((long)worldX * _world.CellSizeGridRaw) + _world.CellSizeGridRaw / 2);
        long centerYGridRaw = checked(_world.OriginZGridRaw + checked((long)worldY * _world.CellSizeGridRaw) + _world.CellSizeGridRaw / 2);
        if (positionXGridRaw >= centerXGridRaw)
        {
            x0 = worldX;
            x1 = worldX + 1;
            tx = NavigationGridFixedMath.ResolveCellFraction(positionXGridRaw, centerXGridRaw, _world.CellSizeGridRaw);
        }
        else
        {
            x0 = worldX - 1;
            x1 = worldX;
            tx = NavigationGridFixedMath.ResolveCellFraction(
                positionXGridRaw,
                checked(centerXGridRaw - _world.CellSizeGridRaw),
                _world.CellSizeGridRaw);
        }

        if (positionYGridRaw >= centerYGridRaw)
        {
            y0 = worldY;
            y1 = worldY + 1;
            ty = NavigationGridFixedMath.ResolveCellFraction(positionYGridRaw, centerYGridRaw, _world.CellSizeGridRaw);
        }
        else
        {
            y0 = worldY - 1;
            y1 = worldY;
            ty = NavigationGridFixedMath.ResolveCellFraction(
                positionYGridRaw,
                checked(centerYGridRaw - _world.CellSizeGridRaw),
                _world.CellSizeGridRaw);
        }
    }

    private static void AccumulateSpatialGradientSample(
        PathHandle handle,
        AgentRuntimeData agent,
        FlowTileCacheEntry tile,
        int sectorPathIndex,
        int currentX,
        int currentY,
        int sampleX,
        int sampleY,
        Fix64 weight,
        System.Text.StringBuilder sampleTraceBuilder,
        ref FixVector2 weightedDirection,
        ref Fix64 totalWeight)
    {
        if (weight <= Fix64.Zero)
            return;
        FlowTileCacheEntry sampleTile = tile;
        int samplePathIndex = sectorPathIndex;
        if (!IsInsideSector(sampleTile, sampleX, sampleY))
            return;
        if ((sampleX != currentX || sampleY != currentY)
            && !CanTraverseNeighborCells(_world, currentX, currentY, sampleX, sampleY))
        {
            return;
        }
        if (sampleX != currentX
            && sampleY != currentY
            && !IsDiagonalPassable(_world, currentX, currentY, sampleX, sampleY))
        {
            return;
        }
        int sampleLocalIndex = sampleTile.GetLocalIndex(sampleX, sampleY);
        if (GetDeterministicIntegrationCost(sampleTile, sampleLocalIndex) == int.MaxValue)
            return;

        int portalGoalIndex = sampleTile.Key.GoalKind == TileGoalKind.Portal
            ? IndexOfGoalCell(sampleTile.GoalCells, sampleX, sampleY)
            : -1;
        FixVector2 sampleGradient;
        string sampleSource;
        if (portalGoalIndex >= 0)
        {
            PortalData portal = GetPortalById(_world, sampleTile.Key.GoalId);
            Vector2Int[] currentPortalCells = GetPortalCellsForSector(portal, sampleTile.Key.SectorId);
            Vector2Int[] oppositePortalCells = GetPortalCellsForSector(
                portal,
                GetOppositeSectorId(portal, sampleTile.Key.SectorId));
            if (portalGoalIndex >= currentPortalCells.Length || portalGoalIndex >= oppositePortalCells.Length)
                throw new InvalidOperationException(
                    $"Portal seed index is outside paired aperture: portal={sampleTile.Key.GoalId} sector={sampleTile.Key.SectorId} " +
                    $"index={portalGoalIndex} currentCount={currentPortalCells.Length} oppositeCount={oppositePortalCells.Length}.");
            Vector2Int pairedCell = oppositePortalCells[portalGoalIndex];
            sampleGradient = new FixVector2(
                (Fix64)(pairedCell.x - sampleX),
                (Fix64)(pairedCell.y - sampleY));
            sampleSource = "portal-window/handoff-normal";
        }
        else
        {
            ResolveDeterministicIntegrationGradientFixed(
                sampleTile,
                sampleX,
                sampleY,
                sampleLocalIndex,
                false,
                0,
                0,
                0,
                out long sampleGradientX,
                out long sampleGradientY);
            sampleGradient = new FixVector2(
                Fix64.FromRaw(sampleGradientX),
                Fix64.FromRaw(sampleGradientY));
            sampleSource = "tile-gradient";
        }
        if (sampleGradient == FixVector2.Zero)
        {
            if (portalGoalIndex >= 0)
            {
                PortalData portal = GetPortalById(_world, sampleTile.Key.GoalId);
                Vector2Int[] currentPortalCells = GetPortalCellsForSector(portal, sampleTile.Key.SectorId);
                Vector2Int[] oppositePortalCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, sampleTile.Key.SectorId));
                Vector2Int pairedCell = oppositePortalCells[portalGoalIndex];
                sampleGradient = new FixVector2(
                    (Fix64)(pairedCell.x - sampleX),
                    (Fix64)(pairedCell.y - sampleY));
                sampleSource += "+portal-normal";
            }
            else
            {
                byte storedDirection = sampleTile.DeterministicFlowDirectionIndices[sampleLocalIndex];
                int offsetIndex = storedDirection - 1;
                if (offsetIndex < 0 || offsetIndex >= NeighborOffsetX.Length)
                    return;
                sampleGradient = new FixVector2(
                    Fix64.FromRaw(NeighborOffsetX[offsetIndex] * 1024L),
                    Fix64.FromRaw(NeighborOffsetY[offsetIndex] * 1024L));
                sampleSource += "+stored";
            }
        }

        sampleTraceBuilder?.Append(sampleTraceBuilder.Length == 0 ? string.Empty : ";")
            .Append("cell=(").Append(sampleX).Append(',').Append(sampleY).Append(')')
            .Append("/tile=").Append(sampleTile.Key.SectorId)
            .Append("/path=").Append(samplePathIndex)
            .Append("/cost=").Append(GetDeterministicIntegrationCost(sampleTile, sampleLocalIndex))
            .Append("/source=").Append(sampleSource)
            .Append("/gradientRaw=(").Append(sampleGradient.x.RawValue).Append(',').Append(sampleGradient.y.RawValue).Append(')')
            .Append("/weightRaw=").Append(weight.RawValue);
        weightedDirection += sampleGradient * weight;
        totalWeight += weight;
    }

    private static int ResolveSteeringReadCorridorTileIndex(
        PathHandle handle,
        AgentRuntimeData agent,
        FlowTileCacheEntry tile)
    {
        if (handle == null || agent == null || tile == null)
            return -1;
        ResolveSteeringReadCorridor(
            handle,
            out ImmutableRouteSequence sectorIds,
            out ImmutableRouteSequence portalIds,
            out int startIndex,
            out int goalX,
            out int goalY,
            out bool usesCommittedCorridor);
        for (int index = startIndex; index < sectorIds.Length; index++)
        {
            FlowTileCacheKey key = CreateSteeringReadDomainTileKey(
                handle,
                sectorIds,
                portalIds,
                index,
                goalX,
                goalY,
                agent.AgentTypeId,
                usesCommittedCorridor);
            if (key.Equals(tile.Key))
                return index;
        }

        throw new InvalidOperationException(
            $"ResolveSteeringReadCorridorTileIndex failed: tile is outside the committed steering corridor key={FormatTileKey(tile.Key)}, handle={FormatPathHandle(handle)}.");
    }

    private static bool TryResolveCommittedCorridorTileForSector(
        PathHandle handle,
        AgentRuntimeData agent,
        FlowTileCacheEntry currentTile,
        int currentPathIndex,
        int sampleSectorId,
        bool allowPreviousPathIndex,
        out FlowTileCacheEntry sampleTile,
        out int samplePathIndex)
    {
        if (handle == null || agent == null || currentTile == null)
            throw new InvalidOperationException("TryResolveCommittedCorridorTileForSector failed: path, agent, or current tile is null.");
        sampleTile = currentTile;
        samplePathIndex = currentPathIndex;
        if (sampleSectorId == currentTile.Key.SectorId)
            return true;

        ResolveSteeringReadCorridor(
            handle,
            out ImmutableRouteSequence sectorIds,
            out ImmutableRouteSequence portalIds,
            out int startIndex,
            out int goalX,
            out int goalY,
            out bool usesCommittedCorridor);
        if (currentPathIndex < startIndex || currentPathIndex >= sectorIds.Length)
        {
            throw new InvalidOperationException(
                $"TryResolveCommittedCorridorTileForSector failed: current path index is invalid index={currentPathIndex}, start={startIndex}, count={sectorIds.Length}.");
        }
        FlowTileCacheKey expectedCurrentKey = CreateSteeringReadDomainTileKey(
            handle,
            sectorIds,
            portalIds,
            currentPathIndex,
            goalX,
            goalY,
            agent.AgentTypeId,
            usesCommittedCorridor);
        if (!expectedCurrentKey.Equals(currentTile.Key))
        {
            throw new InvalidOperationException(
                $"TryResolveCommittedCorridorTileForSector failed: current tile/key mismatch current={FormatTileKey(currentTile.Key)}, expected={FormatTileKey(expectedCurrentKey)}.");
        }

        int resolvedPathIndex = currentPathIndex + 1;
        if (resolvedPathIndex >= sectorIds.Length || sectorIds[resolvedPathIndex] != sampleSectorId)
        {
            resolvedPathIndex = currentPathIndex - 1;
            if (!allowPreviousPathIndex
                || resolvedPathIndex < startIndex
                || sectorIds[resolvedPathIndex] != sampleSectorId)
            {
                return false;
            }
        }
        if (resolvedPathIndex < startIndex || resolvedPathIndex >= sectorIds.Length)
            return false;
        FlowTileCacheKey resolvedKey = CreateSteeringReadDomainTileKey(
            handle,
            sectorIds,
            portalIds,
            resolvedPathIndex,
            goalX,
            goalY,
            agent.AgentTypeId,
            usesCommittedCorridor);
        if (!DeterministicFlowTileCache.TryGetValue(resolvedKey, out sampleTile))
        {
            throw new PendingNavigationTileException(
                $"committed corridor tile is pending current={FormatTileKey(currentTile.Key)}, required={FormatTileKey(resolvedKey)}");
        }

        sampleTile.LastUsedFrame = GetFrameCount();
        samplePathIndex = resolvedPathIndex;
        return true;
    }

    private static bool TryResolveCommittedCorridorTileForSector(
        PathHandle handle,
        AgentRuntimeData agent,
        FlowTileCacheEntry currentTile,
        int currentPathIndex,
        int sampleSectorId,
        out FlowTileCacheEntry sampleTile,
        out int samplePathIndex)
    {
        return TryResolveCommittedCorridorTileForSector(
            handle,
            agent,
            currentTile,
            currentPathIndex,
            sampleSectorId,
            false,
            out sampleTile,
            out samplePathIndex);
    }

    private static FixVector2 ResolveDeterministicIntegrationGradientFixed(
        FlowTileCacheEntry tile,
        int worldX,
        int worldY,
        int localIndex,
        bool hasExternalSample,
        int externalSampleX,
        int externalSampleY,
        int externalSampleCost,
        out long gradientX,
        out long gradientY)
    {
        int currentCost = GetDeterministicIntegrationCost(tile, localIndex);
        if (currentCost == int.MaxValue)
        {
            throw new InvalidOperationException(
                $"ResolveDeterministicIntegrationGradientFixed failed: current cell is unreachable cell=({worldX},{worldY}), key={FormatTileKey(tile.Key)}.");
        }

        gradientX = ResolveDeterministicIntegrationGradientAxis(
            tile,
            worldX,
            worldY,
            currentCost,
            -1,
            0,
            1,
            0,
            hasExternalSample,
            externalSampleX,
            externalSampleY,
            externalSampleCost);
        gradientY = ResolveDeterministicIntegrationGradientAxis(
            tile,
            worldX,
            worldY,
            currentCost,
            0,
            -1,
            0,
            1,
            hasExternalSample,
            externalSampleX,
            externalSampleY,
            externalSampleCost);
        long maximumMagnitude = Math.Max(Math.Abs(gradientX), Math.Abs(gradientY));
        if (maximumMagnitude == 0)
            return FixVector2.Zero;

        Fix64 scale = Fix64.FromRaw(maximumMagnitude);
        return new FixVector2(
            Fix64.FromRaw(gradientX) / scale,
            Fix64.FromRaw(gradientY) / scale);
    }

    private static long ResolveDeterministicIntegrationGradientAxis(
        FlowTileCacheEntry tile,
        int worldX,
        int worldY,
        int currentCost,
        int negativeOffsetX,
        int negativeOffsetY,
        int positiveOffsetX,
        int positiveOffsetY,
        bool hasExternalSample,
        int externalSampleX,
        int externalSampleY,
        int externalSampleCost)
    {
        bool hasNegative = TryGetDeterministicIntegrationGradientSample(
            tile,
            worldX,
            worldY,
            worldX + negativeOffsetX,
            worldY + negativeOffsetY,
            hasExternalSample,
            externalSampleX,
            externalSampleY,
            externalSampleCost,
            out int negativeCost);
        bool hasPositive = TryGetDeterministicIntegrationGradientSample(
            tile,
            worldX,
            worldY,
            worldX + positiveOffsetX,
            worldY + positiveOffsetY,
            hasExternalSample,
            externalSampleX,
            externalSampleY,
            externalSampleCost,
            out int positiveCost);

        if (hasNegative && hasPositive)
            return (long)negativeCost - positiveCost;
        if (hasNegative)
            return Math.Min(0L, negativeCost - (long)currentCost);
        if (hasPositive)
            return Math.Max(0L, (long)currentCost - positiveCost);
        return 0L;
    }

    private static bool TryGetDeterministicIntegrationGradientSample(
        FlowTileCacheEntry tile,
        int worldX,
        int worldY,
        int sampleX,
        int sampleY,
        bool hasExternalSample,
        int externalSampleX,
        int externalSampleY,
        int externalSampleCost,
        out int cost)
    {
        if (hasExternalSample && sampleX == externalSampleX && sampleY == externalSampleY)
        {
            cost = externalSampleCost;
            return true;
        }

        cost = int.MaxValue;
        if (!IsInsideSector(tile, sampleX, sampleY)
            || !CanTraverseSamplePath(_world, worldX, worldY, sampleX, sampleY))
        {
            return false;
        }

        cost = GetDeterministicIntegrationCost(tile, tile.GetLocalIndex(sampleX, sampleY));
        return cost != int.MaxValue;
    }

    private static FixVector2 ResolvePendingPortalVelocityFixed(
        AgentRuntimeData agent,
        FlowTileCacheKey key,
        FixVector2 position,
        int worldX,
        int worldY,
        Fix64 maxSpeed,
        out long portalAccessCost,
        out int directionIndex,
        out int portalSlotIndex,
        out string portalTraversalDiagnostic)
    {
        portalAccessCost = long.MaxValue;
        directionIndex = 0;
        portalSlotIndex = -1;
        portalTraversalDiagnostic = "portalTraversal=unresolved";
        if (agent == null)
            throw new InvalidOperationException("ResolvePendingPortalVelocityFixed failed: agent is null.");
        if (key.GoalKind != TileGoalKind.Portal)
            throw new InvalidOperationException($"ResolvePendingPortalVelocityFixed failed: key is not a portal goal key={FormatTileKey(key)}.");
        if (key.SectorId != agent.NavState.CurrentSectorId)
        {
            throw new InvalidOperationException(
                $"ResolvePendingPortalVelocityFixed failed: current sector mismatch key={key.SectorId}, nav={agent.NavState.CurrentSectorId}.");
        }

        PortalData portal = GetPortalById(_world, key.GoalId);
        Vector2Int[] currentCells = GetPortalCellsForSector(portal, key.SectorId);
        Vector2Int[] oppositeCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, key.SectorId));
        if (currentCells.Length == 0 || currentCells.Length != oppositeCells.Length)
        {
            throw new InvalidOperationException(
                $"ResolvePendingPortalVelocityFixed failed: portal side cells are invalid portal={portal.PortalId}, current={currentCells.Length}, opposite={oppositeCells.Length}.");
        }

        for (int i = 0; i < currentCells.Length; i++)
        {
            if (currentCells[i].x != worldX || currentCells[i].y != worldY)
                continue;

            int previousTraversalWorldVersion = agent.NavState.PortalTraversalWorldVersion;
            int previousTraversalSectorId = agent.NavState.PortalTraversalSectorId;
            int previousTraversalPortalId = agent.NavState.PortalTraversalId;
            int previousTraversalSlotIndex = agent.NavState.PortalTraversalSlotIndex;
            bool previousTraversalCommitted = agent.NavState.PortalTraversalHasCommittedTileSlot;
            portalSlotIndex = ResolveStablePortalTraversalSlotIndex(
                agent.NavState,
                key,
                i,
                oppositeCells.Length,
                recommendationFromCommittedTile: false);
            portalTraversalDiagnostic =
                $"portalTraversal=current-cell recommendedSlot={i} selectedSlot={portalSlotIndex} currentSlot={i} " +
                $"previousTraversal=({previousTraversalWorldVersion},{previousTraversalSectorId},{previousTraversalPortalId},{previousTraversalSlotIndex},committed={previousTraversalCommitted}) " +
                $"currentCells={FormatGoalCells(currentCells)} oppositeCells={FormatGoalCells(oppositeCells)} " +
                $"selectedOpposite=({oppositeCells[portalSlotIndex].x},{oppositeCells[portalSlotIndex].y}) cell=({worldX},{worldY})";
            FixVector2 oppositeCellCenter = _world.GridToWorldCenterFixed(
                oppositeCells[portalSlotIndex].x,
                oppositeCells[portalSlotIndex].y);
            FixVector2 target = ResolvePortalApproachTargetFixed(
                position,
                currentCells[portalSlotIndex],
                oppositeCellCenter,
                portal.IsVerticalBoundary);
            return ScaleFixedDirectionToSpeed(target - position, maxSpeed);
        }

        SectorData sector = _world.Sectors[key.SectorId];
        if (!IsInsideSector(sector, worldX, worldY))
            throw new InvalidOperationException(
                $"ResolvePendingPortalVelocityFixed failed: current cell is outside sector cell=({worldX},{worldY}) sector={key.SectorId}.");

        SectorPortalAccessEntry access = GetPrebuiltSectorPortalAccess(sector, key.SectorId, portal.PortalId);
        portalAccessCost = ResolveDeterministicPortalAccessIntegrationCost(
            _world,
            sector,
            access,
            worldX,
            worldY);
        if (portalAccessCost == long.MaxValue)
        {
            throw new InvalidOperationException(
                $"ResolvePendingPortalVelocityFixed failed: current cell cannot reach portal agent={agent.Id}, " +
                $"cell=({worldX},{worldY}), portal={portal.PortalId}, sector={key.SectorId}.");
        }

        int recommendedPortalSlotIndex = ResolvePendingPortalTargetSlotIndex(
            sector,
            access,
            currentCells,
            worldX,
            worldY,
            portalAccessCost);
        int previousWorldVersion = agent.NavState.PortalTraversalWorldVersion;
        int previousSectorId = agent.NavState.PortalTraversalSectorId;
        int previousPortalId = agent.NavState.PortalTraversalId;
        int previousSlotIndex = agent.NavState.PortalTraversalSlotIndex;
        bool previousCommitted = agent.NavState.PortalTraversalHasCommittedTileSlot;
        portalSlotIndex = ResolveStablePortalTraversalSlotIndex(
            agent.NavState,
            key,
            recommendedPortalSlotIndex,
            oppositeCells.Length,
            recommendationFromCommittedTile: false);
        Vector2Int selectedOppositeCell = oppositeCells[portalSlotIndex];
        portalTraversalDiagnostic =
            $"portalTraversal=access-field recommendedSlot={recommendedPortalSlotIndex} selectedSlot={portalSlotIndex} currentSlot=-1 " +
            $"previousTraversal=({previousWorldVersion},{previousSectorId},{previousPortalId},{previousSlotIndex},committed={previousCommitted}) " +
            $"currentCells={FormatGoalCells(currentCells)} oppositeCells={FormatGoalCells(oppositeCells)} " +
            $"selectedOpposite=({selectedOppositeCell.x},{selectedOppositeCell.y}) cell=({worldX},{worldY})";
        if (!TryResolveLowestPortalAccessNeighbor(
                sector,
                access,
                worldX,
                worldY,
                portalAccessCost,
                out int bestDirectionIndex,
                out _))
        {
            throw new InvalidOperationException(
                $"ResolvePendingPortalVelocityFixed failed: portal access field has no descending neighbor agent={agent.Id}, " +
                $"cell=({worldX},{worldY}), cost={portalAccessCost}, portal={portal.PortalId}, sector={key.SectorId}.");
        }

        directionIndex = bestDirectionIndex + 1;
        FixVector2 direction = ResolveSpatiallyInterpolatedPortalAccessGradientFixed(
            sector,
            access,
            position,
            worldX,
            worldY,
            portalAccessCost,
            out long gradientX,
            out long gradientY);
        if (direction == FixVector2.Zero)
        {
            direction = new FixVector2(
                NeighborOffsetX[bestDirectionIndex],
                NeighborOffsetY[bestDirectionIndex]);
        }
        portalTraversalDiagnostic +=
            $" accessGradient=({gradientX},{gradientY}) accessGradientFixedRaw=({direction.x.RawValue},{direction.y.RawValue})";
        FixVector2 velocity = ResolveSpatiallyIntegratedFieldVelocityFixed(
            null,
            null,
            null,
            sector,
            access,
            position,
            direction,
            maxSpeed,
            out int integrationSubsteps,
            out int domainBoundarySubsteps,
            out int sampledTileTransitions,
            out string integrationTrace);
        portalTraversalDiagnostic +=
            $" integrationSubsteps={integrationSubsteps} domainBoundarySubsteps={domainBoundarySubsteps} " +
            $"sampledTileTransitions={sampledTileTransitions} integrationTrace={integrationTrace}";
        return velocity;
    }

    private static FixVector2 ResolveSpatiallyIntegratedFieldVelocityFixed(
        PathHandle handle,
        AgentRuntimeData agent,
        FlowTileCacheEntry tile,
        SectorData portalAccessSector,
        SectorPortalAccessEntry portalAccess,
        FixVector2 position,
        FixVector2 initialDirection,
        Fix64 maxSpeed,
        out int substepCount,
        out int domainBoundarySubsteps,
        out int sampledTileTransitions,
        out string directionTrace)
    {
        directionTrace = "not-started";
        bool samplesTile = tile != null;
        bool samplesPortalAccess = portalAccessSector != null && portalAccess != null;
        if (samplesTile == samplesPortalAccess)
            throw new InvalidOperationException("ResolveSpatiallyIntegratedFieldVelocityFixed failed: exactly one spatial field is required.");
        if (initialDirection == FixVector2.Zero)
            throw new InvalidOperationException("ResolveSpatiallyIntegratedFieldVelocityFixed failed: initial direction is zero.");

        Fix64 deltaTime = LogicFrameRuntime.FixedDeltaTime;
        Fix64 travelDistance = maxSpeed * deltaTime;
        if (_world.CellSizeGridRaw <= 0)
            throw new InvalidOperationException("ResolveSpatiallyIntegratedFieldVelocityFixed failed: navigation cell size is not positive.");
        if (travelDistance <= Fix64.Zero)
        {
            substepCount = 0;
            domainBoundarySubsteps = 0;
            sampledTileTransitions = 0;
            return FixVector2.Zero;
        }

        substepCount = ResolveSpatialIntegrationSubstepCount(travelDistance, _world.CellSizeGridRaw);
        if (substepCount <= 0)
            throw new InvalidOperationException($"ResolveSpatiallyIntegratedFieldVelocityFixed failed: invalid substep count={substepCount}.");

        Fix64 substepDistance = travelDistance / (Fix64)substepCount;
        Fix64 half = Fix64.FromRaw(2048);
        FixVector2 integratedPosition = position;
        FixVector2 direction = initialDirection.GetNormalized();
        System.Text.StringBuilder directionTraceBuilder = IsMovementDiagnosticsEnabled()
            ? new System.Text.StringBuilder(256)
            : null;
        directionTraceBuilder?.Append("initialRaw=(")
            .Append(direction.x.RawValue).Append(',').Append(direction.y.RawValue).Append(')');
        directionTrace = directionTraceBuilder?.ToString() ?? "disabled";
        domainBoundarySubsteps = 0;
        sampledTileTransitions = 0;
        FlowTileCacheEntry sampledTile = tile;
        int sampledPathIndex = samplesTile
            ? ResolveSteeringReadCorridorTileIndex(handle, agent, tile)
            : -1;
        for (int i = 0; i < substepCount; i++)
        {
            if (samplesTile)
            {
                sampledTile = ResolveSpatialIntegrationTileForPosition(
                    handle,
                    agent,
                    sampledTile,
                    integratedPosition,
                    ref sampledPathIndex,
                    ref sampledTileTransitions);
                if (IsDeterministicFinalGoalFieldMinimum(sampledTile, integratedPosition))
                    return (integratedPosition - position) / deltaTime;
            }
            bool hasStepStartDirection;
            FixVector2 stepStartDirection = FixVector2.Zero;
            string stepStartSampleTrace = "initial-direction";
            try
            {
                hasStepStartDirection = i > 0 && TryResolveSpatialFieldDirectionFixed(
                    handle,
                    agent,
                    sampledTile,
                    sampledPathIndex,
                    portalAccessSector,
                    portalAccess,
                    integratedPosition,
                    out stepStartDirection,
                    out stepStartSampleTrace);
                directionTraceBuilder?.Append(" startSamples={").Append(stepStartSampleTrace).Append('}');
            }
            catch (PendingNavigationTileException)
            {
                throw;
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"ResolveSpatiallyIntegratedFieldVelocityFixed failed at step start substep={i}/{substepCount}, " +
                    $"originRaw=({position.x.RawValue},{position.y.RawValue}), integratedRaw=({integratedPosition.x.RawValue},{integratedPosition.y.RawValue}), " +
                    $"directionRaw=({direction.x.RawValue},{direction.y.RawValue}), substepDistanceRaw={substepDistance.RawValue}.",
                    exception);
            }
            if (hasStepStartDirection)
            {
                direction = stepStartDirection.GetNormalized();
            }
            directionTraceBuilder?.Append("|step=").Append(i)
                .Append(" startTile=").Append(sampledTile?.Key.SectorId ?? -1)
                .Append(" path=").Append(sampledPathIndex)
                .Append(" startRaw=(").Append(direction.x.RawValue).Append(',').Append(direction.y.RawValue).Append(')');
            directionTrace = directionTraceBuilder?.ToString() ?? directionTrace;

            FixVector2 midpoint = integratedPosition + direction * substepDistance * half;
            bool midpointInFieldDomain = true;
            FlowTileCacheEntry midpointSampleTile = sampledTile;
            int midpointPathIndex = sampledPathIndex;
            int midpointTileTransitions = 0;
            if (samplesTile)
            {
                midpointInFieldDomain = TryResolveSpatialIntegrationTileForCandidatePosition(
                    handle,
                    agent,
                    sampledTile,
                    midpoint,
                    sampledPathIndex,
                    out midpointSampleTile,
                    out midpointPathIndex,
                    out midpointTileTransitions)
                    && IsSpatialFieldCandidateReachable(midpointSampleTile, null, null, midpoint);
                sampledTileTransitions += midpointTileTransitions;
                if (midpointInFieldDomain
                    && IsDeterministicFinalGoalFieldMinimum(midpointSampleTile, midpoint))
                {
                    return (midpoint - position) / deltaTime;
                }
            }
            bool hasMidpointDirection;
            FixVector2 midpointDirection = FixVector2.Zero;
            if (!midpointInFieldDomain)
            {
                midpointDirection = ResolveMonotoneSpatialFieldDirectionFixed(
                    handle,
                    agent,
                    sampledTile,
                    portalAccessSector,
                    portalAccess,
                    integratedPosition);
                hasMidpointDirection = true;
                domainBoundarySubsteps++;
            }
            else
            {
                try
                {
                    hasMidpointDirection = TryResolveSpatialFieldDirectionFixed(
                        handle,
                        agent,
                        midpointSampleTile,
                        midpointPathIndex,
                        portalAccessSector,
                        portalAccess,
                        midpoint,
                        out midpointDirection,
                        out string midpointSampleTrace);
                    directionTraceBuilder?.Append(" midpointSamples={").Append(midpointSampleTrace).Append('}');
                }
                catch (PendingNavigationTileException)
                {
                    throw;
                }
                catch (InvalidOperationException exception)
                {
                    throw new InvalidOperationException(
                        $"ResolveSpatiallyIntegratedFieldVelocityFixed failed at midpoint substep={i}/{substepCount}, " +
                        $"originRaw=({position.x.RawValue},{position.y.RawValue}), integratedRaw=({integratedPosition.x.RawValue},{integratedPosition.y.RawValue}), " +
                        $"midpointRaw=({midpoint.x.RawValue},{midpoint.y.RawValue}), directionRaw=({direction.x.RawValue},{direction.y.RawValue}), " +
                        $"substepDistanceRaw={substepDistance.RawValue}.",
                        exception);
                }
            }
            if (hasMidpointDirection)
            {
                direction = midpointDirection.GetNormalized();
            }
            directionTraceBuilder?.Append(" midpointTile=").Append(midpointSampleTile?.Key.SectorId ?? -1)
                .Append(" midpointPath=").Append(midpointPathIndex)
                .Append(" midpointDomain=").Append(midpointInFieldDomain)
                .Append(" midpointRaw=(").Append(direction.x.RawValue).Append(',').Append(direction.y.RawValue).Append(')');
            directionTrace = directionTraceBuilder?.ToString() ?? directionTrace;

            FixVector2 candidatePosition = integratedPosition + direction * substepDistance;
            FlowTileCacheEntry candidateTile = sampledTile;
            int candidatePathIndex = sampledPathIndex;
            int candidateTileTransitions = 0;
            bool candidateInFieldDomain = true;
            if (samplesTile)
            {
                candidateInFieldDomain = TryResolveSpatialIntegrationTileForCandidatePosition(
                    handle,
                    agent,
                    sampledTile,
                    candidatePosition,
                    sampledPathIndex,
                    out candidateTile,
                    out candidatePathIndex,
                    out candidateTileTransitions);
            }
            if (!candidateInFieldDomain
                || !IsSpatialFieldCandidateReachable(
                    candidateTile,
                    portalAccessSector,
                    portalAccess,
                    candidatePosition))
            {
                direction = ResolveMonotoneSpatialFieldDirectionFixed(
                    handle,
                    agent,
                    sampledTile,
                    portalAccessSector,
                    portalAccess,
                    integratedPosition).GetNormalized();
                candidatePosition = integratedPosition + direction * substepDistance;
                domainBoundarySubsteps++;
                candidateTile = sampledTile;
                candidatePathIndex = sampledPathIndex;
                candidateTileTransitions = 0;
                candidateInFieldDomain = true;
                if (samplesTile)
                {
                    candidateInFieldDomain = TryResolveSpatialIntegrationTileForCandidatePosition(
                        handle,
                        agent,
                        sampledTile,
                        candidatePosition,
                        sampledPathIndex,
                        out candidateTile,
                        out candidatePathIndex,
                        out candidateTileTransitions);
                }
                if (!candidateInFieldDomain
                    || !IsSpatialFieldCandidateReachable(
                        candidateTile,
                        portalAccessSector,
                        portalAccess,
                        candidatePosition))
                {
                    throw new InvalidOperationException(
                        $"ResolveSpatiallyIntegratedFieldVelocityFixed failed: monotone upwind step leaves the reachable domain " +
                        $"substep={i}/{substepCount}, integratedRaw=({integratedPosition.x.RawValue},{integratedPosition.y.RawValue}), " +
                        $"candidateRaw=({candidatePosition.x.RawValue},{candidatePosition.y.RawValue}), directionRaw=({direction.x.RawValue},{direction.y.RawValue}).");
                }
            }

            integratedPosition = candidatePosition;
            sampledTile = candidateTile;
            sampledPathIndex = candidatePathIndex;
            sampledTileTransitions += candidateTileTransitions;
            directionTraceBuilder?.Append(" candidateTile=").Append(sampledTile?.Key.SectorId ?? -1)
                .Append(" candidatePath=").Append(sampledPathIndex)
                .Append(" candidateRaw=(").Append(candidatePosition.x.RawValue).Append(',').Append(candidatePosition.y.RawValue).Append(')');
            directionTrace = directionTraceBuilder?.ToString() ?? directionTrace;
        }

        return (integratedPosition - position) / deltaTime;
    }

    private static int ResolveSpatialIntegrationSubstepCount(Fix64 travelDistance, long cellSizeGridRaw)
    {
        int result = NavigationGridFixedMath.DivideCeilingByHalfCellSize(travelDistance, cellSizeGridRaw);
        if (result <= 0)
            throw new InvalidOperationException($"ResolveSpatialIntegrationSubstepCount failed: invalid result={result} travelRaw={travelDistance.RawValue} cellSizeGridRaw={cellSizeGridRaw}.");
        return result;
    }

    private static bool IsDeterministicFinalGoalFieldMinimum(
        FlowTileCacheEntry tile,
        FixVector2 position)
    {
        if (tile == null || tile.Key.GoalKind != TileGoalKind.FinalGoal)
            return false;
        if (!_world.WorldToGridFixed(position, out int worldX, out int worldY)
            || !IsInsideSector(tile, worldX, worldY)
            || worldX + worldY * _world.Width != tile.Key.FinalGoalIndex)
        {
            return false;
        }

        int localIndex = tile.GetLocalIndex(worldX, worldY);
        int goalCost = GetDeterministicIntegrationCost(tile, localIndex);
        if (goalCost != 0
            || tile.DeterministicFlowDirectionIndices[localIndex] != 0)
        {
            throw new InvalidOperationException(
                $"IsDeterministicFinalGoalFieldMinimum failed: final goal cell is not a zero-cost minimum " +
                $"cell=({worldX},{worldY}), cost={goalCost}, " +
                $"direction={tile.DeterministicFlowDirectionIndices[localIndex]}, key={FormatTileKey(tile.Key)}.");
        }

        return true;
    }

    private static bool TryResolveSpatialIntegrationTileForCandidatePosition(
        PathHandle handle,
        AgentRuntimeData agent,
        FlowTileCacheEntry currentTile,
        FixVector2 candidatePosition,
        int currentPathIndex,
        out FlowTileCacheEntry candidateTile,
        out int candidatePathIndex,
        out int candidateTileTransitions)
    {
        candidateTile = currentTile;
        candidatePathIndex = currentPathIndex;
        candidateTileTransitions = 0;
        if (!_world.WorldToGridFixed(candidatePosition, out int worldX, out int worldY)
            || !_world.TryGetSectorId(worldX, worldY, out int sectorId))
        {
            return false;
        }
        if (!TryResolveCommittedCorridorTileForSector(
                handle,
                agent,
                currentTile,
                currentPathIndex,
                sectorId,
                out candidateTile,
                out candidatePathIndex))
        {
            return false;
        }
        candidateTileTransitions = candidatePathIndex == currentPathIndex ? 0 : 1;
        return true;
    }

    private static FlowTileCacheEntry ResolveSpatialIntegrationTileForPosition(
        PathHandle handle,
        AgentRuntimeData agent,
        FlowTileCacheEntry currentTile,
        FixVector2 position,
        ref int sectorPathIndex,
        ref int sampledTileTransitions)
    {
        if (handle == null || agent == null || currentTile == null)
            throw new InvalidOperationException("ResolveSpatialIntegrationTileForPosition failed: path, agent, or tile is null.");
        if (!_world.WorldToGridFixed(position, out int worldX, out int worldY)
            || !_world.TryGetSectorId(worldX, worldY, out int sectorId))
        {
            throw new InvalidOperationException(
                $"ResolveSpatialIntegrationTileForPosition failed: sample is outside navigation world raw=({position.x.RawValue},{position.y.RawValue}).");
        }
        if (currentTile.Key.SectorId == sectorId)
            return currentTile;
        if (!TryResolveCommittedCorridorTileForSector(
                handle,
                agent,
                currentTile,
                sectorPathIndex,
                sectorId,
                out FlowTileCacheEntry resolvedTile,
                out int resolvedPathIndex))
        {
            throw new InvalidOperationException(
                $"ResolveSpatialIntegrationTileForPosition failed: sample left the committed corridor sector={sectorId}, " +
                $"sampledIndex={sectorPathIndex}, cell=({worldX},{worldY}), handle={FormatPathHandle(handle)}.");
        }

        sectorPathIndex = resolvedPathIndex;
        sampledTileTransitions++;
        return resolvedTile;
    }

    private static bool IsSpatialFieldCandidateReachable(
        FlowTileCacheEntry tile,
        SectorData portalAccessSector,
        SectorPortalAccessEntry portalAccess,
        FixVector2 position)
    {
        if (!_world.WorldToGridFixed(position, out int worldX, out int worldY))
            return false;
        if (tile != null)
        {
            return !IsInsideSector(tile, worldX, worldY)
                   || GetDeterministicIntegrationCost(tile, tile.GetLocalIndex(worldX, worldY)) != int.MaxValue;
        }

        if (!IsInsideSector(portalAccessSector, worldX, worldY))
            return true;
        return ResolveDeterministicPortalAccessIntegrationCost(
                   _world,
                   portalAccessSector,
                   portalAccess,
                   worldX,
                   worldY)
               != long.MaxValue;
    }

    private static FixVector2 ResolveMonotoneSpatialFieldDirectionFixed(
        PathHandle handle,
        AgentRuntimeData agent,
        FlowTileCacheEntry tile,
        SectorData portalAccessSector,
        SectorPortalAccessEntry portalAccess,
        FixVector2 position)
    {
        if (!_world.WorldToGridFixed(position, out int worldX, out int worldY))
            throw new InvalidOperationException("ResolveMonotoneSpatialFieldDirectionFixed failed: position is outside world.");
        if (tile != null)
        {
            if (!IsInsideSector(tile, worldX, worldY))
                throw new InvalidOperationException($"ResolveMonotoneSpatialFieldDirectionFixed failed: position is outside tile key={FormatTileKey(tile.Key)}.");
            int localIndex = tile.GetLocalIndex(worldX, worldY);
            if (GetDeterministicIntegrationCost(tile, localIndex) == int.MaxValue)
                throw new InvalidOperationException($"ResolveMonotoneSpatialFieldDirectionFixed failed: current cell is unreachable cell=({worldX},{worldY}), key={FormatTileKey(tile.Key)}.");

            int portalGoalIndex = tile.Key.GoalKind == TileGoalKind.Portal
                ? IndexOfGoalCell(tile.GoalCells, worldX, worldY)
                : -1;
            if (portalGoalIndex >= 0)
            {
                PortalData portal = GetPortalById(_world, tile.Key.GoalId);
                Vector2Int[] oppositeCells = GetPortalCellsForSector(
                    portal,
                    GetOppositeSectorId(portal, tile.Key.SectorId));
                FixVector2 oppositeCellCenter = _world.GridToWorldCenterFixed(
                    oppositeCells[portalGoalIndex].x,
                    oppositeCells[portalGoalIndex].y);
                FixVector2 approachTarget = ResolvePortalApproachTargetFixed(
                    position,
                    tile.GoalCells[portalGoalIndex],
                    oppositeCellCenter,
                    portal.IsVerticalBoundary);
                return approachTarget - position;
            }

            int directionIndex = tile.DeterministicFlowDirectionIndices[localIndex] - 1;
            if (directionIndex < 0 || directionIndex >= NeighborOffsetX.Length)
            {
                throw new InvalidOperationException(
                    $"ResolveMonotoneSpatialFieldDirectionFixed failed: tile cell has no strict descending direction cell=({worldX},{worldY}), key={FormatTileKey(tile.Key)}.");
            }
            return new FixVector2(NeighborOffsetX[directionIndex], NeighborOffsetY[directionIndex]);
        }

        if (!IsInsideSector(portalAccessSector, worldX, worldY))
            throw new InvalidOperationException("ResolveMonotoneSpatialFieldDirectionFixed failed: position is outside portal access sector.");
        long currentCost = ResolveDeterministicPortalAccessIntegrationCost(
            _world,
            portalAccessSector,
            portalAccess,
            worldX,
            worldY);
        if (currentCost == long.MaxValue
            || !TryResolveLowestPortalAccessNeighbor(
                portalAccessSector,
                portalAccess,
                worldX,
                worldY,
                currentCost,
                out int bestDirectionIndex,
                out _))
        {
            throw new InvalidOperationException(
                $"ResolveMonotoneSpatialFieldDirectionFixed failed: portal access cell has no strict descending direction cell=({worldX},{worldY}), sector={portalAccessSector.SectorId}, portal={portalAccess.PortalId}.");
        }
        return new FixVector2(NeighborOffsetX[bestDirectionIndex], NeighborOffsetY[bestDirectionIndex]);
    }

    private static bool TryResolveSpatialFieldDirectionFixed(
        PathHandle handle,
        AgentRuntimeData agent,
        FlowTileCacheEntry tile,
        int sectorPathIndex,
        SectorData portalAccessSector,
        SectorPortalAccessEntry portalAccess,
        FixVector2 position,
        out FixVector2 direction)
    {
        return TryResolveSpatialFieldDirectionFixed(
            handle,
            agent,
            tile,
            sectorPathIndex,
            portalAccessSector,
            portalAccess,
            position,
            out direction,
            out _);
    }

    private static bool TryResolveSpatialFieldDirectionFixed(
        PathHandle handle,
        AgentRuntimeData agent,
        FlowTileCacheEntry tile,
        int sectorPathIndex,
        SectorData portalAccessSector,
        SectorPortalAccessEntry portalAccess,
        FixVector2 position,
        out FixVector2 direction,
        out string sampleTrace)
    {
        direction = FixVector2.Zero;
        sampleTrace = "not-sampled";
        if (!_world.WorldToGridFixed(position, out int worldX, out int worldY))
            return false;

        if (tile != null)
        {
            if (!IsInsideSector(tile, worldX, worldY))
                return false;
            int localIndex = tile.GetLocalIndex(worldX, worldY);
            if (GetDeterministicIntegrationCost(tile, localIndex) == int.MaxValue)
                throw new InvalidOperationException($"TryResolveSpatialFieldDirectionFixed failed: tile sample is unreachable cell=({worldX},{worldY}), key={FormatTileKey(tile.Key)}.");
            direction = ResolveSpatiallyInterpolatedDeterministicGradientFixed(
                handle,
                agent,
                tile,
                sectorPathIndex,
                position,
                worldX,
                worldY,
                localIndex,
                out _,
                out _,
                out sampleTrace);
            if (direction != FixVector2.Zero)
                return true;

            byte storedDirection = tile.DeterministicFlowDirectionIndices[localIndex];
            int offsetIndex = storedDirection - 1;
            if (offsetIndex < 0 || offsetIndex >= NeighborOffsetX.Length)
                throw new InvalidOperationException($"TryResolveSpatialFieldDirectionFixed failed: tile sample has no direction cell=({worldX},{worldY}), key={FormatTileKey(tile.Key)}.");
            direction = new FixVector2(NeighborOffsetX[offsetIndex], NeighborOffsetY[offsetIndex]);
            sampleTrace += ";fallback=stored-direction";
            return true;
        }

        if (!IsInsideSector(portalAccessSector, worldX, worldY))
            return false;
        long currentCost = ResolveDeterministicPortalAccessIntegrationCost(
            _world,
            portalAccessSector,
            portalAccess,
            worldX,
            worldY);
        if (currentCost == long.MaxValue)
        {
            throw new InvalidOperationException(
                $"TryResolveSpatialFieldDirectionFixed failed: portal access sample is unreachable cell=({worldX},{worldY}), " +
                $"sector={portalAccessSector.SectorId}, portal={portalAccess.PortalId}, agent={agent?.Id ?? -1}, " +
                $"handle={FormatPathHandle(handle)} {BuildPortalAccessFieldDiagnostics(_world, portalAccessSector, portalAccess, worldX, worldY)}");
        }

        direction = ResolveSpatiallyInterpolatedPortalAccessGradientFixed(
            portalAccessSector,
            portalAccess,
            position,
            worldX,
            worldY,
            currentCost,
            out _,
            out _);
        sampleTrace = $"portal-access sector={portalAccessSector.SectorId}/portal={portalAccess.PortalId}/cell=({worldX},{worldY})/cost={currentCost}/dirRaw=({direction.x.RawValue},{direction.y.RawValue})";
        if (direction != FixVector2.Zero)
            return true;
        if (!TryResolveLowestPortalAccessNeighbor(
                portalAccessSector,
                portalAccess,
                worldX,
                worldY,
                currentCost,
                out int bestDirectionIndex,
                out _))
        {
            throw new InvalidOperationException(
                $"TryResolveSpatialFieldDirectionFixed failed: portal access sample has no descending neighbor cell=({worldX},{worldY}), " +
                $"sector={portalAccessSector.SectorId}, portal={portalAccess.PortalId}.");
        }

        direction = new FixVector2(NeighborOffsetX[bestDirectionIndex], NeighborOffsetY[bestDirectionIndex]);
        sampleTrace += ";fallback=lowest-neighbor";
        return true;
    }

    private static FixVector2 ResolveSpatiallyInterpolatedPortalAccessGradientFixed(
        SectorData sector,
        SectorPortalAccessEntry access,
        FixVector2 position,
        int worldX,
        int worldY,
        long currentCost,
        out long currentGradientX,
        out long currentGradientY)
    {
        FixVector2 currentDirection = ResolveDeterministicPortalAccessGradientFixed(
            sector,
            access,
            worldX,
            worldY,
            currentCost,
            out currentGradientX,
            out currentGradientY);
        ResolveSpatialGradientSampleGridFixed(
            position,
            worldX,
            worldY,
            out int x0,
            out int x1,
            out int y0,
            out int y1,
            out Fix64 tx,
            out Fix64 ty);

        Fix64 oneMinusX = Fix64.One - tx;
        Fix64 oneMinusY = Fix64.One - ty;
        FixVector2 weightedDirection = FixVector2.Zero;
        Fix64 totalWeight = Fix64.Zero;
        AccumulateSpatialPortalAccessGradientSample(sector, access, worldX, worldY, x0, y0, oneMinusX * oneMinusY, ref weightedDirection, ref totalWeight);
        AccumulateSpatialPortalAccessGradientSample(sector, access, worldX, worldY, x1, y0, tx * oneMinusY, ref weightedDirection, ref totalWeight);
        AccumulateSpatialPortalAccessGradientSample(sector, access, worldX, worldY, x0, y1, oneMinusX * ty, ref weightedDirection, ref totalWeight);
        AccumulateSpatialPortalAccessGradientSample(sector, access, worldX, worldY, x1, y1, tx * ty, ref weightedDirection, ref totalWeight);

        return totalWeight > Fix64.Zero && weightedDirection != FixVector2.Zero
            ? weightedDirection
            : currentDirection;
    }

    private static void AccumulateSpatialPortalAccessGradientSample(
        SectorData sector,
        SectorPortalAccessEntry access,
        int currentX,
        int currentY,
        int sampleX,
        int sampleY,
        Fix64 weight,
        ref FixVector2 weightedDirection,
        ref Fix64 totalWeight)
    {
        if (weight <= Fix64.Zero || !IsInsideSector(sector, sampleX, sampleY))
            return;
        if ((sampleX != currentX || sampleY != currentY)
            && !CanTraverseNeighborCells(_world, currentX, currentY, sampleX, sampleY))
        {
            return;
        }
        if (sampleX != currentX
            && sampleY != currentY
            && !IsDiagonalPassable(_world, currentX, currentY, sampleX, sampleY))
        {
            return;
        }

        long sampleCost = ResolveDeterministicPortalAccessIntegrationCost(
            _world,
            sector,
            access,
            sampleX,
            sampleY);
        if (sampleCost == long.MaxValue)
            return;

        FixVector2 sampleDirection = ResolveDeterministicPortalAccessGradientFixed(
            sector,
            access,
            sampleX,
            sampleY,
            sampleCost,
            out _,
            out _);
        if (sampleDirection == FixVector2.Zero
            && TryResolveLowestPortalAccessNeighbor(
                sector,
                access,
                sampleX,
                sampleY,
                sampleCost,
                out int bestDirectionIndex,
                out _))
        {
            sampleDirection = new FixVector2(
                NeighborOffsetX[bestDirectionIndex],
                NeighborOffsetY[bestDirectionIndex]);
        }
        if (sampleDirection == FixVector2.Zero)
            return;

        weightedDirection += sampleDirection * weight;
        totalWeight += weight;
    }

    private static FixVector2 ResolveDeterministicPortalAccessGradientFixed(
        SectorData sector,
        SectorPortalAccessEntry access,
        int worldX,
        int worldY,
        long currentCost,
        out long gradientX,
        out long gradientY)
    {
        if (currentCost == long.MaxValue)
        {
            throw new InvalidOperationException(
                $"ResolveDeterministicPortalAccessGradientFixed failed: current cell is unreachable cell=({worldX},{worldY}), sector={sector.SectorId}, portal={access.PortalId}.");
        }

        gradientX = ResolveDeterministicPortalAccessGradientAxis(
            sector,
            access,
            worldX,
            worldY,
            currentCost,
            -2,
            0,
            2,
            0);
        gradientY = ResolveDeterministicPortalAccessGradientAxis(
            sector,
            access,
            worldX,
            worldY,
            currentCost,
            0,
            -2,
            0,
            2);
        long maximumMagnitude = Math.Max(Math.Abs(gradientX), Math.Abs(gradientY));
        if (maximumMagnitude == 0)
            return FixVector2.Zero;

        Fix64 scale = Fix64.FromRaw(maximumMagnitude);
        return new FixVector2(
            Fix64.FromRaw(gradientX) / scale,
            Fix64.FromRaw(gradientY) / scale);
    }

    private static long ResolveDeterministicPortalAccessGradientAxis(
        SectorData sector,
        SectorPortalAccessEntry access,
        int worldX,
        int worldY,
        long currentCost,
        int negativeOffsetX,
        int negativeOffsetY,
        int positiveOffsetX,
        int positiveOffsetY)
    {
        bool hasNegative = TryGetDeterministicPortalAccessGradientSample(
            sector,
            access,
            worldX,
            worldY,
            worldX + negativeOffsetX,
            worldY + negativeOffsetY,
            out long negativeCost);
        bool hasPositive = TryGetDeterministicPortalAccessGradientSample(
            sector,
            access,
            worldX,
            worldY,
            worldX + positiveOffsetX,
            worldY + positiveOffsetY,
            out long positiveCost);

        if (hasNegative && hasPositive)
            return negativeCost - positiveCost;
        if (hasNegative)
            return checked(2L * (negativeCost - currentCost));
        if (hasPositive)
            return checked(2L * (currentCost - positiveCost));
        return 0L;
    }

    private static bool TryGetDeterministicPortalAccessGradientSample(
        SectorData sector,
        SectorPortalAccessEntry access,
        int worldX,
        int worldY,
        int sampleX,
        int sampleY,
        out long cost)
    {
        cost = long.MaxValue;
        if (!IsInsideSector(sector, sampleX, sampleY)
            || !CanTraverseSamplePath(_world, worldX, worldY, sampleX, sampleY))
        {
            return false;
        }

        cost = ResolveDeterministicPortalAccessIntegrationCost(
            _world,
            sector,
            access,
            sampleX,
            sampleY);
        return cost != long.MaxValue;
    }

    private static FixVector2 ResolvePortalCrossingTargetFixed(
        FixVector2 position,
        FixVector2 oppositeCellCenter,
        bool isVerticalBoundary)
    {
        return isVerticalBoundary
            ? new FixVector2(oppositeCellCenter.x, position.y)
            : new FixVector2(position.x, oppositeCellCenter.y);
    }

    private static FixVector2 ResolvePortalApproachTargetFixed(
        FixVector2 position,
        Vector2Int selectedCurrentCell,
        FixVector2 oppositeCellCenter,
        bool isVerticalBoundary)
    {
        _world.GetGridCellBoundsFixed(
            selectedCurrentCell.x,
            selectedCurrentCell.y,
            out FixVector2 minimum,
            out FixVector2 maximum);
        if (isVerticalBoundary)
        {
            Fix64 tangent = Fix64.Clamp(position.y, minimum.y, maximum.y);
            return new FixVector2(oppositeCellCenter.x, tangent);
        }

        Fix64 horizontalTangent = Fix64.Clamp(position.x, minimum.x, maximum.x);
        return new FixVector2(horizontalTangent, oppositeCellCenter.y);
    }

    private static int FindPortalCellSlotIndex(Vector2Int[] portalCells, int worldX, int worldY)
    {
        if (portalCells == null)
            throw new InvalidOperationException("FindPortalCellSlotIndex failed: portal cells are null.");

        for (int i = 0; i < portalCells.Length; i++)
        {
            if (portalCells[i].x == worldX && portalCells[i].y == worldY)
                return i;
        }

        return -1;
    }

    private static int ResolveStablePortalTraversalSlotIndex(
        AgentNavState nav,
        FlowTileCacheKey key,
        int recommendedSlotIndex,
        int slotCount,
        bool recommendationFromCommittedTile)
    {
        if (nav == null)
            throw new InvalidOperationException("ResolveStablePortalTraversalSlotIndex failed: nav is null.");
        if (slotCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(slotCount), slotCount, "Portal slot count must be positive.");
        if (recommendedSlotIndex < 0 || recommendedSlotIndex >= slotCount)
            throw new ArgumentOutOfRangeException(nameof(recommendedSlotIndex), recommendedSlotIndex, "Recommended portal slot is invalid.");

        bool matchesCurrentPortal = nav.PortalTraversalWorldVersion == key.WorldVersion
                                    && nav.PortalTraversalSectorId == key.SectorId
                                    && nav.PortalTraversalId == key.GoalId
                                    && nav.PortalTraversalSlotIndex >= 0
                                    && nav.PortalTraversalSlotIndex < slotCount;
        if (!matchesCurrentPortal
            || (recommendationFromCommittedTile && !nav.PortalTraversalHasCommittedTileSlot))
        {
            return SetStablePortalTraversalSlotIndex(
                nav,
                key,
                recommendedSlotIndex,
                recommendationFromCommittedTile);
        }

        return nav.PortalTraversalSlotIndex;
    }

    private static int SetStablePortalTraversalSlotIndex(
        AgentNavState nav,
        FlowTileCacheKey key,
        int slotIndex,
        bool hasCommittedTileSlot)
    {
        nav.PortalTraversalWorldVersion = key.WorldVersion;
        nav.PortalTraversalSectorId = key.SectorId;
        nav.PortalTraversalId = key.GoalId;
        nav.PortalTraversalSlotIndex = slotIndex;
        nav.PortalTraversalHasCommittedTileSlot = hasCommittedTileSlot;
        return slotIndex;
    }

    private static int ResolvePendingPortalTargetSlotIndex(
        SectorData sector,
        SectorPortalAccessEntry access,
        Vector2Int[] portalCells,
        int startX,
        int startY,
        long startCost)
    {
        int worldX = startX;
        int worldY = startY;
        long currentCost = startCost;
        int guard = sector.Width * sector.Height + 1;
        while (guard-- > 0)
        {
            for (int i = 0; i < portalCells.Length; i++)
            {
                if (portalCells[i].x == worldX && portalCells[i].y == worldY)
                    return i;
            }

            if (!TryResolveLowestPortalAccessNeighbor(
                    sector,
                    access,
                    worldX,
                    worldY,
                    currentCost,
                    out int directionIndex,
                    out long nextCost))
            {
                throw new InvalidOperationException(
                    $"ResolvePendingPortalTargetSlotIndex failed: portal access field has no descending neighbor " +
                    $"cell=({worldX},{worldY}), cost={currentCost}, portal={access.PortalId}, sector={sector.SectorId}.");
            }

            worldX += NeighborOffsetX[directionIndex];
            worldY += NeighborOffsetY[directionIndex];
            currentCost = nextCost;
        }

        throw new InvalidOperationException(
            $"ResolvePendingPortalTargetSlotIndex failed: portal access path contains a cycle portal={access.PortalId}, sector={sector.SectorId}.");
    }

    private static bool TryResolveLowestPortalAccessNeighbor(
        SectorData sector,
        SectorPortalAccessEntry access,
        int worldX,
        int worldY,
        long currentCost,
        out int bestDirectionIndex,
        out long bestCost)
    {
        bestDirectionIndex = -1;
        bestCost = currentCost;
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int nextX = worldX + NeighborOffsetX[i];
            int nextY = worldY + NeighborOffsetY[i];
            if (!IsInsideSector(sector, nextX, nextY)
                || !CanTraverseNeighborCells(_world, worldX, worldY, nextX, nextY))
            {
                continue;
            }
            if (NeighborOffsetX[i] != 0
                && NeighborOffsetY[i] != 0
                && !IsDiagonalPassable(_world, worldX, worldY, nextX, nextY))
            {
                continue;
            }

            long nextCost = ResolveDeterministicPortalAccessIntegrationCost(
                _world,
                sector,
                access,
                nextX,
                nextY);
            if (nextCost >= bestCost)
                continue;

            bestCost = nextCost;
            bestDirectionIndex = i;
        }

        return bestDirectionIndex >= 0;
    }

    private static FixVector2 ResolvePortalGoalCellVelocityFixed(
        FlowTileCacheEntry tile,
        FixVector2 position,
        int worldX,
        int worldY,
        Fix64 maxSpeed)
    {
        PortalData portal = GetPortalById(_world, tile.Key.GoalId);
        Vector2Int[] currentCells = GetPortalCellsForSector(portal, tile.Key.SectorId);
        Vector2Int[] oppositeCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, tile.Key.SectorId));
        if (currentCells.Length != oppositeCells.Length)
        {
            throw new InvalidOperationException(
                $"ResolvePortalGoalCellVelocityFixed failed: portal side cell count mismatch portal={portal.PortalId}, current={currentCells.Length}, opposite={oppositeCells.Length}.");
        }

        for (int i = 0; i < currentCells.Length; i++)
        {
            if (currentCells[i].x != worldX || currentCells[i].y != worldY)
                continue;

            FixVector2 oppositeCellCenter = _world.GridToWorldCenterFixed(oppositeCells[i].x, oppositeCells[i].y);
            FixVector2 target = ResolvePortalCrossingTargetFixed(position, oppositeCellCenter, portal.IsVerticalBoundary);
            return ScaleFixedDirectionToSpeed(target - position, maxSpeed);
        }

        throw new InvalidOperationException(
            $"ResolvePortalGoalCellVelocityFixed failed: zero direction cell is not a portal goal cell. cell=({worldX},{worldY}), key={FormatTileKey(tile.Key)}.");
    }

    private const int FixedPortalInfluenceCells = 2;
    private const int FixedPortalMinimumOwnerTicks = 3;
    private const int FixedPortalMaximumOwnerTicks = 45;
    private const int FixedCorridorBuildOperationQuota = 256;

}
