using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class LogicInteractionCommandServiceTests
{
    [SetUp]
    public void SetUp()
    {
        if (LogicInteractionCommandService.IsActive)
            LogicInteractionCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();

        LogicTimeControlService.BeginTimeline();
        LogicInteractionCommandService.BeginTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicInteractionCommandService.IsActive)
            LogicInteractionCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
    }

    [Test]
    public void Commands_ApplyOnExactFrameInSequenceOrder()
    {
        LogicInteractionCommand first = LogicInteractionCommandService.ScheduleForNextFrame(
            LogicInteractionActionKind.ConstructBuilding,
            new LogicEntityId(10),
            "building-a",
            "Building_Barracks_Lv1");
        LogicInteractionCommand second = LogicInteractionCommandService.ScheduleForNextFrame(
            LogicInteractionActionKind.ResearchTech,
            new LogicEntityId(20),
            "building-b",
            "Tech_A");
        var applied = new List<LogicInteractionCommand>();

        Assert.AreEqual(1UL, first.EffectiveFrame);
        Assert.AreEqual(1UL, second.EffectiveFrame);
        Assert.AreEqual(1UL, first.Sequence);
        Assert.AreEqual(2UL, second.Sequence);
        Assert.IsTrue(LogicInteractionCommandService.HasPendingForTarget(new LogicEntityId(10)));

        LogicTimeControlService.BeginFrame(1);
        LogicInteractionCommandService.ApplyFrameForTests(1, applied.Add);

        CollectionAssert.AreEqual(
            new[] { first.Sequence, second.Sequence },
            new[] { applied[0].Sequence, applied[1].Sequence });
        Assert.AreEqual(0, LogicInteractionCommandService.PendingCount);
        Assert.AreEqual(2, LogicInteractionCommandService.AppliedCount);
        Assert.AreEqual(1UL, LogicInteractionCommandService.LastAppliedFrame);
    }

    [Test]
    public void Schedule_RejectsSecondPendingCommandForSameStableTarget()
    {
        LogicEntityId target = new LogicEntityId(10);
        LogicInteractionCommandService.ScheduleForNextFrame(
            LogicInteractionActionKind.ResearchTech,
            target,
            "building-a",
            "Tech_A");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => LogicInteractionCommandService.ScheduleForNextFrame(
                LogicInteractionActionKind.RecycleBuilding,
                target,
                "building-a"));

        StringAssert.Contains("pending command", exception.Message);
    }

    [Test]
    public void DeterministicState_ChangesWhenCommandIsApplied()
    {
        LogicInteractionCommandService.ScheduleForNextFrame(
            LogicInteractionActionKind.UpgradeBuilding,
            new LogicEntityId(10),
            "building-a",
            "Building_Barracks_Lv2",
            "Tech_A");
        ulong pendingHash = CaptureHash();

        LogicTimeControlService.BeginFrame(1);
        LogicInteractionCommandService.ApplyFrameForTests(1, _ => { });
        ulong appliedHash = CaptureHash();

        Assert.AreNotEqual(pendingHash, appliedHash);
        Assert.AreEqual(appliedHash, CaptureHash());
    }

    [Test]
    public void RuntimeBuildingCreation_HasNoDirectVector3Bypass()
    {
        string buildManagerPath = Path.Combine(
            Application.dataPath,
            "AAAGame/Scripts/Build/BuildManager.cs");
        string source = File.ReadAllText(buildManagerPath);

        StringAssert.DoesNotContain(
            "public bool BuildBuilding(string buildingId, Vector3 position",
            source);
    }

    [Test]
    public void RecycleRewardMutation_RejectsOutsideApplyWindow()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RewardManager.HandleBuildingRecycleReward(Vector3.zero, 1));
    }

    private static ulong CaptureHash()
    {
        var hasher = new LogicStateHasher();
        LogicInteractionCommandService.WriteDeterministicState(hasher);
        return hasher.Hash;
    }
}
