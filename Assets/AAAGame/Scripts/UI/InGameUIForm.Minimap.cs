using UnityEngine;
using UnityGameFramework.Runtime;

public partial class InGameUIForm
{
    private const float MinimapSubFormScale = 0.98f;
    private const int MinimapSubFormSortingOffset = 1;
    private const int MinimapMaskSortingOffset = 2;

    private int m_MinimapUIFormId = -1;
    private readonly Vector3[] m_MinimapMaskWorldCorners = new Vector3[4];

    private void InitializeMiniMap()
    {
        if (m_MinimapUIFormId != -1)
        {
            return;
        }

        if (varMiniMapMask == null)
        {
            Log.Warning("[InGameUIForm] MiniMapMask is null, skip minimap initialization.");
            return;
        }

        UIParams uiParams = UIParams.Create();
        uiParams.OpenCallback = AlignMiniMapUIForm;
        m_MinimapUIFormId = OpenSubUIForm(UIViews.MinimapUI, 1, uiParams);
    }

    private void ShutdownMiniMap()
    {
        if (m_MinimapUIFormId == -1)
        {
            return;
        }

        CloseSubUIForm(m_MinimapUIFormId);
        m_MinimapUIFormId = -1;
    }

    private void AlignMiniMapUIForm(UIFormLogic uiFormLogic)
    {
        if (uiFormLogic == null || varMiniMapMask == null)
        {
            return;
        }

        RectTransform targetRect = uiFormLogic.transform as RectTransform;
        RectTransform targetParent = targetRect != null ? targetRect.parent as RectTransform : null;
        if (targetRect == null || targetParent == null)
        {
            return;
        }

        Canvas minimapCanvas = uiFormLogic.GetComponent<Canvas>();
        if (minimapCanvas != null)
        {
            minimapCanvas.overrideSorting = true;
            minimapCanvas.sortingOrder = SortOrder + MinimapSubFormSortingOffset;
        }

        Canvas maskCanvas = varMiniMapMask.GetComponent<Canvas>();
        if (maskCanvas == null)
        {
            maskCanvas = varMiniMapMask.gameObject.AddComponent<Canvas>();
        }

        maskCanvas.overrideSorting = true;
        maskCanvas.sortingOrder = SortOrder + MinimapMaskSortingOffset;

        varMiniMapMask.GetWorldCorners(m_MinimapMaskWorldCorners);

        Canvas canvas = targetParent.GetComponentInParent<Canvas>();
        Camera uiCamera = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            uiCamera = canvas.worldCamera != null ? canvas.worldCamera : GF.UICamera;
        }

        Vector2 bottomLeftScreen = RectTransformUtility.WorldToScreenPoint(uiCamera, m_MinimapMaskWorldCorners[0]);
        Vector2 topRightScreen = RectTransformUtility.WorldToScreenPoint(uiCamera, m_MinimapMaskWorldCorners[2]);

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(targetParent, bottomLeftScreen, uiCamera, out Vector2 bottomLeft)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(targetParent, topRightScreen, uiCamera, out Vector2 topRight))
        {
            return;
        }

        Vector2 size = topRight - bottomLeft;
        targetRect.anchorMin = new Vector2(0.5f, 0.5f);
        targetRect.anchorMax = new Vector2(0.5f, 0.5f);
        targetRect.pivot = new Vector2(0.5f, 0.5f);
        targetRect.anchoredPosition = (bottomLeft + topRight) * 0.5f;
        targetRect.sizeDelta = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y)) * MinimapSubFormScale;
        targetRect.localScale = Vector3.one;
    }
}
