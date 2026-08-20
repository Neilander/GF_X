using UnityEngine;

namespace AAAGame.MiniMap.FOG3
{
    public readonly struct Fog3SlopeCellInfo
    {
        public Fog3SlopeCellInfo(int directionX, int directionY, int baseHeightNumerator, int run, int rise)
        {
            if (System.Math.Abs(directionX) + System.Math.Abs(directionY) != 1)
                throw new System.ArgumentException($"Fog slope direction must be cardinal. actual=({directionX},{directionY}).");
            if (baseHeightNumerator < 0)
                throw new System.ArgumentOutOfRangeException(nameof(baseHeightNumerator));
            if (run <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(run));
            if (rise <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(rise));

            DirectionX = directionX;
            DirectionY = directionY;
            BaseHeightNumerator = baseHeightNumerator;
            Run = run;
            Rise = rise;
        }

        public int DirectionX { get; }
        public int DirectionY { get; }
        public int BaseHeightNumerator { get; }
        public int Run { get; }
        public int Rise { get; }
        public bool IsDefined => Run > 0;
    }

    public sealed class Fog3TerrainInfo
    {
        public Fog3TerrainInfo(int width, int height, float cellSize, Vector3 origin, bool[] walkableMask, string sourceName)
            : this(width, height, cellSize, origin, walkableMask, CreateFlatPlatformHeights(width, height), null, null, sourceName)
        {
        }

        public Fog3TerrainInfo(
            int width,
            int height,
            float cellSize,
            Vector3 origin,
            bool[] walkableMask,
            int[] platformHeights,
            bool[] slopeMask,
            Fog3SlopeCellInfo[] slopeCells,
            string sourceName)
            : this(
                width,
                height,
                cellSize,
                origin,
                walkableMask,
                platformHeights,
                slopeMask,
                slopeCells,
                0f,
                sourceName)
        {
        }

        public Fog3TerrainInfo(
            int width,
            int height,
            float cellSize,
            Vector3 origin,
            bool[] walkableMask,
            int[] platformHeights,
            bool[] slopeMask,
            Fog3SlopeCellInfo[] slopeCells,
            float platformEdgeInset,
            string sourceName)
            : this(
                width,
                height,
                cellSize,
                origin,
                walkableMask,
                platformHeights,
                slopeMask,
                slopeCells,
                platformEdgeInset,
                null,
                sourceName)
        {
        }

        public Fog3TerrainInfo(
            int width,
            int height,
            float cellSize,
            Vector3 origin,
            bool[] walkableMask,
            int[] platformHeights,
            bool[] slopeMask,
            Fog3SlopeCellInfo[] slopeCells,
            float platformEdgeInset,
            float? backgroundSurfaceWorldY,
            string sourceName)
            : this(
                width,
                height,
                cellSize,
                origin,
                walkableMask,
                platformHeights,
                slopeMask,
                slopeCells,
                platformEdgeInset,
                backgroundSurfaceWorldY,
                backgroundSurfaceWorldY,
                sourceName)
        {
        }

        public Fog3TerrainInfo(
            int width,
            int height,
            float cellSize,
            Vector3 origin,
            bool[] walkableMask,
            int[] platformHeights,
            bool[] slopeMask,
            Fog3SlopeCellInfo[] slopeCells,
            float platformEdgeInset,
            float? backgroundSurfaceWorldY,
            float? backgroundFogCoverWorldY,
            string sourceName)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            CellSize = Mathf.Max(0.01f, cellSize);
            if (platformEdgeInset < 0f || platformEdgeInset >= CellSize * 0.5f)
                throw new System.ArgumentOutOfRangeException(nameof(platformEdgeInset));

            Origin = origin;
            PlatformEdgeInset = platformEdgeInset;
            if (backgroundSurfaceWorldY.HasValue &&
                (float.IsNaN(backgroundSurfaceWorldY.Value) || float.IsInfinity(backgroundSurfaceWorldY.Value)))
            {
                throw new System.ArgumentOutOfRangeException(nameof(backgroundSurfaceWorldY));
            }
            BackgroundSurfaceWorldY = backgroundSurfaceWorldY;
            if (backgroundFogCoverWorldY.HasValue &&
                (float.IsNaN(backgroundFogCoverWorldY.Value) || float.IsInfinity(backgroundFogCoverWorldY.Value)))
            {
                throw new System.ArgumentOutOfRangeException(nameof(backgroundFogCoverWorldY));
            }
            BackgroundFogCoverWorldY = backgroundFogCoverWorldY;
            SourceName = string.IsNullOrEmpty(sourceName) ? "Manual" : sourceName;
            WalkableMask = ValidateMask(walkableMask, Width * Height);
            PlatformHeights = ValidatePlatformHeights(platformHeights, Width * Height);
            SlopeMask = ValidateSlopeMask(slopeMask, Width * Height);
            SlopeCells = ValidateSlopeCells(slopeCells, SlopeMask, PlatformHeights, Width * Height);
        }

        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public Vector3 Origin { get; }
        public float PlatformEdgeInset { get; }
        public float? BackgroundSurfaceWorldY { get; }
        public float? BackgroundFogCoverWorldY { get; }
        public string SourceName { get; }
        public bool[] WalkableMask { get; }
        public int[] PlatformHeights { get; }
        public bool[] SlopeMask { get; }
        public Fog3SlopeCellInfo[] SlopeCells { get; }

        public Bounds Bounds
        {
            get
            {
                Vector3 size = new Vector3(Width * CellSize, 0f, Height * CellSize);
                return new Bounds(Origin + size * 0.5f, size);
            }
        }

        public bool IsWalkable(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return false;

            return WalkableMask[x + y * Width];
        }

        public int GetPlatformHeight(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                throw new System.ArgumentOutOfRangeException(nameof(x), $"Fog terrain cell ({x},{y}) is outside {Width}x{Height}.");

            return PlatformHeights[x + y * Width];
        }

        public bool IsSlope(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                throw new System.ArgumentOutOfRangeException(nameof(x), $"Fog terrain cell ({x},{y}) is outside {Width}x{Height}.");

            return SlopeMask[x + y * Width];
        }

        public Fog3SlopeCellInfo GetSlopeCellInfo(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                throw new System.ArgumentOutOfRangeException(nameof(x), $"Fog terrain cell ({x},{y}) is outside {Width}x{Height}.");

            return SlopeCells[x + y * Width];
        }

        private static bool[] ValidateMask(bool[] mask, int expectedLength)
        {
            if (mask != null && mask.Length == expectedLength)
                return mask;

            bool[] result = new bool[expectedLength];
            for (int i = 0; i < result.Length; i++)
                result[i] = true;
            return result;
        }

        private static int[] ValidatePlatformHeights(int[] heights, int expectedLength)
        {
            if (heights == null || heights.Length != expectedLength)
            {
                throw new System.ArgumentException(
                    $"Fog terrain platform height length must be {expectedLength}, actual={heights?.Length ?? -1}.",
                    nameof(heights));
            }

            return heights;
        }

        private static bool[] ValidateSlopeMask(bool[] slopes, int expectedLength)
        {
            if (slopes == null)
                return new bool[expectedLength];
            if (slopes.Length != expectedLength)
            {
                throw new System.ArgumentException(
                    $"Fog terrain slope mask length must be {expectedLength}, actual={slopes.Length}.",
                    nameof(slopes));
            }

            return slopes;
        }

        private static Fog3SlopeCellInfo[] ValidateSlopeCells(
            Fog3SlopeCellInfo[] slopeCells,
            bool[] slopeMask,
            int[] platformHeights,
            int expectedLength)
        {
            if (slopeCells == null)
            {
                for (int i = 0; i < slopeMask.Length; i++)
                {
                    if (slopeMask[i])
                        throw new System.ArgumentException($"Fog slope cell {i} is missing deterministic height metadata.", nameof(slopeCells));
                }

                return new Fog3SlopeCellInfo[expectedLength];
            }
            if (slopeCells.Length != expectedLength)
            {
                throw new System.ArgumentException(
                    $"Fog terrain slope metadata length must be {expectedLength}, actual={slopeCells.Length}.",
                    nameof(slopeCells));
            }

            for (int i = 0; i < expectedLength; i++)
            {
                Fog3SlopeCellInfo slope = slopeCells[i];
                if (slopeMask[i] != slope.IsDefined)
                    throw new System.ArgumentException($"Fog slope mask/metadata mismatch at cell {i}.", nameof(slopeCells));
                if (slope.IsDefined && slope.BaseHeightNumerator / slope.Run != platformHeights[i])
                {
                    throw new System.ArgumentException(
                        $"Fog slope support mismatch at cell {i}. platform=H{platformHeights[i]}, slopeNumerator={slope.BaseHeightNumerator}, run={slope.Run}.",
                        nameof(slopeCells));
                }
            }

            return slopeCells;
        }

        private static int[] CreateFlatPlatformHeights(int width, int height)
        {
            return new int[Mathf.Max(1, width) * Mathf.Max(1, height)];
        }
    }
}
