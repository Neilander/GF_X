using UnityEngine;
using GameFramework;
using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityGameFramework.Runtime;
using AAAGame.Scripts.Entity;

/// <summary>
/// Buff测试流程
/// 测试码农单位（定时死亡）和剔骨狂魔单位（击杀回复）
/// </summary>
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class BuffTestProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        

        
        InitDataModels();
        EntityRegistry.Clear();
        
        // 订阅实体显示成功事件，给所有生物挂血条 + 注册玩家
        GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);
        
        // 创建测试单位
        SpawnTestUnits();
        
        // 设置输入状态为游戏模式
        GameEntry.GetComponent<InputManager>().ChangeState(InputState.Game);
        

    }
    
    private void InitDataModels()
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
    
    private void SpawnTestUnits()
    {
        // 创建玩家控制的码农单位（定时死亡Buff）
        SoldierFactory.ShowSoldier("coder", new Vector3(0, 1, -8), SideType.PlayerSide, BrainType.Player);
        
        // 使用簇生成系统创建友方码农单位（定时死亡Buff）
        bool success = ClusterSpawnSystem.SpawnCluster(
            center: new Vector3(0, 1, -6),
            count: 15,
            radius: 10f,
            minDistance: 2f,
            unitIndex: "coder",
            side: SideType.PlayerSide,
            brainType: BrainType.SoldierAI
        );
        
        if (success)
        {
            // 簇生成成功
        }
        else
        {
            // 簇生成失败
        }
        
        // 使用簇生成系统创建敌方剔骨狂魔单位（击杀回复Buff）
        success = ClusterSpawnSystem.SpawnCluster(
            center: new Vector3(0, 1, 6),
            count: 3,
            radius: 5f,
            minDistance: 3f,
            unitIndex: "bone_reaper",
            side: SideType.EnemySide,
            brainType: BrainType.SoldierAI
        );
        
        if (success)
        {
            // 簇生成成功
        }
        else
        {
            // 簇生成失败
        }
    }
    
    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        // 取消订阅事件
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
                }
                
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