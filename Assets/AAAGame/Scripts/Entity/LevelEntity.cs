using GameFramework;
using GameFramework.Event;
using AAAGame.Scripts.BuffSystem;
using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using Cysharp.Threading.Tasks;
using GiantGrey.TileWorldCreator;
using UnityEngine;
using UnityGameFramework.Runtime;

public partial class LevelEntity : EntityBase
{
    private const string StrongholdLayerPrefix = "SH";
    private const int RuntimeInitItemsPerFrame = 8;
    private const float CaptureVfxBaseDiameter = 12f;

    private TileWorldCreatorManager tileWorldCreatorManager;
    private int m_RuntimeInitializationVersion;
    private bool m_HiddenDuringRuntimeInitialization;
    private EntityPresetPoint[] m_RuntimePresetPoints;

    public event Action<LevelEntity> RuntimeInitializationCompleted;
    public bool IsRuntimeInitializationCompleted { get; private set; }

    private static LevelEntity activeLevelEntity;

    public static LevelEntity ActiveLevelEntity
    {
        get { return activeLevelEntity; }
    }

    public IReadOnlyList<Stronghold> Strongholds
    {
        get { return InGameDataModel.GetStrongholds(); }
    }

    public static Stronghold GetStrongholdAtWorldPosition(Vector3 worldPosition)
    {
        Vector2 gridPosition = ActiveLevelEntity.tileWorldCreatorManager.GetRelativeGridPosition(worldPosition);
        return ActiveLevelEntity.GetStrongholdAtGridPosition(gridPosition);
    }

    public Stronghold GetStrongholdAtGridPosition(Vector2 gridPosition)
    {
        var strongholds = Strongholds;
        for (int i = 0; i < strongholds.Count; i++)
        {
            var stronghold = strongholds[i];
            if (stronghold == null || stronghold.strongholdData == null || stronghold.strongholdData.RangeCells == null)
            {
                continue;
            }

            if (stronghold.strongholdData.RangeCells.Contains(gridPosition))
            {
                return stronghold;
            }
        }

        return null;
    }

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        activeLevelEntity = this;
        LogicBuildingDisabledEventService.BuildingDisabled += OnLogicBuildingDisabled;
        IsRuntimeInitializationCompleted = false;
        m_HiddenDuringRuntimeInitialization = LevelSelectionService.IsLevelLoading;

        int initVersion = ++m_RuntimeInitializationVersion;

        CollectStrongholds();
        m_RuntimePresetPoints = GetComponentsInChildren<EntityPresetPoint>(true);
        PhaseManager.ConfigureInvadeSpawnPoints(m_RuntimePresetPoints);
        SubscribeRuntimeLayerRules();
        if (m_HiddenDuringRuntimeInitialization)
        {
            LevelSelectionService.HideEntityRenderersDuringLoad(this);
        }

        InitializeRuntimeAsync(initVersion).Forget();
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        LogicBuildingDisabledEventService.BuildingDisabled -= OnLogicBuildingDisabled;
        UnsubscribeRuntimeLayerRules();

        bool wasActiveLevel = activeLevelEntity == this;
        if (activeLevelEntity == this)
        {
            activeLevelEntity = null;
        }

        if (wasActiveLevel)
        {
            PhaseManager.ClearInvadeSpawnPoints();
            LogicStrongholdMap.Clear();
            InGameDataModel.ClearStrongholdRuntimeData();
        }
        ClearEnemyStrongholdFogEffects();
        tileWorldCreatorManager = null;
        m_RuntimePresetPoints = null;
        IsRuntimeInitializationCompleted = false;
        m_HiddenDuringRuntimeInitialization = false;
        RuntimeInitializationCompleted = null;
        m_RuntimeInitializationVersion++;

        base.OnHide(isShutdown, userData);
    }

    private async UniTaskVoid InitializeRuntimeAsync(int initVersion)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            LogRuntimeInitTiming("begin", stopwatch);
            await UniTask.Yield(PlayerLoopTiming.Update);
            if (!IsRuntimeInitializationActive(initVersion))
            {
                return;
            }
            LogRuntimeInitTiming("after-initial-yield", stopwatch);

            await ApplyStrongholdRuntimeLayerRulesAsync(initVersion);
            if (!IsRuntimeInitializationActive(initVersion))
            {
                return;
            }
            LogRuntimeInitTiming("after-stronghold-layer-rules", stopwatch);

            await SpawnPresetEntitiesAsync(initVersion);
            if (!IsRuntimeInitializationActive(initVersion))
            {
                return;
            }
            LogRuntimeInitTiming("after-spawn-presets", stopwatch);

            await UniTask.Yield(PlayerLoopTiming.Update);
            if (!IsRuntimeInitializationActive(initVersion))
            {
                return;
            }
            LogRuntimeInitTiming("after-post-spawn-yield", stopwatch);

            SyncEnemyStrongholdFogEffects();
            LogRuntimeInitTiming("after-enemy-stronghold-fog", stopwatch);

            if (GroupMoveManager.HasInstance)
            {
                GroupMoveManager.Instance.PrewarmNavigationWorlds();
            }
            LogRuntimeInitTiming("after-navigation-prewarm", stopwatch);
            IsRuntimeInitializationCompleted = true;
            RuntimeInitializationCompleted?.Invoke(this);
            LogRuntimeInitTiming("completed-event-invoked", stopwatch);
        }
        catch (Exception ex)
        {
            StageCheckpointRuntimeCoordinator.AbortPendingRestore();
            LevelSelectionService.NotifyLevelLoadFailed(ex.Message);
            Log.Error("LevelEntity runtime initialization failed: {0}", ex);
            throw;
        }
    }

    private static void LogRuntimeInitTiming(string stage, Stopwatch stopwatch)
    {
        double elapsedMs = stopwatch != null ? stopwatch.Elapsed.TotalMilliseconds : 0.0;
        Log.Info("[LevelRuntimeInitTiming] stage={0} elapsedMs={1:F3}", stage, elapsedMs);
    }

    private bool IsRuntimeInitializationActive(int initVersion)
    {
        return m_RuntimeInitializationVersion == initVersion && activeLevelEntity == this;
    }

    private void SubscribeRuntimeLayerRules()
    {
        if (tileWorldCreatorManager == null)
        {
            return;
        }

        tileWorldCreatorManager.OnBuildLayersReady -= ApplyStrongholdRuntimeLayerRules;
        tileWorldCreatorManager.OnBuildLayersReady += ApplyStrongholdRuntimeLayerRules;
    }

    private void UnsubscribeRuntimeLayerRules()
    {
        if (tileWorldCreatorManager == null)
        {
            return;
        }

        tileWorldCreatorManager.OnBuildLayersReady -= ApplyStrongholdRuntimeLayerRules;
    }

    private void ApplyStrongholdRuntimeLayerRules()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (tileWorldCreatorManager == null || tileWorldCreatorManager.configuration == null)
        {
            return;
        }

        var configuration = tileWorldCreatorManager.configuration;
        for (int i = 0; i < configuration.buildLayerFolders.Count; i++)
        {
            var folder = configuration.buildLayerFolders[i];
            if (folder == null || folder.buildLayers == null)
            {
                continue;
            }

            for (int j = 0; j < folder.buildLayers.Count; j++)
            {
                var buildLayer = folder.buildLayers[j];
                if (buildLayer == null)
                {
                    continue;
                }

                var blueprintLayer = configuration.GetBlueprintLayerByGuid(buildLayer.assignedBlueprintLayerGuid);
                if (blueprintLayer == null)
                {
                    continue;
                }

                if (!TryParseStrongholdLayerName(blueprintLayer.layerName, out _))
                {
                    continue;
                }

                var layerObject = buildLayer.GetLayerObject(tileWorldCreatorManager.gameObject);
                if (layerObject == null)
                {
                    continue;
                }

                var renderers = layerObject.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    renderers[r].enabled = false;
                }
            }
        }
    }

    private async UniTask ApplyStrongholdRuntimeLayerRulesAsync(int initVersion)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (tileWorldCreatorManager == null || tileWorldCreatorManager.configuration == null)
        {
            return;
        }

        var configuration = tileWorldCreatorManager.configuration;
        for (int i = 0; i < configuration.buildLayerFolders.Count; i++)
        {
            var folder = configuration.buildLayerFolders[i];
            if (folder == null || folder.buildLayers == null)
            {
                continue;
            }

            for (int j = 0; j < folder.buildLayers.Count; j++)
            {
                if (!IsRuntimeInitializationActive(initVersion))
                {
                    return;
                }

                var buildLayer = folder.buildLayers[j];
                if (buildLayer == null)
                {
                    continue;
                }

                var blueprintLayer = configuration.GetBlueprintLayerByGuid(buildLayer.assignedBlueprintLayerGuid);
                if (blueprintLayer == null)
                {
                    continue;
                }

                if (!TryParseStrongholdLayerName(blueprintLayer.layerName, out _))
                {
                    continue;
                }

                var layerObject = buildLayer.GetLayerObject(tileWorldCreatorManager.gameObject);
                if (layerObject == null)
                {
                    continue;
                }

                var renderers = layerObject.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    renderers[r].enabled = false;
                }

                await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }
    }

    private void SpawnPresetEntities()
    {
        var buildManager = GameEntry.GetComponent<BuildManager>();
        var gameEndManager = GameEntry.GetComponent<GameEndManager>();
        // 只使用当前关卡实体层级下的预设点，避免 launch 等并存场景中的同名点干扰出生位置。
        EntityPresetPoint[] presetPoints = m_RuntimePresetPoints
                                           ?? throw new InvalidOperationException("LevelEntity preset points were not captured during OnShow.");
        var testSlotConfig = TechTestSlotConfig.LoadOrNull();
        if (testSlotConfig == null)
        {
            Debug.LogWarning("[TestSlot] TechTestSlotConfig 未加载 (Resources.Load 返回 null)");
        }
        else
        {
            Debug.Log($"[TestSlot] TechTestSlotConfig 加载成功，槽位: [{string.Join(", ", testSlotConfig.SlotBuildingIds ?? new string[0])}]");
        }
        bool heroSpawned = false;
        foreach (var point in presetPoints)
        {
            string effectiveIdentifier = point.Identifier;
            if (point.IsTestSlot)
            {
                string slotId = null;
                if (testSlotConfig != null
                    && testSlotConfig.SlotBuildingIds != null
                    && point.TestSlotIndex >= 0
                    && point.TestSlotIndex < testSlotConfig.SlotBuildingIds.Length)
                {
                    slotId = testSlotConfig.SlotBuildingIds[point.TestSlotIndex];
                }

                if (string.IsNullOrWhiteSpace(slotId))
                {
                    Debug.LogWarning($"[TestSlot] 槽位 {point.TestSlotIndex} 未配建筑，跳过 {point.name}");
                    continue;
                }

                effectiveIdentifier = slotId;
            }

            if (point.PointType == EntityPresetPointType.Building
                && !BuildingDataModel.TryResolvePresetIdentifier(effectiveIdentifier, out effectiveIdentifier))
            {
                Log.Error("LevelEntity.SpawnPresetEntities failed: invalid building identifier '{0}' at point '{1}'.", point.Identifier, point.name);
                continue;
            }

            switch (point.PointType)
            {
                case EntityPresetPointType.Hero:
                    if (heroSpawned)
                    {
                        break;
                    }

                    if (!UnitTypeHelper.TryParseUnitType(effectiveIdentifier, out var heroUnitType))
                    {
                        Log.Error("LevelEntity.SpawnPresetEntities failed: invalid hero identifier '{0}'.", effectiveIdentifier);
                        break;
                    }

                    Log.Info("LevelEntity.SpawnPresetEntities hero spawn point: name={0}, position={1}.", point.name, point.Position);
                    SoldierFactory.ShowSoldierFixed(
                        heroUnitType,
                        new FixVector2((Fix64)point.Position.x, (Fix64)point.Position.z),
                        point.Position.y,
                        SideType.PlayerSide,
                        BrainType.Player);
                    heroSpawned = true;
                    break;

                case EntityPresetPointType.Building:
                    int? initialCoinReserves = point.TryGetInitialCoinReserves(out int customCoinReserves)
                        ? customCoinReserves
                        : null;
                    if (!buildManager.TryBuildBuildingForLevelInit(
                            effectiveIdentifier,
                            point.Position,
                            out var buildingInstanceId,
                            isGameEndConditionBuilding: point.IsGameEndConditionBuilding,
                            initialCoinReserves: initialCoinReserves,
                            isNavigationStaticBaked: !point.IsTestSlot
                                                     && !BuildingAbilityIds.HasPermanentNoCollisionCapability(effectiveIdentifier)))
                    {
                        Log.Error("LevelEntity.SpawnPresetEntities failed: cannot build preset building '{0}'.", effectiveIdentifier);
                        break;
                    }

                    if (point.IsGameEndConditionBuilding)
                    {
                        int initialOwnerFactionId = ResolveOwnerFactionIdByPosition(point.Position);
                        if (effectiveIdentifier.Contains("ParcelLocker"))
                        {
                            Debug.Log($"[LevelEntity] 设置快递柜所有者: {effectiveIdentifier}, 位置: {point.Position}, 据点所有者: {initialOwnerFactionId}, 血条颜色: {(initialOwnerFactionId == EntitySideHelper.PlayerFactionId ? "绿色(友方)" : "红色(敌方)")}");
                        }

                        gameEndManager.RegisterInitialConditionBuilding(buildingInstanceId, initialOwnerFactionId);
                    }
                    break;
            }
        }
    }

    private async UniTask SpawnPresetEntitiesAsync(int initVersion)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        var buildManager = GameEntry.GetComponent<BuildManager>();
        var gameEndManager = GameEntry.GetComponent<GameEndManager>();
        string levelId = ChangeSceneProcedure.SelectedLevelIdentifier;
        StageCheckpoint restoreCheckpoint = StageCheckpointRuntimeCoordinator.GetPendingRestoreForLevelSpawn(levelId);
        EntityPresetPoint[] presetPoints = m_RuntimePresetPoints
                                           ?? throw new InvalidOperationException("LevelEntity preset points were not captured during OnShow.");
        var testSlotConfig = TechTestSlotConfig.LoadOrNull();
        if (testSlotConfig == null)
        {
            Debug.LogWarning("[TestSlot] TechTestSlotConfig 未加载 (Resources.Load 返回 null)");
        }
        else
        {
            Debug.Log($"[TestSlot] TechTestSlotConfig 加载成功，槽位: [{string.Join(", ", testSlotConfig.SlotBuildingIds ?? new string[0])}]");
        }

        bool heroSpawned = false;
        int itemsThisFrame = 0;
        int processedCount = 0;
        int buildingCount = 0;
        int skippedCount = 0;
        int yieldCount = 0;
        foreach (var point in presetPoints)
        {
            if (!IsRuntimeInitializationActive(initVersion))
            {
                return;
            }

            string effectiveIdentifier = point.Identifier;
            if (point.IsTestSlot)
            {
                string slotId = null;
                if (testSlotConfig != null
                    && testSlotConfig.SlotBuildingIds != null
                    && point.TestSlotIndex >= 0
                    && point.TestSlotIndex < testSlotConfig.SlotBuildingIds.Length)
                {
                    slotId = testSlotConfig.SlotBuildingIds[point.TestSlotIndex];
                }

                if (string.IsNullOrWhiteSpace(slotId))
                {
                    Debug.LogWarning($"[TestSlot] 槽位 {point.TestSlotIndex} 未配建筑，跳过 {point.name}");
                    skippedCount++;
                    continue;
                }

                effectiveIdentifier = slotId;
            }

            if (point.PointType == EntityPresetPointType.Building
                && !BuildingDataModel.TryResolvePresetIdentifier(effectiveIdentifier, out effectiveIdentifier))
            {
                Log.Error("LevelEntity.SpawnPresetEntities failed: invalid building identifier '{0}' at point '{1}'.", point.Identifier, point.name);
                skippedCount++;
                continue;
            }

            switch (point.PointType)
            {
                case EntityPresetPointType.Hero:
                    if (heroSpawned)
                    {
                        break;
                    }

                    if (!UnitTypeHelper.TryParseUnitType(effectiveIdentifier, out var heroUnitType))
                    {
                        Log.Error("LevelEntity.SpawnPresetEntities failed: invalid hero identifier '{0}'.", effectiveIdentifier);
                        break;
                    }

                    Log.Info("LevelEntity.SpawnPresetEntities hero spawn point: name={0}, position={1}.", point.name, point.Position);
                    SoldierFactory.ShowSoldierFixed(
                        heroUnitType,
                        new FixVector2((Fix64)point.Position.x, (Fix64)point.Position.z),
                        point.Position.y,
                        SideType.PlayerSide,
                        BrainType.Player);
                    heroSpawned = true;
                    processedCount++;
                    break;

                case EntityPresetPointType.Building:
                    if (restoreCheckpoint != null)
                    {
                        skippedCount++;
                        break;
                    }
                    int? initialCoinReserves = point.TryGetInitialCoinReserves(out int customCoinReserves)
                        ? customCoinReserves
                        : null;
                    if (!buildManager.TryBuildBuildingForLevelInit(
                            effectiveIdentifier,
                            point.Position,
                            out var buildingInstanceId,
                            isGameEndConditionBuilding: point.IsGameEndConditionBuilding,
                            initialCoinReserves: initialCoinReserves,
                            isNavigationStaticBaked: !point.IsTestSlot
                                                     && !BuildingAbilityIds.HasPermanentNoCollisionCapability(effectiveIdentifier)))
                    {
                        Log.Error("LevelEntity.SpawnPresetEntities failed: cannot build preset building '{0}'.", effectiveIdentifier);
                        skippedCount++;
                        break;
                    }

                    buildingCount++;
                    processedCount++;
                    if (point.IsGameEndConditionBuilding)
                    {
                        int initialOwnerFactionId = ResolveOwnerFactionIdByPosition(point.Position);

                        if (effectiveIdentifier.Contains("ParcelLocker"))
                        {
                            Debug.Log($"[LevelEntity] 设置快递柜所有者: {effectiveIdentifier}, 位置: {point.Position}, 据点所有者: {initialOwnerFactionId}, 血条颜色: {(initialOwnerFactionId == EntitySideHelper.PlayerFactionId ? "绿色(友方)" : "红色(敌方)")}");
                        }

                        gameEndManager.RegisterInitialConditionBuilding(buildingInstanceId, initialOwnerFactionId);
                    }
                    break;
            }

            itemsThisFrame++;
            if (itemsThisFrame >= RuntimeInitItemsPerFrame)
            {
                itemsThisFrame = 0;
                yieldCount++;
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }

        if (restoreCheckpoint != null)
        {
            for (int i = 0; i < restoreCheckpoint.Buildings.Count; i++)
            {
                if (!IsRuntimeInitializationActive(initVersion))
                    return;
                StageBuildingCheckpoint building = restoreCheckpoint.Buildings[i];
                buildManager.RestoreBuildingForStageCheckpoint(building);
                if (building.IsGameEndConditionBuilding)
                    gameEndManager.RegisterInitialConditionBuilding(building.BuildingInstanceId, building.OwnerFactionId);
                buildingCount++;
                processedCount++;
                itemsThisFrame++;
                if (itemsThisFrame >= RuntimeInitItemsPerFrame)
                {
                    itemsThisFrame = 0;
                    yieldCount++;
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
            }

        }

        Log.Info(
            "[LevelRuntimeInitTiming] stage=spawn-presets-complete elapsedMs={0:F3} presetPoints={1} processed={2} buildings={3} heroSpawned={4} skipped={5} yields={6}",
            stopwatch.Elapsed.TotalMilliseconds,
            presetPoints.Length,
            processedCount,
            buildingCount,
            heroSpawned,
            skippedCount,
            yieldCount);
    }

    private int ResolveOwnerFactionIdByPosition(Vector3 position)
    {
        var positionFixed = new FixVector2((Fix64)position.x, (Fix64)position.z);
        return LogicStrongholdMap.TryResolveStrongholdId(positionFixed, out string strongholdId)
            ? LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId)
            : EntitySideHelper.EnemyFactionId;
    }



    private void CollectStrongholds()
    {
        tileWorldCreatorManager = GetComponentInChildren<TileWorldCreatorManager>();
        if (tileWorldCreatorManager == null || tileWorldCreatorManager.configuration == null)
        {
            throw new InvalidOperationException("LevelEntity.CollectStrongholds failed: TileWorldCreatorManager or configuration is null.");
        }

        var strongholds = new List<Stronghold>();

        foreach (var folder in tileWorldCreatorManager.configuration.blueprintLayerFolders)
        {
            if (folder == null || folder.blueprintLayers == null)
            {
                continue;
            }

            foreach (var layer in folder.blueprintLayers)
            {
                if (layer == null)
                {
                    continue;
                }

                if (!TryParseStrongholdLayerName(layer.layerName, out int factionId))
                {
                    continue;
                }

                strongholds.Add(new Stronghold
                {
                    OwnerFactionId = factionId,
                    strongholdData = new StrongholdData
                    {
                        StrongholdId = layer.layerName,
                        RangeCells = layer.GetAllCellPositions(new HashSet<Vector2>())
                    }
                });
            }
        }

        InGameDataModel.SetStrongholds(strongholds);
        InitializeLogicStrongholdMap(strongholds);
        StageCheckpointRuntimeCoordinator.RestoreStrongholdOwners(
            strongholds,
            ChangeSceneProcedure.SelectedLevelIdentifier);

        var existingBuildings = GameObject.FindObjectsOfType<BuildingEntity>();
        for (int i = 0; i < existingBuildings.Length; i++)
        {
            InGameDataModel.RegisterBuilding(existingBuildings[i]);
        }

        Log.Info(
            "LevelEntity.CollectStrongholds done. Strongholds={0}",
            strongholds.Count);
    }

    private void InitializeLogicStrongholdMap(IReadOnlyList<Stronghold> strongholds)
    {
        Transform gridTransform = tileWorldCreatorManager.transform;
        Vector3 localX = gridTransform.TransformVector(Vector3.right);
        Vector3 localY = gridTransform.TransformVector(Vector3.up);
        Vector3 localZ = gridTransform.TransformVector(Vector3.forward);
        const float planarTolerance = 0.00001f;
        if (Mathf.Abs(localX.y) > planarTolerance
            || Mathf.Abs(localZ.y) > planarTolerance
            || Mathf.Abs(localY.x) > planarTolerance
            || Mathf.Abs(localY.z) > planarTolerance)
        {
            throw new InvalidOperationException(
                $"LevelEntity stronghold grid must remain planar in XZ. right={localX}, up={localY}, forward={localZ}.");
        }

        var cells = new List<LogicStrongholdCellDefinition>();
        for (int strongholdIndex = 0; strongholdIndex < strongholds.Count; strongholdIndex++)
        {
            Stronghold stronghold = strongholds[strongholdIndex]
                ?? throw new InvalidOperationException($"LevelEntity stronghold {strongholdIndex} is null.");
            StrongholdData data = stronghold.strongholdData
                ?? throw new InvalidOperationException($"LevelEntity stronghold {strongholdIndex} data is null.");
            if (string.IsNullOrWhiteSpace(data.StrongholdId))
                throw new InvalidOperationException($"LevelEntity stronghold {strongholdIndex} id is empty.");
            if (data.RangeCells == null)
                throw new InvalidOperationException($"LevelEntity stronghold '{data.StrongholdId}' cells are null.");
            if (data.RangeCells.Count == 0)
                throw new InvalidOperationException($"LevelEntity stronghold '{data.StrongholdId}' has no authored cells.");

            foreach (Vector2 authoredCell in data.RangeCells)
            {
                if (float.IsNaN(authoredCell.x)
                    || float.IsInfinity(authoredCell.x)
                    || float.IsNaN(authoredCell.y)
                    || float.IsInfinity(authoredCell.y))
                {
                    throw new InvalidOperationException(
                        $"LevelEntity stronghold '{data.StrongholdId}' contains a non-finite cell {authoredCell}.");
                }

                int x = Mathf.RoundToInt(authoredCell.x);
                int y = Mathf.RoundToInt(authoredCell.y);
                if (Mathf.Abs(authoredCell.x - x) > planarTolerance
                    || Mathf.Abs(authoredCell.y - y) > planarTolerance)
                {
                    throw new InvalidOperationException(
                        $"LevelEntity stronghold '{data.StrongholdId}' contains a non-integral cell {authoredCell}.");
                }
                cells.Add(new LogicStrongholdCellDefinition(data.StrongholdId, x, y, stronghold.OwnerFactionId));
            }
        }

        Vector3 origin = gridTransform.position;
        LogicStrongholdMap.Initialize(
            new FixVector2((Fix64)origin.x, (Fix64)origin.z),
            new FixVector2((Fix64)localX.x, (Fix64)localX.z),
            new FixVector2((Fix64)localZ.x, (Fix64)localZ.z),
            (Fix64)tileWorldCreatorManager.configuration.cellSize,
            cells);
    }

    private void OnLogicBuildingDisabled(IBuildingLogicContext building, IEntityContext attacker)
    {
        if (activeLevelEntity != this)
            throw new InvalidOperationException("Inactive LevelEntity received a logic building disabled event.");
        TryCaptureStrongholdAfterBuildingDisabled(building, attacker);
    }

    private bool TryParseStrongholdLayerName(string layerName, out int factionId)
    {
        factionId = -1;

        if (string.IsNullOrEmpty(layerName))
        {
            return false;
        }

        string[] parts = layerName.Split('_');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!string.Equals(parts[0], StrongholdLayerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out factionId))
        {
            return false;
        }

        if (!int.TryParse(parts[2], out _))
        {
            return false;
        }

        return true;
    }

    private void TryCaptureStrongholdAfterBuildingDisabled(IBuildingLogicContext disabledBuilding, IEntityContext attacker)
    {
        if (disabledBuilding == null)
            throw new ArgumentNullException(nameof(disabledBuilding));
        if (string.IsNullOrWhiteSpace(disabledBuilding.StrongholdId))
            return;

        int captureFactionId = ResolveCaptureFactionId(attacker);
        int currentOwnerFactionId = LogicStrongholdMap.GetOwnerFactionIdRequired(disabledBuilding.StrongholdId);
        if (currentOwnerFactionId == captureFactionId)
            return;

        if (!CanCaptureStronghold(disabledBuilding.StrongholdId))
            return;

        CaptureStronghold(disabledBuilding.StrongholdId, captureFactionId);
    }

    internal static bool CanCaptureStronghold(string strongholdId)
    {
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new ArgumentException("Stronghold id is empty.", nameof(strongholdId));

        bool hasCapturableBuildings = false;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!(entities[i] is IBuildingLogicContext building)
                || !string.Equals(building.StrongholdId, strongholdId, StringComparison.Ordinal)
                || building.BuildingData == null
                || building.BuildingData.Lv == 0
                || building.IsPermanentlyInvincible)
            {
                continue;
            }

            hasCapturableBuildings = true;
            if (!building.IsDisabled)
                return false;
        }

        return hasCapturableBuildings;
    }

    private void CaptureStronghold(string strongholdId, int newOwnerFactionId)
    {
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new ArgumentException("Stronghold id is empty.", nameof(strongholdId));
        int oldOwnerFactionId = LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId);
        if (oldOwnerFactionId == newOwnerFactionId)
            throw new InvalidOperationException($"Stronghold '{strongholdId}' is already owned by faction {newOwnerFactionId}.");

        Stronghold stronghold = GetStrongholdViewRequired(strongholdId);
        if (stronghold.OwnerFactionId != oldOwnerFactionId)
        {
            throw new InvalidOperationException(
                $"Stronghold owner mismatch before capture. id={strongholdId}, logic={oldOwnerFactionId}, view={stronghold.OwnerFactionId}.");
        }
        LogicStrongholdMap.SetOwnerFactionId(strongholdId, newOwnerFactionId);
        stronghold.OwnerFactionId = newOwnerFactionId;
        bool capturedByPlayer = oldOwnerFactionId != EntitySideHelper.PlayerFactionId
                                && newOwnerFactionId == EntitySideHelper.PlayerFactionId;
        int captureDay = capturedByPlayer ? Math.Max(1, InGameDataModel.GetValue(IngameValueType.Day)) : 0;

        SideType newSide = EntitySideHelper.ToSide(
            EntityCombatTeamHelper.ResolveTeamIdByFaction(newOwnerFactionId));
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!(entities[i] is LogicEntityState unit)
                || unit.IsBuildingEntity
                || !unit.Alive
                || !string.Equals(unit.SourceStrongholdId, strongholdId, StringComparison.Ordinal)
                || unit.Side == newSide)
            {
                continue;
            }

            unit.SetUnitSide(newSide);
            if (LogicEntityLifecycleService.TryGetBoundView(unit.EntityId, out MAEntity unitView))
            {
                if (unitView is not SoldierEntity soldierView)
                    throw new InvalidOperationException($"Stronghold unit {unit.EntityId.Value} is bound to non-soldier view {unitView.GetType().Name}.");
                soldierView.ChangeSide(newSide);
            }
        }

        for (int i = 0; i < entities.Count; i++)
        {
            if (!(entities[i] is IBuildingLogicContext building)
                || !string.Equals(building.StrongholdId, strongholdId, StringComparison.Ordinal))
            {
                continue;
            }

            building.SetOwnerFaction(newOwnerFactionId);
            building.RestoreBuildingToFullHealth();
            if (capturedByPlayer)
                LevelTagRuntime.ApplyCapturedStrongholdTrainingProvider(building, captureDay);

            IBuffComp buffComp = building.BuffComp
                                 ?? throw new InvalidOperationException(
                                     $"Captured building {building.LogicEntityId.Value} has no BuffComp.");
            string buffId = $"building_capture_invincible_{building.LogicEntityId.Value}";
            var buffData = BuffData.Create(
                id: buffId,
                duration: (Fix64)3,
                isForever: false,
                maxStack: 1,
                modules: new List<BuffCallback> { new BuildingCaptureInvincibleBuff() });
            buffComp.AddBuff(buffData, building);

            if (LogicEntityLifecycleService.TryGetBoundView(building.LogicEntityId, out MAEntity buildingView))
            {
                if (buildingView is not BuildingEntity buildingEntityView)
                    throw new InvalidOperationException($"Building {building.LogicEntityId.Value} is bound to non-building view {buildingView.GetType().Name}.");
                buildingEntityView.BindStrongholdView(stronghold, oldOwnerFactionId, true);
            }
        }

        if (capturedByPlayer)
        {
            bool promotedCore = false;
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i] is not IBuildingLogicContext building
                    || !building.IsGameEndConditionBuilding
                    || !string.Equals(building.StrongholdId, strongholdId, StringComparison.Ordinal))
                {
                    continue;
                }

                LogicGameEndService.PromoteCapturedConditionBuildingToPlayerTarget(building.BuildingInstanceId);
                promotedCore = true;
            }
            if (!promotedCore)
                throw new InvalidOperationException($"Captured stronghold '{strongholdId}' has no game-end condition core building.");
        }

        Log.Info("Stronghold captured. id={0}, newOwnerFaction={1}",
            strongholdId,
            newOwnerFactionId);

        PlayCaptureVfx(stronghold);

        RefreshEnemyStrongholdFogEffects(stronghold);
    }

    private Stronghold GetStrongholdViewRequired(string strongholdId)
    {
        IReadOnlyList<Stronghold> strongholds = Strongholds;
        for (int i = 0; i < strongholds.Count; i++)
        {
            Stronghold stronghold = strongholds[i];
            if (stronghold?.strongholdData != null
                && string.Equals(stronghold.strongholdData.StrongholdId, strongholdId, StringComparison.Ordinal))
            {
                return stronghold;
            }
        }

        throw new InvalidOperationException($"LevelEntity cannot resolve stronghold view '{strongholdId}'.");
    }

    /// <summary>
    /// 在据点中心播放占领特效
    /// </summary>
    private void PlayCaptureVfx(Stronghold stronghold)
    {
        if (stronghold == null)
            return;

        if (!TryGetStrongholdWorldBounds(stronghold, out Bounds worldBounds))
            return;

        Vector3 center = worldBounds.center;
        center.y += 0.1f;
        float shortSide = Mathf.Min(worldBounds.size.x, worldBounds.size.z);
        float vfxScale = shortSide / CaptureVfxBaseDiameter;
        var vfxParams = EntityParams.Create(center, Vector3.zero, Vector3.one * Mathf.Max(0.01f, vfxScale));
        GF.Entity.ShowEffect("占领特效", vfxParams);
    }
    private static int ResolveCaptureFactionId(IEntityContext attacker)
    {
        if (attacker == null)
            throw new InvalidOperationException("Stronghold capture requires an attacker.");

        if (attacker.TryGetLogicBuilding(out IBuildingLogicContext attackerBuilding))
            return attackerBuilding.OwnerFactionId;

        int factionId = EntitySideHelper.ToFactionId(attacker.Side);
        if (factionId < 0)
            throw new InvalidOperationException($"Stronghold capture attacker {attacker.LogicEntityId.Value} has no faction.");
        return factionId;
    }
}

