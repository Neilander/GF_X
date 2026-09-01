using System;
using System.Collections.Generic;
using System.Diagnostics;
using AAAGame.FlowPath;
using UnityEngine;

public static partial class FlowFieldCrowdMovementSystem
{
    private static void InvalidateRuntimeDirtyJob(WorldRuntimeState state)
    {
        if (state == null)
            throw new InvalidOperationException("InvalidateRuntimeDirtyJob failed: state is null.");

        if (state.RuntimeDirtyJob != null)
        {
            foreach (int sectorId in state.RuntimeDirtyJob.DirtySectors)
                state.DirtyRuntimeObstacleSectors.Add(sectorId);
            foreach (int sectorId in state.RuntimeDirtyJob.CostDirtySectors)
                state.DirtyRuntimeObstacleSectors.Add(sectorId);
            ReturnRuntimeDirtyWorkingWorld(state.RuntimeDirtyJob);
        }

        state.RuntimeDirtyJob = null;
    }

    private static bool EnsureRuntimeDirtyJob(WorldRuntimeState state)
    {
        if (state == null || state.World == null)
            return false;
        if (state.RuntimeDirtyJob != null)
            return true;
        if (state.DirtyRuntimeObstacleSectors.Count == 0)
            return false;

        NavigationWorld world = state.World;
        HashSet<int> dirtySectors = new HashSet<int>(state.DirtyRuntimeObstacleSectors);
        HashSet<int> costDirtySectors = ExpandDirtySectorsByCellRadius(world, dirtySectors, ResolveWallCostPaddingCells(world, 1));
        RuntimeDirtyRebuildJob job = new RuntimeDirtyRebuildJob
        {
            TargetWorld = world,
            DirtySectors = dirtySectors,
            CostDirtySectors = costDirtySectors,
            DirtySectorIds = CreateSortedSectorIdSnapshot(dirtySectors, "dirty sectors"),
            CostDirtySectorIds = CreateSortedSectorIdSnapshot(costDirtySectors, "cost dirty sectors"),
            CircleObstacles = CreateSortedCircleObstacleSnapshot(CircleObstacles.Values),
            BoxObstacles = CreateSortedBoxObstacleSnapshot(BoxObstacles.Values),
            CostStamps = CreateSortedCostStampSnapshot(CostStamps.Values),
            Stage = RuntimeDirtyRebuildStage.InitializeClone,
            Reason = _lastRuntimeObstacleDirtyReason
        };
        RefreshRuntimeDirtyAuthorityInputHash(job);

        state.RuntimeDirtyJob = job;
        state.DirtyRuntimeObstacleSectors.Clear();
        _perf.RuntimeDirtyApplications++;
        _perf.RuntimeDirtySectorCount += dirtySectors.Count;
        LogNoStacktrace(
            $"[FlowRuntimeDirtyQueued] worldVersion={world.Version} dirtySectors={dirtySectors.Count} costSectors={costDirtySectors.Count} " +
            $"reason={job.Reason} circleCount={job.CircleObstacles.Count} boxCount={job.BoxObstacles.Count} costStampCount={CostStamps.Count}");
        return true;
    }

    private static void ProcessRuntimeDirtyJob(WorldRuntimeState state, long deadlineTicks, bool forceComplete)
    {
        RuntimeDirtyRebuildJob job = state.RuntimeDirtyJob;
        if (job == null)
            return;

        EnsureRuntimeDirtyTimingStarted(job);
        while (job.Stage != RuntimeDirtyRebuildStage.Complete)
        {
            RuntimeDirtyRebuildStage stageBefore = job.Stage;
            long stageStartTimestamp = Stopwatch.GetTimestamp();
            switch (job.Stage)
            {
                case RuntimeDirtyRebuildStage.InitializeClone:
                    ProcessRuntimeDirtyClone(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyRebuildStage.ResetWalkable:
                    ProcessRuntimeDirtyResetWalkable(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyRebuildStage.ApplyObstacles:
                    ProcessRuntimeDirtyObstacles(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyRebuildStage.NeighborMask:
                    ProcessRuntimeDirtyNeighborMask(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyRebuildStage.SymmetrizeNeighborMask:
                    SymmetrizeNeighborTraversalMaskForSectors(job.WorkingWorld, job.DirtySectors);
                    PruneIsolatedWalkableCellsForSectors(job.WorkingWorld, job.DirtySectors, "runtime-dirty");
                    job.SectorCursor = 0;
                    job.Stage = RuntimeDirtyRebuildStage.CostField;
                    break;
                case RuntimeDirtyRebuildStage.CostField:
                    ProcessRuntimeDirtyCostField(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyRebuildStage.IslandField:
                    ProcessRuntimeDirtyIslandField(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyRebuildStage.SectorComponents:
                    ProcessRuntimeDirtySectorComponents(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyRebuildStage.GoalProjectionIndex:
                    ProcessRuntimeDirtyGoalProjectionIndex(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyRebuildStage.PortalGraph:
                    if (!ProcessRuntimeDirtyPortalGraph(job, deadlineTicks, forceComplete))
                        return;
                    break;
                case RuntimeDirtyRebuildStage.Hierarchy:
                    ProcessRuntimeDirtyHierarchy(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyRebuildStage.PrepareCommit:
                    PrepareRuntimeDirtyCommit(job);
                    job.Stage = RuntimeDirtyRebuildStage.WorldHash;
                    break;
                case RuntimeDirtyRebuildStage.WorldHash:
                    ProcessNavigationWorldHashBuildJob(job.WorldHashBuildJob, deadlineTicks, forceComplete);
                    if (job.WorldHashBuildJob.Complete)
                    {
                        job.WorkingWorld.DeterministicContentHash = job.WorldHashBuildJob.Hash;
                        job.WorkingWorld.HasDeterministicContentHash = true;
                        job.Stage = RuntimeDirtyRebuildStage.Commit;
                    }
                    break;
                case RuntimeDirtyRebuildStage.Commit:
                    CommitRuntimeDirtyJob(state, job);
                    job.Stage = RuntimeDirtyRebuildStage.Complete;
                    break;
                default:
                    throw new InvalidOperationException($"ProcessRuntimeDirtyJob failed: unknown stage {job.Stage}.");
            }

            long stageElapsedTicks = Stopwatch.GetTimestamp() - stageStartTimestamp;
            if (stageBefore >= 0 && stageBefore < RuntimeDirtyRebuildStage.Complete)
                job.StageAccumulatedTicks[(int)stageBefore] += stageElapsedTicks;

            LogRuntimeDirtyStageTimingIfNeeded(job, stageBefore, stageStartTimestamp, forceComplete);
            if (job.Stage == RuntimeDirtyRebuildStage.Complete)
                break;

            if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                return;
        }

        state.RuntimeDirtyJob = null;
    }

    private static void EnsureRuntimeDirtyTimingStarted(RuntimeDirtyRebuildJob job)
    {
        if (job == null)
            throw new InvalidOperationException("EnsureRuntimeDirtyTimingStarted failed: job is null.");
        if (job.TimingInitialized)
            return;

        long now = Stopwatch.GetTimestamp();
        job.BuildStartedTimestamp = now;
        job.StageStartedTimestamp = now;
        job.TimedStage = job.Stage;
        job.TimingInitialized = true;
        LogNoStacktrace($"[FlowRuntimeDirtyStage] begin worldVersion={job.TargetWorld?.Version ?? 0} reason={job.Reason} {BuildRuntimeDirtyTimingSummary(job)}");
    }

    private static void LogRuntimeDirtyStageTimingIfNeeded(RuntimeDirtyRebuildJob job, RuntimeDirtyRebuildStage stageBefore, long stageStartTimestamp, bool forceComplete)
    {
        if (job == null)
            throw new InvalidOperationException("LogRuntimeDirtyStageTimingIfNeeded failed: job is null.");

        bool stageChanged = job.Stage != stageBefore;
        long now = Stopwatch.GetTimestamp();
        double elapsedMs = TicksToMilliseconds(now - stageStartTimestamp);
        if (!stageChanged && (!forceComplete || elapsedMs < 5.0))
            return;

        double totalMs = TicksToMilliseconds(now - job.BuildStartedTimestamp);
        LogNoStacktrace(
            $"[FlowRuntimeDirtyStage] worldVersion={job.TargetWorld?.Version ?? 0} stage={stageBefore} next={job.Stage} " +
            $"elapsedMs={elapsedMs:F3} totalMs={totalMs:F3} forceComplete={forceComplete} {BuildRuntimeDirtyTimingSummary(job)}");
    }

    private static string BuildRuntimeDirtyTimingSummary(RuntimeDirtyRebuildJob job)
    {
        NavigationWorld world = job.WorkingWorld ?? job.TargetWorld;
        int sectorCount = world?.Sectors != null ? world.Sectors.Length : 0;
        int portalCount = world?.PortalsById != null ? world.PortalsById.Count : 0;
        int transitionCount = CountPortalTransitions(world);
        int dirtyCount = job.DirtySectorIds != null ? job.DirtySectorIds.Count : 0;
        int costDirtyCount = job.CostDirtySectorIds != null ? job.CostDirtySectorIds.Count : 0;
        int circleCount = job.CircleObstacles != null ? job.CircleObstacles.Count : 0;
        int boxCount = job.BoxObstacles != null ? job.BoxObstacles.Count : 0;
        int costStampCount = job.CostStamps != null ? job.CostStamps.Count : 0;
        int pendingPortalAccessCount = job.PendingPortalAccessEntries != null ? job.PendingPortalAccessEntries.Count : 0;
        int sectorSize = world != null ? world.SectorSizeInCells : 0;
        int sectorCountX = world != null ? world.SectorCountX : 0;
        int sectorCountY = world != null ? world.SectorCountY : 0;
        int islandCount = world != null ? world.IslandCount : 0;
        int mainIslandSize = world != null ? world.MainIslandSize : 0;

        return
            $"size={(world != null ? world.Width : 0)}x{(world != null ? world.Height : 0)} cellSize={(world != null ? world.CellSize : 0f):F3} " +
            $"sectorSize={sectorSize} sectors={sectorCount}({sectorCountX}x{sectorCountY}) dirtySectors={dirtyCount} costSectors={costDirtyCount} " +
            $"portals={portalCount} transitions={transitionCount} pendingPortalAccess={pendingPortalAccessCount} " +
            $"islands={islandCount} mainIslandSize={mainIslandSize} obstacles(circle={circleCount},box={boxCount},cost={costStampCount}) " +
            $"cursors(sector={job.SectorCursor},obstacle={job.ObstacleCursor},portalSector={job.PortalTransitionCursor},portalFrom={job.PortalTransitionFromCursor}) " +
            BuildRuntimeDirtyStageAccumulatedTiming(job);
    }

    private static string BuildRuntimeDirtyStageAccumulatedTiming(RuntimeDirtyRebuildJob job)
    {
        if (job?.StageAccumulatedTicks == null || job.StageAccumulatedTicks.Length < (int)RuntimeDirtyRebuildStage.Complete)
            return "stageAccum=invalid";

        return
            $"stageAccum(reset={TicksToMilliseconds(job.StageAccumulatedTicks[(int)RuntimeDirtyRebuildStage.ResetWalkable]):F3}ms," +
            $"obstacles={TicksToMilliseconds(job.StageAccumulatedTicks[(int)RuntimeDirtyRebuildStage.ApplyObstacles]):F3}ms," +
            $"neighbor={TicksToMilliseconds(job.StageAccumulatedTicks[(int)RuntimeDirtyRebuildStage.NeighborMask]):F3}ms," +
            $"sym={TicksToMilliseconds(job.StageAccumulatedTicks[(int)RuntimeDirtyRebuildStage.SymmetrizeNeighborMask]):F3}ms," +
            $"cost={TicksToMilliseconds(job.StageAccumulatedTicks[(int)RuntimeDirtyRebuildStage.CostField]):F3}ms," +
            $"island={TicksToMilliseconds(job.StageAccumulatedTicks[(int)RuntimeDirtyRebuildStage.IslandField]):F3}ms," +
            $"portal={TicksToMilliseconds(job.StageAccumulatedTicks[(int)RuntimeDirtyRebuildStage.PortalGraph]):F3}ms," +
            $"prepare={TicksToMilliseconds(job.StageAccumulatedTicks[(int)RuntimeDirtyRebuildStage.PrepareCommit]):F3}ms," +
            $"hash={TicksToMilliseconds(job.StageAccumulatedTicks[(int)RuntimeDirtyRebuildStage.WorldHash]):F3}ms," +
            $"commit={TicksToMilliseconds(job.StageAccumulatedTicks[(int)RuntimeDirtyRebuildStage.Commit]):F3}ms)";
    }

    private static string BuildRuntimeDirtyCommitTiming(RuntimeDirtyRebuildJob job)
    {
        return
            $"commitDetail(swap={TicksToMilliseconds(job.CommitSwapTicks):F3}ms," +
            $"cache={TicksToMilliseconds(job.CommitCacheInvalidationTicks):F3}ms," +
            $"portalAccess={TicksToMilliseconds(job.CommitPortalAccessTicks):F3}ms," +
            $"worldHash={TicksToMilliseconds(job.CommitWorldHashTicks):F3}ms," +
            $"agents={TicksToMilliseconds(job.CommitAgentInvalidationTicks):F3}ms)";
    }

    private static void ProcessRuntimeDirtyClone(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld source = job.TargetWorld;
        if (source == null)
            throw new InvalidOperationException("ProcessRuntimeDirtyClone failed: target world is null.");

        while (!job.CloneShellInitialized)
        {
            switch (job.CloneShellStage)
            {
                case RuntimeDirtyCloneShellStage.CreateMetadata:
                    job.WorkingWorld = new NavigationWorld
                    {
                        Version = source.Version,
                        AgentTypeId = source.AgentTypeId,
                        Width = source.Width,
                        Height = source.Height,
                        CellSize = source.CellSize,
                        EncodedCenterClearance = source.EncodedCenterClearance,
                        Origin = source.Origin,
                        HasImmutableContentHash = source.HasImmutableContentHash,
                        ImmutableContentHash = source.ImmutableContentHash,
                        BaseWalkableMask = source.BaseWalkableMask,
                        BaseNeighborTraversalMask = source.BaseNeighborTraversalMask,
                        StaticCollisionVertices = source.StaticCollisionVertices,
                        StaticCollisionPathStarts = source.StaticCollisionPathStarts,
                        SourceCostField = source.SourceCostField,
                        SectorCostFields = null,
                        CellNavAnchors = source.CellNavAnchors,
                        CellNavAnchorsFixedXZ = source.CellNavAnchorsFixedXZ,
                        IslandCount = source.IslandCount,
                        MainIslandId = source.MainIslandId,
                        MainIslandSize = source.MainIslandSize,
                        SectorSizeInCells = source.SectorSizeInCells,
                        SectorCountX = source.SectorCountX,
                        SectorCountY = source.SectorCountY,
                        NextPortalId = source.NextPortalId
                    };
                    job.WorkingWorld.CopyAuthorityGridMetadataFrom(source);
                    job.CloneShellStage = RuntimeDirtyCloneShellStage.RentWalkableMask;
                    break;
                case RuntimeDirtyCloneShellStage.RentWalkableMask:
                    job.WorkingWorld.WalkableMask = RentBoolArray(source.Width * source.Height, clear: false);
                    job.CloneShellStage = RuntimeDirtyCloneShellStage.RentCostField;
                    break;
                case RuntimeDirtyCloneShellStage.RentCostField:
                    job.WorkingWorld.CostField = RentByteArray(source.Width * source.Height, clear: false);
                    job.CloneShellStage = RuntimeDirtyCloneShellStage.RentNeighborTraversalMask;
                    break;
                case RuntimeDirtyCloneShellStage.RentNeighborTraversalMask:
                    job.WorkingWorld.NeighborTraversalMask = RentByteArray(source.Width * source.Height, clear: false);
                    job.CloneShellStage = RuntimeDirtyCloneShellStage.RentIslandIds;
                    break;
                case RuntimeDirtyCloneShellStage.RentIslandIds:
                    job.WorkingWorld.IslandIds = RentIntArray(source.Width * source.Height, clear: false);
                    job.CloneShellStage = RuntimeDirtyCloneShellStage.CreateSectorSlots;
                    break;
                case RuntimeDirtyCloneShellStage.CreateSectorSlots:
                    job.WorkingWorld.Sectors = new SectorData[source.Sectors.Length];
                    job.CloneShellStage = RuntimeDirtyCloneShellStage.ClonePortalArray;
                    break;
                case RuntimeDirtyCloneShellStage.ClonePortalArray:
                    job.WorkingWorld.Portals = source.Portals != null
                        ? (PortalData[])source.Portals.Clone()
                        : Array.Empty<PortalData>();
                    job.CloneShellStage = RuntimeDirtyCloneShellStage.ClonePortalLookup;
                    break;
                case RuntimeDirtyCloneShellStage.ClonePortalLookup:
                    job.WorkingWorld.PortalsById = new Dictionary<int, PortalData>(source.PortalsById);
                    job.CloneShellStage = RuntimeDirtyCloneShellStage.ClonePortalSignatures;
                    break;
                case RuntimeDirtyCloneShellStage.ClonePortalSignatures:
                    foreach (KeyValuePair<PortalSignature, int> pair in source.PortalIdsBySignature)
                        job.WorkingWorld.PortalIdsBySignature.Add(pair.Key, pair.Value);
                    job.CloneShellStage = RuntimeDirtyCloneShellStage.CloneUsedPortalIds;
                    break;
                case RuntimeDirtyCloneShellStage.CloneUsedPortalIds:
                    foreach (int portalId in source.UsedPortalIds)
                        job.WorkingWorld.UsedPortalIds.Add(portalId);
                    job.CloneShellStage = RuntimeDirtyCloneShellStage.Complete;
                    break;
                case RuntimeDirtyCloneShellStage.Complete:
                    job.CloneCellCursor = 0;
                    job.CloneSectorCursor = 0;
                    job.CloneShellInitialized = true;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"ProcessRuntimeDirtyClone failed: unknown clone shell stage {job.CloneShellStage}.");
            }

            // Every shell transition is one deterministic operation. Keep consuming
            // the current quota instead of terminating the whole job after the first
            // constant-time transition; otherwise a local dirty rebuild spends one
            // logic tick per shell field before any actual clone work can start.
            if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                return;
        }

        NavigationWorld working = job.WorkingWorld;
        int cellCount = source.Width * source.Height;
        if (job.CloneCellCursor < cellCount)
        {
            // The arrays form one immutable snapshot boundary. Copying them in tiny
            // 4096-cell slices made a local obstacle update spend ~90 logic Ticks only
            // entering the working world on Lv2. Keep the snapshot atomic and charge one
            // deterministic clone operation; no partially copied world is ever published.
            Array.Copy(source.WalkableMask, working.WalkableMask, cellCount);
            Array.Copy(source.NeighborTraversalMask, working.NeighborTraversalMask, cellCount);
            Array.Copy(source.IslandIds, working.IslandIds, cellCount);
            if (source.CostField != null)
            {
                Array.Copy(source.CostField, working.CostField, cellCount);
            }
            else
            {
                for (int index = 0; index < cellCount; index++)
                    working.CostField[index] = source.WalkableMask[index] ? (byte)1 : byte.MaxValue;
            }

            job.CloneCellCursor = cellCount;
            if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                return;
        }

        while (job.CloneSectorCursor < source.Sectors.Length)
        {
            int sectorIndex = job.CloneSectorCursor++;
            SectorData sourceSector = source.Sectors[sectorIndex];
            working.Sectors[sectorIndex] = CloneSectorData(sourceSector, job.DirtySectors, job.CostDirtySectors);
            if (source.CostField == null)
                CopyRuntimeDirtySectorCost(source, working, sourceSector);
            if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                return;
        }

        ValidateRuntimeDirtyPortalCloneOwnership(job);
        job.SectorCursor = 0;
        job.Stage = RuntimeDirtyRebuildStage.ResetWalkable;
    }

    private static void ValidateRuntimeDirtyPortalCloneOwnership(RuntimeDirtyRebuildJob job)
    {
        if (job == null || job.TargetWorld == null || job.WorkingWorld == null)
            throw new InvalidOperationException("ValidateRuntimeDirtyPortalCloneOwnership failed: runtime-dirty world is missing.");
        if (job.DirtySectors == null || job.CostDirtySectors == null)
            throw new InvalidOperationException("ValidateRuntimeDirtyPortalCloneOwnership failed: dirty sector sets are missing.");
        if (job.TargetWorld.Sectors.Length != job.WorkingWorld.Sectors.Length)
            throw new InvalidOperationException("ValidateRuntimeDirtyPortalCloneOwnership failed: sector count mismatch.");

        for (int i = 0; i < job.TargetWorld.Sectors.Length; i++)
        {
            SectorData source = job.TargetWorld.Sectors[i];
            SectorData working = job.WorkingWorld.Sectors[i];
            bool mutable = job.CostDirtySectors.Contains(i);
            bool sharesPortalCollections = ReferenceEquals(source.PortalIds, working.PortalIds)
                                           && ReferenceEquals(source.PortalTransitions, working.PortalTransitions)
                                           && ReferenceEquals(source.IncomingPortalTransitionsByToPortalId, working.IncomingPortalTransitionsByToPortalId)
                                           && ReferenceEquals(source.OutgoingPortalTransitionsByFromPortalId, working.OutgoingPortalTransitionsByFromPortalId);
            if (mutable == sharesPortalCollections)
            {
                throw new InvalidOperationException(
                    $"ValidateRuntimeDirtyPortalCloneOwnership failed: sector {i} mutable={mutable}, sharesPortalCollections={sharesPortalCollections}.");
            }
        }

        foreach (PortalData portal in job.TargetWorld.PortalsById.Values)
        {
            if (!job.DirtySectors.Contains(portal.SectorAId) && !job.DirtySectors.Contains(portal.SectorBId))
                continue;
            if (!job.CostDirtySectors.Contains(portal.SectorAId) || !job.CostDirtySectors.Contains(portal.SectorBId))
            {
                throw new InvalidOperationException(
                    $"ValidateRuntimeDirtyPortalCloneOwnership failed: portal {portal.PortalId} touches dirty sectors outside mutable coverage. " +
                    $"sectorA={portal.SectorAId}, sectorB={portal.SectorBId}.");
            }
        }
    }

    private static void CopyRuntimeDirtySectorCost(NavigationWorld source, NavigationWorld working, SectorData sector)
    {
        if (sector == null)
            throw new InvalidOperationException("CopyRuntimeDirtySectorCost failed: sector is null.");

        byte[] chunk = source.SectorCostFields != null ? source.SectorCostFields[sector.SectorId] : null;
        if (chunk == null)
        {
            if (!sector.IsClearCostField)
                throw new InvalidOperationException($"CopyRuntimeDirtySectorCost failed: missing non-clear cost chunk sector={sector.SectorId}.");
            return;
        }
        if (chunk.Length != sector.Width * sector.Height)
            throw new InvalidOperationException($"CopyRuntimeDirtySectorCost failed: invalid cost chunk sector={sector.SectorId} length={chunk.Length}.");

        for (int localY = 0; localY < sector.Height; localY++)
        {
            int sourceOffset = localY * sector.Width;
            int targetOffset = (sector.StartY + localY) * working.Width + sector.StartX;
            Array.Copy(chunk, sourceOffset, working.CostField, targetOffset, sector.Width);
        }
    }

    private static void ProcessRuntimeDirtyResetWalkable(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        while (job.SectorCursor < job.DirtySectorIds.Count)
        {
            int sectorId = job.DirtySectorIds[job.SectorCursor++];
            ResetSectorWalkableFromBase(job.WorkingWorld, job.WorkingWorld.Sectors[sectorId]);
            job.WorkingWorld.Sectors[sectorId].DirtyVersion++;
            if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                return;
        }

        job.SectorCursor = 0;
        job.Stage = RuntimeDirtyRebuildStage.ApplyObstacles;
    }

    private static void ProcessRuntimeDirtyObstacles(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        if (job.ApplyingCircleObstacles)
        {
            while (job.ObstacleCursor < job.CircleObstacles.Count)
            {
                CircleObstacle circle = job.CircleObstacles[job.ObstacleCursor++];
                BlockCellsByCircleInSectorsFixed(job.WorkingWorld, job.DirtySectors, circle.PositionFixed, circle.RadiusFixed);
                if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                    return;
            }

            job.ApplyingCircleObstacles = false;
            job.ObstacleCursor = 0;
        }

        while (job.ObstacleCursor < job.BoxObstacles.Count)
        {
            BoxObstacle box = job.BoxObstacles[job.ObstacleCursor++];
            BlockCellsByBoxInSectorsFixed(
                job.WorkingWorld,
                job.DirtySectors,
                box.CenterFixed,
                ResolveBoxObstacleNavigationHalfExtentsFixed(job.WorkingWorld, box));
            if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                return;
        }

        job.ObstacleCursor = 0;
        job.Stage = RuntimeDirtyRebuildStage.NeighborMask;
    }

    private static void ProcessRuntimeDirtyNeighborMask(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        while (job.SectorCursor < job.DirtySectorIds.Count)
        {
            SectorData sector = job.WorkingWorld.Sectors[job.DirtySectorIds[job.SectorCursor++]];
            for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
            {
                for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
                    RebuildNeighborTraversalMaskCell(job.WorkingWorld, x, y);
            }

            if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                return;
        }

        job.SectorCursor = 0;
        job.Stage = RuntimeDirtyRebuildStage.SymmetrizeNeighborMask;
    }

    private static void ProcessRuntimeDirtyCostField(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        while (job.SectorCursor < job.CostDirtySectorIds.Count)
        {
            RebuildCostFieldForSector(job.WorkingWorld, job.CostDirtySectorIds[job.SectorCursor++], job.CostStamps);
            if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                return;
        }

        job.SectorCursor = 0;
        job.Stage = RuntimeDirtyRebuildStage.IslandField;
    }

    private static void ProcessRuntimeDirtyIslandField(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("ProcessRuntimeDirtyIslandField failed: working world is null.");

        // The initial authored overlay is the first committed island authority. Its
        // source may only contain authored walkability, so it must assign every
        // walkable cell through the canonical full-field BFS before any later
        // runtime-dirty job is allowed to reuse island ids.
        if (job.IsInitialWorldOverlay)
        {
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
                out bool fullBuildComplete);
            if (!fullBuildComplete)
                return;

            RebuildAllSectorLocalComponents(world);
            job.SectorCursor = 0;
            job.Stage = RuntimeDirtyRebuildStage.GoalProjectionIndex;
            return;
        }

        AdvanceRuntimeSectorIslandConnectivity(job, deadlineTicks, forceComplete);
        if (job.IslandStage != RuntimeDirtyIslandStage.Complete)
            return;

        job.IslandInitialized = true;
        job.SectorCursor = 0;
        job.Stage = RuntimeDirtyRebuildStage.GoalProjectionIndex;
    }

    private static void ProcessRuntimeDirtySectorComponents(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        HashSet<int> dirtySectors = job.DirtySectors;
        if (world == null)
            throw new InvalidOperationException("ProcessRuntimeDirtySectorComponents failed: world is null.");
        if (world.Sectors == null)
            throw new InvalidOperationException("ProcessRuntimeDirtySectorComponents failed: sectors are null.");
        if (dirtySectors == null)
            throw new InvalidOperationException("ProcessRuntimeDirtySectorComponents failed: dirty sectors are null.");

        AdvanceRuntimeSectorIslandConnectivity(job, deadlineTicks, forceComplete);
        if (job.IslandStage != RuntimeDirtyIslandStage.Complete)
            return;
        LogIslandFieldDiagnostics(world, "runtime-dirty");
        job.SectorCursor = 0;
        job.Stage = RuntimeDirtyRebuildStage.GoalProjectionIndex;
    }

    private static void ProcessRuntimeDirtyGoalProjectionIndex(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        if (job == null || job.WorkingWorld == null)
            throw new InvalidOperationException("ProcessRuntimeDirtyGoalProjectionIndex failed: working world is null.");

        if (!ProcessGoalProjectionSpatialIndexBuild(
                job.WorkingWorld,
                ref job.GoalProjectionIndexBuildJob,
                deadlineTicks,
                forceComplete))
        {
            return;
        }

        job.Stage = RuntimeDirtyRebuildStage.PortalGraph;
    }

    private static void RebuildAllSectorLocalComponents(NavigationWorld world)
    {
        if (world == null)
            throw new InvalidOperationException("RebuildAllSectorLocalComponents failed: world is null.");
        if (world.Sectors == null)
            throw new InvalidOperationException("RebuildAllSectorLocalComponents failed: sectors are null.");

        for (int i = 0; i < world.Sectors.Length; i++)
        {
            RebuildSectorIslandMetadata(world, world.Sectors[i]);
            RebuildSectorLocalComponents(world, world.Sectors[i]);
        }

        RebuildSectorComponentIslandMappingsFromField(world);
    }

    private static void RebuildSectorComponentIslandMappingsFromField(NavigationWorld world)
    {
        if (world == null || world.Sectors == null || world.IslandIds == null)
            throw new InvalidOperationException("RebuildSectorComponentIslandMappingsFromField failed: world data is incomplete.");

        for (int sectorIndex = 0; sectorIndex < world.Sectors.Length; sectorIndex++)
        {
            SectorData sector = world.Sectors[sectorIndex];
            EnsureSectorComponentStats(world, sector);
            Array.Clear(sector.LocalComponentIslandIds, 0, sector.LocalComponentIslandIds.Length);
            for (int localIndex = 0; localIndex < sector.LocalComponentIds.Length; localIndex++)
            {
                int componentId = sector.LocalComponentIds[localIndex];
                if (componentId <= 0)
                    continue;

                int worldX = sector.StartX + localIndex % sector.Width;
                int worldY = sector.StartY + localIndex / sector.Width;
                int islandId = world.IslandIds[world.GetIndex(worldX, worldY)];
                if (islandId <= 0)
                    throw new InvalidOperationException(
                        $"RebuildSectorComponentIslandMappingsFromField failed: walkable component has invalid island. sector={sector.SectorId} component={componentId} cell=({worldX},{worldY}) island={islandId}.");

                int previous = sector.LocalComponentIslandIds[componentId];
                if (previous != 0 && previous != islandId)
                    throw new InvalidOperationException(
                        $"RebuildSectorComponentIslandMappingsFromField failed: local component crosses islands. sector={sector.SectorId} component={componentId} previous={previous} current={islandId}.");
                sector.LocalComponentIslandIds[componentId] = islandId;
            }
        }
    }

    private static void EnsureSectorComponentStats(NavigationWorld world, SectorData sector)
    {
        if (world == null)
            throw new InvalidOperationException("EnsureSectorComponentStats failed: world is null.");
        if (sector == null || sector.LocalComponentIds == null)
            throw new InvalidOperationException("EnsureSectorComponentStats failed: sector components are missing.");

        int componentCount = sector.LocalComponentCount;
        if (componentCount < 0)
            throw new InvalidOperationException($"EnsureSectorComponentStats failed: invalid component count sector={sector.SectorId} count={componentCount}.");

        int requiredLength = componentCount + 1;
        if (sector.LocalComponentIslandIds == null || sector.LocalComponentIslandIds.Length != requiredLength)
            sector.LocalComponentIslandIds = new int[requiredLength];
        if (sector.LocalComponentSizes == null || sector.LocalComponentSizes.Length != requiredLength)
            sector.LocalComponentSizes = new int[requiredLength];
        if (sector.LocalComponentMinCellIndices == null || sector.LocalComponentMinCellIndices.Length != requiredLength)
            sector.LocalComponentMinCellIndices = new int[requiredLength];

        Array.Clear(sector.LocalComponentSizes, 0, sector.LocalComponentSizes.Length);
        for (int i = 0; i < sector.LocalComponentMinCellIndices.Length; i++)
            sector.LocalComponentMinCellIndices[i] = int.MaxValue;

        for (int localIndex = 0; localIndex < sector.LocalComponentIds.Length; localIndex++)
        {
            int componentId = sector.LocalComponentIds[localIndex];
            if (componentId == 0)
                continue;
            if (componentId < 0 || componentId > componentCount)
                throw new InvalidOperationException(
                    $"EnsureSectorComponentStats failed: component id out of range. sector={sector.SectorId} component={componentId} count={componentCount}.");

            int worldX = sector.StartX + localIndex % sector.Width;
            int worldY = sector.StartY + localIndex / sector.Width;
            sector.LocalComponentSizes[componentId]++;
            int worldIndex = world.GetIndex(worldX, worldY);
            if (worldIndex < sector.LocalComponentMinCellIndices[componentId])
                sector.LocalComponentMinCellIndices[componentId] = worldIndex;
        }
    }

    private static void AdvanceRuntimeSectorIslandConnectivity(
        RuntimeDirtyRebuildJob job,
        long deadlineTicks,
        bool forceComplete)
    {
        NavigationWorld world = job?.WorkingWorld;
        if (world == null || world.Sectors == null)
            throw new InvalidOperationException("AdvanceRuntimeSectorIslandConnectivity failed: world data is incomplete.");
        if (job.DirtySectorIds == null)
            throw new InvalidOperationException("AdvanceRuntimeSectorIslandConnectivity failed: dirty sector index is missing.");

        while (job.IslandStage != RuntimeDirtyIslandStage.Complete)
        {
            switch (job.IslandStage)
            {
                case RuntimeDirtyIslandStage.RebuildDirtyComponents:
                    if (job.IslandSectorCursor < job.DirtySectorIds.Count)
                    {
                        int sectorId = job.DirtySectorIds[job.IslandSectorCursor++];
                        if (sectorId < 0 || sectorId >= world.Sectors.Length)
                            throw new InvalidOperationException($"Runtime island rebuild has an out-of-range dirty sector. sector={sectorId}.");
                        RebuildSectorLocalComponents(world, world.Sectors[sectorId]);
                        if (!forceComplete && IsBudgetExpired(deadlineTicks, job.IslandSectorCursor))
                            return;
                        break;
                    }

                    // Initial prebaked overlay and committed runtime edits are distinct
                    // authority states. Only the latter may use the local component graph;
                    // the initial overlay must finish the canonical full island build.
                    if (!job.IsInitialWorldOverlay && TryReuseRuntimeDirtyIslandField(job))
                    {
                        job.IslandStage = RuntimeDirtyIslandStage.Complete;
                        break;
                    }

                    job.IslandSectorCursor = 0;
                    job.IslandComponentOffsets = RentIntArray(world.Sectors.Length + 1, clear: true);
                    job.IslandStage = RuntimeDirtyIslandStage.PrepareComponentOffsets;
                    break;

                case RuntimeDirtyIslandStage.PrepareComponentOffsets:
                    if (job.IslandSectorCursor < world.Sectors.Length)
                    {
                        int sectorIndex = job.IslandSectorCursor++;
                        SectorData sector = world.Sectors[sectorIndex];
                        ValidateRuntimeSectorComponentStats(sector);
                        job.IslandComponentOffsets[sectorIndex + 1] = checked(
                            job.IslandComponentOffsets[sectorIndex] + sector.LocalComponentCount);
                        if (!forceComplete && IsBudgetExpired(deadlineTicks, job.IslandSectorCursor))
                            return;
                        break;
                    }

                    job.IslandComponentTotal = job.IslandComponentOffsets[world.Sectors.Length];
                    int componentCapacity = Math.Max(1, job.IslandComponentTotal);
                    job.IslandParents = RentIntArray(componentCapacity, clear: false);
                    job.IslandComponentSizes = RentIntArray(componentCapacity, clear: false);
                    job.IslandComponentMins = RentIntArray(componentCapacity, clear: false);
                    job.IslandRootHeap = RentIntArray(componentCapacity, clear: false);
                    job.IslandRootIds = RentIntArray(componentCapacity, clear: true);
                    job.IslandSectorCursor = 0;
                    job.IslandComponentCursor = 1;
                    job.IslandStage = RuntimeDirtyIslandStage.InitializeComponentNodes;
                    if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                        return;
                    break;

                case RuntimeDirtyIslandStage.InitializeComponentNodes:
                    if (job.IslandSectorCursor >= world.Sectors.Length)
                    {
                        job.IslandSectorCursor = 0;
                        job.IslandBoundaryCursor = 0;
                        job.IslandStage = RuntimeDirtyIslandStage.LinkSectorBoundaries;
                        break;
                    }

                    SectorData initializeSector = world.Sectors[job.IslandSectorCursor];
                    if (job.IslandComponentCursor > initializeSector.LocalComponentCount)
                    {
                        job.IslandSectorCursor++;
                        job.IslandComponentCursor = 1;
                        break;
                    }

                    int initializeComponent = job.IslandComponentCursor++;
                    int initializeNode = job.IslandComponentOffsets[initializeSector.SectorId] + initializeComponent - 1;
                    job.IslandParents[initializeNode] = initializeNode;
                    job.IslandComponentSizes[initializeNode] = initializeSector.LocalComponentSizes[initializeComponent];
                    job.IslandComponentMins[initializeNode] = initializeSector.LocalComponentMinCellIndices[initializeComponent];
                    if (!forceComplete && IsBudgetExpired(deadlineTicks, initializeNode))
                        return;
                    break;

                case RuntimeDirtyIslandStage.LinkSectorBoundaries:
                    if (job.IslandSectorCursor >= world.Sectors.Length)
                    {
                        job.IslandNodeCursor = 0;
                        job.IslandRootHeapCount = 0;
                        job.IslandStage = RuntimeDirtyIslandStage.CollectIslandRoots;
                        break;
                    }

                    SectorData boundarySector = world.Sectors[job.IslandSectorCursor];
                    int boundaryCellCount = GetSectorBoundaryCellCount(boundarySector);
                    if (job.IslandBoundaryCursor >= boundaryCellCount)
                    {
                        job.IslandSectorCursor++;
                        job.IslandBoundaryCursor = 0;
                        break;
                    }

                    GetSectorBoundaryCell(boundarySector, job.IslandBoundaryCursor++, out int boundaryX, out int boundaryY);
                    LinkRuntimeIslandBoundaryCell(job, boundarySector, boundaryX, boundaryY);
                    if (!forceComplete && IsBudgetExpired(deadlineTicks, job.IslandBoundaryCursor))
                        return;
                    break;

                case RuntimeDirtyIslandStage.CollectIslandRoots:
                    if (job.IslandNodeCursor >= job.IslandComponentTotal)
                    {
                        job.IslandNextId = 1;
                        job.IslandMainId = 0;
                        job.IslandMainSize = 0;
                        job.IslandStage = RuntimeDirtyIslandStage.AssignIslandIds;
                        break;
                    }

                    int rootCandidate = job.IslandNodeCursor++;
                    if (FindRuntimeIslandComponentRoot(job.IslandParents, rootCandidate) == rootCandidate)
                        PushRuntimeIslandRoot(job, rootCandidate);
                    if (!forceComplete && IsBudgetExpired(deadlineTicks, job.IslandNodeCursor))
                        return;
                    break;

                case RuntimeDirtyIslandStage.AssignIslandIds:
                    if (job.IslandRootHeapCount == 0)
                    {
                        world.IslandCount = job.IslandNextId - 1;
                        world.MainIslandId = job.IslandMainId;
                        world.MainIslandSize = job.IslandMainSize;
                        job.IslandSectorCursor = 0;
                        job.IslandComponentCursor = 0;
                        job.IslandStage = RuntimeDirtyIslandStage.AssignSectorMappings;
                        break;
                    }

                    int root = PopRuntimeIslandRoot(job);
                    int islandId = job.IslandNextId++;
                    job.IslandRootIds[root] = islandId;
                    int rootSize = job.IslandComponentSizes[root];
                    if (rootSize > job.IslandMainSize)
                    {
                        job.IslandMainId = islandId;
                        job.IslandMainSize = rootSize;
                    }
                    if (!forceComplete && IsBudgetExpired(deadlineTicks, islandId))
                        return;
                    break;

                case RuntimeDirtyIslandStage.AssignSectorMappings:
                    if (job.IslandSectorCursor >= world.Sectors.Length)
                    {
                        job.IslandCompatibilitySectorCursor = 0;
                        job.IslandCompatibilityCellCursor = 0;
                        job.IslandStage = RuntimeDirtyIslandStage.UpdateCompatibilityCache;
                        break;
                    }

                    SectorData mappingSector = world.Sectors[job.IslandSectorCursor];
                    if (job.IslandComponentCursor == 0)
                    {
                        job.IslandMappingNeedsWrite = job.DirtySectors.Contains(mappingSector.SectorId);
                        if (!job.IslandMappingNeedsWrite)
                        {
                            for (int componentId = 1; componentId <= mappingSector.LocalComponentCount; componentId++)
                            {
                                int node = job.IslandComponentOffsets[mappingSector.SectorId] + componentId - 1;
                                int componentRoot = FindRuntimeIslandComponentRoot(job.IslandParents, node);
                                int componentIslandId = job.IslandRootIds[componentRoot];
                                if (componentIslandId <= 0)
                                    throw new InvalidOperationException($"Runtime island root has no assigned island id. root={componentRoot}.");
                                if (mappingSector.LocalComponentIslandIds[componentId] != componentIslandId)
                                {
                                    job.IslandMappingNeedsWrite = true;
                                    break;
                                }
                            }
                        }

                        if (job.IslandMappingNeedsWrite)
                        {
                            if (!job.DirtySectors.Contains(mappingSector.SectorId))
                                mappingSector.LocalComponentIslandIds = (int[])mappingSector.LocalComponentIslandIds.Clone();
                            Array.Clear(mappingSector.LocalComponentIslandIds, 0, mappingSector.LocalComponentIslandIds.Length);
                        }
                        job.IslandUniformId = 0;
                        job.IslandUniformMixed = false;
                        job.IslandComponentCursor = 1;
                    }
                    if (job.IslandComponentCursor <= mappingSector.LocalComponentCount)
                    {
                        int componentId = job.IslandComponentCursor++;
                        int node = job.IslandComponentOffsets[mappingSector.SectorId] + componentId - 1;
                        int componentRoot = FindRuntimeIslandComponentRoot(job.IslandParents, node);
                        int componentIslandId = job.IslandRootIds[componentRoot];
                        if (componentIslandId <= 0)
                            throw new InvalidOperationException($"Runtime island root has no assigned island id. root={componentRoot}.");
                        if (job.IslandMappingNeedsWrite)
                            mappingSector.LocalComponentIslandIds[componentId] = componentIslandId;
                        if (job.IslandUniformId == 0)
                            job.IslandUniformId = componentIslandId;
                        else if (job.IslandUniformId != componentIslandId)
                            job.IslandUniformMixed = true;
                        if (!forceComplete && IsBudgetExpired(deadlineTicks, componentId))
                            return;
                        break;
                    }

                    mappingSector.UniformIslandId = job.IslandUniformMixed ? -1 : job.IslandUniformId;
                    job.IslandSectorCursor++;
                    job.IslandComponentCursor = 0;
                    job.IslandMappingNeedsWrite = false;
                    break;

                case RuntimeDirtyIslandStage.UpdateCompatibilityCache:
                    if (job.IslandCompatibilitySectorCursor >= job.DirtySectorIds.Count)
                    {
                        ReturnRuntimeDirtyIslandBuffers(job);
                        job.IslandStage = RuntimeDirtyIslandStage.Complete;
                        break;
                    }

                    SectorData compatibilitySector = world.Sectors[job.DirtySectorIds[job.IslandCompatibilitySectorCursor]];
                    if (job.IslandCompatibilityCellCursor >= compatibilitySector.LocalComponentIds.Length)
                    {
                        job.IslandCompatibilitySectorCursor++;
                        job.IslandCompatibilityCellCursor = 0;
                        break;
                    }

                    int localIndex = job.IslandCompatibilityCellCursor++;
                    int compatibilityComponent = compatibilitySector.LocalComponentIds[localIndex];
                    int worldX = compatibilitySector.StartX + localIndex % compatibilitySector.Width;
                    int worldY = compatibilitySector.StartY + localIndex / compatibilitySector.Width;
                    world.IslandIds[world.GetIndex(worldX, worldY)] = compatibilityComponent > 0
                        ? compatibilitySector.LocalComponentIslandIds[compatibilityComponent]
                        : 0;
                    if (!forceComplete && IsBudgetExpired(deadlineTicks, localIndex))
                        return;
                    break;

                default:
                    throw new InvalidOperationException($"Unknown runtime island stage {job.IslandStage}.");
            }
        }
    }

    private static bool TryReuseRuntimeDirtyIslandField(RuntimeDirtyRebuildJob job)
    {
        if (job == null || job.TargetWorld == null || job.WorkingWorld == null)
            throw new InvalidOperationException("TryReuseRuntimeDirtyIslandField failed: runtime-dirty world is missing.");
        if (job.DirtySectorIds == null || job.DirtySectors == null)
            throw new InvalidOperationException("TryReuseRuntimeDirtyIslandField failed: dirty-sector index is missing.");

        NavigationWorld source = job.TargetWorld;
        NavigationWorld working = job.WorkingWorld;
        if (source.IslandIds == null || source.IslandIds.Length != source.Width * source.Height
            || working.IslandIds == null || working.IslandIds.Length != working.Width * working.Height)
            throw new InvalidOperationException("TryReuseRuntimeDirtyIslandField failed: island fields are incomplete.");

        if (!job.IsInitialWorldOverlay)
        {
            for (int index = 0; index < source.IslandIds.Length; index++)
            {
                if (source.WalkableMask[index] && source.IslandIds[index] <= 0)
                {
                    int invalidX = index % source.Width;
                    int invalidY = index / source.Width;
                    int invalidSector = source.TryGetSectorId(invalidX, invalidY, out int resolvedSector)
                        ? resolvedSector
                        : -1;
                    throw new InvalidOperationException(
                        $"TryReuseRuntimeDirtyIslandField failed: committed source has invalid island. " +
                        $"agentType={source.AgentTypeId} worldVersion={source.Version} frame={LogicFrameRuntime.CurrentFrame} " +
                        $"world={source.Width}x{source.Height} index={index} cell=({invalidX},{invalidY}) sector={invalidSector} " +
                        $"walkable={source.WalkableMask[index]} island={source.IslandIds[index]} islandCount={source.IslandCount} " +
                        $"dirtySectors={string.Join(",", job.DirtySectorIds)}.");
                }
            }
        }

        // Runtime obstacles only remove walkable cells. Rebuild the small graph of local
        // components in dirty sectors and attach its boundary nodes to the unchanged
        // sector graph. A full-cell union-find is unnecessary and turns one building
        // placement into hundreds of logic ticks on large maps.
        var componentOffsets = new Dictionary<int, int>(job.DirtySectorIds.Count);
        int totalComponents = 0;
        for (int dirtyIndex = 0; dirtyIndex < job.DirtySectorIds.Count; dirtyIndex++)
        {
            int sectorId = job.DirtySectorIds[dirtyIndex];
            if (sectorId < 0 || sectorId >= working.Sectors.Length)
                throw new InvalidOperationException($"TryReuseRuntimeDirtyIslandField failed: dirty sector is out of range. sector={sectorId}.");
            SectorData sector = working.Sectors[sectorId];
            ValidateRuntimeSectorComponentStats(sector);
            componentOffsets.Add(sectorId, totalComponents);
            totalComponents = checked(totalComponents + sector.LocalComponentCount);
        }

        if (totalComponents == 0)
        {
            foreach (int sectorId in job.DirtySectorIds)
            {
                SectorData sector = working.Sectors[sectorId];
                for (int localIndex = 0; localIndex < sector.LocalComponentIds.Length; localIndex++)
                {
                    int x = sector.StartX + localIndex % sector.Width;
                    int y = sector.StartY + localIndex / sector.Width;
                    working.IslandIds[working.GetIndex(x, y)] = 0;
                }
            }
            RecomputeRuntimeIslandSummary(working);
            return true;
        }

        int[] parents = new int[totalComponents];
        int[] sizes = new int[totalComponents];
        int[] mins = new int[totalComponents];
        var externalIslandsByNode = new Dictionary<int, HashSet<int>>(totalComponents);
        foreach (int sectorId in job.DirtySectorIds)
        {
            SectorData sector = working.Sectors[sectorId];
            int offset = componentOffsets[sectorId];
            for (int componentId = 1; componentId <= sector.LocalComponentCount; componentId++)
            {
                int node = offset + componentId - 1;
                parents[node] = node;
                sizes[node] = sector.LocalComponentSizes[componentId];
                mins[node] = sector.LocalComponentMinCellIndices[componentId];
            }
        }

        foreach (int sectorId in job.DirtySectorIds)
        {
            SectorData sector = working.Sectors[sectorId];
            int boundaryCount = GetSectorBoundaryCellCount(sector);
            for (int boundaryCursor = 0; boundaryCursor < boundaryCount; boundaryCursor++)
            {
                GetSectorBoundaryCell(sector, boundaryCursor, out int x, out int y);
                int fromComponent = sector.LocalComponentIds[GetSectorLocalIndex(sector, x, y)];
                if (fromComponent <= 0)
                    continue;
                int fromNode = componentOffsets[sectorId] + fromComponent - 1;
                byte traversalMask = working.NeighborTraversalMask[working.GetIndex(x, y)];
                for (int direction = 0; direction < NeighborOffsetX.Length; direction++)
                {
                    if ((traversalMask & (1 << direction)) == 0)
                        continue;
                    int nextX = x + NeighborOffsetX[direction];
                    int nextY = y + NeighborOffsetY[direction];
                    if (!working.TryGetSectorId(nextX, nextY, out int nextSectorId) || nextSectorId == sectorId)
                        continue;
                    SectorData nextSector = working.Sectors[nextSectorId];
                    int nextComponent = nextSector.LocalComponentIds[GetSectorLocalIndex(nextSector, nextX, nextY)];
                    if (nextComponent <= 0)
                        continue;

                    if (job.DirtySectors.Contains(nextSectorId))
                    {
                        int nextNode = componentOffsets[nextSectorId] + nextComponent - 1;
                        UnionRuntimeIslandComponents(parents, sizes, mins, fromNode, nextNode);
                        continue;
                    }

                    int externalIsland = source.IslandIds[source.GetIndex(nextX, nextY)];
                    if (externalIsland <= 0)
                        throw new InvalidOperationException(
                            $"TryReuseRuntimeDirtyIslandField failed: unchanged boundary cell has no island. cell=({nextX},{nextY}).");
                    if (!externalIslandsByNode.TryGetValue(fromNode, out HashSet<int> externalIslands))
                    {
                        externalIslands = new HashSet<int>();
                        externalIslandsByNode.Add(fromNode, externalIslands);
                    }
                    externalIslands.Add(externalIsland);
                }
            }
        }

        var externalIslandsByRoot = new Dictionary<int, HashSet<int>>();
        foreach (KeyValuePair<int, HashSet<int>> pair in externalIslandsByNode)
        {
            int root = FindRuntimeIslandComponentRoot(parents, pair.Key);
            if (!externalIslandsByRoot.TryGetValue(root, out HashSet<int> rootIslands))
            {
                rootIslands = new HashSet<int>();
                externalIslandsByRoot.Add(root, rootIslands);
            }
            foreach (int islandId in pair.Value)
                rootIslands.Add(islandId);
        }

        // A removed bridge can split one previously connected island into multiple
        // dirty roots. Reusing the old island id for all of them would silently merge
        // the split topology back together; only reuse when every old island is owned
        // by exactly one new root.
        var rootsByExternalIsland = new Dictionary<int, int>();
        foreach (KeyValuePair<int, HashSet<int>> pair in externalIslandsByRoot)
        {
            foreach (int islandId in pair.Value)
            {
                if (rootsByExternalIsland.TryGetValue(islandId, out int existingRoot)
                    && existingRoot != pair.Key)
                {
                    return false;
                }

                rootsByExternalIsland[islandId] = pair.Key;
            }
        }

        var assignedIslandByRoot = new Dictionary<int, int>();
        int nextIslandId = Math.Max(source.IslandCount, 0) + 1;
        for (int node = 0; node < totalComponents; node++)
        {
            int root = FindRuntimeIslandComponentRoot(parents, node);
            if (assignedIslandByRoot.ContainsKey(root))
                continue;
            if (externalIslandsByRoot.TryGetValue(root, out HashSet<int> rootIslands))
            {
                if (rootIslands.Count != 1)
                    throw new InvalidOperationException(
                        $"TryReuseRuntimeDirtyIslandField failed: runtime obstacle would merge independent islands. root={root} islands={rootIslands.Count}.");
                foreach (int islandId in rootIslands)
                    assignedIslandByRoot.Add(root, islandId);
            }
            else
            {
                assignedIslandByRoot.Add(root, nextIslandId++);
            }
        }

        foreach (int sectorId in job.DirtySectorIds)
        {
            SectorData sector = working.Sectors[sectorId];
            int offset = componentOffsets[sectorId];
            Array.Clear(sector.LocalComponentIslandIds, 0, sector.LocalComponentIslandIds.Length);
            for (int componentId = 1; componentId <= sector.LocalComponentCount; componentId++)
            {
                int root = FindRuntimeIslandComponentRoot(parents, offset + componentId - 1);
                sector.LocalComponentIslandIds[componentId] = assignedIslandByRoot[root];
            }

            for (int localIndex = 0; localIndex < sector.LocalComponentIds.Length; localIndex++)
            {
                int componentId = sector.LocalComponentIds[localIndex];
                int x = sector.StartX + localIndex % sector.Width;
                int y = sector.StartY + localIndex / sector.Width;
                int index = working.GetIndex(x, y);
                working.IslandIds[index] = componentId > 0
                    ? sector.LocalComponentIslandIds[componentId]
                    : 0;
            }
            RebuildSectorIslandMetadata(working, sector);
        }

        RecomputeRuntimeIslandSummary(working);
        return true;
    }

    private static void RecomputeRuntimeIslandSummary(NavigationWorld world)
    {
        var sizes = new Dictionary<int, int>();
        for (int index = 0; index < world.IslandIds.Length; index++)
        {
            if (!world.WalkableMask[index])
            {
                world.IslandIds[index] = 0;
                continue;
            }
            int islandId = world.IslandIds[index];
            if (islandId <= 0)
                throw new InvalidOperationException($"RecomputeRuntimeIslandSummary failed: walkable cell has invalid island. index={index}.");
            if (sizes.TryGetValue(islandId, out int size))
                sizes[islandId] = size + 1;
            else
                sizes.Add(islandId, 1);
        }

        world.IslandCount = sizes.Count;
        world.MainIslandId = 0;
        world.MainIslandSize = 0;
        foreach (KeyValuePair<int, int> pair in sizes)
        {
            if (pair.Value > world.MainIslandSize
                || (pair.Value == world.MainIslandSize && (world.MainIslandId == 0 || pair.Key < world.MainIslandId)))
            {
                world.MainIslandId = pair.Key;
                world.MainIslandSize = pair.Value;
            }
        }
    }

    private static void ValidateRuntimeSectorComponentStats(SectorData sector)
    {
        if (sector == null || sector.LocalComponentIds == null)
            throw new InvalidOperationException("Runtime island sector components are missing.");
        int requiredLength = sector.LocalComponentCount + 1;
        if (sector.LocalComponentIslandIds == null || sector.LocalComponentIslandIds.Length != requiredLength
            || sector.LocalComponentSizes == null || sector.LocalComponentSizes.Length != requiredLength
            || sector.LocalComponentMinCellIndices == null || sector.LocalComponentMinCellIndices.Length != requiredLength)
        {
            throw new InvalidOperationException($"Runtime island sector component statistics are invalid. sector={sector.SectorId}.");
        }
    }

    private static int GetSectorBoundaryCellCount(SectorData sector)
    {
        if (sector.Width <= 0 || sector.Height <= 0)
            throw new InvalidOperationException($"Runtime island sector has invalid dimensions. sector={sector.SectorId} size={sector.Width}x{sector.Height}.");
        if (sector.Height == 1)
            return sector.Width;
        if (sector.Width == 1)
            return sector.Height;
        return checked(2 * sector.Width + 2 * (sector.Height - 2));
    }

    private static void GetSectorBoundaryCell(SectorData sector, int cursor, out int x, out int y)
    {
        int count = GetSectorBoundaryCellCount(sector);
        if (cursor < 0 || cursor >= count)
            throw new ArgumentOutOfRangeException(nameof(cursor), cursor, "Sector boundary cursor is out of range.");
        if (sector.Height == 1)
        {
            x = sector.StartX + cursor;
            y = sector.StartY;
            return;
        }
        if (sector.Width == 1)
        {
            x = sector.StartX;
            y = sector.StartY + cursor;
            return;
        }
        if (cursor < sector.Width)
        {
            x = sector.StartX + cursor;
            y = sector.StartY;
            return;
        }
        cursor -= sector.Width;
        if (cursor < sector.Width)
        {
            x = sector.StartX + cursor;
            y = sector.StartY + sector.Height - 1;
            return;
        }
        cursor -= sector.Width;
        int sideHeight = sector.Height - 2;
        if (cursor < sideHeight)
        {
            x = sector.StartX;
            y = sector.StartY + cursor + 1;
            return;
        }
        cursor -= sideHeight;
        x = sector.StartX + sector.Width - 1;
        y = sector.StartY + cursor + 1;
    }

    private static void LinkRuntimeIslandBoundaryCell(
        RuntimeDirtyRebuildJob job,
        SectorData sector,
        int x,
        int y)
    {
        NavigationWorld world = job.WorkingWorld;
        int fromComponent = sector.LocalComponentIds[GetSectorLocalIndex(sector, x, y)];
        if (fromComponent <= 0)
            return;

        byte traversalMask = world.NeighborTraversalMask[world.GetIndex(x, y)];
        for (int direction = 0; direction < NeighborOffsetX.Length; direction++)
        {
            int nextX = x + NeighborOffsetX[direction];
            int nextY = y + NeighborOffsetY[direction];
            if (!world.TryGetSectorId(nextX, nextY, out int nextSectorId) || nextSectorId == sector.SectorId)
                continue;
            if ((traversalMask & (1 << direction)) == 0)
                continue;

            SectorData nextSector = world.Sectors[nextSectorId];
            int nextComponent = nextSector.LocalComponentIds[GetSectorLocalIndex(nextSector, nextX, nextY)];
            if (nextComponent <= 0)
                continue;
            UnionRuntimeIslandComponents(
                job.IslandParents,
                job.IslandComponentSizes,
                job.IslandComponentMins,
                job.IslandComponentOffsets[sector.SectorId] + fromComponent - 1,
                job.IslandComponentOffsets[nextSectorId] + nextComponent - 1);
        }
    }

    private static void PushRuntimeIslandRoot(RuntimeDirtyRebuildJob job, int root)
    {
        int index = job.IslandRootHeapCount++;
        while (index > 0)
        {
            int parentIndex = (index - 1) >> 1;
            int parentRoot = job.IslandRootHeap[parentIndex];
            if (CompareRuntimeIslandRoots(job, parentRoot, root) <= 0)
                break;
            job.IslandRootHeap[index] = parentRoot;
            index = parentIndex;
        }
        job.IslandRootHeap[index] = root;
    }

    private static int PopRuntimeIslandRoot(RuntimeDirtyRebuildJob job)
    {
        if (job.IslandRootHeapCount <= 0)
            throw new InvalidOperationException("Runtime island root heap is empty.");
        int result = job.IslandRootHeap[0];
        int replacement = job.IslandRootHeap[--job.IslandRootHeapCount];
        int index = 0;
        while (true)
        {
            int left = index * 2 + 1;
            if (left >= job.IslandRootHeapCount)
                break;
            int right = left + 1;
            int child = right < job.IslandRootHeapCount
                        && CompareRuntimeIslandRoots(job, job.IslandRootHeap[right], job.IslandRootHeap[left]) < 0
                ? right
                : left;
            if (CompareRuntimeIslandRoots(job, replacement, job.IslandRootHeap[child]) <= 0)
                break;
            job.IslandRootHeap[index] = job.IslandRootHeap[child];
            index = child;
        }
        if (job.IslandRootHeapCount > 0)
            job.IslandRootHeap[index] = replacement;
        return result;
    }

    private static int CompareRuntimeIslandRoots(RuntimeDirtyRebuildJob job, int left, int right)
    {
        int compare = job.IslandComponentMins[left].CompareTo(job.IslandComponentMins[right]);
        return compare != 0 ? compare : left.CompareTo(right);
    }

    private static int FindRuntimeIslandComponentRoot(int[] parent, int node)
    {
        int root = node;
        while (parent[root] != root)
            root = parent[root];
        while (parent[node] != node)
        {
            int next = parent[node];
            parent[node] = root;
            node = next;
        }
        return root;
    }

    private static void UnionRuntimeIslandComponents(int[] parent, int[] sizes, int[] mins, int left, int right)
    {
        int leftRoot = FindRuntimeIslandComponentRoot(parent, left);
        int rightRoot = FindRuntimeIslandComponentRoot(parent, right);
        if (leftRoot == rightRoot)
            return;
        if (sizes[leftRoot] < sizes[rightRoot]
            || (sizes[leftRoot] == sizes[rightRoot] && mins[leftRoot] > mins[rightRoot]))
        {
            int swap = leftRoot;
            leftRoot = rightRoot;
            rightRoot = swap;
        }
        parent[rightRoot] = leftRoot;
        sizes[leftRoot] = checked(sizes[leftRoot] + sizes[rightRoot]);
        mins[leftRoot] = Math.Min(mins[leftRoot], mins[rightRoot]);
    }

    private static void RebuildSectorIslandMetadata(NavigationWorld world, SectorData sector)
    {
        if (world == null)
            throw new InvalidOperationException("RebuildSectorIslandMetadata failed: world is null.");
        if (sector == null)
            throw new InvalidOperationException("RebuildSectorIslandMetadata failed: sector is null.");
        if (world.IslandIds == null || world.IslandIds.Length != world.Width * world.Height)
            throw new InvalidOperationException("RebuildSectorIslandMetadata failed: island field is missing or invalid.");

        int uniformIslandId = 0;
        bool mixed = false;
        for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
        {
            int rowStart = y * world.Width;
            for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
            {
                int index = rowStart + x;
                if (!world.WalkableMask[index])
                    continue;

                int islandId = world.IslandIds[index];
                if (islandId <= 0)
                    throw new InvalidOperationException($"RebuildSectorIslandMetadata failed: walkable cell ({x},{y}) has invalid island {islandId}.");

                if (uniformIslandId == 0)
                {
                    uniformIslandId = islandId;
                    continue;
                }

                if (uniformIslandId == islandId)
                    continue;

                mixed = true;
                break;
            }

            if (mixed)
                break;
        }

        sector.UniformIslandId = mixed ? -1 : uniformIslandId;
    }

    private static void RebuildSectorLocalComponents(NavigationWorld world, SectorData sector)
    {
        if (sector == null)
            throw new InvalidOperationException("RebuildSectorLocalComponents failed: sector is null.");

        int cellCount = sector.Width * sector.Height;
        if (sector.LocalComponentIds == null || sector.LocalComponentIds.Length != cellCount)
            sector.LocalComponentIds = new int[cellCount];
        else
            Array.Clear(sector.LocalComponentIds, 0, sector.LocalComponentIds.Length);

        int componentId = 0;
        Queue<int> open = new Queue<int>(Mathf.Min(256, Mathf.Max(1, cellCount)));
        for (int localIndex = 0; localIndex < cellCount; localIndex++)
        {
            if (sector.LocalComponentIds[localIndex] != 0)
                continue;

            int worldX = sector.StartX + localIndex % sector.Width;
            int worldY = sector.StartY + localIndex / sector.Width;
            if (!world.IsWalkable(worldX, worldY))
                continue;

            componentId++;
            sector.LocalComponentIds[localIndex] = componentId;
            open.Enqueue(localIndex);
            while (open.Count > 0)
            {
                int currentLocal = open.Dequeue();
                int currentX = sector.StartX + currentLocal % sector.Width;
                int currentY = sector.StartY + currentLocal / sector.Width;
                byte traversalMask = world.NeighborTraversalMask[world.GetIndex(currentX, currentY)];
                for (int i = 0; i < NeighborOffsetX.Length; i++)
                {
                    if ((traversalMask & (1 << i)) == 0)
                        continue;

                    int nextX = currentX + NeighborOffsetX[i];
                    int nextY = currentY + NeighborOffsetY[i];
                    if (!IsInsideSector(sector, nextX, nextY) || !world.IsWalkable(nextX, nextY))
                        continue;

                    int nextLocal = GetSectorLocalIndex(sector, nextX, nextY);
                    if (sector.LocalComponentIds[nextLocal] != 0)
                        continue;

                    sector.LocalComponentIds[nextLocal] = componentId;
                    open.Enqueue(nextLocal);
                }
            }
        }

        sector.LocalComponentCount = componentId;
        EnsureSectorComponentStats(world, sector);
    }

    private static void AdvanceIslandFieldBuild(
        NavigationWorld world,
        Queue<int> openQueue,
        ref int scanIndex,
        ref int currentId,
        ref int currentSize,
        ref int mainId,
        ref int mainSize,
        ref bool bfsActive,
        long deadlineTicks,
        bool forceComplete,
        out bool complete)
    {
        if (world == null)
            throw new InvalidOperationException("AdvanceIslandFieldBuild failed: world is null.");
        if (openQueue == null)
            throw new InvalidOperationException("AdvanceIslandFieldBuild failed: open queue is null.");

        complete = false;
        while (true)
        {
            if (bfsActive)
            {
                int budgetWork = 0;
                while (openQueue.Count > 0)
                {
                    int currentIndex = openQueue.Dequeue();
                    currentSize++;
                    int currentX = currentIndex % world.Width;
                    int currentY = currentIndex / world.Width;
                    byte traversalMask = world.NeighborTraversalMask[currentIndex];
                    for (int i = 0; i < NeighborOffsetX.Length; i++)
                    {
                        if ((traversalMask & (1 << i)) == 0)
                            continue;

                        int nextX = currentX + NeighborOffsetX[i];
                        int nextY = currentY + NeighborOffsetY[i];
                        if (!world.IsWalkable(nextX, nextY))
                            continue;

                        int nextIndex = world.GetIndex(nextX, nextY);
                        if (world.IslandIds[nextIndex] != 0)
                            continue;

                        world.IslandIds[nextIndex] = currentId;
                        openQueue.Enqueue(nextIndex);
                    }

                    if (!forceComplete && IsBudgetExpired(deadlineTicks, ++budgetWork))
                        return;
                }

                if (currentSize > mainSize)
                {
                    mainId = currentId;
                    mainSize = currentSize;
                }

                bfsActive = false;
                currentSize = 0;
            }

            while (scanIndex < world.Width * world.Height)
            {
                int index = scanIndex++;
                if (!world.WalkableMask[index] || world.IslandIds[index] != 0)
                    continue;

                currentId++;
                world.IslandIds[index] = currentId;
                openQueue.Enqueue(index);
                bfsActive = true;
                break;
            }

            if (bfsActive)
            {
                if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                    return;

                continue;
            }

            world.IslandCount = currentId;
            world.MainIslandId = mainId;
            world.MainIslandSize = mainSize;
            complete = true;
            return;
        }
    }

    private static bool ProcessRuntimeDirtyPortalGraph(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (!job.PortalInitialized)
        {
            job.PendingPortalAccessEntries ??= new List<PendingSectorPortalAccess>();
            job.PendingPortalAccessEntries.Clear();
            job.PortalTransitionDirtySectors = new HashSet<int>(job.DirtySectors);
            foreach (int sectorId in job.CostDirtySectors)
                job.PortalTransitionDirtySectors.Add(sectorId);

            job.PortalRebuiltPortals = new List<PortalData>(32);
            job.PortalProcessedBoundaries = new HashSet<long>();
            job.PortalStage = RuntimeDirtyPortalStage.RemoveOldPortals;
            job.PortalSectorCursor = 0;
            job.PortalAddCursor = 0;
            job.PortalTransitionCursor = 0;
            job.PortalTransitionFromCursor = 0;
            job.PortalInitialized = true;
        }

        while (job.PortalStage != RuntimeDirtyPortalStage.Complete)
        {
            switch (job.PortalStage)
            {
                case RuntimeDirtyPortalStage.RemoveOldPortals:
                    long removeStartTicks = Stopwatch.GetTimestamp();
                    RemovePortalsTouchingSectors(world, job.DirtySectors, job.PortalTransitionDirtySectors);
                    job.PortalGraphBoundaryTicks += Stopwatch.GetTimestamp() - removeStartTicks;
                    job.PortalStage = RuntimeDirtyPortalStage.RebuildBoundaries;
                    break;
                case RuntimeDirtyPortalStage.RebuildBoundaries:
                    while (job.PortalSectorCursor < job.DirtySectorIds.Count)
                    {
                        int sectorId = job.DirtySectorIds[job.PortalSectorCursor++];
                        long boundaryStartTicks = Stopwatch.GetTimestamp();
                        RebuildPortalBoundariesForSector(
                            world,
                            sectorId,
                            job.DirtySectors,
                            job.PortalProcessedBoundaries,
                            job.PortalRebuiltPortals);
                        job.PortalGraphBoundaryTicks += Stopwatch.GetTimestamp() - boundaryStartTicks;

                        if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                            return false;
                    }

                    job.PortalStage = RuntimeDirtyPortalStage.AddRebuiltPortals;
                    break;
                case RuntimeDirtyPortalStage.AddRebuiltPortals:
                    while (job.PortalAddCursor < job.PortalRebuiltPortals.Count)
                    {
                        long addStartTicks = Stopwatch.GetTimestamp();
                        PortalData portal = job.PortalRebuiltPortals[job.PortalAddCursor++];
                        if (world.PortalsById.ContainsKey(portal.PortalId))
                            throw new InvalidOperationException($"ProcessRuntimeDirtyPortalGraph failed: duplicate portal id {portal.PortalId}.");

                        world.PortalsById.Add(portal.PortalId, portal);
                        world.Sectors[portal.SectorAId].PortalIds.Add(portal.PortalId);
                        world.Sectors[portal.SectorBId].PortalIds.Add(portal.PortalId);
                        job.PortalTransitionDirtySectors.Add(portal.SectorAId);
                        job.PortalTransitionDirtySectors.Add(portal.SectorBId);
                        job.PortalGraphAddTicks += Stopwatch.GetTimestamp() - addStartTicks;

                        if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                            return false;
                    }

                    world.Portals = BuildPortalArray(world);
                    job.PortalTransitionSectorIds = CreateSortedSectorIdSnapshot(
                        job.PortalTransitionDirtySectors,
                        "portal transition dirty sectors");
                    job.PortalTransitionCursor = 0;
                    job.PortalTransitionFromCursor = 0;
                    job.PortalStage = RuntimeDirtyPortalStage.RebuildTransitions;
                    break;
                case RuntimeDirtyPortalStage.RebuildTransitions:
                    if (!ProcessRuntimeDirtyPortalTransitions(job, deadlineTicks, forceComplete))
                        return false;
                    break;
                default:
                    throw new InvalidOperationException($"ProcessRuntimeDirtyPortalGraph failed: unknown portal stage {job.PortalStage}.");
            }

            if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                return false;
        }

        LogRuntimeDirtyPortalGraphTiming(job);
        job.Stage = RuntimeDirtyRebuildStage.Hierarchy;
        return true;
    }

    private static void ProcessRuntimeDirtyHierarchy(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        if (job.WorkingWorld == null)
            throw new InvalidOperationException("ProcessRuntimeDirtyHierarchy failed: working world is null.");
        if (CanReuseRuntimeDirtyHierarchy(job))
        {
            // Hierarchy edge weights are derived from the portal graph. If the runtime
            // obstacle changed neither portal geometry nor transition costs, rebuilding
            // every hierarchy source is redundant; retain the immutable committed graph.
            job.WorkingWorld.Hierarchy = job.TargetWorld.Hierarchy;
            job.Stage = RuntimeDirtyRebuildStage.PrepareCommit;
            return;
        }
        if (job.HierarchyBuildJob == null)
        {
            job.HierarchyBuildJob = CreatePortalHierarchyBuildJob(job.WorkingWorld, DefaultHierarchyFanout);
            if (job.TargetWorld.Hierarchy != null
                && job.PortalTransitionDirtySectors != null
                && job.PortalTransitionDirtySectors.Count > 0)
            {
                job.HierarchyBuildJob.ReuseSource = job.TargetWorld.Hierarchy;
                job.HierarchyBuildJob.AffectedSectors = new HashSet<int>(job.PortalTransitionDirtySectors);
            }
        }
        ProcessPortalHierarchyBuildJob(job.HierarchyBuildJob, deadlineTicks, forceComplete);
        if (!job.HierarchyBuildJob.Complete)
            return;
        job.WorkingWorld.Hierarchy = job.HierarchyBuildJob.Result;
        job.Stage = RuntimeDirtyRebuildStage.PrepareCommit;
    }

    private static bool CanReuseRuntimeDirtyHierarchy(RuntimeDirtyRebuildJob job)
    {
        if (job == null || job.TargetWorld == null || job.WorkingWorld == null)
            throw new InvalidOperationException("CanReuseRuntimeDirtyHierarchy failed: runtime-dirty world is missing.");
        if (job.TargetWorld.Hierarchy == null || job.TargetWorld.PortalsById == null || job.WorkingWorld.PortalsById == null)
            return false;
        if (job.TargetWorld.PortalsById.Count != job.WorkingWorld.PortalsById.Count)
            return false;

        foreach (KeyValuePair<int, PortalData> pair in job.TargetWorld.PortalsById)
        {
            if (!job.WorkingWorld.PortalsById.TryGetValue(pair.Key, out PortalData candidate)
                || !AreRuntimePortalDataEquivalent(pair.Value, candidate))
                return false;
        }

        if (job.TargetWorld.Sectors == null || job.WorkingWorld.Sectors == null
            || job.TargetWorld.Sectors.Length != job.WorkingWorld.Sectors.Length)
            return false;
        for (int sectorIndex = 0; sectorIndex < job.TargetWorld.Sectors.Length; sectorIndex++)
        {
            SectorData sourceSector = job.TargetWorld.Sectors[sectorIndex];
            SectorData candidateSector = job.WorkingWorld.Sectors[sectorIndex];
            if (sourceSector.PortalIds.Count != candidateSector.PortalIds.Count
                || sourceSector.PortalTransitions.Count != candidateSector.PortalTransitions.Count)
                return false;
            for (int i = 0; i < sourceSector.PortalIds.Count; i++)
            {
                if (sourceSector.PortalIds[i] != candidateSector.PortalIds[i])
                    return false;
            }
            for (int i = 0; i < sourceSector.PortalTransitions.Count; i++)
            {
                PortalTransition sourceTransition = sourceSector.PortalTransitions[i];
                PortalTransition candidateTransition = candidateSector.PortalTransitions[i];
                if (sourceTransition.FromPortalId != candidateTransition.FromPortalId
                    || sourceTransition.ToPortalId != candidateTransition.ToPortalId
                    || sourceTransition.DeterministicCost != candidateTransition.DeterministicCost)
                    return false;
            }
        }

        return true;
    }

    private static bool AreRuntimePortalDataEquivalent(PortalData source, PortalData candidate)
    {
        if (source == null || candidate == null)
            return source == candidate;
        if (source.PortalId != candidate.PortalId
            || source.SectorAId != candidate.SectorAId
            || source.SectorBId != candidate.SectorBId
            || source.WidthCells != candidate.WidthCells
            || source.IsNarrow != candidate.IsNarrow
            || source.IsVerticalBoundary != candidate.IsVerticalBoundary
            || source.CellsA == null || source.CellsB == null
            || candidate.CellsA == null || candidate.CellsB == null
            || source.CellsA.Length != candidate.CellsA.Length
            || source.CellsB.Length != candidate.CellsB.Length)
            return false;
        for (int i = 0; i < source.CellsA.Length; i++)
        {
            if (source.CellsA[i] != candidate.CellsA[i])
                return false;
        }
        for (int i = 0; i < source.CellsB.Length; i++)
        {
            if (source.CellsB[i] != candidate.CellsB[i])
                return false;
        }
        return true;
    }

    private static bool ProcessRuntimeDirtyPortalTransitions(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        int processedSourceCount = 0;
        while (job.PortalTransitionCursor < job.PortalTransitionSectorIds.Count)
        {
            int sectorId = job.PortalTransitionSectorIds[job.PortalTransitionCursor];
            if (sectorId < 0 || sectorId >= world.Sectors.Length)
            {
                job.PortalTransitionCursor++;
                job.PortalTransitionFromCursor = 0;
                continue;
            }

            SectorData sector = world.Sectors[sectorId];
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

                PendingSectorPortalAccess pending = analytic
                    ? AddPendingAnalyticSectorPortalAccess(job.PendingPortalAccessEntries, sector, fromPortalId)
                    : AddPendingPrebuiltDeterministicSectorPortalAccess(
                        world,
                        job.PendingPortalAccessEntries,
                        sector,
                        fromPortalId);

                AddDeterministicPortalTransitionTopology(world, sector, fromPortalId);
                ApplyPendingPortalTransitionCosts(world, sector, pending);
                job.PortalGraphAccessCount++;
                job.PortalGraphTransitionCostChecks++;
                if (analytic)
                    job.PortalGraphAnalyticAccessCount++;

                job.PortalTransitionFromCursor++;
                processedSourceCount++;
                if (!forceComplete
                    && (IsBudgetExpired(deadlineTicks, job.PortalTransitionFromCursor)
                        || processedSourceCount >= RuntimeDirtyPortalTransitionSourceQuota))
                {
                    return false;
                }
            }

            RebuildIncomingPortalTransitionIndex(sector);
            job.PortalTransitionCursor++;
            job.PortalTransitionFromCursor = 0;
        }

        job.PortalStage = RuntimeDirtyPortalStage.Complete;
        return true;
    }

    private static void PrepareRuntimeDirtyCommit(RuntimeDirtyRebuildJob job)
    {
        if (job == null)
            throw new InvalidOperationException("PrepareRuntimeDirtyCommit failed: job is null.");
        if (job.WorkingWorld == null)
            throw new InvalidOperationException("PrepareRuntimeDirtyCommit failed: working world is null.");
        if (job.WorkingWorld.GoalProjectionSpatialIndex == null)
            throw new InvalidOperationException(
                "PrepareRuntimeDirtyCommit failed: exact goal-projection index was not prepared.");
        if (job.WorldHashBuildJob != null)
            throw new InvalidOperationException("PrepareRuntimeDirtyCommit failed: world hash job already exists.");

        ValidatePendingSectorPortalAccessCoverage(job.WorkingWorld, job.PendingPortalAccessEntries, "runtime-dirty-prepare");
        FinalizeWorldCostStorage(job.WorkingWorld);
        if (job.WorkingWorld.Hierarchy == null)
            throw new InvalidOperationException("PrepareRuntimeDirtyCommit failed: hierarchy was not prepared.");
        EnsureFlowPathKernelWitnessIndex(job.WorkingWorld.Hierarchy);
        foreach (int sectorId in job.CostDirtySectors)
        {
            if (sectorId < 0 || sectorId >= job.WorkingWorld.Sectors.Length)
                throw new InvalidOperationException($"PrepareRuntimeDirtyCommit failed: dirty hash sector out of range. sector={sectorId}.");
            job.WorkingWorld.Sectors[sectorId].HasDeterministicContentHash = false;
        }
        if (job.WorkingWorld.L0SearchGraphIndex != null)
            throw new InvalidOperationException("PrepareRuntimeDirtyCommit found a pre-existing working L0 graph index.");
        if (job.WorkingWorld.Hierarchy.SearchGraphIndexes == null)
            EnsureFlowPathKernelSearchGraphIndexes(job.WorkingWorld);
        else
            job.WorkingWorld.L0SearchGraphIndex = BuildFlowPathKernelL0SearchGraphIndex(job.WorkingWorld);
        job.WorldHashBuildJob = CreateNavigationWorldHashBuildJob(job.WorkingWorld);
    }

    private static bool CanUseAnalyticPortalAccess(NavigationWorld world, SectorData sector, int portalId)
    {
        return CanUseAnalyticPortalAccess(world, sector, portalId, out _);
    }

    private static bool CanUseAnalyticPortalAccess(NavigationWorld world, SectorData sector, int portalId, out AnalyticPortalAccessDiagnostics diagnostics)
    {
        if (world == null)
            throw new InvalidOperationException("CanUseAnalyticPortalAccess failed: world is null.");
        if (sector == null)
            throw new InvalidOperationException("CanUseAnalyticPortalAccess failed: sector is null.");

        diagnostics = default;
        if (sector.LocalComponentCount > 1)
        {
            diagnostics.RejectReason = AnalyticPortalAccessRejectReason.MultipleLocalComponents;
            return false;
        }

        if (sector.IsClearFlowTile)
        {
            diagnostics.IsClearSector = true;
            return true;
        }

        diagnostics.RejectReason = AnalyticPortalAccessRejectReason.NonClearSector;
        return false;
    }

    private static void CommitRuntimeDirtyJob(WorldRuntimeState state, RuntimeDirtyRebuildJob job)
    {
        long stepStartTicks = Stopwatch.GetTimestamp();
        NavigationWorld target = job.TargetWorld;
        NavigationWorld working = job.WorkingWorld;
        if (state.World != target)
            throw new InvalidOperationException("CommitRuntimeDirtyJob failed: target world changed while job was pending.");
        if (!working.HasDeterministicContentHash || job.WorldHashBuildJob == null || !job.WorldHashBuildJob.Complete)
            throw new InvalidOperationException("CommitRuntimeDirtyJob failed: working world hash is incomplete.");
        if (working.GoalProjectionSpatialIndex == null)
            throw new InvalidOperationException(
                "CommitRuntimeDirtyJob failed: working goal-projection index is incomplete.");

        PrepareRuntimeDirtyFlowTileWorldInputBackings(job);

        bool prewarmAffected = NavigationDistancePrewarmTouchesDirtyWorld(target, job.CostDirtySectors);
        bool[] oldWalkableMask = target.WalkableMask;
        byte[] oldNeighborTraversalMask = target.NeighborTraversalMask;
        int[] oldIslandIds = target.IslandIds;
        FlowTileSectorInputBacking[] oldInputBackings = target.FlowTileSectorInputBackings;
        FlowPathKernelGraphIndex oldL0SearchGraphIndex = target.L0SearchGraphIndex;
        List<SectorPortalAccessKey> oldPortalAccessKeys = CreatePortalAccessInvalidationKeys(
            target,
            job.PortalTransitionSectorIds);
        int corridorPoliciesBefore = SectorCorridorPolicies.Count;
        int flowTilesBefore = FlowTileCache.Count;
        int sharedGoalsBefore = SharedGoalFields.Count;
        int anchorsBefore = MovingTargetAnchors.Count;
        int combatSlotsBefore = CombatTargetSlotCache.Count;

        target.WalkableMask = working.WalkableMask;
        target.CostField = working.CostField;
        target.SectorCostFields = working.SectorCostFields;
        target.NeighborTraversalMask = working.NeighborTraversalMask;
        target.GoalProjectionSpatialIndex = working.GoalProjectionSpatialIndex;
        target.FlowTileSectorInputBackings = working.FlowTileSectorInputBackings;
        working.FlowTileSectorInputBackings = null;
        target.IslandIds = working.IslandIds;
        target.IslandCount = working.IslandCount;
        target.MainIslandId = working.MainIslandId;
        target.MainIslandSize = working.MainIslandSize;
        target.Sectors = working.Sectors;
        target.Portals = working.Portals;
        target.PortalsById = working.PortalsById;
        target.L0SearchGraphIndex = working.L0SearchGraphIndex;
        working.L0SearchGraphIndex = null;
        target.NextPortalId = working.NextPortalId;
        target.DeterministicContentHash = working.DeterministicContentHash;
        target.HasDeterministicContentHash = true;
        target.PortalIdsBySignature = working.PortalIdsBySignature;
        target.UsedPortalIds = working.UsedPortalIds;
        PortalHierarchy oldHierarchy = target.Hierarchy;
        target.Hierarchy = working.Hierarchy;

        ReturnWorldArrayIfOwned(oldWalkableMask, target.BaseWalkableMask, target.WalkableMask);
        ReturnWorldArrayIfOwned(oldNeighborTraversalMask, target.BaseNeighborTraversalMask, target.NeighborTraversalMask);
        ReturnWorldArrayIfOwned(oldIslandIds, null, target.IslandIds);
        if (oldInputBackings != null)
        {
            for (int i = 0; i < oldInputBackings.Length; i++)
            {
                FlowTileSectorInputBacking backing = oldInputBackings[i];
                if (backing != null)
                    backing.ReleaseOwner();
            }
        }
        job.CommitSwapTicks = Stopwatch.GetTimestamp() - stepStartTicks;

        stepStartTicks = Stopwatch.GetTimestamp();
        InvalidateCachesForDirtySectors(target, job.CostDirtySectors, oldPortalAccessKeys);
        oldL0SearchGraphIndex?.Dispose();
        if (!ReferenceEquals(oldHierarchy, target.Hierarchy))
            DisposeFlowPathKernelWitnessIndex(oldHierarchy);
        job.CommitCacheInvalidationTicks = Stopwatch.GetTimestamp() - stepStartTicks;

        stepStartTicks = Stopwatch.GetTimestamp();
        CommitPendingSectorPortalAccessEntries(
            target,
            job.PendingPortalAccessEntries,
            entriesAreFinalized: true);
        job.CommitPortalAccessTicks = Stopwatch.GetTimestamp() - stepStartTicks;

        stepStartTicks = Stopwatch.GetTimestamp();
        DeterministicWorldHashRefreshCount++;
        RefreshCommittedWorldSetHash();
        job.CommitWorldHashTicks = Stopwatch.GetTimestamp() - stepStartTicks;

        stepStartTicks = Stopwatch.GetTimestamp();
        InvalidateMovingTargetAnchorsForDirtySectors(target, job.CostDirtySectors);
        InvalidateCombatTargetSlotsForDirtySectors(target, job.CostDirtySectors);
        ClearFixedPortalOwnersForWorld(target.Version);
        int agentsInWorld = 0;
        int pathsCleared = 0;
        int stableGoalsCleared = 0;
        foreach (KeyValuePair<int, AgentRuntimeData> pair in Agents)
        {
            AgentRuntimeData agent = pair.Value
                ?? throw new InvalidOperationException("CommitRuntimeDirtyJob encountered a null agent.");
            if (ResolvePreferredAgentTypeId(agent.AgentTypeId) != target.AgentTypeId)
                continue;
            agentsInWorld++;
            PathHandle handle = agent.NavState.PathHandle;
            bool pathAffected = handle != null
                                && handle.WorldVersion == target.Version
                                && (PathTouchesAnySector(handle, job.CostDirtySectors)
                                    || PathReferencesMissingPortal(target, handle));
            if (pathAffected)
            {
                ClearCommittedNavigationPath(agent);
                pathsCleared++;
            }

            if (pathAffected || StableGoalTouchesAnyDirtySector(target, agent.NavState, job.CostDirtySectors))
            {
                ClearStableGoal(agent);
                stableGoalsCleared++;
            }
        }

        _world = target;
        _navigationTopologyVersion++;
        for (int i = 0; i < NavigationDistancePrewarmRequests.Count; i++)
        {
            NavigationDistancePrewarmRequest request = NavigationDistancePrewarmRequests[i];
            if (request.AgentTypeId != target.AgentTypeId
                || request.ResolvedWorldVersion != target.Version
                || (prewarmAffected && NavigationDistancePrewarmRequestTouchesDirtyWorld(request, target, job.CostDirtySectors)))
                continue;
            request.ResolvedTopologyVersion = _navigationTopologyVersion;
        }
        s_NavigationDistancePrewarmCompleted = false;
        job.CommitAgentInvalidationTicks = Stopwatch.GetTimestamp() - stepStartTicks;
        LogNoStacktrace(
            $"[FlowRuntimeDirtyCommit] worldVersion={target.Version} dirtySectors={job.DirtySectors.Count} costSectors={job.CostDirtySectors.Count} " +
            $"reason={job.Reason} agents={agentsInWorld} pathsCleared={pathsCleared} stableGoalsCleared={stableGoalsCleared} " +
            $"corridorPoliciesRemoved={corridorPoliciesBefore - SectorCorridorPolicies.Count} " +
            $"flowTilesRemoved={flowTilesBefore - FlowTileCache.Count} sharedGoalsRemoved={sharedGoalsBefore - SharedGoalFields.Count} " +
            $"anchorsRemoved={anchorsBefore - MovingTargetAnchors.Count} combatSlotsRemoved={combatSlotsBefore - CombatTargetSlotCache.Count}");
    }

    private static NavigationWorld CloneNavigationWorldForRuntimeDirty(NavigationWorld source, HashSet<int> dirtySectors)
    {
        if (source == null)
            throw new InvalidOperationException("CloneNavigationWorldForRuntimeDirty failed: source is null.");
        if (dirtySectors == null)
            throw new InvalidOperationException("CloneNavigationWorldForRuntimeDirty failed: dirty sectors are null.");

        NavigationWorld clone = new NavigationWorld
        {
            Version = source.Version,
            AgentTypeId = source.AgentTypeId,
            Width = source.Width,
            Height = source.Height,
            CellSize = source.CellSize,
            EncodedCenterClearance = source.EncodedCenterClearance,
            Origin = source.Origin,
            HasImmutableContentHash = source.HasImmutableContentHash,
            ImmutableContentHash = source.ImmutableContentHash,
            BaseWalkableMask = source.BaseWalkableMask,
            BaseNeighborTraversalMask = source.BaseNeighborTraversalMask,
            StaticCollisionVertices = source.StaticCollisionVertices,
            StaticCollisionPathStarts = source.StaticCollisionPathStarts,
            SourceCostField = source.SourceCostField,
            WalkableMask = RentCopiedBoolArray(source.WalkableMask),
            CostField = MaterializeMutableCostField(source),
            SectorCostFields = null,
            CellNavAnchors = source.CellNavAnchors,
            CellNavAnchorsFixedXZ = source.CellNavAnchorsFixedXZ,
            NeighborTraversalMask = RentCopiedByteArray(source.NeighborTraversalMask),
            IslandIds = RentCopiedIntArray(source.IslandIds),
            IslandCount = source.IslandCount,
            MainIslandId = source.MainIslandId,
            MainIslandSize = source.MainIslandSize,
            SectorSizeInCells = source.SectorSizeInCells,
            SectorCountX = source.SectorCountX,
            SectorCountY = source.SectorCountY,
            Sectors = CloneSectors(source.Sectors, dirtySectors),
            Portals = (PortalData[])source.Portals.Clone(),
            PortalsById = new Dictionary<int, PortalData>(source.PortalsById),
            NextPortalId = source.NextPortalId,
            Hierarchy = source.Hierarchy
        };
        clone.CopyAuthorityGridMetadataFrom(source);

        foreach (KeyValuePair<PortalSignature, int> pair in source.PortalIdsBySignature)
            clone.PortalIdsBySignature.Add(pair.Key, pair.Value);
        foreach (int portalId in source.UsedPortalIds)
            clone.UsedPortalIds.Add(portalId);

        return clone;
    }

    private static SectorData[] CloneSectors(SectorData[] source)
    {
        return CloneSectors(source, null);
    }

    private static SectorData[] CloneSectors(SectorData[] source, HashSet<int> dirtySectors)
    {
        if (source == null)
            throw new InvalidOperationException("CloneSectors failed: source is null.");

        SectorData[] clone = new SectorData[source.Length];
        for (int i = 0; i < source.Length; i++)
            clone[i] = CloneSectorData(source[i], dirtySectors);

        return clone;
    }

    private static SectorData CloneSectorData(
        SectorData sector,
        HashSet<int> dirtySectors,
        HashSet<int> portalMutableSectors = null)
    {
        if (sector == null)
            throw new InvalidOperationException("CloneSectorData failed: sector is null.");

        bool sharePortalCollections = portalMutableSectors != null
                                      && !portalMutableSectors.Contains(sector.SectorId);
        bool componentMutable = dirtySectors == null || dirtySectors.Contains(sector.SectorId);
        SectorData copy = sharePortalCollections ? new SectorData(sector) : new SectorData();
        copy.SectorId = sector.SectorId;
        copy.StartX = sector.StartX;
        copy.StartY = sector.StartY;
        copy.Width = sector.Width;
        copy.Height = sector.Height;
        copy.Center = sector.Center;
        copy.DirtyVersion = sector.DirtyVersion;
        copy.IsClearCostField = sector.IsClearCostField;
        copy.IsClearFlowTile = sector.IsClearFlowTile;
        copy.UniformIslandId = sector.UniformIslandId;
        copy.LocalComponentIds = componentMutable || sector.LocalComponentIds == null
            ? new int[sector.Width * sector.Height]
            : sector.LocalComponentIds;
        copy.LocalComponentCount = sector.LocalComponentCount;
        copy.LocalComponentIslandIds = sector.LocalComponentIslandIds != null
            ? (componentMutable ? (int[])sector.LocalComponentIslandIds.Clone() : sector.LocalComponentIslandIds)
            : new int[sector.LocalComponentCount + 1];
        copy.LocalComponentSizes = sector.LocalComponentSizes != null
            ? (componentMutable ? (int[])sector.LocalComponentSizes.Clone() : sector.LocalComponentSizes)
            : new int[sector.LocalComponentCount + 1];
        copy.LocalComponentMinCellIndices = sector.LocalComponentMinCellIndices != null
            ? (componentMutable ? (int[])sector.LocalComponentMinCellIndices.Clone() : sector.LocalComponentMinCellIndices)
            : new int[sector.LocalComponentCount + 1];
        copy.HasDeterministicContentHash = sector.HasDeterministicContentHash;
        copy.DeterministicContentHash = sector.DeterministicContentHash;
        if (!sharePortalCollections)
        {
            copy.PortalIds.AddRange(sector.PortalIds);
            copy.PortalTransitions.AddRange(sector.PortalTransitions);
            RebuildIncomingPortalTransitionIndex(copy);
        }
        return copy;
    }

    private static void ResetSectorWalkableFromBase(NavigationWorld world, SectorData sector)
    {
        for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
        {
            int rowStart = y * world.Width;
            for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
            {
                int index = rowStart + x;
                world.WalkableMask[index] = world.BaseWalkableMask[index];
            }
        }
    }

    private static void BlockCellsByCircleInSectorsFixed(
        NavigationWorld world,
        HashSet<int> sectorIds,
        FixVector2 center,
        Fix64 radius)
    {
        if (world == null)
            throw new InvalidOperationException("BlockCellsByCircleInSectorsFixed failed: world is null.");
        if (sectorIds == null)
            throw new ArgumentNullException(nameof(sectorIds));

        int rawMinX = NavigationGridFixedMath.WorldToGridCell(center.x - radius, world.OriginXGridRaw, world.CellSizeGridRaw);
        int rawMaxX = NavigationGridFixedMath.WorldToGridCell(center.x + radius, world.OriginXGridRaw, world.CellSizeGridRaw);
        int rawMinY = NavigationGridFixedMath.WorldToGridCell(center.y - radius, world.OriginZGridRaw, world.CellSizeGridRaw);
        int rawMaxY = NavigationGridFixedMath.WorldToGridCell(center.y + radius, world.OriginZGridRaw, world.CellSizeGridRaw);
        Fix64 cellSize = world.CellSizeFixed;
        Fix64 blockRadius = radius + cellSize * Fix64.FromRaw(1844);
        Fix64 blockRadiusSquared = blockRadius * blockRadius;

        foreach (int sectorId in sectorIds)
        {
            SectorData sector = world.Sectors[sectorId];
            int minX = Mathf.Max(rawMinX, sector.StartX);
            int maxX = Mathf.Min(rawMaxX, sector.StartX + sector.Width - 1);
            int minY = Mathf.Max(rawMinY, sector.StartY);
            int maxY = Mathf.Min(rawMaxY, sector.StartY + sector.Height - 1);
            if (minX > maxX || minY > maxY)
                continue;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    FixVector2 cellCenter = world.GridToWorldGeometricCenterFixed(x, y);
                    if (FixVector2.SqrMagnitude(cellCenter - center) <= blockRadiusSquared)
                        world.WalkableMask[x + y * world.Width] = false;
                }
            }
        }
    }

    private static void BlockCellsByBoxInSectorsFixed(
        NavigationWorld world,
        HashSet<int> sectorIds,
        FixVector2 center,
        FixVector2 halfExtents)
    {
        if (world == null)
            throw new InvalidOperationException("BlockCellsByBoxInSectorsFixed failed: world is null.");
        if (sectorIds == null)
            throw new ArgumentNullException(nameof(sectorIds));

        int rawMinX = NavigationGridFixedMath.WorldToGridCell(center.x - halfExtents.x, world.OriginXGridRaw, world.CellSizeGridRaw);
        int rawMaxX = NavigationGridFixedMath.WorldToGridCell(center.x + halfExtents.x, world.OriginXGridRaw, world.CellSizeGridRaw);
        int rawMinY = NavigationGridFixedMath.WorldToGridCell(center.y - halfExtents.y, world.OriginZGridRaw, world.CellSizeGridRaw);
        int rawMaxY = NavigationGridFixedMath.WorldToGridCell(center.y + halfExtents.y, world.OriginZGridRaw, world.CellSizeGridRaw);
        foreach (int sectorId in sectorIds)
        {
            SectorData sector = world.Sectors[sectorId];
            int minX = Mathf.Max(rawMinX, sector.StartX);
            int maxX = Mathf.Min(rawMaxX, sector.StartX + sector.Width - 1);
            int minY = Mathf.Max(rawMinY, sector.StartY);
            int maxY = Mathf.Min(rawMaxY, sector.StartY + sector.Height - 1);
            if (minX > maxX || minY > maxY)
                continue;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    FixVector2 cellCenter = world.GridToWorldGeometricCenterFixed(x, y);
                    FixVector2 delta = cellCenter - center;
                    if (Fix64.Abs(delta.x) <= halfExtents.x && Fix64.Abs(delta.y) <= halfExtents.y)
                        world.WalkableMask[x + y * world.Width] = false;
                }
            }
        }
    }

    private static void RebuildNeighborTraversalMaskForSectors(NavigationWorld world, HashSet<int> sectorIds)
    {
        if (world == null)
            throw new InvalidOperationException("RebuildNeighborTraversalMaskForSectors failed: world is null.");

        foreach (int sectorId in sectorIds)
        {
            SectorData sector = world.Sectors[sectorId];
            for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
            {
                for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
                    RebuildNeighborTraversalMaskCell(world, x, y);
            }
        }
    }

    private static void RebuildNeighborTraversalMaskCell(NavigationWorld world, int x, int y)
    {
        if (x < 0 || x >= world.Width || y < 0 || y >= world.Height)
            return;

        int fromIndex = world.GetIndex(x, y);
        if (!world.IsWalkable(x, y))
        {
            world.NeighborTraversalMask[fromIndex] = 0;
            return;
        }

        byte mask = 0;
        byte baseMask = world.BaseNeighborTraversalMask != null && world.BaseNeighborTraversalMask.Length == world.Width * world.Height
            ? world.BaseNeighborTraversalMask[fromIndex]
            : byte.MaxValue;
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int toX = x + NeighborOffsetX[i];
            int toY = y + NeighborOffsetY[i];
            if ((baseMask & (1 << i)) == 0)
                continue;
            if (!world.IsWalkable(toX, toY))
                continue;

            mask |= (byte)(1 << i);
        }

        world.NeighborTraversalMask[fromIndex] = mask;
    }

    private static void SymmetrizeNeighborTraversalMaskForSectors(NavigationWorld world, HashSet<int> sectorIds)
    {
        if (world == null)
            throw new InvalidOperationException("SymmetrizeNeighborTraversalMaskForSectors failed: world is null.");
        if (sectorIds == null || sectorIds.Count == 0)
            return;
        if (world.NeighborTraversalMask == null || world.NeighborTraversalMask.Length != world.Width * world.Height)
            throw new InvalidOperationException("SymmetrizeNeighborTraversalMaskForSectors failed: neighbor traversal mask is invalid.");

        ResolveSectorBounds(world, sectorIds, out int startX, out int startY, out int endX, out int endY);
        startX = Mathf.Max(0, startX - 1);
        startY = Mathf.Max(0, startY - 1);
        endX = Mathf.Min(world.Width - 1, endX + 1);
        endY = Mathf.Min(world.Height - 1, endY + 1);

        for (int y = startY; y <= endY; y++)
        {
            for (int x = startX; x <= endX; x++)
                SymmetrizeNeighborTraversalMaskCell(world, x, y, "SymmetrizeNeighborTraversalMaskForSectors");
        }
    }

    private static void SymmetrizeNeighborTraversalMaskCell(NavigationWorld world, int x, int y, string caller)
    {
        int fromIndex = world.GetIndex(x, y);
        if (!world.IsWalkable(x, y))
        {
            world.NeighborTraversalMask[fromIndex] = 0;
            return;
        }

        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int toX = x + NeighborOffsetX[i];
            int toY = y + NeighborOffsetY[i];
            if (!world.IsWalkable(toX, toY))
            {
                world.NeighborTraversalMask[fromIndex] &= (byte)~(1 << i);
                continue;
            }

            int oppositeIndex = ResolveNeighborOffsetIndex(-NeighborOffsetX[i], -NeighborOffsetY[i]);
            if (oppositeIndex < 0)
                throw new InvalidOperationException($"{caller} failed: missing opposite offset for {NeighborOffsetX[i]},{NeighborOffsetY[i]}.");

            int toIndex = world.GetIndex(toX, toY);
            bool forward = (world.NeighborTraversalMask[fromIndex] & (1 << i)) != 0;
            bool backward = (world.NeighborTraversalMask[toIndex] & (1 << oppositeIndex)) != 0;
            if (!forward && !backward)
                continue;

            world.NeighborTraversalMask[fromIndex] |= (byte)(1 << i);
            world.NeighborTraversalMask[toIndex] |= (byte)(1 << oppositeIndex);
        }
    }

    private static int PruneIsolatedWalkableCells(NavigationWorld world, string reason)
    {
        if (world == null)
            throw new InvalidOperationException("PruneIsolatedWalkableCells failed: world is null.");
        if (world.WalkableMask == null || world.WalkableMask.Length != world.Width * world.Height)
            throw new InvalidOperationException("PruneIsolatedWalkableCells failed: walkable mask is invalid.");
        if (world.NeighborTraversalMask == null || world.NeighborTraversalMask.Length != world.Width * world.Height)
            throw new InvalidOperationException("PruneIsolatedWalkableCells failed: neighbor traversal mask is invalid.");

        int pruned = 0;
        for (int i = 0; i < world.WalkableMask.Length; i++)
        {
            if (!world.WalkableMask[i] || world.NeighborTraversalMask[i] != 0)
                continue;

            world.WalkableMask[i] = false;
            if (world.CostField != null && world.CostField.Length == world.Width * world.Height)
                world.CostField[i] = byte.MaxValue;
            pruned++;
        }

        LogIsolatedWalkablePrune(world, reason, pruned);
        return pruned;
    }

    private static int PruneIsolatedWalkableCellsForSectors(NavigationWorld world, HashSet<int> sectorIds, string reason)
    {
        if (world == null)
            throw new InvalidOperationException("PruneIsolatedWalkableCellsForSectors failed: world is null.");
        if (world.WalkableMask == null || world.WalkableMask.Length != world.Width * world.Height)
            throw new InvalidOperationException("PruneIsolatedWalkableCellsForSectors failed: walkable mask is invalid.");
        if (world.NeighborTraversalMask == null || world.NeighborTraversalMask.Length != world.Width * world.Height)
            throw new InvalidOperationException("PruneIsolatedWalkableCellsForSectors failed: neighbor traversal mask is invalid.");
        if (sectorIds == null || sectorIds.Count == 0)
            return 0;

        ResolveSectorBounds(world, sectorIds, out int startX, out int startY, out int endX, out int endY);
        startX = Mathf.Max(0, startX - 1);
        startY = Mathf.Max(0, startY - 1);
        endX = Mathf.Min(world.Width - 1, endX + 1);
        endY = Mathf.Min(world.Height - 1, endY + 1);

        int pruned = 0;
        for (int y = startY; y <= endY; y++)
        {
            for (int x = startX; x <= endX; x++)
            {
                int index = world.GetIndex(x, y);
                if (!world.WalkableMask[index] || world.NeighborTraversalMask[index] != 0)
                    continue;

                world.WalkableMask[index] = false;
                if (world.CostField != null && world.CostField.Length == world.Width * world.Height)
                    world.CostField[index] = byte.MaxValue;
                pruned++;
            }
        }

        LogIsolatedWalkablePrune(world, reason, pruned);
        return pruned;
    }

    private static void LogIsolatedWalkablePrune(NavigationWorld world, string reason, int pruned)
    {
        if (pruned <= 0)
            return;

        LogNoStacktrace(
            $"[FlowIsolatedWalkablePrune] reason={reason} pruned={pruned} " +
            $"worldVersion={world.Version} size={world.Width}x{world.Height} agentType={world.AgentTypeId}");
    }

    private static List<SectorPortalAccessKey> CreatePortalAccessInvalidationKeys(
        NavigationWorld world,
        IReadOnlyList<int> sectorIds)
    {
        if (world?.Sectors == null)
            throw new InvalidOperationException("CreatePortalAccessInvalidationKeys failed: world is unavailable.");
        if (sectorIds == null)
            throw new InvalidOperationException("CreatePortalAccessInvalidationKeys failed: sector ids are null.");

        var result = new List<SectorPortalAccessKey>();
        int previousSectorId = -1;
        for (int i = 0; i < sectorIds.Count; i++)
        {
            int sectorId = sectorIds[i];
            if (sectorId <= previousSectorId || sectorId < 0 || sectorId >= world.Sectors.Length)
            {
                throw new InvalidOperationException(
                    $"CreatePortalAccessInvalidationKeys failed: sector ids must be sorted, unique and in range previous={previousSectorId} current={sectorId}.");
            }
            previousSectorId = sectorId;

            SectorData sector = world.Sectors[sectorId]
                                ?? throw new InvalidOperationException(
                                    $"CreatePortalAccessInvalidationKeys failed: sector is null id={sectorId}.");
            for (int portalIndex = 0; portalIndex < sector.PortalIds.Count; portalIndex++)
            {
                int portalId = sector.PortalIds[portalIndex];
                var key = new SectorPortalAccessKey(world.Version, sectorId, portalId, sector.DirtyVersion);
                if (!SectorPortalAccessCache.TryGetValue(key, out SectorPortalAccessEntry entry)
                    || !IsValidSectorPortalAccessEntry(entry))
                {
                    throw new InvalidOperationException(
                        $"CreatePortalAccessInvalidationKeys failed: committed portal access is missing sector={sectorId} portal={portalId} dirty={sector.DirtyVersion}.");
                }
                result.Add(key);
            }
        }

        return result;
    }

    private static void InvalidateCachesForDirtySectors(
        NavigationWorld world,
        HashSet<int> dirtySectors,
        IReadOnlyList<SectorPortalAccessKey> portalAccessKeysToRemove)
    {
        if (world == null)
            throw new InvalidOperationException("InvalidateCachesForDirtySectors failed: world is null.");
        if (dirtySectors == null || dirtySectors.Count == 0)
            return;
        if (portalAccessKeysToRemove == null)
            throw new InvalidOperationException("InvalidateCachesForDirtySectors failed: portal access keys are null.");

        InvalidatePendingNavigationPathRequestsForDirtySectors(world, dirtySectors);
        InvalidateSectorCorridorPoliciesForDirtySectors(world, dirtySectors);

        List<FlowTileCacheKey> tileKeysToRemove = null;
        foreach (KeyValuePair<FlowTileCacheKey, FlowTileCacheEntry> pair in FlowTileCache)
        {
            if (pair.Key.WorldVersion != world.Version || !dirtySectors.Contains(pair.Key.SectorId))
                continue;

            tileKeysToRemove ??= new List<FlowTileCacheKey>();
            tileKeysToRemove.Add(pair.Key);
        }

        if (tileKeysToRemove != null)
        {
            for (int i = 0; i < tileKeysToRemove.Count; i++)
                RemoveFlowTileCacheEntry(tileKeysToRemove[i]);
        }

        List<SectorPathCacheKey> pathKeysToRemove = null;
        foreach (KeyValuePair<SectorPathCacheKey, SectorPathCacheEntry> pair in SectorPathCache)
        {
            if (pair.Key.WorldVersion != world.Version)
                continue;

            if (!dirtySectors.Contains(pair.Key.StartSectorId)
                && !dirtySectors.Contains(pair.Key.GoalSectorId)
                && !SectorPathTouchesAnySector(pair.Value, dirtySectors)
                && !SectorPathReferencesMissingPortal(world, pair.Value))
            {
                continue;
            }

            pathKeysToRemove ??= new List<SectorPathCacheKey>();
            pathKeysToRemove.Add(pair.Key);
        }

        if (pathKeysToRemove != null)
        {
            for (int i = 0; i < pathKeysToRemove.Count; i++)
                RemoveSectorPathCacheEntry(pathKeysToRemove[i]);
        }

        for (int i = 0; i < portalAccessKeysToRemove.Count; i++)
            RemoveSectorPortalAccessCacheEntry(portalAccessKeysToRemove[i]);

        List<SharedGoalFieldKey> sharedFieldKeysToRemove = null;
        foreach (KeyValuePair<SharedGoalFieldKey, SharedGoalField> pair in SharedGoalFields)
        {
            if (pair.Key.WorldVersion != world.Version)
                continue;

            if (!dirtySectors.Contains(pair.Key.GoalSectorId)
                && !SharedGoalFieldTouchesAnySector(pair.Value, dirtySectors)
                && !SharedGoalFieldReferencesMissingPortal(world, pair.Value))
            {
                continue;
            }

            sharedFieldKeysToRemove ??= new List<SharedGoalFieldKey>();
            sharedFieldKeysToRemove.Add(pair.Key);
        }

        if (sharedFieldKeysToRemove != null)
        {
            for (int i = 0; i < sharedFieldKeysToRemove.Count; i++)
                RemoveSharedGoalFieldCacheEntry(sharedFieldKeysToRemove[i]);
        }

        InvalidatePendingBuildJobsForDirtySectors(world, dirtySectors);
    }

    private static void InvalidateSectorCorridorPoliciesForDirtySectors(
        NavigationWorld world,
        HashSet<int> dirtySectors)
    {
        if (world == null)
            throw new InvalidOperationException("InvalidateSectorCorridorPoliciesForDirtySectors failed: world is null.");
        if (dirtySectors == null || dirtySectors.Count == 0)
            return;

        List<SectorCorridorPolicyKey> keysToRemove = null;
        foreach (KeyValuePair<SectorCorridorPolicyKey, SectorCorridorPolicy> pair in SectorCorridorPolicies)
        {
            SectorCorridorPolicyKey key = pair.Key;
            if (key.WorldVersion != world.Version)
                continue;

            SectorCorridorPolicy policy = pair.Value
                ?? throw new InvalidOperationException("Sector corridor policy cache contains a null policy during dirty invalidation.");
            bool affected = dirtySectors.Contains(key.GoalSectorId);
            if (!affected)
            {
                foreach (PortalHierarchyReversePolicy hierarchyPolicy in policy.HierarchyPolicies.Values)
                {
                    if (hierarchyPolicy == null)
                        throw new InvalidOperationException("Sector corridor policy contains a null hierarchy policy during dirty invalidation.");
                    foreach (PortalHierarchyL0Witness witness in hierarchyPolicy.L0WitnessesByStartNode.Values)
                    {
                        if (witness?.SectorIds == null)
                            throw new InvalidOperationException("Sector corridor policy witness is missing sector dependencies.");
                        for (int i = 0; i < witness.SectorIds.Length; i++)
                        {
                            if (dirtySectors.Contains(witness.SectorIds[i]))
                            {
                                affected = true;
                                break;
                            }
                        }
                        if (affected)
                            break;
                    }
                    if (affected)
                        break;
                }
            }

            if (affected)
            {
                keysToRemove ??= new List<SectorCorridorPolicyKey>();
                keysToRemove.Add(key);
            }
        }

        if (keysToRemove == null)
            return;
        for (int i = 0; i < keysToRemove.Count; i++)
            RemoveSectorCorridorPolicy(keysToRemove[i]);
    }

    private static bool NavigationDistancePrewarmTouchesDirtyWorld(
        NavigationWorld world,
        HashSet<int> dirtySectors)
    {
        if (world == null)
            throw new InvalidOperationException("NavigationDistancePrewarmTouchesDirtyWorld failed: world is null.");
        if (dirtySectors == null || dirtySectors.Count == 0)
            return false;
        for (int i = 0; i < NavigationDistancePrewarmRequests.Count; i++)
        {
            NavigationDistancePrewarmRequest request = NavigationDistancePrewarmRequests[i];
            if (request.AgentTypeId == world.AgentTypeId
                && request.ResolvedWorldVersion == world.Version
                && NavigationDistancePrewarmRequestTouchesDirtyWorld(request, world, dirtySectors))
                return true;
        }
        return false;
    }

    private static bool NavigationDistancePrewarmRequestTouchesDirtyWorld(
        NavigationDistancePrewarmRequest request,
        NavigationWorld world,
        HashSet<int> dirtySectors)
    {
        if (request == null)
            throw new InvalidOperationException("NavigationDistancePrewarmRequestTouchesDirtyWorld failed: request is null.");
        if (world == null)
            throw new InvalidOperationException("NavigationDistancePrewarmRequestTouchesDirtyWorld failed: world is null.");
        if (dirtySectors == null || dirtySectors.Count == 0)
            return false;
        if (dirtySectors.Contains(request.StartSectorId) || dirtySectors.Contains(request.GoalSectorId))
            return true;

        if (request.GoalSectorId < 0 || request.GoalX < 0 || request.GoalY < 0)
            return false;
        SharedGoalFieldKey key = new SharedGoalFieldKey(
            world.Version,
            request.AgentTypeId,
            request.GoalSectorId,
            world.GetIndex(request.GoalX, request.GoalY),
            world.Sectors[request.GoalSectorId].DirtyVersion);
        return SharedGoalFields.TryGetValue(key, out SharedGoalField field)
               && field != null
               && SharedGoalFieldTouchesAnySector(field, dirtySectors);
    }

    private static void InvalidatePendingBuildJobsForDirtySectors(NavigationWorld world, HashSet<int> dirtySectors)
    {
        if (world == null)
            throw new InvalidOperationException("InvalidatePendingBuildJobsForDirtySectors failed: world is null.");
        if (dirtySectors == null || dirtySectors.Count == 0)
            return;

        InvalidatePendingFlowTileBuildJobsForDirtySectors(world, dirtySectors);
        InvalidatePendingSharedGoalFieldBuildJobsForDirtySectors(world, dirtySectors);
    }

    private static void InvalidatePendingFlowTileBuildJobsForDirtySectors(NavigationWorld world, HashSet<int> dirtySectors)
    {
        LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First;
        while (node != null)
        {
            LinkedListNode<FlowTileBuildJob> next = node.Next;
            FlowTileBuildJob job = node.Value;
            if (job != null
                && job.BuildKey.CacheKey.WorldVersion == world.Version
                && (FlowTileBuildJobTouchesAnySector(job, dirtySectors) || FlowTileBuildJobReferencesMissingPortal(world, job)))
            {
                ReleasePendingFlowTileBuildJobPayloads(job);
                FlowTileBuildQueue.Remove(node);
                PendingFlowTileBuildJobs.Remove(job.BuildKey.CacheKey);
            }

            node = next;
        }
    }

    private static bool FlowTileBuildJobTouchesAnySector(FlowTileBuildJob job, HashSet<int> dirtySectors)
    {
        if (job == null || dirtySectors == null || dirtySectors.Count == 0)
            return false;

        if (dirtySectors.Contains(job.BuildKey.CacheKey.SectorId))
            return true;

        return PathTouchesAnySector(job.HandleSnapshot, dirtySectors);
    }

    private static bool FlowTileBuildJobReferencesMissingPortal(NavigationWorld world, FlowTileBuildJob job)
    {
        if (world == null)
            throw new InvalidOperationException("FlowTileBuildJobReferencesMissingPortal failed: world is null.");
        if (job == null)
            return false;

        FlowTileCacheKey key = job.BuildKey.CacheKey;
        if (key.GoalKind == TileGoalKind.Portal && !TryGetPortalById(world, key.GoalId, out _))
            return true;

        return PathReferencesMissingPortal(world, job.HandleSnapshot);
    }

    private static void InvalidatePendingSharedGoalFieldBuildJobsForDirtySectors(NavigationWorld world, HashSet<int> dirtySectors)
    {
        LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First;
        while (node != null)
        {
            LinkedListNode<SharedGoalFieldBuildJob> next = node.Next;
            SharedGoalFieldBuildJob job = node.Value;
            if (job != null
                && job.Key.WorldVersion == world.Version
                && (SharedGoalFieldBuildJobTouchesAnySector(job, dirtySectors) || SharedGoalFieldBuildJobReferencesMissingPortal(world, job)))
            {
                SharedGoalFieldBuildQueue.Remove(node);
                PendingSharedGoalFieldBuildJobs.Remove(job.Key);
            }

            node = next;
        }
    }

    private static bool SharedGoalFieldBuildJobTouchesAnySector(SharedGoalFieldBuildJob job, HashSet<int> dirtySectors)
    {
        if (job == null || dirtySectors == null || dirtySectors.Count == 0)
            return false;

        if (dirtySectors.Contains(job.GoalSectorId))
            return true;

        return job.Field != null && SharedGoalFieldTouchesAnySector(job.Field, dirtySectors);
    }

    private static bool SharedGoalFieldBuildJobReferencesMissingPortal(NavigationWorld world, SharedGoalFieldBuildJob job)
    {
        return job?.Field != null && SharedGoalFieldReferencesMissingPortal(world, job.Field);
    }

    private static bool SharedGoalFieldTouchesAnySector(SharedGoalField field, HashSet<int> dirtySectors)
    {
        if (field == null || dirtySectors == null || dirtySectors.Count == 0)
            return false;

        foreach (int node in field.NodeCosts.Keys)
        {
            DecodePortalNode(node, out int sectorId, out _);
            if (dirtySectors.Contains(sectorId))
                return true;
        }

        foreach (KeyValuePair<int, int> pair in field.NextNodeTowardGoal)
        {
            DecodePortalNode(pair.Key, out int fromSectorId, out _);
            DecodePortalNode(pair.Value, out int toSectorId, out _);
            if (dirtySectors.Contains(fromSectorId) || dirtySectors.Contains(toSectorId))
                return true;
        }

        return false;
    }

    private static bool SharedGoalFieldReferencesMissingPortal(NavigationWorld world, SharedGoalField field)
    {
        if (world == null)
            throw new InvalidOperationException("SharedGoalFieldReferencesMissingPortal failed: world is null.");
        if (field == null)
            return false;

        foreach (int node in field.NodeCosts.Keys)
        {
            DecodePortalNode(node, out _, out int portalId);
            if (!TryGetPortalById(world, portalId, out _))
                return true;
        }

        foreach (KeyValuePair<int, int> pair in field.NextNodeTowardGoal)
        {
            DecodePortalNode(pair.Key, out _, out int fromPortalId);
            DecodePortalNode(pair.Value, out _, out int toPortalId);
            if (!TryGetPortalById(world, fromPortalId, out _) || !TryGetPortalById(world, toPortalId, out _))
                return true;
        }

        return false;
    }

    private static bool SectorPathTouchesAnySector(SectorPathCacheEntry entry, HashSet<int> dirtySectors)
    {
        if (entry == null || entry.SectorIds == null || dirtySectors == null || dirtySectors.Count == 0)
            return false;

        for (int i = 0; i < entry.SectorIds.Length; i++)
        {
            if (dirtySectors.Contains(entry.SectorIds[i]))
                return true;
        }

        return false;
    }

    private static bool SectorPathReferencesMissingPortal(NavigationWorld world, SectorPathCacheEntry entry)
    {
        if (world == null)
            throw new InvalidOperationException("SectorPathReferencesMissingPortal failed: world is null.");
        if (entry == null || entry.PortalIds == null)
            return false;

        for (int i = 0; i < entry.PortalIds.Length; i++)
        {
            if (!TryGetPortalById(world, entry.PortalIds[i], out _))
                return true;
        }

        return false;
    }

    private static bool StableGoalTouchesAnyDirtySector(
        NavigationWorld world,
        AgentNavState nav,
        HashSet<int> dirtySectors)
    {
        if (world == null)
            throw new InvalidOperationException("StableGoalTouchesAnyDirtySector failed: world is null.");
        if (nav == null || nav.StableGoalTargetId == int.MinValue || dirtySectors == null || dirtySectors.Count == 0)
            return false;
        if (nav.CurrentSectorId >= 0 && dirtySectors.Contains(nav.CurrentSectorId))
            return true;
        if (nav.StableGoalX < 0 || nav.StableGoalY < 0)
            throw new InvalidOperationException("StableGoalTouchesAnyDirtySector failed: stable target is set without a stable goal cell.");
        if (!world.TryGetSectorId(nav.StableGoalX, nav.StableGoalY, out int goalSectorId))
            throw new InvalidOperationException(
                $"StableGoalTouchesAnyDirtySector failed: stable goal is outside world cell=({nav.StableGoalX},{nav.StableGoalY}).");
        return dirtySectors.Contains(goalSectorId);
    }

    private static void InvalidateMovingTargetAnchorsForDirtySectors(
        NavigationWorld world,
        HashSet<int> dirtySectors)
    {
        if (world == null)
            throw new InvalidOperationException("InvalidateMovingTargetAnchorsForDirtySectors failed: world is null.");
        if (dirtySectors == null || dirtySectors.Count == 0 || MovingTargetAnchors.Count == 0)
            return;

        List<MovingTargetAnchorKey> keysToRemove = null;
        foreach (KeyValuePair<MovingTargetAnchorKey, MovingTargetAnchor> pair in MovingTargetAnchors)
        {
            MovingTargetAnchor anchor = pair.Value
                ?? throw new InvalidOperationException("Moving target anchor cache contains a null anchor during dirty invalidation.");
            if (anchor.ActiveWorldVersion != world.Version)
                continue;

            bool affected = dirtySectors.Contains(anchor.ActiveGoalSectorId);
            if (!affected && anchor.HasPinnedSectorCorridorPolicy
                && !SectorCorridorPolicies.ContainsKey(anchor.PinnedSectorCorridorPolicyKey))
                affected = true;
            if (affected)
            {
                keysToRemove ??= new List<MovingTargetAnchorKey>();
                keysToRemove.Add(pair.Key);
            }
        }

        if (keysToRemove == null)
            return;
        for (int i = 0; i < keysToRemove.Count; i++)
            MovingTargetAnchors.Remove(keysToRemove[i]);
    }

    private static void InvalidateCombatTargetSlotsForDirtySectors(
        NavigationWorld world,
        HashSet<int> dirtySectors)
    {
        if (world == null)
            throw new InvalidOperationException("InvalidateCombatTargetSlotsForDirtySectors failed: world is null.");
        if (dirtySectors == null || dirtySectors.Count == 0 || CombatTargetSlotCache.Count == 0)
            return;

        List<CombatTargetSlotKey> keysToRemove = null;
        foreach (CombatTargetSlotKey key in CombatTargetSlotCache.Keys)
        {
            if (key.WorldVersion != world.Version)
                continue;
            int targetX = key.TargetCellIndex % world.Width;
            int targetY = key.TargetCellIndex / world.Width;
            if (!world.TryGetSectorId(targetX, targetY, out int targetSectorId))
                throw new InvalidOperationException(
                    $"InvalidateCombatTargetSlotsForDirtySectors failed: target cell outside world index={key.TargetCellIndex}.");
            if (!dirtySectors.Contains(targetSectorId))
                continue;
            keysToRemove ??= new List<CombatTargetSlotKey>();
            keysToRemove.Add(key);
        }

        if (keysToRemove == null)
            return;
        for (int i = 0; i < keysToRemove.Count; i++)
            CombatTargetSlotCache.Remove(keysToRemove[i]);
    }

    private static bool PathTouchesAnySector(PathHandle handle, HashSet<int> dirtySectors)
    {
        if (handle == null || handle.SectorIds == null || dirtySectors == null || dirtySectors.Count == 0)
            return false;

        for (int i = 0; i < handle.SectorIds.Length; i++)
        {
            if (dirtySectors.Contains(handle.SectorIds[i]))
                return true;
        }

        return false;
    }

    private static bool PathReferencesMissingPortal(PathHandle handle)
    {
        return PathReferencesMissingPortal(_world, handle);
    }

    private static bool PathReferencesMissingPortal(NavigationWorld world, PathHandle handle)
    {
        if (world == null)
            throw new InvalidOperationException("PathReferencesMissingPortal failed: world is null.");
        if (handle == null || handle.PortalIds == null)
            return false;

        for (int i = 0; i < handle.PortalIds.Length; i++)
        {
            if (!TryGetPortalById(world, handle.PortalIds[i], out _))
                return true;
        }

        return false;
    }

    private static void CollectDirtySectorsFixed(
        NavigationWorld world,
        FixVector2 boundsMinimum,
        FixVector2 boundsMaximum,
        HashSet<int> dirtySectors,
        bool includeNeighbors)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (dirtySectors == null)
            throw new ArgumentNullException(nameof(dirtySectors));
        if (boundsMinimum.x > boundsMaximum.x || boundsMinimum.y > boundsMaximum.y)
            throw new ArgumentOutOfRangeException(nameof(boundsMinimum), "Dirty-sector bounds minimum exceeds maximum.");

        FixVector2 worldMinimum = world.OriginFixed;
        Fix64 worldWidth = world.CellSizeFixed * world.Width;
        Fix64 worldHeight = world.CellSizeFixed * world.Height;
        FixVector2 worldMaximum = new FixVector2(worldMinimum.x + worldWidth, worldMinimum.y + worldHeight);
        if (boundsMaximum.x < worldMinimum.x
            || boundsMinimum.x > worldMaximum.x
            || boundsMaximum.y < worldMinimum.y
            || boundsMinimum.y > worldMaximum.y)
        {
            return;
        }

        world.WorldToGridFixed(boundsMinimum, out int rawMinCellX, out int rawMinCellY);
        world.WorldToGridFixed(boundsMaximum, out int rawMaxCellX, out int rawMaxCellY);
        int minCellX = Mathf.Clamp(rawMinCellX, 0, world.Width - 1);
        int maxCellX = Mathf.Clamp(rawMaxCellX, 0, world.Width - 1);
        int minCellY = Mathf.Clamp(rawMinCellY, 0, world.Height - 1);
        int maxCellY = Mathf.Clamp(rawMaxCellY, 0, world.Height - 1);

        int minSectorX = Mathf.Clamp(minCellX / world.SectorSizeInCells, 0, world.SectorCountX - 1);
        int maxSectorX = Mathf.Clamp(maxCellX / world.SectorSizeInCells, 0, world.SectorCountX - 1);
        int minSectorY = Mathf.Clamp(minCellY / world.SectorSizeInCells, 0, world.SectorCountY - 1);
        int maxSectorY = Mathf.Clamp(maxCellY / world.SectorSizeInCells, 0, world.SectorCountY - 1);

        int padding = includeNeighbors ? 1 : 0;
        for (int sectorY = Mathf.Max(0, minSectorY - padding); sectorY <= Mathf.Min(world.SectorCountY - 1, maxSectorY + padding); sectorY++)
        {
            for (int sectorX = Mathf.Max(0, minSectorX - padding); sectorX <= Mathf.Min(world.SectorCountX - 1, maxSectorX + padding); sectorX++)
            {
                dirtySectors.Add(sectorY * world.SectorCountX + sectorX);
            }
        }
    }

}
