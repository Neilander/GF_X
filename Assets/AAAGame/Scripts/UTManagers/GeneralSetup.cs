using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;
using GameFramework.Event;
using AAAGame.Scripts.Entity;
using SixLabors.ImageSharp.ColorSpaces.Companding;
using log4net.Core;

public partial class GeneralSetup : GameFrameworkComponent
{
    public void DataModelSetup(LevelData levelData)
    {
        var levelDataParams = RefParams.Create();
        levelDataParams.Set(InGameDataModel.P_LevelData, levelData);
        GF.DataModel.CreateDataModel<InGameDataModel>(levelDataParams);

        GF.DataModel.CreateDataModel<BuildingDataModel>();
        GF.DataModel.CreateDataModel<TechDataModel>();
        GF.DataModel.CreateDataModel<LocalizationTextDataModel>();
        GF.DataModel.CreateDataModel<InputModel>();
    }

    public void GeneralSystemSetup(string lvIdentifier = "Lv_1")
    {
        GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnGeneralShowEntitySuccess);

        var lvRow = GetLvRow(lvIdentifier);
        var lvData = LevelData.FromRow(lvRow);
        DataModelSetup(lvData);
        GameEntry.GetComponent<GameEndManager>().Init(lvData);
        LevelEntityFactory.ShowLevel(lvRow.PrefabPath);

        var inputManager = GameEntry.GetComponent<InputManager>();
        if (inputManager != null)
        {
            inputManager.ChangeState(InputState.Game);
        }

        SoldierFactory.ShowSoldier(UnitType.Unit_Courier, new Vector3(0, 1, -8), SideType.PlayerSide, BrainType.Player);
    }

    public void GeneralSystemShutDown()
    {
        GF.Event.Unsubscribe(ShowEntitySuccessEventArgs.EventId, OnGeneralShowEntitySuccess);
    }

    public LevelTable GetLvRow(string lvIdentifier)
    {
        var levelTable = GF.DataTable.GetDataTable<LevelTable>();
        var levelRow = levelTable.GetDataRow(row => row.Identifier == lvIdentifier);
        return levelRow;
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

                // 根据单位的Side判断阵营，友方显示绿色血条，敌方显示红色血条
                bool isFriendly = creature.Side == SideType.PlayerSide;
                Log.Info($"Creating health bar for unit {creature.Id}, side={creature.Side}, isFriendly={isFriendly}");
                HealthBarComp.Create(creature.Id, creature.transform, (float)creature.HealthValue, (float)max, isFriendly);
            }

            // SoldierAIBrain 需要重新 Inject（玩家可能在它之后创建）
            if (ma.Brain is SoldierAIBrain soldierBrain)
            {
                soldierBrain.Inject();
            }
        }
    }
}
