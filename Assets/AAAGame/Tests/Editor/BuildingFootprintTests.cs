using System;
using NUnit.Framework;

[TestFixture]
public sealed class BuildingFootprintTests
{
    [TestCase(BuilType.Prod, 2)]
    [TestCase(BuilType.Def, 2)]
    [TestCase(BuilType.Army, 3)]
    [TestCase(BuilType.Base, 4)]
    public void ResolveGridSize_UsesBuildingTypeDefaults(BuilType buildingType, int expectedGridSize)
    {
        Assert.AreEqual(expectedGridSize, BuildingFootprint.ResolveGridSize(buildingType));
        Assert.AreEqual(
            expectedGridSize * BuildingFootprint.GridCellWorldSize,
            BuildingFootprint.ResolveWorldSize(buildingType),
            0.0001f);
        Assert.AreEqual(
            $"Building{expectedGridSize}{expectedGridSize}",
            BuildingFootprint.ResolveLdtkEntityIdentifier(buildingType));
    }

    [Test]
    public void ResolveGridSize_UnconfiguredTypeThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildingFootprint.ResolveGridSize(BuilType.Tech));
    }
}
