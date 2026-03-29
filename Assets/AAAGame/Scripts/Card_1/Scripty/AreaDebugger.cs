using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 区域检测调试工具
/// 用于诊断禁止区域检测和材质叠加问题
/// </summary>
public class AreaDebugger : MonoBehaviour
{
    [Header("调试设置")]
    [SerializeField] private bool enableDebug = true;
    [SerializeField] private KeyCode debugKey = KeyCode.F2;
    [SerializeField] private bool showGizmos = true;

    void Update()
    {
        if (enableDebug && Input.GetKeyDown(debugKey))
        {
            DebugAreaSystem();
        }

        // 实时显示鼠标位置检测
        if (enableDebug && Input.GetMouseButton(0))
        {
            DebugMousePosition();
        }
    }

    /// <summary>
    /// 调试区域系统配置
    /// </summary>
    [ContextMenu("调试区域系统")]
    public void DebugAreaSystem()
    {
        GF.Log("========== 开始调试区域系统 ==========");

        // 检查 CardUIManager
        if (CardUIManager.Instance == null)
        {
            GF.LogError("❌ CardUIManager.Instance 为 null");
            return;
        }
        GF.Log("✅ CardUIManager 存在");

        // 使用反射检查配置
        var manager = CardUIManager.Instance;
        var type = manager.GetType();

        // 检查 forbiddenArea
        var forbiddenAreaField = type.GetField("forbiddenArea",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        var forbiddenArea = forbiddenAreaField?.GetValue(manager) as Transform;

        if (forbiddenArea == null)
        {
            GF.LogError("❌ forbiddenArea 未配置！");
        }
        else
        {
            GF.Log($"✅ forbiddenArea: {forbiddenArea.name}");
            GF.Log($"   位置: {forbiddenArea.position}");
            GF.Log($"   缩放: {forbiddenArea.localScale}");
            
            // 检查 Collider
            var collider = forbiddenArea.GetComponent<Collider>();
            if (collider == null)
            {
                GF.LogWarning("⚠️ forbiddenArea 没有 Collider 组件");
            }
            else
            {
                GF.Log($"✅ Collider: {collider.GetType().Name}");
            }

            // 检查 Renderer
            var renderer = forbiddenArea.GetComponent<Renderer>();
            if (renderer == null)
            {
                GF.LogWarning("⚠️ forbiddenArea 没有 Renderer 组件");
            }
            else
            {
                GF.Log($"✅ Renderer: {renderer.GetType().Name}");
                GF.Log($"   材质数量: {renderer.sharedMaterials.Length}");
            }
        }

        // 检查 AreaMaterialOverlay
        var overlayField = type.GetField("areaMaterialOverlay",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        var overlay = overlayField?.GetValue(manager) as AreaMaterialOverlay;

        if (overlay == null)
        {
            GF.LogError("❌ areaMaterialOverlay 未配置！");
        }
        else
        {
            GF.Log($"✅ areaMaterialOverlay: {overlay.name}");
            CheckOverlayConfiguration(overlay);
        }

        GF.Log("========== 调试完成 ==========");
    }

    /// <summary>
    /// 检查 AreaMaterialOverlay 配置
    /// </summary>
    private void CheckOverlayConfiguration(AreaMaterialOverlay overlay)
    {
        var type = overlay.GetType();

        // 检查材质
        var validMatField = type.GetField("validOverlayMaterial",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        var validMat = validMatField?.GetValue(overlay) as Material;

        var invalidMatField = type.GetField("invalidOverlayMaterial",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        var invalidMat = invalidMatField?.GetValue(overlay) as Material;

        if (validMat == null)
        {
            GF.LogError("❌ validOverlayMaterial 未配置");
        }
        else
        {
            GF.Log($"✅ validOverlayMaterial: {validMat.name}");
        }

        if (invalidMat == null)
        {
            GF.LogError("❌ invalidOverlayMaterial 未配置");
        }
        else
        {
            GF.Log($"✅ invalidOverlayMaterial: {invalidMat.name}");
        }

        // 检查区域对象
        var validAreaField = type.GetField("validAreaObject",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        var validArea = validAreaField?.GetValue(overlay) as GameObject;

        var invalidAreaField = type.GetField("invalidAreaObject",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        var invalidArea = invalidAreaField?.GetValue(overlay) as GameObject;

        if (validArea == null)
        {
            GF.LogWarning("⚠️ validAreaObject 未配置");
        }
        else
        {
            GF.Log($"✅ validAreaObject: {validArea.name}");
            CheckAreaObject(validArea, "可放置区域");
        }

        if (invalidArea == null)
        {
            GF.LogError("❌ invalidAreaObject 未配置");
        }
        else
        {
            GF.Log($"✅ invalidAreaObject: {invalidArea.name}");
            CheckAreaObject(invalidArea, "禁止区域");
        }
    }

    /// <summary>
    /// 检查区域对象配置
    /// </summary>
    private void CheckAreaObject(GameObject obj, string areaName)
    {
        // 检查 Renderer
        var renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            GF.LogError($"❌ {areaName} 没有 Renderer 组件");
        }
        else
        {
            GF.Log($"✅ {areaName} Renderer 数量: {renderers.Length}");
            foreach (var renderer in renderers)
            {
                GF.Log($"   - {renderer.name}: {renderer.sharedMaterials.Length} 个材质");
            }
        }

        // 检查 Collider
        var colliders = obj.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
        {
            GF.LogWarning($"⚠️ {areaName} 没有 Collider 组件");
        }
        else
        {
            GF.Log($"✅ {areaName} Collider 数量: {colliders.Length}");
        }
    }

    /// <summary>
    /// 调试鼠标位置
    /// </summary>
    private void DebugMousePosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            Vector3 worldPos = hit.point;
            
            // 检查是否在禁止区域
            bool isForbidden = CheckIfInForbiddenArea(worldPos);
            
            GF.Log($"鼠标位置: ({worldPos.x:F2}, {worldPos.y:F2}, {worldPos.z:F2}), " +
                   $"是否禁止区域: {isForbidden}, " +
                   $"碰撞对象: {hit.collider.name}");
        }
    }

    /// <summary>
    /// 检查位置是否在禁止区域
    /// </summary>
    private bool CheckIfInForbiddenArea(Vector3 worldPos)
    {
        if (CardUIManager.Instance == null) return false;

        var manager = CardUIManager.Instance;
        var type = manager.GetType();

        var forbiddenAreaField = type.GetField("forbiddenArea",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        var forbiddenArea = forbiddenAreaField?.GetValue(manager) as Transform;

        if (forbiddenArea == null) return false;

        Bounds bounds = new Bounds(forbiddenArea.position, forbiddenArea.localScale);
        return bounds.Contains(worldPos);
    }

    /// <summary>
    /// 绘制调试信息
    /// </summary>
    void OnDrawGizmos()
    {
        if (!showGizmos || CardUIManager.Instance == null) return;

        var manager = CardUIManager.Instance;
        var type = manager.GetType();

        // 绘制禁止区域
        var forbiddenAreaField = type.GetField("forbiddenArea",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        var forbiddenArea = forbiddenAreaField?.GetValue(manager) as Transform;

        if (forbiddenArea != null)
        {
            DrawAreaGizmos(forbiddenArea, Color.red, "禁止区域");
        }
        
        // 绘制可放置区域
        var validAreaField = type.GetField("validArea",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        var validArea = validAreaField?.GetValue(manager) as Transform;

        if (validArea != null)
        {
            DrawAreaGizmos(validArea, Color.green, "可放置区域");
        }
    }
    
    /// <summary>
    /// 绘制区域 Gizmos（支持不规则形状）
    /// </summary>
    private void DrawAreaGizmos(Transform areaTransform, Color color, string label)
    {
        Collider[] colliders = areaTransform.GetComponentsInChildren<Collider>();
        
        if (colliders.Length == 0)
        {
            // 如果没有 Collider，绘制简单的立方体
            Gizmos.color = new Color(color.r, color.g, color.b, 0.3f);
            Gizmos.DrawCube(areaTransform.position, areaTransform.localScale);
            
            Gizmos.color = color;
            Gizmos.DrawWireCube(areaTransform.position, areaTransform.localScale);
            return;
        }
        
        // 绘制每个 Collider
        foreach (var collider in colliders)
        {
            Gizmos.color = new Color(color.r, color.g, color.b, 0.3f);
            
            if (collider is BoxCollider boxCollider)
            {
                Matrix4x4 oldMatrix = Gizmos.matrix;
                Gizmos.matrix = collider.transform.localToWorldMatrix;
                Gizmos.DrawCube(boxCollider.center, boxCollider.size);
                Gizmos.color = color;
                Gizmos.DrawWireCube(boxCollider.center, boxCollider.size);
                Gizmos.matrix = oldMatrix;
            }
            else if (collider is SphereCollider sphereCollider)
            {
                Gizmos.DrawSphere(collider.transform.TransformPoint(sphereCollider.center), 
                                 sphereCollider.radius * collider.transform.lossyScale.x);
                Gizmos.color = color;
                Gizmos.DrawWireSphere(collider.transform.TransformPoint(sphereCollider.center), 
                                     sphereCollider.radius * collider.transform.lossyScale.x);
            }
            else if (collider is CapsuleCollider capsuleCollider)
            {
                // 简化绘制胶囊体为球体
                Gizmos.DrawSphere(collider.transform.TransformPoint(capsuleCollider.center), 
                                 capsuleCollider.radius * collider.transform.lossyScale.x);
                Gizmos.color = color;
                Gizmos.DrawWireSphere(collider.transform.TransformPoint(capsuleCollider.center), 
                                     capsuleCollider.radius * collider.transform.lossyScale.x);
            }
            else if (collider is MeshCollider meshCollider)
            {
                // Mesh Collider 绘制边界框
                Bounds bounds = meshCollider.bounds;
                Gizmos.DrawCube(bounds.center, bounds.size);
                Gizmos.color = color;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }
        }
    }
}
