using System;
using System.Reflection;
using NUnit.Framework;

public sealed class FlowColdStartPreparationTests
{
    [Test]
    public void RuntimeCodePreparationDoesNotCreateNavigationWork()
    {
        int requests = FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationPathRequestCount();
        int sources = FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationPathSourceCount();
        int policies = FlowFieldCrowdMovementSystem.GetEditorTestSectorCorridorPolicyCount();
        int tiles = FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount();
        MethodInfo prepare = typeof(FlowFieldCrowdMovementSystem).GetMethod(
            "PrepareRuntimeCode", BindingFlags.Public | BindingFlags.Static);
        Assert.That(prepare, Is.Not.Null, "Flow runtime has no code preparation entry point.");
        prepare.Invoke(null, null);
        prepare.Invoke(null, null);
        SoldierAIBrain.PrepareRuntimeCode();
        Assert.That(FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationPathRequestCount(), Is.EqualTo(requests));
        Assert.That(FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationPathSourceCount(), Is.EqualTo(sources));
        Assert.That(FlowFieldCrowdMovementSystem.GetEditorTestSectorCorridorPolicyCount(), Is.EqualTo(policies));
        Assert.That(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), Is.EqualTo(tiles));
    }
}
