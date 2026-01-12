using GameFramework;
using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class CharacterTestProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        GF.Log("正在进行测试，取消测试去修改LaunchProcedure");
        
        //尝试创建物体
        InitDataModels();
        EntityParams newParams = new EntityParams();
        newParams.position = new Vector3(0, 1, 0);
        GF.Entity.ShowEntity<PlayerEntity>(0,UtilityBuiltin.AssetsPath.GetPrefab("Entity/TestCreature"),"Player",newParams);
        GameEntry.GetComponent<InputManager>().ChangeState(InputState.Game);
        EntityParams punchParams = new EntityParams();
        punchParams.position = new Vector3(2, 1, 0);
        GF.Entity.ShowEntity<PunchBagEntity>(1,UtilityBuiltin.AssetsPath.GetPrefab("Entity/PunchBag"),"Level",punchParams);
        

        //CreaturePropertyManager creaturePropertyManager = new CreaturePropertyManager("Knight");
        //GF.Log(creaturePropertyManager.propertyManager.ToString());
        //GF.Log(creaturePropertyManager.GetProperty(CreatureCurrentProperty.HealthCurrent).ToString());
        //var vp = PropertyDirectAdditiveModifier.Create((Fix64)(1));


        //creaturePropertyManager.ModifyMainPropertyMul(CreatureMainProperty.Health,NormalBaseValueTp.Base,vp);
        //GF.Log(creaturePropertyManager.propertyManager.ToString());
        //GF.Log(creaturePropertyManager.propertyManager.GetValueProperty("Health_Value_Buff").GetValue().ToString());
        //GF.Log(creaturePropertyManager.GetProperty(CreatureMainProperty.Health).ToString());
        //creaturePropertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Health,vp,false);
        //GF.Log(creaturePropertyManager.GetProperty(CreatureMainProperty.Health).ToString());


        //GF.Log(GF.DataModel.GetDataModel<PlayerDataModel>().Hp.ToString());
        //GF.DataModel.CreateDataModel<PlayerDataModel>();
        //GF.Log(GF.DataModel.GetDataModel<PlayerDataModel>().Hp.ToString());



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
