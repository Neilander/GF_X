using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 科技节点的 UI 脚本（挂在每个节点的 RectTransform 上）。
/// - 使用极坐标（半径/角度）控制相对中心的布局
/// - 保存 TechId、Level、Category、前置科技（用于连线）
/// - 位置可在编辑器中手调（不被生成器覆盖）
/// </summary>
[ExecuteAlways]
public class TechNodeView : MonoBehaviour
{
    [Header("Identity")]
    public string techId;
    public int level;
    public TechCategory category;

    [Header("Layout (Polar)")]
    public float radius;
    public float angleDeg = 90f;

    [Header("Links")]
    public string[] prereqTechIds;

    [Header("Runtime References")]
    public RectTransform rect;

    private void Reset()
    {
        rect = GetComponent<RectTransform>();
    }

    public void SetPolar(float r, float angle)
    {
        radius = r;
        angleDeg = angle;
        ApplyPosition(Vector2.zero, 1f);
    }

    /// <summary>
    /// 将极坐标应用到 anchoredPosition。center 为坐标中心（父容器局部），scale 为半径缩放。
    /// </summary>
    public void ApplyPosition(Vector2 center, float scale)
    {
        if (rect == null) rect = GetComponent<RectTransform>();
        float rad = angleDeg * Mathf.Deg2Rad;
        Vector2 pos = center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * (radius * scale);
        rect.anchoredPosition = pos;
    }
}
