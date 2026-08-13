using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class CameraControllerScreenEdgePanTests
{
    private static readonly MethodInfo CalculateEightWayDirectionMethod = typeof(CameraController).GetMethod(
        "CalculateEightWayDirection",
        BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo IsInScreenEdgeAreaMethod = typeof(CameraController).GetMethod(
        "IsInScreenEdgeArea",
        BindingFlags.NonPublic | BindingFlags.Instance);

    [Test]
    public void CalculateEightWayDirection_SmallMovementAlongRightEdge_KeepsRightDirection()
    {
        Vector2 upperDirection = InvokeCalculateEightWayDirection(new Vector2(1f, 0.1f));
        Vector2 lowerDirection = InvokeCalculateEightWayDirection(new Vector2(1f, -0.1f));

        Assert.That(upperDirection.x, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(upperDirection.y, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(lowerDirection.x, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(lowerDirection.y, Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void CalculateEightWayDirection_TopRightCorner_ReturnsDiagonalDirection()
    {
        Vector2 direction = InvokeCalculateEightWayDirection(Vector2.one);
        Vector2 expected = Vector2.one.normalized;

        Assert.That(direction.x, Is.EqualTo(expected.x).Within(0.0001f));
        Assert.That(direction.y, Is.EqualTo(expected.y).Within(0.0001f));
    }

    [Test]
    public void IsInScreenEdgeArea_ActivatedState_UsesLargerExitBuffer()
    {
        GameObject gameObject = new GameObject(nameof(CameraControllerScreenEdgePanTests));
        CameraController controller = gameObject.AddComponent<CameraController>();

        try
        {
            SetField(controller, "edgeThresholdRatio", 0.1f);
            SetField(controller, "edgeExitThresholdRatio", 0.25f);
            Vector2 insideExitBuffer = new Vector2(Screen.width * 0.2f, Screen.height * 0.5f);

            Assert.That(InvokeIsInScreenEdgeArea(controller, insideExitBuffer, false), Is.False);
            Assert.That(InvokeIsInScreenEdgeArea(controller, insideExitBuffer, true), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    private static Vector2 InvokeCalculateEightWayDirection(Vector2 direction)
    {
        Assert.That(CalculateEightWayDirectionMethod, Is.Not.Null);
        return (Vector2)CalculateEightWayDirectionMethod.Invoke(null, new object[] { direction });
    }

    private static bool InvokeIsInScreenEdgeArea(CameraController controller, Vector2 mousePosition, bool isActivated)
    {
        Assert.That(IsInScreenEdgeAreaMethod, Is.Not.Null);
        return (bool)IsInScreenEdgeAreaMethod.Invoke(controller, new object[] { mousePosition, isActivated });
    }

    private static void SetField(CameraController controller, string fieldName, float value)
    {
        FieldInfo field = typeof(CameraController).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);
        field.SetValue(controller, value);
    }
}
