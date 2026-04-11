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
            // 创建玩家控制的码农单位（定时死亡Buff）
            int playerEntityId = SoldierFactory.ShowSoldier(UnitType.Unit_Coder, new Vector3(0, 1, -8), SideType.PlayerSide, BrainType.Player);

            // 使用族生成创建友方码农单位（定时死亡Buff）
            bool friendSpawnSuccess = ClusterSpawnSystem.SpawnCluster(new Vector3(0, 1, -7), 5, 5f, 2f, UnitType.Unit_Coder, SideType.PlayerSide, BrainType.SoldierAI);

            // 使用族生成创建敌方剔骨狂魔单位（击杀回复Buff）
            bool enemySpawnSuccess = ClusterSpawnSystem.SpawnCluster(new Vector3(0, 1, 6), 3, 5f, 2f, UnitType.Unit_BoneButcher, SideType.EnemySide, BrainType.SoldierAI);
        }
        catch (System.Exception ex)
        {
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
                    Fix64 originalMax = creature.CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
                    Fix64 max = originalMax;
                    
                    // 所有单位血量增加到10倍
                    Fix64 newMax = originalMax * (Fix64)10;
                    Fix64 addValue = newMax - originalMax;
                    var modifier = PropertyDirectAdditiveModifier.Create(addValue);
                    creature.CreaturePropertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Health, modifier, true);
                    max = newMax;
                    
                    // 更新当前生命值，确保单位满血
                    Fix64 currentHealth = creature.HealthValue;
                    Fix64 healAmount = max - currentHealth;
                    if (healAmount > Fix64.Zero)
                    {
                        creature.CreaturePropertyManager.ModifyCurrentProperty(
                            CreatureCurrentProperty.HealthCurrent,
                            PropertyIrreversibleAdditiveModifier.Create(healAmount), true);
                        
                        // 显示治疗跳字
                        Vector3 startPos = creature.transform.position + new Vector3(0, 2.0f, 0);
                        Vector3 endPos = startPos + new Vector3(0, 1.5f, 0);
                        Log.Info($"Heal pop text: healAmount={healAmount}, startPos={startPos}, endPos={endPos}");
                        GF.Entity.ShowPopText(EntityParams.Create(startPos, Vector3.zero, Vector3.one), $"+{healAmount}", endPos, DamageTextType.Heal);
                    }
                    
                    // 根据单位的Side判断阵营
                    bool isFriendly = creature.Side == SideType.PlayerSide;
                    HealthBarComp.Create(creature.Id, creature.transform, (float)creature.HealthValue, (float)max, isFriendly);
                }
                catch (System.Exception ex)
                {
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