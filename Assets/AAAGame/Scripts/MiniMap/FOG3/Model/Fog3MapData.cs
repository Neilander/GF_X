using UnityEngine;

namespace AAAGame.MiniMap.FOG3
{
    public sealed class Fog3ExplorationCheckpoint
    {
        internal Fog3ExplorationCheckpoint(int width, int height, ulong terrainHash, byte[] exploredBits)
        {
            Width = width;
            Height = height;
            TerrainHash = terrainHash;
            ExploredBits = (byte[])exploredBits.Clone();
            var hasher = new LogicStateHasher();
            hasher.Add(0x535447464F473030UL);
            hasher.Add(width);
            hasher.Add(height);
            hasher.Add(terrainHash);
            hasher.Add(ExploredBits.Length);
            for (int i = 0; i < ExploredBits.Length; i++)
                hasher.Add((int)ExploredBits[i]);
            ContentHash = hasher.Hash;
        }

        public int Width { get; }
        public int Height { get; }
        public ulong TerrainHash { get; }
        public ulong ContentHash { get; }
        internal byte[] ExploredBits { get; }
    }

    public sealed class Fog3MapData
    {
        private readonly bool[] explored;
        private readonly bool[] walkable;
        private readonly float[] currentVisibility;
        private readonly ulong terrainHash;
        private int exploredCellCount;
        private ulong explorationXorDigest;
        private ulong explorationSumDigest;

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
            terrainHash = ComputeTerrainHash();
        }

        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public Vector3 WorldOrigin { get; }
        public bool IsDirty { get; private set; }
        public int ExploredCellCount => exploredCellCount;
        public ulong ExplorationXorDigest => explorationXorDigest;
        public ulong ExplorationSumDigest => explorationSumDigest;
        public ulong TerrainHash => terrainHash;

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

            IsDirty = true;
        }

        public bool MarkExplored(int x, int y)
        {
            if (!IsValidCell(x, y))
                throw new System.ArgumentOutOfRangeException(nameof(x), $"Fog exploration cell ({x},{y}) is outside {Width}x{Height}.");

            int index = GetIndex(x, y);
            if (!walkable[index] || explored[index])
                return false;

            explored[index] = true;
            AddExplorationDigest(index);
            IsDirty = true;
            return true;
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

            exploredCellCount = 0;
            explorationXorDigest = 0;
            explorationSumDigest = 0;

            IsDirty = true;
        }

        public Fog3ExplorationCheckpoint CaptureExplorationCheckpoint()
        {
            var bits = new byte[(explored.Length + 7) / 8];
            for (int i = 0; i < explored.Length; i++)
            {
                if (explored[i])
                    bits[i >> 3] |= (byte)(1 << (i & 7));
            }
            return new Fog3ExplorationCheckpoint(Width, Height, terrainHash, bits);
        }

        public void RestoreExplorationCheckpoint(Fog3ExplorationCheckpoint checkpoint)
        {
            if (checkpoint == null)
                throw new System.ArgumentNullException(nameof(checkpoint));
            if (checkpoint.Width != Width || checkpoint.Height != Height)
                throw new System.InvalidOperationException(
                    $"Fog checkpoint size mismatch. expected={Width}x{Height}, actual={checkpoint.Width}x{checkpoint.Height}.");
            if (checkpoint.TerrainHash != terrainHash)
                throw new System.InvalidOperationException(
                    $"Fog checkpoint terrain mismatch. expected={terrainHash}, actual={checkpoint.TerrainHash}.");
            int expectedByteCount = (explored.Length + 7) / 8;
            if (checkpoint.ExploredBits.Length != expectedByteCount)
                throw new System.InvalidOperationException(
                    $"Fog checkpoint payload size mismatch. expected={expectedByteCount}, actual={checkpoint.ExploredBits.Length}.");
            for (int i = 0; i < explored.Length; i++)
            {
                bool nextExplored = (checkpoint.ExploredBits[i >> 3] & (1 << (i & 7))) != 0;
                if (nextExplored && !walkable[i])
                    throw new System.InvalidOperationException($"Fog checkpoint marks non-walkable cell index {i} as explored.");
            }

            exploredCellCount = 0;
            explorationXorDigest = 0;
            explorationSumDigest = 0;
            for (int i = 0; i < explored.Length; i++)
            {
                explored[i] = (checkpoint.ExploredBits[i >> 3] & (1 << (i & 7))) != 0;
                currentVisibility[i] = 0f;
                if (explored[i])
                    AddExplorationDigest(i);
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

        public void GetDiagnostics(
            out int walkableCount,
            out int hiddenCount,
            out int exploredCount,
            out int visibleCount,
            out int outsideCount,
            out float averageVisibility)
        {
            walkableCount = 0;
            hiddenCount = 0;
            exploredCount = 0;
            visibleCount = 0;
            outsideCount = 0;
            float visibilitySum = 0f;

            for (int i = 0; i < walkable.Length; i++)
            {
                if (!walkable[i])
                {
                    outsideCount++;
                    continue;
                }

                walkableCount++;
                float visibility = currentVisibility[i];
                visibilitySum += visibility;

                if (visibility > 0f)
                    visibleCount++;
                else if (explored[i])
                    exploredCount++;
                else
                    hiddenCount++;
            }

            averageVisibility = walkableCount > 0 ? visibilitySum / walkableCount : 0f;
        }

        public bool TryGetCellDiagnostics(Vector3 worldPos, out int gridX, out int gridY, out Fog3CellState state, out float visibility)
        {
            bool isValid = WorldToGrid(worldPos, out gridX, out gridY);
            if (!isValid)
            {
                state = Fog3CellState.Outside;
                visibility = 0f;
                return false;
            }

            state = GetCellState(gridX, gridY);
            visibility = GetVisibility(gridX, gridY);
            return true;
        }

        public bool IsValidCell(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        private int GetIndex(int x, int y)
        {
            return x + y * Width;
        }

        private ulong ComputeTerrainHash()
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            AddHash(ref hash, unchecked((ulong)Width), prime);
            AddHash(ref hash, unchecked((ulong)Height), prime);
            AddHash(ref hash, unchecked((ulong)System.BitConverter.SingleToInt32Bits(CellSize)), prime);
            AddHash(ref hash, unchecked((ulong)System.BitConverter.SingleToInt32Bits(WorldOrigin.x)), prime);
            AddHash(ref hash, unchecked((ulong)System.BitConverter.SingleToInt32Bits(WorldOrigin.y)), prime);
            AddHash(ref hash, unchecked((ulong)System.BitConverter.SingleToInt32Bits(WorldOrigin.z)), prime);
            for (int i = 0; i < walkable.Length; i++)
            {
                hash ^= walkable[i] ? (byte)1 : (byte)0;
                hash *= prime;
            }
            return hash;
        }

        private void AddExplorationDigest(int index)
        {
            ulong cellHash = MixCellIndex(unchecked((ulong)(uint)index));
            exploredCellCount = checked(exploredCellCount + 1);
            explorationXorDigest ^= cellHash;
            explorationSumDigest = unchecked(explorationSumDigest + cellHash);
        }

        private static ulong MixCellIndex(ulong value)
        {
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        private static void AddHash(ref ulong hash, ulong value, ulong prime)
        {
            for (int i = 0; i < sizeof(ulong); i++)
            {
                hash ^= (byte)(value >> (i * 8));
                hash *= prime;
            }
        }
    }
}
