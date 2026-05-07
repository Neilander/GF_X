using GameFramework.Event;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

public partial class PhaseSwitchUIForm : UIFormBase
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
        varPhaseSwitchButton.onClick.RemoveAllListeners();
        varPhaseSwitchButton.onClick.AddListener(SwitchPhase);
        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
        TutorialManager.PhaseSwitchButtonGuideChanged += OnPhaseSwitchButtonGuideChanged;
        RefreshTutorialPhaseSwitchState();
        RefreshCurrentDayText();
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
        TutorialManager.PhaseSwitchButtonGuideChanged -= OnPhaseSwitchButtonGuideChanged;
        varPhaseSwitchButton.onClick.RemoveAllListeners();
        StopPhaseSwitchBlink();
        base.OnClose(isShutdown, userData);
    }

    private void SwitchPhase()
    {
        if (!TryGetCurrentFriendlyStronghold(out Stronghold stronghold))
        {
            if (GF.UI != null)
                GF.UI.ShowSideTips(
                    LocalizationTextDataModel.GetText(BlockedSwitchTipTitleId),
                    LocalizationTextDataModel.GetText(BlockedSwitchTipContentId),
                    BlockedSwitchTipDuration);

            string strongholdId = stronghold?.strongholdData != null ? stronghold.strongholdData.StrongholdId : "none";
            int ownerFactionId = stronghold != null ? stronghold.OwnerFactionId : -1;
            Log.Info("[PhaseSwitch] Blocked switch: player is not in friendly stronghold. id={0}, ownerFaction={1}.", strongholdId, ownerFactionId);
            return;
        }

        PhaseManager.SwitchToNextPhase();
    }

    private static bool TryGetCurrentFriendlyStronghold(out Stronghold stronghold)
    {
        stronghold = null;

        if (EntityRegistry.Player == null || LevelEntity.ActiveLevelEntity == null)
            return false;

        stronghold = LevelEntity.GetStrongholdAtWorldPosition(EntityRegistry.Player.Position);
        return stronghold != null && stronghold.OwnerFactionId == EntitySideHelper.PlayerFactionId;
    }

    private void OnIngamePhaseChanged(object sender, GameEventArgs e)
    {
        RefreshTutorialPhaseSwitchState();
        RefreshCurrentDayText();
    }

    private void OnPhaseSwitchButtonGuideChanged()
    {
        RefreshTutorialPhaseSwitchState();
    }

    private void RefreshCurrentDayText()
    {
        int day = InGameDataModel.GetValue(IngameValueType.Day);
        GamePhase phase = (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);
        varCurrentDayText.text = $"Day{day} {GetPhaseDisplayName(phase)}";
    }

    private void RefreshTutorialPhaseSwitchState()
    {
        bool interactable = true;
        bool shouldBlink = false;

        if (TutorialManager.TryGetPhaseSwitchButtonGuide(out bool guidedInteractable, out bool guidedBlink))
        {
            interactable = guidedInteractable;
            shouldBlink = guidedBlink;
        }

        varPhaseSwitchButton.interactable = interactable;

        if (shouldBlink)
            StartPhaseSwitchBlink();
        else
            StopPhaseSwitchBlink();
    }

    private void StartPhaseSwitchBlink()
    {
        if (m_PhaseSwitchBlinkTween != null && m_PhaseSwitchBlinkTween.IsActive())
            return;

        Graphic graphic = varPhaseSwitchButton != null ? varPhaseSwitchButton.targetGraphic : null;
        if (graphic == null)
            return;

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

        Graphic graphic = varPhaseSwitchButton != null ? varPhaseSwitchButton.targetGraphic : null;
        if (graphic == null)
            return;

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
