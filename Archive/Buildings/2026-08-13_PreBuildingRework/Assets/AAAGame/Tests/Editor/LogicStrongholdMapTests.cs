using System;
using NUnit.Framework;

[TestFixture]
public sealed class LogicStrongholdMapTests
{
    [SetUp]
    public void SetUp()
    {
        LogicStrongholdMap.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        LogicStrongholdMap.Clear();
    }

    [Test]
    public void Resolve_UsesFrozenAffineTransformAndMidpointToEven()
    {
        LogicStrongholdMap.Initialize(
            new FixVector2((Fix64)10, (Fix64)20),
            new FixVector2(Fix64.Zero, (Fix64)2),
            new FixVector2((Fix64)(-3), Fix64.Zero),
            (Fix64)2,
            new[]
            {
                new LogicStrongholdCellDefinition("SH_EVEN_ZERO", 0, 0),
                new LogicStrongholdCellDefinition("SH_EVEN_TWO", 2, 0),
                new LogicStrongholdCellDefinition("SH_NEGATIVE_TWO", -2, 0),
            });

        AssertResolved("SH_EVEN_ZERO", WorldFromLocal((Fix64)1, Fix64.Zero));
        AssertResolved("SH_EVEN_TWO", WorldFromLocal((Fix64)3, Fix64.Zero));
        AssertResolved("SH_NEGATIVE_TWO", WorldFromLocal((Fix64)(-3), Fix64.Zero));
    }

    [Test]
    public void Initialize_RejectsOverlappingAuthoredCells()
    {
        Assert.Throws<InvalidOperationException>(() => LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("SH_A", 0, 0),
                new LogicStrongholdCellDefinition("SH_B", 0, 0),
            }));
    }

    [Test]
    public void CellDefinition_RejectsNegativeOwnerFaction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LogicStrongholdCellDefinition("SH_INVALID_OWNER", 0, 0, -1));
    }

    [Test]
    public void Resolve_RejectsUseBeforeLevelMapInitialization()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LogicStrongholdMap.TryResolveStrongholdId(FixVector2.Zero, out _));
    }

    [Test]
    public void OwnerFaction_IsFrozenAndDynamicChangesEnterDeterministicState()
    {
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("SH_OWNER", 0, 0, 2),
                new LogicStrongholdCellDefinition("SH_OWNER", 1, 0, 2),
            });

        Assert.IsTrue(LogicStrongholdMap.TryGetOwnerFactionId("SH_OWNER", out int initialOwner));
        Assert.AreEqual(2, initialOwner);
        var before = new LogicStateHasher();
        LogicStrongholdMap.WriteDeterministicState(before);

        LogicStrongholdMap.SetOwnerFactionId("SH_OWNER", 1);

        Assert.IsTrue(LogicStrongholdMap.TryGetOwnerFactionId("SH_OWNER", out int capturedOwner));
        Assert.AreEqual(1, capturedOwner);
        var after = new LogicStateHasher();
        LogicStrongholdMap.WriteDeterministicState(after);
        Assert.AreNotEqual(before.Hash, after.Hash);
    }

    [Test]
    public void AuthoredHash_ChangesWhenOnlyMapTransformChanges()
    {
        var cells = new[]
        {
            new LogicStrongholdCellDefinition("SH_HASH", 0, 0, 2),
        };
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            cells);
        var first = new LogicStateHasher();
        LogicStrongholdMap.WriteDeterministicState(first);

        LogicStrongholdMap.Clear();
        LogicStrongholdMap.Initialize(
            new FixVector2((Fix64)10, Fix64.Zero),
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            cells);
        var second = new LogicStateHasher();
        LogicStrongholdMap.WriteDeterministicState(second);

        Assert.AreNotEqual(first.Hash, second.Hash);
    }

    [Test]
    public void OwnerQueries_UseUniqueStrongholdsInStableIdOrder()
    {
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("SH_B", 0, 0, 1),
                new LogicStrongholdCellDefinition("SH_A", 1, 0, 0),
                new LogicStrongholdCellDefinition("SH_B", 2, 0, 1),
            });

        Assert.AreEqual(1, LogicStrongholdMap.CountOwnedStrongholds(1));
        CollectionAssert.AreEqual(
            new[] { "SH_A", "SH_B" },
            LogicStrongholdMap.GetStrongholdIdsOrdered());
    }

    [Test]
    public void CircleClearQuery_RejectsRadiusOverlapAtForeignCellEdgeAndSharedCorner()
    {
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("enemy", 0, 0, EntitySideHelper.EnemyFactionId),
                new LogicStrongholdCellDefinition("enemy", 1, 0, EntitySideHelper.EnemyFactionId),
                new LogicStrongholdCellDefinition("enemy", 0, 1, EntitySideHelper.EnemyFactionId),
                new LogicStrongholdCellDefinition("enemy", 1, 1, EntitySideHelper.EnemyFactionId),
            });

        Fix64 radius = (Fix64)0.25f;
        Assert.IsFalse(LogicStrongholdMap.IsCircleClearOfForeignStrongholds(
            new FixVector2((Fix64)(-0.7f), Fix64.Zero),
            radius,
            EntitySideHelper.PlayerFactionId));
        Assert.IsFalse(LogicStrongholdMap.IsCircleClearOfForeignStrongholds(
            new FixVector2((Fix64)(-0.65f), (Fix64)(-0.65f)),
            radius,
            EntitySideHelper.PlayerFactionId));
        Assert.IsTrue(LogicStrongholdMap.IsCircleClearOfForeignStrongholds(
            new FixVector2((Fix64)(-0.8f), (Fix64)(-0.8f)),
            radius,
            EntitySideHelper.PlayerFactionId));
    }

    private static FixVector2 WorldFromLocal(Fix64 localX, Fix64 localZ)
    {
        return new FixVector2((Fix64)10, (Fix64)20)
               + new FixVector2(Fix64.Zero, (Fix64)2) * localX
               + new FixVector2((Fix64)(-3), Fix64.Zero) * localZ;
    }

    private static void AssertResolved(string expected, FixVector2 worldPosition)
    {
        Assert.IsTrue(LogicStrongholdMap.TryResolveStrongholdId(worldPosition, out string actual));
        Assert.AreEqual(expected, actual);
    }
}
