using System;
using System.Collections.Generic;
using System.Diagnostics;
using AAAGame.FlowPath;
using GameFramework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using MainThreadFrameProfiler = UnityGameFramework.Runtime.MainThreadFrameProfiler;
using MainThreadPerfScope = UnityGameFramework.Runtime.MainThreadPerfScope;

public static partial class FlowFieldCrowdMovementSystem
{
    private static bool CanProcessFlowTileBuildQueue()
    {
        if (WorldStates.Count == 0)
            return false;

        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state == null)
                throw new InvalidOperationException("CanProcessFlowTileBuildQueue failed: world state is null.");
            if (state.IsDirty || state.BuildJob != null)
                return false;
            if (state.World == null)
                throw new InvalidOperationException($"CanProcessFlowTileBuildQueue failed: ready world is null agentType={state.AgentTypeId}.");
            if (state.World.AgentTypeId != state.AgentTypeId)
                throw new InvalidOperationException($"CanProcessFlowTileBuildQueue failed: world agent type mismatch state={state.AgentTypeId} world={state.World.AgentTypeId}.");
        }

        return true;
    }

    private static void ActivateFlowBuildQueueWorld(WorldRuntimeState state)
    {
        if (state == null || state.World == null)
            throw new InvalidOperationException("ActivateFlowBuildQueueWorld failed: state or world is null.");
        if (state.IsDirty || state.BuildJob != null)
            throw new InvalidOperationException($"ActivateFlowBuildQueueWorld failed: world is not ready agentType={state.AgentTypeId}.");

        _activeWorldState = state;
        _world = state.World;
    }

    private static NavigationWorld ResolveCommittedNavigationWorldByVersion(int worldVersion)
    {
        if (_world != null && _world.Version == worldVersion)
            return _world;

        NavigationWorld resolved = null;
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            NavigationWorld candidate = state?.World;
            if (candidate == null || candidate.Version != worldVersion)
                continue;
            if (resolved != null && !ReferenceEquals(resolved, candidate))
            {
                throw new InvalidOperationException(
                    $"ResolveCommittedNavigationWorldByVersion failed: duplicate committed world version={worldVersion}.");
            }
            resolved = candidate;
        }
        if (resolved == null)
        {
            throw new InvalidOperationException(
                $"ResolveCommittedNavigationWorldByVersion failed: committed world is absent version={worldVersion}.");
        }
        return resolved;
    }

    private static bool IsAgentInActiveFlowBuildQueueWorld(AgentRuntimeData agent)
    {
        if (agent == null)
            throw new InvalidOperationException("IsAgentInActiveFlowBuildQueueWorld failed: agent is null.");
        if (_activeWorldState == null || _world == null)
            throw new InvalidOperationException("IsAgentInActiveFlowBuildQueueWorld failed: active world is null.");

        return ResolvePreferredAgentTypeId(agent.AgentTypeId) == _activeWorldState.AgentTypeId
               && _world.AgentTypeId == _activeWorldState.AgentTypeId;
    }

    private static bool IsAgentInCurrentNavigationWorld(AgentRuntimeData agent)
    {
        if (agent == null)
            throw new InvalidOperationException("IsAgentInCurrentNavigationWorld failed: agent is null.");
        if (_world == null)
            throw new InvalidOperationException("IsAgentInCurrentNavigationWorld failed: world is null.");

        return ResolvePreferredAgentTypeId(agent.AgentTypeId) == _world.AgentTypeId;
    }

    private static bool IsAgentPathInCurrentNavigationWorld(AgentRuntimeData agent)
    {
        return IsAgentInCurrentNavigationWorld(agent)
               && agent.NavState.PathHandle != null
               && agent.NavState.PathHandle.WorldVersion == _world.Version;
    }

    private static void EnqueueFlowTileBuildsForActiveAgents(IList<int> orderedAgentIds)
    {
        if (orderedAgentIds == null)
            throw new ArgumentNullException(nameof(orderedAgentIds));

        for (int i = 0; i < orderedAgentIds.Count; i++)
        {
            if (!Agents.TryGetValue(orderedAgentIds[i], out AgentRuntimeData agent) || agent == null)
                throw new InvalidOperationException($"EnqueueFlowTileBuildsForActiveAgents failed: ordered agent is missing id={orderedAgentIds[i]}.");
            if (!IsAgentInActiveFlowBuildQueueWorld(agent))
                continue;

            PathHandle handle = agent.NavState.PathHandle;
            if (handle == null || handle.WorldVersion != _world.Version || handle.SectorIds == null || handle.SectorIds.Length == 0)
                continue;
            if (PathReferencesMissingPortal(handle))
                continue;

            EnqueueSteeringReadDomainFlowTileBuilds(
                agent,
                ResolvePreparedMaximumTravelDistanceFixed(agent));
        }
    }

    private static void PruneInactivePendingFlowTileBuildJobs(IList<int> orderedAgentIds)
    {
        if (FlowTileBuildQueue.Count == 0)
            return;

        BuildActiveFlowTileBuildKeySet(orderedAgentIds);
        LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First;
        while (node != null)
        {
            LinkedListNode<FlowTileBuildJob> next = node.Next;
            FlowTileBuildJob job = node.Value;
            FlowTileCacheKey key = job.BuildKey.CacheKey;
            if (key.WorldVersion == _world.Version && !ActiveFlowTileBuildKeys.Contains(key))
            {
                FlowTileBuildQueue.Remove(node);
                PendingFlowTileBuildJobs.Remove(key);
                ReleasePendingFlowTileBuildJobPayloads(job);
                _perf.FlowTileQueuePruned++;
            }

            node = next;
        }
    }

    private static void BuildActiveFlowTileBuildKeySet(IList<int> orderedAgentIds)
    {
        if (orderedAgentIds == null)
            throw new ArgumentNullException(nameof(orderedAgentIds));

        ActiveFlowTileBuildKeys.Clear();
        PendingFlowTileDependencyKeys.Clear();
        if (_world == null)
            return;

        for (int i = 0; i < orderedAgentIds.Count; i++)
        {
            if (!Agents.TryGetValue(orderedAgentIds[i], out AgentRuntimeData agent) || agent == null)
                throw new InvalidOperationException($"BuildActiveFlowTileBuildKeySet failed: ordered agent is missing id={orderedAgentIds[i]}.");
            if (!IsAgentInActiveFlowBuildQueueWorld(agent))
                continue;

            PathHandle handle = agent.NavState.PathHandle;
            if (handle == null
                || handle.WorldVersion != _world.Version
                || handle.SectorIds == null
                || handle.SectorIds.Length == 0
                || PathReferencesMissingPortal(handle))
            {
                continue;
            }

            AddSteeringReadDomainFlowTileBuildKeys(
                agent,
                ResolvePreparedMaximumTravelDistanceFixed(agent));
        }
    }

    private static void EnqueueExplicitSharedGoalPrewarmRequests()
    {
        BuildActiveSharedGoalFieldBuildKeySet();
        ActiveSharedGoalFieldKeyOrderScratch.Clear();
        ActiveSharedGoalFieldKeyOrderScratch.AddRange(ActiveSharedGoalFieldBuildKeys);
        ActiveSharedGoalFieldKeyOrderScratch.Sort(SharedGoalFieldKeyComparison);
        for (int i = 0; i < ActiveSharedGoalFieldKeyOrderScratch.Count; i++)
        {
            SharedGoalFieldKey key = ActiveSharedGoalFieldKeyOrderScratch[i];
            if (ActiveSharedGoalFieldDemandStartCells.TryGetValue(key, out Dictionary<int, int> demandStartCells))
                EnqueueSharedGoalFieldBuild(key, true, demandStartCells);
            else if (ActiveSharedGoalFieldDemandStartSectors.TryGetValue(key, out HashSet<int> demandStartSectors))
                EnqueueSharedGoalFieldBuild(key, true, demandStartSectors);
            else
                EnqueueSharedGoalFieldBuild(key, prependToFront: true);
        }
    }

    private static void BuildActiveSharedGoalFieldBuildKeySet()
    {
        ActiveSharedGoalFieldBuildKeys.Clear();
        ActiveSharedGoalFieldDemandStartSectors.Clear();
        ActiveSharedGoalFieldDemandStartCells.Clear();
        if (_world == null)
            return;

        AddNavigationDistancePrewarmBuildKeysForActiveWorld();
    }

    private static void AddNavigationDistancePrewarmBuildKeysForActiveWorld()
    {
        if (_world == null)
            throw new InvalidOperationException("Navigation distance prewarm requires an active world.");

        for (int i = 0; i < NavigationDistancePrewarmRequests.Count; i++)
        {
            NavigationDistancePrewarmRequest request = NavigationDistancePrewarmRequests[i];
            if (request.AgentTypeId != _world.AgentTypeId)
                continue;
            if (request.ResolvedWorldVersion != _world.Version
                || request.ResolvedTopologyVersion != _navigationTopologyVersion)
            {
                if (!TryResolveReachableNavigationQueryCellsFixed(
                        _world,
                        request.From,
                        request.RawGoal,
                        out request.StartX,
                        out request.StartY,
                        out request.GoalX,
                        out request.GoalY,
                        out _,
                        out string failureReason)
                    || !_world.TryGetSectorId(request.StartX, request.StartY, out request.StartSectorId)
                    || !_world.TryGetSectorId(request.GoalX, request.GoalY, out request.GoalSectorId))
                {
                    throw new InvalidOperationException(
                        $"Navigation distance prewarm cannot resolve route agentType={request.AgentTypeId} " +
                        $"fromRaw=({request.From.x.RawValue},{request.From.y.RawValue}) " +
                        $"goalRaw=({request.RawGoal.x.RawValue},{request.RawGoal.y.RawValue}) reason={failureReason}.");
                }

                request.ResolvedWorldVersion = _world.Version;
                request.ResolvedTopologyVersion = _navigationTopologyVersion;
                s_NavigationDistancePrewarmCompletionMayHaveChanged = true;
            }

            AddActiveSharedGoalFieldBuildKey(
                CreateSharedGoalFieldKey(request.GoalSectorId, request.GoalX, request.GoalY, request.AgentTypeId),
                request.StartSectorId,
                request.StartX,
                request.StartY);
        }
    }

    private static int CompareNavigationDistancePrewarmRequests(
        NavigationDistancePrewarmRequest left,
        NavigationDistancePrewarmRequest right)
    {
        int order = left.AgentTypeId.CompareTo(right.AgentTypeId);
        if (order != 0) return order;
        order = left.From.x.RawValue.CompareTo(right.From.x.RawValue);
        if (order != 0) return order;
        order = left.From.y.RawValue.CompareTo(right.From.y.RawValue);
        if (order != 0) return order;
        order = left.RawGoal.x.RawValue.CompareTo(right.RawGoal.x.RawValue);
        return order != 0 ? order : left.RawGoal.y.RawValue.CompareTo(right.RawGoal.y.RawValue);
    }

    private static void RefreshNavigationDistancePrewarmCompletion()
    {
        if (s_NavigationDistancePrewarmCompleted || NavigationDistancePrewarmRequests.Count == 0)
            return;

        for (int i = 0; i < NavigationDistancePrewarmRequests.Count; i++)
        {
            NavigationDistancePrewarmRequest request = NavigationDistancePrewarmRequests[i];
            if (!WorldStates.TryGetValue(request.AgentTypeId, out WorldRuntimeState state)
                || state == null
                || state.IsDirty
                || state.BuildJob != null
                || state.World == null
                || request.ResolvedWorldVersion != state.World.Version
                || request.ResolvedTopologyVersion != _navigationTopologyVersion)
            {
                return;
            }

            NavigationWorld world = state.World;
            if (request.StartX == request.GoalX && request.StartY == request.GoalY)
                continue;
            if (request.StartSectorId == request.GoalSectorId)
                continue;

            var key = new SharedGoalFieldKey(
                world.Version,
                request.AgentTypeId,
                request.GoalSectorId,
                world.GetIndex(request.GoalX, request.GoalY),
                world.Sectors[request.GoalSectorId].DirtyVersion);
            if (!SharedGoalFields.TryGetValue(key, out SharedGoalField field) || field == null)
            {
                if (PendingSharedGoalFieldBuildJobs.Contains(key))
                    return;
                throw new InvalidOperationException(
                    $"Navigation distance prewarm failed to build a route agentType={request.AgentTypeId} " +
                    $"start=({request.StartX},{request.StartY}) goal=({request.GoalX},{request.GoalY}).");
            }

            int startCellIndex = world.GetIndex(request.StartX, request.StartY);
            if (!IsSharedGoalFieldDemandCovered(
                    world,
                    field,
                    request.StartSectorId,
                    startCellIndex))
                return;
        }

        for (int i = 0; i < NavigationDistancePrewarmRequests.Count; i++)
        {
            NavigationDistancePrewarmRequest request = NavigationDistancePrewarmRequests[i];
            if (!TryEstimatePrewarmedNavigationDistanceToReachableGoalFixed(
                    request.From,
                    request.RawGoal,
                    request.AgentTypeId,
                    out _,
                    out _,
                    out string failureReason))
            {
                throw new InvalidOperationException(
                    $"Navigation distance prewarm completed without a queryable route agentType={request.AgentTypeId} " +
                    $"fromRaw=({request.From.x.RawValue},{request.From.y.RawValue}) " +
                    $"goalRaw=({request.RawGoal.x.RawValue},{request.RawGoal.y.RawValue}) reason={failureReason}.");
            }
        }

        s_NavigationDistancePrewarmCompleted = true;
        EventHandler<NavigationDistancePrewarmCompletedEventArgs> handler = NavigationDistancePrewarmCompleted;
        if (handler == null)
            return;

        NavigationDistancePrewarmCompletedEventArgs eventArgs =
            ReferencePool.Acquire<NavigationDistancePrewarmCompletedEventArgs>();
        eventArgs.RequestCount = NavigationDistancePrewarmRequests.Count;
        try
        {
            handler(null, eventArgs);
        }
        finally
        {
            ReferencePool.Release(eventArgs);
        }
    }

    private static void AddActiveSharedGoalFieldBuildKey(SharedGoalFieldKey key, int demandStartSectorId)
    {
        ActiveSharedGoalFieldBuildKeys.Add(key);
        if (demandStartSectorId < 0)
            return;

        if (!ActiveSharedGoalFieldDemandStartSectors.TryGetValue(key, out HashSet<int> demandStartSectors))
        {
            demandStartSectors = new HashSet<int>();
            ActiveSharedGoalFieldDemandStartSectors.Add(key, demandStartSectors);
        }

        demandStartSectors.Add(demandStartSectorId);
    }

    private static void AddActiveSharedGoalFieldBuildKey(SharedGoalFieldKey key, int demandStartSectorId, int demandStartX, int demandStartY)
    {
        AddActiveSharedGoalFieldBuildKey(key, demandStartSectorId);
        if (demandStartSectorId < 0
            || demandStartX < 0
            || demandStartX >= _world.Width
            || demandStartY < 0
            || demandStartY >= _world.Height)
        {
            return;
        }

        if (!ActiveSharedGoalFieldDemandStartCells.TryGetValue(key, out Dictionary<int, int> demandStartCells))
        {
            demandStartCells = new Dictionary<int, int>();
            ActiveSharedGoalFieldDemandStartCells.Add(key, demandStartCells);
        }

        demandStartCells[_world.GetIndex(demandStartX, demandStartY)] = demandStartSectorId;
    }

    private static void PruneInactivePendingSharedGoalFieldBuildJobs()
    {
        if (SharedGoalFieldBuildQueue.Count == 0)
            return;

        LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First;
        while (node != null)
        {
            LinkedListNode<SharedGoalFieldBuildJob> next = node.Next;
            SharedGoalFieldBuildJob job = node.Value;
            if (job == null)
            {
                SharedGoalFieldBuildQueue.Remove(node);
                if (job != null)
                    PendingSharedGoalFieldBuildJobs.Remove(job.Key);
            }

            node = next;
        }
    }

    private static void ProcessSharedGoalFieldBuildQueue(long deadlineTicks, bool forceComplete, SharedGoalFieldKey? requiredKey)
    {
        if (_world == null)
            throw new InvalidOperationException("ProcessSharedGoalFieldBuildQueue failed: world is null.");
        if (requiredKey.HasValue && requiredKey.Value.WorldVersion != _world.Version)
            throw new InvalidOperationException($"ProcessSharedGoalFieldBuildQueue failed: required key world mismatch required={requiredKey.Value.WorldVersion} active={_world.Version}.");

        int guard = 0;
        while (true)
        {
            if (!forceComplete && IsDeadlineExpired(deadlineTicks))
                return;

            LinkedListNode<SharedGoalFieldBuildJob> jobNode = FindSharedGoalFieldBuildJobForActiveWorld();
            if (jobNode == null)
                return;

            SharedGoalFieldBuildJob job = jobNode.Value;
            SharedGoalFieldBuildQueue.Remove(jobNode);
            PendingSharedGoalFieldBuildJobs.Remove(job.Key);

            if (SharedGoalFields.ContainsKey(job.Key))
            {
                if (requiredKey.HasValue && SharedGoalFields.ContainsKey(requiredKey.Value))
                    return;
                continue;
            }

            if (IsSharedGoalFieldBuildJobStale(job))
            {
                if (requiredKey.HasValue && SharedGoalFields.ContainsKey(requiredKey.Value))
                    return;
                continue;
            }

            if (!AdvanceSharedGoalFieldBuildJob(job, deadlineTicks, forceComplete))
            {
                SharedGoalFieldBuildQueue.AddFirst(job);
                PendingSharedGoalFieldBuildJobs.Add(job.Key);
                return;
            }

            if (requiredKey.HasValue && SharedGoalFields.ContainsKey(requiredKey.Value))
                return;
            if (!forceComplete && IsBudgetExpired(deadlineTicks, 0))
                return;

            guard++;
            if (guard > Config.FlowTileCacheLimit * 4 + 1024)
                throw new InvalidOperationException("ProcessSharedGoalFieldBuildQueue failed: queue processing exceeded guard.");
        }
    }

    private static bool IsSharedGoalFieldBuildJobStale(SharedGoalFieldBuildJob job)
    {
        if (job == null || _world == null)
            return true;
        if (job.GoalSectorId < 0 || job.GoalSectorId >= _world.Sectors.Length)
            return true;

        SharedGoalFieldKey expectedKey = CreateSharedGoalFieldKey(job.GoalSectorId, job.GoalX, job.GoalY, job.AgentTypeId);
        return !expectedKey.Equals(job.Key);
    }

    private static LinkedListNode<SharedGoalFieldBuildJob> FindSharedGoalFieldBuildJobForActiveWorld()
    {
        for (LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First; node != null; node = node.Next)
        {
            SharedGoalFieldBuildJob job = node.Value;
            if (job != null && job.Key.WorldVersion == _world.Version)
                return node;
        }

        return null;
    }

    private static int CommitPendingDeterministicFlowTilePayloads(int maxCount, ref int remainingOperations)
    {
        if (maxCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, "Deterministic flow tile commit quota must be positive.");
        if (_world == null)
            throw new InvalidOperationException("CommitPendingDeterministicFlowTilePayloads failed: world is null.");
        if (remainingOperations <= 0)
            return 0;

        CurrentFlowTileBuildJobScratch.Clear();
        NextFlowTileBuildJobScratch.Clear();
        BackgroundFlowTileBuildJobScratch.Clear();
        DependencyFlowTileBuildJobScratch.Clear();
        for (LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First; node != null; node = node.Next)
        {
            FlowTileBuildJob job = node.Value
                ?? throw new InvalidOperationException("CommitPendingDeterministicFlowTilePayloads encountered a null job.");
            if (job.BuildKey.CacheKey.WorldVersion != _world.Version || IsFlowTileBuildJobStale(job))
                continue;

            FlowTileCacheKey key = job.BuildKey.CacheKey;
            if (key.GoalKind == TileGoalKind.FinalGoal)
                DependencyFlowTileBuildJobScratch.Add(node);
            else if (CurrentSteeringFlowTileBuildKeys.Contains(key))
                CurrentFlowTileBuildJobScratch.Add(node);
            else if (NextSteeringFlowTileBuildKeys.Contains(key))
                NextFlowTileBuildJobScratch.Add(node);
            else
                BackgroundFlowTileBuildJobScratch.Add(node);
        }

        int committedCount = 0;
#if UNITY_EDITOR
        if (DependencyFlowTileBuildJobScratch.Count > 0)
        {
            for (int i = 0; i < DependencyFlowTileBuildJobScratch.Count; i++)
            {
                FlowTileBuildJob dependencyJob = DependencyFlowTileBuildJobScratch[i].Value;
                if (dependencyJob.Stage == FlowTileBuildStage.Integrate
                    && dependencyJob.HasIntegrationDirectionsHandle
                    && !dependencyJob.IntegrationDirectionsHandle.IsCompleted)
                {
                    dependencyJob.IntegrationDirectionsHandle.Complete();
                }
            }
        }
#endif
        int dependencyOperationSlice = DependencyFlowTileBuildJobScratch.Count > 0
            ? Math.Max(1, remainingOperations / DependencyFlowTileBuildJobScratch.Count)
            : 0;
#if UNITY_EDITOR
        if (_editorTestSynchronousFlowTileBuildActive && DependencyFlowTileBuildJobScratch.Count > 0)
        {
            int synchronousDependencyBudget = int.MaxValue;
            _synchronousDependencyBuildActive = true;
            try
            {
                committedCount += ProcessFlowTileBuildJobScratch(
                    DependencyFlowTileBuildJobScratch,
                    maxCount - committedCount,
                    int.MaxValue,
                    ref synchronousDependencyBudget);
            }
            finally
            {
                _synchronousDependencyBuildActive = false;
            }
        }
        else
#endif
        {
            committedCount += ProcessFlowTileBuildJobScratch(
                DependencyFlowTileBuildJobScratch,
                maxCount - committedCount,
                dependencyOperationSlice,
                ref remainingOperations);
        }
        int steeringJobCount = CurrentFlowTileBuildJobScratch.Count + NextFlowTileBuildJobScratch.Count;
        int steeringOperationSlice = steeringJobCount > 0
            ? Math.Max(1, remainingOperations / steeringJobCount)
            : 0;
        committedCount += ProcessFlowTileBuildJobScratch(
            CurrentFlowTileBuildJobScratch,
            maxCount - committedCount,
            steeringOperationSlice,
            ref remainingOperations);
        if (committedCount < maxCount && remainingOperations > 0)
        {
            committedCount += ProcessFlowTileBuildJobScratch(
                NextFlowTileBuildJobScratch,
                maxCount - committedCount,
                steeringOperationSlice,
                ref remainingOperations);
        }
        if (committedCount < maxCount && remainingOperations > 0)
        {
            int backgroundOperationSlice = BackgroundFlowTileBuildJobScratch.Count > 0
                ? Math.Max(1, remainingOperations / BackgroundFlowTileBuildJobScratch.Count)
                : 0;
            committedCount += ProcessFlowTileBuildJobScratch(
                BackgroundFlowTileBuildJobScratch,
                maxCount - committedCount,
                backgroundOperationSlice,
                ref remainingOperations);
        }

        if (committedCount > 0)
        {
            long referenceStartTicks = Stopwatch.GetTimestamp();
            PromoteReadyCommittedCurrentTileAuthorities();
            RefreshFlowTileReferenceCounts();
            TrimTileCache();
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowTileCommitReferenceTrim, Stopwatch.GetTimestamp() - referenceStartTicks);
        }
        ReorderFlowTileBuildQueueBySteeringPriority();

        return committedCount;
    }

    private static void ReorderFlowTileBuildQueueBySteeringPriority()
    {
        if (FlowTileBuildQueue.Count <= 1)
            return;

        CurrentFlowTileBuildJobScratch.Clear();
        NextFlowTileBuildJobScratch.Clear();
        BackgroundFlowTileBuildJobScratch.Clear();
        DependencyFlowTileBuildJobScratch.Clear();
        for (LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First; node != null; node = node.Next)
        {
            FlowTileCacheKey key = node.Value.BuildKey.CacheKey;
            if (CurrentSteeringFlowTileBuildKeys.Contains(key))
                CurrentFlowTileBuildJobScratch.Add(node);
            else if (NextSteeringFlowTileBuildKeys.Contains(key))
                NextFlowTileBuildJobScratch.Add(node);
            else
                BackgroundFlowTileBuildJobScratch.Add(node);
        }

        AppendFlowTileBuildJobNodes(CurrentFlowTileBuildJobScratch);
        AppendFlowTileBuildJobNodes(NextFlowTileBuildJobScratch);
        AppendFlowTileBuildJobNodes(BackgroundFlowTileBuildJobScratch);
    }

    private static void ClearFlowTileBuildSchedulingState()
    {
        CurrentSteeringFlowTileBuildKeys.Clear();
        NextSteeringFlowTileBuildKeys.Clear();
        CurrentFlowTileBuildJobScratch.Clear();
        NextFlowTileBuildJobScratch.Clear();
        BackgroundFlowTileBuildJobScratch.Clear();
    }

    private static void AppendFlowTileBuildJobNodes(List<LinkedListNode<FlowTileBuildJob>> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            LinkedListNode<FlowTileBuildJob> node = nodes[i];
            if (node?.List != FlowTileBuildQueue)
                throw new InvalidOperationException("Flow tile priority ordering contains a detached queue node.");
            FlowTileBuildQueue.Remove(node);
            FlowTileBuildQueue.AddLast(node);
        }
    }

    private static int ProcessFlowTileBuildJobScratch(
        List<LinkedListNode<FlowTileBuildJob>> jobs,
        int maximumCommits,
        int operationSlice,
        ref int remainingOperations)
    {
        if (jobs == null)
            throw new ArgumentNullException(nameof(jobs));
        if (maximumCommits <= 0 || operationSlice <= 0 || remainingOperations <= 0)
            return 0;

        int committedCount = 0;
        for (int i = 0; i < jobs.Count && committedCount < maximumCommits && remainingOperations > 0; i++)
        {
            LinkedListNode<FlowTileBuildJob> node = jobs[i];
            if (node?.List != FlowTileBuildQueue)
                throw new InvalidOperationException("Flow tile build scheduling scratch contains a detached queue node.");
            FlowTileBuildJob job = node.Value
                ?? throw new InvalidOperationException("Flow tile build scheduling scratch contains a null job.");

            if (job.Stage == FlowTileBuildStage.Integrate
                && job.HasIntegrationDirectionsHandle
                && !job.IntegrationDirectionsHandle.IsCompleted)
            {
                // A worker-owned tile remains pending until its immutable output is ready.
                // Do not burn logical quota or synchronously complete it on the main thread.
                if (_editorTestSynchronousFlowTileBuildActive)
                    job.IntegrationDirectionsHandle.Complete();
                else if (!_synchronousDependencyBuildActive)
                    continue;
            }

            long scanStartTicks = Stopwatch.GetTimestamp();
#if UNITY_EDITOR
            bool synchronousEditorBuild = _editorTestSynchronousFlowTileBuildActive;
#else
            bool synchronousEditorBuild = false;
#endif
            int jobOperations = synchronousEditorBuild
                ? int.MaxValue
                : Math.Min(operationSlice, remainingOperations);
            int initialJobOperations = jobOperations;
            bool completed = AdvanceDeterministicFlowTileBuildJob(job, ref jobOperations);
            int consumedOperations = initialJobOperations - jobOperations;
            if (consumedOperations <= 0)
            {
                throw new InvalidOperationException(
                    $"Flow tile build job consumed no operation budget key={FormatTileKey(job.BuildKey.CacheKey)}, stage={job.Stage}.");
            }
            if (!synchronousEditorBuild)
                remainingOperations -= consumedOperations;
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowTileCommitQueueScan, Stopwatch.GetTimestamp() - scanStartTicks);

            FlowTileBuildQueue.Remove(node);
            if (completed)
            {
                if (!PendingFlowTileBuildJobs.Remove(job.BuildKey.CacheKey))
                {
                    throw new InvalidOperationException(
                        $"CommitPendingDeterministicFlowTilePayloads failed: committed job was absent from pending set key={FormatTileKey(job.BuildKey.CacheKey)}.");
                }
                committedCount++;
            }
            else
            {
                FlowTileBuildQueue.AddLast(node);
            }
        }

        return committedCount;
    }

    private static LinkedListNode<FlowTileBuildJob> FindPendingFlowTileBuildJob(FlowTileCacheKey key)
    {
        for (LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First; node != null; node = node.Next)
        {
            FlowTileBuildJob job = node.Value;
            if (job == null)
                throw new InvalidOperationException("FindPendingFlowTileBuildJob encountered a null job.");
            if (job.BuildKey.CacheKey.Equals(key))
                return node;
        }

        return null;
    }

    private static bool AdvanceDeterministicFlowTileBuildJob(FlowTileBuildJob job, ref int remainingOperations)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));
        if (remainingOperations <= 0)
            return false;

        while (remainingOperations > 0)
        {
            switch (job.Stage)
            {
                case FlowTileBuildStage.Initialize:
                    InitializeDeterministicFlowTileBuildJob(job);
                    remainingOperations--;
                    break;
                case FlowTileBuildStage.PotentialShapeLookup:
                    ResolveDeterministicPortalPotentialShape(job);
                    remainingOperations--;
                    break;
                case FlowTileBuildStage.InitializeIntegration:
                    AdvanceDeterministicFlowTileIntegrationInitialization(job);
                    remainingOperations--;
                    break;
                case FlowTileBuildStage.SeedIntegration:
                    AdvanceDeterministicFlowTileSeed(job);
                    remainingOperations--;
                    break;
                case FlowTileBuildStage.MaterializePotentialShape:
                    AdvanceFlowTilePotentialShapeMaterialization(job);
                    remainingOperations--;
                    break;
                case FlowTileBuildStage.Integrate:
                {
                    if (!job.HasIntegrationDirectionsHandle)
                        throw new InvalidOperationException($"Flow tile integration stage has no scheduled job key={FormatTileKey(job.BuildKey.CacheKey)}.");
                    if (!job.IntegrationDirectionsHandle.IsCompleted)
                    {
                        if (!_editorTestSynchronousFlowTileBuildActive && !_synchronousDependencyBuildActive)
                            return false;
                        job.IntegrationDirectionsHandle.Complete();
                    }
                    long phaseStartTicks = Stopwatch.GetTimestamp();
                    job.IntegrationDirectionsHandle.Complete();
                    if (job.StatusNative[0] != 0)
                    {
                        int status = job.StatusNative[0];
                        throw new InvalidOperationException(
                            $"Flow tile integration job failed status={status} key={FormatTileKey(job.BuildKey.CacheKey)}.");
                    }
                    job.Cursor = 0;
                    job.Stage = FlowTileBuildStage.IntegrationDirectionsCopy;
                    MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowTileCommitDirections, Stopwatch.GetTimestamp() - phaseStartTicks);
                    remainingOperations--;
                    break;
                }
                case FlowTileBuildStage.IntegrationDirectionsPending:
                    throw new InvalidOperationException($"Flow tile integration pending stage was advanced without a job key={FormatTileKey(job.BuildKey.CacheKey)}.");
                case FlowTileBuildStage.IntegrationDirectionsCopy:
                {
                    CopyCompletedDeterministicFlowTilePayload(job);
                    remainingOperations--;
                    break;
                }
                case FlowTileBuildStage.Directions:
                    throw new InvalidOperationException($"Flow tile directions stage is obsolete after worker scheduling key={FormatTileKey(job.BuildKey.CacheKey)}.");
                case FlowTileBuildStage.PortalSlots:
                    AdvanceDeterministicPortalSlotPass(job);
                    remainingOperations--;
                    break;
                case FlowTileBuildStage.PotentialShapeHash:
                    AdvanceDeterministicPortalPotentialShapeHash(job);
                    remainingOperations--;
                    break;
                case FlowTileBuildStage.DiagnosticShadow:
                {
                    long phaseStartTicks = Stopwatch.GetTimestamp();
                    AdvanceFlowTileDiagnosticShadow(job);
                    MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowTileCommitDiagnosticShadow, Stopwatch.GetTimestamp() - phaseStartTicks);
                    remainingOperations--;
                    break;
                }
                case FlowTileBuildStage.AuthorityHash:
                    AdvanceDeterministicFlowTileAuthorityHash(job);
                    remainingOperations--;
                    break;
                case FlowTileBuildStage.Commit:
                    CommitCompletedDeterministicFlowTileBuildJob(job);
                    remainingOperations--;
                    break;
                case FlowTileBuildStage.Complete:
                    return true;
                default:
                    throw new ArgumentOutOfRangeException(nameof(job.Stage), job.Stage, "Unknown flow tile build stage.");
            }
            _perf.FlowTileBuildOperations++;
        }

        return job.Stage == FlowTileBuildStage.Complete;
    }

    private static void InitializeDeterministicFlowTileBuildJob(FlowTileBuildJob job)
    {
        FlowTileCacheKey key = job.BuildKey.CacheKey;
        SectorData sector = _world.Sectors[key.SectorId];
        Vector2Int[] goalCells = ResolveGoalCells(sector, key, job.BuildKey.GoalX, job.BuildKey.GoalY);
        if (goalCells == null || goalCells.Length == 0)
            throw new InvalidOperationException($"InitializeDeterministicFlowTileBuildJob failed: tile has no goal cells key={FormatTileKey(key)}.");

        job.Tile = new FlowTileCacheEntry
        {
            Key = key,
            StartX = sector.StartX,
            StartY = sector.StartY,
            Width = sector.Width,
            Height = sector.Height,
            GoalCells = goalCells,
            UsesClearFlowDescriptor = sector.IsClearFlowTile
        };
        int count = job.Tile.Width * job.Tile.Height;
        job.Tile.DeterministicIntegrationCosts = new int[count];
        job.Tile.DeterministicFlowDirectionIndices = new byte[count];
        job.GoalSeedCosts = new int[goalCells.Length];
        job.Cursor = 0;
        if (key.GoalKind == TileGoalKind.FinalGoal)
        {
            job.Stage = FlowTileBuildStage.InitializeIntegration;
            return;
        }

        job.PortalHandoffDirectionIndices = new byte[goalCells.Length];
        job.PortalExternalCells = null;
        job.PortalExternalCosts = null;
        job.GoalSeedCosts = ResolveDeterministicFlowGoalSeedCosts(
            job.Tile,
            job,
            out job.PortalHandoffDirectionIndices,
            out job.PortalExternalCells,
            out job.PortalExternalCosts);
        job.PotentialShapeKey = new FlowLocalPotentialShapeKey
        {
            WorldVersion = key.WorldVersion,
            AgentTypeId = key.AgentTypeId,
            SectorId = key.SectorId,
            PortalId = key.GoalId,
            DirtyVersion = key.DirtyVersion,
            NormalizedSeedCosts = (int[])job.GoalSeedCosts.Clone(),
            NormalizedExternalCosts = job.PortalExternalCosts != null
                ? (int[])job.PortalExternalCosts.Clone()
                : null,
            PortalHandoffDirectionIndices = (byte[])job.PortalHandoffDirectionIndices.Clone()
        };
        job.PotentialOffset = 0;
        job.Stage = FlowTileBuildStage.PotentialShapeLookup;
    }

    private static void ResolveDeterministicPortalPotentialShape(FlowTileBuildJob job)
    {
        FlowTileCacheKey key = job.Tile.Key;
        job.PotentialShapeKey = new FlowLocalPotentialShapeKey
        {
            WorldVersion = key.WorldVersion,
            AgentTypeId = key.AgentTypeId,
            SectorId = key.SectorId,
            PortalId = key.GoalId,
            DirtyVersion = key.DirtyVersion,
            NormalizedSeedCosts = job.GoalSeedCosts,
            NormalizedExternalCosts = job.PortalExternalCosts,
            PortalHandoffDirectionIndices = job.PortalHandoffDirectionIndices
        };
        if (FlowLocalPotentialShapes.TryGetValue(job.PotentialShapeKey, out FlowLocalPotentialShape shape))
        {
            ValidateFlowLocalPotentialShape(shape, job.Tile, key);
            shape.LastUsedFrame = GetFrameCount();
            job.PotentialShape = shape;
            job.Tile.PotentialShape = shape;
            job.Tile.PotentialOffset = job.PotentialOffset;
            job.Tile.DeterministicIntegrationCosts = null;
            job.Tile.DeterministicFlowDirectionIndices = shape.DirectionIndices;
            job.Tile.DeterministicPortalTargetSlotIndices = shape.PortalTargetSlotIndices;
            job.Tile.FlowFieldValues = shape.FlowFieldValues;
            // A portal-window tile owns only the local sector-to-exit field.  The
            // path handle/funnel owns cross-portal continuation; binding a
            // downstream suffix here would make the hallway identity depend on
            // the exact moving goal again.
            job.Stage = FlowTileBuildStage.DiagnosticShadow;
            _perf.FlowLocalPotentialShapeHits++;
            return;
        }
        _perf.FlowLocalPotentialShapeMisses++;
        job.Cursor = 0;
        job.Stage = FlowTileBuildStage.InitializeIntegration;
    }

    private static void AdvanceDeterministicFlowTileIntegrationInitialization(FlowTileBuildJob job)
    {
        if (job == null || job.Tile == null)
            throw new InvalidOperationException("AdvanceDeterministicFlowTileIntegrationInitialization failed: tile is missing.");
        if (job.HasIntegrationDirectionsHandle)
            throw new InvalidOperationException($"Flow tile integration was scheduled twice key={FormatTileKey(job.BuildKey.CacheKey)}.");

        FlowTileCacheEntry tile = job.Tile;
        int count = checked(tile.Width * tile.Height);
        FlowTileSectorInputBacking worldBacking = RequireFlowTileSectorInputBacking(_world, tile.Key.SectorId);
        worldBacking.Acquire();
        job.SectorInputBacking = worldBacking;
        job.IntegrationCostsNative = new NativeArray<int>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        job.DirectionsNative = new NativeArray<byte>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        int goalCount = tile.GoalCells?.Length ?? 0;
        job.GoalLocalIndicesNative = new NativeArray<int>(goalCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        job.GoalSeedCostsNative = new NativeArray<int>(goalCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        job.PortalHandoffNative = new NativeArray<byte>(goalCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        job.ExternalXNative = new NativeArray<int>(goalCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        job.ExternalYNative = new NativeArray<int>(goalCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        job.ExternalCostsNative = new NativeArray<int>(goalCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        job.StatusNative = new NativeArray<int>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        // A successful relaxation can be inserted at most once per directed
        // cardinal edge, plus the initial wave-front seeds.
        int heapCapacity = checked(Math.Max(64, count * 4 + goalCount));
        NativeArray<int> heapCosts = new NativeArray<int>(heapCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        NativeArray<int> heapIndices = new NativeArray<int>(heapCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        for (int goalIndex = 0; goalIndex < goalCount; goalIndex++)
        {
            Vector2Int goal = tile.GoalCells[goalIndex];
            if (!IsInsideSector(tile, goal.x, goal.y))
                throw new InvalidOperationException($"Flow tile integration snapshot has an outside goal cell goal={goal} key={FormatTileKey(tile.Key)}.");
            int localIndex = tile.GetLocalIndex(goal.x, goal.y);
            job.GoalLocalIndicesNative[goalIndex] = localIndex;
            job.GoalSeedCostsNative[goalIndex] = job.GoalSeedCosts[goalIndex];
            job.PortalHandoffNative[goalIndex] = job.PortalHandoffDirectionIndices != null
                ? job.PortalHandoffDirectionIndices[goalIndex]
                : (byte)0;
            if (job.PortalExternalCells != null && goalIndex < job.PortalExternalCells.Length)
            {
                job.ExternalXNative[goalIndex] = job.PortalExternalCells[goalIndex].x;
                job.ExternalYNative[goalIndex] = job.PortalExternalCells[goalIndex].y;
                job.ExternalCostsNative[goalIndex] = job.PortalExternalCosts[goalIndex];
            }
            else
            {
                job.ExternalXNative[goalIndex] = -1;
                job.ExternalYNative[goalIndex] = -1;
                job.ExternalCostsNative[goalIndex] = int.MaxValue;
            }
        }

        var integrationJob = new DeterministicFlowTileIntegrationDirectionsJob
        {
            Width = tile.Width,
            Height = tile.Height,
            StartX = tile.StartX,
            StartY = tile.StartY,
            IsPortalGoal = tile.Key.GoalKind == TileGoalKind.Portal,
            IntegrationCosts = job.IntegrationCostsNative,
            Directions = job.DirectionsNative,
            Walkable = worldBacking.Walkable,
            TraversalMask = worldBacking.TraversalMask,
            CellCosts = worldBacking.CellCosts,
            GoalLocalIndices = job.GoalLocalIndicesNative,
            GoalSeedCosts = job.GoalSeedCostsNative,
            PortalHandoff = job.PortalHandoffNative,
            ExternalX = job.ExternalXNative,
            ExternalY = job.ExternalYNative,
            ExternalCosts = job.ExternalCostsNative,
            Status = job.StatusNative,
            HeapCosts = heapCosts,
            HeapIndices = heapIndices
        };
        job.IntegrationDirectionsHandle = integrationJob.Schedule();
        job.HasIntegrationDirectionsHandle = true;
        job.Stage = FlowTileBuildStage.Integrate;
        heapCosts.Dispose(job.IntegrationDirectionsHandle);
        heapIndices.Dispose(job.IntegrationDirectionsHandle);
        JobHandle.ScheduleBatchedJobs();
    }

    private static void CopyCompletedDeterministicFlowTilePayload(FlowTileBuildJob job)
    {
        if (job == null || job.Tile == null)
            throw new InvalidOperationException("CopyCompletedDeterministicFlowTilePayload failed: tile is missing.");
        int count = job.Tile.Width * job.Tile.Height;
        int copyCount = Math.Min(count - job.Cursor, 256);
        if (copyCount > 0)
        {
            NativeArray<int>.Copy(job.IntegrationCostsNative, job.Cursor, job.Tile.DeterministicIntegrationCosts, job.Cursor, copyCount);
            NativeArray<byte>.Copy(job.DirectionsNative, job.Cursor, job.Tile.DeterministicFlowDirectionIndices, job.Cursor, copyCount);
            job.Cursor += copyCount;
            return;
        }

        DisposeDeterministicFlowTileIntegrationJobPayloads(job);
        job.Cursor = 0;
        if (job.Tile.Key.GoalKind == TileGoalKind.Portal)
        {
            job.SlotTraceCursor = -1;
            job.SlotTraceLocalIndices.Clear();
            job.Tile.DeterministicPortalTargetSlotIndices = new ushort[count];
            job.Stage = FlowTileBuildStage.PortalSlots;
        }
        else
        {
            job.Tile.DeterministicPortalTargetSlotIndices = null;
            job.Stage = FlowTileBuildStage.DiagnosticShadow;
        }
    }

    private static void AdvanceDeterministicFlowTileSeed(FlowTileBuildJob job)
    {
        FlowTileCacheEntry tile = job.Tile;
        if (job.Cursor < tile.GoalCells.Length)
        {
            int slot = job.Cursor++;
            Vector2Int goal = tile.GoalCells[slot];
            if (!IsInsideSector(tile, goal.x, goal.y) || !_world.IsWalkable(goal.x, goal.y))
                return;
            int seedCost = job.GoalSeedCosts[slot];
            if (seedCost == int.MaxValue)
                return;
            int localIndex = tile.GetLocalIndex(goal.x, goal.y);
            tile.DeterministicIntegrationCosts[localIndex] = seedCost;
            job.OpenSet.Push(seedCost, localIndex);
            return;
        }
        if (job.OpenSet.Count == 0)
            throw new InvalidOperationException($"Flow tile seed pass found no walkable goal key={FormatTileKey(tile.Key)}.");
        job.Stage = FlowTileBuildStage.Integrate;
    }

    private static void ValidateFlowLocalPotentialShape(
        FlowLocalPotentialShape shape,
        FlowTileCacheEntry tile,
        FlowTileCacheKey key)
    {
        int count = tile.Width * tile.Height;
        if (shape?.NormalizedIntegrationCosts == null
            || shape.NormalizedIntegrationCosts.Length != count
            || shape.DirectionIndices == null
            || shape.DirectionIndices.Length != count
            || shape.PortalTargetSlotIndices == null
            || shape.PortalTargetSlotIndices.Length != count
            || shape.FlowFieldValues == null
            || shape.FlowFieldValues.Length != count)
        {
            throw new InvalidOperationException($"Cached local potential shape payload is invalid key={FormatTileKey(key)}.");
        }
        if (!shape.HasAuthorityContentHash)
            throw new InvalidOperationException($"Cached local potential shape is unhashed key={FormatTileKey(key)}.");
    }

    private static void AdvanceFlowTilePotentialShapeMaterialization(FlowTileBuildJob job)
    {
        throw new InvalidOperationException(
            $"Portal potential shape materialization is no longer a valid build stage key={FormatTileKey(job?.Tile?.Key ?? default)}.");
    }

    private static void AdvanceDeterministicPortalSlotPass(FlowTileBuildJob job)
    {
        FlowTileCacheEntry tile = job.Tile ?? throw new InvalidOperationException("Portal slot pass has no tile.");
        if (job.PotentialShapeKey == null)
            throw new InvalidOperationException($"Portal slot pass has no potential shape key key={FormatTileKey(tile.Key)}.");
        int count = tile.Width * tile.Height;
        ushort[] slotIndices = tile.DeterministicPortalTargetSlotIndices
                               ?? throw new InvalidOperationException($"Portal slot pass has no slot payload key={FormatTileKey(tile.Key)}.");
        if (job.SlotTraceCursor == -2)
        {
            if (job.SlotTraceLocalIndices.Count == 0)
            {
                job.SlotTraceCursor = -1;
                job.Cursor++;
                return;
            }
            int last = job.SlotTraceLocalIndices.Count - 1;
            int fillIndex = job.SlotTraceLocalIndices[last];
            job.SlotTraceLocalIndices.RemoveAt(last);
            if (slotIndices[fillIndex] != 0 && slotIndices[fillIndex] != job.SlotTraceValue)
                throw new InvalidOperationException($"Portal slot trace encountered a conflicting slot localIndex={fillIndex}, key={FormatTileKey(tile.Key)}.");
            slotIndices[fillIndex] = job.SlotTraceValue;
            _perf.PortalSlotFillOperations++;
            return;
        }
        if (job.SlotTraceCursor >= 0)
        {
            int traceIndex = job.SlotTraceCursor;
            ushort resolved = slotIndices[traceIndex];
            if (resolved != 0)
            {
                job.SlotTraceValue = resolved;
                job.SlotTraceCursor = -2;
                return;
            }
            if (job.SlotTraceLocalIndices.Count > count)
                throw new InvalidOperationException($"Portal slot trace contains a cycle key={FormatTileKey(tile.Key)}.");
            job.SlotTraceLocalIndices.Add(traceIndex);
            byte directionIndex = tile.DeterministicFlowDirectionIndices[traceIndex];
            int offsetIndex = directionIndex - 1;
            if (offsetIndex < 0 || offsetIndex >= NeighborOffsetX.Length)
                throw new InvalidOperationException($"Reachable portal flow has no direction localIndex={traceIndex}, key={FormatTileKey(tile.Key)}.");
            int worldX = tile.StartX + traceIndex % tile.Width;
            int worldY = tile.StartY + traceIndex / tile.Width;
            int nextX = worldX + NeighborOffsetX[offsetIndex];
            int nextY = worldY + NeighborOffsetY[offsetIndex];
            if (!IsInsideSector(tile, nextX, nextY))
                throw new InvalidOperationException($"Portal flow leaves tile before reaching a portal cell=({worldX},{worldY}), key={FormatTileKey(tile.Key)}.");
            int nextIndex = tile.GetLocalIndex(nextX, nextY);
            int nextGoalCellIndex = IndexOfGoalCell(tile.GoalCells, nextX, nextY);
            if (nextGoalCellIndex >= 0)
            {
                ushort expectedSlot = checked((ushort)(nextGoalCellIndex + 1));
                if (slotIndices[nextIndex] != 0 && slotIndices[nextIndex] != expectedSlot)
                {
                    throw new InvalidOperationException(
                        $"Portal trace reached a conflicting goal slot localIndex={nextIndex}, actual={slotIndices[nextIndex]}, expected={expectedSlot}, key={FormatTileKey(tile.Key)}.");
                }
                slotIndices[nextIndex] = expectedSlot;
                job.SlotTraceValue = expectedSlot;
                job.SlotTraceCursor = -2;
                return;
            }
            if (tile.DeterministicIntegrationCosts[nextIndex] >= tile.DeterministicIntegrationCosts[traceIndex])
                throw new InvalidOperationException($"Portal flow does not descend current={tile.DeterministicIntegrationCosts[traceIndex]}, next={tile.DeterministicIntegrationCosts[nextIndex]}, key={FormatTileKey(tile.Key)}.");
            job.SlotTraceCursor = nextIndex;
            _perf.PortalSlotTraceOperations++;
            return;
        }
        if (job.Cursor < count)
        {
            int localIndex = job.Cursor;
            int worldX = tile.StartX + localIndex % tile.Width;
            int worldY = tile.StartY + localIndex / tile.Width;
            int goalCellIndex = IndexOfGoalCell(tile.GoalCells, worldX, worldY);
            if (goalCellIndex >= 0)
            {
                ushort expectedSlot = checked((ushort)(goalCellIndex + 1));
                if (slotIndices[localIndex] != 0 && slotIndices[localIndex] != expectedSlot)
                {
                    throw new InvalidOperationException(
                        $"Portal goal cell has a conflicting slot localIndex={localIndex}, actual={slotIndices[localIndex]}, expected={expectedSlot}, key={FormatTileKey(tile.Key)}.");
                }
                slotIndices[localIndex] = expectedSlot;
                job.Cursor++;
                return;
            }
            if (tile.DeterministicIntegrationCosts[localIndex] == int.MaxValue || slotIndices[localIndex] != 0)
            {
                job.Cursor++;
                return;
            }
            job.SlotTraceLocalIndices.Clear();
            job.SlotTraceCursor = localIndex;
            return;
        }

        BeginDeterministicPortalPotentialShapeHash(job);
    }

    private static void BeginDeterministicPortalPotentialShapeHash(FlowTileBuildJob job)
    {
        FlowTileCacheEntry tile = job.Tile ?? throw new InvalidOperationException("Completed portal potential has no tile.");
        var shape = new FlowLocalPotentialShape
        {
            NormalizedIntegrationCosts = tile.DeterministicIntegrationCosts,
            DirectionIndices = tile.DeterministicFlowDirectionIndices,
            PortalTargetSlotIndices = tile.DeterministicPortalTargetSlotIndices,
            FlowFieldValues = new byte[checked(tile.Width * tile.Height)],
            LastUsedFrame = GetFrameCount()
        };
        FlowLocalPotentialShapeKey key = job.PotentialShapeKey
                                         ?? throw new InvalidOperationException($"Completed portal potential has no shape key key={FormatTileKey(tile.Key)}.");
        var hasher = new LogicStateHasher();
        hasher.Add(0x4E4156504F545348UL);
        hasher.Add(key.WorldVersion);
        hasher.Add(key.AgentTypeId);
        hasher.Add(key.SectorId);
        hasher.Add(key.PortalId);
        hasher.Add(key.DirtyVersion);
        hasher.Add(key.NormalizedSeedCosts?.Length ?? 0);
        job.PotentialShape = shape;
        job.PotentialShapeHasher = hasher;
        job.PotentialShapeHashSection = 0;
        job.Cursor = 0;
        job.Stage = FlowTileBuildStage.PotentialShapeHash;
    }

    private static void AdvanceDeterministicPortalPotentialShapeHash(FlowTileBuildJob job)
    {
        FlowTileCacheEntry tile = job.Tile ?? throw new InvalidOperationException("Portal potential shape hash has no tile.");
        FlowLocalPotentialShapeKey key = job.PotentialShapeKey
                                         ?? throw new InvalidOperationException($"Portal potential shape hash has no key key={FormatTileKey(tile.Key)}.");
        FlowLocalPotentialShape shape = job.PotentialShape
                                        ?? throw new InvalidOperationException($"Portal potential shape hash has no shape key={FormatTileKey(tile.Key)}.");
        LogicStateHasher hasher = job.PotentialShapeHasher
                                  ?? throw new InvalidOperationException($"Portal potential shape hash has no hasher key={FormatTileKey(tile.Key)}.");

        switch (job.PotentialShapeHashSection)
        {
            case 0:
                if (job.Cursor < (key.NormalizedSeedCosts?.Length ?? 0))
                {
                    hasher.Add(key.NormalizedSeedCosts[job.Cursor++]);
                    return;
                }
                hasher.Add(key.NormalizedExternalCosts?.Length ?? 0);
                job.PotentialShapeHashSection = 1;
                job.Cursor = 0;
                break;
            case 1:
                if (job.Cursor < (key.NormalizedExternalCosts?.Length ?? 0))
                {
                    hasher.Add(key.NormalizedExternalCosts[job.Cursor++]);
                    return;
                }
                hasher.Add(key.PortalHandoffDirectionIndices?.Length ?? 0);
                job.PotentialShapeHashSection = 2;
                job.Cursor = 0;
                break;
            case 2:
                if (job.Cursor < (key.PortalHandoffDirectionIndices?.Length ?? 0))
                {
                    hasher.Add(key.PortalHandoffDirectionIndices[job.Cursor++]);
                    return;
                }
                hasher.Add(shape.NormalizedIntegrationCosts?.Length ?? 0);
                job.PotentialShapeHashSection = 3;
                job.Cursor = 0;
                break;
            case 3:
                if (job.Cursor < shape.NormalizedIntegrationCosts.Length)
                {
                    int localIndex = job.Cursor++;
                    int integrationCost = shape.NormalizedIntegrationCosts[localIndex];
                    hasher.Add(integrationCost);
                    int worldX = tile.StartX + localIndex % tile.Width;
                    int worldY = tile.StartY + localIndex / tile.Width;
                    byte flowValue = 0;
                    if (_world.IsWalkable(worldX, worldY))
                        flowValue |= FlowPathableFlag;
                    if (integrationCost != int.MaxValue)
                        flowValue |= FlowReachableFlag;
                    flowValue |= (byte)(shape.DirectionIndices[localIndex] & FlowDirectionMask);
                    shape.FlowFieldValues[localIndex] = flowValue;
                    return;
                }
                hasher.Add(shape.DirectionIndices?.Length ?? 0);
                job.PotentialShapeHashSection = 4;
                job.Cursor = 0;
                break;
            case 4:
                if (job.Cursor < shape.DirectionIndices.Length)
                {
                    hasher.Add(shape.DirectionIndices[job.Cursor++]);
                    return;
                }
                hasher.Add(shape.PortalTargetSlotIndices?.Length ?? 0);
                job.PotentialShapeHashSection = 5;
                job.Cursor = 0;
                break;
            case 5:
                if (job.Cursor < shape.PortalTargetSlotIndices.Length)
                {
                    hasher.Add((uint)shape.PortalTargetSlotIndices[job.Cursor++]);
                    return;
                }
                shape.AuthorityContentHash = hasher.Hash;
                shape.HasAuthorityContentHash = true;
                job.PotentialShapeHasher = null;
                CommitCompletedDeterministicPortalPotentialShape(job);
                return;
            default:
                throw new InvalidOperationException($"Unknown portal potential shape hash section={job.PotentialShapeHashSection} key={FormatTileKey(tile.Key)}.");
        }
    }

    private static void CommitCompletedDeterministicPortalPotentialShape(FlowTileBuildJob job)
    {
        FlowTileCacheEntry tile = job.Tile ?? throw new InvalidOperationException("Completed portal potential has no tile.");
        FlowLocalPotentialShape shape = job.PotentialShape
                                        ?? throw new InvalidOperationException($"Completed portal potential has no shape key={FormatTileKey(tile.Key)}.");
        if (!shape.HasAuthorityContentHash)
            throw new InvalidOperationException($"Completed portal potential shape is unhashed key={FormatTileKey(tile.Key)}.");
        if (FlowLocalPotentialShapes.ContainsKey(job.PotentialShapeKey))
            throw new InvalidOperationException($"Local potential shape appeared during a deterministic build key={FormatTileKey(tile.Key)}.");
        FlowLocalPotentialShapes.Add(job.PotentialShapeKey, shape);
        TrimFlowLocalPotentialShapes();
        tile.PotentialShape = shape;
        tile.PotentialOffset = job.PotentialOffset;
        tile.DeterministicIntegrationCosts = null;
        tile.DeterministicFlowDirectionIndices = shape.DirectionIndices;
        tile.DeterministicPortalTargetSlotIndices = shape.PortalTargetSlotIndices;
        tile.FlowFieldValues = shape.FlowFieldValues;
        job.Stage = FlowTileBuildStage.DiagnosticShadow;
    }

    private static void TrimFlowLocalPotentialShapes()
    {
        int limit = Mathf.Max(64, Config.FlowTileCacheLimit * 4);
        while (FlowLocalPotentialShapes.Count > limit)
        {
            FlowLocalPotentialShapeKey oldestKey = null;
            int oldestFrame = int.MaxValue;
            foreach (KeyValuePair<FlowLocalPotentialShapeKey, FlowLocalPotentialShape> pair in FlowLocalPotentialShapes)
            {
                int frame = pair.Value?.LastUsedFrame ?? int.MinValue;
                if (oldestKey != null)
                {
                    int frameOrder = frame.CompareTo(oldestFrame);
                    if (frameOrder > 0
                        || (frameOrder == 0 && CompareFlowLocalPotentialShapeKeys(pair.Key, oldestKey) >= 0))
                    {
                        continue;
                    }
                }
                oldestKey = pair.Key;
                oldestFrame = frame;
            }
            if (oldestKey == null || !FlowLocalPotentialShapes.Remove(oldestKey))
                throw new InvalidOperationException("Failed to trim the local potential shape cache.");
        }
    }

    private static void AdvanceFlowTileDiagnosticShadow(FlowTileBuildJob job)
    {
        FlowTileCacheEntry tile = job.Tile ?? throw new InvalidOperationException("Flow diagnostic shadow pass has no tile.");
        int count = tile.Width * tile.Height;
        if (tile.PotentialShape != null)
        {
            if (!ReferenceEquals(tile.FlowFieldValues, tile.PotentialShape.FlowFieldValues)
                || tile.FlowFieldValues == null
                || tile.FlowFieldValues.Length != count)
            {
                throw new InvalidOperationException(
                    $"Flow potential binding does not reference its immutable diagnostic payload key={FormatTileKey(tile.Key)}.");
            }
            BeginDeterministicFlowTileAuthorityHash(job);
            return;
        }
        if (tile.FlowFieldValues == null)
            tile.FlowFieldValues = new byte[count];
        else if (tile.FlowFieldValues.Length != count)
            throw new InvalidOperationException($"Flow diagnostic shadow payload is invalid key={FormatTileKey(tile.Key)}.");
        if (job.Cursor < 0 || job.Cursor > count)
            throw new InvalidOperationException($"Flow diagnostic shadow cursor is invalid cursor={job.Cursor}, key={FormatTileKey(tile.Key)}.");
        if (job.Cursor == count)
        {
            BeginDeterministicFlowTileAuthorityHash(job);
            return;
        }
        int localIndex = job.Cursor++;
        int worldX = tile.StartX + localIndex % tile.Width;
        int worldY = tile.StartY + localIndex / tile.Width;
        if (_world.IsWalkable(worldX, worldY))
            tile.FlowFieldValues[localIndex] |= FlowPathableFlag;
        if (GetDeterministicIntegrationCost(tile, localIndex) != int.MaxValue)
            tile.FlowFieldValues[localIndex] |= FlowReachableFlag;
        tile.FlowFieldValues[localIndex] |= (byte)(tile.DeterministicFlowDirectionIndices[localIndex] & FlowDirectionMask);
        if (job.Cursor >= count)
            BeginDeterministicFlowTileAuthorityHash(job);
    }

    private static void BeginDeterministicFlowTileAuthorityHash(FlowTileBuildJob job)
    {
        FlowTileCacheEntry tile = job.Tile ?? throw new InvalidOperationException("Flow tile authority hash has no tile.");
        if (tile.HasAuthorityContentHash)
            throw new InvalidOperationException($"Flow tile authority hash was already completed key={FormatTileKey(tile.Key)}.");

        var hasher = new LogicStateHasher();
        hasher.Add(0x4E415654494C4546UL);
        FlowTileCacheKey key = tile.Key;
        hasher.Add(key.WorldVersion);
        hasher.Add(key.SectorId);
        hasher.Add((int)key.GoalKind);
        hasher.Add(key.GoalId);
         if (key.GoalKind == TileGoalKind.FinalGoal)
         {
             hasher.Add(key.FinalGoalIndex);
         }
        job.AuthorityHasher = hasher;
        job.AuthorityHashSection = 0;
        job.Cursor = 0;
        job.Stage = FlowTileBuildStage.AuthorityHash;
    }

    private static void AdvanceDeterministicFlowTileAuthorityHash(FlowTileBuildJob job)
    {
        FlowTileCacheEntry tile = job.Tile ?? throw new InvalidOperationException("Flow tile authority hash has no tile.");
        LogicStateHasher hasher = job.AuthorityHasher
                                  ?? throw new InvalidOperationException($"Flow tile authority hash has no hasher key={FormatTileKey(tile.Key)}.");

        if (job.AuthorityHashSection == 0)
        {
            hasher.Add(tile.Key.AgentTypeId);
            hasher.Add(tile.Key.DirtyVersion);
            hasher.Add(tile.StartX);
            hasher.Add(tile.StartY);
            hasher.Add(tile.Width);
            hasher.Add(tile.Height);
            bool usesPotentialShape = tile.PotentialShape != null;
            hasher.Add(usesPotentialShape);
            if (usesPotentialShape)
            {
                if (!tile.PotentialShape.HasAuthorityContentHash
                    || tile.DeterministicIntegrationCosts != null
                    || tile.Key.GoalKind != TileGoalKind.Portal)
                {
                    throw new InvalidOperationException($"Flow tile potential binding is invalid key={FormatTileKey(tile.Key)}.");
                }
                hasher.Add(tile.PotentialShape.AuthorityContentHash);
                hasher.Add(tile.PotentialOffset);
                hasher.Add(tile.GoalCells?.Length ?? 0);
                job.AuthorityHashSection = 4;
            }
            else
            {
                hasher.Add(tile.DeterministicIntegrationCosts?.Length ?? 0);
                job.AuthorityHashSection = 1;
            }
            job.Cursor = 0;
        }

        switch (job.AuthorityHashSection)
        {
            case 1:
                if (job.Cursor < tile.DeterministicIntegrationCosts.Length)
                {
                    hasher.Add(tile.DeterministicIntegrationCosts[job.Cursor++]);
                    return;
                }
                hasher.Add(tile.DeterministicFlowDirectionIndices?.Length ?? 0);
                job.AuthorityHashSection = 2;
                job.Cursor = 0;
                break;
            case 2:
                if (job.Cursor < tile.DeterministicFlowDirectionIndices.Length)
                {
                    hasher.Add(tile.DeterministicFlowDirectionIndices[job.Cursor++]);
                    return;
                }
                hasher.Add(tile.DeterministicPortalTargetSlotIndices?.Length ?? 0);
                job.AuthorityHashSection = 3;
                job.Cursor = 0;
                break;
            case 3:
                if (job.Cursor < (tile.DeterministicPortalTargetSlotIndices?.Length ?? 0))
                {
                    hasher.Add((uint)tile.DeterministicPortalTargetSlotIndices[job.Cursor++]);
                    return;
                }
                hasher.Add(tile.GoalCells?.Length ?? 0);
                job.AuthorityHashSection = 4;
                job.Cursor = 0;
                break;
            case 4:
                if (job.Cursor < (tile.GoalCells?.Length ?? 0))
                {
                    Vector2Int goal = tile.GoalCells[job.Cursor++];
                    hasher.Add(goal.x);
                    hasher.Add(goal.y);
                    return;
                }
                hasher.Add(tile.UsesClearFlowDescriptor);
                tile.AuthorityContentHash = hasher.Hash;
                tile.HasAuthorityContentHash = true;
                job.AuthorityHasher = null;
                job.Stage = FlowTileBuildStage.Commit;
                return;
            default:
                throw new InvalidOperationException($"Unknown flow tile authority hash section={job.AuthorityHashSection} key={FormatTileKey(tile.Key)}.");
        }
    }

    private static void CommitCompletedDeterministicFlowTileBuildJob(FlowTileBuildJob job)
    {
        FlowTileCacheEntry tile = job.Tile ?? throw new InvalidOperationException("Completed flow tile job has no tile.");
        FlowTileCacheKey key = job.BuildKey.CacheKey;
        if (!HasDeterministicIntegrationPayload(tile) || tile.DeterministicFlowDirectionIndices == null)
            throw new InvalidOperationException($"Completed flow tile job has no deterministic payload key={FormatTileKey(key)}.");
        tile.LastUsedFrame = GetFrameCount();
        long phaseStartTicks = Stopwatch.GetTimestamp();
        SetDeterministicFlowTileCacheEntry(key, tile);
        FlowTileCache.Add(key, tile);
        MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowTileCommitCache, Stopwatch.GetTimestamp() - phaseStartTicks);
        job.Stage = FlowTileBuildStage.Complete;
    }

    private static void BuildFlowTileDiagnosticShadow(FlowTileCacheEntry tile)
    {
        if (tile == null)
            throw new ArgumentNullException(nameof(tile));
        int count = tile.Width * tile.Height;
        if (!HasDeterministicIntegrationPayload(tile))
            throw new InvalidOperationException($"BuildFlowTileDiagnosticShadow failed: invalid deterministic integration payload key={FormatTileKey(tile.Key)}.");
        if (tile.DeterministicFlowDirectionIndices == null || tile.DeterministicFlowDirectionIndices.Length != count)
            throw new InvalidOperationException($"BuildFlowTileDiagnosticShadow failed: invalid deterministic direction payload key={FormatTileKey(tile.Key)}.");

        tile.FlowFieldValues = new byte[count];
        for (int localIndex = 0; localIndex < count; localIndex++)
        {
            int worldX = tile.StartX + localIndex % tile.Width;
            int worldY = tile.StartY + localIndex / tile.Width;
            if (_world.IsWalkable(worldX, worldY))
                tile.FlowFieldValues[localIndex] |= FlowPathableFlag;
            if (GetDeterministicIntegrationCost(tile, localIndex) != int.MaxValue)
                tile.FlowFieldValues[localIndex] |= FlowReachableFlag;
            tile.FlowFieldValues[localIndex] |= (byte)(tile.DeterministicFlowDirectionIndices[localIndex] & FlowDirectionMask);
        }
    }

    private static bool IsFlowTileBuildJobStale(FlowTileBuildJob job)
    {
        if (job == null || job.HandleSnapshot == null || _world == null)
            return true;
        if (job.HandleSnapshot.WorldVersion != _world.Version)
            return true;
        if (job.HandleSnapshot.SectorIds == null
            || job.BuildKey.SectorPathIndex < 0
            || job.BuildKey.SectorPathIndex >= job.HandleSnapshot.SectorIds.Length)
        {
            return true;
        }
        if (PathReferencesMissingPortal(job.HandleSnapshot))
            return true;

        FlowTileCacheKey expectedKey = CreateTileCacheKeyForPathSegment(
            job.HandleSnapshot,
            job.BuildKey.SectorPathIndex,
            job.BuildKey.GoalX,
            job.BuildKey.GoalY,
            job.BuildKey.AgentTypeId,
            out _,
            out _);
        return !expectedKey.Equals(job.BuildKey.CacheKey);
    }

    private static void BuildDeterministicFlowDirections(FlowTileCacheEntry tile, FlowTileBuildJob job)
    {
        if (tile == null)
            throw new InvalidOperationException("BuildDeterministicFlowDirections failed: tile is null.");
        if (_world == null)
            throw new InvalidOperationException("BuildDeterministicFlowDirections failed: world is null.");
        if (tile.GoalCells == null || tile.GoalCells.Length == 0)
            throw new InvalidOperationException($"BuildDeterministicFlowDirections failed: tile has no goals key={FormatTileKey(tile.Key)}.");

        int count = tile.Width * tile.Height;
        int[] costs = new int[count];
        byte[] directions = new byte[count];
        for (int i = 0; i < count; i++)
            costs[i] = int.MaxValue;

        int[] goalSeedCosts = ResolveDeterministicFlowGoalSeedCosts(
            tile,
            job,
            out byte[] portalHandoffDirectionIndices,
            out Vector2Int[] portalExternalCells,
            out int[] portalExternalCosts);
        DeterministicFlowHeap open = DeterministicFlowOpenSet;
        open.Clear();
        for (int i = 0; i < tile.GoalCells.Length; i++)
        {
            Vector2Int goal = tile.GoalCells[i];
            if (!IsInsideSector(tile, goal.x, goal.y) || !_world.IsWalkable(goal.x, goal.y))
                continue;
            int seedCost = goalSeedCosts[i];
            if (seedCost == int.MaxValue)
                continue;
            int index = tile.GetLocalIndex(goal.x, goal.y);
            costs[index] = seedCost;
            open.Push(seedCost, index);
        }
        if (open.Count == 0)
            throw new InvalidOperationException($"BuildDeterministicFlowDirections failed: tile has no walkable goals key={FormatTileKey(tile.Key)}.");

        while (open.Count > 0)
        {
            DeterministicFlowNode node = open.Pop();
            if (node.Cost != costs[node.Index])
                continue;

            int worldX = tile.StartX + node.Index % tile.Width;
            int worldY = tile.StartY + node.Index / tile.Width;
            for (int directionIndex = 0; directionIndex < CardinalOffsetX.Length; directionIndex++)
            {
                int nextX = worldX + CardinalOffsetX[directionIndex];
                int nextY = worldY + CardinalOffsetY[directionIndex];
                if (!IsInsideSector(tile, nextX, nextY)
                    || !CanTraverseNeighborCells(_world, nextX, nextY, worldX, worldY))
                {
                    continue;
                }

                int candidateCost = ResolveDeterministicEikonalIntegrationCost(
                    tile,
                    costs,
                    nextX,
                    nextY,
                    portalExternalCells,
                    portalExternalCosts);
                int nextIndex = tile.GetLocalIndex(nextX, nextY);
                if (candidateCost >= costs[nextIndex])
                    continue;

                costs[nextIndex] = candidateCost;
                open.Push(candidateCost, nextIndex);
            }
        }

        for (int localIndex = 0; localIndex < count; localIndex++)
        {
            if (costs[localIndex] == int.MaxValue)
                continue;
            int worldX = tile.StartX + localIndex % tile.Width;
            int worldY = tile.StartY + localIndex / tile.Width;
            int goalCellIndex = IndexOfGoalCell(tile.GoalCells, worldX, worldY);
            if (goalCellIndex >= 0)
            {
                if (tile.Key.GoalKind == TileGoalKind.Portal)
                    directions[localIndex] = portalHandoffDirectionIndices[goalCellIndex];
                continue;
            }

            int bestCost = costs[localIndex];
            int bestDirectionIndex = -1;
            for (int directionIndex = 0; directionIndex < NeighborOffsetX.Length; directionIndex++)
            {
                int nextX = worldX + NeighborOffsetX[directionIndex];
                int nextY = worldY + NeighborOffsetY[directionIndex];
                if (!IsInsideSector(tile, nextX, nextY)
                    || !CanTraverseNeighborCells(_world, worldX, worldY, nextX, nextY))
                {
                    continue;
                }

                int nextCost = costs[tile.GetLocalIndex(nextX, nextY)];
                if (nextCost >= bestCost)
                    continue;
                bestCost = nextCost;
                bestDirectionIndex = directionIndex;
            }

            if (bestDirectionIndex >= 0)
                directions[localIndex] = checked((byte)(bestDirectionIndex + 1));
        }

        tile.DeterministicIntegrationCosts = costs;
        tile.DeterministicFlowDirectionIndices = directions;
        tile.DeterministicPortalTargetSlotIndices = tile.Key.GoalKind == TileGoalKind.Portal
            ? BuildDeterministicPortalTargetSlotIndices(tile, costs, directions)
            : null;
    }

    private static int[] ResolveDeterministicFlowGoalSeedCosts(
        FlowTileCacheEntry tile,
        FlowTileBuildJob job,
        out byte[] portalHandoffDirectionIndices,
        out Vector2Int[] portalExternalCells,
        out int[] portalExternalCosts)
    {
        var seedCosts = new int[tile.GoalCells.Length];
        if (tile.Key.GoalKind == TileGoalKind.FinalGoal)
        {
            portalHandoffDirectionIndices = null;
            portalExternalCells = null;
            portalExternalCosts = null;
            return seedCosts;
        }
        portalHandoffDirectionIndices = new byte[tile.GoalCells.Length];
        portalExternalCells = null;
        portalExternalCosts = null;
        for (int i = 0; i < seedCosts.Length; i++)
        {
            seedCosts[i] = int.MaxValue;
        }

        SectorData sector = _world.Sectors[tile.Key.SectorId];
        SectorPortalAccessEntry localAccess = GetPrebuiltSectorPortalAccess(
            _world,
            sector,
            tile.Key.SectorId,
            tile.Key.GoalId);
        PortalData localPortal = GetPortalById(_world, tile.Key.GoalId);
        int oppositeSectorId = GetOppositeSectorId(localPortal, tile.Key.SectorId);
        SectorData oppositeSector = _world.Sectors[oppositeSectorId];
        SectorPortalAccessEntry oppositeAccess = GetPrebuiltSectorPortalAccess(
            _world,
            oppositeSector,
            oppositeSectorId,
            tile.Key.GoalId);
        Vector2Int[] currentPortalCells = GetPortalCellsForSector(localPortal, tile.Key.SectorId);
        Vector2Int[] oppositePortalCells = GetPortalCellsForSector(
            localPortal,
            oppositeSectorId);
        if (currentPortalCells.Length != oppositePortalCells.Length)
            throw new InvalidOperationException($"ResolveDeterministicFlowGoalSeedCosts failed: portal side count mismatch current={currentPortalCells.Length}, opposite={oppositePortalCells.Length}, key={FormatTileKey(tile.Key)}.");
        for (int i = 0; i < tile.GoalCells.Length; i++)
        {
            Vector2Int currentCell = tile.GoalCells[i];
            long localCost = ResolveDeterministicPortalAccessIntegrationCost(
                _world,
                sector,
                localAccess,
                currentCell.x,
                currentCell.y);
            if (localCost == long.MaxValue)
                continue;
            int portalSlot = IndexOfGoalCell(currentPortalCells, currentCell.x, currentCell.y);
            if (portalSlot < 0 || portalSlot >= oppositePortalCells.Length)
                throw new InvalidOperationException($"ResolveDeterministicFlowGoalSeedCosts failed: portal cell is not in authored slot list cell=({currentCell.x},{currentCell.y}), key={FormatTileKey(tile.Key)}.");
            seedCosts[i] = ConvertPortalAccessCostToFlowCost(localCost);
            portalHandoffDirectionIndices[i] = ResolveDeterministicPortalHandoffDirectionIndex(
                localPortal,
                currentCell,
                oppositePortalCells[portalSlot],
                ResolvePortalAccessDirectionIndex(
                    _world,
                    oppositeSector,
                    oppositeAccess,
                    oppositePortalCells[portalSlot]));
        }
        return seedCosts;
    }

    private static byte ResolvePortalAccessDirectionIndex(
        NavigationWorld world,
        SectorData sector,
        SectorPortalAccessEntry access,
        Vector2Int cell)
    {
        if (world == null || sector == null || access == null)
            throw new InvalidOperationException("ResolvePortalAccessDirectionIndex failed: world, sector, or access is null.");
        long currentCost = ResolveDeterministicPortalAccessIntegrationCost(world, sector, access, cell.x, cell.y);
        if (currentCost == long.MaxValue)
            return 0;
        int bestDirection = -1;
        long bestCost = currentCost;
        for (int directionIndex = 0; directionIndex < NeighborOffsetX.Length; directionIndex++)
        {
            int nextX = cell.x + NeighborOffsetX[directionIndex];
            int nextY = cell.y + NeighborOffsetY[directionIndex];
            if (!IsInsideSector(sector, nextX, nextY)
                || !CanTraverseNeighborCells(world, cell.x, cell.y, nextX, nextY))
            {
                continue;
            }
            long nextCost = ResolveDeterministicPortalAccessIntegrationCost(world, sector, access, nextX, nextY);
            if (nextCost < bestCost)
            {
                bestCost = nextCost;
                bestDirection = directionIndex;
            }
        }
        return bestDirection < 0 ? (byte)0 : checked((byte)(bestDirection + 1));
    }

    private static int ResolveDeterministicEikonalIntegrationCost(
        FlowTileCacheEntry tile,
        int[] integrationCosts,
        int worldX,
        int worldY,
        Vector2Int[] portalExternalCells,
        int[] portalExternalCosts)
    {
        int portalGoalIndex = portalExternalCells != null && portalExternalCosts != null
            ? IndexOfGoalCell(tile.GoalCells, worldX, worldY)
            : -1;
        bool hasExternalSample = portalGoalIndex >= 0
                                 && portalGoalIndex < portalExternalCells.Length
                                 && portalGoalIndex < portalExternalCosts.Length
                                 && portalExternalCosts[portalGoalIndex] != int.MaxValue;
        Vector2Int externalCell = hasExternalSample
            ? portalExternalCells[portalGoalIndex]
            : default;
        return ResolveDeterministicEikonalIntegrationCost(
            tile,
            integrationCosts,
            worldX,
            worldY,
            hasExternalSample,
            externalCell.x,
            externalCell.y,
            hasExternalSample ? portalExternalCosts[portalGoalIndex] : int.MaxValue);
    }

    private static int ResolveDeterministicEikonalIntegrationCost(
        FlowTileCacheEntry tile,
        int worldX,
        int worldY,
        bool hasExternalSample,
        int externalSampleX,
        int externalSampleY,
        int externalSampleCost)
    {
        long horizontal = long.MaxValue;
        long vertical = long.MaxValue;
        for (int i = 0; i < CardinalOffsetX.Length; i++)
        {
            int neighborX = worldX + CardinalOffsetX[i];
            int neighborY = worldY + CardinalOffsetY[i];
            if (!IsInsideSector(tile, neighborX, neighborY)
                || !CanTraverseNeighborCells(_world, worldX, worldY, neighborX, neighborY))
            {
                continue;
            }

            int neighborCost = GetDeterministicIntegrationCost(
                tile,
                tile.GetLocalIndex(neighborX, neighborY));
            if (neighborCost == int.MaxValue)
                continue;
            if (neighborX != worldX)
                horizontal = Math.Min(horizontal, neighborCost);
            else
                vertical = Math.Min(vertical, neighborCost);
        }

        if (hasExternalSample)
        {
            int externalOffsetX = externalSampleX - worldX;
            int externalOffsetY = externalSampleY - worldY;
            if (Math.Abs(externalOffsetX) + Math.Abs(externalOffsetY) != 1)
            {
                throw new InvalidOperationException(
                    $"ResolveDeterministicEikonalIntegrationCost failed: external portal sample is not cardinally adjacent " +
                    $"current=({worldX},{worldY}), external=({externalSampleX},{externalSampleY}), key={FormatTileKey(tile.Key)}.");
            }
            if (externalOffsetX != 0)
                horizontal = Math.Min(horizontal, externalSampleCost);
            else
                vertical = Math.Min(vertical, externalSampleCost);
        }

        return checked((int)ResolveDeterministicEikonalUpdate(
            horizontal,
            vertical,
            ResolveDeterministicEikonalStepCost(worldX, worldY),
            int.MaxValue));
    }

    private static int ResolveDeterministicEikonalIntegrationCost(
        FlowTileCacheEntry tile,
        int[] integrationCosts,
        int worldX,
        int worldY,
        bool hasExternalSample,
        int externalSampleX,
        int externalSampleY,
        int externalSampleCost)
    {
        long horizontal = long.MaxValue;
        long vertical = long.MaxValue;
        for (int i = 0; i < CardinalOffsetX.Length; i++)
        {
            int neighborX = worldX + CardinalOffsetX[i];
            int neighborY = worldY + CardinalOffsetY[i];
            if (!IsInsideSector(tile, neighborX, neighborY)
                || !CanTraverseNeighborCells(_world, worldX, worldY, neighborX, neighborY))
            {
                continue;
            }

            int neighborCost = integrationCosts[tile.GetLocalIndex(neighborX, neighborY)];
            if (neighborCost == int.MaxValue)
                continue;
            if (neighborX != worldX)
                horizontal = Math.Min(horizontal, neighborCost);
            else
                vertical = Math.Min(vertical, neighborCost);
        }

        if (hasExternalSample)
        {
            int externalOffsetX = externalSampleX - worldX;
            int externalOffsetY = externalSampleY - worldY;
            if (Math.Abs(externalOffsetX) + Math.Abs(externalOffsetY) != 1)
            {
                throw new InvalidOperationException(
                    $"ResolveDeterministicEikonalIntegrationCost failed: external portal sample is not cardinally adjacent " +
                    $"current=({worldX},{worldY}), external=({externalSampleX},{externalSampleY}), key={FormatTileKey(tile.Key)}.");
            }
            if (externalOffsetX != 0)
                horizontal = Math.Min(horizontal, externalSampleCost);
            else
                vertical = Math.Min(vertical, externalSampleCost);
        }

        long stepCost = ResolveDeterministicEikonalStepCost(worldX, worldY);
        return checked((int)ResolveDeterministicEikonalUpdate(
            horizontal,
            vertical,
            stepCost,
            int.MaxValue));
    }

    private static int ResolveDeterministicEikonalStepCost(int worldX, int worldY)
    {
        if (!TryGetCostFieldValue(_world, worldX, worldY, out byte cellCost)
            || cellCost == byte.MaxValue)
        {
            return int.MaxValue;
        }

        return checked(1024 * Math.Max(1, (int)cellCost));
    }

    private static long ResolveDeterministicEikonalUpdate(
        long horizontal,
        long vertical,
        long stepCost,
        long unreachableCost)
    {
        if (stepCost <= 0L)
            throw new ArgumentOutOfRangeException(nameof(stepCost), stepCost, "Eikonal step cost must be positive.");
        if (unreachableCost <= 0L)
            throw new ArgumentOutOfRangeException(nameof(unreachableCost), unreachableCost, "Eikonal unreachable cost must be positive.");
        if (horizontal == unreachableCost)
        {
            return vertical == unreachableCost
                ? unreachableCost
                : ClampDeterministicIntegrationCost(checked(vertical + stepCost), unreachableCost);
        }
        if (vertical == unreachableCost)
            return ClampDeterministicIntegrationCost(checked(horizontal + stepCost), unreachableCost);

        long low = Math.Min(horizontal, vertical);
        long high = Math.Max(horizontal, vertical);
        long difference = high - low;
        if (difference >= stepCost)
            return ClampDeterministicIntegrationCost(checked(low + stepCost), unreachableCost);

        long discriminant = checked(2L * stepCost * stepCost - difference * difference);
        long root = ResolveIntegerSquareRoot(discriminant);
        return ClampDeterministicIntegrationCost(checked((low + high + root + 1L) / 2L), unreachableCost);
    }

    private static long ResolveIntegerSquareRoot(long value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Integer square root requires a non-negative value.");

        ulong remainder = (ulong)value;
        ulong result = 0;
        ulong bit = 1UL << 62;
        while (bit > remainder)
            bit >>= 2;
        while (bit != 0)
        {
            if (remainder >= result + bit)
            {
                remainder -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }
            bit >>= 2;
        }
        return checked((long)result);
    }

    private static long ClampDeterministicIntegrationCost(long cost, long unreachableCost)
    {
        if (cost < 0L)
            throw new ArgumentOutOfRangeException(nameof(cost), cost, "Integration cost must be non-negative.");
        return cost >= unreachableCost ? unreachableCost : cost;
    }

    private static int ConvertPortalAccessCostToFlowCost(long portalAccessCost)
    {
        if (portalAccessCost == long.MaxValue)
            return int.MaxValue;
        long converted = checked((portalAccessCost + 2L) / 4L);
        return converted >= int.MaxValue ? int.MaxValue : checked((int)converted);
    }

    private static int IndexOfGoalCell(Vector2Int[] goalCells, int worldX, int worldY)
    {
        for (int i = 0; i < goalCells.Length; i++)
        {
            if (goalCells[i].x == worldX && goalCells[i].y == worldY)
                return i;
        }
        return -1;
    }

    private static ushort[] BuildDeterministicPortalTargetSlotIndices(
        FlowTileCacheEntry tile,
        int[] integrationCosts,
        byte[] directionIndices)
    {
        if (tile.GoalCells.Length > ushort.MaxValue - 1)
        {
            throw new InvalidOperationException(
                $"BuildDeterministicPortalTargetSlotIndices failed: portal has too many slots count={tile.GoalCells.Length}, key={FormatTileKey(tile.Key)}.");
        }

        int count = tile.Width * tile.Height;
        var slotIndices = new ushort[count];
        for (int i = 0; i < tile.GoalCells.Length; i++)
        {
            Vector2Int goal = tile.GoalCells[i];
            if (!IsInsideSector(tile, goal.x, goal.y))
                throw new InvalidOperationException($"BuildDeterministicPortalTargetSlotIndices failed: portal goal is outside tile goal={goal}, key={FormatTileKey(tile.Key)}.");
            slotIndices[tile.GetLocalIndex(goal.x, goal.y)] = checked((ushort)(i + 1));
        }

        for (int localIndex = 0; localIndex < count; localIndex++)
        {
            if (integrationCosts[localIndex] == int.MaxValue || slotIndices[localIndex] != 0)
                continue;

            ushort resolvedSlot = ResolveDeterministicPortalTargetSlotIndex(
                tile,
                integrationCosts,
                directionIndices,
                slotIndices,
                localIndex);
            int cursor = localIndex;
            int guard = count + 1;
            while (slotIndices[cursor] == 0 && guard-- > 0)
            {
                slotIndices[cursor] = resolvedSlot;
                int offsetIndex = directionIndices[cursor] - 1;
                int worldX = tile.StartX + cursor % tile.Width;
                int worldY = tile.StartY + cursor / tile.Width;
                cursor = tile.GetLocalIndex(
                    worldX + NeighborOffsetX[offsetIndex],
                    worldY + NeighborOffsetY[offsetIndex]);
            }
            if (guard <= 0)
                throw new InvalidOperationException($"BuildDeterministicPortalTargetSlotIndices failed: flow path fill exceeded tile size key={FormatTileKey(tile.Key)}.");
        }

        return slotIndices;
    }

    private static ushort ResolveDeterministicPortalTargetSlotIndex(
        FlowTileCacheEntry tile,
        int[] integrationCosts,
        byte[] directionIndices,
        ushort[] slotIndices,
        int startLocalIndex)
    {
        int cursor = startLocalIndex;
        int guard = tile.Width * tile.Height + 1;
        while (guard-- > 0)
        {
            ushort slotIndex = slotIndices[cursor];
            if (slotIndex != 0)
                return slotIndex;

            byte directionIndex = directionIndices[cursor];
            if (directionIndex == 0)
            {
                throw new InvalidOperationException(
                    $"ResolveDeterministicPortalTargetSlotIndex failed: reachable portal flow has no direction localIndex={cursor}, key={FormatTileKey(tile.Key)}.");
            }

            int offsetIndex = directionIndex - 1;
            int worldX = tile.StartX + cursor % tile.Width;
            int worldY = tile.StartY + cursor / tile.Width;
            int nextX = worldX + NeighborOffsetX[offsetIndex];
            int nextY = worldY + NeighborOffsetY[offsetIndex];
            if (!IsInsideSector(tile, nextX, nextY))
            {
                throw new InvalidOperationException(
                    $"ResolveDeterministicPortalTargetSlotIndex failed: flow leaves tile before reaching portal cell=({worldX},{worldY}), key={FormatTileKey(tile.Key)}.");
            }

            int nextLocalIndex = tile.GetLocalIndex(nextX, nextY);
            if (integrationCosts[nextLocalIndex] >= integrationCosts[cursor])
            {
                throw new InvalidOperationException(
                    $"ResolveDeterministicPortalTargetSlotIndex failed: flow does not descend current={integrationCosts[cursor]}, next={integrationCosts[nextLocalIndex]}, key={FormatTileKey(tile.Key)}.");
            }
            cursor = nextLocalIndex;
        }

        throw new InvalidOperationException($"ResolveDeterministicPortalTargetSlotIndex failed: flow path contains a cycle key={FormatTileKey(tile.Key)}.");
    }

    private static byte ResolveDeterministicPortalHandoffDirectionIndex(
        PortalData portal,
        Vector2Int currentCell,
        Vector2Int downstreamCell,
        byte downstreamDirectionIndex)
    {
        if (portal == null)
            throw new InvalidOperationException("ResolveDeterministicPortalHandoffDirectionIndex failed: portal is null.");

        int normalX = downstreamCell.x - currentCell.x;
        int normalY = downstreamCell.y - currentCell.y;
        if ((portal.IsVerticalBoundary && (normalX == 0 || normalY != 0))
            || (!portal.IsVerticalBoundary && (normalY == 0 || normalX != 0)))
        {
            throw new InvalidOperationException(
                $"ResolveDeterministicPortalHandoffDirectionIndex failed: portal {portal.PortalId} pair does not match boundary orientation current={currentCell}, downstream={downstreamCell}.");
        }

        int tangentX = 0;
        int tangentY = 0;
        if (downstreamDirectionIndex != 0)
        {
            int downstreamOffsetIndex = downstreamDirectionIndex - 1;
            if (downstreamOffsetIndex < 0 || downstreamOffsetIndex >= NeighborOffsetX.Length)
                throw new InvalidOperationException($"ResolveDeterministicPortalHandoffDirectionIndex failed: invalid downstream direction {downstreamDirectionIndex}.");
            tangentX = portal.IsVerticalBoundary ? 0 : NeighborOffsetX[downstreamOffsetIndex];
            tangentY = portal.IsVerticalBoundary ? NeighborOffsetY[downstreamOffsetIndex] : 0;
        }

        int offsetIndex = ResolveNeighborOffsetIndex(normalX + tangentX, normalY + tangentY);
        if (offsetIndex < 0)
        {
            throw new InvalidOperationException(
                $"ResolveDeterministicPortalHandoffDirectionIndex failed: portal {portal.PortalId} handoff is not an adjacent direction normal=({normalX},{normalY}), tangent=({tangentX},{tangentY}).");
        }
        return checked((byte)(offsetIndex + 1));
    }

}
