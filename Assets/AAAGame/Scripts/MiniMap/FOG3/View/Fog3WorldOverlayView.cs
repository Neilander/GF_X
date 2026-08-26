using UnityEngine;
using UnityEngine.Rendering;

namespace AAAGame.MiniMap.FOG3
{
    public sealed class Fog3WorldOverlayView : MonoBehaviour
    {
        private const string FogOverlayShaderAssetPath = "Assets/AAAGame/Scripts/MiniMap/FOG3/View/Fog3OverlayAlwaysOnTop.shader";
        private Texture2D fogTexture;
        private Color32[] pixels;
        private Color32[] targetPixels;
        private float[] currentAlphas;
        private float[] targetAlphas;
        private Material fogMaterial;
        private Material outsideMaterial;
        private Fog3ViewSettings settings;
        private LayerMask heightSampleMask;
        private float overlayHeight;
        private Fog3TerrainInfo terrainInfo;
        private MeshFilter fogMeshFilter;
        private Bounds fogMeshBounds;
        private bool fogMeshUsesCameraProjectionGrid;
        private float visibilityFadeSpeed;
        private float visibilityBoundaryFadeDistance;
        private bool visibilityFadeActive;
        private readonly RaycastHit[] projectionHeightHits = new RaycastHit[32];
        private readonly System.Collections.Generic.List<OutsideMaskQuad> outsideMaskQuads = new System.Collections.Generic.List<OutsideMaskQuad>();

        private struct OutsideMaskQuad
        {
            public MeshFilter MeshFilter;
            public Bounds Bounds;
            public float Y;
        }

        public Texture2D FogTexture => fogTexture;
        public Material FogMaterial => fogMaterial;
        public Material OutsideMaterial => outsideMaterial;
        public Bounds FogMeshBounds => fogMeshBounds;
        public int OutsideMaskQuadCount => outsideMaskQuads.Count;
        public bool FogMeshUsesCameraProjectionGrid => fogMeshUsesCameraProjectionGrid;
        public float VisibilityFadeSpeed => visibilityFadeSpeed;
        public float VisibilityBoundaryFadeDistance => visibilityBoundaryFadeDistance;

        public void Build(
            Fog3TerrainInfo terrainInfo,
            Fog3ViewSettings viewSettings,
            float resolvedOverlayHeight,
            LayerMask resolvedHeightSampleMask,
            Vector3 worldOffset,
            float resolvedVisibilityFadeSpeed,
            float resolvedVisibilityBoundaryFadeDistance)
        {
            if (terrainInfo == null)
                throw new System.ArgumentNullException(nameof(terrainInfo));
            if (resolvedVisibilityFadeSpeed <= 0f || float.IsNaN(resolvedVisibilityFadeSpeed) || float.IsInfinity(resolvedVisibilityFadeSpeed))
                throw new System.ArgumentOutOfRangeException(nameof(resolvedVisibilityFadeSpeed), "FOG3 visibility fade speed must be finite and positive.");
            if (resolvedVisibilityBoundaryFadeDistance <= 0f
                || resolvedVisibilityBoundaryFadeDistance > terrainInfo.CellSize
                || float.IsNaN(resolvedVisibilityBoundaryFadeDistance)
                || float.IsInfinity(resolvedVisibilityBoundaryFadeDistance))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(resolvedVisibilityBoundaryFadeDistance),
                    $"FOG3 visibility boundary fade distance must be finite, positive, and no greater than cell size {terrainInfo.CellSize}.");
            }

            this.terrainInfo = terrainInfo;
            settings = viewSettings ?? new Fog3ViewSettings();
            heightSampleMask = resolvedHeightSampleMask;
            overlayHeight = Mathf.Max(0f, resolvedOverlayHeight);
            visibilityFadeSpeed = resolvedVisibilityFadeSpeed;
            visibilityBoundaryFadeDistance = resolvedVisibilityBoundaryFadeDistance;
            SetWorldOffset(terrainInfo, worldOffset);
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            ClearChildren();
            ReleaseRuntimeResources();
            CreateTexture(terrainInfo);
            CreateFogPlane(terrainInfo);
            CreateOutsideMask(terrainInfo);
        }

        private void Update()
        {
            if (visibilityFadeActive)
                AdvanceVisibilityFade(Time.deltaTime);
        }

        public void SetWorldOffset(Fog3TerrainInfo terrainInfo, Vector3 worldOffset)
        {
            if (terrainInfo == null)
                return;

            transform.position = terrainInfo.Origin + worldOffset;
        }

        public bool RefreshCameraProjection()
        {
            if (!ShouldProjectCloudLayerToCamera() || terrainInfo == null)
                return false;

            bool refreshed = false;
            if (fogMeshFilter != null && fogMeshFilter.sharedMesh != null)
            {
                if (fogMeshUsesCameraProjectionGrid)
                    UpdateCameraProjectedCloudMesh(fogMeshFilter.sharedMesh, terrainInfo);
                else
                    UpdateQuadMesh(fogMeshFilter.sharedMesh, fogMeshBounds, overlayHeight);

                refreshed = true;
            }

            for (int i = 0; i < outsideMaskQuads.Count; i++)
            {
                OutsideMaskQuad quad = outsideMaskQuads[i];
                if (quad.MeshFilter == null || quad.MeshFilter.sharedMesh == null)
                    continue;

                UpdateQuadMesh(quad.MeshFilter.sharedMesh, quad.Bounds, quad.Y);
                refreshed = true;
            }

            return refreshed;
        }

        public void Render(Fog3MapData mapData, bool logPerformanceDiagnostics)
        {
            if (mapData == null)
                throw new System.ArgumentNullException(nameof(mapData));
            if (fogTexture == null || pixels == null || targetPixels == null || currentAlphas == null || targetAlphas == null)
                throw new System.InvalidOperationException("FOG3 overlay must be built before rendering visibility.");
            if (mapData.Width != fogTexture.width || mapData.Height != fogTexture.height)
            {
                throw new System.InvalidOperationException(
                    $"FOG3 overlay size mismatch. texture={fogTexture.width}x{fogTexture.height}, map={mapData.Width}x{mapData.Height}.");
            }

            long renderStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            long fillStartTicks = renderStartTicks;
            bool pixelsChanged = false;
            visibilityFadeActive = false;
            for (int y = 0; y < mapData.Height; y++)
            {
                for (int x = 0; x < mapData.Width; x++)
                {
                    int index = x + y * mapData.Width;
                    Color targetColor = GetTargetPixelColor(mapData, x, y);
                    Color32 targetPixel = targetColor;
                    targetPixels[index] = targetPixel;
                    targetAlphas[index] = targetColor.a;
                    visibilityFadeActive |= currentAlphas[index] != targetAlphas[index];

                    Color32 pixel = pixels[index];
                    if (pixel.r == targetPixel.r && pixel.g == targetPixel.g && pixel.b == targetPixel.b)
                        continue;

                    pixel.r = targetPixel.r;
                    pixel.g = targetPixel.g;
                    pixel.b = targetPixel.b;
                    pixels[index] = pixel;
                    pixelsChanged = true;
                }
            }

            long fillTicks = System.Diagnostics.Stopwatch.GetTimestamp() - fillStartTicks;
            long setStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            if (pixelsChanged)
                fogTexture.SetPixels32(pixels);
            long setTicks = System.Diagnostics.Stopwatch.GetTimestamp() - setStartTicks;
            long applyStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            if (pixelsChanged)
                fogTexture.Apply(false);
            long applyTicks = System.Diagnostics.Stopwatch.GetTimestamp() - applyStartTicks;
            long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - renderStartTicks;
            double elapsedMs = elapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (elapsedMs >= 30.0 || (logPerformanceDiagnostics && elapsedMs >= 4.0))
            {
                Debug.LogFormat(
                    LogType.Log,
                    LogOption.NoStacktrace,
                    null,
                    "[FOG3Perf] overlay total={0:F3}ms size={1}x{2} fill={3:F3}ms setPixels={4:F3}ms apply={5:F3}ms",
                    elapsedMs,
                    mapData.Width,
                    mapData.Height,
                    fillTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                    setTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                    applyTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            }
        }

        public bool AdvanceVisibilityFade(float deltaTime)
        {
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                throw new System.ArgumentOutOfRangeException(nameof(deltaTime), "FOG3 visibility fade delta time must be finite and non-negative.");
            if (!visibilityFadeActive || deltaTime <= 0f)
                return false;
            if (fogTexture == null || pixels == null || targetPixels == null || currentAlphas == null || targetAlphas == null)
                throw new System.InvalidOperationException("FOG3 overlay must be built before advancing visibility fade.");

            float maxDelta = visibilityFadeSpeed * deltaTime;
            bool pixelsChanged = false;
            bool remainsActive = false;
            for (int i = 0; i < currentAlphas.Length; i++)
            {
                float current = currentAlphas[i];
                float target = targetAlphas[i];
                if (current == target)
                    continue;

                float next = Mathf.MoveTowards(current, target, maxDelta);
                currentAlphas[i] = next;
                Color32 pixel = targetPixels[i];
                pixel.a = (byte)Mathf.RoundToInt(next * byte.MaxValue);
                pixels[i] = pixel;
                pixelsChanged = true;
                remainsActive |= next != target;
            }

            visibilityFadeActive = remainsActive;
            if (!pixelsChanged)
                return false;

            fogTexture.SetPixels32(pixels);
            fogTexture.Apply(false);
            return true;
        }

        public void GetTextureDiagnostics(
            out int width,
            out int height,
            out float averageR,
            out float averageG,
            out float averageB,
            out float averageA,
            out int alphaZero,
            out int alphaLow,
            out int alphaMid,
            out int alphaHigh,
            out int alphaFull)
        {
            width = fogTexture != null ? fogTexture.width : 0;
            height = fogTexture != null ? fogTexture.height : 0;
            averageR = 0f;
            averageG = 0f;
            averageB = 0f;
            averageA = 0f;
            alphaZero = 0;
            alphaLow = 0;
            alphaMid = 0;
            alphaHigh = 0;
            alphaFull = 0;

            if (pixels == null || pixels.Length == 0)
                return;

            long sumR = 0;
            long sumG = 0;
            long sumB = 0;
            long sumA = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                sumR += pixel.r;
                sumG += pixel.g;
                sumB += pixel.b;
                sumA += pixel.a;

                if (pixel.a == 0)
                    alphaZero++;
                else if (pixel.a == byte.MaxValue)
                    alphaFull++;
                else if (pixel.a < 64)
                    alphaLow++;
                else if (pixel.a < 192)
                    alphaMid++;
                else
                    alphaHigh++;
            }

            float divisor = pixels.Length * 255f;
            averageR = sumR / divisor;
            averageG = sumG / divisor;
            averageB = sumB / divisor;
            averageA = sumA / divisor;
        }

        private Color GetTargetPixelColor(Fog3MapData mapData, int x, int y)
        {
            Fog3CellState state = mapData.GetCellState(x, y);
            switch (state)
            {
                case Fog3CellState.Visible:
                    return settings.VisibleColor;
                case Fog3CellState.Explored:
                    return settings.ExploredColor;
                case Fog3CellState.Outside:
                    return settings.OutsideColor;
                default:
                    return settings.HiddenColor;
            }
        }

        private void CreateTexture(Fog3TerrainInfo terrainInfo)
        {
            int width = terrainInfo.Width;
            int height = terrainInfo.Height;
            fogTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = settings.TextureFilterMode
            };
            int cellCount = checked(width * height);
            pixels = new Color32[cellCount];
            targetPixels = new Color32[cellCount];
            currentAlphas = new float[cellCount];
            targetAlphas = new float[cellCount];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = x + y * width;
                    Color initialColor = terrainInfo.IsWalkable(x, y) ? settings.HiddenColor : settings.OutsideColor;
                    pixels[index] = initialColor;
                    targetPixels[index] = initialColor;
                    currentAlphas[index] = initialColor.a;
                    targetAlphas[index] = initialColor.a;
                }
            }

            fogTexture.SetPixels32(pixels);
            fogTexture.Apply(false);
            visibilityFadeActive = false;
        }

        private void CreateFogPlane(Fog3TerrainInfo terrainInfo)
        {
            GameObject plane = new GameObject("FOG3_WorldOverlay");
            plane.transform.SetParent(transform, false);
            ApplyLayer(plane);

            MeshFilter meshFilter = plane.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = plane.AddComponent<MeshRenderer>();
            fogMeshFilter = meshFilter;
            fogMeshBounds = CreateLocalTerrainBounds(terrainInfo);
            fogMeshUsesCameraProjectionGrid = ShouldProjectCloudLayerToCamera();
            if (settings.SurfaceMode == Fog3OverlaySurfaceMode.TerrainConforming)
                meshFilter.sharedMesh = CreateConformingTerrainMesh(terrainInfo, "FOG3_WorldOverlayMesh");
            else if (fogMeshUsesCameraProjectionGrid)
                meshFilter.sharedMesh = CreateCameraProjectedCloudMesh(terrainInfo, "FOG3_WorldOverlayMesh");
            else
                meshFilter.sharedMesh = CreateQuadMesh(fogMeshBounds, overlayHeight, "FOG3_WorldOverlayMesh");

            fogMaterial = CreateTransparentMaterial("FOG3_WorldOverlayMaterial", Color.white, 100);
            SetMainTexture(fogMaterial, fogTexture);
            fogMaterial.SetFloat("_FogCellSize", terrainInfo.CellSize);
            fogMaterial.SetFloat("_FogBoundaryFadeDistance", visibilityBoundaryFadeDistance);
            meshRenderer.sharedMaterial = fogMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        private void CreateOutsideMask(Fog3TerrainInfo terrainInfo)
        {
            float padding = Mathf.Max(0f, settings.OutsideMaskPadding);
            if (padding <= 0.01f)
                return;

            Bounds bounds = CreateLocalTerrainBounds(terrainInfo);
            float minX = bounds.min.x;
            float maxX = bounds.max.x;
            float minZ = bounds.min.z;
            float maxZ = bounds.max.z;
            float y = terrainInfo.BackgroundFogCoverWorldY.HasValue
                ? terrainInfo.BackgroundFogCoverWorldY.Value - terrainInfo.Origin.y + Mathf.Max(0f, settings.SurfaceOffset)
                : overlayHeight + 0.01f;
            float innerOverlap = Mathf.Max(0f, settings.OutsideMaskInnerOverlap);

            outsideMaterial = CreateTransparentMaterial("FOG3_OutsideMaskMaterial", settings.OutsideColor, 150);
            CreateOutsideQuad("FOG3_Outside_North", new Vector3(minX - padding, 0f, maxZ - innerOverlap), new Vector3(maxX + padding, 0f, maxZ + padding), y);
            CreateOutsideQuad("FOG3_Outside_South", new Vector3(minX - padding, 0f, minZ - padding), new Vector3(maxX + padding, 0f, minZ + innerOverlap), y);
            CreateOutsideQuad("FOG3_Outside_West", new Vector3(minX - padding, 0f, minZ - padding), new Vector3(minX + innerOverlap, 0f, maxZ + padding), y);
            CreateOutsideQuad("FOG3_Outside_East", new Vector3(maxX - innerOverlap, 0f, minZ - padding), new Vector3(maxX + padding, 0f, maxZ + padding), y);
        }

        private static Bounds CreateLocalTerrainBounds(Fog3TerrainInfo terrainInfo)
        {
            Vector3 size = new Vector3(terrainInfo.Width * terrainInfo.CellSize, 0f, terrainInfo.Height * terrainInfo.CellSize);
            return new Bounds(size * 0.5f, size);
        }

        private Mesh CreateConformingTerrainMesh(Fog3TerrainInfo terrainInfo, string meshName)
        {
            int cellCount = checked(terrainInfo.Width * terrainInfo.Height);
            var vertices = new System.Collections.Generic.List<Vector3>(cellCount * 4);
            var uvs = new System.Collections.Generic.List<Vector2>(cellCount * 4);
            var triangles = new System.Collections.Generic.List<int>(cellCount * 6);
            float surfaceOffset = Mathf.Max(0f, settings.SurfaceOffset);

            for (int z = 0; z < terrainInfo.Height; z++)
            {
                for (int x = 0; x < terrainInfo.Width; x++)
                {
                    ResolveConformingCellFootprint(
                        terrainInfo,
                        x,
                        z,
                        out float minX,
                        out float maxX,
                        out float minZ,
                        out float maxZ);

                    bool isSlope = terrainInfo.IsSlope(x, z);
                    float flatLocalY = 0f;
                    if (!isSlope)
                    {
                        float centerWorldX = terrainInfo.Origin.x + (minX + maxX) * 0.5f;
                        float centerWorldZ = terrainInfo.Origin.z + (minZ + maxZ) * 0.5f;
                        flatLocalY = SampleProjectionSourceHeight(terrainInfo, centerWorldX, centerWorldZ) +
                                     surfaceOffset - terrainInfo.Origin.y;
                    }

                    int vertexIndex = vertices.Count;
                    AddConformingTopVertex(terrainInfo, vertices, uvs, minX, minZ, minX, maxX, minZ, maxZ, isSlope, flatLocalY, (float)x / terrainInfo.Width, (float)z / terrainInfo.Height, surfaceOffset);
                    AddConformingTopVertex(terrainInfo, vertices, uvs, minX, maxZ, minX, maxX, minZ, maxZ, isSlope, flatLocalY, (float)x / terrainInfo.Width, (float)(z + 1) / terrainInfo.Height, surfaceOffset);
                    AddConformingTopVertex(terrainInfo, vertices, uvs, maxX, minZ, minX, maxX, minZ, maxZ, isSlope, flatLocalY, (float)(x + 1) / terrainInfo.Width, (float)z / terrainInfo.Height, surfaceOffset);
                    AddConformingTopVertex(terrainInfo, vertices, uvs, maxX, maxZ, minX, maxX, minZ, maxZ, isSlope, flatLocalY, (float)(x + 1) / terrainInfo.Width, (float)(z + 1) / terrainInfo.Height, surfaceOffset);

                    triangles.Add(vertexIndex);
                    triangles.Add(vertexIndex + 1);
                    triangles.Add(vertexIndex + 2);
                    triangles.Add(vertexIndex + 1);
                    triangles.Add(vertexIndex + 3);
                    triangles.Add(vertexIndex + 2);
                }
            }

            for (int x = 0; x + 1 < terrainInfo.Width; x++)
            {
                int previousWallVertexIndex = -1;
                for (int z = 0; z < terrainInfo.Height; z++)
                {
                    int left = checked((x + z * terrainInfo.Width) * 4);
                    int right = left + 4;
                    int wallVertexIndex = AddConformingCliffWall(
                        vertices,
                        uvs,
                        triangles,
                        left + 2,
                        left + 3,
                        right,
                        right + 1);
                    AddConformingCliffWallJunction(
                        vertices,
                        uvs,
                        triangles,
                        previousWallVertexIndex,
                        wallVertexIndex);
                    previousWallVertexIndex = wallVertexIndex;
                }
            }

            for (int z = 0; z + 1 < terrainInfo.Height; z++)
            {
                int previousWallVertexIndex = -1;
                for (int x = 0; x < terrainInfo.Width; x++)
                {
                    int lower = checked((x + z * terrainInfo.Width) * 4);
                    int upper = checked((x + (z + 1) * terrainInfo.Width) * 4);
                    int wallVertexIndex = AddConformingCliffWall(
                        vertices,
                        uvs,
                        triangles,
                        lower + 1,
                        lower + 3,
                        upper,
                        upper + 2);
                    AddConformingCliffWallJunction(
                        vertices,
                        uvs,
                        triangles,
                        previousWallVertexIndex,
                        wallVertexIndex);
                    previousWallVertexIndex = wallVertexIndex;
                }
            }

            Mesh mesh = new Mesh { name = meshName };
            if (vertices.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void AddConformingTopVertex(
            Fog3TerrainInfo terrainInfo,
            System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<Vector2> uvs,
            float localX,
            float localZ,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            bool sampleSlopeHeight,
            float flatLocalY,
            float uvX,
            float uvY,
            float surfaceOffset)
        {
            float localY = flatLocalY;
            if (sampleSlopeHeight)
            {
                float sampleInset = terrainInfo.CellSize * 0.01f;
                float sampleLocalX = Mathf.Clamp(
                    Mathf.Approximately(localX, minX) ? localX + sampleInset : localX - sampleInset,
                    minX,
                    maxX);
                float sampleLocalZ = Mathf.Clamp(
                    Mathf.Approximately(localZ, minZ) ? localZ + sampleInset : localZ - sampleInset,
                    minZ,
                    maxZ);
                float worldX = terrainInfo.Origin.x + sampleLocalX;
                float worldZ = terrainInfo.Origin.z + sampleLocalZ;
                localY = SampleProjectionSourceHeight(terrainInfo, worldX, worldZ) + surfaceOffset - terrainInfo.Origin.y;
            }

            vertices.Add(new Vector3(localX, localY, localZ));
            uvs.Add(new Vector2(uvX, uvY));
        }

        private static void ResolveConformingCellFootprint(
            Fog3TerrainInfo terrainInfo,
            int x,
            int z,
            out float minX,
            out float maxX,
            out float minZ,
            out float maxZ)
        {
            float cellSize = terrainInfo.CellSize;
            float edgeInset = terrainInfo.PlatformEdgeInset;
            minX = x * cellSize;
            maxX = (x + 1) * cellSize;
            minZ = z * cellSize;
            maxZ = (z + 1) * cellSize;
            if (edgeInset <= 0f)
                return;

            if (terrainInfo.IsSlope(x, z))
            {
                Fog3SlopeCellInfo slope = terrainInfo.GetSlopeCellInfo(x, z);
                if (!HasMatchingSlopeNeighbor(terrainInfo, x - slope.DirectionX, z - slope.DirectionY, slope))
                    ExtendSlopeFootprint(ref minX, ref maxX, ref minZ, ref maxZ, -slope.DirectionX, -slope.DirectionY, edgeInset);
                if (!HasMatchingSlopeNeighbor(terrainInfo, x + slope.DirectionX, z + slope.DirectionY, slope))
                    ExtendSlopeFootprint(ref minX, ref maxX, ref minZ, ref maxZ, slope.DirectionX, slope.DirectionY, edgeInset);
                return;
            }

            int platformHeight = terrainInfo.GetPlatformHeight(x, z);
            if (platformHeight < 0)
                return;

            if (!HasMatchingFlatPlatformNeighbor(terrainInfo, x - 1, z, platformHeight))
                minX += edgeInset;
            if (!HasMatchingFlatPlatformNeighbor(terrainInfo, x + 1, z, platformHeight))
                maxX -= edgeInset;
            if (!HasMatchingFlatPlatformNeighbor(terrainInfo, x, z - 1, platformHeight))
                minZ += edgeInset;
            if (!HasMatchingFlatPlatformNeighbor(terrainInfo, x, z + 1, platformHeight))
                maxZ -= edgeInset;
        }

        private static bool HasMatchingFlatPlatformNeighbor(Fog3TerrainInfo terrainInfo, int x, int z, int platformHeight)
        {
            return x >= 0 && x < terrainInfo.Width && z >= 0 && z < terrainInfo.Height &&
                   !terrainInfo.IsSlope(x, z) && terrainInfo.GetPlatformHeight(x, z) == platformHeight;
        }

        private static bool HasMatchingSlopeNeighbor(
            Fog3TerrainInfo terrainInfo,
            int x,
            int z,
            Fog3SlopeCellInfo slope)
        {
            if (x < 0 || x >= terrainInfo.Width || z < 0 || z >= terrainInfo.Height || !terrainInfo.IsSlope(x, z))
                return false;

            Fog3SlopeCellInfo neighbor = terrainInfo.GetSlopeCellInfo(x, z);
            return neighbor.DirectionX == slope.DirectionX &&
                   neighbor.DirectionY == slope.DirectionY &&
                   neighbor.Run == slope.Run &&
                   neighbor.Rise == slope.Rise;
        }

        private static void ExtendSlopeFootprint(
            ref float minX,
            ref float maxX,
            ref float minZ,
            ref float maxZ,
            int directionX,
            int directionZ,
            float extension)
        {
            if (directionX < 0)
                minX -= extension;
            else if (directionX > 0)
                maxX += extension;
            else if (directionZ < 0)
                minZ -= extension;
            else if (directionZ > 0)
                maxZ += extension;
        }

        private static int AddConformingCliffWall(
            System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<Vector2> uvs,
            System.Collections.Generic.List<int> triangles,
            int firstEdgeStart,
            int firstEdgeEnd,
            int secondEdgeStart,
            int secondEdgeEnd)
        {
            float firstHeight = (vertices[firstEdgeStart].y + vertices[firstEdgeEnd].y) * 0.5f;
            float secondHeight = (vertices[secondEdgeStart].y + vertices[secondEdgeEnd].y) * 0.5f;
            if (Mathf.Abs(firstHeight - secondHeight) <= 0.001f)
                return -1;

            bool firstIsHigher = firstHeight > secondHeight;
            int highStart = firstIsHigher ? firstEdgeStart : secondEdgeStart;
            int highEnd = firstIsHigher ? firstEdgeEnd : secondEdgeEnd;
            int lowStart = firstIsHigher ? secondEdgeStart : firstEdgeStart;
            int lowEnd = firstIsHigher ? secondEdgeEnd : firstEdgeEnd;
            Vector3 highStartPosition = vertices[highStart];
            Vector3 highEndPosition = vertices[highEnd];
            Vector3 lowStartPosition = vertices[lowStart];
            Vector3 lowEndPosition = vertices[lowEnd];
            int vertexIndex = vertices.Count;
            vertices.Add(highStartPosition);
            vertices.Add(highEndPosition);
            vertices.Add(lowStartPosition);
            vertices.Add(lowEndPosition);
            uvs.Add(uvs[highStart]);
            uvs.Add(uvs[highEnd]);
            uvs.Add(uvs[highStart]);
            uvs.Add(uvs[highEnd]);
            triangles.Add(vertexIndex);
            triangles.Add(vertexIndex + 1);
            triangles.Add(vertexIndex + 2);
            triangles.Add(vertexIndex + 1);
            triangles.Add(vertexIndex + 3);
            triangles.Add(vertexIndex + 2);
            return vertexIndex;
        }

        private static void AddConformingCliffWallJunction(
            System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<Vector2> uvs,
            System.Collections.Generic.List<int> triangles,
            int previousWallVertexIndex,
            int wallVertexIndex)
        {
            if (previousWallVertexIndex < 0 || wallVertexIndex < 0)
                return;

            Vector3 sharedHighEnd = vertices[previousWallVertexIndex + 1];
            Vector3 sharedHighStart = vertices[wallVertexIndex];
            if ((sharedHighEnd - sharedHighStart).sqrMagnitude > 0.000001f)
                return;

            Vector3 previousLowEnd = vertices[previousWallVertexIndex + 3];
            Vector3 lowStart = vertices[wallVertexIndex + 2];
            if (Vector3.Cross(previousLowEnd - sharedHighEnd, lowStart - sharedHighEnd).sqrMagnitude <= 0.000001f)
                return;

            int vertexIndex = vertices.Count;
            vertices.Add(sharedHighEnd);
            vertices.Add(previousLowEnd);
            vertices.Add(lowStart);
            uvs.Add(uvs[previousWallVertexIndex + 1]);
            uvs.Add(uvs[previousWallVertexIndex + 3]);
            uvs.Add(uvs[wallVertexIndex + 2]);
            triangles.Add(vertexIndex);
            triangles.Add(vertexIndex + 1);
            triangles.Add(vertexIndex + 2);
        }

        private Mesh CreateCameraProjectedCloudMesh(Fog3TerrainInfo terrainInfo, string meshName)
        {
            int vertexCount = terrainInfo.Width * terrainInfo.Height * 4;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];

            FillCameraProjectedCloudVertices(terrainInfo, vertices, uvs);

            Mesh mesh = new Mesh { name = meshName };
            if (vertexCount > 65000)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = CreateCameraProjectedCloudTriangles(terrainInfo);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void UpdateCameraProjectedCloudMesh(Mesh mesh, Fog3TerrainInfo terrainInfo)
        {
            int vertexCount = terrainInfo.Width * terrainInfo.Height * 4;
            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            bool topologyChanged = vertices == null || vertices.Length != vertexCount;
            if (topologyChanged)
                vertices = new Vector3[vertexCount];
            if (uvs == null || uvs.Length != vertexCount)
                uvs = new Vector2[vertexCount];

            FillCameraProjectedCloudVertices(terrainInfo, vertices, uvs);
            mesh.vertices = vertices;
            mesh.uv = uvs;
            if (topologyChanged)
                mesh.triangles = CreateCameraProjectedCloudTriangles(terrainInfo);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private void FillCameraProjectedCloudVertices(Fog3TerrainInfo terrainInfo, Vector3[] vertices, Vector2[] uvs)
        {
            float cellSize = terrainInfo.CellSize;
            float sampleInset = cellSize * 0.01f;
            int vertexIndex = 0;
            for (int z = 0; z < terrainInfo.Height; z++)
            {
                float localMinZ = z * cellSize;
                float localMaxZ = (z + 1) * cellSize;
                float sampleMinZ = localMinZ + sampleInset;
                float sampleMaxZ = localMaxZ - sampleInset;
                float uvMinY = (float)z / terrainInfo.Height;
                float uvMaxY = (float)(z + 1) / terrainInfo.Height;
                for (int x = 0; x < terrainInfo.Width; x++)
                {
                    float localMinX = x * cellSize;
                    float localMaxX = (x + 1) * cellSize;
                    float sampleMinX = localMinX + sampleInset;
                    float sampleMaxX = localMaxX - sampleInset;
                    float uvMinX = (float)x / terrainInfo.Width;
                    float uvMaxX = (float)(x + 1) / terrainInfo.Width;
                    bool isSlope = terrainInfo.IsSlope(x, z);
                    float flatSourceLocalY = 0f;
                    if (!isSlope)
                    {
                        float sampleCenterX = terrainInfo.Origin.x + (x + 0.5f) * cellSize;
                        float sampleCenterZ = terrainInfo.Origin.z + (z + 0.5f) * cellSize;
                        flatSourceLocalY = SampleProjectionSourceHeight(terrainInfo, sampleCenterX, sampleCenterZ) - terrainInfo.Origin.y;
                    }

                    FillCameraProjectedCloudVertex(
                        terrainInfo,
                        vertices,
                        uvs,
                        vertexIndex++,
                        localMinX,
                        localMinZ,
                        sampleMinX,
                        sampleMinZ,
                        isSlope,
                        flatSourceLocalY,
                        uvMinX,
                        uvMinY);
                    FillCameraProjectedCloudVertex(
                        terrainInfo,
                        vertices,
                        uvs,
                        vertexIndex++,
                        localMinX,
                        localMaxZ,
                        sampleMinX,
                        sampleMaxZ,
                        isSlope,
                        flatSourceLocalY,
                        uvMinX,
                        uvMaxY);
                    FillCameraProjectedCloudVertex(
                        terrainInfo,
                        vertices,
                        uvs,
                        vertexIndex++,
                        localMaxX,
                        localMinZ,
                        sampleMaxX,
                        sampleMinZ,
                        isSlope,
                        flatSourceLocalY,
                        uvMaxX,
                        uvMinY);
                    FillCameraProjectedCloudVertex(
                        terrainInfo,
                        vertices,
                        uvs,
                        vertexIndex++,
                        localMaxX,
                        localMaxZ,
                        sampleMaxX,
                        sampleMaxZ,
                        isSlope,
                        flatSourceLocalY,
                        uvMaxX,
                        uvMaxY);
                }
            }
        }

        private void FillCameraProjectedCloudVertex(
            Fog3TerrainInfo terrainInfo,
            Vector3[] vertices,
            Vector2[] uvs,
            int vertexIndex,
            float localX,
            float localZ,
            float sampleLocalX,
            float sampleLocalZ,
            bool sampleSlopeHeight,
            float flatSourceLocalY,
            float uvX,
            float uvY)
        {
            float sourceLocalY = flatSourceLocalY;
            if (sampleSlopeHeight)
            {
                float worldSampleX = terrainInfo.Origin.x + sampleLocalX;
                float worldSampleZ = terrainInfo.Origin.z + sampleLocalZ;
                sourceLocalY = SampleProjectionSourceHeight(terrainInfo, worldSampleX, worldSampleZ) - terrainInfo.Origin.y;
            }
            vertices[vertexIndex] = ProjectLocalPointToCloud(localX, sourceLocalY, overlayHeight, localZ);
            uvs[vertexIndex] = new Vector2(uvX, uvY);
        }

        private static int[] CreateCameraProjectedCloudTriangles(Fog3TerrainInfo terrainInfo)
        {
            int cellCount = terrainInfo.Width * terrainInfo.Height;
            int[] triangles = new int[cellCount * 6];
            for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
            {
                int vertexIndex = cellIndex * 4;
                int triangleIndex = cellIndex * 6;
                triangles[triangleIndex] = vertexIndex;
                triangles[triangleIndex + 1] = vertexIndex + 1;
                triangles[triangleIndex + 2] = vertexIndex + 2;
                triangles[triangleIndex + 3] = vertexIndex + 1;
                triangles[triangleIndex + 4] = vertexIndex + 3;
                triangles[triangleIndex + 5] = vertexIndex + 2;
            }
            return triangles;
        }

        private float SampleTerrainHeight(Fog3TerrainInfo terrainInfo, float worldX, float worldZ)
        {
            float fallbackY = terrainInfo.Origin.y + overlayHeight;
            float startHeight = Mathf.Max(1f, settings.HeightSampleStartHeight);
            float maxDistance = Mathf.Max(startHeight + 1f, settings.HeightSampleMaxDistance);
            Vector3 rayOrigin = new Vector3(worldX, terrainInfo.Origin.y + startHeight, worldZ);

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, maxDistance, heightSampleMask, QueryTriggerInteraction.Ignore))
                return hit.point.y + Mathf.Max(0f, settings.SurfaceOffset);

            return fallbackY;
        }

        private float SampleProjectionSourceHeight(Fog3TerrainInfo terrainInfo, float worldX, float worldZ)
        {
            float startHeight = Mathf.Max(1f, settings.HeightSampleStartHeight);
            float maxDistance = Mathf.Max(startHeight + 1f, settings.HeightSampleMaxDistance);
            Vector3 rayOrigin = new Vector3(worldX, terrainInfo.Origin.y + startHeight, worldZ);
            int hitCount = Physics.RaycastNonAlloc(
                rayOrigin,
                Vector3.down,
                projectionHeightHits,
                maxDistance,
                heightSampleMask,
                QueryTriggerInteraction.Ignore);
            if (hitCount == projectionHeightHits.Length)
            {
                throw new System.InvalidOperationException(
                    $"FOG3 terrain projection hit buffer overflowed at ({worldX:F2}, {worldZ:F2}). capacity={projectionHeightHits.Length}.");
            }

            int closestTerrainHit = -1;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = projectionHeightHits[i];
                UnityGameFramework.Runtime.EntityLogic entity = hit.collider.GetComponentInParent<UnityGameFramework.Runtime.EntityLogic>();
                if (entity != null && entity is not LevelEntity)
                    continue;
                if (hit.distance >= closestDistance)
                    continue;

                closestTerrainHit = i;
                closestDistance = hit.distance;
            }

            if (closestTerrainHit < 0)
            {
                if (terrainInfo.BackgroundFogCoverWorldY.HasValue)
                    return terrainInfo.BackgroundFogCoverWorldY.Value;

                throw new System.InvalidOperationException(
                    $"FOG3 terrain projection found no terrain surface at ({worldX:F2}, {worldZ:F2}). source={terrainInfo.SourceName}.");
            }

            return projectionHeightHits[closestTerrainHit].point.y;
        }

        private void CreateOutsideQuad(string objectName, Vector3 min, Vector3 max, float y)
        {
            Bounds bounds = new Bounds();
            bounds.SetMinMax(new Vector3(min.x, y, min.z), new Vector3(max.x, y, max.z));

            GameObject quad = new GameObject(objectName);
            quad.transform.SetParent(transform, false);
            ApplyLayer(quad);

            MeshFilter meshFilter = quad.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = quad.AddComponent<MeshRenderer>();
            meshFilter.sharedMesh = CreateQuadMesh(bounds, y, objectName + "_Mesh");
            meshRenderer.sharedMaterial = outsideMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            outsideMaskQuads.Add(new OutsideMaskQuad
            {
                MeshFilter = meshFilter,
                Bounds = bounds,
                Y = y
            });
        }

        private Mesh CreateQuadMesh(Bounds bounds, float y, string meshName)
        {
            Mesh mesh = new Mesh { name = meshName };
            mesh.vertices = CreateQuadVertices(bounds, y);
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void UpdateQuadMesh(Mesh mesh, Bounds bounds, float y)
        {
            mesh.vertices = CreateQuadVertices(bounds, y);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private Vector3[] CreateQuadVertices(Bounds bounds, float y)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            return new[]
            {
                CreateOverlayVertex(min.x, y, min.z),
                CreateOverlayVertex(max.x, y, min.z),
                CreateOverlayVertex(min.x, y, max.z),
                CreateOverlayVertex(max.x, y, max.z)
            };
        }

        private Vector3 CreateOverlayVertex(float localX, float localY, float localZ)
        {
            if (!ShouldProjectCloudLayerToCamera())
                return new Vector3(localX, localY, localZ);

            return ProjectLocalPointToCloud(localX, 0f, localY, localZ);
        }

        private Vector3 ProjectLocalPointToCloud(float localX, float sourceLocalY, float localCloudY, float localZ)
        {
            Vector3 fallback = new Vector3(localX, localCloudY, localZ);
            Camera referenceCamera = ResolveReferenceCamera();
            if (referenceCamera == null)
                return fallback;

            Vector3 sourceWorld = transform.TransformPoint(new Vector3(localX, sourceLocalY, localZ));
            float targetWorldY = transform.position.y + localCloudY;
            Vector3 projectedWorld;

            if (referenceCamera.orthographic)
            {
                Vector3 direction = referenceCamera.transform.forward;
                if (Mathf.Abs(direction.y) <= 0.0001f)
                    return fallback;

                float distance = (targetWorldY - sourceWorld.y) / direction.y;
                projectedWorld = sourceWorld + direction * distance;
            }
            else
            {
                Vector3 cameraPosition = referenceCamera.transform.position;
                Vector3 ray = sourceWorld - cameraPosition;
                if (Mathf.Abs(ray.y) <= 0.0001f)
                    return fallback;

                float t = (targetWorldY - cameraPosition.y) / ray.y;
                if (t <= 0f || float.IsNaN(t) || float.IsInfinity(t))
                    return fallback;

                projectedWorld = cameraPosition + ray * t;
            }

            Vector3 projectedLocal = transform.InverseTransformPoint(projectedWorld);
            if (float.IsNaN(projectedLocal.x) || float.IsNaN(projectedLocal.y) || float.IsNaN(projectedLocal.z))
                return fallback;

            if (float.IsInfinity(projectedLocal.x) || float.IsInfinity(projectedLocal.y) || float.IsInfinity(projectedLocal.z))
                return fallback;

            return projectedLocal;
        }

        private Material CreateTransparentMaterial(string materialName, Color color, int transparentQueueOffset)
        {
            Shader shader = settings.OverlayAlwaysOnTopShader;
            if (settings.DrawOverSceneGeometry && shader == null)
            {
                Debug.LogError("[FOG3] DrawOverSceneGeometry is enabled but OverlayAlwaysOnTopShader is not assigned.");
                return null;
            }

            if (shader == null)
                shader = AAAGame.Effect.EffectShaderAssetLoader.TryGet(FogOverlayShaderAssetPath);
            if (shader == null)
            {
                Debug.LogError($"[FOG3] Shader is not ready: {FogOverlayShaderAssetPath}");
                return null;
            }

            Material material = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.DontSave,
                renderQueue = (int)RenderQueue.Transparent + transparentQueueOffset
            };

            SetMaterialColor(material, color);
            ConfigureTransparent(material, settings.DrawOverSceneGeometry);
            return material;
        }

        private static void ConfigureTransparent(Material material, bool drawOverSceneGeometry)
        {
            if (material == null)
                return;

            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", drawOverSceneGeometry ? (int)CompareFunction.Always : (int)CompareFunction.LessEqual);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
                return;

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
        }

        private static void SetMainTexture(Material material, Texture texture)
        {
            if (material == null || texture == null)
                return;

            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
        }

        private void ApplyLayer(GameObject target)
        {
            if (settings.OverlayLayer >= 0 && settings.OverlayLayer <= 31)
                target.layer = settings.OverlayLayer;
        }

        private void ClearChildren()
        {
            fogMeshFilter = null;
            fogMeshBounds = default;
            fogMeshUsesCameraProjectionGrid = false;
            outsideMaskQuads.Clear();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                MeshFilter meshFilter = child.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                    DestroyUnityObjectSafe(meshFilter.sharedMesh);

                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        private void OnDestroy()
        {
            ReleaseRuntimeResources();
        }

        private void ReleaseRuntimeResources()
        {
            pixels = null;
            targetPixels = null;
            currentAlphas = null;
            targetAlphas = null;
            visibilityFadeActive = false;

            if (fogTexture != null)
            {
                DestroyUnityObjectSafe(fogTexture);
                fogTexture = null;
            }

            if (fogMaterial != null)
            {
                DestroyUnityObjectSafe(fogMaterial);
                fogMaterial = null;
            }

            if (outsideMaterial != null)
            {
                DestroyUnityObjectSafe(outsideMaterial);
                outsideMaterial = null;
            }
        }

        private static void DestroyUnityObjectSafe(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        private bool ShouldProjectCloudLayerToCamera()
        {
            return settings != null
                   && settings.SurfaceMode == Fog3OverlaySurfaceMode.CloudLayer
                   && settings.ProjectCloudLayerToCameraView;
        }

        private static Camera ResolveReferenceCamera()
        {
            Camera camera = Camera.main;
            if (IsUsableCamera(camera))
                return camera;

            Camera[] cameras = Camera.allCameras;
            Camera bestCamera = null;
            float bestDepth = float.NegativeInfinity;
            for (int i = 0; i < cameras.Length; i++)
            {
                camera = cameras[i];
                if (!IsUsableCamera(camera) || camera.depth < bestDepth)
                    continue;

                bestDepth = camera.depth;
                bestCamera = camera;
            }

            return bestCamera;
        }

        private static bool IsUsableCamera(Camera camera)
        {
            return camera != null && camera.isActiveAndEnabled && camera.gameObject.activeInHierarchy;
        }
    }
}
