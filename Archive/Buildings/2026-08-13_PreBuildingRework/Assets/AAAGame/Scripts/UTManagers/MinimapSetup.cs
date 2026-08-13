using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.MiniMap;

/// <summary>
/// 小地图系统设置组件
/// 负责初始化、更新和关闭小地图系统
/// </summary>
public partial class MinimapSetup : GameFrameworkComponent
{
    private MinimapManager m_MinimapManager;
    private int m_MinimapUIFormId = -1;

    /// <summary>
    /// 初始化小地图系统
    /// </summary>
    public void MinimapSystemSetup()
    {
        InitializeMinimapSystem();
    }

    /// <summary>
    /// 更新小地图系统
    /// </summary>
    public void MinimapSystemUpdate()
    {
        // 小地图系统的更新由 MinimapManager 自动处理
        // 这里可以添加额外的逻辑
    }

    /// <summary>
    /// 关闭小地图系统
    /// </summary>
    public void MinimapSystemShutdown()
    {
        // 关闭小地图 UI
        if (m_MinimapUIFormId != -1)
        {
            GF.UI.CloseUIForm(m_MinimapUIFormId);
            m_MinimapUIFormId = -1;
        }

        // 清理小地图管理器
        if (m_MinimapManager != null)
        {
            // MinimapManager 会在场景卸载时自动清理
            m_MinimapManager = null;
        }

        Log.Info("[Minimap] 小地图系统已关闭");
    }

    /// <summary>
    /// 初始化小地图系统
    /// </summary>
    private void InitializeMinimapSystem()
    {
        // 从场景中查找 MinimapManager
        m_MinimapManager = GameEntry.GetComponent<MinimapManager>();
        
        if (m_MinimapManager == null)
        {
            Log.Error("[Minimap] 场景中未找到 MinimapManager！请确保 GameEntry 对象上挂载了 MinimapManager 组件。");
            return;
        }

        Log.Info("[Minimap] 小地图系统初始化完成");
    }

    /// <summary>
    /// 打开小地图 UI
    /// </summary>
    public void OpenMinimapUI()
    {
        if (m_MinimapManager == null)
        {
            Log.Error("[Minimap] MinimapManager 未初始化，无法打开 UI");
            return;
        }

        // 使用 GF.UI 打开 MinimapUI
        m_MinimapUIFormId = GF.UI.OpenUIForm(UIViews.MinimapUI);
        
        if (m_MinimapUIFormId == -1)
        {
            Log.Error("[Minimap] 打开小地图 UI 失败");
        }
        else
        {
            Log.Info("[Minimap] 小地图 UI 已打开");
        }
    }

    /// <summary>
    /// 获取小地图管理器
    /// </summary>
    public MinimapManager GetMinimapManager()
    {
        return m_MinimapManager;
    }
}
