using UnityEngine;
using UnityGameFramework.Runtime;

namespace AAAGame.MiniMap.FOG
{
    /// <summary>
    /// 战争迷雾视野组件
    /// 挂在需要提供视野的单位身上（士兵、建筑、侦察塔等）
    /// </summary>
    public class FogOfWarVisionComponent : MonoBehaviour
    {
        [Header("视野设置")]
        [SerializeField] private int playerMask = 1; // 默认玩家0
        [SerializeField] private float visionRange = 10f;
        [SerializeField] private bool autoRegister = true;

        [Header("地形高度")]
        [SerializeField] private bool useTerrainHeight = true;
        [SerializeField] private float heightOffset = 0f;

        private FogOfWarManager fogManager;
        private int visionId = -1;
        private bool isRegistered = false;

        private void Start()
        {
            if (autoRegister)
            {
                RegisterVision();
            }
        }

        private void OnDestroy()
        {
            UnregisterVision();
        }

        /// <summary>
        /// 注册视野
        /// </summary>
        public void RegisterVision()
        {
            if (isRegistered) return;

            // 从场景中查找 FogOfWarManager
            fogManager = FindObjectOfType<FogOfWarManager>();
            if (fogManager == null)
            {
                Log.Error("[FogVision] FogOfWarManager not found in scene! Please add a GameObject named 'FogOfWarManager' with FogOfWarManager component.");
                return;
            }

            short terrainHeight = GetTerrainHeight();
            visionId = fogManager.RegisterVision(playerMask, visionRange, transform.position, terrainHeight);
            isRegistered = true;

            Log.Info($"[FogVision] Vision registered: ID={visionId}, Player={playerMask}, Range={visionRange}");
        }

        /// <summary>
        /// 注销视野
        /// </summary>
        public void UnregisterVision()
        {
            if (!isRegistered || fogManager == null) return;

            fogManager.UnregisterVision(visionId);
            isRegistered = false;
            visionId = -1;

            Log.Info($"[FogVision] Vision unregistered");
        }

        /// <summary>
        /// Tick 更新（由 Entity 或 Update 调用）
        /// </summary>
        public void Tick()
        {
            if (!isRegistered || fogManager == null) return;

            short terrainHeight = GetTerrainHeight();
            fogManager.UpdateVision(visionId, transform.position, terrainHeight);
        }

        private void LateUpdate()
        {
            // 自动更新位置
            if (isRegistered)
            {
                Tick();
            }
        }

        /// <summary>
        /// 获取地形高度
        /// </summary>
        private short GetTerrainHeight()
        {
            if (!useTerrainHeight)
                return 0;

            // 从 Unity Terrain 获取高度
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                TerrainData terrainData = terrain.terrainData;
                Vector3 terrainPos = terrain.transform.position;
                Vector3 localPos = transform.position - terrainPos;

                float normalizedX = Mathf.Clamp01(localPos.x / terrainData.size.x);
                float normalizedZ = Mathf.Clamp01(localPos.z / terrainData.size.z);

                float height = terrainData.GetInterpolatedHeight(normalizedX, normalizedZ);
                return (short)Mathf.RoundToInt(height + heightOffset);
            }

            // 使用世界坐标 Y 值
            return (short)Mathf.RoundToInt(transform.position.y + heightOffset);
        }

        /// <summary>
        /// 设置玩家掩码
        /// </summary>
        public void SetPlayerMask(int mask)
        {
            playerMask = mask;

            if (isRegistered)
            {
                // 重新注册
                UnregisterVision();
                RegisterVision();
            }
        }

        /// <summary>
        /// 设置视野范围
        /// </summary>
        public void SetVisionRange(float range)
        {
            visionRange = range;
        }

        /// <summary>
        /// 获取视野范围
        /// </summary>
        public float GetVisionRange()
        {
            return visionRange;
        }

        /// <summary>
        /// 检查是否已注册
        /// </summary>
        public bool IsRegistered()
        {
            return isRegistered;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 绘制视野范围
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, visionRange);
        }
#endif
    }
}
