using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class HealthBarCompTests
{
    [Test]
    public void LevelLoading_HidesWorldHealthBarUntilLoadCompletes()
    {
        var healthBarObject = new GameObject("EnemyHealthBar_Loading_Test");
        Canvas canvas = healthBarObject.AddComponent<Canvas>();
        HealthBarComp healthBar = healthBarObject.AddComponent<HealthBarComp>();
        const int entityId = 876543;

        try
        {
            SetPrivateField(healthBar, "_entityId", entityId);
            SetPrivateField(healthBar, "ownerCanvas", canvas);
            GetActiveBars()[entityId] = healthBar;

            InvokeSetLevelLoading(true);
            Assert.IsFalse(canvas.enabled, "A world-space health bar must be hidden for the entire level load.");

            InvokePrivateInstanceMethod(healthBar, "LateUpdate");
            Assert.IsFalse(canvas.enabled, "LateUpdate must not make a loading health bar visible again.");

            InvokeSetLevelLoading(false);
            Assert.IsTrue(canvas.enabled, "A fog-visible health bar should return after loading completes.");
        }
        finally
        {
            GetActiveBars().Remove(entityId);
            Object.DestroyImmediate(healthBarObject);
        }
    }

    [Test]
    public void LevelLoadingCompletion_DoesNotRevealFogHiddenHealthBar()
    {
        var healthBarObject = new GameObject("FogHiddenHealthBar_Loading_Test");
        Canvas canvas = healthBarObject.AddComponent<Canvas>();
        HealthBarComp healthBar = healthBarObject.AddComponent<HealthBarComp>();
        const int entityId = 876544;

        try
        {
            SetPrivateField(healthBar, "_entityId", entityId);
            SetPrivateField(healthBar, "ownerCanvas", canvas);
            SetPrivateField(healthBar, "_visibleByFog", false);
            GetActiveBars()[entityId] = healthBar;

            InvokeSetLevelLoading(true);
            InvokeSetLevelLoading(false);

            Assert.IsFalse(canvas.enabled, "Completing a load must preserve fog visibility.");
        }
        finally
        {
            GetActiveBars().Remove(entityId);
            Object.DestroyImmediate(healthBarObject);
        }
    }

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

    private static System.Collections.Generic.Dictionary<int, HealthBarComp> GetActiveBars()
    {
        FieldInfo field = typeof(HealthBarComp).GetField("ActiveBars", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        return (System.Collections.Generic.Dictionary<int, HealthBarComp>)field.GetValue(null);
    }

    private static void InvokeSetLevelLoading(bool isLoading)
    {
        MethodInfo method = typeof(HealthBarComp).GetMethod(
            "SetLevelLoading",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        method.Invoke(null, new object[] { isLoading });
    }

    private static void InvokePrivateInstanceMethod(HealthBarComp healthBar, string methodName)
    {
        MethodInfo method = typeof(HealthBarComp).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        method.Invoke(healthBar, null);
    }

    private static void SetPrivateField<T>(HealthBarComp healthBar, string fieldName, T value)
    {
        FieldInfo field = typeof(HealthBarComp).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        field.SetValue(healthBar, value);
    }
}
