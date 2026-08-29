using System;
using System.Collections.Generic;
using GiantGrey.TileWorldCreator;
using UnityEngine;

namespace AAAGame.MiniMap
{
    public sealed class MinimapTerrainBounds
    {
        public MinimapTerrainBounds(
            int gridWidth,
            int gridHeight,
            int terrainOffsetX,
            int terrainOffsetY,
            float cellSize,
            float worldMinX,
            float worldMaxX,
            float worldMinZ,
            float worldMaxZ,
            bool hasEnvironmentBackground)
        {
            GridWidth = gridWidth;
            GridHeight = gridHeight;
            TerrainOffsetX = terrainOffsetX;
            TerrainOffsetY = terrainOffsetY;
            CellSize = cellSize;
            WorldMinX = worldMinX;
            WorldMaxX = worldMaxX;
            WorldMinZ = worldMinZ;
            WorldMaxZ = worldMaxZ;
            HasEnvironmentBackground = hasEnvironmentBackground;
        }

        public int GridWidth { get; }
        public int GridHeight { get; }
        public int TerrainOffsetX { get; }
        public int TerrainOffsetY { get; }
        public float CellSize { get; }
        public float WorldMinX { get; }
        public float WorldMaxX { get; }
        public float WorldMinZ { get; }
        public float WorldMaxZ { get; }
        public bool HasEnvironmentBackground { get; }
    }

    public sealed class MinimapTerrainMapBuildResult
    {
        public MinimapTerrainMapBuildResult(
            Texture2D texture,
            MinimapTerrainBounds bounds,
            bool useCenteredGrid,
            int paintedCount,
            int groundPaintedCount,
            int waterPaintedCount,
            bool expectsGroundLayer,
            bool expectsWaterLayer)
        {
            Texture = texture;
            Bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
            UseCenteredGrid = useCenteredGrid;
            PaintedCount = paintedCount;
            GroundPaintedCount = groundPaintedCount;
            WaterPaintedCount = waterPaintedCount;
            ExpectsGroundLayer = expectsGroundLayer;
            ExpectsWaterLayer = expectsWaterLayer;
        }

        public Texture2D Texture { get; }
        public MinimapTerrainBounds Bounds { get; }
        public int GridWidth => Bounds.GridWidth;
        public int GridHeight => Bounds.GridHeight;
        public float CellSize => Bounds.CellSize;
        public bool UseCenteredGrid { get; }
        public int PaintedCount { get; }
        public int GroundPaintedCount { get; }
        public int WaterPaintedCount { get; }
        public bool ExpectsGroundLayer { get; }
        public bool ExpectsWaterLayer { get; }

        public bool GroundReady => !ExpectsGroundLayer || GroundPaintedCount > 0;
        public bool WaterReady => !ExpectsWaterLayer || WaterPaintedCount > 0;
        public bool IsTerrainReady => PaintedCount > 0 && GroundReady && WaterReady;
    }

    public static class MinimapTerrainMapBuilder
    {
        public static MinimapTerrainMapBuildResult Build(
            TileWorldCreatorManager manager,
            int textureMaxSize,
            string groundLayerKeyword,
            string waterLayerKeyword,
            Color groundLayerColor,
            Color waterLayerColor,
            Color32 backgroundColor,
            int minimumPaddingCells = 0,
            float targetAspect = 0f)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));
            if (manager.configuration == null)
                throw new InvalidOperationException("Minimap terrain map requires a TileWorldCreator Configuration.");

            Configuration configuration = manager.configuration;
            MinimapTerrainBounds bounds = ResolveBounds(manager, minimumPaddingCells, targetAspect);
            int sourceGridWidth = configuration.width;
            int sourceGridHeight = configuration.height;
            int texWidth = bounds.GridWidth;
            int texHeight = bounds.GridHeight;
            int maxDimension = Mathf.Max(texWidth, texHeight);
            if (textureMaxSize > 0 && maxDimension > textureMaxSize)
            {
                float scale = textureMaxSize / (float)maxDimension;
                texWidth = Mathf.Max(1, Mathf.RoundToInt(texWidth * scale));
                texHeight = Mathf.Max(1, Mathf.RoundToInt(texHeight * scale));
            }

            Texture2D texture = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            Color32 initialColor = bounds.HasEnvironmentBackground ? (Color32)waterLayerColor : backgroundColor;
            Color32[] pixels = new Color32[texWidth * texHeight];
            Array.Fill(pixels, initialColor);

            HashSet<Vector2> groundPositions = CollectBlueprintLayerCells(configuration, groundLayerKeyword);
            HashSet<Vector2> waterPositions = CollectBlueprintLayerCells(configuration, waterLayerKeyword);
            HashSet<Vector2> coordinateSamplePositions = new HashSet<Vector2>(groundPositions);
            coordinateSamplePositions.UnionWith(waterPositions);
            if (coordinateSamplePositions.Count == 0)
                coordinateSamplePositions = CollectAllBlueprintCells(configuration);

            bool useCenteredGrid = DetermineGridCoordinateMode(
                coordinateSamplePositions,
                sourceGridWidth,
                sourceGridHeight,
                false);

            int waterPaintedCount = bounds.HasEnvironmentBackground
                ? checked(bounds.GridWidth * bounds.GridHeight)
                : PaintLayerCells(
                    pixels,
                    texWidth,
                    texHeight,
                    sourceGridWidth,
                    sourceGridHeight,
                    bounds,
                    waterPositions,
                    waterLayerColor,
                    useCenteredGrid);
            int groundPaintedCount = PaintLayerCells(
                pixels,
                texWidth,
                texHeight,
                sourceGridWidth,
                sourceGridHeight,
                bounds,
                groundPositions,
                groundLayerColor,
                useCenteredGrid);

            int paintedCount = checked(waterPaintedCount + groundPaintedCount);
            if (paintedCount == 0)
            {
                HashSet<Vector2> fallbackPositions = CollectAllBlueprintCells(configuration);
                paintedCount = PaintLayerCells(
                    pixels,
                    texWidth,
                    texHeight,
                    sourceGridWidth,
                    sourceGridHeight,
                    bounds,
                    fallbackPositions,
                    groundLayerColor,
                    useCenteredGrid);
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            bool expectsGroundLayer = HasBlueprintLayerMatch(configuration, groundLayerKeyword);
            bool expectsWaterLayer = !bounds.HasEnvironmentBackground &&
                                     HasBlueprintLayerMatch(configuration, waterLayerKeyword);
            return new MinimapTerrainMapBuildResult(
                texture,
                bounds,
                useCenteredGrid,
                paintedCount,
                groundPaintedCount,
                waterPaintedCount,
                expectsGroundLayer,
                expectsWaterLayer);
        }

        public static MinimapTerrainBounds ResolveBounds(
            TileWorldCreatorManager manager,
            int minimumPaddingCells = 0,
            float targetAspect = 0f)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));
            if (manager.configuration == null)
                throw new InvalidOperationException("Minimap terrain bounds require a TileWorldCreator Configuration.");
            if (minimumPaddingCells < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumPaddingCells));
            if (targetAspect < 0f || float.IsNaN(targetAspect) || float.IsInfinity(targetAspect))
                throw new ArgumentOutOfRangeException(nameof(targetAspect));

            Configuration configuration = manager.configuration;
            if (configuration.width <= 0 || configuration.height <= 0)
            {
                throw new InvalidOperationException(
                    $"Minimap terrain grid must have positive dimensions, actual={configuration.width}x{configuration.height}.");
            }
            if (configuration.cellSize <= 0f || float.IsNaN(configuration.cellSize) || float.IsInfinity(configuration.cellSize))
            {
                throw new InvalidOperationException(
                    $"Minimap terrain cell size must be finite and positive, actual={configuration.cellSize}.");
            }

            float cellSize = configuration.cellSize;
            LevelEnvironmentBackground[] backgrounds = manager.GetComponentsInChildren<LevelEnvironmentBackground>(true);
            if (backgrounds.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Minimap terrain supports at most one LevelEnvironmentBackground, actual={backgrounds.Length}.");
            }

            if (backgrounds.Length == 0)
            {
                Vector3 origin = manager.transform.position;
                return new MinimapTerrainBounds(
                    configuration.width,
                    configuration.height,
                    0,
                    0,
                    cellSize,
                    origin.x,
                    origin.x + configuration.width * cellSize,
                    origin.z,
                    origin.z + configuration.height * cellSize,
                    false);
            }

            Bounds backgroundBounds = backgrounds[0].WorldBounds;
            Vector3 terrainGridMin = manager.transform.position - new Vector3(cellSize * 0.5f, 0f, cellSize * 0.5f);
            int expandedWidth = checked(configuration.width + minimumPaddingCells * 2);
            int expandedHeight = checked(configuration.height + minimumPaddingCells * 2);
            if (targetAspect > 0f)
            {
                if (expandedWidth / (float)expandedHeight < targetAspect)
                    expandedWidth = Mathf.CeilToInt(expandedHeight * targetAspect);
                else
                    expandedHeight = Mathf.CeilToInt(expandedWidth / targetAspect);
            }

            int terrainOffsetX = (expandedWidth - configuration.width) / 2;
            int terrainOffsetY = (expandedHeight - configuration.height) / 2;
            float worldMinX = terrainGridMin.x - terrainOffsetX * cellSize;
            float worldMinZ = terrainGridMin.z - terrainOffsetY * cellSize;
            float worldMaxX = worldMinX + expandedWidth * cellSize;
            float worldMaxZ = worldMinZ + expandedHeight * cellSize;
            const float tolerance = 0.001f;
            if (backgroundBounds.min.x > worldMinX + tolerance ||
                backgroundBounds.min.z > worldMinZ + tolerance ||
                backgroundBounds.max.x < worldMaxX - tolerance ||
                backgroundBounds.max.z < worldMaxZ - tolerance)
            {
                throw new InvalidOperationException(
                    $"Minimap environment background does not contain the requested display bounds. " +
                    $"background=({backgroundBounds.min.x},{backgroundBounds.min.z}).." +
                    $"({backgroundBounds.max.x},{backgroundBounds.max.z}), " +
                    $"minimap=({worldMinX},{worldMinZ})..({worldMaxX},{worldMaxZ}).");
            }

            return new MinimapTerrainBounds(
                expandedWidth,
                expandedHeight,
                terrainOffsetX,
                terrainOffsetY,
                cellSize,
                worldMinX,
                worldMaxX,
                worldMinZ,
                worldMaxZ,
                true);
        }

        private static HashSet<Vector2> CollectBlueprintLayerCells(Configuration configuration, string layerKeyword)
        {
            HashSet<Vector2> positions = new HashSet<Vector2>();
            if (string.IsNullOrWhiteSpace(layerKeyword))
                return positions;

            string keyword = layerKeyword.Trim();
            for (int i = 0; i < configuration.blueprintLayerFolders.Count; i++)
            {
                BlueprintLayerFolder folder = configuration.blueprintLayerFolders[i];
                if (folder?.blueprintLayers == null)
                    continue;

                for (int j = 0; j < folder.blueprintLayers.Count; j++)
                {
                    BlueprintLayer layer = folder.blueprintLayers[j];
                    if (layer == null || string.IsNullOrEmpty(layer.layerName))
                        continue;
                    if (layer.layerName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        layer.GetAllCellPositions(positions);
                }
            }

            return positions;
        }

        private static HashSet<Vector2> CollectAllBlueprintCells(Configuration configuration)
        {
            HashSet<Vector2> positions = new HashSet<Vector2>();
            for (int i = 0; i < configuration.blueprintLayerFolders.Count; i++)
            {
                BlueprintLayerFolder folder = configuration.blueprintLayerFolders[i];
                if (folder?.blueprintLayers == null)
                    continue;

                for (int j = 0; j < folder.blueprintLayers.Count; j++)
                {
                    BlueprintLayer layer = folder.blueprintLayers[j];
                    if (layer != null)
                        layer.GetAllCellPositions(positions);
                }
            }

            return positions;
        }

        private static int PaintLayerCells(
            Color32[] pixels,
            int texWidth,
            int texHeight,
            int sourceGridWidth,
            int sourceGridHeight,
            MinimapTerrainBounds bounds,
            HashSet<Vector2> positions,
            Color color,
            bool centeredGrid)
        {
            if (positions == null || positions.Count == 0)
                return 0;

            Color32 pixelColor = color;
            int painted = 0;
            foreach (Vector2 cellPos in positions)
            {
                if (!TryConvertCellToTexture(
                        cellPos,
                        sourceGridWidth,
                        sourceGridHeight,
                        bounds,
                        texWidth,
                        texHeight,
                        centeredGrid,
                        out int texX,
                        out int texY))
                {
                    continue;
                }

                pixels[texX + texY * texWidth] = pixelColor;
                painted++;
            }

            return painted;
        }

        private static bool DetermineGridCoordinateMode(
            HashSet<Vector2> samplePositions,
            int gridWidth,
            int gridHeight,
            bool fallback)
        {
            if (samplePositions == null || samplePositions.Count == 0)
                return fallback;

            int normalPaintable = CountSourceCells(samplePositions, gridWidth, gridHeight, false);
            int centeredPaintable = CountSourceCells(samplePositions, gridWidth, gridHeight, true);
            return centeredPaintable > normalPaintable;
        }

        private static int CountSourceCells(
            HashSet<Vector2> positions,
            int gridWidth,
            int gridHeight,
            bool centeredGrid)
        {
            int count = 0;
            foreach (Vector2 cellPos in positions)
            {
                float rawX = centeredGrid ? cellPos.x + gridWidth * 0.5f : cellPos.x;
                float rawY = centeredGrid ? cellPos.y + gridHeight * 0.5f : cellPos.y;
                int cellX = Mathf.RoundToInt(rawX);
                int cellY = Mathf.RoundToInt(rawY);
                if (cellX >= 0 && cellX < gridWidth && cellY >= 0 && cellY < gridHeight)
                    count++;
            }

            return count;
        }

        private static bool TryConvertCellToTexture(
            Vector2 cellPos,
            int sourceGridWidth,
            int sourceGridHeight,
            MinimapTerrainBounds bounds,
            int texWidth,
            int texHeight,
            bool centeredGrid,
            out int texX,
            out int texY)
        {
            float rawX = centeredGrid ? cellPos.x + sourceGridWidth * 0.5f : cellPos.x;
            float rawY = centeredGrid ? cellPos.y + sourceGridHeight * 0.5f : cellPos.y;
            int cellX = Mathf.RoundToInt(rawX);
            int cellY = Mathf.RoundToInt(rawY);
            if (cellX < 0 || cellX >= sourceGridWidth || cellY < 0 || cellY >= sourceGridHeight)
            {
                texX = 0;
                texY = 0;
                return false;
            }

            int targetCellX = checked(cellX + bounds.TerrainOffsetX);
            int targetCellY = checked(cellY + bounds.TerrainOffsetY);
            if (targetCellX < 0 || targetCellX >= bounds.GridWidth ||
                targetCellY < 0 || targetCellY >= bounds.GridHeight)
            {
                throw new InvalidOperationException("Minimap terrain cell lies outside the resolved map bounds.");
            }

            texX = Mathf.Clamp((int)(((long)targetCellX * texWidth) / bounds.GridWidth), 0, texWidth - 1);
            texY = Mathf.Clamp((int)(((long)targetCellY * texHeight) / bounds.GridHeight), 0, texHeight - 1);
            return true;
        }

        private static bool HasBlueprintLayerMatch(Configuration configuration, string layerKeyword)
        {
            if (string.IsNullOrWhiteSpace(layerKeyword))
                return false;

            string keyword = layerKeyword.Trim();
            for (int i = 0; i < configuration.blueprintLayerFolders.Count; i++)
            {
                BlueprintLayerFolder folder = configuration.blueprintLayerFolders[i];
                if (folder?.blueprintLayers == null)
                    continue;

                for (int j = 0; j < folder.blueprintLayers.Count; j++)
                {
                    BlueprintLayer layer = folder.blueprintLayers[j];
                    if (layer != null && !string.IsNullOrEmpty(layer.layerName) &&
                        layer.layerName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
