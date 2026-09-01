using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using AAAGame.FlowPath;
using AAAGame.MiniMap.FOG3;
using GameFramework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Debug = UnityEngine.Debug;
using MainThreadFrameProfiler = UnityGameFramework.Runtime.MainThreadFrameProfiler;
using MainThreadPerfScope = UnityGameFramework.Runtime.MainThreadPerfScope;

public static partial class FlowFieldCrowdMovementSystem
{
    private static bool TryFindNearestWalkable(NavigationWorld world, int startX, int startY, int radius, out int resultX, out int resultY)
    {
        for (int r = 1; r <= radius; r++)
        {
            for (int y = -r; y <= r; y++)
            {
                for (int x = -r; x <= r; x++)
                {
                    int nx = startX + x;
                    int ny = startY + y;
                    if (!world.IsWalkable(nx, ny))
                        continue;

                    resultX = nx;
                    resultY = ny;
                    return true;
                }
            }
        }

        resultX = 0;
        resultY = 0;
        return false;
    }

    private static bool TryResolveNearbyStartWalkableFixed(
        NavigationWorld world,
        FixVector2 position,
        int startX,
        int startY,
        out int resultX,
        out int resultY)
    {
        resultX = startX;
        resultY = startY;
        if (world == null)
            throw new InvalidOperationException("TryResolveNearbyStartWalkableFixed failed: world is null.");

        Fix64 maxSnapDistance = ResolveNearbyStartMaxSnapDistanceFixed(
            world,
            IsRuntimeRasterOnlyBlockedCell(world, startX, startY));
        Fix64 bestDistanceSq = maxSnapDistance * maxSnapDistance;
        bool found = false;
        int searchRadiusInCells = Math.Max(1, NavigationGridFixedMath.DivideCeilingByCellSize(maxSnapDistance, world.CellSizeGridRaw));
        for (int y = startY - searchRadiusInCells; y <= startY + searchRadiusInCells; y++)
        {
            for (int x = startX - searchRadiusInCells; x <= startX + searchRadiusInCells; x++)
            {
                if (!world.IsWalkable(x, y))
                    continue;

                world.GetGridCellBoundsFixed(x, y, out FixVector2 minimum, out FixVector2 maximum);
                Fix64 dx = position.x < minimum.x
                    ? minimum.x - position.x
                    : position.x > maximum.x ? position.x - maximum.x : Fix64.Zero;
                Fix64 dz = position.y < minimum.y
                    ? minimum.y - position.y
                    : position.y > maximum.y ? position.y - maximum.y : Fix64.Zero;
                Fix64 distanceSq = dx * dx + dz * dz;
                if (distanceSq > bestDistanceSq)
                    continue;

                bestDistanceSq = distanceSq;
                resultX = x;
                resultY = y;
                found = true;
            }
        }

        return found;
    }

    private static bool TryResolveNearbyStartWalkable(NavigationWorld world, Vector3 position, int startX, int startY, out int resultX, out int resultY)
    {
        resultX = startX;
        resultY = startY;
        if (world == null)
            throw new InvalidOperationException("TryResolveNearbyStartWalkable failed: world is null.");

        Fix64 maxSnapDistance = ResolveNearbyStartMaxSnapDistanceFixed(
            world,
            IsRuntimeRasterOnlyBlockedCell(world, startX, startY));
        Fix64 bestDistanceSq = maxSnapDistance * maxSnapDistance;
        FixVector2 positionFixed = new FixVector2((Fix64)position.x, (Fix64)position.z);
        bool found = false;
        int searchRadiusInCells = Math.Max(1, NavigationGridFixedMath.DivideCeilingByCellSize(maxSnapDistance, world.CellSizeGridRaw));
        for (int y = startY - searchRadiusInCells; y <= startY + searchRadiusInCells; y++)
        {
            for (int x = startX - searchRadiusInCells; x <= startX + searchRadiusInCells; x++)
            {
                if (!world.IsWalkable(x, y))
                    continue;

                world.GetGridCellBoundsFixed(x, y, out FixVector2 minimum, out FixVector2 maximum);
                Fix64 dx = positionFixed.x < minimum.x
                    ? minimum.x - positionFixed.x
                    : positionFixed.x > maximum.x ? positionFixed.x - maximum.x : Fix64.Zero;
                Fix64 dz = positionFixed.y < minimum.y
                    ? minimum.y - positionFixed.y
                    : positionFixed.y > maximum.y ? positionFixed.y - maximum.y : Fix64.Zero;
                Fix64 distanceSq = dx * dx + dz * dz;
                if (distanceSq > bestDistanceSq)
                    continue;

                bestDistanceSq = distanceSq;
                resultX = x;
                resultY = y;
                found = true;
            }
        }

        return found;
    }

    private static bool IsRuntimeRasterOnlyBlockedCell(NavigationWorld world, int x, int y)
    {
        if (world == null)
            throw new InvalidOperationException("IsRuntimeRasterOnlyBlockedCell failed: world is null.");
        if (x < 0 || x >= world.Width || y < 0 || y >= world.Height)
            return false;
        if (world.BaseWalkableMask == null || world.BaseWalkableMask.Length != world.Width * world.Height)
            throw new InvalidOperationException("IsRuntimeRasterOnlyBlockedCell failed: base walkable mask is invalid.");

        int index = world.GetIndex(x, y);
        return world.BaseWalkableMask[index] && !world.WalkableMask[index];
    }

    private static Fix64 ResolveNearbyStartMaxSnapDistanceFixed(NavigationWorld world, bool allowRuntimeRasterHalo)
    {
        if (world == null)
            throw new InvalidOperationException("ResolveNearbyStartMaxSnapDistanceFixed failed: world is null.");

        Fix64 cellSize = world.CellSizeFixed;
        Fix64 authoredBlockerSnapDistance = Fix64.Max(
            cellSize * Fix64.FromRaw(1844),
            Fix64.FromRaw(1024));
        if (!allowRuntimeRasterHalo)
            return authoredBlockerSnapDistance;

        Fix64 rasterClearance = Fix64.Max(
            Fix64.Zero,
            world.AgentRadiusFixed - cellSize * Fix64.FromRaw(820));
        Fix64 maximumAxisGap = rasterClearance + cellSize * Fix64.FromRaw(2048);
        Fix64 rasterDiagonalGap = Fix64.Sqrt(maximumAxisGap * maximumAxisGap * (Fix64)2);
        return Fix64.Max(authoredBlockerSnapDistance, rasterDiagonalGap);
    }

    private static bool HasGridLineOfSight(NavigationWorld world, int x0, int y0, int x1, int y1)
    {
        return HasGridLineOfSight(world, x0, y0, x1, y1, allowTargetSoftCost: false);
    }

    private static bool HasGridLineOfSight(NavigationWorld world, int x0, int y0, int x1, int y1, bool allowTargetSoftCost)
    {
        return HasSupercoverGridLineOfSight(
            world,
            x0,
            y0,
            x1,
            y1,
            GridLineOfSightCostMode.Strict,
            allowTargetSoftCost,
            maxAllowedCost: 1);
    }

    private static bool HasFixedGridLineOfSight(
        NavigationWorld world,
        FixVector2 from,
        FixVector2 to,
        bool allowTargetSoftCost)
    {
        if (!world.WorldToGridFixed(from, out int startX, out int startY)
            || !world.WorldToGridFixed(to, out int goalX, out int goalY))
        {
            return false;
        }
        if (!IsGridLineOfSightCellPassable(
                world,
                startX,
                startY,
                goalX,
                goalY,
                GridLineOfSightCostMode.DirectShortcut,
                allowTargetSoftCost,
                maxAllowedCost: 1))
        {
            return false;
        }
        if (startX == goalX && startY == goalY)
            return true;

        long fromXGridRaw = NavigationGridFixedMath.Fix64ToGridRaw(from.x);
        long fromYGridRaw = NavigationGridFixedMath.Fix64ToGridRaw(from.y);
        long toXGridRaw = NavigationGridFixedMath.Fix64ToGridRaw(to.x);
        long toYGridRaw = NavigationGridFixedMath.Fix64ToGridRaw(to.y);
        long displacementXGridRaw = checked(toXGridRaw - fromXGridRaw);
        long displacementYGridRaw = checked(toYGridRaw - fromYGridRaw);
        long absoluteXGridRaw = Math.Abs(displacementXGridRaw);
        long absoluteYGridRaw = Math.Abs(displacementYGridRaw);
        int stepX = Math.Sign(displacementXGridRaw);
        int stepY = Math.Sign(displacementYGridRaw);

        int currentX = startX;
        int currentY = startY;
        int guard = (Math.Abs(goalX - startX) + Math.Abs(goalY - startY) + 4) * 4;
        for (int i = 0; i < guard; i++)
        {
            if (currentX == goalX && currentY == goalY)
                return true;

            int stepAxis = ResolveFixedGridRayStepAxis(
                world,
                fromXGridRaw,
                fromYGridRaw,
                absoluteXGridRaw,
                absoluteYGridRaw,
                currentX,
                currentY,
                goalX,
                goalY,
                stepX,
                stepY,
                out _,
                out _);
            if (stepAxis == 0)
            {
                int sideX = currentX + stepX;
                int sideY = currentY + stepY;
                if (!IsGridLineOfSightStepPassable(world, currentX, currentY, sideX, currentY, goalX, goalY, GridLineOfSightCostMode.DirectShortcut, allowTargetSoftCost, 1)
                    || !IsGridLineOfSightStepPassable(world, currentX, currentY, currentX, sideY, goalX, goalY, GridLineOfSightCostMode.DirectShortcut, allowTargetSoftCost, 1)
                    || !IsGridLineOfSightStepPassable(world, sideX, currentY, sideX, sideY, goalX, goalY, GridLineOfSightCostMode.DirectShortcut, allowTargetSoftCost, 1)
                    || !IsGridLineOfSightStepPassable(world, currentX, sideY, sideX, sideY, goalX, goalY, GridLineOfSightCostMode.DirectShortcut, allowTargetSoftCost, 1))
                {
                    return false;
                }

                currentX = sideX;
                currentY = sideY;
                continue;
            }

            int nextX = currentX;
            int nextY = currentY;
            if (stepAxis < 0)
            {
                nextX += stepX;
            }
            else
            {
                nextY += stepY;
            }

            if (!IsGridLineOfSightStepPassable(
                    world,
                    currentX,
                    currentY,
                    nextX,
                    nextY,
                    goalX,
                    goalY,
                    GridLineOfSightCostMode.DirectShortcut,
                    allowTargetSoftCost,
                    maxAllowedCost: 1))
            {
                return false;
            }

            currentX = nextX;
            currentY = nextY;
        }

        throw new InvalidOperationException(
            $"HasFixedGridLineOfSight exceeded traversal guard start=({startX},{startY}), goal=({goalX},{goalY}), guard={guard}.");
    }

    private static string BuildFixedGridLineOfSightDecisionDiagnostic(
        NavigationWorld world,
        FixVector2 from,
        FixVector2 to,
        bool allowTargetSoftCost)
    {
        if (world == null)
            return "world-null";
        if (!world.WorldToGridFixed(from, out int startX, out int startY)
            || !world.WorldToGridFixed(to, out int goalX, out int goalY))
        {
            return "endpoint-out";
        }
        if (!IsGridLineOfSightCellPassable(
                world,
                startX,
                startY,
                goalX,
                goalY,
                GridLineOfSightCostMode.DirectShortcut,
                allowTargetSoftCost,
                maxAllowedCost: 1))
        {
            return $"start-blocked cell=({startX},{startY})";
        }
        if (startX == goalX && startY == goalY)
            return "clear-same-cell";

        long fromXGridRaw = NavigationGridFixedMath.Fix64ToGridRaw(from.x);
        long fromYGridRaw = NavigationGridFixedMath.Fix64ToGridRaw(from.y);
        long toXGridRaw = NavigationGridFixedMath.Fix64ToGridRaw(to.x);
        long toYGridRaw = NavigationGridFixedMath.Fix64ToGridRaw(to.y);
        long displacementXGridRaw = checked(toXGridRaw - fromXGridRaw);
        long displacementYGridRaw = checked(toYGridRaw - fromYGridRaw);
        long absoluteXGridRaw = Math.Abs(displacementXGridRaw);
        long absoluteYGridRaw = Math.Abs(displacementYGridRaw);
        int stepX = Math.Sign(displacementXGridRaw);
        int stepY = Math.Sign(displacementYGridRaw);

        int currentX = startX;
        int currentY = startY;
        int guard = (Math.Abs(goalX - startX) + Math.Abs(goalY - startY) + 4) * 4;
        for (int i = 0; i < guard; i++)
        {
            if (currentX == goalX && currentY == goalY)
                return "clear";

            int stepAxis = ResolveFixedGridRayStepAxis(
                world,
                fromXGridRaw,
                fromYGridRaw,
                absoluteXGridRaw,
                absoluteYGridRaw,
                currentX,
                currentY,
                goalX,
                goalY,
                stepX,
                stepY,
                out long nextXBoundaryDistanceRaw,
                out long nextYBoundaryDistanceRaw);
            if (stepAxis == 0)
            {
                int sideX = currentX + stepX;
                int sideY = currentY + stepY;
                if (!IsGridLineOfSightStepPassable(world, currentX, currentY, sideX, currentY, goalX, goalY, GridLineOfSightCostMode.DirectShortcut, allowTargetSoftCost, 1))
                    return BuildFixedGridLineOfSightFailureDiagnostic(world, currentX, currentY, sideX, currentY, goalX, goalY, nextXBoundaryDistanceRaw, nextYBoundaryDistanceRaw, "corner-x");
                if (!IsGridLineOfSightStepPassable(world, currentX, currentY, currentX, sideY, goalX, goalY, GridLineOfSightCostMode.DirectShortcut, allowTargetSoftCost, 1))
                    return BuildFixedGridLineOfSightFailureDiagnostic(world, currentX, currentY, currentX, sideY, goalX, goalY, nextXBoundaryDistanceRaw, nextYBoundaryDistanceRaw, "corner-y");
                if (!IsGridLineOfSightStepPassable(world, sideX, currentY, sideX, sideY, goalX, goalY, GridLineOfSightCostMode.DirectShortcut, allowTargetSoftCost, 1))
                    return BuildFixedGridLineOfSightFailureDiagnostic(world, sideX, currentY, sideX, sideY, goalX, goalY, nextXBoundaryDistanceRaw, nextYBoundaryDistanceRaw, "corner-x-y");
                if (!IsGridLineOfSightStepPassable(world, currentX, sideY, sideX, sideY, goalX, goalY, GridLineOfSightCostMode.DirectShortcut, allowTargetSoftCost, 1))
                    return BuildFixedGridLineOfSightFailureDiagnostic(world, currentX, sideY, sideX, sideY, goalX, goalY, nextXBoundaryDistanceRaw, nextYBoundaryDistanceRaw, "corner-y-x");

                currentX = sideX;
                currentY = sideY;
                continue;
            }

            int nextX = currentX;
            int nextY = currentY;
            if (stepAxis < 0)
            {
                nextX += stepX;
            }
            else
            {
                nextY += stepY;
            }

            if (!IsGridLineOfSightStepPassable(
                    world,
                    currentX,
                    currentY,
                    nextX,
                    nextY,
                    goalX,
                    goalY,
                    GridLineOfSightCostMode.DirectShortcut,
                    allowTargetSoftCost,
                    maxAllowedCost: 1))
            {
                return BuildFixedGridLineOfSightFailureDiagnostic(
                    world,
                    currentX,
                    currentY,
                    nextX,
                    nextY,
                    goalX,
                    goalY,
                    nextXBoundaryDistanceRaw,
                    nextYBoundaryDistanceRaw,
                    "step");
            }

            currentX = nextX;
            currentY = nextY;
        }

        return $"guard-exceeded start=({startX},{startY}) goal=({goalX},{goalY}) guard={guard}";
    }

    private static int ResolveFixedGridRayStepAxis(
        NavigationWorld world,
        long fromXGridRaw,
        long fromYGridRaw,
        long absoluteXGridRaw,
        long absoluteYGridRaw,
        int currentX,
        int currentY,
        int goalX,
        int goalY,
        int stepX,
        int stepY,
        out long nextXBoundaryDistanceRaw,
        out long nextYBoundaryDistanceRaw)
    {
        if (currentX == goalX)
        {
            nextXBoundaryDistanceRaw = -1;
            nextYBoundaryDistanceRaw = ResolveFixedGridRayBoundaryDistanceRaw(
                world.OriginZGridRaw, world.CellSizeGridRaw, currentY, stepY, fromYGridRaw);
            return 1;
        }
        if (currentY == goalY)
        {
            nextXBoundaryDistanceRaw = ResolveFixedGridRayBoundaryDistanceRaw(
                world.OriginXGridRaw, world.CellSizeGridRaw, currentX, stepX, fromXGridRaw);
            nextYBoundaryDistanceRaw = -1;
            return -1;
        }

        nextXBoundaryDistanceRaw = ResolveFixedGridRayBoundaryDistanceRaw(
            world.OriginXGridRaw, world.CellSizeGridRaw, currentX, stepX, fromXGridRaw);
        nextYBoundaryDistanceRaw = ResolveFixedGridRayBoundaryDistanceRaw(
            world.OriginZGridRaw, world.CellSizeGridRaw, currentY, stepY, fromYGridRaw);
        return CompareNonNegativeFractions(
            nextXBoundaryDistanceRaw,
            absoluteXGridRaw,
            nextYBoundaryDistanceRaw,
            absoluteYGridRaw);
    }

    private static long ResolveFixedGridRayBoundaryDistanceRaw(
        long originGridRaw,
        long cellSizeGridRaw,
        int currentCell,
        int step,
        long fromGridRaw)
    {
        if (step == 0)
            throw new InvalidOperationException("ResolveFixedGridRayBoundaryDistanceRaw failed: step is zero before reaching the goal cell.");

        long boundaryGridRaw = step > 0
            ? checked(originGridRaw + checked((long)(currentCell + 1) * cellSizeGridRaw))
            : checked(originGridRaw + checked((long)currentCell * cellSizeGridRaw));
        long distanceGridRaw = step > 0
            ? checked(boundaryGridRaw - fromGridRaw)
            : checked(fromGridRaw - boundaryGridRaw);
        if (distanceGridRaw < 0)
        {
            throw new InvalidOperationException(
                $"ResolveFixedGridRayBoundaryDistanceRaw failed: next boundary is behind the ray origin cell={currentCell}, step={step}, distanceRaw={distanceGridRaw}.");
        }

        return distanceGridRaw;
    }

    private static int CompareNonNegativeFractions(
        long leftNumerator,
        long leftDenominator,
        long rightNumerator,
        long rightDenominator)
    {
        if (leftNumerator < 0 || rightNumerator < 0 || leftDenominator <= 0 || rightDenominator <= 0)
            throw new ArgumentOutOfRangeException(nameof(leftNumerator), "Fraction comparison requires non-negative numerators and positive denominators.");

        int direction = 1;
        while (true)
        {
            long leftQuotient = leftNumerator / leftDenominator;
            long rightQuotient = rightNumerator / rightDenominator;
            if (leftQuotient != rightQuotient)
                return direction * leftQuotient.CompareTo(rightQuotient);

            long leftRemainder = leftNumerator % leftDenominator;
            long rightRemainder = rightNumerator % rightDenominator;
            if (leftRemainder == 0 || rightRemainder == 0)
            {
                if (leftRemainder == rightRemainder)
                    return 0;
                return direction * (leftRemainder == 0 ? -1 : 1);
            }

            leftNumerator = leftDenominator;
            leftDenominator = leftRemainder;
            rightNumerator = rightDenominator;
            rightDenominator = rightRemainder;
            direction = -direction;
        }
    }

    private static string BuildFixedGridLineOfSightFailureDiagnostic(
        NavigationWorld world,
        int fromX,
        int fromY,
        int toX,
        int toY,
        int goalX,
        int goalY,
        long nextXBoundaryDistanceRaw,
        long nextYBoundaryDistanceRaw,
        string stage)
    {
        bool walkable = world.IsWalkable(toX, toY);
        bool cellPassable = IsGridLineOfSightCellPassable(
            world,
            toX,
            toY,
            goalX,
            goalY,
            GridLineOfSightCostMode.DirectShortcut,
            allowTargetSoftCost: true,
            maxAllowedCost: 1);
        bool rawLink = walkable && HasRawNeighborTraversal(world, fromX, fromY, toX, toY);
        bool traversable = walkable && CanTraverseNeighborCells(world, fromX, fromY, toX, toY);
        int cost = walkable ? GetCostFieldValueStrict(world, toX, toY) : -1;
        bool costStamp = walkable && IsCellAffectedByCostStamp(world, toX, toY);
        return $"blocked stage={stage} edge=({fromX},{fromY})->({toX},{toY}) " +
               $"walkable={walkable} cellPassable={cellPassable} cost={cost} costStamp={costStamp} " +
               $"rawLink={rawLink} traversable={traversable} nextBoundaryDistanceRaw=({nextXBoundaryDistanceRaw},{nextYBoundaryDistanceRaw})";
    }

    private static bool HasClearanceGridLineOfSight(
        NavigationWorld world,
        int x0,
        int y0,
        int x1,
        int y1,
        Vector3 from,
        Vector3 to,
        float agentRadius,
        bool allowTargetSoftCost)
    {
        if (!HasGridLineOfSight(world, x0, y0, x1, y1, allowTargetSoftCost))
            return false;

        Vector3 horizontal = to - from;
        horizontal.y = 0f;
        if (horizontal.sqrMagnitude <= 0.0001f)
            return true;

        float clearance = ResolveNavigationQueryClearance(world, ResolveNavigationSegmentClearance(world, agentRadius));
        if (!IsNavigationSegmentWalkable(
                world,
                from,
                horizontal,
                clearance,
                requireClearStart: true,
                includeRuntimeObstacleOverlay: true))
            return false;

        if (clearance <= 0.0001f)
            return true;

        Vector3 normal = Vector3.Cross(Vector3.up, horizontal.normalized);
        Vector3 offset = normal * Mathf.Min(clearance, world.CellSize * 0.45f);
        return IsNavigationSegmentWalkable(
                   world,
                   from + offset,
                   horizontal,
                   clearance,
                   requireClearStart: false,
                   includeRuntimeObstacleOverlay: true)
               && IsNavigationSegmentWalkable(
                   world,
                   from - offset,
                   horizontal,
                   clearance,
                   requireClearStart: false,
                   includeRuntimeObstacleOverlay: true);
    }

    private static float ResolveNavigationSegmentClearance(NavigationWorld world, float agentRadius)
    {
        if (world == null)
            return Mathf.Max(0f, agentRadius);

        return Mathf.Max(0f, agentRadius - world.CellSize * 0.2f);
    }

    private static float ResolveNavigationQueryClearance(NavigationWorld world, float requestedClearance)
    {
        requestedClearance = Mathf.Max(0f, requestedClearance);
        if (world == null || requestedClearance <= 0.0001f)
            return requestedClearance;

        float encodedCenterClearance = ResolveNavigationSegmentClearance(world, ResolveAgentTypeRadius(world.AgentTypeId));
        return Mathf.Max(0f, requestedClearance - encodedCenterClearance);
    }

    private static Fix64 ResolveNavigationQueryClearanceFixed(NavigationWorld world, Fix64 requestedClearance)
    {
        requestedClearance = Fix64.Max(Fix64.Zero, requestedClearance);
        if (world == null || requestedClearance <= Fix64.Zero)
            return requestedClearance;

        return Fix64.Max(Fix64.Zero, requestedClearance - world.EncodedCenterClearanceFixed);
    }

    private static float ResolveNavigationExecutionClearance(NavigationWorld world, float requestedClearance)
    {
        requestedClearance = Mathf.Max(0f, requestedClearance);
        if (world == null || requestedClearance <= 0.0001f)
            return requestedClearance;

        return Mathf.Max(0f, requestedClearance - world.EncodedCenterClearance);
    }

    private static bool HasPendingDirectLineOfSight(NavigationWorld world, int x0, int y0, int x1, int y1)
    {
        return HasSupercoverGridLineOfSight(
            world,
            x0,
            y0,
            x1,
            y1,
            GridLineOfSightCostMode.Strict,
            allowTargetSoftCost: false,
            maxAllowedCost: 1);
    }

    private static bool HasSoftCostTolerantGridLineOfSight(NavigationWorld world, int x0, int y0, int x1, int y1, int maxAllowedCost)
    {
        return HasSupercoverGridLineOfSight(
            world,
            x0,
            y0,
            x1,
            y1,
            GridLineOfSightCostMode.SoftCostLimit,
            allowTargetSoftCost: false,
            maxAllowedCost);
    }

    private static bool HasSoftCostTolerantGridLineOfSight(
        NavigationWorld world,
        FixVector2 from,
        FixVector2 to,
        int maxAllowedCost)
    {
        if (world == null)
            throw new InvalidOperationException("HasSoftCostTolerantGridLineOfSight failed: world is null.");
        if (!world.WorldToGridFixed(from, out int startX, out int startY)
            || !world.WorldToGridFixed(to, out int goalX, out int goalY))
        {
            return false;
        }
        return HasSoftCostTolerantGridLineOfSight(
            world,
            startX,
            startY,
            goalX,
            goalY,
            maxAllowedCost);
    }

    private enum GridLineOfSightCostMode
    {
        Strict,
        DirectShortcut,
        SoftCostLimit
    }

    private static bool HasSupercoverGridLineOfSight(
        NavigationWorld world,
        int x0,
        int y0,
        int x1,
        int y1,
        GridLineOfSightCostMode costMode,
        bool allowTargetSoftCost,
        int maxAllowedCost)
    {
        if (!IsGridLineOfSightCellPassable(world, x0, y0, x1, y1, costMode, allowTargetSoftCost, maxAllowedCost))
            return false;
        if (x0 == x1 && y0 == y1)
            return true;

        int currentX = x0;
        int currentY = y0;
        int stepX = x1 > x0 ? 1 : x1 < x0 ? -1 : 0;
        int stepY = y1 > y0 ? 1 : y1 < y0 ? -1 : 0;
        float deltaX = Mathf.Abs(x1 - x0);
        float deltaY = Mathf.Abs(y1 - y0);
        float tDeltaX = stepX == 0 ? float.PositiveInfinity : 1f / deltaX;
        float tDeltaY = stepY == 0 ? float.PositiveInfinity : 1f / deltaY;
        float tMaxX = stepX == 0 ? float.PositiveInfinity : 0.5f * tDeltaX;
        float tMaxY = stepY == 0 ? float.PositiveInfinity : 0.5f * tDeltaY;
        int guard = (Mathf.Abs(x1 - x0) + Mathf.Abs(y1 - y0) + 4) * 4;

        for (int i = 0; i < guard; i++)
        {
            if (currentX == x1 && currentY == y1)
                return true;

            if (Mathf.Abs(tMaxX - tMaxY) <= 0.000001f)
            {
                int sideX = currentX + stepX;
                int sideY = currentY + stepY;
                if (!IsGridLineOfSightStepPassable(world, currentX, currentY, sideX, currentY, x1, y1, costMode, allowTargetSoftCost, maxAllowedCost))
                    return false;
                if (!IsGridLineOfSightStepPassable(world, currentX, currentY, currentX, sideY, x1, y1, costMode, allowTargetSoftCost, maxAllowedCost))
                    return false;
                if (!IsGridLineOfSightStepPassable(world, sideX, currentY, sideX, sideY, x1, y1, costMode, allowTargetSoftCost, maxAllowedCost))
                    return false;
                if (!IsGridLineOfSightStepPassable(world, currentX, sideY, sideX, sideY, x1, y1, costMode, allowTargetSoftCost, maxAllowedCost))
                    return false;

                currentX = sideX;
                currentY = sideY;
                tMaxX += tDeltaX;
                tMaxY += tDeltaY;
                continue;
            }

            int nextX = currentX;
            int nextY = currentY;
            if (tMaxX < tMaxY)
            {
                nextX += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                nextY += stepY;
                tMaxY += tDeltaY;
            }

            if (!IsGridLineOfSightStepPassable(world, currentX, currentY, nextX, nextY, x1, y1, costMode, allowTargetSoftCost, maxAllowedCost))
                return false;

            currentX = nextX;
            currentY = nextY;
        }

        return false;
    }

    private static bool IsGridLineOfSightStepPassable(
        NavigationWorld world,
        int fromX,
        int fromY,
        int toX,
        int toY,
        int goalX,
        int goalY,
        GridLineOfSightCostMode costMode,
        bool allowTargetSoftCost,
        int maxAllowedCost)
    {
        return IsGridLineOfSightCellPassable(world, toX, toY, goalX, goalY, costMode, allowTargetSoftCost, maxAllowedCost)
               && CanTraverseNeighborCells(world, fromX, fromY, toX, toY);
    }

    private static bool IsGridLineOfSightCellPassable(
        NavigationWorld world,
        int x,
        int y,
        int goalX,
        int goalY,
        GridLineOfSightCostMode costMode,
        bool allowTargetSoftCost,
        int maxAllowedCost)
    {
        if (!world.IsWalkable(x, y))
            return false;

        int cellCost = GetCostFieldValueStrict(world, x, y);
        if (costMode == GridLineOfSightCostMode.SoftCostLimit)
            return cellCost <= maxAllowedCost;
        if (costMode == GridLineOfSightCostMode.DirectShortcut)
        {
            return cellCost < byte.MaxValue
                   && (!IsCellAffectedByCostStamp(world, x, y)
                       || (allowTargetSoftCost && x == goalX && y == goalY));
        }

        return cellCost <= 1
               || IsBoundaryOnlySoftCostCell(world, x, y)
               || (allowTargetSoftCost && x == goalX && y == goalY);
    }

    private static bool IsDiagonalPassable(NavigationWorld world, int fromX, int fromY, int toX, int toY)
    {
        return world.IsWalkable(fromX, toY)
               && world.IsWalkable(toX, fromY)
               && HasRawNeighborTraversal(world, fromX, fromY, fromX, toY)
               && HasRawNeighborTraversal(world, fromX, fromY, toX, fromY)
               && HasRawNeighborTraversal(world, fromX, toY, toX, toY)
               && HasRawNeighborTraversal(world, toX, fromY, toX, toY);
    }

}
