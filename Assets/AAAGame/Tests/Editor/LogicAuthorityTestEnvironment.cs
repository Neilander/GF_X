using NUnit.Framework;

[SetUpFixture]
public sealed class LogicAuthorityTestEnvironment
{
    private LogicAuthorityConfigTestScope m_ConfigScope;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        m_ConfigScope = new LogicAuthorityConfigTestScope();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        m_ConfigScope.Dispose();
        m_ConfigScope = null;
    }
}

public sealed class LogicAuthorityConfigTestScope : System.IDisposable
{
    private static readonly string[] ConfigKeys =
    {
        DistanceUnitConverter.DistanceConversionRateKey,
        "SmallUnitCollisionRadius",
        "MediumUnitCollisionRadius",
        "LargeUnitCollisionRadius",
        "SuperLargeUnitCollisionRadius",
        "BaseCriticalDamageRate",
        DisplacementForceUtility.KnockbackForceLevelM1Key,
        DisplacementForceUtility.KnockbackForceLevel0Key,
        DisplacementForceUtility.KnockbackForceLevel1Key,
        DisplacementForceUtility.PullForceLevelM1Key,
        DisplacementForceUtility.PullForceLevel0Key,
        DisplacementForceUtility.PullForceLevel1Key,
        DisplacementForceUtility.PullDurationLevelM1Key,
        DisplacementForceUtility.PullDurationLevel0Key,
        DisplacementForceUtility.FrictionKey,
        "HeroVisionRadius",
        "UnitVisionRadius",
        "BuildingVisionRadius",
        "DefendPhaseEnemyArriveInterval",
        "DefendPhaseEnemyMinSpeed",
        "DefendPhaseEnemyEndlessGrowthRate",
        "BaseResourceIncomeDailyGrowth"
    };

    private static readonly string[] ConfigValues =
    {
        "0.05", "12", "22", "36", "54", "50", "300", "800", "1200", "500", "2500", "10000", "0.4", "1", "1200",
        "1200", "900", "900", "0.8", "500", "1.2", "0.5"
    };

    private readonly bool[] m_HadPreviousValues = new bool[ConfigKeys.Length];
    private readonly Fix64[] m_PreviousValues = new Fix64[ConfigKeys.Length];
    private readonly bool m_HadPreviousDistanceConversionRateText;
    private readonly string m_PreviousDistanceConversionRateText;
    private bool m_Disposed;

    public LogicAuthorityConfigTestScope()
    {
        m_HadPreviousDistanceConversionRateText = DistanceUnitConverter.TryGetEditorTestDistanceConversionRateText(
            out m_PreviousDistanceConversionRateText);
        DistanceUnitConverter.SetEditorTestDistanceConversionRateText(ConfigValues[0]);

        for (int i = 1; i < ConfigKeys.Length; i++)
        {
            string key = ConfigKeys[i];
            m_HadPreviousValues[i] = DistanceUnitConverter.TryGetEditorTestPositiveFixedConfig(key, out m_PreviousValues[i]);
            DistanceUnitConverter.SetEditorTestPositiveFixedConfig(
                key,
                DistanceUnitConverter.ParseFixedConfigText(key, ConfigValues[i]));
        }
    }

    public void Dispose()
    {
        if (m_Disposed)
            return;

        if (m_HadPreviousDistanceConversionRateText)
            DistanceUnitConverter.SetEditorTestDistanceConversionRateText(m_PreviousDistanceConversionRateText);
        else
            DistanceUnitConverter.ClearEditorTestDistanceConversionRate();

        for (int i = 1; i < ConfigKeys.Length; i++)
        {
            if (m_HadPreviousValues[i])
                DistanceUnitConverter.SetEditorTestPositiveFixedConfig(ConfigKeys[i], m_PreviousValues[i]);
            else
                DistanceUnitConverter.ClearEditorTestPositiveFixedConfig(ConfigKeys[i]);
        }
        m_Disposed = true;
    }
}
