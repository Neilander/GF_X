using NUnit.Framework;
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[TestFixture]
public sealed class FlowNavigationGridPrefabBakerTests
{
    private const string TempFolder = "Assets/AAAGame/Tests/Editor/TempFlowNavigationGridPrefabBaker";
    private const string TempPrefabPath = TempFolder + "/Terrain.prefab";
    private const string TempAssetPath = TempFolder + "/FlowGrid.asset";
    private const string TempConfigurationPath = TempFolder + "/TerrainConfiguration.asset";
    private const string LvTestPrefabPath = "Assets/AAAGame/Prefabs/Entity/Level/LvTest.prefab";

    private static readonly string[] LvTestNavigationGridPaths =
    {
        "Assets/AAAGame/Tilemap/LvTest_FlowNavigationGrid_Small.asset",
        "Assets/AAAGame/Tilemap/LvTest_FlowNavigationGrid_Medium.asset",
        "Assets/AAAGame/Tilemap/LvTest_FlowNavigationGrid_Large.asset"
    };

    [SetUp]
    public void SetUp()
    {
        EnsureTempFolder();
        AssetDatabase.DeleteAsset(TempPrefabPath);
        AssetDatabase.DeleteAsset(TempAssetPath);
        AssetDatabase.DeleteAsset(TempConfigurationPath);
    }

    [TearDown]
    public void TearDown()
    {
        AssetDatabase.DeleteAsset(TempPrefabPath);
        AssetDatabase.DeleteAsset(TempAssetPath);
        AssetDatabase.DeleteAsset(TempConfigurationPath);
    }

    [Test]
    public void BakeUsesAgentFootprintAgainstRealObstacleColliders()
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        int obstacleLayer = LayerMask.NameToLayer("LevelObstacle");
        Assert.GreaterOrEqual(groundLayer, 0, "Project must define Ground layer.");
        Assert.GreaterOrEqual(obstacleLayer, 0, "Project must define LevelObstacle layer.");

        CreateTerrainPrefab(groundLayer, obstacleLayer);

        object result = InvokeBakeFromTerrainPrefab(
            TempPrefabPath,
            TempAssetPath,
            agentTypeId: 1001,
            hardClearanceRadius: 0.5f,
            width: 4,
            height: 4,
            cellSize: 1f,
            gridOrigin: Vector3.zero);

        FlowNavigationGridAsset asset = (FlowNavigationGridAsset)result.GetType().GetField("Asset").GetValue(result);
        Assert.IsNotNull(asset);
        Assert.IsFalse(asset.IsCellWalkable(1, 1), "A cell with no full-footprint anchor outside the obstacle must be blocked.");
        Assert.IsTrue(asset.IsCellWalkable(0, 1), "Nearby cell outside the obstacle footprint should remain walkable.");
    }

    [Test]
    public void LvTestPermanentNoCollisionTrapCenterIsWalkableForEveryMovementType()
    {
        GameObject levelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LvTestPrefabPath);
        Assert.IsNotNull(levelPrefab, LvTestPrefabPath);

        EntityPresetPoint trapPoint = null;
        EntityPresetPoint[] points = levelPrefab.GetComponentsInChildren<EntityPresetPoint>(true);
        for (int i = 0; i < points.Length; i++)
        {
            EntityPresetPoint point = points[i];
            if (!string.Equals(point.Identifier, "Buil_Trap_Lv1", StringComparison.Ordinal))
                continue;
            Assert.IsNull(trapPoint, "LvTest must contain exactly one Buil_Trap_Lv1 preset point.");
            trapPoint = point;
        }

        Assert.IsNotNull(trapPoint, "LvTest must contain a Buil_Trap_Lv1 preset point.");
        Assert.IsTrue(BuildingAbilityIds.HasPermanentNoCollisionCapability(trapPoint.Identifier));
        Vector3 trapPosition = trapPoint.Position;
        for (int i = 0; i < LvTestNavigationGridPaths.Length; i++)
        {
            string gridPath = LvTestNavigationGridPaths[i];
            FlowNavigationGridAsset grid = AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>(gridPath);
            Assert.IsNotNull(grid, gridPath);
            Assert.IsTrue(grid.WorldToCell(trapPosition, out int x, out int y),
                $"Trap position {trapPosition} must be inside {gridPath}.");
            Assert.IsTrue(grid.IsCellWalkable(x, y),
                $"Permanent no-collision trap must not block {gridPath} at cell ({x},{y}).");
        }
    }

    [Test]
    public void BakeUsesConfiguredWorldRadiusWithoutOverBlockingNarrowPassages()
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        int obstacleLayer = LayerMask.NameToLayer("LevelObstacle");
        Assert.GreaterOrEqual(groundLayer, 0, "Project must define Ground layer.");
        Assert.GreaterOrEqual(obstacleLayer, 0, "Project must define LevelObstacle layer.");

        CreateNarrowPassagePrefab(groundLayer, obstacleLayer);

        object result = InvokeBakeFromTerrainPrefab(
            TempPrefabPath,
            TempAssetPath,
            agentTypeId: 1002,
            hardClearanceRadius: 0.33f,
            width: 5,
            height: 3,
            cellSize: 1f,
            gridOrigin: Vector3.zero);

        FlowNavigationGridAsset asset = (FlowNavigationGridAsset)result.GetType().GetField("Asset").GetValue(result);
        Assert.IsNotNull(asset);
        Assert.IsTrue(asset.IsCellWalkable(2, 1), "A medium unit radius from GameConfig should fit through this passage.");
    }

    [Test]
    public void BakeWithRuntimeAgentTypeDoesNotRequireRuntimeDependencyPreparation()
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        Assert.GreaterOrEqual(groundLayer, 0, "Project must define Ground layer.");
        CreateRaisedGroundPrefab(groundLayer, 0f);

        FieldInfo preparedField = typeof(FlowFieldCrowdMovementSystem).GetField(
            "s_RuntimeAgentTypeRadiiPrepared",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(preparedField);
        bool wasPrepared = (bool)preparedField.GetValue(null);
        preparedField.SetValue(null, false);
        try
        {
            object result = InvokeBakeFromTerrainPrefab(
                TempPrefabPath,
                TempAssetPath,
                AgentTypeHelper.MediumMovementTypeId,
                hardClearanceRadius: 0.1f,
                width: 2,
                height: 2,
                cellSize: 1f,
                gridOrigin: Vector3.zero);

            FlowNavigationGridAsset asset = (FlowNavigationGridAsset)result.GetType().GetField("Asset").GetValue(result);
            Assert.IsNotNull(asset);
            Assert.IsTrue(asset.HasDerivedNavigationData);
        }
        finally
        {
            preparedField.SetValue(null, wasPrepared);
        }
    }

    [Test]
    public void BakeFindsWalkableAnchorInsideIrregularCellInsteadOfUsingOnlyCenter()
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        int obstacleLayer = LayerMask.NameToLayer("LevelObstacle");
        Assert.GreaterOrEqual(groundLayer, 0, "Project must define Ground layer.");
        Assert.GreaterOrEqual(obstacleLayer, 0, "Project must define LevelObstacle layer.");

        CreateOffsetGroundPrefab(groundLayer);

        object result = InvokeBakeFromTerrainPrefab(
            TempPrefabPath,
            TempAssetPath,
            agentTypeId: 1003,
            hardClearanceRadius: 0.1f,
            width: 1,
            height: 1,
            cellSize: 1f,
            gridOrigin: Vector3.zero);

        FlowNavigationGridAsset asset = (FlowNavigationGridAsset)result.GetType().GetField("Asset").GetValue(result);
        Assert.IsNotNull(asset);
        Assert.IsTrue(asset.IsCellWalkable(0, 0), "A cell must be walkable when it contains a valid full-footprint standing point away from the cell center.");
        Vector3 anchor = asset.GetCellAnchor(0, 0);
        Assert.Greater(anchor.x, 0.6f, "The baked anchor should be the real standing point inside the irregular ground, not the mathematical cell center.");
        Assert.Less(anchor.x, 0.9f);
        Assert.That(anchor.z, Is.InRange(0.35f, 0.65f));
    }

    [Test]
    public void BakeUsesGroundHitHeightForNavigationAnchor()
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        Assert.GreaterOrEqual(groundLayer, 0, "Project must define Ground layer.");

        CreateRaisedGroundPrefab(groundLayer, 2.75f);

        object result = InvokeBakeFromTerrainPrefab(
            TempPrefabPath,
            TempAssetPath,
            agentTypeId: 1005,
            hardClearanceRadius: 0.1f,
            width: 2,
            height: 2,
            cellSize: 1f,
            gridOrigin: new Vector3(0f, 11f, 0f));

        FlowNavigationGridAsset asset = (FlowNavigationGridAsset)result.GetType().GetField("Asset").GetValue(result);
        Assert.IsNotNull(asset);
        Assert.IsTrue(asset.IsCellWalkable(0, 0));
        Assert.AreEqual(2.75f, asset.GetCellAnchor(0, 0).y, 0.0001f);
    }

    [Test]
    public void MovementTypeBakeAppliesLevelTerrainTransformBeforeResolvingGroundHeight()
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        Assert.GreaterOrEqual(groundLayer, 0, "Project must define Ground layer.");

        CreateRaisedGroundPrefab(groundLayer, 3.7f);

        FlowNavigationGridAsset asset = InvokeMovementTypeBakeWithTerrainTransform(
            TempPrefabPath,
            TempAssetPath,
            agentTypeId: 1006,
            hardClearanceRadius: 0.1f,
            cellSize: 1f,
            terrainPosition: new Vector3(0f, -3.7f, 0f));

        Assert.IsNotNull(asset);
        bool foundWalkable = false;
        for (int y = 0; y < asset.Height; y++)
        {
            for (int x = 0; x < asset.Width; x++)
            {
                if (!asset.IsCellWalkable(x, y))
                    continue;

                foundWalkable = true;
                Assert.AreEqual(0f, asset.GetCellAnchor(x, y).y, 0.0001f);
            }
        }

        Assert.IsTrue(foundWalkable);
    }

    [Test]
    public void BakeCutsNeighborTraversalWhenAnchorsAreSeparatedByObstacle()
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        int obstacleLayer = LayerMask.NameToLayer("LevelObstacle");
        Assert.GreaterOrEqual(groundLayer, 0, "Project must define Ground layer.");
        Assert.GreaterOrEqual(obstacleLayer, 0, "Project must define LevelObstacle layer.");

        CreateSplitCellsPrefab(groundLayer, obstacleLayer);

        object result = InvokeBakeFromTerrainPrefab(
            TempPrefabPath,
            TempAssetPath,
            agentTypeId: 1004,
            hardClearanceRadius: 0.1f,
            width: 2,
            height: 1,
            cellSize: 1f,
            gridOrigin: Vector3.zero);

        FlowNavigationGridAsset asset = (FlowNavigationGridAsset)result.GetType().GetField("Asset").GetValue(result);
        Assert.IsNotNull(asset);
        Assert.IsTrue(asset.IsCellWalkable(0, 0));
        Assert.IsTrue(asset.IsCellWalkable(1, 0));
        byte[] mask = asset.CreateNeighborTraversalMaskCopy();
        Assert.IsNotNull(mask);
        Assert.AreEqual(0, mask[0] & (1 << 4), "Right traversal must be cut by the wall between anchors.");
        Assert.AreEqual(0, mask[1] & (1 << 3), "Left traversal must be cut by the wall between anchors.");
    }

    [Test]
    public void BakeTreatsSlopeSidesAsCliffsUsingAuthoredTerrainTopology()
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        Assert.GreaterOrEqual(groundLayer, 0, "Project must define Ground layer.");
        CreateAuthoredTerrainPrefab(groundLayer, includeSlope: true);

        object result = InvokeBakeFromTerrainPrefab(
            TempPrefabPath,
            TempAssetPath,
            agentTypeId: 1007,
            hardClearanceRadius: 0.1f,
            width: 3,
            height: 3,
            cellSize: 1f,
            gridOrigin: new Vector3(-0.5f, 0f, -0.5f));

        FlowNavigationGridAsset asset = (FlowNavigationGridAsset)result.GetType().GetField("Asset").GetValue(result);
        Assert.IsNotNull(asset);
        Assert.IsTrue(asset.IsCellWalkable(1, 1));
        byte[] mask = asset.CreateNeighborTraversalMaskCopy();
        int slopeIndex = 1 + 1 * asset.Width;
        Assert.AreEqual(0, mask[slopeIndex] & (1 << 3), "The west side of a north-facing slope must be a cliff edge.");
        Assert.AreEqual(0, mask[slopeIndex] & (1 << 4), "The east side of a north-facing slope must be a cliff edge.");
        Assert.AreNotEqual(0, mask[slopeIndex] & (1 << 1), "The slope must remain connected to its low platform.");
        Assert.AreNotEqual(0, mask[slopeIndex] & (1 << 6), "The slope must remain connected to its high platform.");
    }

    [Test]
    public void BakeTreatsDifferentHeightPlatformsAsCliffsWithoutSlope()
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        Assert.GreaterOrEqual(groundLayer, 0, "Project must define Ground layer.");
        CreateAuthoredTerrainPrefab(groundLayer, includeSlope: false);

        object result = InvokeBakeFromTerrainPrefab(
            TempPrefabPath,
            TempAssetPath,
            agentTypeId: 1008,
            hardClearanceRadius: 0.1f,
            width: 3,
            height: 3,
            cellSize: 1f,
            gridOrigin: new Vector3(-0.5f, 0f, -0.5f));

        FlowNavigationGridAsset asset = (FlowNavigationGridAsset)result.GetType().GetField("Asset").GetValue(result);
        Assert.IsNotNull(asset);
        byte[] mask = asset.CreateNeighborTraversalMaskCopy();
        int lowPlatformIndex = 1 + 1 * asset.Width;
        Assert.AreEqual(0, mask[lowPlatformIndex] & (1 << 6), "Adjacent H0 and H1 platforms must have a cliff edge when no slope connects them.");
        Assert.AreNotEqual(0, mask[lowPlatformIndex] & (1 << 3), "The same-height H0 platform edge must remain connected.");
    }

    [Test]
    public void OverwriteBakesFixedAuthorityPayloadForGridMetadataAndAnchors()
    {
        FlowNavigationGridAsset asset = ScriptableObject.CreateInstance<FlowNavigationGridAsset>();
        try
        {
            const int width = 2;
            const int height = 1;
            const float cellSize = 0.09f;
            var origin = new Vector3(12.78f, 0f, 7.11f);
            var anchors = new[]
            {
                new Vector3(12.80f, 0f, 7.13f),
                new Vector3(12.915f, 0f, 7.155f)
            };

            asset.Overwrite(
                -1372625422,
                width,
                height,
                cellSize,
                origin,
                new[] { true, true },
                new byte[] { 1, 1 },
                anchors,
                new byte[] { 16, 8 });

            PropertyInfo hasPayloadProperty = typeof(FlowNavigationGridAsset).GetProperty("HasFixedAuthorityPayload");
            MethodInfo getMetadataMethod = typeof(FlowNavigationGridAsset).GetMethod("GetFixedAuthorityMetadata");
            MethodInfo getAnchorsMethod = typeof(FlowNavigationGridAsset).GetMethod("GetCellAnchorsFixedRuntimeReadOnlyReference");

            Assert.IsNotNull(hasPayloadProperty, "FlowNavigationGridAsset must expose a baked fixed authority payload contract.");
            Assert.IsNotNull(getMetadataMethod, "FlowNavigationGridAsset must expose baked Q32 grid metadata.");
            Assert.IsNotNull(getAnchorsMethod, "FlowNavigationGridAsset must expose baked fixed anchors.");
            Assert.IsTrue((bool)hasPayloadProperty.GetValue(asset));

            object metadata = getMetadataMethod.Invoke(asset, null);
            Type metadataType = metadata.GetType();
            long cellSizeRaw = (long)metadataType.GetField("CellSizeGridRaw").GetValue(metadata);
            long originXRaw = (long)metadataType.GetField("OriginXGridRaw").GetValue(metadata);
            long originZRaw = (long)metadataType.GetField("OriginZGridRaw").GetValue(metadata);
            Array fixedAnchors = (Array)getAnchorsMethod.Invoke(asset, null);

            Assert.AreEqual(386547072L, cellSizeRaw);
            Assert.AreEqual(54889680896L, originXRaw);
            Assert.AreEqual(30537218048L, originZRaw);
            Assert.AreEqual(width * height, fixedAnchors.Length);
            Assert.AreEqual(((Fix64)anchors[0].x).RawValue, ReadFixVectorRaw(fixedAnchors.GetValue(0), "x"));
            Assert.AreEqual(((Fix64)anchors[0].z).RawValue, ReadFixVectorRaw(fixedAnchors.GetValue(0), "y"));
            Assert.AreEqual(((Fix64)anchors[1].x).RawValue, ReadFixVectorRaw(fixedAnchors.GetValue(1), "x"));
            Assert.AreEqual(((Fix64)anchors[1].z).RawValue, ReadFixVectorRaw(fixedAnchors.GetValue(1), "y"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(asset);
        }
    }

    [Test]
    public void ProjectFlowNavigationGridAssetsCarryValidV3FixedAuthorityPayloads()
    {
        string[] guids = AssetDatabase.FindAssets("t:FlowNavigationGridAsset", new[] { "Assets/AAAGame/Tilemap" });
        Array.Sort(guids, StringComparer.Ordinal);
        Assert.AreEqual(12, guids.Length);

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            FlowNavigationGridAsset asset = AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>(path);
            Assert.IsNotNull(asset, path);
            Assert.IsTrue(asset.HasFixedAuthorityPayload, path);

            FlowNavigationGridAsset.FixedAuthorityMetadata metadata = asset.GetFixedAuthorityMetadata();
            FlowNavigationGridAsset.DerivedNavigationData derivedData = asset.GetDerivedNavigationDataRuntimeReadOnlyReference();
            Assert.IsNotNull(derivedData, path);
            Assert.IsTrue(derivedData.IsValid, path);
            Assert.AreEqual(FlowNavigationGridAsset.DerivedNavigationData.CurrentVersion, derivedData.Version, path);
            Assert.AreEqual(metadata.CellSizeGridRaw, derivedData.CellSizeGridRaw, path);
            Assert.AreEqual(metadata.OriginXGridRaw, derivedData.OriginXGridRaw, path);
            Assert.AreEqual(metadata.OriginZGridRaw, derivedData.OriginZGridRaw, path);
            Assert.AreEqual(asset.CellCount, asset.GetCellAnchorsFixedRuntimeReadOnlyReference().Length, path);
            if (path.EndsWith("_Medium.asset", StringComparison.Ordinal))
            {
                Assert.IsTrue(asset.HasStaticCollisionGeometry, $"Primary navigation asset must own the raw Ground collision geometry: {path}");
                Assert.Greater(asset.GetStaticCollisionVerticesRuntimeReadOnlyReference().Length, 2, path);
                Assert.Greater(asset.GetStaticCollisionPathStartsRuntimeReadOnlyReference().Length, 1, path);
            }
            else
            {
                Assert.IsFalse(asset.HasStaticCollisionGeometry, $"Derived movement-type asset must not duplicate raw Ground collision geometry: {path}");
            }
        }
    }

    private static long ReadFixVectorRaw(object value, string fieldName)
    {
        FieldInfo field = value.GetType().GetField(fieldName);
        Assert.IsNotNull(field);
        object fix64 = field.GetValue(value);
        PropertyInfo rawValue = fix64.GetType().GetProperty("RawValue");
        Assert.IsNotNull(rawValue);
        return (long)rawValue.GetValue(fix64);
    }

    private static object InvokeBakeFromTerrainPrefab(
        string terrainPrefabPath,
        string assetPath,
        int agentTypeId,
        float hardClearanceRadius,
        int width,
        int height,
        float cellSize,
        Vector3 gridOrigin)
    {
        Type bakerType = Type.GetType("AAAGame.Tools.Editor.FlowNavigationGridPrefabBaker, AAAGame.Tools.Editor");
        Assert.IsNotNull(bakerType, "AAAGame.Tools.Editor.FlowNavigationGridPrefabBaker must be available in the editor.");

        MethodInfo method = bakerType.GetMethod(
            "BakeFromTerrainPrefab",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(string), typeof(string), typeof(int), typeof(float), typeof(int), typeof(int), typeof(float), typeof(Vector3) },
            null);
        Assert.IsNotNull(method, "Expected BakeFromTerrainPrefab overload was not found.");

        return method.Invoke(null, new object[] { terrainPrefabPath, assetPath, agentTypeId, hardClearanceRadius, width, height, cellSize, gridOrigin });
    }

    private static FlowNavigationGridAsset InvokeMovementTypeBakeWithTerrainTransform(
        string terrainPrefabPath,
        string assetPath,
        int agentTypeId,
        float hardClearanceRadius,
        float cellSize,
        Vector3 terrainPosition)
    {
        Type bakerType = Type.GetType("AAAGame.Tools.Editor.FlowNavigationGridPrefabBaker, AAAGame.Tools.Editor");
        Assert.IsNotNull(bakerType, "AAAGame.Tools.Editor.FlowNavigationGridPrefabBaker must be available in the editor.");

        Type requestType = bakerType.GetNestedType("MovementTypeBakeRequest", BindingFlags.Public);
        Type obstacleType = bakerType.GetNestedType("StaticObstacleBakeInstance", BindingFlags.Public);
        Type transformType = bakerType.GetNestedType("TerrainBakeTransform", BindingFlags.Public);
        Assert.IsNotNull(requestType);
        Assert.IsNotNull(obstacleType);
        Assert.IsNotNull(transformType);

        Array requests = Array.CreateInstance(requestType, 1);
        requests.SetValue(Activator.CreateInstance(requestType, agentTypeId, assetPath, hardClearanceRadius), 0);
        Array obstacles = Array.CreateInstance(obstacleType, 0);
        object terrainTransform = Activator.CreateInstance(
            transformType,
            terrainPosition,
            Quaternion.identity,
            Vector3.one);
        Type requestListType = typeof(System.Collections.Generic.IReadOnlyList<>).MakeGenericType(requestType);
        Type obstacleListType = typeof(System.Collections.Generic.IReadOnlyList<>).MakeGenericType(obstacleType);
        MethodInfo method = bakerType.GetMethod(
            "BakeMovementTypesFromTerrainPrefab",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(string), requestListType, typeof(float), obstacleListType, transformType },
            null);
        Assert.IsNotNull(method, "Expected transformed BakeMovementTypesFromTerrainPrefab overload was not found.");

        Array results = (Array)method.Invoke(null, new[] { terrainPrefabPath, requests, (object)cellSize, obstacles, terrainTransform });
        Assert.AreEqual(1, results.Length);
        object result = results.GetValue(0);
        return (FlowNavigationGridAsset)result.GetType().GetField("Asset").GetValue(result);
    }

    private static void CreateTerrainPrefab(int groundLayer, int obstacleLayer)
    {
        GameObject root = new GameObject("TerrainRoot");
        try
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.layer = groundLayer;
            ground.transform.SetParent(root.transform, false);
            ground.transform.position = new Vector3(2f, -0.05f, 2f);
            ground.transform.localScale = new Vector3(4f, 0.1f, 4f);
            ReplacePrimitiveGroundCollider(ground);

            GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.name = "Obstacle";
            obstacle.layer = obstacleLayer;
            obstacle.transform.SetParent(root.transform, false);
            obstacle.transform.position = new Vector3(1.5f, 0.5f, 1.5f);
            obstacle.transform.localScale = new Vector3(0.8f, 1f, 0.8f);

            PrefabUtility.SaveAsPrefabAsset(root, TempPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void CreateNarrowPassagePrefab(int groundLayer, int obstacleLayer)
    {
        GameObject root = new GameObject("NarrowPassageRoot");
        try
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.layer = groundLayer;
            ground.transform.SetParent(root.transform, false);
            ground.transform.position = new Vector3(2.5f, -0.05f, 1.5f);
            ground.transform.localScale = new Vector3(5f, 0.1f, 3f);
            ReplacePrimitiveGroundCollider(ground);

            GameObject lowerWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lowerWall.name = "LowerWall";
            lowerWall.layer = obstacleLayer;
            lowerWall.transform.SetParent(root.transform, false);
            lowerWall.transform.position = new Vector3(2.5f, 0.5f, 0.62f);
            lowerWall.transform.localScale = new Vector3(5f, 1f, 0.24f);

            GameObject upperWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            upperWall.name = "UpperWall";
            upperWall.layer = obstacleLayer;
            upperWall.transform.SetParent(root.transform, false);
            upperWall.transform.position = new Vector3(2.5f, 0.5f, 2.38f);
            upperWall.transform.localScale = new Vector3(5f, 1f, 0.24f);

            PrefabUtility.SaveAsPrefabAsset(root, TempPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void CreateOffsetGroundPrefab(int groundLayer)
    {
        GameObject root = new GameObject("OffsetGroundRoot");
        try
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "OffsetGround";
            ground.layer = groundLayer;
            ground.transform.SetParent(root.transform, false);
            ground.transform.position = new Vector3(0.8f, -0.05f, 0.5f);
            ground.transform.localScale = new Vector3(0.36f, 0.1f, 0.3f);
            ReplacePrimitiveGroundCollider(ground);

            PrefabUtility.SaveAsPrefabAsset(root, TempPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void CreateSplitCellsPrefab(int groundLayer, int obstacleLayer)
    {
        GameObject root = new GameObject("SplitCellsRoot");
        try
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.layer = groundLayer;
            ground.transform.SetParent(root.transform, false);
            ground.transform.position = new Vector3(1f, -0.05f, 0.5f);
            ground.transform.localScale = new Vector3(2f, 0.1f, 1f);
            ReplacePrimitiveGroundCollider(ground);

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Divider";
            wall.layer = obstacleLayer;
            wall.transform.SetParent(root.transform, false);
            wall.transform.position = new Vector3(1f, 0.5f, 0.5f);
            wall.transform.localScale = new Vector3(0.1f, 1f, 1f);

            PrefabUtility.SaveAsPrefabAsset(root, TempPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void CreateRaisedGroundPrefab(int groundLayer, float surfaceHeight)
    {
        GameObject root = new GameObject("RaisedGroundRoot");
        try
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "RaisedGround";
            ground.layer = groundLayer;
            ground.transform.SetParent(root.transform, false);
            ground.transform.position = new Vector3(1f, surfaceHeight - 0.05f, 1f);
            ground.transform.localScale = new Vector3(2f, 0.1f, 2f);
            ReplacePrimitiveGroundCollider(ground);

            PrefabUtility.SaveAsPrefabAsset(root, TempPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void CreateAuthoredTerrainPrefab(int groundLayer, bool includeSlope)
    {
        var configuration = ScriptableObject.CreateInstance<GiantGrey.TileWorldCreator.Configuration>();
        var h0 = ScriptableObject.CreateInstance<GiantGrey.TileWorldCreator.BlueprintLayer>();
        var h1 = ScriptableObject.CreateInstance<GiantGrey.TileWorldCreator.BlueprintLayer>();
        var slope = ScriptableObject.CreateInstance<GiantGrey.TileWorldCreator.BlueprintLayer>();
        GameObject root = new GameObject("AuthoredSlopeTerrainRoot");
        try
        {
            configuration.width = 3;
            configuration.height = 3;
            configuration.cellSize = 1f;
            var folder = new GiantGrey.TileWorldCreator.BlueprintLayerFolder("Root");
            configuration.blueprintLayerFolders.Add(folder);

            h0.layerName = "Plane_H0";
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                    h0.allPositions.Add(new Vector2(x, y));
            }
            h1.layerName = "Plane_H1";
            h1.allPositions.Add(new Vector2(1, 2));
            slope.layerName = "Slope";
            if (includeSlope)
                slope.allPositions.Add(new Vector2(1, 1));
            folder.blueprintLayers.Add(h0);
            folder.blueprintLayers.Add(h1);
            folder.blueprintLayers.Add(slope);

            AssetDatabase.CreateAsset(configuration, TempConfigurationPath);
            AssetDatabase.AddObjectToAsset(h0, configuration);
            AssetDatabase.AddObjectToAsset(h1, configuration);
            AssetDatabase.AddObjectToAsset(slope, configuration);
            AssetDatabase.SaveAssets();

            GiantGrey.TileWorldCreator.TileWorldCreatorManager manager =
                root.AddComponent<GiantGrey.TileWorldCreator.TileWorldCreatorManager>();
            manager.configuration = configuration;

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.layer = groundLayer;
            ground.transform.SetParent(root.transform, false);
            ground.transform.position = new Vector3(1f, -0.05f, 1f);
            ground.transform.localScale = new Vector3(3f, 0.1f, 3f);
            ReplacePrimitiveGroundCollider(ground);

            PrefabUtility.SaveAsPrefabAsset(root, TempPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            if (!AssetDatabase.Contains(h0))
                UnityEngine.Object.DestroyImmediate(h0);
            if (!AssetDatabase.Contains(h1))
                UnityEngine.Object.DestroyImmediate(h1);
            if (!AssetDatabase.Contains(slope))
                UnityEngine.Object.DestroyImmediate(slope);
            if (!AssetDatabase.Contains(configuration))
                UnityEngine.Object.DestroyImmediate(configuration);
        }
    }

    private static void ReplacePrimitiveGroundCollider(GameObject ground)
    {
        MeshFilter meshFilter = ground.GetComponent<MeshFilter>();
        Assert.IsNotNull(meshFilter);
        Assert.IsNotNull(meshFilter.sharedMesh);
        Collider primitiveCollider = ground.GetComponent<Collider>();
        Assert.IsNotNull(primitiveCollider);
        UnityEngine.Object.DestroyImmediate(primitiveCollider);
        MeshCollider meshCollider = ground.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = meshFilter.sharedMesh;
    }

    private static void EnsureTempFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/AAAGame/Tests/Editor"))
            throw new System.InvalidOperationException("Missing editor test folder.");
        if (!AssetDatabase.IsValidFolder(TempFolder))
            AssetDatabase.CreateFolder("Assets/AAAGame/Tests/Editor", "TempFlowNavigationGridPrefabBaker");
    }
}
