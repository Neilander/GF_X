using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyStrongholdFogParticleBaseline : MonoBehaviour
{
    private readonly Dictionary<ParticleSystem, ParticleValues> valuesBySystem =
        new Dictionary<ParticleSystem, ParticleValues>();
    private readonly HashSet<ParticleSystemRenderer> configuredRenderers =
        new HashSet<ParticleSystemRenderer>();

    public void ApplyMultipliers(
        ParticleSystem particleSystem,
        float emissionRate,
        float startSizeMultiplier,
        float alpha)
    {
        ParticleValues values = GetOrCapture(particleSystem);
        ParticleSystem.MainModule main = particleSystem.main;
        Color originalColor = values.StartColor;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(
            originalColor.r,
            originalColor.g,
            originalColor.b,
            originalColor.a * Mathf.Clamp01(alpha)));
        main.startSizeMultiplier = values.StartSizeMultiplier * Mathf.Max(0.1f, startSizeMultiplier);

        ParticleSystem.EmissionModule emission = particleSystem.emission;
        emission.rateOverTimeMultiplier = values.EmissionRateMultiplier * Mathf.Max(0f, emissionRate);
    }

    private ParticleValues GetOrCapture(ParticleSystem particleSystem)
    {
        if (particleSystem == null)
            throw new ArgumentNullException(nameof(particleSystem));

        if (valuesBySystem.TryGetValue(particleSystem, out ParticleValues values))
            return values;

        ParticleSystem.MainModule main = particleSystem.main;
        ParticleSystem.EmissionModule emission = particleSystem.emission;
        values = new ParticleValues(
            main.startColor.color,
            main.startSizeMultiplier,
            emission.rateOverTimeMultiplier);
        valuesBySystem.Add(particleSystem, values);
        return values;
    }

    public bool MarkRendererConfigured(ParticleSystemRenderer renderer)
    {
        if (renderer == null)
            throw new ArgumentNullException(nameof(renderer));
        return configuredRenderers.Add(renderer);
    }

    public readonly struct ParticleValues
    {
        public ParticleValues(Color startColor, float startSizeMultiplier, float emissionRateMultiplier)
        {
            StartColor = startColor;
            StartSizeMultiplier = startSizeMultiplier;
            EmissionRateMultiplier = emissionRateMultiplier;
        }

        public Color StartColor { get; }
        public float StartSizeMultiplier { get; }
        public float EmissionRateMultiplier { get; }
    }
}
