using System;
using System.Collections.Generic;
using GameFramework;
using Newtonsoft.Json;

[JsonObject(MemberSerialization.OptIn)]
public sealed class CareerProgressDataModel : DataModelStorageBase
{
    [JsonProperty]
    private HashSet<string> m_ClearedLevels;

    [JsonProperty]
    private HashSet<string> m_ClearedExperiments;

    [JsonProperty]
    private Dictionary<string, int> m_MaxOffsetRates;

    [JsonProperty]
    private Dictionary<string, int> m_GrowthLevels;

    [JsonProperty]
    private int m_Experience;

    protected override void OnInitialDataModel()
    {
        m_ClearedLevels = new HashSet<string>(StringComparer.Ordinal);
        m_ClearedExperiments = new HashSet<string>(StringComparer.Ordinal);
        m_MaxOffsetRates = new Dictionary<string, int>(StringComparer.Ordinal);
        m_GrowthLevels = new Dictionary<string, int>(StringComparer.Ordinal);
        m_Experience = 0;
    }

    public int Experience
    {
        get
        {
            EnsureLoaded();
            return m_Experience;
        }
    }

    public int CurrentGrade => CareerConfigRuntime.GetGradeForExperience(Experience);

    public bool HasClearedLevel(string levelIdentifier)
    {
        EnsureLoaded();
        return m_ClearedLevels.Contains(RequireLevelIdentifier(levelIdentifier));
    }

    public bool HasClearedExperiment(string levelIdentifier)
    {
        EnsureLoaded();
        return m_ClearedExperiments.Contains(RequireLevelIdentifier(levelIdentifier));
    }

    public bool IsVariableExperimentUnlocked(string levelIdentifier)
    {
        EnsureLoaded();
        string level = RequireLevelIdentifier(levelIdentifier);
        return CareerConfigRuntime.TryGetExperiment(level, out _) && m_ClearedLevels.Contains(level);
    }

    public int GetMaxOffsetRate(string levelIdentifier)
    {
        EnsureLoaded();
        return m_MaxOffsetRates.TryGetValue(RequireLevelIdentifier(levelIdentifier), out int value) ? value : 0;
    }

    public bool TryGetMaxOffsetRate(string levelIdentifier, out int offsetRate)
    {
        EnsureLoaded();
        return m_MaxOffsetRates.TryGetValue(RequireLevelIdentifier(levelIdentifier), out offsetRate);
    }

    public int GetGrowthLevel(string identifier)
    {
        EnsureLoaded();
        string resolved = RequireGrowthIdentifier(identifier);
        return m_GrowthLevels.TryGetValue(resolved, out int level) ? level : 0;
    }

    public int GetEarnedPointCount()
    {
        EnsureLoaded();
        int offsetRewards = 0;
        foreach (KeyValuePair<string, int> pair in m_MaxOffsetRates)
            offsetRewards = checked(offsetRewards + CareerConfigRuntime.GetOffsetBadgePointCount(pair.Value));
        return checked(m_ClearedExperiments.Count + offsetRewards);
    }

    public int GetSpentPointCount()
    {
        EnsureLoaded();
        int spent = 0;
        foreach (KeyValuePair<string, int> pair in m_GrowthLevels)
            spent = checked(spent + GetCostToLevel(CareerConfigRuntime.GetGrowthRequired(pair.Key), pair.Value));
        return spent;
    }

    public int GetAvailablePointCount()
    {
        int available = GetEarnedPointCount() - GetSpentPointCount();
        if (available < 0)
            throw new InvalidOperationException("Career growth spending exceeds points derived from career records.");
        return available;
    }

    public IReadOnlyList<Archetype> GetUnlockedArchetypes()
    {
        EnsureLoaded();
        var unlocked = new HashSet<Archetype>();
        foreach (string levelIdentifier in m_ClearedLevels)
        {
            Archetype[] levelUnlocks = CareerConfigRuntime.GetLevelRequired(levelIdentifier).UnlockArchetype;
            for (int i = 0; i < levelUnlocks.Length; i++)
                unlocked.Add(levelUnlocks[i]);
        }

        var result = new List<Archetype>(unlocked.Count);
        IReadOnlyList<Archetype> archetypeOrder = CareerConfigRuntime.ArchetypeOrder;
        for (int i = 0; i < archetypeOrder.Count; i++)
        {
            Archetype archetype = archetypeOrder[i];
            if (unlocked.Remove(archetype))
                result.Add(archetype);
        }
        if (unlocked.Count > 0)
            throw new InvalidOperationException("Career record contains an industry outside LevelTable unlock order.");
        return result;
    }

    public CareerWinRecordResult RecordWin(string levelIdentifier, bool isExperiment, int offsetRate)
    {
        return RecordWin(levelIdentifier, isExperiment, offsetRate, 0);
    }

    public CareerWinRecordResult RecordWin(
        string levelIdentifier,
        bool isExperiment,
        int offsetRate,
        int completedOptionalExperience)
    {
        EnsureLoaded();
        string level = RequireLevelIdentifier(levelIdentifier);
        if (!CareerConfigRuntime.IsCareerLevel(level))
            return CareerWinRecordResult.CreateIgnored(level, isExperiment, offsetRate, CurrentGrade);
        if (offsetRate < 0)
            throw new ArgumentOutOfRangeException(nameof(offsetRate), offsetRate, "Offset rate cannot be negative.");
        if (completedOptionalExperience < 0)
            throw new ArgumentOutOfRangeException(nameof(completedOptionalExperience));

        LevelTable levelRow = CareerConfigRuntime.GetLevelRequired(level);
        int previousGrade = CurrentGrade;
        bool firstClear = isExperiment
            ? m_ClearedExperiments.Add(level)
            : m_ClearedLevels.Add(level);
        bool hadOffsetRecord = m_MaxOffsetRates.TryGetValue(level, out int previousOffset);
        int previousBadgePoints = hadOffsetRecord
            ? CareerConfigRuntime.GetOffsetBadgePointCount(previousOffset)
            : 0;
        bool offsetImproved = !hadOffsetRecord || offsetRate > previousOffset;
        if (offsetImproved)
            m_MaxOffsetRates[level] = offsetRate;

        IReadOnlyList<Archetype> unlockedArchetypes = Array.Empty<Archetype>();
        if (firstClear && !isExperiment)
            unlockedArchetypes = levelRow.UnlockArchetype;

        int firstClearExperience = firstClear
            ? isExperiment ? levelRow.VariableFirstClearExperience : levelRow.FirstClearExperience
            : 0;
        int clearExperience = isExperiment ? levelRow.VariableClearExperience : levelRow.ClearExperience;
        Fix64 offset = (Fix64)offsetRate;
        Fix64 multiplier = Fix64.One + CareerConfigRuntime.OffsetRateExpCoefficient * offset * offset;
        int multipliedExperience = (int)Fix64.Floor((Fix64)checked(clearExperience + completedOptionalExperience) * multiplier);
        int totalExperienceGained = checked(firstClearExperience + multipliedExperience);
        m_Experience = checked(m_Experience + totalExperienceGained);
        int currentGrade = CurrentGrade;
        IReadOnlyList<LevelTagTable> unlockedLevelTags =
            CareerConfigRuntime.GetTagsUnlockedBetweenGrades(previousGrade, currentGrade);

        int currentBadgePoints = CareerConfigRuntime.GetOffsetBadgePointCount(Math.Max(previousOffset, offsetRate));
        int offsetBadgePointsGained = currentBadgePoints - previousBadgePoints;
        if (firstClear || offsetImproved || totalExperienceGained > 0)
            Save();

        return new CareerWinRecordResult(
            level,
            isExperiment,
            firstClear,
            offsetBadgePointsGained,
            offsetRate,
            unlockedArchetypes,
            firstClearExperience,
            clearExperience,
            completedOptionalExperience,
            multiplier,
            multipliedExperience,
            totalExperienceGained,
            previousGrade,
            currentGrade,
            unlockedLevelTags,
            false);
    }

    public bool TrySetGrowthLevel(string identifier, int targetLevel, out string errorMessage)
    {
        EnsureLoaded();
        MetaGrowthTable row = CareerConfigRuntime.GetGrowthRequired(RequireGrowthIdentifier(identifier));
        int maxLevel = row.UniqueValues.Length - 1;
        if (targetLevel < 0 || targetLevel > maxLevel)
        {
            errorMessage = $"Growth '{identifier}' target level {targetLevel} is outside 0..{maxLevel}.";
            return false;
        }

        int currentLevel = GetGrowthLevel(identifier);
        if (currentLevel == targetLevel)
        {
            errorMessage = null;
            return true;
        }

        int projectedSpent = checked(GetSpentPointCount()
                                     - GetCostToLevel(row, currentLevel)
                                     + GetCostToLevel(row, targetLevel));
        if (projectedSpent > GetEarnedPointCount())
        {
            errorMessage = $"Growth '{identifier}' requires {projectedSpent - GetSpentPointCount()} more point(s).";
            return false;
        }

        if (targetLevel == 0)
            m_GrowthLevels.Remove(identifier);
        else
            m_GrowthLevels[identifier] = targetLevel;
        Save();
        errorMessage = null;
        return true;
    }

    public int GetMaxAffordableLevel(string identifier)
    {
        MetaGrowthTable row = CareerConfigRuntime.GetGrowthRequired(RequireGrowthIdentifier(identifier));
        int currentLevel = GetGrowthLevel(identifier);
        int budget = checked(GetAvailablePointCount() + GetCostToLevel(row, currentLevel));
        int level = currentLevel;
        while (level < row.UniqueValues.Length - 1 && GetCostToLevel(row, level + 1) <= budget)
            level++;
        return level;
    }

    public IReadOnlyCollection<string> GetClearedLevelsForDebug()
    {
        EnsureLoaded();
        return m_ClearedLevels;
    }

    public IReadOnlyCollection<string> GetClearedExperimentsForDebug()
    {
        EnsureLoaded();
        return m_ClearedExperiments;
    }

    private void EnsureLoaded()
    {
        if (m_ClearedLevels == null || m_ClearedExperiments == null || m_MaxOffsetRates == null || m_GrowthLevels == null)
            throw new InvalidOperationException("Career progress data is incomplete.");
        if (m_Experience < 0)
            throw new InvalidOperationException("Career progress experience cannot be negative.");
    }

    private static int GetCostToLevel(MetaGrowthTable row, int level)
    {
        if (level < 0 || level > row.LevelCosts.Length)
            throw new InvalidOperationException($"Stored level {level} is invalid for growth '{row.Identifier}'.");
        int total = 0;
        for (int i = 0; i < level; i++)
            total = checked(total + row.LevelCosts[i]);
        return total;
    }

    private static string RequireLevelIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Career level identifier is empty.", nameof(value));
        return value;
    }

    private static string RequireGrowthIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Meta growth identifier is empty.", nameof(value));
        return value;
    }
}

public readonly struct CareerWinRecordResult
{
    public CareerWinRecordResult(
        string levelIdentifier,
        bool isExperiment,
        bool firstClear,
        int offsetBadgePointsGained,
        int offsetRate,
        IReadOnlyList<Archetype> unlockedArchetypes,
        int firstClearExperience,
        int clearExperience,
        int optionalExperience,
        Fix64 experienceMultiplier,
        int multipliedExperience,
        int totalExperienceGained,
        int previousGrade,
        int currentGrade,
        IReadOnlyList<LevelTagTable> unlockedLevelTags,
        bool ignored)
    {
        LevelIdentifier = levelIdentifier;
        IsExperiment = isExperiment;
        FirstClear = firstClear;
        OffsetBadgePointsGained = offsetBadgePointsGained;
        OffsetRate = offsetRate;
        UnlockedArchetypes = unlockedArchetypes ?? throw new ArgumentNullException(nameof(unlockedArchetypes));
        FirstClearExperience = firstClearExperience;
        ClearExperience = clearExperience;
        OptionalExperience = optionalExperience;
        ExperienceMultiplier = experienceMultiplier;
        MultipliedExperience = multipliedExperience;
        TotalExperienceGained = totalExperienceGained;
        PreviousGrade = previousGrade;
        CurrentGrade = currentGrade;
        UnlockedLevelTags = unlockedLevelTags ?? throw new ArgumentNullException(nameof(unlockedLevelTags));
        Ignored = ignored;
    }

    public string LevelIdentifier { get; }
    public bool IsExperiment { get; }
    public bool FirstClear { get; }
    public int OffsetBadgePointsGained { get; }
    public int OffsetRate { get; }
    public IReadOnlyList<Archetype> UnlockedArchetypes { get; }
    public int FirstClearExperience { get; }
    public int ClearExperience { get; }
    public int OptionalExperience { get; }
    public Fix64 ExperienceMultiplier { get; }
    public int MultipliedExperience { get; }
    public int TotalExperienceGained { get; }
    public int PreviousGrade { get; }
    public int CurrentGrade { get; }
    public IReadOnlyList<LevelTagTable> UnlockedLevelTags { get; }
    public bool Ignored { get; }

    public static CareerWinRecordResult CreateIgnored(string levelIdentifier, bool isExperiment, int offsetRate, int currentGrade)
    {
        return new CareerWinRecordResult(
            levelIdentifier,
            isExperiment,
            false,
            0,
            offsetRate,
            Array.Empty<Archetype>(),
            0,
            0,
            0,
            Fix64.One,
            0,
            0,
            currentGrade,
            currentGrade,
            Array.Empty<LevelTagTable>(),
            true);
    }
}
