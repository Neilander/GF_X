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
    public static int GetEditorTestResolvedSectorSizeInCells(float cellSize)
    {
        return ResolveRuntimeSectorSizeInCells(NavigationGridFixedMath.FloatToGridRaw(cellSize));
    }

    public static int[] GetEditorTestMinHeapPopOrder(int[] indices, float[] costs)
    {
        if (indices == null)
            throw new ArgumentNullException(nameof(indices));
        if (costs == null)
            throw new ArgumentNullException(nameof(costs));
        if (indices.Length != costs.Length)
            throw new ArgumentException("Heap test input lengths must match.");

        var heap = new MinHeap();
        for (int i = 0; i < indices.Length; i++)
            heap.Push(indices[i], costs[i]);

        var result = new int[indices.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = heap.Pop().Index;
        return result;
    }

    public static int[] GetEditorTestDeterministicCostHeapPopOrder(int[] indices, long[] costs)
    {
        if (indices == null)
            throw new ArgumentNullException(nameof(indices));
        if (costs == null)
            throw new ArgumentNullException(nameof(costs));
        if (indices.Length != costs.Length)
            throw new ArgumentException("Deterministic cost heap test input lengths must match.");

        var heap = new DeterministicCostHeap();
        for (int i = 0; i < indices.Length; i++)
            heap.Push(indices[i], costs[i]);

        var result = new int[indices.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = heap.Pop().Index;
        return result;
    }

    public static int GetEditorTestWallCostPenalty(long distanceRaw, long radiusRaw, int outerPenalty, int adjacentPenalty)
    {
        return ResolveWallCostPenalty(distanceRaw, radiusRaw, outerPenalty, adjacentPenalty);
    }

    public static void SetEditorTestNavigationSource(int width, int height, float cellSize, Vector3 origin, bool[] walkableMask)
    {
        SetEditorTestNavigationSource(AnyAgentTypeId, width, height, cellSize, origin, walkableMask);
    }

    public static void SetEditorTestNavigationSource(int agentTypeId, int width, int height, float cellSize, Vector3 origin, bool[] walkableMask)
    {
        SetEditorTestNavigationSource(agentTypeId, width, height, cellSize, origin, walkableMask, null);
    }

    public static void SetEditorTestNavigationSource(int width, int height, float cellSize, Vector3 origin, bool[] walkableMask, Vector3[] cellNavAnchors)
    {
        SetEditorTestNavigationSource(AnyAgentTypeId, width, height, cellSize, origin, walkableMask, cellNavAnchors);
    }

    public static void SetEditorTestNavigationSource(int agentTypeId, int width, int height, float cellSize, Vector3 origin, bool[] walkableMask, Vector3[] cellNavAnchors)
    {
        SetEditorTestNavigationSource(agentTypeId, width, height, cellSize, origin, walkableMask, cellNavAnchors, null);
    }

    public static void SetEditorTestNavigationSource(int agentTypeId, int width, int height, float cellSize, Vector3 origin, bool[] walkableMask, Vector3[] cellNavAnchors, byte[] costField)
    {
        if (agentTypeId == MAEntity.UnknownNavAgentTypeId)
            throw new InvalidOperationException("SetEditorTestNavigationSource failed: explicit agentTypeId is Unknown. Use the overload without agentTypeId for Any test source.");
        if (walkableMask == null)
            throw new InvalidOperationException("SetEditorTestNavigationSource failed: walkableMask is null.");
        if (walkableMask.Length != width * height)
            throw new InvalidOperationException(
                $"SetEditorTestNavigationSource failed: mask length {walkableMask.Length} does not match {width}x{height}.");
        if (cellNavAnchors != null && cellNavAnchors.Length != width * height)
            throw new InvalidOperationException(
                $"SetEditorTestNavigationSource failed: anchor length {cellNavAnchors.Length} does not match {width}x{height}.");
        if (costField != null && costField.Length != width * height)
            throw new InvalidOperationException(
                $"SetEditorTestNavigationSource failed: cost length {costField.Length} does not match {width}x{height}.");

        _testTerrainOverride = CreateTerrainOverride(agentTypeId, width, height, cellSize, origin, walkableMask, cellNavAnchors, costField, null);
        AuthoredTerrainSources.Clear();
        MarkWorldDirty();
    }

    public static void ClearEditorTestNavigationSource()
    {
        _testTerrainOverride = null;
        AuthoredTerrainSources.Clear();
        MarkWorldDirty();
    }

    public static void SetEditorTestAgentTypeRadius(int agentTypeId, float radius)
    {
        SetEditorTestAgentTypeRadiusFixed(agentTypeId, (Fix64)radius);
    }

    public static void SetEditorTestAgentTypeRadiusFixed(int agentTypeId, Fix64 radius)
    {
        if (radius <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(radius), radius.RawValue, "Editor test agent radius must be positive.");

        TestAgentTypeRadii[agentTypeId] = radius;
        MarkWorldDirty();
    }

    public static void ClearEditorTestAgentTypeRadii()
    {
        TestAgentTypeRadii.Clear();
        MarkWorldDirty();
    }

    public static void AddEditorTestOnlyWorldBuildPortalProgressProbe()
    {
        WorldBuildJob job = GetRequiredSingleEditorTestWorldBuildJob();
        job.PortalRebuiltPortals ??= new List<PortalData>();
        job.PortalRebuiltPortals.Add(CreateEditorTestPortalProgressProbe());
    }

    public static void InvalidateEditorTestOnlyWorldBuildPortalProgressProbeCells()
    {
        WorldBuildJob job = GetRequiredSingleEditorTestWorldBuildJob();
        if (job.PortalRebuiltPortals == null || job.PortalRebuiltPortals.Count == 0)
            throw new InvalidOperationException("World-build portal cell probe requires pending portal progress.");
        job.PortalRebuiltPortals[job.PortalRebuiltPortals.Count - 1].CellsA = null;
    }

    public static void AddEditorTestOnlyWorldBuildPendingPortalAccessProbe()
    {
        WorldBuildJob job = GetRequiredSingleEditorTestWorldBuildJob();
        job.PendingPortalAccessEntries ??= new List<PendingSectorPortalAccess>();
        job.PendingPortalAccessEntries.Add(new PendingSectorPortalAccess(701, 702, 703, true));
    }

    public static void AddEditorTestOnlyRuntimeDirtyProcessedBoundaryProbe()
    {
        RuntimeDirtyRebuildJob job = GetRequiredSingleEditorTestRuntimeDirtyJob();
        job.PortalProcessedBoundaries ??= new HashSet<long>();
        if (!job.PortalProcessedBoundaries.Add(0x1020304050607080L))
            throw new InvalidOperationException("Runtime-dirty boundary probe was already present.");
    }

    public static bool HasEditorTestOnlyWorldBuildWorkingWorld()
    {
        WorldBuildJob result = null;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state?.BuildJob == null)
                continue;
            if (result != null)
                throw new InvalidOperationException("Editor test requires at most one pending world-build job.");
            result = state.BuildJob;
        }

        return result?.WorkingWorld != null;
    }

    public static void PerturbEditorTestOnlyWorldBuildWorkingWorldFloatShadow()
    {
        NavigationWorld world = GetRequiredSingleEditorTestWorldBuildJob().WorkingWorld
                                ?? throw new InvalidOperationException("World-build working-world float probe requires an initialized working world.");
        world.CellSize += 0.125f;
        world.Origin += new Vector3(0.25f, 0f, 0.5f);
    }

    public static void PerturbEditorTestOnlyWorldBuildWorkingWorldAuthorityCell()
    {
        NavigationWorld world = GetRequiredSingleEditorTestWorldBuildJob().WorkingWorld
                                ?? throw new InvalidOperationException("World-build working-world authority probe requires an initialized working world.");
        if (world.WalkableMask == null || world.WalkableMask.Length == 0)
            throw new InvalidOperationException("World-build working-world authority probe requires a walkable mask.");
        world.WalkableMask[0] = !world.WalkableMask[0];
    }

    public static bool HasEditorTestOnlyRuntimeDirtyWorkingWorld()
    {
        RuntimeDirtyRebuildJob result = null;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state?.RuntimeDirtyJob == null)
                continue;
            if (result != null)
                throw new InvalidOperationException("Editor test requires at most one pending runtime-dirty job.");
            result = state.RuntimeDirtyJob;
        }

        return result?.WorkingWorld != null;
    }

    public static void PerturbEditorTestOnlyRuntimeDirtyWorkingWorldFloatShadow()
    {
        NavigationWorld world = GetRequiredSingleEditorTestRuntimeDirtyJob().WorkingWorld
                                ?? throw new InvalidOperationException("Runtime-dirty working-world float probe requires an initialized working world.");
        world.CellSize += 0.125f;
        world.Origin += new Vector3(0.25f, 0f, 0.5f);
    }

    public static void PerturbEditorTestOnlyRuntimeDirtyWorkingWorldAuthorityCell()
    {
        NavigationWorld world = GetRequiredSingleEditorTestRuntimeDirtyJob().WorkingWorld
                                ?? throw new InvalidOperationException("Runtime-dirty working-world authority probe requires an initialized working world.");
        if (world.WalkableMask == null || world.WalkableMask.Length == 0)
            throw new InvalidOperationException("Runtime-dirty working-world authority probe requires a walkable mask.");
        world.WalkableMask[0] = !world.WalkableMask[0];
    }

    public static void PerturbEditorTestOnlyRuntimeDirtyDerivedSectorIndex()
    {
        RuntimeDirtyRebuildJob job = GetRequiredSingleEditorTestRuntimeDirtyJob();
        if (job.DirtySectorIds == null || job.DirtySectorIds.Count == 0)
            throw new InvalidOperationException("Runtime-dirty derived-sector probe requires at least one dirty sector.");
        job.DirtySectorIds[0] = job.DirtySectorIds[0] == int.MaxValue
            ? int.MinValue
            : job.DirtySectorIds[0] + 1;
    }

    public static void AddEditorTestOnlyFlowTileMirrorExtraEntry()
    {
        if (DeterministicFlowTileCache.Count != 1 || FlowTileCache.Count != 1)
        {
            throw new InvalidOperationException(
                $"Flow-tile mirror probe requires exactly one committed tile. view={FlowTileCache.Count}, authority={DeterministicFlowTileCache.Count}.");
        }

        foreach (FlowTileCacheKey key in DeterministicFlowTileCache.Keys)
        {
            var extraKey = new FlowTileCacheKey(
                key.WorldVersion,
                key.SectorId,
                key.GoalKind,
                key.GoalId,
                key.FinalGoalIndex,
                key.AgentTypeId,
                checked(key.DirtyVersion + 1));
            FlowTileCache.Add(extraKey, new FlowTileCacheEntry { Key = extraKey });
            return;
        }

        throw new InvalidOperationException("Flow-tile mirror probe found no deterministic tile.");
    }

    public static void PerturbEditorTestOnlyPendingFlowTileIndex()
    {
        if (FlowTileBuildQueue.First?.Value == null)
            throw new InvalidOperationException("Pending flow-tile index probe requires a queued job.");
        FlowTileCacheKey key = FlowTileBuildQueue.First.Value.BuildKey.CacheKey;
        if (!PendingFlowTileBuildJobs.Remove(key))
            throw new InvalidOperationException("Pending flow-tile index probe could not remove the queued key.");
    }

    public static void PerturbEditorTestOnlyPendingSharedGoalIndex()
    {
        if (SharedGoalFieldBuildQueue.First?.Value == null)
            throw new InvalidOperationException("Pending shared-goal index probe requires a queued job.");
        SharedGoalFieldKey key = SharedGoalFieldBuildQueue.First.Value.Key;
        if (!PendingSharedGoalFieldBuildJobs.Remove(key))
            throw new InvalidOperationException("Pending shared-goal index probe could not remove the queued key.");
    }

    public static void PerturbEditorTestOnlyOrderedAgentIndex()
    {
        if (OrderedAgentIds.Count < 2)
            throw new InvalidOperationException("Ordered-agent index probe requires at least two agents.");

        int first = OrderedAgentIds[0];
        OrderedAgentIds[0] = OrderedAgentIds[1];
        OrderedAgentIds[1] = first;
    }

    private static WorldBuildJob GetRequiredSingleEditorTestWorldBuildJob()
    {
        WorldBuildJob result = null;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state?.BuildJob == null)
                continue;
            if (result != null)
                throw new InvalidOperationException("Editor test requires exactly one pending world-build job.");
            result = state.BuildJob;
        }

        return result
               ?? throw new InvalidOperationException("Editor test requires a pending world-build job.");
    }

    private static RuntimeDirtyRebuildJob GetRequiredSingleEditorTestRuntimeDirtyJob()
    {
        RuntimeDirtyRebuildJob result = null;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state?.RuntimeDirtyJob == null)
                continue;
            if (result != null)
                throw new InvalidOperationException("Editor test requires exactly one pending runtime-dirty job.");
            result = state.RuntimeDirtyJob;
        }

        return result
               ?? throw new InvalidOperationException("Editor test requires a pending runtime-dirty job.");
    }

    public static string GetEditorRuntimeDirtyJobDiagnostics()
    {
        return BuildRuntimeDirtyJobDiagnostics();
    }

    private static PortalData CreateEditorTestPortalProgressProbe()
    {
        return new PortalData
        {
            PortalId = 601,
            SectorAId = 602,
            SectorBId = 603,
            CellsA = new[] { new Vector2Int(1, 2) },
            CellsB = new[] { new Vector2Int(2, 2) },
            WidthCells = 1,
            IsNarrow = true,
            IsVerticalBoundary = false,
        };
    }

    public static void SetEditorTestClock(int frameCount, float time)
    {
        if (_hasTestTimeOverride && frameCount > _testFrameCount)
            _testDeltaTime = Mathf.Max(0f, (time - _testTime) / (frameCount - _testFrameCount));
        else if (frameCount > 0 && time > 0f)
            _testDeltaTime = time / frameCount;

        _hasTestTimeOverride = true;
        _testFrameCount = frameCount;
        _testTime = time;
    }

    public static void ClearEditorTestClock()
    {
        _hasTestTimeOverride = false;
        _testFrameCount = 0;
        _testTime = 0f;
        _testDeltaTime = 0.1f;
    }
#endif

}
