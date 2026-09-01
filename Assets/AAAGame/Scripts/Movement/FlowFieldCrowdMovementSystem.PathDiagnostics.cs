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
    private static bool ShouldLogPortalDiagnostics(string characterKey, TileGoalKind goalKind)
    {
        return goalKind == TileGoalKind.Portal
               && GameDebugSettings.IsEnabled(DebugCategory.Move)
               && !string.IsNullOrEmpty(characterKey)
               && GameDebugSettings.ShouldLogMovementForCharacter(characterKey);
    }

    private static string FormatGoalCells(Vector2Int[] cells)
    {
        if (cells == null || cells.Length == 0)
            return "[]";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(cells.Length * 12);
        builder.Append('[');
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append('(');
            builder.Append(cells[i].x);
            builder.Append(',');
            builder.Append(cells[i].y);
            builder.Append(')');
        }
        builder.Append(']');
        return builder.ToString();
    }

    private static string FormatTileKey(FlowTileCacheKey key)
    {
        if (key.GoalKind == TileGoalKind.Portal)
        {
            return $"(world={key.WorldVersion},sector={key.SectorId},goalKind=Portal,exitPortal={key.GoalId},agentType={key.AgentTypeId},dirty={key.DirtyVersion})";
        }
        return $"(world={key.WorldVersion},sector={key.SectorId},goalKind=FinalGoal,goalCell={key.FinalGoalIndex},agentType={key.AgentTypeId},dirty={key.DirtyVersion})";
    }

    private static string FormatSharedGoalFieldKey(SharedGoalFieldKey key)
    {
        return $"(world={key.WorldVersion},sector={key.GoalSectorId},goalCell={key.GoalCellIndex},agentType={key.AgentTypeId},dirty={key.GoalSectorDirtyVersion})";
    }

    private static string FormatPathHandle(PathHandle handle)
    {
        if (handle == null)
            return "null";

        string sectors = handle.SectorIds == null ? "null" : string.Join("->", handle.SectorIds);
        string portals = handle.PortalIds == null ? "null" : string.Join("->", handle.PortalIds);
        string committedTile = handle.HasCommittedCurrentTileKey
            ? FormatTileKey(handle.CommittedCurrentTileKey)
            : "none";
        string committedSectors = handle.CommittedCorridorSectorIds == null
            ? "null"
            : string.Join("->", handle.CommittedCorridorSectorIds);
        string committedPortals = handle.CommittedCorridorPortalIds == null
            ? "null"
            : string.Join("->", handle.CommittedCorridorPortalIds);
        return $"(world={handle.WorldVersion},goal=({handle.GoalX},{handle.GoalY}),currentSectorIndex={handle.CurrentSectorIndex},sectors=[{sectors}],portals=[{portals}]," +
               $"committedTile={committedTile},committedGoal=({handle.CommittedCorridorGoalX},{handle.CommittedCorridorGoalY})," +
               $"committedSectors=[{committedSectors}],committedPortals=[{committedPortals}])";
    }

    private static string BuildCurrentTileSteeringDiagnostics(FlowTileCacheEntry tile, int currentX, int currentY)
    {
        if (tile == null)
            return "tile=null";
        if (!IsInsideSector(tile, currentX, currentY))
            return $"tileKey={FormatTileKey(tile.Key)} cell=outside rect=({tile.StartX},{tile.StartY},{tile.Width},{tile.Height})";

        int localIndex = tile.GetLocalIndex(currentX, currentY);
        TryGetTileIntegrationCostForDiagnostics(tile, currentX, currentY, out float currentCost, out string currentCostSource);
        Vector2 currentFlow = ResolveRuntimeFlowDirection(tile, currentX, currentY, localIndex);
        Vector2 storedFlow = GetFlowDirection(tile, localIndex);
        bool currentPathable = IsFlowPathable(tile, localIndex);
        bool currentReachable = IsFlowReachable(tile, localIndex);
        string goalCellState = BuildTileGoalCellStateDiagnostics(tile, currentX, currentY);

        int bestNeighborX = currentX;
        int bestNeighborY = currentY;
        float bestNeighborCost = currentCost;
        Vector2 bestNeighborFlow = currentFlow;
        bool bestNeighborFound = false;
        System.Text.StringBuilder neighborBuilder = new System.Text.StringBuilder(384);
        neighborBuilder.Append("[");
        int neighborCount = 0;
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int nextX = currentX + NeighborOffsetX[i];
            int nextY = currentY + NeighborOffsetY[i];
            if (!IsInsideSector(tile, nextX, nextY) || !_world.IsWalkable(nextX, nextY))
                continue;

            if (!CanTraverseNeighborCells(_world, currentX, currentY, nextX, nextY))
                continue;

            int nextIndex = tile.GetLocalIndex(nextX, nextY);
            TryGetTileIntegrationCostForDiagnostics(tile, nextX, nextY, out float nextCost, out string nextCostSource);
            Vector2 nextFlow = ResolveRuntimeFlowDirection(tile, nextX, nextY, nextIndex);
            Vector2 nextStoredFlow = GetFlowDirection(tile, nextIndex);
            if (neighborCount > 0)
                neighborBuilder.Append(" | ");
            neighborBuilder.Append("(").Append(nextX).Append(',').Append(nextY).Append("){cost=")
                .Append(float.IsPositiveInfinity(nextCost) ? "INF" : nextCost.ToString("F3"))
                .Append(",costSource=").Append(nextCostSource)
                .Append(",stored=").Append(nextStoredFlow)
                .Append(",runtime=").Append(nextFlow)
                .Append(",pathable=").Append(IsFlowPathable(tile, nextIndex))
                .Append(",reachable=").Append(IsFlowReachable(tile, nextIndex))
                .Append("}");
            neighborCount++;

            if (float.IsPositiveInfinity(nextCost))
                continue;

            if (!bestNeighborFound || nextCost < bestNeighborCost - 0.0001f)
            {
                bestNeighborFound = true;
                bestNeighborX = nextX;
                bestNeighborY = nextY;
                bestNeighborCost = nextCost;
                bestNeighborFlow = nextFlow;
            }
        }

        if (neighborCount == 0)
            neighborBuilder.Append("none");
        neighborBuilder.Append(']');

        return $"tileKey={FormatTileKey(tile.Key)} cell=({currentX},{currentY}) cost={(float.IsPositiveInfinity(currentCost) ? "INF" : currentCost.ToString("F3"))} costSource={currentCostSource} " +
               $"flow={currentFlow} stored={storedFlow} pathable={currentPathable} reachable={currentReachable} " +
               $"{goalCellState} " +
               $"bestNeighbor=({bestNeighborX},{bestNeighborY}) bestNeighborCost={(float.IsPositiveInfinity(bestNeighborCost) ? "INF" : bestNeighborCost.ToString("F3"))} " +
               $"bestNeighborFlow={bestNeighborFlow} neighbors={neighborBuilder}";
    }

    private static string BuildTileGoalCellStateDiagnostics(FlowTileCacheEntry tile, int currentX, int currentY)
    {
        if (tile?.GoalCells == null)
            return "goalCellState=none";

        int goalIndex = -1;
        for (int i = 0; i < tile.GoalCells.Length; i++)
        {
            if (tile.GoalCells[i].x == currentX && tile.GoalCells[i].y == currentY)
            {
                goalIndex = i;
                break;
            }
        }

        if (goalIndex < 0)
            return $"isGoalCell=False goalCells={FormatGoalCells(tile.GoalCells)}";

        if (tile.Key.GoalKind != TileGoalKind.Portal)
            return $"isGoalCell=True goalIndex={goalIndex} goalCells={FormatGoalCells(tile.GoalCells)}";

        PortalData portal = GetPortalById(_world, tile.Key.GoalId);
        int oppositeSectorId = GetOppositeSectorId(portal, tile.Key.SectorId);
        Vector2Int[] oppositeCells = GetPortalCellsForSector(portal, oppositeSectorId);
        if (goalIndex >= oppositeCells.Length)
            return $"isGoalCell=True goalIndex={goalIndex} opposite=out-of-range portal={FormatPortal(portal)}";

        Vector2Int opposite = oppositeCells[goalIndex];
        bool oppositeWalkable = _world.IsWalkable(opposite.x, opposite.y);
        bool traversable = oppositeWalkable && CanTraverseNeighborCells(_world, currentX, currentY, opposite.x, opposite.y);
        return $"isGoalCell=True goalIndex={goalIndex} opposite=({opposite.x},{opposite.y}) oppositeWalkable={oppositeWalkable} oppositeTraversable={traversable} portal={FormatPortal(portal)}";
    }

    private static string BuildGridPathDiagnostics(Vector3 fromPosition, Vector3 toPosition, int agentTypeId)
    {
        if (_world == null)
            return "flowPath=world-null";

        bool fromInGrid = _world.WorldToGrid(fromPosition, out int fromX, out int fromY);
        bool toInGrid = _world.WorldToGrid(toPosition, out int toX, out int toY);
        bool fromWalkable = fromInGrid && _world.IsWalkable(fromX, fromY);
        bool toWalkable = toInGrid && _world.IsWalkable(toX, toY);
        int fromIsland = fromInGrid ? ResolveIslandIdForDiagnostics(_world, fromX, fromY) : -1;
        int toIsland = toInGrid ? ResolveIslandIdForDiagnostics(_world, toX, toY) : -1;
        float distance = ResolveGridPathLengthOrInfinity(fromPosition, toPosition, agentTypeId);
        return $"flowPath={{fromCell={(fromInGrid ? $"({fromX},{fromY})" : "out")},toCell={(toInGrid ? $"({toX},{toY})" : "out")},fromWalk={fromWalkable},toWalk={toWalkable},fromIsland={fromIsland},toIsland={toIsland},sameIsland={fromIsland > 0 && fromIsland == toIsland},gridDistance={FormatDiagnosticCost(distance)}}}";
    }

    private static string BuildSelectedPortalGridTrace(int startSectorId, int startX, int startY, int portalId, int maxSteps)
    {
        if (_world == null)
            return "world-null";
        if (portalId < 0)
            return "none";
        if (!TryGetPortalById(_world, portalId, out PortalData portal))
            return "portal-missing:" + portalId;

        Vector2Int[] currentCells = GetPortalCellsForSector(portal, startSectorId);
        return "portal=" + FormatPortal(portal)
               + ",cells=" + FormatGoalCells(currentCells)
               + ",path=" + BuildGridShortestPathTraceToCells(startX, startY, currentCells, maxSteps);
    }

    private static string BuildHandlePortalRouteDiagnostics(PathHandle handle, int startSectorId, int startX, int startY, int goalX, int goalY, int maxSteps)
    {
        if (_world == null)
            return "world-null";
        if (handle == null || handle.SectorIds == null || handle.PortalIds == null)
            return "handle-null";

        int sectorIndex = FindSectorIndex(handle, startSectorId, Mathf.Max(0, handle.CurrentSectorIndex));
        if (sectorIndex < 0)
            return "start-sector-not-in-handle:" + FormatPathHandle(handle);

        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append(FormatPathHandle(handle));
        int cursorX = -1;
        int cursorY = -1;
        for (int i = sectorIndex; i < handle.PortalIds.Length; i++)
        {
            int sectorId = handle.SectorIds[i];
            int portalId = handle.PortalIds[i];
            if (!TryGetPortalById(_world, portalId, out PortalData portal))
            {
                builder.Append("|missingPortal=").Append(portalId);
                continue;
            }

            Vector2Int[] currentCells = GetPortalCellsForSector(portal, sectorId);
            Vector2Int[] oppositeCells = GetPortalCellsForSector(portal, handle.SectorIds[i + 1]);
            builder.Append("|seg").Append(i)
                .Append("{sector=").Append(sectorId)
                .Append(",portal=").Append(portalId)
                .Append(",cur=").Append(FormatGoalCells(currentCells))
                .Append(",opp=").Append(FormatGoalCells(oppositeCells));
            if (i == sectorIndex)
                builder.Append(",fromStart=").Append(BuildGridShortestPathTraceToCells(startX, startY, currentCells, maxSteps));
            if (oppositeCells != null && oppositeCells.Length > 0)
            {
                Vector2Int opposite = oppositeCells[oppositeCells.Length / 2];
                cursorX = opposite.x;
                cursorY = opposite.y;
            }
            builder.Append('}');
        }

        if (cursorX >= 0)
            builder.Append("|lastOppToGoal=").Append(BuildGridShortestPathTraceToCell(cursorX, cursorY, goalX, goalY, maxSteps));
        return builder.ToString();
    }

    private static string BuildGridShortestPathTraceToCell(int startX, int startY, int goalX, int goalY, int maxSteps)
    {
        if (_world == null)
            return "world-null";
        if (goalX < 0 || goalX >= _world.Width || goalY < 0 || goalY >= _world.Height || !_world.IsWalkable(goalX, goalY))
            return "goal-invalid";

        bool[] targets = new bool[_world.Width * _world.Height];
        targets[_world.GetIndex(goalX, goalY)] = true;
        return BuildGridShortestPathTraceToTargets(startX, startY, targets, maxSteps);
    }

    private static string BuildGridShortestPathTraceToCells(int startX, int startY, Vector2Int[] targetCells, int maxSteps)
    {
        if (_world == null)
            return "world-null";
        if (targetCells == null || targetCells.Length == 0)
            return "no-targets";

        bool[] targets = new bool[_world.Width * _world.Height];
        bool hasTarget = false;
        for (int i = 0; i < targetCells.Length; i++)
        {
            Vector2Int cell = targetCells[i];
            if (cell.x < 0 || cell.x >= _world.Width || cell.y < 0 || cell.y >= _world.Height || !_world.IsWalkable(cell.x, cell.y))
                continue;

            targets[_world.GetIndex(cell.x, cell.y)] = true;
            hasTarget = true;
        }

        return hasTarget ? BuildGridShortestPathTraceToTargets(startX, startY, targets, maxSteps) : "no-walkable-targets";
    }

    private static string BuildGridShortestPathTraceToTargets(int startX, int startY, bool[] targets, int maxSteps)
    {
        if (_world == null)
            return "world-null";
        int cellCount = _world.Width * _world.Height;
        if (targets == null || targets.Length != cellCount)
            return "invalid-targets";
        if (startX < 0 || startX >= _world.Width || startY < 0 || startY >= _world.Height || !_world.IsWalkable(startX, startY))
            return "start-invalid";

        int startIndex = _world.GetIndex(startX, startY);
        if (targets[startIndex])
            return "(" + startX + "," + startY + "):target";

        float[] costs = RentIntegrationArray(cellCount);
        int[] cameFrom = new int[cellCount];
        for (int i = 0; i < cameFrom.Length; i++)
            cameFrom[i] = -1;

        InitializeIntegrationField(costs);
        MinHeap openSet = new MinHeap();
        costs[startIndex] = 0f;
        openSet.Push(startIndex, 0f);
        int reachedIndex = -1;
        int guard = cellCount * 4;
        try
        {
            while (openSet.Count > 0 && guard-- > 0)
            {
                QueueNode node = openSet.Pop();
                if (node.Cost > costs[node.Index] + 0.001f)
                    continue;
                if (targets[node.Index])
                {
                    reachedIndex = node.Index;
                    break;
                }

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
                    cameFrom[nextIndex] = node.Index;
                    openSet.Push(nextIndex, newCost);
                }
            }

            if (reachedIndex < 0)
                return guard <= 0 ? "guard-exceeded" : "unreachable";

            return FormatReconstructedGridPath(cameFrom, startIndex, reachedIndex, maxSteps, costs[reachedIndex]);
        }
        finally
        {
            ReturnIntegrationArray(costs);
        }
    }

    private static string FormatReconstructedGridPath(int[] cameFrom, int startIndex, int reachedIndex, int maxSteps, float totalCost)
    {
        if (_world == null || cameFrom == null)
            return "invalid-reconstruct";

        List<int> reversed = new List<int>(Mathf.Max(8, maxSteps + 1));
        int cursor = reachedIndex;
        int guard = cameFrom.Length + 1;
        while (cursor >= 0 && guard-- > 0)
        {
            reversed.Add(cursor);
            if (cursor == startIndex)
                break;
            cursor = cameFrom[cursor];
        }

        if (reversed[reversed.Count - 1] != startIndex)
            return "reconstruct-failed";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(maxSteps * 12 + 64);
        builder.Append("cost=").Append(FormatDiagnosticCost(totalCost)).Append(",path=");
        int emitted = 0;
        for (int i = reversed.Count - 1; i >= 0 && emitted <= maxSteps; i--, emitted++)
        {
            if (emitted > 0)
                builder.Append("->");
            int index = reversed[i];
            builder.Append('(').Append(index % _world.Width).Append(',').Append(index / _world.Width).Append(')');
        }

        if (emitted < reversed.Count)
            builder.Append("->...");
        return builder.ToString();
    }

    private static bool TryResolveForwardBlockedProbe(int startX, int startY, Vector3 desiredDirection, out string probe, out int blockedX, out int blockedY)
    {
        blockedX = startX;
        blockedY = startY;
        probe = "none";
        if (_world == null)
            return false;

        Vector3 direction = desiredDirection;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return false;
        direction.Normalize();

        int stepX = direction.x > 0.35f ? 1 : direction.x < -0.35f ? -1 : 0;
        int stepY = direction.z > 0.35f ? 1 : direction.z < -0.35f ? -1 : 0;
        if (stepX == 0 && stepY == 0)
            return false;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(256);
        int previousX = startX;
        int previousY = startY;
        bool foundBlocked = false;
        for (int step = 1; step <= 5; step++)
        {
            int x = startX + stepX * step;
            int y = startY + stepY * step;
            if (step > 1)
                builder.Append(" -> ");

            AppendForwardProbeCell(builder, previousX, previousY, x, y);
            bool blocked = x < 0
                           || x >= _world.Width
                           || y < 0
                           || y >= _world.Height
                           || !_world.IsWalkable(x, y)
                           || !CanTraverseNeighborCells(_world, previousX, previousY, x, y);
            if (blocked)
            {
                blockedX = x;
                blockedY = y;
                foundBlocked = true;
                break;
            }

            previousX = x;
            previousY = y;
        }

        probe = builder.ToString();
        return foundBlocked;
    }

    private static void AppendForwardProbeCell(System.Text.StringBuilder builder, int previousX, int previousY, int x, int y)
    {
        builder.Append('(').Append(x).Append(',').Append(y).Append("){");
        if (_world == null || x < 0 || x >= _world.Width || y < 0 || y >= _world.Height)
        {
            builder.Append("out}");
            return;
        }

        int index = _world.GetIndex(x, y);
        bool walkable = _world.IsWalkable(x, y);
        bool baseWalkable = _world.BaseWalkableMask != null
                            && _world.BaseWalkableMask.Length == _world.Width * _world.Height
                            && _world.BaseWalkableMask[index];
        byte cost = 0;
        TryGetCostFieldValue(_world, x, y, out cost);
        int island = ResolveIslandId(_world, x, y);
        string mask = _world.NeighborTraversalMask != null && _world.NeighborTraversalMask.Length == _world.Width * _world.Height
            ? _world.NeighborTraversalMask[index].ToString("X2")
            : "null";
        bool link = previousX == x && previousY == y || CanTraverseNeighborCells(_world, previousX, previousY, x, y);
        builder.Append("walk=").Append(walkable)
            .Append(",base=").Append(baseWalkable)
            .Append(",cost=").Append(cost)
            .Append(",island=").Append(island)
            .Append(",mask=0x").Append(mask)
            .Append(",link=").Append(link)
            .Append('}');
    }

    private static string BuildFlowDirectionCostDiagnostics(FlowTileCacheEntry tile, int currentX, int currentY)
    {
        if (_world == null || tile == null)
            return "{worldOrTile=null}";
        if (!IsInsideSector(tile, currentX, currentY))
            return "{cell=outside}";

        bool rebuiltDebug = false;
        if (tile.DebugIntegrationPayload?.Values == null)
            rebuiltDebug = TryRebuildTileDebugIntegration(tile);

        TryGetTileIntegrationCostForDiagnostics(tile, currentX, currentY, out float currentCost, out string currentCostSource);
        int localIndex = tile.GetLocalIndex(currentX, currentY);
        Vector2 runtimeFlow = ResolveRuntimeFlowDirection(tile, currentX, currentY, localIndex);
        int flowStepX = runtimeFlow.x > 0.35f ? 1 : runtimeFlow.x < -0.35f ? -1 : 0;
        int flowStepY = runtimeFlow.y > 0.35f ? 1 : runtimeFlow.y < -0.35f ? -1 : 0;
        int flowNextX = currentX + flowStepX;
        int flowNextY = currentY + flowStepY;
        bool flowNextInside = flowStepX != 0 || flowStepY != 0;
        flowNextInside = flowNextInside && IsInsideSector(tile, flowNextX, flowNextY);
        bool flowNextWalkable = flowNextInside && _world.IsWalkable(flowNextX, flowNextY);
        bool flowNextTraversable = flowNextWalkable && CanTraverseNeighborCells(_world, currentX, currentY, flowNextX, flowNextY);
        float flowNextCost = float.PositiveInfinity;
        string flowNextCostSource = "missing";
        if (flowNextInside)
            TryGetTileIntegrationCostForDiagnostics(tile, flowNextX, flowNextY, out flowNextCost, out flowNextCostSource);

        int bestX = currentX;
        int bestY = currentY;
        float bestCost = currentCost;
        string bestSource = currentCostSource;
        bool foundBetterNeighbor = false;
        System.Text.StringBuilder neighbors = new System.Text.StringBuilder(768);
        neighbors.Append('[');
        int neighborCount = 0;
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int nextX = currentX + NeighborOffsetX[i];
            int nextY = currentY + NeighborOffsetY[i];
            if (!IsInsideSector(tile, nextX, nextY))
                continue;

            bool walkable = _world.IsWalkable(nextX, nextY);
            bool traversable = walkable && CanTraverseNeighborCells(_world, currentX, currentY, nextX, nextY);
            TryGetTileIntegrationCostForDiagnostics(tile, nextX, nextY, out float nextCost, out string nextSource);
            if (neighborCount > 0)
                neighbors.Append(" | ");
            neighbors.Append('(').Append(nextX).Append(',').Append(nextY).Append("){cost=")
                .Append(FormatDiagnosticCost(nextCost))
                .Append(",source=").Append(nextSource)
                .Append(",walk=").Append(walkable)
                .Append(",trav=").Append(traversable)
                .Append(",flow=").Append(ResolveRuntimeFlowDirection(tile, nextX, nextY, tile.GetLocalIndex(nextX, nextY)))
                .Append('}');
            neighborCount++;

            if (!traversable || float.IsPositiveInfinity(nextCost))
                continue;
            if (!foundBetterNeighbor || nextCost < bestCost - 0.001f)
            {
                foundBetterNeighbor = true;
                bestX = nextX;
                bestY = nextY;
                bestCost = nextCost;
                bestSource = nextSource;
            }
        }

        if (neighborCount == 0)
            neighbors.Append("none");
        neighbors.Append(']');

        bool flowDropsCost = flowNextTraversable
                             && !float.IsPositiveInfinity(currentCost)
                             && flowNextCost < currentCost - 0.001f;
        bool flowMatchesBest = foundBetterNeighbor && flowNextX == bestX && flowNextY == bestY;
        return "{rebuiltDebug=" + rebuiltDebug
               + ",currentCost=" + FormatDiagnosticCost(currentCost)
               + ",currentSource=" + currentCostSource
               + ",runtimeFlow=" + runtimeFlow
               + ",flowNext=(" + flowNextX + "," + flowNextY + ")"
               + ",flowNextInside=" + flowNextInside
               + ",flowNextWalkable=" + flowNextWalkable
               + ",flowNextTraversable=" + flowNextTraversable
               + ",flowNextCost=" + FormatDiagnosticCost(flowNextCost)
               + ",flowNextSource=" + flowNextCostSource
               + ",flowDropsCost=" + flowDropsCost
               + ",best=(" + bestX + "," + bestY + ")"
               + ",bestCost=" + FormatDiagnosticCost(bestCost)
               + ",bestSource=" + bestSource
               + ",flowMatchesBest=" + flowMatchesBest
               + ",neighbors=" + neighbors
               + "}";
    }

    private static string BuildFlowDirectionProbeDiagnostics(
        Vector3 startPosition,
        int startX,
        int startY,
        Vector3 desiredDirection,
        Vector2 runtimeFlow,
        int agentTypeId)
    {
        if (_world == null)
            return "{world=null}";

        Vector3 probeDirection = desiredDirection;
        probeDirection.y = 0f;
        if (probeDirection.sqrMagnitude <= 0.0001f && runtimeFlow.sqrMagnitude > 0.0001f)
            probeDirection = new Vector3(runtimeFlow.x, 0f, runtimeFlow.y);
        if (probeDirection.sqrMagnitude <= 0.0001f)
            return "{direction=zero}";

        probeDirection.Normalize();
        float probeDistance = Mathf.Max(_world.CellSize * 8f, 4f);
        Vector3 endPosition = startPosition + probeDirection * probeDistance;
        bool endInGrid = _world.WorldToGrid(endPosition, out int endX, out int endY);

        System.Text.StringBuilder builder = new System.Text.StringBuilder(1536);
        builder.Append("{dir=").Append(probeDirection)
            .Append(",end=").Append(endPosition)
            .Append(",endInGrid=").Append(endInGrid)
            .Append(",segment=");
        if (endInGrid)
            builder.Append(BuildLineDecisionSegmentDiagnostics(startPosition, endPosition, startX, startY, endX, endY, agentTypeId));
        else
            builder.Append("end-out");

        builder.Append(",flowSteps=[");
        int stepX = runtimeFlow.x > 0.35f ? 1 : runtimeFlow.x < -0.35f ? -1 : 0;
        int stepY = runtimeFlow.y > 0.35f ? 1 : runtimeFlow.y < -0.35f ? -1 : 0;
        if (stepX == 0 && stepY == 0)
        {
            builder.Append("none");
        }
        else
        {
            int previousX = startX;
            int previousY = startY;
            for (int step = 1; step <= 6; step++)
            {
                if (step > 1)
                    builder.Append(" -> ");
                int x = startX + stepX * step;
                int y = startY + stepY * step;
                AppendLineDecisionCell(builder, _world, x, y, previousX, previousY, first: false);
                previousX = x;
                previousY = y;
                if (x < 0 || x >= _world.Width || y < 0 || y >= _world.Height || !_world.IsWalkable(x, y))
                    break;
            }
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static string BuildStartPortalRankDiagnostics(
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int agentTypeId,
        Vector3 startPosition,
        Vector3 stableGoalPosition,
        PathHandle handle = null)
    {
        if (_world == null)
            return "[FlowPortalRankDiag world=null]";
        if (startSectorId < 0 || startSectorId >= _world.Sectors.Length)
            return $"[FlowPortalRankDiag invalid-start-sector startSector={startSectorId}]";
        if (!TryGetCachedSharedGoalField(goalSectorId, goalX, goalY, ResolvePreferredAgentTypeId(agentTypeId), out SharedGoalField sharedField))
            return "[FlowPortalRankDiag shared-field-not-cached]";

        SectorData startSector = _world.Sectors[startSectorId];
        int selectedPortalId = ResolveSelectedPortalIdForDiagnostics(handle, startSectorId);
        int bestStartNodePortalId = -1;
        int bestFlowCrossingPortalId = -1;
        float bestFlowCrossingTotal = float.PositiveInfinity;
        int bestGridDistancePortalId = -1;
        float bestGridDistanceTotal = float.PositiveInfinity;
        List<string> candidates = new List<string>(startSector.PortalIds.Count);

        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            int startNode = EncodePortalNode(startSectorId, portalId);
            long downstreamCostRaw = long.MaxValue;
            bool hasDownstream = sharedField.SettledPortalNodes.Contains(startNode)
                                 && sharedField.NodeCosts.TryGetValue(startNode, out downstreamCostRaw);
            float accessCost = ResolvePortalAccessCost(startSector, startSectorId, portalId, startX, startY);
            float downstreamCost = DequantizeDeterministicPortalCost(downstreamCostRaw);
            float flowTotal = hasDownstream && !float.IsPositiveInfinity(accessCost)
                ? DequantizeDeterministicPortalCost(AddDeterministicPortalCosts(
                    QuantizeDeterministicPortalCost(accessCost, "diagnostic-flow-access"),
                    downstreamCostRaw))
                : float.PositiveInfinity;
            int firstCrossingPortalId = -1;
            bool hasCrossing = hasDownstream
                               && !float.IsPositiveInfinity(accessCost)
                               && TryResolveFirstCrossingPortalFromSharedGoalField(sharedField, startNode, startSectorId, goalSectorId, out firstCrossingPortalId);
            if (hasCrossing && flowTotal < bestFlowCrossingTotal)
            {
                bestFlowCrossingTotal = flowTotal;
                bestStartNodePortalId = portalId;
                bestFlowCrossingPortalId = firstCrossingPortalId;
            }

            float gridDistanceTotal = float.PositiveInfinity;
            float gridAccess = float.PositiveInfinity;
            float gridDownstream = float.PositiveInfinity;
            float gridTotal = float.PositiveInfinity;
            string portalStructure = "portal=missing";
            if (TryGetPortalById(_world, portalId, out PortalData portal))
            {
                Vector2Int[] currentCells = GetPortalCellsForSector(portal, startSectorId);
                Vector2Int[] oppositeCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, startSectorId));
                portalStructure = BuildPortalCandidateStructureDiagnostics(portal, startSectorId, currentCells, oppositeCells);
                Vector3 currentCenter = ResolveCellGroupCenter(currentCells);
                Vector3 oppositeCenter = ResolveCellGroupCenter(oppositeCells);
                float currentGridDistance = ResolveGridPathLengthOrInfinity(startPosition, currentCenter, agentTypeId)
                                   + ResolveGridPathLengthOrInfinity(currentCenter, stableGoalPosition, agentTypeId);
                float oppositeGridDistance = ResolveGridPathLengthOrInfinity(startPosition, oppositeCenter, agentTypeId)
                                    + ResolveGridPathLengthOrInfinity(oppositeCenter, stableGoalPosition, agentTypeId);
                gridDistanceTotal = Mathf.Min(currentGridDistance, oppositeGridDistance);
                gridAccess = ResolveGridPathCostToAnyOrInfinity(startX, startY, currentCells);
                gridDownstream = ResolveGridPathCostFromAnyToCellOrInfinity(oppositeCells, goalX, goalY);
                bool hasTraversablePortalPair = false;
                int pairCount = Mathf.Min(currentCells.Length, oppositeCells.Length);
                for (int pairIndex = 0; pairIndex < pairCount; pairIndex++)
                {
                    Vector2Int currentCell = currentCells[pairIndex];
                    Vector2Int oppositeCell = oppositeCells[pairIndex];
                    if (CanTraverseNeighborCells(_world, currentCell.x, currentCell.y, oppositeCell.x, oppositeCell.y))
                    {
                        hasTraversablePortalPair = true;
                        break;
                    }
                }

                if (hasTraversablePortalPair
                    && !float.IsPositiveInfinity(gridAccess)
                    && !float.IsPositiveInfinity(gridDownstream))
                {
                    gridTotal = gridAccess + ResolvePortalCrossingCost(portal) + gridDownstream;
                }
            }

            if (gridDistanceTotal < bestGridDistanceTotal)
            {
                bestGridDistanceTotal = gridDistanceTotal;
                bestGridDistancePortalId = portalId;
            }

            candidates.Add(
                "{id=" + portalId
                + ",selected=" + (portalId == selectedPortalId)
                + ",flowAccess=" + FormatDiagnosticCost(accessCost)
                + ",flowDownstream=" + (hasDownstream ? FormatDiagnosticCost(downstreamCost) : "missing")
                + ",flowTotal=" + FormatDiagnosticCost(flowTotal)
                + ",firstCrossing=" + (hasCrossing ? firstCrossingPortalId.ToString() : "missing")
                + ",gridDistanceTotal=" + FormatDiagnosticCost(gridDistanceTotal)
                + ",gridAccess=" + FormatDiagnosticCost(gridAccess)
                + ",gridDownstream=" + FormatDiagnosticCost(gridDownstream)
                + ",gridTotal=" + FormatDiagnosticCost(gridTotal)
                + "," + portalStructure
                + "}");
        }

        return "[FlowPortalRankDiag startSector=" + startSectorId
               + " goalSector=" + goalSectorId
               + " start=(" + startX + "," + startY + ")"
               + " goal=(" + goalX + "," + goalY + ")"
               + " selected=" + selectedPortalId
               + " bestFlowCrossing=" + bestFlowCrossingPortalId + ":" + FormatDiagnosticCost(bestFlowCrossingTotal)
               + " bestStartNode=" + bestStartNodePortalId
               + " bestGridDistance=" + bestGridDistancePortalId + ":" + FormatDiagnosticCost(bestGridDistanceTotal)
               + " handle=" + FormatPathHandle(handle)
               + " candidates=" + string.Join(" | ", candidates)
               + "]";
    }

    private static string BuildPortalCandidateStructureDiagnostics(
        PortalData portal,
        int sectorId,
        Vector2Int[] currentCells,
        Vector2Int[] oppositeCells)
    {
        if (_world == null)
            return "portalStruct=world-null";
        if (portal == null)
            return "portalStruct=null";
        if (currentCells == null || oppositeCells == null || currentCells.Length != oppositeCells.Length)
            return "portalStruct=side-mismatch";

        int pairCount = Mathf.Min(currentCells.Length, oppositeCells.Length);
        int traversablePairs = 0;
        int currentCostSum = 0;
        int oppositeCostSum = 0;
        int currentCostMax = 0;
        int oppositeCostMax = 0;
        int minCurrentAwayRun = int.MaxValue;
        int minOppositeAwayRun = int.MaxValue;
        int minCurrentLateralRun = int.MaxValue;
        int minOppositeLateralRun = int.MaxValue;
        System.Text.StringBuilder pairBuilder = new System.Text.StringBuilder(128);

        for (int i = 0; i < pairCount; i++)
        {
            Vector2Int currentCell = currentCells[i];
            Vector2Int oppositeCell = oppositeCells[i];
            bool traversable = CanTraverseNeighborCells(_world, currentCell.x, currentCell.y, oppositeCell.x, oppositeCell.y);
            if (traversable)
                traversablePairs++;

            int currentCost = GetCostFieldValueStrict(_world, currentCell.x, currentCell.y);
            int oppositeCost = GetCostFieldValueStrict(_world, oppositeCell.x, oppositeCell.y);
            currentCostSum += currentCost;
            oppositeCostSum += oppositeCost;
            currentCostMax = Mathf.Max(currentCostMax, currentCost);
            oppositeCostMax = Mathf.Max(oppositeCostMax, oppositeCost);

            int dx = Math.Sign(oppositeCell.x - currentCell.x);
            int dy = Math.Sign(oppositeCell.y - currentCell.y);
            int currentAwayRun = CountWalkableRun(currentCell.x, currentCell.y, -dx, -dy, 8);
            int oppositeAwayRun = CountWalkableRun(oppositeCell.x, oppositeCell.y, dx, dy, 8);
            int lateralDx = portal.IsVerticalBoundary ? 0 : 1;
            int lateralDy = portal.IsVerticalBoundary ? 1 : 0;
            int currentLateralRun = 1
                                    + CountWalkableRun(currentCell.x, currentCell.y, lateralDx, lateralDy, 8)
                                    + CountWalkableRun(currentCell.x, currentCell.y, -lateralDx, -lateralDy, 8);
            int oppositeLateralRun = 1
                                     + CountWalkableRun(oppositeCell.x, oppositeCell.y, lateralDx, lateralDy, 8)
                                     + CountWalkableRun(oppositeCell.x, oppositeCell.y, -lateralDx, -lateralDy, 8);
            minCurrentAwayRun = Mathf.Min(minCurrentAwayRun, currentAwayRun);
            minOppositeAwayRun = Mathf.Min(minOppositeAwayRun, oppositeAwayRun);
            minCurrentLateralRun = Mathf.Min(minCurrentLateralRun, currentLateralRun);
            minOppositeLateralRun = Mathf.Min(minOppositeLateralRun, oppositeLateralRun);

            if (i < 4)
            {
                if (pairBuilder.Length > 0)
                    pairBuilder.Append(';');
                pairBuilder.Append('i').Append(i)
                    .Append(":c=").Append(currentCell.x).Append(',').Append(currentCell.y)
                    .Append("/o=").Append(oppositeCell.x).Append(',').Append(oppositeCell.y)
                    .Append("/tc=").Append(traversable)
                    .Append("/cost=").Append(currentCost).Append(',').Append(oppositeCost)
                    .Append("/away=").Append(currentAwayRun).Append(',').Append(oppositeAwayRun)
                    .Append("/lat=").Append(currentLateralRun).Append(',').Append(oppositeLateralRun);
            }
        }

        float currentCostAvg = pairCount > 0 ? currentCostSum / (float)pairCount : float.PositiveInfinity;
        float oppositeCostAvg = pairCount > 0 ? oppositeCostSum / (float)pairCount : float.PositiveInfinity;
        return "portalWidth=" + portal.WidthCells
               + ",portalNarrow=" + portal.IsNarrow
               + ",portalCrossCost=" + FormatDiagnosticCost(ResolvePortalCrossingCost(portal))
               + ",travPairs=" + traversablePairs + "/" + pairCount
               + ",avgCost=" + FormatDiagnosticCost(currentCostAvg) + "/" + FormatDiagnosticCost(oppositeCostAvg)
               + ",maxCost=" + currentCostMax + "/" + oppositeCostMax
               + ",minAwayRun=" + (minCurrentAwayRun == int.MaxValue ? "NA" : minCurrentAwayRun.ToString()) + "/" + (minOppositeAwayRun == int.MaxValue ? "NA" : minOppositeAwayRun.ToString())
               + ",minLatRun=" + (minCurrentLateralRun == int.MaxValue ? "NA" : minCurrentLateralRun.ToString()) + "/" + (minOppositeLateralRun == int.MaxValue ? "NA" : minOppositeLateralRun.ToString())
               + ",pairs=[" + pairBuilder + "]";
    }

    private static int CountWalkableRun(int startX, int startY, int stepX, int stepY, int maxSteps)
    {
        if (_world == null)
            throw new InvalidOperationException("CountWalkableRun failed: world is null.");
        return CountWalkableRun(_world, startX, startY, stepX, stepY, maxSteps);
    }

    private static int CountWalkableRun(NavigationWorld world, int startX, int startY, int stepX, int stepY, int maxSteps)
    {
        if (world == null)
            throw new InvalidOperationException("CountWalkableRun failed: world is null.");
        if (stepX == 0 && stepY == 0)
            return 0;

        int count = 0;
        for (int i = 1; i <= maxSteps; i++)
        {
            int x = startX + stepX * i;
            int y = startY + stepY * i;
            if (x < 0 || x >= world.Width || y < 0 || y >= world.Height)
                break;
            if (!world.IsWalkable(x, y))
                break;
            if (!CanTraverseNeighborCells(world, x - stepX, y - stepY, x, y))
                break;

            count++;
        }

        return count;
    }

    private static string BuildPathRepathDecisionDiagnostics(PathHandle handle, int startSectorId, int goalSectorId, int startX, int startY, int goalX, int goalY)
    {
        if (_world == null)
            return "world=null";
        if (handle == null)
            return "handle=null";
        if (startSectorId < 0 || startSectorId >= _world.Sectors.Length)
            return "invalid-start-sector";
        if (handle.PortalIds == null || handle.PortalIds.Length == 0)
            return "no-portals";

        int sectorIndex = FindSectorIndex(handle, startSectorId, Mathf.Max(0, handle.CurrentSectorIndex));
        if (sectorIndex < 0)
            return "start-sector-not-in-handle";
        if (sectorIndex >= handle.PortalIds.Length)
            return "final-sector";

        int selectedPortalId = handle.PortalIds[sectorIndex];
        int sharedAgentTypeId = ResolvePreferredAgentTypeId(_world.AgentTypeId);
        bool sharedCached = TryGetCachedSharedGoalField(goalSectorId, goalX, goalY, sharedAgentTypeId, out SharedGoalField sharedField);
        bool pending = PendingSharedGoalFieldBuildJobs.Contains(CreateSharedGoalFieldKey(goalSectorId, goalX, goalY, sharedAgentTypeId));
        if (!sharedCached)
        {
            return "selected=" + selectedPortalId
                   + " sharedCached=False pending=" + pending
                   + " queue=" + SharedGoalFieldBuildQueue.Count;
        }

        SectorData startSector = _world.Sectors[startSectorId];
        int bestPortalId = -1;
        int bestStartNodePortalId = -1;
        float selectedCost = float.PositiveInfinity;
        float bestCost = float.PositiveInfinity;
        System.Text.StringBuilder candidates = new System.Text.StringBuilder(256);
        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            int startNode = EncodePortalNode(startSectorId, portalId);
            long downstreamCostRaw = long.MaxValue;
            bool hasDownstream = sharedField.SettledPortalNodes.Contains(startNode)
                                 && sharedField.NodeCosts.TryGetValue(startNode, out downstreamCostRaw);
            float accessCost = ResolvePortalAccessCost(startSector, startSectorId, portalId, startX, startY);
            float downstreamCost = DequantizeDeterministicPortalCost(downstreamCostRaw);
            float totalCost = hasDownstream && !float.IsPositiveInfinity(accessCost)
                ? DequantizeDeterministicPortalCost(AddDeterministicPortalCosts(
                    QuantizeDeterministicPortalCost(accessCost, "diagnostic-repath-access"),
                    downstreamCostRaw))
                : float.PositiveInfinity;
            int firstCrossingPortalId = -1;
            bool hasCrossing = hasDownstream
                               && !float.IsPositiveInfinity(accessCost)
                               && TryResolveFirstCrossingPortalFromSharedGoalField(sharedField, startNode, startSectorId, goalSectorId, out firstCrossingPortalId);
            if (hasCrossing && firstCrossingPortalId == selectedPortalId && totalCost < selectedCost)
                selectedCost = totalCost;
            if (hasCrossing && totalCost < bestCost)
            {
                bestCost = totalCost;
                bestPortalId = firstCrossingPortalId;
                bestStartNodePortalId = portalId;
            }

            if (i > 0)
                candidates.Append(" | ");
            candidates.Append(portalId)
                .Append(":a=").Append(FormatDiagnosticCost(accessCost))
                .Append(",d=").Append(hasDownstream ? FormatDiagnosticCost(downstreamCost) : "missing")
                .Append(",t=").Append(FormatDiagnosticCost(totalCost))
                .Append(",firstCrossing=").Append(hasCrossing ? firstCrossingPortalId.ToString() : "missing");
        }

        bool shouldRebuild = selectedPortalId != bestPortalId
                             && selectedCost > bestCost + IntegrationSignificantImprovement;
        return "selected=" + selectedPortalId + ":" + FormatDiagnosticCost(selectedCost)
               + " best=" + bestPortalId + ":" + FormatDiagnosticCost(bestCost)
               + " bestStartNode=" + bestStartNodePortalId
               + " threshold=" + IntegrationSignificantImprovement.ToString("F3")
               + " shouldRebuild=" + shouldRebuild
               + " sharedNodes=" + sharedField.NodeCosts.Count
               + " candidates=[" + candidates + "]";
    }

    private static string BuildStartPortalChoiceDiagnostics(
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int agentTypeId,
        Vector3 startPosition,
        Vector3 stableGoalPosition,
        PathHandle handle = null)
    {
        if (_world == null)
            return "portalChoice=world-null";
        if (startSectorId < 0 || startSectorId >= _world.Sectors.Length)
            return $"portalChoice=invalid-start-sector startSector={startSectorId}";

        if (!TryGetCachedSharedGoalField(goalSectorId, goalX, goalY, ResolvePreferredAgentTypeId(agentTypeId), out SharedGoalField sharedField))
            return "portalChoice=shared-field-not-cached";

        SectorData startSector = _world.Sectors[startSectorId];
        System.Text.StringBuilder builder = new System.Text.StringBuilder(1024);
        int selectedPortalId = ResolveSelectedPortalIdForDiagnostics(handle, startSectorId);
        builder.Append("[FlowStartPortalChoiceDiag startSector=").Append(startSectorId)
            .Append(" goalSector=").Append(goalSectorId)
            .Append(" start=(").Append(startX).Append(',').Append(startY).Append(')')
            .Append(" goal=(").Append(goalX).Append(',').Append(goalY).Append(')')
            .Append(" selectedPortal=").Append(selectedPortalId)
            .Append(" handle=").Append(FormatPathHandle(handle))
            .Append(" portals=");

        if (startSector.PortalIds.Count == 0)
        {
            builder.Append("none]");
            return builder.ToString();
        }

        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            if (i > 0)
                builder.Append(" | ");

            int portalId = startSector.PortalIds[i];
            int startNode = EncodePortalNode(startSectorId, portalId);
            long downstreamCostRaw = long.MaxValue;
            bool hasDownstream = sharedField.SettledPortalNodes.Contains(startNode)
                                 && sharedField.NodeCosts.TryGetValue(startNode, out downstreamCostRaw);
            float accessCost = ResolvePortalAccessCost(startSector, startSectorId, portalId, startX, startY);
            float downstreamCost = DequantizeDeterministicPortalCost(downstreamCostRaw);
            float totalCost = hasDownstream && !float.IsPositiveInfinity(accessCost)
                ? DequantizeDeterministicPortalCost(AddDeterministicPortalCosts(
                    QuantizeDeterministicPortalCost(accessCost, "diagnostic-start-choice-access"),
                    downstreamCostRaw))
                : float.PositiveInfinity;
            PortalData portal = TryGetPortalById(_world, portalId, out PortalData portalData) ? portalData : null;
            int oppositeSector = portal != null ? GetOppositeSectorId(portal, startSectorId) : -1;
            Vector2Int[] cells = portal != null ? GetPortalCellsForSector(portal, startSectorId) : null;
            Vector3 center = portal != null ? portal.WorldCenter : Vector3.zero;
            string portalNav = portal != null
                ? BuildPortalCandidateNavDiagnostics(startPosition, stableGoalPosition, portal, startSectorId, agentTypeId)
                : "portal=null";
            builder.Append("{id=").Append(portalId)
                .Append(",selected=").Append(portalId == selectedPortalId)
                .Append(",opp=").Append(oppositeSector)
                .Append(",access=").Append(float.IsPositiveInfinity(accessCost) ? "INF" : accessCost.ToString("F3"))
                .Append(",downstream=").Append(hasDownstream ? downstreamCost.ToString("F3") : "missing")
                .Append(",total=").Append(float.IsPositiveInfinity(totalCost) ? "INF" : totalCost.ToString("F3"))
                .Append(",center=").Append(center)
                .Append(",cells=").Append(FormatGoalCells(cells))
                .Append(",nav=").Append(portalNav)
                .Append('}');
        }

        builder.Append("]");
        return builder.ToString();
    }

    private static int ResolveSelectedPortalIdForDiagnostics(PathHandle handle, int startSectorId)
    {
        if (handle == null || handle.SectorIds == null || handle.PortalIds == null)
            return -1;

        int index = FindSectorIndex(handle, startSectorId, Mathf.Max(0, handle.CurrentSectorIndex));
        if (index < 0 || index >= handle.PortalIds.Length)
            return -1;

        return handle.PortalIds[index];
    }

    private static string BuildPortalCandidateNavDiagnostics(Vector3 startPosition, Vector3 stableGoalPosition, PortalData portal, int sectorId, int agentTypeId)
    {
        Vector2Int[] currentCells = GetPortalCellsForSector(portal, sectorId);
        Vector2Int[] oppositeCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, sectorId));
        Vector3 currentCenter = ResolveCellGroupCenter(currentCells);
        Vector3 oppositeCenter = ResolveCellGroupCenter(oppositeCells);
        string toCurrent = BuildGridPathDiagnostics(startPosition, currentCenter, agentTypeId);
        string toOpposite = BuildGridPathDiagnostics(startPosition, oppositeCenter, agentTypeId);
        string currentToGoal = BuildGridPathDiagnostics(currentCenter, stableGoalPosition, agentTypeId);
        string oppositeToGoal = BuildGridPathDiagnostics(oppositeCenter, stableGoalPosition, agentTypeId);
        bool startInGrid = _world.WorldToGrid(startPosition, out int startX, out int startY);
        bool gridLosCurrent = startInGrid
                              && currentCells.Length > 0
                              && HasGridLineOfSight(_world, startX, startY, currentCells[0].x, currentCells[0].y);
        string losTrace = currentCells.Length > 0 && startInGrid
            ? BuildLineOfSightCellTrace(_world, startX, startY, currentCells[0].x, currentCells[0].y)
            : "trace=unavailable";
        return "{currentCenter=" + currentCenter
               + ",oppositeCenter=" + oppositeCenter
               + ",gridLosFirstCell=" + gridLosCurrent
               + ",losTrace=" + losTrace
               + ",toCurrent=" + toCurrent
               + ",toOpposite=" + toOpposite
               + ",currentToGoal=" + currentToGoal
               + ",oppositeToGoal=" + oppositeToGoal
               + "}";
    }

    private static string BuildFlowLineOfSightDecisionDiagnostics(
        Vector3 startPosition,
        Vector3 stableGoalPosition,
        Vector3 tileTargetPosition,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int agentTypeId,
        FlowTileCacheEntry tile)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(2048);
        builder.Append("[FlowLosDecision goal=")
            .Append(BuildLineDecisionSegmentDiagnostics(startPosition, stableGoalPosition, startX, startY, goalX, goalY, agentTypeId))
            .Append(" tileTarget=");

        if (_world != null && _world.WorldToGrid(tileTargetPosition, out int tileTargetX, out int tileTargetY))
        {
            builder.Append(BuildLineDecisionSegmentDiagnostics(startPosition, tileTargetPosition, startX, startY, tileTargetX, tileTargetY, agentTypeId));
        }
        else
        {
            builder.Append("{targetInGrid=False targetPos=").Append(tileTargetPosition).Append('}');
        }

        if (tile != null && tile.Key.GoalKind == TileGoalKind.Portal)
        {
            PortalData portal = GetPortalById(_world, tile.Key.GoalId);
            Vector2Int[] cells = GetPortalCellsForSector(portal, tile.Key.SectorId);
            builder.Append(" portalCells=[");
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0)
                    builder.Append(" | ");
                Vector2Int cell = cells[i];
                builder.Append('i').Append(i).Append('=')
                    .Append(BuildLineDecisionSegmentDiagnostics(startPosition, _world.GridToWorldCenter(cell.x, cell.y), startX, startY, cell.x, cell.y, agentTypeId));
            }
            builder.Append(']');
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string BuildLineDecisionSegmentDiagnostics(
        Vector3 fromPosition,
        Vector3 toPosition,
        int fromX,
        int fromY,
        int toX,
        int toY,
        int agentTypeId)
    {
        if (_world == null)
            return "{world=null}";

        bool toInBounds = toX >= 0 && toX < _world.Width && toY >= 0 && toY < _world.Height;
        bool strictGridLos = toInBounds && HasGridLineOfSight(_world, fromX, fromY, toX, toY);
        bool pendingGridLos = toInBounds && HasPendingDirectLineOfSight(_world, fromX, fromY, toX, toY);
        bool softGridLos = toInBounds && HasSoftCostTolerantGridLineOfSight(_world, fromX, fromY, toX, toY, maxAllowedCost: 15);
        return "{from=(" + fromX + "," + fromY + ")"
               + ",to=(" + toX + "," + toY + ")"
               + ",toInBounds=" + toInBounds
               + ",strictGridLos=" + strictGridLos
               + ",pendingGridLos=" + pendingGridLos
               + ",softGridLos=" + softGridLos
               + ",trace=" + BuildLineOfSightDecisionCellTrace(_world, fromX, fromY, toX, toY)
               + ",registeredObstacles=" + BuildRegisteredObstacleSegmentDiagnostics(fromPosition, toPosition)
               + ",physics=" + BuildPhysicsSegmentDiagnostics(fromPosition, toPosition, agentTypeId)
               + "}";
    }

    private static string BuildLineOfSightDecisionCellTrace(NavigationWorld world, int x0, int y0, int x1, int y1)
    {
        if (world == null)
            return "world-null";
        if (x0 < 0 || x0 >= world.Width || y0 < 0 || y0 >= world.Height)
            return "start-out";
        if (x1 < 0 || x1 >= world.Width || y1 < 0 || y1 >= world.Height)
            return "target-out";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(768);
        builder.Append("[");
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        int cx = x0;
        int cy = y0;
        int previousX = x0;
        int previousY = y0;
        bool first = true;
        int count = 0;
        const int maxCells = 96;

        while (true)
        {
            if (count > 0)
                builder.Append(" -> ");

            AppendLineDecisionCell(builder, world, cx, cy, previousX, previousY, first);
            count++;

            if (cx == x1 && cy == y1)
                break;
            if (count >= maxCells)
            {
                builder.Append(" -> truncated");
                break;
            }

            previousX = cx;
            previousY = cy;
            first = false;
            int e2 = err * 2;
            if (e2 > -dy)
            {
                err -= dy;
                cx += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                cy += sy;
            }
        }

        builder.Append("]");
        return builder.ToString();
    }

    private static void AppendLineDecisionCell(System.Text.StringBuilder builder, NavigationWorld world, int x, int y, int previousX, int previousY, bool first)
    {
        bool inBounds = x >= 0 && x < world.Width && y >= 0 && y < world.Height;
        builder.Append("(").Append(x).Append(',').Append(y).Append(")");
        if (!inBounds)
        {
            builder.Append("{out}");
            return;
        }

        int index = world.GetIndex(x, y);
        bool walkable = world.WalkableMask != null && world.WalkableMask.Length == world.Width * world.Height && world.WalkableMask[index];
        bool baseWalkable = world.BaseWalkableMask != null && world.BaseWalkableMask.Length == world.Width * world.Height && world.BaseWalkableMask[index];
        int cost = TryGetCostFieldValue(world, x, y, out byte resolvedCost) ? resolvedCost : -1;
        int island = ResolveIslandId(world, x, y);
        bool link = first || CanTraverseNeighborCells(world, previousX, previousY, x, y);
        bool softBoundary = walkable && cost > 1 && IsBoundaryOnlySoftCostCell(world, x, y);
        bool costStamp = walkable && IsCellAffectedByCostStamp(world, x, y);
        builder.Append("{walk=").Append(walkable)
            .Append(",base=").Append(baseWalkable)
            .Append(",cost=").Append(cost)
            .Append(",softBoundary=").Append(softBoundary)
            .Append(",costStamp=").Append(costStamp)
            .Append(",island=").Append(island)
            .Append(",link=").Append(link)
            .Append("}");
    }

    private static string BuildRegisteredObstacleSegmentDiagnostics(Vector3 fromPosition, Vector3 toPosition)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append("[boxes=");
        int count = 0;
        foreach (BoxObstacle box in BoxObstacles.Values)
        {
            Vector3 half = box.HalfExtents;
            if (!TryMeasureSegmentBoxDistance(fromPosition, toPosition, box.Center, half, out float distance, out Vector3 closestPoint))
                continue;
            float inflatedDistance = distance - Mathf.Max(0f, half.x > half.z ? half.x : half.z);
            if (distance > Mathf.Max(_world.CellSize * 4f, 2f))
                continue;

            if (count > 0)
                builder.Append(" | ");
            builder.Append("{id=").Append(box.Id)
                .Append(",center=").Append(box.Center)
                .Append(",half=").Append(box.HalfExtents)
                .Append(",dist=").Append(distance.ToString("F3"))
                .Append(",inflatedDist=").Append(inflatedDistance.ToString("F3"))
                .Append(",closest=").Append(closestPoint)
                .Append(",grid=").Append(FormatBoundsGridRange(new Bounds(box.Center, box.HalfExtents * 2f)))
                .Append("}");
            count++;
            if (count >= 8)
            {
                builder.Append(" | truncated");
                break;
            }
        }

        if (count == 0)
            builder.Append("none");
        builder.Append(" circles=");
        count = 0;
        foreach (CircleObstacle circle in CircleObstacles.Values)
        {
            if (!TryMeasureSegmentCircleDistance(fromPosition, toPosition, circle.Position, circle.Radius, out float distance, out Vector3 closestPoint))
                continue;
            if (distance > Mathf.Max(_world.CellSize * 4f, 2f))
                continue;

            if (count > 0)
                builder.Append(" | ");
            builder.Append("{id=").Append(circle.Id)
                .Append(",center=").Append(circle.Position)
                .Append(",radius=").Append(circle.Radius.ToString("F3"))
                .Append(",dist=").Append(distance.ToString("F3"))
                .Append(",closest=").Append(closestPoint)
                .Append("}");
            count++;
            if (count >= 8)
            {
                builder.Append(" | truncated");
                break;
            }
        }

        if (count == 0)
            builder.Append("none");
        builder.Append(" nearestBoxes=");
        AppendNearestRegisteredBoxes(builder, fromPosition, toPosition);
        builder.Append(']');
        return builder.ToString();
    }

    private static void AppendNearestRegisteredBoxes(System.Text.StringBuilder builder, Vector3 fromPosition, Vector3 toPosition)
    {
        const int maxNearest = 5;
        int[] nearestIds = new int[maxNearest];
        float[] nearestDistances = new float[maxNearest];
        Vector3[] nearestCenters = new Vector3[maxNearest];
        Vector3[] nearestHalfExtents = new Vector3[maxNearest];
        string[] nearestGrids = new string[maxNearest];
        for (int i = 0; i < maxNearest; i++)
        {
            nearestIds[i] = 0;
            nearestDistances[i] = float.PositiveInfinity;
            nearestCenters[i] = Vector3.zero;
            nearestHalfExtents[i] = Vector3.zero;
            nearestGrids[i] = "none";
        }

        foreach (BoxObstacle box in BoxObstacles.Values)
        {
            if (!TryMeasureSegmentBoxDistance(fromPosition, toPosition, box.Center, box.HalfExtents, out float distance, out _))
                continue;

            for (int i = 0; i < maxNearest; i++)
            {
                if (distance >= nearestDistances[i])
                    continue;

                for (int j = maxNearest - 1; j > i; j--)
                {
                    nearestIds[j] = nearestIds[j - 1];
                    nearestDistances[j] = nearestDistances[j - 1];
                    nearestCenters[j] = nearestCenters[j - 1];
                    nearestHalfExtents[j] = nearestHalfExtents[j - 1];
                    nearestGrids[j] = nearestGrids[j - 1];
                }

                nearestIds[i] = box.Id;
                nearestDistances[i] = distance;
                nearestCenters[i] = box.Center;
                nearestHalfExtents[i] = box.HalfExtents;
                nearestGrids[i] = FormatBoundsGridRange(new Bounds(box.Center, box.HalfExtents * 2f))
                    + "/nav=" + FormatBoundsGridRange(ResolveBoxObstacleNavigationBounds(_world, box));
                break;
            }
        }

        if (float.IsPositiveInfinity(nearestDistances[0]))
        {
            builder.Append("none");
            return;
        }

        for (int i = 0; i < maxNearest; i++)
        {
            if (float.IsPositiveInfinity(nearestDistances[i]))
                break;
            if (i > 0)
                builder.Append(" | ");
            builder.Append("{id=").Append(nearestIds[i])
                .Append(",center=").Append(nearestCenters[i])
                .Append(",half=").Append(nearestHalfExtents[i])
                .Append(",dist=").Append(nearestDistances[i].ToString("F3"))
                .Append(",grid=").Append(nearestGrids[i])
                .Append("}");
        }
    }

    private static bool TryMeasureSegmentBoxDistance(Vector3 segmentStart, Vector3 segmentEnd, Vector3 center, Vector3 halfExtents, out float distance, out Vector3 closestPoint)
    {
        Vector3 segment = segmentEnd - segmentStart;
        segment.y = 0f;
        float lengthSq = segment.sqrMagnitude;
        if (lengthSq <= 0.0001f)
        {
            distance = float.PositiveInfinity;
            closestPoint = segmentStart;
            return false;
        }

        Vector3 centerOffset = center - segmentStart;
        centerOffset.y = 0f;
        float t = Mathf.Clamp01(Vector3.Dot(centerOffset, segment) / lengthSq);
        closestPoint = segmentStart + segment * t;
        Vector3 closestOnBox = new Vector3(
            Mathf.Clamp(closestPoint.x, center.x - halfExtents.x, center.x + halfExtents.x),
            closestPoint.y,
            Mathf.Clamp(closestPoint.z, center.z - halfExtents.z, center.z + halfExtents.z));
        Vector3 delta = closestPoint - closestOnBox;
        delta.y = 0f;
        distance = delta.magnitude;
        return true;
    }

    private static bool TryMeasureSegmentCircleDistance(Vector3 segmentStart, Vector3 segmentEnd, Vector3 center, float radius, out float distance, out Vector3 closestPoint)
    {
        Vector3 segment = segmentEnd - segmentStart;
        segment.y = 0f;
        float lengthSq = segment.sqrMagnitude;
        if (lengthSq <= 0.0001f)
        {
            distance = float.PositiveInfinity;
            closestPoint = segmentStart;
            return false;
        }

        Vector3 centerOffset = center - segmentStart;
        centerOffset.y = 0f;
        float t = Mathf.Clamp01(Vector3.Dot(centerOffset, segment) / lengthSq);
        closestPoint = segmentStart + segment * t;
        Vector3 delta = closestPoint - center;
        delta.y = 0f;
        distance = Mathf.Max(0f, delta.magnitude - radius);
        return true;
    }

    private static string BuildPhysicsSegmentDiagnostics(Vector3 fromPosition, Vector3 toPosition, int agentTypeId)
    {
        Vector3 start = fromPosition + Vector3.up * 0.5f;
        Vector3 end = toPosition + Vector3.up * 0.5f;
        Vector3 delta = end - start;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
            return "{empty}";

        Vector3 direction = delta / distance;
        RaycastHit[] hits = Physics.RaycastAll(start, direction, distance, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append("{agentType=").Append(agentTypeId).Append(",hits=");
        int count = 0;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider collider = hits[i].collider;
            if (collider == null)
                continue;
            if (count > 0)
                builder.Append(" | ");
            builder.Append("{name=").Append(collider.name)
                .Append(",layer=").Append(LayerMask.LayerToName(collider.gameObject.layer))
                .Append(",dist=").Append(hits[i].distance.ToString("F3"))
                .Append(",point=").Append(hits[i].point)
                .Append(",normal=").Append(hits[i].normal)
                .Append("}");
            count++;
            if (count >= 8)
            {
                builder.Append(" | truncated");
                break;
            }
        }

        if (count == 0)
            builder.Append("none");
        builder.Append('}');
        return builder.ToString();
    }

    private static Vector3 ResolveCellGroupCenter(Vector2Int[] cells)
    {
        if (cells == null || cells.Length == 0)
            return Vector3.zero;

        Vector3 center = Vector3.zero;
        for (int i = 0; i < cells.Length; i++)
            center += _world.GridToWorldCenter(cells[i].x, cells[i].y);
        return center / cells.Length;
    }

    private static string FormatPortal(PortalData portal)
    {
        if (portal == null)
            return "null";

        return $"(id={portal.PortalId},sectorA={portal.SectorAId},sectorB={portal.SectorBId},vertical={portal.IsVerticalBoundary},width={portal.WidthCells},narrow={portal.IsNarrow},cellsA={FormatGoalCells(portal.CellsA)},cellsB={FormatGoalCells(portal.CellsB)})";
    }

    private static string FormatIntegrationCosts(FlowTileCacheEntry tile, Vector2Int[] cells)
    {
        if (tile == null)
            return "tile=null";
        if (cells == null || cells.Length == 0)
            return "[]";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(cells.Length * 24);
        builder.Append('[');
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            Vector2Int cell = cells[i];
            builder.Append('(');
            builder.Append(cell.x);
            builder.Append(',');
            builder.Append(cell.y);
            builder.Append(")=");

            if (!IsInsideSector(tile, cell.x, cell.y))
            {
                builder.Append("outside");
                continue;
            }

            float cost = GetTileIntegrationCostForDiagnostics(tile, cell.x, cell.y);
            builder.Append(float.IsPositiveInfinity(cost) ? "INF" : cost.ToString("F3"));
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string FormatSectorPortalAccessCosts(SectorData sector, int sectorId, int portalId, Vector2Int[] cells)
    {
        if (sector == null)
            return "sector=null";
        if (portalId < 0)
            return "portal=none";
        if (cells == null || cells.Length == 0)
            return "[]";

        SectorPortalAccessEntry entry = GetPrebuiltSectorPortalAccess(sector, sectorId, portalId);
        System.Text.StringBuilder builder = new System.Text.StringBuilder(cells.Length * 24);
        builder.Append('[');
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            Vector2Int cell = cells[i];
            builder.Append('(');
            builder.Append(cell.x);
            builder.Append(',');
            builder.Append(cell.y);
            builder.Append(")=");

            if (!IsInsideSector(sector, cell.x, cell.y))
            {
                builder.Append("outside");
                continue;
            }

            float cost = ResolvePortalAccessIntegrationCost(_world, sector, entry, cell.x, cell.y);
            builder.Append(float.IsPositiveInfinity(cost) ? "INF" : cost.ToString("F3"));
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string DescribeTileIntegrationSummary(FlowTileCacheEntry tile)
    {
        if (tile == null)
            return "tile=null";
        if (!HasDeterministicIntegrationPayload(tile))
            return "deterministic-cost=null";
        int reachableCount = 0;
        int cellCount = checked(tile.Width * tile.Height);
        for (int i = 0; i < cellCount; i++)
        {
            if (GetDeterministicIntegrationCost(tile, i) != int.MaxValue)
                reachableCount++;
        }
        return $"deterministicReachable={reachableCount} cells={cellCount} debugPayload={tile.DebugIntegrationPayload?.Values != null}";
    }

    private static int CountReachableFlowCells(FlowTileCacheEntry tile)
    {
        if (tile?.FlowFieldValues == null)
            return 0;

        int count = 0;
        for (int i = 0; i < tile.FlowFieldValues.Length; i++)
        {
            if (IsFlowReachable(tile, i))
                count++;
        }

        return count;
    }

    private static bool TryResolveGridEdgeData(Vector3 position, Vector3 desiredDirection, out Vector3 edgeNormal, out float edgeDistance)
    {
        if (_world == null)
            throw new InvalidOperationException("TryResolveGridEdgeData failed: world is null.");

        return TryResolveGridEdgeData(position, desiredDirection, _world.CellSize * 1.5f, out edgeNormal, out edgeDistance);
    }

    private static bool TryResolveGridEdgeData(Vector3 position, Vector3 desiredDirection, float probeDistance, out Vector3 edgeNormal, out float edgeDistance)
    {
        edgeNormal = Vector3.zero;
        edgeDistance = float.MaxValue;

        if (_world == null)
            throw new InvalidOperationException("TryResolveGridEdgeData failed: world is null.");

        if (!_world.WorldToGrid(position, out int cellX, out int cellY))
            return false;

        Vector3 accumulatedNormal = Vector3.zero;
        probeDistance = Mathf.Max(_world.CellSize * 1.5f, probeDistance);
        int radius = Mathf.CeilToInt(probeDistance / Mathf.Max(_world.CellSize, 0.001f));
        for (int y = cellY - radius; y <= cellY + radius; y++)
        {
            for (int x = cellX - radius; x <= cellX + radius; x++)
            {
                if (_world.IsWalkable(x, y))
                    continue;

                float minX = _world.Origin.x + x * _world.CellSize;
                float maxX = _world.Origin.x + (x + 1) * _world.CellSize;
                float minZ = _world.Origin.z + y * _world.CellSize;
                float maxZ = _world.Origin.z + (y + 1) * _world.CellSize;
                float nearestX = Mathf.Clamp(position.x, minX, maxX);
                float nearestZ = Mathf.Clamp(position.z, minZ, maxZ);
                float dx = position.x - nearestX;
                float dz = position.z - nearestZ;
                float distanceSq = dx * dx + dz * dz;
                if (distanceSq > probeDistance * probeDistance)
                    continue;

                Vector2 normal2D = new Vector2(dx, dz);
                if (normal2D.sqrMagnitude <= 0.000001f)
                    normal2D = ResolveNavigationRectangleBoundaryNormal(position, minX, maxX, minZ, maxZ);
                if (normal2D.sqrMagnitude <= 0.000001f)
                    continue;

                normal2D.Normalize();
                Vector3 inwardNormal = new Vector3(normal2D.x, 0f, normal2D.y);
                float distance = Mathf.Sqrt(distanceSq);
                TryAccumulateGridEdge(distance, inwardNormal, desiredDirection, ref accumulatedNormal, ref edgeDistance);
            }
        }

        if (edgeDistance == float.MaxValue || accumulatedNormal.sqrMagnitude <= 0.0001f)
            return false;

        edgeNormal = accumulatedNormal.normalized;
        return true;
    }

    private static void TryAccumulateGridEdge(
        float distanceToBoundary,
        Vector3 inwardNormal,
        Vector3 desiredDirection,
        ref Vector3 accumulatedNormal,
        ref float edgeDistance)
    {
        float headingPressure = desiredDirection.sqrMagnitude > 0.0001f
            ? Mathf.Max(0f, -Vector3.Dot(desiredDirection, inwardNormal))
            : 0f;
        if (headingPressure <= 0.05f && distanceToBoundary > _world.CellSize * 0.18f)
            return;

        float clampedDistance = Mathf.Max(0.001f, distanceToBoundary);
        float weight = (1f / clampedDistance) * Mathf.Max(0.35f, headingPressure);
        accumulatedNormal += inwardNormal * weight;
        edgeDistance = Mathf.Min(edgeDistance, distanceToBoundary);
    }

    private static string FormatGridSampleDiagnostics(Vector3 position)
    {
        if (_world == null)
            return "world-null";

        bool inGrid = _world.WorldToGrid(position, out int x, out int y);
        bool walkable = inGrid && _world.IsWalkable(x, y);
        int island = inGrid ? ResolveIslandIdForDiagnostics(_world, x, y) : -1;
        return $"grid={(inGrid ? $"({x},{y})" : "out")} walkable={walkable} island={island}";
    }

    private static string BuildGoalResolutionFailure(IEntityContext self, Vector3 goalPosition)
    {
        if (_world == null)
            return $"goal unresolved goal={goalPosition} diag=world-null";

        bool goalInGrid = _world.WorldToGrid(goalPosition, out int goalX, out int goalY);
        bool goalWalkable = goalInGrid && _world.IsWalkable(goalX, goalY);
        bool goalBaseWalkable = false;
        bool nearestWalkableFound = false;
        int nearestWalkableX = 0;
        int nearestWalkableY = 0;
        Vector3 nearestWalkableWorld = Vector3.zero;
        float nearestWalkableDistance = 0f;
        if (goalInGrid)
        {
            int index = _world.GetIndex(goalX, goalY);
            goalBaseWalkable = _world.BaseWalkableMask[index];
            nearestWalkableFound = TryFindNearestWalkable(_world, goalX, goalY, 6, out nearestWalkableX, out nearestWalkableY);
            if (nearestWalkableFound)
            {
                nearestWalkableWorld = _world.GridToWorldCenter(nearestWalkableX, nearestWalkableY);
                nearestWalkableDistance = Vector3.Distance(
                    new Vector3(goalPosition.x, 0f, goalPosition.z),
                    new Vector3(nearestWalkableWorld.x, 0f, nearestWalkableWorld.z));
            }
        }

        int selfAgentType = self is ILogicFrameEntity logicEntity ? logicEntity.NavigationAgentTypeId : 0;
        Vector3 selfPosition = self != null ? self.Position : Vector3.zero;
        bool selfInGrid = _world.WorldToGrid(selfPosition, out int selfX, out int selfY);
        bool selfWalkable = selfInGrid && _world.IsWalkable(selfX, selfY);

        bool startResolved = TryResolveStartCellForReachabilityFixed(
            self,
            out int startX,
            out int startY,
            out int startIsland);
        int startIslandDirect = startResolved ? ResolveIslandId(_world, startX, startY) : -1;
        int startSectorUniformIsland = startResolved
                                       && TryGetSectorForCell(_world, startX, startY, out SectorData startSector)
            ? startSector.UniformIslandId
            : -1;
        int fullIslandX = 0;
        int fullIslandY = 0;
        Fix64 fullIslandDistance = Fix64.Zero;
        bool fullIslandFound = startResolved
                               && TryFindNearestWalkableInIslandByWorldDistanceFixed(
                                   _world,
                                   goalX,
                                   goalY,
                                   new FixVector2((Fix64)goalPosition.x, (Fix64)goalPosition.z),
                                   startIsland,
                                   radius: 8,
                                   allowFullIslandSearch: true,
                                   out fullIslandX,
                                   out fullIslandY,
                                   out fullIslandDistance);

        string currentTarget = self?.TargetComp?.CurrentTarget?.CharacterKey ?? "null";
        Vector3 currentTargetPos = self?.TargetComp?.CurrentTarget?.Position ?? Vector3.zero;

        return $"goal unresolved goal={goalPosition} " +
               $"diag=worldAgentType={_world.AgentTypeId} selfAgentType={selfAgentType} " +
               $"worldOrigin={_world.Origin} worldSize={_world.Width}x{_world.Height} cellSize={_world.CellSize:F3} " +
               $"goalCell={(goalInGrid ? $"({goalX},{goalY})" : "out")} goalWalkable={goalWalkable} baseWalkable={goalBaseWalkable} " +
               $"nearestWalkableFound={nearestWalkableFound} nearestWalkableCell={(nearestWalkableFound ? $"({nearestWalkableX},{nearestWalkableY})" : "none")} " +
               $"nearestWalkableWorld={(nearestWalkableFound ? nearestWalkableWorld.ToString() : "none")} nearestWalkableDist={nearestWalkableDistance:F3} " +
               $"goalGridSample={FormatGridSampleDiagnostics(goalPosition)} " +
               $"selfPos={selfPosition} selfCell={(selfInGrid ? $"({selfX},{selfY})" : "out")} selfWalkable={selfWalkable} " +
               $"startResolved={startResolved} startCell={(startResolved ? $"({startX},{startY})" : "none")} " +
               $"startIsland={startIsland} startIslandDirect={startIslandDirect} startSectorUniformIsland={startSectorUniformIsland} " +
               $"islandIdsLength={_world.IslandIds?.Length ?? -1} expectedIslandIdsLength={_world.Width * _world.Height} " +
               $"fullIslandFound={fullIslandFound} fullIslandCell={(fullIslandFound ? $"({fullIslandX},{fullIslandY})" : "none")} " +
               $"fullIslandDistanceRaw={(fullIslandFound ? fullIslandDistance.RawValue : -1L)} " +
               $"runtimeDirtyPending={HasPendingRuntimeDirty(_activeWorldState)} runtimeDirtyReason={_lastRuntimeObstacleDirtyReason} " +
               $"obstacles={BuildNearbyObstacleDiagnostics(goalPosition, goalPosition, _world.AgentTypeId)} " +
               $"target={currentTarget} targetPos={currentTargetPos}";
    }

}
