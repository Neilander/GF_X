using System;
using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public class LogicObstacleCommandServiceTests
{
    [SetUp]
    public void SetUp()
    {
        LogicTimeControlService.BeginTimeline();
        LogicObstacleCommandService.BeginTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicObstacleCommandService.IsActive)
            LogicObstacleCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
    }

    [Test]
    public void ApplyFrame_SortsByStableObstacleIdThenSequence()
    {
        LogicObstacleCommandService.ScheduleBoxForNextFrame(20, new FixVector2(2, 2), new FixVector2(1, 1));
        LogicObstacleCommandService.ScheduleCircleForNextFrame(10, new FixVector2(1, 1), (Fix64)2);
        LogicObstacleCommandService.ScheduleRemoveForNextFrame(10);
        var applied = new List<LogicObstacleCommand>();

        LogicTimeControlService.BeginFrame(1);
        LogicObstacleCommandService.ApplyFrameForTests(1, applied.Add);

        Assert.AreEqual(3, applied.Count);
        Assert.AreEqual(10, applied[0].StableObstacleId);
        Assert.AreEqual(LogicObstacleCommandKind.AddOrUpdateCircle, applied[0].Kind);
        Assert.AreEqual(10, applied[1].StableObstacleId);
        Assert.AreEqual(LogicObstacleCommandKind.Remove, applied[1].Kind);
        Assert.AreEqual(20, applied[2].StableObstacleId);
        Assert.AreEqual(1, LogicObstacleCommandService.ActiveObstacleCount);
        Assert.AreEqual(0, LogicObstacleCommandService.PendingCount);
    }

    [Test]
    public void ApplyFrame_RejectsMissedCommandFrame()
    {
        LogicObstacleCommandService.ScheduleBoxForNextFrame(10, FixVector2.Zero, new FixVector2(1, 1));
        LogicTimeControlService.BeginFrame(1);
        LogicTimeControlService.BeginFrame(2);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => LogicObstacleCommandService.ApplyFrameForTests(2, _ => { }));

        StringAssert.Contains("missed its frame", exception.Message);
    }

    [Test]
    public void CurrentFrameSchedule_RejectsCallsOutsideLifecycleWindow()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => LogicObstacleCommandService.ScheduleRemoveForCurrentLifecycleFrame(10));

        StringAssert.Contains("only allowed while lifecycle commands are being applied", exception.Message);
    }

    [Test]
    public void TimelineReset_RepositionsPendingCommandsToFrameOne()
    {
        LogicTimeControlService.BeginFrame(1);
        LogicObstacleCommandService.ApplyFrameForTests(1, _ => { });
        LogicObstacleCommandService.ScheduleCircleForNextFrame(10, FixVector2.Zero, Fix64.One);

        LogicTimeControlService.ResetFrameTimelinePreservingPauses();
        LogicObstacleCommandService.ResetFrameTimelinePreservingCommands();

        Assert.AreEqual(1UL, LogicObstacleCommandService.History[0].EffectiveFrame);
        LogicTimeControlService.BeginFrame(1);
        LogicObstacleCommandService.ApplyFrameForTests(1, _ => { });
        Assert.AreEqual(1, LogicObstacleCommandService.ActiveObstacleCount);
    }

    [Test]
    public void SnapshotRestore_RestoresActivePendingAndTimelineState()
    {
        var applied = new List<LogicObstacleCommand>();
        LogicObstacleCommandService.ScheduleCircleForNextFrame(10, FixVector2.Zero, Fix64.One);
        LogicTimeControlService.BeginFrame(1);
        LogicObstacleCommandService.ApplyFrameForTests(1, applied.Add);
        LogicObstacleCommandService.ScheduleBoxForNextFrame(20, new FixVector2(3, 4), new FixVector2(1, 2));
        LogicObstacleCommandSnapshot snapshot = LogicObstacleCommandService.CaptureSnapshot();

        LogicTimeControlService.BeginFrame(2);
        LogicObstacleCommandService.ApplyFrameForTests(2, applied.Add);
        Assert.AreEqual(2, LogicObstacleCommandService.ActiveObstacleCount);

        applied.Clear();
        LogicObstacleCommandService.RestoreSnapshotForTests(snapshot, applied.Add);

        Assert.AreEqual(1UL, LogicObstacleCommandService.LastAppliedFrame);
        Assert.AreEqual(1, LogicObstacleCommandService.ActiveObstacleCount);
        Assert.AreEqual(1, LogicObstacleCommandService.PendingCount);
        Assert.AreEqual(2, LogicObstacleCommandService.History.Count);
        Assert.AreEqual(3, applied.Count, "Restore must remove both current obstacles and then reapply the snapshot obstacle.");
    }
}
