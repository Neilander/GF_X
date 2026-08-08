using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameFramework;
using NUnit.Framework;
using UnityEngine;
using UnityGameFramework.Runtime;

public sealed class CareerSystemsTests
{
    private Dictionary<string, LevelTable> m_PreviousLevels;
    private Dictionary<string, VariableExperimentTable> m_PreviousExperiments;
    private Dictionary<string, VariableExperimentRuleTable> m_PreviousRules;
    private Dictionary<string, MetaGrowthTable> m_PreviousGrowthRows;
    private List<MetaGrowthTable> m_PreviousSortedGrowthRows;
    private bool m_PreviousPrepared;
    private int m_PreviousOffsetThreshold;

    [SetUp]
    public void SetUp()
    {
        SnapshotCareerConfig();
        InstallCareerConfigFromGeneratedTables();
        ResetRunSettings();
    }

    [TearDown]
    public void TearDown()
    {
        ResetRunSettings();
        RestoreCareerConfig();
    }

    [Test]
    public void GeneratedTables_HaveRequiredCareerConfiguration()
    {
        Dictionary<string, LevelTable> levels = GetStaticDictionary<LevelTable>("s_Levels");
        Assert.AreEqual(Archetype.Sightseeing, levels["Lv_1"].UnlockArchetype);
        Assert.AreEqual(Archetype.Delivery, levels["Lv_2"].UnlockArchetype);
        Assert.AreEqual(Archetype.Butchery, levels["Lv_3"].UnlockArchetype);
        Assert.AreEqual(Archetype.Firefighting, levels["Lv_4"].UnlockArchetype);
        Assert.AreEqual(Archetype.Security, levels["Lv_5"].UnlockArchetype);

        Dictionary<string, VariableExperimentTable> experiments = GetStaticDictionary<VariableExperimentTable>("s_Experiments");
        VariableExperimentTable levelOneExperiment = experiments["Lv_1"];
        Assert.IsEmpty(levelOneExperiment.LevelConfigIdentifier);
        Assert.AreEqual("VariableRule_OneHealthCoding", levelOneExperiment.RuleIdentifier);
        Assert.AreEqual("LvTest", experiments["Lv_2"].LevelConfigIdentifier);
        Assert.AreEqual("VariableRule_VariantTerrain", experiments["Lv_2"].RuleIdentifier);

        VariableExperimentRuleTable rule = CareerConfigRuntime.GetRuleRequired(levelOneExperiment.RuleIdentifier);
        Assert.AreEqual("VariableExperiment_Name_OneHealthCoding", rule.NameKey);
        Assert.AreEqual("VariableExperiment_Desc_OneHealthCoding", rule.DescKey);
        Assert.AreEqual(Archetype.Coding, rule.ForcedArchetype);
        CollectionAssert.AreEqual(new[] { Fix64.One }, rule.UniqueValues);

        VariableExperimentRuleTable terrainRule = CareerConfigRuntime.GetRuleRequired("VariableRule_VariantTerrain");
        Assert.AreEqual("VariableExperiment_Name_VariantTerrain", terrainRule.NameKey);
        Assert.AreEqual("VariableExperiment_Desc_VariantTerrain", terrainRule.DescKey);
        Assert.AreEqual(Archetype.None, terrainRule.ForcedArchetype);

        List<LocalizationTextTable> miscTexts = LoadRows<LocalizationTextTable>(
            "AAAGame/DataTable/Text/LocalizationTextTable_Misc.txt");
        Assert.AreEqual("VariableExperiment_Name_OneHealthCoding",
            miscTexts.Single(row => row.Identifier == rule.NameKey).TextKey);
        string chineseLocalization = File.ReadAllText(
            Path.Combine(Application.dataPath, "AAAGame/Language/ChineseSimplified.json"));
        StringAssert.Contains("\"VariableExperiment_Name_OneHealthCoding\":\"一命编程\"", chineseLocalization);
        StringAssert.Contains("\"VariableExperiment_Desc_OneHealthCoding\":\"所有单位生命上限固定为1，且初始行业固定为编程。\"", chineseLocalization);
        StringAssert.Contains("\"VariableExperiment_Name_VariantTerrain\":\"地形变体\"", chineseLocalization);
        StringAssert.Contains("\"VariableExperiment_Desc_VariantTerrain\":\"使用试验地形进行挑战。\"", chineseLocalization);

        IReadOnlyList<MetaGrowthTable> growthRows = CareerConfigRuntime.GrowthRows;
        Assert.AreEqual(11, growthRows.Count);
        Assert.IsTrue(growthRows.All(row => row.NameKey.StartsWith("MetaGrowth_Name_", StringComparison.Ordinal)));
        Assert.IsTrue(growthRows.All(row => row.DescKey.StartsWith("MetaGrowth_Desc_", StringComparison.Ordinal)));
        foreach (MetaGrowthTable growthRow in growthRows)
        {
            Assert.AreEqual(growthRow.NameKey,
                miscTexts.Single(row => row.Identifier == growthRow.NameKey).TextKey);
            Assert.AreEqual(growthRow.DescKey,
                miscTexts.Single(row => row.Identifier == growthRow.DescKey).TextKey);
        }
        StringAssert.Contains("\"MetaGrowth_Name_HeroAttack\":\"英雄攻击\"", chineseLocalization);
        StringAssert.Contains("\"MetaGrowth_Desc_HeroAttack\":\"英雄攻击+{0}%\"", chineseLocalization);
        AssertTableHeader("AAAGame/DataTable/MetaGrowthTable.txt", 4, "i18n");
        AssertTableHeader("AAAGame/DataTable/MetaGrowthTable.txt", 5, "i18n");
        AssertTableHeader("AAAGame/DataTable/VariableExperimentRuleTable.txt", 4, "i18n");
        AssertTableHeader("AAAGame/DataTable/VariableExperimentRuleTable.txt", 5, "i18n");
        AssertGrowth(growthRows, "HeroAttack", 15, 30, 1);
        AssertGrowth(growthRows, "HeroHealth", 15, 30, 1);
        AssertGrowth(growthRows, "UnitAttack", 20, 20, 1);
        AssertGrowth(growthRows, "UnitHealth", 20, 20, 1);
        AssertGrowth(growthRows, "BuildingAttack", 10, 20, 1);
        AssertGrowth(growthRows, "BuildingArmor", 3, 3, 4);
        AssertGrowth(growthRows, "BuildingHealth", 10, 20, 1);
        AssertGrowth(growthRows, "CoreUpgradeDiscount", 3, 3, 4);
        CollectionAssert.AreEqual(new[] { 0, 5, 3, 2, 1 },
            growthRows.Single(row => row.Identifier == "PeriodicOrange").UniqueValues.Select(value => (int)value));
        AssertGrowth(growthRows, "PeriodicOrange", 4, 1, 3);
        AssertGrowth(growthRows, "InitialOrange", 3, 3, 4);
        AssertGrowth(growthRows, "InitialFrequency", 5, 5, 2);

        string gameConfig = File.ReadAllText(Path.Combine(Application.dataPath, "AAAGame/Config/GameConfig.txt"));
        StringAssert.Contains("OffsetRateRequiredForGrowthPoint", gameConfig);
        Assert.AreEqual(10, CareerConfigRuntime.OffsetPointThreshold);
    }

    [Test]
    public void RecordWin_DerivesPointsWithoutPersistentPointCounter()
    {
        CareerProgressDataModel progress = CreateProgressModel();

        CareerWinRecordResult firstNormal = progress.RecordWin("Lv_1", false, 10);
        Assert.IsTrue(firstNormal.FirstClear);
        Assert.IsTrue(firstNormal.FirstOffsetReward);
        Assert.AreEqual(Archetype.Sightseeing, firstNormal.UnlockedArchetype);
        Assert.AreEqual(2, progress.GetEarnedPointCount());

        CareerWinRecordResult repeatNormal = progress.RecordWin("Lv_1", false, 20);
        Assert.IsFalse(repeatNormal.FirstClear);
        Assert.IsFalse(repeatNormal.FirstOffsetReward);
        Assert.AreEqual(2, progress.GetEarnedPointCount(), "Improving the same level offset must not award another point.");

        CareerWinRecordResult firstExperiment = progress.RecordWin("Lv_1", true, 0);
        Assert.IsTrue(firstExperiment.FirstClear);
        Assert.AreEqual(Archetype.None, firstExperiment.UnlockedArchetype);
        Assert.AreEqual(3, progress.GetEarnedPointCount());

        progress.RecordWin("Lv_2", true, 10);
        Assert.AreEqual(5, progress.GetEarnedPointCount());
        CollectionAssert.AreEquivalent(
            new[] { Archetype.Coding, Archetype.Sightseeing },
            progress.GetUnlockedArchetypes(),
            "Experiment clears must not unlock LevelTable industries.");

        progress.RecordWin("Lv_2", false, 0);
        CollectionAssert.Contains(progress.GetUnlockedArchetypes(), Archetype.Delivery);
        Assert.AreEqual(6, progress.GetEarnedPointCount());
    }

    [Test]
    public void GrowthLevelChanges_RefundAndMaxUseDerivedBudget()
    {
        CareerProgressDataModel progress = CreateProgressModel();
        progress.RecordWin("Lv_1", false, 10);
        progress.RecordWin("Lv_1", true, 0);
        progress.RecordWin("Lv_2", false, 0);
        Assert.AreEqual(4, progress.GetAvailablePointCount());

        Assert.IsTrue(progress.TrySetGrowthLevel("HeroAttack", 3, out string error), error);
        Assert.AreEqual(3, progress.GetSpentPointCount());
        Assert.AreEqual(1, progress.GetAvailablePointCount());
        Assert.AreEqual(4, progress.GetMaxAffordableLevel("HeroAttack"));

        Assert.IsFalse(progress.TrySetGrowthLevel("HeroAttack", 5, out error));
        StringAssert.Contains("requires", error);
        Assert.AreEqual(3, progress.GetGrowthLevel("HeroAttack"));

        Assert.IsTrue(progress.TrySetGrowthLevel("HeroAttack", 1, out error), error);
        Assert.AreEqual(3, progress.GetAvailablePointCount(), "Decreasing a level must refund its derived cost.");
        Assert.AreEqual(4, progress.GetMaxAffordableLevel("HeroAttack"));

        Assert.IsTrue(progress.TrySetGrowthLevel("HeroAttack", 4, out error), error);
        Assert.AreEqual(0, progress.GetAvailablePointCount());
        Assert.IsTrue(progress.TrySetGrowthLevel("HeroAttack", 0, out error), error);
        Assert.AreEqual(4, progress.GetAvailablePointCount());
    }

    [Test]
    public void VariableExperiment_UsesRuleAndOptionalLevelOverride()
    {
        string runtimeLevel = CareerRunSettings.BeginRun("Lv_1", true, Archetype.Coding);
        Assert.AreEqual("Lv_1", runtimeLevel, "An empty optional level config must use the original level.");
        Assert.IsTrue(CareerRunSettings.IsVariableExperiment);
        Assert.AreEqual(Fix64.One, CareerRuntimeEffects.GetVariableUnitMaxHealth());
        Assert.AreEqual(Fix64.Zero, CareerRuntimeEffects.GetEffectValue(MetaGrowthEffectType.HeroAttackPercent));

        VariableExperimentTable overrideExperiment = ParseRow<VariableExperimentTable>(
            "\t99\tOverride test\tLv_1\tLv_2\tVariableRule_OneHealthCoding");
        Dictionary<string, VariableExperimentTable> experiments = GetStaticDictionary<VariableExperimentTable>("s_Experiments");
        experiments["Lv_1"] = overrideExperiment;

        CareerRunSettings.CancelRun();
        runtimeLevel = CareerRunSettings.BeginRun("Lv_1", true, Archetype.Coding);
        Assert.AreEqual("Lv_2", runtimeLevel);
        Assert.AreEqual("Lv_1", CareerRunSettings.CareerLevelIdentifier);
        Assert.AreEqual("Lv_2", CareerRunSettings.RuntimeLevelIdentifier);
        Assert.Throws<InvalidOperationException>(() =>
            CareerRunSettings.BeginRun("Lv_1", true, Archetype.Sightseeing));

        CareerRunSettings.CancelRun();
        InstallCareerConfigFromGeneratedTables();
        runtimeLevel = CareerRunSettings.BeginRun("Lv_2", true, Archetype.Coding);
        Assert.AreEqual("LvTest", runtimeLevel);
        Assert.AreEqual("Lv_2", CareerRunSettings.CareerLevelIdentifier);
        Assert.AreEqual("LvTest", CareerRunSettings.RuntimeLevelIdentifier);
    }

    [Test]
    public void TutorialLevel_UsesCodingAndRejectsAllTagsThroughLogic()
    {
        Assert.IsTrue(CareerConfigRuntime.IsTutorialLevel("Lv_1"));
        Assert.IsFalse(CareerConfigRuntime.IsTutorialLevel("Lv_2"));

        List<LevelTagTable> generatedTags = LoadRows<LevelTagTable>("AAAGame/DataTable/LevelTagTable.txt");
        Assert.IsNotEmpty(generatedTags);
        Assert.IsTrue(generatedTags.All(row => !CareerConfigRuntime.IsTagAvailableForLevel(row, "Lv_1")));
        Assert.IsTrue(generatedTags.All(row => row.ExceptLevelID == null || !row.ExceptLevelID.Contains("Lv_1")),
            "Tutorial tag exclusion must be implemented in logic instead of repeated in every table row.");

        LevelTagTable scopedTag = ParseRow<LevelTagTable>(
            "\t999\tScoped tag\tTest\t1\t\tLvTag_TestScoped\t\t\t\tLv_2\tLv_3\tTrue\t0\t");
        Assert.IsTrue(CareerConfigRuntime.IsTagAvailableForLevel(scopedTag, "Lv_2"));
        Assert.IsFalse(CareerConfigRuntime.IsTagAvailableForLevel(scopedTag, "Lv_3"));
        Assert.IsFalse(CareerConfigRuntime.IsTagAvailableForLevel(scopedTag, "Lv_4"));

        Assert.AreEqual("Lv_1", CareerRunSettings.BeginRun("Lv_1", false, Archetype.Coding));
        Assert.AreEqual(Archetype.Coding, CareerRunSettings.StartingArchetype);
        Assert.Throws<InvalidOperationException>(() => LevelTagRuntime.SetActiveTagIds(new[] { generatedTags[0].Id }));
        Assert.Throws<InvalidOperationException>(() => LevelTagRuntime.SetActiveTagIdentifiers(new[] { generatedTags[0].Identifier }));
        Assert.Throws<InvalidOperationException>(() =>
            CareerRunSettings.BeginRun("Lv_1", false, Archetype.Sightseeing));
    }

    private static CareerProgressDataModel CreateProgressModel()
    {
        var progress = new CareerProgressDataModel();
        MethodInfo init = typeof(DataModelBase).GetMethod("Init", BindingFlags.Instance | BindingFlags.NonPublic)
                          ?? throw new InvalidOperationException("DataModelBase.Init was not found.");
        init.Invoke(progress, new object[] { 1, null });
        return progress;
    }

    private static void AssertGrowth(
        IReadOnlyList<MetaGrowthTable> rows,
        string identifier,
        int maxLevel,
        int finalValue,
        int perLevelCost)
    {
        MetaGrowthTable row = rows.Single(value => value.Identifier == identifier);
        Assert.AreEqual(maxLevel + 1, row.UniqueValues.Length);
        Assert.AreEqual(Fix64.Zero, row.UniqueValues[0]);
        Assert.AreEqual((Fix64)finalValue, row.UniqueValues[maxLevel]);
        Assert.AreEqual(maxLevel, row.LevelCosts.Length);
        CollectionAssert.AreEqual(Enumerable.Repeat(perLevelCost, maxLevel), row.LevelCosts);
    }

    private static void AssertTableHeader(string relativePath, int column, string expected)
    {
        string path = Path.Combine(Application.dataPath, relativePath);
        string firstLine = File.ReadLines(path).First();
        string[] columns = firstLine.Split('\t');
        Assert.Greater(columns.Length, column, $"Table header has no column {column}: {relativePath}");
        Assert.AreEqual(expected, columns[column]);
    }

    private void SnapshotCareerConfig()
    {
        m_PreviousLevels = new Dictionary<string, LevelTable>(GetStaticDictionary<LevelTable>("s_Levels"), StringComparer.Ordinal);
        m_PreviousExperiments = new Dictionary<string, VariableExperimentTable>(GetStaticDictionary<VariableExperimentTable>("s_Experiments"), StringComparer.Ordinal);
        m_PreviousRules = new Dictionary<string, VariableExperimentRuleTable>(GetStaticDictionary<VariableExperimentRuleTable>("s_Rules"), StringComparer.Ordinal);
        m_PreviousGrowthRows = new Dictionary<string, MetaGrowthTable>(GetStaticDictionary<MetaGrowthTable>("s_GrowthRows"), StringComparer.Ordinal);
        m_PreviousSortedGrowthRows = new List<MetaGrowthTable>(GetStaticList<MetaGrowthTable>("s_SortedGrowthRows"));
        m_PreviousPrepared = CareerConfigRuntime.IsPrepared;
        m_PreviousOffsetThreshold = CareerConfigRuntime.OffsetPointThreshold;
    }

    private static void InstallCareerConfigFromGeneratedTables()
    {
        ReplaceDictionary(GetStaticDictionary<LevelTable>("s_Levels"),
            LoadRows<LevelTable>("AAAGame/DataTable/LevelTable.txt").ToDictionary(row => row.Identifier, StringComparer.Ordinal));
        ReplaceDictionary(GetStaticDictionary<VariableExperimentTable>("s_Experiments"),
            LoadRows<VariableExperimentTable>("AAAGame/DataTable/VariableExperimentTable.txt").ToDictionary(row => row.LevelIdentifier, StringComparer.Ordinal));
        ReplaceDictionary(GetStaticDictionary<VariableExperimentRuleTable>("s_Rules"),
            LoadRows<VariableExperimentRuleTable>("AAAGame/DataTable/VariableExperimentRuleTable.txt").ToDictionary(row => row.Identifier, StringComparer.Ordinal));

        List<MetaGrowthTable> growthRows = LoadRows<MetaGrowthTable>("AAAGame/DataTable/MetaGrowthTable.txt")
            .OrderBy(row => row.Id)
            .ToList();
        ReplaceDictionary(GetStaticDictionary<MetaGrowthTable>("s_GrowthRows"),
            growthRows.ToDictionary(row => row.Identifier, StringComparer.Ordinal));
        List<MetaGrowthTable> sorted = GetStaticList<MetaGrowthTable>("s_SortedGrowthRows");
        sorted.Clear();
        sorted.AddRange(growthRows);
        SetStaticAutoProperty("IsPrepared", true);
        SetStaticAutoProperty("OffsetPointThreshold", 10);
    }

    private void RestoreCareerConfig()
    {
        ReplaceDictionary(GetStaticDictionary<LevelTable>("s_Levels"), m_PreviousLevels);
        ReplaceDictionary(GetStaticDictionary<VariableExperimentTable>("s_Experiments"), m_PreviousExperiments);
        ReplaceDictionary(GetStaticDictionary<VariableExperimentRuleTable>("s_Rules"), m_PreviousRules);
        ReplaceDictionary(GetStaticDictionary<MetaGrowthTable>("s_GrowthRows"), m_PreviousGrowthRows);
        List<MetaGrowthTable> sorted = GetStaticList<MetaGrowthTable>("s_SortedGrowthRows");
        sorted.Clear();
        sorted.AddRange(m_PreviousSortedGrowthRows);
        SetStaticAutoProperty("IsPrepared", m_PreviousPrepared);
        SetStaticAutoProperty("OffsetPointThreshold", m_PreviousOffsetThreshold);
    }

    private static List<TRow> LoadRows<TRow>(string relativePath)
        where TRow : DataRowBase, new()
    {
        string path = Path.Combine(Application.dataPath, relativePath);
        Assert.IsTrue(File.Exists(path), $"Generated table is missing: {path}");
        var rows = new List<TRow>();
        foreach (string line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                continue;
            rows.Add(ParseRow<TRow>(line));
        }
        return rows;
    }

    private static TRow ParseRow<TRow>(string line)
        where TRow : DataRowBase, new()
    {
        var row = new TRow();
        Assert.IsTrue(row.ParseDataRow(line, null), $"Failed to parse {typeof(TRow).Name}: {line}");
        return row;
    }

    private static Dictionary<string, TRow> GetStaticDictionary<TRow>(string fieldName)
    {
        return (Dictionary<string, TRow>)(typeof(CareerConfigRuntime).GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException($"CareerConfigRuntime.{fieldName} was not found."));
    }

    private static List<TRow> GetStaticList<TRow>(string fieldName)
    {
        return (List<TRow>)(typeof(CareerConfigRuntime).GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException($"CareerConfigRuntime.{fieldName} was not found."));
    }

    private static void ReplaceDictionary<TRow>(
        Dictionary<string, TRow> target,
        IReadOnlyDictionary<string, TRow> source)
    {
        target.Clear();
        foreach (KeyValuePair<string, TRow> pair in source)
            target.Add(pair.Key, pair.Value);
    }

    private static void SetStaticAutoProperty(string propertyName, object value)
    {
        FieldInfo field = typeof(CareerConfigRuntime).GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"CareerConfigRuntime.{propertyName} backing field was not found.");
        field.SetValue(null, value);
    }

    private static void ResetRunSettings()
    {
        LevelTagRuntime.ClearActiveTags();
        CareerRunSettings.CancelRun();
        var selections = (Dictionary<string, Archetype>)(typeof(CareerRunSettings).GetField(
            "s_LastSelections",
            BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException("CareerRunSettings.s_LastSelections was not found."));
        selections.Clear();
    }
}
