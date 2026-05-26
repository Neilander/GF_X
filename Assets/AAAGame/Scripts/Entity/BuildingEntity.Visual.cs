using System.Collections.Generic;
using GameFramework;
using GameFramework.Event;
using UnityEngine;

public partial class BuildingEntity
{
    private const float DisabledColorIntensity = 0.45f;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private readonly List<Renderer> _visualRenderers = new List<Renderer>();
    private readonly List<Color> _visualBaseColors = new List<Color>();
    private readonly List<bool> _visualBaseEnabledStates = new List<bool>();
    private MaterialPropertyBlock _propertyBlock;
    private bool _lv0PhaseVisibilityEventSubscribed;
    private bool _phaseVisibilityApplied;

    private void SetDisabledVisual(bool disabled)
    {
        EnsureVisualCache();

        if (_visualRenderers.Count == 0)
            return;

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        for (int i = 0; i < _visualRenderers.Count; i++)
        {
            var renderer = _visualRenderers[i];
            if (renderer == null)
                continue;

            Color original = _visualBaseColors[i];
            Color target = disabled ? GetDisabledColor(original) : original;

            renderer.GetPropertyBlock(_propertyBlock);

            if (HasColorProperty(renderer, BaseColorId))
                _propertyBlock.SetColor(BaseColorId, target);

            if (HasColorProperty(renderer, ColorId))
                _propertyBlock.SetColor(ColorId, target);

            renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    private void EnsureVisualCache()
    {
        if (_visualRenderers.Count > 0)
            return;

        _visualRenderers.Clear();
        _visualBaseColors.Clear();
        _visualBaseEnabledStates.Clear();

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
        }
    }

    private void SubscribeLv0PhaseVisibilityEvents()
    {
        if (_lv0PhaseVisibilityEventSubscribed || !IsLv0Building())
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
        if (!IsLv0Building())
        {
            UpdateMinimapReportVisibility();
            return;
        }

        SetPhaseVisibility(IsVisibleInCurrentPhase());
        UpdateMinimapReportVisibility();
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
        if (CurrentStronghold != null)
            return CurrentStronghold.OwnerFactionId != EntitySideHelper.PlayerFactionId;

        return OwnerFactionID != EntitySideHelper.PlayerFactionId;
    }

    private bool IsLv0Building()
    {
        return buildingData != null && buildingData.Lv == 0;
    }

    private void SetPhaseVisibility(bool visible)
    {
        EnsureVisualCache();

        for (int i = 0; i < _visualRenderers.Count; i++)
        {
            Renderer renderer = _visualRenderers[i];
            if (renderer == null)
                continue;

            bool baseEnabled = i < _visualBaseEnabledStates.Count && _visualBaseEnabledStates[i];
            renderer.enabled = visible && baseEnabled;
        }

        _phaseVisibilityApplied = !visible;
    }

    private void RestorePhaseVisibility()
    {
        if (!_phaseVisibilityApplied)
            return;

        for (int i = 0; i < _visualRenderers.Count; i++)
        {
            Renderer renderer = _visualRenderers[i];
            if (renderer == null)
                continue;

            renderer.enabled = i >= _visualBaseEnabledStates.Count || _visualBaseEnabledStates[i];
        }

        _phaseVisibilityApplied = false;
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
