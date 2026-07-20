using NUnit.Framework;

public class LogicGameplayStateHasherTests
{
    [TearDown]
    public void TearDown()
    {
        EndTimelineIfActive();
    }

    [Test]
    public void EmptyWorldHash_IsStableAcrossEquivalentTimelines()
    {
        ulong first = RunEmptyFrameAndHash();
        EndTimelineIfActive();
        ulong second = RunEmptyFrameAndHash();

        Assert.AreNotEqual(0UL, first);
        Assert.AreEqual(first, second);
    }

    [Test]
    public void StringHash_UsesDeterministicContentIncludingUnicode()
    {
        var first = new LogicStateHasher();
        var second = new LogicStateHasher();
        first.Add("Unit_Hero_英雄");
        second.Add("Unit_Hero_英雄");

        Assert.AreEqual(first.Hash, second.Hash);
        second.Add("changed");
        Assert.AreNotEqual(first.Hash, second.Hash);
    }

    private static ulong RunEmptyFrameAndHash()
    {
        EnsureInGameDataModel();
        EntityRegistry.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicInteractionHoldService.BeginTimeline();
        LogicInteractionTargetStateService.BeginTimeline();
        LogicInteractionCommandService.BeginTimeline();
        Assert.IsTrue(LogicInteractionHoldService.IsActive, "Interaction hold service must be active after BeginTimeline.");
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        LogicTechEffectCommandService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        LogicObstacleCommandService.BeginTimeline();
        LogicFrameRuntime.Begin();
        LogicEntityFrameSnapshotService.BeginTimeline();
        MAEntityLogicFrameSystem.BeginTimeline();
        LogicFrameRuntime.StartTimeline();

        LogicTimeControlService.BeginFrame(1);
        var inputTimeline = new LogicInputTimeline();
        inputTimeline.Begin(0d, FixVector2.Zero, 0, FixVector2.Zero);
        LogicInteractionHoldService.ProcessFrame(inputTimeline.Seal(1, 1d / 30d));
        Assert.IsTrue(LogicInteractionHoldService.IsActive, "Interaction hold service became inactive while sealing input.");
        LogicPhaseCommandService.ApplyFrameForTests(1, _ => { });
        LogicInteractionCommandService.ApplyFrameForTests(1, _ => { });
        LogicTechEffectCommandService.ApplyFrameForTests(1, _ => { });
        LogicEntityLifecycleService.ApplyFrame(1);
        LogicObstacleCommandService.ApplyFrameForTests(1, _ => { });
        LogicFrameRuntime.Tick(1);
        Assert.IsTrue(LogicInteractionHoldService.IsActive, "Interaction hold service became inactive during LogicFrameRuntime.Tick.");
        return LogicGameplayStateHasher.ComputeCurrentFrame();
    }

    private static void EndTimelineIfActive()
    {
        if (MAEntityLogicFrameSystem.IsActive)
            MAEntityLogicFrameSystem.EndTimeline();
        if (LogicEntityFrameSnapshotService.IsActive)
            LogicEntityFrameSnapshotService.EndTimeline();
        if (LogicFrameRuntime.IsActive)
            LogicFrameRuntime.End();
        if (LogicObstacleCommandService.IsActive)
            LogicObstacleCommandService.EndTimeline();
        if (LogicEntityLifecycleService.IsActive)
            LogicEntityLifecycleService.EndTimeline();
        if (LogicTechEffectCommandService.IsActive)
            LogicTechEffectCommandService.EndTimeline();
        if (LogicInteractionCommandService.IsActive)
            LogicInteractionCommandService.EndTimeline();
        if (LogicInteractionTargetStateService.IsActive)
            LogicInteractionTargetStateService.EndTimeline();
        if (LogicPhaseCommandService.IsActive)
            LogicPhaseCommandService.EndTimeline();
        if (LogicInteractionHoldService.IsActive)
            LogicInteractionHoldService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
        EntityRegistry.Clear();
    }

    private static void EnsureInGameDataModel()
    {
        System.Reflection.FieldInfo dataModelField = typeof(GF).GetField(
            "<DataModel>k__BackingField",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        GameFramework.DataModelComponent current = dataModelField?.GetValue(null) as GameFramework.DataModelComponent;
        if (current == null)
        {
            var gameObject = new UnityEngine.GameObject("LogicGameplayStateHasherTests_DataModel");
            current = gameObject.AddComponent<GameFramework.DataModelComponent>();
            dataModelField?.SetValue(null, current);
        }

        System.Reflection.FieldInfo dataModelsField = typeof(GameFramework.DataModelComponent).GetField(
            "m_DataModels",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        object dataModels = dataModelsField?.GetValue(current);
        if (dataModelsField != null && (dataModels == null || dataModels.GetType() != dataModelsField.FieldType))
        {
            dataModels = System.Activator.CreateInstance(dataModelsField.FieldType);
            dataModelsField.SetValue(current, dataModels);
        }
        if (current.GetDataModel<InGameDataModel>() != null)
            return;

        var model = (InGameDataModel)System.Activator.CreateInstance(typeof(InGameDataModel), true);
        System.Type pairType = typeof(GameFramework.DataModelComponent).Assembly.GetType("TypeIdPair");
        object pair = System.Activator.CreateInstance(
            pairType,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic,
            null,
            new object[] { typeof(InGameDataModel), 0 },
            null);
        dataModels.GetType().GetMethod("Add")?.Invoke(dataModels, new[] { pair, model });
    }
}
