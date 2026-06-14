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
        [SerializeField] private bool enableUnitLifecycleLogs;

        private Dictionary<int, MinimapUnitData> units = new Dictionary<int, MinimapUnitData>();
        private int nextUnitId = 1;
        private int syncedLevelEntityId;

        public MinimapConfig Config => config;
        public Color WaterLayerColor => waterLayerColor;
        public Color PlaneLayerColor => planeLayerColor;
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
                    List<MinimapUnitData> unitsList = new List<MinimapUnitData>(units.Values);
                    OnUnitsUpdated.Invoke(unitsList);
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

            var twcConfig = tileWorldCreatorManager.configuration;
            float cellSize = Mathf.Max(0.01f, twcConfig.cellSize);
            float worldWidth = Mathf.Max(1f, twcConfig.width * cellSize);
            float worldHeight = Mathf.Max(1f, twcConfig.height * cellSize);
            Vector3 origin = tileWorldCreatorManager.transform.position;

            config.WorldMinX = origin.x;
            config.WorldMaxX = origin.x + worldWidth;
            config.WorldMinZ = origin.z;
            config.WorldMaxZ = origin.z + worldHeight;

            syncedLevelEntityId = levelId;
            if (enableUnitLifecycleLogs)
            {
                Log.Info("[MinimapManager] Synced bounds from TileWorldCreator: width={0:F2}, height={1:F2}, origin={2}", worldWidth, worldHeight, origin);
            }
        }
    }
}
