using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class EditorRuntimeLevelEntryLauncher
{
    private enum EntryState
    {
        WaitingForPlay = 0,
        WaitingForRuntimeProcedure = 1,
        WaitingForRuntimeReady = 2,
    }

    private const string LaunchScenePath = "Assets/AAAGame/Scene/Launch.unity";
    private const string SessionPrefix = "Avenge.EditorRuntimeLevelEntry.";
    private const string PendingKey = SessionPrefix + "Pending";
    private const string LevelIdentifierKey = SessionPrefix + "LevelIdentifier";
    private const string StateKey = SessionPrefix + "State";
    private const string DeadlineUtcTicksKey = SessionPrefix + "DeadlineUtcTicks";
    private const string LastCompletedLevelKey = SessionPrefix + "LastCompletedLevel";
    private const string LastErrorKey = SessionPrefix + "LastError";
    private const double EntryTimeoutSeconds = 120.0;

    static EditorRuntimeLevelEntryLauncher()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
    }

    public static bool IsEntryPending => SessionState.GetBool(PendingKey, false);
    public static string RequestedLevelIdentifier => SessionState.GetString(LevelIdentifierKey, string.Empty);
    public static string LastCompletedLevelIdentifier => SessionState.GetString(LastCompletedLevelKey, string.Empty);
    public static string LastError => SessionState.GetString(LastErrorKey, string.Empty);

    public static string Status
    {
        get
        {
            if (IsEntryPending)
            {
                EntryState state = (EntryState)SessionState.GetInt(StateKey, (int)EntryState.WaitingForPlay);
                return $"Entering '{RequestedLevelIdentifier}': {state}.";
            }

            if (!string.IsNullOrEmpty(LastError))
                return $"Last entry failed: {LastError}";
            if (!string.IsNullOrEmpty(LastCompletedLevelIdentifier))
                return $"Runtime ready: {LastCompletedLevelIdentifier}.";
            return "No runtime level entry has been requested in this editor session.";
        }
    }

    public static void Enter(string levelIdentifier)
    {
        string normalizedIdentifier = NormalizeLevelIdentifier(levelIdentifier);
        if (IsEntryPending)
            throw new InvalidOperationException($"Runtime level entry is already pending for '{RequestedLevelIdentifier}'.");
        if (EditorApplication.isCompiling)
            throw new InvalidOperationException("Wait for script compilation before entering a runtime level.");
        if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            throw new InvalidOperationException("Wait for the current Play Mode transition before entering a runtime level.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(LaunchScenePath) == null)
            throw new FileNotFoundException("Launch scene is missing.", LaunchScenePath);

        if (!EditorApplication.isPlaying)
            RequireCleanLoadedScenes();

        SessionState.SetBool(PendingKey, true);
        SessionState.SetString(LevelIdentifierKey, normalizedIdentifier);
        SessionState.SetInt(
            StateKey,
            EditorApplication.isPlaying
                ? (int)EntryState.WaitingForRuntimeProcedure
                : (int)EntryState.WaitingForPlay);
        SessionState.SetString(
            DeadlineUtcTicksKey,
            DateTime.UtcNow.AddSeconds(EntryTimeoutSeconds).Ticks.ToString(CultureInfo.InvariantCulture));
        SessionState.EraseString(LastErrorKey);

        if (EditorApplication.isPlaying)
            return;

        try
        {
            EditorSceneManager.OpenScene(LaunchScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }
        catch
        {
            ClearPendingRequest();
            throw;
        }
    }

    private static string NormalizeLevelIdentifier(string levelIdentifier)
    {
        if (string.IsNullOrWhiteSpace(levelIdentifier))
            throw new ArgumentException("Runtime level identifier cannot be empty.", nameof(levelIdentifier));
        return levelIdentifier.Trim();
    }

    private static void RequireCleanLoadedScenes()
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var scene = EditorSceneManager.GetSceneAt(i);
            if (scene.isDirty)
            {
                throw new InvalidOperationException(
                    $"Cannot enter a runtime level while scene '{scene.path}' has unsaved changes.");
            }
        }
    }

    private static void Update()
    {
        if (!IsEntryPending)
            return;

        try
        {
            if (DateTime.UtcNow.Ticks > ReadDeadlineUtcTicks())
                throw new TimeoutException($"Timed out entering runtime level '{RequestedLevelIdentifier}'.");

            EntryState state = (EntryState)SessionState.GetInt(StateKey, (int)EntryState.WaitingForPlay);
            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                throw new InvalidOperationException(
                    $"Play Mode ended before runtime level '{RequestedLevelIdentifier}' became ready.");
            }

            if (state == EntryState.WaitingForPlay)
            {
                SessionState.SetInt(StateKey, (int)EntryState.WaitingForRuntimeProcedure);
                return;
            }

            if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase runtimeProcedure)
                return;

            if (state == EntryState.WaitingForRuntimeProcedure)
            {
                string levelIdentifier = RequestedLevelIdentifier;
                if (!EditorRuntimeLevelEntry.TryEnterWithDefaultCareer(levelIdentifier, out string errorMessage))
                {
                    throw new InvalidOperationException(
                        $"Cannot enter runtime level '{levelIdentifier}' from Launch: {errorMessage}");
                }

                SessionState.SetInt(StateKey, (int)EntryState.WaitingForRuntimeReady);
                Debug.Log($"[EditorRuntimeLevelEntry] Entry requested. level={levelIdentifier}");
                return;
            }

            if (state != EntryState.WaitingForRuntimeReady)
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown runtime level entry state.");

            string requestedLevel = RequestedLevelIdentifier;
            if (!runtimeProcedure.IsEditorStressRuntimeReady
                || LevelSelectionService.IsLevelLoading
                || !string.Equals(LevelSelectionService.SelectedLevelIdentifier, requestedLevel, StringComparison.Ordinal))
            {
                return;
            }

            SessionState.SetString(LastCompletedLevelKey, requestedLevel);
            ClearPendingRequest();
            Debug.Log($"[EditorRuntimeLevelEntry] Runtime ready. level={requestedLevel}");
        }
        catch (Exception exception)
        {
            string error = exception.ToString();
            SessionState.SetString(LastErrorKey, error);
            ClearPendingRequest();
            Debug.LogException(exception);
        }
    }

    private static long ReadDeadlineUtcTicks()
    {
        string raw = SessionState.GetString(DeadlineUtcTicksKey, string.Empty);
        if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) || ticks <= 0)
            throw new InvalidOperationException("Runtime level entry deadline is missing or invalid.");
        return ticks;
    }

    private static void ClearPendingRequest()
    {
        SessionState.SetBool(PendingKey, false);
        SessionState.EraseString(LevelIdentifierKey);
        SessionState.EraseInt(StateKey);
        SessionState.EraseString(DeadlineUtcTicksKey);
    }
}
