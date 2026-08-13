using UnityEngine;

namespace AAAGame.MiniMap.FOG3
{
    public sealed class Fog3TerrainInfo
    {
        public Fog3TerrainInfo(int width, int height, float cellSize, Vector3 origin, bool[] walkableMask, string sourceName)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            CellSize = Mathf.Max(0.01f, cellSize);
            Origin = origin;
            SourceName = string.IsNullOrEmpty(sourceName) ? "Manual" : sourceName;
            WalkableMask = ValidateMask(walkableMask, Width * Height);
        }

        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public Vector3 Origin { get; }
        public string SourceName { get; }
        public bool[] WalkableMask { get; }

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

        private static bool[] ValidateMask(bool[] mask, int expectedLength)
        {
            if (mask != null && mask.Length == expectedLength)
                return mask;

            bool[] result = new bool[expectedLength];
            for (int i = 0; i < result.Length; i++)
                result[i] = true;
            return result;
        }
    }
}
