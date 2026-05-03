using System;
using AAAGame.MiniMap;
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
    private RuntimeInitPipeline m_RuntimeInitPipeline;

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
        m_RuntimeInitPipeline = new RuntimeInitPipeline(RuntimeInitLogTag, RuntimeLevelIdentifier, RequiredRuntimeSystems);
        m_RuntimeInitPipeline.Start(OnRuntimeInitialized);
    }

    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
        if (!IsRuntimeReady)
        {
            return;
        }

        OnRuntimeUpdate(elapseSeconds, realElapseSeconds);
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        OnRuntimeShutdown();
        m_RuntimeInitPipeline?.Shutdown();
        m_RuntimeInitPipeline = null;
        base.OnLeave(procedureOwner, isShutdown);
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
    private readonly string m_LogTag;
    private readonly string m_LevelIdentifier;
    private readonly RuntimeInitSystemFlags m_RuntimeSystems;

    private GeneralSetup m_GeneralSetup;
    private Action m_OnCompleted;
    private bool m_IsStarted;
    private int m_MinimapUIFormId;
    private int m_InGameUIFormId;

    public bool IsCompleted { get; private set; }

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

        m_GeneralSetup = GameEntry.GetComponent<GeneralSetup>();
        if (m_GeneralSetup == null)
        {
            Log.Error("{0} Missing GeneralSetup component on GameEntry.", m_LogTag);
            CompleteStartup();
            return;
        }

        m_GeneralSetup.OnGeneralSetupCompleted += HandleGeneralSetupCompleted;

        GF.BuiltinView.ShowLoadingProgress();
        GF.BuiltinView.SetLoadingProgress(0.05f);

        Log.Info("{0} Runtime startup begin. level={1}, systems={2}", m_LogTag, m_LevelIdentifier, m_RuntimeSystems);

        if (m_GeneralSetup.IsGeneralSetupCompleted)
        {
            HandleGeneralSetupCompleted();
            return;
        }

        m_GeneralSetup.GeneralSystemSetup(m_LevelIdentifier);
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
    }

    private void HandleGeneralSetupCompleted()
    {
        if (IsCompleted)
        {
            return;
        }

        InitializeRuntimeManagers();
        CompleteStartup();
    }

    private void InitializeRuntimeManagers()
    {
        GF.BuiltinView.SetLoadingProgress(0.85f);

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
                if (GF.UI.IsLoadingUIForm(UIViews.MinimapUI) || GF.UI.HasUIForm(UIViews.MinimapUI))
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
            GF.UI.CloseUIForm(m_MinimapUIFormId);
            m_MinimapUIFormId = -1;
        }

        if (m_InGameUIFormId != -1)
        {
            GF.UI.CloseUIForm(m_InGameUIFormId);
            m_InGameUIFormId = -1;
        }

        if (needCardSystem)
        {
            var cardSetup = GameEntry.GetComponent<CardSetup>();
            if (cardSetup != null)
            {
                cardSetup.CardSystemShutdown();
            }
        }
    }

    private void CompleteStartup()
    {
        IsCompleted = true;
        GF.BuiltinView.SetLoadingProgress(1f);
        GF.BuiltinView.HideLoadingProgress();
        Log.Info("{0} Runtime startup completed.", m_LogTag);
        m_OnCompleted?.Invoke();
        ShowLevelObjectiveTips();
        EnablePlayerInput();
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
}
