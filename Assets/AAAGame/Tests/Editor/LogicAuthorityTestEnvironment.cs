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
    private const string ReturnDamageReductionConfigKey = "DefendReturnDamageReductionPercent";
    private static readonly string[] ConfigKeys =
    {
        DistanceUnitConverter.DistanceConversionRateKey,
        "BuildingInteractionRadius",
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
        LogicFactionVisionService.SlopeUpperVisionRadiusConfigKey,
        "AggroOuterRange",
        "DamageAlertVisionDuration",
        "DamageAlertVisionRadius",
        "DamageAlertAllyRadius",
        "DamageAlertTargetDuration",
        "MinimumAggroCandidateRange",
        "DefendPursuitDistance",
        "DefendReturnMoveSpeedBonus",
        "PlayerOutOfCombatMoveSpeedBonus",
        "DefendReturnHealthRegenPercentPerSecond",
        "DefendPhaseEnemyArriveInterval",
        "DefendPhaseEnemyMinSpeed",
        "DefendPhaseEnemyMaxSpeed",
        "DefendPhaseEnemyEndlessGrowthRate",
        "BaseResourceIncomeDailyGrowth",
        "MinimumDamagePerHit"
    };

    private static readonly string[] ConfigValues =
    {
        "0.05", "90", "12", "22", "36", "54", "50", "300", "800", "1200", "500", "2500", "10000", "0.4", "1", "1200",
        "1200", "900", "900", "50", "1800", "2", "200", "700", "2", "700", "1800", "250", "500", "20", "0.8", "500", "1000", "1.2", "0.5", "1"
    };

    private readonly bool[] m_HadPreviousValues = new bool[ConfigKeys.Length];
    private readonly Fix64[] m_PreviousValues = new Fix64[ConfigKeys.Length];
    private readonly bool m_HadPreviousDistanceConversionRateText;
    private readonly string m_PreviousDistanceConversionRateText;
    private readonly bool m_HadPreviousReturnDamageReduction;
    private readonly Fix64 m_PreviousReturnDamageReduction;
    private bool m_Disposed;

    public LogicAuthorityConfigTestScope()
    {
        if (ConfigKeys.Length != ConfigValues.Length)
        {
            throw new System.InvalidOperationException(
                $"Logic authority test config key/value count mismatch. keys={ConfigKeys.Length}, values={ConfigValues.Length}.");
        }
        m_HadPreviousDistanceConversionRateText = DistanceUnitConverter.TryGetEditorTestDistanceConversionRateText(
            out m_PreviousDistanceConversionRateText);
        DistanceUnitConverter.SetEditorTestDistanceConversionRateText(ConfigValues[0]);
        m_HadPreviousReturnDamageReduction = DistanceUnitConverter.TryGetEditorTestFixedConfig(
            ReturnDamageReductionConfigKey,
            out m_PreviousReturnDamageReduction);
        DistanceUnitConverter.SetEditorTestFixedConfig(ReturnDamageReductionConfigKey, Fix64.Zero);

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
        if (m_HadPreviousReturnDamageReduction)
            DistanceUnitConverter.SetEditorTestFixedConfig(ReturnDamageReductionConfigKey, m_PreviousReturnDamageReduction);
        else
            DistanceUnitConverter.ClearEditorTestFixedConfig(ReturnDamageReductionConfigKey);
        m_Disposed = true;
    }
}
