using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.MiniMap.FOG;

/// <summary>
/// 战争迷雾系统设置组件
/// 负责初始化、更新和关闭战争迷雾系统
/// </summary>
public partial class FogOfWarSetup : GameFrameworkComponent
{
    private FogOfWarManager m_FogOfWarManager;
    private int m_FogOfWarUIFormId = -1;

    /// <summary>
    /// 初始化战争迷雾系统
    /// </summary>
    public void FogOfWarSystemSetup()
    {
        InitializeFogOfWarSystem();
    }

    /// <summary>
    /// 更新战争迷雾系统
    /// </summary>
    public void FogOfWarSystemUpdate()
    {
        // 战争迷雾系统的更新由 FogOfWarManager 自动处理
        // 这里可以添加额外的逻辑
    }

    /// <summary>
    /// 关闭战争迷雾系统
    /// </summary>
    public void FogOfWarSystemShutdown()
    {
        // 关闭战争迷雾 UI
        if (m_FogOfWarUIFormId != -1)
        {
            GF.UI.CloseUIForm(m_FogOfWarUIFormId);
            m_FogOfWarUIFormId = -1;
        }

        // 清理战争迷雾管理器
        if (m_FogOfWarManager != null)
        {
            m_FogOfWarManager.ResetFog();
            m_FogOfWarManager = null;
        }

        Log.Info("[FogOfWar] 战争迷雾系统已关闭");
    }

    /// <summary>
    /// 初始化战争迷雾系统
    /// </summary>
    private void InitializeFogOfWarSystem()
    {
        // 从场景中查找 FogOfWarManager
        m_FogOfWarManager = FindObjectOfType<FogOfWarManager>();
        
        if (m_FogOfWarManager == null)
        {
            Log.Error("[FogOfWar] 场景中未找到 FogOfWarManager！请在场景中创建一个空对象并挂载 FogOfWarManager 组件。");
            return;
        }

        // 设置默认玩家掩码（玩家0）
        m_FogOfWarManager.SetActivePlayerMask(1);

        Log.Info("[FogOfWar] 战争迷雾系统初始化完成");
    }

    /// <summary>
    /// 打开战争迷雾 UI
    /// </summary>
    public void OpenFogOfWarUI()
    {
        if (m_FogOfWarManager == null)
        {
            Log.Error("[FogOfWar] FogOfWarManager 未初始化，无法打开 UI");
            return;
        }

        // 使用 GF.UI 打开 FogOfWarUIForm
       // m_FogOfWarUIFormId = GF.UI.OpenUIForm(UIViews.FogOfWarUIForm);
        
        if (m_FogOfWarUIFormId == -1)
        {
            Log.Error("[FogOfWar] 打开战争迷雾 UI 失败");
        }
        else
        {
            Log.Info("[FogOfWar] 战争迷雾 UI 已打开");
        }
    }

    /// <summary>
    /// 获取战争迷雾管理器
    /// </summary>
    public FogOfWarManager GetFogOfWarManager()
    {
        return m_FogOfWarManager;
    }
}
