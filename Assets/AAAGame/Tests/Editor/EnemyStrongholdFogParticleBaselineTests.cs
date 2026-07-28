using NUnit.Framework;
using UnityEngine;

public sealed class EnemyStrongholdFogParticleBaselineTests
{
    [Test]
    public void ApplyMultipliers_RepeatedConfigurationUsesCapturedPrefabValues()
    {
        GameObject gameObject = new GameObject("EnemyStrongholdFogParticleBaselineTest");
        try
        {
            ParticleSystem particleSystem = gameObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particleSystem.main;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.25f, 0.5f, 0.75f, 0.4f));
            main.startSizeMultiplier = 2f;
            ParticleSystem.EmissionModule emission = particleSystem.emission;
            emission.rateOverTimeMultiplier = 13f;

            EnemyStrongholdFogParticleBaseline baseline =
                gameObject.AddComponent<EnemyStrongholdFogParticleBaseline>();

            baseline.ApplyMultipliers(particleSystem, 3f, 2.5f, 0.5f);
            baseline.ApplyMultipliers(particleSystem, 3f, 2.5f, 0.5f);

            Assert.AreEqual(5f, particleSystem.main.startSizeMultiplier, 0.0001f);
            Assert.AreEqual(39f, particleSystem.emission.rateOverTimeMultiplier, 0.0001f);
            Assert.AreEqual(0.2f, particleSystem.main.startColor.color.a, 0.0001f);
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }
}
