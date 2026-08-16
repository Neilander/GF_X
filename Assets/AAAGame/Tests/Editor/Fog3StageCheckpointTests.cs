using AAAGame.MiniMap.FOG3;
using NUnit.Framework;
using AAAGame.Card;
using System.Reflection;
using UnityEngine;
using UnityGameFramework.Runtime;

public sealed class Fog3StageCheckpointTests
{
    [Test]
    public void ViewSettings_HiddenFogIsFullyOpaqueByDefault()
    {
        Assert.AreEqual(1f, new Fog3ViewSettings().HiddenColor.a);
    }

    [Test]
    public void TerrainConformingMesh_SeparatesHighAndLowCellTopsAndDepthTestsTheCliffWall()
    {
        GameObject lowGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject highGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject viewObject = new GameObject("Fog3ConformingCliffTestView");
        try
        {
            lowGround.transform.position = new Vector3(0.5f, -0.5f, 0.5f);
            lowGround.transform.localScale = Vector3.one;
            highGround.transform.position = new Vector3(1.5f, 0.5f, 0.5f);
            highGround.transform.localScale = Vector3.one;
            Physics.SyncTransforms();

            var terrain = new Fog3TerrainInfo(
                2,
                1,
                1f,
                Vector3.zero,
                new[] { true, true },
                new[] { 0, 1 },
                null,
                null,
                0.2f,
                "ConformingCliffTest");
            Fog3WorldOverlayView view = BuildTerrainConformingView(viewObject, terrain);

            Mesh mesh = viewObject.transform.Find("FOG3_WorldOverlay").GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Assert.AreEqual(12, vertices.Length, "Two independent tops and one depth-tested cliff wall are required.");
            Assert.AreEqual(18, mesh.triangles.Length);
            Assert.AreEqual(0.08f, vertices[2].y, 0.001f);
            Assert.AreEqual(1.08f, vertices[4].y, 0.001f);
            int[] triangles = mesh.triangles;
            for (int i = 0; i < 6; i++)
                Assert.Less(triangles[i], 4);
            for (int i = 6; i < 12; i++)
                Assert.GreaterOrEqual(triangles[i], 4);
            Assert.AreEqual(vertices[4].x, vertices[8].x, 0.001f);
            Assert.AreEqual(vertices[2].x, vertices[10].x, 0.001f);
            Assert.Greater(vertices[8].x - vertices[10].x, 0.001f, "The cliff wall must bridge the inset gap between both cell tops.");
            Assert.AreEqual(vertices[4].z, vertices[10].z, 0.001f);
            Assert.AreEqual(vertices[2].y, vertices[10].y, 0.001f);
            Assert.AreEqual(
                (int)UnityEngine.Rendering.CompareFunction.LessEqual,
                view.FogMaterial.GetInt("_ZTest"));
            Assert.IsFalse(view.FogMeshUsesCameraProjectionGrid);
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
            Object.DestroyImmediate(highGround);
            Object.DestroyImmediate(lowGround);
        }
    }

    [Test]
    public void TerrainConformingMesh_PlacesEveryCliffWallOnSharedHeightBoundary()
    {
        GameObject lowGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject highGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject viewObject = new GameObject("Fog3ConformingCliffWindingTestView");
        try
        {
            lowGround.transform.position = new Vector3(1.5f, -0.5f, 1.5f);
            lowGround.transform.localScale = new Vector3(3f, 1f, 3f);
            highGround.transform.position = new Vector3(1.5f, 0.5f, 1.5f);
            highGround.transform.localScale = Vector3.one;
            Physics.SyncTransforms();

            var terrain = new Fog3TerrainInfo(
                3,
                3,
                1f,
                Vector3.zero,
                new[] { true, true, true, true, true, true, true, true, true },
                new[] { 0, 0, 0, 0, 1, 0, 0, 0, 0 },
                null,
                null,
                "ConformingCliffWindingTest");
            BuildTerrainConformingView(viewObject, terrain);

            Mesh mesh = viewObject.transform.Find("FOG3_WorldOverlay").GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            var wallCenters = new System.Collections.Generic.List<Vector3>();
            const int topTriangleIndexCount = 3 * 3 * 6;
            for (int i = topTriangleIndexCount; i < triangles.Length; i += 6)
            {
                int firstWallVertex = triangles[i] - triangles[i] % 4;
                wallCenters.Add(
                    (vertices[firstWallVertex] +
                     vertices[firstWallVertex + 1] +
                     vertices[firstWallVertex + 2] +
                     vertices[firstWallVertex + 3]) * 0.25f);
            }

            Assert.AreEqual(4, wallCenters.Count);
            Assert.IsTrue(wallCenters.Exists(center => Mathf.Abs(center.x - 1f) < 0.001f && Mathf.Abs(center.z - 1.5f) < 0.001f));
            Assert.IsTrue(wallCenters.Exists(center => Mathf.Abs(center.x - 2f) < 0.001f && Mathf.Abs(center.z - 1.5f) < 0.001f));
            Assert.IsTrue(wallCenters.Exists(center => Mathf.Abs(center.x - 1.5f) < 0.001f && Mathf.Abs(center.z - 1f) < 0.001f));
            Assert.IsTrue(wallCenters.Exists(center => Mathf.Abs(center.x - 1.5f) < 0.001f && Mathf.Abs(center.z - 2f) < 0.001f));
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
            Object.DestroyImmediate(highGround);
            Object.DestroyImmediate(lowGround);
        }
    }

    [Test]
    public void TerrainConformingMesh_ClosesJunctionBetweenVoidAndInsetLowCliffWalls()
    {
        GameObject baseGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject lowGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject highGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject viewObject = new GameObject("Fog3ConformingCliffJunctionTestView");
        try
        {
            baseGround.transform.position = new Vector3(1f, -0.5f, 1f);
            baseGround.transform.localScale = new Vector3(2f, 1f, 2f);
            lowGround.transform.position = new Vector3(1.5f, 0.5f, 0.5f);
            lowGround.transform.localScale = Vector3.one;
            highGround.transform.position = new Vector3(1f, 1.5f, 1.5f);
            highGround.transform.localScale = new Vector3(2f, 1f, 1f);
            Physics.SyncTransforms();

            var terrain = new Fog3TerrainInfo(
                2,
                2,
                1f,
                Vector3.zero,
                new[] { false, true, true, true },
                new[] { -1, 1, 2, 2 },
                null,
                null,
                0.2f,
                "ConformingCliffJunctionTest");
            BuildTerrainConformingView(viewObject, terrain);

            Mesh mesh = viewObject.transform.Find("FOG3_WorldOverlay").GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Assert.AreEqual(31, vertices.Length, "Four tops, three cliff walls, and one junction triangle are required.");
            Assert.AreEqual(45, triangles.Length);
            Assert.AreEqual(new Vector3(1f, 2.08f, 1.2f), vertices[28]);
            Assert.AreEqual(new Vector3(1f, 0.08f, 1f), vertices[29]);
            Assert.AreEqual(new Vector3(1.2f, 1.08f, 0.8f), vertices[30]);
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
            Object.DestroyImmediate(highGround);
            Object.DestroyImmediate(lowGround);
            Object.DestroyImmediate(baseGround);
        }
    }

    [Test]
    public void TerrainConformingMesh_ShrinksFlatPlatformOuterEdgesByConfiguredInset()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject viewObject = new GameObject("Fog3ConformingInsetTestView");
        try
        {
            ground.transform.position = new Vector3(1f, -0.5f, 0.5f);
            ground.transform.localScale = new Vector3(2f, 1f, 1f);
            Physics.SyncTransforms();

            var terrain = new Fog3TerrainInfo(
                2,
                1,
                1f,
                Vector3.zero,
                new[] { true, true },
                new[] { 0, 0 },
                null,
                null,
                0.2f,
                "ConformingInsetTest");
            BuildTerrainConformingView(viewObject, terrain);

            Vector3[] vertices = viewObject.transform.Find("FOG3_WorldOverlay").GetComponent<MeshFilter>().sharedMesh.vertices;
            Assert.AreEqual(8, vertices.Length);
            Assert.AreEqual(0.2f, vertices[0].x, 0.001f);
            Assert.AreEqual(1f, vertices[2].x, 0.001f);
            Assert.AreEqual(1f, vertices[4].x, 0.001f);
            Assert.AreEqual(1.8f, vertices[6].x, 0.001f);
            Assert.AreEqual(0.2f, vertices[0].z, 0.001f);
            Assert.AreEqual(0.8f, vertices[1].z, 0.001f);
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
            Object.DestroyImmediate(ground);
        }
    }

    [Test]
    public void TerrainConformingMesh_ExtendsOnlyContinuousSlopeComponentEndpoints()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject viewObject = new GameObject("Fog3ConformingSlopeInsetTestView");
        try
        {
            ground.transform.position = new Vector3(1f, -0.5f, 0.5f);
            ground.transform.localScale = new Vector3(2.4f, 1f, 1f);
            Physics.SyncTransforms();

            var slopeCells = new[]
            {
                new Fog3SlopeCellInfo(1, 0, 0, 2, 1),
                new Fog3SlopeCellInfo(1, 0, 1, 2, 1),
            };
            var terrain = new Fog3TerrainInfo(
                2,
                1,
                1f,
                Vector3.zero,
                new[] { true, true },
                new[] { 0, 0 },
                new[] { true, true },
                slopeCells,
                0.2f,
                "ConformingSlopeInsetTest");
            BuildTerrainConformingView(viewObject, terrain);

            Vector3[] vertices = viewObject.transform.Find("FOG3_WorldOverlay").GetComponent<MeshFilter>().sharedMesh.vertices;
            Assert.AreEqual(-0.2f, vertices[0].x, 0.001f);
            Assert.AreEqual(1f, vertices[2].x, 0.001f);
            Assert.AreEqual(1f, vertices[4].x, 0.001f);
            Assert.AreEqual(2.2f, vertices[6].x, 0.001f);
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
            Object.DestroyImmediate(ground);
        }
    }

    [Test]
    public void ProjectedCloudMesh_AlignsDifferentTerrainHeightsInScreenSpace()
    {
        Camera[] existingCameras = Object.FindObjectsOfType<Camera>();
        var existingCameraStates = new bool[existingCameras.Length];
        for (int i = 0; i < existingCameras.Length; i++)
        {
            existingCameraStates[i] = existingCameras[i].enabled;
            existingCameras[i].enabled = false;
        }

        GameObject cameraObject = new GameObject("Fog3ProjectionTestCamera");
        GameObject lowGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject highGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject entityRoot = new GameObject("Fog3ProjectionEntityBlocker");
        GameObject entityCollider = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject viewObject = new GameObject("Fog3ProjectionTestView");
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.transform.position = new Vector3(4f, 8f, -4f);
            camera.transform.LookAt(new Vector3(1f, 1f, 0.5f));

            lowGround.transform.position = new Vector3(0.5f, -0.5f, 0.5f);
            lowGround.transform.localScale = new Vector3(0.99f, 1f, 0.99f);
            highGround.transform.position = new Vector3(1.5f, 1.5f, 0.5f);
            highGround.transform.localScale = new Vector3(0.99f, 1f, 0.99f);
            entityRoot.AddComponent<ProjectionOccluderEntity>();
            entityCollider.transform.SetParent(entityRoot.transform);
            entityCollider.transform.position = new Vector3(0.5f, 2f, 0.5f);
            entityCollider.transform.localScale = new Vector3(0.5f, 4f, 0.5f);
            Physics.SyncTransforms();

            var terrain = new Fog3TerrainInfo(
                2,
                1,
                1f,
                Vector3.zero,
                new[] { true, true },
                "ProjectedCloudMeshTest");
            var settings = new Fog3ViewSettings
            {
                SurfaceMode = Fog3OverlaySurfaceMode.CloudLayer,
                ProjectCloudLayerToCameraView = true,
                OutsideMaskPadding = 0f,
                DrawOverSceneGeometry = true,
                OverlayAlwaysOnTopShader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop"),
            };
            Fog3WorldOverlayView view = viewObject.AddComponent<Fog3WorldOverlayView>();
            view.Build(terrain, settings, 5f, Physics.DefaultRaycastLayers, Vector3.zero);

            Mesh mesh = viewObject.transform.Find("FOG3_WorldOverlay").GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Assert.IsTrue(view.FogMeshUsesCameraProjectionGrid);
            Assert.AreEqual(8, vertices.Length);
            Assert.Less(
                Vector2.Distance(
                    camera.WorldToScreenPoint(view.transform.TransformPoint(vertices[0])),
                    camera.WorldToScreenPoint(new Vector3(0f, 0f, 0f))),
                0.001f);
            Assert.Less(
                Vector2.Distance(
                    camera.WorldToScreenPoint(view.transform.TransformPoint(vertices[2])),
                    camera.WorldToScreenPoint(new Vector3(1f, 0f, 0f))),
                0.001f);
            Assert.Less(
                Vector2.Distance(
                    camera.WorldToScreenPoint(view.transform.TransformPoint(vertices[4])),
                    camera.WorldToScreenPoint(new Vector3(1f, 2f, 0f))),
                0.001f);
            Assert.Less(
                Vector2.Distance(
                    camera.WorldToScreenPoint(view.transform.TransformPoint(vertices[6])),
                    camera.WorldToScreenPoint(new Vector3(2f, 2f, 0f))),
                0.001f);
        }
        finally
        {
            Object.DestroyImmediate(viewObject);
            Object.DestroyImmediate(entityRoot);
            Object.DestroyImmediate(highGround);
            Object.DestroyImmediate(lowGround);
            Object.DestroyImmediate(cameraObject);
            for (int i = 0; i < existingCameras.Length; i++)
                existingCameras[i].enabled = existingCameraStates[i];
        }
    }

    private sealed class ProjectionOccluderEntity : EntityLogic
    {
    }

    [Test]
    public void PerformanceDiagnostics_AreDisabledByDefault()
    {
        GameObject managerObject = new GameObject("Fog3PerformanceDiagnosticsDefaultManager");
        managerObject.SetActive(false);
        try
        {
            Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
            FieldInfo diagnosticsField = typeof(Fog3Manager).GetField(
                "logPerformanceDiagnostics",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(diagnosticsField);
            Assert.IsFalse((bool)diagnosticsField.GetValue(manager));
        }
        finally
        {
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void RepeatedSceneReadyNotification_DoesNotReplaceInitializedMap()
    {
        GameObject managerObject = new GameObject("Fog3RepeatedSceneReadyManager");
        managerObject.SetActive(false);
        try
        {
            var controller = new Fog3Controller();
            controller.Initialize(CreateTerrainInfo(new[] { true, true, true, true, true, true }));
            Fog3MapData initializedMap = controller.MapData;
            Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
            typeof(Fog3Manager).GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, controller);
            typeof(Fog3Manager).GetField("isInitialized", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, true);

            MethodInfo handleSceneReady = typeof(Fog3Manager).GetMethod(
                "HandleSceneBecameAvailable",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(handleSceneReady);

            handleSceneReady.Invoke(manager, new object[] { "Game", "test duplicate notification" });

            Assert.IsTrue(manager.IsInitialized);
            Assert.AreSame(initializedMap, manager.MapData);
        }
        finally
        {
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void VisibilityPresentation_BecomesReadyOnlyAfterALogicFrame()
    {
        GameObject managerObject = new GameObject("Fog3LogicFramePresentationManager");
        EntityRegistry.Clear();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
        LogicTimeControlService.BeginTimeline();
        try
        {
            var controller = new Fog3Controller();
            controller.Initialize(CreateTerrainInfo(new[] { true, true, true, true, true, true }));
            Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
            typeof(Fog3Manager).GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, controller);
            typeof(Fog3Manager).GetField("isInitialized", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, true);
            MethodInfo presentVisibility = typeof(Fog3Manager).GetMethod(
                "OnVisibilityUpdated",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(presentVisibility);

            presentVisibility.Invoke(manager, new object[] { controller.MapData });
            Assert.IsFalse(manager.HasPresentedLogicFrameVisibility);

            LogicTimeControlService.BeginFrame(1);
            presentVisibility.Invoke(manager, new object[] { controller.MapData });
            Assert.IsTrue(manager.HasPresentedLogicFrameVisibility);
        }
        finally
        {
            if (LogicTimeControlService.IsActive)
                LogicTimeControlService.EndTimeline();
            EntityRegistry.Clear();
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void ExplorationCheckpoint_RestoresExploredBitsButNotTransientVisibility()
    {
        Fog3MapData map = CreateMap(new[] { true, true, true, true, true, true });
        map.MarkExplored(1, 0);
        map.MarkExplored(2, 1);
        map.AddVisibility(1, 0, 1f);
        map.AddVisibility(2, 1, 0.5f);
        Fog3ExplorationCheckpoint checkpoint = map.CaptureExplorationCheckpoint();

        map.ResetExploration();
        map.MarkExplored(0, 1);
        map.AddVisibility(0, 1, 1f);
        map.RestoreExplorationCheckpoint(checkpoint);

        Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(1, 0));
        Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(2, 1));
        Assert.AreEqual(Fog3CellState.Hidden, map.GetCellState(0, 1));
        Assert.AreEqual(0f, map.GetVisibility(1, 0));
        Assert.IsTrue(map.IsDirty);
    }

    [Test]
    public void RenderVisibility_DoesNotMutateLogicExploration()
    {
        Fog3MapData map = CreateMap(new[] { true, true, true, true, true, true });

        map.AddVisibility(1, 0, 1f);

        Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(1, 0));
        Assert.IsFalse(map.IsExplored(1, 0));
        Assert.AreEqual(0, map.ExploredCellCount);
    }

    [Test]
    public void FixedWorldToGrid_UsesOneBoundaryMappingForLogicAndPresentation()
    {
        var map = new Fog3MapData(new Fog3TerrainInfo(
            4,
            1,
            0.09f,
            new Vector3(10.08f, 0f, 5.04f),
            new[] { true, true, true, true },
            "FixedWorldToGridBoundary"));
        FixVector2 logicPosition = new FixVector2(
            Fix64.FromRaw(42025),
            (Fix64)5.04f);
        Vector3 presentationPosition = new Vector3(
            (float)logicPosition.x,
            0f,
            (float)logicPosition.y);

        Assert.IsTrue(map.WorldToGrid(logicPosition, out int logicX, out int logicY));
        Assert.IsTrue(map.WorldToGrid(presentationPosition, out int presentationX, out int presentationY));
        Assert.AreEqual(2, presentationX, "Authored float grid boundary fixture changed.");
        Assert.AreEqual(presentationX, logicX);
        Assert.AreEqual(presentationY, logicY);
    }

    [Test]
    public void WorldToGrid_UsesFloorDivisionForNegativeOffsets()
    {
        var map = new Fog3MapData(new Fog3TerrainInfo(
            4,
            1,
            0.09f,
            Vector3.zero,
            new[] { true, true, true, true },
            "WorldToGridNegativeOffset"));
        FixVector2 logicPosition = new FixVector2(Fix64.FromRaw(-1), Fix64.Zero);
        Vector3 presentationPosition = new Vector3((float)logicPosition.x, 0f, 0f);

        Assert.IsFalse(map.WorldToGrid(logicPosition, out int logicX, out int logicY));
        Assert.IsFalse(map.WorldToGrid(presentationPosition, out int presentationX, out int presentationY));
        Assert.AreEqual(-1, logicX);
        Assert.AreEqual(logicX, presentationX);
        Assert.AreEqual(0, logicY);
        Assert.AreEqual(logicY, presentationY);
    }

    [Test]
    public void Controller_PublishesAuthoritativeVisibilityWithoutRecalculatingIt()
    {
        var controller = new Fog3Controller();
        controller.Initialize(CreateTerrainInfo(new[] { true, true, true, true, true, true }));
        controller.MapData.MarkVisible(1, 0);
        int publishedCount = 0;
        controller.VisibilityUpdated += map =>
        {
            publishedCount++;
            Assert.AreSame(controller.MapData, map);
        };

        controller.PublishAuthoritativeVisibility(false);

        Assert.AreEqual(1, publishedCount);
        Assert.AreEqual(Fog3CellState.Hidden, controller.MapData.GetCellState(0, 0));
        Assert.AreEqual(Fog3CellState.Visible, controller.MapData.GetCellState(1, 0));
        Assert.IsFalse(controller.MapData.IsDirty);
    }

    [Test]
    public void AuthoritativeFog_UsesLogicPositionAndLeavesExploredStateBehind()
    {
        EntityRegistry.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicCardPlacementAuthority.BeginTimeline();
        try
        {
            var entity = new SimEntityContext
            {
                PositionFixed = new FixVector2((Fix64)0.5f, (Fix64)0.5f),
                Side = SideType.PlayerSide,
            };
            EntityRegistry.Register(entity);

            Fog3MapData map = new Fog3MapData(CreateTerrainInfo(new[] { true, true, true, true, true, true }));
            LogicCardPlacementAuthority.BindWorldForTests(
                map,
                System.Array.Empty<LogicCombatShape>(),
                (Fix64)0.49f,
                (Fix64)0.49f,
                (Fix64)0.49f,
                (Fix64)1000);
            LogicTimeControlService.BeginFrame(1);
            LogicCardPlacementAuthority.ApplyFrame(1);

            Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(0, 0));
            Assert.AreEqual(Fog3CellState.Hidden, map.GetCellState(2, 0));

            entity.PositionFixed = new FixVector2((Fix64)1.5f, (Fix64)0.5f);
            LogicTimeControlService.BeginFrame(2);
            LogicCardPlacementAuthority.ApplyFrame(2);

            Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(0, 0));
            Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(1, 0));
        }
        finally
        {
            LogicCardPlacementAuthority.EndTimeline();
            LogicTimeControlService.EndTimeline();
            EntityRegistry.Clear();
        }
    }

    [Test]
    public void ManagerEntityRevealer_KeepsViewAndLogicEntityIdsInSeparateDomains()
    {
        const int viewEntityId = 404;
        const int logicEntityId = 17;
        GameObject managerObject = new GameObject("Fog3EntityIdDomainManager");
        GameObject targetObject = new GameObject("Fog3EntityIdDomainTarget");
        try
        {
            var controller = new Fog3Controller();
            controller.Initialize(CreateTerrainInfo(new[] { true, true, true, true, true, true }));
            Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
            typeof(Fog3Manager).GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, controller);
            typeof(Fog3Manager).GetField("isInitialized", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, true);

            int revealerId = manager.RegisterRevealer(
                targetObject.transform,
                1f,
                viewEntityId,
                false,
                true,
                logicEntityId);

            Assert.IsTrue(controller.TryGetRevealer(revealerId, out Fog3RevealerData revealer));
            Assert.AreEqual(logicEntityId, revealer.LogicEntityId);

            var entityRevealers = (System.Collections.Generic.Dictionary<int, int>)typeof(Fog3Manager)
                .GetField("entityRevealers", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(manager);
            Assert.AreEqual(revealerId, entityRevealers[viewEntityId]);
            Assert.IsFalse(entityRevealers.ContainsKey(logicEntityId));
        }
        finally
        {
            Object.DestroyImmediate(targetObject);
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void ExplorationCheckpoint_RejectsDifferentTerrainTopology()
    {
        Fog3ExplorationCheckpoint checkpoint = CreateMap(
            new[] { true, true, true, true, true, true }).CaptureExplorationCheckpoint();
        Fog3MapData different = CreateMap(new[] { true, true, false, true, true, true });

        Assert.Throws<System.InvalidOperationException>(() =>
            different.RestoreExplorationCheckpoint(checkpoint));
    }

    [Test]
    public void ExplorationCheckpoint_CompressesSparsePayloadAndReusesUnchangedSnapshot()
    {
        const int width = 1024;
        const int height = 1024;
        var walkable = new bool[width * height];
        System.Array.Fill(walkable, true);
        var map = new Fog3MapData(new Fog3TerrainInfo(
            width,
            height,
            1f,
            Vector3.zero,
            walkable,
            "SparseCheckpointTest"));
        map.MarkExplored(1, 1);
        map.MarkExplored(width - 2, height - 2);

        Fog3ExplorationCheckpoint first = map.CaptureExplorationCheckpoint();
        Fog3ExplorationCheckpoint unchanged = map.CaptureExplorationCheckpoint();

        Assert.AreSame(first, unchanged);
        Assert.AreEqual((width * height + 7) / 8, first.RawPayloadByteCount);
        Assert.Less(first.StoredPayloadByteCount, first.RawPayloadByteCount / 10);

        map.MarkExplored(2, 2);
        Fog3ExplorationCheckpoint changed = map.CaptureExplorationCheckpoint();
        Assert.AreNotSame(first, changed);
        map.ResetExploration();
        map.RestoreExplorationCheckpoint(changed);
        Assert.IsTrue(map.IsExplored(2, 2));
    }

    [Test]
    public void EnemyVisibility_ResolvesBoundViewFromLogicRegistry()
    {
        GameObject managerObject = null;
        GameObject viewObject = null;
        GameObject healthBarObject = null;
        LogicEntityId entityId = default;
        bool viewBound = false;

        EntityRegistry.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        try
        {
            entityId = LogicEntityLifecycleService.RequestSpawn(new LogicEntitySpawnDescriptor(
                FixVector2.Zero,
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.EnemySide,
                "Fog3EnemyVisibilityTest"));
            LogicEntityState logicState = LogicEntityStateStore.GetRequired(entityId);
            EntityRegistry.Register(logicState);

            viewObject = new GameObject("Fog3EnemyVisibilityView");
            Entity entityComponent = viewObject.AddComponent<Entity>();
            MAEntity view = viewObject.AddComponent<MAEntity>();
            MeshRenderer renderer = viewObject.AddComponent<MeshRenderer>();
            FieldInfo entityIdField = typeof(Entity).GetField("m_Id", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo entityField = typeof(EntityLogic).GetField("m_Entity", BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo cachedEntityIdProperty = typeof(EntityBase).GetProperty(nameof(EntityBase.Id));
            PropertyInfo logicEntityIdProperty = typeof(MAEntity).GetProperty(nameof(MAEntity.LogicEntityId));
            Assert.NotNull(entityIdField);
            Assert.NotNull(entityField);
            Assert.NotNull(cachedEntityIdProperty);
            Assert.NotNull(logicEntityIdProperty);
            entityIdField.SetValue(entityComponent, 404);
            entityField.SetValue(view, entityComponent);
            cachedEntityIdProperty.SetValue(view, 404);
            logicEntityIdProperty.SetValue(view, entityId);
            Assert.AreEqual(404, view.Id);

            LogicEntityLifecycleService.BindView(entityId, view.Id, view);
            viewBound = true;

            managerObject = new GameObject("Fog3EnemyVisibilityManager");
            managerObject.SetActive(false);
            Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
            MethodInfo updateEnemyVisibility = typeof(Fog3Manager).GetMethod(
                "UpdateEnemyVisibilityByFog",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(updateEnemyVisibility);

            Fog3MapData map = CreateMap(new[] { true, true, true, true, true, true });
            updateEnemyVisibility.Invoke(manager, new object[] { map });
            Assert.IsFalse(renderer.enabled);

            healthBarObject = new GameObject("LateCreatedEnemyHealthBar");
            Canvas healthCanvas = healthBarObject.AddComponent<Canvas>();
            HealthBarComp healthBar = healthBarObject.AddComponent<HealthBarComp>();
            typeof(HealthBarComp).GetField("_entityId", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(healthBar, view.Id);
            typeof(HealthBarComp).GetField("ownerCanvas", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(healthBar, healthCanvas);
            var activeBars = (System.Collections.Generic.Dictionary<int, HealthBarComp>)typeof(HealthBarComp)
                .GetField("ActiveBars", BindingFlags.Static | BindingFlags.NonPublic)
                .GetValue(null);
            activeBars[view.Id] = healthBar;

            updateEnemyVisibility.Invoke(manager, new object[] { map });
            Assert.IsFalse(healthCanvas.enabled, "A health bar created after the cached fog update must still be hidden.");

            map.AddVisibility(0, 0, 1f);
            updateEnemyVisibility.Invoke(manager, new object[] { map });
            Assert.IsTrue(renderer.enabled);
            Assert.IsTrue(healthCanvas.enabled);

            manager.GetEnemyUnitVisibilityDiagnostics(
                out int aliveLogicCount,
                out int boundViewCount,
                out int trackedViewCount,
                out int renderableViewCount,
                out int fogVisibleViewCount,
                out int rendererMismatchViewCount);
            Assert.AreEqual(1, aliveLogicCount);
            Assert.AreEqual(1, boundViewCount);
            Assert.AreEqual(1, trackedViewCount);
            Assert.AreEqual(1, renderableViewCount);
            Assert.AreEqual(1, fogVisibleViewCount);
            Assert.AreEqual(0, rendererMismatchViewCount);
        }
        finally
        {
            if (managerObject != null)
                Object.DestroyImmediate(managerObject);
            if (viewBound)
                LogicEntityLifecycleService.UnbindView(entityId, 404);
            EntityRegistry.Clear();
            if (healthBarObject != null)
                Object.DestroyImmediate(healthBarObject);
            if (viewObject != null)
                Object.DestroyImmediate(viewObject);
            LogicEntityLifecycleService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    [Test]
    public void EnemyPermanentStealth_RemainsHiddenInVisibleFogCellAndAfterFogReset()
    {
        GameObject managerObject = null;
        GameObject viewObject = null;
        LogicEntityId entityId = default;
        bool viewBound = false;

        EntityRegistry.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        try
        {
            entityId = LogicEntityLifecycleService.RequestSpawn(new LogicEntitySpawnDescriptor(
                FixVector2.Zero,
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.EnemySide,
                "Buil_Trap_Lv1"));
            LogicEntityState logicState = LogicEntityStateStore.GetRequired(entityId);
            EntityRegistry.Register(logicState);

            viewObject = new GameObject("Fog3EnemyPermanentStealthView");
            Entity entityComponent = viewObject.AddComponent<Entity>();
            BuildingEntity view = viewObject.AddComponent<BuildingEntity>();
            MeshRenderer renderer = viewObject.AddComponent<MeshRenderer>();
            FieldInfo entityIdField = typeof(Entity).GetField("m_Id", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo entityField = typeof(EntityLogic).GetField("m_Entity", BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo cachedEntityIdProperty = typeof(EntityBase).GetProperty(nameof(EntityBase.Id));
            PropertyInfo logicEntityIdProperty = typeof(MAEntity).GetProperty(nameof(MAEntity.LogicEntityId));
            Assert.NotNull(entityIdField);
            Assert.NotNull(entityField);
            Assert.NotNull(cachedEntityIdProperty);
            Assert.NotNull(logicEntityIdProperty);
            entityIdField.SetValue(entityComponent, 405);
            entityField.SetValue(view, entityComponent);
            cachedEntityIdProperty.SetValue(view, 405);
            logicEntityIdProperty.SetValue(view, entityId);
            view.OwnerFactionID = EntitySideHelper.EnemyFactionId;
            view.SetPermanentStealthVisibility(true);
            Assert.IsFalse(renderer.enabled);

            LogicEntityLifecycleService.BindView(entityId, view.Id, view);
            viewBound = true;

            managerObject = new GameObject("Fog3EnemyPermanentStealthManager");
            managerObject.SetActive(false);
            Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
            MethodInfo updateEnemyVisibility = typeof(Fog3Manager).GetMethod(
                "UpdateEnemyVisibilityByFog",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo resetEnemyVisibility = typeof(Fog3Manager).GetMethod(
                "ResetEnemyVisibilityStates",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(updateEnemyVisibility);
            Assert.NotNull(resetEnemyVisibility);

            Fog3MapData map = CreateMap(new[] { true, true, true, true, true, true });
            map.AddVisibility(0, 0, 1f);
            updateEnemyVisibility.Invoke(manager, new object[] { map });
            Assert.IsFalse(renderer.enabled);

            resetEnemyVisibility.Invoke(manager, null);
            Assert.IsFalse(renderer.enabled);
        }
        finally
        {
            if (managerObject != null)
                Object.DestroyImmediate(managerObject);
            if (viewBound)
                LogicEntityLifecycleService.UnbindView(entityId, 405);
            EntityRegistry.Clear();
            if (viewObject != null)
                Object.DestroyImmediate(viewObject);
            LogicEntityLifecycleService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    private static Fog3MapData CreateMap(bool[] walkable)
    {
        return new Fog3MapData(CreateTerrainInfo(walkable));
    }

    private static Fog3WorldOverlayView BuildTerrainConformingView(GameObject viewObject, Fog3TerrainInfo terrain)
    {
        var settings = new Fog3ViewSettings
        {
            SurfaceMode = Fog3OverlaySurfaceMode.TerrainConforming,
            OutsideMaskPadding = 0f,
            SurfaceOffset = 0.08f,
            DrawOverSceneGeometry = false,
            OverlayAlwaysOnTopShader = Shader.Find("AAAGame/FOG3/OverlayAlwaysOnTop"),
        };
        Fog3WorldOverlayView view = viewObject.AddComponent<Fog3WorldOverlayView>();
        view.Build(terrain, settings, 5f, Physics.DefaultRaycastLayers, Vector3.zero);
        return view;
    }

    private static Fog3TerrainInfo CreateTerrainInfo(bool[] walkable)
    {
        return new Fog3TerrainInfo(
            3,
            2,
            1f,
            Vector3.zero,
            walkable,
            "StageCheckpointTest");
    }
}
