using System.Reflection;
using AAAGame.Tools.Editor;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class LdtkEntityPositionConversionTests
{
    private static readonly MethodInfo ConvertMethod = typeof(LdtkToTileWorldCreatorImporterWindow).GetMethod(
        "ConvertLdtkPivotToLocalPosition",
        BindingFlags.NonPublic | BindingFlags.Static);

    [Test]
    public void ConvertLdtkPivotToLocalPosition_CellCenterMatchesTileWorldCreatorCellCenter()
    {
        Vector3 position = Convert(pixelX: 440, pixelY: 808, gridSize: 16, pixelHeight: 1248, cellSize: 1.4f);

        Assert.That(position.x, Is.EqualTo(27f * 1.4f).Within(0.0001f));
        Assert.That(position.y, Is.Zero);
        Assert.That(position.z, Is.EqualTo(27f * 1.4f).Within(0.0001f));
    }

    [Test]
    public void ConvertLdtkPivotToLocalPosition_SubCellOffsetIsPreservedFromCellCenter()
    {
        Vector3 position = Convert(pixelX: 444, pixelY: 804, gridSize: 16, pixelHeight: 1248, cellSize: 1.4f);

        Assert.That(position.x, Is.EqualTo(27.25f * 1.4f).Within(0.0001f));
        Assert.That(position.y, Is.Zero);
        Assert.That(position.z, Is.EqualTo(27.25f * 1.4f).Within(0.0001f));
    }

    [TestCase(448, 800, 27.5f, 27.5f)]
    [TestCase(440, 808, 27f, 27f)]
    [TestCase(464, 784, 28.5f, 28.5f)]
    public void ConvertCenteredBuildingPivot_PreservesDualGridOffset(
        int pixelX,
        int pixelY,
        float expectedCellX,
        float expectedCellZ)
    {
        Vector3 position = Convert(pixelX, pixelY, 16, 1248, 1.4f);

        Assert.That(position.x, Is.EqualTo(expectedCellX * 1.4f).Within(0.0001f));
        Assert.That(position.y, Is.Zero);
        Assert.That(position.z, Is.EqualTo(expectedCellZ * 1.4f).Within(0.0001f));
    }

    private static Vector3 Convert(int pixelX, int pixelY, int gridSize, int pixelHeight, float cellSize)
    {
        Assert.That(ConvertMethod, Is.Not.Null);
        return (Vector3)ConvertMethod.Invoke(null, new object[] { pixelX, pixelY, gridSize, pixelHeight, cellSize });
    }
}
