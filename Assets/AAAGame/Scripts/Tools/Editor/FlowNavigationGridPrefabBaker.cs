using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AAAGame.Tilemap;
using GiantGrey.TileWorldCreator;
using UnityEditor;
using UnityEngine;

namespace AAAGame.Tools.Editor
{
    public static class FlowNavigationGridPrefabBaker
    {
        private const string GroundLayerName = "Ground";
        private const string LevelObstacleLayerName = "LevelObstacle";
        private const string FlowFieldNavigationConfigPath = "Assets/AAAGame/SOs/FlowFieldNavigationConfig.asset";
        private const float MinimumFootprintSampleRadius = 0.05f;
        private const float BoundaryEpsilon = 0.01f;
        private const int BakeProgressLogRows = 16;
        private const float TerrainBoundsPaddingCells = 1f;
        private const float DistanceTransformInfinity = 1.0e20f;
        private static readonly int[] NeighborOffsetX = { -1, 0, 1, -1, 1, -1, 0, 1 };
        private static readonly int[] NeighborOffsetY = { -1, -1, -1, 0, 0, 1, 1, 1 };
        private static readonly Vector2[] UnitFootprintSamples =
        {
            Vector2.zero,
            new Vector2(1f, 0f),
            new Vector2(-1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, -1f),
            new Vector2(0.7071068f, 0.7071068f),
            new Vector2(-0.7071068f, 0.7071068f),
            new Vector2(0.7071068f, -0.7071068f),
            new Vector2(-0.7071068f, -0.7071068f)
        };
        private static readonly Vector2[] CellAnchorSampleOffsets =
        {
            Vector2.zero,
            new Vector2(0.25f, 0f),
            new Vector2(-0.25f, 0f),
            new Vector2(0f, 0.25f),
            new Vector2(0f, -0.25f),
            new Vector2(0.25f, 0.25f),
            new Vector2(-0.25f, 0.25f),
            new Vector2(0.25f, -0.25f),
            new Vector2(-0.25f, -0.25f),
            new Vector2(0.45f, 0f),
            new Vector2(-0.45f, 0f),
            new Vector2(0f, 0.45f),
            new Vector2(0f, -0.45f),
            new Vector2(0.45f, 0.25f),
            new Vector2(0.45f, -0.25f),
            new Vector2(-0.45f, 0.25f),
            new Vector2(-0.45f, -0.25f),
            new Vector2(0.25f, 0.45f),
            new Vector2(-0.25f, 0.45f),
            new Vector2(0.25f, -0.45f),
            new Vector2(-0.25f, -0.45f),
            new Vector2(0.45f, 0.45f),
            new Vector2(-0.45f, 0.45f),
            new Vector2(0.45f, -0.45f),
            new Vector2(-0.45f, -0.45f)
        };

        public struct Result
        {
            public FlowNavigationGridAsset Asset;
            public string AssetPath;
            public int AgentTypeId;
            public int Width;
            public int Height;
            public int WalkableCount;
            public int BlockedCount;
            public int GroundColliderCount;
            public int ObstacleColliderCount;
        }

        public struct MovementTypeBakeRequest
        {
            public int AgentTypeId;
            public string AssetPath;
            public float HardClearanceRadius;

            public MovementTypeBakeRequest(int agentTypeId, string assetPath, float hardClearanceRadius)
            {
                AgentTypeId = agentTypeId;
                AssetPath = assetPath;
                HardClearanceRadius = hardClearanceRadius;
            }
        }

        public readonly struct StaticObstacleBakeInstance
        {
            public readonly string PrefabPath;
            public readonly Vector3 Position;
            public readonly string Identifier;

            public StaticObstacleBakeInstance(string prefabPath, Vector3 position, string identifier)
            {
                PrefabPath = prefabPath;
                Position = position;
                Identifier = identifier;
            }
        }

        public readonly struct TerrainBakeTransform
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;

            public TerrainBakeTransform(Vector3 position, Quaternion rotation, Vector3 scale)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }

            public static TerrainBakeTransform Identity => new TerrainBakeTransform(
                Vector3.zero,
                Quaternion.identity,
                Vector3.one);
        }

        public static Result BakeFromTerrainPrefab(
            string terrainPrefabPath,
            string assetPath,
            int width,
            int height,
            float cellSize,
            Vector3 gridOrigin)
        {
            return BakeFromTerrainPrefab(
                terrainPrefabPath,
                assetPath,
                int.MinValue + 1,
                0f,
                width,
                height,
                cellSize,
                gridOrigin);
        }

        public static Result BakeFromTerrainPrefab(
            string terrainPrefabPath,
            string assetPath,
            int agentTypeId,
            float hardClearanceRadius,
            int width,
            int height,
            float cellSize,
            Vector3 gridOrigin)
        {
            if (string.IsNullOrWhiteSpace(terrainPrefabPath))
                throw new InvalidOperationException("FlowNavigationGridPrefabBaker.BakeFromTerrainPrefab failed: terrainPrefabPath is empty.");
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new InvalidOperationException("FlowNavigationGridPrefabBaker.BakeFromTerrainPrefab failed: assetPath is empty.");
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.BakeFromTerrainPrefab failed: invalid size {width}x{height}.");
            if (cellSize <= 0.0001f)
                throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.BakeFromTerrainPrefab failed: invalid cellSize={cellSize:F4}.");

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Debug.Log(
                $"[FlowNavigationGridBake] stage=start terrain={terrainPrefabPath} asset={assetPath} agentType={agentTypeId} " +
                $"size={width}x{height} cellSize={cellSize:F4} radius={hardClearanceRadius:F4} origin=({gridOrigin.x:F3},{gridOrigin.y:F3},{gridOrigin.z:F3})");

            GameObject terrainPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(terrainPrefabPath);
            if (terrainPrefab == null)
                throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.BakeFromTerrainPrefab failed: terrain prefab not found at {terrainPrefabPath}.");

            EnsureAssetFolder(assetPath);
            Debug.Log($"[FlowNavigationGridBake] stage=asset-folder-ready elapsedMs={stopwatch.ElapsedMilliseconds} asset={assetPath}");

            GameObject terrainRoot = PrefabUtility.LoadPrefabContents(terrainPrefabPath);
            Debug.Log($"[FlowNavigationGridBake] stage=prefab-loaded elapsedMs={stopwatch.ElapsedMilliseconds} terrain={terrainPrefabPath}");
            try
            {
                int groundLayer = RequireLayer(GroundLayerName);
                int obstacleLayer = LayerMask.NameToLayer(LevelObstacleLayerName);
                Collider[] groundColliders = CollectColliders(terrainRoot, groundLayer);
                Collider[] obstacleColliders = obstacleLayer >= 0
                    ? CollectColliders(terrainRoot, obstacleLayer)
                    : Array.Empty<Collider>();
                ColliderSpatialIndex groundIndex = new ColliderSpatialIndex(groundColliders, cellSize);
                ColliderSpatialIndex obstacleIndex = new ColliderSpatialIndex(obstacleColliders, cellSize);
                Debug.Log(
                    $"[FlowNavigationGridBake] stage=colliders-collected elapsedMs={stopwatch.ElapsedMilliseconds} " +
                    $"agentType={agentTypeId} groundLayer={groundLayer} obstacleLayer={obstacleLayer} " +
                    $"groundColliders={groundColliders.Length} obstacleColliders={obstacleColliders.Length}");

                if (groundColliders.Length == 0)
                    throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.BakeFromTerrainPrefab failed: no enabled non-trigger collider on layer '{GroundLayerName}' in {terrainPrefabPath}.");

                TerrainRaster raster = BuildTerrainRaster(groundIndex, obstacleIndex, width, height, cellSize, gridOrigin, agentTypeId, stopwatch);
                AuthoredTerrainTopology terrainTopology = AuthoredTerrainTopology.TryCreate(terrainRoot);
                return BakeMovementTypeFromRaster(
                    terrainPrefabPath,
                    assetPath,
                    agentTypeId,
                    hardClearanceRadius,
                    raster,
                    groundIndex,
                    obstacleIndex,
                    terrainTopology,
                    groundColliders.Length,
                    obstacleColliders.Length,
                    stopwatch);
            }
            finally
            {
                Debug.Log($"[FlowNavigationGridBake] stage=unload-prefab elapsedMs={stopwatch.ElapsedMilliseconds} agentType={agentTypeId} terrain={terrainPrefabPath}");
                PrefabUtility.UnloadPrefabContents(terrainRoot);
            }
        }

        public static Result[] BakeMovementTypesFromTerrainPrefab(
            string terrainPrefabPath,
            IReadOnlyList<MovementTypeBakeRequest> requests,
            float cellSize)
        {
            return BakeMovementTypesFromTerrainPrefab(
                terrainPrefabPath,
                requests,
                cellSize,
                Array.Empty<StaticObstacleBakeInstance>());
        }

        public static Result[] BakeMovementTypesFromTerrainPrefab(
            string terrainPrefabPath,
            IReadOnlyList<MovementTypeBakeRequest> requests,
            float cellSize,
            IReadOnlyList<StaticObstacleBakeInstance> staticObstacleBakeInstances)
        {
            return BakeMovementTypesFromTerrainPrefab(
                terrainPrefabPath,
                requests,
                cellSize,
                staticObstacleBakeInstances,
                TerrainBakeTransform.Identity);
        }

        public static Result[] BakeMovementTypesFromTerrainPrefab(
            string terrainPrefabPath,
            IReadOnlyList<MovementTypeBakeRequest> requests,
            float cellSize,
            IReadOnlyList<StaticObstacleBakeInstance> staticObstacleBakeInstances,
            TerrainBakeTransform terrainTransform)
        {
            if (requests == null)
                throw new InvalidOperationException("FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab failed: requests is null.");
            if (requests.Count == 0)
                throw new InvalidOperationException("FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab failed: requests is empty.");
            if (cellSize <= 0.0001f)
                throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab failed: invalid cellSize={cellSize:F4}.");
            if (string.IsNullOrWhiteSpace(terrainPrefabPath))
                throw new InvalidOperationException("FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab failed: terrainPrefabPath is empty.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(terrainPrefabPath) == null)
                throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab failed: terrain prefab not found at {terrainPrefabPath}.");

            GameObject terrainRoot = PrefabUtility.LoadPrefabContents(terrainPrefabPath);
            try
            {
                ApplyTerrainBakeTransform(terrainRoot, terrainTransform);
                int groundLayer = RequireLayer(GroundLayerName);
                Collider[] groundColliders = CollectColliders(terrainRoot, groundLayer);
                if (groundColliders.Length == 0)
                    throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab failed: no enabled non-trigger collider on layer '{GroundLayerName}' in {terrainPrefabPath}.");

                Bounds bounds = ResolveColliderBounds(groundColliders);
                float padding = Mathf.Max(cellSize, cellSize * TerrainBoundsPaddingCells);
                float minX = Mathf.Floor((bounds.min.x - padding) / cellSize) * cellSize;
                float minZ = Mathf.Floor((bounds.min.z - padding) / cellSize) * cellSize;
                float maxX = Mathf.Ceil((bounds.max.x + padding) / cellSize) * cellSize;
                float maxZ = Mathf.Ceil((bounds.max.z + padding) / cellSize) * cellSize;
                int width = Mathf.CeilToInt((maxX - minX) / cellSize);
                int height = Mathf.CeilToInt((maxZ - minZ) / cellSize);
                Vector3 origin = new Vector3(minX, 0f, minZ);
                if (width <= 0 || height <= 0)
                    throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab failed: invalid collider-derived grid size {width}x{height} bounds={bounds} cellSize={cellSize:F4}.");

                Debug.Log(
                    $"[FlowNavigationGridBake] stage=terrain-bounds terrain={terrainPrefabPath} boundsMin=({bounds.min.x:F3},{bounds.min.z:F3}) " +
                    $"boundsMax=({bounds.max.x:F3},{bounds.max.z:F3}) grid={width}x{height} cellSize={cellSize:F4} origin=({origin.x:F3},{origin.y:F3},{origin.z:F3})");

                return BakeMovementTypesFromTerrainPrefab(
                    terrainPrefabPath,
                    requests,
                    width,
                    height,
                    cellSize,
                    origin,
                    staticObstacleBakeInstances,
                    terrainTransform);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(terrainRoot);
            }
        }

        public static Result[] BakeMovementTypesFromTerrainPrefab(
            string terrainPrefabPath,
            IReadOnlyList<MovementTypeBakeRequest> requests,
            int width,
            int height,
            float cellSize,
            Vector3 gridOrigin)
        {
            return BakeMovementTypesFromTerrainPrefab(
                terrainPrefabPath,
                requests,
                width,
                height,
                cellSize,
                gridOrigin,
                Array.Empty<StaticObstacleBakeInstance>());
        }

        public static Result[] BakeMovementTypesFromTerrainPrefab(
            string terrainPrefabPath,
            IReadOnlyList<MovementTypeBakeRequest> requests,
            int width,
            int height,
            float cellSize,
            Vector3 gridOrigin,
            IReadOnlyList<StaticObstacleBakeInstance> staticObstacleBakeInstances)
        {
            return BakeMovementTypesFromTerrainPrefab(
                terrainPrefabPath,
                requests,
                width,
                height,
                cellSize,
                gridOrigin,
                staticObstacleBakeInstances,
                TerrainBakeTransform.Identity);
        }

        private static Result[] BakeMovementTypesFromTerrainPrefab(
            string terrainPrefabPath,
            IReadOnlyList<MovementTypeBakeRequest> requests,
            int width,
            int height,
            float cellSize,
            Vector3 gridOrigin,
            IReadOnlyList<StaticObstacleBakeInstance> staticObstacleBakeInstances,
            TerrainBakeTransform terrainTransform)
        {
            if (requests == null)
                throw new InvalidOperationException("FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab failed: requests is null.");
            if (requests.Count == 0)
                throw new InvalidOperationException("FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab failed: requests is empty.");

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Debug.Log(
                $"[FlowNavigationGridBake] stage=movement-types-start terrain={terrainPrefabPath} requestCount={requests.Count} " +
                $"size={width}x{height} cellSize={cellSize:F4}");

            GameObject terrainRoot = PrefabUtility.LoadPrefabContents(terrainPrefabPath);
            try
            {
                ApplyTerrainBakeTransform(terrainRoot, terrainTransform);
                int groundLayer = RequireLayer(GroundLayerName);
                int obstacleLayer = LayerMask.NameToLayer(LevelObstacleLayerName);
                Collider[] groundColliders = CollectColliders(terrainRoot, groundLayer);
                Collider[] obstacleColliders = obstacleLayer >= 0
                    ? CollectColliders(terrainRoot, obstacleLayer)
                    : Array.Empty<Collider>();
                GameObject[] staticObstacleRoots = InstantiateStaticObstacleBakeInstances(terrainRoot, staticObstacleBakeInstances);
                if (staticObstacleRoots.Length > 0)
                    Physics.SyncTransforms();
                Collider[] staticObstacleColliders = CollectColliders(staticObstacleRoots);
                Collider[] combinedObstacleColliders = CombineColliders(obstacleColliders, staticObstacleColliders);
                if (groundColliders.Length == 0)
                    throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab failed: no enabled non-trigger collider on layer '{GroundLayerName}' in {terrainPrefabPath}.");

                ColliderSpatialIndex groundIndex = new ColliderSpatialIndex(groundColliders, cellSize);
                ColliderSpatialIndex obstacleIndex = new ColliderSpatialIndex(combinedObstacleColliders, cellSize);
                Debug.Log(
                    $"[FlowNavigationGridBake] stage=colliders-collected elapsedMs={stopwatch.ElapsedMilliseconds} " +
                    $"groundLayer={groundLayer} obstacleLayer={obstacleLayer} groundColliders={groundColliders.Length} " +
                    $"obstacleColliders={obstacleColliders.Length} staticObstacleInstances={staticObstacleRoots.Length} " +
                    $"staticObstacleColliders={staticObstacleColliders.Length} combinedObstacleColliders={combinedObstacleColliders.Length}");

                TerrainRaster raster = BuildTerrainRaster(groundIndex, obstacleIndex, width, height, cellSize, gridOrigin, int.MinValue, stopwatch);
                AuthoredTerrainTopology terrainTopology = AuthoredTerrainTopology.TryCreate(terrainRoot);
                Result[] results = new Result[requests.Count];
                HashSet<int> agentTypeIds = new HashSet<int>();
                for (int i = 0; i < requests.Count; i++)
                {
                    MovementTypeBakeRequest request = requests[i];
                    if (!agentTypeIds.Add(request.AgentTypeId))
                        throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab failed: duplicate agentTypeId={request.AgentTypeId}.");

                    Debug.Log(
                        $"[FlowNavigationGridBake] stage=movement-type-start elapsedMs={stopwatch.ElapsedMilliseconds} " +
                        $"index={i + 1}/{requests.Count} agentType={request.AgentTypeId} radius={request.HardClearanceRadius:F4} asset={request.AssetPath}");
                    results[i] = BakeMovementTypeFromRaster(
                        terrainPrefabPath,
                        request.AssetPath,
                        request.AgentTypeId,
                        request.HardClearanceRadius,
                        raster,
                        groundIndex,
                        obstacleIndex,
                        terrainTopology,
                        groundColliders.Length,
                        combinedObstacleColliders.Length,
                        stopwatch);
                    Debug.Log(
                        $"[FlowNavigationGridBake] stage=movement-type-complete elapsedMs={stopwatch.ElapsedMilliseconds} " +
                        $"index={i + 1}/{requests.Count} agentType={request.AgentTypeId} walkable={results[i].WalkableCount} blocked={results[i].BlockedCount}");
                }

                Debug.Log($"[FlowNavigationGridBake] stage=movement-types-complete elapsedMs={stopwatch.ElapsedMilliseconds} terrain={terrainPrefabPath}");
                return results;
            }
            finally
            {
                Debug.Log($"[FlowNavigationGridBake] stage=unload-prefab elapsedMs={stopwatch.ElapsedMilliseconds} terrain={terrainPrefabPath}");
                PrefabUtility.UnloadPrefabContents(terrainRoot);
            }
        }

        public static void AttachSourceToLevelPrefab(GameObject prefabRoot, FlowNavigationGridAsset grid)
        {
            AttachSourceToLevelPrefab(prefabRoot, grid, Array.Empty<FlowNavigationGridAsset>());
        }

        public static void AttachSourceToLevelPrefab(GameObject prefabRoot, FlowNavigationGridAsset grid, IReadOnlyList<FlowNavigationGridAsset> movementTypeGrids)
        {
            if (prefabRoot == null)
                throw new InvalidOperationException("FlowNavigationGridPrefabBaker.AttachSourceToLevelPrefab failed: prefabRoot is null.");
            if (grid == null)
                throw new InvalidOperationException("FlowNavigationGridPrefabBaker.AttachSourceToLevelPrefab failed: grid is null.");

            FlowNavigationGridSource[] sources = prefabRoot.GetComponentsInChildren<FlowNavigationGridSource>(true);
            if (sources.Length > 1)
                throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.AttachSourceToLevelPrefab failed: multiple FlowNavigationGridSource components under {prefabRoot.name}.");

            FlowNavigationGridSource source = sources.Length == 1
                ? sources[0]
                : prefabRoot.AddComponent<FlowNavigationGridSource>();
            source.Configure(grid, movementTypeGrids, applyOnEnable: true, applyInEditMode: false, clearOnDisable: true);
            EditorUtility.SetDirty(source);
            EditorUtility.SetDirty(prefabRoot);
        }

        private static int RequireLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
                throw new InvalidOperationException($"FlowNavigationGridPrefabBaker failed: Unity layer '{layerName}' is not defined.");
            return layer;
        }

        private static Collider[] CollectColliders(GameObject root, int layer)
        {
            return root.GetComponentsInChildren<Collider>(true)
                .Where(x => x != null && x.enabled && !x.isTrigger && x.gameObject.layer == layer)
                .ToArray();
        }

        private static Collider[] CollectColliders(IReadOnlyList<GameObject> roots)
        {
            if (roots == null || roots.Count == 0)
                return Array.Empty<Collider>();

            List<Collider> colliders = new List<Collider>();
            for (int i = 0; i < roots.Count; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                    continue;

                colliders.AddRange(root.GetComponentsInChildren<Collider>(true)
                    .Where(x => x != null && x.enabled && !x.isTrigger));
            }

            return colliders.ToArray();
        }

        private static Collider[] CombineColliders(IReadOnlyList<Collider> a, IReadOnlyList<Collider> b)
        {
            int aCount = a != null ? a.Count : 0;
            int bCount = b != null ? b.Count : 0;
            if (aCount == 0 && bCount == 0)
                return Array.Empty<Collider>();

            Collider[] combined = new Collider[aCount + bCount];
            for (int i = 0; i < aCount; i++)
                combined[i] = a[i];
            for (int i = 0; i < bCount; i++)
                combined[aCount + i] = b[i];
            return combined;
        }

        private static GameObject[] InstantiateStaticObstacleBakeInstances(GameObject parentRoot, IReadOnlyList<StaticObstacleBakeInstance> instances)
        {
            if (instances == null || instances.Count == 0)
                return Array.Empty<GameObject>();
            if (parentRoot == null)
                throw new InvalidOperationException("InstantiateStaticObstacleBakeInstances failed: parentRoot is null.");

            List<GameObject> roots = new List<GameObject>(instances.Count);
            Transform parent = parentRoot.transform;
            for (int i = 0; i < instances.Count; i++)
            {
                StaticObstacleBakeInstance instance = instances[i];
                if (string.IsNullOrWhiteSpace(instance.PrefabPath))
                    throw new InvalidOperationException($"InstantiateStaticObstacleBakeInstances failed: prefab path is empty index={i} identifier={instance.Identifier}.");

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(instance.PrefabPath);
                if (prefab == null)
                    throw new InvalidOperationException($"InstantiateStaticObstacleBakeInstances failed: prefab not found path={instance.PrefabPath} identifier={instance.Identifier}.");

                GameObject obstacleRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                if (obstacleRoot == null)
                    obstacleRoot = UnityEngine.Object.Instantiate(prefab, parent);
                if (obstacleRoot == null)
                    throw new InvalidOperationException($"InstantiateStaticObstacleBakeInstances failed: instantiate returned null path={instance.PrefabPath} identifier={instance.Identifier}.");

                obstacleRoot.name = $"__StaticNavObstacle_{i}_{instance.Identifier}";
                obstacleRoot.transform.position = instance.Position;
                obstacleRoot.transform.rotation = Quaternion.identity;
                obstacleRoot.transform.localScale = Vector3.one;
                roots.Add(obstacleRoot);
            }

            return roots.ToArray();
        }

        private static void ApplyTerrainBakeTransform(GameObject terrainRoot, TerrainBakeTransform terrainTransform)
        {
            if (terrainRoot == null)
                throw new InvalidOperationException("ApplyTerrainBakeTransform failed: terrain root is null.");

            Transform transform = terrainRoot.transform;
            transform.SetPositionAndRotation(terrainTransform.Position, terrainTransform.Rotation);
            transform.localScale = terrainTransform.Scale;
            Physics.SyncTransforms();
        }

        private static Bounds ResolveColliderBounds(IReadOnlyList<Collider> colliders)
        {
            if (colliders == null || colliders.Count == 0)
                throw new InvalidOperationException("ResolveColliderBounds failed: colliders are empty.");

            bool found = false;
            Bounds bounds = default;
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                    continue;

                if (!found)
                {
                    bounds = collider.bounds;
                    found = true;
                    continue;
                }

                bounds.Encapsulate(collider.bounds);
            }

            if (!found)
                throw new InvalidOperationException("ResolveColliderBounds failed: all colliders are null.");

            return bounds;
        }

        private static Vector3 GetCellCenter(Vector3 origin, float cellSize, int x, int y)
        {
            return new Vector3(origin.x + (x + 0.5f) * cellSize, origin.y, origin.z + (y + 0.5f) * cellSize);
        }

        private static void ApplyFlowGraphConfigForDerivedBake(System.Diagnostics.Stopwatch stopwatch)
        {
            FlowFieldNavigationConfig config = AssetDatabase.LoadAssetAtPath<FlowFieldNavigationConfig>(FlowFieldNavigationConfigPath);
            if (config == null)
                throw new InvalidOperationException($"FlowNavigationGridPrefabBaker failed: FlowFieldNavigationConfig not found at {FlowFieldNavigationConfigPath}.");

            FlowFieldCrowdMovementSystem.SetConfig(config);
            Debug.Log(
                $"[FlowNavigationGridBake] stage=flow-config-applied elapsedMs={stopwatch.ElapsedMilliseconds} " +
                $"config={FlowFieldNavigationConfigPath} sector={config.SectorSizeInCells} narrow={config.PortalNarrowWidthCells} maxWindow={config.PortalMaxWindowWidthCells}");
        }

        private static Result BakeMovementTypeFromRaster(
            string terrainPrefabPath,
            string assetPath,
            int agentTypeId,
            float hardClearanceRadius,
            TerrainRaster raster,
            ColliderSpatialIndex groundIndex,
            ColliderSpatialIndex obstacleIndex,
            AuthoredTerrainTopology terrainTopology,
            int groundColliderCount,
            int obstacleColliderCount,
            System.Diagnostics.Stopwatch stopwatch)
        {
            if (raster == null)
                throw new InvalidOperationException("BakeMovementTypeFromRaster failed: raster is null.");
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new InvalidOperationException("BakeMovementTypeFromRaster failed: assetPath is empty.");

            EnsureAssetFolder(assetPath);
            bool[] movementWalkable = new bool[raster.CellCount];
            byte[] movementCosts = new byte[raster.CellCount];
            Vector3[] movementAnchors = new Vector3[raster.CellCount];
            byte[] movementNeighborMasks = new byte[raster.CellCount];
            int refinedAnchorCount = 0;
            int walkableCount = 0;
            for (int y = 0; y < raster.Height; y++)
            {
                if (ShouldLogBakeRow(y, raster.Height))
                    Debug.Log($"[FlowNavigationGridBake] stage=anchors-row elapsedMs={stopwatch.ElapsedMilliseconds} agentType={agentTypeId} row={y}/{raster.Height}");

                for (int x = 0; x < raster.Width; x++)
                {
                    int index = raster.ToIndex(x, y);
                    bool refined;
                    bool walkable = TryResolveWalkableCellAnchor(
                        raster,
                        groundIndex,
                        obstacleIndex,
                        terrainTopology,
                        x,
                        y,
                        hardClearanceRadius,
                        out Vector3 localAnchor,
                        out byte sourceCost,
                        out refined);
                    movementWalkable[index] = walkable;
                    movementCosts[index] = walkable ? sourceCost : byte.MaxValue;
                    movementAnchors[index] = localAnchor;
                    if (walkable)
                        walkableCount++;
                    if (refined)
                        refinedAnchorCount++;
                }
            }
            Debug.Log(
                $"[FlowNavigationGridBake] stage=anchors-complete elapsedMs={stopwatch.ElapsedMilliseconds} " +
                $"agentType={agentTypeId} walkable={walkableCount} blocked={raster.CellCount - walkableCount} refinedAnchors={refinedAnchorCount}");

            if (walkableCount == 0)
                throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.BakeFromTerrainPrefab failed: generated zero walkable cells from {terrainPrefabPath}.");

            BuildNeighborTraversalMasks(
                raster,
                groundIndex,
                obstacleIndex,
                terrainTopology,
                movementWalkable,
                movementAnchors,
                movementNeighborMasks,
                hardClearanceRadius,
                agentTypeId,
                stopwatch);
            SymmetrizeNeighborTraversalMasks(raster, movementWalkable, movementNeighborMasks);
            Debug.Log($"[FlowNavigationGridBake] stage=neighbor-masks-complete elapsedMs={stopwatch.ElapsedMilliseconds} agentType={agentTypeId}");

            FlowNavigationGridAsset asset = LoadOrCreateAsset(assetPath);
            Debug.Log($"[FlowNavigationGridBake] stage=asset-loaded elapsedMs={stopwatch.ElapsedMilliseconds} agentType={agentTypeId} asset={assetPath}");
            asset.Overwrite(agentTypeId, raster.Width, raster.Height, raster.CellSize, raster.Origin, movementWalkable, movementCosts, movementAnchors, movementNeighborMasks);
            Debug.Log(
                $"[FlowNavigationGridBake] stage=asset-cells-written elapsedMs={stopwatch.ElapsedMilliseconds} agentType={agentTypeId} " +
                $"walkable={walkableCount} blocked={raster.CellCount - walkableCount}");
            ApplyFlowGraphConfigForDerivedBake(stopwatch);
            FlowNavigationGridAsset.DerivedNavigationData derivedData = FlowFieldCrowdMovementSystem.BuildDerivedNavigationDataForAsset(
                agentTypeId,
                (Fix64)hardClearanceRadius,
                raster.Width,
                raster.Height,
                raster.CellSize,
                raster.Origin,
                movementWalkable,
                movementAnchors,
                movementCosts,
                movementNeighborMasks);
            asset.SetDerivedNavigationData(derivedData);
            Debug.Log(
                $"[FlowNavigationGridBake] stage=derived-navigation-written elapsedMs={stopwatch.ElapsedMilliseconds} agentType={agentTypeId} " +
                $"sectors={derivedData.Sectors.Length} portals={derivedData.Portals.Length} islands={derivedData.IslandCount}");
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            long savedBytes = File.Exists(assetPath) ? new FileInfo(assetPath).Length : -1L;
            Debug.Log(
                $"[FlowNavigationGridBake] stage=asset-saved elapsedMs={stopwatch.ElapsedMilliseconds} agentType={agentTypeId} " +
                $"asset={assetPath} bytes={savedBytes}");
            AssetDatabase.ImportAsset(assetPath);
            Debug.Log($"[FlowNavigationGridBake] stage=asset-imported elapsedMs={stopwatch.ElapsedMilliseconds} agentType={agentTypeId} asset={assetPath}");

            return new Result
            {
                Asset = asset,
                AssetPath = assetPath,
                AgentTypeId = asset.AgentTypeId,
                Width = raster.Width,
                Height = raster.Height,
                WalkableCount = walkableCount,
                BlockedCount = raster.CellCount - walkableCount,
                GroundColliderCount = groundColliderCount,
                ObstacleColliderCount = obstacleColliderCount
            };
        }

        private static TerrainRaster BuildTerrainRaster(
            ColliderSpatialIndex groundIndex,
            ColliderSpatialIndex obstacleIndex,
            int width,
            int height,
            float cellSize,
            Vector3 gridOrigin,
            int agentTypeId,
            System.Diagnostics.Stopwatch stopwatch)
        {
            if (groundIndex == null)
                throw new InvalidOperationException("BuildTerrainRaster failed: groundIndex is null.");
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"BuildTerrainRaster failed: invalid size {width}x{height}.");

            int cellCount = width * height;
            bool[] baseOpen = new bool[cellCount];
            bool[] baseObstacle = new bool[cellCount];
            bool[] groundOverlap = new bool[cellCount];
            bool[] obstacleOverlap = new bool[cellCount];
            int groundCenterCount = 0;
            int obstacleCenterCount = 0;
            int baseOpenCount = 0;
            for (int y = 0; y < height; y++)
            {
                if (ShouldLogBakeRow(y, height))
                    Debug.Log($"[FlowNavigationGridBake] stage=raster-row elapsedMs={stopwatch.ElapsedMilliseconds} agentType={agentTypeId} row={y}/{height}");

                for (int x = 0; x < width; x++)
                {
                    int index = x + y * width;
                    Vector3 center = GetCellCenter(gridOrigin, cellSize, x, y);
                    bool hasGround = groundIndex.RaycastVertical(center.x, center.z);
                    bool hasObstacle = obstacleIndex != null && obstacleIndex.Count > 0 && obstacleIndex.RaycastVertical(center.x, center.z);
                    float minX = gridOrigin.x + x * cellSize;
                    float minZ = gridOrigin.z + y * cellSize;
                    float maxX = gridOrigin.x + (x + 1) * cellSize;
                    float maxZ = gridOrigin.z + (y + 1) * cellSize;
                    groundOverlap[index] = hasGround || groundIndex.OverlapsAabb(minX, minZ, maxX, maxZ);
                    obstacleOverlap[index] = hasObstacle || (obstacleIndex != null && obstacleIndex.Count > 0 && obstacleIndex.OverlapsAabb(minX, minZ, maxX, maxZ));
                    baseObstacle[index] = hasObstacle;
                    baseOpen[index] = hasGround && !hasObstacle;
                    if (hasGround)
                        groundCenterCount++;
                    if (hasObstacle)
                        obstacleCenterCount++;
                    if (baseOpen[index])
                        baseOpenCount++;
                }
            }
            Debug.Log(
                $"[FlowNavigationGridBake] stage=raster-complete elapsedMs={stopwatch.ElapsedMilliseconds} agentType={agentTypeId} " +
                $"cells={cellCount} groundCenters={groundCenterCount} obstacleCenters={obstacleCenterCount} baseOpen={baseOpenCount}");

            float[] clearance = BuildClearanceDistances(baseOpen, width, height, cellSize);
            Debug.Log($"[FlowNavigationGridBake] stage=clearance-complete elapsedMs={stopwatch.ElapsedMilliseconds} agentType={agentTypeId}");
            return new TerrainRaster(width, height, cellSize, gridOrigin, baseOpen, baseObstacle, groundOverlap, obstacleOverlap, clearance);
        }

        private static float[] BuildClearanceDistances(bool[] baseOpen, int width, int height, float cellSize)
        {
            if (baseOpen == null || baseOpen.Length != width * height)
                throw new InvalidOperationException("BuildClearanceDistances failed: baseOpen has invalid length.");

            bool hasBlockedCell = false;
            for (int i = 0; i < baseOpen.Length; i++)
            {
                if (!baseOpen[i])
                {
                    hasBlockedCell = true;
                    break;
                }
            }

            if (!hasBlockedCell)
            {
                float[] openClearance = new float[baseOpen.Length];
                for (int i = 0; i < openClearance.Length; i++)
                    openClearance[i] = float.PositiveInfinity;
                return openClearance;
            }

            float[] vertical = new float[baseOpen.Length];
            float[] distanceSq = new float[baseOpen.Length];
            float[] f = new float[Mathf.Max(width, height)];
            float[] d = new float[Mathf.Max(width, height)];
            int[] v = new int[Mathf.Max(width, height)];
            float[] z = new float[Mathf.Max(width, height) + 1];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                    f[y] = baseOpen[x + y * width] ? DistanceTransformInfinity : 0f;
                DistanceTransform1D(f, height, d, v, z);
                for (int y = 0; y < height; y++)
                    vertical[x + y * width] = d[y];
            }

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                    f[x] = vertical[row + x];
                DistanceTransform1D(f, width, d, v, z);
                for (int x = 0; x < width; x++)
                    distanceSq[row + x] = d[x];
            }

            float[] clearance = new float[baseOpen.Length];
            for (int i = 0; i < clearance.Length; i++)
            {
                if (!baseOpen[i])
                {
                    clearance[i] = 0f;
                    continue;
                }

                float cellDistance = Mathf.Sqrt(distanceSq[i]);
                clearance[i] = Mathf.Max(0f, (cellDistance - 0.5f) * cellSize);
            }

            return clearance;
        }

        private static void DistanceTransform1D(float[] f, int n, float[] d, int[] v, float[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = float.NegativeInfinity;
            z[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                float s;
                do
                {
                    int vk = v[k];
                    s = ((f[q] + q * q) - (f[vk] + vk * vk)) / (2f * q - 2f * vk);
                    if (s <= z[k])
                        k--;
                }
                while (k >= 0 && s <= z[k]);

                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = float.PositiveInfinity;
            }

            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q)
                    k++;
                float delta = q - v[k];
                d[q] = delta * delta + f[v[k]];
            }
        }

        private static bool IsFootprintWalkable(ColliderSpatialIndex groundIndex, ColliderSpatialIndex obstacleIndex, float x, float z, float hardClearanceRadius)
        {
            float sampleRadius = Mathf.Max(0f, hardClearanceRadius);
            if (!HasGroundAtFootprint(groundIndex, x, z, sampleRadius))
                return false;

            return !HasObstacleAtFootprint(obstacleIndex, x, z, sampleRadius);
        }

        private static bool TryResolveWalkableCellAnchor(
            TerrainRaster raster,
            ColliderSpatialIndex groundIndex,
            ColliderSpatialIndex obstacleIndex,
            AuthoredTerrainTopology terrainTopology,
            int cellX,
            int cellY,
            float hardClearanceRadius,
            out Vector3 localAnchor,
            out byte sourceCost,
            out bool refined)
        {
            Vector3 center = raster.GetCellCenter(cellX, cellY);
            localAnchor = center;
            sourceCost = byte.MaxValue;
            refined = false;
            if (terrainTopology != null && !terrainTopology.ContainsTerrainAtWorld(center))
                return false;

            int cellIndex = raster.ToIndex(cellX, cellY);
            if (raster.IsWalkableForRadius(cellIndex, hardClearanceRadius))
            {
                if (!groundIndex.TryRaycastVertical(center.x, center.z, out Vector3 groundPoint))
                    throw new InvalidOperationException($"TryResolveWalkableCellAnchor failed: walkable cell ({cellX},{cellY}) has no ground hit at {center}.");

                localAnchor = groundPoint;
                sourceCost = ResolveSourceCost(raster, cellIndex, hardClearanceRadius);
                return true;
            }

            if (!ShouldTryRefinedAnchor(raster, cellX, cellY, hardClearanceRadius))
                return false;

            bool found = false;
            float bestDistanceSq = float.PositiveInfinity;
            float bestWallDistance = float.NegativeInfinity;
            byte bestCost = byte.MaxValue;
            for (int i = 0; i < CellAnchorSampleOffsets.Length; i++)
            {
                Vector2 offset = CellAnchorSampleOffsets[i] * raster.CellSize;
                Vector3 candidate = new Vector3(center.x + offset.x, 0f, center.z + offset.y);
                if (!IsFootprintWalkable(groundIndex, obstacleIndex, candidate.x, candidate.z, hardClearanceRadius))
                    continue;
                if (!groundIndex.TryRaycastVertical(candidate.x, candidate.z, out Vector3 groundPoint))
                    throw new InvalidOperationException($"TryResolveWalkableCellAnchor failed: valid refined anchor has no ground hit at {candidate}.");
                if (terrainTopology != null && !terrainTopology.ContainsTerrainAtWorld(groundPoint))
                    continue;

                candidate.y = groundPoint.y;

                float distanceSq = offset.sqrMagnitude;
                float wallDistance = raster.TryGetClearanceAtWorld(candidate.x, candidate.z, out float candidateClearance)
                    ? candidateClearance
                    : ResolveGeometryWallDistance(groundIndex, obstacleIndex, candidate.x, candidate.z);
                byte cost = ResolveSourceCostFromClearance(wallDistance, hardClearanceRadius, raster.CellSize);
                bool better = !found
                              || distanceSq < bestDistanceSq - 0.000001f
                              || (Mathf.Abs(distanceSq - bestDistanceSq) <= 0.000001f && wallDistance > bestWallDistance);
                if (!better)
                    continue;

                found = true;
                localAnchor = candidate;
                bestDistanceSq = distanceSq;
                bestWallDistance = wallDistance;
                bestCost = cost;
            }

            if (!found)
                return false;

            sourceCost = bestCost;
            refined = true;
            return true;
        }

        private static void BuildNeighborTraversalMasks(
            TerrainRaster raster,
            ColliderSpatialIndex groundIndex,
            ColliderSpatialIndex obstacleIndex,
            AuthoredTerrainTopology terrainTopology,
            bool[] walkable,
            Vector3[] anchors,
            byte[] neighborMasks,
            float hardClearanceRadius,
            int agentTypeId,
            System.Diagnostics.Stopwatch stopwatch)
        {
            if (raster == null)
                throw new InvalidOperationException("BuildNeighborTraversalMasks failed: raster is null.");
            if (walkable == null || walkable.Length != raster.CellCount)
                throw new InvalidOperationException("BuildNeighborTraversalMasks failed: walkable mask has invalid length.");
            if (anchors == null || anchors.Length != raster.CellCount)
                throw new InvalidOperationException("BuildNeighborTraversalMasks failed: anchors have invalid length.");
            if (neighborMasks == null || neighborMasks.Length != raster.CellCount)
                throw new InvalidOperationException("BuildNeighborTraversalMasks failed: neighbor mask has invalid length.");

            for (int y = 0; y < raster.Height; y++)
            {
                if (ShouldLogBakeRow(y, raster.Height))
                {
                    long elapsedMs = stopwatch != null ? stopwatch.ElapsedMilliseconds : -1L;
                    Debug.Log($"[FlowNavigationGridBake] stage=neighbors-row elapsedMs={elapsedMs} agentType={agentTypeId} row={y}/{raster.Height}");
                }

                for (int x = 0; x < raster.Width; x++)
                {
                    int index = raster.ToIndex(x, y);
                    if (!walkable[index])
                    {
                        neighborMasks[index] = 0;
                        continue;
                    }

                    byte mask = 0;
                    Vector3 from = anchors[index];
                    for (int i = 0; i < NeighborOffsetX.Length; i++)
                    {
                        int toX = x + NeighborOffsetX[i];
                        int toY = y + NeighborOffsetY[i];
                        if (toX < 0 || toX >= raster.Width || toY < 0 || toY >= raster.Height)
                            continue;

                        int toIndex = raster.ToIndex(toX, toY);
                        if (!walkable[toIndex])
                            continue;
                        if (NeighborOffsetX[i] != 0 && NeighborOffsetY[i] != 0)
                        {
                            int sideA = raster.ToIndex(x + NeighborOffsetX[i], y);
                            int sideB = raster.ToIndex(x, y + NeighborOffsetY[i]);
                            if (!walkable[sideA] || !walkable[sideB])
                                continue;
                        }

                        if (terrainTopology != null
                            && !terrainTopology.AllowsTraversal(
                                raster.GetCellCenter(x, y),
                                raster.GetCellCenter(toX, toY)))
                        {
                            continue;
                        }

                        Vector3 to = anchors[toIndex];
                        bool traversable = raster.EdgeNeedsStrictGeometryCheck(index, toIndex)
                            ? IsFootprintPathWalkable(groundIndex, obstacleIndex, from, to, hardClearanceRadius, raster.CellSize)
                            : IsRasterPathWalkable(raster, from, to, hardClearanceRadius);
                        if (traversable)
                            mask |= (byte)(1 << i);
                    }

                    neighborMasks[index] = mask;
                }
            }
        }

        private static void SymmetrizeNeighborTraversalMasks(TerrainRaster raster, bool[] walkable, byte[] neighborMasks)
        {
            if (raster == null)
                throw new InvalidOperationException("SymmetrizeNeighborTraversalMasks failed: raster is null.");
            if (walkable == null || walkable.Length != raster.CellCount)
                throw new InvalidOperationException("SymmetrizeNeighborTraversalMasks failed: walkable mask has invalid length.");
            if (neighborMasks == null || neighborMasks.Length != raster.CellCount)
                throw new InvalidOperationException("SymmetrizeNeighborTraversalMasks failed: neighbor mask has invalid length.");

            for (int y = 0; y < raster.Height; y++)
            {
                for (int x = 0; x < raster.Width; x++)
                {
                    int fromIndex = raster.ToIndex(x, y);
                    if (!walkable[fromIndex])
                    {
                        neighborMasks[fromIndex] = 0;
                        continue;
                    }

                    for (int i = 0; i < NeighborOffsetX.Length; i++)
                    {
                        int toX = x + NeighborOffsetX[i];
                        int toY = y + NeighborOffsetY[i];
                        if (toX < 0 || toX >= raster.Width || toY < 0 || toY >= raster.Height)
                            continue;

                        int toIndex = raster.ToIndex(toX, toY);
                        if (!walkable[toIndex])
                        {
                            neighborMasks[fromIndex] &= (byte)~(1 << i);
                            continue;
                        }

                        int oppositeIndex = ResolveNeighborOffsetIndex(-NeighborOffsetX[i], -NeighborOffsetY[i]);
                        if (oppositeIndex < 0)
                            throw new InvalidOperationException($"SymmetrizeNeighborTraversalMasks failed: missing opposite offset for {NeighborOffsetX[i]},{NeighborOffsetY[i]}.");

                        bool forward = (neighborMasks[fromIndex] & (1 << i)) != 0;
                        bool backward = (neighborMasks[toIndex] & (1 << oppositeIndex)) != 0;
                        if (!forward && !backward)
                            continue;

                        neighborMasks[fromIndex] |= (byte)(1 << i);
                        neighborMasks[toIndex] |= (byte)(1 << oppositeIndex);
                    }
                }
            }
        }

        private static int ResolveNeighborOffsetIndex(int dx, int dy)
        {
            for (int i = 0; i < NeighborOffsetX.Length; i++)
            {
                if (NeighborOffsetX[i] == dx && NeighborOffsetY[i] == dy)
                    return i;
            }

            return -1;
        }

        private static bool ShouldLogBakeRow(int row, int height)
        {
            if (row == 0 || row == height - 1)
                return true;

            int interval = Mathf.Max(1, height / BakeProgressLogRows);
            return row % interval == 0;
        }

        private static bool ShouldTryRefinedAnchor(TerrainRaster raster, int cellX, int cellY, float hardClearanceRadius)
        {
            int index = raster.ToIndex(cellX, cellY);
            if (!raster.GroundMayOverlap(index))
                return false;
            if (!raster.BaseObstacle(index))
                return true;

            return raster.IsNearBaseOpen(cellX, cellY, Mathf.Max(1, Mathf.CeilToInt(hardClearanceRadius / Mathf.Max(0.001f, raster.CellSize))));
        }

        private static bool IsRasterPathWalkable(
            TerrainRaster raster,
            Vector3 from,
            Vector3 to,
            float hardClearanceRadius)
        {
            Vector2 fromXZ = new Vector2(from.x, from.z);
            Vector2 toXZ = new Vector2(to.x, to.z);
            float distance = Vector2.Distance(fromXZ, toXZ);
            float step = Mathf.Max(0.02f, raster.CellSize * 0.5f);
            int sampleCount = Mathf.Max(2, Mathf.CeilToInt(distance / step));
            for (int i = 0; i <= sampleCount; i++)
            {
                float t = i / (float)sampleCount;
                Vector2 sample = Vector2.Lerp(fromXZ, toXZ, t);
                if (!raster.IsWalkableAtWorld(sample.x, sample.y, hardClearanceRadius))
                    return false;
            }

            return true;
        }

        private static bool IsFootprintPathWalkable(
            ColliderSpatialIndex groundIndex,
            ColliderSpatialIndex obstacleIndex,
            Vector3 from,
            Vector3 to,
            float hardClearanceRadius,
            float cellSize)
        {
            Vector2 fromXZ = new Vector2(from.x, from.z);
            Vector2 toXZ = new Vector2(to.x, to.z);
            float distance = Vector2.Distance(fromXZ, toXZ);
            float step = Mathf.Max(0.02f, Mathf.Min(cellSize * 0.25f, Mathf.Max(0.02f, hardClearanceRadius * 0.5f)));
            int sampleCount = Mathf.Max(2, Mathf.CeilToInt(distance / step));
            for (int i = 0; i <= sampleCount; i++)
            {
                float t = i / (float)sampleCount;
                Vector2 sample = Vector2.Lerp(fromXZ, toXZ, t);
                if (!IsFootprintWalkable(groundIndex, obstacleIndex, sample.x, sample.y, hardClearanceRadius))
                    return false;
            }

            return true;
        }

        private static bool HasGroundAtFootprint(ColliderSpatialIndex groundIndex, float x, float z, float sampleRadius)
        {
            float radius = Mathf.Max(0f, sampleRadius - BoundaryEpsilon);
            for (int i = 0; i < UnitFootprintSamples.Length; i++)
            {
                Vector2 offset = UnitFootprintSamples[i] * radius;
                if (!groundIndex.RaycastVertical(x + offset.x, z + offset.y))
                    return false;
            }

            return true;
        }

        private static bool HasObstacleAtFootprint(ColliderSpatialIndex obstacleIndex, float x, float z, float sampleRadius)
        {
            if (obstacleIndex == null || obstacleIndex.Count == 0)
                return false;

            float radius = Mathf.Max(MinimumFootprintSampleRadius, sampleRadius);
            for (int i = 0; i < UnitFootprintSamples.Length; i++)
            {
                Vector2 offset = UnitFootprintSamples[i] * radius;
                if (obstacleIndex.RaycastVertical(x + offset.x, z + offset.y))
                    return true;
            }

            return obstacleIndex.ResolveNearestDistanceXZ(x, z, radius) <= radius;
        }

        private static byte ResolveSourceCost(ColliderSpatialIndex groundIndex, ColliderSpatialIndex obstacleIndex, float x, float z, float hardClearanceRadius, float cellSize)
        {
            float wallDistance = ResolveGeometryWallDistance(groundIndex, obstacleIndex, x, z);
            return ResolveSourceCostFromClearance(wallDistance, hardClearanceRadius, cellSize);
        }

        private static byte ResolveSourceCost(TerrainRaster raster, int index, float hardClearanceRadius)
        {
            return ResolveSourceCostFromClearance(raster.Clearance(index), hardClearanceRadius, raster.CellSize);
        }

        private static byte ResolveSourceCostFromClearance(float clearance, float hardClearanceRadius, float cellSize)
        {
            if (float.IsPositiveInfinity(clearance))
                return 1;

            float blurRadius = Mathf.Max(cellSize * 2.25f, hardClearanceRadius + cellSize * 1.75f);
            if (clearance >= blurRadius)
                return 1;

            float t = Mathf.Clamp01((blurRadius - clearance) / Mathf.Max(0.001f, blurRadius));
            int adjacentPenalty = 1 + Mathf.CeilToInt(Mathf.Max(0f, hardClearanceRadius - cellSize * 0.5f) / Mathf.Max(0.001f, cellSize));
            int penalty = Mathf.RoundToInt(Mathf.Lerp(0f, adjacentPenalty, t));
            return (byte)Mathf.Clamp(1 + penalty, 1, 254);
        }

        private static float ResolveGeometryWallDistance(ColliderSpatialIndex groundIndex, ColliderSpatialIndex obstacleIndex, float x, float z)
        {
            float nearest = obstacleIndex != null ? obstacleIndex.ResolveNearestDistanceXZ(x, z) : float.PositiveInfinity;
            nearest = Mathf.Min(nearest, groundIndex != null ? groundIndex.ResolveNearestContainingBoundaryDistanceXZ(x, z) : float.PositiveInfinity);
            return nearest;
        }

        private sealed class AuthoredTerrainTopology
        {
            private readonly Transform terrainTransform;
            private readonly float cellSize;
            private readonly Dictionary<Vector2Int, int> platformHeights;
            private readonly Dictionary<Vector2Int, Vector2Int> slopeDirections;

            private AuthoredTerrainTopology(
                Transform terrainTransform,
                float cellSize,
                Dictionary<Vector2Int, int> platformHeights,
                Dictionary<Vector2Int, Vector2Int> slopeDirections)
            {
                this.terrainTransform = terrainTransform;
                this.cellSize = cellSize;
                this.platformHeights = platformHeights;
                this.slopeDirections = slopeDirections;
            }

            public static AuthoredTerrainTopology TryCreate(GameObject terrainRoot)
            {
                if (terrainRoot == null)
                    throw new ArgumentNullException(nameof(terrainRoot));

                TileWorldCreatorManager manager = terrainRoot.GetComponentInChildren<TileWorldCreatorManager>(true);
                if (manager == null || manager.configuration == null)
                    return null;
                if (manager.configuration.cellSize <= 0.0001f)
                    throw new InvalidOperationException("Authored terrain topology has an invalid TileWorld cell size.");

                var heights = new Dictionary<Vector2Int, int>();
                for (int height = 0; height <= 5; height++)
                {
                    BlueprintLayer layer = manager.GetBlueprintLayer("Plane_H" + height);
                    if (layer == null || !layer.isEnabled)
                        continue;

                    foreach (Vector2Int cell in ReadCellsRequired(layer))
                    {
                        if (!heights.TryGetValue(cell, out int current) || height > current)
                            heights[cell] = height;
                    }
                }

                BlueprintLayer slopeLayer = manager.GetBlueprintLayer("Slope");
                bool hasSlopes = slopeLayer != null && slopeLayer.isEnabled && slopeLayer.allPositions.Count > 0;
                if (heights.Count == 0)
                {
                    if (hasSlopes)
                        throw new InvalidOperationException("Authored slope topology requires Plane_H0..Plane_H5 support layers.");
                    return null;
                }

                var directions = new Dictionary<Vector2Int, Vector2Int>();
                if (hasSlopes)
                {
                    var slopeCells = new HashSet<Vector2>(slopeLayer.allPositions);
                    var topHeights = heights.ToDictionary(
                        pair => new Vector2(pair.Key.x, pair.Key.y),
                        pair => pair.Value);
                    if (!LdtkSlopeLayoutResolver.TryResolve(
                            slopeCells,
                            topHeights,
                            0f,
                            90f,
                            out LdtkSlopeLayout layout,
                            out string error))
                    {
                        throw new InvalidOperationException("Cannot build authored slope navigation topology: " + error);
                    }

                    foreach (LdtkSlopeRamp ramp in layout.ramps)
                    {
                        Vector2 firstCell = ramp.anchor - ramp.direction * ((ramp.run - 1) * 0.5f);
                        Vector2Int direction = ToGridCellRequired(ramp.direction, "slope direction");
                        for (int offset = 0; offset < ramp.run; offset++)
                        {
                            Vector2Int cell = ToGridCellRequired(firstCell + ramp.direction * offset, "slope cell");
                            if (directions.TryGetValue(cell, out Vector2Int existing) && existing != direction)
                            {
                                throw new InvalidOperationException(
                                    $"Authored slope cell ({cell.x},{cell.y}) has conflicting directions {existing} and {direction}.");
                            }

                            directions[cell] = direction;
                        }
                    }

                    foreach (Vector2 slopeCell in slopeCells)
                    {
                        Vector2Int cell = ToGridCellRequired(slopeCell, "slope cell");
                        if (!heights.ContainsKey(cell))
                        {
                            throw new InvalidOperationException(
                                $"Authored slope cell ({cell.x},{cell.y}) has no Plane_H* support. Reimport the LDtk level.");
                        }
                        if (!directions.ContainsKey(cell))
                            throw new InvalidOperationException($"Authored slope cell ({cell.x},{cell.y}) has no resolved direction.");
                    }
                }

                return new AuthoredTerrainTopology(manager.transform, manager.configuration.cellSize, heights, directions);
            }

            public bool ContainsTerrainAtWorld(Vector3 worldPosition)
            {
                return platformHeights.ContainsKey(WorldToCell(worldPosition));
            }

            public bool AllowsTraversal(Vector3 fromWorld, Vector3 toWorld)
            {
                Vector2Int from = WorldToCell(fromWorld);
                Vector2Int to = WorldToCell(toWorld);
                if (from == to)
                    return platformHeights.ContainsKey(from);

                int deltaX = to.x - from.x;
                int deltaY = to.y - from.y;
                if (Math.Abs(deltaX) > 1 || Math.Abs(deltaY) > 1)
                    return false;
                if (deltaX != 0 && deltaY != 0)
                {
                    var sideA = new Vector2Int(to.x, from.y);
                    var sideB = new Vector2Int(from.x, to.y);
                    return AllowsCardinalTraversal(from, sideA)
                           && AllowsCardinalTraversal(from, sideB)
                           && AllowsCardinalTraversal(sideA, to)
                           && AllowsCardinalTraversal(sideB, to);
                }

                return AllowsCardinalTraversal(from, to);
            }

            private bool AllowsCardinalTraversal(Vector2Int from, Vector2Int to)
            {
                if (!platformHeights.TryGetValue(from, out int fromHeight)
                    || !platformHeights.TryGetValue(to, out int toHeight))
                {
                    return false;
                }

                bool fromIsSlope = slopeDirections.TryGetValue(from, out Vector2Int fromDirection);
                bool toIsSlope = slopeDirections.TryGetValue(to, out Vector2Int toDirection);
                if (!fromIsSlope && !toIsSlope)
                    return fromHeight == toHeight;
                if (fromIsSlope && toIsSlope)
                    return fromDirection == toDirection;

                Vector2Int slopeCell = fromIsSlope ? from : to;
                Vector2Int platformCell = fromIsSlope ? to : from;
                Vector2Int slopeDirection = fromIsSlope ? fromDirection : toDirection;
                Vector2Int transition = platformCell - slopeCell;
                return transition == slopeDirection || transition == -slopeDirection;
            }

            private Vector2Int WorldToCell(Vector3 worldPosition)
            {
                Vector3 local = terrainTransform.InverseTransformPoint(worldPosition);
                return new Vector2Int(
                    Mathf.FloorToInt(local.x / cellSize + 0.5f),
                    Mathf.FloorToInt(local.z / cellSize + 0.5f));
            }

            private static IEnumerable<Vector2Int> ReadCellsRequired(BlueprintLayer layer)
            {
                if (layer == null)
                    throw new ArgumentNullException(nameof(layer));
                if (layer.allPositions == null)
                    throw new InvalidOperationException($"Authored terrain layer '{layer.layerName}' has no cell collection.");

                foreach (Vector2 position in layer.allPositions)
                    yield return ToGridCellRequired(position, layer.layerName);
            }

            private static Vector2Int ToGridCellRequired(Vector2 value, string label)
            {
                int x = Mathf.RoundToInt(value.x);
                int y = Mathf.RoundToInt(value.y);
                if (!Mathf.Approximately(value.x, x) || !Mathf.Approximately(value.y, y))
                    throw new InvalidOperationException($"Authored terrain {label} is not grid-aligned: {value}.");
                return new Vector2Int(x, y);
            }
        }

        private sealed class TerrainRaster
        {
            private readonly bool[] baseOpen;
            private readonly bool[] baseObstacle;
            private readonly bool[] groundOverlap;
            private readonly bool[] obstacleOverlap;
            private readonly float[] clearance;

            public TerrainRaster(
                int width,
                int height,
                float cellSize,
                Vector3 origin,
                bool[] baseOpen,
                bool[] baseObstacle,
                bool[] groundOverlap,
                bool[] obstacleOverlap,
                float[] clearance)
            {
                Width = width;
                Height = height;
                CellSize = cellSize;
                Origin = origin;
                this.baseOpen = baseOpen ?? throw new InvalidOperationException("TerrainRaster failed: baseOpen is null.");
                this.baseObstacle = baseObstacle ?? throw new InvalidOperationException("TerrainRaster failed: baseObstacle is null.");
                this.groundOverlap = groundOverlap ?? throw new InvalidOperationException("TerrainRaster failed: groundOverlap is null.");
                this.obstacleOverlap = obstacleOverlap ?? throw new InvalidOperationException("TerrainRaster failed: obstacleOverlap is null.");
                this.clearance = clearance ?? throw new InvalidOperationException("TerrainRaster failed: clearance is null.");
                int expected = width * height;
                if (width <= 0 || height <= 0 || cellSize <= 0.0001f)
                    throw new InvalidOperationException($"TerrainRaster failed: invalid grid {width}x{height} cellSize={cellSize:F4}.");
                if (this.baseOpen.Length != expected || this.baseObstacle.Length != expected || this.groundOverlap.Length != expected || this.obstacleOverlap.Length != expected || this.clearance.Length != expected)
                    throw new InvalidOperationException("TerrainRaster failed: array length mismatch.");
            }

            public int Width { get; }
            public int Height { get; }
            public float CellSize { get; }
            public Vector3 Origin { get; }
            public int CellCount => Width * Height;

            public int ToIndex(int x, int y)
            {
                return x + y * Width;
            }

            public Vector3 GetCellCenter(int x, int y)
            {
                return FlowNavigationGridPrefabBaker.GetCellCenter(Origin, CellSize, x, y);
            }

            public bool BaseObstacle(int index)
            {
                return baseObstacle[index];
            }

            public bool GroundMayOverlap(int index)
            {
                return groundOverlap[index];
            }

            public bool EdgeNeedsStrictGeometryCheck(int fromIndex, int toIndex)
            {
                return obstacleOverlap[fromIndex] || obstacleOverlap[toIndex] || !baseOpen[fromIndex] || !baseOpen[toIndex];
            }

            public float Clearance(int index)
            {
                return clearance[index];
            }

            public bool IsWalkableForRadius(int index, float radius)
            {
                return baseOpen[index] && clearance[index] + BoundaryEpsilon >= Mathf.Max(0f, radius);
            }

            public bool IsWalkableAtWorld(float x, float z, float radius)
            {
                if (!TryWorldToCell(x, z, out int cellX, out int cellY))
                    return false;

                return IsWalkableForRadius(ToIndex(cellX, cellY), radius);
            }

            public bool TryGetClearanceAtWorld(float x, float z, out float value)
            {
                if (!TryWorldToCell(x, z, out int cellX, out int cellY))
                {
                    value = 0f;
                    return false;
                }

                value = clearance[ToIndex(cellX, cellY)];
                return true;
            }

            public bool IsNearBaseOpen(int cellX, int cellY, int radiusCells)
            {
                int minX = Mathf.Max(0, cellX - radiusCells);
                int maxX = Mathf.Min(Width - 1, cellX + radiusCells);
                int minY = Mathf.Max(0, cellY - radiusCells);
                int maxY = Mathf.Min(Height - 1, cellY + radiusCells);
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (baseOpen[ToIndex(x, y)])
                            return true;
                    }
                }

                return false;
            }

            private bool TryWorldToCell(float x, float z, out int cellX, out int cellY)
            {
                cellX = Mathf.FloorToInt((x - Origin.x) / CellSize);
                cellY = Mathf.FloorToInt((z - Origin.z) / CellSize);
                return cellX >= 0 && cellX < Width && cellY >= 0 && cellY < Height;
            }
        }

        private sealed class ColliderSpatialIndex
        {
            private readonly List<Collider> colliders;
            private readonly Dictionary<Vector2Int, List<Collider>> buckets;
            private readonly Dictionary<Collider, int> visitStamps;
            private readonly float bucketSize;
            private int currentVisitStamp;

            public ColliderSpatialIndex(IReadOnlyList<Collider> source, float cellSize)
            {
                colliders = new List<Collider>();
                buckets = new Dictionary<Vector2Int, List<Collider>>();
                visitStamps = new Dictionary<Collider, int>();
                bucketSize = Mathf.Max(0.25f, cellSize * 2f);

                if (source == null)
                    return;

                for (int i = 0; i < source.Count; i++)
                {
                    Collider collider = source[i];
                    if (collider == null)
                        continue;

                    colliders.Add(collider);
                    Bounds bounds = collider.bounds;
                    int minX = ToBucket(bounds.min.x);
                    int maxX = ToBucket(bounds.max.x);
                    int minZ = ToBucket(bounds.min.z);
                    int maxZ = ToBucket(bounds.max.z);
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        for (int x = minX; x <= maxX; x++)
                        {
                            Vector2Int key = new Vector2Int(x, z);
                            if (!buckets.TryGetValue(key, out List<Collider> bucket))
                            {
                                bucket = new List<Collider>();
                                buckets.Add(key, bucket);
                            }

                            bucket.Add(collider);
                        }
                    }
                }
            }

            public int Count => colliders.Count;

            public bool RaycastVertical(float x, float z)
            {
                return TryRaycastVertical(x, z, out _);
            }

            public bool TryRaycastVertical(float x, float z, out Vector3 hitPoint)
            {
                hitPoint = Vector3.zero;
                if (!TryGetBucket(x, z, out List<Collider> bucket))
                    return false;

                float minY = float.PositiveInfinity;
                float maxY = float.NegativeInfinity;
                for (int i = 0; i < bucket.Count; i++)
                {
                    Bounds bounds = bucket[i].bounds;
                    if (!ContainsXZ(bounds, x, z))
                        continue;

                    minY = Mathf.Min(minY, bounds.min.y);
                    maxY = Mathf.Max(maxY, bounds.max.y);
                }

                if (float.IsPositiveInfinity(minY))
                    return false;

                Ray ray = new Ray(new Vector3(x, maxY + 8f, z), Vector3.down);
                float distance = Mathf.Max(0.1f, maxY - minY + 16f);
                bool found = false;
                float nearestDistance = float.PositiveInfinity;
                for (int i = 0; i < bucket.Count; i++)
                {
                    Collider collider = bucket[i];
                    Bounds bounds = collider.bounds;
                    if (!ContainsXZ(bounds, x, z))
                        continue;

                    if (!collider.Raycast(ray, out RaycastHit hit, distance) || hit.distance >= nearestDistance)
                        continue;

                    found = true;
                    nearestDistance = hit.distance;
                    hitPoint = hit.point;
                }

                return found;
            }

            public bool OverlapsAabb(float minX, float minZ, float maxX, float maxZ)
            {
                int minBucketX = ToBucket(minX);
                int maxBucketX = ToBucket(maxX);
                int minBucketZ = ToBucket(minZ);
                int maxBucketZ = ToBucket(maxZ);
                int visitStamp = NextVisitStamp();
                for (int zBucket = minBucketZ; zBucket <= maxBucketZ; zBucket++)
                {
                    for (int xBucket = minBucketX; xBucket <= maxBucketX; xBucket++)
                    {
                        if (!buckets.TryGetValue(new Vector2Int(xBucket, zBucket), out List<Collider> bucket))
                            continue;

                        for (int i = 0; i < bucket.Count; i++)
                        {
                            Collider collider = bucket[i];
                            if (!TryMarkVisited(collider, visitStamp))
                                continue;

                            Bounds bounds = collider.bounds;
                            if (bounds.max.x >= minX && bounds.min.x <= maxX && bounds.max.z >= minZ && bounds.min.z <= maxZ)
                                return true;
                        }
                    }
                }

                return false;
            }

            public float ResolveNearestDistanceXZ(float x, float z)
            {
                return ResolveNearestDistanceXZ(x, z, float.PositiveInfinity);
            }

            public float ResolveNearestDistanceXZ(float x, float z, float maxDistance)
            {
                if (colliders.Count == 0)
                    return float.PositiveInfinity;

                float best = float.PositiveInfinity;
                float searchRadius = float.IsPositiveInfinity(maxDistance) ? float.PositiveInfinity : Mathf.Max(0f, maxDistance);
                if (float.IsPositiveInfinity(searchRadius))
                {
                    for (int i = 0; i < colliders.Count; i++)
                        best = ResolveNearestDistanceForCollider(colliders[i], x, z, best);
                    return best;
                }

                int radiusBuckets = Mathf.Max(0, Mathf.CeilToInt(searchRadius / bucketSize));
                int centerX = ToBucket(x);
                int centerZ = ToBucket(z);
                int visitStamp = NextVisitStamp();
                for (int zBucket = centerZ - radiusBuckets; zBucket <= centerZ + radiusBuckets; zBucket++)
                {
                    for (int xBucket = centerX - radiusBuckets; xBucket <= centerX + radiusBuckets; xBucket++)
                    {
                        if (!buckets.TryGetValue(new Vector2Int(xBucket, zBucket), out List<Collider> bucket))
                            continue;

                        for (int i = 0; i < bucket.Count; i++)
                        {
                            Collider collider = bucket[i];
                            if (!TryMarkVisited(collider, visitStamp))
                                continue;

                            best = ResolveNearestDistanceForCollider(collider, x, z, best);
                        }
                    }
                }

                return best;
            }

            public float ResolveNearestContainingBoundaryDistanceXZ(float x, float z)
            {
                if (!TryGetBucket(x, z, out List<Collider> bucket))
                    return float.PositiveInfinity;

                float best = float.PositiveInfinity;
                for (int i = 0; i < bucket.Count; i++)
                {
                    Collider collider = bucket[i];
                    Bounds bounds = collider.bounds;
                    if (!ContainsXZ(bounds, x, z))
                        continue;

                    float distance = Mathf.Min(
                        Mathf.Min(x - bounds.min.x, bounds.max.x - x),
                        Mathf.Min(z - bounds.min.z, bounds.max.z - z));
                    if (distance < best)
                        best = distance;
                }

                return best;
            }

            private float ResolveNearestDistanceForCollider(Collider collider, float x, float z, float best)
            {
                if (collider == null)
                    return best;

                Bounds bounds = collider.bounds;
                if (x < bounds.min.x - best || x > bounds.max.x + best || z < bounds.min.z - best || z > bounds.max.z + best)
                    return best;

                Vector3 point = new Vector3(x, 0f, z);
                Vector3 query = new Vector3(x, bounds.center.y, z);
                Vector3 closest = collider.ClosestPoint(query);
                Vector2 delta = new Vector2(closest.x - point.x, closest.z - point.z);
                float distance = delta.magnitude;
                return distance < best ? distance : best;
            }

            private int NextVisitStamp()
            {
                if (currentVisitStamp == int.MaxValue)
                {
                    visitStamps.Clear();
                    currentVisitStamp = 0;
                }

                currentVisitStamp++;
                return currentVisitStamp;
            }

            private bool TryMarkVisited(Collider collider, int visitStamp)
            {
                if (collider == null)
                    return false;

                if (visitStamps.TryGetValue(collider, out int stamp) && stamp == visitStamp)
                    return false;

                visitStamps[collider] = visitStamp;
                return true;
            }

            private bool TryGetBucket(float x, float z, out List<Collider> bucket)
            {
                return buckets.TryGetValue(new Vector2Int(ToBucket(x), ToBucket(z)), out bucket);
            }

            private int ToBucket(float value)
            {
                return Mathf.FloorToInt(value / bucketSize);
            }

            private static bool ContainsXZ(Bounds bounds, float x, float z)
            {
                return x >= bounds.min.x && x <= bounds.max.x && z >= bounds.min.z && z <= bounds.max.z;
            }
        }

        private static FlowNavigationGridAsset LoadOrCreateAsset(string assetPath)
        {
            FlowNavigationGridAsset asset = AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>(assetPath);
            if (asset != null)
                return asset;

            asset = ScriptableObject.CreateInstance<FlowNavigationGridAsset>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            string folder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;

            throw new InvalidOperationException($"FlowNavigationGridPrefabBaker failed: asset folder does not exist: {folder}.");
        }
    }
}
