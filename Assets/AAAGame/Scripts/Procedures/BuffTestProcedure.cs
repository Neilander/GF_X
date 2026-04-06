using UnityEngine;
using GameFramework;
using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityGameFramework.Runtime;
using AAAGame.Scripts.Entity;
using AAAGame.Scripts.BuffSystem;
using AAAGame.Scripts.GeneralCreature;

/// <summary>
/// Buff测试流程
/// 测试码农单位（定时死亡）和剔骨狂魔单位（击杀回复）
/// </summary>
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class BuffTestProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        Debug.Log("BuffTestProcedure.OnEnter开始");
        base.OnEnter(procedureOwner);
        
        InitDataModels();
        EntityRegistry.Clear();
        
        // 订阅实体显示成功事件，给所有生物挂血条 + 注册玩家
        GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);
        
        // 创建测试单位
        SpawnTestUnits();
        
        // 设置输入状态为游戏模式
        var inputManager = GameEntry.GetComponent<InputManager>();
        if (inputManager != null)
        {
            inputManager.ChangeState(InputState.Game);
        }
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
        try
        {
            Debug.Log("BuffTestProcedure.SpawnTestUnits开始执行");
            
            // 创建玩家控制的码农单位（定时死亡Buff）
            Debug.Log("创建玩家控制的码农单位（定时死亡Buff）");
            int playerEntityId = SoldierFactory.ShowSoldier("coder", new Vector3(0, 1, -8), SideType.PlayerSide, BrainType.Player);
            Debug.Log($"玩家单位创建完成，ID={playerEntityId}");
            
            // 使用族生成创建友方码农单位（定时死亡Buff）
            Debug.Log("使用族生成创建友方码农单位（定时死亡Buff）");
            bool friendSpawnSuccess = ClusterSpawnSystem.SpawnCluster(new Vector3(0, 1, -7), 5, 5f, 2f, "coder", SideType.PlayerSide, BrainType.SoldierAI);
            Debug.Log($"友方单位族生成结果: {friendSpawnSuccess}");
            
            // 使用族生成创建敌方剔骨狂魔单位（击杀回复Buff）
            Debug.Log("使用族生成创建敌方剔骨狂魔单位（击杀回复Buff）");
            bool enemySpawnSuccess = ClusterSpawnSystem.SpawnCluster(new Vector3(0, 1, 6), 3, 5f, 2f, "bone_reaper", SideType.EnemySide, BrainType.SoldierAI);
            Debug.Log($"敌方单位族生成结果: {enemySpawnSuccess}");
            
            Debug.Log("BuffTestProcedure.SpawnTestUnits执行完成");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("SpawnTestUnits出错: " + ex.Message);
            Debug.LogError("堆栈跟踪: " + ex.StackTrace);
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
            if (ma is GeneralCreature creature && creature.CreaturePropertyManager != null)
            {
                try
                {
                    float originalMax = (float)creature.CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
                    float max = originalMax;
                    
                    // 根据单位类型调整血量
                    if (ma is SoldierEntity soldier)
                    {
                        if (soldier.UnitIndex == "coder")
                        {
                            // 码农单位：血量减少2倍
                            float newMax = originalMax / 2f;
                            Fix64 subtractValue = (Fix64)(originalMax - newMax);
                            var modifier = PropertyDirectAdditiveModifier.Create(-subtractValue);
                            creature.CreaturePropertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Health, modifier, true);
                            max = newMax;
                        }
                        else if (soldier.UnitIndex == "bone_reaper")
                        {
                            // 剔骨狂魔单位：血量减少2倍，和码农一样
                            float newMax = originalMax / 2f;
                            Fix64 subtractValue = (Fix64)(originalMax - newMax);
                            var modifier = PropertyDirectAdditiveModifier.Create(-subtractValue);
                            creature.CreaturePropertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Health, modifier, true);
                            max = newMax;
                            Debug.Log($"剔骨狂魔单位[ID={ma.Id}]生命值设置为{newMax}");
                        }
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
                catch (System.Exception ex)
                {
                    Debug.LogError("设置生命值和血条出错: " + ex.Message);
                }
            }

            // SoldierAIBrain 需要重新 Inject（玩家可能在它之后创建）
            if (ma.Brain is SoldierAIBrain soldierBrain)
            {
                soldierBrain.Inject();
            }
        }
    }
}