using System.Reflection;
using AAAGame.Tools.Editor;
using NUnit.Framework;
using UnityEngine;
using System;

[TestFixture]
public sealed class LdtkEntityPositionConversionTests
{
    private static readonly MethodInfo ConvertMethod = typeof(LdtkToTileWorldCreatorImporterWindow).GetMethod(
        "ConvertLdtkPivotToLocalPosition",
        BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo ConvertEntityMethod = typeof(LdtkToTileWorldCreatorImporterWindow).GetMethod(
        "TryConvertEntity",
        BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly Type EntityInstanceType = typeof(LdtkToTileWorldCreatorImporterWindow).GetNestedType(
        "LdtkEntityInstance",
        BindingFlags.NonPublic);
    private static readonly Type FieldInstanceType = typeof(LdtkToTileWorldCreatorImporterWindow).GetNestedType(
        "LdtkFieldInstance",
        BindingFlags.NonPublic);

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

    [Test]
    public void ConvertTeleportation_ZeroIdAndFractionalWeightImportCorrectly()
    {
        Assert.That(ConvertEntityMethod, Is.Not.Null);
        Assert.That(EntityInstanceType, Is.Not.Null);
        Assert.That(FieldInstanceType, Is.Not.Null);

        object idField = CreateFieldInstance("ID", 0);
        object weightField = CreateFieldInstance("Weight", 1.5m);
        Array fields = Array.CreateInstance(FieldInstanceType, 2);
        fields.SetValue(idField, 0);
        fields.SetValue(weightField, 1);

        object entity = Activator.CreateInstance(EntityInstanceType);
        SetField(entity, "__identifier", "Teleportation");
        SetField(entity, "px", new[] { 16, 16 });
        SetField(entity, "fieldInstances", fields);

        object[] arguments = { entity, 16, 128, 1f, null };
        bool converted = (bool)ConvertEntityMethod.Invoke(null, arguments);

        Assert.That(converted, Is.True);
        object pointData = arguments[4];
        Assert.That((int)GetField(pointData, "teleportationId"), Is.Zero);
        Assert.That(((Fix64)GetField(pointData, "defendSpawnWeight")).RawValue, Is.EqualTo(6144L));
    }

    private static Vector3 Convert(int pixelX, int pixelY, int gridSize, int pixelHeight, float cellSize)
    {
        Assert.That(ConvertMethod, Is.Not.Null);
        return (Vector3)ConvertMethod.Invoke(null, new object[] { pixelX, pixelY, gridSize, pixelHeight, cellSize });
    }

    private static object CreateFieldInstance(string identifier, object value)
    {
        object field = Activator.CreateInstance(FieldInstanceType);
        Type tokenType = FieldInstanceType.GetField("__value").FieldType;
        Type valueType = tokenType.Assembly.GetType("Newtonsoft.Json.Linq.JValue", throwOnError: true);
        object token = Activator.CreateInstance(valueType, value);
        SetField(field, "__identifier", identifier);
        SetField(field, "__value", token);
        return field;
    }

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name).SetValue(target, value);
    }

    private static object GetField(object target, string name)
    {
        return target.GetType().GetField(name).GetValue(target);
    }
}
