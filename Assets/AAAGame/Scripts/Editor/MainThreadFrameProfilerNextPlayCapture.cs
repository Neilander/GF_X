using System;
using UnityEditor;
using UnityEngine;
using UnityGameFramework.Runtime;

[InitializeOnLoad]
internal static class MainThreadFrameProfilerNextPlayCapture
{
    private const string ArmedKey = "Avenge.MainThreadFrameProfiler.NextPlayCapture";
    private const string ConsoleLoggingKey = "Avenge.MainThreadFrameProfiler.NextPlayConsoleLogging";

    static MainThreadFrameProfilerNextPlayCapture()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        if (EditorApplication.isPlaying)
            EditorApplication.delayCall += EnableIfArmed;
        else
            MainThreadFrameProfiler.LoggingEnabled = false;
    }

    [MenuItem("Tools/Logic Frames/Capture MainPerf Next Play")]
    public static void ArmNextPlay()
    {
        ArmNextPlay(true);
    }

    public static void ArmNextPlay(bool consoleLoggingEnabled)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before arming the next-play MainPerf capture.");

        EditorPrefs.SetBool(ArmedKey, true);
        EditorPrefs.SetBool(ConsoleLoggingKey, consoleLoggingEnabled);
        Debug.Log("[MainPerfCapture] Armed for the next Play session.");
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
            EnableIfArmed();
        else if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            MainThreadFrameProfiler.LoggingEnabled = false;
    }

    private static void EnableIfArmed()
    {
        if (!EditorPrefs.GetBool(ArmedKey, false))
            return;
        if (!EditorApplication.isPlaying)
            throw new InvalidOperationException("MainPerf next-play capture was consumed outside Play mode.");

        EditorPrefs.DeleteKey(ArmedKey);
        bool consoleLoggingEnabled = EditorPrefs.GetBool(ConsoleLoggingKey, true);
        EditorPrefs.DeleteKey(ConsoleLoggingKey);
        MainThreadFrameProfiler.LoggingEnabled = true;
        MainThreadFrameProfiler.ConsoleLoggingEnabled = consoleLoggingEnabled;
        Debug.Log("[MainPerfCapture] Enabled for this Play session.");
    }
}
