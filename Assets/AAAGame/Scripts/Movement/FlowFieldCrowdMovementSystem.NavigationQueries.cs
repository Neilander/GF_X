using System;
using System.Collections.Generic;
using System.Text;
using Stopwatch = System.Diagnostics.Stopwatch;
using AAAGame.FlowPath;
using UnityEngine;
using MainThreadFrameProfiler = UnityGameFramework.Runtime.MainThreadFrameProfiler;
using MainThreadPerfScope = UnityGameFramework.Runtime.MainThreadPerfScope;

public static partial class FlowFieldCrowdMovementSystem
{
#if UNITY_EDITOR
    private static string BuildNavigationWorldCellDiagnostics(NavigationWorld world, int worldX, int worldY)
    {
        if (world == null)
            return "<missing>";
        if (worldX < 0 || worldX >= world.Width || worldY < 0 || worldY >= world.Height)
            return $"out,size={world.Width}x{world.Height}";

        int index = world.GetIndex(worldX, worldY);
        return $"walk={world.WalkableMask[index]},base={world.BaseWalkableMask[index]}," +
               $"version={world.Version},size={world.Width}x{world.Height}";
    }

    public static bool TryGetEditorTestSectorUniformIslandId(int worldX, int worldY, out int uniformIslandId)
    {
        uniformIslandId = -1;
        if (_world == null || !TryGetSectorForCell(_world, worldX, worldY, out SectorData sector))
            return false;

        uniformIslandId = sector.UniformIslandId;
        return true;
    }

    public static bool TryGetEditorTestWorldToGridFixed(FixVector2 position, out int worldX, out int worldY)
    {
        worldX = 0;
        worldY = 0;
        return _world != null && _world.WorldToGridFixed(position, out worldX, out worldY);
    }

    public static bool TryGetEditorTestCircleObstacleFixed(
        int obstacleId,
        out FixVector2 center,
        out Fix64 radius)
    {
        center = FixVector2.Zero;
        radius = Fix64.Zero;
        if (!CircleObstacles.TryGetValue(obstacleId, out CircleObstacle obstacle) || obstacle == null)
            return false;
        center = obstacle.PositionFixed;
        radius = obstacle.RadiusFixed;
        return true;
    }

    public static bool TryGetEditorTestBoxObstacleFixed(
        int obstacleId,
        out FixVector2 center,
        out FixVector2 halfExtents)
    {
        center = FixVector2.Zero;
        halfExtents = FixVector2.Zero;
        if (!BoxObstacles.TryGetValue(obstacleId, out BoxObstacle obstacle) || obstacle == null)
            return false;
        center = obstacle.CenterFixed;
        halfExtents = obstacle.HalfExtentsFixed;
        return true;
    }
#endif

    public static void RegisterCircleObstacle(int obstacleId, Vector3 position, float radius)
    {
        if (float.IsNaN(position.x) || float.IsInfinity(position.x)
            || float.IsNaN(position.y) || float.IsInfinity(position.y)
            || float.IsNaN(position.z) || float.IsInfinity(position.z)
            || float.IsNaN(radius) || float.IsInfinity(radius))
            throw new ArgumentOutOfRangeException(nameof(position), "Circle obstacle values must be finite.");
        Fix64 clampedRadiusFixed = Fix64.Max(Fix64.FromRaw(41), (Fix64)radius);
        FixVector2 positionFixed = new FixVector2((Fix64)position.x, (Fix64)position.z);
        RegisterCircleObstacleFixed(obstacleId, positionFixed, clampedRadiusFixed);
    }

    public static void RegisterCircleObstacleFixed(int obstacleId, FixVector2 position, Fix64 radius)
    {
        if (radius <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "Circle obstacle radius must be positive.");

        bool replacedExisting = TryRemoveRuntimeObstacle(
            obstacleId,
            out FixVector2 previousBoundsMinimum,
            out FixVector2 previousBoundsMaximum);
        CircleObstacles[obstacleId] = new CircleObstacle
        {
            Id = obstacleId,
            Position = new Vector3((float)position.x, 0f, (float)position.y),
            Radius = (float)radius,
            PositionFixed = position,
            RadiusFixed = radius,
        };
        _staticCollisionObstacleSnapshotDirty = true;
        if (replacedExisting)
            MarkRuntimeObstacleDirty(previousBoundsMinimum, previousBoundsMaximum);
        FixVector2 radiusExtents = new FixVector2(radius, radius);
        MarkRuntimeObstacleDirty(position - radiusExtents, position + radiusExtents);
    }

    public static void RegisterBoxObstacle(int obstacleId, Vector3 center, Vector3 halfExtents)
    {
        if (float.IsNaN(center.x) || float.IsInfinity(center.x)
            || float.IsNaN(center.y) || float.IsInfinity(center.y)
            || float.IsNaN(center.z) || float.IsInfinity(center.z)
            || float.IsNaN(halfExtents.x) || float.IsInfinity(halfExtents.x)
            || float.IsNaN(halfExtents.z) || float.IsInfinity(halfExtents.z)
            || halfExtents.x < 0f || halfExtents.z < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(halfExtents), "Box obstacle values must be finite and non-negative.");
        }

        FixVector2 centerFixed = new FixVector2((Fix64)center.x, (Fix64)center.z);
        FixVector2 halfExtentsFixed = new FixVector2((Fix64)halfExtents.x, (Fix64)halfExtents.z);
        RegisterBoxObstacleFixed(obstacleId, centerFixed, halfExtentsFixed);
    }

    public static void RegisterBoxObstacleFixed(int obstacleId, FixVector2 center, FixVector2 halfExtents)
    {
        if (halfExtents.x < Fix64.Zero || halfExtents.y < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(halfExtents), halfExtents, "Box obstacle half extents must be non-negative.");

        bool replacedExisting = TryRemoveRuntimeObstacle(
            obstacleId,
            out FixVector2 previousBoundsMinimum,
            out FixVector2 previousBoundsMaximum);
        BoxObstacles[obstacleId] = new BoxObstacle
        {
            Id = obstacleId,
            Center = new Vector3((float)center.x, 0f, (float)center.y),
            HalfExtents = new Vector3((float)halfExtents.x, 0f, (float)halfExtents.y),
            CenterFixed = center,
            HalfExtentsFixed = halfExtents,
        };
        _staticCollisionObstacleSnapshotDirty = true;
        if (replacedExisting)
            MarkRuntimeObstacleDirty(previousBoundsMinimum, previousBoundsMaximum);
        FixVector2 dirtyHalfExtents = ResolveBoxObstacleDirtyHalfExtentsFixed(halfExtents);
        MarkRuntimeObstacleDirty(center - dirtyHalfExtents, center + dirtyHalfExtents);
    }

    public static void RegisterBoxCostStamp(int stampId, Vector3 center, Vector3 halfExtents, byte cost)
    {
        RegisterBoxCostStamp(stampId, AnyAgentTypeId, center, halfExtents, cost);
    }

    public static void RegisterBoxCostStamp(int stampId, int agentTypeId, Vector3 center, Vector3 halfExtents, byte cost)
    {
        if (cost == 0 || cost >= 255)
            throw new InvalidOperationException($"RegisterBoxCostStamp failed: cost must be in 1..254, cost={cost}.");
        if (!IsFiniteVector3(center) || !IsFiniteVector3(halfExtents) || halfExtents.x < 0f || halfExtents.z < 0f)
            throw new ArgumentOutOfRangeException(nameof(halfExtents), "Box cost stamp values must be finite and non-negative.");

        FixVector2 centerFixed = new FixVector2((Fix64)center.x, (Fix64)center.z);
        FixVector2 halfExtentsFixed = new FixVector2((Fix64)halfExtents.x, (Fix64)halfExtents.z);
        Vector3 quantizedCenter = new Vector3((float)centerFixed.x, center.y, (float)centerFixed.y);
        Vector3 quantizedHalfExtents = new Vector3((float)halfExtentsFixed.x, halfExtents.y, (float)halfExtentsFixed.y);
        Bounds bounds = new Bounds(quantizedCenter, quantizedHalfExtents * 2f);
        SetCostStamp(new CostStamp
        {
            Id = stampId,
            AgentTypeId = agentTypeId,
            Bounds = bounds,
            BoundsMinFixed = centerFixed - halfExtentsFixed,
            BoundsMaxFixed = centerFixed + halfExtentsFixed,
            Cost = cost
        });
        MarkRuntimeObstacleDirty(centerFixed - halfExtentsFixed, centerFixed + halfExtentsFixed);
    }

    public static void RegisterGridCostStamp(int stampId, Vector3 origin, float cellSize, int width, int height, byte[] costs)
    {
        RegisterGridCostStamp(stampId, AnyAgentTypeId, origin, cellSize, width, height, costs);
    }

    public static void RegisterGridCostStamp(int stampId, int agentTypeId, Vector3 origin, float cellSize, int width, int height, byte[] costs)
    {
        if (!IsFiniteVector3(origin) || float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0.0001f)
            throw new InvalidOperationException($"RegisterGridCostStamp failed: cellSize must be positive, cellSize={cellSize}.");
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"RegisterGridCostStamp failed: invalid size {width}x{height}.");
        if (costs == null)
            throw new InvalidOperationException("RegisterGridCostStamp failed: costs is null.");
        if (costs.Length != width * height)
            throw new InvalidOperationException($"RegisterGridCostStamp failed: costs length {costs.Length} does not match {width}x{height}.");

        for (int i = 0; i < costs.Length; i++)
        {
            byte cost = costs[i];
            if (cost == 0 || cost >= 255)
                throw new InvalidOperationException($"RegisterGridCostStamp failed: cost must be in 1..254 at index={i}, cost={cost}.");
        }

        long cellSizeGridRaw = NavigationGridFixedMath.FloatToGridRaw(cellSize);
        if (cellSizeGridRaw <= 0)
            throw new InvalidOperationException($"RegisterGridCostStamp failed: authored cell size must be positive, raw={cellSizeGridRaw}.");
        long originXGridRaw = NavigationGridFixedMath.FloatToGridRaw(origin.x);
        long originZGridRaw = NavigationGridFixedMath.FloatToGridRaw(origin.z);
        long maximumXGridRaw = checked(originXGridRaw + checked((long)width * cellSizeGridRaw));
        long maximumZGridRaw = checked(originZGridRaw + checked((long)height * cellSizeGridRaw));
        Fix64 cellSizeFixed = NavigationGridFixedMath.GridRawToFix64(cellSizeGridRaw);
        FixVector2 originFixed = new FixVector2(
            NavigationGridFixedMath.GridRawToFix64Floor(originXGridRaw),
            NavigationGridFixedMath.GridRawToFix64Floor(originZGridRaw));
        FixVector2 maximumFixed = new FixVector2(
            NavigationGridFixedMath.GridRawToFix64Ceiling(maximumXGridRaw),
            NavigationGridFixedMath.GridRawToFix64Ceiling(maximumZGridRaw));
        Vector3 quantizedOrigin = new Vector3((float)originFixed.x, origin.y, (float)originFixed.y);
        Vector3 size = new Vector3((float)(maximumFixed.x - originFixed.x), 0f, (float)(maximumFixed.y - originFixed.y));
        Bounds bounds = new Bounds(quantizedOrigin + size * 0.5f, size);
        SetCostStamp(new CostStamp
        {
            Id = stampId,
            AgentTypeId = agentTypeId,
            Bounds = bounds,
            BoundsMinFixed = originFixed,
            BoundsMaxFixed = maximumFixed,
            Cost = 1,
            Width = width,
            Height = height,
            CellSize = (float)cellSizeFixed,
            CellSizeFixed = cellSizeFixed,
            CellSizeGridRaw = cellSizeGridRaw,
            Origin = quantizedOrigin,
            OriginFixed = originFixed,
            OriginXGridRaw = originXGridRaw,
            OriginZGridRaw = originZGridRaw,
            Costs = (byte[])costs.Clone()
        });
        MarkRuntimeObstacleDirty(originFixed, maximumFixed);
    }

    public static void UnregisterCostStamp(int stampId)
    {
        if (!CostStamps.TryGetValue(stampId, out CostStamp stamp))
            return;

        RemoveCostStamp(stampId);
        MarkRuntimeObstacleDirty(stamp.BoundsMinFixed, stamp.BoundsMaxFixed);
    }

    private static bool IsFiniteVector3(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
               && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
               && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private static FixVector2[] CreateNavigationAnchorFixedXZSnapshot(Vector3[] anchors)
    {
        if (anchors == null)
            return null;

        var result = new FixVector2[anchors.Length];
        for (int i = 0; i < anchors.Length; i++)
        {
            Vector3 anchor = anchors[i];
            if (!IsFiniteVector3(anchor))
                throw new InvalidOperationException($"Cannot freeze a non-finite navigation anchor. index={i} value={anchor}.");
            result[i] = new FixVector2((Fix64)anchor.x, (Fix64)anchor.z);
        }
        return result;
    }

    public static void UnregisterObstacle(int obstacleId)
    {
        if (TryRemoveRuntimeObstacle(obstacleId, out FixVector2 boundsMinimum, out FixVector2 boundsMaximum))
            MarkRuntimeObstacleDirty(boundsMinimum, boundsMaximum);
    }

    private static bool TryRemoveRuntimeObstacle(
        int obstacleId,
        out FixVector2 boundsMinimum,
        out FixVector2 boundsMaximum)
    {
        bool hasCircle = CircleObstacles.TryGetValue(obstacleId, out CircleObstacle circle);
        bool hasBox = BoxObstacles.TryGetValue(obstacleId, out BoxObstacle box);
        if (hasCircle && hasBox)
        {
            throw new InvalidOperationException(
                $"Runtime obstacle {obstacleId} is registered as both circle and box.");
        }

        if (hasCircle)
        {
            if (!CircleObstacles.Remove(obstacleId))
                throw new InvalidOperationException($"Circle obstacle {obstacleId} disappeared during replacement.");
            _staticCollisionObstacleSnapshotDirty = true;
            FixVector2 radiusExtents = new FixVector2(circle.RadiusFixed, circle.RadiusFixed);
            boundsMinimum = circle.PositionFixed - radiusExtents;
            boundsMaximum = circle.PositionFixed + radiusExtents;
            return true;
        }

        if (hasBox)
        {
            if (!BoxObstacles.Remove(obstacleId))
                throw new InvalidOperationException($"Box obstacle {obstacleId} disappeared during replacement.");
            _staticCollisionObstacleSnapshotDirty = true;
            FixVector2 dirtyHalfExtents = ResolveBoxObstacleDirtyHalfExtentsFixed(box.HalfExtentsFixed);
            boundsMinimum = box.CenterFixed - dirtyHalfExtents;
            boundsMaximum = box.CenterFixed + dirtyHalfExtents;
            return true;
        }

        boundsMinimum = FixVector2.Zero;
        boundsMaximum = FixVector2.Zero;
        return false;
    }

    public static bool IsPositionOccupiedByAgentFixed(FixVector2 position, Fix64 requiredDistance)
    {
        if (requiredDistance < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(requiredDistance), requiredDistance.RawValue, "Required distance cannot be negative.");

        Fix64 requiredDistanceSq = requiredDistance * requiredDistance;
        for (int i = 0; i < OrderedAgentIds.Count; i++)
        {
            AgentRuntimeData agent = Agents[OrderedAgentIds[i]];
            if (agent.IgnoreAgentCollision)
                continue;
            if (FixVector2.SqrMagnitude(agent.PositionFixed - position) < requiredDistanceSq)
                return true;
        }

        return false;
    }

    public static bool TryConstrainNavigationDisplacement(
        Vector3 position,
        Vector3 desiredDisplacement,
        int agentTypeId,
        float edgeClearance,
        out Vector3 constrainedDisplacement)
    {
        BeginPerfCall();
        long constraintStartTicks = GetDiagnosticTimestamp();
        _perf.NavigationConstraintCalls++;
        constrainedDisplacement = Vector3.zero;
        Vector3 horizontal = desiredDisplacement;
        horizontal.y = 0f;
        if (horizontal.sqrMagnitude <= 0.000001f)
        {
            constrainedDisplacement = desiredDisplacement;
            _perf.NavigationConstraintTicks += GetDiagnosticTimestamp() - constraintStartTicks;
            return true;
        }

        long sectionStartTicks = GetDiagnosticTimestamp();
        if (!TryGetCommittedNavigationQueryWorld(agentTypeId, allowSynchronousBuild: true, out NavigationWorld world))
        {
            _perf.NavigationConstraintWorldTicks += GetDiagnosticTimestamp() - sectionStartTicks;
            _perf.NavigationConstraintTicks += GetDiagnosticTimestamp() - constraintStartTicks;
            return false;
        }
        _perf.NavigationConstraintWorldTicks += GetDiagnosticTimestamp() - sectionStartTicks;

        bool includeRuntimeObstacleOverlay = HasPendingRuntimeDirty(_activeWorldState);
        edgeClearance = ResolveNavigationExecutionClearance(world, Mathf.Max(0f, edgeClearance));
        sectionStartTicks = GetDiagnosticTimestamp();
        if (IsNavigationSegmentWalkable(world, position, horizontal, edgeClearance, includeRuntimeObstacleOverlay: includeRuntimeObstacleOverlay))
        {
            constrainedDisplacement = desiredDisplacement;
            _perf.NavigationConstraintDirectTicks += GetDiagnosticTimestamp() - sectionStartTicks;
            _perf.NavigationConstraintTicks += GetDiagnosticTimestamp() - constraintStartTicks;
            return true;
        }
        _perf.NavigationConstraintDirectTicks += GetDiagnosticTimestamp() - sectionStartTicks;
        _perf.NavigationConstraintBlocked++;

        bool startIsClear = IsNavigationPointClear(world, position, edgeClearance, includeRuntimeObstacleOverlay);
        float desiredDistance = horizontal.magnitude;
        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;

        sectionStartTicks = GetDiagnosticTimestamp();
        TrySelectNavigationDirectionalFanCandidate(world, position, horizontal, desiredDistance, edgeClearance, startIsClear, includeRuntimeObstacleOverlay, ref best, ref bestScore);
        TrySelectNavigationDisplacementCandidate(world, position, new Vector3(horizontal.x, 0f, 0f), horizontal, desiredDistance, edgeClearance, startIsClear, includeRuntimeObstacleOverlay, ref best, ref bestScore);
        TrySelectNavigationDisplacementCandidate(world, position, new Vector3(0f, 0f, horizontal.z), horizontal, desiredDistance, edgeClearance, startIsClear, includeRuntimeObstacleOverlay, ref best, ref bestScore);

        Vector3 tangentA = new Vector3(-horizontal.z, 0f, horizontal.x);
        Vector3 tangentB = -tangentA;
        TrySelectNavigationDisplacementCandidate(world, position, tangentA, horizontal, desiredDistance, edgeClearance, startIsClear, includeRuntimeObstacleOverlay, ref best, ref bestScore);
        TrySelectNavigationDisplacementCandidate(world, position, tangentB, horizontal, desiredDistance, edgeClearance, startIsClear, includeRuntimeObstacleOverlay, ref best, ref bestScore);
        _perf.NavigationConstraintCandidateTicks += GetDiagnosticTimestamp() - sectionStartTicks;

        Vector3 slideCandidate = Vector3.zero;
        bool hasSlideCandidate = false;
        sectionStartTicks = GetDiagnosticTimestamp();
        if (startIsClear && TryResolveNavigationSegmentBoundaryNormal(world, position, horizontal, edgeClearance, out Vector3 clearanceNormal))
        {
            Vector3 slide = Vector3.ProjectOnPlane(horizontal, clearanceNormal);
            slide.y = 0f;
            if (slide.sqrMagnitude > 0.000001f)
            {
                slideCandidate = slide.normalized * desiredDistance;
                hasSlideCandidate = IsNavigationSegmentWalkable(world, position, slideCandidate, edgeClearance, startIsClear, includeRuntimeObstacleOverlay);
                TrySelectNavigationDisplacementCandidate(world, position, slide.normalized * desiredDistance, horizontal, desiredDistance, edgeClearance, startIsClear, includeRuntimeObstacleOverlay, ref best, ref bestScore, preserveDistance: false);
            }

            if (!hasSlideCandidate)
            {
                Vector3 tangent = new Vector3(-clearanceNormal.z, 0f, clearanceNormal.x);
                if (tangent.sqrMagnitude > 0.000001f)
                {
                    tangent.Normalize();
                    if (Vector3.Dot(tangent, horizontal) < 0f)
                        tangent = -tangent;

                    slideCandidate = tangent * desiredDistance;
                    hasSlideCandidate = IsNavigationSegmentWalkable(world, position, slideCandidate, edgeClearance, startIsClear, includeRuntimeObstacleOverlay);
                    TrySelectNavigationDisplacementCandidate(world, position, slideCandidate, horizontal, desiredDistance, edgeClearance, startIsClear, includeRuntimeObstacleOverlay, ref best, ref bestScore, preserveDistance: false);
                    if (!hasSlideCandidate)
                    {
                        Vector3 oppositeSlide = -slideCandidate;
                        bool hasOppositeSlide = IsNavigationSegmentWalkable(world, position, oppositeSlide, edgeClearance, startIsClear, includeRuntimeObstacleOverlay);
                        TrySelectNavigationDisplacementCandidate(world, position, oppositeSlide, horizontal, desiredDistance, edgeClearance, startIsClear, includeRuntimeObstacleOverlay, ref best, ref bestScore, preserveDistance: false);
                        if (hasOppositeSlide)
                        {
                            slideCandidate = oppositeSlide;
                            hasSlideCandidate = true;
                        }
                    }
                }
            }
        }
        _perf.NavigationConstraintBoundaryTicks += GetDiagnosticTimestamp() - sectionStartTicks;

        sectionStartTicks = GetDiagnosticTimestamp();
        Vector3 limited = FindLongestWalkablePrefix(world, position, horizontal, edgeClearance, startIsClear, includeRuntimeObstacleOverlay);
        TrySelectNavigationDisplacementCandidate(world, position, limited, horizontal, limited.magnitude, edgeClearance, startIsClear, includeRuntimeObstacleOverlay, ref best, ref bestScore, preserveDistance: false);
        _perf.NavigationConstraintPrefixTicks += GetDiagnosticTimestamp() - sectionStartTicks;
        float minimumUsefulDistance = Mathf.Min(desiredDistance, 0.02f);
        if (hasSlideCandidate
            && best.magnitude < minimumUsefulDistance)
        {
            best = slideCandidate;
            bestScore = Mathf.Max(bestScore, 0f);
        }

        sectionStartTicks = GetDiagnosticTimestamp();
        if (!startIsClear && TryResolveNavigationClearanceRecoveryDirection(world, position, edgeClearance, out Vector3 recoveryDirection))
        {
            TrySelectNavigationDisplacementCandidate(world, position, recoveryDirection, recoveryDirection, desiredDistance, edgeClearance, startIsClear, includeRuntimeObstacleOverlay, ref best, ref bestScore);
            Vector3 recoveryTangentA = new Vector3(-recoveryDirection.z, 0f, recoveryDirection.x);
            Vector3 recoveryTangentB = -recoveryTangentA;
            TrySelectNavigationDisplacementCandidate(world, position, (recoveryDirection + recoveryTangentA).normalized, recoveryDirection, desiredDistance, edgeClearance, startIsClear, includeRuntimeObstacleOverlay, ref best, ref bestScore);
            TrySelectNavigationDisplacementCandidate(world, position, (recoveryDirection + recoveryTangentB).normalized, recoveryDirection, desiredDistance, edgeClearance, startIsClear, includeRuntimeObstacleOverlay, ref best, ref bestScore);
        }
        _perf.NavigationConstraintRecoveryTicks += GetDiagnosticTimestamp() - sectionStartTicks;

        _perf.NavigationConstraintTicks += GetDiagnosticTimestamp() - constraintStartTicks;
        if (bestScore <= float.NegativeInfinity * 0.5f)
            return false;

        constrainedDisplacement = new Vector3(best.x, desiredDisplacement.y, best.z);
        return true;
    }

    internal static bool TryGetStaticCollisionShadowSource(
        int agentTypeId,
        out LogicStaticCollisionSourceData source)
    {
        source = default;
        if (!TryGetCommittedNavigationQueryWorld(agentTypeId, allowSynchronousBuild: false, out NavigationWorld world))
            return false;
        if (world.BaseWalkableMask == null || world.BaseWalkableMask.Length != world.Width * world.Height)
        {
            throw new InvalidOperationException(
                $"Static collision shadow source is invalid. agentType={agentTypeId}, world={world.Version}, size={world.Width}x{world.Height}.");
        }
        if (world.BaseNeighborTraversalMask != null
            && world.BaseNeighborTraversalMask.Length != world.Width * world.Height)
        {
            throw new InvalidOperationException(
                $"Static collision shadow source neighbor traversal mask is invalid. agentType={agentTypeId}, world={world.Version}, size={world.Width}x{world.Height}.");
        }

        source = new LogicStaticCollisionSourceData(
            world.AgentTypeId,
            world.Version,
            world.Width,
            world.Height,
            world.CellSizeGridRaw,
            world.EncodedCenterClearanceFixedRaw,
            world.OriginXGridRaw,
            world.OriginZGridRaw,
            world.BaseWalkableMask,
            world.BaseNeighborTraversalMask,
            world.StaticCollisionVertices,
            world.StaticCollisionPathStarts,
            ResolveStaticCollisionObstacleSnapshot());
        return true;
    }

    private static LogicStaticCollisionObstacle[] ResolveStaticCollisionObstacleSnapshot()
    {
        if (!_staticCollisionObstacleSnapshotDirty)
            return _staticCollisionObstacleSnapshot;

        StaticCollisionObstacleSnapshotBuilder.Clear();
        foreach (BoxObstacle obstacle in BoxObstacles.Values)
        {
            if (obstacle == null)
                throw new InvalidOperationException("Static collision box obstacle snapshot contains null.");
            StaticCollisionObstacleSnapshotBuilder.Add(new LogicStaticCollisionObstacle(
                obstacle.Id,
                LogicStaticCollisionObstacleKind.Box,
                obstacle.CenterFixed,
                obstacle.HalfExtentsFixed,
                Fix64.Zero));
        }
        foreach (CircleObstacle obstacle in CircleObstacles.Values)
        {
            if (obstacle == null)
                throw new InvalidOperationException("Static collision circle obstacle snapshot contains null.");
            StaticCollisionObstacleSnapshotBuilder.Add(new LogicStaticCollisionObstacle(
                obstacle.Id,
                LogicStaticCollisionObstacleKind.Circle,
                obstacle.PositionFixed,
                FixVector2.Zero,
                obstacle.RadiusFixed));
        }
        StaticCollisionObstacleSnapshotBuilder.Sort((left, right) =>
        {
            int idOrder = left.StableId.CompareTo(right.StableId);
            return idOrder != 0 ? idOrder : left.Kind.CompareTo(right.Kind);
        });
        _staticCollisionObstacleSnapshot = StaticCollisionObstacleSnapshotBuilder.ToArray();
        _staticCollisionObstacleSnapshotDirty = false;
        return _staticCollisionObstacleSnapshot;
    }

    private static bool TryResolveNavigationSegmentBoundaryNormal(
        NavigationWorld world,
        Vector3 position,
        Vector3 horizontalDisplacement,
        float edgeClearance,
        out Vector3 normal)
    {
        normal = Vector3.zero;
        if (world == null || horizontalDisplacement.sqrMagnitude <= 0.000001f)
            return false;

        Vector3 target = position + horizontalDisplacement;
        if (!world.WorldToGrid(target, out int targetX, out int targetY))
            return false;

        Vector2 moveDirection = new Vector2(horizontalDisplacement.x, horizontalDisplacement.z);
        if (moveDirection.sqrMagnitude <= 0.000001f)
            return false;
        moveDirection.Normalize();

        Vector2 bestNormal = Vector2.zero;
        float bestScore = float.NegativeInfinity;
        float probeDistance = Mathf.Max(edgeClearance, world.CellSize * 0.5f) + horizontalDisplacement.magnitude + 0.001f;
        int radius = Mathf.CeilToInt(probeDistance / Mathf.Max(world.CellSize, 0.001f)) + 1;
        for (int y = targetY - radius; y <= targetY + radius; y++)
        {
            for (int x = targetX - radius; x <= targetX + radius; x++)
            {
                if (world.IsWalkable(x, y))
                    continue;

                float minX = world.Origin.x + x * world.CellSize;
                float maxX = world.Origin.x + (x + 1) * world.CellSize;
                float minZ = world.Origin.z + y * world.CellSize;
                float maxZ = world.Origin.z + (y + 1) * world.CellSize;
                float nearestX = Mathf.Clamp(target.x, minX, maxX);
                float nearestZ = Mathf.Clamp(target.z, minZ, maxZ);
                float dx = target.x - nearestX;
                float dz = target.z - nearestZ;
                float distanceSq = dx * dx + dz * dz;
                if (distanceSq > probeDistance * probeDistance)
                    continue;

                Vector2 away = new Vector2(dx, dz);
                if (away.sqrMagnitude <= 0.000001f)
                    away = ResolveNavigationRectangleBoundaryNormal(target, minX, maxX, minZ, maxZ);
                if (away.sqrMagnitude <= 0.000001f)
                    continue;

                away.Normalize();
                float distance = Mathf.Sqrt(distanceSq);
                float headingPressure = Mathf.Max(0f, -Vector2.Dot(moveDirection, away));
                float score = headingPressure * 4f + Mathf.Clamp01((probeDistance - distance) / Mathf.Max(probeDistance, 0.001f));
                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestNormal = away;
            }
        }

        if (bestNormal.sqrMagnitude <= 0.000001f)
            return false;

        normal = new Vector3(bestNormal.x, 0f, bestNormal.y);
        return true;
    }

    private static bool TryResolveNavigationClearanceRecoveryDirection(NavigationWorld world, Vector3 point, float edgeClearance, out Vector3 direction)
    {
        direction = Vector3.zero;
        if (edgeClearance <= 0.0001f)
            return false;
        if (!world.WorldToGrid(point, out int cellX, out int cellY) || !world.IsWalkable(cellX, cellY))
            return false;

        Vector2 accumulated = Vector2.zero;
        Vector2 nearestDirection = Vector2.zero;
        float minDistanceSq = float.PositiveInfinity;
        int radius = Mathf.CeilToInt(edgeClearance / Mathf.Max(world.CellSize, 0.001f)) + 1;
        for (int y = cellY - radius; y <= cellY + radius; y++)
        {
            for (int x = cellX - radius; x <= cellX + radius; x++)
            {
                if (world.IsWalkable(x, y))
                    continue;

                float minX = world.Origin.x + x * world.CellSize;
                float maxX = world.Origin.x + (x + 1) * world.CellSize;
                float minZ = world.Origin.z + y * world.CellSize;
                float maxZ = world.Origin.z + (y + 1) * world.CellSize;
                float nearestX = Mathf.Clamp(point.x, minX, maxX);
                float nearestZ = Mathf.Clamp(point.z, minZ, maxZ);
                float dx = point.x - nearestX;
                float dz = point.z - nearestZ;
                float distanceSq = dx * dx + dz * dz;
                if (distanceSq > edgeClearance * edgeClearance)
                    continue;

                Vector2 away = new Vector2(dx, dz);
                if (away.sqrMagnitude <= 0.000001f)
                    away = ResolveNavigationRectangleBoundaryNormal(point, minX, maxX, minZ, maxZ);
                if (away.sqrMagnitude <= 0.000001f)
                    continue;

                away.Normalize();
                float distance = Mathf.Sqrt(distanceSq);
                float weight = Mathf.Max(0.001f, edgeClearance - distance);
                accumulated += away * weight;
                if (distanceSq < minDistanceSq)
                {
                    minDistanceSq = distanceSq;
                    nearestDirection = away;
                }
            }
        }

        if (float.IsPositiveInfinity(minDistanceSq) || minDistanceSq > edgeClearance * edgeClearance)
            return false;

        Vector2 resolved = accumulated.sqrMagnitude > 0.000001f ? accumulated : nearestDirection;
        if (resolved.sqrMagnitude <= 0.000001f)
            return false;

        resolved.Normalize();
        direction = new Vector3(resolved.x, 0f, resolved.y);
        return true;
    }

    private static Vector2 ResolveNavigationRectangleBoundaryNormal(Vector3 point, float minX, float maxX, float minZ, float maxZ)
    {
        const float epsilon = 0.0001f;
        float left = Mathf.Abs(point.x - minX);
        float right = Mathf.Abs(point.x - maxX);
        float bottom = Mathf.Abs(point.z - minZ);
        float top = Mathf.Abs(point.z - maxZ);
        float minDistance = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));
        Vector2 normal = Vector2.zero;
        if (left <= minDistance + epsilon)
            normal.x -= 1f;
        if (right <= minDistance + epsilon)
            normal.x += 1f;
        if (bottom <= minDistance + epsilon)
            normal.y -= 1f;
        if (top <= minDistance + epsilon)
            normal.y += 1f;

        return normal;
    }

    public static bool TryConstrainNavigationDisplacement(
        Vector3 position,
        Vector3 desiredDisplacement,
        int agentTypeId,
        out Vector3 constrainedDisplacement)
    {
        return TryConstrainNavigationDisplacement(position, desiredDisplacement, agentTypeId, 0f, out constrainedDisplacement);
    }

    public static bool TryResolveLegalNavigationPoint(
        Vector3 candidate,
        int agentTypeId,
        float maxSnapDistance,
        float edgeClearance,
        out Vector3 legalPoint)
    {
        legalPoint = Vector3.zero;
        if (!IsFiniteVector3(candidate)
            || float.IsNaN(maxSnapDistance)
            || float.IsInfinity(maxSnapDistance)
            || float.IsNaN(edgeClearance)
            || float.IsInfinity(edgeClearance))
        {
            throw new ArgumentOutOfRangeException(nameof(candidate), "Navigation query values must be finite.");
        }
        if (!TryGetCommittedNavigationQueryWorld(agentTypeId, allowSynchronousBuild: true, out NavigationWorld world))
            return false;

        bool includeRuntimeObstacleOverlay = HasPendingRuntimeDirty(_activeWorldState);
        if (!TryResolveLegalNavigationPointFixed(
                world,
                includeRuntimeObstacleOverlay,
                new FixVector2((Fix64)candidate.x, (Fix64)candidate.z),
                Fix64.Max(Fix64.Zero, (Fix64)maxSnapDistance),
                Fix64.Max(Fix64.Zero, (Fix64)edgeClearance),
                out FixVector2 legalPointFixed))
        {
            return false;
        }

        if (!world.WorldToGridFixed(legalPointFixed, out int legalX, out int legalY))
            throw new InvalidOperationException($"Resolved legal navigation point is outside the committed grid. raw=({legalPointFixed.x.RawValue},{legalPointFixed.y.RawValue}).");
        legalPoint = new Vector3(
            (float)legalPointFixed.x,
            world.GridToWorldCenter(legalX, legalY).y,
            (float)legalPointFixed.y);
        return true;
    }

    public static bool TryResolveLegalNavigationPointFixed(
        FixVector2 candidate,
        int agentTypeId,
        Fix64 maxSnapDistance,
        Fix64 edgeClearance,
        out FixVector2 legalPoint)
    {
        legalPoint = FixVector2.Zero;
        if (!TryGetCommittedNavigationQueryWorld(agentTypeId, allowSynchronousBuild: true, out NavigationWorld world))
            return false;

        return TryResolveLegalNavigationPointFixed(
            world,
            HasPendingRuntimeDirty(_activeWorldState),
            candidate,
            maxSnapDistance,
            edgeClearance,
            out legalPoint);
    }

    public static bool HasBaseWalkableLineFixed(FixVector2 from, FixVector2 to, int agentTypeId)
    {
        if (!TryGetCommittedNavigationQueryWorld(agentTypeId, allowSynchronousBuild: true, out NavigationWorld world))
            throw new InvalidOperationException($"Base walkable line query requires a committed navigation world. agentType={agentTypeId}");
        if (!world.WorldToGridFixed(from, out int x0, out int y0)
            || !world.WorldToGridFixed(to, out int x1, out int y1))
        {
            return false;
        }
        if (world.BaseWalkableMask == null || world.BaseWalkableMask.Length != world.Width * world.Height)
            throw new InvalidOperationException("Base walkable line query has an invalid base mask.");

        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int stepX = Math.Sign(x1 - x0);
        int stepY = Math.Sign(y1 - y0);
        int x = x0;
        int y = y0;
        int error = dx - dy;
        while (true)
        {
            if (!IsBaseWalkableCell(world, x, y))
                return false;
            if (x == x1 && y == y1)
                return true;

            int twiceError = error * 2;
            if (twiceError == 0 && stepX != 0 && stepY != 0)
            {
                if (!IsBaseWalkableCell(world, x + stepX, y)
                    || !IsBaseWalkableCell(world, x, y + stepY))
                {
                    return false;
                }
            }
            if (twiceError > -dy)
            {
                error -= dy;
                x += stepX;
            }
            if (twiceError < dx)
            {
                error += dx;
                y += stepY;
            }
        }
    }

    private static bool IsBaseWalkableCell(NavigationWorld world, int x, int y)
    {
        return x >= 0
               && x < world.Width
               && y >= 0
               && y < world.Height
               && world.BaseWalkableMask[world.GetIndex(x, y)];
    }

    public static bool TryResolveLegalNavigationPointFixedNonBlocking(
        FixVector2 candidate,
        int agentTypeId,
        Fix64 maxSnapDistance,
        Fix64 edgeClearance,
        out FixVector2 legalPoint)
    {
        legalPoint = FixVector2.Zero;
        if (!TryGetCommittedNavigationQueryWorldReadOnly(agentTypeId, out WorldRuntimeState state, out NavigationWorld world))
            return false;

        return TryResolveLegalNavigationPointFixed(
            world,
            HasPendingRuntimeDirty(state),
            candidate,
            maxSnapDistance,
            edgeClearance,
            out legalPoint);
    }

    private static bool TryResolveLegalNavigationPointFixed(
        NavigationWorld world,
        bool includeRuntimeObstacleOverlay,
        FixVector2 candidate,
        Fix64 maxSnapDistance,
        Fix64 edgeClearance,
        out FixVector2 legalPoint)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));

        legalPoint = FixVector2.Zero;
        maxSnapDistance = Fix64.Max(Fix64.Zero, maxSnapDistance);
        edgeClearance = ResolveNavigationQueryClearanceFixed(world, edgeClearance);
        bool candidateInGrid = world.WorldToGridFixed(candidate, out int cellX, out int cellY);
        if (candidateInGrid
            && world.IsWalkable(cellX, cellY)
            && IsNavigationCellClearFixed(world, cellX, cellY, edgeClearance, includeRuntimeObstacleOverlay))
        {
            legalPoint = candidate;
            return true;
        }

        int searchRadius = checked(NavigationGridFixedMath.DivideCeilingByCellSize(
            maxSnapDistance,
            world.CellSizeGridRaw) + 1);
        Fix64 maxDistanceSq = maxSnapDistance * maxSnapDistance;
        bool found = false;
        Fix64 bestDistanceSq = Fix64.FromRaw(long.MaxValue);
        int startX = Mathf.Clamp(cellX, 0, world.Width - 1);
        int startY = Mathf.Clamp(cellY, 0, world.Height - 1);
        int bestX = 0;
        int bestY = 0;

        for (int y = startY - searchRadius; y <= startY + searchRadius; y++)
        {
            for (int x = startX - searchRadius; x <= startX + searchRadius; x++)
            {
                if (!world.IsWalkable(x, y)
                    || !IsNavigationCellClearFixed(world, x, y, edgeClearance, includeRuntimeObstacleOverlay))
                {
                    continue;
                }

                FixVector2 center = world.GridToWorldCenterFixed(x, y);
                Fix64 distanceSq = FixVector2.SqrMagnitude(center - candidate);
                if (distanceSq > maxDistanceSq || distanceSq >= bestDistanceSq)
                    continue;

                bestDistanceSq = distanceSq;
                bestX = x;
                bestY = y;
                found = true;
            }
        }

        if (!found)
            return false;

        legalPoint = world.GridToWorldCenterFixed(bestX, bestY);
        return true;
    }

    public static bool TryEstimateNavigationDistance(
        Vector3 from,
        Vector3 to,
        int agentTypeId,
        out float distance)
    {
        return TryEstimateNavigationDistance(from, to, agentTypeId, out distance, out _);
    }

    public static bool TryEstimateNavigationDistance(
        Vector3 from,
        Vector3 to,
        int agentTypeId,
        out float distance,
        out string failureReason)
    {
        distance = 0f;
        failureReason = string.Empty;
        if (!TryGetNavigationQueryWorld(agentTypeId, allowSynchronousBuild: true, out NavigationWorld world))
        {
            failureReason = $"navigation world unavailable agentType={agentTypeId}";
            return false;
        }
        if (!world.WorldToGrid(from, out int startX, out int startY))
        {
            failureReason = $"start is outside authored grid position={from}";
            return false;
        }
        if (!world.WorldToGrid(to, out int goalX, out int goalY))
        {
            failureReason = $"goal is outside authored grid position={to}";
            return false;
        }
        if (!world.IsWalkable(startX, startY))
        {
            failureReason = $"start cell is blocked cell=({startX},{startY}) position={from}";
            return false;
        }
        if (!world.IsWalkable(goalX, goalY))
        {
            failureReason = $"goal cell is blocked cell=({goalX},{goalY}) position={to}";
            return false;
        }
        if (startX == goalX && startY == goalY)
            return true;

        if (TryFindGridPath(world, startX, startY, goalX, goalY, null, from, to, out distance))
            return true;

        failureReason = $"no traversable grid path start=({startX},{startY}) goal=({goalX},{goalY}) agentType={agentTypeId}";
        return false;
    }

    public static bool TryEstimateNavigationDistanceFixed(
        FixVector2 from,
        FixVector2 to,
        int agentTypeId,
        out Fix64 distance,
        out string failureReason)
    {
        distance = Fix64.Zero;
        failureReason = string.Empty;
        if (!TryGetNavigationQueryWorld(agentTypeId, allowSynchronousBuild: true, out NavigationWorld world))
        {
            failureReason = $"navigation world unavailable agentType={agentTypeId}";
            return false;
        }
        if (!world.WorldToGridFixed(from, out int startX, out int startY))
        {
            failureReason = $"start is outside authored grid raw=({from.x.RawValue},{from.y.RawValue})";
            return false;
        }
        if (!world.WorldToGridFixed(to, out int goalX, out int goalY))
        {
            failureReason = $"goal is outside authored grid raw=({to.x.RawValue},{to.y.RawValue})";
            return false;
        }
        if (!world.IsWalkable(startX, startY))
        {
            failureReason = $"start cell is blocked cell=({startX},{startY}) raw=({from.x.RawValue},{from.y.RawValue})";
            return false;
        }
        if (!world.IsWalkable(goalX, goalY))
        {
            failureReason = $"goal cell is blocked cell=({goalX},{goalY}) raw=({to.x.RawValue},{to.y.RawValue})";
            return false;
        }
        if (startX == goalX && startY == goalY)
            return true;

        if (TryFindGridPathFixed(world, startX, startY, goalX, goalY, out distance))
            return true;

        failureReason = $"no traversable grid path start=({startX},{startY}) goal=({goalX},{goalY}) agentType={agentTypeId}";
        return false;
    }

    public static bool TryEstimateNavigationDistanceToReachableGoalFixed(
        FixVector2 from,
        FixVector2 rawGoal,
        int agentTypeId,
        out Fix64 distance,
        out FixVector2 reachableGoal,
        out string failureReason)
    {
        distance = Fix64.Zero;
        reachableGoal = rawGoal;
        failureReason = string.Empty;
        if (!TryGetNavigationQueryWorld(agentTypeId, allowSynchronousBuild: true, out NavigationWorld world))
        {
            failureReason = $"navigation world unavailable agentType={agentTypeId}";
            return false;
        }
        if (!TryResolveReachableNavigationQueryCellsFixed(
                world,
                from,
                rawGoal,
                out int startX,
                out int startY,
                out int goalX,
                out int goalY,
                out reachableGoal,
                out failureReason))
        {
            return false;
        }
        if (startX == goalX && startY == goalY)
            return true;
        if (TryFindGridPathFixed(world, startX, startY, goalX, goalY, out distance))
            return true;

        failureReason =
            $"no traversable grid path to reachable goal start=({startX},{startY}) goal=({goalX},{goalY}) " +
            $"rawGoal=({rawGoal.x.RawValue},{rawGoal.y.RawValue}) agentType={agentTypeId}";
        return false;
    }

    public static bool TryEstimatePrewarmedNavigationDistanceToReachableGoalFixed(
        FixVector2 from,
        FixVector2 rawGoal,
        int agentTypeId,
        out Fix64 distance,
        out FixVector2 reachableGoal,
        out string failureReason)
    {
        distance = Fix64.Zero;
        reachableGoal = rawGoal;
        failureReason = string.Empty;
        if (!TryGetCommittedNavigationQueryWorld(agentTypeId, allowSynchronousBuild: false, out NavigationWorld world))
        {
            failureReason = $"committed navigation world unavailable agentType={agentTypeId}";
            return false;
        }
        if (!TryResolveReachableNavigationQueryCellsFixed(
                world,
                from,
                rawGoal,
                out int startX,
                out int startY,
                out int goalX,
                out int goalY,
                out reachableGoal,
                out failureReason))
        {
            return false;
        }
        if (startX == goalX && startY == goalY)
            return true;
        if (TryEstimateFlowPathDistanceFixed(world, startX, startY, goalX, goalY, out distance))
            return true;

        failureReason =
            $"prewarmed flow path unavailable start=({startX},{startY}) goal=({goalX},{goalY}) " +
            $"rawGoal=({rawGoal.x.RawValue},{rawGoal.y.RawValue}) agentType={agentTypeId}";
        return false;
    }

    public static void RequestNavigationDistancePrewarmFixed(
        FixVector2 from,
        FixVector2 rawGoal,
        int agentTypeId)
    {
        int resolvedAgentTypeId = ResolvePreferredAgentTypeId(agentTypeId);
        for (int i = 0; i < NavigationDistancePrewarmRequests.Count; i++)
        {
            NavigationDistancePrewarmRequest existing = NavigationDistancePrewarmRequests[i];
            if (existing.AgentTypeId == resolvedAgentTypeId
                && existing.From == from
                && existing.RawGoal == rawGoal)
            {
                return;
            }
        }

        NavigationDistancePrewarmRequests.Add(new NavigationDistancePrewarmRequest
        {
            AgentTypeId = resolvedAgentTypeId,
            From = from,
            RawGoal = rawGoal
        });
        NavigationDistancePrewarmRequests.Sort(CompareNavigationDistancePrewarmRequests);
        s_NavigationDistancePrewarmCompleted = false;
        s_NavigationDistancePrewarmCompletionMayHaveChanged = false;
    }

    public static void ClearNavigationDistancePrewarmRequests()
    {
        NavigationDistancePrewarmRequests.Clear();
        s_NavigationDistancePrewarmCompleted = false;
        s_NavigationDistancePrewarmCompletionMayHaveChanged = false;
    }

    private static bool IsNavigationDistancePrewarmSharedGoalKey(SharedGoalFieldKey key)
    {
        for (int i = 0; i < NavigationDistancePrewarmRequests.Count; i++)
        {
            NavigationDistancePrewarmRequest request = NavigationDistancePrewarmRequests[i];
            if (request.AgentTypeId == key.AgentTypeId
                && request.ResolvedWorldVersion == key.WorldVersion
                && request.GoalSectorId == key.GoalSectorId
                && WorldStates.TryGetValue(request.AgentTypeId, out WorldRuntimeState state)
                && state?.World != null
                && state.World.GetIndex(request.GoalX, request.GoalY) == key.GoalCellIndex)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetNavigationPathCorners(
        Vector3 from,
        Vector3 to,
        int agentTypeId,
        List<Vector3> pathCorners,
        out string failureReason)
    {
        return TryGetNavigationPathCorners(
            from,
            to,
            agentTypeId,
            true,
            true,
            false,
            pathCorners,
            out failureReason,
            out _);
    }

    public static bool TryGetNavigationPathCornersNonBlocking(
        Vector3 from,
        Vector3 to,
        int agentTypeId,
        List<Vector3> pathCorners,
        out string failureReason)
    {
        return TryGetNavigationPathCornersNonBlocking(
            from,
            to,
            agentTypeId,
            pathCorners,
            out failureReason,
            out _);
    }

    public static bool TryGetNavigationPathCornersNonBlocking(
        Vector3 from,
        Vector3 to,
        int agentTypeId,
        List<Vector3> pathCorners,
        out string failureReason,
        out bool navigationUpdatePending)
    {
        return TryGetNavigationPathCorners(
            from,
            to,
            agentTypeId,
            false,
            false,
            false,
            pathCorners,
            out failureReason,
            out navigationUpdatePending);
    }

    public static bool TryGetNavigationPathCornersToReachableGoalNonBlocking(
        Vector3 from,
        Vector3 rawGoal,
        int agentTypeId,
        List<Vector3> pathCorners,
        out string failureReason,
        out bool navigationUpdatePending)
    {
        return TryGetNavigationPathCorners(
            from,
            rawGoal,
            agentTypeId,
            false,
            false,
            true,
            pathCorners,
            out failureReason,
            out navigationUpdatePending);
    }

#if UNITY_EDITOR
    public static bool TryGetEditorNavigationPathCorners(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset staticCollisionSourceGrid,
        Vector3 from,
        Vector3 rawGoal,
        List<Vector3> pathCorners,
        out string failureReason)
    {
        if (grid == null)
            throw new ArgumentNullException(nameof(grid));
        if (pathCorners == null)
            throw new ArgumentNullException(nameof(pathCorners));

        NavigationWorld world = GetOrCreateEditorNavigationPreviewWorld(grid, staticCollisionSourceGrid);
        return TryGetNavigationPathCorners(
            world,
            from,
            rawGoal,
            grid.AgentTypeId,
            resolveReachableGoal: true,
            pathCorners,
            out failureReason);
    }
#endif

    private static bool TryGetNavigationPathCorners(
        Vector3 from,
        Vector3 to,
        int agentTypeId,
        bool allowSynchronousBuild,
        bool useAuthorityPathCaches,
        bool resolveReachableGoal,
        List<Vector3> pathCorners,
        out string failureReason,
        out bool navigationUpdatePending)
    {
        if (pathCorners == null)
            throw new ArgumentNullException(nameof(pathCorners));

        pathCorners.Clear();
        failureReason = string.Empty;
        navigationUpdatePending = false;
        WorldRuntimeState queryWorldState;
        NavigationWorld world;
        bool hasWorld;
        if (useAuthorityPathCaches)
        {
            hasWorld = TryGetCommittedNavigationQueryWorld(agentTypeId, allowSynchronousBuild, out world);
            queryWorldState = _activeWorldState;
        }
        else
        {
            hasWorld = TryGetCommittedNavigationQueryWorldReadOnly(agentTypeId, out queryWorldState, out world);
        }
        if (!hasWorld)
        {
            failureReason = $"navigation world unavailable agentType={agentTypeId}";
            return false;
        }
        if (HasPendingRuntimeDirty(queryWorldState))
        {
            if (!allowSynchronousBuild)
            {
                navigationUpdatePending = true;
                failureReason = $"navigation update pending agentType={agentTypeId}";
                return false;
            }

            world = ResolveReachabilityQueryWorld(queryWorldState);
        }

        return TryGetNavigationPathCorners(
            world,
            from,
            to,
            agentTypeId,
            resolveReachableGoal,
            pathCorners,
            out failureReason);
    }

    private static bool TryGetNavigationPathCorners(
        NavigationWorld world,
        Vector3 from,
        Vector3 to,
        int agentTypeId,
        bool resolveReachableGoal,
        List<Vector3> pathCorners,
        out string failureReason)
    {
        pathCorners.Clear();
        failureReason = string.Empty;
        int startX;
        int startY;
        int goalX;
        int goalY;
        Vector3 resolvedGoal = to;
        if (resolveReachableGoal)
        {
            if (!TryResolveReachableNavigationQueryCellsFixed(
                    world,
                    new FixVector2((Fix64)from.x, (Fix64)from.z),
                    new FixVector2((Fix64)to.x, (Fix64)to.z),
                    out startX,
                    out startY,
                    out goalX,
                    out goalY,
                    out FixVector2 reachableGoal,
                    out failureReason))
            {
                return false;
            }

            resolvedGoal = new Vector3(
                (float)reachableGoal.x,
                world.GridToWorldCenter(goalX, goalY).y,
                (float)reachableGoal.y);
        }
        else
        {
            if (!world.WorldToGrid(from, out startX, out startY))
            {
                failureReason = $"start is outside authored grid position={from}";
                return false;
            }
            if (!world.WorldToGrid(to, out goalX, out goalY))
            {
                failureReason = $"goal is outside authored grid position={to}";
                return false;
            }
        }
        if (!world.IsWalkable(startX, startY))
        {
            failureReason = $"start cell is blocked cell=({startX},{startY}) position={from}";
            return false;
        }
        if (!world.IsWalkable(goalX, goalY))
        {
            failureReason = $"goal cell is blocked cell=({goalX},{goalY}) position={to}";
            return false;
        }
        if (startX == goalX && startY == goalY)
        {
            pathCorners.Add(from);
            pathCorners.Add(resolvedGoal);
            return true;
        }

        if (TryFindGridPath(world, startX, startY, goalX, goalY, pathCorners, from, resolvedGoal, out _))
            return true;

        failureReason = $"no traversable grid path start=({startX},{startY}) goal=({goalX},{goalY}) agentType={agentTypeId}";
        return false;
    }

    public static bool IsPositionOccupiedByOtherAgent(int selfId, Vector3 position, float requiredDistance, out int blockingAgentId, out float blockingDistance)
    {
        return IsPositionOccupiedByOtherAgent(selfId, 0, position, requiredDistance, out blockingAgentId, out blockingDistance);
    }

    public static bool IsPositionOccupiedByOtherAgent(int selfId, int ignoredAgentId, Vector3 position, float requiredDistance, out int blockingAgentId, out float blockingDistance)
    {
        if (!IsFiniteVector3(position))
            throw new ArgumentOutOfRangeException(nameof(position));
        if (float.IsNaN(requiredDistance)
            || float.IsInfinity(requiredDistance)
            || requiredDistance < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredDistance));
        }

        bool occupied = IsPositionOccupiedByOtherAgentFixed(
            selfId,
            ignoredAgentId,
            new FixVector2((Fix64)position.x, (Fix64)position.z),
            (Fix64)requiredDistance,
            out blockingAgentId,
            out Fix64 blockingDistanceFixed);
        blockingDistance = occupied ? (float)blockingDistanceFixed : float.PositiveInfinity;
        return occupied;
    }

    public static bool IsPositionOccupiedByOtherAgentFixed(
        int selfId,
        int ignoredAgentId,
        FixVector2 position,
        Fix64 requiredDistance,
        out int blockingAgentId,
        out Fix64 blockingDistance)
    {
        if (requiredDistance < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(requiredDistance));

        blockingAgentId = 0;
        blockingDistance = Fix64.FromRaw(long.MaxValue);
        Fix64 requiredDistanceSq = requiredDistance * requiredDistance;
        Fix64 bestDistanceSq = Fix64.FromRaw(long.MaxValue);
        for (int i = 0; i < OrderedAgentIds.Count; i++)
        {
            AgentRuntimeData agent = Agents[OrderedAgentIds[i]];
            if (agent.Id == selfId || agent.Id == ignoredAgentId || agent.IgnoreAgentCollision)
                continue;

            Fix64 distanceSq = FixVector2.SqrMagnitude(agent.PositionFixed - position);
            if (distanceSq >= requiredDistanceSq || distanceSq >= bestDistanceSq)
                continue;

            bestDistanceSq = distanceSq;
            blockingAgentId = agent.Id;
        }

        if (blockingAgentId == 0)
            return false;
        blockingDistance = Fix64.Sqrt(bestDistanceSq);
        return true;
    }

    public static bool TryReserveNavigationGoalIfAvailable(
        int selfId,
        int ignoredAgentId,
        Vector3 position,
        float requiredDistance,
        out int blockingAgentId)
    {
        return TryReserveNavigationGoalIfAvailableFixed(
            selfId,
            ignoredAgentId,
            new FixVector2((Fix64)position.x, (Fix64)position.z),
            (Fix64)requiredDistance,
            out blockingAgentId);
    }

    public static bool TryReserveNavigationGoalIfAvailableFixed(
        int selfId,
        int ignoredAgentId,
        FixVector2 position,
        Fix64 requiredDistance,
        out int blockingAgentId)
    {
        if (selfId == 0)
            throw new InvalidOperationException("TryReserveNavigationGoalIfAvailableFixed failed: selfId is zero.");
        if (requiredDistance < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(requiredDistance), "Navigation goal reservation distance cannot be negative.");

        if (IsNavigationGoalOccupiedByOtherFixed(
                selfId,
                ignoredAgentId,
                position,
                requiredDistance,
                includeReservations: true,
                out blockingAgentId))
        {
            return false;
        }

        RegisterNavigationGoalReservationFixed(selfId, position, requiredDistance);
        return true;
    }

    public static bool TryReserveReachableNavigationGoalFixed(
        IEntityContext self,
        int ignoredAgentId,
        FixVector2 position,
        Fix64 navigationClearance,
        Fix64 reservationDistance,
        out int blockingAgentId,
        out string failureReason,
        out NavigationQueryFailureKind failureKind)
    {
        blockingAgentId = 0;
        failureReason = string.Empty;
        failureKind = NavigationQueryFailureKind.None;
        if (self == null)
            throw new InvalidOperationException("TryReserveReachableNavigationGoalFixed failed: self is null.");
        if (navigationClearance < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(navigationClearance), "Navigation clearance cannot be negative.");
        if (reservationDistance < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(reservationDistance), "Navigation goal reservation distance cannot be negative.");

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
            agent.AgentTypeId = self is ILogicFrameEntity logicEntity ? logicEntity.NavigationAgentTypeId : agent.AgentTypeId;
        }

        if (!TryEnsureWorldBuilt(agent.AgentTypeId))
        {
            failureKind = NavigationQueryFailureKind.Unavailable;
            failureReason = $"world unavailable agentType={agent.AgentTypeId}";
            return false;
        }

        if (HasPendingRuntimeDirty(_activeWorldState))
        {
            failureKind = NavigationQueryFailureKind.PendingRuntimeUpdate;
            failureReason = $"runtime dirty pending agentType={agent.AgentTypeId}";
            return false;
        }

        if (!TryResolveStartCellForReachabilityFixed(self, out _, out _, out int startIsland))
        {
            failureKind = NavigationQueryFailureKind.Unreachable;
            failureReason = $"start cell unavailable self={self.CharacterKey} position={self.LogicFramePositionFixed()}";
            return false;
        }

        if (!_world.WorldToGridFixed(position, out int goalX, out int goalY)
            || !_world.IsWalkable(goalX, goalY))
        {
            failureKind = NavigationQueryFailureKind.Unreachable;
            failureReason = $"goal cell unavailable position={position}";
            return false;
        }

        int goalIsland = ResolveIslandIdForDiagnostics(_world, goalX, goalY);
        if (goalIsland != startIsland)
        {
            failureKind = NavigationQueryFailureKind.Unreachable;
            failureReason = $"goal island differs from start position={position} goalIsland={goalIsland} startIsland={startIsland}";
            return false;
        }

        Fix64 resolvedClearance = ResolveNavigationQueryClearanceFixed(_world, navigationClearance);
        if (!IsNavigationPointClearFixed(_world, position, resolvedClearance, includeRuntimeObstacleOverlay: true))
        {
            failureKind = NavigationQueryFailureKind.Unreachable;
            failureReason = $"goal clearance blocked position={position} clearance={resolvedClearance}";
            return false;
        }

        if (IsNavigationGoalOccupiedByOtherFixed(
                selfId,
                ignoredAgentId,
                position,
                reservationDistance,
                includeReservations: true,
                out blockingAgentId))
        {
            failureReason = $"goal occupied position={position} blocker={blockingAgentId}";
            return false;
        }

        RegisterNavigationGoalReservationFixed(selfId, position, reservationDistance);
        return true;
    }

    public static bool TryResolveReachableAttackAreaPointFixed(
        IEntityContext self,
        IEntityContext target,
        Fix64 attackRange,
        out FixVector2 reachablePoint,
        out string failureReason,
        out NavigationQueryFailureKind failureKind)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        LogicCombatShape targetShape = LogicFrameRuntime.IsTicking
            ? LogicEntityFrameSnapshotService.GetRequiredCurrent(target).CombatShape
            : target.CombatShape;
        return TryResolveReachableAttackAreaPointFixed(
            self,
            targetShape,
            attackRange,
            true,
            out reachablePoint,
            out failureReason,
            out failureKind);
    }

    public static bool TryEvaluateTargetingAttackAreaReachabilityFixed(
        IEntityContext self,
        IEntityContext target,
        Fix64 attackRange,
        out string failureReason,
        out NavigationQueryFailureKind failureKind)
    {
        if (self == null)
            throw new ArgumentNullException(nameof(self));
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (attackRange < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(attackRange));

        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long preparationStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        int sourceId = ResolveAgentId(self);
        if (!Agents.TryGetValue(sourceId, out AgentRuntimeData agent))
        {
            RegisterSyntheticAgent(self);
            agent = Agents[sourceId];
        }
        if (!TryEnsureWorldBuilt(agent.AgentTypeId)
            || !TryResolveStartCellForReachabilityFixed(self, out _, out _, out int startIslandId))
        {
            return TryResolveReachableAttackAreaPointFixed(
                self,
                target,
                attackRange,
                out _,
                out failureReason,
                out failureKind);
        }

        LogicCombatShape targetShape = LogicFrameRuntime.IsTicking
            ? LogicEntityFrameSnapshotService.GetRequiredCurrent(target).CombatShape
            : target.CombatShape;
        PrepareTargetingReachabilityCacheForFrame();
        var key = new TargetingReachabilityCacheKey(
            _world.Version,
                _navigationTopologyVersion,
            ResolvePreferredAgentTypeId(agent.AgentTypeId),
            startIslandId,
            targetShape,
            attackRange);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.CharacterTargetingReachabilityPreparation,
                Stopwatch.GetTimestamp() - preparationStartTicks);
        }

        long lookupStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (TargetingReachabilityCache.TryGetValue(key, out TargetingReachabilityCacheEntry cached))
        {
            if (profile)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.CharacterTargetingReachabilityCacheLookup,
                    Stopwatch.GetTimestamp() - lookupStartTicks);
            failureKind = cached.FailureKind;
            failureReason = cached.Reachable
                ? string.Empty
                : $"shared targeting reachability rejected world={key.WorldVersion} agentType={key.AgentTypeId} island={key.StartIslandId}";
            return cached.Reachable;
        }
        if (profile)
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.CharacterTargetingReachabilityCacheLookup,
                Stopwatch.GetTimestamp() - lookupStartTicks);

        bool reachable = TryResolveReachableAttackAreaPointFixed(
            self,
            target,
            attackRange,
            out _,
            out failureReason,
            out failureKind);
        long storeStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        TargetingReachabilityCache.Add(
            key,
            new TargetingReachabilityCacheEntry(reachable, failureKind));
        _targetingReachabilityCacheMissCount++;
        if (profile)
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.CharacterTargetingReachabilityCacheStore,
                Stopwatch.GetTimestamp() - storeStartTicks);
        return reachable;
    }

    public static bool TryResolveReachablePointAreaFixed(
        IEntityContext self,
        FixVector2 point,
        Fix64 range,
        out FixVector2 reachablePoint,
        out string failureReason,
        out NavigationQueryFailureKind failureKind)
    {
        return TryResolveReachableAttackAreaPointFixed(
            self,
            LogicCombatShape.Circle(point, Fix64.Zero),
            range,
            false,
            out reachablePoint,
            out failureReason,
            out failureKind);
    }

    private static bool TryResolveReachableAttackAreaPointFixed(
        IEntityContext self,
        LogicCombatShape targetShape,
        Fix64 attackRange,
        bool keepAgentClearOfTargetSurface,
        out FixVector2 reachablePoint,
        out string failureReason,
        out NavigationQueryFailureKind failureKind)
    {
        long setupStartTicks = Stopwatch.GetTimestamp();
        reachablePoint = targetShape.Center;
        failureReason = string.Empty;
        failureKind = NavigationQueryFailureKind.None;
        if (self == null)
            throw new ArgumentNullException(nameof(self));
        if (attackRange < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(attackRange));

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
            agent.AgentTypeId = self is ILogicFrameEntity logicEntity ? logicEntity.NavigationAgentTypeId : agent.AgentTypeId;
        }

        if (!TryEnsureWorldBuilt(agent.AgentTypeId))
        {
            failureKind = NavigationQueryFailureKind.Unavailable;
            failureReason = $"world unavailable agentType={agent.AgentTypeId}";
            return false;
        }
        if (HasPendingRuntimeDirty(_activeWorldState))
        {
            failureKind = NavigationQueryFailureKind.PendingRuntimeUpdate;
            failureReason = $"runtime dirty pending agentType={agent.AgentTypeId}";
            return false;
        }
        if (!TryResolveStartCellForReachabilityFixed(self, out int startX, out int startY, out int startIsland))
        {
            failureKind = NavigationQueryFailureKind.Unreachable;
            failureReason = $"start reachability failed {BuildReachabilityStartDiagnostics(self)}";
            return false;
        }
        if (!_world.WorldToGridFixed(targetShape.Center, out int targetX, out int targetY))
        {
            failureKind = NavigationQueryFailureKind.Unreachable;
            failureReason = $"attack area center outside grid center={targetShape.Center}";
            return false;
        }
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
        Fix64 minimumSurfaceDistance = keepAgentClearOfTargetSurface
            ? agent.RadiusFixed + Fix64.FromRaw(205)
            : Fix64.Zero;
        FixVector2 selfPosition = self.LogicFramePositionFixed();
        Fix64 bestSelfDistanceSquared = Fix64.FromRaw(long.MaxValue);
        int bestCellIndex = int.MaxValue;
        bool found = false;
        PrepareAttackAreaCandidateCacheForFrame();
        var cacheKey = new AttackAreaCandidateCacheKey(
            _world.Version,
            _navigationTopologyVersion,
            ResolvePreferredAgentTypeId(agent.AgentTypeId),
            targetShape,
            targetX,
            targetY,
            attackRange);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.FlowAttackAreaSetup,
            Stopwatch.GetTimestamp() - setupStartTicks);

        if (!AttackAreaCandidateCache.TryGetValue(cacheKey, out AttackAreaCandidateCacheEntry cacheEntry))
        {
            cacheEntry = BuildAttackAreaCandidateCacheEntry(
                targetShape,
                attackRange,
                targetX,
                targetY,
                radiusX + 2,
                radiusY + 2);
            AttackAreaCandidateCache.Add(cacheKey, cacheEntry);
            _attackAreaCandidateCacheBuildCount++;
        }

        long selectionStartTicks = Stopwatch.GetTimestamp();
        try
        {
            if (cacheEntry.ByIsland.TryGetValue(startIsland, out AttackAreaIslandCandidates islandCandidates))
            {
                for (int i = 0; i < islandCandidates.Candidates.Count; i++)
                {
                AttackAreaCandidate candidate = islandCandidates.Candidates[i];
                if (!IsAttackAreaCandidateWithinSurfaceRange(
                        targetShape,
                        candidate.Point,
                        attackRange,
                        minimumSurfaceDistance))
                    continue;
                Fix64 selfDistanceSquared = FixVector2.SqrMagnitude(candidate.Point - selfPosition);
                if (found
                    && (selfDistanceSquared > bestSelfDistanceSquared
                        || (selfDistanceSquared == bestSelfDistanceSquared && candidate.CellIndex >= bestCellIndex)))
                {
                    continue;
                }

                found = true;
                reachablePoint = candidate.Point;
                bestSelfDistanceSquared = selfDistanceSquared;
                bestCellIndex = candidate.CellIndex;
                }
            }
        }
        finally
        {
            if (MainThreadFrameProfiler.LoggingEnabled)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.CharacterTargetingReachabilityCandidateSelection,
                    Stopwatch.GetTimestamp() - selectionStartTicks);
        }

        if (found)
            return true;

        failureKind = NavigationQueryFailureKind.Unreachable;
        failureReason =
            $"no reachable attack-area point self=({startX},{startY}) island={startIsland} " +
            $"target=({targetX},{targetY}) range={attackRange} minimumSurface={minimumSurfaceDistance}";
        return false;
    }

    private static bool IsAttackAreaCandidateWithinSurfaceRange(
        LogicCombatShape targetShape,
        FixVector2 candidate,
        Fix64 maximumSurfaceDistance,
        Fix64 minimumSurfaceDistance)
    {
        if (targetShape.Kind == LogicCombatShapeKind.Circle)
        {
            FixVector2 offset = candidate - targetShape.Center;
            Fix64 distanceSquared = FixVector2.SqrMagnitude(offset);
            Fix64 radius = targetShape.Radius;
            if (distanceSquared <= radius * radius)
                return minimumSurfaceDistance <= Fix64.Zero;

            Fix64 minimumCenterDistance = radius + minimumSurfaceDistance;
            Fix64 maximumCenterDistance = radius + maximumSurfaceDistance;
            return distanceSquared >= minimumCenterDistance * minimumCenterDistance
                   && distanceSquared <= maximumCenterDistance * maximumCenterDistance;
        }

        if (targetShape.Kind == LogicCombatShapeKind.AxisAlignedBox)
        {
            Fix64 dx = Fix64.Abs(candidate.x - targetShape.Center.x) - targetShape.HalfExtents.x;
            Fix64 dz = Fix64.Abs(candidate.y - targetShape.Center.y) - targetShape.HalfExtents.y;
            dx = dx > Fix64.Zero ? dx : Fix64.Zero;
            dz = dz > Fix64.Zero ? dz : Fix64.Zero;
            Fix64 surfaceDistanceSquared = dx * dx + dz * dz;
            return surfaceDistanceSquared >= minimumSurfaceDistance * minimumSurfaceDistance
                   && surfaceDistanceSquared <= maximumSurfaceDistance * maximumSurfaceDistance;
        }

        throw new InvalidOperationException(
            $"Attack-area candidate has unsupported target shape kind={(int)targetShape.Kind}.");
    }

    private static AttackAreaCandidateCacheEntry BuildAttackAreaCandidateCacheEntry(
        LogicCombatShape targetShape,
        Fix64 attackRange,
        int targetX,
        int targetY,
        int radiusX,
        int radiusY)
    {
        var entry = new AttackAreaCandidateCacheEntry();
        if (_world == null)
            throw new InvalidOperationException("BuildAttackAreaCandidateCacheEntry requires an active world.");
        int expectedCellCount = checked(_world.Width * _world.Height);
        if (_world.IslandIds == null || _world.IslandIds.Length != expectedCellCount)
            throw new InvalidOperationException(
                $"BuildAttackAreaCandidateCacheEntry requires world island ids. world={_world.Version} " +
                $"agentType={_world.AgentTypeId} expected={expectedCellCount} actual={_world.IslandIds?.Length ?? -1}.");
        long scanStartTicks = Stopwatch.GetTimestamp();
        try
        {
            for (int y = Math.Max(0, targetY - radiusY); y <= Math.Min(_world.Height - 1, targetY + radiusY); y++)
            {
                for (int x = Math.Max(0, targetX - radiusX); x <= Math.Min(_world.Width - 1, targetX + radiusX); x++)
                {
                    if (!_world.IsWalkable(x, y))
                        continue;

                    FixVector2 candidatePoint = _world.GridToWorldCenterFixed(x, y);

                    int islandId = _world.IslandIds[_world.GetIndex(x, y)];
                    if (!entry.ByIsland.TryGetValue(islandId, out AttackAreaIslandCandidates islandCandidates))
                    {
                        islandCandidates = new AttackAreaIslandCandidates();
                        entry.ByIsland.Add(islandId, islandCandidates);
                    }

                    islandCandidates.Candidates.Add(
                        new AttackAreaCandidate(candidatePoint, _world.GetIndex(x, y)));
                }
            }
        }
        finally
        {
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowAttackAreaClearance, 0L);
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowAttackAreaScan,
                Stopwatch.GetTimestamp() - scanStartTicks);
        }

        return entry;
    }

    private static void PrepareAttackAreaCandidateCacheForFrame()
    {
        int frame = GetFrameCount();
        if (_attackAreaCandidateCacheFrame == frame)
            return;

        _attackAreaCandidateCacheFrame = frame;
        _attackAreaCandidateCacheBuildCount = 0;
        if (_world == null)
            throw new InvalidOperationException("Attack-area candidate cache requires an active navigation world.");
        if (_attackAreaCandidateCacheWorldVersion != _world.Version
            || _attackAreaCandidateCacheTopologyVersion != _navigationTopologyVersion)
        {
            AttackAreaCandidateCache.Clear();
            _attackAreaCandidateCacheWorldVersion = _world.Version;
            _attackAreaCandidateCacheTopologyVersion = _navigationTopologyVersion;
        }
    }

    private static void PrepareTargetingReachabilityCacheForFrame()
    {
        int frame = GetFrameCount();
        if (_targetingReachabilityCacheFrame == frame)
            return;

        TargetingReachabilityCache.Clear();
        _targetingReachabilityCacheFrame = frame;
        _targetingReachabilityCacheMissCount = 0;
    }

    public static bool TryResolveNearestReachableGoal(
        IEntityContext self,
        Vector3 desiredGoal,
        float searchRadius,
        out Vector3 reachableGoal,
        out string failureReason)
    {
        reachableGoal = desiredGoal;
        failureReason = string.Empty;
        if (self == null)
            throw new InvalidOperationException("TryResolveNearestReachableGoal failed: self is null.");

        Vector3 frameStartPosition = self.LogicFramePosition();

        int selfId = ResolveAgentId(self);
        if (!Agents.TryGetValue(selfId, out AgentRuntimeData agent))
        {
            RegisterSyntheticAgent(self);
            agent = Agents[selfId];
        }
        else
        {
            agent.Position = frameStartPosition;
            agent.Radius = ResolveCollisionRadius(self);
        }

        if (!TryEnsureWorldBuilt(agent.AgentTypeId))
        {
            failureReason = $"world unavailable agentType={agent.AgentTypeId}";
            return false;
        }

        if (HasPendingRuntimeDirty(_activeWorldState))
        {
            failureReason = $"runtime dirty pending agentType={agent.AgentTypeId}";
            return false;
        }

        NavigationWorld queryWorld = _activeWorldState.World;
        if (!queryWorld.WorldToGrid(frameStartPosition, out int startX, out int startY))
        {
            failureReason = $"start not on grid pos={frameStartPosition}";
            return false;
        }

        if (!queryWorld.IsWalkable(startX, startY)
            && !TryResolveNearbyStartWalkable(queryWorld, frameStartPosition, startX, startY, out startX, out startY))
        {
            failureReason = $"start blocked and no nearby walkable pos={frameStartPosition}";
            return false;
        }

        int startIsland = ResolveIslandIdForDiagnostics(queryWorld, startX, startY);
        if (startIsland <= 0)
        {
            failureReason = $"start island invalid start=({startX},{startY}) island={startIsland}";
            return false;
        }

        if (!queryWorld.WorldToGrid(desiredGoal, out int goalX, out int goalY))
        {
            failureReason = $"goal not on grid goal={desiredGoal}";
            return false;
        }

        int maxRadiusCells = Mathf.Max(1, Mathf.CeilToInt(searchRadius / Mathf.Max(queryWorld.CellSize, 0.001f)));
        if (TryFindNearestWalkableInIslandByWorldDistance(
                queryWorld,
                goalX,
                goalY,
                desiredGoal,
                startIsland,
                maxRadiusCells,
                allowFullIslandSearch: false,
                out int resultX,
                out int resultY,
                out float distance))
        {
            reachableGoal = queryWorld.GridToWorldCenter(resultX, resultY);
            if (IsMovementDiagnosticsEnabled())
            {
                LogReachableGoalResolution(queryWorld, self, desiredGoal, startX, startY, goalX, goalY, resultX, resultY, distance, maxRadiusCells, fullIsland: false);
                LogReachableGoalTargetSnapDiagnostics(queryWorld, self, desiredGoal, startX, startY, goalX, goalY, resultX, resultY, distance, fullIsland: false);
            }
            return true;
        }

        bool fullIslandFound = TryFindNearestWalkableInIslandByWorldDistance(
            queryWorld,
            goalX,
            goalY,
            desiredGoal,
            startIsland,
            maxRadiusCells,
            allowFullIslandSearch: true,
            out int fullIslandX,
            out int fullIslandY,
            out float fullIslandDistance);
        if (IsMovementDiagnosticsEnabled())
        {
            Debug.LogWarning(BuildGoalResolutionIslandDiagnostics(
                queryWorld,
                self,
                desiredGoal,
                startX,
                startY,
                goalX,
                goalY,
                startIsland,
                maxRadiusCells,
                fullIslandFound,
                fullIslandX,
                fullIslandY,
                fullIslandDistance));
        }
        if (fullIslandFound)
        {
            reachableGoal = queryWorld.GridToWorldCenter(fullIslandX, fullIslandY);
            LogReachableGoalResolution(queryWorld, self, desiredGoal, startX, startY, goalX, goalY, fullIslandX, fullIslandY, fullIslandDistance, maxRadiusCells, fullIsland: true);
            LogReachableGoalTargetSnapDiagnostics(queryWorld, self, desiredGoal, startX, startY, goalX, goalY, fullIslandX, fullIslandY, fullIslandDistance, fullIsland: true);
            return true;
        }

        failureReason =
            $"no reachable goal in start island desired={desiredGoal} desiredCell=({goalX},{goalY}) start=({startX},{startY}) startIsland={startIsland} radiusCells={maxRadiusCells}";
        return false;
    }

    public static bool TryResolveCombatApproachPoint(
        IEntityContext self,
        IEntityContext target,
        Vector3 targetPoint,
        float standOff,
        float ringSpacing,
        int ringCount,
        int candidateCount,
        float requiredClearance,
        out Vector3 approachPoint,
        out string failureReason)
    {
        return TryResolveCombatApproachPoint(
            self,
            target,
            targetPoint,
            standOff,
            Mathf.Max(0.05f, requiredClearance),
            ringSpacing,
            ringCount,
            candidateCount,
            requiredClearance,
            out approachPoint,
            out failureReason,
            out _);
    }

    public static bool TryResolveCombatApproachPoint(
        IEntityContext self,
        IEntityContext target,
        Vector3 targetPoint,
        float standOff,
        float ringSpacing,
        int ringCount,
        int candidateCount,
        float requiredClearance,
        out Vector3 approachPoint,
        out string failureReason,
        out NavigationQueryFailureKind failureKind)
    {
        return TryResolveCombatApproachPoint(
            self,
            target,
            targetPoint,
            standOff,
            Mathf.Max(0.05f, requiredClearance),
            ringSpacing,
            ringCount,
            candidateCount,
            requiredClearance,
            out approachPoint,
            out failureReason,
            out failureKind);
    }

    public static bool TryResolveCombatApproachPoint(
        IEntityContext self,
        IEntityContext target,
        Vector3 targetPoint,
        float standOff,
        float minimumStandOff,
        float ringSpacing,
        int ringCount,
        int candidateCount,
        float requiredClearance,
        out Vector3 approachPoint,
        out string failureReason,
        out NavigationQueryFailureKind failureKind)
    {
        bool success = TryResolveCombatApproachPointFixed(
            self,
            target,
            new FixVector2((Fix64)targetPoint.x, (Fix64)targetPoint.z),
            (Fix64)standOff,
            (Fix64)minimumStandOff,
            (Fix64)ringSpacing,
            ringCount,
            candidateCount,
            (Fix64)requiredClearance,
            out FixVector2 approachPointFixed,
            out failureReason,
            out failureKind);
        approachPoint = ToWorldVector3(approachPointFixed);
        return success;
    }

    public static bool TryResolveCombatApproachPointFixed(
        IEntityContext self,
        IEntityContext target,
        FixVector2 targetPoint,
        Fix64 standOff,
        Fix64 minimumStandOff,
        Fix64 ringSpacing,
        int ringCount,
        int candidateCount,
        Fix64 requiredClearance,
        out FixVector2 approachPoint,
        out string failureReason,
        out NavigationQueryFailureKind failureKind)
    {
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long corePreparationStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        long phaseStartTicks = corePreparationStartTicks;
        long coreAttributedTicks = 0L;
        _perf.CombatApproachCalls++;
        approachPoint = targetPoint;
        failureReason = string.Empty;
        failureKind = NavigationQueryFailureKind.None;
        if (self == null)
            throw new InvalidOperationException("TryResolveCombatApproachPoint failed: self is null.");
        if (target == null)
            throw new InvalidOperationException("TryResolveCombatApproachPoint failed: target is null.");

        FixVector2 selfFramePosition = self.LogicFramePositionFixed();
        FixVector2 targetPointFixed = targetPoint;

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
            agent.AgentTypeId = self is ILogicFrameEntity logicEntity ? logicEntity.NavigationAgentTypeId : agent.AgentTypeId;
        }

        if (!TryEnsureWorldBuilt(agent.AgentTypeId))
        {
            failureKind = NavigationQueryFailureKind.Unavailable;
            failureReason = $"world unavailable agentType={agent.AgentTypeId}";
            return false;
        }

        if (HasPendingRuntimeDirty(_activeWorldState))
        {
            failureKind = NavigationQueryFailureKind.PendingRuntimeUpdate;
            failureReason = $"runtime dirty pending agentType={agent.AgentTypeId}";
            return false;
        }

        if (!TryResolveStartCellForReachabilityFixed(self, out int startX, out int startY, out int startIsland))
        {
            failureKind = NavigationQueryFailureKind.Unreachable;
            failureReason = $"start reachability failed {BuildReachabilityStartDiagnostics(self)}";
            return false;
        }

        if (!_world.WorldToGridFixed(targetPointFixed, out int targetX, out int targetY))
        {
            failureKind = NavigationQueryFailureKind.Unreachable;
            failureReason = $"target point outside grid targetRaw=({targetPoint.x.RawValue},{targetPoint.y.RawValue})";
            return false;
        }

        if (profile)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - corePreparationStartTicks;
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachCoreAgentPreparation,
                elapsedTicks);
            coreAttributedTicks += elapsedTicks;
        }
        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        Fix64 navigationClearance = Fix64.Max(agent.RadiusFixed, ResolveCollisionRadiusFixed(self));
        CombatTargetSlotKey key = CreateCombatTargetSlotKey(
            target,
            agent.AgentTypeId,
            targetPoint,
            targetX,
            targetY,
            standOff,
            minimumStandOff,
            ringSpacing,
            navigationClearance,
            ringCount,
            candidateCount);
        if (profile)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - phaseStartTicks;
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachCoreSetup,
                elapsedTicks);
            coreAttributedTicks += elapsedTicks;
        }
        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        CombatTargetSlotEntry entry = GetOrBuildCombatTargetSlotEntry(
            key,
            targetPoint,
            standOff,
            minimumStandOff,
            ringSpacing,
            ringCount,
            candidateCount,
            targetX,
            targetY,
            navigationClearance);
        if (profile)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - phaseStartTicks;
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachSlotCache,
                elapsedTicks);
            coreAttributedTicks += elapsedTicks;
        }
        if (entry == null || entry.Points == null)
            throw new InvalidOperationException("TryResolveCombatApproachPoint failed: combat target slot entry is invalid.");
        if (entry.CandidateDirectionsNormalized == null
            || entry.TargetDistanceErrors == null
            || entry.CandidateDirectionsNormalized.Length != entry.Points.Length
            || entry.TargetDistanceErrors.Length != entry.Points.Length)
        {
            throw new InvalidOperationException(
                $"TryResolveCombatApproachPoint failed: slot cache derived data is incomplete points={entry.Points.Length} " +
                $"directions={entry.CandidateDirectionsNormalized?.Length ?? -1} targetErrors={entry.TargetDistanceErrors?.Length ?? -1}.");
        }

        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        FixVector2 toTargetFromSelf = selfFramePosition - targetPointFixed;
        if (FixVector2.SqrMagnitude(toTargetFromSelf) <= Fix64.FromRaw(1))
            toTargetFromSelf = new FixVector2(Fix64.Zero, Fix64.One);
        toTargetFromSelf = toTargetFromSelf.GetNormalized();
        if (profile)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - phaseStartTicks;
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachDirectionSetup,
                elapsedTicks);
            coreAttributedTicks += elapsedTicks;
        }

        int ignoredTargetId = ResolveAgentId(target);
        Fix64 bestScore = Fix64.FromRaw(long.MaxValue);
        int bestIndex = -1;
        bool bestOccupied = false;
        int bestBlockingAgentId = 0;
        int occupiedSlotCount = 0;
        int availableSlotCount = 0;
        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        long scorePhaseStartTicks = phaseStartTicks;
        long scoreTicks = 0L;
        long scoreArithmeticTicks = 0L;
        long occupancyPhaseTicks = 0L;
        long scoreIslandFilterTicks = 0L;
        long occupancyPreparationTicks = 0L;
        long occupancyPreparationStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        PrepareCombatTargetSlotOccupancyCandidates(entry, requiredClearance, startIsland);
        if (profile)
        {
            occupancyPreparationTicks = Stopwatch.GetTimestamp() - occupancyPreparationStartTicks;
            MainThreadFrameProfiler.RecordLogicTickInvocation(
                MainThreadPerfScope.FlowCombatApproachOccupancyPreparation,
                occupancyPreparationTicks);
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachOccupancyPreparation,
                occupancyPreparationTicks);
        }
        for (int i = 0; i < entry.Points.Length; i++)
        {
            _perf.CombatApproachScoredCandidates++;
            long islandFilterStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            if (entry.IslandIds[i] != startIsland)
            {
                if (profile)
                    scoreIslandFilterTicks += Stopwatch.GetTimestamp() - islandFilterStartTicks;
                continue;
            }
            if (profile)
                scoreIslandFilterTicks += Stopwatch.GetTimestamp() - islandFilterStartTicks;
            _perf.CombatApproachSameIslandCandidates++;

            FixVector2 candidate = entry.Points[i];
            List<NavigationGoalOccupancyCandidate> occupancyCandidates = entry.OccupancyCandidatesBySlot[i]
                ?? throw new InvalidOperationException(
                    $"Combat approach occupancy cache missing same-island slot index={i} island={startIsland}.");
            long occupancyStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            bool occupied = IsNavigationGoalOccupiedByOtherFixed(
                selfId,
                ignoredTargetId,
                candidate,
                requiredClearance,
                true,
                out int blockingAgentId,
                occupancyCandidates);
            if (profile)
                occupancyPhaseTicks += Stopwatch.GetTimestamp() - occupancyStartTicks;
            if (occupied)
                occupiedSlotCount++;
            else
                availableSlotCount++;
            long scoreCandidateStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            long arithmeticStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            Fix64 distanceToSelf = FixVector2.Distance(selfFramePosition, candidate);
            Fix64 distanceToTargetError = entry.TargetDistanceErrors[i];
            Fix64 lowerBoundScore = distanceToSelf + distanceToTargetError * (Fix64)3;
            if (occupied)
                lowerBoundScore += (Fix64)1000;

            // Angle penalty is non-negative. If the distance/radius lower
            // bound cannot beat the current best score, skipping Atan2 is
            // exact and avoids the dominant per-candidate transcendental.
            if (lowerBoundScore >= bestScore)
            {
                if (profile)
                {
                    scoreArithmeticTicks += Stopwatch.GetTimestamp() - arithmeticStartTicks;
                    scoreTicks += Stopwatch.GetTimestamp() - scoreCandidateStartTicks;
                }
                continue;
            }

            FixVector2 candidateDirection = entry.CandidateDirectionsNormalized[i];
            Fix64 anglePenalty = ResolveCombatAnglePenaltyNormalized(candidateDirection, toTargetFromSelf);
            Fix64 score = lowerBoundScore + anglePenalty;
            if (profile)
                scoreArithmeticTicks += Stopwatch.GetTimestamp() - arithmeticStartTicks;
            if (score >= bestScore)
            {
                if (profile)
                {
                    scoreTicks += Stopwatch.GetTimestamp() - scoreCandidateStartTicks;
                }
                continue;
            }

            bestScore = score;
            bestIndex = i;
            bestOccupied = occupied;
            bestBlockingAgentId = blockingAgentId;
            if (profile)
            {
                scoreTicks += Stopwatch.GetTimestamp() - scoreCandidateStartTicks;
            }
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachScore,
                scoreTicks);
            if (scoreArithmeticTicks > 0L)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowCombatApproachScoreArithmetic,
                    scoreArithmeticTicks);
            }
            if (scoreIslandFilterTicks > 0L)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowCombatApproachCoreScoreIslandFilter,
                    scoreIslandFilterTicks);
            long elapsedTicks = Stopwatch.GetTimestamp() - scorePhaseStartTicks;
            MainThreadFrameProfiler.RecordLogicTickInvocation(
                MainThreadPerfScope.FlowCombatApproachCoreScorePhase,
                elapsedTicks);
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachCoreScorePhase,
                elapsedTicks);
            coreAttributedTicks += elapsedTicks;
            long scoreUnattributedTicks = elapsedTicks
                                          - occupancyPreparationTicks
                                          - occupancyPhaseTicks
                                          - scoreTicks;
            if (scoreUnattributedTicks < 0L)
                throw new InvalidOperationException(
                    $"Combat approach score timing is inconsistent. total={elapsedTicks}, preparation={occupancyPreparationTicks}, occupancy={occupancyPhaseTicks}, score={scoreTicks}.");
            if (scoreUnattributedTicks > 0L)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowCombatApproachCoreScoreUnattributed,
                    scoreUnattributedTicks);
        }
        long expandedPhaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool usedExpandedSlot = false;
        int expandedRing = -1;
        if (bestIndex < 0)
        {
            if (TryResolveExpandedCombatApproachPoint(
                    selfId,
                    ignoredTargetId,
                    startIsland,
                    targetPoint,
                    targetX,
                    targetY,
                    standOff,
                    minimumStandOff,
                    ringSpacing,
                    ringCount,
                    candidateCount,
                    requiredClearance,
                    toTargetFromSelf,
                    selfFramePosition,
                    out approachPoint,
                    out expandedRing,
                    out Fix64 expandedScore))
            {
                bestScore = expandedScore;
                bestOccupied = false;
                bestBlockingAgentId = 0;
                usedExpandedSlot = true;
            }
            else
            {
                _perf.CombatApproachNoSlotFailures++;
                failureReason =
                    $"no combat approach slot in start island target={target.CharacterKey} start=({startX},{startY}) startIsland={startIsland} " +
                    $"targetPoint={targetPoint} standOff=[{minimumStandOff:F3},{standOff:F3}] slots={entry.Points.Length} build={entry.BuildSummary} " +
                    $"candidateIslands={BuildCombatApproachIslandSummary(entry, startIsland)} expandedFailed=True";
                failureKind = NavigationQueryFailureKind.Unreachable;
                if (profile)
                {
                    long expandedElapsedTicks = Stopwatch.GetTimestamp() - expandedPhaseStartTicks;
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.FlowCombatApproachCoreExpandedPhase,
                        expandedElapsedTicks);
                    coreAttributedTicks += expandedElapsedTicks;
                    long coreElapsedTicks = Stopwatch.GetTimestamp() - corePreparationStartTicks;
                    long coreUnattributedTicks = coreElapsedTicks - coreAttributedTicks;
                    if (coreUnattributedTicks < 0L)
                    {
                        throw new InvalidOperationException(
                            $"Combat approach core timing is inconsistent on failure. total={coreElapsedTicks}, attributed={coreAttributedTicks}.");
                    }
                    if (coreUnattributedTicks > 0L)
                        MainThreadFrameProfiler.Record(
                            MainThreadPerfScope.FlowCombatApproachCoreUnattributed,
                            coreUnattributedTicks);
                }
                return false;
            }
        }
        else
        {
            approachPoint = entry.Points[bestIndex];
        }

        if (!usedExpandedSlot
            && bestOccupied
            && TryResolveExpandedCombatApproachPoint(
                selfId,
                ignoredTargetId,
                startIsland,
                targetPoint,
                targetX,
                targetY,
                standOff,
                minimumStandOff,
                ringSpacing,
                ringCount,
                candidateCount,
                requiredClearance,
                toTargetFromSelf,
                selfFramePosition,
                out FixVector2 expandedApproachPoint,
                out expandedRing,
                out Fix64 occupiedExpandedScore))
        {
            approachPoint = expandedApproachPoint;
            bestScore = occupiedExpandedScore;
            bestOccupied = false;
            bestBlockingAgentId = 0;
            usedExpandedSlot = true;
        }

        if (profile)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - expandedPhaseStartTicks;
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachCoreExpandedPhase,
                elapsedTicks);
            coreAttributedTicks += elapsedTicks;
        }

        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        RegisterNavigationGoalReservationFixed(selfId, approachPoint, requiredClearance);
        if (GameDebugSettings.IsEnabled(DebugCategory.Move)
            && GameDebugSettings.ShouldLogMovementForCharacter(self.CharacterKey)
            && TryConsumeSuccessfulMoveDiagnosticBudget())
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowCombatSlot] self={self.CharacterKey} selfId={selfId} target={target.CharacterKey} targetId={ignoredTargetId} " +
                $"selfPos={self.Position} targetPoint={targetPoint} previousGoal={agent.NavState.LastGoalWorld} " +
                $"approach={approachPoint} slotIndex={bestIndex} occupied={bestOccupied} blocker={bestBlockingAgentId} score={bestScore:F3} " +
                $"start=({startX},{startY}) startIsland={startIsland} keyTargetCell={key.TargetCellIndex} slots={entry.Points.Length} " +
                $"standOff=[{minimumStandOff:F3},{standOff:F3}] availableSlots={availableSlotCount} occupiedSlots={occupiedSlotCount} " +
                $"expanded={usedExpandedSlot} expandedRing={expandedRing}");
        }
        if (profile)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - phaseStartTicks;
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachFinalize,
                elapsedTicks);
            coreAttributedTicks += elapsedTicks;
            long coreElapsedTicks = Stopwatch.GetTimestamp() - corePreparationStartTicks;
            long coreUnattributedTicks = coreElapsedTicks - coreAttributedTicks;
            if (coreUnattributedTicks < 0L)
            {
                throw new InvalidOperationException(
                    $"Combat approach core timing is inconsistent. total={coreElapsedTicks}, attributed={coreAttributedTicks}.");
            }
            if (coreUnattributedTicks > 0L)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowCombatApproachCoreUnattributed,
                    coreUnattributedTicks);
        }

        _perf.CombatApproachSuccesses++;
        return true;
    }

    private static string BuildCombatApproachIslandSummary(CombatTargetSlotEntry entry, int startIsland)
    {
        if (entry == null || entry.IslandIds == null || entry.IslandIds.Length == 0)
            return "none";

        Dictionary<int, int> counts = new Dictionary<int, int>();
        for (int i = 0; i < entry.IslandIds.Length; i++)
        {
            int island = entry.IslandIds[i];
            counts.TryGetValue(island, out int count);
            counts[island] = count + 1;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder(64);
        builder.Append("start=").Append(startIsland).Append('[');
        int emitted = 0;
        foreach (KeyValuePair<int, int> pair in counts)
        {
            if (emitted > 0)
                builder.Append(',');
            builder.Append(pair.Key).Append(':').Append(pair.Value);
            emitted++;
            if (emitted >= 8 && counts.Count > emitted)
            {
                builder.Append(",...");
                break;
            }
        }
        builder.Append(']');
        return builder.ToString();
    }

    private static bool TryResolveExpandedCombatApproachPoint(
        int selfId,
        int ignoredTargetId,
        int startIsland,
        FixVector2 targetPoint,
        int targetX,
        int targetY,
        Fix64 standOff,
        Fix64 minimumStandOff,
        Fix64 ringSpacing,
        int baseRingCount,
        int candidateCount,
        Fix64 requiredClearance,
        FixVector2 preferredDirection,
        FixVector2 selfPosition,
        out FixVector2 approachPoint,
        out int selectedRing,
        out Fix64 selectedScore)
    {
        _perf.CombatApproachExpandedCalls++;
        long expandedStartTicks = MainThreadFrameProfiler.LoggingEnabled
            ? Stopwatch.GetTimestamp()
            : 0L;
        approachPoint = targetPoint;
        selectedRing = -1;
        selectedScore = Fix64.FromRaw(long.MaxValue);
        if (_world == null)
            throw new InvalidOperationException("TryResolveExpandedCombatApproachPoint failed: world is null.");

        FixVector2 targetPointFixed = targetPoint;
        bool targetLineCellValid = _world.IsWalkable(targetX, targetY);
        int targetIsland = targetLineCellValid ? ResolveIslandIdForDiagnostics(_world, targetX, targetY) : 0;
        Fix64 spacing = Fix64.Max(Fix64.FromRaw(205), ringSpacing);
        Fix64 maximumRadius = Fix64.Max(Fix64.FromRaw(205), standOff);
        Fix64 minimumRadius = Fix64.Clamp(minimumStandOff, Fix64.FromRaw(205), maximumRadius);
        int inwardSteps = checked((int)(long)Fix64.Ceiling(Fix64.Max(Fix64.Zero, standOff - minimumRadius) / spacing));
        int generatedRingCount = Mathf.Max(Mathf.Max(1, baseRingCount), inwardSteps + 1);
        int samplesPerRing = Mathf.Max(16, candidateCount * 2);
        for (int ring = 0; ring < generatedRingCount; ring++)
        {
            Fix64 radius = Fix64.Max(minimumRadius, standOff - (Fix64)ring * spacing);
            for (int i = 0; i < samplesPerRing; i++)
            {
                _perf.CombatApproachExpandedCandidates++;
                FixVector2 direction = ResolveCombatSampleDirection(i, samplesPerRing, halfStep: true);
                FixVector2 candidate = targetPointFixed + direction * radius;
                if (!_world.WorldToGridFixed(candidate, out int x, out int y))
                    continue;
                if (!_world.IsWalkable(x, y))
                    continue;

                int islandId = ResolveIslandIdForDiagnostics(_world, x, y);
                if (islandId != startIsland)
                    continue;

                FixVector2 worldPoint = candidate;
                if (!IsNavigationPointClearFixed(
                        _world,
                        worldPoint,
                        ResolveNavigationQueryClearanceFixed(_world, requiredClearance),
                        includeRuntimeObstacleOverlay: true))
                    continue;
                if (targetLineCellValid
                    && targetIsland == islandId
                    && !HasSoftCostTolerantGridLineOfSight(_world, x, y, targetX, targetY, maxAllowedCost: 15))
                    continue;
                if (IsNavigationGoalOccupiedByOtherFixed(
                        selfId,
                        ignoredTargetId,
                        worldPoint,
                        requiredClearance,
                        includeReservations: true,
                        out _))
                    continue;

                FixVector2 candidateDirection = worldPoint - targetPointFixed;
                Fix64 anglePenalty = ResolveCombatAnglePenalty(candidateDirection, preferredDirection);
                Fix64 distanceToSelf = FixVector2.Distance(selfPosition, worldPoint);
                Fix64 distanceToPreferredRadius = Fix64.Abs(FixVector2.Distance(worldPoint, targetPointFixed) - standOff);
                Fix64 score = distanceToSelf + anglePenalty + distanceToPreferredRadius * (Fix64)3;
                if (score >= selectedScore)
                    continue;

                selectedScore = score;
                approachPoint = worldPoint;
                selectedRing = ring;
            }

            if (selectedRing == ring)
            {
                if (MainThreadFrameProfiler.LoggingEnabled)
                {
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.FlowCombatApproachExpanded,
                        Stopwatch.GetTimestamp() - expandedStartTicks);
                }
                return true;
            }
        }

        bool resolved = selectedRing >= 0;
        if (MainThreadFrameProfiler.LoggingEnabled)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachExpanded,
                Stopwatch.GetTimestamp() - expandedStartTicks);
        }
        return resolved;
    }

    private static FixVector2 ResolveCombatSampleDirection(int index, int sampleCount, bool halfStep)
    {
        if (sampleCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleCount), sampleCount, "Combat sample count must be positive.");
        if (index < 0 || index >= sampleCount)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Combat sample index is outside the sample range.");

        int numerator = halfStep ? checked(index * 2 + 1) : index;
        int denominator = halfStep ? checked(sampleCount * 2) : sampleCount;
        Fix64 angle = Fix64.PITimes2 * (Fix64)numerator / (Fix64)denominator;
        return new FixVector2(Fix64.Sin(angle), Fix64.Cos(angle));
    }

    private static Fix64 ResolveCombatAnglePenalty(FixVector2 direction, FixVector2 preferredDirection)
    {
        if (FixVector2.SqrMagnitude(direction) <= Fix64.FromRaw(1)
            || FixVector2.SqrMagnitude(preferredDirection) <= Fix64.FromRaw(1))
        {
            return Fix64.Zero;
        }

        return ResolveCombatAnglePenaltyNormalized(
            direction.GetNormalized(),
            preferredDirection.GetNormalized());
    }

    private static Fix64 ResolveCombatAnglePenaltyNormalized(
        FixVector2 normalizedDirection,
        FixVector2 normalizedPreferred)
    {
        Fix64 cross = Fix64.Abs(
            normalizedDirection.x * normalizedPreferred.y
            - normalizedDirection.y * normalizedPreferred.x);
        Fix64 dot = normalizedDirection.x * normalizedPreferred.x
                    + normalizedDirection.y * normalizedPreferred.y;
        Fix64 angleDegrees = Fix64.Atan2(cross, dot) * (Fix64)180 / Fix64.PI;
        return angleDegrees * Fix64.FromRaw(62);
    }

    private static Vector3 ToWorldVector3(FixVector2 point)
    {
        return new Vector3((float)point.x, 0f, (float)point.y);
    }

    public static bool TryPrepareSharedGoalRequest(Vector3 goalPosition, IReadOnlyList<IEntityContext> sources, out string failureReason)
    {
        failureReason = string.Empty;
        if (sources == null)
            throw new InvalidOperationException("TryPrepareSharedGoalRequest failed: sources is null.");
        if (sources.Count == 0)
        {
            failureReason = "sources is empty";
            return false;
        }

        int preparedCount = 0;
        for (int i = 0; i < sources.Count; i++)
        {
            IEntityContext source = sources[i];
            if (source == null)
            {
                failureReason = $"source[{i}] is null";
                return false;
            }

            if (!TryPrepareNavigationRequest(source, goalPosition, out failureReason))
                return false;

            int sourceId = ResolveAgentId(source);
            if (!Agents.TryGetValue(sourceId, out AgentRuntimeData agent) || agent == null)
                throw new InvalidOperationException($"TryPrepareSharedGoalRequest failed: prepared agent is missing source={source.CharacterKey} id={sourceId}.");
            if (_world == null || !_world.TryGetSectorId(agent.NavState.StableGoalX, agent.NavState.StableGoalY, out int goalSectorId))
                throw new InvalidOperationException(
                    $"TryPrepareSharedGoalRequest failed: prepared goal has no committed sector source={source.CharacterKey} goal=({agent.NavState.StableGoalX},{agent.NavState.StableGoalY}).");
            EnqueueSharedGoalFieldBuild(
                goalSectorId,
                agent.NavState.StableGoalX,
                agent.NavState.StableGoalY,
                ResolvePreferredAgentTypeId(agent.AgentTypeId),
                agent.NavState.CurrentSectorId,
                agent.NavState.CurrentCell.x,
                agent.NavState.CurrentCell.y);

            preparedCount++;
        }

        return preparedCount == sources.Count;
    }

    public static bool TryPrepareNavigationRequestFixed(
        IEntityContext source,
        FixVector2 goalPosition,
        out string failureReason)
    {
        return TryPrepareNavigationRequestFixedCore(source, goalPosition, rejectPendingRuntimeDirty: true, out failureReason);
    }

    public static bool TryPrepareNavigationSyncRequestFixed(
        IEntityContext source,
        FixVector2 inputGoalPosition,
        out string failureReason)
    {
        return TryPrepareNavigationSyncRequestFixed(
            source,
            inputGoalPosition,
            Fix64.Zero,
            out failureReason);
    }

    public static bool TryPrepareNavigationSyncRequestFixed(
        IEntityContext source,
        FixVector2 inputGoalPosition,
        Fix64 maximumTravelDistance,
        out string failureReason)
    {
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        if (source == null)
            throw new InvalidOperationException("TryPrepareNavigationSyncRequestFixed failed: source is null.");
        if (maximumTravelDistance < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumTravelDistance), maximumTravelDistance, "Maximum travel distance cannot be negative.");

        int sourceId = ResolveAgentId(source);
        if (!Agents.TryGetValue(sourceId, out AgentRuntimeData agent))
        {
            RegisterSyntheticAgent(source);
            agent = Agents[sourceId];
        }
        long phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool worldReady = TryEnsureWorldBuilt(agent.AgentTypeId);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowPrepareWorld,
                Stopwatch.GetTimestamp() - phaseStartTicks);
        }
        if (!worldReady)
        {
            failureReason = $"world unavailable source={source.CharacterKey} agentType={agent.AgentTypeId}";
            return false;
        }
        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        FixVector2 occupiedGoal = ResolveNavigationGoalOccupancyFixed(source, agent, inputGoalPosition);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowPrepareGoalOccupancy,
                Stopwatch.GetTimestamp() - phaseStartTicks);
        }
        bool hasMovingTarget = source.TargetComp?.CurrentTarget != null
                               && !ReferenceEquals(source.TargetComp.CurrentTarget, source)
                               && IsNavigationMovingTarget(source.TargetComp.CurrentTarget);
        FixVector2 navigationGoal = hasMovingTarget ? inputGoalPosition : occupiedGoal;
        if (!TryPrepareNavigationRequestFixedCore(
                source,
                navigationGoal,
                rejectPendingRuntimeDirty: false,
                out failureReason))
        {
            agent.NavState.HasPreparedNavigationSnapshot = false;
            agent.NavState.PreparedNavigationFrame = -1;
            agent.NavState.PreparedMaximumTravelDistanceFixed = Fix64.Zero;
            return false;
        }

        agent.NavState.HasPreparedNavigationSnapshot = true;
        agent.NavState.PreparedNavigationFrame = GetFrameCount();
        agent.NavState.PreparedInputGoalFixed = inputGoalPosition;
        agent.NavState.PreparedNavigationGoalFixed = navigationGoal;
        agent.NavState.PreparedMaximumTravelDistanceFixed = maximumTravelDistance;
        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        EnqueueSteeringReadDomainFlowTileBuilds(agent, maximumTravelDistance);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowPrepareReadDomain,
                Stopwatch.GetTimestamp() - phaseStartTicks);
        }
        return true;
    }

    public static void CollectNavigationSyncRequestFixed(
        IEntityContext source,
        FixVector2 inputGoalPosition,
        Fix64 maximumTravelDistance)
    {
        if (source == null)
            throw new InvalidOperationException("CollectNavigationSyncRequestFixed failed: source is null.");
        if (maximumTravelDistance < Fix64.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumTravelDistance),
                maximumTravelDistance,
                "Maximum travel distance cannot be negative.");
        }

        int frame = GetFrameCount();
        if (_collectedNavigationSyncFrame != frame)
        {
            if (CollectedNavigationSyncRequests.Count != 0 || CollectedNavigationSyncSourceIds.Count != 0)
            {
                throw new InvalidOperationException(
                    $"NavigationSync request collection crossed a frame without resolve. collectedFrame={_collectedNavigationSyncFrame}, currentFrame={frame}, requests={CollectedNavigationSyncRequests.Count}.");
            }
            _collectedNavigationSyncFrame = frame;
        }

        int sourceId = ResolveAgentId(source);
        if (!Agents.TryGetValue(sourceId, out AgentRuntimeData agent))
        {
            throw new InvalidOperationException(
                $"CollectNavigationSyncRequestFixed failed: source is not registered source={source.CharacterKey}, id={sourceId}.");
        }
        if (!CollectedNavigationSyncSourceIds.Add(sourceId))
        {
            throw new InvalidOperationException(
                $"NavigationSync source submitted more than one request in the same frame source={source.CharacterKey}, id={sourceId}, frame={frame}.");
        }

        IEntityContext currentTarget = source.TargetComp?.CurrentTarget;
        int movingTargetId = currentTarget != null
                             && !ReferenceEquals(currentTarget, source)
                             && IsNavigationMovingTarget(currentTarget)
            ? ResolveAgentId(currentTarget)
            : int.MinValue;
        agent.NavState.HasPreparedNavigationSnapshot = false;
        agent.NavState.PreparedNavigationFrame = -1;
        agent.NavState.PreparedMaximumTravelDistanceFixed = Fix64.Zero;
        CollectedNavigationSyncRequests.Add(new NavigationSyncRequest
        {
            Source = source,
            SourceId = sourceId,
            AgentTypeId = ResolvePreferredAgentTypeId(agent.AgentTypeId),
            MovingTargetId = movingTargetId,
            InputGoalPosition = inputGoalPosition,
            MaximumTravelDistance = maximumTravelDistance
        });
    }

    public static void ResolveCollectedNavigationSyncRequests()
    {
        BeginPerfCall();
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        int frame = GetFrameCount();
        if (CollectedNavigationSyncRequests.Count == 0)
        {
            if (CollectedNavigationSyncSourceIds.Count != 0)
                throw new InvalidOperationException("NavigationSync request source index is non-empty while the request list is empty.");
            long pruneStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            PruneInactiveNavigationPathRequestSources(frame);
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationRequestPrune,
                    Stopwatch.GetTimestamp() - pruneStartTicks);
            }
            _collectedNavigationSyncFrame = frame;
            return;
        }
        if (_collectedNavigationSyncFrame != frame)
        {
            throw new InvalidOperationException(
                $"NavigationSync resolve frame mismatch. collectedFrame={_collectedNavigationSyncFrame}, currentFrame={frame}, requests={CollectedNavigationSyncRequests.Count}.");
        }

        _perf.SectorCorridorPolicyAuthorityHashRefreshes = 0;
        _editorNavigationSyncAuthorityCommitCount = 0;
        BeginNavigationSyncBatchResolve();
        try
        {
        long demandBatchStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        long sectionStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        MovingTargetAnchorBatchResolutions.Clear();
        CollectedNavigationSyncRequests.Sort(CompareNavigationSyncRequests);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationRequestSort,
                Stopwatch.GetTimestamp() - sectionStartTicks);
        }
        for (int i = 0; i < CollectedNavigationSyncRequests.Count; i++)
        {
            NavigationSyncRequest request = CollectedNavigationSyncRequests[i]
                                            ?? throw new InvalidOperationException($"NavigationSync request is null at sorted index={i}.");
            long phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            if (!TryResolveNavigationPathDemand(request, out NavigationPathDemand demand, out string failureReason))
            {
                throw new InvalidOperationException(
                    $"[{request.Source.CharacterKey}] NavigationSync batch resolve rejected target={request.InputGoalPosition}: {failureReason}");
            }
            _perf.NavigationDemandResolutionCount++;
            if (profile)
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - phaseStartTicks;
                _perf.NavigationDemandResolutionTicks += elapsedTicks;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationDemandResolve,
                    elapsedTicks);
            }

            // A target-side projection can be pending before any path request
            // exists. The source has already received its explicit prepared
            // navigation snapshot; dispatch starts only after publication.
            if (demand == null)
                continue;

            long dispatchStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            phaseStartTicks = dispatchStartTicks;
            bool reused = TryReuseNavigationPathDemand(demand);
            if (profile)
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationDemandReuse, Stopwatch.GetTimestamp() - phaseStartTicks);
            if (!reused)
            {
                phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                EnqueueNavigationPathDemand(demand);
                if (profile)
                    MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowNavigationDemandEnqueue, Stopwatch.GetTimestamp() - phaseStartTicks);
            }
            _perf.NavigationDemandDispatchCount++;
            if (profile)
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - dispatchStartTicks;
                _perf.NavigationDemandDispatchTicks += elapsedTicks;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationDemandDispatch,
                    elapsedTicks);
            }
        }

        MovingTargetAnchorBatchResolutions.Clear();

        sectionStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        PruneInactiveNavigationPathRequestSources(frame);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationRequestPrune,
                Stopwatch.GetTimestamp() - sectionStartTicks);
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowNavigationResolveDemandBatch,
                Stopwatch.GetTimestamp() - demandBatchStartTicks);
        }
        BeginNavigationWorkBudget(Config.PathRequestOperationQuota);
        long pathQueueAllocatedBytes = profile ? GC.GetAllocatedBytesForCurrentThread() : 0L;
        int pathQueueGen0Collections = profile ? GC.CollectionCount(0) : 0;
        int pathQueueGen1Collections = profile ? GC.CollectionCount(1) : 0;
        int pathQueueGen2Collections = profile ? GC.CollectionCount(2) : 0;
        long pathQueueStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        try
        {
            ProcessNavigationPathRequestsAcrossWorlds();
#if UNITY_EDITOR
            // Editor tests call this API synchronously without a live logic
            // timeline. A high-quota resolve must observe the same committed
            // authority as the next runtime ticks, including completion of
            // already scheduled graph/route slices. Keep low-quota tests and
            // the live timeline explicitly incremental.
            if (!LogicFrameRuntime.IsTimelineRunning
                && Config.PathRequestOperationQuota >= 1_000_000)
            {
                int drainPasses = 0;
                while (NavigationPathRequestQueue.Count != 0)
                {
                    if (++drainPasses > 100000)
                    {
                        throw new InvalidOperationException(
                            "Editor navigation path request drain exceeded its deterministic guard.");
                    }

                    EndNavigationWorkBudget();
                    BeginNavigationWorkBudget(Config.PathRequestOperationQuota);
                    if (DeferredSectorCorridorPolicyAuthorityKeys.Count != 0)
                        CommitDeferredNavigationSyncPolicyAuthorities();
                    ProcessNavigationPathRequestsAcrossWorlds();
                }
            }
#endif
        }
        finally
        {
            if (profile)
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - pathQueueStartTicks;
                _perf.NavigationPathRequestQueueTicks += elapsedTicks;
                _perf.NavigationPathAllocatedBytes += Math.Max(
                    0L,
                    GC.GetAllocatedBytesForCurrentThread() - pathQueueAllocatedBytes);
                _perf.NavigationPathGen0Collections += GC.CollectionCount(0) - pathQueueGen0Collections;
                _perf.NavigationPathGen1Collections += GC.CollectionCount(1) - pathQueueGen1Collections;
                _perf.NavigationPathGen2Collections += GC.CollectionCount(2) - pathQueueGen2Collections;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathAdvance,
                    elapsedTicks);
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationResolvePathQueueBatch,
                    elapsedTicks);
            }
#if UNITY_EDITOR
            if (profile)
            {
                long diagnosticStartTicks = Stopwatch.GetTimestamp();
                CaptureEditorNavigationPathTickDiagnostic();
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathDiagnosticCapture,
                    Stopwatch.GetTimestamp() - diagnosticStartTicks);
            }
            else
            {
                CaptureEditorNavigationPathTickDiagnostic();
            }
#endif
            if (profile)
            {
                long budgetEndStartTicks = Stopwatch.GetTimestamp();
                EndNavigationWorkBudget();
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowNavigationPathBudgetEnd,
                    Stopwatch.GetTimestamp() - budgetEndStartTicks);
            }
            else
            {
                EndNavigationWorkBudget();
            }
        }

        EndNavigationSyncBatchResolve();
        CollectedNavigationSyncRequests.Clear();
        CollectedNavigationSyncSourceIds.Clear();
        }
        catch
        {
            if (_navigationSyncBatchResolveActive)
                EndNavigationSyncBatchResolve();
            throw;
        }
    }

}
