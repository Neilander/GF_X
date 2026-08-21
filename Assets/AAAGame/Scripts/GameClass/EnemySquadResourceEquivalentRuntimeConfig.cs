using System;

public enum EnemySquadResourceEquivalentContext
{
    Garrison,
    DefenseWave
}

public readonly struct EnemySquadResourceEquivalentRuntimeSettings
{
    public EnemySquadResourceEquivalentRuntimeSettings(
        EnemySquadResourceEquivalentCurveSettings curve,
        int maximumResolvedUnitCount,
        Fix64 levelTwoResourceEquivalentScale,
        Fix64 levelThreeResourceEquivalentScale)
    {
        Curve = curve;
        MaximumResolvedUnitCount = maximumResolvedUnitCount;
        LevelTwoResourceEquivalentScale = levelTwoResourceEquivalentScale;
        LevelThreeResourceEquivalentScale = levelThreeResourceEquivalentScale;
    }

    public EnemySquadResourceEquivalentCurveSettings Curve { get; }
    public int MaximumResolvedUnitCount { get; }
    public Fix64 LevelTwoResourceEquivalentScale { get; }
    public Fix64 LevelThreeResourceEquivalentScale { get; }
}

public static class EnemySquadResourceEquivalentRuntimeConfig
{
    public const string GarrisonDay2IncrementPerDay1BaseResourceEquivalentKey = "EnemyGarrisonDay2IncrementPerDay1BaseResourceEquivalent";
    public const string GarrisonPreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalentKey = "EnemyGarrisonPreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent";
    public const string GarrisonPostExpectedDailyIncrementMultiplierKey = "EnemyGarrisonPostExpectedDailyIncrementMultiplier";
    public const string DefenseDay2IncrementPerDay1BaseResourceEquivalentKey = "EnemyDefenseDay2IncrementPerDay1BaseResourceEquivalent";
    public const string DefensePreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalentKey = "EnemyDefensePreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent";
    public const string DefensePostExpectedDailyIncrementMultiplierKey = "EnemyDefensePostExpectedDailyIncrementMultiplier";
    public const string LevelTwoResourceEquivalentScaleKey = "EnemyUnitLevel2ResourceEquivalentScale";
    public const string LevelThreeResourceEquivalentScaleKey = "EnemyUnitLevel3ResourceEquivalentScale";
    public const string MaximumResolvedUnitCountKey = "EnemySquadResourceEquivalentMaximumResolvedUnitCount";

    public static EnemySquadResourceEquivalentRuntimeSettings Read(EnemySquadResourceEquivalentContext context)
    {
        string day2IncrementKey = context == EnemySquadResourceEquivalentContext.Garrison
            ? GarrisonDay2IncrementPerDay1BaseResourceEquivalentKey
            : DefenseDay2IncrementPerDay1BaseResourceEquivalentKey;
        string preExpectedIncrementIncreaseKey = context == EnemySquadResourceEquivalentContext.Garrison
            ? GarrisonPreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalentKey
            : DefensePreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalentKey;
        string postExpectedMultiplierKey = context == EnemySquadResourceEquivalentContext.Garrison
            ? GarrisonPostExpectedDailyIncrementMultiplierKey
            : DefensePostExpectedDailyIncrementMultiplierKey;
        var settings = new EnemySquadResourceEquivalentRuntimeSettings(
            new EnemySquadResourceEquivalentCurveSettings(
                FixedConfigReader.ReadRequiredPositiveFixedConfig(day2IncrementKey),
                FixedConfigReader.ReadRequiredPositiveFixedConfig(preExpectedIncrementIncreaseKey),
                RequireUnitExclusiveScale(postExpectedMultiplierKey)),
            RequireInt(MaximumResolvedUnitCountKey, 1),
            RequireUnitScale(LevelTwoResourceEquivalentScaleKey),
            RequireUnitScale(LevelThreeResourceEquivalentScaleKey));
        return settings;
    }

    public static void ValidateCurveOrder(int expectedDays)
    {
        if (expectedDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedDays));
        EnemySquadResourceEquivalentCurveSettings garrison = Read(EnemySquadResourceEquivalentContext.Garrison).Curve;
        EnemySquadResourceEquivalentCurveSettings defense = Read(EnemySquadResourceEquivalentContext.DefenseWave).Curve;
        int lastDay = checked(expectedDays * 2);
        for (int day = 2; day <= lastDay; day++)
        {
            Fix64 garrisonMultiplier = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
                day, expectedDays, Fix64.One, Fix64.One, garrison);
            Fix64 defenseMultiplier = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
                day, expectedDays, Fix64.One, Fix64.One, defense);
            if (garrisonMultiplier >= defenseMultiplier)
            {
                throw new InvalidOperationException(
                    $"Garrison resource-equivalent curve must stay below defense wave curve. day={day} " +
                    $"garrisonRaw={garrisonMultiplier.RawValue} defenseRaw={defenseMultiplier.RawValue}.");
            }
        }
    }

    private static int RequireInt(string key, int minimum)
    {
        if (GF.Config == null)
            throw new InvalidOperationException($"Enemy resource-equivalent config '{key}' cannot be read before GF.Config is initialized.");
        int value = GF.Config.GetInt(key, int.MinValue);
        if (value < minimum)
            throw new InvalidOperationException($"Enemy resource-equivalent config '{key}' must be at least {minimum}. actual={value}.");
        return value;
    }

    private static Fix64 RequireUnitScale(string key)
    {
        Fix64 value = FixedConfigReader.ReadRequiredPositiveFixedConfig(key);
        if (value > Fix64.One)
            throw new InvalidOperationException($"Enemy resource-equivalent config '{key}' must not exceed one. raw={value.RawValue}.");
        return value;
    }

    private static Fix64 RequireUnitExclusiveScale(string key)
    {
        Fix64 value = FixedConfigReader.ReadRequiredPositiveFixedConfig(key);
        if (value >= Fix64.One)
            throw new InvalidOperationException($"Enemy resource-equivalent config '{key}' must be below one. raw={value.RawValue}.");
        return value;
    }

}
