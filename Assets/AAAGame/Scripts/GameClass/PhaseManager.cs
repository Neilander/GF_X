using System;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityGameFramework.Runtime;

public class PhaseManager : GameFrameworkComponent
{
    public static event Action<GamePhase, GamePhase> OnPhaseChanged;

    private const float EnemyPresetClusterRadius = 3f;
    private const float EnemyPresetClusterMinDistance = 1.2f;
    private const long PhaseStepWarnMs = 30;
    private const int EnemySpawnYieldEveryUnits = 2;
    private const string EnemyProductionBuildingDailyResourceCostConfigKey = "EnemyProductionBuildingDailyResourceCost";
    private static int s_InvadeFlowToken;
    private static int s_PhaseSoundToken;

    public static GamePhase CurrentPhase => (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);

    public static void CancelRuntimePhaseFlows()
    {
        s_InvadeFlowToken++;
        s_PhaseSoundToken++;
        DefendPhaseRuntime.CancelRuntime();
    }

    public static void EnterCurrentPhaseOnGameStart()
    {
        DefendPhaseRuntime.PrepareForCurrentLevelIfNeeded();
        GamePhase currentPhase = CurrentPhase;
        switch (currentPhase)
        {
            case GamePhase.BuildBeforeInvade:
            case GamePhase.BuildBeforeDefend:
                HandleEnterBuildPhase(currentPhase, true);
                break;
            case GamePhase.Invade:
                HandleEnterInvadePhaseAsync().Forget();
                break;
            case GamePhase.Defend:
                HandleEnterDefendPhase();
                break;
        }

        Log.Debug($"Initial phase entered: {currentPhase}");
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
        GamePhase oldPhase = CurrentPhase;
        if (oldPhase == phase)
        {
            return;
        }

        var totalWatch = Stopwatch.StartNew();

        TryAdvanceDayOnBuildTransition(oldPhase, phase);
        InGameDataModel.SetPhase(phase);

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
                HandleEnterBuildPhase(oldPhase);
                break;
            case GamePhase.Invade:
                HandleEnterInvadePhase();
                break;
            case GamePhase.Defend:
                HandleEnterDefendPhase();
                break;
        }
    }

    private static void HandleEnterBuildPhase(GamePhase previousPhase, bool isFirstPhase = false)
    {
        PlayPhaseEnterSound("enterManage");

        // 进入 Build 时取消未完成的 Invade 异步生成流程。
        s_InvadeFlowToken++;
        DefendPhaseRuntime.CancelRuntime();

        var totalWatch = Stopwatch.StartNew();

        var removeSoldiersWatch = Stopwatch.StartNew();
        RemoveAllSoldiers();
        removeSoldiersWatch.Stop();
        LogPhaseStep("build.remove-soldiers", removeSoldiersWatch.ElapsedMilliseconds);

        var rewardWatch = Stopwatch.StartNew();
        ConsumeEnemyProductionBuildingCoinReservesOnBuildPhaseEnter();
        RewardManager.HandleEnterBuildPhaseReward(isFirstPhase, previousPhase);
        rewardWatch.Stop();
        LogPhaseStep("build.reward", rewardWatch.ElapsedMilliseconds);

        try
        {
            var restoreWatch = Stopwatch.StartNew();
            int restoredBuildingCount = 0;

            var ingameData = GF.DataModel.GetOrCreate<InGameDataModel>();
            if (ingameData != null)
            {
                foreach (var building in ingameData.Buildings)
                {
                    if (building == null)
                    {
                        continue;
                    }

                    var stronghold = building.CurrentStronghold;
                    if (stronghold != null && stronghold.OwnerFactionId == EntitySideHelper.PlayerFactionId)
                    {
                        building.RestoreToFullHealthAndEnable();
                        restoredBuildingCount++;
                    }
                }
            }

            restoreWatch.Stop();
            LogPhaseStep($"build.restore-buildings count={restoredBuildingCount}", restoreWatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Log.Warning($"[PhaseManager] Restore buildings on enter build phase failed: {ex}");
        }

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

    private static void ConsumeEnemyProductionBuildingCoinReservesOnBuildPhaseEnter()
    {
        int consumeCost = GF.Config != null ? GF.Config.GetInt(EnemyProductionBuildingDailyResourceCostConfigKey, 0) : 0;
        consumeCost = LevelTagRuntime.ModifyEnemyProductionDailyResourceCost(consumeCost);
        if (consumeCost <= 0)
            return;

        InGameDataModel inGameData = GF.DataModel != null ? GF.DataModel.GetDataModel<InGameDataModel>() : null;
        if (inGameData == null)
            return;

        foreach (var building in inGameData.Buildings)
        {
            if (building == null || building.buildingData == null)
                continue;

            if (building.OwnerFactionID == EntitySideHelper.PlayerFactionId)
                continue;

            if (building.buildingData.Type != BuilType.Prod || building.buildingData.Lv <= 0)
                continue;

            InGameDataModel.ConsumeProductionBuildingCoinReserves(building.BuildingInstanceId, consumeCost);
        }
    }

    private static void HandleEnterInvadePhase()
    {
        HandleEnterInvadePhaseAsync().Forget();
    }

    private static async UniTaskVoid HandleEnterInvadePhaseAsync()
    {
        PlayPhaseEnterSound("enterBattle");

        int flowToken = ++s_InvadeFlowToken;
        DefendPhaseRuntime.CancelRuntime();
        var totalWatch = Stopwatch.StartNew();

        PrepareBattlePhaseCards("invade");

        var spawnEnemyWatch = Stopwatch.StartNew();
        await SpawnEnemySoldiersAsync(flowToken);
        spawnEnemyWatch.Stop();
        LogPhaseStep("invade.spawn-enemy-async", spawnEnemyWatch.ElapsedMilliseconds);

        totalWatch.Stop();
        LogPhaseStep("invade.total-async", totalWatch.ElapsedMilliseconds);
    }

    private static void HandleEnterDefendPhase()
    {
        PlayPhaseEnterSound("enterBattle");
        PrepareBattlePhaseCards("defend");
        DefendPhaseRuntime.EnterDefendPhaseAsync().Forget();
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
        if (GF.Base != null && GF.Base.IsGamePaused)
        {
            PlayPhaseEnterSoundWhenUnpausedAsync(cueKey, token).Forget();
            return;
        }

        AudioManager.Instance.Play(cueKey);
    }

    private static async UniTaskVoid PlayPhaseEnterSoundWhenUnpausedAsync(string cueKey, int token)
    {
        await UniTask.WaitUntil(() => GF.Base == null || !GF.Base.IsGamePaused, PlayerLoopTiming.Update);

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

        var ingameData = GF.DataModel.GetOrCreate<InGameDataModel>();
        var cardSetup = GameEntry.GetComponent<CardSetup>();
        int scanBuildingCount = 0;
        int generatedCardCount = 0;

        if (cardSetup != null && ingameData != null)
        {
            foreach (var building in ingameData.Buildings)
            {
                scanBuildingCount++;
                if (building == null || building.CurrentStronghold == null)
                {
                    continue;
                }

                if (building.buildingData.Type == BuilType.Army
                    && building.CurrentStronghold.OwnerFactionId == EntitySideHelper.PlayerFactionId)
                {
                    if (cardSetup.GenerateCardToDeck(building))
                    {
                        generatedCardCount++;
                    }
                }
            }
        }

        watch.Stop();
        LogPhaseStep($"generate-cards.detail scan={scanBuildingCount},generated={generatedCardCount}", watch.ElapsedMilliseconds);
        return generatedCardCount;
    }

    private static async UniTask SpawnEnemySoldiersAsync(int flowToken)
    {
        var totalWatch = Stopwatch.StartNew();
        LogCreatureEntityPoolState("before-spawn");

        var findWatch = Stopwatch.StartNew();
        var presetPoints = GameObject.FindObjectsOfType<EntityPresetPoint>();
        findWatch.Stop();
        LogPhaseStep($"spawn-enemy.find-preset-points count={presetPoints.Length}", findWatch.ElapsedMilliseconds);

        int spawnedCount = 0;
        int attemptedClusterCount = 0;
        long totalSpawnClusterMs = 0;
        long maxSingleSpawnClusterMs = 0;

        foreach (var point in presetPoints)
        {
            if (point == null || point.PointType != EntityPresetPointType.Unit)
            {
                continue;
            }

            if (flowToken != s_InvadeFlowToken || CurrentPhase != GamePhase.Invade)
            {
                Log.Warning("[PhasePerf] spawn-enemy canceled: phase changed while spawning.");
                break;
            }

            var stronghold = LevelEntity.GetStrongholdAtWorldPosition(point.Position);
            if (stronghold == null || stronghold.OwnerFactionId == EntitySideHelper.PlayerFactionId)
            {
                continue;
            }

            if (!UnitTypeHelper.TryParseUnitTypeAndLevel(point.Identifier, out var unitType, out int unitLevel))
            {
                Log.Warning($"Skip unit preset point '{point.name}': invalid identifier '{point.Identifier}'.");
                continue;
            }

            int count = EnemyArmyForceModifierService.CalculateSpawnCount(point.UnitSpawnCount);
            if (count <= 0)
            {
                Log.Warning($"Skip unit preset point '{point.name}': UnitSpawnCount={count}.");
                continue;
            }

            attemptedClusterCount++;

            var spawnWatch = Stopwatch.StartNew();
            int clusterSpawned = await ClusterSpawnSystem.SpawnClusterAwait(
                point.Position,
                count,
                EnemyPresetClusterRadius,
                EnemyPresetClusterMinDistance,
                unitType,
                SideType.EnemySide,
                BrainType.SoldierAI,
                null,
                EnemySpawnYieldEveryUnits,
                () => flowToken == s_InvadeFlowToken && CurrentPhase == GamePhase.Invade,
                stronghold.strongholdData.StrongholdId,
                unitLevel: unitLevel);
            spawnWatch.Stop();

            long spawnMs = spawnWatch.ElapsedMilliseconds;
            totalSpawnClusterMs += spawnMs;
            if (spawnMs > maxSingleSpawnClusterMs)
            {
                maxSingleSpawnClusterMs = spawnMs;
            }

            if (clusterSpawned > 0)
            {
                spawnedCount += clusterSpawned;
            }
            else
            {
                Log.Warning($"Cluster spawn failed at preset point '{point.name}', requestedCount={count}.");
            }
        }

        totalWatch.Stop();
        LogCreatureEntityPoolState("after-spawn");
        LogPhaseStep(
            $"spawn-enemy.detail clusters={attemptedClusterCount},units={spawnedCount},clusterTotalMs={totalSpawnClusterMs},clusterMaxMs={maxSingleSpawnClusterMs}",
            totalWatch.ElapsedMilliseconds);

        Log.Debug($"Spawned {spawnedCount} enemy soldiers from enemy stronghold unit preset points");
    }

    private static void LogPhaseStep(string step, long elapsedMs)
    {
        if (elapsedMs >= PhaseStepWarnMs)
        {
            Log.Warning("[PhasePerf] {0}: {1}ms", step, elapsedMs);
            return;
        }

        Log.Info("[PhasePerf] {0}: {1}ms", step, elapsedMs);
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
            return;
        }

        Log.Info(
            "[PhasePerf] creature-pool.{0}: count={1},canRelease={2},capacity={3},expire={4}",
            step,
            pool.Count,
            pool.CanReleaseCount,
            pool.Capacity,
            pool.ExpireTime);
    }
}
