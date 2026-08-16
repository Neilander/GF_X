using UnityEngine;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AAAGame.Tilemap;

namespace AAAGame.MiniMap.FOG3
{
    public static class Fog3TerrainDetector
    {
        public static Fog3TerrainInfo Detect(Fog3TerrainSettings settings)
        {
            settings ??= new Fog3TerrainSettings();

            if (settings.RequireTileWorldCreatorManager)
            {
                if (TryDetectTileWorld(settings, out Fog3TerrainInfo tileWorldTerrainInfo))
                    return tileWorldTerrainInfo;

                return null;
            }

            if (settings.SourceMode == Fog3TerrainSourceMode.TileWorldCreator || settings.SourceMode == Fog3TerrainSourceMode.Auto)
            {
                if (TryDetectTileWorld(settings, out Fog3TerrainInfo terrainInfo))
                    return terrainInfo;
            }

            if (settings.SourceMode == Fog3TerrainSourceMode.TileWorldCreator)
                return null;

            if (settings.SourceMode == Fog3TerrainSourceMode.AstarGridGraph || settings.SourceMode == Fog3TerrainSourceMode.Auto)
            {
                if (TryDetectAstarGrid(out Fog3TerrainInfo terrainInfo))
                    return terrainInfo;
            }

            if (settings.SourceMode == Fog3TerrainSourceMode.ColliderBounds || settings.SourceMode == Fog3TerrainSourceMode.Auto)
            {
                if (TryDetectColliderBounds(settings, out Fog3TerrainInfo terrainInfo))
                    return terrainInfo;
            }

            return settings.SourceMode == Fog3TerrainSourceMode.Manual ? CreateManual(settings) : null;
        }

        private static Fog3TerrainInfo CreateManual(Fog3TerrainSettings settings)
        {
            int width = Mathf.Max(1, settings.ManualWidth);
            int height = Mathf.Max(1, settings.ManualHeight);
            return new Fog3TerrainInfo(width, height, settings.ManualCellSize, settings.ManualOrigin, CreateFilledMask(width, height, true), "Manual");
        }

        private static bool TryDetectTileWorld(Fog3TerrainSettings settings, out Fog3TerrainInfo terrainInfo)
        {
            terrainInfo = null;
            GiantGrey.TileWorldCreator.TileWorldCreatorManager manager =
                UnityEngine.Object.FindObjectOfType<GiantGrey.TileWorldCreator.TileWorldCreatorManager>();

            if (manager == null || manager.configuration == null)
                return false;

            int width = Mathf.Max(1, manager.configuration.width);
            int height = Mathf.Max(1, manager.configuration.height);
            float cellSize = Mathf.Max(0.01f, manager.configuration.cellSize);
            Vector3 origin = manager.transform.position - new Vector3(cellSize * 0.5f, 0f, cellSize * 0.5f);
            float platformEdgeInset = ResolvePlatformEdgeInset(manager, cellSize);
            bool[] walkable = CreateFilledMask(width, height, true);
            BuildTileWorldHeightData(
                manager,
                width,
                height,
                out int[] platformHeights,
                out bool[] slopeMask,
                out Fog3SlopeCellInfo[] slopeCells);

            if (settings.UseTileWorldBlueprintLayerAsWalkable && !string.IsNullOrEmpty(settings.TileWorldWalkableLayerName))
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                        walkable[x + y * width] = manager.CellPositionExists(settings.TileWorldWalkableLayerName, new Vector2(x, y));
                }
            }
            else if (settings.SampleWalkableWithPhysics)
            {
                ApplyPhysicsWalkableMask(walkable, width, height, cellSize, origin, settings);
            }

            terrainInfo = new Fog3TerrainInfo(
                width,
                height,
                cellSize,
                origin,
                walkable,
                platformHeights,
                slopeMask,
                slopeCells,
                platformEdgeInset,
                "TileWorldCreator");
            return true;
        }

        private static float ResolvePlatformEdgeInset(
            GiantGrey.TileWorldCreator.TileWorldCreatorManager manager,
            float cellSize)
        {
            var slopeBuildLayers = new List<LdtkSlopeBuildLayer>();
            if (manager.configuration.buildLayerFolders != null)
            {
                foreach (GiantGrey.TileWorldCreator.BuildLayerFolder folder in manager.configuration.buildLayerFolders)
                {
                    if (folder?.buildLayers == null)
                        continue;

                    foreach (GiantGrey.TileWorldCreator.BuildLayer buildLayer in folder.buildLayers)
                    {
                        if (buildLayer is LdtkSlopeBuildLayer slopeBuildLayer)
                            slopeBuildLayers.Add(slopeBuildLayer);
                    }
                }
            }

            if (slopeBuildLayers.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Fog3 TileWorld terrain requires exactly one LdtkSlopeBuildLayer, actual={slopeBuildLayers.Count}.");
            }

            float inset = slopeBuildLayers[0].platformEndExtension * cellSize;
            if (inset < 0f || inset >= cellSize * 0.5f)
            {
                throw new InvalidOperationException(
                    $"Fog3 TileWorld platform edge inset must be in [0, {cellSize * 0.5f}), actual={inset}.");
            }

            return inset;
        }

        private static void BuildTileWorldHeightData(
            GiantGrey.TileWorldCreator.TileWorldCreatorManager manager,
            int width,
            int height,
            out int[] platformHeights,
            out bool[] slopeMask,
            out Fog3SlopeCellInfo[] slopeCells)
        {
            int length = checked(width * height);
            var resolvedPlatformHeights = new int[length];
            var resolvedSlopeMask = new bool[length];
            var resolvedSlopeCells = new Fog3SlopeCellInfo[length];
            Array.Fill(resolvedPlatformHeights, -1);

            bool foundPlatformLayer = false;
            for (int platformHeight = 0; platformHeight <= 5; platformHeight++)
            {
                GiantGrey.TileWorldCreator.BlueprintLayer layer = manager.GetBlueprintLayer("Plane_H" + platformHeight);
                if (layer == null)
                    continue;

                foundPlatformLayer = true;
                ApplyLayerCells(layer, width, height, cell =>
                {
                    int index = cell.x + cell.y * width;
                    if (platformHeight > resolvedPlatformHeights[index])
                        resolvedPlatformHeights[index] = platformHeight;
                });
            }

            if (!foundPlatformLayer)
                throw new InvalidOperationException("Fog3 TileWorld terrain requires at least one Plane_H0..Plane_H5 blueprint layer.");

            GiantGrey.TileWorldCreator.BlueprintLayer slopeLayer = manager.GetBlueprintLayer("Slope");
            if (slopeLayer != null)
            {
                ApplyLayerCells(slopeLayer, width, height, cell => resolvedSlopeMask[cell.x + cell.y * width] = true);
            }

            if (resolvedSlopeMask.Any(value => value))
            {
                var topHeights = new Dictionary<Vector2, int>();
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int platformHeight = resolvedPlatformHeights[x + y * width];
                        if (platformHeight >= 0)
                            topHeights.Add(new Vector2(x, y), platformHeight);
                    }
                }

                if (!LdtkSlopeLayoutResolver.TryResolve(
                        slopeLayer.allPositions.ToHashSet(),
                        topHeights,
                        0f,
                        90f,
                        out LdtkSlopeLayout layout,
                        out string error))
                {
                    throw new InvalidOperationException("Cannot resolve fog slope height metadata: " + error);
                }

                foreach (LdtkSlopeRamp ramp in layout.ramps)
                {
                    Vector2 firstCell = ramp.anchor - ramp.direction * ((ramp.run - 1) * 0.5f);
                    int directionX = Mathf.RoundToInt(ramp.direction.x);
                    int directionY = Mathf.RoundToInt(ramp.direction.y);
                    for (int offset = 0; offset < ramp.run; offset++)
                    {
                        Vector2 position = firstCell + ramp.direction * offset;
                        int x = Mathf.RoundToInt(position.x);
                        int y = Mathf.RoundToInt(position.y);
                        if (!Mathf.Approximately(position.x, x) || !Mathf.Approximately(position.y, y))
                            throw new InvalidOperationException($"Resolved fog slope contains a non-grid cell {position}.");

                        int index = checked(x + y * width);
                        if (resolvedSlopeCells[index].IsDefined)
                            throw new InvalidOperationException($"Fog slope cell ({x},{y}) belongs to multiple ramps.");
                        resolvedSlopeCells[index] = new Fog3SlopeCellInfo(
                            directionX,
                            directionY,
                            checked(ramp.lowHeight * ramp.run + offset * ramp.rise),
                            ramp.run,
                            ramp.rise);
                    }
                }
            }

            for (int i = 0; i < resolvedSlopeMask.Length; i++)
            {
                if (resolvedSlopeMask[i] && resolvedPlatformHeights[i] < 0)
                {
                    int x = i % width;
                    int y = i / width;
                    throw new InvalidOperationException(
                        $"Fog3 slope cell ({x},{y}) has no inferred Plane_H* support. Reimport the LDtk level before play.");
                }
                if (resolvedSlopeMask[i] && !resolvedSlopeCells[i].IsDefined)
                {
                    int x = i % width;
                    int y = i / width;
                    throw new InvalidOperationException($"Fog3 slope cell ({x},{y}) has no resolved height metadata.");
                }
            }

            platformHeights = resolvedPlatformHeights;
            slopeMask = resolvedSlopeMask;
            slopeCells = resolvedSlopeCells;
        }

        private static void ApplyLayerCells(
            GiantGrey.TileWorldCreator.BlueprintLayer layer,
            int width,
            int height,
            Action<Vector2Int> apply)
        {
            if (layer == null)
                throw new ArgumentNullException(nameof(layer));
            if (apply == null)
                throw new ArgumentNullException(nameof(apply));
            if (!layer.isEnabled)
                return;

            foreach (Vector2 position in layer.allPositions)
            {
                int x = Mathf.RoundToInt(position.x);
                int y = Mathf.RoundToInt(position.y);
                if (!Mathf.Approximately(position.x, x) || !Mathf.Approximately(position.y, y))
                    throw new InvalidOperationException($"Fog3 blueprint layer '{layer.layerName}' contains a non-grid cell {position}.");
                if (x < 0 || x >= width || y < 0 || y >= height)
                {
                    throw new InvalidOperationException(
                        $"Fog3 blueprint layer '{layer.layerName}' cell ({x},{y}) is outside {width}x{height}.");
                }

                apply(new Vector2Int(x, y));
            }
        }

        private static bool TryDetectAstarGrid(out Fog3TerrainInfo terrainInfo)
        {
            terrainInfo = null;
            Type astarType = FindType("Pathfinding.AstarPath");
            if (astarType == null)
                return false;

            object active = GetStaticValue(astarType, "active");
            object data = GetMemberValue(active, "data");
            object graphsObject = GetMemberValue(data, "graphs");
            if (graphsObject is not IEnumerable graphs)
                return false;

            foreach (object graph in graphs)
            {
                if (graph == null || graph.GetType().Name != "GridGraph")
                    continue;

                int width = GetIntMember(graph, "width", 0);
                int height = GetIntMember(graph, "depth", 0);
                float cellSize = Mathf.Max(0.01f, GetFloatMember(graph, "nodeSize", 1f));
                Vector3 center = GetVector3Member(graph, "center", Vector3.zero);
                if (width <= 0 || height <= 0)
                    continue;

                Vector3 origin = center - new Vector3(width * cellSize * 0.5f, 0f, height * cellSize * 0.5f);
                bool[] walkable = CreateFilledMask(width, height, true);
                MethodInfo getNodeMethod = graph.GetType().GetMethod("GetNode", BindingFlags.Instance | BindingFlags.Public);

                if (getNodeMethod != null)
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            object node = getNodeMethod.Invoke(graph, new object[] { x, y });
                            walkable[x + y * width] = node != null && GetBoolMember(node, "Walkable", true);
                        }
                    }

                    object first = getNodeMethod.Invoke(graph, new object[] { 0, 0 });
                    object last = getNodeMethod.Invoke(graph, new object[] { width - 1, height - 1 });
                    if (TryGetNodePosition(first, out Vector3 firstPos) && TryGetNodePosition(last, out Vector3 lastPos))
                    {
                        origin = new Vector3(
                            Mathf.Min(firstPos.x, lastPos.x) - cellSize * 0.5f,
                            Mathf.Min(firstPos.y, lastPos.y),
                            Mathf.Min(firstPos.z, lastPos.z) - cellSize * 0.5f);
                    }
                }

                terrainInfo = new Fog3TerrainInfo(width, height, cellSize, origin, walkable, "AstarGridGraph");
                return true;
            }

            return false;
        }

        private static bool TryDetectColliderBounds(Fog3TerrainSettings settings, out Fog3TerrainInfo terrainInfo)
        {
            terrainInfo = null;
            LayerMask groundMask = settings.GroundMask;
            if (groundMask.value == 0)
                groundMask = LayerMask.GetMask("Ground");
            if (groundMask.value == 0)
                return false;

            Collider[] colliders = UnityEngine.Object.FindObjectsOfType<Collider>();
            Bounds bounds = default;
            bool hasBounds = false;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || collider.isTrigger || (groundMask.value & (1 << collider.gameObject.layer)) == 0)
                    continue;

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            if (!hasBounds)
                return false;

            float cellSize = Mathf.Max(0.01f, settings.ManualCellSize);
            int width = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / cellSize));
            int height = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / cellSize));
            Vector3 origin = new Vector3(bounds.min.x, bounds.min.y, bounds.min.z);
            bool[] walkable = CreateFilledMask(width, height, true);

            if (settings.SampleWalkableWithPhysics)
                ApplyPhysicsWalkableMask(walkable, width, height, cellSize, origin, settings);

            terrainInfo = new Fog3TerrainInfo(width, height, cellSize, origin, walkable, "ColliderBounds");
            return true;
        }

        private static void ApplyPhysicsWalkableMask(bool[] mask, int width, int height, float cellSize, Vector3 origin, Fog3TerrainSettings settings)
        {
            LayerMask groundMask = settings.GroundMask;
            if (groundMask.value == 0)
                groundMask = LayerMask.GetMask("Ground");
            if (groundMask.value == 0)
                return;

            float sampleHeight = Mathf.Max(1f, settings.PhysicsSampleHeight);
            float sampleDistance = Mathf.Max(sampleHeight, settings.PhysicsSampleDistance);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector3 sample = origin + new Vector3((x + 0.5f) * cellSize, sampleHeight, (y + 0.5f) * cellSize);
                    mask[x + y * width] = Physics.Raycast(sample, Vector3.down, sampleDistance, groundMask, QueryTriggerInteraction.Ignore);
                }
            }
        }

        private static bool[] CreateFilledMask(int width, int height, bool value)
        {
            bool[] mask = new bool[Mathf.Max(1, width * height)];
            for (int i = 0; i < mask.Length; i++)
                mask[i] = value;
            return mask;
        }

        private static Type FindType(string fullName)
        {
            Type type = Type.GetType(fullName);
            if (type != null)
                return type;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(fullName);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static object GetStaticValue(Type type, string memberName)
        {
            if (type == null)
                return null;

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null)
                return property.GetValue(null, null);

            FieldInfo field = type.GetField(memberName, flags);
            return field != null ? field.GetValue(null) : null;
        }

        private static object GetMemberValue(object target, string memberName)
        {
            if (target == null)
                return null;

            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null)
                return property.GetValue(target, null);

            FieldInfo field = type.GetField(memberName, flags);
            return field != null ? field.GetValue(target) : null;
        }

        private static int GetIntMember(object target, string memberName, int fallback)
        {
            object value = GetMemberValue(target, memberName);
            return value != null ? Convert.ToInt32(value) : fallback;
        }

        private static float GetFloatMember(object target, string memberName, float fallback)
        {
            object value = GetMemberValue(target, memberName);
            return value != null ? Convert.ToSingle(value) : fallback;
        }

        private static bool GetBoolMember(object target, string memberName, bool fallback)
        {
            object value = GetMemberValue(target, memberName);
            return value != null ? Convert.ToBoolean(value) : fallback;
        }

        private static Vector3 GetVector3Member(object target, string memberName, Vector3 fallback)
        {
            object value = GetMemberValue(target, memberName);
            return value is Vector3 vector ? vector : fallback;
        }

        private static bool TryGetNodePosition(object node, out Vector3 position)
        {
            position = Vector3.zero;
            object value = GetMemberValue(node, "position");
            if (value == null)
                return false;

            if (value is Vector3 vector)
            {
                position = vector;
                return true;
            }

            MethodInfo[] methods = value.GetType().GetMethods(BindingFlags.Static | BindingFlags.Public);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != "op_Explicit" || method.ReturnType != typeof(Vector3))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == value.GetType())
                {
                    position = (Vector3)method.Invoke(null, new[] { value });
                    return true;
                }
            }

            return false;
        }
    }
}
