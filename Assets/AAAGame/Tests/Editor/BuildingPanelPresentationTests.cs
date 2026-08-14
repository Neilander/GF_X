using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[TestFixture]
public sealed class BuildingPanelPresentationTests
{
    private const string BuildingTableRelativePath = "AAAGame/DataTable/Build/BuildingTable.txt";
    private const string CharacterTableRelativePath = "AAAGame/DataTable/CharacterDataDetail.txt";

    [SetUp]
    public void SetUp()
    {
        LogicRuntimeDataTableCache.PrepareForEditorTests(
            LoadRows(CharacterTableRelativePath, ParseCharacter),
            LoadRows(BuildingTableRelativePath, ParseBuilding),
            Array.Empty<LevelTagTable>());
    }

    [TearDown]
    public void TearDown()
    {
        LogicRuntimeDataTableCache.ResetForEditorTests();
    }

    [Test]
    public void FormatBaseWithTotalModifier_ProductionPenaltyShowsBaseMinusTotalDelta()
    {
        Assert.AreEqual("4-1", BuildingPanelPresentation.FormatBaseWithTotalModifier(4, 3));
    }

    [Test]
    public void FirefighterLevelOneStats_IncludeConfiguredSplashRadius()
    {
        BuildingData data = CreateArmyBuilding("Buil_FireDrillSite_Lv1", "Unit_Firefighter", 1);
        var stats = new List<BuildingPanelStat>();

        BuildingPanelPresentation.CollectUnitStats(data, stats);

        AssertStat(stats, BuildingPanelPresentation.SplashGlyph, "U 250");
    }

    [Test]
    public void InterviewRoomLevelThreeDescriptionValue_UsesFinalLifetime()
    {
        Fix64[] values = SoldierFactory.ResolveArmyPresentationAbilityValues(UnitType.Unit_Intern, 3);

        Assert.AreEqual((Fix64)33, values[0]);
    }

    [Test]
    public void LateRiderDescriptions_UseAllFinalChargeValuesAtEachLevel()
    {
        CollectionAssert.AreEqual(
            new[] { (Fix64)200, (Fix64)700, (Fix64)460, (Fix64)50, (Fix64)5 },
            SoldierFactory.ResolveArmyPresentationAbilityValues(UnitType.Unit_LateRider, 2));
        CollectionAssert.AreEqual(
            new[] { (Fix64)200, (Fix64)800, (Fix64)580, (Fix64)70, (Fix64)5 },
            SoldierFactory.ResolveArmyPresentationAbilityValues(UnitType.Unit_LateRider, 3));
    }

    [Test]
    public void SouvenirStandLevelTwoDescription_UsesFinalCap()
    {
        BuildingTable row = FindBuilding("Buil_SouvenirStand");
        BuildingData data = CreateBuilding(row, 2, row.UniqueValues);

        Fix64[] values = BuildingPanelPresentation.ResolveBuildingDescriptionValues(data);

        Assert.AreEqual((Fix64)4, values[2]);
    }

    [Test]
    public void BallLauncherLevelThreeDescription_UsesFinalReloadDelay()
    {
        BuildingTable row = FindBuilding("Buil_BallLauncher");
        BuildingData data = CreateBuilding(row, 3, row.UniqueValues);

        Fix64[] values = BuildingPanelPresentation.ResolveBuildingDescriptionValues(data);

        Assert.AreEqual((Fix64)12, values[0]);
    }

    [Test]
    public void HarvesterLevelThreeStats_IncludeFinalHealOnHit()
    {
        BuildingData data = CreateArmyBuilding("Buil_HarvestStation_Lv3", "Unit_Harvester", 3);
        var stats = new List<BuildingPanelStat>();

        BuildingPanelPresentation.CollectUnitStats(data, stats);

        AssertStat(stats, BuildingPanelPresentation.HealOnHitGlyph, "U +3");
    }

    [Test]
    public void BuildingInfoItem_UsesTwoRowPropertyGridWithCapacityForArmyStats()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/BuildingInfoItem.prefab");
        Assert.IsNotNull(prefab);

        Transform propertyList = FindChild(prefab.transform, "PropertyList");
        GridLayoutGroup grid = propertyList.GetComponent<GridLayoutGroup>();
        RectTransform rect = (RectTransform)propertyList;

        Assert.IsNotNull(grid);
        Assert.AreEqual(GridLayoutGroup.Constraint.FixedColumnCount, grid.constraint);
        Assert.AreEqual(7, grid.constraintCount);
        Assert.GreaterOrEqual(rect.rect.width, grid.cellSize.x * 7 + grid.spacing.x * 6);
        Assert.GreaterOrEqual(rect.rect.height, grid.cellSize.y * 2 + grid.spacing.y);
    }

    [Test]
    public void IconNumItemFont_ContainsEveryPanelGlyph()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/IconNumItem.prefab");
        Assert.IsNotNull(prefab);

        TMP_Text number = null;
        TMP_Text[] texts = prefab.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i].name == "Num")
            {
                number = texts[i];
                break;
            }
        }

        Assert.IsNotNull(number);
        string[] glyphs =
        {
            BuildingPanelPresentation.HealthGlyph,
            BuildingPanelPresentation.DefenseGlyph,
            BuildingPanelPresentation.AttackGlyph,
            BuildingPanelPresentation.IntervalGlyph,
            BuildingPanelPresentation.RangeGlyph,
            BuildingPanelPresentation.MoveSpeedGlyph,
            BuildingPanelPresentation.SplashGlyph,
            BuildingPanelPresentation.AmmoGlyph,
            BuildingPanelPresentation.ProjectileCountGlyph,
            BuildingPanelPresentation.SplitAngleGlyph,
            BuildingPanelPresentation.SplitDistanceGlyph,
            BuildingPanelPresentation.CriticalGlyph,
            BuildingPanelPresentation.HealOnHitGlyph,
        };
        for (int i = 0; i < glyphs.Length; i++)
            Assert.IsTrue(number.font.HasCharacters(glyphs[i]), $"Panel font is missing label '{glyphs[i]}'.");
    }

    [Test]
    public void BuildingInfoItem_ThirteenArmyStatsStayInsideTwoRowGridWithoutOverlap()
    {
        GameObject infoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/BuildingInfoItem.prefab");
        GameObject statPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/IconNumItem.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(infoPrefab);
        try
        {
            RectTransform propertyList = (RectTransform)FindChild(instance.transform, "PropertyList");
            var items = new List<RectTransform>();
            for (int i = 0; i < 13; i++)
            {
                GameObject item = UnityEngine.Object.Instantiate(statPrefab, propertyList);
                items.Add((RectTransform)item.transform);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(propertyList);
            var occupied = new List<Rect>();
            for (int i = 0; i < items.Count; i++)
            {
                Rect bounds = GetBoundsInParent(items[i], propertyList);
                Assert.GreaterOrEqual(bounds.xMin, propertyList.rect.xMin - 0.01f);
                Assert.LessOrEqual(bounds.xMax, propertyList.rect.xMax + 0.01f);
                Assert.GreaterOrEqual(bounds.yMin, propertyList.rect.yMin - 0.01f);
                Assert.LessOrEqual(bounds.yMax, propertyList.rect.yMax + 0.01f);
                for (int previous = 0; previous < occupied.Count; previous++)
                    Assert.IsFalse(bounds.Overlaps(occupied[previous]), $"Property cells overlap. first={previous}, second={i}.");
                occupied.Add(bounds);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static void AssertStat(List<BuildingPanelStat> stats, string glyph, string value)
    {
        for (int i = 0; i < stats.Count; i++)
        {
            if (stats[i].Glyph == glyph && stats[i].Value == value)
                return;
        }

        Assert.Fail($"Expected panel stat was not found. glyph={glyph}, value={value}.");
    }

    private static Transform FindChild(Transform root, string name)
    {
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == name)
                return children[i];
        }

        throw new InvalidOperationException($"Prefab child was not found. name={name}.");
    }

    private static Rect GetBoundsInParent(RectTransform child, RectTransform parent)
    {
        var corners = new Vector3[4];
        child.GetWorldCorners(corners);
        Vector3 min = parent.InverseTransformPoint(corners[0]);
        Vector3 max = parent.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static BuildingData CreateArmyBuilding(string identifier, string unitId, int level)
    {
        return new BuildingData(
            identifier,
            BuilType.Army,
            Archetype.None,
            string.Empty,
            string.Empty,
            string.Empty,
            level,
            0,
            Fix64.One,
            null,
            Fix64.Zero,
            Array.Empty<Fix64>(),
            unitId,
            1,
            null);
    }

    private static BuildingData CreateBuilding(BuildingTable row, int level, Fix64[] uniqueValues)
    {
        return new BuildingData(
            row.Identifier + "_Lv" + level,
            row.Type,
            row.Archetype,
            string.Empty,
            row.NameKey,
            row.DescKey,
            level,
            0,
            Fix64.One,
            null,
            Fix64.Zero,
            uniqueValues,
            row.UnitID,
            0,
            null);
    }

    private static BuildingTable FindBuilding(string identifier)
    {
        if (LogicRuntimeDataTableCache.TryGetBuilding(identifier, out BuildingTable row))
            return row;
        throw new InvalidOperationException($"Building table row was not found. building={identifier}.");
    }

    private static CharacterDataDetail ParseCharacter(string line)
    {
        var row = new CharacterDataDetail();
        Assert.IsTrue(row.ParseDataRow(line, null));
        return row;
    }

    private static BuildingTable ParseBuilding(string line)
    {
        var row = new BuildingTable();
        Assert.IsTrue(row.ParseDataRow(line, null));
        return row;
    }

    private static T[] LoadRows<T>(string relativePath, Func<string, T> parser)
    {
        string path = Path.Combine(Application.dataPath, relativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("Data table was not found.", path);

        var rows = new List<T>();
        foreach (string line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                continue;

            rows.Add(parser(line));
        }

        return rows.ToArray();
    }
}
