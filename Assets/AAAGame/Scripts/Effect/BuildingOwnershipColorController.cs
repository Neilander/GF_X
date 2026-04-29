using System.Collections.Generic;
using GameFramework;
using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace AAAGame.Effect
{
    public sealed class BuildingOwnershipColorController : GameFrameworkComponent
    {
        private readonly Dictionary<int, RendererColorSnapshot> rendererColorSnapshots = new Dictionary<int, RendererColorSnapshot>();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private EffectRuntimeConfigComponent config;
        private bool isSubscribed;
        private bool isInitialized;

        private sealed class RendererColorSnapshot
        {
            public Renderer Renderer;
            public MaterialPropertyBlock Block;
            public Color[] OriginalColors;
            public bool[] HasBaseColor;
            public bool[] HasColor;
        }

        private void Start()
        {
            TryInitialize();
            TrySubscribeEvents();
        }

        private void Update()
        {
            if (!isInitialized)
                TryInitialize();

            if (!isSubscribed)
                TrySubscribeEvents();
        }

        private void OnDestroy()
        {
            RestoreAllBuildingColors();
            TryUnsubscribeEvents();
        }

        private void TryInitialize()
        {
            if (isInitialized)
                return;

            config = GameEntry.GetComponent<EffectRuntimeConfigComponent>();
            if (config == null)
                return;

            isInitialized = true;
            RefreshAllBuildingColors();
        }

        private void TrySubscribeEvents()
        {
            if (isSubscribed || GF.Event == null)
                return;

            GF.Event.Subscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
            GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);
            isSubscribed = true;
        }

        private void TryUnsubscribeEvents()
        {
            if (!isSubscribed)
                return;

            if (GF.Event != null)
            {
                try
                {
                    GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
                    GF.Event.Unsubscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);
                }
                catch (GameFrameworkException)
                {
                    // PlayMode 退出时 EventPool 可能已释放，忽略退订异常。
                }
            }

            isSubscribed = false;
        }

        private void OnEntityFactionChanged(object sender, GameEventArgs e)
        {
            EntityFactionChangedEventArgs args = e as EntityFactionChangedEventArgs;
            if (args == null)
                return;

            BuildingEntity building = GF.Entity.GetEntity<BuildingEntity>(args.EntityId);
            if (building == null)
                return;

            ApplyOwnershipColor(building);
        }

        private void OnShowEntitySuccess(object sender, GameEventArgs e)
        {
            ShowEntitySuccessEventArgs args = e as ShowEntitySuccessEventArgs;
            if (args == null || args.Entity == null)
                return;

            BuildingEntity building = args.Entity.Logic as BuildingEntity;
            if (building == null)
                return;

            ApplyOwnershipColor(building);
        }

        private void RefreshAllBuildingColors()
        {
            BuildingEntity[] buildings = FindObjectsOfType<BuildingEntity>(true);
            for (int i = 0; i < buildings.Length; i++)
                ApplyOwnershipColor(buildings[i]);
        }

        private void ApplyOwnershipColor(BuildingEntity building)
        {
            if (building == null || config == null)
                return;

            bool isEnemy = IsEnemyBuilding(building);
            Renderer[] renderers = building.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                ApplyRendererOwnershipColor(renderers[i], isEnemy);
        }

        private static bool IsEnemyBuilding(BuildingEntity building)
        {
            if (building == null)
                return false;

            // 优先使用据点实时归属，避免初始化阶段 OwnerFactionID 尚未同步导致误判。
            if (LevelEntity.ActiveLevelEntity != null)
            {
                Stronghold stronghold = LevelEntity.GetStrongholdAtWorldPosition(building.transform.position);
                if (stronghold != null)
                    return stronghold.OwnerFactionId != EntitySideHelper.PlayerFactionId;
            }

            return building.OwnerFactionID != EntitySideHelper.PlayerFactionId;
        }

        private void ApplyRendererOwnershipColor(Renderer renderer, bool isEnemy)
        {
            if (renderer == null || config == null)
                return;

            int rendererId = renderer.GetInstanceID();
            if (!rendererColorSnapshots.TryGetValue(rendererId, out RendererColorSnapshot snapshot) || snapshot == null || snapshot.Renderer == null)
            {
                snapshot = CaptureRendererSnapshot(renderer);
                if (snapshot == null)
                    return;

                rendererColorSnapshots[rendererId] = snapshot;
            }

            Material[] sharedMaterials = renderer.sharedMaterials;
            int materialCount = sharedMaterials != null ? sharedMaterials.Length : 0;
            if (materialCount == 0)
                return;

            if (snapshot.Block == null)
                snapshot.Block = new MaterialPropertyBlock();

            renderer.GetPropertyBlock(snapshot.Block);
            for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
            {
                bool hasBase = materialIndex < snapshot.HasBaseColor.Length && snapshot.HasBaseColor[materialIndex];
                bool hasColor = materialIndex < snapshot.HasColor.Length && snapshot.HasColor[materialIndex];
                if (!hasBase && !hasColor)
                    continue;

                Color targetColor = isEnemy ? config.EnemyBuildingColor : snapshot.OriginalColors[materialIndex];
                if (hasBase)
                    snapshot.Block.SetColor(BaseColorId, targetColor);
                if (hasColor)
                    snapshot.Block.SetColor(ColorId, targetColor);
            }
            renderer.SetPropertyBlock(snapshot.Block);
        }

        private static RendererColorSnapshot CaptureRendererSnapshot(Renderer renderer)
        {
            Material[] sharedMaterials = renderer.sharedMaterials;
            if (sharedMaterials == null || sharedMaterials.Length == 0)
                return null;

            int materialCount = sharedMaterials.Length;
            var snapshot = new RendererColorSnapshot
            {
                Renderer = renderer,
                Block = new MaterialPropertyBlock(),
                OriginalColors = new Color[materialCount],
                HasBaseColor = new bool[materialCount],
                HasColor = new bool[materialCount]
            };

            for (int i = 0; i < materialCount; i++)
            {
                Material material = sharedMaterials[i];
                if (material == null)
                    continue;

                bool hasBase = material.HasProperty(BaseColorId);
                bool hasColor = material.HasProperty(ColorId);
                snapshot.HasBaseColor[i] = hasBase;
                snapshot.HasColor[i] = hasColor;
                if (hasBase)
                    snapshot.OriginalColors[i] = material.GetColor(BaseColorId);
                else if (hasColor)
                    snapshot.OriginalColors[i] = material.GetColor(ColorId);
                else
                    snapshot.OriginalColors[i] = Color.white;
            }

            return snapshot;
        }

        private void RestoreAllBuildingColors()
        {
            foreach (KeyValuePair<int, RendererColorSnapshot> pair in rendererColorSnapshots)
            {
                RendererColorSnapshot snapshot = pair.Value;
                if (snapshot == null || snapshot.Renderer == null)
                    continue;

                Material[] sharedMaterials = snapshot.Renderer.sharedMaterials;
                int materialCount = sharedMaterials != null ? sharedMaterials.Length : 0;
                if (snapshot.Block == null)
                    snapshot.Block = new MaterialPropertyBlock();

                snapshot.Renderer.GetPropertyBlock(snapshot.Block);
                for (int i = 0; i < materialCount && i < snapshot.OriginalColors.Length; i++)
                {
                    bool hasBase = i < snapshot.HasBaseColor.Length && snapshot.HasBaseColor[i];
                    bool hasColor = i < snapshot.HasColor.Length && snapshot.HasColor[i];
                    if (!hasBase && !hasColor)
                        continue;

                    if (hasBase)
                        snapshot.Block.SetColor(BaseColorId, snapshot.OriginalColors[i]);
                    if (hasColor)
                        snapshot.Block.SetColor(ColorId, snapshot.OriginalColors[i]);
                }

                snapshot.Renderer.SetPropertyBlock(snapshot.Block);
            }
        }
    }
}
