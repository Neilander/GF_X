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
        { "CharacterAndSkillTestScene", "CharacterTestProcedure" },
        {"UI_Card","CardGameProcedure"},
        {"Arena", "ArenaProcedure"}
    };

    /// <summary>
    /// 场景→允许的 Procedure 集合，用于兼容性校验
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> SceneCompatibleProcedures = new Dictionary<string, HashSet<string>>
    {
        { "Game", new HashSet<string> { "MenuProcedure", "GameProcedure", "CharacterTestProcedure" , "SampleProcedure", "RangedWeaponTestProcedure", "BuffTestProcedure","CardGameProcedure"} }, // 新增：远程武器测试流程和Buff测试流程
        { "LevelTestScene", new HashSet<string> { "LevelTestProcedure", "CharacterTestProcedure", "BuffTestProcedure" } },
        { "CharacterAndSkillTestScene", new HashSet<string> { "CharacterTestProcedure", "BuffTestProcedure" } },
        {"Arena", new HashSet<string>{"ArenaProcedure"}}
    };

    /// <summary>
    /// 已知的有效 Procedure 名称集合
    /// </summary>
    public static readonly HashSet<string> ValidProcedureNames = new HashSet<string>
    {
        "CharacterTestProcedure", "MenuProcedure", "GameProcedure", "LevelTestProcedure", "SampleProcedure", "RangedWeaponTestProcedure", "BuffTestProcedure" // 新增：远程武器测试流程和Buff测试流程
        ,"CardGameProcedure", "ArenaProcedure"
    };
    
    // 确保BuffTestProcedure被编译到程序集中
    private static System.Type _buffTestProcedureType = typeof(BuffTestProcedure);

    /// <summary>
    /// 要加载的场景资源名,相对于场景目录
    /// </summary>
    internal const string P_SceneName = "SceneName";
    private bool loadSceneOver = false;
    private string nextScene = string.Empty;
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        Debug.Log("ChangeSceneProcedure.OnEnter开始");
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
        Debug.Log("准备加载场景: " + nextScene);
        GF.Scene.LoadScene(UtilityBuiltin.AssetsPath.GetScenePath(nextScene), this);
    }

    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
        Debug.Log("ChangeSceneProcedure.OnUpdate - loadSceneOver: " + loadSceneOver);
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
        Debug.Log("准备切换到Procedure: " + targetProcedure);
        Debug.Log("SelectedProcedureForGame: " + SelectedProcedureForGame);
        
        switch (targetProcedure)
        {
            case "MenuProcedure":
                Debug.Log("切换到MenuProcedure");
                ChangeState<MenuProcedure>(procedureOwner);
                break;
            case "GameProcedure":
                Debug.Log("切换到GameProcedure");
                ChangeState<GameProcedure>(procedureOwner);
                break;
            case "LevelTestProcedure":
                Debug.Log("切换到LevelTestProcedure");
                ChangeState<LevelTestProcedure>(procedureOwner);
                break;
            case "SampleProcedure":
                Debug.Log("切换到SampleProcedure");
                ChangeState<SampleProcedure>(procedureOwner);
                break;
            case "RangedWeaponTestProcedure": // 新增：远程武器测试流程
                Debug.Log("切换到RangedWeaponTestProcedure");
                ChangeState<RangedWeaponTestProcedure>(procedureOwner);
                break;
            
            case "BuffTestProcedure":
                Debug.Log("切换到BuffTestProcedure");
                try
                {
                    // 检查BuffTestProcedure类型是否存在
                    System.Type buffTestType = typeof(BuffTestProcedure);
                    Debug.Log("BuffTestProcedure类型存在: " + (buffTestType != null));
                    
                    Debug.Log("准备调用ChangeState...");
                    ChangeState<BuffTestProcedure>(procedureOwner);
                    Debug.Log("成功切换到BuffTestProcedure");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("切换到BuffTestProcedure失败: " + ex.Message);
                    Debug.LogError("堆栈跟踪: " + ex.StackTrace);
                    Debug.LogError("异常类型: " + ex.GetType().Name);
                    
                    // 检查是否是类型不存在的错误
                    if (ex.Message.Contains("not exist") || ex.Message.Contains("不存在"))
                    {
                        Debug.LogError("BuffTestProcedure类型可能不存在或编译错误");
                    }
                }
                break;
                
            case "TestProcedure":
                Debug.Log("切换到TestProcedure");
                try
                {
                    ChangeState<TestProcedure>(procedureOwner);
                    Debug.Log("成功切换到TestProcedure");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("切换到TestProcedure失败: " + ex.Message);
                }
                break;
            case "CardGameProcedure":
                ChangeState<CardGameProcedure>(procedureOwner);
                break;
            
            case "ArenaProcedure":
                ChangeState<ArenaProcedure>(procedureOwner);
                break;
            
            default:
                Debug.Log("切换到默认的CharacterTestProcedure");
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
        Debug.Log("场景加载进度: " + arg.Progress + ", " + arg.SceneAssetName);
        GF.BuiltinView.SetLoadingProgress(arg.Progress);
    }

    private void OnLoadSceneSuccess(object sender, GameEventArgs e)
    {
        var arg = (LoadSceneSuccessEventArgs)e;
        if (arg.UserData != this)
        {
            return;
        }
        Debug.Log("场景加载成功: " + arg.SceneAssetName);
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
