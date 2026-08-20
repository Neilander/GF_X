using System;
using System.Collections.Generic;

public readonly struct EnemyStrengthCurveSettings
{
    public EnemyStrengthCurveSettings(
        Fix64 growthPerExpectedDay,
        Fix64 growthHalfWidthRatio)
    {
        if (growthPerExpectedDay <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(growthPerExpectedDay));
        if (growthHalfWidthRatio <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(growthHalfWidthRatio));

        GrowthPerExpectedDay = growthPerExpectedDay;
        GrowthHalfWidthRatio = growthHalfWidthRatio;
    }

    public Fix64 GrowthPerExpectedDay { get; }
    public Fix64 GrowthHalfWidthRatio { get; }
}

public readonly struct EnemySquadValueSettings
{
    public EnemySquadValueSettings(
        int earlyCountPivot,
        Fix64 earlyCountSynergy,
        Fix64 crowdingTailScale)
    {
        if (earlyCountPivot < 2)
            throw new ArgumentOutOfRangeException(nameof(earlyCountPivot));
        if (earlyCountSynergy <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(earlyCountSynergy));
        if (crowdingTailScale <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(crowdingTailScale));

        EarlyCountPivot = earlyCountPivot;
        EarlyCountSynergy = earlyCountSynergy;
        CrowdingTailScale = crowdingTailScale;
    }

    public int EarlyCountPivot { get; }
    public Fix64 EarlyCountSynergy { get; }
    public Fix64 CrowdingTailScale { get; }
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

public static class EnemySquadStrengthResolver
{
    private const int MaximumResolvedUnitCount = 1 << 20;

    public static Fix64 CalculateDayStrengthMultiplier(
        int day,
        int expectedDays,
        Fix64 initialStrengthScale,
        Fix64 growthSpeedScale,
        EnemyStrengthCurveSettings settings)
    {
        if (day <= 0)
            throw new ArgumentOutOfRangeException(nameof(day));
        if (expectedDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedDays));
        if (initialStrengthScale <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(initialStrengthScale));
        if (growthSpeedScale <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(growthSpeedScale));

        Fix64 effectiveDay = Fix64.One + (Fix64)(day - 1) * growthSpeedScale;
        Fix64 halfWidth = Fix64.Max(
            Fix64.One,
            (Fix64)Math.Max(1, expectedDays - 1) * settings.GrowthHalfWidthRatio);
        Fix64 firstDayX = (Fix64)(1 - expectedDays) / halfWidth;
        Fix64 currentX = (effectiveDay - (Fix64)expectedDays) / halfWidth;
        Fix64 firstDaySigmoid = EvaluateCenteredSigmoid(firstDayX);
        Fix64 currentSigmoid = EvaluateCenteredSigmoid(currentX);
        Fix64 progress = (currentSigmoid - firstDaySigmoid) / (Fix64.One - firstDaySigmoid);
        progress = Fix64.Clamp(progress, Fix64.Zero, Fix64.One);

        Fix64 finalGrowth = (Fix64)expectedDays * settings.GrowthPerExpectedDay;
        return initialStrengthScale * (Fix64.One + finalGrowth * progress);
    }

    public static Fix64 CalculateCountValue(int count, EnemySquadValueSettings settings)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        int pivot = settings.EarlyCountPivot;
        if (count <= pivot)
        {
            Fix64 pairSynergy = settings.EarlyCountSynergy
                                * (Fix64)count
                                * (Fix64)(count - 1)
                                / (Fix64)(2 * pivot);
            return (Fix64)count + pairSynergy;
        }

        Fix64 pivotValue = CalculateCountValue(pivot, settings);
        Fix64 previousValue = CalculateCountValue(pivot - 1, settings);
        Fix64 entrySlope = pivotValue - previousValue;
        Fix64 delta = (Fix64)(count - pivot);
        Fix64 root = Fix64.Sqrt(Fix64.One + (Fix64)2 * delta / settings.CrowdingTailScale);
        return pivotValue
               + entrySlope * settings.CrowdingTailScale * (root - Fix64.One);
    }

    public static IReadOnlyList<EnemySquadCompositionEntry> Resolve(
        Fix64 initialStrengthValue,
        Fix64 countGrowthWeight,
        int day,
        int expectedDays,
        Fix64 initialStrengthScale,
        Fix64 growthSpeedScale,
        IReadOnlyList<Fix64> levelValues,
        EnemyStrengthCurveSettings curveSettings,
        EnemySquadValueSettings valueSettings)
    {
        if (initialStrengthValue <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(initialStrengthValue));
        if (countGrowthWeight < Fix64.Zero || countGrowthWeight > Fix64.One)
            throw new ArgumentOutOfRangeException(nameof(countGrowthWeight));
        ValidateLevelValues(levelValues);

        Fix64 strengthMultiplier = CalculateDayStrengthMultiplier(
            day,
            expectedDays,
            initialStrengthScale,
            growthSpeedScale,
            curveSettings);
        Fix64 targetBudget = initialStrengthValue * strengthMultiplier;
        Fix64 minimumBudget = CalculateCountValue(1, valueSettings) * levelValues[0];
        if (targetBudget < minimumBudget)
        {
            throw new InvalidOperationException(
                $"Enemy squad target budget is below one level-one unit. targetRaw={targetBudget.RawValue}, minimumRaw={minimumBudget.RawValue}.");
        }

        Fix64 minimumAllowedCountValue = targetBudget / levelValues[levelValues.Count - 1];
        Fix64 maximumAllowedCountValue = targetBudget / levelValues[0];
        Fix64 desiredCountValue = Fix64.Lerp(
            minimumAllowedCountValue,
            maximumAllowedCountValue,
            countGrowthWeight);

        int totalCount = FindNearestCount(desiredCountValue, valueSettings);
        while (CalculateCountValue(totalCount, valueSettings) > maximumAllowedCountValue)
            totalCount--;
        while (CalculateCountValue(totalCount, valueSettings) < minimumAllowedCountValue)
            totalCount++;

        Fix64 averageLevelValue = targetBudget / CalculateCountValue(totalCount, valueSettings);
        int lowerLevelIndex = FindLowerLevelIndex(averageLevelValue, levelValues);
        if (lowerLevelIndex == levelValues.Count - 1)
        {
            return new[]
            {
                new EnemySquadCompositionEntry(levelValues.Count, totalCount)
            };
        }

        Fix64 lowValue = levelValues[lowerLevelIndex];
        Fix64 highValue = levelValues[lowerLevelIndex + 1];
        Fix64 highCountExact = (Fix64)totalCount
                               * (averageLevelValue - lowValue)
                               / (highValue - lowValue);
        int highCount = RoundToNearestInt(highCountExact);
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

    public static Fix64 CalculateCompositionValue(
        IReadOnlyList<EnemySquadCompositionEntry> composition,
        IReadOnlyList<Fix64> levelValues,
        EnemySquadValueSettings settings)
    {
        if (composition == null || composition.Count == 0)
            throw new ArgumentException("Enemy squad composition is empty.", nameof(composition));
        ValidateLevelValues(levelValues);

        int totalCount = 0;
        Fix64 summedUnitValues = Fix64.Zero;
        for (int i = 0; i < composition.Count; i++)
        {
            EnemySquadCompositionEntry entry = composition[i];
            if (entry.Level > levelValues.Count)
                throw new InvalidOperationException($"Enemy squad level {entry.Level} has no configured value.");
            totalCount = checked(totalCount + entry.Count);
            summedUnitValues += (Fix64)entry.Count * levelValues[entry.Level - 1];
        }

        Fix64 averageUnitValue = summedUnitValues / (Fix64)totalCount;
        return CalculateCountValue(totalCount, settings) * averageUnitValue;
    }

    private static Fix64 EvaluateCenteredSigmoid(Fix64 value)
    {
        Fix64 denominator = (Fix64)2 * (Fix64.One + Fix64.Abs(value));
        return Fix64.FromRaw(Fix64.One.RawValue / 2) + value / denominator;
    }

    private static int FindNearestCount(Fix64 targetCountValue, EnemySquadValueSettings settings)
    {
        if (targetCountValue <= CalculateCountValue(1, settings))
            return 1;

        int low = 1;
        int high = 2;
        while (CalculateCountValue(high, settings) < targetCountValue)
        {
            low = high;
            high = checked(high * 2);
            if (high > MaximumResolvedUnitCount)
            {
                throw new InvalidOperationException(
                    $"Enemy squad strength requires more than {MaximumResolvedUnitCount} units.");
            }
        }

        while (low + 1 < high)
        {
            int middle = low + (high - low) / 2;
            if (CalculateCountValue(middle, settings) < targetCountValue)
                low = middle;
            else
                high = middle;
        }

        Fix64 lowError = Fix64.Abs(CalculateCountValue(low, settings) - targetCountValue);
        Fix64 highError = Fix64.Abs(CalculateCountValue(high, settings) - targetCountValue);
        return lowError <= highError ? low : high;
    }

    private static int FindLowerLevelIndex(Fix64 averageValue, IReadOnlyList<Fix64> levelValues)
    {
        if (averageValue <= levelValues[0])
            return 0;
        for (int i = 0; i < levelValues.Count - 1; i++)
        {
            if (averageValue < levelValues[i + 1])
                return i;
        }
        return levelValues.Count - 1;
    }

    private static int RoundToNearestInt(Fix64 value)
    {
        if (value < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(value));
        return checked((int)Fix64.Floor(value + Fix64.FromRaw(Fix64.One.RawValue / 2)));
    }

    private static void ValidateLevelValues(IReadOnlyList<Fix64> levelValues)
    {
        if (levelValues == null || levelValues.Count == 0)
            throw new ArgumentException("Enemy unit level values are empty.", nameof(levelValues));
        for (int i = 0; i < levelValues.Count; i++)
        {
            if (levelValues[i] <= Fix64.Zero)
                throw new ArgumentOutOfRangeException(nameof(levelValues), $"Level {i + 1} value must be positive.");
            if (i > 0 && levelValues[i] <= levelValues[i - 1])
            {
                throw new ArgumentException(
                    $"Enemy unit level values must increase strictly. level={i + 1} raw={levelValues[i].RawValue}.",
                    nameof(levelValues));
            }
        }
    }
}

public static class EnemyUnitStrengthValueResolver
{
    public readonly struct Result
    {
        public Result(Fix64[] investmentValues, Fix64[] effectiveValues)
        {
            InvestmentValues = investmentValues ?? throw new ArgumentNullException(nameof(investmentValues));
            EffectiveValues = effectiveValues ?? throw new ArgumentNullException(nameof(effectiveValues));
        }

        public Fix64[] InvestmentValues { get; }
        public Fix64[] EffectiveValues { get; }
    }

    public static Result ResolveFromArmyBuilding(
        BuildingTable building,
        Fix64 levelTwoValueScale,
        Fix64 levelThreeValueScale)
    {
        if (building == null)
            throw new ArgumentNullException(nameof(building));
        if (building.Type != BuilType.Army)
            throw new InvalidOperationException($"Building '{building.Identifier}' is not an army building.");
        if (string.IsNullOrWhiteSpace(building.UnitID))
            throw new InvalidOperationException($"Army building '{building.Identifier}' has no unit identifier.");
        if (levelTwoValueScale <= Fix64.Zero || levelTwoValueScale > Fix64.One)
            throw new ArgumentOutOfRangeException(nameof(levelTwoValueScale));
        if (levelThreeValueScale <= Fix64.Zero || levelThreeValueScale > Fix64.One)
            throw new ArgumentOutOfRangeException(nameof(levelThreeValueScale));

        int levelOneForce = ResolveLevelValue(building.Lv1Production, 0, building.Identifier, 1);
        int levelTwoForce = ResolveLevelValue(building.Lv2Production, levelOneForce, building.Identifier, 2);
        int levelThreeForce = ResolveLevelValue(building.Lv3Production, levelTwoForce, building.Identifier, 3);
        int cumulativeLevelOneCost = RequirePositiveCost(building.Lv1Cost, building.Identifier, 1);
        int cumulativeLevelTwoCost = checked(
            cumulativeLevelOneCost + RequirePositiveCost(building.Lv2Cost, building.Identifier, 2));
        int cumulativeLevelThreeCost = checked(
            cumulativeLevelTwoCost + RequirePositiveCost(building.Lv3Cost, building.Identifier, 3));

        var investmentValues = new[]
        {
            (Fix64)cumulativeLevelOneCost / (Fix64)levelOneForce,
            (Fix64)cumulativeLevelTwoCost / (Fix64)levelTwoForce,
            (Fix64)cumulativeLevelThreeCost / (Fix64)levelThreeForce
        };
        var effectiveValues = new[]
        {
            investmentValues[0],
            investmentValues[1] * levelTwoValueScale,
            investmentValues[2] * levelThreeValueScale
        };
        for (int i = 1; i < effectiveValues.Length; i++)
        {
            if (effectiveValues[i] <= effectiveValues[i - 1])
            {
                throw new InvalidOperationException(
                    $"Army building '{building.Identifier}' derives a non-increasing effective unit value at level {i + 1}. " +
                    $"previousRaw={effectiveValues[i - 1].RawValue}, currentRaw={effectiveValues[i].RawValue}.");
            }
        }
        return new Result(investmentValues, effectiveValues);
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
