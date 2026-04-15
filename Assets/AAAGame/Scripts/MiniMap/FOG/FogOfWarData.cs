using UnityEngine;

namespace AAAGame.MiniMap.FOG
{
    /// <summary>
    /// 战争迷雾状态
    /// </summary>
    public enum FogState
    {
        Hidden = 0,      // 完全未探索（黑色）
        Explored = 1,    // 已探索但不可见（灰色）
        Visible = 2      // 当前可见（透明）
    }

    /// <summary>
    /// 单位视野数据
    /// 表示一个可以揭示战争迷雾的单位
    /// </summary>
    public struct UnitVision
    {
        /// <summary>
        /// 单位唯一ID
        /// </summary>
        public int UnitId;

        /// <summary>
        /// 玩家位掩码（支持多玩家联盟）
        /// 例如：玩家0=0001, 玩家1=0010, 联盟=0011
        /// </summary>
        public int PlayerMask;

        /// <summary>
        /// 视野范围（世界坐标）
        /// </summary>
        public float VisionRange;

        /// <summary>
        /// 世界坐标位置
        /// </summary>
        public Vector3 WorldPosition;

        /// <summary>
        /// 地形高度（用于视野遮挡）
        /// </summary>
        public short TerrainHeight;

        /// <summary>
        /// 是否激活
        /// </summary>
        public bool IsActive;

        public UnitVision(int unitId, int playerMask, float visionRange, Vector3 worldPosition, short terrainHeight = 0)
        {
            UnitId = unitId;
            PlayerMask = playerMask;
            VisionRange = visionRange;
            WorldPosition = worldPosition;
            TerrainHeight = terrainHeight;
            IsActive = true;
        }
    }

    /// <summary>
    /// 战争迷雾网格配置
    /// </summary>
    [System.Serializable]
    public class FogOfWarConfig
    {
        [Header("网格范围")]
        [Tooltip("世界坐标 X 轴最小值")]
        public float WorldMinX = -50f;

        [Tooltip("世界坐标 X 轴最大值")]
        public float WorldMaxX = 50f;

        [Tooltip("世界坐标 Z 轴最小值")]
        public float WorldMinZ = -50f;

        [Tooltip("世界坐标 Z 轴最大值")]
        public float WorldMaxZ = 50f;

        [Header("网格设置")]
        [Tooltip("网格宽度（格子数）")]
        public int GridWidth = 100;

        [Tooltip("网格高度（格子数）")]
        public int GridHeight = 100;

        [Header("更新设置")]
        [Tooltip("视野更新间隔（秒）- 0表示每帧更新")]
        public float UpdateInterval = 0.1f;

        [Header("渲染设置")]
        [Tooltip("是否启用模糊效果")]
        public bool EnableBlur = true;

        [Tooltip("模糊强度")]
        [Range(0, 5)]
        public int BlurIterations = 2;

        [Header("颜色设置")]
        [Tooltip("完全未探索区域颜色（黑色）")]
        public Color HiddenColor = Color.black;

        [Tooltip("已探索但不可见区域颜色（灰色）")]
        public Color ExploredColor = new Color(0.3f, 0.3f, 0.3f, 0.8f);

        [Tooltip("可见区域颜色（透明）")]
        public Color VisibleColor = new Color(1f, 1f, 1f, 0f);

        [Header("过渡设置")]
        [Tooltip("是否启用颜色过渡")]
        public bool EnableEasing = true;

        [Tooltip("颜色过渡速度")]
        [Range(0.1f, 10f)]
        public float EasingSpeed = 5f;

        [Header("地形遮挡")]
        [Tooltip("是否启用地形高度遮挡")]
        public bool EnableTerrainBlocking = true;

        [Tooltip("高度差阈值（超过此值会遮挡视野）")]
        public float HeightBlockThreshold = 2f;

        /// <summary>
        /// 获取格子大小（世界坐标）
        /// </summary>
        public float GetCellSizeX()
        {
            return (WorldMaxX - WorldMinX) / GridWidth;
        }

        public float GetCellSizeZ()
        {
            return (WorldMaxZ - WorldMinZ) / GridHeight;
        }

        /// <summary>
        /// 世界坐标转网格坐标
        /// </summary>
        public bool WorldToGrid(Vector3 worldPos, out int gridX, out int gridZ)
        {
            float normalizedX = Mathf.InverseLerp(WorldMinX, WorldMaxX, worldPos.x);
            float normalizedZ = Mathf.InverseLerp(WorldMinZ, WorldMaxZ, worldPos.z);

            gridX = Mathf.FloorToInt(normalizedX * GridWidth);
            gridZ = Mathf.FloorToInt(normalizedZ * GridHeight);

            return gridX >= 0 && gridX < GridWidth && gridZ >= 0 && gridZ < GridHeight;
        }

        /// <summary>
        /// 网格坐标转世界坐标（中心点）
        /// </summary>
        public Vector3 GridToWorld(int gridX, int gridZ)
        {
            float cellSizeX = GetCellSizeX();
            float cellSizeZ = GetCellSizeZ();

            float worldX = WorldMinX + (gridX + 0.5f) * cellSizeX;
            float worldZ = WorldMinZ + (gridZ + 0.5f) * cellSizeZ;

            return new Vector3(worldX, 0, worldZ);
        }
    }
}
