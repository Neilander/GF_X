using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct LogicTransform
{
    public LogicTransform(FixVector2 position, FixVector2 velocity, FixVector2 facing)
    {
        Position = position;
        Velocity = velocity;
        Facing = facing;
    }

    public FixVector2 Position { get; }
    public FixVector2 Velocity { get; }
    public FixVector2 Facing { get; }

    public LogicTransform WithMotion(FixVector2 position, FixVector2 velocity)
    {
        FixVector2 facing = velocity == FixVector2.Zero ? Facing : velocity.GetNormalized();
        return new LogicTransform(position, velocity, facing);
    }
}

internal enum LogicStaticCollisionObstacleKind : byte
{
    Box = 0,
    Circle = 1,
}

internal readonly struct LogicStaticCollisionObstacle
{
    public LogicStaticCollisionObstacle(
        int stableId,
        LogicStaticCollisionObstacleKind kind,
        FixVector2 center,
        FixVector2 halfExtents,
        Fix64 radius)
    {
        if (stableId == 0)
            throw new ArgumentOutOfRangeException(nameof(stableId));
        if (kind == LogicStaticCollisionObstacleKind.Box
            && (halfExtents.x < Fix64.Zero || halfExtents.y < Fix64.Zero))
        {
            throw new ArgumentOutOfRangeException(nameof(halfExtents));
        }
        if (kind == LogicStaticCollisionObstacleKind.Circle && radius <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(radius));

        StableId = stableId;
        Kind = kind;
        Center = center;
        HalfExtents = halfExtents;
        Radius = radius;
    }

    public int StableId { get; }
    public LogicStaticCollisionObstacleKind Kind { get; }
    public FixVector2 Center { get; }
    public FixVector2 HalfExtents { get; }
    public Fix64 Radius { get; }
}

internal readonly struct LogicStaticCollisionSourceData
{
    public LogicStaticCollisionSourceData(
        int agentTypeId,
        int worldVersion,
        int width,
        int height,
        long cellSizeGridRaw,
        long encodedCenterClearanceFixedRaw,
        long originXGridRaw,
        long originYGridRaw,
        bool[] baseWalkableMask,
        byte[] baseNeighborTraversalMask,
        LogicStaticCollisionObstacle[] runtimeObstacles)
        : this(
            agentTypeId,
            worldVersion,
            width,
            height,
            cellSizeGridRaw,
            encodedCenterClearanceFixedRaw,
            originXGridRaw,
            originYGridRaw,
            baseWalkableMask,
            baseNeighborTraversalMask,
            null,
            null,
            runtimeObstacles)
    {
    }

    public LogicStaticCollisionSourceData(
        int agentTypeId,
        int worldVersion,
        int width,
        int height,
        long cellSizeGridRaw,
        long encodedCenterClearanceFixedRaw,
        long originXGridRaw,
        long originYGridRaw,
        bool[] baseWalkableMask,
        byte[] baseNeighborTraversalMask,
        FixVector2[] boundaryVertices,
        int[] boundaryPathStarts,
        LogicStaticCollisionObstacle[] runtimeObstacles)
    {
        if (cellSizeGridRaw <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSizeGridRaw), cellSizeGridRaw, "Cell size raw must be positive.");
        if (encodedCenterClearanceFixedRaw < 0)
            throw new ArgumentOutOfRangeException(nameof(encodedCenterClearanceFixedRaw), encodedCenterClearanceFixedRaw, "Encoded center clearance raw must be non-negative.");
        AgentTypeId = agentTypeId;
        WorldVersion = worldVersion;
        Width = width;
        Height = height;
        CellSizeGridRaw = cellSizeGridRaw;
        EncodedCenterClearanceFixedRaw = encodedCenterClearanceFixedRaw;
        OriginXGridRaw = originXGridRaw;
        OriginYGridRaw = originYGridRaw;
        BaseWalkableMask = baseWalkableMask;
        BaseNeighborTraversalMask = baseNeighborTraversalMask;
        BoundaryVertices = boundaryVertices;
        BoundaryPathStarts = boundaryPathStarts;
        RuntimeObstacles = runtimeObstacles ?? Array.Empty<LogicStaticCollisionObstacle>();
    }

    public int AgentTypeId { get; }
    public int WorldVersion { get; }
    public int Width { get; }
    public int Height { get; }
    public long CellSizeGridRaw { get; }
    public long EncodedCenterClearanceFixedRaw { get; }
    public long OriginXGridRaw { get; }
    public long OriginYGridRaw { get; }
    public bool[] BaseWalkableMask { get; }
    public byte[] BaseNeighborTraversalMask { get; }
    public FixVector2[] BoundaryVertices { get; }
    public int[] BoundaryPathStarts { get; }
    public LogicStaticCollisionObstacle[] RuntimeObstacles { get; }
}

public sealed class LogicStaticCollisionWorld
{
    private const int GridFractionalPlaces = 32;
    private const int GridToFixShift = GridFractionalPlaces - Fix64.FRACTIONAL_PLACES;
    private readonly bool[] m_WalkableMask;
    private readonly byte[] m_NeighborTraversalMask;
    private readonly long m_CellSizeGridRaw;
    private readonly long m_OriginXGridRaw;
    private readonly long m_OriginYGridRaw;
    private readonly FixVector2[] m_BoundaryVertices;
    private readonly int[] m_BoundaryPathStarts;
    private readonly int[] m_BoundaryNextVertex;
    private readonly int[][] m_BoundarySegmentIndicesByCell;
    private readonly Fix64[] m_BoundaryPathMinX;
    private readonly Fix64[] m_BoundaryPathMaxX;
    private readonly Fix64[] m_BoundaryPathMinY;
    private readonly Fix64[] m_BoundaryPathMaxY;

    public LogicStaticCollisionWorld(
        int agentTypeId,
        int worldVersion,
        int width,
        int height,
        Fix64 cellSize,
        FixVector2 origin,
        bool[] walkableMask)
        : this(
            agentTypeId,
            worldVersion,
            width,
            height,
            checked(cellSize.RawValue << GridToFixShift),
            checked(origin.x.RawValue << GridToFixShift),
            checked(origin.y.RawValue << GridToFixShift),
            walkableMask,
            null,
            null,
            null)
    {
    }

    public LogicStaticCollisionWorld(
        int agentTypeId,
        int worldVersion,
        int width,
        int height,
        Fix64 cellSize,
        FixVector2 origin,
        bool[] walkableMask,
        byte[] neighborTraversalMask)
        : this(
            agentTypeId,
            worldVersion,
            width,
            height,
            checked(cellSize.RawValue << GridToFixShift),
            checked(origin.x.RawValue << GridToFixShift),
            checked(origin.y.RawValue << GridToFixShift),
            walkableMask,
            neighborTraversalMask,
            null,
            null)
    {
    }

    public LogicStaticCollisionWorld(
        int agentTypeId,
        int worldVersion,
        int width,
        int height,
        float cellSize,
        Vector3 origin,
        bool[] walkableMask)
        : this(
            agentTypeId,
            worldVersion,
            width,
            height,
            FloatToGridRaw(cellSize),
            FloatToGridRaw(origin.x),
            FloatToGridRaw(origin.z),
            walkableMask,
            null,
            null,
            null)
    {
    }

    internal LogicStaticCollisionWorld(
        int agentTypeId,
        int worldVersion,
        int width,
        int height,
        long cellSizeGridRaw,
        long originXGridRaw,
        long originYGridRaw,
        bool[] walkableMask,
        byte[] neighborTraversalMask,
        FixVector2[] boundaryVertices = null,
        int[] boundaryPathStarts = null)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Collision world width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "Collision world height must be positive.");
        if (cellSizeGridRaw <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSizeGridRaw), "Collision world cell size must be positive.");
        if (walkableMask == null)
            throw new ArgumentNullException(nameof(walkableMask));
        if (walkableMask.Length != checked(width * height))
        {
            throw new ArgumentException(
                $"Collision world mask length {walkableMask.Length} does not match {width}x{height}.",
                nameof(walkableMask));
        }
        if (neighborTraversalMask != null && neighborTraversalMask.Length != walkableMask.Length)
        {
            throw new ArgumentException(
                $"Collision world neighbor traversal mask length {neighborTraversalMask.Length} does not match {width}x{height}.",
                nameof(neighborTraversalMask));
        }
        ValidateBoundaryGeometry(boundaryVertices, boundaryPathStarts);

        AgentTypeId = agentTypeId;
        WorldVersion = worldVersion;
        Width = width;
        Height = height;
        m_CellSizeGridRaw = cellSizeGridRaw;
        m_OriginXGridRaw = originXGridRaw;
        m_OriginYGridRaw = originYGridRaw;
        CellSize = GridRawToFix64(cellSizeGridRaw);
        Origin = new FixVector2(GridRawToFix64(originXGridRaw), GridRawToFix64(originYGridRaw));
        m_WalkableMask = (bool[])walkableMask.Clone();
        m_NeighborTraversalMask = neighborTraversalMask != null
            ? (byte[])neighborTraversalMask.Clone()
            : null;
        m_BoundaryVertices = boundaryVertices != null ? (FixVector2[])boundaryVertices.Clone() : null;
        m_BoundaryPathStarts = boundaryPathStarts != null ? (int[])boundaryPathStarts.Clone() : null;
        if (m_BoundaryVertices != null)
        {
            BuildBoundaryGeometryRuntimeData(
                out m_BoundaryNextVertex,
                out m_BoundarySegmentIndicesByCell,
                out m_BoundaryPathMinX,
                out m_BoundaryPathMaxX,
                out m_BoundaryPathMinY,
                out m_BoundaryPathMaxY);
        }
    }

    public int AgentTypeId { get; }
    public int WorldVersion { get; }
    public int Width { get; }
    public int Height { get; }
    public Fix64 CellSize { get; }
    public FixVector2 Origin { get; }
    public Fix64 MinCenterX => Origin.x;
    public Fix64 MinCenterY => Origin.y;
    public Fix64 MaxWorldX => GridRawToFix64(checked(m_OriginXGridRaw + checked((long)Width * m_CellSizeGridRaw)));
    public Fix64 MaxWorldY => GridRawToFix64(checked(m_OriginYGridRaw + checked((long)Height * m_CellSizeGridRaw)));
    public bool HasBoundaryGeometry => m_BoundaryVertices != null;
    public int BoundarySegmentCount => m_BoundaryVertices?.Length ?? 0;

    internal void GetBoundarySegment(int segmentIndex, out FixVector2 start, out FixVector2 end)
    {
        if (!HasBoundaryGeometry || segmentIndex < 0 || segmentIndex >= m_BoundaryVertices.Length)
            throw new ArgumentOutOfRangeException(nameof(segmentIndex));
        start = m_BoundaryVertices[segmentIndex];
        end = m_BoundaryVertices[m_BoundaryNextVertex[segmentIndex]];
    }

    internal int[] GetBoundarySegmentIndicesForCell(int x, int y)
    {
        if (!HasBoundaryGeometry || x < 0 || x >= Width || y < 0 || y >= Height)
            return Array.Empty<int>();
        return m_BoundarySegmentIndicesByCell[GetIndex(x, y)];
    }

    internal bool ContainsPointInBoundaryGeometry(FixVector2 point)
    {
        if (!HasBoundaryGeometry)
            throw new InvalidOperationException("Static collision world has no boundary geometry.");

        bool inside = false;
        for (int pathIndex = 0; pathIndex < m_BoundaryPathStarts.Length - 1; pathIndex++)
        {
            if (point.x < m_BoundaryPathMinX[pathIndex]
                || point.x > m_BoundaryPathMaxX[pathIndex]
                || point.y < m_BoundaryPathMinY[pathIndex]
                || point.y > m_BoundaryPathMaxY[pathIndex])
            {
                continue;
            }

            int start = m_BoundaryPathStarts[pathIndex];
            int end = m_BoundaryPathStarts[pathIndex + 1];
            bool insidePath = false;
            for (int i = start; i < end; i++)
            {
                FixVector2 a = m_BoundaryVertices[i];
                FixVector2 b = m_BoundaryVertices[m_BoundaryNextVertex[i]];
                if ((a.y > point.y) == (b.y > point.y))
                    continue;
                Fix64 intersectionX = a.x + (point.y - a.y) * (b.x - a.x) / (b.y - a.y);
                if (point.x < intersectionX)
                    insidePath = !insidePath;
            }
            if (insidePath)
                inside = !inside;
        }
        return inside;
    }

    public bool IsWalkable(int x, int y)
    {
        return x >= 0
               && x < Width
               && y >= 0
               && y < Height
               && m_WalkableMask[x + y * Width];
    }

    public int GetIndex(int x, int y)
    {
        return checked(x + y * Width);
    }

    public bool CanTraverseCardinal(int fromX, int fromY, int toX, int toY)
    {
        if (!IsWalkable(fromX, fromY) || !IsWalkable(toX, toY))
            return false;
        if (m_NeighborTraversalMask == null)
            return true;

        int dx = toX - fromX;
        int dy = toY - fromY;
        int forwardBit;
        int reverseBit;
        if (dx == -1 && dy == 0)
        {
            forwardBit = 3;
            reverseBit = 4;
        }
        else if (dx == 1 && dy == 0)
        {
            forwardBit = 4;
            reverseBit = 3;
        }
        else if (dx == 0 && dy == -1)
        {
            forwardBit = 1;
            reverseBit = 6;
        }
        else if (dx == 0 && dy == 1)
        {
            forwardBit = 6;
            reverseBit = 1;
        }
        else
        {
            return false;
        }

        return (m_NeighborTraversalMask[GetIndex(fromX, fromY)] & (1 << forwardBit)) != 0
               && (m_NeighborTraversalMask[GetIndex(toX, toY)] & (1 << reverseBit)) != 0;
    }

    public Fix64 GetCellMinX(int x)
    {
        return GridRawToFix64(checked(m_OriginXGridRaw + checked((long)x * m_CellSizeGridRaw)));
    }

    public Fix64 GetCellMinY(int y)
    {
        return GridRawToFix64(checked(m_OriginYGridRaw + checked((long)y * m_CellSizeGridRaw)));
    }

    public int WorldToGridX(Fix64 worldX)
    {
        long worldRaw = checked(worldX.RawValue << GridToFixShift);
        return FloorDivRaw(checked(worldRaw - m_OriginXGridRaw), m_CellSizeGridRaw);
    }

    public int WorldToGridY(Fix64 worldY)
    {
        long worldRaw = checked(worldY.RawValue << GridToFixShift);
        return FloorDivRaw(checked(worldRaw - m_OriginYGridRaw), m_CellSizeGridRaw);
    }

    private static void ValidateBoundaryGeometry(FixVector2[] vertices, int[] pathStarts)
    {
        bool provided = vertices != null || pathStarts != null;
        if (!provided)
            return;
        if (vertices == null || vertices.Length < 3)
            throw new ArgumentException("Collision boundary vertices are missing.", nameof(vertices));
        if (pathStarts == null || pathStarts.Length < 2 || pathStarts[0] != 0 || pathStarts[pathStarts.Length - 1] != vertices.Length)
            throw new ArgumentException("Collision boundary path starts are invalid.", nameof(pathStarts));
        for (int pathIndex = 0; pathIndex < pathStarts.Length - 1; pathIndex++)
        {
            int start = pathStarts[pathIndex];
            int end = pathStarts[pathIndex + 1];
            if (start < 0 || end > vertices.Length || end - start < 3)
                throw new ArgumentException($"Collision boundary path {pathIndex} is invalid. start={start} end={end}.", nameof(pathStarts));
            for (int i = start; i < end; i++)
            {
                if (vertices[i] == vertices[i + 1 < end ? i + 1 : start])
                    throw new ArgumentException($"Collision boundary path {pathIndex} contains a zero-length edge at vertex={i}.", nameof(vertices));
            }
        }
    }

    private void BuildBoundaryGeometryRuntimeData(
        out int[] nextVertex,
        out int[][] segmentIndicesByCell,
        out Fix64[] pathMinX,
        out Fix64[] pathMaxX,
        out Fix64[] pathMinY,
        out Fix64[] pathMaxY)
    {
        nextVertex = new int[m_BoundaryVertices.Length];
        int pathCount = m_BoundaryPathStarts.Length - 1;
        pathMinX = new Fix64[pathCount];
        pathMaxX = new Fix64[pathCount];
        pathMinY = new Fix64[pathCount];
        pathMaxY = new Fix64[pathCount];
        var buckets = new List<int>[checked(Width * Height)];
        for (int pathIndex = 0; pathIndex < pathCount; pathIndex++)
        {
            int start = m_BoundaryPathStarts[pathIndex];
            int end = m_BoundaryPathStarts[pathIndex + 1];
            Fix64 minX = m_BoundaryVertices[start].x;
            Fix64 maxX = minX;
            Fix64 minY = m_BoundaryVertices[start].y;
            Fix64 maxY = minY;
            for (int i = start; i < end; i++)
            {
                int next = i + 1 < end ? i + 1 : start;
                nextVertex[i] = next;
                FixVector2 a = m_BoundaryVertices[i];
                FixVector2 b = m_BoundaryVertices[next];
                minX = Fix64.Min(minX, a.x);
                maxX = Fix64.Max(maxX, a.x);
                minY = Fix64.Min(minY, a.y);
                maxY = Fix64.Max(maxY, a.y);

                int minCellX = Clamp(WorldToGridX(Fix64.Min(a.x, b.x)), 0, Width - 1);
                int maxCellX = Clamp(WorldToGridX(Fix64.Max(a.x, b.x)), 0, Width - 1);
                int minCellY = Clamp(WorldToGridY(Fix64.Min(a.y, b.y)), 0, Height - 1);
                int maxCellY = Clamp(WorldToGridY(Fix64.Max(a.y, b.y)), 0, Height - 1);
                for (int y = minCellY; y <= maxCellY; y++)
                {
                    for (int x = minCellX; x <= maxCellX; x++)
                    {
                        int cellIndex = GetIndex(x, y);
                        if (buckets[cellIndex] == null)
                            buckets[cellIndex] = new List<int>();
                        buckets[cellIndex].Add(i);
                    }
                }
            }
            pathMinX[pathIndex] = minX;
            pathMaxX[pathIndex] = maxX;
            pathMinY[pathIndex] = minY;
            pathMaxY[pathIndex] = maxY;
        }

        segmentIndicesByCell = new int[buckets.Length][];
        for (int i = 0; i < buckets.Length; i++)
            segmentIndicesByCell[i] = buckets[i] != null ? buckets[i].ToArray() : Array.Empty<int>();
    }

    private static long FloatToGridRaw(float value)
    {
        return NavigationGridFixedMath.FloatToGridRaw(value);
    }

    private static Fix64 GridRawToFix64(long value)
    {
        return NavigationGridFixedMath.GridRawToFix64(value);
    }

    private static int FloorDivRaw(long numerator, long positiveDenominator)
    {
        long quotient = numerator / positiveDenominator;
        long remainder = numerator % positiveDenominator;
        if (remainder < 0)
            quotient--;
        return checked((int)quotient);
    }

    private static int Clamp(int value, int min, int max)
    {
        return value < min ? min : value > max ? max : value;
    }
}

public enum LogicStaticCollisionFailure
{
    None = 0,
    InvalidRadius = 1,
    StartOverlapUnresolved = 2,
    ContactIterationLimit = 3,
    NoProgress = 4,
}

public enum LogicStaticCollisionSlideMode
{
    PreserveTangentialComponent = 0,
    PreserveRemainingDistance = 1,
}

public readonly struct LogicStaticCollisionContactTrace
{
    internal LogicStaticCollisionContactTrace(
        int iteration,
        int stableKey,
        Fix64 time,
        FixVector2 normal,
        FixVector2 position,
        FixVector2 incoming,
        FixVector2 leftover)
    {
        Iteration = iteration;
        StableKey = stableKey;
        Time = time;
        Normal = normal;
        Position = position;
        Incoming = incoming;
        Leftover = leftover;
    }

    public int Iteration { get; }
    public int StableKey { get; }
    public Fix64 Time { get; }
    public FixVector2 Normal { get; }
    public FixVector2 Position { get; }
    public FixVector2 Incoming { get; }
    public FixVector2 Leftover { get; }
}

public readonly struct LogicStaticCollisionSolveResult
{
    internal LogicStaticCollisionSolveResult(
        bool success,
        LogicStaticCollisionFailure failure,
        FixVector2 start,
        FixVector2 recoveredStart,
        FixVector2 desiredDisplacement,
        FixVector2 resolvedDisplacement,
        bool startedOverlapping,
        int contactCount,
        int firstHitStableKey = -1,
        FixVector2 firstHitNormal = default,
        LogicStaticCollisionContactTrace contact0 = default,
        LogicStaticCollisionContactTrace contact1 = default,
        LogicStaticCollisionContactTrace contact2 = default,
        LogicStaticCollisionContactTrace contact3 = default)
    {
        Success = success;
        Failure = failure;
        Start = start;
        RecoveredStart = recoveredStart;
        DesiredDisplacement = desiredDisplacement;
        ResolvedDisplacement = resolvedDisplacement;
        StartedOverlapping = startedOverlapping;
        ContactCount = contactCount;
        FirstHitStableKey = firstHitStableKey;
        FirstHitNormal = firstHitNormal;
        Contact0 = contact0;
        Contact1 = contact1;
        Contact2 = contact2;
        Contact3 = contact3;
    }

    public bool Success { get; }
    public LogicStaticCollisionFailure Failure { get; }
    public FixVector2 Start { get; }
    public FixVector2 RecoveredStart { get; }
    public FixVector2 DesiredDisplacement { get; }
    public FixVector2 ResolvedDisplacement { get; }
    public bool StartedOverlapping { get; }
    public int ContactCount { get; }
    public int FirstHitStableKey { get; }
    public FixVector2 FirstHitNormal { get; }

    public LogicStaticCollisionContactTrace GetContactTrace(int index)
    {
        if (index < 0 || index >= ContactCount || index >= MaxRecordedContactCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        switch (index)
        {
            case 0: return Contact0;
            case 1: return Contact1;
            case 2: return Contact2;
            default: return Contact3;
        }
    }

    public const int MaxRecordedContactCount = 4;

    private LogicStaticCollisionContactTrace Contact0 { get; }
    private LogicStaticCollisionContactTrace Contact1 { get; }
    private LogicStaticCollisionContactTrace Contact2 { get; }
    private LogicStaticCollisionContactTrace Contact3 { get; }
}

public static class DeterministicStaticCollisionSolver
{
    private const int MaxPenetrationIterations = 12;
    private const int MaxContactIterations = 4;

    private static readonly Fix64 s_Epsilon = Fix64.FromRaw(1);

    private readonly struct SweepHit
    {
        public SweepHit(Fix64 time, FixVector2 normal, int stableKey)
        {
            Time = time;
            Normal = normal;
            StableKey = stableKey;
        }

        public Fix64 Time { get; }
        public FixVector2 Normal { get; }
        public int StableKey { get; }
    }

    private readonly struct Penetration
    {
        public Penetration(Fix64 depth, FixVector2 normal, int stableKey)
        {
            Depth = depth;
            Normal = normal;
            StableKey = stableKey;
        }

        public Fix64 Depth { get; }
        public FixVector2 Normal { get; }
        public int StableKey { get; }
    }

    private struct ContactManifold
    {
        private FixVector2 m_Normal0;
        private FixVector2 m_Normal1;
        private FixVector2 m_Normal2;
        private FixVector2 m_Normal3;

        public int Count { get; private set; }

        public void Add(FixVector2 normal)
        {
            if (normal == FixVector2.Zero)
                throw new ArgumentException("Static collision contact normal cannot be zero.", nameof(normal));
            if (Count >= MaxContactIterations)
                throw new InvalidOperationException("Static collision contact manifold capacity exceeded.");

            switch (Count)
            {
                case 0: m_Normal0 = normal; break;
                case 1: m_Normal1 = normal; break;
                case 2: m_Normal2 = normal; break;
                default: m_Normal3 = normal; break;
            }
            Count++;
        }

        public FixVector2 Project(FixVector2 displacement)
        {
            if (displacement == FixVector2.Zero || IsFeasible(displacement))
                return displacement;

            FixVector2 best = FixVector2.Zero;
            long bestDistanceSquaredRaw = RawDistanceSquared(displacement);
            for (int i = 0; i < Count; i++)
            {
                FixVector2 normal = GetNormal(i);
                long normalLengthSquaredRaw = RawDot(normal, normal);
                if (normalLengthSquaredRaw <= 0)
                    throw new InvalidOperationException("Static collision contact manifold contains an invalid normal.");

                FixVector2 tangent = new FixVector2(-normal.y, normal.x);
                long projectionNumeratorRaw = RawDot(displacement, tangent);
                long projectedXNumerator = checked(tangent.x.RawValue * projectionNumeratorRaw);
                long projectedYNumerator = checked(tangent.y.RawValue * projectionNumeratorRaw);
                long floorX = DivideFloor(projectedXNumerator, normalLengthSquaredRaw);
                long ceilX = DivideCeiling(projectedXNumerator, normalLengthSquaredRaw);
                long floorY = DivideFloor(projectedYNumerator, normalLengthSquaredRaw);
                long ceilY = DivideCeiling(projectedYNumerator, normalLengthSquaredRaw);

                ConsiderProjectedLatticePoint(
                    new FixVector2(Fix64.FromRaw(floorX), Fix64.FromRaw(floorY)),
                    displacement,
                    normal,
                    tangent,
                    projectionNumeratorRaw,
                    ref best,
                    ref bestDistanceSquaredRaw);
                ConsiderProjectedLatticePoint(
                    new FixVector2(Fix64.FromRaw(floorX), Fix64.FromRaw(ceilY)),
                    displacement,
                    normal,
                    tangent,
                    projectionNumeratorRaw,
                    ref best,
                    ref bestDistanceSquaredRaw);
                ConsiderProjectedLatticePoint(
                    new FixVector2(Fix64.FromRaw(ceilX), Fix64.FromRaw(floorY)),
                    displacement,
                    normal,
                    tangent,
                    projectionNumeratorRaw,
                    ref best,
                    ref bestDistanceSquaredRaw);
                ConsiderProjectedLatticePoint(
                    new FixVector2(Fix64.FromRaw(ceilX), Fix64.FromRaw(ceilY)),
                    displacement,
                    normal,
                    tangent,
                    projectionNumeratorRaw,
                    ref best,
                    ref bestDistanceSquaredRaw);
            }
            return best;
        }

        private void ConsiderProjectedLatticePoint(
            FixVector2 candidate,
            FixVector2 displacement,
            FixVector2 projectionNormal,
            FixVector2 tangent,
            long projectionNumeratorRaw,
            ref FixVector2 best,
            ref long bestDistanceSquaredRaw)
        {
            if (!IsFeasible(candidate))
                return;

            long distanceSquaredRaw = RawDistanceSquared(candidate - displacement);
            if (distanceSquaredRaw > bestDistanceSquaredRaw)
                return;
            if (distanceSquaredRaw == bestDistanceSquaredRaw
                && !IsPreferredTie(
                    candidate,
                    best,
                    displacement,
                    projectionNormal,
                    tangent,
                    projectionNumeratorRaw))
            {
                return;
            }

            best = candidate;
            bestDistanceSquaredRaw = distanceSquaredRaw;
        }

        private static bool IsPreferredTie(
            FixVector2 candidate,
            FixVector2 current,
            FixVector2 displacement,
            FixVector2 projectionNormal,
            FixVector2 tangent,
            long projectionNumeratorRaw)
        {
            if (current == FixVector2.Zero)
                return candidate != FixVector2.Zero;

            long candidateAlignment = RawDot(candidate, displacement);
            long currentAlignment = RawDot(current, displacement);
            if (candidateAlignment != currentAlignment)
                return candidateAlignment > currentAlignment;

            long candidateSlack = RawDot(candidate, projectionNormal);
            long currentSlack = RawDot(current, projectionNormal);
            if (candidateSlack != currentSlack)
                return candidateSlack < currentSlack;

            long candidateTangent = RawDot(candidate, tangent);
            long currentTangent = RawDot(current, tangent);
            return projectionNumeratorRaw >= 0
                ? candidateTangent > currentTangent
                : candidateTangent < currentTangent;
        }

        private bool IsFeasible(FixVector2 candidate)
        {
            for (int i = 0; i < Count; i++)
            {
                if (RawDot(candidate, GetNormal(i)) < 0)
                    return false;
            }
            return true;
        }

        private static long RawDot(FixVector2 left, FixVector2 right)
        {
            return checked(
                checked(left.x.RawValue * right.x.RawValue)
                + checked(left.y.RawValue * right.y.RawValue));
        }

        private static long RawDistanceSquared(FixVector2 value)
        {
            return checked(
                checked(value.x.RawValue * value.x.RawValue)
                + checked(value.y.RawValue * value.y.RawValue));
        }

        private static long DivideFloor(long numerator, long positiveDenominator)
        {
            long quotient = numerator / positiveDenominator;
            return numerator % positiveDenominator < 0 ? checked(quotient - 1) : quotient;
        }

        private static long DivideCeiling(long numerator, long positiveDenominator)
        {
            long quotient = numerator / positiveDenominator;
            return numerator % positiveDenominator > 0 ? checked(quotient + 1) : quotient;
        }

        private FixVector2 GetNormal(int index)
        {
            switch (index)
            {
                case 0: return m_Normal0;
                case 1: return m_Normal1;
                case 2: return m_Normal2;
                case 3: return m_Normal3;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    public static LogicStaticCollisionSolveResult SolveCircle(
        LogicStaticCollisionWorld world,
        FixVector2 start,
        FixVector2 desiredDisplacement,
        Fix64 radius)
    {
        return SolveCircle(
            world,
            start,
            desiredDisplacement,
            radius,
            Array.Empty<LogicStaticCollisionObstacle>(),
            LogicStaticCollisionSlideMode.PreserveTangentialComponent);
    }

    public static LogicStaticCollisionSolveResult SolveCircle(
        LogicStaticCollisionWorld world,
        FixVector2 start,
        FixVector2 desiredDisplacement,
        Fix64 radius,
        LogicStaticCollisionSlideMode slideMode)
    {
        return SolveCircle(
            world,
            start,
            desiredDisplacement,
            radius,
            Array.Empty<LogicStaticCollisionObstacle>(),
            slideMode);
    }

    internal static LogicStaticCollisionSolveResult SolveCircle(
        LogicStaticCollisionWorld world,
        FixVector2 start,
        FixVector2 desiredDisplacement,
        Fix64 radius,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        LogicStaticCollisionSlideMode slideMode = LogicStaticCollisionSlideMode.PreserveTangentialComponent)
    {
        return SolveCircle(
            world,
            start,
            desiredDisplacement,
            radius,
            radius,
            radius,
            runtimeObstacles,
            slideMode);
    }

    internal static LogicStaticCollisionSolveResult SolveCircle(
        LogicStaticCollisionWorld world,
        FixVector2 start,
        FixVector2 desiredDisplacement,
        Fix64 radius,
        Fix64 fullRadius,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        LogicStaticCollisionSlideMode slideMode = LogicStaticCollisionSlideMode.PreserveTangentialComponent)
    {
        return SolveCircle(
            world,
            start,
            desiredDisplacement,
            radius,
            fullRadius,
            fullRadius,
            runtimeObstacles,
            slideMode);
    }

    internal static LogicStaticCollisionSolveResult SolveCircle(
        LogicStaticCollisionWorld world,
        FixVector2 start,
        FixVector2 desiredDisplacement,
        Fix64 radius,
        Fix64 topologyEdgeRadius,
        Fix64 runtimeObstacleRadius,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        LogicStaticCollisionSlideMode slideMode)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (runtimeObstacles == null)
            throw new ArgumentNullException(nameof(runtimeObstacles));
        if (slideMode != LogicStaticCollisionSlideMode.PreserveTangentialComponent
            && slideMode != LogicStaticCollisionSlideMode.PreserveRemainingDistance)
        {
            throw new ArgumentOutOfRangeException(nameof(slideMode), slideMode, "Unsupported static collision slide mode.");
        }
        if (radius < Fix64.Zero
            || topologyEdgeRadius < Fix64.Zero
            || runtimeObstacleRadius < Fix64.Zero
            || radius * (Fix64)2 > world.MaxWorldX - world.Origin.x
            || radius * (Fix64)2 > world.MaxWorldY - world.Origin.y)
        {
            return FailureResult(
                LogicStaticCollisionFailure.InvalidRadius,
                start,
                start,
                desiredDisplacement,
                false,
                0);
        }

        FixVector2 position = start;
        bool startedOverlapping = false;
        if (!RecoverStart(
                world,
                runtimeObstacles,
                ref position,
                radius,
                topologyEdgeRadius,
                runtimeObstacleRadius,
                ref startedOverlapping))
        {
            return FailureResult(
                LogicStaticCollisionFailure.StartOverlapUnresolved,
                start,
                position,
                desiredDisplacement,
                startedOverlapping,
                0);
        }

        FixVector2 recoveredStart = position;
        FixVector2 remaining = desiredDisplacement;
        int contactCount = 0;
        int firstHitStableKey = -1;
        FixVector2 firstHitNormal = FixVector2.Zero;
        LogicStaticCollisionContactTrace contact0 = default;
        LogicStaticCollisionContactTrace contact1 = default;
        LogicStaticCollisionContactTrace contact2 = default;
        LogicStaticCollisionContactTrace contact3 = default;
        ContactManifold contactManifold = default;
        for (int iteration = 0; iteration < MaxContactIterations; iteration++)
        {
            if (remaining == FixVector2.Zero)
                break;

            if (!TryFindEarliestHit(
                    world,
                    runtimeObstacles,
                    position,
                    remaining,
                    radius,
                    topologyEdgeRadius,
                    runtimeObstacleRadius,
                    out SweepHit hit))
            {
                position += remaining;
                remaining = FixVector2.Zero;
                break;
            }

            hit = RefineHitTimeToAdjacentClearLatticePoint(
                world,
                runtimeObstacles,
                position,
                remaining,
                radius,
                topologyEdgeRadius,
                runtimeObstacleRadius,
                hit);

            if (firstHitStableKey < 0)
            {
                firstHitStableKey = hit.StableKey;
                firstHitNormal = hit.Normal;
            }

            FixVector2 incoming = remaining;
            Fix64 travelTime = hit.Time;
            FixVector2 traveled = MultiplyVectorTowardZero(incoming, travelTime);
            position += traveled;

            FixVector2 leftover = incoming - traveled;
            Fix64 remainingDistance = FixVector2.Magnitude(leftover);
            if (traveled != FixVector2.Zero)
                contactManifold = default;
            contactManifold.Add(hit.Normal);
            leftover = contactManifold.Project(leftover);
            if (slideMode == LogicStaticCollisionSlideMode.PreserveRemainingDistance
                && leftover != FixVector2.Zero)
            {
                leftover *= remainingDistance / FixVector2.Magnitude(leftover);
                leftover = contactManifold.Project(leftover);
            }

            var trace = new LogicStaticCollisionContactTrace(
                iteration,
                hit.StableKey,
                hit.Time,
                hit.Normal,
                position,
                incoming,
                leftover);
            switch (iteration)
            {
                case 0: contact0 = trace; break;
                case 1: contact1 = trace; break;
                case 2: contact2 = trace; break;
                case 3: contact3 = trace; break;
            }

            contactCount++;
            if (travelTime == Fix64.Zero && leftover == remaining)
            {
                return FailureResult(
                    LogicStaticCollisionFailure.NoProgress,
                    start,
                    position,
                    desiredDisplacement,
                    startedOverlapping,
                    contactCount,
                    firstHitStableKey,
                    firstHitNormal,
                    contact0,
                    contact1,
                    contact2,
                    contact3);
            }

            remaining = leftover;
        }

        if (remaining != FixVector2.Zero
            && !TryFindEarliestHit(
                world,
                runtimeObstacles,
                position,
                remaining,
                radius,
                topologyEdgeRadius,
                runtimeObstacleRadius,
                out _))
        {
            position += remaining;
            remaining = FixVector2.Zero;
        }

        if (remaining != FixVector2.Zero)
        {
            return FailureResult(
                LogicStaticCollisionFailure.ContactIterationLimit,
                start,
                position,
                desiredDisplacement,
                startedOverlapping,
                contactCount,
                firstHitStableKey,
                firstHitNormal,
                contact0,
                contact1,
                contact2,
                contact3);
        }

        return new LogicStaticCollisionSolveResult(
            true,
            LogicStaticCollisionFailure.None,
            start,
            recoveredStart,
            desiredDisplacement,
            position - start,
            startedOverlapping,
            contactCount,
            firstHitStableKey,
            firstHitNormal,
            contact0,
            contact1,
            contact2,
            contact3);
    }

    private static SweepHit RefineHitTimeToAdjacentClearLatticePoint(
        LogicStaticCollisionWorld world,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        FixVector2 start,
        FixVector2 displacement,
        Fix64 boundaryRadius,
        Fix64 topologyEdgeRadius,
        Fix64 runtimeObstacleRadius,
        SweepHit hit)
    {
        if (hit.Time.RawValue >= Fix64.One.RawValue)
            return hit;

        Fix64 adjacentTime = Fix64.FromRaw(checked(hit.Time.RawValue + 1));
        FixVector2 contact = start + MultiplyVectorTowardZero(displacement, hit.Time);
        FixVector2 adjacentContact = start + MultiplyVectorTowardZero(displacement, adjacentTime);
        FixVector2 latticeStep = adjacentContact - contact;
        if (latticeStep == FixVector2.Zero
            || Fix64.Abs(latticeStep.x) > s_Epsilon
            || Fix64.Abs(latticeStep.y) > s_Epsilon)
        {
            return hit;
        }

        if (!IsCircleClear(
                world,
                adjacentContact,
                boundaryRadius,
                runtimeObstacleRadius,
                runtimeObstacles))
        {
            return hit;
        }

        if (!world.HasBoundaryGeometry
            && TryFindTopologyEdgePenetration(
                world,
                adjacentContact,
                Fix64.Max(topologyEdgeRadius, s_Epsilon),
                out _))
        {
            return hit;
        }

        return new SweepHit(adjacentTime, hit.Normal, hit.StableKey);
    }

    internal static LogicStaticCollisionSolveResult SolveCircleAgainstObstacles(
        FixVector2 start,
        FixVector2 desiredDisplacement,
        Fix64 radius,
        IReadOnlyList<LogicStaticCollisionObstacle> obstacles)
    {
        if (obstacles == null)
            throw new ArgumentNullException(nameof(obstacles));
        if (radius < Fix64.Zero)
        {
            return FailureResult(
                LogicStaticCollisionFailure.InvalidRadius,
                start,
                start,
                desiredDisplacement,
                false,
                0);
        }

        FixVector2 position = start;
        bool startedOverlapping = false;
        for (int iteration = 0; iteration < MaxPenetrationIterations; iteration++)
        {
            if (!TryFindRuntimeObstaclePenetration(obstacles, position, radius, 0, out Penetration penetration))
                break;

            startedOverlapping = true;
            position = ProjectOutOfPenetration(position, penetration);
        }

        if (TryFindRuntimeObstaclePenetration(obstacles, position, radius, 0, out _))
        {
            return FailureResult(
                LogicStaticCollisionFailure.StartOverlapUnresolved,
                start,
                position,
                desiredDisplacement,
                startedOverlapping,
                0);
        }

        FixVector2 recoveredStart = position;
        FixVector2 remaining = desiredDisplacement;
        int contactCount = 0;
        ContactManifold contactManifold = default;
        for (int iteration = 0; iteration < MaxContactIterations; iteration++)
        {
            if (remaining == FixVector2.Zero)
                break;

            if (!TryFindEarliestRuntimeObstacleHit(obstacles, position, remaining, radius, 0, out SweepHit hit))
            {
                position += remaining;
                remaining = FixVector2.Zero;
                break;
            }

            Fix64 travelTime = hit.Time;
            FixVector2 traveled = MultiplyVectorTowardZero(remaining, travelTime);
            position += traveled;
            FixVector2 leftover = remaining - traveled;
            contactManifold.Add(hit.Normal);
            leftover = contactManifold.Project(leftover);

            contactCount++;
            if (travelTime == Fix64.Zero && leftover == remaining)
            {
                return FailureResult(
                    LogicStaticCollisionFailure.NoProgress,
                    start,
                    position,
                    desiredDisplacement,
                    startedOverlapping,
                    contactCount);
            }

            remaining = leftover;
        }

        if (remaining != FixVector2.Zero
            && !TryFindEarliestRuntimeObstacleHit(obstacles, position, remaining, radius, 0, out _))
        {
            position += remaining;
            remaining = FixVector2.Zero;
        }

        if (remaining != FixVector2.Zero)
        {
            return FailureResult(
                LogicStaticCollisionFailure.ContactIterationLimit,
                start,
                position,
                desiredDisplacement,
                startedOverlapping,
                contactCount);
        }

        return new LogicStaticCollisionSolveResult(
            true,
            LogicStaticCollisionFailure.None,
            start,
            recoveredStart,
            desiredDisplacement,
            position - start,
            startedOverlapping,
            contactCount);
    }

    public static bool IsCircleClear(LogicStaticCollisionWorld world, FixVector2 center, Fix64 radius)
    {
        return IsCircleClear(
            world,
            center,
            radius,
            Array.Empty<LogicStaticCollisionObstacle>());
    }

    internal static bool IsCircleClear(
        LogicStaticCollisionWorld world,
        FixVector2 center,
        Fix64 radius,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles)
    {
        return IsCircleClear(world, center, radius, radius, runtimeObstacles);
    }

    private static bool IsCircleClear(
        LogicStaticCollisionWorld world,
        FixVector2 center,
        Fix64 radius,
        Fix64 runtimeObstacleRadius,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (runtimeObstacles == null)
            throw new ArgumentNullException(nameof(runtimeObstacles));
        if (radius < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (runtimeObstacleRadius < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(runtimeObstacleRadius));

        if (world.HasBoundaryGeometry)
        {
            if (!world.ContainsPointInBoundaryGeometry(center))
                return false;

            bool hasBoundary = TryFindBoundaryGeometryPenetration(world, center, radius, out Penetration boundary);
            bool hasRuntimeObstacle = TryFindRuntimeObstaclePenetration(
                world,
                runtimeObstacles,
                center,
                radius,
                runtimeObstacleRadius,
                out Penetration runtimeObstacle);
            return (!hasBoundary && !hasRuntimeObstacle)
                   || IsQuantizedBoundaryObstacleContact(
                       world,
                       runtimeObstacles,
                       center,
                       radius,
                       runtimeObstacleRadius,
                       hasBoundary,
                       boundary,
                       hasRuntimeObstacle,
                       runtimeObstacle);
        }

        Fix64 minX = world.Origin.x + radius;
        Fix64 maxX = world.MaxWorldX - radius;
        Fix64 minY = world.Origin.y + radius;
        Fix64 maxY = world.MaxWorldY - radius;
        if (center.x < minX || center.x > maxX || center.y < minY || center.y > maxY)
            return false;

        int centerCellX = world.WorldToGridX(center.x);
        int centerCellY = world.WorldToGridY(center.y);
        if (!world.IsWalkable(centerCellX, centerCellY))
            return false;

        return !TryFindCellPenetration(world, center, radius, out _)
               && !TryFindTopologyEdgePenetration(world, center, Fix64.Max(radius, s_Epsilon), out _)
               && !TryFindRuntimeObstaclePenetration(
                   world,
                   runtimeObstacles,
                   center,
                   radius,
                   runtimeObstacleRadius,
                   out _);
    }

    private static LogicStaticCollisionSolveResult FailureResult(
        LogicStaticCollisionFailure failure,
        FixVector2 start,
        FixVector2 position,
        FixVector2 desiredDisplacement,
        bool startedOverlapping,
        int contactCount,
        int firstHitStableKey = -1,
        FixVector2 firstHitNormal = default,
        LogicStaticCollisionContactTrace contact0 = default,
        LogicStaticCollisionContactTrace contact1 = default,
        LogicStaticCollisionContactTrace contact2 = default,
        LogicStaticCollisionContactTrace contact3 = default)
    {
        return new LogicStaticCollisionSolveResult(
            false,
            failure,
            start,
            position,
            desiredDisplacement,
            position - start,
            startedOverlapping,
            contactCount,
            firstHitStableKey,
            firstHitNormal,
            contact0,
            contact1,
            contact2,
            contact3);
    }

    private static bool RecoverStart(
        LogicStaticCollisionWorld world,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        ref FixVector2 position,
        Fix64 radius,
        Fix64 topologyEdgeRadius,
        Fix64 runtimeObstacleRadius,
        ref bool startedOverlapping)
    {
        for (int iteration = 0; iteration < MaxPenetrationIterations; iteration++)
        {
            if (world.HasBoundaryGeometry)
            {
                if (!world.ContainsPointInBoundaryGeometry(position))
                    return false;

                bool hasBoundary = TryFindBoundaryGeometryPenetration(
                    world,
                    position,
                    radius,
                    out Penetration geometryBoundary);
                bool hasRuntimeObstacle = TryFindRuntimeObstaclePenetration(
                    world,
                    runtimeObstacles,
                    position,
                    radius,
                    runtimeObstacleRadius,
                    out Penetration geometryRuntimeObstacle);
                if (!hasBoundary && !hasRuntimeObstacle)
                    return true;

                if (IsQuantizedBoundaryObstacleContact(
                        world,
                        runtimeObstacles,
                        position,
                        radius,
                        runtimeObstacleRadius,
                        hasBoundary,
                        geometryBoundary,
                        hasRuntimeObstacle,
                        geometryRuntimeObstacle))
                {
                    if (hasBoundary)
                        position = ProjectOutOfPenetration(position, geometryBoundary);
                    return true;
                }

                if (hasBoundary)
                {
                    startedOverlapping = true;
                    position = ProjectOutOfPenetration(position, geometryBoundary);
                    continue;
                }

                if (hasRuntimeObstacle)
                {
                    startedOverlapping = true;
                    position = ProjectOutOfPenetration(position, geometryRuntimeObstacle);
                    continue;
                }

                throw new InvalidOperationException("Static collision geometry recovery reached an inconsistent penetration state.");
            }

            if (TryFindWorldBoundaryPenetration(world, position, radius, out Penetration boundary))
            {
                startedOverlapping = true;
                position = ProjectOutOfPenetration(position, boundary);
                continue;
            }

            int centerCellX = world.WorldToGridX(position.x);
            int centerCellY = world.WorldToGridY(position.y);
            if (!world.IsWalkable(centerCellX, centerCellY))
            {
                startedOverlapping = true;
                if (!TryRecoverBlockedCenter(
                        world,
                        runtimeObstacles,
                        position,
                        centerCellX,
                        centerCellY,
                        radius,
                        runtimeObstacleRadius,
                        out position))
                    return false;
                continue;
            }

            if (TryFindCellPenetration(world, position, radius, out Penetration cell))
            {
                startedOverlapping = true;
                position = ProjectOutOfPenetration(position, cell);
                continue;
            }

            if (TryFindTopologyEdgePenetration(
                    world,
                    position,
                    Fix64.Max(topologyEdgeRadius, s_Epsilon),
                    out Penetration topologyEdge))
            {
                startedOverlapping = true;
                position = ProjectOutOfPenetration(position, topologyEdge);
                continue;
            }

            if (TryFindRuntimeObstaclePenetration(
                    world,
                    runtimeObstacles,
                    position,
                    radius,
                    runtimeObstacleRadius,
                    out Penetration runtimeObstacle))
            {
                startedOverlapping = true;
                position = ProjectOutOfPenetration(position, runtimeObstacle);
                continue;
            }

            return true;
        }

        return IsCircleClear(world, position, radius, runtimeObstacleRadius, runtimeObstacles)
               && !TryFindTopologyEdgePenetration(
                   world,
                   position,
                   Fix64.Max(topologyEdgeRadius, s_Epsilon),
                   out _);
    }

    private static FixVector2 ProjectOutOfPenetration(FixVector2 position, Penetration penetration)
    {
        if (penetration.Depth <= Fix64.Zero)
            throw new InvalidOperationException("Static collision projection requires a positive penetration depth.");
        if (penetration.Normal == FixVector2.Zero)
            throw new InvalidOperationException("Static collision projection requires a non-zero penetration normal.");

        FixVector2 correction = new FixVector2(
            MultiplyProjectionComponent(penetration.Normal.x, penetration.Depth),
            MultiplyProjectionComponent(penetration.Normal.y, penetration.Depth));
        if (correction != FixVector2.Zero)
            return position + correction;

        if (Fix64.Abs(penetration.Normal.x) >= Fix64.Abs(penetration.Normal.y))
        {
            return position + new FixVector2(
                penetration.Normal.x > Fix64.Zero ? s_Epsilon : -s_Epsilon,
                Fix64.Zero);
        }

        return position + new FixVector2(
            Fix64.Zero,
            penetration.Normal.y > Fix64.Zero ? s_Epsilon : -s_Epsilon);
    }

    private static Fix64 MultiplyProjectionComponent(Fix64 normal, Fix64 depth)
    {
        long product = checked(normal.RawValue * depth.RawValue);
        return Fix64.FromRaw(product / (1L << Fix64.FRACTIONAL_PLACES));
    }

    private static bool IsQuantizedBoundaryObstacleContact(
        LogicStaticCollisionWorld world,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        FixVector2 position,
        Fix64 boundaryRadius,
        Fix64 runtimeObstacleRadius)
    {
        return TryGetQuantizedBoundaryObstacleContact(
            world,
            runtimeObstacles,
            position,
            boundaryRadius,
            runtimeObstacleRadius,
            out _);
    }

    private static bool TryGetQuantizedBoundaryObstacleContact(
        LogicStaticCollisionWorld world,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        FixVector2 position,
        Fix64 boundaryRadius,
        Fix64 runtimeObstacleRadius,
        out Penetration contact)
    {
        bool hasBoundary = TryFindBoundaryGeometryPenetration(
            world,
            position,
            boundaryRadius,
            out Penetration boundary);
        bool hasRuntimeObstacle = TryFindRuntimeObstaclePenetration(
            world,
            runtimeObstacles,
            position,
            boundaryRadius,
            runtimeObstacleRadius,
            out Penetration runtimeObstacle);
        bool accepted = IsQuantizedBoundaryObstacleContact(
            world,
            runtimeObstacles,
            position,
            boundaryRadius,
            runtimeObstacleRadius,
            hasBoundary,
            boundary,
            hasRuntimeObstacle,
            runtimeObstacle);
        contact = accepted
            ? hasBoundary ? boundary : runtimeObstacle
            : default;
        return accepted;
    }

    private static bool IsQuantizedBoundaryObstacleContact(
        LogicStaticCollisionWorld world,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        FixVector2 position,
        Fix64 boundaryRadius,
        Fix64 runtimeObstacleRadius,
        bool hasBoundary,
        Penetration boundary,
        bool hasRuntimeObstacle,
        Penetration runtimeObstacle)
    {
        if (hasBoundary == hasRuntimeObstacle)
            return false;

        Penetration first = hasBoundary ? boundary : runtimeObstacle;
        if (first.Depth <= Fix64.Zero || first.Depth > s_Epsilon)
            return false;

        FixVector2 adjacent = ProjectOutOfPenetration(position, first);
        if (adjacent == position)
            return false;

        bool adjacentHasBoundary = TryFindBoundaryGeometryPenetration(
            world,
            adjacent,
            boundaryRadius,
            out Penetration adjacentBoundary);
        bool adjacentHasRuntimeObstacle = TryFindRuntimeObstaclePenetration(
            world,
            runtimeObstacles,
            adjacent,
            boundaryRadius,
            runtimeObstacleRadius,
            out Penetration adjacentRuntimeObstacle);
        if (adjacentHasBoundary == adjacentHasRuntimeObstacle
            || adjacentHasBoundary == hasBoundary)
        {
            return false;
        }

        Penetration second = adjacentHasBoundary ? adjacentBoundary : adjacentRuntimeObstacle;
        return second.Depth > Fix64.Zero
               && second.Depth <= s_Epsilon
               && ProjectOutOfPenetration(adjacent, second) == position;
    }

    private static bool TryRecoverBlockedCenter(
        LogicStaticCollisionWorld world,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        FixVector2 position,
        int centerCellX,
        int centerCellY,
        Fix64 radius,
        Fix64 runtimeObstacleRadius,
        out FixVector2 recoveredPosition)
    {
        bool found = false;
        recoveredPosition = position;
        Fix64 bestDistanceSquared = Fix64.FromRaw(long.MaxValue);
        int bestStableKey = int.MaxValue;

        TrySelectBlockedCenterRecovery(
            world, runtimeObstacles, position, centerCellX, centerCellY, -1, 0, radius, runtimeObstacleRadius, 0,
            ref found, ref recoveredPosition, ref bestDistanceSquared, ref bestStableKey);
        TrySelectBlockedCenterRecovery(
            world, runtimeObstacles, position, centerCellX, centerCellY, 1, 0, radius, runtimeObstacleRadius, 1,
            ref found, ref recoveredPosition, ref bestDistanceSquared, ref bestStableKey);
        TrySelectBlockedCenterRecovery(
            world, runtimeObstacles, position, centerCellX, centerCellY, 0, -1, radius, runtimeObstacleRadius, 2,
            ref found, ref recoveredPosition, ref bestDistanceSquared, ref bestStableKey);
        TrySelectBlockedCenterRecovery(
            world, runtimeObstacles, position, centerCellX, centerCellY, 0, 1, radius, runtimeObstacleRadius, 3,
            ref found, ref recoveredPosition, ref bestDistanceSquared, ref bestStableKey);
        return found;
    }

    private static void TrySelectBlockedCenterRecovery(
        LogicStaticCollisionWorld world,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        FixVector2 position,
        int centerCellX,
        int centerCellY,
        int directionX,
        int directionY,
        Fix64 radius,
        Fix64 runtimeObstacleRadius,
        int stableKey,
        ref bool found,
        ref FixVector2 bestPosition,
        ref Fix64 bestDistanceSquared,
        ref int bestStableKey)
    {
        int maxSteps = directionX != 0 ? world.Width : world.Height;
        for (int step = 1; step <= maxSteps; step++)
        {
            int cellX = centerCellX + directionX * step;
            int cellY = centerCellY + directionY * step;
            if (cellX < 0 || cellX >= world.Width || cellY < 0 || cellY >= world.Height)
                return;
            if (!world.IsWalkable(cellX, cellY))
                continue;

            FixVector2 candidate = position;
            if (directionX < 0)
                candidate.x = world.GetCellMinX(cellX + 1) - radius - s_Epsilon;
            else if (directionX > 0)
                candidate.x = world.GetCellMinX(cellX) + radius + s_Epsilon;
            else if (directionY < 0)
                candidate.y = world.GetCellMinY(cellY + 1) - radius - s_Epsilon;
            else
                candidate.y = world.GetCellMinY(cellY) + radius + s_Epsilon;

            if (!IsCircleClear(world, candidate, radius, runtimeObstacleRadius, runtimeObstacles))
                continue;

            Fix64 distanceSquared = FixVector2.SqrMagnitude(candidate - position);
            if (!found
                || distanceSquared < bestDistanceSquared
                || (distanceSquared == bestDistanceSquared && stableKey < bestStableKey))
            {
                found = true;
                bestPosition = candidate;
                bestDistanceSquared = distanceSquared;
                bestStableKey = stableKey;
            }
            return;
        }
    }

    private static bool TryFindWorldBoundaryPenetration(
        LogicStaticCollisionWorld world,
        FixVector2 position,
        Fix64 radius,
        out Penetration penetration)
    {
        Fix64 minX = world.Origin.x + radius;
        Fix64 maxX = world.MaxWorldX - radius;
        Fix64 minY = world.Origin.y + radius;
        Fix64 maxY = world.MaxWorldY - radius;

        bool found = false;
        penetration = default;
        if (position.x < minX)
            SelectPenetration(new Penetration(minX - position.x, new FixVector2(1, 0), 0), ref found, ref penetration);
        if (position.x > maxX)
            SelectPenetration(new Penetration(position.x - maxX, new FixVector2(-1, 0), 1), ref found, ref penetration);
        if (position.y < minY)
            SelectPenetration(new Penetration(minY - position.y, new FixVector2(0, 1), 2), ref found, ref penetration);
        if (position.y > maxY)
            SelectPenetration(new Penetration(position.y - maxY, new FixVector2(0, -1), 3), ref found, ref penetration);
        return found;
    }

    private static bool TryFindCellPenetration(
        LogicStaticCollisionWorld world,
        FixVector2 position,
        Fix64 radius,
        out Penetration penetration)
    {
        GetCellRange(world, position, position, radius, out int minCellX, out int maxCellX, out int minCellY, out int maxCellY);
        bool found = false;
        penetration = default;
        for (int y = minCellY; y <= maxCellY; y++)
        {
            for (int x = minCellX; x <= maxCellX; x++)
            {
                if (world.IsWalkable(x, y))
                    continue;

                Fix64 minX = world.GetCellMinX(x) - radius;
                Fix64 maxX = world.GetCellMinX(x + 1) + radius;
                Fix64 minY = world.GetCellMinY(y) - radius;
                Fix64 maxY = world.GetCellMinY(y + 1) + radius;
                if (position.x <= minX || position.x >= maxX || position.y <= minY || position.y >= maxY)
                    continue;

                int cellKey = checked(4 + world.GetIndex(x, y) * 4);
                if (world.IsWalkable(x - 1, y))
                    SelectPenetration(new Penetration(position.x - minX, new FixVector2(-1, 0), cellKey), ref found, ref penetration);
                if (world.IsWalkable(x + 1, y))
                    SelectPenetration(new Penetration(maxX - position.x, new FixVector2(1, 0), cellKey + 1), ref found, ref penetration);
                if (world.IsWalkable(x, y - 1))
                    SelectPenetration(new Penetration(position.y - minY, new FixVector2(0, -1), cellKey + 2), ref found, ref penetration);
                if (world.IsWalkable(x, y + 1))
                    SelectPenetration(new Penetration(maxY - position.y, new FixVector2(0, 1), cellKey + 3), ref found, ref penetration);
            }
        }

        return found;
    }

    private static bool TryFindRuntimeObstaclePenetration(
        LogicStaticCollisionWorld world,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        FixVector2 position,
        Fix64 boundaryRadius,
        Fix64 runtimeObstacleRadius,
        out Penetration penetration)
    {
        int stableKeyBase = GetRuntimeObstacleStableKeyBase(world);
        if (!TryFindRuntimeObstaclePenetration(
                runtimeObstacles,
                position,
                runtimeObstacleRadius,
                stableKeyBase,
                -1,
                out penetration))
        {
            return false;
        }

        if (!TryResolveOneRawBoundaryPinchObstacle(
                world,
                runtimeObstacles,
                penetration.StableKey,
                boundaryRadius,
                runtimeObstacleRadius,
                out int relaxedObstacleKey))
        {
            return true;
        }

        return TryFindRuntimeObstaclePenetration(
            runtimeObstacles,
            position,
            runtimeObstacleRadius,
            stableKeyBase,
            relaxedObstacleKey,
            out penetration);
    }

    private static bool TryFindBoundaryGeometryPenetration(
        LogicStaticCollisionWorld world,
        FixVector2 position,
        Fix64 radius,
        out Penetration penetration)
    {
        bool found = false;
        penetration = default;
        GetCellRange(world, position, position, radius, out int minCellX, out int maxCellX, out int minCellY, out int maxCellY);
        for (int y = minCellY; y <= maxCellY; y++)
        {
            for (int x = minCellX; x <= maxCellX; x++)
            {
                int[] segmentIndices = world.GetBoundarySegmentIndicesForCell(x, y);
                for (int i = 0; i < segmentIndices.Length; i++)
                {
                    int segmentIndex = segmentIndices[i];
                    world.GetBoundarySegment(segmentIndex, out FixVector2 start, out FixVector2 end);
                    FixVector2 edge = end - start;
                    Fix64 lengthSquared = FixVector2.SqrMagnitude(edge);
                    if (lengthSquared <= Fix64.Zero)
                    {
                        SelectBoundaryPointPenetration(
                            position,
                            radius,
                            start,
                            checked(4 + segmentIndex),
                            world,
                            ref found,
                            ref penetration);
                        continue;
                    }
                    FixVector2 closest;
                    if (edge.y == Fix64.Zero)
                    {
                        closest = new FixVector2(
                            Fix64.Max(Fix64.Min(start.x, end.x), Fix64.Min(Fix64.Max(start.x, end.x), position.x)),
                            start.y);
                    }
                    else if (edge.x == Fix64.Zero)
                    {
                        closest = new FixVector2(
                            start.x,
                            Fix64.Max(Fix64.Min(start.y, end.y), Fix64.Min(Fix64.Max(start.y, end.y), position.y)));
                    }
                    else
                    {
                        Fix64 projection = FixVector2.Dot(position - start, edge) / lengthSquared;
                        projection = Fix64.Max(Fix64.Zero, Fix64.Min(Fix64.One, projection));
                        closest = start + edge * projection;
                    }
                    FixVector2 delta = position - closest;
                    Fix64 distance = FixVector2.Magnitude(delta);
                    if (!IsRawDistanceLessThanRadius(delta, radius))
                        continue;
                    FixVector2 normal;
                    if (distance > Fix64.Zero)
                    {
                        normal = delta / distance;
                    }
                    else
                    {
                        FixVector2 perpendicular = NormalizeContactVector(new FixVector2(-edge.y, edge.x));
                        normal = world.ContainsPointInBoundaryGeometry(position + perpendicular * s_Epsilon)
                            ? perpendicular
                            : -perpendicular;
                    }
                    SelectPenetration(
                        new Penetration(radius - distance, normal, checked(4 + segmentIndex)),
                        ref found,
                        ref penetration);
                }
            }
        }
        return found;
    }

    private static void SelectBoundaryPointPenetration(
        FixVector2 position,
        Fix64 radius,
        FixVector2 boundaryPoint,
        int stableKey,
        LogicStaticCollisionWorld world,
        ref bool found,
        ref Penetration penetration)
    {
        FixVector2 delta = position - boundaryPoint;
        Fix64 distance = FixVector2.Magnitude(delta);
        if (!IsRawDistanceLessThanRadius(delta, radius))
            return;
        FixVector2 normal;
        if (distance > Fix64.Zero)
        {
            normal = delta / distance;
        }
        else
        {
            if (world.ContainsPointInBoundaryGeometry(position + new FixVector2(s_Epsilon, Fix64.Zero)))
                normal = new FixVector2(1, 0);
            else if (world.ContainsPointInBoundaryGeometry(position - new FixVector2(s_Epsilon, Fix64.Zero)))
                normal = new FixVector2(-1, 0);
            else if (world.ContainsPointInBoundaryGeometry(position + new FixVector2(Fix64.Zero, s_Epsilon)))
                normal = new FixVector2(0, 1);
            else if (world.ContainsPointInBoundaryGeometry(position - new FixVector2(Fix64.Zero, s_Epsilon)))
                normal = new FixVector2(0, -1);
            else
                throw new InvalidOperationException($"Static collision boundary point has no interior normal. key={stableKey}.");
        }
        SelectPenetration(new Penetration(radius - distance, normal, stableKey), ref found, ref penetration);
    }

    private static bool TryFindTopologyEdgePenetration(
        LogicStaticCollisionWorld world,
        FixVector2 position,
        Fix64 radius,
        out Penetration penetration)
    {
        bool found = false;
        penetration = default;
        GetCellRange(world, position, position, radius, out int minCellX, out int maxCellX, out int minCellY, out int maxCellY);
        for (int y = minCellY; y <= maxCellY; y++)
        {
            for (int x = minCellX; x <= maxCellX; x++)
            {
                if (!world.IsWalkable(x, y))
                    continue;

                if (x + 1 < world.Width
                    && world.IsWalkable(x + 1, y)
                    && !world.CanTraverseCardinal(x, y, x + 1, y))
                {
                    Fix64 edgeX = world.GetCellMinX(x + 1);
                    Fix64 distance = Fix64.Abs(position.x - edgeX);
                    if (distance < radius
                        && position.y > world.GetCellMinY(y) - radius
                        && position.y < world.GetCellMinY(y + 1) + radius)
                    {
                        FixVector2 normal = position.x <= edgeX
                            ? new FixVector2(-1, 0)
                            : new FixVector2(1, 0);
                        int stableKey = checked(GetTopologyEdgeStableKeyBase(world) + world.GetIndex(x, y) * 4 + 1);
                        SelectPenetration(new Penetration(radius - distance, normal, stableKey), ref found, ref penetration);
                    }
                }

                if (y + 1 < world.Height
                    && world.IsWalkable(x, y + 1)
                    && !world.CanTraverseCardinal(x, y, x, y + 1))
                {
                    Fix64 edgeY = world.GetCellMinY(y + 1);
                    Fix64 distance = Fix64.Abs(position.y - edgeY);
                    if (distance < radius
                        && position.x > world.GetCellMinX(x) - radius
                        && position.x < world.GetCellMinX(x + 1) + radius)
                    {
                        FixVector2 normal = position.y <= edgeY
                            ? new FixVector2(0, -1)
                            : new FixVector2(0, 1);
                        int stableKey = checked(GetTopologyEdgeStableKeyBase(world) + world.GetIndex(x, y) * 4 + 3);
                        SelectPenetration(new Penetration(radius - distance, normal, stableKey), ref found, ref penetration);
                    }
                }
            }
        }

        return found;
    }

    private static bool TryFindRuntimeObstaclePenetration(
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        FixVector2 position,
        Fix64 radius,
        int stableKeyBase,
        out Penetration penetration)
    {
        return TryFindRuntimeObstaclePenetration(
            runtimeObstacles,
            position,
            radius,
            stableKeyBase,
            -1,
            out penetration);
    }

    private static bool TryFindRuntimeObstaclePenetration(
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        FixVector2 position,
        Fix64 radius,
        int stableKeyBase,
        int relaxedObstacleKey,
        out Penetration penetration)
    {
        bool found = false;
        penetration = default;
        for (int i = 0; i < runtimeObstacles.Count; i++)
        {
            LogicStaticCollisionObstacle obstacle = runtimeObstacles[i];
            int obstacleKey = checked(stableKeyBase + i * 8);
            Fix64 obstacleRadius = obstacleKey == relaxedObstacleKey
                ? Fix64.Max(Fix64.Zero, radius - s_Epsilon)
                : radius;
            switch (obstacle.Kind)
            {
                case LogicStaticCollisionObstacleKind.Box:
                    Fix64 minX = obstacle.Center.x - obstacle.HalfExtents.x;
                    Fix64 maxX = obstacle.Center.x + obstacle.HalfExtents.x;
                    Fix64 minY = obstacle.Center.y - obstacle.HalfExtents.y;
                    Fix64 maxY = obstacle.Center.y + obstacle.HalfExtents.y;
                    bool strictlyInside = position.x > minX && position.x < maxX
                                          && position.y > minY && position.y < maxY;
                    bool insideOrOn = position.x >= minX && position.x <= maxX
                                      && position.y >= minY && position.y <= maxY;
                    if (strictlyInside || (obstacleRadius > Fix64.Zero && insideOrOn))
                    {
                        SelectPenetration(
                            new Penetration(position.x - minX + obstacleRadius, new FixVector2(-1, 0), obstacleKey),
                            ref found,
                            ref penetration);
                        SelectPenetration(
                            new Penetration(maxX - position.x + obstacleRadius, new FixVector2(1, 0), obstacleKey + 1),
                            ref found,
                            ref penetration);
                        SelectPenetration(
                            new Penetration(position.y - minY + obstacleRadius, new FixVector2(0, -1), obstacleKey + 2),
                            ref found,
                            ref penetration);
                        SelectPenetration(
                            new Penetration(maxY - position.y + obstacleRadius, new FixVector2(0, 1), obstacleKey + 3),
                            ref found,
                            ref penetration);
                        break;
                    }

                    FixVector2 closest = new FixVector2(
                        Fix64.Max(minX, Fix64.Min(maxX, position.x)),
                        Fix64.Max(minY, Fix64.Min(maxY, position.y)));
                    FixVector2 boxDelta = position - closest;
                    Fix64 boxDistance = FixVector2.Magnitude(boxDelta);
                    if (!IsRawDistanceLessThanRadius(boxDelta, obstacleRadius))
                        break;
                    if (boxDistance <= Fix64.Zero)
                        throw new InvalidOperationException($"Rounded box penetration produced no normal. obstacle={obstacle.StableId}.");
                    SelectPenetration(
                        new Penetration(
                            obstacleRadius - boxDistance,
                            boxDelta / boxDistance,
                            ResolveRoundedBoxFeatureKey(position, minX, maxX, minY, maxY, obstacleKey)),
                        ref found,
                        ref penetration);
                    break;
                case LogicStaticCollisionObstacleKind.Circle:
                    Fix64 expandedRadius = obstacle.Radius + obstacleRadius;
                    FixVector2 delta = position - obstacle.Center;
                    Fix64 distance = FixVector2.Magnitude(delta);
                    if (!IsRawDistanceLessThanRadius(delta, expandedRadius))
                        break;
                    FixVector2 normal = distance > Fix64.Zero
                        ? delta / distance
                        : new FixVector2(-1, 0);
                    SelectPenetration(
                        new Penetration(expandedRadius - distance, normal, obstacleKey + 4),
                        ref found,
                        ref penetration);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(obstacle.Kind),
                        obstacle.Kind,
                        "Unknown runtime collision obstacle kind.");
            }
        }

        return found;
    }

    private static void SelectPenetration(Penetration candidate, ref bool found, ref Penetration best)
    {
        if (!found
            || candidate.Depth < best.Depth
            || (candidate.Depth == best.Depth && candidate.StableKey < best.StableKey))
        {
            found = true;
            best = candidate;
        }
    }

    private static bool TryFindEarliestHit(
        LogicStaticCollisionWorld world,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        FixVector2 start,
        FixVector2 displacement,
        Fix64 radius,
        Fix64 topologyEdgeRadius,
        Fix64 runtimeObstacleRadius,
        out SweepHit hit)
    {
        int runtimeKeyBase = GetRuntimeObstacleStableKeyBase(world);
        if (TryFindRuntimeObstaclePenetration(
                runtimeObstacles,
                start,
                runtimeObstacleRadius,
                runtimeKeyBase,
                -1,
                out Penetration startRuntimePenetration)
            && TryResolveOneRawBoundaryPinchObstacle(
                world,
                runtimeObstacles,
                startRuntimePenetration.StableKey,
                radius,
                runtimeObstacleRadius,
                out int startRelaxedObstacleKey))
        {
            return TryFindEarliestHitCore(
                world,
                runtimeObstacles,
                start,
                displacement,
                radius,
                topologyEdgeRadius,
                runtimeObstacleRadius,
                startRelaxedObstacleKey,
                out hit);
        }

        if (!TryFindEarliestHitCore(
                world,
                runtimeObstacles,
                start,
                displacement,
                radius,
                topologyEdgeRadius,
                runtimeObstacleRadius,
                -1,
                out hit))
        {
            return false;
        }

        SweepHit strictHit = hit;
        if (!TryResolveOneRawBoundaryPinchObstacle(
                world,
                runtimeObstacles,
                strictHit.StableKey,
                radius,
                runtimeObstacleRadius,
                out int relaxedObstacleKey))
        {
            return true;
        }

        bool hasRelaxedHit = TryFindEarliestHitCore(
            world,
            runtimeObstacles,
            start,
            displacement,
            radius,
            topologyEdgeRadius,
            runtimeObstacleRadius,
            relaxedObstacleKey,
            out SweepHit relaxedHit);
        hit = relaxedHit;
        return hasRelaxedHit;
    }

    private static bool TryResolveOneRawBoundaryPinchObstacle(
        LogicStaticCollisionWorld world,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        int stableKey,
        Fix64 boundaryRadius,
        Fix64 runtimeObstacleRadius,
        out int obstacleKey)
    {
        int runtimeKeyBase = GetRuntimeObstacleStableKeyBase(world);
        int relativeKey = stableKey - runtimeKeyBase;
        if (!world.HasBoundaryGeometry || relativeKey < 0)
        {
            obstacleKey = -1;
            return false;
        }

        int obstacleIndex = relativeKey / 8;
        if (obstacleIndex < 0 || obstacleIndex >= runtimeObstacles.Count)
            throw new InvalidOperationException(
                $"Static collision key {stableKey} resolved invalid runtime obstacle index " +
                $"{obstacleIndex}/{runtimeObstacles.Count}.");

        obstacleKey = checked(runtimeKeyBase + obstacleIndex * 8);
        Fix64 targetSeparation = boundaryRadius + runtimeObstacleRadius - s_Epsilon;
        if (targetSeparation < Fix64.Zero)
            return false;

        LogicStaticCollisionObstacle obstacle = runtimeObstacles[obstacleIndex];
        for (int segmentIndex = 0; segmentIndex < world.BoundarySegmentCount; segmentIndex++)
        {
            world.GetBoundarySegment(segmentIndex, out FixVector2 segmentStart, out FixVector2 segmentEnd);
            Fix64 separation = MeasureBoundaryObstacleSeparation(segmentStart, segmentEnd, obstacle);
            if (separation == targetSeparation)
                return true;
        }

        return false;
    }

    private static Fix64 MeasureBoundaryObstacleSeparation(
        FixVector2 segmentStart,
        FixVector2 segmentEnd,
        LogicStaticCollisionObstacle obstacle)
    {
        switch (obstacle.Kind)
        {
            case LogicStaticCollisionObstacleKind.Box:
                return MeasureSegmentBoxSeparation(
                    segmentStart,
                    segmentEnd,
                    obstacle.Center - obstacle.HalfExtents,
                    obstacle.Center + obstacle.HalfExtents);
            case LogicStaticCollisionObstacleKind.Circle:
                return Fix64.Max(
                    Fix64.Zero,
                    MeasurePointSegmentDistance(obstacle.Center, segmentStart, segmentEnd) - obstacle.Radius);
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(obstacle.Kind),
                    obstacle.Kind,
                    "Unknown runtime collision obstacle kind.");
        }
    }

    private static Fix64 MeasureSegmentBoxSeparation(
        FixVector2 segmentStart,
        FixVector2 segmentEnd,
        FixVector2 boxMin,
        FixVector2 boxMax)
    {
        if (segmentStart.y == segmentEnd.y
            && RangesOverlap(segmentStart.x, segmentEnd.x, boxMin.x, boxMax.x))
        {
            return AxisRangeSeparation(segmentStart.y, segmentStart.y, boxMin.y, boxMax.y);
        }
        if (segmentStart.x == segmentEnd.x
            && RangesOverlap(segmentStart.y, segmentEnd.y, boxMin.y, boxMax.y))
        {
            return AxisRangeSeparation(segmentStart.x, segmentStart.x, boxMin.x, boxMax.x);
        }

        if (IsPointInsideBox(segmentStart, boxMin, boxMax)
            || IsPointInsideBox(segmentEnd, boxMin, boxMax)
            || SegmentIntersectsBox(segmentStart, segmentEnd, boxMin, boxMax))
        {
            return Fix64.Zero;
        }

        Fix64 best = MeasurePointBoxDistance(segmentStart, boxMin, boxMax);
        best = Fix64.Min(best, MeasurePointBoxDistance(segmentEnd, boxMin, boxMax));
        best = Fix64.Min(best, MeasurePointSegmentDistance(boxMin, segmentStart, segmentEnd));
        best = Fix64.Min(best, MeasurePointSegmentDistance(
            new FixVector2(boxMax.x, boxMin.y), segmentStart, segmentEnd));
        best = Fix64.Min(best, MeasurePointSegmentDistance(boxMax, segmentStart, segmentEnd));
        return Fix64.Min(best, MeasurePointSegmentDistance(
            new FixVector2(boxMin.x, boxMax.y), segmentStart, segmentEnd));
    }

    private static bool RangesOverlap(Fix64 firstStart, Fix64 firstEnd, Fix64 secondStart, Fix64 secondEnd)
    {
        Fix64 firstMin = Fix64.Min(firstStart, firstEnd);
        Fix64 firstMax = Fix64.Max(firstStart, firstEnd);
        return firstMax >= secondStart && firstMin <= secondEnd;
    }

    private static Fix64 AxisRangeSeparation(
        Fix64 firstStart,
        Fix64 firstEnd,
        Fix64 secondStart,
        Fix64 secondEnd)
    {
        Fix64 firstMin = Fix64.Min(firstStart, firstEnd);
        Fix64 firstMax = Fix64.Max(firstStart, firstEnd);
        if (firstMax < secondStart)
            return secondStart - firstMax;
        if (firstMin > secondEnd)
            return firstMin - secondEnd;
        return Fix64.Zero;
    }

    private static bool IsPointInsideBox(FixVector2 point, FixVector2 boxMin, FixVector2 boxMax)
    {
        return point.x >= boxMin.x && point.x <= boxMax.x
               && point.y >= boxMin.y && point.y <= boxMax.y;
    }

    private static bool SegmentIntersectsBox(
        FixVector2 segmentStart,
        FixVector2 segmentEnd,
        FixVector2 boxMin,
        FixVector2 boxMax)
    {
        FixVector2 displacement = segmentEnd - segmentStart;
        return TrySweepPointAabb(
            segmentStart,
            displacement,
            boxMin.x,
            boxMax.x,
            boxMin.y,
            boxMax.y,
            0,
            out _);
    }

    private static Fix64 MeasurePointBoxDistance(
        FixVector2 point,
        FixVector2 boxMin,
        FixVector2 boxMax)
    {
        FixVector2 closest = new FixVector2(
            Fix64.Max(boxMin.x, Fix64.Min(boxMax.x, point.x)),
            Fix64.Max(boxMin.y, Fix64.Min(boxMax.y, point.y)));
        return FixVector2.Magnitude(point - closest);
    }

    private static Fix64 MeasurePointSegmentDistance(
        FixVector2 point,
        FixVector2 segmentStart,
        FixVector2 segmentEnd)
    {
        FixVector2 edge = segmentEnd - segmentStart;
        Fix64 lengthSquared = FixVector2.SqrMagnitude(edge);
        if (lengthSquared <= Fix64.Zero)
            return FixVector2.Magnitude(point - segmentStart);

        Fix64 projection = FixVector2.Dot(point - segmentStart, edge) / lengthSquared;
        projection = Fix64.Max(Fix64.Zero, Fix64.Min(Fix64.One, projection));
        return FixVector2.Magnitude(point - (segmentStart + edge * projection));
    }

    private static bool TryFindEarliestHitCore(
        LogicStaticCollisionWorld world,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        FixVector2 start,
        FixVector2 displacement,
        Fix64 radius,
        Fix64 topologyEdgeRadius,
        Fix64 runtimeObstacleRadius,
        int relaxedStableKey,
        out SweepHit hit)
    {
        bool found = false;
        hit = default;
        if (world.HasBoundaryGeometry)
        {
            TrySelectBoundaryGeometryHit(
                world,
                start,
                displacement,
                radius,
                relaxedStableKey,
                ref found,
                ref hit);
            if (TryFindEarliestRuntimeObstacleHit(
                    runtimeObstacles,
                    start,
                    displacement,
                    runtimeObstacleRadius,
                    GetRuntimeObstacleStableKeyBase(world),
                    relaxedStableKey,
                    out SweepHit geometryRuntimeHit))
            {
                SelectHit(geometryRuntimeHit, ref found, ref hit);
            }
            return found;
        }

        TrySelectBoundaryHit(world, start, displacement, radius, ref found, ref hit);

        FixVector2 end = start + displacement;
        GetCellRange(world, start, end, radius, out int minCellX, out int maxCellX, out int minCellY, out int maxCellY);
        for (int y = minCellY; y <= maxCellY; y++)
        {
            for (int x = minCellX; x <= maxCellX; x++)
            {
                if (world.IsWalkable(x, y))
                    continue;

                Fix64 minX = world.GetCellMinX(x) - radius;
                Fix64 maxX = world.GetCellMinX(x + 1) + radius;
                Fix64 minY = world.GetCellMinY(y) - radius;
                Fix64 maxY = world.GetCellMinY(y + 1) + radius;
                int cellKey = checked(4 + world.GetIndex(x, y) * 4);
                if (!TrySweepPointAabb(start, displacement, minX, maxX, minY, maxY, cellKey, out SweepHit candidate))
                    continue;
                if (!IsExposedFace(world, x, y, candidate.Normal))
                    continue;
                if (RawDot(displacement, candidate.Normal) >= 0)
                    continue;
                SelectHit(candidate, ref found, ref hit);
            }
        }

        GetCellRange(
            world,
            start,
            end,
            topologyEdgeRadius,
            out int topologyMinCellX,
            out int topologyMaxCellX,
            out int topologyMinCellY,
            out int topologyMaxCellY);
        TrySelectTopologyEdgeHit(
            world,
            start,
            displacement,
            Fix64.Max(topologyEdgeRadius, s_Epsilon),
            topologyMinCellX,
            topologyMaxCellX,
            topologyMinCellY,
            topologyMaxCellY,
            ref found,
            ref hit);

        if (TryFindEarliestRuntimeObstacleHit(
                runtimeObstacles,
                start,
                displacement,
                runtimeObstacleRadius,
                GetRuntimeObstacleStableKeyBase(world),
                relaxedStableKey,
                out SweepHit runtimeHit))
        {
            SelectHit(runtimeHit, ref found, ref hit);
        }

        return found;
    }

    private static void TrySelectTopologyEdgeHit(
        LogicStaticCollisionWorld world,
        FixVector2 start,
        FixVector2 displacement,
        Fix64 radius,
        int minCellX,
        int maxCellX,
        int minCellY,
        int maxCellY,
        ref bool found,
        ref SweepHit hit)
    {
        for (int y = minCellY; y <= maxCellY; y++)
        {
            for (int x = minCellX; x <= maxCellX; x++)
            {
                if (!world.IsWalkable(x, y))
                    continue;

                if (x + 1 < world.Width
                    && world.IsWalkable(x + 1, y)
                    && !world.CanTraverseCardinal(x, y, x + 1, y)
                    && TrySweepVerticalTopologyEdge(
                        world,
                        start,
                        displacement,
                        radius,
                        x,
                        y,
                        checked(GetTopologyEdgeStableKeyBase(world) + world.GetIndex(x, y) * 4 + 1),
                        out SweepHit verticalHit))
                {
                    SelectHit(verticalHit, ref found, ref hit);
                }

                if (y + 1 < world.Height
                    && world.IsWalkable(x, y + 1)
                    && !world.CanTraverseCardinal(x, y, x, y + 1)
                    && TrySweepHorizontalTopologyEdge(
                        world,
                        start,
                        displacement,
                        radius,
                        x,
                        y,
                        checked(GetTopologyEdgeStableKeyBase(world) + world.GetIndex(x, y) * 4 + 3),
                        out SweepHit horizontalHit))
                {
                    SelectHit(horizontalHit, ref found, ref hit);
                }
            }
        }
    }

    private static bool TrySweepVerticalTopologyEdge(
        LogicStaticCollisionWorld world,
        FixVector2 start,
        FixVector2 displacement,
        Fix64 radius,
        int leftCellX,
        int cellY,
        int stableKey,
        out SweepHit hit)
    {
        Fix64 contactX;
        FixVector2 normal;
        if (displacement.x > Fix64.Zero)
        {
            contactX = world.GetCellMinX(leftCellX + 1) - radius;
            normal = new FixVector2(-1, 0);
            if (start.x > contactX || start.x + displacement.x <= contactX)
            {
                hit = default;
                return false;
            }
        }
        else if (displacement.x < Fix64.Zero)
        {
            contactX = world.GetCellMinX(leftCellX + 1) + radius;
            normal = new FixVector2(1, 0);
            if (start.x < contactX || start.x + displacement.x >= contactX)
            {
                hit = default;
                return false;
            }
        }
        else
        {
            hit = default;
            return false;
        }

        Fix64 time = (contactX - start.x) / displacement.x;
        Fix64 contactY = start.y + displacement.y * time;
        if (contactY < world.GetCellMinY(cellY) - radius
            || contactY > world.GetCellMinY(cellY + 1) + radius)
        {
            hit = default;
            return false;
        }

        hit = new SweepHit(time, normal, stableKey);
        return true;
    }

    private static bool TrySweepHorizontalTopologyEdge(
        LogicStaticCollisionWorld world,
        FixVector2 start,
        FixVector2 displacement,
        Fix64 radius,
        int cellX,
        int lowerCellY,
        int stableKey,
        out SweepHit hit)
    {
        Fix64 contactY;
        FixVector2 normal;
        if (displacement.y > Fix64.Zero)
        {
            contactY = world.GetCellMinY(lowerCellY + 1) - radius;
            normal = new FixVector2(0, -1);
            if (start.y > contactY || start.y + displacement.y <= contactY)
            {
                hit = default;
                return false;
            }
        }
        else if (displacement.y < Fix64.Zero)
        {
            contactY = world.GetCellMinY(lowerCellY + 1) + radius;
            normal = new FixVector2(0, 1);
            if (start.y < contactY || start.y + displacement.y >= contactY)
            {
                hit = default;
                return false;
            }
        }
        else
        {
            hit = default;
            return false;
        }

        Fix64 time = (contactY - start.y) / displacement.y;
        Fix64 contactX = start.x + displacement.x * time;
        if (contactX < world.GetCellMinX(cellX) - radius
            || contactX > world.GetCellMinX(cellX + 1) + radius)
        {
            hit = default;
            return false;
        }

        hit = new SweepHit(time, normal, stableKey);
        return true;
    }

    private static void TrySelectBoundaryGeometryHit(
        LogicStaticCollisionWorld world,
        FixVector2 start,
        FixVector2 displacement,
        Fix64 radius,
        int relaxedStableKey,
        ref bool found,
        ref SweepHit hit)
    {
        FixVector2 end = start + displacement;
        GetCellRange(world, start, end, radius, out int minCellX, out int maxCellX, out int minCellY, out int maxCellY);
        for (int y = minCellY; y <= maxCellY; y++)
        {
            for (int x = minCellX; x <= maxCellX; x++)
            {
                int[] segmentIndices = world.GetBoundarySegmentIndicesForCell(x, y);
                for (int i = 0; i < segmentIndices.Length; i++)
                {
                    int segmentIndex = segmentIndices[i];
                    int stableKey = checked(4 + segmentIndex);
                    Fix64 segmentRadius = stableKey == relaxedStableKey
                        ? Fix64.Max(Fix64.Zero, radius - s_Epsilon)
                        : radius;
                    world.GetBoundarySegment(segmentIndex, out FixVector2 segmentStart, out FixVector2 segmentEnd);
                    if (TrySweepPointSegmentCapsule(
                            start,
                            displacement,
                            segmentStart,
                            segmentEnd,
                            segmentRadius,
                            stableKey,
                            out SweepHit candidate))
                    {
                        SelectHit(candidate, ref found, ref hit);
                    }
                }
            }
        }
    }

    private static bool TrySweepPointSegmentCapsule(
        FixVector2 start,
        FixVector2 displacement,
        FixVector2 segmentStart,
        FixVector2 segmentEnd,
        Fix64 radius,
        int stableKey,
        out SweepHit hit)
    {
        bool found = false;
        hit = default;
        FixVector2 edge = segmentEnd - segmentStart;
        Fix64 edgeLengthSquared = FixVector2.SqrMagnitude(edge);
        if (edgeLengthSquared <= Fix64.Zero)
        {
            if (!TrySweepPointCircle(start, displacement, segmentStart, radius, stableKey, out hit)
                || RawDot(displacement, hit.Normal) >= 0)
            {
                hit = default;
                return false;
            }
            return true;
        }

        if (TrySweepPointCircle(start, displacement, segmentStart, radius, stableKey, out SweepHit startCap)
            && RawDot(displacement, startCap.Normal) < 0)
        {
            SelectHit(startCap, ref found, ref hit);
        }
        if (TrySweepPointCircle(start, displacement, segmentEnd, radius, stableKey, out SweepHit endCap)
            && RawDot(displacement, endCap.Normal) < 0)
        {
            SelectHit(endCap, ref found, ref hit);
        }

        FixVector2 normal = NormalizeContactVector(new FixVector2(-edge.y, edge.x));
        Fix64 signedStart = FixVector2.Dot(start - segmentStart, normal);
        Fix64 signedVelocity = FixVector2.Dot(displacement, normal);
        if (signedVelocity < Fix64.Zero)
        {
            TrySelectSegmentSideHit(
                start,
                displacement,
                segmentStart,
                edge,
                edgeLengthSquared,
                normal,
                radius,
                signedStart,
                signedVelocity,
                stableKey,
                ref found,
                ref hit);
        }
        else if (signedVelocity > Fix64.Zero)
        {
            TrySelectSegmentSideHit(
                start,
                displacement,
                segmentStart,
                edge,
                edgeLengthSquared,
                -normal,
                -radius,
                signedStart,
                signedVelocity,
                stableKey,
                ref found,
                ref hit);
        }
        return found;
    }

    private static void TrySelectSegmentSideHit(
        FixVector2 start,
        FixVector2 displacement,
        FixVector2 segmentStart,
        FixVector2 edge,
        Fix64 edgeLengthSquared,
        FixVector2 contactNormal,
        Fix64 targetSignedDistance,
        Fix64 signedStart,
        Fix64 signedVelocity,
        int stableKey,
        ref bool found,
        ref SweepHit hit)
    {
        Fix64 time = (targetSignedDistance - signedStart) / signedVelocity;
        if (time < Fix64.Zero || time > Fix64.One)
            return;
        FixVector2 contact = start + MultiplyVectorTowardZero(displacement, time);
        Fix64 edgeProjection = FixVector2.Dot(contact - segmentStart, edge) / edgeLengthSquared;
        if (edgeProjection < Fix64.Zero || edgeProjection > Fix64.One)
            return;
        if (RawDot(displacement, contactNormal) >= 0)
            return;
        SelectHit(new SweepHit(time, contactNormal, stableKey), ref found, ref hit);
    }

    private static bool TryFindEarliestRuntimeObstacleHit(
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        FixVector2 start,
        FixVector2 displacement,
        Fix64 radius,
        int stableKeyBase,
        out SweepHit hit)
    {
        return TryFindEarliestRuntimeObstacleHit(
            runtimeObstacles,
            start,
            displacement,
            radius,
            stableKeyBase,
            -1,
            out hit);
    }

    private static bool TryFindEarliestRuntimeObstacleHit(
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        FixVector2 start,
        FixVector2 displacement,
        Fix64 radius,
        int stableKeyBase,
        int relaxedStableKey,
        out SweepHit hit)
    {
        bool found = false;
        hit = default;
        for (int i = 0; i < runtimeObstacles.Count; i++)
        {
            LogicStaticCollisionObstacle obstacle = runtimeObstacles[i];
            int obstacleKey = checked(stableKeyBase + i * 8);
            Fix64 obstacleRadius = relaxedStableKey >= obstacleKey && relaxedStableKey < obstacleKey + 8
                ? Fix64.Max(Fix64.Zero, radius - s_Epsilon)
                : radius;
            SweepHit candidate;
            bool hasHit;
            switch (obstacle.Kind)
            {
                case LogicStaticCollisionObstacleKind.Box:
                    hasHit = TrySweepPointRoundedBox(
                        start,
                        displacement,
                        obstacle.Center.x - obstacle.HalfExtents.x,
                        obstacle.Center.x + obstacle.HalfExtents.x,
                        obstacle.Center.y - obstacle.HalfExtents.y,
                        obstacle.Center.y + obstacle.HalfExtents.y,
                        obstacleRadius,
                        obstacleKey,
                        out candidate);
                    break;
                case LogicStaticCollisionObstacleKind.Circle:
                    hasHit = TrySweepPointCircle(
                        start,
                        displacement,
                        obstacle.Center,
                        obstacle.Radius + obstacleRadius,
                        obstacleKey + 4,
                        out candidate);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(obstacle.Kind),
                        obstacle.Kind,
                        "Unknown runtime collision obstacle kind.");
            }

            if (!hasHit || RawDot(displacement, candidate.Normal) >= 0)
                continue;
            SelectHit(candidate, ref found, ref hit);
        }

        return found;
    }

    private static int ResolveRoundedBoxFeatureKey(
        FixVector2 position,
        Fix64 minX,
        Fix64 maxX,
        Fix64 minY,
        Fix64 maxY,
        int stableKeyBase)
    {
        if (position.x < minX)
        {
            if (position.y < minY)
                return stableKeyBase + 4;
            if (position.y > maxY)
                return stableKeyBase + 7;
            return stableKeyBase;
        }
        if (position.x > maxX)
        {
            if (position.y < minY)
                return stableKeyBase + 5;
            if (position.y > maxY)
                return stableKeyBase + 6;
            return stableKeyBase + 1;
        }
        return position.y < minY ? stableKeyBase + 2 : stableKeyBase + 3;
    }

    private static bool TrySweepPointRoundedBox(
        FixVector2 start,
        FixVector2 displacement,
        Fix64 minX,
        Fix64 maxX,
        Fix64 minY,
        Fix64 maxY,
        Fix64 radius,
        int stableKeyBase,
        out SweepHit hit)
    {
        bool found = false;
        hit = default;
        if (displacement.x > Fix64.Zero)
        {
            TrySelectAxisAlignedSideHit(
                start,
                displacement,
                minX - radius,
                minY,
                maxY,
                true,
                new FixVector2(-1, 0),
                stableKeyBase,
                ref found,
                ref hit);
        }
        else if (displacement.x < Fix64.Zero)
        {
            TrySelectAxisAlignedSideHit(
                start,
                displacement,
                maxX + radius,
                minY,
                maxY,
                true,
                new FixVector2(1, 0),
                stableKeyBase + 1,
                ref found,
                ref hit);
        }
        if (displacement.y > Fix64.Zero)
        {
            TrySelectAxisAlignedSideHit(
                start,
                displacement,
                minY - radius,
                minX,
                maxX,
                false,
                new FixVector2(0, -1),
                stableKeyBase + 2,
                ref found,
                ref hit);
        }
        else if (displacement.y < Fix64.Zero)
        {
            TrySelectAxisAlignedSideHit(
                start,
                displacement,
                maxY + radius,
                minX,
                maxX,
                false,
                new FixVector2(0, 1),
                stableKeyBase + 3,
                ref found,
                ref hit);
        }

        TrySelectRoundedBoxCornerHit(start, displacement, new FixVector2(minX, minY), radius, -1, -1, stableKeyBase + 4, ref found, ref hit);
        TrySelectRoundedBoxCornerHit(start, displacement, new FixVector2(maxX, minY), radius, 1, -1, stableKeyBase + 5, ref found, ref hit);
        TrySelectRoundedBoxCornerHit(start, displacement, new FixVector2(maxX, maxY), radius, 1, 1, stableKeyBase + 6, ref found, ref hit);
        TrySelectRoundedBoxCornerHit(start, displacement, new FixVector2(minX, maxY), radius, -1, 1, stableKeyBase + 7, ref found, ref hit);
        return found;
    }

    private static void TrySelectAxisAlignedSideHit(
        FixVector2 start,
        FixVector2 displacement,
        Fix64 plane,
        Fix64 rangeMin,
        Fix64 rangeMax,
        bool vertical,
        FixVector2 normal,
        int stableKey,
        ref bool found,
        ref SweepHit hit)
    {
        Fix64 axisDisplacement = vertical ? displacement.x : displacement.y;
        Fix64 axisStart = vertical ? start.x : start.y;
        Fix64 time = (plane - axisStart) / axisDisplacement;
        if (time < Fix64.Zero || time > Fix64.One)
            return;
        Fix64 contactCoordinate = vertical
            ? start.y + displacement.y * time
            : start.x + displacement.x * time;
        if (contactCoordinate < rangeMin || contactCoordinate > rangeMax)
            return;
        SelectHit(new SweepHit(time, normal, stableKey), ref found, ref hit);
    }

    private static void TrySelectRoundedBoxCornerHit(
        FixVector2 start,
        FixVector2 displacement,
        FixVector2 corner,
        Fix64 radius,
        int expectedNormalXSign,
        int expectedNormalYSign,
        int stableKey,
        ref bool found,
        ref SweepHit hit)
    {
        if (!TrySweepPointCircle(start, displacement, corner, radius, stableKey, out SweepHit candidate))
            return;
        if (expectedNormalXSign < 0 && candidate.Normal.x > Fix64.Zero
            || expectedNormalXSign > 0 && candidate.Normal.x < Fix64.Zero
            || expectedNormalYSign < 0 && candidate.Normal.y > Fix64.Zero
            || expectedNormalYSign > 0 && candidate.Normal.y < Fix64.Zero)
        {
            return;
        }
        if (RawDot(displacement, candidate.Normal) >= 0)
            return;
        SelectHit(candidate, ref found, ref hit);
    }

    private static bool TrySweepPointCircle(
        FixVector2 start,
        FixVector2 displacement,
        FixVector2 center,
        Fix64 radius,
        int stableKey,
        out SweepHit hit)
    {
        if (radius <= Fix64.Zero)
        {
            hit = default;
            return false;
        }

        long displacementXRaw = displacement.x.RawValue;
        long displacementYRaw = displacement.y.RawValue;
        long displacementLengthSquaredRaw = checked(
            checked(displacementXRaw * displacementXRaw)
            + checked(displacementYRaw * displacementYRaw));
        if (displacementLengthSquaredRaw <= 0)
        {
            hit = default;
            return false;
        }

        FixVector2 relativeStart = start - center;
        bool startInside = IsRawDistanceLessThanRadius(relativeStart, radius);
        long projectionNumeratorRaw = checked(-checked(
            checked(relativeStart.x.RawValue * displacementXRaw)
            + checked(relativeStart.y.RawValue * displacementYRaw)));
        if (projectionNumeratorRaw <= 0 && !startInside)
        {
            hit = default;
            return false;
        }

        long closestTimeRaw;
        if (projectionNumeratorRaw <= 0)
        {
            closestTimeRaw = 0;
        }
        else if (projectionNumeratorRaw >= displacementLengthSquaredRaw)
        {
            closestTimeRaw = Fix64.One.RawValue;
        }
        else
        {
            long floorTimeRaw = ResolveUnitRatioRaw(
                projectionNumeratorRaw,
                displacementLengthSquaredRaw);
            long ceilTimeRaw = Math.Min(Fix64.One.RawValue, checked(floorTimeRaw + 1));
            long floorDistanceSquaredRaw = EvaluateSweepCircleDistanceSquaredRaw(
                relativeStart,
                displacement,
                floorTimeRaw);
            long ceilDistanceSquaredRaw = EvaluateSweepCircleDistanceSquaredRaw(
                relativeStart,
                displacement,
                ceilTimeRaw);
            closestTimeRaw = floorDistanceSquaredRaw <= ceilDistanceSquaredRaw
                ? floorTimeRaw
                : ceilTimeRaw;
        }

        if (!IsSweepCircleSampleInside(relativeStart, displacement, closestTimeRaw, radius))
        {
            hit = default;
            return false;
        }

        // Search the Q12 time lattice for the last non-penetrating sample before first entry.
        long clearTimeRaw = 0;
        long penetratingTimeRaw = closestTimeRaw;
        if (startInside)
        {
            penetratingTimeRaw = 0;
        }
        else
        {
            while (penetratingTimeRaw - clearTimeRaw > 1)
            {
                long middleTimeRaw = clearTimeRaw + (penetratingTimeRaw - clearTimeRaw) / 2;
                if (IsSweepCircleSampleInside(relativeStart, displacement, middleTimeRaw, radius))
                    penetratingTimeRaw = middleTimeRaw;
                else
                    clearTimeRaw = middleTimeRaw;
            }
        }

        Fix64 time = Fix64.FromRaw(clearTimeRaw);
        FixVector2 contactDelta = start + MultiplyVectorTowardZero(displacement, time) - center;
        if (contactDelta == FixVector2.Zero)
        {
            hit = default;
            return false;
        }

        hit = new SweepHit(time, NormalizeContactVector(contactDelta), stableKey);
        return true;
    }

    private static long ResolveUnitRatioRaw(long numerator, long denominator)
    {
        if (numerator < 0)
            throw new ArgumentOutOfRangeException(nameof(numerator));
        if (denominator <= 0)
            throw new ArgumentOutOfRangeException(nameof(denominator));
        if (numerator >= denominator)
            throw new ArgumentOutOfRangeException(nameof(numerator), "Fixed sweep unit ratio must be less than one.");

        long remainder = numerator;
        long result = 0;
        for (int bit = 0; bit < Fix64.FRACTIONAL_PLACES; bit++)
        {
            result <<= 1;
            long complement = denominator - remainder;
            if (remainder >= complement)
            {
                remainder -= complement;
                result |= 1;
            }
            else
            {
                remainder *= 2;
            }
        }
        return result;
    }

    private static bool IsSweepCircleSampleInside(
        FixVector2 relativeStart,
        FixVector2 displacement,
        long timeRaw,
        Fix64 radius)
    {
        FixVector2 delta = EvaluateSweepCircleDelta(relativeStart, displacement, timeRaw);
        return IsRawDistanceLessThanRadius(delta, radius);
    }

    private static long EvaluateSweepCircleDistanceSquaredRaw(
        FixVector2 relativeStart,
        FixVector2 displacement,
        long timeRaw)
    {
        return RawDistanceSquared(EvaluateSweepCircleDelta(relativeStart, displacement, timeRaw));
    }

    private static FixVector2 EvaluateSweepCircleDelta(
        FixVector2 relativeStart,
        FixVector2 displacement,
        long timeRaw)
    {
        if (timeRaw < 0 || timeRaw > Fix64.One.RawValue)
            throw new ArgumentOutOfRangeException(nameof(timeRaw));
        return relativeStart + MultiplyVectorTowardZero(displacement, Fix64.FromRaw(timeRaw));
    }

    private static bool IsRawDistanceLessThanRadius(FixVector2 delta, Fix64 radius)
    {
        if (radius <= Fix64.Zero)
            return false;
        return RawDistanceSquared(delta) < checked(radius.RawValue * radius.RawValue);
    }

    private static long RawDistanceSquared(FixVector2 delta)
    {
        return checked(
            checked(delta.x.RawValue * delta.x.RawValue)
            + checked(delta.y.RawValue * delta.y.RawValue));
    }

    private static long RawDot(FixVector2 left, FixVector2 right)
    {
        return checked(
            checked(left.x.RawValue * right.x.RawValue)
            + checked(left.y.RawValue * right.y.RawValue));
    }

    private static FixVector2 MultiplyVectorTowardZero(FixVector2 value, Fix64 scalar)
    {
        long denominator = 1L << Fix64.FRACTIONAL_PLACES;
        return new FixVector2(
            Fix64.FromRaw(checked(value.x.RawValue * scalar.RawValue) / denominator),
            Fix64.FromRaw(checked(value.y.RawValue * scalar.RawValue) / denominator));
    }

    private static FixVector2 NormalizeContactVector(FixVector2 value)
    {
        Fix64 magnitude = FixVector2.Magnitude(value);
        if (magnitude <= Fix64.Zero)
            throw new InvalidOperationException("Static collision contact normal cannot be zero.");
        return value / magnitude;
    }

    private static int GetRuntimeObstacleStableKeyBase(LogicStaticCollisionWorld world)
    {
        if (world.HasBoundaryGeometry)
            return checked(4 + world.BoundarySegmentCount);
        return checked(GetTopologyEdgeStableKeyBase(world) + checked(world.Width * world.Height) * 4);
    }

    private static int GetTopologyEdgeStableKeyBase(LogicStaticCollisionWorld world)
    {
        return checked(4 + checked(world.Width * world.Height) * 4);
    }

    private static bool IsExposedFace(
        LogicStaticCollisionWorld world,
        int cellX,
        int cellY,
        FixVector2 normal)
    {
        if (normal.x < Fix64.Zero)
            return world.IsWalkable(cellX - 1, cellY);
        if (normal.x > Fix64.Zero)
            return world.IsWalkable(cellX + 1, cellY);
        if (normal.y < Fix64.Zero)
            return world.IsWalkable(cellX, cellY - 1);
        if (normal.y > Fix64.Zero)
            return world.IsWalkable(cellX, cellY + 1);
        return false;
    }

    private static void TrySelectBoundaryHit(
        LogicStaticCollisionWorld world,
        FixVector2 start,
        FixVector2 displacement,
        Fix64 radius,
        ref bool found,
        ref SweepHit hit)
    {
        Fix64 minX = world.Origin.x + radius;
        Fix64 maxX = world.MaxWorldX - radius;
        Fix64 minY = world.Origin.y + radius;
        Fix64 maxY = world.MaxWorldY - radius;

        if (displacement.x < Fix64.Zero && start.x + displacement.x < minX)
            SelectHit(new SweepHit((minX - start.x) / displacement.x, new FixVector2(1, 0), 0), ref found, ref hit);
        if (displacement.x > Fix64.Zero && start.x + displacement.x > maxX)
            SelectHit(new SweepHit((maxX - start.x) / displacement.x, new FixVector2(-1, 0), 1), ref found, ref hit);
        if (displacement.y < Fix64.Zero && start.y + displacement.y < minY)
            SelectHit(new SweepHit((minY - start.y) / displacement.y, new FixVector2(0, 1), 2), ref found, ref hit);
        if (displacement.y > Fix64.Zero && start.y + displacement.y > maxY)
            SelectHit(new SweepHit((maxY - start.y) / displacement.y, new FixVector2(0, -1), 3), ref found, ref hit);
    }

    private static bool TrySweepPointAabb(
        FixVector2 start,
        FixVector2 displacement,
        Fix64 minX,
        Fix64 maxX,
        Fix64 minY,
        Fix64 maxY,
        int stableKeyBase,
        out SweepHit hit)
    {
        Fix64 enter = Fix64.Zero;
        Fix64 exit = Fix64.One;
        FixVector2 normal = FixVector2.Zero;
        int normalKey = int.MaxValue;

        if (!UpdateSlab(start.x, displacement.x, minX, maxX, new FixVector2(-1, 0), new FixVector2(1, 0), stableKeyBase, stableKeyBase + 1, ref enter, ref exit, ref normal, ref normalKey)
            || !UpdateSlab(start.y, displacement.y, minY, maxY, new FixVector2(0, -1), new FixVector2(0, 1), stableKeyBase + 2, stableKeyBase + 3, ref enter, ref exit, ref normal, ref normalKey)
            || enter < Fix64.Zero
            || enter > Fix64.One
            || normal == FixVector2.Zero)
        {
            hit = default;
            return false;
        }

        hit = new SweepHit(enter, normal, normalKey);
        return true;
    }

    private static bool UpdateSlab(
        Fix64 start,
        Fix64 displacement,
        Fix64 min,
        Fix64 max,
        FixVector2 minNormal,
        FixVector2 maxNormal,
        int minKey,
        int maxKey,
        ref Fix64 enter,
        ref Fix64 exit,
        ref FixVector2 normal,
        ref int normalKey)
    {
        if (displacement == Fix64.Zero)
            return start >= min && start <= max;

        Fix64 near;
        Fix64 far;
        FixVector2 nearNormal;
        int nearKey;
        if (displacement > Fix64.Zero)
        {
            near = (min - start) / displacement;
            far = (max - start) / displacement;
            nearNormal = minNormal;
            nearKey = minKey;
        }
        else
        {
            near = (max - start) / displacement;
            far = (min - start) / displacement;
            nearNormal = maxNormal;
            nearKey = maxKey;
        }

        if (near > enter || (near == enter && nearKey < normalKey))
        {
            enter = near;
            normal = nearNormal;
            normalKey = nearKey;
        }
        if (far < exit)
            exit = far;
        return enter <= exit;
    }

    private static void SelectHit(SweepHit candidate, ref bool found, ref SweepHit best)
    {
        if (candidate.Time < Fix64.Zero || candidate.Time > Fix64.One)
            return;
        if (!found
            || candidate.Time < best.Time
            || (candidate.Time == best.Time && candidate.StableKey < best.StableKey))
        {
            found = true;
            best = candidate;
        }
    }

    private static void GetCellRange(
        LogicStaticCollisionWorld world,
        FixVector2 start,
        FixVector2 end,
        Fix64 radius,
        out int minCellX,
        out int maxCellX,
        out int minCellY,
        out int maxCellY)
    {
        Fix64 minX = Fix64.Min(start.x, end.x) - radius;
        Fix64 maxX = Fix64.Max(start.x, end.x) + radius;
        Fix64 minY = Fix64.Min(start.y, end.y) - radius;
        Fix64 maxY = Fix64.Max(start.y, end.y) + radius;

        minCellX = Clamp(world.WorldToGridX(minX), 0, world.Width - 1);
        maxCellX = Clamp(world.WorldToGridX(maxX), 0, world.Width - 1);
        minCellY = Clamp(world.WorldToGridY(minY), 0, world.Height - 1);
        maxCellY = Clamp(world.WorldToGridY(maxY), 0, world.Height - 1);
    }

    private static int FloorToInt(Fix64 value)
    {
        long integer = value.RawValue >> Fix64.FRACTIONAL_PLACES;
        return checked((int)integer);
    }

    private static int Clamp(int value, int min, int max)
    {
        return value < min ? min : value > max ? max : value;
    }
}

public enum LogicStaticCollisionContactKind
{
    None = 0,
    WorldBoundary = 1,
    AuthoredGridCell = 2,
    AuthoredTraversalEdge = 3,
    RuntimeObstacle = 4,
    AuthoredBoundarySegment = 5,
}

public readonly struct LogicStaticCollisionShadowResult
{
    internal LogicStaticCollisionShadowResult(
        LogicStaticCollisionWorld world,
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        LogicStaticCollisionSolveResult solveResult)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (runtimeObstacles == null)
            throw new ArgumentNullException(nameof(runtimeObstacles));

        WorldVersion = world.WorldVersion;
        SolveResult = solveResult;
        ContactKind = LogicStaticCollisionContactKind.None;
        ContactCellX = -1;
        ContactCellY = -1;
        ContactBoundarySegmentIndex = -1;
        RuntimeObstacleStableId = 0;
        int stableKey = solveResult.FirstHitStableKey;
        if (stableKey < 0)
            return;
        if (stableKey < 4)
        {
            ContactKind = LogicStaticCollisionContactKind.WorldBoundary;
            return;
        }

        if (world.HasBoundaryGeometry)
        {
            int runtimeKeyBase = checked(4 + world.BoundarySegmentCount);
            if (stableKey < runtimeKeyBase)
            {
                ContactKind = LogicStaticCollisionContactKind.AuthoredBoundarySegment;
                ContactBoundarySegmentIndex = stableKey - 4;
                return;
            }

            int geometryObstacleIndex = (stableKey - runtimeKeyBase) / 8;
            if (geometryObstacleIndex < 0 || geometryObstacleIndex >= runtimeObstacles.Count)
            {
                throw new InvalidOperationException(
                    $"Static collision hit key {stableKey} resolved invalid runtime obstacle index {geometryObstacleIndex}/{runtimeObstacles.Count}.");
            }
            ContactKind = LogicStaticCollisionContactKind.RuntimeObstacle;
            RuntimeObstacleStableId = runtimeObstacles[geometryObstacleIndex].StableId;
            return;
        }

        int topologyEdgeKeyBase = checked(4 + checked(world.Width * world.Height) * 4);
        if (stableKey < topologyEdgeKeyBase)
        {
            int cellIndex = (stableKey - 4) / 4;
            ContactKind = LogicStaticCollisionContactKind.AuthoredGridCell;
            ContactCellX = cellIndex % world.Width;
            ContactCellY = cellIndex / world.Width;
            return;
        }

        int runtimeObstacleKeyBase = checked(topologyEdgeKeyBase + checked(world.Width * world.Height) * 4);
        if (stableKey < runtimeObstacleKeyBase)
        {
            int cellIndex = (stableKey - topologyEdgeKeyBase) / 4;
            ContactKind = LogicStaticCollisionContactKind.AuthoredTraversalEdge;
            ContactCellX = cellIndex % world.Width;
            ContactCellY = cellIndex / world.Width;
            return;
        }

        int obstacleIndex = (stableKey - runtimeObstacleKeyBase) / 8;
        if (obstacleIndex < 0 || obstacleIndex >= runtimeObstacles.Count)
        {
            throw new InvalidOperationException(
                $"Static collision hit key {stableKey} resolved invalid runtime obstacle index {obstacleIndex}/{runtimeObstacles.Count}.");
        }
        ContactKind = LogicStaticCollisionContactKind.RuntimeObstacle;
        RuntimeObstacleStableId = runtimeObstacles[obstacleIndex].StableId;
    }

    public int WorldVersion { get; }
    public LogicStaticCollisionSolveResult SolveResult { get; }
    public LogicStaticCollisionContactKind ContactKind { get; }
    public int ContactCellX { get; }
    public int ContactCellY { get; }
    public int ContactBoundarySegmentIndex { get; }
    public int RuntimeObstacleStableId { get; }

    public Vector3 ResolvedDisplacement => new Vector3(
        (float)SolveResult.ResolvedDisplacement.x,
        0f,
        (float)SolveResult.ResolvedDisplacement.y);
}

public static class LogicStaticCollisionShadowService
{
    private sealed class CachedWorld
    {
        public int Version;
        public int AgentTypeId;
        public int Width;
        public int Height;
        public long CellSizeGridRaw;
        public long OriginXGridRaw;
        public long OriginYGridRaw;
        public bool[] BaseWalkableMask;
        public byte[] BaseNeighborTraversalMask;
        public FixVector2[] BoundaryVertices;
        public int[] BoundaryPathStarts;
        public LogicStaticCollisionWorld World;

        public bool Matches(LogicStaticCollisionSourceData source)
        {
            return Version == source.WorldVersion
                   && AgentTypeId == source.AgentTypeId
                   && Width == source.Width
                   && Height == source.Height
                   && CellSizeGridRaw == source.CellSizeGridRaw
                   && OriginXGridRaw == source.OriginXGridRaw
                   && OriginYGridRaw == source.OriginYGridRaw
                   && ReferenceEquals(BaseWalkableMask, source.BaseWalkableMask)
                   && ReferenceEquals(BaseNeighborTraversalMask, source.BaseNeighborTraversalMask)
                   && ReferenceEquals(BoundaryVertices, source.BoundaryVertices)
                   && ReferenceEquals(BoundaryPathStarts, source.BoundaryPathStarts);
        }
    }

    private static readonly Dictionary<int, CachedWorld> s_Worlds = new Dictionary<int, CachedWorld>();

    private static float s_MismatchTolerance = 0.03f;
    private static int s_LogIntervalTicks = 300;
    private static ulong s_NextMismatchLogFrame;

    public static bool Enabled { get; private set; } = true;
    public static ulong ComparisonCount { get; private set; }
    public static ulong MismatchCount { get; private set; }
    public static ulong SolveFailureCount { get; private set; }

    public static void Configure(bool enabled, float mismatchTolerance, int logIntervalTicks)
    {
        if (float.IsNaN(mismatchTolerance) || float.IsInfinity(mismatchTolerance) || mismatchTolerance < 0f)
            throw new ArgumentOutOfRangeException(nameof(mismatchTolerance));
        if (logIntervalTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(logIntervalTicks));

        Enabled = enabled;
        s_MismatchTolerance = mismatchTolerance;
        s_LogIntervalTicks = logIntervalTicks;
    }

    public static void Clear()
    {
        s_Worlds.Clear();
        ComparisonCount = 0;
        MismatchCount = 0;
        SolveFailureCount = 0;
        s_NextMismatchLogFrame = 0;
    }

    public static bool TrySolve(
        int agentTypeId,
        Vector3 start,
        Vector3 desiredDisplacement,
        float radius,
        out LogicStaticCollisionShadowResult result)
    {
        result = default;
        if (!Enabled)
            return false;
        ValidateFinite(start, nameof(start));
        ValidateFinite(desiredDisplacement, nameof(desiredDisplacement));
        if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0f)
            throw new ArgumentOutOfRangeException(nameof(radius));
        var startFixed = new FixVector2((Fix64)start.x, (Fix64)start.z);
        var displacementFixed = new FixVector2((Fix64)desiredDisplacement.x, (Fix64)desiredDisplacement.z);
        return TrySolveFixed(agentTypeId, startFixed, displacementFixed, (Fix64)radius, out result);
    }

    public static bool TrySolveFixed(
        int agentTypeId,
        FixVector2 start,
        FixVector2 desiredDisplacement,
        Fix64 radius,
        out LogicStaticCollisionShadowResult result)
    {
        return TrySolveFixed(
            agentTypeId,
            start,
            desiredDisplacement,
            radius,
            LogicStaticCollisionSlideMode.PreserveTangentialComponent,
            out result);
    }

    public static bool TrySolveFixed(
        int agentTypeId,
        FixVector2 start,
        FixVector2 desiredDisplacement,
        Fix64 radius,
        LogicStaticCollisionSlideMode slideMode,
        out LogicStaticCollisionShadowResult result)
    {
        result = default;
        if (!Enabled)
            return false;
        if (radius < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (!FlowFieldCrowdMovementSystem.TryGetStaticCollisionShadowSource(agentTypeId, out LogicStaticCollisionSourceData source))
            return false;

        LogicStaticCollisionWorld world = ResolveWorld(agentTypeId, source);
        Fix64 effectiveRadius = source.BoundaryVertices != null
            ? radius
            : Fix64.Max(Fix64.Zero, radius - Fix64.FromRaw(source.EncodedCenterClearanceFixedRaw));
        LogicStaticCollisionSolveResult solveResult = DeterministicStaticCollisionSolver.SolveCircle(
            world,
            start,
            desiredDisplacement,
            effectiveRadius,
            radius,
            radius,
            source.RuntimeObstacles,
            slideMode);
        result = new LogicStaticCollisionShadowResult(world, source.RuntimeObstacles, solveResult);
        return true;
    }

    internal static bool TryGetRuntimeObstacleForContact(
        int agentTypeId,
        int stableKey,
        out LogicStaticCollisionObstacle obstacle)
    {
        obstacle = default;
        if (!FlowFieldCrowdMovementSystem.TryGetStaticCollisionShadowSource(agentTypeId, out LogicStaticCollisionSourceData source))
            return false;

        LogicStaticCollisionWorld world = ResolveWorld(agentTypeId, source);
        int runtimeKeyBase = world.HasBoundaryGeometry
            ? checked(4 + world.BoundarySegmentCount)
            : checked(4 + checked(world.Width * world.Height) * 8);
        if (stableKey < runtimeKeyBase)
            return false;

        int obstacleIndex = (stableKey - runtimeKeyBase) / 8;
        if (obstacleIndex < 0 || obstacleIndex >= source.RuntimeObstacles.Length)
        {
            throw new InvalidOperationException(
                $"Static collision contact key {stableKey} resolved invalid runtime obstacle index {obstacleIndex}/{source.RuntimeObstacles.Length}.");
        }

        obstacle = source.RuntimeObstacles[obstacleIndex];
        return true;
    }

    public static void RecordComparison(
        ulong frameId,
        int entityId,
        int agentTypeId,
        Vector3 start,
        Vector3 requested,
        Vector3 oldConstrained,
        Vector3 actual,
        LogicStaticCollisionShadowResult shadow)
    {
        ComparisonCount = checked(ComparisonCount + 1);
        if (!shadow.SolveResult.Success)
            SolveFailureCount = checked(SolveFailureCount + 1);

        Vector3 expected = shadow.ResolvedDisplacement;
        float oldError = HorizontalMagnitude(expected - oldConstrained);
        float actualError = HorizontalMagnitude(expected - actual);
        bool mismatch = !shadow.SolveResult.Success
                        || oldError > s_MismatchTolerance
                        || actualError > s_MismatchTolerance;
        if (!mismatch)
            return;

        MismatchCount = checked(MismatchCount + 1);
        if (!LogicFrameRuntime.IsActive || frameId < s_NextMismatchLogFrame)
            return;

        s_NextMismatchLogFrame = checked(frameId + (ulong)s_LogIntervalTicks);
        LogicStaticCollisionSolveResult solve = shadow.SolveResult;
        Debug.LogWarning(
            $"[StaticCollisionShadow] frame={frameId} entity={entityId} agentType={agentTypeId} world={shadow.WorldVersion} " +
            $"success={solve.Success} failure={solve.Failure} contacts={solve.ContactCount} overlap={solve.StartedOverlapping} " +
            $"start={start} requested={requested} expected={expected} oldConstrained={oldConstrained} actual={actual} " +
            $"oldError={oldError:F4} actualError={actualError:F4} " +
            $"fixedStart=({solve.Start.x.RawValue},{solve.Start.y.RawValue}) " +
            $"fixedDesired=({solve.DesiredDisplacement.x.RawValue},{solve.DesiredDisplacement.y.RawValue}) " +
            $"fixedResolved=({solve.ResolvedDisplacement.x.RawValue},{solve.ResolvedDisplacement.y.RawValue})");
    }

    private static LogicStaticCollisionWorld ResolveWorld(int requestedAgentTypeId, LogicStaticCollisionSourceData source)
    {
        if (s_Worlds.TryGetValue(requestedAgentTypeId, out CachedWorld cached)
            && cached.Matches(source))
        {
            return cached.World;
        }

        if (source.BaseWalkableMask == null)
            throw new InvalidOperationException("Static collision shadow source has no base walkable mask.");
        var world = new LogicStaticCollisionWorld(
            source.AgentTypeId,
            source.WorldVersion,
            source.Width,
            source.Height,
            source.CellSizeGridRaw,
            source.OriginXGridRaw,
            source.OriginYGridRaw,
            source.BaseWalkableMask,
            source.BaseNeighborTraversalMask,
            source.BoundaryVertices,
            source.BoundaryPathStarts);
        s_Worlds[requestedAgentTypeId] = new CachedWorld
        {
            Version = source.WorldVersion,
            AgentTypeId = source.AgentTypeId,
            Width = source.Width,
            Height = source.Height,
            CellSizeGridRaw = source.CellSizeGridRaw,
            OriginXGridRaw = source.OriginXGridRaw,
            OriginYGridRaw = source.OriginYGridRaw,
            BaseWalkableMask = source.BaseWalkableMask,
            BaseNeighborTraversalMask = source.BaseNeighborTraversalMask,
            BoundaryVertices = source.BoundaryVertices,
            BoundaryPathStarts = source.BoundaryPathStarts,
            World = world,
        };
        return world;
    }

#if UNITY_EDITOR
    internal static LogicStaticCollisionWorld ResolveWorldForEditorTest(
        int requestedAgentTypeId,
        LogicStaticCollisionSourceData source)
    {
        return ResolveWorld(requestedAgentTypeId, source);
    }
#endif

    private static void ValidateFinite(Vector3 value, string name)
    {
        if (float.IsNaN(value.x)
            || float.IsNaN(value.y)
            || float.IsNaN(value.z)
            || float.IsInfinity(value.x)
            || float.IsInfinity(value.y)
            || float.IsInfinity(value.z))
        {
            throw new InvalidOperationException($"Static collision shadow received non-finite {name}={value}.");
        }
    }

    private static float HorizontalMagnitude(Vector3 value)
    {
        return Mathf.Sqrt(value.x * value.x + value.z * value.z);
    }
}
