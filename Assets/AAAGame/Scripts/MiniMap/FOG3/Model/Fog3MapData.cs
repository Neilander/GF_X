using UnityEngine;

using System;
using System.IO;
using System.IO.Compression;

namespace AAAGame.MiniMap.FOG3
{
    public sealed class Fog3ExplorationCheckpoint
    {
        private readonly byte[] compressedExploredBits;

        internal Fog3ExplorationCheckpoint(int width, int height, ulong terrainHash, byte[] exploredBits)
        {
            if (exploredBits == null)
                throw new ArgumentNullException(nameof(exploredBits));

            Width = width;
            Height = height;
            TerrainHash = terrainHash;
            RawPayloadByteCount = exploredBits.Length;
            compressedExploredBits = Compress(exploredBits);
            StoredPayloadByteCount = compressedExploredBits.Length;
            var hasher = new LogicStateHasher();
            hasher.Add(0x535447464F473030UL);
            hasher.Add(width);
            hasher.Add(height);
            hasher.Add(terrainHash);
            hasher.Add(exploredBits.Length);
            for (int i = 0; i < exploredBits.Length; i++)
                hasher.Add((int)exploredBits[i]);
            ContentHash = hasher.Hash;
        }

        public int Width { get; }
        public int Height { get; }
        public ulong TerrainHash { get; }
        public ulong ContentHash { get; }
        public int RawPayloadByteCount { get; }
        public int StoredPayloadByteCount { get; }

        internal byte[] DecompressExploredBits()
        {
            var result = new byte[RawPayloadByteCount];
            using (var input = new MemoryStream(compressedExploredBits, false))
            using (var inflater = new DeflateStream(input, CompressionMode.Decompress))
            {
                int offset = 0;
                while (offset < result.Length)
                {
                    int read = inflater.Read(result, offset, result.Length - offset);
                    if (read <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Fog checkpoint payload ended early. expected={result.Length}, actual={offset}.");
                    }

                    offset += read;
                }

                if (inflater.ReadByte() >= 0)
                    throw new InvalidOperationException("Fog checkpoint payload contains trailing decompressed data.");
            }

            return result;
        }

        private static byte[] Compress(byte[] source)
        {
            using (var output = new MemoryStream())
            {
                using (var deflater = new DeflateStream(
                           output,
                           System.IO.Compression.CompressionLevel.Optimal,
                           true))
                    deflater.Write(source, 0, source.Length);
                return output.ToArray();
            }
        }
    }

    public sealed class Fog3MapData
    {
        private readonly bool[] explored;
        private readonly bool[] walkable;
        private readonly int[] platformHeights;
        private readonly bool[] slopeMask;
        private readonly Fog3SlopeCellInfo[] slopeCells;
        private readonly float[] currentVisibility;
        private readonly ulong terrainHash;
        private readonly long cellSizeGridRaw;
        private readonly long originXGridRaw;
        private readonly long originZGridRaw;
        private int exploredCellCount;
        private ulong explorationXorDigest;
        private ulong explorationSumDigest;
        private ulong explorationVersion;
        private ulong lastCheckpointExplorationVersion = ulong.MaxValue;
        private Fog3ExplorationCheckpoint lastExplorationCheckpoint;

        public Fog3MapData(Fog3TerrainInfo terrainInfo)
        {
            Width = terrainInfo.Width;
            Height = terrainInfo.Height;
            CellSize = terrainInfo.CellSize;
            WorldOrigin = terrainInfo.Origin;
            cellSizeGridRaw = NavigationGridFixedMath.FloatToGridRaw(terrainInfo.CellSize);
            originXGridRaw = NavigationGridFixedMath.FloatToGridRaw(terrainInfo.Origin.x);
            originZGridRaw = NavigationGridFixedMath.FloatToGridRaw(terrainInfo.Origin.z);
            if (cellSizeGridRaw <= 0)
                throw new System.InvalidOperationException("Fog3MapData grid cell size must be positive.");

            int length = Width * Height;
            explored = new bool[length];
            currentVisibility = new float[length];
            walkable = new bool[length];
            platformHeights = new int[length];
            slopeMask = new bool[length];
            slopeCells = new Fog3SlopeCellInfo[length];
            System.Array.Copy(terrainInfo.WalkableMask, walkable, length);
            System.Array.Copy(terrainInfo.PlatformHeights, platformHeights, length);
            System.Array.Copy(terrainInfo.SlopeMask, slopeMask, length);
            System.Array.Copy(terrainInfo.SlopeCells, slopeCells, length);
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
            bool changed = false;
            for (int i = 0; i < currentVisibility.Length; i++)
            {
                changed |= currentVisibility[i] > 0f;
                currentVisibility[i] = 0f;
            }

            if (changed)
                IsDirty = true;
        }

        public void MarkVisible(int x, int y)
        {
            if (!IsValidCell(x, y))
                throw new ArgumentOutOfRangeException(nameof(x), $"Fog visibility cell ({x},{y}) is outside {Width}x{Height}.");

            int index = GetIndex(x, y);
            if (!walkable[index] || currentVisibility[index] >= 1f)
                return;

            currentVisibility[index] = 1f;
            IsDirty = true;
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
            explorationVersion = checked(explorationVersion + 1);
            IsDirty = true;
            return true;
        }

        public void MarkClean()
        {
            IsDirty = false;
        }

        public void ResetExploration()
        {
            bool changed = exploredCellCount > 0;
            for (int i = 0; i < explored.Length; i++)
            {
                explored[i] = false;
                currentVisibility[i] = 0f;
            }

            exploredCellCount = 0;
            explorationXorDigest = 0;
            explorationSumDigest = 0;
            if (changed)
                explorationVersion = checked(explorationVersion + 1);

            IsDirty = true;
        }

        public Fog3ExplorationCheckpoint CaptureExplorationCheckpoint()
        {
            if (lastExplorationCheckpoint != null
                && lastCheckpointExplorationVersion == explorationVersion)
            {
                return lastExplorationCheckpoint;
            }

            var bits = new byte[(explored.Length + 7) / 8];
            for (int i = 0; i < explored.Length; i++)
            {
                if (explored[i])
                    bits[i >> 3] |= (byte)(1 << (i & 7));
            }
            lastExplorationCheckpoint = new Fog3ExplorationCheckpoint(Width, Height, terrainHash, bits);
            lastCheckpointExplorationVersion = explorationVersion;
            return lastExplorationCheckpoint;
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
            if (checkpoint.RawPayloadByteCount != expectedByteCount)
                throw new System.InvalidOperationException(
                    $"Fog checkpoint payload size mismatch. expected={expectedByteCount}, actual={checkpoint.RawPayloadByteCount}.");
            byte[] exploredBits = checkpoint.DecompressExploredBits();
            for (int i = 0; i < explored.Length; i++)
            {
                bool nextExplored = (exploredBits[i >> 3] & (1 << (i & 7))) != 0;
                if (nextExplored && !walkable[i])
                    throw new System.InvalidOperationException($"Fog checkpoint marks non-walkable cell index {i} as explored.");
            }

            exploredCellCount = 0;
            explorationXorDigest = 0;
            explorationSumDigest = 0;
            for (int i = 0; i < explored.Length; i++)
            {
                explored[i] = (exploredBits[i >> 3] & (1 << (i & 7))) != 0;
                currentVisibility[i] = 0f;
                if (explored[i])
                    AddExplorationDigest(i);
            }
            explorationVersion = checked(explorationVersion + 1);
            lastExplorationCheckpoint = checkpoint;
            lastCheckpointExplorationVersion = explorationVersion;
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

        public int GetPlatformHeight(int x, int y)
        {
            if (!IsValidCell(x, y))
                throw new ArgumentOutOfRangeException(nameof(x), $"Fog terrain cell ({x},{y}) is outside {Width}x{Height}.");

            return platformHeights[GetIndex(x, y)];
        }

        public bool IsSlope(int x, int y)
        {
            if (!IsValidCell(x, y))
                throw new ArgumentOutOfRangeException(nameof(x), $"Fog terrain cell ({x},{y}) is outside {Width}x{Height}.");

            return slopeMask[GetIndex(x, y)];
        }

        public int GetVisionHeight(FixVector2 worldPosition)
        {
            if (!WorldToGrid(worldPosition, out int x, out int y))
                throw new ArgumentOutOfRangeException(nameof(worldPosition), $"Fog viewer position {worldPosition} is outside {Width}x{Height}.");

            int index = GetIndex(x, y);
            int platformHeight = platformHeights[index];
            if (platformHeight < 0)
                throw new InvalidOperationException($"Fog viewer cell ({x},{y}) has no Plane_H* support height.");
            if (!slopeMask[index])
                return platformHeight;

            Fog3SlopeCellInfo slope = slopeCells[index];
            long lowerXRaw = checked(originXGridRaw + checked((long)x * cellSizeGridRaw));
            long lowerYRaw = checked(originZGridRaw + checked((long)y * cellSizeGridRaw));
            Fix64 progress = slope.DirectionX != 0
                ? NavigationGridFixedMath.ResolveCellFraction(
                    NavigationGridFixedMath.Fix64ToGridRaw(worldPosition.x),
                    lowerXRaw,
                    cellSizeGridRaw)
                : NavigationGridFixedMath.ResolveCellFraction(
                    NavigationGridFixedMath.Fix64ToGridRaw(worldPosition.y),
                    lowerYRaw,
                    cellSizeGridRaw);
            if (slope.DirectionX < 0 || slope.DirectionY < 0)
                progress = Fix64.One - progress;

            Fix64 height = ((Fix64)slope.BaseHeightNumerator + progress * (Fix64)slope.Rise) / (Fix64)slope.Run;
            return (int)Fix64.Floor(height);
        }

        public bool IsVisionBlockedByHigherPlatform(FixVector2 viewerPosition, int targetX, int targetY)
        {
            if (!WorldToGrid(viewerPosition, out int viewerX, out int viewerY))
                throw new ArgumentOutOfRangeException(nameof(viewerPosition), $"Fog viewer position {viewerPosition} is outside {Width}x{Height}.");
            return IsVisionBlockedByHigherPlatform(viewerX, viewerY, GetVisionHeight(viewerPosition), targetX, targetY);
        }

        public bool IsVisionBlockedByHigherPlatform(
            int viewerX,
            int viewerY,
            int viewerHeight,
            int targetX,
            int targetY)
        {
            if (!IsValidCell(viewerX, viewerY))
                throw new ArgumentOutOfRangeException(nameof(viewerX), $"Fog viewer cell ({viewerX},{viewerY}) is outside {Width}x{Height}.");
            if (viewerHeight < 0)
                throw new ArgumentOutOfRangeException(nameof(viewerHeight));
            if (!IsValidCell(targetX, targetY))
                throw new ArgumentOutOfRangeException(nameof(targetX), $"Fog target cell ({targetX},{targetY}) is outside {Width}x{Height}.");

            int x = viewerX;
            int y = viewerY;
            int deltaX = Math.Abs(targetX - viewerX);
            int deltaY = Math.Abs(targetY - viewerY);
            int stepX = Math.Sign(targetX - viewerX);
            int stepY = Math.Sign(targetY - viewerY);
            int crossedX = 0;
            int crossedY = 0;

            while (crossedX < deltaX || crossedY < deltaY)
            {
                long xDecision = (1L + (crossedX << 1)) * deltaY;
                long yDecision = (1L + (crossedY << 1)) * deltaX;
                if (xDecision == yDecision)
                {
                    if (IsAboveViewerVisionHeight(x + stepX, y, viewerHeight)
                        || IsAboveViewerVisionHeight(x, y + stepY, viewerHeight))
                    {
                        return true;
                    }

                    x += stepX;
                    y += stepY;
                    crossedX++;
                    crossedY++;
                }
                else if (xDecision < yDecision)
                {
                    x += stepX;
                    crossedX++;
                }
                else
                {
                    y += stepY;
                    crossedY++;
                }

                if (x == targetX && y == targetY)
                    return IsAboveViewerVisionHeight(x, y, viewerHeight);
                if (IsAboveViewerVisionHeight(x, y, viewerHeight))
                    return true;
            }

            return false;
        }

        public bool WorldToGrid(Vector3 worldPos, out int gridX, out int gridY)
        {
            gridX = NavigationGridFixedMath.WorldToGridCell(worldPos.x, originXGridRaw, cellSizeGridRaw);
            gridY = NavigationGridFixedMath.WorldToGridCell(worldPos.z, originZGridRaw, cellSizeGridRaw);
            return IsValidCell(gridX, gridY);
        }

        public bool WorldToGrid(FixVector2 worldPos, out int gridX, out int gridY)
        {
            gridX = NavigationGridFixedMath.WorldToGridCell(worldPos.x, originXGridRaw, cellSizeGridRaw);
            gridY = NavigationGridFixedMath.WorldToGridCell(worldPos.y, originZGridRaw, cellSizeGridRaw);
            return IsValidCell(gridX, gridY);
        }

        public Vector3 GridToWorldCenter(int gridX, int gridY)
        {
            float worldX = WorldOrigin.x + (gridX + 0.5f) * CellSize;
            float worldZ = WorldOrigin.z + (gridY + 0.5f) * CellSize;
            return new Vector3(worldX, WorldOrigin.y, worldZ);
        }

        public FixVector2 GetCellCenterFixed(int gridX, int gridY)
        {
            return NavigationGridFixedMath.GridCellCenterFixed(
                cellSizeGridRaw,
                originXGridRaw,
                originZGridRaw,
                gridX,
                gridY);
        }

        public int GetCellRangeForRadius(Fix64 radius)
        {
            return NavigationGridFixedMath.DivideCeilingByCellSize(radius, cellSizeGridRaw);
        }

        public Fog3CellState GetCellState(FixVector2 worldPos)
        {
            return WorldToGrid(worldPos, out int gridX, out int gridY)
                ? GetCellState(gridX, gridY)
                : Fog3CellState.Outside;
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
                AddHash(ref hash, unchecked((ulong)platformHeights[i]), prime);
                hash ^= slopeMask[i] ? (byte)1 : (byte)0;
                hash *= prime;
                if (slopeMask[i])
                {
                    Fog3SlopeCellInfo slope = slopeCells[i];
                    AddHash(ref hash, unchecked((ulong)slope.DirectionX), prime);
                    AddHash(ref hash, unchecked((ulong)slope.DirectionY), prime);
                    AddHash(ref hash, unchecked((ulong)slope.BaseHeightNumerator), prime);
                    AddHash(ref hash, unchecked((ulong)slope.Run), prime);
                    AddHash(ref hash, unchecked((ulong)slope.Rise), prime);
                }
            }
            return hash;
        }

        private bool IsAboveViewerVisionHeight(int x, int y, int viewerHeight)
        {
            if (!IsValidCell(x, y))
                return false;

            int index = GetIndex(x, y);
            if (!slopeMask[index])
                return platformHeights[index] > viewerHeight;

            Fog3SlopeCellInfo slope = slopeCells[index];
            int upperNumerator = checked(slope.BaseHeightNumerator + slope.Rise);
            int maximumHeight = checked(upperNumerator + slope.Run - 1) / slope.Run;
            return maximumHeight > checked(viewerHeight + 1);
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
