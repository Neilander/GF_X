using System;
using System.Collections.Generic;
using System.Diagnostics;
using AAAGame.FlowPath;
using UnityEngine;
using UnityGameFramework.Runtime;

public static partial class FlowFieldCrowdMovementSystem
{
    // A request is an incremental state machine.  Keeping a bounded quantum
    // per visit prevents one expensive graph slice from monopolizing the Tick;
    // the enclosing operation quota still determines total work completed.
    private const int NavigationPathRequestFairShareOperationQuota = 256;

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
        public readonly List<int> GraphTargetScratch = new List<int>();
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
        // Derived lookup for source rebinding. Sources remains the ordered
        // authority used by the state machine and digest.
        public readonly Dictionary<int, NavigationPathSourceJob> SourcesById =
            new Dictionary<int, NavigationPathSourceJob>(8);
        public readonly Dictionary<int, NavigationSharedRouteSuffix> SharedRouteSuffixes =
            new Dictionary<int, NavigationSharedRouteSuffix>(32);
        // Goal-side reverse connectors are target authority.  They are shared
        // by every source in this request at the same hierarchy depth; keeping
        // them on the request prevents each source from rebuilding the same
        // restricted search and connector chain on the main thread.
        public readonly Dictionary<int, PortalHierarchyConnector> SharedGoalConnectors =
            new Dictionary<int, PortalHierarchyConnector>(4);
        public readonly HashSet<int> SharedGoalConnectorBuildLevels = new HashSet<int>();
        public readonly Dictionary<int, int> SharedGoalConnectorBuilderSourceIds =
            new Dictionary<int, int>(4);
        public readonly FlowPathKernelRouteMergeIndex SharedRouteMergeIndex =
            new FlowPathKernelRouteMergeIndex(32);
        // Derived dispatch index.  The request queue remains the authority;
        // this index only locates the owning job for a source in O(1).
        public LinkedListNode<NavigationPathRequestJob> QueueNode;
        public ulong SharedRouteSuffixesAuthorityContentHash;
        public int SourceCursor;
        public int GoalX;
        public int GoalY;
        public FixVector2 StableGoal;
        public bool PolicyMutationStarted;
        public bool Complete;
    }

    private static readonly LinkedList<NavigationPathRequestJob> NavigationPathRequestQueue =
        new LinkedList<NavigationPathRequestJob>();
    private static readonly Dictionary<NavigationPathRequestIdentityKey, NavigationPathRequestJob>
        PendingNavigationPathRequests =
            new Dictionary<NavigationPathRequestIdentityKey, NavigationPathRequestJob>();
    private static readonly Dictionary<int, NavigationPathRequestJob>
        NavigationPathRequestBySourceId =
            new Dictionary<int, NavigationPathRequestJob>();
#if UNITY_EDITOR
    private const int EditorNavigationPathTickDiagnosticCapacity = 512;
    private static readonly Queue<FlowPerfAccumulator> EditorNavigationPathTickDiagnostics =
        new Queue<FlowPerfAccumulator>(EditorNavigationPathTickDiagnosticCapacity);
    private static readonly Queue<FlowPerfAccumulator> EditorFlowPerfTickSnapshots =
        new Queue<FlowPerfAccumulator>(EditorNavigationPathTickDiagnosticCapacity);
#endif

    private static void PrepareNavigationPathRuntimeContainerCode()
    {
        PrepareLocalDictionaryCode(
            new NavigationPathRequestIdentityKey(1, 2, 3, 4, 5),
            default(NavigationPathRequestJob));
        PrepareLocalDictionaryCode(1, default(NavigationPathRequestJob));
        PrepareLocalDictionaryCode(1, default(NavigationPathSourceJob));
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

        using (var policy = new SectorCorridorPolicy
               {
                   GoalSectorId = 5,
                   GoalCellIndex = 6,
                   GoalSectorDirtyVersion = 7,
                   LastUsedFrame = 8
               })
        {
            var key = new SectorCorridorPolicyKey(1, 2, 3, 4, 5, 6, 7);
            RefreshSectorCorridorPolicyHierarchyAuthorityContentHash(policy);
            _ = ComputeSectorCorridorPolicyAuthorityContentHash(key, policy);
            if (!policy.SearchState.ValidateAuthorityHashes())
            {
                throw new InvalidOperationException(
                    "Navigation path runtime policy preparation produced an invalid search authority hash.");
            }
        }
    }

    private static void PrepareLocalDictionaryCode<TKey, TValue>(TKey key, TValue value)
    {
        var values = new Dictionary<TKey, TValue>(1);
        values.Add(key, value);
        values[key] = value;
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

        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long agentPreparationStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
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
        if (profile)
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationDemandAgentPreparation,
                Stopwatch.GetTimestamp() - agentPreparationStartTicks);

        long phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (!TryResolveStartCellForReachabilityFixed(
                request.Source,
                out int startX,
                out int startY,
                out int sourceIslandId))
        {
            if (profile)
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowPrepareStartCell, Stopwatch.GetTimestamp() - phaseStartTicks);
            failureReason = $"start reachability failed source={request.Source.CharacterKey} {BuildReachabilityStartDiagnostics(request.Source)}";
            return false;
        }
        if (profile)
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowPrepareStartCell, Stopwatch.GetTimestamp() - phaseStartTicks);

        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        long stableGoalArgumentPreparationStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        IEntityContext stableGoalSource = request.Source;
        FixVector2 stableGoalInputGoal = request.InputGoalPosition;
        int stableGoalMovingTargetId = request.MovingTargetId;
        Fix64 stableGoalMaximumTravelDistance = request.MaximumTravelDistance;
        if (profile)
        {
            _perf.StableGoalPathRequestArgumentPreparationTicks += Stopwatch.GetTimestamp() - stableGoalArgumentPreparationStartTicks;
            _perf.StableGoalPathRequestArgumentPreparationCount++;
        }
        long stableGoalInvocationStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (!TryResolveStableGoalCellFixed(
                agent,
                stableGoalSource,
                stableGoalInputGoal,
                startX,
                startY,
                sourceIslandId,
                stableGoalMovingTargetId,
                out int goalX,
                out int goalY,
                out FixVector2 stableGoal,
                out int finalGoalX,
                out int finalGoalY,
                out FixVector2 finalGoal,
                out bool useSectorCorridorPolicy,
                out bool pendingProjection))
        {
            if (profile)
            {
                long stableGoalInvocationTicks = Stopwatch.GetTimestamp() - stableGoalInvocationStartTicks;
                _perf.StableGoalInvocationTicks += stableGoalInvocationTicks;
                _perf.StableGoalInvocationCount++;
                if (stableGoalInvocationTicks > _perf.StableGoalInvocationMaxTicks)
                    _perf.StableGoalInvocationMaxTicks = stableGoalInvocationTicks;
                _perf.StableGoalPathRequestInvocationTicks += stableGoalInvocationTicks;
                _perf.StableGoalPathRequestInvocationCount++;
                if (stableGoalInvocationTicks > _perf.StableGoalPathRequestInvocationMaxTicks)
                    _perf.StableGoalPathRequestInvocationMaxTicks = stableGoalInvocationTicks;
                long stableGoalInvocationGapTicks = stableGoalInvocationTicks - _lastStableGoalBodyTicks;
                _perf.StableGoalInvocationGapTicks += stableGoalInvocationGapTicks;
                _perf.StableGoalInvocationGapCount++;
                if (stableGoalInvocationGapTicks > _perf.StableGoalInvocationGapMaxTicks)
                    _perf.StableGoalInvocationGapMaxTicks = stableGoalInvocationGapTicks;
            }
            if (pendingProjection)
            {
                if (!_world.TryGetSectorId(startX, startY, out int pendingStartSectorId))
                {
                    failureReason = $"start sector failed while target projection is pending source={request.Source.CharacterKey} start=({startX},{startY})";
                    return false;
                }

                PreparePendingMovingTargetProjectionNavigation(
                    agent,
                    stableGoalMovingTargetId,
                    pendingStartSectorId,
                    stableGoalInputGoal,
                    stableGoalMaximumTravelDistance);
                if (profile)
                {
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.FlowPrepareStableGoal,
                        Stopwatch.GetTimestamp() - phaseStartTicks);
                }
                return true;
            }
            if (profile)
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowPrepareStableGoal, Stopwatch.GetTimestamp() - phaseStartTicks);
            failureReason = $"goal reachability failed source={request.Source.CharacterKey} {BuildGoalResolutionFailure(request.Source, ToWorldVector3(request.InputGoalPosition))}";
            return false;
        }
        if (profile)
        {
            long stableGoalInvocationTicks = Stopwatch.GetTimestamp() - stableGoalInvocationStartTicks;
            _perf.StableGoalInvocationTicks += stableGoalInvocationTicks;
            _perf.StableGoalInvocationCount++;
            if (stableGoalInvocationTicks > _perf.StableGoalInvocationMaxTicks)
                _perf.StableGoalInvocationMaxTicks = stableGoalInvocationTicks;
            _perf.StableGoalPathRequestInvocationTicks += stableGoalInvocationTicks;
            _perf.StableGoalPathRequestInvocationCount++;
            if (stableGoalInvocationTicks > _perf.StableGoalPathRequestInvocationMaxTicks)
                _perf.StableGoalPathRequestInvocationMaxTicks = stableGoalInvocationTicks;
            long stableGoalInvocationGapTicks = stableGoalInvocationTicks - _lastStableGoalBodyTicks;
            _perf.StableGoalInvocationGapTicks += stableGoalInvocationGapTicks;
            _perf.StableGoalInvocationGapCount++;
            if (stableGoalInvocationGapTicks > _perf.StableGoalInvocationGapMaxTicks)
                _perf.StableGoalInvocationGapMaxTicks = stableGoalInvocationGapTicks;
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowPrepareStableGoal, Stopwatch.GetTimestamp() - phaseStartTicks);
        }
        long sectorValidationStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
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
        if (profile)
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationDemandSectorValidation,
                Stopwatch.GetTimestamp() - sectorValidationStartTicks);

        long assemblyStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
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
        agent.NavState.HasPreparedNavigationSnapshot = true;
        agent.NavState.PreparedNavigationFrame = GetFrameCount();
        agent.NavState.PreparedInputGoalFixed = request.InputGoalPosition;
        agent.NavState.PreparedNavigationGoalFixed = finalGoal;
        agent.NavState.PreparedMaximumTravelDistanceFixed = request.MaximumTravelDistance;
        if (profile)
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationDemandAssembly,
                Stopwatch.GetTimestamp() - assemblyStartTicks);
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
        // A moving target's exact cell is final/local binding state.  The
        // corridor policy is keyed by its stable goal sector so same-sector
        // target motion reuses the already-built hierarchy authority.
        int policyGoalCellIndex = requestKey.MovingTargetId == int.MinValue
            ? _world.GetIndex(goalX, goalY)
            : -1;
        return new SectorCorridorPolicyKey(
            requestKey.WorldVersion,
            requestKey.AgentTypeId,
            requestKey.MovingTargetId,
            requestKey.SourceIslandId,
            requestKey.GoalSectorId,
            policyGoalCellIndex,
            requestKey.GoalSectorDirtyVersion);
    }

    private static void EnqueueNavigationPathDemand(NavigationPathDemand demand)
    {
        if (demand == null || demand.Agent == null || demand.Source == null)
            throw new InvalidOperationException("Navigation path demand is incomplete.");
        NavigationPathRequestIdentityKey identityKey = CreateNavigationPathRequestIdentityKey(demand);
        NavigationPathRequestKey key = CreateNavigationPathRequestKey(demand);
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        RemoveSourceFromOtherNavigationPathRequests(demand.SourceId, identityKey);
        if (profile)
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationDemandRemoveOther, Stopwatch.GetTimestamp() - stageStartTicks);
        stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        long queueLookupStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool hasExistingJob = PendingNavigationPathRequests.TryGetValue(identityKey, out NavigationPathRequestJob job);
        if (profile)
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationDemandQueueLookup,
                Stopwatch.GetTimestamp() - queueLookupStartTicks);
        if (!hasExistingJob)
        {
            long queueCreateStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
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
            job.QueueNode = NavigationPathRequestQueue.AddLast(job);
            _perf.NavigationPathRequestGroups++;
            if (profile)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationDemandQueueCreate,
                    Stopwatch.GetTimestamp() - queueCreateStartTicks);
        }
        else if (job.Complete)
        {
            throw new InvalidOperationException("A completed navigation path request remained in the pending index.");
        }
        if (profile)
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationDemandQueueMutation, Stopwatch.GetTimestamp() - stageStartTicks);

        long sourceBindingStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        long sourceLookupStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool hasExistingSource = job.SourcesById.TryGetValue(demand.SourceId, out NavigationPathSourceJob sourceJob);
        if (profile)
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationDemandSourceLookup,
                Stopwatch.GetTimestamp() - sourceLookupStartTicks);
        if (!hasExistingSource)
        {
            long sourceInsertStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            _perf.NavigationPathRequestSourceAdds++;
            sourceJob = new NavigationPathSourceJob
            {
                Demand = demand,
                LatestDemand = demand,
                LastRequestedFrame = GetFrameCount(),
                Stage = NavigationPathSourceStage.Initialize
            };
            int insertionIndex;
            if (job.Sources.Count == 0)
            {
                insertionIndex = 0;
            }
            else
            {
                NavigationPathSourceJob lastSource = job.Sources[job.Sources.Count - 1];
                if (lastSource?.Demand == null)
                    throw new InvalidOperationException("Navigation path request contains an invalid source at its tail.");
                if (lastSource.Demand.SourceId < demand.SourceId)
                {
                    // ResolveCollectedNavigationSyncRequests sorts sources by
                    // id within a request group, so the common path appends
                    // without a second binary search and list shift.
                    insertionIndex = job.Sources.Count;
                }
                else
                {
                    insertionIndex = job.Sources.BinarySearch(
                        sourceJob,
                        NavigationPathSourceJobComparer.Instance);
                    if (insertionIndex >= 0)
                        throw new InvalidOperationException("Navigation path request contains a duplicate source id.");
                    insertionIndex = ~insertionIndex;
                }
            }
            job.Sources.Insert(insertionIndex, sourceJob);
            if (!job.SourcesById.TryAdd(demand.SourceId, sourceJob))
                throw new InvalidOperationException(
                    $"Navigation path request contains duplicate source index id={demand.SourceId}.");
            if (!NavigationPathRequestBySourceId.TryAdd(demand.SourceId, job))
                throw new InvalidOperationException(
                    $"Navigation path source dispatch index already contains source={demand.SourceId}.");
            if (insertionIndex < job.SourceCursor)
                job.SourceCursor = insertionIndex;
            if (profile)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationDemandSourceInsert,
                    Stopwatch.GetTimestamp() - sourceInsertStartTicks);
        }
        else
        {
            if (!NavigationPathRequestBySourceId.TryGetValue(demand.SourceId, out NavigationPathRequestJob indexedJob)
                || !ReferenceEquals(indexedJob, job))
            {
                throw new InvalidOperationException(
                    $"Navigation path source dispatch index disagrees with bound job source={demand.SourceId}.");
            }
            sourceJob.LatestDemand = demand;
            sourceJob.LastRequestedFrame = GetFrameCount();
        }
        if (profile)
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationDemandSourceBinding,
                Stopwatch.GetTimestamp() - sourceBindingStartTicks);

        AgentNavState nav = demand.Agent.NavState;
        stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool canConsumeCommittedPlan = demand.MovingTargetId != int.MinValue
                                       && nav.PathHandle != null
                                       && nav.PathHandle.WorldVersion == _world.Version
                                       && nav.CommittedMovingTargetId == demand.MovingTargetId
                                       && !PathReferencesMissingPortal(nav.PathHandle)
                                       && TryAdvancePathToCurrentSector(demand.Agent, demand.StartSectorId);
        if (profile)
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationDemandPathValidation, Stopwatch.GetTimestamp() - stageStartTicks);
        long snapshotPublishStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (canConsumeCommittedPlan)
        {
            nav.HasPendingNavigation = false;
            nav.HasPendingNavigationReplacement = true;
            nav.PreparedNavigationGoalFixed = nav.LastGoalWorldFixed;
            stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            EnqueueSteeringReadDomainFlowTileBuilds(demand.Agent, demand.MaximumTravelDistance);
            if (profile)
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationDemandTileBuilds, Stopwatch.GetTimestamp() - stageStartTicks);
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
        if (profile)
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationDemandSnapshotPublish,
                Stopwatch.GetTimestamp() - snapshotPublishStartTicks);
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
        if (!NavigationPathRequestBySourceId.TryGetValue(sourceId, out NavigationPathRequestJob job))
            return;
        if (job == null || job.QueueNode == null || job.QueueNode.List != NavigationPathRequestQueue)
            throw new InvalidOperationException(
                $"Navigation path source dispatch index is stale source={sourceId}.");
        if (job.IdentityKey.Equals(retainedKey))
            return;

        int sourceIndex = -1;
        for (int i = 0; i < job.Sources.Count; i++)
        {
            NavigationPathSourceJob source = job.Sources[i]
                ?? throw new InvalidOperationException("Navigation path request contains a null source job.");
            if (source.Demand == null)
                throw new InvalidOperationException("Navigation path request source has no demand.");
            if (source.Demand.SourceId == sourceId)
            {
                if (sourceIndex >= 0)
                    throw new InvalidOperationException(
                        $"Navigation path request contains duplicate source id={sourceId}.");
                sourceIndex = i;
            }
        }
        if (sourceIndex < 0)
            throw new InvalidOperationException(
                $"Navigation path source dispatch index has no matching source id={sourceId}.");

        NavigationPathSourceJob removed = job.Sources[sourceIndex];
        DisposeNavigationPathSourceTransientState(removed);
        job.Sources.RemoveAt(sourceIndex);
        if (!job.SourcesById.Remove(sourceId))
            throw new InvalidOperationException(
                $"Navigation path request source index is missing source={sourceId}.");
        NavigationPathRequestBySourceId.Remove(sourceId);
        if (sourceIndex < job.SourceCursor)
            job.SourceCursor--;
        if (job.Sources.Count == 0)
            RemoveNavigationPathRequestNode(job.QueueNode);
    }

    private static void CancelNavigationPathRequestsForSource(int sourceId)
    {
        if (!NavigationPathRequestBySourceId.TryGetValue(sourceId, out NavigationPathRequestJob job))
            return;
        if (job == null || job.QueueNode == null || job.QueueNode.List != NavigationPathRequestQueue)
            throw new InvalidOperationException(
                $"Navigation path source dispatch index is stale while cancelling source={sourceId}.");
        int sourceIndex = -1;
        for (int i = 0; i < job.Sources.Count; i++)
        {
            NavigationPathSourceJob source = job.Sources[i]
                ?? throw new InvalidOperationException("Navigation path request contains a null source job.");
            if (source.Demand?.SourceId == sourceId)
            {
                sourceIndex = i;
                break;
            }
        }
        if (sourceIndex < 0)
            throw new InvalidOperationException(
                $"Navigation path source dispatch index has no source while cancelling source={sourceId}.");
        DisposeNavigationPathSourceTransientState(job.Sources[sourceIndex]);
        job.Sources.RemoveAt(sourceIndex);
        if (!job.SourcesById.Remove(sourceId))
            throw new InvalidOperationException(
                $"Navigation path request source index is missing cancelled source={sourceId}.");
        NavigationPathRequestBySourceId.Remove(sourceId);
        if (sourceIndex < job.SourceCursor)
            job.SourceCursor--;
        if (job.Sources.Count == 0)
            RemoveNavigationPathRequestNode(job.QueueNode);
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
                if (!job.SourcesById.Remove(source.Demand.SourceId))
                    throw new InvalidOperationException(
                        $"Navigation path request source index is missing pruned source={source.Demand.SourceId}.");
                if (!NavigationPathRequestBySourceId.Remove(source.Demand.SourceId))
                    throw new InvalidOperationException(
                        $"Navigation path source dispatch index is missing pruned source={source.Demand.SourceId}.");
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
        {
            NavigationPathSourceJob source = job.Sources[i]
                ?? throw new InvalidOperationException("Navigation path request contains a null source job.");
            if (source.Demand == null)
                throw new InvalidOperationException("Navigation path request source has no demand.");
            if (!NavigationPathRequestBySourceId.Remove(source.Demand.SourceId))
                throw new InvalidOperationException(
                    $"Navigation path source dispatch index is missing source={source.Demand.SourceId}.");
            if (!job.SourcesById.Remove(source.Demand.SourceId))
                throw new InvalidOperationException(
                    $"Navigation path request source index is missing source={source.Demand.SourceId}.");
            DisposeNavigationPathSourceTransientState(job.Sources[i]);
        }
        if (job.SourcesById.Count != 0)
            throw new InvalidOperationException("Navigation path request source index contains stale entries.");
        job.SourcesById.Clear();
        DisposeSharedGoalConnectorAuthorities(job);
        job.SharedRouteMergeIndex.Dispose();
        if (!PendingNavigationPathRequests.Remove(job.IdentityKey))
            throw new InvalidOperationException("Navigation path request pending index is inconsistent.");
        NavigationPathRequestQueue.Remove(node);
        job.QueueNode = null;
    }

    private static void DisposeSharedGoalConnectorAuthorities(NavigationPathRequestJob job)
    {
        if (job == null)
            throw new InvalidOperationException("Cannot dispose shared goal connectors for a null request.");
        if (job.SharedGoalConnectors.Count == 0)
            return;

        var disposed = new HashSet<PortalHierarchyConnector>();
        foreach (PortalHierarchyConnector root in job.SharedGoalConnectors.Values)
        {
            if (root == null)
                throw new InvalidOperationException("Navigation request shared goal connector map contains a null authority.");
            for (PortalHierarchyConnector cursor = root; cursor != null; cursor = cursor.Child)
            {
                if (!disposed.Add(cursor))
                    continue;
                if (!cursor.IsRequestShared)
                    continue;
                cursor.DisposeOwnedSearchState();
                cursor.IsRequestShared = false;
            }
        }
        job.SharedGoalConnectors.Clear();
        job.SharedGoalConnectorBuildLevels.Clear();
        job.SharedGoalConnectorBuilderSourceIds.Clear();
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

    private static void ProcessNavigationPathRequestQueue(
        ref long processingTicksTotal,
        ref long queueLoopTicksTotal,
        ref long commitLoopTicksTotal)
    {
        if (_world == null)
            throw new InvalidOperationException("Navigation path request processing requires an active world.");
        bool profileProcessing = MainThreadFrameProfiler.LoggingEnabled;
        long processingStartTicks = profileProcessing ? Stopwatch.GetTimestamp() : 0L;
        int fairShareQuota = Math.Min(
            NavigationPathRequestFairShareOperationQuota,
            _remainingNavigationWorkOperations);
        if (fairShareQuota <= 0)
        {
            if (profileProcessing)
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - processingStartTicks;
                processingTicksTotal += elapsedTicks;
            }
            return;
        }

        // Round-robin the pending state machines.  The previous implementation
        // drained one job until the global quota was exhausted, which made a
        // single synchronous Burst graph slice a frame-sized stall and delayed
        // every other source.  A visit commits only the mutation made by that
        // bounded slice; no state is recomputed or discarded between visits.
        long queueLoopStartTicks = MainThreadFrameProfiler.LoggingEnabled
            ? Stopwatch.GetTimestamp()
            : 0L;
        NavigationPathRequestsBlockedByPendingSlice.Clear();
        long budgetAccountingTicks = 0L;
        long eligibilityTicks = 0L;
        long sliceDispatchTicks = 0L;
        long sliceDispatchStageTicks = 0L;
        while (!IsNavigationWorkBudgetExhausted())
        {
            bool progressed = false;
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
                if (job.Complete)
                {
                    node = next;
                    continue;
                }
                long eligibilityStartTicks = MainThreadFrameProfiler.LoggingEnabled
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                bool blockedByPendingSlice = NavigationPathRequestsBlockedByPendingSlice.Contains(job);
                bool blockedByEarlierOwner = false;
                bool blockedByOtherSource = false;
                bool blockedBySharedGoalConnector = false;
                if (!blockedByPendingSlice)
                {
                    blockedByEarlierOwner = IsNavigationPathRequestBlockedByEarlierGraphOwner(job);
                    if (!blockedByEarlierOwner)
                    {
                        blockedByOtherSource = IsNavigationPathRequestBlockedByOtherSourcePendingSlice(job);
                    }
                }
                if (!blockedByPendingSlice && !blockedByEarlierOwner && !blockedByOtherSource)
                    blockedBySharedGoalConnector = IsNavigationPathSourceBlockedBySharedGoalConnector(job);
                if (MainThreadFrameProfiler.LoggingEnabled)
                    eligibilityTicks += Stopwatch.GetTimestamp() - eligibilityStartTicks;
                if (blockedByPendingSlice)
                {
                    node = next;
                    continue;
                }
                if (blockedByEarlierOwner)
                {
                    node = next;
                    continue;
                }
                if (blockedByOtherSource)
                {
                    node = next;
                    continue;
                }
                if (blockedBySharedGoalConnector)
                {
                    node = next;
                    continue;
                }

                progressed = true;
                bool scheduledSliceBeforeAdvance = HasNavigationPathRequestScheduledSlice(job);
                bool policyMutationStarted = job.PolicyMutationStarted;
                int operationCapacity = Math.Min(
                    fairShareQuota,
                    _remainingNavigationWorkOperations);
                long sliceStartTicks = MainThreadFrameProfiler.LoggingEnabled
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                int operationCount = AdvanceNavigationPathRequestSlice(
                    job,
                    ref policyMutationStarted,
                    operationCapacity,
                    ref sliceDispatchStageTicks);
                if (MainThreadFrameProfiler.LoggingEnabled)
                    sliceDispatchTicks += Stopwatch.GetTimestamp() - sliceStartTicks;
                if (operationCount <= 0
                    || (!scheduledSliceBeforeAdvance && operationCount > operationCapacity))
                {
                    throw new InvalidOperationException(
                        $"Navigation path request slice consumed an invalid operation count. consumed={operationCount}, capacity={operationCapacity}.");
                }
                _perf.NavigationPathRequestOperations = checked(
                    _perf.NavigationPathRequestOperations + operationCount);
                job.PolicyMutationStarted = policyMutationStarted;
                long budgetStartTicks = MainThreadFrameProfiler.LoggingEnabled
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                // Worker slices consume the same logical operation budget as
                // synchronous slices.  Scheduling itself costs one visit,
                // but completion accounts for the operations actually run;
                // otherwise a frame can report more work than its quota.
                for (int i = 0; i < operationCount; i++)
                    IsBudgetExpired(0L, 0);
                if (MainThreadFrameProfiler.LoggingEnabled)
                {
                    long elapsedTicks = Stopwatch.GetTimestamp() - budgetStartTicks;
                    _perf.NavigationPathBudgetConsumeTicks += elapsedTicks;
                    budgetAccountingTicks += elapsedTicks;
                }

                // A route slice owns its native state until the worker has
                // completed. Park only while a slice is still pending; a
                // completed slice may advance again within the remaining quota.
                if (HasNavigationPathRequestScheduledSlice(job)
                    || IsNavigationPathRequestBlockedByPendingSlice(job))
                    NavigationPathRequestsBlockedByPendingSlice.Add(job);

                node = next;
            }

            if (!progressed)
                break;
        }
        NavigationPathRequestsBlockedByPendingSlice.Clear();
        long queueLoopElapsedTicks = 0L;
        if (MainThreadFrameProfiler.LoggingEnabled)
        {
            queueLoopElapsedTicks = Stopwatch.GetTimestamp() - queueLoopStartTicks;
            queueLoopTicksTotal += queueLoopElapsedTicks;
            long queueLoopUnattributedTicks = queueLoopElapsedTicks
                                               - eligibilityTicks
                                               - sliceDispatchTicks
                                               - budgetAccountingTicks;
            if (queueLoopUnattributedTicks < 0L)
            {
                throw new InvalidOperationException(
                    $"Navigation path queue loop timing is inconsistent. total={queueLoopElapsedTicks}, eligibility={eligibilityTicks}, dispatch={sliceDispatchTicks}, budget={budgetAccountingTicks}.");
            }
            if (queueLoopUnattributedTicks > 0L)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathQueueLoopUnattributed,
                    queueLoopUnattributedTicks);
            }
            if (eligibilityTicks > 0L)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathQueueEligibility,
                    eligibilityTicks);
            if (sliceDispatchTicks > 0L)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathSliceDispatch,
                    sliceDispatchTicks);
            long dispatchUnattributedTicks = sliceDispatchTicks - sliceDispatchStageTicks;
            if (dispatchUnattributedTicks < 0L)
            {
                throw new InvalidOperationException(
                    $"Navigation path slice dispatch timing is inconsistent. dispatch={sliceDispatchTicks}, stages={sliceDispatchStageTicks}.");
            }
            if (dispatchUnattributedTicks > 0L)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathSliceDispatchUnattributed,
                    dispatchUnattributedTicks);
        }
        if (MainThreadFrameProfiler.LoggingEnabled && budgetAccountingTicks > 0L)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationPathBudgetAccounting,
                budgetAccountingTicks);
        }

        long commitLoopStartTicks = profileProcessing ? Stopwatch.GetTimestamp() : 0L;
        // Commit each policy at most once for this world pass.  Keeping the
        // mutation flag on the request preserves the transaction across fair
        // share visits without multiplying authority refreshes per source.
        long policyCommitTicks = 0L;
        LinkedListNode<NavigationPathRequestJob> commitNode = NavigationPathRequestQueue.First;
        while (commitNode != null)
        {
            LinkedListNode<NavigationPathRequestJob> next = commitNode.Next;
            NavigationPathRequestJob job = commitNode.Value
                                            ?? throw new InvalidOperationException("Navigation path request queue contains a null job.");
            if (job.Key.WorldVersion == _world.Version && job.PolicyMutationStarted)
            {
                long commitStartTicks = MainThreadFrameProfiler.LoggingEnabled
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                try
                {
                    bool policyMutationStarted = job.PolicyMutationStarted;
                    CommitNavigationPathPolicyMutation(job, ref policyMutationStarted);
                    job.PolicyMutationStarted = policyMutationStarted;
                }
                finally
                {
                    if (MainThreadFrameProfiler.LoggingEnabled)
                    {
                        long elapsedTicks = Stopwatch.GetTimestamp() - commitStartTicks;
                        _perf.NavigationPathPolicyCommitTicks += elapsedTicks;
                        policyCommitTicks += elapsedTicks;
                    }
                }
            }
            commitNode = next;
        }
        if (MainThreadFrameProfiler.LoggingEnabled && policyCommitTicks > 0L)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationPathPolicyCommit,
                policyCommitTicks);
        }

        long requestRemovalTicks = 0L;
        LinkedListNode<NavigationPathRequestJob> removeNode = NavigationPathRequestQueue.First;
        while (removeNode != null)
        {
            LinkedListNode<NavigationPathRequestJob> next = removeNode.Next;
            NavigationPathRequestJob job = removeNode.Value
                                            ?? throw new InvalidOperationException("Navigation path request queue contains a null job.");
            if (job.Key.WorldVersion == _world.Version && job.Complete)
            {
                long removalStartTicks = MainThreadFrameProfiler.LoggingEnabled
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                try
                {
                    RemoveNavigationPathRequestNode(removeNode);
                }
                finally
                {
                    if (MainThreadFrameProfiler.LoggingEnabled)
                    {
                        long elapsedTicks = Stopwatch.GetTimestamp() - removalStartTicks;
                        _perf.NavigationPathRequestRemovalTicks += elapsedTicks;
                        requestRemovalTicks += elapsedTicks;
                    }
                }
            }
            removeNode = next;
        }
        if (MainThreadFrameProfiler.LoggingEnabled && requestRemovalTicks > 0L)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationPathRequestRemoval,
                requestRemovalTicks);
        }
        if (profileProcessing)
        {
            long commitLoopTicks = Stopwatch.GetTimestamp() - commitLoopStartTicks;
            commitLoopTicksTotal += commitLoopTicks;
            if (commitLoopTicks > 0L)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathQueueCommitLoop,
                    commitLoopTicks);
            long processingTicks = Stopwatch.GetTimestamp() - processingStartTicks;
            processingTicksTotal += processingTicks;
            long processingUnattributedTicks = processingTicks - queueLoopElapsedTicks - commitLoopTicks;
            if (processingUnattributedTicks < 0L)
            {
                throw new InvalidOperationException(
                    $"Navigation path queue processing timing is inconsistent. total={processingTicks}, queueLoop={queueLoopElapsedTicks}, commitLoop={commitLoopTicks}.");
            }
        }
    }

    private static bool IsNavigationPathRequestBlockedByEarlierGraphOwner(
        NavigationPathRequestJob job)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));

        for (LinkedListNode<NavigationPathRequestJob> node = NavigationPathRequestQueue.First;
             node != null;
             node = node.Next)
        {
            NavigationPathRequestJob candidate = node.Value
                ?? throw new InvalidOperationException("Navigation path request queue contains a null job.");
            if (ReferenceEquals(candidate, job))
                return false;
            if (HasPendingGraphSearchState(candidate)
                && SharesSearchStateWithPendingGraph(candidate, job))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasPendingGraphSearchState(NavigationPathRequestJob job)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));
        if (job.Policy?.SearchState?.HasPendingGraphSlice == true)
            return true;
        for (int i = 0; i < job.Sources.Count; i++)
        {
            NavigationPathSourceJob source = job.Sources[i]
                ?? throw new InvalidOperationException("Navigation path request contains a null source job.");
            if (source.RestrictedSearch?.KernelState?.HasPendingGraphSlice == true
                || source.HierarchyPolicy?.SearchState?.HasPendingGraphSlice == true)
            {
                return true;
            }
        }
        return false;
    }

    private static bool SharesSearchStateWithPendingGraph(
        NavigationPathRequestJob owner,
        NavigationPathRequestJob candidate)
    {
        if (owner == null || candidate == null)
            throw new ArgumentNullException(owner == null ? nameof(owner) : nameof(candidate));

        if (owner.Policy?.SearchState?.HasPendingGraphSlice == true
            && JobUsesSearchState(candidate, owner.Policy.SearchState))
        {
            return true;
        }

        for (int i = 0; i < owner.Sources.Count; i++)
        {
            NavigationPathSourceJob source = owner.Sources[i]
                ?? throw new InvalidOperationException("Navigation path request contains a null source job.");
            FlowPathKernelSearchState restricted = source.RestrictedSearch?.KernelState;
            if (restricted?.HasPendingGraphSlice == true && JobUsesSearchState(candidate, restricted))
                return true;
            FlowPathKernelSearchState hierarchy = source.HierarchyPolicy?.SearchState;
            if (hierarchy?.HasPendingGraphSlice == true && JobUsesSearchState(candidate, hierarchy))
                return true;
        }
        return false;
    }

    private static bool JobUsesSearchState(
        NavigationPathRequestJob job,
        FlowPathKernelSearchState state)
    {
        if (job == null || state == null)
            return false;
        if (ReferenceEquals(job.Policy?.SearchState, state))
            return true;
        for (int i = 0; i < job.Sources.Count; i++)
        {
            NavigationPathSourceJob source = job.Sources[i]
                ?? throw new InvalidOperationException("Navigation path request contains a null source job.");
            if (ReferenceEquals(source.RestrictedSearch?.KernelState, state)
                || ReferenceEquals(source.HierarchyPolicy?.SearchState, state))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsNavigationPathRequestBlockedByPendingSlice(NavigationPathRequestJob job)
    {
        if (job == null || job.Complete || job.SourceCursor >= job.Sources.Count)
            return false;
        NavigationPathSourceJob source = job.Sources[job.SourceCursor]
            ?? throw new InvalidOperationException("Navigation path request contains a null source job.");
        IncrementalRouteMaterialization materialization = source.Materialization;
        bool routePending = materialization != null
                            && materialization.RouteState.HasPendingSlice
                            && !materialization.RouteState.IsPendingSliceCompleted;
        if (routePending || IsNavigationPathSourceBlockedByPendingGraph(job, source))
            return true;

        // A hierarchy/L0 policy search is shared by all sources in the
        // request. If another source owns its scheduled graph slice, keep the
        // request parked until that source completes the slice; otherwise a
        // later source could read the shared search state while the job owns it.
        if (HasPendingGraphSearchState(job) && !IsNavigationPathSourcePendingGraphOwner(job, source))
            return true;
        return false;
    }

    private static bool IsNavigationPathRequestBlockedByOtherSourcePendingSlice(
        NavigationPathRequestJob job)
    {
        if (job == null || job.Complete || job.SourceCursor >= job.Sources.Count)
            return false;
        NavigationPathSourceJob current = job.Sources[job.SourceCursor]
            ?? throw new InvalidOperationException("Navigation path request contains a null source job.");
        if (IsNavigationPathSourcePendingGraphOwner(job, current)
            || (current.Materialization?.RouteState?.HasPendingSlice == true))
        {
            return false;
        }
        if (HasPendingGraphSearchState(job))
            return true;
        for (int i = 0; i < job.Sources.Count; i++)
        {
            NavigationPathSourceJob source = job.Sources[i]
                ?? throw new InvalidOperationException("Navigation path request contains a null source job.");
            if (source.Materialization?.RouteState?.HasPendingSlice == true)
                return true;
        }
        return false;
    }

    private static bool IsNavigationPathSourceBlockedBySharedGoalConnector(
        NavigationPathRequestJob job)
    {
        if (job == null || job.Complete || job.SourceCursor >= job.Sources.Count)
            return false;

        NavigationPathSourceJob source = job.Sources[job.SourceCursor]
            ?? throw new InvalidOperationException("Navigation path request contains a null source job.");
        if (source.Stage != NavigationPathSourceStage.BuildGoalConnector
            || source.HierarchyLevelArrayIndex < 0
            || job.SharedGoalConnectors.ContainsKey(source.HierarchyLevelArrayIndex))
        {
            return false;
        }

        int depth = source.HierarchyLevelArrayIndex;
        if (!job.SharedGoalConnectorBuildLevels.Contains(depth))
            return false;
        if (!job.SharedGoalConnectorBuilderSourceIds.TryGetValue(depth, out int builderSourceId))
        {
            throw new InvalidOperationException(
                $"Navigation shared goal connector build has no deterministic builder depth={depth} target={job.Key.MovingTargetId}.");
        }

        return builderSourceId != source.Demand.SourceId;
    }

    private static bool IsNavigationPathSourcePendingGraphOwner(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source)
    {
        if (job == null || source == null || source.ReversePolicyExpansion == null)
            return source?.RestrictedSearch?.KernelState?.HasPendingGraphSlice == true;

        IncrementalReversePolicyExpansion expansion = source.ReversePolicyExpansion;
        bool expansionCanOwn = expansion.Stage == IncrementalReversePolicyExpansionStage.Pop
                               || expansion.Stage == IncrementalReversePolicyExpansionStage.Edges
                               || expansion.Stage == IncrementalReversePolicyExpansionStage.Crossing;
        if (!expansionCanOwn)
            return false;
        FlowPathKernelSearchState search = expansion.Hierarchy
            ? source.HierarchyPolicy?.SearchState
            : job.Policy?.SearchState;
        return search?.HasPendingGraphSlice == true;
    }

    private static bool HasNavigationPathRequestScheduledSlice(NavigationPathRequestJob job)
    {
        if (job == null || job.Complete || job.SourceCursor >= job.Sources.Count)
            return false;
        NavigationPathSourceJob source = job.Sources[job.SourceCursor]
            ?? throw new InvalidOperationException("Navigation path request contains a null source job.");
        IncrementalRouteMaterialization materialization = source.Materialization;
        return (materialization != null && materialization.RouteState.HasPendingSlice)
               || IsNavigationPathSourceScheduledGraph(job, source);
    }

    private static bool IsNavigationPathSourceBlockedByPendingGraph(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source)
    {
        return IsNavigationPathSourceScheduledGraph(job, source)
               && !IsNavigationPathSourceGraphCompleted(job, source);
    }

    private static bool IsNavigationPathSourceScheduledGraph(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source)
    {
        if (source?.RestrictedSearch?.KernelState?.HasPendingGraphSlice == true)
            return true;
        if (source?.ReversePolicyExpansion != null)
        {
            FlowPathKernelSearchState search = source.ReversePolicyExpansion.Hierarchy
                ? source.HierarchyPolicy?.SearchState
                : job?.Policy?.SearchState;
            if (search?.HasPendingGraphSlice == true)
                return true;
        }
        if (source?.HierarchyPolicy?.SearchState?.HasPendingGraphSlice == true)
            return true;
        return false;
    }

    private static bool IsNavigationPathSourceGraphCompleted(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source)
    {
        if (source?.RestrictedSearch?.KernelState?.HasPendingGraphSlice == true
            && !source.RestrictedSearch.KernelState.IsPendingGraphSliceCompleted)
            return false;
        if (source?.ReversePolicyExpansion != null)
        {
            FlowPathKernelSearchState search = source.ReversePolicyExpansion.Hierarchy
                ? source.HierarchyPolicy?.SearchState
                : job?.Policy?.SearchState;
            if (search?.HasPendingGraphSlice == true && !search.IsPendingGraphSliceCompleted)
                return false;
        }
        if (source?.HierarchyPolicy?.SearchState?.HasPendingGraphSlice == true
            && !source.HierarchyPolicy.SearchState.IsPendingGraphSliceCompleted)
            return false;
        return true;
    }

    private static void ProcessNavigationPathRequestsAcrossWorlds()
    {
        NavigationWorld previousWorld = _world;
        WorldRuntimeState previousState = _activeWorldState;
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long processStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        long stateEnumerationStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        long stateEnumerationTicks = 0L;
        long activationTicks = 0L;
        long queueProcessingTicks = 0L;
        long queueLoopTicks = 0L;
        long queueCommitLoopTicks = 0L;
        var states = new List<WorldRuntimeState>(WorldStates.Values);
        states.Sort((left, right) => left.AgentTypeId.CompareTo(right.AgentTypeId));
        if (profile)
        {
            stateEnumerationTicks = Stopwatch.GetTimestamp() - stateEnumerationStartTicks;
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationPathWorldStateEnumeration,
                stateEnumerationTicks);
        }
        try
        {
            for (int i = 0; i < states.Count && !IsNavigationWorkBudgetExhausted(); i++)
            {
                long activationStartTicks = MainThreadFrameProfiler.LoggingEnabled
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                ActivateFlowBuildQueueWorld(states[i]);
                if (MainThreadFrameProfiler.LoggingEnabled)
                {
                    long elapsedTicks = Stopwatch.GetTimestamp() - activationStartTicks;
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.FlowNavigationPathWorldActivation,
                        elapsedTicks);
                    activationTicks += elapsedTicks;
                }
                ProcessNavigationPathRequestQueue(
                    ref queueProcessingTicks,
                    ref queueLoopTicks,
                    ref queueCommitLoopTicks);
            }
        }
        finally
        {
            long restoreStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            _world = previousWorld;
            _activeWorldState = previousState;
            long restoreTicks = profile ? Stopwatch.GetTimestamp() - restoreStartTicks : 0L;
            if (profile && restoreTicks > 0L)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathWorldRestore,
                    restoreTicks);
            if (profile)
            {
                long processTicks = Stopwatch.GetTimestamp() - processStartTicks;
                long queueProcessingUnattributedTicks = queueProcessingTicks
                                                         - queueLoopTicks
                                                         - queueCommitLoopTicks;
                if (queueProcessingUnattributedTicks < 0L)
                {
                    throw new InvalidOperationException(
                        $"Navigation path queue aggregate timing is inconsistent. total={queueProcessingTicks}, queueLoop={queueLoopTicks}, commitLoop={queueCommitLoopTicks}.");
                }
                if (queueProcessingUnattributedTicks > 0L)
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.FlowNavigationPathQueueProcessingUnattributed,
                        queueProcessingUnattributedTicks);
                long knownTicks = stateEnumerationTicks
                                  + activationTicks
                                  + queueLoopTicks
                                  + queueCommitLoopTicks
                                  + queueProcessingUnattributedTicks
                                  + restoreTicks;
                long outerUnattributedTicks = processTicks - knownTicks;
                if (outerUnattributedTicks < 0L)
                {
                    throw new InvalidOperationException(
                        $"Navigation path world processing timing is inconsistent. total={processTicks}, known={knownTicks}, activation={activationTicks}, queue={queueProcessingTicks}.");
                }
                if (outerUnattributedTicks > 0L)
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.FlowNavigationPathWorldOuterUnattributed,
                        outerUnattributedTicks);
            }
        }
    }

    private static int AdvanceNavigationPathRequestSlice(
        NavigationPathRequestJob job,
        ref bool policyMutationStarted,
        int operationCapacity,
        ref long stageElapsedTicks)
    {
        if (operationCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(operationCapacity));
        if (job.SourceCursor >= job.Sources.Count)
        {
            bool recordTerminalTiming = MainThreadFrameProfiler.LoggingEnabled;
            long terminalStartTicks = recordTerminalTiming ? Stopwatch.GetTimestamp() : 0L;
            try
            {
                if (RestartCompletedNavigationPathSourceMergesForLatestStarts(job, ref policyMutationStarted))
                    return 1;
                CommitNavigationPathRequest(job);
                job.Complete = true;
                return 1;
            }
            finally
            {
                if (recordTerminalTiming)
                {
                    long elapsedTicks = Stopwatch.GetTimestamp() - terminalStartTicks;
                    stageElapsedTicks += elapsedTicks;
                    RecordNavigationPathRequestStage(
                        NavigationPathSourceStage.Complete,
                        1,
                        elapsedTicks);
                    RecordNavigationPathSliceDispatchStage(
                        NavigationPathSourceStage.Complete,
                        elapsedTicks);
                }
            }
        }

        bool profileDispatchParts = MainThreadFrameProfiler.LoggingEnabled;
        long dispatchPreparationStartTicks = profileDispatchParts ? Stopwatch.GetTimestamp() : 0L;
        NavigationPathSourceJob source = job.Sources[job.SourceCursor];
        NavigationPathSourceStage stage = source.Stage;
        IncrementalRouteMaterializationStage materializationStage = source.Materialization?.Stage
                                                                   ?? IncrementalRouteMaterializationStage.Initialize;
        int routeTaskType = -1;
        if (source.Materialization != null)
        {
            FlowPathKernelRouteState routeState = source.Materialization.RouteState;
            if (!routeState.HasPendingSlice && routeState.TaskCount > 0)
                routeTaskType = routeState.Peek().Type;
        }
        if (profileDispatchParts)
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationPathSliceDispatchPreparation,
                Stopwatch.GetTimestamp() - dispatchPreparationStartTicks);
        bool recordTiming = MainThreadFrameProfiler.LoggingEnabled;
        long startTicks = recordTiming ? Stopwatch.GetTimestamp() : 0L;
        int operationCount = 1;
        try
        {
            switch (stage)
            {
                case NavigationPathSourceStage.Initialize:
                    InitializeNavigationPathSource(job, source);
                    break;
                case NavigationPathSourceStage.BuildGoalConnector:
                    operationCount = AdvanceNavigationGoalConnector(job, source, operationCapacity);
                    break;
                case NavigationPathSourceStage.CreateHierarchyPolicy:
                    operationCount = CreateNavigationHierarchyPolicy(
                        job,
                        source,
                        ref policyMutationStarted,
                        operationCapacity);
                    break;
                case NavigationPathSourceStage.ExpandHierarchyPolicy:
                    operationCount = AdvanceNavigationHierarchyPolicy(
                        job,
                        source,
                        ref policyMutationStarted,
                        operationCapacity);
                    break;
                case NavigationPathSourceStage.BuildDownwardCustomization:
                    operationCount = AdvanceNavigationDownwardCustomization(
                        job,
                        source,
                        ref policyMutationStarted,
                        operationCapacity);
                    break;
                case NavigationPathSourceStage.ExpandL0Policy:
                    operationCount = AdvanceNavigationL0Policy(
                        job,
                        source,
                        ref policyMutationStarted,
                        operationCapacity);
                    break;
                case NavigationPathSourceStage.MaterializeRoute:
                    operationCount = AdvanceNavigationPathSourceMaterialization(
                        job,
                        source,
                        ref policyMutationStarted,
                        operationCapacity);
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
            long dispatchFinalizationStartTicks = recordTiming ? Stopwatch.GetTimestamp() : 0L;
                long elapsedTicks = recordTiming ? Stopwatch.GetTimestamp() - startTicks : 0L;
                if (recordTiming)
                {
                    stageElapsedTicks += elapsedTicks;
                    MainThreadFrameProfiler.RecordLogicTickInvocation(
                        MainThreadPerfScope.FlowNavigationResolvePathQueueBatch,
                        elapsedTicks);
                    RecordNavigationPathRequestStage(stage, operationCount, elapsedTicks);
                RecordNavigationPathSliceDispatchStage(stage, elapsedTicks);
            }
            else
                RecordNavigationPathRequestStage(stage, operationCount, 0L);
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
            if (recordTiming)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathSliceDispatchFinalization,
                    Stopwatch.GetTimestamp() - dispatchFinalizationStartTicks);
        }
        return operationCount;
    }

    private static void RecordNavigationPathSliceDispatchStage(
        NavigationPathSourceStage stage,
        long elapsedTicks)
    {
        if (elapsedTicks <= 0L)
            return;

        MainThreadPerfScope scope;
        switch (stage)
        {
            case NavigationPathSourceStage.Initialize:
                scope = MainThreadPerfScope.FlowNavigationPathSliceInitialize;
                break;
            case NavigationPathSourceStage.BuildGoalConnector:
                scope = MainThreadPerfScope.FlowNavigationPathSliceGoalConnector;
                break;
            case NavigationPathSourceStage.CreateHierarchyPolicy:
                scope = MainThreadPerfScope.FlowNavigationPathSliceCreateHierarchy;
                break;
            case NavigationPathSourceStage.ExpandHierarchyPolicy:
                scope = MainThreadPerfScope.FlowNavigationPathSliceExpandHierarchy;
                break;
            case NavigationPathSourceStage.BuildDownwardCustomization:
                scope = MainThreadPerfScope.FlowNavigationPathSliceDownward;
                break;
            case NavigationPathSourceStage.ExpandL0Policy:
                scope = MainThreadPerfScope.FlowNavigationPathSliceL0;
                break;
            case NavigationPathSourceStage.MaterializeRoute:
                scope = MainThreadPerfScope.FlowNavigationPathSliceMaterialize;
                break;
            case NavigationPathSourceStage.Complete:
                scope = MainThreadPerfScope.FlowNavigationPathSliceComplete;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown navigation path source stage.");
        }

        MainThreadFrameProfiler.Record(scope, elapsedTicks);
    }

    private static void RecordNavigationPathRequestStage(
        NavigationPathSourceStage stage,
        int operationCount,
        long elapsedTicks)
    {
        if (operationCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(operationCount));
        switch (stage)
        {
            case NavigationPathSourceStage.Initialize:
                _perf.NavigationPathInitializeOperations = checked(_perf.NavigationPathInitializeOperations + operationCount);
                _perf.NavigationPathInitializeTicks += elapsedTicks;
                return;
            case NavigationPathSourceStage.BuildGoalConnector:
                _perf.NavigationPathGoalConnectorOperations = checked(_perf.NavigationPathGoalConnectorOperations + operationCount);
                _perf.NavigationPathGoalConnectorTicks += elapsedTicks;
                return;
            case NavigationPathSourceStage.CreateHierarchyPolicy:
                _perf.NavigationPathCreateHierarchyOperations = checked(_perf.NavigationPathCreateHierarchyOperations + operationCount);
                _perf.NavigationPathCreateHierarchyTicks += elapsedTicks;
                return;
            case NavigationPathSourceStage.ExpandHierarchyPolicy:
                _perf.NavigationPathExpandHierarchyOperations = checked(_perf.NavigationPathExpandHierarchyOperations + operationCount);
                _perf.NavigationPathExpandHierarchyTicks += elapsedTicks;
                return;
            case NavigationPathSourceStage.BuildDownwardCustomization:
                _perf.NavigationPathDownwardOperations = checked(_perf.NavigationPathDownwardOperations + operationCount);
                _perf.NavigationPathDownwardTicks += elapsedTicks;
                return;
            case NavigationPathSourceStage.ExpandL0Policy:
                _perf.NavigationPathL0Operations = checked(_perf.NavigationPathL0Operations + operationCount);
                _perf.NavigationPathL0Ticks += elapsedTicks;
                return;
            case NavigationPathSourceStage.MaterializeRoute:
                _perf.NavigationPathMaterializeOperations = checked(_perf.NavigationPathMaterializeOperations + operationCount);
                _perf.NavigationPathMaterializeTicks += elapsedTicks;
                return;
            case NavigationPathSourceStage.Complete:
                _perf.NavigationPathCompleteOperations = checked(_perf.NavigationPathCompleteOperations + operationCount);
                _perf.NavigationPathCompleteTicks += elapsedTicks;
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown navigation path source stage.");
        }
    }

    private static void RecordNavigationPathStageProfilerScopes()
    {
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathInitialize,
            _perf.NavigationPathInitializeTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathGoalConnector,
            _perf.NavigationPathGoalConnectorTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathCreateHierarchy,
            _perf.NavigationPathCreateHierarchyTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathExpandHierarchy,
            _perf.NavigationPathExpandHierarchyTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathDownward,
            _perf.NavigationPathDownwardTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathL0,
            _perf.NavigationPathL0Ticks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterialize,
            _perf.NavigationPathMaterializeTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterializeStageInitialize,
            _perf.NavigationPathMaterializeInitializeTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterializeStageStartPortal,
            _perf.NavigationPathMaterializeStartPortalTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterializeStageDownward,
            _perf.NavigationPathMaterializeDownwardTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterializeStagePolicy,
            _perf.NavigationPathMaterializePolicyTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterializeStageGoalConnector,
            _perf.NavigationPathMaterializeGoalConnectorTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterializeStageConversion,
            _perf.NavigationPathMaterializeConversionTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterializeStageImmutableCopy,
            _perf.NavigationPathMaterializeImmutableCopyTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterializeStageHash,
            _perf.NavigationPathMaterializeHashTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterializeStagePublish,
            _perf.NavigationPathMaterializePublishTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterializePublishSuffix,
            _perf.NavigationPathMaterializePublishSuffixTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterializePublishSuffixResolve,
            _perf.NavigationPathMaterializePublishSuffixResolveTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterializePublishSuffixValidation,
            _perf.NavigationPathMaterializePublishSuffixValidationTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterializePublishSuffixInsert,
            _perf.NavigationPathMaterializePublishSuffixInsertTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterializePublishSuffixBookkeeping,
            _perf.NavigationPathMaterializePublishSuffixBookkeepingTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathMaterializePublishSuffixFinalize,
            _perf.NavigationPathMaterializePublishSuffixFinalizeTicks);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowNavigationPathComplete,
            _perf.NavigationPathCompleteTicks);
    }

    private static bool RestartCompletedNavigationPathSourceMergesForLatestStarts(
        NavigationPathRequestJob job,
        ref bool policyMutationStarted)
    {
        if (RestartCompletedNavigationPathRequestForLatestGoal(job, ref policyMutationStarted))
            return true;

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

    private static bool RestartCompletedNavigationPathRequestForLatestGoal(
        NavigationPathRequestJob job,
        ref bool policyMutationStarted)
    {
        if (job.Key.MovingTargetId == int.MinValue || job.Sources.Count == 0)
            return false;
        NavigationPathDemand latestGoal = job.Sources[0].LatestDemand
            ?? throw new InvalidOperationException("Navigation path request has no latest moving-target demand.");
        for (int i = 1; i < job.Sources.Count; i++)
        {
            NavigationPathDemand candidate = job.Sources[i].LatestDemand
                ?? throw new InvalidOperationException("Navigation path request has no latest moving-target demand.");
            if (candidate.GoalSectorId != latestGoal.GoalSectorId
                || candidate.GoalX != latestGoal.GoalX
                || candidate.GoalY != latestGoal.GoalY
                || candidate.StableGoal != latestGoal.StableGoal)
            {
                throw new InvalidOperationException(
                    "Navigation path request moving-target sources disagree on the latest stable goal.");
            }
        }
        if (latestGoal.GoalSectorId == job.Key.GoalSectorId)
            return false;

        // A moving target crossing a sector does not invalidate the source-side
        // hierarchy route when the committed goal policy can be rebound exactly.
        // Keep that policy authority and rebuild only each source's local route
        // materialization.  The full reset below remains the strict topology
        // change path when the boundary costs are not a uniform shift.
        if (TryRebindCompletedNavigationPathRequestGoal(job, latestGoal, ref policyMutationStarted))
            return true;

        DisposeSharedGoalConnectorAuthorities(job);
        for (int i = 0; i < job.Sources.Count; i++)
        {
            NavigationPathSourceJob source = job.Sources[i];
            if (source.Stage != NavigationPathSourceStage.Complete || source.Handle == null)
                throw new InvalidOperationException("Navigation path goal follow-up requires completed frozen sources.");
            source.Demand = source.LatestDemand;
            ResetNavigationPathSourceJobForLatestStart(source);
        }

        CommitNavigationPathPolicyMutation(job, ref policyMutationStarted);
        NavigationPathRequestKey nextKey = CreateNavigationPathRequestKey(latestGoal);
        job.Key = nextKey;
        job.PolicyKey = CreateNavigationPathPolicyKey(nextKey, latestGoal.GoalX, latestGoal.GoalY);
        job.Policy = null;
        job.GoalX = latestGoal.GoalX;
        job.GoalY = latestGoal.GoalY;
        job.StableGoal = latestGoal.StableGoal;
        job.SharedRouteSuffixes.Clear();
        job.SharedRouteMergeIndex.Clear();
        job.SharedRouteSuffixesAuthorityContentHash = 0UL;
        job.SourceCursor = 0;
        return true;
    }

    private static bool TryRebindCompletedNavigationPathRequestGoal(
        NavigationPathRequestJob job,
        NavigationPathDemand latestGoal,
        ref bool policyMutationStarted)
    {
        SectorCorridorPolicy policy = job.Policy;
        if (policy == null || !policy.HasAuthorityContentHash)
            return false;
        if (policy.HierarchyPolicies.Count == 0)
            return false;

        var anchorKey = new MovingTargetAnchorKey(
            job.Key.MovingTargetId,
            job.Key.AgentTypeId,
            job.Key.SourceIslandId);
        if (!MovingTargetAnchors.TryGetValue(anchorKey, out MovingTargetAnchor anchor)
            || anchor == null
            || !anchor.HasPinnedSectorCorridorPolicy
            || !anchor.PinnedSectorCorridorPolicyKey.Equals(job.PolicyKey))
        {
            throw new InvalidOperationException(
                $"Completed moving-target request has no matching pinned policy target={job.Key.MovingTargetId}, " +
                $"agentType={job.Key.AgentTypeId}, island={job.Key.SourceIslandId}.");
        }
        if (!SectorCorridorPolicies.TryGetValue(job.PolicyKey, out SectorCorridorPolicy mappedPolicy)
            || !ReferenceEquals(mappedPolicy, policy))
        {
            throw new InvalidOperationException("Completed moving-target request policy map is not authoritative.");
        }

        NavigationPathRequestKey nextKey = CreateNavigationPathRequestKey(latestGoal);
        SectorCorridorPolicyKey nextPolicyKey = CreateNavigationPathPolicyKey(
            nextKey,
            latestGoal.GoalX,
            latestGoal.GoalY);

        // Remove the old key from the authority index while the policy is
        // mutated.  It remains owned by this pending request throughout.
        _sectorCorridorPolicyAuthorityContentHash ^= policy.AuthorityContentHash;
        SectorCorridorPolicies.Remove(job.PolicyKey);
        policy.HasAuthorityContentHash = false;
        anchor.HasPinnedSectorCorridorPolicy = false;
        anchor.PinnedSectorCorridorPolicyKey = default;

        bool rebound;
        try
        {
            rebound = TryRebindSectorCorridorPolicyExactGoal(
                policy,
                latestGoal.GoalSectorId,
                latestGoal.GoalX,
                latestGoal.GoalY);
        }
        catch
        {
            policy.HasAuthorityContentHash = true;
            SectorCorridorPolicies[job.PolicyKey] = policy;
            RefreshSectorCorridorPolicyAuthority(job.PolicyKey, policy);
            PinMovingTargetSectorCorridorPolicy(anchor, job.PolicyKey);
            throw;
        }

        if (!rebound)
        {
            policy.HasAuthorityContentHash = true;
            SectorCorridorPolicies[job.PolicyKey] = policy;
            RefreshSectorCorridorPolicyAuthority(job.PolicyKey, policy);
            PinMovingTargetSectorCorridorPolicy(anchor, job.PolicyKey);
            return false;
        }

        RefreshSectorCorridorPolicyAuthority(nextPolicyKey, policy);
        SectorCorridorPolicies[nextPolicyKey] = policy;
        PinMovingTargetSectorCorridorPolicy(anchor, nextPolicyKey);
        job.Key = nextKey;
        job.PolicyKey = nextPolicyKey;
        job.GoalX = latestGoal.GoalX;
        job.GoalY = latestGoal.GoalY;
        job.StableGoal = latestGoal.StableGoal;
        DisposeSharedGoalConnectorAuthorities(job);
        job.SharedRouteSuffixes.Clear();
        job.SharedRouteMergeIndex.Clear();
        job.SharedRouteSuffixesAuthorityContentHash = 0UL;

        for (int i = 0; i < job.Sources.Count; i++)
        {
            NavigationPathSourceJob source = job.Sources[i]
                ?? throw new InvalidOperationException("Navigation path request contains a null source job.");
            if (source.Stage != NavigationPathSourceStage.Complete || source.Handle == null)
                throw new InvalidOperationException("Moving-target policy rebind requires completed source routes.");

            source.Demand = source.LatestDemand
                ?? throw new InvalidOperationException("Navigation path request source has no latest demand snapshot.");
            // A moving target crossing into a sector already present in the
            // committed corridor only changes the terminal binding. Preserve
            // that corridor and trim its suffix instead of re-expanding the
            // same hierarchy route for every source on every crossing.
            if (TryRetainNavigationPathThroughGoalSector(source, source.Demand, out PathHandle retainedHandle))
            {
                source.Handle = retainedHandle;
                source.Stage = NavigationPathSourceStage.Complete;
                _perf.NavigationPathMovingGoalCorridorTrimHits++;
                continue;
            }
            if (source.HierarchyLevelArrayIndex < 0)
                throw new InvalidOperationException(
                    $"Moving-target policy rebind encountered a source without hierarchy source={source.Demand.SourceId}.");

            if (source.Materialization?.RouteState != null)
                source.Materialization.RouteState.Dispose();
            source.Materialization = null;
            if (source.RestrictedSearch?.KernelState != null)
                source.RestrictedSearch.KernelState.Dispose();
            source.RestrictedSearch = null;
            EndRestrictedInputCollection(source);
            source.ReversePolicyExpansion = null;
            source.GoalConnector = null;
            source.NextGoalConnectorLevel = 0;
            source.DownwardCustomizations.Clear();
            source.NextDownwardLevel = source.HierarchyLevelArrayIndex;
            source.Handle = null;
            source.Stage = NavigationPathSourceStage.BuildDownwardCustomization;
        }

        job.SourceCursor = 0;
        policyMutationStarted = false;
        return true;
    }

    private static bool TryRetainNavigationPathThroughGoalSector(
        NavigationPathSourceJob source,
        NavigationPathDemand latestGoal,
        out PathHandle retainedHandle)
    {
        retainedHandle = null;
        PathHandle current = source.Handle;
        if (current == null
            || current.WorldVersion != _world.Version
            || current.SectorIds == null
            || current.PortalIds == null
            || current.SectorIds.Length != current.PortalIds.Length + 1)
        {
            return false;
        }

        int currentIndex = Mathf.Max(0, current.CurrentSectorIndex);
        int goalIndex = FindSectorIndex(current, latestGoal.GoalSectorId, currentIndex);
        if (goalIndex < currentIndex)
            return false;

        int sectorCount = goalIndex + 1;
        int portalCount = goalIndex;
        var sectors = new int[sectorCount];
        var portals = new int[portalCount];
        current.SectorIds.CopyTo(0, sectors, 0, sectorCount);
        if (portalCount > 0)
            current.PortalIds.CopyTo(0, portals, 0, portalCount);

        retainedHandle = new PathHandle
        {
            HandleId = _nextPathHandleId++,
            WorldVersion = current.WorldVersion,
            GoalX = latestGoal.GoalX,
            GoalY = latestGoal.GoalY,
            SectorIds = ImmutableRouteSequence.FromArray(sectors),
            PortalIds = ImmutableRouteSequence.FromArray(portals),
            CurrentSectorIndex = currentIndex,
            BuildSource = current.BuildSource + ":movingGoalTrim"
        };
        return true;
    }

    private static void CommitNavigationPathPolicyMutation(
        NavigationPathRequestJob job,
        ref bool policyMutationStarted)
    {
        if (!policyMutationStarted)
            return;
        if (job?.Policy == null)
            throw new InvalidOperationException("Navigation path policy mutation lost its authority instance before commit.");
        if (_navigationSyncBatchResolveActive)
        {
            DeferredSectorCorridorPolicyAuthorityKeys.Add(job.PolicyKey);
            policyMutationStarted = false;
            return;
        }
        RefreshSectorCorridorPolicyAuthority(job.PolicyKey, job.Policy);
        policyMutationStarted = false;
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
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        NavigationPathDemand demand = source.Demand;
        if (demand.StartSectorId == job.Key.GoalSectorId)
        {
            long phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            bool connected = AreCellsConnectedInsideSector(
                    _world.Sectors[demand.StartSectorId],
                    demand.StartX,
                    demand.StartY,
                    job.GoalX,
                    job.GoalY);
            if (profile)
            {
                _perf.NavigationPathInitializeSameSectorTicks += Stopwatch.GetTimestamp() - phaseStartTicks;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathInitializeSameSector,
                    Stopwatch.GetTimestamp() - phaseStartTicks);
            }
            source.Stage = connected
                ? NavigationPathSourceStage.MaterializeRoute
                : NavigationPathSourceStage.ExpandL0Policy;
            return;
        }

        long policyStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        EnsureNavigationPathRequestPolicy(job);
        if (profile)
        {
            _perf.NavigationPathInitializePolicyTicks += Stopwatch.GetTimestamp() - policyStartTicks;
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationPathInitializePolicy,
                Stopwatch.GetTimestamp() - policyStartTicks);
        }
        long hierarchyResolveStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        source.HierarchyLevelArrayIndex = ResolveHighestRequiredPortalHierarchyLevelIndex(
            _world,
            demand.StartSectorId,
            job.Key.GoalSectorId);
        if (profile)
        {
            _perf.NavigationPathInitializeHierarchyResolveTicks +=
                Stopwatch.GetTimestamp() - hierarchyResolveStartTicks;
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationPathInitializeHierarchyResolve,
                Stopwatch.GetTimestamp() - hierarchyResolveStartTicks);
        }
        if (source.HierarchyLevelArrayIndex < 0)
        {
            source.Stage = NavigationPathSourceStage.ExpandL0Policy;
            return;
        }

        long hierarchySelectionStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (job.Policy.HierarchyPolicies.TryGetValue(
                source.HierarchyLevelArrayIndex,
                out PortalHierarchyReversePolicy cached))
        {
            source.HierarchyPolicy = cached;
            source.NextDownwardLevel = source.HierarchyLevelArrayIndex;
            source.Stage = NavigationPathSourceStage.ExpandHierarchyPolicy;
            if (profile)
            {
                _perf.NavigationPathInitializeHierarchySelectionTicks +=
                    Stopwatch.GetTimestamp() - hierarchySelectionStartTicks;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathInitializeHierarchySelection,
                    Stopwatch.GetTimestamp() - hierarchySelectionStartTicks);
            }
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
        source.Stage = NavigationPathSourceStage.BuildGoalConnector;
        if (profile)
        {
            _perf.NavigationPathInitializeHierarchySelectionTicks +=
                Stopwatch.GetTimestamp() - hierarchySelectionStartTicks;
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationPathInitializeHierarchySelection,
                Stopwatch.GetTimestamp() - hierarchySelectionStartTicks);
        }
    }

    private static void EnsureNavigationPathRequestPolicy(NavigationPathRequestJob job)
    {
        if (job.Policy != null)
            return;
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (SectorCorridorPolicies.TryGetValue(job.PolicyKey, out SectorCorridorPolicy cached))
        {
            if (profile)
            {
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationPolicyInitializeLookup, Stopwatch.GetTimestamp() - phaseStartTicks);
                _perf.NavigationPathInitializePolicyLookupTicks += Stopwatch.GetTimestamp() - phaseStartTicks;
            }
            job.Policy = cached;
            return;
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationPolicyInitializeLookup, Stopwatch.GetTimestamp() - phaseStartTicks);
            _perf.NavigationPathInitializePolicyLookupTicks += Stopwatch.GetTimestamp() - phaseStartTicks;
        }

        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
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
            if (TryAcquirePinnedMovingTargetSectorCorridorPolicy(
                    anchor,
                    job.PolicyKey,
                    job.Key.GoalSectorId,
                    job.GoalX,
                    job.GoalY,
                    out SectorCorridorPolicy pinnedPolicy))
            {
                job.Policy = pinnedPolicy;
                if (profile)
                {
                    MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationPolicyInitializeAnchor, Stopwatch.GetTimestamp() - phaseStartTicks);
                    _perf.NavigationPathInitializePolicyAnchorTicks += Stopwatch.GetTimestamp() - phaseStartTicks;
                }
                return;
            }
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationPolicyInitializeAnchor, Stopwatch.GetTimestamp() - phaseStartTicks);
            _perf.NavigationPathInitializePolicyAnchorTicks += Stopwatch.GetTimestamp() - phaseStartTicks;
        }

        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        job.Policy = new SectorCorridorPolicy
        {
            GoalSectorId = job.Key.GoalSectorId,
            GoalCellIndex = _world.GetIndex(job.GoalX, job.GoalY),
            GoalSectorDirtyVersion = job.Key.GoalSectorDirtyVersion,
            LastUsedFrame = GetFrameCount()
        };
        if (profile)
        {
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationPolicyInitializeConstruct, Stopwatch.GetTimestamp() - phaseStartTicks);
            _perf.NavigationPathInitializePolicyConstructTicks += Stopwatch.GetTimestamp() - phaseStartTicks;
        }
        _perf.NavigationPathPolicyCreates++;
        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        SetSectorCorridorPolicy(job.PolicyKey, job.Policy);
        if (profile)
        {
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationPolicyInitializeAuthority, Stopwatch.GetTimestamp() - phaseStartTicks);
            _perf.NavigationPathInitializePolicyAuthorityTicks += Stopwatch.GetTimestamp() - phaseStartTicks;
        }
        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (job.Key.MovingTargetId != int.MinValue)
        {
            var anchorKey = new MovingTargetAnchorKey(
                job.Key.MovingTargetId,
                job.Key.AgentTypeId,
                job.Key.SourceIslandId);
            PinMovingTargetSectorCorridorPolicy(MovingTargetAnchors[anchorKey], job.PolicyKey);
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationPolicyInitializePin, Stopwatch.GetTimestamp() - phaseStartTicks);
            _perf.NavigationPathInitializePolicyPinTicks += Stopwatch.GetTimestamp() - phaseStartTicks;
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
        FlowPathKernelSearchState kernelState = new FlowPathKernelSearchState(
            Math.Max(16, checked(world.Portals.Length * 2)));
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
            KernelState = kernelState
        };
        _perf.NavigationPathRestrictedSearchCreates++;
        return search;
    }

    private static int ExecuteNavigationPathSearchCommandSlice(
        FlowPathKernelSearchState searchState,
        out FlowPathKernelPopStatus popStatus,
        out FlowPathKernelSearchEntry popEntry)
    {
        int operations = searchState.ExecuteCommandSlice(out popStatus, out popEntry);
        if (operations <= 0)
            throw new InvalidOperationException("Navigation path search command slice executed no operations.");
        RecordNavigationPathGraphSlice(operations);
        return operations;
    }

    private static void RecordNavigationPathGraphSlice(int operations)
    {
        if (operations <= 0)
            throw new ArgumentOutOfRangeException(nameof(operations));
        _perf.NavigationPathSearchCommandSlices = checked(_perf.NavigationPathSearchCommandSlices + 1);
        _perf.NavigationPathSearchCommandOperations = checked(
            _perf.NavigationPathSearchCommandOperations + operations);
    }

    private static FlowPathKernelGraphIndex ResolveFlowPathKernelSearchGraphIndex(
        NavigationWorld world,
        PortalHierarchyLevel level)
    {
        if (world == null)
            throw new InvalidOperationException("Navigation graph slice has no world.");
        if (level == null)
        {
            if (world.L0SearchGraphIndex == null || !world.L0SearchGraphIndex.IsCreated)
                throw new InvalidOperationException("Navigation graph slice has no committed L0 graph index.");
            return world.L0SearchGraphIndex;
        }
        int index = level.Level - 1;
        FlowPathKernelGraphIndex[] indexes = world.Hierarchy?.SearchGraphIndexes;
        if ((uint)index >= (uint)(indexes?.Length ?? 0)
            || !ReferenceEquals(world.Hierarchy.Levels[index], level)
            || indexes[index] == null
            || !indexes[index].IsCreated)
        {
            throw new InvalidOperationException($"Navigation graph slice has no committed hierarchy index level={level.Level}.");
        }
        return indexes[index];
    }

    private static int AdvanceIncrementalRestrictedPortalSearchSlice(
        IncrementalRestrictedPortalSearch search,
        int operationCapacity,
        out bool complete)
    {
        if (search == null)
            throw new ArgumentNullException(nameof(search));
        if (operationCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(operationCapacity));

        int operationCount = 0;
        complete = false;
        while (operationCount < operationCapacity)
        {
            switch (search.Stage)
            {
                case IncrementalRestrictedSearchStage.InitializeTargets:
                    if (search.TargetInitializationCursor < search.TargetNodeSequence.Count)
                    {
                        int target = search.TargetNodeSequence[search.TargetInitializationCursor++];
                        if (!search.TargetNodes.Add(target))
                            throw new InvalidOperationException("Incremental restricted search contains a duplicate target.");
                    }
                    else
                    {
                        search.Stage = IncrementalRestrictedSearchStage.InitializeSources;
                    }
                    operationCount++;
                    continue;

                case IncrementalRestrictedSearchStage.InitializeSources:
                    if (search.SourceInitializationCursor < search.SourceNodes.Count)
                    {
                        search.KernelState.BeginCommandSlice();
                        do
                        {
                            int index = search.SourceInitializationCursor++;
                            int node = search.SourceNodes[index];
                            DecodePortalNode(node, out int sectorId, out _);
                            if (!IsIncrementalRestrictedSearchSectorAllowed(search, sectorId))
                            {
                                throw new InvalidOperationException(
                                    "Incremental restricted search source is outside its containing cluster.");
                            }
                            search.KernelState.AppendAddSource(node, search.SourceCosts[index]);
                            operationCount++;
                        }
                        while (operationCount < operationCapacity
                               && search.SourceInitializationCursor < search.SourceNodes.Count);
                        int executed = ExecuteNavigationPathSearchCommandSlice(search.KernelState, out _, out _);
                        if (executed <= 0)
                            throw new InvalidOperationException("Incremental restricted search source slice executed no commands.");
                        continue;
                    }
                    if (search.KernelState.OpenCount == 0)
                        throw new InvalidOperationException("Incremental restricted search has no reachable source.");
                    search.KernelState.SetGraphSliceTargets(search.TargetNodeSequence);
                    search.Stage = IncrementalRestrictedSearchStage.Pop;
                    operationCount++;
                    continue;

                case IncrementalRestrictedSearchStage.Pop:
                case IncrementalRestrictedSearchStage.Crossing:
                case IncrementalRestrictedSearchStage.Edges:
                {
                    FlowPathKernelGraphIndex graph = ResolveFlowPathKernelSearchGraphIndex(
                        search.World,
                        search.LowerLevel);
                    FlowPathKernelGraphCursor cursor = new FlowPathKernelGraphCursor
                    {
                        Stage = search.Stage == IncrementalRestrictedSearchStage.Pop ? 0 : 1,
                        CurrentNode = search.CurrentNode,
                        CurrentCost = search.CurrentCost,
                        EdgeCursor = search.EdgeCursor
                    };
                    PortalHierarchyCluster cluster = search.ContainingCluster;
                    FlowPathKernelGraphSliceResult slice;
                    if (search.KernelState.HasPendingGraphSlice)
                    {
                        if (!search.KernelState.IsPendingGraphSliceCompleted)
                            return 1;
                        slice = search.KernelState.CompleteScheduledGraphSlice();
                    }
                    else
                    {
                        search.KernelState.ScheduleGraphSlice(
                            graph,
                            search.Reverse,
                            search.World.SectorCountX,
                            cluster?.StartSectorX ?? 0,
                            cluster?.StartSectorY ?? 0,
                            cluster?.WidthSectors ?? 0,
                            cluster?.HeightSectors ?? 0,
                            cursor,
                            operationCapacity - operationCount);
                        return operationCapacity;
                    }
                    if (slice.OperationCount <= 0
                        && slice.StopReason != FlowPathKernelGraphSliceStopReason.TargetSettled)
                        throw new InvalidOperationException("Incremental restricted graph slice consumed no operations.");
                        RecordNavigationPathGraphSlice(slice.OperationCount);
                        search.CurrentNode = slice.Cursor.CurrentNode;
                    search.CurrentCost = slice.Cursor.CurrentCost;
                    search.EdgeCursor = slice.Cursor.EdgeCursor;
                    search.Stage = slice.Cursor.Stage == 0
                        ? IncrementalRestrictedSearchStage.Pop
                        : IncrementalRestrictedSearchStage.Edges;
                    operationCount++;
                    search.RemainingTargets = slice.RemainingTargets;
                        if (slice.StopReason == FlowPathKernelGraphSliceStopReason.TargetSettled
                        || slice.StopReason == FlowPathKernelGraphSliceStopReason.FrontierEmpty)
                    {
                        CompleteIncrementalRestrictedPortalSearch(search);
                        search.Stage = IncrementalRestrictedSearchStage.Complete;
                            complete = true;
                        }
                        return operationCount;
                }

                case IncrementalRestrictedSearchStage.Complete:
                    operationCount++;
                    complete = true;
                    return operationCount;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(search.Stage),
                        search.Stage,
                        "Unknown incremental restricted search stage.");
            }
        }
        return operationCount;
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

    private static int AdvanceNavigationGoalConnector(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        int operationCapacity)
    {
        bool profileGoalConnector = MainThreadFrameProfiler.LoggingEnabled;
        long goalConnectorStartTicks = profileGoalConnector ? Stopwatch.GetTimestamp() : 0L;
        long goalConnectorChildTicks = 0L;
        try
        {
            if (job == null || source == null || source.Demand == null)
                throw new InvalidOperationException("Navigation goal connector advance received an incomplete request source.");
            if (source.HierarchyLevelArrayIndex < 0)
                throw new InvalidOperationException("Navigation goal connector advance requires a resolved hierarchy level.");

        int sharedDepth = source.HierarchyLevelArrayIndex;
        if (job.SharedGoalConnectors.TryGetValue(sharedDepth, out PortalHierarchyConnector sharedConnector))
        {
            if (sharedConnector == null)
                throw new InvalidOperationException(
                    $"Navigation request shared goal connector is invalid depth={sharedDepth} target={job.Key.MovingTargetId}.");
            sharedConnector.RequireSingleAuthority();
            source.GoalConnector = sharedConnector;
            source.NextGoalConnectorLevel = sharedDepth + 1;
            _perf.NavigationPathSharedGoalConnectorHits++;
            return 1;
        }

        bool isBuilder = job.SharedGoalConnectorBuilderSourceIds.TryGetValue(
                             sharedDepth,
                             out int builderSourceId)
                         && builderSourceId == source.Demand.SourceId;
        if (job.SharedGoalConnectorBuildLevels.Contains(sharedDepth) && !isBuilder)
        {
            // The first deterministic source owns construction.  Other
            // sources remain pending until that target-side authority is
            // complete; they must not create a duplicate search.
            return 1;
        }

        if (!job.SharedGoalConnectorBuildLevels.Add(sharedDepth))
        {
            if (!job.SharedGoalConnectorBuilderSourceIds.TryGetValue(
                    sharedDepth,
                    out int existingBuilderSourceId)
                || existingBuilderSourceId != source.Demand.SourceId)
                return 1;
        }
        else
        {
            job.SharedGoalConnectorBuilderSourceIds[sharedDepth] = source.Demand.SourceId;
            _perf.SectorCorridorGoalConnectorBuilds++;
            _perf.NavigationPathSharedGoalConnectorBuilds++;
        }

        PortalHierarchy hierarchy = _world.Hierarchy
            ?? throw new InvalidOperationException("Navigation path request requires a committed hierarchy.");
        if (source.NextGoalConnectorLevel > source.HierarchyLevelArrayIndex)
        {
            if (source.GoalConnector == null)
                throw new InvalidOperationException("Navigation goal connector reached completion without an authority.");
            if (!job.SharedGoalConnectors.ContainsKey(sharedDepth))
            {
                for (PortalHierarchyConnector cursor = source.GoalConnector;
                     cursor != null;
                     cursor = cursor.Child)
                {
                    cursor.IsRequestShared = true;
                }
                job.SharedGoalConnectors.Add(sharedDepth, source.GoalConnector);
                job.SharedGoalConnectorBuildLevels.Remove(sharedDepth);
                job.SharedGoalConnectorBuilderSourceIds.Remove(sharedDepth);
            }
            source.Stage = NavigationPathSourceStage.CreateHierarchyPolicy;
            return 1;
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
                    return 1;
                }
                int collectionOperations = 0;
                while (collectionOperations < operationCapacity
                       && source.RestrictedSourceCollectionCursor < goalSector.PortalIds.Count)
                {
                    int portalId = goalSector.PortalIds[source.RestrictedSourceCollectionCursor++];
                    bool profileGoalInput = MainThreadFrameProfiler.LoggingEnabled;
                    long goalInputStartTicks = profileGoalInput ? Stopwatch.GetTimestamp() : 0L;
                    long cost = ResolveDeterministicGoalSectorPortalAccessCost(
                        goalSector,
                        job.Key.GoalSectorId,
                        portalId,
                        job.Policy.GoalCellIndex % _world.Width,
                        job.Policy.GoalCellIndex / _world.Width);
                    if (profileGoalInput)
                    {
                        long goalInputElapsedTicks = Stopwatch.GetTimestamp() - goalInputStartTicks;
                        MainThreadFrameProfiler.Record(
                            MainThreadPerfScope.FlowNavigationGoalConnectorInputCollection,
                            goalInputElapsedTicks);
                        goalConnectorChildTicks += goalInputElapsedTicks;
                    }
                    if (cost != long.MaxValue)
                    {
                        source.RestrictedSourceNodes.Add(EncodePortalNode(job.Key.GoalSectorId, portalId));
                        source.RestrictedSourceCosts.Add(cost);
                    }
                    collectionOperations++;
                }
                while (collectionOperations < operationCapacity
                       && source.RestrictedSourceCollectionCursor >= goalSector.PortalIds.Count
                       && source.RestrictedTargetCollectionCursor < cluster.BoundaryNodes.Length)
                {
                    source.RestrictedTargetNodes.Add(
                        cluster.BoundaryNodes[source.RestrictedTargetCollectionCursor++]);
                    collectionOperations++;
                }
                if (source.RestrictedSourceCollectionCursor < goalSector.PortalIds.Count
                    || source.RestrictedTargetCollectionCursor < cluster.BoundaryNodes.Length)
                {
                    return collectionOperations;
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
                return Math.Max(1, collectionOperations);
            }
            bool profileGoalSearch = MainThreadFrameProfiler.LoggingEnabled;
            long goalSearchStartTicks = profileGoalSearch ? Stopwatch.GetTimestamp() : 0L;
            int operationCount = AdvanceIncrementalRestrictedPortalSearchSlice(
                source.RestrictedSearch,
                operationCapacity,
                out bool complete);
            if (profileGoalSearch)
            {
                long goalSearchElapsedTicks = Stopwatch.GetTimestamp() - goalSearchStartTicks;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationGoalConnectorSearchSlice,
                    goalSearchElapsedTicks);
                goalConnectorChildTicks += goalSearchElapsedTicks;
            }
            if (!complete)
                return operationCount;
            source.GoalConnector = new PortalHierarchyConnector
            {
                TargetLevelIndex = 0,
                SearchState = source.RestrictedSearch.KernelState
            };
            source.RestrictedSearch.KernelState = null;
            source.RestrictedSearch = null;
            source.NextGoalConnectorLevel = 1;
            return operationCount;
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
                return 1;
            }
            int collectionOperations = 0;
            while (collectionOperations < operationCapacity
                   && source.RestrictedSourceCollectionCursor < childCluster.BoundaryNodes.Length)
            {
                int node = childCluster.BoundaryNodes[source.RestrictedSourceCollectionCursor++];
                if (source.GoalConnector.TryGetCost(node, out long cost))
                {
                    if (!source.GoalConnector.ContainsSettled(node))
                        throw new InvalidOperationException("Navigation child goal connector exposed an unsettled boundary.");
                    source.RestrictedSourceNodes.Add(node);
                    source.RestrictedSourceCosts.Add(cost);
                }
                collectionOperations++;
            }
            while (collectionOperations < operationCapacity
                   && source.RestrictedSourceCollectionCursor >= childCluster.BoundaryNodes.Length
                   && source.RestrictedTargetCollectionCursor < targetCluster.BoundaryNodes.Length)
            {
                source.RestrictedTargetNodes.Add(
                    targetCluster.BoundaryNodes[source.RestrictedTargetCollectionCursor++]);
                collectionOperations++;
            }
            if (source.RestrictedSourceCollectionCursor < childCluster.BoundaryNodes.Length
                || source.RestrictedTargetCollectionCursor < targetCluster.BoundaryNodes.Length)
            {
                return collectionOperations;
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
            return Math.Max(1, collectionOperations);
        }
        bool profileUpperSearch = MainThreadFrameProfiler.LoggingEnabled;
        long upperSearchStartTicks = profileUpperSearch ? Stopwatch.GetTimestamp() : 0L;
        int upperOperationCount = AdvanceIncrementalRestrictedPortalSearchSlice(
            source.RestrictedSearch,
            operationCapacity,
            out bool upperComplete);
        if (profileUpperSearch)
        {
            long upperSearchElapsedTicks = Stopwatch.GetTimestamp() - upperSearchStartTicks;
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationGoalConnectorSearchSlice,
                upperSearchElapsedTicks);
            goalConnectorChildTicks += upperSearchElapsedTicks;
        }
        if (!upperComplete)
            return upperOperationCount;
        source.GoalConnector = new PortalHierarchyConnector
        {
            TargetLevelIndex = source.NextGoalConnectorLevel,
            SearchState = source.RestrictedSearch.KernelState,
            Child = source.GoalConnector
        };
        source.RestrictedSearch.KernelState = null;
        source.RestrictedSearch = null;
        source.NextGoalConnectorLevel++;
        return upperOperationCount;
        }
        finally
        {
            if (profileGoalConnector)
            {
                long residualTicks = Stopwatch.GetTimestamp() - goalConnectorStartTicks - goalConnectorChildTicks;
                if (residualTicks < 0L)
                    throw new InvalidOperationException($"Navigation goal connector exclusive timing is inconsistent. residualTicks={residualTicks}.");
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationGoalConnectorUnattributed,
                    residualTicks);
            }
        }
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

    private static int CreateNavigationHierarchyPolicy(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        ref bool policyMutationStarted,
        int operationCapacity)
    {
        if (operationCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(operationCapacity));
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
            return 1;
        }

        if (source.HierarchyPolicyBoundaryCursor < goalCluster.BoundaryNodes.Length)
        {
            FlowPathKernelSearchState searchState = source.HierarchyPolicy.SearchState;
            searchState.BeginCommandSlice();
            int operationCount = 0;
            int commandCount = 0;
            while (operationCount < operationCapacity
                   && source.HierarchyPolicyBoundaryCursor < goalCluster.BoundaryNodes.Length)
            {
                int node = goalCluster.BoundaryNodes[source.HierarchyPolicyBoundaryCursor++];
                if (source.GoalConnector.TryGetCost(node, out long cost))
                {
                    if (!source.GoalConnector.ContainsSettled(node))
                    {
                        throw new InvalidOperationException(
                            "Navigation goal connector exposed an unsettled boundary.");
                    }
                    searchState.AppendAddSource(node, cost);
                    commandCount++;
                }
                operationCount++;
            }
            if (commandCount > 0)
            {
                int executed = ExecuteNavigationPathSearchCommandSlice(searchState, out _, out _);
                if (executed != commandCount)
                {
                    throw new InvalidOperationException(
                        $"Navigation hierarchy policy source count mismatch. queued={commandCount}, executed={executed}.");
                }
            }
            return operationCount;
        }

        if (source.HierarchyPolicy.SearchState.OpenCount == 0)
            throw new InvalidOperationException("Navigation hierarchy policy has no goal boundary source.");
        BeginHashedPortalHierarchyPolicyMutation(job.Policy, ref policyMutationStarted);
        job.Policy.HierarchyPolicies.Add(source.HierarchyLevelArrayIndex, source.HierarchyPolicy);
        for (PortalHierarchyConnector cursor = source.GoalConnector;
             cursor != null;
             cursor = cursor.Child)
        {
            cursor.IsRequestShared = false;
        }
        source.NextDownwardLevel = source.HierarchyLevelArrayIndex;
        source.Stage = NavigationPathSourceStage.ExpandHierarchyPolicy;
        return 1;
    }

    private static int AdvanceNavigationHierarchyPolicy(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        ref bool policyMutationStarted,
        int operationCapacity)
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
            return 1;
        }

        int operationCount = AdvanceReversePolicyExpansionSlice(
            job,
            source,
            ref policyMutationStarted,
            operationCapacity);
        if (source.ReversePolicyExpansion.Stage == IncrementalReversePolicyExpansionStage.Complete)
        {
            source.ReversePolicyExpansion = null;
            source.Stage = NavigationPathSourceStage.BuildDownwardCustomization;
        }
        return operationCount;
    }

    private static int AdvanceNavigationDownwardCustomization(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        ref bool policyMutationStarted,
        int operationCapacity)
    {
        if (source.NextDownwardLevel < 0)
        {
            source.Stage = NavigationPathSourceStage.MaterializeRoute;
            return 1;
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
            return 1;
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
                return 1;
            }
            if (source.RestrictedSourceCollectionCursor < containingCluster.BoundaryNodes.Length)
            {
                int node = containingCluster.BoundaryNodes[source.RestrictedSourceCollectionCursor++];
                bool settled = usePolicySources
                    ? source.HierarchyPolicy.SearchState.ContainsSettled(node)
                    : previousCustomization != null && previousCustomization.ContainsSettled(node);
                if (!settled)
                    return 1;
                bool hasCost = usePolicySources
                    ? source.HierarchyPolicy.SearchState.TryGetCost(node, out long cost)
                    : previousCustomization.TryGetCost(node, out cost);
                if (!hasCost)
                    throw new InvalidOperationException("Navigation downward customization settled source has no cost.");
                source.RestrictedSourceNodes.Add(node);
                source.RestrictedSourceCosts.Add(cost);
                return 1;
            }

            if (levelIndex == 0)
            {
                List<int> portals = _world.Sectors[source.Demand.StartSectorId].PortalIds;
                if (source.RestrictedTargetCollectionCursor < portals.Count)
                {
                    int portalId = portals[source.RestrictedTargetCollectionCursor++];
                    source.RestrictedTargetNodes.Add(
                        EncodePortalNode(source.Demand.StartSectorId, portalId));
                    return 1;
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
                    return 1;
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
            return 1;
        }
        int operationCount = AdvanceIncrementalRestrictedPortalSearchSlice(
            source.RestrictedSearch,
            operationCapacity,
            out bool complete);
        if (!complete)
            return operationCount;
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
        return operationCount;
    }

    private static int AdvanceNavigationL0Policy(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        ref bool policyMutationStarted,
        int operationCapacity)
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
            return 1;
        }

        int operationCount = AdvanceReversePolicyExpansionSlice(
            job,
            source,
            ref policyMutationStarted,
            operationCapacity);
        if (source.ReversePolicyExpansion.Stage == IncrementalReversePolicyExpansionStage.Complete)
        {
            source.ReversePolicyExpansion = null;
            source.Stage = NavigationPathSourceStage.MaterializeRoute;
        }
        return operationCount;
    }

    private static int AdvanceReversePolicyExpansionSlice(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        ref bool policyMutationStarted,
        int operationCapacity)
    {
        IncrementalReversePolicyExpansion expansion = source.ReversePolicyExpansion
            ?? throw new InvalidOperationException("Navigation reverse policy expansion state is missing.");
        if (operationCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(operationCapacity));

        int operationCount = 0;
        while (operationCount < operationCapacity)
        {
            switch (expansion.Stage)
            {
                case IncrementalReversePolicyExpansionStage.InitializeGoal:
                {
                    SectorData goalSector = _world.Sectors[job.Key.GoalSectorId];
                    FlowPathKernelSearchState searchState = job.Policy.SearchState;
                    searchState.BeginCommandSlice();
                    int commandCount = 0;
                    while (operationCount < operationCapacity
                           && expansion.GoalPortalCursor < goalSector.PortalIds.Count)
                    {
                        int portalId = goalSector.PortalIds[expansion.GoalPortalCursor++];
                        long goalCost = ResolveDeterministicGoalSectorPortalAccessCost(
                            goalSector,
                            job.Key.GoalSectorId,
                            portalId,
                            job.Policy.GoalCellIndex % _world.Width,
                            job.Policy.GoalCellIndex / _world.Width);
                        if (goalCost != long.MaxValue)
                        {
                            searchState.AppendAddSource(
                                EncodePortalNode(job.Key.GoalSectorId, portalId),
                                goalCost);
                            commandCount++;
                        }
                        operationCount++;
                    }
                    if (commandCount > 0)
                    {
                        BeginHashedPortalHierarchyPolicyMutation(job.Policy, ref policyMutationStarted);
                        long commandStartTicks = MainThreadFrameProfiler.LoggingEnabled
                            ? Stopwatch.GetTimestamp()
                            : 0L;
                        int executed = ExecuteNavigationPathSearchCommandSlice(searchState, out _, out _);
                        if (MainThreadFrameProfiler.LoggingEnabled)
                        {
                            MainThreadFrameProfiler.Record(
                                MainThreadPerfScope.FlowNavigationPathSearchCommandExecute,
                                Stopwatch.GetTimestamp() - commandStartTicks);
                        }
                        if (executed != commandCount)
                        {
                            throw new InvalidOperationException(
                                $"Navigation goal source command count mismatch. queued={commandCount}, executed={executed}.");
                        }
                    }
                    if (operationCount >= operationCapacity)
                        continue;
                    if (searchState.OpenCount == 0)
                    {
                        throw new InvalidOperationException(
                            $"Navigation L0 policy goal sector has no reachable portal sector={job.Key.GoalSectorId}.");
                    }
                    expansion.Stage = IncrementalReversePolicyExpansionStage.CollectTargets;
                    operationCount++;
                    continue;
                }

                case IncrementalReversePolicyExpansionStage.CollectTargets:
                    while (operationCount < operationCapacity)
                    {
                        bool hasNext;
                        if (expansion.Hierarchy)
                        {
                            int[] boundaries = expansion.HierarchyTargetCluster.BoundaryNodes;
                            hasNext = expansion.TargetCursor < boundaries.Length;
                            if (hasNext)
                            {
                                int node = boundaries[expansion.TargetCursor++];
                                expansion.HasAccessibleTarget = true;
                                if (!source.HierarchyPolicy.SearchState.ContainsSettled(node))
                                    expansion.PendingTargetNodes.Add(node);
                            }
                        }
                        else
                        {
                            List<int> portals = expansion.L0TargetSector.PortalIds;
                            hasNext = expansion.TargetCursor < portals.Count;
                            if (hasNext)
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
                                    FlowPathKernelSearchState policySearch = expansion.Hierarchy
                                        ? source.HierarchyPolicy.SearchState
                                        : job.Policy.SearchState;
                                    if (!policySearch.ContainsSettled(node))
                                        expansion.PendingTargetNodes.Add(node);
                                }
                            }
                        }
                        if (hasNext)
                        {
                            operationCount++;
                            continue;
                        }
                        if (!expansion.HasAccessibleTarget)
                        {
                            throw new InvalidOperationException(
                                "Navigation reverse policy source has no accessible portal target.");
                        }
                        expansion.Stage = expansion.PendingTargetNodes.Count == 0
                            ? IncrementalReversePolicyExpansionStage.Complete
                            : IncrementalReversePolicyExpansionStage.Pop;
                        if (expansion.Stage == IncrementalReversePolicyExpansionStage.Pop)
                        {
                            FlowPathKernelSearchState targetSearch = expansion.Hierarchy
                                ? source.HierarchyPolicy.SearchState
                                : job.Policy.SearchState;
                            ConfigureReversePolicyGraphTargets(expansion, targetSearch);
                        }
                        operationCount++;
                        break;
                    }
                    continue;

                case IncrementalReversePolicyExpansionStage.Pop:
                case IncrementalReversePolicyExpansionStage.Edges:
                case IncrementalReversePolicyExpansionStage.Crossing:
                {
                    FlowPathKernelSearchState searchState = expansion.Hierarchy
                        ? source.HierarchyPolicy.SearchState
                        : job.Policy.SearchState;
                    FlowPathKernelGraphSliceResult slice;
                    if (searchState.HasPendingGraphSlice)
                    {
                        if (!searchState.IsPendingGraphSliceCompleted)
                            return 1;
                        long completeStartTicks = MainThreadFrameProfiler.LoggingEnabled
                            ? Stopwatch.GetTimestamp()
                            : 0L;
                        slice = searchState.CompleteScheduledGraphSlice();
                        if (MainThreadFrameProfiler.LoggingEnabled)
                        {
                            MainThreadFrameProfiler.Record(
                                MainThreadPerfScope.FlowNavigationPathSearchComplete,
                                Stopwatch.GetTimestamp() - completeStartTicks);
                        }
                    }
                    else
                    {
                        bool hasUnsettledTarget = false;
                        foreach (int targetNode in expansion.PendingTargetNodes)
                        {
                            if (!searchState.ContainsSettled(targetNode))
                            {
                                hasUnsettledTarget = true;
                                break;
                            }
                        }
                        if (!hasUnsettledTarget)
                        {
                            expansion.PendingTargetNodes.Clear();
                            expansion.Stage = IncrementalReversePolicyExpansionStage.Complete;
                            return operationCount == 0 ? 1 : operationCount;
                        }
                        BeginHashedPortalHierarchyPolicyMutation(job.Policy, ref policyMutationStarted);
                        FlowPathKernelGraphIndex graph = ResolveFlowPathKernelSearchGraphIndex(
                            _world,
                            expansion.Hierarchy ? expansion.HierarchyLevel : null);
                        FlowPathKernelGraphCursor cursor = new FlowPathKernelGraphCursor
                        {
                            Stage = expansion.Stage == IncrementalReversePolicyExpansionStage.Pop ? 0 : 1,
                            CurrentNode = expansion.CurrentNode,
                            CurrentCost = expansion.CurrentCost,
                            EdgeCursor = expansion.EdgeCursor
                        };
                        long scheduleStartTicks = MainThreadFrameProfiler.LoggingEnabled
                            ? Stopwatch.GetTimestamp()
                            : 0L;
                        searchState.ScheduleGraphSlice(
                            graph,
                            reverse: true,
                            _world.SectorCountX,
                            0,
                            0,
                            0,
                            0,
                            cursor,
                            operationCapacity - operationCount);
                        if (MainThreadFrameProfiler.LoggingEnabled)
                        {
                            MainThreadFrameProfiler.Record(
                                MainThreadPerfScope.FlowNavigationPathSearchSchedule,
                                Stopwatch.GetTimestamp() - scheduleStartTicks);
                        }
                        return operationCapacity;
                    }
                    if (slice.OperationCount <= 0
                        && slice.StopReason != FlowPathKernelGraphSliceStopReason.TargetSettled)
                        throw new InvalidOperationException("Navigation reverse policy graph slice consumed no operations.");
                    RecordNavigationPathGraphSlice(slice.OperationCount);
                    expansion.CurrentNode = slice.Cursor.CurrentNode;
                    expansion.CurrentCost = slice.Cursor.CurrentCost;
                    expansion.EdgeCursor = slice.Cursor.EdgeCursor;
                    expansion.Stage = slice.Cursor.Stage == 0
                        ? IncrementalReversePolicyExpansionStage.Pop
                        : IncrementalReversePolicyExpansionStage.Edges;
                    operationCount++;
                    if (slice.StopReason == FlowPathKernelGraphSliceStopReason.TargetSettled)
                    {
                        expansion.PendingTargetNodes.Clear();
                        expansion.Stage = IncrementalReversePolicyExpansionStage.Complete;
                    }
                    else if (slice.StopReason == FlowPathKernelGraphSliceStopReason.FrontierEmpty)
                    {
                        throw new InvalidOperationException(
                            "Navigation reverse policy graph exhausted before reaching every source boundary.");
                    }
                    return operationCount;
                }

                case IncrementalReversePolicyExpansionStage.Complete:
                    if (operationCount == 0)
                        operationCount = 1;
                    return operationCount;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(expansion.Stage),
                        expansion.Stage,
                        "Unknown reverse policy expansion stage.");
            }
        }
        return operationCount;
    }

    private static void ConfigureReversePolicyGraphTargets(
        IncrementalReversePolicyExpansion expansion,
        FlowPathKernelSearchState searchState)
    {
        expansion.GraphTargetScratch.Clear();
        foreach (int node in expansion.PendingTargetNodes)
            expansion.GraphTargetScratch.Add(node);
        expansion.GraphTargetScratch.Sort();
        searchState.SetGraphSliceTargets(expansion.GraphTargetScratch);
        expansion.GraphTargetScratch.Clear();
    }

    private static int AdvanceNavigationPathSourceMaterialization(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        ref bool policyMutationStarted,
        int operationCapacity)
    {
        if (operationCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(operationCapacity));
        if (source.Materialization == null)
        {
            source.Materialization = new IncrementalRouteMaterialization
            {
                Stage = IncrementalRouteMaterializationStage.Initialize
            };
            _perf.NavigationPathMaterializationCreates++;
        }
        IncrementalRouteMaterialization state = source.Materialization;
        bool profileMerge = MainThreadFrameProfiler.LoggingEnabled;
        long mergeStartTicks = profileMerge ? Stopwatch.GetTimestamp() : 0L;
        TryMergeNavigationRouteIntoSharedSuffix(job, source, state);
        if (profileMerge)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationPathMaterializeSharedSuffixMerge,
                Stopwatch.GetTimestamp() - mergeStartTicks);
        }
        IncrementalRouteMaterializationStage operationStage = state.Stage;
        bool recordTiming = MainThreadFrameProfiler.LoggingEnabled;
        long startTicks = recordTiming ? Stopwatch.GetTimestamp() : 0L;
        int operationCount;
        try
        {
            switch (operationStage)
            {
                case IncrementalRouteMaterializationStage.Initialize:
                    InitializeNavigationRouteMaterialization(job, source, state);
                    operationCount = 1;
                    break;
                case IncrementalRouteMaterializationStage.SelectStartPortal:
                    AdvanceNavigationRouteStartPortalSelection(job, source, state);
                    operationCount = 1;
                    break;
                case IncrementalRouteMaterializationStage.ExpandDownward:
                    operationCount = AdvanceNavigationRouteDownwardExpansion(job, source, state, operationCapacity);
                    break;
                case IncrementalRouteMaterializationStage.ExpandPolicy:
                    operationCount = AdvanceNavigationRoutePolicyExpansion(job, source, state, operationCapacity);
                    break;
                case IncrementalRouteMaterializationStage.ExpandGoalConnector:
                    operationCount = AdvanceNavigationRouteGoalConnectorExpansion(job, source, state, operationCapacity);
                    break;
                case IncrementalRouteMaterializationStage.ConvertRoute:
                    operationCount = AdvanceNavigationRouteConversion(job, source, state, operationCapacity);
                    break;
                case IncrementalRouteMaterializationStage.PrepareImmutableRoute:
                    AdvanceNavigationImmutableRoutePreparation(source, state);
                    operationCount = 1;
                    break;
                case IncrementalRouteMaterializationStage.HashRoute:
                    AdvanceNavigationRouteAuthorityHash(source, state);
                    operationCount = 1;
                    break;
                case IncrementalRouteMaterializationStage.Publish:
                    PublishNavigationRouteMaterialization(job, source, state, ref policyMutationStarted);
                    operationCount = 1;
                    break;
                case IncrementalRouteMaterializationStage.Complete:
                    CompleteNavigationLocalBinding(job, source);
                    operationCount = 1;
                    break;
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
        RecordNavigationPathMaterializationOperation(operationStage, operationCount);
        return operationCount;
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
        if (state.RouteState.HasPendingSlice)
            return;
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
        {
            if (job.SharedRouteMergeIndex.Contains(mergeNode))
            {
                throw new InvalidOperationException(
                    $"Navigation shared route merge index has no authoritative suffix node={FormatRouteNode(mergeNode)}.");
            }
            return;
        }
        if (!job.SharedRouteMergeIndex.Contains(mergeNode))
        {
            throw new InvalidOperationException(
                $"Navigation shared route suffix has no kernel merge index node={FormatRouteNode(mergeNode)}.");
        }
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
        IncrementalRouteMaterializationStage stage,
        int operationCount)
    {
        if (operationCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(operationCount));
        switch (stage)
        {
            case IncrementalRouteMaterializationStage.Initialize:
                _perf.NavigationPathMaterializeInitializeOperations = checked(_perf.NavigationPathMaterializeInitializeOperations + operationCount);
                return;
            case IncrementalRouteMaterializationStage.SelectStartPortal:
                _perf.NavigationPathMaterializeStartPortalOperations = checked(_perf.NavigationPathMaterializeStartPortalOperations + operationCount);
                return;
            case IncrementalRouteMaterializationStage.ExpandDownward:
                _perf.NavigationPathMaterializeDownwardOperations = checked(_perf.NavigationPathMaterializeDownwardOperations + operationCount);
                return;
            case IncrementalRouteMaterializationStage.ExpandPolicy:
                _perf.NavigationPathMaterializePolicyOperations = checked(_perf.NavigationPathMaterializePolicyOperations + operationCount);
                return;
            case IncrementalRouteMaterializationStage.ExpandGoalConnector:
                _perf.NavigationPathMaterializeGoalConnectorOperations = checked(_perf.NavigationPathMaterializeGoalConnectorOperations + operationCount);
                return;
            case IncrementalRouteMaterializationStage.ConvertRoute:
                _perf.NavigationPathMaterializeConversionOperations = checked(_perf.NavigationPathMaterializeConversionOperations + operationCount);
                return;
            case IncrementalRouteMaterializationStage.PrepareImmutableRoute:
                _perf.NavigationPathMaterializeImmutableCopyOperations = checked(_perf.NavigationPathMaterializeImmutableCopyOperations + operationCount);
                return;
            case IncrementalRouteMaterializationStage.HashRoute:
                _perf.NavigationPathMaterializeHashOperations = checked(_perf.NavigationPathMaterializeHashOperations + operationCount);
                return;
            case IncrementalRouteMaterializationStage.Publish:
                _perf.NavigationPathMaterializePublishOperations = checked(_perf.NavigationPathMaterializePublishOperations + operationCount);
                return;
            case IncrementalRouteMaterializationStage.Complete:
                _perf.NavigationPathLocalBindingOperations = checked(_perf.NavigationPathLocalBindingOperations + operationCount);
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

        // A moving-target request is a group authority. Once one source in a
        // start sector has materialized a valid corridor, later sources in
        // that same sector only need an exact local access check. Re-expanding
        // the same high-level route per unit is the performance divergence
        // this request group is designed to eliminate.
        if (TryBindNavigationRouteFromSharedStartSector(job, source, state))
            return;
        state.Stage = IncrementalRouteMaterializationStage.SelectStartPortal;
    }

    private static bool TryBindNavigationRouteFromSharedStartSector(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state)
    {
        if (job == null || source?.Demand == null || state == null)
            throw new InvalidOperationException("Shared start-sector route binding received incomplete state.");
        if (job.SharedRouteSuffixes.Count == 0)
            return false;

        int exactBestStartNode = ResolveExactStartPortalNodeForSharedRoute(job, source);
        if (exactBestStartNode == int.MinValue)
            return false;

        // Dictionary iteration is not an authority order. Select the lowest
        // accessible route node so the group binding is deterministic.
        int selectedNode = int.MaxValue;
        NavigationSharedRouteSuffix selected = null;
        foreach (KeyValuePair<int, NavigationSharedRouteSuffix> pair in job.SharedRouteSuffixes)
        {
            NavigationSharedRouteSuffix suffix = pair.Value
                ?? throw new InvalidOperationException("Shared route suffix index contains a null suffix.");
            DecodePortalNode(suffix.MergeNode, out int sectorId, out int portalId);
            if (sectorId != source.Demand.StartSectorId
                || suffix.PortalStartIndex < 0
                || suffix.PortalStartIndex >= suffix.PortalIds.Length
                || suffix.SectorStartIndex < 0
                || suffix.SectorStartIndex >= suffix.SectorIds.Length
                || suffix.SectorIds[suffix.SectorStartIndex] != source.Demand.StartSectorId
                || pair.Key != suffix.MergeNode)
            {
                continue;
            }

            int firstPortalId = suffix.PortalIds[suffix.PortalStartIndex];
            int firstPortalNode = EncodePortalNode(source.Demand.StartSectorId, firstPortalId);
            if (firstPortalNode != exactBestStartNode)
                continue;
            long accessCost = ResolveDeterministicPortalAccessCost(
                _world,
                _world.Sectors[source.Demand.StartSectorId],
                source.Demand.StartSectorId,
                firstPortalId,
                source.Demand.StartX,
                source.Demand.StartY);
            if (accessCost == long.MaxValue)
                continue;
            if (suffix.MergeNode >= selectedNode)
                continue;
            selectedNode = suffix.MergeNode;
            selected = suffix;
        }

        if (selected == null)
            return false;

        ImmutableRouteSequence sectorIds = ImmutableRouteSequence.Concat(
            Array.Empty<int>(),
            0,
            selected.SectorIds,
            selected.SectorStartIndex);
        ImmutableRouteSequence portalIds = ImmutableRouteSequence.Concat(
            Array.Empty<int>(),
            0,
            selected.PortalIds,
            selected.PortalStartIndex);
        if (sectorIds.Length == 0
            || portalIds.Length + 1 != sectorIds.Length
            || sectorIds[0] != source.Demand.StartSectorId
            || sectorIds[sectorIds.Length - 1] != job.Key.GoalSectorId)
        {
            throw new InvalidOperationException(
                $"Shared start-sector route is inconsistent source={source.Demand.SourceId}, " +
                $"startSector={source.Demand.StartSectorId}, goalSector={job.Key.GoalSectorId}, " +
                $"selectedNode={FormatRouteNode(selectedNode)}.");
        }

        source.Handle = new PathHandle
        {
            HandleId = _nextPathHandleId++,
            WorldVersion = _world.Version,
            GoalX = job.GoalX,
            GoalY = job.GoalY,
            SectorIds = sectorIds,
            PortalIds = portalIds,
            CurrentSectorIndex = 0,
            BuildSource = "sharedStartSectorRoute:pathRequest"
        };
        state.Stage = IncrementalRouteMaterializationStage.Complete;
        _perf.NavigationPathSharedStartRouteHits++;
        return true;
    }

    private static int ResolveExactStartPortalNodeForSharedRoute(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source)
    {
        NavigationPathDemand demand = source.Demand
            ?? throw new InvalidOperationException("Shared route start-node resolution has no demand.");
        SectorData startSector = _world.Sectors[demand.StartSectorId];
        FlowPathKernelSearchState policySearch = source.HierarchyLevelArrayIndex >= 0
            ? source.DownwardCustomizations.Count == 0
                ? throw new InvalidOperationException("Shared route start-node resolution has no downward policy.")
                : source.DownwardCustomizations[source.DownwardCustomizations.Count - 1].SearchState
            : job.Policy?.SearchState;
        if (policySearch == null || !policySearch.IsCreated)
            throw new InvalidOperationException("Shared route start-node resolution has no policy search authority.");

        long bestCost = long.MaxValue;
        int bestNode = int.MinValue;
        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            int node = EncodePortalNode(demand.StartSectorId, portalId);
            long accessCost = ResolveDeterministicPortalAccessCost(
                _world,
                startSector,
                demand.StartSectorId,
                portalId,
                demand.StartX,
                demand.StartY);
            if (accessCost == long.MaxValue
                || !policySearch.ContainsSettled(node)
                || !policySearch.TryGetCost(node, out long suffixCost))
            {
                continue;
            }

            long totalCost = AddDeterministicPortalCosts(accessCost, suffixCost);
            if (totalCost < bestCost
                || (totalCost == bestCost && source.HierarchyLevelArrayIndex >= 0 && node < bestNode))
            {
                bestCost = totalCost;
                bestNode = node;
            }
        }
        return bestNode;
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

    private static int AdvanceNavigationRouteDownwardExpansion(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state,
        int operationCapacity)
    {
        if (state.RouteState.HasPendingSlice)
            return AdvanceRouteExpansionTaskSlice(job, state, operationCapacity);
        if (state.RouteState.TaskCount > 0)
            return AdvanceRouteExpansionTaskSlice(job, state, operationCapacity);
        if (state.DownwardCustomizationIndex < 0)
        {
            state.Stage = IncrementalRouteMaterializationStage.ExpandPolicy;
            return 1;
        }

        PortalHierarchyDownwardCustomization customization =
            source.DownwardCustomizations[state.DownwardCustomizationIndex--];
        ScheduleRoutePolicyTraversal(
            state,
            customization.SearchState,
            GetRouteBoundaryNode(state),
            customization.SourceLevelArrayIndex,
            RouteExpansionTaskType.TraverseDownwardPolicy);
        return 1;
    }

    private static int AdvanceNavigationRoutePolicyExpansion(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state,
        int operationCapacity)
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
            return 1;
        }
        if (state.RouteState.HasPendingSlice)
            return AdvanceRouteExpansionTaskSlice(job, state, operationCapacity);
        if (state.RouteState.TaskCount > 0)
            return AdvanceRouteExpansionTaskSlice(job, state, operationCapacity);
        state.Stage = source.HierarchyLevelArrayIndex >= 0
            ? IncrementalRouteMaterializationStage.ExpandGoalConnector
            : IncrementalRouteMaterializationStage.ConvertRoute;
        return 1;
    }

    private static int AdvanceNavigationRouteGoalConnectorExpansion(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state,
        int operationCapacity)
    {
        if (!state.GoalConnectorScheduled)
        {
            ScheduleRouteConnectorExpansion(
                state,
                source.HierarchyPolicy.GoalConnector,
                GetRouteBoundaryNode(state));
            state.GoalConnectorScheduled = true;
            return 1;
        }
        if (state.RouteState.HasPendingSlice)
            return AdvanceRouteExpansionTaskSlice(job, state, operationCapacity);
        if (state.RouteState.TaskCount > 0)
            return AdvanceRouteExpansionTaskSlice(job, state, operationCapacity);
        state.Stage = IncrementalRouteMaterializationStage.ConvertRoute;
        return 1;
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

    private static int AdvanceRouteExpansionTaskSlice(
        NavigationPathRequestJob job,
        IncrementalRouteMaterialization state,
        int operationCapacity)
    {
        if (operationCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(operationCapacity));
        FlowPathKernelRouteSliceResult result;
        if (state.RouteState.HasPendingSlice)
        {
            if (!state.RouteState.IsPendingSliceCompleted)
                return 1;
            result = state.RouteState.CompleteScheduledSlice();

            // The scheduled job owns the route stack until completion. Only
            // after Complete() may the authority be inspected or mutated.
            _perf.NavigationPathRouteSlices = checked(_perf.NavigationPathRouteSlices + 1);
            _perf.NavigationPathRouteSliceOperations = checked(
                _perf.NavigationPathRouteSliceOperations + result.OperationCount);
            if (result.OperationCount <= 0)
            {
                throw new InvalidOperationException(
                    $"Navigation route slice stopped without consuming work. reason={result.StopReason}.");
            }
            return 1;
        }

        if (state.RouteState.TaskCount == 0)
            throw new InvalidOperationException("Navigation route expansion has no pending task.");

        FlowPathKernelRouteTask top = state.RouteState.Peek();
        RouteExpansionTaskType topType = (RouteExpansionTaskType)top.Type;
        if (topType == RouteExpansionTaskType.BeginConnector)
        {
            state.RouteState.Pop();
            ScheduleRouteConnectorExpansion(
                state,
                ResolveRouteConnectorAuthority(state, top.AuthoritySlot),
                GetRouteBoundaryNode(state));
            return 1;
        }
        if (topType == RouteExpansionTaskType.TraverseNext
            || topType == RouteExpansionTaskType.FindHierarchyEdge)
        {
            throw new InvalidOperationException(
                $"Managed navigation route task type is forbidden in production: {topType}.");
        }

        int authoritySlot = state.RouteState.ResolvePendingSearchAuthoritySlot(
            (int)RouteExpansionTaskType.TraverseHierarchyPolicy,
            (int)RouteExpansionTaskType.TraverseL0Policy,
            (int)RouteExpansionTaskType.TraverseDownwardPolicy,
            (int)RouteExpansionTaskType.TraverseConnectorPolicy);
        FlowPathKernelSearchState search = ResolveRouteSearchAuthority(state, authoritySlot);
        state.RouteState.ScheduleSlice(
            search,
            RequireRouteWitnessIndex(),
            job.SharedRouteMergeIndex,
            authoritySlot,
            (int)RouteExpansionTaskType.TraverseHierarchyPolicy,
            (int)RouteExpansionTaskType.TraverseL0Policy,
            (int)RouteExpansionTaskType.TraverseDownwardPolicy,
            (int)RouteExpansionTaskType.TraverseConnectorPolicy,
            (int)RouteExpansionTaskType.ExpandHierarchyPolicyPair,
            (int)RouteExpansionTaskType.ExpandPair,
            (int)RouteExpansionTaskType.TraverseArray,
            (int)RouteExpansionTaskType.BeginConnector,
            operationCapacity);
#if UNITY_EDITOR
        // Editor tests invoke the synchronous navigation API without a live
        // logic timeline. Complete high-quota slices at this boundary so the
        // test observes the same committed authority that the next runtime
        // tick would consume, while live timeline calls retain cross-tick
        // worker scheduling.
        if (!LogicFrameRuntime.IsTimelineRunning
            && operationCapacity >= NavigationPathRequestFairShareOperationQuota)
        {
            result = state.RouteState.CompleteScheduledSlice();
            _perf.NavigationPathRouteSlices = checked(_perf.NavigationPathRouteSlices + 1);
            _perf.NavigationPathRouteSliceOperations = checked(
                _perf.NavigationPathRouteSliceOperations + result.OperationCount);
            if (result.OperationCount <= 0)
            {
                throw new InvalidOperationException(
                    $"Navigation route slice stopped without consuming work. reason={result.StopReason}.");
            }
            return 1;
        }
#endif
        return operationCapacity;
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

    private static int AdvanceNavigationRouteConversion(
        NavigationPathRequestJob job,
        NavigationPathSourceJob source,
        IncrementalRouteMaterialization state,
        int operationCapacity)
    {
        NavigationPathDemand demand = source.Demand;
        FlowPathKernelRouteSliceResult slice = state.RouteState.AdvanceConversionSlice(
            demand.StartSectorId,
            operationCapacity);
        _perf.NavigationPathRouteSlices = checked(_perf.NavigationPathRouteSlices + 1);
        _perf.NavigationPathRouteSliceOperations = checked(
            _perf.NavigationPathRouteSliceOperations + slice.OperationCount);
        if (slice.OperationCount <= 0)
            throw new InvalidOperationException("Navigation route conversion slice consumed no operations.");
        if (slice.StopReason != FlowPathKernelRouteSliceStopReason.RouteComplete)
            return slice.OperationCount;

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
        return slice.OperationCount;
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
            bool profile = MainThreadFrameProfiler.LoggingEnabled;
            int immutableSectorCount = state.MergedSuffix == null
                ? state.RouteState.ConvertedSectorCount
                : state.RouteState.ConvertedSectorCount - 1;
            if (immutableSectorCount < 0)
                throw new InvalidOperationException("Navigation merged route prefix has no terminal merge sector.");
            long sectorCopyStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            state.ImmutableSectorIds = state.RouteState.CopyConvertedSectors(immutableSectorCount);
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathMaterializeImmutableSectorCopy,
                    Stopwatch.GetTimestamp() - sectorCopyStartTicks);
            }
            long portalCopyStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            state.ImmutablePortalIds = state.RouteState.CopyConvertedPortals();
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathMaterializeImmutablePortalCopy,
                    Stopwatch.GetTimestamp() - portalCopyStartTicks);
            }
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
                bool profile = MainThreadFrameProfiler.LoggingEnabled;
                long concatStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
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
                if (profile)
                {
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.FlowNavigationPathMaterializeImmutableConcat,
                        Stopwatch.GetTimestamp() - concatStartTicks);
                }
            }
            bool profileValidation = MainThreadFrameProfiler.LoggingEnabled;
            long validationStartTicks = profileValidation ? Stopwatch.GetTimestamp() : 0L;
            if (state.RouteSectorIds.Length == 0
                || state.RoutePortalIds.Length + 1 != state.RouteSectorIds.Length)
            {
                throw new InvalidOperationException("Navigation immutable segmented route is inconsistent.");
            }
            if (profileValidation)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathMaterializeImmutableValidation,
                    Stopwatch.GetTimestamp() - validationStartTicks);
            }
            bool profileHash = MainThreadFrameProfiler.LoggingEnabled;
            long hashStartTicks = profileHash ? Stopwatch.GetTimestamp() : 0L;
            state.PathAuthorityHash = ComputeSectorPathAuthorityContentHash(
                    state.PathKey,
                    new SectorPathCacheEntry
                    {
                        SectorIds = state.RouteSectorIds,
                        PortalIds = state.RoutePortalIds
                    });
            if (profileHash)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathMaterializeImmutableHash,
                    Stopwatch.GetTimestamp() - hashStartTicks);
            }
        }
        if (source.HierarchyLevelArrayIndex >= 0 && state.MergedSuffix == null)
        {
            bool profileWitnessHasher = MainThreadFrameProfiler.LoggingEnabled;
            long witnessHasherStartTicks = profileWitnessHasher ? Stopwatch.GetTimestamp() : 0L;
            state.WitnessAuthorityHasher = new LogicStateHasher();
            state.WitnessAuthorityHasher.Add(0x50484C305749544EUL);
            state.WitnessAuthorityHasher.Add(state.BestStartNode);
            state.WitnessAuthorityHasher.Add(state.ImmutableSectorIds.Length);
            if (profileWitnessHasher)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathMaterializeImmutableWitnessHasher,
                    Stopwatch.GetTimestamp() - witnessHasherStartTicks);
            }
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
                bool profile = MainThreadFrameProfiler.LoggingEnabled;
                long witnessStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
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
                long witnessValidationStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                ValidatePortalHierarchyL0Witness(
                    witness,
                    state.BestStartNode,
                    source.Demand.StartSectorId,
                    job.Key.GoalSectorId);
                if (profile)
                {
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.FlowNavigationPathMaterializePublishWitnessValidation,
                        Stopwatch.GetTimestamp() - witnessValidationStartTicks);
                }
                witness.AuthorityContentHash = state.WitnessAuthorityHash;
                long witnessMutationStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                BeginHashedPortalHierarchyPolicyMutation(job.Policy, ref policyMutationStarted);
                source.HierarchyPolicy.L0WitnessesByStartNode.Add(state.BestStartNode, witness);
                source.HierarchyPolicy.L0WitnessesAuthorityContentHash ^=
                    ComputePortalHierarchyL0WitnessEntryAuthorityToken(
                        state.BestStartNode,
                        witness.AuthorityContentHash);
                if (profile)
                {
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.FlowNavigationPathMaterializePublishWitnessMutation,
                        Stopwatch.GetTimestamp() - witnessMutationStartTicks);
                }
                if (profile)
                {
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.FlowNavigationPathMaterializePublishWitness,
                        Stopwatch.GetTimestamp() - witnessStartTicks);
                }
            }

            bool profileCache = MainThreadFrameProfiler.LoggingEnabled;
            long cacheStartTicks = profileCache ? Stopwatch.GetTimestamp() : 0L;
            SetSectorPathCacheEntryWithAuthorityHash(
                state.PathKey,
                new SectorPathCacheEntry
                {
                    SectorIds = sectorIds,
                    PortalIds = portalIds,
                    LastUsedFrame = GetFrameCount()
                },
                state.PathAuthorityHash);
            if (profileCache)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathMaterializePublishCache,
                    Stopwatch.GetTimestamp() - cacheStartTicks);
            }
            bool profileTrim = MainThreadFrameProfiler.LoggingEnabled;
            long trimStartTicks = profileTrim ? Stopwatch.GetTimestamp() : 0L;
            TrimSectorPathCache();
            if (profileTrim)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathMaterializePublishTrim,
                    Stopwatch.GetTimestamp() - trimStartTicks);
            }
            long handleStartTicks = profileTrim ? Stopwatch.GetTimestamp() : 0L;
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
            if (profileTrim)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathMaterializePublishHandle,
                    Stopwatch.GetTimestamp() - handleStartTicks);
            }
            state.SharedSuffixRegistrationCursor = 0;
            state.SharedSuffixSectorPathIndex = 0;
            return;
        }

        if (state.SharedSuffixRegistrationCursor < state.RouteState.L0NodeCount)
        {
            bool profile = MainThreadFrameProfiler.LoggingEnabled;
            long suffixStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            RegisterNavigationSharedRouteSuffixOneOperation(job, state, sectorIds, portalIds);
            if (profile)
                _perf.NavigationPathMaterializePublishSuffixTicks += Stopwatch.GetTimestamp() - suffixStartTicks;
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
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long bookkeepingStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        int index = state.SharedSuffixRegistrationCursor;
        int sectorPathIndex = state.SharedSuffixSectorPathIndex;
        if (profile)
            _perf.NavigationPathMaterializePublishSuffixBookkeepingTicks +=
                Stopwatch.GetTimestamp() - bookkeepingStartTicks;
        long resolveStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
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
        if (profile)
            _perf.NavigationPathMaterializePublishSuffixResolveTicks +=
                Stopwatch.GetTimestamp() - resolveStartTicks;
        _perf.NavigationPathSharedSuffixCreates++;
        long validationStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (job.SharedRouteSuffixes.TryGetValue(node, out NavigationSharedRouteSuffix existing))
        {
            if (!job.SharedRouteMergeIndex.Contains(node))
            {
                throw new InvalidOperationException(
                    $"Navigation shared route merge index is missing an authoritative suffix node={FormatRouteNode(node)}.");
            }
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
            if (profile)
                _perf.NavigationPathMaterializePublishSuffixValidationTicks +=
                    Stopwatch.GetTimestamp() - validationStartTicks;
        }
        else
        {
            if (profile)
                _perf.NavigationPathMaterializePublishSuffixValidationTicks +=
                    Stopwatch.GetTimestamp() - validationStartTicks;
            long insertStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            job.SharedRouteSuffixes.Add(node, suffix);
            if (!job.SharedRouteMergeIndex.Add(node))
            {
                throw new InvalidOperationException(
                    $"Navigation shared route merge index contains a node without an authoritative suffix node={FormatRouteNode(node)}.");
            }
            job.SharedRouteSuffixesAuthorityContentHash ^=
                ComputeNavigationSharedRouteSuffixAuthorityToken(node, suffix);
            if (profile)
                _perf.NavigationPathMaterializePublishSuffixInsertTicks +=
                    Stopwatch.GetTimestamp() - insertStartTicks;
        }
        long finalizeStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        state.SharedSuffixSectorPathIndex = sectorPathIndex;
        state.SharedSuffixRegistrationCursor++;
        if (profile)
            _perf.NavigationPathMaterializePublishSuffixFinalizeTicks +=
                Stopwatch.GetTimestamp() - finalizeStartTicks;
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
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        PathHandle handle = demand.Agent.NavState.PathHandle;
        bool reusable = handle != null
                        && handle.WorldVersion == _world.Version
                        && demand.Agent.NavState.CommittedMovingTargetId == demand.MovingTargetId
                        && handle.SectorIds != null
                        && handle.SectorIds.Length > 0
                        && !PathReferencesMissingPortal(handle)
                        && FindSectorIndex(handle, demand.StartSectorId, 0) >= 0
                        && handle.SectorIds[handle.SectorIds.Length - 1] == demand.GoalSectorId;
        if (profile)
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationDemandPathValidation, Stopwatch.GetTimestamp() - stageStartTicks);
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
        stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (!TryAdvancePathToCurrentSector(demand.Agent, demand.StartSectorId))
            throw new InvalidOperationException("Reusable navigation path could not advance to its resolved source sector.");
        if (profile)
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationDemandPathAdvance, Stopwatch.GetTimestamp() - stageStartTicks);
        stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        UpdateFixedPortalParticipation(demand.Agent);
        if (profile)
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationDemandPortalParticipation, Stopwatch.GetTimestamp() - stageStartTicks);
        stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        EnqueueSteeringReadDomainFlowTileBuilds(demand.Agent, demand.MaximumTravelDistance);
        if (profile)
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationDemandTileBuilds, Stopwatch.GetTimestamp() - stageStartTicks);
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
            DisposeSharedGoalConnectorAuthorities(job);
            job.SharedRouteMergeIndex.Dispose();
        }
        NavigationPathRequestQueue.Clear();
        PendingNavigationPathRequests.Clear();
        NavigationPathRequestBySourceId.Clear();
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
        if (!MainThreadFrameProfiler.LoggingEnabled
            || (_perf.NavigationPathRequestOperations <= 0 && _perf.NavigationDemandResolutionCount <= 0))
            return;
        EditorNavigationPathTickDiagnostics.Enqueue(_perf);
        // The path queue closes the useful navigation statistics before the
        // next BeginPerfCall boundary. Keep the same accumulator reference so
        // later work in this logic frame remains visible to frame-aligned readers.
        EditorFlowPerfTickSnapshots.Enqueue(_perf);
        while (EditorNavigationPathTickDiagnostics.Count > EditorNavigationPathTickDiagnosticCapacity)
            EditorNavigationPathTickDiagnostics.Dequeue();
        while (EditorFlowPerfTickSnapshots.Count > EditorNavigationPathTickDiagnosticCapacity)
            EditorFlowPerfTickSnapshots.Dequeue();
    }

    private static void ClearEditorNavigationPathTickDiagnostics()
    {
        EditorNavigationPathTickDiagnostics.Clear();
        EditorFlowPerfTickSnapshots.Clear();
    }

    private static void CaptureEditorFlowPerfTickSnapshot()
    {
        if (!MainThreadFrameProfiler.LoggingEnabled || !_perfInitialized || _perf.Frame < 0)
            return;
        EditorFlowPerfTickSnapshots.Enqueue(_perf);
        while (EditorFlowPerfTickSnapshots.Count > EditorNavigationPathTickDiagnosticCapacity)
            EditorFlowPerfTickSnapshots.Dequeue();
    }

    private static FlowPerfAccumulator GetEditorFlowPerfTickSnapshot(int logicFrame)
    {
        if (logicFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(logicFrame), logicFrame, "Logic frame cannot be negative.");
        foreach (FlowPerfAccumulator snapshot in EditorFlowPerfTickSnapshots)
        {
            if (snapshot.Frame == logicFrame)
                return snapshot;
        }
        throw new InvalidOperationException(
            $"Flow performance snapshot is unavailable for logic frame {logicFrame}.");
    }

    public static bool HasEditorFlowPerfTickSnapshot(int logicFrame)
    {
        if (logicFrame < 0)
            return false;
        foreach (FlowPerfAccumulator snapshot in EditorFlowPerfTickSnapshots)
        {
            if (snapshot.Frame == logicFrame)
                return true;
        }
        return false;
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
                $"navigationSync=(demandResolve={TicksToMs(snapshot.NavigationDemandResolutionTicks):F3}ms/{snapshot.NavigationDemandResolutionCount}," +
                $"demandDispatch={TicksToMs(snapshot.NavigationDemandDispatchTicks):F3}ms/{snapshot.NavigationDemandDispatchCount})," +
                $"searchSlices={snapshot.NavigationPathSearchCommandSlices}/{snapshot.NavigationPathSearchCommandOperations}," +
                $"routeSlices={snapshot.NavigationPathRouteSlices}/{snapshot.NavigationPathRouteSliceOperations}," +
                $"commits={snapshot.NavigationPathRequestCommits},sourceCommits={snapshot.NavigationPathSourceCommits}," +
                $"goalProjectionIndex(buildStarts={snapshot.GoalProjectionIndexBuildStarts},scannedCells={snapshot.GoalProjectionIndexScannedCells},fullScans={snapshot.GoalProjectionIndexFullScans},publishes={snapshot.GoalProjectionIndexPublishes})," +
                $"orchestration=[policyCommit={TicksToMs(snapshot.NavigationPathPolicyCommitTicks):F3}ms," +
                $"budget={TicksToMs(snapshot.NavigationPathBudgetConsumeTicks):F3}ms," +
                $"removal={TicksToMs(snapshot.NavigationPathRequestRemovalTicks):F3}ms]," +
                $"runtime=[allocatedBytes={snapshot.NavigationPathAllocatedBytes},gc={snapshot.NavigationPathGen0Collections}/{snapshot.NavigationPathGen1Collections}/{snapshot.NavigationPathGen2Collections}]," +
                $"creates=[policy={snapshot.NavigationPathPolicyCreates},search={snapshot.NavigationPathRestrictedSearchCreates}," +
                $"hierarchy={snapshot.NavigationPathHierarchyPolicyCreates},materialization={snapshot.NavigationPathMaterializationCreates}," +
                $"concat={snapshot.NavigationPathImmutableConcatCreates},suffix={snapshot.NavigationPathSharedSuffixCreates},sharedStartRouteHits={snapshot.NavigationPathSharedStartRouteHits},movingGoalCorridorTrimHits={snapshot.NavigationPathMovingGoalCorridorTrimHits}," +
                $"sharedGoalConnectorBuilds={snapshot.NavigationPathSharedGoalConnectorBuilds},sharedGoalConnectorHits={snapshot.NavigationPathSharedGoalConnectorHits}]," +
                $"policyShift(calls={snapshot.NavigationPathPolicyShiftCalls},costEntries={snapshot.NavigationPathPolicyShiftCostEntries},openEntries={snapshot.NavigationPathPolicyShiftOpenEntries})," +
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
               $"initializeDetail=(sameSector={TicksToMs(perf.NavigationPathInitializeSameSectorTicks):F3}ms," +
               $"policy={TicksToMs(perf.NavigationPathInitializePolicyTicks):F3}ms," +
               $"policyStages=(lookup={TicksToMs(perf.NavigationPathInitializePolicyLookupTicks):F3}ms," +
               $"anchor={TicksToMs(perf.NavigationPathInitializePolicyAnchorTicks):F3}ms," +
               $"construct={TicksToMs(perf.NavigationPathInitializePolicyConstructTicks):F3}ms," +
               $"authority={TicksToMs(perf.NavigationPathInitializePolicyAuthorityTicks):F3}ms," +
               $"pin={TicksToMs(perf.NavigationPathInitializePolicyPinTicks):F3}ms)," +
               $"hierarchyResolve={TicksToMs(perf.NavigationPathInitializeHierarchyResolveTicks):F3}ms," +
               $"hierarchySelection={TicksToMs(perf.NavigationPathInitializeHierarchySelectionTicks):F3}ms)," +
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

    public static void RunEditorTestDisconnectedRestrictedBoundarySearch(
        int operationQuota,
        out long reachableCost,
        out bool unreachableSettled)
    {
        if (operationQuota <= 0)
            throw new ArgumentOutOfRangeException(nameof(operationQuota));

        const int sourceNode = 0;
        const int reachableNode = 1;
        const int unreachableNode = 2;
        using var graph = new FlowPathKernelGraphIndex(new[]
        {
            new FlowPathKernelGraphEdge(sourceNode, reachableNode, 7L)
        });
        var world = new NavigationWorld
        {
            SectorCountX = 1,
            Portals = new PortalData[2],
            L0SearchGraphIndex = graph
        };
        IncrementalRestrictedPortalSearch search = CreateIncrementalRestrictedPortalSearch(
            world,
            null,
            null,
            new[] { sourceNode },
            new[] { 0L },
            reverse: false,
            new[] { reachableNode, unreachableNode });
        search.MaterializeResultOnComplete = false;
        try
        {
            bool complete = false;
            int guard = 0;
            while (!complete)
            {
                if (++guard > 32)
                    throw new InvalidOperationException("Disconnected restricted boundary search exceeded its slice bound.");
                AdvanceIncrementalRestrictedPortalSearchSlice(search, operationQuota, out complete);
                if (!complete
                    && search.KernelState.HasPendingGraphSlice
                    && !search.KernelState.IsPendingGraphSliceCompleted)
                {
                    while (!search.KernelState.IsPendingGraphSliceCompleted)
                        System.Threading.Thread.Yield();
                }
            }
            if (!search.KernelState.TryGetCost(reachableNode, out reachableCost))
                throw new InvalidOperationException("Disconnected restricted boundary search did not settle its reachable target.");
            unreachableSettled = search.KernelState.ContainsSettled(unreachableNode);
        }
        finally
        {
            search.KernelState.Dispose();
        }
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

    public static int GetEditorTestFrameNavigationPathRequestGroupCount(int logicFrame)
    {
        return GetEditorFlowPerfTickSnapshot(logicFrame).NavigationPathRequestGroups;
    }

    public static int GetEditorTestFrameNavigationPathRequestOperationCount()
    {
        return _perf.NavigationPathRequestOperations;
    }

    public static int GetEditorTestFrameNavigationPathRequestOperationCount(int logicFrame)
    {
        return GetEditorFlowPerfTickSnapshot(logicFrame).NavigationPathRequestOperations;
    }

    public static void GetEditorTestFrameNavigationPathSliceCounts(
        out int searchSlices,
        out int searchOperations,
        out int routeSlices,
        out int routeOperations)
    {
        searchSlices = _perf.NavigationPathSearchCommandSlices;
        searchOperations = _perf.NavigationPathSearchCommandOperations;
        routeSlices = _perf.NavigationPathRouteSlices;
        routeOperations = _perf.NavigationPathRouteSliceOperations;
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

    public static int GetEditorTestFrameNavigationPathRequestCommitCount(int logicFrame)
    {
        return GetEditorFlowPerfTickSnapshot(logicFrame).NavigationPathRequestCommits;
    }

    public static int GetEditorTestFrameNavigationPathSourceCommitCount()
    {
        return _perf.NavigationPathSourceCommits;
    }

    public static int GetEditorTestFrameNavigationPathSourceCommitCount(int logicFrame)
    {
        return GetEditorFlowPerfTickSnapshot(logicFrame).NavigationPathSourceCommits;
    }

#endif
}
