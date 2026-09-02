using UnityEngine;

using System;
using System.IO;
using System.IO.Compression;
using MainThreadFrameProfiler = UnityGameFramework.Runtime.MainThreadFrameProfiler;
using MainThreadPerfScope = UnityGameFramework.Runtime.MainThreadPerfScope;

namespace AAAGame.MiniMap.FOG3
{
    public readonly struct Fog3VisibilityRowInterval
    {
        public Fog3VisibilityRowInterval(int y, int minimumX, int maximumX)
        {
            if (minimumX > maximumX)
                throw new ArgumentOutOfRangeException(nameof(minimumX));
            Y = y;
            MinimumX = minimumX;
            MaximumX = maximumX;
        }

        public int Y { get; }
        public int MinimumX { get; }
        public int MaximumX { get; }
    }

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
        private readonly int[] visibilityCoverage;
        private readonly int[] visibilityRowDifference;
        private readonly bool[] dirtyVisibilityRows;
        private readonly bool[] dirtyCellFlags;
        private readonly System.Collections.Generic.List<int> dirtyCellIndices;
        private readonly System.Collections.Generic.Dictionary<int, PendingVisibilityChangeTime>
            pendingVisibilityChangeTimes;
        private readonly System.Collections.Generic.Dictionary<int, double> dirtyVisibilityChangeLogicTimes;
        private readonly System.Collections.Generic.Dictionary<VisibilityGeometryCacheKey, Fog3VisibilityRowInterval[]> visibilityGeometryCache;
        private readonly int[] fovVisitStamps;
        private readonly ulong terrainHash;
        private readonly long cellSizeXGridRaw;
        private readonly long cellSizeYGridRaw;
        private readonly long terrainCellSizeGridRaw;
        private readonly long originXGridRaw;
        private readonly long originZGridRaw;
        private readonly long maximumXGridRaw;
        private readonly long maximumZGridRaw;
        private readonly float worldWidth;
        private readonly float worldHeight;
        private int exploredCellCount;
        private ulong explorationXorDigest;
        private ulong explorationSumDigest;
        private ulong explorationVersion;
        private ulong visibilityResetVersion;
        private ulong lastCheckpointExplorationVersion = ulong.MaxValue;
        private Fog3ExplorationCheckpoint lastExplorationCheckpoint;
        private int nextFovVisitStamp = 1;
        private int dirtyMinimumX;
        private int dirtyMinimumY;
        private int dirtyMaximumX = -1;
        private int dirtyMaximumY = -1;

        private struct PendingVisibilityChangeTime
        {
            public byte Kinds;
            public double EnterLogicTime;
            public double ExitLogicTime;
        }

        private readonly struct VisibilityGeometryCacheKey : IEquatable<VisibilityGeometryCacheKey>
        {
            public VisibilityGeometryCacheKey(int centerX, int centerY, int range, int viewerHeight, int requiredHeight)
            {
                CenterX = centerX;
                CenterY = centerY;
                Range = range;
                ViewerHeight = viewerHeight;
                RequiredHeight = requiredHeight;
            }

            private int CenterX { get; }
            private int CenterY { get; }
            private int Range { get; }
            private int ViewerHeight { get; }
            private int RequiredHeight { get; }

            public bool Equals(VisibilityGeometryCacheKey other)
            {
                return CenterX == other.CenterX
                       && CenterY == other.CenterY
                       && Range == other.Range
                       && ViewerHeight == other.ViewerHeight
                       && RequiredHeight == other.RequiredHeight;
            }

            public override bool Equals(object obj)
            {
                return obj is VisibilityGeometryCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = CenterX;
                    hash = hash * 397 ^ CenterY;
                    hash = hash * 397 ^ Range;
                    hash = hash * 397 ^ ViewerHeight;
                    return hash * 397 ^ RequiredHeight;
                }
            }
        }

        public event Action VisibilityReset;

        public Fog3MapData(Fog3TerrainInfo terrainInfo, Fix64 logicCellSize)
        {
            if (terrainInfo == null)
                throw new ArgumentNullException(nameof(terrainInfo));
            if (logicCellSize <= Fix64.Zero)
                throw new ArgumentOutOfRangeException(nameof(logicCellSize), logicCellSize.RawValue, "FOG3 logic cell size must be positive.");

            LogicCellSizeFixed = logicCellSize;
            CellSizeX = (float)logicCellSize;
            CellSizeY = (float)logicCellSize;
            CellSize = (float)logicCellSize;
            WorldOrigin = terrainInfo.Origin;
            cellSizeXGridRaw = NavigationGridFixedMath.Fix64ToGridRaw(logicCellSize);
            cellSizeYGridRaw = cellSizeXGridRaw;
            terrainCellSizeGridRaw = NavigationGridFixedMath.FloatToGridRaw(terrainInfo.CellSize);
            originXGridRaw = NavigationGridFixedMath.FloatToGridRaw(terrainInfo.Origin.x);
            originZGridRaw = NavigationGridFixedMath.FloatToGridRaw(terrainInfo.Origin.z);
            if (cellSizeXGridRaw <= 0 || cellSizeYGridRaw <= 0 || terrainCellSizeGridRaw <= 0)
                throw new System.InvalidOperationException("Fog3MapData grid cell size must be positive.");

            long worldWidthGridRaw = checked((long)terrainInfo.Width * terrainCellSizeGridRaw);
            long worldHeightGridRaw = checked((long)terrainInfo.Height * terrainCellSizeGridRaw);
            Width = DividePositiveCeiling(worldWidthGridRaw, cellSizeXGridRaw);
            Height = DividePositiveCeiling(worldHeightGridRaw, cellSizeYGridRaw);
            maximumXGridRaw = checked(originXGridRaw + worldWidthGridRaw);
            maximumZGridRaw = checked(originZGridRaw + worldHeightGridRaw);
            worldWidth = terrainInfo.Width * terrainInfo.CellSize;
            worldHeight = terrainInfo.Height * terrainInfo.CellSize;

            int length = checked(Width * Height);
            explored = new bool[length];
            currentVisibility = new float[length];
            visibilityCoverage = new int[length];
            visibilityRowDifference = new int[checked((Width + 1) * Height)];
            dirtyVisibilityRows = new bool[Height];
            dirtyCellFlags = new bool[length];
            dirtyCellIndices = new System.Collections.Generic.List<int>();
            pendingVisibilityChangeTimes = new System.Collections.Generic.Dictionary<int, PendingVisibilityChangeTime>();
            dirtyVisibilityChangeLogicTimes = new System.Collections.Generic.Dictionary<int, double>();
            visibilityGeometryCache = new System.Collections.Generic.Dictionary<VisibilityGeometryCacheKey, Fog3VisibilityRowInterval[]>();
            fovVisitStamps = new int[length];
            walkable = new bool[length];
            platformHeights = new int[length];
            slopeMask = new bool[length];
            slopeCells = new Fog3SlopeCellInfo[length];
            for (int y = 0; y < Height; y++)
            {
                long centerYRaw = ResolveCellCenterRaw(y, originZGridRaw, cellSizeYGridRaw, maximumZGridRaw);
                int terrainY = Math.Min(
                    terrainInfo.Height - 1,
                    NavigationGridFixedMath.GridRawToCell(centerYRaw, originZGridRaw, terrainCellSizeGridRaw));
                for (int x = 0; x < Width; x++)
                {
                    long centerXRaw = ResolveCellCenterRaw(x, originXGridRaw, cellSizeXGridRaw, maximumXGridRaw);
                    int terrainX = Math.Min(
                        terrainInfo.Width - 1,
                        NavigationGridFixedMath.GridRawToCell(centerXRaw, originXGridRaw, terrainCellSizeGridRaw));
                    int index = x + y * Width;
                    int terrainIndex = terrainX + terrainY * terrainInfo.Width;
                    walkable[index] = terrainInfo.WalkableMask[terrainIndex];
                    platformHeights[index] = terrainInfo.PlatformHeights[terrainIndex];
                    slopeMask[index] = terrainInfo.SlopeMask[terrainIndex];
                    slopeCells[index] = terrainInfo.SlopeCells[terrainIndex];
                }
            }
            terrainHash = ComputeTerrainHash();
        }

        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public float CellSizeX { get; }
        public float CellSizeY { get; }
        public Fix64 LogicCellSizeFixed { get; }
        public Vector3 WorldOrigin { get; }
        public bool IsDirty { get; private set; }
        public int ExploredCellCount => exploredCellCount;
        public ulong ExplorationXorDigest => explorationXorDigest;
        public ulong ExplorationSumDigest => explorationSumDigest;
        public ulong TerrainHash => terrainHash;
        public ulong ExplorationVersion => explorationVersion;
        public ulong VisibilityResetVersion => visibilityResetVersion;
        public double VisibilityResolutionLogicTime { get; private set; }
        public int DirtyCellCount => dirtyCellIndices.Count;

        public void GetDirtyCell(int dirtyIndex, out int x, out int y)
        {
            if ((uint)dirtyIndex >= (uint)dirtyCellIndices.Count)
                throw new ArgumentOutOfRangeException(nameof(dirtyIndex));
            int cellIndex = dirtyCellIndices[dirtyIndex];
            x = cellIndex % Width;
            y = cellIndex / Width;
        }

        public bool TryGetDirtyBounds(out int minimumX, out int minimumY, out int maximumX, out int maximumY)
        {
            minimumX = dirtyMinimumX;
            minimumY = dirtyMinimumY;
            maximumX = dirtyMaximumX;
            maximumY = dirtyMaximumY;
            return IsDirty;
        }

        public Bounds Bounds
        {
            get
            {
                Vector3 size = new Vector3(worldWidth, 0f, worldHeight);
                return new Bounds(WorldOrigin + size * 0.5f, size);
            }
        }

        public void ClearCurrentVisibility()
        {
            for (int i = 0; i < currentVisibility.Length; i++)
            {
                if (currentVisibility[i] > 0f)
                    MarkDirtyCell(i % Width, i / Width);
                currentVisibility[i] = 0f;
                visibilityCoverage[i] = 0;
            }

            Array.Clear(visibilityRowDifference, 0, visibilityRowDifference.Length);
            Array.Clear(dirtyVisibilityRows, 0, dirtyVisibilityRows.Length);
            pendingVisibilityChangeTimes.Clear();
            dirtyVisibilityChangeLogicTimes.Clear();
            visibilityResetVersion = checked(visibilityResetVersion + 1);
            VisibilityReset?.Invoke();

        }

        public void MarkVisible(int x, int y)
        {
            if (!IsValidCell(x, y))
                throw new ArgumentOutOfRangeException(nameof(x), $"Fog visibility cell ({x},{y}) is outside {Width}x{Height}.");

            int index = GetIndex(x, y);
            if (!walkable[index] || currentVisibility[index] >= 1f)
                return;

            currentVisibility[index] = 1f;
            MarkDirtyCell(x, y);
        }

        public void ChangeVisibilityCoverage(Fog3VisibilityRowInterval interval, int delta)
        {
            if (delta != -1 && delta != 1)
                throw new ArgumentOutOfRangeException(nameof(delta), "Fog visibility coverage delta must be -1 or 1.");
            if (!IsValidCell(interval.MinimumX, interval.Y) || !IsValidCell(interval.MaximumX, interval.Y))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(interval),
                    $"Fog visibility interval y={interval.Y}, x={interval.MinimumX}..{interval.MaximumX} is outside {Width}x{Height}.");
            }

            int rowOffset = interval.Y * (Width + 1);
            visibilityRowDifference[rowOffset + interval.MinimumX] = checked(
                visibilityRowDifference[rowOffset + interval.MinimumX] + delta);
            visibilityRowDifference[rowOffset + interval.MaximumX + 1] = checked(
                visibilityRowDifference[rowOffset + interval.MaximumX + 1] - delta);
            dirtyVisibilityRows[interval.Y] = true;
        }

        public void RecordVisibilityChangeLogicTimeCandidate(
            int x,
            int y,
            bool becomingVisible,
            double logicTime)
        {
            if (!IsValidCell(x, y))
                throw new ArgumentOutOfRangeException(nameof(x), $"Fog visibility timing cell ({x},{y}) is outside {Width}x{Height}.");
            if (double.IsNaN(logicTime) || double.IsInfinity(logicTime) || logicTime < 0d)
                throw new ArgumentOutOfRangeException(nameof(logicTime), "Fog visibility logic time must be finite and non-negative.");

            int index = GetIndex(x, y);
            pendingVisibilityChangeTimes.TryGetValue(index, out PendingVisibilityChangeTime pending);
            byte previousKinds = pending.Kinds;
            byte kind = becomingVisible ? (byte)1 : (byte)2;
            pending.Kinds = (byte)(previousKinds | kind);
            if (becomingVisible)
            {
                if ((previousKinds & kind) == 0 || logicTime < pending.EnterLogicTime)
                    pending.EnterLogicTime = logicTime;
            }
            else if ((previousKinds & kind) == 0 || logicTime > pending.ExitLogicTime)
            {
                pending.ExitLogicTime = logicTime;
            }
            pendingVisibilityChangeTimes[index] = pending;
        }

        public bool TryGetDirtyVisibilityChangeLogicTime(int x, int y, out double logicTime)
        {
            if (!IsValidCell(x, y))
                throw new ArgumentOutOfRangeException(nameof(x), $"Fog visibility timing cell ({x},{y}) is outside {Width}x{Height}.");
            return dirtyVisibilityChangeLogicTimes.TryGetValue(GetIndex(x, y), out logicTime);
        }

        public void ResolveVisibilityCoverageChanges(double logicTime)
        {
            if (double.IsNaN(logicTime) || double.IsInfinity(logicTime) || logicTime < 0d)
                throw new ArgumentOutOfRangeException(nameof(logicTime), "Fog visibility resolution logic time must be finite and non-negative.");
            bool profile = MainThreadFrameProfiler.LoggingEnabled;
            long rowTicks = 0L;
            long cellTicks = 0L;
            for (int y = 0; y < Height; y++)
            {
                long rowStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
                if (!dirtyVisibilityRows[y])
                {
                    if (profile)
                        rowTicks += System.Diagnostics.Stopwatch.GetTimestamp() - rowStartTicks;
                    continue;
                }

                int rowDifferenceOffset = y * (Width + 1);
                int cellOffset = y * Width;
                int coverage = 0;
                long cellStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
                for (int segment = 0; segment < 4; segment++)
                {
                    int segmentMinimumX = segment * Width / 4;
                    int segmentMaximumX = (segment + 1) * Width / 4;
                    long segmentStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
                    for (int x = segmentMinimumX; x < segmentMaximumX; x++)
                    {
                        coverage = checked(coverage + visibilityRowDifference[rowDifferenceOffset + x]);
                        if (coverage < 0)
                        {
                            throw new InvalidOperationException(
                                $"Fog visibility coverage became negative at ({x},{y}). coverage={coverage}.");
                        }

                        int index = cellOffset + x;
                        int previousCoverage = visibilityCoverage[index];
                        visibilityCoverage[index] = coverage;
                        if ((previousCoverage > 0) == (coverage > 0))
                            continue;

                        currentVisibility[index] = coverage > 0 ? 1f : 0f;
                        MarkDirtyCell(x, y);
                        if (pendingVisibilityChangeTimes.TryGetValue(index, out PendingVisibilityChangeTime pending)
                            && coverage > 0
                            && (pending.Kinds & 1) != 0)
                        {
                            dirtyVisibilityChangeLogicTimes[index] = pending.EnterLogicTime;
                        }
                        else if (pendingVisibilityChangeTimes.TryGetValue(index, out pending)
                                 && coverage == 0
                                 && (pending.Kinds & 2) != 0)
                        {
                            dirtyVisibilityChangeLogicTimes[index] = pending.ExitLogicTime;
                        }
                    }
                    if (profile)
                        MainThreadFrameProfiler.Record(
                            (MainThreadPerfScope)((int)MainThreadPerfScope.FogVisibilityCoverageCellsSegment0 + segment),
                            System.Diagnostics.Stopwatch.GetTimestamp() - segmentStartTicks);
                }
                long rowCellTicks = profile
                    ? System.Diagnostics.Stopwatch.GetTimestamp() - cellStartTicks
                    : 0L;
                if (profile)
                    cellTicks += rowCellTicks;

                if (coverage + visibilityRowDifference[rowDifferenceOffset + Width] != 0)
                {
                    throw new InvalidOperationException(
                        $"Fog visibility row {y} has an unbalanced difference sum.");
                }
                dirtyVisibilityRows[y] = false;
                if (profile)
                    rowTicks += System.Diagnostics.Stopwatch.GetTimestamp() - rowStartTicks - rowCellTicks;
            }

            long pendingStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            pendingVisibilityChangeTimes.Clear();
            if (profile)
            {
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FogVisibilityCoverageRows, rowTicks);
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FogVisibilityCoverageCells, cellTicks);
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FogVisibilityCoveragePending,
                    System.Diagnostics.Stopwatch.GetTimestamp() - pendingStartTicks);
            }
            VisibilityResolutionLogicTime = logicTime;
        }

        public void CollectVisibleIntervals(
            FixVector2 viewerPosition,
            Fix64 radius,
            int viewerHeight,
            int? requiredHeight,
            System.Collections.Generic.List<Fog3VisibilityRowInterval> intervals)
        {
            if (radius <= Fix64.Zero)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (viewerHeight < 0)
                throw new ArgumentOutOfRangeException(nameof(viewerHeight));
            if (requiredHeight.HasValue && requiredHeight.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(requiredHeight));
            if (intervals == null)
                throw new ArgumentNullException(nameof(intervals));
            if (!WorldToGrid(viewerPosition, out int centerX, out int centerY))
                throw new ArgumentOutOfRangeException(nameof(viewerPosition));

            bool profile = MainThreadFrameProfiler.LoggingEnabled;
            long stageStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            intervals.Clear();
            int range = GetCellRangeForRadius(radius);
            var cacheKey = new VisibilityGeometryCacheKey(
                centerX,
                centerY,
                range,
                viewerHeight,
                requiredHeight ?? -1);
            if (!visibilityGeometryCache.TryGetValue(cacheKey, out Fog3VisibilityRowInterval[] geometryIntervals))
            {
                geometryIntervals = BuildVisibilityGeometryIntervals(
                    centerX,
                    centerY,
                    range,
                    viewerHeight,
                    requiredHeight);
                visibilityGeometryCache.Add(cacheKey, geometryIntervals);
            }
            if (profile)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FogVisibilityCollectCenter,
                    System.Diagnostics.Stopwatch.GetTimestamp() - stageStartTicks);

            stageStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            Fix64 radiusSquared = radius * radius;
            for (int intervalIndex = 0; intervalIndex < geometryIntervals.Length; intervalIndex++)
            {
                Fog3VisibilityRowInterval geometryInterval = geometryIntervals[intervalIndex];
                int visibleStart = -1;
                for (int x = geometryInterval.MinimumX; x <= geometryInterval.MaximumX; x++)
                {
                    FixVector2 cellCenter = GetCellCenterFixed(x, geometryInterval.Y);
                    bool insideRadius = FixVector2.SqrMagnitude(cellCenter - viewerPosition) <= radiusSquared;
                    if (insideRadius)
                    {
                        if (visibleStart < 0)
                            visibleStart = x;
                    }
                    else if (visibleStart >= 0)
                    {
                        intervals.Add(new Fog3VisibilityRowInterval(geometryInterval.Y, visibleStart, x - 1));
                        visibleStart = -1;
                    }
                }
                if (visibleStart >= 0)
                    intervals.Add(new Fog3VisibilityRowInterval(
                        geometryInterval.Y,
                        visibleStart,
                        geometryInterval.MaximumX));
            }
            if (profile)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FogVisibilityCollectIntervals,
                    System.Diagnostics.Stopwatch.GetTimestamp() - stageStartTicks);
        }

        private Fog3VisibilityRowInterval[] BuildVisibilityGeometryIntervals(
            int centerX,
            int centerY,
            int range,
            int viewerHeight,
            int? requiredHeight)
        {
            int visitStamp = AcquireFovVisitStamp();
            FixVector2 geometryViewerPosition = GetCellCenterFixed(centerX, centerY);
            Fix64 geometryRadiusSquared = Fix64.FromRaw(long.MaxValue);
            TryMarkFovCell(centerX, centerY, geometryViewerPosition, geometryRadiusSquared, requiredHeight, visitStamp);
            for (int octant = 0; octant < 8; octant++)
            {
                CastVisibilityOctant(
                    centerX,
                    centerY,
                    1,
                    Fix64.One,
                    Fix64.Zero,
                    range,
                    geometryViewerPosition,
                    geometryRadiusSquared,
                    viewerHeight,
                    requiredHeight,
                    visitStamp,
                    octant);
            }

            var result = new System.Collections.Generic.List<Fog3VisibilityRowInterval>();
            int minimumY = Math.Max(0, centerY - range);
            int maximumY = Math.Min(Height - 1, centerY + range);
            int minimumX = Math.Max(0, centerX - range);
            int maximumX = Math.Min(Width - 1, centerX + range);
            for (int y = minimumY; y <= maximumY; y++)
            {
                int x = minimumX;
                while (x <= maximumX)
                {
                    while (x <= maximumX && fovVisitStamps[GetIndex(x, y)] != visitStamp)
                        x++;
                    if (x > maximumX)
                        break;
                    int intervalStart = x;
                    while (x < maximumX && fovVisitStamps[GetIndex(x + 1, y)] == visitStamp)
                        x++;
                    result.Add(new Fog3VisibilityRowInterval(y, intervalStart, x));
                    x++;
                }
            }
            return result.ToArray();
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

            if (clamped <= currentVisibility[index])
                return;

            currentVisibility[index] = clamped;
            MarkDirtyCell(x, y);
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
            MarkDirtyCell(x, y);
            return true;
        }

        public void MarkClean()
        {
            for (int i = 0; i < dirtyCellIndices.Count; i++)
            {
                int index = dirtyCellIndices[i];
                dirtyCellFlags[index] = false;
            }
            IsDirty = false;
            dirtyMinimumX = 0;
            dirtyMinimumY = 0;
            dirtyMaximumX = -1;
            dirtyMaximumY = -1;
            dirtyCellIndices.Clear();
            dirtyVisibilityChangeLogicTimes.Clear();
        }

        public void ResetExploration()
        {
            bool changed = exploredCellCount > 0;
            for (int i = 0; i < explored.Length; i++)
            {
                explored[i] = false;
                currentVisibility[i] = 0f;
                visibilityCoverage[i] = 0;
            }

            Array.Clear(visibilityRowDifference, 0, visibilityRowDifference.Length);
            Array.Clear(dirtyVisibilityRows, 0, dirtyVisibilityRows.Length);
            visibilityResetVersion = checked(visibilityResetVersion + 1);
            VisibilityReset?.Invoke();

            exploredCellCount = 0;
            explorationXorDigest = 0;
            explorationSumDigest = 0;
            if (changed)
                explorationVersion = checked(explorationVersion + 1);

            MarkAllDirty();
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
                visibilityCoverage[i] = 0;
                if (explored[i])
                    AddExplorationDigest(i);
            }
            explorationVersion = checked(explorationVersion + 1);
            lastExplorationCheckpoint = checkpoint;
            lastCheckpointExplorationVersion = explorationVersion;
            MarkAllDirty();
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
            int terrainX = NavigationGridFixedMath.WorldToGridCell(worldPosition.x, originXGridRaw, terrainCellSizeGridRaw);
            int terrainY = NavigationGridFixedMath.WorldToGridCell(worldPosition.y, originZGridRaw, terrainCellSizeGridRaw);
            long lowerXRaw = checked(originXGridRaw + checked((long)terrainX * terrainCellSizeGridRaw));
            long lowerYRaw = checked(originZGridRaw + checked((long)terrainY * terrainCellSizeGridRaw));
            Fix64 progress = slope.DirectionX != 0
                ? NavigationGridFixedMath.ResolveCellFraction(
                    NavigationGridFixedMath.Fix64ToGridRaw(worldPosition.x),
                    lowerXRaw,
                    terrainCellSizeGridRaw)
                : NavigationGridFixedMath.ResolveCellFraction(
                    NavigationGridFixedMath.Fix64ToGridRaw(worldPosition.y),
                    lowerYRaw,
                    terrainCellSizeGridRaw);
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
            long worldXGridRaw = NavigationGridFixedMath.FloatToGridRaw(worldPos.x);
            long worldZGridRaw = NavigationGridFixedMath.FloatToGridRaw(worldPos.z);
            gridX = NavigationGridFixedMath.GridRawToCell(worldXGridRaw, originXGridRaw, cellSizeXGridRaw);
            gridY = NavigationGridFixedMath.GridRawToCell(worldZGridRaw, originZGridRaw, cellSizeYGridRaw);
            return worldXGridRaw >= originXGridRaw
                   && worldXGridRaw < maximumXGridRaw
                   && worldZGridRaw >= originZGridRaw
                   && worldZGridRaw < maximumZGridRaw
                   && IsValidCell(gridX, gridY);
        }

        public bool WorldToGrid(FixVector2 worldPos, out int gridX, out int gridY)
        {
            long worldXGridRaw = NavigationGridFixedMath.Fix64ToGridRaw(worldPos.x);
            long worldZGridRaw = NavigationGridFixedMath.Fix64ToGridRaw(worldPos.y);
            gridX = NavigationGridFixedMath.GridRawToCell(worldXGridRaw, originXGridRaw, cellSizeXGridRaw);
            gridY = NavigationGridFixedMath.GridRawToCell(worldZGridRaw, originZGridRaw, cellSizeYGridRaw);
            return worldXGridRaw >= originXGridRaw
                   && worldXGridRaw < maximumXGridRaw
                   && worldZGridRaw >= originZGridRaw
                   && worldZGridRaw < maximumZGridRaw
                   && IsValidCell(gridX, gridY);
        }

        public Vector3 GridToWorldCenter(int gridX, int gridY)
        {
            if (!IsValidCell(gridX, gridY))
                throw new ArgumentOutOfRangeException(nameof(gridX), $"Fog cell ({gridX},{gridY}) is outside {Width}x{Height}.");

            long centerXRaw = ResolveCellCenterRaw(gridX, originXGridRaw, cellSizeXGridRaw, maximumXGridRaw);
            long centerZRaw = ResolveCellCenterRaw(gridY, originZGridRaw, cellSizeYGridRaw, maximumZGridRaw);
            return new Vector3(
                (float)NavigationGridFixedMath.GridRawToFix64(centerXRaw),
                WorldOrigin.y,
                (float)NavigationGridFixedMath.GridRawToFix64(centerZRaw));
        }

        public FixVector2 GetCellCenterFixed(int gridX, int gridY)
        {
            if (!IsValidCell(gridX, gridY))
                throw new ArgumentOutOfRangeException(nameof(gridX), $"Fog cell ({gridX},{gridY}) is outside {Width}x{Height}.");

            long centerXRaw = ResolveCellCenterRaw(gridX, originXGridRaw, cellSizeXGridRaw, maximumXGridRaw);
            long centerYRaw = ResolveCellCenterRaw(gridY, originZGridRaw, cellSizeYGridRaw, maximumZGridRaw);
            return new FixVector2(
                NavigationGridFixedMath.GridRawToFix64(centerXRaw),
                NavigationGridFixedMath.GridRawToFix64(centerYRaw));
        }

        public int GetCellRangeForRadius(Fix64 radius)
        {
            return Math.Max(
                NavigationGridFixedMath.DivideCeilingByCellSize(radius, cellSizeXGridRaw),
                NavigationGridFixedMath.DivideCeilingByCellSize(radius, cellSizeYGridRaw));
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

        private void MarkDirtyCell(int x, int y)
        {
            if (!IsValidCell(x, y))
                throw new ArgumentOutOfRangeException(nameof(x), $"Fog dirty cell ({x},{y}) is outside {Width}x{Height}.");

            if (!IsDirty)
            {
                dirtyMinimumX = x;
                dirtyMaximumX = x;
                dirtyMinimumY = y;
                dirtyMaximumY = y;
                IsDirty = true;
            }
            else
            {
                dirtyMinimumX = Math.Min(dirtyMinimumX, x);
                dirtyMaximumX = Math.Max(dirtyMaximumX, x);
                dirtyMinimumY = Math.Min(dirtyMinimumY, y);
                dirtyMaximumY = Math.Max(dirtyMaximumY, y);
            }
            int index = GetIndex(x, y);
            if (dirtyCellFlags[index])
                return;
            dirtyCellFlags[index] = true;
            dirtyCellIndices.Add(index);
        }

        private void MarkAllDirty()
        {
            dirtyMinimumX = 0;
            dirtyMinimumY = 0;
            dirtyMaximumX = Width - 1;
            dirtyMaximumY = Height - 1;
            IsDirty = true;
            Array.Clear(dirtyCellFlags, 0, dirtyCellFlags.Length);
            dirtyCellIndices.Clear();
            dirtyVisibilityChangeLogicTimes.Clear();
            for (int i = 0; i < currentVisibility.Length; i++)
            {
                dirtyCellFlags[i] = true;
                dirtyCellIndices.Add(i);
            }
        }

        private int AcquireFovVisitStamp()
        {
            if (nextFovVisitStamp == int.MaxValue)
            {
                Array.Clear(fovVisitStamps, 0, fovVisitStamps.Length);
                nextFovVisitStamp = 1;
            }
            return nextFovVisitStamp++;
        }

        private void CastVisibilityOctant(
            int centerX,
            int centerY,
            int row,
            Fix64 startSlope,
            Fix64 endSlope,
            int range,
            FixVector2 viewerPosition,
            Fix64 radiusSquared,
            int viewerHeight,
            int? requiredHeight,
            int visitStamp,
            int octant)
        {
            if (startSlope < endSlope)
                return;

            Fix64 half = Fix64.One / (Fix64)2;
            Fix64 nextStartSlope = startSlope;
            bool blocked = false;
            for (int distance = row; distance <= range && !blocked; distance++)
            {
                int deltaY = -distance;
                for (int deltaX = -distance; deltaX <= 0; deltaX++)
                {
                    ResolveOctantCell(centerX, centerY, deltaX, deltaY, octant, out int x, out int y);
                    Fix64 leftSlope = ((Fix64)deltaX - half) / ((Fix64)deltaY + half);
                    Fix64 rightSlope = ((Fix64)deltaX + half) / ((Fix64)deltaY - half);
                    if (startSlope < rightSlope)
                        continue;
                    if (endSlope > leftSlope)
                        break;

                    bool opaque = !IsValidCell(x, y) || IsAboveViewerVisionHeight(x, y, viewerHeight);
                    if (!opaque)
                        TryMarkFovCell(x, y, viewerPosition, radiusSquared, requiredHeight, visitStamp);

                    if (blocked)
                    {
                        if (opaque)
                        {
                            nextStartSlope = rightSlope;
                            continue;
                        }

                        blocked = false;
                        startSlope = nextStartSlope;
                    }
                    else if (opaque && distance < range)
                    {
                        blocked = true;
                        CastVisibilityOctant(
                            centerX,
                            centerY,
                            distance + 1,
                            startSlope,
                            leftSlope,
                            range,
                            viewerPosition,
                            radiusSquared,
                            viewerHeight,
                            requiredHeight,
                            visitStamp,
                            octant);
                        nextStartSlope = rightSlope;
                    }
                }
            }
        }

        private void TryMarkFovCell(
            int x,
            int y,
            FixVector2 viewerPosition,
            Fix64 radiusSquared,
            int? requiredHeight,
            int visitStamp)
        {
            if (!IsValidCell(x, y) || !walkable[GetIndex(x, y)])
                return;
            if (requiredHeight.HasValue && platformHeights[GetIndex(x, y)] < 0)
                return;
            FixVector2 center = GetCellCenterFixed(x, y);
            if (FixVector2.SqrMagnitude(center - viewerPosition) > radiusSquared)
                return;
            if (requiredHeight.HasValue && GetVisionHeight(center) != requiredHeight.Value)
                return;
            fovVisitStamps[GetIndex(x, y)] = visitStamp;
        }

        private static void ResolveOctantCell(
            int centerX,
            int centerY,
            int deltaX,
            int deltaY,
            int octant,
            out int x,
            out int y)
        {
            switch (octant)
            {
                case 0: x = centerX + deltaX; y = centerY + deltaY; return;
                case 1: x = centerX + deltaY; y = centerY + deltaX; return;
                case 2: x = centerX - deltaY; y = centerY + deltaX; return;
                case 3: x = centerX - deltaX; y = centerY + deltaY; return;
                case 4: x = centerX - deltaX; y = centerY - deltaY; return;
                case 5: x = centerX - deltaY; y = centerY - deltaX; return;
                case 6: x = centerX + deltaY; y = centerY - deltaX; return;
                case 7: x = centerX + deltaX; y = centerY - deltaY; return;
                default: throw new ArgumentOutOfRangeException(nameof(octant));
            }
        }

        private static int DividePositiveCeiling(long value, long divisor)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (divisor <= 0)
                throw new ArgumentOutOfRangeException(nameof(divisor));
            return checked((int)((value + divisor - 1L) / divisor));
        }

        private static long ResolveCellCenterRaw(int cell, long originRaw, long cellSizeRaw, long maximumRaw)
        {
            long lowerRaw = checked(originRaw + checked((long)cell * cellSizeRaw));
            long upperRaw = Math.Min(checked(lowerRaw + cellSizeRaw), maximumRaw);
            if (lowerRaw >= upperRaw)
                throw new InvalidOperationException($"FOG3 cell {cell} lies outside its world bounds.");
            return lowerRaw + (upperRaw - lowerRaw) / 2L;
        }

        private ulong ComputeTerrainHash()
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            AddHash(ref hash, unchecked((ulong)Width), prime);
            AddHash(ref hash, unchecked((ulong)Height), prime);
            AddHash(ref hash, unchecked((ulong)System.BitConverter.SingleToInt32Bits(CellSizeX)), prime);
            AddHash(ref hash, unchecked((ulong)System.BitConverter.SingleToInt32Bits(CellSizeY)), prime);
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
