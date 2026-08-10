using System;
using System.Collections.Generic;

public static class CareerConfigRuntime
{
    public const string OffsetRateExpCoefficientConfigKey = "OffsetRateExpCoefficient";
    public const string OffsetBadgeBronzeThresholdConfigKey = "OffsetBadgeBronzeThreshold";
    public const string OffsetBadgeSilverThresholdConfigKey = "OffsetBadgeSilverThreshold";
    public const string OffsetBadgeGoldThresholdConfigKey = "OffsetBadgeGoldThreshold";
    public const string OffsetBadgeDiamondThresholdConfigKey = "OffsetBadgeDiamondThreshold";
    public const string TutorialLevelIdentifier = "Lv_1";

    private static readonly Dictionary<string, LevelTable> s_Levels = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, LevelTable> s_Experiments = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, VariableExperimentRuleTable> s_Rules = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, MetaGrowthTable> s_GrowthRows = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, GradeExperienceTable> s_GradeExperience = new();
    private static readonly Dictionary<int, LevelTagTable> s_LevelTagsById = new();
    private static readonly List<MetaGrowthTable> s_SortedGrowthRows = new();
    private static readonly List<GradeExperienceTable> s_SortedGradeExperience = new();
    private static readonly List<Archetype> s_ArchetypeOrder = new();

    public static bool IsPrepared { get; private set; }
    public static Fix64 OffsetRateExpCoefficient { get; private set; }
    public static int OffsetBadgeBronzeThreshold { get; private set; }
    public static int OffsetBadgeSilverThreshold { get; private set; }
    public static int OffsetBadgeGoldThreshold { get; private set; }
    public static int OffsetBadgeDiamondThreshold { get; private set; }
    public static IReadOnlyList<MetaGrowthTable> GrowthRows => s_SortedGrowthRows;
    public static IReadOnlyList<GradeExperienceTable> GradeExperienceRows => s_SortedGradeExperience;
    public static IReadOnlyList<Archetype> ArchetypeOrder => s_ArchetypeOrder;

    public static void Prepare()
    {
        if (IsPrepared)
            return;
        if (GF.DataTable == null || GF.Config == null)
            throw new InvalidOperationException("Career configuration requires loaded data tables and game config.");

        BuildUniqueIndex(GF.DataTable.GetDataTable<LevelTable>(), s_Levels, row => row.Identifier, nameof(LevelTable));
        BuildUniqueIndex(GF.DataTable.GetDataTable<VariableExperimentRuleTable>(), s_Rules, row => row.Identifier, nameof(VariableExperimentRuleTable));
        BuildUniqueIndex(GF.DataTable.GetDataTable<MetaGrowthTable>(), s_GrowthRows, row => row.Identifier, nameof(MetaGrowthTable));
        BuildUniqueIndex(GF.DataTable.GetDataTable<GradeExperienceTable>(), s_GradeExperience, row => row.Id, nameof(GradeExperienceTable));
        BuildUniqueIndex(GF.DataTable.GetDataTable<LevelTagTable>(), s_LevelTagsById, row => row.Id, nameof(LevelTagTable));

        foreach (LevelTable level in s_Levels.Values)
            ValidateLevelRow(level);
        foreach (LevelTable level in s_Levels.Values)
        {
            if (string.IsNullOrWhiteSpace(level.VariableRuleIdentifier))
                continue;
            if (!s_Rules.ContainsKey(level.VariableRuleIdentifier))
                throw new InvalidOperationException($"Variable experiment level '{level.Identifier}' references missing rule '{level.VariableRuleIdentifier}'.");
            string variableLevel = string.IsNullOrWhiteSpace(level.VariableLevelConfigIdentifier)
                ? level.Identifier
                : level.VariableLevelConfigIdentifier;
            if (!s_Levels.ContainsKey(variableLevel))
                throw new InvalidOperationException($"Variable experiment level '{level.Identifier}' references missing level config '{variableLevel}'.");
        }
        foreach (VariableExperimentRuleTable rule in s_Rules.Values)
        {
            if (string.IsNullOrWhiteSpace(rule.NameKey) || string.IsNullOrWhiteSpace(rule.DescKey))
                throw new InvalidOperationException($"Variable experiment rule '{rule.Identifier}' requires localization keys.");
        }

        s_Experiments.Clear();
        foreach (LevelTable level in s_Levels.Values)
        {
            if (!string.IsNullOrWhiteSpace(level.VariableRuleIdentifier))
                s_Experiments.Add(level.Identifier, level);
        }

        s_SortedGrowthRows.Clear();
        s_SortedGrowthRows.AddRange(s_GrowthRows.Values);
        s_SortedGrowthRows.Sort((left, right) => left.Id.CompareTo(right.Id));
        for (int i = 0; i < s_SortedGrowthRows.Count; i++)
            ValidateGrowthRow(s_SortedGrowthRows[i]);

        s_SortedGradeExperience.Clear();
        s_SortedGradeExperience.AddRange(s_GradeExperience.Values);
        s_SortedGradeExperience.Sort((left, right) => left.Id.CompareTo(right.Id));
        ValidateGradeExperience();
        foreach (LevelTagTable tag in s_LevelTagsById.Values)
        {
            if (tag.UnlockGrade <= 0)
                throw new InvalidOperationException($"LevelTag '{tag.Identifier}' requires a positive UnlockGrade.");
        }

        string coefficientText = GF.Config.GetString(OffsetRateExpCoefficientConfigKey, string.Empty);
        if (string.IsNullOrWhiteSpace(coefficientText))
            throw new InvalidOperationException($"Game config '{OffsetRateExpCoefficientConfigKey}' must be configured.");
        OffsetRateExpCoefficient = Fix64.Parse(coefficientText);
        if (OffsetRateExpCoefficient < Fix64.Zero)
            throw new InvalidOperationException($"Game config '{OffsetRateExpCoefficientConfigKey}' cannot be negative.");
        OffsetBadgeBronzeThreshold = GetRequiredPositiveConfig(OffsetBadgeBronzeThresholdConfigKey);
        OffsetBadgeSilverThreshold = GetRequiredPositiveConfig(OffsetBadgeSilverThresholdConfigKey);
        OffsetBadgeGoldThreshold = GetRequiredPositiveConfig(OffsetBadgeGoldThresholdConfigKey);
        OffsetBadgeDiamondThreshold = GetRequiredPositiveConfig(OffsetBadgeDiamondThresholdConfigKey);
        if (OffsetBadgeBronzeThreshold >= OffsetBadgeSilverThreshold
            || OffsetBadgeSilverThreshold >= OffsetBadgeGoldThreshold
            || OffsetBadgeGoldThreshold >= OffsetBadgeDiamondThreshold)
        {
            throw new InvalidOperationException("Offset badge thresholds must be strictly increasing.");
        }
        BuildArchetypeOrder();
        IsPrepared = true;
    }

    public static LevelTable GetLevelRequired(string levelIdentifier)
    {
        RequirePrepared();
        if (string.IsNullOrWhiteSpace(levelIdentifier) || !s_Levels.TryGetValue(levelIdentifier, out LevelTable row))
            throw new InvalidOperationException($"Career level config is missing. level='{levelIdentifier}'.");
        return row;
    }

    public static bool TryGetExperiment(string levelIdentifier, out LevelTable experiment)
    {
        RequirePrepared();
        return s_Experiments.TryGetValue(levelIdentifier, out experiment);
    }

    public static LevelTable GetVariableLevelRequired(LevelTable source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        string identifier = string.IsNullOrWhiteSpace(source.VariableLevelConfigIdentifier)
            ? source.Identifier
            : source.VariableLevelConfigIdentifier;
        return GetLevelRequired(identifier);
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

    public static int GetGradeForExperience(int experience)
    {
        RequirePrepared();
        if (experience < 0)
            throw new ArgumentOutOfRangeException(nameof(experience));
        if (s_SortedGradeExperience.Count == 0)
            throw new InvalidOperationException("Grade experience configuration is empty.");
        int grade = 1;
        for (int i = 0; i < s_SortedGradeExperience.Count; i++)
        {
            if (s_SortedGradeExperience[i].RequiredTotalExperience > experience)
                break;
            grade = s_SortedGradeExperience[i].Id;
        }
        return grade;
    }

    public static CareerOffsetBadgeTier GetOffsetBadgeTier(int offsetRate)
    {
        RequirePrepared();
        if (offsetRate < 0)
            throw new ArgumentOutOfRangeException(nameof(offsetRate));
        if (offsetRate >= OffsetBadgeDiamondThreshold)
            return CareerOffsetBadgeTier.Diamond;
        if (offsetRate >= OffsetBadgeGoldThreshold)
            return CareerOffsetBadgeTier.Gold;
        if (offsetRate >= OffsetBadgeSilverThreshold)
            return CareerOffsetBadgeTier.Silver;
        if (offsetRate >= OffsetBadgeBronzeThreshold)
            return CareerOffsetBadgeTier.Bronze;
        return CareerOffsetBadgeTier.Plain;
    }

    public static int GetOffsetBadgePointCount(int offsetRate)
    {
        return checked((int)GetOffsetBadgeTier(offsetRate) + 1);
    }

    public static GradeExperienceTable GetGradeExperienceRequired(int grade)
    {
        RequirePrepared();
        if (!s_GradeExperience.TryGetValue(grade, out GradeExperienceTable row))
            throw new InvalidOperationException($"Grade experience row {grade} is missing.");
        return row;
    }

    public static IReadOnlyList<LevelTagTable> GetTagsUnlockedBetweenGrades(int previousGrade, int currentGrade)
    {
        RequirePrepared();
        if (previousGrade < 1 || currentGrade < previousGrade)
            throw new ArgumentOutOfRangeException(nameof(currentGrade));
        var result = new List<LevelTagTable>();
        foreach (LevelTagTable tag in s_LevelTagsById.Values)
        {
            if (tag.IsPositiveTag && tag.UnlockGrade > previousGrade && tag.UnlockGrade <= currentGrade)
                result.Add(tag);
        }
        result.Sort((left, right) => left.Id.CompareTo(right.Id));
        return result;
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
        archetypes.Sort((left, right) => GetArchetypeOrderIndexRequired(left).CompareTo(GetArchetypeOrderIndexRequired(right)));
    }

    public static bool IsCareerLevel(string levelIdentifier)
    {
        RequirePrepared();
        return !string.IsNullOrWhiteSpace(levelIdentifier)
               && !string.Equals(levelIdentifier, LevelSelectionService.TestLevelIdentifier, StringComparison.Ordinal)
               && s_Levels.ContainsKey(levelIdentifier);
    }

    public static bool IsTutorialLevel(string levelIdentifier) =>
        string.Equals(levelIdentifier, TutorialLevelIdentifier, StringComparison.Ordinal);

    public static bool IsTagAvailableForLevel(LevelTagTable row, string levelIdentifier, int playerGrade = int.MaxValue)
    {
        if (row == null)
            throw new ArgumentNullException(nameof(row));
        if (IsTutorialLevel(levelIdentifier))
            return false;
        if (row.UnlockGrade > playerGrade)
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

    private static void BuildUniqueIndex<TRow>(GameFramework.DataTable.IDataTable<TRow> table, Dictionary<string, TRow> target, Func<TRow, string> keySelector, string tableName)
        where TRow : GameFramework.DataTable.IDataRow
    {
        if (table == null)
            throw new InvalidOperationException($"Required data table '{tableName}' is not loaded.");
        target.Clear();
        TRow[] rows = table.GetAllDataRows();
        for (int i = 0; i < rows.Length; i++)
        {
            TRow row = rows[i] ?? throw new InvalidOperationException($"Data table '{tableName}' has a null row at {i}.");
            string key = keySelector(row);
            if (string.IsNullOrWhiteSpace(key) || !target.TryAdd(key, row))
                throw new InvalidOperationException($"Data table '{tableName}' has an invalid or duplicate key '{key}'.");
        }
    }

    private static void BuildUniqueIndex<TRow>(GameFramework.DataTable.IDataTable<TRow> table, Dictionary<int, TRow> target, Func<TRow, int> keySelector, string tableName)
        where TRow : GameFramework.DataTable.IDataRow
    {
        if (table == null)
            throw new InvalidOperationException($"Required data table '{tableName}' is not loaded.");
        target.Clear();
        TRow[] rows = table.GetAllDataRows();
        for (int i = 0; i < rows.Length; i++)
        {
            TRow row = rows[i] ?? throw new InvalidOperationException($"Data table '{tableName}' has a null row at {i}.");
            int key = keySelector(row);
            if (key <= 0 || !target.TryAdd(key, row))
                throw new InvalidOperationException($"Data table '{tableName}' has an invalid or duplicate key '{key}'.");
        }
    }

    private static void ValidateGradeExperience()
    {
        if (s_SortedGradeExperience.Count == 0 || s_SortedGradeExperience[0].Id != 1 || s_SortedGradeExperience[0].RequiredTotalExperience != 0)
            throw new InvalidOperationException("GradeExperienceTable must start at grade 1 with zero experience.");
        for (int i = 1; i < s_SortedGradeExperience.Count; i++)
        {
            if (s_SortedGradeExperience[i].Id != s_SortedGradeExperience[i - 1].Id + 1
                || s_SortedGradeExperience[i].RequiredTotalExperience <= s_SortedGradeExperience[i - 1].RequiredTotalExperience)
                throw new InvalidOperationException("GradeExperienceTable grades and thresholds must be contiguous and increasing.");
        }
    }

    private static void ValidateLevelRow(LevelTable row)
    {
        if (!IsTutorialLevel(row.Identifier) && (row.DefaultArchetype == Archetype.None || row.DefaultArchetype == Archetype.Common))
            throw new InvalidOperationException($"Career level '{row.Identifier}' requires a selectable default starting industry.");
        Archetype[] unlocks = row.UnlockArchetype ?? Array.Empty<Archetype>();
        var seen = new HashSet<Archetype>();
        for (int i = 0; i < unlocks.Length; i++)
        {
            if (unlocks[i] == Archetype.None || unlocks[i] == Archetype.Common || !seen.Add(unlocks[i]))
                throw new InvalidOperationException($"Career level '{row.Identifier}' has an invalid or duplicate unlocked industry.");
        }
        if (row.FirstClearExperience < 0 || row.ClearExperience < 0 || row.VariableFirstClearExperience < 0 || row.VariableClearExperience < 0)
            throw new InvalidOperationException($"Career level '{row.Identifier}' has negative experience configuration.");
    }

    private static void ValidateGrowthRow(MetaGrowthTable row)
    {
        if (string.IsNullOrWhiteSpace(row.NameKey) || string.IsNullOrWhiteSpace(row.DescKey))
            throw new InvalidOperationException($"Meta growth '{row.Identifier}' requires localization keys.");
        if (row.UniqueValues == null || row.UniqueValues.Length == 0 || row.UniqueValues[0] != Fix64.Zero)
            throw new InvalidOperationException($"Meta growth '{row.Identifier}' requires values including level 0.");
        if (row.LevelCosts == null || row.LevelCosts.Length != row.UniqueValues.Length - 1)
            throw new InvalidOperationException($"Meta growth '{row.Identifier}' costs do not match values.");
    }

    private static void BuildArchetypeOrder()
    {
        s_ArchetypeOrder.Clear();
        var levels = new List<LevelTable>(s_Levels.Values);
        levels.Sort((left, right) => left.Id.CompareTo(right.Id));
        var seen = new HashSet<Archetype>();
        for (int i = 0; i < levels.Count; i++)
        {
            Archetype[] unlocks = levels[i].UnlockArchetype ?? Array.Empty<Archetype>();
            for (int j = 0; j < unlocks.Length; j++)
            {
                if (!seen.Add(unlocks[j]))
                    throw new InvalidOperationException($"Industry '{unlocks[j]}' is unlocked more than once in LevelTable.");
                s_ArchetypeOrder.Add(unlocks[j]);
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

    private static int GetRequiredPositiveConfig(string key)
    {
        int value = GF.Config.GetInt(key, int.MinValue);
        if (value <= 0)
            throw new InvalidOperationException($"Game config '{key}' must be a positive integer.");
        return value;
    }
}

public enum CareerOffsetBadgeTier
{
    Plain,
    Bronze,
    Silver,
    Gold,
    Diamond
}
