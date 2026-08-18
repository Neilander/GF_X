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
            neighborTraversalMask)
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
        byte[] neighborTraversalMask)
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
        FixVector2 firstHitNormal = default)
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

            if (firstHitStableKey < 0)
            {
                firstHitStableKey = hit.StableKey;
                firstHitNormal = hit.Normal;
            }

            Fix64 travelTime = hit.Time;
            position += remaining * travelTime;

            FixVector2 leftover = remaining * (Fix64.One - hit.Time);
            Fix64 remainingDistance = FixVector2.Magnitude(leftover);
            Fix64 inwardDistance = FixVector2.Dot(leftover, hit.Normal);
            if (inwardDistance < Fix64.Zero)
            {
                leftover -= hit.Normal * inwardDistance;
                if (slideMode == LogicStaticCollisionSlideMode.PreserveRemainingDistance
                    && leftover != FixVector2.Zero)
                {
                    leftover *= remainingDistance / FixVector2.Magnitude(leftover);
                }
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
                    contactCount);
            }

            remaining = leftover;
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
            contactCount,
            firstHitStableKey,
            firstHitNormal);
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
            position += penetration.Normal * (penetration.Depth + s_Epsilon);
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
            position += remaining * travelTime;
            FixVector2 leftover = remaining * (Fix64.One - hit.Time);
            Fix64 inwardDistance = FixVector2.Dot(leftover, hit.Normal);
            if (inwardDistance < Fix64.Zero)
                leftover -= hit.Normal * inwardDistance;

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
                   runtimeObstacleRadius,
                   out _);
    }

    private static LogicStaticCollisionSolveResult FailureResult(
        LogicStaticCollisionFailure failure,
        FixVector2 start,
        FixVector2 position,
        FixVector2 desiredDisplacement,
        bool startedOverlapping,
        int contactCount)
    {
        return new LogicStaticCollisionSolveResult(
            false,
            failure,
            start,
            position,
            desiredDisplacement,
            position - start,
            startedOverlapping,
            contactCount);
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
            if (TryFindWorldBoundaryPenetration(world, position, radius, out Penetration boundary))
            {
                startedOverlapping = true;
                position += boundary.Normal * (boundary.Depth + s_Epsilon);
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
                position += cell.Normal * (cell.Depth + s_Epsilon);
                continue;
            }

            if (TryFindTopologyEdgePenetration(
                    world,
                    position,
                    Fix64.Max(topologyEdgeRadius, s_Epsilon),
                    out Penetration topologyEdge))
            {
                startedOverlapping = true;
                position += topologyEdge.Normal * (topologyEdge.Depth + s_Epsilon);
                continue;
            }

            if (TryFindRuntimeObstaclePenetration(
                    world,
                    runtimeObstacles,
                    position,
                    runtimeObstacleRadius,
                    out Penetration runtimeObstacle))
            {
                startedOverlapping = true;
                position += runtimeObstacle.Normal * (runtimeObstacle.Depth + s_Epsilon);
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
        Fix64 radius,
        out Penetration penetration)
    {
        return TryFindRuntimeObstaclePenetration(
            runtimeObstacles,
            position,
            radius,
            GetRuntimeObstacleStableKeyBase(world),
            out penetration);
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
        bool found = false;
        penetration = default;
        for (int i = 0; i < runtimeObstacles.Count; i++)
        {
            LogicStaticCollisionObstacle obstacle = runtimeObstacles[i];
            int obstacleKey = checked(stableKeyBase + i * 8);
            switch (obstacle.Kind)
            {
                case LogicStaticCollisionObstacleKind.Box:
                    Fix64 minX = obstacle.Center.x - obstacle.HalfExtents.x - radius;
                    Fix64 maxX = obstacle.Center.x + obstacle.HalfExtents.x + radius;
                    Fix64 minY = obstacle.Center.y - obstacle.HalfExtents.y - radius;
                    Fix64 maxY = obstacle.Center.y + obstacle.HalfExtents.y + radius;
                    if (position.x <= minX || position.x >= maxX
                        || position.y <= minY || position.y >= maxY)
                    {
                        break;
                    }

                    SelectPenetration(
                        new Penetration(position.x - minX, new FixVector2(-1, 0), obstacleKey),
                        ref found,
                        ref penetration);
                    SelectPenetration(
                        new Penetration(maxX - position.x, new FixVector2(1, 0), obstacleKey + 1),
                        ref found,
                        ref penetration);
                    SelectPenetration(
                        new Penetration(position.y - minY, new FixVector2(0, -1), obstacleKey + 2),
                        ref found,
                        ref penetration);
                    SelectPenetration(
                        new Penetration(maxY - position.y, new FixVector2(0, 1), obstacleKey + 3),
                        ref found,
                        ref penetration);
                    break;
                case LogicStaticCollisionObstacleKind.Circle:
                    Fix64 expandedRadius = obstacle.Radius + radius;
                    FixVector2 delta = position - obstacle.Center;
                    Fix64 distanceSquared = FixVector2.SqrMagnitude(delta);
                    if (distanceSquared >= expandedRadius * expandedRadius)
                        break;

                    Fix64 distance = Fix64.Sqrt(distanceSquared);
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
        bool found = false;
        hit = default;
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
                if (FixVector2.Dot(displacement, candidate.Normal) >= Fix64.Zero)
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

    private static bool TryFindEarliestRuntimeObstacleHit(
        IReadOnlyList<LogicStaticCollisionObstacle> runtimeObstacles,
        FixVector2 start,
        FixVector2 displacement,
        Fix64 radius,
        int stableKeyBase,
        out SweepHit hit)
    {
        bool found = false;
        hit = default;
        for (int i = 0; i < runtimeObstacles.Count; i++)
        {
            LogicStaticCollisionObstacle obstacle = runtimeObstacles[i];
            int obstacleKey = checked(stableKeyBase + i * 8);
            SweepHit candidate;
            bool hasHit;
            switch (obstacle.Kind)
            {
                case LogicStaticCollisionObstacleKind.Box:
                    hasHit = TrySweepPointAabb(
                        start,
                        displacement,
                        obstacle.Center.x - obstacle.HalfExtents.x - radius,
                        obstacle.Center.x + obstacle.HalfExtents.x + radius,
                        obstacle.Center.y - obstacle.HalfExtents.y - radius,
                        obstacle.Center.y + obstacle.HalfExtents.y + radius,
                        obstacleKey,
                        out candidate);
                    break;
                case LogicStaticCollisionObstacleKind.Circle:
                    hasHit = TrySweepPointCircle(
                        start,
                        displacement,
                        obstacle.Center,
                        obstacle.Radius + radius,
                        obstacleKey + 4,
                        out candidate);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(obstacle.Kind),
                        obstacle.Kind,
                        "Unknown runtime collision obstacle kind.");
            }

            if (!hasHit || FixVector2.Dot(displacement, candidate.Normal) >= Fix64.Zero)
                continue;
            SelectHit(candidate, ref found, ref hit);
        }

        return found;
    }

    private static bool TrySweepPointCircle(
        FixVector2 start,
        FixVector2 displacement,
        FixVector2 center,
        Fix64 radius,
        int stableKey,
        out SweepHit hit)
    {
        Fix64 a = FixVector2.Dot(displacement, displacement);
        if (a <= Fix64.Zero)
        {
            hit = default;
            return false;
        }

        FixVector2 relativeStart = start - center;
        Fix64 b = (Fix64)2 * FixVector2.Dot(relativeStart, displacement);
        Fix64 c = FixVector2.Dot(relativeStart, relativeStart) - radius * radius;
        Fix64 discriminant = b * b - (Fix64)4 * a * c;
        if (discriminant < Fix64.Zero)
        {
            hit = default;
            return false;
        }

        Fix64 time = (-b - Fix64.Sqrt(discriminant)) / ((Fix64)2 * a);
        if (time < Fix64.Zero || time > Fix64.One)
        {
            hit = default;
            return false;
        }

        FixVector2 contactDelta = start + displacement * time - center;
        if (contactDelta == FixVector2.Zero)
        {
            hit = default;
            return false;
        }

        hit = new SweepHit(time, contactDelta.GetNormalized(), stableKey);
        return true;
    }

    private static int GetRuntimeObstacleStableKeyBase(LogicStaticCollisionWorld world)
    {
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
        RuntimeObstacleStableId = 0;
        int stableKey = solveResult.FirstHitStableKey;
        if (stableKey < 0)
            return;
        if (stableKey < 4)
        {
            ContactKind = LogicStaticCollisionContactKind.WorldBoundary;
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
                   && ReferenceEquals(BaseNeighborTraversalMask, source.BaseNeighborTraversalMask);
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
        Fix64 effectiveRadius = Fix64.Max(
            Fix64.Zero,
            radius - Fix64.FromRaw(source.EncodedCenterClearanceFixedRaw));
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
            source.BaseNeighborTraversalMask);
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
