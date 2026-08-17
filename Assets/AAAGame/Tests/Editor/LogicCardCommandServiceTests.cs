using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using AAAGame.Card;

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
    public void SubmitWhileInGameUiPaused_AppliesImmediatelyWithoutAdvancingOrRepeatingFrame()
    {
        var applied = new List<LogicCardCommand>();
        LogicTimeControlService.BeginFrame(1);
        LogicTimeControlService.AcquirePause(LogicTimeControlSources.InGameUiPause);

        LogicCardCommand command = LogicCardCommandService.SubmitForTests(
            LogicCardCommandKind.Play,
            31,
            new FixVector2((Fix64)4, (Fix64)5),
            applied.Add);

        Assert.AreEqual(1UL, LogicTimeControlService.CurrentFrame);
        Assert.AreEqual(1UL, command.EffectiveFrame);
        Assert.AreEqual(1, applied.Count);
        Assert.AreEqual(0, LogicCardCommandService.PendingCount);
        Assert.AreEqual(1, LogicCardCommandService.AppliedCount);

        LogicTimeControlService.ReleasePause(LogicTimeControlSources.InGameUiPause);
        LogicTimeControlService.BeginFrame(2);
        LogicCardCommandService.ApplyFrameForTests(2, applied.Add);
        Assert.AreEqual(1, applied.Count);
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

    [TestCase(1, 2)]
    [TestCase(2, 5)]
    [TestCase(3, 9)]
    public void DiscardReward_UsesCardLevelInsteadOfOccupiedSupply(int cardLevel, int expectedReward)
    {
        Assert.AreEqual(
            expectedReward,
            CardSystemController.ResolveDiscardResourceReward(new[] { 2, 5, 9 }, cardLevel));
    }

    [TestCase(0)]
    [TestCase(4)]
    public void DiscardReward_RejectsUnsupportedCardLevel(int cardLevel)
    {
        Assert.Throws<InvalidOperationException>(() =>
            CardSystemController.ResolveDiscardResourceReward(new[] { 2, 5, 9 }, cardLevel));
    }

    [Test]
    public void CardWithoutEnoughSupply_CanBeginDragForDiscard()
    {
        GameObject canvasObject = new GameObject("CardDragTestCanvas", typeof(RectTransform), typeof(Canvas));
        GameObject cardObject = new GameObject("CardDragTestItem", typeof(RectTransform), typeof(CanvasGroup));
        cardObject.SetActive(false);
        cardObject.transform.SetParent(canvasObject.transform, false);
        try
        {
            HandCardItem cardItem = cardObject.AddComponent<HandCardItem>();

            FieldInfo canPlayField = typeof(HandCardItem).GetField(
                "m_CanPlay",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo isDraggingField = typeof(HandCardItem).GetField(
                "m_IsDragging",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo rectTransformField = typeof(HandCardItem).GetField(
                "m_RectTransform",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo canvasGroupField = typeof(HandCardItem).GetField(
                "canvasGroup",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo canvasField = typeof(HandCardItem).GetField(
                "m_Canvas",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(canPlayField);
            Assert.IsNotNull(isDraggingField);
            Assert.IsNotNull(rectTransformField);
            Assert.IsNotNull(canvasGroupField);
            Assert.IsNotNull(canvasField);

            canPlayField.SetValue(cardItem, false);
            rectTransformField.SetValue(cardItem, cardObject.GetComponent<RectTransform>());
            canvasGroupField.SetValue(cardItem, cardObject.GetComponent<CanvasGroup>());
            canvasField.SetValue(cardItem, canvasObject.GetComponent<Canvas>());
            cardItem.OnBeginDrag(new PointerEventData(null));

            Assert.IsTrue(
                (bool)isDraggingField.GetValue(cardItem),
                "A card that cannot be played still needs to enter drag state so the player can discard it.");
            Assert.IsFalse(cardObject.GetComponent<CanvasGroup>().blocksRaycasts);
        }
        finally
        {
            if (cardObject != null)
                UnityEngine.Object.DestroyImmediate(cardObject);
            UnityEngine.Object.DestroyImmediate(canvasObject);
        }
    }

    [TestCase(false, false, false, false)]
    [TestCase(false, true, false, true)]
    [TestCase(false, false, true, true)]
    [TestCase(true, false, false, true)]
    public void FullPopulationDrag_ContinuesOnlyInsideHandOrTrash(
        bool canPlay,
        bool isOverHand,
        bool isOverTrash,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            CardUIForm.IsDragPositionAllowed(canPlay, isOverHand, isOverTrash));
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
        StringAssert.Contains("LogicFrameRuntime.IsExecutingFrame", cardControllerSource);
        StringAssert.Contains("m_PendingPresentationShutdown.Enqueue", cardSetupSource);
        StringAssert.Contains("GF.Event.Fire(this, CardPlayedEventArgs.Create", cardControllerSource);
        StringAssert.Contains("GF.Event.Fire(this, CardDiscardedEventArgs.Create", cardControllerSource);

        int discardBranchStart = cardUiSource.IndexOf(
            "if (isInTrash)",
            StringComparison.Ordinal);
        int handBranchStart = cardUiSource.IndexOf(
            "bool isOverHand",
            discardBranchStart,
            StringComparison.Ordinal);
        Assert.GreaterOrEqual(discardBranchStart, 0);
        Assert.Greater(handBranchStart, discardBranchStart);
        string discardBranch = cardUiSource.Substring(
            discardBranchStart,
            handBranchStart - discardBranchStart);
        int scheduleDiscard = discardBranch.IndexOf(
            "m_CardSystemController.DiscardCard(discardedCardModel)",
            StringComparison.Ordinal);
        Assert.GreaterOrEqual(
            scheduleDiscard,
            0,
            "Trash release is the final discard confirmation and must schedule its logic command immediately.");
        StringAssert.DoesNotContain(
            "OnDiscardSuccess(",
            discardBranch,
            "Render-time discard animation callbacks must not choose the effective logic frame.");
        StringAssert.Contains(
            "cardItem.OnDiscardSuccess(() => RemoveHandCardItemDirect(args.CardModel))",
            cardUiSource,
            "Discard animation must start from the presentation event emitted after the logic command is applied.");
    }
}
