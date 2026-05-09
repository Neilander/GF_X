using GameFramework;
using GameFramework.Event;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public enum TutorialType
{
    None = 0,
    MoveHeroByWASD = 1,
    InvadeSH = 2,
    SwitchPhase = 3,
    Build = 4,
    SwitchPhase2 = 5,
    PlayCard = 6,
}

public class TutorialManager : GameFrameworkComponent
{
    public static event Action PhaseSwitchButtonGuideChanged;

    private const string Lv1Identifier = "Lv_1";
    private const string MoveHeroTipId = "tutorial.move.hero.wasd";
    private const string InvadeSHTipId = "tutorial.invade.sh";
    private const string SwitchPhaseTipId = "tutorial.switch.phase";
    private const string BuildTipId = "tutorial.build";
    private const string SwitchPhase2TipId = "tutorial.switch.phase2";
    private const string PlayCardTipId = "tutorial.play.card";

    private const string MoveHeroTextId = "Tutorial_MoveHero";
    private const string InvadeSHTextId = "Tutorial_InvadeSH";
    private const string SwitchPhaseTextId = "Tutorial_SwitchPhase";
    private const string BuildTextId = "Tutorial_Build";
    private const string SwitchPhase2TextId = "Tutorial_SwitchPhase2";
    private const string PlayCardTextId = "Tutorial_PlayCard";

    private const float MoveInputThreshold = 0.1f;

    private static TutorialManager s_CachedManager;

    private InputModel inputModel;
    private readonly HashSet<TutorialType> activeTutorials = new HashSet<TutorialType>();
    private readonly List<TutorialType> activeTutorialsBuffer = new List<TutorialType>(8);
    private readonly HashSet<TutorialType> completedTutorials = new HashSet<TutorialType>();
    private bool hasLoggedWaitingForInputModel;
    private bool m_EventSubscribed;
    private bool m_WaitingEventReadyLogged;
    private int m_BuildTutorialStartBuiltCount;
    private string m_InvadeTutorialStrongholdId;

    protected override void Awake()
    {
        base.Awake();
        s_CachedManager = this;
    }

    private void Start()
    {
        TrySubscribeEvents();
    }

    private void OnEnable()
    {
        TrySubscribeEvents();
    }

    private void OnDisable()
    {
        TryUnsubscribeEvents();
    }

    private void OnDestroy()
    {
        TryUnsubscribeEvents();
        if (s_CachedManager == this)
            s_CachedManager = null;
    }

    private void Update()
    {
        if (!m_EventSubscribed)
            TrySubscribeEvents();

        if (activeTutorials.Count <= 0)
            return;

        activeTutorialsBuffer.Clear();
        activeTutorialsBuffer.AddRange(activeTutorials);

        for (int i = 0; i < activeTutorialsBuffer.Count; i++)
        {
            TickTutorial(activeTutorialsBuffer[i]);
        }
    }

    public bool NotifyTriggerEntered(TutorialType triggerType, Component triggerSource = null)
    {
        switch (triggerType)
        {
            case TutorialType.MoveHeroByWASD:
            case TutorialType.InvadeSH:
                return TryStartTutorial(triggerType, triggerSource, startedByChain: false);
            default:
                Log.Warning("[Tutorial] Unsupported trigger type: {0}.", triggerType);
                return false;
        }
    }

    public static bool TryGetPhaseSwitchButtonGuide(out bool interactable, out bool shouldBlink)
    {
        interactable = true;
        shouldBlink = false;

        return GameEntry.GetComponent<TutorialManager>().TryGetPhaseSwitchButtonGuideInternal(out interactable, out shouldBlink);
    }

    public static bool IsInvadeTutorialMovementBlocked(Vector3 worldPosition)
    {
        TutorialManager manager = s_CachedManager;
        if (manager == null)
            return false;

        return manager.IsInvadeTutorialMovementBlockedInternal(worldPosition);
    }

    public void ResetMoveTutorialForDebug()
    {
        ResetAllTutorialsForDebug();
    }

    public void ResetAllTutorialsForDebug()
    {
        activeTutorialsBuffer.Clear();
        foreach (var triggerType in activeTutorials)
        {
            if (TryGetTutorialTipConfig(triggerType, out string tipId, out _))
                RequestCloseSideTip(tipId);
        }

        activeTutorials.Clear();
        completedTutorials.Clear();
        inputModel = null;
        hasLoggedWaitingForInputModel = false;
        m_BuildTutorialStartBuiltCount = 0;
        m_InvadeTutorialStrongholdId = null;
        NotifyPhaseSwitchButtonGuideChanged();
    }

    private void TickTutorial(TutorialType triggerType)
    {
        switch (triggerType)
        {
            case TutorialType.MoveHeroByWASD:
                TickMoveHeroTutorial();
                break;
            case TutorialType.Build:
                TickBuildTutorial();
                break;
            case TutorialType.InvadeSH:
                TickInvadeTutorial();
                break;
            case TutorialType.SwitchPhase:
            case TutorialType.SwitchPhase2:
                break;
            case TutorialType.PlayCard:
                TickPlayCardTutorial();
                break;
            default:
                Log.Warning("[Tutorial] Unknown active tutorial trigger: {0}.", triggerType);
                activeTutorials.Remove(triggerType);
                break;
        }
    }

    private bool TryStartTutorial(TutorialType triggerType, Component triggerSource, bool startedByChain)
    {
        if (completedTutorials.Contains(triggerType))
            return false;

        if (activeTutorials.Contains(triggerType))
            return false;

        if (IsLv1FlowTutorial(triggerType) && !IsCurrentLevelLv1())
            return false;

        SideTipsManager sideTipsManager = GameEntry.GetComponent<SideTipsManager>();
        if (sideTipsManager == null)
        {
            Log.Warning("[Tutorial] Start tutorial failed: SideTipsManager is missing. type={0}.", triggerType);
            return false;
        }

        if (!TryGetTutorialTipConfig(triggerType, out string tipId, out string textId))
        {
            Log.Warning("[Tutorial] Start tutorial failed: unsupported type={0}.", triggerType);
            return false;
        }

        string tipContent = LocalizationTextDataModel.GetText(textId);
        if (string.Equals(tipContent, textId, StringComparison.Ordinal))
            tipContent = string.Empty;

        sideTipsManager.ShowConditionalTip(tipId, string.Empty, tipContent);
        activeTutorials.Add(triggerType);

        if (triggerType == TutorialType.Build)
            m_BuildTutorialStartBuiltCount = CountPlayerBuiltBuildings();

        if (triggerType == TutorialType.InvadeSH)
            TryBindInvadeTutorialStronghold(triggerSource);

        string sourceName = triggerSource != null ? triggerSource.name : "Auto";
        Log.Info("[Tutorial] Tutorial started. type={0}, trigger={1}, chain={2}.", triggerType, sourceName, startedByChain);
        NotifyPhaseSwitchButtonGuideChanged();
        return true;
    }

    private void TickMoveHeroTutorial()
    {
        if (!TryGetInputModel(out InputModel model))
            return;

        Fix64 inputThreshold = (Fix64)Mathf.Max(0f, MoveInputThreshold);
        bool hasMovementInput = Fix64.Abs(model.MoveX) > inputThreshold || Fix64.Abs(model.MoveY) > inputThreshold;
        if (!hasMovementInput)
            return;

        CompleteTutorial(TutorialType.MoveHeroByWASD, autoChain: false);
    }

    private void TickInvadeTutorial()
    {
        if (!string.IsNullOrEmpty(m_InvadeTutorialStrongholdId))
            return;

        TryBindInvadeTutorialStronghold(null);
    }

    private void TickBuildTutorial()
    {
        int currentBuiltCount = CountPlayerBuiltBuildings();
        if (currentBuiltCount < m_BuildTutorialStartBuiltCount + 2)
            return;

        CompleteTutorial(TutorialType.Build, autoChain: true);
    }

    private void TickPlayCardTutorial()
    {
        if (EntityRegistry.Player is not SoldierEntity player || !player.IsGhostState)
            return;

        CompleteTutorial(TutorialType.PlayCard, autoChain: false);
    }

    private void CompleteTutorial(TutorialType triggerType, bool autoChain)
    {
        if (!activeTutorials.Remove(triggerType))
            return;

        completedTutorials.Add(triggerType);
        hasLoggedWaitingForInputModel = false;

        if (TryGetTutorialTipConfig(triggerType, out string tipId, out _))
            RequestCloseSideTip(tipId);

        if (triggerType == TutorialType.InvadeSH)
            m_InvadeTutorialStrongholdId = null;

        Log.Info("[Tutorial] Tutorial completed. type={0}.", triggerType);

        if (autoChain)
            TryStartNextTutorial(triggerType);

        NotifyPhaseSwitchButtonGuideChanged();
    }

    private void TryStartNextTutorial(TutorialType completedType)
    {
        switch (completedType)
        {
            case TutorialType.InvadeSH:
                TryStartTutorial(TutorialType.SwitchPhase, null, startedByChain: true);
                break;
            case TutorialType.SwitchPhase:
                TryStartTutorial(TutorialType.Build, null, startedByChain: true);
                break;
            case TutorialType.Build:
                TryStartTutorial(TutorialType.SwitchPhase2, null, startedByChain: true);
                break;
            case TutorialType.SwitchPhase2:
                TryStartTutorial(TutorialType.PlayCard, null, startedByChain: true);
                break;
        }
    }

    private void OnEntityFactionChanged(object sender, GameEventArgs e)
    {
        if (!activeTutorials.Contains(TutorialType.InvadeSH))
            return;

        var args = e as EntityFactionChangedEventArgs;
        if (args == null)
            return;

        if (args.NewFactionId != EntitySideHelper.PlayerFactionId)
            return;

        if (args.OldFactionId == EntitySideHelper.PlayerFactionId)
            return;

        CompleteTutorial(TutorialType.InvadeSH, autoChain: true);
    }

    private void OnIngamePhaseChanged(object sender, GameEventArgs e)
    {
        if (activeTutorials.Contains(TutorialType.SwitchPhase))
        {
            CompleteTutorial(TutorialType.SwitchPhase, autoChain: true);
            return;
        }

        if (activeTutorials.Contains(TutorialType.SwitchPhase2))
        {
            CompleteTutorial(TutorialType.SwitchPhase2, autoChain: true);
        }
    }

    private void OnGameEndResult(object sender, GameEventArgs e)
    {
        if (!activeTutorials.Contains(TutorialType.PlayCard))
            return;

        CompleteTutorial(TutorialType.PlayCard, autoChain: false);
    }

    private void TrySubscribeEvents()
    {
        if (m_EventSubscribed)
            return;

        if (GF.Event == null)
        {
            if (!m_WaitingEventReadyLogged)
            {
                m_WaitingEventReadyLogged = true;
                Log.Warning("[Tutorial] Waiting for GF.Event to become ready...");
            }

            return;
        }

        GF.Event.Subscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
        GF.Event.Subscribe(GameEndResultEventArgs.EventId, OnGameEndResult);
        m_EventSubscribed = true;
        m_WaitingEventReadyLogged = false;
    }

    private void TryUnsubscribeEvents()
    {
        if (!m_EventSubscribed)
            return;

        if (GF.Event != null)
        {
            try
            {
                GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
                GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
                GF.Event.Unsubscribe(GameEndResultEventArgs.EventId, OnGameEndResult);
            }
            catch (GameFrameworkException)
            {
                // PlayMode 退出时 EventPool 可能已释放，忽略退订异常。
            }
        }

        m_EventSubscribed = false;
    }

    private bool TryGetPhaseSwitchButtonGuideInternal(out bool interactable, out bool shouldBlink)
    {
        interactable = true;
        shouldBlink = false;

        if (!IsCurrentLevelLv1())
            return false;

        if (completedTutorials.Contains(TutorialType.PlayCard))
            return false;

        bool isSwitchPhaseTutorialActive = activeTutorials.Contains(TutorialType.SwitchPhase)
            || activeTutorials.Contains(TutorialType.SwitchPhase2);

        interactable = isSwitchPhaseTutorialActive;
        shouldBlink = isSwitchPhaseTutorialActive;
        return true;
    }

    private static void NotifyPhaseSwitchButtonGuideChanged()
    {
        PhaseSwitchButtonGuideChanged?.Invoke();
    }

    private static bool TryGetTutorialTipConfig(TutorialType triggerType, out string tipId, out string textId)
    {
        tipId = null;
        textId = null;

        switch (triggerType)
        {
            case TutorialType.MoveHeroByWASD:
                tipId = MoveHeroTipId;
                textId = MoveHeroTextId;
                return true;
            case TutorialType.InvadeSH:
                tipId = InvadeSHTipId;
                textId = InvadeSHTextId;
                return true;
            case TutorialType.SwitchPhase:
                tipId = SwitchPhaseTipId;
                textId = SwitchPhaseTextId;
                return true;
            case TutorialType.Build:
                tipId = BuildTipId;
                textId = BuildTextId;
                return true;
            case TutorialType.SwitchPhase2:
                tipId = SwitchPhase2TipId;
                textId = SwitchPhase2TextId;
                return true;
            case TutorialType.PlayCard:
                tipId = PlayCardTipId;
                textId = PlayCardTextId;
                return true;
            default:
                return false;
        }
    }

    private static bool IsLv1FlowTutorial(TutorialType triggerType)
    {
        return triggerType == TutorialType.InvadeSH
            || triggerType == TutorialType.SwitchPhase
            || triggerType == TutorialType.Build
            || triggerType == TutorialType.SwitchPhase2
            || triggerType == TutorialType.PlayCard;
    }

    private bool IsInvadeTutorialMovementBlockedInternal(Vector3 worldPosition)
    {
        if (!activeTutorials.Contains(TutorialType.InvadeSH))
            return false;

        if (string.IsNullOrEmpty(m_InvadeTutorialStrongholdId))
            TryBindInvadeTutorialStronghold(null);

        if (string.IsNullOrEmpty(m_InvadeTutorialStrongholdId))
            return false;

        Stronghold stronghold = TryGetStrongholdAtWorldPosition(worldPosition);
        return stronghold == null
            || stronghold.strongholdData == null
            || !string.Equals(stronghold.strongholdData.StrongholdId, m_InvadeTutorialStrongholdId, StringComparison.Ordinal);
    }

    private void TryBindInvadeTutorialStronghold(Component triggerSource)
    {
        if (!string.IsNullOrEmpty(m_InvadeTutorialStrongholdId))
            return;

        if (TryResolveStrongholdId(EntityRegistry.Player?.Position, out string playerStrongholdId))
        {
            m_InvadeTutorialStrongholdId = playerStrongholdId;
            Log.Info("[Tutorial] Invade tutorial stronghold locked. id={0}, source=Player.", playerStrongholdId);
            return;
        }

        if (triggerSource != null && TryResolveStrongholdId(triggerSource.transform.position, out string triggerStrongholdId))
        {
            m_InvadeTutorialStrongholdId = triggerStrongholdId;
            Log.Info("[Tutorial] Invade tutorial stronghold locked. id={0}, source={1}.", triggerStrongholdId, triggerSource.name);
        }
    }

    private static bool TryResolveStrongholdId(Vector3? worldPosition, out string strongholdId)
    {
        strongholdId = null;
        if (!worldPosition.HasValue)
            return false;

        Stronghold stronghold = TryGetStrongholdAtWorldPosition(worldPosition.Value);
        if (stronghold == null || stronghold.strongholdData == null || string.IsNullOrEmpty(stronghold.strongholdData.StrongholdId))
            return false;

        strongholdId = stronghold.strongholdData.StrongholdId;
        return true;
    }

    private static Stronghold TryGetStrongholdAtWorldPosition(Vector3 worldPosition)
    {
        if (LevelEntity.ActiveLevelEntity == null)
            return null;

        return LevelEntity.GetStrongholdAtWorldPosition(worldPosition);
    }

    private static bool IsCurrentLevelLv1()
    {
        var inGameData = GF.DataModel != null ? GF.DataModel.GetDataModel<InGameDataModel>() : null;
        if (inGameData == null || inGameData.lvData == null)
            return false;

        return string.Equals(inGameData.lvData.Identifier, Lv1Identifier, StringComparison.Ordinal);
    }

    private static int CountPlayerBuiltBuildings()
    {
        var inGameData = GF.DataModel != null ? GF.DataModel.GetDataModel<InGameDataModel>() : null;
        if (inGameData == null)
            return 0;

        int count = 0;
        foreach (var building in inGameData.Buildings)
        {
            if (building == null || building.buildingData == null)
                continue;

            if (building.OwnerFactionID != EntitySideHelper.PlayerFactionId)
                continue;

            if (building.buildingData.Lv <= 0)
                continue;

            count++;
        }

        return count;
    }

    private void RequestCloseSideTip(string tipId)
    {
        if (GF.Event != null)
        {
            GF.Event.Fire(this, CloseSideTipEventArgs.Create(tipId));
            return;
        }

        SideTipsManager sideTipsManager = GameEntry.GetComponent<SideTipsManager>();
        if (sideTipsManager != null)
        {
            sideTipsManager.CloseConditionalTip(tipId);
        }
    }

    private bool TryGetInputModel(out InputModel model)
    {
        if (inputModel == null)
        {
            if (GF.DataModel == null || !GF.DataModel.HasDataModel<InputModel>())
            {
                if (!hasLoggedWaitingForInputModel)
                {
                    hasLoggedWaitingForInputModel = true;
                    Log.Warning("[Tutorial] InputModel is not ready yet.");
                }

                model = null;
                return false;
            }

            inputModel = GF.DataModel.GetDataModel<InputModel>();
        }

        hasLoggedWaitingForInputModel = false;
        model = inputModel;
        return model != null;
    }
}
