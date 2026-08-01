using UnityEngine;
using UnityEngine.Rendering;

namespace AAAGame.MiniMap.FOG3
{
    public sealed class Fog3WorldOverlayView : MonoBehaviour
    {
        private const string FogOverlayShaderAssetPath = "Assets/AAAGame/Scripts/MiniMap/FOG3/View/Fog3OverlayAlwaysOnTop.shader";
        private Texture2D fogTexture;
        private Color32[] pixels;
        private Material fogMaterial;
        private Material outsideMaterial;
        private Fog3ViewSettings settings;
        private LayerMask heightSampleMask;
        private float overlayHeight;
        private Fog3TerrainInfo terrainInfo;
        private MeshFilter fogMeshFilter;
        private Bounds fogMeshBounds;
        private bool fogMeshUsesCameraProjectionGrid;
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

        public void Build(Fog3TerrainInfo terrainInfo, Fog3ViewSettings viewSettings, float resolvedOverlayHeight, LayerMask resolvedHeightSampleMask, Vector3 worldOffset)
        {
            this.terrainInfo = terrainInfo;
            settings = viewSettings ?? new Fog3ViewSettings();
            heightSampleMask = resolvedHeightSampleMask;
            overlayHeight = Mathf.Max(0f, resolvedOverlayHeight);
            SetWorldOffset(terrainInfo, worldOffset);
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            ClearChildren();
            ReleaseRuntimeResources();
            CreateTexture(terrainInfo.Width, terrainInfo.Height);
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

        public void Render(Fog3MapData mapData, bool logPerformanceDiagnostics)
        {
            if (mapData == null || fogTexture == null || pixels == null)
                return;

            long renderStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            long fillStartTicks = renderStartTicks;
            for (int y = 0; y < mapData.Height; y++)
            {
                for (int x = 0; x < mapData.Width; x++)
                {
                    int index = x + y * mapData.Width;
                    pixels[index] = GetPixelColor(mapData, x, y);
                }
            }

            long fillTicks = System.Diagnostics.Stopwatch.GetTimestamp() - fillStartTicks;
            long setStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            fogTexture.SetPixels32(pixels);
            long setTicks = System.Diagnostics.Stopwatch.GetTimestamp() - setStartTicks;
            long applyStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
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

        private Color32 GetPixelColor(Fog3MapData mapData, int x, int y)
        {
            Fog3CellState state = mapData.GetCellState(x, y);
            switch (state)
            {
                case Fog3CellState.Visible:
                    return Color.Lerp(settings.ExploredColor, settings.VisibleColor, mapData.GetVisibility(x, y));
                case Fog3CellState.Explored:
                    return settings.ExploredColor;
                case Fog3CellState.Outside:
                    return settings.OutsideColor;
                default:
                    return settings.HiddenColor;
            }
        }

        private void CreateTexture(int width, int height)
        {
            fogTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = settings.TextureFilterMode
            };
            pixels = new Color32[width * height];
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
            fogMeshUsesCameraProjectionGrid = false;
            if (settings.SurfaceMode == Fog3OverlaySurfaceMode.TerrainConforming)
                meshFilter.sharedMesh = CreateConformingTerrainMesh(terrainInfo, "FOG3_WorldOverlayMesh");
            else if (fogMeshUsesCameraProjectionGrid)
                meshFilter.sharedMesh = CreateCameraProjectedCloudMesh(terrainInfo, "FOG3_WorldOverlayMesh");
            else
                meshFilter.sharedMesh = CreateQuadMesh(fogMeshBounds, overlayHeight, "FOG3_WorldOverlayMesh");

            fogMaterial = CreateTransparentMaterial("FOG3_WorldOverlayMaterial", Color.white, 100);
            SetMainTexture(fogMaterial, fogTexture);
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
            float y = overlayHeight + 0.01f;
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
            int vertexWidth = terrainInfo.Width + 1;
            int vertexHeight = terrainInfo.Height + 1;
            int vertexCount = vertexWidth * vertexHeight;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];

            for (int z = 0; z < vertexHeight; z++)
            {
                for (int x = 0; x < vertexWidth; x++)
                {
                    int index = x + z * vertexWidth;
                    float localX = x * terrainInfo.CellSize;
                    float localZ = z * terrainInfo.CellSize;
                    float worldX = terrainInfo.Origin.x + localX;
                    float worldZ = terrainInfo.Origin.z + localZ;
                    float worldY = SampleTerrainHeight(terrainInfo, worldX, worldZ);
                    vertices[index] = new Vector3(localX, worldY - terrainInfo.Origin.y, localZ);
                    uvs[index] = new Vector2((float)x / terrainInfo.Width, (float)z / terrainInfo.Height);
                }
            }

            int[] triangles = new int[terrainInfo.Width * terrainInfo.Height * 6];
            int triangleIndex = 0;
            for (int z = 0; z < terrainInfo.Height; z++)
            {
                for (int x = 0; x < terrainInfo.Width; x++)
                {
                    int bottomLeft = x + z * vertexWidth;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = bottomLeft + vertexWidth;
                    int topRight = topLeft + 1;

                    triangles[triangleIndex++] = bottomLeft;
                    triangles[triangleIndex++] = topLeft;
                    triangles[triangleIndex++] = bottomRight;
                    triangles[triangleIndex++] = topLeft;
                    triangles[triangleIndex++] = topRight;
                    triangles[triangleIndex++] = bottomRight;
                }
            }

            Mesh mesh = new Mesh { name = meshName };
            if (vertexCount > 65000)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh CreateCameraProjectedCloudMesh(Fog3TerrainInfo terrainInfo, string meshName)
        {
            int vertexWidth = terrainInfo.Width + 1;
            int vertexHeight = terrainInfo.Height + 1;
            int vertexCount = vertexWidth * vertexHeight;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];

            FillCameraProjectedCloudVertices(terrainInfo, vertices, uvs);

            int[] triangles = new int[terrainInfo.Width * terrainInfo.Height * 6];
            int triangleIndex = 0;
            for (int z = 0; z < terrainInfo.Height; z++)
            {
                for (int x = 0; x < terrainInfo.Width; x++)
                {
                    int bottomLeft = x + z * vertexWidth;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = bottomLeft + vertexWidth;
                    int topRight = topLeft + 1;

                    triangles[triangleIndex++] = bottomLeft;
                    triangles[triangleIndex++] = topLeft;
                    triangles[triangleIndex++] = bottomRight;
                    triangles[triangleIndex++] = topLeft;
                    triangles[triangleIndex++] = topRight;
                    triangles[triangleIndex++] = bottomRight;
                }
            }

            Mesh mesh = new Mesh { name = meshName };
            if (vertexCount > 65000)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void UpdateCameraProjectedCloudMesh(Mesh mesh, Fog3TerrainInfo terrainInfo)
        {
            int vertexCount = (terrainInfo.Width + 1) * (terrainInfo.Height + 1);
            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            if (vertices == null || vertices.Length != vertexCount)
                vertices = new Vector3[vertexCount];
            if (uvs == null || uvs.Length != vertexCount)
                uvs = new Vector2[vertexCount];

            FillCameraProjectedCloudVertices(terrainInfo, vertices, uvs);
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private void FillCameraProjectedCloudVertices(Fog3TerrainInfo terrainInfo, Vector3[] vertices, Vector2[] uvs)
        {
            int vertexWidth = terrainInfo.Width + 1;
            int vertexHeight = terrainInfo.Height + 1;
            for (int z = 0; z < vertexHeight; z++)
            {
                for (int x = 0; x < vertexWidth; x++)
                {
                    int index = x + z * vertexWidth;
                    float localX = x * terrainInfo.CellSize;
                    float localZ = z * terrainInfo.CellSize;
                    float worldX = terrainInfo.Origin.x + localX;
                    float worldZ = terrainInfo.Origin.z + localZ;
                    float sourceLocalY = SampleProjectionSourceHeight(terrainInfo, worldX, worldZ) - terrainInfo.Origin.y;
                    vertices[index] = ProjectLocalPointToCloud(localX, sourceLocalY, overlayHeight, localZ);
                    uvs[index] = new Vector2((float)x / terrainInfo.Width, (float)z / terrainInfo.Height);
                }
            }
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

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, maxDistance, heightSampleMask, QueryTriggerInteraction.Ignore))
                return hit.point.y;

            return terrainInfo.Origin.y;
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
            Shader shader = settings.DrawOverSceneGeometry ? settings.OverlayAlwaysOnTopShader : null;
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
            return false;
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
