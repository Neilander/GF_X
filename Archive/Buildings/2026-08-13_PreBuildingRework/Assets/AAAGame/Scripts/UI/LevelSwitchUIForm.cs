using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class LevelSwitchUIForm : UIFormBase
{
    public const string P_IsStartup = "IsStartup";

    private bool m_IsStartup;
    private bool m_IsLoading;
    private bool m_HoldsLogicPause;

    public static bool Open(bool isStartup)
    {
        if (GF.UI == null)
        {
            return false;
        }

        if (GF.UI.IsLoadingUIForm(UIViews.LevelSwitchUIForm) || GF.UI.HasUIForm(UIViews.LevelSwitchUIForm))
        {
            SetInputModeToUIForm();
            return true;
        }

        UIParams uiParams = UIParams.Create(!isStartup);
        uiParams.Set<VarBoolean>(P_IsStartup, isStartup);
        return GF.UI.OpenUIForm(UIViews.LevelSwitchUIForm, uiParams) != -1;
    }

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        m_IsStartup = TryGetStartupParam();
        m_IsLoading = LevelSelectionService.IsLevelLoading;
        PauseGameIfNeeded();
        SetInputModeToUIForm();

        BindButtons();
        LevelSelectionService.LevelLoadStarted += OnLevelLoadStarted;
        LevelSelectionService.LevelLoadProgressChanged += OnLevelLoadProgressChanged;
        LevelSelectionService.LevelRuntimeReadyForFirstFrame += OnLevelRuntimeReadyForFirstFrame;
        LevelSelectionService.LevelLoadCompleted += OnLevelLoadCompleted;
        LevelSelectionService.LevelLoadFailed += OnLevelLoadFailed;

        SetProgressVisible(m_IsLoading);
        SetProgress(0f);
        SetButtonsInteractable(!m_IsLoading);
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        LevelSelectionService.LevelLoadStarted -= OnLevelLoadStarted;
        LevelSelectionService.LevelLoadProgressChanged -= OnLevelLoadProgressChanged;
        LevelSelectionService.LevelRuntimeReadyForFirstFrame -= OnLevelRuntimeReadyForFirstFrame;
        LevelSelectionService.LevelLoadCompleted -= OnLevelLoadCompleted;
        LevelSelectionService.LevelLoadFailed -= OnLevelLoadFailed;
        UnbindButtons();
        RelinquishPauseOwnership(isShutdown);
        base.OnClose(isShutdown, userData);

        if (!isShutdown)
        {
            RefreshInputModeAfterCloseAsync().Forget();
        }
    }

    public override void OnClickClose()
    {
        if (m_IsStartup || m_IsLoading)
        {
            return;
        }

        base.OnClickClose();
    }

    protected override void OnOpenAnimationComplete()
    {
        base.OnOpenAnimationComplete();
        if (m_IsLoading)
        {
            Interactable = false;
        }
    }

    private bool TryGetStartupParam()
    {
        if (Params != null && Params.TryGet<VarBoolean>(P_IsStartup, out VarBoolean value))
        {
            return value;
        }

        return false;
    }

    private void PauseGameIfNeeded()
    {
        if (m_HoldsLogicPause || !LogicTimeControlService.IsActive)
        {
            return;
        }

        LogicTimeControlService.AcquirePause(LogicTimeControlSources.LevelSwitchUiPause);
        m_HoldsLogicPause = true;
    }

    private void RelinquishPauseOwnership(bool isShutdown)
    {
        if (!m_HoldsLogicPause)
        {
            return;
        }

        if (!isShutdown)
        {
            LogicTimeControlService.ReleasePause(LogicTimeControlSources.LevelSwitchUiPause);
        }

        m_HoldsLogicPause = false;
    }

    private void BindButtons()
    {
        EnsureButtonTargetGraphic(varAnjian);
        EnsureButtonTargetGraphic(varAnjian2);
        EnsureButtonTargetGraphic(varAnjian3);
        EnsureButtonTargetGraphic(varTestlv);
        BindButton(varAnjian, OnLevel1Clicked);
        BindButton(varAnjian2, OnLevel2Clicked);
        BindButton(varAnjian3, OnLevel3Clicked);
        BindButton(varTestlv, OnTestLevelClicked);
    }

    private void UnbindButtons()
    {
        UnbindButton(varAnjian, OnLevel1Clicked);
        UnbindButton(varAnjian2, OnLevel2Clicked);
        UnbindButton(varAnjian3, OnLevel3Clicked);
        UnbindButton(varTestlv, OnTestLevelClicked);
    }

    private static void BindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private static void UnbindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
    }

    private void OnLevel1Clicked()
    {
        LvEnterDialog.Open("Lv_1");
    }

    private void OnLevel2Clicked()
    {
        LvEnterDialog.Open("Lv_2");
    }

    private void OnLevel3Clicked()
    {
        LvEnterDialog.Open("Lv_3");
    }

    private void OnTestLevelClicked()
    {
        LvEnterDialog.Open(LevelSelectionService.TestLevelIdentifier);
    }

    private void OnLevelLoadStarted()
    {
        m_IsLoading = true;
        PauseGameIfNeeded();
        SetInputModeToUIForm();
        Interactable = false;
        SetButtonsInteractable(false);
        SetProgressVisible(true);
        SetProgress(0f);
    }

    private void OnLevelLoadProgressChanged(float progress)
    {
        if (!m_IsLoading)
        {
            return;
        }

        SetProgress(progress);
    }

    private void OnLevelRuntimeReadyForFirstFrame()
    {
        if (!m_IsLoading)
            throw new System.InvalidOperationException("Level switch UI received runtime-ready outside a level load.");

        RelinquishPauseOwnership(false);
    }

    private void OnLevelLoadCompleted()
    {
        m_IsLoading = false;
        SetProgress(1f);
        SetProgressVisible(false);
        GF.UI.Close(this.UIForm);
    }

    private void OnLevelLoadFailed(string errorMessage)
    {
        m_IsLoading = false;
        PauseGameIfNeeded();
        Interactable = true;
        SetButtonsInteractable(true);
        SetProgressVisible(false);
        Log.Warning("[LevelSwitchUIForm] Level load failed: {0}", errorMessage);
    }

    private void SetButtonsInteractable(bool interactable)
    {
        SetButtonInteractable(varAnjian, interactable);
        SetButtonInteractable(varAnjian2, interactable);
        SetButtonInteractable(varAnjian3, interactable);
        SetButtonInteractable(varTestlv, interactable);
    }

    private static void SetButtonInteractable(Button button, bool interactable)
    {
        if (button != null)
        {
            button.interactable = interactable;
        }
    }

    private void SetProgressVisible(bool visible)
    {
        if (varProgressGO != null)
        {
            varProgressGO.SetActive(visible);
        }
    }

    private void SetProgress(float progress)
    {
        if (varProgressImage != null)
        {
            varProgressImage.fillAmount = Mathf.Clamp01(progress);
        }
    }

    private static void SetInputModeToUIForm()
    {
        InputManager inputManager = GameEntry.GetComponent<InputManager>();
        if (inputManager != null && inputManager.CurState != InputState.UIForm)
        {
            inputManager.ChangeState(InputState.UIForm);
        }
    }

    private static void EnsureButtonTargetGraphic(Button button)
    {
        if (button == null)
        {
            return;
        }

        Graphic graphic = button.GetComponent<Graphic>();
        if (graphic != null)
        {
            button.targetGraphic = graphic;
        }
    }

    private static async UniTaskVoid RefreshInputModeAfterCloseAsync()
    {
        await UniTask.Yield(PlayerLoopTiming.Update);

        InputManager inputManager = GameEntry.GetComponent<InputManager>();
        if (inputManager != null)
        {
            inputManager.RefreshUIFormInputState();
        }
    }
}
