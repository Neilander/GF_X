wusing UnityEngine;
using UnityEngine.Rendering;

namespace AAAGame.Effec
{
    [DisallowMultipleComponent]
    public sealed class UnitDeathDissolveEffect : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static Material s_SmokeMaterial;

        [Header("死亡消散特效")]
        [SerializeField, InspectorName("中心偏移")] private Vector3 centerOffset = new Vector3(0f, 0.45f, 0f);
        [SerializeField, InspectorName("大小倍率"), Min(0.1f)] private float sizeMultiplier = 1f;
        [SerializeField, InspectorName("持续时间"), Min(0.1f)] private float duration = 1.2f;

        [Header("白色圆球")]
        [SerializeField, InspectorName("圆球初始大小"), Min(0.01f)] private float sphereStartScale = 0.25f;
        [SerializeField, InspectorName("圆球结束大小"), Min(0.01f)] private float sphereEndScale = 1.35f;
        [SerializeField, InspectorName("圆球颜色")] private Color sphereColor = new Color(1f, 1f, 1f, 0.85f);

        [Header("消散烟雾")]
        [SerializeField, InspectorName("烟雾粒子数量"), Min(1)] private int smokeParticleCount = 28;
        [SerializeField, InspectorName("烟雾扩散半径"), Min(0.01f)] private float smokeRadius = 0.45f;
        [SerializeField, InspectorName("烟雾上升速度"), Min(0f)] private float smokeUpSpeed = 0.35f;
        [SerializeField, InspectorName("烟雾颜色")] private Color smokeColor = new Color(1f, 1f, 1f, 0.72f);

        private bool m_PlayedThisLife;

        private void OnEnable()
        {
            m_PlayedThisLife = false;
        }

        public void PlayDeathEffect()
        {
            if (m_PlayedThisLife)
            {
                return;
            }

            m_PlayedThisLife = true;

            ResolveEffectTransform(out Vector3 center, out float scale);
            PlayAt(center, scale, this);
        }

        public static void PlayFor(GameObject owner)
        {
            if (owner == null)
            {
                return;
            }

            UnitDeathDissolveEffect effect = owner.GetComponent<UnitDeathDissolveEffect>();
            if (effect == null)
            {
                effect = owner.AddComponent<UnitDeathDissolveEffect>();
            }

            effect.PlayDeathEffect();
        }

        public static void PlayAt(Vector3 worldPosition, float worldScale = 1f)
        {
            CreateRuntimeEffect(worldPosition, Mathf.Max(0.01f, worldScale), null);
        }

        private static void PlayAt(Vector3 worldPosition, float worldScale, UnitDeathDissolveEffect source)
        {
            CreateRuntimeEffect(worldPosition, Mathf.Max(0.01f, worldScale), source);
        }

        private static void CreateRuntimeEffect(Vector3 worldPosition, float worldScale, UnitDeathDissolveEffect source)
        {
            GameObject root = new GameObject("UnitDeathDissolveEffect_Runtime");
            root.transform.position = worldPosition;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            DeathEffectRuntime runtime = root.AddComponent<DeathEffectRuntime>();
            runtime.Begin(source, worldScale);
        }

        private void ResolveEffectTransform(out Vector3 center, out float scale)
        {
            Bounds bounds;
            if (TryGetRendererBounds(out bounds) || TryGetColliderBounds(out bounds))
            {
                center = bounds.center + centerOffset;
                scale = Mathf.Max(0.35f, bounds.extents.magnitude * 0.9f) * sizeMultiplier;
                return;
            }

            center = transform.position + centerOffset;
            scale = Mathf.Max(0.35f, transform.lossyScale.magnitude * 0.35f) * sizeMultiplier;
        }

        private bool TryGetRendererBounds(out Bounds bounds)
        {
            bounds = default;
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        private bool TryGetColliderBounds(out Bounds bounds)
        {
            bounds = default;
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            bool hasBounds = false;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return hasBounds;
        }

        private static Material CreateTransparentMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default");

            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader)
            {
                name = "Runtime_UnitDeathWhiteSphere",
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = (int)RenderQueue.Transparent,
            };

            SetupTransparentMaterial(material);
            SetMaterialColor(material, color);
            return material;
        }

        private static Material GetSmokeMaterial()
        {
            if (s_SmokeMaterial != null)
            {
                return s_SmokeMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                return null;
            }

            s_SmokeMaterial = new Material(shader)
            {
                name = "Runtime_UnitDeathSmokeMaterial",
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = (int)RenderQueue.Transparent,
            };

            SetupTransparentMaterial(s_SmokeMaterial);
            SetMaterialColor(s_SmokeMaterial, Color.white);
            return s_SmokeMaterial;
        }

        private static void SetupTransparentMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty(SrcBlendId))
            {
                material.SetInt(SrcBlendId, (int)BlendMode.SrcAlpha);
            }

            if (material.HasProperty(DstBlendId))
            {
                material.SetInt(DstBlendId, (int)BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty(ZWriteId))
            {
                material.SetInt(ZWriteId, 0);
            }

            if (material.HasProperty(SurfaceId))
            {
                material.SetFloat(SurfaceId, 1f);
            }

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, color);
            }

            if (material.HasProperty(ColorId))
            {
                material.SetColor(ColorId, color);
            }
        }

        private sealed class DeathEffectRuntime : MonoBehaviour
        {
            private GameObject m_Sphere;
            private Material m_SphereMaterial;
            private float m_Duration;
            private float m_StartScale;
            private float m_EndScale;
            private Color m_SphereColor;

            public void Begin(UnitDeathDissolveEffect source, float worldScale)
            {
                m_Duration = source != null ? Mathf.Max(0.1f, source.duration) : 1.2f;
                m_StartScale = (source != null ? source.sphereStartScale : 0.25f) * worldScale;
                m_EndScale = (source != null ? source.sphereEndScale : 1.35f) * worldScale;
                m_SphereColor = source != null ? source.sphereColor : new Color(1f, 1f, 1f, 0.85f);

                CreateSphere();
                CreateSmoke(source, worldScale);
                StartCoroutine(PlayRoutine());
            }

            private void CreateSphere()
            {
                m_Sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                m_Sphere.name = "WhiteDissolveSphere";
                m_Sphere.transform.SetParent(transform, false);
                m_Sphere.transform.localPosition = Vector3.zero;
                m_Sphere.transform.localRotation = Quaternion.identity;
                m_Sphere.transform.localScale = Vector3.one * m_StartScale;

                Collider collider = m_Sphere.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                m_SphereMaterial = CreateTransparentMaterial(m_SphereColor);
                MeshRenderer renderer = m_Sphere.GetComponent<MeshRenderer>();
                if (renderer != null && m_SphereMaterial != null)
                {
                    renderer.sharedMaterial = m_SphereMaterial;
                }
            }

            private void CreateSmoke(UnitDeathDissolveEffect source, float worldScale)
            {
                GameObject smokeObject = new GameObject("WhiteSmokeParticles");
                smokeObject.transform.SetParent(transform, false);
                smokeObject.transform.localPosition = Vector3.zero;
                smokeObject.transform.localRotation = Quaternion.identity;
                smokeObject.transform.localScale = Vector3.one;

                ParticleSystem particleSystem = smokeObject.AddComponent<ParticleSystem>();

                int particleCount = source != null ? source.smokeParticleCount : 28;
                float radius = (source != null ? source.smokeRadius : 0.45f) * worldScale;
                float upSpeed = source != null ? source.smokeUpSpeed : 0.35f;
                Color smokeColor = source != null ? source.smokeColor : new Color(1f, 1f, 1f, 0.72f);

                ParticleSystem.MainModule main = particleSystem.main;
                main.duration = m_Duration;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(m_Duration * 0.55f, m_Duration);
                main.startSpeed = new ParticleSystem.MinMaxCurve(upSpeed, upSpeed + 0.75f * worldScale);
                main.startSize = new ParticleSystem.MinMaxCurve(0.12f * worldScale, 0.42f * worldScale);
                main.startColor = smokeColor;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.gravityModifier = -0.04f;
                main.maxParticles = Mathf.Max(16, particleCount * 2);

                ParticleSystem.EmissionModule emission = particleSystem.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[]
                {
                    new ParticleSystem.Burst(0f, (short)Mathf.Clamp(particleCount, 1, short.MaxValue))
                });

                ParticleSystem.ShapeModule shape = particleSystem.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = radius;
                shape.randomDirectionAmount = 0.65f;

                ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particleSystem.colorOverLifetime;
                colorOverLifetime.enabled = true;
                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(new Color(0.82f, 0.82f, 0.82f), 1f),
                    },
                    new[]
                    {
                        new GradientAlphaKey(smokeColor.a, 0f),
                        new GradientAlphaKey(smokeColor.a * 0.35f, 0.45f),
                        new GradientAlphaKey(0f, 1f),
                    });
                colorOverLifetime.color = gradient;

                ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particleSystem.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.45f),
                    new Keyframe(0.55f, 1f),
                    new Keyframe(1f, 1.35f)));

                ParticleSystemRenderer particleRenderer = smokeObject.GetComponent<ParticleSystemRenderer>();
                particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                particleRenderer.sortingOrder = 2;
                Material smokeMaterial = GetSmokeMaterial();
                if (smokeMaterial != null)
                {
                    particleRenderer.sharedMaterial = smokeMaterial;
                }

                particleSystem.Play(true);
            }

            private System.Collections.IEnumerator PlayRoutine()
            {
                float elapsed = 0f;
                while (elapsed < m_Duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / m_Duration);
                    float eased = 1f - Mathf.Pow(1f - t, 2f);

                    if (m_Sphere != null)
                    {
                        float scale = Mathf.Lerp(m_StartScale, m_EndScale, eased);
                        m_Sphere.transform.localScale = Vector3.one * scale;
                    }

                    if (m_SphereMaterial != null)
                    {
                        Color color = m_SphereColor;
                        color.a *= Mathf.Clamp01(1f - t);
                        SetMaterialColor(m_SphereMaterial, color);
                    }

                    yield return null;
                }

                if (m_SphereMaterial != null)
                {
                    Destroy(m_SphereMaterial);
                }

                Destroy(gameObject, 0.1f);
            }
        }
    }
}
