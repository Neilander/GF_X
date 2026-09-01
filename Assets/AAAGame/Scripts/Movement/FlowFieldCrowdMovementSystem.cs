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

public readonly struct AuthoredNavigationSourceData
{
    public readonly int AgentTypeId;
    public readonly int Width;
    public readonly int Height;
    public readonly float CellSize;
    public readonly Vector3 Origin;
    public readonly bool[] WalkableMask;
    public readonly Vector3[] CellNavAnchors;
    public readonly FixVector2[] CellNavAnchorsFixedXZ;
    public readonly byte[] CostField;
    public readonly byte[] NeighborTraversalMask;
    public readonly FixVector2[] StaticCollisionVertices;
    public readonly int[] StaticCollisionPathStarts;
    public readonly FlowNavigationGridAsset.DerivedNavigationData DerivedNavigationData;
    public readonly bool UseRuntimeReadOnlyReferences;
    public readonly long CellSizeGridRaw;
    public readonly long OriginXGridRaw;
    public readonly long OriginZGridRaw;
    public readonly bool HasFixedAuthorityPayload;

    public AuthoredNavigationSourceData(
        int agentTypeId,
        int width,
        int height,
        float cellSize,
        Vector3 origin,
        bool[] walkableMask,
        Vector3[] cellNavAnchors,
        byte[] costField = null,
        byte[] neighborTraversalMask = null,
        FlowNavigationGridAsset.DerivedNavigationData derivedNavigationData = null,
        bool useRuntimeReadOnlyReferences = false,
        long cellSizeGridRaw = 0,
        long originXGridRaw = 0,
        long originZGridRaw = 0,
        FixVector2[] cellNavAnchorsFixedXZ = null,
        bool hasFixedAuthorityPayload = false,
        FixVector2[] staticCollisionVertices = null,
        int[] staticCollisionPathStarts = null)
    {
        AgentTypeId = agentTypeId;
        Width = width;
        Height = height;
        CellSize = cellSize;
        Origin = origin;
        WalkableMask = walkableMask;
        CellNavAnchors = cellNavAnchors;
        CellNavAnchorsFixedXZ = cellNavAnchorsFixedXZ;
        CostField = costField;
        NeighborTraversalMask = neighborTraversalMask;
        StaticCollisionVertices = staticCollisionVertices;
        StaticCollisionPathStarts = staticCollisionPathStarts;
        DerivedNavigationData = derivedNavigationData;
        UseRuntimeReadOnlyReferences = useRuntimeReadOnlyReferences;
        CellSizeGridRaw = cellSizeGridRaw;
        OriginXGridRaw = originXGridRaw;
        OriginZGridRaw = originZGridRaw;
        HasFixedAuthorityPayload = hasFixedAuthorityPayload;
    }
}

public static partial class FlowFieldCrowdMovementSystem
{
    private static readonly RuntimeConfig Config = new RuntimeConfig();
    private static bool s_RuntimeAgentTypeRadiiPrepared;
    private static Fix64 s_SmallAgentTypeRadiusFixed;
    private static Fix64 s_MediumAgentTypeRadiusFixed;
    private static Fix64 s_LargeAgentTypeRadiusFixed;
    private static readonly Dictionary<int, AgentRuntimeData> Agents = new Dictionary<int, AgentRuntimeData>();
    private static readonly List<int> OrderedAgentIds = new List<int>(128);
    private static readonly List<NavigationSyncRequest> CollectedNavigationSyncRequests =
        new List<NavigationSyncRequest>(128);
    private static readonly HashSet<int> CollectedNavigationSyncSourceIds = new HashSet<int>();
    private static int _collectedNavigationSyncFrame = -1;
    private static readonly List<int> NavigationWorldAgentTypeIdsScratch = new List<int>(8);
    private static readonly HashSet<int> NavigationWorldAgentTypeIdsSeenScratch = new HashSet<int>();
    private static readonly List<FixVector2> CommittedPotentialPathPointsScratch = new List<FixVector2>(64);
    private static readonly List<int> CommittedPotentialPathPrefixCostsScratch = new List<int>(64);
    private static readonly List<ushort> CommittedPotentialPortalSlotsScratch = new List<ushort>(32);
    private static readonly Dictionary<int, CircleObstacle> CircleObstacles = new Dictionary<int, CircleObstacle>();
    private static readonly Dictionary<int, BoxObstacle> BoxObstacles = new Dictionary<int, BoxObstacle>();
    private static readonly List<LogicStaticCollisionObstacle> StaticCollisionObstacleSnapshotBuilder =
        new List<LogicStaticCollisionObstacle>();
    private static LogicStaticCollisionObstacle[] _staticCollisionObstacleSnapshot =
        Array.Empty<LogicStaticCollisionObstacle>();
    private static bool _staticCollisionObstacleSnapshotDirty = true;
    private static readonly Dictionary<int, CostStamp> CostStamps = new Dictionary<int, CostStamp>();
    private static readonly Dictionary<MovingTargetAnchorKey, MovingTargetAnchor> MovingTargetAnchors = new Dictionary<MovingTargetAnchorKey, MovingTargetAnchor>();
    private static readonly LinkedList<MovingTargetAnchorKey> MovingTargetProjectionQueue =
        new LinkedList<MovingTargetAnchorKey>();
    private static readonly HashSet<MovingTargetAnchorKey> PendingMovingTargetProjectionKeys =
        new HashSet<MovingTargetAnchorKey>();
    private static readonly HashSet<MovingTargetAnchorKey> ReferencedMovingTargetAnchorKeysScratch = new HashSet<MovingTargetAnchorKey>();
    private static readonly Dictionary<FlowTileCacheKey, FlowTileCacheEntry> FlowTileCache = new Dictionary<FlowTileCacheKey, FlowTileCacheEntry>();
    private static readonly Dictionary<FlowTileCacheKey, FlowTileCacheEntry> DeterministicFlowTileCache = new Dictionary<FlowTileCacheKey, FlowTileCacheEntry>();
    private static readonly Dictionary<FlowLocalPotentialShapeKey, FlowLocalPotentialShape> FlowLocalPotentialShapes =
        new Dictionary<FlowLocalPotentialShapeKey, FlowLocalPotentialShape>();
    private static readonly LinkedList<FlowTileBuildJob> FlowTileBuildQueue = new LinkedList<FlowTileBuildJob>();
    private static readonly HashSet<FlowTileCacheKey> PendingFlowTileBuildJobs = new HashSet<FlowTileCacheKey>();
    private static readonly HashSet<NavigationPathRequestJob> NavigationPathRequestsBlockedByPendingSlice =
        new HashSet<NavigationPathRequestJob>();
    private static readonly HashSet<FlowTileCacheKey> ActiveFlowTileBuildKeys = new HashSet<FlowTileCacheKey>();
    private static readonly HashSet<FlowTileCacheKey> CurrentSteeringFlowTileBuildKeys = new HashSet<FlowTileCacheKey>();
    private static readonly HashSet<FlowTileCacheKey> NextSteeringFlowTileBuildKeys = new HashSet<FlowTileCacheKey>();
    private static readonly List<LinkedListNode<FlowTileBuildJob>> CurrentFlowTileBuildJobScratch =
        new List<LinkedListNode<FlowTileBuildJob>>(32);
    private static readonly List<LinkedListNode<FlowTileBuildJob>> NextFlowTileBuildJobScratch =
        new List<LinkedListNode<FlowTileBuildJob>>(32);
    private static readonly List<LinkedListNode<FlowTileBuildJob>> BackgroundFlowTileBuildJobScratch =
        new List<LinkedListNode<FlowTileBuildJob>>(128);
    private static readonly HashSet<FlowTileCacheKey> PendingFlowTileDependencyKeys = new HashSet<FlowTileCacheKey>();
    private static readonly Dictionary<int, Stack<float[]>> IntegrationArrayPool = new Dictionary<int, Stack<float[]>>();
    private static readonly Dictionary<int, Stack<long[]>> PortalAccessIntegrationArrayPool = new Dictionary<int, Stack<long[]>>();
    private static readonly Dictionary<SectorPathCacheKey, SectorPathCacheEntry> SectorPathCache = new Dictionary<SectorPathCacheKey, SectorPathCacheEntry>();
    private static readonly Dictionary<SectorCorridorPolicyKey, SectorCorridorPolicy> SectorCorridorPolicies = new Dictionary<SectorCorridorPolicyKey, SectorCorridorPolicy>();
    private static readonly HashSet<SectorCorridorPolicyKey> DeferredSectorCorridorPolicyAuthorityKeys =
        new HashSet<SectorCorridorPolicyKey>();
    private static bool _navigationSyncBatchResolveActive;
    private static readonly Dictionary<SectorPortalAccessKey, SectorPortalAccessEntry> SectorPortalAccessCache = new Dictionary<SectorPortalAccessKey, SectorPortalAccessEntry>();
    private static readonly Dictionary<StartPortalChoiceKey, StartPortalChoiceEntry> StartPortalChoiceCache = new Dictionary<StartPortalChoiceKey, StartPortalChoiceEntry>();
    private static readonly Dictionary<SharedGoalFieldKey, SharedGoalField> SharedGoalFields = new Dictionary<SharedGoalFieldKey, SharedGoalField>();
    private static readonly LinkedList<SharedGoalFieldBuildJob> SharedGoalFieldBuildQueue = new LinkedList<SharedGoalFieldBuildJob>();
    private static readonly HashSet<SharedGoalFieldKey> PendingSharedGoalFieldBuildJobs = new HashSet<SharedGoalFieldKey>();
    private static readonly HashSet<SharedGoalFieldKey> ActiveSharedGoalFieldBuildKeys = new HashSet<SharedGoalFieldKey>();
    private static readonly Dictionary<SharedGoalFieldKey, HashSet<int>> ActiveSharedGoalFieldDemandStartSectors = new Dictionary<SharedGoalFieldKey, HashSet<int>>();
    private static readonly Dictionary<SharedGoalFieldKey, Dictionary<int, int>> ActiveSharedGoalFieldDemandStartCells = new Dictionary<SharedGoalFieldKey, Dictionary<int, int>>();
    private static readonly List<SharedGoalFieldKey> ActiveSharedGoalFieldKeyOrderScratch = new List<SharedGoalFieldKey>(64);
    private static readonly Comparison<SharedGoalFieldKey> SharedGoalFieldKeyComparison = CompareSharedGoalKeys;
    private static readonly List<NavigationDistancePrewarmRequest> NavigationDistancePrewarmRequests = new List<NavigationDistancePrewarmRequest>(64);
    private static bool s_NavigationDistancePrewarmCompleted;
    private static bool s_NavigationDistancePrewarmCompletionMayHaveChanged;
    private static readonly List<int> SharedGoalPruneScratch = new List<int>(256);
    private static readonly Dictionary<CombatTargetSlotKey, CombatTargetSlotEntry> CombatTargetSlotCache = new Dictionary<CombatTargetSlotKey, CombatTargetSlotEntry>();
    private static readonly Dictionary<AttackAreaCandidateCacheKey, AttackAreaCandidateCacheEntry> AttackAreaCandidateCache =
        new Dictionary<AttackAreaCandidateCacheKey, AttackAreaCandidateCacheEntry>();
    private static int _attackAreaCandidateCacheFrame = int.MinValue;
    private static int _attackAreaCandidateCacheWorldVersion = int.MinValue;
    private static int _attackAreaCandidateCacheTopologyVersion = int.MinValue;
    private static int _attackAreaCandidateCacheBuildCount;
    private static readonly Dictionary<TargetingReachabilityCacheKey, TargetingReachabilityCacheEntry> TargetingReachabilityCache =
        new Dictionary<TargetingReachabilityCacheKey, TargetingReachabilityCacheEntry>();
    private static int _targetingReachabilityCacheFrame = int.MinValue;
    private static int _targetingReachabilityCacheMissCount;
    private static readonly Dictionary<FixedPortalOwnerKey, FixedPortalOwnerState> FixedPortalOwners = new Dictionary<FixedPortalOwnerKey, FixedPortalOwnerState>();
    private static readonly Dictionary<int, int> FixedPortalOwnerEvaluatedFrameByWorld = new Dictionary<int, int>();
    private static readonly Dictionary<FixedPortalOwnerKey, FixedPortalParticipantSnapshot> FixedPortalParticipantScratch = new Dictionary<FixedPortalOwnerKey, FixedPortalParticipantSnapshot>();
    private static readonly Dictionary<int, FixedPortalParticipation> FixedPortalParticipationByAgent = new Dictionary<int, FixedPortalParticipation>();
    private static readonly Dictionary<FixedPortalOwnerKey, FixedPortalParticipantIndex> FixedPortalParticipantIndexByKey = new Dictionary<FixedPortalOwnerKey, FixedPortalParticipantIndex>();
    private static readonly Dictionary<int, SortedSet<int>> PendingFixedCorridorParticipantAgentIdsByWorld = new Dictionary<int, SortedSet<int>>();
    private static readonly List<int> FixedPortalParticipationAgentIdScratch = new List<int>();
    private static readonly List<FixedPortalOwnerKey> FixedPortalOwnerKeyScratch = new List<FixedPortalOwnerKey>();
    private static readonly Comparison<FixedPortalOwnerKey> FixedPortalOwnerKeyComparison = CompareFixedPortalOwnerKeys;
    private static readonly Dictionary<int, FixedCorridorLookup> FixedCorridorLookupByWorldVersion = new Dictionary<int, FixedCorridorLookup>();

    private static readonly int[] NeighborOffsetX = { -1, 0, 1, -1, 1, -1, 0, 1 };
    private static readonly int[] NeighborOffsetY = { -1, -1, -1, 0, 0, 1, 1, 1 };
    private static readonly int[] CardinalOffsetX = { -1, 1, 0, 0 };
    private static readonly int[] CardinalOffsetY = { 0, 0, -1, 1 };
    private static readonly int[] CorridorAxisProbeOffsets = { -2, -1, 1, 2 };
    private static readonly Dictionary<long, List<AgentRuntimeData>> AgentSpatialBuckets = new Dictionary<long, List<AgentRuntimeData>>();
    // Goal occupancy used to scan every registered agent for every candidate point.
    // Keep separate position/goal indexes so the exact Fix64 distance test remains
    // authoritative while the broad phase is bounded by the local bucket window.
    private static readonly Dictionary<long, List<AgentRuntimeData>> NavigationGoalPositionBuckets = new Dictionary<long, List<AgentRuntimeData>>();
    private static readonly Dictionary<long, List<AgentRuntimeData>> NavigationGoalTargetBuckets = new Dictionary<long, List<AgentRuntimeData>>();
    private static readonly List<AgentRuntimeData> NearbyAgentScratch = new List<AgentRuntimeData>(32);
    private static readonly List<AgentRuntimeData> CombatClusterScratch = new List<AgentRuntimeData>(16);
    private static readonly List<NavigationGoalReservation> NavigationGoalReservations = new List<NavigationGoalReservation>(128);
    private static readonly List<int> BottleneckWaitingRemovalScratch = new List<int>(16);
    private static readonly MinHeap DistanceEstimateOpenSet = new MinHeap();
    private static readonly DeterministicCostHeap FixedDistanceEstimateOpenSet = new DeterministicCostHeap();
    private static readonly DeterministicCostHeap SectorDistanceEstimateOpenSet = new DeterministicCostHeap();
    private static readonly DeterministicFlowHeap DeterministicFlowOpenSet = new DeterministicFlowHeap();
    private static long[] SectorDistanceEstimateCosts = Array.Empty<long>();
    private static int[] SectorDistanceEstimateVisitedMarks = Array.Empty<int>();
    private static int[] SectorDistanceEstimateClosedMarks = Array.Empty<int>();
    private static int SectorDistanceEstimateSearchId;
    private static readonly DeterministicCostHeap PortalAccessIntegrationOpenSet = new DeterministicCostHeap();
    private static readonly List<int> DistanceEstimatePathIndices = new List<int>(256);
    private static readonly Dictionary<int, Stack<bool[]>> BoolArrayPool = new Dictionary<int, Stack<bool[]>>();
    private static readonly Dictionary<int, Stack<byte[]>> ByteArrayPool = new Dictionary<int, Stack<byte[]>>();
    private static readonly Dictionary<int, Stack<int[]>> IntArrayPool = new Dictionary<int, Stack<int[]>>();
    private static float[] DistanceEstimateCosts = Array.Empty<float>();
    private static long[] FixedDistanceEstimateCosts = Array.Empty<long>();
    private static int[] DistanceEstimateVisitedMarks = Array.Empty<int>();
    private static int[] DistanceEstimateClosedMarks = Array.Empty<int>();
    private static int[] DistanceEstimateParents = Array.Empty<int>();
    private static int DistanceEstimateSearchId;
#if UNITY_EDITOR
    private static readonly Dictionary<int, EditorNavigationPreviewWorld> EditorNavigationPreviewWorlds =
        new Dictionary<int, EditorNavigationPreviewWorld>();
#endif
    private static int _lastRuntimeDirtyPreviewTimingFrame = -100000;
    private static int _navigationGoalReservationFrame = -1;
    private static int _successfulMoveDiagnosticFrame = -1;
    private static int _successfulMoveDiagnosticCountThisFrame;
    private static int _heavySteeringDiagnosticFrame = -1;
    private static int _heavySteeringDiagnosticCountThisFrame;
    private static int _navigationStuckDiagnosticFrame = -1;
    private static int _navigationStuckDiagnosticCountThisFrame;
    private static int _tilePendingWithoutBuildFrames;
    private static int _lastFlowTileQueueTraceFrame = -1;

    private static readonly Dictionary<int, WorldRuntimeState> WorldStates = new Dictionary<int, WorldRuntimeState>();
    private static readonly List<WorldRuntimeState> RuntimeRebuildQueueScratch = new List<WorldRuntimeState>(8);
    private static readonly List<WorldRuntimeState> FlowBuildQueueWorldScratch = new List<WorldRuntimeState>(8);
    private static NavigationWorld _world;
    private static WorldRuntimeState _activeWorldState;
    private static int _flowBuildQueueWorldStartIndex;
    private static int _remainingNavigationWorkOperations;
    private static bool _navigationWorkBudgetActive;
    private static long _editorSlowestRuntimeDirtyCallTicks;
    private static string _editorSlowestRuntimeDirtyCallDiagnostics = "none";
    private static int _navigationTopologyVersion;
    private static int _nextWorldVersion = 1;
    private static int _nextPathHandleId = 1;
    private static int _lastAgentSpatialBucketFrame = -1;
    private static int _lastAgentSpatialBucketWorldVersion = -1;
    private static int _lastNavigationGoalOccupancyBucketFrame = -1;
    private static int _lastNavigationGoalOccupancyBucketWorldVersion = -1;
    private static Fix64 _navigationGoalOccupancyMaximumThreshold = Fix64.Zero;
    private static int _lastAgentRegistrySyncFrame = -1;
    private static int _lastOverlapDiagnosticsFrame = -1;
    private static string _lastWorldDirtyReason = "initial";
    private static string _lastRuntimeObstacleDirtyReason = "none";
    private static TestTerrainOverride _testTerrainOverride;
    private static readonly Dictionary<int, TestTerrainOverride> AuthoredTerrainSources = new Dictionary<int, TestTerrainOverride>();
#if UNITY_EDITOR
    private static readonly Dictionary<int, Fix64> TestAgentTypeRadii = new Dictionary<int, Fix64>();
    private static int _nextEditorBakeWorldVersion = -1;
    private static int _testNavigationPrepareRequestCount;
    private static bool _editorTestRequirePreparedNavigationSnapshot;
    private static int _testFixedPortalParticipationUpdateCount;
    private static int _editorPortalHierarchyMaximumSourceExpansions;
    private static long _editorPortalHierarchyMaximumSourceTicks;
    private static int _editorPortalHierarchyMaximumInvocationExpansions;
    private static long _editorPortalHierarchyMaximumInvocationTicks;
#endif
    private static bool _hasTestTimeOverride;
    private static int _testFrameCount;
    private static float _testTime;
    private static float _testDeltaTime = 0.1f;
    private static FlowPerfAccumulator _perf;
    private static bool _perfInitialized;
    private static int _lastSlowFrameOnlyPerfLogFrame = -100000;
    private static int _lastDiagnosticsEnabledFrame = -1;
    private static bool _diagnosticsEnabledForFrame;
    private static int _lastMaintenanceFrame = -1;
    private static int _runtimeNavigationTransitionDepth;
    private static long _fixedCorridorClassifiedCellCount;
    private static long _fixedCorridorExpandedCellCount;

    public static int NavigationTopologyVersion => _navigationTopologyVersion;
    public static int DeterministicWorldHashRefreshCount { get; private set; }
    public static bool IsNavigationDistancePrewarmCompleted =>
        NavigationDistancePrewarmRequests.Count == 0 || s_NavigationDistancePrewarmCompleted;

    public static event EventHandler<NavigationDistancePrewarmCompletedEventArgs> NavigationDistancePrewarmCompleted;

    public static void ResetAll()
    {
        Agents.Clear();
        OrderedAgentIds.Clear();
        CollectedNavigationSyncRequests.Clear();
        CollectedNavigationSyncSourceIds.Clear();
        ClearNavigationPathRequests();
        _collectedNavigationSyncFrame = -1;
        AgentSpatialBuckets.Clear();
        NavigationGoalPositionBuckets.Clear();
        NavigationGoalTargetBuckets.Clear();
        NearbyAgentScratch.Clear();
        CircleObstacles.Clear();
        BoxObstacles.Clear();
        StaticCollisionObstacleSnapshotBuilder.Clear();
        _staticCollisionObstacleSnapshot = Array.Empty<LogicStaticCollisionObstacle>();
        _staticCollisionObstacleSnapshotDirty = true;
        ClearCostStamps();
        MovingTargetAnchors.Clear();
        MovingTargetProjectionQueue.Clear();
        PendingMovingTargetProjectionKeys.Clear();
        CombatTargetSlotCache.Clear();
        AttackAreaCandidateCache.Clear();
        _attackAreaCandidateCacheFrame = int.MinValue;
        _attackAreaCandidateCacheWorldVersion = int.MinValue;
        _attackAreaCandidateCacheTopologyVersion = int.MinValue;
        _attackAreaCandidateCacheBuildCount = 0;
        TargetingReachabilityCache.Clear();
        _targetingReachabilityCacheFrame = int.MinValue;
        _targetingReachabilityCacheMissCount = 0;
        ClearFlowTileCache();
        ReturnPendingFlowTileBuildIntegrations();
        FlowTileBuildQueue.Clear();
        PendingFlowTileBuildJobs.Clear();
        ActiveFlowTileBuildKeys.Clear();
        ClearFlowTileBuildSchedulingState();
        PendingFlowTileDependencyKeys.Clear();
        ClearSectorPathCache();
        ClearNavigationPathRequests();
        DeferredSectorCorridorPolicyAuthorityKeys.Clear();
        _navigationSyncBatchResolveActive = false;
        ClearSectorPortalAccessCache();
        StartPortalChoiceCache.Clear();
        ClearSharedGoalFieldCache();
        SharedGoalFieldBuildQueue.Clear();
        PendingSharedGoalFieldBuildJobs.Clear();
        ActiveSharedGoalFieldBuildKeys.Clear();
        ActiveSharedGoalFieldDemandStartSectors.Clear();
        ActiveSharedGoalFieldDemandStartCells.Clear();
        ActiveSharedGoalFieldKeyOrderScratch.Clear();
        NavigationDistancePrewarmRequests.Clear();
        s_NavigationDistancePrewarmCompleted = false;
        s_NavigationDistancePrewarmCompletionMayHaveChanged = false;
        FixedPortalOwners.Clear();
        FixedPortalOwnerEvaluatedFrameByWorld.Clear();
        FixedPortalParticipantScratch.Clear();
        FixedPortalParticipationByAgent.Clear();
        FixedPortalParticipantIndexByKey.Clear();
        PendingFixedCorridorParticipantAgentIdsByWorld.Clear();
        FixedPortalParticipationAgentIdScratch.Clear();
        FixedPortalOwnerKeyScratch.Clear();
        FixedCorridorLookupByWorldVersion.Clear();
        _fixedCorridorClassifiedCellCount = 0;
        _fixedCorridorExpandedCellCount = 0;
#if UNITY_EDITOR
        foreach (EditorNavigationPreviewWorld preview in EditorNavigationPreviewWorlds.Values)
        {
            if (preview?.World != null)
                DisposeFlowTileWorldInputBackings(preview.World);
        }
        EditorNavigationPreviewWorlds.Clear();
#endif
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            ReturnRuntimeDirtyWorkingWorld(state.RuntimeDirtyJob);
            DisposeFlowTileWorldInputBackings(state.World);
            state.World?.L0SearchGraphIndex?.Dispose();
            if (state.World != null)
                state.World.L0SearchGraphIndex = null;
            DisposeFlowPathKernelWitnessIndex(state.World?.Hierarchy);
        }

        WorldStates.Clear();
        RuntimeRebuildQueueScratch.Clear();
        FlowBuildQueueWorldScratch.Clear();
        _world = null;
        _activeWorldState = null;
        _navigationTopologyVersion++;
        _flowBuildQueueWorldStartIndex = 0;
        _remainingNavigationWorkOperations = 0;
        _navigationWorkBudgetActive = false;
        _editorSlowestRuntimeDirtyCallTicks = 0;
        _editorSlowestRuntimeDirtyCallDiagnostics = "none";
        _nextWorldVersion = 1;
        _nextPathHandleId = 1;
        DeterministicWorldHashRefreshCount = 0;
        ResetDeterministicHashCheckpoint();
        _lastAgentSpatialBucketFrame = -1;
        _lastAgentSpatialBucketWorldVersion = -1;
        _lastNavigationGoalOccupancyBucketFrame = -1;
        _lastNavigationGoalOccupancyBucketWorldVersion = -1;
        _navigationGoalOccupancyMaximumThreshold = Fix64.Zero;
        _lastAgentRegistrySyncFrame = -1;
        _lastOverlapDiagnosticsFrame = -1;
        _successfulMoveDiagnosticFrame = -1;
        _successfulMoveDiagnosticCountThisFrame = 0;
        _heavySteeringDiagnosticFrame = -1;
        _heavySteeringDiagnosticCountThisFrame = 0;
        _navigationStuckDiagnosticFrame = -1;
        _navigationStuckDiagnosticCountThisFrame = 0;
        _testTerrainOverride = null;
        AuthoredTerrainSources.Clear();
        LogicStaticCollisionShadowService.Clear();
#if UNITY_EDITOR
        TestAgentTypeRadii.Clear();
        ClearEditorNavigationPathTickDiagnostics();
        _nextEditorBakeWorldVersion = -1;
        _editorTestRequirePreparedNavigationSnapshot = false;
        _testFixedPortalParticipationUpdateCount = 0;
        _editorPortalHierarchyMaximumSourceExpansions = 0;
        _editorPortalHierarchyMaximumSourceTicks = 0;
        _editorPortalHierarchyMaximumInvocationExpansions = 0;
        _editorPortalHierarchyMaximumInvocationTicks = 0;
#endif
        _perf = default;
        _perfInitialized = false;
        ClearHierarchyStartConnectorCache();
        _lastDiagnosticsEnabledFrame = -1;
        _diagnosticsEnabledForFrame = false;
        _lastMaintenanceFrame = -1;
        _lastSlowFrameOnlyPerfLogFrame = -100000;
        _runtimeNavigationTransitionDepth = 0;
    }

    public static void PulsePerformanceFrame()
    {
        BeginPerfCall();
    }

    public static void RecordManagerConfigTicks(long ticks)
    {
        BeginPerfCall();
        _perf.ManagerConfigTicks += ticks;
    }

    public static void RecordManagerSourceGateTicks(long ticks)
    {
        BeginPerfCall();
        _perf.ManagerSourceGateTicks += ticks;
    }

    public static bool IsRuntimeNavigationTransitionActive()
    {
        return _runtimeNavigationTransitionDepth > 0;
    }

    public static void BeginRuntimeNavigationTransition()
    {
        if (_runtimeNavigationTransitionDepth == 0)
            ResetAll();
        _runtimeNavigationTransitionDepth++;
    }

    public static void EndRuntimeNavigationTransition()
    {
        if (_runtimeNavigationTransitionDepth <= 0)
            throw new InvalidOperationException("EndRuntimeNavigationTransition failed: transition is not active.");

        _runtimeNavigationTransitionDepth--;
        if (_runtimeNavigationTransitionDepth == 0)
            ClearRuntimeNavigationRegistrations();
    }

    public static void ForceEndRuntimeNavigationTransition()
    {
        if (_runtimeNavigationTransitionDepth <= 0)
            return;

        _runtimeNavigationTransitionDepth = 0;
        ClearRuntimeNavigationRegistrations();
    }

    private static void ClearRuntimeNavigationRegistrations()
    {
        CollectedNavigationSyncRequests.Clear();
        CollectedNavigationSyncSourceIds.Clear();
        ClearNavigationPathRequests();
        _collectedNavigationSyncFrame = -1;
        Agents.Clear();
        OrderedAgentIds.Clear();
        AgentSpatialBuckets.Clear();
        NavigationGoalPositionBuckets.Clear();
        NavigationGoalTargetBuckets.Clear();
        NearbyAgentScratch.Clear();
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
        _lastAgentSpatialBucketFrame = -1;
        _lastAgentSpatialBucketWorldVersion = -1;
        _lastNavigationGoalOccupancyBucketFrame = -1;
        _lastNavigationGoalOccupancyBucketWorldVersion = -1;
        _navigationGoalOccupancyMaximumThreshold = Fix64.Zero;
    }

    private static string BuildRuntimeDirtyJobDiagnostics()
    {
        var agentTypeIds = new List<int>(WorldStates.Keys);
        agentTypeIds.Sort();
        var entries = new List<string>();
        for (int i = 0; i < agentTypeIds.Count; i++)
        {
            WorldRuntimeState state = WorldStates[agentTypeIds[i]];
            RuntimeDirtyRebuildJob job = state?.RuntimeDirtyJob;
            if (job == null)
                continue;

            NavigationWorld world = job.WorkingWorld ?? job.TargetWorld;
            entries.Add(
                $"agent={state.AgentTypeId},stage={job.Stage},portalStage={job.PortalStage}," +
                $"sectorCursor={job.SectorCursor},cloneShellStage={job.CloneShellStage},cloneSectorCursor={job.CloneSectorCursor}," +
                $"islandStage={job.IslandStage},islandSector={job.IslandSectorCursor},islandComponent={job.IslandComponentCursor}," +
                $"islandBoundary={job.IslandBoundaryCursor},islandNode={job.IslandNodeCursor},islandRoots={job.IslandRootHeapCount}," +
                $"portalSectorCursor={job.PortalSectorCursor},portalTransitionCursor={job.PortalTransitionCursor}," +
                $"dirty={job.DirtySectorIds?.Count ?? 0},costDirty={job.CostDirtySectorIds?.Count ?? 0}," +
                $"world={(world != null ? world.Width : 0)}x{(world != null ? world.Height : 0)}," +
                $"sectors={world?.Sectors?.Length ?? 0}," +
                BuildRuntimeDirtyStageAccumulatedTiming(job));
        }

        string pending = entries.Count > 0 ? string.Join("|", entries) : "none";
        return $"pending=[{pending}],slowestCall=[{_editorSlowestRuntimeDirtyCallDiagnostics}]";
    }

#if UNITY_EDITOR
    public static Fix64 ResolveNavigationTargetExtentForEditorTest(IEntityContext target)
    {
        return ResolveNavigationTargetExtentFixed(target);
    }

    public static void RegisterAgentForEditorTest(IEntityContext entity, float radius, int agentTypeId)
    {
        RegisterAgentInternal(entity, radius, ResolvePreferredAgentTypeId(agentTypeId));
    }

    public static void UpdateAgentForEditorTest(IEntityContext entity, float radius, int agentTypeId)
    {
        if (entity == null)
            return;
        if (IsRuntimeNavigationTransitionActive())
            return;

        int id = ResolveAgentId(entity);
        if (!Agents.TryGetValue(id, out AgentRuntimeData agent))
        {
            RegisterAgentForEditorTest(entity, radius, agentTypeId);
            return;
        }

        agent.CharacterKey = entity.CharacterKey;
        agent.Position = entity.LogicFramePosition();
        agent.PositionFixed = entity.LogicFramePositionFixed();
        agent.RegisteredRadius = Mathf.Max(0.05f, radius);
        agent.RadiusFixed = ResolveCollisionRadiusFixed(entity);
        agent.Radius = (float)agent.RadiusFixed;
        agent.Side = entity.Side;
        agent.AgentTypeId = ResolvePreferredAgentTypeId(agentTypeId);
        agent.EntityTypeName = entity.GetType().Name;
        agent.MoveCompTypeName = entity.MoveComp?.GetType().Name ?? "null";
        if (string.IsNullOrEmpty(agent.RegistrationSource))
            agent.RegistrationSource = "UpdateAgentForEditorTest";
        UpdateAgentNavigationIntent(agent, entity.MoveComp);
        _lastAgentSpatialBucketFrame = -1;
        _lastAgentRegistrySyncFrame = -1;
    }
#endif

    private static void RegisterAgentInternal(IEntityContext entity, float radius, int agentTypeId)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));
        if (IsRuntimeNavigationTransitionActive())
            return;

        int id = ResolveAgentId(entity);
        if (!Agents.TryGetValue(id, out AgentRuntimeData agent))
        {
            agent = new AgentRuntimeData
            {
                Id = id
            };
            agent.NavState.AgentId = id;
            Agents.Add(id, agent);
            AddOrderedAgentId(id);
        }

        agent.CharacterKey = entity.CharacterKey;
        agent.Position = entity.LogicFramePosition();
        agent.PositionFixed = entity.LogicFramePositionFixed();
        agent.RegisteredRadius = Mathf.Max(0.05f, radius);
        agent.RadiusFixed = ResolveCollisionRadiusFixed(entity);
        agent.Radius = (float)agent.RadiusFixed;
        agent.Side = entity.Side;
        agent.AgentTypeId = agentTypeId;
        if (string.IsNullOrEmpty(agent.EntityTypeName))
            agent.EntityTypeName = entity.GetType().Name;
        if (string.IsNullOrEmpty(agent.MoveCompTypeName))
            agent.MoveCompTypeName = entity.MoveComp?.GetType().Name ?? "null";
        agent.RegistrationSource = "RegisterAgent";
        agent.IsSyntheticRegistration = false;
        UpdateAgentNavigationIntent(agent, entity.MoveComp);
        _lastAgentSpatialBucketFrame = -1;
        _lastAgentRegistrySyncFrame = -1;
    }

    public static void UnregisterAgent(int agentId)
    {
        CancelNavigationPathRequestsForSource(agentId);
        RemoveFixedPortalParticipation(agentId);
        bool removed = Agents.Remove(agentId);
        int orderedIndex = OrderedAgentIds.BinarySearch(agentId);
        if (removed)
        {
            if (orderedIndex < 0)
                throw new InvalidOperationException($"UnregisterAgent failed: ordered agent id {agentId} is missing.");
            OrderedAgentIds.RemoveAt(orderedIndex);
        }
        else if (orderedIndex >= 0)
        {
            throw new InvalidOperationException($"UnregisterAgent failed: stale ordered agent id {agentId} exists.");
        }

        _lastAgentSpatialBucketFrame = -1;
        _lastAgentSpatialBucketWorldVersion = -1;
        _lastAgentRegistrySyncFrame = -1;
    }

    public static void UpdateAgent(IEntityContext entity)
    {
        BeginPerfCall();
        long startTicks = GetDiagnosticTimestamp();
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long profileStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        try
        {
            if (entity == null)
                return;
            if (IsRuntimeNavigationTransitionActive())
                return;

            int id = ResolveAgentId(entity);
            if (!Agents.TryGetValue(id, out AgentRuntimeData agent))
            {
                RegisterAgent(entity);
                return;
            }

            agent.CharacterKey = entity.CharacterKey;
            agent.Position = entity.LogicFramePosition();
            agent.PositionFixed = entity.LogicFramePositionFixed();
            agent.RadiusFixed = ResolveCollisionRadiusFixed(entity);
            agent.Radius = (float)agent.RadiusFixed;
            agent.RegisteredRadius = agent.Radius;
            agent.Side = entity.Side;
            agent.AgentTypeId = ResolveExplicitAgentTypeId(entity, nameof(UpdateAgent));
            if (string.IsNullOrEmpty(agent.RegistrationSource))
                agent.RegistrationSource = "UpdateAgent";
            UpdateAgentNavigationIntent(agent, entity.MoveComp);
            if (IsMovementDiagnosticsEnabled())
                TrackAndLogNavigationStuckTrace(agent, entity.LogicFramePosition());
            _lastAgentSpatialBucketFrame = -1;
            _lastAgentRegistrySyncFrame = -1;
        }
        finally
        {
            _perf.AgentUpdateTicks += GetDiagnosticTimestamp() - startTicks;
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationAgentUpdate,
                    Stopwatch.GetTimestamp() - profileStartTicks);
            }
        }
    }

    public static void SetAgentSide(int agentId, SideType side)
    {
        if (Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            agent.Side = side;
    }

    public static void SetAgentIgnoreCollision(int agentId, bool ignore)
    {
        if (Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            agent.IgnoreAgentCollision = ignore;
    }

    private static void TrackAndLogNavigationStuckTrace(AgentRuntimeData agent, Vector3 currentPosition)
    {
        if (agent == null)
            throw new InvalidOperationException("TrackAndLogNavigationStuckTrace failed: agent is null.");

        AgentNavState nav = agent.NavState;
        int frame = GetFrameCount();
        Vector3 previousPosition = nav.LastStuckTracePosition;
        float movedDistance = previousPosition.sqrMagnitude > 0.0001f
            ? HorizontalDistanceXZ(previousPosition, currentPosition)
            : float.PositiveInfinity;

        Vector3 toGoal = nav.LastGoalWorld - currentPosition;
        toGoal.y = 0f;
        float goalDistance = toGoal.magnitude;
        bool hasMoveIntent = agent.HasNavigationIntent
                             && (nav.DesiredVelocity.sqrMagnitude > 0.25f
                             || nav.ResolvedVelocity.sqrMagnitude > 0.25f
                             || nav.LastSteeringDesiredVelocity.sqrMagnitude > 0.25f);
        bool goalStillRelevant = nav.HasGoal
                                 && goalDistance > Mathf.Max(agent.Radius * 4f, 0.75f);
        bool notProgressing = movedDistance < Mathf.Max(0.015f, agent.Radius * 0.12f)
                              && goalDistance >= nav.LastStuckTraceGoalDistance - 0.02f;

        if (hasMoveIntent && goalStillRelevant && notProgressing)
        {
            nav.StuckTraceFrames++;
        }
        else
        {
            nav.StuckTraceFrames = 0;
        }

        nav.LastStuckTracePosition = currentPosition;
        nav.LastStuckTraceGoalDistance = goalDistance;

        if (nav.StuckTraceFrames < 18)
            return;
        if (nav.LastStuckTraceDiagnosticFrame >= 0
            && frame - nav.LastStuckTraceDiagnosticFrame < 30)
        {
            return;
        }
        if (!TryConsumeNavigationStuckDiagnosticBudget())
            return;

        nav.LastStuckTraceDiagnosticFrame = frame;
        Debug.LogWarning(BuildNavigationStuckTraceDiagnosticForAgentWorld(agent, currentPosition, movedDistance, goalDistance));
    }

    private static string BuildNavigationStuckTraceDiagnosticForAgentWorld(AgentRuntimeData agent, Vector3 currentPosition, float movedDistance, float goalDistance)
    {
        NavigationWorld previousWorld = _world;
        WorldRuntimeState previousActiveWorldState = _activeWorldState;
        try
        {
            if (!TryEnsureWorldBuilt(agent.AgentTypeId))
            {
                return $"[FlowNavigationStuckTrace] frame={GetFrameCount()} key={agent.CharacterKey} id={agent.Id} " +
                       $"agentType={agent.AgentTypeId} pos={currentPosition} agentWorldUnavailable=True " +
                       $"previousWorld={(previousWorld != null ? previousWorld.Version.ToString() : "null")} " +
                       $"previousAgentType={(previousWorld != null ? previousWorld.AgentTypeId.ToString() : "null")}";
            }

            return BuildNavigationStuckTraceDiagnostic(agent, currentPosition, movedDistance, goalDistance);
        }
        finally
        {
            _world = previousWorld;
            _activeWorldState = previousActiveWorldState;
        }
    }

    private static string BuildNavigationStuckTraceDiagnostic(AgentRuntimeData agent, Vector3 currentPosition, float movedDistance, float goalDistance)
    {
        if (agent == null)
            throw new InvalidOperationException("BuildNavigationStuckTraceDiagnostic failed: agent is null.");

        AgentNavState nav = agent.NavState;
        int frame = GetFrameCount();
        if (_world == null)
        {
            return $"[FlowNavigationStuckTrace] frame={frame} key={agent.CharacterKey} id={agent.Id} pos={currentPosition} world=null " +
                   $"desired={nav.DesiredVelocity} resolved={nav.ResolvedVelocity} goal={nav.LastGoalWorld}";
        }

        string cell = BuildAgentCellDiagnostic(agent, currentPosition);
        string goalPath = BuildGridPathDiagnostics(currentPosition, nav.LastGoalWorld, agent.AgentTypeId);
        string segment = BuildNavigationSegmentWalkableDiagnostics(
            _world,
            currentPosition,
            nav.LastSteeringResult.sqrMagnitude > 0.0001f
                ? nav.LastSteeringResult.normalized * Mathf.Max(_world != null ? _world.CellSize : 0.1f, 0.1f)
                : nav.DesiredVelocity.normalized * Mathf.Max(_world != null ? _world.CellSize : 0.1f, 0.1f),
            ResolveNavigationQueryClearance(_world, agent.Radius),
            requireClearStart: false,
            includeRuntimeObstacleOverlay: true);
        string tileState = BuildAgentCurrentTileStateDiagnostic(agent);
        string neighbors = BuildNearbyStuckNeighborDiagnostics(agent);
        string obstacles = BuildNearbyObstacleDiagnostics(currentPosition, nav.LastGoalWorld, agent.AgentTypeId);

        return $"[FlowNavigationStuckTrace] frame={frame} key={agent.CharacterKey} id={agent.Id} pos={currentPosition} " +
               $"agentType={agent.AgentTypeId} worldVersion={_world.Version} worldAgentType={_world.AgentTypeId} " +
               $"intent={agent.HasNavigationIntent} radius={agent.Radius:F3} mode={nav.LastMovementMode} stuckFrames={nav.StuckTraceFrames} " +
               $"moved={movedDistance:F4} goalDistance={goalDistance:F3} goal={nav.LastGoalWorld} " +
               $"desired={nav.DesiredVelocity} resolved={nav.ResolvedVelocity} resolvedFrame={nav.ResolvedVelocityFrame} " +
               $"lastSteerFrame={nav.LastSteeringFrame} desiredSrc={nav.LastSteeringDesiredSource} desiredDir={nav.LastSteeringDesiredDirection} " +
               $"desiredVel={nav.LastSteeringDesiredVelocity} base={nav.LastSteeringBaseVelocity} " +
               $"preClamp={nav.LastSteeringResultPreClamp} result={nav.LastSteeringResult} los={nav.LastSteeringHasLineOfSight} " +
               $"fixedResult={nav.LastFixedFlowResult} fixedVelocity={nav.LastFixedFlowVelocity} " +
               $"stableGoal=({nav.StableGoalX},{nav.StableGoalY}) stableWorld={nav.StableGoalWorld} raw=({nav.StableGoalRawX},{nav.StableGoalRawY}) " +
               $"currentCell={nav.CurrentCell} currentSector={nav.CurrentSectorId} handle={FormatPathHandle(nav.PathHandle)} " +
               $"{cell} segment={segment} {goalPath} tileState={tileState} neighbors={neighbors} obstacles={obstacles}";
    }

    private static string BuildAgentCurrentTileStateDiagnostic(AgentRuntimeData agent)
    {
        if (agent == null)
            throw new InvalidOperationException("BuildAgentCurrentTileStateDiagnostic failed: agent is null.");
        if (_world == null)
            return "world=null";

        AgentNavState nav = agent.NavState;
        PathHandle handle = nav.PathHandle;
        if (handle == null || handle.SectorIds == null || handle.SectorIds.Length == 0)
            return "handle=null";

        int sectorPathIndex = Mathf.Clamp(handle.CurrentSectorIndex, 0, handle.SectorIds.Length - 1);
        FlowTileCacheKey key = CreateTileCacheKeyForPathSegment(
            handle,
            sectorPathIndex,
            handle.GoalX,
            handle.GoalY,
            agent.AgentTypeId,
            out TileGoalKind goalKind,
            out int downstreamPortalId);
        bool cached = DeterministicFlowTileCache.TryGetValue(key, out FlowTileCacheEntry tile);
        bool pending = PendingFlowTileBuildJobs.Contains(key);
        bool inGrid = _world.WorldToGrid(agent.Position, out int cellX, out int cellY);
        string tileDiag = cached && inGrid ? BuildCurrentTileSteeringDiagnostics(tile, cellX, cellY) : "tile=not-cached";
        return $"goalKind={goalKind} portal={downstreamPortalId} key={FormatTileKey(key)} cached={cached} pending={pending} " +
               $"requiredJob={BuildFlowTileJobDiagnosticForKey(key)} queue={FlowTileBuildQueue.Count} " +
               $"pendingDetails={BuildPendingFlowTileJobDiagnostics()} cacheCount={FlowTileCache.Count} {tileDiag}";
    }

    private static string BuildNearbyStuckNeighborDiagnostics(AgentRuntimeData agent)
    {
        if (agent == null)
            throw new InvalidOperationException("BuildNearbyStuckNeighborDiagnostics failed: agent is null.");
        if (_world == null)
            return "world=null";

        List<AgentRuntimeData> nearby = CollectNearbyDynamicNeighbors(agent, Mathf.Max(agent.Radius * 5f, _world.CellSize * 2f));
        if (nearby.Count == 0)
            return "none";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(768);
        int count = 0;
        for (int i = 0; i < nearby.Count && count < 6; i++)
        {
            AgentRuntimeData other = nearby[i];
            Vector3 delta = other.Position - agent.Position;
            delta.y = 0f;
            float distance = delta.magnitude;
            float penetration = agent.Radius + other.Radius - distance;
            if (count > 0)
                builder.Append(" | ");
            builder.Append("other=").Append(other.CharacterKey)
                .Append(" id=").Append(other.Id)
                .Append(" pos=").Append(other.Position)
                .Append(" dist=").Append(distance.ToString("F3"))
                .Append(" penetration=").Append(penetration.ToString("F3"))
                .Append(" intent=").Append(other.HasNavigationIntent)
                .Append(" desired=").Append(other.NavState.DesiredVelocity)
                .Append(" resolved=").Append(other.NavState.ResolvedVelocity);
            count++;
        }

        return builder.ToString();
    }

    public static bool TryGetNavigationPointClearance(
        Vector3 point,
        float clearance,
        out bool isClear,
        out float violation,
        out int runtimeBoxCount,
        out int runtimeCircleCount)
    {
        isClear = false;
        violation = float.PositiveInfinity;
        runtimeBoxCount = BoxObstacles.Count;
        runtimeCircleCount = CircleObstacles.Count;
        if (_world == null)
            return false;

        float clampedClearance = ResolveNavigationQueryClearance(_world, Mathf.Max(0f, clearance));
        isClear = IsNavigationPointClear(_world, point, clampedClearance, includeRuntimeObstacleOverlay: true);
        violation = ResolveNavigationClearanceViolation(_world, point, clampedClearance, includeRuntimeObstacleOverlay: true);
        return true;
    }

    public static bool TryGetNavigationPointClearanceFixed(
        FixVector2 point,
        int agentTypeId,
        Fix64 clearance,
        out bool isClear,
        out Fix64 violation,
        out int runtimeBoxCount,
        out int runtimeCircleCount)
    {
        isClear = false;
        violation = Fix64.FromRaw(long.MaxValue);
        runtimeBoxCount = BoxObstacles.Count;
        runtimeCircleCount = CircleObstacles.Count;
        if (!TryGetCommittedNavigationQueryWorld(
                ResolvePreferredAgentTypeId(agentTypeId),
                allowSynchronousBuild: true,
                out NavigationWorld world))
        {
            return false;
        }

        Fix64 clampedClearance = ResolveNavigationQueryClearanceFixed(world, Fix64.Max(Fix64.Zero, clearance));
        isClear = IsNavigationPointClearFixed(world, point, clampedClearance, includeRuntimeObstacleOverlay: true);
        violation = ResolveNavigationClearanceViolationFixed(
            world,
            point,
            clampedClearance,
            includeRuntimeObstacleOverlay: true);
        return true;
    }

    private static WorldRuntimeState GetOrCreateWorldState(int agentTypeId)
    {
        if (!WorldStates.TryGetValue(agentTypeId, out WorldRuntimeState state))
        {
            state = new WorldRuntimeState { AgentTypeId = agentTypeId };
            WorldStates.Add(agentTypeId, state);
        }

        state.AgentTypeId = agentTypeId;
        return state;
    }

    private static string BuildCellProbeDiagnostics(NavigationWorld world, int x, int y)
    {
        if (world == null)
            return "centerProbe=world-null";
        if (x < 0 || x >= world.Width || y < 0 || y >= world.Height)
            return $"centerProbe={{cell=({x},{y}) inGrid=False}}";

        int index = world.GetIndex(x, y);
        return $"centerProbe={{cell=({x},{y}) center={world.GridToWorldCenter(x, y)} walk={world.WalkableMask[index]} base={world.BaseWalkableMask[index]} island={ResolveIslandIdForDiagnostics(world, x, y)} mask=0x{(world.NeighborTraversalMask != null && world.NeighborTraversalMask.Length == world.Width * world.Height ? world.NeighborTraversalMask[index].ToString("X2") : "NA")}}}";
    }

    private static string BuildWalkableNeighborhoodDiagnostics(NavigationWorld world, int centerX, int centerY, int radius)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append("neighborhood=[");
        bool first = true;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (x < 0 || x >= world.Width || y < 0 || y >= world.Height)
                    continue;

                if (!first)
                    builder.Append("; ");
                first = false;

                int index = world.GetIndex(x, y);
                Vector3 cellCenter = world.GridToWorldCenter(x, y);
                builder.Append("(");
                builder.Append(x);
                builder.Append(",");
                builder.Append(y);
                builder.Append(")");
                builder.Append("{walk=");
                builder.Append(world.WalkableMask[index]);
                builder.Append(",base=");
                builder.Append(world.BaseWalkableMask[index]);
                builder.Append(",island=");
                builder.Append(world.IslandIds != null && world.IslandIds.Length == world.Width * world.Height ? world.IslandIds[index] : -1);
                builder.Append(",mask=0x");
                builder.Append(world.NeighborTraversalMask != null && world.NeighborTraversalMask.Length == world.Width * world.Height
                    ? world.NeighborTraversalMask[index].ToString("X2")
                    : "NA");
                builder.Append(",center=");
                builder.Append(cellCenter);
                builder.Append("}");
            }
        }

        builder.Append("]");
        return builder.ToString();
    }

    private static string BuildNearestWalkableSearchDiagnostics(NavigationWorld world, int startX, int startY, int radius)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(256);
        builder.Append("nearestSearch=[");
        bool first = true;
        for (int r = 1; r <= radius; r++)
        {
            int foundCount = 0;
            for (int y = -r; y <= r; y++)
            {
                for (int x = -r; x <= r; x++)
                {
                    int nx = startX + x;
                    int ny = startY + y;
                    if (!world.IsWalkable(nx, ny))
                        continue;

                    foundCount++;
                }
            }

            if (!first)
                builder.Append("; ");
            first = false;
            builder.Append("r=");
            builder.Append(r);
            builder.Append(" found=");
            builder.Append(foundCount);
        }

        builder.Append("]");
        return builder.ToString();
    }

    private static void BlockCellsByCircle(bool[] walkableMask, int width, int height, float cellSize, Vector3 origin, Vector3 center, float radius)
    {
        Bounds bounds = new Bounds(center, new Vector3(radius * 2f, 0f, radius * 2f));
        int minX = Mathf.Clamp(Mathf.FloorToInt((bounds.min.x - origin.x) / cellSize), 0, width - 1);
        int maxX = Mathf.Clamp(Mathf.FloorToInt((bounds.max.x - origin.x) / cellSize), 0, width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt((bounds.min.z - origin.z) / cellSize), 0, height - 1);
        int maxY = Mathf.Clamp(Mathf.FloorToInt((bounds.max.z - origin.z) / cellSize), 0, height - 1);
        float blockRadius = radius + cellSize * 0.45f;
        float blockRadiusSq = blockRadius * blockRadius;

        Vector2 centerXZ = new Vector2(center.x, center.z);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 cellCenter = new Vector2(origin.x + (x + 0.5f) * cellSize, origin.z + (y + 0.5f) * cellSize);
                if ((cellCenter - centerXZ).sqrMagnitude <= blockRadiusSq)
                    walkableMask[x + y * width] = false;
            }
        }
    }

    private static void BlockCellsByCircleFixed(
        NavigationWorld world,
        FixVector2 center,
        Fix64 radius)
    {
        if (world == null)
            throw new InvalidOperationException("BlockCellsByCircleFixed failed: world is null.");
        if (world.WalkableMask == null || world.WalkableMask.Length != world.Width * world.Height)
            throw new InvalidOperationException("BlockCellsByCircleFixed failed: invalid walkable mask.");
        if (world.Width <= 0 || world.Height <= 0 || world.CellSizeGridRaw <= 0 || radius < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(radius), "BlockCellsByCircleFixed received invalid geometry.");

        int minX = Mathf.Clamp(NavigationGridFixedMath.WorldToGridCell(center.x - radius, world.OriginXGridRaw, world.CellSizeGridRaw), 0, world.Width - 1);
        int maxX = Mathf.Clamp(NavigationGridFixedMath.WorldToGridCell(center.x + radius, world.OriginXGridRaw, world.CellSizeGridRaw), 0, world.Width - 1);
        int minY = Mathf.Clamp(NavigationGridFixedMath.WorldToGridCell(center.y - radius, world.OriginZGridRaw, world.CellSizeGridRaw), 0, world.Height - 1);
        int maxY = Mathf.Clamp(NavigationGridFixedMath.WorldToGridCell(center.y + radius, world.OriginZGridRaw, world.CellSizeGridRaw), 0, world.Height - 1);
        Fix64 cellSize = world.CellSizeFixed;
        Fix64 blockRadius = radius + cellSize * Fix64.FromRaw(1844);
        Fix64 blockRadiusSquared = blockRadius * blockRadius;
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

    private static void BlockCellsByBounds(bool[] walkableMask, int width, int height, float cellSize, Vector3 origin, Bounds bounds)
    {
        int minX = Mathf.Clamp(Mathf.FloorToInt((bounds.min.x - origin.x) / cellSize), 0, width - 1);
        int maxX = Mathf.Clamp(Mathf.FloorToInt((bounds.max.x - origin.x) / cellSize), 0, width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt((bounds.min.z - origin.z) / cellSize), 0, height - 1);
        int maxY = Mathf.Clamp(Mathf.FloorToInt((bounds.max.z - origin.z) / cellSize), 0, height - 1);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector3 cellCenter = new Vector3(origin.x + (x + 0.5f) * cellSize, bounds.center.y, origin.z + (y + 0.5f) * cellSize);
                if (bounds.Contains(cellCenter))
                    walkableMask[x + y * width] = false;
            }
        }
    }

    private static void BlockCellsByBoxFixed(
        NavigationWorld world,
        FixVector2 center,
        FixVector2 halfExtents)
    {
        if (world == null)
            throw new InvalidOperationException("BlockCellsByBoxFixed failed: world is null.");
        if (world.WalkableMask == null || world.WalkableMask.Length != world.Width * world.Height)
            throw new InvalidOperationException("BlockCellsByBoxFixed failed: invalid walkable mask.");
        if (world.Width <= 0 || world.Height <= 0 || world.CellSizeGridRaw <= 0
            || halfExtents.x < Fix64.Zero || halfExtents.y < Fix64.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(halfExtents), "BlockCellsByBoxFixed received invalid geometry.");
        }

        int minX = Mathf.Clamp(NavigationGridFixedMath.WorldToGridCell(center.x - halfExtents.x, world.OriginXGridRaw, world.CellSizeGridRaw), 0, world.Width - 1);
        int maxX = Mathf.Clamp(NavigationGridFixedMath.WorldToGridCell(center.x + halfExtents.x, world.OriginXGridRaw, world.CellSizeGridRaw), 0, world.Width - 1);
        int minY = Mathf.Clamp(NavigationGridFixedMath.WorldToGridCell(center.y - halfExtents.y, world.OriginZGridRaw, world.CellSizeGridRaw), 0, world.Height - 1);
        int maxY = Mathf.Clamp(NavigationGridFixedMath.WorldToGridCell(center.y + halfExtents.y, world.OriginZGridRaw, world.CellSizeGridRaw), 0, world.Height - 1);
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
