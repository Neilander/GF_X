using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct WallGeometryBlock
{
    public WallGeometryBlock(Vector3 centerOffset, Vector3 scale)
    {
        CenterOffset = centerOffset;
        Scale = scale;
    }

    public Vector3 CenterOffset { get; }
    public Vector3 Scale { get; }
}

public static class WallBranchGeometry
{
    private static readonly Vector3 s_HorizontalScale = new Vector3(1f, 1.35f, 0.32f);
    private static readonly Vector3 s_VerticalScale = new Vector3(0.32f, 1.35f, 1f);
    private static readonly Vector3 s_CenterScale = new Vector3(0.32f, 1.35f, 0.32f);
    private static readonly Vector3 s_HorizontalArmScale = new Vector3(0.34f, 1.35f, 0.32f);
    private static readonly Vector3 s_VerticalArmScale = new Vector3(0.32f, 1.35f, 0.34f);

    public static IReadOnlyList<WallGeometryBlock> BuildConnectedBlocks(WallConnectionDirection connections)
    {
        return BuildConnectedBlocks(connections, 1f);
    }

    public static IReadOnlyList<WallGeometryBlock> BuildConnectedBlocks(
        WallConnectionDirection connections,
        float cellSize)
    {
        if (cellSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(cellSize));

        bool horizontal = HasHorizontal(connections);
        bool vertical = HasVertical(connections);
        if (!vertical)
            return new[] { ScaleBlock(new WallGeometryBlock(Vector3.zero, s_HorizontalScale), cellSize) };
        if (!horizontal)
            return new[] { ScaleBlock(new WallGeometryBlock(Vector3.zero, s_VerticalScale), cellSize) };

        var result = new List<WallGeometryBlock>(5)
        {
            new WallGeometryBlock(Vector3.zero, s_CenterScale),
        };
        AddArm(result, connections, WallConnectionDirection.Left, Vector3.left, s_HorizontalArmScale);
        AddArm(result, connections, WallConnectionDirection.Right, Vector3.right, s_HorizontalArmScale);
        AddArm(result, connections, WallConnectionDirection.Down, Vector3.back, s_VerticalArmScale);
        AddArm(result, connections, WallConnectionDirection.Up, Vector3.forward, s_VerticalArmScale);
        if (!Mathf.Approximately(cellSize, 1f))
        {
            for (int i = 0; i < result.Count; i++)
                result[i] = ScaleBlock(result[i], cellSize);
        }
        return result;
    }

    private static WallGeometryBlock ScaleBlock(WallGeometryBlock block, float cellSize)
    {
        return new WallGeometryBlock(block.CenterOffset * cellSize, block.Scale * cellSize);
    }

    private static void AddArm(
        ICollection<WallGeometryBlock> result,
        WallConnectionDirection connections,
        WallConnectionDirection direction,
        Vector3 axis,
        Vector3 scale)
    {
        if ((connections & direction) != 0)
            result.Add(new WallGeometryBlock(axis * 0.33f, scale));
    }

    private static bool HasHorizontal(WallConnectionDirection connections) =>
        (connections & (WallConnectionDirection.Left | WallConnectionDirection.Right)) != 0;

    private static bool HasVertical(WallConnectionDirection connections) =>
        (connections & (WallConnectionDirection.Down | WallConnectionDirection.Up)) != 0;
}
