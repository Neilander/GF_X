using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class FlowFieldCrowdMovementSystem
{
    private enum NavigationPathSourceStage
    {
        Initialize = 0,
        BuildGoalConnector = 1,
        CreateHierarchyPolicy = 2,
        ExpandHierarchyPolicy = 3,
        BuildDownwardCustomization = 4,
        ExpandL0Policy = 5,
        MaterializeRoute = 6,
        Complete = 7
    }

    private sealed class NavigationPathDemand
    {
        public IEntityContext Source;
        public AgentRuntimeData Agent;
        public int SourceId;
        public int AgentTypeId;
        public int MovingTargetId;
        public int SourceIslandId;
        public int StartSectorId;
        public int StartX;
        public int StartY;
        public int GoalSectorId;
        public int GoalX;
        public int GoalY;
        public FixVector2 InputGoal;
        public FixVector2 StableGoal;
        public Fix64 MaximumTravelDistance;
    }

    private readonly struct NavigationPathRequestKey : IEquatable<NavigationPathRequestKey>
    {
        public NavigationPathRequestKey(
            int worldVersion,
            int agentTypeId,
            int movingTargetId,
            int sourceIslandId,
            int goalSectorId,
            int staticGoalCellIndex,
            int goalSectorDirtyVersion)
        {
            WorldVersion = worldVersion;
            AgentTypeId = agentTypeId;
            MovingTargetId = movingTargetId;
            SourceIslandId = sourceIslandId;
            GoalSectorId = goalSectorId;
            StaticGoalCellIndex = staticGoalCellIndex;
            GoalSectorDirtyVersion = goalSectorDirtyVersion;
        }

        public int WorldVersion { get; }
        public int AgentTypeId { get; }
        public int MovingTargetId { get; }
        public int SourceIslandId { get; }
        public int GoalSectorId { get; }
        public int StaticGoalCellIndex { get; }
        public int GoalSectorDirtyVersion { get; }

        public bool Equals(NavigationPathRequestKey other)
        {
            return WorldVersion == other.WorldVersion
                   && AgentTypeId == other.AgentTypeId
                   && MovingTargetId == other.MovingTargetId
                   && SourceIslandId == other.SourceIslandId
                   && GoalSectorId == other.GoalSectorId
                   && StaticGoalCellIndex == other.StaticGoalCellIndex
                   && GoalSectorDirtyVersion == other.GoalSectorDirtyVersion;
        }

        public override bool Equals(object obj)
        {
            return obj is NavigationPathRequestKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldVersion;
                hash = (hash * 397) ^ AgentTypeId;
                hash = (hash * 397) ^ MovingTargetId;
                hash = (hash * 397) ^ SourceIslandId;
                hash = (hash * 397) ^ GoalSectorId;
                hash = (hash * 397) ^ StaticGoalCellIndex;
                return (hash * 397) ^ GoalSectorDirtyVersion;
            }
        }
    }

    private readonly struct NavigationPathRequestIdentityKey : IEquatable<NavigationPathRequestIdentityKey>
    {
        public NavigationPathRequestIdentityKey(
            int worldVersion,
            int agentTypeId,
            int movingTargetId,
            int sourceIslandId,
            int staticGoalCellIndex)
        {
            WorldVersion = worldVersion;
            AgentTypeId = agentTypeId;
            MovingTargetId = movingTargetId;
            SourceIslandId = sourceIslandId;
            StaticGoalCellIndex = staticGoalCellIndex;
        }

        public int WorldVersion { get; }
        public int AgentTypeId { get; }
        public int MovingTargetId { get; }
        public int SourceIslandId { get; }
        public int StaticGoalCellIndex { get; }

        public bool Equals(NavigationPathRequestIdentityKey other)
        {
            return WorldVersion == other.WorldVersion
                   && AgentTypeId == other.AgentTypeId
                   && MovingTargetId == other.MovingTargetId
                   && SourceIslandId == other.SourceIslandId
                   && StaticGoalCellIndex == other.StaticGoalCellIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is NavigationPathRequestIdentityKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldVersion;
                hash = (hash * 397) ^ AgentTypeId;
                hash = (hash * 397) ^ MovingTargetId;
                hash = (hash * 397) ^ SourceIslandId;
                return (hash * 397) ^ StaticGoalCellIndex;
            }
        }
    }

    private sealed class NavigationPathSourceJob
    {
        public NavigationPathDemand Demand;
        public NavigationPathDemand LatestDemand;
        public int LastRequestedFrame;
        public NavigationPathSourceStage Stage;
        public int HierarchyLevelArrayIndex = -1;
        public PortalHierarchyReversePolicy HierarchyPolicy;
        public int HierarchyPolicyBoundaryCursor;
        public PortalHierarchyConnector GoalConnector;
        public int NextGoalConnectorLevel;
        public int NextDownwardLevel;
        public IncrementalRestrictedPortalSearch RestrictedSearch;
        public readonly List<int> RestrictedSourceNodes = new List<int>(16);
        public readonly List<long> RestrictedSourceCosts = new List<long>(16);
        public readonly List<int> RestrictedTargetNodes = new List<int>(16);
        public int RestrictedSourceCollectionCursor;
        public int RestrictedTargetCollectionCursor;
        public bool RestrictedInputCollectionActive;
        public IncrementalReversePolicyExpansion ReversePolicyExpansion;
        public IncrementalRouteMaterialization Materialization;
        public readonly List<PortalHierarchyDownwardCustomization> DownwardCustomizations =
            new List<PortalHierarchyDownwardCustomization>(4);
        public PathHandle Handle;
    }

    private sealed class NavigationPathSourceJobComparer : IComparer<NavigationPathSourceJob>
    {
        public static readonly NavigationPathSourceJobComparer Instance = new NavigationPathSourceJobComparer();

        public int Compare(NavigationPathSourceJob left, NavigationPathSourceJob right)
        {
            if (left?.Demand == null || right?.Demand == null)
                throw new InvalidOperationException("Navigation path source comparison received an invalid job.");
            return left.Demand.SourceId.CompareTo(right.Demand.SourceId);
        }
    }

    private enum IncrementalRestrictedSearchStage
    {
        InitializeTargets = 0,
        InitializeSources = 1,
        Pop = 2,
        Crossing = 3,
        Edges = 4,
        Complete = 5
    }

    private sealed class IncrementalRestrictedPortalSearch
    {
        public NavigationWorld World;
        public PortalHierarchyCluster ContainingCluster;
        public PortalHierarchyLevel LowerLevel;
        public bool Reverse;
        public readonly PortalGraphSearchResult Result = new PortalGraphSearchResult();
        public readonly DeterministicCostHeap OpenSet = new DeterministicCostHeap();
        public readonly HashSet<int> TargetNodes = new HashSet<int>();
        public IReadOnlyList<int> TargetNodeSequence;
        public IReadOnlyList<int> SourceNodes;
        public IReadOnlyList<long> SourceCosts;
        public int TargetInitializationCursor;
        public int SourceInitializationCursor;
        public int RemainingTargets;
        public IncrementalRestrictedSearchStage Stage;
        public int CurrentNode;
        public long CurrentCost;
        public int CurrentSectorId;
        public int CurrentPortalId;
        public List<PortalTransition> CurrentL0Edges;
        public List<PortalHierarchyEdge> CurrentHierarchyEdges;
        public int EdgeCursor;
    }

    private enum IncrementalReversePolicyExpansionStage
    {
        InitializeGoal = 0,
        CollectTargets = 1,
        Pop = 2,
        Edges = 3,
        Crossing = 4,
        Complete = 5
    }

    private sealed class IncrementalReversePolicyExpansion
    {
        public bool Hierarchy;
        public PortalHierarchyLevel HierarchyLevel;
        public PortalHierarchyCluster HierarchyTargetCluster;
        public SectorData L0TargetSector;
        public int GoalPortalCursor;
        public int TargetCursor;
        public bool HasAccessibleTarget;
        public readonly HashSet<int> PendingTargetNodes = new HashSet<int>();
        public IncrementalReversePolicyExpansionStage Stage;
        public int CurrentNode;
        public long CurrentCost;
        public int CurrentSectorId;
        public int CurrentPortalId;
        public List<PortalHierarchyEdge> CurrentHierarchyEdges;
        public List<PortalTransition> CurrentL0Edges;
        public int EdgeCursor;
        public bool CompleteAfterCurrentExpansion;
    }

    private enum IncrementalRouteMaterializationStage
    {
        Initialize = 0,
        SelectStartPortal = 1,
        ExpandDownward = 2,
        ExpandPolicy = 3,
        ExpandGoalConnector = 4,
        ConvertRoute = 5,
        PrepareImmutableRoute = 6,
        HashRoute = 7,
        Publish = 8,
        Complete = 9
    }

    private enum RouteExpansionTaskType
    {
        TraverseNext = 0,
        TraverseArray = 1,
        ExpandPair = 2,
        FindHierarchyEdge = 3,
        BeginConnector = 4,
        TraverseHierarchyPolicy = 5,
        ExpandHierarchyPolicyPair = 6
    }

    private sealed class RouteExpansionTask
    {
        public RouteExpansionTaskType Type;
        public Dictionary<int, int> Next;
        public int[] Nodes;
        public int Cursor;
        public int CurrentNode;
        public int FromNode;
        public int ToNode;
        public int LevelIndex;
        public int Guard;
        public bool Started;
        public List<PortalHierarchyEdge> CandidateEdges;
        public PortalHierarchyConnector Connector;
    }

    private sealed class IncrementalRouteMaterialization
    {
        public IncrementalRouteMaterializationStage Stage;
        public SectorPathCacheKey PathKey;
        public int StartPortalCursor;
        public int BestStartNode = int.MinValue;
        public long BestStartCost = long.MaxValue;
        public int DownwardCustomizationIndex;
        public bool PolicyExpansionScheduled;
        public bool GoalConnectorScheduled;
        public readonly Stack<RouteExpansionTask> Tasks = new Stack<RouteExpansionTask>(16);
        public readonly List<int> L0Nodes = new List<int>(32);
        public int LastLogicalNode = int.MinValue;
        public int ConvertCursor;
        public readonly List<int> SectorIds = new List<int>(16);
        public readonly List<int> PortalIds = new List<int>(16);
        public int[] ImmutableSectorIds;
        public int[] ImmutablePortalIds;
        public int ImmutableCopyCursor;
        public LogicStateHasher PathAuthorityHasher;
        public LogicStateHasher WitnessAuthorityHasher;
        public int AuthorityHashCursor;
        public bool HashingPortalIds;
        public ulong PathAuthorityHash;
        public ulong WitnessAuthorityHash;
    }

    private sealed class NavigationPathRequestJob
    {
        public NavigationPathRequestIdentityKey IdentityKey;
        public NavigationPathRequestKey Key;
        public SectorCorridorPolicyKey PolicyKey;
        public SectorCorridorPolicy Policy;
        public readonly List<NavigationPathSourceJob> Sources = new List<NavigationPathSourceJob>(8);
        public int SourceCursor;
        public int GoalX;
        public int GoalY;
        public FixVector2 StableGoal;
        public bool Complete;
    }

    private static readonly LinkedList<NavigationPathRequestJob> NavigationPathRequestQueue =
        new LinkedList<NavigationPathRequestJob>();
    private static readonly Dictionary<NavigationPathRequestIdentityKey, NavigationPathRequestJob>
        PendingNavigationPathRequests =
            new Dictionary<NavigationPathRequestIdentityKey, NavigationPathRequestJob>();

    private static bool TryResolveNavigationPathDemand(
        NavigationSyncRequest request,
        out NavigationPathDemand demand,
        out string failureReason)
    {
        demand = null;
        failureReason = string.Empty;
        if (request?.Source == null)
            throw new InvalidOperationException("Navigation path demand resolution received a null request or source.");
        _perf.NavigationPrepareRequests++;
#if UNITY_EDITOR
        _testNavigationPrepareRequestCount++;
#endif
        if (!Agents.TryGetValue(request.SourceId, out AgentRuntimeData agent))
        {
            failureReason = $"source is not registered source={request.Source.CharacterKey}, id={request.SourceId}";
            return false;
        }

        agent.PositionFixed = request.Source.LogicFramePositionFixed();
        agent.Position = ToWorldVector3(agent.PositionFixed);
        agent.RadiusFixed = ResolveCollisionRadiusFixed(request.Source);
        agent.Radius = (float)agent.RadiusFixed;
        agent.AgentTypeId = request.AgentTypeId;
        if (!TryEnsureWorldBuilt(agent.AgentTypeId))
        {
            failureReason = $"world unavailable source={request.Source.CharacterKey} agentType={agent.AgentTypeId}";
            return false;
        }
        if (!TryResolveStartCellForReachabilityFixed(
                request.Source,
                out int startX,
                out int startY,
                out int sourceIslandId))
        {
            failureReason = $"start reachability failed source={request.Source.CharacterKey} {BuildReachabilityStartDiagnostics(request.Source)}";
            return false;
        }
        if (!TryResolveStableGoalCellFixed(
                agent,
                request.Source,
                request.InputGoalPosition,
                out int goalX,
                out int goalY,
                out FixVector2 stableGoal,
                out _))
        {
            failureReason = $"goal reachability failed source={request.Source.CharacterKey} {BuildGoalResolutionFailure(request.Source, ToWorldVector3(request.InputGoalPosition))}";
            return false;
        }
        if (!_world.TryGetSectorId(startX, startY, out int startSectorId))
        {
            failureReason = $"start sector failed source={request.Source.CharacterKey} start=({startX},{startY})";
            return false;
        }
        if (!_world.TryGetSectorId(goalX, goalY, out int goalSectorId))
        {
            failureReason = $"goal sector failed source={request.Source.CharacterKey} goal=({goalX},{goalY})";
            return false;
        }

        agent.NavState.CurrentCell = new Vector2Int(startX, startY);
        agent.NavState.CurrentSectorId = startSectorId;
        agent.NavState.HasGoal = true;
        demand = new NavigationPathDemand
        {
            Source = request.Source,
            Agent = agent,
            SourceId = request.SourceId,
            AgentTypeId = request.AgentTypeId,
            MovingTargetId = request.MovingTargetId,
            SourceIslandId = sourceIslandId,
            StartSectorId = startSectorId,
            StartX = startX,
            StartY = startY,
            GoalSectorId = goalSectorId,
            GoalX = goalX,
            GoalY = goalY,
            InputGoal = request.InputGoalPosition,
            StableGoal = stableGoal,
            MaximumTravelDistance = request.MaximumTravelDistance
        };
        return true;
    }

    private static NavigationPathRequestKey CreateNavigationPathRequestKey(NavigationPathDemand demand)
    {
        int staticGoalCellIndex = demand.MovingTargetId == int.MinValue
            ? _world.GetIndex(demand.GoalX, demand.GoalY)
            : -1;
        return new NavigationPathRequestKey(
            _world.Version,
            demand.AgentTypeId,
            demand.MovingTargetId,
            demand.SourceIslandId,
            demand.GoalSectorId,
            staticGoalCellIndex,
            _world.Sectors[demand.GoalSectorId].DirtyVersion);
    }

    private static NavigationPathRequestIdentityKey CreateNavigationPathRequestIdentityKey(
        NavigationPathDemand demand)
    {
        int staticGoalCellIndex = demand.MovingTargetId == int.MinValue
            ? _world.GetIndex(demand.GoalX, demand.GoalY)
            : -1;
        return new NavigationPathRequestIdentityKey(
            _world.Version,
            demand.AgentTypeId,
            demand.MovingTargetId,
            demand.SourceIslandId,
            staticGoalCellIndex);
    }

    private static SectorCorridorPolicyKey CreateNavigationPathPolicyKey(
        NavigationPathRequestKey requestKey,
        int goalX,
        int goalY)
    {
        return new SectorCorridorPolicyKey(
            requestKey.WorldVersion,
            requestKey.AgentTypeId,
            requestKey.MovingTargetId,
            requestKey.SourceIslandId,
            requestKey.GoalSectorId,
            _world.GetIndex(goalX, goalY),
            requestKey.GoalSectorDirtyVersion);
    }

    private static void EnqueueNavigationPathDemand(NavigationPathDemand demand)
    {
        if (demand == null || demand.Agent == null || demand.Source == null)
            throw new InvalidOperationException("Navigation path demand is incomplete.");
        NavigationPathRequestIdentityKey identityKey = CreateNavigationPathRequestIdentityKey(demand);
        NavigationPathRequestKey key = CreateNavigationPathRequestKey(demand);
        RemoveSourceFromOtherNavigationPathRequests(demand.SourceId, identityKey);
        if (!PendingNavigationPathRequests.TryGetValue(identityKey, out NavigationPathRequestJob job))
        {
            job = new NavigationPathRequestJob
            {
                IdentityKey = identityKey,
                Key = key,
                PolicyKey = CreateNavigationPathPolicyKey(key, demand.GoalX, demand.GoalY),
                GoalX = demand.GoalX,
                GoalY = demand.GoalY,
                StableGoal = demand.StableGoal
            };
            PendingNavigationPathRequests.Add(identityKey, job);
            NavigationPathRequestQueue.AddLast(job);
            _perf.NavigationPathRequestGroups++;
        }
        else if (job.Complete)
        {
            throw new InvalidOperationException("A completed navigation path request remained in the pending index.");
        }

        NavigationPathSourceJob sourceJob = null;
        for (int i = 0; i < job.Sources.Count; i++)
        {
            if (job.Sources[i].Demand.SourceId == demand.SourceId)
            {
                sourceJob = job.Sources[i];
                break;
            }
        }
        if (sourceJob == null)
        {
            sourceJob = new NavigationPathSourceJob
            {
                Demand = demand,
                LatestDemand = demand,
                LastRequestedFrame = GetFrameCount(),
                Stage = NavigationPathSourceStage.Initialize
            };
            int insertionIndex = job.Sources.BinarySearch(
                sourceJob,
                NavigationPathSourceJobComparer.Instance);
            if (insertionIndex >= 0)
                throw new InvalidOperationException("Navigation path request contains a duplicate source id.");
            insertionIndex = ~insertionIndex;
            job.Sources.Insert(insertionIndex, sourceJob);
            if (insertionIndex < job.SourceCursor)
                job.SourceCursor = insertionIndex;
        }
        else
        {
            sourceJob.LatestDemand = demand;
            sourceJob.LastRequestedFrame = GetFrameCount();
        }

        AgentNavState nav = demand.Agent.NavState;
        bool canConsumeCommittedPlan = demand.MovingTargetId != int.MinValue
                                       && nav.PathHandle != null
                                       && nav.PathHandle.WorldVersion == _world.Version
                                       && nav.CommittedMovingTargetId == demand.MovingTargetId
                                       && !PathReferencesMissingPortal(nav.PathHandle)
                                       && TryAdvancePathToCurrentSector(demand.Agent, demand.StartSectorId);
        if (canConsumeCommittedPlan)
        {
            nav.HasPendingNavigation = false;
            nav.HasPendingNavigationReplacement = true;
            nav.PreparedNavigationGoalFixed = nav.LastGoalWorldFixed;
            EnqueueSteeringReadDomainFlowTileBuilds(demand.Agent, demand.MaximumTravelDistance);
        }
        else
        {
            ClearCommittedNavigationPath(demand.Agent);
            nav.HasPendingNavigation = true;
            RemoveFixedPortalParticipation(demand.SourceId);
            nav.PreparedNavigationGoalFixed = demand.StableGoal;
        }
        nav.HasPreparedNavigationSnapshot = true;
        nav.PreparedNavigationFrame = GetFrameCount();
        nav.PreparedInputGoalFixed = demand.InputGoal;
        nav.PreparedMaximumTravelDistanceFixed = demand.MaximumTravelDistance;
    }

    private static void ResetNavigationPathSourceJobForLatestStart(NavigationPathSourceJob source)
    {
        source.Stage = NavigationPathSourceStage.Initialize;
        source.HierarchyLevelArrayIndex = -1;
        source.HierarchyPolicy = null;
        source.HierarchyPolicyBoundaryCursor = 0;
        source.GoalConnector = null;
        source.NextGoalConnectorLevel = 0;
        source.NextDownwardLevel = 0;
        source.RestrictedSearch = null;
        EndRestrictedInputCollection(source);
        source.ReversePolicyExpansion = null;
        source.Materialization = null;
        source.DownwardCustomizations.Clear();
        source.Handle = null;
    }

    private static void RemoveSourceFromOtherNavigationPathRequests(
        int sourceId,
        NavigationPathRequestIdentityKey retainedKey)
    {
        LinkedListNode<NavigationPathRequestJob> node = NavigationPathRequestQueue.First;
        while (node != null)
        {
            LinkedListNode<NavigationPathRequestJob> next = node.Next;
            NavigationPathRequestJob job = node.Value;
            if (job == null)
                throw new InvalidOperationException("Navigation path request queue contains a null job.");
            if (!job.IdentityKey.Equals(retainedKey))
            {
                for (int i = job.Sources.Count - 1; i >= 0; i--)
                {
                    if (job.Sources[i].Demand.SourceId == sourceId)
                    {
                        job.Sources.RemoveAt(i);
                        if (i < job.SourceCursor)
                            job.SourceCursor--;
                    }
                }
                if (job.Sources.Count == 0)
                    RemoveNavigationPathRequestNode(node);
            }
            node = next;
        }
    }

    private static void CancelNavigationPathRequestsForSource(int sourceId)
    {
        LinkedListNode<NavigationPathRequestJob> node = NavigationPathRequestQueue.First;
        while (node != null)
        {
            LinkedListNode<NavigationPathRequestJob> next = node.Next;
            NavigationPathRequestJob job = node.Value;
            if (job == null)
                throw new InvalidOperationException("Navigation path request queue contains a null job.");
            for (int i = job.Sources.Count - 1; i >= 0; i--)
            {
                if (job.Sources[i].Demand.SourceId != sourceId)
                    continue;
                job.Sources.RemoveAt(i);
                if (i < job.SourceCursor)
                    job.SourceCursor--;
            }
            if (job.Sources.Count == 0)
                RemoveNavigationPathRequestNode(node);
            node = next;
        }
    }

    private static void PruneInactiveNavigationPathRequestSources(int frame)
    {
        LinkedListNode<NavigationPathRequestJob> node = NavigationPathRequestQueue.First;
        while (node != null)
        {
            LinkedListNode<NavigationPathRequestJob> next = node.Next;
            NavigationPathRequestJob job = node.Value;
            if (job == null)
                throw new InvalidOperationException("Navigation path request queue contains a null job.");
            for (int i = job.Sources.Count - 1; i >= 0; i--)
            {
                NavigationPathSourceJob source = job.Sources[i];
                if (source.LastRequestedFrame == frame)
                    continue;
                source.Demand.Agent.NavState.HasPendingNavigation = false;
                source.Demand.Agent.NavState.HasPendingNavigationReplacement = false;
                job.Sources.RemoveAt(i);
                if (i < job.SourceCursor)
                    job.SourceCursor--;
            }
            if (job.Sources.Count == 0)
                RemoveNavigationPathRequestNode(node);
            node = next;
        }
    }

    private static void RemoveNavigationPathRequestNode(LinkedListNode<NavigationPathRequestJob> node)
    {
        NavigationPathRequestJob job = node?.Value
            ?? throw new InvalidOperationException("Cannot remove a null navigation path request node.");
        if (!PendingNavigationPathRequests.Remove(job.IdentityKey))
            throw new InvalidOperationException("Navigation path request pending index is inconsistent.");
        NavigationPathRequestQueue.Remove(node);
    }

    private static void InvalidatePendingNavigationPathRequestsForDirtySectors(
        NavigationWorld world,
        HashSet<int> dirtySectors)
    {
        if (world == null)
            throw new InvalidOperationException("Navigation path request invalidation requires a world.");
        if (dirtySectors == null || dirtySectors.Count == 0)
            return;

        LinkedListNode<NavigationPathRequestJob> node = NavigationPathRequestQueue.First;
        while (node != null)
        {
            LinkedListNode<NavigationPathRequestJob> next = node.Next;
            NavigationPathRequestJob job = node.Value
                ?? throw new InvalidOperationException("Navigation path request queue contains a null job.");
            if (job.Key.WorldVersion == world.Version)
            {
                for (int i = 0; i < job.Sources.Count; i++)
                {
                    NavigationPathSourceJob source = job.Sources[i]
                        ?? throw new InvalidOperationException("Navigation path request contains a null source job.");
                    AgentNavState nav = source.Demand?.Agent?.NavState
                        ?? throw new InvalidOperationException("Navigation path request contains an invalid source demand.");
                    ClearCommittedNavigationPath(source.Demand.Agent);
                    nav.HasPendingNavigation = true;
                    RemoveFixedPortalParticipation(source.Demand.SourceId);
                }
                RemoveNavigationPathRequestNode(node);
            }
            node = next;
        }
    }

    private static void ProcessNavigationPathRequestQueue()
    {
        if (_world == null)
            throw new InvalidOperationException("Navigation path request processing requires an active world.");
        LinkedListNode<NavigationPathRequestJob> node = NavigationPathRequestQueue.First;
        while (node != null && !IsNavigationWorkBudgetExhausted())
        {
            LinkedListNode<NavigationPathRequestJob> next = node.Next;
            NavigationPathRequestJob job = node.Value;
            if (job == null)
                throw new InvalidOperationException("Navigation path request queue contains a null job.");
            if (job.Key.WorldVersion != _world.Version)
            {
                node = next;
                continue;
            }

            bool policyMutationStarted = false;
            try
            {
                while (!job.Complete && !IsNavigationWorkBudgetExhausted())
                {
                    AdvanceNavigationPathRequestOneOperation(job, ref policyMutationStarted);
                    _perf.NavigationPathRequestOperations++;
                    IsBudgetExpired(0L, 0);
                }
            }
            finally
            {
                if (policyMutationStarted)
                    RefreshSectorCorridorPolicyAuthority(job.PolicyKey, job.Policy);
            }

            if (job.Complete)
                RemoveNavigationPathRequestNode(node);
            node = next;
        }
    }

    private static void ProcessNavigationPathRequestsAcrossWorlds()
    {
        NavigationWorld previousWorld = _world;
        WorldRuntimeState previousState = _activeWorldState;
        var states = new List<WorldRuntimeState>(WorldStates.Values);
        states.Sort((left, right) => left.AgentTypeId.CompareTo(right.AgentTypeId));
        try
        {
            for (int i = 0; i < states.Count && !IsNavigationWorkBudgetExhausted(); i++)
            {
                ActivateFlowBuildQueueWorld(states[i]);
                ProcessNavigationPathRequestQueue();
            }
        }
        finally
        {
            _world = previousWorld;
            _activeWorldState = previousState;
        }
    }

    private static void AdvanceNavigationPathRequestOneOperation(
        NavigationPathRequestJob job,
        ref bool policyMutationStarted)
    {
        if (job.SourceCursor >= job.Sources.Count)
        {
            if (RestartCompletedNavigationPathSourceMergesForLatestStarts(job))
                return;
            CommitNavigationPathRequest(job);
            job.Complete = true;
            return;
        }

        NavigationPathSourceJob source = job.Sources[job.SourceCursor];
        switch (source.Stage)
        {
            case NavigationPathSourceStage.Initialize:
                InitializeNavigationPathSource(job, source);
                break;
            case NavigationPathSourceStage.BuildGoalConnector:
                AdvanceNavigationGoalConnector(job, source);
                break;
            case NavigationPathSourceStage.CreateHierarchyPolicy:
                CreateNavigationHierarchyPolicy(job, source, ref policyMutationStarted);
                break;
            case NavigationPathSourceStage.ExpandHierarchyPolicy:
                AdvanceNavigationHierarchyPolicy(job, source, ref policyMutationStarted);
                break;
            case NavigationPathSourceStage.BuildDownwardCustomization:
                AdvanceNavigationDownwardCustomization(job, source, ref policyMutationStarted);
                break;
            case NavigationPathSourceStage.ExpandL0Policy:
                AdvanceNavigationL0Policy(job, source, ref policyMutationStarted);
                break;
            case NavigationPathSourceStage.MaterializeRoute:
                AdvanceNavigationPathSourceMaterialization(job, source, ref policyMutationStarted);
                break;
            case NavigationPathSourceStage.Complete:
                job.SourceCursor++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(source.Stage), source.Stage, "Unknown navigation path source stage.");
        }
    }

    private static bool RestartCompletedNavigationPathSourceMergesForLatestStarts(
        NavigationPathRequestJob job)
    {
        int firstRestartedSourceIndex = -1;
        for (int i = 0; i < job.Sources.Count; i++)
        {
            NavigationPathSourceJob source = job.Sources[i]
                ?? throw new InvalidOperationException("Navigation path request contains a null source job.");
            NavigationPathDemand demand = source.Demand
                ?? throw new InvalidOperationException("Navigation path request source has no build demand snapshot.");
            NavigationPathDemand latestDemand = source.LatestDemand
                ?? throw new InvalidOperationException("Navigation path request source has no latest demand snapshot.");
            if (source.Stage != NavigationPathSourceStage.Complete || source.Handle == null)
                throw new InvalidOperationException("Navigation path request reached commit with a partial source.");
            RequireMatchingNavigationPathDemandIdentity(demand, latestDemand);
            if (FindSectorIndex(source.Handle, latestDemand.StartSectorId, 0) >= 0)
                continue;

            demand.StartSectorId = latestDemand.StartSectorId;
            demand.StartX = latestDemand.StartX;
            demand.StartY = latestDemand.StartY;
            ResetNavigationPathSourceJobForLatestStart(source);
            if (firstRestartedSourceIndex < 0)
                firstRestartedSourceIndex = i;
        }

        if (firstRestartedSourceIndex < 0)
            return false;
        job.SourceCursor = firstRestartedSourceIndex;
        return true;
    }

    private static void RequireMatchingNavigationPathDemandIdentity(
        NavigationPathDemand buildDemand,
        NavigationPathDemand latestDemand)
    {
        if (latestDemand.SourceId != buildDemand.SourceId
            || latestDemand.MovingTargetId != buildDemand.MovingTargetId
            || latestDemand.AgentTypeId != buildDemand.AgentTypeId
            || latestDemand.SourceIslandId != buildDemand.SourceIslandId)
        {
            throw new InvalidOperationException("Navigation path request latest demand changed request identity.");
        }
    }

    private static void InitializeNavigationPathSource(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source)
    {
        NavigationPathDemand demand = source.Demand;
        if (demand.StartSectorId == demand.GoalSectorId)
        {
            source.Stage = AreCellsConnectedInsideSector(
                    _world.Sectors[demand.StartSectorId],
                    demand.StartX,
                    demand.StartY,
                    job.GoalX,
                    job.GoalY)
                ? NavigationPathSourceStage.MaterializeRoute
                : NavigationPathSourceStage.ExpandL0Policy;
            return;
        }

        EnsureNavigationPathRequestPolicy(job);
        source.HierarchyLevelArrayIndex = ResolveHighestRequiredPortalHierarchyLevelIndex(
            _world,
            demand.StartSectorId,
            demand.GoalSectorId);
        if (source.HierarchyLevelArrayIndex < 0)
        {
            source.Stage = NavigationPathSourceStage.ExpandL0Policy;
            return;
        }

        if (job.Policy.HierarchyPolicies.TryGetValue(
                source.HierarchyLevelArrayIndex,
                out PortalHierarchyReversePolicy cached))
        {
            source.HierarchyPolicy = cached;
            source.NextDownwardLevel = source.HierarchyLevelArrayIndex;
            source.Stage = NavigationPathSourceStage.ExpandHierarchyPolicy;
            return;
        }

        PortalHierarchyConnector lowerConnector = null;
        for (int level = source.HierarchyLevelArrayIndex - 1; level >= 0; level--)
        {
            if (!job.Policy.HierarchyPolicies.TryGetValue(level, out PortalHierarchyReversePolicy lowerPolicy))
                continue;
            ValidatePortalHierarchyGoalPolicy(lowerPolicy, job.Policy.GoalSectorId, lowerPolicy.GoalX, lowerPolicy.GoalY);
            lowerConnector = lowerPolicy.GoalConnector;
            break;
        }
        source.GoalConnector = lowerConnector;
        source.NextGoalConnectorLevel = lowerConnector == null ? 0 : lowerConnector.TargetLevelIndex + 1;
        _perf.SectorCorridorGoalConnectorBuilds++;
        source.Stage = NavigationPathSourceStage.BuildGoalConnector;
    }

    private static void EnsureNavigationPathRequestPolicy(NavigationPathRequestJob job)
    {
        if (job.Policy != null)
            return;
        if (SectorCorridorPolicies.TryGetValue(job.PolicyKey, out SectorCorridorPolicy cached))
        {
            job.Policy = cached;
            return;
        }

        if (job.Key.MovingTargetId != int.MinValue)
        {
            var anchorKey = new MovingTargetAnchorKey(
                job.Key.MovingTargetId,
                job.Key.AgentTypeId,
                job.Key.SourceIslandId);
            if (!MovingTargetAnchors.TryGetValue(anchorKey, out MovingTargetAnchor anchor) || anchor == null)
            {
                throw new InvalidOperationException(
                    $"Navigation path request has no moving-target anchor target={job.Key.MovingTargetId}, agentType={job.Key.AgentTypeId}, island={job.Key.SourceIslandId}.");
            }
            if (anchor.HasPinnedSectorCorridorPolicy
                && !anchor.PinnedSectorCorridorPolicyKey.Equals(job.PolicyKey))
            {
                RemoveSectorCorridorPolicy(anchor.PinnedSectorCorridorPolicyKey);
                anchor.HasPinnedSectorCorridorPolicy = false;
                anchor.PinnedSectorCorridorPolicyKey = default;
            }
        }

        job.Policy = new SectorCorridorPolicy
        {
            GoalSectorId = job.Key.GoalSectorId,
            GoalCellIndex = _world.GetIndex(job.GoalX, job.GoalY),
            GoalSectorDirtyVersion = job.Key.GoalSectorDirtyVersion,
            LastUsedFrame = GetFrameCount()
        };
        SetSectorCorridorPolicy(job.PolicyKey, job.Policy);
        if (job.Key.MovingTargetId != int.MinValue)
        {
            var anchorKey = new MovingTargetAnchorKey(
                job.Key.MovingTargetId,
                job.Key.AgentTypeId,
                job.Key.SourceIslandId);
            PinMovingTargetSectorCorridorPolicy(MovingTargetAnchors[anchorKey], job.PolicyKey);
        }
        _perf.SectorPathSearches++;
    }

    private static IncrementalRestrictedPortalSearch CreateIncrementalRestrictedPortalSearch(
        NavigationWorld world,
        PortalHierarchyCluster containingCluster,
        PortalHierarchyLevel lowerLevel,
        IReadOnlyList<int> sourceNodes,
        IReadOnlyList<long> sourceCosts,
        bool reverse,
        IReadOnlyList<int> targetNodes)
    {
        if (world == null || containingCluster == null)
            throw new ArgumentNullException("Incremental restricted search requires a world and containing cluster.");
        if (sourceNodes == null || sourceCosts == null || sourceNodes.Count != sourceCosts.Count)
            throw new ArgumentException("Incremental restricted search sources are invalid.");
        if (targetNodes == null || targetNodes.Count == 0)
            throw new ArgumentException("Incremental restricted search targets are empty.");
        var search = new IncrementalRestrictedPortalSearch
        {
            World = world,
            ContainingCluster = containingCluster,
            LowerLevel = lowerLevel,
            Reverse = reverse,
            SourceNodes = sourceNodes,
            SourceCosts = sourceCosts,
            TargetNodeSequence = targetNodes,
            RemainingTargets = targetNodes.Count,
            Stage = IncrementalRestrictedSearchStage.InitializeTargets
        };
        return search;
    }

    private static bool AdvanceIncrementalRestrictedPortalSearchOneOperation(
        IncrementalRestrictedPortalSearch search)
    {
        if (search == null)
            throw new ArgumentNullException(nameof(search));
        switch (search.Stage)
        {
            case IncrementalRestrictedSearchStage.InitializeTargets:
            {
                if (search.TargetInitializationCursor < search.TargetNodeSequence.Count)
                {
                    int target = search.TargetNodeSequence[search.TargetInitializationCursor++];
                    if (!search.TargetNodes.Add(target))
                        throw new InvalidOperationException("Incremental restricted search contains a duplicate target.");
                    return false;
                }
                search.Stage = IncrementalRestrictedSearchStage.InitializeSources;
                return false;
            }
            case IncrementalRestrictedSearchStage.InitializeSources:
            {
                if (search.SourceInitializationCursor < search.SourceNodes.Count)
                {
                    int index = search.SourceInitializationCursor++;
                    int node = search.SourceNodes[index];
                    DecodePortalNode(node, out int sectorId, out _);
                    if (!HierarchyClusterContainsSector(search.World, search.ContainingCluster, sectorId))
                        throw new InvalidOperationException("Incremental restricted search source is outside its containing cluster.");
                    long sourceCost = search.SourceCosts[index];
                    if (!search.Result.Costs.TryGetValue(node, out long existing) || sourceCost < existing)
                    {
                        search.Result.Costs[node] = sourceCost;
                        search.OpenSet.Push(node, sourceCost);
                    }
                    return false;
                }
                if (search.OpenSet.Count == 0)
                    throw new InvalidOperationException("Incremental restricted search has no reachable source.");
                search.Stage = IncrementalRestrictedSearchStage.Pop;
                return false;
            }
            case IncrementalRestrictedSearchStage.Pop:
            {
                if (search.OpenSet.Count == 0)
                {
                    search.Stage = IncrementalRestrictedSearchStage.Complete;
                    return true;
                }
                DeterministicCostQueueNode item = search.OpenSet.Pop();
                if (!search.Result.Costs.TryGetValue(item.Index, out long currentCost)
                    || item.Cost != currentCost
                    || !search.Result.SettledNodes.Add(item.Index))
                {
                    return false;
                }
                search.Result.ExpansionCount++;
                if (search.TargetNodes.Contains(item.Index))
                    search.RemainingTargets--;
                if (search.RemainingTargets == 0)
                {
                    search.Stage = IncrementalRestrictedSearchStage.Complete;
                    return true;
                }
                DecodePortalNode(item.Index, out search.CurrentSectorId, out search.CurrentPortalId);
                if (!HierarchyClusterContainsSector(
                        search.World,
                        search.ContainingCluster,
                        search.CurrentSectorId))
                {
                    throw new InvalidOperationException("Incremental restricted search expanded outside its containing cluster.");
                }
                search.CurrentNode = item.Index;
                search.CurrentCost = currentCost;
                search.CurrentL0Edges = null;
                search.CurrentHierarchyEdges = null;
                search.EdgeCursor = 0;
                if (search.LowerLevel == null)
                {
                    search.CurrentL0Edges = search.Reverse
                        ? GetIncomingPortalTransitions(search.World.Sectors[search.CurrentSectorId], search.CurrentPortalId)
                        : GetOutgoingPortalTransitions(search.World.Sectors[search.CurrentSectorId], search.CurrentPortalId);
                }
                else
                {
                    int clusterId = ResolveHierarchyClusterId(
                        search.World,
                        search.LowerLevel,
                        search.CurrentSectorId);
                    PortalHierarchyCluster cluster = search.LowerLevel.Clusters[clusterId];
                    Dictionary<int, List<PortalHierarchyEdge>> edgeIndex = search.Reverse
                        ? cluster.IncomingEdgesByNode
                        : cluster.OutgoingEdgesByNode;
                    if (!edgeIndex.TryGetValue(item.Index, out search.CurrentHierarchyEdges))
                        throw new InvalidOperationException("Incremental restricted hierarchy search is missing an overlay node.");
                }
                search.Stage = IncrementalRestrictedSearchStage.Crossing;
                return false;
            }
            case IncrementalRestrictedSearchStage.Crossing:
            {
                PortalData portal = GetPortalById(search.World, search.CurrentPortalId);
                int oppositeSectorId = GetOppositeSectorId(portal, search.CurrentSectorId);
                if (search.LowerLevel != null
                    && ResolveHierarchyClusterId(search.World, search.LowerLevel, oppositeSectorId)
                    == ResolveHierarchyClusterId(search.World, search.LowerLevel, search.CurrentSectorId))
                {
                    throw new InvalidOperationException("Incremental restricted hierarchy search found an internal overlay crossing.");
                }
                if (HierarchyClusterContainsSector(search.World, search.ContainingCluster, oppositeSectorId))
                {
                    RelaxRestrictedPortalGraphNode(
                        search.Result,
                        search.OpenSet,
                        search.CurrentNode,
                        EncodePortalNode(oppositeSectorId, search.CurrentPortalId),
                        AddDeterministicPortalCosts(search.CurrentCost, DeterministicPortalCrossingCost),
                        search.Reverse);
                }
                search.Stage = IncrementalRestrictedSearchStage.Edges;
                return false;
            }
            case IncrementalRestrictedSearchStage.Edges:
            {
                int edgeCount = search.LowerLevel == null
                    ? search.CurrentL0Edges?.Count ?? 0
                    : search.CurrentHierarchyEdges?.Count ?? 0;
                if (search.EdgeCursor >= edgeCount)
                {
                    search.Stage = IncrementalRestrictedSearchStage.Pop;
                    return false;
                }
                if (search.LowerLevel == null)
                {
                    PortalTransition edge = search.CurrentL0Edges[search.EdgeCursor++];
                    int nextPortalId = search.Reverse ? edge.FromPortalId : edge.ToPortalId;
                    RelaxRestrictedPortalGraphNode(
                        search.Result,
                        search.OpenSet,
                        search.CurrentNode,
                        EncodePortalNode(search.CurrentSectorId, nextPortalId),
                        AddDeterministicPortalCosts(search.CurrentCost, edge.DeterministicCost),
                        search.Reverse);
                }
                else
                {
                    PortalHierarchyEdge edge = search.CurrentHierarchyEdges[search.EdgeCursor++];
                    int nextNode = search.Reverse ? edge.FromNode : edge.ToNode;
                    RelaxRestrictedPortalGraphNode(
                        search.Result,
                        search.OpenSet,
                        search.CurrentNode,
                        nextNode,
                        AddDeterministicPortalCosts(search.CurrentCost, edge.DeterministicCost),
                        search.Reverse);
                }
                return false;
            }
            case IncrementalRestrictedSearchStage.Complete:
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(search.Stage), search.Stage, "Unknown incremental restricted search stage.");
        }
    }

    private static void AdvanceNavigationGoalConnector(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source)
    {
        PortalHierarchy hierarchy = _world.Hierarchy
            ?? throw new InvalidOperationException("Navigation path request requires a committed hierarchy.");
        if (source.NextGoalConnectorLevel > source.HierarchyLevelArrayIndex)
        {
            source.Stage = NavigationPathSourceStage.CreateHierarchyPolicy;
            return;
        }

        if (source.GoalConnector == null)
        {
            PortalHierarchyLevel level = hierarchy.Levels[0];
            PortalHierarchyCluster cluster = level.Clusters[
                ResolveHierarchyClusterId(_world, level, job.Key.GoalSectorId)];
            if (source.RestrictedSearch == null)
            {
                SectorData goalSector = _world.Sectors[job.Key.GoalSectorId];
                if (!source.RestrictedInputCollectionActive)
                {
                    BeginRestrictedInputCollection(source);
                    return;
                }
                if (source.RestrictedSourceCollectionCursor < goalSector.PortalIds.Count)
                {
                    int portalId = goalSector.PortalIds[source.RestrictedSourceCollectionCursor++];
                    long cost = ResolveDeterministicGoalSectorPortalAccessCost(
                        goalSector,
                        job.Key.GoalSectorId,
                        portalId,
                        job.Policy.GoalCellIndex % _world.Width,
                        job.Policy.GoalCellIndex / _world.Width);
                    if (cost == long.MaxValue)
                        return;
                    source.RestrictedSourceNodes.Add(EncodePortalNode(job.Key.GoalSectorId, portalId));
                    source.RestrictedSourceCosts.Add(cost);
                    return;
                }
                if (source.RestrictedTargetCollectionCursor < cluster.BoundaryNodes.Length)
                {
                    source.RestrictedTargetNodes.Add(
                        cluster.BoundaryNodes[source.RestrictedTargetCollectionCursor++]);
                    return;
                }
                source.RestrictedSearch = CreateIncrementalRestrictedPortalSearch(
                    _world,
                    cluster,
                    null,
                    source.RestrictedSourceNodes.ToArray(),
                    source.RestrictedSourceCosts.ToArray(),
                    reverse: true,
                    source.RestrictedTargetNodes.ToArray());
                EndRestrictedInputCollection(source);
                return;
            }
            if (!AdvanceIncrementalRestrictedPortalSearchOneOperation(source.RestrictedSearch))
                return;
            source.GoalConnector = new PortalHierarchyConnector
            {
                TargetLevelIndex = 0,
                Search = source.RestrictedSearch.Result
            };
            source.RestrictedSearch = null;
            source.NextGoalConnectorLevel = 1;
            return;
        }

        if (source.RestrictedSearch == null)
        {
            PortalHierarchyLevel targetLevel = hierarchy.Levels[source.NextGoalConnectorLevel];
            PortalHierarchyCluster targetCluster = targetLevel.Clusters[
                ResolveHierarchyClusterId(_world, targetLevel, job.Key.GoalSectorId)];
            PortalHierarchyLevel lowerLevel = hierarchy.Levels[source.NextGoalConnectorLevel - 1];
            PortalHierarchyCluster childCluster = lowerLevel.Clusters[
                ResolveHierarchyClusterId(_world, lowerLevel, job.Key.GoalSectorId)];
            if (!source.RestrictedInputCollectionActive)
            {
                BeginRestrictedInputCollection(source);
                return;
            }
            if (source.RestrictedSourceCollectionCursor < childCluster.BoundaryNodes.Length)
            {
                int node = childCluster.BoundaryNodes[source.RestrictedSourceCollectionCursor++];
                if (!source.GoalConnector.Search.Costs.TryGetValue(node, out long cost))
                    return;
                if (!source.GoalConnector.Search.SettledNodes.Contains(node))
                    throw new InvalidOperationException("Navigation child goal connector exposed an unsettled boundary.");
                source.RestrictedSourceNodes.Add(node);
                source.RestrictedSourceCosts.Add(cost);
                return;
            }
            if (source.RestrictedTargetCollectionCursor < targetCluster.BoundaryNodes.Length)
            {
                source.RestrictedTargetNodes.Add(
                    targetCluster.BoundaryNodes[source.RestrictedTargetCollectionCursor++]);
                return;
            }
            source.RestrictedSearch = CreateIncrementalRestrictedPortalSearch(
                _world,
                targetCluster,
                lowerLevel,
                source.RestrictedSourceNodes.ToArray(),
                source.RestrictedSourceCosts.ToArray(),
                reverse: true,
                source.RestrictedTargetNodes.ToArray());
            EndRestrictedInputCollection(source);
            return;
        }
        if (!AdvanceIncrementalRestrictedPortalSearchOneOperation(source.RestrictedSearch))
            return;
        source.GoalConnector = new PortalHierarchyConnector
        {
            TargetLevelIndex = source.NextGoalConnectorLevel,
            Search = source.RestrictedSearch.Result,
            Child = source.GoalConnector
        };
        source.RestrictedSearch = null;
        source.NextGoalConnectorLevel++;
    }

    private static void BeginRestrictedInputCollection(NavigationPathSourceJob source)
    {
        source.RestrictedSourceNodes.Clear();
        source.RestrictedSourceCosts.Clear();
        source.RestrictedTargetNodes.Clear();
        source.RestrictedSourceCollectionCursor = 0;
        source.RestrictedTargetCollectionCursor = 0;
        source.RestrictedInputCollectionActive = true;
    }

    private static void EndRestrictedInputCollection(NavigationPathSourceJob source)
    {
        source.RestrictedSourceNodes.Clear();
        source.RestrictedSourceCosts.Clear();
        source.RestrictedTargetNodes.Clear();
        source.RestrictedSourceCollectionCursor = 0;
        source.RestrictedTargetCollectionCursor = 0;
        source.RestrictedInputCollectionActive = false;
    }

    private static void CreateNavigationHierarchyPolicy(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        ref bool policyMutationStarted)
    {
        PortalHierarchyLevel level = _world.Hierarchy.Levels[source.HierarchyLevelArrayIndex];
        PortalHierarchyCluster goalCluster = level.Clusters[
            ResolveHierarchyClusterId(_world, level, job.Key.GoalSectorId)];
        if (source.HierarchyPolicy == null)
        {
            source.HierarchyPolicy = new PortalHierarchyReversePolicy
            {
                LevelArrayIndex = source.HierarchyLevelArrayIndex,
                GoalSectorId = job.Key.GoalSectorId,
                GoalX = job.Policy.GoalCellIndex % _world.Width,
                GoalY = job.Policy.GoalCellIndex / _world.Width,
                GoalConnector = source.GoalConnector,
                GoalConnectorAuthorityContentHash = ComputePortalHierarchyConnectorAuthorityContentHash(source.GoalConnector)
            };
            source.HierarchyPolicyBoundaryCursor = 0;
            return;
        }

        if (source.HierarchyPolicyBoundaryCursor < goalCluster.BoundaryNodes.Length)
        {
            int node = goalCluster.BoundaryNodes[source.HierarchyPolicyBoundaryCursor++];
            if (!source.GoalConnector.Search.Costs.TryGetValue(node, out long cost))
                return;
            if (!source.GoalConnector.Search.SettledNodes.Contains(node))
                throw new InvalidOperationException("Navigation goal connector exposed an unsettled boundary.");
            SetPortalHierarchyReversePolicyNodeCost(source.HierarchyPolicy, node, cost);
            source.HierarchyPolicy.OpenSet.Push(node, cost);
            return;
        }

        if (source.HierarchyPolicy.OpenSet.Count == 0)
            throw new InvalidOperationException("Navigation hierarchy policy has no goal boundary source.");
        BeginHashedPortalHierarchyPolicyMutation(job.Policy, ref policyMutationStarted);
        job.Policy.HierarchyPolicies.Add(source.HierarchyLevelArrayIndex, source.HierarchyPolicy);
        source.NextDownwardLevel = source.HierarchyLevelArrayIndex;
        source.Stage = NavigationPathSourceStage.ExpandHierarchyPolicy;
    }

    private static void AdvanceNavigationHierarchyPolicy(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        ref bool policyMutationStarted)
    {
        if (source.ReversePolicyExpansion == null)
        {
            PortalHierarchyLevel level = _world.Hierarchy.Levels[source.HierarchyLevelArrayIndex];
            source.ReversePolicyExpansion = new IncrementalReversePolicyExpansion
            {
                Hierarchy = true,
                HierarchyLevel = level,
                HierarchyTargetCluster = level.Clusters[
                    ResolveHierarchyClusterId(_world, level, source.Demand.StartSectorId)],
                Stage = IncrementalReversePolicyExpansionStage.CollectTargets
            };
            return;
        }

        AdvanceReversePolicyExpansionOneOperation(job, source, ref policyMutationStarted);
        if (source.ReversePolicyExpansion.Stage == IncrementalReversePolicyExpansionStage.Complete)
        {
            source.ReversePolicyExpansion = null;
            source.Stage = NavigationPathSourceStage.BuildDownwardCustomization;
            return;
        }
    }

    private static void AdvanceNavigationDownwardCustomization(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        ref bool policyMutationStarted)
    {
        if (source.NextDownwardLevel < 0)
        {
            source.Stage = NavigationPathSourceStage.MaterializeRoute;
            return;
        }

        PortalHierarchy hierarchy = _world.Hierarchy;
        int levelIndex = source.NextDownwardLevel;
        PortalHierarchyLevel sourceLevel = hierarchy.Levels[levelIndex];
        PortalHierarchyCluster containingCluster = sourceLevel.Clusters[
            ResolveHierarchyClusterId(_world, sourceLevel, source.Demand.StartSectorId)];
        int targetRegionId = levelIndex == 0
            ? source.Demand.StartSectorId
            : ResolveHierarchyClusterId(
                _world,
                hierarchy.Levels[levelIndex - 1],
                source.Demand.StartSectorId);
        var key = new PortalHierarchyCustomizationKey(levelIndex, containingCluster.ClusterId, targetRegionId);
        if (source.HierarchyPolicy.DownwardCustomizations.TryGetValue(
                key,
                out PortalHierarchyDownwardCustomization cached))
        {
            source.DownwardCustomizations.Add(cached);
            source.NextDownwardLevel--;
            return;
        }

        if (source.RestrictedSearch == null)
        {
            PortalGraphSearchResult previousSearch = source.DownwardCustomizations.Count == 0
                ? null
                : source.DownwardCustomizations[source.DownwardCustomizations.Count - 1].Search;
            bool usePolicySources = levelIndex == source.HierarchyLevelArrayIndex;
            if (!source.RestrictedInputCollectionActive)
            {
                BeginRestrictedInputCollection(source);
                return;
            }
            if (source.RestrictedSourceCollectionCursor < containingCluster.BoundaryNodes.Length)
            {
                int node = containingCluster.BoundaryNodes[source.RestrictedSourceCollectionCursor++];
                bool settled = usePolicySources
                    ? source.HierarchyPolicy.SettledNodes.Contains(node)
                    : previousSearch != null && previousSearch.SettledNodes.Contains(node);
                if (!settled)
                    return;
                bool hasCost = usePolicySources
                    ? source.HierarchyPolicy.NodeCosts.TryGetValue(node, out long cost)
                    : previousSearch.Costs.TryGetValue(node, out cost);
                if (!hasCost)
                    throw new InvalidOperationException("Navigation downward customization settled source has no cost.");
                source.RestrictedSourceNodes.Add(node);
                source.RestrictedSourceCosts.Add(cost);
                return;
            }

            if (levelIndex == 0)
            {
                List<int> portals = _world.Sectors[source.Demand.StartSectorId].PortalIds;
                if (source.RestrictedTargetCollectionCursor < portals.Count)
                {
                    int portalId = portals[source.RestrictedTargetCollectionCursor++];
                    source.RestrictedTargetNodes.Add(
                        EncodePortalNode(source.Demand.StartSectorId, portalId));
                    return;
                }
            }
            else
            {
                PortalHierarchyLevel childLevel = hierarchy.Levels[levelIndex - 1];
                PortalHierarchyCluster childCluster = childLevel.Clusters[
                    ResolveHierarchyClusterId(_world, childLevel, source.Demand.StartSectorId)];
                if (source.RestrictedTargetCollectionCursor < childCluster.BoundaryNodes.Length)
                {
                    source.RestrictedTargetNodes.Add(
                        childCluster.BoundaryNodes[source.RestrictedTargetCollectionCursor++]);
                    return;
                }
            }
            if (source.RestrictedSourceNodes.Count == 0)
                throw new InvalidOperationException("Navigation downward customization has no settled source.");
            if (source.RestrictedTargetNodes.Count == 0)
                throw new InvalidOperationException("Navigation downward customization has no target nodes.");
            source.RestrictedSearch = CreateIncrementalRestrictedPortalSearch(
                _world,
                containingCluster,
                levelIndex == 0 ? null : hierarchy.Levels[levelIndex - 1],
                source.RestrictedSourceNodes.ToArray(),
                source.RestrictedSourceCosts.ToArray(),
                reverse: true,
                source.RestrictedTargetNodes.ToArray());
            EndRestrictedInputCollection(source);
            return;
        }
        if (!AdvanceIncrementalRestrictedPortalSearchOneOperation(source.RestrictedSearch))
            return;
        PortalGraphSearchResult search = source.RestrictedSearch.Result;
        source.RestrictedSearch = null;
        var created = new PortalHierarchyDownwardCustomization
        {
            SourceLevelArrayIndex = levelIndex,
            ClusterId = containingCluster.ClusterId,
            TargetRegionId = targetRegionId,
            Search = search
        };
        created.AuthorityContentHash = ComputePortalHierarchyDownwardCustomizationAuthorityContentHash(key, created);
        BeginHashedPortalHierarchyPolicyMutation(job.Policy, ref policyMutationStarted);
        source.HierarchyPolicy.DownwardCustomizations.Add(key, created);
        source.HierarchyPolicy.DownwardCustomizationsAuthorityContentHash ^=
            ComputePortalHierarchyCustomizationEntryAuthorityToken(key, created.AuthorityContentHash);
        _perf.HierarchyDownwardCustomizationExpansions = checked(
            _perf.HierarchyDownwardCustomizationExpansions + search.ExpansionCount);
        source.DownwardCustomizations.Add(created);
        source.NextDownwardLevel--;
    }

    private static void AdvanceNavigationL0Policy(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        ref bool policyMutationStarted)
    {
        if (source.ReversePolicyExpansion == null)
        {
            bool policyEmpty = job.Policy.PortalOpenSet.Count == 0
                               && job.Policy.NodeCosts.Count == 0
                               && job.Policy.NextNodeTowardGoal.Count == 0
                               && job.Policy.SettledPortalNodes.Count == 0;
            source.ReversePolicyExpansion = new IncrementalReversePolicyExpansion
            {
                Hierarchy = false,
                L0TargetSector = _world.Sectors[source.Demand.StartSectorId],
                Stage = policyEmpty
                    ? IncrementalReversePolicyExpansionStage.InitializeGoal
                    : IncrementalReversePolicyExpansionStage.CollectTargets
            };
            _perf.SectorCorridorPolicyQueries++;
            return;
        }

        AdvanceReversePolicyExpansionOneOperation(job, source, ref policyMutationStarted);
        if (source.ReversePolicyExpansion.Stage == IncrementalReversePolicyExpansionStage.Complete)
        {
            source.ReversePolicyExpansion = null;
            source.Stage = NavigationPathSourceStage.MaterializeRoute;
        }
    }

    private static void AdvanceReversePolicyExpansionOneOperation(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        ref bool policyMutationStarted)
    {
        IncrementalReversePolicyExpansion expansion = source.ReversePolicyExpansion
            ?? throw new InvalidOperationException("Navigation reverse policy expansion state is missing.");
        switch (expansion.Stage)
        {
            case IncrementalReversePolicyExpansionStage.InitializeGoal:
                AdvanceL0GoalInitializationOneOperation(job, expansion, ref policyMutationStarted);
                return;
            case IncrementalReversePolicyExpansionStage.CollectTargets:
                AdvanceReversePolicyTargetCollectionOneOperation(job, source, expansion);
                return;
            case IncrementalReversePolicyExpansionStage.Pop:
                AdvanceReversePolicyPopOneOperation(job, source, expansion, ref policyMutationStarted);
                return;
            case IncrementalReversePolicyExpansionStage.Edges:
                AdvanceReversePolicyEdgeOneOperation(job, source, expansion, ref policyMutationStarted);
                return;
            case IncrementalReversePolicyExpansionStage.Crossing:
                AdvanceReversePolicyCrossingOneOperation(job, source, expansion, ref policyMutationStarted);
                return;
            case IncrementalReversePolicyExpansionStage.Complete:
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(expansion.Stage), expansion.Stage, "Unknown reverse policy expansion stage.");
        }
    }

    private static void AdvanceL0GoalInitializationOneOperation(
        NavigationPathRequestJob job,
        IncrementalReversePolicyExpansion expansion,
        ref bool policyMutationStarted)
    {
        SectorData goalSector = _world.Sectors[job.Key.GoalSectorId];
        if (expansion.GoalPortalCursor >= goalSector.PortalIds.Count)
        {
            if (job.Policy.PortalOpenSet.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Navigation L0 policy goal sector has no reachable portal sector={job.Key.GoalSectorId}.");
            }
            expansion.Stage = IncrementalReversePolicyExpansionStage.CollectTargets;
            return;
        }

        int portalId = goalSector.PortalIds[expansion.GoalPortalCursor++];
        long goalCost = ResolveDeterministicGoalSectorPortalAccessCost(
            goalSector,
            job.Key.GoalSectorId,
            portalId,
            job.Policy.GoalCellIndex % _world.Width,
            job.Policy.GoalCellIndex / _world.Width);
        if (goalCost == long.MaxValue)
            return;
        BeginHashedPortalHierarchyPolicyMutation(job.Policy, ref policyMutationStarted);
        int goalNode = EncodePortalNode(job.Key.GoalSectorId, portalId);
        SetSectorCorridorPolicyNodeCost(job.Policy, goalNode, goalCost);
        job.Policy.PortalOpenSet.Push(goalNode, goalCost);
    }

    private static void AdvanceReversePolicyTargetCollectionOneOperation(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalReversePolicyExpansion expansion)
    {
        if (expansion.Hierarchy)
        {
            int[] boundaries = expansion.HierarchyTargetCluster.BoundaryNodes;
            if (expansion.TargetCursor < boundaries.Length)
            {
                int node = boundaries[expansion.TargetCursor++];
                expansion.HasAccessibleTarget = true;
                if (!source.HierarchyPolicy.SettledNodes.Contains(node))
                    expansion.PendingTargetNodes.Add(node);
                return;
            }
        }
        else
        {
            List<int> portals = expansion.L0TargetSector.PortalIds;
            if (expansion.TargetCursor < portals.Count)
            {
                int portalId = portals[expansion.TargetCursor++];
                long accessCost = ResolveDeterministicPortalAccessCost(
                    expansion.L0TargetSector,
                    source.Demand.StartSectorId,
                    portalId,
                    source.Demand.StartX,
                    source.Demand.StartY);
                if (accessCost != long.MaxValue)
                {
                    expansion.HasAccessibleTarget = true;
                    int node = EncodePortalNode(source.Demand.StartSectorId, portalId);
                    if (!job.Policy.SettledPortalNodes.Contains(node))
                        expansion.PendingTargetNodes.Add(node);
                }
                return;
            }
        }

        if (!expansion.HasAccessibleTarget)
            throw new InvalidOperationException("Navigation reverse policy source has no accessible portal target.");
        expansion.Stage = expansion.PendingTargetNodes.Count == 0
            ? IncrementalReversePolicyExpansionStage.Complete
            : IncrementalReversePolicyExpansionStage.Pop;
    }

    private static void AdvanceReversePolicyPopOneOperation(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalReversePolicyExpansion expansion,
        ref bool policyMutationStarted)
    {
        DeterministicCostHeap openSet = expansion.Hierarchy
            ? source.HierarchyPolicy.OpenSet
            : job.Policy.PortalOpenSet;
        if (openSet.Count == 0)
            throw new InvalidOperationException("Navigation reverse policy exhausted before reaching every source boundary.");

        BeginHashedPortalHierarchyPolicyMutation(job.Policy, ref policyMutationStarted);
        DeterministicCostQueueNode item = openSet.Pop();
        bool settled;
        long currentCost;
        if (expansion.Hierarchy)
        {
            settled = source.HierarchyPolicy.NodeCosts.TryGetValue(item.Index, out currentCost)
                      && item.Cost == currentCost
                      && AddPortalHierarchyReversePolicySettledNode(source.HierarchyPolicy, item.Index);
        }
        else
        {
            settled = job.Policy.NodeCosts.TryGetValue(item.Index, out currentCost)
                      && item.Cost == currentCost
                      && !job.Policy.SettledPortalNodes.Contains(item.Index);
            if (settled)
                AddSectorCorridorPolicySettledNode(job.Policy, item.Index);
        }
        if (!settled)
            return;

        _perf.PathPortalGraphNodeExpansions++;
        expansion.PendingTargetNodes.Remove(item.Index);
        expansion.CompleteAfterCurrentExpansion = expansion.PendingTargetNodes.Count == 0;

        expansion.CurrentNode = item.Index;
        expansion.CurrentCost = currentCost;
        DecodePortalNode(item.Index, out expansion.CurrentSectorId, out expansion.CurrentPortalId);
        expansion.EdgeCursor = 0;
        if (expansion.Hierarchy)
        {
            int clusterId = ResolveHierarchyClusterId(_world, expansion.HierarchyLevel, expansion.CurrentSectorId);
            PortalHierarchyCluster cluster = expansion.HierarchyLevel.Clusters[clusterId];
            if (!cluster.IncomingEdgesByNode.TryGetValue(item.Index, out expansion.CurrentHierarchyEdges))
                throw new InvalidOperationException("Navigation hierarchy reverse policy is missing an incoming edge list.");
            expansion.Stage = IncrementalReversePolicyExpansionStage.Edges;
        }
        else
        {
            expansion.CurrentL0Edges = GetIncomingPortalTransitions(
                _world.Sectors[expansion.CurrentSectorId],
                expansion.CurrentPortalId);
            expansion.Stage = IncrementalReversePolicyExpansionStage.Crossing;
        }
    }

    private static void AdvanceReversePolicyEdgeOneOperation(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalReversePolicyExpansion expansion,
        ref bool policyMutationStarted)
    {
        int count = expansion.Hierarchy
            ? expansion.CurrentHierarchyEdges?.Count ?? 0
            : expansion.CurrentL0Edges?.Count ?? 0;
        if (expansion.EdgeCursor >= count)
        {
            if (expansion.Hierarchy)
            {
                expansion.Stage = IncrementalReversePolicyExpansionStage.Crossing;
            }
            else
            {
                expansion.Stage = expansion.CompleteAfterCurrentExpansion
                    ? IncrementalReversePolicyExpansionStage.Complete
                    : IncrementalReversePolicyExpansionStage.Pop;
            }
            return;
        }

        BeginHashedPortalHierarchyPolicyMutation(job.Policy, ref policyMutationStarted);
        _perf.PathPortalGraphOutgoingTransitionScans++;
        _perf.PathPortalGraphOutgoingTransitionHits++;
        if (expansion.Hierarchy)
        {
            PortalHierarchyEdge edge = expansion.CurrentHierarchyEdges[expansion.EdgeCursor++];
            RelaxPortalHierarchyReversePolicyNode(
                source.HierarchyPolicy,
                edge.FromNode,
                expansion.CurrentNode,
                AddDeterministicPortalCosts(expansion.CurrentCost, edge.DeterministicCost));
        }
        else
        {
            PortalTransition transition = expansion.CurrentL0Edges[expansion.EdgeCursor++];
            if (transition.ToPortalId != expansion.CurrentPortalId)
                throw new InvalidOperationException("Navigation L0 reverse policy incoming index is inconsistent.");
            AddSectorCorridorPolicyReverseEdge(
                job.Policy,
                job.Policy.PortalOpenSet,
                EncodePortalNode(expansion.CurrentSectorId, transition.FromPortalId),
                expansion.CurrentNode,
                AddDeterministicPortalCosts(expansion.CurrentCost, transition.DeterministicCost));
        }
    }

    private static void AdvanceReversePolicyCrossingOneOperation(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalReversePolicyExpansion expansion,
        ref bool policyMutationStarted)
    {
        BeginHashedPortalHierarchyPolicyMutation(job.Policy, ref policyMutationStarted);
        PortalData portal = GetPortalById(_world, expansion.CurrentPortalId);
        int oppositeSectorId = GetOppositeSectorId(portal, expansion.CurrentSectorId);
        if (expansion.Hierarchy)
        {
            int clusterId = ResolveHierarchyClusterId(_world, expansion.HierarchyLevel, expansion.CurrentSectorId);
            if (ResolveHierarchyClusterId(_world, expansion.HierarchyLevel, oppositeSectorId) == clusterId)
                throw new InvalidOperationException("Navigation hierarchy reverse policy boundary crosses inside one cluster.");
            RelaxPortalHierarchyReversePolicyNode(
                source.HierarchyPolicy,
                EncodePortalNode(oppositeSectorId, expansion.CurrentPortalId),
                expansion.CurrentNode,
                AddDeterministicPortalCosts(expansion.CurrentCost, DeterministicPortalCrossingCost));
            expansion.Stage = expansion.CompleteAfterCurrentExpansion
                ? IncrementalReversePolicyExpansionStage.Complete
                : IncrementalReversePolicyExpansionStage.Pop;
        }
        else
        {
            AddSectorCorridorPolicyReverseEdge(
                job.Policy,
                job.Policy.PortalOpenSet,
                EncodePortalNode(oppositeSectorId, expansion.CurrentPortalId),
                expansion.CurrentNode,
                AddDeterministicPortalCosts(expansion.CurrentCost, DeterministicPortalCrossingCost));
            expansion.Stage = IncrementalReversePolicyExpansionStage.Edges;
        }
    }

    private static void AdvanceNavigationPathSourceMaterialization(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        ref bool policyMutationStarted)
    {
        source.Materialization ??= new IncrementalRouteMaterialization
        {
            Stage = IncrementalRouteMaterializationStage.Initialize
        };
        IncrementalRouteMaterialization state = source.Materialization;
        switch (state.Stage)
        {
            case IncrementalRouteMaterializationStage.Initialize:
                InitializeNavigationRouteMaterialization(job, source, state);
                return;
            case IncrementalRouteMaterializationStage.SelectStartPortal:
                AdvanceNavigationRouteStartPortalSelection(job, source, state);
                return;
            case IncrementalRouteMaterializationStage.ExpandDownward:
                AdvanceNavigationRouteDownwardExpansion(source, state);
                return;
            case IncrementalRouteMaterializationStage.ExpandPolicy:
                AdvanceNavigationRoutePolicyExpansion(job, source, state);
                return;
            case IncrementalRouteMaterializationStage.ExpandGoalConnector:
                AdvanceNavigationRouteGoalConnectorExpansion(source, state);
                return;
            case IncrementalRouteMaterializationStage.ConvertRoute:
                AdvanceNavigationRouteConversion(source, state);
                return;
            case IncrementalRouteMaterializationStage.PrepareImmutableRoute:
                AdvanceNavigationImmutableRoutePreparation(source, state);
                return;
            case IncrementalRouteMaterializationStage.HashRoute:
                AdvanceNavigationRouteAuthorityHash(source, state);
                return;
            case IncrementalRouteMaterializationStage.Publish:
                PublishNavigationRouteMaterialization(job, source, state, ref policyMutationStarted);
                return;
            case IncrementalRouteMaterializationStage.Complete:
                source.Stage = NavigationPathSourceStage.Complete;
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(state.Stage), state.Stage, "Unknown route materialization stage.");
        }
    }

    private static void InitializeNavigationRouteMaterialization(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state)
    {
        NavigationPathDemand demand = source.Demand;
        if (demand.StartSectorId == demand.GoalSectorId
            && AreCellsConnectedInsideSector(
                _world.Sectors[demand.StartSectorId],
                demand.StartX,
                demand.StartY,
                job.GoalX,
                job.GoalY))
        {
            source.Handle = new PathHandle
            {
                HandleId = _nextPathHandleId++,
                WorldVersion = _world.Version,
                GoalX = job.GoalX,
                GoalY = job.GoalY,
                SectorIds = new[] { demand.StartSectorId },
                PortalIds = Array.Empty<int>(),
                CurrentSectorIndex = 0,
                BuildSource = "sameSector:pathRequest"
            };
            state.Stage = IncrementalRouteMaterializationStage.Complete;
            return;
        }

        EnsureNavigationPathRequestPolicy(job);
        state.PathKey = CreateSectorPathCacheKey(
            demand.StartSectorId,
            demand.StartX,
            demand.StartY,
            demand.GoalSectorId,
            job.Policy.GoalCellIndex % _world.Width,
            job.Policy.GoalCellIndex / _world.Width);
        state.Stage = IncrementalRouteMaterializationStage.SelectStartPortal;
    }

    private static void AdvanceNavigationRouteStartPortalSelection(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state)
    {
        NavigationPathDemand demand = source.Demand;
        SectorData startSector = _world.Sectors[demand.StartSectorId];
        if (state.StartPortalCursor < startSector.PortalIds.Count)
        {
            int portalId = startSector.PortalIds[state.StartPortalCursor++];
            int node = EncodePortalNode(demand.StartSectorId, portalId);
            long accessCost = ResolveDeterministicPortalAccessCost(
                _world,
                startSector,
                demand.StartSectorId,
                portalId,
                demand.StartX,
                demand.StartY);
            long suffixCost = 0L;
            bool settled;
            if (source.HierarchyLevelArrayIndex >= 0)
            {
                PortalHierarchyDownwardCustomization leaf =
                    source.DownwardCustomizations[source.DownwardCustomizations.Count - 1];
                settled = leaf.Search.SettledNodes.Contains(node)
                          && leaf.Search.Costs.TryGetValue(node, out suffixCost);
            }
            else
            {
                settled = job.Policy.SettledPortalNodes.Contains(node)
                          && job.Policy.NodeCosts.TryGetValue(node, out suffixCost);
            }
            if (accessCost == long.MaxValue || !settled)
                return;
            long totalCost = AddDeterministicPortalCosts(accessCost, suffixCost);
            bool prefer = source.HierarchyLevelArrayIndex >= 0
                ? totalCost < state.BestStartCost
                  || (totalCost == state.BestStartCost && node < state.BestStartNode)
                : totalCost < state.BestStartCost;
            if (prefer)
            {
                state.BestStartCost = totalCost;
                state.BestStartNode = node;
            }
            return;
        }

        if (state.BestStartNode == int.MinValue)
        {
            throw new InvalidOperationException(
                $"Navigation route has no settled start portal source={demand.SourceId}, sector={demand.StartSectorId}.");
        }

        if (source.HierarchyLevelArrayIndex >= 0
            && source.HierarchyPolicy.L0WitnessesByStartNode.TryGetValue(
                state.BestStartNode,
                out PortalHierarchyL0Witness cached))
        {
            ValidatePortalHierarchyL0Witness(
                cached,
                state.BestStartNode,
                demand.StartSectorId,
                demand.GoalSectorId);
            _perf.HierarchyL0WitnessCacheHits++;
            source.Handle = CreateAndCachePathHandle(
                state.PathKey,
                cached.SectorIds,
                cached.PortalIds,
                job.GoalX,
                job.GoalY);
            source.Handle.BuildSource =
                $"portalHierarchyPolicyL{_world.Hierarchy.Levels[source.HierarchyLevelArrayIndex].Level}:witnessCache:pathRequest";
            state.Stage = IncrementalRouteMaterializationStage.Complete;
            return;
        }

        if (source.HierarchyLevelArrayIndex >= 0)
        {
            _perf.HierarchyL0WitnessCacheMisses++;
            state.DownwardCustomizationIndex = source.DownwardCustomizations.Count - 1;
            state.LastLogicalNode = state.BestStartNode;
            state.Stage = IncrementalRouteMaterializationStage.ExpandDownward;
        }
        else
        {
            state.LastLogicalNode = state.BestStartNode;
            state.Stage = IncrementalRouteMaterializationStage.ExpandPolicy;
        }
    }

    private static void AdvanceNavigationRouteDownwardExpansion(
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state)
    {
        if (state.Tasks.Count > 0)
        {
            AdvanceRouteExpansionTaskOneOperation(state);
            return;
        }
        if (state.DownwardCustomizationIndex < 0)
        {
            state.Stage = IncrementalRouteMaterializationStage.ExpandPolicy;
            return;
        }

        PortalHierarchyDownwardCustomization customization =
            source.DownwardCustomizations[state.DownwardCustomizationIndex--];
        ScheduleRouteNextTraversal(
            state,
            customization.Search.PreviousNode,
            state.LastLogicalNode,
            customization.SourceLevelArrayIndex,
            RouteExpansionTaskType.TraverseNext);
    }

    private static void AdvanceNavigationRoutePolicyExpansion(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state)
    {
        if (!state.PolicyExpansionScheduled)
        {
            Dictionary<int, int> next = source.HierarchyLevelArrayIndex >= 0
                ? source.HierarchyPolicy.NextNodeTowardGoal
                : job.Policy.NextNodeTowardGoal;
            ScheduleRouteNextTraversal(
                state,
                next,
                state.LastLogicalNode,
                Math.Max(0, source.HierarchyLevelArrayIndex),
                source.HierarchyLevelArrayIndex >= 0
                    ? RouteExpansionTaskType.TraverseHierarchyPolicy
                    : RouteExpansionTaskType.TraverseNext);
            state.PolicyExpansionScheduled = true;
            return;
        }
        if (state.Tasks.Count > 0)
        {
            AdvanceRouteExpansionTaskOneOperation(state);
            return;
        }
        state.Stage = source.HierarchyLevelArrayIndex >= 0
            ? IncrementalRouteMaterializationStage.ExpandGoalConnector
            : IncrementalRouteMaterializationStage.ConvertRoute;
    }

    private static void AdvanceNavigationRouteGoalConnectorExpansion(
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state)
    {
        if (!state.GoalConnectorScheduled)
        {
            ScheduleRouteConnectorExpansion(
                state,
                source.HierarchyPolicy.GoalConnector,
                state.LastLogicalNode);
            state.GoalConnectorScheduled = true;
            return;
        }
        if (state.Tasks.Count > 0)
        {
            AdvanceRouteExpansionTaskOneOperation(state);
            return;
        }
        state.Stage = IncrementalRouteMaterializationStage.ConvertRoute;
    }

    private static void ScheduleRouteNextTraversal(
        IncrementalRouteMaterialization state,
        Dictionary<int, int> next,
        int startNode,
        int levelIndex,
        RouteExpansionTaskType traversalType)
    {
        if (next == null)
            throw new InvalidOperationException("Navigation route traversal has no successor map.");
        if (traversalType != RouteExpansionTaskType.TraverseNext
            && traversalType != RouteExpansionTaskType.TraverseHierarchyPolicy)
        {
            throw new ArgumentOutOfRangeException(
                nameof(traversalType),
                traversalType,
                "Navigation route traversal received a non-traversal task type.");
        }
        state.Tasks.Push(new RouteExpansionTask
        {
            Type = traversalType,
            Next = next,
            CurrentNode = startNode,
            LevelIndex = levelIndex
        });
    }

    private static void ScheduleRouteConnectorExpansion(
        IncrementalRouteMaterialization state,
        PortalHierarchyConnector connector,
        int boundaryNode)
    {
        if (connector?.Search == null)
            throw new InvalidOperationException("Navigation route connector is incomplete.");
        if (connector.Child != null)
        {
            state.Tasks.Push(new RouteExpansionTask
            {
                Type = RouteExpansionTaskType.BeginConnector,
                Connector = connector.Child
            });
        }
        ScheduleRouteNextTraversal(
            state,
            connector.Search.PreviousNode,
            boundaryNode,
            connector.TargetLevelIndex,
            RouteExpansionTaskType.TraverseNext);
    }

    private static void AdvanceRouteExpansionTaskOneOperation(IncrementalRouteMaterialization state)
    {
        RouteExpansionTask task = state.Tasks.Pop();
        switch (task.Type)
        {
            case RouteExpansionTaskType.TraverseNext:
            case RouteExpansionTaskType.TraverseHierarchyPolicy:
                AdvanceRouteNextTask(state, task);
                return;
            case RouteExpansionTaskType.TraverseArray:
                AdvanceRouteArrayTask(state, task);
                return;
            case RouteExpansionTaskType.ExpandPair:
            case RouteExpansionTaskType.ExpandHierarchyPolicyPair:
                AdvanceRoutePairTask(state, task);
                return;
            case RouteExpansionTaskType.FindHierarchyEdge:
                AdvanceRouteFindEdgeTask(state, task);
                return;
            case RouteExpansionTaskType.BeginConnector:
                ScheduleRouteConnectorExpansion(state, task.Connector, state.LastLogicalNode);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(task.Type), task.Type, "Unknown route expansion task.");
        }
    }

    private static void AdvanceRouteNextTask(
        IncrementalRouteMaterialization state,
        RouteExpansionTask task)
    {
        if (!task.Started)
        {
            AppendPortalNode(state.L0Nodes, task.CurrentNode);
            state.LastLogicalNode = task.CurrentNode;
            task.Started = true;
            state.Tasks.Push(task);
            return;
        }
        if (!task.Next.TryGetValue(task.CurrentNode, out int nextNode))
            return;
        if (++task.Guard > task.Next.Count)
            throw new InvalidOperationException("Navigation route successor map contains a cycle.");
        int fromNode = task.CurrentNode;
        task.CurrentNode = nextNode;
        state.LastLogicalNode = nextNode;
        state.Tasks.Push(task);
        state.Tasks.Push(new RouteExpansionTask
        {
            Type = task.Type == RouteExpansionTaskType.TraverseHierarchyPolicy
                ? RouteExpansionTaskType.ExpandHierarchyPolicyPair
                : RouteExpansionTaskType.ExpandPair,
            FromNode = fromNode,
            ToNode = nextNode,
            LevelIndex = task.LevelIndex
        });
    }

    private static void AdvanceRouteArrayTask(
        IncrementalRouteMaterialization state,
        RouteExpansionTask task)
    {
        if (task.Nodes == null || task.Nodes.Length == 0)
            throw new InvalidOperationException("Navigation hierarchy witness is empty.");
        if (!task.Started)
        {
            AppendPortalNode(state.L0Nodes, task.Nodes[0]);
            state.LastLogicalNode = task.Nodes[0];
            task.Started = true;
            task.Cursor = 0;
            state.Tasks.Push(task);
            return;
        }
        if (task.Cursor + 1 >= task.Nodes.Length)
            return;
        int fromNode = task.Nodes[task.Cursor];
        int toNode = task.Nodes[++task.Cursor];
        state.LastLogicalNode = toNode;
        state.Tasks.Push(task);
        state.Tasks.Push(new RouteExpansionTask
        {
            Type = RouteExpansionTaskType.ExpandPair,
            FromNode = fromNode,
            ToNode = toNode,
            LevelIndex = task.LevelIndex
        });
    }

    private static void AdvanceRoutePairTask(
        IncrementalRouteMaterialization state,
        RouteExpansionTask task)
    {
        DecodePortalNode(task.FromNode, out int fromSectorId, out int fromPortalId);
        DecodePortalNode(task.ToNode, out int toSectorId, out int toPortalId);
        if (fromPortalId == toPortalId
            && GetOppositeSectorId(GetPortalById(_world, fromPortalId), fromSectorId) == toSectorId)
        {
            AppendPortalNode(state.L0Nodes, task.ToNode);
            return;
        }

        int edgeLevelIndex;
        if (task.Type == RouteExpansionTaskType.ExpandHierarchyPolicyPair)
        {
            edgeLevelIndex = task.LevelIndex;
        }
        else
        {
            if (task.LevelIndex <= 0)
            {
                AppendPortalNode(state.L0Nodes, task.ToNode);
                return;
            }
            edgeLevelIndex = task.LevelIndex - 1;
        }

        PortalHierarchyLevel edgeLevel = _world.Hierarchy.Levels[edgeLevelIndex];
        PortalHierarchyCluster cluster = edgeLevel.Clusters[
            ResolveHierarchyClusterId(_world, edgeLevel, fromSectorId)];
        if (!cluster.OutgoingEdgesByNode.TryGetValue(task.FromNode, out List<PortalHierarchyEdge> edges))
        {
            throw new InvalidOperationException(
                $"Navigation hierarchy witness source is missing cluster={cluster.ClusterId}, node={task.FromNode}.");
        }
        state.Tasks.Push(new RouteExpansionTask
        {
            Type = RouteExpansionTaskType.FindHierarchyEdge,
            CandidateEdges = edges,
            FromNode = task.FromNode,
            ToNode = task.ToNode,
            LevelIndex = edgeLevelIndex
        });
    }

    private static void AdvanceRouteFindEdgeTask(
        IncrementalRouteMaterialization state,
        RouteExpansionTask task)
    {
        if (task.Cursor >= task.CandidateEdges.Count)
        {
            throw new InvalidOperationException(
                $"Navigation hierarchy witness edge is missing from={task.FromNode}, to={task.ToNode}.");
        }
        PortalHierarchyEdge edge = task.CandidateEdges[task.Cursor++];
        if (edge.ToNode != task.ToNode)
        {
            state.Tasks.Push(task);
            return;
        }
        state.Tasks.Push(new RouteExpansionTask
        {
            Type = RouteExpansionTaskType.TraverseArray,
            Nodes = edge.ChildWitnessNodes,
            LevelIndex = task.LevelIndex
        });
    }

    private static void AdvanceNavigationRouteConversion(
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state)
    {
        NavigationPathDemand demand = source.Demand;
        if (state.SectorIds.Count == 0)
        {
            if (state.L0Nodes.Count == 0)
                throw new InvalidOperationException("Navigation route materialization produced no portal nodes.");
            state.SectorIds.Add(demand.StartSectorId);
            return;
        }
        if (state.ConvertCursor + 1 < state.L0Nodes.Count)
        {
            int fromNode = state.L0Nodes[state.ConvertCursor];
            int toNode = state.L0Nodes[++state.ConvertCursor];
            DecodePortalNode(fromNode, out int fromSectorId, out int fromPortalId);
            DecodePortalNode(toNode, out int toSectorId, out int toPortalId);
            if (fromPortalId == toPortalId && fromSectorId != toSectorId)
            {
                state.PortalIds.Add(fromPortalId);
                state.SectorIds.Add(toSectorId);
            }
            return;
        }

        if (state.PortalIds.Count == 0
            || state.SectorIds[state.SectorIds.Count - 1] != demand.GoalSectorId
            || state.PortalIds.Count + 1 != state.SectorIds.Count)
        {
            throw new InvalidOperationException(
                $"Navigation route witness cannot be converted source={demand.SourceId}, " +
                $"startSector={demand.StartSectorId}, goalSector={demand.GoalSectorId}, " +
                $"hierarchyLevelArrayIndex={source.HierarchyLevelArrayIndex}, " +
                $"bestStartNode={FormatRouteNode(state.BestStartNode)}, " +
                $"lastLogicalNode={FormatRouteNode(state.LastLogicalNode)}, " +
                $"l0Nodes=[{FormatRouteNodes(state.L0Nodes)}], " +
                $"sectorIds=[{string.Join(",", state.SectorIds)}], " +
                $"portalIds=[{string.Join(",", state.PortalIds)}].");
        }
        state.Stage = IncrementalRouteMaterializationStage.PrepareImmutableRoute;
    }

    private static string FormatRouteNodes(List<int> nodes)
    {
        if (nodes == null)
            return "null";
        var parts = new string[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
            parts[i] = $"{i}:{FormatRouteNode(nodes[i])}";
        return string.Join(">", parts);
    }

    private static string FormatRouteNode(int node)
    {
        if (node == int.MinValue)
            return "unset";
        DecodePortalNode(node, out int sectorId, out int portalId);
        return $"{sectorId}/{portalId}";
    }

    private static void AdvanceNavigationImmutableRoutePreparation(
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state)
    {
        if (state.ImmutableSectorIds == null)
        {
            state.ImmutableSectorIds = new int[state.SectorIds.Count];
            state.ImmutablePortalIds = new int[state.PortalIds.Count];
            state.ImmutableCopyCursor = 0;
            return;
        }
        if (state.ImmutableCopyCursor < state.ImmutableSectorIds.Length)
        {
            int index = state.ImmutableCopyCursor++;
            state.ImmutableSectorIds[index] = state.SectorIds[index];
            return;
        }
        int portalIndex = state.ImmutableCopyCursor - state.ImmutableSectorIds.Length;
        if (portalIndex < state.ImmutablePortalIds.Length)
        {
            state.ImmutablePortalIds[portalIndex] = state.PortalIds[portalIndex];
            state.ImmutableCopyCursor++;
            return;
        }

        state.PathAuthorityHasher = new LogicStateHasher();
        state.PathAuthorityHasher.Add(0x4E41565041544843UL);
        AddSectorPathKey(state.PathAuthorityHasher, state.PathKey);
        state.PathAuthorityHasher.Add(state.ImmutableSectorIds.Length);
        if (source.HierarchyLevelArrayIndex >= 0)
        {
            state.WitnessAuthorityHasher = new LogicStateHasher();
            state.WitnessAuthorityHasher.Add(0x50484C305749544EUL);
            state.WitnessAuthorityHasher.Add(state.BestStartNode);
            state.WitnessAuthorityHasher.Add(state.ImmutableSectorIds.Length);
        }
        state.AuthorityHashCursor = 0;
        state.HashingPortalIds = false;
        state.Stage = IncrementalRouteMaterializationStage.HashRoute;
    }

    private static void AdvanceNavigationRouteAuthorityHash(
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state)
    {
        if (!state.HashingPortalIds)
        {
            if (state.AuthorityHashCursor < state.ImmutableSectorIds.Length)
            {
                int value = state.ImmutableSectorIds[state.AuthorityHashCursor++];
                state.PathAuthorityHasher.Add(value);
                state.WitnessAuthorityHasher?.Add(value);
                return;
            }
            state.PathAuthorityHasher.Add(state.ImmutablePortalIds.Length);
            state.WitnessAuthorityHasher?.Add(state.ImmutablePortalIds.Length);
            state.AuthorityHashCursor = 0;
            state.HashingPortalIds = true;
            return;
        }
        if (state.AuthorityHashCursor < state.ImmutablePortalIds.Length)
        {
            int value = state.ImmutablePortalIds[state.AuthorityHashCursor++];
            state.PathAuthorityHasher.Add(value);
            state.WitnessAuthorityHasher?.Add(value);
            return;
        }
        state.PathAuthorityHash = state.PathAuthorityHasher.Hash;
        state.WitnessAuthorityHash = source.HierarchyLevelArrayIndex >= 0
            ? state.WitnessAuthorityHasher.Hash
            : 0UL;
        state.Stage = IncrementalRouteMaterializationStage.Publish;
    }

    private static void PublishNavigationRouteMaterialization(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state,
        ref bool policyMutationStarted)
    {
        int[] sectorIds = state.ImmutableSectorIds
            ?? throw new InvalidOperationException("Navigation route immutable sector array is missing.");
        int[] portalIds = state.ImmutablePortalIds
            ?? throw new InvalidOperationException("Navigation route immutable portal array is missing.");
        if (source.HierarchyLevelArrayIndex >= 0)
        {
            var witness = new PortalHierarchyL0Witness
            {
                StartNode = state.BestStartNode,
                SectorIds = sectorIds,
                PortalIds = portalIds
            };
            ValidatePortalHierarchyL0Witness(
                witness,
                state.BestStartNode,
                source.Demand.StartSectorId,
                source.Demand.GoalSectorId);
            witness.AuthorityContentHash = state.WitnessAuthorityHash;
            BeginHashedPortalHierarchyPolicyMutation(job.Policy, ref policyMutationStarted);
            source.HierarchyPolicy.L0WitnessesByStartNode.Add(state.BestStartNode, witness);
            source.HierarchyPolicy.L0WitnessesAuthorityContentHash ^=
                ComputePortalHierarchyL0WitnessEntryAuthorityToken(
                    state.BestStartNode,
                    witness.AuthorityContentHash);
        }

        SetSectorPathCacheEntryWithAuthorityHash(
            state.PathKey,
            new SectorPathCacheEntry
            {
                SectorIds = sectorIds,
                PortalIds = portalIds,
                LastUsedFrame = GetFrameCount()
            },
            state.PathAuthorityHash);
        TrimSectorPathCache();
        source.Handle = new PathHandle
        {
            HandleId = _nextPathHandleId++,
            WorldVersion = _world.Version,
            GoalX = job.GoalX,
            GoalY = job.GoalY,
            SectorIds = sectorIds,
            PortalIds = portalIds,
            CurrentSectorIndex = 0
        };
        source.Handle.BuildSource = source.HierarchyLevelArrayIndex >= 0
            ? $"portalHierarchyPolicyL{_world.Hierarchy.Levels[source.HierarchyLevelArrayIndex].Level}:pathRequest"
            : "sectorCorridorPolicy:pathRequest";
        state.Stage = IncrementalRouteMaterializationStage.Complete;
    }

    private static void CommitNavigationPathRequest(NavigationPathRequestJob job)
    {
        int frame = GetFrameCount();
        _perf.NavigationPathRequestCommits++;
        _perf.NavigationPathSourceCommits = checked(
            _perf.NavigationPathSourceCommits + job.Sources.Count);
        for (int i = 0; i < job.Sources.Count; i++)
        {
            NavigationPathSourceJob source = job.Sources[i];
            NavigationPathDemand demand = source.Demand;
            NavigationPathDemand latestDemand = source.LatestDemand
                                                  ?? throw new InvalidOperationException(
                                                      "Navigation path request source has no latest demand snapshot.");
            if (source.Stage != NavigationPathSourceStage.Complete || source.Handle == null)
                throw new InvalidOperationException("Navigation path request attempted a partial commit.");
            if (source.LastRequestedFrame != frame)
                throw new InvalidOperationException("Navigation path request attempted to commit a stale source.");
            RequireMatchingNavigationPathDemandIdentity(demand, latestDemand);

            AgentNavState nav = demand.Agent.NavState;
            nav.PathHandle = source.Handle;
            nav.PathHandle.GoalX = job.GoalX;
            nav.PathHandle.GoalY = job.GoalY;
            nav.HasPendingNavigation = false;
            nav.HasPendingNavigationReplacement = latestDemand.GoalSectorId != job.Key.GoalSectorId;
            nav.CommittedMovingTargetId = demand.MovingTargetId;
            nav.HasFailedPathRequest = false;
            nav.LastGoalWorldFixed = job.StableGoal;
            nav.LastGoalWorld = ToWorldVector3(job.StableGoal);
            nav.PreparedNavigationGoalFixed = job.StableGoal;
            if (!TryAdvancePathToCurrentSector(latestDemand.Agent, latestDemand.StartSectorId))
            {
                throw new InvalidOperationException(
                    $"Committed navigation path does not contain latest source sector source={demand.SourceId}, " +
                    $"snapshotSector={demand.StartSectorId}, latestSector={latestDemand.StartSectorId}.");
            }
            if (!nav.HasPendingNavigationReplacement)
            {
                nav.PathHandle.GoalX = latestDemand.GoalX;
                nav.PathHandle.GoalY = latestDemand.GoalY;
                nav.LastGoalWorldFixed = latestDemand.StableGoal;
                nav.LastGoalWorld = ToWorldVector3(latestDemand.StableGoal);
                nav.PreparedNavigationGoalFixed = latestDemand.StableGoal;
            }
            UpdateFixedPortalParticipation(demand.Agent);
            EnqueueSteeringReadDomainFlowTileBuilds(demand.Agent, latestDemand.MaximumTravelDistance);
        }
    }

    private static bool TryReuseNavigationPathDemand(NavigationPathDemand demand)
    {
        PathHandle handle = demand.Agent.NavState.PathHandle;
        bool reusable = handle != null
                        && handle.WorldVersion == _world.Version
                        && demand.Agent.NavState.CommittedMovingTargetId == demand.MovingTargetId
                        && handle.SectorIds != null
                        && handle.SectorIds.Length > 0
                        && !PathReferencesMissingPortal(handle)
                        && FindSectorIndex(handle, demand.StartSectorId, 0) >= 0
                        && handle.SectorIds[handle.SectorIds.Length - 1] == demand.GoalSectorId;
        if (!reusable)
            return false;
        handle.GoalX = demand.GoalX;
        handle.GoalY = demand.GoalY;
        if (handle.HasCommittedCurrentTileKey)
        {
            handle.CommittedCorridorGoalX = demand.GoalX;
            handle.CommittedCorridorGoalY = demand.GoalY;
        }
        demand.Agent.NavState.HasPendingNavigation = false;
        demand.Agent.NavState.HasPendingNavigationReplacement = false;
        demand.Agent.NavState.CommittedMovingTargetId = demand.MovingTargetId;
        demand.Agent.NavState.HasPreparedNavigationSnapshot = true;
        demand.Agent.NavState.PreparedNavigationFrame = GetFrameCount();
        demand.Agent.NavState.PreparedInputGoalFixed = demand.InputGoal;
        demand.Agent.NavState.PreparedNavigationGoalFixed = demand.StableGoal;
        demand.Agent.NavState.PreparedMaximumTravelDistanceFixed = demand.MaximumTravelDistance;
        if (!TryAdvancePathToCurrentSector(demand.Agent, demand.StartSectorId))
            throw new InvalidOperationException("Reusable navigation path could not advance to its resolved source sector.");
        UpdateFixedPortalParticipation(demand.Agent);
        EnqueueSteeringReadDomainFlowTileBuilds(demand.Agent, demand.MaximumTravelDistance);
        return true;
    }

    private static void ClearNavigationPathRequests()
    {
        for (LinkedListNode<NavigationPathRequestJob> node = NavigationPathRequestQueue.First;
             node != null;
             node = node.Next)
        {
            NavigationPathRequestJob job = node.Value
                ?? throw new InvalidOperationException("Navigation path request queue contains a null job.");
            for (int i = 0; i < job.Sources.Count; i++)
            {
                NavigationPathSourceJob source = job.Sources[i]
                    ?? throw new InvalidOperationException("Navigation path request contains a null source job.");
                if (source.Demand?.Agent == null)
                    throw new InvalidOperationException("Navigation path request contains an invalid source demand.");
                source.Demand.Agent.NavState.HasPendingNavigation = false;
                source.Demand.Agent.NavState.HasPendingNavigationReplacement = false;
            }
        }
        NavigationPathRequestQueue.Clear();
        PendingNavigationPathRequests.Clear();
    }

#if UNITY_EDITOR
    public static int GetEditorTestPendingNavigationPathRequestCount()
    {
        return PendingNavigationPathRequests.Count;
    }

    public static int GetEditorTestPendingNavigationPathSourceCount()
    {
        int count = 0;
        foreach (NavigationPathRequestJob job in PendingNavigationPathRequests.Values)
            count += job?.Sources.Count ?? 0;
        return count;
    }

    public static string GetEditorTestPendingNavigationPathRequestDiagnostics()
    {
        var builder = new System.Text.StringBuilder(1024);
        builder.Append('[');
        bool wroteJob = false;
        for (LinkedListNode<NavigationPathRequestJob> node = NavigationPathRequestQueue.First;
             node != null;
             node = node.Next)
        {
            NavigationPathRequestJob job = node.Value
                ?? throw new InvalidOperationException("Navigation path request diagnostics found a null job.");
            if (wroteJob)
                builder.Append(';');
            wroteJob = true;
            builder.Append("{world=").Append(job.Key.WorldVersion)
                .Append(",agentType=").Append(job.Key.AgentTypeId)
                .Append(",target=").Append(job.Key.MovingTargetId)
                .Append(",island=").Append(job.Key.SourceIslandId)
                .Append(",goalSector=").Append(job.Key.GoalSectorId)
                .Append(",cursor=").Append(job.SourceCursor)
                .Append(",sources=[");
            for (int i = 0; i < job.Sources.Count; i++)
            {
                NavigationPathSourceJob source = job.Sources[i]
                    ?? throw new InvalidOperationException("Navigation path request diagnostics found a null source.");
                NavigationPathDemand demand = source.Demand
                    ?? throw new InvalidOperationException("Navigation path request diagnostics found an invalid demand.");
                if (i > 0)
                    builder.Append(',');
                bool registered = Agents.TryGetValue(demand.SourceId, out AgentRuntimeData registeredAgent);
                AgentNavState nav = demand.Agent?.NavState;
                builder.Append("{id=").Append(demand.SourceId)
                    .Append(",last=").Append(source.LastRequestedFrame)
                    .Append(",stage=").Append(source.Stage)
                    .Append(",registered=").Append(registered)
                    .Append(",sameAgent=").Append(registered && ReferenceEquals(registeredAgent, demand.Agent))
                    .Append(",pending=").Append(nav?.HasPendingNavigation.ToString() ?? "null")
                    .Append(",handle=").Append(nav?.PathHandle?.HandleId.ToString() ?? "null")
                    .Append('}');
            }
            builder.Append("]}");
        }
        return builder.Append(']').ToString();
    }

    public static int GetEditorTestNavigationPathRequestOperationQuota()
    {
        return Config.PathRequestOperationQuota;
    }

    public static bool GetEditorTestAgentHasPendingNavigation(int sourceId)
    {
        return Agents.TryGetValue(sourceId, out AgentRuntimeData agent)
               && agent.NavState.HasPendingNavigation;
    }

    public static bool GetEditorTestAgentHasPendingNavigationReplacement(int sourceId)
    {
        return Agents.TryGetValue(sourceId, out AgentRuntimeData agent)
               && agent.NavState.HasPendingNavigationReplacement;
    }

    public static bool TryGetEditorTestPendingNavigationPathGoalSectors(
        int sourceId,
        out int snapshotGoalSectorId,
        out int latestGoalSectorId)
    {
        snapshotGoalSectorId = -1;
        latestGoalSectorId = -1;
        for (LinkedListNode<NavigationPathRequestJob> node = NavigationPathRequestQueue.First;
             node != null;
             node = node.Next)
        {
            NavigationPathRequestJob job = node.Value
                ?? throw new InvalidOperationException("Navigation path request test query found a null job.");
            for (int i = 0; i < job.Sources.Count; i++)
            {
                NavigationPathSourceJob source = job.Sources[i]
                    ?? throw new InvalidOperationException("Navigation path request test query found a null source.");
                if (source.Demand?.SourceId != sourceId)
                    continue;
                snapshotGoalSectorId = job.Key.GoalSectorId;
                latestGoalSectorId = source.LatestDemand?.GoalSectorId
                                     ?? throw new InvalidOperationException(
                                         "Navigation path request test query found no latest demand.");
                return true;
            }
        }
        return false;
    }

    public static int GetEditorTestFrameNavigationPathRequestGroupCount()
    {
        return _perf.NavigationPathRequestGroups;
    }

    public static int GetEditorTestFrameNavigationPathRequestOperationCount()
    {
        return _perf.NavigationPathRequestOperations;
    }

    public static int GetEditorTestFrameNavigationPathRequestCommitCount()
    {
        return _perf.NavigationPathRequestCommits;
    }

    public static int GetEditorTestFrameNavigationPathSourceCommitCount()
    {
        return _perf.NavigationPathSourceCommits;
    }
#endif
}
