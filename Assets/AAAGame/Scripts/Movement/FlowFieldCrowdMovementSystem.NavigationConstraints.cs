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
    private static void LogIslandFieldDiagnostics(NavigationWorld world, string reason)
    {
        if (world == null)
            throw new InvalidOperationException("LogIslandFieldDiagnostics failed: world is null.");
        if (world.IslandIds == null || world.IslandIds.Length != world.Width * world.Height)
            throw new InvalidOperationException("LogIslandFieldDiagnostics failed: island field is missing or invalid.");
        if (!GameDebugSettings.IsEnabled(DebugCategory.Move))
            return;

        int walkableCount = 0;
        int[] islandSizes = new int[Mathf.Max(1, world.IslandCount + 1)];
        int[] islandSampleX = new int[islandSizes.Length];
        int[] islandSampleY = new int[islandSizes.Length];
        int[] islandMinX = new int[islandSizes.Length];
        int[] islandMaxX = new int[islandSizes.Length];
        int[] islandMinY = new int[islandSizes.Length];
        int[] islandMaxY = new int[islandSizes.Length];
        for (int i = 0; i < islandSampleX.Length; i++)
        {
            islandSampleX[i] = -1;
            islandSampleY[i] = -1;
            islandMinX[i] = int.MaxValue;
            islandMaxX[i] = int.MinValue;
            islandMinY[i] = int.MaxValue;
            islandMaxY[i] = int.MinValue;
        }

        for (int y = 0; y < world.Height; y++)
        {
            for (int x = 0; x < world.Width; x++)
            {
                int index = world.GetIndex(x, y);
                if (!world.WalkableMask[index])
                    continue;

                walkableCount++;
                int islandId = ResolveIslandId(world, x, y);
                if (islandId <= 0 || islandId >= islandSizes.Length)
                    continue;

                islandSizes[islandId]++;
                if (x < islandMinX[islandId])
                    islandMinX[islandId] = x;
                if (x > islandMaxX[islandId])
                    islandMaxX[islandId] = x;
                if (y < islandMinY[islandId])
                    islandMinY[islandId] = y;
                if (y > islandMaxY[islandId])
                    islandMaxY[islandId] = y;
                if (islandSampleX[islandId] < 0)
                {
                    islandSampleX[islandId] = x;
                    islandSampleY[islandId] = y;
                }
            }
        }

        int largestIslandId = 0;
        int largestIslandSize = 0;
        int smallIslandCount = 0;
        for (int islandId = 1; islandId < islandSizes.Length; islandId++)
        {
            int size = islandSizes[islandId];
            if (size > largestIslandSize)
            {
                largestIslandSize = size;
                largestIslandId = islandId;
            }

            if (size > 0 && size <= 8)
                smallIslandCount++;
        }

        if (world.IslandCount <= 1)
            return;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(1024);
        builder.Append("[FlowIslandFieldDiag] reason=");
        builder.Append(reason);
        builder.Append(" worldVersion=");
        builder.Append(world.Version);
        builder.Append(" agentType=");
        builder.Append(world.AgentTypeId);
        builder.Append(" size=");
        builder.Append(world.Width);
        builder.Append("x");
        builder.Append(world.Height);
        builder.Append(" cellSize=");
        builder.Append(world.CellSize.ToString("F3"));
        builder.Append(" walkable=");
        builder.Append(walkableCount);
        builder.Append(" islandCount=");
        builder.Append(world.IslandCount);
        builder.Append(" largest=");
        builder.Append(largestIslandId);
        builder.Append(":");
        builder.Append(largestIslandSize);
        builder.Append(" smallIslands=");
        builder.Append(smallIslandCount);
        builder.Append(" samples=[");

        int appended = 0;
        for (int islandId = 1; islandId < islandSizes.Length && appended < 8; islandId++)
        {
            int size = islandSizes[islandId];
            if (size <= 0 || islandId == largestIslandId)
                continue;

            if (appended > 0)
                builder.Append(" | ");

            int sampleX = islandSampleX[islandId];
            int sampleY = islandSampleY[islandId];
            builder.Append("island=");
            builder.Append(islandId);
            builder.Append(" size=");
            builder.Append(size);
            builder.Append(" sample=(");
            builder.Append(sampleX);
            builder.Append(",");
            builder.Append(sampleY);
            builder.Append(") ");
            builder.Append("bbox=(");
            builder.Append(islandMinX[islandId]);
            builder.Append(",");
            builder.Append(islandMinY[islandId]);
            builder.Append(")-(");
            builder.Append(islandMaxX[islandId]);
            builder.Append(",");
            builder.Append(islandMaxY[islandId]);
            builder.Append(") ");
            builder.Append(BuildIslandBoundaryDiagnostics(world, islandId, largestIslandId, 6));
            builder.Append(" ");
            builder.Append(BuildNeighborLinkDiagnostics(world, sampleX, sampleY, 1, 6));
            appended++;
        }

        builder.Append("]");
        Debug.LogWarning(builder.ToString());
    }

    private static string BuildIslandBoundaryDiagnostics(NavigationWorld world, int islandId, int largestIslandId, int maxExamples)
    {
        if (world == null)
            return "boundary=world-null";
        if (world.IslandIds == null || world.IslandIds.Length != world.Width * world.Height)
            return "boundary=island-invalid";

        int blockedNeighborCount = 0;
        int baseBlockedNeighborCount = 0;
        int runtimeBlockedNeighborCount = 0;
        int otherIslandNeighborCount = 0;
        int noTraversalNeighborCount = 0;
        int largestNeighborCount = 0;
        int exampleCount = 0;
        System.Text.StringBuilder examples = new System.Text.StringBuilder(768);
        examples.Append("[");

        for (int y = 0; y < world.Height; y++)
        {
            for (int x = 0; x < world.Width; x++)
            {
                int index = world.GetIndex(x, y);
                if (ResolveIslandId(world, x, y) != islandId)
                    continue;

                byte mask = world.NeighborTraversalMask[index];
                for (int i = 0; i < NeighborOffsetX.Length; i++)
                {
                    int toX = x + NeighborOffsetX[i];
                    int toY = y + NeighborOffsetY[i];
                    if (toX < 0 || toX >= world.Width || toY < 0 || toY >= world.Height)
                        continue;

                    int toIndex = world.GetIndex(toX, toY);
                    bool toWalkable = world.WalkableMask[toIndex];
                    if (!toWalkable)
                    {
                        blockedNeighborCount++;
                        bool baseWalkable = world.BaseWalkableMask != null
                                            && world.BaseWalkableMask.Length == world.WalkableMask.Length
                                            && world.BaseWalkableMask[toIndex];
                        if (baseWalkable)
                            runtimeBlockedNeighborCount++;
                        else
                            baseBlockedNeighborCount++;

                        if (exampleCount < maxExamples)
                        {
                            AppendBoundaryExamplePrefix(examples, ref exampleCount);
                            examples.Append("(");
                            examples.Append(x);
                            examples.Append(",");
                            examples.Append(y);
                            examples.Append(")->(");
                            examples.Append(toX);
                            examples.Append(",");
                            examples.Append(toY);
                            examples.Append("){blocked,base=");
                            examples.Append(baseWalkable);
                            examples.Append("}");
                        }

                        continue;
                    }

                    int toIsland = ResolveIslandId(world, toX, toY);
                    if (toIsland == islandId)
                        continue;

                    otherIslandNeighborCount++;
                    if (toIsland == largestIslandId)
                        largestNeighborCount++;

                    bool hasTraversal = (mask & (1 << i)) != 0;
                    if (!hasTraversal)
                        noTraversalNeighborCount++;

                    if (exampleCount < maxExamples)
                    {
                        AppendBoundaryExamplePrefix(examples, ref exampleCount);
                        examples.Append("(");
                        examples.Append(x);
                        examples.Append(",");
                        examples.Append(y);
                        examples.Append(")->(");
                        examples.Append(toX);
                        examples.Append(",");
                        examples.Append(toY);
                        examples.Append("){toIsland=");
                        examples.Append(toIsland);
                        examples.Append(",traverse=");
                        examples.Append(hasTraversal);
                        examples.Append("}");
                    }
                }
            }
        }

        if (exampleCount == 0)
            examples.Append("none");
        examples.Append("]");

        return "boundary={blocked="
               + blockedNeighborCount
               + ",baseBlocked="
               + baseBlockedNeighborCount
               + ",runtimeBlocked="
               + runtimeBlockedNeighborCount
               + ",otherIsland="
               + otherIslandNeighborCount
               + ",largestNeighbors="
               + largestNeighborCount
               + ",noTraversal="
               + noTraversalNeighborCount
               + ",examples="
               + examples
               + "}";
    }

    private static void AppendBoundaryExamplePrefix(System.Text.StringBuilder builder, ref int exampleCount)
    {
        if (exampleCount > 0)
            builder.Append("; ");

        exampleCount++;
    }

    private static string BuildNeighborLinkDiagnostics(NavigationWorld world, int centerX, int centerY, int radius, int maxLinks = 16)
    {
        if (world == null)
            return "links=world-null";
        if (world.NeighborTraversalMask == null || world.NeighborTraversalMask.Length != world.Width * world.Height)
            return "links=mask-invalid";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(768);
        builder.Append("links=[");
        int count = 0;
        for (int y = centerY - radius; y <= centerY + radius && count < maxLinks; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius && count < maxLinks; x++)
            {
                if (x < 0 || x >= world.Width || y < 0 || y >= world.Height || !world.IsWalkable(x, y))
                    continue;

                int fromIndex = world.GetIndex(x, y);
                byte mask = world.NeighborTraversalMask[fromIndex];
                int fromIsland = ResolveIslandIdForDiagnostics(world, x, y);
                for (int i = 0; i < NeighborOffsetX.Length && count < maxLinks; i++)
                {
                    int toX = x + NeighborOffsetX[i];
                    int toY = y + NeighborOffsetY[i];
                    if (toX < 0 || toX >= world.Width || toY < 0 || toY >= world.Height || !world.IsWalkable(toX, toY))
                        continue;

                    bool hasTraversal = (mask & (1 << i)) != 0;
                    int toIsland = ResolveIslandIdForDiagnostics(world, toX, toY);
                    if (hasTraversal && fromIsland == toIsland)
                        continue;

                    if (count > 0)
                        builder.Append("; ");

                    builder.Append("(");
                    builder.Append(x);
                    builder.Append(",");
                    builder.Append(y);
                    builder.Append(")->(");
                    builder.Append(toX);
                    builder.Append(",");
                    builder.Append(toY);
                    builder.Append("){fromIsland=");
                    builder.Append(fromIsland);
                    builder.Append(",toIsland=");
                    builder.Append(toIsland);
                    builder.Append(",traverse=");
                    builder.Append(hasTraversal);

                    builder.Append("}");
                    count++;
                }
            }
        }

        if (count == 0)
            builder.Append("none");
        builder.Append("]");
        return builder.ToString();
    }

    private static bool CanTraverseSamplePath(NavigationWorld world, int fromX, int fromY, int toX, int toY)
    {
        int dx = toX - fromX;
        int dy = toY - fromY;
        if (dx != 0 && dy != 0)
            return false;
        int steps = Mathf.Abs(dx) + Mathf.Abs(dy);
        if (steps == 0)
            return world.IsWalkable(fromX, fromY);

        int stepX = Math.Sign(dx);
        int stepY = Math.Sign(dy);
        int currentX = fromX;
        int currentY = fromY;
        for (int i = 0; i < steps; i++)
        {
            int nextX = currentX + stepX;
            int nextY = currentY + stepY;
            if (!CanTraverseNeighborCells(world, currentX, currentY, nextX, nextY))
                return false;
            currentX = nextX;
            currentY = nextY;
        }
        return true;
    }

    private static bool CanTraverseNeighborCells(NavigationWorld world, int fromX, int fromY, int toX, int toY)
    {
        if (!world.IsWalkable(fromX, fromY) || !world.IsWalkable(toX, toY))
            return false;

        int dx = Mathf.Abs(toX - fromX);
        int dy = Mathf.Abs(toY - fromY);
        if (dx > 1 || dy > 1 || (dx == 0 && dy == 0))
            return false;

        if (!HasRawNeighborTraversal(world, fromX, fromY, toX, toY))
            return false;

        if (dx == 1 && dy == 1 && !IsDiagonalPassable(world, fromX, fromY, toX, toY))
            return false;

        return true;
    }

    private static bool TryGetCommittedNavigationQueryWorld(int agentTypeId, bool allowSynchronousBuild, out NavigationWorld world)
    {
        world = null;
        try
        {
            if (!TryEnsureWorldBuilt(agentTypeId, allowSynchronousBuild))
                return false;
        }
        catch (InvalidOperationException exception)
        {
            if (GameDebugSettings.IsEnabled(DebugCategory.Move))
                Debug.LogWarning($"[FlowNavigationQuery] world unavailable agentType={agentTypeId}: {exception.Message}");
            return false;
        }

        WorldRuntimeState state = _activeWorldState;
        if (state == null || state.World == null)
            return false;

        world = state.World;
        return true;
    }

    private static bool TryGetCommittedNavigationQueryWorldReadOnly(
        int agentTypeId,
        out WorldRuntimeState state,
        out NavigationWorld world)
    {
        int resolvedAgentTypeId = ResolvePreferredAgentTypeId(agentTypeId);
        state = null;
        world = null;
        if (!WorldStates.TryGetValue(resolvedAgentTypeId, out state) || state == null)
            return false;
        if (state.AgentTypeId != resolvedAgentTypeId)
        {
            throw new InvalidOperationException(
                $"Read-only navigation world lookup found an agent type mismatch. key={resolvedAgentTypeId}, state={state.AgentTypeId}.");
        }
        if (state.IsDirty || state.BuildJob != null || state.World == null)
            return false;
        if (state.World.AgentTypeId != resolvedAgentTypeId)
        {
            throw new InvalidOperationException(
                $"Read-only navigation world lookup found a committed world mismatch. key={resolvedAgentTypeId}, world={state.World.AgentTypeId}.");
        }

        world = state.World;
        return true;
    }

    private static bool TryGetNavigationQueryWorld(int agentTypeId, bool allowSynchronousBuild, out NavigationWorld world)
    {
        if (!TryGetCommittedNavigationQueryWorld(agentTypeId, allowSynchronousBuild, out world))
            return false;

        WorldRuntimeState state = _activeWorldState;
        if (state == null || state.World == null)
            return false;

        world = HasPendingRuntimeDirty(state) ? ResolveReachabilityQueryWorld(state) : world;
        return world != null;
    }

    private static bool IsNavigationSegmentWalkable(
        NavigationWorld world,
        Vector3 position,
        Vector3 horizontalDisplacement,
        float edgeClearance = 0f,
        bool requireClearStart = true,
        bool includeRuntimeObstacleOverlay = false)
    {
        if (world == null)
            return false;
        if (!world.WorldToGrid(position, out int previousX, out int previousY) || !world.IsWalkable(previousX, previousY))
            return false;
        if (requireClearStart && !IsNavigationPointClear(world, position, edgeClearance, includeRuntimeObstacleOverlay))
            return false;

        Vector3 target = position + horizontalDisplacement;
        if (!world.WorldToGrid(target, out int targetX, out int targetY) || !world.IsWalkable(targetX, targetY))
            return false;
        if (!IsNavigationPointAllowedFromStart(world, position, target, edgeClearance, requireClearStart, includeRuntimeObstacleOverlay))
            return false;

        float distance = horizontalDisplacement.magnitude;
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(world.CellSize * 0.45f, 0.001f)));
        for (int i = 1; i <= steps; i++)
        {
            Vector3 sample = position + horizontalDisplacement * (i / (float)steps);
            if (!world.WorldToGrid(sample, out int x, out int y) || !world.IsWalkable(x, y))
                return false;
            if (!IsNavigationPointAllowedFromStart(world, position, sample, edgeClearance, requireClearStart, includeRuntimeObstacleOverlay))
                return false;

            if (x == previousX && y == previousY)
                continue;

            int dx = Math.Abs(x - previousX);
            int dy = Math.Abs(y - previousY);
            if (dx > 1 || dy > 1)
            {
                if (!IsNavigationCellTraceWalkable(world, previousX, previousY, x, y))
                    return false;
            }
            else if (!CanTraverseNeighborCells(world, previousX, previousY, x, y))
            {
                return false;
            }

            previousX = x;
            previousY = y;
        }

        return true;
    }

    private static string BuildNavigationSegmentWalkableDiagnostics(
        NavigationWorld world,
        Vector3 position,
        Vector3 horizontalDisplacement,
        float edgeClearance,
        bool requireClearStart,
        bool includeRuntimeObstacleOverlay)
    {
        if (world == null)
            return "world=null";
        if (!world.WorldToGrid(position, out int previousX, out int previousY))
            return $"start outside position={position}";
        if (!world.IsWalkable(previousX, previousY))
            return $"start blocked cell=({previousX},{previousY}) position={position} sample={FormatGridSampleDiagnostics(position)}";
        if (requireClearStart && !IsNavigationPointClear(world, position, edgeClearance, includeRuntimeObstacleOverlay))
        {
            float violation = ResolveNavigationClearanceViolation(world, position, edgeClearance, includeRuntimeObstacleOverlay);
            return $"start clearance failed cell=({previousX},{previousY}) clearance={edgeClearance:F3} violation={(float.IsPositiveInfinity(violation) ? "INF" : violation.ToString("F3"))}";
        }

        Vector3 target = position + horizontalDisplacement;
        if (!world.WorldToGrid(target, out int targetX, out int targetY))
            return $"target outside target={target}";
        if (!world.IsWalkable(targetX, targetY))
            return $"target blocked cell=({targetX},{targetY}) target={target} sample={FormatGridSampleDiagnostics(target)}";
        if (!IsNavigationPointAllowedFromStart(world, position, target, edgeClearance, requireClearStart, includeRuntimeObstacleOverlay))
        {
            float violation = ResolveNavigationClearanceViolation(world, target, edgeClearance, includeRuntimeObstacleOverlay);
            return $"target clearance failed cell=({targetX},{targetY}) clearance={edgeClearance:F3} violation={(float.IsPositiveInfinity(violation) ? "INF" : violation.ToString("F3"))}";
        }

        float distance = horizontalDisplacement.magnitude;
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(world.CellSize * 0.45f, 0.001f)));
        for (int i = 1; i <= steps; i++)
        {
            Vector3 sample = position + horizontalDisplacement * (i / (float)steps);
            if (!world.WorldToGrid(sample, out int x, out int y))
                return $"sample outside step={i}/{steps} sample={sample}";
            if (!world.IsWalkable(x, y))
                return $"sample blocked step={i}/{steps} cell=({x},{y}) sample={sample} trace={BuildLineOfSightDecisionCellTrace(world, previousX, previousY, x, y)}";
            if (!IsNavigationPointAllowedFromStart(world, position, sample, edgeClearance, requireClearStart, includeRuntimeObstacleOverlay))
            {
                float violation = ResolveNavigationClearanceViolation(world, sample, edgeClearance, includeRuntimeObstacleOverlay);
                return $"sample clearance failed step={i}/{steps} cell=({x},{y}) clearance={edgeClearance:F3} violation={(float.IsPositiveInfinity(violation) ? "INF" : violation.ToString("F3"))}";
            }

            if (x == previousX && y == previousY)
                continue;

            int dx = Math.Abs(x - previousX);
            int dy = Math.Abs(y - previousY);
            if (dx > 1 || dy > 1)
            {
                if (!IsNavigationCellTraceWalkable(world, previousX, previousY, x, y))
                    return $"trace failed from=({previousX},{previousY}) to=({x},{y}) step={i}/{steps} trace={BuildLineOfSightDecisionCellTrace(world, previousX, previousY, x, y)}";
            }
            else if (!CanTraverseNeighborCells(world, previousX, previousY, x, y))
            {
                return $"neighbor failed from=({previousX},{previousY}) to=({x},{y}) step={i}/{steps} fromSample={BuildNeighborLinkDiagnostics(world, previousX, previousY, 0, 8)} toSample={BuildNeighborLinkDiagnostics(world, x, y, 0, 8)}";
            }

            previousX = x;
            previousY = y;
        }

        return $"ok start=({previousX},{previousY}) target=({targetX},{targetY}) steps={steps} clearance={edgeClearance:F3}";
    }

    private static bool IsNavigationPointAllowedFromStart(
        NavigationWorld world,
        Vector3 start,
        Vector3 point,
        float edgeClearance,
        bool requireClearStart,
        bool includeRuntimeObstacleOverlay)
    {
        if (requireClearStart)
            return IsNavigationPointClear(world, point, edgeClearance, includeRuntimeObstacleOverlay);
        if (IsNavigationPointClear(world, point, edgeClearance, includeRuntimeObstacleOverlay))
            return true;

        Fix64 clearanceFixed = Fix64.Max(Fix64.Zero, (Fix64)edgeClearance);
        Fix64 startViolation = ResolveNavigationClearanceViolationFixed(
            world,
            new FixVector2((Fix64)start.x, (Fix64)start.z),
            clearanceFixed,
            includeRuntimeObstacleOverlay);
        Fix64 pointViolation = ResolveNavigationClearanceViolationFixed(
            world,
            new FixVector2((Fix64)point.x, (Fix64)point.z),
            clearanceFixed,
            includeRuntimeObstacleOverlay);
        return pointViolation <= startViolation;
    }

    private static bool IsNavigationCellTraceWalkable(NavigationWorld world, int fromX, int fromY, int toX, int toY)
    {
        int currentX = fromX;
        int currentY = fromY;
        int steps = Mathf.Max(Math.Abs(toX - fromX), Math.Abs(toY - fromY));
        if (steps <= 0)
            return true;

        for (int i = 1; i <= steps; i++)
        {
            int nextX = Mathf.RoundToInt(Mathf.Lerp(fromX, toX, i / (float)steps));
            int nextY = Mathf.RoundToInt(Mathf.Lerp(fromY, toY, i / (float)steps));
            if (nextX == currentX && nextY == currentY)
                continue;
            if (!CanTraverseNeighborCells(world, currentX, currentY, nextX, nextY))
                return false;

            currentX = nextX;
            currentY = nextY;
        }

        return true;
    }

    private static void TrySelectNavigationDisplacementCandidate(
        NavigationWorld world,
        Vector3 position,
        Vector3 candidate,
        Vector3 desired,
        float desiredDistance,
        float edgeClearance,
        bool requireClearStart,
        bool includeRuntimeObstacleOverlay,
        ref Vector3 best,
        ref float bestScore,
        bool preserveDistance = true)
    {
        candidate.y = 0f;
        if (candidate.sqrMagnitude <= 0.000001f)
            return;

        if (preserveDistance)
            candidate = candidate.normalized * desiredDistance;

        if (!IsNavigationSegmentWalkable(world, position, candidate, edgeClearance, requireClearStart, includeRuntimeObstacleOverlay))
            return;

        if (!IsNavigationConstraintCandidateAligned(candidate, desired))
            return;

        Vector3 desiredDirection = desired.sqrMagnitude > 0.000001f ? desired.normalized : Vector3.zero;
        float score = Vector3.Dot(candidate, desiredDirection) + candidate.magnitude * 0.05f;
        if (score <= bestScore)
            return;

        bestScore = score;
        best = candidate;
    }

    private static bool IsNavigationConstraintCandidateAligned(Vector3 candidate, Vector3 desired)
    {
        candidate.y = 0f;
        desired.y = 0f;
        if (candidate.sqrMagnitude <= 0.000001f || desired.sqrMagnitude <= 0.000001f)
            return false;

        return Vector3.Dot(candidate.normalized, desired.normalized) >= NavigationConstraintMinDirectionDot;
    }

    private static void TrySelectNavigationDirectionalFanCandidate(
        NavigationWorld world,
        Vector3 position,
        Vector3 desired,
        float desiredDistance,
        float edgeClearance,
        bool requireClearStart,
        bool includeRuntimeObstacleOverlay,
        ref Vector3 best,
        ref float bestScore)
    {
        if (desired.sqrMagnitude <= 0.000001f || desiredDistance <= 0.000001f)
            return;

        Vector3 direction = desired.normalized;
        TrySelectNavigationDisplacementCandidate(world, position, direction, desired, desiredDistance, edgeClearance, requireClearStart, includeRuntimeObstacleOverlay, ref best, ref bestScore);

        for (int angle = 15; angle <= 75; angle += 15)
        {
            Vector3 left = RotateHorizontal(direction, angle);
            Vector3 right = RotateHorizontal(direction, -angle);
            TrySelectNavigationDisplacementCandidate(world, position, left, desired, desiredDistance, edgeClearance, requireClearStart, includeRuntimeObstacleOverlay, ref best, ref bestScore);
            TrySelectNavigationDisplacementCandidate(world, position, right, desired, desiredDistance, edgeClearance, requireClearStart, includeRuntimeObstacleOverlay, ref best, ref bestScore);
        }

        float halfDistance = desiredDistance * 0.5f;
        if (halfDistance <= 0.000001f)
            return;

        TrySelectNavigationDisplacementCandidate(world, position, direction * halfDistance, desired, halfDistance, edgeClearance, requireClearStart, includeRuntimeObstacleOverlay, ref best, ref bestScore, preserveDistance: false);
        for (int angle = 15; angle <= 75; angle += 15)
        {
            Vector3 left = RotateHorizontal(direction, angle) * halfDistance;
            Vector3 right = RotateHorizontal(direction, -angle) * halfDistance;
            TrySelectNavigationDisplacementCandidate(world, position, left, desired, halfDistance, edgeClearance, requireClearStart, includeRuntimeObstacleOverlay, ref best, ref bestScore, preserveDistance: false);
            TrySelectNavigationDisplacementCandidate(world, position, right, desired, halfDistance, edgeClearance, requireClearStart, includeRuntimeObstacleOverlay, ref best, ref bestScore, preserveDistance: false);
        }
    }

    private static Vector3 RotateHorizontal(Vector3 direction, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector3(
            direction.x * cos - direction.z * sin,
            0f,
            direction.x * sin + direction.z * cos);
    }

    private static Vector3 FindLongestWalkablePrefix(
        NavigationWorld world,
        Vector3 position,
        Vector3 desired,
        float edgeClearance,
        bool requireClearStart,
        bool includeRuntimeObstacleOverlay)
    {
        Vector3 best = Vector3.zero;
        float low = 0f;
        float high = 1f;
        for (int i = 0; i < 7; i++)
        {
            float mid = (low + high) * 0.5f;
            Vector3 candidate = desired * mid;
            if (IsNavigationSegmentWalkable(world, position, candidate, edgeClearance, requireClearStart, includeRuntimeObstacleOverlay))
            {
                best = candidate;
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return best;
    }

    private static bool IsNavigationPointClear(NavigationWorld world, Vector3 point, float edgeClearance)
    {
        return IsNavigationPointClear(world, point, edgeClearance, includeRuntimeObstacleOverlay: false);
    }

    private static bool IsNavigationPointClear(NavigationWorld world, Vector3 point, float edgeClearance, bool includeRuntimeObstacleOverlay)
    {
        return IsNavigationPointClearFixed(
            world,
            new FixVector2((Fix64)point.x, (Fix64)point.z),
            Fix64.Max(Fix64.Zero, (Fix64)edgeClearance),
            includeRuntimeObstacleOverlay);
    }

    private static bool IsNavigationPointClearFixed(
        NavigationWorld world,
        FixVector2 point,
        Fix64 edgeClearance,
        bool includeRuntimeObstacleOverlay)
    {
        if (world == null)
            throw new InvalidOperationException("IsNavigationPointClearFixed failed: world is null.");
        if (!world.WorldToGridFixed(point, out int cellX, out int cellY) || !world.IsWalkable(cellX, cellY))
            return false;
        if (edgeClearance <= Fix64.Zero)
            return !includeRuntimeObstacleOverlay || !IsPointInsideRuntimeObstacleOverlayFixed(point, Fix64.Zero);
        if (includeRuntimeObstacleOverlay && IsPointInsideRuntimeObstacleOverlayFixed(point, edgeClearance))
            return false;

        Fix64 clearanceSq = edgeClearance * edgeClearance;
        int radius = checked(NavigationGridFixedMath.DivideCeilingByCellSize(edgeClearance, world.CellSizeGridRaw) + 1);
        for (int y = cellY - radius; y <= cellY + radius; y++)
        {
            for (int x = cellX - radius; x <= cellX + radius; x++)
            {
                if (world.IsWalkable(x, y))
                    continue;

                world.GetGridCellBoundsFixed(x, y, out FixVector2 minimum, out FixVector2 maximum);
                Fix64 nearestX = Fix64.Clamp(point.x, minimum.x, maximum.x);
                Fix64 nearestZ = Fix64.Clamp(point.y, minimum.y, maximum.y);
                Fix64 dx = nearestX - point.x;
                Fix64 dz = nearestZ - point.y;
                if (dx * dx + dz * dz < clearanceSq)
                    return false;
            }
        }

        return true;
    }

    private static float ResolveNavigationClearanceViolation(NavigationWorld world, Vector3 point, float edgeClearance)
    {
        return ResolveNavigationClearanceViolation(world, point, edgeClearance, includeRuntimeObstacleOverlay: false);
    }

    private static float ResolveNavigationClearanceViolation(NavigationWorld world, Vector3 point, float edgeClearance, bool includeRuntimeObstacleOverlay)
    {
        Fix64 violation = ResolveNavigationClearanceViolationFixed(
            world,
            new FixVector2((Fix64)point.x, (Fix64)point.z),
            Fix64.Max(Fix64.Zero, (Fix64)edgeClearance),
            includeRuntimeObstacleOverlay);
        return violation.RawValue == long.MaxValue ? float.PositiveInfinity : (float)violation;
    }

    private static Fix64 ResolveNavigationClearanceViolationFixed(
        NavigationWorld world,
        FixVector2 point,
        Fix64 edgeClearance,
        bool includeRuntimeObstacleOverlay)
    {
        if (!world.WorldToGridFixed(point, out int cellX, out int cellY) || !world.IsWalkable(cellX, cellY))
            return Fix64.FromRaw(long.MaxValue);
        if (edgeClearance <= Fix64.Zero)
            return includeRuntimeObstacleOverlay && IsPointInsideRuntimeObstacleOverlayFixed(point, Fix64.Zero)
                ? Fix64.FromRaw(long.MaxValue)
                : Fix64.Zero;

        Fix64 minDistanceSq = Fix64.FromRaw(long.MaxValue);
        int radius = checked(NavigationGridFixedMath.DivideCeilingByCellSize(edgeClearance, world.CellSizeGridRaw) + 1);
        for (int y = cellY - radius; y <= cellY + radius; y++)
        {
            for (int x = cellX - radius; x <= cellX + radius; x++)
            {
                if (world.IsWalkable(x, y))
                    continue;

                world.GetGridCellBoundsFixed(x, y, out FixVector2 minimum, out FixVector2 maximum);
                Fix64 nearestX = Fix64.Clamp(point.x, minimum.x, maximum.x);
                Fix64 nearestZ = Fix64.Clamp(point.y, minimum.y, maximum.y);
                Fix64 dx = nearestX - point.x;
                Fix64 dz = nearestZ - point.y;
                Fix64 distanceSq = dx * dx + dz * dz;
                if (distanceSq < minDistanceSq)
                    minDistanceSq = distanceSq;
            }
        }

        if (minDistanceSq.RawValue == long.MaxValue)
            return includeRuntimeObstacleOverlay
                ? ResolveRuntimeObstacleOverlayClearanceViolationFixed(point, edgeClearance)
                : Fix64.Zero;

        Fix64 distance = Fix64.Sqrt(minDistanceSq);
        Fix64 staticViolation = Fix64.Max(Fix64.Zero, edgeClearance - distance);
        if (!includeRuntimeObstacleOverlay)
            return staticViolation;

        Fix64 overlayViolation = ResolveRuntimeObstacleOverlayClearanceViolationFixed(point, edgeClearance);
        if (overlayViolation.RawValue == long.MaxValue)
            return overlayViolation;
        return Fix64.Max(staticViolation, overlayViolation);
    }

    private static bool IsPointInsideRuntimeObstacleOverlay(Vector3 point, float clearance)
    {
        return IsPointInsideRuntimeObstacleOverlayFixed(
            new FixVector2((Fix64)point.x, (Fix64)point.z),
            Fix64.Max(Fix64.Zero, (Fix64)clearance));
    }

    private static bool IsPointInsideRuntimeObstacleOverlayFixed(FixVector2 point, Fix64 clearance)
    {
        return ResolveRuntimeObstacleOverlayClearanceViolationFixed(point, clearance) > Fix64.Zero;
    }

    private static float ResolveRuntimeObstacleOverlayClearanceViolation(Vector3 point, float edgeClearance)
    {
        Fix64 violation = ResolveRuntimeObstacleOverlayClearanceViolationFixed(
            new FixVector2((Fix64)point.x, (Fix64)point.z),
            Fix64.Max(Fix64.Zero, (Fix64)edgeClearance));
        return violation.RawValue == long.MaxValue ? float.PositiveInfinity : (float)violation;
    }

    private static Fix64 ResolveRuntimeObstacleOverlayClearanceViolationFixed(FixVector2 point, Fix64 edgeClearance)
    {
        Fix64 maxViolation = Fix64.Zero;
        foreach (BoxObstacle box in BoxObstacles.Values)
        {
            Fix64 dx = Fix64.Max(Fix64.Abs(point.x - box.CenterFixed.x) - Fix64.Max(Fix64.Zero, box.HalfExtentsFixed.x), Fix64.Zero);
            Fix64 dz = Fix64.Max(Fix64.Abs(point.y - box.CenterFixed.y) - Fix64.Max(Fix64.Zero, box.HalfExtentsFixed.y), Fix64.Zero);
            Fix64 distance = Fix64.Sqrt(dx * dx + dz * dz);
            if (distance <= Fix64.Zero)
                return Fix64.FromRaw(long.MaxValue);

            maxViolation = Fix64.Max(maxViolation, edgeClearance - distance);
        }

        foreach (CircleObstacle circle in CircleObstacles.Values)
        {
            Fix64 dx = point.x - circle.PositionFixed.x;
            Fix64 dz = point.y - circle.PositionFixed.y;
            Fix64 distance = Fix64.Sqrt(dx * dx + dz * dz) - Fix64.Max(Fix64.Zero, circle.RadiusFixed);
            if (distance <= Fix64.Zero)
                return Fix64.FromRaw(long.MaxValue);

            maxViolation = Fix64.Max(maxViolation, edgeClearance - distance);
        }

        return Fix64.Max(Fix64.Zero, maxViolation);
    }

    private static float ResolvePointToBoxObstacleDistanceXZ(Vector3 point, BoxObstacle box)
    {
        if (box == null)
            throw new InvalidOperationException("ResolvePointToBoxObstacleDistanceXZ failed: box is null.");

        float dx = Mathf.Max(Mathf.Abs(point.x - box.Center.x) - Mathf.Max(0f, box.HalfExtents.x), 0f);
        float dz = Mathf.Max(Mathf.Abs(point.z - box.Center.z) - Mathf.Max(0f, box.HalfExtents.z), 0f);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static float ResolvePointToCircleObstacleDistanceXZ(Vector3 point, CircleObstacle circle)
    {
        if (circle == null)
            throw new InvalidOperationException("ResolvePointToCircleObstacleDistanceXZ failed: circle is null.");

        float dx = point.x - circle.Position.x;
        float dz = point.z - circle.Position.z;
        return Mathf.Sqrt(dx * dx + dz * dz) - Mathf.Max(0f, circle.Radius);
    }

    private static bool IsNavigationCellClear(NavigationWorld world, int cellX, int cellY, float edgeClearance)
    {
        return IsNavigationCellClear(world, cellX, cellY, edgeClearance, includeRuntimeObstacleOverlay: false);
    }

    private static bool IsNavigationCellClear(NavigationWorld world, int cellX, int cellY, float edgeClearance, bool includeRuntimeObstacleOverlay)
    {
        return IsNavigationCellClearFixed(
            world,
            cellX,
            cellY,
            Fix64.Max(Fix64.Zero, (Fix64)edgeClearance),
            includeRuntimeObstacleOverlay);
    }

    private static bool IsNavigationCellClearFixed(
        NavigationWorld world,
        int cellX,
        int cellY,
        Fix64 edgeClearance,
        bool includeRuntimeObstacleOverlay)
    {
        if (!world.IsWalkable(cellX, cellY))
            return false;
        Fix64 violation = ResolveNavigationClearanceViolationFixed(
            world,
            world.GridToWorldCenterFixed(cellX, cellY),
            Fix64.Max(Fix64.Zero, edgeClearance),
            includeRuntimeObstacleOverlay);
        return violation == Fix64.Zero;
    }

    private static bool TryEstimateGridPathDistance(NavigationWorld world, int startX, int startY, int goalX, int goalY, out float distance)
    {
        return TryFindGridPath(world, startX, startY, goalX, goalY, null, default, default, out distance);
    }

    private static bool TryFindGridPath(
        NavigationWorld world,
        int startX,
        int startY,
        int goalX,
        int goalY,
        List<Vector3> pathCorners,
        Vector3 from,
        Vector3 to,
        out float distance)
    {
        distance = 0f;
        int searchId = BeginDistanceEstimateSearch(world.Width * world.Height);
        int startIndex = world.GetIndex(startX, startY);
        int goalIndex = world.GetIndex(goalX, goalY);
        DistanceEstimateCosts[startIndex] = 0f;
        DistanceEstimateVisitedMarks[startIndex] = searchId;
        if (pathCorners != null)
            DistanceEstimateParents[startIndex] = -1;
        DistanceEstimateOpenSet.Push(startIndex, EstimateGridHeuristicCost(startX, startY, goalX, goalY, world.CellSize));

        while (DistanceEstimateOpenSet.Count > 0)
        {
            int currentIndex = DistanceEstimateOpenSet.Pop().Index;
            if (DistanceEstimateClosedMarks[currentIndex] == searchId)
                continue;

            if (currentIndex == goalIndex)
            {
                distance = DistanceEstimateCosts[currentIndex];
                if (pathCorners != null)
                    BuildGridPathCorners(world, startIndex, goalIndex, from, to, pathCorners);
                return true;
            }

            DistanceEstimateClosedMarks[currentIndex] = searchId;
            int currentX = currentIndex % world.Width;
            int currentY = currentIndex / world.Width;
            float currentCost = DistanceEstimateCosts[currentIndex];
            for (int i = 0; i < NeighborOffsetX.Length; i++)
            {
                int nextX = currentX + NeighborOffsetX[i];
                int nextY = currentY + NeighborOffsetY[i];
                if (!CanTraverseNeighborCells(world, currentX, currentY, nextX, nextY))
                    continue;

                int nextIndex = world.GetIndex(nextX, nextY);
                if (DistanceEstimateClosedMarks[nextIndex] == searchId)
                    continue;

                float stepCost = (NeighborOffsetX[i] != 0 && NeighborOffsetY[i] != 0) ? world.CellSize * 1.41421356f : world.CellSize;
                float nextCost = currentCost + stepCost;
                if (DistanceEstimateVisitedMarks[nextIndex] == searchId && nextCost >= DistanceEstimateCosts[nextIndex])
                    continue;

                DistanceEstimateVisitedMarks[nextIndex] = searchId;
                DistanceEstimateCosts[nextIndex] = nextCost;
                if (pathCorners != null)
                    DistanceEstimateParents[nextIndex] = currentIndex;
                float priority = nextCost + EstimateGridHeuristicCost(nextX, nextY, goalX, goalY, world.CellSize);
                DistanceEstimateOpenSet.Push(nextIndex, priority);
            }
        }

        return false;
    }

    private static bool TryFindGridPathFixed(
        NavigationWorld world,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out Fix64 distance)
    {
        distance = Fix64.Zero;
        int searchId = BeginDistanceEstimateSearch(world.Width * world.Height);
        int startIndex = world.GetIndex(startX, startY);
        int goalIndex = world.GetIndex(goalX, goalY);
        FixedDistanceEstimateCosts[startIndex] = 0L;
        DistanceEstimateVisitedMarks[startIndex] = searchId;
        FixedDistanceEstimateOpenSet.Push(
            startIndex,
            EstimateGridHeuristicCostFixed(startX, startY, goalX, goalY, world.CellSizeFixed).RawValue);

        Fix64 diagonalStepCost = world.CellSizeFixed * Fix64.Sqrt((Fix64)2);
        while (FixedDistanceEstimateOpenSet.Count > 0)
        {
            int currentIndex = FixedDistanceEstimateOpenSet.Pop().Index;
            if (DistanceEstimateClosedMarks[currentIndex] == searchId)
                continue;

            if (currentIndex == goalIndex)
            {
                distance = Fix64.FromRaw(FixedDistanceEstimateCosts[currentIndex]);
                return true;
            }

            DistanceEstimateClosedMarks[currentIndex] = searchId;
            int currentX = currentIndex % world.Width;
            int currentY = currentIndex / world.Width;
            long currentCostRaw = FixedDistanceEstimateCosts[currentIndex];
            for (int i = 0; i < NeighborOffsetX.Length; i++)
            {
                int nextX = currentX + NeighborOffsetX[i];
                int nextY = currentY + NeighborOffsetY[i];
                if (!CanTraverseNeighborCells(world, currentX, currentY, nextX, nextY))
                    continue;

                int nextIndex = world.GetIndex(nextX, nextY);
                if (DistanceEstimateClosedMarks[nextIndex] == searchId)
                    continue;

                long stepCostRaw = NeighborOffsetX[i] != 0 && NeighborOffsetY[i] != 0
                    ? diagonalStepCost.RawValue
                    : world.CellSizeFixed.RawValue;
                long nextCostRaw = checked(currentCostRaw + stepCostRaw);
                if (DistanceEstimateVisitedMarks[nextIndex] == searchId
                    && nextCostRaw >= FixedDistanceEstimateCosts[nextIndex])
                {
                    continue;
                }

                DistanceEstimateVisitedMarks[nextIndex] = searchId;
                FixedDistanceEstimateCosts[nextIndex] = nextCostRaw;
                long priorityRaw = checked(
                    nextCostRaw
                    + EstimateGridHeuristicCostFixed(nextX, nextY, goalX, goalY, world.CellSizeFixed).RawValue);
                FixedDistanceEstimateOpenSet.Push(nextIndex, priorityRaw);
            }
        }

        return false;
    }

    private static bool TryFindSectorPathFixed(
        NavigationWorld world,
        SectorData sector,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out Fix64 distance)
    {
        distance = Fix64.Zero;
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (sector == null)
            throw new ArgumentNullException(nameof(sector));
        if (!IsInsideSector(sector, startX, startY) || !IsInsideSector(sector, goalX, goalY))
            throw new ArgumentOutOfRangeException(nameof(sector), "Sector-local path endpoints must be inside the supplied sector.");

        int searchId = BeginSectorDistanceEstimateSearch(sector.Width * sector.Height);
        int startIndex = GetSectorLocalIndex(sector, startX, startY);
        int goalIndex = GetSectorLocalIndex(sector, goalX, goalY);
        SectorDistanceEstimateCosts[startIndex] = 0L;
        SectorDistanceEstimateVisitedMarks[startIndex] = searchId;
        SectorDistanceEstimateOpenSet.Push(
            startIndex,
            EstimateGridHeuristicCostFixed(startX, startY, goalX, goalY, world.CellSizeFixed).RawValue);

        Fix64 diagonalStepCost = world.CellSizeFixed * Fix64.Sqrt((Fix64)2);
        while (SectorDistanceEstimateOpenSet.Count > 0)
        {
            int currentIndex = SectorDistanceEstimateOpenSet.Pop().Index;
            if (SectorDistanceEstimateClosedMarks[currentIndex] == searchId)
                continue;
            if (currentIndex == goalIndex)
            {
                distance = Fix64.FromRaw(SectorDistanceEstimateCosts[currentIndex]);
                return true;
            }

            SectorDistanceEstimateClosedMarks[currentIndex] = searchId;
            int currentX = sector.StartX + currentIndex % sector.Width;
            int currentY = sector.StartY + currentIndex / sector.Width;
            long currentCostRaw = SectorDistanceEstimateCosts[currentIndex];
            for (int i = 0; i < NeighborOffsetX.Length; i++)
            {
                int nextX = currentX + NeighborOffsetX[i];
                int nextY = currentY + NeighborOffsetY[i];
                if (!IsInsideSector(sector, nextX, nextY)
                    || !CanTraverseNeighborCells(world, currentX, currentY, nextX, nextY))
                {
                    continue;
                }

                int nextIndex = GetSectorLocalIndex(sector, nextX, nextY);
                if (SectorDistanceEstimateClosedMarks[nextIndex] == searchId)
                    continue;
                long stepCostRaw = NeighborOffsetX[i] != 0 && NeighborOffsetY[i] != 0
                    ? diagonalStepCost.RawValue
                    : world.CellSizeFixed.RawValue;
                long nextCostRaw = checked(currentCostRaw + stepCostRaw);
                if (SectorDistanceEstimateVisitedMarks[nextIndex] == searchId
                    && nextCostRaw >= SectorDistanceEstimateCosts[nextIndex])
                {
                    continue;
                }

                SectorDistanceEstimateVisitedMarks[nextIndex] = searchId;
                SectorDistanceEstimateCosts[nextIndex] = nextCostRaw;
                long priorityRaw = checked(
                    nextCostRaw
                    + EstimateGridHeuristicCostFixed(nextX, nextY, goalX, goalY, world.CellSizeFixed).RawValue);
                SectorDistanceEstimateOpenSet.Push(nextIndex, priorityRaw);
            }
        }

        return false;
    }

    private static int BeginSectorDistanceEstimateSearch(int cellCount)
    {
        if (cellCount <= 0)
            throw new InvalidOperationException($"BeginSectorDistanceEstimateSearch failed: invalid cellCount={cellCount}.");
        if (SectorDistanceEstimateCosts.Length < cellCount)
        {
            SectorDistanceEstimateCosts = new long[cellCount];
            SectorDistanceEstimateVisitedMarks = new int[cellCount];
            SectorDistanceEstimateClosedMarks = new int[cellCount];
            SectorDistanceEstimateSearchId = 0;
        }
        if (SectorDistanceEstimateSearchId == int.MaxValue)
        {
            Array.Clear(SectorDistanceEstimateVisitedMarks, 0, SectorDistanceEstimateVisitedMarks.Length);
            Array.Clear(SectorDistanceEstimateClosedMarks, 0, SectorDistanceEstimateClosedMarks.Length);
            SectorDistanceEstimateSearchId = 0;
        }

        SectorDistanceEstimateOpenSet.Clear();
        SectorDistanceEstimateSearchId++;
        return SectorDistanceEstimateSearchId;
    }

    private static void BuildGridPathCorners(
        NavigationWorld world,
        int startIndex,
        int goalIndex,
        Vector3 from,
        Vector3 to,
        List<Vector3> pathCorners)
    {
        DistanceEstimatePathIndices.Clear();
        int currentIndex = goalIndex;
        int remaining = world.Width * world.Height;
        while (currentIndex >= 0 && remaining-- > 0)
        {
            DistanceEstimatePathIndices.Add(currentIndex);
            if (currentIndex == startIndex)
                break;

            currentIndex = DistanceEstimateParents[currentIndex];
        }

        if (DistanceEstimatePathIndices.Count == 0
            || DistanceEstimatePathIndices[DistanceEstimatePathIndices.Count - 1] != startIndex)
        {
            throw new InvalidOperationException(
                $"BuildGridPathCorners failed: parent chain did not reach start startIndex={startIndex} goalIndex={goalIndex}.");
        }

        DistanceEstimatePathIndices.Reverse();
        pathCorners.Clear();
        pathCorners.Add(from);

        int previousDx = 0;
        int previousDy = 0;
        for (int i = 1; i < DistanceEstimatePathIndices.Count; i++)
        {
            int previousIndex = DistanceEstimatePathIndices[i - 1];
            int nextIndex = DistanceEstimatePathIndices[i];
            int dx = nextIndex % world.Width - previousIndex % world.Width;
            int dy = nextIndex / world.Width - previousIndex / world.Width;
            if (i > 1 && (dx != previousDx || dy != previousDy))
            {
                int cornerIndex = DistanceEstimatePathIndices[i - 1];
                pathCorners.Add(world.GridToWorldCenter(cornerIndex % world.Width, cornerIndex / world.Width));
            }

            previousDx = dx;
            previousDy = dy;
        }

        pathCorners.Add(to);
    }

    private static int BeginDistanceEstimateSearch(int cellCount)
    {
        if (cellCount <= 0)
            throw new InvalidOperationException($"BeginDistanceEstimateSearch failed: invalid cellCount={cellCount}.");

        if (DistanceEstimateCosts.Length < cellCount)
        {
            DistanceEstimateCosts = new float[cellCount];
            FixedDistanceEstimateCosts = new long[cellCount];
            DistanceEstimateVisitedMarks = new int[cellCount];
            DistanceEstimateClosedMarks = new int[cellCount];
            DistanceEstimateParents = new int[cellCount];
            DistanceEstimateSearchId = 0;
        }

        if (DistanceEstimateSearchId == int.MaxValue)
        {
            Array.Clear(DistanceEstimateVisitedMarks, 0, DistanceEstimateVisitedMarks.Length);
            Array.Clear(DistanceEstimateClosedMarks, 0, DistanceEstimateClosedMarks.Length);
            DistanceEstimateSearchId = 0;
        }

        DistanceEstimateOpenSet.Clear();
        FixedDistanceEstimateOpenSet.Clear();
        DistanceEstimateSearchId++;
        return DistanceEstimateSearchId;
    }

    private static float EstimateGridHeuristicCost(int fromX, int fromY, int goalX, int goalY, float cellSize)
    {
        int dx = Mathf.Abs(goalX - fromX);
        int dy = Mathf.Abs(goalY - fromY);
        int diagonal = Mathf.Min(dx, dy);
        int straight = Mathf.Max(dx, dy) - diagonal;
        return (diagonal * 1.41421356f + straight) * cellSize;
    }

    private static Fix64 EstimateGridHeuristicCostFixed(
        int fromX,
        int fromY,
        int goalX,
        int goalY,
        Fix64 cellSize)
    {
        int dx = Math.Abs(goalX - fromX);
        int dy = Math.Abs(goalY - fromY);
        int diagonal = Math.Min(dx, dy);
        int straight = Math.Max(dx, dy) - diagonal;
        return ((Fix64)diagonal * Fix64.Sqrt((Fix64)2) + (Fix64)straight) * cellSize;
    }

    private static string BuildStartCellDiagnostics(IEntityContext self, Vector3 position)
    {
        if (_world == null)
            return "diag=world-null";

        bool worldInGrid = _world.WorldToGrid(position, out int worldX, out int worldY);
        bool worldWalkable = worldInGrid && _world.IsWalkable(worldX, worldY);

        bool baseWalkable = false;
        if (worldInGrid)
        {
            int worldIndex = _world.GetIndex(worldX, worldY);
            baseWalkable = _world.BaseWalkableMask[worldIndex];
        }

        Fog3MapData mapData = Fog3Manager.Instance != null ? Fog3Manager.Instance.MapData : null;
        bool fogInGrid = false;
        int fogX = 0;
        int fogY = 0;
        bool fogWalkable = false;
        Fog3CellState fogState = Fog3CellState.Outside;
        float fogVisibility = 0f;
        if (mapData != null)
        {
            fogInGrid = mapData.WorldToGrid(position, out fogX, out fogY);
            if (fogInGrid)
            {
                fogWalkable = mapData.IsWalkable(fogX, fogY);
                fogState = mapData.GetCellState(fogX, fogY);
                fogVisibility = mapData.GetVisibility(fogX, fogY);
            }
        }

        string probeDiagnostics = worldInGrid
            ? BuildCellProbeDiagnostics(_world, worldX, worldY)
            : "probes=out";
        string neighborhoodDiagnostics = worldInGrid
            ? BuildWalkableNeighborhoodDiagnostics(_world, worldX, worldY, 2)
            : "neighborhood=out";
        string nearestSearchDiagnostics = worldInGrid
            ? BuildNearestWalkableSearchDiagnostics(_world, worldX, worldY, 6)
            : "nearestSearch=out";

        return $"diag=agentType={_world.AgentTypeId} cellSize={_world.CellSize:F3} origin={_world.Origin} size={_world.Width}x{_world.Height} " +
               $"worldCell={(worldInGrid ? $"({worldX},{worldY})" : "out")} worldWalkable={worldWalkable} " +
               $"baseWalkable={baseWalkable} fogCell={(fogInGrid ? $"({fogX},{fogY})" : "out")} fogWalkable={fogWalkable} " +
               $"fogState={fogState} fogVis={fogVisibility:F2} " +
               $"{probeDiagnostics} {nearestSearchDiagnostics} {neighborhoodDiagnostics}";
    }

    private static string BuildAgentCellDiagnostic(AgentRuntimeData agent, Vector3 position)
    {
        if (_world == null)
            return "cell=world-null";

        bool inGrid = _world.WorldToGrid(position, out int cellX, out int cellY);
        int island = inGrid ? ResolveIslandIdForDiagnostics(_world, cellX, cellY) : -1;
        bool walkable = inGrid && _world.IsWalkable(cellX, cellY);
        bool baseWalkable = false;
        if (inGrid)
            baseWalkable = _world.BaseWalkableMask[_world.GetIndex(cellX, cellY)];

        return $"cell={(inGrid ? $"({cellX},{cellY})" : "out")} island={island} walkable={walkable} baseWalkable={baseWalkable} " +
               $"navStateCell={agent.NavState.CurrentCell} navStateSector={agent.NavState.CurrentSectorId}";
    }

    private static string BuildGridEdgeDiagnostic(Vector3 position, Vector3 desiredDirection)
    {
        if (_world == null)
            return "gridEdge=world-null";

        bool hasGridEdge = TryResolveGridEdgeData(position, desiredDirection, out Vector3 gridNormal, out float gridDistance);
        return hasGridEdge
            ? $"gridEdgeNormal={gridNormal} gridEdgeDist={gridDistance:F3}"
            : "gridEdge=none";
    }

    private static string BuildExecutorGridSegmentDiagnostic(Vector3 position, Vector3 desiredHorizontalDisplacement, Vector3 inputVelocity)
    {
        if (desiredHorizontalDisplacement.sqrMagnitude <= 0.000001f)
            return "executorGrid=zero-displacement";
        if (_world == null)
            return "executorGrid=world-null";

        Vector3 end = position + desiredHorizontalDisplacement;
        bool startInGrid = _world.WorldToGrid(position, out int startX, out int startY);
        bool endInGrid = _world.WorldToGrid(end, out int endX, out int endY);
        bool startWalkable = startInGrid && _world.IsWalkable(startX, startY);
        bool endWalkable = endInGrid && _world.IsWalkable(endX, endY);
        bool clear = startInGrid && endInGrid && HasGridLineOfSight(_world, startX, startY, endX, endY);
        string trace = startInGrid && endInGrid
            ? BuildLineOfSightCellTrace(_world, startX, startY, endX, endY)
            : "trace=out";

        Vector3 edgeNormal = Vector3.zero;
        float edgeDistance = 0f;
        bool hasEdge = TryResolveGridEdgeData(position, desiredHorizontalDisplacement, out edgeNormal, out edgeDistance);
        float intoBoundary = hasEdge ? Vector3.Dot(inputVelocity, -edgeNormal) : 0f;
        return $"executorGrid={{start=({(startInGrid ? startX.ToString() : "out")},{(startInGrid ? startY.ToString() : "out")}) end=({(endInGrid ? endX.ToString() : "out")},{(endInGrid ? endY.ToString() : "out")}) startWalk={startWalkable} endWalk={endWalkable} clear={clear} edgeNormal={edgeNormal} edgeDist={edgeDistance:F3} intoBoundary={intoBoundary:F3} {trace}}}";
    }

    private static void LogMissingConstraintAgentDiagnostic(
        IEntityContext self,
        int expectedAgentId,
        Vector3 currentPosition,
        Vector3 desiredHorizontalDisplacement,
        Vector3 inputVelocity,
        string executorReason)
    {
        if (!IsMovementDiagnosticsEnabled())
            return;
        string candidates = BuildConstraintAgentCandidateDiagnostic(self, currentPosition, 6);
        string executorGrid = BuildExecutorGridSegmentDiagnostic(
            currentPosition,
            desiredHorizontalDisplacement,
            inputVelocity);

        Debug.LogWarning(
            $"[FlowConstraintDiagMissingAgent] reason={executorReason} key={self.CharacterKey} expectedId={expectedAgentId} " +
            $"selfType={self.GetType().Name} side={self.Side} pos={currentPosition} selfPos={self.Position} " +
            $"agentType={(self is ILogicFrameEntity logicEntity ? logicEntity.NavigationAgentTypeId.ToString() : "unknown")} " +
            $"inputVelocity={inputVelocity} desiredDisp={desiredHorizontalDisplacement} {executorGrid} " +
            $"agents={Agents.Count} candidates={candidates}");
    }

    private static string BuildConstraintAgentCandidateDiagnostic(IEntityContext self, Vector3 currentPosition, int maxCandidates)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append('[');
        int appended = 0;
        foreach (AgentRuntimeData candidate in Agents.Values)
        {
            bool sameKey = !string.IsNullOrEmpty(self.CharacterKey) && candidate.CharacterKey == self.CharacterKey;
            float distance = Vector3.Distance(candidate.Position, currentPosition);
            if (!sameKey && distance > 1.5f)
                continue;

            if (appended > 0)
                builder.Append(" | ");

            builder.Append("id=");
            builder.Append(candidate.Id);
            builder.Append(" key=");
            builder.Append(candidate.CharacterKey);
            builder.Append(" dist=");
            builder.Append(distance.ToString("F3"));
            builder.Append(" pos=");
            builder.Append(candidate.Position);
            builder.Append(" agentType=");
            builder.Append(candidate.AgentTypeId);
            builder.Append(" source=");
            builder.Append(candidate.RegistrationSource);
            builder.Append(" synthetic=");
            builder.Append(candidate.IsSyntheticRegistration);
            builder.Append(" intent=");
            builder.Append(candidate.HasNavigationIntent);
            builder.Append(" desired=");
            builder.Append(candidate.NavState.DesiredVelocity);
            builder.Append(" resolved=");
            builder.Append(candidate.NavState.ResolvedVelocity);

            appended++;
            if (appended >= maxCandidates)
                break;
        }

        if (appended == 0)
            builder.Append("none");
        builder.Append(']');
        return builder.ToString();
    }

    private static int ResolveAgentId(IEntityContext entity)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));
        if (!entity.LogicEntityId.IsValid)
            throw new InvalidOperationException($"FlowFieldCrowdMovementSystem.ResolveAgentId failed: {entity.GetType().Name} has an invalid logic entity id.");

        return entity.LogicEntityId.Value;
    }

    private static int ResolveExplicitAgentTypeId(IEntityContext entity, string caller)
    {
        if (entity == null)
            throw new InvalidOperationException($"{caller} failed: entity is null.");
        if (entity is ILogicFrameEntity logicEntity)
        {
            if (logicEntity.NavigationAgentTypeId == MAEntity.UnknownNavAgentTypeId)
            {
                throw new InvalidOperationException(
                    $"{caller} failed: entity '{entity.CharacterKey}' has Unknown navigation agent type. Runtime flow worlds require an explicit movement type.");
            }

            return logicEntity.NavigationAgentTypeId;
        }

        return 0;
    }

    private static int ResolvePreferredAgentTypeId(int preferredAgentTypeId)
    {
        if (preferredAgentTypeId != MAEntity.UnknownNavAgentTypeId)
            return preferredAgentTypeId;

        throw new InvalidOperationException("ResolvePreferredAgentTypeId failed: preferred agent type is Unknown. Runtime navigation must pass an explicit agent type.");
    }

    private static float ResolveAgentTypeRadius(int agentTypeId)
    {
        return (float)ResolveAgentTypeRadiusFixed(agentTypeId);
    }

    private static Fix64 ResolveAgentTypeRadiusFixed(int agentTypeId)
    {
#if UNITY_EDITOR
        if (TestAgentTypeRadii.TryGetValue(agentTypeId, out Fix64 testRadius))
            return testRadius;
        if (agentTypeId == AnyAgentTypeId
            && TestAgentTypeRadii.TryGetValue(ResolvePreferredAgentTypeId(0), out Fix64 defaultTestRadius))
        {
            return defaultTestRadius;
        }
#endif

        if (agentTypeId == MAEntity.UnknownNavAgentTypeId)
            return Fix64.FromRaw(2048);

        if (agentTypeId == AgentTypeHelper.SmallMovementTypeId)
            return GetPreparedAgentTypeRadiusFixed(agentTypeId, s_SmallAgentTypeRadiusFixed);
        if (agentTypeId == AgentTypeHelper.MediumMovementTypeId)
            return GetPreparedAgentTypeRadiusFixed(agentTypeId, s_MediumAgentTypeRadiusFixed);
        if (agentTypeId == AgentTypeHelper.LargeMovementTypeId)
            return GetPreparedAgentTypeRadiusFixed(agentTypeId, s_LargeAgentTypeRadiusFixed);

        return Fix64.FromRaw(2048);
    }

    private static Fix64 GetPreparedAgentTypeRadiusFixed(int agentTypeId, Fix64 radius)
    {
        if (!s_RuntimeAgentTypeRadiiPrepared)
        {
            throw new InvalidOperationException(
                $"FlowField runtime agent radii were not prepared before resolving agent type {agentTypeId}.");
        }
        if (radius <= Fix64.Zero)
            throw new InvalidOperationException($"Prepared FlowField agent radius is invalid. agentType={agentTypeId}, raw={radius.RawValue}.");

        return radius;
    }

    private static Fix64 ResolveConfiguredAgentTypeRadiusFixed(string configKey)
    {
        Fix64 tableRadius = FixedConfigReader.ReadRequiredPositiveFixedConfig(configKey);
        return (tableRadius);
    }

    private static float ResolveCollisionRadius(IEntityContext entity)
    {
        return (float)ResolveCollisionRadiusFixed(entity);
    }

    private static Fix64 ResolveNavigationTargetExtentFixed(IEntityContext target)
    {
        if (target == null)
            throw new InvalidOperationException("ResolveNavigationTargetExtentFixed failed: target is null.");

        LogicCombatShape shape = LogicFrameRuntime.IsTicking
            ? LogicEntityFrameSnapshotService.GetRequiredCurrent(target).CombatShape
            : target.CombatShape;
        Fix64 extent;
        switch (shape.Kind)
        {
            case LogicCombatShapeKind.Circle:
                extent = shape.Radius;
                break;
            case LogicCombatShapeKind.AxisAlignedBox:
                extent = Fix64.Max(shape.HalfExtents.x, shape.HalfExtents.y);
                break;
            default:
                throw new InvalidOperationException(
                    $"ResolveNavigationTargetExtentFixed failed: target={target.LogicEntityId.Value}, " +
                    $"key={target.CharacterKey}, shapeKind={(int)shape.Kind}.");
        }

        if (extent <= Fix64.Zero)
        {
            throw new InvalidOperationException(
                $"ResolveNavigationTargetExtentFixed failed: target={target.LogicEntityId.Value}, " +
                $"key={target.CharacterKey}, shapeKind={shape.Kind}, extentRaw={extent.RawValue}, " +
                $"radiusRaw={shape.Radius.RawValue}, " +
                $"halfExtentsRaw=({shape.HalfExtents.x.RawValue},{shape.HalfExtents.y.RawValue}).");
        }

        return extent;
    }

    private static Fix64 ResolveCollisionRadiusFixed(IEntityContext entity)
    {
        if (entity == null)
            throw new InvalidOperationException("ResolveCollisionRadiusFixed failed: entity is null.");

        Fix64 propertyRadius = entity.GetProperty(CreatureMainProperty.CollisionRadius);
        Fix64 radius = (propertyRadius);
        if (radius <= Fix64.Zero)
        {
            LogicEntityState logicState = entity as LogicEntityState;
            string unitSize = entity.CharacterData != null ? entity.CharacterData.Size.ToString() : "<none>";
            throw new InvalidOperationException(
                $"ResolveCollisionRadiusFixed failed: entity={entity.LogicEntityId.Value}, key={entity.CharacterKey}, " +
                $"type={entity.GetType().FullName}, alive={entity.Alive}, unitSize={unitSize}, " +
                $"propertyRaw={propertyRadius.RawValue}, worldRaw={radius.RawValue}, " +
                $"navAgentType={logicState?.NavigationAgentTypeId.ToString() ?? "<unavailable>"}, " +
                $"usesFlow={logicState?.UsesFlowNavigationAgent.ToString() ?? "<unavailable>"}, " +
                $"hero={logicState?.IsHeroEntity.ToString() ?? "<unavailable>"}, " +
                $"preparedMovable={logicState?.PreparedCollisionMovable.ToString() ?? "<unavailable>"}.");
        }

        return radius;
    }
}
