using System.Collections.Generic;
using GiantGrey.TileWorldCreator;
using UnityEngine;

namespace AAAGame.MiniMap
{
    public sealed class MinimapTerrainMapBuildResult
    {
        public MinimapTerrainMapBuildResult(
            Texture2D texture,
            int gridWidth,
            int gridHeight,
            float cellSize,
            bool useCenteredGrid,
            int paintedCount,
            int groundPaintedCount,
            int waterPaintedCount,
            bool expectsGroundLayer,
            bool expectsWaterLayer)
        {
            Texture = texture;
            GridWidth = gridWidth;
            GridHeight = gridHeight;
            CellSize = cellSize;
            UseCenteredGrid = useCenteredGrid;
            PaintedCount = paintedCount;
            GroundPaintedCount = groundPaintedCount;
            WaterPaintedCount = waterPaintedCount;
            ExpectsGroundLayer = expectsGroundLayer;
            ExpectsWaterLayer = expectsWaterLayer;
        }

        public Texture2D Texture { get; }
        public int GridWidth { get; }
        public int GridHeight { get; }
        public float CellSize { get; }
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
            Configuration configuration,
            int textureMaxSize,
            string groundLayerKeyword,
            string waterLayerKeyword,
            Color groundLayerColor,
            Color waterLayerColor,
            Color32 backgroundColor)
        {
            if (configuration == null)
            {
                return null;
            }

            int gridWidth = Mathf.Max(1, configuration.width);
            int gridHeight = Mathf.Max(1, configuration.height);
            float cellSize = Mathf.Max(0.01f, configuration.cellSize);

            int texWidth = gridWidth;
            int texHeight = gridHeight;
            int maxDimension = Mathf.Max(texWidth, texHeight);
            if (textureMaxSize > 0 && maxDimension > textureMaxSize)
            {
                float scale = textureMaxSize / (float)maxDimension;
                texWidth = Mathf.Max(1, Mathf.RoundToInt(texWidth * scale));
                texHeight = Mathf.Max(1, Mathf.RoundToInt(texHeight * scale));
            }

            Texture2D texture = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            Color32[] pixels = new Color32[texWidth * texHeight];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = backgroundColor;
            }

            HashSet<Vector2> groundPositions = CollectBlueprintLayerCells(configuration, groundLayerKeyword);
            HashSet<Vector2> waterPositions = CollectBlueprintLayerCells(configuration, waterLayerKeyword);

            HashSet<Vector2> coordinateSamplePositions = new HashSet<Vector2>();
            coordinateSamplePositions.UnionWith(groundPositions);
            coordinateSamplePositions.UnionWith(waterPositions);
            if (coordinateSamplePositions.Count == 0)
            {
                coordinateSamplePositions = CollectAllBlueprintCells(configuration);
            }

            bool useCenteredGrid = DetermineGridCoordinateMode(
                coordinateSamplePositions,
                gridWidth,
                gridHeight,
                texWidth,
                texHeight,
                false);

            int waterPaintedCount = PaintLayerCells(
                pixels,
                texWidth,
                texHeight,
                gridWidth,
                gridHeight,
                waterPositions,
                waterLayerColor,
                useCenteredGrid);
            int groundPaintedCount = PaintLayerCells(
                pixels,
                texWidth,
                texHeight,
                gridWidth,
                gridHeight,
                groundPositions,
                groundLayerColor,
                useCenteredGrid);

            int paintedCount = waterPaintedCount + groundPaintedCount;
            if (paintedCount == 0)
            {
                HashSet<Vector2> fallbackPositions = CollectAllBlueprintCells(configuration);
                paintedCount += PaintLayerCells(
                    pixels,
                    texWidth,
                    texHeight,
                    gridWidth,
                    gridHeight,
                    fallbackPositions,
                    groundLayerColor,
                    useCenteredGrid);
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            bool expectsGroundLayer = HasBlueprintLayerMatch(configuration, groundLayerKeyword);
            bool expectsWaterLayer = HasBlueprintLayerMatch(configuration, waterLayerKeyword);
            return new MinimapTerrainMapBuildResult(
                texture,
                gridWidth,
                gridHeight,
                cellSize,
                useCenteredGrid,
                paintedCount,
                groundPaintedCount,
                waterPaintedCount,
                expectsGroundLayer,
                expectsWaterLayer);
        }

        private static HashSet<Vector2> CollectBlueprintLayerCells(Configuration configuration, string layerKeyword)
        {
            HashSet<Vector2> positions = new HashSet<Vector2>();
            if (configuration == null || string.IsNullOrWhiteSpace(layerKeyword))
            {
                return positions;
            }

            string keyword = layerKeyword.Trim();
            for (int i = 0; i < configuration.blueprintLayerFolders.Count; i++)
            {
                var folder = configuration.blueprintLayerFolders[i];
                if (folder == null || folder.blueprintLayers == null)
                {
                    continue;
                }

                for (int j = 0; j < folder.blueprintLayers.Count; j++)
                {
                    var layer = folder.blueprintLayers[j];
                    if (layer == null || string.IsNullOrEmpty(layer.layerName))
                    {
                        continue;
                    }

                    if (layer.layerName.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    layer.GetAllCellPositions(positions);
                }
            }

            return positions;
        }

        private static HashSet<Vector2> CollectAllBlueprintCells(Configuration configuration)
        {
            HashSet<Vector2> positions = new HashSet<Vector2>();
            if (configuration == null)
            {
                return positions;
            }

            for (int i = 0; i < configuration.blueprintLayerFolders.Count; i++)
            {
                var folder = configuration.blueprintLayerFolders[i];
                if (folder == null || folder.blueprintLayers == null)
                {
                    continue;
                }

                for (int j = 0; j < folder.blueprintLayers.Count; j++)
                {
                    var layer = folder.blueprintLayers[j];
                    if (layer == null)
                    {
                        continue;
                    }

                    layer.GetAllCellPositions(positions);
                }
            }

            return positions;
        }

        private static int PaintLayerCells(
            Color32[] pixels,
            int texWidth,
            int texHeight,
            int gridWidth,
            int gridHeight,
            HashSet<Vector2> positions,
            Color color,
            bool centeredGrid)
        {
            if (positions == null || positions.Count == 0)
            {
                return 0;
            }

            Color32 pixelColor = color;
            int painted = 0;
            foreach (Vector2 cellPos in positions)
            {
                if (TryConvertCellToTexture(cellPos, gridWidth, gridHeight, texWidth, texHeight, centeredGrid, out int texX, out int texY))
                {
                    int idx = texX + texY * texWidth;
                    pixels[idx] = pixelColor;
                    painted++;
                }
            }

            return painted;
        }

        private static bool DetermineGridCoordinateMode(
            HashSet<Vector2> samplePositions,
            int gridWidth,
            int gridHeight,
            int texWidth,
            int texHeight,
            bool fallback)
        {
            if (samplePositions == null || samplePositions.Count == 0)
            {
                return fallback;
            }

            int normalPaintable = CountPaintableCells(samplePositions, gridWidth, gridHeight, texWidth, texHeight, false);
            int centeredPaintable = CountPaintableCells(samplePositions, gridWidth, gridHeight, texWidth, texHeight, true);
            return centeredPaintable > normalPaintable;
        }

        private static int CountPaintableCells(
            HashSet<Vector2> positions,
            int gridWidth,
            int gridHeight,
            int texWidth,
            int texHeight,
            bool centeredGrid)
        {
            int count = 0;
            foreach (Vector2 cellPos in positions)
            {
                if (TryConvertCellToTexture(cellPos, gridWidth, gridHeight, texWidth, texHeight, centeredGrid, out _, out _))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool TryConvertCellToTexture(
            Vector2 cellPos,
            int gridWidth,
            int gridHeight,
            int texWidth,
            int texHeight,
            bool centeredGrid,
            out int texX,
            out int texY)
        {
            float rawX = cellPos.x;
            float rawY = cellPos.y;

            if (centeredGrid)
            {
                rawX += gridWidth * 0.5f;
                rawY += gridHeight * 0.5f;
            }

            int cellX = Mathf.RoundToInt(rawX);
            int cellY = Mathf.RoundToInt(rawY);
            if (cellX < 0 || cellX >= gridWidth || cellY < 0 || cellY >= gridHeight)
            {
                texX = 0;
                texY = 0;
                return false;
            }

            texX = Mathf.Clamp(Mathf.FloorToInt((cellX / (float)gridWidth) * texWidth), 0, texWidth - 1);
            texY = Mathf.Clamp(Mathf.FloorToInt((cellY / (float)gridHeight) * texHeight), 0, texHeight - 1);
            return true;
        }

        private static bool HasBlueprintLayerMatch(Configuration configuration, string layerKeyword)
        {
            if (configuration == null || string.IsNullOrWhiteSpace(layerKeyword))
            {
                return false;
            }

            string keyword = layerKeyword.Trim();
            for (int i = 0; i < configuration.blueprintLayerFolders.Count; i++)
            {
                var folder = configuration.blueprintLayerFolders[i];
                if (folder == null || folder.blueprintLayers == null)
                {
                    continue;
                }

                for (int j = 0; j < folder.blueprintLayers.Count; j++)
                {
                    var layer = folder.blueprintLayers[j];
                    if (layer == null || string.IsNullOrEmpty(layer.layerName))
                    {
                        continue;
                    }

                    if (layer.layerName.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
