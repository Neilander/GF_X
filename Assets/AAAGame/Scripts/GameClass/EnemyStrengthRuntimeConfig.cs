using System;

public enum EnemyStrengthContext
{
    Garrison,
    DefenseWave
}

public readonly struct EnemyStrengthRuntimeSettings
{
    public EnemyStrengthRuntimeSettings(
        EnemyStrengthCurveSettings curve,
        EnemySquadValueSettings squadValue,
        Fix64 levelTwoValueScale,
        Fix64 levelThreeValueScale)
    {
        Curve = curve;
        SquadValue = squadValue;
        LevelTwoValueScale = levelTwoValueScale;
        LevelThreeValueScale = levelThreeValueScale;
    }

    public EnemyStrengthCurveSettings Curve { get; }
    public EnemySquadValueSettings SquadValue { get; }
    public Fix64 LevelTwoValueScale { get; }
    public Fix64 LevelThreeValueScale { get; }
}

public static class EnemyStrengthRuntimeConfig
{
    public const string GarrisonGrowthPerExpectedDayKey = "EnemyGarrisonStrengthGrowthPerExpectedDay";
    public const string GarrisonGrowthHalfWidthRatioKey = "EnemyGarrisonStrengthGrowthHalfWidthRatio";
    public const string DefenseGrowthPerExpectedDayKey = "EnemyDefenseStrengthGrowthPerExpectedDay";
    public const string DefenseGrowthHalfWidthRatioKey = "EnemyDefenseStrengthGrowthHalfWidthRatio";
    public const string EarlyCountPivotKey = "EnemyStrengthEarlyCountPivot";
    public const string EarlyCountSynergyKey = "EnemyStrengthEarlyCountSynergy";
    public const string CrowdingTailScaleKey = "EnemyStrengthCrowdingTailScale";
    public const string LevelTwoValueScaleKey = "EnemyUnitLevel2ValueScale";
    public const string LevelThreeValueScaleKey = "EnemyUnitLevel3ValueScale";

    public static EnemyStrengthRuntimeSettings Read(EnemyStrengthContext context)
    {
        string growthKey = context == EnemyStrengthContext.Garrison
            ? GarrisonGrowthPerExpectedDayKey
            : DefenseGrowthPerExpectedDayKey;
        string halfWidthKey = context == EnemyStrengthContext.Garrison
            ? GarrisonGrowthHalfWidthRatioKey
            : DefenseGrowthHalfWidthRatioKey;
        int pivot = RequireInt(EarlyCountPivotKey, 2);
        var settings = new EnemyStrengthRuntimeSettings(
            new EnemyStrengthCurveSettings(
                DistanceUnitConverter.ReadRequiredPositiveFixedConfig(growthKey),
                DistanceUnitConverter.ReadRequiredPositiveFixedConfig(halfWidthKey)),
            new EnemySquadValueSettings(
                pivot,
                DistanceUnitConverter.ReadRequiredPositiveFixedConfig(EarlyCountSynergyKey),
                DistanceUnitConverter.ReadRequiredPositiveFixedConfig(CrowdingTailScaleKey)),
            RequireUnitScale(LevelTwoValueScaleKey),
            RequireUnitScale(LevelThreeValueScaleKey));
        return settings;
    }

    public static void ValidateCurveOrder(int expectedDays)
    {
        if (expectedDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedDays));
        EnemyStrengthCurveSettings garrison = Read(EnemyStrengthContext.Garrison).Curve;
        EnemyStrengthCurveSettings defense = Read(EnemyStrengthContext.DefenseWave).Curve;
        int lastDay = checked(expectedDays * 2);
        for (int day = 2; day <= lastDay; day++)
        {
            Fix64 garrisonMultiplier = EnemySquadStrengthResolver.CalculateDayStrengthMultiplier(
                day, expectedDays, Fix64.One, Fix64.One, garrison);
            Fix64 defenseMultiplier = EnemySquadStrengthResolver.CalculateDayStrengthMultiplier(
                day, expectedDays, Fix64.One, Fix64.One, defense);
            if (garrisonMultiplier >= defenseMultiplier)
            {
                throw new InvalidOperationException(
                    $"Garrison strength curve must stay below defense wave curve. day={day} " +
                    $"garrisonRaw={garrisonMultiplier.RawValue} defenseRaw={defenseMultiplier.RawValue}.");
            }
        }
    }

    private static int RequireInt(string key, int minimum)
    {
        if (GF.Config == null)
            throw new InvalidOperationException($"Enemy strength config '{key}' cannot be read before GF.Config is initialized.");
        int value = GF.Config.GetInt(key, int.MinValue);
        if (value < minimum)
            throw new InvalidOperationException($"Enemy strength config '{key}' must be at least {minimum}. actual={value}.");
        return value;
    }

    private static Fix64 RequireUnitScale(string key)
    {
        Fix64 value = DistanceUnitConverter.ReadRequiredPositiveFixedConfig(key);
        if (value > Fix64.One)
            throw new InvalidOperationException($"Enemy strength config '{key}' must not exceed one. raw={value.RawValue}.");
        return value;
    }
}
