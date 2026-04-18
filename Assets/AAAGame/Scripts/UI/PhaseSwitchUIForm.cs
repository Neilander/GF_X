using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

public partial class PhaseSwitchUIForm : UIFormBase
{
    private const string BlockedSwitchTipTitle = "无法切换阶段";
    private const string BlockedSwitchTipContent = "请先离开敌方据点。";
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
                GF.UI.ShowSideTips(BlockedSwitchTipTitle, BlockedSwitchTipContent, BlockedSwitchTipDuration);

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
        varCurrentDayText.text = $"Day{day} {GetPhaseDisplayName(phase)}";
    }

    private static string GetPhaseDisplayName(GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.Build:
                return "运营";
            case GamePhase.Invade:
                return "战斗";
            case GamePhase.Defend:
                return "防御";
            default:
                return phase.ToString();
        }
    }
}
