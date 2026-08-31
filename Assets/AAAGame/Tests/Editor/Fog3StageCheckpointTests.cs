using AAAGame.MiniMap.FOG3;
using NUnit.Framework;
using AAAGame.Card;
using System.Reflection;
using UnityEngine;
using UnityGameFramework.Runtime;

public sealed class Fog3StageCheckpointTests
{
    [Test]
    public void ViewSettings_HiddenFogIsFullyOpaqueByDefault()
    {
        Assert.AreEqual(1f, new Fog3ViewSettings().HiddenColor.a);
    }

    [Test]
    public void LogicGrid_UsesFixedPointOneWorldUnitCells()
    {
        Fix64 logicCellSize = (Fix64)0.1f;
        Fog3MapData coarseTerrainMap = new Fog3MapData(CreateFlatTerrain(8, 4, 1f), logicCellSize);
        Fog3MapData fineTerrainMap = new Fog3MapData(CreateFlatTerrain(16, 8, 0.5f), logicCellSize);
        Fog3MapData scaledWorldMap = new Fog3MapData(CreateFlatTerrain(8, 4, 0.2f), logicCellSize);

        Assert.AreEqual(80, coarseTerrainMap.Width);
        Assert.AreEqual(40, coarseTerrainMap.Height);
        Assert.AreEqual(coarseTerrainMap.Width, fineTerrainMap.Width);
        Assert.AreEqual(coarseTerrainMap.Height, fineTerrainMap.Height);
        Assert.AreEqual((float)logicCellSize, coarseTerrainMap.CellSizeX, 0.000001f);
        Assert.AreEqual((float)logicCellSize, coarseTerrainMap.CellSizeY, 0.000001f);
        Assert.AreEqual(16, scaledWorldMap.Width);
        Assert.AreEqual(8, scaledWorldMap.Height);
        Assert.AreEqual(1.6f, scaledWorldMap.Bounds.size.x, 0.000001f);
        Assert.AreEqual(0.8f, scaledWorldMap.Bounds.size.z, 0.000001f);
    }

    [Test]
    public void LogicGrid_DirtyBoundsTrackOnlyChangedCellsAndClearAfterPublish()
    {
        Fog3MapData map = new Fog3MapData(CreateFlatTerrain(8, 4, 1f), (Fix64)0.1f);

        Assert.IsFalse(map.TryGetDirtyBounds(out _, out _, out _, out _));
        map.MarkExplored(2, 3);
        Assert.IsTrue(map.TryGetDirtyBounds(out int minimumX, out int minimumY, out int maximumX, out int maximumY));
        Assert.AreEqual(2, minimumX);
        Assert.AreEqual(3, minimumY);
        Assert.AreEqual(2, maximumX);
        Assert.AreEqual(3, maximumY);

        map.MarkClean();

        Assert.IsFalse(map.TryGetDirtyBounds(out _, out _, out _, out _));
    }

    [Test]
    public void WorldOverlay_TextureUsesLogicGridResolutionInsteadOfTerrainGridResolution()
    {
        GameObject viewObject = new GameObject("Fog3IndependentLogicGridView");
        try
        {
            Fog3TerrainInfo terrain = CreateFlatTerrain(8, 4, 1f);
            Fog3MapData map = new Fog3MapData(terrain, (Fix64)0.1f);
            var settings = new Fog3ViewSettings
            {
                SurfaceMode = Fog3OverlaySurfaceMode.FlatWorldPlane,
                OutsideMaskPadding = 0f,
                OverlayAlwaysOnTopShader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop"),
            };
            Fog3WorldOverlayView view = viewObject.AddComponent<Fog3WorldOverlayView>();

            view.Build(terrain, map, settings, 0f, Physics.DefaultRaycastLayers, Vector3.zero, 1f, 2f);

            Assert.AreEqual(80, view.FogTexture.width);
            Assert.AreEqual(40, view.FogTexture.height);
            Assert.IsFalse(view.FogMaterial.HasProperty("_FogCellSize"));
            Assert.AreEqual(8f, view.FogMeshBounds.size.x, 0.000001f);
            Assert.AreEqual(4f, view.FogMeshBounds.size.z, 0.000001f);
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
        }
    }

    [Test]
    public void WorldOverlay_ChangesLogicalVisibilityImmediatelyAndFadesAlphaTowardTargets()
    {
        GameObject viewObject = new GameObject("Fog3VisibilityFadeTestView");
        try
        {
            var terrain = new Fog3TerrainInfo(
                1,
                1,
                1f,
                Vector3.zero,
                new[] { true },
                "VisibilityFadeTest");
            var settings = new Fog3ViewSettings
            {
                SurfaceMode = Fog3OverlaySurfaceMode.FlatWorldPlane,
                OutsideMaskPadding = 0f,
                HiddenColor = new Color(0f, 0f, 0f, 1f),
                ExploredColor = new Color(0f, 0f, 0f, 0.55f),
                VisibleColor = new Color(0f, 0f, 0f, 0f),
                OverlayAlwaysOnTopShader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop"),
            };
            Fog3WorldOverlayView view = viewObject.AddComponent<Fog3WorldOverlayView>();
            var map = new Fog3MapData(terrain, (Fix64)0.1f);
            view.Build(terrain, map, settings, 0f, Physics.DefaultRaycastLayers, Vector3.zero, 0.5f, 0.25f);
            Assert.AreEqual(0.25f, view.VisibilityBoundaryFadeDistance);
            Assert.AreEqual(0.25f, view.FogMaterial.GetFloat("_FogBoundaryFadeDistance"));
            view.Render(map, false);

            map.MarkExplored(0, 0);
            map.MarkVisible(0, 0);
            view.Render(map, false);
            Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(0, 0));
            Assert.AreEqual(1f, view.GetCurrentAlphaForDiagnostics(0, 0), 1f / 255f);

            Assert.IsTrue(view.AdvanceVisibilityFade(0.5f));
            Assert.AreEqual(0.75f, view.GetCurrentAlphaForDiagnostics(0, 0), 1f / 255f);
            Assert.IsTrue(view.AdvanceVisibilityFade(1.5f));
            Assert.AreEqual(0f, view.GetCurrentAlphaForDiagnostics(0, 0), 1f / 255f);

            map.ClearCurrentVisibility();
            view.Render(map, false);
            Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(0, 0));
            Assert.AreEqual(0f, view.GetCurrentAlphaForDiagnostics(0, 0), 1f / 255f);

            Assert.IsTrue(view.AdvanceVisibilityFade(0.5f));
            Assert.AreEqual(0.25f, view.GetCurrentAlphaForDiagnostics(0, 0), 1f / 255f);
            Assert.IsTrue(view.AdvanceVisibilityFade(0.6f));
            Assert.AreEqual(0.55f, view.GetCurrentAlphaForDiagnostics(0, 0), 1f / 255f);
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
        }
    }

    [Test]
    public void TimedTransition_PreservesSubFrameStartTimesInPresentationTexture()
    {
        Shader shader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop");
        Assert.IsNotNull(shader);

        const int sourceWidth = 3;
        const int renderedPixelsPerCell = 64;
        Texture2D source = new Texture2D(sourceWidth, 1, TextureFormat.RGBAFloat, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        float now = Time.time;
        source.SetPixels(new[]
        {
            new Color(0f, (byte)Fog3CellState.Visible / 255f, 1f, now - 0.01f),
            new Color(0f, (byte)Fog3CellState.Visible / 255f, 1f, now - 0.025f),
            new Color(0f, (byte)Fog3CellState.Visible / 255f, 1f, now - 0.04f),
        });
        source.Apply(false);

        RenderTexture target = RenderTexture.GetTemporary(
            sourceWidth * renderedPixelsPerCell,
            8,
            0,
            RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear);
        Material material = new Material(shader);
        Texture2D readback = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
        RenderTexture previous = RenderTexture.active;
        try
        {
            material.SetFloat("_FogBoundaryFadeDistance", 0.25f);
            material.SetFloat("_FogFadeSpeed", 1f);
            material.SetFloat("_FogUsesTimedTransitions", 1f);
            material.SetColor("_FogVisibleColor", Color.white);

            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            Graphics.Blit(source, target, material);
            readback.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
            readback.Apply(false);

            float newestAlpha = readback.GetPixel(renderedPixelsPerCell / 2, 4).r;
            float middleAlpha = readback.GetPixel(renderedPixelsPerCell + renderedPixelsPerCell / 2, 4).r;
            float oldestAlpha = readback.GetPixel(renderedPixelsPerCell * 2 + renderedPixelsPerCell / 2, 4).r;
            Assert.Greater(newestAlpha - middleAlpha, 0.008f, "The first two sub-frame start times collapsed to one GPU value.");
            Assert.Greater(middleAlpha - oldestAlpha, 0.008f, "The last two sub-frame start times collapsed to one GPU value.");
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(readback);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(source);
        }
    }

    [Test]
    public void TimedTransition_PresentationSnapshotUsesOneClockForStateAndSpatialChanges()
    {
        GameObject viewObject = new GameObject("Fog3SubFramePresentationTimeView");
        try
        {
            Fog3TerrainInfo terrain = CreateFlatTerrain(1, 1, 1f);
            var map = new Fog3MapData(terrain, (Fix64)0.1f);
            var settings = new Fog3ViewSettings
            {
                PresentationResolution = 10,
                SurfaceMode = Fog3OverlaySurfaceMode.FlatWorldPlane,
                OutsideMaskPadding = 0f,
                OverlayAlwaysOnTopShader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop"),
            };
            Fog3WorldOverlayView view = viewObject.AddComponent<Fog3WorldOverlayView>();
            view.Build(terrain, map, settings, 0f, Physics.DefaultRaycastLayers, Vector3.zero, 1f, 0.1f);

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            float now = Time.time;
            typeof(Fog3WorldOverlayView).GetField("previousVisibilityResolutionLogicTime", flags)
                .SetValue(view, 1d);
            typeof(Fog3WorldOverlayView).GetField("previousVisibilityPresentationTime", flags)
                .SetValue(view, now - 1f);
            typeof(Fog3WorldOverlayView).GetField("hasPreviousVisibilityPresentationTime", flags)
                .SetValue(view, true);
            Color[] transitions = (Color[])typeof(Fog3WorldOverlayView)
                .GetField("presentationTransitions", flags)
                .GetValue(view);
            transitions[0].a = now - 1f;

            map.ChangeVisibilityCoverage(new Fog3VisibilityRowInterval(0, 0, 0), 1);
            map.RecordVisibilityChangeLogicTimeCandidate(0, 0, true, 1.25d);
            map.ResolveVisibilityCoverageChanges(2d);
            view.Render(map, false);

            Assert.AreEqual(Time.time, transitions[0].a, 0.02f);
            Color previousTransition = transitions[0];
            float[] logicStartAlphas = (float[])typeof(Fog3WorldOverlayView)
                .GetField("transitionStartAlphas", flags)
                .GetValue(view);
            float[] logicStartTimes = (float[])typeof(Fog3WorldOverlayView)
                .GetField("transitionStartTimes", flags)
                .GetValue(view);

            float[] spatialTargets = (float[])typeof(Fog3WorldOverlayView)
                .GetField("presentationSpatialTargets", flags)
                .GetValue(view);
            spatialTargets[0] = 0.42f;
            logicStartAlphas[0] = 0.91f;
            logicStartTimes[0] = previousTransition.a + 0.1f;

            float fadeSpeed = (float)typeof(Fog3WorldOverlayView)
                .GetField("visibilityFadeSpeed", flags)
                .GetValue(view);
            float expectedStartAlpha = Mathf.MoveTowards(
                previousTransition.b,
                previousTransition.r,
                fadeSpeed * Mathf.Max(0f, Time.time - previousTransition.a));

            typeof(Fog3WorldOverlayView).GetMethod("RefreshPresentationRegion", flags)
                .Invoke(view, new object[] { 0, 0, 0, 0 });

            Assert.AreEqual(0.42f, transitions[0].r, 0.000001f);
            Assert.Greater(Mathf.Abs(logicStartAlphas[0] - transitions[0].b), 0.000001f);
            Assert.AreEqual(expectedStartAlpha, transitions[0].b, 0.000001f);
            Assert.AreEqual(Time.time, transitions[0].a, 0.02f);
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
        }
    }

    [Test]
    public void TimedTransition_SameDirectionSpatialChangeRebasesAtPresentationTime()
    {
        GameObject viewObject = new GameObject("Fog3ActiveSpatialTrajectoryView");
        try
        {
            Fog3TerrainInfo terrain = CreateFlatTerrain(1, 1, 1f);
            var map = new Fog3MapData(terrain, (Fix64)0.1f);
            var settings = new Fog3ViewSettings
            {
                PresentationResolution = 10,
                SurfaceMode = Fog3OverlaySurfaceMode.FlatWorldPlane,
                OutsideMaskPadding = 0f,
                OverlayAlwaysOnTopShader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop"),
            };
            Fog3WorldOverlayView view = viewObject.AddComponent<Fog3WorldOverlayView>();
            view.Build(terrain, map, settings, 0f, Physics.DefaultRaycastLayers, Vector3.zero, 1f, 0.1f);

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Color[] transitions = (Color[])typeof(Fog3WorldOverlayView)
                .GetField("presentationTransitions", flags)
                .GetValue(view);
            float[] spatialTargets = (float[])typeof(Fog3WorldOverlayView)
                .GetField("presentationSpatialTargets", flags)
                .GetValue(view);
            float startTime = Time.time - 0.1f;
            transitions[0] = new Color(
                0.4f,
                (byte)Fog3CellState.Hidden / 255f,
                0.8f,
                startTime);
            spatialTargets[0] = 0.2f;

            typeof(Fog3WorldOverlayView).GetMethod("RefreshPresentationRegion", flags)
                .Invoke(view, new object[] { 0, 0, 0, 0 });

            float expectedStartAlpha = Mathf.MoveTowards(
                0.8f,
                0.4f,
                view.VisibilityFadeSpeed * 0.1f);
            Assert.AreEqual(0.2f, transitions[0].r, 0.000001f);
            Assert.AreEqual(expectedStartAlpha, transitions[0].b, 0.000001f);
            Assert.AreEqual(Time.time, transitions[0].a, 0.02f);
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
        }
    }

    [Test]
    public void SpatialPresentation_InterpolatesCurrentContinuouslyWithoutCenterPlateaus()
    {
        Shader shader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop");
        Assert.IsNotNull(shader);

        const int sourceWidth = 8;
        const int renderedPixelsPerCell = 64;
        const int renderWidth = sourceWidth * renderedPixelsPerCell;
        float now = Time.time;
        Texture2D source = new Texture2D(sourceWidth, 1, TextureFormat.RGBAFloat, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        Color[] sourcePixels = new Color[sourceWidth];
        for (int x = 0; x < sourceWidth; x++)
        {
            float targetAlpha = 0.1f + x * 0.08f;
            float currentAlpha = targetAlpha + 0.15f;
            sourcePixels[x] = new Color(
                targetAlpha,
                (byte)Fog3CellState.Visible / 255f,
                currentAlpha,
                now);
        }

        source.SetPixels(sourcePixels);
        source.Apply(false);
        RenderTexture target = RenderTexture.GetTemporary(
            renderWidth,
            8,
            0,
            RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear);
        Material material = new Material(shader);
        Texture2D readback = new Texture2D(renderWidth, 8, TextureFormat.RGBAFloat, false, true);
        RenderTexture previous = RenderTexture.active;
        try
        {
            material.SetFloat("_FogFadeSpeed", 0f);
            material.SetFloat("_FogUsesTimedTransitions", 1f);
            material.SetColor("_FogVisibleColor", Color.white);

            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            Graphics.Blit(source, target, material);
            readback.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
            readback.Apply(false);

            int sampleStart = renderedPixelsPerCell;
            int sampleEnd = renderWidth - renderedPixelsPerCell;
            int longestRun = 1;
            int longestRunStart = sampleStart;
            int longestRunEnd = sampleStart;
            int currentRun = 1;
            float previousAlpha = readback.GetPixel(sampleStart, 4).r;
            for (int x = sampleStart + 1; x < sampleEnd; x++)
            {
                float alpha = readback.GetPixel(x, 4).r;
                if (Mathf.Abs(alpha - previousAlpha) <= 0.0001f)
                    currentRun++;
                else
                    currentRun = 1;

                if (currentRun > longestRun)
                {
                    longestRun = currentRun;
                    longestRunStart = x - currentRun + 1;
                    longestRunEnd = x;
                }

                previousAlpha = alpha;
            }

            Assert.LessOrEqual(
                longestRun,
                4,
                $"Spatial presentation must not fall back to the discrete center current alpha inside each presentation texel. " +
                $"Longest run [{longestRunStart}, {longestRunEnd}] alpha=" +
                $"{readback.GetPixel(longestRunStart, 4).r:R}.");
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(readback);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(source);
        }
    }

    [Test]
    public void SpatialPresentation_StaticBoundaryFadesByWorldDistance()
    {
        Shader shader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop");
        Assert.IsNotNull(shader);

        const int sourceWidth = 64;
        const int renderedPixelsPerCell = 8;
        const int renderWidth = sourceWidth * renderedPixelsPerCell;
        Texture2D source = new Texture2D(sourceWidth, 1, TextureFormat.RGBAFloat, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        Color[] sourcePixels = new Color[sourceWidth];
        for (int x = 0; x < sourceWidth; x++)
        {
            float alpha = x < sourceWidth / 2 ? 1f : 0f;
            sourcePixels[x] = new Color(alpha, (byte)Fog3CellState.Visible / 255f, alpha, Time.time);
        }

        source.SetPixels(sourcePixels);
        source.Apply(false);
        RenderTexture target = RenderTexture.GetTemporary(
            renderWidth,
            8,
            0,
            RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear);
        Material material = new Material(shader);
        Texture2D readback = new Texture2D(renderWidth, 8, TextureFormat.RGBAFloat, false, true);
        RenderTexture previous = RenderTexture.active;
        try
        {
            material.SetFloat("_FogFadeSpeed", 0f);
            material.SetFloat("_FogUsesTimedTransitions", 1f);
            material.SetFloat("_FogBoundaryFadeDistance", 2f);
            material.SetVector("_FogWorldSize", new Vector4(sourceWidth * 0.1f, 1f, 0f, 0f));
            material.SetColor("_FogHiddenColor", Color.white);
            material.SetColor("_FogVisibleColor", Color.white);

            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            Graphics.Blit(source, target, material);
            readback.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
            readback.Apply(false);

            int boundary = sourceWidth / 2 * renderedPixelsPerCell;
            // The shader writes alpha, but the transparent blend state also
            // blends the destination alpha.  The white red channel therefore
            // exposes the unmultiplied fog alpha for this isolated readback.
            float darkSide = readback.GetPixel(boundary - 16, 4).r;
            float nearBoundary = readback.GetPixel(boundary + 8, 4).r;
            float middle = readback.GetPixel(boundary + 80, 4).r;
            float outside = readback.GetPixel(boundary + 176, 4).r;
            Assert.Greater(darkSide, 0.99f, $"The darker side of a static boundary must stay opaque. probes={darkSide:R},{nearBoundary:R},{middle:R},{outside:R}");
            Assert.Greater(nearBoundary, middle, $"Static spatial fade must decrease away from the boundary. probes={darkSide:R},{nearBoundary:R},{middle:R},{outside:R}");
            Assert.That(middle, Is.InRange(0.3f, 0.7f), $"The middle of a two-world-unit fade must not be a constant plateau. probes={darkSide:R},{nearBoundary:R},{middle:R},{outside:R}");
            Assert.Less(outside, 0.05f, $"The transparent side must reach its unmodified alpha after the configured distance. probes={darkSide:R},{nearBoundary:R},{middle:R},{outside:R}");

            float previousAlpha = nearBoundary;
            for (int x = boundary + 9; x <= boundary + 160; x++)
            {
                float alpha = readback.GetPixel(x, 4).r;
                Assert.LessOrEqual(alpha, previousAlpha + 0.015f, $"Static spatial fade is not monotonic at output pixel {x}.");
                previousAlpha = alpha;
            }
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(readback);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(source);
        }
    }

    [Test]
    public void WorldOverlay_AcceptsTwoCellBoundaryFadeDistance()
    {
        GameObject viewObject = new GameObject("Fog3TwoCellBoundaryFadeTestView");
        try
        {
            var terrain = new Fog3TerrainInfo(1, 1, 1f, Vector3.zero, new[] { true }, "TwoCellBoundaryFadeTest");
            var settings = new Fog3ViewSettings
            {
                SurfaceMode = Fog3OverlaySurfaceMode.FlatWorldPlane,
                OutsideMaskPadding = 0f,
                OverlayAlwaysOnTopShader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop"),
            };
            Fog3WorldOverlayView view = viewObject.AddComponent<Fog3WorldOverlayView>();
            view.Build(terrain, new Fog3MapData(terrain, (Fix64)0.1f), settings, 0f, Physics.DefaultRaycastLayers, Vector3.zero, 1f, 2f);

            Assert.AreEqual(2f, view.VisibilityBoundaryFadeDistance);
            Assert.AreEqual(2f, view.FogMaterial.GetFloat("_FogBoundaryFadeDistance"));
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
        }
    }

    [Test]
    public void TerrainConformingMesh_SeparatesHighAndLowCellTopsAndDepthTestsTheCliffWall()
    {
        GameObject lowGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject highGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject viewObject = new GameObject("Fog3ConformingCliffTestView");
        try
        {
            lowGround.transform.position = new Vector3(0.5f, -0.5f, 0.5f);
            lowGround.transform.localScale = Vector3.one;
            highGround.transform.position = new Vector3(1.5f, 0.5f, 0.5f);
            highGround.transform.localScale = Vector3.one;
            Physics.SyncTransforms();

            var terrain = new Fog3TerrainInfo(
                2,
                1,
                1f,
                Vector3.zero,
                new[] { true, true },
                new[] { 0, 1 },
                null,
                null,
                0.2f,
                "ConformingCliffTest");
            Fog3WorldOverlayView view = BuildTerrainConformingView(viewObject, terrain);

            Mesh mesh = viewObject.transform.Find("FOG3_WorldOverlay").GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Assert.AreEqual(12, vertices.Length, "Two independent tops and one depth-tested cliff wall are required.");
            Assert.AreEqual(18, mesh.triangles.Length);
            Assert.AreEqual(0.08f, vertices[2].y, 0.001f);
            Assert.AreEqual(1.08f, vertices[4].y, 0.001f);
            int[] triangles = mesh.triangles;
            for (int i = 0; i < 6; i++)
                Assert.Less(triangles[i], 4);
            for (int i = 6; i < 12; i++)
                Assert.GreaterOrEqual(triangles[i], 4);
            Assert.AreEqual(vertices[4].x, vertices[8].x, 0.001f);
            Assert.AreEqual(vertices[2].x, vertices[10].x, 0.001f);
            Assert.Greater(vertices[8].x - vertices[10].x, 0.001f, "The cliff wall must bridge the inset gap between both cell tops.");
            Assert.AreEqual(vertices[4].z, vertices[10].z, 0.001f);
            Assert.AreEqual(vertices[2].y, vertices[10].y, 0.001f);
            Assert.AreEqual(
                (int)UnityEngine.Rendering.CompareFunction.LessEqual,
                view.FogMaterial.GetInt("_ZTest"));
            Assert.IsFalse(view.FogMeshUsesCameraProjectionGrid);
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
            Object.DestroyImmediate(highGround);
            Object.DestroyImmediate(lowGround);
        }
    }

    [Test]
    public void TerrainConformingMesh_PlacesEveryCliffWallOnSharedHeightBoundary()
    {
        GameObject lowGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject highGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject viewObject = new GameObject("Fog3ConformingCliffWindingTestView");
        try
        {
            lowGround.transform.position = new Vector3(1.5f, -0.5f, 1.5f);
            lowGround.transform.localScale = new Vector3(3f, 1f, 3f);
            highGround.transform.position = new Vector3(1.5f, 0.5f, 1.5f);
            highGround.transform.localScale = Vector3.one;
            Physics.SyncTransforms();

            var terrain = new Fog3TerrainInfo(
                3,
                3,
                1f,
                Vector3.zero,
                new[] { true, true, true, true, true, true, true, true, true },
                new[] { 0, 0, 0, 0, 1, 0, 0, 0, 0 },
                null,
                null,
                "ConformingCliffWindingTest");
            BuildTerrainConformingView(viewObject, terrain);

            Mesh mesh = viewObject.transform.Find("FOG3_WorldOverlay").GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            var wallCenters = new System.Collections.Generic.List<Vector3>();
            const int topTriangleIndexCount = 3 * 3 * 6;
            for (int i = topTriangleIndexCount; i < triangles.Length; i += 6)
            {
                int firstWallVertex = triangles[i] - triangles[i] % 4;
                wallCenters.Add(
                    (vertices[firstWallVertex] +
                     vertices[firstWallVertex + 1] +
                     vertices[firstWallVertex + 2] +
                     vertices[firstWallVertex + 3]) * 0.25f);
            }

            Assert.AreEqual(4, wallCenters.Count);
            Assert.IsTrue(wallCenters.Exists(center => Mathf.Abs(center.x - 1f) < 0.001f && Mathf.Abs(center.z - 1.5f) < 0.001f));
            Assert.IsTrue(wallCenters.Exists(center => Mathf.Abs(center.x - 2f) < 0.001f && Mathf.Abs(center.z - 1.5f) < 0.001f));
            Assert.IsTrue(wallCenters.Exists(center => Mathf.Abs(center.x - 1.5f) < 0.001f && Mathf.Abs(center.z - 1f) < 0.001f));
            Assert.IsTrue(wallCenters.Exists(center => Mathf.Abs(center.x - 1.5f) < 0.001f && Mathf.Abs(center.z - 2f) < 0.001f));
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
            Object.DestroyImmediate(highGround);
            Object.DestroyImmediate(lowGround);
        }
    }

    [Test]
    public void TerrainConformingMesh_ClosesJunctionBetweenVoidAndInsetLowCliffWalls()
    {
        GameObject baseGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject lowGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject highGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject viewObject = new GameObject("Fog3ConformingCliffJunctionTestView");
        try
        {
            baseGround.transform.position = new Vector3(1f, -0.5f, 1f);
            baseGround.transform.localScale = new Vector3(2f, 1f, 2f);
            lowGround.transform.position = new Vector3(1.5f, 0.5f, 0.5f);
            lowGround.transform.localScale = Vector3.one;
            highGround.transform.position = new Vector3(1f, 1.5f, 1.5f);
            highGround.transform.localScale = new Vector3(2f, 1f, 1f);
            Physics.SyncTransforms();

            var terrain = new Fog3TerrainInfo(
                2,
                2,
                1f,
                Vector3.zero,
                new[] { false, true, true, true },
                new[] { -1, 1, 2, 2 },
                null,
                null,
                0.2f,
                "ConformingCliffJunctionTest");
            BuildTerrainConformingView(viewObject, terrain);

            Mesh mesh = viewObject.transform.Find("FOG3_WorldOverlay").GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Assert.AreEqual(31, vertices.Length, "Four tops, three cliff walls, and one junction triangle are required.");
            Assert.AreEqual(45, triangles.Length);
            Assert.AreEqual(new Vector3(1f, 2.08f, 1.2f), vertices[28]);
            Assert.AreEqual(new Vector3(1f, 0.08f, 1f), vertices[29]);
            Assert.AreEqual(new Vector3(1.2f, 1.08f, 0.8f), vertices[30]);
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
            Object.DestroyImmediate(highGround);
            Object.DestroyImmediate(lowGround);
            Object.DestroyImmediate(baseGround);
        }
    }

    [Test]
    public void TerrainConformingMesh_ShrinksFlatPlatformOuterEdgesByConfiguredInset()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject viewObject = new GameObject("Fog3ConformingInsetTestView");
        try
        {
            ground.transform.position = new Vector3(1f, -0.5f, 0.5f);
            ground.transform.localScale = new Vector3(2f, 1f, 1f);
            Physics.SyncTransforms();

            var terrain = new Fog3TerrainInfo(
                2,
                1,
                1f,
                Vector3.zero,
                new[] { true, true },
                new[] { 0, 0 },
                null,
                null,
                0.2f,
                "ConformingInsetTest");
            BuildTerrainConformingView(viewObject, terrain);

            Vector3[] vertices = viewObject.transform.Find("FOG3_WorldOverlay").GetComponent<MeshFilter>().sharedMesh.vertices;
            Assert.AreEqual(8, vertices.Length);
            Assert.AreEqual(0.2f, vertices[0].x, 0.001f);
            Assert.AreEqual(1f, vertices[2].x, 0.001f);
            Assert.AreEqual(1f, vertices[4].x, 0.001f);
            Assert.AreEqual(1.8f, vertices[6].x, 0.001f);
            Assert.AreEqual(0.2f, vertices[0].z, 0.001f);
            Assert.AreEqual(0.8f, vertices[1].z, 0.001f);
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
            Object.DestroyImmediate(ground);
        }
    }

    [Test]
    public void TerrainConformingMesh_ExtendsOnlyContinuousSlopeComponentEndpoints()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject viewObject = new GameObject("Fog3ConformingSlopeInsetTestView");
        try
        {
            ground.transform.position = new Vector3(1f, -0.5f, 0.5f);
            ground.transform.localScale = new Vector3(2.4f, 1f, 1f);
            Physics.SyncTransforms();

            var slopeCells = new[]
            {
                new Fog3SlopeCellInfo(1, 0, 0, 2, 1),
                new Fog3SlopeCellInfo(1, 0, 1, 2, 1),
            };
            var terrain = new Fog3TerrainInfo(
                2,
                1,
                1f,
                Vector3.zero,
                new[] { true, true },
                new[] { 0, 0 },
                new[] { true, true },
                slopeCells,
                0.2f,
                "ConformingSlopeInsetTest");
            BuildTerrainConformingView(viewObject, terrain);

            Vector3[] vertices = viewObject.transform.Find("FOG3_WorldOverlay").GetComponent<MeshFilter>().sharedMesh.vertices;
            Assert.AreEqual(-0.2f, vertices[0].x, 0.001f);
            Assert.AreEqual(1f, vertices[2].x, 0.001f);
            Assert.AreEqual(1f, vertices[4].x, 0.001f);
            Assert.AreEqual(2.2f, vertices[6].x, 0.001f);
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
            Object.DestroyImmediate(ground);
        }
    }

    [Test]
    public void ProjectedCloudMesh_AlignsDifferentTerrainHeightsInScreenSpace()
    {
        Camera[] existingCameras = Object.FindObjectsOfType<Camera>();
        var existingCameraStates = new bool[existingCameras.Length];
        for (int i = 0; i < existingCameras.Length; i++)
        {
            existingCameraStates[i] = existingCameras[i].enabled;
            existingCameras[i].enabled = false;
        }

        GameObject cameraObject = new GameObject("Fog3ProjectionTestCamera");
        GameObject lowGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject highGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject entityRoot = new GameObject("Fog3ProjectionEntityBlocker");
        GameObject entityCollider = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject viewObject = new GameObject("Fog3ProjectionTestView");
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.transform.position = new Vector3(4f, 8f, -4f);
            camera.transform.LookAt(new Vector3(1f, 1f, 0.5f));

            lowGround.transform.position = new Vector3(0.5f, -0.5f, 0.5f);
            lowGround.transform.localScale = new Vector3(0.99f, 1f, 0.99f);
            lowGround.layer = LayerMask.NameToLayer("Ground");
            highGround.transform.position = new Vector3(1.5f, 1.5f, 0.5f);
            highGround.transform.localScale = new Vector3(0.99f, 1f, 0.99f);
            highGround.layer = LayerMask.NameToLayer("Ground");
            entityRoot.AddComponent<ProjectionOccluderEntity>();
            entityCollider.transform.SetParent(entityRoot.transform);
            entityCollider.transform.position = new Vector3(0.5f, 2f, 0.5f);
            entityCollider.transform.localScale = new Vector3(0.5f, 4f, 0.5f);
            Physics.SyncTransforms();

            var terrain = new Fog3TerrainInfo(
                2,
                1,
                1f,
                Vector3.zero,
                new[] { true, true },
                "ProjectedCloudMeshTest");
            var settings = new Fog3ViewSettings
            {
                SurfaceMode = Fog3OverlaySurfaceMode.CloudLayer,
                ProjectCloudLayerToCameraView = true,
                OutsideMaskPadding = 0f,
                DrawOverSceneGeometry = true,
                OverlayAlwaysOnTopShader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop"),
            };
            Fog3WorldOverlayView view = viewObject.AddComponent<Fog3WorldOverlayView>();
            view.Build(terrain, new Fog3MapData(terrain, (Fix64)0.1f), settings, 5f, LayerMask.GetMask("Ground"), Vector3.zero, 1f, 0.25f);

            Mesh mesh = viewObject.transform.Find("FOG3_WorldOverlay").GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Assert.IsTrue(view.FogMeshUsesCameraProjectionGrid);
            Assert.AreEqual(8, vertices.Length);
            Assert.Less(
                Vector2.Distance(
                    camera.WorldToScreenPoint(view.transform.TransformPoint(vertices[0])),
                    camera.WorldToScreenPoint(new Vector3(0f, 0f, 0f))),
                0.001f);
            Assert.Less(
                Vector2.Distance(
                    camera.WorldToScreenPoint(view.transform.TransformPoint(vertices[2])),
                    camera.WorldToScreenPoint(new Vector3(1f, 0f, 0f))),
                0.001f);
            Assert.Less(
                Vector2.Distance(
                    camera.WorldToScreenPoint(view.transform.TransformPoint(vertices[4])),
                    camera.WorldToScreenPoint(new Vector3(1f, 2f, 0f))),
                0.001f);
            Assert.Less(
                Vector2.Distance(
                    camera.WorldToScreenPoint(view.transform.TransformPoint(vertices[6])),
                    camera.WorldToScreenPoint(new Vector3(2f, 2f, 0f))),
                0.001f);

        }
        finally
        {
            Object.DestroyImmediate(viewObject);
            Object.DestroyImmediate(entityRoot);
            Object.DestroyImmediate(highGround);
            Object.DestroyImmediate(lowGround);
            Object.DestroyImmediate(cameraObject);
            for (int i = 0; i < existingCameras.Length; i++)
                existingCameras[i].enabled = existingCameraStates[i];
        }
    }

    private sealed class ProjectionOccluderEntity : EntityLogic
    {
    }

    [Test]
    public void PerformanceDiagnostics_AreDisabledByDefault()
    {
        GameObject managerObject = new GameObject("Fog3PerformanceDiagnosticsDefaultManager");
        managerObject.SetActive(false);
        try
        {
            Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
            FieldInfo diagnosticsField = typeof(Fog3Manager).GetField(
                "logPerformanceDiagnostics",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(diagnosticsField);
            Assert.IsFalse((bool)diagnosticsField.GetValue(manager));
        }
        finally
        {
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void RepeatedSceneReadyNotification_DoesNotReplaceInitializedMap()
    {
        GameObject managerObject = new GameObject("Fog3RepeatedSceneReadyManager");
        managerObject.SetActive(false);
        try
        {
            var controller = new Fog3Controller();
            controller.Initialize(CreateTerrainInfo(new[] { true, true, true, true, true, true }), (Fix64)0.1f);
            Fog3MapData initializedMap = controller.MapData;
            Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
            typeof(Fog3Manager).GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, controller);
            typeof(Fog3Manager).GetField("isInitialized", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, true);

            MethodInfo handleSceneReady = typeof(Fog3Manager).GetMethod(
                "HandleSceneBecameAvailable",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(handleSceneReady);

            handleSceneReady.Invoke(manager, new object[] { "Game", "test duplicate notification" });

            Assert.IsTrue(manager.IsInitialized);
            Assert.AreSame(initializedMap, manager.MapData);
        }
        finally
        {
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void VisibilityPresentation_BecomesReadyOnlyAfterALogicFrame()
    {
        GameObject managerObject = new GameObject("Fog3LogicFramePresentationManager");
        EntityRegistry.Clear();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
        LogicTimeControlService.BeginTimeline();
        try
        {
            var controller = new Fog3Controller();
            controller.Initialize(CreateTerrainInfo(new[] { true, true, true, true, true, true }), (Fix64)0.1f);
            Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
            typeof(Fog3Manager).GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, controller);
            typeof(Fog3Manager).GetField("isInitialized", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, true);
            MethodInfo presentVisibility = typeof(Fog3Manager).GetMethod(
                "OnVisibilityUpdated",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(presentVisibility);

            presentVisibility.Invoke(manager, new object[] { controller.MapData });
            Assert.IsFalse(manager.HasPresentedLogicFrameVisibility);

            LogicTimeControlService.BeginFrame(1);
            presentVisibility.Invoke(manager, new object[] { controller.MapData });
            Assert.IsTrue(manager.HasPresentedLogicFrameVisibility);
        }
        finally
        {
            if (LogicTimeControlService.IsActive)
                LogicTimeControlService.EndTimeline();
            EntityRegistry.Clear();
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void ExplorationCheckpoint_RestoresExploredBitsButNotTransientVisibility()
    {
        Fog3MapData map = CreateMap(new[] { true, true, true, true, true, true });
        map.MarkExplored(1, 0);
        map.MarkExplored(2, 1);
        map.AddVisibility(1, 0, 1f);
        map.AddVisibility(2, 1, 0.5f);
        Fog3ExplorationCheckpoint checkpoint = map.CaptureExplorationCheckpoint();

        map.ResetExploration();
        map.MarkExplored(0, 1);
        map.AddVisibility(0, 1, 1f);
        map.RestoreExplorationCheckpoint(checkpoint);

        Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(1, 0));
        Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(2, 1));
        Assert.AreEqual(Fog3CellState.Hidden, map.GetCellState(0, 1));
        Assert.AreEqual(0f, map.GetVisibility(1, 0));
        Assert.IsTrue(map.IsDirty);
    }

    [Test]
    public void RenderVisibility_DoesNotMutateLogicExploration()
    {
        Fog3MapData map = CreateMap(new[] { true, true, true, true, true, true });

        map.AddVisibility(1, 0, 1f);

        Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(1, 0));
        Assert.IsFalse(map.IsExplored(1, 0));
        Assert.AreEqual(0, map.ExploredCellCount);
    }

    [Test]
    public void FixedWorldToGrid_UsesOneBoundaryMappingForLogicAndPresentation()
    {
        var map = new Fog3MapData(new Fog3TerrainInfo(
            4,
            1,
            0.09f,
            new Vector3(10.08f, 0f, 5.04f),
            new[] { true, true, true, true },
            "FixedWorldToGridBoundary"), (Fix64)0.1f);
        FixVector2 logicPosition = new FixVector2(
            Fix64.FromRaw(42025),
            (Fix64)5.04f);
        Vector3 presentationPosition = new Vector3(
            (float)logicPosition.x,
            0f,
            (float)logicPosition.y);

        Assert.IsTrue(map.WorldToGrid(logicPosition, out int logicX, out int logicY));
        Assert.IsTrue(map.WorldToGrid(presentationPosition, out int presentationX, out int presentationY));
        Assert.AreEqual(1, presentationX, "Fixed 0.1-world-unit Fog boundary fixture changed.");
        Assert.AreEqual(presentationX, logicX);
        Assert.AreEqual(presentationY, logicY);
    }

    [Test]
    public void WorldToGrid_UsesFloorDivisionForNegativeOffsets()
    {
        var map = new Fog3MapData(new Fog3TerrainInfo(
            4,
            1,
            0.09f,
            Vector3.zero,
            new[] { true, true, true, true },
            "WorldToGridNegativeOffset"), (Fix64)0.1f);
        FixVector2 logicPosition = new FixVector2(Fix64.FromRaw(-1), Fix64.Zero);
        Vector3 presentationPosition = new Vector3((float)logicPosition.x, 0f, 0f);

        Assert.IsFalse(map.WorldToGrid(logicPosition, out int logicX, out int logicY));
        Assert.IsFalse(map.WorldToGrid(presentationPosition, out int presentationX, out int presentationY));
        Assert.AreEqual(-1, logicX);
        Assert.AreEqual(logicX, presentationX);
        Assert.AreEqual(0, logicY);
        Assert.AreEqual(logicY, presentationY);
    }

    [Test]
    public void Controller_PublishesAuthoritativeVisibilityWithoutRecalculatingIt()
    {
        var controller = new Fog3Controller();
        controller.Initialize(CreateTerrainInfo(new[] { true, true, true, true, true, true }), (Fix64)0.1f);
        controller.MapData.MarkVisible(1, 0);
        int publishedCount = 0;
        controller.VisibilityUpdated += map =>
        {
            publishedCount++;
            Assert.AreSame(controller.MapData, map);
        };

        controller.PublishAuthoritativeVisibility(false);

        Assert.AreEqual(1, publishedCount);
        Assert.AreEqual(Fog3CellState.Hidden, controller.MapData.GetCellState(0, 0));
        Assert.AreEqual(Fog3CellState.Visible, controller.MapData.GetCellState(1, 0));
        Assert.IsFalse(controller.MapData.IsDirty);
    }

    [Test]
    public void AuthoritativeFog_UsesLogicPositionAndLeavesExploredStateBehind()
    {
        EntityRegistry.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicCardPlacementAuthority.BeginTimeline();
        try
        {
            var entity = new SimEntityContext
            {
                PositionFixed = new FixVector2((Fix64)0.5f, (Fix64)0.5f),
                Side = SideType.PlayerSide,
            };
            EntityRegistry.Register(entity);

            Fog3MapData map = new Fog3MapData(CreateTerrainInfo(new[] { true, true, true, true, true, true }), (Fix64)0.1f);
            LogicCardPlacementAuthority.BindWorldForTests(
                map,
                System.Array.Empty<LogicCombatShape>(),
                (Fix64)0.49f,
                (Fix64)0.49f,
                (Fix64)0.49f);
            LogicTimeControlService.BeginFrame(1);
            LogicCardPlacementAuthority.ApplyFrame(1);

            Assert.IsTrue(map.WorldToGrid(entity.PositionFixed, out int firstX, out int firstY));
            Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(firstX, firstY));
            Assert.IsTrue(map.WorldToGrid(new FixVector2((Fix64)2.5f, (Fix64)0.5f), out int untouchedX, out int untouchedY));
            Assert.AreEqual(Fog3CellState.Hidden, map.GetCellState(untouchedX, untouchedY));

            entity.PositionFixed = new FixVector2((Fix64)1.5f, (Fix64)0.5f);
            LogicTimeControlService.BeginFrame(2);
            LogicCardPlacementAuthority.ApplyFrame(2);

            Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(firstX, firstY));
            Assert.IsTrue(map.WorldToGrid(entity.PositionFixed, out int secondX, out int secondY));
            Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(secondX, secondY));
        }
        finally
        {
            LogicCardPlacementAuthority.EndTimeline();
            LogicTimeControlService.EndTimeline();
            EntityRegistry.Clear();
        }
    }

    [Test]
    public void ManagerEntityRevealer_KeepsViewAndLogicEntityIdsInSeparateDomains()
    {
        const int viewEntityId = 404;
        const int logicEntityId = 17;
        GameObject managerObject = new GameObject("Fog3EntityIdDomainManager");
        GameObject targetObject = new GameObject("Fog3EntityIdDomainTarget");
        try
        {
            var controller = new Fog3Controller();
            controller.Initialize(CreateTerrainInfo(new[] { true, true, true, true, true, true }), (Fix64)0.1f);
            Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
            typeof(Fog3Manager).GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, controller);
            typeof(Fog3Manager).GetField("isInitialized", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, true);

            int revealerId = manager.RegisterRevealer(
                targetObject.transform,
                1f,
                viewEntityId,
                false,
                true,
                logicEntityId);

            Assert.IsTrue(controller.TryGetRevealer(revealerId, out Fog3RevealerData revealer));
            Assert.AreEqual(logicEntityId, revealer.LogicEntityId);

            var entityRevealers = (System.Collections.Generic.Dictionary<int, int>)typeof(Fog3Manager)
                .GetField("entityRevealers", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(manager);
            Assert.AreEqual(revealerId, entityRevealers[viewEntityId]);
            Assert.IsFalse(entityRevealers.ContainsKey(logicEntityId));
        }
        finally
        {
            Object.DestroyImmediate(targetObject);
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void ExplorationCheckpoint_RejectsDifferentTerrainTopology()
    {
        Fog3ExplorationCheckpoint checkpoint = CreateMap(
            new[] { true, true, true, true, true, true }).CaptureExplorationCheckpoint();
        Fog3MapData different = CreateMap(new[] { true, true, false, true, true, true });

        Assert.Throws<System.InvalidOperationException>(() =>
            different.RestoreExplorationCheckpoint(checkpoint));
    }

    [Test]
    public void ExplorationCheckpoint_CompressesSparsePayloadAndReusesUnchangedSnapshot()
    {
        const int width = 1024;
        const int height = 1024;
        var walkable = new bool[width * height];
        System.Array.Fill(walkable, true);
        var map = new Fog3MapData(new Fog3TerrainInfo(
            width,
            height,
            0.1f,
            Vector3.zero,
            walkable,
            "SparseCheckpointTest"), (Fix64)0.1f);
        map.MarkExplored(1, 1);
        map.MarkExplored(width - 2, height - 2);

        Fog3ExplorationCheckpoint first = map.CaptureExplorationCheckpoint();
        Fog3ExplorationCheckpoint unchanged = map.CaptureExplorationCheckpoint();

        Assert.AreSame(first, unchanged);
        Assert.AreEqual((width * height + 7) / 8, first.RawPayloadByteCount);
        Assert.Less(first.StoredPayloadByteCount, first.RawPayloadByteCount / 10);

        map.MarkExplored(2, 2);
        Fog3ExplorationCheckpoint changed = map.CaptureExplorationCheckpoint();
        Assert.AreNotSame(first, changed);
        map.ResetExploration();
        map.RestoreExplorationCheckpoint(changed);
        Assert.IsTrue(map.IsExplored(2, 2));
    }

    [Test]
    public void LogicGrid_ClipsTheLastCellToTerrainBounds()
    {
        var map = new Fog3MapData(new Fog3TerrainInfo(
            4,
            1,
            0.09f,
            new Vector3(10.08f, 0f, 5.04f),
            new[] { true, true, true, true },
            "ClippedLogicCellTest"), (Fix64)0.1f);

        Assert.AreEqual(4, map.Width);
        Assert.AreEqual(0.36f, map.Bounds.size.x, 0.000001f);
        Assert.IsTrue(map.WorldToGrid(new Vector3(10.439f, 0f, 5.04f), out int insideX, out _));
        Assert.AreEqual(3, insideX);
        Assert.IsFalse(map.WorldToGrid(new Vector3(10.441f, 0f, 5.04f), out _, out _));
        Assert.Less(map.GridToWorldCenter(3, 0).x, map.Bounds.max.x);
    }

    [Test]
    public void EnemyVisibility_ResolvesBoundViewFromLogicRegistry()
    {
        GameObject managerObject = null;
        GameObject viewObject = null;
        GameObject healthBarObject = null;
        LogicEntityId entityId = default;
        bool viewBound = false;

        EntityRegistry.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        try
        {
            entityId = LogicEntityLifecycleService.RequestSpawn(new LogicEntitySpawnDescriptor(
                FixVector2.Zero,
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.EnemySide,
                "Fog3EnemyVisibilityTest"));
            LogicEntityState logicState = LogicEntityStateStore.GetRequired(entityId);
            EntityRegistry.Register(logicState);

            viewObject = new GameObject("Fog3EnemyVisibilityView");
            Entity entityComponent = viewObject.AddComponent<Entity>();
            MAEntity view = viewObject.AddComponent<MAEntity>();
            MeshRenderer renderer = viewObject.AddComponent<MeshRenderer>();
            FieldInfo entityIdField = typeof(Entity).GetField("m_Id", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo entityField = typeof(EntityLogic).GetField("m_Entity", BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo cachedEntityIdProperty = typeof(EntityBase).GetProperty(nameof(EntityBase.Id));
            PropertyInfo logicEntityIdProperty = typeof(MAEntity).GetProperty(nameof(MAEntity.LogicEntityId));
            Assert.NotNull(entityIdField);
            Assert.NotNull(entityField);
            Assert.NotNull(cachedEntityIdProperty);
            Assert.NotNull(logicEntityIdProperty);
            entityIdField.SetValue(entityComponent, 404);
            entityField.SetValue(view, entityComponent);
            cachedEntityIdProperty.SetValue(view, 404);
            logicEntityIdProperty.SetValue(view, entityId);
            Assert.AreEqual(404, view.Id);

            LogicEntityLifecycleService.BindView(entityId, view.Id, view);
            viewBound = true;

            managerObject = new GameObject("Fog3EnemyVisibilityManager");
            managerObject.SetActive(false);
            Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
            MethodInfo updateEnemyVisibility = typeof(Fog3Manager).GetMethod(
                "UpdateEnemyVisibilityByFog",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(updateEnemyVisibility);

            Fog3MapData map = CreateMap(new[] { true, true, true, true, true, true });
            updateEnemyVisibility.Invoke(manager, new object[] { map });
            Assert.IsFalse(renderer.enabled);

            healthBarObject = new GameObject("LateCreatedEnemyHealthBar");
            Canvas healthCanvas = healthBarObject.AddComponent<Canvas>();
            HealthBarComp healthBar = healthBarObject.AddComponent<HealthBarComp>();
            typeof(HealthBarComp).GetField("_entityId", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(healthBar, view.Id);
            typeof(HealthBarComp).GetField("ownerCanvas", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(healthBar, healthCanvas);
            var activeBars = (System.Collections.Generic.Dictionary<int, HealthBarComp>)typeof(HealthBarComp)
                .GetField("ActiveBars", BindingFlags.Static | BindingFlags.NonPublic)
                .GetValue(null);
            activeBars[view.Id] = healthBar;

            updateEnemyVisibility.Invoke(manager, new object[] { map });
            Assert.IsFalse(healthCanvas.enabled, "A health bar created after the cached fog update must still be hidden.");

            map.AddVisibility(0, 0, 1f);
            updateEnemyVisibility.Invoke(manager, new object[] { map });
            Assert.IsTrue(renderer.enabled);
            Assert.IsTrue(healthCanvas.enabled);

            manager.GetEnemyUnitVisibilityDiagnostics(
                out int aliveLogicCount,
                out int boundViewCount,
                out int trackedViewCount,
                out int renderableViewCount,
                out int fogVisibleViewCount,
                out int rendererMismatchViewCount);
            Assert.AreEqual(1, aliveLogicCount);
            Assert.AreEqual(1, boundViewCount);
            Assert.AreEqual(1, trackedViewCount);
            Assert.AreEqual(1, renderableViewCount);
            Assert.AreEqual(1, fogVisibleViewCount);
            Assert.AreEqual(0, rendererMismatchViewCount);
        }
        finally
        {
            if (managerObject != null)
                Object.DestroyImmediate(managerObject);
            if (viewBound)
                LogicEntityLifecycleService.UnbindView(entityId, 404);
            EntityRegistry.Clear();
            if (healthBarObject != null)
                Object.DestroyImmediate(healthBarObject);
            if (viewObject != null)
                Object.DestroyImmediate(viewObject);
            LogicEntityLifecycleService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    [Test]
    public void EnemyPermanentStealth_RemainsHiddenInVisibleFogCellAndAfterFogReset()
    {
        GameObject managerObject = null;
        GameObject viewObject = null;
        LogicEntityId entityId = default;
        bool viewBound = false;

        EntityRegistry.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        try
        {
            entityId = LogicEntityLifecycleService.RequestSpawn(new LogicEntitySpawnDescriptor(
                FixVector2.Zero,
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.EnemySide,
                "Buil_Trap_Lv1"));
            LogicEntityState logicState = LogicEntityStateStore.GetRequired(entityId);
            EntityRegistry.Register(logicState);

            viewObject = new GameObject("Fog3EnemyPermanentStealthView");
            Entity entityComponent = viewObject.AddComponent<Entity>();
            BuildingEntity view = viewObject.AddComponent<BuildingEntity>();
            MeshRenderer renderer = viewObject.AddComponent<MeshRenderer>();
            FieldInfo entityIdField = typeof(Entity).GetField("m_Id", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo entityField = typeof(EntityLogic).GetField("m_Entity", BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo cachedEntityIdProperty = typeof(EntityBase).GetProperty(nameof(EntityBase.Id));
            PropertyInfo logicEntityIdProperty = typeof(MAEntity).GetProperty(nameof(MAEntity.LogicEntityId));
            Assert.NotNull(entityIdField);
            Assert.NotNull(entityField);
            Assert.NotNull(cachedEntityIdProperty);
            Assert.NotNull(logicEntityIdProperty);
            entityIdField.SetValue(entityComponent, 405);
            entityField.SetValue(view, entityComponent);
            cachedEntityIdProperty.SetValue(view, 405);
            logicEntityIdProperty.SetValue(view, entityId);
            view.OwnerFactionID = EntitySideHelper.EnemyFactionId;
            view.SetPermanentStealthVisibility(true);
            Assert.IsFalse(renderer.enabled);

            LogicEntityLifecycleService.BindView(entityId, view.Id, view);
            viewBound = true;

            managerObject = new GameObject("Fog3EnemyPermanentStealthManager");
            managerObject.SetActive(false);
            Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
            MethodInfo updateEnemyVisibility = typeof(Fog3Manager).GetMethod(
                "UpdateEnemyVisibilityByFog",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo resetEnemyVisibility = typeof(Fog3Manager).GetMethod(
                "ResetEnemyVisibilityStates",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(updateEnemyVisibility);
            Assert.NotNull(resetEnemyVisibility);

            Fog3MapData map = CreateMap(new[] { true, true, true, true, true, true });
            map.AddVisibility(0, 0, 1f);
            updateEnemyVisibility.Invoke(manager, new object[] { map });
            Assert.IsFalse(renderer.enabled);

            resetEnemyVisibility.Invoke(manager, null);
            Assert.IsFalse(renderer.enabled);
        }
        finally
        {
            if (managerObject != null)
                Object.DestroyImmediate(managerObject);
            if (viewBound)
                LogicEntityLifecycleService.UnbindView(entityId, 405);
            EntityRegistry.Clear();
            if (viewObject != null)
                Object.DestroyImmediate(viewObject);
            LogicEntityLifecycleService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    private static Fog3MapData CreateMap(bool[] walkable)
    {
        return new Fog3MapData(CreateTerrainInfo(walkable), (Fix64)0.1f);
    }

    private static Fog3WorldOverlayView BuildTerrainConformingView(GameObject viewObject, Fog3TerrainInfo terrain)
    {
        var settings = new Fog3ViewSettings
        {
            SurfaceMode = Fog3OverlaySurfaceMode.TerrainConforming,
            OutsideMaskPadding = 0f,
            SurfaceOffset = 0.08f,
            DrawOverSceneGeometry = false,
            OverlayAlwaysOnTopShader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop"),
        };
        Fog3WorldOverlayView view = viewObject.AddComponent<Fog3WorldOverlayView>();
        view.Build(terrain, new Fog3MapData(terrain, (Fix64)0.1f), settings, 5f, Physics.DefaultRaycastLayers, Vector3.zero, 1f, 0.25f);
        return view;
    }

    private static Fog3TerrainInfo CreateTerrainInfo(bool[] walkable)
    {
        return new Fog3TerrainInfo(
            3,
            2,
            1f,
            Vector3.zero,
            walkable,
            "StageCheckpointTest");
    }

    private static Fog3TerrainInfo CreateFlatTerrain(int width, int height, float cellSize)
    {
        bool[] walkable = new bool[checked(width * height)];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        return new Fog3TerrainInfo(width, height, cellSize, Vector3.zero, walkable, "IndependentFogGridTest");
    }
}
