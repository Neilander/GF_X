using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class LogicCardCommandServiceTests
{
    [SetUp]
    public void SetUp()
    {
        LogicTimeControlService.BeginTimeline();
        LogicCardCommandService.BeginTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicCardCommandService.IsActive)
            LogicCardCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
    }

    [Test]
    public void Commands_ApplyOnExactFrameInSubmissionOrder()
    {
        LogicCardCommand play = LogicCardCommandService.SchedulePlayForNextFrame(
            11,
            new FixVector2(Fix64.FromRaw(123), Fix64.FromRaw(-456)));
        LogicCardCommand discard = LogicCardCommandService.ScheduleDiscardForNextFrame(22);
        var applied = new List<LogicCardCommand>();

        Assert.AreEqual(1UL, play.EffectiveFrame);
        Assert.AreEqual(1UL, discard.EffectiveFrame);
        Assert.AreEqual(1UL, play.Sequence);
        Assert.AreEqual(2UL, discard.Sequence);
        Assert.Throws<InvalidOperationException>(() =>
            LogicCardCommandService.ScheduleDiscardForNextFrame(play.CardRuntimeId));

        LogicTimeControlService.BeginFrame(1);
        LogicCardCommandService.ApplyFrameForTests(1, applied.Add);

        CollectionAssert.AreEqual(
            new[] { play.Sequence, discard.Sequence },
            new[] { applied[0].Sequence, applied[1].Sequence });
        Assert.AreEqual(123L, applied[0].SelectedPosition.x.RawValue);
        Assert.AreEqual(-456L, applied[0].SelectedPosition.y.RawValue);
        Assert.AreEqual(0, LogicCardCommandService.PendingCount);
        Assert.AreEqual(2, LogicCardCommandService.AppliedCount);
    }

    [Test]
    public void ApplyFrame_RejectsMissedCommandFrame()
    {
        LogicCardCommandService.ScheduleDiscardForNextFrame(1);
        LogicTimeControlService.BeginFrame(1);
        LogicTimeControlService.BeginFrame(2);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => LogicCardCommandService.ApplyFrameForTests(2, _ => { }));

        StringAssert.Contains("missed its frame", exception.Message);
    }

    [Test]
    public void PendingPositionRawValues_EnterDeterministicState()
    {
        LogicCardCommandService.SchedulePlayForNextFrame(
            7,
            new FixVector2(Fix64.FromRaw(1000), Fix64.FromRaw(2000)));
        var first = new LogicStateHasher();
        LogicCardCommandService.WriteDeterministicState(first);

        LogicCardCommandService.EndTimeline();
        LogicCardCommandService.BeginTimeline();
        LogicCardCommandService.SchedulePlayForNextFrame(
            7,
            new FixVector2(Fix64.FromRaw(1000), Fix64.FromRaw(2001)));
        var second = new LogicStateHasher();
        LogicCardCommandService.WriteDeterministicState(second);

        Assert.AreNotEqual(first.Hash, second.Hash);
    }

    [Test]
    public void CardResolution_IsRejectedOutsideApplyWindowAndPublishedInsideIt()
    {
        var resolutions = new List<LogicCardResolution>();
        LogicCardCommandService.CardResolved += resolutions.Add;
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                LogicCardCommandService.PublishResolvedCard(LogicCardCommandKind.Play, 1, null));

            LogicCardCommandService.ScheduleDiscardForNextFrame(99);
            LogicTimeControlService.BeginFrame(1);
            LogicCardCommandService.ApplyFrameForTests(1, _ =>
                LogicCardCommandService.PublishResolvedCard(
                    LogicCardCommandKind.Discard,
                    1,
                    "building-source"));

            Assert.AreEqual(1, resolutions.Count);
            Assert.AreEqual(1UL, resolutions[0].FrameId);
            Assert.AreEqual(LogicCardCommandKind.Discard, resolutions[0].Kind);
            Assert.AreEqual("building-source", resolutions[0].SourceBuildingInstanceId);
        }
        finally
        {
            LogicCardCommandService.CardResolved -= resolutions.Add;
        }
    }

    [Test]
    public void CardRewardMutation_RejectsOutsideApplyWindow()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RewardManager.HandleCardDiscardReward(null, 1));
    }

    [Test]
    public void CardGameplayState_HasNoRenderFrameOrUiMutationBypass()
    {
        string legacyManagerPath = Path.Combine(
            Application.dataPath,
            "AAAGame/Scripts/Card/Manager/CardGameManager.cs");
        Assert.IsFalse(
            File.Exists(legacyManagerPath),
            "The legacy MonoBehaviour card manager must not drive a second controller from render Update.");
        string legacyPopulationPath = Path.Combine(
            Application.dataPath,
            "AAAGame/Scripts/Card/Manager/PopulationManager.cs");
        Assert.IsFalse(
            File.Exists(legacyPopulationPath),
            "The unused legacy population MonoBehaviour must not retain a second card gameplay state.");

        string cardUiPath = Path.Combine(
            Application.dataPath,
            "AAAGame/Scripts/Card/UI/CardUIForm.cs");
        string cardUiSource = File.ReadAllText(cardUiPath);
        StringAssert.DoesNotContain("void RedrawCards(", cardUiSource);
        StringAssert.DoesNotContain("m_CardSystemController.DrawCards(", cardUiSource);

        string launchScenePath = Path.Combine(Application.dataPath, "AAAGame/Scene/Launch.unity");
        string launchSceneSource = File.ReadAllText(launchScenePath);
        StringAssert.DoesNotContain(
            "4328fc49a04274501868e332426a0021",
            launchSceneSource,
            "The persistent Launch scene must not expose CardTester gameplay mutation context menus.");

        string cardSetupPath = Path.Combine(Application.dataPath, "AAAGame/Scripts/UTManagers/CardSetup.cs");
        string cardSetupSource = File.ReadAllText(cardSetupPath);
        StringAssert.DoesNotContain("public bool AddCardToDeck(CardData", cardSetupSource);
        StringAssert.DoesNotContain("public bool DrawCard()", cardSetupSource);

        string cardControllerPath = Path.Combine(
            Application.dataPath,
            "AAAGame/Scripts/Card/Controller/CardSystemController.cs");
        string cardControllerSource = File.ReadAllText(cardControllerPath);
        StringAssert.DoesNotContain("public bool AddCardToDeck(CardData", cardControllerSource);
        StringAssert.DoesNotContain("public void DrawCards(", cardControllerSource);
        StringAssert.DoesNotContain("public bool DrawCard()", cardControllerSource);
        StringAssert.DoesNotContain("GF.Event.FireNow", cardControllerSource);
        StringAssert.DoesNotContain("AudioManager", cardControllerSource);
        StringAssert.DoesNotContain("NotifyPlacementApplied", cardControllerSource);
        StringAssert.DoesNotContain("discardScreenPosition", cardControllerSource);
        StringAssert.DoesNotContain("BuildingEntity SourceBuilding", cardControllerSource);
        StringAssert.DoesNotContain("TryGetBoundView", cardControllerSource);
        StringAssert.Contains("EnsurePresentationInitialized", cardControllerSource);
        StringAssert.Contains("LogicFrameRuntime.IsTicking", cardControllerSource);
        StringAssert.Contains("m_PendingPresentationShutdown.Enqueue", cardSetupSource);
        StringAssert.Contains("GF.Event.Fire(this, CardPlayedEventArgs.Create", cardControllerSource);
        StringAssert.Contains("GF.Event.Fire(this, CardDiscardedEventArgs.Create", cardControllerSource);
    }
}
