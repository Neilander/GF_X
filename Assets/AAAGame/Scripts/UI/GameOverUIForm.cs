using GameFramework;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class GameOverUIForm : UIFormBase
{
    public const string P_IsWin = "IsWin";

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        RefreshResultView();
        BindButtons();
        LevelSelectionService.LevelLoadCompleted += OnLevelLoadCompleted;
        UnlockPresentationService.ShowPending(transform);
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        LevelSelectionService.LevelLoadCompleted -= OnLevelLoadCompleted;
        UnbindButtons();
        base.OnClose(isShutdown, userData);
    }

    protected override void OnButtonClick(object sender, Button btSelf)
    {
        base.OnButtonClick(sender, btSelf);

        if (btSelf == varLvselectbtn)
        {
            LevelSwitchUIForm.Open(false);
        }
    }

    private void RefreshResultView()
    {
        bool isWin = Params.Get<VarBoolean>(P_IsWin);

        if (varShengli != null)
        {
            varShengli.SetActive(isWin);
        }

        if (varShibai != null)
        {
            varShibai.SetActive(!isWin);
        }
    }

    private void BindButtons()
    {
        if (varLvselectbtn == null)
        {
            return;
        }

        varLvselectbtn.onClick.RemoveListener(OnLevelSelectClicked);
        varLvselectbtn.onClick.AddListener(OnLevelSelectClicked);
    }

    private void UnbindButtons()
    {
        if (varLvselectbtn == null)
        {
            return;
        }

        varLvselectbtn.onClick.RemoveListener(OnLevelSelectClicked);
    }

    private void OnLevelSelectClicked()
    {
        ClickUIButton(varLvselectbtn);
    }

    private void OnLevelLoadCompleted()
    {
        GF.UI.Close(this.UIForm);
    }
}
