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
        // 初始化数据模型（参考CharacterTestProcedure）
        GF.DataModel.CreateDataModel<ItemDataModel>();
        GF.DataModel.CreateDataModel<DeviceDataModel>();
        GF.DataModel.CreateDataModel<LocalizationTextDataModel>();
        GF.DataModel.CreateDataModel<CraftingDeviceDataModel>();
        GF.DataModel.CreateDataModel<InputModel>(); // 必须初始化输入模型，否则PlayerBrain会出现空引用
        GF.DataModel.CreateDataModel<TechNodeDataModel>();

        GF.DataModel.GetOrCreate<ItemCollectionDataModel>();
        GF.DataModel.GetOrCreate<CapabilityProgressDataModel>();
        GF.DataModel.GetOrCreate<ProfileDataModel>();
        GF.DataModel.GetOrCreate<TechProgressDataModel>();
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
        GF.Event.Unsubscribe(ShowEntitySuccessEventArgs.EventId,OnGeneralShowEntitySuccess);
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
                
                HealthBarComp.Create(creature.Id, creature.transform, creature.health, max);
            }

            // SoldierAIBrain 需要重新 Inject（玩家可能在它之后创建）
            if (ma.Brain is SoldierAIBrain soldierBrain)
            {
                soldierBrain.Inject();
            }
        }
    }
}
