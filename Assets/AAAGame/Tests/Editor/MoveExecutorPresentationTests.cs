using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class MoveExecutorPresentationTests
{
    private const string RampPrefabPath = "Assets/AAAGame/Models/SlopePlaceholder/Ramp45.prefab";

    [Test]
    public void SyncPresentationPosition_ClimbsSlopeThroughCharacterController()
    {
        GameObject floor = CreateCube("PresentationFloor", new Vector3(0f, -0.5f, -1f), new Vector3(4f, 1f, 2f));
        GameObject ramp = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RampPrefabPath));
        GameObject unit = new GameObject("PresentationUnit");
        CharacterController controller = unit.AddComponent<CharacterController>();
        controller.radius = 0.2f;
        controller.height = 1.6f;
        controller.center = new Vector3(0f, 0.8f, 0f);
        controller.slopeLimit = 45f;
        controller.skinWidth = 0.001f;
        MoveExecutor executor = unit.AddComponent<MoveExecutor>();
        executor.Init(controller, 0);
        unit.transform.position = new Vector3(0f, 0f, -1f);
        Physics.SyncTransforms();

        try
        {
            for (int i = 0; i < 35; i++)
                executor.SyncPresentationPosition(new Vector3(0f, 0f, -0.95f + i * 0.05f), 0.02f);

            Assert.That(unit.transform.position.y, Is.GreaterThan(0.5f));
        }
        finally
        {
            Object.DestroyImmediate(unit);
            Object.DestroyImmediate(ramp);
            Object.DestroyImmediate(floor);
        }
    }

    [Test]
    public void SyncPresentationPosition_UsesAuthoritativeHorizontalPositionAcrossWall()
    {
        GameObject floor = CreateCube("PresentationFloor", new Vector3(0f, -0.5f, -1f), new Vector3(4f, 1f, 4f));
        GameObject wall = CreateCube("PresentationWall", new Vector3(0f, 1f, 0f), new Vector3(4f, 2f, 0.2f));
        GameObject unit = new GameObject("PresentationUnit");
        CharacterController controller = unit.AddComponent<CharacterController>();
        controller.radius = 0.2f;
        controller.height = 1.6f;
        controller.center = new Vector3(0f, 0.8f, 0f);
        controller.slopeLimit = 45f;
        controller.skinWidth = 0.001f;
        MoveExecutor executor = unit.AddComponent<MoveExecutor>();
        executor.Init(controller, 0);
        unit.transform.position = new Vector3(0f, 0f, -1f);
        Physics.SyncTransforms();

        try
        {
            for (int i = 0; i < 60; i++)
                executor.SyncPresentationPosition(new Vector3(0f, 0f, 1f), 0.02f);

            Assert.That(unit.transform.position.z, Is.EqualTo(1f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(unit);
            Object.DestroyImmediate(wall);
            Object.DestroyImmediate(floor);
        }
    }

    [Test]
    public void SyncPresentationPosition_LogicRestoreOntoRaisedGround_DoesNotSinkBelowSurface()
    {
        GameObject lowerGround = CreateCube(
            "PresentationLowerGround",
            new Vector3(-2f, -0.5f, 0f),
            new Vector3(2f, 1f, 2f));
        GameObject raisedGround = CreateCube(
            "PresentationRaisedGround",
            new Vector3(2f, 0f, 0f),
            new Vector3(2f, 2f, 2f));
        GameObject unit = new GameObject("PresentationUnit");
        CharacterController controller = unit.AddComponent<CharacterController>();
        controller.radius = 0.2f;
        controller.height = 1.6f;
        controller.center = new Vector3(0f, 0.8f, 0f);
        controller.slopeLimit = 45f;
        controller.skinWidth = 0.001f;
        MoveExecutor executor = unit.AddComponent<MoveExecutor>();
        executor.Init(controller, 0);
        unit.transform.position = new Vector3(-2f, 0f, 0f);
        Physics.SyncTransforms();

        try
        {
            Vector3 restoredLogicPosition = new Vector3(2f, 0f, 0f);
            for (int i = 0; i < 30; i++)
                executor.SyncPresentationPosition(restoredLogicPosition, 0.02f);

            Assert.That(unit.transform.position.x, Is.EqualTo(2f).Within(0.001f));
            Assert.That(unit.transform.position.y, Is.EqualTo(1f).Within(0.01f));
        }
        finally
        {
            Object.DestroyImmediate(unit);
            Object.DestroyImmediate(raisedGround);
            Object.DestroyImmediate(lowerGround);
        }
    }

    private static GameObject CreateCube(string name, Vector3 position, Vector3 scale)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.layer = LayerMask.NameToLayer("Ground");
        cube.transform.position = position;
        cube.transform.localScale = scale;
        return cube;
    }
}
