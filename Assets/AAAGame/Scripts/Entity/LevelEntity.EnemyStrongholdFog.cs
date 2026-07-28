using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.Effect;

public partial class LevelEntity
{
    private const string EnemyStrongholdFogPrefabPath = "Effect/EnemySHFog";
    private readonly Dictionary<string, List<int>> _enemyStrongholdFogEntityIdsByStrongholdId = new Dictionary<string, List<int>>();
    private EffectRuntimeConfigComponent enemyStrongholdFogConfigComponent;
    private bool _enemyStrongholdFogConfigLogged;

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
                GF.Entity.HideEntitySafe(entityIds[i]);
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
            GF.Entity.HideEntitySafe(entityIds[i]);

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

        Vector3 localMin = new Vector3((minX - 0.5f) * cellSize, 0f, (minY - 0.5f) * cellSize);
        Vector3 localMax = new Vector3((maxX + 0.5f) * cellSize, 0f, (maxY + 0.5f) * cellSize);

        Vector3 worldMin = tileWorldCreatorManager.transform.TransformPoint(localMin);
        Vector3 worldMax = tileWorldCreatorManager.transform.TransformPoint(localMax);

        Vector3 centerXZ = new Vector3((worldMin.x + worldMax.x) * 0.5f, 0f, (worldMin.z + worldMax.z) * 0.5f);
        float strongholdPlaneY = tileWorldCreatorManager.transform.position.y;
        if (!TryGetStrongholdPlaneY(stronghold, out strongholdPlaneY))
        {
            try
            {
                strongholdPlaneY += tileWorldCreatorManager.SampleLayerHeight(centerXZ);
            }
            catch
            {
                strongholdPlaneY = tileWorldCreatorManager.transform.position.y;
            }
        }

        Vector3 center = new Vector3(centerXZ.x, strongholdPlaneY, centerXZ.z);
        Vector3 size = new Vector3(
            Mathf.Max(cellSize, Mathf.Abs(worldMax.x - worldMin.x)),
            0.01f,
            Mathf.Max(cellSize, Mathf.Abs(worldMax.z - worldMin.z)));

        worldBounds = new Bounds(center, size);
        return true;
    }

    private bool TryGetStrongholdPlaneY(Stronghold stronghold, out float planeY)
    {
        planeY = 0f;
        var presetPoints = GetComponentsInChildren<EntityPresetPoint>(true);
        bool hasValue = false;

        for (int i = 0; i < presetPoints.Length; i++)
        {
            var point = presetPoints[i];
            if (point == null)
                continue;

            var pointStronghold = GetStrongholdAtWorldPosition(point.Position);
            if (pointStronghold != stronghold)
                continue;

            float pointY = point.Position.y;
            if (!hasValue || pointY > planeY)
            {
                planeY = pointY;
                hasValue = true;
            }
        }

        return hasValue;
    }

    private void SpawnEnemyStrongholdFogLayers(string strongholdId, Bounds worldBounds)
    {
        EffectRuntimeConfigComponent config = GetEnemyStrongholdFogVisualConfig();
        if (config == null)
            return;

        var entityIds = new List<int>();
        float areaBase = Mathf.Max(1f, config.FogAreaBase);
        float outerThickness = Mathf.Max(0.01f, config.FogOuterThickness);
        float outerHeight = Mathf.Max(0.01f, config.FogOuterHeight);
        float hx = worldBounds.size.x * 0.5f;
        float hz = worldBounds.size.z * 0.5f;
        float otHalf = outerThickness * 0.5f;
        float fogGroundY = worldBounds.center.y + config.FogGroundYOffset;
        float outerCenterY = fogGroundY + config.FogOuterBottomOffset + outerHeight * 0.5f;

        Vector3 tbSize = new Vector3(worldBounds.size.x, outerHeight, outerThickness);
        float tbArea = tbSize.x * outerThickness;
        float tbEmission = (tbArea / areaBase) * config.FogOuterDensityScale;

        Vector3 lrSize = new Vector3(outerThickness, outerHeight, Mathf.Max(0.1f, worldBounds.size.z - outerThickness * 2f));
        float lrArea = lrSize.z * outerThickness;
        float lrEmission = (lrArea / areaBase) * config.FogOuterDensityScale;

        SpawnEnemyStrongholdFogLayer(entityIds, new Vector3(worldBounds.center.x, outerCenterY, worldBounds.center.z + hz - otHalf), tbSize, 0f, 0f, tbEmission, config.FogOuterStartSizeMultiplier, config.FogOuterAlpha, config.FogOuterSortingOrder);
        SpawnEnemyStrongholdFogLayer(entityIds, new Vector3(worldBounds.center.x, outerCenterY, worldBounds.center.z - hz + otHalf), tbSize, 0f, 0f, tbEmission, config.FogOuterStartSizeMultiplier, config.FogOuterAlpha, config.FogOuterSortingOrder);
        SpawnEnemyStrongholdFogLayer(entityIds, new Vector3(worldBounds.center.x + hx - otHalf, outerCenterY, worldBounds.center.z), lrSize, 0f, 0f, lrEmission, config.FogOuterStartSizeMultiplier, config.FogOuterAlpha, config.FogOuterSortingOrder);
        SpawnEnemyStrongholdFogLayer(entityIds, new Vector3(worldBounds.center.x - hx + otHalf, outerCenterY, worldBounds.center.z), lrSize, 0f, 0f, lrEmission, config.FogOuterStartSizeMultiplier, config.FogOuterAlpha, config.FogOuterSortingOrder);

        float innerThickness = Mathf.Max(0f, config.FogInnerThickness);
        Vector3 innerSize = new Vector3(
            Mathf.Max(0.1f, worldBounds.size.x - outerThickness * 2f),
            Mathf.Max(0.01f, config.FogInnerHeight),
            Mathf.Max(0.1f, worldBounds.size.z - outerThickness * 2f));
        float innerArea = innerSize.x * innerSize.z;
        float innerEmissionMult = (innerArea / areaBase) * config.FogInnerDensityScale;
        float innerCenterY = fogGroundY + config.FogInnerBottomOffset + innerSize.y * 0.5f;
        if (innerSize.x > 0.5f && innerSize.z > 0.5f)
            SpawnEnemyStrongholdFogLayer(entityIds, new Vector3(worldBounds.center.x, innerCenterY, worldBounds.center.z), innerSize, 0f, innerThickness, innerEmissionMult, config.FogInnerStartSizeMultiplier, config.FogInnerAlpha, config.FogInnerSortingOrder);

        _enemyStrongholdFogEntityIdsByStrongholdId[strongholdId] = entityIds;
    }

    private void SpawnEnemyStrongholdFogLayer(List<int> entityIds, Vector3 worldCenter, Vector3 size, float yOffset, float shellThickness, float emissionRate, float startSizeMultiplier, float alpha, int sortingOrder)
    {
        Vector3 spawnPosition = worldCenter;
        spawnPosition.y = worldCenter.y + yOffset;

        EntityParams fogParams = EntityParams.Create(spawnPosition, null, Vector3.one);
        fogParams.Set<VarFloat>(ParticleEntity.LIFE_TIME, 0f);
        fogParams.OnShowCallback = entity => ConfigureEnemyStrongholdFogParticle(entity, size, shellThickness, emissionRate, startSizeMultiplier, alpha, sortingOrder);

        int entityId = GF.Entity.ShowEntity<ParticleEntity>(EnemyStrongholdFogPrefabPath, Const.EntityGroup.Effect, fogParams);
        if (entityId > 0)
            entityIds.Add(entityId);
    }

    private static void ConfigureEnemyStrongholdFogParticle(EntityLogic entity, Vector3 size, float shellThickness, float emissionRate, float startSizeMultiplier, float alpha, int sortingOrder)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));

        var particleSystems = entity.GetComponentsInChildren<ParticleSystem>(true);
        if (particleSystems == null || particleSystems.Length == 0)
            throw new InvalidOperationException("Enemy stronghold fog entity has no ParticleSystem.");

        EnemyStrongholdFogParticleBaseline baseline = entity.GetComponent<EnemyStrongholdFogParticleBaseline>();
        if (baseline == null)
            baseline = entity.gameObject.AddComponent<EnemyStrongholdFogParticleBaseline>();

        for (int i = 0; i < particleSystems.Length; i++)
        {
            var particleSystem = particleSystems[i];
            if (particleSystem == null)
                throw new InvalidOperationException($"Enemy stronghold fog ParticleSystem is null at index {i}.");

            baseline.ApplyMultipliers(particleSystem, emissionRate, startSizeMultiplier, alpha);

            var shape = particleSystem.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(size.x, size.z, size.y);

            float normX = shellThickness > 0f ? Mathf.Clamp01(shellThickness * 2f / Mathf.Max(0.1f, size.x)) : 1f;
            float normY = shellThickness > 0f ? Mathf.Clamp01(shellThickness * 2f / Mathf.Max(0.1f, size.z)) : 1f;
            shape.boxThickness = new Vector3(normX, normY, 1f);

            var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
                throw new InvalidOperationException($"Enemy stronghold fog ParticleSystemRenderer is missing. particle={particleSystem.name}.");

            renderer.sortingOrder = sortingOrder;

            if (baseline.MarkRendererConfigured(renderer))
            {
                var mats = renderer.materials;
                if (mats == null || mats.Length == 0)
                    throw new InvalidOperationException($"Enemy stronghold fog renderer has no material. particle={particleSystem.name}.");

                int desiredQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 50;
                for (int mi = 0; mi < mats.Length; mi++)
                {
                    Material material = mats[mi];
                    if (material == null)
                        throw new InvalidOperationException($"Enemy stronghold fog renderer material is null. particle={particleSystem.name}, index={mi}.");
                    material.renderQueue = desiredQueue;
                }
            }

            particleSystem.Clear(true);
            particleSystem.Play(true);
        }
    }

    private EffectRuntimeConfigComponent GetEnemyStrongholdFogVisualConfig()
    {
        if (enemyStrongholdFogConfigComponent != null)
        {
            LogEnemyStrongholdFogConfig("Cache", enemyStrongholdFogConfigComponent);
            return enemyStrongholdFogConfigComponent;
        }

        enemyStrongholdFogConfigComponent = GameEntry.GetComponent<EffectRuntimeConfigComponent>();
        if (enemyStrongholdFogConfigComponent == null)
        {
            Debug.LogError("[EnemySHFog] Missing EffectRuntimeConfigComponent in Launch scene.");
            return null;
        }

        LogEnemyStrongholdFogConfig("EffectRuntimeConfigComponent", enemyStrongholdFogConfigComponent);
        return enemyStrongholdFogConfigComponent;
    }

    private void LogEnemyStrongholdFogConfig(string source, EffectRuntimeConfigComponent config)
    {
        if (_enemyStrongholdFogConfigLogged || config == null)
            return;

        _enemyStrongholdFogConfigLogged = true;
        Debug.Log(
            $"[EnemySHFog] Config source={source}, " +
            $"OuterThickness={config.FogOuterThickness}, OuterHeight={config.FogOuterHeight}, OuterDensityScale={config.FogOuterDensityScale}, OuterStartSizeMultiplier={config.FogOuterStartSizeMultiplier}, " +
            $"InnerHeight={config.FogInnerHeight}, InnerDensityScale={config.FogInnerDensityScale}, GroundYOffset={config.FogGroundYOffset}");
    }
}
