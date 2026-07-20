using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class LogicTechEffectCommandServiceTests
{
    [SetUp]
    public void SetUp()
    {
        if (LogicTechEffectCommandService.IsActive)
            LogicTechEffectCommandService.EndTimeline();
        if (LogicInteractionCommandService.IsActive)
            LogicInteractionCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();

        LogicTimeControlService.BeginTimeline();
        LogicInteractionCommandService.BeginTimeline();
        LogicTechEffectCommandService.BeginTimeline();
        ResetTechState();
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicTechEffectCommandService.IsActive)
            LogicTechEffectCommandService.EndTimeline();
        if (LogicInteractionCommandService.IsActive)
            LogicInteractionCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
        ResetTechState();
    }

    [Test]
    public void Commands_ApplyOnExactFrameInSequenceOrder()
    {
        LogicTechEffectCommand first = LogicTechEffectCommandService.ScheduleForNextFrame("Tech_A", false, 0, "building-a");
        LogicTechEffectCommand second = LogicTechEffectCommandService.ScheduleForNextFrame("Tech_B", true, 1, "building-b");
        var applied = new List<LogicTechEffectCommand>();

        Assert.AreEqual(1UL, first.EffectiveFrame);
        Assert.AreEqual(1UL, second.EffectiveFrame);
        Assert.AreEqual(1UL, first.Sequence);
        Assert.AreEqual(2UL, second.Sequence);
        Assert.IsTrue(LogicTechEffectCommandService.HasPending("Tech_A"));
        Assert.IsTrue(LogicTechEffectCommandService.HasPending("Tech_B", "building-b"));

        LogicTimeControlService.BeginFrame(1);
        LogicTechEffectCommandService.ApplyFrameForTests(1, applied.Add);

        CollectionAssert.AreEqual(
            new[] { first.Sequence, second.Sequence },
            new[] { applied[0].Sequence, applied[1].Sequence });
        Assert.AreEqual(0, LogicTechEffectCommandService.PendingCount);
        Assert.IsFalse(LogicTechEffectCommandService.HasPending("Tech_A"));
        Assert.AreEqual(2, LogicTechEffectCommandService.AppliedCount);
        Assert.AreEqual(1UL, LogicTechEffectCommandService.LastAppliedFrame);
    }

    [Test]
    public void ApplyFrame_RejectsMissedCommandFrame()
    {
        LogicTechEffectCommandService.ScheduleForNextFrame("Tech_A", false, 0, "building-a");
        LogicTimeControlService.BeginFrame(1);
        LogicTimeControlService.BeginFrame(2);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => LogicTechEffectCommandService.ApplyFrameForTests(2, _ => { }));

        StringAssert.Contains("missed its frame", exception.Message);
    }

    [Test]
    public void DeterministicState_ChangesWhenCommandIsApplied()
    {
        LogicTechEffectCommandService.ScheduleForNextFrame("Tech_A", false, 0, "building-a");
        ulong pendingHash = CaptureHash();

        LogicTimeControlService.BeginFrame(1);
        LogicTechEffectCommandService.ApplyFrameForTests(1, _ => { });
        ulong appliedHash = CaptureHash();

        Assert.AreNotEqual(pendingHash, appliedHash);
        Assert.AreEqual(appliedHash, CaptureHash());
    }

    [Test]
    public void UnlockTech_CommitsOwnershipOnlyInsideEffectiveFrame()
    {
        Assert.IsTrue(InGameDataModel.UnlockTech("Tech_A", false, "building-a", 0));
        Assert.IsFalse(InGameDataModel.HasUnlockedTech("Tech_A"));
        Assert.IsTrue(LogicTechEffectCommandService.HasPending("Tech_A"));

        LogicTimeControlService.BeginFrame(1);
        LogicTechEffectCommandService.ApplyFrameForTests(1, InGameDataModel.ApplyScheduledTechUnlock);

        Assert.IsTrue(InGameDataModel.HasUnlockedTech("Tech_A"));
        Assert.IsTrue(InGameDataModel.HasUnlockedTech("Tech_A", "building-a"));
        Assert.IsFalse(LogicTechEffectCommandService.HasPending("Tech_A"));
    }

    [Test]
    public void InGameDeterministicState_TracksAppliedTechOwnership()
    {
        ulong before = CaptureInGameHash();
        Assert.IsTrue(InGameDataModel.UnlockTech("Tech_A", false, "building-a", 0));
        Assert.AreEqual(before, CaptureInGameHash(), "Pending commands must not mutate applied economy state.");

        LogicTimeControlService.BeginFrame(1);
        LogicTechEffectCommandService.ApplyFrameForTests(1, InGameDataModel.ApplyScheduledTechUnlock);

        Assert.AreNotEqual(before, CaptureInGameHash());
    }

    [Test]
    public void InteractionApply_CanCommitTechOwnershipInTheSameLogicFrame()
    {
        LogicInteractionCommandService.ScheduleForNextFrame(
            LogicInteractionActionKind.ResearchTech,
            new LogicEntityId(10),
            "building-a",
            "Tech_A");

        LogicTimeControlService.BeginFrame(1);
        LogicInteractionCommandService.ApplyFrameForTests(
            1,
            _ => Assert.IsTrue(InGameDataModel.UnlockTechInCurrentInteractionFrame(
                "Tech_A",
                false,
                "building-a",
                0)));
        Assert.IsFalse(InGameDataModel.HasUnlockedTech("Tech_A"));

        LogicTechEffectCommandService.ApplyFrameForTests(1, InGameDataModel.ApplyScheduledTechUnlock);

        Assert.IsTrue(InGameDataModel.HasUnlockedTech("Tech_A"));
        Assert.AreEqual(1UL, LogicTechEffectCommandService.LastAppliedFrame);
    }

    private static ulong CaptureHash()
    {
        var hasher = new LogicStateHasher();
        LogicTechEffectCommandService.WriteDeterministicState(hasher);
        return hasher.Hash;
    }

    private static ulong CaptureInGameHash()
    {
        var hasher = new LogicStateHasher();
        InGameDataModel.WriteDeterministicState(hasher);
        return hasher.Hash;
    }

    private static void ResetTechState()
    {
        InGameDataModel model = GetOrCreateInGameDataModel();
        FieldInfo ownersField = typeof(InGameDataModel).GetField(
            "m_TechOwnerContextsById",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var owners = ownersField?.GetValue(model) as Dictionary<string, HashSet<string>>;
        owners?.Clear();
        typeof(InGameDataModel).GetProperty(nameof(InGameDataModel.UnlockedTechIds))?.SetValue(model, Array.Empty<string>());
    }

    private static InGameDataModel GetOrCreateInGameDataModel()
    {
        FieldInfo dataModelField = typeof(GF).GetField("<DataModel>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        GameFramework.DataModelComponent current = dataModelField?.GetValue(null) as GameFramework.DataModelComponent;
        if (current == null)
        {
            var gameObject = new GameObject("LogicTechEffectCommandServiceTests_DataModel");
            current = gameObject.AddComponent<GameFramework.DataModelComponent>();
            dataModelField?.SetValue(null, current);
        }

        FieldInfo dataModelsField = typeof(GameFramework.DataModelComponent).GetField(
            "m_DataModels",
            BindingFlags.Instance | BindingFlags.NonPublic);
        object dataModels = dataModelsField?.GetValue(current);
        if (dataModelsField != null && (dataModels == null || dataModels.GetType() != dataModelsField.FieldType))
        {
            dataModels = Activator.CreateInstance(dataModelsField.FieldType);
            dataModelsField.SetValue(current, dataModels);
        }

        InGameDataModel model = current.GetDataModel<InGameDataModel>();
        if (model != null)
            return model;

        model = (InGameDataModel)Activator.CreateInstance(typeof(InGameDataModel), true);
        Type pairType = typeof(GameFramework.DataModelComponent).Assembly.GetType("TypeIdPair");
        object pair = Activator.CreateInstance(
            pairType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { typeof(InGameDataModel), 0 },
            null);
        dataModels.GetType().GetMethod("Add")?.Invoke(dataModels, new[] { pair, model });
        return model;
    }
}
