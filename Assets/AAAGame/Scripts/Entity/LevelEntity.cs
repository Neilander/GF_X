using GameFramework;
using GameFramework.Event;
using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;
using UnityGameFramework.Runtime;
using GiantGrey.TileWorldCreator;

public class LevelEntity : EntityBase
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

    public Stronghold GetStrongholdAtWorldPosition(Vector3 worldPosition)
    {
        if (tileWorldCreatorManager == null)
        {
            return null;
        }

        Vector2 gridPosition = tileWorldCreatorManager.GetRelativeGridPosition(worldPosition);
        return GetStrongholdAtGridPosition(gridPosition);
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
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        UnsubscribeRuntimeLayerRules();

        if (activeLevelEntity == this)
        {
            activeLevelEntity = null;
        }

        InGameDataModel.ClearStrongholdRuntimeData();
        tileWorldCreatorManager = null;
        _navMeshSurfaces = null;

        base.OnHide(isShutdown, userData);
    }

    private float _rebakeTimer = -1f;
    private const float RebakeDelay = 0.5f;

    private void Update()
    {
        // 延迟烘焙：最后一次请求后 0.5 秒执行
        if (_rebakeTimer >= 0f)
        {
            _rebakeTimer -= Time.deltaTime;
            if (_rebakeTimer < 0f)
            {
                Debug.Log("[LevelEntity] 延迟烘焙 NavMesh 执行");
                DoRebakeNavMesh();
            }
        }
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
            return;

        for (int i = 0; i < _navMeshSurfaces.Length; i++)
        {
            _navMeshSurfaces[i].BuildNavMesh();
        }
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
        var presetPoints = GameObject.FindObjectsOfType<EntityPresetPoint>();
        foreach (var point in presetPoints)
        {
            switch (point.PointType)
            {
                case EntityPresetPointType.Building:
                    if (!buildManager.TryBuildBuildingForLevelInit(point.Identifier, point.Position, out var buildingInstanceId))
                    {
                        Log.Error("LevelEntity.SpawnPresetEntities failed: cannot build preset building '{0}'.", point.Identifier);
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
            RegisterBuildingToStrongholdInternal(existingBuildings[i]);
        }

        Log.Info(
            "LevelEntity.CollectStrongholds done. Strongholds={0}",
            strongholds.Count);
    }

    public static void RegisterBuildingToStronghold(BuildingEntity building)
    {
        if (activeLevelEntity == null)
        {
            return;
        }

        activeLevelEntity.RegisterBuildingToStrongholdInternal(building);
    }

    public static void UnregisterBuildingFromStronghold(BuildingEntity building)
    {
        if (activeLevelEntity == null)
        {
            return;
        }

        activeLevelEntity.UnregisterBuildingFromStrongholdInternal(building);
    }

    public static void NotifyBuildingDisabled(BuildingEntity building, IEntityContext attacker)
    {
        if (activeLevelEntity == null)
            return;

        activeLevelEntity.TryCaptureStrongholdAfterBuildingDisabled(building, attacker);
    }

    private void RegisterBuildingToStrongholdInternal(BuildingEntity building)
    {
        if (building == null || tileWorldCreatorManager == null)
        {
            return;
        }

        UnregisterBuildingFromStrongholdInternal(building);

        var stronghold = GetStrongholdAtWorldPosition(building.transform.position);
        building.SetStronghold(stronghold);
        if (stronghold != null)
        {
            stronghold.Buildings.Add(building);
        }

        InGameDataModel.RegisterStrongholdBuilding(building);
    }

    private void UnregisterBuildingFromStrongholdInternal(BuildingEntity building)
    {
        if (building == null)
        {
            return;
        }

        var stronghold = building.CurrentStronghold;
        if (stronghold != null)
        {
            stronghold.Buildings.Remove(building);
        }

        building.SetStronghold(null);
        InGameDataModel.UnregisterStrongholdBuilding(building);
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
