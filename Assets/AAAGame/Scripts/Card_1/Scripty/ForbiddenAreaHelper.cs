using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 禁止区域辅助工具
/// 用于可视化和调整禁止区域范围
/// </summary>
[ExecuteInEditMode]
public class ForbiddenAreaHelper : MonoBehaviour
{
    [Header("区域设置")]
    [Tooltip("禁止区域的中心位置")]
    public Vector3 areaCenter = new Vector3(0, 0, 10);
    
    [Tooltip("禁止区域的大小（X, Y, Z）")]
    public Vector3 areaSize = new Vector3(10, 2, 10);
    
    [Header("可视化设置")]
    [Tooltip("是否显示区域边界")]
    public bool showBounds = true;
    
    [Tooltip("边界颜色")]
    public Color boundsColor = new Color(1, 0, 0, 0.3f);
    
    [Tooltip("边框颜色")]
    public Color wireColor = Color.red;
    
    [Header("自动应用")]
    [Tooltip("是否自动应用到 Transform")]
    public bool autoApplyToTransform = true;

    void Update()
    {
        if (autoApplyToTransform && Application.isEditor && !Application.isPlaying)
        {
            // 在编辑器模式下自动更新 Transform
            transform.position = areaCenter;
            transform.localScale = areaSize;
        }
    }

    void OnDrawGizmos()
    {
        if (!showBounds) return;

        // 绘制半透明立方体
        Gizmos.color = boundsColor;
        Gizmos.DrawCube(areaCenter, areaSize*100);
        
        // 绘制边框
        Gizmos.color = wireColor;
        Gizmos.DrawWireCube(areaCenter, areaSize*10);
        
        // 绘制中心点
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(areaCenter, 0.2f);
        
        // 绘制坐标轴
        DrawAxis(areaCenter, areaSize);
    }

    void OnDrawGizmosSelected()
    {
        if (!showBounds) return;

        // 选中时绘制更详细的信息
        Gizmos.color = Color.white;
        
        // 绘制8个角点
        Vector3 halfSize = areaSize * 0.5f;
        Vector3[] corners = new Vector3[8]
        {
            areaCenter + new Vector3(-halfSize.x, -halfSize.y, -halfSize.z),
            areaCenter + new Vector3(halfSize.x, -halfSize.y, -halfSize.z),
            areaCenter + new Vector3(halfSize.x, -halfSize.y, halfSize.z),
            areaCenter + new Vector3(-halfSize.x, -halfSize.y, halfSize.z),
            areaCenter + new Vector3(-halfSize.x, halfSize.y, -halfSize.z),
            areaCenter + new Vector3(halfSize.x, halfSize.y, -halfSize.z),
            areaCenter + new Vector3(halfSize.x, halfSize.y, halfSize.z),
            areaCenter + new Vector3(-halfSize.x, halfSize.y, halfSize.z),
        };
        
        foreach (var corner in corners)
        {
            Gizmos.DrawSphere(corner, 0.15f);
        }
    }

    /// <summary>
    /// 绘制坐标轴
    /// </summary>
    private void DrawAxis(Vector3 center, Vector3 size)
    {
        float axisLength = Mathf.Max(size.x, size.y, size.z) * 0.6f;
        
        // X轴（红色）
        Gizmos.color = Color.red;
        Gizmos.DrawLine(center, center + Vector3.right * axisLength);
        
        // Y轴（绿色）
        Gizmos.color = Color.green;
        Gizmos.DrawLine(center, center + Vector3.up * axisLength);
        
        // Z轴（蓝色）
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(center, center + Vector3.forward * axisLength);
    }

    /// <summary>
    /// 应用设置到 Transform
    /// </summary>
    [ContextMenu("应用到 Transform")]
    public void ApplyToTransform()
    {
        transform.position = areaCenter;
        transform.localScale = areaSize;
        GF.Log($"已应用设置：位置 {areaCenter}, 大小 {areaSize}");
    }

    /// <summary>
    /// 从 Transform 读取设置
    /// </summary>
    [ContextMenu("从 Transform 读取")]
    public void ReadFromTransform()
    {
        areaCenter = transform.position;
        areaSize = transform.localScale;
        GF.Log($"已读取设置：位置 {areaCenter}, 大小 {areaSize}");
    }

    /// <summary>
    /// 重置为默认值
    /// </summary>
    [ContextMenu("重置为默认值")]
    public void ResetToDefault()
    {
        areaCenter = new Vector3(0, 0, 10);
        areaSize = new Vector3(10, 2, 10);
        ApplyToTransform();
        GF.Log("已重置为默认值");
    }

    /// <summary>
    /// 扩大区域（2倍）
    /// </summary>
    [ContextMenu("扩大区域（2倍）")]
    public void ExpandArea()
    {
        areaSize *= 2f;
        if (autoApplyToTransform)
        {
            ApplyToTransform();
        }
        GF.Log($"区域已扩大：{areaSize}");
    }

    /// <summary>
    /// 缩小区域（0.5倍）
    /// </summary>
    [ContextMenu("缩小区域（0.5倍）")]
    public void ShrinkArea()
    {
        areaSize *= 0.5f;
        if (autoApplyToTransform)
        {
            ApplyToTransform();
        }
        GF.Log($"区域已缩小：{areaSize}");
    }

    /// <summary>
    /// 测试点是否在区域内
    /// </summary>
    public bool IsPointInArea(Vector3 point)
    {
        Bounds bounds = new Bounds(areaCenter, areaSize);
        return bounds.Contains(point);
    }

    /// <summary>
    /// 显示区域信息
    /// </summary>
    [ContextMenu("显示区域信息")]
    public void ShowAreaInfo()
    {
        Bounds bounds = new Bounds(areaCenter, areaSize);
        
        GF.Log("========== 禁止区域信息 ==========");
        GF.Log($"中心位置: ({areaCenter.x:F2}, {areaCenter.y:F2}, {areaCenter.z:F2})");
        GF.Log($"区域大小: ({areaSize.x:F2}, {areaSize.y:F2}, {areaSize.z:F2})");
        GF.Log($"最小点: ({bounds.min.x:F2}, {bounds.min.y:F2}, {bounds.min.z:F2})");
        GF.Log($"最大点: ({bounds.max.x:F2}, {bounds.max.y:F2}, {bounds.max.z:F2})");
        GF.Log($"X 范围: {bounds.min.x:F2} 到 {bounds.max.x:F2}");
        GF.Log($"Y 范围: {bounds.min.y:F2} 到 {bounds.max.y:F2}");
        GF.Log($"Z 范围: {bounds.min.z:F2} 到 {bounds.max.z:F2}");
        GF.Log("================================");
    }
}
