using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;
using GameFramework.Event;
using AAAGame.Scripts.Entity;

public partial class GeneralSetup : GameFrameworkComponent
{
    public event Action OnGeneralSetupCompleted;
    public bool IsGeneralSetupCompleted => m_InitialPhaseEntered;

    private bool m_InitialPhaseEntered;
    private bool m_LevelReady;
    private bool m_PlayerReady;
    private bool m_SetupInProgress;
    private bool m_ShowEntitySubscribed;
    private LevelEntity m_LevelEntity;

    public void GeneralSystemSetup(string lvIdentifier = "Lv_1")
    {
        if (m_SetupInProgress)
        {
            Log.Warning("[GeneralSetup] GeneralSystemSetup is already running, skip duplicate request.");
            return;
        }

        m_SetupInProgress = true;
        m_InitialPhaseEntered = false;
        m_LevelReady = false;
        m_PlayerReady = false;
        m_LevelEntity = null;

        var lvRow = GetLvRow(lvIdentifier);
        if (lvRow == null)
        {
            Log.Error("[GeneralSetup] Level row not found. levelIdentifier={0}", lvIdentifier);
            m_SetupInProgress = false;
            return;
        }

        if (!m_ShowEntitySubscribed)
        {
            GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnGeneralShowEntitySuccess);
            m_ShowEntitySubscribed = true;
        }

        var lvData = LevelData.FromRow(lvRow);
        DataModelSetup(lvData);
        GameEntry.GetComponent<GameEndManager>().Init(lvData);
        LevelEntityFactory.ShowLevel(lvRow.PrefabPath);

        var inputManager = GameEntry.GetComponent<InputManager>();
        if (inputManager != null)
        {
            inputManager.FindModel();
            inputManager.ChangeState(InputState.UIForm);
        }

        BootstrapSideTipsManager();
        GF.UI.OpenUIForm(UIViews.SideTipsUIForm);
        GF.UI.OpenUIForm(UIViews.GoalUIForm);
    }

    public void GeneralSystemShutDown()
    {
        if (m_ShowEntitySubscribed)
        {
            GF.Event.Unsubscribe(ShowEntitySuccessEventArgs.EventId, OnGeneralShowEntitySuccess);
            m_ShowEntitySubscribed = false;
        }

        if (m_LevelEntity != null)
        {
            m_LevelEntity.RuntimeInitializationCompleted -= OnLevelRuntimeInitializationCompleted;
            m_LevelEntity = null;
        }

        GF.UI.CloseUIForms(UIViews.SideTipsUIForm);
        GF.UI.CloseUIForms(UIViews.GoalUIForm);
        m_InitialPhaseEntered = false;
        m_LevelReady = false;
        m_PlayerReady = false;
        m_SetupInProgress = false;
    }

    public void DataModelSetup(LevelData levelData)
    {
        var levelDataParams = RefParams.Create();
        levelDataParams.Set(InGameDataModel.P_LevelData, levelData);
        GF.DataModel.CreateDataModel<InGameDataModel>(levelDataParams);

        RewardManager rewardManager = GameEntry.GetComponent<RewardManager>();
        if (rewardManager == null)
        {
            rewardManager = gameObject.AddComponent<RewardManager>();
        }

        rewardManager.ResetLevelCounters();

        GF.DataModel.CreateDataModel<BuildingDataModel>();
        GF.DataModel.CreateDataModel<TechDataModel>();
        GF.DataModel.CreateDataModel<SkillDataModel>();
        GF.DataModel.CreateDataModel<LocalizationTextDataModel>();
        GF.DataModel.CreateDataModel<InputModel>();
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

        if (args.Entity.Logic is LevelEntity)
        {
            if (m_LevelEntity != null)
            {
                m_LevelEntity.RuntimeInitializationCompleted -= OnLevelRuntimeInitializationCompleted;
            }

            m_LevelEntity = (LevelEntity)args.Entity.Logic;
            if (m_LevelEntity.IsRuntimeInitializationCompleted)
            {
                m_LevelReady = true;
            }
            else
            {
                m_LevelEntity.RuntimeInitializationCompleted += OnLevelRuntimeInitializationCompleted;
            }

            TryEnterInitialPhaseIfReady();
        }

        if (args.Entity.Logic is MAEntity ma)
        {
            // 玩家注册为 Player
            if (ma.Brain is PlayerBrain)
            {
                EntityRegistry.RegisterAsPlayer(ma);
                m_PlayerReady = true;

                // 设置摄像机跟随玩家
                CameraController cameraController = Camera.main.GetComponent<CameraController>();
                if (cameraController != null)
                {
                    cameraController.SetFollowTarget(ma.transform);
                }

                TryEnterInitialPhaseIfReady();
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

    private void OnLevelRuntimeInitializationCompleted(LevelEntity levelEntity)
    {
        if (m_LevelEntity != levelEntity)
        {
            return;
        }

        m_LevelEntity.RuntimeInitializationCompleted -= OnLevelRuntimeInitializationCompleted;
        m_LevelReady = true;
        TryEnterInitialPhaseIfReady();
    }

    private void TryEnterInitialPhaseIfReady()
    {
        if (m_InitialPhaseEntered)
        {
            return;
        }

        if (!m_LevelReady || !m_PlayerReady)
        {
            return;
        }

        PhaseManager.EnterCurrentPhaseOnGameStart();
        m_InitialPhaseEntered = true;
        m_SetupInProgress = false;
        BootstrapSideTipsManager();
        Log.Info("[GeneralSetup] Core runtime systems are ready.");
        OnGeneralSetupCompleted?.Invoke();
    }

    private void BootstrapSideTipsManager()
    {
        var sideTipsManager = GameEntry.GetComponent<SideTipsManager>();
        if (sideTipsManager != null)
        {
            sideTipsManager.BootstrapIfNeeded();
        }
    }
}
