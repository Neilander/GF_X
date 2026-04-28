using GameFramework;
using GameFramework.Event;
using System;
using System.Collections.Generic;
using GiantGrey.TileWorldCreator;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UnityGameFramework.Runtime;

public class LevelEntity : EntityBase
{
    private const string StrongholdLayerPrefix = "SH";
    private const string EnemyStrongholdFogPrefabPath = "Effect/EnemySHFog";
    private const float EnemyStrongholdFogInsetDistance = 1f;
    private const float EnemyStrongholdFogOuterHeight = 4.0f;
    private const float EnemyStrongholdFogMiddleHeight = 1.5f;
    private const float EnemyStrongholdFogInnerHeight = 0.5f;
    private const float EnemyStrongholdFogBaseYOffset = 0.15f;
    private TileWorldCreatorManager tileWorldCreatorManager;
    private NavMeshSurface[] _navMeshSurfaces;
    private readonly Dictionary<string, List<int>> _enemyStrongholdFogEntityIdsByStrongholdId = new Dictionary<string, List<int>>();


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

        _navMeshSurfaces = GetComponentsInChildren<NavMeshSurface>();

        CollectStrongholds();
        SubscribeRuntimeLayerRules();
        ApplyStrongholdRuntimeLayerRules();
        SpawnPresetEntities();
        SyncEnemyStrongholdFogEffects();
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        UnsubscribeRuntimeLayerRules();

        if (activeLevelEntity == this)
        {
            activeLevelEntity = null;
        }

        InGameDataModel.ClearStrongholdRuntimeData();
        ClearEnemyStrongholdFogEffects();
        tileWorldCreatorManager = null;
        _navMeshSurfaces = null;

        base.OnHide(isShutdown, userData);
    }

    private float _rebakeTimer = -1f;
    private const float RebakeDelay = 0.5f;

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);

        // 延迟烘焙：最后一次请求后 0.5 秒执行
        if (_rebakeTimer >= 0f)
        {
            _rebakeTimer -= realElapseSeconds;
            if (_rebakeTimer < 0f)
            {
                Debug.Log("[LevelEntity] 延迟烘焙 NavMesh 执行");
                DoRebakeNavMesh();
            }
        }

#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.T))
        {
            Debug.Log("[LevelEntity] 手动烘焙 NavMesh (T)");
            DoRebakeNavMesh();
        }
#endif
    }

    /// <summary>
    /// 请求烘焙 NavMesh。不会立即执行，而是等最后一次请求后 0.5 秒再烘焙。
    /// 多次调用会重置计时器，确保批量建造只烘焙一次。
    /// </summary>
    public static void RequestRebakeNavMesh()
    {
        if (activeLevelEntity == null)
            return;

        activeLevelEntity._rebakeTimer = RebakeDelay;
    }

    private void DoRebakeNavMesh()
    {
        if (_navMeshSurfaces == null)
        {
            Debug.LogWarning("[LevelEntity] _navMeshSurfaces == null，烘焙跳过");
            return;
        }

        Debug.Log($"[LevelEntity] NavMeshSurface 数量={_navMeshSurfaces.Length}");
        for (int i = 0; i < _navMeshSurfaces.Length; i++)
        {
            var s = _navMeshSurfaces[i];
            Debug.Log($"  [{i}] on='{s.gameObject.name}' collect={s.collectObjects} layers={s.layerMask.value} useGeom={s.useGeometry} agentType={s.agentTypeID} size={s.size} center={s.center}");
            s.BuildNavMesh();
        }

        EnablePlayerNavMeshBypass();
    }

    /// <summary>
    /// 烘焙完后让玩家可以"无视 NavMesh 自由移动"，直到自己走回 NavMesh 上自动恢复。
    /// 用于建造时玩家被新建筑围在 NavMesh 之外的情况。
    /// </summary>
    private static void EnablePlayerNavMeshBypass()
    {
        var player = EntityRegistry.Player;
        if (player == null)
        {
            Debug.Log("[LevelEntity] NavMesh bypass 触发: 无 Player, 跳过");
            return;
        }
        if (!(player is MAEntity mae) || mae == null)
        {
            Debug.Log("[LevelEntity] NavMesh bypass 触发: Player 不是 MAEntity, 跳过");
            return;
        }

        var executor = mae.GetComponent<MoveExecutor>();
        if (executor == null)
        {
            Debug.LogWarning("[LevelEntity] NavMesh bypass 触发: 玩家无 MoveExecutor, 跳过");
            return;
        }

        executor.EnableBypassUntilOnNavMesh();
        Debug.Log($"[LevelEntity] NavMesh bypass: 启用玩家自由移动 (无视 NavMesh) playerPos={mae.transform.position}");
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

    private void SpawnPresetEntities()
    {
        var buildManager = GameEntry.GetComponent<BuildManager>();
        var gameEndManager = GameEntry.GetComponent<GameEndManager>();
        // 只使用当前关卡实体层级下的预设点，避免 launch 等并存场景中的同名点干扰出生位置。
        var presetPoints = GetComponentsInChildren<EntityPresetPoint>(true);
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
                    SoldierFactory.ShowSoldier(heroUnitType, point.Position, SideType.PlayerSide, BrainType.Player);
                    heroSpawned = true;
                    break;

                case EntityPresetPointType.Building:
                    if (!buildManager.TryBuildBuildingForLevelInit(effectiveIdentifier, point.Position, out var buildingInstanceId))
                    {
                        Log.Error("LevelEntity.SpawnPresetEntities failed: cannot build preset building '{0}'.", effectiveIdentifier);
                        break;
                    }

                    if (point.IsGameEndConditionBuilding)
                    {
                        int initialOwnerFactionId = ResolveOwnerFactionIdByPosition(point.Position);
                        gameEndManager.RegisterInitialConditionBuilding(buildingInstanceId, initialOwnerFactionId);
                    }
                    break;
            }
        }

        // 所有初始建筑建完后请求烘焙（延迟 0.5 秒）
        RequestRebakeNavMesh();
    }

    private int ResolveOwnerFactionIdByPosition(Vector3 position)
    {
        var stronghold = GetStrongholdAtWorldPosition(position);
        return stronghold != null ? stronghold.OwnerFactionId : EntitySideHelper.PlayerFactionId;
    }

    private void CollectStrongholds()
    {
        tileWorldCreatorManager = GetComponentInChildren<TileWorldCreatorManager>();
        if (tileWorldCreatorManager == null || tileWorldCreatorManager.configuration == null)
        {
            Log.Error("LevelEntity.CollectStrongholds failed: TileWorldCreatorManager or configuration is null.");
            return;
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

        var existingBuildings = GameObject.FindObjectsOfType<BuildingEntity>();
        for (int i = 0; i < existingBuildings.Length; i++)
        {
            InGameDataModel.RegisterBuilding(existingBuildings[i]);
        }

        Log.Info(
            "LevelEntity.CollectStrongholds done. Strongholds={0}",
            strongholds.Count);
    }

    public static void NotifyBuildingDisabled(BuildingEntity building, IEntityContext attacker)
    {
        if (activeLevelEntity == null)
            return;

        activeLevelEntity.TryCaptureStrongholdAfterBuildingDisabled(building, attacker);
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

    private void TryCaptureStrongholdAfterBuildingDisabled(BuildingEntity disabledBuilding, IEntityContext attacker)
    {
        if (disabledBuilding == null)
            return;

        var stronghold = disabledBuilding.CurrentStronghold;
        if (stronghold == null)
            return;

        int captureFactionId = ResolveCaptureFactionId(attacker);
        if (captureFactionId < 0)
            captureFactionId = EntitySideHelper.PlayerFactionId;

        if (stronghold.OwnerFactionId == captureFactionId)
            return;

        bool hasCapturableBuildings = false;
        for (int i = 0; i < stronghold.Buildings.Count; i++)
        {
            var building = stronghold.Buildings[i];
            if (building == null || building.IsLv0Invincible)
                continue;

            hasCapturableBuildings = true;
            if (!building.IsDisabled)
                return;
        }

        if (!hasCapturableBuildings)
            return;

        CaptureStronghold(stronghold, captureFactionId);
    }

    private void CaptureStronghold(Stronghold stronghold, int newOwnerFactionId)
    {
        if (stronghold == null)
            return;

        stronghold.OwnerFactionId = newOwnerFactionId;

        for (int i = 0; i < stronghold.Buildings.Count; i++)
        {
            var building = stronghold.Buildings[i];
            if (building == null)
                continue;

            building.SetStronghold(stronghold);
            building.RestoreToFullHealthAndEnable();
        }

        Log.Info("Stronghold captured. id={0}, newOwnerFaction={1}",
            stronghold.strongholdData != null ? stronghold.strongholdData.StrongholdId : "<unknown>",
            newOwnerFactionId);

        RefreshEnemyStrongholdFogEffects(stronghold);
    }

    private void SyncEnemyStrongholdFogEffects()
    {
        ClearEnemyStrongholdFogEffects();

        if (tileWorldCreatorManager == null || tileWorldCreatorManager.configuration == null)
            return;

        IReadOnlyList<Stronghold> strongholds = Strongholds;
        if (strongholds == null || strongholds.Count == 0)
            return;

        for (int i = 0; i < strongholds.Count; i++)
        {
            Stronghold stronghold = strongholds[i];
            if (stronghold == null
                || stronghold.OwnerFactionId == EntitySideHelper.PlayerFactionId
                || stronghold.strongholdData == null
                || stronghold.strongholdData.RangeCells == null
                || stronghold.strongholdData.RangeCells.Count == 0)
            {
                continue;
            }

            RefreshEnemyStrongholdFogEffects(stronghold);
        }
    }

    private void RefreshEnemyStrongholdFogEffects(Stronghold stronghold)
    {
        if (stronghold == null || stronghold.strongholdData == null)
            return;

        string strongholdId = stronghold.strongholdData.StrongholdId;
        if (string.IsNullOrWhiteSpace(strongholdId))
            return;

        ClearEnemyStrongholdFogEffects(strongholdId);

        if (stronghold.OwnerFactionId == EntitySideHelper.PlayerFactionId)
            return;

        if (!TryGetStrongholdWorldBounds(stronghold, out var worldBounds))
            return;

        SpawnEnemyStrongholdFogLayers(strongholdId, worldBounds);
    }

    private void ClearEnemyStrongholdFogEffects()
    {
        if (_enemyStrongholdFogEntityIdsByStrongholdId.Count == 0)
            return;

        foreach (var pair in _enemyStrongholdFogEntityIdsByStrongholdId)
        {
            var entityIds = pair.Value;
            if (entityIds == null)
                continue;

            for (int i = 0; i < entityIds.Count; i++)
            {
                GF.Entity.HideEntitySafe(entityIds[i]);
            }
        }

        _enemyStrongholdFogEntityIdsByStrongholdId.Clear();
    }

    private void ClearEnemyStrongholdFogEffects(string strongholdId)
    {
        if (string.IsNullOrWhiteSpace(strongholdId))
            return;

        if (!_enemyStrongholdFogEntityIdsByStrongholdId.TryGetValue(strongholdId, out var entityIds) || entityIds == null)
            return;

        for (int i = 0; i < entityIds.Count; i++)
        {
            GF.Entity.HideEntitySafe(entityIds[i]);
        }

        _enemyStrongholdFogEntityIdsByStrongholdId.Remove(strongholdId);
    }

    private bool TryGetStrongholdWorldBounds(Stronghold stronghold, out Bounds worldBounds)
    {
        worldBounds = default;

        if (stronghold?.strongholdData?.RangeCells == null || stronghold.strongholdData.RangeCells.Count == 0)
            return false;

        var configuration = tileWorldCreatorManager != null ? tileWorldCreatorManager.configuration : null;
        if (configuration == null)
            return false;

        float cellSize = Mathf.Max(0.01f, configuration.cellSize);

        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;

        foreach (Vector2 cell in stronghold.strongholdData.RangeCells)
        {
            int x = Mathf.RoundToInt(cell.x);
            int y = Mathf.RoundToInt(cell.y);
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        if (minX == int.MaxValue || minY == int.MaxValue)
            return false;

        // Compute world-space corners using the manager's transform to respect rotation/position.
        Vector3 localMin = new Vector3(minX * cellSize, 0f, minY * cellSize);
        Vector3 localMax = new Vector3((maxX + 1) * cellSize, 0f, (maxY + 1) * cellSize);

        Vector3 worldMin = tileWorldCreatorManager.transform.TransformPoint(localMin);
        Vector3 worldMax = tileWorldCreatorManager.transform.TransformPoint(localMax);

        Vector3 centerXZ = new Vector3((worldMin.x + worldMax.x) * 0.5f, 0f, (worldMin.z + worldMax.z) * 0.5f);
        float sampledLayerHeight = 0f;
        try
        {
            sampledLayerHeight = tileWorldCreatorManager.SampleLayerHeight(centerXZ);
        }
        catch
        {
            sampledLayerHeight = 0f;
        }

        Vector3 center = new Vector3(
            centerXZ.x,
            tileWorldCreatorManager.transform.position.y + sampledLayerHeight + EnemyStrongholdFogBaseYOffset,
            centerXZ.z);

        Vector3 size = new Vector3(
            Mathf.Max(cellSize, Mathf.Abs(worldMax.x - worldMin.x)),
            0.01f,
            Mathf.Max(cellSize, Mathf.Abs(worldMax.z - worldMin.z)));

        worldBounds = new Bounds(center, size);
        return true;
    }

    private void SpawnEnemyStrongholdFogLayers(string strongholdId, Bounds worldBounds)
    {
        var entityIds = new List<int>();

        float areaBase = 50.0f;

        // --- 1. 边缘圈（Outer rim） ---
        // 为了彻底解决“外圈生成框内部中心会产云”的问题，我们不再依赖BoxThickness的镂空，
        // 而是直接在据点的4个边缘各自生成一个实心的薄栅栏形状，像拼乐高一样围住据点。
        float outerThickness = 1.0f; // 边缘厚度
        float outerHeight = 3.0f;    // 边缘高度
        float hx = worldBounds.size.x * 0.5f;
        float hz = worldBounds.size.z * 0.5f;
        float otHalf = outerThickness * 0.5f;

        // 南北两面墙 (Top / Bottom) - 沿X轴宽，沿Z轴薄
        Vector3 tbSize = new Vector3(worldBounds.size.x, outerHeight, outerThickness);
        float tbArea = tbSize.x * outerThickness;
        float tbEmission = (tbArea / areaBase) * 10f; // 墙面越小，发射密度要对应增加以保持云量
        
        // 东西两面墙 (Left / Right) - 扣掉转角避免重叠，沿Z轴长，沿X轴薄
        Vector3 lrSize = new Vector3(outerThickness, outerHeight, Mathf.Max(0.1f, worldBounds.size.z - outerThickness * 2f));
        float lrArea = lrSize.z * outerThickness;
        float lrEmission = (lrArea / areaBase) * 10f;

        // 生成四面高墙，完全实心 (shellThickness = 0)，只在自己那非常狭窄的范围里产云
        SpawnEnemyStrongholdFogLayer(entityIds, worldBounds.center + new Vector3(0, 0, hz - otHalf), tbSize, 0f, 0f, tbEmission, 0.6f, 1.0f, -12); // 北墙
        SpawnEnemyStrongholdFogLayer(entityIds, worldBounds.center + new Vector3(0, 0, -hz + otHalf), tbSize, 0f, 0f, tbEmission, 0.6f, 1.0f, -12); // 南墙
        SpawnEnemyStrongholdFogLayer(entityIds, worldBounds.center + new Vector3(hx - otHalf, 0, 0), lrSize, 0f, 0f, lrEmission, 0.6f, 1.0f, -12);  // 东墙
        SpawnEnemyStrongholdFogLayer(entityIds, worldBounds.center + new Vector3(-hx + otHalf, 0, 0), lrSize, 0f, 0f, lrEmission, 0.6f, 1.0f, -12); // 西墙

        // --- 2. 内部（Inner area） ---
        // 内部我们只需要非常低矮、稀疏、甚至有点零星的云
        float innerThickness = 0f; 
        Vector3 innerSize = new Vector3(
            Mathf.Max(0.1f, worldBounds.size.x - outerThickness * 2f),
            0.1f, // 内部云完全压成饼
            Mathf.Max(0.1f, worldBounds.size.z - outerThickness * 2f));
        float innerArea = innerSize.x * innerSize.z;
        float innerEmissionMult = (innerArea / areaBase) * 0.5f; 

        if (innerSize.x > 0.5f && innerSize.z > 0.5f)
        {
            // 内圈使用实心Box生成，极低的高度，稍大的粒子。
            SpawnEnemyStrongholdFogLayer(entityIds, worldBounds.center, innerSize, -0.2f, innerThickness, innerEmissionMult, 0.4f, 0.6f, -10);
        }

        _enemyStrongholdFogEntityIdsByStrongholdId[strongholdId] = entityIds;
    }

    private void SpawnEnemyStrongholdFogLayer(List<int> entityIds, Vector3 worldCenter, Vector3 size, float yOffset, float shellThickness, float emissionRate, float startSizeMultiplier, float alpha, int sortingOrder)
    {
        Vector3 spawnPosition = worldCenter;
        spawnPosition.y = worldCenter.y + yOffset;

        EntityParams fogParams = EntityParams.Create(spawnPosition, null, Vector3.one);
        fogParams.Set<VarFloat>(ParticleEntity.LIFE_TIME, 0f);
        fogParams.Set<VarInt32>(ParticleEntity.SORT_LAYER, sortingOrder);
        fogParams.OnShowCallback = entity => ConfigureEnemyStrongholdFogParticle(entity, size, shellThickness, emissionRate, startSizeMultiplier, alpha, sortingOrder);

        int entityId = GF.Entity.ShowEntity<ParticleEntity>(EnemyStrongholdFogPrefabPath, Const.EntityGroup.Effect, fogParams);
        if (entityId > 0)
            entityIds.Add(entityId);
    }

    private static void ConfigureEnemyStrongholdFogParticle(UnityGameFramework.Runtime.EntityLogic entity, Vector3 size, float shellThickness, float emissionRate, float startSizeMultiplier, float alpha, int sortingOrder)
    {
        if (entity == null)
            return;

        var particleSystems = entity.GetComponentsInChildren<ParticleSystem>(true);
        if (particleSystems == null || particleSystems.Length == 0)
            return;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            var particleSystem = particleSystems[i];
            if (particleSystem == null)
                continue;

            var main = particleSystem.main;
            Color originalColor = main.startColor.color;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(originalColor.r, originalColor.g, originalColor.b, originalColor.a * Mathf.Clamp01(alpha)));
            main.startSizeMultiplier *= Mathf.Max(0.1f, startSizeMultiplier);

            var emission = particleSystem.emission;
            emission.rateOverTimeMultiplier *= Mathf.Max(0f, emissionRate);

            var shape = particleSystem.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(size.x, size.z, size.y); // Keep the emitter flat

            float normX = shellThickness > 0f ? Mathf.Clamp01(shellThickness * 2f / Mathf.Max(0.1f, size.x)) : 1f;
            float normY = shellThickness > 0f ? Mathf.Clamp01(shellThickness * 2f / Mathf.Max(0.1f, size.z)) : 1f;
            shape.boxThickness = new Vector3(normX, normY, 1f);

            var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
                renderer.sortingOrder = sortingOrder;

            particleSystem.Clear(true);
            particleSystem.Play(true);
        }
    }

    private static int ResolveCaptureFactionId(IEntityContext attacker)
    {
        if (attacker == null)
            return EntitySideHelper.PlayerFactionId;

        if (attacker is BuildingEntity attackerBuilding)
            return attackerBuilding.OwnerFactionID;

        int factionId = EntitySideHelper.ToFactionId(attacker.Side);
        return factionId >= 0 ? factionId : EntitySideHelper.PlayerFactionId;
    }
}
