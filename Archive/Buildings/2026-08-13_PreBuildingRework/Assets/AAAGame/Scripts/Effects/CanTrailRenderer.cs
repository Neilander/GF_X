using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 为对象提供轨迹拖尾效果。
/// </summary>
public class CanTrailRenderer : MonoBehaviour
{
    [Header("拖尾设置")]
    [SerializeField] private float trailTime = 0.5f;
    [SerializeField] private float trailWidth = 0.1f;
    [SerializeField] private Color trailColor = Color.red;

    private TrailRenderer trailRenderer;
    private static Material runtimeTrailMaterial;

    private void Start()
    {
        trailRenderer = GetComponent<TrailRenderer>();

        if (trailRenderer == null)
        {
            trailRenderer = gameObject.AddComponent<TrailRenderer>();
            Debug.Log($"[CanTrailRenderer] 已为 {gameObject.name} 添加 TrailRenderer");
        }

        ConfigureTrailRenderer();
    }

    private void ConfigureTrailRenderer()
    {
        trailRenderer.time = trailTime;
        trailRenderer.startWidth = trailWidth;
        trailRenderer.endWidth = trailWidth * 0.3f;
        trailRenderer.autodestruct = false;
        trailRenderer.minVertexDistance = 0.05f;
        trailRenderer.emitting = true;
        trailRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trailRenderer.receiveShadows = false;

        Material material = GetRuntimeTrailMaterial();
        if (material == null)
        {
            Debug.LogError("[CanTrailRenderer] 未找到可用拖尾 Shader，无法配置拖尾材质。");
            return;
        }

        if (trailRenderer.sharedMaterial != material)
        {
            trailRenderer.sharedMaterial = material;
        }

        trailRenderer.colorGradient = CreateGradient(trailColor);

        Debug.Log($"[CanTrailRenderer] 拖尾配置完成：time={trailTime}, width={trailWidth}, minVertexDistance={trailRenderer.minVertexDistance}, shader={material.shader.name}");
    }

    public void SetTrailColor(Color newColor)
    {
        trailColor = newColor;
        if (trailRenderer == null)
        {
            return;
        }

        trailRenderer.colorGradient = CreateGradient(trailColor);
    }

    public void SetTrailTime(float newTime)
    {
        trailTime = newTime;
        if (trailRenderer != null)
        {
            trailRenderer.time = trailTime;
        }
    }

    public void SetTrailLinearity(float minVertexDistance)
    {
        if (trailRenderer != null)
        {
            trailRenderer.minVertexDistance = minVertexDistance;
            Debug.Log($"[CanTrailRenderer] 设置 minVertexDistance={minVertexDistance}");
        }
    }

    public void ClearTrail()
    {
        if (trailRenderer != null)
        {
            trailRenderer.Clear();
        }
    }

    private static Gradient CreateGradient(Color color)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(color, 0f),
                new GradientColorKey(color, 0.7f),
                new GradientColorKey(new Color(color.r * 0.3f, color.g * 0.3f, color.b * 0.3f), 1f),
            },
            new[]
            {
                new GradientAlphaKey(0.8f, 0f),
                new GradientAlphaKey(0.6f, 0.3f),
                new GradientAlphaKey(0f, 1f),
            });
        return gradient;
    }

    private static Material GetRuntimeTrailMaterial()
    {
        if (runtimeTrailMaterial != null)
        {
            return runtimeTrailMaterial;
        }

        var config = GameEntry.GetComponent<AAAGame.Effect.EffectRuntimeConfigComponent>();
        Material material = config != null ? config.DefaultCanTrailMaterial : null;
        if (material == null)
        {
            Debug.LogError("[CanTrailRenderer] EffectRuntimeConfigComponent.DefaultCanTrailMaterial is not assigned.");
            return null;
        }

        runtimeTrailMaterial = material;
        return runtimeTrailMaterial;
    }

}
