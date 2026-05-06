using GameFramework;
using GameFramework.Event;
using System;
using System.Collections.Generic;
using GiantGrey.TileWorldCreator;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UnityGameFramework.Runtime;

public partial class LevelEntity : EntityBase
{
    private const string StrongholdLayerPrefix = "SH";
    private TileWorldCreatorManager tileWorldCreatorManager;
    private NavMeshSurface[] _navMeshSurfaces;


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

        // 延迟烘焙：最后一次请求后 0.5 秒执�?
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
            var sh = Strongholds != null && Strongholds.Count > 0 ? Strongholds[0] : null;
            if (sh != null)
            {
                Debug.Log("[LevelEntity] 测试播放占领特效 (T)");
                PlayCaptureVfx(sh);
            }
            else
            {
                Debug.LogWarning("[LevelEntity] 没有可用的 Stronghold 来测试占领特效");
            }
        }
#endif
    }

    /// <summary>
    /// 请求烘焙 NavMesh。不会立即执行，而是等最后一次请求后 0.5 秒再烘焙
    /// 多次调用会重置计时器，确保批量建造只烘焙一次
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
                    if (!buildManager.TryBuildBuildingForLevelInit(effectiveIdentifier, point.Position, out var buildingInstanceId, isGameEndConditionBuilding: point.IsGameEndConditionBuilding))
                    {
                        Log.Error("LevelEntity.SpawnPresetEntities failed: cannot build preset building '{0}'.", effectiveIdentifier);
                        break;
                    }

                    if (point.IsGameEndConditionBuilding)
                    {
                        int initialOwnerFactionId = ResolveOwnerFactionIdByPosition(point.Position);

                        // 特殊逻辑：快递柜的所有者应该根据据点所有权来设置
                        // 如果快递柜位于敌方据点内，应该属于敌人（显示红色血条）
                        // 如果快递柜位于玩家据点内，应该属于玩家（显示绿色血条）
                        if (effectiveIdentifier.Contains("ParcelLocker"))
                        {
                            var stronghold = GetStrongholdAtWorldPosition(point.Position);
                            if (stronghold != null)
                            {
                                initialOwnerFactionId = stronghold.OwnerFactionId;
                                Debug.Log($"[LevelEntity] 设置快递柜所有者: {effectiveIdentifier}, 位置: {point.Position}, 据点所有者: {stronghold.OwnerFactionId}, 血条颜色: {(initialOwnerFactionId == EntitySideHelper.PlayerFactionId ? "绿色(友方)" : "红色(敌方)")}");
                            }
                        }

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

        // 同步修改属于该据点的兵归属
        SideType newSide = EntitySideHelper.ToSide(newOwnerFactionId);
        var creatureGroup = GF.Entity.GetEntityGroup(Const.EntityGroup.Creature.ToString());
        if (creatureGroup != null)
        {
            var entities = creatureGroup.GetAllEntities();
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] is UnityGameFramework.Runtime.Entity entity && entity.Logic is SoldierEntity soldier)
                {
                    if (soldier.Alive && soldier.SourceStrongholdId == stronghold.strongholdData.StrongholdId && soldier.Side != newSide)
                    {
                        soldier.ChangeSide(newSide);
                    }
                }
            }
        }

        for (int i = 0; i < stronghold.Buildings.Count; i++)
        {
            var building = stronghold.Buildings[i];
            if (building == null)
                continue;

            building.SetStronghold(stronghold);
            building.RestoreToFullHealthAndEnable();

            // 占领后短时无敌保护（避免队友立即误伤）
            try
            {
                if (building.BuffComp != null)
                {
                    string buffId = $"building_capture_invincible_{building.Id}";
                    var buffData = BuffData.Create(
                        id: buffId,
                        duration: 3f,
                        isForever: false,
                        maxStack: 1,
                        modules: new System.Collections.Generic.List<BuffCallback> { new BuildingCaptureInvincibleBuff() }
                    );

                    building.BuffComp.AddBuff(buffData, building);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[LevelEntity] Failed to apply capture invincible buff to building id={building.Id}: {ex}");
            }
        }

        Log.Info("Stronghold captured. id={0}, newOwnerFaction={1}",
            stronghold.strongholdData != null ? stronghold.strongholdData.StrongholdId : "<unknown>",
            newOwnerFactionId);

        PlayCaptureVfx(stronghold);

        RefreshEnemyStrongholdFogEffects(stronghold);
    }

    /// <summary>
    /// 在据点中心播放占领特效
    /// </summary>
    private void PlayCaptureVfx(Stronghold stronghold)
    {
        if (stronghold == null || stronghold.Buildings == null || stronghold.Buildings.Count == 0)
            return;

        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < stronghold.Buildings.Count; i++)
        {
            var b = stronghold.Buildings[i];
            if (b == null)
                continue;
            sum += b.transform.position;
            count++;
        }
        if (count == 0)
            return;

        Vector3 center = sum / count;
        center.y += 0.1f;
        var vfxParams = EntityParams.Create(center, Vector3.zero, Vector3.one);
        GF.Entity.ShowEffect("占领特效", vfxParams);
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

