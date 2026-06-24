using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AAAGame.Tools.Editor
{
    public static class FlowNavigationGridPrefabBaker
    {
        private const string GroundLayerName = "Ground";
        private const string LevelObstacleLayerName = "LevelObstacle";

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

            GameObject terrainPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(terrainPrefabPath);
            if (terrainPrefab == null)
                throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.BakeFromTerrainPrefab failed: terrain prefab not found at {terrainPrefabPath}.");

            EnsureAssetFolder(assetPath);

            GameObject terrainRoot = PrefabUtility.LoadPrefabContents(terrainPrefabPath);
            try
            {
                int groundLayer = RequireLayer(GroundLayerName);
                int obstacleLayer = LayerMask.NameToLayer(LevelObstacleLayerName);
                Collider[] groundColliders = CollectColliders(terrainRoot, groundLayer);
                Collider[] obstacleColliders = obstacleLayer >= 0
                    ? CollectColliders(terrainRoot, obstacleLayer)
                    : Array.Empty<Collider>();

                if (groundColliders.Length == 0)
                    throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.BakeFromTerrainPrefab failed: no enabled non-trigger collider on layer '{GroundLayerName}' in {terrainPrefabPath}.");

                bool[] baseWalkable = new bool[width * height];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Vector3 sample = new Vector3((x + 0.5f) * cellSize, 0f, (y + 0.5f) * cellSize);
                        baseWalkable[x + y * width] = RaycastVertical(groundColliders, sample.x, sample.z)
                                                       && !RaycastVertical(obstacleColliders, sample.x, sample.z);
                    }
                }

                bool[] movementWalkable = BuildHardClearanceMask(baseWalkable, width, height, cellSize, hardClearanceRadius);
                byte[] movementCosts = BuildSourceCostField(movementWalkable, width, height, hardClearanceRadius, cellSize);

                FlowNavigationGridAsset asset = LoadOrCreateAsset(assetPath);
                asset.Resize(width, height, cellSize, defaultWalkable: false);
                asset.SetOrigin(gridOrigin);
                asset.SetAgentTypeId(agentTypeId);

                int walkableCount = 0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        bool walkable = movementWalkable[x + y * width];
                        asset.SetCellWalkable(x, y, walkable);
                        asset.SetCellCost(x, y, movementCosts[x + y * width]);
                        if (walkable)
                            walkableCount++;
                    }
                }

                if (walkableCount == 0)
                    throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.BakeFromTerrainPrefab failed: generated zero walkable cells from {terrainPrefabPath}.");

                EditorUtility.SetDirty(asset);
                AssetDatabase.ImportAsset(assetPath);

                return new Result
                {
                    Asset = asset,
                    AssetPath = assetPath,
                    AgentTypeId = asset.AgentTypeId,
                    Width = width,
                    Height = height,
                    WalkableCount = walkableCount,
                    BlockedCount = width * height - walkableCount,
                    GroundColliderCount = groundColliders.Length,
                    ObstacleColliderCount = obstacleColliders.Length
                };
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
            if (requests == null)
                throw new InvalidOperationException("FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab failed: requests is null.");
            if (requests.Count == 0)
                throw new InvalidOperationException("FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab failed: requests is empty.");

            Result[] results = new Result[requests.Count];
            HashSet<int> agentTypeIds = new HashSet<int>();
            for (int i = 0; i < requests.Count; i++)
            {
                MovementTypeBakeRequest request = requests[i];
                if (!agentTypeIds.Add(request.AgentTypeId))
                    throw new InvalidOperationException($"FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab failed: duplicate agentTypeId={request.AgentTypeId}.");

                results[i] = BakeFromTerrainPrefab(
                    terrainPrefabPath,
                    request.AssetPath,
                    request.AgentTypeId,
                    request.HardClearanceRadius,
                    width,
                    height,
                    cellSize,
                    gridOrigin);
            }

            return results;
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

        private static bool RaycastVertical(IReadOnlyList<Collider> colliders, float x, float z)
        {
            if (colliders == null || colliders.Count == 0)
                return false;

            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < colliders.Count; i++)
            {
                Bounds bounds = colliders[i].bounds;
                if (x < bounds.min.x || x > bounds.max.x || z < bounds.min.z || z > bounds.max.z)
                    continue;

                minY = Mathf.Min(minY, bounds.min.y);
                maxY = Mathf.Max(maxY, bounds.max.y);
            }

            if (float.IsPositiveInfinity(minY))
                return false;

            Ray ray = new Ray(new Vector3(x, maxY + 8f, z), Vector3.down);
            float distance = Mathf.Max(0.1f, maxY - minY + 16f);
            for (int i = 0; i < colliders.Count; i++)
            {
                Bounds bounds = colliders[i].bounds;
                if (x < bounds.min.x || x > bounds.max.x || z < bounds.min.z || z > bounds.max.z)
                    continue;

                if (colliders[i].Raycast(ray, out _, distance))
                    return true;
            }

            return false;
        }

        private static bool[] BuildHardClearanceMask(bool[] baseWalkable, int width, int height, float cellSize, float hardClearanceRadius)
        {
            if (baseWalkable == null || baseWalkable.Length != width * height)
                throw new InvalidOperationException("FlowNavigationGridPrefabBaker.BuildHardClearanceMask failed: base mask is invalid.");

            bool[] result = (bool[])baseWalkable.Clone();
            if (hardClearanceRadius <= 0.0001f)
                return result;

            float clearanceSq = hardClearanceRadius * hardClearanceRadius;
            int radiusCells = Mathf.CeilToInt(hardClearanceRadius / Mathf.Max(0.0001f, cellSize));
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = x + y * width;
                    if (!baseWalkable[index])
                        continue;

                    bool blockedByClearance = false;
                    for (int oy = -radiusCells; oy <= radiusCells && !blockedByClearance; oy++)
                    {
                        int ny = y + oy;
                        if (ny < 0 || ny >= height)
                            continue;

                        for (int ox = -radiusCells; ox <= radiusCells; ox++)
                        {
                            int nx = x + ox;
                            if (nx < 0 || nx >= width)
                                continue;
                            if (baseWalkable[nx + ny * width])
                                continue;

                            float dx = Mathf.Max(0f, Mathf.Abs(ox) - 0.5f) * cellSize;
                            float dz = Mathf.Max(0f, Mathf.Abs(oy) - 0.5f) * cellSize;
                            if (dx * dx + dz * dz > clearanceSq)
                                continue;

                            blockedByClearance = true;
                            break;
                        }
                    }

                    if (blockedByClearance)
                        result[index] = false;
                }
            }

            return result;
        }

        private static byte[] BuildSourceCostField(bool[] walkable, int width, int height, float hardClearanceRadius, float cellSize)
        {
            if (walkable == null || walkable.Length != width * height)
                throw new InvalidOperationException("FlowNavigationGridPrefabBaker.BuildSourceCostField failed: walkable mask is invalid.");

            byte[] costs = new byte[walkable.Length];
            float blurRadiusCells = 2.25f + Mathf.Max(0f, (hardClearanceRadius - 0.5f) / Mathf.Max(0.001f, cellSize));
            int adjacentPenalty = 1 + Mathf.CeilToInt(Mathf.Max(0f, (hardClearanceRadius - 0.5f) / Mathf.Max(0.001f, cellSize)));
            int outerPenalty = adjacentPenalty > 1 ? 1 : 0;
            int radiusCells = Mathf.CeilToInt(blurRadiusCells);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = x + y * width;
                    if (!walkable[index])
                    {
                        costs[index] = byte.MaxValue;
                        continue;
                    }

                    float nearestWallDistance = float.PositiveInfinity;
                    for (int oy = -radiusCells; oy <= radiusCells; oy++)
                    {
                        int ny = y + oy;
                        if (ny < 0 || ny >= height)
                            continue;

                        for (int ox = -radiusCells; ox <= radiusCells; ox++)
                        {
                            int nx = x + ox;
                            if (nx < 0 || nx >= width)
                                continue;
                            if (walkable[nx + ny * width])
                                continue;

                            float distance = Mathf.Sqrt(ox * ox + oy * oy);
                            if (distance < nearestWallDistance)
                                nearestWallDistance = distance;
                        }
                    }

                    int penalty = 0;
                    if (!float.IsPositiveInfinity(nearestWallDistance) && nearestWallDistance <= blurRadiusCells)
                    {
                        float t = Mathf.Clamp01((blurRadiusCells - nearestWallDistance) / Mathf.Max(0.001f, blurRadiusCells - 1f));
                        penalty = Mathf.RoundToInt(Mathf.Lerp(outerPenalty, adjacentPenalty, t));
                    }

                    costs[index] = (byte)Mathf.Clamp(1 + penalty, 1, 254);
                }
            }

            return costs;
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
