using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public static partial class FlowFieldCrowdMovementSystem
{
    private static void ProcessWorldBuildObstacles(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("ProcessWorldBuildObstacles failed: working world is null.");

        if (job.ApplyingCircleObstacles)
        {
            while (job.ObstacleCursor < job.CircleObstacles.Count)
            {
                CircleObstacle circle = job.CircleObstacles[job.ObstacleCursor++];
                BlockCellsByCircleFixed(
                    world,
                    circle.PositionFixed,
                    circle.RadiusFixed);
                if (!forceComplete && IsBudgetExpired(deadlineTicks, job.ObstacleCursor))
                    return;
            }

            job.ApplyingCircleObstacles = false;
            job.ObstacleCursor = 0;
        }

        while (job.ObstacleCursor < job.BoxObstacles.Count)
        {
            BoxObstacle box = job.BoxObstacles[job.ObstacleCursor++];
            BlockCellsByBoxFixed(
                world,
                box.CenterFixed,
                ResolveBoxObstacleNavigationHalfExtentsFixed(world, box));
            if (!forceComplete && IsBudgetExpired(deadlineTicks, job.ObstacleCursor))
                return;
        }

        job.ObstacleCursor = 0;
        job.Stage = WorldBuildStage.InitializeSectors;
    }

    private static void InitializeWorldBuildSectors(WorldBuildJob job)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("InitializeWorldBuildSectors failed: working world is null.");

        world.SectorCountX = Mathf.CeilToInt((float)world.Width / world.SectorSizeInCells);
        world.SectorCountY = Mathf.CeilToInt((float)world.Height / world.SectorSizeInCells);
        world.Sectors = new SectorData[world.SectorCountX * world.SectorCountY];

        for (int sectorY = 0; sectorY < world.SectorCountY; sectorY++)
        {
            for (int sectorX = 0; sectorX < world.SectorCountX; sectorX++)
            {
                int sectorId = sectorY * world.SectorCountX + sectorX;
                int startX = sectorX * world.SectorSizeInCells;
                int startY = sectorY * world.SectorSizeInCells;
                int sectorWidth = Mathf.Min(world.SectorSizeInCells, world.Width - startX);
                int sectorHeight = Mathf.Min(world.SectorSizeInCells, world.Height - startY);

                world.Sectors[sectorId] = new SectorData
                {
                    SectorId = sectorId,
                    StartX = startX,
                    StartY = startY,
                    Width = sectorWidth,
                    Height = sectorHeight,
                    DirtyVersion = 1,
                    LocalComponentIds = new int[sectorWidth * sectorHeight],
                    Center = world.Origin + new Vector3(
                        (startX + sectorWidth * 0.5f) * world.CellSize,
                        0f,
                        (startY + sectorHeight * 0.5f) * world.CellSize)
                };
            }
        }

        job.CellCursor = 0;
        job.Stage = WorldBuildStage.BuildCellNavAnchors;
    }

    private static int ResolveRuntimeSectorSizeInCells(long cellSizeGridRaw)
    {
#if UNITY_EDITOR
        if (Config.EditorTestSectorSizeInCells > 0)
            return Mathf.Max(4, Config.EditorTestSectorSizeInCells);
#endif
        if (cellSizeGridRaw <= 0)
            throw new InvalidOperationException($"Cannot derive sector cell count from invalid cellSizeGridRaw={cellSizeGridRaw}.");
        if (Config.SectorWorldSizeMillimeters <= 0)
        {
            throw new InvalidOperationException(
                $"Cannot derive sector cell count from invalid sectorWorldSizeMillimeters={Config.SectorWorldSizeMillimeters}.");
        }

        const long gridOne = 1L << 32;
        long sectorWorldSizeGridRaw = checked(
            (checked((long)Config.SectorWorldSizeMillimeters * gridOne) + 500L) / 1000L);
        long cellCount = checked((sectorWorldSizeGridRaw + cellSizeGridRaw / 2L) / cellSizeGridRaw);
        if (cellCount > int.MaxValue)
            throw new InvalidOperationException($"Derived sector cell count exceeds Int32: {cellCount}.");
        return Mathf.Max(4, (int)cellCount);
    }

    private static int ResolvePortalNarrowWidthCells(NavigationWorld world)
    {
        return ResolveScaledCellCount(world, Config.PortalNarrowWidthCells);
    }

    private static int ResolveScaledCellCount(NavigationWorld world, int referenceCellCount)
    {
        if (world == null)
            throw new InvalidOperationException("ResolveScaledCellCount failed: world is null.");

        return ResolveScaledCellCount(world.CellSize, referenceCellCount);
    }

    private static int ResolveScaledCellCount(float cellSize, int referenceCellCount)
    {
        if (referenceCellCount <= 0)
            throw new InvalidOperationException($"ResolveScaledCellCount failed: invalid referenceCellCount={referenceCellCount}.");

        return referenceCellCount;
    }

    private static void ProcessWorldBuildCellNavAnchors(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("ProcessWorldBuildCellNavAnchors failed: working world is null.");

        int cellCount = world.Width * world.Height;
        if (job.HasProvidedCellNavAnchors)
        {
            job.CellCursor = 0;
            job.Stage = WorldBuildStage.NeighborMask;
            return;
        }

        while (job.CellCursor < cellCount)
        {
            int index = job.CellCursor++;
            if (!world.BaseWalkableMask[index])
                continue;

            int x = index % world.Width;
            int y = index / world.Width;
            world.CellNavAnchors[index] = world.GridToWorldCenter(x, y);
            world.CellNavAnchorsFixedXZ[index] = world.GridToWorldCenterFixed(x, y);
            if (!forceComplete && IsBudgetExpired(deadlineTicks, job.CellCursor))
                return;
        }

        job.CellCursor = 0;
        job.Stage = WorldBuildStage.NeighborMask;
    }

    private static void ProcessWorldBuildNeighborMask(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("ProcessWorldBuildNeighborMask failed: working world is null.");

        int cellCount = world.Width * world.Height;
        while (job.CellCursor < cellCount)
        {
            int index = job.CellCursor++;
            RebuildNeighborTraversalMaskCell(world, index % world.Width, index / world.Width);
            if (!forceComplete && IsBudgetExpired(deadlineTicks, job.CellCursor))
                return;
        }

        job.CellCursor = 0;
        job.Stage = WorldBuildStage.SymmetrizeNeighborMask;
    }

    private static void ProcessWorldBuildSymmetrizeNeighborMask(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("ProcessWorldBuildSymmetrizeNeighborMask failed: working world is null.");
        if (world.NeighborTraversalMask == null || world.NeighborTraversalMask.Length != world.Width * world.Height)
            throw new InvalidOperationException("ProcessWorldBuildSymmetrizeNeighborMask failed: neighbor traversal mask is invalid.");

        int cellCount = world.Width * world.Height;
        while (job.CellCursor < cellCount)
        {
            int index = job.CellCursor++;
            int x = index % world.Width;
            int y = index / world.Width;
            SymmetrizeNeighborTraversalMaskCell(world, x, y, "ProcessWorldBuildSymmetrizeNeighborMask");
            if (!forceComplete && IsBudgetExpired(deadlineTicks, job.CellCursor))
                return;
        }

        PruneIsolatedWalkableCells(world, "world-build");
        if (!job.IncludeRuntimeObstacles)
            FreezeCanonicalStaticNavigationBase(world);
        job.SectorCursor = 0;
        job.CellCursor = 0;
        job.Stage = WorldBuildStage.CostField;
    }

    private static void FreezeCanonicalStaticNavigationBase(NavigationWorld world)
    {
        if (world == null)
            throw new InvalidOperationException("FreezeCanonicalStaticNavigationBase failed: world is null.");
        int cellCount = checked(world.Width * world.Height);
        if (world.WalkableMask == null || world.WalkableMask.Length != cellCount)
            throw new InvalidOperationException("FreezeCanonicalStaticNavigationBase failed: walkable mask is invalid.");
        if (world.NeighborTraversalMask == null || world.NeighborTraversalMask.Length != cellCount)
            throw new InvalidOperationException("FreezeCanonicalStaticNavigationBase failed: neighbor mask is invalid.");

        world.BaseWalkableMask = (bool[])world.WalkableMask.Clone();
        world.BaseNeighborTraversalMask = (byte[])world.NeighborTraversalMask.Clone();
    }

    private static void ProcessWorldBuildCostField(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("ProcessWorldBuildCostField failed: working world is null.");

        while (job.SectorCursor < world.Sectors.Length)
        {
            if (world.SourceCostField != null && world.SourceCostField.Length == world.Width * world.Height)
                InitializeAuthoredCostFieldForSector(world, world.Sectors[job.SectorCursor], job.CostStamps);
            else
                RebuildCostFieldForSector(world, job.SectorCursor, job.CostStamps);

            job.SectorCursor++;
            if (!forceComplete && IsBudgetExpired(deadlineTicks, job.SectorCursor))
                return;
        }

        job.SectorCursor = 0;
        job.Stage = WorldBuildStage.IslandField;
    }

    private static void ProcessWorldBuildIslandField(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        ProcessWorldBuildIslandFieldInternal(job, deadlineTicks, forceComplete);
    }

    private static void ProcessWorldBuildIslandFieldInternal(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("ProcessWorldBuildIslandField failed: working world is null.");

        if (!job.IslandInitialized)
        {
            Array.Clear(world.IslandIds, 0, world.IslandIds.Length);
            world.IslandCount = 0;
            world.MainIslandId = 0;
            world.MainIslandSize = 0;
            job.IslandOpenQueue = new Queue<int>(256);
            job.IslandScanIndex = 0;
            job.IslandCurrentId = 0;
            job.IslandCurrentSize = 0;
            job.IslandMainId = 0;
            job.IslandMainSize = 0;
            job.IslandBfsActive = false;
            job.IslandInitialized = true;
        }

        AdvanceIslandFieldBuild(
            world,
            job.IslandOpenQueue,
            ref job.IslandScanIndex,
            ref job.IslandCurrentId,
            ref job.IslandCurrentSize,
            ref job.IslandMainId,
            ref job.IslandMainSize,
            ref job.IslandBfsActive,
            deadlineTicks,
            forceComplete,
            out bool complete);
        if (!complete)
            return;

        RebuildAllSectorLocalComponents(world);
        LogIslandFieldDiagnostics(world, "world-build-pending");
        job.Stage = WorldBuildStage.GoalProjectionIndex;
    }

    private static void ProcessWorldBuildGoalProjectionIndex(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        if (job == null || job.WorkingWorld == null)
            throw new InvalidOperationException("ProcessWorldBuildGoalProjectionIndex failed: working world is null.");

        if (job.WorkingWorld.GoalProjectionSpatialIndex != null)
        {
            if (!job.UsesPrebakedHierarchy)
                throw new InvalidOperationException("ProcessWorldBuildGoalProjectionIndex found a stale non-prebaked index.");
            job.Stage = WorldBuildStage.Commit;
            return;
        }

        if (!ProcessGoalProjectionSpatialIndexBuild(
                job.WorkingWorld,
                ref job.GoalProjectionIndexBuildJob,
                deadlineTicks,
                forceComplete))
        {
            return;
        }

        job.Stage = job.UsesPrebakedHierarchy
            ? WorldBuildStage.Commit
            : WorldBuildStage.PortalGraph;
    }

    private static void ProcessWorldBuildPortalGraph(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("ProcessWorldBuildPortalGraph failed: working world is null.");

        if (!job.PortalInitialized)
        {
            job.PendingPortalAccessEntries ??= new List<PendingSectorPortalAccess>();
            job.PendingPortalAccessEntries.Clear();
            world.UsedPortalIds.Clear();
            for (int i = 0; i < world.Sectors.Length; i++)
            {
                world.Sectors[i].PortalIds.Clear();
                world.Sectors[i].PortalTransitions.Clear();
            }

            world.PortalsById = new Dictionary<int, PortalData>();
            job.PortalRebuiltPortals = new List<PortalData>();
            job.PortalStage = RuntimeDirtyPortalStage.RebuildBoundaries;
            job.PortalAddCursor = 0;
            job.PortalTransitionCursor = 0;
            job.PortalTransitionFromCursor = 0;
            job.PortalInitialized = true;
        }

        while (job.PortalStage != RuntimeDirtyPortalStage.Complete)
        {
            switch (job.PortalStage)
            {
                case RuntimeDirtyPortalStage.RebuildBoundaries:
                    long boundaryStartTicks = Stopwatch.GetTimestamp();
                    BuildVerticalPortals(world, job.PortalRebuiltPortals);
                    BuildHorizontalPortals(world, job.PortalRebuiltPortals);
                    job.PortalGraphBoundaryTicks += Stopwatch.GetTimestamp() - boundaryStartTicks;
                    job.PortalStage = RuntimeDirtyPortalStage.AddRebuiltPortals;
                    break;
                case RuntimeDirtyPortalStage.AddRebuiltPortals:
                    while (job.PortalAddCursor < job.PortalRebuiltPortals.Count)
                    {
                        long addStartTicks = Stopwatch.GetTimestamp();
                        PortalData portal = job.PortalRebuiltPortals[job.PortalAddCursor++];
                        if (world.PortalsById.ContainsKey(portal.PortalId))
                            throw new InvalidOperationException($"ProcessWorldBuildPortalGraph failed: duplicate portal id {portal.PortalId}.");

                        world.PortalsById.Add(portal.PortalId, portal);
                        world.Sectors[portal.SectorAId].PortalIds.Add(portal.PortalId);
                        world.Sectors[portal.SectorBId].PortalIds.Add(portal.PortalId);
                        job.PortalGraphAddTicks += Stopwatch.GetTimestamp() - addStartTicks;
                        if (!forceComplete && IsBudgetExpired(deadlineTicks, job.PortalAddCursor))
                            return;
                    }

                    world.Portals = job.PortalRebuiltPortals.ToArray();
                    job.PortalStage = RuntimeDirtyPortalStage.RebuildTransitions;
                    break;
                case RuntimeDirtyPortalStage.RebuildTransitions:
                    ProcessWorldBuildPortalTransitions(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyPortalStage.RemoveOldPortals:
                    job.PortalStage = RuntimeDirtyPortalStage.RebuildBoundaries;
                    break;
                default:
                    throw new InvalidOperationException($"ProcessWorldBuildPortalGraph failed: unknown portal stage {job.PortalStage}.");
            }

            if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                return;
        }

        LogWorldBuildPortalGraphTiming(job);
        RemoveStalePortalSignatures(world);
        job.Stage = WorldBuildStage.Hierarchy;
    }

    private static void ProcessWorldBuildHierarchy(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        if (job.WorkingWorld == null)
            throw new InvalidOperationException("ProcessWorldBuildHierarchy failed: working world is null.");
        if (!job.HierarchyTransitionCostsPrepared)
        {
            if (job.PendingPortalAccessEntries == null)
                throw new InvalidOperationException("ProcessWorldBuildHierarchy failed: pending portal access entries are missing.");
            for (int i = 0; i < job.PendingPortalAccessEntries.Count; i++)
            {
                PendingSectorPortalAccess pending = job.PendingPortalAccessEntries[i];
                SectorData sector = job.WorkingWorld.Sectors[pending.SectorId];
                ApplyPendingPortalTransitionCosts(job.WorkingWorld, sector, pending);
            }
            job.HierarchyTransitionCostsPrepared = true;
            job.HierarchyBuildJob = CreatePortalHierarchyBuildJob(job.WorkingWorld, DefaultHierarchyFanout);
        }

        ProcessPortalHierarchyBuildJob(job.HierarchyBuildJob, deadlineTicks, forceComplete);
        if (!job.HierarchyBuildJob.Complete)
            return;
        job.WorkingWorld.Hierarchy = job.HierarchyBuildJob.Result;
        job.Stage = WorldBuildStage.Commit;
    }

    private static void ProcessWorldBuildPortalTransitions(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        while (job.PortalTransitionCursor < world.Sectors.Length)
        {
            SectorData sector = world.Sectors[job.PortalTransitionCursor];
            if (job.PortalTransitionFromCursor == 0)
                sector.PortalTransitions.Clear();

            if (sector.PortalIds.Count == 0)
            {
                RebuildIncomingPortalTransitionIndex(sector);
                job.PortalTransitionCursor++;
                job.PortalTransitionFromCursor = 0;
                continue;
            }

            while (job.PortalTransitionFromCursor < sector.PortalIds.Count)
            {
                int fromPortalId = sector.PortalIds[job.PortalTransitionFromCursor];
                bool analytic = CanUseAnalyticPortalAccess(
                    world,
                    sector,
                    fromPortalId,
                    out AnalyticPortalAccessDiagnostics analyticDiagnostics);
                job.PortalGraphAnalyticCheckCount++;
                if (analyticDiagnostics.IsClearSector)
                    job.PortalGraphAnalyticClearSectorCount++;
                if (analyticDiagnostics.RejectReason == AnalyticPortalAccessRejectReason.MultipleLocalComponents)
                    job.PortalGraphAnalyticMultiComponentRejectCount++;
                else if (analyticDiagnostics.RejectReason == AnalyticPortalAccessRejectReason.NonClearSector)
                    job.PortalGraphAnalyticNonClearRejectCount++;

                if (analytic)
                    AddPendingAnalyticSectorPortalAccess(job.PendingPortalAccessEntries, sector, fromPortalId);
                else
                    AddPendingPrebuiltDeterministicSectorPortalAccess(
                        world,
                        job.PendingPortalAccessEntries,
                        sector,
                        fromPortalId);

                AddDeterministicPortalTransitionTopology(world, sector, fromPortalId);
                job.PortalGraphAccessCount++;
                job.PortalGraphTransitionCostChecks++;
                if (analytic)
                    job.PortalGraphAnalyticAccessCount++;

                job.PortalTransitionFromCursor++;
                if (!forceComplete && IsBudgetExpired(deadlineTicks, job.PortalTransitionFromCursor))
                    return;
            }

            RebuildIncomingPortalTransitionIndex(sector);
            job.PortalTransitionCursor++;
            job.PortalTransitionFromCursor = 0;
        }

        job.PortalStage = RuntimeDirtyPortalStage.Complete;
    }

    private static void AddDeterministicPortalTransitionTopology(
        NavigationWorld world,
        SectorData sector,
        int fromPortalId)
    {
        if (world == null)
            throw new InvalidOperationException("AddDeterministicPortalTransitionTopology failed: world is null.");
        if (sector == null)
            throw new InvalidOperationException("AddDeterministicPortalTransitionTopology failed: sector is null.");

        for (int i = 0; i < sector.PortalIds.Count; i++)
        {
            int toPortalId = sector.PortalIds[i];
            if (toPortalId == fromPortalId
                || !PortalsShareSectorLocalComponent(world, sector, fromPortalId, toPortalId))
            {
                continue;
            }

            AddPortalTransitionIfMissing(sector, fromPortalId, toPortalId);
        }
    }

}
