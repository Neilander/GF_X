using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework.Event;
using UnityEngine;
using UnityEngine.AI;
using UnityGameFramework.Runtime;

public static class DefendPhaseRuntime
{
    private const string DefendEnemyArriveIntervalConfigKey = "DefendPhaseEnemyArriveInterval";
    private const string DefendEnemyMinSpeedConfigKey = "DefendPhaseEnemyMinSpeed";
    private const string DefendEndlessGrowthRateConfigKey = "DefendPhaseEnemyEndlessGrowthRate";
    private const float MinArriveIntervalSeconds = 0.01f;
    private const float MinWorldSpeed = 0.001f;

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

    public static void CancelRuntime()
    {
        s_FlowToken++;
        ResetDefendPhaseState(keepRoundIndex: true);
    }

    public static void PrepareForCurrentLevelIfNeeded()
    {
        EnsureSubscribedSoldierDead();
        EnsureArchetypeCache();
        EnsureSpawnPointCache();
        EnsureWaveConfigLoaded();
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
                BrainType.SoldierAI,
                null,
                evt.SourceStrongholdId);
            if (entityId <= 0)
            {
                Log.Warning("[DefendPhase] 生成单位失败。unit={0}, pos={1}", evt.UnitType, evt.SpawnPosition);
                continue;
            }

            s_AliveEnemyEntityIds.Add(entityId);
            SoldierEntity soldier = GF.Entity.GetEntity(entityId)?.Logic as SoldierEntity;
            if (soldier != null)
            {
                soldier.EnableDefendPhaseSpeedControl((Fix64)evt.SpeedProperty);
            }
        }

        if (flowToken != s_FlowToken || PhaseManager.CurrentPhase != GamePhase.Defend)
            return;

        s_SpawnScheduleCompleted = true;
        TryCompleteDefendPhase();
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

        Vector3 basePosition = ResolvePlayerBasePosition();
        EntityPresetPoint[] points = levelEntity.GetComponentsInChildren<EntityPresetPoint>(true);
        for (int i = 0; i < points.Length; i++)
        {
            EntityPresetPoint point = points[i];
            if (point == null || point.PointType != EntityPresetPointType.DefendSpawn)
                continue;

            float distance = CalculatePathDistance(point.Position, basePosition);
            s_DefendSpawnPoints.Add(new DefendSpawnPointRuntime
            {
                Point = point,
                DistanceToPlayerBase = Mathf.Max(0f, distance)
            });
        }

        Log.Info("[DefendPhase] 已缓存防御出怪点。count={0}, base={1}", s_DefendSpawnPoints.Count, basePosition);
    }

    private static Vector3 ResolvePlayerBasePosition()
    {
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

    private static float CalculatePathDistance(Vector3 from, Vector3 to)
    {
        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) || path == null || path.corners == null || path.corners.Length < 2)
            return Vector3.Distance(from, to);

        float total = 0f;
        for (int i = 1; i < path.corners.Length; i++)
        {
            total += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }

        return total > 0f ? total : Vector3.Distance(from, to);
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
            if (!UnitTypeHelper.TryParseUnitType(unitId, out UnitType unitType))
                continue;
            if (count <= 0)
                continue;

            wave.Entries.Add(new DefendWaveEntry
            {
                UnitType = unitType,
                Count = count
            });
        }

        if (wave.Entries.Count > 0)
            s_DefendWaves.Add(wave);
    }

    private static DefendWaveDefinition ResolveWaveForCurrentRound()
    {
        if (s_DefendWaves.Count == 0)
            return null;

        if (s_DefendRoundIndex <= s_DefendWaves.Count)
            return CloneWave(s_DefendWaves[s_DefendRoundIndex - 1], 1f);

        int overflowRounds = s_DefendRoundIndex - s_DefendWaves.Count;
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

        for (int i = 0; i < wave.Entries.Count; i++)
        {
            DefendWaveEntry entry = wave.Entries[i];
            if (entry.Count <= 0)
                continue;

            List<PointSpawnCount> pointCounts = AllocatePointCountsForUnit(entry.UnitType, entry.Count);
            if (pointCounts.Count == 0)
                continue;

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
                for (int spawnIndex = 0; spawnIndex < spawnCount; spawnIndex++)
                {
                    events.Add(new PlannedSpawnEvent
                    {
                        Time = spawnDelay + spawnIndex * arriveInterval,
                        UnitType = entry.UnitType,
                        SpawnPosition = pointCount.Point.Point.Position,
                        SpeedProperty = pointSpeedProperty,
                        SourceStrongholdId = strongholdId
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
                Count = count,
                DistanceToBase = runtimePoint.DistanceToPlayerBase
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
        public float DistanceToPlayerBase;
    }

    private sealed class DefendWaveDefinition
    {
        public readonly List<DefendWaveEntry> Entries = new();
    }

    private sealed class DefendWaveEntry
    {
        public UnitType UnitType;
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
        public Vector3 SpawnPosition;
        public float SpeedProperty;
        public string SourceStrongholdId;
    }
}
