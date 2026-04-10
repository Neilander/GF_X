using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;
using GameFramework.Event;
using AAAGame.Scripts.Entity;

public partial class GeneralSetup : GameFrameworkComponent
{
    public void DataModelSetup()
    {
        RefParams levelDataParams = new();
        levelDataParams.Set(InGameDataModel.P_StartPhase, GamePhase.Build);
        levelDataParams.Set(InGameDataModel.P_StartCoins, 100);
        levelDataParams.Set(InGameDataModel.P_StartFactions, new Dictionary<int, Faction> { { 0, new Faction(0) }, { 1, new Faction(1) } });   // 通常玩家势力key为0，敌对势力为1、2等。
        GF.DataModel.CreateDataModel<InGameDataModel>(levelDataParams);

        GF.DataModel.CreateDataModel<BuildingDataModel>();
        GF.DataModel.CreateDataModel<TechDataModel>();
        GF.DataModel.CreateDataModel<LocalizationTextDataModel>();
        GF.DataModel.CreateDataModel<InputModel>();
    }

    public void GeneralSystemSetup()
    {
        DataModelSetup();
        GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnGeneralShowEntitySuccess);

        var inputManager = GameEntry.GetComponent<InputManager>();
        if (inputManager != null)
        {
            inputManager.ChangeState(InputState.Game);
        }

        SoldierFactory.ShowSoldier(UnitType.Unit_Hero, new Vector3(0, 1, -8), SideType.PlayerSide, BrainType.Player);
    }

    public void GeneralSystemShutDown()
    {
        GF.Event.Unsubscribe(ShowEntitySuccessEventArgs.EventId, OnGeneralShowEntitySuccess);
    }

    private void OnGeneralShowEntitySuccess(object sender, GameEventArgs e)
    {
        var args = (ShowEntitySuccessEventArgs)e;
        if (args.Entity.Logic is MAEntity ma)
        {
            // 玩家注册为 Player
            if (ma.Brain is PlayerBrain)
            {
                EntityRegistry.RegisterAsPlayer(ma);

                // 设置摄像机跟随玩家
                CameraController cameraController = Camera.main.GetComponent<CameraController>();
                if (cameraController != null)
                {
                    cameraController.SetFollowTarget(ma.transform);
                }
            }

            // 给所有生物挂血条
            if (ma is GeneralCreature creature)
            {
                Log.Info($"Unit {ma.Id} created: type={ma.GetType().Name}, side={creature.Side}");
                float originalMax = (float)creature.CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
                float max = originalMax;

                // 根据单位类型调整血量
                /*
                if (ma is SoldierEntity soldier && soldier.UnitIndex == "coder")
                {
                    // 码农单位：血量减少10倍
                    float newMax = originalMax / 10f;
                    Fix64 subtractValue = (Fix64)(originalMax - newMax);
                    var modifier = PropertyDirectAdditiveModifier.Create(-subtractValue);
                    creature.CreaturePropertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Health, modifier, true);
                    max = newMax;
                }
                else
                {
                    // 敌方单位：保持原血量不变
                }*/
                // 更新当前生命值，确保单位满血
                float currentHealth = creature.health;
                float healAmount = max - currentHealth;
                if (healAmount > 0)
                {
                    creature.CreaturePropertyManager.ModifyCurrentProperty(
                        CreatureCurrentProperty.HealthCurrent,
                        PropertyIrreversibleAdditiveModifier.Create((Fix64)healAmount), true);
                }

                // 根据单位的Side判断阵营，友方显示绿色血条，敌方显示红色血条
                bool isFriendly = creature.Side == SideType.PlayerSide;
                Log.Info($"Creating health bar for unit {creature.Id}, side={creature.Side}, isFriendly={isFriendly}");
                HealthBarComp.Create(creature.Id, creature.transform, creature.health, max, isFriendly);
            }

            // SoldierAIBrain 需要重新 Inject（玩家可能在它之后创建）
            if (ma.Brain is SoldierAIBrain soldierBrain)
            {
                soldierBrain.Inject();
            }
        }
    }
}
