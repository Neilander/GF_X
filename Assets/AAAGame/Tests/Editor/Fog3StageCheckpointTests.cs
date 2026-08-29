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

    [TestCase(0f)]
    [TestCase(0.55f)]
    public void BoundaryFade_RendersOnlyInsideTheMoreTransparentCell(float transparentSideAlpha)
    {
        Shader shader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop");
        Assert.IsNotNull(shader);

        Texture2D source = new Texture2D(2, 1, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        RenderTexture target = RenderTexture.GetTemporary(400, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Material material = new Material(shader);
        Texture2D readback = new Texture2D(400, 8, TextureFormat.RGBA32, false, true);
        RenderTexture previous = RenderTexture.active;
        try
        {
            source.SetPixels(new[]
            {
                new Color(1f, 1f, 1f, 1f),
                new Color(1f, 1f, 1f, transparentSideAlpha),
            });
            source.Apply(false);
            material.SetFloat("_FogBoundaryFadeDistance", 0.25f);

            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            Graphics.Blit(source, target, material);
            readback.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
            readback.Apply(false);

            Assert.AreEqual(1f, readback.GetPixel(180, 4).r, 0.02f, "The darker cell must remain unchanged up to its edge.");
            const int boundarySampleX = 200;
            float boundaryAlpha = readback.GetPixel(boundarySampleX, 4).r;
            float midpointAlpha = readback.GetPixel(225, 4).r;
            Assert.AreEqual(1f, boundaryAlpha, 0.03f, "The transition starts from the darker alpha on the transparent side.");
            Assert.Greater(boundaryAlpha, midpointAlpha);
            Assert.Greater(midpointAlpha, transparentSideAlpha + 0.05f);
            Assert.AreEqual(transparentSideAlpha, readback.GetPixel(251, 4).r, 0.03f, "The fade distance is measured inside the transparent cell.");
            Assert.AreEqual(transparentSideAlpha, readback.GetPixel(320, 4).r, 0.02f);
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
    public void BoundaryFade_SupportsTwoCellDistance()
    {
        Shader shader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop");
        Assert.IsNotNull(shader);

        Texture2D source = new Texture2D(4, 1, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        RenderTexture target = RenderTexture.GetTemporary(800, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Material material = new Material(shader);
        Texture2D readback = new Texture2D(800, 8, TextureFormat.RGBA32, false, true);
        RenderTexture previous = RenderTexture.active;
        try
        {
            source.SetPixels(new[]
            {
                Color.white,
                new Color(1f, 1f, 1f, 0f),
                new Color(1f, 1f, 1f, 0f),
                new Color(1f, 1f, 1f, 0f),
            });
            source.Apply(false);
            material.SetFloat("_FogBoundaryFadeDistance", 2f);

            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            Graphics.Blit(source, target, material);
            readback.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
            readback.Apply(false);

            Assert.AreEqual(1f, readback.GetPixel(180, 4).r, 0.02f, "The darker cell must remain unchanged.");
            float boundaryAlpha = readback.GetPixel(200, 4).r;
            float oneCellAlpha = readback.GetPixel(400, 4).r;
            float twoCellAlpha = readback.GetPixel(600, 4).r;
            Assert.AreEqual(1f, boundaryAlpha, 0.03f, "The transition must start from the darker alpha.");
            Assert.Greater(oneCellAlpha, 0.05f, "The second transparent cell must participate in a two-cell fade.");
            Assert.Less(oneCellAlpha, boundaryAlpha);
            Assert.AreEqual(0f, twoCellAlpha, 0.03f, "The configured fade endpoint must return to the transparent alpha.");
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
    public void BoundaryFade_KeepsTransientCircularRevealContourSmooth()
    {
        Shader shader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop");
        Assert.IsNotNull(shader);

        const int sourceSize = 256;
        const int renderedPixelsPerCell = 4;
        const int renderSize = sourceSize * renderedPixelsPerCell;
        const float transientVisibleAlpha = 0.72f;
        Texture2D source = new Texture2D(sourceSize, sourceSize, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        Color[] sourcePixels = new Color[sourceSize * sourceSize];
        Vector2 sourceCenter = new Vector2(127.5f, 127.5f);
        for (int y = 0; y < sourceSize; y++)
        {
            for (int x = 0; x < sourceSize; x++)
            {
                float alpha = Vector2.Distance(new Vector2(x, y), sourceCenter) <= 72f
                    ? transientVisibleAlpha
                    : 1f;
                sourcePixels[x + y * sourceSize] = new Color(1f, 1f, 1f, alpha);
            }
        }

        source.SetPixels(sourcePixels);
        source.Apply(false);

        RenderTexture target = RenderTexture.GetTemporary(renderSize, renderSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Material material = new Material(shader);
        Texture2D readback = new Texture2D(renderSize, renderSize, TextureFormat.RGBA32, false, true);
        RenderTexture previous = RenderTexture.active;
        try
        {
            material.SetFloat("_FogBoundaryFadeDistance", 2f);

            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            Graphics.Blit(source, target, material);
            readback.ReadPixels(new Rect(0f, 0f, renderSize, renderSize), 0, 0, false);
            readback.Apply(false);

            float renderedCenter = (renderSize - 1) * 0.5f;
            float[] contourLevels = { 0.1f, 0.5f, 0.9f };
            foreach (float contourLevel in contourLevels)
            {
                float contourThreshold = Mathf.Lerp(transientVisibleAlpha, 1f, contourLevel);
                float minimumRadius = float.PositiveInfinity;
                float maximumRadius = 0f;
                for (int angleDegrees = 0; angleDegrees < 360; angleDegrees++)
                {
                    float angleRadians = angleDegrees * Mathf.Deg2Rad;
                    float directionX = Mathf.Cos(angleRadians);
                    float directionY = Mathf.Sin(angleRadians);
                    float contourRadius = -1f;
                    for (float radius = 0f; radius < 76f * renderedPixelsPerCell; radius += 0.25f)
                    {
                        int sampleX = Mathf.RoundToInt(renderedCenter + directionX * radius);
                        int sampleY = Mathf.RoundToInt(renderedCenter + directionY * radius);
                        if (readback.GetPixel(sampleX, sampleY).r < contourThreshold)
                            continue;

                        contourRadius = radius;
                        break;
                    }

                    Assert.GreaterOrEqual(
                        contourRadius,
                        0f,
                        $"No transient reveal contour found at level {contourLevel} and angle {angleDegrees}.");
                    minimumRadius = Mathf.Min(minimumRadius, contourRadius);
                    maximumRadius = Mathf.Max(maximumRadius, contourRadius);
                }

                float normalizedMapRipple = (maximumRadius - minimumRadius) / renderSize;
                Assert.Less(
                    normalizedMapRipple,
                    1f / sourceSize,
                    $"The {contourLevel:P0} contour of a revealing circular sight area must not ripple by one independently configured fog cell.");
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
    public void BoundaryFade_DoesNotInterpretHistoricalAlphaAsTargetStateBoundary()
    {
        Shader shader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop");
        Assert.IsNotNull(shader);

        const int sourceWidth = 32;
        const int renderedPixelsPerCell = 100;
        const int renderWidth = sourceWidth * renderedPixelsPerCell;
        const float leftCurrentAlpha = 0.2f;
        const float rightCurrentAlpha = 0.8f;
        Texture2D source = new Texture2D(sourceWidth, 1, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        Color32[] sourcePixels = new Color32[sourceWidth];
        for (int x = 0; x < sourceWidth; x++)
        {
            byte currentAlpha = (byte)Mathf.RoundToInt(
                (x < sourceWidth / 2 ? leftCurrentAlpha : rightCurrentAlpha) * byte.MaxValue);
            sourcePixels[x] = new Color32(0, (byte)Fog3CellState.Visible, 0, currentAlpha);
        }

        source.SetPixels32(sourcePixels);
        source.Apply(false);

        RenderTexture target = RenderTexture.GetTemporary(renderWidth, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Material material = new Material(shader);
        Texture2D readback = new Texture2D(renderWidth, 8, TextureFormat.RGBA32, false, true);
        RenderTexture previous = RenderTexture.active;
        try
        {
            material.SetFloat("_FogBoundaryFadeDistance", 2f);
            material.SetColor("_FogVisibleColor", Color.white);

            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            Graphics.Blit(source, target, material);
            readback.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
            readback.Apply(false);

            int boundaryX = renderWidth / 2;
            for (int offset = 25; offset <= 175; offset += 25)
            {
                float leftAlpha = readback.GetPixel(boundaryX - offset, 4).r;
                float rightAlpha = readback.GetPixel(boundaryX + offset - 1, 4).r;
                Assert.AreEqual(
                    leftCurrentAlpha + rightCurrentAlpha,
                    leftAlpha + rightAlpha,
                    0.04f,
                    $"Historical alpha must be filtered symmetrically when target state is uniform. offset={offset}.");
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
    public void BoundaryFade_PackedPresentationTextureKeepsTransientCircularRevealSmooth()
    {
        Shader shader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop");
        Assert.IsNotNull(shader);

        const int sourceSize = 64;
        const int renderedPixelsPerCell = 8;
        const int renderSize = sourceSize * renderedPixelsPerCell;
        const float transientVisibleAlpha = 0.72f;
        Texture2D source = new Texture2D(sourceSize, sourceSize, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        Color32[] sourcePixels = new Color32[sourceSize * sourceSize];
        Vector2 sourceCenter = new Vector2(31.5f, 31.5f);
        for (int y = 0; y < sourceSize; y++)
        {
            for (int x = 0; x < sourceSize; x++)
            {
                bool visible = Vector2.Distance(new Vector2(x, y), sourceCenter) <= 18f;
                byte targetAlpha = visible ? (byte)0 : byte.MaxValue;
                byte currentAlpha = visible
                    ? (byte)Mathf.RoundToInt(transientVisibleAlpha * byte.MaxValue)
                    : byte.MaxValue;
                sourcePixels[x + y * sourceSize] = new Color32(
                    targetAlpha,
                    (byte)(visible ? Fog3CellState.Visible : Fog3CellState.Hidden),
                    0,
                    currentAlpha);
            }
        }

        source.SetPixels32(sourcePixels);
        source.Apply(false);
        RenderTexture target = RenderTexture.GetTemporary(renderSize, renderSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Material material = new Material(shader);
        Texture2D readback = new Texture2D(renderSize, renderSize, TextureFormat.RGBA32, false, true);
        RenderTexture previous = RenderTexture.active;
        try
        {
            material.SetFloat("_FogBoundaryFadeDistance", 2f);
            material.SetColor("_FogHiddenColor", Color.white);
            material.SetColor("_FogVisibleColor", Color.white);

            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            Graphics.Blit(source, target, material);
            readback.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
            readback.Apply(false);

            float renderedCenter = (renderSize - 1) * 0.5f;
            float[] contourLevels = { 0.1f, 0.5f, 0.9f };
            foreach (float contourLevel in contourLevels)
            {
                float contourThreshold = Mathf.Lerp(transientVisibleAlpha, 1f, contourLevel);
                float minimumRadius = float.PositiveInfinity;
                float maximumRadius = 0f;
                for (int angleDegrees = 0; angleDegrees < 360; angleDegrees++)
                {
                    float angleRadians = angleDegrees * Mathf.Deg2Rad;
                    float directionX = Mathf.Cos(angleRadians);
                    float directionY = Mathf.Sin(angleRadians);
                    float contourRadius = -1f;
                    for (float radius = 0f; radius < 24f * renderedPixelsPerCell; radius += 0.25f)
                    {
                        int sampleX = Mathf.Clamp(Mathf.RoundToInt(renderedCenter + directionX * radius), 0, renderSize - 1);
                        int sampleY = Mathf.Clamp(Mathf.RoundToInt(renderedCenter + directionY * radius), 0, renderSize - 1);
                        if (readback.GetPixel(sampleX, sampleY).r < contourThreshold)
                            continue;

                        contourRadius = radius;
                        break;
                    }

                    Assert.GreaterOrEqual(contourRadius, 0f, $"No packed contour at level {contourLevel:P0}, angle {angleDegrees}.");
                    minimumRadius = Mathf.Min(minimumRadius, contourRadius);
                    maximumRadius = Mathf.Max(maximumRadius, contourRadius);
                }

                Assert.Less(
                    (maximumRadius - minimumRadius) / renderSize,
                    1f / sourceSize,
                    $"Packed presentation contour ripples by a full logic cell at level {contourLevel:P0}.");
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
    public void BoundaryFade_ReconstructsDiagonalContourWithoutCellSizedSteps()
    {
        Shader shader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop");
        Assert.IsNotNull(shader);

        const int sourceSize = 8;
        Texture2D source = new Texture2D(sourceSize, sourceSize, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        Color[] sourcePixels = new Color[sourceSize * sourceSize];
        for (int y = 0; y < sourceSize; y++)
        {
            for (int x = 0; x < sourceSize; x++)
            {
                float alpha = x + y < sourceSize - 1 ? 1f : 0f;
                sourcePixels[x + y * sourceSize] = new Color(1f, 1f, 1f, alpha);
            }
        }

        source.SetPixels(sourcePixels);
        source.Apply(false);

        RenderTexture target = RenderTexture.GetTemporary(512, 512, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Material material = new Material(shader);
        Texture2D readback = new Texture2D(512, 512, TextureFormat.RGBA32, false, true);
        RenderTexture previous = RenderTexture.active;
        try
        {
            material.SetFloat("_FogBoundaryFadeDistance", 2f);

            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            Graphics.Blit(source, target, material);
            readback.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
            readback.Apply(false);

            int previousContourX = -1;
            int currentRunLength = 0;
            int longestRunLength = 0;
            int maximumRowJump = 0;
            for (int y = 192; y <= 320; y++)
            {
                int contourX = -1;
                for (int x = 0; x < target.width; x++)
                {
                    if (readback.GetPixel(x, y).r < 0.5f)
                    {
                        contourX = x;
                        break;
                    }
                }

                Assert.GreaterOrEqual(contourX, 0, $"No half-alpha contour found on row {y}.");
                if (contourX == previousContourX)
                {
                    currentRunLength++;
                }
                else
                {
                    currentRunLength = 1;
                    if (previousContourX >= 0)
                        maximumRowJump = Mathf.Max(maximumRowJump, Mathf.Abs(contourX - previousContourX));
                }

                longestRunLength = Mathf.Max(longestRunLength, currentRunLength);
                previousContourX = contourX;
            }

            const int renderedPixelsPerCell = 512 / sourceSize;
            Assert.Less(
                longestRunLength,
                renderedPixelsPerCell / 4,
                "The diagonal half-alpha contour must move continuously instead of holding for a visible fraction of a cell.");
            Assert.Less(
                maximumRowJump,
                renderedPixelsPerCell / 4,
                "The diagonal half-alpha contour must not jump across a visible fraction of a cell between adjacent rows.");
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
