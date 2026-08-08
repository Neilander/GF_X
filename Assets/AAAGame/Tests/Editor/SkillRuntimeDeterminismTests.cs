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

    [Test]
    public void SkillAimAndUiStayOnRenderFramesAndOnlySubmitFinalCastCommand()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string inputModel = File.ReadAllText(Path.Combine(
            projectRoot,
            "Assets/AAAGame/Scripts/DataModel/InputModel.cs"));
        string positionAction = File.ReadAllText(Path.Combine(
            projectRoot,
            "Assets/AAAGame/Scripts/ActionSystem/PositionSelectAction.cs"));
        string presentation = File.ReadAllText(Path.Combine(
            projectRoot,
            "Assets/AAAGame/Scripts/UI/SkillCastPresentationService.cs"));
        string inGameUi = File.ReadAllText(Path.Combine(
            projectRoot,
            "Assets/AAAGame/Scripts/UI/InGameUIForm.cs"));
        string activeSkill = File.ReadAllText(Path.Combine(
            projectRoot,
            "Assets/AAAGame/Scripts/SkillSystem/ActiveSkillSO.cs"));
        string directAttack = File.ReadAllText(Path.Combine(
            projectRoot,
            "Assets/AAAGame/Scripts/GeneralCreature/DirectAtkComp.cs"));
        string entityView = File.ReadAllText(Path.Combine(
            projectRoot,
            "Assets/AAAGame/Scripts/Entity/MAEntity.cs"));

        StringAssert.DoesNotContain("Skill1Pressed", inputModel);
        StringAssert.DoesNotContain("SkillConfirm", inputModel);
        StringAssert.DoesNotContain("SelectScreenPosition", inputModel);
        StringAssert.DoesNotContain("Physics.Raycast", positionAction);
        StringAssert.DoesNotContain("CylinderTargetSelector", positionAction);
        StringAssert.DoesNotContain("ICastRangePresenter", positionAction);
        StringAssert.Contains("LogicSkillCastCommandService.Submit", presentation);
        StringAssert.DoesNotContain("LogicInputFrame", presentation);
        StringAssert.DoesNotContain("CurrentLogicFrame", presentation);
        StringAssert.DoesNotContain("CancelRunningSkills", inGameUi);
        StringAssert.Contains("TickSkillPresentation", inGameUi);
        StringAssert.DoesNotContain("TryGetBoundView", activeSkill);
        StringAssert.DoesNotContain("animator", activeSkill);
        StringAssert.DoesNotContain("AttackPresentationStarted", directAttack);
        StringAssert.DoesNotContain("AudioManager", directAttack);
        StringAssert.Contains("SyncActionPresentation", entityView);
    }

    [Test]
    public void SkillAuthorityDoesNotFallbackToSerializedFloatOrPresentationScale()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string activeSkill = File.ReadAllText(Path.Combine(
            projectRoot,
            "Assets/AAAGame/Scripts/SkillSystem/ActiveSkillSO.cs"));
        string urgentRequest = File.ReadAllText(Path.Combine(
            projectRoot,
            "Assets/AAAGame/Scripts/SkillSystem/UrgentRequestActiveSkillSO.cs"));
        string basicAction = File.ReadAllText(Path.Combine(
            projectRoot,
            "Assets/AAAGame/Scripts/ActionSystem/BasicAction.cs"));

        StringAssert.DoesNotContain("return (Fix64)coolDownInterval", activeSkill);
        StringAssert.DoesNotContain("(Fix64)radius", activeSkill);
        StringAssert.DoesNotContain("(Fix64)posSelectInfo.selectScale.x", activeSkill);
        StringAssert.Contains("posSelectInfo.selectionRadius = GetAreaRangeWorldFixed();", activeSkill);
        StringAssert.Contains("CalculateAutoSpawnRadiusFixed(count)", urgentRequest);
        StringAssert.DoesNotContain("(Fix64)radius", urgentRequest);
        StringAssert.DoesNotContain("protected float duration", basicAction);
        StringAssert.DoesNotContain("Dictionary<string, float>", basicAction);
        StringAssert.DoesNotContain("Dictionary<string, UnityEngine.Object>", basicAction);
    }

    [Test]
    public void SkillRuntimeSnapshot_DoesNotShareMutableSkillOrActionAssets()
    {
        ActiveSkillSO sourceSkill = ScriptableObject.CreateInstance<ActiveSkillSO>();
        PositionSelectAction sourceAction = ScriptableObject.CreateInstance<PositionSelectAction>();
        ActiveSkillSO snapshot = null;
        try
        {
            sourceSkill.skillId = "Skill_A";
            sourceSkill.banWhenOtherSkill = true;
            sourceSkill.actions = new List<BasicAction> { sourceAction };
            sourceAction.relatedTriggerString = "Cast_A";
            typeof(PositionSelectAction).GetField(
                    "posSelectPrefabName",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(sourceAction, "Selector_A");

            MethodInfo createSnapshot = typeof(ActiveSkillSO).GetMethod(
                "CreateRuntimeSnapshot",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(createSnapshot);
            snapshot = (ActiveSkillSO)createSnapshot.Invoke(sourceSkill, null);

            sourceSkill.skillId = "Skill_B";
            sourceSkill.banWhenOtherSkill = false;
            sourceSkill.actions.Clear();
            sourceAction.relatedTriggerString = "Cast_B";
            typeof(PositionSelectAction).GetField(
                    "posSelectPrefabName",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(sourceAction, "Selector_B");

            Assert.AreEqual("Skill_A", snapshot.skillId);
            Assert.IsTrue(snapshot.banWhenOtherSkill);
            Assert.AreEqual(1, snapshot.actions.Count);
            Assert.AreNotSame(sourceAction, snapshot.actions[0]);
            Assert.AreEqual("Cast_A", snapshot.actions[0].relatedTriggerString);
            Assert.AreEqual(
                "Selector_A",
                ((PositionSelectAction)snapshot.actions[0]).SelectorPrefabName);
        }
        finally
        {
            if (snapshot != null)
            {
                if (snapshot.actions != null)
                {
                    for (int i = 0; i < snapshot.actions.Count; i++)
                    {
                        if (snapshot.actions[i] != null)
                            UnityEngine.Object.DestroyImmediate(snapshot.actions[i]);
                    }
                }
                UnityEngine.Object.DestroyImmediate(snapshot);
            }
            UnityEngine.Object.DestroyImmediate(sourceAction);
            UnityEngine.Object.DestroyImmediate(sourceSkill);
        }
    }

    [Test]
    public void SkillRuntimeSnapshot_CannotBeCreatedInsideLogicFrame()
    {
        ActiveSkillSO sourceSkill = ScriptableObject.CreateInstance<ActiveSkillSO>();
        try
        {
            LogicFrameRuntime.Begin();
            LogicFrameRuntime.StartTimeline();
            LogicFrameRuntime.BeginFrameExecution(1);
            MethodInfo createSnapshot = typeof(ActiveSkillSO).GetMethod(
                "CreateRuntimeSnapshot",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(createSnapshot);
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                () => createSnapshot.Invoke(sourceSkill, null));
            Assert.IsInstanceOf<InvalidOperationException>(exception.InnerException);
        }
        finally
        {
            if (LogicFrameRuntime.IsExecutingFrame)
                LogicFrameRuntime.EndFrameExecution(1);
            if (LogicFrameRuntime.IsActive)
                LogicFrameRuntime.End();
            UnityEngine.Object.DestroyImmediate(sourceSkill);
        }
    }

    [Test]
    public void ClusterSpawnFixedRadiusPreservesAuthoredQuantization()
    {
        Assert.AreEqual(3277L, ClusterSpawnSystem.CalculateAutoSpawnRadiusFixed(1).RawValue);

        Fix64 first = ClusterSpawnSystem.CalculateAutoSpawnRadiusFixed(8);
        Fix64 repeated = ClusterSpawnSystem.CalculateAutoSpawnRadiusFixed(8);
        Assert.AreEqual(first.RawValue, repeated.RawValue);
        Assert.Greater(first.RawValue, 3277L);
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
