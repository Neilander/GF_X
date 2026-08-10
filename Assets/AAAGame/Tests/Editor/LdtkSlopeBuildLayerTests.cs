using System;
using System.Collections.Generic;
using System.Linq;
using AAAGame.Tilemap;
using GiantGrey.TileWorldCreator;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

[TestFixture]
public sealed class LdtkSlopeBuildLayerTests
{
    private const string Ramp45Path = "Assets/AAAGame/Models/SlopePlaceholder/Ramp45.prefab";
    private const string Ramp2x1Path = "Assets/AAAGame/Models/SlopePlaceholder/Ramp2x1.prefab";
    private const float CellSize = 1.4f;
    private const float SurfaceBase = 3.7f;

    private Configuration configuration;
    private Dictionary<int, BlueprintLayer> platformLayers;
    private BlueprintLayer slopeLayer;
    private LdtkSlopeBuildLayer buildLayer;
    private GameObject managerObject;
    private TileWorldCreatorManager manager;

    [SetUp]
    public void SetUp()
    {
        configuration = ScriptableObject.CreateInstance<Configuration>();
        configuration.cellSize = CellSize;
        platformLayers = new Dictionary<int, BlueprintLayer>();
        slopeLayer = ScriptableObject.CreateInstance<BlueprintLayer>();
        managerObject = new GameObject(nameof(LdtkSlopeBuildLayerTests));
        manager = managerObject.AddComponent<TileWorldCreatorManager>();
        manager.configuration = configuration;

        buildLayer = ScriptableObject.CreateInstance<LdtkSlopeBuildLayer>();
        buildLayer.layerName = "Build Slope Test";
        buildLayer.guid = Guid.NewGuid().ToString();
        buildLayer.hierarchyLayerID = buildLayer.guid;
        buildLayer.slopeLayer = slopeLayer;
        for (int height = 0; height <= 5; height++)
        {
            BlueprintLayer layer = ScriptableObject.CreateInstance<BlueprintLayer>();
            platformLayers.Add(height, layer);
            buildLayer.platformLevels.Add(new LdtkSlopeBuildLayer.PlatformLevel { height = height, layer = layer });
        }

        buildLayer.ramp45Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Ramp45Path);
        buildLayer.ramp2x1Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Ramp2x1Path);
        buildLayer.surfaceBaseHeight = SurfaceBase;
        buildLayer.surfaceHeightStep = CellSize;
        buildLayer.objectLayer = LayerMask.NameToLayer("Ground");

        Assert.That(buildLayer.ramp45Prefab, Is.Not.Null);
        Assert.That(buildLayer.ramp2x1Prefab, Is.Not.Null);
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(managerObject);
        UnityEngine.Object.DestroyImmediate(buildLayer);
        foreach (BlueprintLayer layer in platformLayers.Values)
        {
            UnityEngine.Object.DestroyImmediate(layer);
        }

        UnityEngine.Object.DestroyImmediate(slopeLayer);
        UnityEngine.Object.DestroyImmediate(configuration);
    }

    [Test]
    public void ExecuteLayer_OneCellRun_BuildsExact45DegreeCollider()
    {
        AddPlatform(0, new Vector2(-1f, 0f));
        AddSlope(Vector2.zero);
        AddPlatform(1, new Vector2(1f, 0f));

        buildLayer.ExecuteLayer(configuration, managerObject, manager);

        MeshCollider[] colliders = OrderedColliders();
        Assert.That(colliders, Has.Length.EqualTo(2));
        MeshCollider collider = colliders[0];
        Assert.That(collider, Is.Not.Null);
        Assert.That(collider.sharedMesh, Is.SameAs(collider.GetComponent<MeshFilter>().sharedMesh));
        AssertVector(collider.transform.localPosition, new Vector3(-CellSize * 0.5f, SurfaceBase, -CellSize * 0.5f));
        AssertVector(collider.transform.forward, Vector3.right);
        AssertVector(collider.GetComponent<MeshRenderer>().bounds.size, new Vector3(CellSize, CellSize, CellSize));
        Physics.SyncTransforms();
        Bounds combinedBounds = colliders[0].bounds;
        for (int i = 1; i < colliders.Length; i++)
        {
            combinedBounds.Encapsulate(colliders[i].bounds);
        }
        AssertVector(combinedBounds.size, new Vector3(CellSize, CellSize, CellSize * 2f));
        Assert.That(colliders.All(item => item.gameObject.layer == LayerMask.NameToLayer("Ground")), Is.True);
        Assert.That(
            collider.Raycast(
                new Ray(collider.transform.TransformPoint(new Vector3(0f, 2f, -0.4f)), -collider.transform.up),
                out RaycastHit lowHit,
                CellSize * 3f),
            Is.True);
        Assert.That(
            collider.Raycast(
                new Ray(collider.transform.TransformPoint(new Vector3(0f, 2f, 0.4f)), -collider.transform.up),
                out RaycastHit highHit,
                CellSize * 3f),
            Is.True);
        Assert.That(collider.transform.InverseTransformPoint(lowHit.point).y, Is.EqualTo(0.1f).Within(0.01f));
        Assert.That(collider.transform.InverseTransformPoint(highHit.point).y, Is.EqualTo(0.9f).Within(0.01f));

        buildLayer.ExecuteLayer(configuration, managerObject, manager);
        Assert.That(managerObject.GetComponentsInChildren<MeshCollider>(true), Has.Length.EqualTo(2));
    }

    [Test]
    public void ExecuteLayer_TwoCellRun_BuildsSingleCentered2x1Collider()
    {
        AddPlatform(0, new Vector2(-1f, 0f));
        AddSlope(new Vector2(0f, 0f), new Vector2(1f, 0f));
        AddPlatform(1, new Vector2(2f, 0f));

        buildLayer.ExecuteLayer(configuration, managerObject, manager);

        MeshCollider[] colliders = managerObject.GetComponentsInChildren<MeshCollider>(true);
        Assert.That(colliders, Has.Length.EqualTo(2));
        Assert.That(colliders.All(item => Mathf.Abs(item.transform.localPosition.x) < 0.001f), Is.True);
        Assert.That(colliders.Select(item => item.transform.localPosition.z), Is.EquivalentTo(new[] { -CellSize * 0.5f, CellSize * 0.5f }));
        Assert.That(colliders.All(item => item.GetComponent<MeshRenderer>().bounds.size == new Vector3(CellSize * 2f, CellSize, CellSize)), Is.True);
    }

    [Test]
    public void ExecuteLayer_TwoLevel45DegreeRun_BuildsOneRampPerHeight()
    {
        AddPlatform(0, new Vector2(-1f, 0f));
        AddSlope(new Vector2(0f, 0f), new Vector2(1f, 0f));
        AddPlatform(2, new Vector2(2f, 0f));

        buildLayer.ExecuteLayer(configuration, managerObject, manager);

        MeshCollider[] colliders = OrderedColliders();
        Assert.That(colliders, Has.Length.EqualTo(4));
        Assert.That(colliders.Count(item => Mathf.Abs(item.transform.localPosition.x + CellSize * 0.5f) < 0.001f), Is.EqualTo(2));
        Assert.That(colliders.Count(item => Mathf.Abs(item.transform.localPosition.x - CellSize * 0.5f) < 0.001f), Is.EqualTo(2));
        Assert.That(colliders.All(item => Mathf.Abs(item.GetComponent<MeshRenderer>().bounds.size.x - CellSize) < 0.001f), Is.True);
    }

    [Test]
    public void ExecuteLayer_TwoLevelShallowRun_BuildsOne2x1RampPerHeight()
    {
        AddPlatform(0, new Vector2(-1f, 0f));
        AddSlope(
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(2f, 0f),
            new Vector2(3f, 0f));
        AddPlatform(2, new Vector2(4f, 0f));

        buildLayer.ExecuteLayer(configuration, managerObject, manager);

        MeshCollider[] colliders = OrderedColliders();
        Assert.That(colliders, Has.Length.EqualTo(4));
        Assert.That(colliders.Count(item => Mathf.Abs(item.transform.localPosition.x) < 0.001f), Is.EqualTo(2));
        Assert.That(colliders.Count(item => Mathf.Abs(item.transform.localPosition.x - CellSize * 2f) < 0.001f), Is.EqualTo(2));
        Assert.That(colliders.All(item => Mathf.Abs(item.GetComponent<MeshRenderer>().bounds.size.x - CellSize * 2f) < 0.001f), Is.True);
    }

    [Test]
    public void ExecuteLayer_TwoCellsWide_BuildsEveryLaneAndHeight()
    {
        AddPlatform(0, new Vector2(-1f, 0f), new Vector2(-1f, 1f));
        AddSlope(
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f));
        AddPlatform(2, new Vector2(2f, 0f), new Vector2(2f, 1f));

        buildLayer.ExecuteLayer(configuration, managerObject, manager);

        MeshCollider[] colliders = managerObject.GetComponentsInChildren<MeshCollider>(true);
        Assert.That(colliders, Has.Length.EqualTo(6));
        Assert.That(colliders.Count(item => Mathf.Abs(item.transform.localPosition.y - SurfaceBase) < 0.001f), Is.EqualTo(3));
        Assert.That(colliders.Count(item => Mathf.Abs(item.transform.localPosition.y - SurfaceBase - CellSize) < 0.001f), Is.EqualTo(3));
        Assert.That(colliders.All(item => Vector3.Dot(item.transform.forward, Vector3.right) > 0.999f), Is.True);
    }

    [Test]
    public void Resolve_OverlappingPlatforms_UsesTopLayerAndDerivesSlopeSupport()
    {
        AddPlatform(0, new Vector2(-1f, 0f), new Vector2(0f, 0f), new Vector2(1f, 0f));
        AddPlatform(1, new Vector2(-1f, 0f), new Vector2(1f, 0f));
        AddPlatform(3, new Vector2(2f, 0f));
        AddSlope(new Vector2(0f, 0f), new Vector2(1f, 0f));

        Dictionary<Vector2, int> topHeights = GetTopHeights();
        bool success = LdtkSlopeLayoutResolver.TryResolve(slopeLayer.allPositions.ToHashSet(), topHeights, out LdtkSlopeLayout layout, out string error);

        Assert.That(success, Is.True, error);
        Assert.That(topHeights[new Vector2(-1f, 0f)], Is.EqualTo(1));
        Assert.That(layout.requiredPlatformHeights[new Vector2(0f, 0f)], Is.EqualTo(1));
        Assert.That(layout.requiredPlatformHeights[new Vector2(1f, 0f)], Is.EqualTo(2));
    }

    [Test]
    public void Resolve_PlatformAboveInferredSupport_ReportsExactConflict()
    {
        AddPlatform(0, new Vector2(-1f, 0f));
        AddPlatform(1, new Vector2(1f, 0f));
        AddPlatform(2, Vector2.zero);
        AddSlope(Vector2.zero);

        bool success = LdtkSlopeLayoutResolver.TryResolve(
            slopeLayer.allPositions.ToHashSet(),
            GetTopHeights(),
            out _,
            out string error);

        Assert.That(success, Is.False);
        StringAssert.Contains("(0,0)", error);
        StringAssert.Contains("requires support at Plane_H0", error);
        StringAssert.Contains("top platform is Plane_H2", error);
    }

    [TestCase(1, 2)]
    [TestCase(3, 2)]
    public void ExecuteLayer_InvalidRunRiseRatio_ThrowsWithCellCoordinates(int run, int rise)
    {
        AddPlatform(0, new Vector2(-1f, 0f));
        AddSlope(Enumerable.Range(0, run).Select(x => new Vector2(x, 0f)).ToArray());
        AddPlatform(rise, new Vector2(run, 0f));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => buildLayer.ExecuteLayer(configuration, managerObject, manager));

        StringAssert.Contains("(0,0)", exception.Message);
        StringAssert.Contains("run:rise equal to 1:1 or 2:1", exception.Message);
    }

    [Test]
    public void SampleLayerHeight_NonTileBuildLayer_DoesNotThrow()
    {
        var folder = new BuildLayerFolder("Test");
        folder.buildLayers.Add(buildLayer);
        configuration.buildLayerFolders.Add(folder);

        Assert.DoesNotThrow(() => manager.SampleLayerHeight(Vector3.zero));
        Assert.That(manager.SampleLayerHeight(Vector3.zero), Is.EqualTo(0f));
    }

    private void AddPlatform(int height, params Vector2[] cells)
    {
        platformLayers[height].allPositions.UnionWith(cells);
    }

    private void AddSlope(params Vector2[] cells)
    {
        slopeLayer.allPositions.UnionWith(cells);
    }

    private Dictionary<Vector2, int> GetTopHeights()
    {
        return LdtkSlopeLayoutResolver.GetTopPlatformHeights(platformLayers.Select(pair =>
            new KeyValuePair<int, IEnumerable<Vector2>>(pair.Key, pair.Value.allPositions)));
    }

    private MeshCollider[] OrderedColliders()
    {
        return managerObject.GetComponentsInChildren<MeshCollider>(true)
            .OrderBy(item => item.transform.localPosition.x)
            .ThenBy(item => item.transform.localPosition.z)
            .ToArray();
    }

    private static void AssertVector(Vector3 actual, Vector3 expected)
    {
        Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f));
        Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f));
        Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f));
    }
}
