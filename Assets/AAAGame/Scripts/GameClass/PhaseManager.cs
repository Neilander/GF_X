using System;
using System.Diagnostics;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityGameFramework.Runtime;

public class PhaseManager : GameFrameworkComponent
{
    public static event Action<GamePhase, GamePhase> OnPhaseChanged;
    internal static event Action<GamePhase> PersistentStageCommitted;

    private const float EnemyPresetClusterRadius = 3f;
    private const float EnemyPresetClusterMinDistance = 1.2f;
    private const long PhaseStepWarnMs = 30;
    private const string EnemyProductionBuildingDailyResourceCostConfigKey = "EnemyProductionBuildingDailyResourceCost";
    private static int s_PhaseSoundToken;

    public static GamePhase CurrentPhase => (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);

    public static void CancelRuntimePhaseFlows()
    {
        s_PhaseSoundToken++;
        DefendPhaseRuntime.CancelRuntime();
    }

    public static void EnterCurrentPhaseOnGameStart()
    {
        DefendPhaseRuntime.PrepareForCurrentLevelIfNeeded();
        GamePhase currentPhase = CurrentPhase;
        LogicPhaseCommandService.SetInitialPhase(currentPhase);
        switch (currentPhase)
        {
            case GamePhase.BuildBeforeInvade:
            case GamePhase.BuildBeforeDefend:
                LogicBuildingProductionService.PrepareBuildPhase(false);
                CommitBuildPhasePersistentState(currentPhase, true);
                PublishPersistentStageBoundary(currentPhase);
                HandleEnterBuildPhase();
                break;
            case GamePhase.Invade:
                PublishPersistentStageBoundary(currentPhase);
                HandleEnterInvadePhase();
                break;
            case GamePhase.Defend:
                PublishPersistentStageBoundary(currentPhase);
                HandleEnterDefendPhase();
                break;
        }

        Log.Debug($"Initial phase entered: {currentPhase}");
    }

    public static void EnterRestoredPhaseOnGameStart(GamePhase restoredPhase)
    {
        DefendPhaseRuntime.PrepareForCurrentLevelIfNeeded();
        if (CurrentPhase != restoredPhase)
            throw new InvalidOperationException(
                $"Restored phase mismatch. checkpoint={restoredPhase}, dataModel={CurrentPhase}.");
        LogicPhaseCommandService.SetInitialPhase(restoredPhase);
        HandlePhaseTransition(restoredPhase, restoredPhase);
        Log.Debug($"Restored phase entered: {restoredPhase}");
    }

    public static void SwitchToNextPhase()
    {
        GamePhase currentPhase = CurrentPhase;
        GamePhase nextPhase;

        switch (currentPhase)
        {
            case GamePhase.BuildBeforeInvade:
                nextPhase = GamePhase.Invade;
                break;
            case GamePhase.Invade:
                nextPhase = GamePhase.BuildBeforeDefend;
                break;
            case GamePhase.BuildBeforeDefend:
                nextPhase = GamePhase.Defend;
                break;
            case GamePhase.Defend:
                nextPhase = GamePhase.BuildBeforeInvade;
                break;
            default:
                nextPhase = GamePhase.BuildBeforeInvade;
                break;
        }

        SwitchToPhase(nextPhase);
    }

    public static void SwitchToPhase(GamePhase phase)
    {
        LogicPhaseCommandService.ScheduleForNextFrame(phase);
    }

    internal static void ApplyScheduledPhase(GamePhase phase)
    {
        if (!LogicPhaseCommandService.IsApplyingFrame)
            throw new InvalidOperationException("PhaseManager.ApplyScheduledPhase is only valid while applying a logic phase command.");

        GamePhase oldPhase = CurrentPhase;
        if (oldPhase == phase)
        {
            return;
        }

        var totalWatch = Stopwatch.StartNew();

        TryAdvanceDayOnBuildTransition(oldPhase, phase);
        InGameDataModel.SetPhase(phase);
        if (InGameDataModel.IsBuildPhase(phase))
        {
            LogicBuildingProductionService.PrepareBuildPhase(true);
            CommitBuildPhasePersistentState(oldPhase, false);
        }

        PublishPersistentStageBoundary(phase);

        var transitionWatch = Stopwatch.StartNew();
        HandlePhaseTransition(oldPhase, phase);
        transitionWatch.Stop();
        LogPhaseStep($"transition {oldPhase}->{phase}", transitionWatch.ElapsedMilliseconds);

        var eventWatch = Stopwatch.StartNew();
        OnPhaseChanged?.Invoke(oldPhase, phase);
        eventWatch.Stop();
        LogPhaseStep($"phase-event {oldPhase}->{phase}", eventWatch.ElapsedMilliseconds);

        totalWatch.Stop();
        LogPhaseStep($"switch-total {oldPhase}->{phase}", totalWatch.ElapsedMilliseconds);

        Log.Debug($"Phase switched from {oldPhase} to {phase}");
    }

    private static void TryAdvanceDayOnBuildTransition(GamePhase oldPhase, GamePhase newPhase)
    {
        if (!InGameDataModel.IsBuildPhase(newPhase))
        {
            return;
        }

        InGameDataModel.TryModifyValue(IngameValueType.Day, 1);
        int currentDay = InGameDataModel.GetValue(IngameValueType.Day);
        Log.Debug($"Day advanced to {currentDay} when entering build phase {newPhase} from {oldPhase}");
    }

    private static void HandlePhaseTransition(GamePhase oldPhase, GamePhase newPhase)
    {
        switch (newPhase)
        {
            case GamePhase.BuildBeforeInvade:
            case GamePhase.BuildBeforeDefend:
                HandleEnterBuildPhase();
                break;
            case GamePhase.Invade:
                HandleEnterInvadePhase();
                break;
            case GamePhase.Defend:
                HandleEnterDefendPhase();
                break;
        }
    }

    private static void HandleEnterBuildPhase()
    {
        PlayPhaseEnterSound("enterManage");

        DefendPhaseRuntime.CancelRuntime();

        var totalWatch = Stopwatch.StartNew();

        var removeSoldiersWatch = Stopwatch.StartNew();
        RemoveAllSoldiers();
        removeSoldiersWatch.Stop();
        LogPhaseStep("build.remove-soldiers", removeSoldiersWatch.ElapsedMilliseconds);

        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("PhaseManager.HandleEnterBuildPhase failed: InputModel is unavailable.");
        inputModel.ClearSkillRequests();

        CardSetup cardSetup = GameEntry.GetComponent<CardSetup>();
        if (cardSetup != null)
        {
            var shutdownWatch = Stopwatch.StartNew();
            cardSetup.CardSystemShutdown();
            shutdownWatch.Stop();
            LogPhaseStep("build.card-shutdown", shutdownWatch.ElapsedMilliseconds);
        }

        totalWatch.Stop();
        LogPhaseStep("build.total", totalWatch.ElapsedMilliseconds);
    }

    private static void CommitBuildPhasePersistentState(GamePhase previousPhase, bool isFirstPhase)
    {
        var rewardWatch = Stopwatch.StartNew();
        ConsumeEnemyProductionBuildingCoinReservesOnBuildPhaseEnter();
        RewardManager.HandleEnterBuildPhaseReward(isFirstPhase, previousPhase);
        rewardWatch.Stop();
        LogPhaseStep("build.persistent", rewardWatch.ElapsedMilliseconds);
    }

    private static void PublishPersistentStageBoundary(GamePhase phase)
    {
        PersistentStageCommitted?.Invoke(phase);
    }

    private static void ConsumeEnemyProductionBuildingCoinReservesOnBuildPhaseEnter()
    {
        int consumeCost = GF.Config != null ? GF.Config.GetInt(EnemyProductionBuildingDailyResourceCostConfigKey, 0) : 0;
        consumeCost = LevelTagRuntime.ModifyEnemyProductionDailyResourceCost(consumeCost);
        if (consumeCost <= 0)
            return;

        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!(entities[i] is IBuildingLogicContext building)
                || !building.Alive
                || building.IsDisabled
                || building.BuildingData == null
                || building.OwnerFactionId == EntitySideHelper.PlayerFactionId
                || building.BuildingData.Type != BuilType.Prod
                || building.BuildingData.Lv <= 0)
                continue;

            InGameDataModel.ConsumeProductionBuildingCoinReserves(building.BuildingInstanceId, consumeCost);
        }
    }

    private static void HandleEnterInvadePhase()
    {
        PlayPhaseEnterSound("enterBattle");

        DefendPhaseRuntime.CancelRuntime();
        var totalWatch = Stopwatch.StartNew();

        PrepareBattlePhaseCards("invade");

        var spawnEnemyWatch = Stopwatch.StartNew();
        SpawnEnemySoldiers();
        spawnEnemyWatch.Stop();
        LogPhaseStep("invade.spawn-enemy", spawnEnemyWatch.ElapsedMilliseconds);

        totalWatch.Stop();
        LogPhaseStep("invade.total", totalWatch.ElapsedMilliseconds);
    }

    private static void HandleEnterDefendPhase()
    {
        PlayPhaseEnterSound("enterBattle");
        PrepareBattlePhaseCards("defend");
        DefendPhaseRuntime.EnterDefendPhase();
    }

    private static void PrepareBattlePhaseCards(string phaseTag)
    {
        CardSetup cardSetup = GameEntry.GetComponent<CardSetup>();
        if (cardSetup == null)
            return;

        var setupWatch = Stopwatch.StartNew();
        cardSetup.CardSystemSetup();
        setupWatch.Stop();
        LogPhaseStep($"{phaseTag}.card-setup", setupWatch.ElapsedMilliseconds);

        var generateCardsWatch = Stopwatch.StartNew();
        int generatedCardCount = GenerateCardsFromArmyBuildings();
        generateCardsWatch.Stop();
        LogPhaseStep($"{phaseTag}.generate-cards", generateCardsWatch.ElapsedMilliseconds);

        if (generatedCardCount > 0)
        {
            var openUiWatch = Stopwatch.StartNew();
            cardSetup.OpenCardUI();
            openUiWatch.Stop();
            LogPhaseStep($"{phaseTag}.open-card-ui", openUiWatch.ElapsedMilliseconds);
        }
        else
        {
            Log.Info("[CardGame] Skip opening Card UI: no cards generated for this phase.");
        }
    }

    /// <summary>阶段进入时的统一音效播放入口（cue key 在 AudioCueLibrary 配映射）。</summary>
    private static void PlayPhaseEnterSound(string cueKey)
    {
        if (AudioManager.Instance == null) return;
        int token = ++s_PhaseSoundToken;
        if (LogicTimeControlService.IsPaused)
        {
            PlayPhaseEnterSoundWhenUnpausedAsync(cueKey, token).Forget();
            return;
        }

        AudioManager.Instance.Play(cueKey);
    }

    private static async UniTaskVoid PlayPhaseEnterSoundWhenUnpausedAsync(string cueKey, int token)
    {
        await UniTask.WaitUntil(() => !LogicTimeControlService.IsPaused, PlayerLoopTiming.Update);

        if (token == s_PhaseSoundToken && AudioManager.Instance != null)
        {
            AudioManager.Instance.Play(cueKey);
        }
    }

    private static void RemoveAllSoldiers()
    {
        SoldierFactory.RemoveAllSoldiersInCreatureGroup();
    }

    private static int GenerateCardsFromArmyBuildings()
    {
        var watch = Stopwatch.StartNew();

        var cardSetup = GameEntry.GetComponent<CardSetup>();
        int scanBuildingCount = 0;
        int generatedCardCount = 0;

        if (cardSetup != null)
        {
            IList<IEntityContext> entities = EntityRegistry.AllEntities;
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i] is not IBuildingLogicContext building)
                    continue;
                scanBuildingCount++;
                if (!building.Alive
                    || building.IsDisabled
                    || building.BuildingData == null
                    || building.BuildingData.Type != BuilType.Army
                    || building.OwnerFactionId != EntitySideHelper.PlayerFactionId)
                    continue;

                if (cardSetup.GenerateCardToDeck(building))
                    generatedCardCount++;
            }
        }

        watch.Stop();
        LogPhaseStep($"generate-cards.detail scan={scanBuildingCount},generated={generatedCardCount}", watch.ElapsedMilliseconds);
        return generatedCardCount;
    }

    private static void SpawnEnemySoldiers()
    {
        var totalWatch = Stopwatch.StartNew();
        LogCreatureEntityPoolState("before-spawn");

        var findWatch = Stopwatch.StartNew();
        var presetPoints = GameObject.FindObjectsOfType<EntityPresetPoint>();
        findWatch.Stop();
        LogPhaseStep($"spawn-enemy.find-preset-points count={presetPoints.Length}", findWatch.ElapsedMilliseconds);

        var spawnPlans = new List<InvadeSpawnPlan>();
        for (int i = 0; i < presetPoints.Length; i++)
        {
            EntityPresetPoint point = presetPoints[i];
            if (point == null || point.PointType != EntityPresetPointType.Unit)
                continue;

            Vector3 authoredPosition = point.Position;
            var position = new FixVector2((Fix64)authoredPosition.x, (Fix64)authoredPosition.z);
            if (!LogicStrongholdMap.TryResolveStrongholdId(position, out string strongholdId))
            {
                throw new InvalidOperationException(
                    $"PhaseManager invade spawn point is outside the logic stronghold map. point={point.name} raw=({position.x.RawValue},{position.y.RawValue}).");
            }
            if (!LogicBuildingQueryService.TryResolveStrongholdOwnerFaction(strongholdId, out int ownerFactionId)
                || ownerFactionId == EntitySideHelper.PlayerFactionId)
            {
                continue;
            }
            if (!UnitTypeHelper.TryParseUnitTypeAndLevel(point.Identifier, out UnitType unitType, out int unitLevel))
            {
                throw new InvalidOperationException(
                    $"PhaseManager invade spawn point has invalid unit identifier. point={point.name} identifier={point.Identifier}.");
            }

            int count = EnemyArmyForceModifierService.CalculateSpawnCount(point.UnitSpawnCount);
            if (count <= 0)
                throw new InvalidOperationException($"PhaseManager invade spawn count is not positive. point={point.name} count={count}.");

            spawnPlans.Add(new InvadeSpawnPlan(
                position,
                unitType,
                unitLevel,
                count,
                strongholdId,
                string.IsNullOrWhiteSpace(point.name) ? "<unnamed>" : point.name));
        }
        spawnPlans.Sort(CompareInvadeSpawnPlans);

        int spawnedCount = 0;
        long totalSpawnClusterMs = 0;
        long maxSingleSpawnClusterMs = 0;

        for (int i = 0; i < spawnPlans.Count; i++)
        {
            InvadeSpawnPlan plan = spawnPlans[i];
            var spawnWatch = Stopwatch.StartNew();
            bool clusterSpawned = ClusterSpawnSystem.SpawnCluster(
                new Vector3((float)plan.Position.x, 0f, (float)plan.Position.y),
                plan.Count,
                EnemyPresetClusterRadius,
                EnemyPresetClusterMinDistance,
                plan.UnitType,
                SideType.EnemySide,
                BrainType.SoldierAI,
                null,
                plan.StrongholdId,
                unitLevel: plan.UnitLevel);
            spawnWatch.Stop();

            long spawnMs = spawnWatch.ElapsedMilliseconds;
            totalSpawnClusterMs += spawnMs;
            if (spawnMs > maxSingleSpawnClusterMs)
            {
                maxSingleSpawnClusterMs = spawnMs;
            }

            if (!clusterSpawned)
            {
                throw new InvalidOperationException(
                    $"PhaseManager invade cluster spawn failed. point={plan.Name} requested={plan.Count}.");
            }
            spawnedCount = checked(spawnedCount + plan.Count);
        }

        totalWatch.Stop();
        LogCreatureEntityPoolState("after-spawn");
        LogPhaseStep(
            $"spawn-enemy.detail clusters={spawnPlans.Count},units={spawnedCount},clusterTotalMs={totalSpawnClusterMs},clusterMaxMs={maxSingleSpawnClusterMs}",
            totalWatch.ElapsedMilliseconds);

        Log.Debug($"Spawned {spawnedCount} enemy soldiers from enemy stronghold unit preset points");
    }

    private static int CompareInvadeSpawnPlans(InvadeSpawnPlan left, InvadeSpawnPlan right)
    {
        int stronghold = string.CompareOrdinal(left.StrongholdId, right.StrongholdId);
        if (stronghold != 0)
            return stronghold;
        int unitType = left.UnitType.CompareTo(right.UnitType);
        if (unitType != 0)
            return unitType;
        int level = left.UnitLevel.CompareTo(right.UnitLevel);
        if (level != 0)
            return level;
        int x = left.Position.x.RawValue.CompareTo(right.Position.x.RawValue);
        if (x != 0)
            return x;
        int y = left.Position.y.RawValue.CompareTo(right.Position.y.RawValue);
        return y != 0 ? y : string.CompareOrdinal(left.Name, right.Name);
    }

    private readonly struct InvadeSpawnPlan
    {
        public InvadeSpawnPlan(
            FixVector2 position,
            UnitType unitType,
            int unitLevel,
            int count,
            string strongholdId,
            string name)
        {
            Position = position;
            UnitType = unitType;
            UnitLevel = unitLevel;
            Count = count;
            StrongholdId = strongholdId;
            Name = name;
        }

        public FixVector2 Position { get; }
        public UnitType UnitType { get; }
        public int UnitLevel { get; }
        public int Count { get; }
        public string StrongholdId { get; }
        public string Name { get; }
    }

    private static void LogPhaseStep(string step, long elapsedMs)
    {
        if (elapsedMs >= PhaseStepWarnMs)
        {
            Log.Warning("[PhasePerf] {0}: {1}ms", step, elapsedMs);
        }
    }

    private static void LogCreatureEntityPoolState(string step)
    {
        if (GF.ObjectPool == null)
        {
            return;
        }

        var pool = GF.ObjectPool.GetObjectPool(p => p != null && p.FullName.Contains("Entity Instance Pool (Creature)"));
        if (pool == null)
        {
            Log.Warning("[PhasePerf] creature-pool.{0}: not found", step);
        }
        else if (pool.Count >= pool.Capacity)
        {
            Log.Warning(
                "[PhasePerf] creature-pool.{0}: count={1},canRelease={2},capacity={3},expire={4}",
                step,
                pool.Count,
                pool.CanReleaseCount,
                pool.Capacity,
                pool.ExpireTime);
        }
    }
}
