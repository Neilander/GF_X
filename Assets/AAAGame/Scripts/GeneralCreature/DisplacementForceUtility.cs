using System;

public static class DisplacementForceUtility
{
    public const string KnockbackForceLevelM1Key = "KnockbackForceLevelM1";
    public const string KnockbackForceLevel0Key = "KnockbackForceLevel0";
    public const string KnockbackForceLevel1Key = "KnockbackForceLevel1";
    public const string PullForceLevelM1Key = "PullForceLevelM1";
    public const string PullForceLevel0Key = "PullForceLevel0";
    public const string PullForceLevel1Key = "PullForceLevel1";
    public const string PullDurationLevelM1Key = "PullDurationLevelM1";
    public const string PullDurationLevel0Key = "PullDurationLevel0";
    public const string FrictionKey = "Friction";

    public static bool TryResolveKnockbackVelocity(
        Fix64 strengthLevel,
        Fix64 weightLevel,
        out Fix64 worldVelocity)
    {
        Fix64 difference = strengthLevel - RequireWeight(weightLevel);
        if (difference < -Fix64.One)
        {
            worldVelocity = Fix64.Zero;
            return false;
        }

        worldVelocity = DistanceUnitConverter.ConvertToWorld(ReadForceByDifference(
            difference,
            KnockbackForceLevelM1Key,
            KnockbackForceLevel0Key,
            KnockbackForceLevel1Key));
        return true;
    }

    public static bool TryResolvePull(
        Fix64 strengthLevel,
        Fix64 weightLevel,
        out Fix64 worldAcceleration,
        out Fix64 duration)
    {
        Fix64 difference = strengthLevel - RequireWeight(weightLevel);
        if (difference < -Fix64.One)
        {
            worldAcceleration = Fix64.Zero;
            duration = Fix64.Zero;
            return false;
        }

        worldAcceleration = DistanceUnitConverter.ConvertToWorld(ReadForceByDifference(
            difference,
            PullForceLevelM1Key,
            PullForceLevel0Key,
            PullForceLevel1Key));
        duration = DistanceUnitConverter.ReadRequiredPositiveFixedConfig(
            difference <= -Fix64.One ? PullDurationLevelM1Key : PullDurationLevel0Key);
        return true;
    }

    public static Fix64 ReadWorldFriction()
    {
        return DistanceUnitConverter.ConvertToWorld(
            DistanceUnitConverter.ReadRequiredPositiveFixedConfig(FrictionKey));
    }

    private static Fix64 ReadForceByDifference(
        Fix64 difference,
        string levelM1Key,
        string level0Key,
        string level1Key)
    {
        string key = difference <= -Fix64.One
            ? levelM1Key
            : difference < Fix64.One
                ? level0Key
                : level1Key;
        return DistanceUnitConverter.ReadRequiredPositiveFixedConfig(key);
    }

    private static Fix64 RequireWeight(Fix64 weightLevel)
    {
        if (weightLevel <= Fix64.Zero)
            throw new InvalidOperationException($"Displacement requires a positive target weight level. raw={weightLevel.RawValue}.");
        return weightLevel;
    }
}
