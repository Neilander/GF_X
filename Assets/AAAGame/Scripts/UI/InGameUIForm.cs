using DG.Tweening;
using GameFramework.Event;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class InGameUIForm : UIFormBase
{
    private const string BuildPhaseTextId = "Phase_Build";
    private const string InvadePhaseTextId = "Phase_Invade";
    private const string DefendPhaseTextId = "Phase_Defend";
    private const float PhaseSwitchBlinkMinAlpha = 0.35f;
    private const float PhaseSwitchBlinkDuration = 0.45f;

    private Tween m_PhaseSwitchBlinkTween;
    private PhaseSwitchHoldTrigger m_PhaseSwitchHoldTrigger;

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        BindButtons();
        InitializePhaseSwitchHold();
        GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
        IngameCoinPreviewState.PreviewChanged += OnCoinPreviewChanged;
        TutorialManager.PhaseSwitchButtonGuideChanged += OnPhaseSwitchButtonGuideChanged;
        GameDebugSettings.RuntimeResourceModifyEnabledChanged += OnRuntimeResourceModifyEnabledChanged;
        InitializeMiniMap();
        InitializeDefendEnemySketch();
        InitializeSkills();
        RefreshAll();
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
        IngameCoinPreviewState.PreviewChanged -= OnCoinPreviewChanged;
        TutorialManager.PhaseSwitchButtonGuideChanged -= OnPhaseSwitchButtonGuideChanged;
        GameDebugSettings.RuntimeResourceModifyEnabledChanged -= OnRuntimeResourceModifyEnabledChanged;
        UnbindButtons();
        ShutdownPhaseSwitchHold();
        ShutdownSkills();
        ShutdownMiniMap();
        ShutdownDefendEnemySketch();
        StopPhaseSwitchBlink();
        IngameCoinPreviewState.Reset();
        base.OnClose(isShutdown, userData);
    }

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);

        InputManager inputManager = GameEntry.GetComponent<InputManager>();
        if (inputManager != null
            && inputManager.CurState == InputState.Game
            && inputManager.WasCancelPressedThisFrame())
        {
            OpenLevelSwitch();
        }

        TickMiniMap(inputManager);
        TickDefendEnemySketch();
        TickSkillPresentation();
    }

    protected override void OnButtonClick(object sender, Button btSelf)
    {
        base.OnButtonClick(sender, btSelf);

        if (btSelf == varReturnBtn)
        {
            OpenLevelSwitch();
            return;
        }

        if (!GameDebugSettings.IsRuntimeResourceModifyEnabled())
        {
            return;
        }

        if (btSelf == varCoinIcon)
        {
            LogicInGameValueCommandService.ScheduleDeltaForNextFrame(IngameValueType.Coin, 1);
            return;
        }

        if (btSelf == varSupplyIcon)
        {
            LogicInGameValueCommandService.ScheduleDeltaForNextFrame(IngameValueType.MaxSupply, 1);
        }
    }

    private void BindButtons()
    {
        BindButton(varReturnBtn, OnReturnClicked);
        BindButton(varCoinIcon, OnCoinIconClicked);
        BindButton(varSupplyIcon, OnSupplyIconClicked);
    }

    private void UnbindButtons()
    {
        UnbindButton(varReturnBtn, OnReturnClicked);
        UnbindButton(varCoinIcon, OnCoinIconClicked);
        UnbindButton(varSupplyIcon, OnSupplyIconClicked);
    }

    private static void BindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private static void UnbindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
    }

    private void OnReturnClicked()
    {
        ClearSkillInputRequests();
        ClickUIButton(varReturnBtn);
    }

    private static void OpenLevelSwitch()
    {
        if (LevelSelectionService.IsLevelLoading)
        {
            return;
        }

        if (GF.UI.IsLoadingUIForm(UIViews.LevelSwitchUIForm) || GF.UI.HasUIForm(UIViews.LevelSwitchUIForm))
        {
            return;
        }

        LevelSwitchUIForm.Open(false);
    }

    private void OnCoinIconClicked()
    {
        ClearSkillInputRequests();
        ClickUIButton(varCoinIcon);
    }

    private void OnSupplyIconClicked()
    {
        ClearSkillInputRequests();
        ClickUIButton(varSupplyIcon);
    }

    private static void ClearSkillInputRequests()
    {
        SkillCastPresentationService.Cancel();
    }

    private void OnPhaseSwitchButtonGuideChanged()
    {
        RefreshPhaseSwitchState();
    }

    private void OnRuntimeResourceModifyEnabledChanged(bool enabled)
    {
        RefreshResourceModifyState();
    }

    private void OnCoinPreviewChanged()
    {
        RefreshCoinText();
    }

    private void OnIngameValueChanged(object sender, GameEventArgs e)
    {
        var args = e as IngameValueChangedEventArgs;
        if (args == null)
        {
            return;
        }

        switch (args.DataType)
        {
            case IngameValueType.Phase:
            case IngameValueType.Day:
            case IngameValueType.Coin:
            case IngameValueType.CurrentSupply:
            case IngameValueType.MaxSupply:
                RefreshAllText();
                RefreshDefendEnemySketch();
                RefreshSkills();
                break;
        }
    }

    private void RefreshAll()
    {
        RefreshAllText();
        RefreshPhaseSwitchState();
        RefreshResourceModifyState();
        RefreshDefendEnemySketch();
    }

    private void RefreshAllText()
    {
        RefreshPhaseAndDayText();
        RefreshCoinText();
        RefreshSupplyText();
    }

    private void RefreshPhaseAndDayText()
    {
        int day = InGameDataModel.GetValue(IngameValueType.Day);
        GamePhase phase = (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);
        varDayText.text = day.ToString();
        varPhaseText.text = GetPhaseDisplayName(phase);
    }

    private void RefreshCoinText()
    {
        int realCoin = InGameDataModel.GetValue(IngameValueType.Coin);
        int displayCoin = IngameCoinPreviewState.GetDisplayCoinValue(realCoin);
        varCoinText.text = displayCoin.ToString();
    }

    private void RefreshSupplyText()
    {
        int currentSupply = InGameDataModel.GetCurrentSupply();
        int maxSupply = InGameDataModel.GetMaxSupply();
        varSupplyText.text = $"{currentSupply} / {maxSupply}";
    }

    private void RefreshPhaseSwitchState()
    {
        bool interactable = true;
        bool shouldBlink = false;

        if (TutorialManager.TryGetPhaseSwitchButtonGuide(out bool guidedInteractable, out bool guidedBlink))
        {
            interactable = guidedInteractable;
            shouldBlink = guidedBlink;
        }

        if (!IsPhaseSwitchAllowedByPhase())
        {
            interactable = false;
            shouldBlink = false;
        }

        varPhaseBg.interactable = interactable;

        if (shouldBlink)
        {
            StartPhaseSwitchBlink();
        }
        else
        {
            StopPhaseSwitchBlink();
        }
    }

    private void RefreshResourceModifyState()
    {
        bool interactable = GameDebugSettings.IsRuntimeResourceModifyEnabled();
        varCoinIcon.interactable = interactable;
        varSupplyIcon.interactable = interactable;
    }

    private void SwitchPhase()
    {
        if (!IsPhaseSwitchAllowedByPhase())
        {
            return;
        }

        IEntityContext player = EntityRegistry.Player
            ?? throw new System.InvalidOperationException("Phase switch requires a registered player entity.");
        if (TryGetCurrentEnemyStronghold(player.PositionFixed, out string strongholdId, out int ownerFactionId))
        {
            SideTipsManager sideTipsManager = GameEntry.GetComponent<SideTipsManager>()
                ?? throw new System.InvalidOperationException("Phase switch requires SideTipsManager.");
            sideTipsManager.ShowTip("PhaseSwitchBlocked");

            Log.Info("[PhaseSwitch] Blocked switch: player is in enemy stronghold. id={0}, ownerFaction={1}.", strongholdId, ownerFactionId);
            return;
        }

        PhaseManager.SwitchToNextPhase();
    }

    private static bool IsPhaseSwitchAllowedByPhase()
    {
        GamePhase phase = (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);
        return phase != GamePhase.Defend || TutorialManager.IsManualDefendPhaseSwitchAllowed();
    }

    private void InitializePhaseSwitchHold()
    {
        if (varPhaseBg == null)
            throw new System.InvalidOperationException("InGameUIForm requires the phase button.");

        m_PhaseSwitchHoldTrigger = varPhaseBg.GetComponent<PhaseSwitchHoldTrigger>();
        if (m_PhaseSwitchHoldTrigger == null)
            m_PhaseSwitchHoldTrigger = varPhaseBg.gameObject.AddComponent<PhaseSwitchHoldTrigger>();
        m_PhaseSwitchHoldTrigger.Configure(varPhaseBg, SwitchPhase, IsPhaseSwitchAllowedByPhase);
    }

    private void ShutdownPhaseSwitchHold()
    {
        if (m_PhaseSwitchHoldTrigger == null)
            return;
        m_PhaseSwitchHoldTrigger.Release();
        m_PhaseSwitchHoldTrigger = null;
    }

    internal static bool TryGetCurrentEnemyStronghold(
        FixVector2 heroPosition,
        out string strongholdId,
        out int ownerFactionId)
    {
        if (!LogicStrongholdMap.TryResolveStrongholdId(heroPosition, out strongholdId))
        {
            ownerFactionId = -1;
            return false;
        }

        ownerFactionId = LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId);
        return ownerFactionId == EntitySideHelper.EnemyFactionId;
    }

    private void StartPhaseSwitchBlink()
    {
        if (m_PhaseSwitchBlinkTween != null && m_PhaseSwitchBlinkTween.IsActive())
        {
            return;
        }

        Graphic graphic = varPhaseBg != null ? varPhaseBg.targetGraphic : null;
        if (graphic == null)
        {
            return;
        }

        Color color = graphic.color;
        color.a = 1f;
        graphic.color = color;

        m_PhaseSwitchBlinkTween = DOTween.To(
                () => graphic.color.a,
                alpha =>
                {
                    Color c = graphic.color;
                    c.a = alpha;
                    graphic.color = c;
                },
                PhaseSwitchBlinkMinAlpha,
                PhaseSwitchBlinkDuration)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true);
    }

    private void StopPhaseSwitchBlink()
    {
        if (m_PhaseSwitchBlinkTween != null)
        {
            m_PhaseSwitchBlinkTween.Kill();
            m_PhaseSwitchBlinkTween = null;
        }

        Graphic graphic = varPhaseBg != null ? varPhaseBg.targetGraphic : null;
        if (graphic == null)
        {
            return;
        }

        Color color = graphic.color;
        color.a = 1f;
        graphic.color = color;
    }

    private static string GetPhaseDisplayName(GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.BuildBeforeInvade:
            case GamePhase.BuildBeforeDefend:
                return LocalizationTextDataModel.GetText(BuildPhaseTextId);
            case GamePhase.Invade:
                return LocalizationTextDataModel.GetText(InvadePhaseTextId);
            case GamePhase.Defend:
                return LocalizationTextDataModel.GetText(DefendPhaseTextId);
            default:
                return phase.ToString();
        }
    }
}

/// <summary>
/// UI层的临时资金预扣显示（不改真实数值）。
/// 用于长按建造/升级时让顶部 Coin 文本即时反映星星进度。
/// </summary>
public static class IngameCoinPreviewState
{
    private static int s_OwnerId;
    private static int s_PreviewDeduction;
    private static bool s_IsCommitted;
    private static LogicEntityId s_CommittedTargetEntityId;
    private static LogicInteractionActionKind s_CommittedActionKind;

    public static event System.Action PreviewChanged;

    public static int PreviewDeduction => Mathf.Max(0, s_PreviewDeduction);
    public static bool IsCommitted => s_IsCommitted;

    public static int GetDisplayCoinValue(int realCoinValue)
    {
        return Mathf.Max(0, realCoinValue - PreviewDeduction);
    }

    public static void SetPreviewDeduction(int ownerId, int deduction)
    {
        if (ownerId == 0)
            return;

        if (s_IsCommitted)
        {
            if (s_OwnerId != ownerId)
                throw new System.InvalidOperationException("A committed coin preview cannot be replaced by another owner.");
            return;
        }

        deduction = Mathf.Max(0, deduction);

        if (deduction <= 0)
        {
            ClearPreviewDeduction(ownerId);
            return;
        }

        bool changed = s_OwnerId != ownerId || s_PreviewDeduction != deduction;
        s_OwnerId = ownerId;
        s_PreviewDeduction = deduction;

        if (changed)
            PreviewChanged?.Invoke();
    }

    public static void ClearPreviewDeduction(int ownerId)
    {
        if (ownerId == 0 || s_OwnerId != ownerId)
            return;
        if (s_IsCommitted)
            return;

        ClearState();
    }

    public static void CommitPreviewDeduction(
        int ownerId,
        LogicEntityId targetEntityId,
        LogicInteractionActionKind actionKind)
    {
        if (ownerId == 0 || s_OwnerId != ownerId || s_PreviewDeduction <= 0)
            throw new System.InvalidOperationException("Coin preview commit requires an active deduction owned by the submitting panel.");
        if (!targetEntityId.IsValid)
            throw new System.ArgumentException("Coin preview commit requires a valid interaction target.", nameof(targetEntityId));
        if (actionKind != LogicInteractionActionKind.ConstructBuilding
            && actionKind != LogicInteractionActionKind.UpgradeBuilding
            && actionKind != LogicInteractionActionKind.ResearchTech)
        {
            throw new System.ArgumentOutOfRangeException(nameof(actionKind), actionKind, "Interaction action does not spend previewed coin.");
        }
        if (s_IsCommitted)
            throw new System.InvalidOperationException("Coin preview is already committed.");
        if (!LogicInteractionCommandService.IsActive)
            throw new System.InvalidOperationException("Coin preview cannot be committed without an active interaction timeline.");

        s_IsCommitted = true;
        s_CommittedTargetEntityId = targetEntityId;
        s_CommittedActionKind = actionKind;
        LogicInteractionCommandService.CommandAppliedPresentation += OnCommandAppliedPresentation;
    }

    public static void Reset()
    {
        ClearState();
    }

    private static void OnCommandAppliedPresentation(LogicInteractionCommand command)
    {
        if (!s_IsCommitted
            || command.TargetEntityId != s_CommittedTargetEntityId
            || command.ActionKind != s_CommittedActionKind)
        {
            return;
        }

        ClearState();
    }

    private static void ClearState()
    {
        bool changed = s_PreviewDeduction != 0;
        if (s_IsCommitted)
            LogicInteractionCommandService.CommandAppliedPresentation -= OnCommandAppliedPresentation;

        s_OwnerId = 0;
        s_PreviewDeduction = 0;
        s_IsCommitted = false;
        s_CommittedTargetEntityId = default;
        s_CommittedActionKind = default;
        if (changed)
            PreviewChanged?.Invoke();
    }
}
