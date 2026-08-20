using System;
using UnityEngine;

public static class BuildingPanelScreenClamp
{
    private const float Margin = 8f;
    private const float TargetGap = Margin;
    private const float SizeTolerance = 0.01f;

    public static void PlaceBesideTarget(
        RectTransform panel,
        RectTransform parent,
        Transform target,
        Vector2 preferredOffset,
        float reservedRightWidth = 0f)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        Canvas.ForceUpdateCanvases();
        Rect targetBounds = CalculateTargetBounds(parent, target);
        PlaceBesideTargetBounds(
            panel,
            parent,
            targetBounds,
            preferredOffset,
            reservedRightWidth);
    }

    public static void PlaceBesideTargetBounds(
        RectTransform panel,
        RectTransform parent,
        Rect targetBounds,
        Vector2 preferredOffset,
        float reservedRightWidth = 0f)
    {
        if (panel == null)
            throw new ArgumentNullException(nameof(panel));
        if (parent == null)
            throw new ArgumentNullException(nameof(parent));
        if (panel.parent != parent)
            throw new InvalidOperationException("Building panel target placement requires the panel's direct parent.");
        if (reservedRightWidth < 0f)
            throw new ArgumentOutOfRangeException(nameof(reservedRightWidth));

        panel.anchoredPosition = targetBounds.center + preferredOffset;
        Bounds panelBounds = CalculatePanelBounds(panel, parent, reservedRightWidth);
        Rect available = GetAvailableRect(parent);

        if (panelBounds.size.x > available.width + SizeTolerance
            || panelBounds.size.y > available.height + SizeTolerance)
        {
            throw new InvalidOperationException(
                $"Building panel does not fit its parent. panel={panel.name}, bounds={panelBounds.size}, available={available.size}");
        }

        Vector2[] candidateOffsets =
        {
            new Vector2(
                targetBounds.xMax + TargetGap - panelBounds.min.x,
                CalculateAxisClampOffset(panelBounds.min.y, panelBounds.max.y, available.yMin, available.yMax)),
            new Vector2(
                targetBounds.xMin - TargetGap - panelBounds.max.x,
                CalculateAxisClampOffset(panelBounds.min.y, panelBounds.max.y, available.yMin, available.yMax)),
            new Vector2(
                CalculateAxisClampOffset(panelBounds.min.x, panelBounds.max.x, available.xMin, available.xMax),
                targetBounds.yMax + TargetGap - panelBounds.min.y),
            new Vector2(
                CalculateAxisClampOffset(panelBounds.min.x, panelBounds.max.x, available.xMin, available.xMax),
                targetBounds.yMin - TargetGap - panelBounds.max.y),
        };
        bool placed = false;
        for (int i = 0; i < candidateOffsets.Length; i++)
        {
            if (!FitsAvailable(panelBounds, candidateOffsets[i], available))
                continue;
            panel.anchoredPosition += candidateOffsets[i];
            placed = true;
            break;
        }
        if (!placed)
        {
            throw new InvalidOperationException(
                $"Building panel cannot fit beside its target. panel={panelBounds.size}, target={targetBounds.size}, available={available.size}");
        }
        ClampToParent(panel, parent, reservedRightWidth);
    }

    private static float CalculateAxisClampOffset(
        float boundsMin,
        float boundsMax,
        float availableMin,
        float availableMax)
    {
        if (boundsMin < availableMin)
            return availableMin - boundsMin;
        if (boundsMax > availableMax)
            return availableMax - boundsMax;
        return 0f;
    }

    private static bool FitsAvailable(Bounds bounds, Vector2 offset, Rect available)
    {
        return bounds.min.x + offset.x >= available.xMin - SizeTolerance
               && bounds.max.x + offset.x <= available.xMax + SizeTolerance
               && bounds.min.y + offset.y >= available.yMin - SizeTolerance
               && bounds.max.y + offset.y <= available.yMax + SizeTolerance;
    }

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

        Bounds bounds = CalculatePanelBounds(panel, parent, reservedRightWidth);
        Rect available = GetAvailableRect(parent);

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

    private static Rect CalculateTargetBounds(RectTransform parent, Transform target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(false);
        bool hasBounds = false;
        Bounds worldBounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null
                || !renderer.enabled
                || renderer is ParticleSystemRenderer
                || renderer is LineRenderer
                || renderer is TrailRenderer)
                continue;
            if (!hasBounds)
            {
                worldBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                worldBounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
        {
            Vector2 point = GF.UI.PositionWorldToUI(target.position, parent);
            return new Rect(point, Vector2.zero);
        }

        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;
        Vector2 uiMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 uiMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 worldPoint = new Vector3(
                (corner & 1) == 0 ? min.x : max.x,
                (corner & 2) == 0 ? min.y : max.y,
                (corner & 4) == 0 ? min.z : max.z);
            Vector2 uiPoint = GF.UI.PositionWorldToUI(worldPoint, parent);
            uiMin = Vector2.Min(uiMin, uiPoint);
            uiMax = Vector2.Max(uiMax, uiPoint);
        }
        return Rect.MinMaxRect(uiMin.x, uiMin.y, uiMax.x, uiMax.y);
    }

    private static Bounds CalculatePanelBounds(
        RectTransform panel,
        RectTransform parent,
        float reservedRightWidth)
    {
        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, panel);
        if (reservedRightWidth <= 0f)
            return bounds;

        Vector3 reservedRightPoint = parent.InverseTransformPoint(
            panel.TransformPoint(new Vector3(panel.rect.xMax + reservedRightWidth, panel.rect.center.y, 0f)));
        bounds.max = new Vector3(Mathf.Max(bounds.max.x, reservedRightPoint.x), bounds.max.y, bounds.max.z);
        return bounds;
    }

    private static Rect GetAvailableRect(RectTransform parent)
    {
        return Rect.MinMaxRect(
            parent.rect.xMin + Margin,
            parent.rect.yMin + Margin,
            parent.rect.xMax - Margin,
            parent.rect.yMax - Margin);
    }
}
