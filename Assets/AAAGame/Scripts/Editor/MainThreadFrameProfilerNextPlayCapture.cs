using System;
using UnityEditor;
using UnityEngine;
using UnityGameFramework.Runtime;

[InitializeOnLoad]
internal static class MainThreadFrameProfilerNextPlayCapture
{
    private const string ArmedKey = "Avenge.MainThreadFrameProfiler.NextPlayCapture";

    static MainThreadFrameProfilerNextPlayCapture()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        if (EditorApplication.isPlaying)
            EditorApplication.delayCall += EnableIfArmed;
    }

    [MenuItem("Tools/Logic Frames/Capture MainPerf Next Play")]
    public static void ArmNextPlay()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before arming the next-play MainPerf capture.");

        SessionState.SetBool(ArmedKey, true);
        Debug.Log("[MainPerfCapture] Armed for the next Play session.");
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
            EnableIfArmed();
    }

    private static void EnableIfArmed()
    {
        if (!SessionState.GetBool(ArmedKey, false))
            return;
        if (!EditorApplication.isPlaying)
            throw new InvalidOperationException("MainPerf next-play capture was consumed outside Play mode.");

        SessionState.SetBool(ArmedKey, false);
        MainThreadFrameProfiler.LoggingEnabled = true;
        Debug.Log("[MainPerfCapture] Enabled for this Play session.");
    }
}
