using NUnit.Framework;

using UnityEngine;

public sealed class PositionSelectActionTests
{
    [Test]
    public void SelectorContract_DoesNotExposeGameObjectValidation()
    {
        Assert.IsNull(typeof(ISelector<ISelectable>).GetMethod("Validate"));
        Assert.IsNull(typeof(TargetableSelector).GetMethod("Validate"));
    }

    [Test]
    public void ClampToRadius_UsesExactFixedWorldCoordinates()
    {
        var center = new FixVector2((Fix64)10, (Fix64)(-4));
        var requested = new FixVector2((Fix64)16, (Fix64)4);

        FixVector2 result = PositionSelectAction.ClampToRadius(center, requested, (Fix64)5);
        FixVector2 repeated = PositionSelectAction.ClampToRadius(center, requested, (Fix64)5);

        Assert.AreEqual(result.x.RawValue, repeated.x.RawValue);
        Assert.AreEqual(result.y.RawValue, repeated.y.RawValue);
        Assert.LessOrEqual(FixVector2.SqrMagnitude(result - center).RawValue, ((Fix64)25).RawValue);
        Assert.Greater(FixVector2.Dot(result - center, requested - center).RawValue, 0);
    }

    [Test]
    public void ClampToRadius_PreservesInsidePointAndHandlesZeroRadius()
    {
        var center = new FixVector2(Fix64.FromRaw(100), Fix64.FromRaw(200));
        var inside = new FixVector2(Fix64.FromRaw(103), Fix64.FromRaw(204));

        Assert.AreEqual(inside, PositionSelectAction.ClampToRadius(center, inside, Fix64.One));
        Assert.AreEqual(center, PositionSelectAction.ClampToRadius(center, inside, Fix64.Zero));
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            PositionSelectAction.ClampToRadius(center, inside, -Fix64.One));
    }
    [Test]
    public void StartAction_ViewlessCasterUsesFixedLogicPosition()
    {
        EnsureInputModel();
        var caster = new SimEntityContext
        {
            PositionFixed = new FixVector2(Fix64.FromRaw(1234), Fix64.FromRaw(-5678)),
            Side = SideType.PlayerSide
        };
        var action = ScriptableObject.CreateInstance<PositionSelectAction>();
        try
        {
            action.StartAction(caster, out ActionInfo rawInfo);

            var info = (PositionSelectActionInfo)rawInfo;
            Assert.AreSame(caster, info.selfBody);
            Assert.AreEqual(caster.PositionFixed, info.lastSelectPos);
            Assert.AreEqual(caster.PositionFixed, info.confirmedSelectPos);
            Assert.IsTrue(info.isRunning);
        }
        finally
        {
            Object.DestroyImmediate(action);
        }
    }

    private static void EnsureInputModel()
    {
        System.Reflection.FieldInfo dataModelField = typeof(GF).GetField(
            "<DataModel>k__BackingField",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(dataModelField, "GF.DataModel backing field was not found.");
        var component = dataModelField?.GetValue(null) as GameFramework.DataModelComponent;
        if (component == null)
        {
            var gameObject = new GameObject("PositionSelectActionTests_DataModel");
            component = gameObject.AddComponent<GameFramework.DataModelComponent>();
            dataModelField?.SetValue(null, component);
        }

        System.Reflection.FieldInfo dataModelsField = typeof(GameFramework.DataModelComponent).GetField(
            "m_DataModels",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(dataModelsField, "DataModelComponent.m_DataModels was not found.");
        object dataModels = dataModelsField?.GetValue(component);
        if (dataModels == null)
        {
            dataModels = System.Activator.CreateInstance(dataModelsField.FieldType);
            dataModelsField.SetValue(component, dataModels);
        }

        if (component.GetDataModel<InputModel>() != null)
            return;

        var model = (InputModel)System.Activator.CreateInstance(typeof(InputModel), true);
        System.Type pairType = typeof(GameFramework.DataModelComponent).Assembly.GetType("TypeIdPair");
        Assert.NotNull(pairType, "TypeIdPair was not found.");
        object pair = System.Activator.CreateInstance(
            pairType,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic,
            null,
            new object[] { typeof(InputModel), 0 },
            null);
        System.Reflection.MethodInfo addMethod = dataModels.GetType().GetMethod("Add");
        Assert.NotNull(addMethod, "Data model collection Add method was not found.");
        addMethod.Invoke(dataModels, new[] { pair, model });
        Assert.AreSame(model, component.GetDataModel<InputModel>());
    }
}
