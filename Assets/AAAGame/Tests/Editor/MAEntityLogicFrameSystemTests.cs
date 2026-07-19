using NUnit.Framework;

[TestFixture]
public class MAEntityLogicFrameSystemTests
{
    [SetUp]
    public void SetUp()
    {
        LogicFrameRuntime.Begin();
        LogicEntityFrameSnapshotService.BeginTimeline();
        MAEntityLogicFrameSystem.BeginTimeline();
        LogicFrameRuntime.StartTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        MAEntityLogicFrameSystem.EndTimeline();
        LogicEntityFrameSnapshotService.EndTimeline();
        LogicFrameRuntime.End();
    }

    [Test]
    public void EmptyFrame_CompletesAllPhasesAfterSnapshot()
    {
        LogicFrameRuntime.Tick(1);

        Assert.AreEqual(1UL, LogicEntityFrameSnapshotService.CapturedFrame);
        Assert.AreEqual(1UL, MAEntityLogicFrameSystem.LastCompletedFrame);
        Assert.AreEqual(0, MAEntityLogicFrameSystem.LastFrameEntityCount);
        Assert.AreEqual(0, MAEntityLogicFrameSystem.LastFramePhaseExecutionCount);
        Assert.AreEqual(MAEntityLogicFramePhase.PostUpdate, MAEntityLogicFrameSystem.LastCompletedPhase);
        Assert.AreEqual(1UL, LogicAgentCollisionShadowService.LastCompletedFrame);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastFrameEntityCount);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastBodyCount);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastCandidatePairCount);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastStates.Count);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastPairCorrectedBodyCount);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastStaticProjectionAvailableCount);
        Assert.AreEqual(0UL, LogicAgentCollisionShadowService.FramesWithPairCorrection);
        Assert.AreEqual(0UL, LogicAgentCollisionShadowService.TotalPairCorrectedBodyCount);
        Assert.AreEqual(1UL, LogicDamageEventService.LastCompletedFrame);
        Assert.AreEqual(1UL, LogicProjectileService.LastCompletedFrame);
        Assert.AreEqual(0, LogicProjectileService.ActiveCount);
    }

    [Test]
    public void WorldReset_ClearsLastFrameState()
    {
        LogicFrameRuntime.Tick(1);
        MAEntityLogicFrameSystem.ResetForWorldTransition();
        LogicEntityFrameSnapshotService.ResetForWorldTransition();

        Assert.AreEqual(0UL, MAEntityLogicFrameSystem.LastCompletedFrame);
        Assert.AreEqual(0, MAEntityLogicFrameSystem.LastFrameEntityCount);
        Assert.AreEqual(0, MAEntityLogicFrameSystem.LastFramePhaseExecutionCount);
        Assert.AreEqual(0UL, LogicEntityFrameSnapshotService.CapturedFrame);
        Assert.AreEqual(0UL, LogicAgentCollisionShadowService.LastCompletedFrame);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastFrameEntityCount);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastStates.Count);
        Assert.AreEqual(0UL, LogicAgentCollisionShadowService.FramesWithPairCorrection);
        Assert.AreEqual(0UL, LogicDamageEventService.LastCompletedFrame);
        Assert.AreEqual(0UL, LogicProjectileService.LastCompletedFrame);
    }
}
