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

    public static string SelectedLevelIdentifier => ChangeSceneProcedure.SelectedLevelIdentifier;

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

    public static bool TrySelectLevelByNumber(int levelNumber, out string errorMessage)
    {
        return TrySelectLevel(Utility.Text.Format("Lv_{0}", levelNumber), out _, out errorMessage);
    }

    public static bool TrySelectLevel(string levelIdentifier, out LevelSelectionEntry selectedLevel, out string errorMessage)
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

    public static bool TryEnterLevelByNumber(int levelNumber, out string errorMessage)
    {
        return TryEnterLevel(Utility.Text.Format("Lv_{0}", levelNumber), out errorMessage);
    }

    public static bool TryEnterLevel(string levelIdentifier, out string errorMessage)
    {
        if (!TrySelectLevel(levelIdentifier, out LevelSelectionEntry selectedLevel, out errorMessage))
        {
            return false;
        }

        RuntimeProcedureBase runtimeProcedure = GetCurrentRuntimeProcedure();
        if (runtimeProcedure == null)
        {
            errorMessage = Utility.Text.Format(
                "Cannot enter level '{0}' because current procedure is not a RuntimeProcedureBase.",
                selectedLevel.Identifier);
            return false;
        }

        if (!runtimeProcedure.TryEnterRuntimeLevel(selectedLevel.Identifier, out errorMessage))
        {
            return false;
        }

        Log.Info("[LevelSelection] Enter level requested. level={0}", selectedLevel.Identifier);
        return true;
    }

    public static bool TryEnterLevelInPlaceByNumber(int levelNumber, out string errorMessage)
    {
        return TryEnterLevelInPlace(Utility.Text.Format("Lv_{0}", levelNumber), out errorMessage);
    }

    public static bool TryEnterLevelInPlace(string levelIdentifier, out string errorMessage)
    {
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

        if (!runtimeProcedure.TryEnterRuntimeLevelInPlace(selectedLevel.Identifier, out errorMessage))
        {
            return false;
        }

        Log.Info("[LevelSelection] Enter level in place requested. level={0}", selectedLevel.Identifier);
        return true;
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

        var levelTable = GF.DataTable != null ? GF.DataTable.GetDataTable<LevelTable>() : null;
        if (levelTable == null)
        {
            errorMessage = "LevelTable is not loaded.";
            return false;
        }

        row = levelTable.GetDataRow(item => string.Equals(item.Identifier, levelIdentifier, StringComparison.Ordinal));
        if (row == null)
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
        var levelTable = GF.DataTable != null ? GF.DataTable.GetDataTable<LevelTable>() : null;
        if (levelTable == null)
        {
            return Array.Empty<LevelTable>();
        }

        LevelTable[] rows = levelTable.GetAllDataRows();
        Array.Sort(rows, (a, b) => a.Id.CompareTo(b.Id));
        return rows;
    }

    private static bool IsSelectableLevelRow(LevelTable row)
    {
        return row != null
               && row.Id >= MinSelectableLevelId
               && row.Id <= MaxSelectableLevelId
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
