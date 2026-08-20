using System;
using UnityEngine;

[Flags]
public enum WallConnectionDirection
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Down = 1 << 2,
    Up = 1 << 3,
}

[Serializable]
public struct WallGridCell : IEquatable<WallGridCell>, IComparable<WallGridCell>
{
    public WallGridCell(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X;
    public int Y;

    public bool Equals(WallGridCell other) => X == other.X && Y == other.Y;
    public override bool Equals(object obj) => obj is WallGridCell other && Equals(other);
    public override int GetHashCode() => unchecked((X * 397) ^ Y);

    public int CompareTo(WallGridCell other)
    {
        int result = Y.CompareTo(other.Y);
        return result != 0 ? result : X.CompareTo(other.X);
    }

    public static WallGridCell operator +(WallGridCell cell, WallGridCell offset) =>
        new WallGridCell(cell.X + offset.X, cell.Y + offset.Y);

    public override string ToString() => $"({X},{Y})";
}

[Serializable]
public struct WallGridHeightCell
{
    public WallGridHeightCell(int x, int y, int heightLevel)
    {
        X = x;
        Y = y;
        HeightLevel = heightLevel;
    }

    public int X;
    public int Y;
    public int HeightLevel;
    public WallGridCell Cell => new WallGridCell(X, Y);
}

public sealed class WallGridAuthoring : MonoBehaviour
{
    [SerializeField] private int width;
    [SerializeField] private int height;
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private WallGridHeightCell[] walkableCells = Array.Empty<WallGridHeightCell>();
    [SerializeField] private WallGridCell[] previewCells = Array.Empty<WallGridCell>();
    [SerializeField] private WallGridCell[] presetWallCells = Array.Empty<WallGridCell>();

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;
    public WallGridHeightCell[] WalkableCells => walkableCells;
    public WallGridCell[] PreviewCells => previewCells;
    public WallGridCell[] PresetWallCells => presetWallCells;

    public void SetData(
        int authoredWidth,
        int authoredHeight,
        float authoredCellSize,
        WallGridHeightCell[] authoredWalkableCells,
        WallGridCell[] authoredPreviewCells,
        WallGridCell[] authoredPresetWallCells)
    {
        if (authoredWidth <= 0 || authoredHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(authoredWidth), "Wall grid dimensions must be positive.");
        if (authoredCellSize <= 0f || float.IsNaN(authoredCellSize) || float.IsInfinity(authoredCellSize))
            throw new ArgumentOutOfRangeException(nameof(authoredCellSize));
        width = authoredWidth;
        height = authoredHeight;
        cellSize = authoredCellSize;
        walkableCells = authoredWalkableCells != null
            ? (WallGridHeightCell[])authoredWalkableCells.Clone()
            : throw new ArgumentNullException(nameof(authoredWalkableCells));
        previewCells = authoredPreviewCells != null
            ? (WallGridCell[])authoredPreviewCells.Clone()
            : throw new ArgumentNullException(nameof(authoredPreviewCells));
        presetWallCells = authoredPresetWallCells != null
            ? (WallGridCell[])authoredPresetWallCells.Clone()
            : throw new ArgumentNullException(nameof(authoredPresetWallCells));
    }
}
