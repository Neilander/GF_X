using UnityEngine;

/// <summary>
/// 易拉罐拖尾效果组件
/// 自动为易拉罐对象添加Trail Renderer效果
/// </summary>
public class CanTrailRenderer : MonoBehaviour
{
    [Header("拖尾设置")]
    [SerializeField] private float trailTime = 0.5f;
    [SerializeField] private float trailWidth = 0.1f;
    [SerializeField] private Color trailColor = Color.red;
    
    private TrailRenderer trailRenderer;
    
    void Start()
    {
        // 检查是否已经有Trail Renderer
        trailRenderer = GetComponent<TrailRenderer>();
        
        if (trailRenderer == null)
        {
            // 添加Trail Renderer组件
            trailRenderer = gameObject.AddComponent<TrailRenderer>();
            
            // 配置Trail Renderer参数
            ConfigureTrailRenderer();
            
            Debug.Log($"[CanTrailRenderer] 已为 {gameObject.name} 添加Trail Renderer");
        }
        else
        {
            Debug.Log($"[CanTrailRenderer] {gameObject.name} 已有Trail Renderer，重新配置");
            ConfigureTrailRenderer();
        }
    }
    
    private void ConfigureTrailRenderer()
    {
        // 基本设置 - 更圆滑的拖尾
        trailRenderer.time = trailTime;
        trailRenderer.startWidth = trailWidth;
        trailRenderer.endWidth = trailWidth * 0.3f; // 末端稍微变细，更自然
        trailRenderer.autodestruct = false;
        
        // 关键参数：让拖尾更圆滑
        trailRenderer.minVertexDistance = 0.05f; // 更小的值 = 更圆滑
        trailRenderer.emitting = true;
        trailRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // 关闭阴影，更平滑
        trailRenderer.receiveShadows = false; // 不接受阴影
        
        // 材质设置 - 使用更平滑的材质
        trailRenderer.material = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
        trailRenderer.material.color = trailColor;
        
        // 更平滑的颜色渐变设置
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { 
                new GradientColorKey(trailColor, 0.0f), 
                new GradientColorKey(trailColor, 0.7f), // 中间保持相同颜色
                new GradientColorKey(new Color(trailColor.r * 0.3f, trailColor.g * 0.3f, trailColor.b * 0.3f), 1.0f) 
            },
            new GradientAlphaKey[] { 
                new GradientAlphaKey(0.8f, 0.0f), // 起始稍微透明
                new GradientAlphaKey(0.6f, 0.3f), 
                new GradientAlphaKey(0.0f, 1.0f) 
            }
        );
        trailRenderer.colorGradient = gradient;
        
        Debug.Log($"[CanTrailRenderer] 圆滑拖尾配置完成：时间={trailTime}，宽度={trailWidth}，最小顶点距离={trailRenderer.minVertexDistance}");
    }
    
    /// <summary>
    /// 动态更新拖尾颜色
    /// </summary>
    public void SetTrailColor(Color newColor)
    {
        trailColor = newColor;
        if (trailRenderer != null)
        {
            trailRenderer.material.color = trailColor;
            
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(trailColor, 0.0f), new GradientColorKey(Color.yellow, 1.0f) },
                new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
            );
            trailRenderer.colorGradient = gradient;
        }
    }
    
    /// <summary>
    /// 动态更新拖尾时间
    /// </summary>
    public void SetTrailTime(float newTime)
    {
        trailTime = newTime;
        if (trailRenderer != null)
        {
            trailRenderer.time = trailTime;
        }
    }
    
    /// <summary>
    /// 设置拖尾的折线程度（最小顶点距离）
    /// 值越大，拖尾越接近折线效果
    /// </summary>
    public void SetTrailLinearity(float minVertexDistance)
    {
        if (trailRenderer != null)
        {
            trailRenderer.minVertexDistance = minVertexDistance;
            Debug.Log($"[CanTrailRenderer] 设置最小顶点距离: {minVertexDistance}");
        }
    }
    
    /// <summary>
    /// 清除当前拖尾
    /// </summary>
    public void ClearTrail()
    {
        if (trailRenderer != null)
        {
            trailRenderer.Clear();
        }
    }
}