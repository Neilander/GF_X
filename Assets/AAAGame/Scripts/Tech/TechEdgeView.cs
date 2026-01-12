using UnityEngine;

/// <summary>
/// 连线视图占位（可由单独的批量渲染器读取）。
/// 运行时/编辑器可扫描所有 TechNodeView，并结合表数据的前置关系生成/更新连线。
/// </summary>
public class TechEdgeView : MonoBehaviour
{
    public RectTransform fromNode;
    public RectTransform toNode;
}
