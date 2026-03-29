using UnityEngine;

/// <summary>
/// 卡牌放置指示器 - 显示放置位置和合法性
/// </summary>
public class CardPlacementIndicator : MonoBehaviour
{
    [Header("指示器设置")]
    [SerializeField] private GameObject indicatorObject;
    [SerializeField] private Renderer indicatorRenderer;
    [SerializeField] private Color validColor = Color.green;
    [SerializeField] private Color invalidColor = Color.red;
    [SerializeField] private float yOffset = 0.1f; // 离地高度

    private MaterialPropertyBlock propertyBlock;

    void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();

        if (indicatorObject == null)
        {
            indicatorObject = gameObject;
        }

        if (indicatorRenderer == null)
        {
            indicatorRenderer = GetComponent<Renderer>();
        }

        // 初始隐藏
        Hide();
    }

    /// <summary>
    /// 显示指示器
    /// </summary>
    public void Show()
    {
        if (indicatorObject != null)
        {
            indicatorObject.SetActive(true);
        }
    }

    /// <summary>
    /// 隐藏指示器
    /// </summary>
    public void Hide()
    {
        if (indicatorObject != null)
        {
            indicatorObject.SetActive(false);
        }
    }

    /// <summary>
    /// 更新指示器位置
    /// </summary>
    public void UpdatePosition(Vector3 worldPosition)
    {
        transform.position = worldPosition + Vector3.up * yOffset;
    }

    /// <summary>
    /// 设置指示器合法性（颜色）
    /// </summary>
    public void SetValid(bool isValid)
    {
        if (indicatorRenderer == null) return;

        Color targetColor = isValid ? validColor : invalidColor;

        // 使用 MaterialPropertyBlock 避免创建新材质实例
        indicatorRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_Color", targetColor);
        indicatorRenderer.SetPropertyBlock(propertyBlock);
    }
}
