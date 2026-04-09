using GameFramework;
using GameFramework.Event;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;
using GiantGrey.TileWorldCreator;

public class LevelEntity : EntityBase
{
    private const string StrongholdLayerPrefix = "SH";
    private TileWorldCreatorManager tileWorldCreatorManager;

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

        base.OnHide(isShutdown, userData);
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
        var presetPoints = GameObject.FindObjectsOfType<EntityPresetPoint>();
        foreach (var point in presetPoints)
        {
            switch (point.PointType)
            {
                case EntityPresetPointType.Building:
                    BuildManager.BuildBuildingForLevelInit(point.Identifier, point.Position);
                    break;
            }
        }
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
}
