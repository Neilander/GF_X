using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

public sealed class EnemySquadResourceEquivalentResolverTests
{
    private const int MaximumResolvedUnitCount = 1 << 20;

    private static readonly EnemySquadResourceEquivalentCurveSettings CurveSettings = new(
        (Fix64)0.02m,
        (Fix64)0.008m,
        (Fix64)0.55m);

    private static readonly Fix64[] UnitLevelResourceEquivalents =
    {
        (Fix64)1m,
        (Fix64)2.4m,
        (Fix64)5.5m
    };

    [Test]
    public void CompositionResourceEquivalentIsLinearSumOfUnitResourceEquivalents()
    {
        Fix64 actual = EnemySquadResourceEquivalentResolver.CalculateCompositionResourceEquivalent(
            new[]
            {
                new EnemySquadCompositionEntry(1, 4),
                new EnemySquadCompositionEntry(3, 2)
            },
            UnitLevelResourceEquivalents);

        Assert.That(actual, Is.EqualTo((Fix64)15m));
    }

    [Test]
    public void ExpectedDayEndsAccelerationAndThenDailyGrowthFalls()
    {
        const int expectedDays = 8;
        var multipliers = new List<Fix64>();
        for (int day = 1; day <= 15; day++)
        {
            multipliers.Add(EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
                day,
                expectedDays,
                Fix64.One,
                Fix64.One,
                CurveSettings));
        }

        Fix64 largestSlope = Fix64.Zero;
        int largestSlopeBoundary = 0;
        for (int i = 1; i < multipliers.Count; i++)
        {
            Fix64 slope = multipliers[i] - multipliers[i - 1];
            if (slope > largestSlope)
            {
                largestSlope = slope;
                largestSlopeBoundary = i + 1;
            }
        }

        Assert.That(
            largestSlopeBoundary == expectedDays || largestSlopeBoundary == expectedDays + 1,
            Is.True,
            $"Largest slope boundary was day {largestSlopeBoundary}.");
        Assert.That(multipliers[0], Is.EqualTo(Fix64.One));
        Assert.That(
            multipliers[14] - multipliers[13],
            Is.LessThan(multipliers[8] - multipliers[7]));
    }

    [Test]
    public void PreExpectedResourceEquivalentIsQuadraticWithConstantSecondDifference()
    {
        const int expectedDays = 8;
        Fix64 previousIncrement = Fix64.Zero;
        for (int day = 2; day <= expectedDays; day++)
        {
            Fix64 current = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
                day, expectedDays, Fix64.One, Fix64.One, CurveSettings);
            Fix64 previous = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
                day - 1, expectedDays, Fix64.One, Fix64.One, CurveSettings);
            Fix64 increment = current - previous;

            if (day == 2)
            {
                Assert.That(increment, Is.EqualTo(CurveSettings.Day2IncrementPerDay1BaseResourceEquivalent));
            }
            else
            {
                Assert.That(
                    increment - previousIncrement,
                    Is.EqualTo(CurveSettings.PreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent),
                    $"Second difference was not constant on day {day}.");
            }

            previousIncrement = increment;
        }
    }

    [Test]
    public void ExpectedDaysDoNotChangeSharedEarlyCurveButLongerLevelsAccelerateFurther()
    {
        for (int day = 1; day <= 6; day++)
        {
            Fix64 shortLevel = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
                day, 6, Fix64.One, Fix64.One, CurveSettings);
            Fix64 longLevel = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
                day, 10, Fix64.One, Fix64.One, CurveSettings);
            Assert.That(longLevel, Is.EqualTo(shortLevel), $"Shared early curve diverged on day {day}.");
        }

        Fix64 shortDayTen = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
            10, 6, Fix64.One, Fix64.One, CurveSettings);
        Fix64 longDayTen = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
            10, 10, Fix64.One, Fix64.One, CurveSettings);
        Assert.That(longDayTen, Is.GreaterThan(shortDayTen));
    }

    [Test]
    public void AddingLowLevelUnitsAddsTheirExactResourceEquivalent()
    {
        Fix64 eliteOnly = EnemySquadResourceEquivalentResolver.CalculateCompositionResourceEquivalent(
            new[] { new EnemySquadCompositionEntry(3, 3) },
            UnitLevelResourceEquivalents);
        Fix64 mixed = EnemySquadResourceEquivalentResolver.CalculateCompositionResourceEquivalent(
            new[]
            {
                new EnemySquadCompositionEntry(1, 20),
                new EnemySquadCompositionEntry(3, 3)
            },
            UnitLevelResourceEquivalents);

        Assert.That(mixed - eliteOnly, Is.EqualTo((Fix64)20m));
    }

    [Test]
    public void QuantizedMigratedPureSquadDoesNotGainSpuriousLevels()
    {
        IReadOnlyList<EnemySquadCompositionEntry> composition = EnemySquadResourceEquivalentResolver.Resolve(
            (Fix64)6,
            Fix64.FromRaw(3687),
            1,
            8,
            Fix64.One,
            Fix64.One,
            new[] { Fix64.One, (Fix64)1.8m, (Fix64)2.25m },
            CurveSettings,
            MaximumResolvedUnitCount);

        Assert.That(composition.Count, Is.EqualTo(1));
        Assert.That(composition[0].Level, Is.EqualTo(1));
        Assert.That(composition[0].Count, Is.EqualTo(6));
    }

    [Test]
    public void CountWeightProducesMoreLowerLevelUnits()
    {
        IReadOnlyList<EnemySquadCompositionEntry> quantity = EnemySquadResourceEquivalentResolver.Resolve(
            (Fix64)12m, Fix64.One, 10, 8, Fix64.One, Fix64.One, UnitLevelResourceEquivalents, CurveSettings, MaximumResolvedUnitCount);
        IReadOnlyList<EnemySquadCompositionEntry> quality = EnemySquadResourceEquivalentResolver.Resolve(
            (Fix64)12m, Fix64.Zero, 10, 8, Fix64.One, Fix64.One, UnitLevelResourceEquivalents, CurveSettings, MaximumResolvedUnitCount);

        Assert.That(TotalCount(quantity), Is.GreaterThan(TotalCount(quality)));
        Assert.That(AverageLevel(quantity), Is.LessThan(AverageLevel(quality)));
    }

    [Test]
    public void CountWeightStillControlsCompositionWhenBudgetHasExactPureLevelSolutions()
    {
        IReadOnlyList<EnemySquadCompositionEntry> quantity = EnemySquadResourceEquivalentResolver.Resolve(
            (Fix64)12m, Fix64.One, 1, 8, Fix64.One, Fix64.One,
            UnitLevelResourceEquivalents, CurveSettings, MaximumResolvedUnitCount);
        IReadOnlyList<EnemySquadCompositionEntry> quality = EnemySquadResourceEquivalentResolver.Resolve(
            (Fix64)12m, Fix64.Zero, 1, 8, Fix64.One, Fix64.One,
            UnitLevelResourceEquivalents, CurveSettings, MaximumResolvedUnitCount);

        Assert.That(TotalCount(quantity), Is.EqualTo(12));
        Assert.That(TotalCount(quality), Is.EqualTo(3));
        Assert.That(AverageLevel(quantity), Is.LessThan(AverageLevel(quality)));
    }

    [Test]
    public void ResolverUsesAtMostTwoAdjacentLevelsAndIsDeterministic()
    {
        IReadOnlyList<EnemySquadCompositionEntry> first = EnemySquadResourceEquivalentResolver.Resolve(
            (Fix64)16m, (Fix64)0.42m, 10, 7, (Fix64)1.2m, (Fix64)1.1m, UnitLevelResourceEquivalents, CurveSettings, MaximumResolvedUnitCount);
        IReadOnlyList<EnemySquadCompositionEntry> second = EnemySquadResourceEquivalentResolver.Resolve(
            (Fix64)16m, (Fix64)0.42m, 10, 7, (Fix64)1.2m, (Fix64)1.1m, UnitLevelResourceEquivalents, CurveSettings, MaximumResolvedUnitCount);

        Assert.That(first.Count, Is.LessThanOrEqualTo(2));
        if (first.Count == 2)
            Assert.That(first[1].Level, Is.EqualTo(first[0].Level + 1));
        Assert.That(second.Count, Is.EqualTo(first.Count));
        for (int i = 0; i < first.Count; i++)
        {
            Assert.That(second[i].Level, Is.EqualTo(first[i].Level));
            Assert.That(second[i].Count, Is.EqualTo(first[i].Count));
        }
    }

    [Test]
    public void InitialAndGrowthModifiersAffectTheirOwnDimensions()
    {
        Fix64 baseDayOne = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
            1, 8, Fix64.One, Fix64.One, CurveSettings);
        Fix64 strongerDayOne = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
            1, 8, (Fix64)1.25m, Fix64.One, CurveSettings);
        Fix64 fasterDayOne = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
            1, 8, Fix64.One, (Fix64)1.5m, CurveSettings);
        Fix64 fasterDaySix = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
            6, 8, Fix64.One, (Fix64)1.5m, CurveSettings);
        Fix64 baseDaySix = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
            6, 8, Fix64.One, Fix64.One, CurveSettings);

        Assert.That(baseDayOne, Is.EqualTo(Fix64.One));
        Assert.That(strongerDayOne, Is.EqualTo((Fix64)1.25m));
        Assert.That(fasterDayOne, Is.EqualTo(Fix64.One));
        Assert.That(fasterDaySix, Is.GreaterThan(baseDaySix));
    }

    [Test]
    public void GarrisonCurveGrowsSlowerThanDefenseCurve()
    {
        var garrison = new EnemySquadResourceEquivalentCurveSettings((Fix64)0.3m, (Fix64)0.09m, (Fix64)0.55m);
        var defense = new EnemySquadResourceEquivalentCurveSettings((Fix64)0.7m, (Fix64)0.245m, (Fix64)0.55m);

        for (int day = 2; day <= 20; day++)
        {
            Fix64 garrisonMultiplier = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
                day, 10, Fix64.One, Fix64.One, garrison);
            Fix64 defenseMultiplier = EnemySquadResourceEquivalentResolver.CalculateDaySquadResourceEquivalentMultiplier(
                day, 10, Fix64.One, Fix64.One, defense);
            Assert.That(garrisonMultiplier, Is.LessThan(defenseMultiplier), $"Unexpected curve order on day {day}.");
        }
    }

    [Test]
    public void ArmyBuildingValuesUseCumulativeOrangeMarrowAndLevelEfficiencyScales()
    {
        BuildingTable building = CreateArmyBuilding(
            "Buil_TestArmy",
            4, 8, 12,
            8, 0, 12);

        EnemyUnitResourceEquivalentResolver.Result result = EnemyUnitResourceEquivalentResolver.ResolveFromArmyBuilding(
            building,
            (Fix64)0.9m,
            (Fix64)0.75m);

        Assert.That(result.InvestmentAmounts[0], Is.EqualTo((Fix64)0.5m));
        Assert.That(result.InvestmentAmounts[1], Is.EqualTo((Fix64)1.5m));
        Assert.That(result.InvestmentAmounts[2], Is.EqualTo((Fix64)2m));
        Assert.That(result.EffectiveResourceEquivalents[1], Is.EqualTo((Fix64)1.35m));
        Assert.That(result.EffectiveResourceEquivalents[2], Is.EqualTo((Fix64)1.5m));
    }

    [Test]
    public void ArmyBuildingValuesRejectDiscountsThatCollapseHigherLevels()
    {
        BuildingTable building = CreateArmyBuilding(
            "Buil_TestArmy",
            5, 10, 15,
            2, 2, 3);

        Assert.Throws<System.InvalidOperationException>(() =>
            EnemyUnitResourceEquivalentResolver.ResolveFromArmyBuilding(
                building,
                (Fix64)0.2m,
                (Fix64)0.1m));
    }

    private static int TotalCount(IReadOnlyList<EnemySquadCompositionEntry> entries)
    {
        int total = 0;
        for (int i = 0; i < entries.Count; i++)
            total += entries[i].Count;
        return total;
    }

    private static Fix64 AverageLevel(IReadOnlyList<EnemySquadCompositionEntry> entries)
    {
        int count = 0;
        int levels = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            count += entries[i].Count;
            levels += entries[i].Count * entries[i].Level;
        }
        return (Fix64)levels / (Fix64)count;
    }

    private static BuildingTable CreateArmyBuilding(
        string identifier,
        int levelOneCost,
        int levelTwoCost,
        int levelThreeCost,
        int levelOneProduction,
        int levelTwoProduction,
        int levelThreeProduction)
    {
        var building = new BuildingTable();
        SetProperty(building, nameof(BuildingTable.Identifier), identifier);
        SetProperty(building, nameof(BuildingTable.Type), BuilType.Army);
        SetProperty(building, nameof(BuildingTable.UnitID), "Unit_Intern");
        SetProperty(building, nameof(BuildingTable.Lv1Cost), levelOneCost);
        SetProperty(building, nameof(BuildingTable.Lv2Cost), levelTwoCost);
        SetProperty(building, nameof(BuildingTable.Lv3Cost), levelThreeCost);
        SetProperty(building, nameof(BuildingTable.Lv1Production), levelOneProduction);
        SetProperty(building, nameof(BuildingTable.Lv2Production), levelTwoProduction);
        SetProperty(building, nameof(BuildingTable.Lv3Production), levelThreeProduction);
        return building;
    }

    private static void SetProperty<T>(BuildingTable building, string name, T value)
    {
        PropertyInfo property = typeof(BuildingTable).GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(property, Is.Not.Null, $"Missing BuildingTable property '{name}'.");
        property.SetValue(building, value);
    }
}
