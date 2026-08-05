using GameFramework;
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

public class TutorialManager : GameFrameworkComponent, ILogicFrameUpdate, ILogicFrameStableOrder
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

    private enum TutorialPresentationKind
    {
        ShowTip,
        CloseTip,
        GuideChanged,
    }

    private readonly struct TutorialPresentationRequest
    {
        public TutorialPresentationRequest(TutorialPresentationKind kind, TutorialType type, string tipId, string textId)
        {
            Kind = kind;
            Type = type;
            TipId = tipId;
            TextId = textId;
        }

        public TutorialPresentationKind Kind { get; }
        public TutorialType Type { get; }
        public string TipId { get; }
        public string TextId { get; }
    }

    private InputModel inputModel;
    private readonly HashSet<TutorialType> activeTutorials = new HashSet<TutorialType>();
    private readonly List<TutorialType> activeTutorialsBuffer = new List<TutorialType>(8);
    private readonly HashSet<TutorialType> completedTutorials = new HashSet<TutorialType>();
    private bool m_EventSubscribed;
    private bool m_LogicFrameRegistered;
    private ulong m_LastLogicFrame;
    private int m_BuildTutorialStartBuiltCount;
    private string m_InvadeTutorialStrongholdId;
    private readonly Queue<TutorialPresentationRequest> m_PendingPresentation = new Queue<TutorialPresentationRequest>();

    public int LogicFrameOrder => 1000;
    public long LogicFrameStableKey => 0;

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
        LevelSelectionService.LevelLoadStarted += OnLevelLoadStarted;
        LogicMovementRegionConstraintService.TutorialStrongholdBoundaryActivated += OnTutorialStrongholdBoundaryActivated;
        LogicFrameRuntime.Began += OnLogicFrameRuntimeBegan;
        LogicFrameRuntime.Ending += OnLogicFrameRuntimeEnding;
        TrySubscribeEvents();
        if (LogicFrameRuntime.IsActive)
            RegisterLogicFrame();
    }

    private void OnDisable()
    {
        LevelSelectionService.LevelLoadStarted -= OnLevelLoadStarted;
        LogicMovementRegionConstraintService.TutorialStrongholdBoundaryActivated -= OnTutorialStrongholdBoundaryActivated;
        LogicFrameRuntime.Began -= OnLogicFrameRuntimeBegan;
        LogicFrameRuntime.Ending -= OnLogicFrameRuntimeEnding;
        if (m_LogicFrameRegistered)
            UnregisterLogicFrame();
        TryUnsubscribeEvents();
    }

    private void OnDestroy()
    {
        TryUnsubscribeEvents();
        if (s_CachedManager == this)
            s_CachedManager = null;
    }

    public void PrepareRuntimeDependencies(InputModel model)
    {
        inputModel = model ?? throw new ArgumentNullException(nameof(model));
    }

    public void UpdatePresentation()
    {
        while (m_PendingPresentation.Count > 0)
        {
            TutorialPresentationRequest request = m_PendingPresentation.Dequeue();
            switch (request.Kind)
            {
                case TutorialPresentationKind.ShowTip:
                    ShowTutorialPresentationImmediate(request.Type, request.TipId, request.TextId);
                    break;
                case TutorialPresentationKind.CloseTip:
                    CloseTutorialPresentationImmediate(request.TipId);
                    break;
                case TutorialPresentationKind.GuideChanged:
                    PhaseSwitchButtonGuideChanged?.Invoke();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request.Kind), request.Kind, "Unknown tutorial presentation request.");
            }
        }
    }

    public void OnLogicFrameUpdate(Fix64 deltaTime)
    {
        if (deltaTime != LogicFrameRuntime.FixedDeltaTime)
            throw new InvalidOperationException("TutorialManager received a non-fixed logic delta.");
        if (LogicFrameRuntime.CurrentFrame == 0)
            throw new InvalidOperationException("TutorialManager cannot update on logic frame zero.");

        m_LastLogicFrame = LogicFrameRuntime.CurrentFrame;

        if (activeTutorials.Count <= 0)
            return;

        activeTutorialsBuffer.Clear();
        activeTutorialsBuffer.AddRange(activeTutorials);
        activeTutorialsBuffer.Sort();

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

    public static bool IsCurrentLevelTutorial()
    {
        return IsCurrentLevelLv1();
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
        m_LastLogicFrame = 0;
        m_BuildTutorialStartBuiltCount = 0;
        m_InvadeTutorialStrongholdId = null;
        NotifyPhaseSwitchButtonGuideChanged();
    }

    private void OnLevelLoadStarted()
    {
        ResetAllTutorialsForDebug();
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

        if (!TryGetTutorialTipConfig(triggerType, out string tipId, out string textId))
        {
            Log.Warning("[Tutorial] Start tutorial failed: unsupported type={0}.", triggerType);
            return false;
        }

        activeTutorials.Add(triggerType);

        if (triggerType == TutorialType.Build)
            m_BuildTutorialStartBuiltCount = CountPlayerBuiltBuildings();

        if (triggerType == TutorialType.InvadeSH)
            TryBindInvadeTutorialStronghold();

        m_PendingPresentation.Enqueue(new TutorialPresentationRequest(
            TutorialPresentationKind.ShowTip,
            triggerType,
            tipId,
            textId));

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

        TryBindInvadeTutorialStronghold();
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
        if (EntityRegistry.Player is not IHeroLogicContext player || !player.IsGhostState)
            return;

        CompleteTutorial(TutorialType.PlayCard, autoChain: false);
    }

    private void CompleteTutorial(TutorialType triggerType, bool autoChain)
    {
        if (!activeTutorials.Remove(triggerType))
            return;

        completedTutorials.Add(triggerType);

        if (TryGetTutorialTipConfig(triggerType, out string tipId, out _))
            RequestCloseSideTip(tipId);

        if (triggerType == TutorialType.SwitchPhase)
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

    private void OnLogicBuildingOwnerFactionChanged(
        IBuildingLogicContext building,
        int oldFactionId,
        int newFactionId)
    {
        if (!activeTutorials.Contains(TutorialType.InvadeSH))
            return;
        if (building == null)
            throw new ArgumentNullException(nameof(building));
        if (newFactionId != EntitySideHelper.PlayerFactionId)
            return;
        if (oldFactionId == EntitySideHelper.PlayerFactionId)
            return;

        CompleteTutorial(TutorialType.InvadeSH, autoChain: true);
    }

    private void OnLogicPhaseApplied(GamePhase oldPhase, GamePhase newPhase)
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

    private void OnLogicGameEnded(LogicGameEndResult result)
    {
        if (!activeTutorials.Contains(TutorialType.PlayCard))
            return;

        CompleteTutorial(TutorialType.PlayCard, autoChain: false);
    }

    private void TrySubscribeEvents()
    {
        if (m_EventSubscribed)
            return;

        LogicBuildingOwnershipEventService.OwnerFactionChanged += OnLogicBuildingOwnerFactionChanged;
        LogicPhaseCommandService.PhaseApplied += OnLogicPhaseApplied;
        LogicGameEndService.GameEnded += OnLogicGameEnded;
        m_EventSubscribed = true;
    }

    private void TryUnsubscribeEvents()
    {
        if (!m_EventSubscribed)
            return;

        LogicBuildingOwnershipEventService.OwnerFactionChanged -= OnLogicBuildingOwnerFactionChanged;
        LogicPhaseCommandService.PhaseApplied -= OnLogicPhaseApplied;
        LogicGameEndService.GameEnded -= OnLogicGameEnded;
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

    private void NotifyPhaseSwitchButtonGuideChanged()
    {
        m_PendingPresentation.Enqueue(new TutorialPresentationRequest(
            TutorialPresentationKind.GuideChanged,
            TutorialType.None,
            null,
            null));
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

    private void TryBindInvadeTutorialStronghold()
    {
        if (!string.IsNullOrEmpty(m_InvadeTutorialStrongholdId))
            return;

        if (!LogicMovementRegionConstraintService.HasTutorialStrongholdBoundary)
            return;

        m_InvadeTutorialStrongholdId = LogicMovementRegionConstraintService.TutorialStrongholdId;
        Log.Info("[Tutorial] Invade tutorial stronghold locked. id={0}, source=LogicTrigger.", m_InvadeTutorialStrongholdId);
    }

    private void OnTutorialStrongholdBoundaryActivated(string strongholdId)
    {
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new InvalidOperationException("Tutorial stronghold activation event has an empty stronghold id.");
        TryStartTutorial(TutorialType.InvadeSH, null, startedByChain: false);
    }

    private static bool IsCurrentLevelLv1()
    {
        return string.Equals(LevelSelectionService.SelectedLevelIdentifier, Lv1Identifier, StringComparison.Ordinal);
    }

    private static int CountPlayerBuiltBuildings()
    {
        int count = 0;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!entities[i].TryGetLogicBuilding(out IBuildingLogicContext building)
                || building.BuildingData == null)
                continue;
            if (building.OwnerFactionId != EntitySideHelper.PlayerFactionId)
                continue;
            if (building.BuildingData.Lv <= 0)
                continue;
            count++;
        }

        return count;
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        TutorialManager manager = s_CachedManager;
        hasher.Add(0x5455544F5249414CUL);
        hasher.Add(manager != null);
        if (manager == null)
            return;

        for (int value = (int)TutorialType.InvadeSH; value <= (int)TutorialType.PlayCard; value++)
        {
            TutorialType type = (TutorialType)value;
            hasher.Add(manager.activeTutorials.Contains(type));
            hasher.Add(manager.completedTutorials.Contains(type));
        }
        hasher.Add(manager.m_BuildTutorialStartBuiltCount);
        hasher.Add(manager.m_InvadeTutorialStrongholdId);
        hasher.Add(manager.m_LastLogicFrame);
    }

    private void ShowTutorialPresentationImmediate(TutorialType triggerType, string tipId, string textId)
    {
        SideTipsManager sideTipsManager = GameEntry.GetComponent<SideTipsManager>();
        if (sideTipsManager == null)
        {
            Log.Error("[Tutorial] SideTipsManager is missing. type={0}.", triggerType);
            return;
        }

        string tipContent = LocalizationTextDataModel.GetText(textId);
        if (string.Equals(tipContent, textId, StringComparison.Ordinal))
            tipContent = string.Empty;
        sideTipsManager.ShowConditionalTip(tipId, string.Empty, tipContent);
    }

    private void OnLogicFrameRuntimeBegan()
    {
        RegisterLogicFrame();
    }

    private void OnLogicFrameRuntimeEnding()
    {
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

    private void RequestCloseSideTip(string tipId)
    {
        if (string.IsNullOrWhiteSpace(tipId))
            throw new ArgumentException("Tutorial close tip id is empty.", nameof(tipId));
        m_PendingPresentation.Enqueue(new TutorialPresentationRequest(
            TutorialPresentationKind.CloseTip,
            TutorialType.None,
            tipId,
            null));
    }

    private void CloseTutorialPresentationImmediate(string tipId)
    {
        if (GF.Event == null)
            throw new InvalidOperationException("Tutorial presentation requires GF.Event.");
        GF.Event.Fire(this, CloseSideTipEventArgs.Create(tipId));
    }

    private bool TryGetInputModel(out InputModel model)
    {
        if (inputModel == null)
        {
            throw new InvalidOperationException("TutorialManager InputModel was not bound before logic frames began.");
        }

        model = inputModel;
        return true;
    }
}
