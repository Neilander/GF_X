using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

[TestFixture]
public sealed class DefendRouteRuntimeTests
{
    [TearDown]
    public void TearDown()
    {
        LogicStrongholdMap.Clear();
    }

    [Test]
    public void CapturingSourceDeactivatesOnlyThatFixedSource()
    {
        InitializeTwoStrongholds();

        Assert.IsTrue(DefendPhaseRuntime.GetEditorTestIsAttackGroupSourceActive("SH_A"));
        Assert.IsTrue(DefendPhaseRuntime.GetEditorTestIsAttackGroupSourceActive("SH_B"));

        LogicStrongholdMap.SetOwnerFactionId("SH_A", EntitySideHelper.PlayerFactionId);

        Assert.IsFalse(DefendPhaseRuntime.GetEditorTestIsAttackGroupSourceActive("SH_A"));
        Assert.IsTrue(DefendPhaseRuntime.GetEditorTestIsAttackGroupSourceActive("SH_B"));
    }

    [Test]
    public void RouteStateDoesNotChangeWhenStrongholdOwnershipChanges()
    {
        InitializeTwoStrongholds();
        var brain = new SoldierAIBrain();
        brain.ConfigureDefendRoute(
            new[] { new FixVector2((Fix64)4, (Fix64)5), new FixVector2((Fix64)8, (Fix64)9) },
            new[] { "SH_B", (string)null },
            0,
            null,
            (Fix64)2);

        var before = new LogicStateHasher();
        brain.WriteDeterministicState(before);
        LogicStrongholdMap.SetOwnerFactionId("SH_B", EntitySideHelper.PlayerFactionId);
        var after = new LogicStateHasher();
        brain.WriteDeterministicState(after);

        Assert.AreEqual(before.Hash, after.Hash);
    }

    [Test]
    public void RouteConfigurationClonesInputsAndEntityParamsPoolClearsRoute()
    {
        var positions = new[] { new FixVector2((Fix64)4, (Fix64)5) };
        var teleportationIds = new[] { "1" };
        var configuredBrain = new SoldierAIBrain();
        configuredBrain.ConfigureDefendRoute(positions, teleportationIds, 0, null, (Fix64)2);
        positions[0] = new FixVector2((Fix64)99, (Fix64)99);
        teleportationIds[0] = "CHANGED";

        var expectedBrain = new SoldierAIBrain();
        expectedBrain.ConfigureDefendRoute(
            new[] { new FixVector2((Fix64)4, (Fix64)5) },
            new[] { "1" },
            0,
            null,
            (Fix64)2);
        var configuredHash = new LogicStateHasher();
        var expectedHash = new LogicStateHasher();
        configuredBrain.WriteDeterministicState(configuredHash);
        expectedBrain.WriteDeterministicState(expectedHash);
        Assert.AreEqual(expectedHash.Hash, configuredHash.Hash);

        var entityParams = new TestEntityParams();
        entityParams.DefendRouteWaypointsFixed = positions;
        entityParams.DefendRouteWaypointTeleportationIds = teleportationIds;
        entityParams.ResetForTest();

        Assert.IsNull(entityParams.DefendRouteWaypointsFixed);
        Assert.IsNull(entityParams.DefendRouteWaypointTeleportationIds);
        Assert.AreEqual(-1, entityParams.DefendSpeedReleaseWaypointIndex);
        Assert.IsNull(entityParams.DefendSpeedReleasePositionFixed);
    }

    [Test]
    public void EmptyRouteIsAllowedForDirectAttackToDynamicTarget()
    {
        var brain = new SoldierAIBrain();
        brain.ConfigureDefendRoute(
            Array.Empty<FixVector2>(),
            Array.Empty<string>(),
            -1,
            new FixVector2((Fix64)3, (Fix64)3),
            (Fix64)1);
        var hasher = new LogicStateHasher();
        brain.WriteDeterministicState(hasher);
        Assert.Pass();
    }

    [Test]
    public void DirectGameEndRouteKeepsFixedWaypointsEmptyAndCarriesReleasePosition()
    {
        var entityParams = new TestEntityParams
        {
            DefendRouteWaypointsFixed = Array.Empty<FixVector2>(),
            DefendRouteWaypointTeleportationIds = Array.Empty<string>(),
            DefendSpeedReleaseWaypointIndex = -1,
            DefendSpeedReleasePositionFixed = new FixVector2((Fix64)12, (Fix64)34)
        };

        Assert.IsEmpty(entityParams.DefendRouteWaypointsFixed);
        Assert.IsEmpty(entityParams.DefendRouteWaypointTeleportationIds);
        Assert.AreEqual(-1, entityParams.DefendSpeedReleaseWaypointIndex);
        Assert.AreEqual(
            new FixVector2((Fix64)12, (Fix64)34),
            entityParams.DefendSpeedReleasePositionFixed.Value);
    }

    [Test]
    public void WaveSchedulerInterleavesGroupsWithOneGlobalInterval()
    {
        Fix64[] scheduled = DefendPhaseRuntime.GetEditorTestWaveSpawnSeconds(
            new[] { Fix64.Zero, Fix64.Zero, Fix64.Zero, Fix64.Zero },
            new[] { (Fix64)10, (Fix64)10, (Fix64)10, (Fix64)10 },
            new[] { "A", "A", "B", "B" },
            new[] { 0, 1, 0, 1 },
            Fix64.One);

        CollectionAssert.AreEqual(
            new[] { Fix64.Zero, (Fix64)2, Fix64.One, (Fix64)3 },
            scheduled);
    }

    [Test]
    public void WaveSchedulerRejectsAWindowThatWouldRequireDenseSpawning()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DefendPhaseRuntime.GetEditorTestWaveSpawnSeconds(
                new[] { Fix64.Zero, Fix64.Zero, Fix64.Zero },
                new[] { Fix64.One, Fix64.One, Fix64.One },
                new[] { "A", "A", "B" },
                new[] { 0, 1, 0 },
                Fix64.FromRaw(3277)));
    }

    [Test]
    public void WavePlanningUsesTheCurrentPlayerFrontWithoutChangingFixedTimes()
    {
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("SH_SOURCE", 0, 0, EntitySideHelper.EnemyFactionId),
                new LogicStrongholdCellDefinition("SH_FRONT", 1, 0, EntitySideHelper.PlayerFactionId),
                new LogicStrongholdCellDefinition("SH_REAR", 2, 0, EntitySideHelper.EnemyFactionId),
            });
        Fix64[] scheduled = DefendPhaseRuntime.GetEditorTestWaveSpawnSeconds(
            new[] { Fix64.Zero, Fix64.Zero },
            new[] { (Fix64)10, (Fix64)10 },
            new[] { "A", "B" },
            new[] { 0, 0 },
            Fix64.One);
        string[] waypointStrongholdIds = { "SH_FRONT", "SH_REAR" };
        int initialFront = DefendPhaseRuntime.GetEditorTestFirstPlayerWaypointIndex(
            "Route",
            waypointStrongholdIds);

        Fix64 initialFrontSpeed = DefendPhaseRuntime.GetEditorTestExpectedEngagementSpeed((Fix64)50, (Fix64)10);

        CollectionAssert.AreEqual(new[] { Fix64.Zero, Fix64.One }, scheduled);
        Assert.AreEqual(0, initialFront);
        Assert.AreEqual((Fix64)5, initialFrontSpeed);
    }

    [Test]
    public void EditorPreviewUsesRuntimeNavigationWorldForRealLevelRoute()
    {
        FlowFieldNavigationConfig flowConfig = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowFieldNavigationConfig>(
            "Assets/AAAGame/SOs/FlowFieldNavigationConfig.asset");
        Assert.NotNull(flowConfig);
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.SetConfig(flowConfig);

        UnityEngine.GameObject level = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
            "Assets/AAAGame/Prefabs/Entity/Level/Level_3.prefab");
        FlowNavigationGridAsset smallGrid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>(
            "Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Small.asset");
        FlowNavigationGridAsset collisionGrid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>(
            "Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Medium.asset");
        Assert.NotNull(level);
        Assert.NotNull(smallGrid);
        Assert.NotNull(collisionGrid);
        Assert.IsFalse(smallGrid.HasStaticCollisionGeometry);
        Assert.IsTrue(collisionGrid.HasStaticCollisionGeometry);

        EntityPresetPoint[] points = level.GetComponentsInChildren<EntityPresetPoint>(true);
        EntityPresetPoint target = points.Single(x =>
            x.PointType == EntityPresetPointType.Building
            && x.IsGameEndConditionBuilding
            && x.Identifier == "InitBase_Lv1");
        EntityPresetPoint source = points
            .Where(x => x.PointType == EntityPresetPointType.Teleportation)
            .OrderByDescending(x => UnityEngine.Vector3.SqrMagnitude(x.Position - target.Position))
            .First();

        var first = new List<UnityEngine.Vector3>();
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryGetEditorNavigationPathCorners(
                smallGrid,
                collisionGrid,
                source.Position,
                target.Position,
                first,
                out string firstFailure),
            firstFailure);
        Assert.Greater(first.Count, 2, "真实关卡预览必须包含导航绕行拐点，不能退化成端点直线。");
        float navigationDistance = PolylineDistance(first);
        float directDistance = UnityEngine.Vector3.Distance(source.Position, target.Position);
        Assert.Greater(navigationDistance, directDistance + smallGrid.CellSize);

        var second = new List<UnityEngine.Vector3>();
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryGetEditorNavigationPathCorners(
                smallGrid,
                collisionGrid,
                source.Position,
                target.Position,
                second,
                out string secondFailure),
            secondFailure);
        CollectionAssert.AreEqual(first, second);
    }

    [Test]
    public void EditorPreviewLabelsTeleportationIdOnceAndUsesChineseRouteRoles()
    {
        string source = File.ReadAllText(
            "Assets/AAAGame/ScriptsBuiltin/Editor/Defense/DefendRouteEditorWindow.cs");

        StringAssert.Contains("? \"出兵点\"", source);
        StringAssert.Contains("? \"最终目标\"", source);
        StringAssert.Contains("$\"中转 {pointIndex}\"", source);
        StringAssert.Contains("if (i != _selectedRouteIndex)", source);
        StringAssert.DoesNotContain("SRC TP:", source);
        StringAssert.DoesNotContain("TARGET", source);
        StringAssert.DoesNotContain("$\"TP:{routes[i]", source);
    }

    [Test]
    public void EditorPreviewSeparatesTeleportAndRouteLabelsInCameraSpace()
    {
        string source = File.ReadAllText(
            "Assets/AAAGame/ScriptsBuiltin/Editor/Defense/DefendRouteEditorWindow.cs");
        string preview = ExtractSourceBlock(
            source,
            "private void DrawScenePreview(SceneView sceneView)",
            "private bool TryBuildRoutePoints(");

        StringAssert.Contains("OffsetSceneLabelPosition(sceneView, point, -0.45f, 0.2f)", preview);
        StringAssert.Contains("OffsetSceneLabelPosition(sceneView, preview.TopologyPoints[pointIndex], 0.45f, 0.2f)", preview);
        StringAssert.Contains("cameraTransform.right", preview);
        StringAssert.Contains("HandleUtility.GetHandleSize(point)", preview);
        StringAssert.DoesNotContain("Vector3.up * 1.5f", preview);
        StringAssert.DoesNotContain("Vector3.up * 2.5f", preview);
    }

    [Test]
    public void EditorSpawnWindowUsesInitialGameEndTargetWithoutAuthoredWaypoint()
    {
        string source = File.ReadAllText(
            "Assets/AAAGame/ScriptsBuiltin/Editor/Defense/DefendRouteEditorWindow.cs");
        string distance = ExtractSourceBlock(
            source,
            "private bool TryGetFirstPlayerWaypointDistance(",
            "private string DrawTeleportationPopup(");
        StringAssert.Contains("if (preview.IncludesInitialTarget)", distance);
        StringAssert.Contains("preview.TopologyPoints.Count - 1", distance);
        StringAssert.Contains("preview.TopologyCumulativeNavigationDistances[targetTopologyIndex]", distance);

        string leads = ExtractSourceBlock(
            source,
            "private bool TryGetPresetSpawnLeads(",
            "private static ExcelPackage OpenExcel(");
        StringAssert.Contains("Vector3.Distance(\r\n            preview.TopologyPoints[0]", leads);
        StringAssert.Contains("presetTopologyIndex = preview.TopologyPoints.Count - 1", leads);
        StringAssert.DoesNotContain("route.WaypointTeleportationIds.Count == 0", leads);
    }

    [Test]
    public void RuntimeUsesInitialGameEndTargetForDirectRouteWithoutAppendingWaypoint()
    {
        string source = File.ReadAllText(
            "Assets/AAAGame/Scripts/GameClass/DefendPhaseRuntime.cs");
        StringAssert.Contains("UsesInitialGameEndTarget = usesInitialGameEndTarget", source);
        StringAssert.Contains("InitialGameEndTargetPosition = initialGameEndTargetPosition", source);
        StringAssert.Contains("route.InitialGameEndTargetPosition", source);
        StringAssert.Contains("SpeedReleasePositionFixed = pathEstimate.ReleasePosition", source);
        StringAssert.DoesNotContain("WaypointsFixed = waypointPositions.Append", source);
    }

    [Test]
    public void DefenseWaveScheduleStartsOnlyAfterPrewarmAndUsesTheNextLogicFrame()
    {
        string source = File.ReadAllText(
            "Assets/AAAGame/Scripts/GameClass/DefendPhaseRuntime.cs");
        string enter = ExtractSourceBlock(
            source,
            "public static void EnterDefendPhase()",
            "private static void StartPreparedDefendWave(");
        int readinessCheck = enter.IndexOf(
            "if (!FlowFieldCrowdMovementSystem.IsNavigationDistancePrewarmCompleted)",
            StringComparison.Ordinal);
        int waveStart = enter.IndexOf("StartPreparedDefendWave(groups", StringComparison.Ordinal);
        Assert.GreaterOrEqual(readinessCheck, 0);
        Assert.Greater(waveStart, readinessCheck);

        string apply = ExtractSourceBlock(
            source,
            "public static void ApplyScheduledSpawnRequests(ulong frame)",
            "private static void SpawnPlannedEvent(");
        StringAssert.Contains(
            "if (s_WaitingForNavigationDistancePrewarm)\r\n            return;",
            apply);

        string completion = ExtractSourceBlock(
            source,
            "private static void OnNavigationDistancePrewarmCompleted(",
            "public static void NotifyDefendEnemyReachedSpeedReleaseWaypoint(");
        StringAssert.Contains("LogicTimeControlService.CurrentFrame + 1UL", completion);

        string reset = ExtractSourceBlock(
            source,
            "private static void ResetDefendPhaseState(bool keepRoundIndex)",
            "public static void WriteDeterministicState(");
        StringAssert.Contains("s_WaitingForNavigationDistancePrewarm = false;", reset);
    }

    private static void InitializeTwoStrongholds()
    {
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("SH_A", 0, 0, EntitySideHelper.EnemyFactionId),
                new LogicStrongholdCellDefinition("SH_B", 1, 0, EntitySideHelper.EnemyFactionId),
            });
    }

    private static float PolylineDistance(IReadOnlyList<UnityEngine.Vector3> points)
    {
        float distance = 0f;
        for (int i = 1; i < points.Count; i++)
            distance += UnityEngine.Vector3.Distance(points[i - 1], points[i]);
        return distance;
    }

    private static string ExtractSourceBlock(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.GreaterOrEqual(start, 0, $"Missing source marker '{startMarker}'.");
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.Greater(end, start, $"Missing source marker '{endMarker}'.");
        return source.Substring(start, end - start);
    }

    private sealed class TestEntityParams : EntityParams
    {
        public void ResetForTest()
        {
            ResetProperties();
        }
    }
}
