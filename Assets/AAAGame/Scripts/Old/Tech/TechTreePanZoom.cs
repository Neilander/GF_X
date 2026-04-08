using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 科技树大面板拖拽浏览 + 缩放。
/// 设计目标：适配 GF UIForm/UGUI 工作流；不依赖 ScrollRect，挂在 Viewport 背景上即可。
///
/// 结构建议：
/// - Viewport（挂 Image 作为 RaycastTarget，可透明；挂本脚本）
///   - Content（节点/连线都放这里，Pivot=0.5,0.5，Anchors=0.5,0.5）
///
/// 交互：
/// - 鼠标/单指在空白处拖拽平移 Content
/// - 鼠标滚轮缩放（以鼠标位置为缩放中心）
/// - 移动端双指捏合缩放（以两指中心为缩放中心）
/// </summary>
public sealed class TechTreePanZoom : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IScrollHandler
{
    [Header("Refs")]
    [SerializeField] private RectTransform viewport;
    [SerializeField] private RectTransform content;

    [Header("Pan")]
    [SerializeField] private bool enablePan = true;

    [Header("Zoom")]
    [SerializeField] private bool enableWheelZoom = true;
    [SerializeField] private bool enablePinchZoom = true;
    [SerializeField] private float minScale = 0.5f;
    [SerializeField] private float maxScale = 2.0f;
    [Tooltip("滚轮缩放灵敏度（指数缩放系数）。建议 0.05~0.2；数值越大越敏感")]
    [SerializeField] private float wheelZoomSpeed = 0.12f;
    [Tooltip("单帧内最大允许的滚轮增量（防止某些设备 scrollDelta 过大导致瞬间缩放到极值）")]
    [SerializeField] private float maxWheelDeltaPerFrame = 3f;
    [SerializeField] private float pinchZoomSpeed = 0.01f;

    private Canvas m_Canvas;
    private bool m_Dragging;

    private float m_LastPinchDistance;

    private void Reset()
    {
        viewport = transform as RectTransform;
        if (transform.childCount > 0)
        {
            var t = transform.GetChild(0) as RectTransform;
            content = t;
        }
    }

    private void Awake()
    {
        if (viewport == null) viewport = transform as RectTransform;
        m_Canvas = GetComponentInParent<Canvas>();
    }

    private void Update()
    {
        if (!enablePinchZoom) return;
        if (content == null || viewport == null) return;

        if (Input.touchCount != 2)
        {
            m_LastPinchDistance = 0f;
            return;
        }

        var t0 = Input.GetTouch(0);
        var t1 = Input.GetTouch(1);

        var eventCamera = GetEventCamera();

        // 两指都在 viewport 内才响应（避免与其他 UI 冲突）
        if (!RectTransformUtility.RectangleContainsScreenPoint(viewport, t0.position, eventCamera)) return;
        if (!RectTransformUtility.RectangleContainsScreenPoint(viewport, t1.position, eventCamera)) return;

        float dist = Vector2.Distance(t0.position, t1.position);
        if (m_LastPinchDistance <= 0.001f)
        {
            m_LastPinchDistance = dist;
            return;
        }

        float delta = dist - m_LastPinchDistance;
        m_LastPinchDistance = dist;

        Vector2 center = (t0.position + t1.position) * 0.5f;
        float oldScale = content.localScale.x;
        float newScale = Mathf.Clamp(oldScale + delta * pinchZoomSpeed, minScale, maxScale);
        ZoomTo(newScale, center, eventCamera);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!enablePan) return;
        m_Dragging = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        m_Dragging = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!enablePan) return;
        if (!m_Dragging) return;
        if (content == null || viewport == null) return;

        float scaleFactor = (m_Canvas != null && m_Canvas.isRootCanvas) ? m_Canvas.scaleFactor : (m_Canvas != null ? m_Canvas.scaleFactor : 1f);
        if (scaleFactor <= 0.001f) scaleFactor = 1f;

        // PointerEventData.delta 是屏幕像素，换算到 UI 局部坐标
        Vector2 delta = eventData.delta / scaleFactor;
        content.anchoredPosition += delta;
        ClampContentToViewport();
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (!enableWheelZoom) return;
        if (content == null || viewport == null) return;

        // 有些鼠标/触控板会一次上报很大的 scrollDelta，导致 factor 爆炸式变化。
        float wheelDelta = Mathf.Clamp(eventData.scrollDelta.y, -maxWheelDeltaPerFrame, maxWheelDeltaPerFrame);
        if (Mathf.Abs(wheelDelta) <= 0.0001f) return;

        float oldScale = content.localScale.x;
        // 使用指数缩放：每个滚轮步进按比例缩放，体验更平滑且不容易出现负 factor。
        float factor = Mathf.Pow(1f + wheelZoomSpeed, wheelDelta);
        float newScale = Mathf.Clamp(oldScale * factor, minScale, maxScale);
        ZoomTo(newScale, eventData.position, eventData.pressEventCamera != null ? eventData.pressEventCamera : GetEventCamera());
    }

    private Camera GetEventCamera()
    {
        if (m_Canvas == null) m_Canvas = GetComponentInParent<Canvas>();
        if (m_Canvas == null) return null;

        // Overlay 模式下传 null 即可
        if (m_Canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;

        if (m_Canvas.worldCamera != null) return m_Canvas.worldCamera;
        return Camera.main;
    }

    private void ZoomTo(float newScale, Vector2 screenPoint, Camera eventCamera)
    {
        if (content == null || viewport == null) return;

        float oldScale = content.localScale.x;
        if (Mathf.Abs(newScale - oldScale) <= 0.0001f) return;

        // 以鼠标/捏合中心为缩放中心：保持该屏幕点对应的 content 局部点不漂移
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screenPoint, eventCamera, out var localInContent))
        {
            content.localScale = new Vector3(newScale, newScale, 1f);
            content.anchoredPosition += localInContent * (oldScale - newScale);
            ClampContentToViewport();
        }
        else
        {
            content.localScale = new Vector3(newScale, newScale, 1f);
            ClampContentToViewport();
        }
    }

    private void ClampContentToViewport()
    {
        if (content == null || viewport == null) return;

        float scale = content.localScale.x;
        Vector2 vp = viewport.rect.size;
        Vector2 cs = content.rect.size * scale;

        float maxX = Mathf.Max(0f, (cs.x - vp.x) * 0.5f);
        float maxY = Mathf.Max(0f, (cs.y - vp.y) * 0.5f);

        Vector2 p = content.anchoredPosition;
        p.x = Mathf.Clamp(p.x, -maxX, maxX);
        p.y = Mathf.Clamp(p.y, -maxY, maxY);
        content.anchoredPosition = p;
    }

    public void SetScale(float scale, bool clamp = true)
    {
        if (content == null) return;
        scale = Mathf.Clamp(scale, minScale, maxScale);
        content.localScale = new Vector3(scale, scale, 1f);
        if (clamp) ClampContentToViewport();
    }

    public void CenterContent(bool keepScale = true)
    {
        if (content == null) return;
        content.anchoredPosition = Vector2.zero;
        if (!keepScale) SetScale(1f);
        else ClampContentToViewport();
    }
}
