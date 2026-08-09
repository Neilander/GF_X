using GameFramework;
using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace AAAGame.Effect
{
    public sealed class BuildingOwnershipColorController : GameFrameworkComponent
    {
        private EffectRuntimeConfigComponent config;
        private bool isSubscribed;
        private bool isInitialized;

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
            building.SetOwnershipVisualColor(isEnemy, config.EnemyBuildingColor);
        }

        private static bool IsEnemyBuilding(BuildingEntity building)
        {
            if (building == null)
                return false;

            return ((IBuildingLogicContext)building).OwnerFactionId != EntitySideHelper.PlayerFactionId;
        }

        private void RestoreAllBuildingColors()
        {
            BuildingEntity[] buildings = FindObjectsOfType<BuildingEntity>(true);
            for (int i = 0; i < buildings.Length; i++)
                buildings[i].SetOwnershipVisualColor(false, default);
        }
    }
}
