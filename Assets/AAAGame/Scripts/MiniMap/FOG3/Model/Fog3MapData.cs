using UnityEngine;

namespace AAAGame.MiniMap.FOG3
{
    public sealed class Fog3MapData
    {
        private readonly bool[] explored;
        private readonly bool[] walkable;
        private readonly float[] currentVisibility;

        public Fog3MapData(Fog3TerrainInfo terrainInfo)
        {
            Width = terrainInfo.Width;
            Height = terrainInfo.Height;
            CellSize = terrainInfo.CellSize;
            WorldOrigin = terrainInfo.Origin;

            int length = Width * Height;
            explored = new bool[length];
            currentVisibility = new float[length];
            walkable = new bool[length];
            System.Array.Copy(terrainInfo.WalkableMask, walkable, length);
        }

        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public Vector3 WorldOrigin { get; }
        public bool IsDirty { get; private set; }

        public Bounds Bounds
        {
            get
            {
                Vector3 size = new Vector3(Width * CellSize, 0f, Height * CellSize);
                return new Bounds(WorldOrigin + size * 0.5f, size);
            }
        }

        public void ClearCurrentVisibility()
        {
            for (int i = 0; i < currentVisibility.Length; i++)
                currentVisibility[i] = 0f;
        }

        public void AddVisibility(int x, int y, float intensity)
        {
            if (!IsValidCell(x, y))
                return;

            int index = GetIndex(x, y);
            if (!walkable[index])
                return;

            float clamped = Mathf.Clamp01(intensity);
            if (clamped <= 0f)
                return;

            if (clamped > currentVisibility[index])
                currentVisibility[index] = clamped;

            explored[index] = true;
            IsDirty = true;
        }

        public void MarkClean()
        {
            IsDirty = false;
        }

        public void ResetExploration()
        {
            for (int i = 0; i < explored.Length; i++)
            {
                explored[i] = false;
                currentVisibility[i] = 0f;
            }

            IsDirty = true;
        }

        public Fog3CellState GetCellState(int x, int y)
        {
            if (!IsValidCell(x, y))
                return Fog3CellState.Outside;

            int index = GetIndex(x, y);
            if (!walkable[index])
                return Fog3CellState.Outside;

            if (currentVisibility[index] > 0f)
                return Fog3CellState.Visible;

            return explored[index] ? Fog3CellState.Explored : Fog3CellState.Hidden;
        }

        public float GetVisibility(int x, int y)
        {
            if (!IsValidCell(x, y))
                return 0f;

            return currentVisibility[GetIndex(x, y)];
        }

        public bool IsExplored(int x, int y)
        {
            return IsValidCell(x, y) && explored[GetIndex(x, y)];
        }

        public bool IsWalkable(int x, int y)
        {
            return IsValidCell(x, y) && walkable[GetIndex(x, y)];
        }

        public bool WorldToGrid(Vector3 worldPos, out int gridX, out int gridY)
        {
            Vector3 localPos = worldPos - WorldOrigin;
            gridX = Mathf.FloorToInt(localPos.x / CellSize);
            gridY = Mathf.FloorToInt(localPos.z / CellSize);
            return IsValidCell(gridX, gridY);
        }

        public Vector3 GridToWorldCenter(int gridX, int gridY)
        {
            float worldX = WorldOrigin.x + (gridX + 0.5f) * CellSize;
            float worldZ = WorldOrigin.z + (gridY + 0.5f) * CellSize;
            return new Vector3(worldX, WorldOrigin.y, worldZ);
        }

        public bool IsPositionVisible(Vector3 worldPos)
        {
            return WorldToGrid(worldPos, out int x, out int y) && GetCellState(x, y) == Fog3CellState.Visible;
        }

        public bool IsPositionExplored(Vector3 worldPos)
        {
            if (!WorldToGrid(worldPos, out int x, out int y))
                return false;

            Fog3CellState state = GetCellState(x, y);
            return state == Fog3CellState.Visible || state == Fog3CellState.Explored;
        }

        public bool IsValidCell(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        private int GetIndex(int x, int y)
        {
            return x + y * Width;
        }
    }
}
