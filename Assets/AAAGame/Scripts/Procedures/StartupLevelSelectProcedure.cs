using GameFramework;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityGameFramework.Runtime;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class StartupLevelSelectProcedure : ProcedureBase
{
    private static StartupLevelSelectProcedure s_Current;

    private IFsm<IProcedureManager> m_ProcedureOwner;

    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        s_Current = this;
        m_ProcedureOwner = procedureOwner;
        GF.BuiltinView.HideLoadingProgress();

        if (!LevelSelectionService.OpenLevelSwitch(true))
        {
            Log.Error("[StartupLevelSelect] Failed to open level switch UI.");
        }
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        if (s_Current == this)
        {
            s_Current = null;
        }

        m_ProcedureOwner = null;
        base.OnLeave(procedureOwner, isShutdown);
    }

    public static bool TryEnterLevelByNumber(int levelNumber, out string errorMessage)
    {
        return TryEnterLevel(Utility.Text.Format("Lv_{0}", levelNumber), out errorMessage);
    }

    public static bool TryEnterLevel(string levelIdentifier, out string errorMessage)
    {
        errorMessage = null;
        if (s_Current == null || s_Current.m_ProcedureOwner == null)
        {
            errorMessage = "Startup level select procedure is not active.";
            return false;
        }

        if (!LevelSelectionService.TrySelectLevel(levelIdentifier, out LevelSelectionEntry selectedLevel, out errorMessage))
        {
            return false;
        }

        string sceneName = !string.IsNullOrWhiteSpace(ChangeSceneProcedure.SelectedSceneForGame)
            ? ChangeSceneProcedure.SelectedSceneForGame
            : AppSettings.Instance.StartSceneName;
        string procedureName = !string.IsNullOrWhiteSpace(ChangeSceneProcedure.SelectedProcedureForGame)
            ? ChangeSceneProcedure.SelectedProcedureForGame
            : AppSettings.Instance.StartProcedureName;

        LevelSelectionService.ConsumeStartupLevelSwitch();
        ChangeSceneProcedure.SelectedLevelIdentifier = selectedLevel.Identifier;
        ChangeSceneProcedure.SelectedSceneForGame = sceneName;
        ChangeSceneProcedure.SelectedProcedureForGame = procedureName;
        ChangeSceneProcedure.SuppressNextBuiltinLoadingProgress = true;
        RuntimeProcedureBase.SuppressNextBuiltinLoadingProgress = true;

        s_Current.m_ProcedureOwner.SetData<VarString>(ChangeSceneProcedure.P_SceneName, sceneName);
        s_Current.ChangeState<ChangeSceneProcedure>(s_Current.m_ProcedureOwner);
        Log.Info("[StartupLevelSelect] Enter level requested. level={0}", selectedLevel.Identifier);
        return true;
    }
}
