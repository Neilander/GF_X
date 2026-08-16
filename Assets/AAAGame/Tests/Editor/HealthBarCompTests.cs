using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class HealthBarCompTests
{
    [Test]
    public void SetStealthVisualState_ShutdownRestoreDoesNotRefreshPhasePresentation()
    {
        var buildingObject = new GameObject("Building_ShutdownHealthBar_Test");
        var building = buildingObject.AddComponent<BuildingEntity>();

        try
        {
            MethodInfo method = typeof(BuildingEntity).GetMethod(
                "SetStealthVisualState",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(bool), typeof(bool), typeof(float), typeof(bool) },
                null);

            Assert.IsNotNull(method);
            Assert.DoesNotThrow(() => method.Invoke(building, new object[] { false, false, 1f, false }));
            Assert.IsNull(GameObject.Find($"HealthBar_{building.Id}"));
        }
        finally
        {
            Object.DestroyImmediate(buildingObject);
        }
    }

    [Test]
    public void ResolvePositionTarget_PrefersInterpolatedDisplayTransform()
    {
        var rootObject = new GameObject("EntityRoot");
        var displayObject = new GameObject("Display");
        displayObject.transform.SetParent(rootObject.transform, false);

        try
        {
            MethodInfo method = typeof(HealthBarComp).GetMethod(
                "ResolvePositionTarget",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method);
            Assert.AreSame(displayObject.transform, method.Invoke(null, new object[] { rootObject.transform }));
        }
        finally
        {
            Object.DestroyImmediate(rootObject);
        }
    }

    [Test]
    public void ResolvePositionTarget_UsesRootWhenTargetHasNoDisplay()
    {
        var rootObject = new GameObject("PresentationRoot");

        try
        {
            MethodInfo method = typeof(HealthBarComp).GetMethod(
                "ResolvePositionTarget",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method);
            Assert.AreSame(rootObject.transform, method.Invoke(null, new object[] { rootObject.transform }));
        }
        finally
        {
            Object.DestroyImmediate(rootObject);
        }
    }

}
