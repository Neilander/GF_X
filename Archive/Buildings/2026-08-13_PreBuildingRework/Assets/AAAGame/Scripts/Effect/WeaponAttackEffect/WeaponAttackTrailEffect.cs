using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class WeaponAttackTrailEffect : MonoBehaviour
{
    [Header("触发规则")]
    [SerializeField] private bool onlyPlayerSide = true;
    [SerializeField, Min(0.02f)] private float defaultDuration = 0.28f;

    [Header("拖尾播放模式")]
    [SerializeField, InspectorName("常态拖尾")] private bool alwaysEmitTrail = false;
    [SerializeField, InspectorName("常态拖尾启动时清空旧轨迹")] private bool clearTrailWhenAlwaysEmitStarts = true;
    [SerializeField, InspectorName("每次攻击前清空拖尾")] private bool clearTrailBeforeAttack = true;
    [SerializeField, InspectorName("常态拖尾攻击时重启轨迹")] private bool restartAlwaysTrailOnAttack = true;

    [Header("拖尾形态")]
    [SerializeField, Min(0.01f)] private float trailTime = 0.18f;
    [SerializeField, Min(0f)] private float startWidth = 0.18f;
    [SerializeField, Min(0f)] private float endWidth = 0.02f;
    [SerializeField, Min(0.001f)] private float minVertexDistance = 0.02f;
    [SerializeField] private LineAlignment alignment = LineAlignment.View;

    [Header("拖尾颜色")]
    [SerializeField] private Color headColor = new Color(0.7f, 1f, 1f, 0.95f);
    [SerializeField] private Color tailColor = new Color(0.1f, 0.65f, 1f, 0f);
    [SerializeField, InspectorName("强制尾部Alpha为255")] private bool forceTailAlphaOpaque = true;
    [SerializeField] private Material trailMaterial;

    [Header("拖尾可见度")]
    [SerializeField, InspectorName("宽度倍率"), Min(0.1f)] private float trailWidthMultiplier = 1.45f;
    [SerializeField, InspectorName("颜色强度"), Range(0.1f, 3f)] private float trailColorIntensity = 1.05f;
    [SerializeField, InspectorName("忽略材质底色使用脚本颜色")] private bool ignoreMaterialTint = true;

    private TrailRenderer m_TrailRenderer;
    private Transform weaponAnchor;
    private Transform trailPoint;
    private Coroutine m_PlayCoroutine;
    private Coroutine m_EnableAlwaysTrailCoroutine;
    private Material m_RuntimeTrailMaterialInstance;
    private Material m_RuntimeTrailMaterialSource;
    private MaterialPropertyBlock m_TrailPropertyBlock;
    private static Material s_RuntimeFallbackMaterial;

    private void Awake()
    {
        BindPresentationOrThrow();
        EnsureTrailRenderer();
        ApplySettings();
        ApplyTrailMode(true);
    }

    private void OnEnable()
    {
        BindPresentationOrThrow();
        EnsureTrailRenderer();
        ApplySettings();
        ApplyTrailMode(true);
    }

    public static void Play(IEntityContext context, float duration = 0f)
    {
        if (context is not Component component)
            throw new System.InvalidOperationException("Weapon trail playback requires a Component entity context.");

        var effect = ResolveEffect(component);
        if (effect == null)
        {
            return;
        }

        effect.Play(duration);
    }

    public static void Stop(IEntityContext context, bool clearTrail = false)
    {
        if (context is not Component component)
            throw new System.InvalidOperationException("Weapon trail stop requires a Component entity context.");

        var effect = ResolveEffect(component);
        if (effect == null)
        {
            return;
        }

        effect.StopTrail(clearTrail);
    }

    private static WeaponAttackTrailEffect ResolveEffect(Component owner)
    {
        var effects = owner.GetComponentsInChildren<WeaponAttackTrailEffect>(true);
        if (effects.Length == 0)
            return null;
        if (effects.Length > 1)
            throw new System.InvalidOperationException($"Entity has multiple WeaponAttackTrailEffect components. entity={owner.name}, count={effects.Length}.");
        effects[0].BindPresentationOrThrow();
        return effects[0];
    }

    public void Play(float duration = 0f)
    {
        if (!CanPlayForOwner())
        {
            return;
        }

        EnsureTrailRenderer();
        ApplySettings();

        if (alwaysEmitTrail)
        {
            StartAlwaysEmitTrail(restartAlwaysTrailOnAttack || clearTrailBeforeAttack);
            return;
        }

        if (m_PlayCoroutine != null)
        {
            StopCoroutine(m_PlayCoroutine);
            m_PlayCoroutine = null;
        }

        ResetTrailRendererForFreshColor(clearTrailBeforeAttack);

        float playDuration = duration > 0f ? duration : defaultDuration;
        m_PlayCoroutine = StartCoroutine(PlayRoutine(playDuration));
    }

    public void PlayTrail()
    {
        Play();
    }

    public void PlayTrailForSeconds(float duration)
    {
        Play(duration);
    }

    public void StopTrail(bool clearTrail = false)
    {
        if (m_PlayCoroutine != null)
        {
            StopCoroutine(m_PlayCoroutine);
            m_PlayCoroutine = null;
        }

        if (m_EnableAlwaysTrailCoroutine != null)
        {
            StopCoroutine(m_EnableAlwaysTrailCoroutine);
            m_EnableAlwaysTrailCoroutine = null;
        }

        if (m_TrailRenderer == null)
        {
            m_TrailRenderer = trailPoint != null
                ? trailPoint.GetComponent<TrailRenderer>()
                : GetComponent<TrailRenderer>();
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

    public void StopTrailFromAnimationEvent()
    {
        if (alwaysEmitTrail)
        {
            StartAlwaysEmitTrail(false);
            return;
        }

        StopTrail(false);
    }

    private void OnDisable()
    {
        StopTrail(true);
    }

    private void OnDestroy()
    {
        DestroyRuntimeTrailMaterialInstance();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        EnsureTrailRenderer();
        ApplySettings();
        if (alwaysEmitTrail)
        {
            StartAlwaysEmitTrail(false);
        }
        else if (m_PlayCoroutine == null)
        {
            StopTrail(false);
        }
    }

    private IEnumerator PlayRoutine(float duration)
    {
        m_TrailRenderer.emitting = false;
        ApplySettings();
        yield return null;

        m_TrailRenderer.emitting = true;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            yield return null;
        }

        m_TrailRenderer.emitting = false;
        yield return new WaitForSeconds(trailTime);
        m_TrailRenderer.Clear();
        m_PlayCoroutine = null;
    }

    private void ApplyTrailMode(bool clearTrail)
    {
        if (alwaysEmitTrail)
        {
            StartAlwaysEmitTrail(clearTrail && clearTrailWhenAlwaysEmitStarts, clearTrail);
        }
        else
        {
            StopTrail(clearTrail);
        }
    }

    private void StartAlwaysEmitTrail(bool clearTrail)
    {
        StartAlwaysEmitTrail(clearTrail, false);
    }

    private void StartAlwaysEmitTrail(bool clearTrail, bool waitForTransformSettled)
    {
        if (m_PlayCoroutine != null)
        {
            StopCoroutine(m_PlayCoroutine);
            m_PlayCoroutine = null;
        }

        if (m_EnableAlwaysTrailCoroutine != null)
        {
            StopCoroutine(m_EnableAlwaysTrailCoroutine);
            m_EnableAlwaysTrailCoroutine = null;
        }

        EnsureTrailRenderer();
        ApplySettings();

        if (m_TrailRenderer == null)
        {
            return;
        }

        if (waitForTransformSettled)
        {
            m_EnableAlwaysTrailCoroutine = StartCoroutine(EnableAlwaysTrailAfterTransformSettled(clearTrail));
            return;
        }

        if (clearTrail)
        {
            ResetTrailRendererForFreshColor(true);
        }

        m_TrailRenderer.emitting = true;
    }

    private IEnumerator EnableAlwaysTrailAfterTransformSettled(bool clearTrail)
    {
        m_TrailRenderer.emitting = false;
        if (clearTrail)
        {
            m_TrailRenderer.Clear();
        }

        yield return null;
        yield return null;

        EnsureTrailRenderer();
        ApplySettings();

        if (m_TrailRenderer != null)
        {
            ResetTrailRendererForFreshColor(clearTrail);
            m_TrailRenderer.emitting = true;
        }

        m_EnableAlwaysTrailCoroutine = null;
    }

    private void ResetTrailRendererForFreshColor(bool clearTrail)
    {
        if (m_TrailRenderer == null)
        {
            return;
        }

        m_TrailRenderer.emitting = false;
        ApplySettings();

        if (clearTrail)
        {
            m_TrailRenderer.Clear();
        }
    }

    private bool CanPlayForOwner()
    {
        if (GetComponentInParent<SoldierEntity>() != null)
        {
            return true;
        }

        if (!onlyPlayerSide)
        {
            return true;
        }

        var creature = GetComponentInParent<GeneralCreature>();
        return creature == null || creature.Side == SideType.PlayerSide;
    }

    private void EnsureTrailRenderer()
    {
        BindPresentationOrThrow();

        if (!trailPoint.TryGetComponent(out m_TrailRenderer))
            m_TrailRenderer = trailPoint.gameObject.AddComponent<TrailRenderer>();
    }

    private void BindPresentationOrThrow()
    {
        EntityPresentationBindings bindings = null;
        Transform current = transform;
        while (current != null && bindings == null)
        {
            bindings = current.GetComponent<EntityPresentationBindings>();
            current = current.parent;
        }
        if (bindings == null)
            throw new System.InvalidOperationException($"Weapon trail is outside an entity presentation hierarchy. effect={name}.");
        bindings.RequireTrailBinding(out weaponAnchor, out trailPoint);
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
        m_TrailRenderer.widthCurve = new AnimationCurve(
            new Keyframe(0f, Mathf.Max(0f, startWidth * trailWidthMultiplier)),
            new Keyframe(1f, Mathf.Max(0f, endWidth * trailWidthMultiplier)));
        m_TrailRenderer.colorGradient = CreateGradient();
        m_TrailRenderer.alignment = alignment;
        m_TrailRenderer.textureMode = LineTextureMode.Stretch;
        m_TrailRenderer.numCornerVertices = 2;
        m_TrailRenderer.numCapVertices = 2;
        m_TrailRenderer.generateLightingData = false;
        m_TrailRenderer.shadowCastingMode = ShadowCastingMode.Off;
        m_TrailRenderer.receiveShadows = false;

        Material material = trailMaterial != null ? trailMaterial : GetRuntimeFallbackMaterial();
        material = GetRuntimeTrailMaterialInstance(material);
        if (material != null && m_TrailRenderer.sharedMaterial != material)
        {
            m_TrailRenderer.sharedMaterial = material;
        }

        ApplyTrailRendererPropertyBlock();
    }

    private Gradient CreateGradient()
    {
        var gradient = new Gradient();
        float tailAlpha = forceTailAlphaOpaque ? 1f : tailColor.a;

        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(headColor, 0f),
                new GradientColorKey(tailColor, 1f),
            },
            new[]
            {
                new GradientAlphaKey(headColor.a, 0f),
                new GradientAlphaKey(tailAlpha, 1f),
            });
        return gradient;
    }

    private void ApplyTrailRendererPropertyBlock()
    {
        if (m_TrailRenderer == null)
        {
            return;
        }

        if (m_TrailPropertyBlock == null)
        {
            m_TrailPropertyBlock = new MaterialPropertyBlock();
        }

        m_TrailPropertyBlock.Clear();
        m_TrailPropertyBlock.SetColor("_BaseColor", Color.white);
        m_TrailPropertyBlock.SetColor("_Color", Color.white);
        m_TrailPropertyBlock.SetFloat("_Intensity", Mathf.Max(0.1f, trailColorIntensity));
        m_TrailRenderer.SetPropertyBlock(m_TrailPropertyBlock);
    }

    private Material GetRuntimeTrailMaterialInstance(Material sourceMaterial)
    {
        if (sourceMaterial == null)
        {
            return null;
        }

        if (!Application.isPlaying)
        {
            return sourceMaterial;
        }

        if (m_RuntimeTrailMaterialInstance == null || m_RuntimeTrailMaterialSource != sourceMaterial)
        {
            DestroyRuntimeTrailMaterialInstance();
            m_RuntimeTrailMaterialSource = sourceMaterial;
            m_RuntimeTrailMaterialInstance = new Material(sourceMaterial)
            {
                name = $"{sourceMaterial.name}_{name}_RuntimeTrail",
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        ApplyTrailMaterialColors(m_RuntimeTrailMaterialInstance);
        return m_RuntimeTrailMaterialInstance;
    }

    private void ApplyTrailMaterialColors(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (ignoreMaterialTint)
        {
            SetMaterialColor(material, "_BaseColor", Color.white);
            SetMaterialColor(material, "_Color", Color.white);
        }

        if (material.HasProperty("_Intensity"))
        {
            material.SetFloat("_Intensity", Mathf.Max(0.1f, trailColorIntensity));
        }
    }

    private static void SetMaterialColor(Material material, string propertyName, Color color)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            material.SetColor(propertyName, color);
        }
    }

    private void DestroyRuntimeTrailMaterialInstance()
    {
        if (m_RuntimeTrailMaterialInstance == null)
        {
            return;
        }

        Destroy(m_RuntimeTrailMaterialInstance);
        m_RuntimeTrailMaterialInstance = null;
        m_RuntimeTrailMaterialSource = null;
    }

    private static Material GetRuntimeFallbackMaterial()
    {
        if (s_RuntimeFallbackMaterial != null)
        {
            return s_RuntimeFallbackMaterial;
        }

        Shader shader = AAAGame.Effect.EffectShaderAssetLoader.TryGet(AAAGame.Effect.EffectShaderAssetLoader.WeaponAttackTrailShaderAssetPath);

        if (shader == null)
        {
            Debug.LogError($"[WeaponAttackTrailEffect] Shader is not ready: {AAAGame.Effect.EffectShaderAssetLoader.WeaponAttackTrailShaderAssetPath}");
            return null;
        }

        s_RuntimeFallbackMaterial = new Material(shader)
        {
            name = "Runtime_WeaponAttackTrailMaterial",
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
