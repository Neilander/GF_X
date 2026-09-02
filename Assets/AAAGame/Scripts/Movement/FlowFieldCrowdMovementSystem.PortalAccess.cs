using System;
using System.Collections.Generic;
using System.Text;
using AAAGame.FlowPath;
using UnityEngine;

public static partial class FlowFieldCrowdMovementSystem
{
    private static long ResolveDeterministicPortalAccessEikonalIntegrationCost(
        NavigationWorld world,
        SectorData sector,
        long[] integration,
        int worldX,
        int worldY)
    {
        long horizontal = long.MaxValue;
        long vertical = long.MaxValue;
        for (int i = 0; i < CardinalOffsetX.Length; i++)
        {
            int neighborX = worldX + CardinalOffsetX[i];
            int neighborY = worldY + CardinalOffsetY[i];
            if (!IsInsideSector(sector, neighborX, neighborY)
                || !world.IsWalkable(neighborX, neighborY)
                || !CanTraverseNeighborCells(world, worldX, worldY, neighborX, neighborY))
            {
                continue;
            }

            long neighborCost = integration[GetSectorLocalIndex(sector, neighborX, neighborY)];
            if (neighborCost == long.MaxValue)
                continue;
            if (neighborX != worldX)
                horizontal = Math.Min(horizontal, neighborCost);
            else
                vertical = Math.Min(vertical, neighborCost);
        }

        int cellCost = GetCostFieldValueStrict(world, worldX, worldY);
        if (cellCost >= byte.MaxValue)
            return long.MaxValue;

        long stepCost = checked((long)Math.Max(1, cellCost) * DeterministicPortalCostScale);
        return ResolveDeterministicEikonalUpdate(
            horizontal,
            vertical,
            stepCost,
            long.MaxValue);
    }

    private static long ResolveDeterministicAnalyticPortalAccessCost(
        NavigationWorld world,
        SectorData sector,
        int portalId,
        int worldX,
        int worldY)
    {
        if (world == null)
            throw new InvalidOperationException("ResolveDeterministicAnalyticPortalAccessCost failed: world is null.");
        if (sector == null)
            throw new InvalidOperationException("ResolveDeterministicAnalyticPortalAccessCost failed: sector is null.");
        if (!IsInsideSector(sector, worldX, worldY) || !world.IsWalkable(worldX, worldY))
            return long.MaxValue;
        if (!TryGetPortalById(world, portalId, out PortalData portal))
            throw new InvalidOperationException($"ResolveDeterministicAnalyticPortalAccessCost failed: portal missing sector={sector.SectorId} portal={portalId}.");

        long best = long.MaxValue;
        Vector2Int[] portalCells = GetPortalCellsForSector(portal, sector.SectorId);
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
            if (cost < best)
                best = cost;
        }

        return best;
    }

    private static long ResolveDeterministicPortalTransitionCostFromAccess(
        NavigationWorld world,
        SectorData sector,
        int fromPortalId,
        int toPortalId)
    {
        SectorPortalAccessKey key = new SectorPortalAccessKey(world.Version, sector.SectorId, fromPortalId, sector.DirtyVersion);
        if (!SectorPortalAccessCache.TryGetValue(key, out SectorPortalAccessEntry entry) || !IsValidSectorPortalAccessEntry(entry))
            throw new InvalidOperationException($"ResolveDeterministicPortalTransitionCostFromAccess failed: access field missing sector={sector.SectorId} portal={fromPortalId}.");

        return ResolveDeterministicPortalTransitionCostFromAccess(world, sector, entry, toPortalId);
    }

    private static long ResolveDeterministicPortalTransitionCostFromAccess(
        NavigationWorld world,
        SectorData sector,
        SectorPortalAccessEntry entry,
        int toPortalId)
    {
        if (entry == null)
            throw new InvalidOperationException("ResolveDeterministicPortalTransitionCostFromAccess failed: access entry is null.");

        PortalData targetPortal = GetPortalById(world, toPortalId);
        Vector2Int[] targetCells = GetPortalCellsForSector(targetPortal, sector.SectorId);
        long best = long.MaxValue;
        for (int i = 0; i < targetCells.Length; i++)
        {
            Vector2Int cell = targetCells[i];
            long accessCost = ResolveDeterministicPortalAccessIntegrationCost(world, sector, entry, cell.x, cell.y);
            long totalCost = AddDeterministicPortalCosts(
                accessCost,
                ResolveDeterministicPortalCenterSeedCost(targetPortal, cell));
            if (totalCost < best)
                best = totalCost;
        }

        return best;
    }

    private static void RebuildDeterministicPortalTransitionCosts(NavigationWorld world)
    {
        if (world?.Sectors == null)
            throw new InvalidOperationException("RebuildDeterministicPortalTransitionCosts failed: world is unavailable.");

        for (int sectorIndex = 0; sectorIndex < world.Sectors.Length; sectorIndex++)
        {
            SectorData sector = world.Sectors[sectorIndex];
            for (int portalIndex = 0; portalIndex < sector.PortalIds.Count; portalIndex++)
            {
                int portalId = sector.PortalIds[portalIndex];
                SectorPortalAccessKey key = new SectorPortalAccessKey(world.Version, sector.SectorId, portalId, sector.DirtyVersion);
                if (!SectorPortalAccessCache.TryGetValue(key, out SectorPortalAccessEntry entry)
                    || entry == null
                    || entry.SectorId != sector.SectorId
                    || entry.PortalId != portalId
                    || entry.SectorDirtyVersion != sector.DirtyVersion)
                {
                    throw new InvalidOperationException($"RebuildDeterministicPortalTransitionCosts failed: access entry missing sector={sector.SectorId} portal={portalId}.");
                }

                PortalData portal = GetPortalById(world, portalId);
                entry.DeterministicIntegration = entry.IsAnalyticClearSector
                    ? null
                    : BuildDeterministicPortalAccessIntegration(world, sector, portal);
                entry.DeterministicPortalTargetSlotIndices = BuildDeterministicPortalTargetSlotIndices(
                    world,
                    sector,
                    portal,
                    entry.DeterministicIntegration);
            }
        }

        for (int sectorIndex = 0; sectorIndex < world.Sectors.Length; sectorIndex++)
        {
            SectorData sector = world.Sectors[sectorIndex];
            for (int transitionIndex = 0; transitionIndex < sector.PortalTransitions.Count; transitionIndex++)
            {
                PortalTransition transition = sector.PortalTransitions[transitionIndex];
                transition.DeterministicCost = ResolveDeterministicPortalTransitionCostFromAccess(
                    world,
                    sector,
                    transition.FromPortalId,
                    transition.ToPortalId);
                if (transition.DeterministicCost == long.MaxValue)
                {
                    throw new InvalidOperationException(
                        $"RebuildDeterministicPortalTransitionCosts failed: transition is unreachable sector={sector.SectorId} from={transition.FromPortalId} to={transition.ToPortalId}.");
                }

            }
        }

        RefreshSectorPortalAccessAuthorityContentHashes();
        PortalAccessIntegrationArrayPool.Clear();
    }

    private static void RebuildDeterministicPortalTransitionCosts(
        NavigationWorld world,
        List<int> sectorIds)
    {
        if (world?.Sectors == null)
            throw new InvalidOperationException("RebuildDeterministicPortalTransitionCosts failed: world is unavailable.");
        if (sectorIds == null)
            throw new InvalidOperationException("RebuildDeterministicPortalTransitionCosts failed: sector ids are null.");

        int previousSectorId = -1;
        for (int sectorIndex = 0; sectorIndex < sectorIds.Count; sectorIndex++)
        {
            int sectorId = sectorIds[sectorIndex];
            if (sectorId <= previousSectorId || sectorId < 0 || sectorId >= world.Sectors.Length)
            {
                throw new InvalidOperationException(
                    $"RebuildDeterministicPortalTransitionCosts failed: sector ids must be sorted, unique and in range previous={previousSectorId} current={sectorId}.");
            }
            previousSectorId = sectorId;

            SectorData sector = world.Sectors[sectorId];
            for (int portalIndex = 0; portalIndex < sector.PortalIds.Count; portalIndex++)
            {
                int portalId = sector.PortalIds[portalIndex];
                var key = new SectorPortalAccessKey(world.Version, sector.SectorId, portalId, sector.DirtyVersion);
                if (!SectorPortalAccessCache.TryGetValue(key, out SectorPortalAccessEntry entry)
                    || entry == null
                    || entry.SectorId != sector.SectorId
                    || entry.PortalId != portalId
                    || entry.SectorDirtyVersion != sector.DirtyVersion)
                {
                    throw new InvalidOperationException(
                        $"RebuildDeterministicPortalTransitionCosts failed: access entry missing sector={sector.SectorId} portal={portalId}.");
                }

                ReturnPortalAccessIntegrationArray(entry.DeterministicIntegration);
                PortalData portal = GetPortalById(world, portalId);
                entry.DeterministicIntegration = entry.IsAnalyticClearSector
                    ? null
                    : BuildDeterministicPortalAccessIntegration(world, sector, portal);
                entry.DeterministicPortalTargetSlotIndices = BuildDeterministicPortalTargetSlotIndices(
                    world,
                    sector,
                    portal,
                    entry.DeterministicIntegration);
                RefreshSectorPortalAccessAuthorityContentHash(key, entry);
            }
        }

        for (int sectorIndex = 0; sectorIndex < sectorIds.Count; sectorIndex++)
        {
            SectorData sector = world.Sectors[sectorIds[sectorIndex]];
            for (int transitionIndex = 0; transitionIndex < sector.PortalTransitions.Count; transitionIndex++)
            {
                PortalTransition transition = sector.PortalTransitions[transitionIndex];
                transition.DeterministicCost = ResolveDeterministicPortalTransitionCostFromAccess(
                    world,
                    sector,
                    transition.FromPortalId,
                    transition.ToPortalId);
                if (transition.DeterministicCost == long.MaxValue)
                {
                    throw new InvalidOperationException(
                        $"RebuildDeterministicPortalTransitionCosts failed: transition is unreachable sector={sector.SectorId} from={transition.FromPortalId} to={transition.ToPortalId}.");
                }
            }
        }

        PortalAccessIntegrationArrayPool.Clear();
    }

    private static SectorPortalAccessEntry GetPrebuiltSectorPortalAccess(SectorData sector, int sectorId, int portalId)
    {
        return GetPrebuiltSectorPortalAccess(_world, sector, sectorId, portalId);
    }

    private static SectorPortalAccessEntry GetPrebuiltSectorPortalAccess(
        NavigationWorld world,
        SectorData sector,
        int sectorId,
        int portalId)
    {
        return GetRequiredSectorPortalAccess(world, sector, sectorId, portalId);
    }

    private static SectorPortalAccessEntry GetRequiredSectorPortalAccess(SectorData sector, int sectorId, int portalId)
    {
        return GetRequiredSectorPortalAccess(_world, sector, sectorId, portalId);
    }

    private static SectorPortalAccessEntry GetRequiredSectorPortalAccess(
        NavigationWorld world,
        SectorData sector,
        int sectorId,
        int portalId)
    {
        if (world == null)
            throw new InvalidOperationException("GetPrebuiltSectorPortalAccess failed: world is null.");
        SectorPortalAccessKey key = new SectorPortalAccessKey(
            world.Version,
            sectorId,
            portalId,
            sector.DirtyVersion);
        if (SectorPortalAccessCache.TryGetValue(key, out SectorPortalAccessEntry cached)
            && IsValidSectorPortalAccessEntry(cached))
        {
            cached.LastUsedFrame = GetFrameCount();
            return cached;
        }

        throw new InvalidOperationException(
            $"GetPrebuiltSectorPortalAccess failed: missing prebuilt portal access field world={world.Version} sector={sectorId} portal={portalId} dirty={sector.DirtyVersion}. " +
            $"{BuildMissingPortalAccessDiagnostics(world, sectorId, portalId, sector.DirtyVersion)} " +
            "Portal access integration must be produced by world/runtime portal transition jobs, not synchronously in query/tile build.");
    }

    private static PendingSectorPortalAccess AddPendingAnalyticSectorPortalAccess(
        NavigationWorld world,
        List<PendingSectorPortalAccess> pendingEntries,
        SectorData sector,
        int portalId)
    {
        if (world == null)
            throw new InvalidOperationException("AddPendingAnalyticSectorPortalAccess failed: world is null.");
        if (pendingEntries == null)
            throw new InvalidOperationException("AddPendingAnalyticSectorPortalAccess failed: pendingEntries is null.");
        if (sector == null)
            throw new InvalidOperationException("AddPendingAnalyticSectorPortalAccess failed: sector is null.");

        PortalData portal = GetPortalById(world, portalId);
        var pending = new PendingSectorPortalAccess(
            sector.SectorId,
            portalId,
            sector.DirtyVersion,
            true,
            null,
            BuildDeterministicPortalTargetSlotIndices(world, sector, portal, null));
        pendingEntries.Add(pending);
        return pending;
    }

    private static PendingSectorPortalAccess AddPendingPrebuiltDeterministicSectorPortalAccess(
        NavigationWorld world,
        List<PendingSectorPortalAccess> pendingEntries,
        SectorData sector,
        int portalId)
    {
        if (world == null)
            throw new InvalidOperationException("AddPendingPrebuiltDeterministicSectorPortalAccess failed: world is null.");
        if (pendingEntries == null)
            throw new InvalidOperationException("AddPendingPrebuiltDeterministicSectorPortalAccess failed: pendingEntries is null.");
        if (sector == null)
            throw new InvalidOperationException("AddPendingPrebuiltDeterministicSectorPortalAccess failed: sector is null.");

        PortalData portal = GetPortalById(world, portalId);
        long[] integration = BuildDeterministicPortalAccessIntegration(world, sector, portal);
        int[] targetSlots = BuildDeterministicPortalTargetSlotIndices(world, sector, portal, integration);
        var pending = new PendingSectorPortalAccess(
            sector.SectorId,
            portalId,
            sector.DirtyVersion,
            false,
            integration,
            targetSlots);
        pendingEntries.Add(pending);
        return pending;
    }

    private static void ApplyPendingPortalTransitionCosts(
        NavigationWorld world,
        SectorData sector,
        PendingSectorPortalAccess pending)
    {
        if (pending == null)
            throw new InvalidOperationException("ApplyPendingPortalTransitionCosts failed: pending access is null.");
        if (pending.SectorId != sector.SectorId || pending.SectorDirtyVersion != sector.DirtyVersion)
        {
            throw new InvalidOperationException(
                $"ApplyPendingPortalTransitionCosts failed: pending access identity mismatch sector={sector.SectorId} portal={pending.PortalId}.");
        }

        var entry = new SectorPortalAccessEntry
        {
            DeterministicIntegration = pending.DeterministicIntegration,
            DeterministicPortalTargetSlotIndices = pending.DeterministicPortalTargetSlotIndices,
            IsAnalyticClearSector = pending.IsAnalyticClearSector,
            SectorId = pending.SectorId,
            PortalId = pending.PortalId,
            SectorDirtyVersion = pending.SectorDirtyVersion
        };
        for (int i = 0; i < sector.PortalTransitions.Count; i++)
        {
            PortalTransition transition = sector.PortalTransitions[i];
            if (transition.FromPortalId != pending.PortalId)
                continue;

            transition.DeterministicCost = ResolveDeterministicPortalTransitionCostFromAccess(
                world,
                sector,
                entry,
                transition.ToPortalId);
            if (transition.DeterministicCost == long.MaxValue)
            {
                throw new InvalidOperationException(
                    $"ApplyPendingPortalTransitionCosts failed: transition is unreachable sector={sector.SectorId} from={pending.PortalId} to={transition.ToPortalId}.");
            }
        }
    }

    private static void CommitPendingSectorPortalAccessEntries(
        NavigationWorld world,
        List<PendingSectorPortalAccess> pendingEntries,
        bool entriesAreFinalized)
    {
        if (pendingEntries == null || pendingEntries.Count == 0)
            return;
        if (world == null)
            throw new InvalidOperationException("CommitPendingSectorPortalAccessEntries failed: world is null.");

        for (int i = 0; i < pendingEntries.Count; i++)
        {
            PendingSectorPortalAccess pending = pendingEntries[i]
                ?? throw new InvalidOperationException($"CommitPendingSectorPortalAccessEntries failed: pending entry is null index={i}.");
            SectorPortalAccessKey key = new SectorPortalAccessKey(
                world.Version,
                pending.SectorId,
                pending.PortalId,
                pending.SectorDirtyVersion);
            var entry = new SectorPortalAccessEntry
            {
                DeterministicIntegration = pending.DeterministicIntegration,
                DeterministicPortalTargetSlotIndices = pending.DeterministicPortalTargetSlotIndices,
                IsAnalyticClearSector = pending.IsAnalyticClearSector,
                SectorId = pending.SectorId,
                PortalId = pending.PortalId,
                SectorDirtyVersion = pending.SectorDirtyVersion,
                LastUsedFrame = GetFrameCount()
            };
            SetSectorPortalAccessCacheEntry(key, entry);
            if (entriesAreFinalized)
                RefreshSectorPortalAccessAuthorityContentHash(key, entry);
            pending.DeterministicIntegration = null;
            pending.DeterministicPortalTargetSlotIndices = null;
        }

        TrimSectorPortalAccessCache();
    }

    private static void ReturnPendingSectorPortalAccessIntegrations(List<PendingSectorPortalAccess> pendingEntries)
    {
        if (pendingEntries == null)
            return;

        for (int i = 0; i < pendingEntries.Count; i++)
        {
            PendingSectorPortalAccess pending = pendingEntries[i];
            if (pending == null || pending.DeterministicIntegration == null)
                continue;
            ReturnPortalAccessIntegrationArray(pending.DeterministicIntegration);
            pending.DeterministicIntegration = null;
        }
    }

    private static void ValidatePendingSectorPortalAccessCoverage(
        NavigationWorld world,
        List<PendingSectorPortalAccess> pendingEntries,
        string stage)
    {
        if (world?.Sectors == null)
            throw new InvalidOperationException($"ValidatePendingSectorPortalAccessCoverage failed: world is unavailable stage={stage}.");
        if (pendingEntries == null)
            throw new InvalidOperationException($"ValidatePendingSectorPortalAccessCoverage failed: pending entries are null stage={stage}.");

        var pendingByKey = new Dictionary<SectorPortalAccessKey, PendingSectorPortalAccess>(pendingEntries.Count);
        for (int i = 0; i < pendingEntries.Count; i++)
        {
            PendingSectorPortalAccess pending = pendingEntries[i]
                ?? throw new InvalidOperationException($"ValidatePendingSectorPortalAccessCoverage failed: null entry stage={stage} index={i}.");
            if (!pending.IsAnalyticClearSector && pending.DeterministicIntegration == null)
            {
                throw new InvalidOperationException(
                    $"ValidatePendingSectorPortalAccessCoverage failed: deterministic integration is missing stage={stage} sector={pending.SectorId} portal={pending.PortalId}.");
            }
            if (pending.DeterministicPortalTargetSlotIndices == null)
            {
                throw new InvalidOperationException(
                    $"ValidatePendingSectorPortalAccessCoverage failed: portal target slot field is missing stage={stage} sector={pending.SectorId} portal={pending.PortalId}.");
            }

            var key = new SectorPortalAccessKey(world.Version, pending.SectorId, pending.PortalId, pending.SectorDirtyVersion);
            if (!pendingByKey.TryAdd(key, pending))
                throw new InvalidOperationException($"ValidatePendingSectorPortalAccessCoverage failed: duplicate entry stage={stage} sector={pending.SectorId} portal={pending.PortalId}.");
        }

        for (int sectorId = 0; sectorId < world.Sectors.Length; sectorId++)
        {
            SectorData sector = world.Sectors[sectorId]
                ?? throw new InvalidOperationException($"ValidatePendingSectorPortalAccessCoverage failed: null sector stage={stage} sector={sectorId}.");
            for (int i = 0; i < sector.PortalIds.Count; i++)
            {
                int portalId = sector.PortalIds[i];
                var key = new SectorPortalAccessKey(world.Version, sectorId, portalId, sector.DirtyVersion);
                if (pendingByKey.ContainsKey(key))
                    continue;
                if (SectorPortalAccessCache.TryGetValue(key, out SectorPortalAccessEntry cached)
                    && IsValidSectorPortalAccessEntry(cached))
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"ValidatePendingSectorPortalAccessCoverage failed: future committed coverage is missing stage={stage} sector={sectorId} portal={portalId} dirty={sector.DirtyVersion}.");
            }
        }
    }

    private static void EnsureAllSectorPortalAccessCoverage(NavigationWorld world, string stage)
    {
        if (world == null)
            throw new InvalidOperationException("EnsureAllSectorPortalAccessCoverage failed: world is null.");
        if (world.Sectors == null)
            throw new InvalidOperationException($"EnsureAllSectorPortalAccessCoverage failed: sectors are null stage={stage}.");

        for (int sectorId = 0; sectorId < world.Sectors.Length; sectorId++)
        {
            SectorData sector = world.Sectors[sectorId];
            if (sector == null)
                throw new InvalidOperationException($"EnsureAllSectorPortalAccessCoverage failed: sector is null stage={stage} sector={sectorId}.");

            for (int i = 0; i < sector.PortalIds.Count; i++)
            {
                int portalId = sector.PortalIds[i];
                SectorPortalAccessKey key = new SectorPortalAccessKey(world.Version, sectorId, portalId, sector.DirtyVersion);
                if (SectorPortalAccessCache.TryGetValue(key, out SectorPortalAccessEntry existing)
                    && IsValidSectorPortalAccessEntry(existing))
                {
                    continue;
                }

                SetSectorPortalAccessCacheEntry(key, BuildSectorPortalAccessForCommit(world, sector, sectorId, portalId));
            }
        }
    }

    private static SectorPortalAccessEntry BuildSectorPortalAccessForCommit(NavigationWorld world, SectorData sector, int sectorId, int portalId)
    {
        if (world == null)
            throw new InvalidOperationException("BuildSectorPortalAccessForCommit failed: world is null.");
        if (sector == null)
            throw new InvalidOperationException("BuildSectorPortalAccessForCommit failed: sector is null.");
        if (sector.SectorId != sectorId)
            throw new InvalidOperationException($"BuildSectorPortalAccessForCommit failed: sector id mismatch expected={sectorId} actual={sector.SectorId}.");
        if (!TryGetPortalById(world, portalId, out PortalData portal))
            throw new InvalidOperationException($"BuildSectorPortalAccessForCommit failed: portal missing world={world.Version} sector={sectorId} portal={portalId}.");

        return new SectorPortalAccessEntry
        {
            IsAnalyticClearSector = CanUseAnalyticPortalAccess(world, sector, portalId),
            DeterministicPortalTargetSlotIndices = BuildDeterministicPortalTargetSlotIndices(
                world,
                sector,
                portal,
                null),
            SectorId = sectorId,
            PortalId = portalId,
            SectorDirtyVersion = sector.DirtyVersion,
            LastUsedFrame = GetFrameCount()
        };
    }

    private static bool IsValidSectorPortalAccessEntry(SectorPortalAccessEntry entry)
    {
        if (entry == null)
            return false;
        if (entry.IsAnalyticClearSector)
            return entry.SectorId >= 0
                   && entry.PortalId >= 0
                   && entry.DeterministicPortalTargetSlotIndices != null;

        return entry.DeterministicIntegration != null
               && entry.DeterministicPortalTargetSlotIndices != null;
    }

    private static void ValidateSectorPortalAccessCoverage(NavigationWorld world, HashSet<int> sectorIds, string stage)
    {
        if (world == null)
            throw new InvalidOperationException("ValidateSectorPortalAccessCoverage failed: world is null.");
        if (sectorIds == null || sectorIds.Count == 0)
            return;

        foreach (int sectorId in sectorIds)
        {
            if (sectorId < 0 || sectorId >= world.Sectors.Length)
                throw new InvalidOperationException($"ValidateSectorPortalAccessCoverage failed: sector out of range sector={sectorId} stage={stage}.");

            SectorData sector = world.Sectors[sectorId];
            for (int i = 0; i < sector.PortalIds.Count; i++)
            {
                int portalId = sector.PortalIds[i];
                SectorPortalAccessKey key = new SectorPortalAccessKey(world.Version, sectorId, portalId, sector.DirtyVersion);
                if (!SectorPortalAccessCache.TryGetValue(key, out SectorPortalAccessEntry entry) || !IsValidSectorPortalAccessEntry(entry))
                {
                    throw new InvalidOperationException(
                        $"ValidateSectorPortalAccessCoverage failed: missing portal access stage={stage} world={world.Version} sector={sectorId} portal={portalId} dirty={sector.DirtyVersion}.");
                }
            }
        }
    }

    private static void ValidateAllSectorPortalAccessCoverage(NavigationWorld world, string stage)
    {
        if (world == null)
            throw new InvalidOperationException("ValidateAllSectorPortalAccessCoverage failed: world is null.");
        if (world.Sectors == null)
            throw new InvalidOperationException($"ValidateAllSectorPortalAccessCoverage failed: sectors are null stage={stage}.");

        for (int sectorId = 0; sectorId < world.Sectors.Length; sectorId++)
        {
            SectorData sector = world.Sectors[sectorId];
            if (sector == null)
                throw new InvalidOperationException($"ValidateAllSectorPortalAccessCoverage failed: sector is null stage={stage} sector={sectorId}.");

            for (int i = 0; i < sector.PortalIds.Count; i++)
            {
                int portalId = sector.PortalIds[i];
                SectorPortalAccessKey key = new SectorPortalAccessKey(world.Version, sectorId, portalId, sector.DirtyVersion);
                if (!SectorPortalAccessCache.TryGetValue(key, out SectorPortalAccessEntry entry) || !IsValidSectorPortalAccessEntry(entry))
                {
                    throw new InvalidOperationException(
                        $"ValidateAllSectorPortalAccessCoverage failed: missing portal access stage={stage} world={world.Version} sector={sectorId} portal={portalId} dirty={sector.DirtyVersion}. " +
                        BuildMissingPortalAccessDiagnostics(world, sectorId, portalId, sector.DirtyVersion));
                }
            }
        }
    }

    private static void RemoveSectorPortalAccessCacheEntriesForWorldVersion(int worldVersion)
    {
        if (worldVersion == 0 || SectorPortalAccessCache.Count == 0)
            return;

        List<SectorPortalAccessKey> keysToRemove = null;
        foreach (SectorPortalAccessKey key in SectorPortalAccessCache.Keys)
        {
            if (key.WorldVersion != worldVersion)
                continue;

            keysToRemove ??= new List<SectorPortalAccessKey>();
            keysToRemove.Add(key);
        }

        if (keysToRemove == null)
            return;

        for (int i = 0; i < keysToRemove.Count; i++)
            RemoveSectorPortalAccessCacheEntry(keysToRemove[i]);
    }

    private static int AllocateNavigationWorldVersion()
    {
        if (_nextWorldVersion <= 0 || _nextWorldVersion == int.MaxValue)
            throw new InvalidOperationException($"Navigation world version space is exhausted. next={_nextWorldVersion}.");
        return _nextWorldVersion++;
    }

#if UNITY_EDITOR
    private static int AllocateEditorBakeNavigationWorldVersion()
    {
        if (_nextEditorBakeWorldVersion >= 0 || _nextEditorBakeWorldVersion == int.MinValue)
            throw new InvalidOperationException($"Editor bake navigation world version space is exhausted. next={_nextEditorBakeWorldVersion}.");
        return _nextEditorBakeWorldVersion--;
    }
#endif

    private static string BuildMissingPortalAccessDiagnostics(NavigationWorld world, int sectorId, int portalId, int dirtyVersion)
    {
        if (world == null)
            return "diag=world-null";

        int sameWorldSectorEntries = 0;
        int sameWorldEntries = 0;
        int samePortalAnyWorldEntries = 0;
        string sampleSameWorldSector = "none";
        string sampleSamePortal = "none";
        foreach (SectorPortalAccessKey existingKey in SectorPortalAccessCache.Keys)
        {
            if (existingKey.WorldVersion == world.Version)
            {
                sameWorldEntries++;
                if (existingKey.SectorId == sectorId)
                {
                    sameWorldSectorEntries++;
                    if (sampleSameWorldSector == "none")
                        sampleSameWorldSector = $"world={existingKey.WorldVersion},sector={existingKey.SectorId},portal={existingKey.PortalId},dirty={existingKey.SectorDirtyVersion}";
                }
            }

            if (existingKey.PortalId == portalId)
            {
                samePortalAnyWorldEntries++;
                if (sampleSamePortal == "none")
                    sampleSamePortal = $"world={existingKey.WorldVersion},sector={existingKey.SectorId},portal={existingKey.PortalId},dirty={existingKey.SectorDirtyVersion}";
            }
        }

        string sectorInfo = "sector=out-of-range";
        if (world.Sectors != null && sectorId >= 0 && sectorId < world.Sectors.Length)
        {
            SectorData sector = world.Sectors[sectorId];
            sectorInfo = sector == null
                ? "sector=null"
                : $"sectorPortalCount={sector.PortalIds.Count} sectorTransitionCount={sector.PortalTransitions.Count} sectorDirty={sector.DirtyVersion} sectorBounds=({sector.StartX},{sector.StartY},{sector.Width},{sector.Height}) sectorPortals=[{string.Join(",", sector.PortalIds)}]";
        }

        string portalInfo = "portal=missing";
        if (TryGetPortalById(world, portalId, out PortalData portal))
        {
            portalInfo = $"portalSectors=({portal.SectorAId},{portal.SectorBId}) portalWidth={portal.WidthCells} portalNarrow={portal.IsNarrow} portalVertical={portal.IsVerticalBoundary} cellsA={FormatPortalCellsForDiagnostics(portal.CellsA, 4)} cellsB={FormatPortalCellsForDiagnostics(portal.CellsB, 4)}";
        }

        string states = BuildWorldStateDiagnosticsForPortalAccess();
        return $"diag=cacheCount={SectorPortalAccessCache.Count} sameWorldEntries={sameWorldEntries} sameWorldSectorEntries={sameWorldSectorEntries} samePortalAnyWorldEntries={samePortalAnyWorldEntries} sampleSameWorldSector={sampleSameWorldSector} sampleSamePortal={sampleSamePortal} worldAgentType={world.AgentTypeId} worldDirtyExpected={dirtyVersion} sectors={(world.Sectors != null ? world.Sectors.Length : -1)} portals={(world.Portals != null ? world.Portals.Length : -1)} {sectorInfo} {portalInfo} {states}";
    }

    private static string BuildPortalAccessFieldDiagnostics(
        NavigationWorld world,
        SectorData sector,
        SectorPortalAccessEntry access,
        int worldX,
        int worldY)
    {
        if (world == null)
            return "accessDiag=world-null";
        if (sector == null)
            return "accessDiag=sector-null";
        if (access == null)
            return "accessDiag=entry-null";

        int currentIndex = IsInsideSector(sector, worldX, worldY)
            ? GetSectorLocalIndex(sector, worldX, worldY)
            : -1;
        int globalIndex = worldX >= 0 && worldX < world.Width && worldY >= 0 && worldY < world.Height
            ? world.GetIndex(worldX, worldY)
            : -1;
        int currentIsland = globalIndex >= 0 && world.IslandIds != null && globalIndex < world.IslandIds.Length
            ? world.IslandIds[globalIndex]
            : -1;
        int currentComponent = currentIndex >= 0 && sector.LocalComponentIds != null && currentIndex < sector.LocalComponentIds.Length
            ? sector.LocalComponentIds[currentIndex]
            : -1;
        int mappedIsland = currentComponent > 0
                           && sector.LocalComponentIslandIds != null
                           && currentComponent < sector.LocalComponentIslandIds.Length
            ? sector.LocalComponentIslandIds[currentComponent]
            : -1;

        long currentIntegration = access.IsAnalyticClearSector
            ? ResolveDeterministicAnalyticPortalAccessCost(world, sector, access.PortalId, worldX, worldY)
            : currentIndex >= 0
              && access.DeterministicIntegration != null
              && currentIndex < access.DeterministicIntegration.Length
                ? access.DeterministicIntegration[currentIndex]
                : long.MaxValue;
        int reachableCount = 0;
        long minimum = long.MaxValue;
        long maximum = 0;
        if (!access.IsAnalyticClearSector && access.DeterministicIntegration != null)
        {
            for (int i = 0; i < access.DeterministicIntegration.Length; i++)
            {
                long value = access.DeterministicIntegration[i];
                if (value == long.MaxValue)
                    continue;
                reachableCount++;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }

        string portalCells = "portal=missing";
        if (TryGetPortalById(world, access.PortalId, out PortalData portal))
        {
            Vector2Int[] cells = GetPortalCellsForSector(portal, sector.SectorId);
            var cellsBuilder = new StringBuilder(160);
            cellsBuilder.Append('[');
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0)
                    cellsBuilder.Append(';');
                Vector2Int cell = cells[i];
                int index = IsInsideSector(sector, cell.x, cell.y) ? GetSectorLocalIndex(sector, cell.x, cell.y) : -1;
                int island = cell.x >= 0 && cell.x < world.Width && cell.y >= 0 && cell.y < world.Height
                    ? world.IslandIds[world.GetIndex(cell.x, cell.y)]
                    : -1;
                int component = index >= 0 && sector.LocalComponentIds != null && index < sector.LocalComponentIds.Length
                    ? sector.LocalComponentIds[index]
                    : -1;
                long cost = index >= 0 && !access.IsAnalyticClearSector && access.DeterministicIntegration != null && index < access.DeterministicIntegration.Length
                    ? access.DeterministicIntegration[index]
                    : access.IsAnalyticClearSector
                      ? ResolveDeterministicAnalyticPortalAccessCost(world, sector, access.PortalId, cell.x, cell.y)
                      : long.MaxValue;
                if (i > 7)
                {
                    cellsBuilder.Append(";...").Append(cells.Length);
                    break;
                }
                cellsBuilder.Append('(').Append(cell.x).Append(',').Append(cell.y)
                    .Append(",walk=").Append(world.IsWalkable(cell.x, cell.y))
                    .Append(",island=").Append(island)
                    .Append(",component=").Append(component)
                    .Append(",cost=").Append(cost).Append(')');
            }
            cellsBuilder.Append(']');
            portalCells = $"portalSectors=({portal.SectorAId},{portal.SectorBId}) touches={PortalTouchesSector(portal, sector.SectorId)} portalCells={cellsBuilder}";
        }

        SectorPortalAccessKey key = new SectorPortalAccessKey(world.Version, sector.SectorId, access.PortalId, sector.DirtyVersion);
        bool cacheKeyPresent = SectorPortalAccessCache.ContainsKey(key);
        return $"accessDiag=world={world.Version},sector={sector.SectorId},sectorDirty={sector.DirtyVersion},entry=({access.SectorId},{access.PortalId},{access.SectorDirtyVersion},analytic={access.IsAnalyticClearSector})," +
               $"cacheKey={cacheKeyPresent},cell=({worldX},{worldY}),walk={globalIndex >= 0 && world.IsWalkable(worldX, worldY)},cost={(globalIndex >= 0 ? GetCostFieldValueStrict(world, worldX, worldY) : -1)}," +
               $"island={currentIsland},component={currentComponent},mappedIsland={mappedIsland},currentIntegration={currentIntegration}," +
               $"integrationLength={access.DeterministicIntegration?.Length ?? -1},reachable={reachableCount},min={minimum},max={maximum}," +
               $"sectorBounds=({sector.StartX},{sector.StartY},{sector.Width},{sector.Height}),sectorPortalIds=[{string.Join(",", sector.PortalIds)}] {portalCells}";
    }

    private static string FormatPortalCellsForDiagnostics(Vector2Int[] cells, int maxCells)
    {
        if (cells == null)
            return "null";
        if (cells.Length == 0)
            return "empty";

        int count = Mathf.Min(cells.Length, Mathf.Max(1, maxCells));
        System.Text.StringBuilder builder = new System.Text.StringBuilder(96);
        builder.Append('[');
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                builder.Append(';');

            builder.Append(cells[i].x);
            builder.Append(',');
            builder.Append(cells[i].y);
        }

        if (cells.Length > count)
        {
            builder.Append(";...");
            builder.Append(cells.Length);
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string BuildWorldStateDiagnosticsForPortalAccess()
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(256);
        builder.Append("worldStates=[");
        int appended = 0;
        foreach (KeyValuePair<int, WorldRuntimeState> pair in WorldStates)
        {
            if (appended > 0)
                builder.Append(" | ");

            WorldRuntimeState state = pair.Value;
            builder.Append("agentType=");
            builder.Append(pair.Key);
            builder.Append(",world=");
            builder.Append(state?.World != null ? state.World.Version.ToString() : "null");
            builder.Append(",dirty=");
            builder.Append(state != null && state.IsDirty);
            builder.Append(",build=");
            builder.Append(state?.BuildJob != null ? state.BuildJob.Stage.ToString() : "null");
            builder.Append(",runtime=");
            builder.Append(state?.RuntimeDirtyJob != null ? state.RuntimeDirtyJob.Stage.ToString() : "null");
            appended++;
            if (appended >= 8)
                break;
        }

        if (appended == 0)
            builder.Append("none");
        builder.Append(']');
        return builder.ToString();
    }

    private static void TrimSectorPortalAccessCache()
    {
        // Sector portal access fields are required graph data for strict path building.
        // Dirty-sector invalidation removes stale entries; LRU eviction would create missing-field failures.
    }

}
