using System.Collections.Generic;
using AAAGame.MiniMap;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 小兵实体：使用 DirectAtkComp（直接选定目标造成伤害，不走攻击盒）。
/// 适合大量小兵的战斗场景。
/// </summary>
public partial class SoldierEntity : MAEntity
{
    /// <summary>
    /// AI类型
    /// </summary>
    public BrainType BrainType { get; private set; }
    public string SourceStrongholdId { get; private set; }
    private MinimapReportComponent m_MinimapReportComponent;

    public override void ChangeSide(SideType newSide)
    {
        base.ChangeSide(newSide);
        if (m_MinimapReportComponent != null)
        {
            m_MinimapReportComponent.SetSide(newSide);
        }
    }

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        m_MinimapReportComponent = gameObject.AddComponent<MinimapReportComponent>();
    }

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        if (userData is EntityParams ep)
        {
            Side = ep.Side;
            BrainType = ep.BrainType; // 设置AI类型
            SourceStrongholdId = ep.GetString(EntityParams.P_SourceStrongholdId);
        }

        //Debug.LogError("什么玩意");

        if (m_MinimapReportComponent != null)
        {
            m_MinimapReportComponent.Initialize(Side);
        }

    }

    protected override void OnRenderFrameUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnRenderFrameUpdate(elapseSeconds, realElapseSeconds);
        long stageStartTicks;

        if (m_MinimapReportComponent != null)
        {
            stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            m_MinimapReportComponent.Tick();
            RecordSoldierPerf(UnityGameFramework.Runtime.MainThreadPerfScope.SoldierMinimap, stageStartTicks);
        }

        // Debug: 绿线=导航方向, 红线=到目标直线
        if (targetComp?.CurrentTarget != null && targetComp.CurrentTarget.Alive)
        {
            stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            var target = targetComp.CurrentTarget;
            Vector3 pos = Position + Vector3.up * 0.5f;
            // 红线：到目标的直线
            Debug.DrawLine(pos, target.Position + Vector3.up * 0.5f, Color.red);
            // 绿线：当前导航计算的方向
            if (moveComp != null)
            {
                FixVector2 navDirFixed = moveComp.NavDirectionFixed;
                Vector3 navDir = new Vector3((float)navDirFixed.x, 0f, (float)navDirFixed.y);
                Debug.DrawRay(pos, navDir * 3f, Color.green);
            }
            RecordSoldierPerf(UnityGameFramework.Runtime.MainThreadPerfScope.SoldierDebugDraw, stageStartTicks);
        }

    }

    private static void RecordSoldierPerf(UnityGameFramework.Runtime.MainThreadPerfScope scope, long startTicks)
    {
        UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
            scope,
            System.Diagnostics.Stopwatch.GetTimestamp() - startTicks);
    }

    /// <summary>
    /// 获取单位类型（重写基类方法）
    /// </summary>
    /*
    protected override string GetUnitType()
    {
        return _unitIndex;
    }*/

    protected override void OnHide(bool isShutdown, object userData)
    {
        base.OnHide(isShutdown, userData);
    }

}
