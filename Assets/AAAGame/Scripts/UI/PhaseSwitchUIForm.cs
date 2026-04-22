using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

public partial class PhaseSwitchUIForm : UIFormBase
{
    private const string BlockedSwitchTipTitleId = "PhaseSwitch_Blocked_Title";
    private const string BlockedSwitchTipContentId = "PhaseSwitch_Blocked_Content";
    private const string DayPhaseFormatTextId = "PhaseSwitch_DayPhase_Format";
    private const string BuildPhaseTextId = "PhaseSwitch_Phase_Build";
    private const string InvadePhaseTextId = "PhaseSwitch_Phase_Invade";
    private const string DefendPhaseTextId = "PhaseSwitch_Phase_Defend";
    private const float BlockedSwitchTipDuration = 2f;

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        varPhaseSwitchButton.onClick.RemoveAllListeners();
        varPhaseSwitchButton.onClick.AddListener(SwitchPhase);
        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
        RefreshCurrentDayText();
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
        varPhaseSwitchButton.onClick.RemoveAllListeners();
        base.OnClose(isShutdown, userData);
    }

    private void SwitchPhase()
    {
        if (TryGetCurrentEnemyStronghold(out Stronghold stronghold))
        {
            if (GF.UI != null)
                GF.UI.ShowSideTips(
                    LocalizationTextDataModel.GetText(BlockedSwitchTipTitleId),
                    LocalizationTextDataModel.GetText(BlockedSwitchTipContentId),
                    BlockedSwitchTipDuration);

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
            return false;

        stronghold = LevelEntity.GetStrongholdAtWorldPosition(EntityRegistry.Player.Position);
        return stronghold != null && stronghold.OwnerFactionId != EntitySideHelper.PlayerFactionId;
    }

    private void OnIngamePhaseChanged(object sender, GameEventArgs e)
    {
        RefreshCurrentDayText();
    }

    private void RefreshCurrentDayText()
    {
        int day = InGameDataModel.GetValue(IngameValueType.Day);
        GamePhase phase = (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);
        varCurrentDayText.text = string.Format(
            LocalizationTextDataModel.GetText(DayPhaseFormatTextId),
            day,
            GetPhaseDisplayName(phase));
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
