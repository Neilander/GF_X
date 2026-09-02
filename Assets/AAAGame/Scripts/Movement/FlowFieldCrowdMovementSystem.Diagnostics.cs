using System;
using System.Collections.Generic;
using System.Text;
using Stopwatch = System.Diagnostics.Stopwatch;
using AAAGame.FlowPath;
using UnityEngine;
using MainThreadFrameProfiler = UnityGameFramework.Runtime.MainThreadFrameProfiler;
using MainThreadPerfScope = UnityGameFramework.Runtime.MainThreadPerfScope;

public static partial class FlowFieldCrowdMovementSystem
{
#if UNITY_EDITOR
    private static string FormatPortalHierarchyBuildProgress(PortalHierarchyBuildJob job)
    {
        if (job == null)
            return "hierarchy=none";

        PortalHierarchySourceBuildJob source = job.CurrentSourceBuild;
        string sourceProgress = source == null
            ? "source=none"
            : $"source={source.SourceNode},{source.Search.ExpansionCount},{source.RemainingTargets}," +
              $"{source.Search.Costs.Count},{source.Search.PreviousNode.Count},{source.Search.SettledNodes.Count}," +
              $"{source.OpenSet.Count},{source.OpenSet.AuthorityContentHash},{source.Complete}";
        return $"hierarchy={job.NextLevel},{job.NextClusterSpanSectors},{job.ClusterCursor},{job.SourceCursor}," +
               $"{job.CompletedLevels.Count},{job.Complete};{sourceProgress}";
    }

    public static bool IsEditorTestPendingRuntimeDirtyHierarchyStage()
    {
        RuntimeDirtyRebuildJob found = null;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state?.RuntimeDirtyJob == null)
                continue;
            if (found != null)
                throw new InvalidOperationException("Editor test requires at most one pending runtime-dirty job.");
            found = state.RuntimeDirtyJob;
        }
        return found?.Stage == RuntimeDirtyRebuildStage.Hierarchy;
    }

    public static void AdvanceEditorTestPendingRuntimeDirtyHierarchyProgressProbe()
    {
        RuntimeDirtyRebuildJob job = GetRequiredSingleEditorTestRuntimeDirtyJob();
        if (job.Stage != RuntimeDirtyRebuildStage.Hierarchy || job.HierarchyBuildJob == null)
            throw new InvalidOperationException($"Runtime-dirty hierarchy probe requires active hierarchy stage, actual={job.Stage}.");
        job.HierarchyBuildJob.SourceCursor++;
    }

    public static bool TryGetEditorTestPendingRuntimeDirtyHierarchySourceProgress(
        out int sourceNode,
        out int expansionCount,
        out int remainingTargets,
        out int openCount)
    {
        sourceNode = 0;
        expansionCount = 0;
        remainingTargets = 0;
        openCount = 0;
        RuntimeDirtyRebuildJob job = GetRequiredSingleEditorTestRuntimeDirtyJob();
        PortalHierarchySourceBuildJob source = job.HierarchyBuildJob?.CurrentSourceBuild;
        if (job.Stage != RuntimeDirtyRebuildStage.Hierarchy || source == null)
            return false;

        sourceNode = source.SourceNode;
        expansionCount = source.Search.ExpansionCount;
        remainingTargets = source.RemainingTargets;
        openCount = source.OpenSet.Count;
        return true;
    }

    public static void PerturbEditorTestPendingRuntimeDirtyHierarchySourceAuthorityState()
    {
        RuntimeDirtyRebuildJob job = GetRequiredSingleEditorTestRuntimeDirtyJob();
        PortalHierarchySourceBuildJob source = job.HierarchyBuildJob?.CurrentSourceBuild;
        if (job.Stage != RuntimeDirtyRebuildStage.Hierarchy || source == null)
        {
            throw new InvalidOperationException(
                $"Runtime-dirty hierarchy source authority probe requires an active source, actual={job.Stage}.");
        }

        int probeNode = int.MinValue + 101;
        source.Search.Costs.Add(probeNode, 101L);
        source.Search.PreviousNode.Add(probeNode, source.SourceNode);
        source.Search.SettledNodes.Add(probeNode);
        source.TargetNodes.Add(probeNode);
        source.OpenSet.Push(probeNode + 1, 102L);
    }

    public static int GetEditorTestPendingRuntimeDirtyPortalGraphAccessCount()
    {
        return GetRequiredSingleEditorTestRuntimeDirtyJob().PortalGraphAccessCount;
    }

    public static long GetEditorTestPendingRuntimeDirtyWorldHashProcessedTokenCount()
    {
        RuntimeDirtyRebuildJob found = GetRequiredSingleEditorTestRuntimeDirtyJob();
        if (found.Stage != RuntimeDirtyRebuildStage.WorldHash || found.WorldHashBuildJob == null)
        {
            throw new InvalidOperationException(
                $"GetEditorTestPendingRuntimeDirtyWorldHashProcessedTokenCount failed: current stage is {found.Stage}.");
        }

        return found.WorldHashBuildJob.ProcessedTokenCount;
    }

    public static bool HasEditorTestPendingRuntimeDirtyIslandComponentState()
    {
        RuntimeDirtyRebuildJob found = null;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state?.RuntimeDirtyJob == null)
                continue;
            if (found != null)
                throw new InvalidOperationException("HasEditorTestPendingRuntimeDirtyIslandComponentState failed: multiple jobs are pending.");
            found = state.RuntimeDirtyJob;
        }

        return found?.IslandParents != null && found.IslandComponentTotal > 0;
    }

    public static void PerturbEditorTestPendingRuntimeDirtyIslandComponentSize()
    {
        RuntimeDirtyRebuildJob found = null;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state?.RuntimeDirtyJob == null)
                continue;
            if (found != null)
                throw new InvalidOperationException("PerturbEditorTestPendingRuntimeDirtyIslandComponentSize failed: multiple jobs are pending.");
            found = state.RuntimeDirtyJob;
        }

        if (found?.IslandComponentSizes == null || found.IslandComponentTotal <= 0)
            throw new InvalidOperationException("PerturbEditorTestPendingRuntimeDirtyIslandComponentSize failed: component state is unavailable.");

        found.IslandComponentSizes[0] = checked(found.IslandComponentSizes[0] + 1);
    }

    public static int GetEditorTestPortalCount()
    {
        return _world != null && _world.Portals != null ? _world.Portals.Length : 0;
    }

    public static bool TryGetEditorTestFixedPortalOwner(
        int portalId,
        out int ownerDirection,
        out int ownerSinceFrame)
    {
        ownerDirection = 0;
        ownerSinceFrame = -1;
        if (_world == null)
            return false;

        return TryGetEditorTestFixedPortalOwner(
            _world.AgentTypeId,
            portalId,
            out ownerDirection,
            out ownerSinceFrame);
    }

    public static bool TryGetEditorTestFixedPortalOwner(
        int agentTypeId,
        int portalId,
        out int ownerDirection,
        out int ownerSinceFrame)
    {
        ownerDirection = 0;
        ownerSinceFrame = -1;
        int preferredAgentTypeId = ResolvePreferredAgentTypeId(agentTypeId);
        if (!WorldStates.TryGetValue(preferredAgentTypeId, out WorldRuntimeState worldState)
            || worldState?.World == null)
        {
            return false;
        }

        var key = new FixedPortalOwnerKey(worldState.World.Version, worldState.AgentTypeId, portalId);
        if (!FixedPortalOwners.TryGetValue(key, out FixedPortalOwnerState state) || state == null)
            return false;
        ownerDirection = state.OwnerDirection;
        ownerSinceFrame = state.OwnerSinceFrame;
        return true;
    }

    public static bool TryGetEditorTestFixedCorridorDescriptor(
        int cellX,
        int cellY,
        out int localBottleneckId,
        out int[] componentCellIndices,
        out int[] endpointACellIndices,
        out int[] endpointBCellIndices)
    {
        localBottleneckId = -1;
        componentCellIndices = null;
        endpointACellIndices = null;
        endpointBCellIndices = null;
        if (_world == null || !_world.IsWalkable(cellX, cellY))
            return false;

        FixedCorridorLookup lookup = GetOrCreateFixedCorridorLookup(_world);
        int cellIndex = _world.GetIndex(cellX, cellY);
        FixedCorridorResolution resolution = ResolveFixedCorridorDescriptor(_world, lookup, cellIndex, out FixedCorridorDescriptor descriptor);
        int guard = checked(_world.Width * _world.Height + 1);
        while (resolution == FixedCorridorResolution.Pending && guard-- > 0)
        {
            AdvanceFixedCorridorBuilds(_world, lookup, FixedCorridorBuildOperationQuota);
            resolution = ResolveFixedCorridorDescriptor(_world, lookup, cellIndex, out descriptor);
        }
        if (resolution == FixedCorridorResolution.Pending)
            throw new InvalidOperationException("Editor fixed corridor descriptor did not finish within the bounded cell-count guard.");
        if (resolution != FixedCorridorResolution.Ready)
            return false;

        localBottleneckId = descriptor.Key.LocalBottleneckId;
        var componentCells = new List<int>(descriptor.ComponentCellCount);
        foreach (KeyValuePair<int, int> pair in lookup.ComponentIdByCell)
        {
            if (pair.Value == descriptor.ComponentId)
                componentCells.Add(pair.Key);
        }
        componentCells.Sort();
        componentCellIndices = componentCells.ToArray();
        endpointACellIndices = (int[])descriptor.EndpointACellIndices.Clone();
        endpointBCellIndices = (int[])descriptor.EndpointBCellIndices.Clone();
        return true;
    }

    public static bool IsEditorTestFixedCorridorDescriptorPending(int cellX, int cellY)
    {
        if (_world == null || !_world.IsWalkable(cellX, cellY))
            return false;
        FixedCorridorLookup lookup = GetOrCreateFixedCorridorLookup(_world);
        return ResolveFixedCorridorDescriptor(_world, lookup, _world.GetIndex(cellX, cellY), out _)
               == FixedCorridorResolution.Pending;
    }

    public static string GetEditorTestFixedCorridorLookupDiagnostics(int cellX, int cellY, int radius)
    {
        if (_world == null)
            return "fixedCorridor=world-null";
        if (radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (!FixedCorridorLookupByWorldVersion.TryGetValue(_world.Version, out FixedCorridorLookup lookup) || lookup == null)
            return $"fixedCorridor=lookup-missing world={_world.Version}";

        var builder = new System.Text.StringBuilder(512);
        builder.Append("fixedCorridor={world=").Append(_world.Version)
            .Append(",evaluatedFrame=")
            .Append(FixedPortalOwnerEvaluatedFrameByWorld.TryGetValue(_world.Version, out int evaluatedFrame) ? evaluatedFrame : -1)
            .Append(",pendingStarts=").Append(lookup.PendingStartIndices.Count)
            .Append(",active=");
        if (lookup.ActiveBuildJob == null)
            builder.Append("none");
        else
            builder.Append(lookup.ActiveBuildJob.ComponentId).Append(':').Append(lookup.ActiveBuildJob.Head).Append('/').Append(lookup.ActiveBuildJob.Queue.Count);
        builder.Append(",pendingAgents=")
            .Append(PendingFixedCorridorParticipantAgentIdsByWorld.TryGetValue(_world.Version, out SortedSet<int> pendingAgents) ? pendingAgents.Count : 0)
            .Append(",cells=[");
        bool first = true;
        for (int y = Math.Max(0, cellY - radius); y <= Math.Min(_world.Height - 1, cellY + radius); y++)
        {
            for (int x = Math.Max(0, cellX - radius); x <= Math.Min(_world.Width - 1, cellX + radius); x++)
            {
                if (!_world.IsWalkable(x, y))
                    continue;
                int index = _world.GetIndex(x, y);
                if (!first)
                    builder.Append('|');
                first = false;
                builder.Append('(').Append(x).Append(',').Append(y).Append("):")
                    .Append(lookup.CandidateByCell.TryGetValue(index, out bool candidate) ? (candidate ? "candidate" : "noncandidate") : "unknown")
                    .Append(lookup.ComponentIdByCell.TryGetValue(index, out int componentId)
                        ? $":component={componentId}:completed={lookup.CompletedComponentIds.Contains(componentId)}"
                        : lookup.PendingStartIndices.Contains(index) ? ":pending-start" : string.Empty);
            }
        }
        builder.Append("]}");
        return builder.ToString();
    }

    public static int ProcessEditorTestFixedCorridorBuildStep()
    {
        if (_world == null)
            throw new InvalidOperationException("ProcessEditorTestFixedCorridorBuildStep failed: world is null.");
        return AdvanceFixedCorridorBuilds(
            _world,
            GetOrCreateFixedCorridorLookup(_world),
            FixedCorridorBuildOperationQuota);
    }

    public static void PerturbEditorTestOnlyFixedCorridorUnprocessedQueueOrder()
    {
        if (_world == null
            || !FixedCorridorLookupByWorldVersion.TryGetValue(_world.Version, out FixedCorridorLookup lookup)
            || lookup?.ActiveBuildJob == null)
        {
            throw new InvalidOperationException("Fixed-corridor queue-order probe requires an active build job.");
        }

        FixedCorridorBuildJob job = lookup.ActiveBuildJob;
        if (job.Head < 0 || job.Head + 1 >= job.Queue.Count)
        {
            throw new InvalidOperationException(
                $"Fixed-corridor queue-order probe requires at least two unprocessed cells. head={job.Head}, count={job.Queue.Count}.");
        }

        int first = job.Queue[job.Head];
        job.Queue[job.Head] = job.Queue[job.Head + 1];
        job.Queue[job.Head + 1] = first;
    }

    public static bool TryGetEditorTestFixedCorridorOwner(
        int localBottleneckId,
        out int ownerDirection,
        out int ownerSinceFrame)
    {
        ownerDirection = 0;
        ownerSinceFrame = -1;
        if (_world == null)
            return false;

        var key = new FixedPortalOwnerKey(_world.Version, _world.AgentTypeId, 2, localBottleneckId);
        if (!FixedPortalOwners.TryGetValue(key, out FixedPortalOwnerState state) || state == null)
            return false;
        ownerDirection = state.OwnerDirection;
        ownerSinceFrame = state.OwnerSinceFrame;
        return true;
    }

    public static bool HasEditorTestFixedCorridorLookup()
    {
        return _world != null && FixedCorridorLookupByWorldVersion.ContainsKey(_world.Version);
    }

    public static long GetEditorTestFixedCorridorClassifiedCellCount()
    {
        return _fixedCorridorClassifiedCellCount;
    }

    public static long GetEditorTestFixedCorridorExpandedCellCount()
    {
        return _fixedCorridorExpandedCellCount;
    }

    public static bool TryValidateEditorTestFixedCorridorIncrementalAuthorityHashes(out string failureReason)
    {
        failureReason = null;
        if (_world == null || !FixedCorridorLookupByWorldVersion.TryGetValue(_world.Version, out FixedCorridorLookup lookup))
            throw new InvalidOperationException("Fixed corridor incremental hash validation requires an active lookup.");

        ulong componentAssignments = 0;
        foreach (KeyValuePair<int, int> pair in lookup.ComponentIdByCell)
            componentAssignments ^= ComputeFixedCorridorComponentCellToken(pair.Value, pair.Key);
        if (componentAssignments != lookup.ComponentAssignmentContentHash)
        {
            failureReason = $"component assignment hash mismatch expected={componentAssignments} actual={lookup.ComponentAssignmentContentHash}";
            return false;
        }

        ulong completedComponents = 0;
        foreach (int componentId in lookup.CompletedComponentIds)
            completedComponents ^= ComputeFixedCorridorCellToken(componentId);
        if (completedComponents != lookup.CompletedComponentContentHash)
        {
            failureReason = $"completed component hash mismatch expected={completedComponents} actual={lookup.CompletedComponentContentHash}";
            return false;
        }

        ulong descriptors = 0;
        foreach (FixedCorridorDescriptor descriptor in lookup.DescriptorByComponentId.Values)
            descriptors ^= ComputeFixedCorridorDescriptorToken(descriptor);
        if (descriptors != lookup.DescriptorContentHash)
        {
            failureReason = $"descriptor hash mismatch expected={descriptors} actual={lookup.DescriptorContentHash}";
            return false;
        }

        ulong pendingStarts = 0;
        foreach (int startIndex in lookup.PendingStartIndices)
            pendingStarts ^= ComputeFixedCorridorCellToken(startIndex);
        if (pendingStarts != lookup.PendingStartContentHash)
        {
            failureReason = $"pending start hash mismatch expected={pendingStarts} actual={lookup.PendingStartContentHash}";
            return false;
        }

        FixedCorridorBuildJob job = lookup.ActiveBuildJob;
        if (job == null)
            return true;
        ulong componentCells = 0;
        for (int i = 0; i < job.Queue.Count; i++)
            componentCells ^= ComputeFixedCorridorCellToken(job.Queue[i]);
        if (componentCells != job.ComponentCellContentHash)
        {
            failureReason = $"active component hash mismatch expected={componentCells} actual={job.ComponentCellContentHash}";
            return false;
        }
        if (!TryValidateFixedCorridorEndpointHash(
                job.ExternalEndpointCells,
                job.ExternalEndpointContentHash,
                "external endpoint",
                out failureReason))
        {
            return false;
        }
        return TryValidateFixedCorridorEndpointHash(
            job.TerminalEndpointCells,
            job.TerminalEndpointContentHash,
            "terminal endpoint",
            out failureReason);
    }

    private static bool TryValidateFixedCorridorEndpointHash(
        HashSet<int> cells,
        ulong actualHash,
        string label,
        out string failureReason)
    {
        ulong expectedHash = 0;
        foreach (int cellIndex in cells)
            expectedHash ^= ComputeFixedCorridorCellToken(cellIndex);
        if (expectedHash == actualHash)
        {
            failureReason = null;
            return true;
        }
        failureReason = $"{label} hash mismatch expected={expectedHash} actual={actualHash}";
        return false;
    }

    public static bool GetEditorTestFixedGoalOccupancyParticipation(int agentId)
    {
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent) || agent == null)
            throw new InvalidOperationException($"GetEditorTestFixedGoalOccupancyParticipation failed: agent {agentId} is missing.");
        return ShouldUseAgentNavigationGoalAsOccupancyFixed(agent);
    }

    public static void SetEditorTestOnlyResolvedVelocityFrame(int agentId, int frame)
    {
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent) || agent?.NavState == null)
            throw new InvalidOperationException($"SetEditorTestOnlyResolvedVelocityFrame failed: agent {agentId} is missing.");
        agent.NavState.ResolvedVelocityFrame = frame;
    }

    public static void SetEditorTestOnlyFailedPathMemo(
        int agentId,
        int worldVersion,
        int startCellIndex,
        int goalCellIndex,
        int startSectorDirtyVersion,
        int goalSectorDirtyVersion)
    {
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent) || agent?.NavState == null)
            throw new InvalidOperationException($"SetEditorTestOnlyFailedPathMemo failed: agent {agentId} is missing.");

        AgentNavState nav = agent.NavState;
        nav.HasFailedPathRequest = true;
        nav.FailedPathWorldVersion = worldVersion;
        nav.FailedPathStartCellIndex = startCellIndex;
        nav.FailedPathGoalCellIndex = goalCellIndex;
        nav.FailedPathStartSectorDirtyVersion = startSectorDirtyVersion;
        nav.FailedPathGoalSectorDirtyVersion = goalSectorDirtyVersion;
    }

    public static void SetEditorTestOnlyLastFixedFlowFrame(int agentId, int frame)
    {
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent) || agent?.NavState == null)
            throw new InvalidOperationException($"SetEditorTestOnlyLastFixedFlowFrame failed: agent {agentId} is missing.");
        agent.NavState.LastFixedFlowFrame = frame;
    }

    public static void SetEditorTestOnlyPortalTraversalState(
        int agentId,
        int worldVersion,
        int sectorId,
        int portalId,
        int slotIndex,
        bool hasCommittedTileSlot = true)
    {
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent) || agent?.NavState == null)
            throw new InvalidOperationException($"SetEditorTestOnlyPortalTraversalState failed: agent {agentId} is missing.");

        AgentNavState nav = agent.NavState;
        nav.PortalTraversalWorldVersion = worldVersion;
        nav.PortalTraversalSectorId = sectorId;
        nav.PortalTraversalId = portalId;
        nav.PortalTraversalSlotIndex = slotIndex;
        nav.PortalTraversalHasCommittedTileSlot = hasCommittedTileSlot;
    }

    public static int ResolveEditorTestOnlyStablePortalTraversalSlotIndex(
        int agentId,
        int worldVersion,
        int sectorId,
        int portalId,
        int recommendedSlotIndex,
        int slotCount,
        bool recommendationFromCommittedTile = true)
    {
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent) || agent?.NavState == null)
            throw new InvalidOperationException($"ResolveEditorTestOnlyStablePortalTraversalSlotIndex failed: agent {agentId} is missing.");

        var key = new FlowTileCacheKey(
            worldVersion,
            sectorId,
            TileGoalKind.Portal,
            portalId,
            -1,
            agent.AgentTypeId,
            0);
        return ResolveStablePortalTraversalSlotIndex(
            agent.NavState,
            key,
            recommendedSlotIndex,
            slotCount,
            recommendationFromCommittedTile);
    }

    public static FixVector2 ResolveEditorTestOnlyPortalCrossingTargetFixed(
        FixVector2 position,
        FixVector2 oppositeCellCenter,
        bool isVerticalBoundary)
    {
        return ResolvePortalCrossingTargetFixed(position, oppositeCellCenter, isVerticalBoundary);
    }

    public static FixVector2 ResolveEditorTestOnlyPortalApproachTargetFixed(
        FixVector2 position,
        Vector2Int selectedCurrentCell,
        FixVector2 oppositeCellCenter,
        bool isVerticalBoundary)
    {
        if (_world == null)
            throw new InvalidOperationException("ResolveEditorTestOnlyPortalApproachTargetFixed failed: navigation world is unavailable.");
        return ResolvePortalApproachTargetFixed(
            position,
            selectedCurrentCell,
            oppositeCellCenter,
            isVerticalBoundary);
    }

    public static string GetEditorTestOnlyDirectLineDecisionDiagnostic(int agentId)
    {
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent) || agent?.NavState?.PathHandle == null)
            throw new InvalidOperationException($"GetEditorTestOnlyDirectLineDecisionDiagnostic failed: agent {agentId} has no path handle.");
        if (!TryEnsureWorldBuilt(ResolvePreferredAgentTypeId(agent.AgentTypeId)) || _world == null)
            throw new InvalidOperationException("GetEditorTestOnlyDirectLineDecisionDiagnostic failed: navigation world is unavailable.");

        PathHandle handle = agent.NavState.PathHandle;
        if (!_world.WorldToGridFixed(agent.PositionFixed, out int startX, out int startY))
            throw new InvalidOperationException($"GetEditorTestOnlyDirectLineDecisionDiagnostic failed: agent {agentId} is outside the navigation world.");
        return BuildLineDecisionSegmentDiagnostics(
            ToWorldVector3(agent.PositionFixed),
            agent.NavState.LastGoalWorld,
            startX,
            startY,
            handle.GoalX,
            handle.GoalY,
            agent.AgentTypeId)
               + "/fixedDda="
               + BuildFixedGridLineOfSightDecisionDiagnostic(
                   _world,
                   agent.PositionFixed,
                   agent.NavState.LastGoalWorldFixed,
                   allowTargetSoftCost: true);
    }

    public static bool HasEditorTestOnlyFixedGridLineOfSight(
        FixVector2 from,
        FixVector2 to,
        bool allowTargetSoftCost = false)
    {
        if (!TryEnsureWorldBuilt(ResolvePreferredAgentTypeId(0)) || _world == null)
            throw new InvalidOperationException("HasEditorTestOnlyFixedGridLineOfSight failed: navigation world is unavailable.");
        return HasFixedGridLineOfSight(_world, from, to, allowTargetSoftCost);
    }

    public static string GetEditorTestOnlyFixedGridLineOfSightDiagnostic(
        FixVector2 from,
        FixVector2 to,
        bool allowTargetSoftCost = false)
    {
        if (!TryEnsureWorldBuilt(ResolvePreferredAgentTypeId(0)) || _world == null)
            throw new InvalidOperationException("GetEditorTestOnlyFixedGridLineOfSightDiagnostic failed: navigation world is unavailable.");
        return BuildFixedGridLineOfSightDecisionDiagnostic(_world, from, to, allowTargetSoftCost);
    }

    public static bool HasEditorTestOnlyCellCenterGridLineOfSight(
        int fromX,
        int fromY,
        int toX,
        int toY,
        bool allowTargetSoftCost = false)
    {
        if (!TryEnsureWorldBuilt(ResolvePreferredAgentTypeId(0)) || _world == null)
            throw new InvalidOperationException("HasEditorTestOnlyCellCenterGridLineOfSight failed: navigation world is unavailable.");
        return HasGridLineOfSight(_world, fromX, fromY, toX, toY, allowTargetSoftCost);
    }

    public static bool AreEditorTestOnlyFlowTileKeysEqual(
        int[] firstSectorIds,
        int[] firstPortalIds,
        int[] secondSectorIds,
        int[] secondPortalIds,
        int sectorPathIndex,
        int goalX,
        int goalY)
    {
        if (!TryEnsureWorldBuilt(ResolvePreferredAgentTypeId(0)))
            throw new InvalidOperationException("AreEditorTestOnlyFlowTileKeysEqual failed: navigation world is unavailable.");
        var firstHandle = new PathHandle
        {
            WorldVersion = _world.Version,
            GoalX = goalX,
            GoalY = goalY,
            SectorIds = firstSectorIds,
            PortalIds = firstPortalIds,
            CurrentSectorIndex = sectorPathIndex
        };
        var secondHandle = new PathHandle
        {
            WorldVersion = _world.Version,
            GoalX = goalX,
            GoalY = goalY,
            SectorIds = secondSectorIds,
            PortalIds = secondPortalIds,
            CurrentSectorIndex = sectorPathIndex
        };
        int agentTypeId = ResolvePreferredAgentTypeId(0);
        FlowTileCacheKey firstKey = CreateTileCacheKeyForPathSegment(
            firstHandle,
            sectorPathIndex,
            goalX,
            goalY,
            agentTypeId,
            out _,
            out _);
        FlowTileCacheKey secondKey = CreateTileCacheKeyForPathSegment(
            secondHandle,
            sectorPathIndex,
            goalX,
            goalY,
            agentTypeId,
            out _,
            out _);
        return firstKey.Equals(secondKey);
    }

    public static bool AreEditorTestOnlyPortalWindowTileKeysEqualForGoals(
        int[] sectorIds,
        int[] portalIds,
        int sectorPathIndex,
        int firstGoalX,
        int firstGoalY,
        int secondGoalX,
        int secondGoalY)
    {
        if (!TryEnsureWorldBuilt(ResolvePreferredAgentTypeId(0)))
            throw new InvalidOperationException("AreEditorTestOnlyPortalWindowTileKeysEqualForGoals failed: navigation world is unavailable.");
        if (sectorIds == null || portalIds == null || sectorPathIndex < 0 || sectorPathIndex >= portalIds.Length)
            throw new ArgumentOutOfRangeException(nameof(sectorPathIndex));

        int agentTypeId = ResolvePreferredAgentTypeId(0);
        var firstHandle = new PathHandle
        {
            WorldVersion = _world.Version,
            GoalX = firstGoalX,
            GoalY = firstGoalY,
            SectorIds = (int[])sectorIds.Clone(),
            PortalIds = (int[])portalIds.Clone(),
            CurrentSectorIndex = sectorPathIndex
        };
        var secondHandle = new PathHandle
        {
            WorldVersion = _world.Version,
            GoalX = secondGoalX,
            GoalY = secondGoalY,
            SectorIds = (int[])sectorIds.Clone(),
            PortalIds = (int[])portalIds.Clone(),
            CurrentSectorIndex = sectorPathIndex
        };
        FlowTileCacheKey firstKey = CreateTileCacheKeyForPathSegment(
            firstHandle, sectorPathIndex, firstGoalX, firstGoalY, agentTypeId, out TileGoalKind firstKind, out _);
        FlowTileCacheKey secondKey = CreateTileCacheKeyForPathSegment(
            secondHandle, sectorPathIndex, secondGoalX, secondGoalY, agentTypeId, out TileGoalKind secondKind, out _);
        if (firstKind != TileGoalKind.Portal || secondKind != TileGoalKind.Portal)
            throw new InvalidOperationException(
                $"Portal-window key probe resolved a final tile first={firstKind}, second={secondKind}, pathIndex={sectorPathIndex}.");
        return firstKey.Equals(secondKey);
    }

    public static void ValidateEditorTestOnlyFlowTileKeySourceRebind(
        int[] firstSectorIds,
        int[] firstPortalIds,
        int[] secondSectorIds,
        int[] secondPortalIds,
        int sectorPathIndex,
        int goalX,
        int goalY,
        out bool changedFromFirst,
        out bool matchesFreshSecond)
    {
        if (!TryEnsureWorldBuilt(ResolvePreferredAgentTypeId(0)))
            throw new InvalidOperationException("ValidateEditorTestOnlyFlowTileKeySourceRebind failed: navigation world is unavailable.");
        int agentTypeId = ResolvePreferredAgentTypeId(0);
        var reboundHandle = new PathHandle
        {
            WorldVersion = _world.Version,
            GoalX = goalX,
            GoalY = goalY,
            SectorIds = firstSectorIds,
            PortalIds = firstPortalIds,
            CurrentSectorIndex = sectorPathIndex
        };
        FlowTileCacheKey firstKey = CreateTileCacheKeyForPathSegment(
            reboundHandle, sectorPathIndex, goalX, goalY, agentTypeId, out _, out _);
        reboundHandle.SectorIds = secondSectorIds;
        reboundHandle.PortalIds = secondPortalIds;
        FlowTileCacheKey reboundKey = CreateTileCacheKeyForPathSegment(
            reboundHandle, sectorPathIndex, goalX, goalY, agentTypeId, out _, out _);
        var freshHandle = new PathHandle
        {
            WorldVersion = _world.Version,
            GoalX = goalX,
            GoalY = goalY,
            SectorIds = (int[])secondSectorIds.Clone(),
            PortalIds = (int[])secondPortalIds.Clone(),
            CurrentSectorIndex = sectorPathIndex
        };
        FlowTileCacheKey freshKey = CreateTileCacheKeyForPathSegment(
            freshHandle, sectorPathIndex, goalX, goalY, agentTypeId, out _, out _);
        changedFromFirst = !firstKey.Equals(reboundKey);
        matchesFreshSecond = reboundKey.Equals(freshKey);
    }

    public static void ClearEditorTestFixedPortalOwners()
    {
        FixedPortalOwners.Clear();
        FixedPortalOwnerEvaluatedFrameByWorld.Clear();
    }

    public static bool SetEditorTestPathCurrentSectorIndex(int agentId, int currentSectorIndex)
    {
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent) || agent.NavState.PathHandle == null)
            return false;
        if (currentSectorIndex < 0 || currentSectorIndex >= agent.NavState.PathHandle.SectorIds.Length)
            throw new ArgumentOutOfRangeException(nameof(currentSectorIndex));

        agent.NavState.PathHandle.CurrentSectorIndex = currentSectorIndex;
        return true;
    }

    public static bool TryGetEditorTestDeterministicFlowDiagnostic(int agentId, out string diagnostic)
    {
        diagnostic = "unavailable";
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            return false;

        int preferredAgentTypeId = ResolvePreferredAgentTypeId(agent.AgentTypeId);
        if (!WorldStates.TryGetValue(preferredAgentTypeId, out WorldRuntimeState state)
            || state == null
            || state.World == null)
        {
            diagnostic = $"world=missing/agentType={preferredAgentTypeId}";
            return true;
        }
        if (state.IsDirty || state.BuildJob != null)
        {
            diagnostic = $"world=not-ready/agentType={preferredAgentTypeId}";
            return true;
        }

        NavigationWorld previousWorld = _world;
        WorldRuntimeState previousActiveWorldState = _activeWorldState;
        try
        {
            _world = state.World;
            _activeWorldState = state;
            return TryBuildEditorTestDeterministicFlowDiagnostic(agent, out diagnostic);
        }
        finally
        {
            _world = previousWorld;
            _activeWorldState = previousActiveWorldState;
        }
    }

    public static string GetEditorTestAgentMotionDiagnostics(int agentId)
    {
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent) || agent?.NavState == null)
            throw new InvalidOperationException($"GetEditorTestAgentMotionDiagnostics failed: agent {agentId} is missing.");

        AgentNavState nav = agent.NavState;
        PathHandle handle = nav.PathHandle;
        if (!TryGetEditorTestDeterministicFlowDiagnostic(agentId, out string flowDiagnostic))
            throw new InvalidOperationException($"GetEditorTestAgentMotionDiagnostics failed: agent {agentId} has no flow diagnostic.");

        return $"positionRaw=({agent.PositionFixed.x.RawValue},{agent.PositionFixed.y.RawValue})" +
               $"/desired=({nav.DesiredVelocity.x:F4},{nav.DesiredVelocity.z:F4})" +
               $"/resolved=({nav.ResolvedVelocity.x:F4},{nav.ResolvedVelocity.z:F4})@{nav.ResolvedVelocityFrame}" +
               $"/pending={nav.HasPendingNavigation}/replacement={nav.HasPendingNavigationReplacement}" +
               $"/handle={(handle != null ? handle.HandleId.ToString() : "null")}" +
               $"/handleWorld={(handle != null ? handle.WorldVersion.ToString() : "null")}" +
               $"/pathIndex={(handle != null ? handle.CurrentSectorIndex.ToString() : "null")}" +
               $"/currentSector={nav.CurrentSectorId}/target={nav.StableGoalTargetId}" +
               $"/flow=[{flowDiagnostic}]";
    }

    private static bool TryBuildEditorTestDeterministicFlowDiagnostic(
        AgentRuntimeData agent,
        out string diagnostic)
    {
        diagnostic = "unavailable";

        PathHandle handle = agent.NavState.PathHandle;
        if (handle == null || handle.SectorIds == null || handle.SectorIds.Length == 0)
        {
            diagnostic = "handle=null";
            return true;
        }

        int sectorPathIndex = handle.CurrentSectorIndex;
        if (sectorPathIndex < 0 || sectorPathIndex >= handle.SectorIds.Length)
        {
            diagnostic = $"pathIndex={sectorPathIndex}/pathLength={handle.SectorIds.Length}/invalid";
            return true;
        }
        if (handle.WorldVersion != _world.Version)
        {
            diagnostic = $"handleWorld={handle.WorldVersion}/activeWorld={_world.Version}/mismatch";
            return true;
        }

        FlowTileCacheKey key = CreateTileCacheKeyForPathSegment(
            handle,
            sectorPathIndex,
            handle.GoalX,
            handle.GoalY,
            agent.AgentTypeId,
            out TileGoalKind goalKind,
            out int downstreamPortalId);
        bool cached = DeterministicFlowTileCache.TryGetValue(key, out FlowTileCacheEntry tile);
        if (!cached)
        {
            bool queued = TryGetFlowTileJobQueueState(
                key,
                out int queueIndex,
                out string queueStage,
                out bool waitingForDependency,
                out bool stale);
            diagnostic =
                $"handleWorld={handle.WorldVersion}/activeWorld={_world.Version}" +
                $"/pathIndex={sectorPathIndex}/pathSector={handle.SectorIds[sectorPathIndex]}/navSector={agent.NavState.CurrentSectorId}" +
                $"/goalKind={goalKind}/portal={downstreamPortalId}/cached=false/key={FormatTileKey(key)}" +
                $"/queued={queued}/queueIndex={queueIndex}/queueStage={queueStage}/waitingForDependency={waitingForDependency}/stale={stale}" +
                $"/queueCount={FlowTileBuildQueue.Count}/queueHead={BuildPendingFlowTileJobDiagnostics()}" +
                $"/lastFrame={agent.NavState.LastFixedFlowFrame}/lastResult={agent.NavState.LastFixedFlowResult}" +
                $"/lastVelocityRaw=({agent.NavState.LastFixedFlowVelocity.x.RawValue},{agent.NavState.LastFixedFlowVelocity.y.RawValue})";
            return true;
        }

        bool inGrid = _world.WorldToGridFixed(agent.PositionFixed, out int worldX, out int worldY);
        bool insideTile = inGrid && IsInsideSector(tile, worldX, worldY);
        int integrationCost = int.MaxValue;
        byte directionIndex = 0;
        string neighborhood = "outside";
        if (insideTile)
        {
            int localIndex = tile.GetLocalIndex(worldX, worldY);
            integrationCost = GetDeterministicIntegrationCost(tile, localIndex);
            directionIndex = tile.DeterministicFlowDirectionIndices[localIndex];
            neighborhood = BuildDeterministicFlowNeighborhoodDiagnostic(tile, worldX, worldY);
        }

        diagnostic =
            $"handleWorld={handle.WorldVersion}/activeWorld={_world.Version}" +
            $"/pathIndex={sectorPathIndex}/pathSector={handle.SectorIds[sectorPathIndex]}/navSector={agent.NavState.CurrentSectorId}" +
            $"/goalKind={goalKind}/portal={downstreamPortalId}/cached={cached}/key={FormatTileKey(key)}" +
            $"/positionRaw=({agent.PositionFixed.x.RawValue},{agent.PositionFixed.y.RawValue})/cell=({worldX},{worldY})/inGrid={inGrid}/insideTile={insideTile}" +
            $"/tile=({tile.StartX},{tile.StartY},{tile.Width},{tile.Height})" +
            $"/fixedCost={(integrationCost == int.MaxValue ? "INF" : integrationCost.ToString())}/fixedDirection={directionIndex}" +
            $"/neighbors={neighborhood}" +
            $"/lastFrame={agent.NavState.LastFixedFlowFrame}/lastResult={agent.NavState.LastFixedFlowResult}" +
            $"/lastVelocityRaw=({agent.NavState.LastFixedFlowVelocity.x.RawValue},{agent.NavState.LastFixedFlowVelocity.y.RawValue})";
        return true;
    }

    private static string BuildDeterministicFlowNeighborhoodDiagnostic(
        FlowTileCacheEntry tile,
        int worldX,
        int worldY)
    {
        var builder = new System.Text.StringBuilder(256);
        builder.Append('[');
        bool hasCell = false;
        for (int y = worldY - 1; y <= worldY + 1; y++)
        {
            for (int x = worldX - 1; x <= worldX + 1; x++)
            {
                if (!IsInsideSector(tile, x, y))
                    continue;
                if (hasCell)
                    builder.Append('|');
                hasCell = true;
                int localIndex = tile.GetLocalIndex(x, y);
                int cost = GetDeterministicIntegrationCost(tile, localIndex);
                builder.Append('(').Append(x).Append(',').Append(y).Append(")=")
                    .Append(cost == int.MaxValue ? "INF" : cost.ToString())
                    .Append(':').Append(tile.DeterministicFlowDirectionIndices[localIndex]);
            }
        }
        if (!hasCell)
            builder.Append("none");
        return builder.Append(']').ToString();
    }

    public static int GetEditorTestFrameTileBuildCount()
    {
        return 0;
    }

    public static int GetEditorTestRequiredFlowTileCommitCount()
    {
        return _perf.RequiredFlowTileCommits;
    }

    public static int GetEditorTestFlowTileQueueMutationCount()
    {
        return _perf.FlowTileQueueEnqueued + _perf.FlowTileQueuePruned;
    }

    public static int GetEditorTestFlowTileReferenceRefreshCount()
    {
        return _perf.FlowTileReferenceRefreshCalls;
    }

    public static int GetEditorTestFrameSynchronousSectorIntegrationCount()
    {
        return 0;
    }

    public static int GetEditorTestFramePortalAccessFieldIntegrationCount()
    {
        return 0;
    }

    public static int GetEditorTestFrameSharedGoalFieldBuildCount()
    {
        return _perf.SharedGoalFieldBuilds;
    }

    public static int GetEditorTestFramePortalGraphMergeHitCount()
    {
        return _perf.PortalGraphMergeHits;
    }

    public static int GetEditorTestFrameSectorPathSearchCount()
    {
        return _perf.SectorPathSearches;
    }

    public static string GetEditorTestFramePathSearchDiagnostics()
    {
        return $"logicFrame={_perf.Frame},sectorSearches={_perf.SectorPathSearches}," +
               $"portalNodes={_perf.PathPortalGraphNodeExpansions},transitionScans={_perf.PathPortalGraphOutgoingTransitionScans}," +
               $"transitionHits={_perf.PathPortalGraphOutgoingTransitionHits},stableInitial={_perf.StableGoalRefreshInitial}," +
               $"stableCell={_perf.StableGoalRefreshCellDelta},policyQueries={_perf.SectorCorridorPolicyQueries}," +
               $"policyRebinds={_perf.SectorCorridorExactGoalRebinds},policyReplacements={_perf.SectorCorridorExactGoalReplacements}," +
               $"policyMs={TicksToMs(_perf.SectorCorridorPolicyTicks):F3},policyCount={SectorCorridorPolicies.Count}," +
               $"startConnectorHits={_perf.HierarchyStartConnectorCacheHits},startConnectorMisses={_perf.HierarchyStartConnectorCacheMisses}," +
               $"downwardHits={_perf.HierarchyDownwardCustomizationCacheHits},downwardMisses={_perf.HierarchyDownwardCustomizationCacheMisses}," +
               $"downwardExpansions={_perf.HierarchyDownwardCustomizationExpansions}," +
               $"witnessHits={_perf.HierarchyL0WitnessCacheHits},witnessMisses={_perf.HierarchyL0WitnessCacheMisses}";
    }

    public static string GetEditorTestFrameNavigationPathStageDiagnostics()
    {
        return $"initialize={TicksToMs(_perf.NavigationPathInitializeTicks):F3}ms/{_perf.NavigationPathInitializeOperations}," +
               $"goalConnector={TicksToMs(_perf.NavigationPathGoalConnectorTicks):F3}ms/{_perf.NavigationPathGoalConnectorOperations}," +
               $"createHierarchy={TicksToMs(_perf.NavigationPathCreateHierarchyTicks):F3}ms/{_perf.NavigationPathCreateHierarchyOperations}," +
               $"expandHierarchy={TicksToMs(_perf.NavigationPathExpandHierarchyTicks):F3}ms/{_perf.NavigationPathExpandHierarchyOperations}," +
               $"downward={TicksToMs(_perf.NavigationPathDownwardTicks):F3}ms/{_perf.NavigationPathDownwardOperations}," +
               $"l0={TicksToMs(_perf.NavigationPathL0Ticks):F3}ms/{_perf.NavigationPathL0Operations}," +
               $"materialize={TicksToMs(_perf.NavigationPathMaterializeTicks):F3}ms/{_perf.NavigationPathMaterializeOperations}," +
               $"materializeStages=(initialize={TicksToMs(_perf.NavigationPathMaterializeInitializeTicks):F3}ms/{_perf.NavigationPathMaterializeInitializeOperations}," +
               $"startPortal={TicksToMs(_perf.NavigationPathMaterializeStartPortalTicks):F3}ms/{_perf.NavigationPathMaterializeStartPortalOperations}," +
               $"downward={TicksToMs(_perf.NavigationPathMaterializeDownwardTicks):F3}ms/{_perf.NavigationPathMaterializeDownwardOperations}," +
               $"policy={TicksToMs(_perf.NavigationPathMaterializePolicyTicks):F3}ms/{_perf.NavigationPathMaterializePolicyOperations}," +
               $"goalConnector={TicksToMs(_perf.NavigationPathMaterializeGoalConnectorTicks):F3}ms/{_perf.NavigationPathMaterializeGoalConnectorOperations}," +
               $"conversion={TicksToMs(_perf.NavigationPathMaterializeConversionTicks):F3}ms/{_perf.NavigationPathMaterializeConversionOperations}," +
               $"immutableCopy={TicksToMs(_perf.NavigationPathMaterializeImmutableCopyTicks):F3}ms/{_perf.NavigationPathMaterializeImmutableCopyOperations}," +
               $"hash={TicksToMs(_perf.NavigationPathMaterializeHashTicks):F3}ms/{_perf.NavigationPathMaterializeHashOperations}," +
               $"publish={TicksToMs(_perf.NavigationPathMaterializePublishTicks):F3}ms/{_perf.NavigationPathMaterializePublishOperations}," +
               $"localBinding={TicksToMs(_perf.NavigationPathLocalBindingTicks):F3}ms/{_perf.NavigationPathLocalBindingOperations})," +
               $"complete={TicksToMs(_perf.NavigationPathCompleteTicks):F3}ms/{_perf.NavigationPathCompleteOperations}";
    }

    public static string GetEditorTestFrameNavigationSyncStageDiagnostics()
    {
        return $"agentUpdate={TicksToMs(_perf.AgentUpdateTicks):F3}ms," +
               $"demandResolve={TicksToMs(_perf.NavigationDemandResolutionTicks):F3}ms/{_perf.NavigationDemandResolutionCount}," +
               $"demandDispatch={TicksToMs(_perf.NavigationDemandDispatchTicks):F3}ms/{_perf.NavigationDemandDispatchCount}," +
               $"pathQueue={TicksToMs(_perf.NavigationPathRequestQueueTicks):F3}ms," +
               $"flowQueue={TicksToMs(_perf.FlowTileQueueTicks):F3}ms," +
               $"portalOwner={TicksToMs(_perf.FixedPortalOwnerSnapshotTicks):F3}ms," +
               $"corridorBuild={TicksToMs(_perf.FixedCorridorBuildTicks):F3}ms/{_perf.FixedCorridorBuildOperations}," +
               $"participantRefresh={TicksToMs(_perf.FixedPortalParticipantRefreshTicks):F3}ms," +
               $"ownerResolve={TicksToMs(_perf.FixedPortalOwnerResolveTicks):F3}ms";
    }

    public static string GetEditorTestFrameCombatApproachDiagnostics()
    {
        return $"calls={_perf.CombatApproachCalls},cacheHits={_perf.CombatApproachSlotCacheHits}," +
               $"cacheBuilds={_perf.CombatApproachSlotCacheBuilds},scored={_perf.CombatApproachScoredCandidates}," +
               $"sameIsland={_perf.CombatApproachSameIslandCandidates},expandedCalls={_perf.CombatApproachExpandedCalls}," +
               $"expandedCandidates={_perf.CombatApproachExpandedCandidates},successes={_perf.CombatApproachSuccesses}," +
               $"noSlotFailures={_perf.CombatApproachNoSlotFailures}";
    }

    public static int GetEditorTestFramePathPortalGraphNodeExpansionCount()
    {
        return _perf.PathPortalGraphNodeExpansions;
    }

    public static int GetEditorTestFrameSectorCorridorPolicyQueryCount()
    {
        return _perf.SectorCorridorPolicyQueries;
    }

    public static int GetEditorTestFrameSectorCorridorPolicyAuthorityHashRefreshCount()
    {
        return _perf.SectorCorridorPolicyAuthorityHashRefreshes;
    }

    public static int GetEditorTestFrameSectorCorridorGoalConnectorBuildCount()
    {
        return _perf.SectorCorridorGoalConnectorBuilds;
    }

    public static int GetEditorTestFrameSectorCorridorExactGoalRebindCount()
    {
        return _perf.SectorCorridorExactGoalRebinds;
    }

    public static int GetEditorTestFrameSectorCorridorExactGoalReplacementCount()
    {
        return _perf.SectorCorridorExactGoalReplacements;
    }

    public static int GetEditorTestFrameHierarchyL0WitnessCacheHitCount()
    {
        return _perf.HierarchyL0WitnessCacheHits;
    }

    public static int GetEditorTestFrameHierarchyL0WitnessCacheMissCount()
    {
        return _perf.HierarchyL0WitnessCacheMisses;
    }

    public static int GetEditorTestFrameNavigationPrepareRequestCount()
    {
        return _perf.NavigationPrepareRequests;
    }

    public static void ResetEditorTestNavigationPrepareRequestCount()
    {
        _testNavigationPrepareRequestCount = 0;
    }

    public static int GetEditorTestNavigationPrepareRequestCount()
    {
        return _testNavigationPrepareRequestCount;
    }

    public static void ResetEditorTestFixedPortalParticipationUpdateCount()
    {
        _testFixedPortalParticipationUpdateCount = 0;
    }

    public static int GetEditorTestFixedPortalParticipationUpdateCount()
    {
        return _testFixedPortalParticipationUpdateCount;
    }

    public static int GetEditorTestPortalHierarchyMaximumSourceExpansions()
    {
        return _editorPortalHierarchyMaximumSourceExpansions;
    }

    public static double GetEditorTestPortalHierarchyMaximumSourceMilliseconds()
    {
        return _editorPortalHierarchyMaximumSourceTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    public static int GetEditorTestPortalHierarchyMaximumInvocationExpansions()
    {
        return _editorPortalHierarchyMaximumInvocationExpansions;
    }

    public static double GetEditorTestPortalHierarchyMaximumInvocationMilliseconds()
    {
        return _editorPortalHierarchyMaximumInvocationTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    public static void ResetEditorTestPortalHierarchyBuildMeasurements()
    {
        _editorPortalHierarchyMaximumSourceExpansions = 0;
        _editorPortalHierarchyMaximumSourceTicks = 0;
        _editorPortalHierarchyMaximumInvocationExpansions = 0;
        _editorPortalHierarchyMaximumInvocationTicks = 0;
    }

    public static int GetEditorTestSectorCorridorPolicyCount()
    {
        return SectorCorridorPolicies.Count;
    }

    public static void TrimEditorTestSectorCorridorPolicies()
    {
        TrimSectorCorridorPolicies();
    }

    public static bool TryGetEditorTestPathPortalIds(int entityId, out int[] portalIds)
    {
        portalIds = null;
        if (!Agents.TryGetValue(entityId, out AgentRuntimeData agent))
            return false;

        PathHandle handle = agent.NavState.PathHandle;
        if (handle?.PortalIds == null)
            return false;

        portalIds = (int[])handle.PortalIds.Clone();
        return true;
    }

    public static bool TryGetEditorTestPathSectorIds(int entityId, out int[] sectorIds)
    {
        sectorIds = null;
        if (!Agents.TryGetValue(entityId, out AgentRuntimeData agent))
            return false;

        PathHandle handle = agent.NavState.PathHandle;
        if (handle?.SectorIds == null)
            return false;

        sectorIds = (int[])handle.SectorIds.Clone();
        return true;
    }

    public static bool TryGetEditorTestPathRouteSliceCounts(
        int entityId,
        out int sectorSliceCount,
        out int portalSliceCount)
    {
        sectorSliceCount = 0;
        portalSliceCount = 0;
        if (!Agents.TryGetValue(entityId, out AgentRuntimeData agent))
            return false;

        PathHandle handle = agent.NavState.PathHandle;
        if (handle?.SectorIds == null || handle.PortalIds == null)
            return false;

        sectorSliceCount = handle.SectorIds.SliceCount;
        portalSliceCount = handle.PortalIds.SliceCount;
        return true;
    }

    public static bool TryGetEditorTestCurrentPathSegment(
        int entityId,
        out int sectorPathIndex,
        out int downstreamPortalId,
        out bool isOnPortalCell)
    {
        sectorPathIndex = -1;
        downstreamPortalId = -1;
        isOnPortalCell = false;
        if (!Agents.TryGetValue(entityId, out AgentRuntimeData agent))
            return false;

        PathHandle handle = agent.NavState.PathHandle;
        if (handle?.SectorIds == null || handle.CurrentSectorIndex < 0 || handle.CurrentSectorIndex >= handle.SectorIds.Length)
            return false;

        sectorPathIndex = handle.CurrentSectorIndex;
        downstreamPortalId = ResolveCurrentDownstreamPortalId(handle);
        if (downstreamPortalId < 0)
            return true;

        PortalData portal = GetPortalById(_world, downstreamPortalId);
        Vector2Int[] portalCells = GetPortalCellsForSector(portal, agent.NavState.CurrentSectorId);
        for (int i = 0; i < portalCells.Length; i++)
        {
            if (portalCells[i] == agent.NavState.CurrentCell)
            {
                isOnPortalCell = true;
                break;
            }
        }
        return true;
    }

    public static bool TryGetEditorTestPathGoalCell(int entityId, out int goalX, out int goalY)
    {
        goalX = -1;
        goalY = -1;
        if (!Agents.TryGetValue(entityId, out AgentRuntimeData agent) || agent.NavState.PathHandle == null)
            return false;

        goalX = agent.NavState.PathHandle.GoalX;
        goalY = agent.NavState.PathHandle.GoalY;
        return true;
    }

    public static bool TryGetEditorTestLastFixedNavigationGoal(int entityId, out FixVector2 goal)
    {
        goal = FixVector2.Zero;
        if (!Agents.TryGetValue(entityId, out AgentRuntimeData agent) || !agent.NavState.HasGoal)
            return false;

        goal = agent.NavState.LastGoalWorldFixed;
        return true;
    }

    public static bool TryGetEditorTestPathBuildSource(int entityId, out string buildSource)
    {
        buildSource = null;
        if (!Agents.TryGetValue(entityId, out AgentRuntimeData agent))
            return false;

        PathHandle handle = agent.NavState.PathHandle;
        if (handle == null)
            return false;

        buildSource = handle.BuildSource;
        return true;
    }

    public static bool TryGetEditorTestPortalSummary(int portalId, out int sectorAId, out int sectorBId, out int widthCells, out bool isVerticalBoundary, out Vector3 center)
    {
        sectorAId = -1;
        sectorBId = -1;
        widthCells = 0;
        isVerticalBoundary = false;
        center = Vector3.zero;
        if (_world == null || !TryGetPortalById(_world, portalId, out PortalData portal))
            return false;

        sectorAId = portal.SectorAId;
        sectorBId = portal.SectorBId;
        widthCells = portal.WidthCells;
        isVerticalBoundary = portal.IsVerticalBoundary;
        center = portal.WorldCenter;
        return true;
    }

    public static int GetEditorTestPendingFlowTileBuildCount()
    {
        return PendingFlowTileBuildJobs.Count;
    }

    public static int GetEditorTestPendingFinalGoalFlowTileBuildCount()
    {
        int count = 0;
        foreach (FlowTileCacheKey key in PendingFlowTileBuildJobs)
        {
            if (key.GoalKind == TileGoalKind.FinalGoal)
                count++;
        }
        return count;
    }

    public static int GetEditorTestCachedFinalGoalFlowTileCount()
    {
        int count = 0;
        foreach (FlowTileCacheKey key in FlowTileCache.Keys)
        {
            if (key.GoalKind == TileGoalKind.FinalGoal)
                count++;
        }
        return count;
    }

    public static string GetEditorTestPendingNavigationWorkDiagnostics()
    {
        var builder = new StringBuilder(2048);
        builder.Append("worlds=[");
        bool wroteWorld = false;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state == null)
                throw new InvalidOperationException("Pending navigation diagnostics found a null world state.");
            if (wroteWorld)
                builder.Append(';');
            wroteWorld = true;
            builder.Append("{agentType=").Append(state.AgentTypeId)
                .Append(",dirty=").Append(state.IsDirty)
                .Append(",worldBuild=").Append(state.BuildJob?.Stage.ToString() ?? "none")
                .Append(",runtimeDirty=").Append(state.RuntimeDirtyJob?.Stage.ToString() ?? "none")
                .Append(",dirtySectors=").Append(state.DirtyRuntimeObstacleSectors.Count)
                .Append('}');
        }

        builder.Append("], flowCount=").Append(PendingFlowTileBuildJobs.Count)
            .Append(", flowJobs=").Append(BuildPendingFlowTileJobDiagnostics())
            .Append(", sharedCount=").Append(PendingSharedGoalFieldBuildJobs.Count)
            .Append(", sharedJobs=").Append(BuildPendingSharedGoalFieldJobDiagnostics())
            .Append(", pathRequestCount=").Append(PendingNavigationPathRequests.Count)
            .Append(", pathRequests=").Append(GetEditorTestPendingNavigationPathRequestDiagnostics())
            .Append(", movingProjectionCount=").Append(MovingTargetProjectionQueue.Count)
            .Append(", movingProjectionJobs=").Append(BuildPendingMovingTargetProjectionDiagnostics());
        return builder.ToString();
    }

    public static int GetEditorTestDuplicatePendingFlowTileBuildKeyCount()
    {
        HashSet<FlowTileCacheKey> seen = new HashSet<FlowTileCacheKey>();
        int duplicateCount = 0;
        LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First;
        while (node != null)
        {
            FlowTileCacheKey key = node.Value.BuildKey.CacheKey;
            if (!seen.Add(key))
                duplicateCount++;

            node = node.Next;
        }

        return duplicateCount;
    }

    public static int GetEditorTestPendingFlowTileBuildQueueIndex(int entityId)
    {
        if (_world == null || !Agents.TryGetValue(entityId, out AgentRuntimeData agent))
            return -1;

        PathHandle handle = agent.NavState.PathHandle;
        if (handle == null || handle.SectorIds == null || handle.SectorIds.Length == 0)
            return -1;

        int sectorPathIndex = Mathf.Clamp(handle.CurrentSectorIndex, 0, handle.SectorIds.Length - 1);
        FlowTileCacheKey key = CreateTileCacheKeyForPathSegment(
            handle,
            sectorPathIndex,
            handle.GoalX,
            handle.GoalY,
            agent.AgentTypeId,
            out _,
            out _);
        return TryGetFlowTileJobQueueState(key, out int queueIndex, out _, out _, out _)
            ? queueIndex
            : -1;
    }

    public static string GetEditorTestNavigationDistancePrewarmDiagnostics()
    {
        var diagnostics = new StringBuilder(512);
        diagnostics.Append("completed=").Append(s_NavigationDistancePrewarmCompleted)
            .Append(", completionDirty=").Append(s_NavigationDistancePrewarmCompletionMayHaveChanged)
            .Append(", requests=").Append(NavigationDistancePrewarmRequests.Count)
            .Append(", sharedCache=").Append(SharedGoalFields.Count)
            .Append(", pendingShared=").Append(PendingSharedGoalFieldBuildJobs.Count);

        for (int i = 0; i < NavigationDistancePrewarmRequests.Count; i++)
        {
            NavigationDistancePrewarmRequest request = NavigationDistancePrewarmRequests[i];
            bool hasWorld = WorldStates.TryGetValue(request.AgentTypeId, out WorldRuntimeState state)
                            && state?.World != null;
            bool worldReady = hasWorld && !state.IsDirty && state.BuildJob == null;
            bool hasField = false;
            bool pending = false;
            bool demandCovered = false;
            SharedGoalFieldKey key = default;
            if (hasWorld
                && request.GoalSectorId >= 0
                && request.GoalSectorId < state.World.Sectors.Length)
            {
                NavigationWorld world = state.World;
                key = new SharedGoalFieldKey(
                    world.Version,
                    request.AgentTypeId,
                    request.GoalSectorId,
                    world.GetIndex(request.GoalX, request.GoalY),
                    world.Sectors[request.GoalSectorId].DirtyVersion);
                pending = PendingSharedGoalFieldBuildJobs.Contains(key);
                hasField = SharedGoalFields.TryGetValue(key, out SharedGoalField field)
                           && field != null;
                demandCovered = hasField
                                && IsSharedGoalFieldDemandCovered(
                                    world,
                                    field,
                                    request.StartSectorId,
                                    world.GetIndex(request.StartX, request.StartY));
            }
            bool queryable = TryEstimatePrewarmedNavigationDistanceToReachableGoalFixed(
                request.From,
                request.RawGoal,
                request.AgentTypeId,
                out _,
                out _,
                out string failureReason);
            diagnostics.Append("; request[").Append(i).Append("]={agent=").Append(request.AgentTypeId)
                .Append(", resolvedWorld=").Append(request.ResolvedWorldVersion)
                .Append(", resolvedTopology=").Append(request.ResolvedTopologyVersion)
                .Append(", currentTopology=").Append(_navigationTopologyVersion)
                .Append(", currentWorld=").Append(hasWorld ? state.World.Version : -1)
                .Append(", worldReady=").Append(worldReady)
                .Append(", start=(").Append(request.StartX).Append(',').Append(request.StartY).Append(')')
                .Append(", startSector=").Append(request.StartSectorId)
                .Append(", goal=(").Append(request.GoalX).Append(',').Append(request.GoalY).Append(')')
                .Append(", goalSector=").Append(request.GoalSectorId)
                .Append(", key=").Append(hasWorld ? key.ToString() : "none")
                .Append(", field=").Append(hasField)
                .Append(", pending=").Append(pending)
                .Append(", demandCovered=").Append(demandCovered)
                .Append(", queryable=").Append(queryable)
                .Append(", failure=").Append(queryable ? "none" : failureReason)
                .Append('}');
        }

        return diagnostics.ToString();
    }

    public static int GetEditorTestActiveWorldVersion(int agentTypeId)
    {
        int resolvedAgentTypeId = ResolvePreferredAgentTypeId(agentTypeId);
        if (!WorldStates.TryGetValue(resolvedAgentTypeId, out WorldRuntimeState state)
            || state?.World == null)
        {
            throw new InvalidOperationException(
                $"No committed navigation world exists for agent type {resolvedAgentTypeId}.");
        }

        return state.World.Version;
    }

    public static int GetEditorTestPendingSharedGoalFieldBuildCount()
    {
        return PendingSharedGoalFieldBuildJobs.Count;
    }

    public static void SetEditorTestRequirePreparedNavigationSnapshot(bool required)
    {
#if UNITY_EDITOR
        _editorTestRequirePreparedNavigationSnapshot = required;
#else
        throw new InvalidOperationException("Editor test navigation snapshot mode is unavailable outside Unity Editor.");
#endif
    }

    public static string GetEditorTestMovingTargetAnchorDiagnostics(int agentId)
    {
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            return "movingAnchor=agent-missing";

        NavigationWorld previousWorld = _world;
        WorldRuntimeState previousActiveWorldState = _activeWorldState;
        try
        {
            if (!TryEnsureWorldBuilt(agent.AgentTypeId, allowSynchronousBuild: true))
                return "movingAnchor=world-unavailable";

            AgentNavState nav = agent.NavState;
            int targetId = nav.StableGoalTargetId;
            if (targetId == int.MinValue)
                return "movingAnchor=no-target";

            int islandId = ResolveIslandIdForDiagnostics(_world, agent.NavState.CurrentCell.x, agent.NavState.CurrentCell.y);
            MovingTargetAnchorKey key = new MovingTargetAnchorKey(targetId, ResolvePreferredAgentTypeId(agent.AgentTypeId), islandId);
            if (!MovingTargetAnchors.TryGetValue(key, out MovingTargetAnchor anchor))
                return $"movingAnchor=missing key=target:{targetId},agentType:{ResolvePreferredAgentTypeId(agent.AgentTypeId)},island:{islandId}";

            return $"movingAnchor=key(target:{key.TargetId},agentType:{key.AgentTypeId},island:{key.IslandId}) " +
                   $"activeRaw=({anchor.RawGoalX},{anchor.RawGoalY}) active=({anchor.ActiveGoalX},{anchor.ActiveGoalY}) activeSector={anchor.ActiveGoalSectorId} activeWorld={anchor.ActiveGoalWorld} activeVersion={anchor.ActiveWorldVersion} " +
                   $"projection=(pending={anchor.HasPendingProjection},world={anchor.PendingProjectionWorldVersion},raw=({anchor.PendingProjectionRawGoalX},{anchor.PendingProjectionRawGoalY}),leaf={anchor.PendingProjectionLeafBucketId}:{anchor.PendingProjectionLeafCellCursor},cells={anchor.PendingProjectionCellCursor},frontier={anchor.PendingProjectionNodeHeap.Count},best={anchor.PendingProjectionBestCellIndex}) " +
                   $"projectionQueue={MovingTargetProjectionQueue.Count} queue={SharedGoalFieldBuildQueue.Count} pendingKeys={PendingSharedGoalFieldBuildJobs.Count} sharedCache={SharedGoalFields.Count} sharedJobs={BuildPendingSharedGoalFieldJobDiagnostics()}";
        }
        finally
        {
            _world = previousWorld;
            _activeWorldState = previousActiveWorldState;
        }
    }

    public static int GetEditorTestMovingTargetProjectionQueueCount()
    {
        return MovingTargetProjectionQueue.Count;
    }

    public static bool TryGetEditorTestMovingTargetProjectionState(
        int targetId,
        int agentTypeId,
        int islandId,
        out bool pending,
        out int rawGoalX,
        out int rawGoalY,
        out int processedCells,
        out int frontierCount,
        out int publishedGoalX,
        out int publishedGoalY)
    {
        var key = new MovingTargetAnchorKey(targetId, ResolvePreferredAgentTypeId(agentTypeId), islandId);
        if (!MovingTargetAnchors.TryGetValue(key, out MovingTargetAnchor anchor) || anchor == null)
        {
            pending = false;
            rawGoalX = -1;
            rawGoalY = -1;
            processedCells = 0;
            frontierCount = 0;
            publishedGoalX = -1;
            publishedGoalY = -1;
            return false;
        }

        pending = anchor.HasPendingProjection;
        rawGoalX = anchor.PendingProjectionRawGoalX;
        rawGoalY = anchor.PendingProjectionRawGoalY;
        processedCells = anchor.PendingProjectionCellCursor;
        frontierCount = anchor.PendingProjectionNodeHeap.Count;
        publishedGoalX = anchor.ActiveGoalX;
        publishedGoalY = anchor.ActiveGoalY;
        return true;
    }

    private static string BuildPendingMovingTargetProjectionDiagnostics()
    {
        var builder = new StringBuilder(256);
        bool wrote = false;
        foreach (MovingTargetAnchorKey key in MovingTargetProjectionQueue)
        {
            if (!MovingTargetAnchors.TryGetValue(key, out MovingTargetAnchor anchor) || anchor == null)
                throw new InvalidOperationException("Moving-target projection diagnostics found a missing anchor.");
            if (!anchor.HasPendingProjection)
                throw new InvalidOperationException("Moving-target projection diagnostics found a non-pending anchor in the queue.");
            if (wrote)
                builder.Append(" | ");
            wrote = true;
            builder.Append("{key=").Append(key.TargetId).Append('/').Append(key.AgentTypeId).Append('/').Append(key.IslandId)
                .Append(",world=").Append(anchor.PendingProjectionWorldVersion)
                .Append(",raw=(").Append(anchor.PendingProjectionRawGoalX).Append(',').Append(anchor.PendingProjectionRawGoalY).Append(')')
                .Append(",cells=").Append(anchor.PendingProjectionCellCursor)
                .Append(",leaf=").Append(anchor.PendingProjectionLeafBucketId).Append(':').Append(anchor.PendingProjectionLeafCellCursor)
                .Append(",frontier=").Append(anchor.PendingProjectionNodeHeap.Count)
                .Append(",best=").Append(anchor.PendingProjectionBestCellIndex)
                .Append('}');
        }
        return builder.ToString();
    }

    private static string BuildSharedGoalFieldStateDiagnostic(int goalSectorId, int goalX, int goalY, int agentTypeId)
    {
        if (_world == null)
            return "world-null";
        if (goalSectorId < 0 || goalX < 0 || goalY < 0)
            return "none";
        if (goalSectorId >= _world.Sectors.Length)
            return $"invalid-sector:{goalSectorId}";

        SharedGoalFieldKey key = CreateSharedGoalFieldKey(goalSectorId, goalX, goalY, agentTypeId);
        bool cached = SharedGoalFields.ContainsKey(key);
        bool pending = PendingSharedGoalFieldBuildJobs.Contains(key);
        string job = BuildSharedGoalFieldJobDiagnosticForKey(key);
        return $"key={FormatSharedGoalFieldKey(key)},cached={cached},pending={pending},job={job}";
    }

    private static string BuildSharedGoalFieldJobDiagnosticForKey(SharedGoalFieldKey key)
    {
        int index = 0;
        for (LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First; node != null; node = node.Next, index++)
        {
            SharedGoalFieldBuildJob job = node.Value;
            if (job == null || !job.Key.Equals(key))
                continue;

            bool stale = IsSharedGoalFieldBuildJobStale(job);
            int openCount = job.PortalOpenSet?.Count ?? -1;
            return "{index=" + index +
                   ",stage=" + job.Stage +
                   ",stale=" + stale +
                   ",open=" + openCount +
                   ",hasField=" + (job.Field != null) +
                   "}";
        }

        return "not-in-queue";
    }

    private static string BuildPendingSharedGoalFieldJobDiagnostics()
    {
        if (SharedGoalFieldBuildQueue.Count == 0)
            return "[]";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append('[');
        int count = 0;
        for (LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First; node != null && count < 6; node = node.Next, count++)
        {
            if (count > 0)
                builder.Append(" | ");

            SharedGoalFieldBuildJob job = node.Value;
            if (job == null)
            {
                builder.Append("{null}");
                continue;
            }

            int openCount = job.PortalOpenSet?.Count ?? -1;
            builder.Append("{key=").Append(FormatSharedGoalFieldKey(job.Key))
                .Append(",goal=(").Append(job.GoalX).Append(',').Append(job.GoalY).Append(')')
                .Append(",stage=").Append(job.Stage)
                .Append(",stale=").Append(IsSharedGoalFieldBuildJobStale(job))
                .Append(",open=").Append(openCount)
                .Append(",hasField=").Append(job.Field != null)
                .Append('}');
        }

        if (SharedGoalFieldBuildQueue.Count > count)
            builder.Append(" | ...");
        builder.Append(']');
        return builder.ToString();
    }

    public static int GetEditorTestSectorPortalAccessCacheCount()
    {
        return SectorPortalAccessCache.Count;
    }

    public static bool HasEditorTestCommittedFullCostField()
    {
        return _world?.CostField != null;
    }

    public static int GetEditorTestCommittedSectorCostChunkCount()
    {
        if (_world?.SectorCostFields == null)
            return 0;

        int count = 0;
        for (int i = 0; i < _world.SectorCostFields.Length; i++)
        {
            if (_world.SectorCostFields[i] != null)
                count++;
        }

        return count;
    }

    public static int GetEditorTestAnalyticSectorPortalAccessCacheCount()
    {
        int count = 0;
        foreach (SectorPortalAccessEntry entry in SectorPortalAccessCache.Values)
        {
            if (entry != null && entry.IsAnalyticClearSector)
                count++;
        }

        return count;
    }

    public static int GetEditorTestDeterministicSectorPortalAccessCacheCount()
    {
        int count = 0;
        foreach (SectorPortalAccessEntry entry in SectorPortalAccessCache.Values)
        {
            if (entry == null)
                continue;
            if (entry.IsAnalyticClearSector
                || (entry.DeterministicIntegration != null && entry.DeterministicIntegration.Length > 0))
            {
                count++;
            }
        }

        return count;
    }

    public static int GetEditorTestInvalidDeterministicPortalTransitionCount()
    {
        if (_world?.Sectors == null)
            return 0;

        int count = 0;
        for (int sectorIndex = 0; sectorIndex < _world.Sectors.Length; sectorIndex++)
        {
            List<PortalTransition> transitions = _world.Sectors[sectorIndex].PortalTransitions;
            for (int transitionIndex = 0; transitionIndex < transitions.Count; transitionIndex++)
            {
                if (transitions[transitionIndex].DeterministicCost == long.MaxValue)
                    count++;
            }
        }

        return count;
    }

    public static bool TryGetEditorTestFirstPortalForSector(int sectorId, out int portalId, out int oppositeSectorId)
    {
        portalId = -1;
        oppositeSectorId = -1;
        if (_world == null || sectorId < 0 || sectorId >= _world.Sectors.Length)
            return false;

        SectorData sector = _world.Sectors[sectorId];
        if (sector.PortalIds.Count == 0)
            return false;

        portalId = sector.PortalIds[0];
        if (!TryGetPortalById(_world, portalId, out PortalData portal))
            return false;

        oppositeSectorId = GetOppositeSectorId(portal, sectorId);
        return oppositeSectorId >= 0;
    }

    public static bool TryGetEditorTestSectorPortalAccessCost(int sectorId, int portalId, int worldX, int worldY, out float cost)
    {
        cost = float.PositiveInfinity;
        if (_world == null || sectorId < 0 || sectorId >= _world.Sectors.Length)
            return false;

        SectorData sector = _world.Sectors[sectorId];
        if (!IsInsideSector(sector, worldX, worldY))
            return false;

        SectorPortalAccessEntry entry = GetPrebuiltSectorPortalAccess(sector, sectorId, portalId);
        cost = ResolvePortalAccessIntegrationCost(_world, sector, entry, worldX, worldY);
        return true;
    }

    public static bool TryGetEditorTestSectorPortalAccessCostRaw(int sectorId, int portalId, int worldX, int worldY, out long cost)
    {
        cost = long.MaxValue;
        if (_world == null || sectorId < 0 || sectorId >= _world.Sectors.Length)
            return false;

        SectorData sector = _world.Sectors[sectorId];
        if (!IsInsideSector(sector, worldX, worldY))
            return false;

        cost = ResolveDeterministicPortalAccessCost(sector, sectorId, portalId, worldX, worldY);
        return true;
    }

    public static string BuildEditorTestPortalGraphCostDiagnostics(int startSectorId, int goalSectorId, int startX, int startY, int goalX, int goalY)
    {
        if (_world == null)
            return "portalGraphDiag=world-null";
        if (startSectorId < 0 || startSectorId >= _world.Sectors.Length || goalSectorId < 0 || goalSectorId >= _world.Sectors.Length)
            return $"portalGraphDiag=sector-out startSector={startSectorId} goalSector={goalSectorId}";

        SectorData startSector = _world.Sectors[startSectorId];
        SectorData goalSector = _world.Sectors[goalSectorId];
        System.Text.StringBuilder builder = new System.Text.StringBuilder(1024);
        builder.Append("portalGraphDiag={startPortals=[");
        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            if (i > 0)
                builder.Append(" | ");
            builder.Append(portalId)
                .Append(":access=")
                .Append(FormatDiagnosticCost(ResolvePortalAccessCost(startSector, startSectorId, portalId, startX, startY)))
                .Append(",")
                .Append(FormatPortal(GetPortalById(_world, portalId)));
        }

        builder.Append("] goalPortals=[");
        for (int i = 0; i < goalSector.PortalIds.Count; i++)
        {
            int portalId = goalSector.PortalIds[i];
            if (i > 0)
                builder.Append(" | ");
            builder.Append(portalId)
                .Append(":access=")
                .Append(FormatDiagnosticCost(ResolvePortalAccessCost(goalSector, goalSectorId, portalId, goalX, goalY)))
                .Append(",")
                .Append(FormatPortal(GetPortalById(_world, portalId)));
        }

        builder.Append("] transitions=[");
        for (int s = 0; s < _world.Sectors.Length; s++)
        {
            SectorData sector = _world.Sectors[s];
            for (int i = 0; i < sector.PortalTransitions.Count; i++)
            {
                PortalTransition transition = sector.PortalTransitions[i];
                if (builder[builder.Length - 1] != '[')
                    builder.Append(" | ");
                builder.Append("s")
                    .Append(s)
                    .Append(":")
                    .Append(transition.FromPortalId)
                    .Append("->")
                    .Append(transition.ToPortalId)
                    .Append("=raw:")
                    .Append(transition.DeterministicCost);
            }
        }

        builder.Append("]}");
        return builder.ToString();
    }

    public static bool TryGetEditorTestCachedTileCellFlags(int worldX, int worldY, out bool hasLineOfSight, out bool waveFrontBlocked, out bool pathable)
    {
        hasLineOfSight = false;
        waveFrontBlocked = false;
        pathable = false;
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY))
                continue;

            int localIndex = tile.GetLocalIndex(worldX, worldY);
            pathable = IsFlowPathable(tile, localIndex);
            return true;
        }

        return false;
    }

    public static bool TryGetEditorTestCachedTileCellFlowDirection(int worldX, int worldY, out Vector2 direction)
    {
        direction = Vector2.zero;
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY) || tile.FlowFieldValues == null)
                continue;

            int localIndex = tile.GetLocalIndex(worldX, worldY);
            direction = ResolveRuntimeFlowDirection(tile, worldX, worldY, localIndex);
            return true;
        }

        return false;
    }

    public static bool TryGetEditorTestCachedTileCellStoredFlowDirection(int worldX, int worldY, out Vector2 direction)
    {
        direction = Vector2.zero;
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY) || tile.FlowFieldValues == null)
                continue;

            direction = GetFlowDirection(tile, tile.GetLocalIndex(worldX, worldY));
            return true;
        }

        return false;
    }

    public static bool TryGetEditorTestAnyCachedTileCellDescriptorFlow(int worldX, int worldY, out string diagnostic)
    {
        diagnostic = $"cell=({worldX},{worldY}) tile=missing";
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY) || tile.FlowFieldValues == null)
                continue;

            int localIndex = tile.GetLocalIndex(worldX, worldY);
            Vector2 stored = GetFlowDirection(tile, localIndex);
            Vector2 runtime = ResolveRuntimeFlowDirection(tile, worldX, worldY, localIndex);
            diagnostic = GetEditorTestCachedTileCellDiagnostic(worldX, worldY);
            if (IsFlowPathable(tile, localIndex)
                && stored.sqrMagnitude <= 0.0001f
                && runtime.sqrMagnitude > 0.0001f)
            {
                return true;
            }
        }

        return false;
    }

    public static string GetEditorTestCachedTileCellDiagnostic(int worldX, int worldY)
    {
        if (_world == null)
            return "world=null";

        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY) || tile.FlowFieldValues == null)
                continue;

            int localIndex = tile.GetLocalIndex(worldX, worldY);
            byte cost = TryGetCostFieldValue(_world, worldX, worldY, out byte resolvedCost) ? resolvedCost : (byte)0;
            TryGetSectorForCell(_world, worldX, worldY, out SectorData sector);
            float integration = GetTileIntegrationCostForDiagnostics(tile, worldX, worldY);
            string integrationText = float.IsPositiveInfinity(integration) ? "INF/released" : integration.ToString("F3");
            return $"cell=({worldX},{worldY}) cost={cost} integration={integrationText} " +
                   $"pathable={IsFlowPathable(tile, localIndex)} " +
                   $"stored={GetFlowDirection(tile, localIndex)} runtime={ResolveRuntimeFlowDirection(tile, worldX, worldY, localIndex)} " +
                   $"sectorClearCost={(sector != null && sector.IsClearCostField)} sectorClearFlow={(sector != null && sector.IsClearFlowTile)} " +
                   $"tile={FormatTileKey(tile.Key)}";
        }

        return $"cell=({worldX},{worldY}) tile=missing";
    }

    public static int GetEditorTestSharedGoalFieldCacheCount()
    {
        return SharedGoalFields.Count;
    }

    public static string GetEditorTestStartPortalChoiceDiagnostics(int startSectorId, int goalSectorId, int startX, int startY, int goalX, int goalY, int agentTypeId)
    {
        if (_world == null)
            return "portalChoice=world-null";

        return BuildStartPortalChoiceDiagnostics(
            startSectorId,
            goalSectorId,
            startX,
            startY,
            goalX,
            goalY,
            agentTypeId,
            _world.GridToWorldCenter(startX, startY),
            _world.GridToWorldCenter(goalX, goalY));
    }

    public static bool TryGetEditorTestPathRebuildDecision(
        int agentId,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out bool shouldRebuild,
        out string reason,
        out string diagnostics)
    {
        shouldRebuild = false;
        reason = string.Empty;
        diagnostics = "unavailable";
        if (_world == null)
        {
            diagnostics = "world-null";
            return false;
        }

        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent) || agent?.NavState.PathHandle == null)
        {
            diagnostics = "handle-null";
            return false;
        }

        PathHandle handle = agent.NavState.PathHandle;
        shouldRebuild = ShouldRebuildPathHandleForCurrentStart(
            handle,
            startSectorId,
            goalSectorId,
            startX,
            startY,
            goalX,
            goalY,
            out reason);
        diagnostics = BuildPathRepathDecisionDiagnostics(handle, startSectorId, goalSectorId, startX, startY, goalX, goalY);
        return true;
    }

    public static bool TryGetEditorTestCostFieldValue(int worldX, int worldY, out byte cost)
    {
        cost = 0;
        if (_world == null || !_world.WorldToGrid(_world.GridToWorldCenter(worldX, worldY), out _, out _))
            return false;
        if (worldX < 0 || worldX >= _world.Width || worldY < 0 || worldY >= _world.Height)
            return false;
        if (!TryGetCostFieldValue(_world, worldX, worldY, out cost))
            return false;

        return true;
    }

    public static long GetEditorTestWallCostBlurRadiusFixedRaw()
    {
        if (_world == null)
            throw new InvalidOperationException("GetEditorTestWallCostBlurRadiusFixedRaw failed: world is null.");
        return ResolveWallCostBlurRadiusCellsFixed(_world).RawValue;
    }

    public static int GetEditorTestWallCostAdjacentPenalty()
    {
        if (_world == null)
            throw new InvalidOperationException("GetEditorTestWallCostAdjacentPenalty failed: world is null.");
        return ResolveWallCostAdjacentPenalty(_world);
    }

    public static int GetEditorTestSpatialIntegrationSubstepCount(Fix64 travelDistance)
    {
        if (_world == null)
            throw new InvalidOperationException("GetEditorTestSpatialIntegrationSubstepCount failed: world is null.");
        return ResolveSpatialIntegrationSubstepCount(travelDistance, _world.CellSizeGridRaw);
    }

    public static bool TryGetEditorTestSectorClearCostState(int worldX, int worldY, out bool isClearCostField)
    {
        isClearCostField = false;
        if (_world == null || !TryGetSectorForCell(_world, worldX, worldY, out SectorData sector))
            return false;

        isClearCostField = sector.IsClearCostField;
        return true;
    }

    public static bool TryGetEditorTestSectorClearFlowTileState(int worldX, int worldY, out bool isClearFlowTile)
    {
        isClearFlowTile = false;
        if (_world == null || !TryGetSectorForCell(_world, worldX, worldY, out SectorData sector))
            return false;

        isClearFlowTile = sector.IsClearFlowTile;
        return true;
    }

    public static bool TryGetEditorTestIslandFieldValue(int worldX, int worldY, out int islandId, out int islandCount)
    {
        islandId = 0;
        islandCount = 0;
        if (_world == null || worldX < 0 || worldX >= _world.Width || worldY < 0 || worldY >= _world.Height)
            return false;
        islandId = ResolveIslandId(_world, worldX, worldY);
        islandCount = _world.IslandCount;
        return true;
    }

    public static bool TryGetEditorTestRuntimeCellDiagnostics(
        int worldX,
        int worldY,
        out bool walkable,
        out int islandId,
        out int islandCount,
        out int mainIslandId,
        out int mainIslandSize,
        out byte neighborTraversalMask)
    {
        walkable = false;
        islandId = 0;
        islandCount = 0;
        mainIslandId = 0;
        mainIslandSize = 0;
        neighborTraversalMask = 0;
        if (_world == null || worldX < 0 || worldX >= _world.Width || worldY < 0 || worldY >= _world.Height)
            return false;
        int index = _world.GetIndex(worldX, worldY);
        walkable = _world.WalkableMask != null && index >= 0 && index < _world.WalkableMask.Length && _world.WalkableMask[index];
        islandId = ResolveIslandId(_world, worldX, worldY);
        islandCount = _world.IslandCount;
        mainIslandId = _world.MainIslandId;
        mainIslandSize = _world.MainIslandSize;
        neighborTraversalMask = _world.NeighborTraversalMask != null && index >= 0 && index < _world.NeighborTraversalMask.Length
            ? _world.NeighborTraversalMask[index]
            : (byte)0;
        return true;
    }

    public static string GetEditorNavigationCellDiagnostics(int agentTypeId, int worldX, int worldY)
    {
        int resolvedAgentTypeId = ResolvePreferredAgentTypeId(agentTypeId);
        if (!WorldStates.TryGetValue(resolvedAgentTypeId, out WorldRuntimeState state) || state == null)
            return $"agent={resolvedAgentTypeId},state=<missing>";

        string committed = BuildNavigationWorldCellDiagnostics(state.World, worldX, worldY);
        string preview = HasPendingRuntimeDirty(state)
            ? BuildNavigationWorldCellDiagnostics(ResolveReachabilityQueryWorld(state), worldX, worldY)
            : committed;
        string source;
        if (!TryGetStaticCollisionShadowSource(resolvedAgentTypeId, out LogicStaticCollisionSourceData collisionSource))
        {
            source = "<missing>";
        }
        else if (worldX < 0
                 || worldX >= collisionSource.Width
                 || worldY < 0
                 || worldY >= collisionSource.Height)
        {
            source = $"out,size={collisionSource.Width}x{collisionSource.Height}";
        }
        else
        {
            int index = checked(worldX + worldY * collisionSource.Width);
            source = $"base={collisionSource.BaseWalkableMask[index]},version={collisionSource.WorldVersion}," +
                     $"size={collisionSource.Width}x{collisionSource.Height}," +
                     $"encodedClearanceRaw={collisionSource.EncodedCenterClearanceFixedRaw}";
        }

        return $"agent={resolvedAgentTypeId},cell=({worldX},{worldY}),pending={HasPendingRuntimeDirty(state)}," +
               $"committed=[{committed}],preview=[{preview}],collisionSource=[{source}]";
    }

#endif

}
