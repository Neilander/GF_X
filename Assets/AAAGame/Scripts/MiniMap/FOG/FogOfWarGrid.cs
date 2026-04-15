using UnityEngine;
using System.Collections.Generic;

namespace AAAGame.MiniMap.FOG
{
    /// <summary>
    /// 战争迷雾网格
    /// 存储每个格子的可见性信息
    /// </summary>
    public class FogOfWarGrid
    {
        private int width;
        private int height;

        // 当前可见性（每帧清空并重新计算）
        private int[] visibleMask;

        // 历史探索记录（永久保存）
        private int[] exploredMask;

        // 地形高度数据
        private short[] terrainHeight;

        public int Width => width;
        public int Height => height;

        public FogOfWarGrid(int width, int height)
        {
            this.width = width;
            this.height = height;

            int size = width * height;
            visibleMask = new int[size];
            exploredMask = new int[size];
            terrainHeight = new short[size];
        }

        /// <summary>
        /// 清空当前可见性（每次更新前调用）
        /// </summary>
        public void ClearVisible()
        {
            System.Array.Clear(visibleMask, 0, visibleMask.Length);
        }

        /// <summary>
        /// 设置格子可见
        /// </summary>
        public void SetVisible(int x, int z, int playerMask)
        {
            if (!IsValidCoord(x, z)) return;

            int index = GetIndex(x, z);
            visibleMask[index] |= playerMask;
            exploredMask[index] |= playerMask;
        }

        /// <summary>
        /// 检查格子是否对指定玩家可见
        /// </summary>
        public bool IsVisible(int x, int z, int playerMask)
        {
            if (!IsValidCoord(x, z)) return false;

            int index = GetIndex(x, z);
            return (visibleMask[index] & playerMask) != 0;
        }

        /// <summary>
        /// 检查格子是否被指定玩家探索过
        /// </summary>
        public bool WasExplored(int x, int z, int playerMask)
        {
            if (!IsValidCoord(x, z)) return false;

            int index = GetIndex(x, z);
            return (exploredMask[index] & playerMask) != 0;
        }

        /// <summary>
        /// 获取格子的迷雾状态
        /// </summary>
        public FogState GetFogState(int x, int z, int playerMask)
        {
            if (!IsValidCoord(x, z)) return FogState.Hidden;

            if (IsVisible(x, z, playerMask))
                return FogState.Visible;

            if (WasExplored(x, z, playerMask))
                return FogState.Explored;

            return FogState.Hidden;
        }

        /// <summary>
        /// 设置地形高度
        /// </summary>
        public void SetTerrainHeight(int x, int z, short height)
        {
            if (!IsValidCoord(x, z)) return;

            int index = GetIndex(x, z);
            terrainHeight[index] = height;
        }

        /// <summary>
        /// 获取地形高度
        /// </summary>
        public short GetTerrainHeight(int x, int z)
        {
            if (!IsValidCoord(x, z)) return 0;

            int index = GetIndex(x, z);
            return terrainHeight[index];
        }

        /// <summary>
        /// 检查坐标是否有效
        /// </summary>
        public bool IsValidCoord(int x, int z)
        {
            return x >= 0 && x < width && z >= 0 && z < height;
        }

        /// <summary>
        /// 获取数组索引
        /// </summary>
        private int GetIndex(int x, int z)
        {
            return x + z * width;
        }

        /// <summary>
        /// 重置所有数据
        /// </summary>
        public void Reset()
        {
            System.Array.Clear(visibleMask, 0, visibleMask.Length);
            System.Array.Clear(exploredMask, 0, exploredMask.Length);
            System.Array.Clear(terrainHeight, 0, terrainHeight.Length);
        }

        /// <summary>
        /// 获取指定范围内的所有格子
        /// </summary>
        public List<Vector2Int> GetCellsInRange(int centerX, int centerZ, int range)
        {
            List<Vector2Int> cells = new List<Vector2Int>();

            // 使用圆形范围
            int rangeSquared = range * range;

            for (int x = centerX - range; x <= centerX + range; x++)
            {
                for (int z = centerZ - range; z <= centerZ + range; z++)
                {
                    if (!IsValidCoord(x, z)) continue;

                    int dx = x - centerX;
                    int dz = z - centerZ;
                    int distSquared = dx * dx + dz * dz;

                    if (distSquared <= rangeSquared)
                    {
                        cells.Add(new Vector2Int(x, z));
                    }
                }
            }

            return cells;
        }
    }
}
