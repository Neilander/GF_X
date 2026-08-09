using System;
using System.Collections.Generic;

public static class CareerConfigRuntime
{
    public const string OffsetPointThresholdConfigKey = "OffsetRateRequiredForGrowthPoint";
    public const string TutorialLevelIdentifier = "Lv_1";

    private static readonly Dictionary<string, LevelTable> s_Levels = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, VariableExperimentTable> s_Experiments = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, VariableExperimentRuleTable> s_Rules = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, MetaGrowthTable> s_GrowthRows = new(StringComparer.Ordinal);
    private static readonly List<MetaGrowthTable> s_SortedGrowthRows = new();
    private static readonly List<Archetype> s_ArchetypeOrder = new();

    public static bool IsPrepared { get; private set; }
    public static int OffsetPointThreshold { get; private set; }
    public static IReadOnlyList<MetaGrowthTable> GrowthRows => s_SortedGrowthRows;
    public static IReadOnlyList<Archetype> ArchetypeOrder => s_ArchetypeOrder;

    public static void Prepare()
    {
        if (IsPrepared)
            return;
        if (GF.DataTable == null || GF.Config == null)
            throw new InvalidOperationException("Career configuration requires loaded data tables and game config.");

        BuildUniqueIndex(GF.DataTable.GetDataTable<LevelTable>(), s_Levels, row => row.Identifier, nameof(LevelTable));
        BuildUniqueIndex(GF.DataTable.GetDataTable<VariableExperimentTable>(), s_Experiments, row => row.LevelIdentifier, nameof(VariableExperimentTable));
        BuildUniqueIndex(GF.DataTable.GetDataTable<VariableExperimentRuleTable>(), s_Rules, row => row.Identifier, nameof(VariableExperimentRuleTable));
        BuildUniqueIndex(GF.DataTable.GetDataTable<MetaGrowthTable>(), s_GrowthRows, row => row.Identifier, nameof(MetaGrowthTable));

        foreach (LevelTable level in s_Levels.Values)
            ValidateLevelRow(level);
        BuildArchetypeOrder();

        foreach (VariableExperimentTable experiment in s_Experiments.Values)
        {
            if (!s_Levels.ContainsKey(experiment.LevelIdentifier))
                throw new InvalidOperationException($"Variable experiment references missing level '{experiment.LevelIdentifier}'.");
            if (!string.IsNullOrWhiteSpace(experiment.LevelConfigIdentifier)
                && !s_Levels.ContainsKey(experiment.LevelConfigIdentifier))
            {
                throw new InvalidOperationException(
                    $"Variable experiment '{experiment.LevelIdentifier}' references missing level config '{experiment.LevelConfigIdentifier}'.");
            }
            if (string.IsNullOrWhiteSpace(experiment.RuleIdentifier)
                || !s_Rules.ContainsKey(experiment.RuleIdentifier))
            {
                throw new InvalidOperationException(
                    $"Variable experiment '{experiment.LevelIdentifier}' references missing rule '{experiment.RuleIdentifier}'.");
            }
        }

        foreach (VariableExperimentRuleTable rule in s_Rules.Values)
        {
            if (string.IsNullOrWhiteSpace(rule.NameKey) || string.IsNullOrWhiteSpace(rule.DescKey))
                throw new InvalidOperationException($"Variable experiment rule '{rule.Identifier}' requires localization keys.");
            if (string.Equals(rule.Identifier, "VariableRule_OneHealthCoding", StringComparison.Ordinal)
                && (rule.UniqueValues == null || rule.UniqueValues.Length != 1 || rule.UniqueValues[0] <= Fix64.Zero))
            {
                throw new InvalidOperationException(
                    $"Variable experiment rule '{rule.Identifier}' requires one positive UniqueValues entry for unit max health.");
            }
        }

        s_SortedGrowthRows.AddRange(s_GrowthRows.Values);
        s_SortedGrowthRows.Sort((left, right) => left.Id.CompareTo(right.Id));
        for (int i = 0; i < s_SortedGrowthRows.Count; i++)
            ValidateGrowthRow(s_SortedGrowthRows[i]);

        OffsetPointThreshold = GF.Config.GetInt(OffsetPointThresholdConfigKey, 0);
        if (OffsetPointThreshold <= 0)
            throw new InvalidOperationException($"Game config '{OffsetPointThresholdConfigKey}' must be positive.");

        IsPrepared = true;
    }

    public static LevelTable GetLevelRequired(string levelIdentifier)
    {
        RequirePrepared();
        if (string.IsNullOrWhiteSpace(levelIdentifier) || !s_Levels.TryGetValue(levelIdentifier, out LevelTable row))
            throw new InvalidOperationException($"Career level config is missing. level='{levelIdentifier}'.");
        return row;
    }

    public static bool TryGetExperiment(string levelIdentifier, out VariableExperimentTable experiment)
    {
        RequirePrepared();
        return s_Experiments.TryGetValue(levelIdentifier, out experiment);
    }

    public static VariableExperimentRuleTable GetRuleRequired(string identifier)
    {
        RequirePrepared();
        if (string.IsNullOrWhiteSpace(identifier) || !s_Rules.TryGetValue(identifier, out VariableExperimentRuleTable row))
            throw new InvalidOperationException($"Variable experiment rule is missing. rule='{identifier}'.");
        return row;
    }

    public static MetaGrowthTable GetGrowthRequired(string identifier)
    {
        RequirePrepared();
        if (string.IsNullOrWhiteSpace(identifier) || !s_GrowthRows.TryGetValue(identifier, out MetaGrowthTable row))
            throw new InvalidOperationException($"Meta growth config is missing. growth='{identifier}'.");
        return row;
    }

    public static int GetArchetypeOrderIndexRequired(Archetype archetype)
    {
        RequirePrepared();
        int index = s_ArchetypeOrder.IndexOf(archetype);
        if (index < 0)
            throw new InvalidOperationException($"Industry '{archetype}' is missing from the authoritative industry order.");
        return index;
    }

    public static void SortArchetypes(List<Archetype> archetypes)
    {
        if (archetypes == null)
            throw new ArgumentNullException(nameof(archetypes));
        archetypes.Sort((left, right) =>
            GetArchetypeOrderIndexRequired(left).CompareTo(GetArchetypeOrderIndexRequired(right)));
    }

    public static bool IsCareerLevel(string levelIdentifier)
    {
        RequirePrepared();
        return !string.IsNullOrWhiteSpace(levelIdentifier)
               && !string.Equals(levelIdentifier, LevelSelectionService.TestLevelIdentifier, StringComparison.Ordinal)
               && s_Levels.ContainsKey(levelIdentifier);
    }

    public static bool IsTutorialLevel(string levelIdentifier)
    {
        return string.Equals(levelIdentifier, TutorialLevelIdentifier, StringComparison.Ordinal);
    }

    public static bool IsTagAvailableForLevel(LevelTagTable row, string levelIdentifier)
    {
        if (row == null)
            throw new ArgumentNullException(nameof(row));
        if (IsTutorialLevel(levelIdentifier))
            return false;
        if (!string.IsNullOrWhiteSpace(levelIdentifier))
        {
            if (row.BelongLevelID != null && row.BelongLevelID.Length > 0 && !Array.Exists(row.BelongLevelID, value => value == levelIdentifier))
                return false;
            if (row.ExceptLevelID != null && Array.Exists(row.ExceptLevelID, value => value == levelIdentifier))
                return false;
        }
        return true;
    }

    private static void BuildUniqueIndex<TRow>(
        GameFramework.DataTable.IDataTable<TRow> table,
        Dictionary<string, TRow> target,
        Func<TRow, string> keySelector,
        string tableName)
        where TRow : GameFramework.DataTable.IDataRow
    {
        if (table == null)
            throw new InvalidOperationException($"Required data table '{tableName}' is not loaded.");

        target.Clear();
        TRow[] rows = table.GetAllDataRows();
        for (int i = 0; i < rows.Length; i++)
        {
            TRow row = rows[i];
            string key = keySelector(row);
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException($"Data table '{tableName}' has an empty key at row id {row.Id}.");
            if (!target.TryAdd(key, row))
                throw new InvalidOperationException($"Data table '{tableName}' has duplicate key '{key}'.");
        }
    }

    private static void ValidateGrowthRow(MetaGrowthTable row)
    {
        if (string.IsNullOrWhiteSpace(row.NameKey) || string.IsNullOrWhiteSpace(row.DescKey))
            throw new InvalidOperationException($"Meta growth '{row.Identifier}' requires localization keys.");
        if (row.UniqueValues == null || row.UniqueValues.Length == 0)
            throw new InvalidOperationException($"Meta growth '{row.Identifier}' requires level values including level 0.");
        if (row.UniqueValues[0] != Fix64.Zero)
            throw new InvalidOperationException($"Meta growth '{row.Identifier}' level 0 value must be zero.");
        if (row.LevelCosts == null || row.LevelCosts.Length != row.UniqueValues.Length - 1)
            throw new InvalidOperationException($"Meta growth '{row.Identifier}' costs must match its level transitions.");
        for (int i = 0; i < row.LevelCosts.Length; i++)
        {
            if (row.LevelCosts[i] <= 0)
                throw new InvalidOperationException($"Meta growth '{row.Identifier}' has a non-positive cost at level {i + 1}.");
        }
    }

    private static void ValidateLevelRow(LevelTable row)
    {
        if (!IsTutorialLevel(row.Identifier)
            && (row.DefaultArchetype == Archetype.None || row.DefaultArchetype == Archetype.Common))
        {
            throw new InvalidOperationException(
                $"Career level '{row.Identifier}' requires a selectable default starting industry.");
        }

        var rowUnlocks = new HashSet<Archetype>();
        Archetype[] unlocks = row.UnlockArchetype ?? Array.Empty<Archetype>();
        for (int i = 0; i < unlocks.Length; i++)
        {
            Archetype archetype = unlocks[i];
            if (archetype == Archetype.None || archetype == Archetype.Common)
            {
                throw new InvalidOperationException(
                    $"Career level '{row.Identifier}' has invalid unlocked industry '{archetype}'.");
            }
            if (!rowUnlocks.Add(archetype))
            {
                throw new InvalidOperationException(
                    $"Career level '{row.Identifier}' repeats unlocked industry '{archetype}'.");
            }
        }
    }

    private static void BuildArchetypeOrder()
    {
        s_ArchetypeOrder.Clear();
        var levels = new List<LevelTable>(s_Levels.Values);
        levels.Sort((left, right) =>
        {
            int idComparison = left.Id.CompareTo(right.Id);
            return idComparison != 0
                ? idComparison
                : string.Compare(left.Identifier, right.Identifier, StringComparison.Ordinal);
        });

        var seen = new HashSet<Archetype>();
        for (int levelIndex = 0; levelIndex < levels.Count; levelIndex++)
        {
            LevelTable level = levels[levelIndex];
            Archetype[] unlocks = level.UnlockArchetype ?? Array.Empty<Archetype>();
            for (int unlockIndex = 0; unlockIndex < unlocks.Length; unlockIndex++)
            {
                Archetype archetype = unlocks[unlockIndex];
                if (!seen.Add(archetype))
                {
                    throw new InvalidOperationException(
                        $"Industry '{archetype}' is unlocked more than once in LevelTable.");
                }
                s_ArchetypeOrder.Add(archetype);
            }
        }

        s_ArchetypeOrder.Reverse();
        s_ArchetypeOrder.Add(Archetype.Common);
    }

    private static void RequirePrepared()
    {
        if (!IsPrepared)
            throw new InvalidOperationException("Career configuration has not been prepared.");
    }
}
