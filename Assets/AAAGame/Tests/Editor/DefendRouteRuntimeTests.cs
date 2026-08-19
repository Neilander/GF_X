using NUnit.Framework;

[TestFixture]
public sealed class DefendRouteRuntimeTests
{
    [TearDown]
    public void TearDown()
    {
        LogicStrongholdMap.Clear();
    }

    [Test]
    public void CapturingSourceDeactivatesOnlyThatFixedSource()
    {
        InitializeTwoStrongholds();

        Assert.IsTrue(DefendPhaseRuntime.GetEditorTestIsAttackGroupSourceActive("SH_A"));
        Assert.IsTrue(DefendPhaseRuntime.GetEditorTestIsAttackGroupSourceActive("SH_B"));

        LogicStrongholdMap.SetOwnerFactionId("SH_A", EntitySideHelper.PlayerFactionId);

        Assert.IsFalse(DefendPhaseRuntime.GetEditorTestIsAttackGroupSourceActive("SH_A"));
        Assert.IsTrue(DefendPhaseRuntime.GetEditorTestIsAttackGroupSourceActive("SH_B"));
    }

    [Test]
    public void RouteStateDoesNotChangeWhenStrongholdOwnershipChanges()
    {
        InitializeTwoStrongholds();
        var brain = new SoldierAIBrain();
        brain.ConfigureDefendRoute(
            new[] { new FixVector2((Fix64)4, (Fix64)5), new FixVector2((Fix64)8, (Fix64)9) },
            new[] { "SH_B", (string)null });

        var before = new LogicStateHasher();
        brain.WriteDeterministicState(before);
        LogicStrongholdMap.SetOwnerFactionId("SH_B", EntitySideHelper.PlayerFactionId);
        var after = new LogicStateHasher();
        brain.WriteDeterministicState(after);

        Assert.AreEqual(before.Hash, after.Hash);
    }

    [Test]
    public void RouteConfigurationClonesInputsAndEntityParamsPoolClearsRoute()
    {
        var positions = new[] { new FixVector2((Fix64)4, (Fix64)5) };
        var strongholdIds = new[] { "SH_A" };
        var configuredBrain = new SoldierAIBrain();
        configuredBrain.ConfigureDefendRoute(positions, strongholdIds);
        positions[0] = new FixVector2((Fix64)99, (Fix64)99);
        strongholdIds[0] = "CHANGED";

        var expectedBrain = new SoldierAIBrain();
        expectedBrain.ConfigureDefendRoute(
            new[] { new FixVector2((Fix64)4, (Fix64)5) },
            new[] { "SH_A" });
        var configuredHash = new LogicStateHasher();
        var expectedHash = new LogicStateHasher();
        configuredBrain.WriteDeterministicState(configuredHash);
        expectedBrain.WriteDeterministicState(expectedHash);
        Assert.AreEqual(expectedHash.Hash, configuredHash.Hash);

        var entityParams = new TestEntityParams();
        entityParams.DefendRouteWaypointsFixed = positions;
        entityParams.DefendRouteWaypointStrongholdIds = strongholdIds;
        entityParams.ResetForTest();

        Assert.IsNull(entityParams.DefendRouteWaypointsFixed);
        Assert.IsNull(entityParams.DefendRouteWaypointStrongholdIds);
    }

    private static void InitializeTwoStrongholds()
    {
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("SH_A", 0, 0, EntitySideHelper.EnemyFactionId),
                new LogicStrongholdCellDefinition("SH_B", 1, 0, EntitySideHelper.EnemyFactionId),
            });
    }

    private sealed class TestEntityParams : EntityParams
    {
        public void ResetForTest()
        {
            ResetProperties();
        }
    }
}
