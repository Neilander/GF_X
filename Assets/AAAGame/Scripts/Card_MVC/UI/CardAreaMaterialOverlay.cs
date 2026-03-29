using UnityEngine;
using System.Collections.Generic;

namespace AAAGame.Card.UI
{
    /// <summary>
    /// 卡牌区域材质叠加器 - 在地面对象上动态添加/移除材质效果
    /// 支持三种区域：可放置区域（绿色）、禁止区域（红色）、其他区域（无效果）
    /// </summary>
    public class CardAreaMaterialOverlay : MonoBehaviour
    {
        [Header("材质设置")]
        [SerializeField] private Material validOverlayMaterial; // 可放置区域叠加材质（绿色）
        [SerializeField] private Material invalidOverlayMaterial; // 禁止区域叠加材质（红色）
        
        [Header("区域设置")]
        [SerializeField] private GameObject validAreaObject; // 可放置区域对象（地面）
        [SerializeField] private GameObject invalidAreaObject; // 禁止区域对象
        
        // 存储原始材质
        private Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
        
        // 当前激活的区域
        private GameObject currentActiveArea;
        private AreaType currentAreaType = AreaType.None;
        
        /// <summary>
        /// 区域类型
        /// </summary>
        public enum AreaType
        {
            None,       // 无区域（不显示效果）
            Valid,      // 可放置区域（绿色）
            Invalid     // 禁止区域（红色）
        }

        void Awake()
        {
            // 存储所有区域对象的原始材质
            if (validAreaObject != null)
            {
                StoreOriginalMaterials(validAreaObject);
            }
            
            if (invalidAreaObject != null)
            {
                StoreOriginalMaterials(invalidAreaObject);
            }
        }

        /// <summary>
        /// 存储对象的原始材质
        /// </summary>
        private void StoreOriginalMaterials(GameObject obj)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                if (!originalMaterials.ContainsKey(renderer))
                {
                    originalMaterials[renderer] = renderer.sharedMaterials;
                }
            }
        }

        /// <summary>
        /// 显示区域效果
        /// </summary>
        /// <param name="worldPosition">世界坐标位置</param>
        /// <param name="isValid">是否为有效区域（不在禁止区域）</param>
        public void ShowAreaEffect(Vector3 worldPosition, bool isValid)
        {
            // 判断位置属于哪个区域
            AreaType areaType = DetermineAreaType(worldPosition, isValid);
            
            Debug.Log($"[Card] ShowAreaEffect: position={worldPosition}, isValid={isValid}, areaType={areaType}");
            
            // 根据区域类型显示效果
            switch (areaType)
            {
                case AreaType.Valid:
                    ShowValidAreaEffect();
                    break;
                    
                case AreaType.Invalid:
                    ShowInvalidAreaEffect();
                    break;
                    
                case AreaType.None:
                    HideAreaEffect();
                    break;
            }
        }
        
        /// <summary>
        /// 判断位置属于哪个区域
        /// </summary>
        private AreaType DetermineAreaType(Vector3 worldPosition, bool isValid)
        {
            // 如果在禁止区域
            if (!isValid)
            {
                if (invalidAreaObject != null && IsPositionInArea(worldPosition, invalidAreaObject))
                {
                    return AreaType.Invalid;
                }
            }
            
            // 如果在可放置区域
            if (isValid)
            {
                if (validAreaObject != null && IsPositionInArea(worldPosition, validAreaObject))
                {
                    return AreaType.Valid;
                }
            }
            
            // 其他区域（不在任何配置的区域内）
            return AreaType.None;
        }
        
        /// <summary>
        /// 显示可放置区域效果（绿色）
        /// </summary>
        private void ShowValidAreaEffect()
        {
            GameObject targetArea = validAreaObject;
            Material overlayMaterial = validOverlayMaterial;
            
            // 检查配置
            if (targetArea == null)
            {
                Debug.LogWarning("[Card] ValidAreaObject not configured");
                return;
            }
            
            if (overlayMaterial == null)
            {
                Debug.LogWarning("[Card] ValidOverlayMaterial not configured");
                return;
            }
            
            // 如果区域切换了，先清除之前的效果
            if (currentActiveArea != targetArea && currentActiveArea != null)
            {
                Debug.Log("[Card] Switching area, clearing previous effect");
                RemoveOverlayMaterial(currentActiveArea);
            }
            
            // 如果已经是当前区域，不重复应用
            if (currentActiveArea == targetArea && currentAreaType == AreaType.Valid)
            {
                return;
            }
            
            // 应用叠加材质
            Debug.Log($"[Card] ✅ Showing valid area effect (green): {targetArea.name}");
            ApplyOverlayMaterial(targetArea, overlayMaterial);
            currentActiveArea = targetArea;
            currentAreaType = AreaType.Valid;
        }
        
        /// <summary>
        /// 显示禁止区域效果（红色）
        /// </summary>
        private void ShowInvalidAreaEffect()
        {
            GameObject targetArea = invalidAreaObject;
            Material overlayMaterial = invalidOverlayMaterial;
            
            // 检查配置
            if (targetArea == null)
            {
                Debug.LogWarning("[Card] InvalidAreaObject not configured");
                return;
            }
            
            if (overlayMaterial == null)
            {
                Debug.LogWarning("[Card] InvalidOverlayMaterial not configured");
                return;
            }
            
            // 如果区域切换了，先清除之前的效果
            if (currentActiveArea != targetArea && currentActiveArea != null)
            {
                Debug.Log("[Card] Switching area, clearing previous effect");
                RemoveOverlayMaterial(currentActiveArea);
            }
            
            // 如果已经是当前区域，不重复应用
            if (currentActiveArea == targetArea && currentAreaType == AreaType.Invalid)
            {
                return;
            }
            
            // 应用叠加材质
            Debug.Log($"[Card] ❌ Showing invalid area effect (red): {targetArea.name}");
            ApplyOverlayMaterial(targetArea, overlayMaterial);
            currentActiveArea = targetArea;
            currentAreaType = AreaType.Invalid;
        }

        /// <summary>
        /// 隐藏区域效果
        /// </summary>
        public void HideAreaEffect()
        {
            if (currentActiveArea != null)
            {
                Debug.Log($"[Card] Hiding area effect: {currentAreaType}");
                RemoveOverlayMaterial(currentActiveArea);
                currentActiveArea = null;
                currentAreaType = AreaType.None;
            }
        }

        /// <summary>
        /// 应用叠加材质到对象
        /// </summary>
        private void ApplyOverlayMaterial(GameObject obj, Material overlayMaterial)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            Debug.Log($"[Card] Found {renderers.Length} renderers");
            
            foreach (var renderer in renderers)
            {
                // 获取原始材质
                if (!originalMaterials.ContainsKey(renderer))
                {
                    originalMaterials[renderer] = renderer.sharedMaterials;
                    Debug.Log($"[Card] Stored original materials: {renderer.name}, count: {renderer.sharedMaterials.Length}");
                }
                
                Material[] originalMats = originalMaterials[renderer];
                
                // 创建新的材质数组，添加叠加材质
                Material[] newMaterials = new Material[originalMats.Length + 1];
                for (int i = 0; i < originalMats.Length; i++)
                {
                    newMaterials[i] = originalMats[i];
                }
                newMaterials[originalMats.Length] = overlayMaterial;
                
                // 应用新材质数组
                renderer.materials = newMaterials;
                Debug.Log($"[Card] Applied overlay material to {renderer.name}, new count: {newMaterials.Length}");
            }
        }

        /// <summary>
        /// 移除叠加材质
        /// </summary>
        private void RemoveOverlayMaterial(GameObject obj)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                // 恢复原始材质
                if (originalMaterials.ContainsKey(renderer))
                {
                    renderer.materials = originalMaterials[renderer];
                }
            }
        }

        /// <summary>
        /// 检查位置是否在指定区域内（支持不规则形状）
        /// </summary>
        public bool IsPositionInArea(Vector3 worldPosition, GameObject areaObject)
        {
            if (areaObject == null) return false;
            
            // 使用 Collider 检测（支持不规则形状）
            Collider[] colliders = areaObject.GetComponentsInChildren<Collider>();
            
            if (colliders.Length == 0)
            {
                Debug.LogWarning($"[Card] {areaObject.name} has no Collider components");
                return false;
            }
            
            foreach (var collider in colliders)
            {
                // 使用 ClosestPoint 检测点是否在 Collider 内
                Vector3 closestPoint = collider.ClosestPoint(worldPosition);
                float distance = Vector3.Distance(worldPosition, closestPoint);
                
                // 如果距离非常小（小于0.01），说明点在 Collider 内部或表面
                if (distance < 0.01f)
                {
                    return true;
                }
            }
            
            return false;
        }

        void OnDestroy()
        {
            // 清理：恢复所有原始材质
            foreach (var kvp in originalMaterials)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.materials = kvp.Value;
                }
            }
            originalMaterials.Clear();
        }
    }
}
