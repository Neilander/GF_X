using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using AAAGame.FlowPath;
using UnityEngine;
using Debug = UnityEngine.Debug;
using MainThreadFrameProfiler = UnityGameFramework.Runtime.MainThreadFrameProfiler;
using MainThreadPerfScope = UnityGameFramework.Runtime.MainThreadPerfScope;

public static partial class FlowFieldCrowdMovementSystem
{
    private static bool TryResolveStableGoalCellFixed(
        AgentRuntimeData agent,
        IEntityContext self,
        FixVector2 rawGoalPosition,
        out int goalX,
        out int goalY,
        out FixVector2 stableGoalPosition,
        out int finalGoalX,
        out int finalGoalY,
        out FixVector2 finalGoalPosition,
        out bool useSectorCorridorPolicy,
        out bool pendingProjection)
    {
        goalX = 0;
        goalY = 0;
        stableGoalPosition = rawGoalPosition;
        finalGoalX = 0;
        finalGoalY = 0;
        finalGoalPosition = rawGoalPosition;
        useSectorCorridorPolicy = false;
        pendingProjection = false;
        IEntityContext currentTarget = self?.TargetComp?.CurrentTarget;
        bool useRawGoal = currentTarget == null
                          || ReferenceEquals(currentTarget, self)
                          || !IsNavigationMovingTarget(currentTarget);
        bool hasMovingTarget = !useRawGoal;
        FixVector2 currentTargetFramePosition = default;
        if (!useRawGoal)
        {
            currentTargetFramePosition = currentTarget.LogicFramePositionFixed();
            FixVector2 targetOffset = rawGoalPosition - currentTargetFramePosition;
            Fix64 currentTargetExtent = ResolveNavigationTargetExtentFixed(currentTarget);
            Fix64 targetMatchDistance = Fix64.Max(currentTargetExtent * Fix64.FromRaw(3072), Fix64.FromRaw(1434));
            bool hasExplicitTargetGoal =
                FixVector2.SqrMagnitude(targetOffset) > targetMatchDistance * targetMatchDistance;
            useRawGoal = hasExplicitTargetGoal
                         && ShouldUseExactMovingTargetGoalFixed(self, rawGoalPosition);
        }

        if (useRawGoal && !hasMovingTarget)
        {
            if (!TryResolveReachableNavigationPointCellFixed(
                    self,
                    rawGoalPosition,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out int reachableGoalX,
                    out int reachableGoalY,
                    out FixVector2 reachableGoalWorld,
                    out _))
            {
                return false;
            }

            _perf.StableGoalRaw++;
            ClearStableGoal(agent);
            agent.NavState.StableGoalRawX = reachableGoalX;
            agent.NavState.StableGoalRawY = reachableGoalY;
            agent.NavState.StableGoalX = reachableGoalX;
            agent.NavState.StableGoalY = reachableGoalY;
            agent.NavState.StableGoalWorldFixed = reachableGoalWorld;
            agent.NavState.StableGoalWorld = ToWorldVector3(reachableGoalWorld);
            goalX = reachableGoalX;
            goalY = reachableGoalY;
            stableGoalPosition = reachableGoalWorld;
            finalGoalX = reachableGoalX;
            finalGoalY = reachableGoalY;
            finalGoalPosition = reachableGoalWorld;
            return true;
        }

        if (!_world.WorldToGridFixed(currentTargetFramePosition, out int anchorRawGoalX, out int anchorRawGoalY)
            || !_world.TryGetSectorId(anchorRawGoalX, anchorRawGoalY, out _)
            || !TryResolveStartCellForReachabilityFixed(self, out int startX, out int startY, out int startIsland))
        {
            return false;
        }

        int targetId = ResolveAgentId(currentTarget);
        int agentTypeId = ResolvePreferredAgentTypeId(agent.AgentTypeId);
        MovingTargetAnchorKey anchorKey = new MovingTargetAnchorKey(targetId, agentTypeId, startIsland);
        if (!MovingTargetAnchors.TryGetValue(anchorKey, out MovingTargetAnchor anchor))
        {
            anchor = new MovingTargetAnchor { Key = anchorKey };
            MovingTargetAnchors.Add(anchorKey, anchor);
        }
        anchor.LastUsedFrame = GetFrameCount();

        bool hasStableGoal = anchor.ActiveGoalX >= 0
                             && anchor.ActiveGoalY >= 0
                             && anchor.ActiveWorldVersion == _world.Version;
        bool hasCurrentProjection = anchor.ReachabilityWorldVersion == _world.Version
                                    && anchor.ReachabilityRawGoalX == anchorRawGoalX
                                    && anchor.ReachabilityRawGoalY == anchorRawGoalY;
        if (!hasCurrentProjection)
        {
            int rawGoalIsland = _world.IsWalkable(anchorRawGoalX, anchorRawGoalY)
                ? ResolveIslandId(_world, anchorRawGoalX, anchorRawGoalY)
                : -1;
            if (rawGoalIsland == startIsland)
            {
                CancelMovingTargetProjectionTask(anchor);
                PublishDirectMovingTargetProjection(
                    anchor,
                    _world,
                    anchorRawGoalX,
                    anchorRawGoalY,
                    currentTargetFramePosition);
                hasStableGoal = true;
            }
            else
            {
                RequestMovingTargetAnchorProjection(
                    anchor,
                    _world,
                    anchorRawGoalX,
                    anchorRawGoalY,
                    currentTargetFramePosition);
                pendingProjection = true;
                return false;
            }

            _perf.StableGoalRefreshCellDelta++;
        }
        else
        {
            _perf.StableGoalReuse++;
            _perf.StableGoalReachabilityReuse++;
        }

        agent.NavState.StableGoalTargetId = targetId;
        agent.NavState.StableGoalRawX = anchor.RawGoalX;
        agent.NavState.StableGoalRawY = anchor.RawGoalY;
        agent.NavState.StableGoalX = anchor.ActiveGoalX;
        agent.NavState.StableGoalY = anchor.ActiveGoalY;
        agent.NavState.StableGoalWorldFixed = anchor.ActiveGoalWorldFixed;
        agent.NavState.StableGoalWorld = anchor.ActiveGoalWorld;
        useSectorCorridorPolicy = true;
        goalX = anchor.ActiveGoalX;
        goalY = anchor.ActiveGoalY;
        stableGoalPosition = anchor.ActiveGoalWorldFixed;
        if (useRawGoal)
        {
            if (!TryResolveReachableNavigationPointCellFixed(
                    self,
                    rawGoalPosition,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out int reachableGoalX,
                    out int reachableGoalY,
                    out FixVector2 reachableGoalWorld,
                    out _))
            {
                return false;
            }

            _perf.StableGoalRaw++;
            finalGoalX = reachableGoalX;
            finalGoalY = reachableGoalY;
            finalGoalPosition = reachableGoalWorld;
            return true;
        }
        finalGoalX = goalX;
        finalGoalY = goalY;
        finalGoalPosition = stableGoalPosition;
        return true;
    }

    private static bool ShouldUseExactMovingTargetGoalFixed(
        IEntityContext self,
        FixVector2 rawGoalPosition)
    {
        if (!_world.WorldToGridFixed(rawGoalPosition, out int rawGoalX, out int rawGoalY)
            || !_world.TryGetSectorId(rawGoalX, rawGoalY, out _)
            || !TryResolveStartCellForReachabilityFixed(self, out int startX, out int startY, out _))
        {
            return true;
        }

        int cellDistance = Math.Max(Math.Abs(startX - rawGoalX), Math.Abs(startY - rawGoalY));
        return cellDistance <= checked(_world.SectorSizeInCells * 2);
    }

    private static bool TryResolveReachableNavigationPointCellFixed(
        IEntityContext self,
        FixVector2 rawGoalPosition,
        out int rawGoalX,
        out int rawGoalY,
        out int rawGoalSectorId,
        out int startX,
        out int startY,
        out int startIsland,
        out int reachableGoalX,
        out int reachableGoalY,
        out FixVector2 reachableGoalWorld,
        out int reachableGoalSectorId)
    {
        rawGoalX = 0;
        rawGoalY = 0;
        rawGoalSectorId = -1;
        startX = 0;
        startY = 0;
        startIsland = -1;
        reachableGoalX = 0;
        reachableGoalY = 0;
        reachableGoalWorld = rawGoalPosition;
        reachableGoalSectorId = -1;

        if (!_world.WorldToGridFixed(rawGoalPosition, out rawGoalX, out rawGoalY))
            return false;
        if (!_world.TryGetSectorId(rawGoalX, rawGoalY, out rawGoalSectorId))
            return false;
        if (!TryResolveStartCellForReachabilityFixed(self, out startX, out startY, out startIsland))
            return false;
        bool resolved = TryResolveReachableGoalCellFixed(
            _world,
            rawGoalPosition,
            rawGoalX,
            rawGoalY,
            startIsland,
            out reachableGoalX,
            out reachableGoalY,
            out reachableGoalWorld,
            out reachableGoalSectorId);
        if (resolved
            && (reachableGoalX != rawGoalX || reachableGoalY != rawGoalY)
            && IsMovementDiagnosticsEnabled())
        {
            Fix64 distance = FixVector2.Distance(rawGoalPosition, reachableGoalWorld);
            Debug.LogWarning(
                $"[FlowGoalResolvedToReachable] agent={self?.CharacterKey ?? "null"} rawGoalRaw=({rawGoalPosition.x.RawValue},{rawGoalPosition.y.RawValue}) " +
                $"rawCell=({rawGoalX},{rawGoalY}) start=({startX},{startY}) startIsland={startIsland} " +
                $"resolved=({reachableGoalX},{reachableGoalY}) resolvedRaw=({reachableGoalWorld.x.RawValue},{reachableGoalWorld.y.RawValue}) " +
                $"resolvedDistanceRaw={distance.RawValue} worldVersion={_world.Version}");
        }

        return resolved;
    }

    private static void SetMovingTargetActiveGoalFixed(
        NavigationWorld world,
        MovingTargetAnchor anchor,
        int rawGoalX,
        int rawGoalY,
        int goalX,
        int goalY,
        int goalSectorId,
        FixVector2 goalWorld)
    {
        if (world == null)
            throw new InvalidOperationException("SetMovingTargetActiveGoalFixed failed: world is null.");
        if (anchor == null)
            throw new InvalidOperationException("SetMovingTargetActiveGoalFixed failed: anchor is null.");

        anchor.RawGoalX = rawGoalX;
        anchor.RawGoalY = rawGoalY;
        anchor.ActiveGoalX = goalX;
        anchor.ActiveGoalY = goalY;
        anchor.ActiveGoalSectorId = goalSectorId;
        anchor.ActiveGoalWorldFixed = goalWorld;
        anchor.ActiveGoalWorld = ToWorldVector3(goalWorld);
        anchor.ActiveWorldVersion = world.Version;
    }

    private static void RequestMovingTargetAnchorProjection(
        MovingTargetAnchor anchor,
        NavigationWorld world,
        int rawGoalX,
        int rawGoalY,
        FixVector2 rawGoalWorld)
    {
        if (anchor == null)
            throw new InvalidOperationException("Moving-target projection request received a null anchor.");
        if (world == null)
            throw new InvalidOperationException("Moving-target projection request received a null world.");
        if (!world.WorldToGridFixed(rawGoalWorld, out int resolvedRawGoalX, out int resolvedRawGoalY)
            || resolvedRawGoalX != rawGoalX
            || resolvedRawGoalY != rawGoalY)
        {
            throw new InvalidOperationException(
                $"Moving-target projection request has inconsistent raw goal. key={anchor.Key.TargetId}/{anchor.Key.AgentTypeId}/{anchor.Key.IslandId} raw=({rawGoalX},{rawGoalY}).");
        }

        bool sameWorldRequest = anchor.HasPendingProjection
                                && anchor.PendingProjectionWorldVersion == world.Version;
        if (sameWorldRequest)
        {
            if (anchor.PendingProjectionRawGoalX == rawGoalX
                && anchor.PendingProjectionRawGoalY == rawGoalY)
                return;

            return;
        }

        if (anchor.HasPendingProjection)
            ResetMovingTargetProjectionTask(anchor);

        if (!anchor.HasPendingProjection)
        {
            anchor.HasPendingProjection = true;
            anchor.PendingProjectionWorldVersion = world.Version;
            anchor.PendingProjectionRawGoalX = rawGoalX;
            anchor.PendingProjectionRawGoalY = rawGoalY;
            anchor.PendingProjectionGoalWorldFixed = world.GridToWorldCenterFixed(rawGoalX, rawGoalY);
            anchor.PendingProjectionCellCursor = 0;
            anchor.PendingProjectionBestCellIndex = int.MaxValue;
            anchor.PendingProjectionBestDistanceSquared = Fix64.FromRaw(long.MaxValue);
            anchor.PendingProjectionLeafBucketId = -1;
            anchor.PendingProjectionLeafCellCursor = 0;
        }
        GoalProjectionSpatialIndex index = world.GoalProjectionSpatialIndex
            ?? throw new InvalidOperationException(
                $"Moving-target projection requested against a world without its published spatial index. world={world.Version}.");
        if (index.HierarchyNodes == null
            || index.HierarchyNodes.Length == 0
            || index.HierarchyRootNodeIndex < 0
            || index.HierarchyRootNodeIndex >= index.HierarchyNodes.Length)
        {
            throw new InvalidOperationException(
                $"Moving-target projection requested against a world without a published spatial hierarchy. world={world.Version}.");
        }
        PushMovingTargetProjectionNode(anchor, world, index, index.HierarchyRootNodeIndex);
        if (PendingMovingTargetProjectionKeys.Add(anchor.Key))
            InsertMovingTargetProjectionQueueKey(anchor.Key);
    }

    private static void PublishDirectMovingTargetProjection(
        MovingTargetAnchor anchor,
        NavigationWorld world,
        int rawGoalX,
        int rawGoalY,
        FixVector2 rawGoalWorld)
    {
        if (!world.IsWalkable(rawGoalX, rawGoalY)
            || ResolveIslandId(world, rawGoalX, rawGoalY) != anchor.Key.IslandId
            || !world.TryGetSectorId(rawGoalX, rawGoalY, out int goalSectorId))
        {
            throw new InvalidOperationException(
                $"Direct moving-target projection is not on the source island. key={anchor.Key.TargetId}/{anchor.Key.AgentTypeId}/{anchor.Key.IslandId} raw=({rawGoalX},{rawGoalY}).");
        }

        anchor.ReachabilityFrame = GetFrameCount();
        anchor.ReachabilityWorldVersion = world.Version;
        anchor.ReachabilityRawGoalX = rawGoalX;
        anchor.ReachabilityRawGoalY = rawGoalY;
        anchor.ReachabilityGoalX = rawGoalX;
        anchor.ReachabilityGoalY = rawGoalY;
        anchor.ReachabilityGoalSectorId = goalSectorId;
        anchor.ReachabilityGoalWorldFixed = rawGoalWorld;
        SetMovingTargetActiveGoalFixed(
            world,
            anchor,
            rawGoalX,
            rawGoalY,
            rawGoalX,
            rawGoalY,
            goalSectorId,
            rawGoalWorld);
        _perf.StableGoalRefreshInitial++;
    }

    private static void CancelMovingTargetProjectionTask(MovingTargetAnchor anchor)
    {
        if (anchor == null)
            throw new ArgumentNullException(nameof(anchor));
        if (!anchor.HasPendingProjection)
        {
            if (PendingMovingTargetProjectionKeys.Contains(anchor.Key))
                throw new InvalidOperationException("Moving-target projection key exists without pending anchor state.");
            return;
        }

        LinkedListNode<MovingTargetAnchorKey> node = MovingTargetProjectionQueue.First;
        while (node != null && !node.Value.Equals(anchor.Key))
            node = node.Next;
        if (node == null || !PendingMovingTargetProjectionKeys.Remove(anchor.Key))
            throw new InvalidOperationException("Moving-target projection queue is missing a pending anchor.");
        MovingTargetProjectionQueue.Remove(node);
        ResetMovingTargetProjectionTask(anchor);
    }

    private static void InsertMovingTargetProjectionQueueKey(MovingTargetAnchorKey key)
    {
        LinkedListNode<MovingTargetAnchorKey> node = MovingTargetProjectionQueue.First;
        while (node != null && CompareMovingTargetAnchorKeys(node.Value, key) <= 0)
            node = node.Next;
        if (node == null)
            MovingTargetProjectionQueue.AddLast(key);
        else
            MovingTargetProjectionQueue.AddBefore(node, key);
    }

    private static void ProcessMovingTargetProjectionQueue()
    {
        EnsureNavigationWorkBudgetActive();
        while (!IsNavigationWorkBudgetExhausted() && MovingTargetProjectionQueue.First != null)
        {
            LinkedListNode<MovingTargetAnchorKey> node = FindReadyMovingTargetProjectionNode();
            if (node == null)
                return;
            MovingTargetAnchorKey key = node.Value;
            MovingTargetAnchor anchor = MovingTargetAnchors[key];
            WorldRuntimeState state = WorldStates[key.AgentTypeId];

            NavigationWorld world = state.World;
            GoalProjectionSpatialIndex index = world.GoalProjectionSpatialIndex
                ?? throw new InvalidOperationException(
                    $"Moving-target projection consumed world without index. world={world.Version}.");
            if (key.IslandId <= 0
                || index.IslandCellIndices == null
                || key.IslandId >= index.IslandCellIndices.Length)
            {
                throw new InvalidOperationException(
                    $"Moving-target projection has invalid source island. world={world.Version} island={key.IslandId}.");
            }

            if (ProcessMovingTargetProjectionTask(anchor, world, index))
                PublishCompletedMovingTargetProjection(node, anchor, world);
        }
    }

    private static LinkedListNode<MovingTargetAnchorKey> FindReadyMovingTargetProjectionNode()
    {
        LinkedListNode<MovingTargetAnchorKey> node = MovingTargetProjectionQueue.First;
        while (node != null)
        {
            LinkedListNode<MovingTargetAnchorKey> next = node.Next;
            MovingTargetAnchorKey key = node.Value;
            if (!MovingTargetAnchors.TryGetValue(key, out MovingTargetAnchor anchor) || anchor == null)
                throw new InvalidOperationException("Moving-target projection queue references a missing anchor.");
            if (!anchor.HasPendingProjection)
                throw new InvalidOperationException("Moving-target projection queue references an anchor without pending authority.");
            if (!WorldStates.TryGetValue(key.AgentTypeId, out WorldRuntimeState state) || state?.World == null)
                throw new InvalidOperationException("Moving-target projection queue references an unavailable world state.");

            if (state.IsDirty || state.BuildJob != null || state.RuntimeDirtyJob != null
                || state.World.Version != anchor.PendingProjectionWorldVersion)
            {
                // A projection may only publish against the exact immutable
                // world it started from. Discard it explicitly; the next
                // demand against the newly published world creates its task.
                ResetMovingTargetProjectionTask(anchor);
                MovingTargetProjectionQueue.Remove(node);
                if (!PendingMovingTargetProjectionKeys.Remove(key))
                    throw new InvalidOperationException("Moving-target projection queue index is inconsistent while invalidating a stale task.");
            }
            else
            {
                return node;
            }
            node = next;
        }
        return null;
    }

    private static bool ProcessMovingTargetProjectionTask(
        MovingTargetAnchor anchor,
        NavigationWorld world,
        GoalProjectionSpatialIndex index)
    {
        if (index.HierarchyNodes == null || index.HierarchyNodes.Length == 0)
            throw new InvalidOperationException("Moving-target projection consumed a spatial index without hierarchy nodes.");
        while (!IsNavigationWorkBudgetExhausted())
        {
            if (anchor.PendingProjectionLeafBucketId >= 0)
            {
                GoalProjectionBucket bucket = index.Buckets[anchor.PendingProjectionLeafBucketId]
                    ?? throw new InvalidOperationException("Moving-target projection leaf references a null bucket.");
                if (bucket.CellIndices == null || bucket.IslandIds == null || bucket.CellIndices.Length != bucket.IslandIds.Length)
                    throw new InvalidOperationException("Moving-target projection leaf bucket has invalid storage.");
                while (anchor.PendingProjectionLeafCellCursor < bucket.CellIndices.Length)
                {
                    int candidateCursor = anchor.PendingProjectionLeafCellCursor++;
                    if (bucket.IslandIds[candidateCursor] == anchor.Key.IslandId)
                    {
                        int cellIndex = bucket.CellIndices[candidateCursor];
                        int x = cellIndex % world.Width;
                        int y = cellIndex / world.Width;
                        FixVector2 center = world.GridToWorldCenterFixed(x, y);
                        Fix64 dx = center.x - anchor.PendingProjectionGoalWorldFixed.x;
                        Fix64 dz = center.y - anchor.PendingProjectionGoalWorldFixed.y;
                        Fix64 distanceSquared = dx * dx + dz * dz;
                        if (distanceSquared < anchor.PendingProjectionBestDistanceSquared
                            || (distanceSquared == anchor.PendingProjectionBestDistanceSquared
                                && cellIndex < anchor.PendingProjectionBestCellIndex))
                        {
                            anchor.PendingProjectionBestDistanceSquared = distanceSquared;
                            anchor.PendingProjectionBestCellIndex = cellIndex;
                        }
                    }
                    anchor.PendingProjectionCellCursor++;
                    if (IsBudgetExpired(0L, anchor.PendingProjectionCellCursor))
                        return false;
                }
                anchor.PendingProjectionLeafBucketId = -1;
                anchor.PendingProjectionLeafCellCursor = 0;
                continue;
            }

            if (anchor.PendingProjectionNodeHeap.Count == 0)
                return true;
            PopMovingTargetProjectionNode(anchor, out int nodeIndex, out Fix64 lowerBoundSquared);
            if (anchor.PendingProjectionBestCellIndex != int.MaxValue
                && lowerBoundSquared > anchor.PendingProjectionBestDistanceSquared)
            {
                anchor.PendingProjectionNodeHeap.Clear();
                anchor.PendingProjectionNodeLowerBounds.Clear();
                return true;
            }

            GoalProjectionHierarchyNode node = index.HierarchyNodes[nodeIndex]
                ?? throw new InvalidOperationException("Moving-target projection hierarchy contains a null node.");
            if (node.BucketId >= 0)
            {
                anchor.PendingProjectionLeafBucketId = node.BucketId;
                anchor.PendingProjectionLeafCellCursor = 0;
            }
            else
            {
                PushMovingTargetProjectionChildIfRelevant(anchor, world, index, node.Child0);
                PushMovingTargetProjectionChildIfRelevant(anchor, world, index, node.Child1);
                PushMovingTargetProjectionChildIfRelevant(anchor, world, index, node.Child2);
                PushMovingTargetProjectionChildIfRelevant(anchor, world, index, node.Child3);
            }
            if (IsBudgetExpired(0L, nodeIndex))
                return false;
        }
        return false;
    }

    private static void PushMovingTargetProjectionChildIfRelevant(
        MovingTargetAnchor anchor,
        NavigationWorld world,
        GoalProjectionSpatialIndex index,
        int childNodeIndex)
    {
        if (childNodeIndex < 0)
            return;
        GoalProjectionHierarchyNode child = index.HierarchyNodes[childNodeIndex]
            ?? throw new InvalidOperationException("Moving-target projection hierarchy contains a null child.");
        if (ContainsGoalProjectionIslandId(child.IslandIds, anchor.Key.IslandId))
            PushMovingTargetProjectionNode(anchor, world, index, childNodeIndex);
    }

    private static void PushMovingTargetProjectionNode(
        MovingTargetAnchor anchor,
        NavigationWorld world,
        GoalProjectionSpatialIndex index,
        int nodeIndex)
    {
        GoalProjectionHierarchyNode node = index.HierarchyNodes[nodeIndex]
            ?? throw new InvalidOperationException("Moving-target projection hierarchy contains a null node.");
        if (!ContainsGoalProjectionIslandId(node.IslandIds, anchor.Key.IslandId))
            return;
        Fix64 lowerBoundSquared = GetGoalProjectionHierarchyNodeLowerBoundSquared(
            world,
            node,
            anchor.PendingProjectionGoalWorldFixed);
        int heapIndex = anchor.PendingProjectionNodeHeap.Count;
        anchor.PendingProjectionNodeHeap.Add(nodeIndex);
        anchor.PendingProjectionNodeLowerBounds.Add(lowerBoundSquared);
        while (heapIndex > 0)
        {
            int parent = (heapIndex - 1) / 2;
            if (CompareGoalProjectionHeapNodes(
                    anchor.PendingProjectionNodeLowerBounds[parent], anchor.PendingProjectionNodeHeap[parent],
                    lowerBoundSquared, nodeIndex) <= 0)
            {
                break;
            }
            anchor.PendingProjectionNodeHeap[heapIndex] = anchor.PendingProjectionNodeHeap[parent];
            anchor.PendingProjectionNodeLowerBounds[heapIndex] = anchor.PendingProjectionNodeLowerBounds[parent];
            heapIndex = parent;
        }
        anchor.PendingProjectionNodeHeap[heapIndex] = nodeIndex;
        anchor.PendingProjectionNodeLowerBounds[heapIndex] = lowerBoundSquared;
    }

    private static void PopMovingTargetProjectionNode(
        MovingTargetAnchor anchor,
        out int nodeIndex,
        out Fix64 lowerBoundSquared)
    {
        int count = anchor.PendingProjectionNodeHeap.Count;
        if (count <= 0 || anchor.PendingProjectionNodeLowerBounds.Count != count)
            throw new InvalidOperationException("Moving-target projection node heap is invalid.");
        nodeIndex = anchor.PendingProjectionNodeHeap[0];
        lowerBoundSquared = anchor.PendingProjectionNodeLowerBounds[0];
        int lastIndex = count - 1;
        int lastNode = anchor.PendingProjectionNodeHeap[lastIndex];
        Fix64 lastBound = anchor.PendingProjectionNodeLowerBounds[lastIndex];
        anchor.PendingProjectionNodeHeap.RemoveAt(lastIndex);
        anchor.PendingProjectionNodeLowerBounds.RemoveAt(lastIndex);
        if (lastIndex == 0)
            return;

        int heapIndex = 0;
        while (true)
        {
            int left = heapIndex * 2 + 1;
            if (left >= lastIndex)
                break;
            int right = left + 1;
            int child = right < lastIndex
                        && CompareGoalProjectionHeapNodes(
                            anchor.PendingProjectionNodeLowerBounds[right], anchor.PendingProjectionNodeHeap[right],
                            anchor.PendingProjectionNodeLowerBounds[left], anchor.PendingProjectionNodeHeap[left]) < 0
                ? right
                : left;
            if (CompareGoalProjectionHeapNodes(
                    lastBound, lastNode,
                    anchor.PendingProjectionNodeLowerBounds[child], anchor.PendingProjectionNodeHeap[child]) <= 0)
            {
                break;
            }
            anchor.PendingProjectionNodeHeap[heapIndex] = anchor.PendingProjectionNodeHeap[child];
            anchor.PendingProjectionNodeLowerBounds[heapIndex] = anchor.PendingProjectionNodeLowerBounds[child];
            heapIndex = child;
        }
        anchor.PendingProjectionNodeHeap[heapIndex] = lastNode;
        anchor.PendingProjectionNodeLowerBounds[heapIndex] = lastBound;
    }

    private static bool ContainsGoalProjectionIslandId(int[] islandIds, int islandId)
    {
        return islandIds != null && Array.BinarySearch(islandIds, islandId) >= 0;
    }

    private static Fix64 GetGoalProjectionHierarchyNodeLowerBoundSquared(
        NavigationWorld world,
        GoalProjectionHierarchyNode node,
        FixVector2 desiredWorld)
    {
        int minX = node.MinBucketX * GoalProjectionSpatialIndex.BucketSizeInCells;
        int minY = node.MinBucketY * GoalProjectionSpatialIndex.BucketSizeInCells;
        int maxX = Math.Min(world.Width - 1, (node.MinBucketX + node.BucketSpan) * GoalProjectionSpatialIndex.BucketSizeInCells - 1);
        int maxY = Math.Min(world.Height - 1, (node.MinBucketY + node.BucketSpan) * GoalProjectionSpatialIndex.BucketSizeInCells - 1);
        if (minX > maxX || minY > maxY)
            throw new InvalidOperationException("Moving-target projection hierarchy node with members lies outside the world.");
        world.GetGridCellBoundsFixed(minX, minY, out FixVector2 minimum, out _);
        world.GetGridCellBoundsFixed(maxX, maxY, out _, out FixVector2 maximum);
        Fix64 dx = desiredWorld.x < minimum.x
            ? minimum.x - desiredWorld.x
            : desiredWorld.x > maximum.x ? desiredWorld.x - maximum.x : Fix64.Zero;
        Fix64 dz = desiredWorld.y < minimum.y
            ? minimum.y - desiredWorld.y
            : desiredWorld.y > maximum.y ? desiredWorld.y - maximum.y : Fix64.Zero;
        return dx * dx + dz * dz;
    }

    private static void PublishCompletedMovingTargetProjection(
        LinkedListNode<MovingTargetAnchorKey> queueNode,
        MovingTargetAnchor anchor,
        NavigationWorld world)
    {
        int bestCellIndex = anchor.PendingProjectionBestCellIndex;
        if (bestCellIndex < 0 || bestCellIndex == int.MaxValue)
            throw new InvalidOperationException("Moving-target projection completed without a candidate.");
        int bestX = bestCellIndex % world.Width;
        int bestY = bestCellIndex / world.Width;
        if (!world.TryGetSectorId(bestX, bestY, out int bestSectorId))
            throw new InvalidOperationException("Moving-target projection selected a cell without a sector.");

        anchor.ReachabilityFrame = GetFrameCount();
        anchor.ReachabilityWorldVersion = world.Version;
        anchor.ReachabilityRawGoalX = anchor.PendingProjectionRawGoalX;
        anchor.ReachabilityRawGoalY = anchor.PendingProjectionRawGoalY;
        anchor.ReachabilityGoalX = bestX;
        anchor.ReachabilityGoalY = bestY;
        anchor.ReachabilityGoalSectorId = bestSectorId;
        anchor.ReachabilityGoalWorldFixed = world.GridToWorldCenterFixed(bestX, bestY);
        SetMovingTargetActiveGoalFixed(
            world,
            anchor,
            anchor.PendingProjectionRawGoalX,
            anchor.PendingProjectionRawGoalY,
            bestX,
            bestY,
            bestSectorId,
            anchor.ReachabilityGoalWorldFixed);
        _perf.StableGoalRefreshInitial++;
        ResetMovingTargetProjectionTask(anchor);
        MovingTargetProjectionQueue.Remove(queueNode);
        if (!PendingMovingTargetProjectionKeys.Remove(anchor.Key))
            throw new InvalidOperationException("Moving-target projection queue index is inconsistent.");
    }

    private static void ResetMovingTargetProjectionTask(MovingTargetAnchor anchor)
    {
        anchor.HasPendingProjection = false;
        anchor.PendingProjectionWorldVersion = -1;
        anchor.PendingProjectionRawGoalX = -1;
        anchor.PendingProjectionRawGoalY = -1;
        anchor.PendingProjectionCellCursor = 0;
        anchor.PendingProjectionBestCellIndex = int.MaxValue;
        anchor.PendingProjectionBestDistanceSquared = Fix64.FromRaw(long.MaxValue);
        anchor.PendingProjectionLeafBucketId = -1;
        anchor.PendingProjectionLeafCellCursor = 0;
        anchor.PendingProjectionNodeHeap.Clear();
        anchor.PendingProjectionNodeLowerBounds.Clear();
    }

    private static bool TryResolveStartCellForReachabilityFixed(IEntityContext self, out int startX, out int startY, out int startIsland)
    {
        startX = 0;
        startY = 0;
        startIsland = -1;
        if (self == null || _world == null)
            return false;

        FixVector2 selfFramePosition = self.LogicFramePositionFixed();
        if (!_world.WorldToGridFixed(selfFramePosition, out startX, out startY))
            return false;

        if (!_world.IsWalkable(startX, startY)
            && !TryResolveNearbyStartWalkableFixed(_world, selfFramePosition, startX, startY, out startX, out startY))
        {
            return false;
        }

        startIsland = ResolveIslandIdForDiagnostics(_world, startX, startY);
        return startIsland > 0;
    }


    private static string BuildReachabilityStartDiagnostics(IEntityContext self)
    {
        if (self == null)
            return "self=null";
        Vector3 selfFramePosition = self.LogicFramePosition();
        if (_world == null)
            return $"pos={selfFramePosition} world=null";
        if (!_world.WorldToGrid(selfFramePosition, out int rawX, out int rawY))
            return $"pos={selfFramePosition} outsideGrid";

        bool rawWalkable = _world.IsWalkable(rawX, rawY);
        int rawIsland = rawWalkable ? ResolveIslandIdForDiagnostics(_world, rawX, rawY) : -1;
        bool resolved = TryResolveNearbyStartWalkable(_world, selfFramePosition, rawX, rawY, out int resolvedX, out int resolvedY);
        int resolvedIsland = resolved ? ResolveIslandIdForDiagnostics(_world, resolvedX, resolvedY) : -1;
        float radius = ResolveCollisionRadius(self);
        float requestedClearance = ResolveNavigationSegmentClearance(_world, radius);
        float clearance = ResolveNavigationQueryClearance(_world, requestedClearance);
        float violation = ResolveNavigationClearanceViolation(_world, selfFramePosition, clearance, includeRuntimeObstacleOverlay: true);
        return $"pos={selfFramePosition} raw=({rawX},{rawY}) rawWalk={rawWalkable} rawIsland={rawIsland} " +
               $"resolved={resolved} resolvedCell=({resolvedX},{resolvedY}) resolvedWalk={(resolved && _world.IsWalkable(resolvedX, resolvedY))} resolvedIsland={resolvedIsland} " +
               $"agentType={_world.AgentTypeId} radius={radius:F3} requestedClearance={requestedClearance:F3} queryClearance={clearance:F3} violation={(float.IsPositiveInfinity(violation) ? "INF" : violation.ToString("F3"))} " +
               $"worldVersion={_world.Version} islandCount={_world.IslandCount} mainIsland={_world.MainIslandId}:{_world.MainIslandSize} " +
               $"grid={FormatGridSampleDiagnostics(selfFramePosition)} obstacles={BuildNearbyObstacleDiagnostics(selfFramePosition, selfFramePosition, _world.AgentTypeId)} " +
               $"neighborhood={BuildIslandNeighborhoodDiagnostics(_world, rawX, rawY, 2)}";
    }

    private static bool TryResolveReachableNavigationQueryCellsFixed(
        NavigationWorld world,
        FixVector2 from,
        FixVector2 rawGoal,
        out int startX,
        out int startY,
        out int goalX,
        out int goalY,
        out FixVector2 reachableGoal,
        out string failureReason)
    {
        if (world == null)
            throw new InvalidOperationException("TryResolveReachableNavigationQueryCellsFixed failed: world is null.");

        startX = 0;
        startY = 0;
        goalX = 0;
        goalY = 0;
        reachableGoal = rawGoal;
        failureReason = string.Empty;
        if (!world.WorldToGridFixed(from, out startX, out startY))
        {
            failureReason = $"start is outside authored grid raw=({from.x.RawValue},{from.y.RawValue})";
            return false;
        }
        if (!world.IsWalkable(startX, startY)
            && !TryResolveNearbyStartWalkableFixed(world, from, startX, startY, out startX, out startY))
        {
            failureReason =
                $"start has no nearby walkable cell raw=({from.x.RawValue},{from.y.RawValue}) cell=({startX},{startY})";
            return false;
        }

        int startIsland = ResolveIslandIdForDiagnostics(world, startX, startY);
        if (startIsland <= 0)
        {
            failureReason = $"start cell has no navigation island cell=({startX},{startY})";
            return false;
        }
        if (!world.WorldToGridFixed(rawGoal, out int rawGoalX, out int rawGoalY))
        {
            failureReason = $"goal is outside authored grid raw=({rawGoal.x.RawValue},{rawGoal.y.RawValue})";
            return false;
        }
        if (!TryResolveReachableGoalCellFixed(
                world,
                rawGoal,
                rawGoalX,
                rawGoalY,
                startIsland,
                out goalX,
                out goalY,
                out reachableGoal,
                out _))
        {
            failureReason =
                $"goal has no reachable cell on start island raw=({rawGoal.x.RawValue},{rawGoal.y.RawValue}) " +
                $"rawCell=({rawGoalX},{rawGoalY}) start=({startX},{startY}) startIsland={startIsland}";
            return false;
        }

        return true;
    }

    private static bool TryResolveReachableGoalCellFixed(
        NavigationWorld world,
        FixVector2 rawGoalPosition,
        int rawGoalX,
        int rawGoalY,
        int startIsland,
        out int goalX,
        out int goalY,
        out FixVector2 goalWorld,
        out int goalSectorId)
    {
        if (world == null)
            throw new InvalidOperationException("TryResolveReachableGoalCellFixed failed: world is null.");

        goalX = rawGoalX;
        goalY = rawGoalY;
        goalWorld = rawGoalPosition;
        goalSectorId = -1;

        int rawGoalIsland = ResolveIslandIdForDiagnostics(world, rawGoalX, rawGoalY);
        if (world.IsWalkable(rawGoalX, rawGoalY) && rawGoalIsland == startIsland)
        {
            goalWorld = world.GridToWorldCenterFixed(rawGoalX, rawGoalY);
            return world.TryGetSectorId(goalX, goalY, out goalSectorId);
        }

        if (!TryFindNearestWalkableInIslandByWorldDistanceFixed(
                world,
                rawGoalX,
                rawGoalY,
                rawGoalPosition,
                startIsland,
                radius: 8,
                allowFullIslandSearch: true,
                out goalX,
                out goalY,
                out _))
        {
            return false;
        }

        goalWorld = world.GridToWorldCenterFixed(goalX, goalY);
        return world.TryGetSectorId(goalX, goalY, out goalSectorId);
    }


    private static string BuildReachabilityTargetDiagnostics(IEntityContext self, Vector3 rawGoalPosition, int startX, int startY)
    {
        IEntityContext target = self?.TargetComp?.CurrentTarget;
        if (target == null)
            return "target=null";

        Vector3 targetFramePosition = target.LogicFramePosition();
        bool targetInGrid = _world.WorldToGrid(targetFramePosition, out int targetX, out int targetY);
        int targetIsland = targetInGrid ? ResolveIslandIdForDiagnostics(_world, targetX, targetY) : -1;
        bool targetIsMainIsland = targetIsland > 0 && targetIsland == _world.MainIslandId;
        float rawTargetDistance = HorizontalDistanceXZ(rawGoalPosition, targetFramePosition);

        Vector3 startPosition = _world.GridToWorldCenter(startX, startY);
        string flowPath = BuildGridPathDiagnostics(startPosition, targetFramePosition, _world.AgentTypeId);

        return $"target={{key={target.CharacterKey},pos={targetFramePosition},inGrid={targetInGrid},cell=({targetX},{targetY}),island={targetIsland},main={targetIsMainIsland},rawDist={rawTargetDistance:F3},{flowPath}}}";
    }

    private static void TrimMovingTargetAnchors()
    {
        if (MovingTargetAnchors.Count == 0)
            return;

        int expireBeforeFrame = GetFrameCount() - 300;
        List<MovingTargetAnchorKey> expiredIds = null;
        bool referencedKeysBuilt = false;
        foreach (KeyValuePair<MovingTargetAnchorKey, MovingTargetAnchor> pair in MovingTargetAnchors)
        {
            if (pair.Value.LastUsedFrame >= expireBeforeFrame)
                continue;

            if (!referencedKeysBuilt)
            {
                BuildReferencedMovingTargetAnchorKeySet();
                referencedKeysBuilt = true;
            }
            if (ReferencedMovingTargetAnchorKeysScratch.Contains(pair.Key))
                continue;

            expiredIds ??= new List<MovingTargetAnchorKey>();
            expiredIds.Add(pair.Key);
        }

        ReferencedMovingTargetAnchorKeysScratch.Clear();
        if (expiredIds == null)
            return;

        for (int i = 0; i < expiredIds.Count; i++)
        {
            MovingTargetAnchor anchor = MovingTargetAnchors[expiredIds[i]]
                                        ?? throw new InvalidOperationException("TrimMovingTargetAnchors encountered a null expired anchor.");
            if (anchor.HasPinnedSectorCorridorPolicy)
                RemoveSectorCorridorPolicy(anchor.PinnedSectorCorridorPolicyKey);
            if (!MovingTargetAnchors.Remove(expiredIds[i]))
                throw new InvalidOperationException($"TrimMovingTargetAnchors failed to remove target={expiredIds[i].TargetId}.");
        }
    }

    private static void BuildReferencedMovingTargetAnchorKeySet()
    {
        ReferencedMovingTargetAnchorKeysScratch.Clear();
        if (_world == null)
            return;

        foreach (NavigationPathRequestJob job in PendingNavigationPathRequests.Values)
        {
            if (job == null)
                throw new InvalidOperationException("Pending navigation path request index contains a null job during anchor ownership collection.");
            if (job.Key.MovingTargetId == int.MinValue)
                continue;
            ReferencedMovingTargetAnchorKeysScratch.Add(new MovingTargetAnchorKey(
                job.Key.MovingTargetId,
                job.Key.AgentTypeId,
                job.Key.SourceIslandId));
        }

        foreach (AgentRuntimeData agent in Agents.Values)
        {
            if (agent == null || agent.NavState.StableGoalTargetId == int.MinValue)
                continue;
            if (!IsAgentInCurrentNavigationWorld(agent))
                continue;

            int islandId = ResolveIslandIdForDiagnostics(_world, agent.NavState.CurrentCell.x, agent.NavState.CurrentCell.y);
            if (islandId <= 0)
                continue;

            ReferencedMovingTargetAnchorKeysScratch.Add(new MovingTargetAnchorKey(
                agent.NavState.StableGoalTargetId,
                ResolvePreferredAgentTypeId(agent.AgentTypeId),
                islandId));
        }
    }

    private static void TrimCombatTargetSlotCache()
    {
        if (CombatTargetSlotCache.Count == 0)
            return;

        int expireBeforeFrame = GetFrameCount() - CombatTargetSlotCacheFrameLifetime;
        List<CombatTargetSlotKey> expiredKeys = null;
        foreach (KeyValuePair<CombatTargetSlotKey, CombatTargetSlotEntry> pair in CombatTargetSlotCache)
        {
            if (pair.Value != null && pair.Value.LastUsedFrame >= expireBeforeFrame)
                continue;

            expiredKeys ??= new List<CombatTargetSlotKey>();
            expiredKeys.Add(pair.Key);
        }

        if (expiredKeys == null)
            return;

        for (int i = 0; i < expiredKeys.Count; i++)
            CombatTargetSlotCache.Remove(expiredKeys[i]);
    }

    private static void ClearStableGoal(AgentRuntimeData agent)
    {
        if (agent == null)
            return;

        agent.NavState.StableGoalTargetId = int.MinValue;
        agent.NavState.StableGoalX = -1;
        agent.NavState.StableGoalY = -1;
        agent.NavState.StableGoalWorld = Vector3.zero;
        agent.NavState.StableGoalWorldFixed = FixVector2.Zero;
    }

    private static bool IsNavigationMovingTarget(IEntityContext target)
    {
        if (target == null)
            return false;

        IMoveComp moveComp = target.MoveComp;
        if (moveComp != null)
            return moveComp.GetType() != typeof(NoMoveComp);

        return target.MoveExecutor != null;
    }

    private static FixVector2 ResolveNavigationGoalOccupancyFixed(
        IEntityContext self,
        AgentRuntimeData agent,
        FixVector2 goalPosition)
    {
        if (self == null || agent == null)
            throw new InvalidOperationException("ResolveNavigationGoalOccupancyFixed failed: self or agent is null.");
        if (_world == null)
            throw new InvalidOperationException("ResolveNavigationGoalOccupancyFixed failed: world is null.");

        int frame = GetFrameCount();
        if (_navigationGoalReservationFrame != frame)
        {
            NavigationGoalReservations.Clear();
            _navigationGoalReservationFrame = frame;
        }
        SyncRegisteredAgentSpatialState(frame);

        FixVector2 selfFramePosition = self.LogicFramePositionFixed();
        int selfId = ResolveAgentId(self);
        if (HasExactNavigationGoalReservationFixed(selfId, goalPosition))
            return goalPosition;

        Fix64 selfRadius = Fix64.Max(ResolveCollisionRadiusFixed(self), agent.RadiusFixed);
        Fix64 requiredDistance = Fix64.Max(
            selfRadius * (Fix64)2 + NavigationGoalOccupancyPadding,
            selfRadius + Fix64.FromRaw(820));
        int ignoredTargetId = ResolveIgnoredGoalOccupancyTargetIdFixed(self, goalPosition);
        Fix64 distanceToGoal = FixVector2.Magnitude(goalPosition - selfFramePosition);
        Fix64 cellSize = _world.CellSizeFixed;
        Fix64 blockingActivationDistance = Fix64.Max(requiredDistance * (Fix64)4, cellSize * (Fix64)5);
        Fix64 reservationActivationDistance = Fix64.Max(requiredDistance * (Fix64)2, cellSize * (Fix64)2);
        if (distanceToGoal > blockingActivationDistance)
            return goalPosition;

        bool useReservations = distanceToGoal <= reservationActivationDistance;
        if (IsNavigationGoalOccupiedByOtherFixed(
                selfId,
                ignoredTargetId,
                goalPosition,
                requiredDistance,
                useReservations,
                out _))
        {
            return ResolveRotatedNearbyGoalFixed(
                self,
                goalPosition,
                ignoredTargetId,
                selfRadius,
                requiredDistance,
                preferLateral: true);
        }

        RegisterNavigationGoalReservationFixed(selfId, goalPosition, requiredDistance);
        return goalPosition;
    }

    private static int ResolveIgnoredGoalOccupancyTargetIdFixed(IEntityContext self, FixVector2 goalPosition)
    {
        IEntityContext currentTarget = self.TargetComp?.CurrentTarget;
        if (currentTarget == null)
            return 0;

        FixVector2 offset = goalPosition - currentTarget.LogicFramePositionFixed();
        Fix64 targetExtent = ResolveNavigationTargetExtentFixed(currentTarget);
        Fix64 threshold = Fix64.Max(targetExtent * Fix64.FromRaw(3072), Fix64.FromRaw(1434));
        return FixVector2.SqrMagnitude(offset) > threshold * threshold
            ? ResolveAgentId(currentTarget)
            : 0;
    }

    private static FixVector2 ResolveRotatedNearbyGoalFixed(
        IEntityContext self,
        FixVector2 goalPosition,
        int targetId,
        Fix64 selfRadius,
        Fix64 requiredDistance,
        bool preferLateral)
    {
        if (!_world.WorldToGridFixed(goalPosition, out int goalX, out int goalY)
            || !_world.TryGetSectorId(goalX, goalY, out _))
        {
            return goalPosition;
        }

        int goalIsland = ResolveIslandIdForDiagnostics(_world, goalX, goalY);
        if (goalIsland <= 0)
            return goalPosition;

        FixVector2 selfFramePosition = self.LogicFramePositionFixed();
        FixVector2 baseDirection = selfFramePosition - goalPosition;
        if (FixVector2.SqrMagnitude(baseDirection) <= Fix64.Zero)
            baseDirection = new FixVector2(Fix64.Zero, Fix64.One);
        else
            baseDirection = baseDirection.GetNormalized();

        FixVector2 bestPoint = goalPosition;
        int bestPreferenceClass = int.MaxValue;
        int bestRing = int.MaxValue;
        Fix64 bestTravelDistance = Fix64.FromRaw(long.MaxValue);
        int candidateOrderStart = preferLateral ? 1 : 0;
        for (int ring = 0; ring < NavigationGoalRingCount; ring++)
        {
            Fix64 radius = Fix64.Max(
                               selfRadius + Fix64.FromRaw(205),
                               requiredDistance - selfRadius * Fix64.FromRaw(2048))
                           + (Fix64)ring * Fix64.Max(Fix64.FromRaw(1434), selfRadius * Fix64.FromRaw(1434));
            for (int i = 0; i < NavigationGoalCandidateCount; i++)
            {
                int candidateIndex = (candidateOrderStart + i) % NavigationGoalCandidateCount;
                Fix64 angleDegrees = ResolveNavigationGoalAngleFixed(candidateIndex);
                Fix64 radians = angleDegrees * Fix64.PI / (Fix64)180;
                Fix64 sin = Fix64.Sin(radians);
                Fix64 cos = Fix64.Cos(radians);
                FixVector2 direction = new FixVector2(
                    baseDirection.x * cos - baseDirection.y * sin,
                    baseDirection.x * sin + baseDirection.y * cos);
                FixVector2 candidate = goalPosition + direction * radius;
                if (!_world.WorldToGridFixed(candidate, out int candidateX, out int candidateY)
                    || !_world.IsWalkable(candidateX, candidateY)
                    || ResolveIslandIdForDiagnostics(_world, candidateX, candidateY) != goalIsland)
                {
                    continue;
                }

                if (!TryResolveReachableNavigationPointCellFixed(
                        self,
                        candidate,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _,
                        out int resolvedX,
                        out int resolvedY,
                        out FixVector2 resolvedWorld,
                        out _))
                {
                    continue;
                }
                if (ResolveIslandIdForDiagnostics(_world, resolvedX, resolvedY) != goalIsland
                    || !IsNavigationPointClearFixed(
                        _world,
                        resolvedWorld,
                        ResolveNavigationQueryClearanceFixed(_world, selfRadius),
                        includeRuntimeObstacleOverlay: true)
                    || IsNavigationGoalOccupiedByOtherFixed(
                        ResolveAgentId(self),
                        targetId,
                        resolvedWorld,
                        requiredDistance,
                        includeReservations: true,
                        out _))
                {
                    continue;
                }

                FixVector2 resolvedGoalOffset = resolvedWorld - goalPosition;
                Fix64 lateralArea = baseDirection.x * resolvedGoalOffset.y
                                    - baseDirection.y * resolvedGoalOffset.x;
                int preferenceClass = preferLateral && lateralArea == Fix64.Zero ? 1 : 0;
                Fix64 travelDistance = FixVector2.Magnitude(resolvedWorld - selfFramePosition);
                if (preferenceClass > bestPreferenceClass
                    || (preferenceClass == bestPreferenceClass && ring > bestRing)
                    || (preferenceClass == bestPreferenceClass && ring == bestRing && travelDistance >= bestTravelDistance))
                    continue;

                bestPreferenceClass = preferenceClass;
                bestRing = ring;
                bestTravelDistance = travelDistance;
                bestPoint = resolvedWorld;
            }
        }

        RegisterNavigationGoalReservationFixed(ResolveAgentId(self), bestPoint, requiredDistance);
        return bestPoint;
    }

    private static void RegisterNavigationGoalReservationFixed(int selfId, FixVector2 point, Fix64 requiredDistance)
    {
        int frame = GetFrameCount();
        if (_navigationGoalReservationFrame != frame)
        {
            NavigationGoalReservations.Clear();
            _navigationGoalReservationFrame = frame;
        }

        for (int i = NavigationGoalReservations.Count - 1; i >= 0; i--)
        {
            if (NavigationGoalReservations[i].SelfId == selfId)
                NavigationGoalReservations.RemoveAt(i);
        }
        NavigationGoalReservations.Add(new NavigationGoalReservation(selfId, point, requiredDistance));
    }

    private static bool HasExactNavigationGoalReservationFixed(int selfId, FixVector2 point)
    {
        for (int i = 0; i < NavigationGoalReservations.Count; i++)
        {
            NavigationGoalReservation reservation = NavigationGoalReservations[i];
            if (reservation.SelfId == selfId
                && reservation.Point.x.RawValue == point.x.RawValue
                && reservation.Point.y.RawValue == point.y.RawValue)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNavigationGoalOccupiedByOtherFixed(
        int selfId,
        int ignoredAgentId,
        FixVector2 position,
        Fix64 requiredDistance,
        bool includeReservations,
        out int reservingAgentId,
        List<NavigationGoalOccupancyCandidate> cachedCandidates = null)
    {
        long occupancyStartTicks = MainThreadFrameProfiler.LoggingEnabled
            ? Stopwatch.GetTimestamp()
            : 0L;
        int frame = GetFrameCount();
        if (_navigationGoalReservationFrame != frame)
        {
            NavigationGoalReservations.Clear();
            _navigationGoalReservationFrame = frame;
        }

        reservingAgentId = 0;
        Fix64 bestDistanceSq = Fix64.FromRaw(long.MaxValue);
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long reservationStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (includeReservations)
        {
            for (int i = 0; i < NavigationGoalReservations.Count; i++)
            {
                NavigationGoalReservation reservation = NavigationGoalReservations[i];
                if (reservation.SelfId == selfId)
                    continue;
                Fix64 reservationThreshold = Fix64.Max(requiredDistance, reservation.RequiredDistance);
                Fix64 reservationThresholdSq = reservationThreshold * reservationThreshold;
                Fix64 distanceSq = FixVector2.SqrMagnitude(reservation.Point - position);
                if (distanceSq >= reservationThresholdSq
                    || distanceSq > bestDistanceSq
                    || (distanceSq == bestDistanceSq && reservation.SelfId >= reservingAgentId))
                {
                    continue;
                }

                bestDistanceSq = distanceSq;
                reservingAgentId = reservation.SelfId;
            }
        }
        long reservationTicks = profile ? Stopwatch.GetTimestamp() - reservationStartTicks : 0L;

        long bucketStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        long candidateTicks = 0L;
        if (cachedCandidates != null)
        {
            for (int i = 0; i < cachedCandidates.Count; i++)
            {
                NavigationGoalOccupancyCandidate candidate = cachedCandidates[i];
                long candidateStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                EvaluateNavigationGoalOccupancyCandidate(
                    candidate.Agent,
                    selfId,
                    ignoredAgentId,
                    position,
                    requiredDistance,
                    ref bestDistanceSq,
                    ref reservingAgentId,
                    candidate.UseGoalPosition);
                if (profile)
                    candidateTicks += Stopwatch.GetTimestamp() - candidateStartTicks;
            }
        }
        else
        {
            long querySetupStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            EnsureNavigationGoalOccupancyBuckets();
            float bucketSize = ResolveAgentSpatialBucketSize();
            Fix64 broadPhaseThreshold = Fix64.Max(requiredDistance, _navigationGoalOccupancyMaximumThreshold);
            // Add one bucket for boundary-straddling points: two cells in buckets
            // separated by ceil(d / bucketSize) + 1 can still be within d.
            int searchRadius = Mathf.Max(1, Mathf.CeilToInt((float)broadPhaseThreshold / bucketSize) + 1);
            ResolveSpatialBucketCell(ToWorldVector3(position), out int centerCellX, out int centerCellY);
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowCombatApproachOccupancyQuerySetup,
                    Stopwatch.GetTimestamp() - querySetupStartTicks);
            }

            for (int bucketY = centerCellY - searchRadius; bucketY <= centerCellY + searchRadius; bucketY++)
            {
                for (int bucketX = centerCellX - searchRadius; bucketX <= centerCellX + searchRadius; bucketX++)
                {
                    long bucketKey = BuildSpatialBucketKey(bucketX, bucketY);
                    if (NavigationGoalPositionBuckets.TryGetValue(bucketKey, out List<AgentRuntimeData> positionBucket))
                    {
                        for (int i = 0; i < positionBucket.Count; i++)
                        {
                            long candidateStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                            EvaluateNavigationGoalOccupancyCandidate(
                                positionBucket[i],
                                selfId,
                                ignoredAgentId,
                                position,
                                requiredDistance,
                                ref bestDistanceSq,
                                ref reservingAgentId,
                                useGoalPosition: false);
                            if (profile)
                                candidateTicks += Stopwatch.GetTimestamp() - candidateStartTicks;
                        }
                    }

                    if (NavigationGoalTargetBuckets.TryGetValue(bucketKey, out List<AgentRuntimeData> targetBucket))
                    {
                        for (int i = 0; i < targetBucket.Count; i++)
                        {
                            long candidateStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                            EvaluateNavigationGoalOccupancyCandidate(
                                targetBucket[i],
                                selfId,
                                ignoredAgentId,
                                position,
                                requiredDistance,
                                ref bestDistanceSq,
                                ref reservingAgentId,
                                useGoalPosition: true);
                            if (profile)
                                candidateTicks += Stopwatch.GetTimestamp() - candidateStartTicks;
                        }
                    }
                }
            }
        }
        long bucketTicks = profile ? Stopwatch.GetTimestamp() - bucketStartTicks - candidateTicks : 0L;

        bool occupied = reservingAgentId != 0;
        if (MainThreadFrameProfiler.LoggingEnabled)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachOccupancy,
                Stopwatch.GetTimestamp() - occupancyStartTicks);
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachOccupancyReservations,
                reservationTicks);
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachOccupancyBuckets,
                bucketTicks);
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachOccupancyCandidate,
                candidateTicks);
        }
        return occupied;
    }

    private static List<NavigationGoalOccupancyCandidate> GetOrBuildCombatTargetSlotOccupancyCandidates(
        CombatTargetSlotEntry entry,
        int slotIndex,
        FixVector2 position,
        Fix64 requiredDistance)
    {
        if (entry == null)
            throw new InvalidOperationException("GetOrBuildCombatTargetSlotOccupancyCandidates failed: entry is null.");
        if (entry.Points == null || slotIndex < 0 || slotIndex >= entry.Points.Length)
            throw new InvalidOperationException("GetOrBuildCombatTargetSlotOccupancyCandidates failed: slot index is invalid.");
        if (_world == null)
            throw new InvalidOperationException("GetOrBuildCombatTargetSlotOccupancyCandidates failed: world is null.");

        int frame = GetFrameCount();
        int worldVersion = _world.Version;
        Fix64 broadPhaseThreshold = Fix64.Max(requiredDistance, _navigationGoalOccupancyMaximumThreshold);
        if (entry.OccupancyCandidatesBySlot == null
            || entry.OccupancyCandidatesBySlot.Length != entry.Points.Length
            || entry.OccupancyCandidateFrame != frame
            || entry.OccupancyCandidateWorldVersion != worldVersion
            || entry.OccupancyCandidateThresholdRaw < broadPhaseThreshold.RawValue)
        {
            EnsureNavigationGoalOccupancyBuckets();
            broadPhaseThreshold = Fix64.Max(requiredDistance, _navigationGoalOccupancyMaximumThreshold);
            if (entry.OccupancyCandidatesBySlot == null
                || entry.OccupancyCandidatesBySlot.Length != entry.Points.Length)
            {
                entry.OccupancyCandidatesBySlot =
                    new List<NavigationGoalOccupancyCandidate>[entry.Points.Length];
                entry.OccupancyCandidatesBuiltBySlot = new bool[entry.Points.Length];
            }
            else
            {
                for (int i = 0; i < entry.OccupancyCandidatesBySlot.Length; i++)
                    entry.OccupancyCandidatesBySlot[i]?.Clear();
                if (entry.OccupancyCandidatesBuiltBySlot == null
                    || entry.OccupancyCandidatesBuiltBySlot.Length != entry.Points.Length)
                {
                    entry.OccupancyCandidatesBuiltBySlot = new bool[entry.Points.Length];
                }
                else
                {
                    Array.Clear(entry.OccupancyCandidatesBuiltBySlot, 0, entry.OccupancyCandidatesBuiltBySlot.Length);
                }
            }
            entry.OccupancyCandidateFrame = frame;
            entry.OccupancyCandidateWorldVersion = worldVersion;
            entry.OccupancyCandidateThresholdRaw = broadPhaseThreshold.RawValue;
        }

        List<NavigationGoalOccupancyCandidate> cached = entry.OccupancyCandidatesBySlot[slotIndex];
        if (entry.OccupancyCandidatesBuiltBySlot[slotIndex])
            return cached;

        float bucketSize = ResolveAgentSpatialBucketSize();
        int searchRadius = Mathf.Max(1, Mathf.CeilToInt((float)broadPhaseThreshold / bucketSize) + 1);
        ResolveSpatialBucketCell(ToWorldVector3(position), out int centerCellX, out int centerCellY);
        cached ??= new List<NavigationGoalOccupancyCandidate>(16);
        for (int bucketY = centerCellY - searchRadius; bucketY <= centerCellY + searchRadius; bucketY++)
        {
            for (int bucketX = centerCellX - searchRadius; bucketX <= centerCellX + searchRadius; bucketX++)
            {
                long bucketKey = BuildSpatialBucketKey(bucketX, bucketY);
                if (NavigationGoalPositionBuckets.TryGetValue(bucketKey, out List<AgentRuntimeData> positionBucket))
                {
                    for (int i = 0; i < positionBucket.Count; i++)
                        cached.Add(new NavigationGoalOccupancyCandidate(positionBucket[i], useGoalPosition: false));
                }

                if (NavigationGoalTargetBuckets.TryGetValue(bucketKey, out List<AgentRuntimeData> targetBucket))
                {
                    for (int i = 0; i < targetBucket.Count; i++)
                        cached.Add(new NavigationGoalOccupancyCandidate(targetBucket[i], useGoalPosition: true));
                }
            }
        }

        entry.OccupancyCandidatesBySlot[slotIndex] = cached;
        entry.OccupancyCandidatesBuiltBySlot[slotIndex] = true;
        return cached;
    }

    private static void PrepareCombatTargetSlotOccupancyCandidates(
        CombatTargetSlotEntry entry,
        Fix64 requiredDistance,
        int startIsland)
    {
        if (entry == null || entry.Points == null || entry.IslandIds == null)
            throw new InvalidOperationException("PrepareCombatTargetSlotOccupancyCandidates received an incomplete entry.");
        if (entry.Points.Length != entry.IslandIds.Length)
            throw new InvalidOperationException("Combat target slot entry point/island arrays are inconsistent.");
        if (_world == null)
            throw new InvalidOperationException("PrepareCombatTargetSlotOccupancyCandidates requires an active world.");

        int frame = GetFrameCount();
        int worldVersion = _world.Version;
        Fix64 threshold = Fix64.Max(requiredDistance, _navigationGoalOccupancyMaximumThreshold);
        if (entry.OccupancyCandidatesPreparedFrame == frame
            && entry.OccupancyCandidatesPreparedWorldVersion == worldVersion
            && entry.OccupancyCandidatesPreparedThresholdRaw >= threshold.RawValue)
            return;

        for (int slot = 0; slot < entry.Points.Length; slot++)
        {
            if (entry.IslandIds[slot] != startIsland)
                continue;
            GetOrBuildCombatTargetSlotOccupancyCandidates(
                entry,
                slot,
                entry.Points[slot],
                requiredDistance);
        }

        entry.OccupancyCandidatesPreparedFrame = frame;
        entry.OccupancyCandidatesPreparedWorldVersion = worldVersion;
        entry.OccupancyCandidatesPreparedThresholdRaw = threshold.RawValue;
    }

    private static void EvaluateNavigationGoalOccupancyCandidate(
        AgentRuntimeData other,
        int selfId,
        int ignoredAgentId,
        FixVector2 position,
        Fix64 requiredDistance,
        ref Fix64 bestDistanceSq,
        ref int reservingAgentId,
        bool useGoalPosition)
    {
        if (other == null)
            throw new InvalidOperationException("EvaluateNavigationGoalOccupancyCandidate received a null agent.");
        if (other.Id == selfId || other.Id == ignoredAgentId || other.IgnoreAgentCollision)
            return;

        Fix64 otherThreshold = Fix64.Max(
            requiredDistance,
            other.RadiusFixed * (Fix64)2 + NavigationGoalOccupancyPadding);
        Fix64 otherThresholdSq = otherThreshold * otherThreshold;
        FixVector2 candidatePosition = useGoalPosition
            ? other.NavState.LastGoalWorldFixed
            : other.PositionFixed;
        Fix64 distanceSq = FixVector2.SqrMagnitude(candidatePosition - position);
        if (distanceSq < otherThresholdSq
            && (distanceSq < bestDistanceSq
                || (distanceSq == bestDistanceSq && other.Id < reservingAgentId)))
        {
            bestDistanceSq = distanceSq;
            reservingAgentId = other.Id;
        }
    }

    private static Fix64 ResolveNavigationGoalAngleFixed(int index)
    {
        if (index == 0)
            return Fix64.Zero;

        int step = (index + 1) / 2;
        int sign = (index & 1) == 1 ? 1 : -1;
        return (Fix64)(sign * step) * (Fix64)360 / (Fix64)NavigationGoalCandidateCount;
    }

    private static float HorizontalDistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static bool AreCellsOnSameIsland(NavigationWorld world, int startX, int startY, int goalX, int goalY)
    {
        if (world == null)
            throw new InvalidOperationException("AreCellsOnSameIsland failed: world is null.");
        if (!world.IsWalkable(startX, startY) || !world.IsWalkable(goalX, goalY))
            return false;

        int startIsland = ResolveIslandId(world, startX, startY);
        int goalIsland = ResolveIslandId(world, goalX, goalY);
        return startIsland > 0 && startIsland == goalIsland;
    }

    private static string BuildIslandMismatchFailure(IEntityContext self, Vector3 rawGoalPosition, int startX, int startY, int goalX, int goalY)
    {
        int startIsland = ResolveIslandIdForDiagnostics(_world, startX, startY);
        int goalIsland = ResolveIslandIdForDiagnostics(_world, goalX, goalY);
        string nearestGoalIsland = TryFindNearestCellInIsland(_world, startX, startY, goalIsland, 32, out int nearestGoalIslandX, out int nearestGoalIslandY, out int nearestGoalIslandDistance)
            ? $" nearestGoalIslandFromStart=({nearestGoalIslandX},{nearestGoalIslandY}) manhattan={nearestGoalIslandDistance}"
            : " nearestGoalIslandFromStart=none";
        string nearestStartIsland = TryFindNearestCellInIsland(_world, goalX, goalY, startIsland, 32, out int nearestStartIslandX, out int nearestStartIslandY, out int nearestStartIslandDistance)
            ? $" nearestStartIslandFromGoal=({nearestStartIslandX},{nearestStartIslandY}) manhattan={nearestStartIslandDistance}"
            : " nearestStartIslandFromGoal=none";

        Vector3 startWorld = _world != null ? _world.GridToWorldCenter(startX, startY) : Vector3.zero;
        Vector3 goalWorld = _world != null ? _world.GridToWorldCenter(goalX, goalY) : Vector3.zero;
        IEntityContext target = self?.TargetComp?.CurrentTarget;
        string targetInfo = target != null
            ? $" target={target.CharacterKey} targetSide={target.Side} targetPos={target.Position} targetMoveMode={(target.MoveExecutor != null ? target.MoveExecutor.MovementMode.ToString() : "null")}"
            : " target=null";
        string selfInfo = self != null
            ? $" self={self.CharacterKey} selfSide={self.Side} selfPos={self.Position} selfMoveMode={(self.MoveExecutor != null ? self.MoveExecutor.MovementMode.ToString() : "null")}"
            : " self=null";
        string startGrid = FormatGridSampleDiagnostics(startWorld);
        string goalGrid = FormatGridSampleDiagnostics(rawGoalPosition);
        string startNeighborhood = BuildIslandNeighborhoodDiagnostics(_world, startX, startY, 2);
        string goalNeighborhood = BuildIslandNeighborhoodDiagnostics(_world, goalX, goalY, 2);

        return $"island mismatch start=({startX},{startY}) island={startIsland} startWorld={startWorld} " +
               $"goal=({goalX},{goalY}) island={goalIsland} goalWorld={goalWorld} rawGoal={rawGoalPosition} " +
               $"worldVersion={_world?.Version ?? -1} agentType={_world?.AgentTypeId ?? int.MinValue} islandCount={_world?.IslandCount ?? -1} " +
               $"runtimeDirtySectors={(_activeWorldState != null ? _activeWorldState.DirtyRuntimeObstacleSectors.Count : -1)} " +
               $"runtimeDirtyReason={_lastRuntimeObstacleDirtyReason} circleObstacles={CircleObstacles.Count} boxObstacles={BoxObstacles.Count}" +
               $"{nearestGoalIsland}{nearestStartIsland}{selfInfo}{targetInfo} startGrid={startGrid} goalGrid={goalGrid} " +
               $"startNeighborhood={startNeighborhood} goalNeighborhood={goalNeighborhood}";
    }

    private static int ResolveIslandIdForDiagnostics(NavigationWorld world, int x, int y)
    {
        return ResolveIslandId(world, x, y);
    }

    private static int ResolveIslandId(NavigationWorld world, int x, int y)
    {
        if (world == null || x < 0 || x >= world.Width || y < 0 || y >= world.Height)
            return -1;
        if (!TryGetSectorForCell(world, x, y, out SectorData sector)
            || sector.LocalComponentIds == null
            || sector.LocalComponentIds.Length != sector.Width * sector.Height)
            throw new InvalidOperationException($"ResolveIslandId failed: sector component data is unavailable. cell=({x},{y}) world={world.Version}.");

        int localIndex = GetSectorLocalIndex(sector, x, y);
        int componentId = sector.LocalComponentIds[localIndex];
        if (componentId <= 0)
            return 0;
        if (sector.LocalComponentIslandIds == null || componentId >= sector.LocalComponentIslandIds.Length)
            throw new InvalidOperationException(
                $"ResolveIslandId failed: component-to-island mapping is unavailable. sector={sector.SectorId} component={componentId}.");
        return sector.LocalComponentIslandIds[componentId];
    }

    private static string BuildIslandNeighborhoodDiagnostics(NavigationWorld world, int centerX, int centerY, int radius)
    {
        if (world == null)
            return "world-null";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append("[");
        bool first = true;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (x < 0 || x >= world.Width || y < 0 || y >= world.Height)
                    continue;

                if (!first)
                    builder.Append("; ");
                first = false;

                int index = world.GetIndex(x, y);
                builder.Append("(");
                builder.Append(x);
                builder.Append(",");
                builder.Append(y);
                builder.Append("){walk=");
                builder.Append(world.WalkableMask != null && world.WalkableMask.Length == world.Width * world.Height && world.WalkableMask[index]);
                builder.Append(",base=");
                builder.Append(world.BaseWalkableMask != null && world.BaseWalkableMask.Length == world.Width * world.Height && world.BaseWalkableMask[index]);
                builder.Append(",island=");
                builder.Append(world.IslandIds != null && world.IslandIds.Length == world.Width * world.Height ? world.IslandIds[index] : -1);
                builder.Append(",mask=0x");
                builder.Append(world.NeighborTraversalMask != null && world.NeighborTraversalMask.Length == world.Width * world.Height
                    ? world.NeighborTraversalMask[index].ToString("X2")
                    : "NA");
                builder.Append(",sector=");
                builder.Append(world.TryGetSectorId(x, y, out int sectorId) ? sectorId : -1);
                builder.Append("}");
            }
        }

        builder.Append("]");
        return builder.ToString();
    }

    private static string BuildGoalResolutionIslandDiagnostics(
        NavigationWorld world,
        IEntityContext self,
        Vector3 desiredGoal,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int startIsland,
        int radiusCells,
        bool fullIslandFound,
        int fullIslandX,
        int fullIslandY,
        float fullIslandDistance)
    {
        if (world == null)
            return $"[FlowGoalResolutionIslandDiag] world=null desired={desiredGoal}";

        int goalIsland = ResolveIslandIdForDiagnostics(world, goalX, goalY);
        Vector3 startWorld = world.GridToWorldCenter(startX, startY);
        Vector3 goalWorld = world.GridToWorldCenter(goalX, goalY);
        string fullIslandResult = fullIslandFound
            ? $"fullIslandNearest=({fullIslandX},{fullIslandY}) world={world.GridToWorldCenter(fullIslandX, fullIslandY)} dist={fullIslandDistance:F3}"
            : "fullIslandNearest=none";
        IEntityContext target = self?.TargetComp?.CurrentTarget;
        string selfInfo = self != null
            ? $"self={self.CharacterKey} side={self.Side} pos={self.Position} moveMode={(self.MoveExecutor != null ? self.MoveExecutor.MovementMode.ToString() : "null")}"
            : "self=null";
        string targetInfo = target != null
            ? $"target={target.CharacterKey} side={target.Side} pos={target.Position} moveMode={(target.MoveExecutor != null ? target.MoveExecutor.MovementMode.ToString() : "null")}"
            : "target=null";
        string startNeighborhood = BuildWalkableNeighborhoodDiagnostics(world, startX, startY, 2);
        string goalNeighborhood = BuildWalkableNeighborhoodDiagnostics(world, goalX, goalY, 2);

        return $"[FlowGoalResolutionIslandDiag] local same-island goal resolution failed desired={desiredGoal} " +
               $"start=({startX},{startY}) island={startIsland} world={startWorld} " +
               $"goal=({goalX},{goalY}) island={goalIsland} world={goalWorld} radiusCells={radiusCells} " +
               $"worldVersion={world.Version} agentType={world.AgentTypeId} islandCount={world.IslandCount} " +
               $"runtimeDirtySectors={(_activeWorldState != null ? _activeWorldState.DirtyRuntimeObstacleSectors.Count : -1)} " +
               $"runtimeDirtyReason={_lastRuntimeObstacleDirtyReason} circleObstacles={CircleObstacles.Count} boxObstacles={BoxObstacles.Count} " +
               $"{fullIslandResult} {selfInfo} {targetInfo} desiredGrid={FormatGridSampleDiagnostics(desiredGoal)} " +
               $"startGrid={FormatGridSampleDiagnostics(startWorld)} goalCellGrid={FormatGridSampleDiagnostics(goalWorld)} " +
               $"startNeighborhood={startNeighborhood} startLinks={BuildNeighborLinkDiagnostics(world, startX, startY, 2)} " +
               $"goalNeighborhood={goalNeighborhood} goalLinks={BuildNeighborLinkDiagnostics(world, goalX, goalY, 2)}";
    }

    private static bool TryFindNearestCellInIsland(NavigationWorld world, int startX, int startY, int islandId, int radius, out int resultX, out int resultY, out int distance)
    {
        resultX = 0;
        resultY = 0;
        distance = int.MaxValue;
        if (world == null || islandId <= 0)
            return false;

        bool found = false;
        for (int y = startY - radius; y <= startY + radius; y++)
        {
            for (int x = startX - radius; x <= startX + radius; x++)
            {
                if (x < 0 || x >= world.Width || y < 0 || y >= world.Height)
                    continue;

                if (ResolveIslandId(world, x, y) != islandId)
                    continue;

                int candidateDistance = Mathf.Abs(x - startX) + Mathf.Abs(y - startY);
                if (found && candidateDistance >= distance)
                    continue;

                resultX = x;
                resultY = y;
                distance = candidateDistance;
                found = true;
            }
        }

        return found;
    }

    private static bool ProcessGoalProjectionSpatialIndexBuild(
        NavigationWorld world,
        ref GoalProjectionSpatialIndexBuildJob buildJob,
        long deadlineTicks,
        bool forceComplete)
    {
        if (world == null || world.Width <= 0 || world.Height <= 0)
            throw new InvalidOperationException("Goal projection spatial index build received an invalid world.");
        if (world.Sectors == null || world.Sectors.Length == 0)
            throw new InvalidOperationException("Goal projection spatial index build requires finalized sector components.");
        if (world.GoalProjectionSpatialIndex != null)
            throw new InvalidOperationException("Goal projection spatial index build attempted to replace a published index.");

        int cellCount = checked(world.Width * world.Height);
        if (buildJob == null)
        {
            _perf.GoalProjectionIndexBuildStarts++;
            int bucketCountX = (world.Width + GoalProjectionSpatialIndex.BucketSizeInCells - 1)
                               / GoalProjectionSpatialIndex.BucketSizeInCells;
            int bucketCountY = (world.Height + GoalProjectionSpatialIndex.BucketSizeInCells - 1)
                               / GoalProjectionSpatialIndex.BucketSizeInCells;
            buildJob = new GoalProjectionSpatialIndexBuildJob
            {
                BucketCellLists = new List<int>[checked(bucketCountX * bucketCountY)],
                IslandCellLists = new List<int>[checked(world.IslandCount + 1)]
            };
        }

        int expectedBucketCountX = (world.Width + GoalProjectionSpatialIndex.BucketSizeInCells - 1)
                                   / GoalProjectionSpatialIndex.BucketSizeInCells;
        int expectedBucketCountY = (world.Height + GoalProjectionSpatialIndex.BucketSizeInCells - 1)
                                   / GoalProjectionSpatialIndex.BucketSizeInCells;
        if (buildJob.BucketCellLists == null
            || buildJob.BucketCellLists.Length != expectedBucketCountX * expectedBucketCountY
            || buildJob.IslandCellLists == null
            || buildJob.IslandCellLists.Length != world.IslandCount + 1)
        {
            throw new InvalidOperationException("Goal projection spatial index build has invalid bucket storage.");
        }

        int scanIndexBefore = buildJob.ScanIndex;
        while (buildJob.ScanIndex < cellCount)
        {
            int cellIndex = buildJob.ScanIndex++;
            _perf.GoalProjectionIndexScannedCells++;
            if (world.WalkableMask[cellIndex])
            {
                int x = cellIndex % world.Width;
                int y = cellIndex / world.Width;
                int bucketX = x / GoalProjectionSpatialIndex.BucketSizeInCells;
                int bucketY = y / GoalProjectionSpatialIndex.BucketSizeInCells;
                int bucketId = bucketX + bucketY * expectedBucketCountX;
                List<int> cells = buildJob.BucketCellLists[bucketId];
                if (cells == null)
                {
                    cells = new List<int>(GoalProjectionSpatialIndex.BucketSizeInCells * GoalProjectionSpatialIndex.BucketSizeInCells);
                    buildJob.BucketCellLists[bucketId] = cells;
                }
                cells.Add(cellIndex);

                int islandId = ResolveIslandId(world, x, y);
                if (islandId <= 0 || islandId >= buildJob.IslandCellLists.Length)
                {
                    throw new InvalidOperationException(
                        $"Goal projection spatial index found a walkable cell without a finalized island. cell={cellIndex} world={world.Version}.");
                }
                List<int> islandCells = buildJob.IslandCellLists[islandId];
                if (islandCells == null)
                {
                    islandCells = new List<int>();
                    buildJob.IslandCellLists[islandId] = islandCells;
                }
                islandCells.Add(cellIndex);
            }

            if (!forceComplete && IsBudgetExpired(deadlineTicks, buildJob.ScanIndex))
                return false;
        }

        if (scanIndexBefore < cellCount && buildJob.ScanIndex >= cellCount)
            _perf.GoalProjectionIndexFullScans++;

        GoalProjectionSpatialIndex index = buildJob.WorkingIndex;
        if (index == null)
        {
            index = new GoalProjectionSpatialIndex
            {
                BucketCountX = expectedBucketCountX,
                BucketCountY = expectedBucketCountY,
                Buckets = new GoalProjectionBucket[buildJob.BucketCellLists.Length],
                QueryVisitStamps = new int[buildJob.BucketCellLists.Length],
                HeapBucketIds = new int[buildJob.BucketCellLists.Length],
                HeapLowerBounds = new Fix64[buildJob.BucketCellLists.Length],
                IslandCellIndices = new int[buildJob.IslandCellLists.Length][]
            };
            for (int islandId = 1; islandId < index.IslandCellIndices.Length; islandId++)
            {
                List<int> islandCells = buildJob.IslandCellLists[islandId];
                index.IslandCellIndices[islandId] = islandCells != null
                    ? islandCells.ToArray()
                    : Array.Empty<int>();
            }
            buildJob.WorkingIndex = index;
        }
        while (buildJob.FinalizeBucketCursor < index.Buckets.Length)
        {
            int bucketId = buildJob.FinalizeBucketCursor++;
            List<int> cells = buildJob.BucketCellLists[bucketId];
            if (cells == null || cells.Count == 0)
            {
                index.Buckets[bucketId] = new GoalProjectionBucket
                {
                    CellIndices = Array.Empty<int>(),
                    IslandIds = Array.Empty<int>()
                };
            }
            else
            {
                int[] cellIndices = cells.ToArray();
                int[] islandIds = new int[cellIndices.Length];
                for (int i = 0; i < cellIndices.Length; i++)
                {
                    int cellIndex = cellIndices[i];
                    int islandId = ResolveIslandId(world, cellIndex % world.Width, cellIndex / world.Width);
                    if (islandId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Goal projection spatial index found a walkable cell without a finalized island. cell={cellIndex} world={world.Version}.");
                    }
                    islandIds[i] = islandId;
                }
                index.Buckets[bucketId] = new GoalProjectionBucket
                {
                    CellIndices = cellIndices,
                    IslandIds = islandIds
                };
            }

            if (!forceComplete && IsBudgetExpired(deadlineTicks, buildJob.FinalizeBucketCursor))
                return false;
        }

        if (!ProcessGoalProjectionSpatialHierarchyBuild(world, buildJob, deadlineTicks, forceComplete))
            return false;

        world.GoalProjectionSpatialIndex = index;
        _perf.GoalProjectionIndexPublishes++;
        buildJob = null;
        return true;
    }

    private static bool ProcessGoalProjectionSpatialHierarchyBuild(
        NavigationWorld world,
        GoalProjectionSpatialIndexBuildJob buildJob,
        long deadlineTicks,
        bool forceComplete)
    {
        GoalProjectionSpatialIndex index = buildJob.WorkingIndex
                                           ?? throw new InvalidOperationException("Goal projection hierarchy build has no working index.");
        if (buildJob.HierarchyNodes == null)
        {
            int leafSpan = 1;
            int requiredSpan = Math.Max(index.BucketCountX, index.BucketCountY);
            while (leafSpan < requiredSpan)
                leafSpan = checked(leafSpan * 2);

            buildJob.HierarchyLeafSpan = leafSpan;
            buildJob.HierarchyNodes = new List<GoalProjectionHierarchyNode>(checked(leafSpan * leafSpan * 2));
            buildJob.HierarchyCurrentLevelNodeIndices = new int[checked(leafSpan * leafSpan)];
            buildJob.HierarchyCurrentLevelWidth = leafSpan;
            buildJob.HierarchyCurrentLevelHeight = leafSpan;
        }

        while (buildJob.HierarchyLeafCursor < buildJob.HierarchyCurrentLevelNodeIndices.Length)
        {
            int leafIndex = buildJob.HierarchyLeafCursor++;
            int bucketX = leafIndex % buildJob.HierarchyLeafSpan;
            int bucketY = leafIndex / buildJob.HierarchyLeafSpan;
            int bucketId = bucketX < index.BucketCountX && bucketY < index.BucketCountY
                ? bucketX + bucketY * index.BucketCountX
                : -1;
            int[] islandIds = bucketId >= 0
                ? GetSortedDistinctGoalProjectionIslandIds(index.Buckets[bucketId])
                : Array.Empty<int>();
            buildJob.HierarchyCurrentLevelNodeIndices[leafIndex] = buildJob.HierarchyNodes.Count;
            buildJob.HierarchyNodes.Add(new GoalProjectionHierarchyNode
            {
                MinBucketX = bucketX,
                MinBucketY = bucketY,
                BucketSpan = 1,
                BucketId = bucketId,
                IslandIds = islandIds
            });
            if (!forceComplete && IsBudgetExpired(deadlineTicks, buildJob.HierarchyLeafCursor))
                return false;
        }

        while (buildJob.HierarchyCurrentLevelWidth > 1 || buildJob.HierarchyCurrentLevelHeight > 1)
        {
            int parentWidth = Math.Max(1, buildJob.HierarchyCurrentLevelWidth / 2);
            int parentHeight = Math.Max(1, buildJob.HierarchyCurrentLevelHeight / 2);
            int parentCount = checked(parentWidth * parentHeight);
            if (buildJob.HierarchyNextLevelNodeIndices == null)
                buildJob.HierarchyNextLevelNodeIndices = new List<int>(parentCount);

            while (buildJob.HierarchyParentCursor < parentCount)
            {
                int parentIndex = buildJob.HierarchyParentCursor++;
                int parentX = parentIndex % parentWidth;
                int parentY = parentIndex / parentWidth;
                int childX = parentX * 2;
                int childY = parentY * 2;
                int child0 = buildJob.HierarchyCurrentLevelNodeIndices[childX + childY * buildJob.HierarchyCurrentLevelWidth];
                int child1 = childX + 1 < buildJob.HierarchyCurrentLevelWidth
                    ? buildJob.HierarchyCurrentLevelNodeIndices[childX + 1 + childY * buildJob.HierarchyCurrentLevelWidth]
                    : -1;
                int child2 = childY + 1 < buildJob.HierarchyCurrentLevelHeight
                    ? buildJob.HierarchyCurrentLevelNodeIndices[childX + (childY + 1) * buildJob.HierarchyCurrentLevelWidth]
                    : -1;
                int child3 = childX + 1 < buildJob.HierarchyCurrentLevelWidth && childY + 1 < buildJob.HierarchyCurrentLevelHeight
                    ? buildJob.HierarchyCurrentLevelNodeIndices[childX + 1 + (childY + 1) * buildJob.HierarchyCurrentLevelWidth]
                    : -1;
                buildJob.HierarchyNextLevelNodeIndices.Add(buildJob.HierarchyNodes.Count);
                buildJob.HierarchyNodes.Add(new GoalProjectionHierarchyNode
                {
                    MinBucketX = childX,
                    MinBucketY = childY,
                    BucketSpan = checked(buildJob.HierarchyLeafSpan / parentWidth),
                    Child0 = child0,
                    Child1 = child1,
                    Child2 = child2,
                    Child3 = child3,
                    IslandIds = MergeGoalProjectionIslandIds(
                        buildJob.HierarchyNodes[child0].IslandIds,
                        child1 >= 0 ? buildJob.HierarchyNodes[child1].IslandIds : null,
                        child2 >= 0 ? buildJob.HierarchyNodes[child2].IslandIds : null,
                        child3 >= 0 ? buildJob.HierarchyNodes[child3].IslandIds : null)
                });
                if (!forceComplete && IsBudgetExpired(deadlineTicks, buildJob.HierarchyParentCursor))
                    return false;
            }

            buildJob.HierarchyCurrentLevelNodeIndices = buildJob.HierarchyNextLevelNodeIndices.ToArray();
            buildJob.HierarchyCurrentLevelWidth = parentWidth;
            buildJob.HierarchyCurrentLevelHeight = parentHeight;
            buildJob.HierarchyParentCursor = 0;
            buildJob.HierarchyNextLevelNodeIndices = null;
        }

        if (buildJob.HierarchyCurrentLevelNodeIndices == null || buildJob.HierarchyCurrentLevelNodeIndices.Length != 1)
            throw new InvalidOperationException("Goal projection hierarchy did not converge to one root.");
        index.HierarchyNodes = buildJob.HierarchyNodes.ToArray();
        index.HierarchyRootNodeIndex = buildJob.HierarchyCurrentLevelNodeIndices[0];
        return true;
    }

    private static int[] GetSortedDistinctGoalProjectionIslandIds(GoalProjectionBucket bucket)
    {
        if (bucket == null || bucket.IslandIds == null || bucket.IslandIds.Length == 0)
            return Array.Empty<int>();

        int[] sorted = (int[])bucket.IslandIds.Clone();
        Array.Sort(sorted);
        int distinctCount = 1;
        for (int i = 1; i < sorted.Length; i++)
        {
            if (sorted[i] != sorted[distinctCount - 1])
                sorted[distinctCount++] = sorted[i];
        }
        if (distinctCount == sorted.Length)
            return sorted;
        int[] result = new int[distinctCount];
        Array.Copy(sorted, result, distinctCount);
        return result;
    }

    private static int[] MergeGoalProjectionIslandIds(int[] first, int[] second, int[] third, int[] fourth)
    {
        int total = (first?.Length ?? 0) + (second?.Length ?? 0) + (third?.Length ?? 0) + (fourth?.Length ?? 0);
        if (total == 0)
            return Array.Empty<int>();

        int[] merged = new int[total];
        int[] cursors = { 0, 0, 0, 0 };
        int[][] sources = { first ?? Array.Empty<int>(), second ?? Array.Empty<int>(), third ?? Array.Empty<int>(), fourth ?? Array.Empty<int>() };
        int count = 0;
        while (true)
        {
            int next = int.MaxValue;
            for (int i = 0; i < sources.Length; i++)
            {
                if (cursors[i] < sources[i].Length && sources[i][cursors[i]] < next)
                    next = sources[i][cursors[i]];
            }
            if (next == int.MaxValue)
                break;
            if (count == 0 || merged[count - 1] != next)
                merged[count++] = next;
            for (int i = 0; i < sources.Length; i++)
            {
                while (cursors[i] < sources[i].Length && sources[i][cursors[i]] == next)
                    cursors[i]++;
            }
        }
        if (count == merged.Length)
            return merged;
        int[] result = new int[count];
        Array.Copy(merged, result, count);
        return result;
    }

    private static void BuildGoalProjectionSpatialIndexImmediate(NavigationWorld world)
    {
        GoalProjectionSpatialIndexBuildJob buildJob = null;
        if (!ProcessGoalProjectionSpatialIndexBuild(world, ref buildJob, long.MaxValue, forceComplete: true)
            || buildJob != null)
        {
            throw new InvalidOperationException("Immediate goal projection spatial index build did not complete.");
        }
    }

    private static bool TryFindNearestWalkableInIslandByWorldDistanceFromSpatialIndex(
        NavigationWorld world,
        int centerX,
        int centerY,
        FixVector2 desiredWorld,
        int islandId,
        out int resultX,
        out int resultY,
        out Fix64 distance)
    {
        resultX = 0;
        resultY = 0;
        distance = Fix64.FromRaw(long.MaxValue);
        if (world == null || islandId <= 0)
            return false;

        GoalProjectionSpatialIndex index = world.GoalProjectionSpatialIndex
                                            ?? throw new InvalidOperationException(
                                                $"Reachable-goal projection attempted to consume a world without its published spatial index. world={world.Version}.");
        if (index.Buckets == null
            || index.Buckets.Length == 0
            || index.HierarchyNodes == null
            || index.HierarchyNodes.Length == 0
            || index.HierarchyRootNodeIndex < 0
            || index.HierarchyRootNodeIndex >= index.HierarchyNodes.Length)
        {
            throw new InvalidOperationException("Reachable-goal projection spatial index has invalid storage.");
        }

        if (!world.WorldToGridFixed(desiredWorld, out int desiredX, out int desiredY))
        {
            throw new InvalidOperationException(
                $"Reachable-goal projection desired point is outside the published grid. raw=({desiredWorld.x.RawValue},{desiredWorld.y.RawValue}).");
        }
        var query = new MovingTargetAnchor
        {
            Key = new MovingTargetAnchorKey(int.MinValue, world.AgentTypeId, islandId),
            PendingProjectionGoalWorldFixed = desiredWorld
        };
        PushMovingTargetProjectionNode(query, world, index, index.HierarchyRootNodeIndex);
        while (query.PendingProjectionNodeHeap.Count > 0)
        {
            PopMovingTargetProjectionNode(query, out int nodeIndex, out Fix64 lowerBoundSquared);
            if (query.PendingProjectionBestCellIndex != int.MaxValue
                && lowerBoundSquared > query.PendingProjectionBestDistanceSquared)
            {
                break;
            }
            GoalProjectionHierarchyNode node = index.HierarchyNodes[nodeIndex]
                ?? throw new InvalidOperationException("Reachable-goal projection hierarchy contains a null node.");
            if (node.BucketId >= 0)
            {
                GoalProjectionBucket bucket = index.Buckets[node.BucketId]
                    ?? throw new InvalidOperationException($"Goal projection spatial index bucket is null. bucket={node.BucketId}.");
                if (bucket.CellIndices == null || bucket.IslandIds == null || bucket.CellIndices.Length != bucket.IslandIds.Length)
                    throw new InvalidOperationException($"Goal projection spatial index bucket has invalid cells. bucket={node.BucketId}.");
                for (int i = 0; i < bucket.CellIndices.Length; i++)
                {
                    if (bucket.IslandIds[i] != islandId)
                        continue;
                    int cellIndex = bucket.CellIndices[i];
                    int x = cellIndex % world.Width;
                    int y = cellIndex / world.Width;
                    FixVector2 worldCenter = world.GridToWorldCenterFixed(x, y);
                    Fix64 dx = worldCenter.x - desiredWorld.x;
                    Fix64 dz = worldCenter.y - desiredWorld.y;
                    Fix64 candidateDistanceSquared = dx * dx + dz * dz;
                    if (candidateDistanceSquared < query.PendingProjectionBestDistanceSquared
                        || (candidateDistanceSquared == query.PendingProjectionBestDistanceSquared
                            && cellIndex < query.PendingProjectionBestCellIndex))
                    {
                        query.PendingProjectionBestCellIndex = cellIndex;
                        query.PendingProjectionBestDistanceSquared = candidateDistanceSquared;
                    }
                }
            }
            else
            {
                PushMovingTargetProjectionChildIfRelevant(query, world, index, node.Child0);
                PushMovingTargetProjectionChildIfRelevant(query, world, index, node.Child1);
                PushMovingTargetProjectionChildIfRelevant(query, world, index, node.Child2);
                PushMovingTargetProjectionChildIfRelevant(query, world, index, node.Child3);
            }
        }

        if (query.PendingProjectionBestCellIndex == int.MaxValue)
            return false;

        resultX = query.PendingProjectionBestCellIndex % world.Width;
        resultY = query.PendingProjectionBestCellIndex / world.Width;
        distance = Fix64.Sqrt(query.PendingProjectionBestDistanceSquared);
        return true;
    }

    private static int BeginGoalProjectionSpatialIndexQuery(GoalProjectionSpatialIndex index)
    {
        if (index.QueryVisitToken == int.MaxValue)
        {
            Array.Clear(index.QueryVisitStamps, 0, index.QueryVisitStamps.Length);
            index.QueryVisitToken = 1;
            return index.QueryVisitToken;
        }

        return ++index.QueryVisitToken;
    }

    private static void TryPushGoalProjectionBucketNeighbor(
        NavigationWorld world,
        GoalProjectionSpatialIndex index,
        int bucketX,
        int bucketY,
        FixVector2 desiredWorld,
        int visitToken)
    {
        if (bucketX < 0 || bucketX >= index.BucketCountX || bucketY < 0 || bucketY >= index.BucketCountY)
            return;

        PushGoalProjectionBucket(world, index, bucketX + bucketY * index.BucketCountX, desiredWorld, visitToken);
    }

    private static void PushGoalProjectionBucket(
        NavigationWorld world,
        GoalProjectionSpatialIndex index,
        int bucketId,
        FixVector2 desiredWorld,
        int visitToken)
    {
        if (index.QueryVisitStamps[bucketId] == visitToken)
            return;
        if (index.HeapCount >= index.HeapBucketIds.Length)
            throw new InvalidOperationException("Goal projection spatial index heap capacity is exhausted.");

        index.QueryVisitStamps[bucketId] = visitToken;
        Fix64 lowerBoundSquared = GetGoalProjectionBucketLowerBoundSquared(world, index, bucketId, desiredWorld);
        int heapIndex = index.HeapCount++;
        while (heapIndex > 0)
        {
            int parent = (heapIndex - 1) / 2;
            if (CompareGoalProjectionHeapNodes(
                    index.HeapLowerBounds[parent],
                    index.HeapBucketIds[parent],
                    lowerBoundSquared,
                    bucketId) <= 0)
            {
                break;
            }

            index.HeapLowerBounds[heapIndex] = index.HeapLowerBounds[parent];
            index.HeapBucketIds[heapIndex] = index.HeapBucketIds[parent];
            heapIndex = parent;
        }
        index.HeapLowerBounds[heapIndex] = lowerBoundSquared;
        index.HeapBucketIds[heapIndex] = bucketId;
    }

    private static void PopGoalProjectionBucket(
        GoalProjectionSpatialIndex index,
        out int bucketId,
        out Fix64 lowerBoundSquared)
    {
        if (index.HeapCount <= 0)
            throw new InvalidOperationException("Goal projection spatial index heap is empty.");

        bucketId = index.HeapBucketIds[0];
        lowerBoundSquared = index.HeapLowerBounds[0];
        int lastIndex = --index.HeapCount;
        if (lastIndex == 0)
            return;

        int lastBucketId = index.HeapBucketIds[lastIndex];
        Fix64 lastLowerBound = index.HeapLowerBounds[lastIndex];
        int heapIndex = 0;
        while (true)
        {
            int left = heapIndex * 2 + 1;
            if (left >= lastIndex)
                break;
            int right = left + 1;
            int child = right < lastIndex
                        && CompareGoalProjectionHeapNodes(
                            index.HeapLowerBounds[right], index.HeapBucketIds[right],
                            index.HeapLowerBounds[left], index.HeapBucketIds[left]) < 0
                ? right
                : left;
            if (CompareGoalProjectionHeapNodes(
                    lastLowerBound, lastBucketId,
                    index.HeapLowerBounds[child], index.HeapBucketIds[child]) <= 0)
            {
                break;
            }

            index.HeapLowerBounds[heapIndex] = index.HeapLowerBounds[child];
            index.HeapBucketIds[heapIndex] = index.HeapBucketIds[child];
            heapIndex = child;
        }
        index.HeapLowerBounds[heapIndex] = lastLowerBound;
        index.HeapBucketIds[heapIndex] = lastBucketId;
    }

    private static int CompareGoalProjectionHeapNodes(Fix64 leftDistance, int leftBucketId, Fix64 rightDistance, int rightBucketId)
    {
        int distanceOrder = leftDistance.CompareTo(rightDistance);
        return distanceOrder != 0 ? distanceOrder : leftBucketId.CompareTo(rightBucketId);
    }

    private static Fix64 GetGoalProjectionBucketLowerBoundSquared(
        NavigationWorld world,
        GoalProjectionSpatialIndex index,
        int bucketId,
        FixVector2 desiredWorld)
    {
        int bucketX = bucketId % index.BucketCountX;
        int bucketY = bucketId / index.BucketCountX;
        int minX = bucketX * GoalProjectionSpatialIndex.BucketSizeInCells;
        int minY = bucketY * GoalProjectionSpatialIndex.BucketSizeInCells;
        int maxX = Math.Min(world.Width - 1, minX + GoalProjectionSpatialIndex.BucketSizeInCells - 1);
        int maxY = Math.Min(world.Height - 1, minY + GoalProjectionSpatialIndex.BucketSizeInCells - 1);
        world.GetGridCellBoundsFixed(minX, minY, out FixVector2 minimum, out _);
        world.GetGridCellBoundsFixed(maxX, maxY, out _, out FixVector2 maximum);
        Fix64 dx = desiredWorld.x < minimum.x
            ? minimum.x - desiredWorld.x
            : desiredWorld.x > maximum.x ? desiredWorld.x - maximum.x : Fix64.Zero;
        Fix64 dz = desiredWorld.y < minimum.y
            ? minimum.y - desiredWorld.y
            : desiredWorld.y > maximum.y ? desiredWorld.y - maximum.y : Fix64.Zero;
        return dx * dx + dz * dz;
    }

    private static bool TryFindNearestWalkableInIslandByWorldDistanceFixed(
        NavigationWorld world,
        int centerX,
        int centerY,
        FixVector2 desiredWorld,
        int islandId,
        int radius,
        bool allowFullIslandSearch,
        out int resultX,
        out int resultY,
        out Fix64 distance)
    {
        resultX = 0;
        resultY = 0;
        distance = Fix64.FromRaw(long.MaxValue);
        if (world == null || islandId <= 0)
            return false;

        Fix64 bestDistanceSquared = Fix64.FromRaw(long.MaxValue);
        bool found = TryFindNearestWalkableInIslandByWorldDistanceInBounds(
            world,
            Math.Max(0, centerX - radius),
            Math.Min(world.Width - 1, centerX + radius),
            Math.Max(0, centerY - radius),
            Math.Min(world.Height - 1, centerY + radius),
            desiredWorld,
            islandId,
            ref resultX,
            ref resultY,
            ref bestDistanceSquared);
        if (!found && allowFullIslandSearch)
        {
            found = TryFindNearestWalkableInIslandByWorldDistanceFromSpatialIndex(
                world,
                centerX,
                centerY,
                desiredWorld,
                islandId,
                out resultX,
                out resultY,
                out distance);
            if (found)
                return true;
        }

        if (found)
            distance = Fix64.Sqrt(bestDistanceSquared);
        return found;
    }

    private static bool TryFindNearestWalkableInIslandByWorldDistance(
        NavigationWorld world,
        int centerX,
        int centerY,
        Vector3 desiredWorld,
        int islandId,
        int radius,
        bool allowFullIslandSearch,
        out int resultX,
        out int resultY,
        out float distance)
    {
        resultX = 0;
        resultY = 0;
        distance = float.PositiveInfinity;
        if (world == null || islandId <= 0)
            return false;

        FixVector2 desiredFixed = new FixVector2((Fix64)desiredWorld.x, (Fix64)desiredWorld.z);
        Fix64 bestDistanceSquared = Fix64.FromRaw(long.MaxValue);
        bool found = TryFindNearestWalkableInIslandByWorldDistanceInBounds(
            world,
            Mathf.Max(0, centerX - radius),
            Mathf.Min(world.Width - 1, centerX + radius),
            Mathf.Max(0, centerY - radius),
            Mathf.Min(world.Height - 1, centerY + radius),
            desiredFixed,
            islandId,
            ref resultX,
            ref resultY,
            ref bestDistanceSquared);

        if (found || !allowFullIslandSearch)
        {
            if (found)
                distance = (float)Fix64.Sqrt(bestDistanceSquared);
            return found;
        }

        found = TryFindNearestWalkableInIslandByWorldDistanceFromSpatialIndex(
            world,
            centerX,
            centerY,
            desiredFixed,
            islandId,
            out resultX,
            out resultY,
            out Fix64 fullDistance);
        if (found)
            distance = (float)fullDistance;
        return found;
    }

    private static bool TryFindNearestWalkableInIslandByWorldDistanceInBounds(
        NavigationWorld world,
        int minX,
        int maxX,
        int minY,
        int maxY,
        FixVector2 desiredWorld,
        int islandId,
        ref int resultX,
        ref int resultY,
        ref Fix64 bestDistanceSquared)
    {
        bool found = false;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!world.IsWalkable(x, y) || ResolveIslandId(world, x, y) != islandId)
                    continue;

                FixVector2 worldCenter = world.GridToWorldCenterFixed(x, y);
                Fix64 dx = worldCenter.x - desiredWorld.x;
                Fix64 dz = worldCenter.y - desiredWorld.y;
                Fix64 candidateDistanceSquared = dx * dx + dz * dz;
                if (found && candidateDistanceSquared >= bestDistanceSquared)
                    continue;

                resultX = x;
                resultY = y;
                bestDistanceSquared = candidateDistanceSquared;
                found = true;
            }
        }

        return found;
    }


}
