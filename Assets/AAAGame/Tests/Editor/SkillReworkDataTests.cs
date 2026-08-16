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
    public void GeneratedSkillRows_ContainCurrentEffectsAndSkillTypes()
    {
        SkillTable bladeDance = FindSkill("Skill_InsatiableThirst");
        Assert.AreEqual(SkillType.Active, bladeDance.Type);
        CollectionAssert.AreEqual(new[] { (Fix64)50, (Fix64)25 }, bladeDance.Lv1UniqueValues);
        CollectionAssert.AreEqual(new[] { (Fix64)20, (Fix64)10 }, bladeDance.UpgradeIncrementUniqueValues);
        Assert.AreEqual((Fix64)6, bladeDance.Lv1Duration);
        Assert.AreEqual(2, bladeDance.Lv1UsageCount);
        Assert.AreEqual((Fix64)8, bladeDance.Lv1Cooldown);

        SkillTable fireDrill = FindSkill("Skill_ForgedInFire");
        Assert.AreEqual(SkillType.Passive, fireDrill.Type);
        CollectionAssert.AreEqual(new[] { (Fix64)1, (Fix64)50 }, fireDrill.Lv1UniqueValues);
        CollectionAssert.AreEqual(new[] { Fix64.Parse("0.5"), (Fix64)20 }, fireDrill.UpgradeIncrementUniqueValues);
        Assert.AreEqual((Fix64)500, fireDrill.Lv1EffectRadius);
        Assert.AreEqual((Fix64)100, fireDrill.UpgradeIncrementEffectRadius);

        SkillTable urgentRequest = FindSkill("Skill_UrgentRequest");
        Assert.AreEqual(SkillType.Active, urgentRequest.Type);
        CollectionAssert.AreEqual(new[] { (Fix64)8, (Fix64)3, (Fix64)15 }, urgentRequest.Lv1UniqueValues);
        CollectionAssert.AreEqual(new[] { (Fix64)2, (Fix64)2, (Fix64)10 }, urgentRequest.UpgradeIncrementUniqueValues);
        Assert.AreEqual((Fix64)900, urgentRequest.Lv1CastRange);
        Assert.AreEqual(2, urgentRequest.Lv1UsageCount);

        SkillTable lightLoad = FindSkill("Skill_ExpressDelivery");
        Assert.AreEqual(SkillType.Active, lightLoad.Type);
        Assert.AreEqual((Fix64)40, lightLoad.Lv1UniqueValues[0]);
        Assert.AreEqual((Fix64)600, lightLoad.Lv1CastRange);
        Assert.AreEqual((Fix64)250, lightLoad.Lv1EffectRadius);
        Assert.AreEqual((Fix64)2, lightLoad.Lv1Duration);
        Assert.AreEqual(2, lightLoad.Lv1UsageCount);

        SkillTable disarm = FindSkill("Skill_RiotArmor");
        Assert.AreEqual(SkillType.Passive, disarm.Type);
        Assert.AreEqual((Fix64)5, disarm.Lv1UniqueValues[0]);
        Assert.AreEqual((Fix64)4, disarm.UpgradeIncrementUniqueValues[0]);
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
    }

    [Test]
    public void SkillFactories_ContainFiveActiveAndFivePassiveAssets()
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
        Assert.IsInstanceOf<FireDrillPassiveSkillSO>(FindAsset(player.passiveSkills, "Skill_ForgedInFire"));
        Assert.IsInstanceOf<ArmorPassiveSkillSO>(FindAsset(player.passiveSkills, "Skill_RiotArmor"));
    }

    [Test]
    public void SkillPanelStats_UseFinalValuesAtRequestedLevel()
    {
        AssertSkillStats("Skill_InsatiableThirst", 2,
            (BuildingPanelPresentation.DurationGlyph, ((Fix64)7).ToString()),
            (BuildingPanelPresentation.UsageGlyph, "3"),
            (BuildingPanelPresentation.CooldownGlyph, ((Fix64)8).ToString()));
        AssertSkillStats("Skill_ForgedInFire", 3,
            (BuildingPanelPresentation.SplashGlyph, ((Fix64)700).ToString()),
            (BuildingPanelPresentation.StackGlyph, ((Fix64)90).ToString()));
        AssertSkillStats("Skill_ExpressDelivery", 3,
            (BuildingPanelPresentation.CastDistanceGlyph, ((Fix64)700).ToString()),
            (BuildingPanelPresentation.SplashGlyph, ((Fix64)270).ToString()),
            (BuildingPanelPresentation.DurationGlyph, ((Fix64)3).ToString()),
            (BuildingPanelPresentation.UsageGlyph, "4"),
            (BuildingPanelPresentation.CooldownGlyph, ((Fix64)9).ToString()));
    }

    [Test]
    public void ChineseSkillDescriptions_DoNotExposeImplementationRemarks()
    {
        string language = File.ReadAllText(ChineseLanguagePath);
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
            Assert.AreEqual("7/70", counter.text);
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

    private static void AssertSkillStats(string skillId, int level, params (string Glyph, string Value)[] expected)
    {
        SkillTable row = FindSkill(skillId);
        SkillData skill = CreateSkillData(row);
        var stats = new List<BuildingPanelStat>();
        BuildingPanelPresentation.CollectSkillStats(skill, level, stats);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.IsTrue(stats.Any(stat => stat.Glyph == expected[i].Glyph && stat.Value == expected[i].Value),
                $"Missing skill stat {expected[i].Glyph}={expected[i].Value} for {skillId} Lv{level}.");
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

    private static void AssertFactory(
        IReadOnlyList<ActiveSkillSO> active,
        IReadOnlyList<PassiveSkillSO> passive)
    {
        Assert.AreEqual(5, active.Count);
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
