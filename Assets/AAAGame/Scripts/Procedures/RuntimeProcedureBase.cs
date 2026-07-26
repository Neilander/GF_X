using System;
using AAAGame.Card;
using AAAGame.MiniMap;
using Stopwatch = System.Diagnostics.Stopwatch;
using Cysharp.Threading.Tasks;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;

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
    public static bool SuppressNextBuiltinLoadingProgress { get; set; }

    private RuntimeInitPipeline m_RuntimeInitPipeline;
    private IFsm<IProcedureManager> m_ProcedureOwner;
    private bool m_InPlaceLevelSwitchInProgress;
    private readonly LogicFrameClock m_LogicFrameClock = new LogicFrameClock();
    private bool m_LogicFrameClockStarted;
    private bool m_LogicTimelineRestartPending;
    private ulong m_NextLogicFrameStatusLogFrame;
#if UNITY_EDITOR
    private double m_EditorStressCutoffRealtime;
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
        LogicFrameRuntime.Begin();
        LogicTimeControlService.BeginTimeline();
        LogicInteractionHoldService.BeginTimeline();
        LogicInteractionTargetStateService.BeginTimeline();
        LogicInteractionCommandService.BeginTimeline();
        LogicCardCommandService.BeginTimeline();
        LogicCardPlacementAuthority.BeginTimeline();
        LogicSkillSlotCommandService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicTechEffectCommandService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        LogicObstacleCommandService.BeginTimeline();
        LogicEntityFrameSnapshotService.BeginTimeline();
        MAEntityLogicFrameSystem.BeginTimeline();
        m_LogicFrameClockStarted = false;
        m_LogicTimelineRestartPending = false;
        m_NextLogicFrameStatusLogFrame = 300;

        if (LevelSelectionService.ShouldShowStartupLevelSwitch)
        {
            GF.BuiltinView.HideLoadingProgress();
            if (!LevelSelectionService.OpenLevelSwitch(true))
            {
                Log.Error("{0} Failed to open startup level switch UI.", RuntimeInitLogTag);
                StartRuntimeInitPipeline(RuntimeLevelIdentifier, true);
            }

            return;
        }

        bool showBuiltinProgress = !SuppressNextBuiltinLoadingProgress;
        SuppressNextBuiltinLoadingProgress = false;
        StartRuntimeInitPipeline(RuntimeLevelIdentifier, showBuiltinProgress);
    }

    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
        m_RuntimeInitPipeline?.Update(realElapseSeconds);
        if (!IsRuntimeReady)
        {
            return;
        }

        UpdateLogicFrames();
        OnRuntimeUpdate(elapseSeconds, realElapseSeconds);
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        FlowFieldCrowdMovementSystem.ForceEndRuntimeNavigationTransition();
        if (StageCheckpointRuntimeCoordinator.IsActive)
            StageCheckpointRuntimeCoordinator.EndSession();
        OnRuntimeShutdown();
        m_RuntimeInitPipeline?.Shutdown();
        m_RuntimeInitPipeline = null;
        m_ProcedureOwner = null;
        m_InPlaceLevelSwitchInProgress = false;
        m_LogicFrameClockStarted = false;
        m_LogicTimelineRestartPending = false;
        if (LogicReplayRuntime.IsRecording)
        {
            LogicReplayRuntime.EndRecording();
        }
        LogicEntityLifecycleService.DeactivateAllForShutdown();
        GF.Entity.HideAllLoadingEntities();
        GF.Entity.HideAllLoadedEntities();
        LogicObstacleCommandService.EndTimeline();
        LogicEntityLifecycleService.EndTimeline();
        LogicTechEffectCommandService.EndTimeline();
        LogicSkillSlotCommandService.EndTimeline();
        LogicCardPlacementAuthority.EndTimeline();
        LogicCardCommandService.EndTimeline();
        LogicInteractionCommandService.EndTimeline();
        LogicInteractionTargetStateService.EndTimeline();
        MAEntityLogicFrameSystem.EndTimeline();
        LogicEntityFrameSnapshotService.EndTimeline();
        LogicPhaseCommandService.EndTimeline();
        LogicInteractionHoldService.EndTimeline();
        LogicTimeControlService.EndTimeline();
        LogicFrameRuntime.End();
        base.OnLeave(procedureOwner, isShutdown);
    }

    public bool TryEnterRuntimeLevel(string levelIdentifier, out string errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(levelIdentifier))
        {
            errorMessage = "Level identifier is empty.";
            return false;
        }

        if (m_ProcedureOwner == null)
        {
            errorMessage = "Runtime procedure is not active.";
            return false;
        }

        string sceneName = !string.IsNullOrWhiteSpace(ChangeSceneProcedure.SelectedSceneForGame)
            ? ChangeSceneProcedure.SelectedSceneForGame
            : AppSettings.Instance.StartSceneName;

        ChangeSceneProcedure.SelectedLevelIdentifier = levelIdentifier;
        ChangeSceneProcedure.SelectedProcedureForGame = GetType().Name;
        ChangeSceneProcedure.SelectedSceneForGame = sceneName;

        m_ProcedureOwner.SetData<VarString>(ChangeSceneProcedure.P_SceneName, sceneName);
        ChangeState<ChangeSceneProcedure>(m_ProcedureOwner);
        return true;
    }

    public bool TryEnterRuntimeLevelInPlace(string levelIdentifier, out string errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(levelIdentifier))
        {
            errorMessage = "Level identifier is empty.";
            return false;
        }

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

    public bool TryStartRuntimeLevel(string levelIdentifier, out string errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(levelIdentifier))
        {
            errorMessage = "Level identifier is empty.";
            return false;
        }

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
        StartRuntimeInitPipeline(RuntimeLevelIdentifier, false);
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
            m_RuntimeInitPipeline?.Shutdown();
            m_RuntimeInitPipeline = null;
            m_LogicFrameClockStarted = false;
            m_LogicTimelineRestartPending = true;

            Log.Info(
                "[LogicEntityWorldTransition] Begin. level={0}, frame={1}, requested={2}, bound={3}, active={4}, lastAllocated={5}.",
                levelIdentifier,
                LogicTimeControlService.CurrentFrame,
                LogicEntityLifecycleService.RequestedEntityCount,
                LogicEntityLifecycleService.BoundViewCount,
                LogicEntityLifecycleService.ActiveEntityCount,
                LogicEntityIdAllocator.LastAllocatedValue);
            LogicEntityLifecycleService.DeactivateAllForShutdown();
            LogicInteractionHoldService.ResetForWorldTransition();
            LogicInteractionTargetStateService.ResetForWorldTransition();
            LogicInteractionCommandService.ResetForWorldTransition();
            LogicCardCommandService.ResetForWorldTransition();
            LogicCardPlacementAuthority.ResetForWorldTransition();
            LogicSkillSlotCommandService.ResetForWorldTransition();
            LogicPhaseCommandService.ResetForWorldTransition();
            LogicTechEffectCommandService.ResetForWorldTransition();
            LogicObstacleCommandService.ResetForWorldTransition();
            GF.Entity.HideAllLoadingEntities();
            HideRuntimeEntitiesExceptLevel();
            Log.Info(
                "[LogicEntityWorldTransition] Old world hidden. level={0}, requested={1}, bound={2}, active={3}, listeners={4}.",
                levelIdentifier,
                LogicEntityLifecycleService.RequestedEntityCount,
                LogicEntityLifecycleService.BoundViewCount,
                LogicEntityLifecycleService.ActiveEntityCount,
                LogicFrameRuntime.ListenerCount);
            LogicEntityLifecycleService.ResetForWorldTransition();
            LogicEntityFrameSnapshotService.ResetForWorldTransition();
            MAEntityLogicFrameSystem.ResetForWorldTransition();
            Log.Info(
                "[LogicEntityWorldTransition] Identity timeline reset. level={0}, requested={1}, bound={2}, active={3}, lastAllocated={4}.",
                levelIdentifier,
                LogicEntityLifecycleService.RequestedEntityCount,
                LogicEntityLifecycleService.BoundViewCount,
                LogicEntityLifecycleService.ActiveEntityCount,
                LogicEntityIdAllocator.LastAllocatedValue);
            await UniTask.Yield(PlayerLoopTiming.Update);

            m_RuntimeInitPipeline = new RuntimeInitPipeline(RuntimeInitLogTag, RuntimeLevelIdentifier, RequiredRuntimeSystems, false);
            m_RuntimeInitPipeline.Start(() =>
            {
                try
                {
                    if (previousLevel != null && previousLevel.Available)
                    {
                        GF.Entity.HideEntitySafe(previousLevel);
                    }

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
            LevelSelectionService.NotifyLevelLoadFailed(ex.Message);
            Log.Error("{0} Enter runtime level in place failed. level={1}, error={2}", RuntimeInitLogTag, levelIdentifier, ex);
        }
    }

    private void StartRuntimeInitPipeline(string levelIdentifier, bool showBuiltinProgress)
    {
        m_LogicFrameClockStarted = false;
        m_RuntimeInitPipeline = new RuntimeInitPipeline(RuntimeInitLogTag, levelIdentifier, RequiredRuntimeSystems, showBuiltinProgress);
        m_RuntimeInitPipeline.Start(OnRuntimeInitialized);
    }

    private void UpdateLogicFrames()
    {
        double realtime = Time.realtimeSinceStartupAsDouble;
        if (!m_LogicFrameClockStarted)
        {
            if (m_LogicTimelineRestartPending)
            {
                if (LogicReplayRuntime.IsRecording)
                {
                    LogicReplayRuntime.EndRecording();
                }
                LogicTimeControlService.ResetFrameTimelinePreservingPauses();
                LogicInteractionHoldService.ResetFrameTimeline();
                LogicInteractionCommandService.ResetFrameTimeline();
                LogicCardCommandService.ResetFrameTimeline();
                LogicCardPlacementAuthority.ResetFrameTimeline();
                LogicSkillSlotCommandService.ResetFrameTimeline();
                LogicPhaseCommandService.ResetFrameTimeline();
                LogicTechEffectCommandService.ResetFrameTimeline();
                LogicEntityLifecycleService.ResetFrameTimelinePreservingEntities();
                LogicObstacleCommandService.ResetFrameTimelinePreservingCommands();
                m_LogicTimelineRestartPending = false;
            }

            LogicFrameRuntime.ResetTimeline();
            m_LogicFrameClock.Start(realtime);
            InputManager inputManager = GameEntry.GetComponent<InputManager>();
            if (inputManager == null)
                throw new InvalidOperationException("RuntimeProcedureBase.UpdateLogicFrames failed: InputManager is null.");
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

        InputManager logicInputManager = GameEntry.GetComponent<InputManager>();
        if (logicInputManager == null)
            throw new InvalidOperationException("RuntimeProcedureBase.UpdateLogicFrames failed: InputManager is null.");

#if UNITY_EDITOR
        if (EditorLogicRuntimeStressGate.OwnsLogicClock)
        {
            UpdateEditorStressLogicFrames(logicInputManager);
            return;
        }
#endif

        int tickCount = m_LogicFrameClock.Advance(
            realtime,
            () =>
            {
                LogicTimeControlService.PrepareFrame(checked(m_LogicFrameClock.Frame + 1));
                return LogicTimeControlService.SchedulerScale;
            },
            (frame, cutoffRealtime) =>
            {
                LogicGameplayStateDigest gameplayDigest = ExecuteLogicFrame(
                    logicInputManager,
                    frame,
                    cutoffRealtime,
                    out LogicInputFrame inputFrame);
                if (LogicReplayRuntime.IsRecording)
                    LogicReplayRuntime.RecordFrame(inputFrame, gameplayDigest);
            });
        LogicFrameRuntime.CompleteRenderFrame(
            tickCount,
            m_LogicFrameClock.AccumulatorSeconds,
            m_LogicFrameClock.Interpolation);

        if (tickCount >= 3)
        {
            Log.Warning(
                "[LogicFrame] Catch-up executed. runtime={0}, ticks={1}, frame={2}, backlogSeconds={3:F6}, interpolation={4:F4}.",
                GetType().Name,
                tickCount,
                m_LogicFrameClock.Frame,
                m_LogicFrameClock.AccumulatorSeconds,
                m_LogicFrameClock.Interpolation);
        }

        if (m_LogicFrameClock.Frame >= m_NextLogicFrameStatusLogFrame)
        {
            Log.Info(
                "[LogicFrame] Status. runtime={0}, frame={1}, ticksThisRenderFrame={2}, backlogSeconds={3:F6}, interpolation={4:F4}, listeners={5}.",
                GetType().Name,
                m_LogicFrameClock.Frame,
                tickCount,
                m_LogicFrameClock.AccumulatorSeconds,
                m_LogicFrameClock.Interpolation,
                LogicFrameRuntime.ListenerCount);
            m_NextLogicFrameStatusLogFrame = m_LogicFrameClock.Frame + 300;
        }

#if UNITY_EDITOR
        if (EditorLogicRuntimeStressGate.Status == EditorLogicRuntimeStressGateStatus.Armed)
        {
            m_EditorStressCutoffRealtime = realtime;
            EditorLogicRuntimeStressGate.Activate(m_LogicFrameClock.Frame);
        }
#endif
    }

    private static LogicGameplayStateDigest ExecuteLogicFrame(
        InputManager logicInputManager,
        ulong frame,
        double cutoffRealtime,
        out LogicInputFrame inputFrame)
    {
        LogicTimeControlService.BeginFrame(frame);
        inputFrame = logicInputManager.SealLogicInputFrame(frame, cutoffRealtime);
        LogicInteractionHoldService.ProcessFrame(inputFrame);
        if (LogicCardPlacementAuthority.IsWorldBound)
        {
            LogicCardPlacementAuthority.ApplyFrame(frame);
        }
        else if (LogicCardRuntimeState.IsBound)
        {
            throw new InvalidOperationException("Card runtime is bound without a logic card-placement world.");
        }
        LogicCardCommandService.ApplyFrame(frame);
        LogicSkillSlotCommandService.ApplyFrame(frame);
        LogicPhaseCommandService.ApplyFrame(frame);
        CardSetup cardSetup = GameEntry.GetComponent<CardSetup>()
                              ?? throw new InvalidOperationException("RuntimeProcedureBase requires CardSetup for logic-frame card updates.");
        cardSetup.ApplyLogicFrame(frame);
        LogicInteractionCommandService.ApplyFrame(frame);
        LogicTechEffectCommandService.ApplyFrame(frame);
        DefendPhaseRuntime.ApplyScheduledSpawnRequests(frame);
        LogicEntityLifecycleService.ApplyFrame(frame);
        LogicObstacleCommandService.ApplyFrame(frame);
        LogicFrameRuntime.Tick(frame);
#if UNITY_EDITOR
        if (EditorLogicRuntimeStressGate.OwnsLogicClock && !EditorLogicRuntimeStressGate.ComputeFullHash)
            return LogicGameplayStateDigest.FromOpaqueHash(0);
#endif
        return LogicGameplayStateHasher.ComputeCurrentFrameDigest();
    }

#if UNITY_EDITOR
    private void UpdateEditorStressLogicFrames(InputManager logicInputManager)
    {
        if (EditorLogicRuntimeStressGate.Status != EditorLogicRuntimeStressGateStatus.Running)
        {
            LogicFrameRuntime.CompleteRenderFrame(
                0,
                m_LogicFrameClock.AccumulatorSeconds,
                m_LogicFrameClock.Interpolation);
            return;
        }

        int requestedTickCount = EditorLogicRuntimeStressGate.NextBatchTickCount;
        m_EditorStressCutoffRealtime += requestedTickCount * LogicFrameClock.FrameDurationSeconds;

        try
        {
            int actualTickCount = m_LogicFrameClock.Advance(
                m_EditorStressCutoffRealtime,
                () =>
                {
                    LogicTimeControlService.PrepareFrame(checked(m_LogicFrameClock.Frame + 1));
                    double schedulerScale = LogicTimeControlService.SchedulerScale;
                    if (schedulerScale != 1d)
                    {
                        throw new InvalidOperationException(
                            $"Editor logic stress gate requires scheduler scale 1. frame={m_LogicFrameClock.Frame + 1}, scale={schedulerScale:R}.");
                    }
                    return schedulerScale;
                },
                (frame, cutoffRealtime) =>
                {
                    EditorLogicRuntimeStressGate.PrepareInputFrame(frame, cutoffRealtime);
                    LogicGameplayStateDigest gameplayDigest = ExecuteLogicFrame(
                        logicInputManager,
                        frame,
                        cutoffRealtime,
                        out LogicInputFrame inputFrame);
                    EditorLogicRuntimeStressGate.RecordFrame(inputFrame, gameplayDigest);
                });

            if (actualTickCount != requestedTickCount)
            {
                throw new InvalidOperationException(
                    $"Editor logic stress gate tick mismatch. requested={requestedTickCount}, actual={actualTickCount}, frame={m_LogicFrameClock.Frame}.");
            }

            LogicFrameRuntime.CompleteRenderFrame(
                actualTickCount,
                m_LogicFrameClock.AccumulatorSeconds,
                m_LogicFrameClock.Interpolation);
        }
        catch (Exception exception)
        {
            EditorLogicRuntimeStressGate.Fail(exception);
            throw;
        }
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
    private readonly bool m_ShowBuiltinProgress;

    private GeneralSetup m_GeneralSetup;
    private Action m_OnCompleted;
    private bool m_IsStarted;
    private int m_MinimapUIFormId;
    private int m_InGameUIFormId;
    private float m_DisplayedProgress;
    private float m_TargetProgress;
    private bool m_FinishPending;
    private Stopwatch m_StartupStopwatch;

    public bool IsCompleted { get; private set; }

    public RuntimeInitPipeline(string logTag, string levelIdentifier, RuntimeInitSystemFlags runtimeSystems, bool showBuiltinProgress = true)
    {
        m_LogTag = string.IsNullOrWhiteSpace(logTag) ? "[RuntimeInit]" : logTag;
        m_LevelIdentifier = string.IsNullOrWhiteSpace(levelIdentifier) ? "Lv_1" : levelIdentifier;
        m_RuntimeSystems = runtimeSystems;
        m_ShowBuiltinProgress = showBuiltinProgress;
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
        m_StartupStopwatch = Stopwatch.StartNew();
        PhaseManager.CancelRuntimePhaseFlows();
        LogRuntimeInitTiming("cancel-phase-flows");
        LevelSelectionService.NotifyLevelLoadStarted();
        NotifyLevelLoadProgress();
        if (m_ShowBuiltinProgress)
        {
            GF.BuiltinView.ShowLoadingProgress(RuntimeProgressStart);
        }
        else
        {
            GF.BuiltinView.HideLoadingProgress();
        }

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
        if (m_ShowBuiltinProgress)
        {
            GF.BuiltinView.SetLoadingProgress(m_DisplayedProgress);
        }

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

        GF.BuiltinView.HideLoadingProgress();

        m_IsStarted = false;
        IsCompleted = false;
        m_OnCompleted = null;
        m_GeneralSetup = null;
        m_FinishPending = false;
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
        if (m_ShowBuiltinProgress)
        {
            GF.BuiltinView.SetLoadingProgress(1f);
        }
        m_OnCompleted?.Invoke();
        if (m_ShowBuiltinProgress)
        {
            GF.BuiltinView.HideLoadingProgress();
        }
        LevelSelectionService.NotifyLevelLoadCompleted();
        LogRuntimeInitTiming("startup-complete");
        Log.Info("{0} Runtime startup completed.", m_LogTag);
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
