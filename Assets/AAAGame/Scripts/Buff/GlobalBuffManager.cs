using System;
using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 全局 Buff 管理器：监听科技解锁事件，根据科技效果给单位施加全局 Buff。
/// 挂在场景 GameEntry 上，继承 GameFrameworkComponent。
/// </summary>
public class GlobalBuffManager : GameFrameworkComponent
{
    private TechScopeIndex m_TechScopeIndex;
    private TechScopeResolver m_TechScopeResolver;
    private bool m_IsSubscribed;

    public TechScopeResolver ScopeResolver => m_TechScopeResolver;

    protected void Start()
    {
        TryInitializeScopeResolver();
        TrySubscribeTechUnlockedEvent();
    }

    private void Update()
    {
        if (m_IsSubscribed)
            return;

        TryInitializeScopeResolver();
        TrySubscribeTechUnlockedEvent();
    }

    protected  void OnDestroy()
    {
        if (m_IsSubscribed && GF.Event != null)
            GF.Event.Unsubscribe(TechUnlockedEventArgs.EventId, OnTechUnlocked);

        m_IsSubscribed = false;
    }

    private void OnTechUnlocked(object sender, GameEventArgs e)
    {
        if (!TryInitializeScopeResolver())
            return;

        var args = (TechUnlockedEventArgs)e;
        var techData = TechDataModel.GetTechData(args.TechId);
        if (techData == null)
        {
            Debug.LogWarning($"[GlobalBuffManager] 找不到 TechData, techId={args.TechId}");
            return;
        }

        var resolvedScope = m_TechScopeResolver.Resolve(techData);
        Debug.Log(
            $"[GlobalBuffManager] 科技解锁: techId={args.TechId}, " +
            $"scopeType={techData.ScopeType}, " +
            $"characterKeys=[{string.Join(",", resolvedScope.CharacterKeys)}], " +
            $"unitTypes=[{string.Join(",", resolvedScope.UnitTypes)}]");

        // TODO: 根据 techId 查 TechData，读 UniqueValues，给所有/特定单位施加对应的全局 Buff
    }

    private bool TryInitializeScopeResolver()
    {
        if (m_TechScopeResolver != null)
            return true;

        try
        {
            m_TechScopeIndex = TechScopeIndex.CreateFromCurrentDataTables();
            m_TechScopeResolver = new TechScopeResolver(m_TechScopeIndex);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GlobalBuffManager] 初始化 TechScopeResolver 失败: {exception.Message}");
            return false;
        }
    }

    private bool TrySubscribeTechUnlockedEvent()
    {
        if (m_IsSubscribed)
            return true;

        if (GF.Event == null)
            return false;

        GF.Event.Subscribe(TechUnlockedEventArgs.EventId, OnTechUnlocked);
        m_IsSubscribed = true;
        return true;
    }
}
