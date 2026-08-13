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
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.BuildBeforeInvade);
        LogicTeleportCommandService.BeginTimeline();
        LogicStrongholdMap.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        LogicStrongholdMap.Clear();
        if (LogicTeleportCommandService.IsActive)
            LogicTeleportCommandService.EndTimeline();
        if (LogicPhaseCommandService.IsActive)
            LogicPhaseCommandService.EndTimeline();
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

    [Test]
    public void CombatTeleport_UsesCeilingWindUpFramesAndIncludesPlayerUnits()
    {
        Fix64 windUp = LogicFrameRuntime.FixedDeltaTime * (Fix64)2 + LogicFrameRuntime.FixedDeltaTime / (Fix64)2;

        LogicTeleportCommand command = LogicTeleportCommandService.ScheduleCombatTeleport(
            new LogicEntityId(11),
            new FixVector2((Fix64)3, (Fix64)4),
            "Stronghold_Combat",
            windUp);

        Assert.AreEqual(3ul, command.EffectiveFrame);
        Assert.IsTrue(command.IncludesPlayerUnits);
        Assert.AreEqual(1, LogicTeleportCommandService.PendingCount);
    }

    [Test]
    public void CombatTeleportInterrupt_RemovesOnlyCombatCast()
    {
        var entityId = new LogicEntityId(12);
        LogicTeleportCommandService.ScheduleForNextFrame(
            entityId,
            FixVector2.Zero,
            "Stronghold_Build");
        LogicTeleportCommandService.ScheduleCombatTeleport(
            entityId,
            FixVector2.Zero,
            "Stronghold_Combat",
            LogicFrameRuntime.FixedDeltaTime * (Fix64)2);

        LogicTeleportCommandService.InterruptCombatTeleport(entityId);

        Assert.AreEqual(1, LogicTeleportCommandService.PendingCount);
        Assert.IsFalse(LogicTeleportCommandService.History[0].IncludesPlayerUnits);
        Assert.IsTrue(LogicTeleportCommandService.History[1].IncludesPlayerUnits);
    }

    [Test]
    public void BlockingDestination_InterruptsPendingCombatTeleport()
    {
        LogicTeleportCommandService.ScheduleCombatTeleport(
            new LogicEntityId(13),
            FixVector2.Zero,
            "Stronghold_Blocked",
            LogicFrameRuntime.FixedDeltaTime * (Fix64)2);

        TeleportationPointService.BlockStrongholdTeleport("Stronghold_Blocked");

        Assert.AreEqual(0, LogicTeleportCommandService.PendingCount);
        Assert.IsTrue(TeleportationPointService.IsStrongholdTeleportBlocked("Stronghold_Blocked"));
    }

    [Test]
    public void RuntimeApply_RejectsDestinationOutsideClaimedStronghold()
    {
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("Stronghold_0", 0, 0, EntitySideHelper.PlayerFactionId),
                new LogicStrongholdCellDefinition("Stronghold_1", 1, 0, EntitySideHelper.PlayerFactionId),
            });
        LogicTeleportCommandService.ScheduleForNextFrame(
            new LogicEntityId(10),
            new FixVector2(Fix64.One, Fix64.Zero),
            "Stronghold_0");

        LogicTimeControlService.BeginFrame(1);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            LogicTeleportCommandService.ApplyFrame(1));

        StringAssert.Contains("destination stronghold mismatch", exception.Message);
        StringAssert.Contains("command=Stronghold_0", exception.Message);
        StringAssert.Contains("actual=Stronghold_1", exception.Message);
    }

    private static ulong ComputeStateHash()
    {
        var hasher = new LogicStateHasher();
        LogicTeleportCommandService.WriteDeterministicState(hasher);
        return hasher.Hash;
    }
}
