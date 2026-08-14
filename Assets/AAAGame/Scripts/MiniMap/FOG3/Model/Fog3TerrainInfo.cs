using UnityEngine;

namespace AAAGame.MiniMap.FOG3
{
    public sealed class Fog3TerrainInfo
    {
        public Fog3TerrainInfo(int width, int height, float cellSize, Vector3 origin, bool[] walkableMask, string sourceName)
            : this(width, height, cellSize, origin, walkableMask, CreateFlatPlatformHeights(width, height), null, sourceName)
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
            string sourceName)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            CellSize = Mathf.Max(0.01f, cellSize);
            Origin = origin;
            SourceName = string.IsNullOrEmpty(sourceName) ? "Manual" : sourceName;
            WalkableMask = ValidateMask(walkableMask, Width * Height);
            PlatformHeights = ValidatePlatformHeights(platformHeights, Width * Height);
            SlopeMask = ValidateSlopeMask(slopeMask, Width * Height);
        }

        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public Vector3 Origin { get; }
        public string SourceName { get; }
        public bool[] WalkableMask { get; }
        public int[] PlatformHeights { get; }
        public bool[] SlopeMask { get; }

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

        private static int[] CreateFlatPlatformHeights(int width, int height)
        {
            return new int[Mathf.Max(1, width) * Mathf.Max(1, height)];
        }
    }
}
