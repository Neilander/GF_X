using NUnit.Framework;
using UnityEngine;

public class LogicCombatShapeTests
{
    [Test]
    public void AxisAlignedBox_DistanceAndClosestPointUseFixedPointShape()
    {
        LogicCombatShape shape = LogicCombatShape.AxisAlignedBox(
            new FixVector2((Fix64)10, (Fix64)20),
            new FixVector2((Fix64)2, (Fix64)3));

        Assert.AreEqual(Fix64.Zero, shape.DistanceToSurface(new FixVector2((Fix64)9, (Fix64)22)));
        Assert.AreEqual((Fix64)4, shape.DistanceToSurface(new FixVector2((Fix64)16, (Fix64)20)));
        Assert.AreEqual(
            new FixVector2((Fix64)12, (Fix64)23),
            shape.ClosestPoint(new FixVector2((Fix64)16, (Fix64)27)));
    }

    [Test]
    public void AuthoredCatalog_LoadsEveryBuildingPrefabShape()
    {
        BuildingCombatShapeCatalog catalog = BuildingCombatShapeCatalog.LoadRequired();

        Assert.AreEqual(56, catalog.Entries.Count);
        for (int i = 0; i < catalog.Entries.Count; i++)
        {
            BuildingCombatShapeCatalog.Entry entry = catalog.Entries[i];
            LogicCombatShape shape = catalog.ResolveRequired(entry.PrefabPath, Vector3.zero, 0f);
            Assert.AreEqual(LogicCombatShapeKind.AxisAlignedBox, shape.Kind, entry.PrefabPath);
            Assert.IsTrue(shape.HalfExtents.x > Fix64.Zero, entry.PrefabPath);
            Assert.IsTrue(shape.HalfExtents.y > Fix64.Zero, entry.PrefabPath);
        }
    }

    [Test]
    public void AuthoredCatalog_QuarterTurnSwapsBoxExtentsExactly()
    {
        BuildingCombatShapeCatalog catalog = BuildingCombatShapeCatalog.LoadRequired();
        BuildingCombatShapeCatalog.Entry entry = catalog.Entries[0];
        LogicCombatShape unrotated = catalog.ResolveRequired(entry.PrefabPath, Vector3.zero, 0f);
        LogicCombatShape rotated = catalog.ResolveRequired(entry.PrefabPath, Vector3.zero, 90f);

        Assert.AreEqual(unrotated.HalfExtents.x, rotated.HalfExtents.y);
        Assert.AreEqual(unrotated.HalfExtents.y, rotated.HalfExtents.x);
        Assert.AreEqual(unrotated.Center.y, rotated.Center.x);
        Assert.AreEqual(-unrotated.Center.x, rotated.Center.y);
    }

    [Test]
    public void AuthoredCatalog_FixedAuthorityPositionPreservesEveryRawUnit()
    {
        BuildingCombatShapeCatalog catalog = BuildingCombatShapeCatalog.LoadRequired();
        BuildingCombatShapeCatalog.Entry entry = catalog.Entries[0];
        var position = new FixVector2(Fix64.FromRaw(123456789), Fix64.FromRaw(-987654321));
        LogicCombatShape local = catalog.ResolveRequired(entry.PrefabPath, FixVector2.Zero, 1);
        LogicCombatShape translated = catalog.ResolveRequired(entry.PrefabPath, position, 1);

        Assert.AreEqual(position.x.RawValue, (translated.Center.x - local.Center.x).RawValue);
        Assert.AreEqual(position.y.RawValue, (translated.Center.y - local.Center.y).RawValue);
        Assert.AreEqual(local.HalfExtents, translated.HalfExtents);
    }

    [Test]
    public void AuthoredObstacleCatalog_PreservesAllBlockingBoxes()
    {
        BuildingLogicObstacleShapeCatalog catalog = BuildingLogicObstacleShapeCatalog.LoadRequired();
        int boxCount = 0;
        int emptyPrefabCount = 0;
        for (int i = 0; i < catalog.Entries.Count; i++)
        {
            BuildingLogicObstacleShapeCatalog.PrefabEntry entry = catalog.Entries[i];
            var boxes = catalog.ResolveRequired(entry.PrefabPath, Vector3.zero, 0f);
            boxCount += boxes.Count;
            if (boxes.Count == 0)
                emptyPrefabCount++;
            for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
            {
                Assert.AreEqual(LogicCombatShapeKind.AxisAlignedBox, boxes[boxIndex].Kind, entry.PrefabPath);
                Assert.IsTrue(boxes[boxIndex].HalfExtents.x > Fix64.Zero, entry.PrefabPath);
                Assert.IsTrue(boxes[boxIndex].HalfExtents.y > Fix64.Zero, entry.PrefabPath);
            }
        }

        Assert.AreEqual(56, catalog.Entries.Count);
        Assert.AreEqual(160, boxCount);
        Assert.AreEqual(4, emptyPrefabCount);
    }
}
