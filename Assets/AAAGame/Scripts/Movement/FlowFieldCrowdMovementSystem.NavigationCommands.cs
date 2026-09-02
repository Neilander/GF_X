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
    private static void BeginNavigationSyncBatchResolve()
    {
        if (_navigationSyncBatchResolveActive || DeferredSectorCorridorPolicyAuthorityKeys.Count != 0)
            throw new InvalidOperationException("NavigationSync batch resolve began with an active or dirty authority transaction.");
        _navigationSyncBatchResolveActive = true;
    }

    private static void EndNavigationSyncBatchResolve()
    {
        if (!_navigationSyncBatchResolveActive)
            throw new InvalidOperationException("NavigationSync batch resolve ended without an active transaction.");
        try
        {
            CommitDeferredNavigationSyncPolicyAuthorities();
        }
        finally
        {
            DeferredSectorCorridorPolicyAuthorityKeys.Clear();
            _navigationSyncBatchResolveActive = false;
        }
    }

    private static int CompareNavigationSyncRequests(NavigationSyncRequest left, NavigationSyncRequest right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left == null)
            return -1;
        if (right == null)
            return 1;
        int order = left.AgentTypeId.CompareTo(right.AgentTypeId);
        if (order != 0) return order;
        order = left.MovingTargetId.CompareTo(right.MovingTargetId);
        if (order != 0) return order;
        order = left.InputGoalPosition.x.RawValue.CompareTo(right.InputGoalPosition.x.RawValue);
        if (order != 0) return order;
        order = left.InputGoalPosition.y.RawValue.CompareTo(right.InputGoalPosition.y.RawValue);
        return order != 0 ? order : left.SourceId.CompareTo(right.SourceId);
    }

    public static void ClearNavigationSyncRequest(IEntityContext source)
    {
        if (source == null)
            throw new InvalidOperationException("ClearNavigationSyncRequest failed: source is null.");

        int sourceId = ResolveAgentId(source);
        if (!Agents.TryGetValue(sourceId, out AgentRuntimeData agent))
            return;

        agent.NavState.HasPreparedNavigationSnapshot = false;
        agent.NavState.PreparedNavigationFrame = -1;
        agent.NavState.PreparedMaximumTravelDistanceFixed = Fix64.Zero;
        agent.NavState.HasGoal = false;
        ClearCommittedNavigationPath(agent);
        agent.NavState.HasPendingNavigation = false;
        CancelNavigationPathRequestsForSource(sourceId);
        RemoveFixedPortalParticipation(sourceId);
    }

    private static void ClearCommittedNavigationPath(AgentRuntimeData agent)
    {
        if (agent == null)
            throw new ArgumentNullException(nameof(agent));
        agent.NavState.PathHandle = null;
        agent.NavState.HasPendingNavigationReplacement = false;
        agent.NavState.CommittedMovingTargetId = int.MinValue;
    }

    private static void PreparePendingMovingTargetProjectionNavigation(
        AgentRuntimeData agent,
        int movingTargetId,
        int startSectorId,
        FixVector2 inputGoal,
        Fix64 maximumTravelDistance)
    {
        if (agent == null)
            throw new ArgumentNullException(nameof(agent));
        if (movingTargetId == int.MinValue)
            throw new InvalidOperationException("Pending target projection requires a moving target id.");
        if (startSectorId < 0)
            throw new InvalidOperationException("Pending target projection requires a valid start sector.");

        AgentNavState nav = agent.NavState;
        ClearCommittedNavigationPath(agent);
        nav.HasPendingNavigation = true;
        RemoveFixedPortalParticipation(agent.Id);
        nav.PreparedNavigationGoalFixed = inputGoal;

        nav.StableGoalTargetId = movingTargetId;
        nav.PreparedNavigationFrame = GetFrameCount();
        nav.HasPreparedNavigationSnapshot = true;
        nav.PreparedInputGoalFixed = inputGoal;
        nav.PreparedMaximumTravelDistanceFixed = maximumTravelDistance;
    }

    private static bool RequiresPreparedNavigationSnapshot()
    {
        if (LogicFrameRuntime.IsTimelineRunning)
            return true;
#if UNITY_EDITOR
        return _editorTestRequirePreparedNavigationSnapshot;
#else
        return false;
#endif
    }

    private static bool TryPrepareNavigationRequestFixedCore(
        IEntityContext source,
        FixVector2 goalPosition,
        bool rejectPendingRuntimeDirty,
        out string failureReason)
    {
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        _perf.NavigationPrepareRequests++;
#if UNITY_EDITOR
        _testNavigationPrepareRequestCount++;
#endif
        failureReason = string.Empty;
        if (source == null)
            throw new InvalidOperationException("TryPrepareNavigationRequestFixed failed: source is null.");

        int sourceId = ResolveAgentId(source);
        if (!Agents.TryGetValue(sourceId, out AgentRuntimeData agent))
        {
            RegisterSyntheticAgent(source);
            agent = Agents[sourceId];
        }
        else
        {
            agent.PositionFixed = source.LogicFramePositionFixed();
            agent.Position = ToWorldVector3(agent.PositionFixed);
            agent.RadiusFixed = ResolveCollisionRadiusFixed(source);
            agent.Radius = (float)agent.RadiusFixed;
            agent.AgentTypeId = source is ILogicFrameEntity logicEntity ? logicEntity.NavigationAgentTypeId : agent.AgentTypeId;
        }

        if (!TryEnsureWorldBuilt(agent.AgentTypeId))
        {
            failureReason = $"world unavailable source={source.CharacterKey} agentType={agent.AgentTypeId}";
            return false;
        }

        if (rejectPendingRuntimeDirty && HasPendingRuntimeDirty(_activeWorldState))
        {
            failureReason = $"runtime dirty pending source={source.CharacterKey} agentType={agent.AgentTypeId}";
            return false;
        }

        long phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool startResolved = TryResolveStartCellForReachabilityFixed(source, out int startX, out int startY, out int startIsland);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowPrepareStartCell,
                Stopwatch.GetTimestamp() - phaseStartTicks);
        }
        if (!startResolved)
        {
            failureReason = $"start reachability failed source={source.CharacterKey} {BuildReachabilityStartDiagnostics(source)}";
            return false;
        }

        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool goalResolved = TryResolveStableGoalCellFixed(
                agent,
                source,
                goalPosition,
                out int goalX,
                out int goalY,
                out FixVector2 stableGoalPosition,
                out int finalGoalX,
                out int finalGoalY,
                out FixVector2 finalGoalPosition,
                out bool useSectorCorridorPolicy,
                out bool pendingProjection);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowPrepareStableGoal,
                Stopwatch.GetTimestamp() - phaseStartTicks);
        }
        if (!goalResolved)
        {
            if (pendingProjection)
            {
                failureReason = $"moving-target projection pending source={source.CharacterKey}";
                return false;
            }
            failureReason = $"goal reachability failed source={source.CharacterKey} {BuildGoalResolutionFailure(source, ToWorldVector3(goalPosition))}";
            return false;
        }

        if (!_world.TryGetSectorId(startX, startY, out int startSectorId))
        {
            failureReason = $"start sector failed source={source.CharacterKey} start=({startX},{startY})";
            return false;
        }

        if (!_world.TryGetSectorId(goalX, goalY, out int goalSectorId))
        {
            failureReason = $"route goal sector failed source={source.CharacterKey} goal=({goalX},{goalY})";
            return false;
        }
        if (!_world.TryGetSectorId(finalGoalX, finalGoalY, out _))
        {
            failureReason = $"final goal sector failed source={source.CharacterKey} goal=({finalGoalX},{finalGoalY})";
            return false;
        }

        agent.NavState.CurrentCell = new Vector2Int(startX, startY);
        agent.NavState.CurrentSectorId = startSectorId;
        agent.NavState.HasGoal = true;
        agent.NavState.LastGoalWorldFixed = finalGoalPosition;
        agent.NavState.LastGoalWorld = ToWorldVector3(finalGoalPosition);
        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool pathReady = EnsurePathHandle(
            agent,
            startSectorId,
            goalSectorId,
            startX,
            startY,
            goalX,
            goalY,
            useSectorCorridorPolicy);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowPreparePathHandle,
                Stopwatch.GetTimestamp() - phaseStartTicks);
        }
        if (!pathReady)
        {
            failureReason = $"path handle failed source={source.CharacterKey} startSector={startSectorId} goalSector={goalSectorId}";
            return false;
        }
        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool pathAdvanced = TryAdvancePathToCurrentSector(agent, startSectorId);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowPreparePathAdvance,
                Stopwatch.GetTimestamp() - phaseStartTicks);
        }
        if (!pathAdvanced)
        {
            failureReason = $"prepared path does not contain current sector source={source.CharacterKey} startSector={startSectorId} goalSector={goalSectorId}";
            return false;
        }

        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        UpdateFixedPortalParticipation(agent);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowPrepareTileDemand,
                Stopwatch.GetTimestamp() - phaseStartTicks);
        }
        return true;
    }

    public static bool TryPrepareNavigationRequest(IEntityContext source, Vector3 goalPosition, out string failureReason)
    {
        return TryPrepareNavigationRequestFixed(
            source,
            new FixVector2((Fix64)goalPosition.x, (Fix64)goalPosition.z),
            out failureReason);
    }

    private static CombatTargetSlotKey CreateCombatTargetSlotKey(
        IEntityContext target,
        int agentTypeId,
        FixVector2 targetPoint,
        int targetX,
        int targetY,
        Fix64 standOff,
        Fix64 minimumStandOff,
        Fix64 ringSpacing,
        Fix64 requiredClearance,
        int ringCount,
        int candidateCount)
    {
        int targetId = ResolveAgentId(target);
        return new CombatTargetSlotKey(
            _world.Version,
            ResolvePreferredAgentTypeId(agentTypeId),
            targetId,
            _world.GetIndex(targetX, targetY),
            targetPoint.x.RawValue,
            targetPoint.y.RawValue,
            standOff.RawValue,
            minimumStandOff.RawValue,
            ringSpacing.RawValue,
            Fix64.Max(Fix64.Zero, requiredClearance).RawValue,
            Mathf.Max(1, ringCount),
            Mathf.Max(4, candidateCount));
    }

    private static CombatTargetSlotEntry GetOrBuildCombatTargetSlotEntry(
        CombatTargetSlotKey key,
        FixVector2 targetPoint,
        Fix64 standOff,
        Fix64 minimumStandOff,
        Fix64 ringSpacing,
        int ringCount,
        int candidateCount,
        int targetX,
        int targetY,
        Fix64 requiredClearance)
    {
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long lookupStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool cacheHit = CombatTargetSlotCache.TryGetValue(key, out CombatTargetSlotEntry cached)
                        && cached?.Points != null;
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachSlotCacheLookup,
                Stopwatch.GetTimestamp() - lookupStartTicks);
        }
        if (cacheHit)
        {
            _perf.CombatApproachSlotCacheHits++;
            cached.LastUsedFrame = GetFrameCount();
            return cached;
        }

        _perf.CombatApproachSlotCacheBuilds++;
        long buildStartTicks = MainThreadFrameProfiler.LoggingEnabled
            ? Stopwatch.GetTimestamp()
            : 0L;
        List<FixVector2> points = new List<FixVector2>(Mathf.Max(4, ringCount * candidateCount));
        List<FixVector2> candidateDirectionsNormalized = new List<FixVector2>(points.Capacity);
        List<Fix64> targetDistanceErrors = new List<Fix64>(points.Capacity);
        List<int> cellX = new List<int>(points.Capacity);
        List<int> cellY = new List<int>(points.Capacity);
        List<int> islandIds = new List<int>(points.Capacity);
        int rejectedOutside = 0;
        int rejectedBlocked = 0;
        int rejectedClearance = 0;
        int rejectedNoLos = 0;
        bool targetLineCellValid = _world.IsWalkable(targetX, targetY);
        long generateStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        long clearanceTicks = 0L;
        long lineOfSightTicks = 0L;
        long deduplicateTicks = 0L;

        FixVector2 targetPointFixed = targetPoint;
        Fix64 spacing = Fix64.Max(Fix64.FromRaw(205), ringSpacing);
        Fix64 maximumRadius = Fix64.Max(Fix64.FromRaw(205), standOff);
        Fix64 minimumRadius = Fix64.Clamp(minimumStandOff, Fix64.FromRaw(205), maximumRadius);
        int inwardSteps = checked((int)(long)Fix64.Ceiling(Fix64.Max(Fix64.Zero, standOff - minimumRadius) / spacing));
        int generatedRingCount = Mathf.Max(Mathf.Max(1, ringCount), inwardSteps + 1);
        for (int ring = 0; ring < generatedRingCount; ring++)
        {
            Fix64 radius = Fix64.Max(minimumRadius, standOff - (Fix64)ring * spacing);
            for (int i = 0; i < Mathf.Max(4, candidateCount); i++)
            {
                int samplesPerRing = Mathf.Max(4, candidateCount);
                FixVector2 direction = ResolveCombatSampleDirection(i, samplesPerRing, halfStep: false);
                FixVector2 candidate = targetPointFixed + direction * radius;
                if (!_world.WorldToGridFixed(candidate, out int x, out int y))
                {
                    rejectedOutside++;
                    continue;
                }

                if (!_world.IsWalkable(x, y))
                {
                    rejectedBlocked++;
                    continue;
                }

                int islandId = ResolveIslandIdForDiagnostics(_world, x, y);
                if (islandId <= 0)
                {
                    rejectedBlocked++;
                    continue;
                }

                FixVector2 worldPoint = candidate;
                long segmentStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                bool clear = IsNavigationPointClearFixed(
                    _world,
                    worldPoint,
                    ResolveNavigationQueryClearanceFixed(_world, requiredClearance),
                    includeRuntimeObstacleOverlay: true);
                if (profile)
                    clearanceTicks += Stopwatch.GetTimestamp() - segmentStartTicks;
                if (!clear)
                {
                    rejectedClearance++;
                    continue;
                }

                bool lineOfSightPass = true;
                if (targetLineCellValid
                    && ResolveIslandIdForDiagnostics(_world, targetX, targetY) == islandId)
                {
                    segmentStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                    lineOfSightPass = HasSoftCostTolerantGridLineOfSight(
                        _world,
                        x,
                        y,
                        targetX,
                        targetY,
                        maxAllowedCost: 15);
                    if (profile)
                        lineOfSightTicks += Stopwatch.GetTimestamp() - segmentStartTicks;
                }
                if (!lineOfSightPass)
                {
                    rejectedNoLos++;
                    continue;
                }

                bool duplicate = false;
                segmentStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                for (int existing = 0; existing < points.Count; existing++)
                {
                    if (points[existing].x.RawValue == worldPoint.x.RawValue
                        && points[existing].y.RawValue == worldPoint.y.RawValue)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (profile)
                    deduplicateTicks += Stopwatch.GetTimestamp() - segmentStartTicks;
                if (duplicate)
                    continue;

                points.Add(worldPoint);
                candidateDirectionsNormalized.Add((worldPoint - targetPointFixed).GetNormalized());
                targetDistanceErrors.Add(Fix64.Abs(FixVector2.Distance(worldPoint, targetPointFixed) - standOff));
                cellX.Add(x);
                cellY.Add(y);
                islandIds.Add(islandId);
            }
        }

        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachSlotGenerate,
                Stopwatch.GetTimestamp() - generateStartTicks - clearanceTicks - lineOfSightTicks - deduplicateTicks);
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowCombatApproachSlotClearance, clearanceTicks);
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowCombatApproachSlotLineOfSight, lineOfSightTicks);
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowCombatApproachSlotDeduplicate, deduplicateTicks);
        }

        CombatTargetSlotEntry entry = new CombatTargetSlotEntry
        {
            TargetPoint = targetPointFixed,
            Points = points.ToArray(),
            CandidateDirectionsNormalized = candidateDirectionsNormalized.ToArray(),
            TargetDistanceErrors = targetDistanceErrors.ToArray(),
            CellX = cellX.ToArray(),
            CellY = cellY.ToArray(),
            IslandIds = islandIds.ToArray(),
            LastUsedFrame = GetFrameCount(),
            BuildSummary =
                $"built={points.Count} rings={generatedRingCount} radius=[{minimumRadius:F3},{standOff:F3}] " +
                $"rejectedOutside={rejectedOutside} rejectedBlocked={rejectedBlocked} rejectedClearance={rejectedClearance} rejectedNoLos={rejectedNoLos}"
        };
        CombatTargetSlotCache[key] = entry;
        if (MainThreadFrameProfiler.LoggingEnabled)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachSlotCacheBuild,
                Stopwatch.GetTimestamp() - buildStartTicks);
        }
        return entry;
    }

    private static void LogReachableGoalTargetSnapDiagnostics(
        NavigationWorld world,
        IEntityContext self,
        Vector3 desiredGoal,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int resultX,
        int resultY,
        float distance,
        bool fullIsland)
    {
        if (!IsMovementDiagnosticsEnabled())
            return;
        if (world == null || self == null)
            return;

        IEntityContext target = self.TargetComp?.CurrentTarget;
        if (target == null)
            return;

        int desiredIsland = ResolveIslandIdForDiagnostics(world, goalX, goalY);
        bool targetInGrid = world.WorldToGrid(target.Position, out int targetX, out int targetY);
        int targetIsland = targetInGrid ? ResolveIslandIdForDiagnostics(world, targetX, targetY) : -1;
        if (desiredIsland == targetIsland && distance <= Mathf.Max(0.35f, world.CellSize * 1.5f))
            return;

        Vector3 resultWorld = world.GridToWorldCenter(resultX, resultY);
        Debug.LogWarning(
            $"[FlowReachableTargetSnapDiag] self={self.CharacterKey} target={target.CharacterKey} fullIsland={fullIsland} " +
            $"selfPos={self.Position} targetPos={target.Position} desired={desiredGoal} desiredCell=({goalX},{goalY}) desiredIsland={desiredIsland} " +
            $"targetCell={(targetInGrid ? $"({targetX},{targetY})" : "out")} targetIsland={targetIsland} result=({resultX},{resultY}) resultWorld={resultWorld} " +
            $"resultIsland={ResolveIslandIdForDiagnostics(world, resultX, resultY)} snapDistance={distance:F3} start=({startX},{startY}) " +
            $"startIsland={ResolveIslandIdForDiagnostics(world, startX, startY)} worldVersion={world.Version}");
    }

    private static void LogReachableGoalResolution(
        NavigationWorld world,
        IEntityContext self,
        Vector3 desiredGoal,
        int startX,
        int startY,
        int rawGoalX,
        int rawGoalY,
        int resolvedX,
        int resolvedY,
        float distance,
        int radiusCells,
        bool fullIsland)
    {
        if (!GameDebugSettings.IsEnabled(DebugCategory.Move)
            || self == null
            || !GameDebugSettings.ShouldLogMovementForCharacter(self.CharacterKey)
            || world == null)
        {
            return;
        }

        int startIsland = ResolveIslandIdForDiagnostics(world, startX, startY);
        int rawGoalIsland = ResolveIslandIdForDiagnostics(world, rawGoalX, rawGoalY);
        int resolvedIsland = ResolveIslandIdForDiagnostics(world, resolvedX, resolvedY);
        bool changed = rawGoalX != resolvedX || rawGoalY != resolvedY;
        if (!changed && !fullIsland)
            return;

        Vector3 resolvedWorld = world.GridToWorldCenter(resolvedX, resolvedY);
        GameDebugSettings.Log(DebugCategory.Move,
            $"[FlowReachableGoalResolve] key={self.CharacterKey} desired={desiredGoal} start=({startX},{startY}) startIsland={startIsland} " +
            $"raw=({rawGoalX},{rawGoalY}) rawIsland={rawGoalIsland} resolved=({resolvedX},{resolvedY}) resolvedIsland={resolvedIsland} " +
            $"resolvedWorld={resolvedWorld} distance={distance:F3} radiusCells={radiusCells} fullIsland={fullIsland} " +
            $"rawGrid={FormatGridSampleDiagnostics(desiredGoal)} resolvedGrid={FormatGridSampleDiagnostics(resolvedWorld)}");
    }

    public static bool TryGetSteeringVelocityFixed(
        IEntityContext self,
        FixVector2 goalPosition,
        Fix64 maxSpeed,
        out FixVector2 velocity)
    {
        BeginPerfCall();
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long setupStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (self == null)
            throw new InvalidOperationException("TryGetSteeringVelocityFixed failed: self is null.");
        if (maxSpeed < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxSpeed), "Flow steering speed cannot be negative.");
        if (maxSpeed == Fix64.Zero)
        {
            velocity = FixVector2.Zero;
            return true;
        }

        int selfId = ResolveAgentId(self);
        if (!Agents.TryGetValue(selfId, out AgentRuntimeData agent))
        {
            RegisterSyntheticAgent(self);
            agent = Agents[selfId];
        }

        agent.PositionFixed = self.LogicFramePositionFixed();
        agent.Position = ToWorldVector3(agent.PositionFixed);
        agent.RadiusFixed = ResolveCollisionRadiusFixed(self);
        agent.Radius = (float)agent.RadiusFixed;
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowSteeringSetupAgent,
                Stopwatch.GetTimestamp() - setupStartTicks);
        }

        long worldStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (!TryEnsureWorldBuilt(agent.AgentTypeId))
        {
            string message = $"[{self.CharacterKey}] Flow strict fail: world unavailable agentType={agent.AgentTypeId}";
            Debug.LogError(message);
            throw new InvalidOperationException(message);
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowSteeringSetupWorld,
                Stopwatch.GetTimestamp() - worldStartTicks);
        }

        bool requirePreparedSnapshot = RequiresPreparedNavigationSnapshot();
        long occupancyStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        FixVector2 occupiedGoalPosition;
        FixVector2 navigationGoalPosition;
        if (requirePreparedSnapshot)
        {
            if (!agent.NavState.HasPreparedNavigationSnapshot
                || agent.NavState.PreparedNavigationFrame != GetFrameCount()
                || agent.NavState.PreparedInputGoalFixed != goalPosition)
            {
                throw new InvalidOperationException(
                    $"[{self.CharacterKey}] Flow steering requires the current NavigationSync snapshot. " +
                    $"frame={GetFrameCount()} prepared={agent.NavState.PreparedNavigationFrame} hasSnapshot={agent.NavState.HasPreparedNavigationSnapshot} " +
                    $"requestedRaw=({goalPosition.x.RawValue},{goalPosition.y.RawValue}) preparedRaw=({agent.NavState.PreparedInputGoalFixed.x.RawValue},{agent.NavState.PreparedInputGoalFixed.y.RawValue}).");
            }
            occupiedGoalPosition = agent.NavState.PreparedNavigationGoalFixed;
            navigationGoalPosition = agent.NavState.PreparedNavigationGoalFixed;
        }
        else
        {
            bool hasMovingTarget = self.TargetComp?.CurrentTarget != null
                                   && !ReferenceEquals(self.TargetComp.CurrentTarget, self)
                                   && IsNavigationMovingTarget(self.TargetComp.CurrentTarget);
            occupiedGoalPosition = ResolveNavigationGoalOccupancyFixed(self, agent, goalPosition);
            navigationGoalPosition = hasMovingTarget ? goalPosition : occupiedGoalPosition;
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowSteeringSetupOccupancy,
                Stopwatch.GetTimestamp() - occupancyStartTicks);
        }

        if (agent.NavState.HasPendingNavigation)
        {
            int pendingFrame = GetFrameCount();
            velocity = FixVector2.Zero;
            agent.NavState.DesiredVelocity = Vector3.zero;
            agent.NavState.ResolvedVelocity = Vector3.zero;
            agent.NavState.ResolvedVelocityFrame = pendingFrame;
            agent.NavState.CurrentFlowDirection = Vector3.zero;
            agent.NavState.LastSteeringFrame = pendingFrame;
            agent.NavState.LastSteeringGoal = ToWorldVector3(occupiedGoalPosition);
            agent.NavState.LastSteeringDesiredDirection = Vector3.zero;
            agent.NavState.LastSteeringDesiredVelocity = Vector3.zero;
            agent.NavState.LastSteeringBaseVelocity = Vector3.zero;
            agent.NavState.LastSteeringResultPreClamp = Vector3.zero;
            agent.NavState.LastSteeringResult = Vector3.zero;
            agent.NavState.LastSteeringMaxSpeed = (float)maxSpeed;
            agent.NavState.LastSteeringDesiredSource = DesiredDirectionSource.PendingBuild;
            agent.NavState.LastSteeringHasLineOfSight = false;
            agent.NavState.LastFixedFlowFrame = pendingFrame;
            agent.NavState.LastFixedFlowResult = "pending-navigation-path-request";
            agent.NavState.LastFixedFlowVelocity = FixVector2.Zero;
            agent.HasNavigationIntent = false;
            return true;
        }

        long prepareStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (!requirePreparedSnapshot && !TryPrepareNavigationRequestFixedCore(
                self,
                navigationGoalPosition,
                rejectPendingRuntimeDirty: false,
                out string prepareFailureReason))
        {
            agent.NavState.LastFixedFlowResult = $"prepare-failed: {prepareFailureReason}";
            velocity = FixVector2.Zero;
            string reason = prepareFailureReason.StartsWith("start reachability failed", StringComparison.Ordinal)
                ? $"start blocked: {prepareFailureReason}"
                : $"navigation prepare failed: {prepareFailureReason}";
            string message = $"[{self.CharacterKey}] Flow strict fail: {reason}";
            Debug.LogError(message);
            throw new InvalidOperationException(message);
        }
        long prepareElapsedTicks = profile ? Stopwatch.GetTimestamp() - prepareStartTicks : 0L;
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowSteeringSetupPrepare,
                prepareElapsedTicks);
            if (prepareElapsedTicks >= Stopwatch.Frequency * 2L / 1000L)
            {
                IEntityContext currentTarget = self.TargetComp?.CurrentTarget;
                int startIsland = ResolveIslandIdForDiagnostics(
                    _world,
                    agent.NavState.CurrentCell.x,
                    agent.NavState.CurrentCell.y);
                Debug.LogFormat(
                    LogType.Log,
                    LogOption.NoStacktrace,
                    null,
                    "[FlowSteeringPrepareSlow] frame={0} elapsed={1:F3}ms entity={2}/{3} agentType={4} world={5} startCell=({6},{7}) startIsland={8} " +
                    "target={9}/{10} targetBuilding={11} targetMoving={12} targetMoveComp={13} targetMoveExecutor={14} goalRaw=({15},{16}).",
                    GetFrameCount(),
                    prepareElapsedTicks * 1000.0 / Stopwatch.Frequency,
                    self.LogicEntityId.Value,
                    self.CharacterKey,
                    agent.AgentTypeId,
                    _world.Version,
                    agent.NavState.CurrentCell.x,
                    agent.NavState.CurrentCell.y,
                    startIsland,
                    agent.NavState.StableGoalTargetId,
                    currentTarget?.CharacterKey ?? "null",
                    currentTarget?.IsBuildingEntity ?? false,
                    IsNavigationMovingTarget(currentTarget),
                    currentTarget?.MoveComp?.GetType().FullName ?? "null",
                    currentTarget?.MoveExecutor?.GetType().FullName ?? "null",
                    goalPosition.x.RawValue,
                    goalPosition.y.RawValue);
            }
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowSteeringSetup,
                Stopwatch.GetTimestamp() - setupStartTicks);
        }

        long pathStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        bool preparedPathMatchesCurrentSector = agent.NavState.PathHandle != null
                                                && agent.NavState.PathHandle.SectorIds != null
                                                && agent.NavState.PathHandle.CurrentSectorIndex >= 0
                                                && agent.NavState.PathHandle.CurrentSectorIndex < agent.NavState.PathHandle.SectorIds.Length
                                                && agent.NavState.PathHandle.SectorIds[agent.NavState.PathHandle.CurrentSectorIndex] == agent.NavState.CurrentSectorId;
        if (requirePreparedSnapshot && !preparedPathMatchesCurrentSector)
        {
            throw new InvalidOperationException(
                $"[{self.CharacterKey}] Prepared navigation path does not match the current sector. " +
                $"frame={GetFrameCount()} currentSector={agent.NavState.CurrentSectorId} handle={FormatPathHandle(agent.NavState.PathHandle)}.");
        }
        if (!requirePreparedSnapshot && !TryAdvancePathToCurrentSector(agent, agent.NavState.CurrentSectorId))
        {
            PathHandle invalidHandle = agent.NavState.PathHandle;
            int goalX = invalidHandle?.GoalX ?? -1;
            int goalY = invalidHandle?.GoalY ?? -1;
            ClearCommittedNavigationPath(agent);
            if (goalX < 0
                || goalY < 0
                || !_world.TryGetSectorId(goalX, goalY, out int goalSectorId)
                || !EnsurePathHandle(
                    agent,
                    agent.NavState.CurrentSectorId,
                    goalSectorId,
                    agent.NavState.CurrentCell.x,
                    agent.NavState.CurrentCell.y,
                    goalX,
                    goalY,
                    agent.NavState.StableGoalTargetId != int.MinValue)
                || !TryAdvancePathToCurrentSector(agent, agent.NavState.CurrentSectorId))
            {
                agent.NavState.LastFixedFlowResult =
                    $"path-rebuild-failed current=({agent.NavState.CurrentCell.x},{agent.NavState.CurrentCell.y}) goal=({goalX},{goalY})";
                velocity = FixVector2.Zero;
                return false;
            }
        }
        if (!requirePreparedSnapshot)
        {
            EnqueueSteeringReadDomainFlowTileBuilds(
                agent,
                maxSpeed * LogicFrameRuntime.FixedDeltaTime);
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowSteeringPath,
                Stopwatch.GetTimestamp() - pathStartTicks);
        }

        agent.NavState.HasGoal = true;
        long portalOwnerStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        if (requirePreparedSnapshot)
        {
            int portalOwnerFrame = GetFrameCount();
            if (!FixedPortalOwnerEvaluatedFrameByWorld.TryGetValue(_world.Version, out int evaluatedFrame)
                || evaluatedFrame != portalOwnerFrame)
            {
                throw new InvalidOperationException(
                    $"[{self.CharacterKey}] Flow steering requires the current NavigationSync portal-owner snapshot. " +
                    $"frame={portalOwnerFrame} world={_world.Version} evaluated={evaluatedFrame}.");
            }
        }
        else
        {
            EnsureFixedPortalOwnerFrame();
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowSteeringPortalOwner,
                Stopwatch.GetTimestamp() - portalOwnerStartTicks);
        }
        long velocityStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        velocity = ResolveDeterministicFlowVelocityFixed(self, agent, maxSpeed);
        velocity = ApplyFixedPortalOwner(agent, velocity);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowSteeringVelocity,
                Stopwatch.GetTimestamp() - velocityStartTicks);
        }

        int frame = GetFrameCount();
        Vector3 velocityView = ToWorldVector3(velocity);
        agent.NavState.DesiredVelocity = velocityView;
        agent.NavState.ResolvedVelocity = velocityView;
        agent.NavState.ResolvedVelocityFrame = frame;
        agent.NavState.CurrentFlowDirection = velocity == FixVector2.Zero
            ? Vector3.zero
            : ToWorldVector3(velocity.GetNormalized());
        agent.NavState.LastSteeringFrame = frame;
        agent.NavState.LastSteeringGoal = ToWorldVector3(occupiedGoalPosition);
        agent.NavState.LastSteeringDesiredDirection = agent.NavState.CurrentFlowDirection;
        agent.NavState.LastSteeringDesiredVelocity = velocityView;
        agent.NavState.LastSteeringBaseVelocity = velocityView;
        agent.NavState.LastSteeringResultPreClamp = velocityView;
        agent.NavState.LastSteeringResult = velocityView;
        agent.NavState.LastSteeringMaxSpeed = (float)maxSpeed;
        bool usedDirectLineOfSight = agent.NavState.LastFixedFlowResult.StartsWith("direct-static-clear", StringComparison.Ordinal);
        agent.NavState.LastSteeringDesiredSource = usedDirectLineOfSight
            ? DesiredDirectionSource.LineOfSight
            : DesiredDirectionSource.FlowField;
        agent.NavState.LastSteeringHasLineOfSight = usedDirectLineOfSight;
        agent.HasNavigationIntent = velocity != FixVector2.Zero;
        return true;
    }

    public static bool TryGetSteeringVelocity(IEntityContext self, Vector3 goalPosition, float maxSpeed, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        bool success = TryGetSteeringVelocityFixed(
            self,
            new FixVector2((Fix64)goalPosition.x, (Fix64)goalPosition.z),
            (Fix64)maxSpeed,
            out FixVector2 velocityFixed);
        velocity = ToWorldVector3(velocityFixed);
        return success;
    }

    private static string BuildLineOfSightFailureDiagnostic(Vector3 fromPosition, Vector3 targetPosition, int agentTypeId)
    {
        if (_world == null)
            return "[FlowLineOfSightDiag] world=null";

        bool fromInGrid = _world.WorldToGrid(fromPosition, out int fromX, out int fromY);
        bool targetInGrid = _world.WorldToGrid(targetPosition, out int targetX, out int targetY);
        if (!fromInGrid || !targetInGrid)
        {
            return $"[FlowLineOfSightDiag] fromInGrid={fromInGrid} targetInGrid={targetInGrid} " +
                   $"fromPos={fromPosition} targetPos={targetPosition}";
        }

        bool gridLos = HasGridLineOfSight(_world, fromX, fromY, targetX, targetY);
        return $"[FlowLineOfSightDiag] from=({fromX},{fromY}) target=({targetX},{targetY}) " +
               $"fromPos={fromPosition} targetPos={targetPosition} gridLos={gridLos} " +
               $"cells={BuildLineOfSightCellTrace(_world, fromX, fromY, targetX, targetY)} " +
               $"obstacles={BuildNearbyObstacleDiagnostics(fromPosition, targetPosition, agentTypeId)}";
    }

    private static string BuildLineOfSightCellTrace(NavigationWorld world, int x0, int y0, int x1, int y1)
    {
        if (world == null)
            return "world-null";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
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
        int count = 0;
        const int maxCells = 96;

        while (true)
        {
            if (count > 0)
                builder.Append(" -> ");

            AppendCellDiagnostic(builder, world, cx, cy, previousX, previousY, count == 0);
            count++;

            if (cx == x1 && cy == y1)
                break;
            if (count >= maxCells)
            {
                builder.Append(" -> truncated");
                break;
            }

            int oldX = cx;
            int oldY = cy;
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

            previousX = oldX;
            previousY = oldY;
        }

        builder.Append("]");
        return builder.ToString();
    }

    private static void AppendCellDiagnostic(System.Text.StringBuilder builder, NavigationWorld world, int x, int y, int previousX, int previousY, bool isFirst)
    {
        bool inBounds = x >= 0 && x < world.Width && y >= 0 && y < world.Height;
        builder.Append("(").Append(x).Append(",").Append(y).Append(")");
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
        string mask = world.NeighborTraversalMask != null && world.NeighborTraversalMask.Length == world.Width * world.Height
            ? world.NeighborTraversalMask[index].ToString("X2")
            : "NA";
        bool link = isFirst || CanTraverseNeighborCells(world, previousX, previousY, x, y);
        builder.Append("{walk=").Append(walkable)
            .Append(",base=").Append(baseWalkable)
            .Append(",cost=").Append(cost)
            .Append(",island=").Append(island)
            .Append(",mask=0x").Append(mask)
            .Append(",link=").Append(link)
            .Append("}");
    }

    private static string BuildNearbyObstacleDiagnostics(Vector3 fromPosition, Vector3 targetPosition, int agentTypeId)
    {
        if (_world == null)
            return "world-null";

        Vector3 min = Vector3.Min(fromPosition, targetPosition);
        Vector3 max = Vector3.Max(fromPosition, targetPosition);
        float padding = Mathf.Max(2f, _world.CellSize * 4f);
        Bounds corridor = new Bounds((min + max) * 0.5f, max - min);
        corridor.Expand(new Vector3(padding * 2f, 0f, padding * 2f));

        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append("[boxes=");
        int count = 0;
        foreach (BoxObstacle box in BoxObstacles.Values)
        {
            Bounds bounds = new Bounds(box.Center, box.HalfExtents * 2f);
            Bounds navBounds = ResolveBoxObstacleNavigationBounds(_world, box);
            if (!IntersectsXZ(corridor, navBounds))
                continue;

            if (count > 0)
                builder.Append(" | ");

            AppendBoxObstacleDiagnostic(builder, box, bounds);
            builder.Append(",navGrid=").Append(FormatBoundsGridRange(navBounds));
            count++;
            if (count >= 12)
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
            Bounds bounds = new Bounds(circle.Position, new Vector3(circle.Radius * 2f, 0f, circle.Radius * 2f));
            if (!IntersectsXZ(corridor, bounds))
                continue;

            if (count > 0)
                builder.Append(" | ");

            builder.Append("{id=").Append(circle.Id)
                .Append(",pos=").Append(circle.Position)
                .Append(",r=").Append(circle.Radius.ToString("F3"))
                .Append(",grid=").Append(FormatBoundsGridRange(bounds))
                .Append("}");
            count++;
            if (count >= 12)
            {
                builder.Append(" | truncated");
                break;
            }
        }

        if (count == 0)
            builder.Append("none");

        builder.Append(",agentType=").Append(agentTypeId).Append("]");
        return builder.ToString();
    }

    private static bool IntersectsXZ(Bounds a, Bounds b)
    {
        return a.min.x <= b.max.x && a.max.x >= b.min.x
               && a.min.z <= b.max.z && a.max.z >= b.min.z;
    }

    private static void AppendBoxObstacleDiagnostic(System.Text.StringBuilder builder, BoxObstacle box, Bounds bounds)
    {
        builder.Append("{id=").Append(box.Id)
            .Append(",center=").Append(box.Center)
            .Append(",half=").Append(box.HalfExtents)
            .Append(",grid=").Append(FormatBoundsGridRange(bounds))
            .Append("}");
    }

    private static Bounds ResolveBoxObstacleNavigationBounds(NavigationWorld world, BoxObstacle box)
    {
        if (world == null)
            throw new InvalidOperationException("ResolveBoxObstacleNavigationBounds failed: world is null.");
        if (box == null)
            throw new InvalidOperationException("ResolveBoxObstacleNavigationBounds failed: box is null.");

        float clearance = ResolveNavigationSegmentClearance(world, ResolveAgentTypeRadius(world.AgentTypeId));
        Vector3 halfExtents = box.HalfExtents + new Vector3(clearance, 0f, clearance);
        return new Bounds(box.Center, halfExtents * 2f);
    }

    private static FixVector2 ResolveBoxObstacleNavigationHalfExtentsFixed(NavigationWorld world, BoxObstacle box)
    {
        if (world == null)
            throw new InvalidOperationException("ResolveBoxObstacleNavigationHalfExtentsFixed failed: world is null.");
        if (box == null)
            throw new InvalidOperationException("ResolveBoxObstacleNavigationHalfExtentsFixed failed: box is null.");

        Fix64 clearance = Fix64.Max(Fix64.Zero, world.AgentRadiusFixed - world.CellSizeFixed * Fix64.FromRaw(820));
        return box.HalfExtentsFixed + new FixVector2(clearance, clearance);
    }

    private static FixVector2 ResolveBoxObstacleDirtyHalfExtentsFixed(FixVector2 halfExtents)
    {
        Fix64 maxRadius = Fix64.FromRaw(2048);
        foreach (int agentTypeId in CollectNavigationWorldAgentTypes())
            maxRadius = Fix64.Max(maxRadius, ResolveAgentTypeRadiusFixed(agentTypeId));

        Fix64 clearance = Fix64.Max(Fix64.Zero, maxRadius - Fix64.FromRaw(82));
        return halfExtents + new FixVector2(clearance, clearance);
    }

    private static string FormatBoundsGridRange(Bounds bounds)
    {
        if (_world == null)
            return "world-null";

        int minX = Mathf.Clamp(Mathf.FloorToInt((bounds.min.x - _world.Origin.x) / _world.CellSize), 0, _world.Width - 1);
        int maxX = Mathf.Clamp(Mathf.FloorToInt((bounds.max.x - _world.Origin.x) / _world.CellSize), 0, _world.Width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt((bounds.min.z - _world.Origin.z) / _world.CellSize), 0, _world.Height - 1);
        int maxY = Mathf.Clamp(Mathf.FloorToInt((bounds.max.z - _world.Origin.z) / _world.CellSize), 0, _world.Height - 1);
        return $"({minX},{minY})-({maxX},{maxY})";
    }

    private static bool ShouldLogSuccessfulMoveDiagnostic(AgentRuntimeData agent, IEntityContext self)
    {
        if (!GameDebugSettings.IsEnabled(DebugCategory.Move))
            return false;
        if (agent == null || self == null)
            return false;
        if (!GameDebugSettings.ShouldLogMovementForCharacter(self.CharacterKey))
            return false;

        int frame = GetFrameCount();
        if (agent.NavState.LastSuccessfulMoveDiagnosticFrame >= 0
            && frame - agent.NavState.LastSuccessfulMoveDiagnosticFrame < FlowSuccessfulMoveDiagnosticCooldownFrames)
        {
            return false;
        }

        if (_successfulMoveDiagnosticFrame != frame)
        {
            _successfulMoveDiagnosticFrame = frame;
            _successfulMoveDiagnosticCountThisFrame = 0;
        }

        if (_successfulMoveDiagnosticCountThisFrame >= MaxSuccessfulMoveDiagnosticsPerFrame)
            return false;

        _successfulMoveDiagnosticCountThisFrame++;
        agent.NavState.LastSuccessfulMoveDiagnosticFrame = frame;
        return true;
    }

    private static bool TryConsumeSuccessfulMoveDiagnosticBudget()
    {
        if (!GameDebugSettings.IsEnabled(DebugCategory.Move))
            return false;

        int frame = GetFrameCount();
        if (_successfulMoveDiagnosticFrame != frame)
        {
            _successfulMoveDiagnosticFrame = frame;
            _successfulMoveDiagnosticCountThisFrame = 0;
        }

        if (_successfulMoveDiagnosticCountThisFrame >= MaxSuccessfulMoveDiagnosticsPerFrame)
            return false;

        _successfulMoveDiagnosticCountThisFrame++;
        return true;
    }

    private static bool TryConsumeHeavySteeringDiagnosticBudget()
    {
        if (!GameDebugSettings.IsEnabled(DebugCategory.Move))
            return false;

        int frame = GetFrameCount();
        if (_heavySteeringDiagnosticFrame != frame)
        {
            _heavySteeringDiagnosticFrame = frame;
            _heavySteeringDiagnosticCountThisFrame = 0;
        }

        if (_heavySteeringDiagnosticCountThisFrame >= MaxHeavySteeringDiagnosticsPerFrame)
            return false;

        _heavySteeringDiagnosticCountThisFrame++;
        return true;
    }

    private static bool TryConsumeNavigationStuckDiagnosticBudget()
    {
        int frame = GetFrameCount();
        if (_navigationStuckDiagnosticFrame != frame)
        {
            _navigationStuckDiagnosticFrame = frame;
            _navigationStuckDiagnosticCountThisFrame = 0;
        }

        if (_navigationStuckDiagnosticCountThisFrame >= MaxNavigationStuckDiagnosticsPerFrame)
            return false;

        _navigationStuckDiagnosticCountThisFrame++;
        return true;
    }

}
