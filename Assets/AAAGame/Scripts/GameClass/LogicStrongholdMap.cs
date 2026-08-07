using System;
using System.Collections.Generic;

public readonly struct LogicStrongholdCellDefinition
{
    public LogicStrongholdCellDefinition(string strongholdId, int x, int y, int ownerFactionId = 0)
    {
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new ArgumentException("Stronghold id is empty.", nameof(strongholdId));
        if (ownerFactionId < 0)
            throw new ArgumentOutOfRangeException(nameof(ownerFactionId));

        StrongholdId = strongholdId;
        X = x;
        Y = y;
        OwnerFactionId = ownerFactionId;
    }

    public string StrongholdId { get; }
    public int X { get; }
    public int Y { get; }
    public int OwnerFactionId { get; }
}

public static class LogicStrongholdMap
{
    private readonly struct Cell : IEquatable<Cell>
    {
        public Cell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public bool Equals(Cell other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Cell other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Y);
    }

    private static readonly Dictionary<Cell, string> s_StrongholdIdByCell = new();
    private static readonly Dictionary<string, int> s_OwnerFactionByStrongholdId = new(StringComparer.Ordinal);
    private static readonly List<Cell> s_DeterministicCells = new List<Cell>();
    private static readonly List<LogicStaticCollisionObstacle> s_BlockedCellObstacles =
        new List<LogicStaticCollisionObstacle>();
    private static readonly List<string> s_DeterministicStrongholdIds = new List<string>();
    private static readonly Comparison<string> s_DeterministicStrongholdIdComparison =
        string.CompareOrdinal;
    private static long s_OriginXGridRaw;
    private static long s_OriginZGridRaw;
    private static FixVector2 s_LocalXAxis;
    private static FixVector2 s_LocalZAxis;
    private static long s_CellSizeGridRaw;
    private static Fix64 s_Determinant;
    private static FixVector2 s_CollisionXAxis;
    private static FixVector2 s_CollisionZAxis;
    private static Fix64 s_CollisionXAxisScale;
    private static Fix64 s_CollisionZAxisScale;
    private static int s_OwnerVersion;
    private static int s_BlockedCellObstacleVersion = -1;
    private static int s_BlockedCellObstacleAllowedFactionId;
    private static ulong s_AuthorityHash;

    public static bool IsInitialized { get; private set; }
    public static int CellCount => s_StrongholdIdByCell.Count;

    public static void Initialize(
        FixVector2 origin,
        FixVector2 localXAxis,
        FixVector2 localZAxis,
        Fix64 cellSize,
        IReadOnlyList<LogicStrongholdCellDefinition> cells)
    {
        InitializeCore(
            NavigationGridFixedMath.Fix64ToGridRaw(origin.x),
            NavigationGridFixedMath.Fix64ToGridRaw(origin.y),
            localXAxis,
            localZAxis,
            NavigationGridFixedMath.Fix64ToGridRaw(cellSize),
            cells);
    }

    public static void InitializeFromAuthoredGrid(
        float originX,
        float originZ,
        float localXAxisX,
        float localXAxisZ,
        float localZAxisX,
        float localZAxisZ,
        float cellSize,
        IReadOnlyList<LogicStrongholdCellDefinition> cells)
    {
        long localXAxisXGridRaw = NavigationGridFixedMath.FloatToGridRaw(localXAxisX);
        long localXAxisZGridRaw = NavigationGridFixedMath.FloatToGridRaw(localXAxisZ);
        long localZAxisXGridRaw = NavigationGridFixedMath.FloatToGridRaw(localZAxisX);
        long localZAxisZGridRaw = NavigationGridFixedMath.FloatToGridRaw(localZAxisZ);
        InitializeCore(
            NavigationGridFixedMath.FloatToGridRaw(originX),
            NavigationGridFixedMath.FloatToGridRaw(originZ),
            new FixVector2(
                RequireExactFixedAxis(localXAxisXGridRaw, nameof(localXAxisX)),
                RequireExactFixedAxis(localXAxisZGridRaw, nameof(localXAxisZ))),
            new FixVector2(
                RequireExactFixedAxis(localZAxisXGridRaw, nameof(localZAxisX)),
                RequireExactFixedAxis(localZAxisZGridRaw, nameof(localZAxisZ))),
            NavigationGridFixedMath.FloatToGridRaw(cellSize),
            cells);
    }

    private static void InitializeCore(
        long originXGridRaw,
        long originZGridRaw,
        FixVector2 localXAxis,
        FixVector2 localZAxis,
        long cellSizeGridRaw,
        IReadOnlyList<LogicStrongholdCellDefinition> cells)
    {
        if (IsInitialized)
            throw new InvalidOperationException("LogicStrongholdMap is already initialized.");
        if (cellSizeGridRaw <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSizeGridRaw), cellSizeGridRaw, "Stronghold map cell size must be positive.");
        if (cells == null)
            throw new ArgumentNullException(nameof(cells));

        Fix64 determinant = localXAxis.x * localZAxis.y - localXAxis.y * localZAxis.x;
        if (determinant == Fix64.Zero)
            throw new InvalidOperationException("LogicStrongholdMap transform is degenerate.");
        Fix64 xAxisScale = FixVector2.Magnitude(localXAxis);
        Fix64 zAxisScale = FixVector2.Magnitude(localZAxis);
        FixVector2 collisionXAxis = localXAxis / xAxisScale;
        FixVector2 collisionZAxis = localZAxis / zAxisScale;
        if (Fix64.Abs(FixVector2.Dot(collisionXAxis, collisionZAxis)) > Fix64.FromRaw(16))
            throw new InvalidOperationException("LogicStrongholdMap transform axes must be orthogonal for circle collision.");

        var ordered = new List<LogicStrongholdCellDefinition>(cells.Count);
        for (int i = 0; i < cells.Count; i++)
            ordered.Add(cells[i]);
        ordered.Sort(CompareCells);

        var strongholdIdByCell = new Dictionary<Cell, string>();
        var ownerFactionByStrongholdId = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < ordered.Count; i++)
        {
            LogicStrongholdCellDefinition definition = ordered[i];
            var cell = new Cell(definition.X, definition.Y);
            if (strongholdIdByCell.TryGetValue(cell, out string existing))
            {
                throw new InvalidOperationException(
                    $"LogicStrongholdMap cell ({cell.X},{cell.Y}) is assigned to both '{existing}' and '{definition.StrongholdId}'.");
            }
            strongholdIdByCell.Add(cell, definition.StrongholdId);
            if (ownerFactionByStrongholdId.TryGetValue(definition.StrongholdId, out int existingOwner)
                && existingOwner != definition.OwnerFactionId)
            {
                throw new InvalidOperationException(
                    $"LogicStrongholdMap stronghold '{definition.StrongholdId}' has conflicting owners {existingOwner} and {definition.OwnerFactionId}.");
            }
            ownerFactionByStrongholdId[definition.StrongholdId] = definition.OwnerFactionId;
        }

        s_StrongholdIdByCell.Clear();
        s_DeterministicCells.Clear();
        foreach (KeyValuePair<Cell, string> pair in strongholdIdByCell)
            s_StrongholdIdByCell.Add(pair.Key, pair.Value);
        for (int i = 0; i < ordered.Count; i++)
            s_DeterministicCells.Add(new Cell(ordered[i].X, ordered[i].Y));
        s_OwnerFactionByStrongholdId.Clear();
        foreach (KeyValuePair<string, int> pair in ownerFactionByStrongholdId)
            s_OwnerFactionByStrongholdId.Add(pair.Key, pair.Value);
        s_OriginXGridRaw = originXGridRaw;
        s_OriginZGridRaw = originZGridRaw;
        s_LocalXAxis = localXAxis;
        s_LocalZAxis = localZAxis;
        s_CellSizeGridRaw = cellSizeGridRaw;
        s_Determinant = determinant;
        s_CollisionXAxis = collisionXAxis;
        s_CollisionZAxis = collisionZAxis;
        s_CollisionXAxisScale = xAxisScale;
        s_CollisionZAxisScale = zAxisScale;
        s_OwnerVersion = 1;
        s_BlockedCellObstacleVersion = -1;
        s_BlockedCellObstacleAllowedFactionId = 0;
        s_BlockedCellObstacles.Clear();
        s_AuthorityHash = ComputeAuthorityHash(
            originXGridRaw,
            originZGridRaw,
            localXAxis,
            localZAxis,
            cellSizeGridRaw,
            ordered);
        IsInitialized = true;
    }

    public static void Clear()
    {
        s_StrongholdIdByCell.Clear();
        s_OwnerFactionByStrongholdId.Clear();
        s_DeterministicCells.Clear();
        s_BlockedCellObstacles.Clear();
        s_OriginXGridRaw = 0;
        s_OriginZGridRaw = 0;
        s_LocalXAxis = FixVector2.Zero;
        s_LocalZAxis = FixVector2.Zero;
        s_CellSizeGridRaw = 0;
        s_Determinant = Fix64.Zero;
        s_CollisionXAxis = FixVector2.Zero;
        s_CollisionZAxis = FixVector2.Zero;
        s_CollisionXAxisScale = Fix64.Zero;
        s_CollisionZAxisScale = Fix64.Zero;
        s_OwnerVersion = 0;
        s_BlockedCellObstacleVersion = -1;
        s_BlockedCellObstacleAllowedFactionId = 0;
        s_AuthorityHash = 0;
        IsInitialized = false;
    }

    public static bool TryResolveStrongholdId(FixVector2 worldPosition, out string strongholdId)
    {
        EnsureInitialized();

        FixVector2 delta = WorldDeltaFromAuthoredOrigin(worldPosition);
        Fix64 localX = (delta.x * s_LocalZAxis.y - delta.y * s_LocalZAxis.x) / s_Determinant;
        Fix64 localZ = (s_LocalXAxis.x * delta.y - s_LocalXAxis.y * delta.x) / s_Determinant;
        int cellX = RoundToIntMidpointToEven(localX, s_CellSizeGridRaw);
        int cellY = RoundToIntMidpointToEven(localZ, s_CellSizeGridRaw);
        return s_StrongholdIdByCell.TryGetValue(new Cell(cellX, cellY), out strongholdId);
    }

    public static bool TryGetStrongholdIdAtCell(int x, int y, out string strongholdId)
    {
        EnsureInitialized();
        return s_StrongholdIdByCell.TryGetValue(new Cell(x, y), out strongholdId);
    }

    public static bool TryGetOwnerFactionId(string strongholdId, out int ownerFactionId)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new ArgumentException("Stronghold id is empty.", nameof(strongholdId));
        return s_OwnerFactionByStrongholdId.TryGetValue(strongholdId, out ownerFactionId);
    }

    public static int GetOwnerFactionIdRequired(string strongholdId)
    {
        if (!TryGetOwnerFactionId(strongholdId, out int ownerFactionId))
            throw new InvalidOperationException($"LogicStrongholdMap does not contain stronghold '{strongholdId}'.");
        return ownerFactionId;
    }

    public static int CountOwnedStrongholds(int ownerFactionId)
    {
        EnsureInitialized();
        int count = 0;
        foreach (KeyValuePair<string, int> pair in s_OwnerFactionByStrongholdId)
        {
            if (pair.Value == ownerFactionId)
                count++;
        }
        return count;
    }

    public static IReadOnlyList<string> GetStrongholdIdsOrdered()
    {
        EnsureInitialized();
        var strongholdIds = new List<string>(s_OwnerFactionByStrongholdId.Keys);
        strongholdIds.Sort(StringComparer.Ordinal);
        return strongholdIds;
    }

    public static void SetOwnerFactionId(string strongholdId, int ownerFactionId)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new ArgumentException("Stronghold id is empty.", nameof(strongholdId));
        if (ownerFactionId < 0)
            throw new ArgumentOutOfRangeException(nameof(ownerFactionId));
        if (!s_OwnerFactionByStrongholdId.ContainsKey(strongholdId))
            throw new InvalidOperationException($"LogicStrongholdMap cannot update unknown stronghold '{strongholdId}'.");
        if (s_OwnerFactionByStrongholdId[strongholdId] == ownerFactionId)
            return;
        s_OwnerFactionByStrongholdId[strongholdId] = ownerFactionId;
        s_OwnerVersion = checked(s_OwnerVersion + 1);
    }

    public static FixVector2 ResolveCircleMotionAvoidingForeignStrongholds(
        FixVector2 frameStart,
        FixVector2 candidate,
        Fix64 radius,
        int allowedFactionId,
        out bool constrained)
    {
        EnsureInitialized();
        if (radius < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (allowedFactionId < 0)
            throw new ArgumentOutOfRangeException(nameof(allowedFactionId));

        RefreshBlockedCellObstacles(allowedFactionId);
        if (s_BlockedCellObstacles.Count == 0 || frameStart == candidate)
        {
            constrained = false;
            return candidate;
        }

        FixVector2 localStart = WorldToCollisionLocal(frameStart);
        FixVector2 localCandidate = WorldToCollisionLocal(candidate);
        LogicStaticCollisionSolveResult result = DeterministicStaticCollisionSolver.SolveCircleAgainstObstacles(
            localStart,
            localCandidate - localStart,
            radius,
            s_BlockedCellObstacles);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"LogicStrongholdMap circle motion failed. failure={result.Failure}, startRaw=({frameStart.x.RawValue},{frameStart.y.RawValue}), candidateRaw=({candidate.x.RawValue},{candidate.y.RawValue}), radiusRaw={radius.RawValue}.");
        }

        constrained = result.StartedOverlapping
                      || result.ResolvedDisplacement != result.DesiredDisplacement;
        if (!constrained)
            return candidate;

        return CollisionLocalToWorld(localStart + result.ResolvedDisplacement);
    }

    public static bool IsCircleClearOfForeignStrongholds(
        FixVector2 center,
        Fix64 radius,
        int allowedFactionId)
    {
        EnsureInitialized();
        if (radius < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (allowedFactionId < 0)
            throw new ArgumentOutOfRangeException(nameof(allowedFactionId));

        RefreshBlockedCellObstacles(allowedFactionId);
        if (s_BlockedCellObstacles.Count == 0)
            return true;

        LogicStaticCollisionSolveResult result = DeterministicStaticCollisionSolver.SolveCircleAgainstObstacles(
            WorldToCollisionLocal(center),
            FixVector2.Zero,
            radius,
            s_BlockedCellObstacles);
        if (!result.Success && !result.StartedOverlapping)
        {
            throw new InvalidOperationException(
                $"LogicStrongholdMap circle-clear query failed. failure={result.Failure}, centerRaw=({center.x.RawValue},{center.y.RawValue}), radiusRaw={radius.RawValue}.");
        }

        return !result.StartedOverlapping;
    }

    public static void EnsureInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("LogicStrongholdMap is not initialized.");
    }

    private static void RefreshBlockedCellObstacles(int allowedFactionId)
    {
        if (s_BlockedCellObstacleVersion == s_OwnerVersion
            && s_BlockedCellObstacleAllowedFactionId == allowedFactionId)
        {
            return;
        }

        s_BlockedCellObstacles.Clear();
        Fix64 cellSize = NavigationGridFixedMath.GridRawToFix64(s_CellSizeGridRaw);
        FixVector2 halfExtents = new FixVector2(
            cellSize * s_CollisionXAxisScale / (Fix64)2,
            cellSize * s_CollisionZAxisScale / (Fix64)2);
        for (int i = 0; i < s_DeterministicCells.Count; i++)
        {
            Cell cell = s_DeterministicCells[i];
            string strongholdId = s_StrongholdIdByCell[cell];
            if (s_OwnerFactionByStrongholdId[strongholdId] == allowedFactionId)
                continue;

            Fix64 localCellX = NavigationGridFixedMath.GridRawToFix64(
                checked((long)cell.X * s_CellSizeGridRaw));
            Fix64 localCellY = NavigationGridFixedMath.GridRawToFix64(
                checked((long)cell.Y * s_CellSizeGridRaw));
            s_BlockedCellObstacles.Add(new LogicStaticCollisionObstacle(
                i + 1,
                LogicStaticCollisionObstacleKind.Box,
                new FixVector2(
                    localCellX * s_CollisionXAxisScale,
                    localCellY * s_CollisionZAxisScale),
                halfExtents,
                Fix64.Zero));
        }

        s_BlockedCellObstacleVersion = s_OwnerVersion;
        s_BlockedCellObstacleAllowedFactionId = allowedFactionId;
    }

    private static FixVector2 WorldToCollisionLocal(FixVector2 worldPosition)
    {
        FixVector2 delta = WorldDeltaFromAuthoredOrigin(worldPosition);
        return new FixVector2(
            FixVector2.Dot(delta, s_CollisionXAxis),
            FixVector2.Dot(delta, s_CollisionZAxis));
    }

    private static FixVector2 CollisionLocalToWorld(FixVector2 localPosition)
    {
        FixVector2 offset = s_CollisionXAxis * localPosition.x
                            + s_CollisionZAxis * localPosition.y;
        return new FixVector2(
            NavigationGridFixedMath.GridRawToFix64(
                checked(s_OriginXGridRaw + NavigationGridFixedMath.Fix64ToGridRaw(offset.x))),
            NavigationGridFixedMath.GridRawToFix64(
                checked(s_OriginZGridRaw + NavigationGridFixedMath.Fix64ToGridRaw(offset.y))));
    }

    private static FixVector2 WorldDeltaFromAuthoredOrigin(FixVector2 worldPosition)
    {
        return new FixVector2(
            NavigationGridFixedMath.GridRawToFix64(
                checked(NavigationGridFixedMath.Fix64ToGridRaw(worldPosition.x) - s_OriginXGridRaw)),
            NavigationGridFixedMath.GridRawToFix64(
                checked(NavigationGridFixedMath.Fix64ToGridRaw(worldPosition.y) - s_OriginZGridRaw)));
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add(IsInitialized);
        if (!IsInitialized)
            return;
        hasher.Add(s_AuthorityHash);
        hasher.Add(s_StrongholdIdByCell.Count);
        s_DeterministicStrongholdIds.Clear();
        foreach (string strongholdId in s_OwnerFactionByStrongholdId.Keys)
            s_DeterministicStrongholdIds.Add(strongholdId);
        s_DeterministicStrongholdIds.Sort(s_DeterministicStrongholdIdComparison);
        hasher.Add(s_DeterministicStrongholdIds.Count);
        for (int i = 0; i < s_DeterministicStrongholdIds.Count; i++)
        {
            string strongholdId = s_DeterministicStrongholdIds[i];
            hasher.Add(strongholdId);
            hasher.Add(s_OwnerFactionByStrongholdId[strongholdId]);
        }
    }

    private static ulong ComputeAuthorityHash(
        long originXGridRaw,
        long originZGridRaw,
        FixVector2 localXAxis,
        FixVector2 localZAxis,
        long cellSizeGridRaw,
        IReadOnlyList<LogicStrongholdCellDefinition> ordered)
    {
        var hasher = new LogicStateHasher();
        hasher.Add(0x5354524F4E474D50UL);
        hasher.Add(originXGridRaw);
        hasher.Add(originZGridRaw);
        hasher.Add(localXAxis.x.RawValue);
        hasher.Add(localXAxis.y.RawValue);
        hasher.Add(localZAxis.x.RawValue);
        hasher.Add(localZAxis.y.RawValue);
        hasher.Add(cellSizeGridRaw);
        hasher.Add(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            hasher.Add(ordered[i].StrongholdId);
            hasher.Add(ordered[i].X);
            hasher.Add(ordered[i].Y);
            hasher.Add(ordered[i].OwnerFactionId);
        }
        return hasher.Hash;
    }

    private static int CompareCells(LogicStrongholdCellDefinition left, LogicStrongholdCellDefinition right)
    {
        int x = left.X.CompareTo(right.X);
        if (x != 0)
            return x;
        int y = left.Y.CompareTo(right.Y);
        return y != 0 ? y : string.CompareOrdinal(left.StrongholdId, right.StrongholdId);
    }

    private static int RoundToIntMidpointToEven(Fix64 localCoordinate, long cellSizeGridRaw)
    {
        long value = NavigationGridFixedMath.Fix64ToGridRaw(localCoordinate);
        long whole = value / cellSizeGridRaw;
        long remainder = value % cellSizeGridRaw;
        long absoluteRemainder = remainder < 0 ? -remainder : remainder;
        long half = cellSizeGridRaw / 2;
        bool exactHalf = (cellSizeGridRaw & 1L) == 0L && absoluteRemainder == half;
        if (absoluteRemainder > half || (exactHalf && (whole & 1L) != 0L))
            whole += remainder < 0 ? -1L : 1L;
        if (whole < int.MinValue || whole > int.MaxValue)
            throw new OverflowException($"LogicStrongholdMap grid coordinate is outside Int32. raw={value}.");
        return (int)whole;
    }

    private static Fix64 RequireExactFixedAxis(long gridRaw, string parameterName)
    {
        Fix64 value = NavigationGridFixedMath.GridRawToFix64(gridRaw);
        if (NavigationGridFixedMath.Fix64ToGridRaw(value) != gridRaw)
        {
            throw new InvalidOperationException(
                $"LogicStrongholdMap authored grid axis '{parameterName}' cannot be represented exactly by Fix64.");
        }

        return value;
    }

}
