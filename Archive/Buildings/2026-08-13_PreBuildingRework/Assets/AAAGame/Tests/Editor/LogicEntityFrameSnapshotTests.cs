using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class LogicEntityFrameSnapshotTests
{
    [Test]
    public void Build_SortsByLogicId_AndCopiesFrameStartValues()
    {
        var entity20 = CreateEntity(20, new Vector3(2f, 7f, 4f), (Fix64)20);
        var entity10 = CreateEntity(10, new Vector3(1f, 9f, 3f), (Fix64)10);
        var entities = new List<IEntityContext> { entity20, entity10 };

        LogicEntityFrameSnapshot snapshot = LogicEntityFrameSnapshotBuilder.Build(17, entities);
        entity10.Position = new Vector3(99f, 0f, 99f);

        Assert.AreEqual(17UL, snapshot.FrameId);
        Assert.AreEqual(2, snapshot.States.Count);
        Assert.AreEqual(10, snapshot.States[0].EntityId.Value);
        Assert.AreEqual(20, snapshot.States[1].EntityId.Value);
        Assert.AreEqual((Fix64)1f, snapshot.States[0].Position.x);
        Assert.AreEqual((Fix64)3f, snapshot.States[0].Position.y);
        Assert.AreEqual(Fix64.Zero, snapshot.States[0].Forward.x);
        Assert.AreEqual(Fix64.One, snapshot.States[0].Forward.y);
        Assert.AreEqual(DistanceUnitConverter.ConvertToWorld((Fix64)10), snapshot.States[0].CollisionRadius);
        Assert.AreEqual(LogicCombatShapeKind.Circle, snapshot.States[0].CombatShape.Kind);
        Assert.AreEqual(snapshot.States[0].Position, snapshot.States[0].CombatShape.Center);
        Assert.AreEqual(snapshot.States[0].CollisionRadius, snapshot.States[0].CombatShape.Radius);
        Assert.AreEqual((Fix64)1f, snapshot.GetRequired(new LogicEntityId(10)).Position.x);
        Assert.AreEqual((Fix64)1f, snapshot.GetRequired(entity10).Position.x);
    }

    [Test]
    public void Build_IsIndependentOfInputOrder()
    {
        var entity30 = CreateEntity(30, new Vector3(3f, 0f, 6f), (Fix64)30);
        var entity10 = CreateEntity(10, new Vector3(1f, 0f, 2f), (Fix64)10);
        var entity20 = CreateEntity(20, new Vector3(2f, 0f, 4f), (Fix64)20);

        LogicEntityFrameSnapshot forward = LogicEntityFrameSnapshotBuilder.Build(
            5,
            new List<IEntityContext> { entity10, entity20, entity30 });
        LogicEntityFrameSnapshot reversed = LogicEntityFrameSnapshotBuilder.Build(
            5,
            new List<IEntityContext> { entity30, entity20, entity10 });

        for (int i = 0; i < forward.States.Count; i++)
        {
            Assert.AreEqual(forward.States[i].EntityId, reversed.States[i].EntityId);
            Assert.AreEqual(forward.States[i].Position, reversed.States[i].Position);
            Assert.AreEqual(forward.States[i].CollisionRadius, reversed.States[i].CollisionRadius);
            Assert.AreEqual(forward.States[i].CombatShape.Kind, reversed.States[i].CombatShape.Kind);
            Assert.AreEqual(forward.States[i].CombatShape.Center, reversed.States[i].CombatShape.Center);
        }
    }

    [Test]
    public void Build_RejectsDuplicateLogicIds()
    {
        var first = CreateEntity(7, Vector3.zero, (Fix64)10);
        var second = CreateEntity(7, Vector3.one, (Fix64)10);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => LogicEntityFrameSnapshotBuilder.Build(
                1,
                new List<IEntityContext> { first, second }));

        StringAssert.Contains("duplicate logic entity id 7", exception.Message);
    }

    [Test]
    public void PositionBoundary_RejectsNonFiniteBeforeSnapshot()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateEntity(1, new Vector3(float.NaN, 0f, 0f), (Fix64)10));

        StringAssert.Contains("finite XZ", exception.Message);
    }

    private static SimEntityContext CreateEntity(int id, Vector3 position, Fix64 collisionRadius)
    {
        var entity = new SimEntityContext
        {
            LogicEntityId = new LogicEntityId(id),
            Position = position,
            Side = SideType.PlayerSide,
        };
        entity.SetProperty(CreatureMainProperty.CollisionRadius, collisionRadius);
        return entity;
    }
}
