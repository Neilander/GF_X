using NUnit.Framework;

using UnityEditor;
using UnityEngine;

[TestFixture]
public sealed class TutorialSystemTests
{
    [TearDown]
    public void TearDown()
    {
        TutorialObjectiveService.Reset();
    }

    [Test]
    public void TipPresentation_UsesTableDurationUnlessExplicitlyOverridden()
    {
        var timed = new TipPresentation("title", "content", "Narrator", 2f);
        var conditional = new TipPresentation("title", "content", "Director", -1f);

        Assert.AreEqual(2f, timed.ResolveDuration(null));
        Assert.AreEqual(4.5f, timed.ResolveDuration(4.5f));
        Assert.AreEqual(-1f, conditional.ResolveDuration(null));
        Assert.IsNull(SideTipsManager.ResolveConditionalTipId("timed", 2f, null));
        Assert.AreEqual("conditional", SideTipsManager.ResolveConditionalTipId("conditional", -1f, null));
        Assert.AreEqual("explicit", SideTipsManager.ResolveConditionalTipId("timed", 2f, "explicit"));
    }

    [Test]
    public void PhaseSwitchHold_ReachesThresholdAtOnePointFiveSecondsAndReboundsQuickly()
    {
        float progress = 0f;
        progress = PhaseSwitchHoldTrigger.AdvanceProgress(progress, true, 1.49f);
        Assert.Less(progress, 1f);
        progress = PhaseSwitchHoldTrigger.AdvanceProgress(progress, true, 0.01f);
        Assert.AreEqual(1f, progress, 0.0001f);

        progress = PhaseSwitchHoldTrigger.AdvanceProgress(progress, false, 0.17f);
        Assert.Greater(progress, 0f);
        progress = PhaseSwitchHoldTrigger.AdvanceProgress(progress, false, 0.01f);
        Assert.AreEqual(0f, progress, 0.0001f);
    }

    [Test]
    public void TutorialRules_ExposeOnlyAuthoredPhaseActions()
    {
        Assert.IsTrue(TutorialManager.IsPhaseSwitchGuidedStage(TutorialStage.AwaitFirstBuildPhase));
        Assert.IsTrue(TutorialManager.IsPhaseSwitchGuidedStage(TutorialStage.AwaitInvadePhase));
        Assert.IsFalse(TutorialManager.IsPhaseSwitchGuidedStage(TutorialStage.FirstDefense));
        Assert.IsFalse(TutorialManager.IsPhaseSwitchGuidedStage(TutorialStage.UpgradeCore));

        Assert.IsTrue(TutorialManager.IsConstructTypeAllowed(TutorialStage.BuildMilitaryAndDefense, BuilType.Army));
        Assert.IsTrue(TutorialManager.IsConstructTypeAllowed(TutorialStage.BuildMilitaryAndDefense, BuilType.Def));
        Assert.IsFalse(TutorialManager.IsConstructTypeAllowed(TutorialStage.BuildMilitaryAndDefense, BuilType.Prod));
        Assert.IsTrue(TutorialManager.IsConstructTypeAllowed(TutorialStage.BuildProductionAndResearch, BuilType.Prod));
        Assert.IsTrue(TutorialManager.IsConstructTypeAllowed(TutorialStage.BuildProductionAndResearch, BuilType.Tech));
        Assert.IsFalse(TutorialManager.IsConstructTypeAllowed(TutorialStage.UpgradeCore, BuilType.Army));

        Assert.IsTrue(TutorialManager.IsDefendPreviewAllowedStage(TutorialStage.AwaitDefensePhase));
        Assert.IsFalse(TutorialManager.IsDefendPreviewAllowedStage(TutorialStage.BuildProductionAndResearch));
    }

    [Test]
    public void TutorialObjectives_TrackCompletedAndFailedRowsIndependently()
    {
        TutorialObjectiveService.Replace(
            new TutorialObjective("first", "BuildMilitaryBuilding", TutorialObjectiveStatus.Active, 1),
            new TutorialObjective("second", "BuildDefenseBuilding", TutorialObjectiveStatus.Active, 1));

        TutorialObjectiveService.SetStatus("first", TutorialObjectiveStatus.Completed);
        TutorialObjectiveService.FailActiveObjectives();

        Assert.AreEqual(TutorialObjectiveStatus.Completed, TutorialObjectiveService.GetStatus("first"));
        Assert.AreEqual(TutorialObjectiveStatus.Failed, TutorialObjectiveService.GetStatus("second"));
        Assert.Throws<System.InvalidOperationException>(() =>
            TutorialObjectiveService.SetStatus("missing", TutorialObjectiveStatus.Completed));
    }

    [Test]
    public void DestinationRadius_ConvertsGameDistanceAndUsesCircularBoundary()
    {
        DistanceUnitConverter.SetEditorTestDistanceConversionRateText("0.015");
        try
        {
            Fix64 worldRadius = ObjectiveDestinationService.ConvertConfiguredRadius((Fix64)250);
            Assert.AreEqual(((Fix64)3.75f).RawValue, worldRadius.RawValue);

            var center = new FixVector2((Fix64)10, (Fix64)20);
            Assert.IsTrue(ObjectiveDestinationService.Contains(
                new FixVector2((Fix64)13.75f, (Fix64)20),
                center,
                worldRadius));
            Assert.IsFalse(ObjectiveDestinationService.Contains(
                new FixVector2((Fix64)13.76f, (Fix64)20),
                center,
                worldRadius));
        }
        finally
        {
            DistanceUnitConverter.ClearEditorTestDistanceConversionRate();
        }
    }

    [Test]
    public void Level1Prefab_ContainsAuthoredDestination()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/Entity/Level/Level_1.prefab");
        Assert.IsNotNull(prefab);

        EntityPresetPoint[] points = prefab.GetComponentsInChildren<EntityPresetPoint>(true);
        EntityPresetPoint destination = null;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i].PointType != EntityPresetPointType.Destination)
                continue;
            Assert.IsNull(destination, "Level_1 must not contain duplicate tutorial destinations.");
            destination = points[i];
        }

        Assert.IsNotNull(destination);
        Assert.AreEqual(0, destination.DestinationId);
        Assert.AreEqual(250f, destination.DestinationRadius);
        Assert.AreEqual(41.3f, destination.transform.localPosition.x, 0.001f);
        Assert.AreEqual(38.5f, destination.transform.localPosition.z, 0.001f);
    }

    [Test]
    public void CommonArchetype_IsAppendedAndAssignedToGeneralUnitsAndBuildings()
    {
        Assert.AreEqual(1, (int)Archetype.Coding);
        Assert.AreEqual(10, (int)Archetype.Sports);
        Assert.AreEqual(11, (int)Archetype.Common);

        string buildingTable = System.IO.File.ReadAllText(
            "Assets/AAAGame/DataTable/Build/BuildingTable.txt");
        StringAssert.Contains("Buil_VendingMachine\t自助售货机\t\t\tBuilType.Prod\tArchetype.Common", buildingTable);
        StringAssert.Contains("Buil_BodyguardAgency\t保镖公司\t\t\tBuilType.Army\tArchetype.Common", buildingTable);
        StringAssert.Contains("Buil_SouthernMoon\t南方皓月\t\t\tBuilType.Def\tArchetype.Common", buildingTable);

        string characterTable = System.IO.File.ReadAllText(
            "Assets/AAAGame/DataTable/CharacterDataDetail.txt");
        StringAssert.Contains(
            "Unit_HiredBodyguard\t雇佣保镖\t\t\tSoldier/雇佣保镖\tUnit_Name_HiredBodyguard\tUnit_Desc_HiredBodyguard\tArchetype.Common",
            characterTable);
    }

    [Test]
    public void Level1Prefab_HasOneCodingCoreMarkedAsGameEndCondition()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/Entity/Level/Level_1.prefab");
        Assert.IsNotNull(prefab);

        EntityPresetPoint[] points = prefab.GetComponentsInChildren<EntityPresetPoint>(true);
        int codingCoreCount = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i].Identifier != "Buil_ResearchCenter_Lv1")
                continue;
            codingCoreCount++;
            Assert.IsTrue(points[i].IsGameEndConditionBuilding);
        }

        Assert.AreEqual(1, codingCoreCount);
    }

    [Test]
    public void GoalConditionItem_TutorialObjectivesUseHollowCheckboxAndStatusStyling()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/Items/GoalConditionItem.prefab");
        Assert.IsNotNull(prefab);

        GameObject instance = Object.Instantiate(prefab);
        try
        {
            GoalConditionItem item = instance.GetComponent<GoalConditionItem>();
            Assert.IsNotNull(item);

            item.SetTutorialObjective("Objective", TutorialObjectiveStatus.Active);
            Transform icon = instance.transform.Find("Icon");
            Assert.IsNotNull(icon);
            Assert.IsFalse(icon.GetComponent<UnityEngine.UI.Image>().enabled);

            Transform checkbox = icon.Find("TutorialCheckbox");
            Assert.IsNotNull(checkbox);
            Assert.AreEqual(4, checkbox.GetComponentsInChildren<UnityEngine.UI.Image>(true).Length);

            Component checkmark = checkbox.Find("Checkmark").GetComponent("TextMeshProUGUI");
            Assert.IsNotNull(checkmark);
            Assert.AreEqual(string.Empty, GetText(checkmark));

            item.SetTutorialObjective("Objective", TutorialObjectiveStatus.Completed);
            Assert.AreEqual("\u2713", GetText(checkmark));

            item.SetTutorialObjective("Objective", TutorialObjectiveStatus.Failed);
            Assert.AreEqual(string.Empty, GetText(checkmark));
            StringAssert.Contains("<s>Objective</s>", GetText(instance.GetComponent("TextMeshProUGUI")));
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static string GetText(Component textComponent)
    {
        Assert.IsNotNull(textComponent);
        return (string)textComponent.GetType().GetProperty("text").GetValue(textComponent);
    }
}
