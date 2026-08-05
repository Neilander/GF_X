using System;
using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public sealed class LogicSkillCastCommandServiceTests
{
    [SetUp]
    public void SetUp()
    {
        LogicTimeControlService.BeginTimeline();
        LogicSkillCastCommandService.BeginTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicSkillCastCommandService.IsActive)
            LogicSkillCastCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
    }

    [Test]
    public void FinalCastRequestsApplyOnlyOnNextFrameInSequenceOrder()
    {
        var applied = new List<LogicSkillCastCommand>();
        LogicSkillCastCommand first = LogicSkillCastCommandService.ScheduleForNextFrame(
            new LogicEntityId(11),
            0,
            new FixVector2(Fix64.FromRaw(101), Fix64.FromRaw(202)));

        Assert.IsEmpty(applied);
        Assert.AreEqual(1UL, first.EffectiveFrame);
        Assert.AreEqual(1UL, first.Sequence);
        LogicTimeControlService.BeginFrame(1);
        LogicSkillCastCommandService.ApplyFrameForTests(1, applied.Add);

        Assert.AreEqual(1, applied.Count);
        Assert.AreEqual(first.Sequence, applied[0].Sequence);
        Assert.AreEqual(first.RequestedWorldPosition, applied[0].RequestedWorldPosition);
        Assert.AreEqual(0, LogicSkillCastCommandService.PendingCount);
    }

    [Test]
    public void PendingAndAppliedStateIncludeFinalWorldPosition()
    {
        ulong initial = ComputeHash();
        LogicSkillCastCommandService.ScheduleForNextFrame(
            new LogicEntityId(11),
            2,
            new FixVector2(Fix64.FromRaw(101), Fix64.FromRaw(202)));
        ulong firstPosition = ComputeHash();
        LogicSkillCastCommandService.EndTimeline();
        LogicSkillCastCommandService.BeginTimeline();
        LogicSkillCastCommandService.ScheduleForNextFrame(
            new LogicEntityId(11),
            2,
            new FixVector2(Fix64.FromRaw(101), Fix64.FromRaw(203)));
        ulong secondPosition = ComputeHash();

        Assert.AreNotEqual(initial, firstPosition);
        Assert.AreNotEqual(firstPosition, secondPosition);
    }

    [Test]
    public void DuplicatePendingCastForCasterIsRejected()
    {
        LogicSkillCastCommandService.ScheduleForNextFrame(
            new LogicEntityId(11),
            0,
            FixVector2.Zero);

        Assert.Throws<InvalidOperationException>(() =>
            LogicSkillCastCommandService.ScheduleForNextFrame(
                new LogicEntityId(11),
                1,
                FixVector2.Zero));
    }

    [Test]
    public void InvalidCasterOrSlotIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            LogicSkillCastCommandService.ScheduleForNextFrame(default, 0, FixVector2.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogicSkillCastCommandService.ScheduleForNextFrame(
                new LogicEntityId(11),
                SkillInputRuntime.MaxSkillCount,
                FixVector2.Zero));
    }

    private static ulong ComputeHash()
    {
        var hasher = new LogicStateHasher();
        LogicSkillCastCommandService.WriteDeterministicState(hasher);
        return hasher.Hash;
    }
}
