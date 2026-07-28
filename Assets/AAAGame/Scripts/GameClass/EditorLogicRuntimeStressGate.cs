#if UNITY_EDITOR
using System;
using AAAGame.MiniMap.FOG3;
using DG.Tweening;
using GameFramework.ObjectPool;
using UnityEngine;
using UnityEngine.Profiling;
using UnityGameFramework.Runtime;

public enum EditorLogicRuntimeStressGateStatus
{
    Idle = 0,
    Armed = 1,
    Running = 2,
    AwaitingViewSettlement = 3,
    Completed = 4,
    Failed = 5,
}

public static class EditorLogicRuntimeStressGate
{
    private static readonly LogicStateHasher s_GameplayTraceHasher = new LogicStateHasher();
    private static readonly LogicStateHasher s_DamageTraceHasher = new LogicStateHasher();

    private readonly struct RuntimeRetentionCensus
    {
        public RuntimeRetentionCensus(
            int objectPoolCount,
            int pooledObjectCount,
            int releasableObjectCount,
            string largestObjectPool,
            int largestObjectPoolCount,
            int gameObjectCount,
            int componentCount,
            int monoBehaviourCount,
            int transformCount,
            int rendererCount,
            int animatorCount,
            int particleSystemCount,
            int audioSourceCount,
            int activeTweenCount,
            int playingTweenCount,
            int commandHistoryCount)
        {
            ObjectPoolCount = objectPoolCount;
            PooledObjectCount = pooledObjectCount;
            ReleasableObjectCount = releasableObjectCount;
            LargestObjectPool = largestObjectPool;
            LargestObjectPoolCount = largestObjectPoolCount;
            GameObjectCount = gameObjectCount;
            ComponentCount = componentCount;
            MonoBehaviourCount = monoBehaviourCount;
            TransformCount = transformCount;
            RendererCount = rendererCount;
            AnimatorCount = animatorCount;
            ParticleSystemCount = particleSystemCount;
            AudioSourceCount = audioSourceCount;
            ActiveTweenCount = activeTweenCount;
            PlayingTweenCount = playingTweenCount;
            CommandHistoryCount = commandHistoryCount;
        }

        public int ObjectPoolCount { get; }
        public int PooledObjectCount { get; }
        public int ReleasableObjectCount { get; }
        public string LargestObjectPool { get; }
        public int LargestObjectPoolCount { get; }
        public int GameObjectCount { get; }
        public int ComponentCount { get; }
        public int MonoBehaviourCount { get; }
        public int TransformCount { get; }
        public int RendererCount { get; }
        public int AnimatorCount { get; }
        public int ParticleSystemCount { get; }
        public int AudioSourceCount { get; }
        public int ActiveTweenCount { get; }
        public int PlayingTweenCount { get; }
        public int CommandHistoryCount { get; }

        public override string ToString()
        {
            return
                $"objectPools={ObjectPoolCount}/{PooledObjectCount}/{ReleasableObjectCount}," +
                $"largestPool={LargestObjectPool}:{LargestObjectPoolCount}," +
                $"unityObjects={GameObjectCount}/{ComponentCount}/{MonoBehaviourCount}/{TransformCount}," +
                $"render={RendererCount}/{AnimatorCount}/{ParticleSystemCount}/{AudioSourceCount}," +
                $"tweens={ActiveTweenCount}/{PlayingTweenCount}," +
                $"commandHistory={CommandHistoryCount}";
        }
    }

    private readonly struct ReferencePoolCensus
    {
        public ReferencePoolCensus(
            int poolCount,
            long unusedCount,
            long usingCount,
            long acquireCount,
            long releaseCount,
            string largestUnusedType,
            int largestUnusedCount,
            string largestUsingType,
            int largestUsingCount)
        {
            PoolCount = poolCount;
            UnusedCount = unusedCount;
            UsingCount = usingCount;
            AcquireCount = acquireCount;
            ReleaseCount = releaseCount;
            LargestUnusedType = largestUnusedType;
            LargestUnusedCount = largestUnusedCount;
            LargestUsingType = largestUsingType;
            LargestUsingCount = largestUsingCount;
        }

        public int PoolCount { get; }
        public long UnusedCount { get; }
        public long UsingCount { get; }
        public long AcquireCount { get; }
        public long ReleaseCount { get; }
        public string LargestUnusedType { get; }
        public int LargestUnusedCount { get; }
        public string LargestUsingType { get; }
        public int LargestUsingCount { get; }

        public override string ToString()
        {
            return
                $"pools={PoolCount},unused={UnusedCount},using={UsingCount},acquire={AcquireCount},release={ReleaseCount}," +
                $"maxUnused={LargestUnusedType}:{LargestUnusedCount},maxUsing={LargestUsingType}:{LargestUsingCount}";
        }
    }

    private const int MovementChangeIntervalTicks = 300;
    private const int CombatObservationWindowTicks = 3000;
    private const int ProjectileRetainedStateLimit = 4096;

    private static int s_TotalTicks;
    private static int s_WarmupTicks;
    private static int s_BatchTicks;
    private static int s_ProcessedTicks;
    private static int s_CombatBaselineAuthorityCount;
    private static int s_InitialObstacleCount;
    private static int s_FlowTileCacheLimit;
    private static int s_InjectedInputCount;
    private static bool s_InjectedThisFrame;
    private static FixVector2 s_InjectedDirection;
    private static ulong s_ActivationFrame;
    private static ulong s_StartFrame;
    private static ulong s_RequestedMeasurementStartFrame;
    private static bool s_MeasurementInitialized;
    private static ulong s_InitialLateInputCount;
    private static ulong s_InitialStaticProjectionFailureCount;
    private static ulong s_FirstNavigationAgentsHash;
    private static bool s_NavigationHashChanged;
    private static bool s_CombatAuthorityObserved;
    private static bool s_DamageObserved;
    private static long s_DamageSubmittedCount;
    private static long s_DamageAppliedCount;
    private static long s_DamageSkippedDeadTargetCount;
    private static bool s_CombatWindowEnemyObserved;
    private static bool s_CombatWindowDamageObserved;
    private static int s_CombatWindowsValidated;
    private static int s_ProtectedPlayerBuildingCount;
    private static int s_CurrentEnemyUnitCount;
    private static int s_MaxEnemyUnitCount;
    private static int s_EnemyUnitBoundViewCount;
    private static int s_EnemyUnitFogTrackedViewCount;
    private static int s_MaxAuthorityCount;
    private static int s_MaxBoundViewCount;
    private static int s_MaxProjectileCount;
    private static int s_MaxProjectileRetainedStateCount;
    private static int s_MaxFlowTileCacheCount;
    private static int s_MaxSharedGoalCacheCount;
    private static int s_MaxPendingFlowTileCount;
    private static int s_MaxPendingSharedGoalCount;
    private static long s_ManagedBaselineBytes;
    private static long s_ReservedBaselineBytes;
    private static long s_MonoUsedBaselineBytes;
    private static long s_MonoHeapBaselineBytes;
    private static long s_AllocatedBaselineBytes;
    private static int s_GcCollectionBaselineCount;
    private static FlowFieldCrowdMovementSystem.RuntimeMemoryCensus s_FlowMemoryBaseline;
    private static ReferencePoolCensus s_ReferencePoolBaseline;
    private static RuntimeRetentionCensus s_RuntimeRetentionBaseline;
    private static int s_CheckpointBaselineCount;
    private static int s_RetainedFogSnapshotBaselineCount;
    private static long s_RetainedFogPayloadBaselineBytes;
    private static int s_FlowTileBaselineCount;
    private static int s_SharedGoalBaselineCount;
    private static int s_PendingFlowBaselineCount;
    private static int s_PendingSharedGoalBaselineCount;
    private static long s_ManagedFinalBytes;
    private static long s_ReservedFinalBytes;
    private static long s_MonoUsedFinalBytes;
    private static long s_MonoHeapFinalBytes;
    private static long s_AllocatedFinalBytes;
    private static int s_GcCollectionFinalCount;
    private static FlowFieldCrowdMovementSystem.RuntimeMemoryCensus s_FlowMemoryFinal;
    private static ReferencePoolCensus s_ReferencePoolFinal;
    private static RuntimeRetentionCensus s_RuntimeRetentionFinal;
    private static LogicGameplayStateDigest s_FinalDigest;
    private static ulong s_FinalFrame;
    private static bool s_ComputeFullHash;
    private static bool s_SuppressPhysicsSimulation;
    private static bool s_AllowProfiler;
    private static string s_Failure = string.Empty;
    private static GameEndManager s_GameEndManager;

    public static EditorLogicRuntimeStressGateStatus Status { get; private set; }
    public static bool OwnsLogicClock =>
        Status == EditorLogicRuntimeStressGateStatus.Armed
        || Status == EditorLogicRuntimeStressGateStatus.Running
        || Status == EditorLogicRuntimeStressGateStatus.AwaitingViewSettlement
        || Status == EditorLogicRuntimeStressGateStatus.Completed
        || Status == EditorLogicRuntimeStressGateStatus.Failed;
    public static int ProcessedTicks => s_ProcessedTicks;
    public static int TotalTicks => s_TotalTicks;
    public static bool ComputeFullHash => s_ComputeFullHash;
    public static bool SuppressPhysicsSimulation => s_SuppressPhysicsSimulation && s_MeasurementInitialized;
    public static bool AllowProfiler => s_AllowProfiler;
    public static event Action<LogicGameplayStateDigest> AuthorityFrameRecorded;
    public static int NextBatchTickCount
    {
        get
        {
            EnsureStatus(EditorLogicRuntimeStressGateStatus.Running);
            if (!s_MeasurementInitialized)
            {
                ulong preludeTicks = s_StartFrame - LogicFrameRuntime.CurrentFrame;
                return (int)Math.Min((ulong)s_BatchTicks, preludeTicks);
            }
            int remaining = s_TotalTicks - s_ProcessedTicks;
            int nextMilestone = s_ProcessedTicks < s_WarmupTicks
                ? s_WarmupTicks - s_ProcessedTicks
                : remaining;
            return Math.Min(s_BatchTicks, Math.Min(remaining, nextMilestone));
        }
    }

    public static void Arm(
        int totalTicks,
        int warmupTicks,
        int batchTicks,
        int combatBaselineAuthorityCount,
        bool computeFullHash,
        bool suppressPhysicsSimulation,
        bool allowProfiler,
        ulong measurementStartFrame = 0)
    {
        if (Status == EditorLogicRuntimeStressGateStatus.Armed
            || Status == EditorLogicRuntimeStressGateStatus.Running
            || Status == EditorLogicRuntimeStressGateStatus.AwaitingViewSettlement)
        {
            throw new InvalidOperationException($"Editor logic stress gate is already active. status={Status}.");
        }
        if (totalTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalTicks));
        if (warmupTicks <= 0 || warmupTicks >= totalTicks)
            throw new ArgumentOutOfRangeException(nameof(warmupTicks));
        if (batchTicks <= 0 || batchTicks > warmupTicks)
            throw new ArgumentOutOfRangeException(nameof(batchTicks));
        if (combatBaselineAuthorityCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(combatBaselineAuthorityCount));
        if (!LogicFrameRuntime.IsActive || !LogicFrameRuntime.IsTimelineRunning)
            throw new InvalidOperationException("Editor logic stress gate requires an active logic timeline.");
        if (LogicReplayRuntime.IsRecording || LogicReplayRuntime.ShouldRecordRuntimeSession)
            throw new InvalidOperationException("Editor logic stress gate requires replay recording to be disabled.");
        if (LogicTimeControlService.IsPaused || LogicTimeControlService.SchedulerScale != 1d)
            throw new InvalidOperationException("Editor logic stress gate requires unpaused scheduler scale 1.");

        ResetMetrics();
        s_TotalTicks = totalTicks;
        s_WarmupTicks = warmupTicks;
        s_BatchTicks = batchTicks;
        s_CombatBaselineAuthorityCount = combatBaselineAuthorityCount;
        s_ComputeFullHash = computeFullHash;
        s_SuppressPhysicsSimulation = suppressPhysicsSimulation;
        s_AllowProfiler = allowProfiler;
        s_RequestedMeasurementStartFrame = measurementStartFrame;
        Status = EditorLogicRuntimeStressGateStatus.Armed;
        Log.Info(
            "[LogicLongSessionGate] Armed. totalTicks={0}, warmupTicks={1}, batchTicks={2}, combatBaselineAuthority={3}, computeFullHash={4}, suppressPhysicsSimulation={5}, allowProfiler={6}.",
            totalTicks,
            warmupTicks,
            batchTicks,
            combatBaselineAuthorityCount,
            computeFullHash,
            suppressPhysicsSimulation,
            allowProfiler);
    }

    public static void Activate(ulong startFrame)
    {
        EnsureStatus(EditorLogicRuntimeStressGateStatus.Armed);
        if (s_RequestedMeasurementStartFrame != 0 && s_RequestedMeasurementStartFrame <= startFrame)
        {
            throw new InvalidOperationException(
                $"Editor logic stress gate fixed measurement frame must be after activation. activation={startFrame}, measurement={s_RequestedMeasurementStartFrame}.");
        }

        s_ActivationFrame = startFrame;
        s_StartFrame = s_RequestedMeasurementStartFrame == 0
            ? startFrame
            : s_RequestedMeasurementStartFrame;
        Status = EditorLogicRuntimeStressGateStatus.Running;
        if (s_RequestedMeasurementStartFrame == 0)
            InitializeMeasurement(startFrame);

        Log.Info(
            "[LogicLongSessionGate] Activated. activationFrame={0}, measurementStartFrame={1}, authority={2}.",
            s_ActivationFrame,
            s_StartFrame,
            LogicEntityLifecycleService.AuthorityEntityCount);
    }

    private static void InitializeMeasurement(ulong startFrame)
    {
        InputModel inputModel = GetRequiredInputModel();
        if (!inputModel.LogicTimeline.IsStarted)
            throw new InvalidOperationException("Editor logic stress gate requires a started input timeline.");
        if (inputModel.LogicTimeline.CurrentFrame.FrameId != startFrame)
            throw new InvalidOperationException(
                $"Editor logic stress gate input frame mismatch at activation. input={inputModel.LogicTimeline.CurrentFrame.FrameId}, clock={startFrame}.");

        s_InitialLateInputCount = inputModel.LogicTimeline.LateEventCount;
        s_GameplayTraceHasher.Reset();
        s_GameplayTraceHasher.Add(0x4741544554524143UL);
        s_DamageTraceHasher.Reset();
        s_DamageTraceHasher.Add(0x44414D4147455452UL);
        s_InitialStaticProjectionFailureCount = LogicAgentCollisionShadowService.TotalStaticProjectionFailureCount;
        s_InitialObstacleCount = LogicObstacleCommandService.ActiveObstacleCount;
        s_FlowTileCacheLimit = FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheLimit();
        s_GameEndManager = GameEntry.GetComponent<GameEndManager>()
                           ?? throw new InvalidOperationException("Editor logic stress gate requires GameEndManager.");
        if (s_GameEndManager.IsGameEnded)
            throw new InvalidOperationException("Editor logic stress gate cannot activate after the level has ended.");
        s_ProtectedPlayerBuildingCount = RestorePlayerBuildingsToFullHealth();
        if (s_ProtectedPlayerBuildingCount <= 0)
            throw new InvalidOperationException("Editor logic stress gate requires at least one active player building.");
        if (s_InitialObstacleCount <= 0)
            throw new InvalidOperationException("Editor logic stress gate requires authored runtime obstacles.");
        if (s_FlowTileCacheLimit < 16)
            throw new InvalidOperationException($"Editor logic stress gate received invalid flow cache limit {s_FlowTileCacheLimit}.");

        s_MeasurementInitialized = true;
        Log.Info(
            "[LogicLongSessionGate] Measurement initialized. startFrame={0}, authority={1}, obstacles={2}, flowLimit={3}.",
            startFrame,
            LogicEntityLifecycleService.AuthorityEntityCount,
            s_InitialObstacleCount,
            s_FlowTileCacheLimit);
    }

    public static void PrepareInputFrame(ulong frame, double cutoffRealtime)
    {
        EnsureStatus(EditorLogicRuntimeStressGateStatus.Running);
        if (!s_MeasurementInitialized)
        {
            if (frame > s_StartFrame)
            {
                throw new InvalidOperationException(
                    $"Editor logic stress gate prelude exceeded measurement start. frame={frame}, start={s_StartFrame}.");
            }
            return;
        }
        ulong expectedFrame = checked(s_StartFrame + (ulong)s_ProcessedTicks + 1UL);
        if (frame != expectedFrame)
            throw new InvalidOperationException($"Editor logic stress gate prepare frame mismatch. expected={expectedFrame}, actual={frame}.");

        int restoredBuildingCount = RestorePlayerBuildingsToFullHealth();
        if (restoredBuildingCount != s_ProtectedPlayerBuildingCount)
        {
            throw new InvalidOperationException(
                $"Editor logic stress gate player building set changed. expected={s_ProtectedPlayerBuildingCount}, actual={restoredBuildingCount}.");
        }

        s_InjectedThisFrame = s_ProcessedTicks % MovementChangeIntervalTicks == 0;
        if (!s_InjectedThisFrame)
            return;

        int directionIndex = (s_ProcessedTicks / MovementChangeIntervalTicks) & 3;
        s_InjectedDirection = directionIndex switch
        {
            0 => new FixVector2(Fix64.One, Fix64.Zero),
            1 => new FixVector2(Fix64.Zero, Fix64.One),
            2 => new FixVector2(-Fix64.One, Fix64.Zero),
            _ => new FixVector2(Fix64.Zero, -Fix64.One),
        };
        GetRequiredInputModel().LogicTimeline.EnqueueWorldMove(cutoffRealtime, s_InjectedDirection);
        s_InjectedInputCount = checked(s_InjectedInputCount + 1);
    }

    public static void RecordFrame(LogicInputFrame inputFrame, LogicGameplayStateDigest gameplayDigest)
    {
        EnsureStatus(EditorLogicRuntimeStressGateStatus.Running);
        if (inputFrame == null)
            throw new ArgumentNullException(nameof(inputFrame));
        if (!s_MeasurementInitialized)
        {
            if (inputFrame.FrameId > s_StartFrame)
            {
                throw new InvalidOperationException(
                    $"Editor logic stress gate prelude completed past measurement start. frame={inputFrame.FrameId}, start={s_StartFrame}.");
            }
            if (inputFrame.FrameId == s_StartFrame)
                InitializeMeasurement(s_StartFrame);
            return;
        }
        if (s_ComputeFullHash && !gameplayDigest.HasDetails)
            throw new InvalidOperationException("Editor logic stress gate requires a detailed gameplay digest.");

        ulong expectedFrame = checked(s_StartFrame + (ulong)s_ProcessedTicks + 1UL);
        if (inputFrame.FrameId != expectedFrame
            || (s_ComputeFullHash && gameplayDigest.FrameId != expectedFrame))
        {
            throw new InvalidOperationException(
                $"Editor logic stress gate completed frame mismatch. expected={expectedFrame}, input={inputFrame.FrameId}, digest={gameplayDigest.FrameId}.");
        }

        ValidateCompletedFrame(inputFrame, gameplayDigest);
        RecordAuthorityTraces(inputFrame.FrameId, gameplayDigest);
        AuthorityFrameRecorded?.Invoke(gameplayDigest);
        s_ProcessedTicks = checked(s_ProcessedTicks + 1);
        s_FinalDigest = gameplayDigest;
        s_FinalFrame = inputFrame.FrameId;

        if (s_ProcessedTicks == s_WarmupTicks)
            CaptureMemoryBaseline();

        if (s_ProcessedTicks % CombatObservationWindowTicks == 0)
        {
            ValidateCombatObservationWindow();
            Log.Info("[LogicLongSessionGate] Progress. {0}", BuildReport());
        }

        if (s_ProcessedTicks < s_TotalTicks)
            EnsureDefendCombatScheduled();

        if (s_ProcessedTicks == s_TotalTicks)
        {
            Status = EditorLogicRuntimeStressGateStatus.AwaitingViewSettlement;
            Log.Info("[LogicLongSessionGate] Logic ticks complete; waiting for view settlement. {0}", BuildReport());
        }
    }

    public static void CompleteAfterViewSettlement()
    {
        EnsureStatus(EditorLogicRuntimeStressGateStatus.AwaitingViewSettlement);
        ValidateWorldClosure(requireBoundViews: true);
        ValidateEnemyUnitPresentationClosure();
        if (FlowFieldCrowdMovementSystem.GetEditorTestDuplicatePendingFlowTileBuildKeyCount() != 0)
            throw new InvalidOperationException("Editor logic stress gate found duplicate pending flow-tile keys.");
        if (s_ProcessedTicks != s_TotalTicks)
            throw new InvalidOperationException($"Editor logic stress gate ended early. processed={s_ProcessedTicks}, total={s_TotalTicks}.");
        if (s_ProcessedTicks % CombatObservationWindowTicks != 0)
            ValidateCombatObservationWindow();
        int expectedCombatWindows = (s_TotalTicks + CombatObservationWindowTicks - 1) / CombatObservationWindowTicks;
        if (s_CombatWindowsValidated != expectedCombatWindows)
        {
            throw new InvalidOperationException(
                $"Editor logic stress gate combat window count mismatch. expected={expectedCombatWindows}, actual={s_CombatWindowsValidated}.");
        }
        if (!s_CombatAuthorityObserved)
            throw new InvalidOperationException("Editor logic stress gate did not observe authored combat entities above the pre-combat baseline.");
        if (!s_DamageObserved)
            throw new InvalidOperationException("Editor logic stress gate did not observe any submitted damage event.");
        if (s_ComputeFullHash && !s_NavigationHashChanged)
            throw new InvalidOperationException("Editor logic stress gate did not observe changing navigation authority state.");
        if (s_InjectedInputCount <= 0)
            throw new InvalidOperationException("Editor logic stress gate did not inject any deterministic movement input.");

        ForceFullCollection();
        s_ManagedFinalBytes = GC.GetTotalMemory(true);
        s_ReservedFinalBytes = Profiler.GetTotalReservedMemoryLong();
        s_MonoUsedFinalBytes = Profiler.GetMonoUsedSizeLong();
        s_MonoHeapFinalBytes = Profiler.GetMonoHeapSizeLong();
        s_AllocatedFinalBytes = GC.GetAllocatedBytesForCurrentThread();
        s_GcCollectionFinalCount = GetGcCollectionCount();
        s_FlowMemoryFinal = FlowFieldCrowdMovementSystem.CaptureRuntimeMemoryCensus();
        s_ReferencePoolFinal = CaptureReferencePoolCensus();
        s_RuntimeRetentionFinal = CaptureRuntimeRetentionCensus();
        ValidateRetainedRuntimeState();

        Status = EditorLogicRuntimeStressGateStatus.Completed;
        Log.Info("[LogicLongSessionGate] PASS. {0}", BuildReport());
    }

    private static void ValidateRetainedRuntimeState()
    {
        int checkpointCount = StageCheckpointService.History.Count;
        if (checkpointCount != s_CheckpointBaselineCount)
        {
            throw new InvalidOperationException(
                $"Editor logic stress gate retained stage checkpoints. baseline={s_CheckpointBaselineCount}, final={checkpointCount}.");
        }

        int retainedFogSnapshotCount = StageCheckpointService.RetainedFogSnapshotCount;
        long retainedFogPayloadBytes = StageCheckpointService.RetainedFogPayloadBytes;
        if (retainedFogSnapshotCount != s_RetainedFogSnapshotBaselineCount
            || retainedFogPayloadBytes != s_RetainedFogPayloadBaselineBytes)
        {
            throw new InvalidOperationException(
                $"Editor logic stress gate retained fog data grew. snapshots={s_RetainedFogSnapshotBaselineCount}->{retainedFogSnapshotCount}, " +
                $"bytes={s_RetainedFogPayloadBaselineBytes}->{retainedFogPayloadBytes}.");
        }

        if (s_RuntimeRetentionFinal.CommandHistoryCount != s_RuntimeRetentionBaseline.CommandHistoryCount)
        {
            throw new InvalidOperationException(
                $"Editor logic stress gate command history grew. baseline={s_RuntimeRetentionBaseline.CommandHistoryCount}, " +
                $"final={s_RuntimeRetentionFinal.CommandHistoryCount}.");
        }
        if (s_RuntimeRetentionFinal.ObjectPoolCount != s_RuntimeRetentionBaseline.ObjectPoolCount)
        {
            throw new InvalidOperationException(
                $"Editor logic stress gate object-pool registry changed. baseline={s_RuntimeRetentionBaseline.ObjectPoolCount}, " +
                $"final={s_RuntimeRetentionFinal.ObjectPoolCount}.");
        }
        if (s_RuntimeRetentionFinal.GameObjectCount > s_RuntimeRetentionBaseline.GameObjectCount
            || s_RuntimeRetentionFinal.ComponentCount > s_RuntimeRetentionBaseline.ComponentCount
            || s_RuntimeRetentionFinal.MonoBehaviourCount > s_RuntimeRetentionBaseline.MonoBehaviourCount
            || s_RuntimeRetentionFinal.TransformCount > s_RuntimeRetentionBaseline.TransformCount
            || s_RuntimeRetentionFinal.RendererCount > s_RuntimeRetentionBaseline.RendererCount
            || s_RuntimeRetentionFinal.AnimatorCount > s_RuntimeRetentionBaseline.AnimatorCount
            || s_RuntimeRetentionFinal.ParticleSystemCount > s_RuntimeRetentionBaseline.ParticleSystemCount
            || s_RuntimeRetentionFinal.AudioSourceCount > s_RuntimeRetentionBaseline.AudioSourceCount)
        {
            throw new InvalidOperationException(
                $"Editor logic stress gate retained Unity objects. baseline=[{s_RuntimeRetentionBaseline}], final=[{s_RuntimeRetentionFinal}].");
        }
        if (s_RuntimeRetentionFinal.ActiveTweenCount > s_RuntimeRetentionBaseline.ActiveTweenCount
            || s_RuntimeRetentionFinal.PlayingTweenCount > s_RuntimeRetentionBaseline.PlayingTweenCount)
        {
            throw new InvalidOperationException(
                $"Editor logic stress gate retained tweens. baseline=[{s_RuntimeRetentionBaseline}], final=[{s_RuntimeRetentionFinal}].");
        }

        if (FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount() != 0
            || FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount() != 0)
        {
            throw new InvalidOperationException(
                "Editor logic stress gate completed with pending navigation work.");
        }
    }

    public static void Fail(Exception exception)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));
        s_Failure = exception.ToString();
        Status = EditorLogicRuntimeStressGateStatus.Failed;
        Log.Error("[LogicLongSessionGate] FAIL. {0}", s_Failure);
    }

    public static string BuildReport()
    {
        long managedGrowth = Math.Max(0L, s_ManagedFinalBytes - s_ManagedBaselineBytes);
        long reservedGrowth = Math.Max(0L, s_ReservedFinalBytes - s_ReservedBaselineBytes);
        bool managedEqualsMonoUsed =
            s_ManagedBaselineBytes == s_MonoUsedBaselineBytes
            && s_ManagedFinalBytes == s_MonoUsedFinalBytes;
        return
            $"status={Status}, ticks={s_ProcessedTicks}/{s_TotalTicks}, activationFrame={s_ActivationFrame}, startFrame={s_StartFrame}, finalFrame={s_FinalFrame}, computeFullHash={s_ComputeFullHash}, suppressPhysicsSimulation={s_SuppressPhysicsSimulation}, allowProfiler={s_AllowProfiler}, " +
            $"gameplayHash={s_FinalDigest.GameplayStateHash}, navigationAgentsHash={s_FinalDigest.Navigation.AgentsHash}, " +
            $"gameplayTraceHash={s_GameplayTraceHasher.Hash}, damageTraceHash={s_DamageTraceHasher.Hash}, " +
            $"damageSubmitted={s_DamageSubmittedCount}, damageApplied={s_DamageAppliedCount}, damageSkippedDead={s_DamageSkippedDeadTargetCount}, " +
            $"combatObserved={s_CombatAuthorityObserved}, damageObserved={s_DamageObserved}, navigationChanged={s_NavigationHashChanged}, " +
            $"combatWindows={s_CombatWindowsValidated}, protectedBuildings={s_ProtectedPlayerBuildingCount}, " +
            $"enemyUnits={s_CurrentEnemyUnitCount}, enemyUnitPeak={s_MaxEnemyUnitCount}, enemyViewsBound={s_EnemyUnitBoundViewCount}, " +
            $"enemyViewsFogTracked={s_EnemyUnitFogTrackedViewCount}, gameEnded={s_GameEndManager?.IsGameEnded.ToString() ?? "<unavailable>"}, " +
            $"checkpoints={StageCheckpointService.History.Count}, checkpointGrowth={StageCheckpointService.History.Count - s_CheckpointBaselineCount}, " +
            $"retainedFogSnapshots={StageCheckpointService.RetainedFogSnapshotCount}, " +
            $"retainedFogBytes={StageCheckpointService.RetainedFogPayloadBytes}, " +
            $"retainedFogGrowth={StageCheckpointService.RetainedFogPayloadBytes - s_RetainedFogPayloadBaselineBytes}, " +
            $"inputInjected={s_InjectedInputCount}, authorityPeak={s_MaxAuthorityCount}, boundPeak={s_MaxBoundViewCount}, " +
            $"projectilePeak={s_MaxProjectileCount}, projectileRetainedPeak={s_MaxProjectileRetainedStateCount}, " +
            $"flow={FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount()}, flowBaseline={s_FlowTileBaselineCount}, flowPeak={s_MaxFlowTileCacheCount}/{s_FlowTileCacheLimit}, " +
            $"sharedGoal={FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalFieldCacheCount()}, sharedGoalBaseline={s_SharedGoalBaselineCount}, sharedGoalPeak={s_MaxSharedGoalCacheCount}, " +
            $"pendingFlow={FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount()}, pendingFlowBaseline={s_PendingFlowBaselineCount}, pendingFlowPeak={s_MaxPendingFlowTileCount}, " +
            $"pendingShared={FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount()}, pendingSharedBaseline={s_PendingSharedGoalBaselineCount}, pendingSharedPeak={s_MaxPendingSharedGoalCount}, " +
            $"managedMetricScope=editorMonoAllocatorUsedDiagnosticOnly, managedEqualsMonoUsed={managedEqualsMonoUsed}, profilerContaminatesManagedComparison={s_AllowProfiler}, " +
            $"managedBaseline={s_ManagedBaselineBytes}, managedFinal={s_ManagedFinalBytes}, managedGrowth={managedGrowth}, " +
            $"reservedBaseline={s_ReservedBaselineBytes}, reservedFinal={s_ReservedFinalBytes}, reservedGrowth={reservedGrowth}, " +
            $"monoUsedBaseline={s_MonoUsedBaselineBytes}, monoUsedFinal={s_MonoUsedFinalBytes}, monoUsedGrowth={Math.Max(0L, s_MonoUsedFinalBytes - s_MonoUsedBaselineBytes)}, " +
            $"monoHeapBaseline={s_MonoHeapBaselineBytes}, monoHeapFinal={s_MonoHeapFinalBytes}, monoHeapGrowth={Math.Max(0L, s_MonoHeapFinalBytes - s_MonoHeapBaselineBytes)}, " +
            $"allocatedBaseline={s_AllocatedBaselineBytes}, allocatedFinal={s_AllocatedFinalBytes}, allocatedDelta={Math.Max(0L, s_AllocatedFinalBytes - s_AllocatedBaselineBytes)}, " +
            $"gcBaseline={s_GcCollectionBaselineCount}, gcFinal={s_GcCollectionFinalCount}, gcDelta={Math.Max(0, s_GcCollectionFinalCount - s_GcCollectionBaselineCount)}, " +
            $"flowMemoryBaseline=[{s_FlowMemoryBaseline}], flowMemoryFinal=[{s_FlowMemoryFinal}], " +
            $"referencePoolBaseline=[{s_ReferencePoolBaseline}], referencePoolFinal=[{s_ReferencePoolFinal}], " +
            $"runtimeRetentionBaseline=[{s_RuntimeRetentionBaseline}], runtimeRetentionFinal=[{s_RuntimeRetentionFinal}], " +
            $"projectionFailures={LogicAgentCollisionShadowService.TotalStaticProjectionFailureCount - s_InitialStaticProjectionFailureCount}, " +
            $"failure={s_Failure}";
    }

    private static void ValidateCompletedFrame(
        LogicInputFrame inputFrame,
        LogicGameplayStateDigest gameplayDigest)
    {
        if (LogicReplayRuntime.IsRecording || LogicReplayRuntime.ShouldRecordRuntimeSession)
            throw new InvalidOperationException("Replay recording became enabled during the editor logic stress gate.");
        if (LogicTimeControlService.IsPaused || LogicTimeControlService.SchedulerScale != 1d)
            throw new InvalidOperationException("Logic scheduler changed during the editor logic stress gate.");
        if (s_GameEndManager == null || s_GameEndManager.IsGameEnded)
            throw new InvalidOperationException("Level ended during the editor logic stress gate.");

        InputModel inputModel = GetRequiredInputModel();
        LogicInputTimeline timeline = inputModel.LogicTimeline;
        if (timeline.RetainedSealedFrameCount != 1 || timeline.PendingEventCount != 0)
        {
            throw new InvalidOperationException(
                $"Input retention invariant failed. retained={timeline.RetainedSealedFrameCount}, pending={timeline.PendingEventCount}.");
        }
        if (timeline.LateEventCount != s_InitialLateInputCount)
        {
            throw new InvalidOperationException(
                $"Input timeline observed a late event. initial={s_InitialLateInputCount}, current={timeline.LateEventCount}.");
        }
        int expectedEventCount = s_InjectedThisFrame ? 1 : 0;
        if (inputFrame.Events.Count != expectedEventCount)
        {
            throw new InvalidOperationException(
                $"Injected input event count mismatch. frame={inputFrame.FrameId}, expected={expectedEventCount}, actual={inputFrame.Events.Count}.");
        }
        if (s_InjectedThisFrame && inputFrame.WorldMove != s_InjectedDirection)
            throw new InvalidOperationException($"Injected movement input was not applied on frame {inputFrame.FrameId}.");

        ValidateWorldClosure(requireBoundViews: false);
        if (LogicObstacleCommandService.PendingCount != 0
            || LogicObstacleCommandService.ActiveObstacleCount != s_InitialObstacleCount)
        {
            throw new InvalidOperationException(
                $"Obstacle state drifted. pending={LogicObstacleCommandService.PendingCount}, active={LogicObstacleCommandService.ActiveObstacleCount}, expectedActive={s_InitialObstacleCount}.");
        }
        if (LogicAgentCollisionShadowService.TotalStaticProjectionFailureCount != s_InitialStaticProjectionFailureCount)
            throw new InvalidOperationException("A deterministic static projection failed during the editor logic stress gate.");

        int projectileCount = LogicProjectileService.ActiveCount;
        int retainedProjectileCount = LogicProjectileService.RetainedViewStateCount;
        if (projectileCount > retainedProjectileCount || retainedProjectileCount > ProjectileRetainedStateLimit)
        {
            throw new InvalidOperationException(
                $"Projectile retention invariant failed. active={projectileCount}, retained={retainedProjectileCount}, limit={ProjectileRetainedStateLimit}.");
        }

        int flowTileCount = FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount();
        int deterministicFlowTileCount = FlowFieldCrowdMovementSystem.GetEditorTestDeterministicFlowTileCacheCount();
        int sharedGoalCount = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalFieldCacheCount();
        int pendingFlowCount = FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount();
        int pendingSharedGoalCount = FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount();
        int sharedGoalLimit = Math.Max(16, s_FlowTileCacheLimit / 4);
        int pendingWorkLimit = checked(s_FlowTileCacheLimit * 4 + 1024);
        if (flowTileCount > s_FlowTileCacheLimit || deterministicFlowTileCount > s_FlowTileCacheLimit)
        {
            throw new InvalidOperationException(
                $"Flow tile cache exceeded configured limit. runtime={flowTileCount}, deterministic={deterministicFlowTileCount}, limit={s_FlowTileCacheLimit}.");
        }
        if (sharedGoalCount > sharedGoalLimit)
            throw new InvalidOperationException($"Shared-goal cache exceeded limit. count={sharedGoalCount}, limit={sharedGoalLimit}.");
        if (pendingFlowCount > pendingWorkLimit || pendingSharedGoalCount > pendingWorkLimit)
        {
            throw new InvalidOperationException(
                $"Navigation pending work exceeded bound. flow={pendingFlowCount}, shared={pendingSharedGoalCount}, limit={pendingWorkLimit}.");
        }

        int authorityCount = LogicEntityLifecycleService.AuthorityEntityCount;
        s_MaxAuthorityCount = Math.Max(s_MaxAuthorityCount, authorityCount);
        s_MaxBoundViewCount = Math.Max(s_MaxBoundViewCount, LogicEntityLifecycleService.BoundViewCount);
        s_MaxProjectileCount = Math.Max(s_MaxProjectileCount, projectileCount);
        s_MaxProjectileRetainedStateCount = Math.Max(s_MaxProjectileRetainedStateCount, retainedProjectileCount);
        s_MaxFlowTileCacheCount = Math.Max(s_MaxFlowTileCacheCount, flowTileCount);
        s_MaxSharedGoalCacheCount = Math.Max(s_MaxSharedGoalCacheCount, sharedGoalCount);
        s_MaxPendingFlowTileCount = Math.Max(s_MaxPendingFlowTileCount, pendingFlowCount);
        s_MaxPendingSharedGoalCount = Math.Max(s_MaxPendingSharedGoalCount, pendingSharedGoalCount);
        s_CurrentEnemyUnitCount = CountAliveEnemyUnits();
        s_MaxEnemyUnitCount = Math.Max(s_MaxEnemyUnitCount, s_CurrentEnemyUnitCount);
        s_CombatWindowEnemyObserved |= s_CurrentEnemyUnitCount > 0;
        bool submittedDamage = LogicDamageEventService.LastSubmittedCount > 0;
        s_CombatWindowDamageObserved |= submittedDamage;
        s_CombatAuthorityObserved |= authorityCount > s_CombatBaselineAuthorityCount && s_CurrentEnemyUnitCount > 0;
        s_DamageObserved |= submittedDamage;
        if (s_ComputeFullHash)
        {
            if (s_FirstNavigationAgentsHash == 0)
                s_FirstNavigationAgentsHash = gameplayDigest.Navigation.AgentsHash;
            else
                s_NavigationHashChanged |= s_FirstNavigationAgentsHash != gameplayDigest.Navigation.AgentsHash;
        }
    }

    private static void RecordAuthorityTraces(ulong frame, LogicGameplayStateDigest gameplayDigest)
    {
        s_GameplayTraceHasher.Add(frame);
        s_GameplayTraceHasher.Add(gameplayDigest.GameplayStateHash);

        s_DamageTraceHasher.Add(frame);
        s_DamageTraceHasher.Add(LogicDamageEventService.LastSubmittedCount);
        s_DamageTraceHasher.Add(LogicDamageEventService.LastAppliedCount);
        s_DamageTraceHasher.Add(LogicDamageEventService.LastSkippedDeadTargetCount);
        var events = LogicDamageEventService.LastOrderedEvents;
        s_DamageTraceHasher.Add(events.Count);
        for (int i = 0; i < events.Count; i++)
        {
            LogicHealthEvent healthEvent = events[i];
            s_DamageTraceHasher.Add((int)healthEvent.Kind);
            s_DamageTraceHasher.Add(healthEvent.AttackerId.Value);
            s_DamageTraceHasher.Add(healthEvent.TargetId.Value);
            s_DamageTraceHasher.Add(healthEvent.Amount.RawValue);
            s_DamageTraceHasher.Add((int)healthEvent.ModifyType);
            s_DamageTraceHasher.Add(healthEvent.ApplyDamageHooks);
            s_DamageTraceHasher.Add(healthEvent.HitIndex);
            s_DamageTraceHasher.Add(healthEvent.TotalHits);
        }

        s_DamageSubmittedCount = checked(s_DamageSubmittedCount + LogicDamageEventService.LastSubmittedCount);
        s_DamageAppliedCount = checked(s_DamageAppliedCount + LogicDamageEventService.LastAppliedCount);
        s_DamageSkippedDeadTargetCount = checked(
            s_DamageSkippedDeadTargetCount + LogicDamageEventService.LastSkippedDeadTargetCount);
    }

    private static void ValidateWorldClosure(bool requireBoundViews)
    {
        int authorityCount = LogicEntityLifecycleService.AuthorityEntityCount;
        int activeCount = LogicEntityLifecycleService.ActiveEntityCount;
        int registryCount = EntityRegistry.AllEntities.Count;
        int stateCount = LogicEntityStateStore.Count;
        int snapshotCount = LogicEntityFrameSnapshotService.CapturedEntityCount;
        int boundCount = LogicEntityLifecycleService.BoundViewCount;
        if (authorityCount != activeCount
            || authorityCount != registryCount
            || authorityCount != stateCount
            || authorityCount != snapshotCount)
        {
            throw new InvalidOperationException(
                $"Logic world closure failed. authority={authorityCount}, active={activeCount}, registry={registryCount}, state={stateCount}, snapshot={snapshotCount}.");
        }
        if (boundCount > authorityCount || (requireBoundViews && boundCount != authorityCount))
        {
            throw new InvalidOperationException(
                $"Logic view closure failed. bound={boundCount}, authority={authorityCount}, requireEqual={requireBoundViews}.");
        }
    }

    private static void ValidateCombatObservationWindow()
    {
        if (!s_CombatWindowEnemyObserved || !s_CombatWindowDamageObserved)
        {
            throw new InvalidOperationException(
                $"Combat observation window failed at tick {s_ProcessedTicks}. " +
                $"enemyObserved={s_CombatWindowEnemyObserved}, damageObserved={s_CombatWindowDamageObserved}, " +
                $"currentEnemyUnits={s_CurrentEnemyUnitCount}, phase={PhaseManager.CurrentPhase}.");
        }

        s_CombatWindowsValidated = checked(s_CombatWindowsValidated + 1);
        s_CombatWindowEnemyObserved = false;
        s_CombatWindowDamageObserved = false;
    }

    private static void EnsureDefendCombatScheduled()
    {
        if (PhaseManager.CurrentPhase == GamePhase.Defend || LogicPhaseCommandService.PendingCount != 0)
            return;

        PhaseManager.SwitchToPhase(GamePhase.Defend);
    }

    private static int RestorePlayerBuildingsToFullHealth()
    {
        int count = 0;
        var entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity is not IBuildingLogicContext building
                || building.OwnerFactionId != EntitySideHelper.PlayerFactionId)
            {
                continue;
            }

            count++;
            if (!entity.Alive || building.IsDisabled)
            {
                throw new InvalidOperationException(
                    $"Editor logic stress gate player building became disabled. entity={entity.LogicEntityId.Value}, " +
                    $"key={entity.CharacterKey}, alive={entity.Alive}, disabled={building.IsDisabled}.");
            }

            Fix64 maxHealth = entity.GetProperty(CreatureMainProperty.Health);
            if (maxHealth <= Fix64.Zero)
            {
                throw new InvalidOperationException(
                    $"Editor logic stress gate player building has invalid max health. entity={entity.LogicEntityId.Value}, maxRaw={maxHealth.RawValue}.");
            }
            entity.Heal(maxHealth);
            if (entity.HealthValue != maxHealth)
            {
                throw new InvalidOperationException(
                    $"Editor logic stress gate failed to restore player building health. entity={entity.LogicEntityId.Value}, " +
                    $"currentRaw={entity.HealthValue.RawValue}, maxRaw={maxHealth.RawValue}.");
            }
        }

        return count;
    }

    private static int CountAliveEnemyUnits()
    {
        int count = 0;
        var entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] is LogicEntityState state
                && state.Alive
                && state.Side == SideType.EnemySide
                && !state.IsBuildingEntity)
            {
                count++;
            }
        }

        return count;
    }

    private static void ValidateEnemyUnitPresentationClosure()
    {
        Fog3Manager fogManager = Fog3Manager.Instance ?? GameEntry.GetComponent<Fog3Manager>();
        if (fogManager == null)
            throw new InvalidOperationException("Editor logic stress gate requires Fog3Manager for enemy View validation.");
        if (!fogManager.IsInitialized)
            throw new InvalidOperationException("Editor logic stress gate cannot validate enemy Views before Fog3Manager initialization.");

        fogManager.GetEnemyUnitVisibilityDiagnostics(
            out int aliveLogicCount,
            out s_EnemyUnitBoundViewCount,
            out s_EnemyUnitFogTrackedViewCount,
            out int renderableViewCount,
            out int fogVisibleViewCount,
            out int rendererMismatchViewCount);
        if (aliveLogicCount != s_CurrentEnemyUnitCount)
        {
            throw new InvalidOperationException(
                $"Enemy unit presentation validation observed a logic count mismatch. gate={s_CurrentEnemyUnitCount}, fog={aliveLogicCount}.");
        }
        if (aliveLogicCount <= 0
            || s_EnemyUnitBoundViewCount != aliveLogicCount
            || s_EnemyUnitFogTrackedViewCount != aliveLogicCount
            || renderableViewCount != aliveLogicCount
            || rendererMismatchViewCount != 0)
        {
            throw new InvalidOperationException(
                $"Enemy unit presentation closure failed. logic={aliveLogicCount}, bound={s_EnemyUnitBoundViewCount}, " +
                $"fogTracked={s_EnemyUnitFogTrackedViewCount}, renderable={renderableViewCount}, " +
                $"fogVisible={fogVisibleViewCount}, rendererMismatch={rendererMismatchViewCount}.");
        }

        Log.Info(
            "[LogicLongSessionGate] Enemy presentation closure. logic={0}, bound={1}, fogTracked={2}, " +
            "renderable={3}, fogVisible={4}, rendererMismatch={5}.",
            aliveLogicCount,
            s_EnemyUnitBoundViewCount,
            s_EnemyUnitFogTrackedViewCount,
            renderableViewCount,
            fogVisibleViewCount,
            rendererMismatchViewCount);
    }

    private static void CaptureMemoryBaseline()
    {
        ForceFullCollection();
        s_ManagedBaselineBytes = GC.GetTotalMemory(true);
        s_ReservedBaselineBytes = Profiler.GetTotalReservedMemoryLong();
        s_MonoUsedBaselineBytes = Profiler.GetMonoUsedSizeLong();
        s_MonoHeapBaselineBytes = Profiler.GetMonoHeapSizeLong();
        s_AllocatedBaselineBytes = GC.GetAllocatedBytesForCurrentThread();
        s_GcCollectionBaselineCount = GetGcCollectionCount();
        s_FlowMemoryBaseline = FlowFieldCrowdMovementSystem.CaptureRuntimeMemoryCensus();
        s_ReferencePoolBaseline = CaptureReferencePoolCensus();
        s_RuntimeRetentionBaseline = CaptureRuntimeRetentionCensus();
        s_CheckpointBaselineCount = StageCheckpointService.History.Count;
        s_RetainedFogSnapshotBaselineCount = StageCheckpointService.RetainedFogSnapshotCount;
        s_RetainedFogPayloadBaselineBytes = StageCheckpointService.RetainedFogPayloadBytes;
        s_FlowTileBaselineCount = FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount();
        s_SharedGoalBaselineCount = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalFieldCacheCount();
        s_PendingFlowBaselineCount = FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount();
        s_PendingSharedGoalBaselineCount = FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount();
        Log.Info(
            "[LogicLongSessionGate] Memory baseline. tick={0}, managed={1}, reserved={2}, checkpoints={3}, " +
            "retainedFogSnapshots={4}, retainedFogBytes={5}, flow={6}, sharedGoal={7}, pendingFlow={8}, pendingShared={9}, " +
            "allocated={10}, gc={11}, monoUsed={12}, monoHeap={13}, flowMemory=[{14}], referencePool=[{15}].",
            s_ProcessedTicks,
            s_ManagedBaselineBytes,
            s_ReservedBaselineBytes,
            s_CheckpointBaselineCount,
            s_RetainedFogSnapshotBaselineCount,
            s_RetainedFogPayloadBaselineBytes,
            s_FlowTileBaselineCount,
            s_SharedGoalBaselineCount,
            s_PendingFlowBaselineCount,
            s_PendingSharedGoalBaselineCount,
            s_AllocatedBaselineBytes,
            s_GcCollectionBaselineCount,
            s_MonoUsedBaselineBytes,
            s_MonoHeapBaselineBytes,
            s_FlowMemoryBaseline,
            s_ReferencePoolBaseline);
    }

    private static void ForceFullCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static int GetGcCollectionCount()
    {
        return checked(GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2));
    }

    private static ReferencePoolCensus CaptureReferencePoolCensus()
    {
        GameFramework.ReferencePoolInfo[] infos = GameFramework.ReferencePool.GetAllReferencePoolInfos();
        long unusedCount = 0;
        long usingCount = 0;
        long acquireCount = 0;
        long releaseCount = 0;
        int largestUnusedCount = 0;
        int largestUsingCount = 0;
        string largestUnusedType = "none";
        string largestUsingType = "none";
        for (int i = 0; i < infos.Length; i++)
        {
            GameFramework.ReferencePoolInfo info = infos[i];
            unusedCount = checked(unusedCount + info.UnusedReferenceCount);
            usingCount = checked(usingCount + info.UsingReferenceCount);
            acquireCount = checked(acquireCount + info.AcquireReferenceCount);
            releaseCount = checked(releaseCount + info.ReleaseReferenceCount);
            string typeName = info.Type?.FullName ?? "<null>";
            if (info.UnusedReferenceCount > largestUnusedCount
                || (info.UnusedReferenceCount == largestUnusedCount
                    && string.CompareOrdinal(typeName, largestUnusedType) < 0))
            {
                largestUnusedCount = info.UnusedReferenceCount;
                largestUnusedType = typeName;
            }
            if (info.UsingReferenceCount > largestUsingCount
                || (info.UsingReferenceCount == largestUsingCount
                    && string.CompareOrdinal(typeName, largestUsingType) < 0))
            {
                largestUsingCount = info.UsingReferenceCount;
                largestUsingType = typeName;
            }
        }

        return new ReferencePoolCensus(
            infos.Length,
            unusedCount,
            usingCount,
            acquireCount,
            releaseCount,
            largestUnusedType,
            largestUnusedCount,
            largestUsingType,
            largestUsingCount);
    }

    private static RuntimeRetentionCensus CaptureRuntimeRetentionCensus()
    {
        if (GF.ObjectPool == null)
            throw new InvalidOperationException("Runtime retention census requires ObjectPoolComponent.");

        ObjectPoolBase[] pools = GF.ObjectPool.GetAllObjectPools(true);
        int pooledObjectCount = 0;
        int releasableObjectCount = 0;
        string largestObjectPool = string.Empty;
        int largestObjectPoolCount = 0;
        for (int i = 0; i < pools.Length; i++)
        {
            ObjectPoolBase pool = pools[i]
                                  ?? throw new InvalidOperationException($"Runtime retention census found a null object pool. index={i}.");
            pooledObjectCount = checked(pooledObjectCount + pool.Count);
            releasableObjectCount = checked(releasableObjectCount + pool.CanReleaseCount);
            if (pool.Count > largestObjectPoolCount)
            {
                largestObjectPool = pool.FullName;
                largestObjectPoolCount = pool.Count;
            }
        }

        int commandHistoryCount = checked(
            LogicPhaseCommandService.History.Count
            + LogicTechEffectCommandService.History.Count
            + LogicInteractionCommandService.History.Count
            + LogicCardCommandService.History.Count
            + LogicSkillSlotCommandService.History.Count
            + LogicObstacleCommandService.History.Count);

        return new RuntimeRetentionCensus(
            pools.Length,
            pooledObjectCount,
            releasableObjectCount,
            largestObjectPool,
            largestObjectPoolCount,
            Resources.FindObjectsOfTypeAll<GameObject>().Length,
            Resources.FindObjectsOfTypeAll<Component>().Length,
            Resources.FindObjectsOfTypeAll<MonoBehaviour>().Length,
            Resources.FindObjectsOfTypeAll<Transform>().Length,
            Resources.FindObjectsOfTypeAll<Renderer>().Length,
            Resources.FindObjectsOfTypeAll<Animator>().Length,
            Resources.FindObjectsOfTypeAll<ParticleSystem>().Length,
            Resources.FindObjectsOfTypeAll<AudioSource>().Length,
            DOTween.TotalActiveTweens(),
            DOTween.TotalPlayingTweens(),
            commandHistoryCount);
    }

    private static InputModel GetRequiredInputModel()
    {
        return GF.DataModel?.GetDataModel<InputModel>()
               ?? throw new InvalidOperationException("Editor logic stress gate requires InputModel.");
    }

    private static void EnsureStatus(EditorLogicRuntimeStressGateStatus expected)
    {
        if (Status != expected)
            throw new InvalidOperationException($"Editor logic stress gate status mismatch. expected={expected}, actual={Status}.");
    }

    private static void ResetMetrics()
    {
        Status = EditorLogicRuntimeStressGateStatus.Idle;
        s_TotalTicks = 0;
        s_WarmupTicks = 0;
        s_BatchTicks = 0;
        s_ProcessedTicks = 0;
        s_CombatBaselineAuthorityCount = 0;
        s_InitialObstacleCount = 0;
        s_FlowTileCacheLimit = 0;
        s_InjectedInputCount = 0;
        s_InjectedThisFrame = false;
        s_InjectedDirection = FixVector2.Zero;
        s_ActivationFrame = 0;
        s_StartFrame = 0;
        s_RequestedMeasurementStartFrame = 0;
        s_MeasurementInitialized = false;
        s_GameplayTraceHasher.Reset();
        s_DamageTraceHasher.Reset();
        s_InitialLateInputCount = 0;
        s_InitialStaticProjectionFailureCount = 0;
        s_FirstNavigationAgentsHash = 0;
        s_NavigationHashChanged = false;
        s_CombatAuthorityObserved = false;
        s_DamageObserved = false;
        s_DamageSubmittedCount = 0;
        s_DamageAppliedCount = 0;
        s_DamageSkippedDeadTargetCount = 0;
        s_CombatWindowEnemyObserved = false;
        s_CombatWindowDamageObserved = false;
        s_CombatWindowsValidated = 0;
        s_ProtectedPlayerBuildingCount = 0;
        s_CurrentEnemyUnitCount = 0;
        s_MaxEnemyUnitCount = 0;
        s_EnemyUnitBoundViewCount = 0;
        s_EnemyUnitFogTrackedViewCount = 0;
        s_MaxAuthorityCount = 0;
        s_MaxBoundViewCount = 0;
        s_MaxProjectileCount = 0;
        s_MaxProjectileRetainedStateCount = 0;
        s_MaxFlowTileCacheCount = 0;
        s_MaxSharedGoalCacheCount = 0;
        s_MaxPendingFlowTileCount = 0;
        s_MaxPendingSharedGoalCount = 0;
        s_ManagedBaselineBytes = 0;
        s_ReservedBaselineBytes = 0;
        s_MonoUsedBaselineBytes = 0;
        s_MonoHeapBaselineBytes = 0;
        s_AllocatedBaselineBytes = 0;
        s_GcCollectionBaselineCount = 0;
        s_FlowMemoryBaseline = default;
        s_ReferencePoolBaseline = default;
        s_RuntimeRetentionBaseline = default;
        s_CheckpointBaselineCount = 0;
        s_RetainedFogSnapshotBaselineCount = 0;
        s_RetainedFogPayloadBaselineBytes = 0;
        s_FlowTileBaselineCount = 0;
        s_SharedGoalBaselineCount = 0;
        s_PendingFlowBaselineCount = 0;
        s_PendingSharedGoalBaselineCount = 0;
        s_ManagedFinalBytes = 0;
        s_ReservedFinalBytes = 0;
        s_MonoUsedFinalBytes = 0;
        s_MonoHeapFinalBytes = 0;
        s_AllocatedFinalBytes = 0;
        s_GcCollectionFinalCount = 0;
        s_FlowMemoryFinal = default;
        s_ReferencePoolFinal = default;
        s_RuntimeRetentionFinal = default;
        s_FinalDigest = default;
        s_FinalFrame = 0;
        s_ComputeFullHash = true;
        s_SuppressPhysicsSimulation = false;
        s_AllowProfiler = false;
        s_Failure = string.Empty;
        s_GameEndManager = null;
    }
}
#endif
