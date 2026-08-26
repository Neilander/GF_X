using System.Reflection;
using AAAGame.MiniMap.FOG3;
using AAAGame.Tools.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class Fog3EnvironmentBackgroundTests
{
    [Test]
    public void GeneratedWaterMesh_IsSubdividedAtCellScale()
    {
        MethodInfo createMesh = typeof(LdtkToTileWorldCreatorImporterWindow).GetMethod(
            "CreateEnvironmentBackgroundMesh",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(createMesh, Is.Not.Null);

        Mesh mesh = (Mesh)createMesh.Invoke(null, new object[] { -64.5f, -64.5f, 150.5f, 140.5f, 1f });
        try
        {
            Assert.That(mesh.vertexCount, Is.EqualTo(216 * 206));
            Assert.That(mesh.triangles.Length / 3, Is.EqualTo(215 * 205 * 2));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void WaterMaterialWaveHeight_IsIncludedInFogCoverHeight()
    {
        Material waterMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/TileWorldCreator/Tiles URP/BaseBlockTiles/Materials/matBaseBlockTilesWater.mat");
        Assert.That(waterMaterial, Is.Not.Null);
        MethodInfo calculateHeight = typeof(LdtkToTileWorldCreatorImporterWindow).GetMethod(
            "CalculateMaximumWaveVerticalDisplacement",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(calculateHeight, Is.Not.Null);

        float maximumWaveHeight = (float)calculateHeight.Invoke(null, new object[] { waterMaterial });
        var backgroundObject = new GameObject("Water Background");
        LevelEnvironmentBackground background = backgroundObject.AddComponent<LevelEnvironmentBackground>();
        try
        {
            backgroundObject.transform.position = new Vector3(0f, -3.7f, 0f);
            background.SetMaximumVerticalDisplacement(maximumWaveHeight);

            Assert.That(maximumWaveHeight, Is.GreaterThan(1.5f));
            Assert.That(background.FogCoverWorldY, Is.EqualTo(-3.7f + maximumWaveHeight).Within(0.0001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(backgroundObject);
        }
    }

    [Test]
    public void ProjectionWithoutCollider_UsesConfiguredEnvironmentFogCoverHeight()
    {
        var viewObject = new GameObject(nameof(Fog3EnvironmentBackgroundTests));
        Fog3WorldOverlayView view = viewObject.AddComponent<Fog3WorldOverlayView>();
        var terrain = new Fog3TerrainInfo(
            1,
            1,
            1f,
            Vector3.zero,
            new[] { true },
            new[] { 0 },
            new[] { false },
            new Fog3SlopeCellInfo[1],
            0f,
            -3.7f,
            -2.1f,
            "Test");

        try
        {
            SetField(view, "settings", new Fog3ViewSettings());
            SetField(view, "heightSampleMask", (LayerMask)0);

            MethodInfo sample = typeof(Fog3WorldOverlayView).GetMethod(
                "SampleProjectionSourceHeight",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(sample, Is.Not.Null);
            float result = (float)sample.Invoke(view, new object[] { terrain, 0f, 0f });

            Assert.That(result, Is.EqualTo(-2.1f).Within(0.0001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(viewObject);
        }
    }

    [Test]
    public void Build_WithEnvironmentBackground_CoversOutsideCellsAndCreatesOuterMasks()
    {
        var viewObject = new GameObject(nameof(Fog3EnvironmentBackgroundTests));
        Fog3WorldOverlayView view = viewObject.AddComponent<Fog3WorldOverlayView>();
        var terrain = new Fog3TerrainInfo(
            1,
            1,
            1f,
            Vector3.zero,
            new[] { false },
            new[] { -1 },
            new[] { false },
            new Fog3SlopeCellInfo[1],
            0f,
            -3.7f,
            -2.1f,
            "Test");

        try
        {
            var settings = new Fog3ViewSettings
            {
                DrawOverSceneGeometry = false,
                OverlayAlwaysOnTopShader = AssetDatabase.LoadAssetAtPath<Shader>(
                    "Assets/AAAGame/Scripts/MiniMap/FOG3/View/Fog3OverlayAlwaysOnTop.shader"),
            };
            Assert.That(settings.OverlayAlwaysOnTopShader, Is.Not.Null);
            view.Build(terrain, settings, 1f, (LayerMask)0, Vector3.zero, 10f, 0.25f);

            Assert.That(view.FogTexture.GetPixel(0, 0), Is.EqualTo(settings.OutsideColor));
            Assert.That(view.OutsideMaskQuadCount, Is.EqualTo(4));
            MeshFilter northMask = view.transform.Find("FOG3_Outside_North").GetComponent<MeshFilter>();
            Assert.That(northMask.sharedMesh.bounds.center.y, Is.EqualTo(-2.1f + settings.SurfaceOffset).Within(0.0001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(viewObject);
        }
    }

    [Test]
    public void ExpandedEnvironmentWater_CanRevealPastLdtkBounds()
    {
        MethodInfo expand = typeof(Fog3TerrainDetector).GetMethod(
            "ExpandTerrainToEnvironmentBackground",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(expand, Is.Not.Null);

        object[] arguments =
        {
            new Bounds(new Vector3(0.5f, 0f, 0f), new Vector3(4f, 0f, 1f)),
            1f,
            2,
            1,
            new Vector3(-0.5f, 0f, -0.5f),
            new[] { true, false },
            new[] { 0, -1 },
            new[] { false, false },
            new Fog3SlopeCellInfo[2],
        };
        expand.Invoke(null, arguments);

        int width = (int)arguments[2];
        int height = (int)arguments[3];
        Vector3 origin = (Vector3)arguments[4];
        bool[] revealable = (bool[])arguments[5];
        int[] platformHeights = (int[])arguments[6];
        bool[] slopes = (bool[])arguments[7];
        Fog3SlopeCellInfo[] slopeCells = (Fog3SlopeCellInfo[])arguments[8];
        Assert.That(width, Is.EqualTo(4));
        Assert.That(height, Is.EqualTo(1));
        Assert.That(origin, Is.EqualTo(new Vector3(-1.5f, 0f, -0.5f)));
        Assert.That(revealable, Is.All.True);
        Assert.That(platformHeights, Is.EqualTo(new[] { -1, 0, -1, -1 }));

        var terrain = new Fog3TerrainInfo(
            width,
            height,
            1f,
            origin,
            revealable,
            platformHeights,
            slopes,
            slopeCells,
            0f,
            0f,
            0f,
            "Expanded water test");
        var map = new Fog3MapData(terrain);
        map.MarkVisible(0, 0);

        Assert.That(map.GetCellState(0, 0), Is.EqualTo(Fog3CellState.Visible));
        Assert.That(map.GetCellState(3, 0), Is.EqualTo(Fog3CellState.Hidden));
        Assert.That(map.GetCellState(4, 0), Is.EqualTo(Fog3CellState.Outside));
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(target, value);
    }
}
