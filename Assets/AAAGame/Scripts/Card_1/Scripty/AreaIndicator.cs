using UnityEngine;
using DG.Tweening;
using UnityGameFramework.Runtime;

/// <summary>
/// 区域指示器 - 显示可放置区域和禁止区域的菲涅尔光效果
/// </summary>
public class AreaIndicator : MonoBehaviour
{
    [Header("指示器对象")]
    [SerializeField] private GameObject validAreaIndicator; // 可放置区域指示器
    [SerializeField] private GameObject invalidAreaIndicator; // 禁止区域指示器
    
    [Header("材质设置")]
    [SerializeField] private Material validMaterial; // 可放置区域材质（绿色菲涅尔）
    [SerializeField] private Material invalidMaterial; // 禁止区域材质（红色菲涅尔）
    
    [Header("颜色设置")]
    [SerializeField] private Color validColor = new Color(0, 1, 0, 0.5f); // 绿色半透明
    [SerializeField] private Color invalidColor = new Color(1, 0, 0, 0.5f); // 红色半透明
    
    [Header("动画设置")]
    [SerializeField] private float fadeInDuration = 0.3f;
    [SerializeField] private float fadeOutDuration = 0.2f;
    [SerializeField] private float yOffset = 0.05f; // 离地高度
    
    private Renderer validRenderer;
    private Renderer invalidRenderer;
    private MaterialPropertyBlock validPropertyBlock;
    private MaterialPropertyBlock invalidPropertyBlock;
    
    private Tween validFadeTween;
    private Tween invalidFadeTween;
    
    private bool isValid = true;
    private bool isShowing = false;

    void Awake()
    {
        InitializeIndicators();
    }

    /// <summary>
    /// 初始化指示器
    /// </summary>
    private void InitializeIndicators()
    {
        // 如果没有配置指示器对象，创建默认的
        if (validAreaIndicator == null)
        {
            validAreaIndicator = CreateDefaultIndicator("ValidAreaIndicator");
        }
        
        if (invalidAreaIndicator == null)
        {
            invalidAreaIndicator = CreateDefaultIndicator("InvalidAreaIndicator");
        }
        
        // 获取Renderer
        validRenderer = validAreaIndicator.GetComponent<Renderer>();
        invalidRenderer = invalidAreaIndicator.GetComponent<Renderer>();
        
        // 创建MaterialPropertyBlock
        validPropertyBlock = new MaterialPropertyBlock();
        invalidPropertyBlock = new MaterialPropertyBlock();
        
        // 设置材质
        if (validMaterial != null && validRenderer != null)
        {
            validRenderer.material = validMaterial;
        }
        
        if (invalidMaterial != null && invalidRenderer != null)
        {
            invalidRenderer.material = invalidMaterial;
        }
        
        // 初始隐藏
        Hide();
    }

    /// <summary>
    /// 创建默认指示器（圆形平面）
    /// </summary>
    private GameObject CreateDefaultIndicator(string name)
    {
        GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        indicator.name = name;
        indicator.transform.SetParent(transform);
        indicator.transform.localPosition = Vector3.zero;
        indicator.transform.localScale = new Vector3(3f, 0.01f, 3f); // 扁平圆形
        
        // 移除碰撞体
        Collider collider = indicator.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }
        
        return indicator;
    }

    /// <summary>
    /// 显示指示器
    /// </summary>
    /// <param name="valid">是否为有效区域</param>
    public void Show(bool valid = true)
    {
        isShowing = true;
        isValid = valid;
        
        if (valid)
        {
            // 显示绿色可放置区域
            ShowValidArea();
            HideInvalidArea();
        }
        else
        {
            // 显示红色禁止区域
            ShowInvalidArea();
            HideValidArea();
        }
    }

    /// <summary>
    /// 隐藏指示器
    /// </summary>
    public void Hide()
    {
        isShowing = false;
        HideValidArea();
        HideInvalidArea();
    }

    /// <summary>
    /// 更新指示器位置
    /// </summary>
    public void UpdatePosition(Vector3 worldPosition)
    {
        transform.position = worldPosition + Vector3.up * yOffset;
    }

    /// <summary>
    /// 设置指示器合法性
    /// </summary>
    public void SetValid(bool valid)
    {
        if (isValid == valid || !isShowing) return;
        
        isValid = valid;
        
        if (valid)
        {
            ShowValidArea();
            HideInvalidArea();
        }
        else
        {
            ShowInvalidArea();
            HideValidArea();
        }
    }

    /// <summary>
    /// 显示可放置区域
    /// </summary>
    private void ShowValidArea()
    {
        if (validAreaIndicator == null) return;
        
        validAreaIndicator.SetActive(true);
        
        // 淡入动画
        validFadeTween?.Kill();
        validFadeTween = DOTween.To(
            () => GetAlpha(validRenderer, validPropertyBlock),
            alpha => SetAlpha(validRenderer, validPropertyBlock, validColor, alpha),
            validColor.a,
            fadeInDuration
        ).SetEase(Ease.OutQuad);
    }

    /// <summary>
    /// 隐藏可放置区域
    /// </summary>
    private void HideValidArea()
    {
        if (validAreaIndicator == null) return;
        
        validFadeTween?.Kill();
        validFadeTween = DOTween.To(
            () => GetAlpha(validRenderer, validPropertyBlock),
            alpha => SetAlpha(validRenderer, validPropertyBlock, validColor, alpha),
            0f,
            fadeOutDuration
        ).SetEase(Ease.InQuad).OnComplete(() =>
        {
            validAreaIndicator.SetActive(false);
        });
    }

    /// <summary>
    /// 显示禁止区域
    /// </summary>
    private void ShowInvalidArea()
    {
        if (invalidAreaIndicator == null) return;
        
        invalidAreaIndicator.SetActive(true);
        
        // 淡入动画
        invalidFadeTween?.Kill();
        invalidFadeTween = DOTween.To(
            () => GetAlpha(invalidRenderer, invalidPropertyBlock),
            alpha => SetAlpha(invalidRenderer, invalidPropertyBlock, invalidColor, alpha),
            invalidColor.a,
            fadeInDuration
        ).SetEase(Ease.OutQuad);
    }

    /// <summary>
    /// 隐藏禁止区域
    /// </summary>
    private void HideInvalidArea()
    {
        if (invalidAreaIndicator == null) return;
        
        invalidFadeTween?.Kill();
        invalidFadeTween = DOTween.To(
            () => GetAlpha(invalidRenderer, invalidPropertyBlock),
            alpha => SetAlpha(invalidRenderer, invalidPropertyBlock, invalidColor, alpha),
            0f,
            fadeOutDuration
        ).SetEase(Ease.InQuad).OnComplete(() =>
        {
            invalidAreaIndicator.SetActive(false);
        });
    }

    /// <summary>
    /// 获取当前Alpha值
    /// </summary>
    private float GetAlpha(Renderer renderer, MaterialPropertyBlock propertyBlock)
    {
        if (renderer == null) return 0f;
        
        renderer.GetPropertyBlock(propertyBlock);
        Color color = propertyBlock.GetColor("_BaseColor");
        return color.a;
    }

    /// <summary>
    /// 设置Alpha值
    /// </summary>
    private void SetAlpha(Renderer renderer, MaterialPropertyBlock propertyBlock, Color baseColor, float alpha)
    {
        if (renderer == null) return;
        
        Color color = baseColor;
        color.a = alpha;
        
        renderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_BaseColor", color);
        propertyBlock.SetColor("_EmissionColor", color * 2f); // 发光效果
        renderer.SetPropertyBlock(propertyBlock);
    }

    void OnDestroy()
    {
        validFadeTween?.Kill();
        invalidFadeTween?.Kill();
    }
}
