using System;
using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public class PhaseManager : GameFrameworkComponent
{
    public static event Action<GamePhase, GamePhase> OnPhaseChanged;
    internal static event Action<GamePhase> PersistentStageCommitted;

    private static readonly Fix64 EnemyPresetClusterRadius = Fix64.FromRaw(12288);
    private static readonly Fix64 EnemyPresetClusterMinDistance = Fix64.FromRaw(4916);
    private const long PhaseStepWarnMs = 30;
    private const string EnemyProductionBuildingDailyResourceCostConfigKey = "EnemyProductionBuildingDailyResourceCost";
    private static readonly Queue<string> s_PendingPhaseSounds = new Queue<string>();
    private static readonly Queue<PhasePresentationEvent> s_PendingPhaseEvents = new Queue<PhasePresentationEvent>();
    private static readonly List<InvadeSpawnPointDefinition> s_InvadeSpawnPoints = new List<InvadeSpawnPointDefinition>();
    private static bool s_InvadeSpawnPointsConfigured;
    private static bool s_RuntimeDependenciesPrepared;
    private static CardSetup s_CardSetup;
    private static int s_EnemyProductionBuildingDailyResourceCost;

    public static GamePhase CurrentPhase => (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);

    public static void CancelRuntimePhaseFlows()
    {
        s_PendingPhaseSounds.Clear();
        s_PendingPhaseEvents.Clear();
        DefendPhaseRuntime.CancelRuntime();
        DefendPhaseRuntime.ClearPreparedRuntime();
        s_CardSetup = null;
        s_EnemyProductionBuildingDailyResourceCost = 0;
        s_RuntimeDependenciesPrepared = false;
    }

    internal static void ConfigureInvadeSpawnPoints(IReadOnlyList<EntityPresetPoint> presetPoints)
    {
        if (presetPoints == null)
            throw new ArgumentNullException(nameof(presetPoints));
        if (s_InvadeSpawnPointsConfigured)
            throw new InvalidOperationException("PhaseManager invade spawn points are already configured.");

        s_InvadeSpawnPoints.Clear();
        for (int i = 0; i < presetPoints.Count; i++)
        {
            EntityPresetPoint point = presetPoints[i];
            if (point == null)
                throw new InvalidOperationException($"PhaseManager found a null preset point at level index {i}.");
            if (point.PointType != EntityPresetPointType.Unit)
                continue;
            if (!UnitTypeHelper.TryParseUnitTypeAndLevel(point.Identifier, out UnitType unitType, out int unitLevel))
            {
                throw new InvalidOperationException(
                    $"PhaseManager invade spawn point has invalid unit identifier. point={point.name} identifier={point.Identifier}.");
            }
            if (point.UnitSpawnCount <= 0)
            {
                throw new InvalidOperationException(
                    $"PhaseManager invade authored spawn count is not positive. point={point.name} count={point.UnitSpawnCount}.");
            }

            Vector3 authoredPosition = point.Position;
            s_InvadeSpawnPoints.Add(new InvadeSpawnPointDefinition(
                new FixVector2((Fix64)authoredPosition.x, (Fix64)authoredPosition.z),
                unitType,
                unitLevel,
                point.UnitSpawnCount,
                string.IsNullOrWhiteSpace(point.name) ? "<unnamed>" : point.name));
        }
        s_InvadeSpawnPoints.Sort(CompareInvadeSpawnPointDefinitions);
        s_InvadeSpawnPointsConfigured = true;
        DefendPhaseRuntime.ConfigureTutorialTriggeredSpawnPoints(presetPoints);
    }

    internal static void ClearInvadeSpawnPoints()
    {
        s_InvadeSpawnPoints.Clear();
        s_InvadeSpawnPointsConfigured = false;
        DefendPhaseRuntime.ClearTutorialTriggeredSpawnPoints();
    }

	private void Update()
	{
		while (s_PendingPhaseEvents.Count > 0)
        {
            PhasePresentationEvent evt = s_PendingPhaseEvents.Dequeue();
            OnPhaseChanged?.Invoke(evt.OldPhase, evt.NewPhase);
        }

		if (s_PendingPhaseSounds.Count == 0
			|| LogicTimeControlService.IsPaused
			|| AudioManager.Instance == null)
		{
			return;
		}

		AudioManager.Instance.Play(s_PendingPhaseSounds.Dequeue());
	}

    public static void EnterCurrentPhaseOnGameStart()
    {
        PrepareRuntimeDependencies();
        GamePhase currentPhase = CurrentPhase;
        RequireInitializedPhaseAuthority(currentPhase);
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
        PrepareRuntimeDependencies();
        if (CurrentPhase != restoredPhase)
            throw new InvalidOperationException(
                $"Restored phase mismatch. checkpoint={restoredPhase}, dataModel={CurrentPhase}.");
        RequireInitializedPhaseAuthority(restoredPhase);
        HandlePhaseTransition(restoredPhase, restoredPhase);
        Log.Debug($"Restored phase entered: {restoredPhase}");
    }

    public static void SwitchToNextPhase()
    {
        GamePhase currentPhase = LogicPhaseCommandService.GetRequiredCurrentPhase();
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
                throw new InvalidOperationException(
                    $"PhaseManager.SwitchToNextPhase failed: unsupported current phase {currentPhase}.");
        }

        SwitchToPhase(nextPhase);
    }

    public static void SwitchToPhase(GamePhase phase)
    {
        LogicPhaseCommandService.Submit(phase);
    }

    public static void InitializePhaseAuthorityOnGameStart(GamePhase phase)
    {
        if (CurrentPhase != phase)
        {
            throw new InvalidOperationException(
                $"Initial phase authority mismatch. requested={phase}, dataModel={CurrentPhase}.");
        }
        LogicPhaseCommandService.SetInitialPhase(phase);
    }

    internal static void ApplyScheduledPhase(GamePhase phase)
    {
        if (!LogicPhaseCommandService.IsApplyingFrame)
            throw new InvalidOperationException("PhaseManager.ApplyScheduledPhase is only valid while applying a logic phase command.");

        RequireRuntimeDependencies();

        GamePhase oldPhase = CurrentPhase;
        if (LogicPhaseCommandService.GetRequiredCurrentPhase() != phase)
            throw new InvalidOperationException("Phase authority was not committed before applying phase effects.");
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
        if (LogicPausedOperationService.IsExecuting && phase == GamePhase.Defend)
            DefendPhaseRuntime.ApplyScheduledSpawnRequests(LogicTimeControlService.CurrentFrame);
        transitionWatch.Stop();
        LogPhaseStep($"transition {oldPhase}->{phase}", transitionWatch.ElapsedMilliseconds);

        var eventWatch = Stopwatch.StartNew();
        s_PendingPhaseEvents.Enqueue(new PhasePresentationEvent(oldPhase, phase));
        eventWatch.Stop();
        LogPhaseStep($"phase-event {oldPhase}->{phase}", eventWatch.ElapsedMilliseconds);

        totalWatch.Stop();
        LogPhaseStep($"switch-total {oldPhase}->{phase}", totalWatch.ElapsedMilliseconds);

        Log.Debug($"Phase switched from {oldPhase} to {phase}");
    }

    private static void RequireInitializedPhaseAuthority(GamePhase expectedPhase)
    {
        GamePhase logicPhase = LogicPhaseCommandService.GetRequiredCurrentPhase();
        if (logicPhase != expectedPhase)
        {
            throw new InvalidOperationException(
                $"Initial phase authority mismatch. expected={expectedPhase}, logic={logicPhase}.");
        }
    }

    private static void TryAdvanceDayOnBuildTransition(GamePhase oldPhase, GamePhase newPhase)
    {
        if (!InGameDataModel.IsBuildPhase(newPhase))
        {
            return;
        }

        if (!InGameDataModel.TryModifyValue(IngameValueType.Day, 1))
            throw new InvalidOperationException("Phase day advancement could not be committed to InGameDataModel.");
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
		RequestPhaseEnterSound("enterManage");

        DefendPhaseRuntime.CancelRuntime();

        var totalWatch = Stopwatch.StartNew();

        var removeSoldiersWatch = Stopwatch.StartNew();
        RemoveAllSoldiers();
        removeSoldiersWatch.Stop();
        LogPhaseStep("build.remove-soldiers", removeSoldiersWatch.ElapsedMilliseconds);

        var shutdownWatch = Stopwatch.StartNew();
        s_CardSetup.CardSystemShutdown();
        shutdownWatch.Stop();
        LogPhaseStep("build.card-shutdown", shutdownWatch.ElapsedMilliseconds);

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
        int consumeCost = s_EnemyProductionBuildingDailyResourceCost;
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
		RequestPhaseEnterSound("enterBattle");

        DefendPhaseRuntime.CancelRuntime();
        var totalWatch = Stopwatch.StartNew();

        CompleteNavigationForBattlePhase("invade");

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
		RequestPhaseEnterSound("enterBattle");
        CompleteNavigationForBattlePhase("defend");
        PrepareBattlePhaseCards("defend");
        DefendPhaseRuntime.EnterDefendPhase();
    }

    private static void CompleteNavigationForBattlePhase(string phaseTag)
    {
        var watch = Stopwatch.StartNew();
        int completedWorldCount = FlowFieldCrowdMovementSystem.CompleteRuntimeRebuildQueue();
        watch.Stop();
        Log.Info(
            "[PhaseNavigation] {0}.navigation-ready worlds={1}, elapsedMs={2}",
            phaseTag,
            completedWorldCount,
            watch.ElapsedMilliseconds);
    }

    private static void PrepareBattlePhaseCards(string phaseTag)
    {
        var setupWatch = Stopwatch.StartNew();
        s_CardSetup.CardSystemSetup();
        setupWatch.Stop();
        LogPhaseStep($"{phaseTag}.card-setup", setupWatch.ElapsedMilliseconds);

        var generateCardsWatch = Stopwatch.StartNew();
        int generatedCardCount = GenerateCardsFromArmyBuildings();
        generateCardsWatch.Stop();
        LogPhaseStep($"{phaseTag}.generate-cards", generateCardsWatch.ElapsedMilliseconds);

        if (generatedCardCount > 0)
        {
            var openUiWatch = Stopwatch.StartNew();
            s_CardSetup.OpenCardUI();
            openUiWatch.Stop();
            LogPhaseStep($"{phaseTag}.open-card-ui", openUiWatch.ElapsedMilliseconds);
        }
        else
        {
            Log.Info("[CardGame] Skip opening Card UI: no cards generated for this phase.");
        }
    }

	private static void RequestPhaseEnterSound(string cueKey)
    {
		if (string.IsNullOrWhiteSpace(cueKey))
			throw new ArgumentException("Phase presentation cue key is empty.", nameof(cueKey));
		s_PendingPhaseSounds.Clear();
		s_PendingPhaseSounds.Enqueue(cueKey);
    }

    private static void RemoveAllSoldiers()
    {
        SoldierFactory.RemoveAllCurrentBattleTroops();
    }

    private static int GenerateCardsFromArmyBuildings()
    {
        var watch = Stopwatch.StartNew();

        int scanBuildingCount = 0;
        int generatedCardCount = 0;

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

            if (s_CardSetup.GenerateCardToDeck(building))
                generatedCardCount++;
        }

        watch.Stop();
        LogPhaseStep($"generate-cards.detail scan={scanBuildingCount},generated={generatedCardCount}", watch.ElapsedMilliseconds);
        return generatedCardCount;
    }

    private static void SpawnEnemySoldiers()
    {
        var totalWatch = Stopwatch.StartNew();

        if (!s_InvadeSpawnPointsConfigured)
            throw new InvalidOperationException("PhaseManager cannot spawn invade enemies before level spawn points are configured.");

        var spawnPlans = new List<InvadeSpawnPlan>();
        for (int i = 0; i < s_InvadeSpawnPoints.Count; i++)
        {
            InvadeSpawnPointDefinition point = s_InvadeSpawnPoints[i];
            FixVector2 position = point.Position;
            string strongholdId = null;
            if (!LogicStrongholdMap.TryResolveStrongholdId(position, out strongholdId))
            {
                Log.Warning(
                    "PhaseManager invade spawn point is outside every stronghold; defaulting to enemy. point={0} raw=({1},{2}).",
                    point.Name,
                    position.x.RawValue,
                    position.y.RawValue);
            }
            else if (!LogicBuildingQueryService.TryResolveStrongholdOwnerFaction(strongholdId, out int ownerFactionId))
            {
                throw new InvalidOperationException(
                    $"PhaseManager invade spawn point stronghold has no owner. point={point.Name} stronghold={strongholdId}.");
            }
            else if (ownerFactionId == EntitySideHelper.PlayerFactionId)
            {
                continue;
            }
            int count = EnemyArmyForceModifierService.CalculateSpawnCount(point.AuthoredCount);
            if (count <= 0)
                throw new InvalidOperationException($"PhaseManager invade spawn count is not positive. point={point.Name} count={count}.");

            spawnPlans.Add(new InvadeSpawnPlan(
                position,
                point.UnitType,
                point.UnitLevel,
                count,
                strongholdId,
                point.Name));
        }
        spawnPlans.Sort(CompareInvadeSpawnPlans);

        int spawnedCount = 0;
        long totalSpawnClusterMs = 0;
        long maxSingleSpawnClusterMs = 0;

        for (int i = 0; i < spawnPlans.Count; i++)
        {
            InvadeSpawnPlan plan = spawnPlans[i];
            var spawnWatch = Stopwatch.StartNew();
            bool clusterSpawned = ClusterSpawnSystem.SpawnClusterFixed(
                plan.Position,
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

    private static int CompareInvadeSpawnPointDefinitions(
        InvadeSpawnPointDefinition left,
        InvadeSpawnPointDefinition right)
    {
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

    private readonly struct InvadeSpawnPointDefinition
    {
        public InvadeSpawnPointDefinition(
            FixVector2 position,
            UnitType unitType,
            int unitLevel,
            int authoredCount,
            string name)
        {
            Position = position;
            UnitType = unitType;
            UnitLevel = unitLevel;
            AuthoredCount = authoredCount;
            Name = name;
        }

        public FixVector2 Position { get; }
        public UnitType UnitType { get; }
        public int UnitLevel { get; }
        public int AuthoredCount { get; }
        public string Name { get; }
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

    private static void PrepareRuntimeDependencies()
    {
        if (s_RuntimeDependenciesPrepared)
            return;
        s_CardSetup = GameEntry.GetComponent<CardSetup>()
                      ?? throw new InvalidOperationException("PhaseManager requires CardSetup before entering the initial phase.");
        if (GF.Config == null)
            throw new InvalidOperationException("PhaseManager requires initialized game config before entering the initial phase.");
        s_EnemyProductionBuildingDailyResourceCost = GF.Config.GetInt(
            EnemyProductionBuildingDailyResourceCostConfigKey,
            0);
        DefendPhaseRuntime.PrepareForCurrentLevelIfNeeded();
        s_RuntimeDependenciesPrepared = true;
    }

    private static void RequireRuntimeDependencies()
    {
        if (!s_RuntimeDependenciesPrepared || s_CardSetup == null)
            throw new InvalidOperationException("PhaseManager runtime dependencies were not prepared before logic phase apply.");
    }

    private readonly struct PhasePresentationEvent
    {
        public PhasePresentationEvent(GamePhase oldPhase, GamePhase newPhase)
        {
            OldPhase = oldPhase;
            NewPhase = newPhase;
        }

        public GamePhase OldPhase { get; }
        public GamePhase NewPhase { get; }
    }
}
