using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 完整游戏流程
/// 同时加载卡牌系统、小地图系统和战争迷雾系统
/// 这是推荐的游戏主流程
/// </summary>
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class CompleteGameProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        
        Log.Info("[CompleteGame] 进入完整游戏流程");

        // 1. 初始化通用系统
        GameEntry.GetComponent<GeneralSetup>().GeneralSystemSetup();

        // 2. 初始化卡牌系统
        GameEntry.GetComponent<CardSetup>().CardSystemSetup();
        GameEntry.GetComponent<CardSetup>().OpenCardUI();

        // 3. 初始化小地图系统
        GameEntry.GetComponent<MinimapSetup>().MinimapSystemSetup();
        GameEntry.GetComponent<MinimapSetup>().OpenMinimapUI();



        Log.Info("[CompleteGame] 所有系统初始化完成");
    }

    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
        
        // 更新所有系统
        GameEntry.GetComponent<CardSetup>().CardSystemUpdate();
        GameEntry.GetComponent<MinimapSetup>().MinimapSystemUpdate();
    
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        base.OnLeave(procedureOwner, isShutdown);
        
        Log.Info("[CompleteGame] 离开完整游戏流程");

        // 按相反顺序关闭系统
        
        GameEntry.GetComponent<MinimapSetup>().MinimapSystemShutdown();
        GameEntry.GetComponent<CardSetup>().CardSystemShutdown();
        GameEntry.GetComponent<GeneralSetup>().GeneralSystemShutDown();

        Log.Info("[CompleteGame] 所有系统已关闭");
    }
}
