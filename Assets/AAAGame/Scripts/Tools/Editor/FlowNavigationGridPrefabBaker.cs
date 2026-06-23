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
            public int Width;
            public int Height;
            public int WalkableCount;
            public int BlockedCount;
            public int GroundColliderCount;
            public int ObstacleColliderCount;
        }

        public static Result BakeFromTerrainPrefab(
            string terrainPrefabPath,
            string assetPath,
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

                FlowNavigationGridAsset asset = LoadOrCreateAsset(assetPath);
                int preservedAgentTypeId = asset.AgentTypeId;
                asset.Resize(width, height, cellSize, defaultWalkable: false);
                asset.SetOrigin(gridOrigin);
                asset.SetAgentTypeId(preservedAgentTypeId);

                int walkableCount = 0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Vector3 sample = new Vector3((x + 0.5f) * cellSize, 0f, (y + 0.5f) * cellSize);
                        bool walkable = RaycastVertical(groundColliders, sample.x, sample.z)
                                        && !RaycastVertical(obstacleColliders, sample.x, sample.z);
                        asset.SetCellWalkable(x, y, walkable);
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

        public static void AttachSourceToLevelPrefab(GameObject prefabRoot, FlowNavigationGridAsset grid)
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
            source.Configure(grid, applyOnEnable: true, applyInEditMode: false, clearOnDisable: true);
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
