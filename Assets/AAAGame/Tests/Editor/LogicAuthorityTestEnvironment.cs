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
        "AggroCandidatePropagationRadius",
        "DamageAlertTargetDuration",
        "MinimumAggroCandidateRange",
        "DefendPursuitDistance",
        "DefendReturnMoveSpeedBonus",
        "PlayerOutOfCombatMoveSpeedBonus",
        "DefendReturnHealthRegenPercentPerSecond",
        "DefendPhaseSameGroupSpawnIntervalSeconds",
        "DefendPhaseEnemyMinSpeed",
        "DefendPhaseEnemyMaxSpeed",
        "DefendPhaseEnemyEndlessGrowthRate",
        "BaseResourceIncomeDailyGrowth",
        "MinimumDamagePerHit"
    };

    private static readonly string[] ConfigValues =
    {
        "1.4", "0.2", "0.4", "0.6", "1.0", "50", "1.8", "5.4", "8.1", "3.6", "14.4", "54.0", "0.4", "1", "7.2",
        "27.0", "21.6", "21.6", "0.9", "32.4", "2", "3.6", "12.6", "2", "12.6", "32.4", "4.5", "9.0", "20", "1", "5.4", "21.6", "1.2", "0.5", "1"
    };

    private readonly bool[] m_HadPreviousValues = new bool[ConfigKeys.Length];
    private readonly Fix64[] m_PreviousValues = new Fix64[ConfigKeys.Length];
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
        m_HadPreviousReturnDamageReduction = FixedConfigReader.TryGetEditorTestFixedConfig(
            ReturnDamageReductionConfigKey,
            out m_PreviousReturnDamageReduction);
        FixedConfigReader.SetEditorTestFixedConfig(ReturnDamageReductionConfigKey, Fix64.Zero);

        for (int i = 0; i < ConfigKeys.Length; i++)
        {
            string key = ConfigKeys[i];
            m_HadPreviousValues[i] = FixedConfigReader.TryGetEditorTestPositiveFixedConfig(key, out m_PreviousValues[i]);
            FixedConfigReader.SetEditorTestPositiveFixedConfig(
                key,
                FixedConfigReader.ParseFixedConfigText(key, ConfigValues[i]));
        }
    }

    public void Dispose()
    {
        if (m_Disposed)
            return;

        for (int i = 0; i < ConfigKeys.Length; i++)
        {
            if (m_HadPreviousValues[i])
                FixedConfigReader.SetEditorTestPositiveFixedConfig(ConfigKeys[i], m_PreviousValues[i]);
            else
                FixedConfigReader.ClearEditorTestPositiveFixedConfig(ConfigKeys[i]);
        }
        if (m_HadPreviousReturnDamageReduction)
            FixedConfigReader.SetEditorTestFixedConfig(ReturnDamageReductionConfigKey, m_PreviousReturnDamageReduction);
        else
            FixedConfigReader.ClearEditorTestFixedConfig(ReturnDamageReductionConfigKey);
        m_Disposed = true;
    }
}
