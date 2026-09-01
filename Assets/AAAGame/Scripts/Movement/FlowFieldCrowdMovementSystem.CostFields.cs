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
    private static void RebuildCostFieldForSector(NavigationWorld world, int sectorId, IReadOnlyCollection<CostStamp> costStamps)
    {
        if (world == null)
            throw new InvalidOperationException("RebuildCostFieldForSector failed: world is null.");
        if (sectorId < 0 || sectorId >= world.Sectors.Length)
            throw new InvalidOperationException($"RebuildCostFieldForSector failed: sector id out of range {sectorId}.");
        if (world.CostField == null || world.CostField.Length != world.Width * world.Height)
            world.CostField = new byte[world.Width * world.Height];

        SectorData sector = world.Sectors[sectorId];
        if (world.SourceCostField != null && world.SourceCostField.Length == world.Width * world.Height)
        {
            InitializeAuthoredCostFieldForSector(world, sector, costStamps);
            return;
        }

        int sectorMinX = sector.StartX;
        int sectorMinY = sector.StartY;
        int sectorMaxX = sector.StartX + sector.Width - 1;
        int sectorMaxY = sector.StartY + sector.Height - 1;
        int padding = ResolveWallCostPaddingCells(world, 1);
        int sourceMinX = Mathf.Max(0, sectorMinX - padding);
        int sourceMinY = Mathf.Max(0, sectorMinY - padding);
        int sourceMaxX = Mathf.Min(world.Width - 1, sectorMaxX + padding);
        int sourceMaxY = Mathf.Min(world.Height - 1, sectorMaxY + padding);
        RebuildCostFieldInBounds(world, sourceMinX, sourceMinY, sourceMaxX, sourceMaxY, sectorMinX, sectorMinY, sectorMaxX, sectorMaxY, costStamps);
        sector.IsClearCostField = IsSectorCostFieldClear(world, sector);
        sector.IsClearFlowTile = IsSectorClearFlowTile(world, sector);
    }

    private static void InitializeAuthoredCostFieldForSector(NavigationWorld world, SectorData sector, IReadOnlyCollection<CostStamp> costStamps)
    {
        if (world == null)
            throw new InvalidOperationException("InitializeAuthoredCostFieldForSector failed: world is null.");
        if (sector == null)
            throw new InvalidOperationException("InitializeAuthoredCostFieldForSector failed: sector is null.");
        if (world.CostField == null || world.CostField.Length != world.Width * world.Height)
            throw new InvalidOperationException("InitializeAuthoredCostFieldForSector failed: authored cost field is missing.");

        for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
        {
            for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
            {
                int index = world.GetIndex(x, y);
                byte cost = world.SourceCostField != null && world.SourceCostField.Length == world.Width * world.Height
                    ? world.SourceCostField[index]
                    : world.CostField[index];
                if (!world.WalkableMask[index])
                {
                    world.CostField[index] = byte.MaxValue;
                    continue;
                }

                world.CostField[index] = cost == 0 || cost == byte.MaxValue ? (byte)1 : cost;
            }
        }

        ApplyCostStampsInBounds(
            world,
            sector.StartX,
            sector.StartY,
            sector.StartX + sector.Width - 1,
            sector.StartY + sector.Height - 1,
            costStamps);
        ApplyDynamicObstacleBlurToAuthoredCostSector(world, sector);
        sector.IsClearCostField = IsSectorCostFieldClear(world, sector);
        sector.IsClearFlowTile = IsSectorClearFlowTile(world, sector);
    }

    private static void ApplyDynamicObstacleBlurToAuthoredCostSector(NavigationWorld world, SectorData sector)
    {
        if (world.SourceCostField == null || world.SourceCostField.Length != world.Width * world.Height)
            return;

        int sectorMinX = sector.StartX;
        int sectorMinY = sector.StartY;
        int sectorMaxX = sector.StartX + sector.Width - 1;
        int sectorMaxY = sector.StartY + sector.Height - 1;
        int padding = ResolveWallCostPaddingCells(world, 1);
        int sourceMinX = Mathf.Max(0, sectorMinX - padding);
        int sourceMinY = Mathf.Max(0, sectorMinY - padding);
        int sourceMaxX = Mathf.Min(world.Width - 1, sectorMaxX + padding);
        int sourceMaxY = Mathf.Min(world.Height - 1, sectorMaxY + padding);

        int sourceWidth = sourceMaxX - sourceMinX + 1;
        int sourceHeight = sourceMaxY - sourceMinY + 1;

        // Most cost-dirty sectors are only padding around the actual obstacle. Their
        // authored cost is unchanged, so avoid allocating and running a local Dijkstra
        // unless the padded read domain contains a dynamically blocked authored cell.
        bool hasDynamicBlockedCell = false;
        for (int y = sourceMinY; y <= sourceMaxY && !hasDynamicBlockedCell; y++)
        {
            for (int x = sourceMinX; x <= sourceMaxX; x++)
            {
                int index = world.GetIndex(x, y);
                bool baseWalkable = world.BaseWalkableMask != null
                                    && world.BaseWalkableMask.Length == world.Width * world.Height
                                    && world.BaseWalkableMask[index];
                if (baseWalkable && !world.WalkableMask[index])
                {
                    hasDynamicBlockedCell = true;
                    break;
                }
            }
        }
        if (!hasDynamicBlockedCell)
            return;

        long[] wallDistance = CreateInitializedWallDistance(sourceWidth * sourceHeight);
        long wallCostBlurRadiusRaw = ResolveWallCostBlurRadiusCellsFixed(world).RawValue;
        DeterministicCostHeap openSet = new DeterministicCostHeap();
        for (int y = sourceMinY; y <= sourceMaxY; y++)
        {
            for (int x = sourceMinX; x <= sourceMaxX; x++)
            {
                int index = world.GetIndex(x, y);
                bool baseWalkable = world.BaseWalkableMask != null
                                    && world.BaseWalkableMask.Length == world.Width * world.Height
                                    && world.BaseWalkableMask[index];
                if (!baseWalkable)
                    continue;

                if (!world.WalkableMask[index])
                {
                    int blockedLocalIndex = GetLocalCostFieldIndex(x, y, sourceMinX, sourceMinY, sourceWidth);
                    wallDistance[blockedLocalIndex] = 0L;
                    openSet.Push(blockedLocalIndex, 0L);
                    continue;
                }

                if (TouchesDynamicBlockedOrUntraversableNeighbor(world, x, y))
                {
                    int localIndex = GetLocalCostFieldIndex(x, y, sourceMinX, sourceMinY, sourceWidth);
                    wallDistance[localIndex] = DeterministicPortalCostScale;
                    openSet.Push(localIndex, DeterministicPortalCostScale);
                }
            }
        }

        while (openSet.Count > 0)
        {
            DeterministicCostQueueNode node = openSet.Pop();
            if (node.Cost != wallDistance[node.Index])
                continue;
            if (node.Cost >= wallCostBlurRadiusRaw)
                continue;

            int worldX = sourceMinX + node.Index % sourceWidth;
            int worldY = sourceMinY + node.Index / sourceWidth;
            for (int i = 0; i < NeighborOffsetX.Length; i++)
            {
                int nextX = worldX + NeighborOffsetX[i];
                int nextY = worldY + NeighborOffsetY[i];
                if (nextX < sourceMinX || nextX > sourceMaxX || nextY < sourceMinY || nextY > sourceMaxY)
                    continue;

                int nextIndex = world.GetIndex(nextX, nextY);
                if (!world.WalkableMask[nextIndex])
                    continue;

                long stepCost = NeighborOffsetX[i] != 0 && NeighborOffsetY[i] != 0
                    ? DeterministicPortalDiagonalCost
                    : DeterministicPortalCostScale;
                long newDistance = checked(node.Cost + stepCost);
                int nextLocalIndex = GetLocalCostFieldIndex(nextX, nextY, sourceMinX, sourceMinY, sourceWidth);
                if (newDistance >= wallDistance[nextLocalIndex] || newDistance > wallCostBlurRadiusRaw)
                    continue;

                wallDistance[nextLocalIndex] = newDistance;
                openSet.Push(nextLocalIndex, newDistance);
            }
        }

        int adjacentPenalty = ResolveWallCostAdjacentPenalty(world);
        int outerPenalty = adjacentPenalty > WallCostAdjacentPenalty ? 1 : WallCostOuterPenalty;
        for (int y = sectorMinY; y <= sectorMaxY; y++)
        {
            for (int x = sectorMinX; x <= sectorMaxX; x++)
            {
                int index = world.GetIndex(x, y);
                if (!world.WalkableMask[index])
                    continue;

                long distance = wallDistance[GetLocalCostFieldIndex(x, y, sourceMinX, sourceMinY, sourceWidth)];
                if (distance == long.MaxValue || distance > wallCostBlurRadiusRaw)
                    continue;

                int penalty = ResolveWallCostPenalty(distance, wallCostBlurRadiusRaw, outerPenalty, adjacentPenalty);
                world.CostField[index] = (byte)Mathf.Clamp(Mathf.Max(world.CostField[index], 1 + penalty), 1, 254);
            }
        }
    }

    private static bool TouchesDynamicBlockedOrUntraversableNeighbor(NavigationWorld world, int x, int y)
    {
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int nx = x + NeighborOffsetX[i];
            int ny = y + NeighborOffsetY[i];
            if (nx < 0 || nx >= world.Width || ny < 0 || ny >= world.Height)
                continue;

            int neighborIndex = world.GetIndex(nx, ny);
            bool baseWalkable = world.BaseWalkableMask != null
                                && world.BaseWalkableMask.Length == world.Width * world.Height
                                && world.BaseWalkableMask[neighborIndex];
            if (baseWalkable && !world.WalkableMask[neighborIndex])
                return true;
        }

        return false;
    }

    private static void RebuildCostFieldInBounds(
        NavigationWorld world,
        int sourceMinX,
        int sourceMinY,
        int sourceMaxX,
        int sourceMaxY,
        int writeMinX,
        int writeMinY,
        int writeMaxX,
        int writeMaxY,
        IReadOnlyCollection<CostStamp> costStamps)
    {
        int sourceWidth = sourceMaxX - sourceMinX + 1;
        int sourceHeight = sourceMaxY - sourceMinY + 1;
        long[] wallDistance = CreateInitializedWallDistance(sourceWidth * sourceHeight);
        long wallCostBlurRadiusRaw = ResolveWallCostBlurRadiusCellsFixed(world).RawValue;

        DeterministicCostHeap openSet = new DeterministicCostHeap();
        for (int y = sourceMinY; y <= sourceMaxY; y++)
        {
            for (int x = sourceMinX; x <= sourceMaxX; x++)
            {
                int index = world.GetIndex(x, y);
                if (!world.WalkableMask[index])
                {
                    if (IsInsideBounds(x, y, writeMinX, writeMinY, writeMaxX, writeMaxY))
                        world.CostField[index] = 255;
                    int blockedLocalIndex = GetLocalCostFieldIndex(x, y, sourceMinX, sourceMinY, sourceWidth);
                    wallDistance[blockedLocalIndex] = 0L;
                    openSet.Push(blockedLocalIndex, 0L);
                    continue;
                }

                if (IsInsideBounds(x, y, writeMinX, writeMinY, writeMaxX, writeMaxY))
                    world.CostField[index] = 1;
                if (!TouchesBlockedOrUntraversableNeighbor(world, x, y))
                    continue;

                int localIndex = GetLocalCostFieldIndex(x, y, sourceMinX, sourceMinY, sourceWidth);
                wallDistance[localIndex] = DeterministicPortalCostScale;
                openSet.Push(localIndex, DeterministicPortalCostScale);
            }
        }

        while (openSet.Count > 0)
        {
            DeterministicCostQueueNode node = openSet.Pop();
            if (node.Cost != wallDistance[node.Index])
                continue;
            if (node.Cost >= wallCostBlurRadiusRaw)
                continue;

            int worldX = sourceMinX + node.Index % sourceWidth;
            int worldY = sourceMinY + node.Index / sourceWidth;
            for (int i = 0; i < NeighborOffsetX.Length; i++)
            {
                int nextX = worldX + NeighborOffsetX[i];
                int nextY = worldY + NeighborOffsetY[i];
                if (nextX < sourceMinX || nextX > sourceMaxX || nextY < sourceMinY || nextY > sourceMaxY)
                    continue;

                int nextIndex = world.GetIndex(nextX, nextY);
                if (!world.WalkableMask[nextIndex])
                    continue;

                long stepCost = NeighborOffsetX[i] != 0 && NeighborOffsetY[i] != 0
                    ? DeterministicPortalDiagonalCost
                    : DeterministicPortalCostScale;
                long newDistance = checked(node.Cost + stepCost);
                int nextLocalIndex = GetLocalCostFieldIndex(nextX, nextY, sourceMinX, sourceMinY, sourceWidth);
                if (newDistance >= wallDistance[nextLocalIndex] || newDistance > wallCostBlurRadiusRaw)
                    continue;

                wallDistance[nextLocalIndex] = newDistance;
                openSet.Push(nextLocalIndex, newDistance);
            }
        }

        for (int y = writeMinY; y <= writeMaxY; y++)
        {
            for (int x = writeMinX; x <= writeMaxX; x++)
            {
                int index = world.GetIndex(x, y);
                if (!world.WalkableMask[index])
                {
                    world.CostField[index] = 255;
                    continue;
                }

                int penalty = ResolveSlopeCostPenalty(world, x, y);
                long distance = wallDistance[GetLocalCostFieldIndex(x, y, sourceMinX, sourceMinY, sourceWidth)];
                if (distance != long.MaxValue && distance <= wallCostBlurRadiusRaw)
                {
                    int adjacentPenalty = ResolveWallCostAdjacentPenalty(world);
                    int outerPenalty = adjacentPenalty > WallCostAdjacentPenalty ? 1 : WallCostOuterPenalty;
                    penalty += ResolveWallCostPenalty(distance, wallCostBlurRadiusRaw, outerPenalty, adjacentPenalty);
                }

                world.CostField[index] = (byte)Mathf.Clamp(1 + penalty, 1, 254);
            }
        }
        ApplyCostStampsInBounds(world, writeMinX, writeMinY, writeMaxX, writeMaxY, costStamps);
    }

    private static Fix64 ResolveWallCostBlurRadiusCellsFixed(NavigationWorld world)
    {
        if (world == null)
            throw new InvalidOperationException("ResolveWallCostBlurRadiusCellsFixed failed: world is null.");

        Fix64 extraRadius = Fix64.Max(
            Fix64.Zero,
            world.AgentRadiusFixed - Fix64.FromRaw(2048));
        Fix64 extraRadiusCells = NavigationGridFixedMath.DivideByCellSize(
            extraRadius,
            world.CellSizeGridRaw);
        return Fix64.FromRaw(WallCostBlurRadiusRaw) + extraRadiusCells;
    }

    private static int ResolveWallCostPaddingCells(NavigationWorld world, int extraPadding)
    {
        if (extraPadding < 0)
            throw new ArgumentOutOfRangeException(nameof(extraPadding), extraPadding, "Wall cost padding cannot be negative.");
        return checked((int)(long)Fix64.Ceiling(ResolveWallCostBlurRadiusCellsFixed(world)) + extraPadding);
    }

    private static long[] CreateInitializedWallDistance(int length)
    {
        if (length <= 0)
            throw new InvalidOperationException($"CreateInitializedWallDistance failed: invalid length {length}.");

        long[] wallDistance = new long[length];
        for (int i = 0; i < wallDistance.Length; i++)
            wallDistance[i] = long.MaxValue;

        return wallDistance;
    }

    private static int ResolveWallCostPenalty(long distanceRaw, long radiusRaw, int outerPenalty, int adjacentPenalty)
    {
        if (distanceRaw < 0 || radiusRaw <= 0 || distanceRaw > radiusRaw)
            throw new ArgumentOutOfRangeException(nameof(distanceRaw), distanceRaw, $"Invalid wall cost distance/radius distance={distanceRaw} radius={radiusRaw}.");
        if (outerPenalty < 0 || adjacentPenalty < outerPenalty)
            throw new ArgumentOutOfRangeException(nameof(outerPenalty), outerPenalty, $"Invalid wall penalties outer={outerPenalty} adjacent={adjacentPenalty}.");

        long denominatorRaw = Math.Max(Fix64.FromRaw(5).RawValue, radiusRaw - Fix64.One.RawValue);
        Fix64 t = Fix64.Clamp(
            Fix64.FromRaw(radiusRaw - distanceRaw) / Fix64.FromRaw(denominatorRaw),
            Fix64.Zero,
            Fix64.One);
        Fix64 interpolated = (Fix64)outerPenalty + (Fix64)(adjacentPenalty - outerPenalty) * t;
        return RoundNonNegativeFix64ToIntMidpointToEven(interpolated);
    }

    private static int RoundNonNegativeFix64ToIntMidpointToEven(Fix64 value)
    {
        if (value < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(value), value.RawValue, "Wall cost penalty cannot be negative.");

        long whole = value.RawValue / Fix64.One.RawValue;
        long remainder = value.RawValue % Fix64.One.RawValue;
        long half = Fix64.One.RawValue / 2;
        if (remainder > half || (remainder == half && (whole & 1L) != 0L))
            whole = checked(whole + 1L);
        return checked((int)whole);
    }

    private static int GetLocalCostFieldIndex(int worldX, int worldY, int minX, int minY, int width)
    {
        int localX = worldX - minX;
        int localY = worldY - minY;
        int index = localX + localY * width;
        if (localX < 0 || localX >= width || localY < 0 || index < 0)
            throw new InvalidOperationException(
                $"GetLocalCostFieldIndex failed: cell=({worldX},{worldY}) origin=({minX},{minY}) width={width} local=({localX},{localY}).");

        return index;
    }

    private static int ResolveWallCostAdjacentPenalty(NavigationWorld world)
    {
        if (world == null)
            throw new InvalidOperationException("ResolveWallCostAdjacentPenalty failed: world is null.");

        Fix64 extraRadius = Fix64.Max(
            Fix64.Zero,
            world.AgentRadiusFixed - Fix64.FromRaw(2048));
        int extraRadiusCells = NavigationGridFixedMath.DivideCeilingByCellSize(
            extraRadius,
            world.CellSizeGridRaw);
        return WallCostAdjacentPenalty + extraRadiusCells;
    }

    private static int ResolveSlopeCostPenalty(NavigationWorld world, int worldX, int worldY)
    {
        return 0;
    }

    private static void ApplyCostStampsInBounds(NavigationWorld world, int writeMinX, int writeMinY, int writeMaxX, int writeMaxY, IReadOnlyCollection<CostStamp> costStamps)
    {
        if (costStamps == null)
            costStamps = CostStamps.Values;

        if (costStamps.Count == 0)
            return;

        foreach (CostStamp stamp in costStamps)
        {
            if (stamp.AgentTypeId != AnyAgentTypeId && stamp.AgentTypeId != world.AgentTypeId)
                continue;

            world.WorldToGridFixed(stamp.BoundsMinFixed, out int rawMinX, out int rawMinY);
            world.WorldToGridFixed(stamp.BoundsMaxFixed, out int rawMaxX, out int rawMaxY);
            int minX = Mathf.Max(rawMinX, writeMinX);
            int maxX = Mathf.Min(rawMaxX, writeMaxX);
            int minY = Mathf.Max(rawMinY, writeMinY);
            int maxY = Mathf.Min(rawMaxY, writeMaxY);
            if (minX > maxX || minY > maxY)
                continue;

            for (int y = minY; y <= maxY; y++)
            {
                int rowStart = y * world.Width;
                for (int x = minX; x <= maxX; x++)
                {
                    int index = rowStart + x;
                    if (!world.WalkableMask[index] || world.CostField[index] >= 255)
                        continue;

                    if (!TryResolveCostStampCellCost(world, stamp, x, y, out byte stampCost))
                        continue;

                    world.CostField[index] = stampCost;
                }
            }
        }
    }

    private static bool TryResolveCostStampCellCost(NavigationWorld world, CostStamp stamp, int worldX, int worldY, out byte cost)
    {
        cost = stamp.Cost;
        if (stamp.Costs == null)
            return true;
        if (stamp.Width <= 0 || stamp.Height <= 0 || stamp.CellSizeGridRaw <= 0)
            throw new InvalidOperationException($"TryResolveCostStampCellCost failed: invalid grid stamp id={stamp.Id} size={stamp.Width}x{stamp.Height} cellSize={stamp.CellSize}.");
        if (stamp.Costs.Length != stamp.Width * stamp.Height)
            throw new InvalidOperationException($"TryResolveCostStampCellCost failed: grid stamp costs length {stamp.Costs.Length} does not match {stamp.Width}x{stamp.Height} id={stamp.Id}.");

        long centerXGridRaw = NavigationGridFixedMath.GridCellCenterRaw(
            world.OriginXGridRaw,
            world.CellSizeGridRaw,
            worldX);
        long centerYGridRaw = NavigationGridFixedMath.GridCellCenterRaw(
            world.OriginZGridRaw,
            world.CellSizeGridRaw,
            worldY);
        int stampX = NavigationGridFixedMath.GridRawToCell(
            centerXGridRaw,
            stamp.OriginXGridRaw,
            stamp.CellSizeGridRaw);
        int stampY = NavigationGridFixedMath.GridRawToCell(
            centerYGridRaw,
            stamp.OriginZGridRaw,
            stamp.CellSizeGridRaw);
        if (stampX < 0 || stampX >= stamp.Width || stampY < 0 || stampY >= stamp.Height)
            return false;

        cost = stamp.Costs[stampX + stampY * stamp.Width];
        return true;
    }

    private static bool IsCellAffectedByCostStamp(NavigationWorld world, int worldX, int worldY)
    {
        if (CostStamps.Count == 0)
            return false;

        foreach (CostStamp stamp in CostStamps.Values)
        {
            if (stamp.AgentTypeId != AnyAgentTypeId && stamp.AgentTypeId != world.AgentTypeId)
                continue;

            world.WorldToGridFixed(stamp.BoundsMinFixed, out int rawMinX, out int rawMinY);
            world.WorldToGridFixed(stamp.BoundsMaxFixed, out int rawMaxX, out int rawMaxY);
            if (worldX < rawMinX || worldX > rawMaxX || worldY < rawMinY || worldY > rawMaxY)
                continue;

            if (!world.WalkableMask[world.GetIndex(worldX, worldY)] || GetCostFieldValueStrict(world, worldX, worldY) >= 255)
                continue;

            if (TryResolveCostStampCellCost(world, stamp, worldX, worldY, out _))
                return true;
        }

        return false;
    }

    private static bool IsInsideBounds(int x, int y, int minX, int minY, int maxX, int maxY)
    {
        return x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    private static void ResolveSectorBounds(NavigationWorld world, HashSet<int> sectorIds, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = world.Width - 1;
        minY = world.Height - 1;
        maxX = 0;
        maxY = 0;
        bool found = false;
        foreach (int sectorId in sectorIds)
        {
            SectorData sector = world.Sectors[sectorId];
            minX = Mathf.Min(minX, sector.StartX);
            minY = Mathf.Min(minY, sector.StartY);
            maxX = Mathf.Max(maxX, sector.StartX + sector.Width - 1);
            maxY = Mathf.Max(maxY, sector.StartY + sector.Height - 1);
            found = true;
        }

        if (!found)
            throw new InvalidOperationException("ResolveSectorBounds failed: sector set is empty.");
    }

    private static HashSet<int> ExpandDirtySectorsByCellRadius(NavigationWorld world, HashSet<int> sectorIds, int radiusCells)
    {
        if (world == null)
            throw new InvalidOperationException("ExpandDirtySectorsByCellRadius failed: world is null.");
        if (sectorIds == null)
            throw new InvalidOperationException("ExpandDirtySectorsByCellRadius failed: sectorIds is null.");

        HashSet<int> expanded = new HashSet<int>(sectorIds);
        if (sectorIds.Count == 0)
            return expanded;

        int clampedRadius = Mathf.Max(0, radiusCells);
        foreach (int sectorId in sectorIds)
        {
            if (sectorId < 0 || sectorId >= world.Sectors.Length)
                throw new InvalidOperationException($"ExpandDirtySectorsByCellRadius failed: sector id out of range {sectorId}.");

            SectorData sector = world.Sectors[sectorId];
            int minX = Mathf.Max(0, sector.StartX - clampedRadius);
            int minY = Mathf.Max(0, sector.StartY - clampedRadius);
            int maxX = Mathf.Min(world.Width - 1, sector.StartX + sector.Width - 1 + clampedRadius);
            int maxY = Mathf.Min(world.Height - 1, sector.StartY + sector.Height - 1 + clampedRadius);

            int minSectorX = Mathf.Clamp(minX / world.SectorSizeInCells, 0, world.SectorCountX - 1);
            int maxSectorX = Mathf.Clamp(maxX / world.SectorSizeInCells, 0, world.SectorCountX - 1);
            int minSectorY = Mathf.Clamp(minY / world.SectorSizeInCells, 0, world.SectorCountY - 1);
            int maxSectorY = Mathf.Clamp(maxY / world.SectorSizeInCells, 0, world.SectorCountY - 1);
            for (int sy = minSectorY; sy <= maxSectorY; sy++)
            {
                for (int sx = minSectorX; sx <= maxSectorX; sx++)
                    expanded.Add(sy * world.SectorCountX + sx);
            }
        }

        return expanded;
    }

    private static bool TouchesBlockedOrUntraversableNeighbor(NavigationWorld world, int centerX, int centerY)
    {
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int nextX = centerX + NeighborOffsetX[i];
            int nextY = centerY + NeighborOffsetY[i];
            if (nextX < 0 || nextX >= world.Width || nextY < 0 || nextY >= world.Height)
                return true;
            if (!world.IsWalkable(nextX, nextY))
                return true;
            if (!CanTraverseNeighborCells(world, centerX, centerY, nextX, nextY))
                return true;
        }

        return false;
    }

    private static bool HasInternalBlockedOrUntraversableNeighbor(NavigationWorld world, int centerX, int centerY)
    {
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int nextX = centerX + NeighborOffsetX[i];
            int nextY = centerY + NeighborOffsetY[i];
            if (nextX < 0 || nextX >= world.Width || nextY < 0 || nextY >= world.Height)
                continue;
            if (!world.IsWalkable(nextX, nextY))
                return true;
            if (!CanTraverseNeighborCells(world, centerX, centerY, nextX, nextY))
                return true;
        }

        return false;
    }

    private static bool IsBoundaryOnlySoftCostCell(NavigationWorld world, int centerX, int centerY)
    {
        if (IsCellAffectedByCostStamp(world, centerX, centerY))
            return false;

        int centerIndex = world.GetIndex(centerX, centerY);
        if (world.SourceCostField != null
            && world.SourceCostField.Length == world.Width * world.Height
            && world.SourceCostField[centerIndex] > 1
            && world.SourceCostField[centerIndex] < byte.MaxValue)
        {
            return false;
        }

        int radius = ResolveWallCostPaddingCells(world, 0);
        bool hasOutsideWithinRadius = false;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                int dx = x - centerX;
                int dy = y - centerY;
                if (dx * dx + dy * dy > radius * radius)
                    continue;

                if (x < 0 || x >= world.Width || y < 0 || y >= world.Height)
                {
                    hasOutsideWithinRadius = true;
                    continue;
                }

                if (!world.IsWalkable(x, y))
                    return false;
            }
        }

        if (!hasOutsideWithinRadius)
            return false;

        return !HasInternalBlockedOrUntraversableNeighbor(world, centerX, centerY);
    }

    private static float ResolveTraversalCost(NavigationWorld world, int fromX, int fromY, int toX, int toY)
    {
        int fromCost = GetCostFieldValueStrict(world, fromX, fromY);
        int toCost = GetCostFieldValueStrict(world, toX, toY);
        if (fromCost >= 255 || toCost >= 255)
            return float.PositiveInfinity;

        return Mathf.Max(1f, (fromCost + toCost) * 0.5f);
    }

    private static bool IsSectorCostFieldClear(NavigationWorld world, SectorData sector)
    {
        if (world == null)
            throw new InvalidOperationException("IsSectorCostFieldClear failed: world is null.");
        if (sector == null)
            throw new InvalidOperationException("IsSectorCostFieldClear failed: sector is null.");
        if (world.CostField == null || world.CostField.Length != world.Width * world.Height)
            return true;

        for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
        {
            int rowStart = y * world.Width;
            for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
            {
                int index = rowStart + x;
                if (!world.WalkableMask[index] || world.CostField[index] != 1)
                    return false;
            }
        }

        return true;
    }

    private static bool IsSectorClearFlowTile(NavigationWorld world, SectorData sector)
    {
        if (!IsSectorCostFieldClear(world, sector))
            return false;
        if (world.NeighborTraversalMask == null || world.NeighborTraversalMask.Length != world.Width * world.Height)
            return false;

        for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
        {
            for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
            {
                if (!world.IsWalkable(x, y))
                    return false;

                for (int i = 0; i < NeighborOffsetX.Length; i++)
                {
                    int nextX = x + NeighborOffsetX[i];
                    int nextY = y + NeighborOffsetY[i];
                    if (!IsInsideSector(sector, nextX, nextY))
                        continue;
                    if (!CanTraverseNeighborCells(world, x, y, nextX, nextY))
                        return false;
                }
            }
        }

        return true;
    }

    private static bool TryGetSectorForCell(NavigationWorld world, int worldX, int worldY, out SectorData sector)
    {
        sector = null;
        if (world == null || world.Sectors == null)
            return false;
        if (!world.TryGetSectorId(worldX, worldY, out int sectorId))
            return false;

        sector = world.Sectors[sectorId];
        return sector != null;
    }

    private static bool AreSectorsSameOrAdjacent(NavigationWorld world, int sectorAId, int sectorBId)
    {
        if (world == null)
            throw new InvalidOperationException("AreSectorsSameOrAdjacent failed: world is null.");
        if (world.SectorCountX <= 0)
            throw new InvalidOperationException($"AreSectorsSameOrAdjacent failed: invalid sector grid countX={world.SectorCountX}.");
        if (sectorAId < 0 || sectorAId >= world.Sectors.Length || sectorBId < 0 || sectorBId >= world.Sectors.Length)
            return false;

        int ax = sectorAId % world.SectorCountX;
        int ay = sectorAId / world.SectorCountX;
        int bx = sectorBId % world.SectorCountX;
        int by = sectorBId / world.SectorCountX;
        return Mathf.Abs(ax - bx) <= 1 && Mathf.Abs(ay - by) <= 1;
    }

}
