using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;

namespace UnityGameFramework.Runtime
{
    public enum MainThreadPerfScope
    {
        GameFrameworkUpdate = 0,
        FlowGroupMove = 1,
        FlowConfig = 2,
        FlowSourceGate = 3,
        Fog3Update = 4,
        Fog3Visibility = 5,
        Fog3OverlayRender = 6,
        Fog3EnemyVisibility = 7,
        InteractionTrigger = 8,
        InteractionCleanup = 9,
        InteractionManagerUpdate = 10,
        InteractionResolveTarget = 11,
        InteractionFocusEvent = 12,
        InteractionFocusLog = 13,
        InteractionFocusPresenter = 14,
        InteractionBuildTipsRequest = 15,
        EntityUpdate = 16,
        EntityBase = 17,
        EntityBuff = 18,
        EntityScale = 19,
        EntityAgentPosition = 20,
        EntityBrain = 21,
        EntityTargeting = 22,
        EntityAttack = 23,
        EntityDurationMove = 24,
        EntityMoveComp = 25,
        EntityMoveExecutor = 26,
        EntityAnimator = 27,
        EntityRotation = 28,
        SoldierPostUpdate = 29,
        SoldierMinimap = 30,
        SoldierDebugDraw = 31,
        MoveExecutorConstraint = 32,
        MoveExecutorControllerMove = 33,
        CharacterMoveSteering = 34,
        CharacterMoveIdleRecovery = 35,
        CharacterMoveSetInput = 36,
        CharacterMoveDebugLog = 37,
        CharacterMovePrepare = 38,
        EntityShowRequest = 39,
        EntityInstantiate = 40,
        EntityCreate = 41,
        EntityInitTotal = 42,
        EntityLogicCreate = 43,
        EntityLogicInit = 44,
        EntityShowTotal = 45,
        EntityLogicShow = 46,
        EntitySuccessEvent = 47,
        Fog3EnemyBind = 48,
        Fog3EnemyStateCreate = 49,
        Fog3EnemyResolve = 50,
        Fog3EnemyEvent = 51,
        Fog3EnemyApply = 52,
        Fog3EnemyStale = 53,
        Fog3EnemyHealth = 54,
        ClusterSpawnLog = 55,
        ClusterSpawnPositions = 56,
        ClusterSpawnUnits = 57,
        SoldierResolvePrefab = 58,
        SoldierCreateBuffs = 59,
        SoldierCreateParams = 60,
        LogicStateCreate = 61,
        LogicStateConfigure = 62,
        LogicStateRecord = 63,
        SoldierViewEnqueue = 64,
        UnitConfigData = 65,
        UnitConfigProperties = 66,
        UnitConfigBrain = 67,
        UnitConfigState = 68,
        UnitConfigDefend = 69,
        UnitConfigMove = 70,
        UnitConfigAttack = 71,
        UnitConfigTargeting = 72,
        UnitConfigSkills = 73,
        UnitConfigBuffs = 74,
        CardSetupTotal = 75,
        CardSetupShutdown = 76,
        CardSetupController = 77,
        CardSetupCopyPool = 78,
        CardSetupSetPool = 79,
        CardSetupReset = 80,
        CardSetupBindWorld = 81,
        CardSetupBindRuntime = 82,
        FlowWorldBuildQueue = 83,
        FlowRuntimeRebuildQueue = 84,
        FlowTileBuildQueue = 85,
        LogicFrameAdvance = 86,
        LogicFrameCommands = 87,
        LogicFrameTick = 88,
        LogicFrameListenerCallbacks = 89,
        RenderFramePhysicsSync = 90,
        LogicFrameListenerSnapshot = 91,
        LogicEntityFrameSetup = 92,
        LogicEntityBaseAndBuffs = 93,
        LogicEntityNavigationSync = 94,
        LogicEntityBrain = 95,
        LogicEntityTargeting = 96,
        LogicEntityProjectile = 97,
        LogicEntityAttack = 98,
        LogicEntityDamageResolve = 99,
        LogicEntityMoveIntent = 100,
        LogicEntityMoveResolve = 101,
        LogicEntityMoveCommit = 102,
        LogicEntityPostUpdate = 103,
        LogicEntityFrameComplete = 104,
        LogicMoveResolvePrepare = 105,
        LogicMoveResolvePairSolver = 106,
        LogicMoveResolveProjection = 107,
        LogicMoveResolveStaticSolver = 108,
        LogicMoveResolveRegionConstraint = 109,
        LogicMoveResolveRebuild = 110,
        LogicMoveResolveBookkeeping = 111,
        FlowSteeringSetup = 112,
        FlowSteeringSetupAgent = 113,
        FlowSteeringSetupWorld = 114,
        FlowSteeringSetupOccupancy = 115,
        FlowSteeringSetupPrepare = 116,
        FlowSteeringPath = 117,
        FlowSteeringPortalOwner = 118,
        FlowSteeringVelocity = 119,
        FlowSteeringDirectStatic = 120,
        FlowSteeringDirectLineOfSight = 121,
        CharacterTargetingEvaluate = 122,
        CharacterTargetingCandidateScan = 123,
        CharacterTargetingReachability = 124,
        CharacterTargetingWallDetour = 125,
        FlowAttackAreaSetup = 126,
        FlowAttackAreaScan = 127,
        FlowAttackAreaClearance = 128,
        FlowPrepareStartCell = 129,
        FlowPrepareStableGoal = 130,
        FlowPreparePathHandle = 131,
        FlowPrepareTileDemand = 132,
        FlowPrepareGoalOccupancy = 133,
        FlowPrepareWorld = 134,
        FlowPreparePathAdvance = 135,
        FlowPrepareReadDomain = 136,
        FlowPathFastValidation = 137,
        FlowPathBuildCache = 138,
        FlowPathBuildSharedGoal = 139,
        FlowPathBuildCorridorPolicy = 140,
        FlowPathBuildHierarchy = 141,
        FlowPathBuildPortalGraph = 142,
        FlowPathBuildCommittedPrefix = 143,
        FlowCorridorPolicyLookup = 144,
        FlowCorridorPolicyHierarchy = 145,
        FlowCorridorPolicyAuthorityHash = 146,
        FlowCorridorPolicyReconstruct = 147,
        FlowPathHandleCreateCache = 148,
        FlowCorridorPolicyGoalConnector = 149,
        FlowCorridorPolicyStartConnector = 150,
        FlowCorridorPolicyReverseExpand = 151,
        FlowTileQueueActiveDemand = 152,
        FlowTileQueuePrune = 153,
        FlowTileQueueReferenceTrim = 154,
        FlowTileQueueCommit = 155,
        FlowTileQueueSharedGoal = 156,
        FlowTileCommitQueueScan = 157,
        FlowTileCommitDirections = 158,
        FlowTileCommitContinuation = 159,
        FlowTileCommitDiagnosticShadow = 160,
        FlowTileCommitCache = 161,
        FlowTileCommitReferenceTrim = 162,
        FlowCorridorPolicyStartLeafAccess = 163,
        FlowCorridorPolicyStartLeafSearch = 164,
        FlowCorridorPolicyStartExtend = 165,
        FlowCorridorPolicyDownwardCustomize = 166,
        FlowNavigationAgentUpdate = 167,
        FlowNavigationInactiveClear = 168,
        FlowNavigationCommit = 169,
        FlowNavigationResolveRequests = 170,
        FlowNavigationTileQueue = 171,
        FlowNavigationPortalOwners = 172,
        FlowNavigationRequestSort = 173,
        FlowNavigationDemandResolve = 174,
        FlowNavigationDemandDispatch = 175,
        FlowNavigationRequestPrune = 176,
        FlowNavigationPathAdvance = 177,
        FlowNavigationPathInitialize = 178,
        FlowNavigationPathGoalConnector = 179,
        FlowNavigationPathCreateHierarchy = 180,
        FlowNavigationPathExpandHierarchy = 181,
        FlowNavigationPathDownward = 182,
        FlowNavigationPathL0 = 183,
        FlowNavigationPathMaterialize = 184,
        FlowNavigationPathComplete = 185,
        FlowTileQueueMovingTargetProjection = 193,
        FlowSteeringPortalState = 186,
        FlowSteeringFunnel = 187,
        FlowSteeringGradient = 188,
        FlowSteeringIntegration = 189,
        FlowSteeringDiagnostics = 190,
        FlowSteeringFunnelGridLos = 191,
        FlowSteeringFunnelStaticSweep = 192,
        FlowNavigationDemandReuse = 194,
        FlowNavigationDemandEnqueue = 195,
        FlowNavigationDemandPathValidation = 196,
        FlowNavigationDemandPathAdvance = 197,
        FlowNavigationDemandPortalParticipation = 198,
        FlowNavigationDemandTileBuilds = 199,
        FlowNavigationDemandRemoveOther = 200,
        FlowNavigationDemandQueueMutation = 201,
        EntityBrainCombat = 202,
        FlowCombatApproach = 203,
        FlowCombatApproachOccupancy = 204,
        FlowCombatApproachCoreSetup = 205,
        FlowCombatApproachSlotCache = 206,
        FlowCombatApproachSlotCacheBuild = 207,
        FlowCombatApproachScore = 208,
        FlowCombatApproachExpanded = 209,
        FlowCombatApproachFinalize = 210,
        FlowCombatApproachPreResolve = 211,
        FlowCombatApproachCore = 212,
        FlowCombatApproachDirectionSetup = 213,
        FlowCombatApproachSlotGenerate = 214,
        FlowCombatApproachSlotClearance = 215,
        FlowCombatApproachSlotLineOfSight = 216,
        FlowCombatApproachSlotDeduplicate = 217,
        FlowCombatApproachOccupancyReservations = 218,
        FlowCombatApproachOccupancyBuckets = 219,
        FlowCombatApproachOccupancyCandidate = 220,
        FlowCombatApproachOccupancyBucketBuild = 221,
        FlowCombatApproachOccupancyQuerySetup = 222,
        FlowCombatApproachScoreArithmetic = 223,
        FlowNavigationPathPolicyCommit = 224,
        FlowNavigationPathRequestRemoval = 225,
        FlowNavigationPathBudgetAccounting = 226,
        FlowNavigationPathWorldActivation = 227,
        FlowNavigationPathQueueEligibility = 228,
        FlowNavigationPathSliceDispatch = 229,
        FlowNavigationPathQueueCommitLoop = 230,
        FlowNavigationDemandSectorValidation = 231,
        FlowNavigationDemandSourceBinding = 232,
        FlowNavigationDemandSnapshotPublish = 233,
        FlowNavigationPathSearchSchedule = 234,
        FlowNavigationPathSearchComplete = 235,
        FlowNavigationPathSearchCommandExecute = 236,
        FlowCombatApproachOccupancyAgentSync = 237,
        FlowCombatApproachOccupancyBucketFill = 238,
        FlowMovingTargetPolicyAnchorLookup = 239,
        FlowMovingTargetPolicyAnchorDetach = 240,
        FlowMovingTargetPolicyAnchorRebind = 241,
        FlowMovingTargetPolicyAnchorPublish = 242,
        FlowCombatApproachScoreAngle = 243,
        FlowCombatApproachScoreSelfDistance = 244,
        FlowCombatApproachScoreTargetDistance = 245,
        FlowCombatApproachScoreSelection = 246,
        FlowMovingTargetPolicyAnchorRebindBuild = 247,
        FlowMovingTargetPolicyAnchorRebindBoundary = 248,
        FlowMovingTargetPolicyAnchorRebindImport = 249,
        FlowMovingTargetPolicyAnchorRebindShift = 250,
        FlowMovingTargetPolicyAnchorRebindDispose = 251,
        FlowNavigationPathMaterializeImmutableSectorCopy = 252,
        FlowNavigationPathMaterializeImmutablePortalCopy = 253,
        FlowNavigationPathMaterializeImmutableConcat = 254,
        FlowNavigationPathMaterializePublishWitness = 255,
        FlowNavigationPathMaterializePublishCache = 256,
        FlowNavigationPathMaterializePublishTrim = 257,
        FlowNavigationPathMaterializePublishHandle = 258,
        FlowNavigationPathMaterializePublishSuffix = 259,
        FlowMovingTargetPolicyAnchorRebindSectorAccess = 260,
        FlowMovingTargetPolicyAnchorRebindSectorShift = 261,
        FlowNavigationPathMaterializeImmutableHash = 262,
        FlowMovingTargetPolicyAnchorRebindSectorValidation = 263,
        FlowMovingTargetPolicyAnchorReplacementDispose = 264,
        FlowNavigationPathMaterializeSharedSuffixMerge = 265,
        FlowMovingTargetPolicyDisposeSearchState = 266,
        FlowMovingTargetPolicyDisposeConnectors = 267,
        FlowMovingTargetPolicyDisposeHierarchyPolicies = 268,
        FlowMovingTargetPolicyDisposeHierarchyCustomizations = 269,
        FlowMovingTargetPolicyDisposeHierarchySearchStates = 270,
        FlowMovingTargetPolicyDisposeDownwardSearchStates = 271,
        FlowMovingTargetPolicyDisposeHierarchyCustomizationClear = 272,
        FlowMovingTargetPolicyDisposeHierarchyPolicyClear = 273,
        FlowNavigationPathMaterializeStageInitialize = 274,
        FlowNavigationPathMaterializeStageStartPortal = 275,
        FlowNavigationPathMaterializeStageDownward = 276,
        FlowNavigationPathMaterializeStagePolicy = 277,
        FlowNavigationPathMaterializeStageGoalConnector = 278,
        FlowNavigationPathMaterializeStageConversion = 279,
        FlowNavigationPathMaterializeStageImmutableCopy = 280,
        FlowNavigationPathMaterializeStageHash = 281,
        FlowNavigationPathMaterializeStagePublish = 282,
        FlowNavigationPathMaterializeImmutableValidation = 283,
        FlowNavigationPathMaterializeImmutableWitnessHasher = 284,
        FlowNavigationPathMaterializePublishWitnessValidation = 285,
        FlowNavigationPathMaterializePublishWitnessMutation = 286,
        FlowNavigationPathMaterializePublishSuffixResolve = 287,
        FlowNavigationPathMaterializePublishSuffixValidation = 288,
        FlowNavigationPathMaterializePublishSuffixInsert = 289,
        FlowNavigationPathMaterializePublishSuffixBookkeeping = 290,
        FlowNavigationPathMaterializePublishSuffixFinalize = 291,
        FlowNavigationResolveDemandBatch = 292,
        FlowNavigationResolvePathQueueBatch = 293,
        FlowCombatApproachCoreAgentPreparation = 294,
        LogicFrameCommandTimeAndInput = 295,
        LogicFrameCommandValueTeleport = 296,
        LogicFrameCommandCardPlacement = 297,
        LogicFrameCommandCard = 298,
        LogicFrameCommandSkills = 299,
        LogicFrameCommandMovementConstraint = 300,
        LogicFrameCommandCardSetup = 301,
        LogicFrameCommandInteractionTech = 302,
        LogicFrameCommandSpawnLifecycleObstacle = 303,
        LogicFrameCommandCardPlacementVisibilityReset = 304,
        LogicFrameCommandCardPlacementAllEntities = 305,
        LogicFrameCommandCardPlacementDirtyEntities = 306,
        LogicFrameCommandCardPlacementStationary = 307,
        LogicFrameCommandCardPlacementExploration = 308,
        LogicFrameCommandCardPlacementCoverage = 309,
        FlowNavigationPathSliceInitialize = 310,
        FlowNavigationPathSliceGoalConnector = 311,
        FlowNavigationPathSliceCreateHierarchy = 312,
        FlowNavigationPathSliceExpandHierarchy = 313,
        FlowNavigationPathSliceDownward = 314,
        FlowNavigationPathSliceL0 = 315,
        FlowNavigationPathSliceMaterialize = 316,
        FlowNavigationPathSliceComplete = 317,
        LogicFrameCommandCardPlacementDirtyLookup = 318,
        LogicFrameCommandCardPlacementVisibilityCollect = 319,
        LogicFrameCommandCardPlacementVisibilityFilter = 320,
        LogicFrameCommandCardPlacementVisibilityTransition = 321,
        LogicFrameCommandCardPlacementVisibilityRemoveCoverage = 322,
        LogicFrameCommandCardPlacementVisibilityAddCoverage = 323,
        LogicFrameCommandCardPlacementVisibilityPublish = 324,
        FogVisibilityCoverageRows = 325,
        FogVisibilityCoverageCells = 326,
        FogVisibilityCoveragePending = 327,
        FogVisibilityCollectCenter = 328,
        FogVisibilityCollectOctants = 329,
        FogVisibilityCollectIntervals = 330,
        FogVisibilityCollectOctant0 = 331,
        FogVisibilityCollectOctant1 = 332,
        FogVisibilityCollectOctant2 = 333,
        FogVisibilityCollectOctant3 = 334,
        FogVisibilityCollectOctant4 = 335,
        FogVisibilityCollectOctant5 = 336,
        FogVisibilityCollectOctant6 = 337,
        FogVisibilityCollectOctant7 = 338,
        FogVisibilityCoverageCellsSegment0 = 339,
        FogVisibilityCoverageCellsSegment1 = 340,
        FogVisibilityCoverageCellsSegment2 = 341,
        FogVisibilityCoverageCellsSegment3 = 342,
        FlowNavigationGoalConnectorSearchSlice = 343,
        FlowNavigationGoalConnectorInputCollection = 344,
        FlowNavigationGoalConnectorLink = 345,
        FlowNavigationGoalConnectorUnattributed = 417,
        FlowNavigationDemandAgentPreparation = 346,
        FlowNavigationDemandAssembly = 347,
        LogicFrameListenerAttributed = 348,
        LogicFrameListenerUnattributed = 349,
        FlowNavigationPathSliceDispatchUnattributed = 350,
        CharacterTargetingReachabilityPreparation = 351,
        CharacterTargetingReachabilityCacheLookup = 352,
        CharacterTargetingReachabilityCandidateSelection = 353,
        CharacterTargetingReachabilityCacheStore = 354,
        FlowNavigationPathSliceDispatchPreparation = 355,
        FlowNavigationPathSliceDispatchFinalization = 356,
        FlowNavigationPathQueueLoopUnattributed = 357,
        FlowCombatApproachSlotCacheLookup = 358,
        FlowNavigationPathWorldStateEnumeration = 359,
        FlowNavigationPathQueueProcessingUnattributed = 360,
        FlowNavigationPathWorldOuterUnattributed = 361,
        FlowCombatApproachCoreScorePhase = 362,
        FlowCombatApproachCoreExpandedPhase = 363,
        FlowCombatApproachCoreUnattributed = 364,
        FlowNavigationPathWorldRestore = 365,
        FlowCombatApproachCoreScoreUnattributed = 366,
        FlowNavigationPathWorldBody = 367,
        FlowCombatApproachCoreBody = 368,
        FlowCombatApproachOccupancyPreparation = 369,
        FlowCombatApproachOccupancySnapshot = 370,
        FlowCombatApproachOccupancySlotFilter = 371,
        FlowCombatApproachSlotCacheArrayMaterialize = 372,
        FlowCombatApproachSlotCachePublish = 373,
        FlowCombatApproachOccupancyPrepareIteration = 374,
        FlowNavigationPathDiagnosticCapture = 375,
        FlowNavigationPathBudgetEnd = 376,
        FlowCombatApproachCoreScoreIslandFilter = 377,
        FlowCombatApproachOccupancyBucketPosition = 378,
        FlowCombatApproachOccupancyBucketTarget = 379,
        FlowCombatApproachOccupancyBucketThreshold = 380,
        FogVisibilityCollectGeometryIntervals = 381,
        FlowMovingTargetPolicyClassification = 382,
        FlowMovingTargetPolicyAnchorState = 383,
        FlowMovingTargetPolicyFinalGoalResolution = 384,
        FlowMovingTargetPolicyAnchorDictionary = 385,
        FlowMovingTargetPolicyAnchorGoalState = 386,
        FlowMovingTargetPolicyAnchorNavState = 387,
        FlowNavigationPathInitializeSameSector = 388,
        FlowNavigationPathInitializePolicy = 389,
        FlowNavigationPathInitializeHierarchyResolve = 390,
        FlowNavigationPathInitializeHierarchySelection = 391,
        FlowNavigationDemandQueueLookup = 392,
        FlowNavigationDemandQueueCreate = 393,
        FlowNavigationDemandSourceLookup = 394,
        FlowNavigationDemandSourceInsert = 395,
        FlowCombatApproachOccupancyPrepareGuard = 396,
        FlowCombatApproachOccupancyPrepareSlotLookup = 397,
        FlowCombatApproachOccupancyBucketIncremental = 398,
        FlowCombatApproachOccupancyPrepareInitialization = 399,
        FlowCombatApproachOccupancyPrepareCacheState = 400,
        FlowMovingTargetPolicyAnchorTargetResolve = 401,
        FlowMovingTargetPolicyAnchorKeyBuild = 402,
        FlowMovingTargetPolicyAnchorDictionaryLookup = 403,
        FlowMovingTargetPolicyAnchorDictionaryCreate = 404,
        FlowMovingTargetPolicyAnchorDictionaryInsert = 405,
        FlowMovingTargetPolicyBatchResolutionLookup = 406,
        FlowMovingTargetPolicyBatchStatePublish = 407,
        FlowMovingTargetPolicyAnchorProjectionCacheCheck = 408,
        FlowMovingTargetPolicyAnchorProjectionIslandResolve = 409,
        FlowMovingTargetPolicyAnchorProjectionRequest = 410,
        FlowMovingTargetPolicyInputResolution = 411,
        FlowMovingTargetPolicyBatchContinuation = 412,
        FlowMovingTargetPolicyBatchEarlyPath = 413,
        FlowMovingTargetPolicyRawGoalPath = 414,
        FlowMovingTargetPolicyOutputInitialization = 415,
        LogicEntityNavigationPositionSync = 416,
        FlowStableGoalBody = 418,
        FlowStableGoalPrologue = 419,
        FlowStableGoalMeasurementBegin = 420,
        FlowStableGoalMeasurementFinalize = 421,
        FlowNavigationPolicyInitializeLookup = 422,
        FlowNavigationPolicyInitializeAnchor = 423,
        FlowNavigationPolicyInitializeConstruct = 424,
        FlowNavigationPolicyInitializeAuthority = 425,
        FlowNavigationPolicyInitializePin = 426,
        FlowStableGoalProfilerRecord = 427,
        Count = 428
    }

    public static class MainThreadFrameProfiler
    {
        private const int SlowFrameMilliseconds = 30;
        private const int ForceLogFrameMilliseconds = 200;
        private const int ForceTrackedLogMilliseconds = 50;
        private const int MinLogFrameInterval = 30;
        private const long HighAllocationBytes = 512 * 1024;
        private const long ForceAllocationLogBytes = 4 * 1024 * 1024;
        private static readonly long SlowFrameTicks = Stopwatch.Frequency * SlowFrameMilliseconds / 1000;
        private static readonly long ForceLogFrameTicks = Stopwatch.Frequency * ForceLogFrameMilliseconds / 1000;
        private static readonly long ForceTrackedLogTicks = Stopwatch.Frequency * ForceTrackedLogMilliseconds / 1000;
        private static readonly long[] ScopeTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly int[] ScopeCalls = new int[(int)MainThreadPerfScope.Count];
        private static readonly long[] LastCompletedScopeTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly int[] LastCompletedScopeCalls = new int[(int)MainThreadPerfScope.Count];
        private static readonly long[] CurrentLogicTickScopeTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly long[] CurrentMaxLogicTickScopeTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly long[] LastCompletedMaxLogicTickScopeTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly long[] CurrentLogicTickScopeRecordFirstTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly long[] CurrentLogicTickScopeRecordSubsequentTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly int[] CurrentLogicTickScopeRecordCounts = new int[(int)MainThreadPerfScope.Count];
        private static readonly long[] CurrentMaxLogicTickScopeRecordFirstTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly long[] CurrentMaxLogicTickScopeRecordSubsequentTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly int[] CurrentMaxLogicTickScopeRecordCounts = new int[(int)MainThreadPerfScope.Count];
        private static readonly long[] LastCompletedMaxLogicTickScopeRecordFirstTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly long[] LastCompletedMaxLogicTickScopeRecordSubsequentTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly int[] LastCompletedMaxLogicTickScopeRecordCounts = new int[(int)MainThreadPerfScope.Count];
        private static readonly long[] CurrentMaxLogicTickInvocationFirstTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly long[] CurrentMaxLogicTickInvocationSubsequentTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly int[] CurrentMaxLogicTickInvocationCounts = new int[(int)MainThreadPerfScope.Count];
        private static readonly long[] CurrentLogicTickInvocationFirstTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly long[] CurrentLogicTickInvocationSubsequentTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly int[] CurrentLogicTickInvocationCounts = new int[(int)MainThreadPerfScope.Count];
        private static readonly long[] LastCompletedMaxLogicTickInvocationFirstTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly long[] LastCompletedMaxLogicTickInvocationSubsequentTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly int[] LastCompletedMaxLogicTickInvocationCounts = new int[(int)MainThreadPerfScope.Count];
        private static readonly long[] InvocationFirstTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly long[] InvocationSubsequentTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly int[] InvocationCounts = new int[(int)MainThreadPerfScope.Count];
        private static readonly ulong[] InvocationFirstLogicFrames = new ulong[(int)MainThreadPerfScope.Count];
        private static readonly long[] ScopeRecordFirstTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly long[] ScopeRecordSubsequentTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly int[] ScopeRecordCounts = new int[(int)MainThreadPerfScope.Count];
        private static readonly ulong[] ScopeRecordFirstLogicFrames = new ulong[(int)MainThreadPerfScope.Count];
        private static readonly long[] ScopeActiveTickFirstTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly long[] ScopeActiveTickSubsequentTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly int[] ScopeActiveTickCounts = new int[(int)MainThreadPerfScope.Count];
        private static readonly ulong[] ScopeActiveTickFirstLogicFrames = new ulong[(int)MainThreadPerfScope.Count];
        private const int LogicTickHistoryCapacity = 2048;
        private static readonly ulong[] LogicTickHistoryFrames = new ulong[LogicTickHistoryCapacity];
        private static readonly long[] LogicTickHistoryDurations = new long[LogicTickHistoryCapacity];
        private static readonly ulong[] LogicTickHistoryWorkloadSignatures = new ulong[LogicTickHistoryCapacity];
        private static readonly long[] LogicTickHistoryScopeTicks = new long[LogicTickHistoryCapacity * (int)MainThreadPerfScope.Count];
        private static readonly int[] LogicTickHistoryScopeCalls = new int[LogicTickHistoryCapacity * (int)MainThreadPerfScope.Count];
        private static readonly int[] LogicTickHistoryInvocationCalls = new int[LogicTickHistoryCapacity * (int)MainThreadPerfScope.Count];
        private static int _logicTickHistoryNext;
        private static int _logicTickHistoryCount;
        private static readonly long[] ScopeAllocatedBytes = new long[(int)MainThreadPerfScope.Count];
        private static readonly long[] IntervalScopeAllocatedBytes = new long[(int)MainThreadPerfScope.Count];
        private static bool _recordMeasurementActive;
        private static long _recordMeasurementTicks;
        private static int _recordMeasurementCount;
        private static readonly Dictionary<Type, LogicListenerSample> LogicListenerSamples = new Dictionary<Type, LogicListenerSample>();
        private static readonly List<LogicListenerSample> LogicListenerSortBuffer = new List<LogicListenerSample>();
        private static readonly Dictionary<Type, long> CurrentLogicTickListenerTicks = new Dictionary<Type, long>();
        private static readonly Dictionary<Type, long> LastCompletedMaxLogicTickListenerTicks = new Dictionary<Type, long>();
        private static readonly List<LogicListenerTypeSample> LogicListenerTypeSortBuffer = new List<LogicListenerTypeSample>();
        private static readonly ProfilerMarkerSampler[] MarkerSamplers =
        {
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "PlayerLoop"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "EarlyUpdate"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "FixedUpdate"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "PreUpdate"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "Update.ScriptRunBehaviourUpdate"),
            new ProfilerMarkerSampler(ProfilerCategory.Scripts, "BehaviourUpdate"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "PreLateUpdate"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "PostLateUpdate"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "PlayerSendFrameStarted"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "PlayerSendFrameComplete"),
            new ProfilerMarkerSampler(ProfilerCategory.Render, "Camera.Render"),
            new ProfilerMarkerSampler(ProfilerCategory.Render, "RenderPipelineManager.DoRenderLoop_Internal"),
            new ProfilerMarkerSampler(ProfilerCategory.Render, "RenderLoop.Draw"),
            new ProfilerMarkerSampler(ProfilerCategory.Render, "Gfx.PresentFrame"),
            new ProfilerMarkerSampler(ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread"),
            new ProfilerMarkerSampler(ProfilerCategory.Gui, "Canvas.BuildBatch"),
            new ProfilerMarkerSampler(ProfilerCategory.Gui, "Canvas.SendWillRenderCanvases"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "WaitForTargetFPS"),
            new ProfilerMarkerSampler(ProfilerCategory.Physics, "Physics.Simulate"),
            new ProfilerMarkerSampler(ProfilerCategory.Memory, "GC.Collect"),
        };

        private static bool _initialized;
        private static bool _recordersInitialized;
        private static int _frame;
        private static int _lastLogFrame = -100000;
        private static long _frameStartTicks;
        private static long _frameStartAllocatedBytes;
        private static int _frameStartCollectionCount;
        private static long _intervalAllocatedBytes;
        private static long _currentMaxLogicTickTicks;
        private static ulong _currentLogicTickFrame;
        private static ulong _currentMaxLogicTickFrame;
        private static bool _logicTickActive;

        public static bool LoggingEnabled { get; set; }
        public readonly struct ScopeRecordSample
        {
            public ScopeRecordSample(ulong logicFrame, MainThreadPerfScope scope, long ticks, long recordedAt, Type listenerType = null)
            {
                LogicFrame = logicFrame;
                Scope = scope;
                Ticks = ticks;
                RecordedAt = recordedAt;
                ListenerType = listenerType;
                CollectionCount = GC.CollectionCount(0);
            }

            public ulong LogicFrame { get; }
            public MainThreadPerfScope Scope { get; }
            public int CollectionCount { get; }
            public long Ticks { get; }
            public long RecordedAt { get; }
            public Type ListenerType { get; }
        }

        private const int ScopeRecordCaptureCapacity = 1048576;
        private static List<ScopeRecordSample> _scopeRecordCapture;
        private static bool _scopeRecordCaptureEnabled;

        public static IReadOnlyList<ScopeRecordSample> CapturedScopeRecords => _scopeRecordCapture;

        public static void BeginScopeRecordCapture()
        {
            if (_logicTickActive || _scopeRecordCaptureEnabled)
                throw new InvalidOperationException("Scope record capture must begin once, outside a logic Tick.");
            _scopeRecordCapture = new List<ScopeRecordSample>(ScopeRecordCaptureCapacity);
            _scopeRecordCaptureEnabled = true;
        }

        public static void EndScopeRecordCapture()
        {
            if (_logicTickActive)
                throw new InvalidOperationException("Scope record capture cannot end during a logic Tick.");
            _scopeRecordCaptureEnabled = false;
        }
        public static bool ConsoleLoggingEnabled { get; set; } = true;
        public static int LastCompletedFrame { get; private set; } = -1;
        public static double LastCompletedFrameMilliseconds { get; private set; }
        public static double LastCompletedTrackedMilliseconds { get; private set; }
        public static double LastCompletedUntrackedMilliseconds { get; private set; }
        public static double LastCompletedLogicFrameMilliseconds { get; private set; }
        public static double LastCompletedMaxLogicTickMilliseconds { get; private set; }
        public static ulong LastCompletedMaxLogicTickFrame { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _scopeRecordCaptureEnabled = false;
            _scopeRecordCapture = null;
            LoggingEnabled = false;
            ConsoleLoggingEnabled = true;
            LastCompletedFrame = -1;
            LastCompletedFrameMilliseconds = 0.0;
            LastCompletedTrackedMilliseconds = 0.0;
            LastCompletedUntrackedMilliseconds = 0.0;
            LastCompletedLogicFrameMilliseconds = 0.0;
            LastCompletedMaxLogicTickMilliseconds = 0.0;
            LastCompletedMaxLogicTickFrame = 0;
            _recordMeasurementActive = false;
            _recordMeasurementTicks = 0L;
            _recordMeasurementCount = 0;
            _currentMaxLogicTickTicks = 0L;
            _currentLogicTickFrame = 0;
            _currentMaxLogicTickFrame = 0;
            Array.Clear(LastCompletedScopeTicks, 0, LastCompletedScopeTicks.Length);
            Array.Clear(LastCompletedScopeCalls, 0, LastCompletedScopeCalls.Length);
            Array.Clear(CurrentLogicTickScopeTicks, 0, CurrentLogicTickScopeTicks.Length);
            Array.Clear(CurrentMaxLogicTickScopeTicks, 0, CurrentMaxLogicTickScopeTicks.Length);
            Array.Clear(LastCompletedMaxLogicTickScopeTicks, 0, LastCompletedMaxLogicTickScopeTicks.Length);
            Array.Clear(CurrentLogicTickScopeRecordFirstTicks, 0, CurrentLogicTickScopeRecordFirstTicks.Length);
            Array.Clear(CurrentLogicTickScopeRecordSubsequentTicks, 0, CurrentLogicTickScopeRecordSubsequentTicks.Length);
            Array.Clear(CurrentLogicTickScopeRecordCounts, 0, CurrentLogicTickScopeRecordCounts.Length);
            Array.Clear(CurrentMaxLogicTickScopeRecordFirstTicks, 0, CurrentMaxLogicTickScopeRecordFirstTicks.Length);
            Array.Clear(CurrentMaxLogicTickScopeRecordSubsequentTicks, 0, CurrentMaxLogicTickScopeRecordSubsequentTicks.Length);
            Array.Clear(CurrentMaxLogicTickScopeRecordCounts, 0, CurrentMaxLogicTickScopeRecordCounts.Length);
            Array.Clear(LastCompletedMaxLogicTickScopeRecordFirstTicks, 0, LastCompletedMaxLogicTickScopeRecordFirstTicks.Length);
            Array.Clear(LastCompletedMaxLogicTickScopeRecordSubsequentTicks, 0, LastCompletedMaxLogicTickScopeRecordSubsequentTicks.Length);
            Array.Clear(LastCompletedMaxLogicTickScopeRecordCounts, 0, LastCompletedMaxLogicTickScopeRecordCounts.Length);
            Array.Clear(CurrentMaxLogicTickInvocationFirstTicks, 0, CurrentMaxLogicTickInvocationFirstTicks.Length);
            Array.Clear(CurrentMaxLogicTickInvocationSubsequentTicks, 0, CurrentMaxLogicTickInvocationSubsequentTicks.Length);
            Array.Clear(CurrentMaxLogicTickInvocationCounts, 0, CurrentMaxLogicTickInvocationCounts.Length);
            Array.Clear(CurrentLogicTickInvocationFirstTicks, 0, CurrentLogicTickInvocationFirstTicks.Length);
            Array.Clear(CurrentLogicTickInvocationSubsequentTicks, 0, CurrentLogicTickInvocationSubsequentTicks.Length);
            Array.Clear(CurrentLogicTickInvocationCounts, 0, CurrentLogicTickInvocationCounts.Length);
            Array.Clear(LastCompletedMaxLogicTickInvocationFirstTicks, 0, LastCompletedMaxLogicTickInvocationFirstTicks.Length);
            Array.Clear(LastCompletedMaxLogicTickInvocationSubsequentTicks, 0, LastCompletedMaxLogicTickInvocationSubsequentTicks.Length);
            Array.Clear(LastCompletedMaxLogicTickInvocationCounts, 0, LastCompletedMaxLogicTickInvocationCounts.Length);
            Array.Clear(InvocationFirstTicks, 0, InvocationFirstTicks.Length);
            Array.Clear(InvocationSubsequentTicks, 0, InvocationSubsequentTicks.Length);
            Array.Clear(InvocationCounts, 0, InvocationCounts.Length);
            Array.Clear(InvocationFirstLogicFrames, 0, InvocationFirstLogicFrames.Length);
            Array.Clear(ScopeRecordFirstTicks, 0, ScopeRecordFirstTicks.Length);
            Array.Clear(ScopeRecordSubsequentTicks, 0, ScopeRecordSubsequentTicks.Length);
            Array.Clear(ScopeRecordCounts, 0, ScopeRecordCounts.Length);
            Array.Clear(ScopeRecordFirstLogicFrames, 0, ScopeRecordFirstLogicFrames.Length);
            Array.Clear(ScopeActiveTickFirstTicks, 0, ScopeActiveTickFirstTicks.Length);
            Array.Clear(ScopeActiveTickSubsequentTicks, 0, ScopeActiveTickSubsequentTicks.Length);
            Array.Clear(ScopeActiveTickCounts, 0, ScopeActiveTickCounts.Length);
            Array.Clear(ScopeActiveTickFirstLogicFrames, 0, ScopeActiveTickFirstLogicFrames.Length);
            Array.Clear(LogicTickHistoryFrames, 0, LogicTickHistoryFrames.Length);
            Array.Clear(LogicTickHistoryDurations, 0, LogicTickHistoryDurations.Length);
            Array.Clear(LogicTickHistoryWorkloadSignatures, 0, LogicTickHistoryWorkloadSignatures.Length);
            Array.Clear(LogicTickHistoryScopeTicks, 0, LogicTickHistoryScopeTicks.Length);
            Array.Clear(LogicTickHistoryScopeCalls, 0, LogicTickHistoryScopeCalls.Length);
            Array.Clear(LogicTickHistoryInvocationCalls, 0, LogicTickHistoryInvocationCalls.Length);
            _logicTickHistoryNext = 0;
            _logicTickHistoryCount = 0;
            CurrentLogicTickListenerTicks.Clear();
            LastCompletedMaxLogicTickListenerTicks.Clear();
            _logicTickActive = false;
        }

        public static void ResetPerformanceWindow()
        {
            if (_logicTickActive)
                throw new InvalidOperationException("Cannot reset the performance window while a logic Tick is active.");

            int frame = Time.frameCount;
            long now = Stopwatch.GetTimestamp();
            _initialized = true;
            _frame = frame;
            _frameStartTicks = now;
            _frameStartAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
            _frameStartCollectionCount = GetCollectionCount();
            _lastLogFrame = -100000;
            LastCompletedFrame = -1;
            LastCompletedFrameMilliseconds = 0.0;
            LastCompletedTrackedMilliseconds = 0.0;
            LastCompletedUntrackedMilliseconds = 0.0;
            LastCompletedLogicFrameMilliseconds = 0.0;
            LastCompletedMaxLogicTickMilliseconds = 0.0;
            LastCompletedMaxLogicTickFrame = 0;
            _intervalAllocatedBytes = 0L;
            _currentMaxLogicTickTicks = 0L;
            _currentLogicTickFrame = 0;
            _currentMaxLogicTickFrame = 0;
            Array.Clear(ScopeTicks, 0, ScopeTicks.Length);
            Array.Clear(ScopeCalls, 0, ScopeCalls.Length);
            Array.Clear(LastCompletedScopeTicks, 0, LastCompletedScopeTicks.Length);
            Array.Clear(LastCompletedScopeCalls, 0, LastCompletedScopeCalls.Length);
            Array.Clear(ScopeAllocatedBytes, 0, ScopeAllocatedBytes.Length);
            Array.Clear(IntervalScopeAllocatedBytes, 0, IntervalScopeAllocatedBytes.Length);
            Array.Clear(CurrentLogicTickScopeTicks, 0, CurrentLogicTickScopeTicks.Length);
            Array.Clear(CurrentMaxLogicTickScopeTicks, 0, CurrentMaxLogicTickScopeTicks.Length);
            Array.Clear(LastCompletedMaxLogicTickScopeTicks, 0, LastCompletedMaxLogicTickScopeTicks.Length);
            Array.Clear(CurrentLogicTickScopeRecordFirstTicks, 0, CurrentLogicTickScopeRecordFirstTicks.Length);
            Array.Clear(CurrentLogicTickScopeRecordSubsequentTicks, 0, CurrentLogicTickScopeRecordSubsequentTicks.Length);
            Array.Clear(CurrentLogicTickScopeRecordCounts, 0, CurrentLogicTickScopeRecordCounts.Length);
            Array.Clear(CurrentMaxLogicTickScopeRecordFirstTicks, 0, CurrentMaxLogicTickScopeRecordFirstTicks.Length);
            Array.Clear(CurrentMaxLogicTickScopeRecordSubsequentTicks, 0, CurrentMaxLogicTickScopeRecordSubsequentTicks.Length);
            Array.Clear(CurrentMaxLogicTickScopeRecordCounts, 0, CurrentMaxLogicTickScopeRecordCounts.Length);
            Array.Clear(LastCompletedMaxLogicTickScopeRecordFirstTicks, 0, LastCompletedMaxLogicTickScopeRecordFirstTicks.Length);
            Array.Clear(LastCompletedMaxLogicTickScopeRecordSubsequentTicks, 0, LastCompletedMaxLogicTickScopeRecordSubsequentTicks.Length);
            Array.Clear(LastCompletedMaxLogicTickScopeRecordCounts, 0, LastCompletedMaxLogicTickScopeRecordCounts.Length);
            Array.Clear(CurrentMaxLogicTickInvocationFirstTicks, 0, CurrentMaxLogicTickInvocationFirstTicks.Length);
            Array.Clear(CurrentMaxLogicTickInvocationSubsequentTicks, 0, CurrentMaxLogicTickInvocationSubsequentTicks.Length);
            Array.Clear(CurrentMaxLogicTickInvocationCounts, 0, CurrentMaxLogicTickInvocationCounts.Length);
            Array.Clear(CurrentLogicTickInvocationFirstTicks, 0, CurrentLogicTickInvocationFirstTicks.Length);
            Array.Clear(CurrentLogicTickInvocationSubsequentTicks, 0, CurrentLogicTickInvocationSubsequentTicks.Length);
            Array.Clear(CurrentLogicTickInvocationCounts, 0, CurrentLogicTickInvocationCounts.Length);
            Array.Clear(LastCompletedMaxLogicTickInvocationFirstTicks, 0, LastCompletedMaxLogicTickInvocationFirstTicks.Length);
            Array.Clear(LastCompletedMaxLogicTickInvocationSubsequentTicks, 0, LastCompletedMaxLogicTickInvocationSubsequentTicks.Length);
            Array.Clear(LastCompletedMaxLogicTickInvocationCounts, 0, LastCompletedMaxLogicTickInvocationCounts.Length);
            Array.Clear(ScopeRecordFirstTicks, 0, ScopeRecordFirstTicks.Length);
            Array.Clear(ScopeRecordSubsequentTicks, 0, ScopeRecordSubsequentTicks.Length);
            Array.Clear(ScopeRecordCounts, 0, ScopeRecordCounts.Length);
            Array.Clear(ScopeRecordFirstLogicFrames, 0, ScopeRecordFirstLogicFrames.Length);
            Array.Clear(ScopeActiveTickFirstTicks, 0, ScopeActiveTickFirstTicks.Length);
            Array.Clear(ScopeActiveTickSubsequentTicks, 0, ScopeActiveTickSubsequentTicks.Length);
            Array.Clear(ScopeActiveTickCounts, 0, ScopeActiveTickCounts.Length);
            Array.Clear(ScopeActiveTickFirstLogicFrames, 0, ScopeActiveTickFirstLogicFrames.Length);
            Array.Clear(LogicTickHistoryFrames, 0, LogicTickHistoryFrames.Length);
            Array.Clear(LogicTickHistoryDurations, 0, LogicTickHistoryDurations.Length);
            Array.Clear(LogicTickHistoryWorkloadSignatures, 0, LogicTickHistoryWorkloadSignatures.Length);
            Array.Clear(LogicTickHistoryScopeTicks, 0, LogicTickHistoryScopeTicks.Length);
            Array.Clear(LogicTickHistoryScopeCalls, 0, LogicTickHistoryScopeCalls.Length);
            Array.Clear(LogicTickHistoryInvocationCalls, 0, LogicTickHistoryInvocationCalls.Length);
            _logicTickHistoryNext = 0;
            _logicTickHistoryCount = 0;
            CurrentLogicTickListenerTicks.Clear();
            LastCompletedMaxLogicTickListenerTicks.Clear();
            _logicTickActive = false;
        }

        public static double GetLastCompletedScopeMilliseconds(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= LastCompletedScopeTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return TicksToMs(LastCompletedScopeTicks[index]);
        }

        public static int GetLastCompletedScopeCalls(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= LastCompletedScopeCalls.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return LastCompletedScopeCalls[index];
        }

        public static double GetLastCompletedMaxLogicTickScopeMilliseconds(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= LastCompletedMaxLogicTickScopeTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return TicksToMs(LastCompletedMaxLogicTickScopeTicks[index]);
        }

        public static double GetLastCompletedMaxLogicTickScopeRecordFirstMilliseconds(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= LastCompletedMaxLogicTickScopeRecordFirstTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return TicksToMs(LastCompletedMaxLogicTickScopeRecordFirstTicks[index]);
        }

        public static double GetLastCompletedMaxLogicTickScopeRecordSubsequentMilliseconds(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= LastCompletedMaxLogicTickScopeRecordSubsequentTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return TicksToMs(LastCompletedMaxLogicTickScopeRecordSubsequentTicks[index]);
        }

        public static int GetLastCompletedMaxLogicTickScopeRecordCount(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= LastCompletedMaxLogicTickScopeRecordCounts.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return LastCompletedMaxLogicTickScopeRecordCounts[index];
        }

        public static double GetLastCompletedMaxLogicTickInvocationFirstMilliseconds(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= LastCompletedMaxLogicTickInvocationFirstTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return TicksToMs(LastCompletedMaxLogicTickInvocationFirstTicks[index]);
        }

        public static double GetLastCompletedMaxLogicTickInvocationSubsequentMilliseconds(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= LastCompletedMaxLogicTickInvocationSubsequentTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return TicksToMs(LastCompletedMaxLogicTickInvocationSubsequentTicks[index]);
        }

        public static int GetLastCompletedMaxLogicTickInvocationCount(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= LastCompletedMaxLogicTickInvocationCounts.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return LastCompletedMaxLogicTickInvocationCounts[index];
        }

        public static double GetInvocationFirstMilliseconds(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= InvocationFirstTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return TicksToMs(InvocationFirstTicks[index]);
        }

        public static double GetInvocationSubsequentMilliseconds(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= InvocationSubsequentTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return TicksToMs(InvocationSubsequentTicks[index]);
        }

        public static int GetInvocationCount(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= InvocationCounts.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return InvocationCounts[index];
        }

        public static ulong GetInvocationFirstLogicFrame(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= InvocationFirstLogicFrames.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return InvocationFirstLogicFrames[index];
        }

        public static double GetScopeRecordFirstMilliseconds(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= ScopeRecordFirstTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return TicksToMs(ScopeRecordFirstTicks[index]);
        }

        public static double GetScopeRecordSubsequentMilliseconds(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= ScopeRecordSubsequentTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return TicksToMs(ScopeRecordSubsequentTicks[index]);
        }

        public static int GetScopeRecordCount(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= ScopeRecordCounts.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return ScopeRecordCounts[index];
        }

        public static ulong GetScopeRecordFirstLogicFrame(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= ScopeRecordFirstLogicFrames.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return ScopeRecordFirstLogicFrames[index];
        }

        public static double GetScopeActiveTickSubsequentMilliseconds(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= ScopeActiveTickSubsequentTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return TicksToMs(ScopeActiveTickSubsequentTicks[index]);
        }

        public static double GetScopeActiveTickFirstMilliseconds(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= ScopeActiveTickFirstTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return TicksToMs(ScopeActiveTickFirstTicks[index]);
        }

        public static ulong GetScopeActiveTickFirstLogicFrame(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= ScopeActiveTickFirstLogicFrames.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return ScopeActiveTickFirstLogicFrames[index];
        }

        public static int GetScopeActiveTickCount(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= ScopeActiveTickCounts.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return ScopeActiveTickCounts[index];
        }

        public static bool TryGetNextLogicTickFrame(ulong afterLogicFrame, out ulong logicFrame)
        {
            logicFrame = 0;
            bool found = false;
            for (int i = 0; i < _logicTickHistoryCount; i++)
            {
                int historyIndex = (_logicTickHistoryNext - _logicTickHistoryCount + i + LogicTickHistoryCapacity) % LogicTickHistoryCapacity;
                ulong candidate = LogicTickHistoryFrames[historyIndex];
                if (candidate <= afterLogicFrame || (found && candidate >= logicFrame))
                    continue;
                logicFrame = candidate;
                found = true;
            }
            return found;
        }

        public static ulong GetLogicTickWorkloadSignature(ulong logicFrame)
        {
            int historyIndex = FindLogicTickHistoryIndex(logicFrame);
            return LogicTickHistoryWorkloadSignatures[historyIndex];
        }

        public static bool TryGetLogicTickHotScopeComparison(
            ulong targetLogicFrame,
            MainThreadPerfScope scope,
            out ulong hotLogicFrame,
            out double hotMilliseconds,
            out int targetCalls,
            out int hotCalls)
        {
            int targetIndex = FindLogicTickHistoryIndex(targetLogicFrame);
            int scopeIndex = ValidateScopeIndex(scope);
            int targetOffset = targetIndex * (int)MainThreadPerfScope.Count;
            targetCalls = LogicTickHistoryScopeCalls[targetOffset + scopeIndex];
            hotLogicFrame = 0;
            hotMilliseconds = 0.0;
            hotCalls = 0;
            if (targetCalls <= 0)
                return false;

            ulong targetSignature = LogicTickHistoryWorkloadSignatures[targetIndex];
            bool found = false;
            double bestMilliseconds = double.MaxValue;
            for (int i = 0; i < _logicTickHistoryCount; i++)
            {
                int candidateIndex = (_logicTickHistoryNext - _logicTickHistoryCount + i + LogicTickHistoryCapacity) % LogicTickHistoryCapacity;
                if (LogicTickHistoryFrames[candidateIndex] <= targetLogicFrame
                    || LogicTickHistoryWorkloadSignatures[candidateIndex] != targetSignature)
                    continue;

                int candidateOffset = candidateIndex * (int)MainThreadPerfScope.Count;
                if (LogicTickHistoryScopeCalls[candidateOffset + scopeIndex] != targetCalls
                    || !LogicTickTickCallsMatch(candidateOffset, targetOffset))
                    continue;

                double candidateMilliseconds = TicksToMs(LogicTickHistoryScopeTicks[candidateOffset + scopeIndex]);
                if (found && candidateMilliseconds >= bestMilliseconds)
                    continue;
                found = true;
                bestMilliseconds = candidateMilliseconds;
                hotLogicFrame = LogicTickHistoryFrames[candidateIndex];
                hotMilliseconds = candidateMilliseconds;
                hotCalls = LogicTickHistoryScopeCalls[candidateOffset + scopeIndex];
            }
            return found;
        }

        public static double GetLogicTickScopeMilliseconds(ulong logicFrame, MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= (int)MainThreadPerfScope.Count)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            for (int i = 0; i < _logicTickHistoryCount; i++)
            {
                int historyIndex = (_logicTickHistoryNext - _logicTickHistoryCount + i + LogicTickHistoryCapacity) % LogicTickHistoryCapacity;
                if (LogicTickHistoryFrames[historyIndex] == logicFrame)
                    return TicksToMs(LogicTickHistoryScopeTicks[historyIndex * (int)MainThreadPerfScope.Count + index]);
            }
            throw new InvalidOperationException($"Logic Tick scope snapshot is unavailable. logicFrame={logicFrame}, scope={scope}.");
        }

        public static int GetLogicTickScopeCalls(ulong logicFrame, MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= (int)MainThreadPerfScope.Count)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            for (int i = 0; i < _logicTickHistoryCount; i++)
            {
                int historyIndex = (_logicTickHistoryNext - _logicTickHistoryCount + i + LogicTickHistoryCapacity) % LogicTickHistoryCapacity;
                if (LogicTickHistoryFrames[historyIndex] == logicFrame)
                    return LogicTickHistoryScopeCalls[historyIndex * (int)MainThreadPerfScope.Count + index];
            }
            throw new InvalidOperationException($"Logic Tick scope snapshot is unavailable. logicFrame={logicFrame}, scope={scope}.");
        }

        public static string GetLastCompletedMaxLogicTickListenerSummary()
        {
            if (LastCompletedMaxLogicTickListenerTicks.Count == 0)
                return "none";

            LogicListenerTypeSortBuffer.Clear();
            foreach (KeyValuePair<Type, long> entry in LastCompletedMaxLogicTickListenerTicks)
            {
                if (entry.Value > 0L)
                    LogicListenerTypeSortBuffer.Add(new LogicListenerTypeSample(entry.Key, entry.Value));
            }
            LogicListenerTypeSortBuffer.Sort(CompareLogicListenerTypeSamples);

            var builder = new System.Text.StringBuilder(192);
            int count = Math.Min(12, LogicListenerTypeSortBuffer.Count);
            for (int i = 0; i < count; i++)
            {
                if (builder.Length > 0)
                    builder.Append(',');
                LogicListenerTypeSample sample = LogicListenerTypeSortBuffer[i];
                builder.Append(sample.ListenerType.FullName)
                    .Append('=')
                    .Append(TicksToMs(sample.Ticks).ToString("F3", System.Globalization.CultureInfo.InvariantCulture))
                    .Append("ms");
            }
            return builder.Length > 0 ? builder.ToString() : "none";
        }

        public static void BeginLogicTick(ulong logicFrame)
        {
            if (!LoggingEnabled)
                return;
            EnsureFrame();
            Array.Clear(CurrentLogicTickScopeTicks, 0, CurrentLogicTickScopeTicks.Length);
            Array.Clear(CurrentLogicTickScopeRecordFirstTicks, 0, CurrentLogicTickScopeRecordFirstTicks.Length);
            Array.Clear(CurrentLogicTickScopeRecordSubsequentTicks, 0, CurrentLogicTickScopeRecordSubsequentTicks.Length);
            Array.Clear(CurrentLogicTickScopeRecordCounts, 0, CurrentLogicTickScopeRecordCounts.Length);
            Array.Clear(CurrentLogicTickInvocationFirstTicks, 0, CurrentLogicTickInvocationFirstTicks.Length);
            Array.Clear(CurrentLogicTickInvocationSubsequentTicks, 0, CurrentLogicTickInvocationSubsequentTicks.Length);
            Array.Clear(CurrentLogicTickInvocationCounts, 0, CurrentLogicTickInvocationCounts.Length);
            CurrentLogicTickListenerTicks.Clear();
            _currentLogicTickFrame = logicFrame;
            _logicTickActive = true;
        }

        public static void PulseFrame()
        {
            EnsureFrame();
        }

        public static void Record(MainThreadPerfScope scope, long ticks)
        {
            Record(scope, ticks, 0L);
        }

        public static void Record(MainThreadPerfScope scope, long ticks, long allocatedBytes)
        {
            if (ticks <= 0 && allocatedBytes <= 0)
                return;

            long measurementStartTicks = _recordMeasurementActive ? Stopwatch.GetTimestamp() : 0L;
            EnsureFrame();
            int index = (int)scope;
            if (index < 0 || index >= ScopeTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");

            ScopeTicks[index] += ticks;
            ScopeCalls[index]++;
            if (_logicTickActive)
            {
                if (_scopeRecordCaptureEnabled)
                {
                    if (_scopeRecordCapture.Count == ScopeRecordCaptureCapacity)
                        throw new InvalidOperationException("Scope record capture capacity exceeded; the capture is incomplete.");
                    _scopeRecordCapture.Add(new ScopeRecordSample(_currentLogicTickFrame, scope, ticks, Stopwatch.GetTimestamp()));
                }
                CurrentLogicTickScopeTicks[index] += ticks;
                if (CurrentLogicTickScopeRecordCounts[index] == 0)
                    CurrentLogicTickScopeRecordFirstTicks[index] = ticks;
                else
                    CurrentLogicTickScopeRecordSubsequentTicks[index] += ticks;
                CurrentLogicTickScopeRecordCounts[index]++;
                if (ScopeRecordCounts[index] == 0)
                {
                    ScopeRecordFirstTicks[index] = ticks;
                    ScopeRecordFirstLogicFrames[index] = _currentLogicTickFrame;
                }
                else
                    ScopeRecordSubsequentTicks[index] += ticks;
                ScopeRecordCounts[index]++;
            }
            if (allocatedBytes > 0)
                ScopeAllocatedBytes[index] += allocatedBytes;
            if (_recordMeasurementActive)
            {
                _recordMeasurementTicks += Stopwatch.GetTimestamp() - measurementStartTicks;
                _recordMeasurementCount++;
            }
        }

        // Diagnostic-only invocation timing; it does not change existing scope totals.
        public static void RecordLogicTickInvocation(MainThreadPerfScope scope, long ticks)
        {
            if (!LoggingEnabled || !_logicTickActive || ticks <= 0L)
                return;
            int index = (int)scope;
            if (index < 0 || index >= CurrentLogicTickInvocationFirstTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            if (CurrentLogicTickInvocationCounts[index] == 0)
                CurrentLogicTickInvocationFirstTicks[index] = ticks;
            else
                CurrentLogicTickInvocationSubsequentTicks[index] += ticks;
            CurrentLogicTickInvocationCounts[index]++;
            if (InvocationCounts[index] == 0)
            {
                InvocationFirstTicks[index] = ticks;
                InvocationFirstLogicFrames[index] = _currentLogicTickFrame;
            }
            else
                InvocationSubsequentTicks[index] += ticks;
            InvocationCounts[index]++;
        }

        public static void RecordLogicFrameListener(Type listenerType, long ticks)
        {
            if (listenerType == null)
                throw new ArgumentNullException(nameof(listenerType));
            if (ticks <= 0)
                return;

            EnsureFrame();
            if (!LogicListenerSamples.TryGetValue(listenerType, out LogicListenerSample sample))
            {
                sample = new LogicListenerSample(listenerType);
                LogicListenerSamples.Add(listenerType, sample);
            }

            sample.Ticks += ticks;
            sample.Calls++;
            if (_logicTickActive)
            {
                CurrentLogicTickListenerTicks.TryGetValue(listenerType, out long currentTicks);
                CurrentLogicTickListenerTicks[listenerType] = currentTicks + ticks;
                if (_scopeRecordCaptureEnabled)
                {
                    if (_scopeRecordCapture.Count == ScopeRecordCaptureCapacity)
                        throw new InvalidOperationException("Scope record capture capacity exceeded; the capture is incomplete.");
                    _scopeRecordCapture.Add(new ScopeRecordSample(_currentLogicTickFrame, MainThreadPerfScope.Count,
                        ticks, Stopwatch.GetTimestamp(), listenerType));
                }
            }
        }

        public static void RecordLogicTickDuration(long ticks)
        {
            if (ticks <= 0)
                return;

            EnsureFrame();
            for (int i = 0; i < CurrentLogicTickScopeTicks.Length; i++)
            {
                long scopeTicks = CurrentLogicTickScopeTicks[i];
                if (scopeTicks <= 0L)
                    continue;
                if (ScopeActiveTickCounts[i] == 0)
                {
                    ScopeActiveTickFirstTicks[i] = scopeTicks;
                    ScopeActiveTickFirstLogicFrames[i] = _currentLogicTickFrame;
                }
                else
                    ScopeActiveTickSubsequentTicks[i] += scopeTicks;
                ScopeActiveTickCounts[i]++;
            }
            int historyIndexToWrite = _logicTickHistoryNext;
            LogicTickHistoryFrames[historyIndexToWrite] = _currentLogicTickFrame;
            LogicTickHistoryDurations[historyIndexToWrite] = ticks;
            LogicTickHistoryWorkloadSignatures[historyIndexToWrite] = ComputeCurrentLogicTickWorkloadSignature();
            int historyScopeOffset = historyIndexToWrite * (int)MainThreadPerfScope.Count;
            Array.Copy(CurrentLogicTickScopeTicks, 0, LogicTickHistoryScopeTicks, historyScopeOffset, CurrentLogicTickScopeTicks.Length);
            Array.Copy(CurrentLogicTickScopeRecordCounts, 0, LogicTickHistoryScopeCalls, historyScopeOffset, CurrentLogicTickScopeRecordCounts.Length);
            Array.Copy(CurrentLogicTickInvocationCounts, 0, LogicTickHistoryInvocationCalls, historyScopeOffset, CurrentLogicTickInvocationCounts.Length);
            _logicTickHistoryNext = (_logicTickHistoryNext + 1) % LogicTickHistoryCapacity;
            if (_logicTickHistoryCount < LogicTickHistoryCapacity)
                _logicTickHistoryCount++;
            bool isNewMaximum = ticks > _currentMaxLogicTickTicks;
            if (isNewMaximum)
            {
                _currentMaxLogicTickTicks = ticks;
                _currentMaxLogicTickFrame = _currentLogicTickFrame;
                Array.Copy(CurrentLogicTickScopeTicks, CurrentMaxLogicTickScopeTicks, CurrentLogicTickScopeTicks.Length);
                Array.Copy(CurrentLogicTickScopeRecordFirstTicks, CurrentMaxLogicTickScopeRecordFirstTicks, CurrentLogicTickScopeRecordFirstTicks.Length);
                Array.Copy(CurrentLogicTickScopeRecordSubsequentTicks, CurrentMaxLogicTickScopeRecordSubsequentTicks, CurrentLogicTickScopeRecordSubsequentTicks.Length);
                Array.Copy(CurrentLogicTickScopeRecordCounts, CurrentMaxLogicTickScopeRecordCounts, CurrentLogicTickScopeRecordCounts.Length);
                Array.Copy(CurrentLogicTickInvocationFirstTicks, CurrentMaxLogicTickInvocationFirstTicks, CurrentLogicTickInvocationFirstTicks.Length);
                Array.Copy(CurrentLogicTickInvocationSubsequentTicks, CurrentMaxLogicTickInvocationSubsequentTicks, CurrentLogicTickInvocationSubsequentTicks.Length);
                Array.Copy(CurrentLogicTickInvocationCounts, CurrentMaxLogicTickInvocationCounts, CurrentLogicTickInvocationCounts.Length);
                LastCompletedMaxLogicTickListenerTicks.Clear();
                foreach (KeyValuePair<Type, long> entry in CurrentLogicTickListenerTicks)
                    LastCompletedMaxLogicTickListenerTicks[entry.Key] = entry.Value;
            }
            _logicTickActive = false;
        }

        private static ulong ComputeCurrentLogicTickWorkloadSignature()
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;
            for (int i = 0; i < CurrentLogicTickScopeRecordCounts.Length; i++)
            {
                hash ^= unchecked((uint)CurrentLogicTickScopeRecordCounts[i]);
                hash *= prime;
                hash ^= unchecked((uint)CurrentLogicTickInvocationCounts[i]);
                hash *= prime;
            }
            return hash;
        }

        private static int FindLogicTickHistoryIndex(ulong logicFrame)
        {
            for (int i = 0; i < _logicTickHistoryCount; i++)
            {
                int historyIndex = (_logicTickHistoryNext - _logicTickHistoryCount + i + LogicTickHistoryCapacity) % LogicTickHistoryCapacity;
                if (LogicTickHistoryFrames[historyIndex] == logicFrame)
                    return historyIndex;
            }
            throw new InvalidOperationException($"Logic Tick history snapshot is unavailable. logicFrame={logicFrame}.");
        }

        private static bool LogicTickTickCallsMatch(int candidateOffset, int targetOffset)
        {
            for (int i = 0; i < (int)MainThreadPerfScope.Count; i++)
            {
                if (LogicTickHistoryScopeCalls[candidateOffset + i] != LogicTickHistoryScopeCalls[targetOffset + i]
                    || LogicTickHistoryInvocationCalls[candidateOffset + i] != LogicTickHistoryInvocationCalls[targetOffset + i])
                    return false;
            }
            return true;
        }

        private static int ValidateScopeIndex(MainThreadPerfScope scope)
        {
            int index = (int)scope;
            if (index < 0 || index >= (int)MainThreadPerfScope.Count)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");
            return index;
        }

        public static void BeginRecordMeasurement()
        {
            if (!LoggingEnabled)
                return;
            _recordMeasurementActive = true;
            _recordMeasurementTicks = 0L;
            _recordMeasurementCount = 0;
        }

        public static void EndRecordMeasurement(out long ticks, out int count)
        {
            ticks = _recordMeasurementTicks;
            count = _recordMeasurementCount;
            _recordMeasurementActive = false;
            _recordMeasurementTicks = 0L;
            _recordMeasurementCount = 0;
        }

        private static void EnsureFrame()
        {
            int frame = Time.frameCount;
            long now = Stopwatch.GetTimestamp();
            // A logic tick may span multiple render frames in the editor. Keep
            // all tick scopes on its start frame and roll over only after the
            // tick has recorded its duration.
            if (_initialized && _logicTickActive && frame != _frame)
                return;
            if (!_initialized || frame < _frame)
            {
                bool frameRolledBack = _initialized;
                _initialized = true;
                Array.Clear(ScopeTicks, 0, ScopeTicks.Length);
                Array.Clear(ScopeCalls, 0, ScopeCalls.Length);
                Array.Clear(ScopeAllocatedBytes, 0, ScopeAllocatedBytes.Length);
                Array.Clear(IntervalScopeAllocatedBytes, 0, IntervalScopeAllocatedBytes.Length);
                ResetLogicListenerSamples();
                _intervalAllocatedBytes = 0L;
                _currentMaxLogicTickTicks = 0L;
                _currentMaxLogicTickFrame = 0;
                Array.Clear(CurrentMaxLogicTickScopeTicks, 0, CurrentMaxLogicTickScopeTicks.Length);
                Array.Clear(CurrentMaxLogicTickScopeRecordFirstTicks, 0, CurrentMaxLogicTickScopeRecordFirstTicks.Length);
                Array.Clear(CurrentMaxLogicTickScopeRecordSubsequentTicks, 0, CurrentMaxLogicTickScopeRecordSubsequentTicks.Length);
                Array.Clear(CurrentMaxLogicTickScopeRecordCounts, 0, CurrentMaxLogicTickScopeRecordCounts.Length);
                Array.Clear(CurrentMaxLogicTickInvocationFirstTicks, 0, CurrentMaxLogicTickInvocationFirstTicks.Length);
                Array.Clear(CurrentMaxLogicTickInvocationSubsequentTicks, 0, CurrentMaxLogicTickInvocationSubsequentTicks.Length);
                Array.Clear(CurrentMaxLogicTickInvocationCounts, 0, CurrentMaxLogicTickInvocationCounts.Length);
                Array.Clear(CurrentLogicTickScopeRecordFirstTicks, 0, CurrentLogicTickScopeRecordFirstTicks.Length);
                Array.Clear(CurrentLogicTickScopeRecordSubsequentTicks, 0, CurrentLogicTickScopeRecordSubsequentTicks.Length);
                Array.Clear(CurrentLogicTickScopeRecordCounts, 0, CurrentLogicTickScopeRecordCounts.Length);
                _logicTickActive = false;
                _frame = frame;
                _frameStartTicks = now;
                _frameStartAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
                _frameStartCollectionCount = GetCollectionCount();
                if (frameRolledBack)
                    _lastLogFrame = -100000;
                return;
            }

            if (_frame == frame)
                return;

            long frameTicks = now - _frameStartTicks;
            long allocatedBytes = Math.Max(0L, GC.GetAllocatedBytesForCurrentThread() - _frameStartAllocatedBytes);
            int collectionCount = GetCollectionCount();
            Flush(frameTicks, allocatedBytes, Math.Max(0, collectionCount - _frameStartCollectionCount));
            Array.Clear(ScopeTicks, 0, ScopeTicks.Length);
            Array.Clear(ScopeCalls, 0, ScopeCalls.Length);
            Array.Clear(ScopeAllocatedBytes, 0, ScopeAllocatedBytes.Length);
            ResetLogicListenerSamples();
            _currentMaxLogicTickTicks = 0L;
            _currentMaxLogicTickFrame = 0;
            Array.Clear(CurrentMaxLogicTickScopeTicks, 0, CurrentMaxLogicTickScopeTicks.Length);
            Array.Clear(CurrentMaxLogicTickScopeRecordFirstTicks, 0, CurrentMaxLogicTickScopeRecordFirstTicks.Length);
            Array.Clear(CurrentMaxLogicTickScopeRecordSubsequentTicks, 0, CurrentMaxLogicTickScopeRecordSubsequentTicks.Length);
            Array.Clear(CurrentMaxLogicTickScopeRecordCounts, 0, CurrentMaxLogicTickScopeRecordCounts.Length);
            Array.Clear(CurrentMaxLogicTickInvocationFirstTicks, 0, CurrentMaxLogicTickInvocationFirstTicks.Length);
            Array.Clear(CurrentMaxLogicTickInvocationSubsequentTicks, 0, CurrentMaxLogicTickInvocationSubsequentTicks.Length);
            Array.Clear(CurrentMaxLogicTickInvocationCounts, 0, CurrentMaxLogicTickInvocationCounts.Length);
            _logicTickActive = false;
            _frame = frame;
            _frameStartTicks = now;
            _frameStartAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
            _frameStartCollectionCount = collectionCount;
        }

        private static void Flush(long frameTicks, long allocatedBytes, int collectionCount)
        {
            if (!LoggingEnabled)
                return;

            _intervalAllocatedBytes += allocatedBytes;
            for (int i = 0; i < ScopeAllocatedBytes.Length; i++)
                IntervalScopeAllocatedBytes[i] += ScopeAllocatedBytes[i];

            long trackedTicks = ScopeTicks[(int)MainThreadPerfScope.GameFrameworkUpdate]
                                + ScopeTicks[(int)MainThreadPerfScope.Fog3Update]
                                + ScopeTicks[(int)MainThreadPerfScope.InteractionTrigger]
                                + ScopeTicks[(int)MainThreadPerfScope.InteractionCleanup]
                                + ScopeTicks[(int)MainThreadPerfScope.InteractionManagerUpdate]
                                + ScopeTicks[(int)MainThreadPerfScope.EntityUpdate];

            double frameMs = TicksToMs(frameTicks);
            double trackedMs = TicksToMs(trackedTicks);
            double untrackedMs = Math.Max(0.0, frameMs - trackedMs);
            LastCompletedFrame = _frame;
            LastCompletedFrameMilliseconds = frameMs;
            LastCompletedTrackedMilliseconds = trackedMs;
            LastCompletedUntrackedMilliseconds = untrackedMs;
            long logicFrameTicks = ScopeTicks[(int)MainThreadPerfScope.LogicFrameAdvance];
            LastCompletedLogicFrameMilliseconds = TicksToMs(logicFrameTicks);
            LastCompletedMaxLogicTickMilliseconds = TicksToMs(_currentMaxLogicTickTicks);
            LastCompletedMaxLogicTickFrame = _currentMaxLogicTickFrame;
            Array.Copy(ScopeTicks, LastCompletedScopeTicks, ScopeTicks.Length);
            Array.Copy(ScopeCalls, LastCompletedScopeCalls, ScopeCalls.Length);
            Array.Copy(CurrentMaxLogicTickScopeTicks, LastCompletedMaxLogicTickScopeTicks, CurrentMaxLogicTickScopeTicks.Length);
            Array.Copy(CurrentMaxLogicTickScopeRecordFirstTicks, LastCompletedMaxLogicTickScopeRecordFirstTicks, CurrentMaxLogicTickScopeRecordFirstTicks.Length);
            Array.Copy(CurrentMaxLogicTickScopeRecordSubsequentTicks, LastCompletedMaxLogicTickScopeRecordSubsequentTicks, CurrentMaxLogicTickScopeRecordSubsequentTicks.Length);
            Array.Copy(CurrentMaxLogicTickScopeRecordCounts, LastCompletedMaxLogicTickScopeRecordCounts, CurrentMaxLogicTickScopeRecordCounts.Length);
            Array.Copy(CurrentMaxLogicTickInvocationFirstTicks, LastCompletedMaxLogicTickInvocationFirstTicks, CurrentMaxLogicTickInvocationFirstTicks.Length);
            Array.Copy(CurrentMaxLogicTickInvocationSubsequentTicks, LastCompletedMaxLogicTickInvocationSubsequentTicks, CurrentMaxLogicTickInvocationSubsequentTicks.Length);
            Array.Copy(CurrentMaxLogicTickInvocationCounts, LastCompletedMaxLogicTickInvocationCounts, CurrentMaxLogicTickInvocationCounts.Length);

            EnsureRecorders();

            bool slowFrame = frameTicks >= SlowFrameTicks;
            bool highAllocation = allocatedBytes >= HighAllocationBytes;
            bool forceLog = frameTicks >= ForceLogFrameTicks
                            || trackedTicks >= ForceTrackedLogTicks
                            || logicFrameTicks >= SlowFrameTicks
                            || allocatedBytes >= ForceAllocationLogBytes
                            || collectionCount > 0
                            || ScopeCalls[(int)MainThreadPerfScope.ClusterSpawnUnits] > 0;
            if (!ConsoleLoggingEnabled)
                return;
            if (!slowFrame && !highAllocation && !forceLog)
                return;
            int minLogFrameInterval = highAllocation ? 10 : MinLogFrameInterval;
            if (!forceLog && _frame - _lastLogFrame < minLogFrameInterval)
                return;
            _lastLogFrame = _frame;

            UnityEngine.Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                null,
                "[MainPerf] frame={0} frameDt={1:F3}ms tracked={2:F3}ms untracked={3:F3}ms " +
                "gf={4:F3}ms/{5} flow={6:F3}ms/{7} flowConfig={8:F3}ms/{9} flowSource={10:F3}ms/{11} " +
                "fogUpdate={12:F3}ms/{13} fogVisibility={14:F3}ms/{15} fogOverlay={16:F3}ms/{17} fogEnemy={18:F3}ms/{19} " +
                "interactionTrigger={20:F3}ms/{21} interactionCleanup={22:F3}ms/{23} interactionDetail({24}) entity={25:F3}ms/{26} " +
                "entityDetail({27}) moveExecDetail({28}) characterMoveDetail({29}) entityShowDetail({30}) fogEnemyDetail({31}) spawnDetail({32}) " +
                "env(targetFps={33},vSync={34},screen={35}x{36},focused={37},gcIncremental={38}) markers({39}) " +
                "alloc(frame={40:F1}KB,sinceLastGC={41:F1}KB,gcCollections={42},scopes={43},sinceLastGCScopes={44}) " +
                "flowQueues({45}) logicDetail({46}) logicListeners({47})",
                _frame,
                frameMs,
                trackedMs,
                untrackedMs,
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.GameFrameworkUpdate]), ScopeCalls[(int)MainThreadPerfScope.GameFrameworkUpdate],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.FlowGroupMove]), ScopeCalls[(int)MainThreadPerfScope.FlowGroupMove],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.FlowConfig]), ScopeCalls[(int)MainThreadPerfScope.FlowConfig],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.FlowSourceGate]), ScopeCalls[(int)MainThreadPerfScope.FlowSourceGate],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.Fog3Update]), ScopeCalls[(int)MainThreadPerfScope.Fog3Update],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.Fog3Visibility]), ScopeCalls[(int)MainThreadPerfScope.Fog3Visibility],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.Fog3OverlayRender]), ScopeCalls[(int)MainThreadPerfScope.Fog3OverlayRender],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.Fog3EnemyVisibility]), ScopeCalls[(int)MainThreadPerfScope.Fog3EnemyVisibility],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.InteractionTrigger]), ScopeCalls[(int)MainThreadPerfScope.InteractionTrigger],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.InteractionCleanup]), ScopeCalls[(int)MainThreadPerfScope.InteractionCleanup],
                BuildScopeSummary(MainThreadPerfScope.InteractionManagerUpdate, MainThreadPerfScope.InteractionBuildTipsRequest),
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.EntityUpdate]), ScopeCalls[(int)MainThreadPerfScope.EntityUpdate],
                BuildScopeSummary(MainThreadPerfScope.EntityBase, MainThreadPerfScope.SoldierDebugDraw),
                BuildScopeSummary(MainThreadPerfScope.MoveExecutorConstraint, MainThreadPerfScope.MoveExecutorControllerMove),
                BuildScopeSummary(MainThreadPerfScope.CharacterMoveSteering, MainThreadPerfScope.CharacterMovePrepare),
                BuildScopeSummary(MainThreadPerfScope.EntityShowRequest, MainThreadPerfScope.EntitySuccessEvent),
                BuildScopeSummary(MainThreadPerfScope.Fog3EnemyBind, MainThreadPerfScope.Fog3EnemyHealth),
                BuildScopeSummary(MainThreadPerfScope.ClusterSpawnLog, MainThreadPerfScope.CardSetupBindRuntime),
                Application.targetFrameRate,
                QualitySettings.vSyncCount,
                Screen.width,
                Screen.height,
                Application.isFocused,
                UnityEngine.Scripting.GarbageCollector.isIncremental,
                BuildMarkerSummary(),
                allocatedBytes / 1024.0,
                _intervalAllocatedBytes / 1024.0,
                collectionCount,
                BuildAllocationScopeSummary(ScopeAllocatedBytes),
                BuildAllocationScopeSummary(IntervalScopeAllocatedBytes),
                BuildScopeSummary(MainThreadPerfScope.FlowWorldBuildQueue, MainThreadPerfScope.FlowTileBuildQueue),
                BuildScopeSummary(MainThreadPerfScope.LogicFrameAdvance, MainThreadPerfScope.FlowAttackAreaClearance),
                BuildLogicListenerSummary());

            if (collectionCount > 0)
            {
                _intervalAllocatedBytes = 0L;
                Array.Clear(IntervalScopeAllocatedBytes, 0, IntervalScopeAllocatedBytes.Length);
            }
        }

        private static int GetCollectionCount()
        {
            return GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
        }

        private static double TicksToMs(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static void EnsureRecorders()
        {
            if (_recordersInitialized)
                return;

            _recordersInitialized = true;
            for (int i = 0; i < MarkerSamplers.Length; i++)
                MarkerSamplers[i].Initialize();
        }

        private static string BuildMarkerSummary()
        {
            string result = string.Empty;
            for (int i = 0; i < MarkerSamplers.Length; i++)
            {
                if (!MarkerSamplers[i].TryReadMilliseconds(out double milliseconds))
                    continue;

                if (result.Length > 0)
                    result += ",";
                result += MarkerSamplers[i].Name + "=" + milliseconds.ToString("F3") + "ms";
            }

            return result.Length > 0 ? result : "none";
        }

        private static string BuildScopeSummary(MainThreadPerfScope first, MainThreadPerfScope last)
        {
            string result = string.Empty;
            int firstIndex = (int)first;
            int lastIndex = (int)last;
            for (int i = firstIndex; i <= lastIndex; i++)
            {
                if (ScopeCalls[i] <= 0)
                    continue;

                if (result.Length > 0)
                    result += ",";
                result += ((MainThreadPerfScope)i).ToString() + "=" + TicksToMs(ScopeTicks[i]).ToString("F3") + "ms/" + ScopeCalls[i];
            }

            return result.Length > 0 ? result : "none";
        }

        private static string BuildAllocationScopeSummary(long[] allocatedBytesByScope)
        {
            if (allocatedBytesByScope == null)
                throw new ArgumentNullException(nameof(allocatedBytesByScope));

            string result = string.Empty;
            for (int i = 0; i < allocatedBytesByScope.Length; i++)
            {
                if (allocatedBytesByScope[i] <= 0)
                    continue;

                if (result.Length > 0)
                    result += ",";
                result += ((MainThreadPerfScope)i).ToString() + "=" + (allocatedBytesByScope[i] / 1024.0).ToString("F1") + "KB";
            }

            return result.Length > 0 ? result : "none";
        }

        private static string BuildLogicListenerSummary()
        {
            LogicListenerSortBuffer.Clear();
            foreach (LogicListenerSample sample in LogicListenerSamples.Values)
            {
                if (sample.Calls > 0)
                    LogicListenerSortBuffer.Add(sample);
            }
            LogicListenerSortBuffer.Sort(CompareLogicListenerSamples);

            string result = string.Empty;
            int count = Math.Min(8, LogicListenerSortBuffer.Count);
            for (int i = 0; i < count; i++)
            {
                LogicListenerSample sample = LogicListenerSortBuffer[i];
                if (result.Length > 0)
                    result += ",";
                result += sample.ListenerType.FullName + "=" + TicksToMs(sample.Ticks).ToString("F3") + "ms/" + sample.Calls;
            }

            return result.Length > 0 ? result : "none";
        }

        private static int CompareLogicListenerSamples(LogicListenerSample left, LogicListenerSample right)
        {
            int ticksComparison = right.Ticks.CompareTo(left.Ticks);
            return ticksComparison != 0
                ? ticksComparison
                : string.CompareOrdinal(left.ListenerType.FullName, right.ListenerType.FullName);
        }

        private static int CompareLogicListenerTypeSamples(LogicListenerTypeSample left, LogicListenerTypeSample right)
        {
            int ticksComparison = right.Ticks.CompareTo(left.Ticks);
            return ticksComparison != 0
                ? ticksComparison
                : string.CompareOrdinal(left.ListenerType.FullName, right.ListenerType.FullName);
        }

        private static void ResetLogicListenerSamples()
        {
            foreach (LogicListenerSample sample in LogicListenerSamples.Values)
            {
                sample.Ticks = 0L;
                sample.Calls = 0;
            }
        }

        private sealed class LogicListenerSample
        {
            public LogicListenerSample(Type listenerType)
            {
                ListenerType = listenerType;
            }

            public Type ListenerType { get; }
            public long Ticks;
            public int Calls;
        }

        private sealed class LogicListenerTypeSample
        {
            public readonly Type ListenerType;
            public readonly long Ticks;

            public LogicListenerTypeSample(Type listenerType, long ticks)
            {
                ListenerType = listenerType ?? throw new ArgumentNullException(nameof(listenerType));
                Ticks = ticks;
            }
        }

        private struct ProfilerMarkerSampler
        {
            public readonly ProfilerCategory Category;
            public readonly string Name;
            private ProfilerRecorder _recorder;
            private bool _initialized;

            public ProfilerMarkerSampler(ProfilerCategory category, string name)
            {
                Category = category;
                Name = name;
                _recorder = default;
                _initialized = false;
            }

            public void Initialize()
            {
                if (_initialized)
                    return;

                _initialized = true;
                try
                {
                    _recorder = ProfilerRecorder.StartNew(Category, Name, 1);
                }
                catch (Exception)
                {
                    _recorder = default;
                }
            }

            public bool TryReadMilliseconds(out double milliseconds)
            {
                milliseconds = 0.0;
                if (!_recorder.Valid)
                    return false;

                milliseconds = _recorder.LastValue / 1000000.0;
                return true;
            }
        }
    }
}
