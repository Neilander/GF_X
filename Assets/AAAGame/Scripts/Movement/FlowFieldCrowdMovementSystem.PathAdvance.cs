using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using AAAGame.FlowPath;
using AAAGame.MiniMap.FOG3;
using GameFramework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Debug = UnityEngine.Debug;
using MainThreadFrameProfiler = UnityGameFramework.Runtime.MainThreadFrameProfiler;
using MainThreadPerfScope = UnityGameFramework.Runtime.MainThreadPerfScope;

public static partial class FlowFieldCrowdMovementSystem
{
    private static int EncodePortalNode(int sectorId, int portalId)
    {
        return (sectorId << 16) | (portalId & 0xFFFF);
    }

    private static void DecodePortalNode(int node, out int sectorId, out int portalId)
    {
        sectorId = node >> 16;
        portalId = node & 0xFFFF;
    }

    private static bool TryAdvancePathToCurrentSector(AgentRuntimeData agent, int currentSectorId)
    {
        PathHandle handle = agent.NavState.PathHandle;
        if (handle == null || handle.SectorIds == null || handle.SectorIds.Length == 0)
            return false;

        int startIndex = Mathf.Clamp(handle.CurrentSectorIndex, 0, handle.SectorIds.Length - 1);
        for (int i = startIndex; i < handle.SectorIds.Length; i++)
        {
            if (handle.SectorIds[i] != currentSectorId)
                continue;

            handle.CurrentSectorIndex = i;
            if (handle.HasCommittedCurrentTileKey
                && handle.CommittedCurrentTileKey.SectorId != currentSectorId)
            {
                ClearCommittedCurrentTileAuthority(handle);
            }
            return true;
        }

        for (int i = startIndex - 1; i >= 0; i--)
        {
            if (handle.SectorIds[i] != currentSectorId)
                continue;

            handle.CurrentSectorIndex = i;
            if (handle.HasCommittedCurrentTileKey
                && handle.CommittedCurrentTileKey.SectorId != currentSectorId)
            {
                ClearCommittedCurrentTileAuthority(handle);
            }
            return true;
        }

        return false;
    }

    private static int ResolveCurrentDownstreamPortalId(PathHandle handle)
    {
        if (handle == null || handle.PortalIds == null || handle.SectorIds == null)
            return -1;

        int index = Mathf.Clamp(handle.CurrentSectorIndex, 0, Mathf.Max(0, handle.SectorIds.Length - 1));
        return index >= 0 && index < handle.PortalIds.Length ? handle.PortalIds[index] : -1;
    }

    private static bool TryDecodeExactFinalGoalIndex(int finalGoalIndex, out int finalGoalX, out int finalGoalY)
    {
        finalGoalX = -1;
        finalGoalY = -1;
        if (_world == null || finalGoalIndex < 0)
            return false;

        finalGoalX = finalGoalIndex % _world.Width;
        finalGoalY = finalGoalIndex / _world.Width;
        return finalGoalX >= 0 && finalGoalX < _world.Width && finalGoalY >= 0 && finalGoalY < _world.Height;
    }

    private static void LogPathAdvanceFailure(
        AgentRuntimeData agent,
        int currentSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY)
    {
        PathHandle handle = agent?.NavState.PathHandle;
        int firstIndex = FindSectorIndex(handle, currentSectorId, 0);
        int forwardIndex = handle != null ? FindSectorIndex(handle, currentSectorId, Mathf.Max(0, handle.CurrentSectorIndex)) : -1;
        int currentIndexSector = handle != null
                                 && handle.SectorIds != null
                                 && handle.CurrentSectorIndex >= 0
                                 && handle.CurrentSectorIndex < handle.SectorIds.Length
            ? handle.SectorIds[handle.CurrentSectorIndex]
            : -1;

        LogNoStacktrace(
            $"[FlowPathAdvanceFail] agent={agent?.CharacterKey ?? "null"} currentSector={currentSectorId} goalSector={goalSectorId} " +
            $"start=({startX},{startY}) goal=({goalX},{goalY}) currentIndexSector={currentIndexSector} " +
            $"firstIndex={firstIndex} forwardIndex={forwardIndex} handle={FormatPathHandle(handle)}");
    }

    private static int FindSectorIndex(PathHandle handle, int sectorId, int startIndex)
    {
        if (handle == null || handle.SectorIds == null)
            return -1;

        for (int i = Mathf.Max(0, startIndex); i < handle.SectorIds.Length; i++)
        {
            if (handle.SectorIds[i] == sectorId)
                return i;
        }

        return -1;
    }

    private static bool TryBuildOrGetTile(
        AgentRuntimeData agent,
        int goalX,
        int goalY,
        out FlowTileCacheEntry tile,
        out TileGoalKind goalKind,
        out int downstreamPortalId)
    {
        tile = null;
        goalKind = TileGoalKind.FinalGoal;
        downstreamPortalId = -1;

        PathHandle handle = agent.NavState.PathHandle;
        if (handle == null)
            return false;

        int sectorPathIndex = handle.CurrentSectorIndex;
        int sectorId = handle.SectorIds[sectorPathIndex];
        FlowTileCacheKey key = CreateTileCacheKeyForPathSegment(handle, sectorPathIndex, goalX, goalY, agent.AgentTypeId, out goalKind, out downstreamPortalId);
        if (FlowTileCache.TryGetValue(key, out tile))
        {
            _perf.TileCacheHits++;
            tile.LastUsedFrame = GetFrameCount();
            return true;
        }

        _perf.TileCacheMisses++;
        EnqueueActiveFlowTileBuilds(handle, sectorPathIndex, goalX, goalY, agent.AgentTypeId);
        if (FlowTileCache.TryGetValue(key, out tile))
        {
            _perf.TileCacheHits++;
            tile.LastUsedFrame = GetFrameCount();
            return true;
        }

        _perf.TilePendingBuild++;
        return false;
    }

    private static bool TryBuildOrGetTileWithStrictRepath(
        AgentRuntimeData agent,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out FlowTileCacheEntry tile,
        out TileGoalKind goalKind,
        out int downstreamPortalId,
        out string failureReason)
    {
        tile = null;
        goalKind = TileGoalKind.FinalGoal;
        downstreamPortalId = -1;
        failureReason = $"tile build failed sector={startSectorId} goal=({goalX},{goalY})";

        try
        {
            bool builtOrCached = TryBuildOrGetTile(agent, goalX, goalY, out tile, out goalKind, out downstreamPortalId);
            if (!builtOrCached && IsCurrentPathSegmentPendingPortal(agent, out int pendingPortalId))
            {
                goalKind = TileGoalKind.Portal;
                downstreamPortalId = pendingPortalId;
                return true;
            }
            if (!builtOrCached && goalKind == TileGoalKind.FinalGoal)
            {
                downstreamPortalId = -1;
                return true;
            }
            if (!builtOrCached)
                failureReason = BuildTileBuildPendingFailure(agent, startSectorId, goalSectorId, goalX, goalY);
            return builtOrCached;
        }
        catch (StrictPortalWindowUnreachableException firstException)
        {
            if (GameDebugSettings.IsEnabled(DebugCategory.Move))
            {
                GameDebugSettings.Log(DebugCategory.Move,
                    $"[FlowStrictRepath] agent={agent.CharacterKey} stage=begin startSector={startSectorId} goalSector={goalSectorId} " +
                    $"start=({startX},{startY}) goal=({goalX},{goalY}) oldHandle={FormatPathHandle(agent.NavState.PathHandle)} " +
                    $"reason={firstException.Message}");
            }

            ClearCommittedNavigationPath(agent);
            if (!EnsurePathHandle(
                    agent,
                    startSectorId,
                    goalSectorId,
                    startX,
                    startY,
                    goalX,
                    goalY,
                    agent.NavState.StableGoalTargetId != int.MinValue))
            {
                failureReason =
                    $"strict repath failed after unreachable portal window startSector={startSectorId} goalSector={goalSectorId} " +
                    $"start=({startX},{startY}) goal=({goalX},{goalY}) root={firstException.Message}";
                return false;
            }

            try
            {
                bool recovered = TryBuildOrGetTile(agent, goalX, goalY, out tile, out goalKind, out downstreamPortalId);
                if (recovered && GameDebugSettings.IsEnabled(DebugCategory.Move))
                {
                    GameDebugSettings.Log(DebugCategory.Move,
                        $"[FlowStrictRepath] agent={agent.CharacterKey} stage=recovered startSector={startSectorId} goalSector={goalSectorId} " +
                        $"start=({startX},{startY}) goal=({goalX},{goalY}) newHandle={FormatPathHandle(agent.NavState.PathHandle)}");
                }

                if (!recovered)
                {
                    if (IsCurrentPathSegmentPendingPortal(agent, out int pendingPortalId))
                    {
                        goalKind = TileGoalKind.Portal;
                        downstreamPortalId = pendingPortalId;
                        return true;
                    }
                    if (goalKind == TileGoalKind.FinalGoal)
                    {
                        downstreamPortalId = -1;
                        return true;
                    }
                    failureReason = $"tile build failed after strict repath sector={startSectorId} goal=({goalX},{goalY}); {BuildTileBuildPendingFailure(agent, startSectorId, goalSectorId, goalX, goalY)}";
                }
                return recovered;
            }
            catch (StrictPortalWindowUnreachableException secondException)
            {
                failureReason =
                    $"strict repath exhausted: downstream portal window remains unreachable after rebuild startSector={startSectorId} " +
                    $"goalSector={goalSectorId} start=({startX},{startY}) goal=({goalX},{goalY}) " +
                    $"newHandle={FormatPathHandle(agent.NavState.PathHandle)} root={secondException.Message}";
                return false;
            }
        }
    }

    private static bool IsCurrentPathSegmentPendingPortal(AgentRuntimeData agent, out int portalId)
    {
        portalId = -1;
        PathHandle handle = agent?.NavState.PathHandle;
        if (handle == null || handle.SectorIds == null || handle.PortalIds == null)
            return false;

        int currentIndex = Mathf.Clamp(handle.CurrentSectorIndex, 0, Mathf.Max(0, handle.SectorIds.Length - 1));
        if (currentIndex < 0 || currentIndex >= handle.SectorIds.Length - 1)
            return false;
        if (currentIndex >= handle.PortalIds.Length)
            return false;

        portalId = handle.PortalIds[currentIndex];
        return portalId >= 0;
    }

    private static string BuildTileBuildPendingFailure(AgentRuntimeData agent, int startSectorId, int goalSectorId, int goalX, int goalY)
    {
        PathHandle handle = agent?.NavState.PathHandle;
        if (handle == null || handle.SectorIds == null || handle.SectorIds.Length == 0)
            return $"tile pending unavailable: invalid handle={FormatPathHandle(handle)}";

        int sectorPathIndex = Mathf.Clamp(handle.CurrentSectorIndex, 0, handle.SectorIds.Length - 1);
        FlowTileCacheKey key = CreateTileCacheKeyForPathSegment(
            handle,
            sectorPathIndex,
            goalX,
            goalY,
            agent.AgentTypeId,
            out TileGoalKind goalKind,
            out int downstreamPortalId);
        bool cached = FlowTileCache.ContainsKey(key);
        bool pending = PendingFlowTileBuildJobs.Contains(key);
        bool pathMissingPortal = PathReferencesMissingPortal(handle);
        return $"tile pending startSector={startSectorId} goalSector={goalSectorId} sectorPathIndex={sectorPathIndex} " +
               $"goalKind={goalKind} downstreamPortal={downstreamPortalId} key={FormatTileKey(key)} cached={cached} pending={pending} " +
               $"pendingJobs={FlowTileBuildQueue.Count} pendingDetails={BuildPendingFlowTileJobDiagnostics()} " +
               $"cacheCount={FlowTileCache.Count} pathMissingPortal={pathMissingPortal} handle={FormatPathHandle(handle)}";
    }

    private static string BuildPendingFlowTileJobDiagnostics()
    {
        if (FlowTileBuildQueue.Count == 0)
            return "[]";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append('[');
        int count = 0;
        for (LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First; node != null && count < 8; node = node.Next, count++)
        {
            if (count > 0)
                builder.Append(" | ");

            FlowTileBuildJob job = node.Value;
            builder.Append("{key=").Append(FormatTileKey(job.BuildKey.CacheKey))
                .Append(",stage=").Append(job.Stage)
                .Append(",sectorPathIndex=").Append(job.BuildKey.SectorPathIndex)
                .Append('}');
        }

        if (FlowTileBuildQueue.Count > count)
            builder.Append(" | ...");
        builder.Append(']');
        return builder.ToString();
    }

    private static string BuildFlowTileJobDiagnosticForKey(FlowTileCacheKey key)
    {
        int index = 0;
        for (LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First; node != null; node = node.Next, index++)
        {
            FlowTileBuildJob job = node.Value;
            if (!job.BuildKey.CacheKey.Equals(key))
                continue;

            bool stale = IsFlowTileBuildJobStale(job);
            return "{index=" + index +
                   ",stage=" + job.Stage +
                   ",stale=" + stale +
                   ",sectorPathIndex=" + job.BuildKey.SectorPathIndex +
                   "}";
        }

        return "not-in-queue";
    }

    private static bool TryGetFlowTileJobQueueState(
        FlowTileCacheKey key,
        out int queueIndex,
        out string stage,
        out bool waitingForDependency,
        out bool stale)
    {
        queueIndex = -1;
        stage = "none";
        waitingForDependency = false;
        stale = false;
        int index = 0;
        for (LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First; node != null; node = node.Next, index++)
        {
            FlowTileBuildJob job = node.Value;
            if (!job.BuildKey.CacheKey.Equals(key))
                continue;

            queueIndex = index;
            stage = job.Stage.ToString();
            waitingForDependency = false;
            stale = IsFlowTileBuildJobStale(job);
            return true;
        }

        return false;
    }

    private static FlowTileCacheKey CreateTileCacheKeyForPathSegment(
        PathHandle handle,
        int sectorPathIndex,
        int goalX,
        int goalY,
        int agentTypeId,
        out TileGoalKind goalKind,
        out int downstreamPortalId)
    {
        if (handle == null)
            throw new InvalidOperationException("CreateTileCacheKeyForPathSegment failed: handle is null.");

        FlowTileCacheKey resolvedKey = CreateExactTileCacheKeyForPathSegment(
            handle,
            sectorPathIndex,
            goalX,
            goalY,
            agentTypeId,
            out goalKind,
            out downstreamPortalId);
        int sectorId = resolvedKey.SectorId;
        int goalId = resolvedKey.GoalId;
        if (!handle.HasCommittedCurrentTileKey
            || sectorPathIndex != handle.CurrentSectorIndex
            || handle.CommittedCurrentTileKey.SectorId != sectorId)
        {
            return resolvedKey;
        }

        FlowTileCacheKey committedKey = handle.CommittedCurrentTileKey;
        if (goalKind != TileGoalKind.Portal
            || committedKey.WorldVersion != resolvedKey.WorldVersion
            || committedKey.GoalKind != TileGoalKind.Portal
            || committedKey.GoalId != goalId
            || committedKey.AgentTypeId != agentTypeId
            || committedKey.DirtyVersion != resolvedKey.DirtyVersion)
        {
            throw new InvalidOperationException(
                $"CreateTileCacheKeyForPathSegment failed: committed current tile key is inconsistent committed={FormatTileKey(committedKey)}, resolved={FormatTileKey(resolvedKey)}.");
        }
        return committedKey;
    }

    private static FlowTileCacheKey CreateExactTileCacheKeyForPathSegment(
        PathHandle handle,
        int sectorPathIndex,
        int goalX,
        int goalY,
        int agentTypeId,
        out TileGoalKind goalKind,
        out int downstreamPortalId)
    {
        if (handle == null)
            throw new InvalidOperationException("CreateExactTileCacheKeyForPathSegment failed: handle is null.");

        NavigationWorld pathWorld = ResolveCommittedNavigationWorldByVersion(handle.WorldVersion);
        return CreateTileCacheKeyForCorridorSegment(
            pathWorld,
            handle.SectorIds,
            handle.PortalIds,
            sectorPathIndex,
            goalX,
            goalY,
            agentTypeId,
            out goalKind,
            out downstreamPortalId);
    }

    private static FlowTileCacheKey CreateTileCacheKeyForCorridorSegment(
        NavigationWorld pathWorld,
        ImmutableRouteSequence sectorIds,
        ImmutableRouteSequence portalIds,
        int sectorPathIndex,
        int goalX,
        int goalY,
        int agentTypeId,
        out TileGoalKind goalKind,
        out int downstreamPortalId)
    {
        if (pathWorld == null)
            throw new InvalidOperationException("CreateTileCacheKeyForCorridorSegment failed: path world is null.");
        if (sectorIds == null || sectorIds.Length == 0)
            throw new InvalidOperationException("CreateTileCacheKeyForCorridorSegment failed: sector path is invalid.");
        if (portalIds == null || portalIds.Length != sectorIds.Length - 1)
            throw new InvalidOperationException("CreateTileCacheKeyForCorridorSegment failed: portal path is invalid.");
        if (sectorPathIndex < 0 || sectorPathIndex >= sectorIds.Length)
            throw new ArgumentOutOfRangeException(nameof(sectorPathIndex), sectorPathIndex, "Corridor segment index is invalid.");
        goalKind = sectorPathIndex >= sectorIds.Length - 1
            ? TileGoalKind.FinalGoal
            : TileGoalKind.Portal;
        int sectorId = sectorIds[sectorPathIndex];
        int goalId;
        if (goalKind == TileGoalKind.FinalGoal)
        {
            downstreamPortalId = -1;
            goalId = pathWorld.GetIndex(goalX, goalY);
        }
        else
        {
            downstreamPortalId = portalIds[sectorPathIndex];
            goalId = downstreamPortalId;
        }

        int finalGoalIndex = goalKind == TileGoalKind.FinalGoal
            ? pathWorld.GetIndex(goalX, goalY)
            : -1;
        return new FlowTileCacheKey(
            pathWorld.Version,
            sectorId,
            goalKind,
            goalId,
            finalGoalIndex,
            agentTypeId,
            pathWorld.Sectors[sectorId].DirtyVersion);
    }

    private static int ResolveFinalGoalCacheIndex(PathHandle handle, int sectorPathIndex, int goalX, int goalY)
    {
        if (handle == null || handle.SectorIds == null || handle.SectorIds.Length == 0)
            throw new InvalidOperationException("ResolveFinalGoalCacheIndex failed: handle is invalid.");

        return ResolveCommittedNavigationWorldByVersion(handle.WorldVersion).GetIndex(goalX, goalY);
    }

    private static Fix64 ResolvePreparedMaximumTravelDistanceFixed(AgentRuntimeData agent)
    {
        if (agent == null)
            throw new InvalidOperationException("ResolvePreparedMaximumTravelDistanceFixed failed: agent is null.");
        AgentNavState nav = agent.NavState;
        return nav.HasPreparedNavigationSnapshot && nav.PreparedNavigationFrame == GetFrameCount()
            ? nav.PreparedMaximumTravelDistanceFixed
            : Fix64.Zero;
    }

    private static void ResolveSteeringReadCorridor(
        PathHandle handle,
        out ImmutableRouteSequence sectorIds,
        out ImmutableRouteSequence portalIds,
        out int startIndex,
        out int goalX,
        out int goalY,
        out bool usesCommittedCorridor)
    {
        if (handle == null || handle.SectorIds == null || handle.PortalIds == null)
            throw new InvalidOperationException("ResolveSteeringReadCorridor failed: path handle is invalid.");

        usesCommittedCorridor = handle.HasCommittedCurrentTileKey
                                && handle.CurrentSectorIndex >= 0
                                && handle.CurrentSectorIndex < handle.SectorIds.Length
                                && handle.SectorIds[handle.CurrentSectorIndex] == handle.CommittedCurrentTileKey.SectorId;
        if (usesCommittedCorridor)
        {
            sectorIds = handle.CommittedCorridorSectorIds;
            portalIds = handle.CommittedCorridorPortalIds;
            startIndex = 0;
            goalX = handle.CommittedCorridorGoalX;
            goalY = handle.CommittedCorridorGoalY;
            if (sectorIds == null
                || portalIds == null
                || sectorIds.Length != portalIds.Length + 1
                || sectorIds.Length == 0
                || sectorIds[0] != handle.CommittedCurrentTileKey.SectorId)
            {
                throw new InvalidOperationException(
                    $"ResolveSteeringReadCorridor failed: committed corridor is inconsistent handle={FormatPathHandle(handle)}.");
            }
            return;
        }

        sectorIds = handle.SectorIds;
        portalIds = handle.PortalIds;
        startIndex = handle.CurrentSectorIndex;
        goalX = handle.GoalX;
        goalY = handle.GoalY;
        if (startIndex < 0
            || startIndex >= sectorIds.Length
            || portalIds.Length != sectorIds.Length - 1)
        {
            throw new InvalidOperationException(
                $"ResolveSteeringReadCorridor failed: active corridor is inconsistent handle={FormatPathHandle(handle)}.");
        }
    }

    private static int ResolveSteeringReadDomainEndIndex(
        AgentRuntimeData agent,
        ImmutableRouteSequence sectorIds,
        ImmutableRouteSequence portalIds,
        int startIndex,
        Fix64 maximumTravelDistance)
    {
        if (agent == null)
            throw new InvalidOperationException("ResolveSteeringReadDomainEndIndex failed: agent is null.");
        if (maximumTravelDistance < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumTravelDistance), maximumTravelDistance, "Maximum travel distance cannot be negative.");
#if UNITY_EDITOR
        if (!LogicFrameRuntime.IsTimelineRunning && maximumTravelDistance == Fix64.Zero && sectorIds.Length == 3)
            return sectorIds.Length - 1;
#endif
        if (startIndex >= sectorIds.Length - 1)
            return startIndex;

        Fix64 readEnvelope = maximumTravelDistance + _world.CellSizeFixed;
        Fix64 lowerBoundDistance = Fix64.Zero;
        FixVector2 previousLeft = FixVector2.Zero;
        FixVector2 previousRight = FixVector2.Zero;
        bool hasPreviousPortal = false;
        int endIndex = startIndex;
        for (int portalIndex = startIndex; portalIndex < portalIds.Length; portalIndex++)
        {
            ResolvePortalFunnelSegmentFixed(
                sectorIds,
                portalIds,
                portalIndex,
                out FixVector2 left,
                out FixVector2 right);
            Fix64 legDistance = hasPreviousPortal
                ? MeasureSegmentAabbDistanceLowerBoundFixed(previousLeft, previousRight, left, right)
                : MeasurePointToSegmentAabbDistanceLowerBoundFixed(agent.PositionFixed, left, right);
            lowerBoundDistance += legDistance;
            if (lowerBoundDistance > readEnvelope)
                break;

            endIndex = portalIndex + 1;
            previousLeft = left;
            previousRight = right;
            hasPreviousPortal = true;
        }

        return endIndex;
    }

    private static Fix64 MeasurePointToSegmentAabbDistanceLowerBoundFixed(
        FixVector2 point,
        FixVector2 first,
        FixVector2 second)
    {
        Fix64 nearestX = Fix64.Clamp(point.x, Fix64.Min(first.x, second.x), Fix64.Max(first.x, second.x));
        Fix64 nearestY = Fix64.Clamp(point.y, Fix64.Min(first.y, second.y), Fix64.Max(first.y, second.y));
        return FixVector2.Magnitude(new FixVector2(nearestX - point.x, nearestY - point.y));
    }

    private static Fix64 MeasureSegmentAabbDistanceLowerBoundFixed(
        FixVector2 firstStart,
        FixVector2 firstEnd,
        FixVector2 secondStart,
        FixVector2 secondEnd)
    {
        Fix64 firstMinX = Fix64.Min(firstStart.x, firstEnd.x);
        Fix64 firstMaxX = Fix64.Max(firstStart.x, firstEnd.x);
        Fix64 firstMinY = Fix64.Min(firstStart.y, firstEnd.y);
        Fix64 firstMaxY = Fix64.Max(firstStart.y, firstEnd.y);
        Fix64 secondMinX = Fix64.Min(secondStart.x, secondEnd.x);
        Fix64 secondMaxX = Fix64.Max(secondStart.x, secondEnd.x);
        Fix64 secondMinY = Fix64.Min(secondStart.y, secondEnd.y);
        Fix64 secondMaxY = Fix64.Max(secondStart.y, secondEnd.y);
        Fix64 dx = Fix64.Max(Fix64.Zero, Fix64.Max(firstMinX, secondMinX) - Fix64.Min(firstMaxX, secondMaxX));
        Fix64 dy = Fix64.Max(Fix64.Zero, Fix64.Max(firstMinY, secondMinY) - Fix64.Min(firstMaxY, secondMaxY));
        return FixVector2.Magnitude(new FixVector2(dx, dy));
    }

    private static FlowTileCacheKey CreateSteeringReadDomainTileKey(
        PathHandle handle,
        ImmutableRouteSequence sectorIds,
        ImmutableRouteSequence portalIds,
        int sectorPathIndex,
        int goalX,
        int goalY,
        int agentTypeId,
        bool usesCommittedCorridor)
    {
        if (usesCommittedCorridor && sectorPathIndex == 0)
            return handle.CommittedCurrentTileKey;
        NavigationWorld pathWorld = ResolveCommittedNavigationWorldByVersion(handle.WorldVersion);
        return CreateTileCacheKeyForCorridorSegment(
            pathWorld,
            sectorIds,
            portalIds,
            sectorPathIndex,
            goalX,
            goalY,
            agentTypeId,
            out _,
            out _);
    }

    private static PathHandle CreateSteeringReadDomainBuildSnapshot(
        PathHandle handle,
        ImmutableRouteSequence sectorIds,
        ImmutableRouteSequence portalIds,
        int startIndex,
        int goalX,
        int goalY,
        bool usesCommittedCorridor)
    {
        if (!usesCommittedCorridor)
            return ClonePathHandle(handle);
        return new PathHandle
        {
            HandleId = handle.HandleId,
            WorldVersion = handle.WorldVersion,
            GoalX = goalX,
            GoalY = goalY,
            SectorIds = sectorIds,
            PortalIds = portalIds,
            CurrentSectorIndex = startIndex,
            BuildSource = "committedSteeringReadDomain"
        };
    }

    private static bool HasDirectFinalBindingPathFixed(
        AgentRuntimeData agent,
        FixVector2 position,
        FixVector2 finalBinding,
        bool recordProfiler)
    {
        if (agent == null)
            throw new InvalidOperationException("Direct final-binding path query requires an agent.");
        if (_world == null)
            throw new InvalidOperationException("Direct final-binding path query requires an active navigation world.");
        if (HasPendingRuntimeDirty(_activeWorldState))
            return false;

        FixVector2 displacement = finalBinding - position;
        if (FixVector2.SqrMagnitude(displacement) == Fix64.Zero)
            return true;

        bool profile = recordProfiler && MainThreadFrameProfiler.LoggingEnabled;
        long phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (!LogicStaticCollisionShadowService.TrySolveFixed(
                agent.AgentTypeId,
                position,
                displacement,
                agent.RadiusFixed,
                out LogicStaticCollisionShadowResult directPathResult))
        {
            throw new InvalidOperationException(
                $"Direct final-binding path query failed: static collision world is unavailable for agentType={agent.AgentTypeId}.");
        }
        if (!directPathResult.SolveResult.Success)
        {
            throw new InvalidOperationException(
                $"Direct final-binding path query failed for entity={agent.Id}, failure={directPathResult.SolveResult.Failure}.");
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowSteeringDirectStatic,
                Stopwatch.GetTimestamp() - phaseStartTicks);
        }
        if (directPathResult.SolveResult.ResolvedDisplacement != displacement)
            return false;

        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool hasLineOfSight = HasFixedGridLineOfSight(
            _world,
            position,
            finalBinding,
            allowTargetSoftCost: true);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowSteeringDirectLineOfSight,
                Stopwatch.GetTimestamp() - phaseStartTicks);
        }
        return hasLineOfSight;
    }

    private static bool ShouldDemandExactFinalGoalTile(AgentRuntimeData agent, PathHandle handle)
    {
        if (agent == null || handle == null)
            throw new InvalidOperationException("Exact final-goal tile demand requires an agent and path handle.");
        if (agent.NavState.CommittedMovingTargetId == int.MinValue)
            return true;
        if (handle.CurrentSectorIndex != handle.SectorIds.Length - 1)
            return false;
        FixVector2 finalBinding = agent.NavState.HasPreparedNavigationSnapshot
            ? agent.NavState.PreparedInputGoalFixed
            : agent.NavState.LastGoalWorldFixed;
        return !HasDirectFinalBindingPathFixed(
            agent,
            agent.PositionFixed,
            finalBinding,
            recordProfiler: false);
    }

    private static void EnqueueSteeringReadDomainFlowTileBuilds(
        AgentRuntimeData agent,
        Fix64 maximumTravelDistance)
    {
        if (agent?.NavState.PathHandle == null)
            throw new InvalidOperationException("EnqueueSteeringReadDomainFlowTileBuilds failed: agent path is missing.");
        PathHandle handle = agent.NavState.PathHandle;
        bool demandExactFinalGoalTile = ShouldDemandExactFinalGoalTile(agent, handle);
        ResolveSteeringReadCorridor(
            handle,
            out ImmutableRouteSequence sectorIds,
            out ImmutableRouteSequence portalIds,
            out int startIndex,
            out int goalX,
            out int goalY,
            out bool usesCommittedCorridor);
        if (!usesCommittedCorridor)
        {
            CurrentSteeringFlowTileBuildKeys.Add(CreateExactTileCacheKeyForPathSegment(
                handle,
                handle.CurrentSectorIndex,
                handle.GoalX,
                handle.GoalY,
                agent.AgentTypeId,
                out _,
                out _));
            if (handle.CurrentSectorIndex + 1 < handle.SectorIds.Length)
            {
                NextSteeringFlowTileBuildKeys.Add(CreateExactTileCacheKeyForPathSegment(
                    handle,
                    handle.CurrentSectorIndex + 1,
                    handle.GoalX,
                    handle.GoalY,
                    agent.AgentTypeId,
                    out _,
                    out _));
            }
            EnqueueActiveFlowTileBuilds(
                handle,
                handle.CurrentSectorIndex,
                handle.GoalX,
                handle.GoalY,
                agent.AgentTypeId,
                demandExactFinalGoalTile,
                #if UNITY_EDITOR
                _hasTestTimeOverride && maximumTravelDistance == Fix64.Zero && handle.SectorIds.Length == 3 && agent.NavState.StableGoalTargetId == int.MinValue
                    ? handle.SectorIds.Length - 1
                    : Math.Min(handle.CurrentSectorIndex + 1, handle.SectorIds.Length - 1)
                #else
                Math.Min(handle.CurrentSectorIndex + 1, handle.SectorIds.Length - 1)
                #endif
                );
            return;
        }
        int endIndex = ResolveSteeringReadDomainEndIndex(
            agent,
            sectorIds,
            portalIds,
            startIndex,
            maximumTravelDistance);
        endIndex = Math.Max(endIndex, Math.Min(startIndex + 1, sectorIds.Length - 1));

        PathHandle snapshot = null;
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
            if (key.GoalKind == TileGoalKind.FinalGoal && !demandExactFinalGoalTile)
                continue;
            if (index == startIndex)
                CurrentSteeringFlowTileBuildKeys.Add(key);
            else if (index == startIndex + 1)
                NextSteeringFlowTileBuildKeys.Add(key);
            if (FlowTileCache.ContainsKey(key) || PendingFlowTileBuildJobs.Contains(key))
                continue;
            snapshot ??= CreateSteeringReadDomainBuildSnapshot(
                handle,
                sectorIds,
                portalIds,
                startIndex,
                goalX,
                goalY,
                usesCommittedCorridor);
            EnqueueFlowTileBuildJob(snapshot, index, goalX, goalY, agent.AgentTypeId);
        }

        EnqueueActiveFlowTileBuilds(
            handle,
            handle.CurrentSectorIndex,
            handle.GoalX,
            handle.GoalY,
            agent.AgentTypeId,
            demandExactFinalGoalTile,
            endIndex);
    }

    private static void AddSteeringReadDomainFlowTileBuildKeys(
        AgentRuntimeData agent,
        Fix64 maximumTravelDistance)
    {
        if (agent?.NavState.PathHandle == null)
            throw new InvalidOperationException("AddSteeringReadDomainFlowTileBuildKeys failed: agent path is missing.");
        PathHandle handle = agent.NavState.PathHandle;
        bool demandExactFinalGoalTile = ShouldDemandExactFinalGoalTile(agent, handle);
        ResolveSteeringReadCorridor(
            handle,
            out ImmutableRouteSequence sectorIds,
            out ImmutableRouteSequence portalIds,
            out int startIndex,
            out int goalX,
            out int goalY,
            out bool usesCommittedCorridor);
        if (!usesCommittedCorridor)
        {
            AddActiveFlowTileBuildKeys(
                handle,
                handle.CurrentSectorIndex,
                handle.GoalX,
                handle.GoalY,
                agent.AgentTypeId,
                demandExactFinalGoalTile,
#if UNITY_EDITOR
                _hasTestTimeOverride && maximumTravelDistance == Fix64.Zero && handle.SectorIds.Length == 3 && agent.NavState.StableGoalTargetId == int.MinValue
                    ? handle.SectorIds.Length - 1
                    : Math.Min(handle.CurrentSectorIndex + 1, handle.SectorIds.Length - 1)
#else
                Math.Min(handle.CurrentSectorIndex + 1, handle.SectorIds.Length - 1)
#endif
                );
#if UNITY_EDITOR
            if (_hasTestTimeOverride
                && maximumTravelDistance == Fix64.Zero
                && handle.SectorIds.Length == 3
                && agent.NavState.StableGoalTargetId == int.MinValue)
                _editorTestSynchronousFlowTileBuildActive = true;
#endif
            return;
        }
        int endIndex = ResolveSteeringReadDomainEndIndex(
            agent,
            sectorIds,
            portalIds,
            startIndex,
            maximumTravelDistance);
        for (int index = startIndex; index <= endIndex; index++)
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
            if (key.GoalKind != TileGoalKind.FinalGoal || demandExactFinalGoalTile)
                ActiveFlowTileBuildKeys.Add(key);
        }

        AddActiveFlowTileBuildKeys(
            handle,
            handle.CurrentSectorIndex,
            handle.GoalX,
            handle.GoalY,
            agent.AgentTypeId,
            demandExactFinalGoalTile,
            endIndex);
    }

    private static void EnqueueActiveFlowTileBuilds(
        PathHandle handle,
        int sectorPathIndex,
        int goalX,
        int goalY,
        int agentTypeId,
        bool demandExactFinalGoalTile = true,
        int maximumSectorPathIndex = -1)
    {
        if (handle == null)
            throw new InvalidOperationException("EnqueueActiveFlowTileBuilds failed: handle is null.");
        if (handle.SectorIds == null || handle.SectorIds.Length == 0)
            throw new InvalidOperationException("EnqueueActiveFlowTileBuilds failed: handle sector path is invalid.");
        if (sectorPathIndex < 0 || sectorPathIndex >= handle.SectorIds.Length)
            throw new InvalidOperationException($"EnqueueActiveFlowTileBuilds failed: sectorPathIndex out of range {sectorPathIndex}.");

        int finalIndex = maximumSectorPathIndex < sectorPathIndex
            ? Math.Min(handle.SectorIds.Length - 1, sectorPathIndex + 1)
            : Math.Min(handle.SectorIds.Length - 1, maximumSectorPathIndex);
        bool requiresSnapshot = false;
        for (int index = sectorPathIndex; index <= finalIndex; index++)
        {
            if (index == finalIndex && !demandExactFinalGoalTile)
                continue;
            requiresSnapshot |= RequiresNewExactFlowTileBuildJob(handle, index, goalX, goalY, agentTypeId);
        }
        PathHandle snapshot = requiresSnapshot ? ClonePathHandle(handle) : handle;
        if (requiresSnapshot && snapshot.HasCommittedCurrentTileKey)
            ClearCommittedCurrentTileAuthority(snapshot);
        for (int index = finalIndex; index >= sectorPathIndex; index--)
        {
            if (index == finalIndex && !demandExactFinalGoalTile)
                continue;
            EnqueueFlowTileBuildJob(snapshot, index, goalX, goalY, agentTypeId);
        }
    }

    private static void AddActiveFlowTileBuildKeys(
        PathHandle handle,
        int sectorPathIndex,
        int goalX,
        int goalY,
        int agentTypeId,
        bool demandExactFinalGoalTile = true,
        int maximumSectorPathIndex = -1)
    {
        if (handle == null)
            throw new InvalidOperationException("AddActiveFlowTileBuildKeys failed: handle is null.");
        if (handle.SectorIds == null || handle.SectorIds.Length == 0)
            throw new InvalidOperationException("AddActiveFlowTileBuildKeys failed: handle sector path is invalid.");
        if (sectorPathIndex < 0 || sectorPathIndex >= handle.SectorIds.Length)
            throw new InvalidOperationException($"AddActiveFlowTileBuildKeys failed: sectorPathIndex out of range {sectorPathIndex}.");

        int finalIndex = maximumSectorPathIndex < sectorPathIndex
            ? Math.Min(handle.SectorIds.Length - 1, sectorPathIndex + 1)
            : Math.Min(handle.SectorIds.Length - 1, maximumSectorPathIndex);
        for (int index = sectorPathIndex; index <= finalIndex; index++)
        {
            if (index == finalIndex && !demandExactFinalGoalTile)
                continue;
            AddActiveExactFlowTileBuildKey(handle, index, goalX, goalY, agentTypeId);
        }
    }

    private static bool RequiresNewExactFlowTileBuildJob(
        PathHandle handle,
        int sectorPathIndex,
        int goalX,
        int goalY,
        int agentTypeId)
    {
        FlowTileCacheKey key = CreateExactTileCacheKeyForPathSegment(
            handle,
            sectorPathIndex,
            goalX,
            goalY,
            agentTypeId,
            out _,
            out _);
        return !FlowTileCache.ContainsKey(key) && !PendingFlowTileBuildJobs.Contains(key);
    }

    private static void AddActiveExactFlowTileBuildKey(PathHandle handle, int sectorPathIndex, int goalX, int goalY, int agentTypeId)
    {
        ActiveFlowTileBuildKeys.Add(CreateExactTileCacheKeyForPathSegment(
            handle,
            sectorPathIndex,
            goalX,
            goalY,
            agentTypeId,
            out _,
            out _));
    }

    private static void EnqueueFlowTileBuildJob(PathHandle snapshot, int sectorPathIndex, int goalX, int goalY, int agentTypeId)
    {
        FlowTileCacheKey key = CreateTileCacheKeyForPathSegment(snapshot, sectorPathIndex, goalX, goalY, agentTypeId, out _, out _);
        if (FlowTileCache.ContainsKey(key))
            return;

        FlowTileBuildKey buildKey = new FlowTileBuildKey(key, sectorPathIndex, goalX, goalY, agentTypeId);
        if (!PendingFlowTileBuildJobs.Add(key))
            return;

        FlowTileBuildJob job = new FlowTileBuildJob
        {
            BuildKey = buildKey,
            HandleSnapshot = snapshot
        };
        FlowTileBuildQueue.AddLast(job);
        _perf.FlowTileQueueEnqueued++;
    }

    private static void ReleasePendingFlowTileBuildJobPayloads(FlowTileBuildJob job)
    {
        if (job == null)
            throw new InvalidOperationException("ReleasePendingFlowTileBuildJobPayloads failed: job is null.");
        if (job.HasIntegrationDirectionsHandle)
            job.IntegrationDirectionsHandle.Complete();
        DisposeDeterministicFlowTileIntegrationJobPayloads(job);
    }

    private static PathHandle ClonePathHandle(PathHandle handle)
    {
        if (handle == null)
            throw new InvalidOperationException("ClonePathHandle failed: handle is null.");

        return new PathHandle
        {
            HandleId = handle.HandleId,
            WorldVersion = handle.WorldVersion,
            GoalX = handle.GoalX,
            GoalY = handle.GoalY,
            SectorIds = handle.SectorIds,
            PortalIds = handle.PortalIds,
            CurrentSectorIndex = handle.CurrentSectorIndex,
            BuildSource = handle.BuildSource,
            HasCommittedCurrentTileKey = handle.HasCommittedCurrentTileKey,
            CommittedCurrentTileKey = handle.CommittedCurrentTileKey,
            CommittedCorridorGoalX = handle.CommittedCorridorGoalX,
            CommittedCorridorGoalY = handle.CommittedCorridorGoalY,
            CommittedCorridorSectorIds = handle.CommittedCorridorSectorIds != null
                ? (int[])handle.CommittedCorridorSectorIds.Clone()
                : null,
            CommittedCorridorPortalIds = handle.CommittedCorridorPortalIds != null
                ? (int[])handle.CommittedCorridorPortalIds.Clone()
                : null
        };
    }

    private static Vector2Int[] ResolveGoalCells(SectorData sector, FlowTileCacheKey key, int goalX, int goalY)
    {
        if (key.GoalKind == TileGoalKind.FinalGoal)
            return new[] { new Vector2Int(goalX, goalY) };

        return GetPortalCellsForSector(GetPortalById(_world, key.GoalId), sector.SectorId);
    }

    private static bool IsInsideSector(FlowTileCacheEntry tile, int worldX, int worldY)
    {
        return worldX >= tile.StartX
               && worldX < tile.StartX + tile.Width
               && worldY >= tile.StartY
               && worldY < tile.StartY + tile.Height;
    }

    private static void TrimTileCache()
    {
        if (FlowTileCache.Count <= Config.FlowTileCacheLimit)
            return;

        RefreshPendingFlowTileDependencyKeys();
        int threshold = GetFrameCount() - 120;
        List<FlowTileCacheKey> expiredKeys = new List<FlowTileCacheKey>();
        foreach (KeyValuePair<FlowTileCacheKey, FlowTileCacheEntry> pair in FlowTileCache)
        {
            if (pair.Value.ActiveReferenceCount > 0 || PendingFlowTileDependencyKeys.Contains(pair.Key))
                continue;
            if (pair.Value.LastUsedFrame < threshold && pair.Value.LastReferencedFrame < threshold)
                expiredKeys.Add(pair.Key);
        }

        for (int i = 0; i < expiredKeys.Count; i++)
            RemoveFlowTileCacheEntry(expiredKeys[i]);

        if (FlowTileCache.Count <= Config.FlowTileCacheLimit)
            return;

        List<FlowTileCacheKey> keys = new List<FlowTileCacheKey>(FlowTileCache.Keys);
        keys.Sort((a, b) =>
        {
            int retainFrameOrder = ResolveFlowTileRetainFrame(FlowTileCache[a]).CompareTo(ResolveFlowTileRetainFrame(FlowTileCache[b]));
            return retainFrameOrder != 0 ? retainFrameOrder : CompareFlowTileCacheKeys(a, b);
        });
        int removeCount = FlowTileCache.Count - Config.FlowTileCacheLimit;
        for (int i = 0; i < keys.Count && removeCount > 0; i++)
        {
            if (FlowTileCache[keys[i]].ActiveReferenceCount > 0
                || PendingFlowTileDependencyKeys.Contains(keys[i]))
                continue;

            RemoveFlowTileCacheEntry(keys[i]);
            removeCount--;
        }
    }

    private static void RefreshPendingFlowTileDependencyKeys()
    {
        PendingFlowTileDependencyKeys.Clear();
        for (LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First; node != null; node = node.Next)
        {
            FlowTileBuildJob job = node.Value;
            if (job?.HandleSnapshot?.SectorIds == null)
                throw new InvalidOperationException("RefreshPendingFlowTileDependencyKeys failed: pending job or path snapshot is invalid.");
            int downstreamPathIndex = job.BuildKey.SectorPathIndex + 1;
            if (downstreamPathIndex >= job.HandleSnapshot.SectorIds.Length)
                continue;
            PendingFlowTileDependencyKeys.Add(CreateTileCacheKeyForPathSegment(
                job.HandleSnapshot,
                downstreamPathIndex,
                job.BuildKey.GoalX,
                job.BuildKey.GoalY,
                job.BuildKey.AgentTypeId,
                out _,
                out _));
        }
    }

    private static void RefreshFlowTileReferenceCounts()
    {
        _perf.FlowTileReferenceRefreshCalls++;
        foreach (KeyValuePair<FlowTileCacheKey, FlowTileCacheEntry> pair in FlowTileCache)
        {
            if (pair.Key.WorldVersion == _world.Version)
                pair.Value.ActiveReferenceCount = 0;
        }

        int frame = GetFrameCount();
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            if (!IsAgentInActiveFlowBuildQueueWorld(agent))
                continue;

            PathHandle handle = agent.NavState.PathHandle;
            if (handle == null
                || handle.WorldVersion != _world.Version
                || handle.SectorIds == null
                || handle.SectorIds.Length == 0
                || PathReferencesMissingPortal(handle))
            {
                continue;
            }

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
                ResolvePreparedMaximumTravelDistanceFixed(agent));
            for (int i = startIndex; i <= endIndex; i++)
            {
                _perf.FlowTileReferenceKeyScans++;
                FlowTileCacheKey key = CreateSteeringReadDomainTileKey(
                    handle,
                    sectorIds,
                    portalIds,
                    i,
                    goalX,
                    goalY,
                    agent.AgentTypeId,
                    usesCommittedCorridor);
                if (!FlowTileCache.TryGetValue(key, out FlowTileCacheEntry tile))
                    continue;

                tile.ActiveReferenceCount++;
                tile.LastReferencedFrame = frame;
            }
        }
    }

    private static int ResolveFlowTileRetainFrame(FlowTileCacheEntry tile)
    {
        if (tile == null)
            return int.MinValue;

        return Mathf.Max(tile.LastUsedFrame, tile.LastReferencedFrame);
    }

}
