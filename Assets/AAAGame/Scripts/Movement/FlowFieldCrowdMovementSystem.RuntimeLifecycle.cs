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
    public static void SetConfig(FlowFieldNavigationConfig config)
    {
        if (config == null)
            throw new InvalidOperationException("FlowFieldCrowdMovementSystem.SetConfig failed: config is null.");

        bool requiresRebuild = Config.SectorWorldSizeMillimeters != config.SectorWorldSizeMillimeters
                               || Config.PortalNarrowWidthCells != config.PortalNarrowWidthCells
                               || Config.PortalMaxWindowWidthCells != config.PortalMaxWindowWidthCells;
#if UNITY_EDITOR
        requiresRebuild |= Config.EditorTestSectorSizeInCells != config.EditorTestSectorSizeInCells;
#endif

        if (config.SectorWorldSizeMillimeters <= 0)
            throw new InvalidOperationException("Flow navigation sector world size must be positive.");
        Config.SectorWorldSizeMillimeters = config.SectorWorldSizeMillimeters;
#if UNITY_EDITOR
        Config.EditorTestSectorSizeInCells = config.EditorTestSectorSizeInCells;
#endif
        Config.PortalNarrowWidthCells = Mathf.Max(1, config.PortalNarrowWidthCells);
        Config.PortalMaxWindowWidthCells = Mathf.Max(2, config.PortalMaxWindowWidthCells);
        Config.FlowTileCacheLimit = Mathf.Max(16, config.FlowTileCacheLimit);
        Config.WorldBuildOperationQuota = Mathf.Max(1, config.WorldBuildOperationQuota);
        Config.RuntimeRebuildOperationQuota = Mathf.Max(1, config.RuntimeRebuildOperationQuota);
        Config.DeterministicFlowTileCommitQuota = Mathf.Max(1, config.DeterministicFlowTileCommitQuota);
        Config.FlowTileBuildOperationQuota = Mathf.Max(1, config.FlowTileBuildOperationQuota);
        Config.SharedGoalBuildOperationQuota = Mathf.Max(1, config.SharedGoalBuildOperationQuota);
        Config.MovingTargetProjectionOperationQuota = Mathf.Max(1, config.MovingTargetProjectionOperationQuota);
        Config.PathRequestOperationQuota = Mathf.Max(1, config.PathRequestOperationQuota);
        Config.RequireAuthoredNavigationSource = config.RequireAuthoredNavigationSource;
        Config.EnableDeterministicStaticCollisionShadow = config.EnableDeterministicStaticCollisionShadow;
        Config.StaticCollisionShadowMismatchTolerance = Mathf.Max(0f, config.StaticCollisionShadowMismatchTolerance);
        Config.StaticCollisionShadowLogIntervalTicks = Mathf.Max(1, config.StaticCollisionShadowLogIntervalTicks);
        LogicStaticCollisionShadowService.Configure(
            Config.EnableDeterministicStaticCollisionShadow,
            Config.StaticCollisionShadowMismatchTolerance,
            Config.StaticCollisionShadowLogIntervalTicks);
        Config.DrawNavigationDebug = config.DrawNavigationDebug;
        Config.DrawFlowFieldDebug = config.DrawFlowFieldDebug;
        Config.StrictNoFallback = true;

        if (requiresRebuild)
            MarkWorldDirty();
    }

    public static void PrepareRuntimeDependencies()
    {
        Fix64 smallRadius = ResolveConfiguredAgentTypeRadiusFixed("SmallUnitCollisionRadius");
        Fix64 mediumRadius = ResolveConfiguredAgentTypeRadiusFixed("MediumUnitCollisionRadius");
        Fix64 largeRadius = ResolveConfiguredAgentTypeRadiusFixed("LargeUnitCollisionRadius");

        AAAGame.FlowPath.FlowPathKernelRuntime.Prepare();
        PrepareNavigationPathRuntimeContainerCode();
        PrepareMovingTargetRuntimeContainerCode();
        PrepareRuntimeCode();

        s_SmallAgentTypeRadiusFixed = smallRadius;
        s_MediumAgentTypeRadiusFixed = mediumRadius;
        s_LargeAgentTypeRadiusFixed = largeRadius;
        s_RuntimeAgentTypeRadiiPrepared = true;
        Debug.Log(
            $"[FlowField] Runtime agent radii prepared. smallRaw={smallRadius.RawValue}, " +
            $"mediumRaw={mediumRadius.RawValue}, largeRaw={largeRadius.RawValue}.");
    }

    public static void PrepareRuntimeCode()
    {
        NavigationRuntimeCodePreparation.Prepare(typeof(FlowFieldCrowdMovementSystem));
    }

    public static void MarkWorldDirty(string reason = null)
    {
        _lastWorldDirtyReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;

        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            state.IsDirty = true;
            state.DirtyRuntimeObstacleSectors.Clear();
            ReturnRuntimeDirtyWorkingWorld(state.RuntimeDirtyJob);
            state.RuntimeDirtyJob = null;
            state.BuildJob = null;
        }

        ClearFlowTileCache();
        ReturnPendingFlowTileBuildIntegrations();
        FlowTileBuildQueue.Clear();
        PendingFlowTileBuildJobs.Clear();
        ActiveFlowTileBuildKeys.Clear();
        ClearFlowTileBuildSchedulingState();
        PendingFlowTileDependencyKeys.Clear();
        ClearNavigationPathRequests();
        ClearSectorPathCache();
        ClearSectorPortalAccessCache();
        StartPortalChoiceCache.Clear();
        ClearSharedGoalFieldCache();
        SharedGoalFieldBuildQueue.Clear();
        PendingSharedGoalFieldBuildJobs.Clear();
        ActiveSharedGoalFieldBuildKeys.Clear();
        ActiveSharedGoalFieldDemandStartSectors.Clear();
        ActiveSharedGoalFieldDemandStartCells.Clear();
        s_NavigationDistancePrewarmCompleted = false;
        MovingTargetAnchors.Clear();
        MovingTargetProjectionQueue.Clear();
        PendingMovingTargetProjectionKeys.Clear();
        CombatTargetSlotCache.Clear();
        FixedPortalOwners.Clear();
        FixedPortalOwnerEvaluatedFrameByWorld.Clear();
        FixedPortalParticipationByAgent.Clear();
        FixedPortalParticipantIndexByKey.Clear();
        PendingFixedCorridorParticipantAgentIdsByWorld.Clear();
        FixedCorridorLookupByWorldVersion.Clear();
        AgentSpatialBuckets.Clear();
        ResetNavigationGoalOccupancyBuckets();
        NearbyAgentScratch.Clear();
        _lastAgentSpatialBucketFrame = -1;
        _lastAgentSpatialBucketWorldVersion = -1;
        _lastNavigationGoalOccupancyBucketFrame = -1;
        _lastNavigationGoalOccupancyBucketWorldVersion = -1;
        _navigationGoalOccupancyMaximumThreshold = Fix64.Zero;
        foreach (KeyValuePair<int, AgentRuntimeData> pair in Agents)
        {
            ClearCommittedNavigationPath(pair.Value);
            ClearStableGoal(pair.Value);
        }

        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
            LogNoStacktrace($"[FlowWorld] MarkWorldDirty reason={_lastWorldDirtyReason} states={WorldStates.Count} agents={Agents.Count}");
    }

    public static void PrepareInitialNavigationWorlds()
    {
        List<int> agentTypeIds = CollectNavigationWorldAgentTypes();
        Stopwatch stopwatch = Stopwatch.StartNew();
        LogNoStacktrace(
            $"[FlowWorld] Initial preparation begin agentTypes=[{string.Join(",", agentTypeIds)}] agents={Agents.Count} " +
            $"config(sectorMm={Config.SectorWorldSizeMillimeters}, tileCacheLimit={Config.FlowTileCacheLimit})");
        int builtCount = 0;
        foreach (int agentTypeId in agentTypeIds)
        {
            bool hadWorld = WorldStates.TryGetValue(agentTypeId, out WorldRuntimeState existingState)
                            && existingState.World != null
                            && !existingState.IsDirty;
            if (!TryEnsureWorldBuilt(agentTypeId, allowSynchronousBuild: true))
            {
                throw new InvalidOperationException(
                    $"PrepareInitialNavigationWorlds failed: terrain source unavailable for agentType={agentTypeId}.");
            }

            if (!hadWorld)
                builtCount++;
        }

        int runtimeCompletedCount = CompleteRuntimeRebuildQueue();
        RequireRuntimeNavigationReady("initial-navigation-preparation");
        // Build the occupancy index after all initial entities are committed and
        // navigation worlds are ready, before the first gameplay logic tick.
        EnsureNavigationGoalOccupancyBuckets();
        stopwatch.Stop();
        LogNoStacktrace(
            $"[FlowWorld] Initial preparation end elapsed={stopwatch.Elapsed.TotalMilliseconds:F3}ms " +
            $"built={builtCount} runtimeCompleted={runtimeCompletedCount} " +
            $"worldStates={WorldStates.Count} activeWorld={(_world != null ? _world.Version.ToString() : "null")}");
    }

    public static int RequireRuntimeNavigationReady(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
            throw new ArgumentException("Navigation readiness context is empty.", nameof(context));
        if (_navigationWorkBudgetActive)
            throw new InvalidOperationException($"Navigation readiness failed for '{context}': navigation work budget is active.");

        List<int> requiredAgentTypeIds = CollectNavigationWorldAgentTypes();
        for (int i = 0; i < requiredAgentTypeIds.Count; i++)
        {
            int agentTypeId = requiredAgentTypeIds[i];
            if (!WorldStates.TryGetValue(agentTypeId, out WorldRuntimeState requiredState) || requiredState == null)
            {
                throw new InvalidOperationException(
                    $"Navigation readiness failed for '{context}': required world is missing. agentType={agentTypeId}.");
            }
        }

        if (WorldStates.Count == 0)
            throw new InvalidOperationException($"Navigation readiness failed for '{context}': no navigation worlds are registered.");

        var registeredAgentTypeIds = new List<int>(WorldStates.Keys);
        registeredAgentTypeIds.Sort();
        for (int i = 0; i < registeredAgentTypeIds.Count; i++)
        {
            int agentTypeId = registeredAgentTypeIds[i];
            WorldRuntimeState state = WorldStates[agentTypeId];
            if (state == null)
                throw new InvalidOperationException($"Navigation readiness failed for '{context}': world state is null. agentType={agentTypeId}.");
            if (state.IsDirty || state.World == null || state.BuildJob != null)
            {
                throw new InvalidOperationException(
                    $"Navigation readiness failed for '{context}': world build is pending. " +
                    $"agentType={agentTypeId}, isDirty={state.IsDirty}, hasWorld={state.World != null}, hasBuildJob={state.BuildJob != null}.");
            }
            if (state.DirtyRuntimeObstacleSectors.Count != 0 || state.RuntimeDirtyJob != null)
            {
                throw new InvalidOperationException(
                    $"Navigation readiness failed for '{context}': runtime obstacle update is pending. " +
                    $"agentType={agentTypeId}, dirtySectors={state.DirtyRuntimeObstacleSectors.Count}, " +
                    $"hasRuntimeJob={state.RuntimeDirtyJob != null}, diagnostics={BuildRuntimeDirtyJobDiagnostics()}.");
            }
        }

        return registeredAgentTypeIds.Count;
    }

    public static bool IsRuntimeNavigationReadyForPhaseCommand()
    {
        if (_navigationWorkBudgetActive)
            return false;

        List<int> requiredAgentTypeIds = CollectNavigationWorldAgentTypes();
        if (requiredAgentTypeIds.Count == 0 || WorldStates.Count == 0)
            throw new InvalidOperationException("Phase navigation readiness failed: no required navigation worlds are registered.");
        for (int i = 0; i < requiredAgentTypeIds.Count; i++)
        {
            int agentTypeId = requiredAgentTypeIds[i];
            if (!WorldStates.TryGetValue(agentTypeId, out WorldRuntimeState requiredState) || requiredState == null)
                throw new InvalidOperationException($"Phase navigation readiness failed: required world is missing. agentType={agentTypeId}.");
        }

        foreach (KeyValuePair<int, WorldRuntimeState> pair in WorldStates)
        {
            WorldRuntimeState state = pair.Value
                ?? throw new InvalidOperationException($"Phase navigation readiness failed: world state is null. agentType={pair.Key}.");
            if (state.World == null && state.BuildJob == null && !state.IsDirty)
                throw new InvalidOperationException($"Phase navigation readiness failed: world is missing without a build job. agentType={pair.Key}.");
            if (state.IsDirty || state.World == null || state.BuildJob != null
                || state.DirtyRuntimeObstacleSectors.Count != 0 || state.RuntimeDirtyJob != null)
                return false;
        }
        return true;
    }

    public static bool HasActiveNavigationAgents()
    {
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            if (agent == null || agent.IgnoreAgentCollision)
                continue;
            if (agent.HasNavigationIntent)
                return true;
        }

        return false;
    }

    private static List<int> CollectNavigationWorldAgentTypes()
    {
        List<int> agentTypeIds = NavigationWorldAgentTypeIdsScratch;
        HashSet<int> seen = NavigationWorldAgentTypeIdsSeenScratch;
        agentTypeIds.Clear();
        seen.Clear();

        if (AuthoredTerrainSources.Count > 1
            || (AuthoredTerrainSources.Count == 1 && !AuthoredTerrainSources.ContainsKey(AnyAgentTypeId)))
        {
            foreach (int agentTypeId in AuthoredTerrainSources.Keys)
                AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(agentTypeId));

            return SortNavigationWorldAgentTypes(agentTypeIds);
        }

#if UNITY_EDITOR
        if (_testTerrainOverride != null)
        {
            if (_testTerrainOverride.AgentTypeId != AnyAgentTypeId)
            {
                AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(_testTerrainOverride.AgentTypeId));
                return SortNavigationWorldAgentTypes(agentTypeIds);
            }

            AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(0));
            foreach (AgentRuntimeData agent in Agents.Values)
                AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(agent.AgentTypeId));

            return SortNavigationWorldAgentTypes(agentTypeIds);
        }
#endif

        if (AuthoredTerrainSources.Count > 0)
        {
            if (!AuthoredTerrainSources.ContainsKey(AnyAgentTypeId))
            {
                foreach (int agentTypeId in AuthoredTerrainSources.Keys)
                    AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(agentTypeId));
            }
            else
            {
                AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(0));
                foreach (AgentRuntimeData agent in Agents.Values)
                    AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(agent.AgentTypeId));
            }

            return SortNavigationWorldAgentTypes(agentTypeIds);
        }

        AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(0));
        foreach (AgentRuntimeData agent in Agents.Values)
            AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(agent.AgentTypeId));

        return SortNavigationWorldAgentTypes(agentTypeIds);
    }

    private static List<int> SortNavigationWorldAgentTypes(List<int> agentTypeIds)
    {
        if (agentTypeIds == null)
            throw new ArgumentNullException(nameof(agentTypeIds));
        agentTypeIds.Sort();
        return agentTypeIds;
    }

    private static void AddNavigationWorldAgentType(List<int> agentTypeIds, HashSet<int> seen, int agentTypeId)
    {
        if (!seen.Add(agentTypeId))
            return;

        int insertIndex = agentTypeIds.BinarySearch(agentTypeId);
        if (insertIndex >= 0)
            throw new InvalidOperationException($"AddNavigationWorldAgentType failed: duplicate agent type {agentTypeId} escaped the seen set.");
        agentTypeIds.Insert(~insertIndex, agentTypeId);
    }

    private static void MarkRuntimeObstacleDirty(FixVector2 boundsMinimum, FixVector2 boundsMaximum)
    {
        if (boundsMinimum.x > boundsMaximum.x || boundsMinimum.y > boundsMaximum.y)
            throw new ArgumentOutOfRangeException(nameof(boundsMinimum), "Runtime obstacle bounds minimum exceeds maximum.");

        _lastRuntimeObstacleDirtyReason =
            $"boundsMinRaw=({boundsMinimum.x.RawValue},{boundsMinimum.y.RawValue}) boundsMaxRaw=({boundsMaximum.x.RawValue},{boundsMaximum.y.RawValue}) " +
            $"circleCount={CircleObstacles.Count} boxCount={BoxObstacles.Count} costStampCount={CostStamps.Count}";
        if (WorldStates.Count == 0)
        {
            MarkWorldDirty();
            return;
        }

        foreach (KeyValuePair<int, WorldRuntimeState> pair in WorldStates)
        {
            WorldRuntimeState state = pair.Value;
            if (state.IsDirty)
            {
                state.BuildJob = null;
                continue;
            }

            if (state.World == null)
                continue;

            int dirtySectorCountBefore = state.DirtyRuntimeObstacleSectors.Count;
            CollectDirtySectorsFixed(state.World, boundsMinimum, boundsMaximum, state.DirtyRuntimeObstacleSectors, includeNeighbors: true);
            if (state.DirtyRuntimeObstacleSectors.Count != dirtySectorCountBefore)
                InvalidateRuntimeDirtyJob(state);
        }
    }

    public static void ProcessRuntimeRebuildQueue()
    {
        BeginPerfCall();
        long queueStartTicks = Stopwatch.GetTimestamp();
        try
        {
            const long deadlineTicks = 0L;

            RuntimeRebuildQueueScratch.Clear();
            foreach (WorldRuntimeState state in WorldStates.Values)
            {
                if (state == null || state.IsDirty || state.World == null)
                    continue;
                if (state.DirtyRuntimeObstacleSectors.Count == 0 && state.RuntimeDirtyJob == null)
                    continue;

                RuntimeRebuildQueueScratch.Add(state);
            }

            RuntimeRebuildQueueScratch.Sort((left, right) =>
            {
                int priorityOrder = ResolveRuntimeDirtyPriority(right).CompareTo(ResolveRuntimeDirtyPriority(left));
                return priorityOrder != 0 ? priorityOrder : left.AgentTypeId.CompareTo(right.AgentTypeId);
            });
            for (int i = 0; i < RuntimeRebuildQueueScratch.Count; i++)
            {
                WorldRuntimeState state = RuntimeRebuildQueueScratch[i];
                BeginNavigationWorkBudget(Config.RuntimeRebuildOperationQuota);
                try
                {
                    if (!EnsureRuntimeDirtyJob(state))
                        continue;
                    RuntimeDirtyRebuildJob job = state.RuntimeDirtyJob;
                    RuntimeDirtyRebuildStage stageBefore = job.Stage;
                    long jobStartTicks = Stopwatch.GetTimestamp();
                    ProcessRuntimeDirtyJob(state, deadlineTicks, forceComplete: false);
                    long jobElapsedTicks = Stopwatch.GetTimestamp() - jobStartTicks;
                    if (jobElapsedTicks > _editorSlowestRuntimeDirtyCallTicks)
                    {
                        _editorSlowestRuntimeDirtyCallTicks = jobElapsedTicks;
                        _editorSlowestRuntimeDirtyCallDiagnostics =
                            $"elapsedMs={TicksToMilliseconds(jobElapsedTicks):F3},agent={state.AgentTypeId}," +
                            $"stage={stageBefore}->{job.Stage},cloneShellStage={job.CloneShellStage},cloneSectorCursor={job.CloneSectorCursor}," +
                            $"islandStage={job.IslandStage},islandSector={job.IslandSectorCursor},islandBoundary={job.IslandBoundaryCursor}," +
                            $"portalStage={job.PortalStage},portalSectorCursor={job.PortalSectorCursor}," +
                            BuildRuntimeDirtyStageAccumulatedTiming(job) + "," +
                            BuildRuntimeDirtyCommitTiming(job);
                    }
                }
                finally
                {
                    EndNavigationWorkBudget();
                }
            }
        }
        finally
        {
            _perf.RuntimeRebuildQueueTicks += Stopwatch.GetTimestamp() - queueStartTicks;
        }
    }

    public static int CompleteRuntimeRebuildQueue()
    {
        if (_navigationWorkBudgetActive)
            throw new InvalidOperationException("CompleteRuntimeRebuildQueue failed: navigation work budget is already active.");
        if (WorldStates.Count == 0)
            throw new InvalidOperationException("CompleteRuntimeRebuildQueue failed: no navigation worlds are registered.");

        RuntimeRebuildQueueScratch.Clear();
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state == null)
                throw new InvalidOperationException("CompleteRuntimeRebuildQueue failed: navigation world state is null.");
            if (state.IsDirty || state.World == null)
            {
                throw new InvalidOperationException(
                    $"CompleteRuntimeRebuildQueue failed: navigation world is not ready. agentType={state.AgentTypeId}, isDirty={state.IsDirty}, hasWorld={state.World != null}.");
            }
            if (state.DirtyRuntimeObstacleSectors.Count == 0 && state.RuntimeDirtyJob == null)
                continue;

            RuntimeRebuildQueueScratch.Add(state);
        }

        RuntimeRebuildQueueScratch.Sort((left, right) => left.AgentTypeId.CompareTo(right.AgentTypeId));
        int completedCount = 0;
        for (int i = 0; i < RuntimeRebuildQueueScratch.Count; i++)
        {
            WorldRuntimeState state = RuntimeRebuildQueueScratch[i];
            if (!EnsureRuntimeDirtyJob(state))
                throw new InvalidOperationException($"CompleteRuntimeRebuildQueue failed: pending state has no rebuild job. agentType={state.AgentTypeId}.");

            ProcessRuntimeDirtyJob(state, long.MaxValue, forceComplete: true);
            if (state.RuntimeDirtyJob != null || state.DirtyRuntimeObstacleSectors.Count != 0)
            {
                throw new InvalidOperationException(
                    $"CompleteRuntimeRebuildQueue failed: rebuild remains pending. agentType={state.AgentTypeId}, diagnostics={BuildRuntimeDirtyJobDiagnostics()}.");
            }

            completedCount++;
        }

        return completedCount;
    }

    public static void ProcessWorldBuildQueue()
    {
        BeginPerfCall();
        long queueStartTicks = Stopwatch.GetTimestamp();
        BeginNavigationWorkBudget(Config.WorldBuildOperationQuota);
        try
        {
            if (Config.RequireAuthoredNavigationSource && !HasAuthoredNavigationSource())
            {
                if (HasActiveNavigationAgents())
                {
                    throw new InvalidOperationException(
                        "ProcessWorldBuildQueue failed: active navigation agents exist before any FlowNavigationGridSource has applied a FlowNavigationGridAsset.");
                }

                return;
            }

            const long deadlineTicks = 0L;

            EnsureWorldBuildStatesForKnownAgentTypes();
            RuntimeRebuildQueueScratch.Clear();
            foreach (WorldRuntimeState state in WorldStates.Values)
            {
                if (state == null || !state.IsDirty)
                    continue;

                RuntimeRebuildQueueScratch.Add(state);
            }

            RuntimeRebuildQueueScratch.Sort((left, right) =>
            {
                int priorityOrder = ResolveWorldBuildPriority(right).CompareTo(ResolveWorldBuildPriority(left));
                return priorityOrder != 0 ? priorityOrder : left.AgentTypeId.CompareTo(right.AgentTypeId);
            });
            for (int i = 0; i < RuntimeRebuildQueueScratch.Count; i++)
            {
                WorldRuntimeState state = RuntimeRebuildQueueScratch[i];
                if (!EnsureWorldBuildJob(state))
                    continue;

                ProcessWorldBuildJob(state, deadlineTicks, forceComplete: false);
                if (IsNavigationWorkBudgetExhausted())
                    break;
            }
        }
        finally
        {
            EndNavigationWorkBudget();
            _perf.WorldBuildQueueTicks += Stopwatch.GetTimestamp() - queueStartTicks;
        }
    }

    public static void ProcessFlowTileBuildQueue()
    {
        BeginPerfCall();
        long queueStartTicks = Stopwatch.GetTimestamp();
        NavigationWorld previousWorld = _world;
        WorldRuntimeState previousActiveWorldState = _activeWorldState;
        try
        {
            // Target-side projection owns and validates its own immutable world.
            // Do not make a ready world wait for an unrelated agent-type rebuild.
            const long deadlineTicks = 0L;
            long projectionStartTicks = Stopwatch.GetTimestamp();
            BeginNavigationWorkBudget(Config.MovingTargetProjectionOperationQuota);
            try
            {
                ProcessMovingTargetProjectionQueue();
            }
            finally
            {
                EndNavigationWorkBudget();
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowTileQueueMovingTargetProjection,
                    Stopwatch.GetTimestamp() - projectionStartTicks);
            }

            if (!CanProcessFlowTileBuildQueue())
                return;
            CurrentSteeringFlowTileBuildKeys.Clear();
            NextSteeringFlowTileBuildKeys.Clear();

            FlowBuildQueueWorldScratch.Clear();
            foreach (WorldRuntimeState state in WorldStates.Values)
                FlowBuildQueueWorldScratch.Add(state);
            FlowBuildQueueWorldScratch.Sort((left, right) => left.AgentTypeId.CompareTo(right.AgentTypeId));

            int worldCount = FlowBuildQueueWorldScratch.Count;
            int startIndex = worldCount > 0 ? _flowBuildQueueWorldStartIndex % worldCount : 0;
            for (int reverseOffset = worldCount - 1; reverseOffset >= 0; reverseOffset--)
            {
                int worldIndex = (startIndex + reverseOffset) % worldCount;
                ActivateFlowBuildQueueWorld(FlowBuildQueueWorldScratch[worldIndex]);
                PromoteReadyCommittedCurrentTileAuthorities();

                long phaseTicks = Stopwatch.GetTimestamp();
                EnqueueExplicitSharedGoalPrewarmRequests();
                long phaseElapsedTicks = Stopwatch.GetTimestamp() - phaseTicks;
                _perf.SharedGoalActiveEnqueueTicks += phaseElapsedTicks;
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowTileQueueActiveDemand, phaseElapsedTicks);

                phaseTicks = Stopwatch.GetTimestamp();
                PruneInactivePendingSharedGoalFieldBuildJobs();
                phaseElapsedTicks = Stopwatch.GetTimestamp() - phaseTicks;
                _perf.SharedGoalPruneTicks += phaseElapsedTicks;
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowTileQueuePrune, phaseElapsedTicks);

                phaseTicks = Stopwatch.GetTimestamp();
                EnqueueFlowTileBuildsForActiveAgents(OrderedAgentIds);
                phaseElapsedTicks = Stopwatch.GetTimestamp() - phaseTicks;
                _perf.FlowTileActiveEnqueueTicks += phaseElapsedTicks;
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowTileQueueActiveDemand, phaseElapsedTicks);

                phaseTicks = Stopwatch.GetTimestamp();
                PruneInactivePendingFlowTileBuildJobs(OrderedAgentIds);
                phaseElapsedTicks = Stopwatch.GetTimestamp() - phaseTicks;
                _perf.FlowTilePruneTicks += phaseElapsedTicks;
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowTileQueuePrune, phaseElapsedTicks);

                phaseTicks = Stopwatch.GetTimestamp();
                RefreshFlowTileReferenceCounts();
                TrimTileCache();
                phaseElapsedTicks = Stopwatch.GetTimestamp() - phaseTicks;
                _perf.FlowTileReferenceTicks += phaseElapsedTicks;
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowTileQueueReferenceTrim, phaseElapsedTicks);
            }

            int remainingDeterministicCommits = Config.DeterministicFlowTileCommitQuota;
            int remainingFlowTileOperations = Config.FlowTileBuildOperationQuota;
            for (int offset = 0;
                 offset < worldCount && remainingDeterministicCommits > 0 && remainingFlowTileOperations > 0;
                 offset++)
            {
                int worldIndex = (startIndex + offset) % worldCount;
                ActivateFlowBuildQueueWorld(FlowBuildQueueWorldScratch[worldIndex]);
                long commitStartTicks = Stopwatch.GetTimestamp();
                int committed = CommitPendingDeterministicFlowTilePayloads(
                    remainingDeterministicCommits,
                    ref remainingFlowTileOperations);
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowTileQueueCommit,
                    Stopwatch.GetTimestamp() - commitStartTicks);
                remainingDeterministicCommits -= committed;
            }

            BeginNavigationWorkBudget(Config.SharedGoalBuildOperationQuota);
            try
            {
                for (int offset = 0; offset < worldCount; offset++)
                {
                    int worldIndex = (startIndex + offset) % worldCount;
                    ActivateFlowBuildQueueWorld(FlowBuildQueueWorldScratch[worldIndex]);

                    long phaseTicks = Stopwatch.GetTimestamp();
                    ProcessSharedGoalFieldBuildQueue(deadlineTicks, forceComplete: false, requiredKey: null);
                    long phaseElapsedTicks = Stopwatch.GetTimestamp() - phaseTicks;
                    _perf.SharedGoalProcessTicks += phaseElapsedTicks;
                    MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowTileQueueSharedGoal, phaseElapsedTicks);
                    if (IsNavigationWorkBudgetExhausted())
                        break;
                }
            }
            finally
            {
                EndNavigationWorkBudget();
            }

            if (worldCount > 0)
                _flowBuildQueueWorldStartIndex = (startIndex + 1) % worldCount;

            if (!s_NavigationDistancePrewarmCompleted
                && NavigationDistancePrewarmRequests.Count > 0)
            {
                s_NavigationDistancePrewarmCompletionMayHaveChanged = false;
                RefreshNavigationDistancePrewarmCompletion();
            }
        }
        finally
        {
#if UNITY_EDITOR
            if (PendingFlowTileBuildJobs.Count == 0)
                _editorTestSynchronousFlowTileBuildActive = false;
#endif
            _world = previousWorld;
            _activeWorldState = previousActiveWorldState;
            _perf.FlowTileQueueTicks += Stopwatch.GetTimestamp() - queueStartTicks;
        }
    }

    private static int ResolveWorldBuildPriority(WorldRuntimeState state)
    {
        if (state == null)
            return 0;

        int agentTypeId = state.BuildJob != null ? state.BuildJob.AgentTypeId : state.AgentTypeId;
        int priority = 1;
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            if (ResolvePreferredAgentTypeId(agent.AgentTypeId) == agentTypeId)
                priority += 100;
        }

        return priority;
    }

    private static void EnsureWorldBuildStatesForKnownAgentTypes()
    {
        List<int> agentTypeIds = CollectNavigationWorldAgentTypes();
        for (int i = 0; i < agentTypeIds.Count; i++)
        {
            int agentTypeId = ResolvePreferredAgentTypeId(agentTypeIds[i]);
            GetOrCreateWorldState(agentTypeId);
        }
    }

    private static int ResolveRuntimeDirtyPriority(WorldRuntimeState state)
    {
        if (state == null || state.World == null)
            return int.MinValue;

        HashSet<int> dirtySectors = state.RuntimeDirtyJob != null
            ? state.RuntimeDirtyJob.DirtySectors
            : state.DirtyRuntimeObstacleSectors;
        if (dirtySectors == null || dirtySectors.Count == 0)
            return 0;

        NavigationWorld world = state.World;
        int priority = dirtySectors.Count;
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            if (ResolvePreferredAgentTypeId(agent.AgentTypeId) != world.AgentTypeId)
                continue;

            if (world.WorldToGridFixed(agent.PositionFixed, out int agentX, out int agentY)
                && world.TryGetSectorId(agentX, agentY, out int agentSectorId)
                && dirtySectors.Contains(agentSectorId))
            {
                priority += 1000;
                continue;
            }

            if (PathTouchesAnySector(agent.NavState.PathHandle, dirtySectors))
                priority += 100;
        }

        return priority;
    }

}
