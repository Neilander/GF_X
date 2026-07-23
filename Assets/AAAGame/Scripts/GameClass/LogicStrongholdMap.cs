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
    private static FixVector2 s_Origin;
    private static FixVector2 s_LocalXAxis;
    private static FixVector2 s_LocalZAxis;
    private static Fix64 s_CellSize;
    private static Fix64 s_Determinant;
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
        if (IsInitialized)
            throw new InvalidOperationException("LogicStrongholdMap is already initialized.");
        if (cellSize <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize.RawValue, "Stronghold map cell size must be positive.");
        if (cells == null)
            throw new ArgumentNullException(nameof(cells));

        Fix64 determinant = localXAxis.x * localZAxis.y - localXAxis.y * localZAxis.x;
        if (determinant == Fix64.Zero)
            throw new InvalidOperationException("LogicStrongholdMap transform is degenerate.");

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
        foreach (KeyValuePair<Cell, string> pair in strongholdIdByCell)
            s_StrongholdIdByCell.Add(pair.Key, pair.Value);
        s_OwnerFactionByStrongholdId.Clear();
        foreach (KeyValuePair<string, int> pair in ownerFactionByStrongholdId)
            s_OwnerFactionByStrongholdId.Add(pair.Key, pair.Value);
        s_Origin = origin;
        s_LocalXAxis = localXAxis;
        s_LocalZAxis = localZAxis;
        s_CellSize = cellSize;
        s_Determinant = determinant;
        s_AuthorityHash = ComputeAuthorityHash(
            origin,
            localXAxis,
            localZAxis,
            cellSize,
            ordered);
        IsInitialized = true;
    }

    public static void Clear()
    {
        s_StrongholdIdByCell.Clear();
        s_OwnerFactionByStrongholdId.Clear();
        s_Origin = FixVector2.Zero;
        s_LocalXAxis = FixVector2.Zero;
        s_LocalZAxis = FixVector2.Zero;
        s_CellSize = Fix64.Zero;
        s_Determinant = Fix64.Zero;
        s_AuthorityHash = 0;
        IsInitialized = false;
    }

    public static bool TryResolveStrongholdId(FixVector2 worldPosition, out string strongholdId)
    {
        EnsureInitialized();

        FixVector2 delta = worldPosition - s_Origin;
        Fix64 localX = (delta.x * s_LocalZAxis.y - delta.y * s_LocalZAxis.x) / s_Determinant;
        Fix64 localZ = (s_LocalXAxis.x * delta.y - s_LocalXAxis.y * delta.x) / s_Determinant;
        int cellX = RoundToIntMidpointToEven(localX / s_CellSize);
        int cellY = RoundToIntMidpointToEven(localZ / s_CellSize);
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
        s_OwnerFactionByStrongholdId[strongholdId] = ownerFactionId;
    }

    public static void EnsureInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("LogicStrongholdMap is not initialized.");
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
        var strongholdIds = new List<string>(s_OwnerFactionByStrongholdId.Keys);
        strongholdIds.Sort(StringComparer.Ordinal);
        hasher.Add(strongholdIds.Count);
        for (int i = 0; i < strongholdIds.Count; i++)
        {
            hasher.Add(strongholdIds[i]);
            hasher.Add(s_OwnerFactionByStrongholdId[strongholdIds[i]]);
        }
    }

    private static ulong ComputeAuthorityHash(
        FixVector2 origin,
        FixVector2 localXAxis,
        FixVector2 localZAxis,
        Fix64 cellSize,
        IReadOnlyList<LogicStrongholdCellDefinition> ordered)
    {
        var hasher = new LogicStateHasher();
        hasher.Add(0x5354524F4E474D50UL);
        hasher.Add(origin.x.RawValue);
        hasher.Add(origin.y.RawValue);
        hasher.Add(localXAxis.x.RawValue);
        hasher.Add(localXAxis.y.RawValue);
        hasher.Add(localZAxis.x.RawValue);
        hasher.Add(localZAxis.y.RawValue);
        hasher.Add(cellSize.RawValue);
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

    private static int RoundToIntMidpointToEven(Fix64 value)
    {
        long whole = value.RawValue / Fix64.One.RawValue;
        long remainder = value.RawValue % Fix64.One.RawValue;
        long absoluteRemainder = remainder < 0 ? -remainder : remainder;
        long half = Fix64.One.RawValue / 2;
        if (absoluteRemainder > half || (absoluteRemainder == half && (whole & 1L) != 0L))
            whole += remainder < 0 ? -1L : 1L;
        if (whole < int.MinValue || whole > int.MaxValue)
            throw new OverflowException($"LogicStrongholdMap grid coordinate is outside Int32. raw={value.RawValue}.");
        return (int)whole;
    }

}
