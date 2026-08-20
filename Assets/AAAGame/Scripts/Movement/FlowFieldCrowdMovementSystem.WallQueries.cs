using System;
using System.Collections.Generic;

public static partial class FlowFieldCrowdMovementSystem
{
    private readonly struct WallOverlayKey : IEquatable<WallOverlayKey>
    {
        public WallOverlayKey(int agentTypeId, int worldVersion, int topologyVersion, int factionId, long clearanceRaw)
        {
            AgentTypeId = agentTypeId;
            WorldVersion = worldVersion;
            TopologyVersion = topologyVersion;
            FactionId = factionId;
            ClearanceRaw = clearanceRaw;
        }

        public int AgentTypeId { get; }
        public int WorldVersion { get; }
        public int TopologyVersion { get; }
        public int FactionId { get; }
        public long ClearanceRaw { get; }

        public bool Equals(WallOverlayKey other) =>
            AgentTypeId == other.AgentTypeId
            && WorldVersion == other.WorldVersion
            && TopologyVersion == other.TopologyVersion
            && FactionId == other.FactionId
            && ClearanceRaw == other.ClearanceRaw;

        public override bool Equals(object obj) => obj is WallOverlayKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = AgentTypeId;
                hash = hash * 397 ^ WorldVersion;
                hash = hash * 397 ^ TopologyVersion;
                hash = hash * 397 ^ FactionId;
                hash = hash * 397 ^ ClearanceRaw.GetHashCode();
                return hash;
            }
        }
    }

    private static readonly Dictionary<WallOverlayKey, byte[]> s_WallOverlayCache = new();
    private static int[] s_WallAttackGoalMarks = Array.Empty<int>();
    private static int s_WallAttackGoalGeneration;
    private static int s_WallOverlayCacheTopologyVersion = -1;

    public static bool TryEstimateWallDetourToAttackAreaFixed(
        IEntityContext self,
        IEntityContext target,
        Fix64 attackRange,
        out Fix64 noWallDistance,
        out Fix64 wallDistance,
        out bool wallPathReachable,
        out string failureReason,
        out NavigationQueryFailureKind failureKind)
    {
        if (self == null)
            throw new ArgumentNullException(nameof(self));
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (attackRange < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(attackRange));

        noWallDistance = Fix64.Zero;
        wallDistance = Fix64.Zero;
        wallPathReachable = true;
        failureReason = string.Empty;
        failureKind = NavigationQueryFailureKind.None;
        if (!LogicWallRuntime.HasBuiltWalls)
            return true;

        int selfId = ResolveAgentId(self);
        if (!Agents.TryGetValue(selfId, out AgentRuntimeData agent))
        {
            RegisterSyntheticAgent(self);
            agent = Agents[selfId];
        }
        else
        {
            agent.PositionFixed = self.LogicFramePositionFixed();
            agent.Position = ToWorldVector3(agent.PositionFixed);
            agent.RadiusFixed = ResolveCollisionRadiusFixed(self);
            agent.Radius = (float)agent.RadiusFixed;
            if (self is ILogicFrameEntity logicEntity)
                agent.AgentTypeId = logicEntity.NavigationAgentTypeId;
        }

        if (!TryEnsureWorldBuilt(agent.AgentTypeId))
        {
            failureKind = NavigationQueryFailureKind.Unavailable;
            failureReason = $"wall detour world unavailable agentType={agent.AgentTypeId}";
            return false;
        }
        if (HasPendingRuntimeDirty(_activeWorldState))
        {
            failureKind = NavigationQueryFailureKind.PendingRuntimeUpdate;
            failureReason = $"wall detour runtime dirty pending agentType={agent.AgentTypeId}";
            return false;
        }
        if (!TryResolveStartCellForReachabilityFixed(self, out int startX, out int startY, out int startIsland))
        {
            failureKind = NavigationQueryFailureKind.Unreachable;
            failureReason = $"wall detour start reachability failed {BuildReachabilityStartDiagnostics(self)}";
            return false;
        }

        LogicCombatShape targetShape = LogicFrameRuntime.IsTicking
            ? LogicEntityFrameSnapshotService.GetRequiredCurrent(target).CombatShape
            : target.CombatShape;
        Fix64 navigationClearance = ResolveNavigationQueryClearanceFixed(_world, agent.RadiusFixed);
        int goalGeneration = BuildWallAttackGoalSet(
            targetShape,
            attackRange,
            agent.RadiusFixed,
            navigationClearance,
            startIsland);
        if (goalGeneration == 0)
        {
            failureKind = NavigationQueryFailureKind.Unreachable;
            failureReason = $"wall detour has no attack-area goal target={target.CharacterKey} range={attackRange}";
            return false;
        }

        int factionId = EntitySideHelper.ToFactionId(self.Side);
        byte[] blockedMask = GetWallOverlayMask(agent.AgentTypeId, factionId, navigationClearance);
        if (!TryFindShortestWallQueryPath(
                startX,
                startY,
                goalGeneration,
                null,
                out noWallDistance,
                out int noWallGoalIndex))
        {
            failureKind = NavigationQueryFailureKind.Unreachable;
            failureReason = $"wall detour baseline path is unreachable target={target.CharacterKey}";
            return false;
        }

        if (!DoesResolvedPathUseWall(startX, startY, noWallGoalIndex, blockedMask))
        {
            wallDistance = noWallDistance;
            return true;
        }

        if (!TryFindShortestWallQueryPath(
                startX,
                startY,
                goalGeneration,
                blockedMask,
                out wallDistance,
                out _))
        {
            wallPathReachable = false;
            wallDistance = Fix64.FromRaw(long.MaxValue);
        }
        return true;
    }

    private static int BuildWallAttackGoalSet(
        LogicCombatShape targetShape,
        Fix64 attackRange,
        Fix64 agentRadius,
        Fix64 navigationClearance,
        int startIsland)
    {
        int cellCount = checked(_world.Width * _world.Height);
        if (s_WallAttackGoalMarks.Length < cellCount)
        {
            s_WallAttackGoalMarks = new int[cellCount];
            s_WallAttackGoalGeneration = 0;
        }
        if (s_WallAttackGoalGeneration == int.MaxValue)
        {
            Array.Clear(s_WallAttackGoalMarks, 0, s_WallAttackGoalMarks.Length);
            s_WallAttackGoalGeneration = 0;
        }
        int generation = ++s_WallAttackGoalGeneration;

        if (!_world.WorldToGridFixed(targetShape.Center, out int targetX, out int targetY))
            return 0;
        Fix64 extentX;
        Fix64 extentY;
        switch (targetShape.Kind)
        {
            case LogicCombatShapeKind.Circle:
                extentX = targetShape.Radius;
                extentY = targetShape.Radius;
                break;
            case LogicCombatShapeKind.AxisAlignedBox:
                extentX = targetShape.HalfExtents.x;
                extentY = targetShape.HalfExtents.y;
                break;
            default:
                throw new InvalidOperationException($"Unknown combat shape kind {(int)targetShape.Kind}.");
        }

        int radiusX = Math.Max(
            1,
            NavigationGridFixedMath.DivideCeilingByCellSize(extentX + attackRange, _world.CellSizeGridRaw) + 1);
        int radiusY = Math.Max(
            1,
            NavigationGridFixedMath.DivideCeilingByCellSize(extentY + attackRange, _world.CellSizeGridRaw) + 1);
        Fix64 minimumSurfaceDistance = agentRadius + Fix64.FromRaw(205);
        int count = 0;
        for (int y = Math.Max(0, targetY - radiusY); y <= Math.Min(_world.Height - 1, targetY + radiusY); y++)
        {
            for (int x = Math.Max(0, targetX - radiusX); x <= Math.Min(_world.Width - 1, targetX + radiusX); x++)
            {
                if (!_world.IsWalkable(x, y) || ResolveIslandIdForDiagnostics(_world, x, y) != startIsland)
                    continue;
                FixVector2 candidate = _world.GridToWorldCenterFixed(x, y);
                Fix64 surfaceDistance = targetShape.DistanceToSurface(candidate);
                if (surfaceDistance > attackRange || surfaceDistance < minimumSurfaceDistance)
                    continue;
                if (!IsNavigationPointClearFixed(
                        _world,
                        candidate,
                        navigationClearance,
                        includeRuntimeObstacleOverlay: true))
                    continue;
                s_WallAttackGoalMarks[_world.GetIndex(x, y)] = generation;
                count++;
            }
        }
        return count > 0 ? generation : 0;
    }

    private static byte[] GetWallOverlayMask(int agentTypeId, int factionId, Fix64 clearance)
    {
        int topologyVersion = LogicWallRuntime.TopologyVersion;
        if (s_WallOverlayCacheTopologyVersion != topologyVersion)
        {
            s_WallOverlayCache.Clear();
            s_WallOverlayCacheTopologyVersion = topologyVersion;
        }
        var key = new WallOverlayKey(agentTypeId, _world.Version, topologyVersion, factionId, clearance.RawValue);
        if (s_WallOverlayCache.TryGetValue(key, out byte[] cached))
            return cached;

        int cellCount = checked(_world.Width * _world.Height);
        var mask = new byte[cellCount];
        IReadOnlyList<LogicStaticCollisionObstacle> obstacles =
            LogicWallRuntime.GetCollisionObstaclesForFaction(factionId);
        for (int y = 0; y < _world.Height; y++)
        {
            for (int x = 0; x < _world.Width; x++)
            {
                if (!_world.IsWalkable(x, y))
                    continue;
                FixVector2 center = _world.GridToWorldCenterFixed(x, y);
                for (int obstacleIndex = 0; obstacleIndex < obstacles.Count; obstacleIndex++)
                {
                    LogicStaticCollisionObstacle obstacle = obstacles[obstacleIndex];
                    Fix64 dx = Fix64.Max(
                        Fix64.Abs(center.x - obstacle.Center.x) - obstacle.HalfExtents.x,
                        Fix64.Zero);
                    Fix64 dy = Fix64.Max(
                        Fix64.Abs(center.y - obstacle.Center.y) - obstacle.HalfExtents.y,
                        Fix64.Zero);
                    if (dx * dx + dy * dy > clearance * clearance)
                        continue;
                    mask[_world.GetIndex(x, y)] = 1;
                    break;
                }
            }
        }
        s_WallOverlayCache.Add(key, mask);
        return mask;
    }

    private static bool TryFindShortestWallQueryPath(
        int startX,
        int startY,
        int goalGeneration,
        byte[] blockedMask,
        out Fix64 distance,
        out int goalIndex)
    {
        distance = Fix64.Zero;
        goalIndex = -1;
        int searchId = BeginDistanceEstimateSearch(checked(_world.Width * _world.Height));
        int startIndex = _world.GetIndex(startX, startY);
        FixedDistanceEstimateCosts[startIndex] = 0L;
        DistanceEstimateVisitedMarks[startIndex] = searchId;
        DistanceEstimateParents[startIndex] = -1;
        FixedDistanceEstimateOpenSet.Push(startIndex, 0L);
        Fix64 diagonalStepCost = _world.CellSizeFixed * Fix64.Sqrt((Fix64)2);

        while (FixedDistanceEstimateOpenSet.Count > 0)
        {
            DeterministicCostQueueNode node = FixedDistanceEstimateOpenSet.Pop();
            int currentIndex = node.Index;
            if (DistanceEstimateClosedMarks[currentIndex] == searchId)
                continue;
            if (s_WallAttackGoalMarks[currentIndex] == goalGeneration)
            {
                distance = Fix64.FromRaw(FixedDistanceEstimateCosts[currentIndex]);
                goalIndex = currentIndex;
                return true;
            }

            DistanceEstimateClosedMarks[currentIndex] = searchId;
            int currentX = currentIndex % _world.Width;
            int currentY = currentIndex / _world.Width;
            long currentCostRaw = FixedDistanceEstimateCosts[currentIndex];
            for (int i = 0; i < NeighborOffsetX.Length; i++)
            {
                int nextX = currentX + NeighborOffsetX[i];
                int nextY = currentY + NeighborOffsetY[i];
                if (!CanTraverseNeighborCells(_world, currentX, currentY, nextX, nextY)
                    || IsWallOverlayStepBlocked(currentX, currentY, nextX, nextY, blockedMask))
                    continue;
                int nextIndex = _world.GetIndex(nextX, nextY);
                if (DistanceEstimateClosedMarks[nextIndex] == searchId)
                    continue;
                long stepCostRaw = NeighborOffsetX[i] != 0 && NeighborOffsetY[i] != 0
                    ? diagonalStepCost.RawValue
                    : _world.CellSizeFixed.RawValue;
                long nextCostRaw = checked(currentCostRaw + stepCostRaw);
                if (DistanceEstimateVisitedMarks[nextIndex] == searchId
                    && nextCostRaw >= FixedDistanceEstimateCosts[nextIndex])
                    continue;
                DistanceEstimateVisitedMarks[nextIndex] = searchId;
                FixedDistanceEstimateCosts[nextIndex] = nextCostRaw;
                DistanceEstimateParents[nextIndex] = currentIndex;
                FixedDistanceEstimateOpenSet.Push(nextIndex, nextCostRaw);
            }
        }
        return false;
    }

    private static bool IsWallOverlayStepBlocked(
        int currentX,
        int currentY,
        int nextX,
        int nextY,
        byte[] blockedMask)
    {
        if (blockedMask == null)
            return false;
        if (blockedMask[_world.GetIndex(nextX, nextY)] != 0)
            return true;
        if (currentX == nextX || currentY == nextY)
            return false;
        return blockedMask[_world.GetIndex(nextX, currentY)] != 0
               || blockedMask[_world.GetIndex(currentX, nextY)] != 0;
    }

    private static bool DoesResolvedPathUseWall(int startX, int startY, int goalIndex, byte[] blockedMask)
    {
        int startIndex = _world.GetIndex(startX, startY);
        int current = goalIndex;
        int guard = 0;
        while (current != startIndex)
        {
            if (current < 0 || current >= blockedMask.Length)
                throw new InvalidOperationException($"Wall path reconstruction reached invalid cell {current}.");
            if (blockedMask[current] != 0)
                return true;
            int parent = DistanceEstimateParents[current];
            if (parent < 0 || ++guard > blockedMask.Length)
                throw new InvalidOperationException("Wall path reconstruction did not reach its start cell.");
            int currentX = current % _world.Width;
            int currentY = current / _world.Width;
            int parentX = parent % _world.Width;
            int parentY = parent / _world.Width;
            if (currentX != parentX && currentY != parentY
                && (blockedMask[_world.GetIndex(currentX, parentY)] != 0
                    || blockedMask[_world.GetIndex(parentX, currentY)] != 0))
                return true;
            current = parent;
        }
        return blockedMask[startIndex] != 0;
    }
}
