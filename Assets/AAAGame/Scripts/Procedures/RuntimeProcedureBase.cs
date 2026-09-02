using System;
using AAAGame.Card;
using AAAGame.MiniMap;
using AAAGame.MiniMap.FOG3;
using Stopwatch = System.Diagnostics.Stopwatch;
using Cysharp.Threading.Tasks;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;
#if UNITY_EDITOR
using UnityEditor;
#endif

[Flags]
public enum RuntimeInitSystemFlags
{
    None = 0,
    MinimapSystem = 1 << 0,
    MinimapUI = 1 << 1,
    CardSystem = 1 << 2,
    CardUI = 1 << 3,
}

public abstract class RuntimeProcedureBase : ProcedureBase
{
    private const int MaxLogicTicksPerRenderFrame = 4;

    private RuntimeInitPipeline m_RuntimeInitPipeline;
    private IFsm<IProcedureManager> m_ProcedureOwner;
    private bool m_InPlaceLevelSwitchInProgress;
    private readonly LogicFrameClock m_LogicFrameClock = new LogicFrameClock();
    private InputManager m_LogicInputManager;
    private CardSetup m_LogicCardSetup;
    private Func<double> m_LogicFrameScaleProvider;
    private Action<ulong, double> m_LogicFrameTickCallback;
    private bool m_LogicFrameClockStarted;
    private ulong m_NextLogicFrameStatusLogFrame;
    private ResourceComponent m_RuntimeResourceComponent;
    private float m_MaxUnloadUnusedAssetsIntervalBeforeRuntime;
#if UNITY_EDITOR
    private double m_EditorStressCutoffRealtime;
    private Func<double> m_EditorStressScaleProvider;
    private Action<ulong, double> m_EditorStressTickCallback;
    private bool m_EditorPauseNeedsClockRebase;
#endif

    protected virtual string RuntimeLevelIdentifier =>
        string.IsNullOrWhiteSpace(ChangeSceneProcedure.SelectedLevelIdentifier)
            ? "Lv_2"
            : ChangeSceneProcedure.SelectedLevelIdentifier;
    protected virtual string RuntimeInitLogTag => "[RuntimeInit]";
    protected virtual RuntimeInitSystemFlags RequiredRuntimeSystems => RuntimeInitSystemFlags.None;

    protected bool IsRuntimeReady => m_RuntimeInitPipeline != null && m_RuntimeInitPipeline.IsCompleted;

#if UNITY_EDITOR
    public bool IsEditorStressRuntimeReady => IsRuntimeReady;
#endif

    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        m_ProcedureOwner = procedureOwner;
        BeginRuntimeResourceUnloadDeferral();
        LogicFrameRuntime.Begin();
        LogicTimeControlService.BeginTimeline();
        LogicInGameValueCommandService.BeginTimeline();
        LogicTeleportCommandService.BeginTimeline();
        LogicGameEndService.BeginTimeline();
        LogicInteractionTargetStateService.BeginTimeline();
        LogicInteractionCommandService.BeginTimeline();
        LogicCardCommandService.BeginTimeline();
        LogicCardPlacementAuthority.BeginTimeline();
        LogicMovementRegionConstraintService.BeginTimeline();
        LogicSkillSlotCommandService.BeginTimeline();
        LogicSkillCastCommandService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicTechEffectCommandService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        LogicEntityViewSpawnQueue.BeginTimeline();
        LogicObstacleCommandService.BeginTimeline();
        LogicEntityFrameSnapshotService.BeginTimeline();
        LogicInteractionAuthorityService.BeginTimeline();
        MAEntityLogicFrameSystem.BeginTimeline();
        ProjectilePresentationService.BeginTimeline();
        m_LogicFrameScaleProvider ??= PrepareNextLogicFrame;
        m_LogicFrameTickCallback ??= ExecuteScheduledLogicFrame;
#if UNITY_EDITOR
        m_EditorStressScaleProvider ??= PrepareNextEditorStressLogicFrame;
        m_EditorStressTickCallback ??= ExecuteScheduledEditorStressLogicFrame;
        m_EditorPauseNeedsClockRebase = false;
        EditorApplication.pauseStateChanged -= OnEditorPauseStateChanged;
        EditorApplication.pauseStateChanged += OnEditorPauseStateChanged;
#endif
        m_LogicInputManager = null;
        m_LogicCardSetup = null;
        m_LogicFrameClockStarted = false;
        m_NextLogicFrameStatusLogFrame = 300;

        if (LevelSelectionService.ShouldShowStartupLevelSwitch)
        {
            if (!LevelSelectionService.OpenLevelSwitch(true))
                throw new InvalidOperationException($"{RuntimeInitLogTag} Failed to open startup level switch UI.");

            return;
        }

        if (!TryValidatePreparedCareerRunLevel(RuntimeLevelIdentifier, out string careerError))
            throw new InvalidOperationException(careerError);

        StartRuntimeInitPipeline(RuntimeLevelIdentifier);
    }

    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
        m_RuntimeInitPipeline?.Update(realElapseSeconds);
        LogicEntityViewSpawnQueue.UpdateRenderFrame();
        if (!IsRuntimeReady)
        {
            return;
        }

        UpdateLogicFrames();
        TryCompleteLoadingPresentation();
        LogicFrameRuntime.SyncPresentationPhysics();
        LogicEntityLifecycleService.UpdatePresentation();
        LevelEntity.UpdateActivePresentation();
        LogicInteractionCommandService.UpdatePresentationEvents();
        InGameDataModel.UpdatePresentationEvents();
        SkillRuntimeDataModel.UpdatePresentationEvents();
        LogicTechEffectCommandService.UpdatePresentationEvents();
        GlobalBuffManager.RequireCurrent().UpdatePresentation();
        BuildManager buildManager = GameEntry.GetComponent<BuildManager>();
        if (buildManager != null)
            buildManager.UpdatePresentation();
        RewardManager rewardManager = GameEntry.GetComponent<RewardManager>();
        if (rewardManager != null)
            rewardManager.UpdatePresentation();
        GameEndManager gameEndManager = GameEntry.GetComponent<GameEndManager>()
                                        ?? throw new InvalidOperationException("RuntimeProcedureBase requires GameEndManager.");
        gameEndManager.UpdatePresentation();
        TutorialManager tutorialManager = GameEntry.GetComponent<TutorialManager>();
        if (tutorialManager != null)
            tutorialManager.UpdatePresentation();
        ObjectiveDestinationService.PublishPendingPresentation();
        ProjectilePresentationService.UpdateRenderFrame();
        OnRuntimeUpdate(elapseSeconds, realElapseSeconds);
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("RuntimeProcedureBase cannot leave during a logic frame.");
#if UNITY_EDITOR
        EditorApplication.pauseStateChanged -= OnEditorPauseStateChanged;
        m_EditorPauseNeedsClockRebase = false;
#endif
        FlowFieldCrowdMovementSystem.ForceEndRuntimeNavigationTransition();
        bool logicInputTimelineStarted = InputModel.RequireActive().LogicTimeline.IsStarted;
        if (m_LogicFrameClockStarted && logicInputTimelineStarted)
        {
            if (ReferenceEquals(m_LogicInputManager, null))
                throw new InvalidOperationException("RuntimeProcedureBase cannot end a started logic input timeline without InputManager.");
            m_LogicInputManager.EndLogicInputTimeline();
        }
        if (StageCheckpointRuntimeCoordinator.IsActive)
            StageCheckpointRuntimeCoordinator.EndSession();
        StageCheckpointRuntimeCoordinator.AbortPendingRestore();
        OnRuntimeShutdown();
        LogicEntityLifecycleService.DeactivateAllForShutdown();
        GF.Entity.HideAllLoadingEntities();
        GF.Entity.HideAllLoadedEntities();
        m_RuntimeInitPipeline?.Shutdown();
        m_RuntimeInitPipeline = null;
        m_ProcedureOwner = null;
        m_InPlaceLevelSwitchInProgress = false;
        m_LogicInputManager = null;
        m_LogicCardSetup = null;
        m_LogicFrameClockStarted = false;
        if (LogicReplayRuntime.IsRecording)
        {
            LogicReplayRuntime.EndRecording();
        }
        ProjectilePresentationService.EndTimeline();
        LogicEntityViewSpawnQueue.EndTimeline();
        LogicObstacleCommandService.EndTimeline();
        LogicEntityLifecycleService.EndTimeline();
        LogicTechEffectCommandService.EndTimeline();
        LogicSkillCastCommandService.EndTimeline();
        LogicSkillSlotCommandService.EndTimeline();
        LogicMovementRegionConstraintService.EndTimeline();
        LogicCardPlacementAuthority.EndTimeline();
        LogicCardCommandService.EndTimeline();
        LogicInteractionCommandService.EndTimeline();
        LogicInteractionAuthorityService.EndTimeline();
        LogicInteractionTargetStateService.EndTimeline();
        MAEntityLogicFrameSystem.EndTimeline();
        LogicEntityFrameSnapshotService.EndTimeline();
        LogicPhaseCommandService.EndTimeline();
        LogicGameEndService.EndTimeline();
        LogicInGameValueCommandService.EndTimeline();
        LogicTeleportCommandService.EndTimeline();
        LogicTimeControlService.EndTimeline();
        LogicFrameRuntime.End();
        EndRuntimeResourceUnloadDeferral();
        base.OnLeave(procedureOwner, isShutdown);
    }

    private void BeginRuntimeResourceUnloadDeferral()
    {
        if (m_RuntimeResourceComponent != null)
            throw new InvalidOperationException("RuntimeProcedureBase.BeginRuntimeResourceUnloadDeferral failed: deferral is already active.");

        ResourceComponent resourceComponent = GameEntry.GetComponent<ResourceComponent>();
        if (resourceComponent == null)
            throw new InvalidOperationException("RuntimeProcedureBase.BeginRuntimeResourceUnloadDeferral failed: ResourceComponent is null.");

        float maxInterval = resourceComponent.MaxUnloadUnusedAssetsInterval;
        if (float.IsNaN(maxInterval) || float.IsInfinity(maxInterval) || maxInterval <= 0f)
            throw new InvalidOperationException(
                $"RuntimeProcedureBase.BeginRuntimeResourceUnloadDeferral failed: invalid max interval {maxInterval:R}.");

        m_RuntimeResourceComponent = resourceComponent;
        m_MaxUnloadUnusedAssetsIntervalBeforeRuntime = maxInterval;
        resourceComponent.MaxUnloadUnusedAssetsInterval = float.PositiveInfinity;
        Log.Info(
            "[RuntimeResource] Automatic unused-asset unload deferred. runtime={0}, originalMaxInterval={1:R}.",
            GetType().Name,
            maxInterval);
    }

    private void EndRuntimeResourceUnloadDeferral()
    {
        if (m_RuntimeResourceComponent == null)
            throw new InvalidOperationException("RuntimeProcedureBase.EndRuntimeResourceUnloadDeferral failed: deferral is not active.");

        m_RuntimeResourceComponent.MaxUnloadUnusedAssetsInterval = m_MaxUnloadUnusedAssetsIntervalBeforeRuntime;
        Log.Info(
            "[RuntimeResource] Automatic unused-asset unload restored. runtime={0}, maxInterval={1:R}, elapsed={2:R}.",
            GetType().Name,
            m_MaxUnloadUnusedAssetsIntervalBeforeRuntime,
            m_RuntimeResourceComponent.LastUnloadUnusedAssetsOperationElapseSeconds);
        m_RuntimeResourceComponent = null;
        m_MaxUnloadUnusedAssetsIntervalBeforeRuntime = 0f;
    }

    internal bool TryEnterPreparedCareerRun(out string errorMessage)
    {
        string levelIdentifier = CareerRunSettings.RuntimeLevelIdentifier;
        if (!TryValidatePreparedCareerRunLevel(levelIdentifier, out errorMessage))
            return false;

        return IsRuntimeReady
            ? TryEnterRuntimeLevelInPlace(levelIdentifier, out errorMessage)
            : TryStartRuntimeLevel(levelIdentifier, out errorMessage);
    }

    private bool TryEnterRuntimeLevelInPlace(string levelIdentifier, out string errorMessage)
    {
        if (!TryValidatePreparedCareerRunLevel(levelIdentifier, out errorMessage))
            return false;

        if (m_ProcedureOwner == null)
        {
            errorMessage = "Runtime procedure is not active.";
            return false;
        }

        if (m_InPlaceLevelSwitchInProgress)
        {
            errorMessage = "Runtime level switch is already running.";
            return false;
        }

        if (!IsRuntimeReady)
        {
            errorMessage = "Runtime procedure is not ready.";
            return false;
        }

        m_InPlaceLevelSwitchInProgress = true;
        EnterRuntimeLevelInPlaceAsync(levelIdentifier).Forget();
        return true;
    }

    public bool TryRestoreStageStart(int phaseEpoch, out string errorMessage)
    {
        errorMessage = null;
        if (m_ProcedureOwner == null)
        {
            errorMessage = "Runtime procedure is not active.";
            return false;
        }
        if (m_InPlaceLevelSwitchInProgress || !IsRuntimeReady)
        {
            errorMessage = "Runtime procedure is not ready for a stage restore.";
            return false;
        }

        StageCheckpointRestoreRequest request;
        try
        {
            request = StageCheckpointRuntimeCoordinator.PrepareRestore(phaseEpoch);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }

        if (TryEnterRuntimeLevelInPlace(request.Checkpoint.LevelId, out errorMessage))
            return true;
        StageCheckpointRuntimeCoordinator.CancelPendingRestore(request);
        return false;
    }

    private bool TryStartRuntimeLevel(string levelIdentifier, out string errorMessage)
    {
        if (!TryValidatePreparedCareerRunLevel(levelIdentifier, out errorMessage))
            return false;

        if (m_ProcedureOwner == null)
        {
            errorMessage = "Runtime procedure is not active.";
            return false;
        }

        if (m_RuntimeInitPipeline != null || m_InPlaceLevelSwitchInProgress)
        {
            errorMessage = "Runtime level initialization is already running.";
            return false;
        }

        ChangeSceneProcedure.SelectedLevelIdentifier = levelIdentifier;
        StartRuntimeInitPipeline(RuntimeLevelIdentifier);
        return true;
    }

    private static bool TryValidatePreparedCareerRunLevel(string levelIdentifier, out string errorMessage)
    {
        if (!CareerRunSettings.HasActiveRun)
        {
            errorMessage = "Cannot enter a runtime level before selecting a starting industry.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(CareerRunSettings.RuntimeLevelIdentifier))
            throw new InvalidOperationException("Active career run has no runtime level identifier.");
        if (!string.Equals(
                levelIdentifier,
                CareerRunSettings.RuntimeLevelIdentifier,
                StringComparison.Ordinal))
        {
            errorMessage =
                $"Runtime level '{levelIdentifier}' does not match prepared career level '{CareerRunSettings.RuntimeLevelIdentifier}'.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    private async UniTaskVoid EnterRuntimeLevelInPlaceAsync(string levelIdentifier)
    {
        LevelEntity previousLevel = LevelEntity.ActiveLevelEntity;
        bool navigationTransitionStarted = false;
        bool runtimePauseHeld = false;
        try
        {
            ChangeSceneProcedure.SelectedLevelIdentifier = levelIdentifier;
            FlowFieldCrowdMovementSystem.BeginRuntimeNavigationTransition();
            navigationTransitionStarted = true;

            var inputManager = GameEntry.GetComponent<InputManager>();
            if (inputManager == null)
                throw new InvalidOperationException("RuntimeProcedureBase.EnterRuntimeLevelInPlaceAsync failed: InputManager is null.");
            inputManager.ChangeState(InputState.UIForm);

            LogicTimeControlService.AcquirePause(LogicTimeControlSources.RuntimeLevelSwitchPause);
            runtimePauseHeld = true;
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.StopAllSfx();
            }

            PhaseManager.CancelRuntimePhaseFlows();
            if (StageCheckpointRuntimeCoordinator.IsActive)
                StageCheckpointRuntimeCoordinator.EndSession();
            OnRuntimeShutdown();
            m_LogicFrameClockStarted = false;
            LogicEntityViewSpawnQueue.ResetForWorldTransition();
            ProjectilePresentationService.ResetForWorldTransition();

            Log.Info(
                "[LogicEntityWorldTransition] Begin. level={0}, frame={1}, requested={2}, bound={3}, active={4}, lastAllocated={5}.",
                levelIdentifier,
                LogicTimeControlService.CurrentFrame,
                LogicEntityLifecycleService.RequestedEntityCount,
                LogicEntityLifecycleService.BoundViewCount,
                LogicEntityLifecycleService.ActiveEntityCount,
                LogicEntityIdAllocator.LastAllocatedValue);
            LogicEntityLifecycleService.DeactivateAllForShutdown();
            LogicInGameValueCommandService.ResetForWorldTransition();
            LogicTeleportCommandService.ResetForWorldTransition();
            LogicGameEndService.ResetForWorldTransition();
            LogicInteractionTargetStateService.ResetForWorldTransition();
            LogicInteractionAuthorityService.ResetForWorldTransition();
            LogicInteractionCommandService.ResetForWorldTransition();
            LogicCardCommandService.ResetForWorldTransition();
            LogicCardPlacementAuthority.ResetForWorldTransition();
            LogicMovementRegionConstraintService.ResetForWorldTransition();
            LogicSkillSlotCommandService.ResetForWorldTransition();
            LogicSkillCastCommandService.ResetForWorldTransition();
            LogicPhaseCommandService.ResetForWorldTransition();
            LogicTechEffectCommandService.ResetForWorldTransition();
            LogicObstacleCommandService.ResetForWorldTransition();
            GF.Entity.HideAllLoadingEntities();
            if (previousLevel == null || !previousLevel.Available)
                throw new InvalidOperationException("Runtime level world transition requires an available previous level.");
            GF.Entity.HideEntitySafe(previousLevel);
            HideRuntimeEntitiesExceptLevel();
            LogicStrongholdMap.Clear();
            Log.Info(
                "[LogicEntityWorldTransition] Old world hidden. level={0}, requested={1}, bound={2}, active={3}, listeners={4}.",
                levelIdentifier,
                LogicEntityLifecycleService.RequestedEntityCount,
                LogicEntityLifecycleService.BoundViewCount,
                LogicEntityLifecycleService.ActiveEntityCount,
                LogicFrameRuntime.ListenerCount);
            m_RuntimeInitPipeline?.Shutdown();
            m_RuntimeInitPipeline = null;
            Fog3Manager fog3Manager = Fog3Manager.Instance ?? GameEntry.GetComponent<Fog3Manager>();
            if (fog3Manager == null)
                throw new InvalidOperationException("Runtime level world transition requires Fog3Manager.");
            fog3Manager.ResetForWorldTransition();
            LogicEntityLifecycleService.ResetForWorldTransition();
            LogicEntityFrameSnapshotService.ResetForWorldTransition();
            MAEntityLogicFrameSystem.ResetForWorldTransition();
            if (LogicReplayRuntime.IsRecording)
            {
                LogicReplayRuntime.EndRecording();
            }
            LogicTimeControlService.ResetFrameTimelinePreservingPauses();
            LogicInteractionCommandService.ResetFrameTimeline();
            LogicCardCommandService.ResetFrameTimeline();
            LogicCardPlacementAuthority.ResetFrameTimeline();
            LogicInGameValueCommandService.ResetFrameTimeline();
            LogicTeleportCommandService.ResetFrameTimeline();
            LogicSkillSlotCommandService.ResetFrameTimeline();
            LogicSkillCastCommandService.ResetFrameTimeline();
            LogicTechEffectCommandService.ResetFrameTimeline();
            LogicEntityLifecycleService.ResetFrameTimelinePreservingEntities();
            LogicObstacleCommandService.ResetFrameTimelinePreservingCommands();
            Log.Info(
                "[LogicEntityWorldTransition] Identity timeline reset. level={0}, requested={1}, bound={2}, active={3}, lastAllocated={4}.",
                levelIdentifier,
                LogicEntityLifecycleService.RequestedEntityCount,
                LogicEntityLifecycleService.BoundViewCount,
                LogicEntityLifecycleService.ActiveEntityCount,
                LogicEntityIdAllocator.LastAllocatedValue);
            await UniTask.Yield(PlayerLoopTiming.Update);

            m_RuntimeInitPipeline = new RuntimeInitPipeline(RuntimeInitLogTag, RuntimeLevelIdentifier, RequiredRuntimeSystems);
            m_RuntimeInitPipeline.Start(() =>
            {
                try
                {
                    LogicTimeControlService.ReleasePause(LogicTimeControlSources.RuntimeLevelSwitchPause);
                    runtimePauseHeld = false;
                    m_InPlaceLevelSwitchInProgress = false;
                    Log.Info(
                        "[LogicEntityWorldTransition] New world ready. level={0}, requested={1}, bound={2}, active={3}, lastAllocated={4}.",
                        levelIdentifier,
                        LogicEntityLifecycleService.RequestedEntityCount,
                        LogicEntityLifecycleService.BoundViewCount,
                        LogicEntityLifecycleService.ActiveEntityCount,
                        LogicEntityIdAllocator.LastAllocatedValue);
                }
                finally
                {
                    if (navigationTransitionStarted)
                    {
                        FlowFieldCrowdMovementSystem.EndRuntimeNavigationTransition();
                        navigationTransitionStarted = false;
                    }
                }

                OnRuntimeInitialized();
            });
        }
        catch (Exception ex)
        {
            m_InPlaceLevelSwitchInProgress = false;
            if (navigationTransitionStarted)
            {
                FlowFieldCrowdMovementSystem.EndRuntimeNavigationTransition();
            }
            if (runtimePauseHeld)
            {
                LogicTimeControlService.ReleasePause(LogicTimeControlSources.RuntimeLevelSwitchPause);
            }
            StageCheckpointRuntimeCoordinator.AbortPendingRestore();
            LevelSelectionService.NotifyLevelLoadFailed(ex.Message);
            Log.Error("{0} Enter runtime level in place failed. level={1}, error={2}", RuntimeInitLogTag, levelIdentifier, ex);
        }
    }

    private void StartRuntimeInitPipeline(string levelIdentifier)
    {
        m_LogicFrameClockStarted = false;
        m_RuntimeInitPipeline = new RuntimeInitPipeline(RuntimeInitLogTag, levelIdentifier, RequiredRuntimeSystems);
        m_RuntimeInitPipeline.Start(OnRuntimeInitialized);
    }

    private void UpdateLogicFrames()
    {
        double realtime = Time.realtimeSinceStartupAsDouble;
        if (!m_LogicFrameClockStarted)
        {
            LogicFrameRuntime.ResetTimeline();
            m_LogicFrameClock.Start(realtime);
            InputManager inputManager = GameEntry.GetComponent<InputManager>();
            if (inputManager == null)
                throw new InvalidOperationException("RuntimeProcedureBase.UpdateLogicFrames failed: InputManager is null.");
            m_LogicInputManager = inputManager;
            CardSetup cardSetup = GameEntry.GetComponent<CardSetup>();
            if (cardSetup == null)
                throw new InvalidOperationException("RuntimeProcedureBase.UpdateLogicFrames failed: CardSetup is null.");
            m_LogicCardSetup = cardSetup;
            inputManager.BeginLogicInputTimeline(realtime);
            LogicFrameRuntime.StartTimeline();
            if (LogicReplayRuntime.ShouldRecordRuntimeSession && !LogicReplayRuntime.IsRecording)
            {
                LogicReplayRuntime.BeginRecording();
            }
            m_LogicFrameClockStarted = true;
            m_NextLogicFrameStatusLogFrame = 300;
            Log.Info("[LogicFrame] Clock started. runtime={0}, realtime={1:R}.", GetType().Name, realtime);
        }

        if (m_LogicInputManager == null)
            throw new InvalidOperationException("RuntimeProcedureBase.UpdateLogicFrames failed: logic InputManager is null.");
        if (m_LogicCardSetup == null)
            throw new InvalidOperationException("RuntimeProcedureBase.UpdateLogicFrames failed: logic CardSetup is null.");

#if UNITY_EDITOR
        if (m_EditorPauseNeedsClockRebase)
        {
            m_LogicFrameClock.RebaseRealtimePreservingAccumulator(realtime);
            m_EditorPauseNeedsClockRebase = false;
            Log.Info("[LogicFrame] Editor pause ended. Rebased realtime without catch-up. runtime={0}, realtime={1:R}.", GetType().Name, realtime);
        }

        if (EditorLogicRuntimeStressGate.OwnsLogicClock)
        {
            UpdateEditorStressLogicFrames(realtime);
            return;
        }
#endif

        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long advanceStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        int tickCount;
        try
        {
            tickCount = m_LogicFrameClock.Advance(
                realtime,
                m_LogicFrameScaleProvider,
                m_LogicFrameTickCallback,
                MaxLogicTicksPerRenderFrame);
        }
        finally
        {
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.LogicFrameAdvance,
                    Stopwatch.GetTimestamp() - advanceStartTicks);
            }
        }
        LogicFrameRuntime.CompleteRenderFrame(
            tickCount,
            m_LogicFrameClock.AccumulatorSeconds,
            m_LogicFrameClock.DeferredRealtimeSeconds,
            m_LogicFrameClock.Interpolation);

        if (tickCount >= 3)
        {
            Log.Warning(
                "[LogicFrame] Catch-up executed. runtime={0}, ticks={1}, frame={2}, deferredRealtimeSeconds={3:F6}, accumulatorSeconds={4:F6}, interpolation={5:F4}.",
                GetType().Name,
                tickCount,
                m_LogicFrameClock.Frame,
                m_LogicFrameClock.DeferredRealtimeSeconds,
                m_LogicFrameClock.AccumulatorSeconds,
                m_LogicFrameClock.Interpolation);
        }

        if (m_LogicFrameClock.Frame >= m_NextLogicFrameStatusLogFrame)
        {
            Log.Info(
                "[LogicFrame] Status. runtime={0}, frame={1}, ticksThisRenderFrame={2}, deferredRealtimeSeconds={3:F6}, accumulatorSeconds={4:F6}, interpolation={5:F4}, listeners={6}.",
                GetType().Name,
                m_LogicFrameClock.Frame,
                tickCount,
                m_LogicFrameClock.DeferredRealtimeSeconds,
                m_LogicFrameClock.AccumulatorSeconds,
                m_LogicFrameClock.Interpolation,
                LogicFrameRuntime.ListenerCount);
            m_NextLogicFrameStatusLogFrame = m_LogicFrameClock.Frame + 300;
        }

    }

#if UNITY_EDITOR
    private void OnEditorPauseStateChanged(PauseState state)
    {
        if (state == PauseState.Unpaused && m_LogicFrameClockStarted)
            m_EditorPauseNeedsClockRebase = true;
    }
#endif

    private double PrepareNextLogicFrame()
    {
        if (LogicGameEndService.IsGameEnded)
            return 0d;

        LogicTimeControlService.PrepareFrame(checked(m_LogicFrameClock.Frame + 1));
        return LogicTimeControlService.SchedulerScale;
    }

    private void ExecuteScheduledLogicFrame(ulong frame, double cutoffRealtime)
    {
        LogicGameplayStateDigest gameplayDigest = ExecuteLogicFrame(
            m_LogicInputManager,
            m_LogicCardSetup,
            frame,
            cutoffRealtime,
            out LogicInputFrame inputFrame);
        if (LogicReplayRuntime.IsRecording)
            LogicReplayRuntime.RecordFrame(inputFrame, gameplayDigest);
    }

    private static LogicGameplayStateDigest ExecuteLogicFrame(
        InputManager logicInputManager,
        CardSetup cardSetup,
        ulong frame,
        double cutoffRealtime,
        out LogicInputFrame inputFrame)
    {
        LogicFrameRuntime.BeginFrameExecution(frame);
        try
        {
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long commandsStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        long commandSectionStartTicks;
        LogicTimeControlService.BeginFrame(frame);
        LogicFactionVisionService.Advance(LogicFrameRuntime.FixedDeltaTime);
        inputFrame = logicInputManager.SealLogicInputFrame(frame, cutoffRealtime);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.LogicFrameCommandTimeAndInput,
                Stopwatch.GetTimestamp() - commandsStartTicks);
        }

        commandSectionStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        LogicInGameValueCommandService.ApplyFrame(frame);
        LogicTeleportCommandService.ApplyFrame(frame);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.LogicFrameCommandValueTeleport,
                Stopwatch.GetTimestamp() - commandSectionStartTicks);
        }

        commandSectionStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (LogicCardPlacementAuthority.IsWorldBound)
        {
            LogicCardPlacementAuthority.ApplyFrame(frame);
        }
        else if (LogicCardRuntimeState.IsBound)
        {
            throw new InvalidOperationException("Card runtime is bound without a logic card-placement world.");
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.LogicFrameCommandCardPlacement,
                Stopwatch.GetTimestamp() - commandSectionStartTicks);
        }

        commandSectionStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        LogicCardCommandService.ApplyFrame(frame);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.LogicFrameCommandCard,
                Stopwatch.GetTimestamp() - commandSectionStartTicks);
        }

        commandSectionStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        LogicSkillSlotCommandService.ApplyFrame(frame);
        LogicPhaseCommandService.ApplyFrame(frame);
        LogicSkillCastCommandService.ApplyFrame(frame);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.LogicFrameCommandSkills,
                Stopwatch.GetTimestamp() - commandSectionStartTicks);
        }

        commandSectionStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        LogicMovementRegionConstraintService.ApplyFrame(frame);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.LogicFrameCommandMovementConstraint,
                Stopwatch.GetTimestamp() - commandSectionStartTicks);
        }

        if (cardSetup == null)
            throw new ArgumentNullException(nameof(cardSetup));
        commandSectionStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        cardSetup.ApplyLogicFrame(frame);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.LogicFrameCommandCardSetup,
                Stopwatch.GetTimestamp() - commandSectionStartTicks);
        }

        commandSectionStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        LogicInteractionCommandService.ApplyFrame(frame);
        LogicTechEffectCommandService.ApplyFrame(frame);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.LogicFrameCommandInteractionTech,
                Stopwatch.GetTimestamp() - commandSectionStartTicks);
        }

        commandSectionStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        DefendPhaseRuntime.ApplyScheduledSpawnRequests(frame);
        LogicEntityLifecycleService.ApplyFrame(frame);
        LogicObstacleCommandService.ApplyFrame(frame);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.LogicFrameCommandSpawnLifecycleObstacle,
                Stopwatch.GetTimestamp() - commandSectionStartTicks);
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.LogicFrameCommands,
                Stopwatch.GetTimestamp() - commandsStartTicks);
        }

        long tickStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (profile)
            MainThreadFrameProfiler.BeginLogicTick(frame);
        try
        {
            LogicFrameRuntime.Tick(frame);
        }
        finally
        {
            if (profile)
            {
                long tickElapsedTicks = Stopwatch.GetTimestamp() - tickStartTicks;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.LogicFrameTick,
                    tickElapsedTicks);
                MainThreadFrameProfiler.RecordLogicTickDuration(tickElapsedTicks);
            }
        }
        LogicGameEndService.ApplyFrame(frame);
        bool requiresGameplayDigest = LogicReplayRuntime.IsRecording;
#if UNITY_EDITOR
        requiresGameplayDigest |= EditorLogicRuntimeStressGate.OwnsLogicClock
                                  && EditorLogicRuntimeStressGate.ComputeFullHash;
#endif
        if (!requiresGameplayDigest)
            return LogicGameplayStateDigest.FromOpaqueHash(0);

        return LogicGameplayStateHasher.ComputeCurrentFrameDigest();
        }
        finally
        {
            LogicFrameRuntime.EndFrameExecution(frame);
        }
    }

    private void TryCompleteLoadingPresentation()
    {
        if (m_RuntimeInitPipeline == null
            || !m_RuntimeInitPipeline.IsCompleted
            || m_RuntimeInitPipeline.IsLoadingPresentationCompleted)
        {
            return;
        }

        if (!m_RuntimeInitPipeline.RequiresAuthoritativeFogPresentation)
        {
            m_RuntimeInitPipeline.CompleteLoadingPresentation();
            return;
        }

        Fog3Manager fogManager = Fog3Manager.Instance ?? GameEntry.GetComponent<Fog3Manager>();
        if (fogManager == null || !fogManager.IsInitialized || fogManager.MapData == null)
            throw new InvalidOperationException("Runtime loading completion requires initialized Fog3Manager presentation.");
        if (!LogicCardPlacementAuthority.IsActive || !LogicCardPlacementAuthority.IsWorldBound)
            throw new InvalidOperationException("Runtime loading completion requires bound authoritative fog.");
        if (!LogicCardPlacementAuthority.IsBoundTo(fogManager.MapData))
            throw new InvalidOperationException("Runtime loading completion found authoritative fog bound to a stale map.");
        if (LogicCardPlacementAuthority.LastAppliedFrame == 0 || !fogManager.HasPresentedLogicFrameVisibility)
            return;

        m_RuntimeInitPipeline.CompleteLoadingPresentation();
    }

#if UNITY_EDITOR
    private void UpdateEditorStressLogicFrames(double realtime)
    {
        if (EditorLogicRuntimeStressGate.Status == EditorLogicRuntimeStressGateStatus.Armed)
        {
            m_LogicFrameClock.RebaseRealtimePreservingAccumulator(realtime);
            m_EditorStressCutoffRealtime = realtime;
            EditorLogicRuntimeStressGate.Activate(m_LogicFrameClock.Frame);
        }

        if (EditorLogicRuntimeStressGate.Status != EditorLogicRuntimeStressGateStatus.Running)
        {
            LogicFrameRuntime.CompleteRenderFrame(
                0,
                m_LogicFrameClock.AccumulatorSeconds,
                m_LogicFrameClock.DeferredRealtimeSeconds,
                m_LogicFrameClock.Interpolation);
            return;
        }

        int requestedTickCount = EditorLogicRuntimeStressGate.NextBatchTickCount;
        m_EditorStressCutoffRealtime += requestedTickCount * LogicFrameClock.FrameDurationSeconds;

        try
        {
            int actualTickCount = m_LogicFrameClock.Advance(
                m_EditorStressCutoffRealtime,
                m_EditorStressScaleProvider,
                m_EditorStressTickCallback);

            if (actualTickCount != requestedTickCount)
            {
                throw new InvalidOperationException(
                    $"Editor logic stress gate tick mismatch. requested={requestedTickCount}, actual={actualTickCount}, frame={m_LogicFrameClock.Frame}.");
            }

            LogicFrameRuntime.CompleteRenderFrame(
                actualTickCount,
                m_LogicFrameClock.AccumulatorSeconds,
                m_LogicFrameClock.DeferredRealtimeSeconds,
                m_LogicFrameClock.Interpolation);
        }
        catch (Exception exception)
        {
            EditorLogicRuntimeStressGate.Fail(exception);
            throw;
        }
    }

    private double PrepareNextEditorStressLogicFrame()
    {
        LogicTimeControlService.PrepareFrame(checked(m_LogicFrameClock.Frame + 1));
        double schedulerScale = LogicTimeControlService.SchedulerScale;
        if (schedulerScale != 1d)
        {
            throw new InvalidOperationException(
                $"Editor logic stress gate requires scheduler scale 1. frame={m_LogicFrameClock.Frame + 1}, scale={schedulerScale:R}.");
        }
        return schedulerScale;
    }

    private void ExecuteScheduledEditorStressLogicFrame(ulong frame, double cutoffRealtime)
    {
        EditorLogicRuntimeStressGate.PrepareInputFrame(frame, cutoffRealtime);
        LogicGameplayStateDigest gameplayDigest = ExecuteLogicFrame(
            m_LogicInputManager,
            m_LogicCardSetup,
            frame,
            cutoffRealtime,
            out LogicInputFrame inputFrame);
        EditorLogicRuntimeStressGate.RecordFrame(inputFrame, gameplayDigest);
    }
#endif

    private static void HideRuntimeEntitiesExceptLevel()
    {
        HideEntityGroup(Const.EntityGroup.Default);
        HideEntityGroup(Const.EntityGroup.Player);
        HideEntityGroup(Const.EntityGroup.Effect);
        HideEntityGroup(Const.EntityGroup.Item);
        HideEntityGroup(Const.EntityGroup.Bullet);
        HideEntityGroup(Const.EntityGroup.Unrecycle);
        HideEntityGroup(Const.EntityGroup.Building);
        HideEntityGroup(Const.EntityGroup.Creature);
    }

    private static void HideEntityGroup(Const.EntityGroup group)
    {
        string groupName = group.ToString();
        if (GF.Entity == null || !GF.Entity.HasEntityGroup(groupName))
        {
            return;
        }

        GF.Entity.HideGroup(groupName);
    }

    protected virtual void OnRuntimeInitialized()
    {
    }

    protected virtual void OnRuntimeUpdate(float elapseSeconds, float realElapseSeconds)
    {
    }

    protected virtual void OnRuntimeShutdown()
    {
    }
}

internal sealed class RuntimeInitPipeline
{
    private const float RuntimeProgressStart = 0.85f;
    private const float RuntimeProgressBeforeComplete = 0.994f;
    private const float RuntimeProgressSmoothSpeed = 0.35f;

    private readonly string m_LogTag;
    private readonly string m_LevelIdentifier;
    private readonly RuntimeInitSystemFlags m_RuntimeSystems;

    private GeneralSetup m_GeneralSetup;
    private Action m_OnCompleted;
    private bool m_IsStarted;
    private int m_MinimapUIFormId;
    private int m_InGameUIFormId;
    private float m_DisplayedProgress;
    private float m_TargetProgress;
    private bool m_FinishPending;
    private bool m_LoadingPresentationCompleted;
    private Stopwatch m_StartupStopwatch;

    public bool IsCompleted { get; private set; }
    public bool IsLoadingPresentationCompleted => m_LoadingPresentationCompleted;
    public bool RequiresAuthoritativeFogPresentation =>
        HasFlag(RuntimeInitSystemFlags.MinimapSystem) || HasFlag(RuntimeInitSystemFlags.MinimapUI);

    public RuntimeInitPipeline(string logTag, string levelIdentifier, RuntimeInitSystemFlags runtimeSystems)
    {
        m_LogTag = string.IsNullOrWhiteSpace(logTag) ? "[RuntimeInit]" : logTag;
        m_LevelIdentifier = string.IsNullOrWhiteSpace(levelIdentifier) ? "Lv_1" : levelIdentifier;
        m_RuntimeSystems = runtimeSystems;
        m_MinimapUIFormId = -1;
        m_InGameUIFormId = -1;
    }

    public void Start(Action onCompleted)
    {
        if (m_IsStarted)
        {
            Log.Warning("{0} RuntimeInitPipeline already started.", m_LogTag);
            return;
        }

        m_IsStarted = true;
        IsCompleted = false;
        m_OnCompleted = onCompleted;
        m_DisplayedProgress = RuntimeProgressStart;
        m_TargetProgress = RuntimeProgressStart;
        m_FinishPending = false;
        m_LoadingPresentationCompleted = false;
        m_StartupStopwatch = Stopwatch.StartNew();
        PhaseManager.CancelRuntimePhaseFlows();
        LogRuntimeInitTiming("cancel-phase-flows");
        LevelSelectionService.NotifyLevelLoadStarted();
        NotifyLevelLoadProgress();

        m_GeneralSetup = GameEntry.GetComponent<GeneralSetup>();
        if (m_GeneralSetup == null)
        {
            Log.Error("{0} Missing GeneralSetup component on GameEntry.", m_LogTag);
            CompleteStartup();
            return;
        }

        m_GeneralSetup.OnGeneralSetupCompleted += HandleGeneralSetupCompleted;
        SetTargetProgress(RuntimeProgressBeforeComplete);

        Log.Info("{0} Runtime startup begin. level={1}, systems={2}", m_LogTag, m_LevelIdentifier, m_RuntimeSystems);
        LogRuntimeInitTiming("before-general-setup");

        if (m_GeneralSetup.IsGeneralSetupCompleted)
        {
            HandleGeneralSetupCompleted();
            return;
        }

        m_GeneralSetup.GeneralSystemSetup(m_LevelIdentifier);
        LogRuntimeInitTiming("general-setup-requested");
    }

    public void Update(float elapseSeconds)
    {
        if (!m_IsStarted || IsCompleted)
        {
            return;
        }

        if (m_DisplayedProgress >= m_TargetProgress)
        {
            return;
        }

        float delta = Math.Max(0f, elapseSeconds);
        m_DisplayedProgress = Math.Min(m_TargetProgress, m_DisplayedProgress + RuntimeProgressSmoothSpeed * delta);
        NotifyLevelLoadProgress();

        if (m_FinishPending && m_DisplayedProgress >= 0.999f)
        {
            FinishStartup();
        }
    }

    public void Shutdown()
    {
        if (!m_IsStarted)
        {
            return;
        }

        if (m_GeneralSetup != null)
        {
            m_GeneralSetup.OnGeneralSetupCompleted -= HandleGeneralSetupCompleted;
        }

        ShutdownRuntimeManagers();

        if (m_GeneralSetup != null)
        {
            m_GeneralSetup.GeneralSystemShutDown();
        }

        m_IsStarted = false;
        IsCompleted = false;
        m_OnCompleted = null;
        m_GeneralSetup = null;
        m_FinishPending = false;
        m_LoadingPresentationCompleted = false;
        m_StartupStopwatch = null;
    }

    private void HandleGeneralSetupCompleted()
    {
        if (IsCompleted)
        {
            return;
        }

        LogRuntimeInitTiming("general-setup-completed");
        InitializeRuntimeManagers();
        LogRuntimeInitTiming("runtime-managers-initialized");
        BeginCompleteStartup();
    }

    private void InitializeRuntimeManagers()
    {
        bool needMinimapSystem = HasFlag(RuntimeInitSystemFlags.MinimapSystem) || HasFlag(RuntimeInitSystemFlags.MinimapUI);
        bool needMinimapUI = HasFlag(RuntimeInitSystemFlags.MinimapUI);
        bool needCardSystem = HasFlag(RuntimeInitSystemFlags.CardSystem) || HasFlag(RuntimeInitSystemFlags.CardUI);

        var minimapManager = GameEntry.GetComponent<MinimapManager>();
        var cardSetup = GameEntry.GetComponent<CardSetup>();

        if (needMinimapSystem)
        {
            if (minimapManager != null)
            {
                Log.Info("{0} MinimapManager is ready.", m_LogTag);
            }
            else
            {
                Log.Error("{0} Missing MinimapManager component.", m_LogTag);
            }

            if (cardSetup == null)
                throw new InvalidOperationException("Runtime initialization requires CardSetup to bind logic fog exploration.");
            cardSetup.EnsureLogicCardPlacementWorldBound();
        }

        if (needCardSystem)
        {
            if (cardSetup != null)
            {
                cardSetup.CardSystemSetup();
            }
            else
            {
                Log.Error("{0} Missing CardSetup component.", m_LogTag);
            }
        }

        if (GF.UI.IsLoadingUIForm(UIViews.InGameUIForm) || GF.UI.HasUIForm(UIViews.InGameUIForm))
        {
            Log.Info("{0} InGameUIForm is already open/loading, skip open.", m_LogTag);
        }
        else
        {
            m_InGameUIFormId = GF.UI.OpenUIForm(UIViews.InGameUIForm);
            if (m_InGameUIFormId == -1)
            {
                Log.Error("{0} Failed to open InGameUIForm.", m_LogTag);
            }
            else
            {
                Log.Info("{0} Opened InGameUIForm.", m_LogTag);
            }
        }

        if (needMinimapUI)
        {
            if (minimapManager != null)
            {
                bool inGameUIOwnsMinimap = m_InGameUIFormId != -1
                    || GF.UI.IsLoadingUIForm(UIViews.InGameUIForm)
                    || GF.UI.HasUIForm(UIViews.InGameUIForm);

                if (inGameUIOwnsMinimap)
                {
                    Log.Info("{0} MinimapUI is owned by InGameUIForm, skip standalone open.", m_LogTag);
                }
                else if (GF.UI.IsLoadingUIForm(UIViews.MinimapUI) || GF.UI.HasUIForm(UIViews.MinimapUI))
                {
                    Log.Info("{0} MinimapUI is already open/loading, skip open.", m_LogTag);
                }
                else
                {
                    m_MinimapUIFormId = GF.UI.OpenUIForm(UIViews.MinimapUI);
                    if (m_MinimapUIFormId == -1)
                    {
                        Log.Error("{0} Failed to open MinimapUI.", m_LogTag);
                    }
                    else
                    {
                        Log.Info("{0} Opened MinimapUI by MinimapManager path.", m_LogTag);
                    }
                }
            }
            else
            {
                Log.Error("{0} Cannot open MinimapUI: missing MinimapManager.", m_LogTag);
            }
        }

        if (HasFlag(RuntimeInitSystemFlags.CardUI) && cardSetup != null)
        {
            cardSetup.OpenCardUI();
        }
    }

    private void ShutdownRuntimeManagers()
    {
        bool needMinimapUI = HasFlag(RuntimeInitSystemFlags.MinimapUI);
        bool needCardSystem = HasFlag(RuntimeInitSystemFlags.CardSystem) || HasFlag(RuntimeInitSystemFlags.CardUI);

        if (needMinimapUI && m_MinimapUIFormId != -1)
        {
            CloseUIFormIfAlive(m_MinimapUIFormId);
            m_MinimapUIFormId = -1;
        }

        if (m_InGameUIFormId != -1)
        {
            CloseUIFormIfAlive(m_InGameUIFormId);
            m_InGameUIFormId = -1;
        }

        if (needCardSystem)
        {
            var cardSetup = GameEntry.GetComponent<CardSetup>();
            if (cardSetup != null)
            {
                cardSetup.CardSystemShutdown(false);
            }
        }
    }

    private static void CloseUIFormIfAlive(int uiFormId)
    {
        if (uiFormId == -1 || GF.UI == null)
        {
            return;
        }

        if (GF.UI.IsLoadingUIForm(uiFormId) || GF.UI.HasUIForm(uiFormId))
        {
            GF.UI.CloseUIForm(uiFormId);
        }
    }

    private void BeginCompleteStartup()
    {
        m_TargetProgress = 1f;
        m_FinishPending = true;
    }

    private void CompleteStartup()
    {
        m_DisplayedProgress = 1f;
        m_TargetProgress = 1f;
        FinishStartup();
    }

    private void FinishStartup()
    {
        if (IsCompleted)
        {
            return;
        }

        IsCompleted = true;
        m_FinishPending = false;
        m_DisplayedProgress = 1f;
        m_TargetProgress = 1f;
        NotifyLevelLoadProgress();
        m_OnCompleted?.Invoke();
        LevelSelectionService.NotifyLevelRuntimeReadyForFirstFrame();
    }

    public void CompleteLoadingPresentation()
    {
        if (!IsCompleted)
            throw new InvalidOperationException("Runtime loading presentation cannot complete before runtime initialization.");
        if (m_LoadingPresentationCompleted)
            return;

        m_LoadingPresentationCompleted = true;
        LevelSelectionService.NotifyLevelLoadCompleted();
        LogRuntimeInitTiming("startup-complete");
        Log.Info("{0} Runtime startup completed after authoritative fog presentation.", m_LogTag);
        ShowLevelObjectiveTips();
        EnablePlayerInput();
    }

    private void LogRuntimeInitTiming(string stage)
    {
        double elapsedMs = m_StartupStopwatch != null ? m_StartupStopwatch.Elapsed.TotalMilliseconds : 0.0;
        Log.Info("{0} RuntimeInitTiming stage={1} elapsedMs={2:F3}", m_LogTag, stage, elapsedMs);
    }

    private static void ShowLevelObjectiveTips()
    {
    }

    private static void EnablePlayerInput()
    {
        var inputManager = GameEntry.GetComponent<InputManager>();
        if (inputManager == null)
        {
            return;
        }

        inputManager.FindModel();
        if (inputManager.CurState != InputState.Game)
        {
            inputManager.ChangeState(InputState.Game);
        }
    }

    private bool HasFlag(RuntimeInitSystemFlags flag)
    {
        return (m_RuntimeSystems & flag) != 0;
    }

    private void SetTargetProgress(float progress)
    {
        m_TargetProgress = Math.Max(m_TargetProgress, Math.Min(progress, 0.99f));
    }

    private void NotifyLevelLoadProgress()
    {
        float progress = (m_DisplayedProgress - RuntimeProgressStart) / (1f - RuntimeProgressStart);
        LevelSelectionService.NotifyLevelLoadProgress(progress);
    }
}
