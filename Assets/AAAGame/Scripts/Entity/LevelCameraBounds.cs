using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public struct LevelCameraBoundsSpan
{
    public LevelCameraBoundsSpan(int row, int minColumn, int maxColumn)
    {
        Row = row;
        MinColumn = minColumn;
        MaxColumn = maxColumn;
    }

    public int Row;
    public int MinColumn;
    public int MaxColumn;
}

public sealed class LevelCameraBounds : MonoBehaviour
{
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private LevelCameraBoundsSpan[] spans = Array.Empty<LevelCameraBoundsSpan>();

    public float CellSize => cellSize;
    public LevelCameraBoundsSpan[] Spans => spans;

    public void SetData(float authoredCellSize, IEnumerable<WallGridCell> authoredCells)
    {
        if (authoredCellSize <= 0f || float.IsNaN(authoredCellSize) || float.IsInfinity(authoredCellSize))
            throw new ArgumentOutOfRangeException(nameof(authoredCellSize));
        if (authoredCells == null)
            throw new ArgumentNullException(nameof(authoredCells));

        WallGridCell[] cells = authoredCells.Distinct().OrderBy(cell => cell).ToArray();
        if (cells.Length == 0)
            throw new InvalidOperationException("Level camera bounds require at least one walkable cell.");

        var builtSpans = new List<LevelCameraBoundsSpan>();
        int row = cells[0].Y;
        int minColumn = cells[0].X;
        int maxColumn = cells[0].X;
        for (int i = 1; i < cells.Length; i++)
        {
            WallGridCell cell = cells[i];
            if (cell.Y == row && cell.X == maxColumn + 1)
            {
                maxColumn = cell.X;
                continue;
            }

            builtSpans.Add(new LevelCameraBoundsSpan(row, minColumn, maxColumn));
            row = cell.Y;
            minColumn = cell.X;
            maxColumn = cell.X;
        }

        builtSpans.Add(new LevelCameraBoundsSpan(row, minColumn, maxColumn));
        cellSize = authoredCellSize;
        spans = builtSpans.ToArray();
    }

    public Vector3 ClampWorldPoint(Vector3 worldPoint)
    {
        if (cellSize <= 0f || spans == null || spans.Length == 0)
            throw new InvalidOperationException("LevelCameraBounds has not been configured.");

        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        Vector2 point = new Vector2(localPoint.x, localPoint.z);
        Vector2 closest = default;
        float closestDistance = float.PositiveInfinity;
        float halfCell = cellSize * 0.5f;

        for (int i = 0; i < spans.Length; i++)
        {
            LevelCameraBoundsSpan span = spans[i];
            float minX = span.MinColumn * cellSize - halfCell;
            float maxX = span.MaxColumn * cellSize + halfCell;
            float minZ = span.Row * cellSize - halfCell;
            float maxZ = span.Row * cellSize + halfCell;
            Vector2 candidate = new Vector2(
                Mathf.Clamp(point.x, minX, maxX),
                Mathf.Clamp(point.y, minZ, maxZ));
            float distance = (candidate - point).sqrMagnitude;
            if (distance >= closestDistance)
                continue;

            closestDistance = distance;
            closest = candidate;
            if (distance <= Mathf.Epsilon)
                break;
        }

        localPoint.x = closest.x;
        localPoint.z = closest.y;
        return transform.TransformPoint(localPoint);
    }
}
