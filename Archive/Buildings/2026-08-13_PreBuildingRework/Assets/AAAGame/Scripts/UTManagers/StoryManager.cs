using System;
using System.Collections.Generic;
using GameFramework;
using UnityGameFramework.Runtime;

public static class StoryManager
{
    private static string s_ActiveScriptId;
    private static StoryScriptTable[] s_ActiveRows;
    private static Action s_OnComplete;
    private static int s_UIFormId = -1;
    private static bool s_CompletionRequested;

    public static bool IsPlaying => s_ActiveScriptId != null;

    public static void PlayScript(string scriptId, Action onComplete)
    {
        if (IsPlaying)
            throw new InvalidOperationException($"Story script '{s_ActiveScriptId}' is already playing.");

        StoryScriptTable[] rows = LoadAndValidateScript(scriptId);
        s_ActiveScriptId = scriptId;
        s_ActiveRows = rows;
        s_OnComplete = onComplete;
        s_CompletionRequested = false;

        UIParams uiParams = UIParams.Create(false);
        uiParams.Set<VarString>(StoryUIForm.P_ScriptId, scriptId);
        uiParams.CloseCallback = OnStoryFormClosed;
        s_UIFormId = GF.UI.OpenUIForm(UIViews.StoryUIForm, uiParams);
        if (s_UIFormId >= 0)
        {
            Log.Info("[Story] Playback started. script={0}, screens={1}, read={2}.", scriptId, rows.Length, HasRead(scriptId));
            return;
        }

        ClearActivePlayback();
        throw new InvalidOperationException($"Failed to open StoryUIForm for script '{scriptId}'.");
    }

    public static bool HasRead(string scriptId)
    {
        ValidateScriptId(scriptId);
        return GF.DataModel.GetOrCreate<StoryProgressDataModel>().HasRead(scriptId);
    }

    public static bool TryPlayTriggeredStory(
        string levelIdentifier,
        StoryTiming timing,
        Action onComplete)
    {
        if (string.IsNullOrWhiteSpace(levelIdentifier))
            throw new ArgumentException("Story level identifier is empty.", nameof(levelIdentifier));

        var table = GF.DataTable.GetDataTable<StoryTriggerTable>();
        if (table == null)
            throw new InvalidOperationException("StoryTriggerTable is not loaded.");

        StoryTriggerTable match = StoryTableQuery.FindTrigger(
            table.GetAllDataRows(),
            levelIdentifier,
            timing);

        if (match == null)
        {
            onComplete?.Invoke();
            return false;
        }

        if (string.IsNullOrWhiteSpace(match.ScriptID))
            throw new InvalidOperationException($"Story trigger {match.Id} has an empty script id.");

        if (match.OnceOnly && HasRead(match.ScriptID))
        {
            Log.Info("[Story] Once-only script skipped because it was read. script={0}.", match.ScriptID);
            onComplete?.Invoke();
            return false;
        }

        PlayScript(match.ScriptID, onComplete);
        return true;
    }

    internal static IReadOnlyList<StoryScriptTable> GetActiveRows(string scriptId)
    {
        ValidateScriptId(scriptId);
        if (!IsPlaying || !string.Equals(s_ActiveScriptId, scriptId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Story script '{scriptId}' is not the active playback.");
        return s_ActiveRows;
    }

    internal static void RequestComplete(string scriptId)
    {
        if (!IsPlaying || !string.Equals(s_ActiveScriptId, scriptId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Cannot complete inactive story script '{scriptId}'.");
        if (s_CompletionRequested)
            throw new InvalidOperationException($"Story script '{scriptId}' completion was already requested.");

        GF.DataModel.GetOrCreate<StoryProgressDataModel>().MarkRead(scriptId);
        s_CompletionRequested = true;
        GF.UI.CloseUIForm(s_UIFormId);
    }

    private static StoryScriptTable[] LoadAndValidateScript(string scriptId)
    {
        ValidateScriptId(scriptId);
        var table = GF.DataTable.GetDataTable<StoryScriptTable>();
        if (table == null)
            throw new InvalidOperationException("StoryScriptTable is not loaded.");

        return StoryTableQuery.GetScriptRows(table.GetAllDataRows(), scriptId);
    }

    private static void OnStoryFormClosed(UIFormLogic form)
    {
        if (!s_CompletionRequested)
            throw new InvalidOperationException($"StoryUIForm closed before script '{s_ActiveScriptId}' completed.");

        string completedScriptId = s_ActiveScriptId;
        Action callback = s_OnComplete;
        ClearActivePlayback();
        Log.Info("[Story] Playback completed. script={0}.", completedScriptId);
        callback?.Invoke();
    }

    private static void ClearActivePlayback()
    {
        s_ActiveScriptId = null;
        s_ActiveRows = null;
        s_OnComplete = null;
        s_UIFormId = -1;
        s_CompletionRequested = false;
    }

    private static void ValidateScriptId(string scriptId)
    {
        if (string.IsNullOrWhiteSpace(scriptId))
            throw new ArgumentException("Story script id is empty.", nameof(scriptId));
    }
}
