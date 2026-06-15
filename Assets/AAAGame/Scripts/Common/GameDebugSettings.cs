using UnityEngine;
using System.Diagnostics;
using System;
using Debug = UnityEngine.Debug;

/// <summary>
/// 全局 Debug 开关面板。挂在场景物体上，Inspector 里勾选对应模块即可看到日志。
/// 运行时通过 GameDebugSettings.Log(Category, msg) 输出。
/// </summary>
public class GameDebugSettings : MonoBehaviour
{
    public static GameDebugSettings Instance { get; private set; }
    public static event System.Action<bool> RuntimeResourceModifyEnabledChanged;
    private const bool ForceMovementDiagnostics = true;

    [Header("模块开关")]
    public bool targetDebug;
    public bool atkDebug;
    public bool moveDebug;
    public bool brainDebug;
    public bool groupMoveDebug;

    [Header("运行时调试")]
    public bool runtimeResourceModifyEnabled = true;
    private bool m_LastRuntimeResourceModifyEnabled;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        m_LastRuntimeResourceModifyEnabled = runtimeResourceModifyEnabled;
        DontDestroyOnLoad(gameObject);
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
            return;

        if (m_LastRuntimeResourceModifyEnabled == runtimeResourceModifyEnabled)
            return;

        m_LastRuntimeResourceModifyEnabled = runtimeResourceModifyEnabled;
        RuntimeResourceModifyEnabledChanged?.Invoke(runtimeResourceModifyEnabled);
    }

    /// <summary>
    /// 按模块输出日志。开关关闭时零开销（不拼接字符串）。
    /// </summary>
    public static bool IsEnabled(DebugCategory category)
    {
        if (ForceMovementDiagnostics
            && (category == DebugCategory.Move || category == DebugCategory.Brain || category == DebugCategory.GroupMove))
        {
            return true;
        }

        if (Instance == null) return false;
        return category switch
        {
            DebugCategory.Targeting => Instance.targetDebug,
            DebugCategory.Attack    => Instance.atkDebug,
            DebugCategory.Move      => Instance.moveDebug,
            DebugCategory.Brain     => Instance.brainDebug,
            DebugCategory.GroupMove => Instance.groupMoveDebug,
            _ => false
        };
    }

    public static void Log(DebugCategory category, string message)
    {
        if (!IsEnabled(category)) return;
        Debug.Log($"[{category}] {message}");
    }

    public static bool ShouldLogMovementForCharacter(string characterKey)
    {
        return !string.Equals(characterKey, "Unit_Hero", StringComparison.Ordinal);
    }

    public static bool IsRuntimeResourceModifyEnabled()
    {
#if UNITY_EDITOR
        return Instance != null && Instance.runtimeResourceModifyEnabled;
#else
        return false;
#endif
    }
}

public enum DebugCategory
{
    Targeting,
    Attack,
    Move,
    Brain,
    GroupMove
}
