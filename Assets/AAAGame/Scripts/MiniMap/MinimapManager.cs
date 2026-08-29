using System;
using System.Collections.Generic;
using UnityEngine;
using GiantGrey.TileWorldCreator;
using UnityGameFramework.Runtime;

namespace AAAGame.MiniMap
{
    /// <summary>
    /// 小地图管理器
    /// 继承 GameFrameworkComponent，作为全局单例管理小地图数据
    /// 使用 C# 委托事件系统，数据与表现分离
    /// </summary>
    public class MinimapManager : GameFrameworkComponent
    {
        [SerializeField] private MinimapConfig config = new MinimapConfig();
        [SerializeField] private Color waterLayerColor = new Color(0.14f, 0.36f, 0.52f, 1f);
        [SerializeField] private Color planeLayerColor = new Color(0.42f, 0.45f, 0.33f, 1f);
        [SerializeField, Min(0)] private int terrainPaddingCells = 8;
        [SerializeField] private bool enableUnitLifecycleLogs;

        private Dictionary<int, MinimapUnitData> units = new Dictionary<int, MinimapUnitData>();
        private readonly List<MinimapUnitData> unitSnapshot = new List<MinimapUnitData>();
        private int nextUnitId = 1;
        private int syncedLevelEntityId;

        public MinimapConfig Config => config;
        public Color WaterLayerColor => waterLayerColor;
        public Color PlaneLayerColor => planeLayerColor;
        public int TerrainPaddingCells => terrainPaddingCells;
        public bool EnableUnitLifecycleLogs => enableUnitLifecycleLogs;

        // C# 委托事件 - 数据变化时触发
        public event Action<List<MinimapUnitData>> OnUnitsUpdated;

        protected override void Awake()
        {
            base.Awake();
        }

        private void LateUpdate()
        {
            TrySyncBoundsFromLevelEntity();

            // 每帧结束时广播单位数据
            if (units.Count > 0)
            {
                if (OnUnitsUpdated != null)
                {
                    unitSnapshot.Clear();
                    unitSnapshot.AddRange(units.Values);
                    OnUnitsUpdated.Invoke(unitSnapshot);
                }
            }
        }

        /// <summary>
        /// 注册单位到小地图
        /// </summary>
        public int RegisterUnit(Vector3 worldPosition, SideType side, MinimapUnitType unitType, string iconPrefabName = null)
        {
            int unitId = nextUnitId++;
            var unitData = new MinimapUnitData(unitId, worldPosition, side, unitType, iconPrefabName, true);
            units[unitId] = unitData;
            if (enableUnitLifecycleLogs)
            {
                Log.Info("[MinimapManager] Unit registered: ID={0}, Side={1}, Type={2}, Total units={3}", unitId, side, unitType, units.Count);
            }
            return unitId;
        }

        /// <summary>
        /// 更新单位位置和势力
        /// </summary>
        public void UpdateUnit(int unitId, Vector3 worldPosition, SideType side)
        {
            if (units.ContainsKey(unitId))
            {
                var unitData = units[unitId];
                unitData.WorldPosition = worldPosition;
                unitData.Side = side;
                units[unitId] = unitData;
            }
        }

        /// <summary>
        /// 注销单位
        /// </summary>
        public void UnregisterUnit(int unitId)
        {
            if (units.Remove(unitId) && enableUnitLifecycleLogs)
            {
                Log.Info("[MinimapManager] Unit unregistered: ID={0}, Remaining units={1}", unitId, units.Count);
            }
        }

        /// <summary>
        /// 获取单位数量
        /// </summary>
        public int GetUnitCount()
        {
            return units.Count;
        }

        private void TrySyncBoundsFromLevelEntity()
        {
            LevelEntity levelEntity = LevelEntity.ActiveLevelEntity;
            if (levelEntity == null)
            {
                return;
            }

            int levelId = levelEntity.GetInstanceID();
            if (syncedLevelEntityId == levelId)
            {
                return;
            }

            TileWorldCreatorManager tileWorldCreatorManager = levelEntity.GetComponentInChildren<TileWorldCreatorManager>();
            if (tileWorldCreatorManager == null || tileWorldCreatorManager.configuration == null)
            {
                return;
            }

            MinimapTerrainBounds bounds = MinimapTerrainMapBuilder.ResolveBounds(
                tileWorldCreatorManager,
                terrainPaddingCells);
            ApplyTerrainBounds(bounds);

            syncedLevelEntityId = levelId;
            if (enableUnitLifecycleLogs)
            {
                Log.Info(
                    "[MinimapManager] Synced terrain bounds: min=({0:F2},{1:F2}), max=({2:F2},{3:F2}), environmentBackground={4}",
                    bounds.WorldMinX,
                    bounds.WorldMinZ,
                    bounds.WorldMaxX,
                    bounds.WorldMaxZ,
                    bounds.HasEnvironmentBackground);
            }
        }

        public void ApplyTerrainBounds(MinimapTerrainBounds bounds)
        {
            if (bounds == null)
                throw new ArgumentNullException(nameof(bounds));

            config.WorldMinX = bounds.WorldMinX;
            config.WorldMaxX = bounds.WorldMaxX;
            config.WorldMinZ = bounds.WorldMinZ;
            config.WorldMaxZ = bounds.WorldMaxZ;

            LevelEntity levelEntity = LevelEntity.ActiveLevelEntity;
            if (levelEntity != null)
                syncedLevelEntityId = levelEntity.GetInstanceID();
        }
    }
}
