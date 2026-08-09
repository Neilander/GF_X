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
    public void SubmitWhileInGameUiPaused_AppliesInCurrentFrameWithoutRemainingPending()
    {
        var applied = new List<LogicInteractionCommand>();
        LogicTimeControlService.BeginFrame(1);
        LogicInteractionCommandService.ApplyFrameForTests(1, applied.Add);
        LogicTimeControlService.AcquirePause(LogicTimeControlSources.InGameUiPause);

        LogicInteractionCommand command = LogicInteractionCommandService.SubmitForTests(
            LogicInteractionActionKind.ConstructBuilding,
            new LogicEntityId(10),
            "building-a",
            "Building_Barracks_Lv1",
            null,
            applied.Add);

        Assert.AreEqual(1UL, LogicTimeControlService.CurrentFrame);
        Assert.AreEqual(1UL, command.EffectiveFrame);
        Assert.AreEqual(1, applied.Count);
        Assert.AreEqual(0, LogicInteractionCommandService.PendingCount);
        Assert.AreEqual(1, LogicInteractionCommandService.AppliedCount);
        Assert.AreEqual(1, LogicInteractionCommandService.PendingAppliedPresentationCount);

        LogicTimeControlService.ReleasePause(LogicTimeControlSources.InGameUiPause);
        LogicTimeControlService.BeginFrame(2);
        LogicInteractionCommandService.ApplyFrameForTests(2, applied.Add);
        Assert.AreEqual(1, applied.Count);
    }

    [Test]
    public void PausedSubmit_KeepsPresentationReceiptUntilPanelCommitsPreview()
    {
        const int ownerId = 71005;
        LogicEntityId target = new LogicEntityId(10);
        LogicTimeControlService.BeginFrame(1);
        LogicInteractionCommandService.ApplyFrameForTests(1, _ => { });
        LogicTimeControlService.AcquirePause(LogicTimeControlSources.InGameUiPause);
        try
        {
            IngameCoinPreviewState.SetPreviewDeduction(ownerId, 3);
            LogicInteractionCommand command = LogicInteractionCommandService.SubmitForTests(
                LogicInteractionActionKind.UpgradeBuilding,
                target,
                "building-a",
                "Building_Barracks_Lv2",
                "Tech_A",
                _ => { });

            IngameCoinPreviewState.CommitPreviewDeduction(ownerId, target, command.ActionKind);
            Assert.IsTrue(IngameCoinPreviewState.IsCommitted);
            Assert.AreEqual(1, LogicInteractionCommandService.PendingAppliedPresentationCount);

            LogicInteractionCommandService.UpdatePresentationEvents();

            Assert.IsFalse(IngameCoinPreviewState.IsCommitted);
            Assert.AreEqual(0, IngameCoinPreviewState.PreviewDeduction);
        }
        finally
        {
            IngameCoinPreviewState.Reset();
            LogicTimeControlService.ReleasePause(LogicTimeControlSources.InGameUiPause);
        }
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
    public void ConsecutiveCommittedCoinPreviewsHandOffWithoutRestoringPreUpgradeValue()
    {
        const int ownerId = 71003;
        LogicEntityId target = new LogicEntityId(30);
        int authoritativeCoin = 10;
        bool presentationReceiptObserved = false;
        Action<LogicInteractionCommand> observeReceipt = _ => presentationReceiptObserved = true;
        LogicInteractionCommandService.CommandAppliedPresentation += observeReceipt;
        try
        {
            IngameCoinPreviewState.SetPreviewDeduction(ownerId, 3);
            LogicInteractionCommand command = LogicInteractionCommandService.ScheduleForNextFrame(
                LogicInteractionActionKind.UpgradeBuilding,
                target,
                "building-c",
                "Building_Barracks_Lv2",
                "Tech_C");
            IngameCoinPreviewState.CommitPreviewDeduction(ownerId, target, command.ActionKind);

            Assert.AreEqual(7, IngameCoinPreviewState.GetDisplayCoinValue(authoritativeCoin));
            IngameCoinPreviewState.ClearPreviewDeduction(ownerId);
            Assert.AreEqual(7, IngameCoinPreviewState.GetDisplayCoinValue(authoritativeCoin),
                "提交后松手或旧建筑面板关闭不得恢复提交前金币");

            LogicTimeControlService.BeginFrame(1);
            LogicInteractionCommandService.ApplyFrameForTests(1, _ => authoritativeCoin -= 3);

            Assert.IsFalse(presentationReceiptObserved,
                "逻辑帧内只能排队成交回执，不能直接驱动 UI");
            Assert.IsTrue(IngameCoinPreviewState.IsCommitted);
            Assert.AreEqual(1, LogicInteractionCommandService.PendingAppliedPresentationCount);

            LogicInteractionCommandService.UpdatePresentationEvents();

            Assert.IsTrue(presentationReceiptObserved);
            Assert.IsFalse(IngameCoinPreviewState.IsCommitted);
            Assert.AreEqual(0, IngameCoinPreviewState.PreviewDeduction);
            Assert.AreEqual(7, IngameCoinPreviewState.GetDisplayCoinValue(authoritativeCoin));

            const int secondOwnerId = 71004;
            LogicEntityId upgradedTarget = new LogicEntityId(31);
            IngameCoinPreviewState.SetPreviewDeduction(secondOwnerId, 2);
            LogicInteractionCommand secondCommand = LogicInteractionCommandService.ScheduleForNextFrame(
                LogicInteractionActionKind.UpgradeBuilding,
                upgradedTarget,
                "building-c",
                "Building_Barracks_Lv3",
                "Tech_D");
            IngameCoinPreviewState.CommitPreviewDeduction(secondOwnerId, upgradedTarget, secondCommand.ActionKind);

            Assert.AreEqual(5, IngameCoinPreviewState.GetDisplayCoinValue(authoritativeCoin));
            IngameCoinPreviewState.ClearPreviewDeduction(secondOwnerId);
            Assert.AreEqual(5, IngameCoinPreviewState.GetDisplayCoinValue(authoritativeCoin),
                "连续第二次提交也不得在权威扣款前恢复金额");

            LogicTimeControlService.BeginFrame(2);
            LogicInteractionCommandService.ApplyFrameForTests(2, _ => authoritativeCoin -= 2);
            LogicInteractionCommandService.UpdatePresentationEvents();

            Assert.IsFalse(IngameCoinPreviewState.IsCommitted);
            Assert.AreEqual(0, IngameCoinPreviewState.PreviewDeduction);
            Assert.AreEqual(5, IngameCoinPreviewState.GetDisplayCoinValue(authoritativeCoin));
        }
        finally
        {
            LogicInteractionCommandService.CommandAppliedPresentation -= observeReceipt;
            IngameCoinPreviewState.Reset();
        }
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
            RewardManager.HandleBuildingRecycleReward(FixVector2.Zero, 1));
    }

    private static ulong CaptureHash()
    {
        var hasher = new LogicStateHasher();
        LogicInteractionCommandService.WriteDeterministicState(hasher);
        return hasher.Hash;
    }
}
