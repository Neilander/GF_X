using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using Cysharp.Threading.Tasks;
using GameFramework.Event;
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
    private static readonly List<DefendSpawnPointRuntime> s_DefendSpawnPoints = new();
    private static readonly List<DefendWaveDefinition> s_DefendWaves = new();
    private static readonly HashSet<int> s_AliveEnemyEntityIds = new();

    private static bool s_SubscribedSoldierDead;
    private static int s_FlowToken;
    private static int s_CachedLevelEntityId;
    private static int s_DefendRoundIndex;
    private static bool s_SpawnScheduleCompleted;
    private static string s_WaveConfigLevelIdentifier = string.Empty;

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
        s_FlowToken++;
        ResetDefendPhaseState(keepRoundIndex: true);
    }

    public static void PrepareForCurrentLevelIfNeeded()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        EnsureSubscribedSoldierDead();
        double subscribeMs = stopwatch.Elapsed.TotalMilliseconds;
        EnsureArchetypeCache();
        double archetypeMs = stopwatch.Elapsed.TotalMilliseconds;
        EnsureSpawnPointCache();
        double spawnPointMs = stopwatch.Elapsed.TotalMilliseconds;
        EnsureWaveConfigLoaded();
        Log.Info(
            "[DefendPhaseTiming] stage=prepare totalMs={0:F3} subscribeMs={1:F3} archetypeMs={2:F3} spawnPointMs={3:F3} waveMs={4:F3}",
            stopwatch.Elapsed.TotalMilliseconds,
            subscribeMs,
            archetypeMs - subscribeMs,
            spawnPointMs - archetypeMs,
            stopwatch.Elapsed.TotalMilliseconds - spawnPointMs);
    }

    public static async UniTaskVoid EnterDefendPhaseAsync()
    {
        EnsureSubscribedSoldierDead();
        EnsureArchetypeCache();
        EnsureSpawnPointCache();
        EnsureWaveConfigLoaded();

        int flowToken = ++s_FlowToken;
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

        List<PlannedSpawnEvent> spawnEvents = BuildSpawnEvents(wave);
        if (spawnEvents.Count == 0)
        {
            Log.Warning("[DefendPhase] 当前防御波次无法生成出怪计划，直接结束。round={0}", s_DefendRoundIndex);
            s_SpawnScheduleCompleted = true;
            TryCompleteDefendPhase();
            return;
        }

        spawnEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
        float lastTime = 0f;
        for (int i = 0; i < spawnEvents.Count; i++)
        {
            if (flowToken != s_FlowToken || PhaseManager.CurrentPhase != GamePhase.Defend)
                return;

            PlannedSpawnEvent evt = spawnEvents[i];
            float waitTime = Mathf.Max(0f, evt.Time - lastTime);
            lastTime = evt.Time;
            if (waitTime > 0f)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(waitTime), DelayType.DeltaTime, PlayerLoopTiming.Update);
                if (flowToken != s_FlowToken || PhaseManager.CurrentPhase != GamePhase.Defend)
                    return;
            }

            int entityId = SoldierFactory.ShowSoldier(
                evt.UnitType,
                evt.SpawnPosition,
                SideType.EnemySide,
                BrainType.DefendEnemyAI,
                null,
                evt.SourceStrongholdId,
                entityParams =>
                {
                    entityParams.Set<VarFloat>(SoldierEntity.P_DefendAssignedSpeed, evt.SpeedProperty);
                },
                evt.UnitLevel);
            if (entityId <= 0)
            {
                Log.Warning("[DefendPhase] 生成单位失败。unit={0}, pos={1}", evt.UnitType, evt.SpawnPosition);
                continue;
            }

            LogDefendSpawnEvent(evt, entityId);

            s_AliveEnemyEntityIds.Add(entityId);
        }

        if (flowToken != s_FlowToken || PhaseManager.CurrentPhase != GamePhase.Defend)
            return;

        s_SpawnScheduleCompleted = true;
        TryCompleteDefendPhase();
    }

    public static bool TryGetNextDefendPreviewSpawnEntries(List<DefendPreviewSpawnEntry> results)
    {
        if (results == null)
            return false;

        results.Clear();
        EnsureArchetypeCache();
        EnsureSpawnPointCache();
        EnsureWaveConfigLoaded();

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
                if (pointCount == null || pointCount.Point == null || pointCount.Point.Point == null)
                    continue;
                if (pointCount.Count <= 0)
                    continue;

                results.Add(new DefendPreviewSpawnEntry(
                    waveEntry.UnitType,
                    waveEntry.UnitLevel,
                    pointCount.Count,
                    pointCount.Point.Point.Position,
                    ResolvePreviewSpawnPointIdentifier(pointCount.Point.Point)));
            }
        }

        return results.Count > 0;
    }

    public static bool TryGetNavigationPathCorners(
        UnitType unitType,
        Vector3 spawnPosition,
        Vector3 basePosition,
        List<Vector3> pathCorners,
        out string failureReason)
    {
        if (pathCorners == null)
            throw new ArgumentNullException(nameof(pathCorners));

        pathCorners.Clear();
        int agentTypeId = ResolveAgentTypeId(unitType);
        if (!TryResolveBaseNavigationPoint(basePosition, agentTypeId, out Vector3 navigationBase, out failureReason))
            return false;

        return FlowFieldCrowdMovementSystem.TryGetNavigationPathCorners(
            spawnPosition,
            navigationBase,
            agentTypeId,
            pathCorners,
            out failureReason);
    }

    private static void EnsureSubscribedSoldierDead()
    {
        if (s_SubscribedSoldierDead || GF.Event == null)
            return;

        GF.Event.Subscribe(SoldierDeadEventArgs.EventId, OnSoldierDead);
        s_SubscribedSoldierDead = true;
    }

    private static void OnSoldierDead(object sender, GameEventArgs e)
    {
        if (PhaseManager.CurrentPhase != GamePhase.Defend)
            return;

        if (e is not SoldierDeadEventArgs args || args.VictimSide != SideType.EnemySide)
            return;

        if (!s_AliveEnemyEntityIds.Remove(args.VictimEntityId))
            return;

        TryCompleteDefendPhase();
    }

    private static void TryCompleteDefendPhase()
    {
        if (PhaseManager.CurrentPhase != GamePhase.Defend)
            return;

        if (!s_SpawnScheduleCompleted)
            return;

        if (s_AliveEnemyEntityIds.Count > 0)
            return;

        Log.Info("[DefendPhase] 防御阶段结束：敌兵已全部清空。round={0}", s_DefendRoundIndex);
        PhaseManager.SwitchToPhase(GamePhase.BuildBeforeInvade);
    }

    private static void EnsureArchetypeCache()
    {
        if (s_ArchetypeByUnitType.Count > 0)
            return;

        foreach (Archetype archetype in Enum.GetValues(typeof(Archetype)))
        {
            if (archetype == Archetype.None)
                continue;

            var unitTypes = s_ArchetypeUnitTypeMapper.GetUnitTypes(archetype);
            foreach (UnitType unitType in unitTypes)
            {
                if (!s_ArchetypeByUnitType.ContainsKey(unitType))
                    s_ArchetypeByUnitType[unitType] = archetype;
            }
        }
    }

    private static void EnsureSpawnPointCache()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        LevelEntity levelEntity = LevelEntity.ActiveLevelEntity;
        if (levelEntity == null)
            return;

        int levelEntityId = levelEntity.Id;
        if (s_CachedLevelEntityId == levelEntityId && s_DefendSpawnPoints.Count > 0)
            return;

        s_CachedLevelEntityId = levelEntityId;
        s_DefendRoundIndex = 0;
        s_WaveConfigLevelIdentifier = string.Empty;
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

            s_DefendSpawnPoints.Add(new DefendSpawnPointRuntime
            {
                Point = point
            });
        }

        Log.Info(
            "[DefendPhaseTiming] stage=spawn-point-cache totalMs={0:F3} presetPoints={1} defendPoints={2} cached={3} diagnosticsMs={4:F3}",
            stopwatch.Elapsed.TotalMilliseconds,
            points.Length,
            defendPointCount,
            s_DefendSpawnPoints.Count,
            diagnosticsMs);
    }

    private static Vector3 ResolvePlayerBasePosition()
    {
        var gameEndManager = GameEntry.GetComponent<GameEndManager>();
        if (gameEndManager != null && gameEndManager.TryGetAnyPlayerInitialConditionBuilding(out BuildingEntity initialBase) && initialBase != null)
            return initialBase.transform.position;

        var inGameData = GF.DataModel != null ? GF.DataModel.GetDataModel<InGameDataModel>() : null;
        if (inGameData != null)
        {
            foreach (var building in inGameData.Buildings)
            {
                if (building == null || building.buildingData == null)
                    continue;

                if (building.OwnerFactionID != EntitySideHelper.PlayerFactionId)
                    continue;

                if (building.buildingData.Type != BuilType.Base)
                    continue;

                return building.transform.position;
            }
        }

        if (EntityRegistry.Player != null)
            return EntityRegistry.Player.Position;

        return Vector3.zero;
    }

    private static float CalculatePathDistance(Vector3 from, Vector3 to, UnitType unitType)
    {
        int agentTypeId = ResolveAgentTypeId(unitType);
        if (!TryResolveBaseNavigationPoint(to, agentTypeId, out Vector3 navigationBase, out string failureReason))
        {
            throw new InvalidOperationException(
                $"DefendPhaseRuntime.CalculatePathDistance failed: unit={unitType} from={from} to={to} agentType={agentTypeId} reason={failureReason}");
        }

        if (FlowFieldCrowdMovementSystem.TryEstimateNavigationDistance(
                from,
                navigationBase,
                agentTypeId,
                out float distance,
                out failureReason))
        {
            return distance;
        }

        throw new InvalidOperationException(
            $"DefendPhaseRuntime.CalculatePathDistance failed: unit={unitType} from={from} to={to} agentType={agentTypeId} reason={failureReason}");
    }

    private static bool TryResolveBaseNavigationPoint(
        Vector3 basePosition,
        int agentTypeId,
        out Vector3 navigationBase,
        out string failureReason)
    {
        if (FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
                basePosition,
                agentTypeId,
                NavigationPointProbeRadius,
                0f,
                out navigationBase))
        {
            failureReason = string.Empty;
            return true;
        }

        failureReason =
            $"no legal navigation point near player base position={basePosition} agentType={agentTypeId} maxSnapDistance={NavigationPointProbeRadius:F2}";
        return false;
    }

    private static void LogSpawnPointDiagnostics(EntityPresetPoint point)
    {
        if (point == null)
            return;

        string identifier = ResolvePreviewSpawnPointIdentifier(point);
        Vector3 position = point.Position;
        Stronghold stronghold = LevelEntity.GetStrongholdAtWorldPosition(position);

        int smallAgentTypeId = ResolveAgentTypeId(UnitSize.Small);
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

    private static void LogDefendSpawnEvent(PlannedSpawnEvent evt, int entityId)
    {
        int agentTypeId = ResolveAgentTypeId(evt.UnitType);
        bool flowHit = FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
            evt.SpawnPosition,
            agentTypeId,
            NavigationPointProbeRadius,
            0f,
            out Vector3 legalPoint);

        Log.Info(
            "[DefendPhase] SpawnEvent entityId={0} unit={1} level={2} pos={3} speedProp={4:F2} point={5} stronghold={6} flowHit={7} flowPos={8} agentType={9}",
            entityId,
            evt.UnitType,
            evt.UnitLevel,
            evt.SpawnPosition,
            evt.SpeedProperty,
            evt.SpawnPointName,
            evt.SourceStrongholdId ?? "null",
            flowHit,
            flowHit ? legalPoint.ToString() : "none",
            agentTypeId);
    }

    private static int ResolveAgentTypeId(UnitSize unitSize)
    {
        AgentTypeHelper helper = GameEntry.GetComponent<AgentTypeHelper>();
        if (helper == null)
            throw new InvalidOperationException("DefendPhaseRuntime.ResolveAgentTypeId failed: AgentTypeHelper is not available.");

        return helper.GetNavAgentTypeID(unitSize);
    }

    private static int ResolveAgentTypeId(UnitType unitType)
    {
        AgentTypeHelper helper = GameEntry.GetComponent<AgentTypeHelper>();
        if (helper == null)
            throw new InvalidOperationException("DefendPhaseRuntime.ResolveAgentTypeId failed: AgentTypeHelper is not available.");

        return helper.GetNavAgentTypeID(unitType);
    }

    private static void EnsureWaveConfigLoaded()
    {
        string levelIdentifier = ResolveCurrentLevelIdentifier();
        if (string.IsNullOrWhiteSpace(levelIdentifier))
            return;

        if (string.Equals(s_WaveConfigLevelIdentifier, levelIdentifier, StringComparison.Ordinal) && s_DefendWaves.Count > 0)
            return;

        s_WaveConfigLevelIdentifier = levelIdentifier;
        s_DefendWaves.Clear();

        if (!LevelSelectionService.TryGetLevelRow(levelIdentifier, out LevelTable levelRow, out string errorMessage) || levelRow == null)
        {
            Log.Warning("[DefendPhase] 读取 LevelTable 行失败。level={0}, error={1}", levelIdentifier, errorMessage);
            return;
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

        Log.Info("[DefendPhase] 防御波次配置加载完成。level={0}, waves={1}", levelIdentifier, s_DefendWaves.Count);
    }

    private static string ResolveCurrentLevelIdentifier()
    {
        var inGameData = GF.DataModel != null ? GF.DataModel.GetDataModel<InGameDataModel>() : null;
        return inGameData?.lvData?.Identifier;
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
                continue;
            if (count <= 0)
                continue;

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
            return CloneWave(s_DefendWaves[Mathf.Max(0, roundIndex - 1)], 1f);

        int overflowRounds = roundIndex - s_DefendWaves.Count;
        float growthRate = GF.Config != null ? GF.Config.GetFloat(DefendEndlessGrowthRateConfigKey, 1f) : 1f;
        double scale = Math.Pow(Math.Max(0f, growthRate), overflowRounds);
        return CloneWave(s_DefendWaves[s_DefendWaves.Count - 1], (float)scale);
    }

    private static DefendWaveDefinition CloneWave(DefendWaveDefinition source, float scale)
    {
        if (source == null)
            return null;

        var clone = new DefendWaveDefinition();
        for (int i = 0; i < source.Entries.Count; i++)
        {
            DefendWaveEntry entry = source.Entries[i];
            int scaledCount = Mathf.RoundToInt(entry.Count * Mathf.Max(0f, scale));
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

    private static List<PlannedSpawnEvent> BuildSpawnEvents(DefendWaveDefinition wave)
    {
        var events = new List<PlannedSpawnEvent>();
        if (wave == null || wave.Entries.Count == 0 || s_DefendSpawnPoints.Count == 0)
            return events;

        float arriveInterval = GF.Config != null
            ? Mathf.Max(MinArriveIntervalSeconds, GF.Config.GetFloat(DefendEnemyArriveIntervalConfigKey, 0.8f))
            : 0.8f;
        float minSpeedProperty = GF.Config != null
            ? Mathf.Max(1f, GF.Config.GetFloat(DefendEnemyMinSpeedConfigKey, 500f))
            : 500f;
        float minSpeedWorld = Mathf.Max(MinWorldSpeed, DistanceUnitConverter.ConvertToWorldFloat((Fix64)minSpeedProperty));
        float conversionRate = Mathf.Max(0.0001f, DistanceUnitConverter.DistanceConversionRate);
        Vector3 basePosition = ResolvePlayerBasePosition();

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
                if (pointCount?.Point?.Point == null)
                    throw new InvalidOperationException($"DefendPhaseRuntime.BuildSpawnEvents failed: spawn point is null. unit={entry.UnitType} index={pointIndex}.");

                pointCount.DistanceToBase = CalculatePathDistance(
                    pointCount.Point.Point.Position,
                    basePosition,
                    entry.UnitType);
            }

            pointCounts.Sort((a, b) => a.DistanceToBase.CompareTo(b.DistanceToBase));

            float previousLastArrival = 0f;
            for (int pointIndex = 0; pointIndex < pointCounts.Count; pointIndex++)
            {
                PointSpawnCount pointCount = pointCounts[pointIndex];
                int spawnCount = pointCount.Count;
                if (spawnCount <= 0)
                    continue;

                float distance = Mathf.Max(0f, pointCount.DistanceToBase);
                float targetFirstArrival;
                float spawnDelay;
                float pointSpeedWorld;

                if (pointIndex == 0)
                {
                    targetFirstArrival = distance / minSpeedWorld;
                    spawnDelay = 0f;
                    pointSpeedWorld = minSpeedWorld;
                }
                else
                {
                    targetFirstArrival = previousLastArrival + arriveInterval;
                    float naturalArrival = distance / minSpeedWorld;
                    if (naturalArrival <= targetFirstArrival)
                    {
                        spawnDelay = targetFirstArrival - naturalArrival;
                        pointSpeedWorld = minSpeedWorld;
                    }
                    else
                    {
                        spawnDelay = 0f;
                        pointSpeedWorld = distance / Mathf.Max(targetFirstArrival, MinArriveIntervalSeconds);
                    }
                }

                float pointSpeedProperty = pointSpeedWorld / conversionRate;
                float pointLastArrival = targetFirstArrival + (spawnCount - 1) * arriveInterval;
                previousLastArrival = pointLastArrival;

                string strongholdId = pointCount.PointStronghold?.strongholdData?.StrongholdId;
                string pointName = pointCount.Point.Point != null ? pointCount.Point.Point.name : "<null>";

                for (int spawnIndex = 0; spawnIndex < spawnCount; spawnIndex++)
                {
                    events.Add(new PlannedSpawnEvent
                    {
                        Time = spawnDelay + spawnIndex * arriveInterval,
                        UnitType = entry.UnitType,
                        UnitLevel = entry.UnitLevel,
                        SpawnPosition = pointCount.Point.Point.Position,
                        SpeedProperty = pointSpeedProperty,
                        SourceStrongholdId = strongholdId,
                        SpawnPointName = pointName,
                        TheoreticalArrivalTime = targetFirstArrival + spawnIndex * arriveInterval
                    });
                }
            }
        }

        return events;
    }

    private static List<PointSpawnCount> AllocatePointCountsForUnit(UnitType unitType, int totalCount)
    {
        var result = new List<PointSpawnCount>();
        if (totalCount <= 0 || s_DefendSpawnPoints.Count == 0)
            return result;

        bool hasArchetype = s_ArchetypeByUnitType.TryGetValue(unitType, out Archetype unitArchetype);
        var eligiblePoints = new List<DefendSpawnPointRuntime>();
        float totalWeightAllSides = 0f;

        for (int i = 0; i < s_DefendSpawnPoints.Count; i++)
        {
            DefendSpawnPointRuntime runtimePoint = s_DefendSpawnPoints[i];
            Stronghold stronghold = LevelEntity.GetStrongholdAtWorldPosition(runtimePoint.Point.Position);
            if (stronghold == null)
                continue;

            bool archMatched = !hasArchetype || StrongholdContainsArchetypeBuilding(stronghold, unitArchetype);
            if (!archMatched)
                continue;

            int weight = Mathf.Max(0, runtimePoint.Point.DefendSpawnWeight);
            if (weight <= 0)
                continue;

            eligiblePoints.Add(runtimePoint);
            totalWeightAllSides += weight;
        }

        if (eligiblePoints.Count == 0 || totalWeightAllSides <= 0f)
        {
            for (int i = 0; i < s_DefendSpawnPoints.Count; i++)
            {
                var runtimePoint = s_DefendSpawnPoints[i];
                Stronghold stronghold = LevelEntity.GetStrongholdAtWorldPosition(runtimePoint.Point.Position);
                if (stronghold == null || stronghold.OwnerFactionId == EntitySideHelper.PlayerFactionId)
                    continue;

                int weight = Mathf.Max(0, runtimePoint.Point.DefendSpawnWeight);
                if (weight <= 0)
                    continue;

                eligiblePoints.Add(runtimePoint);
                totalWeightAllSides += weight;
            }
        }

        if (eligiblePoints.Count == 0 || totalWeightAllSides <= 0f)
            return result;

        int allocatedTotal = 0;
        for (int i = 0; i < eligiblePoints.Count; i++)
        {
            DefendSpawnPointRuntime runtimePoint = eligiblePoints[i];
            Stronghold stronghold = LevelEntity.GetStrongholdAtWorldPosition(runtimePoint.Point.Position);
            if (stronghold == null || stronghold.OwnerFactionId == EntitySideHelper.PlayerFactionId)
                continue;

            int weight = Mathf.Max(0, runtimePoint.Point.DefendSpawnWeight);
            int count = Mathf.RoundToInt(weight / totalWeightAllSides * totalCount);
            if (count <= 0)
                continue;

            allocatedTotal += count;
            result.Add(new PointSpawnCount
            {
                Point = runtimePoint,
                PointStronghold = stronghold,
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

    private static bool StrongholdContainsArchetypeBuilding(Stronghold stronghold, Archetype archetype)
    {
        if (stronghold?.Buildings == null || archetype == Archetype.None)
            return false;

        for (int i = 0; i < stronghold.Buildings.Count; i++)
        {
            BuildingEntity building = stronghold.Buildings[i];
            if (building == null || building.buildingData == null)
                continue;

            if (building.buildingData.Arche == archetype)
                return true;
        }

        return false;
    }

    private static string ResolvePreviewSpawnPointIdentifier(EntityPresetPoint point)
    {
        if (point == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(point.Identifier))
            return point.Identifier;

        return string.IsNullOrWhiteSpace(point.name) ? string.Empty : point.name;
    }

    private static void ResetDefendPhaseState(bool keepRoundIndex)
    {
        s_AliveEnemyEntityIds.Clear();
        s_SpawnScheduleCompleted = false;
        if (!keepRoundIndex)
            s_DefendRoundIndex = 0;
    }

    private sealed class DefendSpawnPointRuntime
    {
        public EntityPresetPoint Point;
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
        public Stronghold PointStronghold;
        public int Count;
        public float DistanceToBase;
    }

    private sealed class PlannedSpawnEvent
    {
        public float Time;
        public UnitType UnitType;
        public int UnitLevel;
        public Vector3 SpawnPosition;
        public float SpeedProperty;
        public string SourceStrongholdId;
        public string SpawnPointName;
        public float TheoreticalArrivalTime;
    }
}
