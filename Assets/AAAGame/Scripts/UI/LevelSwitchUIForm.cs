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
        SetInputModeToUIForm();

        BindButtons();
        LevelSelectionService.LevelLoadStarted += OnLevelLoadStarted;
        LevelSelectionService.LevelLoadProgressChanged += OnLevelLoadProgressChanged;
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
        LevelSelectionService.LevelLoadCompleted -= OnLevelLoadCompleted;
        LevelSelectionService.LevelLoadFailed -= OnLevelLoadFailed;
        UnbindButtons();
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

    private void BindButtons()
    {
        EnsureButtonTargetGraphic(varAnjian);
        EnsureButtonTargetGraphic(varAnjian2);
        EnsureButtonTargetGraphic(varAnjian3);
        BindButton(varAnjian, OnLevel1Clicked);
        BindButton(varAnjian2, OnLevel2Clicked);
        BindButton(varAnjian3, OnLevel3Clicked);
    }

    private void UnbindButtons()
    {
        UnbindButton(varAnjian, OnLevel1Clicked);
        UnbindButton(varAnjian2, OnLevel2Clicked);
        UnbindButton(varAnjian3, OnLevel3Clicked);
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
        TryLoadLevel(1);
    }

    private void OnLevel2Clicked()
    {
        TryLoadLevel(2);
    }

    private void OnLevel3Clicked()
    {
        TryLoadLevel(3);
    }

    private void TryLoadLevel(int levelNumber)
    {
        if (m_IsLoading)
        {
            return;
        }

        m_IsLoading = true;
        SetInputModeToUIForm();
        Interactable = false;
        SetButtonsInteractable(false);
        SetProgressVisible(true);
        SetProgress(0f);

        if (!LevelSelectionService.TryEnterLevelInPlaceByNumber(levelNumber, out string errorMessage))
        {
            m_IsLoading = false;
            Interactable = true;
            SetButtonsInteractable(true);
            SetProgressVisible(false);
            Log.Warning("[LevelSwitchUIForm] Failed to load level {0}: {1}", levelNumber, errorMessage);
        }
    }

    private void OnLevelLoadStarted()
    {
        m_IsLoading = true;
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
