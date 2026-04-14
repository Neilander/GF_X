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
        // 初始化数据模型
        var generalSetup = GameEntry.GetComponent<GeneralSetup>();
        generalSetup.GeneralSystemSetup();
        generalSetup.InitLevel("Lv_1");
        // 初始化卡牌系统
        GameEntry.GetComponent<CardSetup>().CardSystemSetup();
        GameEntry.GetComponent<CardSetup>().OpenCardUI();

        GF.UI.OpenUIForm(UIViews.ResourceModifyBar);

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
    // private void InitDataModels()
    // {
    //     RefParams levelDataParams = new();
    //     levelDataParams.Set(InGameDataModel.P_StartPhase, GamePhase.Build);
    //     levelDataParams.Set(InGameDataModel.P_StartCoins, 100);
    //     levelDataParams.Set(InGameDataModel.P_StartFactions, new Dictionary<int, Faction> { { 0, new Faction(0) }, { 1, new Faction(1) } });   // 通常玩家势力key为0，敌对势力为1、2等。
    //     GF.DataModel.CreateDataModel<InGameDataModel>(levelDataParams);

    //     GF.DataModel.CreateDataModel<BuildingDataModel>();
    //     GF.DataModel.CreateDataModel<TechDataModel>();
    //     GF.DataModel.CreateDataModel<LocalizationTextDataModel>();
    //     GF.DataModel.CreateDataModel<InputModel>();
    // }
}
