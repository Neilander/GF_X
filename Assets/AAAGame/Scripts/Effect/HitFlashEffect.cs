using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AAAGame.Effect
{
    /// <summary>
    /// 单个单位/建筑的受击闪白。运行时临时切换到闪白材质，结束后恢复原材质。
    /// </summary>
    public sealed class HitFlashEffect : MonoBehaviour
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
        private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");

        [Header("受击闪白")]
        [SerializeField] [InspectorName("闪白Shader")] private Shader hitFlashShader;
        [SerializeField] [InspectorName("闪白颜色")] private Color flashColor = Color.white;
        [SerializeField] [InspectorName("闪白持续时间")] private float duration = 0.12f;
        [SerializeField] [InspectorName("闪白峰值")] [Range(0f, 1f)] private float peakAmount = 1f;
        [SerializeField] [InspectorName("淡出占比")] [Range(0.05f, 1f)] private float fadeOutPercent = 0.75f;
        [SerializeField] [InspectorName("包含未激活子物体")] private bool includeInactiveChildren;
        [SerializeField] [InspectorName("包含粒子渲染器")] private bool includeParticleRenderers;

        private readonly List<RendererState> m_RendererStates = new List<RendererState>();
        private Coroutine m_FlashRoutine;
        private bool m_CacheDirty = true;
        private bool m_UsingFlashMaterials;

        private sealed class RendererState
        {
            public Renderer Renderer;
            public Material[] OriginalMaterials;
            public Material[] FlashMaterials;
        }

        public void ApplyRuntimeDefaults(Color color, float flashDuration, float peak)
        {
            flashColor = color;
            duration = Mathf.Max(0.01f, flashDuration);
            peakAmount = Mathf.Clamp01(peak);
        }

        public void Play()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            EnsureCache();
            if (m_RendererStates.Count <= 0)
            {
                return;
            }

            if (m_FlashRoutine != null)
            {
                StopCoroutine(m_FlashRoutine);
            }

            m_FlashRoutine = StartCoroutine(PlayFlashRoutine());
        }

        private void OnEnable()
        {
            m_CacheDirty = true;
        }

        private void OnDisable()
        {
            if (m_FlashRoutine != null)
            {
                StopCoroutine(m_FlashRoutine);
                m_FlashRoutine = null;
            }

            RestoreOriginalMaterials();
        }

        private void OnDestroy()
        {
            RestoreOriginalMaterials();
            ReleaseFlashMaterials();
        }

        private IEnumerator PlayFlashRoutine()
        {
            float safeDuration = Mathf.Max(0.01f, duration);
            float fadeStartTime = safeDuration * Mathf.Clamp01(1f - fadeOutPercent);
            float elapsed = 0f;

            ApplyFlashMaterials();
            SetFlashAmount(peakAmount);

            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;

                float amount = peakAmount;
                if (elapsed > fadeStartTime)
                {
                    float fadeDuration = Mathf.Max(0.001f, safeDuration - fadeStartTime);
                    float fadeT = Mathf.Clamp01((elapsed - fadeStartTime) / fadeDuration);
                    amount = Mathf.Lerp(peakAmount, 0f, fadeT);
                }

                SetFlashAmount(amount);
                yield return null;
            }

            RestoreOriginalMaterials();
            m_FlashRoutine = null;
        }

        private void EnsureCache()
        {
            if (!m_CacheDirty && m_RendererStates.Count > 0)
            {
                return;
            }

            ReleaseFlashMaterials();
            m_RendererStates.Clear();

            Shader shader = hitFlashShader != null
                ? hitFlashShader
                : Resources.Load<Shader>("HitFlashWhite");
            if (shader == null)
            {
                shader = Shader.Find("AAAGame/Effec/HitFlashWhite");
            }

            if (shader == null)
            {
                Debug.LogWarning("[HitFlashEffect] Cannot find shader: AAAGame/Effec/HitFlashWhite", this);
                return;
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(includeInactiveChildren);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || (!includeParticleRenderers && renderer is ParticleSystemRenderer))
                {
                    continue;
                }

                Material[] originalMaterials = renderer.sharedMaterials;
                if (originalMaterials == null || originalMaterials.Length <= 0)
                {
                    continue;
                }

                Material[] flashMaterials = new Material[originalMaterials.Length];
                for (int materialIndex = 0; materialIndex < originalMaterials.Length; materialIndex++)
                {
                    flashMaterials[materialIndex] = CreateFlashMaterial(shader, originalMaterials[materialIndex]);
                }

                m_RendererStates.Add(new RendererState
                {
                    Renderer = renderer,
                    OriginalMaterials = originalMaterials,
                    FlashMaterials = flashMaterials
                });
            }

            m_CacheDirty = false;
        }

        private Material CreateFlashMaterial(Shader shader, Material source)
        {
            Material material = new Material(shader)
            {
                name = source != null ? $"{source.name}_HitFlash" : "HitFlash_Material",
                hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
            };

            Texture texture = null;
            if (source != null)
            {
                if (source.HasProperty(BaseMapId))
                {
                    texture = source.GetTexture(BaseMapId);
                }
                else if (source.HasProperty(MainTexId))
                {
                    texture = source.GetTexture(MainTexId);
                }
            }

            if (texture != null)
            {
                if (material.HasProperty(BaseMapId))
                {
                    material.SetTexture(BaseMapId, texture);
                }

                if (material.HasProperty(MainTexId))
                {
                    material.SetTexture(MainTexId, texture);
                }
            }

            Color baseColor = Color.white;
            if (source != null)
            {
                if (source.HasProperty(BaseColorId))
                {
                    baseColor = source.GetColor(BaseColorId);
                }
                else if (source.HasProperty(ColorId))
                {
                    baseColor = source.GetColor(ColorId);
                }
            }

            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, baseColor);
            }

            if (material.HasProperty(ColorId))
            {
                material.SetColor(ColorId, baseColor);
            }

            SetFlashMaterialValues(material, 0f);
            return material;
        }

        private void ApplyFlashMaterials()
        {
            for (int i = 0; i < m_RendererStates.Count; i++)
            {
                RendererState state = m_RendererStates[i];
                if (state.Renderer != null)
                {
                    state.Renderer.sharedMaterials = state.FlashMaterials;
                }
            }

            m_UsingFlashMaterials = true;
        }

        private void RestoreOriginalMaterials()
        {
            if (!m_UsingFlashMaterials)
            {
                return;
            }

            for (int i = 0; i < m_RendererStates.Count; i++)
            {
                RendererState state = m_RendererStates[i];
                if (state.Renderer != null)
                {
                    state.Renderer.sharedMaterials = state.OriginalMaterials;
                }
            }

            m_UsingFlashMaterials = false;
        }

        private void SetFlashAmount(float amount)
        {
            for (int i = 0; i < m_RendererStates.Count; i++)
            {
                Material[] materials = m_RendererStates[i].FlashMaterials;
                if (materials == null)
                {
                    continue;
                }

                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    SetFlashMaterialValues(materials[materialIndex], amount);
                }
            }
        }

        private void SetFlashMaterialValues(Material material, float amount)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty(FlashColorId))
            {
                material.SetColor(FlashColorId, flashColor);
            }

            if (material.HasProperty(FlashAmountId))
            {
                material.SetFloat(FlashAmountId, Mathf.Clamp01(amount));
            }
        }

        private void ReleaseFlashMaterials()
        {
            for (int i = 0; i < m_RendererStates.Count; i++)
            {
                Material[] materials = m_RendererStates[i].FlashMaterials;
                if (materials == null)
                {
                    continue;
                }

                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material != null)
                    {
                        Destroy(material);
                    }
                }
            }
        }
    }
}
