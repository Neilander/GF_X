using System;
using AAAGame.MiniMap;
using Stopwatch = System.Diagnostics.Stopwatch;
using Cysharp.Threading.Tasks;
using GameFramework.Fsm;
using GameFramework.Procedure;
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

    protected virtual string RuntimeLevelIdentifier =>
        string.IsNullOrWhiteSpace(ChangeSceneProcedure.SelectedLevelIdentifier)
            ? "Lv_2"
            : ChangeSceneProcedure.SelectedLevelIdentifier;
    protected virtual string RuntimeInitLogTag => "[RuntimeInit]";
    protected virtual RuntimeInitSystemFlags RequiredRuntimeSystems => RuntimeInitSystemFlags.None;

    protected bool IsRuntimeReady => m_RuntimeInitPipeline != null && m_RuntimeInitPipeline.IsCompleted;

    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        m_ProcedureOwner = procedureOwner;

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

        OnRuntimeUpdate(elapseSeconds, realElapseSeconds);
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        FlowFieldCrowdMovementSystem.ForceEndRuntimeNavigationTransition();
        OnRuntimeShutdown();
        m_RuntimeInitPipeline?.Shutdown();
        m_RuntimeInitPipeline = null;
        m_ProcedureOwner = null;
        m_InPlaceLevelSwitchInProgress = false;
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
        try
        {
            ChangeSceneProcedure.SelectedLevelIdentifier = levelIdentifier;
            FlowFieldCrowdMovementSystem.BeginRuntimeNavigationTransition();
            navigationTransitionStarted = true;

            var inputManager = GameEntry.GetComponent<InputManager>();
            if (inputManager != null)
            {
                inputManager.ChangeState(InputState.UIForm);
            }

            GF.Base.PauseGame();
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.StopAllSfx();
            }

            PhaseManager.CancelRuntimePhaseFlows();
            m_RuntimeInitPipeline?.Shutdown();
            m_RuntimeInitPipeline = null;

            HideRuntimeEntitiesExceptLevel();
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

                    GF.Base.ResumeGame();
                    m_InPlaceLevelSwitchInProgress = false;
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
            GF.Base.ResumeGame();
            LevelSelectionService.NotifyLevelLoadFailed(ex.Message);
            Log.Error("{0} Enter runtime level in place failed. level={1}, error={2}", RuntimeInitLogTag, levelIdentifier, ex);
        }
    }

    private void StartRuntimeInitPipeline(string levelIdentifier, bool showBuiltinProgress)
    {
        m_RuntimeInitPipeline = new RuntimeInitPipeline(RuntimeInitLogTag, levelIdentifier, RequiredRuntimeSystems, showBuiltinProgress);
        m_RuntimeInitPipeline.Start(OnRuntimeInitialized);
    }

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
