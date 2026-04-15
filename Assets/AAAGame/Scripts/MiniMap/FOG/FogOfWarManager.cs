using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace AAAGame.MiniMap.FOG
{
    /// <summary>
    /// 战争迷雾管理器
    /// 继承 GameFrameworkComponent，作为全局单例
    /// 负责计算和管理战争迷雾
    /// </summary>
    public class FogOfWarManager : GameFrameworkComponent
    {
        [SerializeField] private FogOfWarConfig config = new FogOfWarConfig();

        private FogOfWarGrid fogGrid;
        private Dictionary<int, UnitVision> unitVisions = new Dictionary<int, UnitVision>();
        private int nextUnitId = 1;
        private float updateTimer = 0f;

        // 当前激活的玩家掩码（用于渲染）
        private int activePlayerMask = 1; // 默认玩家0

        public FogOfWarConfig Config => config;
        public FogOfWarGrid Grid => fogGrid;
        public int ActivePlayerMask => activePlayerMask;

        // 事件：视野更新完成
        public event System.Action OnVisionUpdated;

        protected override void Awake()
        {
            base.Awake();

            // 初始化网格
            fogGrid = new FogOfWarGrid(config.GridWidth, config.GridHeight);

            // 初始化地形高度（可选）
            InitializeTerrainHeight();

            Log.Info($"[FogOfWar] Manager initialized with grid size {config.GridWidth}x{config.GridHeight}");
        }

        private void Update()
        {
            // 定时更新视野
            updateTimer += Time.deltaTime;

            if (config.UpdateInterval <= 0 || updateTimer >= config.UpdateInterval)
            {
                updateTimer = 0f;
                UpdateVision();
            }
        }

        /// <summary>
        /// 注册单位视野
        /// </summary>
        /// <param name="playerMask">玩家掩码（例如：1=玩家0, 2=玩家1, 3=玩家0和1联盟）</param>
        /// <param name="visionRange">视野范围（世界坐标）</param>
        /// <param name="worldPosition">世界坐标位置</param>
        /// <param name="terrainHeight">地形高度</param>
        /// <returns>单位视野ID</returns>
        public int RegisterVision(int playerMask, float visionRange, Vector3 worldPosition, short terrainHeight = 0)
        {
            int unitId = nextUnitId++;
            var vision = new UnitVision(unitId, playerMask, visionRange, worldPosition, terrainHeight);
            unitVisions[unitId] = vision;

            Log.Info($"[FogOfWar] Vision registered: ID={unitId}, Player={playerMask}, Range={visionRange}");
            return unitId;
        }

        /// <summary>
        /// 更新单位视野
        /// </summary>
        public void UpdateVision(int unitId, Vector3 worldPosition, short terrainHeight = 0)
        {
            if (unitVisions.ContainsKey(unitId))
            {
                var vision = unitVisions[unitId];
                vision.WorldPosition = worldPosition;
                vision.TerrainHeight = terrainHeight;
                unitVisions[unitId] = vision;
            }
        }

        /// <summary>
        /// 注销单位视野
        /// </summary>
        public void UnregisterVision(int unitId)
        {
            if (unitVisions.Remove(unitId))
            {
                Log.Info($"[FogOfWar] Vision unregistered: ID={unitId}");
            }
        }

        /// <summary>
        /// 设置激活的玩家掩码（用于渲染）
        /// </summary>
        public void SetActivePlayerMask(int playerMask)
        {
            activePlayerMask = playerMask;
            Log.Info($"[FogOfWar] Active player mask set to: {playerMask}");
        }

        /// <summary>
        /// 更新视野计算
        /// </summary>
        private void UpdateVision()
        {
            // 清空当前可见性
            fogGrid.ClearVisible();

            // 遍历所有单位视野
            foreach (var vision in unitVisions.Values)
            {
                if (!vision.IsActive) continue;

                CalculateVisionForUnit(vision);
            }

            // 触发更新事件
            OnVisionUpdated?.Invoke();
        }

        /// <summary>
        /// 计算单个单位的视野
        /// </summary>
        private void CalculateVisionForUnit(UnitVision vision)
        {
            // 转换为网格坐标
            if (!config.WorldToGrid(vision.WorldPosition, out int centerX, out int centerZ))
                return;

            // 计算网格范围
            float cellSizeX = config.GetCellSizeX();
            float cellSizeZ = config.GetCellSizeZ();
            int gridRange = Mathf.CeilToInt(vision.VisionRange / Mathf.Max(cellSizeX, cellSizeZ));

            // 获取范围内的所有格子
            var cells = fogGrid.GetCellsInRange(centerX, centerZ, gridRange);

            // 检查每个格子是否可见
            foreach (var cell in cells)
            {
                // 检查是否被地形遮挡
                if (config.EnableTerrainBlocking && IsVisionBlocked(centerX, centerZ, cell.x, cell.y, vision.TerrainHeight))
                {
                    continue;
                }

                // 设置为可见
                fogGrid.SetVisible(cell.x, cell.y, vision.PlayerMask);
            }
        }

        /// <summary>
        /// 检查视野是否被遮挡（使用 Bresenham 直线算法）
        /// </summary>
        private bool IsVisionBlocked(int x0, int z0, int x1, int z1, short visionHeight)
        {
            // 使用 Bresenham 算法绘制直线
            int dx = Mathf.Abs(x1 - x0);
            int dz = Mathf.Abs(z1 - z0);
            int sx = x0 < x1 ? 1 : -1;
            int sz = z0 < z1 ? 1 : -1;
            int err = dx - dz;

            int x = x0;
            int z = z0;

            while (true)
            {
                // 检查当前格子的高度
                short cellHeight = fogGrid.GetTerrainHeight(x, z);
                if (cellHeight > visionHeight + config.HeightBlockThreshold)
                {
                    return true; // 被遮挡
                }

                // 到达目标点
                if (x == x1 && z == z1)
                    break;

                int e2 = 2 * err;
                if (e2 > -dz)
                {
                    err -= dz;
                    x += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    z += sz;
                }
            }

            return false; // 未被遮挡
        }

        /// <summary>
        /// 初始化地形高度（从 Unity Terrain 或自定义数据）
        /// </summary>
        private void InitializeTerrainHeight()
        {
            // 尝试从场景中获取 Terrain
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null)
            {
                Log.Warning("[FogOfWar] No active terrain found, using flat terrain (height=0)");
                return;
            }

            TerrainData terrainData = terrain.terrainData;
            Vector3 terrainPos = terrain.transform.position;

            // 遍历网格，采样地形高度
            for (int x = 0; x < config.GridWidth; x++)
            {
                for (int z = 0; z < config.GridHeight; z++)
                {
                    Vector3 worldPos = config.GridToWorld(x, z);

                    // 转换为 Terrain 本地坐标
                    Vector3 localPos = worldPos - terrainPos;
                    float normalizedX = Mathf.Clamp01(localPos.x / terrainData.size.x);
                    float normalizedZ = Mathf.Clamp01(localPos.z / terrainData.size.z);

                    // 采样高度
                    float height = terrainData.GetInterpolatedHeight(normalizedX, normalizedZ);
                    short heightShort = (short)Mathf.RoundToInt(height);

                    fogGrid.SetTerrainHeight(x, z, heightShort);
                }
            }

            Log.Info("[FogOfWar] Terrain height initialized from Unity Terrain");
        }

        /// <summary>
        /// 检查世界坐标位置是否对指定玩家可见
        /// </summary>
        public bool IsPositionVisible(Vector3 worldPosition, int playerMask)
        {
            if (!config.WorldToGrid(worldPosition, out int x, out int z))
                return false;

            return fogGrid.IsVisible(x, z, playerMask);
        }

        /// <summary>
        /// 检查世界坐标位置是否被指定玩家探索过
        /// </summary>
        public bool WasPositionExplored(Vector3 worldPosition, int playerMask)
        {
            if (!config.WorldToGrid(worldPosition, out int x, out int z))
                return false;

            return fogGrid.WasExplored(x, z, playerMask);
        }

        /// <summary>
        /// 获取世界坐标位置的迷雾状态
        /// </summary>
        public FogState GetFogState(Vector3 worldPosition, int playerMask)
        {
            if (!config.WorldToGrid(worldPosition, out int x, out int z))
                return FogState.Hidden;

            return fogGrid.GetFogState(x, z, playerMask);
        }

        /// <summary>
        /// 强制立即更新视野
        /// </summary>
        public void ForceUpdateVision()
        {
            UpdateVision();
        }

        /// <summary>
        /// 重置所有迷雾数据
        /// </summary>
        public void ResetFog()
        {
            fogGrid.Reset();
            Log.Info("[FogOfWar] Fog data reset");
        }

        /// <summary>
        /// 获取单位视野数量
        /// </summary>
        public int GetVisionCount()
        {
            return unitVisions.Count;
        }
    }
}
