using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class FlowFieldCrowdMovementSystem
{
    private static float ResolveGridPathCostToAnyOrInfinity(int startX, int startY, Vector2Int[] goalCells)
    {
        if (_world == null || goalCells == null || goalCells.Length == 0)
            return float.PositiveInfinity;
        if (startX < 0 || startX >= _world.Width || startY < 0 || startY >= _world.Height || !_world.IsWalkable(startX, startY))
            return float.PositiveInfinity;

        bool[] targets = new bool[_world.Width * _world.Height];
        bool hasTarget = false;
        for (int i = 0; i < goalCells.Length; i++)
        {
            Vector2Int cell = goalCells[i];
            if (cell.x < 0 || cell.x >= _world.Width || cell.y < 0 || cell.y >= _world.Height || !_world.IsWalkable(cell.x, cell.y))
                continue;

            targets[_world.GetIndex(cell.x, cell.y)] = true;
            hasTarget = true;
        }

        if (!hasTarget)
            return float.PositiveInfinity;

        return ResolveGridPathCostToTargetsOrInfinity(startX, startY, targets);
    }

    private static float ResolveGridPathCostFromAnyToCellOrInfinity(Vector2Int[] startCells, int goalX, int goalY)
    {
        if (_world == null || startCells == null || startCells.Length == 0)
            return float.PositiveInfinity;

        float best = float.PositiveInfinity;
        for (int i = 0; i < startCells.Length; i++)
        {
            Vector2Int start = startCells[i];
            float cost = ResolveGridPathCostOrInfinity(start.x, start.y, goalX, goalY);
            if (cost < best)
                best = cost;
        }

        return best;
    }

    private static float ResolveGridPathCostOrInfinity(int startX, int startY, int goalX, int goalY)
    {
        if (_world == null)
            return float.PositiveInfinity;
        if (goalX < 0 || goalX >= _world.Width || goalY < 0 || goalY >= _world.Height || !_world.IsWalkable(goalX, goalY))
            return float.PositiveInfinity;

        bool[] targets = new bool[_world.Width * _world.Height];
        targets[_world.GetIndex(goalX, goalY)] = true;
        return ResolveGridPathCostToTargetsOrInfinity(startX, startY, targets);
    }

    private static float ResolveGridPathCostToTargetsOrInfinity(int startX, int startY, bool[] targets)
    {
        if (_world == null || targets == null || targets.Length != _world.Width * _world.Height)
            return float.PositiveInfinity;
        if (startX < 0 || startX >= _world.Width || startY < 0 || startY >= _world.Height || !_world.IsWalkable(startX, startY))
            return float.PositiveInfinity;

        int startIndex = _world.GetIndex(startX, startY);
        if (targets[startIndex])
            return 0f;

        float[] costs = RentIntegrationArray(_world.Width * _world.Height);
        InitializeIntegrationField(costs);
        MinHeap openSet = new MinHeap();
        costs[startIndex] = 0f;
        openSet.Push(startIndex, 0f);
        int guard = _world.Width * _world.Height * 4;
        try
        {
            while (openSet.Count > 0 && guard-- > 0)
            {
                QueueNode node = openSet.Pop();
                if (node.Cost > costs[node.Index] + 0.001f)
                    continue;
                if (targets[node.Index])
                    return node.Cost;

                int worldX = node.Index % _world.Width;
                int worldY = node.Index / _world.Width;
                for (int i = 0; i < NeighborOffsetX.Length; i++)
                {
                    int nextX = worldX + NeighborOffsetX[i];
                    int nextY = worldY + NeighborOffsetY[i];
                    if (nextX < 0 || nextX >= _world.Width || nextY < 0 || nextY >= _world.Height)
                        continue;
                    if (!_world.IsWalkable(nextX, nextY) || !CanTraverseNeighborCells(_world, worldX, worldY, nextX, nextY))
                        continue;

                    int nextIndex = _world.GetIndex(nextX, nextY);
                    float stepDistance = Mathf.Abs(NeighborOffsetX[i]) + Mathf.Abs(NeighborOffsetY[i]) == 2 ? 1.4142135f : 1f;
                    float stepCost = Mathf.Max(ResolveCellIntegrationCost(_world, nextX, nextY), 0.001f) * stepDistance;
                    float newCost = node.Cost + stepCost;
                    if (!IsSignificantIntegrationImprovement(newCost, costs[nextIndex]))
                        continue;

                    costs[nextIndex] = newCost;
                    openSet.Push(nextIndex, newCost);
                }
            }
        }
        finally
        {
            ReturnIntegrationArray(costs);
        }

        return float.PositiveInfinity;
    }

    private static float ResolveGridPathLengthOrInfinity(Vector3 fromPosition, Vector3 toPosition, int agentTypeId)
    {
        if (_world == null)
            return float.PositiveInfinity;
        return TryEstimateNavigationDistance(fromPosition, toPosition, agentTypeId >= 0 ? agentTypeId : _world.AgentTypeId, out float distance)
            ? distance
            : float.PositiveInfinity;
    }

    private static string FormatDiagnosticCost(float cost)
    {
        return float.IsPositiveInfinity(cost) ? "INF" : cost.ToString("F3");
    }

}

