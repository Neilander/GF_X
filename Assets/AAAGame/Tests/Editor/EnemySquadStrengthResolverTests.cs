using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

public sealed class EnemySquadStrengthResolverTests
{
    private static readonly EnemyStrengthCurveSettings CurveSettings = new(
        (Fix64)0.18m,
        (Fix64)0.25m);

    private static readonly EnemySquadValueSettings ValueSettings = new(
        6,
        (Fix64)0.45m,
        (Fix64)8m);

    private static readonly Fix64[] LevelValues =
    {
        (Fix64)1m,
        (Fix64)2.4m,
        (Fix64)5.5m
    };

    [Test]
    public void CountValueStartsSuperlinearThenHasDiminishingMarginalValue()
    {
        Fix64 oneAverage = EnemySquadStrengthResolver.CalculateCountValue(1, ValueSettings);
        Fix64 twoAverage = EnemySquadStrengthResolver.CalculateCountValue(2, ValueSettings) / (Fix64)2;
        Assert.That(twoAverage, Is.GreaterThan(oneAverage));

        Fix64 earlyMarginal = EnemySquadStrengthResolver.CalculateCountValue(6, ValueSettings)
                              - EnemySquadStrengthResolver.CalculateCountValue(5, ValueSettings);
        Fix64 crowdedMarginal = EnemySquadStrengthResolver.CalculateCountValue(30, ValueSettings)
                                - EnemySquadStrengthResolver.CalculateCountValue(29, ValueSettings);
        Assert.That(crowdedMarginal, Is.GreaterThan(Fix64.Zero));
        Assert.That(crowdedMarginal, Is.LessThan(earlyMarginal));
    }

    [Test]
    public void ExpectedDayIsTheBaseCurveSteepestPoint()
    {
        const int expectedDays = 8;
        var multipliers = new List<Fix64>();
        for (int day = 1; day <= 15; day++)
        {
            multipliers.Add(EnemySquadStrengthResolver.CalculateDayStrengthMultiplier(
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
    public void CountWeightProducesMoreLowerLevelUnits()
    {
        IReadOnlyList<EnemySquadCompositionEntry> quantity = EnemySquadStrengthResolver.Resolve(
            (Fix64)12m, Fix64.One, 10, 8, Fix64.One, Fix64.One, LevelValues, CurveSettings, ValueSettings);
        IReadOnlyList<EnemySquadCompositionEntry> quality = EnemySquadStrengthResolver.Resolve(
            (Fix64)12m, Fix64.Zero, 10, 8, Fix64.One, Fix64.One, LevelValues, CurveSettings, ValueSettings);

        Assert.That(TotalCount(quantity), Is.GreaterThan(TotalCount(quality)));
        Assert.That(AverageLevel(quantity), Is.LessThan(AverageLevel(quality)));
    }

    [Test]
    public void ResolverUsesAtMostTwoAdjacentLevelsAndIsDeterministic()
    {
        IReadOnlyList<EnemySquadCompositionEntry> first = EnemySquadStrengthResolver.Resolve(
            (Fix64)16m, (Fix64)0.42m, 10, 7, (Fix64)1.2m, (Fix64)1.1m, LevelValues, CurveSettings, ValueSettings);
        IReadOnlyList<EnemySquadCompositionEntry> second = EnemySquadStrengthResolver.Resolve(
            (Fix64)16m, (Fix64)0.42m, 10, 7, (Fix64)1.2m, (Fix64)1.1m, LevelValues, CurveSettings, ValueSettings);

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
        Fix64 baseDayOne = EnemySquadStrengthResolver.CalculateDayStrengthMultiplier(
            1, 8, Fix64.One, Fix64.One, CurveSettings);
        Fix64 strongerDayOne = EnemySquadStrengthResolver.CalculateDayStrengthMultiplier(
            1, 8, (Fix64)1.25m, Fix64.One, CurveSettings);
        Fix64 fasterDayOne = EnemySquadStrengthResolver.CalculateDayStrengthMultiplier(
            1, 8, Fix64.One, (Fix64)1.5m, CurveSettings);
        Fix64 fasterDaySix = EnemySquadStrengthResolver.CalculateDayStrengthMultiplier(
            6, 8, Fix64.One, (Fix64)1.5m, CurveSettings);
        Fix64 baseDaySix = EnemySquadStrengthResolver.CalculateDayStrengthMultiplier(
            6, 8, Fix64.One, Fix64.One, CurveSettings);

        Assert.That(baseDayOne, Is.EqualTo(Fix64.One));
        Assert.That(strongerDayOne, Is.EqualTo((Fix64)1.25m));
        Assert.That(fasterDayOne, Is.EqualTo(Fix64.One));
        Assert.That(fasterDaySix, Is.GreaterThan(baseDaySix));
    }

    [Test]
    public void GarrisonCurveGrowsSlowerThanDefenseCurve()
    {
        var garrison = new EnemyStrengthCurveSettings((Fix64)0.10m, (Fix64)0.25m);
        var defense = new EnemyStrengthCurveSettings((Fix64)0.18m, (Fix64)0.25m);

        for (int day = 2; day <= 20; day++)
        {
            Fix64 garrisonMultiplier = EnemySquadStrengthResolver.CalculateDayStrengthMultiplier(
                day, 10, Fix64.One, Fix64.One, garrison);
            Fix64 defenseMultiplier = EnemySquadStrengthResolver.CalculateDayStrengthMultiplier(
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

        EnemyUnitStrengthValueResolver.Result result = EnemyUnitStrengthValueResolver.ResolveFromArmyBuilding(
            building,
            (Fix64)0.9m,
            (Fix64)0.75m);

        Assert.That(result.InvestmentValues[0], Is.EqualTo((Fix64)0.5m));
        Assert.That(result.InvestmentValues[1], Is.EqualTo((Fix64)1.5m));
        Assert.That(result.InvestmentValues[2], Is.EqualTo((Fix64)2m));
        Assert.That(result.EffectiveValues[1], Is.EqualTo((Fix64)1.35m));
        Assert.That(result.EffectiveValues[2], Is.EqualTo((Fix64)1.5m));
    }

    [Test]
    public void ArmyBuildingValuesRejectDiscountsThatCollapseHigherLevels()
    {
        BuildingTable building = CreateArmyBuilding(
            "Buil_TestArmy",
            5, 10, 15,
            2, 2, 3);

        Assert.Throws<System.InvalidOperationException>(() =>
            EnemyUnitStrengthValueResolver.ResolveFromArmyBuilding(
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
