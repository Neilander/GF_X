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
        EntityRegistry.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        LogicObstacleCommandService.BeginTimeline();
        LogicFrameRuntime.Begin();
        LogicEntityFrameSnapshotService.BeginTimeline();
        MAEntityLogicFrameSystem.BeginTimeline();
        LogicFrameRuntime.StartTimeline();

        LogicTimeControlService.BeginFrame(1);
        LogicEntityLifecycleService.ApplyFrame(1);
        LogicObstacleCommandService.ApplyFrameForTests(1, _ => { });
        LogicFrameRuntime.Tick(1);
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
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
        EntityRegistry.Clear();
    }
}
