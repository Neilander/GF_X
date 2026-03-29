using GameFramework;
using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class SampleProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        GF.Log("这是一个示例Procedure");
        
        //这是必要的初始化
        InitDataModels();

        //GF.UI.OpenUIForm(UIViews.MaterialModifyBar);

        //这是设置游戏中的input模式
        GameEntry.GetComponent<InputManager>().ChangeState(InputState.Game);

        //这是一个自定义的方法，用于创建物体
        //SpawnPresetEntities();

    }

    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
    }
    
    /* 可以写自己的初始化创建方法
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
            }
        }
    }*/
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
