using UnityEngine;
using System.Collections.Generic;

namespace AAAGame.MiniMap
{
    /// <summary>
    /// 小地图单位类型
    /// </summary>
    public enum MinimapUnitType
    {
        Soldier,    // 士兵 - 显示为正方形小点
        Building,   // 建筑 - 显示为自定义图标
        Objective,  // 行动目标 - 始终显示为绿色标志
        LevelTarget // 关卡条件目标 - 显示为红色目标标点
    }

    /// <summary>
    /// 小地图单位数据
    /// 表示一个单位在小地图上的信息
    /// </summary>
    public struct MinimapUnitData
    {
        public const string TargetLocationIconName = "TargetLocation";

        /// <summary>
        /// 单位唯一ID
        /// </summary>
        public int UnitId;

        /// <summary>
        /// 世界坐标位置
        /// </summary>
        public Vector3 WorldPosition;

        /// <summary>
        /// 势力类型
        /// </summary>
        public SideType Side;

        /// <summary>
        /// 单位类型（士兵/建筑）
        /// </summary>
        public MinimapUnitType UnitType;

        /// <summary>
        /// 建筑图标预制体名称（仅建筑使用）
        /// </summary>
        public string IconPrefabName;

        /// <summary>
        /// 是否可见（用于战争迷雾）
        /// </summary>
        public bool IsVisible;

        public MinimapUnitData(int unitId, Vector3 worldPosition, SideType side, MinimapUnitType unitType, string iconPrefabName = null, bool isVisible = true)
        {
            UnitId = unitId;
            WorldPosition = worldPosition;
            Side = side;
            UnitType = unitType;
            IconPrefabName = iconPrefabName;
            IsVisible = isVisible;
        }
    }

    /// <summary>
    /// 小地图配置
    /// </summary>
    [System.Serializable]
    public class MinimapConfig
    {
        [Header("地图范围")]
        [Tooltip("世界坐标 X 轴最小值")]
        public float WorldMinX = -50f;

        [Tooltip("世界坐标 X 轴最大值")]
        public float WorldMaxX = 50f;

        [Tooltip("世界坐标 Z 轴最小值")]
        public float WorldMinZ = -50f;

        [Tooltip("世界坐标 Z 轴最大值")]
        public float WorldMaxZ = 50f;

        [Header("士兵显示（正方形小点）")]
        [Tooltip("玩家士兵颜色")]
        public Color PlayerSoldierColor = new Color(0.15f, 0.9f, 0.15f); // 绿色

        [Tooltip("敌方士兵颜色")]
        public Color EnemySoldierColor = Color.red;

        [Tooltip("士兵点大小")]
        public float SoldierDotSize = 5f;

        [Header("建筑显示（自定义图标）")]
        [Tooltip("建筑图标大小")]
        public float BuildingIconSize = 15f;

        [Header("战争迷雾（进阶功能）")]
        [Tooltip("是否启用战争迷雾")]
        public bool EnableFogOfWar = false;

        [Tooltip("迷雾格子大小（N×N）")]
        public int FogGridSize = 50;

        [Tooltip("每个友方单位的视野半径（格子数）")]
        public float VisionRadius = 5f;

        /// <summary>
        /// 获取士兵颜色
        /// </summary>
        public Color GetSoldierColor(SideType side)
        {
            switch (side)
            {
                case SideType.PlayerSide:
                    return PlayerSoldierColor;
                case SideType.EnemySide:
                    return EnemySoldierColor;
                default:
                    return Color.gray;
            }
        }
    }
}
