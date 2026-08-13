using System;
using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public sealed class LogicSkillSlotCommandServiceTests
{
    [SetUp]
    public void SetUp()
    {
        LogicTimeControlService.BeginTimeline();
        LogicSkillSlotCommandService.BeginTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicSkillSlotCommandService.IsActive)
            LogicSkillSlotCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
    }

    [Test]
    public void Requests_DoNotApplyImmediatelyAndApplyNextFrameInSequenceOrder()
    {
        var applied = new List<LogicSkillSlotCommand>();

        LogicSkillSlotCommand first = LogicSkillSlotCommandService.ScheduleForNextFrame(0, 1);
        LogicSkillSlotCommand second = LogicSkillSlotCommandService.ScheduleForNextFrame(1, 2);

        Assert.IsEmpty(applied);
        Assert.AreEqual(1UL, first.EffectiveFrame);
        Assert.AreEqual(1UL, second.EffectiveFrame);
        Assert.AreEqual(1UL, first.Sequence);
        Assert.AreEqual(2UL, second.Sequence);
        LogicTimeControlService.BeginFrame(1);
        LogicSkillSlotCommandService.ApplyFrameForTests(1, applied.Add);

        CollectionAssert.AreEqual(
            new[] { first.Sequence, second.Sequence },
            new[] { applied[0].Sequence, applied[1].Sequence });
        Assert.AreEqual(0, LogicSkillSlotCommandService.PendingCount);
        Assert.AreEqual(2, LogicSkillSlotCommandService.AppliedCount);
        Assert.AreEqual(1UL, LogicSkillSlotCommandService.LastAppliedFrame);
    }

    [Test]
    public void PausedTimeline_KeepsCommandPendingUntilFirstResumedTick()
    {
        const int pauseSource = 991;
        var applied = new List<LogicSkillSlotCommand>();
        LogicTimeControlService.AcquirePause(pauseSource);

        LogicSkillSlotCommand command = LogicSkillSlotCommandService.ScheduleForNextFrame(0, 1);

        Assert.AreEqual(0UL, LogicTimeControlService.CurrentFrame);
        Assert.AreEqual(1UL, command.EffectiveFrame);
        Assert.AreEqual(1, LogicSkillSlotCommandService.PendingCount);
        Assert.IsEmpty(applied);

        LogicTimeControlService.ReleasePause(pauseSource);
        LogicTimeControlService.BeginFrame(1);
        LogicSkillSlotCommandService.ApplyFrameForTests(1, applied.Add);

        Assert.AreEqual(1, applied.Count);
        Assert.AreEqual(command.Sequence, applied[0].Sequence);
    }

    [Test]
    public void ApplyFrame_RejectsMissedCommandFrame()
    {
        LogicSkillSlotCommandService.ScheduleForNextFrame(0, 1);
        LogicTimeControlService.BeginFrame(1);
        LogicTimeControlService.BeginFrame(2);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => LogicSkillSlotCommandService.ApplyFrameForTests(2, _ => { }));

        StringAssert.Contains("missed its frame", exception.Message);
    }

    [Test]
    public void DeterministicState_ChangesForPendingAndAppliedCommands()
    {
        ulong initial = ComputeStateHash();
        LogicSkillSlotCommandService.ScheduleForNextFrame(0, 1);
        ulong pending = ComputeStateHash();
        LogicTimeControlService.BeginFrame(1);
        LogicSkillSlotCommandService.ApplyFrameForTests(1, _ => { });
        ulong applied = ComputeStateHash();

        Assert.AreNotEqual(initial, pending);
        Assert.AreNotEqual(pending, applied);
        Assert.AreNotEqual(initial, applied);
    }

    [Test]
    public void Schedule_RejectsInvalidOrIdenticalIndices()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LogicSkillSlotCommandService.ScheduleForNextFrame(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LogicSkillSlotCommandService.ScheduleForNextFrame(0, SkillInputRuntime.MaxSkillCount));
        Assert.Throws<ArgumentException>(
            () => LogicSkillSlotCommandService.ScheduleForNextFrame(1, 1));
    }

    private static ulong ComputeStateHash()
    {
        var hasher = new LogicStateHasher();
        LogicSkillSlotCommandService.WriteDeterministicState(hasher);
        return hasher.Hash;
    }
}
