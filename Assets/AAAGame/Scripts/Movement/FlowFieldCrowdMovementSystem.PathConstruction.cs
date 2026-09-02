using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using AAAGame.FlowPath;
using UnityEngine;
using MainThreadFrameProfiler = UnityGameFramework.Runtime.MainThreadFrameProfiler;
using MainThreadPerfScope = UnityGameFramework.Runtime.MainThreadPerfScope;

public static partial class FlowFieldCrowdMovementSystem
{
    private static bool EnsurePathHandle(
        AgentRuntimeData agent,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        bool useSectorCorridorPolicy)
    {
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        PathHandle handle = agent.NavState.PathHandle;
        long stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool canReuseHandle = handle != null
                              && handle.WorldVersion == _world.Version
                              && handle.SectorIds != null
                              && handle.SectorIds.Length > 0
                              && !PathReferencesMissingPortal(handle)
                              && FindSectorIndex(handle, startSectorId, 0) >= 0
                              && handle.SectorIds[handle.SectorIds.Length - 1] == goalSectorId;
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowPathFastValidation,
                Stopwatch.GetTimestamp() - stageStartTicks);
        }
        if (canReuseHandle)
        {
            if (!handle.MatchesGoal(goalX, goalY))
            {
                int oldGoalX = handle.GoalX;
                int oldGoalY = handle.GoalY;
                _perf.PathBuildGoalCellMismatch++;
                handle.GoalX = goalX;
                handle.GoalY = goalY;
                if (handle.HasCommittedCurrentTileKey)
                {
                    handle.CommittedCorridorGoalX = goalX;
                    handle.CommittedCorridorGoalY = goalY;
                }
                if (GameDebugSettings.IsEnabled(DebugCategory.Move)
                    && TryConsumeSuccessfulMoveDiagnosticBudget())
                {
                    GameDebugSettings.Log(DebugCategory.Move,
                        $"[FlowPathGoalCellUpdate] agent={agent.CharacterKey} action=updateFinalGoal startSector={startSectorId} goalSector={goalSectorId} " +
                        $"start=({startX},{startY}) oldGoal=({oldGoalX},{oldGoalY}) newGoal=({goalX},{goalY}) " +
                        $"source={handle.BuildSource ?? "unknown"} decision={BuildPathRepathDecisionDiagnostics(handle, startSectorId, goalSectorId, startX, startY, goalX, goalY)} " +
                        $"handle={FormatPathHandle(handle)}");
                }
            }

            return true;
        }

        int startCellIndex = _world.GetIndex(startX, startY);
        int goalCellIndex = _world.GetIndex(goalX, goalY);
        int startSectorDirtyVersion = _world.Sectors[startSectorId].DirtyVersion;
        int goalSectorDirtyVersion = _world.Sectors[goalSectorId].DirtyVersion;
        if (agent.NavState.HasFailedPathRequest
            && agent.NavState.FailedPathWorldVersion == _world.Version
            && agent.NavState.FailedPathStartCellIndex == startCellIndex
            && agent.NavState.FailedPathGoalCellIndex == goalCellIndex
            && agent.NavState.FailedPathStartSectorDirtyVersion == startSectorDirtyVersion
            && agent.NavState.FailedPathGoalSectorDirtyVersion == goalSectorDirtyVersion)
        {
            return false;
        }

        PathHandle oldHandle = handle;
        string rebuildReason = ResolvePathHandleRebuildReason(oldHandle, startSectorId, goalSectorId, goalX, goalY);
        IncrementPathHandleRebuildReason(rebuildReason);
        PathHandle committedPrefixHandle = null;
        bool committedPrefixBuilt = false;
        if (rebuildReason == "goalSectorMismatch")
        {
            stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            committedPrefixBuilt = TryBuildPathHandleWithCommittedPortalPrefix(
                agent,
                oldHandle,
                startSectorId,
                goalSectorId,
                goalX,
                goalY,
                useSectorCorridorPolicy,
                out committedPrefixHandle);
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowPathBuildCommittedPrefix,
                    Stopwatch.GetTimestamp() - stageStartTicks);
            }
        }
        handle = committedPrefixBuilt
            ? committedPrefixHandle
            : BuildPathHandle(
                startSectorId,
                goalSectorId,
                startX,
                startY,
                goalX,
                goalY,
                useSectorCorridorPolicy,
                agent.NavState.StableGoalTargetId,
                ResolveIslandIdForDiagnostics(_world, startX, startY));
        agent.NavState.PathHandle = handle;
        agent.NavState.HasFailedPathRequest = handle == null;
        agent.NavState.FailedPathWorldVersion = _world.Version;
        agent.NavState.FailedPathStartCellIndex = startCellIndex;
        agent.NavState.FailedPathGoalCellIndex = goalCellIndex;
        agent.NavState.FailedPathStartSectorDirtyVersion = startSectorDirtyVersion;
        agent.NavState.FailedPathGoalSectorDirtyVersion = goalSectorDirtyVersion;
        if (GameDebugSettings.IsEnabled(DebugCategory.Move)
            && TryConsumeSuccessfulMoveDiagnosticBudget())
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowPathRebuild] agent={agent.CharacterKey} reason={rebuildReason} startSector={startSectorId} goalSector={goalSectorId} " +
                $"start=({startX},{startY}) goal=({goalX},{goalY}) oldHandle={FormatPathHandle(oldHandle)} result={(handle != null ? "ok" : "null")} newHandle={FormatPathHandle(handle)}");
        }
        return handle != null;
    }

    private static bool TryBuildPathHandleWithCommittedPortalPrefix(
        AgentRuntimeData agent,
        PathHandle previousHandle,
        int startSectorId,
        int goalSectorId,
        int goalX,
        int goalY,
        bool useSectorCorridorPolicy,
        out PathHandle handle)
    {
        handle = null;
        AgentNavState nav = agent?.NavState;
        if (nav == null
            || nav.StableGoalTargetId == int.MinValue
            || previousHandle == null
            || previousHandle.WorldVersion != _world.Version
            || goalSectorId == startSectorId
            || !nav.PortalTraversalHasCommittedTileSlot
            || nav.PortalTraversalWorldVersion != _world.Version
            || nav.PortalTraversalSectorId != startSectorId)
        {
            return false;
        }

        int previousSectorIndex = previousHandle.CurrentSectorIndex;
        if (previousHandle.SectorIds == null
            || previousSectorIndex < 0
            || previousSectorIndex >= previousHandle.SectorIds.Length
            || previousHandle.SectorIds[previousSectorIndex] != startSectorId)
        {
            return false;
        }

        int committedPortalId = ResolveCurrentDownstreamPortalId(previousHandle);
        if (committedPortalId < 0 || committedPortalId != nav.PortalTraversalId)
            return false;

        FlowTileCacheKey committedTileKey = previousHandle.HasCommittedCurrentTileKey
            ? previousHandle.CommittedCurrentTileKey
            : CreateTileCacheKeyForPathSegment(
                previousHandle,
                previousSectorIndex,
                previousHandle.GoalX,
                previousHandle.GoalY,
                agent.AgentTypeId,
                out _,
                out _);
        if (committedTileKey.WorldVersion != _world.Version
            || committedTileKey.SectorId != startSectorId
            || committedTileKey.GoalKind != TileGoalKind.Portal
            || committedTileKey.GoalId != committedPortalId
            || committedTileKey.AgentTypeId != agent.AgentTypeId
            || committedTileKey.DirtyVersion != _world.Sectors[startSectorId].DirtyVersion)
        {
            throw new InvalidOperationException(
                $"TryBuildPathHandleWithCommittedPortalPrefix failed: committed tile key is inconsistent key={FormatTileKey(committedTileKey)}, portal={committedPortalId}, sector={startSectorId}.");
        }
        if (!DeterministicFlowTileCache.ContainsKey(committedTileKey))
        {
            throw new InvalidOperationException(
                $"TryBuildPathHandleWithCommittedPortalPrefix failed: committed tile is absent key={FormatTileKey(committedTileKey)}.");
        }
        if (!previousHandle.HasCommittedCurrentTileKey)
        {
            CommitCurrentFunnelCorridor(previousHandle, committedTileKey);
            previousHandle.HasCommittedCurrentTileKey = true;
            previousHandle.CommittedCurrentTileKey = committedTileKey;
        }

        PortalData committedPortal = GetPortalById(_world, committedPortalId);
        int downstreamSectorId = GetOppositeSectorId(committedPortal, startSectorId);
        Vector2Int[] downstreamCells = GetPortalCellsForSector(committedPortal, downstreamSectorId);
        int slotIndex = nav.PortalTraversalSlotIndex;
        if (slotIndex < 0 || slotIndex >= downstreamCells.Length)
        {
            throw new InvalidOperationException(
                $"TryBuildPathHandleWithCommittedPortalPrefix failed: committed slot is invalid portal={committedPortalId}, slot={slotIndex}, count={downstreamCells.Length}.");
        }

        Vector2Int downstreamStart = downstreamCells[slotIndex];
        PathHandle suffix = BuildPathHandle(
            downstreamSectorId,
            goalSectorId,
            downstreamStart.x,
            downstreamStart.y,
            goalX,
            goalY,
            useSectorCorridorPolicy,
            agent.NavState.StableGoalTargetId,
            ResolveIslandIdForDiagnostics(_world, downstreamStart.x, downstreamStart.y));
        if (suffix == null)
            return false;
        if (suffix.SectorIds == null
            || suffix.SectorIds.Length == 0
            || suffix.SectorIds[0] != downstreamSectorId
            || suffix.PortalIds == null
            || suffix.PortalIds.Length != suffix.SectorIds.Length - 1)
        {
            throw new InvalidOperationException(
                $"TryBuildPathHandleWithCommittedPortalPrefix failed: suffix is invalid portal={committedPortalId}, suffix={FormatPathHandle(suffix)}.");
        }
        if (suffix.PortalIds.Length > 0 && suffix.PortalIds[0] == committedPortalId)
            return false;

        int[] sectorIds = new int[suffix.SectorIds.Length + 1];
        sectorIds[0] = startSectorId;
        suffix.SectorIds.CopyTo(0, sectorIds, 1, suffix.SectorIds.Length);
        int[] portalIds = new int[suffix.PortalIds.Length + 1];
        portalIds[0] = committedPortalId;
        suffix.PortalIds.CopyTo(0, portalIds, 1, suffix.PortalIds.Length);
        handle = new PathHandle
        {
            HandleId = _nextPathHandleId++,
            WorldVersion = _world.Version,
            GoalX = goalX,
            GoalY = goalY,
            SectorIds = sectorIds,
            PortalIds = portalIds,
            CurrentSectorIndex = 0,
            BuildSource = "committedPortalPrefix",
            HasCommittedCurrentTileKey = true,
            CommittedCurrentTileKey = committedTileKey,
            CommittedCorridorGoalX = previousHandle.CommittedCorridorGoalX,
            CommittedCorridorGoalY = previousHandle.CommittedCorridorGoalY,
            CommittedCorridorSectorIds = previousHandle.CommittedCorridorSectorIds != null
                ? (int[])previousHandle.CommittedCorridorSectorIds.Clone()
                : null,
            CommittedCorridorPortalIds = previousHandle.CommittedCorridorPortalIds != null
                ? (int[])previousHandle.CommittedCorridorPortalIds.Clone()
                : null
        };
        return true;
    }

    private static string ResolvePathHandleRebuildReason(PathHandle handle, int startSectorId, int goalSectorId, int goalX, int goalY)
    {
        if (handle == null)
            return "noHandle";
        if (handle.WorldVersion != _world.Version)
            return "worldMismatch";
        if (handle.SectorIds == null || handle.SectorIds.Length == 0)
            return "invalid";
        if (PathReferencesMissingPortal(handle))
            return "invalid";
        if (FindSectorIndex(handle, startSectorId, 0) < 0)
            return "startSectorMismatch";
        if (handle.SectorIds[handle.SectorIds.Length - 1] != goalSectorId)
            return "goalSectorMismatch";
        if (!handle.MatchesGoal(goalX, goalY))
            return "goalCellMismatch";
        return "invalid";
    }

    private static bool ShouldRebuildPathHandleForCurrentStart(
        PathHandle handle,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out string reason)
    {
        reason = string.Empty;
        if (handle == null
            || handle.PortalIds == null
            || handle.PortalIds.Length == 0
            || handle.SectorIds == null
            || handle.SectorIds.Length == 0)
        {
            return false;
        }

        int sectorIndex = FindSectorIndex(handle, startSectorId, Mathf.Max(0, handle.CurrentSectorIndex));
        if (sectorIndex < 0 || sectorIndex >= handle.PortalIds.Length)
            return false;

        int selectedPortalId = handle.PortalIds[sectorIndex];
        if (!TryResolveBestStartPortalForCurrentCell(
                startSectorId,
                goalSectorId,
                startX,
                startY,
                goalX,
                goalY,
                selectedPortalId,
                out int bestPortalId,
                out long selectedCost,
                out long bestCost))
        {
            return false;
        }

        if (selectedPortalId == bestPortalId || selectedCost <= bestCost)
            return false;

        reason =
            $"startPortalMismatch selected={selectedPortalId}:{FormatDiagnosticCost(DequantizeDeterministicPortalCost(selectedCost))} best={bestPortalId}:{FormatDiagnosticCost(DequantizeDeterministicPortalCost(bestCost))} sectorIndex={sectorIndex}";
        return true;
    }

    private static bool TryResolveBestStartPortalForCurrentCell(
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int selectedPortalId,
        out int bestPortalId,
        out long selectedCost,
        out long bestCost)
    {
        bestPortalId = -1;
        selectedCost = long.MaxValue;
        bestCost = long.MaxValue;
        if (_world == null || startSectorId < 0 || startSectorId >= _world.Sectors.Length)
            return false;

        long checkStartTicks = Stopwatch.GetTimestamp();
        int sharedAgentTypeId = ResolvePreferredAgentTypeId(_world.AgentTypeId);
        SectorData startSector = _world.Sectors[startSectorId];
        SectorData goalSector = _world.Sectors[goalSectorId];
        StartPortalChoiceKey cacheKey = new StartPortalChoiceKey(
            _world.Version,
            startSectorId,
            _world.GetIndex(startX, startY),
            goalSectorId,
            _world.GetIndex(goalX, goalY),
            sharedAgentTypeId,
            selectedPortalId,
            startSector.DirtyVersion,
            goalSector.DirtyVersion);
        if (StartPortalChoiceCache.TryGetValue(cacheKey, out StartPortalChoiceEntry cachedChoice))
        {
            cachedChoice.LastUsedFrame = GetFrameCount();
            bestPortalId = cachedChoice.BestPortalId;
            selectedCost = cachedChoice.SelectedCost;
            bestCost = cachedChoice.BestCost;
            _perf.PathStartPortalCacheHits++;
            _perf.PathStartPortalCheckTicks += Stopwatch.GetTimestamp() - checkStartTicks;
            return bestPortalId >= 0;
        }

        _perf.PathStartPortalCacheMisses++;
        if (!TryGetCachedSharedGoalField(goalSectorId, goalX, goalY, sharedAgentTypeId, out SharedGoalField sharedField))
        {
            _perf.PathStartPortalCheckTicks += Stopwatch.GetTimestamp() - checkStartTicks;
            return false;
        }

        long selectedCostRaw = long.MaxValue;
        long bestCostRaw = long.MaxValue;
        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            _perf.PathStartPortalCandidates++;
            int portalId = startSector.PortalIds[i];
            int startNode = EncodePortalNode(startSectorId, portalId);
            if (!sharedField.SettledPortalNodes.Contains(startNode)
                || !sharedField.NodeCosts.TryGetValue(startNode, out long downstreamCost))
                continue;

            long accessCost = ResolveDeterministicPortalAccessCost(startSector, startSectorId, portalId, startX, startY);
            if (accessCost == long.MaxValue)
                continue;

            if (!TryResolveFirstCrossingPortalFromSharedGoalField(sharedField, startNode, startSectorId, goalSectorId, out int firstCrossingPortalId))
                continue;

            long totalCost = AddDeterministicPortalCosts(
                accessCost,
                downstreamCost);
            if (firstCrossingPortalId == selectedPortalId && totalCost < selectedCostRaw)
                selectedCostRaw = totalCost;
            if (totalCost >= bestCostRaw)
                continue;

            bestCostRaw = totalCost;
            bestPortalId = firstCrossingPortalId;
        }

        selectedCost = selectedCostRaw;
        bestCost = bestCostRaw;

        _perf.PathStartPortalChecks++;
        StartPortalChoiceCache[cacheKey] = new StartPortalChoiceEntry
        {
            BestPortalId = bestPortalId,
            SelectedCost = selectedCost,
            BestCost = bestCost,
            LastUsedFrame = GetFrameCount()
        };
        TrimStartPortalChoiceCache();
        _perf.PathStartPortalCheckTicks += Stopwatch.GetTimestamp() - checkStartTicks;
        return bestPortalId >= 0;
    }

    private static void TrimStartPortalChoiceCache()
    {
        int limit = Mathf.Max(128, Config.FlowTileCacheLimit * 8);
        if (StartPortalChoiceCache.Count <= limit)
            return;

        StartPortalChoiceKey oldestKey = default;
        int oldestFrame = int.MaxValue;
        bool found = false;
        foreach (KeyValuePair<StartPortalChoiceKey, StartPortalChoiceEntry> pair in StartPortalChoiceCache)
        {
            int frame = pair.Value?.LastUsedFrame ?? int.MinValue;
            if (found)
            {
                int frameOrder = frame.CompareTo(oldestFrame);
                if (frameOrder > 0 || (frameOrder == 0 && CompareStartPortalChoiceKeys(pair.Key, oldestKey) >= 0))
                    continue;
            }

            oldestKey = pair.Key;
            oldestFrame = frame;
            found = true;
        }

        if (found)
            StartPortalChoiceCache.Remove(oldestKey);
    }

    private static int CompareStartPortalChoiceKeys(StartPortalChoiceKey left, StartPortalChoiceKey right)
    {
        int order = left.WorldVersion.CompareTo(right.WorldVersion);
        if (order != 0) return order;
        order = left.StartSectorId.CompareTo(right.StartSectorId);
        if (order != 0) return order;
        order = left.StartCellIndex.CompareTo(right.StartCellIndex);
        if (order != 0) return order;
        order = left.GoalSectorId.CompareTo(right.GoalSectorId);
        if (order != 0) return order;
        order = left.GoalCellIndex.CompareTo(right.GoalCellIndex);
        if (order != 0) return order;
        order = left.AgentTypeId.CompareTo(right.AgentTypeId);
        if (order != 0) return order;
        order = left.SelectedPortalId.CompareTo(right.SelectedPortalId);
        if (order != 0) return order;
        order = left.StartSectorDirtyVersion.CompareTo(right.StartSectorDirtyVersion);
        return order != 0 ? order : left.GoalSectorDirtyVersion.CompareTo(right.GoalSectorDirtyVersion);
    }

    private static bool TryResolveFirstCrossingPortalFromSharedGoalField(
        SharedGoalField sharedField,
        int startNode,
        int startSectorId,
        int goalSectorId,
        out int firstCrossingPortalId)
    {
        firstCrossingPortalId = -1;
        if (sharedField == null)
            return false;

        if (sharedField.FirstCrossingPortalByStartNode.TryGetValue(startNode, out FirstCrossingPortalCacheEntry cached))
        {
            _perf.PathFirstCrossingCacheHits++;
            firstCrossingPortalId = cached.PortalId;
            return cached.IsValid;
        }

        _perf.PathFirstCrossingCacheMisses++;
        long resolveStartTicks = Stopwatch.GetTimestamp();
        int cursor = startNode;
        int guard = 0;
        while (sharedField.NextNodeTowardGoal.TryGetValue(cursor, out int nextNode))
        {
            DecodePortalNode(cursor, out int fromSectorId, out int fromPortalId);
            DecodePortalNode(nextNode, out int toSectorId, out int toPortalId);
            if (fromPortalId == toPortalId && fromSectorId != toSectorId && firstCrossingPortalId < 0)
                firstCrossingPortalId = fromPortalId;

            cursor = nextNode;
            guard++;
            if (guard > _world.Sectors.Length + _world.PortalsById.Count)
                throw new InvalidOperationException("TryResolveFirstCrossingPortalFromSharedGoalField failed: reconstruction exceeded guard.");
        }

        int endSectorId = DecodePortalSector(cursor);
        bool isValid = firstCrossingPortalId >= 0 && endSectorId == goalSectorId;
        sharedField.FirstCrossingPortalByStartNode[startNode] = new FirstCrossingPortalCacheEntry(isValid, firstCrossingPortalId);
        _perf.PathFirstCrossingTicks += Stopwatch.GetTimestamp() - resolveStartTicks;
        return isValid;
    }

    private static string BuildPathHandleFailure(IEntityContext self, Vector3 stableGoalPosition, int startSectorId, int goalSectorId, int startX, int startY, int goalX, int goalY)
    {
        if (_world != null
            && _world.IslandIds != null
            && _world.IslandIds.Length == _world.Width * _world.Height
            && !AreCellsOnSameIsland(_world, startX, startY, goalX, goalY))
        {
            return BuildIslandMismatchFailure(self, stableGoalPosition, startX, startY, goalX, goalY);
        }

        SectorData startSector = _world.Sectors[startSectorId];
        SectorData goalSector = _world.Sectors[goalSectorId];
        int accessibleStartPortals = 0;
        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            if (!float.IsPositiveInfinity(ResolvePortalAccessCost(startSector, startSectorId, portalId, startX, startY)))
                accessibleStartPortals++;
        }

        int accessibleGoalPortals = 0;
        for (int i = 0; i < goalSector.PortalIds.Count; i++)
        {
            int portalId = goalSector.PortalIds[i];
            if (!float.IsPositiveInfinity(ResolveGoalSectorPortalAccessCost(goalSector, goalSectorId, portalId, goalX, goalY)))
                accessibleGoalPortals++;
        }

        return $"path handle build failed selfPos={self.Position} stableGoal={stableGoalPosition} " +
               $"startCell=({startX},{startY}) startSector={startSectorId} startIsland={ResolveIslandIdForDiagnostics(_world, startX, startY)} " +
               $"startPortals={startSector.PortalIds.Count} accessibleStartPortals={accessibleStartPortals} " +
               $"goalCell=({goalX},{goalY}) goalSector={goalSectorId} goalIsland={ResolveIslandIdForDiagnostics(_world, goalX, goalY)} " +
               $"goalPortals={goalSector.PortalIds.Count} accessibleGoalPortals={accessibleGoalPortals} " +
               $"worldVersion={_world.Version} pendingRuntimeDirty={HasPendingRuntimeDirty(_activeWorldState)} " +
               $"boxObstacles={BoxObstacles.Count} circleObstacles={CircleObstacles.Count} " +
               $"gridPathDiag={BuildGridPathDiagnostics(self.Position, stableGoalPosition, _world.AgentTypeId)}";
    }

    private static int DecodePortalSector(int node)
    {
        DecodePortalNode(node, out int sectorId, out _);
        return sectorId;
    }

    private static bool TryGetCachedSharedGoalField(int goalSectorId, int goalX, int goalY, int agentTypeId, out SharedGoalField field)
    {
        SharedGoalFieldKey key = CreateSharedGoalFieldKey(goalSectorId, goalX, goalY, agentTypeId);
        if (SharedGoalFields.TryGetValue(key, out field)
            && !SharedGoalFieldReferencesMissingPortal(_world, field))
        {
            field.LastUsedFrame = GetFrameCount();
            return true;
        }

        field = null;
        return false;
    }

    private static SharedGoalFieldKey CreateSharedGoalFieldKey(int goalSectorId, int goalX, int goalY, int agentTypeId)
    {
        SectorData goalSector = _world.Sectors[goalSectorId];
        return new SharedGoalFieldKey(
            _world.Version,
            agentTypeId,
            goalSectorId,
            _world.GetIndex(goalX, goalY),
            goalSector.DirtyVersion);
    }

    private static void EnqueueSharedGoalFieldBuild(int goalSectorId, int goalX, int goalY, int agentTypeId)
    {
        SharedGoalFieldKey key = CreateSharedGoalFieldKey(goalSectorId, goalX, goalY, agentTypeId);
        EnqueueSharedGoalFieldBuild(key, prependToFront: false);
    }

    private static void EnqueueSharedGoalFieldBuild(int goalSectorId, int goalX, int goalY, int agentTypeId, int demandStartSectorId)
    {
        SharedGoalFieldKey key = CreateSharedGoalFieldKey(goalSectorId, goalX, goalY, agentTypeId);
        EnqueueSharedGoalFieldBuild(key, false, demandStartSectorId);
    }

    private static void EnqueueSharedGoalFieldBuild(int goalSectorId, int goalX, int goalY, int agentTypeId, int demandStartSectorId, int demandStartX, int demandStartY)
    {
        SharedGoalFieldKey key = CreateSharedGoalFieldKey(goalSectorId, goalX, goalY, agentTypeId);
        EnqueueSharedGoalFieldBuild(key, false, demandStartSectorId, demandStartX, demandStartY);
    }

    private static void EnqueueSharedGoalFieldBuild(SharedGoalFieldKey key, bool prependToFront)
    {
        EnqueueSharedGoalFieldBuild(key, prependToFront, demandStartSectors: null);
    }

    private static void EnqueueSharedGoalFieldBuild(SharedGoalFieldKey key, bool prependToFront, int demandStartSectorId)
    {
        if (TryHandleCachedSharedGoalFieldDemand(key, prependToFront, demandStartSectorId, -1, -1))
            return;
        if (!PendingSharedGoalFieldBuildJobs.Add(key))
        {
            AddSharedGoalFieldBuildDemand(key, demandStartSectorId);
            if (prependToFront)
                PromotePendingSharedGoalFieldBuildJobToFront(key);
            return;
        }

        _perf.PathPendingSharedGoal++;
        SharedGoalFieldBuildJob job = new SharedGoalFieldBuildJob
        {
            Key = key,
            GoalSectorId = key.GoalSectorId,
            GoalX = key.GoalCellIndex % _world.Width,
            GoalY = key.GoalCellIndex / _world.Width,
            AgentTypeId = key.AgentTypeId
        };
        AddSharedGoalFieldBuildDemand(job, demandStartSectorId);

        if (prependToFront)
            SharedGoalFieldBuildQueue.AddFirst(job);
        else
            SharedGoalFieldBuildQueue.AddLast(job);
    }

    private static void EnqueueSharedGoalFieldBuild(SharedGoalFieldKey key, bool prependToFront, int demandStartSectorId, int demandStartX, int demandStartY)
    {
        if (TryHandleCachedSharedGoalFieldDemand(key, prependToFront, demandStartSectorId, demandStartX, demandStartY))
            return;
        if (!PendingSharedGoalFieldBuildJobs.Add(key))
        {
            AddSharedGoalFieldBuildDemand(key, demandStartSectorId, demandStartX, demandStartY);
            if (prependToFront)
                PromotePendingSharedGoalFieldBuildJobToFront(key);
            return;
        }

        _perf.PathPendingSharedGoal++;
        SharedGoalFieldBuildJob job = new SharedGoalFieldBuildJob
        {
            Key = key,
            GoalSectorId = key.GoalSectorId,
            GoalX = key.GoalCellIndex % _world.Width,
            GoalY = key.GoalCellIndex / _world.Width,
            AgentTypeId = key.AgentTypeId
        };
        AddSharedGoalFieldBuildDemand(job, demandStartSectorId, demandStartX, demandStartY);

        if (prependToFront)
            SharedGoalFieldBuildQueue.AddFirst(job);
        else
            SharedGoalFieldBuildQueue.AddLast(job);
    }

    private static void EnqueueSharedGoalFieldBuild(SharedGoalFieldKey key, bool prependToFront, Dictionary<int, int> demandStartCells)
    {
        if (TryHandleCachedSharedGoalFieldDemand(key, prependToFront, demandStartCells))
            return;
        if (!PendingSharedGoalFieldBuildJobs.Add(key))
        {
            AddSharedGoalFieldBuildDemand(key, demandStartCells);
            if (prependToFront)
                PromotePendingSharedGoalFieldBuildJobToFront(key);
            return;
        }

        _perf.PathPendingSharedGoal++;
        SharedGoalFieldBuildJob job = new SharedGoalFieldBuildJob
        {
            Key = key,
            GoalSectorId = key.GoalSectorId,
            GoalX = key.GoalCellIndex % _world.Width,
            GoalY = key.GoalCellIndex / _world.Width,
            AgentTypeId = key.AgentTypeId
        };
        AddSharedGoalFieldBuildDemand(job, demandStartCells);

        if (prependToFront)
            SharedGoalFieldBuildQueue.AddFirst(job);
        else
            SharedGoalFieldBuildQueue.AddLast(job);
    }

    private static void EnqueueSharedGoalFieldBuild(SharedGoalFieldKey key, bool prependToFront, HashSet<int> demandStartSectors)
    {
        if (TryHandleCachedSharedGoalFieldDemand(key, prependToFront, demandStartSectors))
            return;
        if (!PendingSharedGoalFieldBuildJobs.Add(key))
        {
            AddSharedGoalFieldBuildDemand(key, demandStartSectors);
            if (prependToFront)
                PromotePendingSharedGoalFieldBuildJobToFront(key);
            return;
        }

        _perf.PathPendingSharedGoal++;
        SharedGoalFieldBuildJob job = new SharedGoalFieldBuildJob
        {
            Key = key,
            GoalSectorId = key.GoalSectorId,
            GoalX = key.GoalCellIndex % _world.Width,
            GoalY = key.GoalCellIndex / _world.Width,
            AgentTypeId = key.AgentTypeId
        };
        AddSharedGoalFieldBuildDemand(job, demandStartSectors);

        if (prependToFront)
            SharedGoalFieldBuildQueue.AddFirst(job);
        else
            SharedGoalFieldBuildQueue.AddLast(job);
    }

    private static bool TryHandleCachedSharedGoalFieldDemand(
        SharedGoalFieldKey key,
        bool prependToFront,
        int demandStartSectorId,
        int demandStartX,
        int demandStartY)
    {
        if (!SharedGoalFields.TryGetValue(key, out SharedGoalField field))
            return false;
        if (field == null)
            throw new InvalidOperationException($"Shared-goal cache contains a null field. key={key}.");

        int demandStartCellIndex = demandStartX >= 0
                                   && demandStartX < _world.Width
                                   && demandStartY >= 0
                                   && demandStartY < _world.Height
            ? _world.GetIndex(demandStartX, demandStartY)
            : -1;
        if (IsSharedGoalFieldDemandCovered(field, demandStartSectorId, demandStartCellIndex))
            return true;

        SharedGoalFieldBuildJob job = BeginCachedSharedGoalFieldExpansion(key, field);
        AddSharedGoalFieldBuildDemand(job, demandStartSectorId, demandStartX, demandStartY);
        EnqueueSharedGoalFieldExpansionJob(job, prependToFront);
        return true;
    }

    private static bool TryHandleCachedSharedGoalFieldDemand(
        SharedGoalFieldKey key,
        bool prependToFront,
        Dictionary<int, int> demandStartCells)
    {
        if (!SharedGoalFields.TryGetValue(key, out SharedGoalField field))
            return false;
        if (field == null)
            throw new InvalidOperationException($"Shared-goal cache contains a null field. key={key}.");
        if (demandStartCells == null || demandStartCells.Count == 0 || AreSharedGoalFieldDemandsCovered(field, demandStartCells))
            return true;

        SharedGoalFieldBuildJob job = BeginCachedSharedGoalFieldExpansion(key, field);
        AddSharedGoalFieldBuildDemand(job, demandStartCells);
        EnqueueSharedGoalFieldExpansionJob(job, prependToFront);
        return true;
    }

    private static bool TryHandleCachedSharedGoalFieldDemand(
        SharedGoalFieldKey key,
        bool prependToFront,
        HashSet<int> demandStartSectors)
    {
        if (!SharedGoalFields.TryGetValue(key, out SharedGoalField field))
            return false;
        if (field == null)
            throw new InvalidOperationException($"Shared-goal cache contains a null field. key={key}.");
        if (field.PortalOpenSet == null)
            throw new InvalidOperationException($"Shared-goal field is missing its continuation frontier. key={field.Key}.");
        if (field.PortalOpenSet.Count == 0
            || demandStartSectors != null && demandStartSectors.Count > 0 && AreSharedGoalFieldDemandsCovered(field, demandStartSectors))
            return true;

        SharedGoalFieldBuildJob job = BeginCachedSharedGoalFieldExpansion(key, field);
        AddSharedGoalFieldBuildDemand(job, demandStartSectors);
        EnqueueSharedGoalFieldExpansionJob(job, prependToFront);
        return true;
    }

    private static bool IsSharedGoalFieldDemandCovered(SharedGoalField field, int startSectorId, int startCellIndex)
    {
        return IsSharedGoalFieldDemandCovered(_world, field, startSectorId, startCellIndex);
    }

    private static bool IsSharedGoalFieldDemandCovered(
        NavigationWorld world,
        SharedGoalField field,
        int startSectorId,
        int startCellIndex)
    {
        if (world == null)
            throw new InvalidOperationException("Shared-goal demand coverage requires a navigation world.");
        if (field.PortalOpenSet == null)
            throw new InvalidOperationException($"Shared-goal field is missing its continuation frontier. key={field.Key}.");
        return startCellIndex >= 0
            ? IsSharedGoalFieldDemandStartCellCovered(world, field, startSectorId, startCellIndex)
            : IsSharedGoalFieldDemandStartSectorCovered(world, field, startSectorId);
    }

    private static bool IsSharedGoalFieldDemandStartSectorCovered(SharedGoalField field, int startSectorId)
    {
        return IsSharedGoalFieldDemandStartSectorCovered(_world, field, startSectorId);
    }

    private static bool IsSharedGoalFieldDemandStartSectorCovered(
        NavigationWorld world,
        SharedGoalField field,
        int startSectorId)
    {
        if (world == null)
            throw new InvalidOperationException("Shared-goal sector demand coverage requires a navigation world.");
        if (startSectorId < 0 || startSectorId >= world.Sectors.Length || field.SettledPortalNodes == null)
            return false;

        SectorData sector = world.Sectors[startSectorId];
        if (sector == null || sector.PortalIds == null || sector.PortalIds.Count == 0)
            return false;
        for (int i = 0; i < sector.PortalIds.Count; i++)
        {
            if (!field.SettledPortalNodes.Contains(EncodePortalNode(startSectorId, sector.PortalIds[i])))
                return false;
        }

        return true;
    }

    private static bool IsSharedGoalFieldDemandStartCellCovered(
        SharedGoalField field,
        int startSectorId,
        int startCellIndex)
    {
        return IsSharedGoalFieldDemandStartCellCovered(
            _world,
            field,
            startSectorId,
            startCellIndex);
    }

    private static bool IsSharedGoalFieldDemandStartCellCovered(
        NavigationWorld world,
        SharedGoalField field,
        int startSectorId,
        int startCellIndex)
    {
        if (world == null)
            throw new InvalidOperationException("Shared-goal cell demand coverage requires a navigation world.");
        if (startSectorId < 0
            || startSectorId >= world.Sectors.Length
            || startCellIndex < 0
            || startCellIndex >= world.Width * world.Height
            || field.SettledPortalNodes == null)
        {
            return false;
        }

        SectorData sector = world.Sectors[startSectorId];
        if (sector == null || sector.PortalIds == null || sector.PortalIds.Count == 0)
            return false;

        int startX = startCellIndex % world.Width;
        int startY = startCellIndex / world.Width;
        long bestSettledTotal = long.MaxValue;
        for (int i = 0; i < sector.PortalIds.Count; i++)
        {
            int portalId = sector.PortalIds[i];
            int node = EncodePortalNode(startSectorId, portalId);
            if (!field.SettledPortalNodes.Contains(node)
                || !field.NodeCosts.TryGetValue(node, out long nodeCost))
            {
                continue;
            }

            long accessCost = ResolveDeterministicPortalAccessCost(
                world,
                sector,
                startSectorId,
                portalId,
                startX,
                startY);
            if (accessCost == long.MaxValue)
                continue;

            bestSettledTotal = Math.Min(
                bestSettledTotal,
                AddDeterministicPortalCosts(accessCost, nodeCost));
        }

        return bestSettledTotal != long.MaxValue
               && bestSettledTotal <= field.PortalOpenSet.PeekCost;
    }

    private static bool AreSharedGoalFieldDemandsCovered(SharedGoalField field, Dictionary<int, int> demandStartCells)
    {
        foreach (KeyValuePair<int, int> demand in demandStartCells)
        {
            if (!IsSharedGoalFieldDemandCovered(field, demand.Value, demand.Key))
                return false;
        }

        return true;
    }

    private static bool AreSharedGoalFieldDemandsCovered(SharedGoalField field, HashSet<int> demandStartSectors)
    {
        foreach (int demandStartSectorId in demandStartSectors)
        {
            if (!IsSharedGoalFieldDemandCovered(field, demandStartSectorId, -1))
                return false;
        }

        return true;
    }

    private static SharedGoalFieldBuildJob BeginCachedSharedGoalFieldExpansion(SharedGoalFieldKey key, SharedGoalField field)
    {
        if (field.PortalOpenSet == null || field.SettledPortalNodes == null)
            throw new InvalidOperationException($"Shared-goal field cannot resume without frontier and settled state. key={key}.");
        if (field.PortalOpenSet.Count == 0)
            throw new InvalidOperationException($"Shared-goal field requested expansion after its frontier was exhausted. key={key}.");

        RemoveSharedGoalFieldCacheEntry(key);
        var job = new SharedGoalFieldBuildJob
        {
            Key = key,
            GoalSectorId = field.GoalSectorId,
            GoalX = field.GoalX,
            GoalY = field.GoalY,
            AgentTypeId = key.AgentTypeId,
            Stage = SharedGoalFieldBuildStage.PortalGraph,
            GoalSector = _world.Sectors[field.GoalSectorId],
            Field = field,
            PortalOpenSet = field.PortalOpenSet,
            SettledPortalNodes = field.SettledPortalNodes,
            SettledPortalAuthorityContentHash = field.SettledPortalAuthorityContentHash
        };

        AddSharedGoalFieldBuildDemand(job, field.CompletedDemandStartSectorIds);
        foreach (int cellIndex in field.CompletedDemandStartCellIndices)
        {
            int startX = cellIndex % _world.Width;
            int startY = cellIndex / _world.Width;
            if (!_world.TryGetSectorId(startX, startY, out int startSectorId))
                throw new InvalidOperationException($"Cached shared-goal demand cell is outside the active world. key={key} cell={cellIndex}.");
            AddSharedGoalFieldBuildDemand(job, startSectorId, startX, startY);
        }

        return job;
    }

    private static void EnqueueSharedGoalFieldExpansionJob(SharedGoalFieldBuildJob job, bool prependToFront)
    {
        if (!PendingSharedGoalFieldBuildJobs.Add(job.Key))
            throw new InvalidOperationException($"Shared-goal expansion attempted to enqueue a duplicate key. key={job.Key}.");
        _perf.PathPendingSharedGoal++;
        if (prependToFront)
            SharedGoalFieldBuildQueue.AddFirst(job);
        else
            SharedGoalFieldBuildQueue.AddLast(job);
    }

    private static void AddSharedGoalFieldBuildDemand(SharedGoalFieldKey key, HashSet<int> demandStartSectors)
    {
        if (demandStartSectors == null || demandStartSectors.Count == 0)
            return;

        for (LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First; node != null; node = node.Next)
        {
            SharedGoalFieldBuildJob job = node.Value;
            if (job == null || !job.Key.Equals(key))
                continue;

            AddSharedGoalFieldBuildDemand(job, demandStartSectors);
            return;
        }
    }

    private static void AddSharedGoalFieldBuildDemand(SharedGoalFieldKey key, int demandStartSectorId)
    {
        if (demandStartSectorId < 0)
            return;

        for (LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First; node != null; node = node.Next)
        {
            SharedGoalFieldBuildJob job = node.Value;
            if (job == null || !job.Key.Equals(key))
                continue;

            AddSharedGoalFieldBuildDemand(job, demandStartSectorId);
            return;
        }
    }

    private static void AddSharedGoalFieldBuildDemand(SharedGoalFieldKey key, int demandStartSectorId, int demandStartX, int demandStartY)
    {
        if (demandStartSectorId < 0)
            return;

        for (LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First; node != null; node = node.Next)
        {
            SharedGoalFieldBuildJob job = node.Value;
            if (job == null || !job.Key.Equals(key))
                continue;

            AddSharedGoalFieldBuildDemand(job, demandStartSectorId, demandStartX, demandStartY);
            return;
        }
    }

    private static void AddSharedGoalFieldBuildDemand(SharedGoalFieldKey key, Dictionary<int, int> demandStartCells)
    {
        if (demandStartCells == null || demandStartCells.Count == 0)
            return;

        for (LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First; node != null; node = node.Next)
        {
            SharedGoalFieldBuildJob job = node.Value;
            if (job == null || !job.Key.Equals(key))
                continue;

            AddSharedGoalFieldBuildDemand(job, demandStartCells);
            return;
        }
    }

    private static void AddSharedGoalFieldBuildDemand(SharedGoalFieldBuildJob job, HashSet<int> demandStartSectors)
    {
        if (job == null)
            throw new InvalidOperationException("AddSharedGoalFieldBuildDemand failed: job is null.");
        if (demandStartSectors == null || demandStartSectors.Count == 0)
            return;

        job.DemandStartSectorIds ??= new HashSet<int>();
        bool changed = false;
        foreach (int sectorId in demandStartSectors)
        {
            if (sectorId >= 0 && AddSharedGoalDemandStartSector(job, sectorId))
                changed = true;
        }
        if (changed)
            job.HasAuthorityProgressHash = false;
    }

    private static void AddSharedGoalFieldBuildDemand(SharedGoalFieldBuildJob job, int demandStartSectorId)
    {
        if (job == null)
            throw new InvalidOperationException("AddSharedGoalFieldBuildDemand failed: job is null.");
        if (demandStartSectorId < 0)
            return;

        job.DemandStartSectorIds ??= new HashSet<int>();
        if (AddSharedGoalDemandStartSector(job, demandStartSectorId))
            job.HasAuthorityProgressHash = false;
    }

    private static void AddSharedGoalFieldBuildDemand(SharedGoalFieldBuildJob job, int demandStartSectorId, int demandStartX, int demandStartY)
    {
        if (job == null)
            throw new InvalidOperationException("AddSharedGoalFieldBuildDemand failed: job is null.");
        if (demandStartSectorId < 0)
            return;

        job.DemandStartSectorIds ??= new HashSet<int>();
        bool changed = AddSharedGoalDemandStartSector(job, demandStartSectorId);
        if (demandStartX < 0
            || demandStartX >= _world.Width
            || demandStartY < 0
            || demandStartY >= _world.Height)
        {
            if (changed)
                job.HasAuthorityProgressHash = false;
            return;
        }

        job.DemandStartSectorByCellIndex ??= new Dictionary<int, int>();
        int cellIndex = _world.GetIndex(demandStartX, demandStartY);
        if (SetSharedGoalDemandStartCell(job, cellIndex, demandStartSectorId))
            changed = true;
        if (changed)
            job.HasAuthorityProgressHash = false;
    }

    private static void AddSharedGoalFieldBuildDemand(SharedGoalFieldBuildJob job, Dictionary<int, int> demandStartCells)
    {
        if (job == null)
            throw new InvalidOperationException("AddSharedGoalFieldBuildDemand failed: job is null.");
        if (demandStartCells == null || demandStartCells.Count == 0)
            return;

        job.DemandStartSectorByCellIndex ??= new Dictionary<int, int>();
        job.DemandStartSectorIds ??= new HashSet<int>();
        bool changed = false;
        foreach (KeyValuePair<int, int> pair in demandStartCells)
        {
            if (pair.Value < 0)
                continue;

            if (SetSharedGoalDemandStartCell(job, pair.Key, pair.Value))
                changed = true;
            if (AddSharedGoalDemandStartSector(job, pair.Value))
                changed = true;
        }
        if (changed)
            job.HasAuthorityProgressHash = false;
    }

    private static bool AddSharedGoalDemandStartSector(SharedGoalFieldBuildJob job, int sectorId)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));
        if (job.DemandStartSectorIds == null)
            throw new InvalidOperationException("AddSharedGoalDemandStartSector failed: demand sector set is null.");
        if (!job.DemandStartSectorIds.Add(sectorId))
            return false;

        job.DemandStartSectorAuthorityContentHash ^= ComputeAuthorityIntToken(0x534744534543544FUL, sectorId);
        return true;
    }

    private static bool SetSharedGoalDemandStartCell(SharedGoalFieldBuildJob job, int cellIndex, int sectorId)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));
        if (job.DemandStartSectorByCellIndex == null)
            throw new InvalidOperationException("SetSharedGoalDemandStartCell failed: demand cell map is null.");
        if (job.DemandStartSectorByCellIndex.TryGetValue(cellIndex, out int previousSectorId))
        {
            if (previousSectorId == sectorId)
                return false;
            job.DemandStartCellAuthorityContentHash ^= ComputeAuthorityIntIntToken(0x53474443454C4C4FUL, cellIndex, previousSectorId);
        }

        job.DemandStartSectorByCellIndex[cellIndex] = sectorId;
        job.DemandStartCellAuthorityContentHash ^= ComputeAuthorityIntIntToken(0x53474443454C4C4FUL, cellIndex, sectorId);
        return true;
    }

    private static void PromotePendingSharedGoalFieldBuildJobToFront(SharedGoalFieldKey key)
    {
        for (LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First; node != null; node = node.Next)
        {
            SharedGoalFieldBuildJob job = node.Value;
            if (job == null || !job.Key.Equals(key))
                continue;

            SharedGoalFieldBuildQueue.Remove(node);
            SharedGoalFieldBuildQueue.AddFirst(job);
            return;
        }

        PendingSharedGoalFieldBuildJobs.Remove(key);
    }

    private static bool AdvanceSharedGoalFieldBuildJob(SharedGoalFieldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));

        job.HasAuthorityProgressHash = false;
        while (job.Stage != SharedGoalFieldBuildStage.Complete)
        {
            switch (job.Stage)
            {
                case SharedGoalFieldBuildStage.Initialize:
                    if (!InitializeSharedGoalFieldBuild(job))
                        return false;
                    break;
                case SharedGoalFieldBuildStage.PortalGraph:
                    if (!AdvanceSharedGoalFieldPortalGraph(job, deadlineTicks, forceComplete))
                        return false;
                    break;
                case SharedGoalFieldBuildStage.Commit:
                    if (!forceComplete && IsDeadlineExpired(deadlineTicks))
                        return false;
                    CommitSharedGoalFieldBuildJob(job);
                    break;
                default:
                    throw new InvalidOperationException($"AdvanceSharedGoalFieldBuildJob failed: unknown stage {job.Stage}.");
            }

            if (job.Stage == SharedGoalFieldBuildStage.Complete)
                break;

            if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                return false;
        }

        return true;
    }

    private static bool InitializeSharedGoalFieldBuild(SharedGoalFieldBuildJob job)
    {
        job.GoalSector ??= _world.Sectors[job.GoalSectorId];
        job.Field = BeginSharedGoalField(job.Key, job.GoalSectorId, job.GoalX, job.GoalY, out job.PortalOpenSet);
        if (job.Field == null)
        {
            job.Stage = SharedGoalFieldBuildStage.Complete;
            return true;
        }

        job.SettledPortalNodes = job.Field.SettledPortalNodes;
        job.Stage = SharedGoalFieldBuildStage.PortalGraph;
        return true;
    }

    private static long QuantizeDeterministicPortalCost(float cost, string source)
    {
        if (float.IsNaN(cost) || float.IsNegativeInfinity(cost) || cost < 0f)
            throw new InvalidOperationException($"Deterministic portal cost is invalid. source={source} cost={cost}.");
        if (float.IsPositiveInfinity(cost))
            return long.MaxValue;

        double scaled = (double)cost * DeterministicPortalCostScale;
        if (scaled >= long.MaxValue)
            throw new OverflowException($"Deterministic portal cost overflow. source={source} cost={cost}.");
        return checked((long)Math.Round(scaled, MidpointRounding.AwayFromZero));
    }

    private static long AddDeterministicPortalCosts(long first, long second)
    {
        if (first == long.MaxValue || second == long.MaxValue)
            return long.MaxValue;
        return first > long.MaxValue - second ? long.MaxValue : first + second;
    }

    private static float DequantizeDeterministicPortalCost(long cost)
    {
        return cost == long.MaxValue ? float.PositiveInfinity : (float)((double)cost / DeterministicPortalCostScale);
    }

    private static bool AdvanceSharedGoalFieldPortalGraph(SharedGoalFieldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        int budgetWork = 0;
        while (job.PortalOpenSet.Count > 0)
        {
            DeterministicCostQueueNode node = job.PortalOpenSet.Pop();
            int currentNode = node.Index;
            if (!job.Field.NodeCosts.TryGetValue(currentNode, out long currentCost) || node.Cost != currentCost)
                continue;

            job.SettledPortalNodes ??= new HashSet<int>();
            if (job.SettledPortalNodes.Add(currentNode))
            {
                job.SettledPortalAuthorityContentHash ^= ComputeAuthorityIntToken(0x5347534554544C45UL, currentNode);
                job.Field.SettledPortalAuthorityContentHash = job.SettledPortalAuthorityContentHash;
            }

            DecodePortalNode(currentNode, out int currentSectorId, out int currentPortalId);
            PortalData currentPortal = GetPortalById(_world, currentPortalId);
            int oppositeSectorId = GetOppositeSectorId(currentPortal, currentSectorId);
            int oppositeNode = EncodePortalNode(oppositeSectorId, currentPortalId);
            AddSharedGoalReverseEdge(
                job.Field,
                job.PortalOpenSet,
                oppositeNode,
                currentNode,
                AddDeterministicPortalCosts(
                    currentCost,
                    DeterministicPortalCrossingCost));

            SectorData sector = _world.Sectors[currentSectorId];
            List<PortalTransition> incomingTransitions = GetIncomingPortalTransitions(sector, currentPortalId);
            _perf.SharedGoalPortalGraphNodeExpansions++;
            if (incomingTransitions != null)
            {
                _perf.SharedGoalPortalGraphIncomingTransitionScans += incomingTransitions.Count;
                for (int i = 0; i < incomingTransitions.Count; i++)
                {
                    PortalTransition transition = incomingTransitions[i];
                    if (transition.ToPortalId != currentPortalId)
                    {
                        throw new InvalidOperationException(
                            $"AdvanceSharedGoalFieldPortalGraph failed: incoming index mismatch sector={currentSectorId} currentPortal={currentPortalId} from={transition.FromPortalId} to={transition.ToPortalId}.");
                    }

                    _perf.SharedGoalPortalGraphIncomingTransitionHits++;
                    int predecessorNode = EncodePortalNode(currentSectorId, transition.FromPortalId);
                    AddSharedGoalReverseEdge(
                        job.Field,
                        job.PortalOpenSet,
                        predecessorNode,
                        currentNode,
                        AddDeterministicPortalCosts(
                            currentCost,
                            transition.DeterministicCost));
                }
            }

            DiscardSettledOrOutdatedSharedGoalFrontierEntries(job);

            if (IsSharedGoalFieldDemandComplete(job))
            {
                FinalizeDemandLimitedSharedGoalField(job);
                job.Stage = SharedGoalFieldBuildStage.Commit;
                return true;
            }

            if (!forceComplete && IsBudgetExpired(deadlineTicks, ++budgetWork))
                return false;
        }

        FinalizeDemandLimitedSharedGoalField(job);
        job.Stage = SharedGoalFieldBuildStage.Commit;
        return true;
    }

    private static void DiscardSettledOrOutdatedSharedGoalFrontierEntries(SharedGoalFieldBuildJob job)
    {
        if (job == null || job.Field == null || job.PortalOpenSet == null || job.SettledPortalNodes == null)
            throw new InvalidOperationException("Shared-goal frontier normalization requires initialized job state.");

        while (job.PortalOpenSet.TryPeek(out DeterministicCostQueueNode node)
               && (job.SettledPortalNodes.Contains(node.Index)
                   || !job.Field.NodeCosts.TryGetValue(node.Index, out long currentCost)
                   || currentCost != node.Cost))
        {
            job.PortalOpenSet.Pop();
            job.HasAuthorityProgressHash = false;
        }
    }

    private static bool IsSharedGoalFieldDemandComplete(SharedGoalFieldBuildJob job)
    {
        if (job == null)
            throw new InvalidOperationException("IsSharedGoalFieldDemandComplete failed: job is null.");
        if (job.SettledPortalNodes == null || job.SettledPortalNodes.Count == 0)
            return false;
        if (job.DemandStartSectorByCellIndex != null && job.DemandStartSectorByCellIndex.Count > 0)
            return IsSharedGoalFieldDemandStartCellComplete(job);
        if (job.DemandStartSectorIds == null || job.DemandStartSectorIds.Count == 0)
            return false;

        bool hasValidDemandSector = false;
        foreach (int sectorId in job.DemandStartSectorIds)
        {
            if (sectorId < 0 || sectorId >= _world.Sectors.Length)
                continue;

            SectorData sector = _world.Sectors[sectorId];
            if (sector == null || sector.PortalIds == null || sector.PortalIds.Count == 0)
                continue;

            hasValidDemandSector = true;
            for (int i = 0; i < sector.PortalIds.Count; i++)
            {
                int node = EncodePortalNode(sectorId, sector.PortalIds[i]);
                if (!job.SettledPortalNodes.Contains(node))
                    return false;
            }
        }

        return hasValidDemandSector;
    }

    private static bool IsSharedGoalFieldDemandStartCellComplete(SharedGoalFieldBuildJob job)
    {
        bool hasValidDemandCell = false;
        foreach (KeyValuePair<int, int> pair in job.DemandStartSectorByCellIndex)
        {
            if (!IsSharedGoalFieldDemandStartCellCovered(job.Field, pair.Value, pair.Key))
                return false;
            hasValidDemandCell = true;
        }

        return hasValidDemandCell;
    }

    private static void FinalizeDemandLimitedSharedGoalField(SharedGoalFieldBuildJob job)
    {
        if (job == null)
            throw new InvalidOperationException("FinalizeDemandLimitedSharedGoalField failed: job is null.");
        if (job.Field == null || job.DemandStartSectorIds == null || job.DemandStartSectorIds.Count == 0)
            return;
        if (job.SettledPortalNodes == null || job.SettledPortalNodes.Count == 0)
            return;

        if (job.DemandStartSectorIds != null)
        {
            foreach (int sectorId in job.DemandStartSectorIds)
            {
                if (sectorId >= 0)
                    AddSharedGoalCompletedDemandStartSector(job.Field, sectorId);
            }
        }

        if (job.DemandStartSectorByCellIndex != null)
        {
            foreach (int cellIndex in job.DemandStartSectorByCellIndex.Keys)
                AddSharedGoalCompletedDemandStartCell(job.Field, cellIndex);
        }
    }

    private static void CommitSharedGoalFieldBuildJob(SharedGoalFieldBuildJob job)
    {
        if (job.Field != null)
        {
            job.Field.SettledPortalAuthorityContentHash = job.SettledPortalAuthorityContentHash;
            SetSharedGoalFieldCacheEntry(job.Key, job.Field);
            _perf.SharedGoalFieldBuilds++;
            TrimSharedGoalFields();
            if (IsNavigationDistancePrewarmSharedGoalKey(job.Key))
                s_NavigationDistancePrewarmCompletionMayHaveChanged = true;
        }

        job.Stage = SharedGoalFieldBuildStage.Complete;
    }

    private static SharedGoalField BeginSharedGoalField(SharedGoalFieldKey key, int goalSectorId, int goalX, int goalY, out DeterministicCostHeap openSet)
    {
        SectorData goalSector = _world.Sectors[goalSectorId];
        SharedGoalField field = new SharedGoalField
        {
            Key = key,
            GoalSectorId = goalSectorId,
            GoalX = goalX,
            GoalY = goalY,
            LastUsedFrame = GetFrameCount()
        };

        openSet = new DeterministicCostHeap();
        field.PortalOpenSet = openSet;
        field.SettledPortalNodes = new HashSet<int>();
        for (int i = 0; i < goalSector.PortalIds.Count; i++)
        {
            int portalId = goalSector.PortalIds[i];
            long deterministicGoalCost = ResolveDeterministicGoalSectorPortalAccessCost(
                goalSector,
                goalSectorId,
                portalId,
                goalX,
                goalY);
            if (deterministicGoalCost == long.MaxValue)
                continue;

            int goalNode = EncodePortalNode(goalSectorId, portalId);
            SetSharedGoalNodeCost(field, goalNode, deterministicGoalCost);
            openSet.Push(goalNode, deterministicGoalCost);
        }

        if (openSet.Count == 0)
            return null;

        return field;
    }

    private static void AddSharedGoalReverseEdge(SharedGoalField field, DeterministicCostHeap openSet, int predecessorNode, int nextNodeTowardGoal, long cost)
    {
        if (cost == long.MaxValue)
            return;
        if (field.NodeCosts.TryGetValue(predecessorNode, out long existingCost) && cost >= existingCost)
            return;

        SetSharedGoalNodeCost(field, predecessorNode, cost);
        SetSharedGoalNextNode(field, predecessorNode, nextNodeTowardGoal);
        openSet.Push(predecessorNode, cost);
    }

    private static void SetSharedGoalNodeCost(SharedGoalField field, int node, long cost)
    {
        if (field == null)
            throw new ArgumentNullException(nameof(field));
        if (field.NodeCosts.TryGetValue(node, out long previousCost))
            field.NodeCostsAuthorityContentHash ^= ComputeAuthorityIntLongToken(0x5347464E434F5354UL, node, previousCost);
        field.NodeCosts[node] = cost;
        field.NodeCostsAuthorityContentHash ^= ComputeAuthorityIntLongToken(0x5347464E434F5354UL, node, cost);
    }

    private static void RemoveSharedGoalNodeCost(SharedGoalField field, int node)
    {
        if (field == null)
            throw new ArgumentNullException(nameof(field));
        if (!field.NodeCosts.TryGetValue(node, out long cost))
            return;
        field.NodeCostsAuthorityContentHash ^= ComputeAuthorityIntLongToken(0x5347464E434F5354UL, node, cost);
        if (!field.NodeCosts.Remove(node))
            throw new InvalidOperationException($"RemoveSharedGoalNodeCost failed: node disappeared during removal node={node}.");
    }

    private static void SetSharedGoalNextNode(SharedGoalField field, int node, int nextNode)
    {
        if (field == null)
            throw new ArgumentNullException(nameof(field));
        if (field.NextNodeTowardGoal.TryGetValue(node, out int previousNextNode))
            field.NextNodeTowardGoalAuthorityContentHash ^= ComputeAuthorityIntIntToken(0x5347464E4558544EUL, node, previousNextNode);
        field.NextNodeTowardGoal[node] = nextNode;
        field.NextNodeTowardGoalAuthorityContentHash ^= ComputeAuthorityIntIntToken(0x5347464E4558544EUL, node, nextNode);
    }

    private static void RemoveSharedGoalNextNode(SharedGoalField field, int node)
    {
        if (field == null)
            throw new ArgumentNullException(nameof(field));
        if (!field.NextNodeTowardGoal.TryGetValue(node, out int nextNode))
            return;
        field.NextNodeTowardGoalAuthorityContentHash ^= ComputeAuthorityIntIntToken(0x5347464E4558544EUL, node, nextNode);
        if (!field.NextNodeTowardGoal.Remove(node))
            throw new InvalidOperationException($"RemoveSharedGoalNextNode failed: node disappeared during removal node={node}.");
    }

    private static void AddSharedGoalCompletedDemandStartSector(SharedGoalField field, int sectorId)
    {
        if (field == null)
            throw new ArgumentNullException(nameof(field));
        if (field.CompletedDemandStartSectorIds.Add(sectorId))
            field.CompletedDemandStartSectorAuthorityContentHash ^= ComputeAuthorityIntToken(0x534746434F4D5053UL, sectorId);
    }

    private static void AddSharedGoalCompletedDemandStartCell(SharedGoalField field, int cellIndex)
    {
        if (field == null)
            throw new ArgumentNullException(nameof(field));
        if (field.CompletedDemandStartCellIndices.Add(cellIndex))
            field.CompletedDemandStartCellAuthorityContentHash ^= ComputeAuthorityIntToken(0x534746434F4D5043UL, cellIndex);
    }

    private static void TrimSharedGoalFields()
    {
        int limit = Mathf.Max(16, Config.FlowTileCacheLimit / 4);
        while (SharedGoalFields.Count > limit)
        {
            SharedGoalFieldKey oldestKey = default;
            int oldestFrame = int.MaxValue;
            bool found = false;
            foreach (KeyValuePair<SharedGoalFieldKey, SharedGoalField> pair in SharedGoalFields)
            {
                if (IsNavigationDistancePrewarmSharedGoalKey(pair.Key))
                    continue;

                int frame = pair.Value?.LastUsedFrame ?? int.MinValue;
                if (found)
                {
                    int frameOrder = frame.CompareTo(oldestFrame);
                    if (frameOrder > 0 || (frameOrder == 0 && CompareSharedGoalKeys(pair.Key, oldestKey) >= 0))
                        continue;
                }

                oldestKey = pair.Key;
                oldestFrame = frame;
                found = true;
            }

            if (!found)
                return;

            RemoveSharedGoalFieldCacheEntry(oldestKey);
        }
    }

    private static void IncrementPathHandleRebuildReason(string reason)
    {
        switch (reason)
        {
            case "noHandle":
                _perf.PathBuildNoHandle++;
                break;
            case "worldMismatch":
                _perf.PathBuildWorldMismatch++;
                break;
            case "goalSectorMismatch":
                _perf.PathBuildGoalSectorMismatch++;
                break;
            case "goalCellMismatch":
                _perf.PathBuildGoalCellMismatch++;
                break;
            case "startSectorMismatch":
                _perf.PathBuildInvalidHandle++;
                break;
            case "startPortalMismatch":
                _perf.PathBuildInvalidHandle++;
                break;
            default:
                _perf.PathBuildInvalidHandle++;
                break;
        }
    }

    private static PathHandle BuildPathHandle(
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        bool useSectorCorridorPolicy,
        int movingTargetId = int.MinValue,
        int sourceIslandId = -1)
    {
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        _perf.PathBuilds++;
        SectorData startSector = _world.Sectors[startSectorId];
        SectorData goalSector = _world.Sectors[goalSectorId];

        if (startSectorId == goalSectorId)
        {
            if (AreCellsConnectedInsideSector(startSector, startX, startY, goalX, goalY))
            {
                PathHandle sameSectorHandle = new PathHandle
                {
                    HandleId = _nextPathHandleId++,
                    WorldVersion = _world.Version,
                    GoalX = goalX,
                    GoalY = goalY,
                    SectorIds = new[] { startSectorId },
                    PortalIds = Array.Empty<int>(),
                    CurrentSectorIndex = 0,
                    BuildSource = "sameSector"
                };
                if (GameDebugSettings.IsEnabled(DebugCategory.Move)
                    && TryConsumeSuccessfulMoveDiagnosticBudget())
                {
                    GameDebugSettings.Log(DebugCategory.Move,
                        $"[FlowPathHandleBuild] result=ok sameSector=true startSector={startSectorId} goalSector={goalSectorId} " +
                        $"start=({startX},{startY}) goal=({goalX},{goalY}) handle={FormatPathHandle(sameSectorHandle)}");
                }

                return sameSectorHandle;
            }

            if (GameDebugSettings.IsEnabled(DebugCategory.Move))
            {
                GameDebugSettings.Log(DebugCategory.Move,
                    $"[FlowPathHandleBuild] sameSectorLocalBlocked=true startSector={startSectorId} goalSector={goalSectorId} " +
                    $"start=({startX},{startY}) goal=({goalX},{goalY}); trying portal route");
            }
        }

        SectorPathCacheKey sectorPathKey = CreateSectorPathCacheKey(startSectorId, startX, startY, goalSectorId, goalX, goalY);
        long stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool cacheHit = TryCreatePathHandleFromSectorPathCache(
            sectorPathKey,
            startSector,
            startSectorId,
            goalSectorId,
            startX,
            startY,
            goalX,
            goalY,
            out PathHandle cachedHandle);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowPathBuildCache,
                Stopwatch.GetTimestamp() - stageStartTicks);
        }
        if (cacheHit)
            return cachedHandle;

        int sharedAgentTypeId = ResolvePreferredAgentTypeId(_world.AgentTypeId);
        stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        PathHandle sharedHandle = null;
        bool sharedGoalBuilt = TryGetCachedSharedGoalField(goalSectorId, goalX, goalY, sharedAgentTypeId, out SharedGoalField sharedField)
                               && TryBuildPathHandleFromSharedGoalField(
                sectorPathKey,
                sharedField,
                startSector,
                startSectorId,
                goalSectorId,
                startX,
                startY,
                goalX,
                goalY,
                out sharedHandle);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowPathBuildSharedGoal,
                Stopwatch.GetTimestamp() - stageStartTicks);
        }
        if (sharedGoalBuilt)
        {
            return sharedHandle;
        }

        stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        PathHandle policyHandle = null;
        bool corridorPolicyBuilt = useSectorCorridorPolicy
                                   && startSectorId != goalSectorId
                                   && TryBuildPathHandleFromSectorCorridorPolicy(
                sectorPathKey,
                startSector,
                startSectorId,
                goalSectorId,
                startX,
                startY,
                goalX,
                goalY,
                sharedAgentTypeId,
                movingTargetId,
                sourceIslandId,
                out policyHandle);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowPathBuildCorridorPolicy,
                Stopwatch.GetTimestamp() - stageStartTicks);
        }
        if (corridorPolicyBuilt)
        {
            return policyHandle;
        }

        _perf.SectorPathSearches++;
        if (IsPortalHierarchyQueryRequired(_world, startSectorId, goalSectorId))
        {
            stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            bool hierarchyBuilt = TryBuildPathHandleWithPortalHierarchy(
                sectorPathKey,
                startSectorId,
                goalSectorId,
                startX,
                startY,
                goalX,
                goalY,
                out PathHandle hierarchyHandle);
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowPathBuildHierarchy,
                    Stopwatch.GetTimestamp() - stageStartTicks);
            }
            return hierarchyBuilt ? hierarchyHandle : null;
        }

        stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool graphBuilt = TryBuildPathHandleWithPortalGraphAStar(
            sectorPathKey,
            startSector,
            goalSector,
            startSectorId,
            goalSectorId,
            startX,
            startY,
            goalX,
            goalY,
            out PathHandle graphHandle);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowPathBuildPortalGraph,
                Stopwatch.GetTimestamp() - stageStartTicks);
        }
        if (graphBuilt)
        {
            return graphHandle;
        }

        return null;
    }

    private static bool TryEstimateFlowPathDistanceFixed(
        NavigationWorld world,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out Fix64 distance)
    {
        distance = Fix64.Zero;
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (!ReferenceEquals(world, _world))
            throw new InvalidOperationException("Flow path distance estimation requires the active committed navigation world.");
        if (!world.TryGetSectorId(startX, startY, out int startSectorId)
            || !world.TryGetSectorId(goalX, goalY, out int goalSectorId))
        {
            return false;
        }

        if (startSectorId == goalSectorId
            && TryFindSectorPathFixed(
                world,
                world.Sectors[startSectorId],
                startX,
                startY,
                goalX,
                goalY,
                out distance))
        {
            return true;
        }

        int sharedAgentTypeId = ResolvePreferredAgentTypeId(world.AgentTypeId);
        if (!TryGetCachedSharedGoalField(goalSectorId, goalX, goalY, sharedAgentTypeId, out SharedGoalField sharedField))
            return false;
        int startCellIndex = world.GetIndex(startX, startY);
        if (!IsSharedGoalFieldDemandCovered(sharedField, startSectorId, startCellIndex))
            return false;

        SectorData startSector = world.Sectors[startSectorId];
        long bestCost = long.MaxValue;
        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            int startNode = EncodePortalNode(startSectorId, portalId);
            if (!sharedField.SettledPortalNodes.Contains(startNode)
                || !sharedField.NodeCosts.TryGetValue(startNode, out long downstreamCost))
                continue;
            long startCost = ResolveDeterministicPortalAccessCost(startSector, startSectorId, portalId, startX, startY);
            bestCost = Math.Min(bestCost, AddDeterministicPortalCosts(startCost, downstreamCost));
        }

        if (bestCost == long.MaxValue)
            return false;
        distance = Fix64.FromRaw(bestCost) * world.CellSizeFixed;
        return distance > Fix64.Zero;
    }

    private static bool TryBuildPathHandleFromSharedGoalField(
        SectorPathCacheKey sectorPathKey,
        SharedGoalField sharedField,
        SectorData startSector,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out PathHandle handle)
    {
        handle = null;
        if (sharedField == null)
            return false;
        int startCellIndex = _world.GetIndex(startX, startY);
        if (!IsSharedGoalFieldDemandCovered(sharedField, startSectorId, startCellIndex))
            return false;

        int bestStartNode = int.MinValue;
        long bestStartCost = long.MaxValue;
        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            int startNode = EncodePortalNode(startSectorId, portalId);
            if (!sharedField.SettledPortalNodes.Contains(startNode)
                || !sharedField.NodeCosts.TryGetValue(startNode, out long downstreamCost))
                continue;

            long startCost = ResolveDeterministicPortalAccessCost(startSector, startSectorId, portalId, startX, startY);
            if (startCost == long.MaxValue)
                continue;

            long totalCost = AddDeterministicPortalCosts(
                startCost,
                downstreamCost);
            if (totalCost >= bestStartCost)
                continue;

            bestStartCost = totalCost;
            bestStartNode = startNode;
        }

        if (bestStartNode == int.MinValue)
            return false;

        List<int> sectorIds = new List<int>(8) { startSectorId };
        List<int> portalIds = new List<int>(8);
        int cursor = bestStartNode;
        int guard = 0;
        while (sharedField.NextNodeTowardGoal.TryGetValue(cursor, out int nextNode))
        {
            DecodePortalNode(cursor, out int fromSectorId, out int fromPortalId);
            DecodePortalNode(nextNode, out int toSectorId, out int toPortalId);
            if (fromPortalId == toPortalId && fromSectorId != toSectorId)
            {
                portalIds.Add(fromPortalId);
                sectorIds.Add(toSectorId);
            }

            cursor = nextNode;
            guard++;
            if (guard > _world.Sectors.Length + _world.PortalsById.Count)
                throw new InvalidOperationException("BuildPathHandle failed: shared goal field path reconstruction exceeded guard.");
        }

        int endSectorId = DecodePortalSector(cursor);
        if (portalIds.Count == 0 || sectorIds[sectorIds.Count - 1] != goalSectorId)
        {
            if (GameDebugSettings.IsEnabled(DebugCategory.Move))
            {
                GameDebugSettings.Log(DebugCategory.Move,
                    $"[FlowPathHandleBuild] result=null source=sharedGoal startSector={startSectorId} goalSector={goalSectorId} " +
                    $"start=({startX},{startY}) goal=({goalX},{goalY}) bestStartNode={bestStartNode} bestStartCost={DequantizeDeterministicPortalCost(bestStartCost):F3} endSector={endSectorId} " +
                    $"sharedNodes={sharedField.NodeCosts.Count} sectorIdsPartial=[{string.Join(",", sectorIds)}] portalIdsPartial=[{string.Join(",", portalIds)}]");
            }
            return false;
        }

        handle = CreateAndCachePathHandle(sectorPathKey, sectorIds, portalIds, goalX, goalY);
        handle.BuildSource = "sharedGoal";
        if (GameDebugSettings.IsEnabled(DebugCategory.Move)
            && TryConsumeSuccessfulMoveDiagnosticBudget())
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowPathHandleBuild] result=ok source=sharedGoal startSector={startSectorId} goalSector={goalSectorId} " +
                $"start=({startX},{startY}) goal=({goalX},{goalY}) handle={FormatPathHandle(handle)}");
        }

        return true;
    }

    private static bool TryBuildPathHandleWithPortalGraphAStar(
        SectorPathCacheKey sectorPathKey,
        SectorData startSector,
        SectorData goalSector,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out PathHandle handle)
    {
        handle = null;
        DeterministicCostHeap openSet = new DeterministicCostHeap();
        Dictionary<int, long> nodeCosts = new Dictionary<int, long>(256);
        Dictionary<int, int> cameFrom = new Dictionary<int, int>(256);
        Dictionary<int, int> startPortalByNode = new Dictionary<int, int>(64);
        HashSet<int> goalNodes = new HashSet<int>();
        Dictionary<int, long> goalAccessCosts = new Dictionary<int, long>(64);
        Dictionary<int, ExistingPathMergePoint> mergeNodes = BuildExistingPathMergeNodes(startSectorId, goalSectorId, goalX, goalY);

        for (int i = 0; i < goalSector.PortalIds.Count; i++)
        {
            int portalId = goalSector.PortalIds[i];
            long goalAccessCost = ResolveDeterministicGoalSectorPortalAccessCost(goalSector, goalSectorId, portalId, goalX, goalY);
            if (goalAccessCost == long.MaxValue)
                continue;

            int goalNode = EncodePortalNode(goalSectorId, portalId);
            goalNodes.Add(goalNode);
            goalAccessCosts[goalNode] = goalAccessCost;
        }

        if (goalNodes.Count == 0)
            return false;

        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            long startCost = ResolveDeterministicPortalAccessCost(startSector, startSectorId, portalId, startX, startY);
            if (startCost == long.MaxValue)
                continue;

            int startNode = EncodePortalNode(startSectorId, portalId);
            long startCostRaw = startCost;
            long priority = AddDeterministicPortalCosts(
                startCostRaw,
                ResolveDeterministicPortalGraphHeuristic(startNode, goalX, goalY));
            nodeCosts[startNode] = startCostRaw;
            startPortalByNode[startNode] = portalId;
            openSet.Push(startNode, priority);
        }

        int bestGoalNode = int.MinValue;
        long bestGoalCost = long.MaxValue;
        int bestMergeNode = int.MinValue;
        long bestMergeCost = long.MaxValue;
        ExistingPathMergePoint bestMergePoint = default;
        int guard = 0;
        while (openSet.Count > 0)
        {
            DeterministicCostQueueNode node = openSet.Pop();
            int currentNode = node.Index;
            if (!nodeCosts.TryGetValue(currentNode, out long currentCost))
                continue;
            if (node.Cost != AddDeterministicPortalCosts(
                    currentCost,
                    ResolveDeterministicPortalGraphHeuristic(currentNode, goalX, goalY)))
                continue;

            bool canFinishAtGoalNode = goalNodes.Contains(currentNode)
                                       && (startSectorId != goalSectorId || cameFrom.ContainsKey(currentNode));
            if (canFinishAtGoalNode)
            {
                long goalAccessCost = goalAccessCosts.TryGetValue(currentNode, out long foundGoalAccess)
                    ? foundGoalAccess
                    : 0L;
                long totalGoalCost = AddDeterministicPortalCosts(currentCost, goalAccessCost);
                if (totalGoalCost < bestGoalCost)
                {
                    bestGoalNode = currentNode;
                    bestGoalCost = totalGoalCost;
                }
            }

            if (mergeNodes.TryGetValue(currentNode, out ExistingPathMergePoint mergePoint)
                && TryResolveExistingPathSuffixCost(mergePoint, goalSectorId, goalX, goalY, out long suffixCost))
            {
                long totalMergeCost = AddDeterministicPortalCosts(currentCost, suffixCost);
                if (totalMergeCost <= bestMergeCost
                    && totalMergeCost <= bestGoalCost)
                {
                    bestMergeNode = currentNode;
                    bestMergeCost = totalMergeCost;
                    bestMergePoint = mergePoint;
                }
            }

            DecodePortalNode(currentNode, out int currentSectorId, out int currentPortalId);
            PortalData currentPortal = GetPortalById(_world, currentPortalId);
            int oppositeSectorId = GetOppositeSectorId(currentPortal, currentSectorId);
            int oppositeNode = EncodePortalNode(oppositeSectorId, currentPortalId);
            TryRelaxPortalGraphEdge(
                openSet,
                nodeCosts,
                cameFrom,
                currentNode,
                oppositeNode,
                AddDeterministicPortalCosts(
                    currentCost,
                    DeterministicPortalCrossingCost),
                goalX,
                goalY);

            SectorData sector = _world.Sectors[currentSectorId];
            List<PortalTransition> outgoingTransitions = GetOutgoingPortalTransitions(sector, currentPortalId);
            _perf.PathPortalGraphNodeExpansions++;
            if (outgoingTransitions != null)
            {
                _perf.PathPortalGraphOutgoingTransitionScans += outgoingTransitions.Count;
                for (int i = 0; i < outgoingTransitions.Count; i++)
                {
                    PortalTransition transition = outgoingTransitions[i];
                    if (transition.FromPortalId != currentPortalId)
                    {
                        throw new InvalidOperationException(
                            $"TryBuildPathHandleWithPortalGraphAStar failed: outgoing index mismatch sector={currentSectorId} currentPortal={currentPortalId} from={transition.FromPortalId} to={transition.ToPortalId}.");
                    }

                    _perf.PathPortalGraphOutgoingTransitionHits++;
                    int nextNode = EncodePortalNode(currentSectorId, transition.ToPortalId);
                    TryRelaxPortalGraphEdge(
                        openSet,
                        nodeCosts,
                        cameFrom,
                        currentNode,
                        nextNode,
                        AddDeterministicPortalCosts(
                            currentCost,
                            transition.DeterministicCost),
                        goalX,
                        goalY);
                }
            }

            long bestKnownFinishCost = Math.Min(bestGoalCost, bestMergeCost);
            if (openSet.PeekCost >= bestKnownFinishCost)
                break;

            guard++;
            if (guard > _world.Sectors.Length * Mathf.Max(1, _world.PortalsById.Count + 1))
                throw new InvalidOperationException("TryBuildPathHandleWithPortalGraphAStar failed: graph search exceeded guard.");
        }

        if (bestGoalNode == int.MinValue && bestMergeNode == int.MinValue)
            return false;

        bool useMergedPath = bestMergeNode != int.MinValue && bestMergeCost <= bestGoalCost;
        long finalCost = useMergedPath ? bestMergeCost : bestGoalCost;
        List<int> sectorIds;
        List<int> portalIds;
        int startPortalId;
        if (useMergedPath)
        {
            if (!TryConvertPortalNodesToMergedPath(bestMergeNode, cameFrom, bestMergePoint, startSectorId, goalSectorId, out sectorIds, out portalIds))
                return false;

            startPortalId = DecodePortalId(ResolveFirstPortalGraphNode(bestMergeNode, cameFrom));
        }
        else
        {
            List<int> reversedNodes = new List<int>(16);
            int cursor = bestGoalNode;
            reversedNodes.Add(cursor);
            guard = 0;
            while (cameFrom.TryGetValue(cursor, out int previousNode))
            {
                cursor = previousNode;
                reversedNodes.Add(cursor);
                guard++;
                if (guard > _world.Sectors.Length + _world.PortalsById.Count)
                    throw new InvalidOperationException("TryBuildPathHandleWithPortalGraphAStar failed: reconstruction exceeded guard.");
            }

            reversedNodes.Reverse();
            if (!TryConvertPortalNodesToPath(reversedNodes, startSectorId, goalSectorId, out sectorIds, out portalIds))
                return false;

            startPortalId = startPortalByNode.TryGetValue(reversedNodes[0], out int foundStartPortal) ? foundStartPortal : -1;
        }

        handle = CreateAndCachePathHandle(sectorPathKey, sectorIds, portalIds, goalX, goalY);
        handle.BuildSource = useMergedPath ? "portalGraphMerged" : "portalGraph";
        if (useMergedPath)
            _perf.PortalGraphMergeHits++;
        if (GameDebugSettings.IsEnabled(DebugCategory.Move)
            && TryConsumeSuccessfulMoveDiagnosticBudget())
        {
            long successDiagStartTicks = Stopwatch.GetTimestamp();
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowPathHandleBuild] result=ok source={handle.BuildSource} startSector={startSectorId} goalSector={goalSectorId} " +
                $"start=({startX},{startY}) goal=({goalX},{goalY}) startPortal={startPortalId} cost={DequantizeDeterministicPortalCost(finalCost):F3} " +
                $"mergeNode={bestMergeNode} mergeCost={DequantizeDeterministicPortalCost(bestMergeCost):F3} goalCost={DequantizeDeterministicPortalCost(bestGoalCost):F3} handle={FormatPathHandle(handle)}");
            _perf.SuccessfulMoveDiagnosticTicks += Stopwatch.GetTimestamp() - successDiagStartTicks;
        }

        return true;
    }

    private readonly struct ExistingPathMergePoint
    {
        public readonly int[] SectorIds;
        public readonly int[] PortalIds;
        public readonly int SectorIndex;

        public ExistingPathMergePoint(int[] sectorIds, int[] portalIds, int sectorIndex)
        {
            SectorIds = sectorIds;
            PortalIds = portalIds;
            SectorIndex = sectorIndex;
        }
    }

    private static Dictionary<int, ExistingPathMergePoint> BuildExistingPathMergeNodes(int startSectorId, int goalSectorId, int goalX, int goalY)
    {
        Dictionary<int, ExistingPathMergePoint> mergeNodes = null;
        for (int agentIndex = 0; agentIndex < OrderedAgentIds.Count; agentIndex++)
        {
            AgentRuntimeData agent = Agents[OrderedAgentIds[agentIndex]]
                ?? throw new InvalidOperationException($"BuildExistingPathMergeNodes failed: agent is null id={OrderedAgentIds[agentIndex]}.");
            PathHandle handle = agent.NavState.PathHandle;
            if (handle == null
                || handle.WorldVersion != _world.Version
                || handle.SectorIds == null
                || handle.PortalIds == null
                || handle.SectorIds.Length <= 1
                || handle.SectorIds[handle.SectorIds.Length - 1] != goalSectorId
                || handle.GoalX != goalX
                || handle.GoalY != goalY
                || PathReferencesMissingPortal(handle))
            {
                continue;
            }

            for (int i = Mathf.Max(0, handle.CurrentSectorIndex); i < handle.PortalIds.Length; i++)
            {
                int sectorId = handle.SectorIds[i];
                if (sectorId == startSectorId)
                    continue;

                int node = EncodePortalNode(sectorId, handle.PortalIds[i]);
                mergeNodes ??= new Dictionary<int, ExistingPathMergePoint>(32);
                if (!mergeNodes.ContainsKey(node))
                {
                    mergeNodes[node] = new ExistingPathMergePoint(
                        (int[])handle.SectorIds.Clone(),
                        (int[])handle.PortalIds.Clone(),
                        i);
                }
            }
        }

        return mergeNodes ?? EmptyMergeNodes;
    }

    private static readonly Dictionary<int, ExistingPathMergePoint> EmptyMergeNodes = new Dictionary<int, ExistingPathMergePoint>(0);

    private static bool TryResolveExistingPathSuffixCost(ExistingPathMergePoint mergePoint, int goalSectorId, int goalX, int goalY, out long cost)
    {
        cost = 0L;
        if (mergePoint.SectorIds == null
            || mergePoint.PortalIds == null
            || mergePoint.SectorIndex < 0
            || mergePoint.SectorIndex >= mergePoint.PortalIds.Length
            || mergePoint.PortalIds.Length + 1 != mergePoint.SectorIds.Length)
        {
            return false;
        }

        for (int i = mergePoint.SectorIndex; i < mergePoint.PortalIds.Length; i++)
        {
            int fromSectorId = mergePoint.SectorIds[i];
            int portalId = mergePoint.PortalIds[i];
            PortalData portal = GetPortalById(_world, portalId);
            if (!PortalTouchesSector(portal, fromSectorId))
                return false;

            cost = AddDeterministicPortalCosts(
                cost,
                DeterministicPortalCrossingCost);
            int nextSectorId = mergePoint.SectorIds[i + 1];
            if (GetOppositeSectorId(portal, fromSectorId) != nextSectorId)
                return false;

            if (i + 1 < mergePoint.PortalIds.Length)
            {
                long transitionCost = ResolveDeterministicPortalTransitionCost(nextSectorId, portalId, mergePoint.PortalIds[i + 1]);
                if (transitionCost == long.MaxValue)
                    return false;

                cost = AddDeterministicPortalCosts(
                    cost,
                    transitionCost);
            }
            else
            {
                if (nextSectorId != goalSectorId)
                    return false;

                long goalAccessCost = ResolveDeterministicGoalSectorPortalAccessCost(_world.Sectors[goalSectorId], goalSectorId, portalId, goalX, goalY);
                if (goalAccessCost == long.MaxValue)
                    return false;

                cost = AddDeterministicPortalCosts(
                    cost,
                    goalAccessCost);
            }
        }

        return true;
    }

    private static bool PortalTouchesSector(PortalData portal, int sectorId)
    {
        return portal.SectorAId == sectorId || portal.SectorBId == sectorId;
    }

    private static float ResolvePortalTransitionCost(int sectorId, int fromPortalId, int toPortalId)
    {
        return DequantizeDeterministicPortalCost(
            ResolveDeterministicPortalTransitionCost(sectorId, fromPortalId, toPortalId));
    }

    private static long ResolveDeterministicPortalTransitionCost(int sectorId, int fromPortalId, int toPortalId)
    {
        return ResolveDeterministicPortalTransitionCost(_world, sectorId, fromPortalId, toPortalId);
    }

    private static long ResolveDeterministicPortalTransitionCost(
        NavigationWorld world,
        int sectorId,
        int fromPortalId,
        int toPortalId)
    {
        if (world == null)
            throw new InvalidOperationException("ResolveDeterministicPortalTransitionCost failed: world is null.");
        if (sectorId < 0 || sectorId >= world.Sectors.Length)
            throw new ArgumentOutOfRangeException(nameof(sectorId));
        SectorData sector = world.Sectors[sectorId];
        List<PortalTransition> outgoingTransitions = GetOutgoingPortalTransitions(sector, fromPortalId);
        if (outgoingTransitions == null)
            return long.MaxValue;

        for (int i = 0; i < outgoingTransitions.Count; i++)
        {
            PortalTransition transition = outgoingTransitions[i];
            if (transition.FromPortalId != fromPortalId)
            {
                throw new InvalidOperationException(
                    $"ResolvePortalTransitionCost failed: outgoing index mismatch sector={sectorId} fromPortal={fromPortalId} transitionFrom={transition.FromPortalId} to={transition.ToPortalId}.");
            }

            if (transition.ToPortalId == toPortalId)
                return transition.DeterministicCost;
        }

        return long.MaxValue;
    }

    private static int ResolveFirstPortalGraphNode(int node, Dictionary<int, int> cameFrom)
    {
        int cursor = node;
        int guard = 0;
        while (cameFrom.TryGetValue(cursor, out int previousNode))
        {
            cursor = previousNode;
            guard++;
            if (guard > _world.Sectors.Length + _world.PortalsById.Count)
                throw new InvalidOperationException("ResolveFirstPortalGraphNode failed: reconstruction exceeded guard.");
        }

        return cursor;
    }

    private static int DecodePortalId(int node)
    {
        DecodePortalNode(node, out _, out int portalId);
        return portalId;
    }

    private static bool TryConvertPortalNodesToMergedPath(
        int mergeNode,
        Dictionary<int, int> cameFrom,
        ExistingPathMergePoint mergePoint,
        int startSectorId,
        int goalSectorId,
        out List<int> sectorIds,
        out List<int> portalIds)
    {
        sectorIds = null;
        portalIds = null;
        List<int> prefixNodes = new List<int>(16);
        int cursor = mergeNode;
        prefixNodes.Add(cursor);
        int guard = 0;
        while (cameFrom.TryGetValue(cursor, out int previousNode))
        {
            cursor = previousNode;
            prefixNodes.Add(cursor);
            guard++;
            if (guard > _world.Sectors.Length + _world.PortalsById.Count)
                throw new InvalidOperationException("TryConvertPortalNodesToMergedPath failed: prefix reconstruction exceeded guard.");
        }

        prefixNodes.Reverse();
        if (!TryConvertPortalNodesToPathPrefix(prefixNodes, startSectorId, out sectorIds, out portalIds))
            return false;

        int mergeSector = mergePoint.SectorIds[mergePoint.SectorIndex];
        if (sectorIds[sectorIds.Count - 1] != mergeSector)
            return false;

        for (int i = mergePoint.SectorIndex; i < mergePoint.PortalIds.Length; i++)
        {
            portalIds.Add(mergePoint.PortalIds[i]);
            sectorIds.Add(mergePoint.SectorIds[i + 1]);
        }

        return portalIds.Count > 0 && sectorIds[sectorIds.Count - 1] == goalSectorId;
    }

    private static bool TryConvertPortalNodesToPathPrefix(List<int> nodes, int startSectorId, out List<int> sectorIds, out List<int> portalIds)
    {
        sectorIds = new List<int>(8) { startSectorId };
        portalIds = new List<int>(8);
        if (nodes == null || nodes.Count == 0)
            return false;

        for (int i = 0; i < nodes.Count - 1; i++)
        {
            DecodePortalNode(nodes[i], out int fromSectorId, out int fromPortalId);
            DecodePortalNode(nodes[i + 1], out int toSectorId, out int toPortalId);
            if (fromPortalId == toPortalId && fromSectorId != toSectorId)
            {
                portalIds.Add(fromPortalId);
                sectorIds.Add(toSectorId);
            }
        }

        return sectorIds.Count > 0;
    }

    private static void TryRelaxPortalGraphEdge(
        DeterministicCostHeap openSet,
        Dictionary<int, long> nodeCosts,
        Dictionary<int, int> cameFrom,
        int fromNode,
        int toNode,
        long newCost,
        int goalX,
        int goalY)
    {
        if (newCost == long.MaxValue)
            return;
        if (nodeCosts.TryGetValue(toNode, out long existingCost) && newCost >= existingCost)
            return;

        nodeCosts[toNode] = newCost;
        cameFrom[toNode] = fromNode;
        long priority = AddDeterministicPortalCosts(
            newCost,
            ResolveDeterministicPortalGraphHeuristic(toNode, goalX, goalY));
        openSet.Push(toNode, priority);
    }

    private static long ResolveDeterministicPortalGraphHeuristic(int node, int goalX, int goalY)
    {
        DecodePortalNode(node, out int sectorId, out int portalId);
        PortalData portal = GetPortalById(_world, portalId);
        Vector2Int[] portalCells = GetPortalCellsForSector(portal, sectorId);
        if (portalCells == null || portalCells.Length == 0)
            throw new InvalidOperationException($"ResolveDeterministicPortalGraphHeuristic failed: portal has no cells. node={node} sector={sectorId} portal={portalId}.");

        int minimumCellDistance = int.MaxValue;
        for (int i = 0; i < portalCells.Length; i++)
        {
            int dx = Math.Abs(portalCells[i].x - goalX);
            int dy = Math.Abs(portalCells[i].y - goalY);
            minimumCellDistance = Math.Min(minimumCellDistance, Math.Max(dx, dy));
        }
        return checked((long)minimumCellDistance * DeterministicPortalCostScale);
    }

    private static bool TryConvertPortalNodesToPath(List<int> nodes, int startSectorId, int goalSectorId, out List<int> sectorIds, out List<int> portalIds)
    {
        sectorIds = new List<int>(8) { startSectorId };
        portalIds = new List<int>(8);
        if (nodes == null || nodes.Count == 0)
            return false;

        for (int i = 0; i < nodes.Count - 1; i++)
        {
            DecodePortalNode(nodes[i], out int fromSectorId, out int fromPortalId);
            DecodePortalNode(nodes[i + 1], out int toSectorId, out int toPortalId);
            if (fromPortalId == toPortalId && fromSectorId != toSectorId)
            {
                portalIds.Add(fromPortalId);
                sectorIds.Add(toSectorId);
            }
        }

        return portalIds.Count > 0 && sectorIds[sectorIds.Count - 1] == goalSectorId;
    }

    private static PathHandle CreateAndCachePathHandle(SectorPathCacheKey sectorPathKey, List<int> sectorIds, List<int> portalIds, int goalX, int goalY)
    {
        if (sectorIds == null || sectorIds.Count == 0 || portalIds == null)
            throw new InvalidOperationException("CreateAndCachePathHandle failed: invalid path lists.");

        return CreateAndCachePathHandle(sectorPathKey, sectorIds.ToArray(), portalIds.ToArray(), goalX, goalY);
    }

    private static PathHandle CreateAndCachePathHandle(SectorPathCacheKey sectorPathKey, int[] sectorIds, int[] portalIds, int goalX, int goalY)
    {
        if (sectorIds == null || sectorIds.Length == 0 || portalIds == null || portalIds.Length + 1 != sectorIds.Length)
            throw new InvalidOperationException("CreateAndCachePathHandle failed: invalid immutable route arrays.");

        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long createStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        PathHandle handle = new PathHandle
        {
            HandleId = _nextPathHandleId++,
            WorldVersion = _world.Version,
            GoalX = goalX,
            GoalY = goalY,
            SectorIds = sectorIds,
            PortalIds = portalIds,
            CurrentSectorIndex = 0,
            BuildSource = "created"
        };
        SetSectorPathCacheEntry(sectorPathKey, new SectorPathCacheEntry
        {
            SectorIds = sectorIds,
            PortalIds = portalIds,
            LastUsedFrame = GetFrameCount()
        });
        TrimSectorPathCache();
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowPathHandleCreateCache,
                Stopwatch.GetTimestamp() - createStartTicks);
        }
        return handle;
    }

    private static void TrimSectorPathCache()
    {
        int limit = Mathf.Max(32, Config.FlowTileCacheLimit * 2);
        if (SectorPathCache.Count <= limit)
            return;

        SectorPathCacheKey oldestKey = default;
        int oldestFrame = int.MaxValue;
        bool found = false;
        foreach (KeyValuePair<SectorPathCacheKey, SectorPathCacheEntry> pair in SectorPathCache)
        {
            int frame = pair.Value?.LastUsedFrame ?? int.MinValue;
            if (found)
            {
                int frameOrder = frame.CompareTo(oldestFrame);
                if (frameOrder > 0 || (frameOrder == 0 && CompareSectorPathKeys(pair.Key, oldestKey) >= 0))
                    continue;
            }

            oldestKey = pair.Key;
            oldestFrame = frame;
            found = true;
        }

        if (found)
            RemoveSectorPathCacheEntry(oldestKey);
    }

    private static SectorPathCacheKey CreateSectorPathCacheKey(int startSectorId, int startX, int startY, int goalSectorId, int goalX, int goalY)
    {
        SectorData startSector = _world.Sectors[startSectorId];
        SectorData goalSector = _world.Sectors[goalSectorId];
        return new SectorPathCacheKey(
            _world.Version,
            startSectorId,
            _world.GetIndex(startX, startY),
            goalSectorId,
            _world.GetIndex(goalX, goalY),
            startSector.DirtyVersion,
            goalSector.DirtyVersion);
    }

    private static bool TryCreatePathHandleFromSectorPathCache(
        SectorPathCacheKey key,
        SectorData startSector,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out PathHandle handle)
    {
        handle = null;
        if (!SectorPathCache.TryGetValue(key, out SectorPathCacheEntry cached)
            || cached.SectorIds == null
            || cached.PortalIds == null
            || cached.SectorIds.Length == 0
            || cached.PortalIds.Length == 0)
        {
            return false;
        }

        if (cached.SectorIds[0] != startSectorId || cached.SectorIds[cached.SectorIds.Length - 1] != goalSectorId)
        {
            RemoveSectorPathCacheEntry(key);
            return false;
        }

        int firstPortalId = cached.PortalIds[0];
        long startCost = ResolveDeterministicPortalAccessCost(startSector, startSectorId, firstPortalId, startX, startY);
        if (startCost == long.MaxValue)
            return false;

        if (TryResolveBestStartPortalForCurrentCell(
                startSectorId,
                goalSectorId,
                startX,
                startY,
                goalX,
                goalY,
                firstPortalId,
                out int bestPortalId,
                out long selectedCost,
                out long bestCost)
            && firstPortalId != bestPortalId
            && selectedCost > bestCost)
        {
            RemoveSectorPathCacheEntry(key);
            if (GameDebugSettings.IsEnabled(DebugCategory.Move))
            {
                GameDebugSettings.Log(DebugCategory.Move,
                    $"[FlowPathCacheReject] reason=startPortalMismatch startSector={startSectorId} goalSector={goalSectorId} " +
                    $"start=({startX},{startY}) goal=({goalX},{goalY}) selected={firstPortalId}:{FormatDiagnosticCost(DequantizeDeterministicPortalCost(selectedCost))} " +
                    $"best={bestPortalId}:{FormatDiagnosticCost(DequantizeDeterministicPortalCost(bestCost))}");
            }

            return false;
        }

        cached.LastUsedFrame = GetFrameCount();
        _perf.SectorPathCacheHits++;
        handle = new PathHandle
        {
            HandleId = _nextPathHandleId++,
            WorldVersion = _world.Version,
            GoalX = goalX,
            GoalY = goalY,
            SectorIds = cached.SectorIds,
            PortalIds = cached.PortalIds,
            CurrentSectorIndex = 0,
            BuildSource = "sectorPathCache"
        };

        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowPathCacheHit] startSector={startSectorId} goalSector={goalSectorId} start=({startX},{startY}) goal=({goalX},{goalY}) handle={FormatPathHandle(handle)}");
        }

        return true;
    }

    private static bool TryBuildPathHandleFromSectorCorridorPolicy(
        SectorPathCacheKey sectorPathKey,
        SectorData startSector,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int agentTypeId,
        int movingTargetId,
        int sourceIslandId,
        out PathHandle handle)
    {
        handle = null;
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long lookupStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        int policyGoalCellIndex = movingTargetId == int.MinValue
            ? _world.GetIndex(goalX, goalY)
            : -1;
        var policyKey = new SectorCorridorPolicyKey(
            _world.Version,
            agentTypeId,
            movingTargetId,
            sourceIslandId,
            goalSectorId,
            policyGoalCellIndex,
            _world.Sectors[goalSectorId].DirtyVersion);
        MovingTargetAnchor pinnedAnchor = null;
        bool policyFound;
        SectorCorridorPolicy policy;
        if (movingTargetId != int.MinValue)
        {
            if (sourceIslandId <= 0)
                throw new InvalidOperationException($"Moving-target corridor policy requires a valid source island target={movingTargetId}, island={sourceIslandId}.");
            var anchorKey = new MovingTargetAnchorKey(movingTargetId, agentTypeId, sourceIslandId);
            if (!MovingTargetAnchors.TryGetValue(anchorKey, out pinnedAnchor) || pinnedAnchor == null)
            {
                throw new InvalidOperationException(
                    $"Moving-target corridor policy has no stable-goal anchor target={movingTargetId}, agentType={agentTypeId}, island={sourceIslandId}.");
            }
            policyFound = TryAcquirePinnedMovingTargetSectorCorridorPolicy(
                pinnedAnchor,
                policyKey,
                goalSectorId,
                goalX,
                goalY,
                out policy);
        }
        else
        {
            policyFound = SectorCorridorPolicies.TryGetValue(policyKey, out policy);
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCorridorPolicyLookup,
                Stopwatch.GetTimestamp() - lookupStartTicks);
        }
        if (!policyFound)
        {
            _perf.SectorPathSearches++;
            policy = new SectorCorridorPolicy
            {
                GoalSectorId = goalSectorId,
                GoalCellIndex = _world.GetIndex(goalX, goalY),
                GoalSectorDirtyVersion = _world.Sectors[goalSectorId].DirtyVersion,
                LastUsedFrame = GetFrameCount()
            };
            bool policyAuthorityMutationStarted = false;
            bool pathBuilt = IsPortalHierarchyQueryRequired(_world, startSectorId, goalSectorId)
                ? TryBuildPathHandleFromPortalHierarchyReversePolicy(
                    policy,
                    sectorPathKey,
                    startSectorId,
                    goalSectorId,
                    startX,
                    startY,
                    goalX,
                    goalY,
                    ref policyAuthorityMutationStarted,
                    out handle)
                : InitializeAndExpandL0SectorCorridorPolicy(
                    policy,
                    goalSectorId,
                    goalX,
                    goalY,
                    startSectorId,
                    startX,
                    startY);
            if (!pathBuilt)
                return false;
            SetSectorCorridorPolicy(policyKey, policy);
            PinMovingTargetSectorCorridorPolicy(pinnedAnchor, policyKey);
            TrimSectorCorridorPolicies();
        }
        else if (IsPortalHierarchyQueryRequired(_world, startSectorId, goalSectorId))
        {
            if (!ExpandHashedPortalHierarchyReversePolicyToStartCell(
                    policyKey,
                    policy,
                    sectorPathKey,
                    startSectorId,
                    goalSectorId,
                    startX,
                    startY,
                    goalX,
                    goalY,
                    out handle))
            {
                return false;
            }
        }
        else
        {
            if (policy.SearchState.OpenCount == 0 && policy.SearchState.CostCount == 0)
                InitializeL0SectorCorridorPolicy(policy, goalSectorId, goalX, goalY);
            if (!IsSectorCorridorPolicyStartCellCovered(policy, startSectorId, startX, startY)
                && !ExpandHashedSectorCorridorPolicyToStartCell(policyKey, policy, startSectorId, startX, startY))
            {
                return false;
            }
        }
        policy.LastUsedFrame = GetFrameCount();

        if (handle != null)
            return true;

        int bestStartNode = int.MinValue;
        long bestStartCost = long.MaxValue;
        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            int startNode = EncodePortalNode(startSectorId, portalId);
            if (!policy.SearchState.ContainsSettled(startNode)
                || !policy.SearchState.TryGetCost(startNode, out long downstreamCost))
                continue;
            long accessCost = ResolveDeterministicPortalAccessCost(startSector, startSectorId, portalId, startX, startY);
            long totalCost = AddDeterministicPortalCosts(accessCost, downstreamCost);
            if (totalCost >= bestStartCost)
                continue;
            bestStartCost = totalCost;
            bestStartNode = startNode;
        }
        if (bestStartNode == int.MinValue)
            return false;

        long reconstructStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        var nodes = new List<int>(16) { bestStartNode };
        int cursor = bestStartNode;
        int guard = 0;
        while (policy.SearchState.TryGetPrevious(cursor, out int nextNode))
        {
            nodes.Add(nextNode);
            cursor = nextNode;
            if (++guard > _world.Sectors.Length + _world.PortalsById.Count)
                throw new InvalidOperationException("TryBuildPathHandleFromSectorCorridorPolicy failed: reconstruction exceeded guard.");
        }

        if (!TryConvertPortalNodesToPath(nodes, startSectorId, goalSectorId, out List<int> sectorIds, out List<int> portalIds))
            throw new InvalidOperationException(
                $"TryBuildPathHandleFromSectorCorridorPolicy failed: settled policy did not reach goal startSector={startSectorId} goalSector={goalSectorId} startNode={bestStartNode}.");
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCorridorPolicyReconstruct,
                Stopwatch.GetTimestamp() - reconstructStartTicks);
        }

        handle = CreateAndCachePathHandle(sectorPathKey, sectorIds, portalIds, goalX, goalY);
        handle.BuildSource = "sectorCorridorPolicy";
        return true;
    }

    private static void CommitDeferredNavigationSyncPolicyAuthorities()
    {
        if (!_navigationSyncBatchResolveActive)
            throw new InvalidOperationException("NavigationSync policy authority commit requires an active batch transaction.");
        if (DeferredSectorCorridorPolicyAuthorityKeys.Count == 0)
            return;
        var keys = new List<SectorCorridorPolicyKey>(DeferredSectorCorridorPolicyAuthorityKeys);
        keys.Sort(CompareSectorCorridorPolicyKeys);
        for (int i = 0; i < keys.Count; i++)
            CommitDeferredSectorCorridorPolicyAuthority(keys[i]);
        DeferredSectorCorridorPolicyAuthorityKeys.Clear();
    }

    private static bool AreNavigationSyncRequestsInSameGoalPolicyGroup(
        NavigationSyncRequest left,
        NavigationSyncRequest right)
    {
        if (left == null || right == null)
            throw new InvalidOperationException("NavigationSync goal policy group contains a null request.");
        return left.AgentTypeId == right.AgentTypeId
               && left.MovingTargetId == right.MovingTargetId
               && left.InputGoalPosition.x.RawValue == right.InputGoalPosition.x.RawValue
               && left.InputGoalPosition.y.RawValue == right.InputGoalPosition.y.RawValue;
    }

    private static bool TryAcquirePinnedMovingTargetSectorCorridorPolicy(
        MovingTargetAnchor anchor,
        SectorCorridorPolicyKey requestedKey,
        int goalSectorId,
        int goalX,
        int goalY,
        out SectorCorridorPolicy policy)
    {
        if (anchor == null)
            throw new ArgumentNullException(nameof(anchor));
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (!anchor.HasPinnedSectorCorridorPolicy)
        {
            if (SectorCorridorPolicies.TryGetValue(requestedKey, out policy))
            {
                PinMovingTargetSectorCorridorPolicy(anchor, requestedKey);
                if (profile)
                {
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.FlowMovingTargetPolicyAnchorLookup,
                        Stopwatch.GetTimestamp() - phaseStartTicks);
                }
                return true;
            }
            policy = null;
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowMovingTargetPolicyAnchorLookup,
                    Stopwatch.GetTimestamp() - phaseStartTicks);
            }
            return false;
        }

        SectorCorridorPolicyKey pinnedKey = anchor.PinnedSectorCorridorPolicyKey;
        if (!SectorCorridorPolicies.TryGetValue(pinnedKey, out policy) || policy == null)
        {
            anchor.HasPinnedSectorCorridorPolicy = false;
            anchor.PinnedSectorCorridorPolicyKey = default;
            policy = null;
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowMovingTargetPolicyAnchorLookup,
                    Stopwatch.GetTimestamp() - phaseStartTicks);
            }
            return false;
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowMovingTargetPolicyAnchorLookup,
                Stopwatch.GetTimestamp() - phaseStartTicks);
        }
        if (pinnedKey.Equals(requestedKey))
            return true;
        if (!policy.HasAuthorityContentHash)
        {
            throw new InvalidOperationException(
                $"Moving-target corridor policy changed exact goal before its previous authority transaction committed target={anchor.Key.TargetId}, oldGoal={pinnedKey.GoalCellIndex}, newGoal={requestedKey.GoalCellIndex}.");
        }

        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        SectorCorridorPolicy detached = DetachSectorCorridorPolicy(pinnedKey);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowMovingTargetPolicyAnchorDetach,
                Stopwatch.GetTimestamp() - phaseStartTicks);
        }
        if (!ReferenceEquals(detached, policy))
            throw new InvalidOperationException("Moving-target corridor policy detach returned a different authority instance.");
        anchor.HasPinnedSectorCorridorPolicy = false;
        anchor.PinnedSectorCorridorPolicyKey = default;
        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool rebound = TryRebindSectorCorridorPolicyExactGoal(policy, goalSectorId, goalX, goalY);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowMovingTargetPolicyAnchorRebind,
                Stopwatch.GetTimestamp() - phaseStartTicks);
        }
        if (!rebound)
        {
            _perf.SectorCorridorExactGoalReplacements++;
            long disposeStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            policy.Dispose();
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowMovingTargetPolicyAnchorReplacementDispose,
                    Stopwatch.GetTimestamp() - disposeStartTicks);
            }
            policy = null;
            return false;
        }
        _perf.SectorCorridorExactGoalRebinds++;

        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        policy.GoalSectorId = goalSectorId;
        policy.GoalCellIndex = _world.GetIndex(goalX, goalY);
        policy.GoalSectorDirtyVersion = _world.Sectors[goalSectorId].DirtyVersion;
        policy.LastUsedFrame = GetFrameCount();
        SetSectorCorridorPolicy(requestedKey, policy);
        PinMovingTargetSectorCorridorPolicy(anchor, requestedKey);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowMovingTargetPolicyAnchorPublish,
                Stopwatch.GetTimestamp() - phaseStartTicks);
        }
        return true;
    }

    private static void PinMovingTargetSectorCorridorPolicy(
        MovingTargetAnchor anchor,
        SectorCorridorPolicyKey key)
    {
        if (anchor == null)
            return;
        anchor.HasPinnedSectorCorridorPolicy = true;
        anchor.PinnedSectorCorridorPolicyKey = key;
    }

    private static bool TryRebindSectorCorridorPolicyExactGoal(
        SectorCorridorPolicy policy,
        int goalSectorId,
        int goalX,
        int goalY)
    {
        if (policy == null)
            throw new ArgumentNullException(nameof(policy));
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long validationStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool hasHierarchyPolicies;
        try
        {
            if (policy.GoalSectorDirtyVersion != _world.Sectors[policy.GoalSectorId].DirtyVersion
                || _world.Sectors[goalSectorId].DirtyVersion != policy.GoalSectorDirtyVersion)
            {
                return false;
            }
            hasHierarchyPolicies = policy.HierarchyPolicies.Count > 0;
            if (!hasHierarchyPolicies && policy.GoalSectorId != goalSectorId)
                return false;
        }
        finally
        {
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowMovingTargetPolicyAnchorRebindSectorValidation,
                    Stopwatch.GetTimestamp() - validationStartTicks);
            }
        }
        if (hasHierarchyPolicies)
            return TryRebindPortalHierarchyReversePoliciesExactGoal(policy, goalSectorId, goalX, goalY);

        SectorData goalSector = _world.Sectors[goalSectorId];
        bool hasDelta = false;
        long delta = 0L;
        long accessStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        try
        {
            for (int i = 0; i < goalSector.PortalIds.Count; i++)
            {
                int portalId = goalSector.PortalIds[i];
                long previous = ResolveDeterministicGoalSectorPortalAccessCost(
                    goalSector,
                    goalSectorId,
                    portalId,
                    policy.GoalCellIndex % _world.Width,
                    policy.GoalCellIndex / _world.Width);
                long next = ResolveDeterministicGoalSectorPortalAccessCost(
                    goalSector,
                    goalSectorId,
                    portalId,
                    goalX,
                    goalY);
                if ((previous == long.MaxValue) != (next == long.MaxValue))
                    return false;
                if (previous == long.MaxValue)
                    continue;
                long candidateDelta = checked(next - previous);
                if (!hasDelta)
                {
                    delta = candidateDelta;
                    hasDelta = true;
                }
                else if (candidateDelta != delta)
                {
                    return false;
                }
            }
        }
        finally
        {
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowMovingTargetPolicyAnchorRebindSectorAccess,
                    Stopwatch.GetTimestamp() - accessStartTicks);
            }
        }
        if (!hasDelta)
            return false;
        _perf.NavigationPathPolicyShiftCalls++;
        _perf.NavigationPathPolicyShiftCostEntries = checked(
            _perf.NavigationPathPolicyShiftCostEntries + policy.SearchState.CostCount);
        _perf.NavigationPathPolicyShiftOpenEntries = checked(
            _perf.NavigationPathPolicyShiftOpenEntries + policy.SearchState.OpenCount);
        long shiftStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        policy.SearchState.ShiftCosts(delta);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowMovingTargetPolicyAnchorRebindSectorShift,
                Stopwatch.GetTimestamp() - shiftStartTicks);
        }
        return true;
    }

    private static bool InitializeAndExpandL0SectorCorridorPolicy(
        SectorCorridorPolicy policy,
        int goalSectorId,
        int goalX,
        int goalY,
        int startSectorId,
        int startX,
        int startY)
    {
        InitializeL0SectorCorridorPolicy(policy, goalSectorId, goalX, goalY);
        return ExpandSectorCorridorPolicyToStartCell(policy, startSectorId, startX, startY);
    }

    private static void InitializeL0SectorCorridorPolicy(
        SectorCorridorPolicy policy,
        int goalSectorId,
        int goalX,
        int goalY)
    {
        if (policy == null)
            throw new ArgumentNullException(nameof(policy));
        if (policy.SearchState.CostCount != 0 || policy.SearchState.PreviousCount != 0
            || policy.SearchState.SettledCount != 0 || policy.SearchState.OpenCount != 0)
        {
            throw new InvalidOperationException("InitializeL0SectorCorridorPolicy failed: L0 policy is already initialized.");
        }

        SectorData goalSector = _world.Sectors[goalSectorId];
        for (int i = 0; i < goalSector.PortalIds.Count; i++)
        {
            int portalId = goalSector.PortalIds[i];
            long goalCost = ResolveDeterministicGoalSectorPortalAccessCost(goalSector, goalSectorId, portalId, goalX, goalY);
            if (goalCost == long.MaxValue)
                continue;
            int goalNode = EncodePortalNode(goalSectorId, portalId);
            policy.SearchState.AddSource(goalNode, goalCost);
        }
        if (policy.SearchState.OpenCount == 0)
            throw new InvalidOperationException(
                $"InitializeL0SectorCorridorPolicy failed: goal sector has no reachable portal goalSector={goalSectorId} goal=({goalX},{goalY}).");
    }

    private static SectorCorridorPolicy CreateSectorCorridorPolicy(int goalSectorId, int goalX, int goalY)
    {
        var policy = new SectorCorridorPolicy { LastUsedFrame = GetFrameCount() };
        InitializeL0SectorCorridorPolicy(policy, goalSectorId, goalX, goalY);
        if (policy.SearchState.OpenCount > 0)
            return policy;
        policy.Dispose();
        return null;
    }

    private static bool ExpandSectorCorridorPolicyToStartCell(
        SectorCorridorPolicy policy,
        int startSectorId,
        int startX,
        int startY)
    {
        if (policy == null)
            throw new ArgumentNullException(nameof(policy));
        if (IsSectorCorridorPolicyStartCellCovered(policy, startSectorId, startX, startY))
            return true;

        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long queryStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        _perf.SectorCorridorPolicyQueries++;
        try
        {
            while (policy.SearchState.OpenCount > 0)
            {
                FlowPathKernelPopStatus popStatus = policy.SearchState.PopOne(out FlowPathKernelSearchEntry node);
                if (popStatus == FlowPathKernelPopStatus.CostMismatch
                    || popStatus == FlowPathKernelPopStatus.AlreadySettled
                    || popStatus == FlowPathKernelPopStatus.Stale)
                {
                    continue;
                }
                if (popStatus != FlowPathKernelPopStatus.Settled)
                {
                    throw new InvalidOperationException(
                        $"L0 corridor policy kernel pop failed status={popStatus}, node={node.Node}.");
                }
                int currentNode = node.Node;
                long currentCost = node.Cost;

                DecodePortalNode(currentNode, out int currentSectorId, out int currentPortalId);
                PortalData currentPortal = GetPortalById(_world, currentPortalId);
                int oppositeNode = EncodePortalNode(GetOppositeSectorId(currentPortal, currentSectorId), currentPortalId);
                AddSectorCorridorPolicyReverseEdge(policy, oppositeNode, currentNode,
                    AddDeterministicPortalCosts(currentCost, DeterministicPortalCrossingCost));

                List<PortalTransition> incomingTransitions = GetIncomingPortalTransitions(_world.Sectors[currentSectorId], currentPortalId);
                _perf.PathPortalGraphNodeExpansions++;
                if (incomingTransitions != null)
                {
                    _perf.PathPortalGraphOutgoingTransitionScans += incomingTransitions.Count;
                    for (int i = 0; i < incomingTransitions.Count; i++)
                    {
                        PortalTransition transition = incomingTransitions[i];
                        if (transition.ToPortalId != currentPortalId)
                            throw new InvalidOperationException(
                                $"ExpandSectorCorridorPolicyToStartCell failed: incoming index mismatch sector={currentSectorId} currentPortal={currentPortalId} from={transition.FromPortalId} to={transition.ToPortalId}.");
                        _perf.PathPortalGraphOutgoingTransitionHits++;
                        AddSectorCorridorPolicyReverseEdge(
                            policy,
                            EncodePortalNode(currentSectorId, transition.FromPortalId),
                            currentNode,
                            AddDeterministicPortalCosts(currentCost, transition.DeterministicCost));
                    }
                }

                if (IsSectorCorridorPolicyStartCellCovered(policy, startSectorId, startX, startY))
                    return true;
            }

            return IsSectorCorridorPolicyStartCellCovered(policy, startSectorId, startX, startY);
        }
        finally
        {
            if (profile)
                _perf.SectorCorridorPolicyTicks += Stopwatch.GetTimestamp() - queryStartTicks;
        }
    }

    private static bool IsSectorCorridorPolicyStartCellCovered(
        SectorCorridorPolicy policy,
        int startSectorId,
        int startX,
        int startY)
    {
        SectorData startSector = _world.Sectors[startSectorId];
        if (startSector.PortalIds.Count == 0)
            return false;

        bool hasAccessiblePortal = false;
        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            long accessCost = ResolveDeterministicPortalAccessCost(
                startSector,
                startSectorId,
                portalId,
                startX,
                startY);
            if (accessCost == long.MaxValue)
                continue;

            hasAccessiblePortal = true;
            int startNode = EncodePortalNode(startSectorId, portalId);
            if (!policy.SearchState.ContainsSettled(startNode)
                || !policy.SearchState.TryGetCost(startNode, out _))
            {
                return false;
            }
        }

        return hasAccessiblePortal;
    }

    private static void AddSectorCorridorPolicyReverseEdge(
        SectorCorridorPolicy policy,
        int predecessorNode,
        int nextNodeTowardGoal,
        long cost)
    {
        if (cost == long.MaxValue)
            return;
        policy.SearchState.Relax(nextNodeTowardGoal, predecessorNode, cost);
    }

    private static void TrimSectorCorridorPolicies()
    {
        int pinnedPolicyCount = ValidateAndCountPinnedSectorCorridorPolicies();
        int limit = Math.Max(Mathf.Max(32, Config.FlowTileCacheLimit / 2), pinnedPolicyCount);
        while (SectorCorridorPolicies.Count > limit)
        {
            SectorCorridorPolicyKey oldestKey = default;
            int oldestFrame = int.MaxValue;
            bool found = false;
            foreach (KeyValuePair<SectorCorridorPolicyKey, SectorCorridorPolicy> pair in SectorCorridorPolicies)
            {
                if (pair.Key.MovingTargetId != int.MinValue)
                    continue;
                if (pair.Value == null)
                    throw new InvalidOperationException("TrimSectorCorridorPolicies encountered a null policy.");
                if (IsSectorCorridorPolicyReferencedByPendingNavigationPathRequest(pair.Key, pair.Value))
                    continue;
                int frame = pair.Value?.LastUsedFrame ?? int.MinValue;
                if (found)
                {
                    int order = frame.CompareTo(oldestFrame);
                    if (order > 0 || (order == 0 && CompareSectorCorridorPolicyKeys(pair.Key, oldestKey) >= 0))
                        continue;
                }
                oldestKey = pair.Key;
                oldestFrame = frame;
                found = true;
            }
            if (!found)
                break;
            RemoveSectorCorridorPolicy(oldestKey);
        }
    }

    private static int ValidateAndCountPinnedSectorCorridorPolicies()
    {
        int pinnedPolicyCount = 0;
        foreach (KeyValuePair<SectorCorridorPolicyKey, SectorCorridorPolicy> pair in SectorCorridorPolicies)
        {
            SectorCorridorPolicyKey key = pair.Key;
            if (key.MovingTargetId == int.MinValue)
                continue;
            var anchorKey = new MovingTargetAnchorKey(key.MovingTargetId, key.AgentTypeId, key.SourceIslandId);
            if (!MovingTargetAnchors.TryGetValue(anchorKey, out MovingTargetAnchor anchor)
                || anchor == null
                || !anchor.HasPinnedSectorCorridorPolicy
                || !anchor.PinnedSectorCorridorPolicyKey.Equals(key))
            {
                throw new InvalidOperationException(
                    $"Moving-target corridor policy is not owned by its anchor target={key.MovingTargetId}, agentType={key.AgentTypeId}, island={key.SourceIslandId}, goal={key.GoalCellIndex}.");
            }
            pinnedPolicyCount++;
        }

        foreach (MovingTargetAnchor anchor in MovingTargetAnchors.Values)
        {
            if (anchor == null)
                throw new InvalidOperationException("Pinned corridor policy validation encountered a null moving-target anchor.");
            if (!anchor.HasPinnedSectorCorridorPolicy)
                continue;
            SectorCorridorPolicyKey key = anchor.PinnedSectorCorridorPolicyKey;
            if (key.MovingTargetId != anchor.Key.TargetId
                || key.AgentTypeId != anchor.Key.AgentTypeId
                || key.SourceIslandId != anchor.Key.IslandId
                || !SectorCorridorPolicies.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"Moving-target anchor owns an absent or mismatched corridor policy target={anchor.Key.TargetId}, agentType={anchor.Key.AgentTypeId}, island={anchor.Key.IslandId}.");
            }
        }
        return pinnedPolicyCount;
    }

    private static int CompareSectorCorridorPolicyKeys(SectorCorridorPolicyKey left, SectorCorridorPolicyKey right)
    {
        int order = left.WorldVersion.CompareTo(right.WorldVersion);
        if (order != 0) return order;
        order = left.AgentTypeId.CompareTo(right.AgentTypeId);
        if (order != 0) return order;
        order = left.MovingTargetId.CompareTo(right.MovingTargetId);
        if (order != 0) return order;
        order = left.SourceIslandId.CompareTo(right.SourceIslandId);
        if (order != 0) return order;
        order = left.GoalSectorId.CompareTo(right.GoalSectorId);
        if (order != 0) return order;
        order = left.GoalCellIndex.CompareTo(right.GoalCellIndex);
        return order != 0 ? order : left.GoalSectorDirtyVersion.CompareTo(right.GoalSectorDirtyVersion);
    }

    private static void ClearSectorCorridorPolicies()
    {
        ClearSectorCorridorPolicyCache();
        foreach (MovingTargetAnchor anchor in MovingTargetAnchors.Values)
        {
            if (anchor == null)
                throw new InvalidOperationException("ClearSectorCorridorPolicies encountered a null moving-target anchor.");
            anchor.HasPinnedSectorCorridorPolicy = false;
            anchor.PinnedSectorCorridorPolicyKey = default;
        }
    }

}
