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

    [SetUp]
    public void SetUp()
    {
        EnsureTempFolder();
        AssetDatabase.DeleteAsset(TempPrefabPath);
        AssetDatabase.DeleteAsset(TempAssetPath);
    }

    [TearDown]
    public void TearDown()
    {
        AssetDatabase.DeleteAsset(TempPrefabPath);
        AssetDatabase.DeleteAsset(TempAssetPath);
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

            PrefabUtility.SaveAsPrefabAsset(root, TempPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void EnsureTempFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/AAAGame/Tests/Editor"))
            throw new System.InvalidOperationException("Missing editor test folder.");
        if (!AssetDatabase.IsValidFolder(TempFolder))
            AssetDatabase.CreateFolder("Assets/AAAGame/Tests/Editor", "TempFlowNavigationGridPrefabBaker");
    }
}
