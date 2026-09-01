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
        Count = 202
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
        private static readonly long[] ScopeAllocatedBytes = new long[(int)MainThreadPerfScope.Count];
        private static readonly long[] IntervalScopeAllocatedBytes = new long[(int)MainThreadPerfScope.Count];
        private static readonly Dictionary<Type, LogicListenerSample> LogicListenerSamples = new Dictionary<Type, LogicListenerSample>();
        private static readonly List<LogicListenerSample> LogicListenerSortBuffer = new List<LogicListenerSample>();
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
            LoggingEnabled = false;
            ConsoleLoggingEnabled = true;
            LastCompletedFrame = -1;
            LastCompletedFrameMilliseconds = 0.0;
            LastCompletedTrackedMilliseconds = 0.0;
            LastCompletedUntrackedMilliseconds = 0.0;
            LastCompletedLogicFrameMilliseconds = 0.0;
            LastCompletedMaxLogicTickMilliseconds = 0.0;
            LastCompletedMaxLogicTickFrame = 0;
            _currentMaxLogicTickTicks = 0L;
            _currentLogicTickFrame = 0;
            _currentMaxLogicTickFrame = 0;
            Array.Clear(LastCompletedScopeTicks, 0, LastCompletedScopeTicks.Length);
            Array.Clear(LastCompletedScopeCalls, 0, LastCompletedScopeCalls.Length);
            Array.Clear(CurrentLogicTickScopeTicks, 0, CurrentLogicTickScopeTicks.Length);
            Array.Clear(CurrentMaxLogicTickScopeTicks, 0, CurrentMaxLogicTickScopeTicks.Length);
            Array.Clear(LastCompletedMaxLogicTickScopeTicks, 0, LastCompletedMaxLogicTickScopeTicks.Length);
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

        public static void BeginLogicTick(ulong logicFrame)
        {
            if (!LoggingEnabled)
                return;
            EnsureFrame();
            Array.Clear(CurrentLogicTickScopeTicks, 0, CurrentLogicTickScopeTicks.Length);
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

            EnsureFrame();
            int index = (int)scope;
            if (index < 0 || index >= ScopeTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");

            ScopeTicks[index] += ticks;
            ScopeCalls[index]++;
            if (_logicTickActive)
                CurrentLogicTickScopeTicks[index] += ticks;
            if (allocatedBytes > 0)
                ScopeAllocatedBytes[index] += allocatedBytes;
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
        }

        public static void RecordLogicTickDuration(long ticks)
        {
            if (ticks <= 0)
                return;

            EnsureFrame();
            bool isNewMaximum = ticks > _currentMaxLogicTickTicks;
            if (isNewMaximum)
            {
                _currentMaxLogicTickTicks = ticks;
                _currentMaxLogicTickFrame = _currentLogicTickFrame;
                Array.Copy(CurrentLogicTickScopeTicks, CurrentMaxLogicTickScopeTicks, CurrentLogicTickScopeTicks.Length);
            }
            _logicTickActive = false;
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
