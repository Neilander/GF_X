using UnityEngine;

/// <summary>
/// 将一个“固定世界坐标点”投影到 UI，并移动指定 RectTransform。
/// 注意：worldPoint 通常在打开 UI 时设置一次（例如目标初始提示点）。
/// </summary>
[DefaultExecutionOrder(10000)]
public class UIFollowWorldPoint : MonoBehaviour
{
    [SerializeField] private RectTransform rectToMove;
    [SerializeField] private Vector2 uiOffset = new Vector2(0, 80);

    private Vector3 _worldPoint;
    private bool _hasPoint;
    private int _lastLateUpdateFrame = -1;

    public void Init(RectTransform rect, Vector3 worldPoint, Vector2 offset)
    {
        rectToMove = rect;
        _worldPoint = worldPoint;
        _hasPoint = true;
        uiOffset = offset;
    }

    private void OnEnable()
    {
        Canvas.willRenderCanvases -= OnWillRenderCanvases;
        Canvas.willRenderCanvases += OnWillRenderCanvases;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= OnWillRenderCanvases;
    }

    private void OnWillRenderCanvases()
    {
        // 强制在渲染前用最终相机状态再校正一次。
        UpdatePosition(force: true);
    }

    private void LateUpdate()
    {
        UpdatePosition(force: false);
    }

    private void UpdatePosition(bool force)
    {
        if (!force)
        {
            if (Time.frameCount == _lastLateUpdateFrame)
                return;
            _lastLateUpdateFrame = Time.frameCount;
        }

        if (!_hasPoint || rectToMove == null)
            return;

        var parentRect = rectToMove.parent as RectTransform;
        if (parentRect == null)
            return;

        Vector3 localPoint = GF.UI.PositionWorldToUI(_worldPoint, parentRect);
        rectToMove.anchoredPosition = (Vector2)localPoint + uiOffset;
    }
}
