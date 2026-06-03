using System.Collections.Generic;
using AAAGame.Scripts.BuffSystem;
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
    // 静态配置，由 Editor Window 写入，EditorPrefs 持久化
    public static int FriendlyCount = 5;
    public static int EnemyCount = 1;
    public static Vector3 PlayerSpawn = new Vector3(0, 1, 0);
    public static float FriendlySpacing = 3f;
    public static Vector3 EnemySpawnCenter = new Vector3(20, 1, 0);
    public static float EnemySpacing = 3f;

    // Buff 配置：由 EditorWindow 控制哪些 buff 在出生时自动附加
    public static bool StartWithBuff = false;
    public static bool FriendlyBuff_Dot = false;
    public static bool FriendlyBuff_Regen = false;
    public static bool FriendlyBuff_Speed = false;
    public static bool FriendlyBuff_Shield = false;
    public static bool EnemyBuff_Dot = false;
    public static bool EnemyBuff_Regen = false;
    public static bool EnemyBuff_Speed = false;
    public static bool EnemyBuff_Shield = false;

    /*
    private static List<BuffData> BuildBuffList(bool dot, bool regen, bool speed, bool shield)
    {
        if (!StartWithBuff) return null;
        var list = new List<BuffData>();
        if (dot) list.Add(DebugBuffExamples.CreatePoisonBuff());
        if (regen) list.Add(DebugBuffExamples.CreateRegenBuff());
        if (speed) list.Add(DebugBuffExamples.CreateSpeedBuff());
        if (shield) list.Add(DebugBuffExamples.CreateShieldBuff());
        return list.Count > 0 ? list : null;
    }*/

    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        GF.Log("正在进行测试，取消测试去修改LaunchProcedure");

        //尝试创建物体
        InitDataModels();
        EntityRegistry.Clear();

        // 订阅实体显示成功事件，给所有生物挂血条 + 注册玩家
        GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);

        int friendlyCount = FriendlyCount;
        int enemyCount = EnemyCount;
        Vector3 playerSpawn = PlayerSpawn;
        float friendlySpacing = FriendlySpacing;
        Vector3 enemyCenter = EnemySpawnCenter;
        float enemySpacing = EnemySpacing;

        //var friendlyBuffs = BuildBuffList(FriendlyBuff_Dot, FriendlyBuff_Regen, FriendlyBuff_Speed, FriendlyBuff_Shield);
        //var enemyBuffs = BuildBuffList(EnemyBuff_Dot, EnemyBuff_Regen, EnemyBuff_Speed, EnemyBuff_Shield);

        // --- 玩家 ---
        MAEntityFactory.ShowCharacter(
            prefabName: "gujia",
            characterKey: "Unit_Intern",
            position: playerSpawn,
            side: SideType.PlayerSide,
            brainType: BrainType.Player,
            entityGroup: Const.EntityGroup.Player);

        // --- 友军小兵 ---
        for (int i = 0; i < friendlyCount; i++)
        {
            Vector3 spawnPos = new Vector3(1f + (i * friendlySpacing), 1f, 0f);
            MAEntityFactory.ShowSoldier(
                prefabName: "gujia",
                characterKey: "Unit_Intern",
                position: spawnPos,
                side: SideType.PlayerSide,
                brainType: BrainType.SoldierAI,
                entityGroup: Const.EntityGroup.Level);
        }

        // --- 敌方小兵 ---
        for (int i = 0; i < enemyCount; i++)
        {
            Vector3 spawnPos = enemyCenter + new Vector3(i * enemySpacing, 0, 0);
            MAEntityFactory.ShowSoldier(
                prefabName: "gujia",
                characterKey: "Unit_BoneButcher",
                position: spawnPos,
                side: SideType.EnemySide,
                brainType: BrainType.SoldierAI,
                entityGroup: Const.EntityGroup.Level);
        }


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
                }

                // 根据单位的Side判断阵营
                bool isFriendly = creature.Side == SideType.PlayerSide;
                HealthBarComp.Create(creature.Id, creature.transform, (float)creature.HealthValue, (float)max, isFriendly);
            }

            // SoldierAIBrain 需要重新 Inject（玩家可能在它之后创建）
            if (ma.Brain is SoldierAIBrain soldierBrain)
            {
                soldierBrain.Inject();
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
