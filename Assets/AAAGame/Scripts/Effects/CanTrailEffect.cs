using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 易拉罐拖尾效果实体
/// 在易拉罐单位生成时自动附加拖尾效果
/// </summary>
public class CanTrailEffect : EntityLogic
{
    [SerializeField] private float trailTime = 0.5f;
    [SerializeField] private float trailWidth = 0.1f;
    [SerializeField] private Color trailColor = Color.red;
    
    private TrailRenderer trailRenderer;
    
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        
        // 添加Trail Renderer组件
        trailRenderer = gameObject.AddComponent<TrailRenderer>();
        
        // 配置Trail Renderer
        ConfigureTrailRenderer();
        
        Log.Info($"[CanTrailEffect] 已为 {gameObject.name} 添加拖尾效果");
    }
    
    protected override void OnHide(bool isShutdown, object userData)
    {
        // 清理Trail Renderer
        if (trailRenderer != null)
        {
            Destroy(trailRenderer);
            trailRenderer = null;
        }
        
        base.OnHide(isShutdown, userData);
    }
    
    private void ConfigureTrailRenderer()
    {
        if (trailRenderer == null) return;
        
        // 基本设置 - 更圆滑的拖尾
        trailRenderer.time = trailTime;
        trailRenderer.startWidth = trailWidth;
        trailRenderer.endWidth = trailWidth * 0.3f; // 末端稍微变细，更自然
        trailRenderer.autodestruct = false;
        trailRenderer.minVertexDistance = 0.05f; // 更小的值 = 更圆滑
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
        
        Log.Info($"[CanTrailEffect] 圆滑拖尾配置完成：时间={trailTime}，宽度={trailWidth}");
    }
    
    /// <summary>
    /// 启用/禁用拖尾效果
    /// </summary>
    public void SetTrailEnabled(bool enabled)
    {
        if (trailRenderer != null)
        {
            trailRenderer.enabled = enabled;
        }
    }
    
    /// <summary>
    /// 设置拖尾颜色
    /// </summary>
    public void SetTrailColor(Color color)
    {
        trailColor = color;
        if (trailRenderer != null)
        {
            trailRenderer.material.color = trailColor;
            
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { 
                    new GradientColorKey(trailColor, 0.0f), 
                    new GradientColorKey(new Color(trailColor.r * 0.5f, trailColor.g * 0.5f, trailColor.b * 0.5f), 1.0f) 
                },
                new GradientAlphaKey[] { 
                    new GradientAlphaKey(1.0f, 0.0f), 
                    new GradientAlphaKey(0.0f, 1.0f) 
                }
            );
            trailRenderer.colorGradient = gradient;
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