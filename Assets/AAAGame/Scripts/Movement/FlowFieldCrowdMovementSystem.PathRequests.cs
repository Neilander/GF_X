using System;
using System.Collections.Generic;
using System.Diagnostics;
using AAAGame.FlowPath;
using UnityEngine;
using UnityGameFramework.Runtime;

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
        public int FinalGoalX;
        public int FinalGoalY;
        public FixVector2 InputGoal;
        public FixVector2 StableGoal;
        public FixVector2 FinalGoal;
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
        public FlowPathKernelSearchState KernelState;
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
        public bool MaterializeResultOnComplete = true;
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
        ExpandHierarchyPolicyPair = 6,
        TraverseL0Policy = 7,
        TraverseDownwardPolicy = 8,
        TraverseConnectorPolicy = 9
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
        public readonly FlowPathKernelRouteState RouteState = new FlowPathKernelRouteState(16, 32);
        public readonly List<FlowPathKernelSearchState> SearchAuthorities =
            new List<FlowPathKernelSearchState>(8);
        public readonly List<PortalHierarchyConnector> ConnectorAuthorities =
            new List<PortalHierarchyConnector>(4);
        public int[] ImmutableSectorIds;
        public int[] ImmutablePortalIds;
        public ImmutableRouteSequence RouteSectorIds;
        public ImmutableRouteSequence RoutePortalIds;
        public NavigationSharedRouteSuffix MergedSuffix;
        public LogicStateHasher WitnessAuthorityHasher;
        public int AuthorityHashCursor;
        public bool HashingPortalIds;
        public int SharedSuffixRegistrationCursor;
        public int SharedSuffixSectorPathIndex;
        public ulong PathAuthorityHash;
        public ulong WitnessAuthorityHash;
    }

    private sealed class NavigationSharedRouteSuffix
    {
        public NavigationSharedRouteSuffix(
            int mergeNode,
            ImmutableRouteSequence sectorIds,
            int sectorStartIndex,
            ImmutableRouteSequence portalIds,
            int portalStartIndex)
        {
            MergeNode = mergeNode;
            SectorIds = sectorIds ?? throw new ArgumentNullException(nameof(sectorIds));
            SectorStartIndex = sectorStartIndex;
            PortalIds = portalIds ?? throw new ArgumentNullException(nameof(portalIds));
            PortalStartIndex = portalStartIndex;
        }

        public int MergeNode { get; }
        public ImmutableRouteSequence SectorIds { get; }
        public int SectorStartIndex { get; }
        public ImmutableRouteSequence PortalIds { get; }
        public int PortalStartIndex { get; }
    }

    private sealed class NavigationPathRequestJob
    {
        public NavigationPathRequestIdentityKey IdentityKey;
        public NavigationPathRequestKey Key;
        public SectorCorridorPolicyKey PolicyKey;
        public SectorCorridorPolicy Policy;
        public readonly List<NavigationPathSourceJob> Sources = new List<NavigationPathSourceJob>(8);
        public readonly Dictionary<int, NavigationSharedRouteSuffix> SharedRouteSuffixes =
            new Dictionary<int, NavigationSharedRouteSuffix>(32);
        public ulong SharedRouteSuffixesAuthorityContentHash;
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
#if UNITY_EDITOR
    private const int EditorNavigationPathTickDiagnosticCapacity = 512;
    private static readonly Queue<FlowPerfAccumulator> EditorNavigationPathTickDiagnostics =
        new Queue<FlowPerfAccumulator>(EditorNavigationPathTickDiagnosticCapacity);
#endif

    private static void PrepareNavigationPathRuntimeContainerCode()
    {
        PrepareLocalDictionaryCode(
            new NavigationPathRequestIdentityKey(1, 2, 3, 4, 5),
            default(NavigationPathRequestJob));
        PrepareLocalDictionaryCode(1, default(NavigationSharedRouteSuffix));
        PrepareLocalDictionaryCode(1, 2L);
        PrepareLocalDictionaryCode(1, 2);
        PrepareLocalDictionaryCode(
            new PortalHierarchyCustomizationKey(1, 2, 3),
            default(PortalHierarchyDownwardCustomization));
        PrepareLocalDictionaryCode(
            new SectorCorridorPolicyKey(1, 2, 3, 4, 5, 6, 7),
            default(SectorCorridorPolicy));
        PrepareLocalDictionaryCode(
            new SectorPathCacheKey(1, 2, 3, 4, 5, 6, 7),
            default(SectorPathCacheEntry));

        PrepareLocalSetCode(1);
        PrepareLocalListCode<int>();
        PrepareLocalListCode<long>();
        PrepareLocalListCode<NavigationPathSourceJob>();
        PrepareLocalListCode<PortalHierarchyDownwardCustomization>();

        var requestQueue = new LinkedList<NavigationPathRequestJob>();
        LinkedListNode<NavigationPathRequestJob> requestNode = requestQueue.AddLast(
            default(NavigationPathRequestJob));
        if (requestQueue.First != requestNode)
            throw new InvalidOperationException("Navigation path runtime linked-list preparation failed.");
        requestQueue.Remove(requestNode);
        requestQueue.Clear();

    }

    private static void PrepareLocalDictionaryCode<TKey, TValue>(TKey key, TValue value)
    {
        var values = new Dictionary<TKey, TValue>(1);
        values.Add(key, value);
        if (!values.TryGetValue(key, out TValue found)
            || !EqualityComparer<TValue>.Default.Equals(found, value)
            || !values.Remove(key)
            || values.Count != 0)
        {
            throw new InvalidOperationException(
                $"Navigation path runtime dictionary preparation failed for {typeof(TKey).FullName}.");
        }
        values.Clear();
    }

    private static void PrepareLocalSetCode<T>(T value)
    {
        var values = new HashSet<T>();
        if (!values.Add(value) || !values.Contains(value) || !values.Remove(value) || values.Count != 0)
        {
            throw new InvalidOperationException(
                $"Navigation path runtime set preparation failed for {typeof(T).FullName}.");
        }
        values.Clear();
    }

    private static void PrepareLocalListCode<T>()
    {
        var values = new List<T>(1);
        int insertionIndex = values.BinarySearch(default, Comparer<T>.Default);
        if (insertionIndex != -1)
        {
            throw new InvalidOperationException(
                $"Navigation path runtime list preparation failed for {typeof(T).FullName}.");
        }
        values.Insert(~insertionIndex, default);
        values.RemoveAt(0);
        values.Clear();
    }

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
                out int finalGoalX,
                out int finalGoalY,
                out FixVector2 finalGoal,
                out bool useSectorCorridorPolicy))
        {
            failureReason = $"goal reachability failed source={request.Source.CharacterKey} {BuildGoalResolutionFailure(request.Source, ToWorldVector3(request.InputGoalPosition))}";
            return false;
        }
        if (!_world.TryGetSectorId(startX, startY, out int startSectorId))
        {
            failureReason = $"start sector failed source={request.Source.CharacterKey} start=({startX},{startY})";
            return false;
        }
        if (!_world.TryGetSectorId(finalGoalX, finalGoalY, out _))
        {
            failureReason = $"final goal sector failed source={request.Source.CharacterKey} goal=({finalGoalX},{finalGoalY})";
            return false;
        }

        if (request.MovingTargetId != int.MinValue)
        {
            if (!useSectorCorridorPolicy
                || agent.NavState.StableGoalTargetId != request.MovingTargetId)
            {
                throw new InvalidOperationException(
                    $"Moving-target navigation demand has no matching shared route anchor source={request.SourceId}, target={request.MovingTargetId}.");
            }
        }
        else if (useSectorCorridorPolicy)
        {
            throw new InvalidOperationException(
                $"Static navigation demand unexpectedly resolved a moving-target route anchor source={request.SourceId}.");
        }
        if (!_world.TryGetSectorId(goalX, goalY, out int goalSectorId))
        {
            failureReason = $"route goal sector failed source={request.Source.CharacterKey} goal=({goalX},{goalY})";
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
            FinalGoalX = finalGoalX,
            FinalGoalY = finalGoalY,
            InputGoal = request.InputGoalPosition,
            StableGoal = stableGoal,
            FinalGoal = finalGoal,
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
            _perf.NavigationPathRequestSourceAdds++;
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
            nav.PreparedNavigationGoalFixed = demand.FinalGoal;
        }
        nav.HasPreparedNavigationSnapshot = true;
        nav.PreparedNavigationFrame = GetFrameCount();
        nav.PreparedInputGoalFixed = demand.InputGoal;
        nav.PreparedMaximumTravelDistanceFixed = demand.MaximumTravelDistance;
    }

    private static void ResetNavigationPathSourceJobForLatestStart(NavigationPathSourceJob source)
    {
        DisposeNavigationPathSourceTransientState(source);
        source.Stage = NavigationPathSourceStage.Initialize;
        source.HierarchyLevelArrayIndex = -1;
        source.HierarchyPolicy = null;
        source.HierarchyPolicyBoundaryCursor = 0;
        source.GoalConnector = null;
        source.NextGoalConnectorLevel = 0;
        source.NextDownwardLevel = 0;
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
                        DisposeNavigationPathSourceTransientState(job.Sources[i]);
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
                DisposeNavigationPathSourceTransientState(job.Sources[i]);
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
                DisposeNavigationPathSourceTransientState(source);
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
        for (int i = 0; i < job.Sources.Count; i++)
            DisposeNavigationPathSourceTransientState(job.Sources[i]);
        if (!PendingNavigationPathRequests.Remove(job.IdentityKey))
            throw new InvalidOperationException("Navigation path request pending index is inconsistent.");
        NavigationPathRequestQueue.Remove(node);
    }

    private static bool IsSectorCorridorPolicyReferencedByPendingNavigationPathRequest(
        SectorCorridorPolicyKey key,
        SectorCorridorPolicy policy)
    {
        for (LinkedListNode<NavigationPathRequestJob> node = NavigationPathRequestQueue.First;
             node != null;
             node = node.Next)
        {
            NavigationPathRequestJob job = node.Value
                ?? throw new InvalidOperationException("Navigation path request queue contains a null job during policy ownership validation.");
            if (job.Policy == null)
                continue;
            bool matchingKey = job.PolicyKey.Equals(key);
            bool matchingPolicy = ReferenceEquals(job.Policy, policy);
            if (matchingKey != matchingPolicy)
            {
                throw new InvalidOperationException(
                    "Navigation path request policy key and authority instance ownership are inconsistent.");
            }
            if (matchingPolicy)
                return true;
        }
        return false;
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
        NavigationPathSourceStage stage = source.Stage;
        IncrementalRouteMaterializationStage materializationStage = source.Materialization?.Stage
                                                                   ?? IncrementalRouteMaterializationStage.Initialize;
        int routeTaskType = source.Materialization != null && source.Materialization.RouteState.TaskCount > 0
            ? source.Materialization.RouteState.Peek().Type
            : -1;
        bool recordTiming = MainThreadFrameProfiler.LoggingEnabled;
        long startTicks = recordTiming ? Stopwatch.GetTimestamp() : 0L;
        try
        {
            switch (stage)
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
        finally
        {
            long elapsedTicks = recordTiming ? Stopwatch.GetTimestamp() - startTicks : 0L;
            if (recordTiming)
                RecordNavigationPathRequestStage(stage, elapsedTicks);
            else
                RecordNavigationPathRequestStage(stage, 0L);
            if (elapsedTicks > _perf.NavigationPathSlowestOperationTicks)
            {
                _perf.NavigationPathSlowestOperationTicks = elapsedTicks;
                _perf.NavigationPathSlowestWorldVersion = job.Key.WorldVersion;
                _perf.NavigationPathSlowestAgentTypeId = job.Key.AgentTypeId;
                _perf.NavigationPathSlowestMovingTargetId = job.Key.MovingTargetId;
                _perf.NavigationPathSlowestSourceId = source.Demand.SourceId;
                _perf.NavigationPathSlowestSourceStage = (int)stage;
                _perf.NavigationPathSlowestMaterializationStage = stage == NavigationPathSourceStage.MaterializeRoute
                    ? (int)materializationStage
                    : -1;
                _perf.NavigationPathSlowestRouteTaskType = stage == NavigationPathSourceStage.MaterializeRoute
                    ? routeTaskType
                    : -1;
            }
        }
    }

    private static void RecordNavigationPathRequestStage(NavigationPathSourceStage stage, long elapsedTicks)
    {
        switch (stage)
        {
            case NavigationPathSourceStage.Initialize:
                _perf.NavigationPathInitializeOperations++;
                _perf.NavigationPathInitializeTicks += elapsedTicks;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathInitialize,
                    elapsedTicks);
                return;
            case NavigationPathSourceStage.BuildGoalConnector:
                _perf.NavigationPathGoalConnectorOperations++;
                _perf.NavigationPathGoalConnectorTicks += elapsedTicks;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathGoalConnector,
                    elapsedTicks);
                return;
            case NavigationPathSourceStage.CreateHierarchyPolicy:
                _perf.NavigationPathCreateHierarchyOperations++;
                _perf.NavigationPathCreateHierarchyTicks += elapsedTicks;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathCreateHierarchy,
                    elapsedTicks);
                return;
            case NavigationPathSourceStage.ExpandHierarchyPolicy:
                _perf.NavigationPathExpandHierarchyOperations++;
                _perf.NavigationPathExpandHierarchyTicks += elapsedTicks;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathExpandHierarchy,
                    elapsedTicks);
                return;
            case NavigationPathSourceStage.BuildDownwardCustomization:
                _perf.NavigationPathDownwardOperations++;
                _perf.NavigationPathDownwardTicks += elapsedTicks;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathDownward,
                    elapsedTicks);
                return;
            case NavigationPathSourceStage.ExpandL0Policy:
                _perf.NavigationPathL0Operations++;
                _perf.NavigationPathL0Ticks += elapsedTicks;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathL0,
                    elapsedTicks);
                return;
            case NavigationPathSourceStage.MaterializeRoute:
                _perf.NavigationPathMaterializeOperations++;
                _perf.NavigationPathMaterializeTicks += elapsedTicks;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathMaterialize,
                    elapsedTicks);
                return;
            case NavigationPathSourceStage.Complete:
                _perf.NavigationPathCompleteOperations++;
                _perf.NavigationPathCompleteTicks += elapsedTicks;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathComplete,
                    elapsedTicks);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown navigation path source stage.");
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
            if (FindSectorIndex(source.Handle, latestDemand.StartSectorId, 0) >= 0
                && source.Handle.SectorIds[source.Handle.SectorIds.Length - 1] == latestDemand.GoalSectorId)
                continue;

            source.Demand = latestDemand;
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
        if (demand.StartSectorId == job.Key.GoalSectorId)
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
            job.Key.GoalSectorId);
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
        _perf.NavigationPathPolicyCreates++;
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
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (containingCluster == null && lowerLevel != null)
            throw new ArgumentNullException(nameof(containingCluster), "Hierarchy-restricted search requires a containing cluster.");
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
            Stage = IncrementalRestrictedSearchStage.InitializeTargets,
            KernelState = new FlowPathKernelSearchState(Math.Max(16, checked(world.Portals.Length * 2)))
        };
        _perf.NavigationPathRestrictedSearchCreates++;
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
                    if (!IsIncrementalRestrictedSearchSectorAllowed(search, sectorId))
                        throw new InvalidOperationException("Incremental restricted search source is outside its containing cluster.");
                    long sourceCost = search.SourceCosts[index];
                    search.KernelState.AddSource(node, sourceCost);
                    return false;
                }
                if (search.KernelState.OpenCount == 0)
                    throw new InvalidOperationException("Incremental restricted search has no reachable source.");
                search.Stage = IncrementalRestrictedSearchStage.Pop;
                return false;
            }
            case IncrementalRestrictedSearchStage.Pop:
            {
                if (search.KernelState.OpenCount == 0)
                {
                    CompleteIncrementalRestrictedPortalSearch(search);
                    search.Stage = IncrementalRestrictedSearchStage.Complete;
                    return true;
                }
                FlowPathKernelPopStatus popStatus = search.KernelState.PopOne(
                    out FlowPathKernelSearchEntry item);
                if (popStatus == FlowPathKernelPopStatus.Empty)
                    throw new InvalidOperationException("Restricted search kernel returned empty with a non-empty heap.");
                if (popStatus == FlowPathKernelPopStatus.MissingCost)
                    throw new InvalidOperationException("Restricted search kernel popped a node without authoritative cost.");
                if (popStatus != FlowPathKernelPopStatus.Settled)
                    return false;
                if (search.TargetNodes.Contains(item.Node))
                    search.RemainingTargets--;
                if (search.RemainingTargets == 0)
                {
                    CompleteIncrementalRestrictedPortalSearch(search);
                    search.Stage = IncrementalRestrictedSearchStage.Complete;
                    return true;
                }
                DecodePortalNode(item.Node, out search.CurrentSectorId, out search.CurrentPortalId);
                if (!IsIncrementalRestrictedSearchSectorAllowed(search, search.CurrentSectorId))
                {
                    throw new InvalidOperationException("Incremental restricted search expanded outside its containing cluster.");
                }
                search.CurrentNode = item.Node;
                search.CurrentCost = item.Cost;
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
                    if (!edgeIndex.TryGetValue(item.Node, out search.CurrentHierarchyEdges))
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
                if (IsIncrementalRestrictedSearchSectorAllowed(search, oppositeSectorId))
                {
                    search.KernelState.Relax(
                        search.CurrentNode,
                        EncodePortalNode(oppositeSectorId, search.CurrentPortalId),
                        AddDeterministicPortalCosts(search.CurrentCost, DeterministicPortalCrossingCost));
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
                    search.KernelState.Relax(
                        search.CurrentNode,
                        EncodePortalNode(search.CurrentSectorId, nextPortalId),
                        AddDeterministicPortalCosts(search.CurrentCost, edge.DeterministicCost));
                }
                else
                {
                    PortalHierarchyEdge edge = search.CurrentHierarchyEdges[search.EdgeCursor++];
                    int nextNode = search.Reverse ? edge.FromNode : edge.ToNode;
                    search.KernelState.Relax(
                        search.CurrentNode,
                        nextNode,
                        AddDeterministicPortalCosts(search.CurrentCost, edge.DeterministicCost));
                }
                return false;
            }
            case IncrementalRestrictedSearchStage.Complete:
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(search.Stage), search.Stage, "Unknown incremental restricted search stage.");
        }
    }

    private static bool IsIncrementalRestrictedSearchSectorAllowed(
        IncrementalRestrictedPortalSearch search,
        int sectorId)
    {
        return search.ContainingCluster == null
               || HierarchyClusterContainsSector(search.World, search.ContainingCluster, sectorId);
    }

    private static void CompleteIncrementalRestrictedPortalSearch(
        IncrementalRestrictedPortalSearch search)
    {
        FlowPathKernelSearchState kernel = search?.KernelState
            ?? throw new InvalidOperationException("Incremental restricted search kernel state is missing.");
        if (!search.MaterializeResultOnComplete)
            return;
        if (search.Result.Costs.Count != 0
            || search.Result.PreviousNode.Count != 0
            || search.Result.SettledNodes.Count != 0)
        {
            throw new InvalidOperationException("Incremental restricted search result was materialized more than once.");
        }
        kernel.CopyTo(
            (node, cost) => search.Result.Costs.Add(node, cost),
            (node, previous) => search.Result.PreviousNode.Add(node, previous),
            node => search.Result.SettledNodes.Add(node));
        search.Result.ExpansionCount = kernel.ExpansionCount;
        kernel.Dispose();
        search.KernelState = null;
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
                source.RestrictedSearch.MaterializeResultOnComplete = false;
                EndRestrictedInputCollection(source);
                return;
            }
            if (!AdvanceIncrementalRestrictedPortalSearchOneOperation(source.RestrictedSearch))
                return;
            source.GoalConnector = new PortalHierarchyConnector
            {
                TargetLevelIndex = 0,
                SearchState = source.RestrictedSearch.KernelState
            };
            source.RestrictedSearch.KernelState = null;
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
                if (!source.GoalConnector.TryGetCost(node, out long cost))
                    return;
                if (!source.GoalConnector.ContainsSettled(node))
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
            source.RestrictedSearch.MaterializeResultOnComplete = false;
            EndRestrictedInputCollection(source);
            return;
        }
        if (!AdvanceIncrementalRestrictedPortalSearchOneOperation(source.RestrictedSearch))
            return;
        source.GoalConnector = new PortalHierarchyConnector
        {
            TargetLevelIndex = source.NextGoalConnectorLevel,
            SearchState = source.RestrictedSearch.KernelState,
            Child = source.GoalConnector
        };
        source.RestrictedSearch.KernelState = null;
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
            _perf.NavigationPathHierarchyPolicyCreates++;
            source.HierarchyPolicyBoundaryCursor = 0;
            return;
        }

        if (source.HierarchyPolicyBoundaryCursor < goalCluster.BoundaryNodes.Length)
        {
            int node = goalCluster.BoundaryNodes[source.HierarchyPolicyBoundaryCursor++];
            if (!source.GoalConnector.TryGetCost(node, out long cost))
                return;
            if (!source.GoalConnector.ContainsSettled(node))
                throw new InvalidOperationException("Navigation goal connector exposed an unsettled boundary.");
            source.HierarchyPolicy.SearchState.AddSource(node, cost);
            return;
        }

        if (source.HierarchyPolicy.SearchState.OpenCount == 0)
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
            PortalHierarchyDownwardCustomization previousCustomization = source.DownwardCustomizations.Count == 0
                ? null
                : source.DownwardCustomizations[source.DownwardCustomizations.Count - 1];
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
                    ? source.HierarchyPolicy.SearchState.ContainsSettled(node)
                    : previousCustomization != null && previousCustomization.ContainsSettled(node);
                if (!settled)
                    return;
                bool hasCost = usePolicySources
                    ? source.HierarchyPolicy.SearchState.TryGetCost(node, out long cost)
                    : previousCustomization.TryGetCost(node, out cost);
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
            source.RestrictedSearch.MaterializeResultOnComplete = false;
            EndRestrictedInputCollection(source);
            return;
        }
        if (!AdvanceIncrementalRestrictedPortalSearchOneOperation(source.RestrictedSearch))
            return;
        FlowPathKernelSearchState searchState = source.RestrictedSearch.KernelState
            ?? throw new InvalidOperationException("Completed downward customization has no kernel authority.");
        source.RestrictedSearch.KernelState = null;
        source.RestrictedSearch = null;
        var created = new PortalHierarchyDownwardCustomization
        {
            SourceLevelArrayIndex = levelIndex,
            ClusterId = containingCluster.ClusterId,
            TargetRegionId = targetRegionId,
            SearchState = searchState
        };
        created.AuthorityContentHash = ComputePortalHierarchyDownwardCustomizationAuthorityContentHash(key, created);
        BeginHashedPortalHierarchyPolicyMutation(job.Policy, ref policyMutationStarted);
        source.HierarchyPolicy.DownwardCustomizations.Add(key, created);
        source.HierarchyPolicy.DownwardCustomizationsAuthorityContentHash ^=
            ComputePortalHierarchyCustomizationEntryAuthorityToken(key, created.AuthorityContentHash);
        _perf.HierarchyDownwardCustomizationExpansions = checked(
            _perf.HierarchyDownwardCustomizationExpansions + created.ExpansionCount);
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
            bool policyEmpty = job.Policy.SearchState.OpenCount == 0
                               && job.Policy.SearchState.CostCount == 0
                               && job.Policy.SearchState.PreviousCount == 0
                               && job.Policy.SearchState.SettledCount == 0;
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
            if (job.Policy.SearchState.OpenCount == 0)
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
        job.Policy.SearchState.AddSource(goalNode, goalCost);
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
                if (!source.HierarchyPolicy.SearchState.ContainsSettled(node))
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
                    if (!job.Policy.SearchState.ContainsSettled(node))
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
        FlowPathKernelSearchState searchState = expansion.Hierarchy
            ? source.HierarchyPolicy.SearchState
            : job.Policy.SearchState;
        if (searchState.OpenCount == 0)
            throw new InvalidOperationException("Navigation reverse policy exhausted before reaching every source boundary.");

        BeginHashedPortalHierarchyPolicyMutation(job.Policy, ref policyMutationStarted);
        FlowPathKernelPopStatus popStatus = searchState.PopOne(out FlowPathKernelSearchEntry item);
        switch (popStatus)
        {
            case FlowPathKernelPopStatus.CostMismatch:
            case FlowPathKernelPopStatus.AlreadySettled:
            case FlowPathKernelPopStatus.Stale:
                return;
            case FlowPathKernelPopStatus.Settled:
                break;
            case FlowPathKernelPopStatus.Empty:
                throw new InvalidOperationException("Navigation reverse policy kernel returned empty after a non-empty frontier check.");
            case FlowPathKernelPopStatus.MissingCost:
                throw new InvalidOperationException($"Navigation reverse policy kernel frontier has no cost node={item.Node}.");
            default:
                throw new ArgumentOutOfRangeException(nameof(popStatus), popStatus, "Unknown reverse policy kernel pop status.");
        }

        _perf.PathPortalGraphNodeExpansions++;
        expansion.PendingTargetNodes.Remove(item.Node);
        expansion.CompleteAfterCurrentExpansion = expansion.PendingTargetNodes.Count == 0;

        expansion.CurrentNode = item.Node;
        expansion.CurrentCost = item.Cost;
        DecodePortalNode(item.Node, out expansion.CurrentSectorId, out expansion.CurrentPortalId);
        expansion.EdgeCursor = 0;
        if (expansion.Hierarchy)
        {
            int clusterId = ResolveHierarchyClusterId(_world, expansion.HierarchyLevel, expansion.CurrentSectorId);
            PortalHierarchyCluster cluster = expansion.HierarchyLevel.Clusters[clusterId];
            if (!cluster.IncomingEdgesByNode.TryGetValue(item.Node, out expansion.CurrentHierarchyEdges))
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
        if (source.Materialization == null)
        {
            source.Materialization = new IncrementalRouteMaterialization
            {
                Stage = IncrementalRouteMaterializationStage.Initialize
            };
            _perf.NavigationPathMaterializationCreates++;
        }
        IncrementalRouteMaterialization state = source.Materialization;
        TryMergeNavigationRouteIntoSharedSuffix(job, source, state);
        IncrementalRouteMaterializationStage operationStage = state.Stage;
        RecordNavigationPathMaterializationOperation(operationStage);
        bool recordTiming = MainThreadFrameProfiler.LoggingEnabled;
        long startTicks = recordTiming ? Stopwatch.GetTimestamp() : 0L;
        try
        {
            switch (operationStage)
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
                    AdvanceNavigationRouteConversion(job, source, state);
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
                    CompleteNavigationLocalBinding(job, source);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state.Stage), state.Stage, "Unknown route materialization stage.");
            }
        }
        finally
        {
            RecordNavigationPathMaterializationTicks(
                operationStage,
                recordTiming ? Stopwatch.GetTimestamp() - startTicks : 0L);
        }
    }

    private static void RecordNavigationPathMaterializationTicks(
        IncrementalRouteMaterializationStage stage,
        long elapsedTicks)
    {
        switch (stage)
        {
            case IncrementalRouteMaterializationStage.Initialize:
                _perf.NavigationPathMaterializeInitializeTicks += elapsedTicks;
                return;
            case IncrementalRouteMaterializationStage.SelectStartPortal:
                _perf.NavigationPathMaterializeStartPortalTicks += elapsedTicks;
                return;
            case IncrementalRouteMaterializationStage.ExpandDownward:
                _perf.NavigationPathMaterializeDownwardTicks += elapsedTicks;
                return;
            case IncrementalRouteMaterializationStage.ExpandPolicy:
                _perf.NavigationPathMaterializePolicyTicks += elapsedTicks;
                return;
            case IncrementalRouteMaterializationStage.ExpandGoalConnector:
                _perf.NavigationPathMaterializeGoalConnectorTicks += elapsedTicks;
                return;
            case IncrementalRouteMaterializationStage.ConvertRoute:
                _perf.NavigationPathMaterializeConversionTicks += elapsedTicks;
                return;
            case IncrementalRouteMaterializationStage.PrepareImmutableRoute:
                _perf.NavigationPathMaterializeImmutableCopyTicks += elapsedTicks;
                return;
            case IncrementalRouteMaterializationStage.HashRoute:
                _perf.NavigationPathMaterializeHashTicks += elapsedTicks;
                return;
            case IncrementalRouteMaterializationStage.Publish:
                _perf.NavigationPathMaterializePublishTicks += elapsedTicks;
                return;
            case IncrementalRouteMaterializationStage.Complete:
                _perf.NavigationPathLocalBindingTicks += elapsedTicks;
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown route materialization stage.");
        }
    }

    private static void TryMergeNavigationRouteIntoSharedSuffix(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state)
    {
        if (state.MergedSuffix != null
            || state.RouteState.L0NodeCount == 0
            || (state.Stage != IncrementalRouteMaterializationStage.ExpandDownward
                && state.Stage != IncrementalRouteMaterializationStage.ExpandPolicy
                && state.Stage != IncrementalRouteMaterializationStage.ExpandGoalConnector))
        {
            return;
        }

        int mergeNode = state.RouteState.GetL0Node(state.RouteState.L0NodeCount - 1);
        if (!job.SharedRouteSuffixes.TryGetValue(mergeNode, out NavigationSharedRouteSuffix suffix))
            return;
        DecodePortalNode(mergeNode, out int mergeSectorId, out _);
        if (suffix == null
            || suffix.MergeNode != mergeNode
            || suffix.SectorIds == null
            || suffix.PortalIds == null
            || suffix.SectorStartIndex < 0
            || suffix.SectorStartIndex >= suffix.SectorIds.Length
            || suffix.PortalStartIndex < 0
            || suffix.PortalStartIndex > suffix.PortalIds.Length
            || suffix.SectorIds[suffix.SectorStartIndex] != mergeSectorId
            || suffix.PortalStartIndex != suffix.SectorStartIndex)
        {
            throw new InvalidOperationException(
                $"Navigation shared route suffix is invalid source={source.Demand.SourceId}, node={FormatRouteNode(mergeNode)}.");
        }

        state.MergedSuffix = suffix;
        state.RouteState.ClearTasks();
        state.Stage = IncrementalRouteMaterializationStage.ConvertRoute;
    }

    private static void RecordNavigationPathMaterializationOperation(
        IncrementalRouteMaterializationStage stage)
    {
        switch (stage)
        {
            case IncrementalRouteMaterializationStage.Initialize:
                _perf.NavigationPathMaterializeInitializeOperations++;
                return;
            case IncrementalRouteMaterializationStage.SelectStartPortal:
                _perf.NavigationPathMaterializeStartPortalOperations++;
                return;
            case IncrementalRouteMaterializationStage.ExpandDownward:
                _perf.NavigationPathMaterializeDownwardOperations++;
                return;
            case IncrementalRouteMaterializationStage.ExpandPolicy:
                _perf.NavigationPathMaterializePolicyOperations++;
                return;
            case IncrementalRouteMaterializationStage.ExpandGoalConnector:
                _perf.NavigationPathMaterializeGoalConnectorOperations++;
                return;
            case IncrementalRouteMaterializationStage.ConvertRoute:
                _perf.NavigationPathMaterializeConversionOperations++;
                return;
            case IncrementalRouteMaterializationStage.PrepareImmutableRoute:
                _perf.NavigationPathMaterializeImmutableCopyOperations++;
                return;
            case IncrementalRouteMaterializationStage.HashRoute:
                _perf.NavigationPathMaterializeHashOperations++;
                return;
            case IncrementalRouteMaterializationStage.Publish:
                _perf.NavigationPathMaterializePublishOperations++;
                return;
            case IncrementalRouteMaterializationStage.Complete:
                _perf.NavigationPathLocalBindingOperations++;
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown route materialization stage.");
        }
    }

    private static void CompleteNavigationLocalBinding(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source)
    {
        NavigationPathDemand demand = source.Demand;
        PathHandle handle = source.Handle
                            ?? throw new InvalidOperationException("Local binding requires a materialized shared-anchor route.");
        if (handle.SectorIds == null
            || handle.PortalIds == null
            || handle.SectorIds.Length != handle.PortalIds.Length + 1
            || handle.SectorIds[handle.SectorIds.Length - 1] != job.Key.GoalSectorId)
        {
            throw new InvalidOperationException(
                $"Local binding shared-anchor route is invalid source={demand.SourceId}, handle={FormatPathHandle(handle)}.");
        }

        _perf.NavigationPathFinalBindingSources++;
        if (!_world.TryGetSectorId(demand.FinalGoalX, demand.FinalGoalY, out int finalGoalSectorId))
        {
            throw new InvalidOperationException(
                $"Local binding has no final sector source={demand.SourceId}, goal=({demand.FinalGoalX},{demand.FinalGoalY}).");
        }
        if (finalGoalSectorId != job.Key.GoalSectorId)
        {
            _perf.NavigationPathFinalBindingCrossSectorSources++;
            if (_perf.NavigationPathFinalBindingFirstSampleSet == 0)
            {
                _perf.NavigationPathFinalBindingFirstSampleSet = 1;
                _perf.NavigationPathFinalBindingFirstSourceId = demand.SourceId;
                _perf.NavigationPathFinalBindingFirstAnchorSectorId = job.Key.GoalSectorId;
                _perf.NavigationPathFinalBindingFirstGoalSectorId = finalGoalSectorId;
            }
        }
        source.Stage = NavigationPathSourceStage.Complete;
    }
    private static void InitializeNavigationRouteMaterialization(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state)
    {
        NavigationPathDemand demand = source.Demand;
        if (demand.StartSectorId == job.Key.GoalSectorId
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
            job.Key.GoalSectorId,
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
                settled = leaf.ContainsSettled(node)
                          && leaf.TryGetCost(node, out suffixCost);
            }
            else
            {
                settled = job.Policy.SearchState.ContainsSettled(node)
                          && job.Policy.SearchState.TryGetCost(node, out suffixCost);
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
                job.Key.GoalSectorId);
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
            state.Stage = IncrementalRouteMaterializationStage.ExpandDownward;
        }
        else
        {
            state.Stage = IncrementalRouteMaterializationStage.ExpandPolicy;
        }
    }

    private static void AdvanceNavigationRouteDownwardExpansion(
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state)
    {
        if (state.RouteState.TaskCount > 0)
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
        ScheduleRoutePolicyTraversal(
            state,
            customization.SearchState,
            GetRouteBoundaryNode(state),
            customization.SourceLevelArrayIndex,
            RouteExpansionTaskType.TraverseDownwardPolicy);
    }

    private static void AdvanceNavigationRoutePolicyExpansion(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state)
    {
        if (!state.PolicyExpansionScheduled)
        {
            FlowPathKernelSearchState policySearch = source.HierarchyLevelArrayIndex >= 0
                ? source.HierarchyPolicy.SearchState
                : job.Policy.SearchState;
            ScheduleRoutePolicyTraversal(
                state,
                policySearch,
                GetRouteBoundaryNode(state),
                Math.Max(0, source.HierarchyLevelArrayIndex),
                source.HierarchyLevelArrayIndex >= 0
                    ? RouteExpansionTaskType.TraverseHierarchyPolicy
                    : RouteExpansionTaskType.TraverseL0Policy);
            state.PolicyExpansionScheduled = true;
            return;
        }
        if (state.RouteState.TaskCount > 0)
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
                GetRouteBoundaryNode(state));
            state.GoalConnectorScheduled = true;
            return;
        }
        if (state.RouteState.TaskCount > 0)
        {
            AdvanceRouteExpansionTaskOneOperation(state);
            return;
        }
        state.Stage = IncrementalRouteMaterializationStage.ConvertRoute;
    }

    private static void ScheduleRoutePolicyTraversal(
        IncrementalRouteMaterialization state,
        FlowPathKernelSearchState policySearch,
        int startNode,
        int levelIndex,
        RouteExpansionTaskType traversalType)
    {
        if (policySearch == null || !policySearch.IsCreated)
            throw new InvalidOperationException("Navigation route traversal has no policy search authority.");
        if (traversalType != RouteExpansionTaskType.TraverseL0Policy
            && traversalType != RouteExpansionTaskType.TraverseHierarchyPolicy
            && traversalType != RouteExpansionTaskType.TraverseDownwardPolicy
            && traversalType != RouteExpansionTaskType.TraverseConnectorPolicy)
        {
            throw new ArgumentOutOfRangeException(
                nameof(traversalType),
                traversalType,
                "Navigation route policy traversal received a non-policy task type.");
        }
        int authoritySlot = RegisterRouteSearchAuthority(state, policySearch);
        state.RouteState.Push(new FlowPathKernelRouteTask
        {
            Type = (int)traversalType,
            AuthoritySlot = authoritySlot,
            CurrentNode = startNode,
            LevelIndex = levelIndex
        });
    }

    private static void ScheduleRouteConnectorExpansion(
        IncrementalRouteMaterialization state,
        PortalHierarchyConnector connector,
        int boundaryNode)
    {
        if (connector == null)
            throw new InvalidOperationException("Navigation route connector is incomplete.");
        connector.RequireSingleAuthority();
        if (connector.SearchState == null || !connector.SearchState.IsCreated || connector.Search != null)
        {
            throw new InvalidOperationException(
                "Production navigation route connector must use a live kernel search authority.");
        }
        if (connector.Child != null)
        {
            state.RouteState.Push(new FlowPathKernelRouteTask
            {
                Type = (int)RouteExpansionTaskType.BeginConnector,
                AuthoritySlot = RegisterRouteConnectorAuthority(state, connector.Child)
            });
        }
        ScheduleRoutePolicyTraversal(
            state,
            connector.SearchState,
            boundaryNode,
            connector.TargetLevelIndex,
            RouteExpansionTaskType.TraverseConnectorPolicy);
    }

    private static void AdvanceRouteExpansionTaskOneOperation(IncrementalRouteMaterialization state)
    {
        if (state.RouteState.TaskCount == 0)
            throw new InvalidOperationException("Navigation route expansion has no pending task.");
        FlowPathKernelRouteTask task = state.RouteState.Peek();
        RouteExpansionTaskType taskType = (RouteExpansionTaskType)task.Type;
        switch (taskType)
        {
            case RouteExpansionTaskType.TraverseHierarchyPolicy:
            case RouteExpansionTaskType.TraverseL0Policy:
            case RouteExpansionTaskType.TraverseDownwardPolicy:
            case RouteExpansionTaskType.TraverseConnectorPolicy:
                AdvanceRoutePolicyTraversal(state, task, taskType);
                return;
            case RouteExpansionTaskType.TraverseArray:
                state.RouteState.AdvanceWitnessTraversal(
                    RequireRouteWitnessIndex(),
                    (int)RouteExpansionTaskType.ExpandPair);
                return;
            case RouteExpansionTaskType.ExpandPair:
            case RouteExpansionTaskType.ExpandHierarchyPolicyPair:
                AdvanceRoutePairTask(state, task, taskType);
                return;
            case RouteExpansionTaskType.BeginConnector:
                state.RouteState.Pop();
                ScheduleRouteConnectorExpansion(
                    state,
                    ResolveRouteConnectorAuthority(state, task.AuthoritySlot),
                    GetRouteBoundaryNode(state));
                return;
            case RouteExpansionTaskType.TraverseNext:
            case RouteExpansionTaskType.FindHierarchyEdge:
                throw new InvalidOperationException(
                    $"Managed navigation route task type is forbidden in production: {taskType}.");
            default:
                throw new ArgumentOutOfRangeException(nameof(task.Type), task.Type, "Unknown route expansion task.");
        }
    }

    private static void AdvanceRoutePolicyTraversal(
        IncrementalRouteMaterialization state,
        FlowPathKernelRouteTask task,
        RouteExpansionTaskType taskType)
    {
        FlowPathKernelSearchState search = ResolveRouteSearchAuthority(state, task.AuthoritySlot);
        state.RouteState.AdvanceTraversal(
            search,
            taskType == RouteExpansionTaskType.TraverseHierarchyPolicy
                ? (int)RouteExpansionTaskType.ExpandHierarchyPolicyPair
                : (int)RouteExpansionTaskType.ExpandPair,
            search.PreviousCount);
    }

    private static void AdvanceRoutePairTask(
        IncrementalRouteMaterialization state,
        FlowPathKernelRouteTask task,
        RouteExpansionTaskType taskType)
    {
        DecodePortalNode(task.FromNode, out int fromSectorId, out int fromPortalId);
        DecodePortalNode(task.ToNode, out int toSectorId, out int toPortalId);
        bool directPortalCrossing = fromPortalId == toPortalId
                                    && GetOppositeSectorId(
                                        GetPortalById(_world, fromPortalId),
                                        fromSectorId) == toSectorId;

        int edgeLevelIndex;
        if (taskType == RouteExpansionTaskType.ExpandHierarchyPolicyPair)
        {
            edgeLevelIndex = task.LevelIndex;
        }
        else
        {
            edgeLevelIndex = task.LevelIndex <= 0 ? -1 : task.LevelIndex - 1;
        }

        int clusterId = -1;
        if (!directPortalCrossing && edgeLevelIndex >= 0)
        {
            PortalHierarchyLevel edgeLevel = _world.Hierarchy.Levels[edgeLevelIndex];
            clusterId = ResolveHierarchyClusterId(_world, edgeLevel, fromSectorId);
        }
        state.RouteState.AdvancePair(
            RequireRouteWitnessIndex(),
            directPortalCrossing,
            edgeLevelIndex,
            clusterId,
            (int)RouteExpansionTaskType.TraverseArray);
    }

    private static int RegisterRouteSearchAuthority(
        IncrementalRouteMaterialization state,
        FlowPathKernelSearchState search)
    {
        if (search == null || !search.IsCreated)
            throw new InvalidOperationException("Navigation route search authority is unavailable.");
        for (int i = 0; i < state.SearchAuthorities.Count; i++)
        {
            if (ReferenceEquals(state.SearchAuthorities[i], search))
                return i;
        }
        state.SearchAuthorities.Add(search);
        return state.SearchAuthorities.Count - 1;
    }

    private static FlowPathKernelSearchState ResolveRouteSearchAuthority(
        IncrementalRouteMaterialization state,
        int slot)
    {
        if ((uint)slot >= (uint)state.SearchAuthorities.Count)
            throw new InvalidOperationException($"Navigation route search authority slot is invalid: {slot}.");
        FlowPathKernelSearchState search = state.SearchAuthorities[slot];
        if (search == null || !search.IsCreated)
            throw new InvalidOperationException($"Navigation route search authority slot is disposed: {slot}.");
        return search;
    }

    private static int RegisterRouteConnectorAuthority(
        IncrementalRouteMaterialization state,
        PortalHierarchyConnector connector)
    {
        if (connector == null)
            throw new InvalidOperationException("Navigation route connector authority is unavailable.");
        for (int i = 0; i < state.ConnectorAuthorities.Count; i++)
        {
            if (ReferenceEquals(state.ConnectorAuthorities[i], connector))
                return i;
        }
        state.ConnectorAuthorities.Add(connector);
        return state.ConnectorAuthorities.Count - 1;
    }

    private static PortalHierarchyConnector ResolveRouteConnectorAuthority(
        IncrementalRouteMaterialization state,
        int slot)
    {
        if ((uint)slot >= (uint)state.ConnectorAuthorities.Count)
            throw new InvalidOperationException($"Navigation route connector authority slot is invalid: {slot}.");
        return state.ConnectorAuthorities[slot]
               ?? throw new InvalidOperationException(
                   $"Navigation route connector authority slot is null: {slot}.");
    }

    private static int GetRouteBoundaryNode(IncrementalRouteMaterialization state)
    {
        int node = state.RouteState.LastLogicalNode;
        return node != int.MinValue ? node : state.BestStartNode;
    }

    private static FlowPathKernelWitnessIndex RequireRouteWitnessIndex()
    {
        FlowPathKernelWitnessIndex witnessIndex = _world?.Hierarchy?.WitnessIndex;
        if (witnessIndex == null || !witnessIndex.IsCreated)
            throw new InvalidOperationException("Navigation route witness index is unavailable.");
        return witnessIndex;
    }

    private static void AdvanceNavigationRouteConversion(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state)
    {
        NavigationPathDemand demand = source.Demand;
        FlowPathKernelRouteConversionStatus conversionStatus =
            state.RouteState.AdvanceConversion(demand.StartSectorId);
        if (conversionStatus != FlowPathKernelRouteConversionStatus.Complete)
            return;

        int expectedTerminalSectorId = job.Key.GoalSectorId;
        if (state.MergedSuffix != null)
            DecodePortalNode(state.MergedSuffix.MergeNode, out expectedTerminalSectorId, out _);
        bool emptyMergedPrefix = state.MergedSuffix != null
                                 && state.RouteState.ConvertedPortalCount == 0
                                 && state.RouteState.ConvertedSectorCount == 1;
        if ((!emptyMergedPrefix && state.RouteState.ConvertedPortalCount == 0)
            || state.RouteState.GetConvertedSector(state.RouteState.ConvertedSectorCount - 1)
            != expectedTerminalSectorId
            || state.RouteState.ConvertedPortalCount + 1 != state.RouteState.ConvertedSectorCount)
        {
            throw new InvalidOperationException(
                $"Navigation route witness cannot be converted source={demand.SourceId}, " +
                $"startSector={demand.StartSectorId}, goalSector={job.Key.GoalSectorId}, " +
                $"mergedSuffix={FormatRouteNode(state.MergedSuffix?.MergeNode ?? int.MinValue)}, " +
                $"hierarchyLevelArrayIndex={source.HierarchyLevelArrayIndex}, " +
                $"bestStartNode={FormatRouteNode(state.BestStartNode)}, " +
                $"lastLogicalNode={FormatRouteNode(state.RouteState.LastLogicalNode)}, " +
                $"l0Nodes=[{FormatRouteNodes(state.RouteState)}], " +
                $"sectorIds=[{FormatConvertedSectors(state.RouteState)}], " +
                $"portalIds=[{FormatConvertedPortals(state.RouteState)}].");
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

    private static string FormatRouteNodes(FlowPathKernelRouteState state)
    {
        if (state == null || !state.IsCreated)
            return "null";
        var parts = new string[state.L0NodeCount];
        for (int i = 0; i < state.L0NodeCount; i++)
            parts[i] = $"{i}:{FormatRouteNode(state.GetL0Node(i))}";
        return string.Join(">", parts);
    }

    private static string FormatConvertedSectors(FlowPathKernelRouteState state)
    {
        var values = new int[state.ConvertedSectorCount];
        for (int i = 0; i < values.Length; i++)
            values[i] = state.GetConvertedSector(i);
        return string.Join(",", values);
    }

    private static string FormatConvertedPortals(FlowPathKernelRouteState state)
    {
        var values = new int[state.ConvertedPortalCount];
        for (int i = 0; i < values.Length; i++)
            values[i] = state.GetConvertedPortal(i);
        return string.Join(",", values);
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
            int immutableSectorCount = state.MergedSuffix == null
                ? state.RouteState.ConvertedSectorCount
                : state.RouteState.ConvertedSectorCount - 1;
            if (immutableSectorCount < 0)
                throw new InvalidOperationException("Navigation merged route prefix has no terminal merge sector.");
            state.ImmutableSectorIds = state.RouteState.CopyConvertedSectors(immutableSectorCount);
            state.ImmutablePortalIds = state.RouteState.CopyConvertedPortals();
        }

        if (state.RouteSectorIds == null)
        {
            if (state.MergedSuffix == null)
            {
                state.RouteSectorIds = state.ImmutableSectorIds;
                state.RoutePortalIds = state.ImmutablePortalIds;
            }
            else
            {
                state.RouteSectorIds = ImmutableRouteSequence.Concat(
                    state.ImmutableSectorIds,
                    state.ImmutableSectorIds.Length,
                    state.MergedSuffix.SectorIds,
                    state.MergedSuffix.SectorStartIndex);
                state.RoutePortalIds = ImmutableRouteSequence.Concat(
                    state.ImmutablePortalIds,
                    state.ImmutablePortalIds.Length,
                    state.MergedSuffix.PortalIds,
                    state.MergedSuffix.PortalStartIndex);
                _perf.NavigationPathImmutableConcatCreates += 2;
            }
            if (state.RouteSectorIds.Length == 0
                || state.RoutePortalIds.Length + 1 != state.RouteSectorIds.Length)
            {
                throw new InvalidOperationException("Navigation immutable segmented route is inconsistent.");
            }
            state.PathAuthorityHash = ComputeSectorPathAuthorityContentHash(
                state.PathKey,
                new SectorPathCacheEntry
                {
                    SectorIds = state.RouteSectorIds,
                    PortalIds = state.RoutePortalIds
                });
        }
        if (source.HierarchyLevelArrayIndex >= 0 && state.MergedSuffix == null)
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
        if (source.HierarchyLevelArrayIndex < 0 || state.MergedSuffix != null)
        {
            state.WitnessAuthorityHash = 0UL;
            state.Stage = IncrementalRouteMaterializationStage.Publish;
            return;
        }
        if (!state.HashingPortalIds)
        {
            if (state.AuthorityHashCursor < state.ImmutableSectorIds.Length)
            {
                int value = state.ImmutableSectorIds[state.AuthorityHashCursor++];
                state.WitnessAuthorityHasher.Add(value);
                return;
            }
            state.WitnessAuthorityHasher.Add(state.ImmutablePortalIds.Length);
            state.AuthorityHashCursor = 0;
            state.HashingPortalIds = true;
            return;
        }
        if (state.AuthorityHashCursor < state.ImmutablePortalIds.Length)
        {
            int value = state.ImmutablePortalIds[state.AuthorityHashCursor++];
            state.WitnessAuthorityHasher.Add(value);
            return;
        }
        state.WitnessAuthorityHash = state.WitnessAuthorityHasher.Hash;
        state.Stage = IncrementalRouteMaterializationStage.Publish;
    }

    private static void PublishNavigationRouteMaterialization(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state,
        ref bool policyMutationStarted)
    {
        ImmutableRouteSequence sectorIds = state.RouteSectorIds
            ?? throw new InvalidOperationException("Navigation route immutable sector sequence is missing.");
        ImmutableRouteSequence portalIds = state.RoutePortalIds
            ?? throw new InvalidOperationException("Navigation route immutable portal sequence is missing.");
        if (source.Handle == null)
        {
            if (source.HierarchyLevelArrayIndex >= 0 && state.MergedSuffix == null)
            {
                int[] witnessSectorIds = state.ImmutableSectorIds
                    ?? throw new InvalidOperationException("Navigation hierarchy witness sector array is missing.");
                int[] witnessPortalIds = state.ImmutablePortalIds
                    ?? throw new InvalidOperationException("Navigation hierarchy witness portal array is missing.");
                var witness = new PortalHierarchyL0Witness
                {
                    StartNode = state.BestStartNode,
                    SectorIds = witnessSectorIds,
                    PortalIds = witnessPortalIds
                };
                ValidatePortalHierarchyL0Witness(
                    witness,
                    state.BestStartNode,
                    source.Demand.StartSectorId,
                    job.Key.GoalSectorId);
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
            state.SharedSuffixRegistrationCursor = 0;
            state.SharedSuffixSectorPathIndex = 0;
            return;
        }

        if (state.SharedSuffixRegistrationCursor < state.RouteState.L0NodeCount)
        {
            RegisterNavigationSharedRouteSuffixOneOperation(job, state, sectorIds, portalIds);
            return;
        }
        state.Stage = IncrementalRouteMaterializationStage.Complete;
    }

    private static void RegisterNavigationSharedRouteSuffixOneOperation(
        NavigationPathRequestJob job,
        IncrementalRouteMaterialization state,
        ImmutableRouteSequence sectorIds,
        ImmutableRouteSequence portalIds)
    {
        if (state.RouteState.L0NodeCount == 0)
            throw new InvalidOperationException("Navigation shared route registration has no L0 nodes.");
        int index = state.SharedSuffixRegistrationCursor;
        int sectorPathIndex = state.SharedSuffixSectorPathIndex;
        int node = state.RouteState.GetL0Node(index);
        if (index > 0)
        {
            DecodePortalNode(
                state.RouteState.GetL0Node(index - 1),
                out int previousSectorId,
                out int previousPortalId);
            DecodePortalNode(node, out int currentSectorId, out int currentPortalId);
            if (previousPortalId == currentPortalId && previousSectorId != currentSectorId)
                sectorPathIndex++;
        }
        DecodePortalNode(node, out int nodeSectorId, out _);
        if (sectorPathIndex < 0
            || sectorPathIndex >= sectorIds.Length
            || sectorPathIndex > portalIds.Length
            || sectorIds[sectorPathIndex] != nodeSectorId)
        {
            throw new InvalidOperationException(
                $"Navigation shared route node does not match materialized route node={FormatRouteNode(node)}, index={sectorPathIndex}.");
        }

        var suffix = new NavigationSharedRouteSuffix(
            node,
            sectorIds,
            sectorPathIndex,
            portalIds,
            sectorPathIndex);
        _perf.NavigationPathSharedSuffixCreates++;
        if (job.SharedRouteSuffixes.TryGetValue(node, out NavigationSharedRouteSuffix existing))
        {
            int existingSectorCount = existing.SectorIds.Length - existing.SectorStartIndex;
            int suffixSectorCount = sectorIds.Length - sectorPathIndex;
            int existingPortalCount = existing.PortalIds.Length - existing.PortalStartIndex;
            int suffixPortalCount = portalIds.Length - sectorPathIndex;
            if (existingSectorCount != suffixSectorCount
                || existingPortalCount != suffixPortalCount
                || existing.SectorIds.GetRangeAuthorityHash(existing.SectorStartIndex, existingSectorCount)
                != sectorIds.GetRangeAuthorityHash(sectorPathIndex, suffixSectorCount)
                || existing.PortalIds.GetRangeAuthorityHash(existing.PortalStartIndex, existingPortalCount)
                != portalIds.GetRangeAuthorityHash(sectorPathIndex, suffixPortalCount))
            {
                throw new InvalidOperationException(
                    $"Navigation shared route tree contains conflicting suffixes node={FormatRouteNode(node)}.");
            }
        }
        else
        {
            job.SharedRouteSuffixes.Add(node, suffix);
            job.SharedRouteSuffixesAuthorityContentHash ^=
                ComputeNavigationSharedRouteSuffixAuthorityToken(node, suffix);
        }
        state.SharedSuffixSectorPathIndex = sectorPathIndex;
        state.SharedSuffixRegistrationCursor++;
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
            nav.PathHandle.GoalX = latestDemand.GoalX;
            nav.PathHandle.GoalY = latestDemand.GoalY;
            nav.HasPendingNavigation = false;
            nav.HasPendingNavigationReplacement = latestDemand.GoalSectorId != job.Key.GoalSectorId;
            nav.CommittedMovingTargetId = demand.MovingTargetId;
            nav.HasFailedPathRequest = false;
            nav.LastGoalWorldFixed = latestDemand.FinalGoal;
            nav.LastGoalWorld = ToWorldVector3(latestDemand.FinalGoal);
            nav.PreparedNavigationGoalFixed = latestDemand.FinalGoal;
            if (!TryAdvancePathToCurrentSector(latestDemand.Agent, latestDemand.StartSectorId))
            {
                throw new InvalidOperationException(
                    $"Committed navigation path does not contain latest source sector source={demand.SourceId}, " +
                    $"snapshotSector={demand.StartSectorId}, latestSector={latestDemand.StartSectorId}.");
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
        demand.Agent.NavState.PreparedNavigationGoalFixed = demand.FinalGoal;
        demand.Agent.NavState.PreparedMaximumTravelDistanceFixed = demand.MaximumTravelDistance;
        demand.Agent.NavState.LastGoalWorldFixed = demand.FinalGoal;
        demand.Agent.NavState.LastGoalWorld = ToWorldVector3(demand.FinalGoal);
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
                DisposeNavigationPathSourceTransientState(source);
            }
        }
        NavigationPathRequestQueue.Clear();
        PendingNavigationPathRequests.Clear();
    }

    private static void DisposeNavigationPathSourceTransientState(NavigationPathSourceJob source)
    {
        if (source == null)
            throw new InvalidOperationException("Cannot dispose a null navigation path source.");
        if (source.RestrictedSearch?.KernelState != null)
        {
            source.RestrictedSearch.KernelState.Dispose();
            source.RestrictedSearch.KernelState = null;
        }
        source.RestrictedSearch = null;
        if (source.Materialization?.RouteState != null)
            source.Materialization.RouteState.Dispose();
        source.Materialization = null;
        DisposeUnpublishedPortalHierarchyConnectorStates(source.GoalConnector);
        if (source.HierarchyPolicy != null && !IsPublishedPortalHierarchyReversePolicy(source.HierarchyPolicy))
            source.HierarchyPolicy.Dispose();
    }

    private static bool IsPublishedPortalHierarchyReversePolicy(PortalHierarchyReversePolicy policy)
    {
        foreach (SectorCorridorPolicy sectorPolicy in SectorCorridorPolicies.Values)
        {
            if (sectorPolicy == null)
                throw new InvalidOperationException("Navigation policy cache contains a null policy during hierarchy ownership validation.");
            foreach (PortalHierarchyReversePolicy published in sectorPolicy.HierarchyPolicies.Values)
            {
                if (ReferenceEquals(published, policy))
                    return true;
            }
        }
        return false;
    }

    private static void DisposeUnpublishedPortalHierarchyConnectorStates(
        PortalHierarchyConnector connector)
    {
        if (connector == null)
            return;
        var published = new HashSet<PortalHierarchyConnector>();
        foreach (SectorCorridorPolicy sectorPolicy in SectorCorridorPolicies.Values)
        {
            if (sectorPolicy == null)
                throw new InvalidOperationException("Navigation policy cache contains a null policy during connector disposal.");
            foreach (PortalHierarchyReversePolicy hierarchyPolicy in sectorPolicy.HierarchyPolicies.Values)
            {
                for (PortalHierarchyConnector cursor = hierarchyPolicy?.GoalConnector;
                     cursor != null;
                     cursor = cursor.Child)
                {
                    published.Add(cursor);
                }
            }
        }
        for (PortalHierarchyConnector cursor = connector; cursor != null; cursor = cursor.Child)
        {
            if (!published.Contains(cursor))
                cursor.Dispose();
        }
    }

#if UNITY_EDITOR
    private static void CaptureEditorNavigationPathTickDiagnostic()
    {
        if (!MainThreadFrameProfiler.LoggingEnabled || _perf.NavigationPathRequestOperations <= 0)
            return;
        EditorNavigationPathTickDiagnostics.Enqueue(_perf);
        while (EditorNavigationPathTickDiagnostics.Count > EditorNavigationPathTickDiagnosticCapacity)
            EditorNavigationPathTickDiagnostics.Dequeue();
    }

    private static void ClearEditorNavigationPathTickDiagnostics()
    {
        EditorNavigationPathTickDiagnostics.Clear();
    }

    public static string[] DrainEditorTestNavigationPathTickDiagnostics()
    {
        var diagnostics = new string[EditorNavigationPathTickDiagnostics.Count];
        for (int i = 0; i < diagnostics.Length; i++)
        {
            FlowPerfAccumulator snapshot = EditorNavigationPathTickDiagnostics.Dequeue();
            string sourceStage = Enum.IsDefined(
                typeof(NavigationPathSourceStage),
                snapshot.NavigationPathSlowestSourceStage)
                ? ((NavigationPathSourceStage)snapshot.NavigationPathSlowestSourceStage).ToString()
                : "none";
            string materializationStage = snapshot.NavigationPathSlowestMaterializationStage >= 0
                                          && Enum.IsDefined(
                                              typeof(IncrementalRouteMaterializationStage),
                                              snapshot.NavigationPathSlowestMaterializationStage)
                ? ((IncrementalRouteMaterializationStage)snapshot.NavigationPathSlowestMaterializationStage).ToString()
                : "none";
            string routeTaskType = snapshot.NavigationPathSlowestRouteTaskType >= 0
                                   && Enum.IsDefined(
                                       typeof(RouteExpansionTaskType),
                                       snapshot.NavigationPathSlowestRouteTaskType)
                ? ((RouteExpansionTaskType)snapshot.NavigationPathSlowestRouteTaskType).ToString()
                : "none";
            diagnostics[i] =
                $"logicPathTick logic={snapshot.Frame},queue={TicksToMs(snapshot.NavigationPathRequestQueueTicks):F3}ms," +
                $"groups={snapshot.NavigationPathRequestGroups},sourceAdds={snapshot.NavigationPathRequestSourceAdds},operations={snapshot.NavigationPathRequestOperations}," +
                $"commits={snapshot.NavigationPathRequestCommits},sourceCommits={snapshot.NavigationPathSourceCommits}," +
                $"runtime=[monoUsedDelta={snapshot.NavigationPathMonoUsedBytesDelta},gc={snapshot.NavigationPathGen0Collections}/{snapshot.NavigationPathGen1Collections}/{snapshot.NavigationPathGen2Collections}]," +
                $"creates=[policy={snapshot.NavigationPathPolicyCreates},search={snapshot.NavigationPathRestrictedSearchCreates}," +
                $"hierarchy={snapshot.NavigationPathHierarchyPolicyCreates},materialization={snapshot.NavigationPathMaterializationCreates}," +
                $"concat={snapshot.NavigationPathImmutableConcatCreates},suffix={snapshot.NavigationPathSharedSuffixCreates}]," +
                $"stages=[{BuildEditorNavigationPathStageDiagnostics(snapshot)}]," +
                $"slowest=[milliseconds={TicksToMs(snapshot.NavigationPathSlowestOperationTicks):F3}," +
                $"world={snapshot.NavigationPathSlowestWorldVersion},agentType={snapshot.NavigationPathSlowestAgentTypeId}," +
                $"target={snapshot.NavigationPathSlowestMovingTargetId},source={snapshot.NavigationPathSlowestSourceId}," +
                $"stage={sourceStage},materialization={materializationStage},task={routeTaskType}]";
        }
        return diagnostics;
    }

    private static string BuildEditorNavigationPathStageDiagnostics(FlowPerfAccumulator perf)
    {
        return $"initialize={TicksToMs(perf.NavigationPathInitializeTicks):F3}ms/{perf.NavigationPathInitializeOperations}," +
               $"goalConnector={TicksToMs(perf.NavigationPathGoalConnectorTicks):F3}ms/{perf.NavigationPathGoalConnectorOperations}," +
               $"createHierarchy={TicksToMs(perf.NavigationPathCreateHierarchyTicks):F3}ms/{perf.NavigationPathCreateHierarchyOperations}," +
               $"expandHierarchy={TicksToMs(perf.NavigationPathExpandHierarchyTicks):F3}ms/{perf.NavigationPathExpandHierarchyOperations}," +
               $"downward={TicksToMs(perf.NavigationPathDownwardTicks):F3}ms/{perf.NavigationPathDownwardOperations}," +
               $"l0={TicksToMs(perf.NavigationPathL0Ticks):F3}ms/{perf.NavigationPathL0Operations}," +
               $"materialize={TicksToMs(perf.NavigationPathMaterializeTicks):F3}ms/{perf.NavigationPathMaterializeOperations}," +
               $"materializeStages=(initialize={TicksToMs(perf.NavigationPathMaterializeInitializeTicks):F3}ms/{perf.NavigationPathMaterializeInitializeOperations}," +
               $"startPortal={TicksToMs(perf.NavigationPathMaterializeStartPortalTicks):F3}ms/{perf.NavigationPathMaterializeStartPortalOperations}," +
               $"downward={TicksToMs(perf.NavigationPathMaterializeDownwardTicks):F3}ms/{perf.NavigationPathMaterializeDownwardOperations}," +
               $"policy={TicksToMs(perf.NavigationPathMaterializePolicyTicks):F3}ms/{perf.NavigationPathMaterializePolicyOperations}," +
               $"goalConnector={TicksToMs(perf.NavigationPathMaterializeGoalConnectorTicks):F3}ms/{perf.NavigationPathMaterializeGoalConnectorOperations}," +
               $"conversion={TicksToMs(perf.NavigationPathMaterializeConversionTicks):F3}ms/{perf.NavigationPathMaterializeConversionOperations}," +
               $"immutableCopy={TicksToMs(perf.NavigationPathMaterializeImmutableCopyTicks):F3}ms/{perf.NavigationPathMaterializeImmutableCopyOperations}," +
               $"hash={TicksToMs(perf.NavigationPathMaterializeHashTicks):F3}ms/{perf.NavigationPathMaterializeHashOperations}," +
               $"publish={TicksToMs(perf.NavigationPathMaterializePublishTicks):F3}ms/{perf.NavigationPathMaterializePublishOperations}," +
               $"localBinding={TicksToMs(perf.NavigationPathLocalBindingTicks):F3}ms/{perf.NavigationPathLocalBindingOperations}," +
               $"finalBindingGraph=(sources={perf.NavigationPathFinalBindingSources},crossSector={perf.NavigationPathFinalBindingCrossSectorSources}," +
               $"goalPortals={perf.NavigationPathFinalBindingGoalPortalsAccessible}/{perf.NavigationPathFinalBindingGoalPortalsScanned}," +
               $"anchorPortals={perf.NavigationPathFinalBindingAnchorPortalsAccessible}/{perf.NavigationPathFinalBindingAnchorPortalsScanned}," +
               $"settled={perf.NavigationPathFinalBindingSettledNodes},routeNodes={perf.NavigationPathFinalBindingRouteNodes}," +
               $"first={perf.NavigationPathFinalBindingFirstSourceId}:{perf.NavigationPathFinalBindingFirstAnchorSectorId}->{perf.NavigationPathFinalBindingFirstGoalSectorId}))," +
               $"complete={TicksToMs(perf.NavigationPathCompleteTicks):F3}ms/{perf.NavigationPathCompleteOperations}";
    }

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

    public static int GetEditorTestPendingNavigationSharedRouteSuffixCount()
    {
        int count = 0;
        foreach (NavigationPathRequestJob job in PendingNavigationPathRequests.Values)
        {
            if (job == null)
                throw new InvalidOperationException("Pending navigation path request index contains a null job.");
            count = checked(count + job.SharedRouteSuffixes.Count);
        }
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

    public static void GetEditorTestFrameNavigationPathMaterializationOperationCounts(
        out int initialize,
        out int startPortal,
        out int downward,
        out int policy,
        out int goalConnector,
        out int conversion,
        out int immutableCopy,
        out int hash,
        out int publish,
        out int localBinding)
    {
        initialize = _perf.NavigationPathMaterializeInitializeOperations;
        startPortal = _perf.NavigationPathMaterializeStartPortalOperations;
        downward = _perf.NavigationPathMaterializeDownwardOperations;
        policy = _perf.NavigationPathMaterializePolicyOperations;
        goalConnector = _perf.NavigationPathMaterializeGoalConnectorOperations;
        conversion = _perf.NavigationPathMaterializeConversionOperations;
        immutableCopy = _perf.NavigationPathMaterializeImmutableCopyOperations;
        hash = _perf.NavigationPathMaterializeHashOperations;
        publish = _perf.NavigationPathMaterializePublishOperations;
        localBinding = _perf.NavigationPathLocalBindingOperations;
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
