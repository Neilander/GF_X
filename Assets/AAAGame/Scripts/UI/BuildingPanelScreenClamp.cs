using System;
using UnityEngine;

public static class BuildingPanelScreenClamp
{
    private const float Margin = 8f;
    private const float SizeTolerance = 0.01f;

    public static void ClampToParent(RectTransform panel, RectTransform parent, float reservedRightWidth = 0f)
    {
        if (panel == null)
            throw new ArgumentNullException(nameof(panel));
        if (parent == null)
            throw new ArgumentNullException(nameof(parent));
        if (panel.parent != parent)
            throw new InvalidOperationException("Building panel screen clamp requires the panel's direct parent.");
        if (reservedRightWidth < 0f)
            throw new ArgumentOutOfRangeException(nameof(reservedRightWidth), reservedRightWidth, "Reserved panel width cannot be negative.");

        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, panel);
        if (reservedRightWidth > 0f)
        {
            Vector3 reservedRightPoint = parent.InverseTransformPoint(
                panel.TransformPoint(new Vector3(panel.rect.xMax + reservedRightWidth, panel.rect.center.y, 0f)));
            bounds.max = new Vector3(Mathf.Max(bounds.max.x, reservedRightPoint.x), bounds.max.y, bounds.max.z);
        }
        Rect available = Rect.MinMaxRect(
            parent.rect.xMin + Margin,
            parent.rect.yMin + Margin,
            parent.rect.xMax - Margin,
            parent.rect.yMax - Margin);

        if (bounds.size.x > available.width + SizeTolerance || bounds.size.y > available.height + SizeTolerance)
        {
            throw new InvalidOperationException(
                $"Building panel does not fit its parent. panel={panel.name}, bounds={bounds.size}, available={available.size}");
        }

        Vector2 offset = Vector2.zero;
        if (bounds.min.x < available.xMin)
            offset.x = available.xMin - bounds.min.x;
        else if (bounds.max.x > available.xMax)
            offset.x = available.xMax - bounds.max.x;

        if (bounds.min.y < available.yMin)
            offset.y = available.yMin - bounds.min.y;
        else if (bounds.max.y > available.yMax)
            offset.y = available.yMax - bounds.max.y;

        panel.anchoredPosition += offset;
    }
}
