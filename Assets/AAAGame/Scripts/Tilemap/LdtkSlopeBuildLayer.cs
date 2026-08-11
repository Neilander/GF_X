using System;
using System.Collections.Generic;
using System.Linq;
using GiantGrey.TileWorldCreator;
using UnityEngine;

namespace AAAGame.Tilemap
{
    public sealed class LdtkSlopeLayout
    {
        public readonly List<LdtkSlopeRamp> ramps = new List<LdtkSlopeRamp>();
        public readonly Dictionary<Vector2, int> requiredPlatformHeights = new Dictionary<Vector2, int>();
    }

    public sealed class LdtkSlopeRamp
    {
        public Vector2 direction;
        public Vector2 anchor;
        public int lowHeight;
        public int run;
        public int rise;
    }

    public static class LdtkSlopeLayoutResolver
    {
        private static readonly Vector2[] CardinalDirections =
        {
            Vector2.right,
            Vector2.left,
            Vector2.up,
            Vector2.down
        };

        private sealed class ComponentResolution
        {
            public Vector2 direction;
            public int lowHeight;
            public int run;
            public int rise;
            public List<Vector2> lowEdgeCells;
        }

        public static bool TryResolve(
            HashSet<Vector2> slopeCells,
            Dictionary<Vector2, int> topPlatformHeights,
            float highEndExtension,
            float maximumSlopeAngle,
            out LdtkSlopeLayout layout,
            out string error)
        {
            layout = null;
            error = null;
            if (slopeCells == null)
            {
                error = "Slope cells are missing.";
                return false;
            }

            if (topPlatformHeights == null)
            {
                error = "Platform heights are missing.";
                return false;
            }

            if (highEndExtension < 0f)
            {
                error = "Slope high-end extension cannot be negative: " + highEndExtension + ".";
                return false;
            }

            if (maximumSlopeAngle <= 0f || maximumSlopeAngle > 90f)
            {
                error = "Maximum walkable slope angle must be in (0, 90]: " + maximumSlopeAngle + ".";
                return false;
            }

            var result = new LdtkSlopeLayout();
            var visited = new HashSet<Vector2>();
            foreach (Vector2 start in slopeCells.OrderBy(cell => cell.y).ThenBy(cell => cell.x))
            {
                if (!visited.Add(start))
                {
                    continue;
                }

                HashSet<Vector2> component = ReadComponent(start, slopeCells, visited);
                var candidates = new List<ComponentResolution>();
                foreach (Vector2 direction in CardinalDirections)
                {
                    if (TryResolveDirection(component, direction, topPlatformHeights, out ComponentResolution candidate))
                    {
                        candidates.Add(candidate);
                    }
                }

                if (candidates.Count != 1)
                {
                    error = "Slope direction/height cannot be inferred uniquely for cells: " + FormatCells(component) +
                            ". Each component must be a straight rectangular run, and both ends must touch uniform platform heights.";
                    return false;
                }

                ComponentResolution resolution = candidates[0];
                float slopeAngle = Mathf.Atan2(resolution.rise, resolution.run + highEndExtension) * Mathf.Rad2Deg;
                if (slopeAngle > maximumSlopeAngle + 0.001f)
                {
                    error = "Slope cells " + FormatCells(component) + " produce a " +
                            slopeAngle.ToString("0.##") + " degree ramp (run " + resolution.run +
                            ", rise " + resolution.rise + "), exceeding the unit walkable limit of " +
                            maximumSlopeAngle.ToString("0.##") + " degrees.";
                    return false;
                }

                if (!AppendRamp(component, resolution, topPlatformHeights, result, out error))
                {
                    return false;
                }
            }

            layout = result;
            return true;
        }

        public static Dictionary<Vector2, int> GetTopPlatformHeights(IEnumerable<KeyValuePair<int, IEnumerable<Vector2>>> platformLevels)
        {
            if (platformLevels == null)
            {
                throw new ArgumentNullException(nameof(platformLevels));
            }

            var result = new Dictionary<Vector2, int>();
            foreach (KeyValuePair<int, IEnumerable<Vector2>> level in platformLevels)
            {
                if (level.Value == null)
                {
                    throw new InvalidOperationException("Platform cells are missing for Plane_H" + level.Key + ".");
                }

                foreach (Vector2 cell in level.Value)
                {
                    if (!result.TryGetValue(cell, out int currentHeight) || level.Key > currentHeight)
                    {
                        result[cell] = level.Key;
                    }
                }
            }

            return result;
        }

        private static bool TryResolveDirection(
            HashSet<Vector2> component,
            Vector2 direction,
            Dictionary<Vector2, int> topPlatformHeights,
            out ComponentResolution resolution)
        {
            resolution = null;
            int minProjection = component.Min(cell => Project(cell, direction));
            int maxProjection = component.Max(cell => Project(cell, direction));
            int run = maxProjection - minProjection + 1;
            List<Vector2> lowEdgeCells = component
                .Where(cell => Project(cell, direction) == minProjection)
                .OrderBy(cell => Project(cell, new Vector2(-direction.y, direction.x)))
                .ToList();
            if (component.Count != lowEdgeCells.Count * run)
            {
                return false;
            }

            foreach (Vector2 lowEdgeCell in lowEdgeCells)
            {
                for (int offset = 0; offset < run; offset++)
                {
                    if (!component.Contains(lowEdgeCell + direction * offset))
                    {
                        return false;
                    }
                }
            }

            int? lowHeight = null;
            int? highHeight = null;
            foreach (Vector2 lowEdgeCell in lowEdgeCells)
            {
                if (!topPlatformHeights.TryGetValue(lowEdgeCell - direction, out int currentLowHeight) ||
                    !topPlatformHeights.TryGetValue(lowEdgeCell + direction * run, out int currentHighHeight))
                {
                    return false;
                }

                lowHeight = lowHeight ?? currentLowHeight;
                highHeight = highHeight ?? currentHighHeight;
                if (lowHeight.Value != currentLowHeight || highHeight.Value != currentHighHeight)
                {
                    return false;
                }
            }

            int rise = highHeight.Value - lowHeight.Value;
            if (rise <= 0)
            {
                return false;
            }

            resolution = new ComponentResolution
            {
                direction = direction,
                lowHeight = lowHeight.Value,
                run = run,
                rise = rise,
                lowEdgeCells = lowEdgeCells
            };
            return true;
        }

        private static bool AppendRamp(
            HashSet<Vector2> component,
            ComponentResolution resolution,
            Dictionary<Vector2, int> topPlatformHeights,
            LdtkSlopeLayout layout,
            out string error)
        {
            error = null;
            foreach (Vector2 lowEdgeCell in resolution.lowEdgeCells)
            {
                for (int offset = 0; offset < resolution.run; offset++)
                {
                    Vector2 cell = lowEdgeCell + resolution.direction * offset;
                    int requiredHeight = resolution.lowHeight + offset * resolution.rise / resolution.run;
                    if (!component.Contains(cell))
                    {
                        error = "Slope component is not rectangular at " + FormatCell(cell) + ".";
                        return false;
                    }

                    if (topPlatformHeights.TryGetValue(cell, out int explicitHeight) && explicitHeight > requiredHeight)
                    {
                        error = "Slope cell " + FormatCell(cell) + " requires support at Plane_H" + requiredHeight +
                                ", but its top platform is Plane_H" + explicitHeight + ".";
                        return false;
                    }

                    layout.requiredPlatformHeights[cell] = requiredHeight;
                }

                layout.ramps.Add(new LdtkSlopeRamp
                {
                    direction = resolution.direction,
                    anchor = lowEdgeCell + resolution.direction * ((resolution.run - 1) * 0.5f),
                    lowHeight = resolution.lowHeight,
                    run = resolution.run,
                    rise = resolution.rise
                });
            }

            return true;
        }

        private static HashSet<Vector2> ReadComponent(Vector2 start, HashSet<Vector2> positions, HashSet<Vector2> visited)
        {
            var result = new HashSet<Vector2>();
            var queue = new Queue<Vector2>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Vector2 current = queue.Dequeue();
                result.Add(current);
                foreach (Vector2 direction in CardinalDirections)
                {
                    Vector2 next = current + direction;
                    if (positions.Contains(next) && visited.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            return result;
        }

        private static int Project(Vector2 cell, Vector2 direction)
        {
            return Mathf.RoundToInt(Vector2.Dot(cell, direction));
        }

        private static string FormatCells(IEnumerable<Vector2> cells)
        {
            return string.Join(", ", cells.OrderBy(cell => cell.y).ThenBy(cell => cell.x).Select(FormatCell));
        }

        private static string FormatCell(Vector2 cell)
        {
            return "(" + Mathf.RoundToInt(cell.x) + "," + Mathf.RoundToInt(cell.y) + ")";
        }
    }

    [Serializable]
    public sealed class LdtkSlopeBuildLayer : BuildLayer
    {
        [Serializable]
        public sealed class PlatformLevel
        {
            public int height;
            public BlueprintLayer layer;
        }

        public BlueprintLayer slopeLayer;
        public List<PlatformLevel> platformLevels = new List<PlatformLevel>();
        public GameObject rampPrefab;
        public float highEndExtension;
        public float maximumSlopeAngle = 45f;
        public float surfaceBaseHeight;
        public float surfaceHeightStep = 1f;
        public Vector3 layerOffset = Vector3.zero;
        public LayerMask objectLayer;

        private GameObject slopeLayerObject;

        public override void ResetLayer(TileWorldCreatorManager owner)
        {
            if (owner == null)
            {
                throw new InvalidOperationException("Slope build layer requires a TWC manager: " + layerName);
            }

            slopeLayerObject = GetLayerObject(owner.gameObject);
            if (slopeLayerObject == null)
            {
                throw new InvalidOperationException("Slope build layer has no TWC manager hierarchy object: " + layerName);
            }

            ClearGeneratedObjects();
        }

        public override void ExecuteLayer(Configuration configuration, GameObject owner, TileWorldCreatorManager twcManager)
        {
            if (!isEnabled)
            {
                return;
            }

            if (twcManager == null)
            {
                throw new InvalidOperationException("Slope build layer requires a TWC manager: " + layerName);
            }

            if (slopeLayer == null)
            {
                throw new InvalidOperationException("Slope build layer has no slope blueprint: " + layerName);
            }

            if (rampPrefab == null)
            {
                throw new InvalidOperationException("Slope build layer requires a ramp prefab: " + layerName);
            }

            if (highEndExtension < 0f)
            {
                throw new InvalidOperationException("Slope build layer has a negative high-end extension: " + highEndExtension + ".");
            }

            if (maximumSlopeAngle <= 0f || maximumSlopeAngle > 90f)
            {
                throw new InvalidOperationException("Slope build layer has an invalid maximum slope angle: " + maximumSlopeAngle + ".");
            }

            slopeLayerObject = GetLayerObject(owner);
            if (slopeLayerObject == null)
            {
                throw new InvalidOperationException("Slope build layer hierarchy object could not be created: " + layerName);
            }

            ClearGeneratedObjects();

            if (platformLevels == null || platformLevels.Count == 0)
            {
                throw new InvalidOperationException("Slope build layer has no platform levels: " + layerName);
            }

            var levels = new List<KeyValuePair<int, IEnumerable<Vector2>>>();
            foreach (PlatformLevel level in platformLevels)
            {
                if (level == null || level.layer == null)
                {
                    throw new InvalidOperationException("Slope build layer contains a missing platform level: " + layerName);
                }

                levels.Add(new KeyValuePair<int, IEnumerable<Vector2>>(level.height, level.layer.allPositions));
            }

            Dictionary<Vector2, int> topHeights = LdtkSlopeLayoutResolver.GetTopPlatformHeights(levels);
            if (!LdtkSlopeLayoutResolver.TryResolve(
                    slopeLayer.allPositions.ToHashSet(),
                    topHeights,
                    highEndExtension,
                    maximumSlopeAngle,
                    out LdtkSlopeLayout layout,
                    out string error))
            {
                throw new InvalidOperationException(error);
            }

            int targetLayer = objectLayer.value;
            if (targetLayer < 0 || targetLayer > 31)
            {
                throw new InvalidOperationException("Slope build layer has an invalid object layer: " + targetLayer + ".");
            }

            var spawnedRamps = new HashSet<(Vector2 anchor, Vector2 direction, int lowHeight, int run, int rise)>();
            foreach (LdtkSlopeRamp ramp in layout.ramps)
            {
                Vector2 lateral = new Vector2(-ramp.direction.y, ramp.direction.x);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 dualGridAnchor = ramp.anchor + lateral * (side * 0.5f);
                    var key = (dualGridAnchor, ramp.direction, ramp.lowHeight, ramp.run, ramp.rise);
                    if (spawnedRamps.Add(key))
                    {
                        SpawnRamp(ramp, dualGridAnchor, configuration.cellSize, targetLayer, slopeLayerObject.transform);
                    }
                }
            }
        }

        private void ClearGeneratedObjects()
        {
            for (int i = slopeLayerObject.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = slopeLayerObject.transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(child);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(child);
                }
            }
        }

        private void SpawnRamp(LdtkSlopeRamp ramp, Vector2 dualGridAnchor, float cellSize, int targetLayer, Transform parent)
        {
            float y = surfaceBaseHeight + ramp.lowHeight * surfaceHeightStep + layerOffset.y;
            Vector2 alignedAnchor = dualGridAnchor - ramp.direction * 0.5f + ramp.direction * (highEndExtension * 0.5f);
            GameObject instance = UnityEngine.Object.Instantiate(rampPrefab, parent);
            instance.name = rampPrefab.name + "_" + alignedAnchor.x.ToString("0.#") + "_" + alignedAnchor.y.ToString("0.#");
            instance.transform.localPosition = new Vector3(
                alignedAnchor.x * cellSize + layerOffset.x,
                y,
                alignedAnchor.y * cellSize + layerOffset.z);
            instance.transform.localRotation = Quaternion.LookRotation(
                new Vector3(ramp.direction.x, 0f, ramp.direction.y),
                Vector3.up);
            instance.transform.localScale = new Vector3(
                cellSize,
                ramp.rise * cellSize,
                (ramp.run + highEndExtension) * cellSize);
            SetLayerRecursively(instance.transform, targetLayer);
        }

        private static void SetLayerRecursively(Transform root, int targetLayer)
        {
            root.gameObject.layer = targetLayer;
            for (int i = 0; i < root.childCount; i++)
            {
                SetLayerRecursively(root.GetChild(i), targetLayer);
            }
        }
    }
}
