using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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

    [TestCase(1, "I")]
    [TestCase(2, "II")]
    [TestCase(3, "III")]
    public void BuildingLevelTitle_UsesRomanNumerals(int level, string expected)
    {
        Assert.AreEqual(expected, BuildingPanelPresentation.GetBuildingLevelTitle(level));
    }

    [Test]
    public void BuildingName_OnlyAddsLevelSuffixAboveLevelOne()
    {
        Assert.AreEqual("Camp", BuildingPanelPresentation.FormatBuildingName("Camp", 1));
        Assert.AreEqual("Camp-Lv2", BuildingPanelPresentation.FormatBuildingName("Camp", 2));
    }

    [Test]
    public void SkillName_AlwaysUsesHyphenBeforeLevel()
    {
        Assert.AreEqual("Skill-Lv1", BuildingPanelPresentation.FormatSkillName("Skill", 1));
        Assert.AreEqual("Skill-Lv3", BuildingPanelPresentation.FormatSkillName("Skill", 3));
    }

    [TestCase(true, 0, 0)]
    [TestCase(false, 0, 1)]
    [TestCase(true, 1, 1)]
    [TestCase(false, 1, 2)]
    [TestCase(true, 2, 2)]
    [TestCase(false, 2, 3)]
    public void SkillDetailLevel_SeparatesCurrentAndUpgradeFrames(
        bool currentBuilding,
        int currentLevel,
        int expected)
    {
        Assert.AreEqual(
            expected,
            BuildingUpgradeTips.ResolveSkillDetailLevel(currentBuilding, currentLevel));
    }

    [Test]
    public void UpgradeConditions_IgnoreInactivePooledItems()
    {
        GameObject areaObject = new("ConditionArea", typeof(RectTransform));
        GameObject pooledItem = new("PooledCondition", typeof(RectTransform));
        try
        {
            pooledItem.transform.SetParent(areaObject.transform, false);
            pooledItem.SetActive(false);

            Assert.IsFalse(BuildingUpgradeTips.HasActiveConditionItem(
                areaObject.GetComponent<RectTransform>()));

            pooledItem.SetActive(true);
            Assert.IsTrue(BuildingUpgradeTips.HasActiveConditionItem(
                areaObject.GetComponent<RectTransform>()));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(areaObject);
        }
    }

    [Test]
    public void BuildingInfoPreviewFrameKeepsOriginalKeyholeHeight()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/BuildingInfoItem.prefab");
        Assert.IsNotNull(prefab);

        RectTransform preview = (RectTransform)FindChild(prefab.transform, "Preview");
        Assert.AreEqual(132f, preview.rect.height, 0.01f);
    }

    [TestCase(1, "I\u2192II")]
    [TestCase(2, "II\u2192III")]
    public void UpgradeLevelTitle_ShowsCurrentAndNextLevel(int level, string expected)
    {
        Assert.AreEqual(expected, BuildingPanelPresentation.GetUpgradeLevelTitle(level));
    }

    [TestCase("训练单位：单位说明", "训练单位")]
    [TestCase("Train unit: unit description", "Train unit")]
    [TestCase("No unit suffix", "No unit suffix")]
    public void StripUnitDescriptionSuffix_RemovesOnlyColonSuffix(string source, string expected)
    {
        Assert.AreEqual(expected, BuildingPanelPresentation.StripUnitDescriptionSuffix(source));
    }

    [Test]
    public void BuildingDescriptionTemplate_FillsIndustryLevelAndUnitNames()
    {
        string result = BuildingPanelPresentation.FillBuildingDescriptionTemplate(
            "{Arch}|{Lv}|{Unit}",
            "Industry",
            2,
            "Unit");

        Assert.AreEqual("Industry|2|Unit", result);
    }

    [Test]
    public void FirefighterLevelOneStats_IncludeConfiguredSplashRadius()
    {
        BuildingData data = CreateArmyBuilding("Buil_FireDrillSite_Lv1", "Unit_Firefighter", 1);
        var stats = new List<BuildingPanelStat>();

        BuildingPanelPresentation.CollectUnitStats(data, stats);

        AssertStat(stats, BuildingPanelPresentation.SplashGlyph, "250");
        Assert.IsFalse(stats.Exists(stat => stat.Value.StartsWith("B ", StringComparison.Ordinal)));
        Assert.IsFalse(stats.Exists(stat => stat.Value.StartsWith("U ", StringComparison.Ordinal)));
    }

    [TestCase(WeaponType.Melee, true)]
    [TestCase(WeaponType.CleaveMelee, true)]
    [TestCase(WeaponType.HealMelee, true)]
    [TestCase(WeaponType.Projectile, false)]
    [TestCase(WeaponType.CleaveRanged, false)]
    public void IsMeleeWeaponType_UsesMeleeWeaponTypeName(WeaponType type, bool expected)
    {
        Assert.AreEqual(expected, BuildingPanelPresentation.IsMeleeWeaponType(type));
    }

    [Test]
    public void ChineseLocalization_DefinesMeleeRangeText()
    {
        string path = Path.Combine(Application.dataPath, "AAAGame/Language/ChineseSimplified.json");
        Dictionary<string, string> language = UtilityBuiltin.Json.ToObject<Dictionary<string, string>>(
            File.ReadAllText(path));

        Assert.AreEqual("近战", language["Building.Unit.Range.Melee"]);
    }

    [Test]
    public void ChineseLocalization_DefinesSkillUpgradeNotices()
    {
        string path = Path.Combine(Application.dataPath, "AAAGame/Language/ChineseSimplified.json");
        Dictionary<string, string> language = UtilityBuiltin.Json.ToObject<Dictionary<string, string>>(
            File.ReadAllText(path));

        Assert.AreEqual("获得技能{Skill}", language["Building.Skill.Obtain.Format"]);
        Assert.AreEqual("{Skill}技能等级+1", language["Building.Skill.Upgrade.Format"]);
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

        AssertStat(stats, BuildingPanelPresentation.HealOnHitGlyph, "+3");
    }

    [Test]
    public void BuildingInfoItem_UsesFourColumnBuildingGridAndIndependentDetailPanel()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/BuildingInfoItem.prefab");
        Assert.IsNotNull(prefab);

        Transform propertyList = FindChild(prefab.transform, "PropertyList");
        GridLayoutGroup grid = propertyList.GetComponent<GridLayoutGroup>();
        RectTransform rect = (RectTransform)propertyList;

        Assert.IsNotNull(grid);
        Assert.AreEqual(GridLayoutGroup.Constraint.FixedColumnCount, grid.constraint);
        Assert.AreEqual(4, grid.constraintCount);
        Assert.GreaterOrEqual(rect.rect.width, grid.cellSize.x * 4 + grid.spacing.x * 3);
        Assert.GreaterOrEqual(rect.rect.height, grid.cellSize.y * 4 + grid.spacing.y * 3);

        Transform unitPanel = FindChild(prefab.transform, "UnitPanel");
        Transform unitProperties = FindChild(unitPanel, "UnitPropertyList");
        GridLayoutGroup unitGrid = unitProperties.GetComponent<GridLayoutGroup>();
        Assert.IsNotNull(unitGrid);
        Assert.AreEqual(2, unitGrid.constraintCount);
        Assert.AreEqual(194f, ((RectTransform)unitPanel).rect.width, 0.01f);
        Assert.AreEqual(2f, unitGrid.spacing.x, 0.01f);
        TMP_Text unitDesc = FindChild(unitPanel, "UnitDesc").GetComponent<TMP_Text>();
        Assert.IsTrue(unitDesc.enableAutoSizing);
        Assert.AreEqual(Vector4.zero, unitDesc.margin);
        Assert.IsFalse(unitPanel.gameObject.activeSelf);

        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            BuildingInfoItem item = instance.GetComponent<BuildingInfoItem>();
            Assert.IsNotNull(item);
            Assert.IsFalse(item.IsDetailPanelVisible);

            item.SetDetailData("Skill Lv2", "Skill description");

            Assert.IsTrue(item.IsDetailPanelVisible);
            Assert.AreEqual("Skill Lv2", FindChild(instance.transform, "UnitName").GetComponent<TextMeshProUGUI>().text);
            Assert.AreEqual("Skill description", FindChild(instance.transform, "UnitDesc").GetComponent<TextMeshProUGUI>().text);
            Assert.AreSame(
                FindChild(instance.transform, "UnitPropertyList").gameObject,
                item.DetailPropertyListRoot);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void BuildingInfoItem_DetailPanelFitsItsOwnContentAndTopAlignsWithOptionCard()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/BuildingInfoItem.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            BuildingInfoItem item = instance.GetComponent<BuildingInfoItem>();
            Assert.IsNotNull(item);
            item.SetData(string.Empty, "Building", "Building description.");
            item.SetDetailData("Unit", "Short detail.");

            float mainHeight = item.ApplyPanelLayout(
                reservePreviewColumn: true,
                reserveProgressRow: true,
                minimumPanelHeight: 300f);
            RectTransform detail = (RectTransform)FindChild(instance.transform, "UnitPanel");
            float shortDetailHeight = detail.rect.height;

            item.SetDetailData(
                "Unit",
                "Detail line one with enough text to wrap across the panel width.\n" +
                "Detail line two with enough text to wrap across the panel width.\n" +
                "Detail line three.");
            Transform detailProperties = FindChild(detail, "UnitPropertyList");
            for (int i = 0; i < 7; i++)
            {
                GameObject property = new($"Property {i}", typeof(RectTransform));
                property.transform.SetParent(detailProperties, false);
            }

            item.ApplyPanelLayout(
                reservePreviewColumn: true,
                reserveProgressRow: true,
                minimumPanelHeight: 300f);

            Assert.Greater(detail.rect.height, shortDetailHeight);
            Assert.Greater(Mathf.Abs(mainHeight - detail.rect.height), 0.01f);
            Assert.AreEqual(
                ((RectTransform)instance.transform).rect.yMax,
                detail.anchoredPosition.y + detail.rect.yMax,
                0.01f,
                "Detail panel top must align with its option card without forcing equal heights.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void BuildingInfoItem_ActionRowsUseKeyholeAndCardEdgeAlignment()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/BuildingInfoItem.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            BuildingInfoItem item = instance.GetComponent<BuildingInfoItem>();
            Assert.IsNotNull(item);
            item.SetData("1", "Building", "Building description.");

            RectTransform preview = (RectTransform)FindChild(instance.transform, "Preview");
            Vector3 previewScale = preview.localScale;
            item.ApplyPanelLayout(
                reservePreviewColumn: true,
                reserveProgressRow: true,
                minimumPanelHeight: 300f);

            RectTransform card = (RectTransform)instance.transform;
            RectTransform key = (RectTransform)FindChild(preview, "Key");
            RectTransform price = (RectTransform)FindChild(card, "Price");
            RectTransform reserves = (RectTransform)FindChild(card, "CoinReserves");
            RectTransform progress = (RectTransform)FindChild(card, "Progress");

            Assert.AreEqual(-82.76f, key.anchoredPosition.x, 0.01f);
            Assert.AreEqual(-46f, key.anchoredPosition.y, 0.01f);
            Assert.AreEqual(previewScale, preview.localScale);
            Assert.AreEqual(12f, card.rect.xMax - (price.anchoredPosition.x + price.rect.xMax), 0.01f);
            Assert.AreEqual(12f, card.rect.xMax - (reserves.anchoredPosition.x + reserves.rect.xMax), 0.01f);
            Assert.AreEqual(6f, progress.anchoredPosition.y + progress.rect.yMin - card.rect.yMin, 0.01f);
            RectTransform title = (RectTransform)FindChild(card, "Name");
            Assert.AreEqual(12f, card.rect.yMax - (title.anchoredPosition.y + title.rect.yMax), 0.01f);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void BuildingPanelScreenClamp_IncludesActiveDetailPanelAndPreservesScale()
    {
        GameObject parentObject = new("Screen", typeof(RectTransform));
        GameObject panelObject = new("Panel", typeof(RectTransform));
        GameObject detailObject = new("Detail", typeof(RectTransform));
        try
        {
            RectTransform parent = parentObject.GetComponent<RectTransform>();
            parent.sizeDelta = new Vector2(1920f, 1080f);

            RectTransform panel = panelObject.GetComponent<RectTransform>();
            panel.SetParent(parent, false);
            panel.sizeDelta = new Vector2(624f, 650f);
            panel.anchoredPosition = new Vector2(900f, 500f);

            RectTransform detail = detailObject.GetComponent<RectTransform>();
            detail.SetParent(panel, false);
            detail.sizeDelta = new Vector2(194f, 220f);
            detail.anchoredPosition = new Vector2(409f, 0f);
            Vector3 originalScale = panel.localScale;

            BuildingPanelScreenClamp.ClampToParent(panel, parent);

            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, panel);
            Assert.GreaterOrEqual(bounds.min.x, parent.rect.xMin + 8f - 0.01f);
            Assert.LessOrEqual(bounds.max.x, parent.rect.xMax - 8f + 0.01f);
            Assert.GreaterOrEqual(bounds.min.y, parent.rect.yMin + 8f - 0.01f);
            Assert.LessOrEqual(bounds.max.y, parent.rect.yMax - 8f + 0.01f);
            Assert.AreEqual(originalScale, panel.localScale);

            panel.anchoredPosition = new Vector2(900f, 500f);
            detail.gameObject.SetActive(false);
            BuildingPanelScreenClamp.ClampToParent(panel, parent, BuildingInfoItem.DetailPanelWidth);
            Vector2 hiddenDetailPosition = panel.anchoredPosition;

            panel.anchoredPosition = new Vector2(900f, 500f);
            detail.gameObject.SetActive(true);
            BuildingPanelScreenClamp.ClampToParent(panel, parent, BuildingInfoItem.DetailPanelWidth);
            Assert.AreEqual(hiddenDetailPosition, panel.anchoredPosition);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(parentObject);
        }
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
    public void BuildingInfoItem_ThirteenStatsStayInsideFourRowGridWithoutOverlap()
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

            BuildingInfoItem infoItem = instance.GetComponent<BuildingInfoItem>();
            Assert.IsNotNull(infoItem);
            infoItem.ApplyPanelLayout(reservePreviewColumn: true, reserveProgressRow: true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(propertyList);
            RectTransform card = (RectTransform)instance.transform;
            AssertRectInside(GetBoundsInParent(propertyList, card), card.rect, "PropertyList");
            AssertRectInside(
                GetBoundsInParent((RectTransform)FindChild(card, "Progress"), card),
                card.rect,
                "Progress");
            AssertRectInside(
                GetBoundsInParent((RectTransform)FindChild(card, "Price"), card),
                card.rect,
                "Price");

            Rect nameBounds = GetBoundsInParent((RectTransform)FindChild(card, "Name"), card);
            Rect descriptionBounds = GetBoundsInParent((RectTransform)FindChild(card, "Desc"), card);
            Rect progressBounds = GetBoundsInParent((RectTransform)FindChild(card, "Progress"), card);
            Rect propertyBounds = GetBoundsInParent(propertyList, card);
            Assert.GreaterOrEqual(card.rect.yMax - nameBounds.yMax, 12f);
            Assert.GreaterOrEqual(card.rect.xMax - descriptionBounds.xMax, 36f);
            Assert.GreaterOrEqual(progressBounds.yMin - card.rect.yMin, 6f);
            Assert.AreEqual(10f, propertyBounds.yMin - progressBounds.yMax, 0.01f);

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

    [Test]
    public void BuildingInfoItem_MultilineDescriptionGrowsCardAtFixedFontSize()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/BuildingInfoItem.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            BuildingInfoItem item = instance.GetComponent<BuildingInfoItem>();
            Assert.IsNotNull(item);

            item.SetData(string.Empty, "Building", "Base description.");
            float shortHeight = item.ApplyPanelLayout(reservePreviewColumn: true, reserveProgressRow: true);

            item.SetData(
                string.Empty,
                "Building",
                "Base description.\nEffect one with enough text to occupy a full line.\nEffect two with enough text to occupy another full line.\nEffect three.");
            float longHeight = item.ApplyPanelLayout(reservePreviewColumn: true, reserveProgressRow: true);
            TextMeshProUGUI description = FindChild(instance.transform, "Desc").GetComponent<TextMeshProUGUI>();

            Assert.Greater(longHeight, shortHeight);
            Assert.IsFalse(description.enableAutoSizing);
            Assert.AreEqual(12.5f, description.fontSize, 0.01f);
            AssertRectInside(
                GetBoundsInParent(description.rectTransform, (RectTransform)instance.transform),
                ((RectTransform)instance.transform).rect,
                "Description");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void BuildingInfoItem_UsesReservedMaximumHeightAcrossDescriptions()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/BuildingInfoItem.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            BuildingInfoItem item = instance.GetComponent<BuildingInfoItem>();
            Assert.IsNotNull(item);

            const string shortDescription = "Base description.";
            const string longDescription = "Base description.\nEffect one with enough text to occupy a full line.\nEffect two with enough text to occupy another full line.\nEffect three.";
            float shortHeight = item.MeasurePanelHeight(
                shortDescription,
                reservePreviewColumn: true,
                reserveProgressRow: true);
            float longHeight = item.MeasurePanelHeight(
                longDescription,
                reservePreviewColumn: true,
                reserveProgressRow: true);
            Assert.Greater(longHeight, shortHeight);

            item.SetData(string.Empty, "Building", shortDescription);
            float appliedHeight = item.ApplyPanelLayout(
                reservePreviewColumn: true,
                reserveProgressRow: true,
                minimumPanelHeight: longHeight);

            Assert.AreEqual(longHeight, appliedHeight, 0.01f);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void BuildingInfoItem_NoBottomRowKeepsStatsAboveSlicedBorder()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/BuildingInfoItem.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            BuildingInfoItem item = instance.GetComponent<BuildingInfoItem>();
            item.SetData(string.Empty, "Building", "Description");
            item.SetProgressVisible(false);
            item.SetCoinReservesVisible(false);
            item.ApplyPanelLayout(
                reservePreviewColumn: true,
                reserveProgressRow: false,
                minimumPanelHeight: 152f,
                compactToContent: true);

            RectTransform card = (RectTransform)instance.transform;
            RectTransform properties = (RectTransform)FindChild(card, "PropertyList");
            Rect propertyBounds = GetBoundsInParent(properties, card);
            Assert.AreEqual(12f, propertyBounds.yMin - card.rect.yMin, 0.01f);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void IconNumItem_WideGridCellKeepsIconAndNumberSeparatedInsideCell()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/IconNumItem.prefab");
        Assert.IsNotNull(prefab);

        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            RectTransform root = (RectTransform)instance.transform;
            root.sizeDelta = new Vector2(94.5f, 20f);
            IconNumItem item = instance.GetComponent<IconNumItem>();
            Assert.IsNotNull(item);
            item.ConfigureNumberLayout(hasIcon: true);

            Rect iconBounds = GetBoundsInParent((RectTransform)FindChild(root, "Icon"), root);
            Rect numberBounds = GetBoundsInParent((RectTransform)FindChild(root, "Num"), root);
            AssertRectInside(iconBounds, root.rect, "Icon");
            AssertRectInside(numberBounds, root.rect, "Num");
            Assert.IsFalse(iconBounds.Overlaps(numberBounds), "Icon and number bounds overlap.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void IconNumItem_RightAlignedContentUsesVisualRightEdge()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/IconNumItem.prefab");
        GameObject canvasObject = new("Canvas", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        GameObject instance = UnityEngine.Object.Instantiate(prefab, canvasObject.transform);
        try
        {
            RectTransform root = (RectTransform)instance.transform;
            root.sizeDelta = new Vector2(96f, 24f);
            IconNumItem item = instance.GetComponent<IconNumItem>();
            TextMeshProUGUI number = FindChild(root, "Num").GetComponent<TextMeshProUGUI>();
            number.text = "50";
            item.ConfigureNumberLayout(hasIcon: true);

            Canvas.ForceUpdateCanvases();
            Rect iconBounds = GetBoundsInParent((RectTransform)FindChild(root, "Icon"), root);
            float gridVisualGap = GetFirstGlyphLeftInParent(number, root) - iconBounds.xMax;
            item.AlignContentRight();

            Canvas.ForceUpdateCanvases();
            iconBounds = GetBoundsInParent((RectTransform)FindChild(root, "Icon"), root);
            Rect numberBounds = GetBoundsInParent(number.rectTransform, root);
            float alignedVisualGap = GetFirstGlyphLeftInParent(number, root) - iconBounds.xMax;
            Assert.AreEqual(root.rect.xMax - 8f, numberBounds.xMax, 0.01f);
            Assert.AreEqual(8f, numberBounds.xMin - iconBounds.xMax, 0.01f);
            Assert.AreEqual(gridVisualGap, alignedVisualGap, 0.01f);
            AssertRectInside(iconBounds, root.rect, "Icon");
            AssertRectInside(numberBounds, root.rect, "Num");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(canvasObject);
        }
    }

    [Test]
    public void IconNumItem_LeadingReserveLabelPreservesStatRowVisualGap()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/IconNumItem.prefab");
        GameObject canvasObject = new("Canvas", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        GameObject instance = UnityEngine.Object.Instantiate(prefab, canvasObject.transform);
        try
        {
            RectTransform root = (RectTransform)instance.transform;
            root.sizeDelta = new Vector2(160f, 24f);
            IconNumItem item = instance.GetComponent<IconNumItem>();
            RectTransform icon = (RectTransform)FindChild(root, "Icon");
            TextMeshProUGUI number = FindChild(root, "Num").GetComponent<TextMeshProUGUI>();

            number.text = "50";
            item.ConfigureNumberLayout(hasIcon: true);
            Canvas.ForceUpdateCanvases();
            float statRowGap = GetFirstGlyphLeftInParent(number, root)
                               - GetBoundsInParent(icon, root).xMax;

            item.SetLeadingLabelData("剩余", "Coin", "50");
            item.AlignTextRight();
            Canvas.ForceUpdateCanvases();

            Assert.That(number.text, Does.StartWith("剩余 "));
            Assert.IsFalse(icon.gameObject.activeSelf);
            Assert.AreEqual(statRowGap, GetInlineSpriteToFollowingGlyphGap(number), 0.01f);
            AssertRectInside(GetBoundsInParent(number.rectTransform, root), root.rect, "Reserve text");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(canvasObject);
        }
    }

    [Test]
    public void IconNumItem_FitParentRectClearsPooledGridPosition()
    {
        GameObject infoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/BuildingInfoItem.prefab");
        GameObject statPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/IconNumItem.prefab");
        GameObject infoInstance = UnityEngine.Object.Instantiate(infoPrefab);
        try
        {
            RectTransform card = (RectTransform)infoInstance.transform;
            RectTransform reserves = (RectTransform)FindChild(card, "CoinReserves");
            GameObject statInstance = UnityEngine.Object.Instantiate(statPrefab, reserves);
            RectTransform statRect = (RectTransform)statInstance.transform;
            statRect.anchorMin = new Vector2(0f, 1f);
            statRect.anchorMax = new Vector2(0f, 1f);
            statRect.anchoredPosition = new Vector2(296f, -10f);
            statRect.sizeDelta = new Vector2(95.33f, 20f);

            IconNumItem item = statInstance.GetComponent<IconNumItem>();
            Assert.IsNotNull(item);
            item.FitParentRect();

            AssertRectInside(GetBoundsInParent(statRect, reserves), reserves.rect, "Coin reserve item");
            AssertRectInside(GetBoundsInParent(reserves, card), card.rect, "Coin reserve root");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(infoInstance);
        }
    }

    [Test]
    public void IconNumItem_FitParentRectKeepsReusedPriceInsideCard()
    {
        GameObject infoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/BuildingInfoItem.prefab");
        GameObject statPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/IconNumItem.prefab");
        GameObject infoInstance = UnityEngine.Object.Instantiate(infoPrefab);
        try
        {
            RectTransform card = (RectTransform)infoInstance.transform;
            RectTransform price = (RectTransform)FindChild(card, "Price");
            GameObject statInstance = UnityEngine.Object.Instantiate(statPrefab, price);
            RectTransform statRect = (RectTransform)statInstance.transform;
            statRect.anchorMin = new Vector2(0f, 1f);
            statRect.anchorMax = new Vector2(0f, 1f);
            statRect.anchoredPosition = new Vector2(296f, -10f);
            statRect.sizeDelta = new Vector2(95.33f, 20f);

            IconNumItem item = statInstance.GetComponent<IconNumItem>();
            Assert.IsNotNull(item);
            item.FitParentRect();

            AssertRectInside(GetBoundsInParent(statRect, price), price.rect, "Price item");
            AssertRectInside(GetBoundsInParent(price, card), card.rect, "Price root");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(infoInstance);
        }
    }

    [Test]
    public void UpgradePanelVisualLayers_AreIndependentAndAlignedToTheirRows()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/BuildingUpgradeTips.prefab");
        Assert.IsNotNull(prefab);

        Transform panel = FindChild(prefab.transform, "UpgradePanel");
        Assert.IsNull(panel.Find("Preview"), "Composite preview image must not be restored.");

        Image background = FindChild(panel, "Bg").GetComponent<Image>();
        Assert.AreEqual(Image.Type.Sliced, background.type);
        Assert.AreEqual(
            "Assets/AAAGame/Sprites/BuildingBuildTips/建造1底.png",
            AssetDatabase.GetAssetPath(background.sprite));

        RectTransform currentFrame = (RectTransform)FindChild(panel, "CurrentPreviewFrame");
        RectTransform upgradeFrame = (RectTransform)FindChild(panel, "UpgradePreviewFrame");
        RectTransform infoRoot = (RectTransform)FindChild(prefab.transform, "InfoRoot");
        RectTransform upgradeRoot = (RectTransform)FindChild(prefab.transform, "UpgradeRoot");
        Assert.AreEqual(-12f, infoRoot.anchoredPosition.y - currentFrame.anchoredPosition.y, 0.01f);
        Assert.AreEqual(-12f, upgradeRoot.anchoredPosition.y - upgradeFrame.anchoredPosition.y, 0.01f);
        Assert.AreEqual("BuildingUpgradePreviewCurrent", currentFrame.GetComponent<Image>().sprite.name);
        Assert.AreEqual("BuildingUpgradePreviewNext", upgradeFrame.GetComponent<Image>().sprite.name);

        RectTransform currentBottom = (RectTransform)FindChild(panel, "CurrentInfoBottomLine");
        RectTransform upgradeTop = (RectTransform)FindChild(panel, "UpgradeInfoTopLine");
        RectTransform upgradeBottom = (RectTransform)FindChild(panel, "UpgradeInfoBottomLine");
        Assert.Greater(currentBottom.anchoredPosition.y, upgradeTop.anchoredPosition.y);
        float currentTop = (2f * currentFrame.anchoredPosition.y) - currentBottom.anchoredPosition.y;
        float currentHeight = currentTop - currentBottom.anchoredPosition.y;
        float upgradeHeight = upgradeTop.anchoredPosition.y - upgradeBottom.anchoredPosition.y;
        Assert.AreEqual(currentHeight, upgradeHeight, 2f, "Current and next-level information bands must be symmetric.");

        RectTransform[] lines = { currentBottom, upgradeTop, upgradeBottom };
        for (int i = 0; i < lines.Length; i++)
        {
            GameObject lineSource = PrefabUtility.GetCorrespondingObjectFromSource(lines[i].gameObject);
            Assert.AreEqual(
                "Assets/AAAGame/Prefabs/UI/Items/BuildingInfoSeparationLineItem.prefab",
                AssetDatabase.GetAssetPath(lineSource));
        }

        Transform badge = FindChild(panel, "LevelTitleBadge");
        Assert.AreEqual(
            "Assets/AAAGame/Sprites/BuildingBuildTips/等级框.png",
            AssetDatabase.GetAssetPath(badge.GetComponent<Image>().sprite));
        Assert.AreEqual("I", badge.Find("CurrentLevel").GetComponent<TextMeshProUGUI>().text);
        Assert.AreEqual("II", badge.Find("NextLevel").GetComponent<TextMeshProUGUI>().text);
        Assert.IsNotNull(badge.Find("Arrow/Shaft").GetComponent<Image>());
        Assert.IsNotNull(badge.Find("Arrow/Head").GetComponent<Image>());
    }

    [Test]
    public void MaxLevelPanel_UsesIndependentRomanLevelBadge()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/BuildingInfoTips.prefab");
        Assert.IsNotNull(prefab);

        Transform panel = FindChild(prefab.transform, "InfoPanel");
        Image background = FindChild(panel, "Bg").GetComponent<Image>();
        Assert.AreEqual(Image.Type.Sliced, background.type);
        Assert.AreEqual(
            "Assets/AAAGame/Sprites/BuildingBuildTips/建造1底.png",
            AssetDatabase.GetAssetPath(background.sprite));
        Transform badge = FindChild(panel, "LevelTitleBadge");
        Assert.AreEqual(
            "Assets/AAAGame/Sprites/BuildingBuildTips/等级框.png",
            AssetDatabase.GetAssetPath(badge.GetComponent<Image>().sprite));
        Assert.AreEqual("III", badge.Find("Title").GetComponent<TextMeshProUGUI>().text);

        RectTransform previewFrame = (RectTransform)FindChild(panel, "PreviewFrame");
        Assert.AreEqual("BuildingUpgradePreviewCurrent", previewFrame.GetComponent<Image>().sprite.name);
        Assert.AreEqual(new Vector2(201.84f, 132f), previewFrame.sizeDelta);
        Assert.AreEqual(Vector3.one, previewFrame.localScale);
        Assert.AreEqual(-12f, ((RectTransform)FindChild(panel, "InfoRoot")).anchoredPosition.y - previewFrame.anchoredPosition.y, 0.01f);
    }

    [Test]
    public void MaxLevelPanel_AdaptsOuterHeightToMeasuredInformation()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/BuildingInfoTips.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            BuildingInfoTips tips = instance.GetComponent<BuildingInfoTips>();
            MethodInfo method = typeof(BuildingInfoTips).GetMethod(
                "ApplyAdaptiveLayout",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            RectTransform panel = (RectTransform)FindChild(instance.transform, "InfoPanel");
            RectTransform background = (RectTransform)FindChild(panel, "Bg");
            RectTransform infoRoot = (RectTransform)FindChild(panel, "InfoRoot");
            RectTransform preview = (RectTransform)FindChild(panel, "PreviewFrame");
            Vector3 previewScale = preview.localScale;

            method.Invoke(tips, new object[] { 152f });

            Assert.AreEqual(234f, panel.rect.height, 0.01f);
            Assert.AreEqual(panel.rect.height, background.rect.height, 0.01f);
            Assert.AreEqual(-31f, infoRoot.anchoredPosition.y, 0.01f);
            Assert.AreEqual(infoRoot.anchoredPosition.y + 12f, preview.anchoredPosition.y, 0.01f);
            Assert.AreEqual(previewScale, preview.localScale);
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

    private static void AssertRectInside(Rect actual, Rect container, string label)
    {
        Assert.GreaterOrEqual(actual.xMin, container.xMin - 0.01f, label + " extends past the left edge.");
        Assert.LessOrEqual(actual.xMax, container.xMax + 0.01f, label + " extends past the right edge.");
        Assert.GreaterOrEqual(actual.yMin, container.yMin - 0.01f, label + " extends past the bottom edge.");
        Assert.LessOrEqual(actual.yMax, container.yMax + 0.01f, label + " extends past the top edge.");
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

    private static float GetFirstGlyphLeftInParent(TextMeshProUGUI text, RectTransform parent)
    {
        text.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
        Assert.Greater(text.textInfo.characterCount, 0);
        Vector3 worldPosition = text.rectTransform.TransformPoint(text.textInfo.characterInfo[0].bottomLeft);
        return parent.InverseTransformPoint(worldPosition).x;
    }

    private static float GetInlineSpriteToFollowingGlyphGap(TextMeshProUGUI text)
    {
        text.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
        int spriteIndex = -1;
        for (int i = 0; i < text.textInfo.characterCount; i++)
        {
            TMP_CharacterInfo character = text.textInfo.characterInfo[i];
            if (character.elementType == TMP_TextElementType.Sprite)
            {
                spriteIndex = i;
                continue;
            }

            if (spriteIndex >= 0 && character.isVisible)
            {
                TMP_CharacterInfo sprite = text.textInfo.characterInfo[spriteIndex];
                return character.bottomLeft.x - sprite.topRight.x;
            }
        }

        Assert.Fail("Reserve text must contain an inline sprite followed by a visible number.");
        return 0f;
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
