using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;
using GameFramework.Event;
using AAAGame.Scripts.Entity;
using Stopwatch = System.Diagnostics.Stopwatch;

public partial class GeneralSetup : GameFrameworkComponent
{
    public event Action OnGeneralSetupCompleted;
    public bool IsGeneralSetupCompleted => m_InitialPhaseEntered;

    [Header("BGM")]
    [SerializeField, Tooltip("AudioCueLibrary 里配好的 BGM cue key；为空则不播放。")]
    private string m_BgmCueKey = "BGM";

    private bool m_InitialPhaseEntered;
    private bool m_LevelReady;
    private bool m_PlayerReady;
    private bool m_SetupInProgress;
    private bool m_ShowEntitySubscribed;
    private LevelEntity m_LevelEntity;
    private Stopwatch m_SetupStopwatch;

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
        m_SetupStopwatch = Stopwatch.StartNew();
        LogSetupTiming("begin");

        var lvRow = GetLvRow(lvIdentifier);
        if (lvRow == null)
        {
            Log.Error("[GeneralSetup] Level row not found. levelIdentifier={0}", lvIdentifier);
            m_SetupInProgress = false;
            m_SetupStopwatch = null;
            return;
        }
        LogSetupTiming("level-row-resolved");

        if (!m_ShowEntitySubscribed)
        {
            GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnGeneralShowEntitySuccess);
            m_ShowEntitySubscribed = true;
        }
        LogSetupTiming("show-entity-subscribed");

        var lvData = LevelData.FromRow(lvRow);
        DataModelSetup(lvData);
        GameEntry.GetComponent<GameEndManager>().Init(lvData);
        LogSetupTiming("data-model-ready");
        LevelEntityFactory.ShowLevel(lvRow.PrefabPath);
        LogSetupTiming("show-level-requested");

        var inputManager = GameEntry.GetComponent<InputManager>();
        if (inputManager != null)
        {
            inputManager.FindModel();
            inputManager.ChangeState(InputState.UIForm);
        }
        LogSetupTiming("input-ready");

        BootstrapSideTipsManager();
        GF.UI.OpenUIForm(UIViews.SideTipsUIForm);
        GF.UI.OpenUIForm(UIViews.GoalUIForm);
        LogSetupTiming("setup-ui-requested");

        PlayBgm();
        LogSetupTiming("setup-request-complete");
    }

    private void PlayBgm()
    {
        if (string.IsNullOrWhiteSpace(m_BgmCueKey)) return;
        if (AudioManager.Instance == null)
        {
            Log.Warning("[GeneralSetup] AudioManager.Instance 为空，跳过 BGM 播放。");
            return;
        }
        AudioManager.Instance.Play(m_BgmCueKey);
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
        DataModelShutDown();
        m_InitialPhaseEntered = false;
        m_LevelReady = false;
        m_PlayerReady = false;
        m_SetupInProgress = false;
        m_SetupStopwatch = null;
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

        GF.DataModel.CreateDataModel<SkillRuntimeDataModel>();
        GF.DataModel.CreateDataModel<InputModel>();
    }

    private void DataModelShutDown()
    {
        if (GF.DataModel == null)
        {
            return;
        }

        GF.DataModel.ReleaseDataModel<InGameDataModel>();
        GF.DataModel.ReleaseDataModel<SkillRuntimeDataModel>();
        GF.DataModel.ReleaseDataModel<InputModel>();
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
            LogSetupTiming("level-entity-shown");
            if (m_LevelEntity.IsRuntimeInitializationCompleted)
            {
                m_LevelReady = true;
                LogSetupTiming("level-already-runtime-ready");
            }
            else
            {
                m_LevelEntity.RuntimeInitializationCompleted += OnLevelRuntimeInitializationCompleted;
                LogSetupTiming("level-runtime-waiting");
            }

            TryEnterInitialPhaseIfReady();
        }
        else
        {
            LevelSelectionService.HideEntityRenderersDuringLoad(args.Entity.Logic);
        }

        if (args.Entity.Logic is MAEntity ma)
        {
            // 玩家注册为 Player
            if (ma.Brain is PlayerBrain)
            {
                m_PlayerReady = true;
                LogSetupTiming("player-ready");

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
                if (creature.CreaturePropertyManager == null)
                {
                    throw new InvalidOperationException(
                        $"GeneralSetup.OnGeneralShowEntitySuccess failed: creature OnShow did not complete. viewEntity={creature.Id}, logicEntity={ma.LogicEntityId.Value}.");
                }
                Fix64 max = creature.CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);

                // 根据单位的Side判断阵营，友方显示绿色血条，敌方显示红色血条
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

    private void OnLevelRuntimeInitializationCompleted(LevelEntity levelEntity)
    {
        if (m_LevelEntity != levelEntity)
        {
            return;
        }

        m_LevelEntity.RuntimeInitializationCompleted -= OnLevelRuntimeInitializationCompleted;
        m_LevelReady = true;
        LogSetupTiming("level-runtime-ready");
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

        InGameDataModel inGameData = GF.DataModel?.GetDataModel<InGameDataModel>()
                                     ?? throw new System.InvalidOperationException("GeneralSetup requires InGameDataModel before entering the initial phase.");
        string levelId = inGameData.lvData?.Identifier;
        if (string.IsNullOrWhiteSpace(levelId))
            throw new System.InvalidOperationException("GeneralSetup requires a stable level id for stage checkpoints.");
        StageCheckpointRuntimeCoordinator.BeginSession(levelId);
        PhaseManager.EnterCurrentPhaseOnGameStart();
        m_InitialPhaseEntered = true;
        m_SetupInProgress = false;
        BootstrapSideTipsManager();
        Log.Info("[GeneralSetup] Core runtime systems are ready.");
        LogSetupTiming("completed");
        OnGeneralSetupCompleted?.Invoke();
    }

    private void LogSetupTiming(string stage)
    {
        double elapsedMs = m_SetupStopwatch != null ? m_SetupStopwatch.Elapsed.TotalMilliseconds : 0.0;
        Log.Info(
            "[GeneralSetupTiming] stage={0} elapsedMs={1:F3} levelReady={2} playerReady={3} setupInProgress={4}",
            stage,
            elapsedMs,
            m_LevelReady,
            m_PlayerReady,
            m_SetupInProgress);
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
