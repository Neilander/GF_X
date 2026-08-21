using System.Reflection;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class CameraControllerScreenEdgePanTests
{
    private static readonly MethodInfo CalculateEightWayDirectionMethod = typeof(CameraController).GetMethod(
        "CalculateEightWayDirection",
        BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo IsInScreenEdgeAreaMethod = typeof(CameraController).GetMethod(
        "IsInScreenEdgeArea",
        BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly MethodInfo CalculateFollowOffsetMethod = typeof(CameraController).GetMethod(
        "CalculateFollowOffset",
        BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo ApplyCameraViewMethod = typeof(CameraController).GetMethod(
        "ApplyCameraView",
        BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly MethodInfo UpdateFollowProxyPositionMethod = typeof(CameraController).GetMethod(
        "UpdateFollowProxyPosition",
        BindingFlags.NonPublic | BindingFlags.Instance);

    [Test]
    public void DefaultCameraViewTableRow_DrivesCurrentOrthographicView()
    {
        string path = Path.Combine(Application.dataPath, "AAAGame/DataTable/CameraViewTable.txt");
        string dataLine = File.ReadLines(path).Single(line => line.StartsWith("\t1\t"));
        var row = new CameraViewTable();

        Assert.That(row.ParseDataRow(dataLine, null), Is.True);
        Assert.That(row.Rotation, Is.EqualTo(new Vector3(35f, 45f, 0f)));
        Assert.That(row.OrthographicSize, Is.EqualTo(12f).Within(0.0001f));
        Assert.That(row.CameraHeight, Is.EqualTo(56.1638f).Within(0.0001f));

        Vector3 offset = InvokeCalculateFollowOffset(row.Rotation, row.CameraHeight);
        Assert.That(offset.x, Is.EqualTo(-56.71719f).Within(0.001f));
        Assert.That(offset.y, Is.EqualTo(56.1638f).Within(0.001f));
        Assert.That(offset.z, Is.EqualTo(-56.71719f).Within(0.001f));
    }

    [Test]
    public void ApplyCameraView_ConfiguresRealCameraPrefabFromTableRow()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AAAGame/Prefabs/Meiyou/Camera.prefab");
        Assert.That(prefab, Is.Not.Null);
        GameObject instance = Object.Instantiate(prefab);

        try
        {
            CameraController controller = instance.GetComponentInChildren<CameraController>(true);
            Assert.That(controller, Is.Not.Null, "Camera prefab must contain CameraController.");
            Camera mainCamera = instance.GetComponentInChildren<Camera>(true);
            Assert.That(mainCamera, Is.Not.Null, "Camera prefab must contain a Unity Camera.");
            FieldInfo mainCameraField = typeof(CameraController).GetField(
                "<mainCam>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(mainCameraField, Is.Not.Null);
            mainCameraField.SetValue(controller, mainCamera);

            string path = Path.Combine(Application.dataPath, "AAAGame/DataTable/CameraViewTable.txt");
            string dataLine = File.ReadLines(path).Single(line => line.StartsWith("\t1\t"));
            var row = new CameraViewTable();
            Assert.That(row.ParseDataRow(dataLine, null), Is.True);

            Assert.That(ApplyCameraViewMethod, Is.Not.Null, "CameraController.ApplyCameraView must exist.");
            ApplyCameraViewMethod.Invoke(controller, new object[] { row, false });

            Assert.That(controller.mainCam.orthographic, Is.True);
            Assert.That(controller.mainCam.orthographicSize, Is.EqualTo(row.OrthographicSize).Within(0.0001f));

            FieldInfo virtualCameraField = typeof(CameraController).GetField(
                "followerVCamera",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(virtualCameraField, Is.Not.Null);
            var virtualCamera = (Component)virtualCameraField.GetValue(controller);
            Assert.That(virtualCamera, Is.Not.Null);
            object lens = ReadMember(virtualCamera, "m_Lens");
            Assert.That((bool)ReadMember(lens, "Orthographic"), Is.True);
            Assert.That((float)ReadMember(lens, "OrthographicSize"), Is.EqualTo(row.OrthographicSize).Within(0.0001f));
            Assert.That(Quaternion.Angle(virtualCamera.transform.rotation, Quaternion.Euler(row.Rotation)), Is.LessThan(0.001f));

            Component transposer = virtualCamera.GetComponentsInChildren<Component>(true)
                .Single(component => component.GetType().Name == "CinemachineTransposer");
            Assert.That(transposer, Is.Not.Null);
            Vector3 expectedOffset = InvokeCalculateFollowOffset(row.Rotation, row.CameraHeight);
            Vector3 actualOffset = (Vector3)ReadMember(transposer, "m_FollowOffset");
            Assert.That(Vector3.Distance(actualOffset, expectedOffset), Is.LessThan(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void CalculateFollowOffset_PitchChangePreservesConfiguredCameraHeight()
    {
        const float cameraHeight = 56.1638f;

        Vector3 offsetAt30Degrees = InvokeCalculateFollowOffset(new Vector3(30f, 45f, 0f), cameraHeight);
        Vector3 offsetAt35Degrees = InvokeCalculateFollowOffset(new Vector3(35f, 45f, 0f), cameraHeight);

        Assert.That(offsetAt30Degrees.y, Is.EqualTo(cameraHeight).Within(0.0001f));
        Assert.That(offsetAt35Degrees.y, Is.EqualTo(cameraHeight).Within(0.0001f));
        Assert.That(new Vector2(offsetAt35Degrees.x, offsetAt35Degrees.z).magnitude,
            Is.LessThan(new Vector2(offsetAt30Degrees.x, offsetAt30Degrees.z).magnitude));
    }

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

    [Test]
    public void UpdateFollowProxyPosition_DoesNotClampToAuthoredTerrainShape()
    {
        GameObject controllerObject = new GameObject(nameof(CameraControllerScreenEdgePanTests));
        GameObject targetObject = new GameObject("Camera target");
        GameObject proxyObject = new GameObject("Camera follow proxy");
        CameraController controller = controllerObject.AddComponent<CameraController>();

        try
        {
            targetObject.transform.position = new Vector3(100f, 2f, 120f);
            SetField(controller, "target", targetObject.transform);
            SetField(controller, "followProxy", proxyObject.transform);
            SetField(controller, "currentPanOffset", new Vector3(8f, 0f, -6f));

            Assert.That(UpdateFollowProxyPositionMethod, Is.Not.Null);
            UpdateFollowProxyPositionMethod.Invoke(controller, null);

            Assert.That(proxyObject.transform.position, Is.EqualTo(new Vector3(108f, 2f, 114f)));
        }
        finally
        {
            Object.DestroyImmediate(controllerObject);
            Object.DestroyImmediate(targetObject);
            Object.DestroyImmediate(proxyObject);
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

    private static Vector3 InvokeCalculateFollowOffset(Vector3 rotation, float cameraHeight)
    {
        Assert.That(CalculateFollowOffsetMethod, Is.Not.Null);
        return (Vector3)CalculateFollowOffsetMethod.Invoke(null, new object[] { rotation, cameraHeight });
    }

    private static object ReadMember(object target, string memberName)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        FieldInfo field = target.GetType().GetField(memberName, Flags);
        if (field != null)
            return field.GetValue(target);

        PropertyInfo property = target.GetType().GetProperty(memberName, Flags);
        Assert.That(property, Is.Not.Null, $"Member '{memberName}' is missing from {target.GetType().FullName}.");
        return property.GetValue(target);
    }

    private static void SetField(CameraController controller, string fieldName, float value)
    {
        FieldInfo field = typeof(CameraController).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);
        field.SetValue(controller, value);
    }

    private static void SetField(CameraController controller, string fieldName, object value)
    {
        FieldInfo field = typeof(CameraController).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);
        field.SetValue(controller, value);
    }
}
