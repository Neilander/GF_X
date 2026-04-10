using System.Collections.Generic;
using UnityEngine;

public partial class BuildingEntity
{
    private const float DisabledColorIntensity = 0.45f;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private readonly List<Renderer> _visualRenderers = new List<Renderer>();
    private readonly List<Color> _visualBaseColors = new List<Color>();
    private MaterialPropertyBlock _propertyBlock;

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

        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || renderer.sharedMaterial == null)
                continue;

            _visualRenderers.Add(renderer);
            _visualBaseColors.Add(GetRendererBaseColor(renderer));
        }
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
