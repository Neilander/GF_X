using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

public partial class PhaseSwitchUIForm : UIFormBase
{
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
        PhaseManager.SwitchToNextPhase();
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
