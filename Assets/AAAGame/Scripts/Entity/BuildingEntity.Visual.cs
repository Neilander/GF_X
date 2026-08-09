using System.Collections.Generic;
using GameFramework;
using GameFramework.Event;
using UnityEngine;
using UnityEngine.Rendering;

public partial class BuildingEntity
{
    private const float DisabledColorIntensity = 0.45f;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

    private readonly List<Renderer> _visualRenderers = new List<Renderer>();
    private readonly List<Color> _visualBaseColors = new List<Color>();
    private readonly List<bool> _visualBaseEnabledStates = new List<bool>();
    private readonly List<Material[]> _visualBaseMaterials = new List<Material[]>();
    private readonly List<Material[]> _visualRuntimeMaterials = new List<Material[]>();
    private MaterialPropertyBlock _propertyBlock;
    private bool _lv0PhaseVisibilityEventSubscribed;
    private bool _phaseVisibilityApplied;
    private bool _disabledVisualApplied;
    private bool _ownershipColorApplied;
    private Color _ownershipVisualColor;
    private bool _stealthVisualActive;
    private bool _stealthVisualHidden;
    private float _stealthVisibleAlpha = 1f;

    private void SetDisabledVisual(bool disabled)
    {
        EnsureVisualCache();
        _disabledVisualApplied = disabled;
        ApplyVisualState(false);
    }

    private void EnsureVisualCache()
    {
        if (_visualRenderers.Count > 0)
            return;

        _visualRenderers.Clear();
        _visualBaseColors.Clear();
        _visualBaseEnabledStates.Clear();
        _visualBaseMaterials.Clear();
        _visualRuntimeMaterials.Clear();

        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            _visualRenderers.Add(renderer);
            _visualBaseColors.Add(GetRendererBaseColor(renderer));
            _visualBaseEnabledStates.Add(renderer.enabled);
            _visualBaseMaterials.Add(renderer.sharedMaterials);
            _visualRuntimeMaterials.Add(null);
        }
    }

    private void SubscribeLv0PhaseVisibilityEvents()
    {
        if (_lv0PhaseVisibilityEventSubscribed)
            return;

        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChangedForVisibility);
        _lv0PhaseVisibilityEventSubscribed = true;
    }

    private void UnsubscribeLv0PhaseVisibilityEvents()
    {
        if (!_lv0PhaseVisibilityEventSubscribed)
            return;

        try
        {
            GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChangedForVisibility);
        }
        catch (GameFrameworkException)
        {
        }
        finally
        {
            _lv0PhaseVisibilityEventSubscribed = false;
        }
    }

    private void OnIngamePhaseChangedForVisibility(object sender, GameEventArgs e)
    {
        RefreshLv0PhaseVisibility();
    }

    internal void RefreshLv0PhaseVisibility()
    {
        RefreshPhaseHealthBarPresentation();
        if (!IsLv0Building())
        {
            UpdateMinimapReportVisibility();
            return;
        }

        SetPhaseVisibility(IsVisibleInCurrentPhase());
        UpdateMinimapReportVisibility();
    }

    private void RefreshPhaseHealthBarPresentation()
    {
        bool suppress = InGameDataModel.IsBuildPhase((GamePhase)InGameDataModel.GetValue(IngameValueType.Phase))
                        || IsLv0Building()
                        || IsPermanentlyInvincible;
        SetHealthBarSuppressedByBuff(suppress);
        if (suppress)
        {
            HealthBarComp.Remove(Id);
            return;
        }
        if (IsHealthBarSuppressedByBuff || GameObject.Find($"HealthBar_{Id}") != null)
            return;

        Fix64 max = CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
        HealthBarComp.Create(
            Id,
            transform,
            (float)HealthValue,
            (float)max,
            OwnerFactionID == EntitySideHelper.PlayerFactionId);
    }

    private bool IsVisibleInCurrentPhase()
    {
        if (!IsLv0Building())
            return true;

        if (IsNonPlayerOwnedBuilding())
            return false;

        return InGameDataModel.IsBuildPhase((GamePhase)InGameDataModel.GetValue(IngameValueType.Phase));
    }

    private bool IsNonPlayerOwnedBuilding()
    {
        return LogicState.OwnerFactionId != EntitySideHelper.PlayerFactionId;
    }

    private bool IsLv0Building()
    {
        return buildingData != null && buildingData.Lv == 0;
    }

    private void SetPhaseVisibility(bool visible)
    {
        EnsureVisualCache();
        _phaseVisibilityApplied = !visible;
        ApplyVisualState(true);
    }

    private void SetStealthVisualState(
        bool stealthActive,
        bool hidden,
        float visibleAlpha,
        bool refreshPhaseVisibility = true)
    {
        EnsureVisualCache();
        _stealthVisualActive = stealthActive;
        _stealthVisualHidden = hidden;
        _stealthVisibleAlpha = Mathf.Clamp01(visibleAlpha);
        ApplyVisualState(true);

        if (refreshPhaseVisibility && !hidden && !stealthActive)
            RefreshLv0PhaseVisibility();
    }

    public void SetOwnershipVisualColor(bool enabled, Color color)
    {
        EnsureVisualCache();
        _ownershipColorApplied = enabled;
        _ownershipVisualColor = color;
        ApplyVisualState(false);
    }

    private void ApplyVisualState(bool updateVisibility)
    {
        if (_visualRenderers.Count == 0)
            return;

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        bool showStealth = _stealthVisualActive && !_stealthVisualHidden;
        for (int i = 0; i < _visualRenderers.Count; i++)
        {
            Renderer renderer = _visualRenderers[i];
            if (renderer == null)
                continue;

            if (updateVisibility)
            {
                bool baseEnabled = i >= _visualBaseEnabledStates.Count || _visualBaseEnabledStates[i];
                renderer.enabled = baseEnabled && !_phaseVisibilityApplied && !_stealthVisualHidden;
            }

            ApplyMaterialSurfaceState(i, showStealth);

            Color targetColor = _ownershipColorApplied
                ? _ownershipVisualColor
                : _visualBaseColors[i];
            if (_disabledVisualApplied)
                targetColor = GetDisabledColor(targetColor);
            if (showStealth)
                targetColor.a = _stealthVisibleAlpha;

            renderer.GetPropertyBlock(_propertyBlock);
            if (HasColorProperty(renderer, BaseColorId))
                _propertyBlock.SetColor(BaseColorId, targetColor);
            if (HasColorProperty(renderer, ColorId))
                _propertyBlock.SetColor(ColorId, targetColor);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    private void ApplyMaterialSurfaceState(int rendererIndex, bool transparent)
    {
        Material[] runtimeMaterials = _visualRuntimeMaterials[rendererIndex];
        if (transparent)
        {
            runtimeMaterials = GetOrCreateRuntimeMaterials(rendererIndex);
            Material[] sources = _visualBaseMaterials[rendererIndex];
            for (int i = 0; i < runtimeMaterials.Length; i++)
                ConfigureTransparentMaterial(runtimeMaterials[i], sources[i]);
            _visualRenderers[rendererIndex].sharedMaterials = runtimeMaterials;
            return;
        }

        if (runtimeMaterials == null)
            return;

        Material[] baseMaterials = _visualBaseMaterials[rendererIndex];
        for (int i = 0; i < runtimeMaterials.Length; i++)
            RestoreRuntimeMaterial(runtimeMaterials[i], baseMaterials[i]);
        _visualRenderers[rendererIndex].sharedMaterials = runtimeMaterials;
    }

    private Material[] GetOrCreateRuntimeMaterials(int rendererIndex)
    {
        Material[] cached = _visualRuntimeMaterials[rendererIndex];
        if (cached != null)
            return cached;

        Material[] sources = _visualBaseMaterials[rendererIndex];
        var created = new Material[sources.Length];
        try
        {
            for (int i = 0; i < sources.Length; i++)
            {
                Material source = sources[i]
                                  ?? throw new System.InvalidOperationException(
                                      $"Building stealth visual requires a material. renderer={_visualRenderers[rendererIndex].name}, slot={i}.");
                created[i] = new Material(source)
                {
                    name = source.name + "_BuildingVisual",
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
        }
        catch
        {
            DestroyMaterials(created);
            throw;
        }

        _visualRuntimeMaterials[rendererIndex] = created;
        return created;
    }

    private static void ConfigureTransparentMaterial(Material material, Material source)
    {
        RestoreRuntimeMaterial(material, source);
        if (!material.HasProperty(SurfaceId)
            || !material.HasProperty(SrcBlendId)
            || !material.HasProperty(DstBlendId)
            || !material.HasProperty(ZWriteId))
        {
            throw new System.InvalidOperationException(
                $"Building stealth visual requires a surface shader with alpha blending properties. material={source.name}, shader={source.shader?.name}.");
        }

        material.SetFloat(SurfaceId, 1f);
        material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
        material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat(ZWriteId, 0f);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private static void RestoreRuntimeMaterial(Material material, Material source)
    {
        material.shader = source.shader;
        material.CopyPropertiesFromMaterial(source);
        material.shaderKeywords = source.shaderKeywords;
        material.renderQueue = source.renderQueue;
        material.SetOverrideTag("RenderType", source.GetTag("RenderType", false, string.Empty));
    }

    private void ReleaseVisualMaterials()
    {
        for (int i = 0; i < _visualRuntimeMaterials.Count; i++)
        {
            Renderer renderer = i < _visualRenderers.Count ? _visualRenderers[i] : null;
            if (renderer != null)
                renderer.sharedMaterials = _visualBaseMaterials[i];
            DestroyMaterials(_visualRuntimeMaterials[i]);
            _visualRuntimeMaterials[i] = null;
        }
    }

    private static void DestroyMaterials(Material[] materials)
    {
        if (materials == null)
            return;

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null)
                continue;

            if (Application.isPlaying)
                Object.Destroy(material);
            else
                Object.DestroyImmediate(material);
        }
    }

    private void RestorePhaseVisibility()
    {
        if (!_phaseVisibilityApplied)
            return;
        _phaseVisibilityApplied = false;
        ApplyVisualState(true);
    }

    private static Color GetDisabledColor(Color source)
    {
        float g = source.grayscale;
        return new Color(g * DisabledColorIntensity, g * DisabledColorIntensity, g * DisabledColorIntensity, source.a);
    }

    private static bool HasColorProperty(Renderer renderer, int propertyId)
    {
        var material = renderer.sharedMaterial;
        return material != null && material.HasProperty(propertyId);
    }

    private static Color GetRendererBaseColor(Renderer renderer)
    {
        var material = renderer.sharedMaterial;
        if (material == null)
            return Color.white;

        if (material.HasProperty(BaseColorId))
            return material.GetColor(BaseColorId);

        if (material.HasProperty(ColorId))
            return material.GetColor(ColorId);

        return Color.white;
    }
}
