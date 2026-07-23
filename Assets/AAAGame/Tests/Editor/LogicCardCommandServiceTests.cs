using System;
using System.Collections.Generic;
using NUnit.Framework;

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
}
