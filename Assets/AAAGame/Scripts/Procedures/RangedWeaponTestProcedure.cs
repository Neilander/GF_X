using GameFramework;
using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.Scripts.Entity;

/// <summary>
/// 远程武器测试流程：用于测试远程武器系统的功能
/// </summary>
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class RangedWeaponTestProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        GF.Log("远程武器测试流程开始");

        // 初始化数据模型
        InitDataModels();

        // 订阅实体显示成功事件，设置摄像机跟随玩家
        GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);

        // 设置输入模式为游戏模式
        GameEntry.GetComponent<InputManager>().ChangeState(InputState.Game);

        // 创建远程单位进行测试
        SpawnRangedUnits();
    }

    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        GF.Event.Unsubscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);
        base.OnLeave(procedureOwner, isShutdown);
    }

    /// <summary>
    /// 实体显示成功回调：设置摄像机跟随玩家
    /// </summary>
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
                if (CameraController.Instance != null)
                {
                    CameraController.Instance.SetFollowTarget(ma.transform);
                }
            }
        }
    }

    /// <summary>
    /// 创建远程单位进行测试
    /// </summary>
    private void SpawnRangedUnits()
    {
        // 创建玩家侧的远程单位（使用ranged_test来加载远程武器）
        MAEntityFactory.ShowSoldier(
            prefabName: "gujia",
            characterKey: "Unit_Intern",
            position: new Vector3(0, 1, -10),
            side: SideType.PlayerSide,
            brainType: BrainType.Player,
            entityGroup: Const.EntityGroup.Level);

        // 创建多个友方远程单位
        for (int i = 0; i < 3; i++)
        {
            Vector3 spawnPos = new Vector3(-4f + (i * 3f), 1f, -8f);
            MAEntityFactory.ShowSoldier(
                prefabName: "gujia",
                characterKey: "Unit_Intern",
                position: spawnPos,
                side: SideType.PlayerSide,
                brainType: BrainType.SoldierAI,
                entityGroup: Const.EntityGroup.Level);
        }

        // 创建多个敌方远程单位
        for (int i = 0; i < 3; i++)
        {
            Vector3 spawnPos = new Vector3(-4f + (i * 3f), 1f, 8f);
            MAEntityFactory.ShowSoldier(
                prefabName: "gujia",
                characterKey: "Unit_BoneButcher",
                position: spawnPos,
                side: SideType.EnemySide,
                brainType: BrainType.SoldierAI,
                entityGroup: Const.EntityGroup.Level);
        }

        GF.Log("远程武器测试单位创建完成，友方和敌方单位将互相发射子弹攻击");
    }

    /// <summary>
    /// 初始化数据模型
    /// </summary>
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
