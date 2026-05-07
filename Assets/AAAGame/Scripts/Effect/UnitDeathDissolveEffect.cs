using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace AAAGame.Effect
{
    [DisallowMultipleComponent]
    public sealed class UnitDeathDissolveEffect : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        private static Material s_CoreMaterial;
        private static Material s_SmokeMaterial;
        private static Material s_SparkMaterial;

        [Header("死亡VFX粒子")]
        [SerializeField, InspectorName("中心偏移")] private Vector3 centerOffset = new Vector3(0f, 0.45f, 0f);
        [SerializeField, InspectorName("大小倍率"), Min(0.1f)] private float sizeMultiplier = 1f;
        [SerializeField, InspectorName("持续时间"), Min(0.1f)] private float duration = 1.15f;

        [Header("核心光爆粒子")]
        [SerializeField, InspectorName("核心粒子数量"), Min(1)] private int coreParticleCount = 18;
        [SerializeField, InspectorName("核心颜色")] private Color coreColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField, InspectorName("核心粒子强度"), Min(0.1f)] private float coreIntensity = 1.35f;
        [SerializeField, InspectorName("核心粒子尺寸"), Min(0.01f)] private float coreParticleSize = 0.42f;

        [Header("消散烟雾粒子")]
        [SerializeField, InspectorName("烟雾粒子数量"), Min(1)] private int smokeParticleCount = 34;
        [SerializeField, InspectorName("烟雾扩散半径"), Min(0.01f)] private float smokeRadius = 0.48f;
        [SerializeField, InspectorName("烟雾上升速度"), Min(0f)] private float smokeUpSpeed = 0.35f;
        [SerializeField, InspectorName("烟雾颜色")] private Color smokeColor = new Color(1f, 1f, 1f, 0.62f);

        [Header("消散碎光粒子")]
        [SerializeField, InspectorName("碎光粒子数量"), Min(0)] private int sparkParticleCount = 12;
        [SerializeField, InspectorName("碎光颜色")] private Color sparkColor = new Color(1f, 1f, 1f, 0.85f);
        [SerializeField, InspectorName("碎光速度"), Min(0f)] private float sparkSpeed = 1.15f;

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
            GameObject root = new GameObject("UnitDeathVFXParticle_Runtime");
            root.transform.position = worldPosition;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            DeathVfxRuntime runtime = root.AddComponent<DeathVfxRuntime>();
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

        private static Material GetCoreMaterial()
        {
            if (s_CoreMaterial == null)
            {
                s_CoreMaterial = CreateParticleMaterial("Runtime_UnitDeathCoreVFX", new Color(1f, 1f, 1f, 1f), 1.35f);
            }

            return s_CoreMaterial;
        }

        private static Material GetSmokeMaterial()
        {
            if (s_SmokeMaterial == null)
            {
                s_SmokeMaterial = CreateParticleMaterial("Runtime_UnitDeathSmokeVFX", new Color(1f, 1f, 1f, 1f), 0.78f);
            }

            return s_SmokeMaterial;
        }

        private static Material GetSparkMaterial()
        {
            if (s_SparkMaterial == null)
            {
                s_SparkMaterial = CreateParticleMaterial("Runtime_UnitDeathSparkVFX", new Color(1f, 1f, 1f, 1f), 1.85f);
            }

            return s_SparkMaterial;
        }

        private static Material CreateParticleMaterial(string materialName, Color color, float intensity)
        {
            Shader shader = Resources.Load<Shader>("UnitDeathVFXParticle")
                ?? Shader.Find("AAAGame/Effec/UnitDeathVFXParticle")
                ?? ResolveTransparentShader();

            if (shader == null)
            {
                Debug.LogWarning("[UnitDeathDissolveEffect] Cannot find a transparent shader for death VFX particles.");
                return null;
            }

            Material material = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = (int)RenderQueue.Transparent,
            };

            SetupTransparentMaterial(material);
            SetMaterialColor(material, color);

            if (material.HasProperty(IntensityId))
            {
                material.SetFloat(IntensityId, intensity);
            }

            return material;
        }

        private static Shader ResolveTransparentShader()
        {
            return Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Transparent")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Standard");
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

        private sealed class DeathVfxRuntime : MonoBehaviour
        {
            private float m_Duration;

            public void Begin(UnitDeathDissolveEffect source, float worldScale)
            {
                m_Duration = source != null ? Mathf.Max(0.1f, source.duration) : 1.15f;

                CreateCoreBurst(source, worldScale);
                CreateSmoke(source, worldScale);
                CreateSparks(source, worldScale);

                StartCoroutine(DestroyAfterParticles());
            }

            private void CreateCoreBurst(UnitDeathDissolveEffect source, float worldScale)
            {
                int count = source != null ? source.coreParticleCount : 18;
                Color color = source != null ? source.coreColor : new Color(1f, 1f, 1f, 0.95f);
                float baseSize = (source != null ? source.coreParticleSize : 0.42f) * worldScale;
                Material material = GetCoreMaterial();

                ParticleSystem particleSystem = CreateParticleSystem("CoreBurstParticles");
                ConfigureCommonParticleSystem(particleSystem, m_Duration, count, color);

                ParticleSystem.MainModule main = particleSystem.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(m_Duration * 0.28f, m_Duration * 0.52f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f * worldScale, 0.34f * worldScale);
                main.startSize = new ParticleSystem.MinMaxCurve(baseSize * 0.65f, baseSize * 1.25f);
                main.maxParticles = Mathf.Max(8, count * 2);

                ParticleSystem.ShapeModule shape = particleSystem.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.08f * worldScale;
                shape.randomDirectionAmount = 1f;

                ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particleSystem.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.55f),
                    new Keyframe(0.28f, 1.65f),
                    new Keyframe(1f, 0f)));

                ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                ApplyRendererMaterial(renderer, material, 5);

                StartConfiguredParticleSystem(particleSystem);
            }

            private void CreateSmoke(UnitDeathDissolveEffect source, float worldScale)
            {
                int count = source != null ? source.smokeParticleCount : 34;
                float radius = (source != null ? source.smokeRadius : 0.48f) * worldScale;
                float upSpeed = source != null ? source.smokeUpSpeed : 0.35f;
                Color color = source != null ? source.smokeColor : new Color(1f, 1f, 1f, 0.62f);
                Material material = GetSmokeMaterial();

                ParticleSystem particleSystem = CreateParticleSystem("SmokeDissolveParticles");
                ConfigureCommonParticleSystem(particleSystem, m_Duration, count, color);

                ParticleSystem.MainModule main = particleSystem.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(m_Duration * 0.62f, m_Duration * 1.08f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(upSpeed, upSpeed + 0.72f * worldScale);
                main.startSize = new ParticleSystem.MinMaxCurve(0.14f * worldScale, 0.48f * worldScale);
                main.gravityModifier = -0.04f;
                main.maxParticles = Mathf.Max(16, count * 2);

                ParticleSystem.ShapeModule shape = particleSystem.shape;
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
                        new GradientAlphaKey(color.a, 0f),
                        new GradientAlphaKey(color.a * 0.38f, 0.48f),
                        new GradientAlphaKey(0f, 1f),
                    });
                colorOverLifetime.color = gradient;

                ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particleSystem.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.45f),
                    new Keyframe(0.55f, 1f),
                    new Keyframe(1f, 1.35f)));

                ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                ApplyRendererMaterial(renderer, material, 3);

                StartConfiguredParticleSystem(particleSystem);
            }

            private void CreateSparks(UnitDeathDissolveEffect source, float worldScale)
            {
                int count = source != null ? source.sparkParticleCount : 12;
                if (count <= 0)
                {
                    return;
                }

                Color color = source != null ? source.sparkColor : new Color(1f, 1f, 1f, 0.85f);
                float speed = (source != null ? source.sparkSpeed : 1.15f) * worldScale;
                Material material = GetSparkMaterial();

                ParticleSystem particleSystem = CreateParticleSystem("SparkDissolveParticles");
                ConfigureCommonParticleSystem(particleSystem, m_Duration, count, color);

                ParticleSystem.MainModule main = particleSystem.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(m_Duration * 0.25f, m_Duration * 0.75f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.55f, speed);
                main.startSize = new ParticleSystem.MinMaxCurve(0.035f * worldScale, 0.095f * worldScale);
                main.gravityModifier = 0.08f;
                main.maxParticles = Mathf.Max(8, count * 2);

                ParticleSystem.ShapeModule shape = particleSystem.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.24f * worldScale;
                shape.randomDirectionAmount = 1f;

                ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particleSystem.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.35f),
                    new Keyframe(0.22f, 1f),
                    new Keyframe(1f, 0f)));

                ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                ApplyRendererMaterial(renderer, material, 6);

                StartConfiguredParticleSystem(particleSystem);
            }

            private ParticleSystem CreateParticleSystem(string objectName)
            {
                GameObject particleObject = new GameObject(objectName);
                particleObject.SetActive(false);
                particleObject.transform.SetParent(transform, false);
                particleObject.transform.localPosition = Vector3.zero;
                particleObject.transform.localRotation = Quaternion.identity;
                particleObject.transform.localScale = Vector3.one;
                return particleObject.AddComponent<ParticleSystem>();
            }

            private void ConfigureCommonParticleSystem(ParticleSystem particleSystem, float duration, int burstCount, Color startColor)
            {
                ParticleSystem.MainModule main = particleSystem.main;
                main.playOnAwake = false;
                main.duration = Mathf.Max(0.05f, duration);
                main.loop = false;
                main.startColor = startColor;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;

                ParticleSystem.EmissionModule emission = particleSystem.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[]
                {
                    new ParticleSystem.Burst(0f, (short)Mathf.Clamp(burstCount, 1, short.MaxValue))
                });

                ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particleSystem.colorOverLifetime;
                colorOverLifetime.enabled = true;
                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(Color.white, 1f),
                    },
                    new[]
                    {
                        new GradientAlphaKey(startColor.a, 0f),
                        new GradientAlphaKey(startColor.a * 0.42f, 0.48f),
                        new GradientAlphaKey(0f, 1f),
                    });
                colorOverLifetime.color = gradient;
            }

            private void ApplyRendererMaterial(ParticleSystemRenderer renderer, Material material, int sortingOrder)
            {
                if (renderer == null)
                {
                    return;
                }

                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
                renderer.sortingOrder = sortingOrder;

                if (material != null)
                {
                    renderer.sharedMaterial = material;
                }
                else
                {
                    renderer.enabled = false;
                }
            }

            private void StartConfiguredParticleSystem(ParticleSystem particleSystem)
            {
                if (particleSystem == null)
                {
                    return;
                }

                particleSystem.gameObject.SetActive(true);
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particleSystem.Play(true);
            }

            private IEnumerator DestroyAfterParticles()
            {
                yield return new WaitForSeconds(m_Duration + 0.35f);
                Destroy(gameObject);
            }
        }
    }
}
