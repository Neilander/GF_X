using System.Collections.Generic;
using GameFramework;
using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class RealProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        GameEntry.GetComponent<GeneralSetup>().GeneralSystemSetup("Lv_2");

        GF.UI.OpenUIForm(UIViews.ResourceModifyBar);
        // 加载阶段切换按钮
        GF.UI.OpenUIForm(UIViews.PhaseSwitchUIForm);
        GF.UI.OpenUIForm(UIViews.SupplyUIForm);


        GameEntry.GetComponent<InputManager>().ChangeState(InputState.Game);

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
