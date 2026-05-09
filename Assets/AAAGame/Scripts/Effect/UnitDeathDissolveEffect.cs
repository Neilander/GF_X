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
        private static Material s_FlashMaterial;
        private static Material s_ShockwaveMaterial;

        [Header("死亡VFX粒子")]
        [SerializeField, InspectorName("中心偏移")] private Vector3 centerOffset = new Vector3(0f, 0.45f, 0f);
        [SerializeField, InspectorName("大小倍率"), Min(0.1f)] private float sizeMultiplier = 1f;
        [SerializeField, InspectorName("持续时间"), Min(0.1f)] private float duration = 1.45f;

        [Header("死亡强调效果")]
        [SerializeField, InspectorName("启用暖色爆闪")] private bool enableImpactFlash = true;
        [SerializeField, InspectorName("爆闪尺寸"), Min(0.01f)] private float impactFlashSize = 1.15f;
        [SerializeField, InspectorName("爆闪持续时间"), Min(0.03f)] private float impactFlashDuration = 0.24f;
        [SerializeField, InspectorName("启用冲击环")] private bool enableShockwaveRing = true;
        [SerializeField, InspectorName("冲击环半径"), Min(0.01f)] private float shockwaveRadius = 1.45f;
        [SerializeField, InspectorName("冲击环宽度"), Min(0.005f)] private float shockwaveWidth = 0.075f;
        [SerializeField, InspectorName("冲击环持续时间"), Min(0.03f)] private float shockwaveDuration = 0.36f;
        [SerializeField, InspectorName("启用死亡补光")] private bool enableImpactLight = true;
        [SerializeField, InspectorName("补光强度"), Min(0f)] private float impactLightIntensity = 4.5f;
        [SerializeField, InspectorName("补光范围"), Min(0.01f)] private float impactLightRange = 3.2f;

        [Header("核心光爆粒子")]
        [SerializeField, InspectorName("核心粒子数量"), Min(1)] private int coreParticleCount = 36;
        [SerializeField, InspectorName("核心颜色")] private Color coreColor = new Color(1f, 0.72f, 0.28f, 0.95f);
        [SerializeField, InspectorName("核心粒子强度"), Min(0.1f)] private float coreIntensity = 2.15f;
        [SerializeField, InspectorName("核心粒子尺寸"), Min(0.01f)] private float coreParticleSize = 0.56f;

        [Header("消散烟雾粒子")]
        [SerializeField, InspectorName("烟雾粒子数量"), Min(1)] private int smokeParticleCount = 78;
        [SerializeField, InspectorName("烟雾扩散半径"), Min(0.01f)] private float smokeRadius = 0.72f;
        [SerializeField, InspectorName("烟雾上升速度"), Min(0f)] private float smokeUpSpeed = 0.58f;
        [SerializeField, InspectorName("烟雾颜色")] private Color smokeColor = new Color(0.48f, 0.42f, 0.34f, 0.88f);

        [Header("消散碎光粒子")]
        [SerializeField, InspectorName("碎光粒子数量"), Min(0)] private int sparkParticleCount = 26;
        [SerializeField, InspectorName("碎光颜色")] private Color sparkColor = new Color(1f, 0.46f, 0.12f, 0.9f);
        [SerializeField, InspectorName("碎光速度"), Min(0f)] private float sparkSpeed = 2.1f;

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
                effect = owner.GetComponentInChildren<UnitDeathDissolveEffect>(true);
            }

            if (effect == null)
            {
                return;
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
                s_CoreMaterial = CreateParticleMaterial("Runtime_UnitDeathCoreVFX", Color.white, 2.15f);
            }

            return s_CoreMaterial;
        }

        private static Material GetSmokeMaterial()
        {
            if (s_SmokeMaterial == null)
            {
                s_SmokeMaterial = CreateParticleMaterial("Runtime_UnitDeathSmokeVFX", Color.white, 0.95f);
            }

            return s_SmokeMaterial;
        }

        private static Material GetSparkMaterial()
        {
            if (s_SparkMaterial == null)
            {
                s_SparkMaterial = CreateParticleMaterial("Runtime_UnitDeathSparkVFX", Color.white, 2.1f);
            }

            return s_SparkMaterial;
        }

        private static Material GetFlashMaterial()
        {
            if (s_FlashMaterial == null)
            {
                s_FlashMaterial = CreateParticleMaterial("Runtime_UnitDeathFlashVFX", Color.white, 3.1f);
            }

            return s_FlashMaterial;
        }

        private static Material GetShockwaveMaterial()
        {
            if (s_ShockwaveMaterial == null)
            {
                s_ShockwaveMaterial = CreateParticleMaterial("Runtime_UnitDeathShockwaveVFX", Color.white, 2.55f);
            }

            return s_ShockwaveMaterial;
        }

        private static Material CreateParticleMaterial(string materialName, Color color, float intensity)
        {
            Shader shader = EffectShaderAssetLoader.TryGet(EffectShaderAssetLoader.UnitDeathShaderAssetPath);

            if (shader == null)
            {
                Debug.LogError($"[UnitDeathDissolveEffect] Shader is not ready: {EffectShaderAssetLoader.UnitDeathShaderAssetPath}");
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

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Color BlendRgb(Color from, Color to, float t)
        {
            Color color = Color.Lerp(from, to, Mathf.Clamp01(t));
            color.a = from.a;
            return color;
        }

        private static Color ScaleRgb(Color color, float multiplier)
        {
            color.r = Mathf.Clamp01(color.r * multiplier);
            color.g = Mathf.Clamp01(color.g * multiplier);
            color.b = Mathf.Clamp01(color.b * multiplier);
            return color;
        }

        private static Color ResolveImpactColor(UnitDeathDissolveEffect source)
        {
            Color color = source != null ? source.coreColor : new Color(1f, 0.72f, 0.28f, 0.95f);
            color.a = Mathf.Max(0.05f, color.a);
            return color;
        }

        private static Color ResolveSparkColor(UnitDeathDissolveEffect source)
        {
            Color color = source != null ? source.sparkColor : new Color(1f, 0.46f, 0.12f, 0.9f);
            color.a = Mathf.Max(0.05f, color.a);
            return color;
        }

        private sealed class DeathVfxRuntime : MonoBehaviour
        {
            private float m_Duration;

            public void Begin(UnitDeathDissolveEffect source, float worldScale)
            {
                m_Duration = source != null ? Mathf.Max(0.1f, source.duration) : 1.15f;

                CreateImpactFlash(source, worldScale);
                CreateShockwaveRing(source, worldScale);
                CreateImpactLight(source, worldScale);
                CreateCoreBurst(source, worldScale);
                CreateSmoke(source, worldScale);
                CreateSparks(source, worldScale);

                StartCoroutine(DestroyAfterParticles());
            }

            private void CreateImpactFlash(UnitDeathDissolveEffect source, float worldScale)
            {
                if (source != null && !source.enableImpactFlash)
                {
                    return;
                }

                Material material = GetFlashMaterial();
                if (material == null)
                {
                    return;
                }

                GameObject flashObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                flashObject.name = "DeathImpactFlashSphere";
                flashObject.transform.SetParent(transform, false);
                flashObject.transform.localPosition = Vector3.zero;
                flashObject.transform.localRotation = Quaternion.identity;
                flashObject.transform.localScale = Vector3.one * 0.01f;

                Collider collider = flashObject.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                Renderer renderer = flashObject.GetComponent<Renderer>();
                if (renderer == null)
                {
                    Destroy(flashObject);
                    return;
                }

                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sortingOrder = 8;

                float maxSize = (source != null ? source.impactFlashSize : 1.15f) * worldScale;
                float lifetime = source != null ? source.impactFlashDuration : 0.24f;
                Color flashColor = ResolveImpactColor(source);
                StartCoroutine(AnimateFlashSphere(flashObject.transform, renderer, maxSize, lifetime, flashColor));
            }

            private void CreateShockwaveRing(UnitDeathDissolveEffect source, float worldScale)
            {
                if (source != null && !source.enableShockwaveRing)
                {
                    return;
                }

                Material material = GetShockwaveMaterial();
                if (material == null)
                {
                    return;
                }

                GameObject ringObject = new GameObject("DeathShockwaveRing");
                ringObject.transform.SetParent(transform, false);
                ringObject.transform.localPosition = Vector3.zero;
                ringObject.transform.localRotation = Quaternion.identity;
                ringObject.transform.localScale = Vector3.one;

                LineRenderer lineRenderer = ringObject.AddComponent<LineRenderer>();
                lineRenderer.useWorldSpace = false;
                lineRenderer.loop = true;
                lineRenderer.positionCount = 64;
                lineRenderer.alignment = LineAlignment.View;
                lineRenderer.numCapVertices = 4;
                lineRenderer.numCornerVertices = 4;
                lineRenderer.textureMode = LineTextureMode.Stretch;
                lineRenderer.sharedMaterial = material;
                lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
                lineRenderer.receiveShadows = false;
                lineRenderer.sortingOrder = 7;

                for (int i = 0; i < lineRenderer.positionCount; i++)
                {
                    float angle = (Mathf.PI * 2f * i) / lineRenderer.positionCount;
                    lineRenderer.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
                }

                float radius = (source != null ? source.shockwaveRadius : 1.45f) * worldScale;
                float width = (source != null ? source.shockwaveWidth : 0.075f) * worldScale;
                float lifetime = source != null ? source.shockwaveDuration : 0.36f;
                Color ringColor = ResolveImpactColor(source);
                StartCoroutine(AnimateShockwaveRing(lineRenderer, radius, width, lifetime, ringColor));
            }

            private void CreateImpactLight(UnitDeathDissolveEffect source, float worldScale)
            {
                if (source != null && !source.enableImpactLight)
                {
                    return;
                }

                float intensity = source != null ? source.impactLightIntensity : 4.5f;
                if (intensity <= 0f)
                {
                    return;
                }

                GameObject lightObject = new GameObject("DeathImpactLight");
                lightObject.transform.SetParent(transform, false);
                lightObject.transform.localPosition = Vector3.zero;

                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = WithAlpha(ResolveImpactColor(source), 1f);
                light.intensity = intensity;
                light.range = (source != null ? source.impactLightRange : 3.2f) * worldScale;
                light.shadows = LightShadows.None;

                StartCoroutine(AnimateImpactLight(light, Mathf.Max(0.08f, m_Duration * 0.18f), intensity));
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
                main.startLifetime = new ParticleSystem.MinMaxCurve(m_Duration * 0.24f, m_Duration * 0.48f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f * worldScale, 0.85f * worldScale);
                main.startSize = new ParticleSystem.MinMaxCurve(baseSize * 0.65f, baseSize * 1.25f);
                main.maxParticles = Mathf.Max(8, count * 2);

                ParticleSystem.ShapeModule shape = particleSystem.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.16f * worldScale;
                shape.randomDirectionAmount = 1f;

                ParticleSystem.NoiseModule noise = particleSystem.noise;
                noise.enabled = true;
                noise.strength = 0.18f * worldScale;
                noise.frequency = 0.8f;
                noise.scrollSpeed = 0.35f;

                ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particleSystem.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.55f),
                    new Keyframe(0.28f, 1.65f),
                    new Keyframe(1f, 0f)));

                ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                ApplyRendererMaterial(renderer, material, 5);
                ApplyRendererTint(renderer, Color.white, source != null ? source.coreIntensity : 2.15f);

                StartConfiguredParticleSystem(particleSystem);
            }

            private void CreateSmoke(UnitDeathDissolveEffect source, float worldScale)
            {
                int count = source != null ? source.smokeParticleCount : 34;
                float radius = (source != null ? source.smokeRadius : 0.48f) * worldScale;
                float upSpeed = source != null ? source.smokeUpSpeed : 0.35f;
                Color color = source != null ? source.smokeColor : new Color(0.48f, 0.42f, 0.34f, 0.88f);
                Material material = GetSmokeMaterial();

                ParticleSystem particleSystem = CreateParticleSystem("SmokeDissolveParticles");
                ConfigureCommonParticleSystem(particleSystem, m_Duration, count, color);

                ParticleSystem.MainModule main = particleSystem.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(m_Duration * 0.72f, m_Duration * 1.22f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(upSpeed * 0.35f, upSpeed + 1.15f * worldScale);
                main.startSize = new ParticleSystem.MinMaxCurve(0.22f * worldScale, 0.82f * worldScale);
                main.gravityModifier = -0.08f;
                main.maxParticles = Mathf.Max(16, count * 2);

                ParticleSystem.ShapeModule shape = particleSystem.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = radius;
                shape.randomDirectionAmount = 0.92f;

                ParticleSystem.VelocityOverLifetimeModule velocity = particleSystem.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.World;
                float lateralSpeed = 0.36f * worldScale;
                velocity.x = new ParticleSystem.MinMaxCurve(-lateralSpeed, lateralSpeed);
                velocity.y = new ParticleSystem.MinMaxCurve(0.12f * worldScale, 0.52f * worldScale);
                velocity.z = new ParticleSystem.MinMaxCurve(-lateralSpeed, lateralSpeed);

                ParticleSystem.NoiseModule noise = particleSystem.noise;
                noise.enabled = true;
                noise.strength = 0.42f * worldScale;
                noise.frequency = 0.55f;
                noise.scrollSpeed = 0.28f;

                ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particleSystem.colorOverLifetime;
                colorOverLifetime.enabled = true;
                Gradient gradient = new Gradient();
                Color brightSmoke = BlendRgb(color, Color.white, 0.2f);
                Color midSmoke = color;
                Color deepSmoke = ScaleRgb(color, 0.62f);
                Color endSmoke = ScaleRgb(color, 0.28f);
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(brightSmoke, 0f),
                        new GradientColorKey(midSmoke, 0.18f),
                        new GradientColorKey(deepSmoke, 0.58f),
                        new GradientColorKey(endSmoke, 1f),
                    },
                    new[]
                    {
                        new GradientAlphaKey(color.a * 0.3f, 0f),
                        new GradientAlphaKey(color.a, 0.16f),
                        new GradientAlphaKey(color.a * 0.46f, 0.58f),
                        new GradientAlphaKey(0f, 1f),
                    });
                colorOverLifetime.color = gradient;

                ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particleSystem.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.28f),
                    new Keyframe(0.2f, 1.05f),
                    new Keyframe(0.68f, 1.72f),
                    new Keyframe(1f, 2.05f)));

                ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                ApplyRendererMaterial(renderer, material, 3);
                ApplyRendererTint(renderer, Color.white, 0.95f);

                StartConfiguredParticleSystem(particleSystem);
            }

            private void CreateSparks(UnitDeathDissolveEffect source, float worldScale)
            {
                int count = source != null ? source.sparkParticleCount : 12;
                if (count <= 0)
                {
                    return;
                }

                Color color = ResolveSparkColor(source);
                float speed = (source != null ? source.sparkSpeed : 1.15f) * worldScale;
                Material material = GetSparkMaterial();

                ParticleSystem particleSystem = CreateParticleSystem("SparkDissolveParticles");
                ConfigureCommonParticleSystem(particleSystem, m_Duration, count, color);

                ParticleSystem.MainModule main = particleSystem.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(m_Duration * 0.22f, m_Duration * 0.62f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.65f, speed * 1.25f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.045f * worldScale, 0.14f * worldScale);
                main.gravityModifier = 0.16f;
                main.maxParticles = Mathf.Max(8, count * 2);

                ParticleSystem.ShapeModule shape = particleSystem.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.3f * worldScale;
                shape.randomDirectionAmount = 1f;

                ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particleSystem.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.35f),
                    new Keyframe(0.22f, 1f),
                    new Keyframe(1f, 0f)));

                ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                ApplyRendererMaterial(renderer, material, 6);
                ApplyRendererTint(renderer, Color.white, 2.1f);

                StartConfiguredParticleSystem(particleSystem);
            }

            private IEnumerator AnimateFlashSphere(Transform flashTransform, Renderer renderer, float maxSize, float lifetime, Color flashColor)
            {
                float elapsed = 0f;
                while (elapsed < lifetime && flashTransform != null && renderer != null)
                {
                    float normalizedTime = Mathf.Clamp01(elapsed / lifetime);
                    float eased = 1f - Mathf.Pow(1f - normalizedTime, 3f);
                    float scale = Mathf.Lerp(maxSize * 0.18f, maxSize, eased);
                    float alpha = Mathf.Lerp(0.95f, 0f, normalizedTime * normalizedTime);

                    flashTransform.localScale = Vector3.one * scale;
                    ApplyRendererTint(renderer, WithAlpha(flashColor, alpha), Mathf.Lerp(3.2f, 0.4f, normalizedTime));

                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (flashTransform != null)
                {
                    Destroy(flashTransform.gameObject);
                }
            }

            private IEnumerator AnimateShockwaveRing(LineRenderer lineRenderer, float targetRadius, float width, float lifetime, Color ringColor)
            {
                float elapsed = 0f;
                Transform ringTransform = lineRenderer != null ? lineRenderer.transform : null;

                while (elapsed < lifetime && lineRenderer != null && ringTransform != null)
                {
                    float normalizedTime = Mathf.Clamp01(elapsed / lifetime);
                    float eased = 1f - Mathf.Pow(1f - normalizedTime, 3f);
                    float radius = Mathf.Lerp(targetRadius * 0.18f, targetRadius, eased);
                    float alpha = Mathf.Lerp(0.85f, 0f, normalizedTime);
                    Color color = WithAlpha(ringColor, alpha);

                    ringTransform.localScale = Vector3.one * radius;
                    lineRenderer.widthMultiplier = Mathf.Lerp(width, width * 0.2f, normalizedTime);
                    lineRenderer.startColor = color;
                    lineRenderer.endColor = color;
                    ApplyRendererTint(lineRenderer, color, Mathf.Lerp(2.5f, 0.5f, normalizedTime));

                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (ringTransform != null)
                {
                    Destroy(ringTransform.gameObject);
                }
            }

            private IEnumerator AnimateImpactLight(Light impactLight, float lifetime, float startIntensity)
            {
                float elapsed = 0f;
                while (elapsed < lifetime && impactLight != null)
                {
                    float normalizedTime = Mathf.Clamp01(elapsed / lifetime);
                    impactLight.intensity = Mathf.Lerp(startIntensity, 0f, normalizedTime * normalizedTime);
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (impactLight != null)
                {
                    Destroy(impactLight.gameObject);
                }
            }

            private static void ApplyRendererTint(Renderer renderer, Color color, float intensity)
            {
                if (renderer == null)
                {
                    return;
                }

                MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(BaseColorId, color);
                propertyBlock.SetColor(ColorId, color);
                propertyBlock.SetFloat(IntensityId, intensity);
                renderer.SetPropertyBlock(propertyBlock);
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
