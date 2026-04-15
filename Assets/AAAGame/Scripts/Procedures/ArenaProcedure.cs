using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityGameFramework.Runtime;

public class ArenaProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);

        Log.Info("[CardGame] 进入竞技场游戏流程");

        // 初始化数据模型
        GameEntry.GetComponent<GeneralSetup>().GeneralSystemSetup();
    }

    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

        GameEntry.GetComponent<CardSetup>().CardSystemUpdate();
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        base.OnLeave(procedureOwner, isShutdown);

        GameEntry.GetComponent<CardSetup>().CardSystemShutdown();
        GameEntry.GetComponent<GeneralSetup>().GeneralSystemShutDown();
    }
}
