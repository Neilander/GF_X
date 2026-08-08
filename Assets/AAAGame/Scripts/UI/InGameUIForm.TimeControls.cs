using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

public partial class InGameUIForm
{
    private const int FastPlaybackScaleUnits = LogicTimeControlService.NormalScaleUnits * 2;

    [SerializeField] private Button varPauseButton;
    [SerializeField] private Button varSpeedButton;
    [SerializeField] private TextMeshProUGUI varPauseIcon;
    [SerializeField] private TextMeshProUGUI varSpeedIcon;

    private InputAction m_PauseShortcut;
    private InputAction m_SpeedShortcut;
    private int m_RequestedPlaybackScaleUnits = LogicTimeControlService.NormalScaleUnits;

    private void InitializeTimeControls()
    {
        if (varPauseButton == null || varSpeedButton == null || varPauseIcon == null || varSpeedIcon == null)
            throw new InvalidOperationException("InGameUIForm time controls are not fully bound.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("InGameUIForm time controls require an active logic timeline.");

        InputManager inputManager = GameEntry.GetComponent<InputManager>()
            ?? throw new InvalidOperationException("InGameUIForm time controls require InputManager.");
        InputActionAsset actions = inputManager.playerInput?.actions
            ?? throw new InvalidOperationException("InGameUIForm time controls require PlayerInput actions.");
        m_PauseShortcut = actions.FindAction("Pause", true);
        m_SpeedShortcut = actions.FindAction("Speed", true);

        varPauseButton.onClick.RemoveListener(TogglePause);
        varPauseButton.onClick.AddListener(TogglePause);
        varSpeedButton.onClick.RemoveListener(ToggleSpeed);
        varSpeedButton.onClick.AddListener(ToggleSpeed);
        LogicTimeControlService.Changed += RefreshTimeControls;

        m_RequestedPlaybackScaleUnits = LogicTimeControlService.BasePlaybackScaleUnits;
        EnforcePlaybackScaleForPhase();
        RefreshTimeControls();
    }

    private void ShutdownTimeControls()
    {
        LogicTimeControlService.Changed -= RefreshTimeControls;
        if (varPauseButton != null)
            varPauseButton.onClick.RemoveListener(TogglePause);
        if (varSpeedButton != null)
            varSpeedButton.onClick.RemoveListener(ToggleSpeed);
        m_PauseShortcut = null;
        m_SpeedShortcut = null;

        if (!LogicTimeControlService.IsActive)
            return;
        if (LogicTimeControlService.HasPause(LogicTimeControlSources.InGameUiPause))
            LogicTimeControlService.ReleasePause(LogicTimeControlSources.InGameUiPause);
        if (m_RequestedPlaybackScaleUnits != LogicTimeControlService.NormalScaleUnits)
            RequestPlaybackScale(LogicTimeControlService.NormalScaleUnits);
    }

    private void TickTimeControls(InputManager inputManager)
    {
        if (inputManager == null || inputManager.CurState != InputState.Game)
            return;
        if (m_PauseShortcut.WasPressedThisFrame())
            TogglePause();
        if (m_SpeedShortcut.WasPressedThisFrame())
            ToggleSpeed();
    }

    private void TogglePause()
    {
        if (LogicTimeControlService.HasPause(LogicTimeControlSources.InGameUiPause))
            LogicTimeControlService.ReleasePause(LogicTimeControlSources.InGameUiPause);
        else
            LogicTimeControlService.AcquirePause(LogicTimeControlSources.InGameUiPause);
        RefreshTimeControls();
    }

    private void ToggleSpeed()
    {
        if (!CanUseFastPlayback())
        {
            EnforcePlaybackScaleForPhase();
            return;
        }

        int nextScale = m_RequestedPlaybackScaleUnits == FastPlaybackScaleUnits
            ? LogicTimeControlService.NormalScaleUnits
            : FastPlaybackScaleUnits;
        RequestPlaybackScale(nextScale);
    }

    private void OnTimeControlPhaseChanged()
    {
        EnforcePlaybackScaleForPhase();
        RefreshTimeControls();
    }

    private void EnforcePlaybackScaleForPhase()
    {
        if (!CanUseFastPlayback() && m_RequestedPlaybackScaleUnits != LogicTimeControlService.NormalScaleUnits)
            RequestPlaybackScale(LogicTimeControlService.NormalScaleUnits);
    }

    private void RequestPlaybackScale(int scaleUnits)
    {
        LogicTimeControlService.SetBasePlaybackScale(scaleUnits);
        m_RequestedPlaybackScaleUnits = scaleUnits;
        RefreshTimeControls();
    }

    private static bool CanUseFastPlayback()
    {
        return (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase) == GamePhase.Defend;
    }

    private void RefreshTimeControls()
    {
        if (varPauseButton == null || varSpeedButton == null || varPauseIcon == null || varSpeedIcon == null)
            return;

        bool paused = LogicTimeControlService.IsActive
                      && LogicTimeControlService.HasPause(LogicTimeControlSources.InGameUiPause);
        varPauseIcon.text = paused ? ">" : "||";
        varSpeedIcon.text = m_RequestedPlaybackScaleUnits == FastPlaybackScaleUnits ? "2×" : "1×";
        varSpeedButton.interactable = CanUseFastPlayback();
    }
}
