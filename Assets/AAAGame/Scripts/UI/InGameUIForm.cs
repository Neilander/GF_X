using DG.Tweening;
using GameFramework.Event;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class InGameUIForm : UIFormBase
{
    private const string BlockedSwitchTipTitleId = "Tips_PhaseSwitchBlocked_Title";
    private const string BlockedSwitchTipContentId = "Tips_PhaseSwitchBlocked_Content";
    private const string BuildPhaseTextId = "Phase_Build";
    private const string InvadePhaseTextId = "Phase_Invade";
    private const string DefendPhaseTextId = "Phase_Defend";
    private const float BlockedSwitchTipDuration = 2f;
    private const float PhaseSwitchBlinkMinAlpha = 0.35f;
    private const float PhaseSwitchBlinkDuration = 0.45f;

    private Tween m_PhaseSwitchBlinkTween;

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        BindButtons();
        GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
        TutorialManager.PhaseSwitchButtonGuideChanged += OnPhaseSwitchButtonGuideChanged;
        GameDebugSettings.RuntimeResourceModifyEnabledChanged += OnRuntimeResourceModifyEnabledChanged;
        InitializeMiniMap();
        RefreshAll();
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
        TutorialManager.PhaseSwitchButtonGuideChanged -= OnPhaseSwitchButtonGuideChanged;
        GameDebugSettings.RuntimeResourceModifyEnabledChanged -= OnRuntimeResourceModifyEnabledChanged;
        UnbindButtons();
        ShutdownMiniMap();
        StopPhaseSwitchBlink();
        base.OnClose(isShutdown, userData);
    }

    protected override void OnButtonClick(object sender, Button btSelf)
    {
        base.OnButtonClick(sender, btSelf);

        if (btSelf == varPhaseBg)
        {
            SwitchPhase();
            return;
        }

        if (!GameDebugSettings.IsRuntimeResourceModifyEnabled())
        {
            return;
        }

        if (btSelf == varCoinIcon)
        {
            InGameDataModel.TryModifyValue(IngameValueType.Coin, 1, true);
            return;
        }

        if (btSelf == varSupplyIcon)
        {
            InGameDataModel.TryModifyValue(IngameValueType.MaxSupply, 1, true);
        }
    }

    private void BindButtons()
    {
        BindButton(varPhaseBg, OnPhaseBgClicked);
        BindButton(varCoinIcon, OnCoinIconClicked);
        BindButton(varSupplyIcon, OnSupplyIconClicked);
    }

    private void UnbindButtons()
    {
        UnbindButton(varPhaseBg, OnPhaseBgClicked);
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

    private void OnPhaseBgClicked()
    {
        ClickUIButton(varPhaseBg);
    }

    private void OnCoinIconClicked()
    {
        ClickUIButton(varCoinIcon);
    }

    private void OnSupplyIconClicked()
    {
        ClickUIButton(varSupplyIcon);
    }

    private void OnPhaseSwitchButtonGuideChanged()
    {
        RefreshPhaseSwitchState();
    }

    private void OnRuntimeResourceModifyEnabledChanged(bool enabled)
    {
        RefreshResourceModifyState();
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
                break;
        }
    }

    private void RefreshAll()
    {
        RefreshAllText();
        RefreshPhaseSwitchState();
        RefreshResourceModifyState();
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
        varCoinText.text = InGameDataModel.GetValue(IngameValueType.Coin).ToString();
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
        if (TryGetCurrentEnemyStronghold(out Stronghold stronghold))
        {
            if (GF.UI != null)
            {
                GF.UI.ShowSideTips(
                    LocalizationTextDataModel.GetText(BlockedSwitchTipTitleId),
                    LocalizationTextDataModel.GetText(BlockedSwitchTipContentId),
                    BlockedSwitchTipDuration);
            }

            string strongholdId = stronghold.strongholdData != null ? stronghold.strongholdData.StrongholdId : "unknown";
            Log.Info("[PhaseSwitch] Blocked switch: player is in enemy stronghold. id={0}, ownerFaction={1}.", strongholdId, stronghold.OwnerFactionId);
            return;
        }

        PhaseManager.SwitchToNextPhase();
    }

    private static bool TryGetCurrentEnemyStronghold(out Stronghold stronghold)
    {
        stronghold = null;

        if (EntityRegistry.Player == null || LevelEntity.ActiveLevelEntity == null)
        {
            return false;
        }

        stronghold = LevelEntity.GetStrongholdAtWorldPosition(EntityRegistry.Player.Position);
        return stronghold != null && stronghold.OwnerFactionId != EntitySideHelper.PlayerFactionId;
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
            case GamePhase.Build:
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
