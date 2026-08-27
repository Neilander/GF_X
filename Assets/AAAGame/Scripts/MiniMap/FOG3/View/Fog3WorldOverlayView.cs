using UnityEngine;
using UnityEngine.Rendering;

namespace AAAGame.MiniMap.FOG3
{
    public sealed class Fog3WorldOverlayView : MonoBehaviour
    {
        public const float MaximumBoundaryFadeCellRatio = 2f;
        private const int BoundarySampleKernelRadiusInLogicCells = 8;
        private const string FogOverlayShaderAssetPath = "Assets/AAAGame/Scripts/MiniMap/FOG3/View/Fog3OverlayAlwaysOnTop.shader";
        private Texture2D fogTexture;
        private RenderTexture fogPresentationTexture;
        private Texture2D fogUploadTexture;
        private Color32[] pixels;
        private Color[] presentationTransitions;
        private Color[] uploadPixels;
        private Color32[] targetPixels;
        private Fog3CellState[] targetStates;
        private Fog3CellState[] presentationStates;
        private float[] currentAlphas;
        private float[] targetAlphas;
        private float[] transitionStartAlphas;
        private float[] transitionStartTimes;
        private int logicWidth;
        private int logicHeight;
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
        private Camera presentationCamera;
        private Matrix4x4 presentationCameraViewProjection;
        private Rect presentationCameraPixelRect;
        private int cameraPresentationMinimumX;
        private int cameraPresentationMinimumY;
        private int cameraPresentationMaximumX = -1;
        private int cameraPresentationMaximumY = -1;
        private readonly System.Collections.Generic.List<OutsideMaskQuad> outsideMaskQuads = new System.Collections.Generic.List<OutsideMaskQuad>();

        private struct OutsideMaskQuad
        {
            public MeshFilter MeshFilter;
            public Bounds Bounds;
            public float Y;
        }

        public Texture2D FogTexture => fogTexture;
        public RenderTexture FogPresentationTexture => fogPresentationTexture;
        public Material FogMaterial => fogMaterial;
        public Material OutsideMaterial => outsideMaterial;
        public Bounds FogMeshBounds => fogMeshBounds;
        public int OutsideMaskQuadCount => outsideMaskQuads.Count;
        public bool FogMeshUsesCameraProjectionGrid => fogMeshUsesCameraProjectionGrid;
        public float VisibilityFadeSpeed => visibilityFadeSpeed;
        public float VisibilityBoundaryFadeDistance => visibilityBoundaryFadeDistance;

        public void Build(
            Fog3TerrainInfo terrainInfo,
            Fog3MapData mapData,
            Fog3ViewSettings viewSettings,
            float resolvedOverlayHeight,
            LayerMask resolvedHeightSampleMask,
            Vector3 worldOffset,
            float resolvedVisibilityFadeSpeed,
            float resolvedVisibilityBoundaryFadeDistance)
        {
            if (terrainInfo == null)
                throw new System.ArgumentNullException(nameof(terrainInfo));
            if (mapData == null)
                throw new System.ArgumentNullException(nameof(mapData));
            if (mapData.WorldOrigin != terrainInfo.Origin
                || Mathf.Abs(mapData.Bounds.size.x - terrainInfo.Bounds.size.x) > 0.001f
                || Mathf.Abs(mapData.Bounds.size.z - terrainInfo.Bounds.size.z) > 0.001f)
            {
                throw new System.ArgumentException(
                    $"FOG3 logic grid bounds must match terrain bounds. fog={mapData.Bounds}, terrain={terrainInfo.Bounds}.",
                    nameof(mapData));
            }
            if (resolvedVisibilityFadeSpeed <= 0f || float.IsNaN(resolvedVisibilityFadeSpeed) || float.IsInfinity(resolvedVisibilityFadeSpeed))
                throw new System.ArgumentOutOfRangeException(nameof(resolvedVisibilityFadeSpeed), "FOG3 visibility fade speed must be finite and positive.");
            if (resolvedVisibilityBoundaryFadeDistance <= 0f
                || resolvedVisibilityBoundaryFadeDistance > MaximumBoundaryFadeCellRatio
                || float.IsNaN(resolvedVisibilityBoundaryFadeDistance)
                || float.IsInfinity(resolvedVisibilityBoundaryFadeDistance))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(resolvedVisibilityBoundaryFadeDistance),
                    $"FOG3 visibility boundary fade distance must be finite, positive, and no greater than {MaximumBoundaryFadeCellRatio} fog cells.");
            }

            this.terrainInfo = terrainInfo;
            settings = viewSettings ?? new Fog3ViewSettings();
            if (settings.PresentationResolution <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(viewSettings), "FOG3 presentation resolution must be positive.");
            heightSampleMask = resolvedHeightSampleMask;
            overlayHeight = Mathf.Max(0f, resolvedOverlayHeight);
            visibilityFadeSpeed = resolvedVisibilityFadeSpeed;
            visibilityBoundaryFadeDistance = resolvedVisibilityBoundaryFadeDistance;
            SetWorldOffset(terrainInfo, worldOffset);
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            ClearChildren();
            ReleaseRuntimeResources();
            CreateTexture(mapData);
            CreateFogPlane(terrainInfo);
            CreateOutsideMask(terrainInfo);
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

        public bool RefreshCameraPresentation(Camera camera)
        {
            if (camera == null)
                throw new System.ArgumentNullException(nameof(camera));
            if (terrainInfo == null || fogPresentationTexture == null)
                throw new System.InvalidOperationException("FOG3 overlay must be built before refreshing camera presentation.");

            Matrix4x4 viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
            if (ReferenceEquals(presentationCamera, camera)
                && presentationCameraViewProjection == viewProjection
                && presentationCameraPixelRect == camera.pixelRect)
            {
                return false;
            }

            presentationCamera = camera;
            presentationCameraViewProjection = viewProjection;
            presentationCameraPixelRect = camera.pixelRect;
            ResolveCameraPresentationBounds(camera);
            if (cameraPresentationMaximumX < cameraPresentationMinimumX)
                return false;

            UploadPresentationRectangle(
                cameraPresentationMinimumX,
                cameraPresentationMinimumY,
                cameraPresentationMaximumX,
                cameraPresentationMaximumY);
            return true;
        }

        public void Render(Fog3MapData mapData, bool logPerformanceDiagnostics)
        {
            if (mapData == null)
                throw new System.ArgumentNullException(nameof(mapData));
            if (fogTexture == null || fogPresentationTexture == null || fogUploadTexture == null
                || pixels == null || presentationTransitions == null || uploadPixels == null
                || targetPixels == null || targetStates == null || presentationStates == null
                || currentAlphas == null || targetAlphas == null
                || transitionStartAlphas == null || transitionStartTimes == null)
                throw new System.InvalidOperationException("FOG3 overlay must be built before rendering visibility.");
            if (mapData.Width != fogTexture.width || mapData.Height != fogTexture.height)
            {
                throw new System.InvalidOperationException(
                    $"FOG3 overlay size mismatch. texture={fogTexture.width}x{fogTexture.height}, map={mapData.Width}x{mapData.Height}.");
            }
            if (!mapData.TryGetDirtyBounds(out int minimumX, out int minimumY, out int maximumX, out int maximumY))
                return;

            long renderStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            long fillStartTicks = renderStartTicks;
            float now = Time.time;
            int changedMinimumX = mapData.Width;
            int changedMinimumY = mapData.Height;
            int changedMaximumX = -1;
            int changedMaximumY = -1;
            for (int dirtyIndex = 0; dirtyIndex < mapData.DirtyCellCount; dirtyIndex++)
            {
                mapData.GetDirtyCell(dirtyIndex, out int x, out int y);
                int index = x + y * mapData.Width;
                Fog3CellState state = mapData.GetCellState(x, y);
                if (targetStates[index] == state)
                    continue;

                Color targetColor = GetTargetPixelColor(state);
                Color32 targetPixel = targetColor;
                float currentAlpha = ResolveTransitionAlpha(index, now);
                targetPixels[index] = targetPixel;
                targetStates[index] = state;
                transitionStartAlphas[index] = currentAlpha;
                transitionStartTimes[index] = now;
                currentAlphas[index] = currentAlpha;
                targetAlphas[index] = targetColor.a;
                targetPixel.a = (byte)Mathf.RoundToInt(currentAlpha * byte.MaxValue);
                pixels[index] = targetPixel;
                changedMinimumX = Mathf.Min(changedMinimumX, x);
                changedMinimumY = Mathf.Min(changedMinimumY, y);
                changedMaximumX = Mathf.Max(changedMaximumX, x);
                changedMaximumY = Mathf.Max(changedMaximumY, y);
            }

            long fillTicks = System.Diagnostics.Stopwatch.GetTimestamp() - fillStartTicks;
            long setStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            if (changedMaximumX >= changedMinimumX)
            {
                RefreshPresentationRegion(
                    changedMinimumX,
                    changedMinimumY,
                    changedMaximumX,
                    changedMaximumY,
                    now);
            }
            long setTicks = System.Diagnostics.Stopwatch.GetTimestamp() - setStartTicks;
            long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - renderStartTicks;
            double elapsedMs = elapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (elapsedMs >= 30.0 || (logPerformanceDiagnostics && elapsedMs >= 4.0))
            {
                Debug.LogFormat(
                    LogType.Log,
                    LogOption.NoStacktrace,
                    null,
                    "[FOG3Perf] overlay total={0:F3}ms dirtyCells={1} bounds=({2},{3})..({4},{5}) fill={6:F3}ms upload={7:F3}ms",
                    elapsedMs,
                    mapData.DirtyCellCount,
                    minimumX,
                    minimumY,
                    maximumX,
                    maximumY,
                    fillTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                    setTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            }
        }

        public bool AdvanceVisibilityFade(float deltaTime)
        {
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                throw new System.ArgumentOutOfRangeException(nameof(deltaTime), "FOG3 visibility fade delta time must be finite and non-negative.");
            if (deltaTime <= 0f)
                return false;
            if (fogTexture == null || pixels == null || targetPixels == null
                || currentAlphas == null || targetAlphas == null)
                throw new System.InvalidOperationException("FOG3 overlay must be built before advancing visibility fade.");

            float maxDelta = visibilityFadeSpeed * deltaTime;
            bool pixelsChanged = false;
            for (int i = 0; i < currentAlphas.Length; i++)
            {
                float current = currentAlphas[i];
                float target = targetAlphas[i];
                if (current == target)
                    continue;

                float next = Mathf.MoveTowards(current, target, maxDelta);
                currentAlphas[i] = next;
                transitionStartAlphas[i] = next;
                transitionStartTimes[i] = Time.time;
                Color32 pixel = targetPixels[i];
                pixel.a = (byte)Mathf.RoundToInt(next * byte.MaxValue);
                pixels[i] = pixel;
                pixelsChanged = true;
            }

            if (!pixelsChanged)
                return false;

            return true;
        }

        public float GetCurrentAlphaForDiagnostics(int x, int y)
        {
            if ((uint)x >= (uint)logicWidth || (uint)y >= (uint)logicHeight)
                throw new System.ArgumentOutOfRangeException(nameof(x));
            return ResolveTransitionAlpha(x + y * logicWidth, Time.time);
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
            float now = Time.time;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                pixel.a = (byte)Mathf.RoundToInt(ResolveTransitionAlpha(i, now) * byte.MaxValue);
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

        private Color GetTargetPixelColor(Fog3CellState state)
        {
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

        private void CreateTexture(Fog3MapData mapData)
        {
            int width = mapData.Width;
            int height = mapData.Height;
            logicWidth = width;
            logicHeight = height;
            int presentationWidth;
            int presentationHeight;
            if (width >= height)
            {
                presentationWidth = settings.PresentationResolution;
                presentationHeight = Mathf.Max(1, Mathf.RoundToInt(settings.PresentationResolution * (float)height / width));
            }
            else
            {
                presentationHeight = settings.PresentationResolution;
                presentationWidth = Mathf.Max(1, Mathf.RoundToInt(settings.PresentationResolution * (float)width / height));
            }
            fogTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            fogPresentationTexture = new RenderTexture(
                presentationWidth,
                presentationHeight,
                0,
                RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear)
            {
                name = "FOG3_PresentationTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
                useMipMap = false,
                autoGenerateMips = false
            };
            if (!fogPresentationTexture.Create())
                throw new System.InvalidOperationException("FOG3 presentation RenderTexture creation failed.");
            fogUploadTexture = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "FOG3_DirtyUploadTile",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            int cellCount = checked(width * height);
            pixels = new Color32[cellCount];
            int presentationCellCount = checked(presentationWidth * presentationHeight);
            presentationTransitions = new Color[presentationCellCount];
            uploadPixels = new Color[presentationCellCount];
            targetPixels = new Color32[cellCount];
            targetStates = new Fog3CellState[cellCount];
            presentationStates = new Fog3CellState[presentationCellCount];
            currentAlphas = new float[cellCount];
            targetAlphas = new float[cellCount];
            transitionStartAlphas = new float[cellCount];
            transitionStartTimes = new float[cellCount];
            float now = Time.time;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = x + y * width;
                    Fog3CellState initialState = mapData.GetCellState(x, y);
                    Color initialColor = GetTargetPixelColor(initialState);
                    Color32 initialPixel = initialColor;
                    pixels[index] = initialColor;
                    targetPixels[index] = initialColor;
                    targetStates[index] = initialState;
                    currentAlphas[index] = initialColor.a;
                    targetAlphas[index] = initialColor.a;
                    transitionStartAlphas[index] = initialColor.a;
                    transitionStartTimes[index] = now;
                }
            }

            fogTexture.SetPixels32(pixels);
            fogTexture.Apply(false);
            for (int y = 0; y < presentationHeight; y++)
            {
                int logicY = PresentationToLogic(y, presentationHeight, logicHeight);
                for (int x = 0; x < presentationWidth; x++)
                {
                    int logicX = PresentationToLogic(x, presentationWidth, logicWidth);
                    int logicIndex = logicX + logicY * logicWidth;
                    int presentationIndex = x + y * presentationWidth;
                    Fog3CellState state = targetStates[logicIndex];
                    float alpha = targetAlphas[logicIndex];
                    presentationStates[presentationIndex] = state;
                    presentationTransitions[presentationIndex] = PackPresentationTransition(alpha, state, alpha, now);
                }
            }
        }

        private void RefreshPresentationRegion(
            int logicMinimumX,
            int logicMinimumY,
            int logicMaximumX,
            int logicMaximumY,
            float now)
        {
            int presentationWidth = fogPresentationTexture.width;
            int presentationHeight = fogPresentationTexture.height;
            int minimumX = Mathf.Max(0, Mathf.FloorToInt(logicMinimumX * (float)presentationWidth / logicWidth) - 1);
            int minimumY = Mathf.Max(0, Mathf.FloorToInt(logicMinimumY * (float)presentationHeight / logicHeight) - 1);
            int maximumX = Mathf.Min(presentationWidth - 1, Mathf.CeilToInt((logicMaximumX + 1) * (float)presentationWidth / logicWidth) + 1);
            int maximumY = Mathf.Min(presentationHeight - 1, Mathf.CeilToInt((logicMaximumY + 1) * (float)presentationHeight / logicHeight) + 1);
            int changedMinimumX = presentationWidth;
            int changedMinimumY = presentationHeight;
            int changedMaximumX = -1;
            int changedMaximumY = -1;

            for (int y = minimumY; y <= maximumY; y++)
            {
                int logicY = PresentationToLogic(y, presentationHeight, logicHeight);
                for (int x = minimumX; x <= maximumX; x++)
                {
                    int logicX = PresentationToLogic(x, presentationWidth, logicWidth);
                    int logicIndex = logicX + logicY * logicWidth;
                    int presentationIndex = x + y * presentationWidth;
                    Fog3CellState state = targetStates[logicIndex];
                    if (presentationStates[presentationIndex] == state)
                        continue;

                    Color previous = presentationTransitions[presentationIndex];
                    float currentAlpha = ResolvePackedTransitionAlpha(previous, now);
                    presentationStates[presentationIndex] = state;
                    presentationTransitions[presentationIndex] = PackPresentationTransition(
                        targetAlphas[logicIndex],
                        state,
                        currentAlpha,
                        now);
                    changedMinimumX = Mathf.Min(changedMinimumX, x);
                    changedMinimumY = Mathf.Min(changedMinimumY, y);
                    changedMaximumX = Mathf.Max(changedMaximumX, x);
                    changedMaximumY = Mathf.Max(changedMaximumY, y);
                }
            }

            if (changedMaximumX < changedMinimumX || cameraPresentationMaximumX < cameraPresentationMinimumX)
                return;

            changedMinimumX = Mathf.Max(changedMinimumX, cameraPresentationMinimumX);
            changedMinimumY = Mathf.Max(changedMinimumY, cameraPresentationMinimumY);
            changedMaximumX = Mathf.Min(changedMaximumX, cameraPresentationMaximumX);
            changedMaximumY = Mathf.Min(changedMaximumY, cameraPresentationMaximumY);
            if (changedMaximumX >= changedMinimumX && changedMaximumY >= changedMinimumY)
                UploadPresentationRectangle(changedMinimumX, changedMinimumY, changedMaximumX, changedMaximumY);
        }

        private void ResolveCameraPresentationBounds(Camera camera)
        {
            float fogWorldY = transform.TransformPoint(new Vector3(0f, overlayHeight, 0f)).y;
            var plane = new Plane(Vector3.up, new Vector3(0f, fogWorldY, 0f));
            float minimumWorldX = float.PositiveInfinity;
            float minimumWorldZ = float.PositiveInfinity;
            float maximumWorldX = float.NegativeInfinity;
            float maximumWorldZ = float.NegativeInfinity;
            for (int corner = 0; corner < 4; corner++)
            {
                float viewportX = (corner & 1) == 0 ? 0f : 1f;
                float viewportY = (corner & 2) == 0 ? 0f : 1f;
                Ray ray = camera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0f));
                if (!plane.Raycast(ray, out float distance) || distance < 0f)
                {
                    SetFullCameraPresentationBounds();
                    return;
                }

                Vector3 point = ray.GetPoint(distance);
                minimumWorldX = Mathf.Min(minimumWorldX, point.x);
                minimumWorldZ = Mathf.Min(minimumWorldZ, point.z);
                maximumWorldX = Mathf.Max(maximumWorldX, point.x);
                maximumWorldZ = Mathf.Max(maximumWorldZ, point.z);
            }

            Bounds mapBounds = terrainInfo.Bounds;
            float minimumNormalizedX = (minimumWorldX - mapBounds.min.x) / mapBounds.size.x;
            float minimumNormalizedY = (minimumWorldZ - mapBounds.min.z) / mapBounds.size.z;
            float maximumNormalizedX = (maximumWorldX - mapBounds.min.x) / mapBounds.size.x;
            float maximumNormalizedY = (maximumWorldZ - mapBounds.min.z) / mapBounds.size.z;
            int paddingX = Mathf.CeilToInt(
                BoundarySampleKernelRadiusInLogicCells * (float)fogPresentationTexture.width / logicWidth);
            int paddingY = Mathf.CeilToInt(
                BoundarySampleKernelRadiusInLogicCells * (float)fogPresentationTexture.height / logicHeight);
            cameraPresentationMinimumX = Mathf.Clamp(
                Mathf.FloorToInt(minimumNormalizedX * fogPresentationTexture.width) - paddingX,
                0,
                fogPresentationTexture.width - 1);
            cameraPresentationMinimumY = Mathf.Clamp(
                Mathf.FloorToInt(minimumNormalizedY * fogPresentationTexture.height) - paddingY,
                0,
                fogPresentationTexture.height - 1);
            cameraPresentationMaximumX = Mathf.Clamp(
                Mathf.CeilToInt(maximumNormalizedX * fogPresentationTexture.width) + paddingX,
                0,
                fogPresentationTexture.width - 1);
            cameraPresentationMaximumY = Mathf.Clamp(
                Mathf.CeilToInt(maximumNormalizedY * fogPresentationTexture.height) + paddingY,
                0,
                fogPresentationTexture.height - 1);

            if (maximumNormalizedX < 0f || minimumNormalizedX > 1f
                || maximumNormalizedY < 0f || minimumNormalizedY > 1f)
            {
                cameraPresentationMinimumX = 0;
                cameraPresentationMinimumY = 0;
                cameraPresentationMaximumX = -1;
                cameraPresentationMaximumY = -1;
            }
        }

        private void SetFullCameraPresentationBounds()
        {
            cameraPresentationMinimumX = 0;
            cameraPresentationMinimumY = 0;
            cameraPresentationMaximumX = fogPresentationTexture.width - 1;
            cameraPresentationMaximumY = fogPresentationTexture.height - 1;
        }

        private void UploadPresentationRectangle(int minimumX, int minimumY, int maximumX, int maximumY)
        {
            int presentationWidth = fogPresentationTexture.width;
            int dirtyWidth = maximumX - minimumX + 1;
            int dirtyHeight = maximumY - minimumY + 1;
            for (int y = 0; y < dirtyHeight; y++)
            {
                int sourceOffset = minimumX + (minimumY + y) * presentationWidth;
                System.Array.Copy(presentationTransitions, sourceOffset, uploadPixels, y * dirtyWidth, dirtyWidth);
            }

            if (fogUploadTexture.width != dirtyWidth || fogUploadTexture.height != dirtyHeight)
                fogUploadTexture.Reinitialize(dirtyWidth, dirtyHeight, TextureFormat.RGBAFloat, false);
            fogUploadTexture.SetPixelData(uploadPixels, 0, 0);
            fogUploadTexture.Apply(false, false);
            Graphics.CopyTexture(
                fogUploadTexture,
                0,
                0,
                0,
                0,
                dirtyWidth,
                dirtyHeight,
                fogPresentationTexture,
                0,
                0,
                minimumX,
                minimumY);
        }

        private float ResolveTransitionAlpha(int index, float now)
        {
            return Mathf.MoveTowards(
                transitionStartAlphas[index],
                targetAlphas[index],
                visibilityFadeSpeed * Mathf.Max(0f, now - transitionStartTimes[index]));
        }

        private float ResolvePackedTransitionAlpha(Color transition, float now)
        {
            return Mathf.MoveTowards(
                transition.b,
                transition.r,
                visibilityFadeSpeed * Mathf.Max(0f, now - transition.a));
        }

        private static Color PackPresentationTransition(
            float targetAlpha,
            Fog3CellState state,
            float startAlpha,
            float startTime)
        {
            return new Color(targetAlpha, (byte)state / 255f, startAlpha, startTime);
        }

        private static int PresentationToLogic(int coordinate, int presentationSize, int logicSize)
        {
            return Mathf.Min(logicSize - 1, (int)(((long)coordinate * 2 + 1) * logicSize / (presentationSize * 2L)));
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
            SetMainTexture(fogMaterial, fogPresentationTexture);
            fogMaterial.SetFloat("_FogBoundaryFadeDistance", visibilityBoundaryFadeDistance);
            fogMaterial.SetFloat("_FogFadeSpeed", visibilityFadeSpeed);
            fogMaterial.SetFloat("_FogUsesTimedTransitions", 1f);
            fogMaterial.SetColor("_FogHiddenColor", settings.HiddenColor);
            fogMaterial.SetColor("_FogExploredColor", settings.ExploredColor);
            fogMaterial.SetColor("_FogVisibleColor", settings.VisibleColor);
            fogMaterial.SetColor("_FogOutsideColor", settings.OutsideColor);
            fogMaterial.SetVector(
                "_FogPresentationTexelScale",
                new Vector4(
                    (float)fogPresentationTexture.width / logicWidth,
                    (float)fogPresentationTexture.height / logicHeight,
                    0f,
                    0f));
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
            if (!Physics.Raycast(
                    rayOrigin,
                    Vector3.down,
                    out RaycastHit hit,
                    maxDistance,
                    heightSampleMask,
                    QueryTriggerInteraction.Ignore))
            {
                if (terrainInfo.BackgroundFogCoverWorldY.HasValue)
                    return terrainInfo.BackgroundFogCoverWorldY.Value;

                throw new System.InvalidOperationException(
                    $"FOG3 terrain projection found no terrain surface at ({worldX:F2}, {worldZ:F2}). source={terrainInfo.SourceName}.");
            }

            return hit.point.y;
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
            presentationTransitions = null;
            uploadPixels = null;
            targetPixels = null;
            targetStates = null;
            presentationStates = null;
            currentAlphas = null;
            targetAlphas = null;
            transitionStartAlphas = null;
            transitionStartTimes = null;
            logicWidth = 0;
            logicHeight = 0;
            presentationCamera = null;
            presentationCameraViewProjection = default;
            presentationCameraPixelRect = default;
            cameraPresentationMinimumX = 0;
            cameraPresentationMinimumY = 0;
            cameraPresentationMaximumX = -1;
            cameraPresentationMaximumY = -1;

            if (fogTexture != null)
            {
                DestroyUnityObjectSafe(fogTexture);
                fogTexture = null;
            }

            if (fogPresentationTexture != null)
            {
                fogPresentationTexture.Release();
                DestroyUnityObjectSafe(fogPresentationTexture);
                fogPresentationTexture = null;
            }

            if (fogUploadTexture != null)
            {
                DestroyUnityObjectSafe(fogUploadTexture);
                fogUploadTexture = null;
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
