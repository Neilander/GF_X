using GameFramework;
using GameFramework.Procedure;
using UnityGameFramework.Runtime;
using GameFramework.Fsm;
using GameFramework.Event;
using UnityEngine;
using System.Collections.Generic;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class ChangeSceneProcedure : ProcedureBase
{
    /// <summary>
    /// 编辑器工具可在运行前设置此字段，控制 "Game" 场景加载后切换到哪个 Procedure
    /// </summary>
    public static string SelectedProcedureForGame = "CharacterTestProcedure";

    /// <summary>
    /// 编辑器工具可在运行前设置此字段，控制 Preload 完成后要加载的场景名称
    /// </summary>
    public static string SelectedSceneForGame = "Game";

    /// <summary>
    /// 场景→默认 Procedure 的映射，用于兼容性校验和回退
    /// </summary>
    private static readonly Dictionary<string, string> SceneDefaultProcedure = new Dictionary<string, string>
    {
        { "Game", "MenuProcedure" },
        { "LevelTestScene", "LevelTestProcedure" },
        { "CharacterAndSkillTestScene", "CharacterTestProcedure" }
    };

    /// <summary>
    /// 场景→允许的 Procedure 集合，用于兼容性校验
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> SceneCompatibleProcedures = new Dictionary<string, HashSet<string>>
    {
        { "Game", new HashSet<string> { "MenuProcedure", "GameProcedure", "CharacterTestProcedure" , "SampleProcedure"} },
        { "LevelTestScene", new HashSet<string> { "LevelTestProcedure", "CharacterTestProcedure" } },
        { "CharacterAndSkillTestScene", new HashSet<string> { "CharacterTestProcedure" } }
    };

    /// <summary>
    /// 已知的有效 Procedure 名称集合
    /// </summary>
    public static readonly HashSet<string> ValidProcedureNames = new HashSet<string>
    {
        "CharacterTestProcedure", "MenuProcedure", "GameProcedure", "LevelTestProcedure", "SampleProcedure"
    };

    /// <summary>
    /// 要加载的场景资源名,相对于场景目录
    /// </summary>
    internal const string P_SceneName = "SceneName";
    private bool loadSceneOver = false;
    private string nextScene = string.Empty;
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        loadSceneOver = false;
        GF.BuiltinView.ShowLoadingProgress();
        GF.Event.Subscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
        GF.Event.Subscribe(LoadSceneFailureEventArgs.EventId, OnLoadSceneFailure);
        GF.Event.Subscribe(LoadSceneUpdateEventArgs.EventId, OnLoadSceneUpdate);
        // 停止所有声音
        GF.Sound.StopAllLoadingSounds();
        GF.Sound.StopAllLoadedSounds();

        // 隐藏所有实体
        GF.Entity.HideAllLoadingEntities();
        GF.Entity.HideAllLoadedEntities();

        // 卸载所有场景
        string[] loadedSceneAssetNames = GF.Scene.GetLoadedSceneAssetNames();
        for (int i = 0; i < loadedSceneAssetNames.Length; i++)
        {
            GF.Scene.UnloadScene(loadedSceneAssetNames[i]);
        }

        // 还原游戏速度
        GF.Base.ResetNormalGameSpeed();

        if (!procedureOwner.HasData(P_SceneName))
        {
            throw new GameFrameworkException("未设置要加载的场景资源名!");
        }
        nextScene = procedureOwner.GetData<VarString>(P_SceneName);
        procedureOwner.RemoveData(P_SceneName);
        GF.Scene.LoadScene(UtilityBuiltin.AssetsPath.GetScenePath(nextScene), this);
    }

    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
        if (!loadSceneOver)
        {
            return;
        }

        // 场景-Procedure 兼容性校验：不兼容时回退到场景默认 Procedure
        string targetProcedure = SelectedProcedureForGame;
        if (SceneCompatibleProcedures.TryGetValue(nextScene, out var compatible))
        {
            if (!compatible.Contains(targetProcedure))
            {
                string fallback = SceneDefaultProcedure.ContainsKey(nextScene)
                    ? SceneDefaultProcedure[nextScene]
                    : "CharacterTestProcedure";
                Log.Warning("Procedure '{0}' 与场景 '{1}' 不兼容，回退到 '{2}'",
                    targetProcedure, nextScene, fallback);
                targetProcedure = fallback;
            }
        }

        // 根据 targetProcedure 切换到对应 Procedure
        switch (targetProcedure)
        {
            case "MenuProcedure":
                ChangeState<MenuProcedure>(procedureOwner);
                break;
            case "GameProcedure":
                ChangeState<GameProcedure>(procedureOwner);
                break;
            case "LevelTestProcedure":
                ChangeState<LevelTestProcedure>(procedureOwner);
                break;
            case "SampleProcedure":
                ChangeState<SampleProcedure>(procedureOwner);
                break;
            
            default:
                ChangeState<CharacterTestProcedure>(procedureOwner);
                break;
        }
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        GF.BuiltinView.HideLoadingProgress();
        GF.Event.Unsubscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
        GF.Event.Unsubscribe(LoadSceneFailureEventArgs.EventId, OnLoadSceneFailure);
        GF.Event.Unsubscribe(LoadSceneUpdateEventArgs.EventId, OnLoadSceneUpdate);
        base.OnLeave(procedureOwner, isShutdown);
    }
    private void OnLoadSceneUpdate(object sender, GameEventArgs e)
    {
        var arg = (LoadSceneUpdateEventArgs)e;
        if (arg.UserData != this)
        {
            return;
        }
        //Log.Info("场景加载进度:{0}, {1}", arg.Progress, arg.SceneAssetName);
        GF.BuiltinView.SetLoadingProgress(arg.Progress);
    }

    private void OnLoadSceneSuccess(object sender, GameEventArgs e)
    {
        var arg = (LoadSceneSuccessEventArgs)e;
        if (arg.UserData != this)
        {
            return;
        }
        //Log.Info("场景加载成功:{0}", arg.SceneAssetName);
        loadSceneOver = true;
    }
    //加载场景资源失败 重启游戏框架
    private void OnLoadSceneFailure(object sender, GameEventArgs e)
    {
        var arg = (LoadSceneFailureEventArgs)e;
        if (arg.UserData != this)
        {
            return;
        }

        Log.Error("加载场景失败,自动重启框架！", arg.SceneAssetName);
        GameEntry.Shutdown(ShutdownType.Restart);
    }
}
