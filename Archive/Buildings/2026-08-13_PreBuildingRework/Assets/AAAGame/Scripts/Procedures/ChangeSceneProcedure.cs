using GameFramework;
using GameFramework.Procedure;
using UnityGameFramework.Runtime;
using GameFramework.Fsm;
using GameFramework.Event;
using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine.SceneManagement;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class ChangeSceneProcedure : ProcedureBase
{
    private const float RuntimeSceneLoadProgressEnd = 0.85f;
    public static bool SuppressNextBuiltinLoadingProgress { get; set; }

    /// <summary>
    /// 编辑器工具可在运行前设置此字段，控制 "Game" 场景加载后切换到哪个 Procedure
    /// </summary>
    public static string SelectedProcedureForGame = GetDefaultProcedureName();

    /// <summary>
    /// 编辑器工具可在运行前设置此字段，控制 Preload 完成后要加载的场景名称
    /// </summary>
    public static string SelectedSceneForGame = GetDefaultSceneName();

    /// <summary>
    /// 编辑器工具可在运行前设置此字段，控制 RuntimeProcedure 初始化时使用的关卡标识
    /// </summary>
    public static string SelectedLevelIdentifier = GetDefaultLevelIdentifier();

    /// <summary>
    /// 场景→默认 Procedure 的映射，用于兼容性校验和回退
    /// </summary>
    private static readonly Dictionary<string, string> SceneDefaultProcedure = new Dictionary<string, string>
    {
        { "Game", "MenuProcedure" },
        {"Arena", "ArenaProcedure"}
    };

    /// <summary>
    /// 场景→允许的 Procedure 集合，用于兼容性校验
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> SceneCompatibleProcedures = new Dictionary<string, HashSet<string>>
    {
        { "Game", new HashSet<string> { "MenuProcedure", "GameProcedure", "CardGameProcedure" } },
        {"Arena", new HashSet<string>{"ArenaProcedure"}}
    };

    /// <summary>
    /// 已知的有效 Procedure 名称集合
    /// </summary>
    public static readonly HashSet<string> ValidProcedureNames = new HashSet<string>();

    private static Dictionary<string, Type> s_ProcedureTypes;

    static ChangeSceneProcedure()
    {
        RebuildProcedureTypeCache();
    }

    private static string GetDefaultProcedureName()
    {
        var settings = AppSettings.Instance;
        return settings != null && !string.IsNullOrWhiteSpace(settings.StartProcedureName)
            ? settings.StartProcedureName
            : "RealProcedure";
    }

    private static string GetDefaultSceneName()
    {
        var settings = AppSettings.Instance;
        return settings != null && !string.IsNullOrWhiteSpace(settings.StartSceneName)
            ? settings.StartSceneName
            : "Game";
    }

    private static string GetDefaultLevelIdentifier()
    {
        var settings = AppSettings.Instance;
        return settings != null && !string.IsNullOrWhiteSpace(settings.StartLevelIdentifier)
            ? settings.StartLevelIdentifier
            : "Lv_2";
    }

    private static void RebuildProcedureTypeCache()
    {
        s_ProcedureTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly =>
            {
                try
                {
                    return assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    return ex.Types.Where(t => t != null);
                }
            })
            .Where(type => type != null
                           && typeof(ProcedureBase).IsAssignableFrom(type)
                           && type.IsClass
                           && !type.IsAbstract)
            .GroupBy(type => type.Name)
            .ToDictionary(group => group.Key, group => group.First());

        ValidProcedureNames.Clear();
        foreach (var name in s_ProcedureTypes.Keys)
        {
            ValidProcedureNames.Add(name);
        }
    }

    private bool TryChangeStateByName(IFsm<IProcedureManager> procedureOwner, string procedureName)
    {
        if (s_ProcedureTypes == null || s_ProcedureTypes.Count == 0)
        {
            RebuildProcedureTypeCache();
        }

        if (!s_ProcedureTypes.TryGetValue(procedureName, out var targetType))
        {
            return false;
        }

        var method = typeof(ProcedureBase)
            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "ChangeState" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
        if (method == null)
        {
            return false;
        }

        var genericMethod = method.MakeGenericMethod(targetType);
        genericMethod.Invoke(this, new object[] { procedureOwner });
        return true;
    }

    private bool IsRuntimeProcedure(string procedureName)
    {
        if (string.IsNullOrEmpty(procedureName))
        {
            return false;
        }

        if (s_ProcedureTypes == null || s_ProcedureTypes.Count == 0)
        {
            RebuildProcedureTypeCache();
        }

        if (!s_ProcedureTypes.TryGetValue(procedureName, out var targetType))
        {
            return false;
        }

        return typeof(RuntimeProcedureBase).IsAssignableFrom(targetType);
    }

    /// <summary>
    /// 要加载的场景资源名,相对于场景目录
    /// </summary>
    internal const string P_SceneName = "SceneName";
    private bool loadSceneOver = false;
    private string nextScene = string.Empty;
    private string loadedSceneAssetName = string.Empty;
    private bool sceneLightingSynced;
    private bool keepLoadingForRuntimeInit;
    private bool runtimeInitFollowsSceneLoad;
    private bool showBuiltinLoadingProgress;
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {

        base.OnEnter(procedureOwner);
        showBuiltinLoadingProgress = !SuppressNextBuiltinLoadingProgress;
        SuppressNextBuiltinLoadingProgress = false;
        loadSceneOver = false;
        loadedSceneAssetName = string.Empty;
        sceneLightingSynced = false;
        keepLoadingForRuntimeInit = false;
        runtimeInitFollowsSceneLoad = false;
        if (showBuiltinLoadingProgress)
        {
            GF.BuiltinView.ShowLoadingProgress();
        }
        else
        {
            GF.BuiltinView.HideLoadingProgress();
        }
        GF.Event.Subscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
        GF.Event.Subscribe(LoadSceneFailureEventArgs.EventId, OnLoadSceneFailure);
        GF.Event.Subscribe(LoadSceneUpdateEventArgs.EventId, OnLoadSceneUpdate);
        GF.Event.Subscribe(ActiveSceneChangedEventArgs.EventId, OnActiveSceneChanged);
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
        runtimeInitFollowsSceneLoad = WillRuntimeInitFollowSceneLoad(nextScene);
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
        if (!sceneLightingSynced)
        {
            sceneLightingSynced = SyncLoadedSceneLighting(loadedSceneAssetName);
            if (!sceneLightingSynced)
            {
                return;
            }
        }

        string targetProcedure = SelectedProcedureForGame;
        if (SceneCompatibleProcedures.TryGetValue(nextScene, out var compatible))
        {
            if (!compatible.Contains(targetProcedure))
            {
                string fallback = SceneDefaultProcedure.ContainsKey(nextScene)
                    ? SceneDefaultProcedure[nextScene]
                    : "RealProcedure";
                Log.Warning("Procedure '{0}' 与场景 '{1}' 不兼容，回退到 '{2}'",
                    targetProcedure, nextScene, fallback);
                targetProcedure = fallback;
            }
        }

        // 根据 targetProcedure 切换到对应 Procedure
        keepLoadingForRuntimeInit = IsRuntimeProcedure(targetProcedure);
        if (!TryChangeStateByName(procedureOwner, targetProcedure))
        {
            Log.Warning("Procedure '{0}' 不存在，回退到 RealProcedure", targetProcedure);
            keepLoadingForRuntimeInit = IsRuntimeProcedure("RealProcedure");
            ChangeState<RealProcedure>(procedureOwner);
        }
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        if (showBuiltinLoadingProgress && !keepLoadingForRuntimeInit)
        {
            GF.BuiltinView.HideLoadingProgress();
        }
        GF.Event.Unsubscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
        GF.Event.Unsubscribe(LoadSceneFailureEventArgs.EventId, OnLoadSceneFailure);
        GF.Event.Unsubscribe(LoadSceneUpdateEventArgs.EventId, OnLoadSceneUpdate);
        GF.Event.Unsubscribe(ActiveSceneChangedEventArgs.EventId, OnActiveSceneChanged);
        base.OnLeave(procedureOwner, isShutdown);
    }
    private void OnLoadSceneUpdate(object sender, GameEventArgs e)
    {
        var arg = (LoadSceneUpdateEventArgs)e;
        if (arg.UserData != this)
        {
            return;
        }
        float progress = runtimeInitFollowsSceneLoad
            ? Mathf.Clamp01(arg.Progress) * RuntimeSceneLoadProgressEnd
            : arg.Progress;
        if (showBuiltinLoadingProgress)
        {
            GF.BuiltinView.SetLoadingProgress(progress);
        }
    }

    private void OnLoadSceneSuccess(object sender, GameEventArgs e)
    {
        var arg = (LoadSceneSuccessEventArgs)e;
        if (arg.UserData != this)
        {
            return;
        }
        loadedSceneAssetName = arg.SceneAssetName;
        if (runtimeInitFollowsSceneLoad)
        {
            if (showBuiltinLoadingProgress)
            {
                GF.BuiltinView.SetLoadingProgress(RuntimeSceneLoadProgressEnd);
            }
        }
        loadSceneOver = true;
    }
    //加载场景资源失败 重启游戏框架
    private bool SyncLoadedSceneLighting(string sceneAssetName)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!IsTargetScene(activeScene, sceneAssetName))
        {
            return false;
        }

        DynamicGI.UpdateEnvironment();
        return true;
    }

    private void OnActiveSceneChanged(object sender, GameEventArgs e)
    {
        var arg = (ActiveSceneChangedEventArgs)e;
        if (!loadSceneOver || sceneLightingSynced || !IsTargetScene(arg.ActiveScene, loadedSceneAssetName))
        {
            return;
        }

        sceneLightingSynced = SyncLoadedSceneLighting(loadedSceneAssetName);
    }

    private bool IsTargetScene(Scene scene, string sceneAssetName)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(sceneAssetName) && string.Equals(scene.path, sceneAssetName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(scene.name, nextScene, StringComparison.Ordinal);
    }

    private bool WillRuntimeInitFollowSceneLoad(string sceneName)
    {
        string targetProcedure = SelectedProcedureForGame;
        if (SceneCompatibleProcedures.TryGetValue(sceneName, out var compatible) && !compatible.Contains(targetProcedure))
        {
            targetProcedure = SceneDefaultProcedure.TryGetValue(sceneName, out var fallback)
                ? fallback
                : "RealProcedure";
        }

        return IsRuntimeProcedure(targetProcedure);
    }

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
