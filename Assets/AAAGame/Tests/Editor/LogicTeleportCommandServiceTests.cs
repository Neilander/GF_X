using System;
using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public sealed class LogicTeleportCommandServiceTests
{
    [SetUp]
    public void SetUp()
    {
        LogicTimeControlService.BeginTimeline();
        LogicTeleportCommandService.BeginTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicTeleportCommandService.IsActive)
            LogicTeleportCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
    }

    [Test]
    public void PausedRequest_AppliesOnFirstResumedLogicFrame()
    {
        const int pauseSource = 91001;
        var applied = new List<LogicTeleportCommand>();
        var destination = new FixVector2((Fix64)12, (Fix64)34);

        LogicTimeControlService.AcquirePause(pauseSource);
        LogicTeleportCommand command = LogicTeleportCommandService.ScheduleForNextFrame(
            new LogicEntityId(7),
            destination,
            "Stronghold_0");

        Assert.AreEqual(1, LogicTeleportCommandService.PendingCount);
        Assert.IsEmpty(applied);

        LogicTimeControlService.ReleasePause(pauseSource);
        LogicTimeControlService.BeginFrame(1);
        LogicTeleportCommandService.ApplyFrameForTests(1, applied.Add);

        Assert.AreEqual(command.Sequence, applied[0].Sequence);
        Assert.AreEqual(destination, applied[0].Destination);
        Assert.AreEqual(0, LogicTeleportCommandService.PendingCount);
    }

    [Test]
    public void DeterministicState_ChangesForPendingAndAppliedCommands()
    {
        ulong initial = ComputeStateHash();
        LogicTeleportCommandService.ScheduleForNextFrame(
            new LogicEntityId(8),
            new FixVector2((Fix64)1, (Fix64)2),
            "Stronghold_1");
        ulong pending = ComputeStateHash();

        LogicTimeControlService.BeginFrame(1);
        LogicTeleportCommandService.ApplyFrameForTests(1, _ => { });
        ulong applied = ComputeStateHash();

        Assert.AreNotEqual(initial, pending);
        Assert.AreNotEqual(pending, applied);
        Assert.AreNotEqual(initial, applied);
    }

    [Test]
    public void Schedule_RejectsInvalidEntityAndStronghold()
    {
        Assert.Throws<ArgumentException>(() => LogicTeleportCommandService.ScheduleForNextFrame(
            default,
            FixVector2.Zero,
            "Stronghold_0"));
        Assert.Throws<ArgumentException>(() => LogicTeleportCommandService.ScheduleForNextFrame(
            new LogicEntityId(9),
            FixVector2.Zero,
            string.Empty));
    }

    private static ulong ComputeStateHash()
    {
        var hasher = new LogicStateHasher();
        LogicTeleportCommandService.WriteDeterministicState(hasher);
        return hasher.Hash;
    }
}
