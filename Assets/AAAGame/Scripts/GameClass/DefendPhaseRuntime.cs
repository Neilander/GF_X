using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;
using UnityGameFramework.Runtime;

public static class DefendPhaseRuntime
{
    private const string DefendEnemyMinSpeedConfigKey = "DefendPhaseEnemyMinSpeed";
    private const string DefendEnemyMaxSpeedConfigKey = "DefendPhaseEnemyMaxSpeed";
    private const string DefendEnemySpawnIntervalConfigKey = "DefendPhaseEnemyArriveInterval";
    private static readonly Fix64 MinWorldSpeed = Fix64.FromRaw(5);
    private static readonly Fix64 NavigationPointProbeRadius = Fix64.FromRaw(10240);
    private const string TutorialLevelIdentifier = "Lv_1";
    private static readonly Fix64 TutorialEnemyClusterRadius = Fix64.FromRaw(12288);
    private static readonly Fix64 TutorialEnemyClusterMinDistance = Fix64.FromRaw(4916);

    private static ArchetypeUnitTypeMapper s_ArchetypeUnitTypeMapper;
    private static readonly Dictionary<UnitType, Archetype> s_ArchetypeByUnitType = new();
    private static readonly Dictionary<UnitType, int> s_AgentTypeIdByUnitType = new();
    private static readonly List<DefendSpawnPointRuntime> s_DefendSpawnPoints = new();
    private static readonly Dictionary<string, DefendRouteDefinition> s_DefendRoutesByIdentifier =
        new(StringComparer.Ordinal);
    private static readonly List<DefendAttackGroupDefinition> s_DefendAttackGroups = new();
    private static readonly List<TutorialTriggeredSpawnPoint> s_TutorialTriggeredSpawnPoints = new();
    private static readonly HashSet<int> s_AliveEnemyLogicEntityIds = new();
    private static readonly List<int> s_DeterministicAliveEnemyIds = new();
    private static readonly HashSet<int> s_AcceleratedEnemyLogicEntityIds = new();
    private static readonly List<int> s_DeterministicAcceleratedEnemyIds = new();
    private static readonly Comparison<int> s_DeterministicEnemyIdComparison =
        (left, right) => left.CompareTo(right);

    private static bool s_SubscribedLogicUnitDead;
    private static bool s_SubscribedNavigationDistancePrewarm;
    private static bool s_ArchetypeCacheConfigured;
    private static bool s_SpawnPointCacheConfigured;
    private static bool s_WaveConfigConfigured;
    private static int s_CachedLevelEntityId;
    private static int s_DefendRoundIndex;
    private static bool s_SpawnScheduleCompleted;
    private static bool s_WaitingForNavigationDistancePrewarm;
    private static string s_WaveConfigLevelIdentifier = string.Empty;
    private static readonly List<PlannedSpawnEvent> s_PlannedSpawnEvents = new();
    private static int s_NextPlannedSpawnIndex;
    private static ulong s_SpawnRequestStartFrame;
    private static Fix64 s_MinSpeedWorld;
    private static Fix64 s_MaxSpeedWorld;
    private static Fix64 s_SpawnIntervalSeconds;
    private static Fix64 s_WaypointArrivalRadiusWorld;
    private static decimal s_DistanceConversionRate;
    private static bool s_TutorialFirstDefenseConsumed;
    private static bool s_TutorialFirstDefenseWaiting;
    private static bool s_TutorialFirstDefenseActive;

    public static event Action TutorialTriggeredDefenseCleared;
    public static bool IsTutorialTriggeredFirstDefenseWaiting => s_TutorialFirstDefenseWaiting;
    public static bool IsTutorialTriggeredFirstDefenseActive => s_TutorialFirstDefenseActive;

    internal static void ConfigureTutorialTriggeredSpawnPoints(IReadOnlyList<EntityPresetPoint> presetPoints)
    {
        if (presetPoints == null)
            throw new ArgumentNullException(nameof(presetPoints));
        if (s_TutorialTriggeredSpawnPoints.Count > 0)
            throw new InvalidOperationException("Tutorial triggered spawn points are already configured.");

        for (int i = 0; i < presetPoints.Count; i++)
        {
            EntityPresetPoint point = presetPoints[i]
                ?? throw new InvalidOperationException($"Tutorial spawn preset {i} is null.");
            if (point.PointType != EntityPresetPointType.Unit)
                continue;
            if (!UnitTypeHelper.TryParseUnitType(point.Identifier, out UnitType unitType))
            {
                throw new InvalidOperationException(
                    $"Tutorial spawn point has invalid unit identifier. point={point.name} identifier={point.Identifier}.");
            }
            if (point.UnitStrengthValue <= Fix64.Zero)
                throw new InvalidOperationException($"Tutorial spawn point '{point.name}' has a non-positive strength value.");
            if (point.UnitCountGrowthWeight < Fix64.Zero || point.UnitCountGrowthWeight > Fix64.One)
                throw new InvalidOperationException($"Tutorial spawn point '{point.name}' has an invalid count growth weight.");

            Vector3 position = point.Position;
            s_TutorialTriggeredSpawnPoints.Add(new TutorialTriggeredSpawnPoint(
                new FixVector2((Fix64)position.x, (Fix64)position.z),
                unitType,
                point.UnitStrengthValue,
                point.UnitCountGrowthWeight,
                string.IsNullOrWhiteSpace(point.name) ? "<unnamed>" : point.name));
        }
        s_TutorialTriggeredSpawnPoints.Sort((left, right) =>
        {
            int x = left.Position.x.RawValue.CompareTo(right.Position.x.RawValue);
            if (x != 0)
                return x;
            int y = left.Position.y.RawValue.CompareTo(right.Position.y.RawValue);
            return y != 0 ? y : string.CompareOrdinal(left.Name, right.Name);
        });
    }

    internal static void ClearTutorialTriggeredSpawnPoints()
    {
        s_TutorialTriggeredSpawnPoints.Clear();
    }

    public readonly struct DefendPreviewSpawnEntry
    {
        public readonly UnitType UnitType;
        public readonly int UnitLevel;
        public readonly int Count;
        public readonly Vector3 SpawnPosition;
        public readonly string SpawnPointIdentifier;

        public DefendPreviewSpawnEntry(UnitType unitType, int unitLevel, int count, Vector3 spawnPosition, string spawnPointIdentifier)
        {
            UnitType = unitType;
            UnitLevel = unitLevel;
            Count = count;
            SpawnPosition = spawnPosition;
            SpawnPointIdentifier = spawnPointIdentifier;
        }
    }

    public static void CancelRuntime()
    {
        ResetDefendPhaseState(keepRoundIndex: true);
    }

    public static void ClearPreparedRuntime()
    {
        ResetDefendPhaseState(keepRoundIndex: false);
        FlowFieldCrowdMovementSystem.ClearNavigationDistancePrewarmRequests();
        s_ArchetypeByUnitType.Clear();
        s_ArchetypeUnitTypeMapper = null;
        s_AgentTypeIdByUnitType.Clear();
        s_DefendSpawnPoints.Clear();
        s_DefendRoutesByIdentifier.Clear();
        s_DefendAttackGroups.Clear();
        s_ArchetypeCacheConfigured = false;
        s_SpawnPointCacheConfigured = false;
        s_WaveConfigConfigured = false;
        s_CachedLevelEntityId = 0;
        s_WaveConfigLevelIdentifier = string.Empty;
        s_MinSpeedWorld = Fix64.Zero;
        s_MaxSpeedWorld = Fix64.Zero;
        s_SpawnIntervalSeconds = Fix64.Zero;
        s_WaypointArrivalRadiusWorld = Fix64.Zero;
        s_DistanceConversionRate = decimal.Zero;
    }

    public static void PrepareForCurrentLevelIfNeeded()
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("DefendPhaseRuntime cannot prepare runtime dependencies during a logic frame.");

        Stopwatch stopwatch = Stopwatch.StartNew();
        EnsureSubscribedSoldierDead();
        EnsureSubscribedNavigationDistancePrewarm();
        double subscribeMs = stopwatch.Elapsed.TotalMilliseconds;
        ConfigureArchetypeCacheIfNeeded();
        double archetypeMs = stopwatch.Elapsed.TotalMilliseconds;
        ConfigureSpawnPointCacheIfNeeded();
        double spawnPointMs = stopwatch.Elapsed.TotalMilliseconds;
        ConfigureWaveRuntimeIfNeeded();
        double waveMs = stopwatch.Elapsed.TotalMilliseconds;
        if (s_DistanceConversionRate <= decimal.Zero)
            s_DistanceConversionRate = DistanceUnitConverter.ReadDistanceConversionRateDecimal();
        Log.Info(
            "[DefendPhaseTiming] stage=prepare totalMs={0:F3} subscribeMs={1:F3} archetypeMs={2:F3} spawnPointMs={3:F3} waveMs={4:F3}",
            stopwatch.Elapsed.TotalMilliseconds,
            subscribeMs,
            archetypeMs - subscribeMs,
            spawnPointMs - archetypeMs,
            waveMs - spawnPointMs);
    }

    public static void EnterDefendPhase()
    {
        RequirePreparedRuntime();

        ResetDefendPhaseState(keepRoundIndex: true);
        if (!s_TutorialFirstDefenseConsumed
            && string.Equals(ResolveCurrentLevelIdentifier(), TutorialLevelIdentifier, StringComparison.Ordinal))
        {
            s_TutorialFirstDefenseConsumed = true;
            s_TutorialFirstDefenseWaiting = true;
            Log.Info("[DefendPhase] Tutorial first defense is waiting for the friendly-stronghold trigger.");
            return;
        }

        s_DefendRoundIndex = Math.Max(1, InGameDataModel.GetValue(IngameValueType.Day));

        List<DefendAttackGroupDefinition> groups = ResolveAttackGroupsForRound(s_DefendRoundIndex);
        if (groups.Count == 0)
        {
            Log.Warning("[DefendPhase] 当前防御波次无配置，直接结束。round={0}", s_DefendRoundIndex);
            s_SpawnScheduleCompleted = true;
            TryCompleteDefendPhase();
            return;
        }

        if (!FlowFieldCrowdMovementSystem.IsNavigationDistancePrewarmCompleted)
        {
            s_WaitingForNavigationDistancePrewarm = true;
            Log.Info("[DefendPhase] Waiting for navigation distance prewarm. round={0}", s_DefendRoundIndex);
            return;
        }

        StartPreparedDefendWave(groups, LogicTimeControlService.CurrentFrame > 0
            ? LogicTimeControlService.CurrentFrame
            : 1UL);
    }

    private static void StartPreparedDefendWave(
        IReadOnlyList<DefendAttackGroupDefinition> groups,
        ulong startFrame)
    {
        if (groups == null)
            throw new ArgumentNullException(nameof(groups));
        if (groups.Count == 0)
            throw new InvalidOperationException("Cannot start an empty prepared defense wave.");
        if (startFrame == 0)
            throw new ArgumentOutOfRangeException(nameof(startFrame));
        if (!FlowFieldCrowdMovementSystem.IsNavigationDistancePrewarmCompleted)
            throw new InvalidOperationException("Cannot start a defense wave before navigation distance prewarm completes.");
        if (s_PlannedSpawnEvents.Count != 0 || s_NextPlannedSpawnIndex != 0 || s_SpawnRequestStartFrame != 0)
            throw new InvalidOperationException("Cannot start a defense wave over an existing spawn schedule.");

        s_WaitingForNavigationDistancePrewarm = false;
        s_PlannedSpawnEvents.AddRange(BuildSpawnEvents(groups));
        if (s_PlannedSpawnEvents.Count == 0)
        {
            Log.Warning("[DefendPhase] 当前防御波次无法生成出怪计划，直接结束。round={0}", s_DefendRoundIndex);
            s_SpawnScheduleCompleted = true;
            TryCompleteDefendPhase();
            return;
        }

        s_PlannedSpawnEvents.Sort(ComparePlannedSpawnEvents);
        s_SpawnRequestStartFrame = startFrame;
    }

    public static void StartTutorialTriggeredFirstDefense(string strongholdId)
    {
        if (!s_TutorialFirstDefenseWaiting || s_TutorialFirstDefenseActive)
            throw new InvalidOperationException("Tutorial first defense is not waiting for its trigger.");
        if (LogicPhaseCommandService.GetRequiredCurrentPhase() != GamePhase.Defend)
            throw new InvalidOperationException("Tutorial first defense can only start during the Defend phase.");
        if (EntityRegistry.Player == null || !EntityRegistry.Player.Alive)
            throw new InvalidOperationException("Tutorial first defense requires a live player.");
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new ArgumentException("Tutorial first defense requires a stronghold ID.", nameof(strongholdId));
        if (LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId) != EntitySideHelper.PlayerFactionId)
            throw new InvalidOperationException($"Tutorial first defense stronghold '{strongholdId}' is not player owned.");

        Fix64 assignedSpeed = DistanceUnitConverter.ConvertFromWorld(s_MinSpeedWorld, s_DistanceConversionRate);
        if (assignedSpeed <= Fix64.Zero)
            throw new InvalidOperationException("Tutorial first defense resolved a non-positive assigned speed.");

        int spawnedCount = 0;
        int currentDay = Math.Max(1, InGameDataModel.GetValue(IngameValueType.Day));
        LevelTable level = LogicRuntimeDataTableCache.GetLevelRequired(ResolveCurrentLevelIdentifier());
        EnemyStrengthRuntimeSettings strengthSettings = EnemyStrengthRuntimeConfig.Read(EnemyStrengthContext.DefenseWave);
        Fix64 initialStrengthScale = LevelTagRuntime.GetEnemyInitialStrengthScale(EnemyStrengthContext.DefenseWave);
        Fix64 growthSpeedScale = LevelTagRuntime.GetEnemyGrowthSpeedScale(EnemyStrengthContext.DefenseWave);
        for (int i = 0; i < s_TutorialTriggeredSpawnPoints.Count; i++)
        {
            TutorialTriggeredSpawnPoint point = s_TutorialTriggeredSpawnPoints[i];
            if (LogicStrongholdMap.TryResolveStrongholdId(point.Position, out _))
                continue;

            IReadOnlyList<EnemySquadCompositionEntry> composition = ResolveSquadComposition(
                point.UnitType,
                point.InitialStrengthValue,
                point.CountGrowthWeight,
                currentDay,
                level.ExpectedDays,
                initialStrengthScale,
                growthSpeedScale,
                strengthSettings);
            for (int entryIndex = 0; entryIndex < composition.Count; entryIndex++)
            {
                EnemySquadCompositionEntry entry = composition[entryIndex];
                bool spawned = ClusterSpawnSystem.SpawnClusterFixed(
                    point.Position,
                    entry.Count,
                    TutorialEnemyClusterRadius,
                    TutorialEnemyClusterMinDistance,
                    point.UnitType,
                    SideType.EnemySide,
                    BrainType.DefendEnemyAI,
                    sourceStrongholdId: strongholdId,
                    unitLevel: entry.Level,
                    spawned: entityId =>
                    {
                        TrackSpawnedDefendEnemy(entityId, "Tutorial first defense");
                        ReleaseTrackedDefendEnemySpawnSpeed(entityId, "tutorial spawn", null);
                        spawnedCount++;
                    },
                    configureParams: entityParams => entityParams.DefendAssignedSpeed = assignedSpeed);
                if (!spawned)
                    throw new InvalidOperationException($"Tutorial first defense failed to spawn point '{point.Name}'.");
            }
        }

        if (spawnedCount == 0)
            throw new InvalidOperationException("Tutorial first defense found no Unit presets outside authored strongholds.");

        s_TutorialFirstDefenseWaiting = false;
        s_TutorialFirstDefenseActive = true;
        s_SpawnScheduleCompleted = true;
        Log.Info("[DefendPhase] Tutorial first defense started. stronghold={0}, enemies={1}.", strongholdId, spawnedCount);
    }

    public static void ApplyScheduledSpawnRequests(ulong frame)
    {
        if (frame == 0 || frame != LogicTimeControlService.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"DefendPhaseRuntime.ApplyScheduledSpawnRequests failed: frame mismatch. requested={frame}, logic={LogicTimeControlService.CurrentFrame}.");
        }
        ReleaseEngagedOrPlayerVisibleEnemySpawnSpeeds(frame);
        if (s_WaitingForNavigationDistancePrewarm)
            return;
        if (s_SpawnScheduleCompleted || s_PlannedSpawnEvents.Count == 0)
            return;
        if (LogicPhaseCommandService.GetRequiredCurrentPhase() != GamePhase.Defend)
            throw new InvalidOperationException("DefendPhaseRuntime has a pending spawn schedule outside the Defend phase.");

        while (s_NextPlannedSpawnIndex < s_PlannedSpawnEvents.Count)
        {
            PlannedSpawnEvent evt = s_PlannedSpawnEvents[s_NextPlannedSpawnIndex];
            ulong requestFrame = checked(s_SpawnRequestStartFrame + evt.RequestFrameOffset);
            if (requestFrame < frame)
            {
                throw new InvalidOperationException(
                    $"DefendPhaseRuntime missed a scheduled spawn request. index={s_NextPlannedSpawnIndex} expected={requestFrame} current={frame}.");
            }
            if (requestFrame > frame)
                return;

            SpawnPlannedEvent(evt);
            s_NextPlannedSpawnIndex++;
        }

        s_SpawnScheduleCompleted = true;
        TryCompleteDefendPhase();
    }

    private static void SpawnPlannedEvent(PlannedSpawnEvent evt)
    {
        if (!IsAttackGroupSourceActive(evt.SourceStrongholdId))
        {
            Log.Info(
                "[DefendPhase] Skip occupied source slot. unit={0}, point={1}, stronghold={2}, requestOffset={3}.",
                evt.UnitType,
                evt.SpawnPointName,
                evt.SourceStrongholdId,
                evt.RequestFrameOffset);
            return;
        }

        LogicEntityId entityId = SoldierFactory.ShowCurrentBattleTroopFixed(
            evt.UnitType,
            evt.SpawnPosition,
            0f,
            SideType.EnemySide,
            BrainType.DefendEnemyAI,
            null,
            evt.SourceStrongholdId,
            entityParams =>
            {
                entityParams.DefendAssignedSpeed = evt.SpeedProperty;
                entityParams.DefendRouteWaypointsFixed = evt.RouteWaypointsFixed;
                entityParams.DefendRouteWaypointTeleportationIds = evt.RouteWaypointTeleportationIds;
                entityParams.DefendSpeedReleaseWaypointIndex = evt.SpeedReleaseWaypointIndex;
                entityParams.DefendSpeedReleasePositionFixed = evt.SpeedReleasePositionFixed;
            },
            evt.UnitLevel);
        if (!entityId.IsValid)
        {
            throw new InvalidOperationException(
                $"DefendPhaseRuntime failed to request soldier spawn. unit={evt.UnitType} raw=({evt.SpawnPosition.x.RawValue},{evt.SpawnPosition.y.RawValue}).");
        }

        LogDefendSpawnEvent(evt, entityId);
        TrackSpawnedDefendEnemy(entityId, "Scheduled defense");
    }

    private static void TrackSpawnedDefendEnemy(LogicEntityId entityId, string source)
    {
        if (!entityId.IsValid)
            throw new ArgumentException("Spawned defend enemy id is invalid.", nameof(entityId));
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Spawned defend enemy source is empty.", nameof(source));
        if (!s_AliveEnemyLogicEntityIds.Add(entityId.Value))
            throw new InvalidOperationException($"{source} produced duplicate enemy logic entity id {entityId.Value}.");
        if (!s_AcceleratedEnemyLogicEntityIds.Add(entityId.Value))
            throw new InvalidOperationException($"{source} produced duplicate accelerated enemy logic entity id {entityId.Value}.");
    }

    public static bool TryGetNextDefendPreviewSpawnEntries(List<DefendPreviewSpawnEntry> results)
    {
        if (results == null)
            return false;

        results.Clear();
        RequirePreparedRuntime();

        int previewDay = Math.Max(1, InGameDataModel.GetValue(IngameValueType.Day));
        List<DefendAttackGroupDefinition> groups = ResolveAttackGroupsForRound(previewDay);
        if (groups.Count == 0)
            return false;

        for (int i = 0; i < groups.Count; i++)
        {
            DefendAttackGroupDefinition group = groups[i];
            if (!IsAttackGroupSourceActive(group.Route.SourceStrongholdId))
                continue;

            for (int j = 0; j < group.Enemies.Count; j++)
            {
                DefendAttackEntry entry = group.Enemies[j];
                FixVector2 spawnPosition = group.Route.SourcePoint.Position;
                results.Add(new DefendPreviewSpawnEntry(
                    entry.UnitType,
                    entry.UnitLevel,
                    entry.Count,
                    new Vector3((float)spawnPosition.x, 0f, (float)spawnPosition.y),
                    group.Route.SourceStrongholdId));
            }
        }

        return results.Count > 0;
    }

    public static int NavigationPathVersion => FlowFieldCrowdMovementSystem.NavigationTopologyVersion;

    public static bool TryGetNavigationPathCorners(
        UnitType unitType,
        Vector3 spawnPosition,
        Vector3 basePosition,
        List<Vector3> pathCorners,
        out string failureReason)
    {
        return TryGetNavigationPathCorners(
            unitType,
            spawnPosition,
            basePosition,
            pathCorners,
            out failureReason,
            out _);
    }

    public static bool TryGetNavigationPathCorners(
        UnitType unitType,
        Vector3 spawnPosition,
        Vector3 basePosition,
        List<Vector3> pathCorners,
        out string failureReason,
        out bool navigationUpdatePending)
    {
        if (pathCorners == null)
            throw new ArgumentNullException(nameof(pathCorners));

        RequirePreparedRuntime();

        pathCorners.Clear();
        navigationUpdatePending = false;
        int agentTypeId = ResolveAgentTypeId(unitType);
        return FlowFieldCrowdMovementSystem.TryGetNavigationPathCornersToReachableGoalNonBlocking(
            spawnPosition,
            basePosition,
            agentTypeId,
            pathCorners,
            out failureReason,
            out navigationUpdatePending);
    }

    private static void EnsureSubscribedSoldierDead()
    {
        if (s_SubscribedLogicUnitDead)
            return;

        LogicUnitDeathEventService.UnitDied += OnLogicUnitDied;
        s_SubscribedLogicUnitDead = true;
    }

    private static void EnsureSubscribedNavigationDistancePrewarm()
    {
        if (s_SubscribedNavigationDistancePrewarm)
            return;

        FlowFieldCrowdMovementSystem.NavigationDistancePrewarmCompleted +=
            OnNavigationDistancePrewarmCompleted;
        s_SubscribedNavigationDistancePrewarm = true;
    }

    private static void OnNavigationDistancePrewarmCompleted(
        object sender,
        NavigationDistancePrewarmCompletedEventArgs eventArgs)
    {
        if (!s_WaitingForNavigationDistancePrewarm)
            return;
        if (eventArgs == null || eventArgs.RequestCount <= 0)
            throw new InvalidOperationException("Navigation distance prewarm completion event is invalid.");
        if (LogicPhaseCommandService.GetRequiredCurrentPhase() != GamePhase.Defend)
            throw new InvalidOperationException("Navigation distance prewarm completed for a pending wave outside the Defend phase.");

        List<DefendAttackGroupDefinition> groups = ResolveAttackGroupsForRound(s_DefendRoundIndex);
        if (groups.Count == 0)
            throw new InvalidOperationException($"Pending defense round {s_DefendRoundIndex} has no attack groups.");
        ulong startFrame = LogicTimeControlService.CurrentFrame > 0
            ? checked(LogicTimeControlService.CurrentFrame + 1UL)
            : 1UL;
        StartPreparedDefendWave(groups, startFrame);
        Log.Info(
            "[DefendPhase] Navigation distance prewarm completed; spawn schedule starts next frame. round={0}, requests={1}, startFrame={2}",
            s_DefendRoundIndex,
            eventArgs.RequestCount,
            startFrame);
    }

    public static void NotifyDefendEnemyReachedSpeedReleaseWaypoint(
        LogicEntityId entityId,
        string teleportationId)
    {
        if (!entityId.IsValid)
            throw new ArgumentException("Defense speed release entity ID is invalid.", nameof(entityId));
        if (string.IsNullOrWhiteSpace(teleportationId))
            throw new ArgumentException("Defense speed release teleportation ID is empty.", nameof(teleportationId));
        ReleaseTrackedDefendEnemySpawnSpeed(entityId, "route waypoint", teleportationId, allowAlreadyReleased: true);
    }

    public static void NotifyDefendEnemyReachedSpeedReleaseTarget(LogicEntityId entityId)
    {
        if (!entityId.IsValid)
            throw new ArgumentException("Defense speed release entity ID is invalid.", nameof(entityId));
        ReleaseTrackedDefendEnemySpawnSpeed(entityId, "initial GameEnd target", null, allowAlreadyReleased: true);
    }

    private static void ReleaseEngagedOrPlayerVisibleEnemySpawnSpeeds(ulong frame)
    {
        if (s_AcceleratedEnemyLogicEntityIds.Count == 0)
            return;
        if (LogicPhaseCommandService.GetRequiredCurrentPhase() != GamePhase.Defend)
            throw new InvalidOperationException("DefendPhaseRuntime tracks accelerated enemies outside the Defend phase.");

        s_DeterministicAcceleratedEnemyIds.Clear();
        foreach (int entityId in s_AcceleratedEnemyLogicEntityIds)
            s_DeterministicAcceleratedEnemyIds.Add(entityId);
        s_DeterministicAcceleratedEnemyIds.Sort(s_DeterministicEnemyIdComparison);

        for (int i = 0; i < s_DeterministicAcceleratedEnemyIds.Count; i++)
        {
            int entityIdValue = s_DeterministicAcceleratedEnemyIds[i];
            if (!s_AliveEnemyLogicEntityIds.Contains(entityIdValue))
            {
                throw new InvalidOperationException(
                    $"DefendPhaseRuntime tracks spawn acceleration for non-alive enemy {entityIdValue}.");
            }

            var entityId = new LogicEntityId(entityIdValue);
            LogicEntityState state = LogicEntityStateStore.GetRequired(entityId);
            if (!state.BuffComp.HasBuff(LogicUnitConfigurator.DefendSpeedBuffId))
            {
                throw new InvalidOperationException(
                    $"DefendPhaseRuntime accelerated enemy {entityIdValue} is missing its speed override buff.");
            }

            string reason = !state.IsOutOfCombat
                ? "entered combat"
                : LogicFactionVisionService.IsEntityVisibleToSide(SideType.PlayerSide, state)
                    ? "entered player vision"
                    : null;
            if (reason != null)
                ReleaseTrackedDefendEnemySpawnSpeed(entityId, reason, null, allowAlreadyReleased: false, frame);
        }
        s_DeterministicAcceleratedEnemyIds.Clear();
    }

    private static void ReleaseTrackedDefendEnemySpawnSpeed(
        LogicEntityId entityId,
        string reason,
        string teleportationId,
        bool allowAlreadyReleased = false,
        ulong? releaseFrame = null)
    {
        if (!s_AliveEnemyLogicEntityIds.Contains(entityId.Value))
            throw new InvalidOperationException($"Defense speed release references non-alive enemy {entityId.Value}.");

        LogicEntityState state = LogicEntityStateStore.GetRequired(entityId);
        bool tracked = s_AcceleratedEnemyLogicEntityIds.Contains(entityId.Value);
        bool hasBuff = state.BuffComp.HasBuff(LogicUnitConfigurator.DefendSpeedBuffId);
        if (!tracked || !hasBuff)
        {
            if (allowAlreadyReleased && !tracked && !hasBuff)
                return;
            throw new InvalidOperationException(
                $"Defense enemy {entityId.Value} speed override state is inconsistent. tracked={tracked}, hasBuff={hasBuff}.");
        }

        Fix64 speedBefore = state.GetProperty(CreatureMainProperty.Speed);
        if (!LogicUnitConfigurator.ReleaseDefendEnemySpawnSpeed(state))
            throw new InvalidOperationException($"Defense enemy {entityId.Value} is missing its speed override buff.");
        if (!s_AcceleratedEnemyLogicEntityIds.Remove(entityId.Value))
            throw new InvalidOperationException($"Defense enemy {entityId.Value} speed override tracking removal failed.");
        Fix64 speedAfter = state.GetProperty(CreatureMainProperty.Speed);
        Log.Info(
            "[DefendPhase] Released spawn speed. logicEntityId={0} reason={1} teleportation={2} frame={3} speedBeforeRaw={4} speedAfterRaw={5}",
            entityId.Value,
            reason,
            teleportationId ?? "none",
            releaseFrame ?? LogicTimeControlService.CurrentFrame,
            speedBefore.RawValue,
            speedAfter.RawValue);
    }

    private static void OnLogicUnitDied(IEntityContext victim)
    {
        if (LogicPhaseCommandService.GetRequiredCurrentPhase() != GamePhase.Defend)
            return;

        if (victim == null)
            throw new InvalidOperationException("DefendPhaseRuntime received a null logic death victim.");
        if (victim.Side != SideType.EnemySide)
            return;

        if (!s_AliveEnemyLogicEntityIds.Remove(victim.LogicEntityId.Value))
            return;
        s_AcceleratedEnemyLogicEntityIds.Remove(victim.LogicEntityId.Value);

        TryCompleteDefendPhase();
    }

    private static void TryCompleteDefendPhase()
    {
        if (LogicPhaseCommandService.GetRequiredCurrentPhase() != GamePhase.Defend)
            return;

        if (!s_SpawnScheduleCompleted)
            return;

        if (s_AliveEnemyLogicEntityIds.Count > 0)
            return;

        if (s_TutorialFirstDefenseActive)
        {
            s_TutorialFirstDefenseActive = false;
            Log.Info("[DefendPhase] Tutorial first defense cleared; awaiting manual phase switch.");
            TutorialTriggeredDefenseCleared?.Invoke();
            return;
        }

        Log.Info("[DefendPhase] 防御阶段结束：敌兵已全部清空。round={0}", s_DefendRoundIndex);
        PhaseManager.SwitchToPhase(GamePhase.BuildBeforeInvade);
    }

    private static void ConfigureArchetypeCacheIfNeeded()
    {
        if (s_ArchetypeCacheConfigured)
            return;

        s_ArchetypeUnitTypeMapper = new ArchetypeUnitTypeMapper();
        foreach (Archetype archetype in Enum.GetValues(typeof(Archetype)))
        {
            var unitTypes = s_ArchetypeUnitTypeMapper.GetUnitTypes(archetype);
            foreach (UnitType unitType in unitTypes)
            {
                if (!s_ArchetypeByUnitType.ContainsKey(unitType))
                    s_ArchetypeByUnitType[unitType] = archetype;
            }
        }
        s_ArchetypeCacheConfigured = true;
    }

    private static void ConfigureSpawnPointCacheIfNeeded()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        LevelEntity levelEntity = LevelEntity.ActiveLevelEntity;
        if (levelEntity == null)
            throw new InvalidOperationException("DefendPhaseRuntime cannot prepare spawn points without an active level entity.");

        int levelEntityId = levelEntity.Id;
        if (s_SpawnPointCacheConfigured && s_CachedLevelEntityId == levelEntityId)
            return;

        s_CachedLevelEntityId = levelEntityId;
        s_DefendRoundIndex = 0;
        s_WaveConfigLevelIdentifier = string.Empty;
        s_WaveConfigConfigured = false;
        s_DefendRoutesByIdentifier.Clear();
        s_DefendAttackGroups.Clear();
        s_DefendSpawnPoints.Clear();

        EntityPresetPoint[] points = levelEntity.GetComponentsInChildren<EntityPresetPoint>(true);
        int defendPointCount = 0;
        double diagnosticsMs = 0.0;
        var teleportationIds = new HashSet<int>();
        var teleportationStrongholdIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < points.Length; i++)
        {
            EntityPresetPoint point = points[i];
            if (point == null || point.PointType != EntityPresetPointType.Teleportation)
                continue;

            if (point.TeleportationId < 0)
                throw new InvalidOperationException($"Teleportation point has an invalid ID. point={point.name}, id={point.TeleportationId}.");
            if (!teleportationIds.Add(point.TeleportationId))
                throw new InvalidOperationException($"Duplicate Teleportation ID {point.TeleportationId} in level '{levelEntity.name}'.");
            if (point.DefendSpawnWeight < Fix64.Zero)
                throw new InvalidOperationException($"Teleportation {point.TeleportationId} has a negative defend spawn weight.");

            defendPointCount++;
            if (GameDebugSettings.IsEnabled(DebugCategory.Move))
            {
                Stopwatch diagnosticsStopwatch = Stopwatch.StartNew();
                LogSpawnPointDiagnostics(point);
                diagnosticsMs += diagnosticsStopwatch.Elapsed.TotalMilliseconds;
            }

            Vector3 pointPosition = point.Position;
            var pointPositionFixed = new FixVector2((Fix64)pointPosition.x, (Fix64)pointPosition.z);
            if (!LogicStrongholdMap.TryResolveStrongholdId(pointPositionFixed, out string strongholdId))
            {
                throw new InvalidOperationException(
                    $"DefendPhaseRuntime spawn point is outside the logic stronghold map. point={point.name} " +
                    $"raw=({pointPositionFixed.x.RawValue},{pointPositionFixed.y.RawValue}).");
            }
            if (!teleportationStrongholdIds.Add(strongholdId))
                throw new InvalidOperationException($"Stronghold '{strongholdId}' has multiple Teleportation points.");

            s_DefendSpawnPoints.Add(new DefendSpawnPointRuntime
            {
                Position = pointPositionFixed,
                Weight = point.DefendSpawnWeight,
                Identifier = point.TeleportationId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name = string.IsNullOrWhiteSpace(point.name) ? "<unnamed>" : point.name,
                StrongholdId = strongholdId,
            });
        }

        s_DefendSpawnPoints.Sort(CompareDefendSpawnPoints);
        s_SpawnPointCacheConfigured = true;

        Log.Info(
            "[DefendPhaseTiming] stage=spawn-point-cache totalMs={0:F3} presetPoints={1} defendPoints={2} cached={3} diagnosticsMs={4:F3}",
            stopwatch.Elapsed.TotalMilliseconds,
            points.Length,
            defendPointCount,
            s_DefendSpawnPoints.Count,
            diagnosticsMs);
    }

    private static Fix64 CalculatePathDistanceFixed(FixVector2 from, FixVector2 to, UnitType unitType)
    {
        int agentTypeId = ResolveAgentTypeId(unitType);
        if (FlowFieldCrowdMovementSystem.TryEstimatePrewarmedNavigationDistanceToReachableGoalFixed(
                from,
                to,
                agentTypeId,
                out Fix64 distance,
                out _,
                out string failureReason))
        {
            return distance;
        }

        throw new InvalidOperationException(
            $"DefendPhaseRuntime.CalculatePathDistanceFixed failed: unit={unitType} fromRaw=({from.x.RawValue},{from.y.RawValue}) " +
            $"toRaw=({to.x.RawValue},{to.y.RawValue}) agentType={agentTypeId} reason={failureReason}");
    }

    private static void LogSpawnPointDiagnostics(EntityPresetPoint point)
    {
        if (point == null)
            return;

        string identifier = ResolvePreviewSpawnPointIdentifier(point);
        Vector3 position = point.Position;
        var positionFixed = new FixVector2((Fix64)position.x, (Fix64)position.z);
        string strongholdOwner = LogicStrongholdMap.TryResolveStrongholdId(positionFixed, out string strongholdId)
            ? LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId).ToString()
            : "null";

        int smallAgentTypeId = AgentTypeHelper.ResolveNavAgentTypeId(UnitSize.Small);
        bool flowSmall = FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
            position,
            smallAgentTypeId,
            (float)NavigationPointProbeRadius,
            0f,
            out Vector3 smallLegalPoint);
        string smallLegalPos = flowSmall ? smallLegalPoint.ToString() : "none";

        Log.Info(
            "[DefendPhase] SpawnPoint diag id={0} name={1} pos={2} weight={3} stronghold={4} flowSmall={5} flowSmallPos={6} smallAgentType={7}",
            identifier,
            point.name,
            position,
            point.DefendSpawnWeight,
            strongholdOwner,
            flowSmall,
            smallLegalPos,
            smallAgentTypeId);
    }

    private static void LogDefendSpawnEvent(PlannedSpawnEvent evt, LogicEntityId entityId)
    {
        int agentTypeId = ResolveAgentTypeId(evt.UnitType);
        Vector3 spawnPosition = new Vector3((float)evt.SpawnPosition.x, 0f, (float)evt.SpawnPosition.y);
        bool flowHit = FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
            spawnPosition,
            agentTypeId,
            (float)NavigationPointProbeRadius,
            0f,
            out Vector3 legalPoint);

        Log.Info(
            "[DefendPhase] SpawnEvent logicEntityId={0} unit={1} level={2} pos={3} speedProp={4:F2} speedRaw={5} point={6} stronghold={7} flowHit={8} flowPos={9} agentType={10} releaseWaypoint={11}",
            entityId.Value,
            evt.UnitType,
            evt.UnitLevel,
            spawnPosition,
            (float)evt.SpeedProperty,
            evt.SpeedProperty.RawValue,
            evt.SpawnPointName,
            evt.SourceStrongholdId ?? "null",
            flowHit,
            flowHit ? legalPoint.ToString() : "none",
            agentTypeId,
            evt.SpeedReleaseWaypointIndex);
    }

    private static int ResolveAgentTypeId(UnitType unitType)
    {
        return s_AgentTypeIdByUnitType.TryGetValue(unitType, out int agentTypeId)
            ? agentTypeId
            : throw new InvalidOperationException($"DefendPhaseRuntime has no prepared agent type for UnitType={unitType}.");
    }

    private static void ConfigureWaveRuntimeIfNeeded()
    {
        string levelIdentifier = ResolveCurrentLevelIdentifier();
        if (string.IsNullOrWhiteSpace(levelIdentifier))
            throw new InvalidOperationException("DefendPhaseRuntime cannot prepare waves without a level identifier.");

        if (s_WaveConfigConfigured
            && string.Equals(s_WaveConfigLevelIdentifier, levelIdentifier, StringComparison.Ordinal))
            return;

        s_WaveConfigLevelIdentifier = levelIdentifier;
        s_DefendRoutesByIdentifier.Clear();
        s_DefendAttackGroups.Clear();

        var routeTable = GF.DataTable.GetDataTable<DefendRouteTable>()
                         ?? throw new InvalidOperationException("DefendRouteTable is required for defense configuration.");
        var groupTable = GF.DataTable.GetDataTable<DefendAttackGroupTable>()
                         ?? throw new InvalidOperationException("DefendAttackGroupTable is required for defense configuration.");
        var pointsByTeleportationId = new Dictionary<string, DefendSpawnPointRuntime>(StringComparer.Ordinal);
        for (int i = 0; i < s_DefendSpawnPoints.Count; i++)
        {
            DefendSpawnPointRuntime point = s_DefendSpawnPoints[i];
            pointsByTeleportationId.Add(point.Identifier, point);
        }

        DefendRouteTable[] routeRows = routeTable.GetAllDataRows();
        for (int rowIndex = 0; rowIndex < routeRows.Length; rowIndex++)
        {
            DefendRouteTable row = routeRows[rowIndex];
            if (!string.Equals(row.LevelIdentifier, levelIdentifier, StringComparison.Ordinal))
                continue;
            if (string.IsNullOrWhiteSpace(row.Identifier))
                throw new InvalidOperationException($"Defend route row {row.Id} has an empty identifier.");
            if (string.IsNullOrWhiteSpace(row.SourceTeleportationId))
                throw new InvalidOperationException($"Defend route '{row.Identifier}' has an empty source teleportation id.");
            if (!pointsByTeleportationId.TryGetValue(row.SourceTeleportationId, out DefendSpawnPointRuntime sourcePoint))
                throw new InvalidOperationException($"Defend route '{row.Identifier}' source teleportation '{row.SourceTeleportationId}' has no matching point.");

            string[] waypointIds = row.WaypointTeleportationIds ?? Array.Empty<string>();
            var seenTeleportationIds = new HashSet<string>(StringComparer.Ordinal) { row.SourceTeleportationId };
            var waypointPositions = new FixVector2[waypointIds.Length];
            var waypointTeleportationIds = new string[waypointIds.Length];
            var waypointStrongholdIds = new string[waypointIds.Length];
            FixVector2 previousPosition = sourcePoint.Position;
            Fix64 firstWaypointDistance = Fix64.Zero;
            int presetFirstPlayerWaypointIndex = -1;
            for (int waypointIndex = 0; waypointIndex < waypointIds.Length; waypointIndex++)
            {
                string waypointId = waypointIds[waypointIndex];
                if (string.IsNullOrWhiteSpace(waypointId))
                    throw new InvalidOperationException($"Defend route '{row.Identifier}' contains an empty waypoint at index {waypointIndex}.");
                if (!seenTeleportationIds.Add(waypointId))
                    throw new InvalidOperationException($"Defend route '{row.Identifier}' repeats teleportation id '{waypointId}'.");
                if (!pointsByTeleportationId.TryGetValue(waypointId, out DefendSpawnPointRuntime waypointPoint))
                    throw new InvalidOperationException($"Defend route '{row.Identifier}' waypoint teleportation '{waypointId}' has no matching point.");

                waypointPositions[waypointIndex] = waypointPoint.Position;
                waypointTeleportationIds[waypointIndex] = waypointId;
                waypointStrongholdIds[waypointIndex] = waypointPoint.StrongholdId;
                Fix64 segmentDistance = FixVector2.Distance(previousPosition, waypointPoint.Position);
                if (waypointIndex == 0)
                    firstWaypointDistance = segmentDistance;
                previousPosition = waypointPoint.Position;
                if (presetFirstPlayerWaypointIndex < 0
                    && ResolveAuthoredStrongholdFactionId(waypointPoint.StrongholdId)
                    == EntitySideHelper.PlayerFactionId)
                {
                    presetFirstPlayerWaypointIndex = waypointIndex;
                }
            }

            bool usesInitialGameEndTarget = false;
            FixVector2 initialGameEndTargetPosition = FixVector2.Zero;
            if (presetFirstPlayerWaypointIndex < 0
                && LogicGameEndService.TryGetNearestPlayerInitialConditionBuilding(
                    previousPosition,
                    out IBuildingLogicContext initialGameEndTarget))
            {
                usesInitialGameEndTarget = true;
                initialGameEndTargetPosition = initialGameEndTarget.PositionFixed;
                if (waypointPositions.Length == 0)
                    firstWaypointDistance = FixVector2.Distance(sourcePoint.Position, initialGameEndTargetPosition);
            }

            var route = new DefendRouteDefinition
            {
                Identifier = row.Identifier,
                SourceStrongholdId = sourcePoint.StrongholdId,
                SourcePoint = sourcePoint,
                WaypointsFixed = waypointPositions,
                WaypointTeleportationIds = waypointTeleportationIds,
                WaypointStrongholdIds = waypointStrongholdIds,
                FirstWaypointDistance = firstWaypointDistance,
                PresetEngagementDistance = presetFirstPlayerWaypointIndex >= 0
                    ? CalculatePolylineDistance(sourcePoint.Position, waypointPositions, presetFirstPlayerWaypointIndex)
                    : usesInitialGameEndTarget
                        ? CalculatePolylineDistanceToTarget(
                            sourcePoint.Position,
                            waypointPositions,
                            initialGameEndTargetPosition)
                        : Fix64.Zero,
                PresetFirstPlayerWaypointIndex = presetFirstPlayerWaypointIndex,
                UsesInitialGameEndTarget = usesInitialGameEndTarget,
                InitialGameEndTargetPosition = initialGameEndTargetPosition
            };
            if (!s_DefendRoutesByIdentifier.TryAdd(route.Identifier, route))
                throw new InvalidOperationException($"Duplicate defend route identifier '{route.Identifier}'.");
        }

        var groupsByIdentifier = new Dictionary<string, DefendAttackGroupDefinition>(StringComparer.Ordinal);
        DefendAttackGroupTable[] groupRows = groupTable.GetAllDataRows();
        for (int rowIndex = 0; rowIndex < groupRows.Length; rowIndex++)
        {
            DefendAttackGroupTable row = groupRows[rowIndex];
            if (!string.Equals(row.LevelIdentifier, levelIdentifier, StringComparison.Ordinal))
                continue;
            if (string.IsNullOrWhiteSpace(row.Identifier))
                throw new InvalidOperationException($"Defend attack group row {row.Id} has an empty identifier.");
            if (row.ActiveDays == null || row.ActiveDays.Length == 0)
                throw new InvalidOperationException($"Defend attack group '{row.Identifier}' has no active days.");
            ValidateActiveDays(row.Identifier, row.ActiveDays);
            if (!s_DefendRoutesByIdentifier.TryGetValue(row.RouteIdentifier, out DefendRouteDefinition route))
                throw new InvalidOperationException($"Defend attack group '{row.Identifier}' references unknown route '{row.RouteIdentifier}'.");
            if (!UnitTypeHelper.TryParseUnitType(row.UnitIdentifier, out UnitType unitType))
                throw new InvalidOperationException($"Defend attack group '{row.Identifier}' has invalid unit '{row.UnitIdentifier}'.");
            if (row.InitialStrengthValue <= Fix64.Zero)
                throw new InvalidOperationException($"Defend attack group '{row.Identifier}' has non-positive initial strength value.");
            if (row.CountGrowthWeight < Fix64.Zero || row.CountGrowthWeight > Fix64.One)
                throw new InvalidOperationException($"Defend attack group '{row.Identifier}' has count growth weight outside zero to one.");
            if (row.StartDelaySeconds < Fix64.Zero || row.ExpectedEngagementSeconds <= Fix64.Zero)
            {
                throw new InvalidOperationException($"Defend attack group '{row.Identifier}' has invalid delay or expected engagement time.");
            }

            var group = new DefendAttackGroupDefinition
            {
                Identifier = row.Identifier,
                DefendRound = row.ActiveDays[0],
                ActiveDays = (int[])row.ActiveDays.Clone(),
                Route = route,
                UnitType = unitType,
                InitialStrengthValue = row.InitialStrengthValue,
                CountGrowthWeight = row.CountGrowthWeight,
                AfterGroupIdentifier = string.IsNullOrWhiteSpace(row.AfterGroupIdentifier) ? null : row.AfterGroupIdentifier,
                DelaySeconds = row.StartDelaySeconds,
                ExpectedEngagementSeconds = row.ExpectedEngagementSeconds
            };

            if (!groupsByIdentifier.TryAdd(group.Identifier, group))
                throw new InvalidOperationException($"Duplicate defend attack group identifier '{group.Identifier}'.");
            s_DefendAttackGroups.Add(group);
        }

        ValidateActivePredecessors(groupsByIdentifier);

        s_AgentTypeIdByUnitType.Clear();
        for (int groupIndex = 0; groupIndex < s_DefendAttackGroups.Count; groupIndex++)
        {
            UnitType unitType = s_DefendAttackGroups[groupIndex].UnitType;
            s_AgentTypeIdByUnitType[unitType] = AgentTypeHelper.ResolveNavAgentTypeId(unitType);
        }
        Fix64 minSpeedProperty = ResolveFiniteConfigFixed(DefendEnemyMinSpeedConfigKey, Fix64.One);
        Fix64 maxSpeedProperty = ResolveFiniteConfigFixed(DefendEnemyMaxSpeedConfigKey, Fix64.One);
        s_MinSpeedWorld = Fix64.Max(
            MinWorldSpeed,
            DistanceUnitConverter.ConvertToWorld(minSpeedProperty));
        s_MaxSpeedWorld = DistanceUnitConverter.ConvertToWorld(maxSpeedProperty);
        if (s_MaxSpeedWorld < s_MinSpeedWorld)
            throw new InvalidOperationException("DefendPhaseEnemyMaxSpeed must be greater than or equal to DefendPhaseEnemyMinSpeed.");
        s_SpawnIntervalSeconds = DistanceUnitConverter.ReadRequiredPositiveFixedConfig(
            DefendEnemySpawnIntervalConfigKey);
        s_WaypointArrivalRadiusWorld = DistanceUnitConverter.ConvertToWorld(
            DistanceUnitConverter.ReadRequiredPositiveFixedConfig(
                LogicUnitConfigurator.DefendRouteWaypointArrivalRadiusConfigKey));
        LevelTable level = LogicRuntimeDataTableCache.GetLevelRequired(levelIdentifier);
        if (level.ExpectedDays <= 0)
            throw new InvalidOperationException($"Level '{levelIdentifier}' has invalid expected days {level.ExpectedDays}.");
        EnemyStrengthRuntimeConfig.ValidateCurveOrder(level.ExpectedDays);

        for (int i = 0; i < s_DefendAttackGroups.Count; i++)
            ConfigureFixedSpawnWindow(s_DefendAttackGroups[i]);

        foreach (DefendAttackGroupDefinition group in s_DefendAttackGroups)
            ResolveGroupStartSeconds(group, groupsByIdentifier, new HashSet<string>(StringComparer.Ordinal));
        s_DefendAttackGroups.Sort(CompareAttackGroups);
        ConfigureNavigationDistancePrewarmRequests();
        s_WaveConfigConfigured = true;

        Log.Info("[DefendPhase] Defense routes loaded. level={0}, routes={1}, groups={2}",
            levelIdentifier,
            s_DefendRoutesByIdentifier.Count,
            s_DefendAttackGroups.Count);
    }

    private static void ConfigureNavigationDistancePrewarmRequests()
    {
        FlowFieldCrowdMovementSystem.ClearNavigationDistancePrewarmRequests();
        for (int groupIndex = 0; groupIndex < s_DefendAttackGroups.Count; groupIndex++)
        {
            DefendAttackGroupDefinition group = s_DefendAttackGroups[groupIndex];
            DefendRouteDefinition route = group.Route
                ?? throw new InvalidOperationException($"Defend group '{group.Identifier}' has no route for navigation prewarm.");
            int agentTypeId = ResolveAgentTypeId(group.UnitType);
            FixVector2 from = route.SourcePoint.Position;
            for (int waypointIndex = 0; waypointIndex < route.WaypointsFixed.Length; waypointIndex++)
            {
                FixVector2 to = route.WaypointsFixed[waypointIndex];
                FlowFieldCrowdMovementSystem.RequestNavigationDistancePrewarmFixed(from, to, agentTypeId);
                from = to;
            }
            if (route.UsesInitialGameEndTarget)
            {
                FlowFieldCrowdMovementSystem.RequestNavigationDistancePrewarmFixed(
                    from,
                    route.InitialGameEndTargetPosition,
                    agentTypeId);
            }
        }
    }

    private static string ResolveCurrentLevelIdentifier()
    {
        return LevelSelectionService.SelectedLevelIdentifier;
    }

    private static int ResolveAuthoredStrongholdFactionId(string strongholdId)
    {
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new ArgumentException("Stronghold ID is empty.", nameof(strongholdId));
        string[] parts = strongholdId.Split('_');
        if (parts.Length != 3 || !string.Equals(parts[0], "SH", StringComparison.Ordinal)
                              || !int.TryParse(parts[1], out int factionId))
        {
            throw new InvalidOperationException(
                $"Stronghold '{strongholdId}' does not use the authored SH_faction_index format.");
        }
        return factionId;
    }

    private static Fix64 CalculatePolylineDistance(
        FixVector2 source,
        IReadOnlyList<FixVector2> waypoints,
        int finalWaypointIndex)
    {
        if (waypoints == null)
            throw new ArgumentNullException(nameof(waypoints));
        if (finalWaypointIndex < 0 || finalWaypointIndex >= waypoints.Count)
            throw new ArgumentOutOfRangeException(nameof(finalWaypointIndex));

        Fix64 distance = Fix64.Zero;
        FixVector2 previous = source;
        for (int i = 0; i <= finalWaypointIndex; i++)
        {
            distance += FixVector2.Distance(previous, waypoints[i]);
            previous = waypoints[i];
        }
        return distance;
    }

    private static Fix64 CalculatePolylineDistanceToTarget(
        FixVector2 source,
        IReadOnlyList<FixVector2> waypoints,
        FixVector2 target)
    {
        if (waypoints == null)
            throw new ArgumentNullException(nameof(waypoints));
        Fix64 distance = Fix64.Zero;
        FixVector2 previous = source;
        for (int i = 0; i < waypoints.Count; i++)
        {
            distance += FixVector2.Distance(previous, waypoints[i]);
            previous = waypoints[i];
        }
        return distance + FixVector2.Distance(previous, target);
    }

    private static void ConfigureFixedSpawnWindow(DefendAttackGroupDefinition group)
    {
        DefendRouteDefinition route = group.Route
            ?? throw new InvalidOperationException($"Defend group '{group.Identifier}' has no route.");
        if (route.PresetFirstPlayerWaypointIndex < 0 && !route.UsesInitialGameEndTarget)
        {
            throw new InvalidOperationException(
                $"Defend route '{route.Identifier}' has no authored player waypoint or initial player GameEnd target for fixed wave spawn scheduling.");
        }
        Fix64 shortestEngagementDistance = route.FirstWaypointDistance - s_WaypointArrivalRadiusWorld;
        Fix64 presetEngagementDistance = route.PresetEngagementDistance - s_WaypointArrivalRadiusWorld;
        if (shortestEngagementDistance <= Fix64.Zero || presetEngagementDistance <= Fix64.Zero)
            throw new InvalidOperationException($"Defend route '{route.Identifier}' has a non-positive preset distance.");

        Fix64 longestRouteAtMaxSpeed = presetEngagementDistance / s_MaxSpeedWorld;
        Fix64 shortestRouteAtMinSpeed = shortestEngagementDistance / s_MinSpeedWorld;
        group.FirstSpawnLeadSeconds = Fix64.Max(longestRouteAtMaxSpeed, shortestRouteAtMinSpeed);
        group.LastSpawnLeadSeconds = Fix64.Min(longestRouteAtMaxSpeed, shortestRouteAtMinSpeed);
        if (group.LastSpawnLeadSeconds <= Fix64.Zero
            || group.ExpectedEngagementSeconds <= group.FirstSpawnLeadSeconds)
        {
            throw new InvalidOperationException(
                $"Defend group '{group.Identifier}' expected engagement time must exceed its preset first-spawn lead. " +
                $"expectedRaw={group.ExpectedEngagementSeconds.RawValue}, leadRaw={group.FirstSpawnLeadSeconds.RawValue}.");
        }
    }

    private static Fix64 ResolveGroupStartSeconds(
        DefendAttackGroupDefinition group,
        IReadOnlyDictionary<string, DefendAttackGroupDefinition> groupsByIdentifier,
        HashSet<string> visiting)
    {
        if (group.StartResolved)
            return group.StartSeconds;
        if (!visiting.Add(group.Identifier))
            throw new InvalidOperationException($"Defend attack group dependency cycle contains '{group.Identifier}'.");

        Fix64 start = group.DelaySeconds;
        if (!string.IsNullOrWhiteSpace(group.AfterGroupIdentifier))
        {
            if (!groupsByIdentifier.TryGetValue(group.AfterGroupIdentifier, out DefendAttackGroupDefinition predecessor))
                throw new InvalidOperationException($"Defend attack group '{group.Identifier}' references unknown predecessor '{group.AfterGroupIdentifier}'.");
            start += ResolveGroupStartSeconds(predecessor, groupsByIdentifier, visiting)
                     + GetFixedSpawnWindowEndOffset(predecessor);
        }

        visiting.Remove(group.Identifier);
        group.StartSeconds = start;
        group.StartResolved = true;
        return start;
    }

    private static Fix64 GetFixedSpawnWindowEndOffset(DefendAttackGroupDefinition group)
    {
        Fix64 offset = group.ExpectedEngagementSeconds - group.LastSpawnLeadSeconds;
        if (offset < Fix64.Zero)
            throw new InvalidOperationException($"Defend group '{group.Identifier}' has an invalid fixed spawn window end.");
        return offset;
    }

    private static int CompareAttackGroups(DefendAttackGroupDefinition left, DefendAttackGroupDefinition right)
    {
        int round = left.DefendRound.CompareTo(right.DefendRound);
        if (round != 0)
            return round;
        int start = left.StartSeconds.CompareTo(right.StartSeconds);
        return start != 0 ? start : string.CompareOrdinal(left.Identifier, right.Identifier);
    }

    private static void ValidateActiveDays(string identifier, IReadOnlyList<int> activeDays)
    {
        var seen = new HashSet<int>();
        int previous = 0;
        for (int i = 0; i < activeDays.Count; i++)
        {
            int day = activeDays[i];
            if (day <= 0 || !seen.Add(day) || day <= previous)
                throw new InvalidOperationException($"Defend attack group '{identifier}' active days must be positive, unique, and ascending.");
            previous = day;
        }
    }

    private static void ValidateActivePredecessors(
        IReadOnlyDictionary<string, DefendAttackGroupDefinition> groupsByIdentifier)
    {
        foreach (DefendAttackGroupDefinition group in groupsByIdentifier.Values)
        {
            if (string.IsNullOrWhiteSpace(group.AfterGroupIdentifier))
                continue;
            if (!groupsByIdentifier.TryGetValue(group.AfterGroupIdentifier, out DefendAttackGroupDefinition predecessor))
                throw new InvalidOperationException($"Defend attack group '{group.Identifier}' references unknown predecessor '{group.AfterGroupIdentifier}'.");
            for (int i = 0; i < group.ActiveDays.Length; i++)
            {
                if (!ContainsDay(predecessor.ActiveDays, group.ActiveDays[i]))
                {
                    throw new InvalidOperationException(
                        $"Defend attack group '{group.Identifier}' is active on day {group.ActiveDays[i]} but predecessor " +
                        $"'{predecessor.Identifier}' is not.");
                }
            }
        }
    }

    private static bool ContainsDay(IReadOnlyList<int> activeDays, int day)
    {
        for (int i = 0; i < activeDays.Count; i++)
        {
            if (activeDays[i] == day)
                return true;
        }
        return false;
    }

    private static IReadOnlyList<EnemySquadCompositionEntry> ResolveSquadComposition(
        UnitType unitType,
        Fix64 initialStrengthValue,
        Fix64 countGrowthWeight,
        int day,
        int expectedDays,
        Fix64 initialStrengthScale,
        Fix64 growthSpeedScale,
        EnemyStrengthRuntimeSettings settings)
    {
        BuildingTable building = LogicRuntimeDataTableCache.GetArmyBuilding(unitType)
            ?? throw new InvalidOperationException($"Cannot resolve army building for enemy unit '{unitType}'.");
        EnemyUnitStrengthValueResolver.Result unitValues = EnemyUnitStrengthValueResolver.ResolveFromArmyBuilding(
            building,
            settings.LevelTwoValueScale,
            settings.LevelThreeValueScale);
        return EnemySquadStrengthResolver.Resolve(
            initialStrengthValue,
            countGrowthWeight,
            day,
            expectedDays,
            initialStrengthScale,
            growthSpeedScale,
            unitValues.EffectiveValues,
            settings.Curve,
            settings.SquadValue);
    }

    private static List<DefendAttackGroupDefinition> ResolveAttackGroupsForRound(int roundIndex)
    {
        var result = new List<DefendAttackGroupDefinition>();
        if (roundIndex <= 0)
            return result;

        LevelTable level = LogicRuntimeDataTableCache.GetLevelRequired(ResolveCurrentLevelIdentifier());
        if (level.ExpectedDays <= 0)
            throw new InvalidOperationException($"Level '{level.Identifier}' has invalid expected days {level.ExpectedDays}.");
        EnemyStrengthRuntimeSettings strengthSettings = EnemyStrengthRuntimeConfig.Read(EnemyStrengthContext.DefenseWave);
        Fix64 initialStrengthScale = LevelTagRuntime.GetEnemyInitialStrengthScale(EnemyStrengthContext.DefenseWave);
        Fix64 growthSpeedScale = LevelTagRuntime.GetEnemyGrowthSpeedScale(EnemyStrengthContext.DefenseWave);
        for (int i = 0; i < s_DefendAttackGroups.Count; i++)
        {
            DefendAttackGroupDefinition source = s_DefendAttackGroups[i];
            if (!ContainsDay(source.ActiveDays, roundIndex))
                continue;
            var clone = new DefendAttackGroupDefinition
            {
                Identifier = source.Identifier,
                DefendRound = roundIndex,
                ActiveDays = source.ActiveDays,
                Route = source.Route,
                UnitType = source.UnitType,
                InitialStrengthValue = source.InitialStrengthValue,
                CountGrowthWeight = source.CountGrowthWeight,
                AfterGroupIdentifier = source.AfterGroupIdentifier,
                DelaySeconds = source.DelaySeconds,
                ExpectedEngagementSeconds = source.ExpectedEngagementSeconds,
                FirstSpawnLeadSeconds = source.FirstSpawnLeadSeconds,
                LastSpawnLeadSeconds = source.LastSpawnLeadSeconds,
                StartSeconds = source.StartSeconds,
                StartResolved = true
            };
            IReadOnlyList<EnemySquadCompositionEntry> composition = ResolveSquadComposition(
                source.UnitType,
                source.InitialStrengthValue,
                source.CountGrowthWeight,
                roundIndex,
                level.ExpectedDays,
                initialStrengthScale,
                growthSpeedScale,
                strengthSettings);
            for (int entryIndex = 0; entryIndex < composition.Count; entryIndex++)
            {
                EnemySquadCompositionEntry entry = composition[entryIndex];
                clone.Enemies.Add(new DefendAttackEntry
                {
                    UnitType = source.UnitType,
                    UnitLevel = entry.Level,
                    Count = entry.Count
                });
            }
            result.Add(clone);
        }

        return result;
    }

    private static List<PlannedSpawnEvent> BuildSpawnEvents(IReadOnlyList<DefendAttackGroupDefinition> groups)
    {
        var events = new List<PlannedSpawnEvent>();
        var candidates = new List<WaveSpawnCandidate>();
        var distanceByRouteUnitAndWaypoint = new Dictionary<string, EngagementPathEstimate>(StringComparer.Ordinal);
        if (groups == null || groups.Count == 0 || s_DefendSpawnPoints.Count == 0)
            return events;

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            DefendAttackGroupDefinition group = groups[groupIndex];
            Fix64 earliestSpawnSeconds = group.StartSeconds
                                         + group.ExpectedEngagementSeconds
                                         - group.FirstSpawnLeadSeconds;
            Fix64 latestSpawnSeconds = group.StartSeconds
                                       + group.ExpectedEngagementSeconds
                                       - group.LastSpawnLeadSeconds;
            if (earliestSpawnSeconds < Fix64.Zero || latestSpawnSeconds < earliestSpawnSeconds)
                throw new InvalidOperationException($"Defend group '{group.Identifier}' has an invalid fixed spawn window.");

            int ordinalInGroup = 0;
            for (int entryIndex = 0; entryIndex < group.Enemies.Count; entryIndex++)
            {
                DefendAttackEntry entry = group.Enemies[entryIndex];
                for (int spawnIndex = 0; spawnIndex < entry.Count; spawnIndex++)
                {
                    candidates.Add(new WaveSpawnCandidate
                    {
                        Group = group,
                        Entry = entry,
                        EarliestSpawnSeconds = earliestSpawnSeconds,
                        LatestSpawnSeconds = latestSpawnSeconds,
                        OrdinalInGroup = ordinalInGroup++,
                        StableSequence = candidates.Count
                    });
                }
            }
        }

        ScheduleWaveSpawnCandidates(candidates, s_SpawnIntervalSeconds);
        candidates.Sort((left, right) =>
        {
            int time = left.ScheduledSpawnSeconds.CompareTo(right.ScheduledSpawnSeconds);
            return time != 0 ? time : left.StableSequence.CompareTo(right.StableSequence);
        });
        for (int i = 0; i < candidates.Count; i++)
        {
            AddPlannedSpawnEvent(
                candidates[i],
                candidates[i].ScheduledSpawnSeconds,
                distanceByRouteUnitAndWaypoint,
                events);
        }

        return events;
    }

    private static void ScheduleWaveSpawnCandidates(
        IReadOnlyList<WaveSpawnCandidate> candidates,
        Fix64 spawnIntervalSeconds)
    {
        if (spawnIntervalSeconds <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(spawnIntervalSeconds));
        var pending = new List<WaveSpawnCandidate>(candidates);
        Fix64 nextSpawnSeconds = Fix64.Zero;
        if (pending.Count > 0)
        {
            nextSpawnSeconds = pending[0].EarliestSpawnSeconds;
            for (int i = 1; i < pending.Count; i++)
                nextSpawnSeconds = Fix64.Min(nextSpawnSeconds, pending[i].EarliestSpawnSeconds);
        }
        while (pending.Count > 0)
        {
            int selectedIndex = SelectNextWaveSpawnCandidate(pending, nextSpawnSeconds);
            if (selectedIndex < 0)
            {
                nextSpawnSeconds = pending[0].EarliestSpawnSeconds;
                for (int i = 1; i < pending.Count; i++)
                    nextSpawnSeconds = Fix64.Min(nextSpawnSeconds, pending[i].EarliestSpawnSeconds);
                selectedIndex = SelectNextWaveSpawnCandidate(pending, nextSpawnSeconds);
            }
            if (selectedIndex < 0)
                throw new InvalidOperationException("Defense wave scheduler could not select an available spawn candidate.");

            WaveSpawnCandidate candidate = pending[selectedIndex];
            pending.RemoveAt(selectedIndex);
            if (nextSpawnSeconds > candidate.LatestSpawnSeconds)
            {
                throw new InvalidOperationException(
                    $"Defense wave cannot keep the global spawn interval inside group '{candidate.Group.Identifier}' fixed window. " +
                    $"scheduledRaw={nextSpawnSeconds.RawValue}, latestRaw={candidate.LatestSpawnSeconds.RawValue}.");
            }

            candidate.ScheduledSpawnSeconds = nextSpawnSeconds;
            nextSpawnSeconds += spawnIntervalSeconds;
        }
    }

    private static int SelectNextWaveSpawnCandidate(
        IReadOnlyList<WaveSpawnCandidate> candidates,
        Fix64 spawnSeconds)
    {
        int selectedIndex = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            WaveSpawnCandidate candidate = candidates[i];
            if (candidate.EarliestSpawnSeconds > spawnSeconds)
                continue;
            if (selectedIndex < 0 || CompareWaveSpawnCandidates(candidate, candidates[selectedIndex]) < 0)
                selectedIndex = i;
        }
        return selectedIndex;
    }

    private static int CompareWaveSpawnCandidates(WaveSpawnCandidate left, WaveSpawnCandidate right)
    {
        int deadline = left.LatestSpawnSeconds.CompareTo(right.LatestSpawnSeconds);
        if (deadline != 0)
            return deadline;
        int ordinal = left.OrdinalInGroup.CompareTo(right.OrdinalInGroup);
        if (ordinal != 0)
            return ordinal;
        int group = string.CompareOrdinal(left.Group.Identifier, right.Group.Identifier);
        return group != 0 ? group : left.StableSequence.CompareTo(right.StableSequence);
    }

    private static void AddPlannedSpawnEvent(
        WaveSpawnCandidate candidate,
        Fix64 spawnSeconds,
        IDictionary<string, EngagementPathEstimate> distanceCache,
        ICollection<PlannedSpawnEvent> events)
    {
        DefendAttackGroupDefinition group = candidate.Group;
        int speedReleaseWaypointIndex = TryResolveFirstPlayerWaypointIndex(
            group.Route.Identifier,
            group.Route.WaypointStrongholdIds);
        if (speedReleaseWaypointIndex < 0 && !group.Route.UsesInitialGameEndTarget)
        {
            throw new InvalidOperationException(
                $"Active defend route '{group.Route.Identifier}' has no player-owned waypoint or initial player GameEnd target for engagement speed estimation.");
        }
        FixVector2 dynamicTargetPosition = group.Route.InitialGameEndTargetPosition;
        string distanceCacheKey = speedReleaseWaypointIndex >= 0
            ? $"{group.Route.Identifier}|{(int)candidate.Entry.UnitType}|WP:{speedReleaseWaypointIndex}"
            : $"{group.Route.Identifier}|{(int)candidate.Entry.UnitType}|TARGET:{dynamicTargetPosition.x.RawValue}:{dynamicTargetPosition.y.RawValue}";
        if (!distanceCache.TryGetValue(distanceCacheKey, out EngagementPathEstimate pathEstimate))
        {
            pathEstimate = speedReleaseWaypointIndex >= 0
                ? new EngagementPathEstimate(
                    CalculateRouteDistanceToWaypoint(
                        group.Route.Identifier,
                        group.Route.SourcePoint.Position,
                        group.Route.WaypointsFixed,
                        candidate.Entry.UnitType,
                        speedReleaseWaypointIndex),
                    null)
                : CalculateRouteDistanceToTarget(
                    group.Route.Identifier,
                    group.Route.SourcePoint.Position,
                    group.Route.WaypointsFixed,
                    dynamicTargetPosition,
                    candidate.Entry.UnitType);
            distanceCache.Add(distanceCacheKey, pathEstimate);
        }

        Fix64 engagementSeconds = group.StartSeconds + group.ExpectedEngagementSeconds;
        if (engagementSeconds <= spawnSeconds)
        {
            throw new InvalidOperationException(
                $"Defend group '{group.Identifier}' scheduled a unit at or after its expected engagement time.");
        }
        ulong requestTicks = SecondsToTicksCeiling(spawnSeconds);
        ulong expectedEngagementTicks = SecondsToTicksCeiling(engagementSeconds);
        if (expectedEngagementTicks <= requestTicks)
            throw new InvalidOperationException($"Defend group '{group.Identifier}' has no travel ticks after scheduling.");
        Fix64 remainingTravelSeconds = TicksToDuration(expectedEngagementTicks - requestTicks);
        Fix64 speedWorld = CalculateExpectedEngagementSpeed(pathEstimate.Distance, remainingTravelSeconds);
        Fix64 speedProperty = DistanceUnitConverter.ConvertFromWorld(speedWorld, s_DistanceConversionRate);
        events.Add(new PlannedSpawnEvent
        {
            RequestFrameOffset = requestTicks,
            Sequence = checked((ulong)candidate.StableSequence + 1UL),
            UnitType = candidate.Entry.UnitType,
            UnitLevel = candidate.Entry.UnitLevel,
            SpawnPosition = group.Route.SourcePoint.Position,
            RouteIdentifier = group.Route.Identifier,
            SpeedProperty = speedProperty,
            SourceStrongholdId = group.Route.SourceStrongholdId,
            SpawnPointName = group.Route.SourcePoint.Name,
            RouteWaypointsFixed = group.Route.WaypointsFixed,
            RouteWaypointTeleportationIds = group.Route.WaypointTeleportationIds,
            ExpectedEngagementFrameOffset = expectedEngagementTicks,
            SpeedReleaseWaypointIndex = speedReleaseWaypointIndex,
            SpeedReleasePositionFixed = pathEstimate.ReleasePosition
        });
    }

    private static Fix64 CalculateExpectedEngagementSpeed(
        Fix64 engagementDistance,
        Fix64 remainingTravelSeconds)
    {
        if (engagementDistance <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(engagementDistance));
        if (remainingTravelSeconds <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(remainingTravelSeconds));
        return engagementDistance / remainingTravelSeconds;
    }

    private static int TryResolveFirstPlayerWaypointIndex(
        string routeIdentifier,
        IReadOnlyList<string> waypointStrongholdIds)
    {
        if (string.IsNullOrWhiteSpace(routeIdentifier))
            throw new ArgumentException("Defense route identifier is empty.", nameof(routeIdentifier));
        if (waypointStrongholdIds == null)
            throw new ArgumentNullException(nameof(waypointStrongholdIds));
        for (int i = 0; i < waypointStrongholdIds.Count; i++)
        {
            if (LogicStrongholdMap.GetOwnerFactionIdRequired(waypointStrongholdIds[i])
                == EntitySideHelper.PlayerFactionId)
            {
                return i;
            }
        }

        return -1;
    }

    private static Fix64 CalculateRouteDistanceToWaypoint(
        string routeIdentifier,
        FixVector2 sourcePosition,
        IReadOnlyList<FixVector2> waypoints,
        UnitType unitType,
        int waypointIndex)
    {
        if (string.IsNullOrWhiteSpace(routeIdentifier))
            throw new ArgumentException("Defense route identifier is empty.", nameof(routeIdentifier));
        if (waypoints == null)
            throw new ArgumentNullException(nameof(waypoints));
        if (waypointIndex < 0 || waypointIndex >= waypoints.Count)
            throw new ArgumentOutOfRangeException(nameof(waypointIndex), waypointIndex, "Defense engagement waypoint is outside the route.");

        FixVector2 from = sourcePosition;
        Fix64 distance = Fix64.Zero;
        for (int i = 0; i <= waypointIndex; i++)
        {
            FixVector2 to = waypoints[i];
            distance += CalculatePathDistanceFixed(from, to, unitType);
            from = to;
        }
        distance -= s_WaypointArrivalRadiusWorld;
        if (distance <= Fix64.Zero)
            throw new InvalidOperationException($"Defend route '{routeIdentifier}' resolved a non-positive engagement distance.");
        return distance;
    }

    private static EngagementPathEstimate CalculateRouteDistanceToTarget(
        string routeIdentifier,
        FixVector2 sourcePosition,
        IReadOnlyList<FixVector2> waypoints,
        FixVector2 targetPosition,
        UnitType unitType)
    {
        if (waypoints == null)
            throw new ArgumentNullException(nameof(waypoints));
        FixVector2 from = sourcePosition;
        Fix64 distance = Fix64.Zero;
        for (int i = 0; i < waypoints.Count; i++)
        {
            distance += CalculatePathDistanceFixed(from, waypoints[i], unitType);
            from = waypoints[i];
        }
        int agentTypeId = ResolveAgentTypeId(unitType);
        if (!FlowFieldCrowdMovementSystem.TryEstimatePrewarmedNavigationDistanceToReachableGoalFixed(
                from,
                targetPosition,
                agentTypeId,
                out Fix64 finalDistance,
                out FixVector2 reachableGoal,
                out string failureReason))
        {
            throw new InvalidOperationException(
                $"Defend route '{routeIdentifier}' final target navigation distance failed: {failureReason}");
        }
        distance += finalDistance - s_WaypointArrivalRadiusWorld;
        if (distance <= Fix64.Zero)
            throw new InvalidOperationException($"Defend route '{routeIdentifier}' resolved a non-positive final target engagement distance.");
        return new EngagementPathEstimate(distance, reachableGoal);
    }

    private static bool IsAttackGroupSourceActive(string sourceStrongholdId)
    {
        if (string.IsNullOrWhiteSpace(sourceStrongholdId))
            throw new ArgumentException("Defense attack group source stronghold ID is empty.", nameof(sourceStrongholdId));
        return LogicStrongholdMap.GetOwnerFactionIdRequired(sourceStrongholdId)
               != EntitySideHelper.PlayerFactionId;
    }

    private static Fix64 ResolveFiniteConfigFixed(string key, Fix64 minimum)
    {
        return Fix64.Max(minimum, DistanceUnitConverter.ReadRequiredPositiveFixedConfig(key));
    }

    private static ulong SecondsToTicksCeiling(Fix64 duration)
    {
        if (duration < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), duration.RawValue, "Duration cannot be negative.");
        long ticks = (long)Fix64.Ceiling(duration / LogicFrameRuntime.FixedDeltaTime);
        return checked((ulong)ticks);
    }

    private static Fix64 TicksToDuration(ulong ticks)
    {
        if (ticks == 0 || ticks > long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Tick duration must fit a positive Int64.");
        return LogicFrameRuntime.FixedDeltaTime * (Fix64)(long)ticks;
    }

    private static int CompareDefendSpawnPoints(DefendSpawnPointRuntime left, DefendSpawnPointRuntime right)
    {
        int identifier = string.CompareOrdinal(left.Identifier, right.Identifier);
        if (identifier != 0)
            return identifier;
        int x = left.Position.x.RawValue.CompareTo(right.Position.x.RawValue);
        if (x != 0)
            return x;
        int y = left.Position.y.RawValue.CompareTo(right.Position.y.RawValue);
        if (y != 0)
            return y;
        int stronghold = string.CompareOrdinal(left.StrongholdId, right.StrongholdId);
        if (stronghold != 0)
            return stronghold;
        int name = string.CompareOrdinal(left.Name, right.Name);
        return name != 0 ? name : left.Weight.CompareTo(right.Weight);
    }

    private static int ComparePlannedSpawnEvents(PlannedSpawnEvent left, PlannedSpawnEvent right)
    {
        int frame = left.RequestFrameOffset.CompareTo(right.RequestFrameOffset);
        return frame != 0 ? frame : left.Sequence.CompareTo(right.Sequence);
    }

    private static int RoundPositiveRatioToInt(long numerator, long denominator)
    {
        if (numerator < 0)
            throw new ArgumentOutOfRangeException(nameof(numerator));
        if (denominator <= 0)
            throw new ArgumentOutOfRangeException(nameof(denominator));

        long whole = numerator / denominator;
        long remainder = numerator % denominator;
        long twiceRemainder = checked(remainder * 2L);
        if (twiceRemainder > denominator || (twiceRemainder == denominator && (whole & 1L) != 0L))
            whole++;
        return checked((int)whole);
    }

    private static string ResolvePreviewSpawnPointIdentifier(EntityPresetPoint point)
    {
        if (point == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(point.Identifier))
            return point.Identifier;

        return string.IsNullOrWhiteSpace(point.name) ? string.Empty : point.name;
    }

    private static void RequirePreparedRuntime()
    {
        if (!s_SubscribedLogicUnitDead
            || !s_SubscribedNavigationDistancePrewarm
            || !s_ArchetypeCacheConfigured
            || !s_SpawnPointCacheConfigured
            || !s_WaveConfigConfigured)
        {
            throw new InvalidOperationException(
                "DefendPhaseRuntime was not prepared before entering the logic Defend phase.");
        }
        if (s_MinSpeedWorld <= Fix64.Zero
            || s_MaxSpeedWorld < s_MinSpeedWorld
            || s_SpawnIntervalSeconds <= Fix64.Zero
            || s_WaypointArrivalRadiusWorld <= Fix64.Zero)
        {
            throw new InvalidOperationException("DefendPhaseRuntime prepared config contains a non-positive value.");
        }
    }

    private static void ResetDefendPhaseState(bool keepRoundIndex)
    {
        s_AliveEnemyLogicEntityIds.Clear();
        s_AcceleratedEnemyLogicEntityIds.Clear();
        s_PlannedSpawnEvents.Clear();
        s_NextPlannedSpawnIndex = 0;
        s_SpawnRequestStartFrame = 0;
        s_SpawnScheduleCompleted = false;
        s_WaitingForNavigationDistancePrewarm = false;
        s_TutorialFirstDefenseWaiting = false;
        s_TutorialFirstDefenseActive = false;
        if (!keepRoundIndex)
            s_TutorialFirstDefenseConsumed = false;
        if (!keepRoundIndex)
            s_DefendRoundIndex = 0;
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add(s_DefendRoundIndex);
        hasher.Add(s_SpawnScheduleCompleted);
        hasher.Add(s_WaitingForNavigationDistancePrewarm);
        hasher.Add(s_SpawnRequestStartFrame);
        hasher.Add(s_NextPlannedSpawnIndex);
        hasher.Add(s_PlannedSpawnEvents.Count);
        for (int i = 0; i < s_PlannedSpawnEvents.Count; i++)
        {
            PlannedSpawnEvent evt = s_PlannedSpawnEvents[i];
            hasher.Add(evt.RequestFrameOffset);
            hasher.Add(evt.Sequence);
            hasher.Add((int)evt.UnitType);
            hasher.Add(evt.UnitLevel);
            hasher.Add(evt.SpawnPosition.x.RawValue);
            hasher.Add(evt.SpawnPosition.y.RawValue);
            hasher.Add(evt.RouteIdentifier);
            hasher.Add(evt.SpeedProperty.RawValue);
            hasher.Add(evt.SourceStrongholdId);
            hasher.Add(evt.SpawnPointName);
            hasher.Add(evt.ExpectedEngagementFrameOffset);
            hasher.Add(evt.SpeedReleaseWaypointIndex);
            if (evt.SpeedReleasePositionFixed.HasValue)
            {
                hasher.Add(true);
                hasher.Add(evt.SpeedReleasePositionFixed.Value.x.RawValue);
                hasher.Add(evt.SpeedReleasePositionFixed.Value.y.RawValue);
            }
            else
            {
                hasher.Add(false);
            }
            int routeCount = evt.RouteWaypointsFixed?.Length ?? 0;
            hasher.Add(routeCount);
            for (int routeIndex = 0; routeIndex < routeCount; routeIndex++)
            {
                hasher.Add(evt.RouteWaypointsFixed[routeIndex].x.RawValue);
                hasher.Add(evt.RouteWaypointsFixed[routeIndex].y.RawValue);
                hasher.Add(evt.RouteWaypointTeleportationIds[routeIndex]);
            }
        }

        s_DeterministicAliveEnemyIds.Clear();
        foreach (int entityId in s_AliveEnemyLogicEntityIds)
            s_DeterministicAliveEnemyIds.Add(entityId);
        s_DeterministicAliveEnemyIds.Sort(s_DeterministicEnemyIdComparison);
        hasher.Add(s_DeterministicAliveEnemyIds.Count);
        for (int i = 0; i < s_DeterministicAliveEnemyIds.Count; i++)
            hasher.Add(s_DeterministicAliveEnemyIds[i]);

        s_DeterministicAcceleratedEnemyIds.Clear();
        foreach (int entityId in s_AcceleratedEnemyLogicEntityIds)
            s_DeterministicAcceleratedEnemyIds.Add(entityId);
        s_DeterministicAcceleratedEnemyIds.Sort(s_DeterministicEnemyIdComparison);
        hasher.Add(s_DeterministicAcceleratedEnemyIds.Count);
        for (int i = 0; i < s_DeterministicAcceleratedEnemyIds.Count; i++)
            hasher.Add(s_DeterministicAcceleratedEnemyIds[i]);
    }

#if UNITY_EDITOR
    public static bool GetEditorTestIsWaitingForNavigationDistancePrewarm()
    {
        return s_WaitingForNavigationDistancePrewarm;
    }

    public static int GetEditorTestPlannedSpawnCount()
    {
        return s_PlannedSpawnEvents.Count;
    }

    public static int GetEditorTestAliveEnemyCount()
    {
        return s_AliveEnemyLogicEntityIds.Count;
    }

    public static ulong GetEditorTestSpawnRequestStartFrame()
    {
        return s_SpawnRequestStartFrame;
    }

    public static bool GetEditorTestIsAttackGroupSourceActive(string sourceStrongholdId)
    {
        return IsAttackGroupSourceActive(sourceStrongholdId);
    }

    public static ulong GetEditorTestTickCount(Fix64 duration)
    {
        return SecondsToTicksCeiling(duration);
    }

    public static int GetEditorTestWeightedSpawnCount(Fix64 weight, Fix64 totalWeight, int totalCount)
    {
        return RoundPositiveRatioToInt(checked(weight.RawValue * totalCount), totalWeight.RawValue);
    }

    public static Fix64[] GetEditorTestWaveSpawnSeconds(
        Fix64[] earliestSpawnSeconds,
        Fix64[] latestSpawnSeconds,
        string[] groupIdentifiers,
        int[] ordinalsInGroup,
        Fix64 spawnIntervalSeconds)
    {
        if (earliestSpawnSeconds == null || latestSpawnSeconds == null
            || groupIdentifiers == null || ordinalsInGroup == null)
        {
            throw new ArgumentNullException("Wave spawn test inputs cannot be null.");
        }
        int count = earliestSpawnSeconds.Length;
        if (latestSpawnSeconds.Length != count || groupIdentifiers.Length != count || ordinalsInGroup.Length != count)
            throw new ArgumentException("Wave spawn test input lengths are mismatched.");

        var candidates = new List<WaveSpawnCandidate>(count);
        for (int i = 0; i < count; i++)
        {
            candidates.Add(new WaveSpawnCandidate
            {
                Group = new DefendAttackGroupDefinition { Identifier = groupIdentifiers[i] },
                EarliestSpawnSeconds = earliestSpawnSeconds[i],
                LatestSpawnSeconds = latestSpawnSeconds[i],
                OrdinalInGroup = ordinalsInGroup[i],
                StableSequence = i
            });
        }
        ScheduleWaveSpawnCandidates(candidates, spawnIntervalSeconds);
        var result = new Fix64[count];
        for (int i = 0; i < count; i++)
            result[i] = candidates[i].ScheduledSpawnSeconds;
        return result;
    }

    public static Fix64 GetEditorTestExpectedEngagementSpeed(
        Fix64 engagementDistance,
        Fix64 remainingTravelSeconds)
    {
        return CalculateExpectedEngagementSpeed(engagementDistance, remainingTravelSeconds);
    }

    public static int GetEditorTestFirstPlayerWaypointIndex(
        string routeIdentifier,
        string[] waypointStrongholdIds)
    {
        return TryResolveFirstPlayerWaypointIndex(routeIdentifier, waypointStrongholdIds);
    }
#endif

    private sealed class DefendSpawnPointRuntime
    {
        public FixVector2 Position;
        public Fix64 Weight;
        public string Identifier;
        public string Name;
        public string StrongholdId;
    }

    private sealed class DefendRouteDefinition
    {
        public string Identifier;
        public string SourceStrongholdId;
        public DefendSpawnPointRuntime SourcePoint;
        public FixVector2[] WaypointsFixed;
        public string[] WaypointTeleportationIds;
        public string[] WaypointStrongholdIds;
        public Fix64 FirstWaypointDistance;
        public Fix64 PresetEngagementDistance;
        public int PresetFirstPlayerWaypointIndex;
        public bool UsesInitialGameEndTarget;
        public FixVector2 InitialGameEndTargetPosition;
    }

    private sealed class DefendAttackGroupDefinition
    {
        public readonly List<DefendAttackEntry> Enemies = new();
        public string Identifier;
        public int DefendRound;
        public int[] ActiveDays;
        public DefendRouteDefinition Route;
        public UnitType UnitType;
        public Fix64 InitialStrengthValue;
        public Fix64 CountGrowthWeight;
        public string AfterGroupIdentifier;
        public Fix64 DelaySeconds;
        public Fix64 ExpectedEngagementSeconds;
        public Fix64 FirstSpawnLeadSeconds;
        public Fix64 LastSpawnLeadSeconds;
        public Fix64 StartSeconds;
        public bool StartResolved;
    }

    private sealed class DefendAttackEntry
    {
        public UnitType UnitType;
        public int UnitLevel;
        public int Count;
    }

    private sealed class PlannedSpawnEvent
    {
        public ulong RequestFrameOffset;
        public ulong Sequence;
        public UnitType UnitType;
        public int UnitLevel;
        public FixVector2 SpawnPosition;
        public string RouteIdentifier;
        public Fix64 SpeedProperty;
        public string SourceStrongholdId;
        public string SpawnPointName;
        public ulong ExpectedEngagementFrameOffset;
        public FixVector2[] RouteWaypointsFixed;
        public string[] RouteWaypointTeleportationIds;
        public int SpeedReleaseWaypointIndex;
        public FixVector2? SpeedReleasePositionFixed;
    }

    private readonly struct EngagementPathEstimate
    {
        public EngagementPathEstimate(Fix64 distance, FixVector2? releasePosition)
        {
            Distance = distance;
            ReleasePosition = releasePosition;
        }

        public Fix64 Distance { get; }
        public FixVector2? ReleasePosition { get; }
    }

    private sealed class WaveSpawnCandidate
    {
        public DefendAttackGroupDefinition Group;
        public DefendAttackEntry Entry;
        public Fix64 EarliestSpawnSeconds;
        public Fix64 LatestSpawnSeconds;
        public int OrdinalInGroup;
        public int StableSequence;
        public Fix64 ScheduledSpawnSeconds;
    }

    private sealed class TutorialTriggeredSpawnPoint
    {
        public TutorialTriggeredSpawnPoint(
            FixVector2 position,
            UnitType unitType,
            Fix64 initialStrengthValue,
            Fix64 countGrowthWeight,
            string name)
        {
            Position = position;
            UnitType = unitType;
            InitialStrengthValue = initialStrengthValue;
            CountGrowthWeight = countGrowthWeight;
            Name = name;
        }

        public FixVector2 Position { get; }
        public UnitType UnitType { get; }
        public Fix64 InitialStrengthValue { get; }
        public Fix64 CountGrowthWeight { get; }
        public string Name { get; }
    }
}
