using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[TestFixture]
public sealed class SkillReworkDataTests
{
    private const string SkillTablePath = "Assets/AAAGame/DataTable/Hero/SkillTable.txt";
    private const string BuildingTablePath = "Assets/AAAGame/DataTable/Build/BuildingTable.txt";
    private const string ChineseLanguagePath = "Assets/AAAGame/Language/ChineseSimplified.json";

    [Test]
    public void GeneratedSkillRows_ContainRequiredEffectShapesAndSkillTypes()
    {
        AssertSkillShape("Skill_InsatiableThirst", SkillType.Active, 2, requiresDuration: true);
        AssertSkillShape("Skill_ForgedInFire", SkillType.Passive, 2, requiresArea: true);
        AssertSkillShape("Skill_UrgentRequest", SkillType.Active, 3, requiresCastDistance: true);
        AssertSkillShape("Skill_ExpressDelivery", SkillType.Active, 1, requiresCastDistance: true, requiresArea: true, requiresDuration: true);
        AssertSkillShape("Skill_RiotArmor", SkillType.Passive, 1);
        AssertSkillShape("Skill_GiantSlayer", SkillType.Passive, 1);
        AssertSkillShape("Skill_NeatAndTidy", SkillType.Passive, 1, requiresArea: true);
        AssertSkillShape("Skill_Sweep", SkillType.Active, 1, requiresArea: true);
    }

    [Test]
    public void BuildingSkillTechs_MapEachSkillToOneIndustry()
    {
        Dictionary<string, Archetype> map = SkillDataModel.BuildSkillIndustryMap(LoadRows(BuildingTablePath, ParseBuilding));
        var expected = new Dictionary<string, Archetype>(StringComparer.Ordinal)
        {
            ["Skill_UrgentRequest"] = Archetype.Coding,
            ["Skill_JustPassingBy"] = Archetype.Sightseeing,
            ["Skill_ExpressDelivery"] = Archetype.Delivery,
            ["Skill_InsatiableThirst"] = Archetype.Butchery,
            ["Skill_ForgedInFire"] = Archetype.Firefighting,
            ["Skill_RiotArmor"] = Archetype.Security,
            ["Skill_GiantSlayer"] = Archetype.Hunting,
            ["Skill_NeatAndTidy"] = Archetype.Gardening,
            ["Skill_Injection"] = Archetype.Medical,
            ["Skill_CheerSquad"] = Archetype.Sports,
        };

        Assert.AreEqual(expected.Count, map.Count);
        foreach (KeyValuePair<string, Archetype> pair in expected)
            Assert.AreEqual(pair.Value, map[pair.Key], pair.Key);
        Assert.IsFalse(map.ContainsKey("Skill_Sweep"), "Keepsake skills must not require a building industry mapping.");

        var skillIds = new HashSet<string>(LoadRows(SkillTablePath, ParseSkill).Select(row => row.Identifier));
        CollectionAssert.IsSubsetOf(map.Keys, skillIds, "Every building skill mapping must reference an existing skill.");
    }

    [Test]
    public void KeepsakeSkill_SortsBeforeBuildingSkillsWithoutIndustryLookup()
    {
        Dictionary<string, Archetype> map = SkillDataModel.BuildSkillIndustryMap(LoadRows(BuildingTablePath, ParseBuilding));
        SkillData sweep = CreateSkillData(FindSkill("Skill_Sweep"));
        SkillData buildingSkill = CreateSkillData(FindSkill("Skill_InsatiableThirst"));

        Assert.Less(SkillRuntimeDataModel.CompareCanonicalOrder(sweep, buildingSkill, map), 0);
        Assert.Greater(SkillRuntimeDataModel.CompareCanonicalOrder(buildingSkill, sweep, map), 0);
    }

    [Test]
    public void InstantSkillAction_CommitsAfterWindUpAndFinishesAfterWindDown()
    {
        InstantSkillAction action = ScriptableObject.CreateInstance<InstantSkillAction>();
        try
        {
            Fix64 windUp = (Fix64)2;
            Fix64 windDown = (Fix64)3;
            action.StartAction(
                null,
                new SkillInfo(),
                info =>
                {
                    var instantInfo = (InstantSkillActionInfo)info;
                    instantInfo.windUp = windUp;
                    instantInfo.windDown = windDown;
                },
                out ActionInfo rawInfo);
            var info = (InstantSkillActionInfo)rawInfo;

            Assert.IsFalse(info.effectCommitted);
            Assert.IsFalse(info.isFinished);

            action.Tick(info, windUp - Fix64.One);
            Assert.IsFalse(info.effectCommitted);
            Assert.IsFalse(info.isFinished);

            action.Tick(info, Fix64.One);
            Assert.IsTrue(info.effectCommitted);
            Assert.IsFalse(info.isFinished);

            action.Tick(info, windDown);
            Assert.IsTrue(info.isFinished);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(action);
        }
    }

    [Test]
    public void SkillFactories_ContainSweepAndExistingSkillAssets()
    {
        PlayerSkillFactory player = AssetDatabase.LoadAssetAtPath<PlayerSkillFactory>(
            "Assets/AAAGame/SOs/SkillCompFactory/PlayerSkillFactory.asset");
        CharacterSkillFactory character = AssetDatabase.LoadAssetAtPath<CharacterSkillFactory>(
            "Assets/AAAGame/SOs/SkillCompFactory/CharacterSkillFactory.asset");
        Assert.IsNotNull(player);
        Assert.IsNotNull(character);
        AssertFactory(player.skills, player.passiveSkills);
        AssertFactory(character.skills, character.passiveSkills);
        Assert.IsInstanceOf<InsatiableThirstActiveSkillSO>(FindAsset(player.skills, "Skill_InsatiableThirst"));
        Assert.IsInstanceOf<UrgentRequestActiveSkillSO>(FindAsset(player.skills, "Skill_UrgentRequest"));
        Assert.IsInstanceOf<LightLoadDashActiveSkillSO>(FindAsset(player.skills, "Skill_ExpressDelivery"));
        AssertInstantAction(FindAsset(player.skills, "Skill_InsatiableThirst"));
        AssertInstantAction(FindAsset(player.skills, "Skill_Injection"));
        AssertInstantAction(FindAsset(player.skills, "Skill_Sweep"));
        Assert.IsInstanceOf<FireDrillPassiveSkillSO>(FindAsset(player.passiveSkills, "Skill_ForgedInFire"));
        Assert.IsInstanceOf<ArmorPassiveSkillSO>(FindAsset(player.passiveSkills, "Skill_RiotArmor"));
    }

    [Test]
    public void Sweep_DamagesNearbyEnemiesIncludingBuildings()
    {
        EntityRegistry.Clear();
        LogicTestInGameDataModelAuthority.Ensure(GamePhase.Defend, nameof(Sweep_DamagesNearbyEnemiesIncludingBuildings));
        try
        {
            SkillData sweep = CreateSkillData(FindSkill("Skill_Sweep"));
            Fix64 damage = sweep.GetUniqueValue(0, 1);
            Fix64 radius = sweep.GetAreaRange(1);
            Fix64 initialHealth = damage * (Fix64)2;
            var caster = CreateCombatTarget(FixVector2.Zero, SideType.PlayerSide, initialHealth);
            var enemyUnit = CreateCombatTarget(
                new FixVector2(radius / (Fix64)2, Fix64.Zero),
                SideType.EnemySide,
                initialHealth);
            var enemyBuilding = new SimBuildingEntityContext(EntitySideHelper.EnemyFactionId)
            {
                PositionFixed = new FixVector2(radius * (Fix64)3 / (Fix64)4, Fix64.Zero),
                Side = SideType.EnemySide,
            };
            enemyBuilding.Health.Init(initialHealth);
            var ally = CreateCombatTarget(
                new FixVector2(radius / (Fix64)2, Fix64.Zero),
                SideType.PlayerSide,
                initialHealth);
            var distantEnemy = CreateCombatTarget(
                new FixVector2(radius + Fix64.One, Fix64.Zero),
                SideType.EnemySide,
                initialHealth);
            EntityRegistry.Register(enemyBuilding);

            SweepActiveSkillSO.ApplyArea(caster, damage, radius);

            Assert.AreEqual(initialHealth - damage, enemyUnit.HealthValue);
            Assert.AreEqual(initialHealth - damage, enemyBuilding.HealthValue);
            Assert.AreEqual(initialHealth, ally.HealthValue);
            Assert.AreEqual(initialHealth, distantEnemy.HealthValue);
        }
        finally
        {
            EntityRegistry.Clear();
        }
    }

    [Test]
    public void GiantSlayer_OnlyAddsCurrentHealthDamageAgainstEnemyUnits()
    {
        LogicTestInGameDataModelAuthority.Ensure(GamePhase.Defend, nameof(GiantSlayer_OnlyAddsCurrentHealthDamageAgainstEnemyUnits));
        SkillData giantSlayer = CreateSkillData(FindSkill("Skill_GiantSlayer"));
        Fix64 currentHealthPercent = giantSlayer.GetUniqueValue(0, 1);
        Fix64 targetHealth = (Fix64)100;
        Fix64 baseDamage = (Fix64)10;
        var caster = new SimEntityContext { Side = SideType.PlayerSide };
        var enemyUnit = new SimEntityContext { Side = SideType.EnemySide };
        enemyUnit.Health.Init(targetHealth);
        var enemyBuilding = new SimBuildingEntityContext(EntitySideHelper.EnemyFactionId) { Side = SideType.EnemySide };
        enemyBuilding.Health.Init(targetHealth);
        var buff = new SkillCurrentHealthDamageBuff(currentHealthPercent);
        buff.Initialize(null, caster);

        Assert.AreEqual(
            baseDamage + targetHealth * currentHealthPercent / (Fix64)100,
            buff.ModifyOutgoingDamage(enemyUnit, baseDamage));
        Assert.AreEqual(baseDamage, buff.ModifyOutgoingDamage(enemyBuilding, baseDamage));
    }

    [Test]
    public void SkillPanelStats_UseFinalValuesAtRequestedLevel()
    {
        AssertSkillStatsMatchCurrentData("Skill_InsatiableThirst", 2);
        AssertSkillStatsMatchCurrentData("Skill_ForgedInFire", 3);
        AssertSkillStatsMatchCurrentData("Skill_ExpressDelivery", 3);
        AssertSkillStatsMatchCurrentData("Skill_Sweep", 2);
    }

    [Test]
    public void ChineseSkillDescriptions_DoNotExposeImplementationRemarks()
    {
        string language = File.ReadAllText(ChineseLanguagePath);
        StringAssert.Contains("\"Skill_Name_Sweep\":\"横扫\"", language);
        StringAssert.Contains("\"Skill_Desc_Sweep\":\"对英雄附近的敌人造成{0}伤害。\"", language);
        StringAssert.Contains("\"Skill_Desc_GiantSlayer\":\"英雄攻击时，额外附加敌方单位当前生命{0}%的伤害。\"", language);
        StringAssert.Contains("\"Skill_Desc_NeatAndTidy\":\"英雄的普通攻击也会对目标附近生命比例更高的敌方单位造成{0}%伤害。\"", language);
        string[] keys =
        {
            "Skill_Desc_InsatiableThirst",
            "Skill_Desc_ForgedInFire",
            "Skill_Desc_UrgentRequest",
            "Skill_Desc_ExpressDelivery",
            "Skill_Desc_RiotArmor",
        };
        for (int i = 0; i < keys.Length; i++)
        {
            Match match = Regex.Match(
                language,
                "\\\"" + Regex.Escape(keys[i]) + "\\\":\\\"(?<value>(?:\\\\.|[^\\\"\\\\])*)\\\"",
                RegexOptions.CultureInvariant);
            string value = match.Success ? match.Groups["value"].Value : null;
            Assert.IsFalse(string.IsNullOrWhiteSpace(value), keys[i]);
            StringAssert.DoesNotContain("备注", value, keys[i]);
        }
    }

    [Test]
    public void PassiveSkillSlot_ReceivesHoverWithoutRequestingAnActiveCast()
    {
        var slot = new GameObject(
            "PassiveSkillSlot",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button),
            typeof(SkillSlotInputProxy));
        try
        {
            Button button = slot.GetComponent<Button>();
            button.interactable = false;
            int hoveredSlot = -1;
            SkillSlotInputProxy proxy = slot.GetComponent<SkillSlotInputProxy>();
            proxy.Initialize(3, () => -1, () => 3, index => hoveredSlot = index, _ => { });
            var pointer = new PointerEventData(null) { button = PointerEventData.InputButton.Left };

            Assert.DoesNotThrow(() => ExecuteEvents.Execute(slot, pointer, ExecuteEvents.pointerEnterHandler));
            Assert.AreEqual(3, hoveredSlot);
            Assert.DoesNotThrow(() => proxy.OnPointerClick(pointer));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(slot);
        }
    }

    [Test]
    public void FireDrillCounter_IsSmallAndAnchoredAtBottomRight()
    {
        var slot = new GameObject("FireDrillSlot", typeof(RectTransform));
        var mainTextObject = new GameObject("MainText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        mainTextObject.transform.SetParent(slot.transform, false);
        try
        {
            SkillData fireDrill = CreateSkillData(FindSkill("Skill_ForgedInFire"));
            var info = new SkillRuntimeInfo(fireDrill, 2, 0, 0, 7);
            MethodInfo setCounter = typeof(InGameUIForm).GetMethod(
                "SetSkillSlotCounter",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(setCounter);
            setCounter.Invoke(null, new object[] { slot, info });

            RectTransform counterRect = (RectTransform)slot.transform.Find("SkillCounter");
            TextMeshProUGUI counter = counterRect.GetComponent<TextMeshProUGUI>();
            Assert.AreEqual($"7/{fireDrill.GetUniqueValue(1, 2)}", counter.text);
            Assert.Less(counter.fontSize, mainTextObject.GetComponent<TextMeshProUGUI>().fontSize);
            Assert.AreEqual(new Vector2(1f, 0f), counterRect.anchorMin);
            Assert.AreEqual(new Vector2(1f, 0f), counterRect.anchorMax);
            Assert.AreEqual(new Vector2(1f, 0f), counterRect.pivot);
            Assert.AreEqual(TextAlignmentOptions.BottomRight, counter.alignment);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(slot);
        }
    }

    [Test]
    public void SkillSlot_MainTextIsCenteredAndDoesNotShowLevel()
    {
        var formObject = new GameObject("InGameUIForm", typeof(RectTransform), typeof(InGameUIForm));
        var slot = new GameObject("SkillSlot", typeof(RectTransform));
        var textObject = new GameObject("MainText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(slot.transform, false);
        try
        {
            ((RectTransform)slot.transform).sizeDelta = new Vector2(140f, 80f);
            SkillData skill = CreateSkillData(FindSkill("Skill_InsatiableThirst"));
            var info = new SkillRuntimeInfo(skill, 2, 0, 0, 0);
            MethodInfo method = typeof(InGameUIForm).GetMethod(
                "SetSkillSlotName",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            method.Invoke(formObject.GetComponent<InGameUIForm>(), new object[] { slot, info, 0 });

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            Assert.AreEqual(new Vector2(6f, 6f), text.rectTransform.offsetMin);
            Assert.AreEqual(new Vector2(-6f, -6f), text.rectTransform.offsetMax);
            Assert.IsFalse(text.enableWordWrapping);
            Assert.AreEqual(10f, text.fontSizeMin, 0.01f);
            StringAssert.DoesNotContain("Lv2", text.text);
            StringAssert.DoesNotContain("-Lv", text.text);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(formObject);
            UnityEngine.Object.DestroyImmediate(slot);
        }
    }

    [Test]
    public void SkillCooldownVisual_UsesRadial360RemainingRatioAndCeilingSeconds()
    {
        var texture = new Texture2D(4, 4);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
        var slot = new GameObject("SkillSlot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var mainTextObject = new GameObject("MainText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        mainTextObject.transform.SetParent(slot.transform, false);
        slot.GetComponent<Image>().sprite = sprite;
        try
        {
            InGameUIForm.SetSkillCooldownVisual(slot, true, (Fix64)5.25f, (Fix64)10f);

            Image fill = slot.transform.Find("SkillCooldownFill").GetComponent<Image>();
            TextMeshProUGUI text = slot.transform.Find("SkillCooldownText").GetComponent<TextMeshProUGUI>();
            Assert.IsTrue(fill.gameObject.activeSelf);
            Assert.IsTrue(text.gameObject.activeSelf);
            Assert.AreEqual(Image.Type.Filled, fill.type);
            Assert.AreEqual(Image.FillMethod.Radial360, fill.fillMethod);
            Assert.AreEqual(0.525f, fill.fillAmount, 0.001f);
            Assert.AreEqual("6", text.text);

            InGameUIForm.SetSkillCooldownVisual(slot, false, Fix64.Zero, (Fix64)10f);
            Assert.IsFalse(fill.gameObject.activeSelf);
            Assert.IsFalse(text.gameObject.activeSelf);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(slot);
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void GeneralCounter_RemainingCooldownTracksResetAndTicks()
    {
        var counter = new GeneralCounter();
        counter.Init((Fix64)10f, true);
        Assert.AreEqual(Fix64.Zero, counter.GetRemainingRequired());

        counter.Reset();
        Assert.AreEqual((Fix64)10f, counter.GetTargetRequired());
        Assert.AreEqual((Fix64)10f, counter.GetRemainingRequired());

        counter.Tick((Fix64)3.25f);
        Assert.AreEqual((Fix64)6.75f, counter.GetRemainingRequired());
    }

    private static void AssertSkillStatsMatchCurrentData(string skillId, int level)
    {
        SkillTable row = FindSkill(skillId);
        SkillData skill = CreateSkillData(row);
        var stats = new List<BuildingPanelStat>();
        BuildingPanelPresentation.CollectSkillStats(skill, level, stats);

        var expected = new List<BuildingPanelStat>();
        Fix64 castDistance = skill.GetCastDistance(level);
        Fix64 areaRange = skill.GetAreaRange(level);
        Fix64 duration = skill.GetDuration(level);
        if (castDistance > Fix64.Zero)
            expected.Add(new BuildingPanelStat(BuildingPanelPresentation.CastDistanceGlyph, castDistance.ToStringRound()));
        if (areaRange > Fix64.Zero)
            expected.Add(new BuildingPanelStat(BuildingPanelPresentation.SplashGlyph, areaRange.ToStringRound()));
        if (duration > Fix64.Zero)
            expected.Add(new BuildingPanelStat(BuildingPanelPresentation.DurationGlyph, duration.ToString()));
        if (skill.Type == SkillType.Active)
        {
            int usage = skill.Lv1UsageCount + skill.UpgradeIncrementUsageCount * (level - 1);
            expected.Add(new BuildingPanelStat(BuildingPanelPresentation.UsageGlyph, usage.ToString()));
            expected.Add(new BuildingPanelStat(BuildingPanelPresentation.CooldownGlyph, skill.GetCooldown(level).ToString()));
        }
        if (skill.Identifier == "Skill_ForgedInFire")
            expected.Add(new BuildingPanelStat(BuildingPanelPresentation.StackGlyph, skill.GetUniqueValue(1, level).ToString()));

        CollectionAssert.AreEqual(
            expected.Select(stat => $"{stat.Glyph}={stat.Value}"),
            stats.Select(stat => $"{stat.Glyph}={stat.Value}"),
            $"Skill panel stats must reflect current data. skill={skillId}, level={level}");
    }

    private static void AssertSkillShape(
        string skillId,
        SkillType type,
        int uniqueValueCount,
        bool requiresCastDistance = false,
        bool requiresArea = false,
        bool requiresDuration = false)
    {
        SkillTable row = FindSkill(skillId);
        Assert.AreEqual(type, row.Type, skillId);
        Assert.AreEqual(uniqueValueCount, row.Lv1UniqueValues.Length, skillId);
        Assert.IsTrue(row.Lv1UniqueValues.All(value => value > Fix64.Zero), skillId);
        Assert.AreEqual(uniqueValueCount, row.UpgradeIncrementUniqueValues.Length, skillId);
        Assert.IsTrue(row.WindUp >= Fix64.Zero, skillId);
        Assert.IsTrue(row.WindDown >= Fix64.Zero, skillId);
        if (requiresCastDistance)
            Assert.IsTrue(row.Lv1CastRange > Fix64.Zero, skillId);
        if (requiresArea)
            Assert.IsTrue(row.Lv1EffectRadius > Fix64.Zero, skillId);
        if (requiresDuration)
            Assert.IsTrue(row.Lv1Duration > Fix64.Zero, skillId);
        if (type == SkillType.Active)
        {
            Assert.Greater(row.Lv1UsageCount, 0, skillId);
            Assert.IsTrue(row.Lv1Cooldown > Fix64.Zero, skillId);
        }
    }

    private static SkillData CreateSkillData(SkillTable row)
    {
        return new SkillData(
            row.Identifier,
            row.Lv1UniqueValues,
            row.Lv1CastRange,
            row.Lv1EffectRadius,
            row.Lv1Duration,
            row.Lv1UsageCount,
            row.UpgradeIncrementUniqueValues,
            row.UpgradeIncrementCastRange,
            row.UpgradeIncrementEffectRadius,
            row.UpgradeIncrementDuration,
            row.UpgradeIncrementUsageCount,
            row.Lv1Cooldown,
            row.UpgradeDecrementCooldown,
            row.WindUp,
            row.WindDown,
            row.Type,
            row.NameKey,
            row.DescKey,
            row.SpritePath);
    }

    private static T FindAsset<T>(IReadOnlyList<T> assets, string skillId) where T : SkillEffectSO
    {
        for (int i = 0; i < assets.Count; i++)
        {
            if (assets[i] != null && assets[i].skillId == skillId)
                return assets[i];
        }
        throw new InvalidOperationException($"Skill factory asset was not found. skill={skillId}.");
    }

    private static void AssertInstantAction(ActiveSkillSO skill)
    {
        Assert.IsInstanceOf<InstantActiveSkillSO>(skill);
        Assert.AreEqual(1, skill.actions.Count, skill.skillId);
        Assert.IsInstanceOf<InstantSkillAction>(skill.actions[0], skill.skillId);
    }

    private static SimEntityContext CreateCombatTarget(FixVector2 position, SideType side, Fix64 health)
    {
        var entity = new SimEntityContext { PositionFixed = position, Side = side };
        entity.Health.Init(health);
        EntityRegistry.Register(entity);
        return entity;
    }

    private static void AssertFactory(
        IReadOnlyList<ActiveSkillSO> active,
        IReadOnlyList<PassiveSkillSO> passive)
    {
        Assert.AreEqual(6, active.Count);
        Assert.AreEqual(5, passive.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < active.Count; i++)
        {
            Assert.IsNotNull(active[i]);
            Assert.IsTrue(ids.Add(active[i].skillId), $"Duplicate active skill {active[i].skillId}.");
            Assert.AreEqual(SkillType.Active, FindSkill(active[i].skillId).Type);
        }
        for (int i = 0; i < passive.Count; i++)
        {
            Assert.IsNotNull(passive[i]);
            Assert.IsTrue(ids.Add(passive[i].skillId), $"Duplicate passive skill {passive[i].skillId}.");
            Assert.AreEqual(SkillType.Passive, FindSkill(passive[i].skillId).Type);
        }
    }

    private static SkillTable FindSkill(string identifier)
    {
        SkillTable[] rows = LoadRows(SkillTablePath, ParseSkill);
        SkillTable row = rows.FirstOrDefault(item => item.Identifier == identifier);
        return row ?? throw new InvalidOperationException($"Skill table row was not found. skill={identifier}.");
    }

    private static T[] LoadRows<T>(string path, Func<string, T> parser)
    {
        var rows = new List<T>();
        foreach (string line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                continue;
            rows.Add(parser(line));
        }
        return rows.ToArray();
    }

    private static SkillTable ParseSkill(string line)
    {
        var row = new SkillTable();
        Assert.IsTrue(row.ParseDataRow(line, null));
        return row;
    }

    private static BuildingTable ParseBuilding(string line)
    {
        var row = new BuildingTable();
        Assert.IsTrue(row.ParseDataRow(line, null));
        return row;
    }
}
