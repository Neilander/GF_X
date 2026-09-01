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
    private static bool TryEnsureWorldBuilt(int preferredAgentTypeId, bool allowSynchronousBuild = false)
    {
        int agentTypeId = ResolvePreferredAgentTypeId(preferredAgentTypeId);
        WorldRuntimeState state = GetOrCreateWorldState(agentTypeId);
        _activeWorldState = state;
        _world = state.World;

        if (!state.IsDirty && state.World != null)
        {
            _world = state.World;
            return true;
        }

        if (!EnsureWorldBuildJob(state, agentTypeId))
            return false;

        if (!allowSynchronousBuild && !CanSynchronouslyBuildWorldForEditorTest(agentTypeId))
            return false;

        ProcessWorldBuildJob(state, long.MaxValue, forceComplete: true);
        if (state.IsDirty || state.World == null || state.BuildJob != null)
            throw new InvalidOperationException($"TryEnsureWorldBuilt failed: world build did not complete agentType={agentTypeId}.");

        _world = state.World;
        return true;
    }

    private static bool HasPendingRuntimeDirty(WorldRuntimeState state)
    {
        return state != null
               && (state.RuntimeDirtyJob != null || state.DirtyRuntimeObstacleSectors.Count > 0);
    }

    private static NavigationWorld ResolveReachabilityQueryWorld(WorldRuntimeState state)
    {
        if (state == null)
            throw new InvalidOperationException("ResolveReachabilityQueryWorld failed: state is null.");
        if (state.World == null)
            throw new InvalidOperationException("ResolveReachabilityQueryWorld failed: committed world is null.");

        if (state.RuntimeDirtyJob != null)
            return BuildRuntimeDirtyPreviewWorld(state.RuntimeDirtyJob);

        if (state.DirtyRuntimeObstacleSectors.Count == 0)
            return state.World;

        return BuildRuntimeDirtyPreviewWorld(state, state.DirtyRuntimeObstacleSectors);
    }

    private static NavigationWorld BuildRuntimeDirtyPreviewWorld(RuntimeDirtyRebuildJob sourceJob)
    {
        if (sourceJob == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: source job is null.");
        if (sourceJob.TargetWorld == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: source target world is null.");
        if (sourceJob.DirtySectors == null || sourceJob.DirtySectors.Count == 0)
            return sourceJob.TargetWorld;

        return BuildRuntimeDirtyPreviewWorld(
            sourceJob.TargetWorld,
            sourceJob.DirtySectors,
            sourceJob.CostDirtySectors,
            sourceJob.CircleObstacles,
            sourceJob.BoxObstacles,
            sourceJob.CostStamps);
    }

    private static NavigationWorld BuildRuntimeDirtyPreviewWorld(WorldRuntimeState state, HashSet<int> dirtySectors)
    {
        if (state == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: state is null.");
        if (state.World == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: state world is null.");
        if (dirtySectors == null || dirtySectors.Count == 0)
            return state.World;

        HashSet<int> costDirtySectors = ExpandDirtySectorsByCellRadius(
            state.World,
            dirtySectors,
            ResolveWallCostPaddingCells(state.World, 0));
        return BuildRuntimeDirtyPreviewWorld(
            state.World,
            dirtySectors,
            costDirtySectors,
            CircleObstacles.Values,
            BoxObstacles.Values,
            CostStamps.Values);
    }

    private static NavigationWorld BuildRuntimeDirtyPreviewWorld(
        NavigationWorld targetWorld,
        HashSet<int> dirtySectors,
        HashSet<int> costDirtySectors,
        IEnumerable<CircleObstacle> circleObstacles,
        IEnumerable<BoxObstacle> boxObstacles,
        IReadOnlyCollection<CostStamp> costStamps)
    {
        if (targetWorld == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: target world is null.");
        if (dirtySectors == null || dirtySectors.Count == 0)
            return targetWorld;
        if (costDirtySectors == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: cost dirty sectors are null.");
        if (circleObstacles == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: circle obstacles are null.");
        if (boxObstacles == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: box obstacles are null.");

        List<int> dirtySectorIds = CreateSortedSectorIdSnapshot(dirtySectors, "preview dirty sectors");
        List<int> costDirtySectorIds = CreateSortedSectorIdSnapshot(costDirtySectors, "preview cost dirty sectors");
        List<CircleObstacle> circleObstacleSnapshot = CreateSortedCircleObstacleSnapshot(circleObstacles);
        List<BoxObstacle> boxObstacleSnapshot = CreateSortedBoxObstacleSnapshot(boxObstacles);
        List<CostStamp> costStampSnapshot = CreateSortedCostStampSnapshot(costStamps);

        long totalTicks = Stopwatch.GetTimestamp();
        long sectionTicks = Stopwatch.GetTimestamp();
        NavigationWorld preview = CloneNavigationWorldForRuntimeDirty(targetWorld, dirtySectors);
        long cloneTicks = Stopwatch.GetTimestamp() - sectionTicks;

        sectionTicks = Stopwatch.GetTimestamp();
        for (int i = 0; i < dirtySectorIds.Count; i++)
        {
            int sectorId = dirtySectorIds[i];
            ResetSectorWalkableFromBase(preview, preview.Sectors[sectorId]);
            preview.Sectors[sectorId].DirtyVersion++;
        }
        long resetTicks = Stopwatch.GetTimestamp() - sectionTicks;

        sectionTicks = Stopwatch.GetTimestamp();
        int circleCount = 0;
        for (int i = 0; i < circleObstacleSnapshot.Count; i++)
        {
            CircleObstacle circle = circleObstacleSnapshot[i];
            circleCount++;
            BlockCellsByCircleInSectorsFixed(preview, dirtySectors, circle.PositionFixed, circle.RadiusFixed);
        }

        int boxCount = 0;
        for (int i = 0; i < boxObstacleSnapshot.Count; i++)
        {
            BoxObstacle box = boxObstacleSnapshot[i];
            boxCount++;
            BlockCellsByBoxInSectorsFixed(
                preview,
                dirtySectors,
                box.CenterFixed,
                ResolveBoxObstacleNavigationHalfExtentsFixed(preview, box));
        }
        long obstacleTicks = Stopwatch.GetTimestamp() - sectionTicks;

        sectionTicks = Stopwatch.GetTimestamp();
        RebuildNeighborTraversalMaskForSectors(preview, dirtySectors);
        long neighborTicks = Stopwatch.GetTimestamp() - sectionTicks;

        sectionTicks = Stopwatch.GetTimestamp();
        SymmetrizeNeighborTraversalMaskForSectors(preview, dirtySectors);
        long symmetrizeTicks = Stopwatch.GetTimestamp() - sectionTicks;

        sectionTicks = Stopwatch.GetTimestamp();
        for (int i = 0; i < costDirtySectorIds.Count; i++)
            RebuildCostFieldForSector(preview, costDirtySectorIds[i], costStampSnapshot);
        long costTicks = Stopwatch.GetTimestamp() - sectionTicks;

        sectionTicks = Stopwatch.GetTimestamp();
        RebuildIslandFieldImmediate(preview);
        long islandTicks = Stopwatch.GetTimestamp() - sectionTicks;
        long elapsedTicks = Stopwatch.GetTimestamp() - totalTicks;
        LogRuntimeDirtyPreviewTimingIfNeeded(
            targetWorld,
            dirtySectors.Count,
            costDirtySectors.Count,
            circleCount,
            boxCount,
            cloneTicks,
            resetTicks,
            obstacleTicks,
            neighborTicks,
            symmetrizeTicks,
            costTicks,
            islandTicks,
            elapsedTicks);
        return preview;
    }

    private static void LogRuntimeDirtyPreviewTimingIfNeeded(
        NavigationWorld targetWorld,
        int dirtySectorCount,
        int costDirtySectorCount,
        int circleCount,
        int boxCount,
        long cloneTicks,
        long resetTicks,
        long obstacleTicks,
        long neighborTicks,
        long symmetrizeTicks,
        long costTicks,
        long islandTicks,
        long totalTicks)
    {
        if (totalTicks < Stopwatch.Frequency * 5 / 1000)
            return;

        int frame = GetFrameCount();
        if (frame - _lastRuntimeDirtyPreviewTimingFrame < 10)
            return;

        _lastRuntimeDirtyPreviewTimingFrame = frame;
        LogNoStacktrace(
            $"[FlowRuntimeDirtyPreviewTiming] frame={frame} worldVersion={targetWorld?.Version ?? 0} " +
            $"size={(targetWorld != null ? targetWorld.Width : 0)}x{(targetWorld != null ? targetWorld.Height : 0)} " +
            $"dirtySectors={dirtySectorCount} costSectors={costDirtySectorCount} obstacles(circle={circleCount},box={boxCount}) " +
            $"clone={TicksToMilliseconds(cloneTicks):F3}ms reset={TicksToMilliseconds(resetTicks):F3}ms " +
            $"obstacles={TicksToMilliseconds(obstacleTicks):F3}ms neighbor={TicksToMilliseconds(neighborTicks):F3}ms " +
            $"sym={TicksToMilliseconds(symmetrizeTicks):F3}ms cost={TicksToMilliseconds(costTicks):F3}ms " +
                   $"island={TicksToMilliseconds(islandTicks):F3}ms total={TicksToMilliseconds(totalTicks):F3}ms");
    }

    private static List<int> CreateSortedSectorIdSnapshot(IEnumerable<int> sectorIds, string source)
    {
        if (sectorIds == null)
            throw new InvalidOperationException($"CreateSortedSectorIdSnapshot failed: {source} is null.");

        var result = new List<int>(sectorIds);
        result.Sort();
        return result;
    }

    private static List<CircleObstacle> CreateSortedCircleObstacleSnapshot(IEnumerable<CircleObstacle> obstacles)
    {
        if (obstacles == null)
            throw new InvalidOperationException("CreateSortedCircleObstacleSnapshot failed: source is null.");

        var result = new List<CircleObstacle>(obstacles);
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i] == null)
                throw new InvalidOperationException($"CreateSortedCircleObstacleSnapshot failed: source contains null at index={i}.");
        }
        result.Sort((left, right) => left.Id.CompareTo(right.Id));
        return result;
    }

    private static List<BoxObstacle> CreateSortedBoxObstacleSnapshot(IEnumerable<BoxObstacle> obstacles)
    {
        if (obstacles == null)
            throw new InvalidOperationException("CreateSortedBoxObstacleSnapshot failed: source is null.");

        var result = new List<BoxObstacle>(obstacles);
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i] == null)
                throw new InvalidOperationException($"CreateSortedBoxObstacleSnapshot failed: source contains null at index={i}.");
        }
        result.Sort((left, right) => left.Id.CompareTo(right.Id));
        return result;
    }

    private static List<CostStamp> CreateSortedCostStampSnapshot(IEnumerable<CostStamp> stamps)
    {
        if (stamps == null)
            throw new InvalidOperationException("CreateSortedCostStampSnapshot failed: source is null.");

        var result = new List<CostStamp>(stamps);
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i] == null)
                throw new InvalidOperationException($"CreateSortedCostStampSnapshot failed: source contains null at index={i}.");
        }
        result.Sort((left, right) => left.Id.CompareTo(right.Id));
        return result;
    }

    private static void RebuildIslandFieldImmediate(NavigationWorld world)
    {
        if (world == null)
            throw new InvalidOperationException("RebuildIslandFieldImmediate failed: world is null.");

        if (world.IslandIds == null || world.IslandIds.Length != world.Width * world.Height)
            world.IslandIds = new int[world.Width * world.Height];

        Array.Clear(world.IslandIds, 0, world.IslandIds.Length);
        world.IslandCount = 0;
        world.MainIslandId = 0;
        world.MainIslandSize = 0;
        Queue<int> openQueue = new Queue<int>(256);
        int scanIndex = 0;
        int currentId = 0;
        int currentSize = 0;
        int mainId = 0;
        int mainSize = 0;
        bool bfsActive = false;
        AdvanceIslandFieldBuild(
            world,
            openQueue,
            ref scanIndex,
            ref currentId,
            ref currentSize,
            ref mainId,
            ref mainSize,
            ref bfsActive,
            long.MaxValue,
            forceComplete: true,
            out bool complete);
        if (!complete)
            throw new InvalidOperationException("RebuildIslandFieldImmediate failed: island build did not complete.");

        RebuildAllSectorLocalComponents(world);
    }

    private static bool CanSynchronouslyBuildWorldForEditorTest(int agentTypeId)
    {
#if UNITY_EDITOR
        return TryResolveTerrainSourceForAgentType(agentTypeId, out _);
#else
        return false;
#endif
    }

    private static bool EnsureWorldBuildJob(WorldRuntimeState state)
    {
        if (state == null)
            return false;
        if (state.BuildJob != null)
            return true;

        return EnsureWorldBuildJob(state, state.AgentTypeId);
    }

    private static bool EnsureWorldBuildJob(WorldRuntimeState state, int agentTypeId)
    {
        if (state == null)
            return false;
        if (state.BuildJob != null)
            return true;

        WorldBuildJob job = new WorldBuildJob
        {
            AgentTypeId = agentTypeId,
            Stage = WorldBuildStage.Initialize,
            Reason = _lastWorldDirtyReason
        };
        state.BuildJob = job;
        return true;
    }

    private static void ProcessWorldBuildJob(WorldRuntimeState state, long deadlineTicks, bool forceComplete)
    {
        WorldBuildJob job = state.BuildJob;
        if (job == null)
            return;

        EnsureWorldBuildTimingStarted(job);
        while (job.Stage != WorldBuildStage.Complete)
        {
            WorldBuildStage stageBefore = job.Stage;
            long stageStartTimestamp = Stopwatch.GetTimestamp();
            switch (job.Stage)
            {
                case WorldBuildStage.Initialize:
                    if (!InitializeWorldBuildJob(job))
                    {
                        state.BuildJob = null;
                        return;
                    }
                    break;
                case WorldBuildStage.CreateWorldShell:
                    CreateWorldBuildShell(job);
                    break;
                case WorldBuildStage.ApplyRuntimeObstacles:
                    ProcessWorldBuildObstacles(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.InitializeSectors:
                    InitializeWorldBuildSectors(job);
                    break;
                case WorldBuildStage.BuildCellNavAnchors:
                    ProcessWorldBuildCellNavAnchors(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.NeighborMask:
                    ProcessWorldBuildNeighborMask(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.SymmetrizeNeighborMask:
                    ProcessWorldBuildSymmetrizeNeighborMask(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.CostField:
                    ProcessWorldBuildCostField(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.IslandField:
                    ProcessWorldBuildIslandField(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.GoalProjectionIndex:
                    ProcessWorldBuildGoalProjectionIndex(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.PortalGraph:
                    ProcessWorldBuildPortalGraph(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.Hierarchy:
                    ProcessWorldBuildHierarchy(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.Commit:
                    CommitWorldBuildJob(state, job);
                    job.Stage = WorldBuildStage.Complete;
                    break;
                default:
                    throw new InvalidOperationException($"ProcessWorldBuildJob failed: unknown stage {job.Stage}.");
            }

            LogWorldBuildStageTimingIfNeeded(job, stageBefore, stageStartTimestamp, forceComplete);
            if (job.Stage == WorldBuildStage.Complete)
                break;

            if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                return;
        }

        state.BuildJob = null;
    }

    private static void EnsureWorldBuildTimingStarted(WorldBuildJob job)
    {
        if (job == null)
            throw new InvalidOperationException("EnsureWorldBuildTimingStarted failed: job is null.");
        if (job.TimingInitialized)
            return;

        long now = Stopwatch.GetTimestamp();
        job.BuildStartedTimestamp = now;
        job.StageStartedTimestamp = now;
        job.TimedStage = job.Stage;
        job.TimingInitialized = true;
        LogNoStacktrace($"[FlowWorldBuildStage] begin agentType={job.AgentTypeId} reason={job.Reason}");
    }

    private static void LogWorldBuildStageTimingIfNeeded(WorldBuildJob job, WorldBuildStage stageBefore, long stageStartTimestamp, bool forceComplete)
    {
        if (job == null)
            throw new InvalidOperationException("LogWorldBuildStageTimingIfNeeded failed: job is null.");

        bool stageChanged = job.Stage != stageBefore;
        long now = Stopwatch.GetTimestamp();
        double elapsedMs = TicksToMilliseconds(now - stageStartTimestamp);
        if (!stageChanged && (!forceComplete || elapsedMs < 5.0))
            return;

        double totalMs = TicksToMilliseconds(now - job.BuildStartedTimestamp);
        LogNoStacktrace(
            $"[FlowWorldBuildStage] agentType={job.AgentTypeId} stage={stageBefore} next={job.Stage} " +
            $"elapsedMs={elapsedMs:F3} totalMs={totalMs:F3} forceComplete={forceComplete} {BuildWorldBuildTimingSummary(job)}");
    }

    private static string BuildWorldBuildTimingSummary(WorldBuildJob job)
    {
        int cellCount = job.Width > 0 && job.Height > 0 ? job.Width * job.Height : 0;
        NavigationWorld world = job.WorkingWorld;
        int sectorCount = world?.Sectors != null ? world.Sectors.Length : 0;
        int portalCount = world?.PortalsById != null ? world.PortalsById.Count : 0;
        int transitionCount = CountPortalTransitions(world);
        int pendingPortalAccessCount = job.PendingPortalAccessEntries != null ? job.PendingPortalAccessEntries.Count : 0;
        int circleCount = job.CircleObstacles != null ? job.CircleObstacles.Count : 0;
        int boxCount = job.BoxObstacles != null ? job.BoxObstacles.Count : 0;
        int costStampCount = job.CostStamps != null ? job.CostStamps.Count : 0;
        int sectorSize = world != null ? world.SectorSizeInCells : 0;
        int sectorCountX = world != null ? world.SectorCountX : 0;
        int sectorCountY = world != null ? world.SectorCountY : 0;
        int islandCount = world != null ? world.IslandCount : 0;
        int mainIslandSize = world != null ? world.MainIslandSize : 0;

        return
            $"size={job.Width}x{job.Height} cellSize={job.CellSize:F3} cells={cellCount} " +
            $"sectorSize={sectorSize} sectors={sectorCount}({sectorCountX}x{sectorCountY}) " +
            $"portals={portalCount} transitions={transitionCount} pendingPortalAccess={pendingPortalAccessCount} " +
            $"islands={islandCount} mainIslandSize={mainIslandSize} obstacles(circle={circleCount},box={boxCount},cost={costStampCount}) " +
            $"cursors(cell={job.CellCursor},sector={job.SectorCursor},portalAdd={job.PortalAddCursor},portalSector={job.PortalTransitionCursor},portalFrom={job.PortalTransitionFromCursor})";
    }

    private static int CountPortalTransitions(NavigationWorld world)
    {
        if (world?.Sectors == null)
            return 0;

        int count = 0;
        for (int i = 0; i < world.Sectors.Length; i++)
        {
            SectorData sector = world.Sectors[i];
            if (sector?.PortalTransitions != null)
                count += sector.PortalTransitions.Count;
        }

        return count;
    }

    private static bool HasPortalTransition(SectorData sector, int fromPortalId, int toPortalId)
    {
        if (sector == null)
            throw new InvalidOperationException("HasPortalTransition failed: sector is null.");

        for (int i = 0; i < sector.PortalTransitions.Count; i++)
        {
            PortalTransition transition = sector.PortalTransitions[i];
            if (transition.FromPortalId == fromPortalId && transition.ToPortalId == toPortalId)
                return true;
        }

        return false;
    }

    private static void RebuildIncomingPortalTransitionIndex(SectorData sector)
    {
        if (sector == null)
            throw new InvalidOperationException("RebuildIncomingPortalTransitionIndex failed: sector is null.");

        sector.IncomingPortalTransitionsByToPortalId.Clear();
        sector.OutgoingPortalTransitionsByFromPortalId.Clear();
        for (int i = 0; i < sector.PortalTransitions.Count; i++)
        {
            PortalTransition transition = sector.PortalTransitions[i];
            if (transition == null)
                throw new InvalidOperationException($"RebuildIncomingPortalTransitionIndex failed: transition is null sector={sector.SectorId} index={i}.");

            if (!sector.IncomingPortalTransitionsByToPortalId.TryGetValue(transition.ToPortalId, out List<PortalTransition> incoming))
            {
                incoming = new List<PortalTransition>(4);
                sector.IncomingPortalTransitionsByToPortalId.Add(transition.ToPortalId, incoming);
            }

            incoming.Add(transition);

            if (!sector.OutgoingPortalTransitionsByFromPortalId.TryGetValue(transition.FromPortalId, out List<PortalTransition> outgoing))
            {
                outgoing = new List<PortalTransition>(4);
                sector.OutgoingPortalTransitionsByFromPortalId.Add(transition.FromPortalId, outgoing);
            }

            outgoing.Add(transition);
        }
    }

    private static List<PortalTransition> GetIncomingPortalTransitions(SectorData sector, int toPortalId)
    {
        if (sector == null)
            throw new InvalidOperationException("GetIncomingPortalTransitions failed: sector is null.");
        if (sector.PortalTransitions.Count > 0 && sector.IncomingPortalTransitionsByToPortalId.Count == 0)
            throw new InvalidOperationException($"GetIncomingPortalTransitions failed: missing incoming transition index sector={sector.SectorId} transitions={sector.PortalTransitions.Count} toPortal={toPortalId}.");

        return sector.IncomingPortalTransitionsByToPortalId.TryGetValue(toPortalId, out List<PortalTransition> incoming)
            ? incoming
            : null;
    }

    private static List<PortalTransition> GetOutgoingPortalTransitions(SectorData sector, int fromPortalId)
    {
        if (sector == null)
            throw new InvalidOperationException("GetOutgoingPortalTransitions failed: sector is null.");
        if (sector.PortalTransitions.Count > 0 && sector.OutgoingPortalTransitionsByFromPortalId.Count == 0)
            throw new InvalidOperationException($"GetOutgoingPortalTransitions failed: missing outgoing transition index sector={sector.SectorId} transitions={sector.PortalTransitions.Count} fromPortal={fromPortalId}.");

        return sector.OutgoingPortalTransitionsByFromPortalId.TryGetValue(fromPortalId, out List<PortalTransition> outgoing)
            ? outgoing
            : null;
    }

    private static void AddPortalTransitionIfMissing(SectorData sector, int fromPortalId, int toPortalId)
    {
        if (sector == null)
            throw new InvalidOperationException("AddPortalTransitionIfMissing failed: sector is null.");
        if (fromPortalId == toPortalId)
            throw new InvalidOperationException($"AddPortalTransitionIfMissing failed: self transition sector={sector.SectorId} portal={fromPortalId}.");
        if (HasPortalTransition(sector, fromPortalId, toPortalId))
            return;

        sector.PortalTransitions.Add(new PortalTransition
        {
            FromPortalId = fromPortalId,
            ToPortalId = toPortalId
        });
    }

    private static bool PortalsShareSectorLocalComponent(NavigationWorld world, SectorData sector, int portalAId, int portalBId)
    {
        if (world == null)
            throw new InvalidOperationException("PortalsShareSectorLocalComponent failed: world is null.");
        if (sector == null)
            throw new InvalidOperationException("PortalsShareSectorLocalComponent failed: sector is null.");
        if (sector.LocalComponentIds == null || sector.LocalComponentIds.Length != sector.Width * sector.Height)
            throw new InvalidOperationException($"PortalsShareSectorLocalComponent failed: sector local components missing sector={sector.SectorId}.");
        if (sector.LocalComponentCount <= 1)
            return true;

        PortalData portalA = GetPortalById(world, portalAId);
        PortalData portalB = GetPortalById(world, portalBId);
        Vector2Int[] cellsA = GetPortalCellsForSector(portalA, sector.SectorId);
        Vector2Int[] cellsB = GetPortalCellsForSector(portalB, sector.SectorId);
        if (cellsA == null || cellsA.Length == 0 || cellsB == null || cellsB.Length == 0)
            throw new InvalidOperationException($"PortalsShareSectorLocalComponent failed: empty portal cells sector={sector.SectorId} a={portalAId} b={portalBId}.");

        bool hasComponentA = false;
        bool hasComponentB = false;
        for (int a = 0; a < cellsA.Length; a++)
        {
            Vector2Int cellA = cellsA[a];
            if (!IsInsideSector(sector, cellA.x, cellA.y) || !world.IsWalkable(cellA.x, cellA.y))
                continue;

            int componentA = sector.LocalComponentIds[GetSectorLocalIndex(sector, cellA.x, cellA.y)];
            if (componentA <= 0)
                continue;

            hasComponentA = true;
            for (int b = 0; b < cellsB.Length; b++)
            {
                Vector2Int cellB = cellsB[b];
                if (!IsInsideSector(sector, cellB.x, cellB.y) || !world.IsWalkable(cellB.x, cellB.y))
                    continue;

                int componentB = sector.LocalComponentIds[GetSectorLocalIndex(sector, cellB.x, cellB.y)];
                if (componentB <= 0)
                    continue;

                hasComponentB = true;
                if (componentA == componentB)
                    return true;
            }
        }

        if (!hasComponentA || !hasComponentB)
            throw new InvalidOperationException(
                $"PortalsShareSectorLocalComponent failed: portal has no local component sector={sector.SectorId} a={portalAId} hasA={hasComponentA} b={portalBId} hasB={hasComponentB}.");

        return false;
    }

    private static void LogWorldBuildPortalGraphTiming(WorldBuildJob job)
    {
        if (job == null)
            throw new InvalidOperationException("LogWorldBuildPortalGraphTiming failed: job is null.");

        LogNoStacktrace(
            $"[FlowPortalGraphTiming] kind=world-build agentType={job.AgentTypeId} " +
            BuildPortalGraphTimingSummary(
                job.PortalGraphBoundaryTicks,
                job.PortalGraphAddTicks,
                job.PortalGraphAccessCount,
                job.PortalGraphAnalyticAccessCount,
                job.PortalGraphTransitionCostChecks,
                job.PortalGraphAnalyticCheckCount,
                job.PortalGraphAnalyticClearSectorCount,
                job.PortalGraphAnalyticMultiComponentRejectCount,
                job.PortalGraphAnalyticNonClearRejectCount));
    }

    private static void LogRuntimeDirtyPortalGraphTiming(RuntimeDirtyRebuildJob job)
    {
        if (job == null)
            throw new InvalidOperationException("LogRuntimeDirtyPortalGraphTiming failed: job is null.");

        LogNoStacktrace(
            $"[FlowPortalGraphTiming] kind=runtime-dirty worldVersion={job.TargetWorld?.Version ?? 0} " +
            BuildPortalGraphTimingSummary(
                job.PortalGraphBoundaryTicks,
                job.PortalGraphAddTicks,
                job.PortalGraphAccessCount,
                job.PortalGraphAnalyticAccessCount,
                job.PortalGraphTransitionCostChecks,
                job.PortalGraphAnalyticCheckCount,
                job.PortalGraphAnalyticClearSectorCount,
                job.PortalGraphAnalyticMultiComponentRejectCount,
                job.PortalGraphAnalyticNonClearRejectCount));
    }

    private static string BuildPortalGraphTimingSummary(
        long boundaryTicks,
        long addTicks,
        int accessCount,
        int analyticAccessCount,
        int transitionCostChecks,
        int analyticCheckCount,
        int analyticClearSectorCount,
        int analyticMultiComponentRejectCount,
        int analyticNonClearRejectCount)
    {
        return
            $"boundaryMs={TicksToMilliseconds(boundaryTicks):F3} addMs={TicksToMilliseconds(addTicks):F3} " +
            $"accessFields={accessCount} analyticAccessFields={analyticAccessCount} transitionSources={transitionCostChecks} " +
            $"analyticChecks={analyticCheckCount} analyticClearSectors={analyticClearSectorCount} analyticRejects(component={analyticMultiComponentRejectCount},nonClear={analyticNonClearRejectCount}) " +
            "authority=deterministic-topology";
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static bool[] RentCopiedBoolArray(bool[] source)
    {
        if (source == null)
            throw new InvalidOperationException("RentCopiedBoolArray failed: source is null.");

        bool[] copy = RentBoolArray(source.Length, clear: false);
        Array.Copy(source, copy, source.Length);
        return copy;
    }

    private static byte[] RentCopiedByteArray(byte[] source)
    {
        if (source == null)
            throw new InvalidOperationException("RentCopiedByteArray failed: source is null.");

        byte[] copy = RentByteArray(source.Length, clear: false);
        Array.Copy(source, copy, source.Length);
        return copy;
    }

    private static int[] RentCopiedIntArray(int[] source)
    {
        if (source == null)
            throw new InvalidOperationException("RentCopiedIntArray failed: source is null.");

        int[] copy = RentIntArray(source.Length, clear: false);
        Array.Copy(source, copy, source.Length);
        return copy;
    }

    private static bool[] RentBoolArray(int length, bool clear)
    {
        if (length <= 0)
            throw new InvalidOperationException($"RentBoolArray failed: invalid length={length}.");

        bool[] array = RentArray(BoolArrayPool, length);
        if (array == null)
            array = new bool[length];
        else if (clear)
            Array.Clear(array, 0, array.Length);
        return array;
    }

    private static byte[] RentByteArray(int length, bool clear)
    {
        if (length <= 0)
            throw new InvalidOperationException($"RentByteArray failed: invalid length={length}.");

        byte[] array = RentArray(ByteArrayPool, length);
        if (array == null)
            array = new byte[length];
        else if (clear)
            Array.Clear(array, 0, array.Length);
        return array;
    }

    private static int[] RentIntArray(int length, bool clear)
    {
        if (length <= 0)
            throw new InvalidOperationException($"RentIntArray failed: invalid length={length}.");

        int[] array = RentArray(IntArrayPool, length);
        if (array == null)
            array = new int[length];
        else if (clear)
            Array.Clear(array, 0, array.Length);
        return array;
    }

    private static T[] RentArray<T>(Dictionary<int, Stack<T[]>> pool, int length)
    {
        if (pool.TryGetValue(length, out Stack<T[]> stack) && stack.Count > 0)
            return stack.Pop();

        return null;
    }

    private static void ReturnWorldArrayIfOwned(bool[] array, bool[] readOnlySource, bool[] current)
    {
        if (array == null || ReferenceEquals(array, readOnlySource) || ReferenceEquals(array, current))
            return;

        ReturnArray(BoolArrayPool, array);
    }

    private static void ReturnWorldArrayIfOwned(byte[] array, byte[] readOnlySource, byte[] current)
    {
        if (array == null || ReferenceEquals(array, readOnlySource) || ReferenceEquals(array, current))
            return;

        ReturnArray(ByteArrayPool, array);
    }

    private static void ReturnWorldArrayIfOwned(int[] array, int[] readOnlySource, int[] current)
    {
        if (array == null || ReferenceEquals(array, readOnlySource) || ReferenceEquals(array, current))
            return;

        ReturnArray(IntArrayPool, array);
    }

    private static void ReturnByteArray(byte[] array)
    {
        if (array == null)
            return;

        ReturnArray(ByteArrayPool, array);
    }

    private static void ReturnArray<T>(Dictionary<int, Stack<T[]>> pool, T[] array)
    {
        if (array == null || array.Length == 0)
            return;

        if (!pool.TryGetValue(array.Length, out Stack<T[]> stack))
        {
            stack = new Stack<T[]>();
            pool.Add(array.Length, stack);
        }

        if (stack.Count >= MaxRuntimeArrayPoolEntriesPerLength)
            return;

        stack.Push(array);
    }

    private static void LogNoStacktrace(string message)
    {
        if (!IsMovementDiagnosticsEnabled())
            return;
        Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", message);
    }

    private static void LogWarningNoStacktrace(string message)
    {
        if (!IsMovementDiagnosticsEnabled())
            return;
        Debug.LogFormat(LogType.Warning, LogOption.NoStacktrace, null, "{0}", message);
    }

    private static bool InitializeWorldBuildJob(WorldBuildJob job)
    {
        if (job == null)
            throw new InvalidOperationException("InitializeWorldBuildJob failed: job is null.");

        if (TryResolveTerrainSourceForAgentType(job.AgentTypeId, out TestTerrainOverride terrainSource))
        {
            job.Width = terrainSource.Width;
            job.Height = terrainSource.Height;
            job.CellSize = terrainSource.CellSize;
            job.EncodedCenterClearance = terrainSource.EncodedCenterClearance;
            job.Origin = terrainSource.Origin;
            if (terrainSource.HasFixedAuthorityPayload)
            {
                job.SetAuthorityGridMetadata(
                    terrainSource.CellSizeGridRaw,
                    terrainSource.EncodedCenterClearanceFixedRaw,
                    terrainSource.AgentRadiusFixedRaw,
                    terrainSource.OriginXGridRaw,
                    terrainSource.OriginZGridRaw);
            }
            else
            {
                job.FreezeAuthorityGridMetadata();
            }
            if (terrainSource.DerivedNavigationData != null)
            {
                NavigationWorld prebakedWorld = ImportDerivedNavigationWorld(terrainSource);
                job.WorkingWorld = ApplyInitialRuntimeOverlayToPrebakedWorldIfNeeded(
                    prebakedWorld,
                    job.AgentTypeId,
                    out job.PendingPortalAccessEntries);
                RefreshWorldBuildAuthorityInputHash(job);
                if (job.WorkingWorld.Hierarchy == null)
                    throw new InvalidOperationException("InitializeWorldBuildJob failed: prebaked navigation has no hierarchy.");
                job.UsesPrebakedHierarchy = true;
                job.Stage = WorldBuildStage.GoalProjectionIndex;
                return true;
            }

            job.BaseWalkableMask = (bool[])terrainSource.WalkableMask.Clone();
            job.BaseCostField = terrainSource.CostField != null
                ? (byte[])terrainSource.CostField.Clone()
                : null;
            job.BaseNeighborTraversalMask = terrainSource.NeighborTraversalMask != null
                ? (byte[])terrainSource.NeighborTraversalMask.Clone()
                : null;
            job.StaticCollisionVertices = terrainSource.StaticCollisionVertices != null
                ? (FixVector2[])terrainSource.StaticCollisionVertices.Clone()
                : null;
            job.StaticCollisionPathStarts = terrainSource.StaticCollisionPathStarts != null
                ? (int[])terrainSource.StaticCollisionPathStarts.Clone()
                : null;
            job.CellNavAnchors = terrainSource.CellNavAnchors != null
                ? (Vector3[])terrainSource.CellNavAnchors.Clone()
                : null;
            job.CellNavAnchorsFixedXZ = terrainSource.HasFixedAuthorityPayload
                ? (FixVector2[])terrainSource.CellNavAnchorsFixedXZ.Clone()
                : CreateNavigationAnchorFixedXZSnapshot(job.CellNavAnchors);
            job.HasProvidedCellNavAnchors = job.CellNavAnchors != null;
            job.HasProvidedNeighborTraversalMask = job.BaseNeighborTraversalMask != null;
            RefreshWorldBuildAuthorityInputHash(job);
            job.Stage = WorldBuildStage.CreateWorldShell;
            return true;
        }

        throw new InvalidOperationException(
            "InitializeWorldBuildJob failed: FlowNavigationGridSource has not applied a FlowNavigationGridAsset. " +
            "Runtime navigation is strict authored-grid only and will not fall back to any legacy navigation source.");
    }

    private static bool TryResolveTerrainSourceForAgentType(int agentTypeId, out TestTerrainOverride terrainSource)
    {
        if (AuthoredTerrainSources.TryGetValue(agentTypeId, out terrainSource))
            return true;

        if (_testTerrainOverride != null)
        {
            if (_testTerrainOverride.AgentTypeId == AnyAgentTypeId || _testTerrainOverride.AgentTypeId == agentTypeId)
            {
                terrainSource = _testTerrainOverride;
                return true;
            }
        }

        if (AuthoredTerrainSources.TryGetValue(AnyAgentTypeId, out terrainSource))
            return true;

        terrainSource = null;
        return false;
    }

    private static void CreateWorldBuildShell(WorldBuildJob job)
    {
        bool[] runtimeWalkableMask = (bool[])job.BaseWalkableMask.Clone();
        job.WorkingWorld = new NavigationWorld
        {
            AgentTypeId = job.AgentTypeId,
            Width = job.Width,
            Height = job.Height,
            CellSize = job.CellSize,
            EncodedCenterClearance = job.EncodedCenterClearance,
            Origin = job.Origin,
            BaseWalkableMask = job.BaseWalkableMask,
            BaseNeighborTraversalMask = job.BaseNeighborTraversalMask != null && job.BaseNeighborTraversalMask.Length == job.Width * job.Height
                ? (byte[])job.BaseNeighborTraversalMask.Clone()
                : null,
            StaticCollisionVertices = job.StaticCollisionVertices,
            StaticCollisionPathStarts = job.StaticCollisionPathStarts,
            SourceCostField = job.BaseCostField != null && job.BaseCostField.Length == job.Width * job.Height
                ? (byte[])job.BaseCostField.Clone()
                : null,
            WalkableMask = runtimeWalkableMask,
            CostField = job.BaseCostField != null && job.BaseCostField.Length == job.Width * job.Height
                ? (byte[])job.BaseCostField.Clone()
                : new byte[job.Width * job.Height],
            CellNavAnchors = job.CellNavAnchors != null && job.CellNavAnchors.Length == job.Width * job.Height ? job.CellNavAnchors : new Vector3[job.Width * job.Height],
            CellNavAnchorsFixedXZ = job.CellNavAnchorsFixedXZ != null && job.CellNavAnchorsFixedXZ.Length == job.Width * job.Height
                ? job.CellNavAnchorsFixedXZ
                : new FixVector2[job.Width * job.Height],
            NeighborTraversalMask = job.BaseNeighborTraversalMask != null && job.BaseNeighborTraversalMask.Length == job.Width * job.Height
                ? (byte[])job.BaseNeighborTraversalMask.Clone()
                : new byte[job.Width * job.Height],
            IslandIds = new int[job.Width * job.Height],
            SectorSizeInCells = ResolveRuntimeSectorSizeInCells(job.CellSizeGridRaw)
        };
        job.WorkingWorld.SetAuthorityGridMetadata(
            job.CellSizeGridRaw,
            job.EncodedCenterClearanceFixedRaw,
            job.AgentRadiusFixedRaw,
            job.OriginXGridRaw,
            job.OriginZGridRaw);
        if (!job.HasAuthorityGridMetadata
            || job.WorkingWorld.CellSizeGridRaw != job.CellSizeGridRaw
            || job.WorkingWorld.EncodedCenterClearanceFixedRaw != job.EncodedCenterClearanceFixedRaw
            || job.WorkingWorld.AgentRadiusFixedRaw != job.AgentRadiusFixedRaw
            || job.WorkingWorld.OriginXGridRaw != job.OriginXGridRaw
            || job.WorkingWorld.OriginZGridRaw != job.OriginZGridRaw)
        {
            throw new InvalidOperationException("CreateWorldBuildShell authority grid metadata does not match the frozen build input.");
        }
        job.CircleObstacles = job.IncludeRuntimeObstacles
            ? CreateSortedCircleObstacleSnapshot(CircleObstacles.Values)
            : new List<CircleObstacle>();
        job.BoxObstacles = job.IncludeRuntimeObstacles
            ? CreateSortedBoxObstacleSnapshot(BoxObstacles.Values)
            : new List<BoxObstacle>();
        job.CostStamps = job.IncludeRuntimeObstacles
            ? CreateSortedCostStampSnapshot(CostStamps.Values)
            : new List<CostStamp>();
        RefreshWorldBuildAuthorityInputHash(job);
        job.ObstacleCursor = 0;
        job.ApplyingCircleObstacles = true;
        job.Stage = WorldBuildStage.ApplyRuntimeObstacles;
    }

    private static void CommitWorldBuildJob(WorldRuntimeState state, WorldBuildJob job)
    {
        if (job.WorkingWorld == null)
            throw new InvalidOperationException("CommitWorldBuildJob failed: working world is null.");
        if (job.WorkingWorld.GoalProjectionSpatialIndex == null)
            throw new InvalidOperationException("CommitWorldBuildJob failed: exact goal-projection index was not prepared before commit.");

        NavigationWorld previousWorld = state.World;
        int previousWorldVersion = previousWorld != null ? previousWorld.Version : 0;
        _perf.WorldBuilds++;
        state.World = job.WorkingWorld;
        state.World.Version = AllocateNavigationWorldVersion();
        _navigationTopologyVersion++;
        LogIslandFieldDiagnostics(state.World, "world-build");
        state.IsDirty = false;
        state.DirtyRuntimeObstacleSectors.Clear();
        state.RuntimeDirtyJob = null;
        ClearFlowTileCache();
        ReturnPendingFlowTileBuildIntegrations();
        FlowTileBuildQueue.Clear();
        PendingFlowTileBuildJobs.Clear();
        ActiveFlowTileBuildKeys.Clear();
        ClearFlowTileBuildSchedulingState();
        PendingFlowTileDependencyKeys.Clear();
        ClearNavigationPathRequests();
        ClearSectorPathCache();
        StartPortalChoiceCache.Clear();
        RemoveSectorPortalAccessCacheEntriesForWorldVersion(previousWorldVersion);
        ClearSharedGoalFieldCache();
        SharedGoalFieldBuildQueue.Clear();
        PendingSharedGoalFieldBuildJobs.Clear();
        ActiveSharedGoalFieldBuildKeys.Clear();
        ActiveSharedGoalFieldDemandStartSectors.Clear();
        ActiveSharedGoalFieldDemandStartCells.Clear();
        s_NavigationDistancePrewarmCompleted = false;
        CommitPendingSectorPortalAccessEntries(
            state.World,
            job.PendingPortalAccessEntries,
            entriesAreFinalized: false);
        EnsureAllSectorPortalAccessCoverage(state.World, "world-build-commit");
        FinalizeWorldCostStorage(state.World);
        RebuildDeterministicPortalTransitionCosts(state.World);
        EnsureFlowTileWorldInputBackings(state.World);
        ValidateAllSectorPortalAccessCoverage(state.World, "world-build-commit");
        if (state.World.Hierarchy == null)
            throw new InvalidOperationException("CommitWorldBuildJob failed: hierarchy was not prepared before commit.");
        ValidatePortalHierarchyEdgeCosts(state.World, state.World.Hierarchy);
        EnsureFlowPathKernelWitnessIndex(state.World.Hierarchy);
        EnsureFlowPathKernelSearchGraphIndexes(state.World);
        if (!ReferenceEquals(previousWorld?.Hierarchy, state.World.Hierarchy))
        {
            previousWorld?.L0SearchGraphIndex?.Dispose();
            if (previousWorld != null)
                previousWorld.L0SearchGraphIndex = null;
            DisposeFlowPathKernelWitnessIndex(previousWorld?.Hierarchy);
        }
        if (previousWorld != null && !ReferenceEquals(previousWorld, state.World))
            DisposeFlowTileWorldInputBackings(previousWorld);
        RefreshNavigationWorldDeterministicHash(state.World);
        CombatTargetSlotCache.Clear();
        ClearFixedPortalOwnersForWorld(previousWorldVersion);
        _world = state.World;

        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            int walkableCount = 0;
            bool[] walkableMask = state.World.WalkableMask;
            for (int i = 0; i < walkableMask.Length; i++)
            {
                if (walkableMask[i])
                    walkableCount++;
            }

            LogNoStacktrace(
                $"[FlowWorld] Rebuilt worldVersion={state.World.Version} agentType={job.AgentTypeId} size={job.Width}x{job.Height} cellSize={job.CellSize:F3} " +
                $"origin={job.Origin} walkable={walkableCount}/{walkableMask.Length} dirtyReason={job.Reason}");
        }
    }

}
