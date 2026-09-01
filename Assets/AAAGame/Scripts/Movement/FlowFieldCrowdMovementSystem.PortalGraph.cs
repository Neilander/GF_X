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
    private static void RemovePortalsTouchingSectors(NavigationWorld world, HashSet<int> affectedSectors, HashSet<int> transitionDirtySectors)
    {
        List<int> portalIdsToRemove = null;
        foreach (KeyValuePair<int, PortalData> pair in world.PortalsById)
        {
            PortalData portal = pair.Value;
            if (!affectedSectors.Contains(portal.SectorAId) && !affectedSectors.Contains(portal.SectorBId))
                continue;

            portalIdsToRemove ??= new List<int>();
            portalIdsToRemove.Add(pair.Key);
        }

        if (portalIdsToRemove == null)
            return;

        for (int i = 0; i < portalIdsToRemove.Count; i++)
        {
            int portalId = portalIdsToRemove[i];
            if (!world.PortalsById.TryGetValue(portalId, out PortalData portal))
                continue;

            world.PortalsById.Remove(portalId);
            world.UsedPortalIds.Remove(portalId);
            PortalSignature signature = BuildPortalSignature(
                portal.SectorAId,
                portal.SectorBId,
                portal.CellsA,
                portal.IsVerticalBoundary);
            if (!world.PortalIdsBySignature.TryGetValue(signature, out int signaturePortalId)
                || signaturePortalId != portalId)
            {
                throw new InvalidOperationException(
                    $"RemovePortalsTouchingSectors failed: portal signature mismatch portal={portalId} signaturePortal={signaturePortalId}.");
            }
            world.PortalIdsBySignature.Remove(signature);
            world.Sectors[portal.SectorAId].PortalIds.Remove(portalId);
            world.Sectors[portal.SectorBId].PortalIds.Remove(portalId);
            transitionDirtySectors.Add(portal.SectorAId);
            transitionDirtySectors.Add(portal.SectorBId);
        }
    }

    private static void RebuildPortalBoundariesForSector(
        NavigationWorld world,
        int sectorId,
        HashSet<int> affectedSectors,
        HashSet<long> processedBoundaries,
        List<PortalData> portals)
    {
        if (sectorId < 0 || sectorId >= world.Sectors.Length)
            return;

        int sectorX = sectorId % world.SectorCountX;
        int sectorY = sectorId / world.SectorCountX;
        TryRebuildPortalBoundary(world, sectorId, sectorX + 1, sectorY, affectedSectors, processedBoundaries, portals);
        TryRebuildPortalBoundary(world, sectorId, sectorX - 1, sectorY, affectedSectors, processedBoundaries, portals);
        TryRebuildPortalBoundary(world, sectorId, sectorX, sectorY + 1, affectedSectors, processedBoundaries, portals);
        TryRebuildPortalBoundary(world, sectorId, sectorX, sectorY - 1, affectedSectors, processedBoundaries, portals);
    }

    private static void TryRebuildPortalBoundary(
        NavigationWorld world,
        int sectorId,
        int neighborSectorX,
        int neighborSectorY,
        HashSet<int> affectedSectors,
        HashSet<long> processedBoundaries,
        List<PortalData> portals)
    {
        if (neighborSectorX < 0 || neighborSectorX >= world.SectorCountX || neighborSectorY < 0 || neighborSectorY >= world.SectorCountY)
            return;

        int neighborSectorId = neighborSectorY * world.SectorCountX + neighborSectorX;
        if (!affectedSectors.Contains(sectorId) && !affectedSectors.Contains(neighborSectorId))
            return;

        int minSectorId = Mathf.Min(sectorId, neighborSectorId);
        int maxSectorId = Mathf.Max(sectorId, neighborSectorId);
        long boundaryKey = ((long)minSectorId << 32) | (uint)maxSectorId;
        if (!processedBoundaries.Add(boundaryKey))
            return;

        int sectorX = sectorId % world.SectorCountX;
        int sectorY = sectorId / world.SectorCountX;
        if (neighborSectorY == sectorY)
        {
            int leftId = sectorX < neighborSectorX ? sectorId : neighborSectorId;
            int rightId = sectorX < neighborSectorX ? neighborSectorId : sectorId;
            BuildVerticalBoundaryPortals(world, portals, world.Sectors[leftId], world.Sectors[rightId]);
            return;
        }

        int topId = sectorY < neighborSectorY ? sectorId : neighborSectorId;
        int bottomId = sectorY < neighborSectorY ? neighborSectorId : sectorId;
        BuildHorizontalBoundaryPortals(world, portals, world.Sectors[topId], world.Sectors[bottomId]);
    }

    private static PortalData[] BuildPortalArray(NavigationWorld world)
    {
        if (world == null || world.PortalsById == null)
            throw new InvalidOperationException("BuildPortalArray failed: world or portal lookup is null.");

        PortalData[] portals = new PortalData[world.PortalsById.Count];
        var portalIds = new List<int>(world.PortalsById.Keys);
        portalIds.Sort();
        for (int i = 0; i < portalIds.Count; i++)
        {
            int portalId = portalIds[i];
            PortalData portal = world.PortalsById[portalId];
            if (portal == null || portal.PortalId != portalId)
                throw new InvalidOperationException($"BuildPortalArray failed: portal lookup mismatch key={portalId}, value={portal?.PortalId.ToString() ?? "null"}.");
            portals[i] = portal;
        }
        return portals;
    }

    private static void BuildVerticalPortals(NavigationWorld world, List<PortalData> portals)
    {
        for (int sectorY = 0; sectorY < world.SectorCountY; sectorY++)
        {
            for (int sectorX = 0; sectorX < world.SectorCountX - 1; sectorX++)
            {
                SectorData sectorA = world.Sectors[sectorY * world.SectorCountX + sectorX];
                SectorData sectorB = world.Sectors[sectorY * world.SectorCountX + sectorX + 1];
                BuildVerticalBoundaryPortals(world, portals, sectorA, sectorB);
            }
        }
    }

    private static void BuildHorizontalPortals(NavigationWorld world, List<PortalData> portals)
    {
        for (int sectorY = 0; sectorY < world.SectorCountY - 1; sectorY++)
        {
            for (int sectorX = 0; sectorX < world.SectorCountX; sectorX++)
            {
                SectorData sectorA = world.Sectors[sectorY * world.SectorCountX + sectorX];
                SectorData sectorB = world.Sectors[(sectorY + 1) * world.SectorCountX + sectorX];
                BuildHorizontalBoundaryPortals(world, portals, sectorA, sectorB);
            }
        }
    }

    private static void BuildVerticalBoundaryPortals(NavigationWorld world, List<PortalData> portals, SectorData sectorA, SectorData sectorB)
    {
        int boundaryAX = sectorA.StartX + sectorA.Width - 1;
        int boundaryBX = sectorB.StartX;

        List<Vector2Int> cellsA = null;
        List<Vector2Int> cellsB = null;
        for (int y = Mathf.Max(sectorA.StartY, sectorB.StartY); y < Mathf.Min(sectorA.StartY + sectorA.Height, sectorB.StartY + sectorB.Height); y++)
        {
            bool passable = CanTraverseNeighborCells(world, boundaryAX, y, boundaryBX, y);
            if (passable)
            {
                cellsA ??= new List<Vector2Int>(8);
                cellsB ??= new List<Vector2Int>(8);
                cellsA.Add(new Vector2Int(boundaryAX, y));
                cellsB.Add(new Vector2Int(boundaryBX, y));
                continue;
            }

            FlushPortalRun(world, portals, sectorA.SectorId, sectorB.SectorId, cellsA, cellsB, true);
            cellsA = null;
            cellsB = null;
        }

        FlushPortalRun(world, portals, sectorA.SectorId, sectorB.SectorId, cellsA, cellsB, true);
    }

    private static void BuildHorizontalBoundaryPortals(NavigationWorld world, List<PortalData> portals, SectorData sectorA, SectorData sectorB)
    {
        int boundaryAY = sectorA.StartY + sectorA.Height - 1;
        int boundaryBY = sectorB.StartY;

        List<Vector2Int> cellsA = null;
        List<Vector2Int> cellsB = null;
        for (int x = Mathf.Max(sectorA.StartX, sectorB.StartX); x < Mathf.Min(sectorA.StartX + sectorA.Width, sectorB.StartX + sectorB.Width); x++)
        {
            bool passable = CanTraverseNeighborCells(world, x, boundaryAY, x, boundaryBY);
            if (passable)
            {
                cellsA ??= new List<Vector2Int>(8);
                cellsB ??= new List<Vector2Int>(8);
                cellsA.Add(new Vector2Int(x, boundaryAY));
                cellsB.Add(new Vector2Int(x, boundaryBY));
                continue;
            }

            FlushPortalRun(world, portals, sectorA.SectorId, sectorB.SectorId, cellsA, cellsB, false);
            cellsA = null;
            cellsB = null;
        }

        FlushPortalRun(world, portals, sectorA.SectorId, sectorB.SectorId, cellsA, cellsB, false);
    }

    private static void FlushPortalRun(
        NavigationWorld world,
        List<PortalData> portals,
        int sectorAId,
        int sectorBId,
        List<Vector2Int> cellsA,
        List<Vector2Int> cellsB,
        bool isVerticalBoundary)
    {
        if (cellsA == null || cellsB == null || cellsA.Count == 0 || cellsB.Count == 0)
            return;

        if (cellsA.Count != cellsB.Count)
            throw new InvalidOperationException($"FlushPortalRun failed: side cell count mismatch A={cellsA.Count} B={cellsB.Count}.");

        bool runIsNarrow = IsPortalBottleneck(world, sectorAId, sectorBId, cellsA, cellsB, isVerticalBoundary);
        int segmentStart = 0;
        for (int i = 1; i <= cellsA.Count; i++)
        {
            bool reachedEnd = i == cellsA.Count;
            bool costSplit = !reachedEnd && ShouldSplitPortalRunBeforeIndex(world, cellsA, cellsB, i);
            if (!reachedEnd && !costSplit)
                continue;

            AddPortalSegment(world, portals, sectorAId, sectorBId, cellsA, cellsB, segmentStart, i - segmentStart, isVerticalBoundary, runIsNarrow);
            segmentStart = i;
        }
    }

    private static bool ShouldSplitPortalRunBeforeIndex(NavigationWorld world, List<Vector2Int> cellsA, List<Vector2Int> cellsB, int index)
    {
        if (index <= 0 || index >= cellsA.Count || index >= cellsB.Count)
            return false;

        int previousCost = ResolvePortalPairCost(world, cellsA[index - 1], cellsB[index - 1]);
        int currentCost = ResolvePortalPairCost(world, cellsA[index], cellsB[index]);
        return Mathf.Abs(currentCost - previousCost) >= PortalWindowSplitCostDelta;
    }

    private static int ResolvePortalPairCost(NavigationWorld world, Vector2Int cellA, Vector2Int cellB)
    {
        int costA = GetCostFieldValueStrict(world, cellA.x, cellA.y);
        int costB = GetCostFieldValueStrict(world, cellB.x, cellB.y);
        if (costA >= 255 || costB >= 255)
            return 255;

        return Mathf.Max(1, (costA + costB) / 2);
    }

    private static void AddPortalSegment(
        NavigationWorld world,
        List<PortalData> portals,
        int sectorAId,
        int sectorBId,
        List<Vector2Int> runCellsA,
        List<Vector2Int> runCellsB,
        int start,
        int count,
        bool isVerticalBoundary,
        bool runIsNarrow)
    {
        if (count <= 0)
            throw new InvalidOperationException("AddPortalSegment failed: segment count must be positive.");

        List<Vector2Int> cellsA = runCellsA.GetRange(start, count);
        List<Vector2Int> cellsB = runCellsB.GetRange(start, count);
        Vector3 center = Vector3.zero;
        for (int i = 0; i < cellsA.Count; i++)
        {
            center += world.GridToWorldCenter(cellsA[i].x, cellsA[i].y);
            center += world.GridToWorldCenter(cellsB[i].x, cellsB[i].y);
        }

        center /= cellsA.Count * 2f;

        PortalData portal = new PortalData
        {
            PortalId = ResolveStablePortalId(world, BuildPortalSignature(sectorAId, sectorBId, cellsA, isVerticalBoundary)),
            SectorAId = sectorAId,
            SectorBId = sectorBId,
            CellsA = cellsA.ToArray(),
            CellsB = cellsB.ToArray(),
            WorldCenter = center,
            WidthCells = cellsA.Count,
            IsNarrow = runIsNarrow,
            IsVerticalBoundary = isVerticalBoundary
        };
        portals.Add(portal);
    }

    private static float ResolvePortalCrossingCost(PortalData portal)
    {
        if (portal == null)
            throw new InvalidOperationException("ResolvePortalCrossingCost failed: portal is null.");

        return PortalBaseCrossingCost;
    }

    private static PortalSignature BuildPortalSignature(int sectorAId, int sectorBId, List<Vector2Int> cellsA, bool isVerticalBoundary)
    {
        if (cellsA == null || cellsA.Count == 0)
            throw new InvalidOperationException("BuildPortalSignature failed: cells are empty.");

        int firstAxis = isVerticalBoundary ? cellsA[0].y : cellsA[0].x;
        int lastAxis = isVerticalBoundary ? cellsA[cellsA.Count - 1].y : cellsA[cellsA.Count - 1].x;
        int boundary = isVerticalBoundary ? cellsA[0].x : cellsA[0].y;
        return new PortalSignature(sectorAId, sectorBId, isVerticalBoundary, boundary, firstAxis, lastAxis, cellsA.Count);
    }

    private static PortalSignature BuildPortalSignature(int sectorAId, int sectorBId, Vector2Int[] cellsA, bool isVerticalBoundary)
    {
        if (cellsA == null || cellsA.Length == 0)
            throw new InvalidOperationException("BuildPortalSignature failed: cells are empty.");

        int firstAxis = isVerticalBoundary ? cellsA[0].y : cellsA[0].x;
        int lastAxis = isVerticalBoundary ? cellsA[cellsA.Length - 1].y : cellsA[cellsA.Length - 1].x;
        int boundary = isVerticalBoundary ? cellsA[0].x : cellsA[0].y;
        return new PortalSignature(sectorAId, sectorBId, isVerticalBoundary, boundary, firstAxis, lastAxis, cellsA.Length);
    }

    private static int ResolveStablePortalId(NavigationWorld world, PortalSignature signature)
    {
        if (world.PortalIdsBySignature.TryGetValue(signature, out int existingId))
        {
            world.UsedPortalIds.Add(existingId);
            return existingId;
        }

        int id = world.NextPortalId;
        while (world.UsedPortalIds.Contains(id))
        {
            id++;
            if (id > ushort.MaxValue)
                throw new InvalidOperationException("ResolveStablePortalId failed: portal id range exhausted.");
        }

        world.NextPortalId = id + 1;
        world.PortalIdsBySignature.Add(signature, id);
        world.UsedPortalIds.Add(id);
        return id;
    }

    private static void RemoveStalePortalSignatures(NavigationWorld world)
    {
        List<PortalSignature> staleSignatures = null;
        foreach (KeyValuePair<PortalSignature, int> pair in world.PortalIdsBySignature)
        {
            if (world.UsedPortalIds.Contains(pair.Value))
                continue;

            staleSignatures ??= new List<PortalSignature>();
            staleSignatures.Add(pair.Key);
        }

        if (staleSignatures == null)
            return;

        for (int i = 0; i < staleSignatures.Count; i++)
            world.PortalIdsBySignature.Remove(staleSignatures[i]);
    }

    private static bool IsPortalBottleneck(
        NavigationWorld world,
        int sectorAId,
        int sectorBId,
        List<Vector2Int> cellsA,
        List<Vector2Int> cellsB,
        bool isVerticalBoundary)
    {
        if (cellsA == null || cellsB == null || cellsA.Count == 0 || cellsB.Count == 0)
            throw new InvalidOperationException("IsPortalBottleneck failed: portal cells are empty.");

        return cellsA.Count <= ResolvePortalNarrowWidthCells(world);
    }

    private static Vector2Int[] GetPortalCellsForSector(PortalData portal, int sectorId)
    {
        if (portal.SectorAId == sectorId)
            return portal.CellsA;
        if (portal.SectorBId == sectorId)
            return portal.CellsB;

        throw new InvalidOperationException($"GetPortalCellsForSector failed: sector {sectorId} is not connected to portal {portal.PortalId}.");
    }

    private static int GetOppositeSectorId(PortalData portal, int sectorId)
    {
        if (portal.SectorAId == sectorId)
            return portal.SectorBId;
        if (portal.SectorBId == sectorId)
            return portal.SectorAId;

        throw new InvalidOperationException($"GetOppositeSectorId failed: sector {sectorId} is not connected to portal {portal.PortalId}.");
    }

}
