using NUnit.Framework;

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
            new TutorialObjective("first", "Tutorial.Goal.BuildArmy", TutorialObjectiveStatus.Active),
            new TutorialObjective("second", "Tutorial.Goal.BuildDefense", TutorialObjectiveStatus.Active));

        TutorialObjectiveService.SetStatus("first", TutorialObjectiveStatus.Completed);
        TutorialObjectiveService.FailActiveObjectives();

        Assert.AreEqual(TutorialObjectiveStatus.Completed, TutorialObjectiveService.GetStatus("first"));
        Assert.AreEqual(TutorialObjectiveStatus.Failed, TutorialObjectiveService.GetStatus("second"));
        Assert.Throws<System.InvalidOperationException>(() =>
            TutorialObjectiveService.SetStatus("missing", TutorialObjectiveStatus.Completed));
    }
}
