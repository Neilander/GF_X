using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class FlowFieldCrowdMovementSystem
{
    private const ulong DeterministicHashCheckpointIntervalFrames = 30;
    private static ulong _deterministicHashCheckpointFrame;
    private static ulong _deterministicHashCheckpointValue;
    private static ulong _committedWorldSetHash;

    public static int DeterministicCheckpointRefreshCount { get; private set; }

    public static ulong CaptureDeterministicHash()
    {
        var hasher = new LogicStateHasher();
        WriteDeterministicState(hasher);
        return hasher.Hash;
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add(0x4E41564947415445UL);
        hasher.Add(_navigationTopologyVersion);
        hasher.Add(_nextWorldVersion);
        hasher.Add(_nextPathHandleId);
        hasher.Add(_flowBuildQueueWorldStartIndex);
        hasher.Add(_runtimeNavigationTransitionDepth);

        AddWorldStates(hasher);
        AddRuntimeObstacles(hasher);
        AddAgents(hasher);
        AddNavigationCaches(hasher);
        AddBuildQueues(hasher);
        AddMovingTargetAnchors(hasher);
        AddGoalReservations(hasher);
        AddBottlenecks(hasher);
    }

    public static void WriteDeterministicCheckpointState(LogicStateHasher hasher, ulong frame)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));
        if (frame == 0)
            throw new ArgumentOutOfRangeException(nameof(frame), "Navigation deterministic checkpoint requires a positive logic frame.");

        if (_deterministicHashCheckpointFrame == 0
            || frame < _deterministicHashCheckpointFrame
            || frame - _deterministicHashCheckpointFrame >= DeterministicHashCheckpointIntervalFrames)
        {
            _deterministicHashCheckpointValue = CaptureDeterministicHash();
            _deterministicHashCheckpointFrame = frame;
            DeterministicCheckpointRefreshCount++;
        }

        hasher.Add(0x4E415643484B5054UL);
        hasher.Add(_deterministicHashCheckpointFrame);
        hasher.Add(_deterministicHashCheckpointValue);
        hasher.Add(DeterministicHashCheckpointIntervalFrames);
        AddDeterministicCheckpointLiveState(hasher);
    }

    public static void WriteDeterministicFrameDigest(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add(0x4E41564652414D45UL);
        hasher.Add(_committedWorldSetHash);
        AddDeterministicCheckpointLiveState(hasher);
    }

    private static void ResetDeterministicHashCheckpoint()
    {
        _deterministicHashCheckpointFrame = 0;
        _deterministicHashCheckpointValue = 0;
        _committedWorldSetHash = 0;
        DeterministicCheckpointRefreshCount = 0;
    }

    private static void AddDeterministicCheckpointLiveState(LogicStateHasher hasher)
    {
        hasher.Add(_navigationTopologyVersion);
        hasher.Add(_nextWorldVersion);
        hasher.Add(_nextPathHandleId);
        hasher.Add(_flowBuildQueueWorldStartIndex);
        hasher.Add(_runtimeNavigationTransitionDepth);
        hasher.Add(WorldStates.Count);
        hasher.Add(_activeWorldState != null ? _activeWorldState.AgentTypeId : int.MinValue);
        hasher.Add(_world != null ? _world.Version : 0);
        hasher.Add(Agents.Count);
        hasher.Add(CircleObstacles.Count);
        hasher.Add(BoxObstacles.Count);
        hasher.Add(CostStamps.Count);
        hasher.Add(SectorPathCache.Count);
        hasher.Add(SectorPortalAccessCache.Count);
        hasher.Add(FlowTileCache.Count);
        hasher.Add(SharedGoalFields.Count);
        hasher.Add(FlowTileBuildQueue.Count);
        hasher.Add(SharedGoalFieldBuildQueue.Count);
        hasher.Add(MovingTargetAnchors.Count);
        hasher.Add(NavigationGoalReservations.Count);
        hasher.Add(Bottlenecks.Count);

        int dirtyWorldCount = 0;
        int worldBuildJobCount = 0;
        int runtimeDirtyJobCount = 0;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state == null)
                throw new InvalidOperationException("Navigation frame digest encountered a null world state.");
            if (state.IsDirty)
                dirtyWorldCount++;
            if (state.BuildJob != null)
                worldBuildJobCount++;
            if (state.RuntimeDirtyJob != null)
                runtimeDirtyJobCount++;
        }
        hasher.Add(dirtyWorldCount);
        hasher.Add(worldBuildJobCount);
        hasher.Add(runtimeDirtyJobCount);
    }

    private static void AddWorldStates(LogicStateHasher hasher)
    {
        var keys = new List<int>(WorldStates.Keys);
        keys.Sort();
        hasher.Add(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            WorldRuntimeState state = WorldStates[keys[i]];
            if (state == null)
                throw new InvalidOperationException($"Flow world state is null. agentType={keys[i]}.");

            hasher.Add(keys[i]);
            hasher.Add(state.AgentTypeId);
            hasher.Add(state.IsDirty);
            AddSortedInts(hasher, state.DirtyRuntimeObstacleSectors);
            AddNavigationWorld(hasher, state.World);
            AddWorldBuildJob(hasher, state.BuildJob);
            AddRuntimeDirtyJob(hasher, state.RuntimeDirtyJob);
        }

        hasher.Add(_activeWorldState != null ? _activeWorldState.AgentTypeId : int.MinValue);
        hasher.Add(_world != null ? _world.Version : 0);
    }

    private static void AddNavigationWorld(LogicStateHasher hasher, NavigationWorld world)
    {
        hasher.Add(world != null);
        if (world == null)
            return;

        if (!world.HasDeterministicContentHash)
        {
            throw new InvalidOperationException(
                $"Committed navigation world has no deterministic content hash. agentType={world.AgentTypeId}, version={world.Version}.");
        }

        hasher.Add(world.DeterministicContentHash);
    }

    private static void RefreshNavigationWorldDeterministicHash(NavigationWorld world)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));

        var hasher = new LogicStateHasher();
        AddNavigationWorldContents(hasher, world);
        world.DeterministicContentHash = hasher.Hash;
        world.HasDeterministicContentHash = true;
        DeterministicWorldHashRefreshCount++;
        RefreshCommittedWorldSetHash();
    }

    private static void RefreshCommittedWorldSetHash()
    {
        var keys = new List<int>(WorldStates.Keys);
        keys.Sort();
        var hasher = new LogicStateHasher();
        hasher.Add(0x4E4156574F524C53UL);
        hasher.Add(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            WorldRuntimeState state = WorldStates[keys[i]];
            hasher.Add(keys[i]);
            hasher.Add(state?.World != null);
            if (state?.World == null)
                continue;
            if (!state.World.HasDeterministicContentHash)
            {
                throw new InvalidOperationException(
                    $"Committed navigation world set contains an unhashed world. agentType={keys[i]}, version={state.World.Version}.");
            }
            hasher.Add(state.World.DeterministicContentHash);
        }
        _committedWorldSetHash = hasher.Hash;
    }

    private static void AddNavigationWorldBuildState(LogicStateHasher hasher, NavigationWorld world)
    {
        hasher.Add(world != null);
        if (world == null)
            return;

        hasher.Add(world.Version);
        hasher.Add(world.AgentTypeId);
        hasher.Add(world.Width);
        hasher.Add(world.Height);
        AddFloat(hasher, world.CellSize);
        AddFloat(hasher, world.EncodedCenterClearance);
        AddVector3(hasher, world.Origin);
        hasher.Add(world.IslandCount);
        hasher.Add(world.MainIslandId);
        hasher.Add(world.MainIslandSize);
        hasher.Add(world.SectorSizeInCells);
        hasher.Add(world.SectorCountX);
        hasher.Add(world.SectorCountY);
        hasher.Add(world.NextPortalId);
    }

    private static void AddNavigationWorldContents(LogicStateHasher hasher, NavigationWorld world)
    {
        hasher.Add(0x4E4156574F524C44UL);

        hasher.Add(world.Version);
        hasher.Add(world.AgentTypeId);
        hasher.Add(world.Width);
        hasher.Add(world.Height);
        AddFloat(hasher, world.CellSize);
        AddFloat(hasher, world.EncodedCenterClearance);
        AddVector3(hasher, world.Origin);
        hasher.Add(world.IslandCount);
        hasher.Add(world.MainIslandId);
        hasher.Add(world.MainIslandSize);
        hasher.Add(world.SectorSizeInCells);
        hasher.Add(world.SectorCountX);
        hasher.Add(world.SectorCountY);
        hasher.Add(world.NextPortalId);
        AddBoolArray(hasher, world.BaseWalkableMask);
        AddByteArray(hasher, world.BaseNeighborTraversalMask);
        AddByteArray(hasher, world.SourceCostField);
        AddBoolArray(hasher, world.WalkableMask);
        AddByteArray(hasher, world.CostField);
        AddByteArray(hasher, world.NeighborTraversalMask);
        AddIntArray(hasher, world.IslandIds);
        AddByteArrayArray(hasher, world.SectorCostFields);
        AddVector3Array(hasher, world.CellNavAnchors);

        int sectorCount = world.Sectors?.Length ?? 0;
        hasher.Add(sectorCount);
        for (int i = 0; i < sectorCount; i++)
        {
            SectorData sector = world.Sectors[i];
            if (sector == null)
                throw new InvalidOperationException($"Flow sector is null. world={world.Version}, index={i}.");
            hasher.Add(sector.SectorId);
            hasher.Add(sector.StartX);
            hasher.Add(sector.StartY);
            hasher.Add(sector.Width);
            hasher.Add(sector.Height);
            hasher.Add(sector.DirtyVersion);
            hasher.Add(sector.IsClearCostField);
            hasher.Add(sector.IsClearFlowTile);
            hasher.Add(sector.UniformIslandId);
            hasher.Add(sector.LocalComponentCount);
            AddIntArray(hasher, sector.LocalComponentIds);
            AddOrderedInts(hasher, sector.PortalIds);
            hasher.Add(sector.PortalTransitions.Count);
            for (int transitionIndex = 0; transitionIndex < sector.PortalTransitions.Count; transitionIndex++)
            {
                PortalTransition transition = sector.PortalTransitions[transitionIndex];
                hasher.Add(transition.FromPortalId);
                hasher.Add(transition.ToPortalId);
                hasher.Add(transition.DeterministicCost);
            }
        }

        var portals = world.Portals != null ? new List<PortalData>(world.Portals) : new List<PortalData>();
        portals.Sort((left, right) => left.PortalId.CompareTo(right.PortalId));
        hasher.Add(portals.Count);
        for (int i = 0; i < portals.Count; i++)
        {
            PortalData portal = portals[i];
            hasher.Add(portal.PortalId);
            hasher.Add(portal.SectorAId);
            hasher.Add(portal.SectorBId);
            hasher.Add(portal.WidthCells);
            hasher.Add(portal.IsNarrow);
            hasher.Add(portal.IsVerticalBoundary);
            AddVector2IntArray(hasher, portal.CellsA);
            AddVector2IntArray(hasher, portal.CellsB);
        }

        AddSortedPortalSignatures(hasher, world.PortalIdsBySignature);
        AddSortedInts(hasher, world.UsedPortalIds);
    }

    private static void AddWorldBuildJob(LogicStateHasher hasher, WorldBuildJob job)
    {
        hasher.Add(job != null);
        if (job == null)
            return;

        hasher.Add(job.AgentTypeId);
        hasher.Add((int)job.Stage);
        hasher.Add(job.RasterCursor);
        hasher.Add(job.ObstacleCursor);
        hasher.Add(job.ApplyingCircleObstacles);
        hasher.Add(job.SectorCursor);
        hasher.Add(job.CellCursor);
        hasher.Add(job.IslandScanIndex);
        hasher.Add(job.IslandCurrentId);
        hasher.Add(job.IslandCurrentSize);
        hasher.Add(job.IslandMainId);
        hasher.Add(job.IslandMainSize);
        hasher.Add(job.IslandBfsActive);
        hasher.Add(job.IslandInitialized);
        hasher.Add((int)job.PortalStage);
        hasher.Add(job.PortalInitialized);
        hasher.Add(job.PortalAddCursor);
        hasher.Add(job.PortalTransitionCursor);
        hasher.Add(job.PortalTransitionFromCursor);
        hasher.Add(job.PortalTransitionIntegrationActive);
        hasher.Add(job.PortalTransitionFromPortalId);
        hasher.Add(job.PortalTransitionTargetLinkCount);
        AddQueue(hasher, job.IslandOpenQueue);
        AddFloatArray(hasher, job.PortalTransitionIntegration);
        AddMinHeap(hasher, job.PortalTransitionOpenSet);
        AddNavigationWorldBuildState(hasher, job.WorkingWorld);
    }

    private static void AddRuntimeDirtyJob(LogicStateHasher hasher, RuntimeDirtyRebuildJob job)
    {
        hasher.Add(job != null);
        if (job == null)
            return;

        hasher.Add((int)job.Stage);
        hasher.Add(job.CloneCellCursor);
        hasher.Add(job.CloneSectorCursor);
        hasher.Add(job.CloneShellInitialized);
        hasher.Add(job.SectorCursor);
        hasher.Add(job.ObstacleCursor);
        hasher.Add(job.ApplyingCircleObstacles);
        hasher.Add(job.IslandScanIndex);
        hasher.Add(job.IslandCurrentId);
        hasher.Add(job.IslandCurrentSize);
        hasher.Add(job.IslandMainId);
        hasher.Add(job.IslandMainSize);
        hasher.Add(job.IslandBfsActive);
        hasher.Add(job.IslandInitialized);
        hasher.Add((int)job.PortalStage);
        hasher.Add(job.PortalInitialized);
        hasher.Add(job.PortalSectorCursor);
        hasher.Add(job.PortalAddCursor);
        hasher.Add(job.PortalTransitionCursor);
        hasher.Add(job.PortalTransitionFromCursor);
        hasher.Add(job.PortalTransitionIntegrationActive);
        hasher.Add(job.PortalTransitionFromPortalId);
        hasher.Add(job.PortalTransitionTargetLinkCount);
        AddSortedInts(hasher, job.DirtySectors);
        AddSortedInts(hasher, job.CostDirtySectors);
        AddSortedInts(hasher, job.PortalTransitionDirtySectors);
        AddQueue(hasher, job.IslandOpenQueue);
        AddFloatArray(hasher, job.PortalTransitionIntegration);
        AddMinHeap(hasher, job.PortalTransitionOpenSet);
        AddNavigationWorldBuildState(hasher, job.WorkingWorld);
    }

    private static void AddRuntimeObstacles(LogicStateHasher hasher)
    {
        var circleIds = new List<int>(CircleObstacles.Keys);
        circleIds.Sort();
        hasher.Add(circleIds.Count);
        for (int i = 0; i < circleIds.Count; i++)
        {
            CircleObstacle obstacle = CircleObstacles[circleIds[i]];
            hasher.Add(obstacle.Id);
            AddVector3(hasher, obstacle.Position);
            AddFloat(hasher, obstacle.Radius);
        }

        var boxIds = new List<int>(BoxObstacles.Keys);
        boxIds.Sort();
        hasher.Add(boxIds.Count);
        for (int i = 0; i < boxIds.Count; i++)
        {
            BoxObstacle obstacle = BoxObstacles[boxIds[i]];
            hasher.Add(obstacle.Id);
            AddVector3(hasher, obstacle.Center);
            AddVector3(hasher, obstacle.HalfExtents);
        }

        var costIds = new List<int>(CostStamps.Keys);
        costIds.Sort();
        hasher.Add(costIds.Count);
        for (int i = 0; i < costIds.Count; i++)
        {
            CostStamp stamp = CostStamps[costIds[i]];
            hasher.Add(stamp.Id);
            hasher.Add(stamp.AgentTypeId);
            hasher.Add(stamp.Cost);
            hasher.Add(stamp.Width);
            hasher.Add(stamp.Height);
            AddFloat(hasher, stamp.CellSize);
            AddVector3(hasher, stamp.Origin);
            AddByteArray(hasher, stamp.Costs);
        }
    }

    private static void AddAgents(LogicStateHasher hasher)
    {
        var ids = new List<int>(Agents.Keys);
        ids.Sort();
        hasher.Add(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            AgentRuntimeData agent = Agents[ids[i]];
            AgentNavState nav = agent.NavState;
            hasher.Add(agent.Id);
            hasher.Add(agent.CharacterKey);
            AddVector3(hasher, agent.Position);
            AddFloat(hasher, agent.Radius);
            AddFloat(hasher, agent.RegisteredRadius);
            hasher.Add((int)agent.Side);
            hasher.Add(agent.IgnoreAgentCollision);
            hasher.Add(agent.IsLeader);
            hasher.Add(agent.GroupId);
            hasher.Add((int)agent.State);
            hasher.Add(agent.AgentTypeId);
            hasher.Add(agent.HasNavigationIntent);
            hasher.Add(agent.LastAvoidanceActiveFrame);
            hasher.Add(nav.CurrentSectorId);
            AddVector2Int(hasher, nav.CurrentCell);
            AddPathHandle(hasher, nav.PathHandle);
            hasher.Add(nav.CurrentTileKeyHash);
            AddVector3(hasher, nav.CurrentFlowDirection);
            AddVector3(hasher, nav.PathDirection);
            AddVector2Int(hasher, nav.PathDirectionCell);
            hasher.Add(nav.PathDirectionContextHash);
            AddVector3(hasher, nav.DesiredVelocity);
            AddVector3(hasher, nav.PreviousResolvedVelocity);
            AddVector3(hasher, nav.ResolvedVelocity);
            hasher.Add(nav.ResolvedVelocityFrame);
            hasher.Add((int)nav.LastMovementMode);
            AddVector3(hasher, nav.LastGoalWorld);
            hasher.Add(nav.HasGoal);
            hasher.Add(nav.StableGoalX);
            hasher.Add(nav.StableGoalY);
            hasher.Add(nav.StableGoalRawX);
            hasher.Add(nav.StableGoalRawY);
            AddVector3(hasher, nav.StableGoalWorld);
            hasher.Add(nav.StableGoalTargetId);
            hasher.Add(nav.BottleneckLaneAxisMode);
            AddFloat(hasher, nav.BottleneckLaneSign);
        }
    }

    private static void AddNavigationCaches(LogicStateHasher hasher)
    {
        var pathKeys = new List<SectorPathCacheKey>(SectorPathCache.Keys);
        pathKeys.Sort(CompareSectorPathKeys);
        hasher.Add(pathKeys.Count);
        for (int i = 0; i < pathKeys.Count; i++)
        {
            SectorPathCacheKey key = pathKeys[i];
            AddSectorPathKey(hasher, key);
            SectorPathCacheEntry entry = SectorPathCache[key];
            AddIntArray(hasher, entry.SectorIds);
            AddIntArray(hasher, entry.PortalIds);
            hasher.Add(entry.LastUsedFrame);
        }

        var accessKeys = new List<SectorPortalAccessKey>(SectorPortalAccessCache.Keys);
        accessKeys.Sort(CompareSectorPortalAccessKeys);
        hasher.Add(accessKeys.Count);
        for (int i = 0; i < accessKeys.Count; i++)
        {
            SectorPortalAccessKey key = accessKeys[i];
            hasher.Add(key.WorldVersion);
            hasher.Add(key.SectorId);
            hasher.Add(key.PortalId);
            hasher.Add(key.SectorDirtyVersion);
            SectorPortalAccessEntry entry = SectorPortalAccessCache[key];
            AddLongArray(hasher, entry.DeterministicIntegration);
            hasher.Add(entry.LastUsedFrame);
            hasher.Add(entry.IsAnalyticClearSector);
        }

        var tileKeys = new List<FlowTileCacheKey>(FlowTileCache.Keys);
        tileKeys.Sort(CompareFlowTileCacheKeys);
        hasher.Add(tileKeys.Count);
        for (int i = 0; i < tileKeys.Count; i++)
            AddFlowTile(hasher, FlowTileCache[tileKeys[i]]);

        var sharedKeys = new List<SharedGoalFieldKey>(SharedGoalFields.Keys);
        sharedKeys.Sort(CompareSharedGoalKeys);
        hasher.Add(sharedKeys.Count);
        for (int i = 0; i < sharedKeys.Count; i++)
            AddSharedGoalField(hasher, SharedGoalFields[sharedKeys[i]]);
    }

    private static void AddBuildQueues(LogicStateHasher hasher)
    {
        hasher.Add(FlowTileBuildQueue.Count);
        for (LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First; node != null; node = node.Next)
        {
            FlowTileBuildJob job = node.Value;
            AddFlowTileBuildKey(hasher, job.BuildKey);
            hasher.Add((int)job.Stage);
            hasher.Add(job.CellCursor);
            hasher.Add(job.PortalHandoffCursor);
            hasher.Add(job.WaitingForDependency);
            AddPathHandle(hasher, job.HandleSnapshot);
            AddIntegrationSeeds(hasher, job.Seeds);
            AddMinHeap(hasher, job.LineOfSightOpenSet);
            AddMinHeap(hasher, job.IntegrationOpenSet);
            AddFlowTile(hasher, job.Tile);
        }

        hasher.Add(SharedGoalFieldBuildQueue.Count);
        for (LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First; node != null; node = node.Next)
        {
            SharedGoalFieldBuildJob job = node.Value;
            AddSharedGoalKey(hasher, job.Key);
            hasher.Add(job.GoalSectorId);
            hasher.Add(job.GoalX);
            hasher.Add(job.GoalY);
            hasher.Add(job.AgentTypeId);
            hasher.Add((int)job.Stage);
            AddFloatArray(hasher, job.GoalIntegration);
            AddMinHeap(hasher, job.GoalIntegrationOpenSet);
            AddDeterministicCostHeap(hasher, job.PortalOpenSet);
            AddSortedInts(hasher, job.DemandStartSectorIds);
            AddSortedIntDictionary(hasher, job.DemandStartSectorByCellIndex);
            AddSortedInts(hasher, job.SettledPortalNodes);
            AddSharedGoalField(hasher, job.Field);
        }
    }

    private static void AddMovingTargetAnchors(LogicStateHasher hasher)
    {
        var keys = new List<MovingTargetAnchorKey>(MovingTargetAnchors.Keys);
        keys.Sort((left, right) =>
        {
            int result = left.TargetId.CompareTo(right.TargetId);
            if (result != 0) return result;
            result = left.AgentTypeId.CompareTo(right.AgentTypeId);
            return result != 0 ? result : left.IslandId.CompareTo(right.IslandId);
        });
        hasher.Add(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            MovingTargetAnchor anchor = MovingTargetAnchors[keys[i]];
            hasher.Add(anchor.Key.TargetId);
            hasher.Add(anchor.Key.AgentTypeId);
            hasher.Add(anchor.Key.IslandId);
            hasher.Add(anchor.RawGoalX);
            hasher.Add(anchor.RawGoalY);
            hasher.Add(anchor.ActiveGoalX);
            hasher.Add(anchor.ActiveGoalY);
            hasher.Add(anchor.ActiveGoalSectorId);
            hasher.Add(anchor.ActiveWorldVersion);
            AddVector3(hasher, anchor.ActiveGoalWorld);
            hasher.Add(anchor.PendingRawGoalX);
            hasher.Add(anchor.PendingRawGoalY);
            hasher.Add(anchor.PendingGoalX);
            hasher.Add(anchor.PendingGoalY);
            hasher.Add(anchor.PendingGoalSectorId);
            hasher.Add(anchor.PendingWorldVersion);
            AddVector3(hasher, anchor.PendingGoalWorld);
            hasher.Add(anchor.LastUsedFrame);
        }
    }

    private static void AddGoalReservations(LogicStateHasher hasher)
    {
        hasher.Add(_navigationGoalReservationFrame);
        hasher.Add(NavigationGoalReservations.Count);
        for (int i = 0; i < NavigationGoalReservations.Count; i++)
        {
            NavigationGoalReservation reservation = NavigationGoalReservations[i];
            hasher.Add(reservation.SelfId);
            hasher.Add(reservation.Point.x.RawValue);
            hasher.Add(reservation.Point.y.RawValue);
            hasher.Add(reservation.RequiredDistance.RawValue);
        }
    }

    private static void AddBottlenecks(LogicStateHasher hasher)
    {
        var keys = new List<BottleneckRuntimeKey>(Bottlenecks.Keys);
        keys.Sort(CompareBottleneckKeys);
        hasher.Add(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            BottleneckRuntimeState state = Bottlenecks[keys[i]];
            AddBottleneckKey(hasher, keys[i]);
            hasher.Add(state.BottleneckId);
            hasher.Add(state.PortalId);
            hasher.Add(state.CorridorAxisMode);
            AddVector3(hasher, state.CorridorAxis);
            hasher.Add(state.CurrentDirection);
            hasher.Add(state.CurrentOwnerAgentId);
            hasher.Add(state.ConvoyTokenAgentId);
            hasher.Add(state.PriorityOverrideAgentId);
            hasher.Add((int)state.CurrentOwnerState);
            AddFloat(hasher, state.SwitchBlockedUntil);
            hasher.Add(state.OccupiedCount);
            AddFloat(hasher, state.LastOccupiedTime);
            hasher.Add(state.LastFrameTouched);
            AddSortedFloatDictionary(hasher, state.WaitingStartTimes);
            AddSortedFloatDictionary(hasher, state.AgentLaneSigns);
            AddOrderedInts(hasher, state.WaitingAgentsOrdered);
        }
    }

    private static void AddFlowTile(LogicStateHasher hasher, FlowTileCacheEntry tile)
    {
        hasher.Add(tile != null);
        if (tile == null)
            return;
        AddFlowTileCacheKey(hasher, tile.Key);
        hasher.Add(tile.StartX);
        hasher.Add(tile.StartY);
        hasher.Add(tile.Width);
        hasher.Add(tile.Height);
        AddFloatArray(hasher, tile.Integration);
        AddByteArray(hasher, tile.FlowFieldValues);
        AddIntArray(hasher, tile.DeterministicIntegrationCosts);
        AddByteArray(hasher, tile.DeterministicFlowDirectionIndices);
        AddVector2IntArray(hasher, tile.GoalCells);
        hasher.Add(tile.LastUsedFrame);
        hasher.Add(tile.LastReferencedFrame);
        hasher.Add(tile.ActiveReferenceCount);
        hasher.Add(tile.IntegrationReleased);
        hasher.Add(tile.UsesClearFlowDescriptor);
    }

    private static void AddSharedGoalField(LogicStateHasher hasher, SharedGoalField field)
    {
        hasher.Add(field != null);
        if (field == null)
            return;
        AddSharedGoalKey(hasher, field.Key);
        hasher.Add(field.GoalSectorId);
        hasher.Add(field.GoalX);
        hasher.Add(field.GoalY);
        AddSortedLongDictionary(hasher, field.NodeCosts);
        AddSortedIntDictionary(hasher, field.NextNodeTowardGoal);
        AddSortedInts(hasher, field.CompletedDemandStartSectorIds);
        AddSortedInts(hasher, field.CompletedDemandStartCellIndices);
        hasher.Add(field.LastUsedFrame);
    }

    private static void AddPathHandle(LogicStateHasher hasher, PathHandle handle)
    {
        hasher.Add(handle != null);
        if (handle == null)
            return;
        hasher.Add(handle.HandleId);
        hasher.Add(handle.WorldVersion);
        hasher.Add(handle.GoalX);
        hasher.Add(handle.GoalY);
        AddIntArray(hasher, handle.SectorIds);
        AddIntArray(hasher, handle.PortalIds);
        hasher.Add(handle.CurrentSectorIndex);
        hasher.Add(handle.BuildSource);
    }

    private static void AddFlowTileBuildKey(LogicStateHasher hasher, FlowTileBuildKey key)
    {
        AddFlowTileCacheKey(hasher, key.CacheKey);
        hasher.Add(key.SectorPathIndex);
        hasher.Add(key.GoalX);
        hasher.Add(key.GoalY);
        hasher.Add(key.AgentTypeId);
    }

    private static void AddFlowTileCacheKey(LogicStateHasher hasher, FlowTileCacheKey key)
    {
        hasher.Add(key.WorldVersion);
        hasher.Add(key.SectorId);
        hasher.Add((int)key.GoalKind);
        hasher.Add(key.GoalId);
        hasher.Add(key.DownstreamGoalHint);
        hasher.Add(key.FinalGoalIndex);
        hasher.Add(key.AgentTypeId);
        hasher.Add(key.DirtyVersion);
    }

    private static void AddSharedGoalKey(LogicStateHasher hasher, SharedGoalFieldKey key)
    {
        hasher.Add(key.WorldVersion);
        hasher.Add(key.AgentTypeId);
        hasher.Add(key.GoalSectorId);
        hasher.Add(key.GoalCellIndex);
        hasher.Add(key.GoalSectorDirtyVersion);
    }

    private static void AddSectorPathKey(LogicStateHasher hasher, SectorPathCacheKey key)
    {
        hasher.Add(key.WorldVersion);
        hasher.Add(key.StartSectorId);
        hasher.Add(key.StartCellIndex);
        hasher.Add(key.GoalSectorId);
        hasher.Add(key.GoalCellIndex);
        hasher.Add(key.StartSectorDirtyVersion);
        hasher.Add(key.GoalSectorDirtyVersion);
    }

    private static void AddBottleneckKey(LogicStateHasher hasher, BottleneckRuntimeKey key)
    {
        hasher.Add(key.WorldVersion);
        hasher.Add(key.AgentTypeId);
        hasher.Add(key.LocalBottleneckId);
        hasher.Add(key.Kind);
        hasher.Add(key.AxisMode);
        hasher.Add(key.SpanMin);
        hasher.Add(key.SpanMax);
        hasher.Add(key.RunMin);
        hasher.Add(key.RunMax);
    }

    private static int CompareSectorPathKeys(SectorPathCacheKey left, SectorPathCacheKey right)
    {
        int result = left.WorldVersion.CompareTo(right.WorldVersion);
        if (result != 0) return result;
        result = left.StartSectorId.CompareTo(right.StartSectorId);
        if (result != 0) return result;
        result = left.StartCellIndex.CompareTo(right.StartCellIndex);
        if (result != 0) return result;
        result = left.GoalSectorId.CompareTo(right.GoalSectorId);
        if (result != 0) return result;
        result = left.GoalCellIndex.CompareTo(right.GoalCellIndex);
        if (result != 0) return result;
        result = left.StartSectorDirtyVersion.CompareTo(right.StartSectorDirtyVersion);
        return result != 0 ? result : left.GoalSectorDirtyVersion.CompareTo(right.GoalSectorDirtyVersion);
    }

    private static int CompareSectorPortalAccessKeys(SectorPortalAccessKey left, SectorPortalAccessKey right)
    {
        int result = left.WorldVersion.CompareTo(right.WorldVersion);
        if (result != 0) return result;
        result = left.SectorId.CompareTo(right.SectorId);
        if (result != 0) return result;
        result = left.PortalId.CompareTo(right.PortalId);
        return result != 0 ? result : left.SectorDirtyVersion.CompareTo(right.SectorDirtyVersion);
    }

    private static int CompareSharedGoalKeys(SharedGoalFieldKey left, SharedGoalFieldKey right)
    {
        int result = left.WorldVersion.CompareTo(right.WorldVersion);
        if (result != 0) return result;
        result = left.AgentTypeId.CompareTo(right.AgentTypeId);
        if (result != 0) return result;
        result = left.GoalSectorId.CompareTo(right.GoalSectorId);
        if (result != 0) return result;
        result = left.GoalCellIndex.CompareTo(right.GoalCellIndex);
        return result != 0 ? result : left.GoalSectorDirtyVersion.CompareTo(right.GoalSectorDirtyVersion);
    }

    private static int CompareBottleneckKeys(BottleneckRuntimeKey left, BottleneckRuntimeKey right)
    {
        int result = left.WorldVersion.CompareTo(right.WorldVersion);
        if (result != 0) return result;
        result = left.AgentTypeId.CompareTo(right.AgentTypeId);
        if (result != 0) return result;
        result = left.LocalBottleneckId.CompareTo(right.LocalBottleneckId);
        if (result != 0) return result;
        result = left.Kind.CompareTo(right.Kind);
        if (result != 0) return result;
        result = left.AxisMode.CompareTo(right.AxisMode);
        if (result != 0) return result;
        result = left.SpanMin.CompareTo(right.SpanMin);
        if (result != 0) return result;
        result = left.SpanMax.CompareTo(right.SpanMax);
        if (result != 0) return result;
        result = left.RunMin.CompareTo(right.RunMin);
        return result != 0 ? result : left.RunMax.CompareTo(right.RunMax);
    }

    private static void AddIntegrationSeeds(LogicStateHasher hasher, IntegrationSeed[] values)
    {
        int count = values?.Length ?? 0;
        hasher.Add(count);
        for (int i = 0; i < count; i++)
        {
            AddVector2Int(hasher, values[i].Cell);
            AddFloat(hasher, values[i].Cost);
        }
    }

    private static void AddMinHeap(LogicStateHasher hasher, MinHeap heap)
    {
        hasher.Add(heap != null);
        if (heap == null)
            return;
        heap.WriteDeterministicState(hasher);
    }

    private static void AddDeterministicCostHeap(LogicStateHasher hasher, DeterministicCostHeap heap)
    {
        hasher.Add(heap != null);
        if (heap == null)
            return;
        heap.WriteDeterministicState(hasher);
    }

    private static void AddQueue(LogicStateHasher hasher, Queue<int> queue)
    {
        hasher.Add(queue != null);
        if (queue == null)
            return;
        hasher.Add(queue.Count);
        foreach (int value in queue)
            hasher.Add(value);
    }

    private static void AddSortedInts(LogicStateHasher hasher, IEnumerable<int> values)
    {
        if (values == null)
        {
            hasher.Add(-1);
            return;
        }
        var sorted = new List<int>(values);
        sorted.Sort();
        hasher.Add(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
            hasher.Add(sorted[i]);
    }

    private static void AddOrderedInts(LogicStateHasher hasher, IList<int> values)
    {
        int count = values?.Count ?? 0;
        hasher.Add(count);
        for (int i = 0; i < count; i++)
            hasher.Add(values[i]);
    }

    private static void AddSortedIntDictionary(LogicStateHasher hasher, IDictionary<int, int> values)
    {
        if (values == null)
        {
            hasher.Add(-1);
            return;
        }
        var keys = new List<int>(values.Keys);
        keys.Sort();
        hasher.Add(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            hasher.Add(keys[i]);
            hasher.Add(values[keys[i]]);
        }
    }

    private static void AddSortedLongDictionary(LogicStateHasher hasher, IDictionary<int, long> values)
    {
        if (values == null)
        {
            hasher.Add(-1);
            return;
        }
        var keys = new List<int>(values.Keys);
        keys.Sort();
        hasher.Add(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            hasher.Add(keys[i]);
            hasher.Add(values[keys[i]]);
        }
    }

    private static void AddSortedFloatDictionary(LogicStateHasher hasher, IDictionary<int, float> values)
    {
        if (values == null)
        {
            hasher.Add(-1);
            return;
        }
        var keys = new List<int>(values.Keys);
        keys.Sort();
        hasher.Add(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            hasher.Add(keys[i]);
            AddFloat(hasher, values[keys[i]]);
        }
    }

    private static void AddBoolArray(LogicStateHasher hasher, bool[] values)
    {
        int count = values?.Length ?? 0;
        hasher.Add(count);
        for (int i = 0; i < count; i++)
            hasher.Add(values[i]);
    }

    private static void AddByteArray(LogicStateHasher hasher, byte[] values)
    {
        int count = values?.Length ?? 0;
        hasher.Add(count);
        for (int i = 0; i < count; i++)
            hasher.Add(values[i]);
    }

    private static void AddUShortArray(LogicStateHasher hasher, ushort[] values)
    {
        int count = values?.Length ?? 0;
        hasher.Add(count);
        for (int i = 0; i < count; i++)
            hasher.Add((uint)values[i]);
    }

    private static void AddIntArray(LogicStateHasher hasher, int[] values)
    {
        int count = values?.Length ?? 0;
        hasher.Add(count);
        for (int i = 0; i < count; i++)
            hasher.Add(values[i]);
    }

    private static void AddLongArray(LogicStateHasher hasher, long[] values)
    {
        int count = values?.Length ?? 0;
        hasher.Add(count);
        for (int i = 0; i < count; i++)
            hasher.Add(values[i]);
    }

    private static void AddFloatArray(LogicStateHasher hasher, float[] values)
    {
        int count = values?.Length ?? 0;
        hasher.Add(count);
        for (int i = 0; i < count; i++)
            AddFloat(hasher, values[i]);
    }

    private static void AddVector2IntArray(LogicStateHasher hasher, Vector2Int[] values)
    {
        int count = values?.Length ?? 0;
        hasher.Add(count);
        for (int i = 0; i < count; i++)
            AddVector2Int(hasher, values[i]);
    }

    private static void AddByteArrayArray(LogicStateHasher hasher, byte[][] values)
    {
        int count = values?.Length ?? 0;
        hasher.Add(count);
        for (int i = 0; i < count; i++)
            AddByteArray(hasher, values[i]);
    }

    private static void AddVector3Array(LogicStateHasher hasher, Vector3[] values)
    {
        int count = values?.Length ?? 0;
        hasher.Add(count);
        for (int i = 0; i < count; i++)
            AddVector3(hasher, values[i]);
    }

    private static void AddSortedPortalSignatures(
        LogicStateHasher hasher,
        IDictionary<PortalSignature, int> values)
    {
        if (values == null)
        {
            hasher.Add(-1);
            return;
        }

        var entries = new List<KeyValuePair<PortalSignature, int>>(values);
        entries.Sort((left, right) =>
        {
            int result = left.Key.MinSectorId.CompareTo(right.Key.MinSectorId);
            if (result != 0) return result;
            result = left.Key.MaxSectorId.CompareTo(right.Key.MaxSectorId);
            if (result != 0) return result;
            result = left.Key.IsVerticalBoundary.CompareTo(right.Key.IsVerticalBoundary);
            if (result != 0) return result;
            result = left.Key.Boundary.CompareTo(right.Key.Boundary);
            if (result != 0) return result;
            result = left.Key.FirstAxis.CompareTo(right.Key.FirstAxis);
            if (result != 0) return result;
            result = left.Key.LastAxis.CompareTo(right.Key.LastAxis);
            return result != 0 ? result : left.Key.WidthCells.CompareTo(right.Key.WidthCells);
        });
        hasher.Add(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            PortalSignature key = entries[i].Key;
            hasher.Add(key.MinSectorId);
            hasher.Add(key.MaxSectorId);
            hasher.Add(key.IsVerticalBoundary);
            hasher.Add(key.Boundary);
            hasher.Add(key.FirstAxis);
            hasher.Add(key.LastAxis);
            hasher.Add(key.WidthCells);
            hasher.Add(entries[i].Value);
        }
    }

    private static void AddVector2Int(LogicStateHasher hasher, Vector2Int value)
    {
        hasher.Add(value.x);
        hasher.Add(value.y);
    }

    private static void AddVector3(LogicStateHasher hasher, Vector3 value)
    {
        AddFloat(hasher, value.x);
        AddFloat(hasher, value.y);
        AddFloat(hasher, value.z);
    }

    private static void AddFloat(LogicStateHasher hasher, float value)
    {
        hasher.Add(BitConverter.SingleToInt32Bits(value));
    }
}
