using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameFramework;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityGameFramework.Runtime;

public sealed class CareerSystemsTests
{
    private Dictionary<string, LevelTable> m_PreviousLevels;
    private Dictionary<string, VariableExperimentRuleTable> m_PreviousRules;
    private Dictionary<string, MetaGrowthTable> m_PreviousGrowthRows;
    private Dictionary<int, GradeExperienceTable> m_PreviousGradeExperience;
    private Dictionary<int, LevelTagTable> m_PreviousLevelTags;
    private List<MetaGrowthTable> m_PreviousSortedGrowthRows;
    private List<GradeExperienceTable> m_PreviousSortedGradeExperience;
    private List<Archetype> m_PreviousArchetypeOrder;
    private bool m_PreviousPrepared;
    private int[] m_PreviousOffsetBadgeThresholds;
    private Fix64 m_PreviousOffsetExperienceCoefficient;

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
        CollectionAssert.AreEqual(new[] { Archetype.Coding }, levels["Lv_1"].UnlockArchetype);
        CollectionAssert.AreEqual(new[] { Archetype.Sightseeing }, levels["Lv_2"].UnlockArchetype);
        CollectionAssert.AreEqual(new[] { Archetype.Delivery }, levels["Lv_3"].UnlockArchetype);
        CollectionAssert.AreEqual(new[] { Archetype.Medical, Archetype.Sports }, levels["Lv_8"].UnlockArchetype);
        Assert.AreEqual(Archetype.Coding, levels["Lv_2"].DefaultArchetype);
        Assert.AreEqual(Archetype.Sightseeing, levels["Lv_3"].DefaultArchetype);
        Assert.AreEqual(1000, levels["LvTest"].Id);
        LevelData tutorialLevel = LevelData.FromRow(levels["Lv_1"]);
        CollectionAssert.AreEqual(
            new[] { LevelObjectiveIds.UpgradeCodingCoreLevel3 },
            tutorialLevel.PrimaryObjectives.Select(value => value.DefinitionId));
        LevelData levelTwo = LevelData.FromRow(levels["Lv_2"]);
        CollectionAssert.AreEqual(
            new[] { LevelObjectiveIds.CaptureSpecificStrongholds, LevelObjectiveIds.DefendBase },
            levelTwo.PrimaryObjectives.Select(value => value.DefinitionId));
        CollectionAssert.AreEqual(
            new[] { LevelObjectiveIds.SurviveDays, LevelObjectiveIds.ProtectStronghold },
            levelTwo.OptionalObjectives.Select(value => value.DefinitionId));
        CollectionAssert.AreEqual(new[] { 15, 10 }, levelTwo.OptionalObjectives.Select(value => value.Experience));
        CollectionAssert.AreEqual(new[] { (Fix64)2 }, levelTwo.OptionalObjectives[0].UniqueValues);
        LevelData levelThree = LevelData.FromRow(levels["Lv_3"]);
        CollectionAssert.AreEqual(
            new[] { LevelObjectiveIds.CaptureStrongholdCount, LevelObjectiveIds.SurviveDays },
            levelThree.OptionalObjectives.Select(value => value.DefinitionId));
        CollectionAssert.AreEqual(
            new[]
            {
                Archetype.Gardening,
                Archetype.Sports,
                Archetype.Medical,
                Archetype.Butchery,
                Archetype.Hunting,
                Archetype.Security,
                Archetype.Firefighting,
                Archetype.Delivery,
                Archetype.Sightseeing,
                Archetype.Coding,
                Archetype.Common
            },
            CareerConfigRuntime.ArchetypeOrder);

        Dictionary<string, LevelTable> experiments = GetStaticDictionary<LevelTable>("s_Experiments");
        LevelTable levelOneExperiment = experiments["Lv_1"];
        Assert.IsEmpty(levelOneExperiment.VariableLevelConfigIdentifier);
        Assert.AreEqual("VariableRule_OneHealthCoding", levelOneExperiment.VariableRuleIdentifier);
        Assert.AreEqual("LvTest", experiments["Lv_2"].VariableLevelConfigIdentifier);
        Assert.AreEqual("VariableRule_VariantTerrain", experiments["Lv_2"].VariableRuleIdentifier);

        VariableExperimentRuleTable rule = CareerConfigRuntime.GetRuleRequired(levelOneExperiment.VariableRuleIdentifier);
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
        StringAssert.DoesNotContain("OffsetRateRequiredForGrowthPoint", gameConfig);
        Assert.AreEqual(10, CareerConfigRuntime.OffsetBadgeBronzeThreshold);
        Assert.AreEqual(18, CareerConfigRuntime.OffsetBadgeSilverThreshold);
        Assert.AreEqual(24, CareerConfigRuntime.OffsetBadgeGoldThreshold);
        Assert.AreEqual(28, CareerConfigRuntime.OffsetBadgeDiamondThreshold);
        Assert.AreEqual(Fix64.Parse("0.1"), CareerConfigRuntime.OffsetRateExpCoefficient);
        CollectionAssert.AreEqual(
            new[] { 0, 100, 250, 450, 700, 1000 },
            CareerConfigRuntime.GradeExperienceRows.Select(row => row.RequiredTotalExperience));

        List<BuildingTable> buildingRows = LoadRows<BuildingTable>("AAAGame/DataTable/Build/BuildingTable.txt");
        Assert.AreEqual("Buil_ResearchCenter_Lv1",
            BuildingDataModel.ResolveStartingBaseIdentifier(buildingRows, Archetype.Coding));
        Assert.AreEqual("Buil_DreamPark_Lv1",
            BuildingDataModel.ResolveStartingBaseIdentifier(buildingRows, Archetype.Sightseeing));
        Assert.AreEqual("Buil_SortingCenter_Lv1",
            BuildingDataModel.ResolveStartingBaseIdentifier(buildingRows, Archetype.Delivery));
        Assert.AreEqual("Buil_FarmBase_Lv1",
            BuildingDataModel.ResolveStartingBaseIdentifier(buildingRows, Archetype.Butchery));
        foreach (Archetype archetype in Enum.GetValues(typeof(Archetype)))
        {
            if (archetype == Archetype.None || archetype == Archetype.Common)
                continue;
            Assert.DoesNotThrow(() => BuildingDataModel.ResolveStartingBaseIdentifier(buildingRows, archetype),
                $"Starting industry '{archetype}' must have exactly one base building.");
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildingDataModel.ResolveStartingBaseIdentifier(buildingRows, Archetype.Common));
    }

    [Test]
    public void RecordWin_DerivesPointsWithoutPersistentPointCounter()
    {
        CareerProgressDataModel progress = CreateProgressModel();
        Assert.IsEmpty(progress.GetUnlockedArchetypes());
        Assert.IsFalse(progress.IsVariableExperimentUnlocked("Lv_1"));

        CareerWinRecordResult firstNormal = progress.RecordWin("Lv_1", false, 10);
        Assert.IsTrue(firstNormal.FirstClear);
        Assert.AreEqual(2, firstNormal.OffsetBadgePointsGained);
        Assert.IsTrue(progress.IsVariableExperimentUnlocked("Lv_1"));
        CollectionAssert.AreEqual(new[] { Archetype.Coding }, firstNormal.UnlockedArchetypes);
        Assert.AreEqual(2, progress.GetEarnedPointCount());

        CareerWinRecordResult repeatNormal = progress.RecordWin("Lv_1", false, 20);
        Assert.IsFalse(repeatNormal.FirstClear);
        Assert.AreEqual(1, repeatNormal.OffsetBadgePointsGained);
        Assert.AreEqual(3, progress.GetEarnedPointCount());

        CareerWinRecordResult firstExperiment = progress.RecordWin("Lv_1", true, 0);
        Assert.IsTrue(firstExperiment.FirstClear);
        Assert.IsEmpty(firstExperiment.UnlockedArchetypes);
        Assert.AreEqual(4, progress.GetEarnedPointCount());

        progress.RecordWin("Lv_2", true, 10);
        Assert.AreEqual(7, progress.GetEarnedPointCount());
        CollectionAssert.AreEquivalent(
            new[] { Archetype.Coding },
            progress.GetUnlockedArchetypes(),
            "Experiment clears must not unlock LevelTable industries.");

        progress.RecordWin("Lv_2", false, 0);
        CollectionAssert.Contains(progress.GetUnlockedArchetypes(), Archetype.Sightseeing);
        Assert.AreEqual(7, progress.GetEarnedPointCount());

        CareerWinRecordResult multiUnlock = progress.RecordWin("Lv_8", false, 0);
        CollectionAssert.AreEqual(new[] { Archetype.Medical, Archetype.Sports }, multiUnlock.UnlockedArchetypes);
        CollectionAssert.IsSubsetOf(
            new[] { Archetype.Medical, Archetype.Sports },
            progress.GetUnlockedArchetypes());
        Assert.AreEqual(8, progress.GetEarnedPointCount());
    }

    [Test]
    public void OffsetBadgePoints_AreDerivedFromHighestRecordedTier()
    {
        CareerProgressDataModel progress = CreateProgressModel();

        CareerWinRecordResult plain = progress.RecordWin("Lv_1", false, 0);
        Assert.AreEqual(1, plain.OffsetBadgePointsGained);
        Assert.AreEqual(1, progress.GetEarnedPointCount());

        CareerWinRecordResult bronze = progress.RecordWin("Lv_1", false, 10);
        Assert.AreEqual(1, bronze.OffsetBadgePointsGained);
        Assert.AreEqual(2, progress.GetEarnedPointCount());

        CareerWinRecordResult gold = progress.RecordWin("Lv_1", false, 24);
        Assert.AreEqual(2, gold.OffsetBadgePointsGained);
        Assert.AreEqual(4, progress.GetEarnedPointCount());

        CareerWinRecordResult diamond = progress.RecordWin("Lv_1", false, 28);
        Assert.AreEqual(1, diamond.OffsetBadgePointsGained);
        Assert.AreEqual(5, progress.GetEarnedPointCount());

        CareerProgressDataModel directDiamond = CreateProgressModel();
        Assert.AreEqual(5, directDiamond.RecordWin("Lv_1", false, 28).OffsetBadgePointsGained);
        Assert.AreEqual(5, directDiamond.GetEarnedPointCount());
    }

    [Test]
    public void RecordWin_AwardsExperienceAppliesMultiplierAndUnlocksGradeTags()
    {
        CareerProgressDataModel progress = CreateProgressModel();
        Assert.AreEqual(0, progress.Experience);
        Assert.AreEqual(1, progress.CurrentGrade);

        CareerWinRecordResult first = progress.RecordWin("Lv_1", false, 0, 15);
        Assert.AreEqual(50, first.FirstClearExperience);
        Assert.AreEqual(20, first.ClearExperience);
        Assert.AreEqual(15, first.OptionalExperience);
        Assert.AreEqual(Fix64.One, first.ExperienceMultiplier);
        Assert.AreEqual(35, first.MultipliedExperience);
        Assert.AreEqual(85, first.TotalExperienceGained);
        Assert.AreEqual(85, progress.Experience);
        Assert.AreEqual(1, first.PreviousGrade);
        Assert.AreEqual(1, first.CurrentGrade);
        Assert.IsEmpty(first.UnlockedLevelTags);

        CareerWinRecordResult second = progress.RecordWin("Lv_2", false, 2, 10);
        Assert.AreEqual(
            Fix64.One + CareerConfigRuntime.OffsetRateExpCoefficient * (Fix64)2 * (Fix64)2,
            second.ExperienceMultiplier);
        Assert.AreEqual(42, second.MultipliedExperience);
        Assert.AreEqual(92, second.TotalExperienceGained);
        Assert.AreEqual(177, progress.Experience);
        Assert.AreEqual(1, second.PreviousGrade);
        Assert.AreEqual(2, second.CurrentGrade);
        CollectionAssert.AreEqual(
            GetStaticDictionary<int, LevelTagTable>("s_LevelTagsById").Values
                .Where(row => row.IsPositiveTag && row.UnlockGrade == 2)
                .OrderBy(row => row.Id)
                .Select(row => row.Id),
            second.UnlockedLevelTags.Select(row => row.Id));

        CareerWinRecordResult repeated = progress.RecordWin("Lv_2", false, 0, 0);
        Assert.AreEqual(0, repeated.FirstClearExperience);
        Assert.AreEqual(20, repeated.TotalExperienceGained);

        CareerWinRecordResult variable = progress.RecordWin("Lv_3", true, 0, 15);
        Assert.AreEqual(40, variable.FirstClearExperience);
        Assert.AreEqual(15, variable.ClearExperience);
        Assert.AreEqual(70, variable.TotalExperienceGained);
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

        string levelOneLine = File.ReadLines(Path.Combine(Application.dataPath, "AAAGame/DataTable/LevelTable.txt"))
            .Single(line => line.Contains("\tLv_1\t", StringComparison.Ordinal));
        LevelTable overrideLevel = ParseRow<LevelTable>(levelOneLine);
        SetInstanceProperty(overrideLevel, nameof(LevelTable.VariableLevelConfigIdentifier), "Lv_2");
        Dictionary<string, LevelTable> experiments = GetStaticDictionary<LevelTable>("s_Experiments");
        experiments["Lv_1"] = overrideLevel;

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

    [Test]
    public void InitialBasePlaceholder_UsesExplicitIdentifier()
    {
        Assert.IsTrue(EntityPresetPoint.IsInitialBaseIdentifier("InitBase_Lv1"));
        Assert.IsFalse(EntityPresetPoint.IsInitialBaseIdentifier("InitBase"));
        Assert.IsFalse(EntityPresetPoint.IsInitialBaseIdentifier("Buil_ResearchCenter_Lv1"));

        AssertInitialBaseCount("Level_1", 0);
        AssertInitialBaseCount("Level_2", 1);
        AssertInitialBaseCount("Level_3", 1);
        AssertInitialBaseCount("LvTest", 1);
    }

    [Test]
    public void StartingIndustry_DefaultsPerLevelAndRemembersForCurrentProcess()
    {
        var available = new[] { Archetype.Coding, Archetype.Sightseeing };
        Assert.AreEqual(
            Archetype.Sightseeing,
            CareerRunSettings.ResolveRememberedSelection("Lv_3", available, Archetype.Sightseeing));

        CareerRunSettings.RememberSelection("Lv_3", Archetype.Coding);
        Assert.AreEqual(
            Archetype.Coding,
            CareerRunSettings.ResolveRememberedSelection("Lv_3", available, Archetype.Sightseeing));
        Assert.Throws<InvalidOperationException>(() =>
            CareerRunSettings.ResolveRememberedSelection("Lv_4", available, Archetype.Delivery));

        var testLevelAvailable = new List<Archetype>();
        CareerRunSettings.EnsureDefaultSelectionAvailable(testLevelAvailable, Archetype.Hunting);
        CareerRunSettings.EnsureDefaultSelectionAvailable(testLevelAvailable, Archetype.Hunting);
        CollectionAssert.AreEqual(new[] { Archetype.Hunting }, testLevelAvailable);
        Assert.AreEqual(
            Archetype.Hunting,
            CareerRunSettings.ResolveRememberedSelection("LvTest", testLevelAvailable, Archetype.Hunting));

        var unordered = new List<Archetype> { Archetype.Sports, Archetype.Coding, Archetype.Medical };
        CareerRunSettings.EnsureDefaultSelectionAvailable(unordered, Archetype.Butchery);
        CollectionAssert.AreEqual(
            new[] { Archetype.Sports, Archetype.Medical, Archetype.Butchery, Archetype.Coding },
            unordered);

        var alreadyContainsDefault = new List<Archetype>
        {
            Archetype.Gardening,
            Archetype.Delivery,
            Archetype.Coding
        };
        CareerRunSettings.EnsureDefaultSelectionAvailable(alreadyContainsDefault, Archetype.Delivery);
        CollectionAssert.AreEqual(
            new[] { Archetype.Gardening, Archetype.Delivery, Archetype.Coding },
            alreadyContainsDefault);

        var buildingPanelOrder = new List<Archetype>
        {
            Archetype.Common,
            Archetype.Coding,
            Archetype.Hunting
        };
        CareerConfigRuntime.SortArchetypes(buildingPanelOrder);
        CollectionAssert.AreEqual(
            new[] { Archetype.Hunting, Archetype.Coding, Archetype.Common },
            buildingPanelOrder);
    }

    private static CareerProgressDataModel CreateProgressModel()
    {
        var progress = new CareerProgressDataModel();
        MethodInfo init = typeof(DataModelBase).GetMethod("Init", BindingFlags.Instance | BindingFlags.NonPublic)
                          ?? throw new InvalidOperationException("DataModelBase.Init was not found.");
        init.Invoke(progress, new object[] { 1, null });
        return progress;
    }

    private static void AssertInitialBaseCount(string prefabName, int expectedCount)
    {
        string path = $"Assets/AAAGame/Prefabs/Entity/Level/{prefabName}.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, $"Level prefab is missing: {path}");
        int actualCount = prefab.GetComponentsInChildren<EntityPresetPoint>(true).Count(point =>
            point.PointType == EntityPresetPointType.Building
            && EntityPresetPoint.IsInitialBaseIdentifier(point.Identifier));
        Assert.AreEqual(expectedCount, actualCount, path);
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
        m_PreviousRules = new Dictionary<string, VariableExperimentRuleTable>(GetStaticDictionary<VariableExperimentRuleTable>("s_Rules"), StringComparer.Ordinal);
        m_PreviousGrowthRows = new Dictionary<string, MetaGrowthTable>(GetStaticDictionary<MetaGrowthTable>("s_GrowthRows"), StringComparer.Ordinal);
        m_PreviousGradeExperience = new Dictionary<int, GradeExperienceTable>(GetStaticDictionary<int, GradeExperienceTable>("s_GradeExperience"));
        m_PreviousLevelTags = new Dictionary<int, LevelTagTable>(GetStaticDictionary<int, LevelTagTable>("s_LevelTagsById"));
        m_PreviousSortedGrowthRows = new List<MetaGrowthTable>(GetStaticList<MetaGrowthTable>("s_SortedGrowthRows"));
        m_PreviousSortedGradeExperience = new List<GradeExperienceTable>(GetStaticList<GradeExperienceTable>("s_SortedGradeExperience"));
        m_PreviousArchetypeOrder = new List<Archetype>(GetStaticList<Archetype>("s_ArchetypeOrder"));
        m_PreviousPrepared = CareerConfigRuntime.IsPrepared;
        m_PreviousOffsetBadgeThresholds = new[]
        {
            CareerConfigRuntime.OffsetBadgeBronzeThreshold,
            CareerConfigRuntime.OffsetBadgeSilverThreshold,
            CareerConfigRuntime.OffsetBadgeGoldThreshold,
            CareerConfigRuntime.OffsetBadgeDiamondThreshold
        };
        m_PreviousOffsetExperienceCoefficient = CareerConfigRuntime.OffsetRateExpCoefficient;
    }

    private static void InstallCareerConfigFromGeneratedTables()
    {
        ReplaceDictionary(GetStaticDictionary<LevelTable>("s_Levels"),
            LoadRows<LevelTable>("AAAGame/DataTable/LevelTable.txt").ToDictionary(row => row.Identifier, StringComparer.Ordinal));
        ReplaceDictionary(GetStaticDictionary<LevelTable>("s_Experiments"),
            LoadRows<LevelTable>("AAAGame/DataTable/LevelTable.txt")
                .Where(row => !string.IsNullOrWhiteSpace(row.VariableRuleIdentifier))
                .ToDictionary(row => row.Identifier, StringComparer.Ordinal));
        ReplaceDictionary(GetStaticDictionary<VariableExperimentRuleTable>("s_Rules"),
            LoadRows<VariableExperimentRuleTable>("AAAGame/DataTable/VariableExperimentRuleTable.txt").ToDictionary(row => row.Identifier, StringComparer.Ordinal));

        List<GradeExperienceTable> gradeRows = LoadRows<GradeExperienceTable>("AAAGame/DataTable/GradeExperienceTable.txt")
            .OrderBy(row => row.Id)
            .ToList();
        ReplaceDictionary(GetStaticDictionary<int, GradeExperienceTable>("s_GradeExperience"),
            gradeRows.ToDictionary(row => row.Id));
        ReplaceDictionary(GetStaticDictionary<int, LevelTagTable>("s_LevelTagsById"),
            LoadRows<LevelTagTable>("AAAGame/DataTable/LevelTagTable.txt").ToDictionary(row => row.Id));

        List<MetaGrowthTable> growthRows = LoadRows<MetaGrowthTable>("AAAGame/DataTable/MetaGrowthTable.txt")
            .OrderBy(row => row.Id)
            .ToList();
        ReplaceDictionary(GetStaticDictionary<MetaGrowthTable>("s_GrowthRows"),
            growthRows.ToDictionary(row => row.Identifier, StringComparer.Ordinal));
        List<MetaGrowthTable> sorted = GetStaticList<MetaGrowthTable>("s_SortedGrowthRows");
        sorted.Clear();
        sorted.AddRange(growthRows);
        List<GradeExperienceTable> sortedGrades = GetStaticList<GradeExperienceTable>("s_SortedGradeExperience");
        sortedGrades.Clear();
        sortedGrades.AddRange(gradeRows);
        List<Archetype> archetypeOrder = GetStaticList<Archetype>("s_ArchetypeOrder");
        archetypeOrder.Clear();
        foreach (LevelTable level in GetStaticDictionary<LevelTable>("s_Levels").Values.OrderBy(row => row.Id))
            archetypeOrder.AddRange(level.UnlockArchetype ?? Array.Empty<Archetype>());
        archetypeOrder.Reverse();
        archetypeOrder.Add(Archetype.Common);
        SetStaticAutoProperty("IsPrepared", true);
        SetStaticAutoProperty("OffsetBadgeBronzeThreshold", 10);
        SetStaticAutoProperty("OffsetBadgeSilverThreshold", 18);
        SetStaticAutoProperty("OffsetBadgeGoldThreshold", 24);
        SetStaticAutoProperty("OffsetBadgeDiamondThreshold", 28);
        SetStaticAutoProperty("OffsetRateExpCoefficient", Fix64.Parse("0.1"));
    }

    private void RestoreCareerConfig()
    {
        ReplaceDictionary(GetStaticDictionary<LevelTable>("s_Levels"), m_PreviousLevels);
        ReplaceDictionary(GetStaticDictionary<VariableExperimentRuleTable>("s_Rules"), m_PreviousRules);
        ReplaceDictionary(GetStaticDictionary<MetaGrowthTable>("s_GrowthRows"), m_PreviousGrowthRows);
        ReplaceDictionary(GetStaticDictionary<int, GradeExperienceTable>("s_GradeExperience"), m_PreviousGradeExperience);
        ReplaceDictionary(GetStaticDictionary<int, LevelTagTable>("s_LevelTagsById"), m_PreviousLevelTags);
        List<MetaGrowthTable> sorted = GetStaticList<MetaGrowthTable>("s_SortedGrowthRows");
        sorted.Clear();
        sorted.AddRange(m_PreviousSortedGrowthRows);
        List<GradeExperienceTable> sortedGrades = GetStaticList<GradeExperienceTable>("s_SortedGradeExperience");
        sortedGrades.Clear();
        sortedGrades.AddRange(m_PreviousSortedGradeExperience);
        List<Archetype> archetypeOrder = GetStaticList<Archetype>("s_ArchetypeOrder");
        archetypeOrder.Clear();
        archetypeOrder.AddRange(m_PreviousArchetypeOrder);
        SetStaticAutoProperty("IsPrepared", m_PreviousPrepared);
        SetStaticAutoProperty("OffsetBadgeBronzeThreshold", m_PreviousOffsetBadgeThresholds[0]);
        SetStaticAutoProperty("OffsetBadgeSilverThreshold", m_PreviousOffsetBadgeThresholds[1]);
        SetStaticAutoProperty("OffsetBadgeGoldThreshold", m_PreviousOffsetBadgeThresholds[2]);
        SetStaticAutoProperty("OffsetBadgeDiamondThreshold", m_PreviousOffsetBadgeThresholds[3]);
        SetStaticAutoProperty("OffsetRateExpCoefficient", m_PreviousOffsetExperienceCoefficient);
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

    private static Dictionary<TKey, TRow> GetStaticDictionary<TKey, TRow>(string fieldName)
    {
        return (Dictionary<TKey, TRow>)(typeof(CareerConfigRuntime).GetField(
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

    private static void ReplaceDictionary<TKey, TRow>(
        Dictionary<TKey, TRow> target,
        IReadOnlyDictionary<TKey, TRow> source)
    {
        target.Clear();
        foreach (KeyValuePair<TKey, TRow> pair in source)
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

    private static void SetInstanceProperty<TRow>(TRow row, string propertyName, object value)
    {
        PropertyInfo property = typeof(TRow).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"{typeof(TRow).Name}.{propertyName} was not found.");
        property.SetValue(row, value, null);
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
