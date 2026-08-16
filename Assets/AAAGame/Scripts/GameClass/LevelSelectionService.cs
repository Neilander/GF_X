using System;
using System.Collections.Generic;
using GameFramework;
using GameFramework.Resource;
using UnityGameFramework.Runtime;

public sealed class LevelSelectionEntry
{
    public int Id { get; }
    public string Identifier { get; }
    public string NameKey { get; }
    public string DescKey { get; }
    public string PrefabPath { get; }

    public LevelSelectionEntry(LevelTable row)
    {
        Id = row.Id;
        Identifier = row.Identifier;
        NameKey = row.NameKey;
        DescKey = row.DescKey;
        PrefabPath = row.PrefabPath;
    }
}

public static class LevelSelectionService
{
    private const int MinSelectableLevelId = 1;
    private const int MaxSelectableLevelId = 3;
    public const string TestLevelIdentifier = "LvTest";
    private static bool s_ShouldShowStartupLevelSwitch = AppSettings.Instance == null || AppSettings.Instance.ShowStartupLevelSwitch;

    public static event Action<float> LevelLoadProgressChanged;
    public static event Action LevelLoadStarted;
    public static event Action LevelRuntimeReadyForFirstFrame;
    public static event Action LevelLoadCompleted;
    public static event Action<string> LevelLoadFailed;

    public static string SelectedLevelIdentifier => ChangeSceneProcedure.SelectedLevelIdentifier;
    public static bool ShouldShowStartupLevelSwitch => s_ShouldShowStartupLevelSwitch;
    public static bool IsLevelLoading { get; private set; }

    public static bool OpenLevelSwitch(bool isStartup)
    {
        return LevelSwitchUIForm.Open(isStartup);
    }

    public static void ConsumeStartupLevelSwitch()
    {
        s_ShouldShowStartupLevelSwitch = false;
    }

    public static IReadOnlyList<LevelSelectionEntry> GetAvailableLevels()
    {
        LevelTable[] rows = GetSortedLevelRows();
        if (rows.Length == 0)
        {
            return Array.Empty<LevelSelectionEntry>();
        }

        var result = new List<LevelSelectionEntry>(rows.Length);
        for (int i = 0; i < rows.Length; i++)
        {
            if (IsSelectableLevelRow(rows[i]))
            {
                result.Add(new LevelSelectionEntry(rows[i]));
            }
        }

        return result;
    }

    private static bool TrySelectLevel(string levelIdentifier, out LevelSelectionEntry selectedLevel, out string errorMessage)
    {
        selectedLevel = null;
        errorMessage = null;

        if (!TryGetLevelRow(levelIdentifier, out LevelTable row, out errorMessage))
        {
            return false;
        }

        if (!ValidateLevelPrefab(row, out errorMessage))
        {
            return false;
        }

        ChangeSceneProcedure.SelectedLevelIdentifier = row.Identifier;
        selectedLevel = new LevelSelectionEntry(row);
        return true;
    }

    public static bool TryEnterPreparedCareerRun(out string errorMessage)
    {
        if (!CareerRunSettings.HasActiveRun)
        {
            errorMessage = "Cannot enter a level before selecting a starting industry.";
            return false;
        }

        string levelIdentifier = CareerRunSettings.RuntimeLevelIdentifier;
        if (string.IsNullOrWhiteSpace(levelIdentifier))
            throw new InvalidOperationException("Active career run has no runtime level identifier.");

        if (!TrySelectLevel(levelIdentifier, out LevelSelectionEntry selectedLevel, out errorMessage))
        {
            return false;
        }

        RuntimeProcedureBase runtimeProcedure = GetCurrentRuntimeProcedure();
        if (runtimeProcedure == null)
        {
            errorMessage = Utility.Text.Format(
                "Cannot enter level '{0}' in place because current procedure is not a RuntimeProcedureBase.",
                selectedLevel.Identifier);
            return false;
        }

        ConsumeStartupLevelSwitch();

        if (!runtimeProcedure.TryEnterPreparedCareerRun(out errorMessage))
        {
            return false;
        }

        Log.Info("[LevelSelection] Prepared career run entry requested. level={0}", selectedLevel.Identifier);
        return true;
    }

    public static bool TryRestoreStageStart(int phaseEpoch, out string errorMessage)
    {
        RuntimeProcedureBase runtimeProcedure = GetCurrentRuntimeProcedure();
        if (runtimeProcedure == null)
        {
            errorMessage = "Cannot restore a stage checkpoint because the current procedure is not a RuntimeProcedureBase.";
            return false;
        }
        if (!runtimeProcedure.TryRestoreStageStart(phaseEpoch, out errorMessage))
            return false;
        Log.Info("[StageCheckpoint] Restore requested. epoch={0}.", phaseEpoch);
        return true;
    }

    internal static void NotifyLevelLoadStarted()
    {
        IsLevelLoading = true;
        LevelLoadStarted?.Invoke();
        NotifyLevelLoadProgress(0f);
    }

    internal static void NotifyLevelLoadProgress(float progress)
    {
        float clampedProgress = Math.Max(0f, Math.Min(1f, progress));
        LevelLoadProgressChanged?.Invoke(clampedProgress);
    }

    internal static void NotifyLevelRuntimeReadyForFirstFrame()
    {
        if (!IsLevelLoading)
            throw new InvalidOperationException("Level runtime became ready while no level load was active.");

        LevelRuntimeReadyForFirstFrame?.Invoke();
    }

    internal static void NotifyLevelLoadCompleted()
    {
        IsLevelLoading = false;
        NotifyLevelLoadProgress(1f);
        LevelLoadCompleted?.Invoke();
    }

    internal static void NotifyLevelLoadFailed(string errorMessage)
    {
        IsLevelLoading = false;
        LevelLoadFailed?.Invoke(errorMessage);
    }

    public static bool TryGetLevelRow(string levelIdentifier, out LevelTable row, out string errorMessage)
    {
        row = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(levelIdentifier))
        {
            errorMessage = "Level identifier is empty.";
            return false;
        }

        if (!LogicRuntimeDataTableCache.IsPrepared)
        {
            errorMessage = "Logic runtime level snapshot is not prepared.";
            return false;
        }

        if (!LogicRuntimeDataTableCache.TryGetLevel(levelIdentifier, out row))
        {
            errorMessage = Utility.Text.Format("Level '{0}' not found in LevelTable.", levelIdentifier);
            return false;
        }

        if (!IsSelectableLevelRow(row))
        {
            errorMessage = Utility.Text.Format("Level '{0}' is not selectable.", levelIdentifier);
            return false;
        }

        return true;
    }

    private static LevelTable[] GetSortedLevelRows()
    {
        if (!LogicRuntimeDataTableCache.IsPrepared)
        {
            return Array.Empty<LevelTable>();
        }

        IReadOnlyList<LevelTable> snapshotRows = LogicRuntimeDataTableCache.LevelRows;
        var rows = new LevelTable[snapshotRows.Count];
        for (int i = 0; i < snapshotRows.Count; i++)
            rows[i] = snapshotRows[i];
        Array.Sort(rows, (a, b) => a.Id.CompareTo(b.Id));
        return rows;
    }

    private static bool IsSelectableLevelRow(LevelTable row)
    {
        return row != null
               && (CareerRunSettings.HasActiveRun
                       && string.Equals(row.Identifier, CareerRunSettings.RuntimeLevelIdentifier, StringComparison.Ordinal)
                   || row.Id >= MinSelectableLevelId && row.Id <= MaxSelectableLevelId
                   || string.Equals(row.Identifier, TestLevelIdentifier, StringComparison.Ordinal))
               && !string.IsNullOrWhiteSpace(row.Identifier)
               && !string.IsNullOrWhiteSpace(row.PrefabPath);
    }

    private static bool ValidateLevelPrefab(LevelTable row, out string errorMessage)
    {
        errorMessage = null;

        if (GF.Resource == null)
        {
            errorMessage = "Resource component is not ready.";
            return false;
        }

        string assetPath = UtilityBuiltin.AssetsPath.GetEntityPath(row.PrefabPath);
        HasAssetResult result = GF.Resource.HasAsset(assetPath);
        if (result == HasAssetResult.NotExist)
        {
            errorMessage = Utility.Text.Format("Level prefab asset does not exist. level={0}, asset={1}", row.Identifier, assetPath);
            return false;
        }

        if (result == HasAssetResult.NotReady)
        {
            errorMessage = Utility.Text.Format("Level prefab asset is not ready. level={0}, asset={1}", row.Identifier, assetPath);
            return false;
        }

        return true;
    }

    private static RuntimeProcedureBase GetCurrentRuntimeProcedure()
    {
        try
        {
            return GF.Procedure != null ? GF.Procedure.CurrentProcedure as RuntimeProcedureBase : null;
        }
        catch (Exception ex)
        {
            Log.Warning("[LevelSelection] Failed to get current procedure: {0}", ex.Message);
            return null;
        }
    }
}
