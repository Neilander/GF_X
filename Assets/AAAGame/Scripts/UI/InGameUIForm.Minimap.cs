using System;
using AAAGame.MiniMap;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

public partial class InGameUIForm
{
    private const float MinimapSubFormScale = 0.98f;
    private const float LargeMapViewportScale = 0.8f;
    private const int MinimapSubFormSortingOffset = 1;
    private const int MinimapMaskSortingOffset = 2;

    private int m_MinimapUIFormId = -1;
    private readonly Vector3[] m_MinimapMaskWorldCorners = new Vector3[4];
    private MinimapUI m_MinimapUI;
    private RectTransform m_MinimapArea;
    private Graphic m_MinimapMaskGraphic;
    private MinimapOpenClickHandler m_MinimapOpenClickHandler;
    private RectLayout m_MinimapAreaLayout;
    private RectLayout m_MinimapMaskLayout;
    private bool m_OriginalMaskRaycastTarget;
    private bool m_IsLargeMapOpen;

    private void InitializeMiniMap()
    {
        if (m_MinimapUIFormId != -1)
            return;
        if (varMiniMapMask == null)
            throw new InvalidOperationException("InGameUIForm requires varMiniMapMask.");

        m_MinimapArea = varMiniMapMask.parent as RectTransform
            ?? throw new InvalidOperationException("InGameUIForm minimap mask requires a RectTransform parent.");
        m_MinimapMaskGraphic = varMiniMapMask.GetComponent<Graphic>()
            ?? throw new InvalidOperationException("InGameUIForm minimap mask requires a Graphic.");
        m_MinimapAreaLayout = RectLayout.Capture(m_MinimapArea);
        m_MinimapMaskLayout = RectLayout.Capture(varMiniMapMask);
        m_OriginalMaskRaycastTarget = m_MinimapMaskGraphic.raycastTarget;

        m_MinimapOpenClickHandler = varMiniMapMask.GetComponent<MinimapOpenClickHandler>();
        if (m_MinimapOpenClickHandler == null)
            m_MinimapOpenClickHandler = varMiniMapMask.gameObject.AddComponent<MinimapOpenClickHandler>();
        m_MinimapOpenClickHandler.Configure(OpenLargeMap);

        UIParams uiParams = UIParams.Create();
        uiParams.OpenCallback = AlignMiniMapUIForm;
        m_MinimapUIFormId = OpenSubUIForm(UIViews.MinimapUI, 1, uiParams);
    }

    private void ShutdownMiniMap()
    {
        if (m_IsLargeMapOpen)
            CloseLargeMap();

        if (m_MinimapOpenClickHandler != null)
            m_MinimapOpenClickHandler.Configure(null);
        m_MinimapOpenClickHandler = null;
        m_MinimapUI = null;

        if (m_MinimapUIFormId != -1)
        {
            CloseSubUIForm(m_MinimapUIFormId);
            m_MinimapUIFormId = -1;
        }
    }

    private void TickMiniMap(InputManager inputManager)
    {
        if (inputManager == null || inputManager.CurState != InputState.Game || m_MinimapUI == null)
            return;

        if (inputManager.WasActionPressedThisFrame("Player/Map"))
        {
            if (m_IsLargeMapOpen)
                CloseLargeMap();
            else
                OpenLargeMap();
            return;
        }

        if (m_IsLargeMapOpen && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            CloseLargeMap();
    }

    private void OpenLargeMap()
    {
        if (m_IsLargeMapOpen)
            return;
        if (m_MinimapUI == null)
            throw new InvalidOperationException("InGameUIForm cannot open the large map before MinimapUI is loaded.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("InGameUIForm cannot open the large map without an active logic timeline.");

        RectTransform viewport = m_MinimapArea.parent as RectTransform
            ?? throw new InvalidOperationException("InGameUIForm minimap area requires a RectTransform parent.");
        Vector2 available = viewport.rect.size * LargeMapViewportScale;
        float aspect = Mathf.Abs(m_MinimapMaskLayout.SizeDelta.x / m_MinimapMaskLayout.SizeDelta.y);
        Vector2 expandedSize = available.x / available.y > aspect
            ? new Vector2(available.y * aspect, available.y)
            : new Vector2(available.x, available.x / aspect);

        m_MinimapArea.anchorMin = new Vector2(0.5f, 0.5f);
        m_MinimapArea.anchorMax = new Vector2(0.5f, 0.5f);
        m_MinimapArea.pivot = new Vector2(0.5f, 0.5f);
        m_MinimapArea.anchoredPosition = Vector2.zero;
        m_MinimapArea.sizeDelta = expandedSize;

        varMiniMapMask.anchorMin = new Vector2(0.5f, 0.5f);
        varMiniMapMask.anchorMax = new Vector2(0.5f, 0.5f);
        varMiniMapMask.pivot = new Vector2(0.5f, 0.5f);
        varMiniMapMask.anchoredPosition = Vector2.zero;
        varMiniMapMask.sizeDelta = expandedSize;
        m_MinimapMaskGraphic.raycastTarget = false;

        m_IsLargeMapOpen = true;
        LogicTimeControlService.AcquirePause(LogicTimeControlSources.LargeMapUiPause);
        AlignMiniMapUIForm(m_MinimapUI);
        m_MinimapUI.SetLargeMapState(true, OnTeleportationPointClicked);
        Log.Info("[LargeMap] Opened. phase={0}, size={1}.", PhaseManager.CurrentPhase, expandedSize);
    }

    private void CloseLargeMap()
    {
        if (!m_IsLargeMapOpen)
            return;

        m_MinimapAreaLayout.Apply(m_MinimapArea);
        m_MinimapMaskLayout.Apply(varMiniMapMask);
        m_MinimapMaskGraphic.raycastTarget = m_OriginalMaskRaycastTarget;
        m_IsLargeMapOpen = false;

        if (m_MinimapUI != null)
        {
            AlignMiniMapUIForm(m_MinimapUI);
            m_MinimapUI.SetLargeMapState(false, null);
        }

        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("InGameUIForm cannot release the large-map pause after the logic timeline ended.");
        LogicTimeControlService.ReleasePause(LogicTimeControlSources.LargeMapUiPause);
        Log.Info("[LargeMap] Closed.");
    }

    private void OnTeleportationPointClicked(EntityPresetPoint point)
    {
        LogicTeleportCommand command = TeleportationPointService.SchedulePlayerTeleport(point);
        Log.Info(
            "[LargeMap] Teleport scheduled. point={0}, stronghold={1}, entity={2}, frame={3}, raw=({4},{5}).",
            point.name,
            command.StrongholdId,
            command.EntityId.Value,
            command.EffectiveFrame,
            command.Destination.x.RawValue,
            command.Destination.y.RawValue);
        CloseLargeMap();
    }

    private void AlignMiniMapUIForm(UIFormLogic uiFormLogic)
    {
        m_MinimapUI = uiFormLogic as MinimapUI
            ?? throw new InvalidOperationException("InGameUIForm expected MinimapUI for its minimap sub-form.");

        RectTransform targetRect = m_MinimapUI.transform as RectTransform
            ?? throw new InvalidOperationException("MinimapUI requires a RectTransform.");
        RectTransform targetParent = targetRect.parent as RectTransform
            ?? throw new InvalidOperationException("MinimapUI requires a RectTransform parent.");

        Canvas minimapCanvas = m_MinimapUI.GetComponent<Canvas>();
        if (minimapCanvas != null)
        {
            minimapCanvas.overrideSorting = true;
            minimapCanvas.sortingOrder = SortOrder + MinimapSubFormSortingOffset;
        }

        Canvas maskCanvas = varMiniMapMask.GetComponent<Canvas>();
        if (maskCanvas == null)
            maskCanvas = varMiniMapMask.gameObject.AddComponent<Canvas>();
        maskCanvas.overrideSorting = true;
        maskCanvas.sortingOrder = SortOrder + MinimapMaskSortingOffset;
        if (varMiniMapMask.GetComponent<GraphicRaycaster>() == null)
            varMiniMapMask.gameObject.AddComponent<GraphicRaycaster>();

        varMiniMapMask.GetWorldCorners(m_MinimapMaskWorldCorners);
        Canvas canvas = targetParent.GetComponentInParent<Canvas>();
        Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera != null ? canvas.worldCamera : GF.UICamera
            : null;

        Vector2 bottomLeftScreen = RectTransformUtility.WorldToScreenPoint(uiCamera, m_MinimapMaskWorldCorners[0]);
        Vector2 topRightScreen = RectTransformUtility.WorldToScreenPoint(uiCamera, m_MinimapMaskWorldCorners[2]);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(targetParent, bottomLeftScreen, uiCamera, out Vector2 bottomLeft)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(targetParent, topRightScreen, uiCamera, out Vector2 topRight))
        {
            throw new InvalidOperationException("InGameUIForm failed to convert minimap mask corners into UI-local coordinates.");
        }

        Vector2 size = topRight - bottomLeft;
        targetRect.anchorMin = new Vector2(0.5f, 0.5f);
        targetRect.anchorMax = new Vector2(0.5f, 0.5f);
        targetRect.pivot = new Vector2(0.5f, 0.5f);
        targetRect.anchoredPosition = (bottomLeft + topRight) * 0.5f;
        targetRect.sizeDelta = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y)) * MinimapSubFormScale;
        targetRect.localScale = Vector3.one;
        Canvas.ForceUpdateCanvases();
        m_MinimapUI.RefreshLayout();
    }

    private readonly struct RectLayout
    {
        private RectLayout(RectTransform rect)
        {
            AnchorMin = rect.anchorMin;
            AnchorMax = rect.anchorMax;
            Pivot = rect.pivot;
            AnchoredPosition = rect.anchoredPosition;
            SizeDelta = rect.sizeDelta;
        }

        public Vector2 AnchorMin { get; }
        public Vector2 AnchorMax { get; }
        public Vector2 Pivot { get; }
        public Vector2 AnchoredPosition { get; }
        public Vector2 SizeDelta { get; }

        public static RectLayout Capture(RectTransform rect) => new RectLayout(rect);

        public void Apply(RectTransform rect)
        {
            rect.anchorMin = AnchorMin;
            rect.anchorMax = AnchorMax;
            rect.pivot = Pivot;
            rect.anchoredPosition = AnchoredPosition;
            rect.sizeDelta = SizeDelta;
        }
    }
}

public sealed class MinimapOpenClickHandler : MonoBehaviour, IPointerClickHandler
{
    private Action m_OnLeftClick;

    public void Configure(Action onLeftClick)
    {
        m_OnLeftClick = onLeftClick;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            m_OnLeftClick?.Invoke();
    }
}
