using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(TrailRenderer))]
public class CanTrailEffect : MonoBehaviour
{
    [Header("拖尾开关")]
    [SerializeField] private bool playOnEnable = true;
    [SerializeField] private bool clearOnEnable = true;
    [SerializeField] private bool clearOnDisable = true;
    [SerializeField] private bool delayEmitOneFrame = true;

    [Header("拖尾形态")]
    [SerializeField, Min(0.01f)] private float trailTime = 0.35f;
    [SerializeField, Min(0f)] private float startWidth = 0.18f;
    [SerializeField, Min(0f)] private float endWidth = 0.02f;
    [SerializeField, Min(0.001f)] private float minVertexDistance = 0.03f;
    [SerializeField] private LineAlignment alignment = LineAlignment.View;
    [SerializeField] private LineTextureMode textureMode = LineTextureMode.Stretch;

    [Header("拖尾颜色")]
    [SerializeField] private Color headColor = new Color(1f, 0.88f, 0.38f, 0.78f);
    [SerializeField] private Color tailColor = new Color(1f, 0.38f, 0.08f, 0f);
    [SerializeField] private Material trailMaterial;

    private TrailRenderer m_TrailRenderer;
    private Coroutine m_EnableTrailCoroutine;
    private static Material s_RuntimeFallbackMaterial;

    private void Awake()
    {
        EnsureTrailRenderer();
        ApplySettings();
    }

    private void OnEnable()
    {
        EnsureTrailRenderer();
        ApplySettings();

        if (clearOnEnable)
        {
            m_TrailRenderer.Clear();
        }

        m_TrailRenderer.emitting = false;

        if (!playOnEnable)
        {
            return;
        }

        if (delayEmitOneFrame)
        {
            m_EnableTrailCoroutine = StartCoroutine(EnableTrailAfterFrame());
            return;
        }

        m_TrailRenderer.emitting = true;
    }

    private void OnDisable()
    {
        if (m_EnableTrailCoroutine != null)
        {
            StopCoroutine(m_EnableTrailCoroutine);
            m_EnableTrailCoroutine = null;
        }

        if (m_TrailRenderer == null)
        {
            return;
        }

        m_TrailRenderer.emitting = false;

        if (clearOnDisable)
        {
            m_TrailRenderer.Clear();
        }
    }

    private void Reset()
    {
        EnsureTrailRenderer();
        ApplySettings();
    }

    private void OnValidate()
    {
        m_TrailRenderer = GetComponent<TrailRenderer>();

        if (m_TrailRenderer != null)
        {
            ApplySettings();
        }
    }

    public void PlayTrail()
    {
        EnsureTrailRenderer();
        ApplySettings();
        m_TrailRenderer.Clear();
        m_TrailRenderer.emitting = true;
    }

    public void StopTrail(bool clearTrail = true)
    {
        if (m_TrailRenderer == null)
        {
            m_TrailRenderer = GetComponent<TrailRenderer>();
        }

        if (m_TrailRenderer == null)
        {
            return;
        }

        m_TrailRenderer.emitting = false;

        if (clearTrail)
        {
            m_TrailRenderer.Clear();
        }
    }

    private IEnumerator EnableTrailAfterFrame()
    {
        yield return null;

        EnsureTrailRenderer();
        ApplySettings();

        if (clearOnEnable)
        {
            m_TrailRenderer.Clear();
        }

        m_TrailRenderer.emitting = true;
        m_EnableTrailCoroutine = null;
    }

    private void EnsureTrailRenderer()
    {
        if (m_TrailRenderer != null)
        {
            return;
        }

        if (!TryGetComponent(out m_TrailRenderer))
        {
            m_TrailRenderer = gameObject.AddComponent<TrailRenderer>();
        }
    }

    private void ApplySettings()
    {
        if (m_TrailRenderer == null)
        {
            return;
        }

        m_TrailRenderer.time = Mathf.Max(0.01f, trailTime);
        m_TrailRenderer.minVertexDistance = Mathf.Max(0.001f, minVertexDistance);
        m_TrailRenderer.widthMultiplier = 1f;
        m_TrailRenderer.widthCurve = CreateWidthCurve();
        m_TrailRenderer.colorGradient = CreateGradient();
        m_TrailRenderer.alignment = alignment;
        m_TrailRenderer.textureMode = textureMode;
        m_TrailRenderer.numCornerVertices = 2;
        m_TrailRenderer.numCapVertices = 2;
        m_TrailRenderer.generateLightingData = false;
        m_TrailRenderer.shadowCastingMode = ShadowCastingMode.Off;
        m_TrailRenderer.receiveShadows = false;

        Material material = trailMaterial != null ? trailMaterial : GetRuntimeFallbackMaterial();
        if (material != null && m_TrailRenderer.sharedMaterial != material)
        {
            m_TrailRenderer.sharedMaterial = material;
        }
    }

    private AnimationCurve CreateWidthCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, Mathf.Max(0f, startWidth)),
            new Keyframe(1f, Mathf.Max(0f, endWidth)));
    }

    private Gradient CreateGradient()
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(headColor, 0f),
                new GradientColorKey(tailColor, 1f),
            },
            new[]
            {
                new GradientAlphaKey(headColor.a, 0f),
                new GradientAlphaKey(tailColor.a, 1f),
            });
        return gradient;
    }

    private static Material GetRuntimeFallbackMaterial()
    {
        if (s_RuntimeFallbackMaterial != null)
        {
            return s_RuntimeFallbackMaterial;
        }

        Shader shader = Shader.Find("AAAGame/Effec/CanTrailAdditive")
            ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Sprites/Default");

        if (shader == null)
        {
            return null;
        }

        s_RuntimeFallbackMaterial = new Material(shader)
        {
            name = "Runtime_CanTrailMaterial",
            hideFlags = HideFlags.HideAndDontSave,
        };

        if (s_RuntimeFallbackMaterial.HasProperty("_BaseColor"))
        {
            s_RuntimeFallbackMaterial.SetColor("_BaseColor", Color.white);
        }

        if (s_RuntimeFallbackMaterial.HasProperty("_Color"))
        {
            s_RuntimeFallbackMaterial.SetColor("_Color", Color.white);
        }

        return s_RuntimeFallbackMaterial;
    }
}
