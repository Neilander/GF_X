using System;
using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public sealed class LogicInGameValueCommandServiceTests
{
    [SetUp]
    public void SetUp()
    {
        LogicTimeControlService.BeginTimeline();
        LogicInGameValueCommandService.BeginTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicInGameValueCommandService.IsActive)
            LogicInGameValueCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
    }

    [Test]
    public void Requests_DoNotApplyImmediatelyAndApplyOnExactNextFrame()
    {
        var applied = new List<LogicInGameValueCommand>();
        LogicInGameValueCommand coin = LogicInGameValueCommandService.ScheduleDeltaForNextFrame(IngameValueType.Coin, 1);
        LogicInGameValueCommand supply = LogicInGameValueCommandService.ScheduleDeltaForNextFrame(IngameValueType.MaxSupply, 2);

        Assert.IsEmpty(applied);
        Assert.AreEqual(1UL, coin.EffectiveFrame);
        Assert.AreEqual(1UL, supply.EffectiveFrame);
        LogicTimeControlService.BeginFrame(1);
        LogicInGameValueCommandService.ApplyFrameForTests(1, applied.Add);

        CollectionAssert.AreEqual(
            new[] { coin.Sequence, supply.Sequence },
            new[] { applied[0].Sequence, applied[1].Sequence });
        Assert.AreEqual(0, LogicInGameValueCommandService.PendingCount);
        Assert.AreEqual(2, LogicInGameValueCommandService.AppliedCount);
        Assert.AreEqual(1UL, LogicInGameValueCommandService.LastAppliedFrame);
    }

    [Test]
    public void PausedTimeline_KeepsCommandPendingUntilFirstResumedTick()
    {
        const int pauseSource = 771;
        var applied = new List<LogicInGameValueCommand>();
        LogicTimeControlService.AcquirePause(pauseSource);
        LogicInGameValueCommand command = LogicInGameValueCommandService.ScheduleDeltaForNextFrame(IngameValueType.Coin, 1);

        Assert.AreEqual(1, LogicInGameValueCommandService.PendingCount);
        Assert.IsEmpty(applied);
        LogicTimeControlService.ReleasePause(pauseSource);
        LogicTimeControlService.BeginFrame(1);
        LogicInGameValueCommandService.ApplyFrameForTests(1, applied.Add);

        Assert.AreEqual(command.Sequence, applied[0].Sequence);
    }

    [Test]
    public void DeterministicState_ChangesForPendingAndAppliedCommands()
    {
        ulong initial = ComputeStateHash();
        LogicInGameValueCommandService.ScheduleDeltaForNextFrame(IngameValueType.Coin, 1);
        ulong pending = ComputeStateHash();
        LogicTimeControlService.BeginFrame(1);
        LogicInGameValueCommandService.ApplyFrameForTests(1, _ => { });
        ulong applied = ComputeStateHash();

        Assert.AreNotEqual(initial, pending);
        Assert.AreNotEqual(pending, applied);
        Assert.AreNotEqual(initial, applied);
    }

    [Test]
    public void Schedule_RejectsPhaseZeroDeltaAndUnknownType()
    {
        Assert.Throws<ArgumentException>(
            () => LogicInGameValueCommandService.ScheduleDeltaForNextFrame(IngameValueType.Phase, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LogicInGameValueCommandService.ScheduleDeltaForNextFrame(IngameValueType.Coin, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LogicInGameValueCommandService.ScheduleDeltaForNextFrame((IngameValueType)int.MaxValue, 1));
    }

    private static ulong ComputeStateHash()
    {
        var hasher = new LogicStateHasher();
        LogicInGameValueCommandService.WriteDeterministicState(hasher);
        return hasher.Hash;
    }
}
