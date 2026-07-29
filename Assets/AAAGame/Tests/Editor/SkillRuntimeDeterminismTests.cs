using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameFramework;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class SkillRuntimeDeterminismTests
{
    private SkillRuntimeDataModel m_Model;
    private Dictionary<string, int> m_Levels;
    private Dictionary<string, int> m_Remaining;
    private List<string> m_Order;

    [SetUp]
    public void SetUp()
    {
        m_Model = GetOrCreateSkillModel();
        m_Levels = GetField<Dictionary<string, int>>("m_SkillLevels");
        m_Remaining = GetField<Dictionary<string, int>>("m_SkillRemainingUsageCounts");
        m_Order = GetField<List<string>>("m_UnlockOrder");
        ClearState();
    }

    [TearDown]
    public void TearDown()
    {
        ClearState();
    }

    [Test]
    public void DeterministicStateTracksRemainingUsageAndSlotOrder()
    {
        m_Order.Add("skill-a");
        m_Order.Add("skill-b");
        m_Levels.Add("skill-a", 1);
        m_Levels.Add("skill-b", 2);
        m_Remaining.Add("skill-a", 3);
        m_Remaining.Add("skill-b", 4);
        ulong baseline = ComputeHash();

        m_Remaining["skill-a"] = 2;
        ulong usageChanged = ComputeHash();
        Assert.AreNotEqual(baseline, usageChanged);

        m_Remaining["skill-a"] = 3;
        m_Order.Reverse();
        ulong orderChanged = ComputeHash();
        Assert.AreNotEqual(baseline, orderChanged);
    }

    [Test]
    public void PhaseAuthorityDoesNotSubscribeGameplayStateToGfPhaseEvents()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string skillSource = File.ReadAllText(Path.Combine(
            projectRoot,
            "Assets/AAAGame/Scripts/DataModel/SkillRuntimeDataModel.cs"));
        string buffSource = File.ReadAllText(Path.Combine(
            projectRoot,
            "Assets/AAAGame/Scripts/Buff/BuildingCombatBuffs.cs"));

        StringAssert.Contains("LogicPhaseCommandService.PhaseApplied += OnLogicPhaseApplied", skillSource);
        StringAssert.Contains("LogicPhaseCommandService.PhaseApplied += OnLogicPhaseApplied", buffSource);
        StringAssert.DoesNotContain("GF.Event.Subscribe(IngamePhaseChangedEventArgs", skillSource);
        StringAssert.DoesNotContain("GF.Event.Subscribe(IngamePhaseChangedEventArgs", buffSource);
    }

    [Test]
    public void TechAppliedNotificationRunsInsideExactLogicFrame()
    {
        LogicTimeControlService.BeginTimeline();
        LogicTechEffectCommandService.BeginTimeline();
        LogicTechEffectCommand observed = default;
        int notificationCount = 0;
        LogicTechEffectCommandService.EffectApplied += OnEffectApplied;
        try
        {
            LogicTechEffectCommand expected = LogicTechEffectCommandService.ScheduleForNextFrame(
                "tech-cache",
                false,
                EntitySideHelper.PlayerFactionId,
                "building-cache");
            LogicTimeControlService.BeginFrame(1);

            LogicTechEffectCommandService.ApplyFrameForTests(1, _ => { });

            Assert.AreEqual(1, notificationCount);
            Assert.AreEqual(expected.Sequence, observed.Sequence);
            Assert.AreEqual(1ul, LogicTimeControlService.CurrentFrame);
        }
        finally
        {
            LogicTechEffectCommandService.EffectApplied -= OnEffectApplied;
            LogicTechEffectCommandService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }

        void OnEffectApplied(LogicTechEffectCommand command)
        {
            notificationCount++;
            observed = command;
        }
    }

    [Test]
    public void BuildManagerCacheDoesNotDependOnGfTechEventPump()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string source = File.ReadAllText(Path.Combine(
            projectRoot,
            "Assets/AAAGame/Scripts/Build/BuildManager.cs"));

        StringAssert.Contains("LogicTechEffectCommandService.EffectApplied += OnLogicTechEffectApplied", source);
        StringAssert.DoesNotContain("GF.Event.Subscribe(TechUnlockedEventArgs", source);
    }

    [Test]
    public void TutorialFlowUsesLogicFramesLogicEntitiesAndPureLogicEvents()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string source = File.ReadAllText(Path.Combine(
            projectRoot,
            "Assets/AAAGame/Scripts/MeiyouUtility/TutorialManager.cs"));

        StringAssert.Contains("ILogicFrameUpdate", source);
        StringAssert.Contains("LogicBuildingOwnershipEventService.OwnerFactionChanged +=", source);
        StringAssert.Contains("LogicPhaseCommandService.PhaseApplied +=", source);
        StringAssert.Contains("LogicGameEndService.GameEnded +=", source);
        StringAssert.Contains("EntityRegistry.AllEntities", source);
        StringAssert.Contains("WriteDeterministicState", source);
        StringAssert.DoesNotContain("private void Update()", source);
        StringAssert.DoesNotContain("GF.Event.Subscribe(EntityFactionChangedEventArgs", source);
        StringAssert.DoesNotContain("inGameData.Buildings", source);
    }

    private ulong ComputeHash()
    {
        var hasher = new LogicStateHasher();
        SkillRuntimeDataModel.WriteDeterministicState(hasher);
        return hasher.Hash;
    }

    private T GetField<T>(string fieldName)
    {
        return (T)typeof(SkillRuntimeDataModel)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(m_Model);
    }

    private void ClearState()
    {
        m_Levels?.Clear();
        m_Remaining?.Clear();
        m_Order?.Clear();
    }

    private static SkillRuntimeDataModel GetOrCreateSkillModel()
    {
        FieldInfo dataModelField = typeof(GF).GetField(
            "<DataModel>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic);
        var component = dataModelField?.GetValue(null) as DataModelComponent;
        if (component == null)
        {
            var gameObject = new GameObject("SkillRuntimeDeterminismTests_DataModel");
            component = gameObject.AddComponent<DataModelComponent>();
            dataModelField?.SetValue(null, component);
        }

        FieldInfo dataModelsField = typeof(DataModelComponent).GetField(
            "m_DataModels",
            BindingFlags.Instance | BindingFlags.NonPublic);
        object dataModels = dataModelsField?.GetValue(component);
        if (dataModelsField != null && (dataModels == null || dataModels.GetType() != dataModelsField.FieldType))
        {
            dataModels = Activator.CreateInstance(dataModelsField.FieldType);
            dataModelsField.SetValue(component, dataModels);
        }

        SkillRuntimeDataModel model = component.GetDataModel<SkillRuntimeDataModel>();
        if (model != null)
            return model;

        model = (SkillRuntimeDataModel)Activator.CreateInstance(typeof(SkillRuntimeDataModel), true);
        Type pairType = typeof(DataModelComponent).Assembly.GetType("TypeIdPair");
        object pair = Activator.CreateInstance(
            pairType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { typeof(SkillRuntimeDataModel), 0 },
            null);
        object dictionary = dataModelsField.GetValue(component);
        dictionary.GetType().GetMethod("Add")?.Invoke(dictionary, new[] { pair, model });
        return model;
    }
}
