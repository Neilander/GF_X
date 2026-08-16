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
    private const string RampPath = "Assets/AAAGame/Models/SlopePlaceholder/Ramp.prefab";
    private const float CellSize = 1.4f;
    private const float SurfaceBase = 3.7f;
    private const float PlatformEndExtension = 0.2f;
    private const float MaximumSlopeAngle = 45f;

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

        buildLayer.rampPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RampPath);
        buildLayer.platformEndExtension = PlatformEndExtension;
        buildLayer.maximumSlopeAngle = MaximumSlopeAngle;
        buildLayer.surfaceBaseHeight = SurfaceBase;
        buildLayer.surfaceHeightStep = CellSize;
        buildLayer.objectLayer = LayerMask.NameToLayer("Ground");

        Assert.That(buildLayer.rampPrefab, Is.Not.Null);
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
    public void DualGridControls_SeventeenByFourPlatform_ReconstructsExactDimensions()
    {
        var desired = new HashSet<Vector2>();
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 17; x++)
            {
                desired.Add(new Vector2(x, y));
            }
        }

        bool success = LdtkDualGridControlBuilder.TryBuildExactControls(
            desired,
            out HashSet<Vector2> controls,
            out HashSet<Vector2> missing,
            out HashSet<Vector2> extra,
            out string error);

        Assert.That(success, Is.True, error);
        Assert.That(controls, Has.Count.EqualTo(16 * 3));
        Assert.That(controls.Min(cell => cell.x), Is.EqualTo(0.5f));
        Assert.That(controls.Max(cell => cell.x), Is.EqualTo(15.5f));
        Assert.That(missing, Is.Empty);
        Assert.That(extra, Is.Empty);
    }

    [Test]
    public void DualGridControls_OneCellWidePlatform_IsRejectedWithExactMissingCells()
    {
        var desired = new HashSet<Vector2>
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(2f, 0f)
        };

        bool success = LdtkDualGridControlBuilder.TryBuildExactControls(
            desired,
            out HashSet<Vector2> controls,
            out HashSet<Vector2> missing,
            out HashSet<Vector2> extra,
            out string error);

        Assert.That(success, Is.False);
        Assert.That(error, Is.Null);
        Assert.That(controls, Is.Empty);
        Assert.That(missing, Is.EquivalentTo(desired));
        Assert.That(extra, Is.Empty);
    }

    [Test]
    public void ExecuteLayer_OneCellRun_ExtendsBothEndsToMeetInsetPlatforms()
    {
        AddPlatform(0, new Vector2(-1f, 0f));
        AddSlope(Vector2.zero);
        AddPlatform(1, new Vector2(1f, 0f));

        buildLayer.ExecuteLayer(configuration, managerObject, manager);

        MeshCollider[] colliders = OrderedColliders();
        Assert.That(colliders, Has.Length.EqualTo(1));
        MeshCollider collider = colliders[0];
        Assert.That(collider, Is.Not.Null);
        Assert.That(collider.sharedMesh, Is.SameAs(collider.GetComponent<MeshFilter>().sharedMesh));
        AssertVector(collider.transform.localPosition, new Vector3(0f, SurfaceBase, 0f));
        AssertVector(collider.transform.forward, Vector3.right);
        AssertVector(collider.GetComponent<MeshRenderer>().bounds.size, new Vector3(CellSize * 1.4f, CellSize, CellSize));
        Physics.SyncTransforms();
        Assert.That(collider.bounds.min.x, Is.EqualTo(-CellSize * (0.5f + PlatformEndExtension)).Within(0.001f));
        Assert.That(collider.bounds.max.x, Is.EqualTo(CellSize * (0.5f + PlatformEndExtension)).Within(0.001f));
        Assert.That(collider.bounds.min.z, Is.EqualTo(-CellSize * 0.5f).Within(0.001f));
        Assert.That(collider.bounds.max.z, Is.EqualTo(CellSize * 0.5f).Within(0.001f));
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
        Assert.That(managerObject.GetComponentsInChildren<MeshCollider>(true), Has.Length.EqualTo(1));
    }

    [Test]
    public void ExecuteLayer_TwoCellRun_BuildsOneContinuousRampCenteredOnSlopeCells()
    {
        AddPlatform(0, new Vector2(-1f, 0f));
        AddSlope(new Vector2(0f, 0f), new Vector2(1f, 0f));
        AddPlatform(1, new Vector2(2f, 0f));

        buildLayer.ExecuteLayer(configuration, managerObject, manager);

        MeshCollider[] colliders = managerObject.GetComponentsInChildren<MeshCollider>(true);
        Assert.That(colliders, Has.Length.EqualTo(1));
        Assert.That(colliders.All(item => Mathf.Abs(item.transform.localPosition.x - CellSize * 0.5f) < 0.001f), Is.True);
        Assert.That(colliders[0].transform.localPosition.z, Is.EqualTo(0f).Within(0.001f));
        Assert.That(colliders.All(item => Mathf.Abs(item.GetComponent<MeshRenderer>().bounds.size.x - CellSize * 2.4f) < 0.001f), Is.True);
    }

    [Test]
    public void ExecuteLayer_TwoLevel45DegreeRun_BuildsOneContinuousRampForTheWholeRise()
    {
        AddPlatform(0, new Vector2(-1f, 0f));
        AddSlope(new Vector2(0f, 0f), new Vector2(1f, 0f));
        AddPlatform(2, new Vector2(2f, 0f));

        buildLayer.ExecuteLayer(configuration, managerObject, manager);

        MeshCollider[] colliders = OrderedColliders();
        Assert.That(colliders, Has.Length.EqualTo(1));
        Assert.That(colliders.All(item => Mathf.Abs(item.transform.localPosition.x - CellSize * 0.5f) < 0.001f), Is.True);
        Assert.That(colliders.All(item => Mathf.Abs(item.GetComponent<MeshRenderer>().bounds.size.x - CellSize * 2.4f) < 0.001f), Is.True);
        Assert.That(colliders.All(item => Mathf.Abs(item.GetComponent<MeshRenderer>().bounds.size.y - CellSize * 2f) < 0.001f), Is.True);
        Assert.That(colliders.All(item => item.sharedMesh == item.GetComponent<MeshFilter>().sharedMesh), Is.True);

        Physics.SyncTransforms();
        MeshCollider collider = colliders[0];
        foreach (float localZ in new[] { -0.45f, -0.2f, 0f, 0.2f, 0.45f })
        {
            Ray ray = new Ray(collider.transform.TransformPoint(new Vector3(0f, 2f, localZ)), -collider.transform.up);
            Assert.That(collider.Raycast(ray, out RaycastHit hit, CellSize * 5f), Is.True, "No ramp surface at local z=" + localZ);
            Assert.That(
                collider.transform.InverseTransformPoint(hit.point).y,
                Is.EqualTo(localZ + 0.5f).Within(0.01f),
                "Ramp surface is discontinuous at local z=" + localZ);
        }
    }

    [Test]
    public void ExecuteLayer_TwoLevelShallowRun_BuildsOneContinuousRampForTheWholeRise()
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
        Assert.That(colliders, Has.Length.EqualTo(1));
        Assert.That(colliders.All(item => Mathf.Abs(item.transform.localPosition.x - CellSize * 1.5f) < 0.001f), Is.True);
        Assert.That(colliders.All(item => Mathf.Abs(item.GetComponent<MeshRenderer>().bounds.size.x - CellSize * 4.4f) < 0.001f), Is.True);
        Assert.That(colliders.All(item => Mathf.Abs(item.GetComponent<MeshRenderer>().bounds.size.y - CellSize * 2f) < 0.001f), Is.True);
    }

    [Test]
    public void ExecuteLayer_TwoCellsWide_BuildsEachSlopeRowOnce()
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
        Assert.That(colliders, Has.Length.EqualTo(2));
        Assert.That(colliders.All(item => Mathf.Abs(item.transform.localPosition.y - SurfaceBase) < 0.001f), Is.True);
        Assert.That(colliders.All(item => Mathf.Abs(item.GetComponent<MeshRenderer>().bounds.size.y - CellSize * 2f) < 0.001f), Is.True);
        Assert.That(colliders.All(item => Vector3.Dot(item.transform.forward, Vector3.right) > 0.999f), Is.True);
        Assert.That(colliders.Select(item => item.transform.localPosition.z), Is.EquivalentTo(new[] { 0f, CellSize }));
    }

    [Test]
    public void Resolve_OverlappingPlatforms_UsesTopLayerAndDerivesSlopeSupport()
    {
        AddPlatform(0, new Vector2(-1f, 0f), new Vector2(0f, 0f), new Vector2(1f, 0f));
        AddPlatform(1, new Vector2(-1f, 0f), new Vector2(1f, 0f));
        AddPlatform(3, new Vector2(2f, 0f));
        AddSlope(new Vector2(0f, 0f), new Vector2(1f, 0f));

        Dictionary<Vector2, int> topHeights = GetTopHeights();
        bool success = LdtkSlopeLayoutResolver.TryResolve(
            slopeLayer.allPositions.ToHashSet(),
            topHeights,
            PlatformEndExtension,
            MaximumSlopeAngle,
            out LdtkSlopeLayout layout,
            out string error);

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
            PlatformEndExtension,
            MaximumSlopeAngle,
            out _,
            out string error);

        Assert.That(success, Is.False);
        StringAssert.Contains("(0,0)", error);
        StringAssert.Contains("requires support at Plane_H0", error);
        StringAssert.Contains("top platform is Plane_H2", error);
    }

    [Test]
    public void Resolve_ArbitraryThreeByTwoRamp_InfersProportionalSupportHeights()
    {
        AddPlatform(0, new Vector2(-1f, 0f));
        AddSlope(new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(2f, 0f));
        AddPlatform(2, new Vector2(3f, 0f));

        bool success = LdtkSlopeLayoutResolver.TryResolve(
            slopeLayer.allPositions.ToHashSet(),
            GetTopHeights(),
            PlatformEndExtension,
            MaximumSlopeAngle,
            out LdtkSlopeLayout layout,
            out string error);

        Assert.That(success, Is.True, error);
        Assert.That(layout.ramps, Has.Count.EqualTo(1));
        Assert.That(layout.ramps[0].run, Is.EqualTo(3));
        Assert.That(layout.ramps[0].rise, Is.EqualTo(2));
        Assert.That(layout.requiredPlatformHeights[new Vector2(0f, 0f)], Is.EqualTo(0));
        Assert.That(layout.requiredPlatformHeights[new Vector2(1f, 0f)], Is.EqualTo(0));
        Assert.That(layout.requiredPlatformHeights[new Vector2(2f, 0f)], Is.EqualTo(1));
    }

    [Test]
    public void ExecuteLayer_OneByTwoRamp_UsesSameWedgeWhenUnitSlopeLimitAllowsIt()
    {
        AddPlatform(0, new Vector2(-1f, 0f));
        AddSlope(Vector2.zero);
        AddPlatform(2, new Vector2(1f, 0f));
        buildLayer.maximumSlopeAngle = 65f;

        buildLayer.ExecuteLayer(configuration, managerObject, manager);

        MeshCollider[] colliders = OrderedColliders();
        Assert.That(colliders, Has.Length.EqualTo(1));
        Assert.That(colliders.All(item => Mathf.Abs(item.GetComponent<MeshRenderer>().bounds.size.x - CellSize * 1.4f) < 0.001f), Is.True);
        Assert.That(colliders.All(item => Mathf.Abs(item.GetComponent<MeshRenderer>().bounds.size.y - CellSize * 2f) < 0.001f), Is.True);
    }

    [Test]
    public void ExecuteLayer_SlopeSteeperThanUnitLimit_ThrowsMeasuredAngleAndCells()
    {
        AddPlatform(0, new Vector2(-1f, 0f));
        AddSlope(Vector2.zero);
        AddPlatform(2, new Vector2(1f, 0f));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => buildLayer.ExecuteLayer(configuration, managerObject, manager));

        StringAssert.Contains("(0,0)", exception.Message);
        StringAssert.Contains("55.01 degree", exception.Message);
        StringAssert.Contains("45 degrees", exception.Message);
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
