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

        GF.UI.OpenUIForm(UIViews.MaterialModifyBar);

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
                case EntityPresetPointType.Spawn:
                case EntityPresetPointType.Respawn:
                case EntityPresetPointType.Patrol:
                    //尝试创建物体
                    // EntityParams newParams = new EntityParams();
                    // newParams.position = new Vector3(0, 1, 0);
                    // GF.Entity.ShowEntity<PlayerEntity>(0,UtilityBuiltin.AssetsPath.GetPrefab("Entity/TestCreature"),"Player",newParams);
                    break;
                case EntityPresetPointType.Device:
                    EntityParams deviceParams = EntityParams.Create(point.Position);
                    Device deviceData = new Device("Device_Base", null, null, Fix64.Zero, null, "Device_Base", "Device_Base", upgradeID: point.Identifier, null);
                    deviceParams.Set(DeviceEntity.P_DeviceData, deviceData);
                    GF.Entity.ShowEntity<DeviceEntity>("Device/DeviceBase", Const.EntityGroup.Building, deviceParams);
                    break;
                case EntityPresetPointType.Buil_Base:
                    EntityParams buildingParams = EntityParams.Create(point.Position);
                    BuildingData buildingData = new BuildingData("Buil_Base_Lv0", BuilType.Base, Archetype.Coding, "Buil_Base", "Buil_Base", "Buil_Base", 0, 0, 1, 0, 0, null, null, 0, null);
                    buildingParams.Set(BuildingEntity.P_BuildingData, buildingData);
                    GF.Entity.ShowEntity<BuildingEntity>("Building/Buil_Base", Const.EntityGroup.Building, buildingParams);
                    break;
            }
        }
    }
    private void InitDataModels()
    {
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
