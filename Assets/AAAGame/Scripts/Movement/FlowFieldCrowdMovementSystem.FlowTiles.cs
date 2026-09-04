using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public static partial class FlowFieldCrowdMovementSystem
{
    private static int ResolveNeighborOffsetIndex(int dx, int dy)
    {
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            if (NeighborOffsetX[i] == dx && NeighborOffsetY[i] == dy)
                return i;
        }

        return -1;
    }

    private static byte EncodeFlowDirectionIndex(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return 0;

        Vector2 normalized = direction.normalized;
        float bestDot = float.NegativeInfinity;
        int bestIndex = 0;
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            Vector2 candidate = new Vector2(NeighborOffsetX[i], NeighborOffsetY[i]).normalized;
            float dot = Vector2.Dot(normalized, candidate);
            if (dot <= bestDot)
                continue;

            bestDot = dot;
            bestIndex = i + 1;
        }

        return (byte)bestIndex;
    }

    private static Vector2 DecodeFlowDirectionIndex(byte directionIndex)
    {
        if (directionIndex == 0)
            return Vector2.zero;

        int offsetIndex = directionIndex - 1;
        if (offsetIndex < 0 || offsetIndex >= NeighborOffsetX.Length)
            throw new InvalidOperationException($"DecodeFlowDirectionIndex failed: invalid directionIndex={directionIndex}.");

        return new Vector2(NeighborOffsetX[offsetIndex], NeighborOffsetY[offsetIndex]).normalized;
    }

    private static void ValidateFlowFieldCell(FlowTileCacheEntry tile, int localIndex)
    {
        if (tile == null)
            throw new InvalidOperationException("ValidateFlowFieldCell failed: tile is null.");
        if (tile.FlowFieldValues == null || tile.FlowFieldValues.Length != tile.Width * tile.Height)
            throw new InvalidOperationException($"ValidateFlowFieldCell failed: invalid flow field values key={FormatTileKey(tile.Key)}.");
        if (localIndex < 0 || localIndex >= tile.FlowFieldValues.Length)
            throw new InvalidOperationException($"ValidateFlowFieldCell failed: localIndex out of range index={localIndex} key={FormatTileKey(tile.Key)}.");
    }

    private static byte GetFlowDirectionIndex(FlowTileCacheEntry tile, int localIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        return (byte)(tile.FlowFieldValues[localIndex] & FlowDirectionMask);
    }

    private static Vector2 GetFlowDirection(FlowTileCacheEntry tile, int localIndex)
    {
        return DecodeFlowDirectionIndex(GetFlowDirectionIndex(tile, localIndex));
    }

    private static Vector2 ResolveRuntimeFlowDirection(FlowTileCacheEntry tile, int worldX, int worldY, int localIndex)
    {
        return ResolveRuntimeFlowDirection(tile, worldX, worldY, localIndex, Vector3.zero, 0f, constrainFromActualPosition: false);
    }

    private static Vector2 ResolveRuntimeFlowDirection(
        FlowTileCacheEntry tile,
        int worldX,
        int worldY,
        int localIndex,
        Vector3 actualPosition,
        float agentRadius,
        bool constrainFromActualPosition)
    {
        if (!IsFlowPathable(tile, localIndex))
            return Vector2.zero;
        if (!IsFlowReachable(tile, localIndex))
            return Vector2.zero;

        Vector2 storedDirection = GetFlowDirection(tile, localIndex);
        if (constrainFromActualPosition
            && storedDirection.sqrMagnitude > 0.0001f
            && !IsFlowDirectionGridTraversable(worldX, worldY, storedDirection))
        {
            return Vector2.zero;
        }

        return storedDirection;
    }

    private static bool IsFlowDirectionGridTraversable(
        int worldX,
        int worldY,
        Vector2 direction)
    {
        if (_world == null || direction.sqrMagnitude <= 0.0001f)
            return false;

        int stepX = direction.x > 0.5f ? 1 : direction.x < -0.5f ? -1 : 0;
        int stepY = direction.y > 0.5f ? 1 : direction.y < -0.5f ? -1 : 0;
        if (stepX == 0 && stepY == 0)
            return false;

        int nextX = worldX + stepX;
        int nextY = worldY + stepY;
        if (!_world.IsWalkable(nextX, nextY) || !CanTraverseNeighborCells(_world, worldX, worldY, nextX, nextY))
            return false;

        return true;
    }

    private static string BuildFlowDirectionGridTraversableReason(
        int worldX,
        int worldY,
        Vector2 direction)
    {
        if (_world == null)
            return "world-null";
        if (direction.sqrMagnitude <= 0.0001f)
            return "zero-dir";

        int stepX = direction.x > 0.5f ? 1 : direction.x < -0.5f ? -1 : 0;
        int stepY = direction.y > 0.5f ? 1 : direction.y < -0.5f ? -1 : 0;
        if (stepX == 0 && stepY == 0)
            return $"zero-step dir={direction}";

        int nextX = worldX + stepX;
        int nextY = worldY + stepY;
        bool walkable = _world.IsWalkable(nextX, nextY);
        bool traversable = walkable && CanTraverseNeighborCells(_world, worldX, worldY, nextX, nextY);
        return $"step=({stepX},{stepY}),next=({nextX},{nextY}),walk={walkable},trav={traversable}";
    }

    private static float[] RentIntegrationArray(int length)
    {
        if (length <= 0)
            throw new InvalidOperationException($"RentIntegrationArray failed: invalid length={length}.");

        if (IntegrationArrayPool.TryGetValue(length, out Stack<float[]> stack) && stack.Count > 0)
            return stack.Pop();

        return new float[length];
    }

    private static void ReturnIntegrationArray(float[] array)
    {
        if (array == null)
            return;
        if (array.Length <= 0)
            throw new InvalidOperationException("ReturnIntegrationArray failed: invalid zero-length array.");

        if (!IntegrationArrayPool.TryGetValue(array.Length, out Stack<float[]> stack))
        {
            stack = new Stack<float[]>();
            IntegrationArrayPool.Add(array.Length, stack);
        }

        if (stack.Count < 16)
            stack.Push(array);
    }

    private static long[] RentPortalAccessIntegrationArray(int length)
    {
        if (length <= 0)
            throw new InvalidOperationException($"RentPortalAccessIntegrationArray failed: invalid length={length}.");

        if (PortalAccessIntegrationArrayPool.TryGetValue(length, out Stack<long[]> stack) && stack.Count > 0)
            return stack.Pop();

        return new long[length];
    }

    private static void ReturnPortalAccessIntegrationArray(long[] array)
    {
        if (array == null)
            return;
        if (array.Length <= 0)
            throw new InvalidOperationException("ReturnPortalAccessIntegrationArray failed: invalid zero-length array.");

        if (!PortalAccessIntegrationArrayPool.TryGetValue(array.Length, out Stack<long[]> stack))
        {
            stack = new Stack<long[]>();
            PortalAccessIntegrationArrayPool.Add(array.Length, stack);
        }

        stack.Push(array);
    }

    private static void ReturnTileDebugIntegrationPayload(FlowTileCacheEntry tile)
    {
        if (tile?.DebugIntegrationPayload?.Values == null)
            return;

        float[] values = tile.DebugIntegrationPayload.Values;
        tile.DebugIntegrationPayload.Values = null;
        tile.DebugIntegrationPayload = null;
        ReturnIntegrationArray(values);
    }

    private static void ReleaseTilePayloads(FlowTileCacheEntry tile)
    {
        ReturnTileDebugIntegrationPayload(tile);
    }

    private static void ClearFlowTileCache()
    {
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
            ReleaseTilePayloads(tile);

        FlowTileCache.Clear();
        ClearDeterministicFlowTileCache();
        FlowLocalPotentialShapes.Clear();
    }

    private static void RemoveFlowTileCacheEntry(FlowTileCacheKey key)
    {
        RemoveDeterministicFlowTileCacheEntry(key);
        if (!FlowTileCache.TryGetValue(key, out FlowTileCacheEntry tile))
            return;
        if (!FlowTileCache.Remove(key))
            throw new InvalidOperationException($"Flow tile cache removal failed. key={FormatTileKey(key)}.");
        ReleaseTilePayloads(tile);
    }

    private static void ReturnPendingFlowTileBuildIntegrations()
    {
        for (LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First; node != null; node = node.Next)
        {
            FlowTileBuildJob job = node.Value;
            if (job == null)
                throw new InvalidOperationException("ReturnPendingFlowTileBuildIntegrations encountered a null job.");
            if (job.HasIntegrationDirectionsHandle)
                job.IntegrationDirectionsHandle.Complete();
            DisposeDeterministicFlowTileIntegrationJobPayloads(job);
        }
    }

    private static void DisposeDeterministicFlowTileIntegrationJobPayloads(FlowTileBuildJob job)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));
        if (job.IntegrationCostsNative.IsCreated) job.IntegrationCostsNative.Dispose();
        if (job.DirectionsNative.IsCreated) job.DirectionsNative.Dispose();
        if (job.GoalLocalIndicesNative.IsCreated) job.GoalLocalIndicesNative.Dispose();
        if (job.GoalSeedCostsNative.IsCreated) job.GoalSeedCostsNative.Dispose();
        if (job.PortalHandoffNative.IsCreated) job.PortalHandoffNative.Dispose();
        if (job.ExternalXNative.IsCreated) job.ExternalXNative.Dispose();
        if (job.ExternalYNative.IsCreated) job.ExternalYNative.Dispose();
        if (job.ExternalCostsNative.IsCreated) job.ExternalCostsNative.Dispose();
        if (job.StatusNative.IsCreated) job.StatusNative.Dispose();
        if (job.SectorInputBacking != null)
        {
            job.SectorInputBacking.Release();
            job.SectorInputBacking = null;
        }
        job.HasIntegrationDirectionsHandle = false;
        job.IntegrationDirectionsHandle = default;
    }

    private static void ReplaceFlowTileWorldInputBackings(NavigationWorld world)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        int cellCount = checked(world.Width * world.Height);
        bool hasWholeCostField = world.CostField != null && world.CostField.Length == cellCount;
        bool hasSectorCostFields = world.SectorCostFields != null
                                   && world.Sectors != null
                                   && world.SectorCostFields.Length == world.Sectors.Length;
        if (world.WalkableMask == null || world.WalkableMask.Length != cellCount
            || world.NeighborTraversalMask == null || world.NeighborTraversalMask.Length != cellCount
            || (!hasWholeCostField && !hasSectorCostFields))
        {
            throw new InvalidOperationException(
                $"Flow tile world input backing requires complete committed fields world={world.Version}, agentType={world.AgentTypeId}.");
        }

        DisposeFlowTileWorldInputBackings(world);
        world.FlowTileSectorInputBackings = new FlowTileSectorInputBacking[world.Sectors.Length];
        for (int sectorIndex = 0; sectorIndex < world.Sectors.Length; sectorIndex++)
            world.FlowTileSectorInputBackings[sectorIndex] = CreateFlowTileSectorInputBacking(world, sectorIndex);
    }

    private static void EnsureFlowTileWorldInputBackings(NavigationWorld world)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (world.FlowTileSectorInputBackings == null)
            ReplaceFlowTileWorldInputBackings(world);
        else if (world.FlowTileSectorInputBackings.Length != world.Sectors.Length)
            throw new InvalidOperationException($"Flow tile world input backing count mismatch world={world.Version}.");
    }

    private static FlowTileSectorInputBacking CreateFlowTileSectorInputBacking(NavigationWorld world, int sectorIndex)
    {
        if (world == null || world.Sectors == null)
            throw new InvalidOperationException("CreateFlowTileSectorInputBacking failed: world sectors are missing.");
        SectorData sector = world.Sectors[sectorIndex]
            ?? throw new InvalidOperationException($"Flow tile input backing sector is null index={sectorIndex}.");
        int count = checked(sector.Width * sector.Height);
        bool[] walkable = new bool[count];
        byte[] traversal = new byte[count];
        byte[] costs = new byte[count];
        for (int y = 0; y < sector.Height; y++)
        for (int x = 0; x < sector.Width; x++)
        {
            int local = x + y * sector.Width;
            int worldIndex = world.GetIndex(sector.StartX + x, sector.StartY + y);
            walkable[local] = world.WalkableMask[worldIndex];
            traversal[local] = world.NeighborTraversalMask[worldIndex];
            costs[local] = checked((byte)GetCostFieldValueStrict(world, sector.StartX + x, sector.StartY + y));
        }
        return new FlowTileSectorInputBacking
        {
            Walkable = new NativeArray<bool>(walkable, Allocator.Persistent),
            TraversalMask = new NativeArray<byte>(traversal, Allocator.Persistent),
            CellCosts = new NativeArray<byte>(costs, Allocator.Persistent)
        };
    }

    private static void PrepareRuntimeDirtyFlowTileWorldInputBackings(RuntimeDirtyRebuildJob job)
    {
        if (job == null || job.TargetWorld == null || job.WorkingWorld == null)
            throw new InvalidOperationException("PrepareRuntimeDirtyFlowTileWorldInputBackings failed: world is missing.");
        if (job.WorkingWorld.FlowTileSectorInputBackings != null)
            throw new InvalidOperationException("PrepareRuntimeDirtyFlowTileWorldInputBackings called twice.");
        FlowTileSectorInputBacking[] source = job.TargetWorld.FlowTileSectorInputBackings;
        if (source == null || source.Length != job.TargetWorld.Sectors.Length)
            throw new InvalidOperationException("PrepareRuntimeDirtyFlowTileWorldInputBackings failed: target backing is missing.");
        FlowTileSectorInputBacking[] result = new FlowTileSectorInputBacking[job.WorkingWorld.Sectors.Length];
        try
        {
            for (int i = 0; i < result.Length; i++)
            {
                if (job.DirtySectors.Contains(i) || job.CostDirtySectors.Contains(i))
                    result[i] = CreateFlowTileSectorInputBacking(job.WorkingWorld, i);
                else
                {
                    FlowTileSectorInputBacking backing = source[i]
                        ?? throw new InvalidOperationException($"PrepareRuntimeDirtyFlowTileWorldInputBackings found null source backing sector={i}.");
                    backing.Acquire();
                    result[i] = backing;
                }
            }
            job.WorkingWorld.FlowTileSectorInputBackings = result;
        }
        catch
        {
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] == null)
                    continue;
                if (job.DirtySectors.Contains(i) || job.CostDirtySectors.Contains(i))
                    result[i].ReleaseOwner();
                else
                    result[i].Release();
            }
            throw;
        }
    }

    private static FlowTileSectorInputBacking RequireFlowTileSectorInputBacking(NavigationWorld world, int sectorId)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (world.FlowTileSectorInputBackings == null || sectorId < 0 || sectorId >= world.FlowTileSectorInputBackings.Length)
            throw new InvalidOperationException($"Flow tile sector input backing is missing world={world.Version} sector={sectorId}.");
        FlowTileSectorInputBacking backing = world.FlowTileSectorInputBackings[sectorId];
        SectorData sector = world.Sectors[sectorId];
        int cellCount = checked(sector.Width * sector.Height);
        if (backing == null
            || !backing.Walkable.IsCreated || backing.Walkable.Length != cellCount
            || !backing.TraversalMask.IsCreated || backing.TraversalMask.Length != cellCount
            || !backing.CellCosts.IsCreated || backing.CellCosts.Length != cellCount)
        {
            throw new InvalidOperationException(
                $"Flow tile world input backing is missing or invalid world={world.Version}, agentType={world.AgentTypeId}.");
        }
        return backing;
    }

    private static void DisposeFlowTileWorldInputBackings(NavigationWorld world)
    {
        if (world?.FlowTileSectorInputBackings == null)
            return;
        for (int i = 0; i < world.FlowTileSectorInputBackings.Length; i++)
        {
            FlowTileSectorInputBacking backing = world.FlowTileSectorInputBackings[i];
            if (backing == null)
                continue;
            backing.ReleaseOwner();
        }
        world.FlowTileSectorInputBackings = null;
    }

    private static void ReturnRuntimeDirtyWorkingWorld(RuntimeDirtyRebuildJob job)
    {
        job?.WorldHashBuildJob?.Dispose();
        ReturnPendingSectorPortalAccessIntegrations(job?.PendingPortalAccessEntries);
        ReturnRuntimeDirtyIslandBuffers(job);
        NavigationWorld working = job?.WorkingWorld;
        if (working == null)
            return;

        DisposeFlowTileWorldInputBackings(working);

        if (!ReferenceEquals(working.Hierarchy, job.TargetWorld?.Hierarchy))
            DisposeFlowPathKernelWitnessIndex(working.Hierarchy);
        if (!ReferenceEquals(working.L0SearchGraphIndex, job.TargetWorld?.L0SearchGraphIndex))
            working.L0SearchGraphIndex?.Dispose();
        working.L0SearchGraphIndex = null;

        ReturnWorldArrayIfOwned(working.WalkableMask, working.BaseWalkableMask, null);
        ReturnWorldArrayIfOwned(working.NeighborTraversalMask, working.BaseNeighborTraversalMask, null);
        ReturnWorldArrayIfOwned(working.IslandIds, null, null);
        ReturnByteArray(working.CostField);
        job.WorkingWorld = null;
    }

    private static void ReturnRuntimeDirtyIslandBuffers(RuntimeDirtyRebuildJob job)
    {
        if (job == null)
            return;

        job.IslandOpenQueue = null;

        ReturnWorldArrayIfOwned(job.IslandComponentOffsets, null, null);
        ReturnWorldArrayIfOwned(job.IslandParents, null, null);
        ReturnWorldArrayIfOwned(job.IslandComponentSizes, null, null);
        ReturnWorldArrayIfOwned(job.IslandComponentMins, null, null);
        ReturnWorldArrayIfOwned(job.IslandRootHeap, null, null);
        ReturnWorldArrayIfOwned(job.IslandRootIds, null, null);
        job.IslandComponentOffsets = null;
        job.IslandParents = null;
        job.IslandComponentSizes = null;
        job.IslandComponentMins = null;
        job.IslandRootHeap = null;
        job.IslandRootIds = null;
    }

    private static bool TryGetTileIntegrationCost(FlowTileCacheEntry tile, int worldX, int worldY, out float cost)
    {
        cost = float.PositiveInfinity;
        if (tile == null || !IsInsideSector(tile, worldX, worldY))
            return false;
        int deterministicCost = GetDeterministicIntegrationCost(
            tile,
            tile.GetLocalIndex(worldX, worldY));
        cost = deterministicCost == int.MaxValue ? float.PositiveInfinity : deterministicCost / 1024f;
        return true;
    }

    private static bool HasDeterministicIntegrationPayload(FlowTileCacheEntry tile)
    {
        if (tile == null)
            return false;
        int count = checked(tile.Width * tile.Height);
        if (tile.DeterministicIntegrationCosts != null)
            return tile.DeterministicIntegrationCosts.Length == count && tile.PotentialShape == null;
        return tile.PotentialShape?.NormalizedIntegrationCosts != null
               && tile.PotentialShape.NormalizedIntegrationCosts.Length == count;
    }

    private static int GetDeterministicIntegrationCost(FlowTileCacheEntry tile, int localIndex)
    {
        if (tile == null)
            throw new InvalidOperationException("Deterministic integration cost read failed: tile is null.");
        int count = checked(tile.Width * tile.Height);
        if (localIndex < 0 || localIndex >= count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(localIndex),
                localIndex,
                $"Deterministic integration cost index is outside tile payload count={count}, key={FormatTileKey(tile.Key)}.");
        }
        if (tile.DeterministicIntegrationCosts != null)
        {
            if (tile.DeterministicIntegrationCosts.Length != count || tile.PotentialShape != null)
                throw new InvalidOperationException($"Deterministic integration tile owns inconsistent payloads key={FormatTileKey(tile.Key)}.");
            return tile.DeterministicIntegrationCosts[localIndex];
        }

        FlowLocalPotentialShape shape = tile.PotentialShape
                                        ?? throw new InvalidOperationException($"Deterministic integration tile has no payload key={FormatTileKey(tile.Key)}.");
        if (shape.NormalizedIntegrationCosts == null || shape.NormalizedIntegrationCosts.Length != count)
            throw new InvalidOperationException($"Deterministic integration shape payload is invalid key={FormatTileKey(tile.Key)}.");
        int normalizedCost = shape.NormalizedIntegrationCosts[localIndex];
        return normalizedCost == int.MaxValue
            ? int.MaxValue
            : checked((int)ClampDeterministicIntegrationCost(
                checked((long)normalizedCost + tile.PotentialOffset),
                int.MaxValue));
    }

    private static bool TryRebuildTileDebugIntegration(FlowTileCacheEntry tile)
    {
        if (_world == null || tile == null)
            return false;
        if (tile.Key.SectorId < 0 || tile.Key.SectorId >= _world.Sectors.Length)
            return false;
        if (!HasDeterministicIntegrationPayload(tile))
        {
            throw new InvalidOperationException($"TryRebuildTileDebugIntegration failed: invalid deterministic integration payload key={FormatTileKey(tile.Key)}.");
        }

        ReturnTileDebugIntegrationPayload(tile);
        FlowTileDebugIntegrationPayload payload = new FlowTileDebugIntegrationPayload(tile.Width * tile.Height);
        float[] integration = payload.Values;
        for (int i = 0; i < integration.Length; i++)
        {
            int deterministicCost = GetDeterministicIntegrationCost(tile, i);
            integration[i] = deterministicCost == int.MaxValue
                ? float.PositiveInfinity
                : deterministicCost / 1024f;
        }

        tile.DebugIntegrationPayload = payload;
        return true;
    }

    private static float GetTileIntegrationCostForDiagnostics(FlowTileCacheEntry tile, int worldX, int worldY)
    {
        return TryGetTileIntegrationCost(tile, worldX, worldY, out float cost)
            ? cost
            : float.PositiveInfinity;
    }

    private static bool TryGetTileIntegrationCostForDiagnostics(
        FlowTileCacheEntry tile,
        int worldX,
        int worldY,
        out float cost,
        out string source)
    {
        cost = float.PositiveInfinity;
        source = "missing";
        if (tile == null || !IsInsideSector(tile, worldX, worldY))
            return false;

        int localIndex = tile.GetLocalIndex(worldX, worldY);
        if (tile.DebugIntegrationPayload?.Values != null)
        {
            cost = tile.DebugIntegrationPayload.Values[localIndex];
            source = "debug";
            return true;
        }

        if (!TryGetTileIntegrationCost(tile, worldX, worldY, out cost))
            return false;
        source = "deterministic";
        return true;
    }

    private static bool TryGetTileIntegrationSummaryCostForDiagnostics(
        FlowTileCacheEntry tile,
        int worldX,
        int worldY,
        out float cost,
        out string source)
    {
        cost = float.PositiveInfinity;
        source = "missing";
        if (tile == null || !IsInsideSector(tile, worldX, worldY))
            return false;

        if (!TryGetTileIntegrationCost(tile, worldX, worldY, out cost))
            return false;
        source = "deterministic";
        return true;
    }

    private static void SetFlowDirectionIndex(FlowTileCacheEntry tile, int localIndex, byte directionIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        if ((directionIndex & ~FlowDirectionMask) != 0)
            throw new InvalidOperationException($"SetFlowDirectionIndex failed: invalid directionIndex={directionIndex}.");

        tile.FlowFieldValues[localIndex] = (byte)((tile.FlowFieldValues[localIndex] & ~FlowDirectionMask) | directionIndex);
    }

    private static bool IsFlowPathable(FlowTileCacheEntry tile, int localIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        return (tile.FlowFieldValues[localIndex] & FlowPathableFlag) != 0;
    }

    private static void SetFlowPathable(FlowTileCacheEntry tile, int localIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        tile.FlowFieldValues[localIndex] |= FlowPathableFlag;
    }

    private static bool IsFlowReachable(FlowTileCacheEntry tile, int localIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        return (tile.FlowFieldValues[localIndex] & FlowReachableFlag) != 0;
    }

    private static void SetFlowReachable(FlowTileCacheEntry tile, int localIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        tile.FlowFieldValues[localIndex] |= FlowReachableFlag;
    }

    private static bool HasRawNeighborTraversal(NavigationWorld world, int fromX, int fromY, int toX, int toY)
    {
        if (world == null)
            throw new InvalidOperationException("HasRawNeighborTraversal failed: world is null.");
        if (!world.IsWalkable(fromX, fromY) || !world.IsWalkable(toX, toY))
            return false;

        int dx = toX - fromX;
        int dy = toY - fromY;
        int offsetIndex = ResolveNeighborOffsetIndex(dx, dy);
        if (offsetIndex < 0)
            return false;

        int fromIndex = world.GetIndex(fromX, fromY);
        return (world.NeighborTraversalMask[fromIndex] & (1 << offsetIndex)) != 0;
    }

    private sealed class WorldRuntimeState
    {
        public int AgentTypeId;
        public NavigationWorld World;
        public bool IsDirty = true;
        public readonly HashSet<int> DirtyRuntimeObstacleSectors = new HashSet<int>();
        public RuntimeDirtyRebuildJob RuntimeDirtyJob;
        public WorldBuildJob BuildJob;
    }

    private enum WorldBuildStage
    {
        Initialize = 0,
        CreateWorldShell = 2,
        ApplyRuntimeObstacles = 3,
        InitializeSectors = 4,
        BuildCellNavAnchors = 5,
        NeighborMask = 6,
        SymmetrizeNeighborMask = 7,
        CostField = 8,
        IslandField = 9,
        GoalProjectionIndex = 10,
        PortalGraph = 11,
        Hierarchy = 12,
        Commit = 13,
        Complete = 14
    }

    private sealed class WorldBuildJob
    {
        public int AgentTypeId;
        public WorldBuildStage Stage;
        public int Width;
        public int Height;
        public float CellSize;
        public float EncodedCenterClearance;
        public Vector3 Origin;
        public long CellSizeGridRaw;
        public long EncodedCenterClearanceFixedRaw;
        public long OriginXGridRaw;
        public long OriginZGridRaw;
        public long AgentRadiusFixedRaw;
        public bool HasAuthorityGridMetadata;
        public bool[] BaseWalkableMask;
        public byte[] BaseNeighborTraversalMask;
        public FixVector2[] StaticCollisionVertices;
        public int[] StaticCollisionPathStarts;
        public byte[] BaseCostField;
        public Vector3[] CellNavAnchors;
        public FixVector2[] CellNavAnchorsFixedXZ;
        public bool HasProvidedCellNavAnchors;
        public bool HasProvidedNeighborTraversalMask;
        public bool IncludeRuntimeObstacles = true;
        public NavigationWorld WorkingWorld;
        public List<CircleObstacle> CircleObstacles;
        public List<BoxObstacle> BoxObstacles;
        public List<CostStamp> CostStamps;
        public int ObstacleCursor;
        public bool ApplyingCircleObstacles = true;
        public int SectorCursor;
        public int CellCursor;
        public int IslandScanIndex;
        public int IslandCurrentId;
        public int IslandCurrentSize;
        public Queue<int> IslandOpenQueue;
        public int IslandMainId;
        public int IslandMainSize;
        public bool IslandInitialized;
        public bool IslandBfsActive;
        public GoalProjectionSpatialIndexBuildJob GoalProjectionIndexBuildJob;
        public bool UsesPrebakedHierarchy;
        public RuntimeDirtyPortalStage PortalStage;
        public bool PortalInitialized;
        public List<PortalData> PortalRebuiltPortals;
        public int PortalAddCursor;
        public int PortalTransitionCursor;
        public int PortalTransitionFromCursor;
        public List<PendingSectorPortalAccess> PendingPortalAccessEntries;
        public PortalHierarchyBuildJob HierarchyBuildJob;
        public bool HierarchyTransitionCostsPrepared;
        public ulong AuthorityInputHash;
        public bool HasAuthorityInputHash;
        public string Reason;
        public long BuildStartedTimestamp;
        public long StageStartedTimestamp;
        public WorldBuildStage TimedStage;
        public bool TimingInitialized;
        public long PortalGraphBoundaryTicks;
        public long PortalGraphAddTicks;
        public int PortalGraphAccessCount;
        public int PortalGraphAnalyticAccessCount;
        public int PortalGraphTransitionCostChecks;
        public int PortalGraphAnalyticCheckCount;
        public int PortalGraphAnalyticClearSectorCount;
        public int PortalGraphAnalyticMultiComponentRejectCount;
        public int PortalGraphAnalyticNonClearRejectCount;
        public void FreezeAuthorityGridMetadata()
        {
            FreezeAuthorityGridMetadata(ResolveAgentTypeRadiusFixed(AgentTypeId).RawValue);
        }

        public void FreezeAuthorityGridMetadata(long agentRadiusFixedRaw)
        {
            CellSizeGridRaw = NavigationWorld.FloatToGridRaw(CellSize);
            if (CellSizeGridRaw <= 0)
                throw new InvalidOperationException("WorldBuildJob authority cell size must be positive.");
            if (agentRadiusFixedRaw <= 0)
                throw new InvalidOperationException("WorldBuildJob authority agent radius must be positive.");
            EncodedCenterClearanceFixedRaw = ((Fix64)EncodedCenterClearance).RawValue;
            OriginXGridRaw = NavigationWorld.FloatToGridRaw(Origin.x);
            OriginZGridRaw = NavigationWorld.FloatToGridRaw(Origin.z);
            AgentRadiusFixedRaw = agentRadiusFixedRaw;
            HasAuthorityGridMetadata = true;
        }

        public void SetAuthorityGridMetadata(
            long cellSizeGridRaw,
            long encodedCenterClearanceFixedRaw,
            long agentRadiusFixedRaw,
            long originXGridRaw,
            long originZGridRaw)
        {
            if (cellSizeGridRaw <= 0)
                throw new InvalidOperationException("WorldBuildJob authority cell size must be positive.");
            if (agentRadiusFixedRaw <= 0)
                throw new InvalidOperationException("WorldBuildJob authority agent radius must be positive.");

            CellSizeGridRaw = cellSizeGridRaw;
            EncodedCenterClearanceFixedRaw = encodedCenterClearanceFixedRaw;
            AgentRadiusFixedRaw = agentRadiusFixedRaw;
            OriginXGridRaw = originXGridRaw;
            OriginZGridRaw = originZGridRaw;
            HasAuthorityGridMetadata = true;
        }
    }

    private enum RuntimeDirtyRebuildStage
    {
        InitializeClone = 0,
        ResetWalkable = 1,
        ApplyObstacles = 2,
        NeighborMask = 3,
        SymmetrizeNeighborMask = 4,
        CostField = 5,
        IslandField = 6,
        SectorComponents = 7,
        GoalProjectionIndex = 8,
        PortalGraph = 9,
        Hierarchy = 10,
        PrepareCommit = 11,
        WorldHash = 12,
        Commit = 13,
        Complete = 14
    }

    private enum RuntimeDirtyCloneShellStage
    {
        CreateMetadata = 0,
        RentWalkableMask = 1,
        RentCostField = 2,
        RentNeighborTraversalMask = 3,
        RentIslandIds = 4,
        CreateSectorSlots = 5,
        ClonePortalArray = 6,
        ClonePortalLookup = 7,
        ClonePortalSignatures = 8,
        CloneUsedPortalIds = 9,
        Complete = 10
    }

    private enum RuntimeDirtyPortalStage
    {
        RemoveOldPortals = 0,
        RebuildBoundaries = 1,
        AddRebuiltPortals = 2,
        RebuildTransitions = 3,
        Complete = 4
    }

    private enum RuntimeDirtyIslandStage
    {
        RebuildDirtyComponents = 0,
        PrepareComponentOffsets = 1,
        InitializeComponentNodes = 2,
        LinkSectorBoundaries = 3,
        CollectIslandRoots = 4,
        AssignIslandIds = 5,
        AssignSectorMappings = 6,
        UpdateCompatibilityCache = 7,
        Complete = 8
    }

    private sealed class RuntimeDirtyRebuildJob
    {
        public NavigationWorld TargetWorld;
        public NavigationWorld WorkingWorld;
        public HashSet<int> DirtySectors;
        public HashSet<int> CostDirtySectors;
        public List<int> DirtySectorIds;
        public List<int> CostDirtySectorIds;
        public List<CircleObstacle> CircleObstacles;
        public List<BoxObstacle> BoxObstacles;
        public List<CostStamp> CostStamps;
        public RuntimeDirtyRebuildStage Stage;
        public int CloneCellCursor;
        public int CloneSectorCursor;
        public RuntimeDirtyCloneShellStage CloneShellStage;
        public bool CloneShellInitialized;
        public int SectorCursor;
        public int ObstacleCursor;
        public bool ApplyingCircleObstacles = true;
        public int IslandMainId;
        public int IslandMainSize;
        public bool IslandInitialized;
        public bool IsInitialWorldOverlay;
        public Queue<int> IslandOpenQueue;
        public int IslandScanIndex;
        public int IslandCurrentId;
        public int IslandCurrentSize;
        public bool IslandBfsActive;
        public RuntimeDirtyIslandStage IslandStage;
        public int[] IslandComponentOffsets;
        public int[] IslandParents;
        public int[] IslandComponentSizes;
        public int[] IslandComponentMins;
        public int[] IslandRootHeap;
        public int[] IslandRootIds;
        public int IslandComponentTotal;
        public int IslandSectorCursor;
        public int IslandComponentCursor;
        public int IslandBoundaryCursor;
        public int IslandNodeCursor;
        public int IslandRootHeapCount;
        public int IslandNextId;
        public int IslandCompatibilitySectorCursor;
        public int IslandCompatibilityCellCursor;
        public int IslandUniformId;
        public bool IslandUniformMixed;
        public bool IslandMappingNeedsWrite;
        public GoalProjectionSpatialIndexBuildJob GoalProjectionIndexBuildJob;
        public RuntimeDirtyPortalStage PortalStage;
        public bool PortalInitialized;
        public HashSet<int> PortalTransitionDirtySectors;
        public List<int> PortalTransitionSectorIds;
        public List<PortalData> PortalRebuiltPortals;
        public HashSet<long> PortalProcessedBoundaries;
        public int PortalSectorCursor;
        public int PortalAddCursor;
        public int PortalTransitionCursor;
        public int PortalTransitionFromCursor;
        public List<PendingSectorPortalAccess> PendingPortalAccessEntries;
        public PortalHierarchyBuildJob HierarchyBuildJob;
        public NavigationWorldHashBuildJob WorldHashBuildJob;
        public ulong AuthorityInputHash;
        public bool HasAuthorityInputHash;
        public string Reason;
        public long BuildStartedTimestamp;
        public long StageStartedTimestamp;
        public RuntimeDirtyRebuildStage TimedStage;
        public bool TimingInitialized;
        public readonly long[] StageAccumulatedTicks = new long[(int)RuntimeDirtyRebuildStage.Complete];
        public long PortalGraphBoundaryTicks;
        public long PortalGraphAddTicks;
        public int PortalGraphAccessCount;
        public int PortalGraphAnalyticAccessCount;
        public int PortalGraphTransitionCostChecks;
        public int PortalGraphAnalyticCheckCount;
        public int PortalGraphAnalyticClearSectorCount;
        public int PortalGraphAnalyticMultiComponentRejectCount;
        public int PortalGraphAnalyticNonClearRejectCount;
        public long CommitSwapTicks;
        public long CommitCacheInvalidationTicks;
        public long CommitPortalAccessTicks;
        public long CommitWorldHashTicks;
        public long CommitAgentInvalidationTicks;
    }

    private struct FlowPerfAccumulator
    {
        public int Frame;
        public int WorldBuilds;
        public int PathBuilds;
        public int NavigationPrepareRequests;
        public int NavigationPathRequestGroups;
        public int NavigationPathRequestSourceAdds;
        public int NavigationPathRequestOperations;
        public int NavigationPathSearchCommandSlices;
        public int NavigationPathSearchCommandOperations;
        public int NavigationPathRouteSlices;
        public int NavigationPathRouteSliceOperations;
        public int NavigationPathRequestCommits;
        public int NavigationPathSourceCommits;
        public int NavigationPathPolicyCreates;
        public int NavigationPathRestrictedSearchCreates;
        public int NavigationPathHierarchyPolicyCreates;
        public int NavigationPathMaterializationCreates;
        public int NavigationPathImmutableConcatCreates;
        public int NavigationPathSharedSuffixCreates;
        public int NavigationPathSharedStartRouteHits;
        public int NavigationPathMovingGoalCorridorTrimHits;
        public int NavigationPathSharedGoalConnectorBuilds;
        public int NavigationPathSharedGoalConnectorHits;
        public int NavigationDemandResolutionCount;
        public int NavigationDemandDispatchCount;
        public int FixedCorridorBuildOperations;
        public int NavigationPathInitializeOperations;
        public int NavigationPathGoalConnectorOperations;
        public int NavigationPathCreateHierarchyOperations;
        public int NavigationPathExpandHierarchyOperations;
        public int NavigationPathDownwardOperations;
        public int NavigationPathL0Operations;
        public int NavigationPathMaterializeOperations;
        public int NavigationPathMaterializeInitializeOperations;
        public int NavigationPathMaterializeStartPortalOperations;
        public int NavigationPathMaterializeDownwardOperations;
        public int NavigationPathMaterializePolicyOperations;
        public int NavigationPathMaterializeGoalConnectorOperations;
        public int NavigationPathMaterializeConversionOperations;
        public int NavigationPathMaterializeImmutableCopyOperations;
        public int NavigationPathMaterializeHashOperations;
        public int NavigationPathMaterializePublishOperations;
        public int NavigationPathLocalBindingOperations;
        public int NavigationPathFinalBindingSources;
        public int NavigationPathFinalBindingCrossSectorSources;
        public int NavigationPathFinalBindingGoalPortalsScanned;
        public int NavigationPathFinalBindingGoalPortalsAccessible;
        public int NavigationPathFinalBindingAnchorPortalsScanned;
        public int NavigationPathFinalBindingAnchorPortalsAccessible;
        public int NavigationPathFinalBindingSettledNodes;
        public int NavigationPathFinalBindingRouteNodes;
        public int NavigationPathFinalBindingFirstSampleSet;
        public int NavigationPathFinalBindingFirstSourceId;
        public int NavigationPathFinalBindingFirstAnchorSectorId;
        public int NavigationPathFinalBindingFirstGoalSectorId;
        public int NavigationPathCompleteOperations;
        public int SectorPathSearches;
        public int SectorPathCacheHits;
        public int PortalGraphMergeHits;
        public int SharedGoalFieldBuilds;
        public int PathBuildNoHandle;
        public int PathBuildWorldMismatch;
        public int PathBuildGoalSectorMismatch;
        public int PathBuildGoalCellMismatch;
        public int PathBuildInvalidHandle;
        public int PathStartPortalChecks;
        public int PathStartPortalCandidates;
        public int PathStartPortalCacheHits;
        public int PathStartPortalCacheMisses;
        public int PathFirstCrossingCacheHits;
        public int PathFirstCrossingCacheMisses;
        public int StableGoalRaw;
        public int StableGoalReachabilityReuse;
        public int StableGoalReuse;
        public int StableGoalRefreshInitial;
        public int StableGoalRefreshCellDelta;
        public int MovingTargetAnchorBatchHits;
        public int MovingTargetAnchorBatchMisses;
        public int SectorCorridorPolicyAuthorityHashRefreshes;
        public int SectorCorridorGoalConnectorBuilds;
        public int SectorCorridorExactGoalRebinds;
        public int SectorCorridorExactGoalReplacements;
        public int NavigationPathPolicyShiftCalls;
        public int NavigationPathPolicyShiftCostEntries;
        public int NavigationPathPolicyShiftOpenEntries;
        public int TileCacheHits;
        public int TileCacheMisses;
        public int PathPendingSharedGoal;
        public int TilePendingBuild;
        public int FlowTileQueueEnqueued;
        public int FlowTileQueuePruned;
        public int RequiredFlowTileCommits;
        public int FlowLocalPotentialShapeHits;
        public int FlowLocalPotentialShapeMisses;
        public int FlowTileBuildOperations;
        public int PortalSlotTraceOperations;
        public int PortalSlotFillOperations;
        public int SharedGoalPortalGraphNodeExpansions;
        public int SharedGoalPortalGraphIncomingTransitionScans;
        public int SharedGoalPortalGraphIncomingTransitionHits;
        public int PathPortalGraphNodeExpansions;
        public int PathPortalGraphOutgoingTransitionScans;
        public int PathPortalGraphOutgoingTransitionHits;
        public int SectorCorridorPolicyQueries;
        public int HierarchyStartConnectorCacheHits;
        public int HierarchyStartConnectorCacheMisses;
        public int HierarchyDownwardCustomizationCacheHits;
        public int HierarchyDownwardCustomizationCacheMisses;
        public int HierarchyDownwardCustomizationExpansions;
        public int HierarchyL0WitnessCacheHits;
        public int HierarchyL0WitnessCacheMisses;
        public int RuntimeDirtyApplications;
        public int RuntimeDirtySectorCount;
        public int GoalProjectionIndexBuildStarts;
        public int GoalProjectionIndexScannedCells;
        public int GoalProjectionIndexFullScans;
        public int GoalProjectionIndexPublishes;
        public int NavigationConstraintCalls;
        public int NavigationConstraintBlocked;
        public int CombatApproachCalls;
        public int CombatApproachSlotCacheHits;
        public int CombatApproachSlotCacheBuilds;
        public int CombatApproachScoredCandidates;
        public int CombatApproachSameIslandCandidates;
        public int CombatApproachExpandedCalls;
        public int CombatApproachExpandedCandidates;
        public int CombatApproachSuccesses;
        public int CombatApproachNoSlotFailures;
        public long FrameStartTicks;
        public long FrameTicks;
        public long PathStartPortalCheckTicks;
        public long PathFirstCrossingTicks;
        public long SectorCorridorPolicyTicks;
        public long AgentUpdateTicks;
        public long ManagerConfigTicks;
        public long ManagerSourceGateTicks;
        public long WorldBuildQueueTicks;
        public long RuntimeRebuildQueueTicks;
        public long FlowTileQueueTicks;
        public long FlowTileActiveEnqueueTicks;
        public long FlowTilePruneTicks;
        public long FlowTileReferenceTicks;
        public long NavigationPathInitializeTicks;
        public long NavigationPathInitializeSameSectorTicks;
        public long NavigationPathInitializePolicyTicks;
        public long NavigationPathInitializePolicyLookupTicks;
        public long NavigationPathInitializePolicyAnchorTicks;
        public long NavigationPathInitializePolicyConstructTicks;
        public long NavigationPathInitializePolicyAuthorityTicks;
        public long NavigationPathInitializePolicyPinTicks;
        public long NavigationPathInitializeHierarchyResolveTicks;
        public long NavigationPathInitializeHierarchySelectionTicks;
        public long NavigationPathGoalConnectorTicks;
        public long NavigationPathCreateHierarchyTicks;
        public long NavigationPathExpandHierarchyTicks;
        public long NavigationPathDownwardTicks;
        public long NavigationPathL0Ticks;
        public long NavigationPathMaterializeTicks;
        public long NavigationPathMaterializeInitializeTicks;
        public long NavigationPathMaterializeStartPortalTicks;
        public long NavigationPathMaterializeDownwardTicks;
        public long NavigationPathMaterializePolicyTicks;
        public long NavigationPathMaterializeGoalConnectorTicks;
        public long NavigationPathMaterializeConversionTicks;
        public long NavigationPathMaterializeImmutableCopyTicks;
        public long NavigationPathMaterializeHashTicks;
        public long NavigationPathMaterializePublishTicks;
        public long NavigationPathMaterializePublishSuffixTicks;
        public long NavigationPathMaterializePublishSuffixResolveTicks;
        public long NavigationPathMaterializePublishSuffixValidationTicks;
        public long NavigationPathMaterializePublishSuffixInsertTicks;
        public long NavigationPathMaterializePublishSuffixBookkeepingTicks;
        public long NavigationPathMaterializePublishSuffixFinalizeTicks;
        public long NavigationPathLocalBindingTicks;
        public long NavigationPathCompleteTicks;
        public long NavigationPathSlowestOperationTicks;
        public int NavigationPathSlowestWorldVersion;
        public int NavigationPathSlowestAgentTypeId;
        public int NavigationPathSlowestMovingTargetId;
        public int NavigationPathSlowestSourceId;
        public int NavigationPathSlowestSourceStage;
        public int NavigationPathSlowestMaterializationStage;
        public int NavigationPathSlowestRouteTaskType;
        public long NavigationDemandResolutionTicks;
        public long NavigationDemandDispatchTicks;
        public long StableGoalProfilerRecordTicks;
        public int StableGoalProfilerRecordCount;
        public long StableGoalPrologueTicks;
        public long StableGoalBodyTicks;
        public int StableGoalBodyCallCount;
        public long StableGoalMeasurementBeginTicks;
        public long StableGoalInvocationTicks;
        public int StableGoalInvocationCount;
        public long StableGoalInvocationMaxTicks;
        public long StableGoalBodyMaxTicks;
        public long StableGoalPathRequestInvocationTicks;
        public int StableGoalPathRequestInvocationCount;
        public long StableGoalPathRequestInvocationMaxTicks;
        public long StableGoalPathRequestArgumentPreparationTicks;
        public int StableGoalPathRequestArgumentPreparationCount;
        public long StableGoalNavigationCommandInvocationTicks;
        public int StableGoalNavigationCommandInvocationCount;
        public long StableGoalNavigationCommandInvocationMaxTicks;
        public long StableGoalMeasurementFinalizeTicks;
        public long NavigationPathRequestQueueTicks;
        public long NavigationPathPolicyCommitTicks;
        public long NavigationPathBudgetConsumeTicks;
        public long NavigationPathRequestRemovalTicks;
        public long NavigationPathAllocatedBytes;
        public int NavigationPathGen0Collections;
        public int NavigationPathGen1Collections;
        public int NavigationPathGen2Collections;
        public long FixedPortalOwnerSnapshotTicks;
        public long FixedCorridorBuildTicks;
        public long FixedPortalParticipantRefreshTicks;
        public long FixedPortalOwnerResolveTicks;
        public long SharedGoalActiveEnqueueTicks;
        public long SharedGoalPruneTicks;
        public long SharedGoalProcessTicks;
        public long SuccessfulMoveDiagnosticTicks;
        public long NavigationConstraintTicks;
        public long NavigationConstraintWorldTicks;
        public long NavigationConstraintDirectTicks;
        public long NavigationConstraintCandidateTicks;
        public long NavigationConstraintBoundaryTicks;
        public long NavigationConstraintPrefixTicks;
        public long NavigationConstraintRecoveryTicks;
        public int FlowTileReferenceRefreshCalls;
        public int FlowTileReferenceKeyScans;
    }

}
