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
#if UNITY_EDITOR
    public static bool TryGetEditorTestDesiredDirection(int agentId, out Vector3 direction)
    {
        direction = Vector3.zero;
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            return false;

        Vector3 desiredVelocity = agent.NavState.DesiredVelocity;
        if (desiredVelocity.sqrMagnitude <= 0.0001f)
            return false;

        direction = desiredVelocity.normalized;
        return true;
    }

    public static bool TryGetEditorTestLastSteeringDesiredDirection(int agentId, out Vector3 direction)
    {
        direction = Vector3.zero;
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            return false;

        AgentNavState nav = agent.NavState;
        if (nav.LastSteeringFrame < 0 || nav.LastSteeringDesiredDirection.sqrMagnitude <= 0.0001f)
            return false;

        direction = nav.LastSteeringDesiredDirection.normalized;
        return true;
    }

    public static int GetEditorTestStableGoalReachabilityReuseCount()
    {
        return _perf.StableGoalReachabilityReuse;
    }

    public static int GetEditorTestFlowLocalPotentialShapeCacheCount()
    {
        return FlowLocalPotentialShapes.Count;
    }

    public static int GetEditorTestFrameFlowLocalPotentialShapeHitCount()
    {
        return _perf.FlowLocalPotentialShapeHits;
    }

    public static int GetEditorTestFrameFlowLocalPotentialShapeMissCount()
    {
        return _perf.FlowLocalPotentialShapeMisses;
    }

    public static ulong[] GetEditorTestFlowLocalPotentialShapeAuthorityHashes()
    {
        var keys = new List<FlowLocalPotentialShapeKey>(FlowLocalPotentialShapes.Keys);
        keys.Sort(CompareFlowLocalPotentialShapeKeys);
        var hashes = new ulong[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            FlowLocalPotentialShape shape = FlowLocalPotentialShapes[keys[i]]
                                            ?? throw new InvalidOperationException("Flow local-potential shape cache contains null.");
            if (!shape.HasAuthorityContentHash)
                throw new InvalidOperationException("Flow local-potential shape cache contains unhashed payload.");
            hashes[i] = shape.AuthorityContentHash;
        }
        return hashes;
    }

    public static bool TryGetEditorTestActiveSteeringGoalIndices(
        int agentId,
        out int handleGoalIndex,
        out int steeringGoalIndex)
    {
        handleGoalIndex = -1;
        steeringGoalIndex = -1;
        if (_world == null
            || !Agents.TryGetValue(agentId, out AgentRuntimeData agent)
            || agent?.NavState?.PathHandle == null)
        {
            return false;
        }

        PathHandle handle = agent.NavState.PathHandle;
        handleGoalIndex = _world.GetIndex(handle.GoalX, handle.GoalY);
        ResolveSteeringReadCorridor(
            handle,
            out _,
            out _,
            out _,
            out int steeringGoalX,
            out int steeringGoalY,
            out _);
        steeringGoalIndex = _world.GetIndex(steeringGoalX, steeringGoalY);
        return true;
    }

    public static int GetEditorTestFrameFlowTileBuildOperationCount()
    {
        return _perf.FlowTileBuildOperations;
    }

    public static int GetEditorTestFramePortalSlotTraceOperationCount()
    {
        return _perf.PortalSlotTraceOperations;
    }

    public static int GetEditorTestFramePortalSlotFillOperationCount()
    {
        return _perf.PortalSlotFillOperations;
    }

    public static bool TryGetEditorTestCachedTileDeterministicPayload(
        int agentId,
        out int[] integrationCosts,
        out byte[] directionIndices,
        out ushort[] portalSlotIndices)
    {
        integrationCosts = null;
        directionIndices = null;
        portalSlotIndices = null;
        if (_world == null
            || !Agents.TryGetValue(agentId, out AgentRuntimeData agent)
            || agent?.NavState?.PathHandle == null)
        {
            return false;
        }
        PathHandle handle = agent.NavState.PathHandle;
        int sectorPathIndex = Mathf.Clamp(handle.CurrentSectorIndex, 0, handle.SectorIds.Length - 1);
        FlowTileCacheKey key = CreateTileCacheKeyForPathSegment(
            handle,
            sectorPathIndex,
            handle.GoalX,
            handle.GoalY,
            agent.AgentTypeId,
            out _,
            out _);
        if (!DeterministicFlowTileCache.TryGetValue(key, out FlowTileCacheEntry tile))
            return false;
        integrationCosts = new int[checked(tile.Width * tile.Height)];
        for (int i = 0; i < integrationCosts.Length; i++)
            integrationCosts[i] = GetDeterministicIntegrationCost(tile, i);
        directionIndices = (byte[])tile.DeterministicFlowDirectionIndices.Clone();
        portalSlotIndices = tile.DeterministicPortalTargetSlotIndices != null
            ? (ushort[])tile.DeterministicPortalTargetSlotIndices.Clone()
            : null;
        return true;
    }

    public static bool TryGetEditorTestCurrentTilePotentialBinding(
        int agentId,
        out bool usesPotentialShape,
        out bool ownsAbsoluteIntegrationArray,
        out int potentialOffset)
    {
        usesPotentialShape = false;
        ownsAbsoluteIntegrationArray = false;
        potentialOffset = 0;
        if (_world == null
            || !Agents.TryGetValue(agentId, out AgentRuntimeData agent)
            || agent?.NavState?.PathHandle == null)
        {
            return false;
        }
        PathHandle handle = agent.NavState.PathHandle;
        int sectorPathIndex = Mathf.Clamp(handle.CurrentSectorIndex, 0, handle.SectorIds.Length - 1);
        FlowTileCacheKey key = CreateTileCacheKeyForPathSegment(
            handle,
            sectorPathIndex,
            handle.GoalX,
            handle.GoalY,
            agent.AgentTypeId,
            out _,
            out _);
        if (!DeterministicFlowTileCache.TryGetValue(key, out FlowTileCacheEntry tile))
            return false;
        usesPotentialShape = tile.PotentialShape != null;
        ownsAbsoluteIntegrationArray = tile.DeterministicIntegrationCosts != null;
        potentialOffset = tile.PotentialOffset;
        return true;
    }

    public static bool TryGetEditorTestLastSteeringSource(int agentId, out int source, out bool hasLineOfSight)
    {
        source = (int)DesiredDirectionSource.Zero;
        hasLineOfSight = false;
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            return false;

        source = (int)agent.NavState.LastSteeringDesiredSource;
        hasLineOfSight = agent.NavState.LastSteeringHasLineOfSight;
        return agent.NavState.LastSteeringFrame >= 0;
    }

    public static bool TryGetEditorTestCurrentTileQueueState(
        int agentId,
        out string goalKind,
        out int downstreamPortalId,
        out bool cached,
        out bool pending,
        out int queueIndex,
        out string stage,
        out bool waitingForDependency,
        out bool stale,
        out int pendingPortalFrames)
    {
        goalKind = TileGoalKind.FinalGoal.ToString();
        downstreamPortalId = -1;
        cached = false;
        pending = false;
        queueIndex = -1;
        stage = "none";
        waitingForDependency = false;
        stale = false;
        pendingPortalFrames = 0;
        if (_world == null || !Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            return false;

        AgentNavState nav = agent.NavState;
        PathHandle handle = nav.PathHandle;
        if (handle == null || handle.SectorIds == null || handle.SectorIds.Length == 0)
            return false;

        int sectorPathIndex = Mathf.Clamp(handle.CurrentSectorIndex, 0, handle.SectorIds.Length - 1);
        FlowTileCacheKey key = CreateTileCacheKeyForPathSegment(
            handle,
            sectorPathIndex,
            handle.GoalX,
            handle.GoalY,
            agent.AgentTypeId,
            out TileGoalKind tileGoalKind,
            out downstreamPortalId);
        goalKind = tileGoalKind.ToString();
        cached = FlowTileCache.ContainsKey(key);
        pending = PendingFlowTileBuildJobs.Contains(key);
        return TryGetFlowTileJobQueueState(key, out queueIndex, out stage, out waitingForDependency, out stale) || cached || pending;
    }

    public static bool TryGetEditorTestSteeringWorkingSetTileState(
        int agentId,
        int corridorOffset,
        out bool cached,
        out bool pending,
        out int queueIndex,
        out string stage,
        out int cursor)
    {
        cached = false;
        pending = false;
        queueIndex = -1;
        stage = "none";
        cursor = -1;
        if (corridorOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(corridorOffset), corridorOffset, "Corridor offset cannot be negative.");
        if (_world == null || !Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            return false;

        PathHandle handle = agent.NavState.PathHandle;
        if (handle == null || handle.SectorIds == null || handle.SectorIds.Length == 0)
            return false;

        ResolveSteeringReadCorridor(
            handle,
            out ImmutableRouteSequence sectorIds,
            out ImmutableRouteSequence portalIds,
            out int startIndex,
            out int goalX,
            out int goalY,
            out bool usesCommittedCorridor);
        int sectorPathIndex = startIndex + corridorOffset;
        if (sectorPathIndex >= sectorIds.Length)
            return false;

        FlowTileCacheKey key = CreateSteeringReadDomainTileKey(
            handle,
            sectorIds,
            portalIds,
            sectorPathIndex,
            goalX,
            goalY,
            agent.AgentTypeId,
            usesCommittedCorridor);
        cached = FlowTileCache.ContainsKey(key);
        pending = PendingFlowTileBuildJobs.Contains(key);
        TryGetFlowTileJobQueueState(key, out queueIndex, out stage, out _, out _);
        if (queueIndex >= 0)
        {
            LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First;
            for (int index = 0; index < queueIndex; index++)
                node = node?.Next;
            cursor = node?.Value?.Cursor
                ?? throw new InvalidOperationException(
                    $"Steering working-set tile queue index is inconsistent. index={queueIndex}, key={FormatTileKey(key)}.");
        }
        return cached || pending;
    }

    public static bool TryGetEditorTestLastSteeringGoal(int agentId, out Vector3 goal)
    {
        goal = Vector3.zero;
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            return false;

        AgentNavState nav = agent.NavState;
        goal = nav.LastSteeringGoal;
        return nav.LastSteeringFrame >= 0;
    }

    public static bool TryGetEditorTestLastSteeringGoal(int agentId, out Vector3 goal, out int frame)
    {
        goal = Vector3.zero;
        frame = -1;
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            return false;

        AgentNavState nav = agent.NavState;
        goal = nav.LastSteeringGoal;
        frame = nav.LastSteeringFrame;
        return frame >= 0;
    }

    public static bool TryGetEditorTestStableGoal(
        int agentId,
        out int targetId,
        out int rawX,
        out int rawY,
        out int stableX,
        out int stableY,
        out Vector3 stableWorld)
    {
        targetId = int.MinValue;
        rawX = -1;
        rawY = -1;
        stableX = -1;
        stableY = -1;
        stableWorld = Vector3.zero;
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            return false;

        AgentNavState nav = agent.NavState;
        targetId = nav.StableGoalTargetId;
        rawX = nav.StableGoalRawX;
        rawY = nav.StableGoalRawY;
        stableX = nav.StableGoalX;
        stableY = nav.StableGoalY;
        stableWorld = nav.StableGoalWorld;
        return stableX >= 0 && stableY >= 0;
    }

    public static bool TryGetEditorTestLastSteeringDiagnostic(
        int agentId,
        out Vector3 desiredVelocity,
        out Vector3 baseVelocity,
        out Vector3 resultPreClamp,
        out Vector3 result)
    {
        desiredVelocity = Vector3.zero;
        baseVelocity = Vector3.zero;
        resultPreClamp = Vector3.zero;
        result = Vector3.zero;
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            return false;

        AgentNavState nav = agent.NavState;
        desiredVelocity = nav.LastSteeringDesiredVelocity;
        baseVelocity = nav.LastSteeringBaseVelocity;
        resultPreClamp = nav.LastSteeringResultPreClamp;
        result = nav.LastSteeringResult;
        return nav.LastSteeringFrame >= 0;
    }

    public static bool TryGetEditorTestNavigationClearance(
        Vector3 point,
        float clearance,
        out bool isClear,
        out float violation,
        out int runtimeBoxCount,
        out int runtimeCircleCount)
    {
        return TryGetNavigationPointClearance(
            point,
            clearance,
            out isClear,
            out violation,
            out runtimeBoxCount,
            out runtimeCircleCount);
    }

    public static bool TryGetEditorTestNavigationSegmentDiagnostics(
        Vector3 position,
        Vector3 desiredDisplacement,
        int agentTypeId,
        float edgeClearance,
        out string diagnostics)
    {
        diagnostics = string.Empty;
        if (!TryGetCommittedNavigationQueryWorld(agentTypeId, allowSynchronousBuild: true, out NavigationWorld world))
            return false;

        bool includeRuntimeObstacleOverlay = HasPendingRuntimeDirty(_activeWorldState);
        float queryClearance = ResolveNavigationQueryClearance(world, Mathf.Max(0f, edgeClearance));
        diagnostics = BuildNavigationSegmentWalkableDiagnostics(
            world,
            position,
            desiredDisplacement,
            queryClearance,
            requireClearStart: true,
            includeRuntimeObstacleOverlay: includeRuntimeObstacleOverlay);
        return true;
    }

    public static int GetEditorTestFlowTileCacheCount()
    {
        return FlowTileCache.Count;
    }

    public static string GetEditorTestFlowTileRetentionDiagnostic()
    {
        RefreshPendingFlowTileDependencyKeys();
        var entries = new List<FlowTileCacheKey>(FlowTileCache.Keys);
        entries.Sort(CompareFlowTileCacheKeys);
        var builder = new System.Text.StringBuilder(entries.Count * 96);
        for (int i = 0; i < entries.Count; i++)
        {
            FlowTileCacheKey key = entries[i];
            FlowTileCacheEntry tile = FlowTileCache[key];
            if (i > 0)
                builder.AppendLine();
            builder.Append(FormatTileKey(key))
                .Append(" active=").Append(tile.ActiveReferenceCount)
                .Append(" dependency=").Append(PendingFlowTileDependencyKeys.Contains(key))
                .Append(" used=").Append(tile.LastUsedFrame)
                .Append(" referenced=").Append(tile.LastReferencedFrame);
        }
        return builder.ToString();
    }

    public static int GetEditorTestFlowTileCacheLimit()
    {
        return Config.FlowTileCacheLimit;
    }

    public static int GetEditorTestDeterministicFlowTileCacheCount()
    {
        return DeterministicFlowTileCache.Count;
    }

    public static ulong GetEditorTestDeterministicFlowTileAuthorityContentHash()
    {
        return _deterministicFlowTileAuthorityContentHash;
    }

    public static bool TryValidateEditorTestPortalWindowTilePayloads(
        out int validatedPortalTileCount,
        out string failureReason)
    {
        validatedPortalTileCount = 0;
        failureReason = null;
        if (_world == null)
        {
            failureReason = "Committed navigation world is unavailable.";
            return false;
        }

        foreach (KeyValuePair<FlowTileCacheKey, FlowTileCacheEntry> pair in DeterministicFlowTileCache)
        {
            FlowTileCacheKey key = pair.Key;
            FlowTileCacheEntry tile = pair.Value;
            if (key.GoalKind != TileGoalKind.Portal)
                continue;
            if (tile?.GoalCells == null
                || tile.DeterministicPortalTargetSlotIndices == null
                || tile.DeterministicPortalTargetSlotIndices.Length != tile.Width * tile.Height
                || tile.DeterministicFlowDirectionIndices == null
                || tile.DeterministicFlowDirectionIndices.Length != tile.Width * tile.Height)
            {
                failureReason = $"Portal-window payload shape is invalid key={FormatTileKey(key)}.";
                return false;
            }
            validatedPortalTileCount++;
        }

        return true;
    }

    public static void SetEditorTestOnlyDeterministicTileRetentionFrames(int lastUsedFrame, int lastReferencedFrame)
    {
        if (DeterministicFlowTileCache.Count != 1)
            throw new InvalidOperationException($"SetEditorTestOnlyDeterministicTileRetentionFrames failed: expected one tile, actual={DeterministicFlowTileCache.Count}.");

        foreach (FlowTileCacheEntry tile in DeterministicFlowTileCache.Values)
        {
            if (tile == null)
                throw new InvalidOperationException("SetEditorTestOnlyDeterministicTileRetentionFrames failed: tile is null.");
            tile.LastUsedFrame = lastUsedFrame;
            tile.LastReferencedFrame = lastReferencedFrame;
            return;
        }

        throw new InvalidOperationException("SetEditorTestOnlyDeterministicTileRetentionFrames failed: tile enumeration was empty.");
    }

    public static ulong GetEditorTestSectorPathAuthorityContentHash()
    {
        return _sectorPathAuthorityContentHash;
    }

    public static int GetEditorTestSectorPathTrimVictim(bool reverseInsertion)
    {
        if (_world == null || _world.Sectors == null || _world.Sectors.Length == 0)
            throw new InvalidOperationException("GetEditorTestSectorPathTrimVictim failed: world is unavailable.");

        ClearSectorPathCache();
        int entryCount = Mathf.Max(32, Config.FlowTileCacheLimit * 2) + 1;
        for (int insertionIndex = 0; insertionIndex < entryCount; insertionIndex++)
        {
            int cellIndex = reverseInsertion ? entryCount - insertionIndex - 1 : insertionIndex;
            var key = new SectorPathCacheKey(_world.Version, 0, cellIndex, 0, cellIndex, 0, 0);
            SetSectorPathCacheEntry(key, new SectorPathCacheEntry
            {
                SectorIds = new[] { 0 },
                PortalIds = Array.Empty<int>(),
                LastUsedFrame = 17
            });
        }

        TrimSectorPathCache();
        int victim = -1;
        for (int cellIndex = 0; cellIndex < entryCount; cellIndex++)
        {
            var key = new SectorPathCacheKey(_world.Version, 0, cellIndex, 0, cellIndex, 0, 0);
            if (SectorPathCache.ContainsKey(key))
                continue;
            if (victim >= 0)
                throw new InvalidOperationException("GetEditorTestSectorPathTrimVictim failed: trim removed multiple entries.");
            victim = cellIndex;
        }

        if (victim < 0)
            throw new InvalidOperationException("GetEditorTestSectorPathTrimVictim failed: trim removed no entry.");
        return victim;
    }

    public static ulong GetEditorTestSectorPortalAccessAuthorityContentHash()
    {
        return _sectorPortalAccessAuthorityContentHash;
    }

    public static ulong GetEditorTestCommittedWorldSetHash()
    {
        return _committedWorldSetHash;
    }

    public static long GetEditorTestGridRawFromFloat(float value)
    {
        return NavigationWorld.FloatToGridRaw(value);
    }

    public static int[] RebuildEditorTestPortalArrayWithReverseLookupInsertion()
    {
        if (_world?.PortalsById == null || _world.PortalsById.Count == 0)
            throw new InvalidOperationException("RebuildEditorTestPortalArrayWithReverseLookupInsertion failed: portal lookup is unavailable.");

        var portalIds = new List<int>(_world.PortalsById.Keys);
        portalIds.Sort();
        var reversedLookup = new Dictionary<int, PortalData>(_world.PortalsById.Count);
        for (int i = portalIds.Count - 1; i >= 0; i--)
        {
            int portalId = portalIds[i];
            reversedLookup.Add(portalId, _world.PortalsById[portalId]);
        }

        _world.PortalsById = reversedLookup;
        _world.Portals = BuildPortalArray(_world);
        RefreshNavigationWorldDeterministicHash(_world);

        var rebuiltPortalIds = new int[_world.Portals.Length];
        for (int i = 0; i < _world.Portals.Length; i++)
            rebuiltPortalIds[i] = _world.Portals[i].PortalId;
        return rebuiltPortalIds;
    }

    public static void PerturbEditorTestWorldGridFloatShadows(float cellSize, Vector3 origin)
    {
        if (_world == null)
            throw new InvalidOperationException("PerturbEditorTestWorldGridFloatShadows failed: world is unavailable.");
        if (float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0f || !IsFiniteVector3(origin))
            throw new ArgumentOutOfRangeException(nameof(cellSize));

        _world.CellSize = cellSize;
        _world.Origin = origin;
        RefreshNavigationWorldDeterministicHash(_world);
    }

    public static FixVector2 GetEditorTestGridToWorldCenterFixed(int x, int y)
    {
        if (_world == null)
            throw new InvalidOperationException("GetEditorTestGridToWorldCenterFixed failed: world is unavailable.");
        return _world.GridToWorldCenterFixed(x, y);
    }

    public static void GetEditorTestSpatialGradientSampleGridFixed(
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
        if (_world == null)
            throw new InvalidOperationException("GetEditorTestSpatialGradientSampleGridFixed failed: world is unavailable.");
        ResolveSpatialGradientSampleGridFixed(position, worldX, worldY, out x0, out x1, out y0, out y1, out tx, out ty);
    }

    public static void PerturbEditorTestWorldAnchorFloatShadows(float xOffset, float zOffset)
    {
        if (_world?.CellNavAnchors == null)
            throw new InvalidOperationException("PerturbEditorTestWorldAnchorFloatShadows failed: anchors are unavailable.");
        if (float.IsNaN(xOffset) || float.IsInfinity(xOffset) || float.IsNaN(zOffset) || float.IsInfinity(zOffset))
            throw new ArgumentOutOfRangeException(nameof(xOffset));

        for (int i = 0; i < _world.CellNavAnchors.Length; i++)
        {
            Vector3 anchor = _world.CellNavAnchors[i];
            _world.CellNavAnchors[i] = new Vector3(anchor.x + xOffset, anchor.y, anchor.z + zOffset);
        }
        RefreshNavigationWorldDeterministicHash(_world);
    }

    public static void SetEditorTestWorldAnchorFloatShadow(int index, Vector3 value)
    {
        if (_world?.CellNavAnchors == null)
            throw new InvalidOperationException("SetEditorTestWorldAnchorFloatShadow failed: anchors are unavailable.");
        if (index < 0 || index >= _world.CellNavAnchors.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        _world.CellNavAnchors[index] = value;
        RefreshNavigationWorldDeterministicHash(_world);
    }

    public static ulong GetEditorTestAuthorityWorldProgressHash()
    {
        var hasher = new LogicStateHasher();
        AddAuthorityWorldProgress(hasher);
        return hasher.Hash;
    }

    public static bool TryBuildEditorTestPortalPath(
        int startX,
        int startY,
        int goalX,
        int goalY,
        out int[] portalIds)
    {
        portalIds = null;
        if (_world == null)
            throw new InvalidOperationException("TryBuildEditorTestPortalPath failed: world is unavailable.");
        if (!_world.TryGetSectorId(startX, startY, out int startSectorId))
            throw new ArgumentOutOfRangeException(nameof(startX), $"Start cell is outside the committed world: ({startX},{startY}).");
        if (!_world.TryGetSectorId(goalX, goalY, out int goalSectorId))
            throw new ArgumentOutOfRangeException(nameof(goalX), $"Goal cell is outside the committed world: ({goalX},{goalY}).");

        PathHandle handle = BuildPathHandle(startSectorId, goalSectorId, startX, startY, goalX, goalY, useSectorCorridorPolicy: false);
        if (handle == null)
            return false;

        portalIds = (int[])handle.PortalIds.Clone();
        return true;
    }

    public static void ClearEditorTestSectorPathCache()
    {
        ClearSectorPathCache();
    }

    public static ulong GetEditorTestSharedGoalFieldAuthorityContentHash()
    {
        return _sharedGoalFieldAuthorityContentHash;
    }

    public static int GetEditorTestSharedGoalFieldTrimVictim(bool reverseInsertion)
    {
        if (_world == null || _world.Sectors == null || _world.Sectors.Length == 0)
            throw new InvalidOperationException("GetEditorTestSharedGoalFieldTrimVictim failed: world is unavailable.");

        ClearSharedGoalFieldCache();
        int entryCount = Mathf.Max(16, Config.FlowTileCacheLimit / 4) + 1;
        int dirtyVersion = _world.Sectors[0].DirtyVersion;
        for (int insertionIndex = 0; insertionIndex < entryCount; insertionIndex++)
        {
            int cellIndex = reverseInsertion ? entryCount - insertionIndex - 1 : insertionIndex;
            var key = new SharedGoalFieldKey(_world.Version, _world.AgentTypeId, 0, cellIndex, dirtyVersion);
            SetSharedGoalFieldCacheEntry(key, new SharedGoalField
            {
                Key = key,
                GoalSectorId = 0,
                GoalX = cellIndex,
                GoalY = 0,
                LastUsedFrame = 23
            });
        }

        TrimSharedGoalFields();
        int victim = -1;
        for (int cellIndex = 0; cellIndex < entryCount; cellIndex++)
        {
            var key = new SharedGoalFieldKey(_world.Version, _world.AgentTypeId, 0, cellIndex, dirtyVersion);
            if (SharedGoalFields.ContainsKey(key))
                continue;
            if (victim >= 0)
                throw new InvalidOperationException("GetEditorTestSharedGoalFieldTrimVictim failed: trim removed multiple entries.");
            victim = cellIndex;
        }

        if (victim < 0)
            throw new InvalidOperationException("GetEditorTestSharedGoalFieldTrimVictim failed: trim removed no entry.");
        return victim;
    }

    public static int GetEditorTestStartPortalChoiceTrimVictim(bool reverseInsertion)
    {
        if (_world == null || _world.Sectors == null || _world.Sectors.Length == 0)
            throw new InvalidOperationException("GetEditorTestStartPortalChoiceTrimVictim failed: world is unavailable.");

        StartPortalChoiceCache.Clear();
        int entryCount = Mathf.Max(128, Config.FlowTileCacheLimit * 8) + 1;
        for (int insertionIndex = 0; insertionIndex < entryCount; insertionIndex++)
        {
            int cellIndex = reverseInsertion ? entryCount - insertionIndex - 1 : insertionIndex;
            var key = new StartPortalChoiceKey(_world.Version, 0, cellIndex, 0, cellIndex, _world.AgentTypeId, 0, 0, 0);
            StartPortalChoiceCache.Add(key, new StartPortalChoiceEntry
            {
                BestPortalId = 0,
                SelectedCost = 1,
                BestCost = 1,
                LastUsedFrame = 29
            });
        }

        TrimStartPortalChoiceCache();
        int victim = -1;
        for (int cellIndex = 0; cellIndex < entryCount; cellIndex++)
        {
            var key = new StartPortalChoiceKey(_world.Version, 0, cellIndex, 0, cellIndex, _world.AgentTypeId, 0, 0, 0);
            if (StartPortalChoiceCache.ContainsKey(key))
                continue;
            if (victim >= 0)
                throw new InvalidOperationException("GetEditorTestStartPortalChoiceTrimVictim failed: trim removed multiple entries.");
            victim = cellIndex;
        }

        if (victim < 0)
            throw new InvalidOperationException("GetEditorTestStartPortalChoiceTrimVictim failed: trim removed no entry.");
        return victim;
    }

    public static int[] GetEditorTestRuntimeDirtySectorIds(int agentTypeId)
    {
        int resolvedAgentTypeId = ResolvePreferredAgentTypeId(agentTypeId);
        if (!WorldStates.TryGetValue(resolvedAgentTypeId, out WorldRuntimeState state) || state == null)
            throw new InvalidOperationException($"GetEditorTestRuntimeDirtySectorIds failed: world state is unavailable agentType={resolvedAgentTypeId}.");

        var result = new List<int>(state.DirtyRuntimeObstacleSectors);
        result.Sort();
        return result.ToArray();
    }

    public static int GetEditorTestRuntimeDirtyPortalTransitionSourceQuota()
    {
        return RuntimeDirtyPortalTransitionSourceQuota;
    }

    public static void PerturbEditorTestAgentFloatPosition(int agentId, Vector3 position)
    {
        if (!IsFiniteVector3(position))
            throw new ArgumentOutOfRangeException(nameof(position));
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent) || agent == null)
            throw new InvalidOperationException($"PerturbEditorTestAgentFloatPosition failed: agent is unavailable id={agentId}.");
        agent.Position = position;
    }

    public static int GetEditorTestRuntimeDirtyPriority(int agentTypeId)
    {
        int resolvedAgentTypeId = ResolvePreferredAgentTypeId(agentTypeId);
        if (!WorldStates.TryGetValue(resolvedAgentTypeId, out WorldRuntimeState state) || state == null)
            throw new InvalidOperationException($"GetEditorTestRuntimeDirtyPriority failed: world state is unavailable agentType={resolvedAgentTypeId}.");
        return ResolveRuntimeDirtyPriority(state);
    }

    public static ulong GetEditorTestCostStampAuthorityContentHash()
    {
        return _costStampAuthorityContentHash;
    }

    public static int GetEditorTestRuntimeCostStampCount()
    {
        return CostStamps.Count;
    }

    public static int GetEditorTestCombatTargetSlotCacheCount()
    {
        return CombatTargetSlotCache.Count;
    }

    public static int GetEditorTestAttackAreaCandidateCacheBuildCount()
    {
        return _attackAreaCandidateCacheBuildCount;
    }

    public static int GetEditorTestTargetingReachabilityCacheMissCount()
    {
        return _targetingReachabilityCacheMissCount;
    }

    public static ulong GetEditorTestSharedGoalBuildQueueAuthorityHash()
    {
        var hasher = new LogicStateHasher();
        AddAuthoritySharedGoalFieldBuildQueue(hasher);
        return hasher.Hash;
    }

    public static int GetEditorTestSharedGoalBuildJobAuthorityHashRefreshCount()
    {
        return _sharedGoalBuildJobAuthorityHashRefreshCount;
    }

    public static long GetEditorTestSharedGoalBuildJobAuthorityHashVisitedEntryCount()
    {
        return _sharedGoalBuildJobAuthorityHashVisitedEntryCount;
    }

    public static long GetEditorTestNavigationSharedRouteSuffixDigestVisitedEntryCount()
    {
        return _navigationSharedRouteSuffixDigestVisitedEntryCount;
    }

    public static int GetEditorTestReleasedIntegrationTileCount()
    {
        return 0;
    }

    public static int GetEditorTestDebugIntegrationPayloadTileCount()
    {
        int count = 0;
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (tile.DebugIntegrationPayload?.Values != null)
                count++;
        }

        return count;
    }

    public static bool TryRebuildEditorDebugTileIntegration(int worldX, int worldY)
    {
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY))
                continue;

            return TryRebuildTileDebugIntegration(tile);
        }

        return false;
    }

    public static bool TryGetEditorTestDebugIntegrationCost(int worldX, int worldY, out float cost)
    {
        cost = float.PositiveInfinity;
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY) || tile.DebugIntegrationPayload?.Values == null)
                continue;

            cost = tile.DebugIntegrationPayload.Values[tile.GetLocalIndex(worldX, worldY)];
            return true;
        }

        return false;
    }

    public static int GetEditorTestIntegrationArrayPoolCount()
    {
        int count = 0;
        foreach (Stack<float[]> stack in IntegrationArrayPool.Values)
            count += stack.Count;

        return count;
    }

    public static int GetEditorTestPortalAccessIntegrationArrayPoolCount()
    {
        int count = 0;
        foreach (Stack<long[]> stack in PortalAccessIntegrationArrayPool.Values)
            count += stack.Count;

        return count;
    }

    public static void ClearEditorTestFlowTileCache()
    {
        ClearFlowTileCache();
        ReturnPendingFlowTileBuildIntegrations();
        FlowTileBuildQueue.Clear();
        PendingFlowTileBuildJobs.Clear();
        ClearFlowTileBuildSchedulingState();
    }

    public static bool HasEditorTestWorld()
    {
        return _world != null;
    }

    public static bool HasEditorTestPendingWorldBuild()
    {
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state != null && state.BuildJob != null)
                return true;
        }

        return false;
    }

    public static string GetEditorTestPendingWorldBuildProgressSignature()
    {
        WorldBuildJob found = null;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state?.BuildJob == null)
                continue;
            if (found != null)
                throw new InvalidOperationException("GetEditorTestPendingWorldBuildProgressSignature failed: multiple jobs are pending.");
            found = state.BuildJob;
        }

        if (found == null)
            throw new InvalidOperationException("GetEditorTestPendingWorldBuildProgressSignature failed: no job is pending.");
        return $"{found.AgentTypeId}:{found.Stage}:{found.ObstacleCursor}:{found.ApplyingCircleObstacles}:{found.SectorCursor}:{found.CellCursor}:{found.IslandScanIndex}:{found.IslandCurrentId}:{found.IslandCurrentSize}:{found.PortalStage}:{found.PortalAddCursor}:{found.PortalTransitionCursor}:{found.PortalTransitionFromCursor}:{FormatPortalHierarchyBuildProgress(found.HierarchyBuildJob)}";
    }

    public static int GetEditorTestPendingWorldBuildIslandQueueCount()
    {
        WorldBuildJob found = null;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state?.BuildJob == null)
                continue;
            if (found != null)
                throw new InvalidOperationException("GetEditorTestPendingWorldBuildIslandQueueCount failed: multiple jobs are pending.");
            found = state.BuildJob;
        }

        if (found == null)
            throw new InvalidOperationException("GetEditorTestPendingWorldBuildIslandQueueCount failed: no job is pending.");
        return found.IslandOpenQueue?.Count ?? 0;
    }

    public static bool TryGetEditorTestPendingWorldBuildHierarchySourceProgress(
        out int sourceNode,
        out int expansionCount,
        out int remainingTargets,
        out int openCount)
    {
        sourceNode = 0;
        expansionCount = 0;
        remainingTargets = 0;
        openCount = 0;
        WorldBuildJob found = null;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state?.BuildJob == null)
                continue;
            if (found != null)
                throw new InvalidOperationException("Editor test requires at most one pending world-build job.");
            found = state.BuildJob;
        }

        PortalHierarchySourceBuildJob source = found?.HierarchyBuildJob?.CurrentSourceBuild;
        if (found?.Stage != WorldBuildStage.Hierarchy || source == null)
            return false;
        sourceNode = source.SourceNode;
        expansionCount = source.Search.ExpansionCount;
        remainingTargets = source.RemainingTargets;
        openCount = source.OpenSet.Count;
        return true;
    }

    public static void PerturbEditorTestPendingWorldBuildIslandQueueOrder()
    {
        WorldBuildJob found = null;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state?.BuildJob == null)
                continue;
            if (found != null)
                throw new InvalidOperationException("PerturbEditorTestPendingWorldBuildIslandQueueOrder failed: multiple jobs are pending.");
            found = state.BuildJob;
        }

        if (found?.IslandOpenQueue == null || found.IslandOpenQueue.Count < 2)
            throw new InvalidOperationException("PerturbEditorTestPendingWorldBuildIslandQueueOrder failed: island queue needs at least two cells.");

        int[] values = found.IslandOpenQueue.ToArray();
        found.IslandOpenQueue.Clear();
        found.IslandOpenQueue.Enqueue(values[1]);
        found.IslandOpenQueue.Enqueue(values[0]);
        for (int i = 2; i < values.Length; i++)
            found.IslandOpenQueue.Enqueue(values[i]);
    }

    public static bool HasEditorTestPendingRuntimeDirty()
    {
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state == null)
                continue;
            if (state.RuntimeDirtyJob != null || state.DirtyRuntimeObstacleSectors.Count > 0)
                return true;
        }

        return false;
    }

    public static string GetEditorTestPendingRuntimeDirtyProgressSignature()
    {
        RuntimeDirtyRebuildJob found = null;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state?.RuntimeDirtyJob == null)
                continue;
            if (found != null)
                throw new InvalidOperationException("GetEditorTestPendingRuntimeDirtyProgressSignature failed: multiple jobs are pending.");
            found = state.RuntimeDirtyJob;
        }

        if (found == null)
            throw new InvalidOperationException("GetEditorTestPendingRuntimeDirtyProgressSignature failed: no job is pending.");
        return $"{found.Stage}:{found.CloneShellStage}:{found.CloneCellCursor}:{found.CloneSectorCursor}:{found.CloneShellInitialized}:{found.SectorCursor}:{found.ObstacleCursor}:{found.ApplyingCircleObstacles}:{found.IslandStage}:{found.IslandSectorCursor}:{found.IslandComponentCursor}:{found.IslandBoundaryCursor}:{found.IslandNodeCursor}:{found.IslandRootHeapCount}:{found.IslandMappingNeedsWrite}:{found.PortalStage}:{found.PortalSectorCursor}:{found.PortalAddCursor}:{found.PortalTransitionCursor}:{found.PortalTransitionFromCursor}:{FormatPortalHierarchyBuildProgress(found.HierarchyBuildJob)}";
    }

#endif
}
