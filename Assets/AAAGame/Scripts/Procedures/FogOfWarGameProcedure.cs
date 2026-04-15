using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 战争迷雾游戏流程
/// 在 Game 场景加载完成后进入此流程，显示战争迷雾 UI
/// 可以与 CardGameProcedure 结合使用
/// </summary>
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class FogOfWarGameProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        
        Log.Info("[FogOfWar] 进入战争迷雾游戏流程");

        // 初始化通用系统
        GameEntry.GetComponent<GeneralSetup>().GeneralSystemSetup();

        // 初始化卡牌系统（如果需要）
        GameEntry.GetComponent<CardSetup>().CardSystemSetup();
        GameEntry.GetComponent<CardSetup>().OpenCardUI();

        // 初始化战争迷雾系统
        GameEntry.GetComponent<FogOfWarSetup>().FogOfWarSystemSetup();
        GameEntry.GetComponent<FogOfWarSetup>().OpenFogOfWarUI();
    }

    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
        
        // 更新卡牌系统
        GameEntry.GetComponent<CardSetup>().CardSystemUpdate();
        
        // 更新战争迷雾系统
        GameEntry.GetComponent<FogOfWarSetup>().FogOfWarSystemUpdate();
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        base.OnLeave(procedureOwner, isShutdown);
        
        // 关闭战争迷雾系统
        GameEntry.GetComponent<FogOfWarSetup>().FogOfWarSystemShutdown();
        
        // 关闭卡牌系统
        GameEntry.GetComponent<CardSetup>().CardSystemShutdown();
        
        // 关闭通用系统
        GameEntry.GetComponent<GeneralSetup>().GeneralSystemShutDown();
    }
}
