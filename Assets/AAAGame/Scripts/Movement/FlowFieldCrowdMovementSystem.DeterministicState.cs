using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class FlowFieldCrowdMovementSystem
{
    public readonly struct RuntimeMemoryCensus
    {
        public RuntimeMemoryCensus(
            int portalAccessCount,
            long portalAccessPayloadBytes,
            int sectorPathCount,
            long sectorPathPayloadBytes,
            int flowTileCount,
            long flowTilePayloadBytes,
            int sharedGoalCount,
            long sharedGoalEntryCount,
            int pendingSharedGoalCount,
            long pendingSharedGoalEntryCount,
            int startPortalChoiceCount,
            int corridorLookupCount,
            long corridorEntryCount,
            int agentSpatialBucketCount,
            long agentSpatialBucketCapacity,
            int combatTargetSlotCount,
            long combatTargetSlotEntryCount,
            int navigationGoalReservationCapacity,
            int distanceEstimateCellCapacity,
            int runtimeArrayPoolArrayCount,
            long runtimeArrayPoolPayloadBytes,
            int worldCount,
            long worldArrayPayloadBytes)
        {
            PortalAccessCount = portalAccessCount;
            PortalAccessPayloadBytes = portalAccessPayloadBytes;
            SectorPathCount = sectorPathCount;
            SectorPathPayloadBytes = sectorPathPayloadBytes;
            FlowTileCount = flowTileCount;
            FlowTilePayloadBytes = flowTilePayloadBytes;
            SharedGoalCount = sharedGoalCount;
            SharedGoalEntryCount = sharedGoalEntryCount;
            PendingSharedGoalCount = pendingSharedGoalCount;
            PendingSharedGoalEntryCount = pendingSharedGoalEntryCount;
            StartPortalChoiceCount = startPortalChoiceCount;
            CorridorLookupCount = corridorLookupCount;
            CorridorEntryCount = corridorEntryCount;
            AgentSpatialBucketCount = agentSpatialBucketCount;
            AgentSpatialBucketCapacity = agentSpatialBucketCapacity;
            CombatTargetSlotCount = combatTargetSlotCount;
            CombatTargetSlotEntryCount = combatTargetSlotEntryCount;
            NavigationGoalReservationCapacity = navigationGoalReservationCapacity;
            DistanceEstimateCellCapacity = distanceEstimateCellCapacity;
            RuntimeArrayPoolArrayCount = runtimeArrayPoolArrayCount;
            RuntimeArrayPoolPayloadBytes = runtimeArrayPoolPayloadBytes;
            WorldCount = worldCount;
            WorldArrayPayloadBytes = worldArrayPayloadBytes;
        }

        public int PortalAccessCount { get; }
        public long PortalAccessPayloadBytes { get; }
        public int SectorPathCount { get; }
        public long SectorPathPayloadBytes { get; }
        public int FlowTileCount { get; }
        public long FlowTilePayloadBytes { get; }
        public int SharedGoalCount { get; }
        public long SharedGoalEntryCount { get; }
        public int PendingSharedGoalCount { get; }
        public long PendingSharedGoalEntryCount { get; }
        public int StartPortalChoiceCount { get; }
        public int CorridorLookupCount { get; }
        public long CorridorEntryCount { get; }
        public int AgentSpatialBucketCount { get; }
        public long AgentSpatialBucketCapacity { get; }
        public int CombatTargetSlotCount { get; }
        public long CombatTargetSlotEntryCount { get; }
        public int NavigationGoalReservationCapacity { get; }
        public int DistanceEstimateCellCapacity { get; }
        public int RuntimeArrayPoolArrayCount { get; }
        public long RuntimeArrayPoolPayloadBytes { get; }
        public int WorldCount { get; }
        public long WorldArrayPayloadBytes { get; }

        public override string ToString()
        {
            return
                $"portal={PortalAccessCount}/{PortalAccessPayloadBytes}, " +
                $"sectorPath={SectorPathCount}/{SectorPathPayloadBytes}, " +
                $"flow={FlowTileCount}/{FlowTilePayloadBytes}, " +
                $"shared={SharedGoalCount}/{SharedGoalEntryCount}, pendingShared={PendingSharedGoalCount}/{PendingSharedGoalEntryCount}, " +
                $"startPortal={StartPortalChoiceCount}, corridor={CorridorLookupCount}/{CorridorEntryCount}, " +
                $"spatial={AgentSpatialBucketCount}/{AgentSpatialBucketCapacity}, combatSlots={CombatTargetSlotCount}/{CombatTargetSlotEntryCount}, " +
                $"goalWorkspace={NavigationGoalReservationCapacity}/{DistanceEstimateCellCapacity}, " +
                $"arrayPool={RuntimeArrayPoolArrayCount}/{RuntimeArrayPoolPayloadBytes}, world={WorldCount}/{WorldArrayPayloadBytes}";
        }
    }

    private const ulong DeterministicHashCheckpointIntervalFrames = 30;
    private static ulong _deterministicHashCheckpointFrame;
    private static ulong _deterministicHashCheckpointValue;
    private static ulong _committedWorldSetHash;
    private static ulong _deterministicFlowTileAuthorityContentHash;
    private static ulong _sectorPathAuthorityContentHash;
    private static ulong _sectorPortalAccessAuthorityContentHash;
    private static ulong _sharedGoalFieldAuthorityContentHash;
    private static ulong _costStampAuthorityContentHash;
    private static int _sharedGoalBuildJobAuthorityHashRefreshCount;
    private static long _sharedGoalBuildJobAuthorityHashVisitedEntryCount;
    private static readonly List<int> AuthorityWorldKeys = new List<int>();
    private static readonly List<int> AuthorityCircleObstacleIds = new List<int>();
    private static readonly List<int> AuthorityBoxObstacleIds = new List<int>();
    private static readonly List<int> AuthorityAgentIds = new List<int>();
    private static readonly List<MovingTargetAnchorKey> AuthorityMovingTargetKeys = new List<MovingTargetAnchorKey>();
    private static readonly List<FixedPortalOwnerKey> AuthorityFixedPortalOwnerKeys = new List<FixedPortalOwnerKey>();
    private static readonly List<int> AuthorityCorridorWorldVersions = new List<int>();
    private static readonly List<int> AuthorityWorkingWorldPortalIds = new List<int>();
    private static readonly List<int> SortedIntValues = new List<int>();
    private static readonly Comparison<MovingTargetAnchorKey> AuthorityMovingTargetKeyComparison = CompareMovingTargetAnchorKeys;
    private static readonly Comparison<FixedPortalOwnerKey> AuthorityFixedPortalOwnerKeyComparison = CompareFixedPortalOwnerKeys;
    private static readonly Comparison<int> AuthorityIntComparison =
        (left, right) => left.CompareTo(right);
    private static readonly HashSet<FlowTileCacheKey> AuthorityPendingFlowTileKeys = new HashSet<FlowTileCacheKey>();
    private static readonly HashSet<SharedGoalFieldKey> AuthorityPendingSharedGoalKeys = new HashSet<SharedGoalFieldKey>();
    private static readonly LogicStateHasher SectorPortalAccessContentHasher = new LogicStateHasher();

    public static int DiagnosticCheckpointRefreshCount { get; private set; }

    public static RuntimeMemoryCensus CaptureRuntimeMemoryCensus()
    {
        long portalAccessPayloadBytes = 0;
        foreach (SectorPortalAccessEntry entry in SectorPortalAccessCache.Values)
        {
            if (entry == null)
                throw new InvalidOperationException("Flow memory census encountered a null portal-access entry.");
            portalAccessPayloadBytes = checked(
                portalAccessPayloadBytes
                + GetArrayPayloadBytes(entry.DeterministicIntegration, sizeof(long)));
        }

        long sectorPathPayloadBytes = 0;
        foreach (SectorPathCacheEntry entry in SectorPathCache.Values)
        {
            if (entry == null)
                throw new InvalidOperationException("Flow memory census encountered a null sector-path entry.");
            sectorPathPayloadBytes = checked(
                sectorPathPayloadBytes
                + GetArrayPayloadBytes(entry.SectorIds, sizeof(int))
                + GetArrayPayloadBytes(entry.PortalIds, sizeof(int)));
        }

        long flowTilePayloadBytes = 0;
        foreach (FlowTileCacheEntry tile in DeterministicFlowTileCache.Values)
        {
            if (tile == null)
                throw new InvalidOperationException("Flow memory census encountered a null deterministic tile.");
            flowTilePayloadBytes = checked(
                flowTilePayloadBytes
                + GetArrayPayloadBytes(tile.FlowFieldValues, sizeof(byte))
                + GetArrayPayloadBytes(tile.DeterministicIntegrationCosts, sizeof(int))
                + GetArrayPayloadBytes(tile.DeterministicFlowDirectionIndices, sizeof(byte))
                + GetArrayPayloadBytes(tile.GoalCells, sizeof(int) * 2)
                + GetArrayPayloadBytes(tile.DebugIntegrationPayload?.Values, sizeof(float)));
        }

        long sharedGoalEntryCount = 0;
        foreach (SharedGoalField field in SharedGoalFields.Values)
        {
            if (field == null)
                throw new InvalidOperationException("Flow memory census encountered a null shared-goal field.");
            sharedGoalEntryCount = checked(
                sharedGoalEntryCount
                + field.NodeCosts.Count
                + field.NextNodeTowardGoal.Count
                + field.FirstCrossingPortalByStartNode.Count
                + field.CompletedDemandStartSectorIds.Count
                + field.CompletedDemandStartCellIndices.Count);
        }

        long pendingSharedGoalEntryCount = 0;
        for (LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First; node != null; node = node.Next)
        {
            SharedGoalFieldBuildJob job = node.Value
                ?? throw new InvalidOperationException("Flow memory census encountered a null shared-goal job.");
            pendingSharedGoalEntryCount = checked(
                pendingSharedGoalEntryCount
                + (job.PortalOpenSet?.Count ?? 0)
                + (job.DemandStartSectorIds?.Count ?? 0)
                + (job.DemandStartSectorByCellIndex?.Count ?? 0)
                + (job.SettledPortalNodes?.Count ?? 0));
            if (job.Field != null)
            {
                pendingSharedGoalEntryCount = checked(
                    pendingSharedGoalEntryCount
                    + job.Field.NodeCosts.Count
                    + job.Field.NextNodeTowardGoal.Count
                    + job.Field.FirstCrossingPortalByStartNode.Count
                    + job.Field.CompletedDemandStartSectorIds.Count
                    + job.Field.CompletedDemandStartCellIndices.Count);
            }
        }

        long corridorEntryCount = 0;
        foreach (FixedCorridorLookup lookup in FixedCorridorLookupByWorldVersion.Values)
        {
            if (lookup == null)
                throw new InvalidOperationException("Flow memory census encountered a null corridor lookup.");
            corridorEntryCount = checked(
                corridorEntryCount
                + lookup.OrientationsByCell.Count
                + lookup.CandidateByCell.Count
                + lookup.ComponentIdByCell.Count
                + lookup.DescriptorByComponentId.Count
                + lookup.CompletedComponentIds.Count
                + lookup.ResolvedNonCorridorCells.Count
                + lookup.PendingStartIndices.Count
                + (lookup.ActiveBuildJob?.Queue.Count ?? 0)
                + (lookup.ActiveBuildJob?.ExternalEndpointCells.Count ?? 0)
                + (lookup.ActiveBuildJob?.TerminalEndpointCells.Count ?? 0));
        }

        long agentSpatialBucketCapacity = 0;
        foreach (List<AgentRuntimeData> bucket in AgentSpatialBuckets.Values)
        {
            if (bucket == null)
                throw new InvalidOperationException("Flow memory census encountered a null agent spatial bucket.");
            agentSpatialBucketCapacity = checked(agentSpatialBucketCapacity + bucket.Capacity);
        }

        long combatTargetSlotEntryCount = 0;
        foreach (CombatTargetSlotEntry entry in CombatTargetSlotCache.Values)
        {
            if (entry == null)
                throw new InvalidOperationException("Flow memory census encountered a null combat target slot entry.");
            combatTargetSlotEntryCount = checked(
                combatTargetSlotEntryCount
                + (entry.Points?.Length ?? 0)
                + (entry.CellX?.Length ?? 0)
                + (entry.CellY?.Length ?? 0)
                + (entry.IslandIds?.Length ?? 0));
        }

        int runtimeArrayPoolArrayCount = 0;
        long runtimeArrayPoolPayloadBytes = 0;
        AddArrayPoolCensus(IntegrationArrayPool, sizeof(float), ref runtimeArrayPoolArrayCount, ref runtimeArrayPoolPayloadBytes);
        AddArrayPoolCensus(PortalAccessIntegrationArrayPool, sizeof(long), ref runtimeArrayPoolArrayCount, ref runtimeArrayPoolPayloadBytes);
        AddArrayPoolCensus(BoolArrayPool, sizeof(byte), ref runtimeArrayPoolArrayCount, ref runtimeArrayPoolPayloadBytes);
        AddArrayPoolCensus(ByteArrayPool, sizeof(byte), ref runtimeArrayPoolArrayCount, ref runtimeArrayPoolPayloadBytes);
        AddArrayPoolCensus(IntArrayPool, sizeof(int), ref runtimeArrayPoolArrayCount, ref runtimeArrayPoolPayloadBytes);

        int worldCount = 0;
        long worldArrayPayloadBytes = 0;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state == null)
                throw new InvalidOperationException("Flow memory census encountered a null world state.");
            AddWorldArrayCensus(state.World, ref worldCount, ref worldArrayPayloadBytes);
            AddWorldArrayCensus(state.BuildJob?.WorkingWorld, ref worldCount, ref worldArrayPayloadBytes);
            AddWorldArrayCensus(state.RuntimeDirtyJob?.WorkingWorld, ref worldCount, ref worldArrayPayloadBytes);
        }

        return new RuntimeMemoryCensus(
            SectorPortalAccessCache.Count,
            portalAccessPayloadBytes,
            SectorPathCache.Count,
            sectorPathPayloadBytes,
            DeterministicFlowTileCache.Count,
            flowTilePayloadBytes,
            SharedGoalFields.Count,
            sharedGoalEntryCount,
            SharedGoalFieldBuildQueue.Count,
            pendingSharedGoalEntryCount,
            StartPortalChoiceCache.Count,
            FixedCorridorLookupByWorldVersion.Count,
            corridorEntryCount,
            AgentSpatialBuckets.Count,
            agentSpatialBucketCapacity,
            CombatTargetSlotCache.Count,
            combatTargetSlotEntryCount,
            NavigationGoalReservations.Capacity,
            DistanceEstimateCosts.Length,
            runtimeArrayPoolArrayCount,
            runtimeArrayPoolPayloadBytes,
            worldCount,
            worldArrayPayloadBytes);
    }

    private static long GetArrayPayloadBytes(Array array, int elementSize)
    {
        return array == null ? 0L : checked((long)array.Length * elementSize);
    }

    private static void AddArrayPoolCensus<T>(
        Dictionary<int, Stack<T[]>> pool,
        int elementSize,
        ref int arrayCount,
        ref long payloadBytes)
    {
        foreach (Stack<T[]> arrays in pool.Values)
        {
            foreach (T[] array in arrays)
            {
                if (array == null)
                    throw new InvalidOperationException("Flow memory census encountered a null pooled array.");
                arrayCount = checked(arrayCount + 1);
                payloadBytes = checked(payloadBytes + (long)array.Length * elementSize);
            }
        }
    }

    private static void AddWorldArrayCensus(
        NavigationWorld world,
        ref int worldCount,
        ref long payloadBytes)
    {
        if (world == null)
            return;

        worldCount = checked(worldCount + 1);
        payloadBytes = checked(
            payloadBytes
            + GetArrayPayloadBytes(world.BaseWalkableMask, sizeof(byte))
            + GetArrayPayloadBytes(world.BaseNeighborTraversalMask, sizeof(byte))
            + GetArrayPayloadBytes(world.SourceCostField, sizeof(byte))
            + GetArrayPayloadBytes(world.WalkableMask, sizeof(byte))
            + GetArrayPayloadBytes(world.CostField, sizeof(byte))
            + GetArrayPayloadBytes(world.CellNavAnchors, sizeof(float) * 3)
            + GetArrayPayloadBytes(world.CellNavAnchorsFixedXZ, sizeof(long) * 2)
            + GetArrayPayloadBytes(world.NeighborTraversalMask, sizeof(byte))
            + GetArrayPayloadBytes(world.IslandIds, sizeof(int)));
        if (world.SectorCostFields != null)
        {
            for (int i = 0; i < world.SectorCostFields.Length; i++)
                payloadBytes = checked(payloadBytes + GetArrayPayloadBytes(world.SectorCostFields[i], sizeof(byte)));
        }
        if (world.Sectors != null)
        {
            for (int i = 0; i < world.Sectors.Length; i++)
                payloadBytes = checked(payloadBytes + GetArrayPayloadBytes(world.Sectors[i]?.LocalComponentIds, sizeof(int)));
        }
        if (world.Portals != null)
        {
            for (int i = 0; i < world.Portals.Length; i++)
            {
                PortalData portal = world.Portals[i];
                if (portal == null)
                    continue;
                payloadBytes = checked(
                    payloadBytes
                    + GetArrayPayloadBytes(portal.CellsA, sizeof(int) * 2)
                    + GetArrayPayloadBytes(portal.CellsB, sizeof(int) * 2));
            }
        }
    }


    // Full navigation diagnostics include authored Unity boundary and float shadow fields.
    // This hash is useful for same-runtime investigation, but is not an authority replay hash.
    public static ulong CaptureDiagnosticStateHash()
    {
        var hasher = new LogicStateHasher();
        WriteDiagnosticState(hasher);
        return hasher.Hash;
    }

    public static void WriteDiagnosticState(LogicStateHasher hasher)
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
    }

    public static void WriteDiagnosticCheckpointState(LogicStateHasher hasher, ulong frame)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));
        if (frame == 0)
            throw new ArgumentOutOfRangeException(nameof(frame), "Navigation deterministic checkpoint requires a positive logic frame.");

        if (_deterministicHashCheckpointFrame == 0
            || frame < _deterministicHashCheckpointFrame
            || frame - _deterministicHashCheckpointFrame >= DeterministicHashCheckpointIntervalFrames)
        {
            _deterministicHashCheckpointValue = CaptureDiagnosticStateHash();
            _deterministicHashCheckpointFrame = frame;
            DiagnosticCheckpointRefreshCount++;
        }

        hasher.Add(0x4E415643484B5054UL);
        hasher.Add(_deterministicHashCheckpointFrame);
        hasher.Add(_deterministicHashCheckpointValue);
        hasher.Add(DeterministicHashCheckpointIntervalFrames);
        AddDeterministicCheckpointLiveState(hasher);
    }

    public static void WriteDeterministicFrameDigest(LogicStateHasher hasher)
    {
        WriteDeterministicFrameDigestWithCheckpoints(hasher);
    }

    public static LogicNavigationAuthorityDigest WriteDeterministicFrameDigestWithCheckpoints(
        LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));
        ValidateAuthorityDigestCallBoundary();

        hasher.Add(0x4E41564652414D45UL);
        hasher.Add(_committedWorldSetHash);
        AddAuthorityNavigationConfig(hasher);
        ulong worldAndConfigHash = hasher.Hash;
        AddDeterministicCheckpointLiveState(hasher);
        ulong checkpointLiveStateHash = hasher.Hash;
        AddAuthorityWorldProgress(hasher);
        ulong worldProgressHash = hasher.Hash;
        AddAuthorityRuntimeObstacles(hasher);
        ulong runtimeObstaclesHash = hasher.Hash;
        AddAuthorityAgents(hasher);
        ulong agentsHash = hasher.Hash;
        AddAuthorityNavigationCaches(hasher);
        ulong cachesHash = hasher.Hash;
        AddAuthorityDeterministicFlowTiles(hasher);
        ulong flowTilesHash = hasher.Hash;
        AddAuthorityFlowTileBuildQueue(hasher);
        ulong flowTileBuildQueueHash = hasher.Hash;
        AddAuthoritySharedGoalFieldBuildQueue(hasher);
        ulong sharedGoalBuildQueueHash = hasher.Hash;
        AddAuthorityMovingTargetAnchors(hasher);
        ulong movingTargetAnchorsHash = hasher.Hash;
        AddGoalReservations(hasher);
        ulong goalReservationsHash = hasher.Hash;
        AddAuthorityFixedPortalOwners(hasher);
        ulong fixedPortalOwnersHash = hasher.Hash;
        AddAuthorityFixedCorridorBuilds(hasher);
        return new LogicNavigationAuthorityDigest(
            worldAndConfigHash,
            checkpointLiveStateHash,
            worldProgressHash,
            runtimeObstaclesHash,
            agentsHash,
            cachesHash,
            flowTilesHash,
            flowTileBuildQueueHash,
            sharedGoalBuildQueueHash,
            movingTargetAnchorsHash,
            goalReservationsHash,
            fixedPortalOwnersHash,
            hasher.Hash);
    }

    private static void AddAuthorityNavigationConfig(LogicStateHasher hasher)
    {
        hasher.Add(0x4E41564155544846UL);
        hasher.Add(Config.RequireAuthoredNavigationSource);
        hasher.Add(Config.SectorSizeInCells);
        hasher.Add(Config.PortalNarrowWidthCells);
        hasher.Add(Config.PortalMaxWindowWidthCells);
        hasher.Add(Config.FlowTileCacheLimit);
        hasher.Add(Config.WorldBuildOperationQuota);
        hasher.Add(Config.RuntimeRebuildOperationQuota);
        hasher.Add(Config.DeterministicFlowTileCommitQuota);
        hasher.Add(Config.SharedGoalBuildOperationQuota);
    }

    private static void ResetDeterministicHashCheckpoint()
    {
        _deterministicHashCheckpointFrame = 0;
        _deterministicHashCheckpointValue = 0;
        _committedWorldSetHash = 0;
        _deterministicFlowTileAuthorityContentHash = 0;
        _sectorPathAuthorityContentHash = 0;
        _sectorPortalAccessAuthorityContentHash = 0;
        _sharedGoalFieldAuthorityContentHash = 0;
        _costStampAuthorityContentHash = 0;
        _sharedGoalBuildJobAuthorityHashRefreshCount = 0;
        _sharedGoalBuildJobAuthorityHashVisitedEntryCount = 0;
        DiagnosticCheckpointRefreshCount = 0;
    }

    private static void AddAuthorityNavigationCaches(LogicStateHasher hasher)
    {
        hasher.Add(0x4E41564155544843UL);
        AddAuthoritySectorPathCache(hasher);
        AddAuthoritySectorPortalAccessCache(hasher);
        AddAuthoritySharedGoalFieldCache(hasher);
    }

    private static void AddAuthoritySectorPathCache(LogicStateHasher hasher)
    {
        hasher.Add(SectorPathCache.Count);
        hasher.Add(_sectorPathAuthorityContentHash);
        hasher.Add(ComputeSectorPathRuntimeSetHash());
    }

    private static ulong ComputeSectorPathRuntimeSetHash()
    {
        ulong runtimeSetHash = 0;
        foreach (KeyValuePair<SectorPathCacheKey, SectorPathCacheEntry> pair in SectorPathCache)
        {
            SectorPathCacheEntry entry = pair.Value;
            if (entry == null || !entry.HasAuthorityContentHash)
                throw new InvalidOperationException("Navigation authority digest encountered an unhashed sector path cache entry.");

            ulong token = 14695981039346656037UL;
            AddSectorPathAuthorityToken(ref token, pair.Key);
            AddAuthorityToken(ref token, entry.LastUsedFrame);
            runtimeSetHash ^= token;
        }
        return runtimeSetHash;
    }

    private static void AddAuthoritySectorPortalAccessCache(LogicStateHasher hasher)
    {
        hasher.Add(SectorPortalAccessCache.Count);
        hasher.Add(_sectorPortalAccessAuthorityContentHash);
        hasher.Add(ComputeSectorPortalAccessRuntimeSetHash());
    }

    private static ulong ComputeSectorPortalAccessRuntimeSetHash()
    {
        ulong runtimeSetHash = 0;
        foreach (KeyValuePair<SectorPortalAccessKey, SectorPortalAccessEntry> pair in SectorPortalAccessCache)
        {
            SectorPortalAccessEntry entry = pair.Value;
            if (entry == null || !entry.HasAuthorityContentHash)
                throw new InvalidOperationException("Navigation authority digest encountered an unhashed sector portal access cache entry.");

            ulong token = 14695981039346656037UL;
            AddSectorPortalAccessAuthorityToken(ref token, pair.Key);
            AddAuthorityToken(ref token, entry.LastUsedFrame);
            runtimeSetHash ^= token;
        }
        return runtimeSetHash;
    }

    private static void AddAuthoritySharedGoalFieldCache(LogicStateHasher hasher)
    {
        hasher.Add(SharedGoalFields.Count);
        hasher.Add(_sharedGoalFieldAuthorityContentHash);
        hasher.Add(ComputeSharedGoalFieldRuntimeSetHash());
    }

    private static ulong ComputeSharedGoalFieldRuntimeSetHash()
    {
        ulong runtimeSetHash = 0;
        foreach (KeyValuePair<SharedGoalFieldKey, SharedGoalField> pair in SharedGoalFields)
        {
            SharedGoalField field = pair.Value;
            if (field == null || !field.HasAuthorityContentHash)
                throw new InvalidOperationException("Navigation authority digest encountered an unhashed shared-goal field.");

            ulong token = 14695981039346656037UL;
            AddSharedGoalAuthorityToken(ref token, pair.Key);
            AddAuthorityToken(ref token, field.LastUsedFrame);
            runtimeSetHash ^= token;
        }
        return runtimeSetHash;
    }

#if UNITY_EDITOR
    public static void GetEditorTestAuthorityCacheState(
        out int sectorPathCount,
        out ulong sectorPathContentHash,
        out ulong sectorPathUsageHash,
        out int sectorPortalAccessCount,
        out ulong sectorPortalAccessContentHash,
        out ulong sectorPortalAccessUsageHash,
        out int sharedGoalCount,
        out ulong sharedGoalContentHash,
        out ulong sharedGoalUsageHash)
    {
        sectorPathCount = SectorPathCache.Count;
        sectorPathContentHash = _sectorPathAuthorityContentHash;
        sectorPathUsageHash = ComputeSectorPathRuntimeSetHash();
        sectorPortalAccessCount = SectorPortalAccessCache.Count;
        sectorPortalAccessContentHash = _sectorPortalAccessAuthorityContentHash;
        sectorPortalAccessUsageHash = ComputeSectorPortalAccessRuntimeSetHash();
        sharedGoalCount = SharedGoalFields.Count;
        sharedGoalContentHash = _sharedGoalFieldAuthorityContentHash;
        sharedGoalUsageHash = ComputeSharedGoalFieldRuntimeSetHash();
    }

    public static void SetEditorTestOnlySectorPortalAccessLastUsedFrame(int frame)
    {
        if (SectorPortalAccessCache.Count == 0)
            throw new InvalidOperationException("SetEditorTestOnlySectorPortalAccessLastUsedFrame requires a populated cache.");
        foreach (SectorPortalAccessEntry entry in SectorPortalAccessCache.Values)
        {
            if (entry == null)
                throw new InvalidOperationException("Sector portal-access cache contains a null entry.");
            entry.LastUsedFrame = frame;
        }
    }

    public static string GetEditorTestAuthorityCheckpointLiveStateSignature()
    {
        int dirtyWorldCount = 0;
        int worldBuildJobCount = 0;
        int runtimeDirtyJobCount = 0;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state == null)
                throw new InvalidOperationException("Checkpoint live-state signature encountered a null world state.");
            if (state.IsDirty)
                dirtyWorldCount++;
            if (state.BuildJob != null)
                worldBuildJobCount++;
            if (state.RuntimeDirtyJob != null)
                runtimeDirtyJobCount++;
        }

        return
            $"topology={_navigationTopologyVersion}|nextWorld={_nextWorldVersion}|nextPath={_nextPathHandleId}|" +
            $"flowStart={_flowBuildQueueWorldStartIndex}|transitionDepth={_runtimeNavigationTransitionDepth}|" +
            $"worlds={WorldStates.Count}|activeAgentType={(_activeWorldState != null ? _activeWorldState.AgentTypeId : int.MinValue)}|" +
            $"activeWorld={(_world != null ? _world.Version : 0)}|agents={Agents.Count}|circles={CircleObstacles.Count}|" +
            $"boxes={BoxObstacles.Count}|costStamps={CostStamps.Count}|sectorPaths={SectorPathCache.Count}|" +
            $"portalAccess={SectorPortalAccessCache.Count}|flowTiles={DeterministicFlowTileCache.Count}|sharedGoals={SharedGoalFields.Count}|" +
            $"flowQueue={FlowTileBuildQueue.Count}|sharedQueue={SharedGoalFieldBuildQueue.Count}|anchors={MovingTargetAnchors.Count}|" +
            $"reservations={NavigationGoalReservations.Count}|dirtyWorlds={dirtyWorldCount}|worldJobs={worldBuildJobCount}|runtimeJobs={runtimeDirtyJobCount}";
    }
#endif

    private static void AddSectorPathAuthorityToken(ref ulong token, SectorPathCacheKey key)
    {
        AddAuthorityToken(ref token, key.WorldVersion);
        AddAuthorityToken(ref token, key.StartSectorId);
        AddAuthorityToken(ref token, key.StartCellIndex);
        AddAuthorityToken(ref token, key.GoalSectorId);
        AddAuthorityToken(ref token, key.GoalCellIndex);
        AddAuthorityToken(ref token, key.StartSectorDirtyVersion);
        AddAuthorityToken(ref token, key.GoalSectorDirtyVersion);
    }

    private static void AddSectorPortalAccessAuthorityToken(ref ulong token, SectorPortalAccessKey key)
    {
        AddAuthorityToken(ref token, key.WorldVersion);
        AddAuthorityToken(ref token, key.SectorId);
        AddAuthorityToken(ref token, key.PortalId);
        AddAuthorityToken(ref token, key.SectorDirtyVersion);
    }

    private static void AddSharedGoalAuthorityToken(ref ulong token, SharedGoalFieldKey key)
    {
        AddAuthorityToken(ref token, key.WorldVersion);
        AddAuthorityToken(ref token, key.AgentTypeId);
        AddAuthorityToken(ref token, key.GoalSectorId);
        AddAuthorityToken(ref token, key.GoalCellIndex);
        AddAuthorityToken(ref token, key.GoalSectorDirtyVersion);
    }

    private static ulong ComputeSectorPathAuthorityContentHash(SectorPathCacheKey key, SectorPathCacheEntry entry)
    {
        if (entry == null || entry.SectorIds == null || entry.PortalIds == null)
            throw new InvalidOperationException("Cannot hash an invalid sector path cache entry.");

        var hasher = new LogicStateHasher();
        hasher.Add(0x4E41565041544843UL);
        AddSectorPathKey(hasher, key);
        AddIntArray(hasher, entry.SectorIds);
        AddIntArray(hasher, entry.PortalIds);
        return hasher.Hash;
    }

    private static void SetSectorPathCacheEntry(SectorPathCacheKey key, SectorPathCacheEntry entry)
    {
        RemoveSectorPathCacheEntry(key);
        entry.AuthorityContentHash = ComputeSectorPathAuthorityContentHash(key, entry);
        entry.HasAuthorityContentHash = true;
        SectorPathCache[key] = entry;
        _sectorPathAuthorityContentHash ^= entry.AuthorityContentHash;
    }

    private static void RemoveSectorPathCacheEntry(SectorPathCacheKey key)
    {
        if (!SectorPathCache.TryGetValue(key, out SectorPathCacheEntry entry))
            return;
        if (entry == null || !entry.HasAuthorityContentHash)
            throw new InvalidOperationException("Cannot remove an unhashed sector path cache entry.");
        _sectorPathAuthorityContentHash ^= entry.AuthorityContentHash;
        SectorPathCache.Remove(key);
    }

    private static void ClearSectorPathCache()
    {
        SectorPathCache.Clear();
        _sectorPathAuthorityContentHash = 0;
    }

    private static ulong ComputeSectorPortalAccessAuthorityContentHash(SectorPortalAccessKey key, SectorPortalAccessEntry entry)
    {
        if (entry == null)
            throw new InvalidOperationException("Cannot hash a null sector portal access cache entry.");
        if (!entry.IsAnalyticClearSector && entry.DeterministicIntegration == null)
            throw new InvalidOperationException("Cannot hash sector portal access before deterministic integration is committed.");

        LogicStateHasher hasher = SectorPortalAccessContentHasher;
        hasher.Reset();
        hasher.Add(0x4E4156504F525443UL);
        hasher.Add(key.WorldVersion);
        hasher.Add(key.SectorId);
        hasher.Add(key.PortalId);
        hasher.Add(key.SectorDirtyVersion);
        AddLongArray(hasher, entry.DeterministicIntegration);
        hasher.Add(entry.IsAnalyticClearSector);
        hasher.Add(entry.SectorId);
        hasher.Add(entry.PortalId);
        hasher.Add(entry.SectorDirtyVersion);
        return hasher.Hash;
    }

    private static void SetSectorPortalAccessCacheEntry(SectorPortalAccessKey key, SectorPortalAccessEntry entry)
    {
        RemoveSectorPortalAccessCacheEntry(key);
        entry.AuthorityContentHash = 0;
        entry.HasAuthorityContentHash = false;
        SectorPortalAccessCache[key] = entry;
    }

    private static void RefreshSectorPortalAccessAuthorityContentHashes()
    {
        _sectorPortalAccessAuthorityContentHash = 0;
        foreach (KeyValuePair<SectorPortalAccessKey, SectorPortalAccessEntry> pair in SectorPortalAccessCache)
        {
            SectorPortalAccessEntry entry = pair.Value;
            entry.AuthorityContentHash = ComputeSectorPortalAccessAuthorityContentHash(pair.Key, entry);
            entry.HasAuthorityContentHash = true;
            _sectorPortalAccessAuthorityContentHash ^= entry.AuthorityContentHash;
        }
    }

    private static void RemoveSectorPortalAccessCacheEntry(SectorPortalAccessKey key)
    {
        if (!SectorPortalAccessCache.TryGetValue(key, out SectorPortalAccessEntry entry))
            return;
        if (entry != null && entry.HasAuthorityContentHash)
            _sectorPortalAccessAuthorityContentHash ^= entry.AuthorityContentHash;
        if (entry != null)
        {
            ReturnPortalAccessIntegrationArray(entry.DeterministicIntegration);
            entry.DeterministicIntegration = null;
        }
        SectorPortalAccessCache.Remove(key);
    }

    private static void ClearSectorPortalAccessCache()
    {
        foreach (SectorPortalAccessEntry entry in SectorPortalAccessCache.Values)
        {
            if (entry != null)
                ReturnPortalAccessIntegrationArray(entry.DeterministicIntegration);
        }
        SectorPortalAccessCache.Clear();
        PortalAccessIntegrationArrayPool.Clear();
        _sectorPortalAccessAuthorityContentHash = 0;
    }

    private static ulong ComputeSharedGoalFieldAuthorityContentHash(SharedGoalField field)
    {
        if (field == null)
            throw new InvalidOperationException("Cannot hash a null shared-goal field.");

        var hasher = new LogicStateHasher();
        hasher.Add(0x4E41565348415245UL);
        AddSharedGoalKey(hasher, field.Key);
        hasher.Add(field.GoalSectorId);
        hasher.Add(field.GoalX);
        hasher.Add(field.GoalY);
        AddSortedLongDictionary(hasher, field.NodeCosts);
        AddSortedIntDictionary(hasher, field.NextNodeTowardGoal);
        AddSortedInts(hasher, field.CompletedDemandStartSectorIds);
        AddSortedInts(hasher, field.CompletedDemandStartCellIndices);
        return hasher.Hash;
    }

    private static void SetSharedGoalFieldCacheEntry(SharedGoalFieldKey key, SharedGoalField field)
    {
        RemoveSharedGoalFieldCacheEntry(key);
        field.AuthorityContentHash = ComputeSharedGoalFieldAuthorityContentHash(field);
        field.HasAuthorityContentHash = true;
        SharedGoalFields[key] = field;
        _sharedGoalFieldAuthorityContentHash ^= field.AuthorityContentHash;
    }

    private static void RemoveSharedGoalFieldCacheEntry(SharedGoalFieldKey key)
    {
        if (!SharedGoalFields.TryGetValue(key, out SharedGoalField field))
            return;
        if (field == null || !field.HasAuthorityContentHash)
            throw new InvalidOperationException("Cannot remove an unhashed shared-goal field.");
        _sharedGoalFieldAuthorityContentHash ^= field.AuthorityContentHash;
        SharedGoalFields.Remove(key);
    }

    private static void ClearSharedGoalFieldCache()
    {
        SharedGoalFields.Clear();
        _sharedGoalFieldAuthorityContentHash = 0;
    }

    private static void AddAuthorityDeterministicFlowTiles(LogicStateHasher hasher)
    {
        if (FlowTileCache.Count != DeterministicFlowTileCache.Count)
        {
            throw new InvalidOperationException(
                $"Navigation flow-tile cache mirror count mismatch. view={FlowTileCache.Count}, authority={DeterministicFlowTileCache.Count}.");
        }

        hasher.Add(0x4E41564155544854UL);
        hasher.Add(DeterministicFlowTileCache.Count);
        hasher.Add(_deterministicFlowTileAuthorityContentHash);
        ulong retentionStateHash = 0;
        foreach (KeyValuePair<FlowTileCacheKey, FlowTileCacheEntry> pair in DeterministicFlowTileCache)
        {
            FlowTileCacheEntry tile = pair.Value;
            if (tile == null)
                throw new InvalidOperationException("Navigation authority digest encountered a null deterministic tile.");
            if (!FlowTileCache.TryGetValue(pair.Key, out FlowTileCacheEntry mirroredTile)
                || !ReferenceEquals(tile, mirroredTile))
            {
                throw new InvalidOperationException(
                    $"Navigation flow-tile cache mirror mismatch. key={FormatTileKey(pair.Key)}.");
            }

            ulong token = 14695981039346656037UL;
            AddAuthorityToken(ref token, pair.Key.WorldVersion);
            AddAuthorityToken(ref token, pair.Key.SectorId);
            AddAuthorityToken(ref token, (int)pair.Key.GoalKind);
            AddAuthorityToken(ref token, pair.Key.GoalId);
            AddAuthorityToken(ref token, pair.Key.DownstreamGoalHint);
            AddAuthorityToken(ref token, pair.Key.FinalGoalIndex);
            AddAuthorityToken(ref token, pair.Key.AgentTypeId);
            AddAuthorityToken(ref token, pair.Key.DirtyVersion);
            AddAuthorityToken(ref token, tile.LastUsedFrame);
            AddAuthorityToken(ref token, tile.LastReferencedFrame);
            retentionStateHash ^= token;
        }
        hasher.Add(retentionStateHash);
    }

    private static void AddAuthorityFlowTileBuildQueue(LogicStateHasher hasher)
    {
        hasher.Add(0x4E41564155544851UL);
        hasher.Add(FlowTileBuildQueue.Count);
        AuthorityPendingFlowTileKeys.Clear();
        for (LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First; node != null; node = node.Next)
        {
            FlowTileBuildJob job = node.Value;
            if (job == null)
                throw new InvalidOperationException("Navigation authority digest encountered a null flow-tile build job.");
            if (!AuthorityPendingFlowTileKeys.Add(job.BuildKey.CacheKey))
            {
                throw new InvalidOperationException(
                    $"Navigation flow-tile build queue contains a duplicate key. key={FormatTileKey(job.BuildKey.CacheKey)}.");
            }
            AddFlowTileBuildKey(hasher, job.BuildKey);
            AddPathHandle(hasher, job.HandleSnapshot);
        }

        ValidatePendingFlowTileBuildIndex();
        ulong pendingSetHash = 0;
        foreach (FlowTileCacheKey key in PendingFlowTileBuildJobs)
        {
            ulong token = 14695981039346656037UL;
            AddAuthorityToken(ref token, key.WorldVersion);
            AddAuthorityToken(ref token, key.SectorId);
            AddAuthorityToken(ref token, (int)key.GoalKind);
            AddAuthorityToken(ref token, key.GoalId);
            AddAuthorityToken(ref token, key.DownstreamGoalHint);
            AddAuthorityToken(ref token, key.FinalGoalIndex);
            AddAuthorityToken(ref token, key.AgentTypeId);
            AddAuthorityToken(ref token, key.DirtyVersion);
            pendingSetHash ^= token;
        }

        hasher.Add(PendingFlowTileBuildJobs.Count);
        hasher.Add(pendingSetHash);
    }

    private static void ValidatePendingFlowTileBuildIndex()
    {
        if (PendingFlowTileBuildJobs.Count != AuthorityPendingFlowTileKeys.Count)
        {
            throw new InvalidOperationException(
                $"Navigation pending flow-tile index count mismatch. queue={AuthorityPendingFlowTileKeys.Count}, index={PendingFlowTileBuildJobs.Count}.");
        }

        foreach (FlowTileCacheKey key in PendingFlowTileBuildJobs)
        {
            if (!AuthorityPendingFlowTileKeys.Contains(key))
            {
                throw new InvalidOperationException(
                    $"Navigation pending flow-tile index contains a key absent from the queue. key={FormatTileKey(key)}.");
            }
        }
    }

    private static void AddAuthorityToken(ref ulong hash, int value)
    {
        uint raw = unchecked((uint)value);
        for (int i = 0; i < sizeof(uint); i++)
        {
            hash ^= (byte)(raw >> (i * 8));
            hash *= 1099511628211UL;
        }
    }

    private static void AddAuthorityToken(ref ulong hash, long value)
    {
        ulong raw = unchecked((ulong)value);
        for (int i = 0; i < sizeof(ulong); i++)
        {
            hash ^= (byte)(raw >> (i * 8));
            hash *= 1099511628211UL;
        }
    }

    private static ulong ComputeAuthorityIntToken(ulong domain, int value)
    {
        ulong token = 14695981039346656037UL;
        AddAuthorityToken(ref token, unchecked((long)domain));
        AddAuthorityToken(ref token, value);
        return token;
    }

    private static ulong ComputeAuthorityIntIntToken(ulong domain, int key, int value)
    {
        ulong token = 14695981039346656037UL;
        AddAuthorityToken(ref token, unchecked((long)domain));
        AddAuthorityToken(ref token, key);
        AddAuthorityToken(ref token, value);
        return token;
    }

    private static ulong ComputeAuthorityIntLongToken(ulong domain, int key, long value)
    {
        ulong token = 14695981039346656037UL;
        AddAuthorityToken(ref token, unchecked((long)domain));
        AddAuthorityToken(ref token, key);
        AddAuthorityToken(ref token, value);
        return token;
    }

    private static ulong ComputeDeterministicCostQueueNodeAuthorityToken(int index, long cost)
    {
        return ComputeAuthorityIntLongToken(0x5347484541504E44UL, index, cost);
    }

    public static bool TryValidateEditorTestSharedGoalIncrementalAuthorityHashes(out string failureReason)
    {
        for (LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First; node != null; node = node.Next)
        {
            SharedGoalFieldBuildJob job = node.Value;
            if (job == null)
            {
                failureReason = "Shared-goal build queue contains a null job.";
                return false;
            }
            if (job.PortalOpenSet != null
                && job.PortalOpenSet.AuthorityContentHash != job.PortalOpenSet.ComputeAuthorityContentHashForValidation())
            {
                failureReason = $"Shared-goal heap authority hash mismatch key={job.Key}.";
                return false;
            }
            if (job.DemandStartSectorAuthorityContentHash
                != ComputeAuthorityIntSetHash(job.DemandStartSectorIds, 0x534744534543544FUL))
            {
                failureReason = $"Shared-goal demand sector authority hash mismatch key={job.Key}.";
                return false;
            }
            if (job.DemandStartCellAuthorityContentHash
                != ComputeAuthorityIntIntMapHash(job.DemandStartSectorByCellIndex, 0x53474443454C4C4FUL))
            {
                failureReason = $"Shared-goal demand cell authority hash mismatch key={job.Key}.";
                return false;
            }
            if (job.SettledPortalAuthorityContentHash
                != ComputeAuthorityIntSetHash(job.SettledPortalNodes, 0x5347534554544C45UL))
            {
                failureReason = $"Shared-goal settled portal authority hash mismatch key={job.Key}.";
                return false;
            }
            if (!TryValidateSharedGoalFieldIncrementalAuthorityHashes(job.Field, out failureReason))
                return false;
        }

        foreach (SharedGoalField field in SharedGoalFields.Values)
        {
            if (!TryValidateSharedGoalFieldIncrementalAuthorityHashes(field, out failureReason))
                return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TryValidateSharedGoalFieldIncrementalAuthorityHashes(SharedGoalField field, out string failureReason)
    {
        if (field == null)
        {
            failureReason = null;
            return true;
        }
        if (field.NodeCostsAuthorityContentHash
            != ComputeAuthorityIntLongMapHash(field.NodeCosts, 0x5347464E434F5354UL))
        {
            failureReason = $"Shared-goal node cost authority hash mismatch key={field.Key}.";
            return false;
        }
        if (field.NextNodeTowardGoalAuthorityContentHash
            != ComputeAuthorityIntIntMapHash(field.NextNodeTowardGoal, 0x5347464E4558544EUL))
        {
            failureReason = $"Shared-goal next node authority hash mismatch key={field.Key}.";
            return false;
        }
        if (field.CompletedDemandStartSectorAuthorityContentHash
            != ComputeAuthorityIntSetHash(field.CompletedDemandStartSectorIds, 0x534746434F4D5053UL))
        {
            failureReason = $"Shared-goal completed demand sector authority hash mismatch key={field.Key}.";
            return false;
        }
        if (field.CompletedDemandStartCellAuthorityContentHash
            != ComputeAuthorityIntSetHash(field.CompletedDemandStartCellIndices, 0x534746434F4D5043UL))
        {
            failureReason = $"Shared-goal completed demand cell authority hash mismatch key={field.Key}.";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static ulong ComputeAuthorityIntSetHash(IEnumerable<int> values, ulong domain)
    {
        ulong contentHash = 0;
        if (values == null)
            return contentHash;
        foreach (int value in values)
            contentHash ^= ComputeAuthorityIntToken(domain, value);
        return contentHash;
    }

    private static ulong ComputeAuthorityIntIntMapHash(IDictionary<int, int> values, ulong domain)
    {
        ulong contentHash = 0;
        if (values == null)
            return contentHash;
        foreach (KeyValuePair<int, int> pair in values)
            contentHash ^= ComputeAuthorityIntIntToken(domain, pair.Key, pair.Value);
        return contentHash;
    }

    private static ulong ComputeAuthorityIntLongMapHash(IDictionary<int, long> values, ulong domain)
    {
        ulong contentHash = 0;
        if (values == null)
            return contentHash;
        foreach (KeyValuePair<int, long> pair in values)
            contentHash ^= ComputeAuthorityIntLongToken(domain, pair.Key, pair.Value);
        return contentHash;
    }

    private static void AddAuthoritySharedGoalFieldBuildQueue(LogicStateHasher hasher)
    {
        hasher.Add(0x4E415653484A4F42UL);
        hasher.Add(SharedGoalFieldBuildQueue.Count);
        AuthorityPendingSharedGoalKeys.Clear();
        for (LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First; node != null; node = node.Next)
        {
            SharedGoalFieldBuildJob job = node.Value;
            if (job == null)
                throw new InvalidOperationException("Navigation authority digest encountered a null shared-goal build job.");
            if (!AuthorityPendingSharedGoalKeys.Add(job.Key))
            {
                throw new InvalidOperationException(
                    $"Navigation shared-goal build queue contains a duplicate key. key={job.Key}.");
            }

            EnsureSharedGoalBuildJobAuthorityProgressHash(job);
            hasher.Add(job.AuthorityProgressHash);
        }

        ValidatePendingSharedGoalBuildIndex();
        ulong pendingSetHash = 0;
        foreach (SharedGoalFieldKey key in PendingSharedGoalFieldBuildJobs)
        {
            ulong token = 14695981039346656037UL;
            AddSharedGoalAuthorityToken(ref token, key);
            pendingSetHash ^= token;
        }
        hasher.Add(PendingSharedGoalFieldBuildJobs.Count);
        hasher.Add(pendingSetHash);
    }

    private static void ValidatePendingSharedGoalBuildIndex()
    {
        if (PendingSharedGoalFieldBuildJobs.Count != AuthorityPendingSharedGoalKeys.Count)
        {
            throw new InvalidOperationException(
                $"Navigation pending shared-goal index count mismatch. queue={AuthorityPendingSharedGoalKeys.Count}, index={PendingSharedGoalFieldBuildJobs.Count}.");
        }

        foreach (SharedGoalFieldKey key in PendingSharedGoalFieldBuildJobs)
        {
            if (!AuthorityPendingSharedGoalKeys.Contains(key))
            {
                throw new InvalidOperationException(
                    $"Navigation pending shared-goal index contains a key absent from the queue. key={key}.");
            }
        }
    }

    private static void EnsureSharedGoalBuildJobAuthorityProgressHash(SharedGoalFieldBuildJob job)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));
        if (job.HasAuthorityProgressHash)
            return;

        var hasher = new LogicStateHasher();
        hasher.Add(0x4E415653484A5052UL);
        AddSharedGoalKey(hasher, job.Key);
        hasher.Add(job.GoalSectorId);
        hasher.Add(job.GoalX);
        hasher.Add(job.GoalY);
        hasher.Add(job.AgentTypeId);
        hasher.Add((int)job.Stage);
        AddSharedGoalBuildHeapProgressDigest(hasher, job.PortalOpenSet);
        AddSharedGoalBuildSetProgressDigest(hasher, job.DemandStartSectorIds, job.DemandStartSectorAuthorityContentHash);
        AddSharedGoalBuildMapProgressDigest(hasher, job.DemandStartSectorByCellIndex, job.DemandStartCellAuthorityContentHash);
        AddSharedGoalBuildSetProgressDigest(hasher, job.SettledPortalNodes, job.SettledPortalAuthorityContentHash);
        AddSharedGoalFieldProgressDigest(hasher, job.Field);
        job.AuthorityProgressHash = hasher.Hash;
        job.HasAuthorityProgressHash = true;
        _sharedGoalBuildJobAuthorityHashRefreshCount++;
    }

    private static void AddSharedGoalBuildHeapProgressDigest(LogicStateHasher hasher, DeterministicCostHeap heap)
    {
        hasher.Add(heap != null);
        if (heap == null)
            return;
        hasher.Add(heap.Count);
        hasher.Add(heap.AuthorityContentHash);
    }

    private static void AddSharedGoalBuildSetProgressDigest(LogicStateHasher hasher, ICollection<int> values, ulong contentHash)
    {
        hasher.Add(values != null);
        if (values == null)
            return;
        hasher.Add(values.Count);
        hasher.Add(contentHash);
    }

    private static void AddSharedGoalBuildMapProgressDigest(LogicStateHasher hasher, IDictionary<int, int> values, ulong contentHash)
    {
        hasher.Add(values != null);
        if (values == null)
            return;
        hasher.Add(values.Count);
        hasher.Add(contentHash);
    }

    private static void AddSharedGoalFieldProgressDigest(LogicStateHasher hasher, SharedGoalField field)
    {
        hasher.Add(field != null);
        if (field == null)
            return;
        AddSharedGoalKey(hasher, field.Key);
        hasher.Add(field.GoalSectorId);
        hasher.Add(field.GoalX);
        hasher.Add(field.GoalY);
        hasher.Add(field.NodeCosts.Count);
        hasher.Add(field.NodeCostsAuthorityContentHash);
        hasher.Add(field.NextNodeTowardGoal.Count);
        hasher.Add(field.NextNodeTowardGoalAuthorityContentHash);
        hasher.Add(field.CompletedDemandStartSectorIds.Count);
        hasher.Add(field.CompletedDemandStartSectorAuthorityContentHash);
        hasher.Add(field.CompletedDemandStartCellIndices.Count);
        hasher.Add(field.CompletedDemandStartCellAuthorityContentHash);
        hasher.Add(field.LastUsedFrame);
    }

    private static ulong ComputeDeterministicFlowTileAuthorityContentHash(FlowTileCacheEntry tile)
    {
        if (tile == null)
            throw new ArgumentNullException(nameof(tile));
        if (tile.DeterministicIntegrationCosts == null || tile.DeterministicFlowDirectionIndices == null)
            throw new InvalidOperationException($"Deterministic tile has no fixed payload. key={FormatTileKey(tile.Key)}.");

        var hasher = new LogicStateHasher();
        hasher.Add(0x4E415654494C4546UL);
        AddFlowTileCacheKey(hasher, tile.Key);
        hasher.Add(tile.StartX);
        hasher.Add(tile.StartY);
        hasher.Add(tile.Width);
        hasher.Add(tile.Height);
        AddIntArray(hasher, tile.DeterministicIntegrationCosts);
        AddByteArray(hasher, tile.DeterministicFlowDirectionIndices);
        AddVector2IntArray(hasher, tile.GoalCells);
        hasher.Add(tile.UsesClearFlowDescriptor);
        return hasher.Hash;
    }

    private static void SetDeterministicFlowTileCacheEntry(FlowTileCacheKey key, FlowTileCacheEntry tile)
    {
        if (tile == null)
            throw new ArgumentNullException(nameof(tile));
        if (!tile.Key.Equals(key))
            throw new InvalidOperationException($"Deterministic tile key mismatch. dictionary={FormatTileKey(key)} tile={FormatTileKey(tile.Key)}.");

        if (DeterministicFlowTileCache.TryGetValue(key, out FlowTileCacheEntry existing))
            _deterministicFlowTileAuthorityContentHash ^= ComputeDeterministicFlowTileAuthorityContentHash(existing);

        DeterministicFlowTileCache[key] = tile;
        _deterministicFlowTileAuthorityContentHash ^= ComputeDeterministicFlowTileAuthorityContentHash(tile);
    }

    private static void RemoveDeterministicFlowTileCacheEntry(FlowTileCacheKey key)
    {
        if (!DeterministicFlowTileCache.TryGetValue(key, out FlowTileCacheEntry tile))
            return;

        _deterministicFlowTileAuthorityContentHash ^= ComputeDeterministicFlowTileAuthorityContentHash(tile);
        if (!DeterministicFlowTileCache.Remove(key))
            throw new InvalidOperationException($"Deterministic tile removal failed. key={FormatTileKey(key)}.");
    }

    private static void ClearDeterministicFlowTileCache()
    {
        DeterministicFlowTileCache.Clear();
        _deterministicFlowTileAuthorityContentHash = 0;
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
        hasher.Add(DeterministicFlowTileCache.Count);
        hasher.Add(SharedGoalFields.Count);
        hasher.Add(FlowTileBuildQueue.Count);
        hasher.Add(SharedGoalFieldBuildQueue.Count);
        hasher.Add(MovingTargetAnchors.Count);
        hasher.Add(NavigationGoalReservations.Count);
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

    private static void AddAuthorityWorldProgress(LogicStateHasher hasher)
    {
        AuthorityWorldKeys.Clear();
        foreach (int agentTypeId in WorldStates.Keys)
            AuthorityWorldKeys.Add(agentTypeId);
        AuthorityWorldKeys.Sort(AuthorityIntComparison);
        hasher.Add(0x4E41564155544857UL);
        hasher.Add(AuthorityWorldKeys.Count);
        for (int i = 0; i < AuthorityWorldKeys.Count; i++)
        {
            WorldRuntimeState state = WorldStates[AuthorityWorldKeys[i]];
            if (state == null)
                throw new InvalidOperationException($"Navigation authority digest encountered a null world state. agentType={AuthorityWorldKeys[i]}.");

            hasher.Add(AuthorityWorldKeys[i]);
            hasher.Add(state.IsDirty);
            AddSortedInts(hasher, state.DirtyRuntimeObstacleSectors);
            AddAuthorityWorldBuildProgress(hasher, state.BuildJob);
            AddAuthorityRuntimeDirtyProgress(hasher, state.RuntimeDirtyJob);
        }
    }

    private static void AddAuthorityWorldBuildProgress(LogicStateHasher hasher, WorldBuildJob job)
    {
        hasher.Add(job != null);
        if (job == null)
            return;

        hasher.Add(job.AgentTypeId);
        hasher.Add((int)job.Stage);
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
        AddQueue(hasher, job.IslandOpenQueue);
        hasher.Add((int)job.PortalStage);
        hasher.Add(job.PortalInitialized);
        hasher.Add(job.PortalAddCursor);
        hasher.Add(job.PortalTransitionCursor);
        hasher.Add(job.PortalTransitionFromCursor);
        AddAuthorityPortalBuildProgress(hasher, job.PortalRebuiltPortals);
        AddAuthorityPendingPortalAccessProgress(hasher, job.PendingPortalAccessEntries);
        hasher.Add(job.HasAuthorityInputHash);
        if (job.HasAuthorityInputHash)
            hasher.Add(job.AuthorityInputHash);
        AddAuthorityWorkingWorldProgress(hasher, job.WorkingWorld);
    }

    private static void AddAuthorityRuntimeDirtyProgress(LogicStateHasher hasher, RuntimeDirtyRebuildJob job)
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
        AddQueue(hasher, job.IslandOpenQueue);
        hasher.Add((int)job.PortalStage);
        hasher.Add(job.PortalInitialized);
        hasher.Add(job.PortalSectorCursor);
        hasher.Add(job.PortalAddCursor);
        hasher.Add(job.PortalTransitionCursor);
        hasher.Add(job.PortalTransitionFromCursor);
        AddAuthorityLongSetProgress(hasher, job.PortalProcessedBoundaries);
        AddAuthorityPortalBuildProgress(hasher, job.PortalRebuiltPortals);
        AddAuthorityPendingPortalAccessProgress(hasher, job.PendingPortalAccessEntries);
        ValidateDerivedSortedIntSnapshot(job.DirtySectors, job.DirtySectorIds, "runtime-dirty sectors");
        ValidateDerivedSortedIntSnapshot(job.CostDirtySectors, job.CostDirtySectorIds, "runtime-dirty cost sectors");
        if (job.PortalTransitionSectorIds != null)
        {
            ValidateDerivedSortedIntSnapshot(
                job.PortalTransitionDirtySectors,
                job.PortalTransitionSectorIds,
                "runtime-dirty portal transition sectors");
        }
        else if (job.PortalStage >= RuntimeDirtyPortalStage.RebuildTransitions)
        {
            throw new InvalidOperationException(
                $"Navigation runtime-dirty portal transition index is missing. stage={job.PortalStage}.");
        }
        AddSortedInts(hasher, job.DirtySectors);
        AddSortedInts(hasher, job.CostDirtySectors);
        AddSortedInts(hasher, job.PortalTransitionDirtySectors);
        if (!job.HasAuthorityInputHash)
            throw new InvalidOperationException("Runtime-dirty authority digest encountered a job without an input hash.");
        hasher.Add(job.AuthorityInputHash);
        AddAuthorityWorkingWorldProgress(hasher, job.WorkingWorld);
    }

    private static void AddAuthorityPortalBuildProgress(LogicStateHasher hasher, IList<PortalData> portals)
    {
        hasher.Add(portals != null);
        if (portals == null)
            return;

        hasher.Add(portals.Count);
        for (int i = 0; i < portals.Count; i++)
            AddAuthorityPortalData(hasher, portals[i], $"pending index={i}");
    }

    private static void ValidateDerivedSortedIntSnapshot(
        ICollection<int> authorityValues,
        IList<int> orderedSnapshot,
        string label)
    {
        if (authorityValues == null)
            throw new InvalidOperationException($"Navigation {label} authority set is null.");
        if (orderedSnapshot == null)
            throw new InvalidOperationException($"Navigation {label} derived index is null.");

        SortedIntValues.Clear();
        SortedIntValues.AddRange(authorityValues);
        SortedIntValues.Sort();
        if (orderedSnapshot.Count != SortedIntValues.Count)
        {
            throw new InvalidOperationException(
                $"Navigation {label} derived index count mismatch. index={orderedSnapshot.Count}, authority={SortedIntValues.Count}.");
        }

        for (int i = 0; i < SortedIntValues.Count; i++)
        {
            if (orderedSnapshot[i] != SortedIntValues[i])
            {
                throw new InvalidOperationException(
                    $"Navigation {label} derived index mismatch. index={i}, ordered={orderedSnapshot[i]}, authority={SortedIntValues[i]}.");
            }
        }
    }

    private static void ValidateAuthorityDigestCallBoundary()
    {
        if (_navigationWorkBudgetActive || _remainingNavigationWorkOperations != 0)
        {
            throw new InvalidOperationException(
                $"Navigation authority digest cannot run inside a navigation work-budget scope. active={_navigationWorkBudgetActive}, remaining={_remainingNavigationWorkOperations}.");
        }
    }

    private static void AddAuthorityPendingPortalAccessProgress(
        LogicStateHasher hasher,
        IList<PendingSectorPortalAccess> entries)
    {
        hasher.Add(entries != null);
        if (entries == null)
            return;

        hasher.Add(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            PendingSectorPortalAccess entry = entries[i];
            hasher.Add(entry.SectorId);
            hasher.Add(entry.PortalId);
            hasher.Add(entry.SectorDirtyVersion);
            hasher.Add(entry.IsAnalyticClearSector);
        }
    }

    private static void AddAuthorityLongSetProgress(LogicStateHasher hasher, ICollection<long> values)
    {
        hasher.Add(values != null);
        if (values == null)
            return;

        ulong contentHash = 0;
        foreach (long value in values)
        {
            ulong token = 14695981039346656037UL;
            AddAuthorityToken(ref token, value);
            contentHash ^= token;
        }

        hasher.Add(values.Count);
        hasher.Add(contentHash);
    }

    private static void AddAuthorityWorkingWorldProgress(LogicStateHasher hasher, NavigationWorld world)
    {
        hasher.Add(world != null);
        if (world == null)
            return;

        hasher.Add(0x574F524B574F524CUL);
        hasher.Add(world.Version);
        hasher.Add(world.AgentTypeId);
        hasher.Add(world.Width);
        hasher.Add(world.Height);
        world.ValidateAuthorityGridMetadata("AddAuthorityWorkingWorldProgress");
        hasher.Add(world.CellSizeGridRaw);
        hasher.Add(world.EncodedCenterClearanceFixedRaw);
        hasher.Add(world.AgentRadiusFixedRaw);
        hasher.Add(world.OriginXGridRaw);
        hasher.Add(world.OriginZGridRaw);
        hasher.Add(world.IslandCount);
        hasher.Add(world.MainIslandId);
        hasher.Add(world.MainIslandSize);
        hasher.Add(world.SectorSizeInCells);
        hasher.Add(world.SectorCountX);
        hasher.Add(world.SectorCountY);
        hasher.Add(world.NextPortalId);

        AddAuthorityOptionalBoolArray(hasher, world.BaseWalkableMask);
        AddAuthorityOptionalByteArray(hasher, world.BaseNeighborTraversalMask);
        AddAuthorityOptionalByteArray(hasher, world.SourceCostField);
        AddAuthorityOptionalBoolArray(hasher, world.WalkableMask);
        AddAuthorityOptionalByteArray(hasher, world.CostField);
        AddAuthorityOptionalByteArrayArray(hasher, world.SectorCostFields);
        AddAuthorityOptionalNavigationAnchors(hasher, world.CellNavAnchorsFixedXZ);
        AddAuthorityOptionalByteArray(hasher, world.NeighborTraversalMask);
        AddAuthorityOptionalIntArray(hasher, world.IslandIds);

        int sectorCount = world.Sectors?.Length ?? -1;
        hasher.Add(sectorCount);
        if (world.Sectors != null)
        {
            for (int i = 0; i < world.Sectors.Length; i++)
            {
                SectorData sector = world.Sectors[i];
                hasher.Add(sector != null);
                if (sector == null)
                    continue;

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
                AddAuthorityOptionalIntArray(hasher, sector.LocalComponentIds);
                AddOrderedInts(hasher, sector.PortalIds);
                hasher.Add(sector.PortalTransitions.Count);
                for (int transitionIndex = 0; transitionIndex < sector.PortalTransitions.Count; transitionIndex++)
                {
                    PortalTransition transition = sector.PortalTransitions[transitionIndex]
                                                  ?? throw new InvalidOperationException(
                                                      $"Navigation authority digest encountered a null working-world transition. sector={sector.SectorId}, index={transitionIndex}.");
                    hasher.Add(transition.FromPortalId);
                    hasher.Add(transition.ToPortalId);
                    hasher.Add(transition.DeterministicCost);
                }
            }
        }

        int portalArrayCount = world.Portals?.Length ?? -1;
        hasher.Add(portalArrayCount);
        if (world.Portals != null)
        {
            for (int i = 0; i < world.Portals.Length; i++)
                AddAuthorityPortalData(hasher, world.Portals[i], $"array index={i}");
        }

        hasher.Add(world.PortalsById != null);
        if (world.PortalsById != null)
        {
            AuthorityWorkingWorldPortalIds.Clear();
            foreach (int portalId in world.PortalsById.Keys)
                AuthorityWorkingWorldPortalIds.Add(portalId);
            AuthorityWorkingWorldPortalIds.Sort(AuthorityIntComparison);
            hasher.Add(AuthorityWorkingWorldPortalIds.Count);
            for (int i = 0; i < AuthorityWorkingWorldPortalIds.Count; i++)
            {
                int portalId = AuthorityWorkingWorldPortalIds[i];
                hasher.Add(portalId);
                PortalData portal = world.PortalsById[portalId];
                if (portal == null || portal.PortalId != portalId)
                {
                    throw new InvalidOperationException(
                        $"Navigation authority digest encountered an invalid working-world portal lookup. key={portalId}, value={portal?.PortalId.ToString() ?? "null"}.");
                }
                AddAuthorityPortalData(hasher, portal, $"lookup key={portalId}");
            }
        }

        AddSortedPortalSignatures(hasher, world.PortalIdsBySignature);
        AddSortedInts(hasher, world.UsedPortalIds);
    }

    private static void AddAuthorityPortalData(LogicStateHasher hasher, PortalData portal, string location)
    {
        if (portal == null)
            throw new InvalidOperationException($"Navigation authority digest encountered a null portal. {location}.");
        if (portal.CellsA == null || portal.CellsB == null)
        {
            throw new InvalidOperationException(
                $"Navigation authority digest encountered a portal without cell payloads. portal={portal.PortalId}, {location}.");
        }

        hasher.Add(portal.PortalId);
        hasher.Add(portal.SectorAId);
        hasher.Add(portal.SectorBId);
        AddVector2IntArray(hasher, portal.CellsA);
        AddVector2IntArray(hasher, portal.CellsB);
        hasher.Add(portal.WidthCells);
        hasher.Add(portal.IsNarrow);
        hasher.Add(portal.IsVerticalBoundary);
    }

    private static void AddAuthorityOptionalBoolArray(LogicStateHasher hasher, bool[] values)
    {
        hasher.Add(values != null);
        if (values != null)
            AddBoolArray(hasher, values);
    }

    private static void AddAuthorityOptionalByteArray(LogicStateHasher hasher, byte[] values)
    {
        hasher.Add(values != null);
        if (values != null)
            AddByteArray(hasher, values);
    }

    private static void AddAuthorityOptionalIntArray(LogicStateHasher hasher, int[] values)
    {
        hasher.Add(values != null);
        if (values != null)
            AddIntArray(hasher, values);
    }

    private static void AddAuthorityOptionalByteArrayArray(LogicStateHasher hasher, byte[][] values)
    {
        hasher.Add(values != null);
        if (values == null)
            return;

        hasher.Add(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            hasher.Add(values[i] != null);
            if (values[i] != null)
                AddByteArray(hasher, values[i]);
        }
    }

    private static void AddAuthorityOptionalNavigationAnchors(LogicStateHasher hasher, FixVector2[] values)
    {
        hasher.Add(values != null);
        if (values != null)
            AddNavigationAnchorFixedXZArray(hasher, values);
    }

    private static void RefreshWorldBuildAuthorityInputHash(WorldBuildJob job)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));

        var hasher = new LogicStateHasher();
        hasher.Add(0x574F524C44494E50UL);
        hasher.Add(job.AgentTypeId);
        hasher.Add(job.Width);
        hasher.Add(job.Height);
        if (!job.HasAuthorityGridMetadata)
            throw new InvalidOperationException("Cannot hash a world-build job without frozen authority grid metadata.");
        hasher.Add(job.CellSizeGridRaw);
        hasher.Add(job.EncodedCenterClearanceFixedRaw);
        hasher.Add(job.OriginXGridRaw);
        hasher.Add(job.OriginZGridRaw);
        hasher.Add(job.HasProvidedCellNavAnchors);
        hasher.Add(job.HasProvidedNeighborTraversalMask);
        hasher.Add(job.IncludeRuntimeObstacles);
        if (job.HasProvidedCellNavAnchors
            && (job.CellNavAnchorsFixedXZ == null || job.CellNavAnchorsFixedXZ.Length != job.Width * job.Height))
        {
            throw new InvalidOperationException("Cannot hash a world-build job with incomplete fixed navigation anchors.");
        }
        hasher.Add(Config.SectorSizeInCells);
        hasher.Add(Config.PortalNarrowWidthCells);
        hasher.Add(Config.PortalMaxWindowWidthCells);
        hasher.Add(job.AgentRadiusFixedRaw);
        AddBoolArray(hasher, job.BaseWalkableMask);
        AddByteArray(hasher, job.BaseNeighborTraversalMask);
        AddByteArray(hasher, job.BaseCostField);
        AddNavigationAnchorFixedXZArray(hasher, job.CellNavAnchorsFixedXZ);
        AddAuthorityCircleObstacleSnapshot(hasher, job.CircleObstacles);
        AddAuthorityBoxObstacleSnapshot(hasher, job.BoxObstacles);
        AddAuthorityCostStampSnapshot(hasher, job.CostStamps);

        bool usesImportedWorld = job.BaseWalkableMask == null && job.WorkingWorld != null;
        hasher.Add(usesImportedWorld);
        if (usesImportedWorld)
            AddNavigationWorldContents(hasher, job.WorkingWorld);

        job.AuthorityInputHash = hasher.Hash;
        job.HasAuthorityInputHash = true;
    }

    private static void RefreshRuntimeDirtyAuthorityInputHash(RuntimeDirtyRebuildJob job)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));
        if (job.TargetWorld == null || !job.TargetWorld.HasDeterministicContentHash)
            throw new InvalidOperationException("Cannot hash runtime-dirty input without a hashed target world.");

        var hasher = new LogicStateHasher();
        hasher.Add(0x52554E4449525459UL);
        hasher.Add(job.TargetWorld.DeterministicContentHash);
        AddSortedInts(hasher, job.DirtySectors);
        AddSortedInts(hasher, job.CostDirtySectors);
        AddAuthorityCircleObstacleSnapshot(hasher, job.CircleObstacles);
        AddAuthorityBoxObstacleSnapshot(hasher, job.BoxObstacles);
        AddAuthorityCostStampSnapshot(hasher, job.CostStamps);
        job.AuthorityInputHash = hasher.Hash;
        job.HasAuthorityInputHash = true;
    }

    private static void AddNavigationAnchorFixedXZArray(LogicStateHasher hasher, FixVector2[] anchors)
    {
        if (anchors == null)
        {
            hasher.Add(-1);
            return;
        }

        hasher.Add(anchors.Length);
        for (int i = 0; i < anchors.Length; i++)
        {
            hasher.Add(anchors[i].x.RawValue);
            hasher.Add(anchors[i].y.RawValue);
        }
    }

    private static void AddAuthorityCircleObstacleSnapshot(LogicStateHasher hasher, List<CircleObstacle> obstacles)
    {
        if (obstacles == null)
        {
            hasher.Add(-1);
            return;
        }

        hasher.Add(obstacles.Count);
        for (int i = 0; i < obstacles.Count; i++)
        {
            CircleObstacle obstacle = obstacles[i] ?? throw new InvalidOperationException($"Cannot hash a null circle obstacle snapshot. index={i}.");
            hasher.Add(obstacle.Id);
            hasher.Add(obstacle.PositionFixed.x.RawValue);
            hasher.Add(obstacle.PositionFixed.y.RawValue);
            hasher.Add(obstacle.RadiusFixed.RawValue);
        }
    }

    private static void AddAuthorityBoxObstacleSnapshot(LogicStateHasher hasher, List<BoxObstacle> obstacles)
    {
        if (obstacles == null)
        {
            hasher.Add(-1);
            return;
        }

        hasher.Add(obstacles.Count);
        for (int i = 0; i < obstacles.Count; i++)
        {
            BoxObstacle obstacle = obstacles[i] ?? throw new InvalidOperationException($"Cannot hash a null box obstacle snapshot. index={i}.");
            hasher.Add(obstacle.Id);
            hasher.Add(obstacle.CenterFixed.x.RawValue);
            hasher.Add(obstacle.CenterFixed.y.RawValue);
            hasher.Add(obstacle.HalfExtentsFixed.x.RawValue);
            hasher.Add(obstacle.HalfExtentsFixed.y.RawValue);
        }
    }

    private static void AddAuthorityCostStampSnapshot(LogicStateHasher hasher, List<CostStamp> stamps)
    {
        if (stamps == null)
        {
            hasher.Add(-1);
            return;
        }

        hasher.Add(stamps.Count);
        for (int i = 0; i < stamps.Count; i++)
        {
            CostStamp stamp = stamps[i] ?? throw new InvalidOperationException($"Cannot hash a null cost stamp snapshot. index={i}.");
            hasher.Add(ComputeCostStampAuthorityContentHash(stamp));
        }
    }

    private static void AddAuthorityRuntimeObstacles(LogicStateHasher hasher)
    {
        AuthorityCircleObstacleIds.Clear();
        foreach (int obstacleId in CircleObstacles.Keys)
            AuthorityCircleObstacleIds.Add(obstacleId);
        AuthorityCircleObstacleIds.Sort(AuthorityIntComparison);
        hasher.Add(0x4E4156415554484FUL);
        hasher.Add(AuthorityCircleObstacleIds.Count);
        for (int i = 0; i < AuthorityCircleObstacleIds.Count; i++)
        {
            CircleObstacle obstacle = CircleObstacles[AuthorityCircleObstacleIds[i]];
            if (obstacle == null)
                throw new InvalidOperationException($"Navigation authority digest encountered a null circle obstacle. id={AuthorityCircleObstacleIds[i]}.");
            hasher.Add(obstacle.Id);
            hasher.Add(obstacle.PositionFixed.x.RawValue);
            hasher.Add(obstacle.PositionFixed.y.RawValue);
            hasher.Add(obstacle.RadiusFixed.RawValue);
        }

        AuthorityBoxObstacleIds.Clear();
        foreach (int obstacleId in BoxObstacles.Keys)
            AuthorityBoxObstacleIds.Add(obstacleId);
        AuthorityBoxObstacleIds.Sort(AuthorityIntComparison);
        hasher.Add(AuthorityBoxObstacleIds.Count);
        for (int i = 0; i < AuthorityBoxObstacleIds.Count; i++)
        {
            BoxObstacle obstacle = BoxObstacles[AuthorityBoxObstacleIds[i]];
            if (obstacle == null)
                throw new InvalidOperationException($"Navigation authority digest encountered a null box obstacle. id={AuthorityBoxObstacleIds[i]}.");
            hasher.Add(obstacle.Id);
            hasher.Add(obstacle.CenterFixed.x.RawValue);
            hasher.Add(obstacle.CenterFixed.y.RawValue);
            hasher.Add(obstacle.HalfExtentsFixed.x.RawValue);
            hasher.Add(obstacle.HalfExtentsFixed.y.RawValue);
        }

        hasher.Add(CostStamps.Count);
        hasher.Add(_costStampAuthorityContentHash);
    }

    private static ulong ComputeCostStampAuthorityContentHash(CostStamp stamp)
    {
        if (stamp == null)
            throw new InvalidOperationException("Cannot hash a null cost stamp.");

        var hasher = new LogicStateHasher();
        hasher.Add(0x4E4156434F535453UL);
        hasher.Add(stamp.Id);
        hasher.Add(stamp.AgentTypeId);
        hasher.Add(stamp.BoundsMinFixed.x.RawValue);
        hasher.Add(stamp.BoundsMinFixed.y.RawValue);
        hasher.Add(stamp.BoundsMaxFixed.x.RawValue);
        hasher.Add(stamp.BoundsMaxFixed.y.RawValue);
        hasher.Add(stamp.Cost);
        hasher.Add(stamp.Width);
        hasher.Add(stamp.Height);
        hasher.Add(stamp.CellSizeFixed.RawValue);
        hasher.Add(stamp.OriginFixed.x.RawValue);
        hasher.Add(stamp.OriginFixed.y.RawValue);
        AddByteArray(hasher, stamp.Costs);
        return hasher.Hash;
    }

    private static void SetCostStamp(CostStamp stamp)
    {
        if (stamp == null)
            throw new ArgumentNullException(nameof(stamp));
        RemoveCostStamp(stamp.Id);
        stamp.AuthorityContentHash = ComputeCostStampAuthorityContentHash(stamp);
        CostStamps[stamp.Id] = stamp;
        _costStampAuthorityContentHash ^= stamp.AuthorityContentHash;
    }

    private static void RemoveCostStamp(int stampId)
    {
        if (!CostStamps.TryGetValue(stampId, out CostStamp stamp))
            return;
        if (stamp == null)
            throw new InvalidOperationException($"Cannot remove a null cost stamp. id={stampId}.");
        _costStampAuthorityContentHash ^= stamp.AuthorityContentHash;
        CostStamps.Remove(stampId);
    }

    private static void ClearCostStamps()
    {
        CostStamps.Clear();
        _costStampAuthorityContentHash = 0;
    }

    private static void AddAuthorityAgents(LogicStateHasher hasher)
    {
        AuthorityAgentIds.Clear();
        foreach (int agentId in Agents.Keys)
            AuthorityAgentIds.Add(agentId);
        AuthorityAgentIds.Sort(AuthorityIntComparison);
        if (OrderedAgentIds.Count != AuthorityAgentIds.Count)
        {
            throw new InvalidOperationException(
                $"Navigation ordered-agent index count mismatch. ordered={OrderedAgentIds.Count}, authority={AuthorityAgentIds.Count}.");
        }
        for (int i = 0; i < AuthorityAgentIds.Count; i++)
        {
            if (OrderedAgentIds[i] != AuthorityAgentIds[i])
            {
                throw new InvalidOperationException(
                    $"Navigation ordered-agent index mismatch. index={i}, ordered={OrderedAgentIds[i]}, authority={AuthorityAgentIds[i]}.");
            }
        }
        hasher.Add(0x4E41564155544841UL);
        hasher.Add(AuthorityAgentIds.Count);
        for (int i = 0; i < AuthorityAgentIds.Count; i++)
        {
            AgentRuntimeData agent = Agents[AuthorityAgentIds[i]];
            if (agent == null || agent.NavState == null)
                throw new InvalidOperationException($"Navigation authority digest encountered an invalid agent. id={AuthorityAgentIds[i]}.");

            AgentNavState nav = agent.NavState;
            hasher.Add(agent.Id);
            hasher.Add(agent.IgnoreAgentCollision);
            hasher.Add(agent.AgentTypeId);
            hasher.Add(agent.HasNavigationIntent);
            hasher.Add(agent.PositionFixed.x.RawValue);
            hasher.Add(agent.PositionFixed.y.RawValue);
            hasher.Add(agent.RadiusFixed.RawValue);
            hasher.Add(nav.CurrentSectorId);
            AddVector2Int(hasher, nav.CurrentCell);
            AddPathHandle(hasher, nav.PathHandle);
            hasher.Add((int)nav.LastMovementMode);
            hasher.Add(nav.HasGoal);
            hasher.Add(nav.LastGoalWorldFixed.x.RawValue);
            hasher.Add(nav.LastGoalWorldFixed.y.RawValue);
            hasher.Add(nav.StableGoalX);
            hasher.Add(nav.StableGoalY);
            hasher.Add(nav.StableGoalRawX);
            hasher.Add(nav.StableGoalRawY);
            hasher.Add(nav.StableGoalTargetId);
            hasher.Add(nav.StableGoalWorldFixed.x.RawValue);
            hasher.Add(nav.StableGoalWorldFixed.y.RawValue);
            hasher.Add(nav.HasFailedPathRequest);
            hasher.Add(nav.FailedPathWorldVersion);
            hasher.Add(nav.FailedPathStartCellIndex);
            hasher.Add(nav.FailedPathGoalCellIndex);
            hasher.Add(nav.FailedPathStartSectorDirtyVersion);
            hasher.Add(nav.FailedPathGoalSectorDirtyVersion);
            hasher.Add(nav.LastFixedFlowFrame);
            hasher.Add(nav.LastFixedFlowVelocity.x.RawValue);
            hasher.Add(nav.LastFixedFlowVelocity.y.RawValue);
        }
    }

    private static void AddAuthorityMovingTargetAnchors(LogicStateHasher hasher)
    {
        AuthorityMovingTargetKeys.Clear();
        foreach (MovingTargetAnchorKey key in MovingTargetAnchors.Keys)
            AuthorityMovingTargetKeys.Add(key);
        AuthorityMovingTargetKeys.Sort(AuthorityMovingTargetKeyComparison);
        hasher.Add(0x4E4156415554484DUL);
        hasher.Add(AuthorityMovingTargetKeys.Count);
        for (int i = 0; i < AuthorityMovingTargetKeys.Count; i++)
        {
            MovingTargetAnchor anchor = MovingTargetAnchors[AuthorityMovingTargetKeys[i]];
            if (anchor == null)
                throw new InvalidOperationException("Navigation authority digest encountered a null moving-target anchor.");
            hasher.Add(anchor.Key.TargetId);
            hasher.Add(anchor.Key.AgentTypeId);
            hasher.Add(anchor.Key.IslandId);
            hasher.Add(anchor.RawGoalX);
            hasher.Add(anchor.RawGoalY);
            hasher.Add(anchor.ActiveGoalX);
            hasher.Add(anchor.ActiveGoalY);
            hasher.Add(anchor.ActiveGoalSectorId);
            hasher.Add(anchor.ActiveWorldVersion);
            hasher.Add(anchor.ActiveGoalWorldFixed.x.RawValue);
            hasher.Add(anchor.ActiveGoalWorldFixed.y.RawValue);
            hasher.Add(anchor.PendingRawGoalX);
            hasher.Add(anchor.PendingRawGoalY);
            hasher.Add(anchor.PendingGoalX);
            hasher.Add(anchor.PendingGoalY);
            hasher.Add(anchor.PendingGoalSectorId);
            hasher.Add(anchor.PendingWorldVersion);
            hasher.Add(anchor.PendingGoalWorldFixed.x.RawValue);
            hasher.Add(anchor.PendingGoalWorldFixed.y.RawValue);
            hasher.Add(anchor.LastUsedFrame);
        }
    }

    private static void AddAuthorityFixedPortalOwners(LogicStateHasher hasher)
    {
        AuthorityFixedPortalOwnerKeys.Clear();
        foreach (FixedPortalOwnerKey key in FixedPortalOwners.Keys)
            AuthorityFixedPortalOwnerKeys.Add(key);
        AuthorityFixedPortalOwnerKeys.Sort(AuthorityFixedPortalOwnerKeyComparison);
        hasher.Add(0x4E4156504F574E52UL);
        hasher.Add(AuthorityFixedPortalOwnerKeys.Count);
        for (int i = 0; i < AuthorityFixedPortalOwnerKeys.Count; i++)
        {
            FixedPortalOwnerState state = FixedPortalOwners[AuthorityFixedPortalOwnerKeys[i]]
                ?? throw new InvalidOperationException("Navigation authority digest encountered a null fixed portal owner.");
            hasher.Add(state.Key.WorldVersion);
            hasher.Add(state.Key.AgentTypeId);
            hasher.Add(state.Key.Kind);
            hasher.Add(state.Key.LocalBottleneckId);
            hasher.Add(state.OwnerDirection);
            hasher.Add(state.OwnerSinceFrame);
            hasher.Add(state.LastEvaluatedFrame);
            hasher.Add(state.PositiveMinimumAgentId);
            hasher.Add(state.NegativeMinimumAgentId);
        }
    }

    private static void AddAuthorityFixedCorridorBuilds(LogicStateHasher hasher)
    {
        AuthorityCorridorWorldVersions.Clear();
        foreach (int worldVersion in FixedCorridorLookupByWorldVersion.Keys)
            AuthorityCorridorWorldVersions.Add(worldVersion);
        AuthorityCorridorWorldVersions.Sort(AuthorityIntComparison);
        hasher.Add(0x4E4156434F525244UL);
        hasher.Add(FixedCorridorBuildOperationQuota);
        hasher.Add(AuthorityCorridorWorldVersions.Count);
        for (int i = 0; i < AuthorityCorridorWorldVersions.Count; i++)
        {
            int worldVersion = AuthorityCorridorWorldVersions[i];
            FixedCorridorLookup lookup = FixedCorridorLookupByWorldVersion[worldVersion]
                ?? throw new InvalidOperationException("Navigation authority digest encountered a null fixed corridor lookup.");
            hasher.Add(worldVersion);
            hasher.Add(lookup.NarrowWidth);
            hasher.Add(lookup.NextComponentId);
            hasher.Add(lookup.ComponentIdByCell.Count);
            hasher.Add(lookup.ComponentAssignmentContentHash);
            hasher.Add(lookup.CompletedComponentIds.Count);
            hasher.Add(lookup.CompletedComponentContentHash);
            hasher.Add(lookup.DescriptorByComponentId.Count);
            hasher.Add(lookup.DescriptorContentHash);
            hasher.Add(lookup.PendingStartIndices.Count);
            hasher.Add(lookup.PendingStartContentHash);

            FixedCorridorBuildJob job = lookup.ActiveBuildJob;
            hasher.Add(job != null);
            if (job == null)
                continue;
            hasher.Add(job.ComponentId);
            hasher.Add(job.StartIndex);
            hasher.Add(job.MinimumCellIndex);
            hasher.Add(job.Head);
            hasher.Add(job.Queue.Count);
            hasher.Add(job.ComponentCellContentHash);
            int unprocessedQueueCount = job.Queue.Count - job.Head;
            if (unprocessedQueueCount < 0)
            {
                throw new InvalidOperationException(
                    $"Navigation authority digest encountered an invalid fixed-corridor queue. head={job.Head}, count={job.Queue.Count}.");
            }
            hasher.Add(unprocessedQueueCount);
            for (int queueIndex = job.Head; queueIndex < job.Queue.Count; queueIndex++)
                hasher.Add(job.Queue[queueIndex]);
            hasher.Add(job.ExternalEndpointCells.Count);
            hasher.Add(job.ExternalEndpointContentHash);
            hasher.Add(job.ExternalEndpointOverflow);
            hasher.Add(job.TerminalEndpointCells.Count);
            hasher.Add(job.TerminalEndpointContentHash);
            hasher.Add(job.TerminalEndpointOverflow);
        }
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
        world.ValidateAuthorityGridMetadata("AddNavigationWorldBuildState");
        hasher.Add(world.CellSizeGridRaw);
        hasher.Add(world.EncodedCenterClearanceFixedRaw);
        hasher.Add(world.AgentRadiusFixedRaw);
        hasher.Add(world.OriginXGridRaw);
        hasher.Add(world.OriginZGridRaw);
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
        world.ValidateAuthorityGridMetadata("AddNavigationWorldContents");
        hasher.Add(world.CellSizeGridRaw);
        hasher.Add(world.EncodedCenterClearanceFixedRaw);
        hasher.Add(world.AgentRadiusFixedRaw);
        hasher.Add(world.OriginXGridRaw);
        hasher.Add(world.OriginZGridRaw);
        hasher.Add(world.IslandCount);
        hasher.Add(world.MainIslandId);
        hasher.Add(world.MainIslandSize);
        hasher.Add(world.SectorSizeInCells);
        hasher.Add(world.SectorCountX);
        hasher.Add(world.SectorCountY);
        hasher.Add(world.NextPortalId);
        if (world.CellNavAnchorsFixedXZ == null || world.CellNavAnchorsFixedXZ.Length != world.Width * world.Height)
            throw new InvalidOperationException("Cannot hash a navigation world with incomplete fixed navigation anchors.");
        AddBoolArray(hasher, world.BaseWalkableMask);
        AddByteArray(hasher, world.BaseNeighborTraversalMask);
        AddByteArray(hasher, world.SourceCostField);
        AddBoolArray(hasher, world.WalkableMask);
        AddByteArray(hasher, world.CostField);
        AddByteArray(hasher, world.NeighborTraversalMask);
        AddIntArray(hasher, world.IslandIds);
        AddByteArrayArray(hasher, world.SectorCostFields);
        AddNavigationAnchorFixedXZArray(hasher, world.CellNavAnchorsFixedXZ);

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
        hasher.Add(job.HasAuthorityInputHash);
        if (job.HasAuthorityInputHash)
            hasher.Add(job.AuthorityInputHash);
        AddQueue(hasher, job.IslandOpenQueue);
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
        hasher.Add(job.HasAuthorityInputHash);
        if (job.HasAuthorityInputHash)
            hasher.Add(job.AuthorityInputHash);
        AddSortedInts(hasher, job.DirtySectors);
        AddSortedInts(hasher, job.CostDirtySectors);
        AddSortedInts(hasher, job.PortalTransitionDirtySectors);
        AddQueue(hasher, job.IslandOpenQueue);
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
            hasher.Add(agent.AgentTypeId);
            hasher.Add(agent.HasNavigationIntent);
            hasher.Add(nav.CurrentSectorId);
            AddVector2Int(hasher, nav.CurrentCell);
            AddPathHandle(hasher, nav.PathHandle);
            AddVector3(hasher, nav.CurrentFlowDirection);
            AddVector3(hasher, nav.DesiredVelocity);
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
            AddPathHandle(hasher, job.HandleSnapshot);
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
        AddByteArray(hasher, tile.FlowFieldValues);
        AddIntArray(hasher, tile.DeterministicIntegrationCosts);
        AddByteArray(hasher, tile.DeterministicFlowDirectionIndices);
        AddVector2IntArray(hasher, tile.GoalCells);
        hasher.Add(tile.LastUsedFrame);
        hasher.Add(tile.LastReferencedFrame);
        hasher.Add(tile.ActiveReferenceCount);
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

    private static int CompareMovingTargetAnchorKeys(MovingTargetAnchorKey left, MovingTargetAnchorKey right)
    {
        int result = left.TargetId.CompareTo(right.TargetId);
        if (result != 0) return result;
        result = left.AgentTypeId.CompareTo(right.AgentTypeId);
        return result != 0 ? result : left.IslandId.CompareTo(right.IslandId);
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
        SortedIntValues.Clear();
        SortedIntValues.AddRange(values);
        SortedIntValues.Sort();
        hasher.Add(SortedIntValues.Count);
        for (int i = 0; i < SortedIntValues.Count; i++)
            hasher.Add(SortedIntValues[i]);
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
