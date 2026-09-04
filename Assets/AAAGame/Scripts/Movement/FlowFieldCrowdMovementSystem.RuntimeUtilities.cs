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
    public static void DrawGizmos()
    {
        if (!GameDebugSettings.IsEnabled(DebugCategory.Move))
            return;

        if (!Config.DrawNavigationDebug || _world == null)
            return;

        DrawSectorDebug();
        DrawPortalDebug();
        if (Config.DrawFlowFieldDebug)
            DrawFlowTileDebug();
        DrawAgentNavigationDebug();
    }

    private static int GetFrameCount()
    {
        if (_hasTestTimeOverride)
            return _testFrameCount;
        if (LogicFrameRuntime.IsTimelineRunning)
            return checked((int)LogicFrameRuntime.CurrentFrame);
        return 0;
    }

    public static int GetCurrentNavigationFrame()
    {
        return GetFrameCount();
    }

    private static void BeginNavigationWorkBudget(int operationQuota)
    {
        if (_navigationWorkBudgetActive)
            throw new InvalidOperationException("BeginNavigationWorkBudget failed: a navigation work budget is already active.");
        if (operationQuota <= 0)
            throw new ArgumentOutOfRangeException(nameof(operationQuota), operationQuota, "Navigation operation quota must be positive.");

        _remainingNavigationWorkOperations = operationQuota;
        _navigationWorkBudgetActive = true;
    }

    private static void EndNavigationWorkBudget()
    {
        if (!_navigationWorkBudgetActive)
            throw new InvalidOperationException("EndNavigationWorkBudget failed: no navigation work budget is active.");

        _remainingNavigationWorkOperations = 0;
        _navigationWorkBudgetActive = false;
    }

    private static void ExhaustNavigationWorkBudget()
    {
        EnsureNavigationWorkBudgetActive();
        _remainingNavigationWorkOperations = 0;
    }

    private static bool IsBudgetExpired(long deadlineTicks, int workCursor)
    {
        _ = deadlineTicks;
        _ = workCursor;
        EnsureNavigationWorkBudgetActive();
        _remainingNavigationWorkOperations--;
        return _remainingNavigationWorkOperations <= 0;
    }

    private static bool IsDeadlineExpired(long deadlineTicks)
    {
        _ = deadlineTicks;
        return IsNavigationWorkBudgetExhausted();
    }

    private static bool IsNavigationWorkBudgetExhausted()
    {
        EnsureNavigationWorkBudgetActive();
        return _remainingNavigationWorkOperations <= 0;
    }

    private static void EnsureNavigationWorkBudgetActive()
    {
        if (!_navigationWorkBudgetActive)
            throw new InvalidOperationException("Navigation work budget is not active.");
    }

    private static bool IsMovementDiagnosticsEnabled()
    {
        if (_hasTestTimeOverride)
            return true;

        int frame = Time.frameCount;
        if (_lastDiagnosticsEnabledFrame == frame)
            return _diagnosticsEnabledForFrame;

        _lastDiagnosticsEnabledFrame = frame;
        _diagnosticsEnabledForFrame = GameDebugSettings.IsEnabled(DebugCategory.Move);
        return _diagnosticsEnabledForFrame;
    }

    private static long GetDiagnosticTimestamp()
    {
        return IsMovementDiagnosticsEnabled() ? Stopwatch.GetTimestamp() : 0L;
    }

    private static void BeginPerfCall()
    {
        int frame = GetFrameCount();
        if (!IsMovementDiagnosticsEnabled())
        {
            if (_lastMaintenanceFrame != frame)
            {
                _lastMaintenanceFrame = frame;
                TrimMovingTargetAnchors();
                TrimCombatTargetSlotCache();
                _perf = new FlowPerfAccumulator { Frame = frame };
#if UNITY_EDITOR
                ResetEditorHierarchyStartConnectorDiagnostics();
#endif
            }
            _perfInitialized = false;
            return;
        }

        long nowTicks = Stopwatch.GetTimestamp();
        if (!_perfInitialized)
        {
            _perf = new FlowPerfAccumulator { Frame = frame, FrameStartTicks = nowTicks };
#if UNITY_EDITOR
            ResetEditorHierarchyStartConnectorDiagnostics();
#endif
            _perfInitialized = true;
            return;
        }

        if (_perf.Frame == frame)
            return;

        _perf.FrameTicks = nowTicks - _perf.FrameStartTicks;
#if UNITY_EDITOR
        CaptureEditorFlowPerfTickSnapshot();
#endif
        FlushPerfIfNeeded();
        TrimMovingTargetAnchors();
        TrimCombatTargetSlotCache();
        _perf = new FlowPerfAccumulator { Frame = frame, FrameStartTicks = nowTicks };
    }

    private static void FlushPerfIfNeeded()
    {
        if (!IsMovementDiagnosticsEnabled())
            return;
        if (!_perfInitialized)
            return;

        TrackAndLogFlowTileQueueTrace();

        bool shouldAlwaysLog = _perf.WorldBuilds > 0
                               || _perf.RuntimeDirtyApplications > 0
                               || _perf.GoalProjectionIndexBuildStarts > 0
                               || _perf.GoalProjectionIndexScannedCells > 0;

        long diagnosticTicks = _perf.NavigationConstraintTicks
                               + _perf.AgentUpdateTicks
                               + _perf.ManagerConfigTicks
                               + _perf.ManagerSourceGateTicks
                               + _perf.WorldBuildQueueTicks
                               + _perf.RuntimeRebuildQueueTicks
                               + _perf.FlowTileQueueTicks;
        bool slowFrameOnly = _perf.FrameTicks >= Stopwatch.Frequency * 30 / 1000
                             && diagnosticTicks < FlowPerfLogThresholdTicks
                             && _perf.Frame - _lastSlowFrameOnlyPerfLogFrame >= 30;
        if (diagnosticTicks < FlowPerfLogThresholdTicks && !shouldAlwaysLog && !slowFrameOnly)
            return;
        if (slowFrameOnly)
            _lastSlowFrameOnlyPerfLogFrame = _perf.Frame;

        double frameMs = TicksToMs(_perf.FrameTicks);
        double trackedMs = TicksToMs(diagnosticTicks);
        double untrackedMs = Math.Max(0.0, frameMs - trackedMs);

        Debug.LogFormat(
            LogType.Log,
            LogOption.NoStacktrace,
            null,
            $"[FlowPerf] frame={_perf.Frame} frameDt={frameMs:F3}ms tracked={trackedMs:F3}ms untracked={untrackedMs:F3}ms worldBuilds={_perf.WorldBuilds} pathBuilds={_perf.PathBuilds} tileBuilds=0 portalAccessIntegrations=0 sharedGoalIntegrations=0 " +
            $"pathDetail(startPortal={TicksToMs(_perf.PathStartPortalCheckTicks):F3}ms,firstCross={TicksToMs(_perf.PathFirstCrossingTicks):F3}ms,checks={_perf.PathStartPortalChecks},candidates={_perf.PathStartPortalCandidates},startPortalHits={_perf.PathStartPortalCacheHits},startPortalMisses={_perf.PathStartPortalCacheMisses},firstCrossHits={_perf.PathFirstCrossingCacheHits},firstCrossMisses={_perf.PathFirstCrossingCacheMisses}) " +
            $"successDiag={TicksToMs(_perf.SuccessfulMoveDiagnosticTicks):F3}ms " +
            $"constraint(total={TicksToMs(_perf.NavigationConstraintTicks):F3}ms,calls={_perf.NavigationConstraintCalls},blocked={_perf.NavigationConstraintBlocked},world={TicksToMs(_perf.NavigationConstraintWorldTicks):F3}ms,direct={TicksToMs(_perf.NavigationConstraintDirectTicks):F3}ms,candidate={TicksToMs(_perf.NavigationConstraintCandidateTicks):F3}ms,boundary={TicksToMs(_perf.NavigationConstraintBoundaryTicks):F3}ms,prefix={TicksToMs(_perf.NavigationConstraintPrefixTicks):F3}ms,recovery={TicksToMs(_perf.NavigationConstraintRecoveryTicks):F3}ms) " +
            $"manager(config={TicksToMs(_perf.ManagerConfigTicks):F3}ms,sourceGate={TicksToMs(_perf.ManagerSourceGateTicks):F3}ms) " +
            $"queues(world={TicksToMs(_perf.WorldBuildQueueTicks):F3}ms,runtime={TicksToMs(_perf.RuntimeRebuildQueueTicks):F3}ms,tile={TicksToMs(_perf.FlowTileQueueTicks):F3}ms," +
            $"tileActive={TicksToMs(_perf.FlowTileActiveEnqueueTicks):F3}ms,tilePrune={TicksToMs(_perf.FlowTilePruneTicks):F3}ms,tileRefs={TicksToMs(_perf.FlowTileReferenceTicks):F3}ms," +
            $"sharedActive={TicksToMs(_perf.SharedGoalActiveEnqueueTicks):F3}ms,sharedPrune={TicksToMs(_perf.SharedGoalPruneTicks):F3}ms,sharedProcess={TicksToMs(_perf.SharedGoalProcessTicks):F3}ms,sharedPortalNodes={_perf.SharedGoalPortalGraphNodeExpansions},sharedPortalIncoming={_perf.SharedGoalPortalGraphIncomingTransitionScans},sharedPortalHits={_perf.SharedGoalPortalGraphIncomingTransitionHits},refCalls={_perf.FlowTileReferenceRefreshCalls},refKeys={_perf.FlowTileReferenceKeyScans}) " +
            $"pathRequests(groups={_perf.NavigationPathRequestGroups},operations={_perf.NavigationPathRequestOperations},searchSlices={_perf.NavigationPathSearchCommandSlices}/{_perf.NavigationPathSearchCommandOperations},routeSlices={_perf.NavigationPathRouteSlices}/{_perf.NavigationPathRouteSliceOperations},commits={_perf.NavigationPathRequestCommits},sourceCommits={_perf.NavigationPathSourceCommits},pendingGroups={PendingNavigationPathRequests.Count}) " +
            $"policyShift(calls={_perf.NavigationPathPolicyShiftCalls},costEntries={_perf.NavigationPathPolicyShiftCostEntries},openEntries={_perf.NavigationPathPolicyShiftOpenEntries}) " +
            $"pathPortal(nodes={_perf.PathPortalGraphNodeExpansions},outgoing={_perf.PathPortalGraphOutgoingTransitionScans},hits={_perf.PathPortalGraphOutgoingTransitionHits}) " +
            $"corridorPolicy(ms={TicksToMs(_perf.SectorCorridorPolicyTicks):F3},queries={_perf.SectorCorridorPolicyQueries}) " +
            $"agentUpdate={TicksToMs(_perf.AgentUpdateTicks):F3}ms " +
            $"sectorPath(searches={_perf.SectorPathSearches},cacheHits={_perf.SectorPathCacheHits}) " +
            $"portalGraphMergeHits={_perf.PortalGraphMergeHits} " +
            $"tileQueueOps(enq={_perf.FlowTileQueueEnqueued},promote=0,prune={_perf.FlowTileQueuePruned},requiredCommits={_perf.RequiredFlowTileCommits}) " +
            $"tileCache(hits={_perf.TileCacheHits},misses={_perf.TileCacheMisses}) " +
            $"pending(sharedGoal={_perf.PathPendingSharedGoal},tile={_perf.TilePendingBuild}) " +
            $"stableGoal(raw={_perf.StableGoalRaw},reachabilityReuse={_perf.StableGoalReachabilityReuse},reuse={_perf.StableGoalReuse},initial={_perf.StableGoalRefreshInitial},cell={_perf.StableGoalRefreshCellDelta}) " +
            $"pathReasons(noHandle={_perf.PathBuildNoHandle},worldMismatch={_perf.PathBuildWorldMismatch},goalSectorMismatch={_perf.PathBuildGoalSectorMismatch},goalCellMismatch={_perf.PathBuildGoalCellMismatch},invalid={_perf.PathBuildInvalidHandle}) " +
            $"runtimeDirty(apply={_perf.RuntimeDirtyApplications},sectors={_perf.RuntimeDirtySectorCount}) " +
            $"goalProjectionIndex(buildStarts={_perf.GoalProjectionIndexBuildStarts},scannedCells={_perf.GoalProjectionIndexScannedCells},fullScans={_perf.GoalProjectionIndexFullScans},publishes={_perf.GoalProjectionIndexPublishes})");
    }

    private static void TrackAndLogFlowTileQueueTrace()
    {
        if (!GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            _tilePendingWithoutBuildFrames = 0;
            return;
        }

        bool pendingWithoutBuild = _perf.TilePendingBuild > 0
                                   && FlowTileBuildQueue.Count > 0;
        if (pendingWithoutBuild)
            _tilePendingWithoutBuildFrames++;
        else
            _tilePendingWithoutBuildFrames = 0;

        if (_tilePendingWithoutBuildFrames < FlowTileQueueTraceThresholdFrames)
            return;
        if (_lastFlowTileQueueTraceFrame >= 0
            && _perf.Frame - _lastFlowTileQueueTraceFrame < FlowTileQueueTraceCooldownFrames)
        {
            return;
        }

        _lastFlowTileQueueTraceFrame = _perf.Frame;
        LogWarningNoStacktrace(
            $"[FlowTileQueueTrace] frame={_perf.Frame} frames={_tilePendingWithoutBuildFrames} " +
            $"pendingTile={_perf.TilePendingBuild} tileBuilds=0 queue={FlowTileBuildQueue.Count} " +
            $"pendingSet={PendingFlowTileBuildJobs.Count} tileQueueTicks={TicksToMs(_perf.FlowTileQueueTicks):F3}ms " +
            $"ops(enq={_perf.FlowTileQueueEnqueued},promote=0,prune={_perf.FlowTileQueuePruned}) " +
            $"tileCache(hits={_perf.TileCacheHits},misses={_perf.TileCacheMisses},count={FlowTileCache.Count}) " +
            $"pathDetail(startPortal={TicksToMs(_perf.PathStartPortalCheckTicks):F3}ms,firstCross={TicksToMs(_perf.PathFirstCrossingTicks):F3}ms,checks={_perf.PathStartPortalChecks},candidates={_perf.PathStartPortalCandidates},startPortalHits={_perf.PathStartPortalCacheHits},startPortalMisses={_perf.PathStartPortalCacheMisses},firstCrossHits={_perf.PathFirstCrossingCacheHits},firstCrossMisses={_perf.PathFirstCrossingCacheMisses}) " +
            $"pathReasons(noHandle={_perf.PathBuildNoHandle},worldMismatch={_perf.PathBuildWorldMismatch},goalSectorMismatch={_perf.PathBuildGoalSectorMismatch},goalCellMismatch={_perf.PathBuildGoalCellMismatch},invalid={_perf.PathBuildInvalidHandle}) " +
            $"jobs={BuildPendingFlowTileJobDiagnostics()}");
    }

    private static void LogAgentOverlapDiagnostics(int frame)
    {
        if (Agents.Count < 2)
            return;
        if (_world == null)
            return;
        if (_lastOverlapDiagnosticsFrame >= 0 && frame - _lastOverlapDiagnosticsFrame < 20)
            return;

        _lastOverlapDiagnosticsFrame = frame;
        const int maxLoggedPairs = 10;
        OverlapDiagnosticRecord[] selected = new OverlapDiagnosticRecord[maxLoggedPairs];
        int overlapPairs = 0;
        int activeOverlapPairs = 0;
        Dictionary<string, int> characterPairCounts = new Dictionary<string, int>(16);
        foreach (AgentRuntimeData self in Agents.Values)
        {
            if (self.IgnoreAgentCollision)
                continue;

            List<AgentRuntimeData> nearbyAgents = CollectNearbyDynamicNeighbors(
                self.Position,
                self.Id,
                Mathf.Max(self.Radius * 4f, _world.CellSize * 2f));
            for (int nearbyIndex = 0; nearbyIndex < nearbyAgents.Count; nearbyIndex++)
            {
                AgentRuntimeData other = nearbyAgents[nearbyIndex];
                if (other.Id <= self.Id || other.IgnoreAgentCollision)
                    continue;

                Vector3 delta = other.Position - self.Position;
                delta.y = 0f;
                float distance = delta.magnitude;
                float combinedRadius = self.Radius + other.Radius;
                float penetration = combinedRadius - distance;
                if (penetration <= Mathf.Max(0.02f, combinedRadius * 0.08f))
                    continue;

                overlapPairs++;
                bool activePair = self.HasNavigationIntent || other.HasNavigationIntent;
                if (activePair)
                    activeOverlapPairs++;

                AccumulateOverlapCharacterPair(characterPairCounts, self, other);
                float priority = penetration + (activePair ? 1000f : 0f);
                TryInsertOverlapDiagnostic(selected, new OverlapDiagnosticRecord(self, other, penetration, distance, combinedRadius, priority));
            }
        }

        if (overlapPairs == 0)
            return;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(2048);
        int loggedPairs = 0;
        for (int i = 0; i < selected.Length; i++)
        {
            OverlapDiagnosticRecord record = selected[i];
            if (record.Self == null || record.Other == null)
                continue;

            builder.Append("[FlowAgentOverlap] frame=");
            builder.Append(frame);
            builder.Append(" penetration=");
            builder.Append(record.Penetration.ToString("F3"));
            builder.Append(" distance=");
            builder.Append(record.Distance.ToString("F3"));
            builder.Append(" combinedRadius=");
            builder.Append(record.CombinedRadius.ToString("F3"));
            builder.Append(" self={");
            AppendAgentAvoidanceDiagnostics(builder, record.Self);
            builder.Append("} other={");
            AppendAgentAvoidanceDiagnostics(builder, record.Other);
            builder.Append("}\n");
            loggedPairs++;
        }

        builder.Append("[FlowAgentOverlapSummary] frame=");
        builder.Append(frame);
        builder.Append(" overlapPairs=");
        builder.Append(overlapPairs);
        builder.Append(" activePairs=");
        builder.Append(activeOverlapPairs);
        builder.Append(" logged=");
        builder.Append(loggedPairs);
        builder.Append(" characterPairs=");
        AppendOverlapCharacterPairCounts(builder, characterPairCounts);
        Debug.LogWarning(builder.ToString());
    }

    private readonly struct OverlapDiagnosticRecord
    {
        public readonly AgentRuntimeData Self;
        public readonly AgentRuntimeData Other;
        public readonly float Penetration;
        public readonly float Distance;
        public readonly float CombinedRadius;
        public readonly float Priority;

        public OverlapDiagnosticRecord(AgentRuntimeData self, AgentRuntimeData other, float penetration, float distance, float combinedRadius, float priority)
        {
            Self = self;
            Other = other;
            Penetration = penetration;
            Distance = distance;
            CombinedRadius = combinedRadius;
            Priority = priority;
        }
    }

    private static void TryInsertOverlapDiagnostic(OverlapDiagnosticRecord[] selected, OverlapDiagnosticRecord candidate)
    {
        for (int i = 0; i < selected.Length; i++)
        {
            if (selected[i].Self != null && candidate.Priority <= selected[i].Priority)
                continue;

            for (int shift = selected.Length - 1; shift > i; shift--)
                selected[shift] = selected[shift - 1];

            selected[i] = candidate;
            return;
        }
    }

    private static void AccumulateOverlapCharacterPair(Dictionary<string, int> counts, AgentRuntimeData self, AgentRuntimeData other)
    {
        string left = string.IsNullOrEmpty(self.CharacterKey) ? "unknown" : self.CharacterKey;
        string right = string.IsNullOrEmpty(other.CharacterKey) ? "unknown" : other.CharacterKey;
        string key = string.CompareOrdinal(left, right) <= 0 ? $"{left}+{right}" : $"{right}+{left}";
        counts.TryGetValue(key, out int count);
        counts[key] = count + 1;
    }

    private static void AppendOverlapCharacterPairCounts(System.Text.StringBuilder builder, Dictionary<string, int> counts)
    {
        builder.Append('[');
        bool first = true;
        foreach (KeyValuePair<string, int> pair in counts)
        {
            if (!first)
                builder.Append(", ");
            first = false;
            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(pair.Value);
        }

        if (first)
            builder.Append("none");
        builder.Append(']');
    }

    private static void AppendAgentAvoidanceDiagnostics(System.Text.StringBuilder builder, AgentRuntimeData agent)
    {
        builder.Append("key=");
        builder.Append(agent.CharacterKey);
        builder.Append(" id=");
        builder.Append(agent.Id);
        builder.Append(" pos=");
        builder.Append(agent.Position);
        builder.Append(" radius=");
        builder.Append(agent.Radius.ToString("F3"));
        builder.Append(" side=");
        builder.Append(agent.Side);
        builder.Append(" ignore=");
        builder.Append(agent.IgnoreAgentCollision);
        builder.Append(" intent=");
        builder.Append(agent.HasNavigationIntent);
        builder.Append(" moveMode=");
        builder.Append(agent.NavState.LastMovementMode);
        builder.Append(" moveComp=");
        builder.Append(agent.MoveCompTypeName);
        builder.Append(" entity=");
        builder.Append(agent.EntityTypeName);
        builder.Append(" source=");
        builder.Append(agent.RegistrationSource);
        builder.Append(" synthetic=");
        builder.Append(agent.IsSyntheticRegistration);
        builder.Append(" desired=");
        builder.Append(agent.NavState.DesiredVelocity);
        builder.Append(" resolved=");
        builder.Append(agent.NavState.ResolvedVelocity);
        builder.Append(" resolvedFrame=");
        builder.Append(agent.NavState.ResolvedVelocityFrame);
        builder.Append(" desiredSrc=");
        builder.Append(agent.NavState.LastSteeringDesiredSource);
        builder.Append(" fixedResult=");
        builder.Append(agent.NavState.LastFixedFlowResult);
        builder.Append(" tileState=");
        builder.Append(BuildAgentCurrentTileStateDiagnostic(agent));
    }

    private static double TicksToMs(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static void RegisterSyntheticAgent(IEntityContext self)
    {
        int agentId = ResolveAgentId(self);
        if (Agents.ContainsKey(agentId))
            throw new InvalidOperationException($"RegisterSyntheticAgent failed: agent {agentId} is already registered.");

        var agent = new AgentRuntimeData
        {
            Id = agentId,
            CharacterKey = self.CharacterKey,
            Position = self.LogicFramePosition(),
            PositionFixed = self.LogicFramePositionFixed(),
            Radius = ResolveCollisionRadius(self),
            RadiusFixed = ResolveCollisionRadiusFixed(self),
            RegisteredRadius = ResolveCollisionRadius(self),
            Side = self.Side,
            AgentTypeId = self is ILogicFrameEntity logicEntity ? logicEntity.NavigationAgentTypeId : 0,
            EntityTypeName = self.GetType().Name,
            MoveCompTypeName = self.MoveComp?.GetType().Name ?? "null",
            RegistrationSource = "RegisterSyntheticAgent",
            IsSyntheticRegistration = true
        };
        agent.NavState.AgentId = agentId;
        agent.NavState.LastMovementMode = self.MoveExecutor?.MovementMode ?? MovementMode.Normal;
        UpdateAgentNavigationIntent(agent, self.MoveComp);
        Agents.Add(agentId, agent);
        AddOrderedAgentId(agentId);
    }

    private static void AddOrderedAgentId(int agentId)
    {
        int index = OrderedAgentIds.BinarySearch(agentId);
        if (index >= 0)
            throw new InvalidOperationException($"AddOrderedAgentId failed: duplicate agent id {agentId}.");
        OrderedAgentIds.Insert(~index, agentId);
    }

    private static void DrawSectorDebug()
    {
        if (_world.Sectors == null)
            return;

        Gizmos.color = new Color(0.15f, 0.7f, 0.9f, 0.7f);
        for (int i = 0; i < _world.Sectors.Length; i++)
        {
            SectorData sector = _world.Sectors[i];
            Vector3 size = new Vector3(sector.Width * _world.CellSize, 0.02f, sector.Height * _world.CellSize);
            Gizmos.DrawWireCube(
                _world.Origin + new Vector3((sector.StartX + sector.Width * 0.5f) * _world.CellSize, 0f, (sector.StartY + sector.Height * 0.5f) * _world.CellSize),
                size);
        }
    }

    private static void DrawPortalDebug()
    {
        if (_world.Portals == null)
            return;

        for (int i = 0; i < _world.Portals.Length; i++)
        {
            PortalData portal = _world.Portals[i];
            Gizmos.color = portal.IsNarrow ? Color.red : Color.green;
            Gizmos.DrawSphere(portal.WorldCenter + Vector3.up * 0.05f, _world.CellSize * 0.18f);
            DrawPortalCells(portal.CellsA, portal.IsNarrow ? new Color(1f, 0.35f, 0.35f, 0.35f) : new Color(0.35f, 1f, 0.35f, 0.2f), 0.015f);
            DrawPortalCells(portal.CellsB, portal.IsNarrow ? new Color(1f, 0.35f, 0.35f, 0.35f) : new Color(0.35f, 1f, 0.35f, 0.2f), 0.015f);
        }
    }

    private static void DrawPortalCells(Vector2Int[] cells, Color color, float yOffset)
    {
        if (cells == null)
            return;

        Gizmos.color = color;
        Vector3 size = new Vector3(_world.CellSize * 0.9f, 0.01f, _world.CellSize * 0.9f);
        for (int i = 0; i < cells.Length; i++)
        {
            Vector3 center = _world.GridToWorldCenter(cells[i].x, cells[i].y);
            center.y += yOffset;
            Gizmos.DrawCube(center, size);
        }
    }

    private static void DrawFlowTileDebug()
    {
        foreach (KeyValuePair<FlowTileCacheKey, FlowTileCacheEntry> pair in FlowTileCache)
        {
            FlowTileCacheEntry tile = pair.Value;
            DrawTileIntegrationHeat(tile);
            DrawTileFlowArrows(tile);
        }
    }

    private static void DrawTileIntegrationHeat(FlowTileCacheEntry tile)
    {
        float[] integration = tile?.DebugIntegrationPayload?.Values;
        if (integration == null)
            return;

        float maxReachableCost = 0f;
        for (int i = 0; i < integration.Length; i++)
        {
            float cost = integration[i];
            if (!float.IsPositiveInfinity(cost) && cost > maxReachableCost)
                maxReachableCost = cost;
        }

        float normalization = Mathf.Max(maxReachableCost, 0.001f);
        Vector3 size = new Vector3(_world.CellSize * 0.88f, 0.006f, _world.CellSize * 0.88f);
        for (int worldY = tile.StartY; worldY < tile.StartY + tile.Height; worldY++)
        {
            for (int worldX = tile.StartX; worldX < tile.StartX + tile.Width; worldX++)
            {
                if (!_world.IsWalkable(worldX, worldY))
                    continue;

                int localIndex = tile.GetLocalIndex(worldX, worldY);
                float cost = integration[localIndex];
                if (float.IsPositiveInfinity(cost))
                    continue;

                float t = Mathf.Clamp01(cost / normalization);
                Gizmos.color = Color.Lerp(new Color(0.1f, 0.9f, 0.25f, 0.1f), new Color(1f, 0.55f, 0.05f, 0.16f), t);
                Vector3 center = _world.GridToWorldCenter(worldX, worldY);
                center.y += 0.01f;
                Gizmos.DrawCube(center, size);
            }
        }
    }

    private static void DrawTileFlowArrows(FlowTileCacheEntry tile)
    {
        if (tile?.FlowFieldValues == null)
            return;

        for (int worldY = tile.StartY; worldY < tile.StartY + tile.Height; worldY++)
        {
            for (int worldX = tile.StartX; worldX < tile.StartX + tile.Width; worldX++)
            {
                if (!_world.IsWalkable(worldX, worldY))
                    continue;

                int localIndex = tile.GetLocalIndex(worldX, worldY);
                Vector2 flow2 = ResolveRuntimeFlowDirection(tile, worldX, worldY, localIndex);
                if (flow2.sqrMagnitude <= 0.0001f)
                    continue;

                Vector3 center = _world.GridToWorldCenter(worldX, worldY) + Vector3.up * 0.035f;
                Vector3 dir = new Vector3(flow2.x, 0f, flow2.y).normalized;
                Gizmos.color = new Color(1f, 0.95f, 0.2f, 0.7f);
                DrawArrow(center, dir, _world.CellSize * 0.32f, _world.CellSize * 0.08f);
            }
        }
    }

    private static void DrawAgentNavigationDebug()
    {
        foreach (KeyValuePair<int, AgentRuntimeData> pair in Agents)
        {
            AgentRuntimeData agent = pair.Value;
            if (!IsAgentInCurrentNavigationWorld(agent))
                continue;

            Vector3 origin = agent.Position + Vector3.up * 0.1f;

            Gizmos.color = agent.IgnoreAgentCollision ? Color.gray : Color.white;
            Gizmos.DrawWireSphere(origin, Mathf.Max(agent.Radius, 0.05f));

            if (_world.IsWalkable(agent.NavState.CurrentCell.x, agent.NavState.CurrentCell.y))
            {
                Gizmos.color = new Color(1f, 1f, 1f, 0.22f);
                Vector3 cellCenter = _world.GridToWorldCenter(agent.NavState.CurrentCell.x, agent.NavState.CurrentCell.y) + Vector3.up * 0.02f;
                Gizmos.DrawCube(cellCenter, new Vector3(_world.CellSize * 0.35f, 0.02f, _world.CellSize * 0.35f));
            }

            if (agent.NavState.CurrentFlowDirection.sqrMagnitude > 0.0001f)
            {
                Gizmos.color = Color.yellow;
                DrawArrow(origin, agent.NavState.CurrentFlowDirection.normalized, _world.CellSize * 0.5f, _world.CellSize * 0.12f);
            }

            if (agent.NavState.DesiredVelocity.sqrMagnitude > 0.0001f)
            {
                Gizmos.color = new Color(0.2f, 0.45f, 1f, 0.85f);
                DrawArrow(origin + Vector3.up * 0.03f, agent.NavState.DesiredVelocity.normalized, _world.CellSize * 0.45f, _world.CellSize * 0.1f);
            }

            if (agent.NavState.ResolvedVelocity.sqrMagnitude > 0.0001f)
            {
                Gizmos.color = new Color(1f, 0.6f, 0.15f, 0.9f);
                DrawArrow(origin + Vector3.up * 0.06f, agent.NavState.ResolvedVelocity.normalized, _world.CellSize * 0.58f, _world.CellSize * 0.13f);
            }

            if (agent.NavState.LastMovementMode != MovementMode.Normal)
            {
                Gizmos.color = agent.NavState.LastMovementMode == MovementMode.Displaced
                    ? new Color(0.35f, 0.9f, 1f, 0.8f)
                    : new Color(1f, 0.2f, 0.2f, 0.8f);
                Gizmos.DrawWireCube(origin + Vector3.up * 0.1f, new Vector3(_world.CellSize * 0.25f, 0.08f, _world.CellSize * 0.25f));
            }

            if (agent.NavState.PathHandle?.PortalIds != null)
            {
                Gizmos.color = new Color(1f, 0.95f, 0.3f, 0.7f);
                ImmutableRouteSequence portals = agent.NavState.PathHandle.PortalIds;
                for (int i = agent.NavState.PathHandle.CurrentSectorIndex; i < portals.Length; i++)
                {
                    if (!TryGetPortalById(_world, portals[i], out PortalData portal))
                        continue;

                    Vector3 center = portal.WorldCenter + Vector3.up * 0.05f;
                    Gizmos.DrawSphere(center, _world.CellSize * 0.1f);
                }
            }
        }
    }

    private static bool TryGetPortalById(NavigationWorld world, int portalId, out PortalData portal)
    {
        portal = null;
        return world != null
               && world.PortalsById != null
               && world.PortalsById.TryGetValue(portalId, out portal);
    }

    private static PortalData GetPortalById(NavigationWorld world, int portalId)
    {
        if (TryGetPortalById(world, portalId, out PortalData portal))
            return portal;

        throw new InvalidOperationException($"GetPortalById failed: portal {portalId} not found.");
    }

    private static void DrawArrow(Vector3 origin, Vector3 direction, float length, float headSize)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Vector3 dir = direction.normalized;
        Vector3 tip = origin + dir * length;
        Gizmos.DrawLine(origin, tip);

        Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
        Gizmos.DrawLine(tip, tip - dir * headSize + right * headSize * 0.5f);
        Gizmos.DrawLine(tip, tip - dir * headSize - right * headSize * 0.5f);
    }

    private static void UpdateAgentNavigationIntent(AgentRuntimeData agent, IMoveComp moveComp)
    {
        if (agent == null)
            throw new InvalidOperationException("UpdateAgentNavigationIntent failed: agent is null.");
        if (moveComp == null)
            return;

        bool hasIntent = false;
        if (moveComp is CharacterMoveComp characterMoveComp)
        {
            hasIntent = characterMoveComp.HasNavigationTarget || characterMoveComp.IsMoving;
        }
        else if (moveComp != null && moveComp.GetType() != typeof(NoMoveComp))
        {
            hasIntent = true;
        }

        agent.HasNavigationIntent = hasIntent;
        if (!hasIntent)
            ClearInactiveNavigationDemand(agent);
    }

    private static void ClearInactiveNavigationDemand(AgentRuntimeData agent)
    {
        AgentNavState nav = agent.NavState;
        nav.HasGoal = false;
        nav.DesiredVelocity = Vector3.zero;
        nav.ResolvedVelocity = Vector3.zero;
        nav.ResolvedVelocityFrame = -1;
        nav.CurrentFlowDirection = Vector3.zero;
        nav.LastFixedFlowFrame = -1;
        nav.LastFixedFlowVelocity = FixVector2.Zero;
        nav.PortalTraversalWorldVersion = -1;
        nav.PortalTraversalSectorId = -1;
        nav.PortalTraversalId = -1;
        nav.PortalTraversalSlotIndex = -1;
        nav.PortalTraversalHasCommittedTileSlot = false;
        ClearStableGoal(agent);
    }

    private static bool CanIncludeInSpatialDiagnostics(AgentRuntimeData agent)
    {
        if (agent == null)
            throw new InvalidOperationException("CanIncludeInSpatialDiagnostics failed: agent is null.");

        if (agent.IgnoreAgentCollision)
            return false;

        if (agent.MoveCompTypeName == nameof(NoMoveComp))
            return false;
        if (agent.NavState.LastMovementMode != MovementMode.Normal)
            return false;

        return true;
    }

    private static long BuildSpatialBucketKey(int cellX, int cellY)
    {
        unchecked
        {
            return ((long)cellX << 32) | (uint)cellY;
        }
    }

    private static void ResolveSpatialBucketCell(Vector3 position, out int cellX, out int cellY)
    {
        if (_world == null)
            throw new InvalidOperationException("ResolveSpatialBucketCell failed: world is null.");

        float inverseCellSize = 1f / ResolveAgentSpatialBucketSize();
        Vector3 local = position - _world.Origin;
        cellX = Mathf.FloorToInt(local.x * inverseCellSize);
        cellY = Mathf.FloorToInt(local.z * inverseCellSize);
    }

    private static float ResolveAgentSpatialBucketSize()
    {
        if (_world == null)
            throw new InvalidOperationException("ResolveAgentSpatialBucketSize failed: world is null.");

        return Mathf.Clamp(
            _world.CellSize * 8f,
            AgentSpatialBucketMinWorldSize,
            AgentSpatialBucketMaxWorldSize);
    }

    private static void SyncRegisteredAgentSpatialState(int frameCount)
    {
        if (_lastAgentRegistrySyncFrame == frameCount)
            return;

        _lastAgentRegistrySyncFrame = frameCount;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        if (entities == null)
            throw new InvalidOperationException("SyncRegisteredAgentSpatialState failed: EntityRegistry.AllEntities is null.");

        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity == null)
                throw new InvalidOperationException($"SyncRegisteredAgentSpatialState failed: entity at index {i} is null.");

            int agentId = ResolveAgentId(entity);
            if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent))
                continue;

            agent.Position = entity.LogicFramePosition();
            agent.PositionFixed = entity.LogicFramePositionFixed();
            agent.RadiusFixed = ResolveCollisionRadiusFixed(entity);
            agent.Radius = (float)agent.RadiusFixed;
            agent.Side = entity.Side;
            agent.MoveCompTypeName = entity.MoveComp?.GetType().Name ?? "null";
            agent.NavState.LastMovementMode = entity.MoveExecutor?.MovementMode ?? MovementMode.Normal;
            UpdateAgentNavigationIntent(agent, entity.MoveComp);
        }
    }

    private static void EnsureAgentSpatialBuckets()
    {
        if (_world == null)
            throw new InvalidOperationException("EnsureAgentSpatialBuckets failed: world is null.");

        int frameCount = GetFrameCount();
        int worldVersion = _world.Version;
        if (_lastAgentSpatialBucketFrame == frameCount && _lastAgentSpatialBucketWorldVersion == worldVersion)
            return;

        SyncRegisteredAgentSpatialState(frameCount);
        _lastAgentSpatialBucketFrame = frameCount;
        _lastAgentSpatialBucketWorldVersion = worldVersion;
        ClearAgentBucketContents(AgentSpatialBuckets);
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            if (!CanIncludeInSpatialDiagnostics(agent))
                continue;

            ResolveSpatialBucketCell(agent.Position, out int cellX, out int cellY);
            long bucketKey = BuildSpatialBucketKey(cellX, cellY);
            if (!AgentSpatialBuckets.TryGetValue(bucketKey, out List<AgentRuntimeData> bucket))
            {
                bucket = new List<AgentRuntimeData>(4);
                AgentSpatialBuckets.Add(bucketKey, bucket);
            }

            bucket.Add(agent);
        }
    }

    private static long ResolveNavigationGoalBucketKey(FixVector2 point)
    {
        Vector3 worldPoint = ToWorldVector3(point);
        ResolveSpatialBucketCell(worldPoint, out int cellX, out int cellY);
        return BuildSpatialBucketKey(cellX, cellY);
    }

    private static void AddNavigationGoalBucketEntry(
        Dictionary<long, List<AgentRuntimeData>> buckets,
        long key,
        AgentRuntimeData agent)
    {
        if (!buckets.TryGetValue(key, out List<AgentRuntimeData> bucket))
        {
            bucket = NavigationGoalBucketListPool.Count > 0
                ? NavigationGoalBucketListPool.Pop()
                : new List<AgentRuntimeData>(4);
            buckets.Add(key, bucket);
        }
        bucket.Add(agent);
    }

    private static void AddNavigationGoalBucketEntryOrdered(
        Dictionary<long, List<AgentRuntimeData>> buckets,
        long key,
        AgentRuntimeData agent,
        int agentId)
    {
        if (agent == null)
            throw new InvalidOperationException("AddNavigationGoalBucketEntryOrdered received a null agent.");
        if (!buckets.TryGetValue(key, out List<AgentRuntimeData> bucket))
        {
            bucket = NavigationGoalBucketListPool.Count > 0
                ? NavigationGoalBucketListPool.Pop()
                : new List<AgentRuntimeData>(4);
            buckets.Add(key, bucket);
        }

        int insertionIndex = bucket.Count;
        for (int i = 0; i < bucket.Count; i++)
        {
            AgentRuntimeData existingAgent = bucket[i]
                ?? throw new InvalidOperationException("AddNavigationGoalBucketEntryOrdered found a null bucket agent.");
            int existingId = existingAgent.Id;
            if (existingId == agentId)
                throw new InvalidOperationException($"Navigation goal bucket already contains agent={agentId}.");
            if (existingId > agentId)
            {
                insertionIndex = i;
                break;
            }
        }
        bucket.Insert(insertionIndex, agent);
    }

    private static void RemoveNavigationGoalBucketEntry(
        Dictionary<long, List<AgentRuntimeData>> buckets,
        long key,
        int agentId)
    {
        if (!buckets.TryGetValue(key, out List<AgentRuntimeData> bucket) || bucket == null)
            throw new InvalidOperationException($"Navigation goal bucket missing for agent removal agent={agentId} key={key}.");
        int foundIndex = -1;
        for (int i = 0; i < bucket.Count; i++)
        {
            AgentRuntimeData agent = bucket[i]
                ?? throw new InvalidOperationException("RemoveNavigationGoalBucketEntry found a null bucket agent.");
            if (agent.Id == agentId)
            {
                foundIndex = i;
                break;
            }
        }
        if (foundIndex < 0)
            throw new InvalidOperationException($"Navigation goal bucket did not contain agent={agentId} key={key}.");
        bucket.RemoveAt(foundIndex);
        if (bucket.Count == 0)
        {
            bucket.Clear();
            NavigationGoalBucketListPool.Push(bucket);
            buckets.Remove(key);
        }
    }

    private static void ResetNavigationGoalOccupancyBuckets()
    {
        foreach (List<AgentRuntimeData> bucket in NavigationGoalPositionBuckets.Values)
        {
            if (bucket == null)
                throw new InvalidOperationException("ResetNavigationGoalOccupancyBuckets found a null position bucket.");
            bucket.Clear();
            NavigationGoalBucketListPool.Push(bucket);
        }
        NavigationGoalPositionBuckets.Clear();
        foreach (List<AgentRuntimeData> bucket in NavigationGoalTargetBuckets.Values)
        {
            if (bucket == null)
                throw new InvalidOperationException("ResetNavigationGoalOccupancyBuckets found a null target bucket.");
            bucket.Clear();
            NavigationGoalBucketListPool.Push(bucket);
        }
        NavigationGoalTargetBuckets.Clear();
        NavigationGoalPositionBucketByAgent.Clear();
        NavigationGoalTargetBucketByAgent.Clear();
    }

    private static void EnsureNavigationGoalOccupancyBuckets()
    {
        if (_world == null)
            throw new InvalidOperationException("EnsureNavigationGoalOccupancyBuckets failed: world is null.");

        int frame = GetFrameCount();
        int worldVersion = _world.Version;
        float bucketGeometrySize = ResolveAgentSpatialBucketSize();
        Vector3 bucketGeometryOrigin = _world.Origin;
        bool geometrySame = _navigationGoalOccupancyBucketGeometryInitialized
                            && _navigationGoalOccupancyBucketGeometrySize == bucketGeometrySize
                            && _navigationGoalOccupancyBucketGeometryOrigin == bucketGeometryOrigin;
        if (!geometrySame)
        {
            _navigationGoalOccupancyBucketGeometryInitialized = true;
            _navigationGoalOccupancyBucketGeometrySize = bucketGeometrySize;
            _navigationGoalOccupancyBucketGeometryOrigin = bucketGeometryOrigin;
            _navigationGoalOccupancyBucketGeometryGeneration = checked(
                _navigationGoalOccupancyBucketGeometryGeneration + 1);
            ResetNavigationGoalOccupancyBuckets();
            _lastNavigationGoalOccupancyBucketFrame = -1;
        }
        if (_lastNavigationGoalOccupancyBucketFrame == frame
            && _lastNavigationGoalOccupancyBucketWorldVersion == _navigationGoalOccupancyBucketGeometryGeneration)
        {
            return;
        }

        int previousGeometryGeneration = _lastNavigationGoalOccupancyBucketWorldVersion;

        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long buildStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        // Spatial state is synchronized once per logical frame before this broad
        // phase. Reusing that snapshot keeps occupancy lookup out of the entity loop.
        long syncStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        SyncRegisteredAgentSpatialState(frame);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachOccupancyAgentSync,
                System.Diagnostics.Stopwatch.GetTimestamp() - syncStartTicks);
        }

        bool canUpdateIncrementally = previousGeometryGeneration == _navigationGoalOccupancyBucketGeometryGeneration
                                       && NavigationGoalPositionBucketByAgent.Count == OrderedAgentIds.Count
                                       && NavigationGoalTargetBucketByAgent.Count == OrderedAgentIds.Count;
        if (canUpdateIncrementally)
        {
            for (int i = 0; i < OrderedAgentIds.Count; i++)
            {
                int agentId = OrderedAgentIds[i];
                if (!NavigationGoalPositionBucketByAgent.ContainsKey(agentId)
                    || !NavigationGoalTargetBucketByAgent.ContainsKey(agentId))
                {
                    canUpdateIncrementally = false;
                    break;
                }
            }
        }
        if (canUpdateIncrementally)
        {
            long incrementalStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            long maximumThresholdRaw = 0L;
            for (int i = 0; i < OrderedAgentIds.Count; i++)
            {
                int agentId = OrderedAgentIds[i];
                if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent) || agent == null)
                    throw new InvalidOperationException(
                        $"EnsureNavigationGoalOccupancyBuckets failed: ordered agent is missing id={agentId}.");

                long newPositionBucket = ResolveNavigationGoalBucketKey(agent.PositionFixed);
                long oldPositionBucket = NavigationGoalPositionBucketByAgent[agentId];
                if (newPositionBucket != oldPositionBucket)
                {
                    RemoveNavigationGoalBucketEntry(NavigationGoalPositionBuckets, oldPositionBucket, agentId);
                    AddNavigationGoalBucketEntryOrdered(NavigationGoalPositionBuckets, newPositionBucket, agent, agentId);
                    NavigationGoalPositionBucketByAgent[agentId] = newPositionBucket;
                }

                long newTargetBucket = ShouldUseAgentNavigationGoalAsOccupancyFixed(agent)
                    ? ResolveNavigationGoalBucketKey(agent.NavState.LastGoalWorldFixed)
                    : long.MinValue;
                long oldTargetBucket = NavigationGoalTargetBucketByAgent[agentId];
                if (newTargetBucket != oldTargetBucket)
                {
                    if (oldTargetBucket != long.MinValue)
                        RemoveNavigationGoalBucketEntry(NavigationGoalTargetBuckets, oldTargetBucket, agentId);
                    if (newTargetBucket != long.MinValue)
                        AddNavigationGoalBucketEntryOrdered(NavigationGoalTargetBuckets, newTargetBucket, agent, agentId);
                    NavigationGoalTargetBucketByAgent[agentId] = newTargetBucket;
                }

                Fix64 agentThreshold = agent.RadiusFixed * (Fix64)2 + NavigationGoalOccupancyPadding;
                if (agentThreshold.RawValue > maximumThresholdRaw)
                    maximumThresholdRaw = agentThreshold.RawValue;
            }
            _navigationGoalOccupancyMaximumThreshold = Fix64.FromRaw(maximumThresholdRaw);
            _lastNavigationGoalOccupancyBucketFrame = frame;
            _lastNavigationGoalOccupancyBucketWorldVersion = _navigationGoalOccupancyBucketGeometryGeneration;
            if (profile)
            {
                long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - incrementalStartTicks;
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowCombatApproachOccupancyBucketIncremental,
                    elapsedTicks);
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowCombatApproachOccupancyBucketFill,
                    elapsedTicks);
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowCombatApproachOccupancyBucketBuild,
                    System.Diagnostics.Stopwatch.GetTimestamp() - buildStartTicks);
            }
            return;
        }

        long bucketFillStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        ResetNavigationGoalOccupancyBuckets();
        _navigationGoalOccupancyMaximumThreshold = Fix64.Zero;
        long positionBucketTicks = 0L;
        long targetBucketTicks = 0L;
        long thresholdTicks = 0L;
        for (int i = 0; i < OrderedAgentIds.Count; i++)
        {
            if (!Agents.TryGetValue(OrderedAgentIds[i], out AgentRuntimeData agent) || agent == null)
                throw new InvalidOperationException(
                    $"EnsureNavigationGoalOccupancyBuckets failed: ordered agent is missing id={OrderedAgentIds[i]}.");

            long sectionStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            long positionBucketKey = ResolveNavigationGoalBucketKey(agent.PositionFixed);
            AddNavigationGoalBucketEntry(
                NavigationGoalPositionBuckets,
                positionBucketKey,
                agent);
            NavigationGoalPositionBucketByAgent[OrderedAgentIds[i]] = positionBucketKey;
            if (profile)
                positionBucketTicks += System.Diagnostics.Stopwatch.GetTimestamp() - sectionStartTicks;

            sectionStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            Fix64 agentThreshold = agent.RadiusFixed * (Fix64)2 + NavigationGoalOccupancyPadding;
            if (agentThreshold > _navigationGoalOccupancyMaximumThreshold)
                _navigationGoalOccupancyMaximumThreshold = agentThreshold;
            if (profile)
                thresholdTicks += System.Diagnostics.Stopwatch.GetTimestamp() - sectionStartTicks;

            sectionStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            if (ShouldUseAgentNavigationGoalAsOccupancyFixed(agent))
            {
                long targetBucketKey = ResolveNavigationGoalBucketKey(agent.NavState.LastGoalWorldFixed);
                AddNavigationGoalBucketEntry(
                    NavigationGoalTargetBuckets,
                    targetBucketKey,
                    agent);
                NavigationGoalTargetBucketByAgent[OrderedAgentIds[i]] = targetBucketKey;
            }
            else
            {
                NavigationGoalTargetBucketByAgent[OrderedAgentIds[i]] = long.MinValue;
            }
            if (profile)
                targetBucketTicks += System.Diagnostics.Stopwatch.GetTimestamp() - sectionStartTicks;
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachOccupancyBucketFill,
                System.Diagnostics.Stopwatch.GetTimestamp() - bucketFillStartTicks);
            if (positionBucketTicks > 0L)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowCombatApproachOccupancyBucketPosition,
                    positionBucketTicks);
            if (targetBucketTicks > 0L)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowCombatApproachOccupancyBucketTarget,
                    targetBucketTicks);
            if (thresholdTicks > 0L)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowCombatApproachOccupancyBucketThreshold,
                    thresholdTicks);
        }

        _lastNavigationGoalOccupancyBucketFrame = frame;
        _lastNavigationGoalOccupancyBucketWorldVersion = _navigationGoalOccupancyBucketGeometryGeneration;
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachOccupancyBucketBuild,
                System.Diagnostics.Stopwatch.GetTimestamp() - buildStartTicks);
        }
    }

    private static List<AgentRuntimeData> CollectNearbyDynamicNeighbors(AgentRuntimeData self, float avoidRadius)
    {
        if (self == null)
            throw new InvalidOperationException("CollectNearbyDynamicNeighbors failed: self is null.");
        if (_world == null)
            throw new InvalidOperationException("CollectNearbyDynamicNeighbors failed: world is null.");

        NearbyAgentScratch.Clear();
        EnsureAgentSpatialBuckets();

        ResolveSpatialBucketCell(self.Position, out int selfCellX, out int selfCellY);
        int searchRadiusInCells = Mathf.Max(1, Mathf.CeilToInt(avoidRadius / ResolveAgentSpatialBucketSize()));
        float avoidRadiusSq = avoidRadius * avoidRadius;
        for (int y = selfCellY - searchRadiusInCells; y <= selfCellY + searchRadiusInCells; y++)
        {
            for (int x = selfCellX - searchRadiusInCells; x <= selfCellX + searchRadiusInCells; x++)
            {
                long bucketKey = BuildSpatialBucketKey(x, y);
                if (!AgentSpatialBuckets.TryGetValue(bucketKey, out List<AgentRuntimeData> bucket))
                    continue;

                for (int i = 0; i < bucket.Count; i++)
                {
                    AgentRuntimeData other = bucket[i];
                    if (other.Id == self.Id)
                        continue;

                    Vector3 delta = other.Position - self.Position;
                    delta.y = 0f;
                    if (delta.sqrMagnitude > avoidRadiusSq)
                        continue;

                    NearbyAgentScratch.Add(other);
                }
            }
        }

        return NearbyAgentScratch;
    }

    private static List<AgentRuntimeData> CollectNearbyDynamicNeighbors(Vector3 center, int excludedAgentId, float radius)
    {
        if (_world == null)
            throw new InvalidOperationException("CollectNearbyDynamicNeighbors failed: world is null.");

        NearbyAgentScratch.Clear();
        EnsureAgentSpatialBuckets();

        ResolveSpatialBucketCell(center, out int centerCellX, out int centerCellY);
        int searchRadiusInCells = Mathf.Max(1, Mathf.CeilToInt(radius / ResolveAgentSpatialBucketSize()));
        float radiusSq = radius * radius;
        for (int y = centerCellY - searchRadiusInCells; y <= centerCellY + searchRadiusInCells; y++)
        {
            for (int x = centerCellX - searchRadiusInCells; x <= centerCellX + searchRadiusInCells; x++)
            {
                long bucketKey = BuildSpatialBucketKey(x, y);
                if (!AgentSpatialBuckets.TryGetValue(bucketKey, out List<AgentRuntimeData> bucket))
                    continue;

                for (int i = 0; i < bucket.Count; i++)
                {
                    AgentRuntimeData other = bucket[i];
                    if (other.Id == excludedAgentId)
                        continue;

                    Vector3 delta = other.Position - center;
                    delta.y = 0f;
                    if (delta.sqrMagnitude > radiusSq)
                        continue;

                    NearbyAgentScratch.Add(other);
                }
            }
        }

        return NearbyAgentScratch;
    }

    private static void ClearAgentBucketContents(Dictionary<long, List<AgentRuntimeData>> buckets)
    {
        if (buckets == null)
            throw new InvalidOperationException("ClearAgentBucketContents failed: buckets is null.");

        foreach (List<AgentRuntimeData> bucket in buckets.Values)
        {
            if (bucket == null)
                throw new InvalidOperationException("ClearAgentBucketContents failed: bucket is null.");

            bucket.Clear();
        }
    }

    private static bool ShouldUseAgentNavigationGoalAsOccupancyFixed(AgentRuntimeData agent)
    {
        if (agent == null)
            throw new InvalidOperationException("ShouldUseAgentNavigationGoalAsOccupancyFixed failed: agent is null.");

        AgentNavState nav = agent.NavState;
        if (!agent.HasNavigationIntent || !nav.HasGoal || nav.LastFixedFlowFrame < 0)
            return false;

        int frame = GetFrameCount();
        if (nav.LastFixedFlowFrame > frame)
        {
            throw new InvalidOperationException(
                $"Fixed goal occupancy encountered a future flow frame. agent={agent.Id}, current={frame}, lastFixed={nav.LastFixedFlowFrame}.");
        }
        if (frame - nav.LastFixedFlowFrame > 8)
            return false;

        FixVector2 goalOffset = nav.LastGoalWorldFixed - agent.PositionFixed;
        Fix64 minimumDistanceSq = Fix64.Max(agent.RadiusFixed * agent.RadiusFixed, Fix64.FromRaw(164));
        return FixVector2.SqrMagnitude(goalOffset) > minimumDistanceSq;
    }

}
