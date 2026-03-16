using AAAGame.Scripts.Entity;
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
        EntityRegistry.Clear();

        // 订阅实体显示成功事件，给所有生物挂血条 + 注册玩家
        GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);

        // --- 玩家 ---
        var pPlayer = EntityParams.Create(
            position: new Vector3(0, 1, 0)
        );
        pPlayer.Side = SideType.PlayerSide;
        pPlayer.BrainType = BrainType.Player;
        GF.Entity.ShowEntity<CharacterEntity>(3, UtilityBuiltin.AssetsPath.GetPrefab("Entity/TestCreature"), "Player", pPlayer);

        // --- 5 个友军小兵（Steering AI）---
        for (int i = 0; i < 5; i++)
        {
            Vector3 spawnPos = new Vector3(1f + (i * 3f), 1f, 0f);

            var pFriendly = EntityParams.Create(position: spawnPos);
            pFriendly.Side = SideType.PlayerSide;
            pFriendly.BrainType = BrainType.SoldierAI;

            int entityId = 10 + i;

            GF.Entity.ShowEntity<SoldierEntity>(
                entityId,
                UtilityBuiltin.AssetsPath.GetPrefab("Entity/TestCreature"),
                "Level",
                pFriendly
            );
        }

        // --- 敌方小兵 ---
        var pEnemy = EntityParams.Create(
            position: new Vector3(20, 1, 0)
        );
        pEnemy.Side = SideType.EnemySide;
        pEnemy.BrainType = BrainType.SoldierAI;
        GF.Entity.ShowEntity<SoldierEntity>(5, UtilityBuiltin.AssetsPath.GetPrefab("Entity/TestCreature"), "Level", pEnemy);


        GameEntry.GetComponent<InputManager>()
            .ChangeState(InputState.Game);
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        GF.Event.Unsubscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);
        base.OnLeave(procedureOwner, isShutdown);
    }

    private void OnShowEntitySuccess(object sender, GameEventArgs e)
    {
        var args = (ShowEntitySuccessEventArgs)e;
        if (args.Entity.Logic is MAEntity ma)
        {
            // 玩家注册为 Player
            if (ma.Brain is PlayerBrain)
            {
                EntityRegistry.RegisterAsPlayer(ma);
            }

            // 给所有生物挂血条
            if (ma is GeneralCreature creature)
            {
                float max = (float)creature.CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
                HealthBarComp.Create(creature.Id, creature.transform, creature.health, max);
            }

            // SoldierAIBrain 需要重新 Inject（玩家可能在它之后创建）
            if (ma.Brain is SoldierAIBrain soldierBrain)
            {
                soldierBrain.Inject(EntityRegistry.Player, EntityRegistry.AllEntities);
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
