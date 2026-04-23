using UnityEngine;
using System.Collections.Generic;

namespace AAAGame.Card
{
    /// <summary>
    /// 区域检测控制器
    /// 负责检测可放置区域和禁止区域
    /// </summary>
    public class AreaDetectionController
    {
        private LayerMask m_GroundLayer;
        private LayerMask m_ForbiddenLayer;
        
        // 区域对象引用
        private GameObject m_ValidAreaObject;
        private GameObject m_InvalidAreaObject;
        
        // 区域检测缓存
        private Dictionary<GameObject, Collider[]> m_AreaColliderCache;
        
        public AreaDetectionController()
        {
            m_GroundLayer = LayerMask.GetMask("Ground");
            m_ForbiddenLayer = LayerMask.GetMask("ForbiddenArea");
            m_AreaColliderCache = new Dictionary<GameObject, Collider[]>();
        }

        /// <summary>
        /// 设置区域对象
        /// </summary>
        public void SetAreaObjects(GameObject validAreaObject, GameObject invalidAreaObject)
        {
            m_ValidAreaObject = validAreaObject;
            m_InvalidAreaObject = invalidAreaObject;
            
            // 缓存 Collider
            CacheAreaColliders(m_ValidAreaObject);
            CacheAreaColliders(m_InvalidAreaObject);
        }

        /// <summary>
        /// 缓存区域的 Collider
        /// </summary>
        private void CacheAreaColliders(GameObject areaObject)
        {
            if (areaObject == null) return;
            
            Collider[] colliders = areaObject.GetComponentsInChildren<Collider>();
            m_AreaColliderCache[areaObject] = colliders;
            
            if (colliders.Length == 0)
            {
                Debug.LogWarning($"[Card] Area object {areaObject.name} has no colliders.");
            }
        }

        /// <summary>
        /// 检查位置是否在指定区域内
        /// </summary>
        public bool IsPositionInArea(Vector3 worldPosition, GameObject areaObject)
        {
            if (areaObject == null) return false;
            
            // 从缓存获取 Collider
            if (!m_AreaColliderCache.TryGetValue(areaObject, out Collider[] colliders))
            {
                CacheAreaColliders(areaObject);
                if (!m_AreaColliderCache.TryGetValue(areaObject, out colliders))
                {
                    return false;
                }
            }
            
            if (colliders.Length == 0) return false;
            
            // 检测点是否在任一 Collider 内
            foreach (var collider in colliders)
            {
                if (collider == null) continue;
                
                Vector3 closestPoint = collider.ClosestPoint(worldPosition);
                float distance = Vector3.Distance(worldPosition, closestPoint);
                
                // 距离小于阈值，说明在 Collider 内部或表面
                if (distance < 0.01f)
                {
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// 检查位置是否在可放置区域
        /// </summary>
        public bool IsPositionInValidArea(Vector3 worldPosition)
        {
            return IsPositionInArea(worldPosition, m_ValidAreaObject);
        }

        /// <summary>
        /// 检查位置是否在禁止区域
        /// </summary>
        public bool IsPositionInInvalidArea(Vector3 worldPosition)
        {
            return IsPositionInArea(worldPosition, m_InvalidAreaObject);
        }

        /// <summary>
        /// 检查一个带半径的放置位置是否与禁止区域发生重叠。
        /// </summary>
        public bool IsPositionBlockedByInvalidArea(Vector3 worldPosition, float radius)
        {
            if (IsPositionInInvalidArea(worldPosition))
            {
                return true;
            }

            float clampedRadius = Mathf.Max(0f, radius);
            if (clampedRadius <= 0.01f)
            {
                return false;
            }

            return SampleInvalidArea(worldPosition, clampedRadius * 0.5f, 8)
                || SampleInvalidArea(worldPosition, clampedRadius, 12);
        }

        private bool SampleInvalidArea(Vector3 center, float radius, int sampleCount)
        {
            if (radius <= 0.01f)
            {
                return false;
            }

            for (int i = 0; i < sampleCount; i++)
            {
                float angle = Mathf.PI * 2f * i / sampleCount;
                Vector3 samplePoint = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (IsPositionInInvalidArea(samplePoint))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 判断位置的区域类型
        /// </summary>
        public AreaType DetermineAreaType(Vector3 worldPosition)
        {
            // 优先检测禁止区域
            if (IsPositionInInvalidArea(worldPosition))
            {
                return AreaType.Invalid;
            }
            
            // 检测可放置区域
            if (IsPositionInValidArea(worldPosition))
            {
                return AreaType.Valid;
            }
            
            // 其他区域
            return AreaType.None;
        }

        /// <summary>
        /// 使用射线检测区域
        /// </summary>
        public bool RaycastArea(Vector3 worldPosition, float radius, out AreaType areaType)
        {
            areaType = AreaType.None;
            
            // 检测禁止区域
            Collider[] forbiddenColliders = Physics.OverlapSphere(
                worldPosition, radius, m_ForbiddenLayer);
            
            if (forbiddenColliders.Length > 0)
            {
                areaType = AreaType.Invalid;
                return true;
            }
            
            // 检测地面区域
            Collider[] groundColliders = Physics.OverlapSphere(
                worldPosition, radius, m_GroundLayer);
            
            if (groundColliders.Length > 0)
            {
                areaType = AreaType.Valid;
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// 清除缓存
        /// </summary>
        public void ClearCache()
        {
            m_AreaColliderCache.Clear();
        }

        /// <summary>
        /// 清理
        /// </summary>
        public void Shutdown()
        {
            ClearCache();
            m_ValidAreaObject = null;
            m_InvalidAreaObject = null;
        }
    }

    /// <summary>
    /// 区域类型
    /// </summary>
    public enum AreaType
    {
        None,       // 无区域
        Valid,      // 可放置区域
        Invalid     // 禁止区域
    }
}
