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
        
        var playerParams = EntityParams.Create(
            position: new Vector3(0, 1, 0)
        );
        
        // GF.Entity.ShowEntity<PlayerEntity>(
        //     0,
        //     UtilityBuiltin.AssetsPath.GetPrefab("Entity/TestCreature"),
        //     "Player",
        //     playerParams
        // );
        
        
        var pPlayer = EntityParams.Create(
            position: new Vector3(0, 1, 0)
        );
        pPlayer.Side = SideType.PlayerSide;
        pPlayer.BrainType = BrainType.Player;
        GF.Entity.ShowEntity<CharacterEntity>(3, UtilityBuiltin.AssetsPath.GetPrefab("Entity/TestCreature"), "Player", pPlayer);

        // 循环生成 5 个友军护卫
        for (int i = 0; i < 5; i++)
        {
            // 利用循环的 i 给他们一个初始的位置偏移，比如排成一排 (每个间隔 1.5 单位)
            Vector3 spawnPos = new Vector3(1f + (i * 3f), 1f, 0f);

            var pFriendly = EntityParams.Create(position: spawnPos);
            pFriendly.Side = SideType.PlayerSide;
            pFriendly.BrainType = BrainType.FriendlyAI;

            // 分配唯一的实体 ID：10, 11, 12, 13, 14
            int entityId = 10 + i;

            GF.Entity.ShowEntity<CharacterEntity>(
                entityId, 
                UtilityBuiltin.AssetsPath.GetPrefab("Entity/TestCreature"), 
                "Level", 
                pFriendly
            );
        }
        

        var pEnemy = EntityParams.Create(
            position: new Vector3(20, 1, 0)
        );
        pEnemy.Side = SideType.EnemySide;
        pEnemy.BrainType = BrainType.EnemyAI;
        GF.Entity.ShowEntity<CharacterEntity>(5, UtilityBuiltin.AssetsPath.GetPrefab("Entity/TestCreature"), "Level", pEnemy);
        
        
        
        
        
        
        
        GameEntry.GetComponent<InputManager>()
            .ChangeState(InputState.Game);
        
        // var punchBagParams = EntityParams.Create(
        //     position: new Vector3(2, 1, 0)
        // );
        //
        // GF.Entity.ShowEntity<PunchBagEntity>(
        //     1,
        //     UtilityBuiltin.AssetsPath.GetPrefab("Entity/PunchBag"),
        //     "Level",
        //     punchBagParams
        // );
        
        
       

        
        
        /*
        EntityParams newParams = new EntityParams();
        newParams.position = new Vector3(0, 1, 0);
        GF.Entity.ShowEntity<PlayerEntity>(0,UtilityBuiltin.AssetsPath.GetPrefab("Entity/TestCreature"),"Player",newParams);
        GameEntry.GetComponent<InputManager>().ChangeState(InputState.Game);
        EntityParams punchParams = new EntityParams();
        punchParams.position = new Vector3(2, 1, 0);
        GF.Entity.ShowEntity<PunchBagEntity>(1,UtilityBuiltin.AssetsPath.GetPrefab("Entity/PunchBag"),"Level",punchParams);*/
        //GF.Entity.ShowEntity<DeviceEntity>(deviceData.PrefabName, Const.EntityGroup.Building, deviceParams);
        

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
