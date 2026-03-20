using UnityEngine;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

/// <summary>
/// 全局 Debug 开关面板。挂在场景物体上，Inspector 里勾选对应模块即可看到日志。
/// 运行时通过 GameDebugSettings.Log(Category, msg) 输出。
/// </summary>
public class GameDebugSettings : MonoBehaviour
{
    public static GameDebugSettings Instance { get; private set; }

    [Header("模块开关")]
    public bool targetDebug;
    public bool atkDebug;
    public bool moveDebug;
    public bool brainDebug;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// 按模块输出日志。开关关闭时零开销（不拼接字符串）。
    /// </summary>
    public static bool IsEnabled(DebugCategory category)
    {
        if (Instance == null) return false;
        return category switch
        {
            DebugCategory.Targeting => Instance.targetDebug,
            DebugCategory.Attack    => Instance.atkDebug,
            DebugCategory.Move      => Instance.moveDebug,
            DebugCategory.Brain     => Instance.brainDebug,
            _ => false
        };
    }

    public static void Log(DebugCategory category, string message)
    {
        if (!IsEnabled(category)) return;
        Debug.Log($"[{category}] {message}");
    }
}

public enum DebugCategory
{
    Targeting,
    Attack,
    Move,
    Brain
}
