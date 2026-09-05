using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using GameFramework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityGameFramework.Runtime;

[InitializeOnLoad]
internal static class Lv2PullChasePerformanceRunner
{
    private const string LaunchScenePath = "Assets/AAAGame/Scene/Launch.unity";
    private const string ResultRelativePath = "Logs/Lv2PullChasePerformance.txt";
    private const string BuildResultRelativePath = "Logs/Lv2RuntimeDirtyBuildPerformance.txt";
    private const string RuntimeDirtyBuildBuildingId = "Buil_DDoSDevice_Lv1";
    private const string SessionPrefix = "Avenge.Lv2PullChasePerformance.";
    private const string RunningKey = SessionPrefix + "Running";
    private const string StateKey = SessionPrefix + "State";
    private const string StartedUtcKey = SessionPrefix + "StartedUtc";
    private const string BaselineStartFrameKey = SessionPrefix + "BaselineStartFrame";
    private const string InvadeScheduledFrameKey = SessionPrefix + "InvadeScheduledFrame";
    private const string TargetEntityIdKey = SessionPrefix + "TargetEntityId";
    private const string ModeKey = SessionPrefix + "Mode";
    private const string ModeStartFrameKey = SessionPrefix + "ModeStartFrame";
    private const string WaypointIndexKey = SessionPrefix + "WaypointIndex";
    private const string RetreatStartFrameKey = SessionPrefix + "RetreatStartFrame";
    private const string BuildScenarioKey = SessionPrefix + "BuildScenario";
    private const string PrewarmKey = SessionPrefix + "Prewarm";
    private const string BuildScheduledFrameKey = SessionPrefix + "BuildScheduledFrame";
    private const string BuildAppliedFrameKey = SessionPrefix + "BuildAppliedFrame";
    private const string BuildBeforeDefendScheduledFrameKey = SessionPrefix + "BuildBeforeDefendScheduledFrame";
    private const string DefenseScheduledFrameKey = SessionPrefix + "DefenseScheduledFrame";
    private const string DefenseAppliedFrameKey = SessionPrefix + "DefenseAppliedFrame";
    private const ulong BaselineTicks = 30;
    private const ulong RetreatTicks = 600;
    private const int SampleIntervalRenderFrames = 10;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);
    private static readonly List<Vector3> s_Route = new List<Vector3>();
    private static readonly List<string> s_Samples = new List<string>(2048);
    private static readonly MainThreadPerfScope[] s_ChaseScopes =
    {
        MainThreadPerfScope.LogicFrameCommands,
        MainThreadPerfScope.LogicFrameCommandTimeAndInput,
        MainThreadPerfScope.LogicFrameCommandValueTeleport,
        MainThreadPerfScope.LogicFrameCommandCardPlacement,
        MainThreadPerfScope.LogicFrameCommandCard,
        MainThreadPerfScope.LogicFrameCommandSkills,
        MainThreadPerfScope.LogicFrameCommandMovementConstraint,
        MainThreadPerfScope.LogicFrameCommandCardSetup,
        MainThreadPerfScope.LogicFrameCommandInteractionTech,
        MainThreadPerfScope.LogicFrameCommandSpawnLifecycleObstacle,
        MainThreadPerfScope.LogicFrameCommandCardPlacementVisibilityReset,
        MainThreadPerfScope.LogicFrameCommandCardPlacementAllEntities,
        MainThreadPerfScope.LogicFrameCommandCardPlacementDirtyEntities,
        MainThreadPerfScope.LogicFrameCommandCardPlacementDirtyLookup,
        MainThreadPerfScope.LogicFrameCommandCardPlacementVisibilityCollect,
        MainThreadPerfScope.LogicFrameCommandCardPlacementVisibilityFilter,
        MainThreadPerfScope.LogicFrameCommandCardPlacementVisibilityTransition,
        MainThreadPerfScope.LogicFrameCommandCardPlacementVisibilityRemoveCoverage,
        MainThreadPerfScope.LogicFrameCommandCardPlacementVisibilityAddCoverage,
        MainThreadPerfScope.LogicFrameCommandCardPlacementVisibilityPublish,
        MainThreadPerfScope.LogicFrameCommandCardPlacementStationary,
        MainThreadPerfScope.LogicFrameCommandCardPlacementExploration,
        MainThreadPerfScope.LogicFrameCommandCardPlacementCoverage,
        MainThreadPerfScope.LogicFrameTick,
        MainThreadPerfScope.LogicFrameListenerSnapshot,
        MainThreadPerfScope.LogicFrameListenerCallbacks,
        MainThreadPerfScope.LogicFrameListenerAttributed,
        MainThreadPerfScope.LogicFrameListenerUnattributed,
        MainThreadPerfScope.LogicEntityFrameSetup,
        MainThreadPerfScope.LogicEntityBaseAndBuffs,
        MainThreadPerfScope.LogicEntityNavigationPositionSync,
        MainThreadPerfScope.LogicEntityNavigationSync,
        MainThreadPerfScope.FlowNavigationAgentUpdate,
        MainThreadPerfScope.FlowNavigationInactiveClear,
        MainThreadPerfScope.FlowNavigationCommit,
        MainThreadPerfScope.FlowNavigationResolveRequests,
        MainThreadPerfScope.FlowNavigationResolveDemandBatch,
        MainThreadPerfScope.FlowNavigationResolvePathQueueBatch,
        MainThreadPerfScope.FlowNavigationPathSliceInitialize,
        MainThreadPerfScope.FlowNavigationPathSliceGoalConnector,
        MainThreadPerfScope.FlowNavigationPathSliceCreateHierarchy,
        MainThreadPerfScope.FlowNavigationPathSliceExpandHierarchy,
        MainThreadPerfScope.FlowNavigationPathSliceDownward,
        MainThreadPerfScope.FlowNavigationPathSliceL0,
        MainThreadPerfScope.FlowNavigationPathSliceMaterialize,
        MainThreadPerfScope.FlowNavigationPathSliceComplete,
        MainThreadPerfScope.FogVisibilityCoverageRows,
        MainThreadPerfScope.FogVisibilityCoverageCells,
        MainThreadPerfScope.FogVisibilityCoveragePending,
        MainThreadPerfScope.FogVisibilityCollectCenter,
        MainThreadPerfScope.FogVisibilityCollectOctants,
        MainThreadPerfScope.FogVisibilityCollectIntervals,
        MainThreadPerfScope.FogVisibilityCollectOctant0,
        MainThreadPerfScope.FogVisibilityCollectOctant1,
        MainThreadPerfScope.FogVisibilityCollectOctant2,
        MainThreadPerfScope.FogVisibilityCollectOctant3,
        MainThreadPerfScope.FogVisibilityCollectOctant4,
        MainThreadPerfScope.FogVisibilityCollectOctant5,
        MainThreadPerfScope.FogVisibilityCollectOctant6,
        MainThreadPerfScope.FogVisibilityCollectOctant7,
        MainThreadPerfScope.FogVisibilityCollectGeometryIntervals,
        MainThreadPerfScope.FogVisibilityCoverageCellsSegment0,
        MainThreadPerfScope.FogVisibilityCoverageCellsSegment1,
        MainThreadPerfScope.FogVisibilityCoverageCellsSegment2,
        MainThreadPerfScope.FogVisibilityCoverageCellsSegment3,
        MainThreadPerfScope.FlowNavigationGoalConnectorSearchSlice,
        MainThreadPerfScope.FlowNavigationGoalConnectorInputCollection,
        MainThreadPerfScope.FlowNavigationGoalConnectorLink,
        MainThreadPerfScope.FlowNavigationGoalConnectorUnattributed,
        MainThreadPerfScope.FlowNavigationDemandAgentPreparation,
        MainThreadPerfScope.FlowNavigationDemandAssembly,
        MainThreadPerfScope.FlowNavigationTileQueue,
        MainThreadPerfScope.FlowNavigationPortalOwners,
        MainThreadPerfScope.FlowNavigationRequestSort,
        MainThreadPerfScope.FlowNavigationDemandResolve,
        MainThreadPerfScope.FlowNavigationDemandDispatch,
        MainThreadPerfScope.FlowNavigationDemandReuse,
        MainThreadPerfScope.FlowNavigationDemandEnqueue,
        MainThreadPerfScope.FlowNavigationDemandPathValidation,
        MainThreadPerfScope.FlowNavigationDemandPathAdvance,
        MainThreadPerfScope.FlowNavigationDemandPortalParticipation,
        MainThreadPerfScope.FlowNavigationDemandTileBuilds,
        MainThreadPerfScope.FlowNavigationDemandRemoveOther,
        MainThreadPerfScope.FlowNavigationDemandQueueMutation,
        MainThreadPerfScope.FlowNavigationRequestPrune,
        MainThreadPerfScope.FlowNavigationPathAdvance,
        MainThreadPerfScope.FlowNavigationPathInitialize,
        MainThreadPerfScope.FlowNavigationPathGoalConnector,
        MainThreadPerfScope.FlowNavigationPathCreateHierarchy,
        MainThreadPerfScope.FlowNavigationPathExpandHierarchy,
        MainThreadPerfScope.FlowNavigationPathDownward,
        MainThreadPerfScope.FlowNavigationPathL0,
        MainThreadPerfScope.FlowNavigationPathMaterialize,
        MainThreadPerfScope.FlowNavigationPathComplete,
        MainThreadPerfScope.LogicEntityBrain,
        MainThreadPerfScope.LogicEntityTargeting,
        MainThreadPerfScope.LogicEntityProjectile,
        MainThreadPerfScope.LogicEntityAttack,
        MainThreadPerfScope.LogicEntityDamageResolve,
        MainThreadPerfScope.LogicEntityMoveIntent,
        MainThreadPerfScope.LogicEntityMoveResolve,
        MainThreadPerfScope.LogicEntityMoveCommit,
        MainThreadPerfScope.LogicEntityPostUpdate,
        MainThreadPerfScope.LogicEntityFrameComplete,
        MainThreadPerfScope.CharacterMovePrepare,
        MainThreadPerfScope.FlowGroupMove,
        MainThreadPerfScope.FlowWorldBuildQueue,
        MainThreadPerfScope.FlowRuntimeRebuildQueue,
        MainThreadPerfScope.FlowTileBuildQueue,
        MainThreadPerfScope.FlowSteeringSetupPrepare,
        MainThreadPerfScope.FlowPrepareStartCell,
        MainThreadPerfScope.FlowPrepareStableGoal,
        MainThreadPerfScope.FlowPreparePathHandle,
        MainThreadPerfScope.FlowPrepareTileDemand,
        MainThreadPerfScope.FlowPrepareGoalOccupancy,
        MainThreadPerfScope.FlowPrepareWorld,
        MainThreadPerfScope.FlowPreparePathAdvance,
        MainThreadPerfScope.FlowPrepareReadDomain,
        MainThreadPerfScope.FlowPathFastValidation,
        MainThreadPerfScope.FlowPathBuildCache,
        MainThreadPerfScope.FlowPathBuildSharedGoal,
        MainThreadPerfScope.FlowPathBuildCorridorPolicy,
        MainThreadPerfScope.FlowPathBuildHierarchy,
        MainThreadPerfScope.FlowPathBuildPortalGraph,
        MainThreadPerfScope.FlowPathBuildCommittedPrefix,
        MainThreadPerfScope.FlowCorridorPolicyLookup,
        MainThreadPerfScope.FlowCorridorPolicyHierarchy,
        MainThreadPerfScope.FlowCorridorPolicyAuthorityHash,
        MainThreadPerfScope.FlowCorridorPolicyReconstruct,
        MainThreadPerfScope.FlowPathHandleCreateCache,
        MainThreadPerfScope.FlowCorridorPolicyGoalConnector,
        MainThreadPerfScope.FlowCorridorPolicyStartConnector,
        MainThreadPerfScope.FlowCorridorPolicyStartLeafAccess,
        MainThreadPerfScope.FlowCorridorPolicyStartLeafSearch,
        MainThreadPerfScope.FlowCorridorPolicyStartExtend,
        MainThreadPerfScope.FlowCorridorPolicyDownwardCustomize,
        MainThreadPerfScope.FlowCorridorPolicyReverseExpand,
        MainThreadPerfScope.FlowTileQueueActiveDemand,
        MainThreadPerfScope.FlowTileQueuePrune,
        MainThreadPerfScope.FlowTileQueueReferenceTrim,
        MainThreadPerfScope.FlowTileQueueCommit,
        MainThreadPerfScope.FlowTileQueueSharedGoal,
        MainThreadPerfScope.FlowTileQueueMovingTargetProjection,
        MainThreadPerfScope.FlowTileCommitQueueScan,
        MainThreadPerfScope.FlowTileCommitDirections,
        MainThreadPerfScope.FlowTileCommitContinuation,
        MainThreadPerfScope.FlowTileCommitDiagnosticShadow,
        MainThreadPerfScope.FlowTileCommitCache,
        MainThreadPerfScope.FlowTileCommitReferenceTrim,
        MainThreadPerfScope.FlowSteeringPath,
        MainThreadPerfScope.FlowSteeringPortalOwner,
        MainThreadPerfScope.FlowSteeringVelocity,
        MainThreadPerfScope.FlowSteeringPortalState,
        MainThreadPerfScope.FlowSteeringFunnel,
        MainThreadPerfScope.FlowSteeringGradient,
        MainThreadPerfScope.FlowSteeringIntegration,
        MainThreadPerfScope.FlowSteeringDiagnostics,
        MainThreadPerfScope.FlowSteeringFunnelGridLos,
        MainThreadPerfScope.FlowSteeringFunnelStaticSweep,
        MainThreadPerfScope.FlowSteeringDirectStatic,
        MainThreadPerfScope.FlowSteeringDirectLineOfSight,
        MainThreadPerfScope.CharacterTargetingEvaluate,
        MainThreadPerfScope.CharacterTargetingCandidateScan,
        MainThreadPerfScope.CharacterTargetingReachability,
        MainThreadPerfScope.CharacterTargetingReachabilityPreparation,
        MainThreadPerfScope.CharacterTargetingReachabilityCacheLookup,
        MainThreadPerfScope.CharacterTargetingReachabilityCandidateSelection,
        MainThreadPerfScope.CharacterTargetingReachabilityCacheStore,
        MainThreadPerfScope.CharacterTargetingWallDetour,
        MainThreadPerfScope.FlowAttackAreaSetup,
        MainThreadPerfScope.FlowAttackAreaScan,
        MainThreadPerfScope.EntityBrainCombat,
        MainThreadPerfScope.FlowCombatApproach,
        MainThreadPerfScope.FlowCombatApproachOccupancy,
        MainThreadPerfScope.FlowCombatApproachCoreSetup,
        MainThreadPerfScope.FlowCombatApproachSlotCache,
        MainThreadPerfScope.FlowCombatApproachSlotCacheBuild,
        MainThreadPerfScope.FlowCombatApproachScore,
        MainThreadPerfScope.FlowCombatApproachExpanded,
        MainThreadPerfScope.FlowCombatApproachFinalize,
        MainThreadPerfScope.FlowCombatApproachPreResolve,
        MainThreadPerfScope.FlowCombatApproachCore,
        MainThreadPerfScope.FlowCombatApproachCoreAgentPreparation,
        MainThreadPerfScope.FlowCombatApproachDirectionSetup,
        MainThreadPerfScope.FlowCombatApproachSlotGenerate,
        MainThreadPerfScope.FlowCombatApproachSlotClearance,
        MainThreadPerfScope.FlowCombatApproachSlotLineOfSight,
        MainThreadPerfScope.FlowCombatApproachSlotDeduplicate,
        MainThreadPerfScope.FlowCombatApproachOccupancyReservations,
        MainThreadPerfScope.FlowCombatApproachOccupancyBuckets,
        MainThreadPerfScope.FlowCombatApproachOccupancyCandidate,
        MainThreadPerfScope.FlowCombatApproachOccupancyBucketBuild,
        MainThreadPerfScope.FlowCombatApproachOccupancyQuerySetup,
        MainThreadPerfScope.FlowCombatApproachScoreArithmetic,
        MainThreadPerfScope.FlowNavigationPathPolicyCommit,
        MainThreadPerfScope.FlowNavigationPathRequestRemoval,
        MainThreadPerfScope.FlowNavigationPathBudgetAccounting,
        MainThreadPerfScope.FlowNavigationPathWorldActivation,
        MainThreadPerfScope.FlowNavigationPathQueueEligibility,
        MainThreadPerfScope.FlowNavigationPathSliceDispatch,
        MainThreadPerfScope.FlowNavigationPathSliceDispatchUnattributed,
        MainThreadPerfScope.FlowNavigationPathSliceDispatchPreparation,
        MainThreadPerfScope.FlowNavigationPathSliceDispatchFinalization,
        MainThreadPerfScope.FlowNavigationPathQueueLoopUnattributed,
        MainThreadPerfScope.FlowCombatApproachSlotCacheLookup,
        MainThreadPerfScope.FlowNavigationPathWorldStateEnumeration,
        MainThreadPerfScope.FlowNavigationPathQueueProcessingUnattributed,
        MainThreadPerfScope.FlowNavigationPathWorldOuterUnattributed,
        MainThreadPerfScope.FlowNavigationPathQueueCommitLoop,
        MainThreadPerfScope.FlowCombatApproachCoreScorePhase,
        MainThreadPerfScope.FlowCombatApproachCoreExpandedPhase,
        MainThreadPerfScope.FlowCombatApproachCoreUnattributed,
        MainThreadPerfScope.FlowNavigationPathWorldRestore,
        MainThreadPerfScope.FlowCombatApproachCoreScoreUnattributed,
        MainThreadPerfScope.FlowNavigationPathWorldBody,
        MainThreadPerfScope.FlowCombatApproachCoreBody,
        MainThreadPerfScope.FlowCombatApproachOccupancyPreparation,
        MainThreadPerfScope.FlowCombatApproachOccupancySnapshot,
        MainThreadPerfScope.FlowCombatApproachOccupancySlotFilter,
        MainThreadPerfScope.FlowCombatApproachSlotCacheArrayMaterialize,
        MainThreadPerfScope.FlowCombatApproachSlotCachePublish,
        MainThreadPerfScope.FlowCombatApproachOccupancyPrepareIteration,
        MainThreadPerfScope.FlowNavigationPathDiagnosticCapture,
        MainThreadPerfScope.FlowNavigationPathBudgetEnd,
        MainThreadPerfScope.FlowCombatApproachCoreScoreIslandFilter,
        MainThreadPerfScope.FlowCombatApproachOccupancyBucketPosition,
        MainThreadPerfScope.FlowCombatApproachOccupancyBucketTarget,
        MainThreadPerfScope.FlowCombatApproachOccupancyBucketThreshold,
        MainThreadPerfScope.FlowNavigationDemandSectorValidation,
        MainThreadPerfScope.FlowNavigationDemandSourceBinding,
        MainThreadPerfScope.FlowNavigationDemandSnapshotPublish,
        MainThreadPerfScope.FlowNavigationPathSearchSchedule,
        MainThreadPerfScope.FlowNavigationPathSearchComplete,
        MainThreadPerfScope.FlowNavigationPathSearchCommandExecute,
        MainThreadPerfScope.FlowCombatApproachOccupancyAgentSync,
        MainThreadPerfScope.FlowCombatApproachOccupancyBucketFill,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorLookup,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorDetach,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorRebind,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorPublish,
        MainThreadPerfScope.FlowMovingTargetPolicyClassification,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorState,
        MainThreadPerfScope.FlowMovingTargetPolicyFinalGoalResolution,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorDictionary,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorGoalState,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorNavState,
        MainThreadPerfScope.FlowNavigationPathInitializeSameSector,
        MainThreadPerfScope.FlowNavigationPathInitializePolicy,
        MainThreadPerfScope.FlowNavigationPathInitializeHierarchyResolve,
        MainThreadPerfScope.FlowNavigationPathInitializeHierarchySelection,
        MainThreadPerfScope.FlowNavigationDemandQueueLookup,
        MainThreadPerfScope.FlowNavigationDemandQueueCreate,
        MainThreadPerfScope.FlowNavigationDemandSourceLookup,
        MainThreadPerfScope.FlowNavigationDemandSourceInsert,
        MainThreadPerfScope.FlowCombatApproachOccupancyPrepareGuard,
        MainThreadPerfScope.FlowCombatApproachOccupancyPrepareSlotLookup,
        MainThreadPerfScope.FlowCombatApproachOccupancyBucketIncremental,
        MainThreadPerfScope.FlowCombatApproachOccupancyPrepareInitialization,
        MainThreadPerfScope.FlowCombatApproachOccupancyPrepareCacheState,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorTargetResolve,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorKeyBuild,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorDictionaryLookup,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorDictionaryCreate,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorDictionaryInsert,
        MainThreadPerfScope.FlowMovingTargetPolicyBatchResolutionLookup,
        MainThreadPerfScope.FlowMovingTargetPolicyBatchStatePublish,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorProjectionCacheCheck,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorProjectionIslandResolve,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorProjectionRequest,
        MainThreadPerfScope.FlowMovingTargetPolicyInputResolution,
        MainThreadPerfScope.FlowMovingTargetPolicyBatchContinuation,
        MainThreadPerfScope.FlowMovingTargetPolicyBatchEarlyPath,
        MainThreadPerfScope.FlowMovingTargetPolicyRawGoalPath,
        MainThreadPerfScope.FlowMovingTargetPolicyOutputInitialization,
        MainThreadPerfScope.FlowCombatApproachScoreAngle,
        MainThreadPerfScope.FlowCombatApproachScoreSelfDistance,
        MainThreadPerfScope.FlowCombatApproachScoreTargetDistance,
        MainThreadPerfScope.FlowCombatApproachScoreSelection,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorRebindBuild,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorRebindBoundary,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorRebindImport,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorRebindShift,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorRebindDispose,
        MainThreadPerfScope.FlowNavigationPathMaterializeImmutableSectorCopy,
        MainThreadPerfScope.FlowNavigationPathMaterializeImmutablePortalCopy,
        MainThreadPerfScope.FlowNavigationPathMaterializeImmutableConcat,
        MainThreadPerfScope.FlowNavigationPathMaterializePublishWitness,
        MainThreadPerfScope.FlowNavigationPathMaterializePublishCache,
        MainThreadPerfScope.FlowNavigationPathMaterializePublishTrim,
        MainThreadPerfScope.FlowNavigationPathMaterializePublishHandle,
        MainThreadPerfScope.FlowNavigationPathMaterializePublishSuffix,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorRebindSectorAccess,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorRebindSectorShift,
        MainThreadPerfScope.FlowNavigationPathMaterializeImmutableHash,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorRebindSectorValidation,
        MainThreadPerfScope.FlowMovingTargetPolicyAnchorReplacementDispose,
        MainThreadPerfScope.FlowNavigationPathMaterializeSharedSuffixMerge,
        MainThreadPerfScope.FlowNavigationPathMaterializeStageInitialize,
        MainThreadPerfScope.FlowNavigationPathMaterializeStageStartPortal,
        MainThreadPerfScope.FlowNavigationPathMaterializeStageDownward,
        MainThreadPerfScope.FlowNavigationPathMaterializeStagePolicy,
        MainThreadPerfScope.FlowNavigationPathMaterializeStageGoalConnector,
        MainThreadPerfScope.FlowNavigationPathMaterializeStageConversion,
        MainThreadPerfScope.FlowNavigationPathMaterializeStageImmutableCopy,
        MainThreadPerfScope.FlowNavigationPathMaterializeStageHash,
        MainThreadPerfScope.FlowNavigationPathMaterializeStagePublish,
        MainThreadPerfScope.FlowNavigationPathMaterializeImmutableValidation,
        MainThreadPerfScope.FlowNavigationPathMaterializeImmutableWitnessHasher,
        MainThreadPerfScope.FlowNavigationPathMaterializePublishWitnessValidation,
        MainThreadPerfScope.FlowNavigationPathMaterializePublishWitnessMutation,
        MainThreadPerfScope.FlowNavigationPathMaterializePublishSuffixResolve,
        MainThreadPerfScope.FlowNavigationPathMaterializePublishSuffixValidation,
        MainThreadPerfScope.FlowNavigationPathMaterializePublishSuffixInsert,
        MainThreadPerfScope.FlowNavigationPathMaterializePublishSuffixBookkeeping,
        MainThreadPerfScope.FlowNavigationPathMaterializePublishSuffixFinalize,
        MainThreadPerfScope.FlowMovingTargetPolicyDisposeSearchState,
        MainThreadPerfScope.FlowMovingTargetPolicyDisposeConnectors,
        MainThreadPerfScope.FlowMovingTargetPolicyDisposeHierarchyPolicies,
        MainThreadPerfScope.FlowMovingTargetPolicyDisposeHierarchyCustomizations,
        MainThreadPerfScope.FlowMovingTargetPolicyDisposeHierarchySearchStates,
        MainThreadPerfScope.FlowMovingTargetPolicyDisposeDownwardSearchStates,
        MainThreadPerfScope.FlowMovingTargetPolicyDisposeHierarchyCustomizationClear,
        MainThreadPerfScope.FlowMovingTargetPolicyDisposeHierarchyPolicyClear,
    };
    private static readonly double[] s_ChaseScopePeakMilliseconds = new double[s_ChaseScopes.Length];
    private static readonly int[] s_ChaseScopePeakRenderFrames = new int[s_ChaseScopes.Length];
    private static readonly int[] s_ChaseScopePeakCalls = new int[s_ChaseScopes.Length];
    private static readonly List<double> s_ApproachFrameMilliseconds = new List<double>(1024);
    private static readonly List<double> s_ApproachLogicMilliseconds = new List<double>(1024);
    private static readonly List<double> s_RetreatFrameMilliseconds = new List<double>(1024);
    private static readonly List<double> s_RetreatLogicMilliseconds = new List<double>(1024);
    private static readonly Dictionary<int, FixVector2> s_LastEnemyMotionPositions = new Dictionary<int, FixVector2>();
    private static long s_LastEditorUpdateTimestamp;
    private static ulong s_LastEnemyMotionFrame;
    private static int s_LastProfilerFrame = -1;
    private static int s_LastPeriodicSampleFrame = -1;
    private static double s_MaxFrameMilliseconds;
    private static int s_MaxFrame;
    private static double s_MaxLogicMilliseconds;
    private static int s_MaxLogicFrame;
    private static double s_MaxLogicTickMilliseconds;
    private static int s_MaxLogicTickRenderFrame;
    private static ulong s_MaxLogicTickLogicFrame;
    private static ulong s_MaxLogicTickWarmLogicFrame;
    private static readonly double[] s_MaxLogicTickScopeMilliseconds = new double[(int)MainThreadPerfScope.Count];
    private static readonly double[] s_MaxLogicTickScopeRecordFirstMilliseconds = new double[(int)MainThreadPerfScope.Count];
    private static readonly double[] s_MaxLogicTickScopeRecordHotAverageMilliseconds = new double[(int)MainThreadPerfScope.Count];
    private static readonly int[] s_MaxLogicTickScopeRecordCounts = new int[(int)MainThreadPerfScope.Count];
    private static string s_MaxLogicTickInvocationEvidence = string.Empty;

    private readonly struct TickTimingMetric
    {
        public TickTimingMetric(double maxTickMs, double firstMs, double hotAverageMs, string hotAverageSource = "derived")
        {
            MaxTickMs = maxTickMs;
            FirstMs = firstMs;
            HotAverageMs = hotAverageMs;
            HotAverageSource = hotAverageSource;
        }

        public double MaxTickMs { get; }
        public double FirstMs { get; }
        public double HotAverageMs { get; }
        public string HotAverageSource { get; }
        public double RawColdGapMs => FirstMs - HotAverageMs;
        public double ColdGapMs => RawColdGapMs;
    }
    private static string s_NavigationBeforeInvade = string.Empty;
    private static string s_NavigationAfterInvade = string.Empty;
    private static string s_LastRetreatDirection = string.Empty;
    private static bool s_CaptureActive;
    private static int s_PeakRequiredFlowTileCommits;
    private static int s_PeakFlowTileQueueMutations;
    private static int s_PeakPathPortalExpansions;
    private static int s_PeakPathRequestGroups;
    private static int s_PeakPathRequestOperations;
    private static int s_PeakPathRequestCommits;
    private static int s_PeakPathRequestSourceCommits;
    private static int s_PeakPendingPathRequestGroups;
    private static int s_PeakPendingPathRequestSources;
    private static int s_PortalTileWaitAccessSamples;
    private static int s_PortalTileWaitZeroVelocitySamples;
    private static int s_PortalTileWaitStoppedSamples;

    private enum RunnerState
    {
        WaitingForPlay = 0,
        WaitingForStartup = 1,
        WaitingForRuntime = 2,
        Baseline = 3,
        WaitingForInvade = 4,
        WaitingForTarget = 5,
        Running = 6,
        Finishing = 7,
        WaitingForBuild = 8,
        WaitingForDefense = 9,
        WaitingForBuildInvade = 10,
        WaitingForBuildBeforeDefend = 11,
    }

    private enum ScenarioMode
    {
        Approach = 0,
        Retreat = 1,
    }

    static Lv2PullChasePerformanceRunner()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
    }

    [MenuItem("Tools/Logic Frames/Run Lv2 Pull Chase Performance")]
    public static void Run()
    {
        Start(false, false);
    }

    [MenuItem("Tools/Logic Frames/Run Lv2 Pull Chase Performance (Prewarm)")]
    public static void RunPrewarmed()
    {
        Start(false, true);
    }

    [MenuItem("Tools/Logic Frames/Run Lv2 RuntimeDirty Build Performance")]
    public static void RunRuntimeDirtyBuildPerformance()
    {
        Start(true, false);
    }

    private static void Start(bool buildScenario, bool prewarm)
    {
        if (SessionState.GetBool(RunningKey, false))
            throw new InvalidOperationException("Lv2 pull-chase performance runner is already running.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before starting the Lv2 pull-chase performance runner.");
        if (EditorApplication.isCompiling)
            throw new InvalidOperationException("Wait for script compilation before starting the Lv2 pull-chase performance runner.");
        if (EditorSceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Save or discard the dirty scene before starting the Lv2 pull-chase performance runner.");

        ResetCaptureState();
        string startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        SessionState.SetBool(RunningKey, true);
        SessionState.SetBool(BuildScenarioKey, buildScenario);
        SessionState.SetBool(PrewarmKey, prewarm);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForPlay);
        SessionState.SetString(StartedUtcKey, startedUtc);
        MainThreadFrameProfilerNextPlayCapture.ArmNextPlay(false);
        WriteResult(
            buildScenario ? BuildResultRelativePath : ResultRelativePath,
            "RESULT=RUNNING" + Environment.NewLine + "startedUtc=" + startedUtc + Environment.NewLine);
        EditorSceneManager.OpenScene(LaunchScenePath, OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }

    private static void Update()
    {
        if (!SessionState.GetBool(RunningKey, false))
            return;

        try
        {
            ValidateTimeout();
            CaptureCompletedFrame();
            ValidateLogicEntityChain();
            RunnerState state = (RunnerState)SessionState.GetInt(StateKey, (int)RunnerState.WaitingForPlay);
            if (!EditorApplication.isPlaying)
            {
                if (state == RunnerState.Finishing)
                {
                    SessionState.SetBool(RunningKey, false);
                    SessionState.EraseInt(StateKey);
                }
                else if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    throw new InvalidOperationException($"Lv2 pull-chase runner left Play mode unexpectedly. state={state}.");
                }
                return;
            }

            switch (state)
            {
                case RunnerState.WaitingForPlay:
                    SessionState.SetInt(StateKey, (int)RunnerState.WaitingForStartup);
                    break;
                case RunnerState.WaitingForStartup:
                    EnterLv2();
                    break;
                case RunnerState.WaitingForRuntime:
                    BeginBaselineWhenReady();
                    break;
                case RunnerState.Baseline:
                    AdvanceBaseline();
                    break;
                case RunnerState.WaitingForInvade:
                    WaitForInvade();
                    break;
                case RunnerState.WaitingForBuild:
                    WaitForBuild();
                    break;
                case RunnerState.WaitingForBuildInvade:
                    WaitForBuildInvade();
                    break;
                case RunnerState.WaitingForBuildBeforeDefend:
                    WaitForBuildBeforeDefend();
                    break;
                case RunnerState.WaitingForDefense:
                    WaitForDefense();
                    break;
                case RunnerState.WaitingForTarget:
                    BeginChaseWhenReady();
                    break;
                case RunnerState.Running:
                    AdvanceChase();
                    break;
                case RunnerState.Finishing:
                    break;
                default:
                    throw new InvalidOperationException($"Unknown Lv2 pull-chase runner state {state}.");
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
        finally
        {
            s_LastEditorUpdateTimestamp = Stopwatch.GetTimestamp();
        }
    }

    private static void EnterLv2()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase)
            return;
        if (!EditorRuntimeLevelEntry.TryEnterWithDefaultCareer("Lv_2", out string error))
            throw new InvalidOperationException($"Cannot enter Lv_2 from Launch: {error}");
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForRuntime);
    }

    private static void BeginBaselineWhenReady()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase runtimeProcedure
            || !runtimeProcedure.IsEditorStressRuntimeReady
            || !LogicFrameRuntime.IsTimelineRunning)
            return;

        InputManager inputManager = GameEntry.GetComponent<InputManager>()
                                    ?? throw new InvalidOperationException("Lv2 pull-chase runner requires InputManager.");
        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("Lv2 pull-chase runner requires InputModel.");
        if (!inputModel.LogicTimeline.IsStarted)
            return;
        if (PhaseManager.CurrentPhase != GamePhase.BuildBeforeInvade)
            throw new InvalidOperationException($"Lv2 pull-chase runner requires BuildBeforeInvade, actual={PhaseManager.CurrentPhase}.");

        inputManager.ChangeState(InputState.Game);
        if (SessionState.GetBool(PrewarmKey, false))
            PrewarmStableGoalMethod();
        ulong frame = LogicFrameRuntime.CurrentFrame;
        SessionState.SetInt(BaselineStartFrameKey, ToSessionInt(frame));
        SessionState.SetInt(StateKey, (int)RunnerState.Baseline);
        AppendEvent("baseline-begin", frame, null);
    }

    private static void AdvanceBaseline()
    {
        ulong frame = LogicFrameRuntime.CurrentFrame;
        ulong startFrame = FromSessionInt(BaselineStartFrameKey);
        if (frame < startFrame + BaselineTicks)
            return;

        s_NavigationBeforeInvade = FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics();
        if (LogicPhaseCommandService.PendingCount != 0)
            throw new InvalidOperationException($"Lv2 pull-chase runner found {LogicPhaseCommandService.PendingCount} pending phase commands before invade.");

        if (SessionState.GetBool(BuildScenarioKey, false))
        {
            ScheduleDDoSConstruction(frame);
            SessionState.SetInt(StateKey, (int)RunnerState.WaitingForBuild);
            AppendEvent("ddos-build-scheduled", frame, FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics());
            BeginPerformanceWindow();
            return;
        }

        PhaseManager.SwitchToPhase(GamePhase.Invade);
        SessionState.SetInt(InvadeScheduledFrameKey, ToSessionInt(frame));
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForInvade);
        AppendEvent("invade-scheduled", frame, s_NavigationBeforeInvade);
    }

    private static void WaitForInvade()
    {
        ulong frame = LogicFrameRuntime.CurrentFrame;
        ulong scheduledFrame = FromSessionInt(InvadeScheduledFrameKey);
        if (PhaseManager.CurrentPhase != GamePhase.Invade)
        {
            if (frame > scheduledFrame + 5)
                throw new InvalidOperationException($"Lv2 invade did not apply within five ticks. scheduled={scheduledFrame}, current={frame}.");
            return;
        }

        s_NavigationAfterInvade = FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics();
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForTarget);
        AppendEvent("invade-applied", frame, s_NavigationAfterInvade);
    }

    private static void PrewarmStableGoalMethod()
    {
        MethodInfo method = typeof(FlowFieldCrowdMovementSystem).GetMethod(
            "TryResolveStableGoalCellFixed",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method == null)
            throw new InvalidOperationException("Prewarm could not find TryResolveStableGoalCellFixed.");
        RuntimeHelpers.PrepareMethod(method.MethodHandle);
        AppendEvent("stable-goal-method-prewarmed", LogicFrameRuntime.CurrentFrame, method.MethodHandle.GetFunctionPointer().ToInt64().ToString(CultureInfo.InvariantCulture));
    }

    private static void ScheduleDDoSConstruction(ulong currentFrame)
    {
        BuildManager buildManager = UnityGameFramework.Runtime.GameEntry.GetComponent<BuildManager>()
                                    ?? throw new InvalidOperationException("Lv2 RuntimeDirty build runner requires BuildManager.");
        IList<IEntityContext> entities = EntityRegistry.AllEntities
                                         ?? throw new InvalidOperationException("Lv2 RuntimeDirty build runner requires EntityRegistry.AllEntities.");
        IBuildingLogicContext selectedOwner = null;
        string constructionDiagnostics = string.Empty;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i]
                                    ?? throw new InvalidOperationException($"Lv2 RuntimeDirty build runner found a null entity at index {i}.");
            if (!entity.Alive
                || !entity.TryGetLogicBuilding(out IBuildingLogicContext owner)
                || owner.OwnerFactionId != EntitySideHelper.PlayerFactionId
                || owner.BuildingData == null
                || owner.BuildingData.Lv != 0)
            {
                continue;
            }

            List<BuildingData> candidates = buildManager.GetLv0ConstructCandidates(owner, requireUnlockedArche: false);
            constructionDiagnostics += $" owner={owner.LogicEntityId.Value}/{owner.BuildingData.Identifier} candidates={candidates.Count}";
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                BuildingData candidate = candidates[candidateIndex];
                if (candidate == null)
                    throw new InvalidOperationException($"Lv2 RuntimeDirty build runner encountered a null candidate for owner {owner.LogicEntityId.Value} at index {candidateIndex}.");
                constructionDiagnostics += $" [{candidate.Identifier}:visible={buildManager.IsConstructOptionVisible(owner, candidate.Identifier)},condition={buildManager.SatisfyBuildCondition(candidate, owner.OwnerFactionId)},affordable={buildManager.HasBuildCost(candidate.Identifier, owner)}]";
                if (!string.Equals(candidate.Identifier, RuntimeDirtyBuildBuildingId, StringComparison.Ordinal))
                    continue;
                if (!buildManager.IsConstructOptionExecutable(owner, candidate.Identifier))
                {
                    constructionDiagnostics += $" ddosExecutable=false cost={buildManager.GetBuildingCost(candidate, owner)}";
                    continue;
                }
                selectedOwner = owner;
                break;
            }
            if (selectedOwner != null)
                break;
        }

        if (selectedOwner == null)
        {
            throw new InvalidOperationException(
                $"Lv2 RuntimeDirty build runner found no executable {RuntimeDirtyBuildBuildingId} option." + constructionDiagnostics);
        }
        if (!buildManager.ConstructBuilding(selectedOwner, RuntimeDirtyBuildBuildingId))
            throw new InvalidOperationException($"Lv2 RuntimeDirty build runner failed to submit {RuntimeDirtyBuildBuildingId} construction.");

        SessionState.SetInt(BuildScheduledFrameKey, ToSessionInt(currentFrame));
    }

    private static void WaitForBuild()
    {
        ulong frame = LogicFrameRuntime.CurrentFrame;
        ulong scheduledFrame = FromSessionInt(BuildScheduledFrameKey);
        IEntityContext built = ResolvePlayerBuilding(RuntimeDirtyBuildBuildingId);
        if (built == null)
        {
            if (frame > scheduledFrame + 180)
                throw new InvalidOperationException($"Lv2 RuntimeDirty build runner did not observe {RuntimeDirtyBuildBuildingId} after 180 ticks. frame={frame}.");
            return;
        }

        if (SessionState.GetInt(BuildAppliedFrameKey, 0) == 0)
        {
            SessionState.SetInt(BuildAppliedFrameKey, ToSessionInt(frame));
            AppendEvent("ddos-build-applied", frame, $"entity={built.LogicEntityId.Value}/{built.CharacterKey}");
        }

        if (FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty())
            return;

        if (PhaseManager.CurrentPhase != GamePhase.BuildBeforeInvade)
            throw new InvalidOperationException($"Lv2 RuntimeDirty build runner expected BuildBeforeInvade before invade, actual={PhaseManager.CurrentPhase}.");
        PhaseManager.SwitchToPhase(GamePhase.Invade);
        SessionState.SetInt(InvadeScheduledFrameKey, ToSessionInt(frame));
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForBuildInvade);
        AppendEvent("invade-after-build-scheduled", frame, FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics());
    }

    private static void WaitForBuildInvade()
    {
        ulong frame = LogicFrameRuntime.CurrentFrame;
        ulong scheduledFrame = FromSessionInt(InvadeScheduledFrameKey);
        if (PhaseManager.CurrentPhase != GamePhase.Invade)
        {
            if (frame > scheduledFrame + 30)
                throw new InvalidOperationException($"Lv2 RuntimeDirty build runner invade-after-build did not apply within 30 ticks. scheduled={scheduledFrame}, current={frame}.");
            return;
        }

        PhaseManager.SwitchToPhase(GamePhase.BuildBeforeDefend);
        SessionState.SetInt(BuildBeforeDefendScheduledFrameKey, ToSessionInt(frame));
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForBuildBeforeDefend);
        AppendEvent("build-before-defend-scheduled", frame, FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics());
    }

    private static void WaitForBuildBeforeDefend()
    {
        ulong frame = LogicFrameRuntime.CurrentFrame;
        ulong scheduledFrame = FromSessionInt(BuildBeforeDefendScheduledFrameKey);
        if (PhaseManager.CurrentPhase != GamePhase.BuildBeforeDefend)
        {
            if (frame > scheduledFrame + 30)
                throw new InvalidOperationException($"Lv2 RuntimeDirty build runner BuildBeforeDefend did not apply within 30 ticks. scheduled={scheduledFrame}, current={frame}.");
            return;
        }

        PhaseManager.SwitchToPhase(GamePhase.Defend);
        SessionState.SetInt(DefenseScheduledFrameKey, ToSessionInt(frame));
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForDefense);
        AppendEvent("defense-scheduled", frame, FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics());
    }

    private static void WaitForDefense()
    {
        ulong frame = LogicFrameRuntime.CurrentFrame;
        ulong scheduledFrame = FromSessionInt(DefenseScheduledFrameKey);
        if (PhaseManager.CurrentPhase != GamePhase.Defend)
        {
            if (frame > scheduledFrame + 30)
                throw new InvalidOperationException($"Lv2 RuntimeDirty build runner defense switch did not apply within 30 ticks. scheduled={scheduledFrame}, actual={PhaseManager.CurrentPhase}.");
            return;
        }
        if (SessionState.GetInt(DefenseAppliedFrameKey, 0) == 0)
        {
            SessionState.SetInt(DefenseAppliedFrameKey, ToSessionInt(frame));
            AppendEvent("defense-applied", frame, FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics());
        }
        if (frame < FromSessionInt(DefenseAppliedFrameKey) + 30)
            return;
        PassBuild(frame);
    }

    private static IEntityContext ResolvePlayerBuilding(string buildingId)
    {
        IList<IEntityContext> entities = EntityRegistry.AllEntities
                                         ?? throw new InvalidOperationException("Lv2 RuntimeDirty build runner requires EntityRegistry.AllEntities.");
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i]
                                    ?? throw new InvalidOperationException($"Lv2 RuntimeDirty build runner found a null entity at index {i}.");
            if (entity.Alive
                && entity.TryGetLogicBuilding(out IBuildingLogicContext building)
                && building.OwnerFactionId == EntitySideHelper.PlayerFactionId
                && building.BuildingData != null
                && string.Equals(building.BuildingData.Identifier, buildingId, StringComparison.Ordinal))
            {
                return entity;
            }
        }
        return null;
    }

    private static void BeginChaseWhenReady()
    {
        IEntityContext hero = EntityRegistry.Player;
        if (hero == null || !hero.Alive)
            return;
        if (hero is not ILogicFrameEntity logicHero)
            throw new InvalidOperationException("Lv2 hero does not implement ILogicFrameEntity.");

        IEntityContext target = ResolveNearestEnemySoldier(hero.PositionFixed);
        if (target == null)
        {
            ulong scheduledFrame = FromSessionInt(InvadeScheduledFrameKey);
            if (LogicFrameRuntime.CurrentFrame > scheduledFrame + 180)
                throw new InvalidOperationException("Lv2 pull-chase runner found no live enemy soldier within 180 ticks of invade.");
            return;
        }

        bool pending;
        if (!FlowFieldCrowdMovementSystem.TryGetNavigationPathCornersToReachableGoalNonBlocking(
                hero.Position,
                target.Position,
                logicHero.NavigationAgentTypeId,
                s_Route,
                out string failureReason,
                out pending))
        {
            if (pending)
                return;
            throw new InvalidOperationException($"Lv2 pull-chase route failed: {failureReason}");
        }
        if (s_Route.Count < 2)
            throw new InvalidOperationException($"Lv2 pull-chase route contains {s_Route.Count} coners.");

        ulong frame = LogicFrameRuntime.CurrentFrame;
        SessionState.SetInt(TargetEntityIdKey, target.LogicEntityId.Value);
        SessionState.SetInt(ModeKey, (int)ScenarioMode.Approach);
        SessionState.SetInt(ModeStartFrameKey, ToSessionInt(frame));
        SessionState.SetInt(WaypointIndexKey, 1);
        SessionState.SetInt(StateKey, (int)RunnerState.Running);
        BeginPerformanceWindow();
        AppendEvent("approach-begin", frame, $"target={DescribeEntity(target)},routeCorners={s_Route.Count}");
        EnqueueMove(ResolveRouteDirection(hero.Position, 1));
    }

    private static void AdvanceChase()
    {
        IEntityContext hero = EntityRegistry.Player
                              ?? throw new InvalidOperationException("Lv2 pull-chase runner lost the hero.");
        int targetId = SessionState.GetInt(TargetEntityIdKey, 0);
        if (!EntityRegistry.TryGet(new LogicEntityId(targetId), out IEntityContext target) || target == null || !target.Alive)
            throw new InvalidOperationException($"Lv2 pull-chase runner lost target {targetId} before capture completed.");

        ulong frame = LogicFrameRuntime.CurrentFrame;
        ScenarioMode mode = (ScenarioMode)SessionState.GetInt(ModeKey, (int)ScenarioMode.Approach);
        CaptureEnemyMotion(frame, hero, mode);
        int waypointIndex = SessionState.GetInt(WaypointIndexKey, 0);
        if (mode == ScenarioMode.Approach)
        {
            IEntityContext engagedTarget = ResolveNearestEnemyTargetingHero(hero);
            if (engagedTarget != null)
            {
                target = engagedTarget;
                SessionState.SetInt(TargetEntityIdKey, target.LogicEntityId.Value);
                mode = ScenarioMode.Retreat;
                waypointIndex = Math.Max(0, waypointIndex - 1);
                SessionState.SetInt(ModeKey, (int)mode);
                SessionState.SetInt(ModeStartFrameKey, ToSessionInt(frame));
                SessionState.SetInt(RetreatStartFrameKey, ToSessionInt(frame));
                SessionState.SetInt(WaypointIndexKey, waypointIndex);
                AppendEvent("retreat-begin", frame, DescribeChaseState(hero, target));
            }
        }

        if (mode == ScenarioMode.Retreat)
        {
            ulong retreatStart = FromSessionInt(RetreatStartFrameKey);
            if (frame >= retreatStart + RetreatTicks)
            {
                EnqueueMove(FixVector2.Zero);
                Pass(frame, hero, target);
                return;
            }
        }
        else if (frame > FromSessionInt(ModeStartFrameKey) + 900)
        {
            throw new InvalidOperationException($"Lv2 target did not acquire the hero within 900 approach ticks. {DescribeChaseState(hero, target)}");
        }

        FixVector2 direction = AdvanceRoute(hero.Position, mode, ref waypointIndex);
        SessionState.SetInt(WaypointIndexKey, waypointIndex);
        if (mode == ScenarioMode.Retreat && direction != FixVector2.Zero)
            s_LastRetreatDirection = $"({direction.x.RawValue},{direction.y.RawValue})";
        EnqueueMove(direction);
    }

    private static FixVector2 AdvanceRoute(Vector3 heroPosition, ScenarioMode mode, ref int waypointIndex)
    {
        if (waypointIndex < 0 || waypointIndex >= s_Route.Count)
            throw new InvalidOperationException($"Invalid route waypoint {waypointIndex}/{s_Route.Count}.");

        bool passedTerminalWaypoint = false;
        while (HasReachedOrPassedWaypoint(heroPosition, waypointIndex, mode))
        {
            if (mode == ScenarioMode.Approach && waypointIndex < s_Route.Count - 1)
            {
                waypointIndex++;
                continue;
            }
            if (mode == ScenarioMode.Retreat && waypointIndex > 0)
            {
                waypointIndex--;
                continue;
            }
            passedTerminalWaypoint = true;
            break;
        }

        if (mode == ScenarioMode.Retreat && waypointIndex == 0 && passedTerminalWaypoint)
            return ReadLastRetreatDirection();
        return ResolveRouteDirection(heroPosition, waypointIndex);
    }

    private static bool HasReachedOrPassedWaypoint(Vector3 heroPosition, int waypointIndex, ScenarioMode mode)
    {
        Vector3 waypoint = s_Route[waypointIndex];
        if (HorizontalDistance(heroPosition, waypoint) <= 0.08f)
            return true;

        int previousIndex = mode == ScenarioMode.Approach ? waypointIndex - 1 : waypointIndex + 1;
        if (previousIndex < 0 || previousIndex >= s_Route.Count)
            return false;

        Vector3 segment = waypoint - s_Route[previousIndex];
        segment.y = 0f;
        Vector3 beyondWaypoint = heroPosition - waypoint;
        beyondWaypoint.y = 0f;
        return segment.sqrMagnitude > 0.000001f && Vector3.Dot(beyondWaypoint, segment) >= 0f;
    }

    private static FixVector2 ReadLastRetreatDirection()
    {
        if (string.IsNullOrEmpty(s_LastRetreatDirection))
            throw new InvalidOperationException("Lv2 pull-chase runner reached route start without a retreat direction.");
        string[] raw = s_LastRetreatDirection.Trim('(', ')').Split(',');
        return new FixVector2(
            Fix64.FromRaw(long.Parse(raw[0], CultureInfo.InvariantCulture)),
            Fix64.FromRaw(long.Parse(raw[1], CultureInfo.InvariantCulture)));
    }

    private static FixVector2 ResolveRouteDirection(Vector3 heroPosition, int waypointIndex)
    {
        Vector3 waypoint = s_Route[waypointIndex];
        var delta = new FixVector2((Fix64)(waypoint.x - heroPosition.x), (Fix64)(waypoint.z - heroPosition.z));
        return FixVector2.SqrMagnitude(delta) > Fix64.Zero ? delta.GetNormalized() : FixVector2.Zero;
    }

    private static void EnqueueMove(FixVector2 direction)
    {
        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("Lv2 pull-chase runner lost InputModel.");
        inputModel.LogicTimeline.EnqueueEditorWorldMoveForNextFrame(direction);
    }

    private static IEntityContext ResolveNearestEnemySoldier(FixVector2 heroPosition)
    {
        IEntityContext nearest = null;
        Fix64 nearestDistanceSquared = Fix64.FromRaw(long.MaxValue);
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity == null || !entity.Alive || entity.Side != SideType.EnemySide || entity.Brain is not SoldierAIBrain)
                continue;
            Fix64 distanceSquared = FixVector2.SqrMagnitude(entity.PositionFixed - heroPosition);
            if (distanceSquared >= nearestDistanceSquared)
                continue;
            nearest = entity;
            nearestDistanceSquared = distanceSquared;
        }
        return nearest;
    }

    private static IEntityContext ResolveNearestEnemyTargetingHero(IEntityContext hero)
    {
        IEntityContext nearest = null;
        Fix64 nearestDistanceSquared = Fix64.FromRaw(long.MaxValue);
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity == null
                || !entity.Alive
                || entity.Side != SideType.EnemySide
                || entity.Brain is not SoldierAIBrain
                || !ReferenceEquals(entity.TargetComp?.CurrentTarget, hero))
                continue;
            Fix64 distanceSquared = FixVector2.SqrMagnitude(entity.PositionFixed - hero.PositionFixed);
            if (distanceSquared >= nearestDistanceSquared)
                continue;
            nearest = entity;
            nearestDistanceSquared = distanceSquared;
        }
        return nearest;
    }

    private static void BeginPerformanceWindow()
    {
        MainThreadFrameProfiler.ResetPerformanceWindow();
        Array.Clear(s_ChaseScopePeakMilliseconds, 0, s_ChaseScopePeakMilliseconds.Length);
        Array.Clear(s_ChaseScopePeakRenderFrames, 0, s_ChaseScopePeakRenderFrames.Length);
        Array.Clear(s_ChaseScopePeakCalls, 0, s_ChaseScopePeakCalls.Length);
        s_LastProfilerFrame = MainThreadFrameProfiler.LastCompletedFrame;
        s_LastPeriodicSampleFrame = s_LastProfilerFrame;
        s_MaxFrameMilliseconds = 0.0;
        s_MaxFrame = -1;
        s_MaxLogicMilliseconds = 0.0;
        s_MaxLogicFrame = -1;
        s_MaxLogicTickMilliseconds = 0.0;
        s_MaxLogicTickRenderFrame = -1;
        s_MaxLogicTickLogicFrame = 0;
        s_MaxLogicTickWarmLogicFrame = 0;
        Array.Clear(s_MaxLogicTickScopeMilliseconds, 0, s_MaxLogicTickScopeMilliseconds.Length);
        Array.Clear(s_MaxLogicTickScopeRecordFirstMilliseconds, 0, s_MaxLogicTickScopeRecordFirstMilliseconds.Length);
        Array.Clear(s_MaxLogicTickScopeRecordHotAverageMilliseconds, 0, s_MaxLogicTickScopeRecordHotAverageMilliseconds.Length);
        Array.Clear(s_MaxLogicTickScopeRecordCounts, 0, s_MaxLogicTickScopeRecordCounts.Length);
        s_ApproachFrameMilliseconds.Clear();
        s_ApproachLogicMilliseconds.Clear();
        s_RetreatFrameMilliseconds.Clear();
        s_RetreatLogicMilliseconds.Clear();
        s_PeakRequiredFlowTileCommits = 0;
        s_PeakFlowTileQueueMutations = 0;
        s_PeakPathPortalExpansions = 0;
        s_PeakPathRequestGroups = 0;
        s_PeakPathRequestOperations = 0;
        s_PeakPathRequestCommits = 0;
        s_PeakPathRequestSourceCommits = 0;
        s_PeakPendingPathRequestGroups = 0;
        s_PeakPendingPathRequestSources = 0;
        s_PortalTileWaitAccessSamples = 0;
        s_PortalTileWaitZeroVelocitySamples = 0;
        s_PortalTileWaitStoppedSamples = 0;
        s_CaptureActive = true;
    }

    private static void CaptureCompletedFrame()
    {
        if (!s_CaptureActive)
            return;
        DrainNavigationPathTickDiagnostics();
        int completedFrame = MainThreadFrameProfiler.LastCompletedFrame;
        if (completedFrame < 0 || completedFrame == s_LastProfilerFrame)
            return;
        s_LastProfilerFrame = completedFrame;

        double frameMs = MainThreadFrameProfiler.LastCompletedFrameMilliseconds;
        double logicMs = MainThreadFrameProfiler.LastCompletedLogicFrameMilliseconds;
        ScenarioMode mode = (ScenarioMode)SessionState.GetInt(ModeKey, (int)ScenarioMode.Approach);
        if (mode == ScenarioMode.Approach)
        {
            s_ApproachFrameMilliseconds.Add(frameMs);
            s_ApproachLogicMilliseconds.Add(logicMs);
        }
        else
        {
            s_RetreatFrameMilliseconds.Add(frameMs);
            s_RetreatLogicMilliseconds.Add(logicMs);
        }
        s_PeakRequiredFlowTileCommits = Math.Max(
            s_PeakRequiredFlowTileCommits,
            FlowFieldCrowdMovementSystem.GetEditorTestRequiredFlowTileCommitCount());
        s_PeakFlowTileQueueMutations = Math.Max(
            s_PeakFlowTileQueueMutations,
            FlowFieldCrowdMovementSystem.GetEditorTestFlowTileQueueMutationCount());
        s_PeakPathPortalExpansions = Math.Max(
            s_PeakPathPortalExpansions,
            FlowFieldCrowdMovementSystem.GetEditorTestFramePathPortalGraphNodeExpansionCount());
        int pathRequestGroups = -1;
        int pathRequestOperations = -1;
        int pathRequestCommits = -1;
        int pathRequestSourceCommits = -1;
        int pendingPathRequestGroups = FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationPathRequestCount();
        int pendingPathRequestSources = FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationPathSourceCount();
        int pathRequestOperationQuota = FlowFieldCrowdMovementSystem.GetEditorTestNavigationPathRequestOperationQuota();
        if (pathRequestOperations > pathRequestOperationQuota)
        {
            throw new InvalidOperationException(
                $"Lv2 pull-chase path request exceeded operation quota. operations={pathRequestOperations}, quota={pathRequestOperationQuota}.");
        }
        s_PeakPathRequestGroups = Math.Max(s_PeakPathRequestGroups, pathRequestGroups);
        s_PeakPathRequestOperations = Math.Max(s_PeakPathRequestOperations, pathRequestOperations);
        s_PeakPathRequestCommits = Math.Max(s_PeakPathRequestCommits, pathRequestCommits);
        s_PeakPathRequestSourceCommits = Math.Max(s_PeakPathRequestSourceCommits, pathRequestSourceCommits);
        s_PeakPendingPathRequestGroups = Math.Max(s_PeakPendingPathRequestGroups, pendingPathRequestGroups);
        s_PeakPendingPathRequestSources = Math.Max(s_PeakPendingPathRequestSources, pendingPathRequestSources);
        CaptureChaseScopePeaks(completedFrame);
        if (frameMs > s_MaxFrameMilliseconds)
        {
            s_MaxFrameMilliseconds = frameMs;
            s_MaxFrame = completedFrame;
        }
        if (logicMs > s_MaxLogicMilliseconds)
        {
            s_MaxLogicMilliseconds = logicMs;
            s_MaxLogicFrame = completedFrame;
        }
        double logicTickMs = MainThreadFrameProfiler.LastCompletedMaxLogicTickMilliseconds;
        if (logicTickMs > s_MaxLogicTickMilliseconds)
        {
            s_MaxLogicTickMilliseconds = logicTickMs;
            s_MaxLogicTickRenderFrame = completedFrame;
            s_MaxLogicTickLogicFrame = MainThreadFrameProfiler.LastCompletedMaxLogicTickFrame;
            for (int i = 0; i < s_ChaseScopes.Length; i++)
            {
                MainThreadPerfScope scope = s_ChaseScopes[i];
                s_MaxLogicTickScopeMilliseconds[(int)scope] =
                    MainThreadFrameProfiler.GetLastCompletedMaxLogicTickScopeMilliseconds(scope);
            }
            Array.Clear(s_MaxLogicTickScopeRecordFirstMilliseconds, 0, s_MaxLogicTickScopeRecordFirstMilliseconds.Length);
            Array.Clear(s_MaxLogicTickScopeRecordHotAverageMilliseconds, 0, s_MaxLogicTickScopeRecordHotAverageMilliseconds.Length);
            Array.Clear(s_MaxLogicTickScopeRecordCounts, 0, s_MaxLogicTickScopeRecordCounts.Length);
            for (int i = 0; i < (int)MainThreadPerfScope.Count; i++)
            {
                MainThreadPerfScope scope = (MainThreadPerfScope)i;
                int count = MainThreadFrameProfiler.GetLastCompletedMaxLogicTickScopeRecordCount(scope);
                s_MaxLogicTickScopeRecordCounts[i] = count;
                s_MaxLogicTickScopeRecordFirstMilliseconds[i] = MainThreadFrameProfiler.GetLastCompletedMaxLogicTickScopeRecordFirstMilliseconds(scope);
                double subsequentMs = MainThreadFrameProfiler.GetLastCompletedMaxLogicTickScopeRecordSubsequentMilliseconds(scope);
                s_MaxLogicTickScopeRecordHotAverageMilliseconds[i] = count > 1 ? subsequentMs / (count - 1) : 0.0;
            }
            s_MaxLogicTickInvocationEvidence = BuildCurrentMaxLogicTickInvocationReport();
        }

        bool hasCompletedLogicTickSnapshot = logicTickMs > 0.0
                                              && MainThreadFrameProfiler.LastCompletedMaxLogicTickFrame >= 0
                                              && MainThreadFrameProfiler.LastCompletedMaxLogicTickFrame <= int.MaxValue;
        int diagnosticLogicFrame = hasCompletedLogicTickSnapshot
            ? checked((int)MainThreadFrameProfiler.LastCompletedMaxLogicTickFrame)
            : -1;
        if (hasCompletedLogicTickSnapshot
            && !FlowFieldCrowdMovementSystem.HasEditorFlowPerfTickSnapshot(diagnosticLogicFrame))
        {
            hasCompletedLogicTickSnapshot = false;
        }
        if (hasCompletedLogicTickSnapshot)
        {
            pathRequestGroups = FlowFieldCrowdMovementSystem.GetEditorTestFrameNavigationPathRequestGroupCount(diagnosticLogicFrame);
            pathRequestOperations = FlowFieldCrowdMovementSystem.GetEditorTestFrameNavigationPathRequestOperationCount(diagnosticLogicFrame);
            pathRequestCommits = FlowFieldCrowdMovementSystem.GetEditorTestFrameNavigationPathRequestCommitCount(diagnosticLogicFrame);
            pathRequestSourceCommits = FlowFieldCrowdMovementSystem.GetEditorTestFrameNavigationPathSourceCommitCount(diagnosticLogicFrame);
        }

        bool periodic = completedFrame - s_LastPeriodicSampleFrame >= SampleIntervalRenderFrames;
        double pathHandleMilliseconds = MainThreadFrameProfiler.GetLastCompletedScopeMilliseconds(MainThreadPerfScope.FlowPreparePathHandle);
        double continuationMilliseconds = MainThreadFrameProfiler.GetLastCompletedScopeMilliseconds(MainThreadPerfScope.FlowTileCommitContinuation);
        bool pathSearchExpanded = hasCompletedLogicTickSnapshot
                                  && FlowFieldCrowdMovementSystem.GetEditorTestFramePathPortalGraphNodeExpansionCount(diagnosticLogicFrame) > 0;
        bool pathRequestAdvanced = hasCompletedLogicTickSnapshot
                                    && (pathRequestOperations > 0 || pathRequestCommits > 0);
        if (!periodic && !pathSearchExpanded && !pathRequestAdvanced && pathHandleMilliseconds < 0.5 && continuationMilliseconds < 0.5)
            return;
        if (periodic)
            s_LastPeriodicSampleFrame = completedFrame;

        string chase = string.Empty;
        if (EditorApplication.isPlaying && LogicFrameRuntime.IsActive)
        {
            IEntityContext hero = EntityRegistry.Player;
            int targetId = SessionState.GetInt(TargetEntityIdKey, 0);
            if (hero != null
                && targetId > 0
                && EntityRegistry.TryGet(new LogicEntityId(targetId), out IEntityContext target)
                && target != null)
            {
                chase = DescribeChaseState(hero, target);
            }
        }

        string navigation = EditorApplication.isPlaying
            ? FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics()
            : string.Empty;
        string snapshotState = hasCompletedLogicTickSnapshot
            ? $"logic={diagnosticLogicFrame}"
            : "unavailable";
        string pathSearch = EditorApplication.isPlaying && hasCompletedLogicTickSnapshot
            ? FlowFieldCrowdMovementSystem.GetEditorTestFramePathSearchDiagnostics(diagnosticLogicFrame)
            : $"snapshot={snapshotState}";
        string pathStages = EditorApplication.isPlaying && hasCompletedLogicTickSnapshot
            ? FlowFieldCrowdMovementSystem.GetEditorTestFrameNavigationPathStageDiagnostics(diagnosticLogicFrame)
            : $"snapshot={snapshotState}";
        string navigationSyncStages = EditorApplication.isPlaying && hasCompletedLogicTickSnapshot
            ? FlowFieldCrowdMovementSystem.GetEditorTestFrameNavigationSyncStageDiagnostics(diagnosticLogicFrame)
            : $"snapshot={snapshotState}";
        string combatApproach = EditorApplication.isPlaying && hasCompletedLogicTickSnapshot
            ? FlowFieldCrowdMovementSystem.GetEditorTestFrameCombatApproachDiagnostics(diagnosticLogicFrame)
            : $"snapshot={snapshotState}";
        string pathRequestSample = hasCompletedLogicTickSnapshot
            ? $"groups={pathRequestGroups},operations={pathRequestOperations},quota={pathRequestOperationQuota},commits={pathRequestCommits},sourceCommits={pathRequestSourceCommits}"
            : $"snapshot={snapshotState},quota={pathRequestOperationQuota}";
        string startConnectors = EditorApplication.isPlaying
            ? FlowFieldCrowdMovementSystem.GetEditorTestHierarchyStartConnectorDiagnostics(completedFrame)
            : string.Empty;
        double updateGapMs = s_LastEditorUpdateTimestamp == 0
            ? 0.0
            : (Stopwatch.GetTimestamp() - s_LastEditorUpdateTimestamp) * 1000.0 / Stopwatch.Frequency;
        s_Samples.Add(
            $"sample render={completedFrame},logic={LogicFrameRuntime.CurrentFrame},state={(RunnerState)SessionState.GetInt(StateKey, 0)}," +
            $"mode={(ScenarioMode)SessionState.GetInt(ModeKey, 0)},frameMs={frameMs:F3},trackedMs={MainThreadFrameProfiler.LastCompletedTrackedMilliseconds:F3}," +
            $"untrackedMs={MainThreadFrameProfiler.LastCompletedUntrackedMilliseconds:F3},logicMs={logicMs:F3},maxLogicTickMs={MainThreadFrameProfiler.LastCompletedMaxLogicTickMilliseconds:F3},maxLogicTickFrame={MainThreadFrameProfiler.LastCompletedMaxLogicTickFrame},editorGapMs={updateGapMs:F3}," +
            $"scopes=[{BuildChaseScopeSample()}],tickScopes=[{BuildCompletedMaxLogicTickScopeSample()}],pathRequests=[{pathRequestSample},pendingGroups={pendingPathRequestGroups},pendingSources={pendingPathRequestSources}]," +
            $"pathSearch=[{pathSearch}],pathStages=[{pathStages}],navigationSyncStages=[{navigationSyncStages}]," +
            $"combatApproach=[{combatApproach}]," +
            $"maxTickListeners=[{MainThreadFrameProfiler.GetLastCompletedMaxLogicTickListenerSummary()}]," +
            $"startConnectors=[{startConnectors}],chase=[{chase}],navigation=[{navigation}]");
    }

    private static void ValidateLogicEntityChain()
    {
        if (!EditorApplication.isPlaying
            || !LogicFrameRuntime.IsTimelineRunning
            || LogicFrameRuntime.CurrentFrame == 0)
        {
            return;
        }

        if (!MAEntityLogicFrameSystem.IsActive)
        {
            throw new InvalidOperationException(
                $"Lv2 pull-chase entity chain is inactive. logicFrame={LogicFrameRuntime.CurrentFrame}.");
        }

        MAEntityLogicFrameSystem.ValidateAndGetLatestCompletedFrame();
    }

    private static void CaptureChaseScopePeaks(int completedFrame)
    {
        for (int i = 0; i < s_ChaseScopes.Length; i++)
        {
            MainThreadPerfScope scope = s_ChaseScopes[i];
            double milliseconds = MainThreadFrameProfiler.GetLastCompletedScopeMilliseconds(scope);
            if (milliseconds <= s_ChaseScopePeakMilliseconds[i])
                continue;
            s_ChaseScopePeakMilliseconds[i] = milliseconds;
            s_ChaseScopePeakRenderFrames[i] = completedFrame;
            s_ChaseScopePeakCalls[i] = MainThreadFrameProfiler.GetLastCompletedScopeCalls(scope);
        }
    }

    private static string BuildChaseScopeSample()
    {
        var builder = new System.Text.StringBuilder(256);
        for (int i = 0; i < s_ChaseScopes.Length; i++)
        {
            int calls = MainThreadFrameProfiler.GetLastCompletedScopeCalls(s_ChaseScopes[i]);
            if (calls <= 0)
                continue;
            if (builder.Length > 0)
                builder.Append(',');
            builder.Append(s_ChaseScopes[i])
                .Append('=')
                .Append(MainThreadFrameProfiler.GetLastCompletedScopeMilliseconds(s_ChaseScopes[i]).ToString("F3", CultureInfo.InvariantCulture))
                .Append("ms/")
                .Append(calls);
        }
        return builder.Length > 0 ? builder.ToString() : "none";
    }

    private static string BuildChaseScopePeakReport()
    {
        var lines = new string[s_ChaseScopes.Length];
        for (int i = 0; i < s_ChaseScopes.Length; i++)
        {
            lines[i] = $"scopePeak name={s_ChaseScopes[i]},milliseconds={s_ChaseScopePeakMilliseconds[i].ToString("F3", CultureInfo.InvariantCulture)}," +
                       $"render={s_ChaseScopePeakRenderFrames[i]},calls={s_ChaseScopePeakCalls[i]}";
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildCompletedMaxLogicTickScopeSample()
    {
        var builder = new System.Text.StringBuilder(192);
        for (int i = 0; i < s_ChaseScopes.Length; i++)
        {
            MainThreadPerfScope scope = s_ChaseScopes[i];
            double milliseconds = MainThreadFrameProfiler.GetLastCompletedMaxLogicTickScopeMilliseconds(scope);
            if (milliseconds <= 0.001)
                continue;
            if (builder.Length > 0)
                builder.Append(',');
            builder.Append(scope).Append('=').Append(milliseconds.ToString("F3", CultureInfo.InvariantCulture));
        }
        return builder.Length > 0 ? builder.ToString() : "none";
    }

    private static string BuildMaxLogicTickScopeReport()
    {
        var lines = new string[s_ChaseScopes.Length];
        for (int i = 0; i < s_ChaseScopes.Length; i++)
        {
            MainThreadPerfScope scope = s_ChaseScopes[i];
            lines[i] = $"maxTickScope name={scope},milliseconds={s_MaxLogicTickScopeMilliseconds[(int)scope].ToString("F3", CultureInfo.InvariantCulture)}";
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildMaxLogicTickScopeRecordEvidence()
    {
        var lines = new List<string>();
        for (int i = 0; i < (int)MainThreadPerfScope.Count; i++)
        {
            MainThreadPerfScope scope = (MainThreadPerfScope)i;
            double maxMs = MainThreadPerfScopeMilliseconds(scope);
            if (maxMs < 0.1)
                continue;

            int count = s_MaxLogicTickScopeRecordCounts[i];
            double firstMs = s_MaxLogicTickScopeRecordFirstMilliseconds[i];
            double hotAverageMs = s_MaxLogicTickScopeRecordHotAverageMilliseconds[i];
            string hotAverageSource = "maxTick";
            if (count <= 1)
            {
                int overallCount = MainThreadFrameProfiler.GetScopeRecordCount(scope);
                if (overallCount <= 1)
                    throw new InvalidOperationException(
                        $"Max logic Tick scope has no hot comparison record. name={scope}, logicFrame={s_MaxLogicTickLogicFrame}, count={count}, overallCount={overallCount}.");
                double overallSubsequentMs = MainThreadFrameProfiler.GetScopeRecordSubsequentMilliseconds(scope);
                hotAverageMs = overallSubsequentMs / (overallCount - 1);
                hotAverageSource = "overall";
            }
            double coldGapMs = count > 0 ? firstMs - hotAverageMs : 0.0;
            ulong firstRecordLogicFrame = count > 0 ? s_MaxLogicTickLogicFrame : 0;
            bool firstInMaxTick = count > 0;
            lines.Add($"scopeRecord name={scope},maxTickMs={maxMs.ToString("F3", CultureInfo.InvariantCulture)},count={count},firstRecordLogicFrame={firstRecordLogicFrame},firstInMaxTick={firstInMaxTick},firstMs={firstMs.ToString("F3", CultureInfo.InvariantCulture)},hotAverageMs={hotAverageMs.ToString("F3", CultureInfo.InvariantCulture)},hotAverageSource={hotAverageSource},coldGapMs={coldGapMs.ToString("F3", CultureInfo.InvariantCulture)}");
        }
        return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "none";
    }

    private static double MainThreadPerfScopeMilliseconds(MainThreadPerfScope scope)
    {
        return s_MaxLogicTickScopeMilliseconds[(int)scope];
    }

    private static string BuildOverallScopeRecordEvidence()
    {
        var lines = new List<string>();
        for (int i = 0; i < (int)MainThreadPerfScope.Count; i++)
        {
            MainThreadPerfScope scope = (MainThreadPerfScope)i;
            int count = MainThreadFrameProfiler.GetScopeRecordCount(scope);
            double firstMs = MainThreadFrameProfiler.GetScopeRecordFirstMilliseconds(scope);
            double subsequentMs = MainThreadFrameProfiler.GetScopeRecordSubsequentMilliseconds(scope);
            double maxMs = MainThreadPerfScopeMilliseconds(scope);
            if (maxMs < 0.1 && firstMs < 0.1)
                continue;
            double hotAverageMs = count > 1 ? subsequentMs / (count - 1) : 0.0;
            double coldGapMs = count > 1 ? firstMs - hotAverageMs : 0.0;
            ulong firstFrame = MainThreadFrameProfiler.GetScopeRecordFirstLogicFrame(scope);
            bool firstInMaxTick = firstFrame != 0 && firstFrame == s_MaxLogicTickLogicFrame;
            lines.Add($"scopeOverall name={scope},maxTickMs={maxMs.ToString("F3", CultureInfo.InvariantCulture)},count={count},firstLogicFrame={firstFrame},firstInMaxTick={firstInMaxTick},firstMs={firstMs.ToString("F3", CultureInfo.InvariantCulture)},hotAverageMs={hotAverageMs.ToString("F3", CultureInfo.InvariantCulture)},coldGapMs={coldGapMs.ToString("F3", CultureInfo.InvariantCulture)}");
        }
        return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "none";
    }

    private static string BuildCurrentMaxLogicTickInvocationReport()
    {
        MainThreadPerfScope[] scopes =
        {
            MainThreadPerfScope.LogicEntityBrain,
            MainThreadPerfScope.EntityBrainCombat,
            MainThreadPerfScope.FlowCombatApproach,
            MainThreadPerfScope.FlowCombatApproachCore,
            MainThreadPerfScope.FlowCombatApproachCoreScorePhase,
            MainThreadPerfScope.FlowNavigationResolvePathQueueBatch,
            MainThreadPerfScope.FlowCombatApproachOccupancyPreparation,
        };
        var lines = new string[scopes.Length];
        for (int i = 0; i < scopes.Length; i++)
        {
            MainThreadPerfScope scope = scopes[i];
            int count = MainThreadFrameProfiler.GetLastCompletedMaxLogicTickInvocationCount(scope);
            double firstMs = MainThreadFrameProfiler.GetLastCompletedMaxLogicTickInvocationFirstMilliseconds(scope);
            double subsequentMs = MainThreadFrameProfiler.GetLastCompletedMaxLogicTickInvocationSubsequentMilliseconds(scope);
            double subsequentAverageMs = count > 1 ? subsequentMs / (count - 1) : 0.0;
            double firstGapMs = count > 1 ? firstMs - subsequentAverageMs : 0.0;
            lines[i] = $"invocation name={scope},count={count},firstMs={firstMs.ToString("F3", CultureInfo.InvariantCulture)}," +
                       $"subsequentTotalMs={subsequentMs.ToString("F3", CultureInfo.InvariantCulture)}," +
                       $"subsequentAverageMs={subsequentAverageMs.ToString("F3", CultureInfo.InvariantCulture)}," +
                       $"firstMinusSubsequentAverageMs={firstGapMs.ToString("F3", CultureInfo.InvariantCulture)}";
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildOverallInvocationReport()
    {
        MainThreadPerfScope[] scopes =
        {
            MainThreadPerfScope.LogicEntityBrain,
            MainThreadPerfScope.EntityBrainCombat,
            MainThreadPerfScope.FlowCombatApproach,
            MainThreadPerfScope.FlowCombatApproachCore,
            MainThreadPerfScope.FlowCombatApproachCoreScorePhase,
            MainThreadPerfScope.FlowNavigationResolvePathQueueBatch,
            MainThreadPerfScope.FlowCombatApproachOccupancyPreparation,
        };
        var lines = new string[scopes.Length];
        for (int i = 0; i < scopes.Length; i++)
        {
            MainThreadPerfScope scope = scopes[i];
            int count = MainThreadFrameProfiler.GetInvocationCount(scope);
            double firstMs = MainThreadFrameProfiler.GetInvocationFirstMilliseconds(scope);
            double subsequentMs = MainThreadFrameProfiler.GetInvocationSubsequentMilliseconds(scope);
            double subsequentAverageMs = count > 1 ? subsequentMs / (count - 1) : 0.0;
            double firstGapMs = count > 1 ? firstMs - subsequentAverageMs : 0.0;
            ulong firstFrame = MainThreadFrameProfiler.GetInvocationFirstLogicFrame(scope);
            bool firstInMaxTick = firstFrame != 0 && firstFrame == s_MaxLogicTickLogicFrame;
            lines[i] = $"invocation name={scope},count={count},firstLogicFrame={firstFrame},firstInMaxTick={firstInMaxTick},firstMs={firstMs.ToString("F3", CultureInfo.InvariantCulture)}," +
                       $"subsequentTotalMs={subsequentMs.ToString("F3", CultureInfo.InvariantCulture)}," +
                       $"subsequentAverageMs={subsequentAverageMs.ToString("F3", CultureInfo.InvariantCulture)}," +
                       $"firstMinusSubsequentAverageMs={firstGapMs.ToString("F3", CultureInfo.InvariantCulture)}";
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static TickTimingMetric GetMaxLogicTickTiming(MainThreadPerfScope scope)
    {
        int index = (int)scope;
        double maxTickMs = s_MaxLogicTickScopeMilliseconds[index];
        int count = s_MaxLogicTickScopeRecordCounts[index];
        if (maxTickMs > 0.0 && count <= 0)
        {
            throw new InvalidOperationException(
                $"Max logic Tick scope has a value without a record. name={scope}, maxTickMs={maxTickMs:F6}, logicFrame={s_MaxLogicTickLogicFrame}.");
        }

        if (count <= 0)
            return new TickTimingMetric(0.0, 0.0, 0.0);

        if (!MainThreadFrameProfiler.TryGetLogicTickHotScopeComparison(
                s_MaxLogicTickLogicFrame,
                scope,
                out ulong hotLogicFrame,
                out double hotMilliseconds,
                out int targetCalls,
                out int hotCalls))
        {
            throw new InvalidOperationException(
                $"Max logic Tick scope has no same-workload hot comparison. name={scope}, logicFrame={s_MaxLogicTickLogicFrame}, count={count}, targetCalls={targetCalls}, workloadSignature=0x{MainThreadFrameProfiler.GetLogicTickWorkloadSignature(s_MaxLogicTickLogicFrame):X16}.");
        }
        if (hotCalls != targetCalls)
        {
            throw new InvalidOperationException(
                $"Logic Tick hot comparison call count mismatch. name={scope}, targetLogicFrame={s_MaxLogicTickLogicFrame}, hotLogicFrame={hotLogicFrame}, targetCalls={targetCalls}, hotCalls={hotCalls}.");
        }

        return new TickTimingMetric(
            maxTickMs,
            maxTickMs,
            hotMilliseconds,
            $"logicTick={hotLogicFrame}");
    }

    private static TickTimingMetric SubtractTickTiming(
        string name,
        TickTimingMetric parent,
        params TickTimingMetric[] children)
    {
        double maxTickMs = parent.MaxTickMs;
        double coldGapMs = parent.ColdGapMs;
        for (int i = 0; i < children.Length; i++)
        {
            maxTickMs -= children[i].MaxTickMs;
            coldGapMs -= children[i].ColdGapMs;
        }

        if (maxTickMs < -0.002)
        {
            throw new InvalidOperationException(
                $"Max logic Tick leaf timing is inconsistent. name={name}, maxTickMs={maxTickMs:F6}, coldGapMs={coldGapMs:F6}.");
        }

        return new TickTimingMetric(maxTickMs, maxTickMs, maxTickMs - coldGapMs, "derived");
    }

    private static string BuildMaxLogicTickLeafColdFormula()
    {
        var leaves = new List<KeyValuePair<string, TickTimingMetric>>();
        void Add(string name, TickTimingMetric metric)
        {
            leaves.Add(new KeyValuePair<string, TickTimingMetric>(name, metric));
        }

        TickTimingMetric tick = GetMaxLogicTickTiming(MainThreadPerfScope.LogicFrameTick);
        TickTimingMetric listenerCallbacks = GetMaxLogicTickTiming(MainThreadPerfScope.LogicFrameListenerCallbacks);
        TickTimingMetric listenerAttributed = GetMaxLogicTickTiming(MainThreadPerfScope.LogicFrameListenerAttributed);
        Add("Tick外Listener", SubtractTickTiming(
            "Tick外Listener",
            tick,
            listenerCallbacks,
            GetMaxLogicTickTiming(MainThreadPerfScope.LogicFrameListenerSnapshot)));
        Add("Listener未归属", GetMaxLogicTickTiming(MainThreadPerfScope.LogicFrameListenerUnattributed));

        TickTimingMetric stableGoal = GetMaxLogicTickTiming(MainThreadPerfScope.FlowPrepareStableGoal);
        TickTimingMetric anchorDictionary = GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyAnchorDictionary);
        TickTimingMetric anchorDictionaryLookup = GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyAnchorDictionaryLookup);
        TickTimingMetric anchorDictionaryInsert = GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyAnchorDictionaryInsert);
        TickTimingMetric anchorDictionaryResidual = SubtractTickTiming(
            "StableGoal.AnchorDictionary未归属",
            anchorDictionary,
            anchorDictionaryLookup,
            anchorDictionaryInsert,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyAnchorDictionaryCreate));
        TickTimingMetric anchorState = GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyAnchorState);
        TickTimingMetric anchorStateResidual = SubtractTickTiming(
            "StableGoal.AnchorState未归属",
            anchorState,
            anchorDictionary,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyAnchorGoalState),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyAnchorNavState));
        TickTimingMetric inputResolution = GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyInputResolution);
        TickTimingMetric inputResolutionResidual = SubtractTickTiming(
            "StableGoal.InputResolution未归属",
            inputResolution,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyBatchResolutionLookup));
        TickTimingMetric stableGoalResidual = SubtractTickTiming(
            "StableGoal调用边界未归属",
            stableGoal,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyClassification),
            inputResolution,
            anchorState,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyAnchorPublish));
        Add("StableGoal分类", GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyClassification));
        Add("StableGoal批解析定位", GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyBatchResolutionLookup));
        Add("StableGoal输入解析未归属", inputResolutionResidual);
        Add("StableGoal锚点字典查找", anchorDictionaryLookup);
        Add("StableGoal锚点字典插入", anchorDictionaryInsert);
        Add("StableGoal锚点字典未归属", anchorDictionaryResidual);
        Add("StableGoal锚点目标状态", GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyAnchorGoalState));
        Add("StableGoal锚点导航状态", GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyAnchorNavState));
        Add("StableGoal锚点状态未归属", anchorStateResidual);
        Add("StableGoal锚点发布", GetMaxLogicTickTiming(MainThreadPerfScope.FlowMovingTargetPolicyAnchorPublish));
        Add("StableGoal调用边界未归属", stableGoalResidual);

        TickTimingMetric demandResolve = GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationDemandResolve);
        TickTimingMetric demandResolveResidual = SubtractTickTiming(
            "DemandResolve未归属",
            demandResolve,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowPrepareStartCell),
            stableGoal);
        Add("DemandResolve未归属", demandResolveResidual);

        TickTimingMetric demandDispatch = GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationDemandDispatch);
        Add("DemandReuse", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationDemandReuse));
        Add("DemandEnqueue", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationDemandEnqueue));
        Add("DemandDispatch未归属", SubtractTickTiming(
            "DemandDispatch未归属",
            demandDispatch,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationDemandReuse),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationDemandEnqueue)));
        TickTimingMetric demandBatch = GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationResolveDemandBatch);
        Add("DemandBatch未归属", SubtractTickTiming("DemandBatch未归属", demandBatch, demandResolve, demandDispatch));

        TickTimingMetric pathGoal = GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathSliceGoalConnector);
        TickTimingMetric pathGoalSearch = GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationGoalConnectorSearchSlice);
        TickTimingMetric pathGoalInput = GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationGoalConnectorInputCollection);
        Add("PathSliceInitialize", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathSliceInitialize));
        Add("PathSliceGoalConnector搜索", pathGoalSearch);
        Add("PathSliceGoalConnector输入收集", pathGoalInput);
        Add("PathSliceGoalConnector未归属", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationGoalConnectorUnattributed));
        TickTimingMetric pathDispatchFinalization = GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathSliceDispatchFinalization);
        Add("PathSliceDispatch收尾", pathDispatchFinalization);
        Add("PathSliceDispatch未归属", SubtractTickTiming(
            "PathSliceDispatch未归属",
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathSliceDispatchUnattributed),
            pathDispatchFinalization,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathSliceDispatchPreparation)));
        TickTimingMetric pathQueue = GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationResolvePathQueueBatch);
        Add("PathQueue资格", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathQueueEligibility));
        Add("PathQueue循环未归属", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathQueueLoopUnattributed));
        Add("PathQueue处理未归属", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathQueueProcessingUnattributed));
        Add("PathQueue提交循环", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathQueueCommitLoop));
        Add("PathQueue世界状态枚举", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathWorldStateEnumeration));
        Add("PathQueue世界切换", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathWorldActivation));
        Add("PathQueue世界恢复", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathWorldRestore));
        Add("PathQueue世界外未归属", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathWorldOuterUnattributed));
        Add("PathQueue预算记账", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathBudgetAccounting));
        Add("PathQueue未归属", SubtractTickTiming(
            "PathQueue未归属",
            pathQueue,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathSliceInitialize),
            pathGoal,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathSliceDispatchUnattributed),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathQueueEligibility),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathQueueLoopUnattributed),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathQueueProcessingUnattributed),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathQueueCommitLoop),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathWorldStateEnumeration),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathWorldActivation),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathWorldRestore),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathWorldOuterUnattributed),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathBudgetAccounting)));

        TickTimingMetric resolveRequests = GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationResolveRequests);
        Add("ResolveRequests路径诊断", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathDiagnosticCapture));
        Add("ResolveRequests预算结束", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathBudgetEnd));
        Add("ResolveRequests未归属", SubtractTickTiming(
            "ResolveRequests未归属",
            resolveRequests,
            demandBatch,
            pathQueue,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathDiagnosticCapture),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationPathBudgetEnd)));
        TickTimingMetric navigationCommit = GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationCommit);
        Add("NavigationCommit未归属", SubtractTickTiming("NavigationCommit未归属", navigationCommit, resolveRequests));

        TickTimingMetric slotCacheBuild = GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotCacheBuild);
        TickTimingMetric slotCacheBuildResidual = SubtractTickTiming(
            "SlotCacheBuild未归属",
            slotCacheBuild,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotGenerate),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotClearance),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotLineOfSight),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotDeduplicate),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotCacheArrayMaterialize),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotCachePublish));
        Add("SlotCache查找", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotCacheLookup));
        Add("SlotCache生成", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotGenerate));
        Add("SlotCache清除判定", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotClearance));
        Add("SlotCache视线判定", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotLineOfSight));
        Add("SlotCache去重", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotDeduplicate));
        Add("SlotCache数组物化", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotCacheArrayMaterialize));
        Add("SlotCache发布", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotCachePublish));
        Add("SlotCacheBuild未归属", slotCacheBuildResidual);
        TickTimingMetric slotCache = GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotCache);
        Add("SlotCache未归属", SubtractTickTiming(
            "SlotCache未归属",
            slotCache,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachSlotCacheLookup),
            slotCacheBuild));

        TickTimingMetric occupancyPreparation = GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachOccupancyPreparation);
        Add("Occupancy桶填充", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachOccupancyBucketFill));
        Add("Occupancy准备迭代", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachOccupancyPrepareIteration));
        Add("Occupancy准备快照", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachOccupancySnapshot));
        Add("OccupancyPreparation未归属", SubtractTickTiming(
            "OccupancyPreparation未归属",
            occupancyPreparation,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachOccupancyBucketFill),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachOccupancyPrepareIteration),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachOccupancySnapshot),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachOccupancyPrepareGuard),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachOccupancyPrepareCacheState),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachOccupancySlotFilter)));
        TickTimingMetric score = GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachScore);
        Add("CombatScore算术", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachScoreArithmetic));
        Add("CombatScore未归属", SubtractTickTiming(
            "CombatScore未归属",
            score,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachScoreArithmetic)));
        TickTimingMetric scorePhase = GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachCoreScorePhase);
        Add("Occupancy判定", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachOccupancy));
        Add("CombatScorePhase未归属", SubtractTickTiming(
            "CombatScorePhase未归属",
            scorePhase,
            occupancyPreparation,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachOccupancy),
            score,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachCoreScoreUnattributed)));

        TickTimingMetric combatCore = GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachCore);
        Add("CombatCore准备代理", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachCoreAgentPreparation));
        Add("CombatCore设置", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachCoreSetup));
        Add("CombatCore方向设置", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachDirectionSetup));
        Add("CombatCore扩展", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachCoreExpandedPhase));
        Add("CombatCore收尾", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachFinalize));
        Add("CombatCore未归属", SubtractTickTiming(
            "CombatCore未归属",
            combatCore,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachCoreAgentPreparation),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachCoreSetup),
            slotCache,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachDirectionSetup),
            scorePhase,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachCoreExpandedPhase),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachCoreUnattributed),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachFinalize)));
        TickTimingMetric approach = GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproach);
        Add("CombatApproach预解析", GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachPreResolve));
        Add("CombatApproach未归属", SubtractTickTiming(
            "CombatApproach未归属",
            approach,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowCombatApproachPreResolve),
            combatCore));
        TickTimingMetric entityBrainCombat = GetMaxLogicTickTiming(MainThreadPerfScope.EntityBrainCombat);
        Add("EntityBrainCombat未归属", SubtractTickTiming("EntityBrainCombat未归属", entityBrainCombat, approach));
        TickTimingMetric brain = GetMaxLogicTickTiming(MainThreadPerfScope.LogicEntityBrain);
        Add("Brain未归属", SubtractTickTiming("Brain未归属", brain, entityBrainCombat));

        TickTimingMetric targeting = GetMaxLogicTickTiming(MainThreadPerfScope.LogicEntityTargeting);
        TickTimingMetric targetingEvaluate = GetMaxLogicTickTiming(MainThreadPerfScope.CharacterTargetingEvaluate);
        Add("Targeting候选扫描", GetMaxLogicTickTiming(MainThreadPerfScope.CharacterTargetingCandidateScan));
        Add("TargetingEvaluate未归属", SubtractTickTiming(
            "TargetingEvaluate未归属",
            targetingEvaluate,
            GetMaxLogicTickTiming(MainThreadPerfScope.CharacterTargetingCandidateScan)));
        Add("Targeting未归属", SubtractTickTiming("Targeting未归属", targeting, targetingEvaluate));

        TickTimingMetric navigationPositionSync = GetMaxLogicTickTiming(MainThreadPerfScope.LogicEntityNavigationPositionSync);
        Add("NavigationPositionSync代理更新", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationAgentUpdate));
        Add("NavigationPositionSync非活动清理", GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationInactiveClear));
        Add("NavigationPositionSync未归属", SubtractTickTiming(
            "NavigationPositionSync未归属",
            navigationPositionSync,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationAgentUpdate),
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowNavigationInactiveClear)));
        TickTimingMetric navigationSync = GetMaxLogicTickTiming(MainThreadPerfScope.LogicEntityNavigationSync);
        Add("NavigationSync组移动", GetMaxLogicTickTiming(MainThreadPerfScope.FlowGroupMove));
        Add("NavigationSync未归属", SubtractTickTiming(
            "NavigationSync未归属",
            navigationSync,
            navigationCommit,
            GetMaxLogicTickTiming(MainThreadPerfScope.FlowGroupMove)));

        TickTimingMetric moveIntent = GetMaxLogicTickTiming(MainThreadPerfScope.LogicEntityMoveIntent);
        Add("MoveIntent准备", GetMaxLogicTickTiming(MainThreadPerfScope.CharacterMovePrepare));
        Add("MoveIntent未归属", SubtractTickTiming(
            "MoveIntent未归属",
            moveIntent,
            GetMaxLogicTickTiming(MainThreadPerfScope.CharacterMovePrepare)));
        Add("BaseAndBuffs", GetMaxLogicTickTiming(MainThreadPerfScope.LogicEntityBaseAndBuffs));
        Add("FrameSetup", GetMaxLogicTickTiming(MainThreadPerfScope.LogicEntityFrameSetup));
        Add("MoveResolve", GetMaxLogicTickTiming(MainThreadPerfScope.LogicEntityMoveResolve));
        Add("MoveCommit", GetMaxLogicTickTiming(MainThreadPerfScope.LogicEntityMoveCommit));
        Add("Attack", GetMaxLogicTickTiming(MainThreadPerfScope.LogicEntityAttack));
        Add("DamageResolve", GetMaxLogicTickTiming(MainThreadPerfScope.LogicEntityDamageResolve));
        Add("Projectile", GetMaxLogicTickTiming(MainThreadPerfScope.LogicEntityProjectile));
        Add("PostUpdate", GetMaxLogicTickTiming(MainThreadPerfScope.LogicEntityPostUpdate));
        Add("FrameComplete", GetMaxLogicTickTiming(MainThreadPerfScope.LogicEntityFrameComplete));

        TickTimingMetric attributedChildren = new TickTimingMetric(0.0, 0.0, 0.0);
        for (int i = 0; i < leaves.Count; i++)
        {
            if (leaves[i].Key.StartsWith("Tick外", StringComparison.Ordinal)
                || leaves[i].Key.StartsWith("Listener", StringComparison.Ordinal))
                continue;
            attributedChildren = new TickTimingMetric(
                attributedChildren.MaxTickMs + leaves[i].Value.MaxTickMs,
                attributedChildren.FirstMs + leaves[i].Value.FirstMs,
                attributedChildren.HotAverageMs + leaves[i].Value.HotAverageMs);
        }
        Add("ListenerAttributed未归属", SubtractTickTiming("ListenerAttributed未归属", listenerAttributed, attributedChildren));

        double maxTickTotal = 0.0;
        double coldTotal = 0.0;
        var lines = new List<string>(leaves.Count + 2);
        for (int i = 0; i < leaves.Count; i++)
        {
            TickTimingMetric metric = leaves[i].Value;
            maxTickTotal += metric.MaxTickMs;
            coldTotal += metric.ColdGapMs;
            lines.Add($"leaf name={leaves[i].Key},maxTickMs={metric.MaxTickMs.ToString("F3", CultureInfo.InvariantCulture)},logicTickFrame={s_MaxLogicTickLogicFrame},hotAverageSource={metric.HotAverageSource},tickValueMs={metric.FirstMs.ToString("F3", CultureInfo.InvariantCulture)},warmTickValueMs={metric.HotAverageMs.ToString("F3", CultureInfo.InvariantCulture)},coldGapMs={metric.ColdGapMs.ToString("F3", CultureInfo.InvariantCulture)}");
        }

        double tickCold = tick.ColdGapMs;
        double contributionRate = tick.MaxTickMs > 0.0 ? coldTotal / tick.MaxTickMs : 0.0;
        lines.Add($"leafFormula maxTickTotalMs={maxTickTotal.ToString("F3", CultureInfo.InvariantCulture)},tickMaxMs={tick.MaxTickMs.ToString("F3", CultureInfo.InvariantCulture)},residualMs={(maxTickTotal - tick.MaxTickMs).ToString("F3", CultureInfo.InvariantCulture)}");
        lines.Add($"leafFormulaCold tickColdMs={tickCold.ToString("F3", CultureInfo.InvariantCulture)},leafColdTotalMs={coldTotal.ToString("F3", CultureInfo.InvariantCulture)},residualMs={(coldTotal - tickCold).ToString("F3", CultureInfo.InvariantCulture)},contributionRate={contributionRate.ToString("F6", CultureInfo.InvariantCulture)},equation={tick.MaxTickMs.ToString("F3", CultureInfo.InvariantCulture)}*{contributionRate.ToString("F6", CultureInfo.InvariantCulture)}={coldTotal.ToString("F3", CultureInfo.InvariantCulture)}");
        if (System.Math.Abs(maxTickTotal - tick.MaxTickMs) > 0.02 || System.Math.Abs(coldTotal - tickCold) > 0.02)
        {
            throw new InvalidOperationException(
                $"Max logic Tick leaf formula does not close. maxTickResidual={maxTickTotal - tick.MaxTickMs:F6}, coldResidual={coldTotal - tickCold:F6}.");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildDistributionReport(string name, List<double> samples)
    {
        if (samples.Count == 0)
            return $"distribution name={name},count=0";

        double[] ordered = samples.ToArray();
        Array.Sort(ordered);
        return $"distribution name={name},count={ordered.Length}," +
               $"p50={Percentile(ordered, 0.50).ToString("F3", CultureInfo.InvariantCulture)}," +
               $"p95={Percentile(ordered, 0.95).ToString("F3", CultureInfo.InvariantCulture)}," +
               $"p99={Percentile(ordered, 0.99).ToString("F3", CultureInfo.InvariantCulture)}," +
               $"max={ordered[ordered.Length - 1].ToString("F3", CultureInfo.InvariantCulture)}";
    }

    private static double Percentile(double[] ordered, double percentile)
    {
        if (ordered == null || ordered.Length == 0)
            throw new ArgumentException("Percentile requires at least one ordered sample.", nameof(ordered));
        int index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
        return ordered[Math.Max(0, Math.Min(ordered.Length - 1, index))];
    }

    private static string DescribeChaseState(IEntityContext hero, IEntityContext target)
    {
        int enemyCount = 0;
        int heroTargetCount = 0;
        int combatCount = 0;
        int returningCount = 0;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity == null || !entity.Alive || entity.Side != SideType.EnemySide || entity.Brain is not SoldierAIBrain brain)
                continue;
            enemyCount++;
            if (ReferenceEquals(entity.TargetComp?.CurrentTarget, hero))
                heroTargetCount++;
            if (brain.State == SoldierAIBrain.SoldierState.Combat)
                combatCount++;
            if (brain.State == SoldierAIBrain.SoldierState.Returning)
                returningCount++;
        }
        return $"hero={hero.PositionFixed},target={DescribeEntity(target)},targetState={((SoldierAIBrain)target.Brain).State}," +
               $"targetIsHero={ReferenceEquals(target.TargetComp?.CurrentTarget, hero)},enemy={enemyCount},heroTargets={heroTargetCount},combat={combatCount},returning={returningCount}";
    }

    private static string DescribeEntity(IEntityContext entity)
    {
        return $"{entity.LogicEntityId.Value}/{entity.CharacterKey}@{entity.PositionFixed}";
    }

    private static void CaptureEnemyMotion(ulong frame, IEntityContext hero, ScenarioMode mode)
    {
        if (frame == s_LastEnemyMotionFrame)
            return;
        if (frame < s_LastEnemyMotionFrame)
            throw new InvalidOperationException($"Lv2 enemy motion frame moved backwards. previous={s_LastEnemyMotionFrame}, current={frame}.");
        s_LastEnemyMotionFrame = frame;

        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity == null
                || !entity.Alive
                || entity.Side != SideType.EnemySide
                || entity.Brain is not SoldierAIBrain brain
                || (brain.State != SoldierAIBrain.SoldierState.Combat
                    && brain.State != SoldierAIBrain.SoldierState.Returning
                    && !ReferenceEquals(entity.TargetComp?.CurrentTarget, hero)))
            {
                continue;
            }

            int entityId = entity.LogicEntityId.Value;
            FixVector2 position = entity.PositionFixed;
            bool hasPrevious = s_LastEnemyMotionPositions.TryGetValue(entityId, out FixVector2 previousPosition);
            FixVector2 displacement = hasPrevious ? position - previousPosition : FixVector2.Zero;
            s_LastEnemyMotionPositions[entityId] = position;

            FixVector2 moveTarget = FixVector2.Zero;
            bool hasMoveTarget = entity.MoveComp is CharacterMoveComp moveComp
                                 && moveComp.TryGetNavigationTargetFixed(out moveTarget);
            string collision = "unavailable";
            if (LogicAgentCollisionShadowService.LastCompletedFrame == frame)
            {
                LogicAgentCollisionShadowState state = LogicAgentCollisionShadowService.GetRequiredState(
                    entity.LogicEntityId,
                    frame);
                collision =
                    $"proposedRaw=({state.ProposedPosition.x.RawValue},{state.ProposedPosition.y.RawValue})" +
                    $"/pairRaw=({state.PairCorrection.x.RawValue},{state.PairCorrection.y.RawValue})" +
                    $"/staticRaw=({state.StaticCorrection.x.RawValue},{state.StaticCorrection.y.RawValue})" +
                    $"/regionRaw=({state.RegionCorrection.x.RawValue},{state.RegionCorrection.y.RawValue})" +
                    $"/finalRaw=({state.FinalResolvedPosition.x.RawValue},{state.FinalResolvedPosition.y.RawValue})" +
                    $"/staticContact={state.StaticContactKind}/regionFailure={state.RegionConstraintFailure}";
            }

            string navigation = FlowFieldCrowdMovementSystem.GetEditorTestAgentMotionDiagnostics(entityId);
            bool isPortalTileWait = navigation.Contains("/goalKind=Portal/", StringComparison.Ordinal)
                                    && navigation.Contains("/cached=false/", StringComparison.Ordinal);
            if (isPortalTileWait
                && navigation.Contains("/lastResult=tile-pending-portal-access", StringComparison.Ordinal))
            {
                s_PortalTileWaitAccessSamples++;
                if (navigation.Contains("/lastVelocityRaw=(0,0)", StringComparison.Ordinal))
                    s_PortalTileWaitZeroVelocitySamples++;
            }
            if (isPortalTileWait
                && navigation.Contains("/lastResult=pending-navigation-current-tile", StringComparison.Ordinal))
            {
                s_PortalTileWaitStoppedSamples++;
            }
            s_Samples.Add(
                $"enemyMotion logic={frame},mode={mode},id={entityId},state={brain.State},attack={brain.Attack}," +
                $"target={entity.TargetComp?.CurrentTarget?.LogicEntityId.Value.ToString() ?? "null"}," +
                $"positionRaw=({position.x.RawValue},{position.y.RawValue})," +
                $"displacementRaw=({displacement.x.RawValue},{displacement.y.RawValue}),hasPrevious={hasPrevious}," +
                $"isMoving={entity.MoveComp?.IsMoving ?? false},hasMoveTarget={hasMoveTarget}," +
                $"moveTargetRaw={(hasMoveTarget ? $"({moveTarget.x.RawValue},{moveTarget.y.RawValue})" : "null")}," +
                $"collision=[{collision}],navigation=[{navigation}]");
        }
    }

    private static void AppendEvent(string name, ulong logicFrame, string detail)
    {
        s_Samples.Add($"event name={name},render={Time.frameCount},logic={logicFrame},detail=[{detail ?? string.Empty}]");
    }

    private static void DrainNavigationPathTickDiagnostics()
    {
        string[] diagnostics = FlowFieldCrowdMovementSystem.DrainEditorTestNavigationPathTickDiagnostics();
        for (int i = 0; i < diagnostics.Length; i++)
            s_Samples.Add(diagnostics[i]);
    }

    private static void Pass(ulong frame, IEntityContext hero, IEntityContext target)
    {
        DrainNavigationPathTickDiagnostics();
        if (s_PeakRequiredFlowTileCommits != 0)
        {
            throw new InvalidOperationException(
                $"Lv2 pull-chase observed {s_PeakRequiredFlowTileCommits} required Flow tile commits inside steering.");
        }
        if (s_PortalTileWaitZeroVelocitySamples != 0 || s_PortalTileWaitStoppedSamples != 0)
        {
            throw new InvalidOperationException(
                $"Lv2 pull-chase observed navigation-induced portal tile waiting stops. " +
                $"accessSamples={s_PortalTileWaitAccessSamples}, zeroVelocity={s_PortalTileWaitZeroVelocitySamples}, " +
                $"pendingCurrentTile={s_PortalTileWaitStoppedSamples}.");
        }

        string report =
            "RESULT=PASS" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            "finalLogicFrame=" + frame + Environment.NewLine +
            "maxFrameMs=" + s_MaxFrameMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + Environment.NewLine +
            "maxFrame=" + s_MaxFrame + Environment.NewLine +
            "maxLogicMs=" + s_MaxLogicMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + Environment.NewLine +
            "maxLogicRenderFrame=" + s_MaxLogicFrame + Environment.NewLine +
            "peakRequiredFlowTileCommits=" + s_PeakRequiredFlowTileCommits + Environment.NewLine +
            "peakFlowTileQueueMutations=" + s_PeakFlowTileQueueMutations + Environment.NewLine +
            "peakPathPortalExpansions=" + s_PeakPathPortalExpansions + Environment.NewLine +
            "pathRequestOperationQuota=" + FlowFieldCrowdMovementSystem.GetEditorTestNavigationPathRequestOperationQuota() + Environment.NewLine +
            "peakPathRequestGroups=" + s_PeakPathRequestGroups + Environment.NewLine +
            "peakPathRequestOperations=" + s_PeakPathRequestOperations + Environment.NewLine +
            "peakPathRequestCommits=" + s_PeakPathRequestCommits + Environment.NewLine +
            "peakPathRequestSourceCommits=" + s_PeakPathRequestSourceCommits + Environment.NewLine +
            "peakPendingPathRequestGroups=" + s_PeakPendingPathRequestGroups + Environment.NewLine +
            "peakPendingPathRequestSources=" + s_PeakPendingPathRequestSources + Environment.NewLine +
            "portalTileWaitAccessSamples=" + s_PortalTileWaitAccessSamples + Environment.NewLine +
            "portalTileWaitZeroVelocitySamples=" + s_PortalTileWaitZeroVelocitySamples + Environment.NewLine +
            "portalTileWaitStoppedSamples=" + s_PortalTileWaitStoppedSamples + Environment.NewLine +
            BuildDistributionReport("approach-frame", s_ApproachFrameMilliseconds) + Environment.NewLine +
            BuildDistributionReport("approach-logic", s_ApproachLogicMilliseconds) + Environment.NewLine +
            BuildDistributionReport("retreat-frame", s_RetreatFrameMilliseconds) + Environment.NewLine +
            BuildDistributionReport("retreat-logic", s_RetreatLogicMilliseconds) + Environment.NewLine +
            "navigationBeforeInvade=" + s_NavigationBeforeInvade + Environment.NewLine +
            "navigationAfterInvade=" + s_NavigationAfterInvade + Environment.NewLine +
            "finalChase=" + DescribeChaseState(hero, target) + Environment.NewLine +
            "maxLogicTickMs=" + s_MaxLogicTickMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + Environment.NewLine +
            "maxLogicTickRenderFrame=" + s_MaxLogicTickRenderFrame + Environment.NewLine +
            "maxLogicTickLogicFrame=" + s_MaxLogicTickLogicFrame + Environment.NewLine +
            "scopePeaks:" + Environment.NewLine + BuildChaseScopePeakReport() + Environment.NewLine +
            "maxLogicTickScopes:" + Environment.NewLine + BuildMaxLogicTickScopeReport() + Environment.NewLine +
            "maxLogicTickScopeRecordEvidence:" + Environment.NewLine + BuildMaxLogicTickScopeRecordEvidence() + Environment.NewLine +
            "maxLogicTickLeafColdFormula:" + Environment.NewLine + BuildMaxLogicTickLeafColdFormula() + Environment.NewLine +
            "overallScopeRecordEvidence:" + Environment.NewLine + BuildOverallScopeRecordEvidence() + Environment.NewLine +
            "maxLogicTickInvocationEvidence:" + Environment.NewLine + s_MaxLogicTickInvocationEvidence + Environment.NewLine +
            "overallInvocationEvidence:" + Environment.NewLine + BuildOverallInvocationReport() + Environment.NewLine +
            "samples:" + Environment.NewLine + string.Join(Environment.NewLine, s_Samples) + Environment.NewLine;
        WriteResult(ResultRelativePath, report);
        Log.Info("[Lv2PullChasePerformance] PASS. maxFrameMs={0:F3}, maxLogicMs={1:F3}, samples={2}.", s_MaxFrameMilliseconds, s_MaxLogicMilliseconds, s_Samples.Count);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        EditorApplication.isPlaying = false;
    }

    private static void PassBuild(ulong frame)
    {
        DrainNavigationPathTickDiagnostics();
        if (FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty())
            throw new InvalidOperationException("Lv2 RuntimeDirty build runner reached completion with pending navigation rebuild.");

        string report =
            "RESULT=PASS" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            "buildScheduledFrame=" + SessionState.GetInt(BuildScheduledFrameKey, 0) + Environment.NewLine +
            "buildAppliedFrame=" + SessionState.GetInt(BuildAppliedFrameKey, 0) + Environment.NewLine +
            "buildBeforeDefendScheduledFrame=" + SessionState.GetInt(BuildBeforeDefendScheduledFrameKey, 0) + Environment.NewLine +
            "defenseScheduledFrame=" + SessionState.GetInt(DefenseScheduledFrameKey, 0) + Environment.NewLine +
            "defenseAppliedFrame=" + SessionState.GetInt(DefenseAppliedFrameKey, 0) + Environment.NewLine +
            "finalLogicFrame=" + frame + Environment.NewLine +
            "maxFrameMs=" + s_MaxFrameMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + Environment.NewLine +
            "maxFrame=" + s_MaxFrame + Environment.NewLine +
            "maxLogicMs=" + s_MaxLogicMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + Environment.NewLine +
            "maxLogicRenderFrame=" + s_MaxLogicFrame + Environment.NewLine +
            "maxLogicTickMs=" + s_MaxLogicTickMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + Environment.NewLine +
            "maxLogicTickRenderFrame=" + s_MaxLogicTickRenderFrame + Environment.NewLine +
            "peakRequiredFlowTileCommits=" + s_PeakRequiredFlowTileCommits + Environment.NewLine +
            "peakFlowTileQueueMutations=" + s_PeakFlowTileQueueMutations + Environment.NewLine +
            "peakPathPortalExpansions=" + s_PeakPathPortalExpansions + Environment.NewLine +
            "navigationFinal=" + FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics() + Environment.NewLine +
            "scopePeaks:" + Environment.NewLine + BuildChaseScopePeakReport() + Environment.NewLine +
            "maxLogicTickScopes:" + Environment.NewLine + BuildMaxLogicTickScopeReport() + Environment.NewLine +
            "lastCompletedMaxLogicTickScopes:" + Environment.NewLine + BuildMaxLogicTickScopeReport() + Environment.NewLine +
            "samples:" + Environment.NewLine + string.Join(Environment.NewLine, s_Samples) + Environment.NewLine;
        WriteResult(BuildResultRelativePath, report);
        Log.Info("[Lv2RuntimeDirtyBuildPerformance] PASS. maxFrameMs={0:F3}, maxLogicMs={1:F3}, samples={2}.", s_MaxFrameMilliseconds, s_MaxLogicMilliseconds, s_Samples.Count);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        EditorApplication.isPlaying = false;
    }

    private static void Fail(Exception exception)
    {
        if (EditorApplication.isPlaying)
            DrainNavigationPathTickDiagnostics();
        string report =
            "RESULT=FAIL" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            "state=" + (RunnerState)SessionState.GetInt(StateKey, 0) + Environment.NewLine +
            "navigation=" + (EditorApplication.isPlaying ? FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics() : string.Empty) + Environment.NewLine +
            "exception=" + exception + Environment.NewLine +
            "samples:" + Environment.NewLine + string.Join(Environment.NewLine, s_Samples) + Environment.NewLine;
        WriteResult(
            SessionState.GetBool(BuildScenarioKey, false) ? BuildResultRelativePath : ResultRelativePath,
            report);
        UnityEngine.Debug.LogException(exception);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
        else
            SessionState.SetBool(RunningKey, false);
    }

    private static void ValidateTimeout()
    {
        string raw = SessionState.GetString(StartedUtcKey, string.Empty);
        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime startedUtc))
            throw new InvalidOperationException($"Invalid runner start timestamp '{raw}'.");
        if (DateTime.UtcNow - startedUtc > Timeout)
            throw new TimeoutException($"Lv2 pull-chase runner exceeded {Timeout}.");
    }

    private static void ResetCaptureState()
    {
        s_Route.Clear();
        s_Samples.Clear();
        Array.Clear(s_ChaseScopePeakMilliseconds, 0, s_ChaseScopePeakMilliseconds.Length);
        Array.Clear(s_ChaseScopePeakRenderFrames, 0, s_ChaseScopePeakRenderFrames.Length);
        Array.Clear(s_ChaseScopePeakCalls, 0, s_ChaseScopePeakCalls.Length);
        s_LastEditorUpdateTimestamp = 0;
        s_LastProfilerFrame = -1;
        s_LastPeriodicSampleFrame = -1;
        s_MaxFrameMilliseconds = 0.0;
        s_MaxFrame = -1;
        s_MaxLogicMilliseconds = 0.0;
        s_MaxLogicFrame = -1;
        s_MaxLogicTickMilliseconds = 0.0;
        s_MaxLogicTickRenderFrame = -1;
        s_MaxLogicTickLogicFrame = 0;
        s_MaxLogicTickWarmLogicFrame = 0;
        Array.Clear(s_MaxLogicTickScopeMilliseconds, 0, s_MaxLogicTickScopeMilliseconds.Length);
        Array.Clear(s_MaxLogicTickScopeRecordFirstMilliseconds, 0, s_MaxLogicTickScopeRecordFirstMilliseconds.Length);
        Array.Clear(s_MaxLogicTickScopeRecordHotAverageMilliseconds, 0, s_MaxLogicTickScopeRecordHotAverageMilliseconds.Length);
        Array.Clear(s_MaxLogicTickScopeRecordCounts, 0, s_MaxLogicTickScopeRecordCounts.Length);
        s_MaxLogicTickInvocationEvidence = string.Empty;
        s_ApproachFrameMilliseconds.Clear();
        s_ApproachLogicMilliseconds.Clear();
        s_RetreatFrameMilliseconds.Clear();
        s_RetreatLogicMilliseconds.Clear();
        s_LastEnemyMotionPositions.Clear();
        s_LastEnemyMotionFrame = 0;
        s_PeakRequiredFlowTileCommits = 0;
        s_PeakFlowTileQueueMutations = 0;
        s_PeakPathPortalExpansions = 0;
        s_PeakPathRequestGroups = 0;
        s_PeakPathRequestOperations = 0;
        s_PeakPathRequestCommits = 0;
        s_PeakPathRequestSourceCommits = 0;
        s_PeakPendingPathRequestGroups = 0;
        s_PeakPendingPathRequestSources = 0;
        s_PortalTileWaitAccessSamples = 0;
        s_PortalTileWaitZeroVelocitySamples = 0;
        s_PortalTileWaitStoppedSamples = 0;
        s_NavigationBeforeInvade = string.Empty;
        s_NavigationAfterInvade = string.Empty;
        s_LastRetreatDirection = string.Empty;
        s_CaptureActive = false;
        SessionState.SetInt(TargetEntityIdKey, 0);
        SessionState.SetInt(ModeKey, (int)ScenarioMode.Approach);
        SessionState.SetInt(BaselineStartFrameKey, 0);
        SessionState.SetInt(InvadeScheduledFrameKey, 0);
        SessionState.SetInt(RetreatStartFrameKey, 0);
        SessionState.SetInt(BuildScheduledFrameKey, 0);
        SessionState.SetInt(BuildAppliedFrameKey, 0);
        SessionState.SetInt(BuildBeforeDefendScheduledFrameKey, 0);
        SessionState.SetInt(DefenseScheduledFrameKey, 0);
        SessionState.SetInt(DefenseAppliedFrameKey, 0);
        SessionState.SetBool(BuildScenarioKey, false);
        SessionState.SetBool(PrewarmKey, false);
    }

    private static int ToSessionInt(ulong frame)
    {
        return checked((int)Math.Min(int.MaxValue, frame));
    }

    private static ulong FromSessionInt(string key)
    {
        return checked((ulong)SessionState.GetInt(key, 0));
    }

    private static float HorizontalDistance(Vector3 left, Vector3 right)
    {
        float x = left.x - right.x;
        float z = left.z - right.z;
        return Mathf.Sqrt(x * x + z * z);
    }

    private static void WriteResult(string relativePath, string text)
    {
        string path = Path.Combine(Directory.GetParent(Application.dataPath)?.FullName
                                   ?? throw new InvalidOperationException("Cannot resolve project root."), relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)
                                  ?? throw new InvalidOperationException("Cannot resolve result directory."));
        File.WriteAllText(path, text);
    }
}
