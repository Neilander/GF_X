using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AAAGame.Tools.Editor;
using GiantGrey.TileWorldCreator;
using GiantGrey.TileWorldCreator.Utilities;
using NUnit.Framework;
using UnityEngine;

public sealed class LdtkImporterBuildSafetyTests
{
    private static readonly MethodInfo UpdateEditorCoroutines =
        typeof(EditorCoroutines).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo StopAllEditorCoroutines =
        typeof(EditorCoroutines).GetMethod("StopAll", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo ValidatePlatformColliderMeshes =
        typeof(LdtkToTileWorldCreatorImporterWindow).GetMethod(
            "ValidatePlatformColliderMeshes",
            BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo GetManagersToRebuild =
        typeof(PaintSceneOverlay).GetMethod(
            "GetManagersToRebuild",
            BindingFlags.NonPublic | BindingFlags.Static);

    [Test]
    public void ValidatePlatformColliderMeshes_SharedRenderMesh_IsAccepted()
    {
        GameObject root = new GameObject("Terrain");
        Mesh mesh = CreateTriangleMesh("Shared");
        try
        {
            GameObject cluster = CreatePlatformCluster(root.transform, mesh, mesh);
            Assert.That(cluster, Is.Not.Null);
            Assert.That(ValidatePlatformColliderMeshes, Is.Not.Null);
            Assert.DoesNotThrow(() => ValidatePlatformColliderMeshes.Invoke(null, new object[] { root }));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void ValidatePlatformColliderMeshes_SeparateGeneratedCollider_IsRejectedBeforeSave()
    {
        GameObject root = new GameObject("Terrain");
        Mesh renderMesh = CreateTriangleMesh("Render");
        Mesh colliderMesh = CreateTriangleMesh("Collider");
        try
        {
            CreatePlatformCluster(root.transform, renderMesh, colliderMesh);
            Assert.That(ValidatePlatformColliderMeshes, Is.Not.Null);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                () => ValidatePlatformColliderMeshes.Invoke(null, new object[] { root }));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(exception.InnerException.Message, Does.Contain("export was stopped before writing assets"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(renderMesh);
            UnityEngine.Object.DestroyImmediate(colliderMesh);
        }
    }

    [Test]
    public void EditorCoroutines_NestedEnumerator_RemainsActiveUntilOuterCompletes()
    {
        Assert.That(UpdateEditorCoroutines, Is.Not.Null);
        Assert.That(StopAllEditorCoroutines, Is.Not.Null);
        StopAllEditorCoroutines.Invoke(null, null);

        int completedSteps = 0;
        try
        {
            EditorCoroutines.Execute(OuterCoroutine(() => completedSteps++));

            UpdateEditorCoroutines.Invoke(null, null);
            Assert.That(EditorCoroutines.HasActiveCoroutines, Is.True);

            UpdateEditorCoroutines.Invoke(null, null);
            Assert.That(completedSteps, Is.EqualTo(1));
            Assert.That(EditorCoroutines.HasActiveCoroutines, Is.True);

            UpdateEditorCoroutines.Invoke(null, null);
            Assert.That(EditorCoroutines.HasActiveCoroutines, Is.True);

            UpdateEditorCoroutines.Invoke(null, null);
            Assert.That(completedSteps, Is.EqualTo(2));
            Assert.That(EditorCoroutines.HasActiveCoroutines, Is.False);
        }
        finally
        {
            StopAllEditorCoroutines.Invoke(null, null);
        }
    }

    [Test]
    public void TilesBuildLayer_ResetWhileEditorGenerationIsActive_IsRejected()
    {
        TilesBuildLayer layer = ScriptableObject.CreateInstance<TilesBuildLayer>();
        FieldInfo activeField = typeof(TilesBuildLayer).GetField(
            "editorGenerationActive",
            BindingFlags.NonPublic | BindingFlags.Instance);
        try
        {
            Assert.That(activeField, Is.Not.Null);
            activeField.SetValue(layer, true);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => layer.ResetLayer(null));
            Assert.That(exception.Message, Does.Contain("before its previous editor generation completed"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(layer);
        }
    }

    [Test]
    public void PaintSceneOverlay_MultipleChangedBlueprintsForOneConfiguration_RebuildsManagerOnce()
    {
        Configuration configuration = ScriptableObject.CreateInstance<Configuration>();
        GameObject firstManagerObject = new GameObject("First changed configuration manager");
        GameObject secondManagerObject = new GameObject("Second changed configuration manager");
        TileWorldCreatorManager firstManager = firstManagerObject.AddComponent<TileWorldCreatorManager>();
        TileWorldCreatorManager secondManager = secondManagerObject.AddComponent<TileWorldCreatorManager>();
        firstManager.configuration = configuration;
        secondManager.configuration = configuration;
        try
        {
            Assert.That(GetManagersToRebuild, Is.Not.Null);
            var changedConfigurations = new List<Configuration>();
            for (int i = 0; i < 20; i++)
                changedConfigurations.Add(configuration);

            object result = GetManagersToRebuild.Invoke(
                null,
                new object[]
                {
                    changedConfigurations,
                    new[] { firstManager, secondManager },
                    false,
                });

            var managers = (List<TileWorldCreatorManager>)result;
            Assert.That(managers, Is.EqualTo(new[] { firstManager }));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(firstManagerObject);
            UnityEngine.Object.DestroyImmediate(secondManagerObject);
            UnityEngine.Object.DestroyImmediate(configuration);
        }
    }

    [Test]
    public void PaintSceneOverlay_ActiveBuild_DoesNotScheduleChangedConfiguration()
    {
        Configuration configuration = ScriptableObject.CreateInstance<Configuration>();
        GameObject managerObject = new GameObject("Active build manager");
        TileWorldCreatorManager manager = managerObject.AddComponent<TileWorldCreatorManager>();
        manager.configuration = configuration;
        try
        {
            object result = GetManagersToRebuild.Invoke(
                null,
                new object[]
                {
                    new[] { configuration },
                    new[] { manager },
                    true,
                });

            Assert.That((List<TileWorldCreatorManager>)result, Is.Empty);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(managerObject);
            UnityEngine.Object.DestroyImmediate(configuration);
        }
    }

    [Test]
    public void PaintSceneOverlay_ChangedBlueprintConfiguration_SelectsOnlyMatchingManager()
    {
        Configuration changedConfiguration = ScriptableObject.CreateInstance<Configuration>();
        Configuration unchangedConfiguration = ScriptableObject.CreateInstance<Configuration>();
        GameObject changedObject = new GameObject("Changed manager");
        GameObject unchangedObject = new GameObject("Unchanged manager");
        TileWorldCreatorManager changedManager = changedObject.AddComponent<TileWorldCreatorManager>();
        TileWorldCreatorManager unchangedManager = unchangedObject.AddComponent<TileWorldCreatorManager>();
        changedManager.configuration = changedConfiguration;
        unchangedManager.configuration = unchangedConfiguration;
        try
        {
            object result = GetManagersToRebuild.Invoke(
                null,
                new object[]
                {
                    new[] { changedConfiguration },
                    new[] { unchangedManager, changedManager },
                    false,
                });

            var managers = (List<TileWorldCreatorManager>)result;
            Assert.That(managers, Is.EqualTo(new[] { changedManager }));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(changedObject);
            UnityEngine.Object.DestroyImmediate(unchangedObject);
            UnityEngine.Object.DestroyImmediate(changedConfiguration);
            UnityEngine.Object.DestroyImmediate(unchangedConfiguration);
        }
    }

    private static GameObject CreatePlatformCluster(Transform parent, Mesh renderMesh, Mesh colliderMesh)
    {
        var cluster = new GameObject("Build Plane_H0_Cluster_7011");
        cluster.transform.SetParent(parent, false);
        cluster.AddComponent<MeshFilter>().sharedMesh = renderMesh;
        cluster.AddComponent<MeshCollider>().sharedMesh = colliderMesh;
        return cluster;
    }

    private static Mesh CreateTriangleMesh(string name)
    {
        var mesh = new Mesh { name = name };
        mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward };
        mesh.triangles = new[] { 0, 1, 2 };
        return mesh;
    }

    private static IEnumerator OuterCoroutine(Action onStep)
    {
        yield return InnerCoroutine(onStep);
        onStep();
    }

    private static IEnumerator InnerCoroutine(Action onStep)
    {
        onStep();
        yield return null;
    }
}
