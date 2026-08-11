using GameFramework;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public enum TutorialType
{
    None = 0,
    FriendlyStronghold = 1,
    EnemyStronghold = 2,
}

public enum TutorialStage
{
    Inactive = 0,
    ReachFriendlyStronghold = 1,
    FirstDefense = 2,
    AwaitFirstBuildPhase = 3,
    BuildMilitaryAndDefense = 4,
    AwaitInvadePhase = 5,
    CaptureEnemyStronghold = 6,
    AwaitCapturedBuildPhase = 7,
    BuildProductionAndResearch = 8,
    AwaitDefensePhase = 9,
    DefendBase = 10,
    UpgradeCore = 11,
    Completed = 12,
}

public class TutorialManager : GameFrameworkComponent, ILogicFrameUpdate, ILogicFrameStableOrder
{
    public static event Action PhaseSwitchButtonGuideChanged;

    private const string LevelIdentifier = "Lv_1";
    private const int DevelopmentCoinGrant = 8;
    private const int CoreUpgradeCoinGrant = 40;
    private const string GoalReach = "reach-friendly";
    private const string GoalProtect = "protect-stronghold";
    private const string GoalArmy = "build-army";
    private const string GoalDefense = "build-defense";
    private const string GoalCapture = "capture-stronghold";
    private const string GoalProduction = "build-production";
    private const string GoalResearchBuilding = "build-research";
    private const string GoalResearchTech = "research-tech";
    private const string GoalDefendBase = "defend-base";
    private const string GoalUpgradeCore = "upgrade-core";

    private static TutorialManager s_Current;

    private enum PresentationKind
    {
        ShowTip,
        CloseTip,
        PhaseGuideChanged,
    }

    private readonly struct PresentationRequest
    {
        public PresentationRequest(PresentationKind kind, string identifier)
        {
            Kind = kind;
            Identifier = identifier;
        }

        public PresentationKind Kind { get; }
        public string Identifier { get; }
    }

    private readonly Queue<PresentationRequest> m_PresentationRequests = new Queue<PresentationRequest>();
    private readonly HashSet<string> m_ActiveTipIds = new HashSet<string>(StringComparer.Ordinal);
    private InputModel m_InputModel;
    private bool m_EventsSubscribed;
    private bool m_LogicFrameRegistered;
    private bool m_EnemyStrongholdIntroShown;
    private bool m_UpgradeControlsShown;
    private bool m_CoreFirstUpgradeCompleted;
    private int m_ArmyBaseline;
    private int m_DefenseBaseline;
    private int m_ProductionBaseline;
    private int m_ResearchBuildingBaseline;
    private int m_ResearchCommandBaseline;
    private int m_CoreStartLevel;
    private string m_CapturedStrongholdId;
    private string m_CapturedCoreBuildingInstanceId;
    private ulong m_LastLogicFrame;

    public int LogicFrameOrder => 1000;
    public long LogicFrameStableKey => 0;
    public TutorialStage Stage { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        if (s_Current != null && !ReferenceEquals(s_Current, this))
            throw new InvalidOperationException("Only one TutorialManager can be active.");
        s_Current = this;
    }

    private void OnEnable()
    {
        LevelSelectionService.LevelLoadStarted += OnLevelLoadStarted;
        LogicFrameRuntime.Began += OnLogicFrameRuntimeBegan;
        LogicFrameRuntime.Ending += OnLogicFrameRuntimeEnding;
        SubscribeEvents();
        if (LogicFrameRuntime.IsActive)
            RegisterLogicFrame();
    }

    private void OnDisable()
    {
        LevelSelectionService.LevelLoadStarted -= OnLevelLoadStarted;
        LogicFrameRuntime.Began -= OnLogicFrameRuntimeBegan;
        LogicFrameRuntime.Ending -= OnLogicFrameRuntimeEnding;
        if (m_LogicFrameRegistered)
            UnregisterLogicFrame();
        UnsubscribeEvents();
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(s_Current, this))
            s_Current = null;
    }

    public void PrepareRuntimeDependencies(InputModel inputModel)
    {
        m_InputModel = inputModel ?? throw new ArgumentNullException(nameof(inputModel));
    }

    public void UpdatePresentation()
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("Tutorial presentation cannot run during a logic frame.");
        while (m_PresentationRequests.Count > 0)
        {
            PresentationRequest request = m_PresentationRequests.Dequeue();
            switch (request.Kind)
            {
                case PresentationKind.ShowTip:
                    RequireSideTipsManager().ShowTip(request.Identifier);
                    break;
                case PresentationKind.CloseTip:
                    RequireSideTipsManager().CloseTip(request.Identifier);
                    break;
                case PresentationKind.PhaseGuideChanged:
                    PhaseSwitchButtonGuideChanged?.Invoke();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request.Kind), request.Kind, "Unknown tutorial presentation request.");
            }
        }

        TutorialObjectiveService.PublishPendingPresentation(this);
    }

    public void OnLogicFrameUpdate(Fix64 deltaTime)
    {
        if (deltaTime != LogicFrameRuntime.FixedDeltaTime)
            throw new InvalidOperationException("TutorialManager received a non-fixed logic delta.");
        if (LogicFrameRuntime.CurrentFrame == 0)
            throw new InvalidOperationException("TutorialManager cannot update on logic frame zero.");

        m_LastLogicFrame = LogicFrameRuntime.CurrentFrame;
        if (!IsCurrentLevelTutorial())
            return;

        if (Stage == TutorialStage.Inactive)
        {
            InitializeTutorial();
            return;
        }

        switch (Stage)
        {
            case TutorialStage.ReachFriendlyStronghold:
                TickReachFriendlyStronghold();
                break;
            case TutorialStage.BuildMilitaryAndDefense:
                TickFirstBuildGoals();
                break;
            case TutorialStage.CaptureEnemyStronghold:
                TickEnemyStrongholdEntry();
                break;
            case TutorialStage.BuildProductionAndResearch:
                TickDevelopmentGoals();
                break;
            case TutorialStage.UpgradeCore:
                TickCoreUpgrade();
                break;
        }
    }

    public bool NotifyTriggerEntered(TutorialType triggerType, Component triggerSource = null)
    {
        if (!IsCurrentLevelTutorial())
            return false;

        switch (triggerType)
        {
            case TutorialType.FriendlyStronghold:
            case TutorialType.EnemyStronghold:
                return false;
            default:
                throw new ArgumentOutOfRangeException(nameof(triggerType), triggerType, "Unknown tutorial trigger type.");
        }
    }

    public static bool IsCurrentLevelTutorial()
    {
        return string.Equals(LevelSelectionService.SelectedLevelIdentifier, LevelIdentifier, StringComparison.Ordinal);
    }

    public static bool IsManualDefendPhaseSwitchAllowed()
    {
        return IsCurrentLevelTutorial()
               && s_Current != null
               && s_Current.Stage == TutorialStage.AwaitFirstBuildPhase;
    }

    public static bool TryGetPhaseSwitchButtonGuide(out bool interactable, out bool shouldBlink)
    {
        interactable = true;
        shouldBlink = false;
        if (!IsCurrentLevelTutorial())
            return false;

        TutorialManager manager = RequireCurrent();
        bool guided = IsPhaseSwitchGuidedStage(manager.Stage);
        interactable = guided;
        shouldBlink = guided;
        return true;
    }

    public static bool IsConstructOptionAllowed(BuildingData target)
    {
        if (!IsCurrentLevelTutorial())
            return true;
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        return IsConstructTypeAllowed(RequireCurrent().Stage, target.Type);
    }

    public static bool IsUpgradeOptionAllowed(IBuildingLogicContext owner)
    {
        if (!IsCurrentLevelTutorial())
            return true;
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));

        TutorialManager manager = RequireCurrent();
        if (manager.Stage != TutorialStage.UpgradeCore)
            return false;
        if (manager.m_CoreFirstUpgradeCompleted)
            return true;
        return string.Equals(
            owner.BuildingInstanceId,
            manager.m_CapturedCoreBuildingInstanceId,
            StringComparison.Ordinal);
    }

    public static bool AreDefendPreviewArrowsAllowed()
    {
        return !IsCurrentLevelTutorial() || IsDefendPreviewAllowedStage(RequireCurrent().Stage);
    }

    public static bool IsPhaseSwitchGuidedStage(TutorialStage stage)
    {
        return stage == TutorialStage.AwaitFirstBuildPhase
               || stage == TutorialStage.AwaitInvadePhase
               || stage == TutorialStage.AwaitCapturedBuildPhase
               || stage == TutorialStage.AwaitDefensePhase;
    }

    public static bool IsConstructTypeAllowed(TutorialStage stage, BuilType type)
    {
        if (stage == TutorialStage.BuildMilitaryAndDefense)
            return type == BuilType.Army || type == BuilType.Def;
        if (stage == TutorialStage.BuildProductionAndResearch)
            return type == BuilType.Prod || type == BuilType.Tech;
        return false;
    }

    public static bool IsDefendPreviewAllowedStage(TutorialStage stage)
    {
        return stage == TutorialStage.AwaitDefensePhase;
    }

    public static TutorialStage GetCurrentStageForTests()
    {
        return s_Current != null ? s_Current.Stage : TutorialStage.Inactive;
    }

    public void ResetAllTutorialsForDebug()
    {
        CloseAllTips();
        TutorialObjectiveService.Reset();
        ObjectiveDestinationService.Clear();
        Stage = TutorialStage.Inactive;
        m_InputModel = null;
        m_EnemyStrongholdIntroShown = false;
        m_UpgradeControlsShown = false;
        m_CoreFirstUpgradeCompleted = false;
        m_ArmyBaseline = 0;
        m_DefenseBaseline = 0;
        m_ProductionBaseline = 0;
        m_ResearchBuildingBaseline = 0;
        m_ResearchCommandBaseline = 0;
        m_CoreStartLevel = 0;
        m_CapturedStrongholdId = null;
        m_CapturedCoreBuildingInstanceId = null;
        m_LastLogicFrame = 0;
        QueuePhaseGuideChanged();
    }

    public void ResetMoveTutorialForDebug()
    {
        ResetAllTutorialsForDebug();
    }

    private void InitializeTutorial()
    {
        if (m_InputModel == null)
            throw new InvalidOperationException("TutorialManager InputModel was not bound before logic frames began.");
        GamePhase currentPhase = LogicPhaseCommandService.GetRequiredCurrentPhase();
        if (currentPhase != GamePhase.Defend)
        {
            throw new InvalidOperationException(
                $"Level_1 tutorial must begin in Defend phase, actual={currentPhase}.");
        }
        if (!DefendPhaseRuntime.IsTutorialTriggeredFirstDefenseWaiting)
            throw new InvalidOperationException("Level_1 tutorial began before DefendPhaseRuntime entered its triggered waiting state.");
        if (EntityRegistry.Player == null || !EntityRegistry.Player.Alive)
            throw new InvalidOperationException("Level_1 tutorial requires a live player before its first logic frame.");

        Stage = TutorialStage.ReachFriendlyStronghold;
        ObjectiveDestinationService.Activate(0);
        TutorialObjectiveService.Replace(
            Objective(GoalReach, "ReachDestination"));
        ShowTip("TutorialGoFriendlyStronghold");
        ShowTip("TutorialMovement");
        QueuePhaseGuideChanged();
        Log.Info("[Tutorial] Initialized Level_1 tutorial. stage={0}.", Stage);
    }

    private void StartFirstDefense(string strongholdId)
    {
        TutorialObjectiveService.SetStatus(GoalReach, TutorialObjectiveStatus.Completed);
        ObjectiveDestinationService.Clear();
        CloseAllTips();
        Stage = TutorialStage.FirstDefense;
        TutorialObjectiveService.Replace(
            Objective(GoalProtect, "ProtectStronghold"));
        ShowTip("TutorialEnemyAttack");
        DefendPhaseRuntime.StartTutorialTriggeredFirstDefense(strongholdId);
        QueuePhaseGuideChanged();
        Log.Info(
            "[Tutorial] First-defense destination reached; triggered defense for stronghold={0}.",
            strongholdId);
    }

    private void TickReachFriendlyStronghold()
    {
        IEntityContext player = EntityRegistry.Player;
        if (player == null)
            return;
        if (!player.Alive || player.Side != SideType.PlayerSide)
            throw new InvalidOperationException("Tutorial friendly-stronghold detection requires a live player-side entity.");
        if (!ObjectiveDestinationService.IsReached(player.PositionFixed))
            return;

        string strongholdId = ResolveFirstDefenseStrongholdId(ObjectiveDestinationService.ActivePosition);
        StartFirstDefense(strongholdId);
    }

    private static string ResolveFirstDefenseStrongholdId(FixVector2 destinationPosition)
    {
        if (!LogicStrongholdMap.TryResolveStrongholdId(destinationPosition, out string strongholdId))
            throw new InvalidOperationException("Tutorial first-defense destination is outside every stronghold.");
        if (LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId) != EntitySideHelper.PlayerFactionId)
            throw new InvalidOperationException(
                $"Tutorial first-defense destination stronghold '{strongholdId}' is not player owned.");
        return strongholdId;
    }

    private void TickEnemyStrongholdEntry()
    {
        if (m_EnemyStrongholdIntroShown)
            return;

        IEntityContext player = EntityRegistry.Player;
        if (player == null)
            return;
        if (!player.Alive || player.Side != SideType.PlayerSide)
            throw new InvalidOperationException("Tutorial enemy-stronghold detection requires a live player-side entity.");
        if (!LogicStrongholdMap.TryResolveStrongholdId(player.PositionFixed, out string strongholdId))
            return;
        if (LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId) == EntitySideHelper.PlayerFactionId)
            return;

        LogicMovementRegionConstraintService.SetTutorialStrongholdBoundary(strongholdId);
        OnTutorialStrongholdBoundaryActivated(strongholdId);
    }

    private void OnTutorialTriggeredDefenseCleared()
    {
        if (!IsCurrentLevelTutorial())
            return;
        if (Stage != TutorialStage.FirstDefense)
            throw new InvalidOperationException($"Tutorial defense cleared in unexpected stage {Stage}.");

        TutorialObjectiveService.SetStatus(GoalProtect, TutorialObjectiveStatus.Completed);
        CloseAllTips();
        Stage = TutorialStage.AwaitFirstBuildPhase;
        m_ArmyBaseline = CountPlayerBuildings(BuilType.Army);
        m_DefenseBaseline = CountPlayerBuildings(BuilType.Def);
        TutorialObjectiveService.Replace(
            Objective(GoalArmy, "BuildMilitaryBuilding", 1),
            Objective(GoalDefense, "BuildDefenseBuilding", 1));
        ShowTip("TutorialFirstDefenseComplete");
        ShowTip("TutorialSwitchToBuild");
        QueuePhaseGuideChanged();
    }

    private void TickFirstBuildGoals()
    {
        bool armyComplete = CountPlayerBuildings(BuilType.Army) > m_ArmyBaseline;
        bool defenseComplete = CountPlayerBuildings(BuilType.Def) > m_DefenseBaseline;
        SetObjectiveCompletion(GoalArmy, armyComplete);
        SetObjectiveCompletion(GoalDefense, defenseComplete);
        if (!armyComplete || !defenseComplete)
            return;

        CloseAllTips();
        Stage = TutorialStage.AwaitInvadePhase;
        TutorialObjectiveService.Replace(
            Objective(GoalCapture, "CaptureStrongholdCount", 1));
        ShowTip("TutorialGoInvade");
        ShowTip("TutorialSwitchToInvade");
        QueuePhaseGuideChanged();
    }

    private void TickDevelopmentGoals()
    {
        bool productionComplete = CountPlayerBuildings(BuilType.Prod) > m_ProductionBaseline;
        bool researchBuildingComplete = CountPlayerBuildings(BuilType.Tech) > m_ResearchBuildingBaseline;
        bool techComplete = CountAppliedResearchCommands() > m_ResearchCommandBaseline;
        SetObjectiveCompletion(GoalProduction, productionComplete);
        SetObjectiveCompletion(GoalResearchBuilding, researchBuildingComplete);
        SetObjectiveCompletion(GoalResearchTech, techComplete);
        if (!productionComplete || !researchBuildingComplete || !techComplete)
            return;

        CloseAllTips();
        Stage = TutorialStage.AwaitDefensePhase;
        TutorialObjectiveService.Replace(
            Objective(GoalDefendBase, "DefendBase"));
        ShowTip("TutorialEnemyWarning");
        ShowTip("TutorialSwitchToDefend");
        QueuePhaseGuideChanged();
    }

    private void TickCoreUpgrade()
    {
        IBuildingLogicContext core = FindCapturedCoreRequired();
        int coreLevel = core.BuildingData?.Lv
            ?? throw new InvalidOperationException("Captured tutorial core has no BuildingData.");

        if (!m_UpgradeControlsShown && EntityRegistry.Player != null)
        {
            FixVector2 offset = EntityRegistry.Player.PositionFixed - core.PositionFixed;
            Fix64 distanceSquared = FixVector2.SqrMagnitude(offset);
            Fix64 tipDistance = (Fix64)5;
            if (distanceSquared <= tipDistance * tipDistance)
            {
                m_UpgradeControlsShown = true;
                CloseAllTips();
                ShowTip("TutorialUpgradeControls");
            }
        }

        if (!m_CoreFirstUpgradeCompleted && coreLevel > m_CoreStartLevel)
        {
            m_CoreFirstUpgradeCompleted = true;
            CloseAllTips();
            ShowTip("TutorialFreePlay");
            Log.Info("[Tutorial] Core upgraded once; all building upgrade options unlocked.");
        }

        if (coreLevel < 3)
            return;

        TutorialObjectiveService.SetStatus(GoalUpgradeCore, TutorialObjectiveStatus.Completed);
        CloseAllTips();
        Stage = TutorialStage.Completed;
        QueuePhaseGuideChanged();
        LogicGameEndService.CompleteScriptedObjective(LevelObjectiveIdentifiers.UpgradeCodingCoreLevel3);
    }

    private void OnLogicPhaseApplied(GamePhase oldPhase, GamePhase newPhase)
    {
        if (!IsCurrentLevelTutorial())
            return;

        switch (Stage)
        {
            case TutorialStage.AwaitFirstBuildPhase when newPhase == GamePhase.BuildBeforeInvade:
                CloseAllTips();
                Stage = TutorialStage.BuildMilitaryAndDefense;
                ShowTip("TutorialBuildControls");
                ShowTip("TutorialMercenaryBackground");
                QueuePhaseGuideChanged();
                break;
            case TutorialStage.AwaitInvadePhase when newPhase == GamePhase.Invade:
                CloseAllTips();
                Stage = TutorialStage.CaptureEnemyStronghold;
                ShowTip("TutorialCardSystem");
                QueuePhaseGuideChanged();
                break;
            case TutorialStage.AwaitCapturedBuildPhase when InGameDataModel.IsBuildPhase(newPhase):
                CloseAllTips();
                Stage = TutorialStage.BuildProductionAndResearch;
                GrantTutorialCoins(DevelopmentCoinGrant, "development");
                m_ProductionBaseline = CountPlayerBuildings(BuilType.Prod);
                m_ResearchBuildingBaseline = CountPlayerBuildings(BuilType.Tech);
                m_ResearchCommandBaseline = CountAppliedResearchCommands();
                ShowTip("TutorialProductionResearch");
                ShowTip("TutorialResearchControls");
                QueuePhaseGuideChanged();
                break;
            case TutorialStage.AwaitDefensePhase when newPhase == GamePhase.Defend:
                CloseAllTips();
                Stage = TutorialStage.DefendBase;
                QueuePhaseGuideChanged();
                break;
            case TutorialStage.DefendBase when InGameDataModel.IsBuildPhase(newPhase):
                TutorialObjectiveService.SetStatus(GoalDefendBase, TutorialObjectiveStatus.Completed);
                EnterCoreUpgradeStage();
                break;
        }
    }

    private void EnterCoreUpgradeStage()
    {
        CloseAllTips();
        Stage = TutorialStage.UpgradeCore;
        GrantTutorialCoins(CoreUpgradeCoinGrant, "core-upgrade");
        IBuildingLogicContext core = FindCapturedCoreRequired();
        m_CoreStartLevel = core.BuildingData?.Lv
            ?? throw new InvalidOperationException("Captured tutorial core has no BuildingData.");
        TutorialObjectiveService.Replace(
            Objective(GoalUpgradeCore, "UpgradeCodingCoreLevel3"));
        ShowTip("TutorialUpgradeMission");
        ShowTip("TutorialWorldMap");
        QueuePhaseGuideChanged();
    }

    private void OnTutorialStrongholdBoundaryActivated(string strongholdId)
    {
        if (!IsCurrentLevelTutorial())
            return;
        if (Stage != TutorialStage.CaptureEnemyStronghold)
            throw new InvalidOperationException($"Enemy stronghold trigger activated in unexpected tutorial stage {Stage}.");
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new InvalidOperationException("Enemy stronghold trigger returned an empty stronghold id.");
        if (m_EnemyStrongholdIntroShown)
            throw new InvalidOperationException("Enemy stronghold tutorial intro was triggered twice.");

        m_EnemyStrongholdIntroShown = true;
        m_CapturedStrongholdId = strongholdId;
        CloseAllTips();
        ShowTip("TutorialCodingIndustry");
        ShowTip("TutorialCaptureRule");
    }

    private void OnLogicBuildingOwnerFactionChanged(
        IBuildingLogicContext building,
        int oldFactionId,
        int newFactionId)
    {
        if (!IsCurrentLevelTutorial() || Stage != TutorialStage.CaptureEnemyStronghold)
            return;
        if (building == null)
            throw new ArgumentNullException(nameof(building));
        if (oldFactionId == EntitySideHelper.PlayerFactionId
            || newFactionId != EntitySideHelper.PlayerFactionId)
        {
            return;
        }
        BuildingData buildingData = building.BuildingData
                                    ?? throw new InvalidOperationException("Captured tutorial building has no BuildingData.");
        if (buildingData.Type != BuilType.Base)
            return;
        if (!m_EnemyStrongholdIntroShown)
            throw new InvalidOperationException("Tutorial captured a stronghold before its authored enemy-stronghold trigger.");
        if (!string.Equals(building.StrongholdId, m_CapturedStrongholdId, StringComparison.Ordinal))
            throw new InvalidOperationException("Tutorial captured a different stronghold than the one introduced by its trigger.");

        LogicGameEndService.RegisterCapturedBuildingAsPlayerTarget(building);
        m_CapturedCoreBuildingInstanceId = building.BuildingInstanceId;
        TutorialObjectiveService.SetStatus(GoalCapture, TutorialObjectiveStatus.Completed);
        CloseAllTips();
        Stage = TutorialStage.AwaitCapturedBuildPhase;
        TutorialObjectiveService.Replace(
            Objective(GoalProduction, "BuildProductionBuilding", 1),
            Objective(GoalResearchBuilding, "BuildResearchBuilding", 1),
            Objective(GoalResearchTech, "ResearchTechnologyCount", 1));
        ShowTip("TutorialOccupied");
        ShowTip("TutorialSwitchToDevelopment");
        QueuePhaseGuideChanged();
    }

    private void OnLogicGameEnded(LogicGameEndResult result)
    {
        if (!IsCurrentLevelTutorial())
            return;
        if (!result.IsWin)
            TutorialObjectiveService.FailActiveObjectives();
    }

    private void OnLevelLoadStarted()
    {
        ResetAllTutorialsForDebug();
    }

    private void SubscribeEvents()
    {
        if (m_EventsSubscribed)
            return;
        LogicBuildingOwnershipEventService.OwnerFactionChanged += OnLogicBuildingOwnerFactionChanged;
        LogicPhaseCommandService.PhaseApplied += OnLogicPhaseApplied;
        LogicGameEndService.GameEnded += OnLogicGameEnded;
        DefendPhaseRuntime.TutorialTriggeredDefenseCleared += OnTutorialTriggeredDefenseCleared;
        m_EventsSubscribed = true;
    }

    private void UnsubscribeEvents()
    {
        if (!m_EventsSubscribed)
            return;
        LogicBuildingOwnershipEventService.OwnerFactionChanged -= OnLogicBuildingOwnerFactionChanged;
        LogicPhaseCommandService.PhaseApplied -= OnLogicPhaseApplied;
        LogicGameEndService.GameEnded -= OnLogicGameEnded;
        DefendPhaseRuntime.TutorialTriggeredDefenseCleared -= OnTutorialTriggeredDefenseCleared;
        m_EventsSubscribed = false;
    }

    private void ShowTip(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Tutorial tip identifier is empty.", nameof(identifier));
        if (!m_ActiveTipIds.Add(identifier))
            throw new InvalidOperationException($"Tutorial tip '{identifier}' is already active.");
        m_PresentationRequests.Enqueue(new PresentationRequest(PresentationKind.ShowTip, identifier));
    }

    private void CloseAllTips()
    {
        if (m_ActiveTipIds.Count == 0)
            return;

        var identifiers = new List<string>(m_ActiveTipIds);
        identifiers.Sort(StringComparer.Ordinal);
        m_ActiveTipIds.Clear();
        for (int i = 0; i < identifiers.Count; i++)
            m_PresentationRequests.Enqueue(new PresentationRequest(PresentationKind.CloseTip, identifiers[i]));
    }

    private void QueuePhaseGuideChanged()
    {
        m_PresentationRequests.Enqueue(new PresentationRequest(PresentationKind.PhaseGuideChanged, null));
    }

    private static TutorialObjective Objective(
        string id,
        string definitionIdentifier,
        params object[] formatArgs)
    {
        return new TutorialObjective(
            id,
            definitionIdentifier,
            TutorialObjectiveStatus.Active,
            formatArgs);
    }

    private static void SetObjectiveCompletion(string id, bool completed)
    {
        TutorialObjectiveStatus desired = completed
            ? TutorialObjectiveStatus.Completed
            : TutorialObjectiveStatus.Active;
        if (TutorialObjectiveService.GetStatus(id) != desired)
            TutorialObjectiveService.SetStatus(id, desired);
    }

    private static void GrantTutorialCoins(int amount, string reason)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Tutorial coin grant must be positive.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Tutorial coin grant reason is empty.", nameof(reason));

        LogicInGameValueCommand command = LogicInGameValueCommandService.ScheduleDeltaForNextFrame(
            IngameValueType.Coin,
            amount);
        Log.Info(
            "[Tutorial] Scheduled coin grant. reason={0}, amount={1}, frame={2}.",
            reason,
            amount,
            command.EffectiveFrame);
    }

    private static int CountPlayerBuildings(BuilType type)
    {
        int count = 0;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!entities[i].TryGetLogicBuilding(out IBuildingLogicContext building)
                || building.BuildingData == null
                || building.BuildingData.Lv <= 0
                || building.OwnerFactionId != EntitySideHelper.PlayerFactionId
                || building.BuildingData.Type != type)
            {
                continue;
            }
            count++;
        }
        return count;
    }

    private static int CountAppliedResearchCommands()
    {
        int count = 0;
        IReadOnlyList<LogicInteractionCommand> history = LogicInteractionCommandService.History;
        for (int i = 0; i < history.Count; i++)
        {
            LogicInteractionCommand command = history[i];
            if (command.ActionKind == LogicInteractionActionKind.ResearchTech
                && command.EffectiveFrame <= LogicInteractionCommandService.LastAppliedFrame)
            {
                count++;
            }
        }
        return count;
    }

    private IBuildingLogicContext FindCapturedCoreRequired()
    {
        if (string.IsNullOrWhiteSpace(m_CapturedCoreBuildingInstanceId))
            throw new InvalidOperationException("Tutorial captured core building id is missing.");

        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i].TryGetLogicBuilding(out IBuildingLogicContext building)
                && string.Equals(
                    building.BuildingInstanceId,
                    m_CapturedCoreBuildingInstanceId,
                    StringComparison.Ordinal))
            {
                return building;
            }
        }
        throw new InvalidOperationException(
            $"Tutorial cannot resolve captured core '{m_CapturedCoreBuildingInstanceId}'.");
    }

    private static TutorialManager RequireCurrent()
    {
        return s_Current ?? throw new InvalidOperationException("TutorialManager is not active.");
    }

    private static SideTipsManager RequireSideTipsManager()
    {
        return GameEntry.GetComponent<SideTipsManager>()
               ?? throw new InvalidOperationException("Tutorial presentation requires SideTipsManager.");
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add(0x5455544F5249414CUL);
        hasher.Add(s_Current != null);
        if (s_Current == null)
            return;

        hasher.Add((int)s_Current.Stage);
        hasher.Add(s_Current.m_EnemyStrongholdIntroShown);
        hasher.Add(s_Current.m_UpgradeControlsShown);
        hasher.Add(s_Current.m_CoreFirstUpgradeCompleted);
        hasher.Add(s_Current.m_ArmyBaseline);
        hasher.Add(s_Current.m_DefenseBaseline);
        hasher.Add(s_Current.m_ProductionBaseline);
        hasher.Add(s_Current.m_ResearchBuildingBaseline);
        hasher.Add(s_Current.m_ResearchCommandBaseline);
        hasher.Add(s_Current.m_CoreStartLevel);
        hasher.Add(s_Current.m_CapturedStrongholdId);
        hasher.Add(s_Current.m_CapturedCoreBuildingInstanceId);
        hasher.Add(s_Current.m_LastLogicFrame);
    }

    private void OnLogicFrameRuntimeBegan()
    {
        RegisterLogicFrame();
    }

    private void OnLogicFrameRuntimeEnding()
    {
        if (m_LogicFrameRegistered)
            UnregisterLogicFrame();
    }

    private void RegisterLogicFrame()
    {
        if (m_LogicFrameRegistered)
            throw new InvalidOperationException("TutorialManager logic-frame listener is already registered.");
        LogicFrameRuntime.Register(this);
        m_LogicFrameRegistered = true;
    }

    private void UnregisterLogicFrame()
    {
        if (!m_LogicFrameRegistered)
            throw new InvalidOperationException("TutorialManager logic-frame listener is not registered.");
        LogicFrameRuntime.Unregister(this);
        m_LogicFrameRegistered = false;
    }
}
