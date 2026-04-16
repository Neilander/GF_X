using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 小地图游戏流程
/// 在 Game 场景加载完成后进入此流程，显示小地图 UI
/// 可以与卡牌系统和战争迷雾系统结合使用
/// </summary>
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class MinimapGameProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        
        Log.Info("[Minimap] 进入小地图游戏流程");

        // 初始化通用系统
        GameEntry.GetComponent<GeneralSetup>().GeneralSystemSetup();

        // 初始化小地图系统
        GameEntry.GetComponent<MinimapSetup>().MinimapSystemSetup();
        GameEntry.GetComponent<MinimapSetup>().OpenMinimapUI();
    }

    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
        
        // 更新小地图系统
        GameEntry.GetComponent<MinimapSetup>().MinimapSystemUpdate();
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        base.OnLeave(procedureOwner, isShutdown);
        
        // 关闭小地图系统
        GameEntry.GetComponent<MinimapSetup>().MinimapSystemShutdown();
        
        // 关闭通用系统
        GameEntry.GetComponent<GeneralSetup>().GeneralSystemShutDown();
    }
}
