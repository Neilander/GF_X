using AAAGame.MiniMap;
using GiantGrey.TileWorldCreator;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class MinimapEnvironmentBackgroundTests
{
    private static readonly Color GroundColor = new Color(0.44f, 0.44f, 0.44f, 1f);
    private static readonly Color WaterColor = new Color(0.09f, 0.13f, 0.19f, 1f);

    [Test]
    public void Lv2_BuildsCompleteEnvironmentWaterWithTerrainOffset()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AAAGame/Tilemap/Lv2.prefab");
        Assert.That(prefab, Is.Not.Null);
        TileWorldCreatorManager manager = prefab.GetComponent<TileWorldCreatorManager>();
        Assert.That(manager, Is.Not.Null);

        MinimapTerrainMapBuildResult result = MinimapTerrainMapBuilder.Build(
            manager,
            512,
            "Plane",
            "Water",
            GroundColor,
            WaterColor,
            Color.black,
            8,
            16f / 9f);
        try
        {
            Assert.That(manager.configuration.width, Is.EqualTo(82));
            Assert.That(manager.configuration.height, Is.EqualTo(60));
            Assert.That(result.GridWidth, Is.EqualTo(136));
            Assert.That(result.GridHeight, Is.EqualTo(76));
            Assert.That(result.Bounds.TerrainOffsetX, Is.EqualTo(27));
            Assert.That(result.Bounds.TerrainOffsetY, Is.EqualTo(8));
            Assert.That(result.Bounds.WorldMinX, Is.EqualTo(-27.5f).Within(0.001f));
            Assert.That(result.Bounds.WorldMaxX, Is.EqualTo(108.5f).Within(0.001f));
            Assert.That(result.Bounds.WorldMinZ, Is.EqualTo(-8.5f).Within(0.001f));
            Assert.That(result.Bounds.WorldMaxZ, Is.EqualTo(67.5f).Within(0.001f));
            Assert.That(result.Texture.width, Is.EqualTo(136));
            Assert.That(result.Texture.height, Is.EqualTo(76));
            AssertColor(result.Texture.GetPixel(0, 0), WaterColor);
            AssertColor(result.Texture.GetPixel(135, 75), WaterColor);
            Assert.That(result.GroundPaintedCount, Is.GreaterThan(0));
            Assert.That(result.WaterReady, Is.True);
            Assert.That(result.IsTerrainReady, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(result.Texture);
        }
    }

    [Test]
    public void EnvironmentBackground_DoesNotRequirePaintedWaterLayer()
    {
        Configuration configuration = ScriptableObject.CreateInstance<Configuration>();
        configuration.width = 2;
        configuration.height = 1;
        configuration.cellSize = 1f;
        BlueprintLayer groundLayer = ScriptableObject.CreateInstance<BlueprintLayer>();
        groundLayer.layerName = "Plane_H0";
        groundLayer.AddCells(new System.Collections.Generic.HashSet<Vector2> { Vector2.zero });
        configuration.blueprintLayerFolders.Add(new BlueprintLayerFolder("Root"));
        configuration.blueprintLayerFolders[0].blueprintLayers.Add(groundLayer);

        var root = new GameObject(nameof(EnvironmentBackground_DoesNotRequirePaintedWaterLayer));
        TileWorldCreatorManager manager = root.AddComponent<TileWorldCreatorManager>();
        manager.configuration = configuration;
        var backgroundObject = new GameObject("Environment Background");
        backgroundObject.transform.SetParent(root.transform, false);
        backgroundObject.AddComponent<LevelEnvironmentBackground>();
        Mesh mesh = CreateQuadMesh(-1.5f, -1.5f, 2.5f, 1.5f);
        backgroundObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        backgroundObject.AddComponent<MeshRenderer>();

        MinimapTerrainMapBuildResult result = null;
        try
        {
            result = MinimapTerrainMapBuilder.Build(
                manager,
                512,
                "Plane",
                "Water",
                GroundColor,
                WaterColor,
                Color.black,
                1);

            Assert.That(result.GridWidth, Is.EqualTo(4));
            Assert.That(result.GridHeight, Is.EqualTo(3));
            Assert.That(result.Bounds.TerrainOffsetX, Is.EqualTo(1));
            Assert.That(result.Bounds.TerrainOffsetY, Is.EqualTo(1));
            Assert.That(result.ExpectsWaterLayer, Is.False);
            Assert.That(result.WaterPaintedCount, Is.EqualTo(12));
            AssertColor(result.Texture.GetPixel(0, 0), WaterColor);
            AssertColor(result.Texture.GetPixel(1, 1), GroundColor);
            Assert.That(result.IsTerrainReady, Is.True);
        }
        finally
        {
            if (result?.Texture != null)
                Object.DestroyImmediate(result.Texture);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(groundLayer);
            Object.DestroyImmediate(configuration);
        }
    }

    private static Mesh CreateQuadMesh(float minX, float minZ, float maxX, float maxZ)
    {
        var mesh = new Mesh { name = "Minimap Environment Test Mesh" };
        mesh.vertices = new[]
        {
            new Vector3(minX, 0f, minZ),
            new Vector3(maxX, 0f, minZ),
            new Vector3(minX, 0f, maxZ),
            new Vector3(maxX, 0f, maxZ),
        };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AssertColor(Color actual, Color expected)
    {
        const float tolerance = 1f / 255f;
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance));
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance));
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance));
        Assert.That(actual.a, Is.EqualTo(expected.a).Within(tolerance));
    }
}
