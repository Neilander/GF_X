using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor;

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

        CollectionAssert.AreEquivalent(GetBuildingPrefabPaths(), GetCombatCatalogPaths(catalog));
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
        CollectionAssert.AreEquivalent(GetBuildingPrefabPaths(), GetObstacleCatalogPaths(catalog));
        for (int i = 0; i < catalog.Entries.Count; i++)
        {
            BuildingLogicObstacleShapeCatalog.PrefabEntry entry = catalog.Entries[i];
            var boxes = catalog.ResolveRequired(entry.PrefabPath, Vector3.zero, 0f);
            for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
            {
                Assert.AreEqual(LogicCombatShapeKind.AxisAlignedBox, boxes[boxIndex].Kind, entry.PrefabPath);
                Assert.IsTrue(boxes[boxIndex].HalfExtents.x > Fix64.Zero, entry.PrefabPath);
                Assert.IsTrue(boxes[boxIndex].HalfExtents.y > Fix64.Zero, entry.PrefabPath);
            }
        }

    }

    private static List<string> GetBuildingPrefabPaths()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { FbxToPrefab.BuildingOutputFolder });
        var paths = new List<string>(guids.Length);
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab != null && prefab.GetComponent<WallBranchView>() != null)
                continue;
            const string Prefix = "Assets/AAAGame/Prefabs/Entity/";
            paths.Add(assetPath.Substring(Prefix.Length, assetPath.Length - Prefix.Length - ".prefab".Length));
        }
        return paths;
    }

    private static List<string> GetCombatCatalogPaths(BuildingCombatShapeCatalog catalog)
    {
        var paths = new List<string>(catalog.Entries.Count);
        for (int i = 0; i < catalog.Entries.Count; i++)
            paths.Add(catalog.Entries[i].PrefabPath);
        return paths;
    }

    private static List<string> GetObstacleCatalogPaths(BuildingLogicObstacleShapeCatalog catalog)
    {
        var paths = new List<string>(catalog.Entries.Count);
        for (int i = 0; i < catalog.Entries.Count; i++)
            paths.Add(catalog.Entries[i].PrefabPath);
        return paths;
    }
}
