using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class WeaponAttackTrailEffect : MonoBehaviour
{
    private const string RuntimeTrailPointName = "WeaponAttackTrail_Tip";

    [Header("触发规则")]
    [SerializeField] private bool onlyPlayerSide = true;
    [SerializeField] private bool allowRuntimeAutoFallback = false;
    [SerializeField, Min(0.02f)] private float defaultDuration = 0.28f;

    [Header("拖尾挂点")]
    [SerializeField] private Transform weaponAnchor;
    [SerializeField] private Transform trailPoint;
    [SerializeField] private bool autoCreateTrailPoint = true;
    [SerializeField] private Vector3 handLocalOffset = new Vector3(0f, 0.38f, 0f);
    [SerializeField] private Vector3 fallbackLocalCenter = new Vector3(0.35f, 1.15f, 0.45f);

    [Header("兜底挥动")]
    [SerializeField] private bool useProceduralFallback = true;
    [SerializeField, Min(0.05f)] private float fallbackSwingWidth = 0.9f;
    [SerializeField, Min(0.05f)] private float fallbackSwingHeight = 0.42f;

    [Header("拖尾形态")]
    [SerializeField, Min(0.01f)] private float trailTime = 0.18f;
    [SerializeField, Min(0f)] private float startWidth = 0.18f;
    [SerializeField, Min(0f)] private float endWidth = 0.02f;
    [SerializeField, Min(0.001f)] private float minVertexDistance = 0.02f;
    [SerializeField] private LineAlignment alignment = LineAlignment.View;

    [Header("拖尾颜色")]
    [SerializeField] private Color headColor = new Color(0.7f, 1f, 1f, 0.95f);
    [SerializeField] private Color tailColor = new Color(0.1f, 0.65f, 1f, 0f);
    [SerializeField] private Material trailMaterial;

    private TrailRenderer m_TrailRenderer;
    private Coroutine m_PlayCoroutine;
    private bool m_UsingFallbackPoint;
    private static Material s_RuntimeFallbackMaterial;

    private void Awake()
    {
        if (!IsConfiguredForStartup())
        {
            StopTrail(true);
            return;
        }

        EnsureTrailRenderer();
        ApplySettings();
        StopTrail(true);
    }

    private void OnEnable()
    {
        if (!IsConfiguredForStartup())
        {
            StopTrail(true);
            return;
        }

        EnsureTrailRenderer();
        ApplySettings();
        StopTrail(true);
    }

    public static void Play(IEntityContext context, float duration = 0f)
    {
        if (context is not Component component)
        {
            return;
        }

        var effect = ResolveEffect(component);
        if (effect == null)
        {
            return;
        }

        effect.Play(duration);
    }

    private static WeaponAttackTrailEffect ResolveEffect(Component owner)
    {
        var effects = owner.GetComponentsInChildren<WeaponAttackTrailEffect>(true);
        if (effects == null || effects.Length == 0)
        {
            return null;
        }

        WeaponAttackTrailEffect first = null;
        WeaponAttackTrailEffect firstManual = null;

        for (int i = 0; i < effects.Length; i++)
        {
            WeaponAttackTrailEffect effect = effects[i];
            if (effect == null)
            {
                continue;
            }

            if (!effect.IsConfiguredForPlayback(owner.transform))
            {
                continue;
            }

            first ??= effect;

            if (effect.HasManualBinding)
            {
                firstManual ??= effect;
                if (effect.gameObject != owner.gameObject)
                {
                    return effect;
                }
            }
        }
        

        return firstManual != null ? firstManual : first;
    }

    public void Play(float duration = 0f)
    {
        if (!CanPlayForOwner())
        {
            return;
        }

        EnsureTrailRenderer();
        ApplySettings();

        if (m_PlayCoroutine != null)
        {
            StopCoroutine(m_PlayCoroutine);
        }

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
        StopTrail(false);
    }

    private void OnDisable()
    {
        StopTrail(true);
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        EnsureTrailRenderer();
        ApplySettings();
    }

    private IEnumerator PlayRoutine(float duration)
    {
        m_TrailRenderer.emitting = false;
        m_TrailRenderer.Clear();
        yield return null;

        m_TrailRenderer.emitting = true;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            if (m_UsingFallbackPoint && useProceduralFallback)
            {
                UpdateProceduralTrailPoint(Mathf.Clamp01(elapsed / duration));
            }

            yield return null;
        }

        m_TrailRenderer.emitting = false;
        yield return new WaitForSeconds(trailTime);
        m_TrailRenderer.Clear();
        m_PlayCoroutine = null;
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
        EnsureTrailPoint();

        if (trailPoint == null)
        {
            return;
        }

        if (!trailPoint.TryGetComponent(out m_TrailRenderer))
        {
            m_TrailRenderer = trailPoint.gameObject.AddComponent<TrailRenderer>();
        }
    }

    private void EnsureTrailPoint()
    {
        if (trailPoint != null)
        {
            m_UsingFallbackPoint = false;
            return;
        }

        if (weaponAnchor == null && !allowRuntimeAutoFallback)
        {
            trailPoint = transform;
            m_UsingFallbackPoint = false;
            return;
        }

        if (!autoCreateTrailPoint)
        {
            return;
        }

        Transform anchor = ResolveWeaponAnchor();
        if (anchor == null)
        {
            anchor = transform;
        }

        Transform existing = anchor.Find(RuntimeTrailPointName);
        GameObject pointObject;
        if (existing != null)
        {
            pointObject = existing.gameObject;
        }
        else
        {
            pointObject = new GameObject(RuntimeTrailPointName);
            pointObject.transform.SetParent(anchor, false);
        }

        trailPoint = pointObject.transform;
        m_UsingFallbackPoint = IsFallbackParent(anchor);
        trailPoint.localRotation = Quaternion.identity;
        trailPoint.localScale = Vector3.one;
        trailPoint.localPosition = m_UsingFallbackPoint ? fallbackLocalCenter : handLocalOffset;
    }

    private Transform ResolveWeaponAnchor()
    {
        if (weaponAnchor != null)
        {
            return weaponAnchor;
        }

        var creature = GetComponent<GeneralCreature>();
        Animator animator = creature != null ? creature.animator : GetComponentInChildren<Animator>(true);

        if (animator != null && animator.isHuman)
        {
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (rightHand != null)
            {
                return rightHand;
            }
        }

        Transform namedAnchor = FindNamedAnchor(transform);
        if (namedAnchor != null)
        {
            return namedAnchor;
        }

        return creature != null && creature.display != null ? creature.display : transform;
    }

    private Transform FindNamedAnchor(Transform root)
    {
        Transform bestHand = null;
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            string lower = child.name.ToLowerInvariant();

            if (lower.Contains("weapon") || lower.Contains("sword") || lower.Contains("blade") || lower.Contains("knife")
                || child.name.Contains("武器") || child.name.Contains("刀") || child.name.Contains("剑"))
            {
                return child;
            }

            if (bestHand == null
                && (lower.Contains("righthand") || lower.Contains("right_hand") || lower.Contains("r hand")
                    || lower.Contains("hand_r") || lower.Contains("mixamorig:righthand") || child.name.Contains("右手")))
            {
                bestHand = child;
            }
        }

        return bestHand;
    }

    private bool IsFallbackParent(Transform parent)
    {
        return parent == null || parent == transform || parent.name == "Display";
    }

    private bool IsConfiguredForPlayback(Transform ownerRoot)
    {
        if (weaponAnchor != null || HasExplicitTrailPoint || allowRuntimeAutoFallback)
        {
            return true;
        }

        return transform != ownerRoot;
    }

    private bool IsConfiguredForStartup()
    {
        return weaponAnchor != null || HasExplicitTrailPoint || allowRuntimeAutoFallback;
    }

    private bool HasManualBinding => weaponAnchor != null || HasExplicitTrailPoint || !autoCreateTrailPoint || allowRuntimeAutoFallback;

    private bool HasExplicitTrailPoint => trailPoint != null && trailPoint.name != RuntimeTrailPointName;

    private void UpdateProceduralTrailPoint(float normalizedTime)
    {
        float angle = Mathf.Lerp(-80f, 100f, normalizedTime) * Mathf.Deg2Rad;
        float x = Mathf.Cos(angle) * fallbackSwingWidth * 0.5f;
        float y = Mathf.Sin(angle) * fallbackSwingHeight;
        float z = Mathf.Lerp(0.36f, 0.72f, normalizedTime);
        trailPoint.localPosition = fallbackLocalCenter + new Vector3(x, y, z);
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
            new Keyframe(0f, Mathf.Max(0f, startWidth)),
            new Keyframe(1f, Mathf.Max(0f, endWidth)));
        m_TrailRenderer.colorGradient = CreateGradient();
        m_TrailRenderer.alignment = alignment;
        m_TrailRenderer.textureMode = LineTextureMode.Stretch;
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

        Shader shader = Shader.Find("AAAGame/Effec/WeaponAttackTrailAdditive")
            ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Sprites/Default");

        if (shader == null)
        {
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
