using System;
using System.Collections.Generic;

public readonly struct EnemySquadResourceEquivalentCurveSettings
{
    public EnemySquadResourceEquivalentCurveSettings(
        Fix64 day2IncrementPerDay1BaseResourceEquivalent,
        Fix64 preExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent,
        Fix64 postExpectedDailyIncrementMultiplier)
    {
        if (day2IncrementPerDay1BaseResourceEquivalent <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(day2IncrementPerDay1BaseResourceEquivalent));
        if (preExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(preExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent));
        if (postExpectedDailyIncrementMultiplier <= Fix64.Zero || postExpectedDailyIncrementMultiplier >= Fix64.One)
            throw new ArgumentOutOfRangeException(nameof(postExpectedDailyIncrementMultiplier));

        Day2IncrementPerDay1BaseResourceEquivalent = day2IncrementPerDay1BaseResourceEquivalent;
        PreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent = preExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent;
        PostExpectedDailyIncrementMultiplier = postExpectedDailyIncrementMultiplier;
    }

    public Fix64 Day2IncrementPerDay1BaseResourceEquivalent { get; }
    public Fix64 PreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent { get; }
    public Fix64 PostExpectedDailyIncrementMultiplier { get; }
}

public readonly struct EnemySquadCompositionEntry
{
    public EnemySquadCompositionEntry(int level, int count)
    {
        if (level <= 0)
            throw new ArgumentOutOfRangeException(nameof(level));
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        Level = level;
        Count = count;
    }

    public int Level { get; }
    public int Count { get; }
}

public static class EnemySquadResourceEquivalentResolver
{
    public static Fix64 CalculateDaySquadResourceEquivalentMultiplier(
        int day,
        int expectedDays,
        Fix64 initialResourceEquivalentScale,
        Fix64 resourceEquivalentGrowthSpeedScale,
        EnemySquadResourceEquivalentCurveSettings settings)
    {
        if (day <= 0)
            throw new ArgumentOutOfRangeException(nameof(day));
        if (expectedDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedDays));
        if (initialResourceEquivalentScale <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(initialResourceEquivalentScale));
        if (resourceEquivalentGrowthSpeedScale <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(resourceEquivalentGrowthSpeedScale));

        Fix64 growth = Fix64.Zero;
        Fix64 dailyIncrementPerDay1BaseResourceEquivalent = settings.Day2IncrementPerDay1BaseResourceEquivalent;
        for (int currentDay = 2; currentDay <= day; currentDay++)
        {
            if (currentDay <= expectedDays)
            {
                growth += dailyIncrementPerDay1BaseResourceEquivalent;
                if (currentDay < expectedDays)
                {
                    dailyIncrementPerDay1BaseResourceEquivalent +=
                        settings.PreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent;
                }
            }
            else
            {
                dailyIncrementPerDay1BaseResourceEquivalent *= settings.PostExpectedDailyIncrementMultiplier;
                growth += dailyIncrementPerDay1BaseResourceEquivalent;
            }
        }

        return initialResourceEquivalentScale * (Fix64.One + growth * resourceEquivalentGrowthSpeedScale);
    }

    public static IReadOnlyList<EnemySquadCompositionEntry> Resolve(
        Fix64 initialResourceEquivalent,
        Fix64 countGrowthWeight,
        int day,
        int expectedDays,
        Fix64 initialResourceEquivalentScale,
        Fix64 resourceEquivalentGrowthSpeedScale,
        IReadOnlyList<Fix64> unitLevelResourceEquivalents,
        EnemySquadResourceEquivalentCurveSettings curveSettings,
        int maximumResolvedUnitCount)
    {
        if (initialResourceEquivalent < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(initialResourceEquivalent));
        if (countGrowthWeight < Fix64.Zero || countGrowthWeight > Fix64.One)
            throw new ArgumentOutOfRangeException(nameof(countGrowthWeight));
        if (maximumResolvedUnitCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumResolvedUnitCount));
        ValidateUnitLevelResourceEquivalents(unitLevelResourceEquivalents);

        Fix64 squadResourceEquivalentMultiplier = CalculateDaySquadResourceEquivalentMultiplier(
            day,
            expectedDays,
            initialResourceEquivalentScale,
            resourceEquivalentGrowthSpeedScale,
            curveSettings);
        Fix64 targetResourceEquivalent = initialResourceEquivalent * squadResourceEquivalentMultiplier;
        Fix64 minimumResourceEquivalent = unitLevelResourceEquivalents[0];
        long minimumNonEmptyTargetRaw = minimumResourceEquivalent.RawValue / 2
                                        + minimumResourceEquivalent.RawValue % 2;
        if (targetResourceEquivalent.RawValue < minimumNonEmptyTargetRaw)
            return Array.Empty<EnemySquadCompositionEntry>();

        Fix64 minimumAllowedCount = targetResourceEquivalent / unitLevelResourceEquivalents[unitLevelResourceEquivalents.Count - 1];
        Fix64 maximumAllowedCount = targetResourceEquivalent / unitLevelResourceEquivalents[0];
        if (minimumAllowedCount >= (Fix64)maximumResolvedUnitCount + Fix64.FromRaw(2048))
        {
            throw new InvalidOperationException(
                    $"Enemy squad resource equivalent requires more than {maximumResolvedUnitCount} units.");
        }

        int minimumTotalCount = Math.Max(
            1,
            RoundPositiveToInt(minimumAllowedCount, maximumResolvedUnitCount));
        int maximumTotalCount = Math.Max(
            1,
            RoundPositiveToInt(maximumAllowedCount, maximumResolvedUnitCount));
        Fix64 desiredCount = Fix64.Lerp(
            minimumAllowedCount,
            maximumAllowedCount,
            countGrowthWeight);
        int totalCount = Math.Max(
            minimumTotalCount,
            Math.Min(maximumTotalCount, RoundPositiveToInt(desiredCount, maximumResolvedUnitCount)));

        Fix64 uniformUnitResourceEquivalent = targetResourceEquivalent / (Fix64)totalCount;
        int lowerLevelIndex = FindLowerLevelIndex(uniformUnitResourceEquivalent, unitLevelResourceEquivalents);
        if (lowerLevelIndex == unitLevelResourceEquivalents.Count - 1)
        {
            return new[]
            {
                new EnemySquadCompositionEntry(unitLevelResourceEquivalents.Count, totalCount)
            };
        }

        Fix64 lowResourceEquivalent = unitLevelResourceEquivalents[lowerLevelIndex];
        Fix64 highResourceEquivalent = unitLevelResourceEquivalents[lowerLevelIndex + 1];
        Fix64 targetUpgradeCount = (targetResourceEquivalent - lowResourceEquivalent * (Fix64)totalCount)
                                   / (highResourceEquivalent - lowResourceEquivalent);
        int highCount = RoundPositiveToInt(targetUpgradeCount, totalCount);
        highCount = Math.Max(0, Math.Min(totalCount, highCount));
        int lowCount = totalCount - highCount;

        if (highCount == 0)
            return new[] { new EnemySquadCompositionEntry(lowerLevelIndex + 1, lowCount) };
        if (lowCount == 0)
            return new[] { new EnemySquadCompositionEntry(lowerLevelIndex + 2, highCount) };
        return new[]
        {
            new EnemySquadCompositionEntry(lowerLevelIndex + 1, lowCount),
            new EnemySquadCompositionEntry(lowerLevelIndex + 2, highCount)
        };
    }

    public static Fix64 CalculateCompositionResourceEquivalent(
        IReadOnlyList<EnemySquadCompositionEntry> composition,
        IReadOnlyList<Fix64> unitLevelResourceEquivalents)
    {
        if (composition == null)
            throw new ArgumentNullException(nameof(composition));
        ValidateUnitLevelResourceEquivalents(unitLevelResourceEquivalents);
        if (composition.Count == 0)
            return Fix64.Zero;

        Fix64 totalResourceEquivalent = Fix64.Zero;
        for (int i = 0; i < composition.Count; i++)
        {
            EnemySquadCompositionEntry entry = composition[i];
            if (entry.Level <= 0 || entry.Level > unitLevelResourceEquivalents.Count)
                throw new InvalidOperationException($"Enemy squad level {entry.Level} has no configured resource equivalent.");
            if (entry.Count <= 0)
                throw new InvalidOperationException($"Enemy squad level {entry.Level} has non-positive count {entry.Count}.");
            totalResourceEquivalent += unitLevelResourceEquivalents[entry.Level - 1] * (Fix64)entry.Count;
        }
        return totalResourceEquivalent;
    }

    private static int RoundPositiveToInt(Fix64 value, int maximum)
    {
        if (value <= Fix64.Zero)
            return 0;
        if (value >= (Fix64)maximum)
            return maximum;
        return checked((int)((value.RawValue + Fix64.One.RawValue / 2) / Fix64.One.RawValue));
    }

    private static int FindLowerLevelIndex(
        Fix64 averageResourceEquivalent,
        IReadOnlyList<Fix64> unitLevelResourceEquivalents)
    {
        if (averageResourceEquivalent <= unitLevelResourceEquivalents[0])
            return 0;
        for (int i = 0; i < unitLevelResourceEquivalents.Count - 1; i++)
        {
            if (averageResourceEquivalent < unitLevelResourceEquivalents[i + 1])
                return i;
        }
        return unitLevelResourceEquivalents.Count - 1;
    }

    private static void ValidateUnitLevelResourceEquivalents(IReadOnlyList<Fix64> unitLevelResourceEquivalents)
    {
        if (unitLevelResourceEquivalents == null || unitLevelResourceEquivalents.Count == 0)
            throw new ArgumentException("Enemy unit level resource equivalents are empty.", nameof(unitLevelResourceEquivalents));
        for (int i = 0; i < unitLevelResourceEquivalents.Count; i++)
        {
            if (unitLevelResourceEquivalents[i] <= Fix64.Zero)
                throw new ArgumentOutOfRangeException(nameof(unitLevelResourceEquivalents), $"Level {i + 1} unit resource equivalent must be positive.");
            if (i > 0 && unitLevelResourceEquivalents[i] <= unitLevelResourceEquivalents[i - 1])
            {
                throw new ArgumentException(
                    $"Enemy unit level resource equivalents must increase strictly. level={i + 1} raw={unitLevelResourceEquivalents[i].RawValue}.",
                    nameof(unitLevelResourceEquivalents));
            }
        }
    }
}

public static class EnemyUnitResourceEquivalentResolver
{
    public readonly struct Result
    {
        public Result(Fix64[] investmentAmounts, Fix64[] effectiveResourceEquivalents)
        {
            InvestmentAmounts = investmentAmounts ?? throw new ArgumentNullException(nameof(investmentAmounts));
            EffectiveResourceEquivalents = effectiveResourceEquivalents ?? throw new ArgumentNullException(nameof(effectiveResourceEquivalents));
        }

        public Fix64[] InvestmentAmounts { get; }
        public Fix64[] EffectiveResourceEquivalents { get; }
    }

    public static Result ResolveFromArmyBuilding(
        BuildingTable building,
        Fix64 levelTwoResourceEquivalentScale,
        Fix64 levelThreeResourceEquivalentScale)
    {
        if (building == null)
            throw new ArgumentNullException(nameof(building));
        if (building.Type != BuilType.Army)
            throw new InvalidOperationException($"Building '{building.Identifier}' is not an army building.");
        if (string.IsNullOrWhiteSpace(building.UnitID))
            throw new InvalidOperationException($"Army building '{building.Identifier}' has no unit identifier.");
        if (levelTwoResourceEquivalentScale <= Fix64.Zero || levelTwoResourceEquivalentScale > Fix64.One)
            throw new ArgumentOutOfRangeException(nameof(levelTwoResourceEquivalentScale));
        if (levelThreeResourceEquivalentScale <= Fix64.Zero || levelThreeResourceEquivalentScale > Fix64.One)
            throw new ArgumentOutOfRangeException(nameof(levelThreeResourceEquivalentScale));

        int levelOneForce = ResolveLevelValue(building.Lv1Production, 0, building.Identifier, 1);
        int levelTwoForce = ResolveLevelValue(building.Lv2Production, levelOneForce, building.Identifier, 2);
        int levelThreeForce = ResolveLevelValue(building.Lv3Production, levelTwoForce, building.Identifier, 3);
        int cumulativeLevelOneCost = RequirePositiveCost(building.Lv1Cost, building.Identifier, 1);
        int cumulativeLevelTwoCost = checked(
            cumulativeLevelOneCost + RequirePositiveCost(building.Lv2Cost, building.Identifier, 2));
        int cumulativeLevelThreeCost = checked(
            cumulativeLevelTwoCost + RequirePositiveCost(building.Lv3Cost, building.Identifier, 3));

        var investmentAmounts = new[]
        {
            (Fix64)cumulativeLevelOneCost / (Fix64)levelOneForce,
            (Fix64)cumulativeLevelTwoCost / (Fix64)levelTwoForce,
            (Fix64)cumulativeLevelThreeCost / (Fix64)levelThreeForce
        };
        var effectiveResourceEquivalents = new[]
        {
            investmentAmounts[0],
            investmentAmounts[1] * levelTwoResourceEquivalentScale,
            investmentAmounts[2] * levelThreeResourceEquivalentScale
        };
        for (int i = 1; i < effectiveResourceEquivalents.Length; i++)
        {
            if (effectiveResourceEquivalents[i] <= effectiveResourceEquivalents[i - 1])
            {
                throw new InvalidOperationException(
                    $"Army building '{building.Identifier}' derives a non-increasing unit resource equivalent at level {i + 1}. " +
                    $"previousRaw={effectiveResourceEquivalents[i - 1].RawValue}, currentRaw={effectiveResourceEquivalents[i].RawValue}.");
            }
        }
        return new Result(investmentAmounts, effectiveResourceEquivalents);
    }

    private static int ResolveLevelValue(int configured, int previous, string identifier, int level)
    {
        int value = configured > 0 ? configured : previous;
        if (value <= 0)
        {
            throw new InvalidOperationException(
                $"Army building '{identifier}' has no positive force at level {level}.");
        }
        return value;
    }

    private static int RequirePositiveCost(int cost, string identifier, int level)
    {
        if (cost <= 0)
        {
            throw new InvalidOperationException(
                $"Army building '{identifier}' has non-positive level {level} cost {cost}.");
        }
        return cost;
    }
}
