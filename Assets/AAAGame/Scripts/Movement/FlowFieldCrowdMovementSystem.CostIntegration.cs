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
    private static void InitializeIntegrationField(float[] integration)
    {
        if (integration == null)
            throw new InvalidOperationException("InitializeIntegrationField failed: integration is null.");

        for (int i = 0; i < integration.Length; i++)
            integration[i] = float.PositiveInfinity;
    }

    private static bool IsSignificantIntegrationImprovement(float newCost, float existingCost)
    {
        if (float.IsPositiveInfinity(newCost))
            return false;
        if (float.IsPositiveInfinity(existingCost))
            return true;

        return newCost < existingCost - IntegrationSignificantImprovement;
    }

    private static float ResolveEikonalIntegrationCost(
        NavigationWorld world,
        SectorData sector,
        float[] integration,
        int worldX,
        int worldY,
        bool reverseTraversal)
    {
        float horizontal = float.PositiveInfinity;
        float vertical = float.PositiveInfinity;
        for (int i = 0; i < CardinalOffsetX.Length; i++)
        {
            int neighborX = worldX + CardinalOffsetX[i];
            int neighborY = worldY + CardinalOffsetY[i];
            if (!IsInsideSector(sector, neighborX, neighborY) || !world.IsWalkable(neighborX, neighborY))
                continue;
            if (!CanUseNeighborForIntegration(world, worldX, worldY, neighborX, neighborY, reverseTraversal))
                continue;

            float neighborCost = integration[GetSectorLocalIndex(sector, neighborX, neighborY)];
            if (float.IsPositiveInfinity(neighborCost))
                continue;

            if (neighborX != worldX)
                horizontal = Mathf.Min(horizontal, neighborCost);
            else
                vertical = Mathf.Min(vertical, neighborCost);
        }

        float cellCost = ResolveCellIntegrationCost(world, worldX, worldY);
        if (float.IsPositiveInfinity(cellCost))
            return float.PositiveInfinity;

        if (float.IsPositiveInfinity(horizontal))
            return float.IsPositiveInfinity(vertical) ? float.PositiveInfinity : vertical + cellCost;
        if (float.IsPositiveInfinity(vertical))
            return horizontal + cellCost;

        float a = Mathf.Min(horizontal, vertical);
        float b = Mathf.Max(horizontal, vertical);
        if (b - a >= cellCost)
            return a + cellCost;

        float discriminant = Mathf.Max(0f, 2f * cellCost * cellCost - (b - a) * (b - a));
        return (a + b + Mathf.Sqrt(discriminant)) * 0.5f;
    }

    private static bool CanUseNeighborForIntegration(
        NavigationWorld world,
        int worldX,
        int worldY,
        int neighborX,
        int neighborY,
        bool reverseTraversal)
    {
        return CanTraverseNeighborCells(
            world,
            reverseTraversal ? worldX : neighborX,
            reverseTraversal ? worldY : neighborY,
            reverseTraversal ? neighborX : worldX,
            reverseTraversal ? neighborY : worldY);
    }

    private static float ResolveCellIntegrationCost(NavigationWorld world, int worldX, int worldY)
    {
        int cost = GetCostFieldValueStrict(world, worldX, worldY);
        if (cost >= 255)
            return float.PositiveInfinity;

        if (cost <= 1)
            return 1f;

        return Mathf.Max(1f, cost);
    }

    private static int GetCostFieldValueStrict(
        NavigationWorld world,
        int worldX,
        int worldY,
        [CallerMemberName] string caller = null)
    {
        if (!TryGetCostFieldValue(world, worldX, worldY, out byte cost))
        {
            throw new InvalidOperationException(
                $"GetCostFieldValueStrict failed: invalid cost field read caller={caller} cell=({worldX},{worldY}) " +
                $"world={(world != null ? world.Width + "x" + world.Height : "null")} walkable={world?.WalkableMask?.Length ?? -1} " +
                $"cost={world?.CostField?.Length ?? -1} sectorCosts={world?.SectorCostFields?.Length ?? -1}.");
        }

        return cost;
    }

    private static bool TryGetCostFieldValue(NavigationWorld world, int worldX, int worldY, out byte cost)
    {
        cost = 0;
        if (world == null)
            return false;
        if (worldX < 0 || worldX >= world.Width || worldY < 0 || worldY >= world.Height)
            return false;
        if (world.WalkableMask == null || world.WalkableMask.Length != world.Width * world.Height)
            return false;

        int index = world.GetIndex(worldX, worldY);
        if (TryGetSectorForCell(world, worldX, worldY, out SectorData sector) && sector.IsClearCostField)
        {
            cost = world.WalkableMask[index] ? (byte)1 : byte.MaxValue;
            return true;
        }

        if (sector != null && world.SectorCostFields != null)
        {
            if (sector.SectorId < 0 || sector.SectorId >= world.SectorCostFields.Length)
                return false;

            byte[] sectorCost = world.SectorCostFields[sector.SectorId];
            if (sectorCost == null || sectorCost.Length != sector.Width * sector.Height)
                return false;

            cost = sectorCost[GetSectorLocalIndex(sector, worldX, worldY)];
            return true;
        }

        if (world.CostField == null || world.CostField.Length != world.Width * world.Height)
            return false;

        cost = world.CostField[index];
        return true;
    }

    private static void FinalizeWorldCostStorage(NavigationWorld world)
    {
        if (world == null)
            throw new InvalidOperationException("FinalizeWorldCostStorage failed: world is null.");
        if (world.CostField == null || world.CostField.Length != world.Width * world.Height)
            throw new InvalidOperationException("FinalizeWorldCostStorage failed: mutable cost field is missing.");
        if (world.Sectors == null || world.Sectors.Length != world.SectorCountX * world.SectorCountY)
            throw new InvalidOperationException("FinalizeWorldCostStorage failed: sectors are missing.");

        byte[][] sectorCostFields = new byte[world.Sectors.Length][];
        for (int i = 0; i < world.Sectors.Length; i++)
        {
            SectorData sector = world.Sectors[i];
            if (sector.IsClearCostField)
                continue;

            byte[] chunk = new byte[sector.Width * sector.Height];
            for (int y = 0; y < sector.Height; y++)
            {
                for (int x = 0; x < sector.Width; x++)
                {
                    int worldX = sector.StartX + x;
                    int worldY = sector.StartY + y;
                    chunk[x + y * sector.Width] = world.CostField[world.GetIndex(worldX, worldY)];
                }
            }

            sectorCostFields[sector.SectorId] = chunk;
        }

        world.SectorCostFields = sectorCostFields;
        ReturnByteArray(world.CostField);
        world.CostField = null;
    }

    private static byte[] MaterializeMutableCostField(NavigationWorld world)
    {
        if (world == null)
            throw new InvalidOperationException("MaterializeMutableCostField failed: world is null.");
        int cellCount = checked(world.Width * world.Height);
        if (world.WalkableMask == null || world.WalkableMask.Length != cellCount)
            throw new InvalidOperationException("MaterializeMutableCostField failed: walkable mask is missing.");
        if (world.Sectors == null || world.Sectors.Length != world.SectorCountX * world.SectorCountY)
            throw new InvalidOperationException("MaterializeMutableCostField failed: sectors are missing.");
        if (world.CostField != null)
        {
            if (world.CostField.Length != cellCount)
                throw new InvalidOperationException($"MaterializeMutableCostField failed: invalid full cost field length={world.CostField.Length} expected={cellCount}.");
            if (world.SectorCostFields != null)
                throw new InvalidOperationException("MaterializeMutableCostField failed: full and sector cost fields are both present.");

            return RentCopiedByteArray(world.CostField);
        }

        byte[] costField = RentByteArray(cellCount, clear: false);
        for (int i = 0; i < costField.Length; i++)
            costField[i] = world.WalkableMask[i] ? (byte)1 : byte.MaxValue;

        for (int i = 0; i < world.Sectors.Length; i++)
        {
            SectorData sector = world.Sectors[i];
            byte[] chunk = world.SectorCostFields != null && sector.SectorId >= 0 && sector.SectorId < world.SectorCostFields.Length
                ? world.SectorCostFields[sector.SectorId]
                : null;
            if (chunk == null)
            {
                if (!sector.IsClearCostField)
                    throw new InvalidOperationException($"MaterializeMutableCostField failed: missing non-clear cost chunk sector={sector.SectorId}.");
                continue;
            }
            if (chunk.Length != sector.Width * sector.Height)
                throw new InvalidOperationException($"MaterializeMutableCostField failed: invalid cost chunk sector={sector.SectorId} length={chunk.Length}.");

            for (int y = 0; y < sector.Height; y++)
            {
                for (int x = 0; x < sector.Width; x++)
                {
                    int worldX = sector.StartX + x;
                    int worldY = sector.StartY + y;
                    costField[world.GetIndex(worldX, worldY)] = chunk[x + y * sector.Width];
                }
            }
        }

        return costField;
    }

    private static float ResolveMinimumIntegrationCost(SectorData sector, float[] integration, Vector2Int[] targetCells)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < targetCells.Length; i++)
        {
            Vector2Int cell = targetCells[i];
            if (!IsInsideSector(sector, cell.x, cell.y))
                continue;

            float cost = integration[GetSectorLocalIndex(sector, cell.x, cell.y)];
            if (cost < best)
                best = cost;
        }

        return best;
    }

    private static float ResolvePortalAccessCost(SectorData sector, int sectorId, int portalId, int worldX, int worldY)
    {
        return DequantizeDeterministicPortalCost(
            ResolveDeterministicPortalAccessCost(sector, sectorId, portalId, worldX, worldY));
    }

    private static long ResolveDeterministicPortalAccessCost(SectorData sector, int sectorId, int portalId, int worldX, int worldY)
    {
        return ResolveDeterministicPortalAccessCost(
            _world,
            sector,
            sectorId,
            portalId,
            worldX,
            worldY);
    }

    private static long ResolveDeterministicPortalAccessCost(
        NavigationWorld world,
        SectorData sector,
        int sectorId,
        int portalId,
        int worldX,
        int worldY)
    {
        if (world == null)
            throw new InvalidOperationException("ResolveDeterministicPortalAccessCost failed: world is null.");
        if (!IsInsideSector(sector, worldX, worldY))
            return long.MaxValue;

        SectorPortalAccessEntry entry = GetPrebuiltSectorPortalAccess(
            world,
            sector,
            sectorId,
            portalId);
        return ResolveDeterministicPortalAccessIntegrationCost(
            world,
            sector,
            entry,
            worldX,
            worldY);
    }

    private static float ResolveGoalSectorPortalAccessCost(SectorData goalSector, int goalSectorId, int portalId, int goalX, int goalY)
    {
        if (_world == null)
            throw new InvalidOperationException("ResolveGoalSectorPortalAccessCost failed: world is null.");
        if (goalSector == null)
            throw new InvalidOperationException("ResolveGoalSectorPortalAccessCost failed: goal sector is null.");
        if (!IsInsideSector(goalSector, goalX, goalY))
            return float.PositiveInfinity;
        if (!TryGetPortalById(_world, portalId, out PortalData portal))
            throw new InvalidOperationException($"ResolveGoalSectorPortalAccessCost failed: portal missing sector={goalSectorId} portal={portalId}.");
        if (!PortalTouchesSector(portal, goalSectorId))
            return float.PositiveInfinity;

        return ResolvePortalAccessCost(goalSector, goalSectorId, portalId, goalX, goalY);
    }

    private static long ResolveDeterministicGoalSectorPortalAccessCost(SectorData goalSector, int goalSectorId, int portalId, int goalX, int goalY)
    {
        if (_world == null)
            throw new InvalidOperationException("ResolveDeterministicGoalSectorPortalAccessCost failed: world is null.");
        if (goalSector == null)
            throw new InvalidOperationException("ResolveDeterministicGoalSectorPortalAccessCost failed: goal sector is null.");
        if (!IsInsideSector(goalSector, goalX, goalY))
            return long.MaxValue;
        if (!TryGetPortalById(_world, portalId, out PortalData portal))
            throw new InvalidOperationException($"ResolveDeterministicGoalSectorPortalAccessCost failed: portal missing sector={goalSectorId} portal={portalId}.");
        if (!PortalTouchesSector(portal, goalSectorId))
            return long.MaxValue;

        return ResolveDeterministicPortalAccessCost(goalSector, goalSectorId, portalId, goalX, goalY);
    }

    private static float ResolvePortalAccessIntegrationCost(NavigationWorld world, SectorData sector, SectorPortalAccessEntry entry, int worldX, int worldY)
    {
        return DequantizeDeterministicPortalCost(
            ResolveDeterministicPortalAccessIntegrationCost(world, sector, entry, worldX, worldY));
    }

    private static long ResolveDeterministicPortalAccessIntegrationCost(NavigationWorld world, SectorData sector, SectorPortalAccessEntry entry, int worldX, int worldY)
    {
        if (entry == null)
            throw new InvalidOperationException("ResolveDeterministicPortalAccessIntegrationCost failed: entry is null.");
        if (sector == null)
            throw new InvalidOperationException("ResolveDeterministicPortalAccessIntegrationCost failed: sector is null.");
        if (!IsInsideSector(sector, worldX, worldY))
            return long.MaxValue;

        if (entry.IsAnalyticClearSector)
            return ResolveDeterministicAnalyticPortalAccessCost(world, sector, entry.PortalId, worldX, worldY);

        if (entry.DeterministicIntegration == null)
            throw new InvalidOperationException($"ResolveDeterministicPortalAccessIntegrationCost failed: deterministic integration is null sector={entry.SectorId} portal={entry.PortalId}.");
        int localIndex = GetSectorLocalIndex(sector, worldX, worldY);
        if (localIndex < 0 || localIndex >= entry.DeterministicIntegration.Length)
            throw new InvalidOperationException($"ResolveDeterministicPortalAccessIntegrationCost failed: local index out of range index={localIndex} length={entry.DeterministicIntegration.Length}.");
        return entry.DeterministicIntegration[localIndex];
    }

    private static long ResolveDeterministicPortalCenterSeedCost(PortalData portal, Vector2Int cell)
    {
        if (portal == null)
            throw new InvalidOperationException("ResolveDeterministicPortalCenterSeedCost failed: portal is null.");

        Vector2Int[] cells = portal.CellsA;
        if (cells == null || cells.Length == 0)
            throw new InvalidOperationException($"ResolveDeterministicPortalCenterSeedCost failed: portal has no cells portal={portal.PortalId}.");

        int firstAxis = portal.IsVerticalBoundary ? cells[0].y : cells[0].x;
        int lastAxis = portal.IsVerticalBoundary ? cells[cells.Length - 1].y : cells[cells.Length - 1].x;
        int cellAxis = portal.IsVerticalBoundary ? cell.y : cell.x;
        long doubledDistance = Math.Abs(checked((long)cellAxis * 2L - firstAxis - lastAxis));
        return checked(doubledDistance * (DeterministicPortalCostScale / 2L));
    }

    private static long[] BuildDeterministicPortalAccessIntegration(
        NavigationWorld world,
        SectorData sector,
        PortalData portal)
    {
        if (world == null)
            throw new InvalidOperationException("BuildDeterministicPortalAccessIntegration failed: world is null.");
        if (sector == null)
            throw new InvalidOperationException("BuildDeterministicPortalAccessIntegration failed: sector is null.");
        if (portal == null)
            throw new InvalidOperationException("BuildDeterministicPortalAccessIntegration failed: portal is null.");

        long[] integration = RentPortalAccessIntegrationArray(sector.Width * sector.Height);
        for (int i = 0; i < integration.Length; i++)
            integration[i] = long.MaxValue;

        DeterministicCostHeap openSet = PortalAccessIntegrationOpenSet;
        openSet.Clear();
        Vector2Int[] portalCells = GetPortalCellsForSector(portal, sector.SectorId);
        for (int i = 0; i < portalCells.Length; i++)
        {
            Vector2Int cell = portalCells[i];
            if (!IsInsideSector(sector, cell.x, cell.y) || !world.IsWalkable(cell.x, cell.y))
                continue;

            int localIndex = GetSectorLocalIndex(sector, cell.x, cell.y);
            long seedCost = ResolveDeterministicPortalCenterSeedCost(portal, cell);
            if (seedCost >= integration[localIndex])
                continue;

            integration[localIndex] = seedCost;
            openSet.Push(localIndex, seedCost);
        }

        if (openSet.Count == 0)
            throw new InvalidOperationException($"BuildDeterministicPortalAccessIntegration failed: portal has no walkable seed sector={sector.SectorId} portal={portal.PortalId}.");

        while (openSet.Count > 0)
        {
            DeterministicCostQueueNode node = openSet.Pop();
            if (node.Cost != integration[node.Index])
                continue;

            int worldX = sector.StartX + node.Index % sector.Width;
            int worldY = sector.StartY + node.Index / sector.Width;
            for (int i = 0; i < CardinalOffsetX.Length; i++)
            {
                int nextX = worldX + CardinalOffsetX[i];
                int nextY = worldY + CardinalOffsetY[i];
                if (!IsInsideSector(sector, nextX, nextY) || !world.IsWalkable(nextX, nextY))
                    continue;
                if (!CanTraverseNeighborCells(world, nextX, nextY, worldX, worldY))
                    continue;

                long newCost = ResolveDeterministicPortalAccessEikonalIntegrationCost(
                    world,
                    sector,
                    integration,
                    nextX,
                    nextY);
                int nextLocalIndex = GetSectorLocalIndex(sector, nextX, nextY);
                if (newCost >= integration[nextLocalIndex])
                    continue;

                integration[nextLocalIndex] = newCost;
                openSet.Push(nextLocalIndex, newCost);
            }
        }

        return integration;
    }

    private static int[] BuildDeterministicPortalTargetSlotIndices(
        NavigationWorld world,
        SectorData sector,
        PortalData portal,
        long[] integration)
    {
        if (world == null)
            throw new InvalidOperationException("BuildDeterministicPortalTargetSlotIndices failed: world is null.");
        if (sector == null)
            throw new InvalidOperationException("BuildDeterministicPortalTargetSlotIndices failed: sector is null.");
        if (portal == null)
            throw new InvalidOperationException("BuildDeterministicPortalTargetSlotIndices failed: portal is null.");
        if (integration != null && integration.Length != sector.Width * sector.Height)
            throw new InvalidOperationException("BuildDeterministicPortalTargetSlotIndices failed: integration size mismatch.");

        Vector2Int[] portalCells = GetPortalCellsForSector(portal, sector.SectorId);
        if (portalCells == null || portalCells.Length == 0)
            throw new InvalidOperationException($"BuildDeterministicPortalTargetSlotIndices failed: portal has no cells sector={sector.SectorId} portal={portal.PortalId}.");

        int count = sector.Width * sector.Height;
        if (integration == null)
        {
            int[] analyticSlots = new int[count];
            for (int localIndex = 0; localIndex < count; localIndex++)
            {
                int worldX = sector.StartX + localIndex % sector.Width;
                int worldY = sector.StartY + localIndex / sector.Width;
                analyticSlots[localIndex] = ResolveDeterministicAnalyticPortalTargetSlotIndex(
                    world,
                    sector,
                    portal,
                    portalCells,
                    worldX,
                    worldY);
            }
            return analyticSlots;
        }

        long[] costs = integration;
        int[] orderedIndices = new int[count];
        long[] orderedCosts = new long[count];
        for (int i = 0; i < count; i++)
        {
            orderedIndices[i] = i;
            orderedCosts[i] = costs[i];
        }
        Array.Sort(orderedCosts, orderedIndices);

        int[] slots = new int[count];
        for (int orderIndex = 0; orderIndex < count; orderIndex++)
        {
            int localIndex = orderedIndices[orderIndex];
            long currentCost = orderedCosts[orderIndex];
            if (currentCost == long.MaxValue)
            {
                slots[localIndex] = -1;
                continue;
            }

            int worldX = sector.StartX + localIndex % sector.Width;
            int worldY = sector.StartY + localIndex / sector.Width;
            int portalSlot = FindPortalCellIndex(portalCells, worldX, worldY);
            if (portalSlot >= 0)
            {
                slots[localIndex] = portalSlot + 1;
                continue;
            }

            if (!TryResolveLowestPortalAccessNeighborForBuild(
                    world,
                    sector,
                    costs,
                    worldX,
                    worldY,
                    currentCost,
                    out int directionIndex,
                    out _))
            {
                throw new InvalidOperationException(
                    $"BuildDeterministicPortalTargetSlotIndices failed: reachable cell has no descending neighbor cell=({worldX},{worldY}) sector={sector.SectorId} portal={portal.PortalId} cost={currentCost}.");
            }

            int nextX = worldX + NeighborOffsetX[directionIndex];
            int nextY = worldY + NeighborOffsetY[directionIndex];
            int nextIndex = GetSectorLocalIndex(sector, nextX, nextY);
            if (costs[nextIndex] >= currentCost || slots[nextIndex] <= 0)
            {
                throw new InvalidOperationException(
                    $"BuildDeterministicPortalTargetSlotIndices failed: descending neighbor has no resolved slot cell=({worldX},{worldY}) next=({nextX},{nextY}) sector={sector.SectorId} portal={portal.PortalId}.");
            }
            slots[localIndex] = slots[nextIndex];
        }

        return slots;
    }

    private static int FindPortalCellIndex(Vector2Int[] portalCells, int worldX, int worldY)
    {
        for (int i = 0; i < portalCells.Length; i++)
        {
            if (portalCells[i].x == worldX && portalCells[i].y == worldY)
                return i;
        }

        return -1;
    }

    private static int ResolveDeterministicAnalyticPortalTargetSlotIndex(
        NavigationWorld world,
        SectorData sector,
        PortalData portal,
        Vector2Int[] portalCells,
        int worldX,
        int worldY)
    {
        if (!IsInsideSector(sector, worldX, worldY) || !world.IsWalkable(worldX, worldY))
            return -1;

        long bestCost = long.MaxValue;
        int bestSlot = -1;
        for (int i = 0; i < portalCells.Length; i++)
        {
            Vector2Int portalCell = portalCells[i];
            if (!IsInsideSector(sector, portalCell.x, portalCell.y) || !world.IsWalkable(portalCell.x, portalCell.y))
                continue;

            int dx = Math.Abs(portalCell.x - worldX);
            int dy = Math.Abs(portalCell.y - worldY);
            int diagonalSteps = Math.Min(dx, dy);
            int cardinalSteps = Math.Max(dx, dy) - diagonalSteps;
            long distanceCost = checked((long)diagonalSteps * 5793L + (long)cardinalSteps * DeterministicPortalCostScale);
            long cost = AddDeterministicPortalCosts(
                distanceCost,
                ResolveDeterministicPortalCenterSeedCost(portal, portalCell));
            if (cost >= bestCost)
                continue;
            bestCost = cost;
            bestSlot = i;
        }

        if (bestSlot < 0)
            return -1;
        return bestSlot + 1;
    }

    private static bool TryResolveLowestPortalAccessNeighborForBuild(
        NavigationWorld world,
        SectorData sector,
        long[] costs,
        int worldX,
        int worldY,
        long currentCost,
        out int bestDirectionIndex,
        out long bestCost)
    {
        bestDirectionIndex = -1;
        bestCost = currentCost;
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int nextX = worldX + NeighborOffsetX[i];
            int nextY = worldY + NeighborOffsetY[i];
            if (!IsInsideSector(sector, nextX, nextY)
                || !CanTraverseNeighborCells(world, worldX, worldY, nextX, nextY)
                || (NeighborOffsetX[i] != 0
                    && NeighborOffsetY[i] != 0
                    && !IsDiagonalPassable(world, worldX, worldY, nextX, nextY)))
            {
                continue;
            }

            long nextCost = costs[GetSectorLocalIndex(sector, nextX, nextY)];
            if (nextCost >= bestCost)
                continue;
            bestCost = nextCost;
            bestDirectionIndex = i;
        }

        return bestDirectionIndex >= 0;
    }

    private static bool IsInsideSector(SectorData sector, int worldX, int worldY)
    {
        return worldX >= sector.StartX
               && worldX < sector.StartX + sector.Width
               && worldY >= sector.StartY
               && worldY < sector.StartY + sector.Height;
    }

    private static int GetSectorLocalIndex(SectorData sector, int worldX, int worldY)
    {
        return (worldX - sector.StartX) + (worldY - sector.StartY) * sector.Width;
    }

    private static bool IsCellReachableInTile(FlowTileCacheEntry tile, int worldX, int worldY)
    {
        int localIndex = tile.GetLocalIndex(worldX, worldY);
        return IsFlowReachable(tile, localIndex);
    }

    private static bool AreCellsConnectedInsideSector(SectorData sector, int startX, int startY, int goalX, int goalY)
    {
        if (sector == null)
            throw new InvalidOperationException("AreCellsConnectedInsideSector failed: sector is null.");
        if (!IsInsideSector(sector, startX, startY) || !IsInsideSector(sector, goalX, goalY))
            return false;
        if (sector.LocalComponentIds == null || sector.LocalComponentIds.Length != sector.Width * sector.Height)
            throw new InvalidOperationException($"AreCellsConnectedInsideSector failed: local components missing sector={sector.SectorId}.");

        int startComponent = sector.LocalComponentIds[GetSectorLocalIndex(sector, startX, startY)];
        int goalComponent = sector.LocalComponentIds[GetSectorLocalIndex(sector, goalX, goalY)];
        return startComponent > 0 && startComponent == goalComponent;
    }

    private static void LogTileReachabilityFailure(
        AgentRuntimeData agent,
        FlowTileCacheEntry tile,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        TileGoalKind goalKind,
        int downstreamPortalId)
    {
        PathHandle handle = agent?.NavState.PathHandle;
        SectorData startSector = _world != null && startSectorId >= 0 && startSectorId < _world.Sectors.Length
            ? _world.Sectors[startSectorId]
            : null;
        bool startWalkable = _world != null && _world.IsWalkable(startX, startY);
        bool insideTile = tile != null && IsInsideSector(tile, startX, startY);
        string startCost = "tile=null";
        string startFlow = "tile=null";
        if (tile != null && insideTile)
        {
            int localIndex = tile.GetLocalIndex(startX, startY);
            float cost = GetTileIntegrationCostForDiagnostics(tile, startX, startY);
            startCost = float.IsPositiveInfinity(cost) ? "INF" : cost.ToString("F3");
            startFlow = ResolveRuntimeFlowDirection(tile, startX, startY, localIndex).ToString();
        }

        Debug.LogError(
            $"[FlowTileReachabilityFail] agent={agent?.CharacterKey ?? "null"} start=({startX},{startY}) goal=({goalX},{goalY}) " +
            $"startSector={startSectorId} goalSector={goalSectorId} startWalkable={startWalkable} insideTile={insideTile} " +
            $"startCost={startCost} startFlow={startFlow} goalKind={goalKind} downstreamPortal={downstreamPortalId} " +
            $"sectorDirty={(startSector != null ? startSector.DirtyVersion : -1)} tileKey={(tile != null ? FormatTileKey(tile.Key) : "null")} " +
            $"tileRect={(tile != null ? $"({tile.StartX},{tile.StartY},{tile.Width},{tile.Height})" : "null")} " +
            $"tileGoals={(tile != null ? FormatGoalCells(tile.GoalCells) : "null")} goalCosts={(tile != null ? FormatIntegrationCosts(tile, tile.GoalCells) : "null")} " +
            $"tileSummary={DescribeTileIntegrationSummary(tile)} handle={FormatPathHandle(handle)}");
    }

}
