using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 使用 UI Image 连接两点的简单连线。
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Image))]
public class TechEdgeLine : MonoBehaviour
{
    public RectTransform from;
    public RectTransform to;

    [SerializeField] private float thickness = 4f;

    private RectTransform m_Rect;

    private void Awake()
    {
        m_Rect = GetComponent<RectTransform>();
        var img = GetComponent<Image>();
        if (img != null)
        {
            img.raycastTarget = false;
        }
    }

    private void LateUpdate()
    {
        UpdateLine();
    }

    public void UpdateLine()
    {
        if (from == null || to == null) return;
        if (m_Rect == null) m_Rect = GetComponent<RectTransform>();

        Vector2 a = from.anchoredPosition;
        Vector2 b = to.anchoredPosition;
        Vector2 dir = b - a;
        float len = dir.magnitude;
        if (len < 0.001f) len = 0.001f;

        m_Rect.anchoredPosition = (a + b) * 0.5f;
        m_Rect.sizeDelta = new Vector2(len, thickness);

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        m_Rect.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
}
