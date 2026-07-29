using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class HealthBarCompTests
{
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
