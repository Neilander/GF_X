using System;
using System.Collections.Generic;
using AAAGame.Card;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;
using UnityGameFramework.Runtime;

public static class DefendPhaseRuntime
{
    private const string DefendEnemyArriveIntervalConfigKey = "DefendPhaseEnemyArriveInterval";
    private const string DefendEnemyMinSpeedConfigKey = "DefendPhaseEnemyMinSpeed";
    private const string DefendEndlessGrowthRateConfigKey = "DefendPhaseEnemyEndlessGrowthRate";
    private const float MinArriveIntervalSeconds = 0.01f;
    private const float MinWorldSpeed = 0.001f;
    private const float NavigationPointProbeRadius = 2.5f;

    private static readonly ArchetypeUnitTypeMapper s_ArchetypeUnitTypeMapper = new();
    private static readonly Dictionary<UnitType, Archetype> s_ArchetypeByUnitType = new();
    private static readonly Dictionary<UnitType, int> s_AgentTypeIdByUnitType = new();
    private static readonly List<DefendSpawnPointRuntime> s_DefendSpawnPoints = new();
    private static readonly List<DefendWaveDefinition> s_DefendWaves = new();
    private static readonly HashSet<int> s_AliveEnemyLogicEntityIds = new();
    private static readonly List<int> s_DeterministicAliveEnemyIds = new();
    private static readonly HashSet<int> s_AcceleratedEnemyLogicEntityIds = new();
    private static readonly List<int> s_DeterministicAcceleratedEnemyIds = new();
    private static readonly Comparison<int> s_DeterministicEnemyIdComparison =
        (left, right) => left.CompareTo(right);

    private static bool s_SubscribedLogicUnitDead;
    private static bool s_ArchetypeCacheConfigured;
    private static bool s_SpawnPointCacheConfigured;
    private static bool s_WaveConfigConfigured;
    private static int s_CachedLevelEntityId;
    private static int s_DefendRoundIndex;
    private static bool s_SpawnScheduleCompleted;
    private static string s_WaveConfigLevelIdentifier = string.Empty;
    private static readonly List<PlannedSpawnEvent> s_PlannedSpawnEvents = new();
    private static int s_NextPlannedSpawnIndex;
    private static ulong s_SpawnRequestStartFrame;
    private static Fix64 s_ArriveInterval;
    private static Fix64 s_MinSpeedWorld;
    private static Fix64 s_EndlessGrowthRate;
    private static decimal s_DistanceConversionRate;

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
        s_ArchetypeByUnitType.Clear();
        s_AgentTypeIdByUnitType.Clear();
        s_DefendSpawnPoints.Clear();
        s_DefendWaves.Clear();
        s_ArchetypeCacheConfigured = false;
        s_SpawnPointCacheConfigured = false;
        s_WaveConfigConfigured = false;
        s_CachedLevelEntityId = 0;
        s_WaveConfigLevelIdentifier = string.Empty;
        s_ArriveInterval = Fix64.Zero;
        s_MinSpeedWorld = Fix64.Zero;
        s_EndlessGrowthRate = Fix64.Zero;
        s_DistanceConversionRate = decimal.Zero;
    }

    public static void PrepareForCurrentLevelIfNeeded()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        EnsureSubscribedSoldierDead();
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
        s_DefendRoundIndex++;

        DefendWaveDefinition wave = ResolveWaveForCurrentRound();
        if (wave == null || wave.Entries.Count == 0)
        {
            Log.Warning("[DefendPhase] 当前防御波次无配置，直接结束。round={0}", s_DefendRoundIndex);
            s_SpawnScheduleCompleted = true;
            TryCompleteDefendPhase();
            return;
        }

        s_PlannedSpawnEvents.AddRange(BuildSpawnEvents(wave));
        if (s_PlannedSpawnEvents.Count == 0)
        {
            Log.Warning("[DefendPhase] 当前防御波次无法生成出怪计划，直接结束。round={0}", s_DefendRoundIndex);
            s_SpawnScheduleCompleted = true;
            TryCompleteDefendPhase();
            return;
        }

        s_PlannedSpawnEvents.Sort(ComparePlannedSpawnEvents);
        s_SpawnRequestStartFrame = LogicTimeControlService.CurrentFrame > 0
            ? LogicTimeControlService.CurrentFrame
            : 1UL;
    }

    public static void ApplyScheduledSpawnRequests(ulong frame)
    {
        if (frame == 0 || frame != LogicTimeControlService.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"DefendPhaseRuntime.ApplyScheduledSpawnRequests failed: frame mismatch. requested={frame}, logic={LogicTimeControlService.CurrentFrame}.");
        }
        ReleaseAuthoritativelyVisibleEnemySpawnSpeeds(frame);
        if (s_SpawnScheduleCompleted || s_PlannedSpawnEvents.Count == 0)
            return;
        if (PhaseManager.CurrentPhase != GamePhase.Defend)
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
        LogicEntityId entityId = SoldierFactory.ShowSoldierFixed(
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
            },
            evt.UnitLevel);
        if (!entityId.IsValid)
        {
            throw new InvalidOperationException(
                $"DefendPhaseRuntime failed to request soldier spawn. unit={evt.UnitType} raw=({evt.SpawnPosition.x.RawValue},{evt.SpawnPosition.y.RawValue}).");
        }

        LogDefendSpawnEvent(evt, entityId);
        if (!s_AliveEnemyLogicEntityIds.Add(entityId.Value))
            throw new InvalidOperationException($"DefendPhaseRuntime produced duplicate enemy logic entity id {entityId.Value}.");
        if (!s_AcceleratedEnemyLogicEntityIds.Add(entityId.Value))
            throw new InvalidOperationException($"DefendPhaseRuntime produced duplicate accelerated enemy logic entity id {entityId.Value}.");
    }

    public static bool TryGetNextDefendPreviewSpawnEntries(List<DefendPreviewSpawnEntry> results)
    {
        if (results == null)
            return false;

        results.Clear();
        PrepareForCurrentLevelIfNeeded();

        DefendWaveDefinition wave = ResolveWaveForRound(Mathf.Max(1, s_DefendRoundIndex + 1));
        if (wave == null || wave.Entries.Count == 0)
            return false;

        for (int i = 0; i < wave.Entries.Count; i++)
        {
            DefendWaveEntry waveEntry = wave.Entries[i];
            if (waveEntry.Count <= 0)
                continue;

            List<PointSpawnCount> pointCounts = AllocatePointCountsForUnit(waveEntry.UnitType, EnemyArmyForceModifierService.CalculateSpawnCount(waveEntry.Count));
            for (int j = 0; j < pointCounts.Count; j++)
            {
                PointSpawnCount pointCount = pointCounts[j];
                if (pointCount == null || pointCount.Point == null)
                    continue;
                if (pointCount.Count <= 0)
                    continue;

                FixVector2 spawnPosition = pointCount.Point.Position;
                results.Add(new DefendPreviewSpawnEntry(
                    waveEntry.UnitType,
                    waveEntry.UnitLevel,
                    pointCount.Count,
                    new Vector3((float)spawnPosition.x, 0f, (float)spawnPosition.y),
                    pointCount.Point.Identifier));
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

        PrepareForCurrentLevelIfNeeded();

        pathCorners.Clear();
        navigationUpdatePending = false;
        int agentTypeId = ResolveAgentTypeId(unitType);
        if (!TryResolveBaseNavigationPoint(basePosition, agentTypeId, out Vector3 navigationBase, out failureReason))
            return false;

        return FlowFieldCrowdMovementSystem.TryGetNavigationPathCornersNonBlocking(
            spawnPosition,
            navigationBase,
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

    private static void ReleaseAuthoritativelyVisibleEnemySpawnSpeeds(ulong frame)
    {
        if (s_AcceleratedEnemyLogicEntityIds.Count == 0)
            return;
        if (!LogicCardPlacementAuthority.IsWorldBound)
            throw new InvalidOperationException("DefendPhaseRuntime cannot evaluate enemy visibility without a bound logic visibility world.");

        s_DeterministicAcceleratedEnemyIds.Clear();
        foreach (int entityId in s_AcceleratedEnemyLogicEntityIds)
            s_DeterministicAcceleratedEnemyIds.Add(entityId);
        s_DeterministicAcceleratedEnemyIds.Sort(s_DeterministicEnemyIdComparison);

        for (int i = 0; i < s_DeterministicAcceleratedEnemyIds.Count; i++)
        {
            int entityId = s_DeterministicAcceleratedEnemyIds[i];
            if (!s_AliveEnemyLogicEntityIds.Contains(entityId))
                throw new InvalidOperationException($"DefendPhaseRuntime tracks spawn acceleration for non-alive defend enemy {entityId}.");

            LogicEntityState state = LogicEntityStateStore.GetRequired(new LogicEntityId(entityId));
            if (!state.BuffComp.HasBuff(LogicUnitConfigurator.DefendSpeedBuffId))
                throw new InvalidOperationException($"DefendPhaseRuntime accelerated enemy {entityId} is missing its speed override buff.");
            if (!LogicCardPlacementAuthority.IsVisibleFromCurrentLogicRevealers(state.Position))
                continue;

            Fix64 speedBefore = state.GetProperty(CreatureMainProperty.Speed);
            if (!LogicUnitConfigurator.ReleaseDefendEnemySpawnSpeed(state))
                throw new InvalidOperationException($"DefendPhaseRuntime failed to release spawn acceleration for visible enemy {entityId}.");
            Fix64 speedAfter = state.GetProperty(CreatureMainProperty.Speed);
            s_AcceleratedEnemyLogicEntityIds.Remove(entityId);
            Log.Info(
                "[DefendPhase] Released spawn speed on authoritative visibility. logicEntityId={0} frame={1} speedBeforeRaw={2} speedAfterRaw={3}",
                entityId,
                frame,
                speedBefore.RawValue,
                speedAfter.RawValue);
        }
        s_DeterministicAcceleratedEnemyIds.Clear();
    }

    private static void OnLogicUnitDied(IEntityContext victim)
    {
        if (PhaseManager.CurrentPhase != GamePhase.Defend)
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
        if (PhaseManager.CurrentPhase != GamePhase.Defend)
            return;

        if (!s_SpawnScheduleCompleted)
            return;

        if (s_AliveEnemyLogicEntityIds.Count > 0)
            return;

        Log.Info("[DefendPhase] 防御阶段结束：敌兵已全部清空。round={0}", s_DefendRoundIndex);
        PhaseManager.SwitchToPhase(GamePhase.BuildBeforeInvade);
    }

    private static void ConfigureArchetypeCacheIfNeeded()
    {
        if (s_ArchetypeCacheConfigured)
            return;

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
        s_DefendWaves.Clear();
        s_DefendSpawnPoints.Clear();

        EntityPresetPoint[] points = levelEntity.GetComponentsInChildren<EntityPresetPoint>(true);
        int defendPointCount = 0;
        double diagnosticsMs = 0.0;
        for (int i = 0; i < points.Length; i++)
        {
            EntityPresetPoint point = points[i];
            if (point == null || point.PointType != EntityPresetPointType.DefendSpawn)
                continue;

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

            s_DefendSpawnPoints.Add(new DefendSpawnPointRuntime
            {
                Position = pointPositionFixed,
                Weight = Math.Max(0, point.DefendSpawnWeight),
                Identifier = ResolvePreviewSpawnPointIdentifier(point),
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

    private static FixVector2 ResolvePlayerBasePositionFixed()
    {
        IEntityContext bestBase = null;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!(entities[i] is IBuildingLogicContext building)
                || !building.Alive
                || building.OwnerFactionId != EntitySideHelper.PlayerFactionId
                || building.BuildingData == null
                || building.BuildingData.Type != BuilType.Base)
            {
                continue;
            }

            if (bestBase == null || building.LogicEntityId.Value < bestBase.LogicEntityId.Value)
                bestBase = building;
        }
        if (bestBase != null)
            return bestBase.PositionFixed;

        if (EntityRegistry.Player != null)
            return EntityRegistry.Player.PositionFixed;

        throw new InvalidOperationException("DefendPhaseRuntime cannot resolve a player base or player logic position.");
    }

    private static Fix64 CalculatePathDistanceFixed(FixVector2 from, FixVector2 to, UnitType unitType)
    {
        int agentTypeId = ResolveAgentTypeId(unitType);
        if (!TryResolveBaseNavigationPointFixed(to, agentTypeId, out FixVector2 navigationBase, out string failureReason))
        {
            throw new InvalidOperationException(
                $"DefendPhaseRuntime.CalculatePathDistanceFixed failed: unit={unitType} fromRaw=({from.x.RawValue},{from.y.RawValue}) " +
                $"toRaw=({to.x.RawValue},{to.y.RawValue}) agentType={agentTypeId} reason={failureReason}");
        }

        if (FlowFieldCrowdMovementSystem.TryEstimateNavigationDistanceFixed(
                from,
                navigationBase,
                agentTypeId,
                out Fix64 distance,
                out failureReason))
        {
            return distance;
        }

        throw new InvalidOperationException(
            $"DefendPhaseRuntime.CalculatePathDistanceFixed failed: unit={unitType} fromRaw=({from.x.RawValue},{from.y.RawValue}) " +
            $"toRaw=({to.x.RawValue},{to.y.RawValue}) agentType={agentTypeId} reason={failureReason}");
    }

    private static bool TryResolveBaseNavigationPointFixed(
        FixVector2 basePosition,
        int agentTypeId,
        out FixVector2 navigationBase,
        out string failureReason)
    {
        if (FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPointFixed(
                basePosition,
                agentTypeId,
                (Fix64)NavigationPointProbeRadius,
                Fix64.Zero,
                out navigationBase))
        {
            failureReason = string.Empty;
            return true;
        }

        failureReason =
            $"no legal navigation point near player base raw=({basePosition.x.RawValue},{basePosition.y.RawValue}) " +
            $"agentType={agentTypeId} maxSnapDistanceRaw={((Fix64)NavigationPointProbeRadius).RawValue}";
        return false;
    }

    private static bool TryResolveBaseNavigationPoint(
        Vector3 basePosition,
        int agentTypeId,
        out Vector3 navigationBase,
        out string failureReason)
    {
        var fixedBase = new FixVector2((Fix64)basePosition.x, (Fix64)basePosition.z);
        if (FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPointFixedNonBlocking(
                fixedBase,
                agentTypeId,
                (Fix64)NavigationPointProbeRadius,
                Fix64.Zero,
                out FixVector2 fixedNavigationBase))
        {
            navigationBase = new Vector3((float)fixedNavigationBase.x, basePosition.y, (float)fixedNavigationBase.y);
            failureReason = string.Empty;
            return true;
        }

        navigationBase = Vector3.zero;
        failureReason =
            $"no committed legal navigation point near player base raw=({fixedBase.x.RawValue},{fixedBase.y.RawValue}) " +
            $"agentType={agentTypeId} maxSnapDistanceRaw={((Fix64)NavigationPointProbeRadius).RawValue}";
        return false;
    }

    private static void LogSpawnPointDiagnostics(EntityPresetPoint point)
    {
        if (point == null)
            return;

        string identifier = ResolvePreviewSpawnPointIdentifier(point);
        Vector3 position = point.Position;
        Stronghold stronghold = LevelEntity.GetStrongholdAtWorldPosition(position);

        int smallAgentTypeId = AgentTypeHelper.ResolveNavAgentTypeId(UnitSize.Small);
        bool flowSmall = FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
            position,
            smallAgentTypeId,
            NavigationPointProbeRadius,
            0f,
            out Vector3 smallLegalPoint);
        string smallLegalPos = flowSmall ? smallLegalPoint.ToString() : "none";

        Log.Info(
            "[DefendPhase] SpawnPoint diag id={0} name={1} pos={2} weight={3} stronghold={4} flowSmall={5} flowSmallPos={6} smallAgentType={7}",
            identifier,
            point.name,
            position,
            point.DefendSpawnWeight,
            stronghold != null ? stronghold.OwnerFactionId.ToString() : "null",
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
            NavigationPointProbeRadius,
            0f,
            out Vector3 legalPoint);

        Log.Info(
            "[DefendPhase] SpawnEvent logicEntityId={0} unit={1} level={2} pos={3} speedProp={4:F2} speedRaw={5} point={6} stronghold={7} flowHit={8} flowPos={9} agentType={10}",
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
            agentTypeId);
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
        s_DefendWaves.Clear();

        if (!LevelSelectionService.TryGetLevelRow(levelIdentifier, out LevelTable levelRow, out string errorMessage) || levelRow == null)
        {
            throw new InvalidOperationException(
                $"DefendPhaseRuntime failed to load LevelTable row. level={levelIdentifier}, error={errorMessage}");
        }

        AppendWaveFromPairs(levelRow.Def1Enemies);
        AppendWaveFromPairs(levelRow.Def2Enemies);
        AppendWaveFromPairs(levelRow.Def3Enemies);
        AppendWaveFromPairs(levelRow.Def4Enemies);
        AppendWaveFromPairs(levelRow.Def5Enemies);
        AppendWaveFromPairs(levelRow.Def6Enemies);
        AppendWaveFromPairs(levelRow.Def7Enemies);
        AppendWaveFromPairs(levelRow.Def8Enemies);
        AppendWaveFromPairs(levelRow.Def9Enemies);
        AppendWaveFromPairs(levelRow.Def10Enemies);

        s_AgentTypeIdByUnitType.Clear();
        for (int waveIndex = 0; waveIndex < s_DefendWaves.Count; waveIndex++)
        {
            DefendWaveDefinition wave = s_DefendWaves[waveIndex];
            for (int entryIndex = 0; entryIndex < wave.Entries.Count; entryIndex++)
            {
                UnitType unitType = wave.Entries[entryIndex].UnitType;
                s_AgentTypeIdByUnitType[unitType] = AgentTypeHelper.ResolveNavAgentTypeId(unitType);
            }
        }
        s_ArriveInterval = ResolveFiniteConfigFixed(
            DefendEnemyArriveIntervalConfigKey,
            (Fix64)MinArriveIntervalSeconds);
        Fix64 minSpeedProperty = ResolveFiniteConfigFixed(DefendEnemyMinSpeedConfigKey, Fix64.One);
        s_MinSpeedWorld = Fix64.Max(
            (Fix64)MinWorldSpeed,
            DistanceUnitConverter.ConvertToWorld(minSpeedProperty));
        s_EndlessGrowthRate = DistanceUnitConverter.ReadRequiredPositiveFixedConfig(
            DefendEndlessGrowthRateConfigKey);
        s_WaveConfigConfigured = true;

        Log.Info("[DefendPhase] 防御波次配置加载完成。level={0}, waves={1}", levelIdentifier, s_DefendWaves.Count);
    }

    private static string ResolveCurrentLevelIdentifier()
    {
        return LevelSelectionService.SelectedLevelIdentifier;
    }

    private static void AppendWaveFromPairs(StringIntPair[] wavePairs)
    {
        if (wavePairs == null || wavePairs.Length == 0)
            return;

        var wave = new DefendWaveDefinition();
        for (int i = 0; i < wavePairs.Length; i++)
        {
            string unitId = wavePairs[i].str;
            int count = wavePairs[i].num;
            if (!UnitTypeHelper.TryParseUnitTypeAndLevel(unitId, out UnitType unitType, out int unitLevel))
                throw new InvalidOperationException($"DefendPhaseRuntime found an invalid wave unit identifier '{unitId}'.");
            if (count <= 0)
                throw new InvalidOperationException($"DefendPhaseRuntime found a non-positive wave count. unit={unitId}, count={count}.");

            wave.Entries.Add(new DefendWaveEntry
            {
                UnitType = unitType,
                UnitLevel = unitLevel,
                Count = count
            });
        }

        if (wave.Entries.Count > 0)
            s_DefendWaves.Add(wave);
    }

    private static DefendWaveDefinition ResolveWaveForCurrentRound()
    {
        return ResolveWaveForRound(s_DefendRoundIndex);
    }

    private static DefendWaveDefinition ResolveWaveForRound(int roundIndex)
    {
        if (s_DefendWaves.Count == 0)
            return null;

        if (roundIndex <= s_DefendWaves.Count)
            return CloneWave(s_DefendWaves[Mathf.Max(0, roundIndex - 1)], Fix64.One);

        int overflowRounds = roundIndex - s_DefendWaves.Count;
        Fix64 scale = Fix64.Pow(s_EndlessGrowthRate, overflowRounds);
        return CloneWave(s_DefendWaves[s_DefendWaves.Count - 1], scale);
    }

    private static DefendWaveDefinition CloneWave(DefendWaveDefinition source, Fix64 scale)
    {
        if (source == null)
            return null;

        var clone = new DefendWaveDefinition();
        for (int i = 0; i < source.Entries.Count; i++)
        {
            DefendWaveEntry entry = source.Entries[i];
            long scaledCountRaw = checked((long)entry.Count * Fix64.Max(Fix64.Zero, scale).RawValue);
            long roundedCount = checked(scaledCountRaw + (1L << (Fix64.FRACTIONAL_PLACES - 1)))
                                >> Fix64.FRACTIONAL_PLACES;
            int scaledCount = checked((int)roundedCount);
            if (entry.Count > 0 && scaledCount <= 0)
                scaledCount = 1;

            clone.Entries.Add(new DefendWaveEntry
            {
                UnitType = entry.UnitType,
                UnitLevel = entry.UnitLevel,
                Count = scaledCount
            });
        }

        return clone;
    }

#if UNITY_EDITOR
    public static int GetEditorTestScaledSpawnCount(int count, Fix64 scale)
    {
        var wave = new DefendWaveDefinition();
        wave.Entries.Add(new DefendWaveEntry { Count = count });
        return CloneWave(wave, scale).Entries[0].Count;
    }
#endif

    private static List<PlannedSpawnEvent> BuildSpawnEvents(DefendWaveDefinition wave)
    {
        var events = new List<PlannedSpawnEvent>();
        if (wave == null || wave.Entries.Count == 0 || s_DefendSpawnPoints.Count == 0)
            return events;

        ulong arriveIntervalTicks = SecondsToTicksCeiling(s_ArriveInterval);
        Fix64 minSpeedWorld = s_MinSpeedWorld;
        FixVector2 basePosition = ResolvePlayerBasePositionFixed();

        for (int i = 0; i < wave.Entries.Count; i++)
        {
            DefendWaveEntry entry = wave.Entries[i];
            if (entry.Count <= 0)
                continue;

            List<PointSpawnCount> pointCounts = AllocatePointCountsForUnit(entry.UnitType, EnemyArmyForceModifierService.CalculateSpawnCount(entry.Count));
            if (pointCounts.Count == 0)
                continue;

            for (int pointIndex = 0; pointIndex < pointCounts.Count; pointIndex++)
            {
                PointSpawnCount pointCount = pointCounts[pointIndex];
                if (pointCount?.Point == null)
                    throw new InvalidOperationException($"DefendPhaseRuntime.BuildSpawnEvents failed: spawn point is null. unit={entry.UnitType} index={pointIndex}.");

                pointCount.DistanceToBase = CalculatePathDistanceFixed(
                    pointCount.Point.Position,
                    basePosition,
                    entry.UnitType);
            }

            pointCounts.Sort(ComparePointSpawnCounts);

            ulong previousLastArrivalTicks = 0;
            for (int pointIndex = 0; pointIndex < pointCounts.Count; pointIndex++)
            {
                PointSpawnCount pointCount = pointCounts[pointIndex];
                int spawnCount = pointCount.Count;
                if (spawnCount <= 0)
                    continue;

                Fix64 distance = Fix64.Max(Fix64.Zero, pointCount.DistanceToBase);
                ulong targetFirstArrivalTicks;
                ulong spawnDelayTicks;
                Fix64 pointSpeedWorld;
                ulong naturalArrivalTicks = SecondsToTicksCeiling(distance / minSpeedWorld);

                if (pointIndex == 0)
                {
                    targetFirstArrivalTicks = naturalArrivalTicks;
                    spawnDelayTicks = 0;
                    pointSpeedWorld = minSpeedWorld;
                }
                else
                {
                    targetFirstArrivalTicks = checked(previousLastArrivalTicks + arriveIntervalTicks);
                    if (naturalArrivalTicks <= targetFirstArrivalTicks)
                    {
                        spawnDelayTicks = targetFirstArrivalTicks - naturalArrivalTicks;
                        pointSpeedWorld = minSpeedWorld;
                    }
                    else
                    {
                        spawnDelayTicks = 0;
                        Fix64 targetArrivalTime = TicksToDuration(targetFirstArrivalTicks);
                        pointSpeedWorld = distance / targetArrivalTime;
                    }
                }

                Fix64 pointSpeedProperty = DistanceUnitConverter.ConvertFromWorld(pointSpeedWorld, s_DistanceConversionRate);
                ulong pointLastArrivalTicks = checked(
                    targetFirstArrivalTicks + checked((ulong)(spawnCount - 1) * arriveIntervalTicks));
                previousLastArrivalTicks = pointLastArrivalTicks;

                string strongholdId = pointCount.StrongholdId;
                string pointName = pointCount.Point.Name;
                FixVector2 spawnPosition = pointCount.Point.Position;

                for (int spawnIndex = 0; spawnIndex < spawnCount; spawnIndex++)
                {
                    events.Add(new PlannedSpawnEvent
                    {
                        RequestFrameOffset = checked(spawnDelayTicks + checked((ulong)spawnIndex * arriveIntervalTicks)),
                        Sequence = checked((ulong)events.Count + 1UL),
                        UnitType = entry.UnitType,
                        UnitLevel = entry.UnitLevel,
                        SpawnPosition = spawnPosition,
                        SpeedProperty = pointSpeedProperty,
                        SourceStrongholdId = strongholdId,
                        SpawnPointName = pointName,
                        TheoreticalArrivalFrameOffset = checked(
                            targetFirstArrivalTicks + checked((ulong)spawnIndex * arriveIntervalTicks))
                    });
                }
            }
        }

        return events;
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

    private static int ComparePointSpawnCounts(PointSpawnCount left, PointSpawnCount right)
    {
        int distance = left.DistanceToBase.CompareTo(right.DistanceToBase);
        return distance != 0
            ? distance
            : CompareDefendSpawnPoints(left.Point, right.Point);
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

    private static List<PointSpawnCount> AllocatePointCountsForUnit(UnitType unitType, int totalCount)
    {
        var result = new List<PointSpawnCount>();
        if (totalCount <= 0 || s_DefendSpawnPoints.Count == 0)
            return result;

        if (!s_ArchetypeByUnitType.TryGetValue(unitType, out Archetype unitArchetype))
        {
            Log.Info(
                "[DefendPhase] no archetype mapping for unit; no spawn will be scheduled. unit={0}.",
                unitType);
            return result;
        }

        var eligiblePoints = new List<DefendSpawnPointRuntime>();
        int totalWeightAllSides = 0;

        for (int i = 0; i < s_DefendSpawnPoints.Count; i++)
        {
            DefendSpawnPointRuntime runtimePoint = s_DefendSpawnPoints[i];
            if (!LogicBuildingQueryService.TryResolveStrongholdOwnerFaction(runtimePoint.StrongholdId, out int ownerFactionId)
                || ownerFactionId == EntitySideHelper.PlayerFactionId)
            {
                continue;
            }

            bool archMatched = LogicBuildingQueryService.HasBuildingArchetype(
                runtimePoint.StrongholdId,
                ownerFactionId,
                unitArchetype);
            if (!archMatched)
                continue;

            int weight = runtimePoint.Weight;
            if (weight <= 0)
                continue;

            eligiblePoints.Add(runtimePoint);
            totalWeightAllSides += weight;
        }

        if (eligiblePoints.Count == 0 || totalWeightAllSides <= 0)
        {
            Log.Info(
                "[DefendPhase] no eligible enemy stronghold for unit archetype; no spawn will be scheduled. unit={0} archetype={1}.",
                unitType,
                unitArchetype);
            return result;
        }

        int allocatedTotal = 0;
        for (int i = 0; i < eligiblePoints.Count; i++)
        {
            DefendSpawnPointRuntime runtimePoint = eligiblePoints[i];
            if (!LogicBuildingQueryService.TryResolveStrongholdOwnerFaction(runtimePoint.StrongholdId, out int ownerFactionId)
                || ownerFactionId == EntitySideHelper.PlayerFactionId)
            {
                continue;
            }

            int weight = runtimePoint.Weight;
            int count = RoundPositiveRatioToInt((long)weight * totalCount, totalWeightAllSides);
            if (count <= 0)
                continue;

            allocatedTotal += count;
            result.Add(new PointSpawnCount
            {
                Point = runtimePoint,
                StrongholdId = runtimePoint.StrongholdId,
                Count = count
            });
        }

        if (result.Count == 0)
            return result;

        int delta = totalCount - allocatedTotal;
        if (delta > 0)
        {
            int index = result.Count - 1;
            while (delta > 0)
            {
                result[index].Count++;
                delta--;
                if (--index < 0)
                    index = result.Count - 1;
            }
        }
        else if (delta < 0)
        {
            delta = -delta;
            int index = result.Count - 1;
            while (delta > 0 && index >= 0)
            {
                if (result[index].Count > 0)
                {
                    result[index].Count--;
                    delta--;
                }

                if (result[index].Count <= 0)
                {
                    result.RemoveAt(index);
                }

                index = result.Count - 1;
            }
        }

        result.RemoveAll(x => x.Count <= 0);
        return result;
    }

    private static int RoundPositiveRatioToInt(long numerator, int denominator)
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
            || !s_ArchetypeCacheConfigured
            || !s_SpawnPointCacheConfigured
            || !s_WaveConfigConfigured)
        {
            throw new InvalidOperationException(
                "DefendPhaseRuntime was not prepared before entering the logic Defend phase.");
        }
        if (s_ArriveInterval <= Fix64.Zero
            || s_MinSpeedWorld <= Fix64.Zero
            || s_EndlessGrowthRate <= Fix64.Zero)
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
        if (!keepRoundIndex)
            s_DefendRoundIndex = 0;
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add(s_DefendRoundIndex);
        hasher.Add(s_SpawnScheduleCompleted);
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
            hasher.Add(evt.SpeedProperty.RawValue);
            hasher.Add(evt.SourceStrongholdId);
            hasher.Add(evt.SpawnPointName);
            hasher.Add(evt.TheoreticalArrivalFrameOffset);
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
    public static ulong GetEditorTestTickCount(Fix64 duration)
    {
        return SecondsToTicksCeiling(duration);
    }
#endif

    private sealed class DefendSpawnPointRuntime
    {
        public FixVector2 Position;
        public int Weight;
        public string Identifier;
        public string Name;
        public string StrongholdId;
    }

    private sealed class DefendWaveDefinition
    {
        public readonly List<DefendWaveEntry> Entries = new();
    }

    private sealed class DefendWaveEntry
    {
        public UnitType UnitType;
        public int UnitLevel;
        public int Count;
    }

    private sealed class PointSpawnCount
    {
        public DefendSpawnPointRuntime Point;
        public string StrongholdId;
        public int Count;
        public Fix64 DistanceToBase;
    }

    private sealed class PlannedSpawnEvent
    {
        public ulong RequestFrameOffset;
        public ulong Sequence;
        public UnitType UnitType;
        public int UnitLevel;
        public FixVector2 SpawnPosition;
        public Fix64 SpeedProperty;
        public string SourceStrongholdId;
        public string SpawnPointName;
        public ulong TheoreticalArrivalFrameOffset;
    }
}
