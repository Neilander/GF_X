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
    private List<KeepsakeTable> m_PreviousKeepsakeRows;
    private bool m_PreviousKeepsakePrepared;

    [SetUp]
    public void SetUp()
    {
        SnapshotCareerConfig();
        SnapshotKeepsakeConfig();
        InstallCareerConfigFromGeneratedTables();
        InstallKeepsakeConfigFromGeneratedTable();
        ResetRunSettings();
    }

    [TearDown]
    public void TearDown()
    {
        ResetRunSettings();
        RestoreKeepsakeConfig();
        RestoreCareerConfig();
    }

    [Test]
    public void GeneratedTables_HaveRequiredCareerConfiguration()
    {
        Dictionary<string, LevelTable> levels = GetStaticDictionary<LevelTable>("s_Levels");
        CollectionAssert.AreEqual(new[] { Archetype.Coding }, levels["Lv_1"].UnlockArchetype);
        Assert.AreEqual(Archetype.None, levels["Lv_1"].DefaultArchetype);
        CollectionAssert.AreEqual(new[] { Archetype.Sightseeing }, levels["Lv_2"].UnlockArchetype);
        CollectionAssert.AreEqual(new[] { Archetype.Delivery }, levels["Lv_3"].UnlockArchetype);
        CollectionAssert.AreEqual(new[] { Archetype.Medical, Archetype.Sports }, levels["Lv_8"].UnlockArchetype);
        Assert.AreEqual(Archetype.Coding, levels["Lv_2"].DefaultArchetype);
        Assert.AreEqual(Archetype.Sightseeing, levels["Lv_3"].DefaultArchetype);
        Assert.AreEqual(1000, levels["LvTest"].Id);
        LevelData tutorialLevel = LevelData.FromRow(levels["Lv_1"]);
        CollectionAssert.AreEqual(
            new[] { LevelObjectiveIdentifiers.UpgradeCodingCoreLevel3 },
            tutorialLevel.PrimaryObjectives.Select(value => value.ObjectiveIdentifier));
        LevelData levelTwo = LevelData.FromRow(levels["Lv_2"]);
        CollectionAssert.AreEqual(
            new[] { LevelObjectiveIdentifiers.CaptureSpecificStrongholds, LevelObjectiveIdentifiers.DefendBase },
            levelTwo.PrimaryObjectives.Select(value => value.ObjectiveIdentifier));
        CollectionAssert.AreEqual(
            new[] { LevelObjectiveIdentifiers.SurviveDays, LevelObjectiveIdentifiers.ProtectStronghold },
            levelTwo.OptionalObjectives.Select(value => value.ObjectiveIdentifier));
        CollectionAssert.AreEqual(new[] { 15, 10 }, levelTwo.OptionalObjectives.Select(value => value.Experience));
        CollectionAssert.AreEqual(new[] { (Fix64)2 }, levelTwo.OptionalObjectives[0].UniqueValues);
        LevelData levelThree = LevelData.FromRow(levels["Lv_3"]);
        CollectionAssert.AreEqual(
            new[] { LevelObjectiveIdentifiers.CaptureStrongholdCount, LevelObjectiveIdentifiers.SurviveDays },
            levelThree.OptionalObjectives.Select(value => value.ObjectiveIdentifier));
        var objectiveIdentifiers = new HashSet<string>(
            LoadRows<ObjectiveTable>("AAAGame/DataTable/Level/ObjectiveTable.txt")
                .Select(value => value.Identifier),
            StringComparer.Ordinal);
        foreach (LevelTable level in levels.Values)
        {
            LevelData configured = LevelData.FromRow(level);
            Assert.IsTrue(
                configured.PrimaryObjectives
                    .Concat(configured.OptionalObjectives)
                    .All(value => objectiveIdentifiers.Contains(value.ObjectiveIdentifier)),
                $"Level '{level.Identifier}' references an objective outside ObjectiveTable.Identifier.");
        }
        string levelTableHeader = File.ReadLines(
            Path.Combine(Application.dataPath, "AAAGame/DataTable/Level/LevelTable.txt")).Skip(1).First();
        StringAssert.Contains("PrimaryObjective1Identifier", levelTableHeader);
        StringAssert.DoesNotContain("TargetIds", levelTableHeader);
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
        StringAssert.Contains("\"GoalUI.Collapse\":\"收起目标\"", chineseLocalization);
        StringAssert.Contains("\"GoalUI.Expand\":\"展开目标\"", chineseLocalization);
        StringAssert.Contains("\"LvEnter.Grade\":\"适配等级 {0}  适配度 {1}/{2}\"", chineseLocalization);

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
        AssertTableHeader("AAAGame/DataTable/Career/MetaGrowthTable.txt", 4, "i18n");
        AssertTableHeader("AAAGame/DataTable/Career/MetaGrowthTable.txt", 5, "i18n");
        AssertTableHeader("AAAGame/DataTable/Level/VariableExperimentRuleTable.txt", 4, "i18n");
        AssertTableHeader("AAAGame/DataTable/Level/VariableExperimentRuleTable.txt", 5, "i18n");
        AssertTableHeader("AAAGame/DataTable/Hero/KeepsakeTable.txt", 4, "i18n");
        AssertTableHeader("AAAGame/DataTable/Hero/KeepsakeTable.txt", 5, "i18n");
        Assert.AreEqual(6, KeepsakeConfigRuntime.Rows.Count);
        KeepsakeTable defaultKeepsake = KeepsakeConfigRuntime.GetDefaultRequired();
        Assert.AreEqual("Keepsake_Default", defaultKeepsake.Identifier);
        Assert.AreEqual("Hero_Keepsake_Default", defaultKeepsake.HeroCharacterKey);
        CollectionAssert.AreEqual(new[] { "Skill_Sweep" }, defaultKeepsake.InitialSkillIdentifiers);
        List<CharacterDataDetail> heroRows = LoadRows<CharacterDataDetail>(
                "AAAGame/DataTable/CharacterDataDetail.txt")
            .Where(row => row.UnitTags != null && row.UnitTags.Contains(UnitTag.Hero))
            .ToList();
        Assert.AreEqual(6, heroRows.Count);
        Assert.IsFalse(heroRows.Any(row => row.CharacterKey == "Unit_Hero"));
        CollectionAssert.AreEquivalent(
            heroRows.Select(row => row.CharacterKey),
            KeepsakeConfigRuntime.Rows.Select(row => row.HeroCharacterKey));
        Assert.AreEqual(6, KeepsakeConfigRuntime.Rows.Select(row => row.HeroCharacterKey).Distinct().Count());
        StringAssert.Contains("\"Keepsake.Name.Default\":\"\u57fa\u7840\u4fe1\u7269\"", chineseLocalization);
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
        CareerConfigRuntime.GetGradeProgressForExperience(177, out int currentExperience, out int requiredExperience);
        Assert.AreEqual(77, currentExperience);
        Assert.AreEqual(150, requiredExperience);
        CareerConfigRuntime.GetGradeProgressForExperience(1000, out currentExperience, out requiredExperience);
        Assert.AreEqual(300, currentExperience);
        Assert.AreEqual(300, requiredExperience);

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
        Assert.AreEqual(5, progress.GetEarnedPointCount());
        CollectionAssert.AreEquivalent(
            new[] { Archetype.Coding },
            progress.GetUnlockedArchetypes(),
            "Experiment clears must not unlock LevelTable industries.");

        progress.RecordWin("Lv_2", false, 0);
        CollectionAssert.Contains(progress.GetUnlockedArchetypes(), Archetype.Sightseeing);
        Assert.AreEqual(6, progress.GetEarnedPointCount());

        CareerWinRecordResult multiUnlock = progress.RecordWin("Lv_8", false, 0);
        CollectionAssert.AreEqual(new[] { Archetype.Medical, Archetype.Sports }, multiUnlock.UnlockedArchetypes);
        CollectionAssert.IsSubsetOf(
            new[] { Archetype.Medical, Archetype.Sports },
            progress.GetUnlockedArchetypes());
        Assert.AreEqual(7, progress.GetEarnedPointCount());
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
    public void LegacyNormalClears_AreMigratedToExplicitPlainOffsetRecords()
    {
        CareerProgressDataModel progress = CreateProgressModel();
        SetInstanceField(
            progress,
            "m_LegacyClearedLevels",
            new HashSet<string>(new[] { "Lv_1", "Lv_2", "Lv_3", "Lv_4" }, StringComparer.Ordinal));
        SetInstanceField(
            progress,
            "m_MaxOffsetRates",
            new Dictionary<string, int>(StringComparer.Ordinal) { ["Lv_2"] = 28 });

        Assert.AreEqual(8, progress.GetEarnedPointCount());
        Assert.IsTrue(progress.TryGetMaxOffsetRate("Lv_1", out int plainOffset));
        Assert.AreEqual(0, plainOffset);
        Assert.IsTrue(progress.HasClearedLevel("Lv_4"));
        FieldInfo legacyField = typeof(CareerProgressDataModel).GetField(
                                    "m_LegacyClearedLevels",
                                    BindingFlags.Instance | BindingFlags.NonPublic)
                                ?? throw new InvalidOperationException(
                                    "CareerProgressDataModel.m_LegacyClearedLevels was not found.");
        Assert.IsNull(legacyField.GetValue(progress));
    }

    [Test]
    public void ExperimentClear_DoesNotCreateNormalOffsetRecord()
    {
        CareerProgressDataModel progress = CreateProgressModel();

        CareerWinRecordResult result = progress.RecordWin("Lv_1", true, 28);

        Assert.IsTrue(result.FirstClear);
        Assert.AreEqual(0, result.OffsetBadgePointsGained);
        Assert.AreEqual(1, progress.GetEarnedPointCount());
        Assert.IsFalse(progress.HasClearedLevel("Lv_1"));
        Assert.IsFalse(progress.TryGetMaxOffsetRate("Lv_1", out _));
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
    public void KeepsakeUnlock_IsCommittedOnlyByWinningSettlement()
    {
        CareerProgressDataModel progress = CreateProgressModel();
        Assert.IsTrue(progress.IsKeepsakeUnlocked("Keepsake_Default"));
        Assert.IsFalse(progress.IsKeepsakeUnlocked("Keepsake_02"));

        CareerRunSettings.BeginRun("Lv_2", false, Archetype.Coding, "Keepsake_Default");
        KeepsakeSettlementUnlockService.RequestUnlock("Keepsake_02");
        Assert.IsFalse(progress.IsKeepsakeUnlocked("Keepsake_02"),
            "Requesting an unlock must not mutate career progress before settlement.");
        IReadOnlyList<KeepsakeTable> unlocked = KeepsakeSettlementUnlockService.Commit(progress);
        CollectionAssert.AreEqual(new[] { "Keepsake_02" }, unlocked.Select(row => row.Identifier));
        Assert.IsTrue(progress.IsKeepsakeUnlocked("Keepsake_02"));

        CareerRunSettings.CancelRun();
        CareerRunSettings.BeginRun("Lv_2", false, Archetype.Coding, "Keepsake_02");
        Assert.AreEqual("Keepsake_02", CareerRunSettings.KeepsakeIdentifier);
        KeepsakeSettlementUnlockService.RequestUnlock("Keepsake_03");
        KeepsakeSettlementUnlockService.DiscardPending();
        Assert.IsFalse(progress.IsKeepsakeUnlocked("Keepsake_03"),
            "A failed settlement must discard requested keepsake unlocks.");
    }

    [Test]
    public void VariableExperiment_UsesRuleAndOptionalLevelOverride()
    {
        string runtimeLevel = CareerRunSettings.BeginRun("Lv_1", true, Archetype.None);
        Assert.AreEqual("Lv_1", runtimeLevel, "An empty optional level config must use the original level.");
        Assert.IsTrue(CareerRunSettings.IsVariableExperiment);
        Assert.AreEqual(Fix64.One, CareerRuntimeEffects.GetVariableUnitMaxHealth());
        Assert.AreEqual(Fix64.Zero, CareerRuntimeEffects.GetEffectValue(MetaGrowthEffectType.HeroAttackPercent));

        string levelOneLine = File.ReadLines(Path.Combine(Application.dataPath, "AAAGame/DataTable/Level/LevelTable.txt"))
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
    public void MetaGrowth_AppliesOnlyToStandardNonTutorialRuns()
    {
        CareerRunSettings.BeginRun("Lv_2", false, Archetype.Coding);
        Assert.IsTrue(CareerRuntimeEffects.ShouldApplyGrowthForActiveRun());

        CareerRunSettings.CancelRun();
        CareerRunSettings.BeginRun("Lv_2", true, Archetype.Coding);
        Assert.IsFalse(CareerRuntimeEffects.ShouldApplyGrowthForActiveRun());
        Assert.AreEqual(Fix64.Zero, CareerRuntimeEffects.GetEffectValue(MetaGrowthEffectType.HeroAttackPercent));

        CareerRunSettings.CancelRun();
        CareerRunSettings.BeginRun("Lv_1", false, Archetype.None);
        Assert.IsFalse(CareerRuntimeEffects.ShouldApplyGrowthForActiveRun());
        Assert.AreEqual(Fix64.Zero, CareerRuntimeEffects.GetEffectValue(MetaGrowthEffectType.HeroAttackPercent));
    }

    [Test]
    public void TutorialLevel_HasNoStartingIndustryAndRejectsAllTagsThroughLogic()
    {
        Assert.IsTrue(CareerConfigRuntime.IsTutorialLevel("Lv_1"));
        Assert.IsFalse(CareerConfigRuntime.IsTutorialLevel("Lv_2"));

        List<LevelTagTable> generatedTags = LoadRows<LevelTagTable>("AAAGame/DataTable/Level/LevelTagTable.txt");
        Assert.IsNotEmpty(generatedTags);
        Assert.IsTrue(generatedTags.All(row => !CareerConfigRuntime.IsTagAvailableForLevel(row, "Lv_1")));
        Assert.IsTrue(generatedTags.All(row => row.ExceptLevelID == null || !row.ExceptLevelID.Contains("Lv_1")),
            "Tutorial tag exclusion must be implemented in logic instead of repeated in every table row.");

        LevelTagTable scopedTag = ParseRow<LevelTagTable>(
            "\t999\tScoped tag\tTest\t1\t\tLvTag_TestScoped\t\t\t\tLv_2\tLv_3\tTrue\t0\t");
        Assert.IsTrue(CareerConfigRuntime.IsTagAvailableForLevel(scopedTag, "Lv_2"));
        Assert.IsFalse(CareerConfigRuntime.IsTagAvailableForLevel(scopedTag, "Lv_3"));
        Assert.IsFalse(CareerConfigRuntime.IsTagAvailableForLevel(scopedTag, "Lv_4"));

        Assert.AreEqual("Lv_1", CareerRunSettings.BeginRun("Lv_1", false, Archetype.None));
        Assert.AreEqual(Archetype.None, CareerRunSettings.StartingArchetype);
        Assert.Throws<InvalidOperationException>(() => LevelTagRuntime.SetActiveTagIds(new[] { generatedTags[0].Id }));
        Assert.Throws<InvalidOperationException>(() => LevelTagRuntime.SetActiveTagIdentifiers(new[] { generatedTags[0].Identifier }));
        Assert.Throws<InvalidOperationException>(() =>
            CareerRunSettings.BeginRun("Lv_1", false, Archetype.Coding));
        Assert.Throws<InvalidOperationException>(() =>
            CareerRunSettings.BeginRun("Lv_1", false, Archetype.Sightseeing));
    }

    [Test]
    public void StartingIndustry_DoesNotGrantBuildArchetypeWithoutBaseMilestone()
    {
        CareerRunSettings.BeginRun("Lv_2", false, Archetype.Coding);
        Assert.AreEqual(Archetype.Coding, CareerRunSettings.StartingArchetype);

        MethodInfo collectMethod = typeof(BuildManager).GetMethod(
            "CollectPlayerUnlockedBaseArches",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(collectMethod);

        var withoutBase = (HashSet<Archetype>)collectMethod.Invoke(
            null,
            new object[] { new Func<Archetype, bool>(_ => false) });
        CollectionAssert.AreEquivalent(new[] { Archetype.Common }, withoutBase);

        var withCodingBase = (HashSet<Archetype>)collectMethod.Invoke(
            null,
            new object[] { new Func<Archetype, bool>(archetype => archetype == Archetype.Coding) });
        CollectionAssert.AreEquivalent(new[] { Archetype.Common, Archetype.Coding }, withCodingBase);
    }

    [Test]
    public void InitialBasePlaceholder_UsesExplicitIdentifier()
    {
        Assert.IsTrue(EntityPresetPoint.IsInitialBaseIdentifier("InitBase_Lv1"));
        Assert.IsFalse(EntityPresetPoint.IsInitialBaseIdentifier("InitBase"));
        Assert.IsFalse(EntityPresetPoint.IsInitialBaseIdentifier("Buil_ResearchCenter_Lv1"));

        Dictionary<string, LevelTable> levels = GetStaticDictionary<LevelTable>("s_Levels");
        AssertInitialBaseMatchesDefaultArchetype(levels["Lv_1"]);
        AssertInitialBaseMatchesDefaultArchetype(levels["Lv_2"]);
        AssertInitialBaseMatchesDefaultArchetype(levels["Lv_3"]);
        AssertInitialBaseMatchesDefaultArchetype(levels["LvTest"]);
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

    private static void SetInstanceField<T>(CareerProgressDataModel progress, string fieldName, T value)
    {
        FieldInfo field = typeof(CareerProgressDataModel).GetField(
                              fieldName,
                              BindingFlags.Instance | BindingFlags.NonPublic)
                          ?? throw new InvalidOperationException($"CareerProgressDataModel.{fieldName} was not found.");
        field.SetValue(progress, value);
    }

    private static void AssertInitialBaseMatchesDefaultArchetype(LevelTable level)
    {
        string path = $"Assets/AAAGame/Prefabs/Entity/{level.PrefabPath}.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, $"Level prefab is missing: {path}");
        int actualCount = prefab.GetComponentsInChildren<EntityPresetPoint>(true).Count(point =>
            point.PointType == EntityPresetPointType.Building
            && EntityPresetPoint.IsInitialBaseIdentifier(point.Identifier));
        int expectedCount = level.DefaultArchetype == Archetype.None ? 0 : 1;
        Assert.AreEqual(expectedCount, actualCount, $"{path} DefaultArchetype={level.DefaultArchetype}");
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

    private void SnapshotKeepsakeConfig()
    {
        m_PreviousKeepsakePrepared = KeepsakeConfigRuntime.IsPrepared;
        m_PreviousKeepsakeRows = m_PreviousKeepsakePrepared
            ? KeepsakeConfigRuntime.Rows.ToList()
            : new List<KeepsakeTable>();
    }

    private static void InstallCareerConfigFromGeneratedTables()
    {
        ReplaceDictionary(GetStaticDictionary<LevelTable>("s_Levels"),
            LoadRows<LevelTable>("AAAGame/DataTable/Level/LevelTable.txt").ToDictionary(row => row.Identifier, StringComparer.Ordinal));
        ReplaceDictionary(GetStaticDictionary<LevelTable>("s_Experiments"),
            LoadRows<LevelTable>("AAAGame/DataTable/Level/LevelTable.txt")
                .Where(row => !string.IsNullOrWhiteSpace(row.VariableRuleIdentifier))
                .ToDictionary(row => row.Identifier, StringComparer.Ordinal));
        ReplaceDictionary(GetStaticDictionary<VariableExperimentRuleTable>("s_Rules"),
            LoadRows<VariableExperimentRuleTable>("AAAGame/DataTable/Level/VariableExperimentRuleTable.txt").ToDictionary(row => row.Identifier, StringComparer.Ordinal));

        List<GradeExperienceTable> gradeRows = LoadRows<GradeExperienceTable>("AAAGame/DataTable/Career/GradeExperienceTable.txt")
            .OrderBy(row => row.Id)
            .ToList();
        ReplaceDictionary(GetStaticDictionary<int, GradeExperienceTable>("s_GradeExperience"),
            gradeRows.ToDictionary(row => row.Id));
        ReplaceDictionary(GetStaticDictionary<int, LevelTagTable>("s_LevelTagsById"),
            LoadRows<LevelTagTable>("AAAGame/DataTable/Level/LevelTagTable.txt").ToDictionary(row => row.Id));

        List<MetaGrowthTable> growthRows = LoadRows<MetaGrowthTable>("AAAGame/DataTable/Career/MetaGrowthTable.txt")
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

    private static void InstallKeepsakeConfigFromGeneratedTable()
    {
        KeepsakeConfigRuntime.PrepareForEditorTests(
            LoadRows<KeepsakeTable>("AAAGame/DataTable/Hero/KeepsakeTable.txt"));
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

    private void RestoreKeepsakeConfig()
    {
        KeepsakeConfigRuntime.ResetForEditorTests();
        if (m_PreviousKeepsakePrepared)
            KeepsakeConfigRuntime.PrepareForEditorTests(m_PreviousKeepsakeRows);
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
        var keepsakeSelections = (Dictionary<string, string>)(typeof(CareerRunSettings).GetField(
            "s_LastKeepsakeSelections",
            BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException("CareerRunSettings.s_LastKeepsakeSelections was not found."));
        keepsakeSelections.Clear();
    }
}
