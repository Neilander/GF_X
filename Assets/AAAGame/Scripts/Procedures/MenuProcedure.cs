using GameFramework;
using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class MenuProcedure : ProcedureBase
{
    bool fastEnterGame = true;
    LevelEntity lvEntity;
    IFsm<IProcedureManager> procedure;

    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        procedure = procedureOwner;
        //测试合成面版
        GF.DataModel.CreateDataModel<CraftingDeviceDataModel>();
        GF.DataModel.CreateDataModel<ItemDataModel>();
        UIParams uiParams = UIParams.Create();
        uiParams.Set(CraftingDialog.P_CraftingFormulas, CraftingDeviceDataModel.GetCraftingDeviceData("Device_Aaa"));
        GF.UI.OpenUIForm(UIViews.CraftingDialog, uiParams);

        //ShowLevel();
    }
    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
        // if (lvEntity == null || !lvEntity.IsAllReady)
        // {
        //     return;
        // }
        //点击屏幕开始游戏
        if (fastEnterGame)
        {
            EnterGame();
        }
    }
    public void EnterGame()
    {
        procedure.SetData<VarUnityObject>("LevelEntity", lvEntity);
        ChangeState<GameProcedure>(procedure);
    }
    public async void ShowLevel()
    {
        lvEntity = null;

        //动态创建关卡
        var lvTb = GF.DataTable.GetDataTable<LevelTable>();
        var playerMd = GF.DataModel.GetOrCreate<PlayerDataModel>();
        var lvId = playerMd.LevelId;
        var lvRow = lvTb.GetDataRow(lvId);

        // 获取已加载的数据表 - 使用指定名称
        // var lvEmyTb = GF.DataTable.GetDataTable<LevelEnemyTable>(lvId.ToString());
        // LevelEnemyTable[] lvEmy = lvEmyTb.GetAllDataRows();
        // var lvRouteTb = GF.DataTable.GetDataTable<LevelRouteTable>(lvId.ToString());
        // LevelRouteTable[] lvRoute = lvRouteTb.GetAllDataRows();

        var lvParams = EntityParams.Create(Vector3.zero);
        // lvParams.Set(LevelEntity.P_LevelDataRow, lvRow);
        // lvParams.Set(LevelEntity.P_LevelEnemyData, lvEmy);
        // lvParams.Set(LevelEntity.P_LevelRouteData, lvRoute);
        lvEntity = await GF.Entity.ShowEntityAwait<LevelEntity>(lvRow.LvPfbName, Const.EntityGroup.Level, lvParams) as LevelEntity;
        GF.BuiltinView.HideLoadingProgress();
    }
}
