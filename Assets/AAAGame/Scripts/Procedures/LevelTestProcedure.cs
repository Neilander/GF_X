using System.Collections.Generic;
using GameFramework;
using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class LevelTestProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        GF.Log("正在进行测试，取消测试去修改LaunchProcedure");
        InitDataModels();

        GF.UI.OpenUIForm(UIViews.ResourceModifyBar);

        GameEntry.GetComponent<InputManager>().ChangeState(InputState.Game);

        SpawnPresetEntities();

    }

    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
        if (GF.DataModel.GetDataModel<InputModel>().OpenTechTreePressed)
        {
            GF.UI.OpenUIForm(UIViews.TechTreeDialog);
        }
    }
    private void SpawnPresetEntities()
    {
        var presetPoints = GameObject.FindObjectsOfType<EntityPresetPoint>();
        foreach (var point in presetPoints)
        {
            switch (point.PointType)
            {
                case EntityPresetPointType.Building:
                    BuildManager.BuildBuildingForLevelInit(point.Identifier, point.Position, point.OwnerFactionID);
                    break;
                    // case EntityPresetPointType.Spawn:
                    // case EntityPresetPointType.Respawn:
                    // case EntityPresetPointType.Patrol:
                    //尝试创建物体
                    // EntityParams newParams = new EntityParams();
                    // newParams.position = new Vector3(0, 1, 0);
                    // GF.Entity.ShowEntity<PlayerEntity>(0,UtilityBuiltin.AssetsPath.GetPrefab("Entity/TestCreature"),"Player",newParams);
                    //    break;
                    // case EntityPresetPointType.Device:   //以前的实现，只能建造预设的一个特定设备
                    //     EntityParams deviceParams = EntityParams.Create(point.Position);
                    //     Device deviceData = new Device("Device_Base", null, null, Fix64.Zero, null, "Device_Base", "Device_Base", upgradeID: point.Identifier, null);
                    //     deviceParams.Set(DeviceEntity.P_DeviceData, deviceData);
                    //     GF.Entity.ShowEntity<DeviceEntity>("Device/DeviceBase", Const.EntityGroup.Building, deviceParams);
                    //     break;
            }
        }
    }
    private void InitDataModels()
    {
        RefParams levelParams = new();
        levelParams.Set(InGameDataModel.P_StartPhase, GamePhase.Build);
        levelParams.Set(InGameDataModel.P_StartCoins, 100);
        levelParams.Set(InGameDataModel.P_StartFactions, new Dictionary<int, Faction> { { 0, new Faction(0) }, { 1, new Faction(1) } });   // 通常玩家势力key为0，敌对势力为1、2等。
        GF.DataModel.CreateDataModel<InGameDataModel>(levelParams);

        GF.DataModel.CreateDataModel<BuildingDataModel>();
        GF.DataModel.CreateDataModel<TechDataModel>();
        GF.DataModel.CreateDataModel<ItemDataModel>();
        GF.DataModel.CreateDataModel<DeviceDataModel>();
        GF.DataModel.CreateDataModel<LocalizationTextDataModel>();
        GF.DataModel.CreateDataModel<CraftingDeviceDataModel>();
        GF.DataModel.CreateDataModel<InputModel>();
        GF.DataModel.CreateDataModel<TechNodeDataModel>();


        GF.DataModel.GetOrCreate<ItemCollectionDataModel>();
        GF.DataModel.GetOrCreate<CapabilityProgressDataModel>();
        GF.DataModel.GetOrCreate<ProfileDataModel>();
        GF.DataModel.GetOrCreate<TechProgressDataModel>();
    }
}
