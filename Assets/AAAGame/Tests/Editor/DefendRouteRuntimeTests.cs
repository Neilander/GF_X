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
    public void DefenseEditorDraftSurvivesDomainReloadSerializationAndOnEnable()
    {
        Type windowType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("DefendRouteEditorWindow", false))
            .FirstOrDefault(type => type != null);
        Assert.NotNull(windowType);
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;

        UnityEditor.EditorWindow original = null;
        UnityEditor.EditorWindow restored = null;
        try
        {
            original = (UnityEditor.EditorWindow)UnityEngine.ScriptableObject.CreateInstance(windowType);
            System.Collections.IList originalRoutes =
                (System.Collections.IList)windowType.GetField("_routes", flags).GetValue(original);
            System.Collections.IList originalGroups =
                (System.Collections.IList)windowType.GetField("_groups", flags).GetValue(original);
            originalRoutes.Clear();
            originalGroups.Clear();

            Type routeType = windowType.GetNestedType(
                "RouteRecord",
                System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(routeType);
            object route = Activator.CreateInstance(routeType, true);
            routeType.GetField("Identifier", flags).SetValue(route, "1_0_Unsaved");
            routeType.GetField("LevelIdentifier", flags).SetValue(route, "Lv_1");
            routeType.GetField("SourceTeleportationId", flags).SetValue(route, "1");
            routeType.GetField("Suffix", flags).SetValue(route, "Unsaved");
            var waypoints = (List<string>)routeType.GetField("WaypointTeleportationIds", flags).GetValue(route);
            waypoints.Add("0");
            originalRoutes.Add(route);
            windowType.GetField("_draftInitialized", flags).SetValue(original, true);
            windowType.GetField("_derivedIdentifierVersion", flags).SetValue(original, 1);
            windowType.GetField("_dirty", flags).SetValue(original, true);

            string serialized = UnityEditor.EditorJsonUtility.ToJson(original);
            StringAssert.Contains("1_0_Unsaved", serialized);

            restored = (UnityEditor.EditorWindow)UnityEngine.ScriptableObject.CreateInstance(windowType);
            UnityEditor.EditorJsonUtility.FromJsonOverwrite(serialized, restored);
            windowType.GetMethod("OnEnable", flags).Invoke(restored, null);

            System.Collections.IList restoredRoutes =
                (System.Collections.IList)windowType.GetField("_routes", flags).GetValue(restored);
            Assert.AreEqual(1, restoredRoutes.Count);
            Assert.AreEqual(
                "1_0_Unsaved",
                routeType.GetField("Identifier", flags).GetValue(restoredRoutes[0]));
            Assert.IsTrue((bool)windowType.GetField("_dirty", flags).GetValue(restored));
        }
        finally
        {
            if (original != null)
                UnityEngine.Object.DestroyImmediate(original);
            if (restored != null)
                UnityEngine.Object.DestroyImmediate(restored);
        }
    }

    [Test]
    public void DefenseEditorInitializesOneEmptyRoutePerNonPlayerTeleportation()
    {
        Type windowType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("DefendRouteEditorWindow", false))
            .FirstOrDefault(type => type != null);
        Assert.NotNull(windowType);
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;

        UnityEditor.EditorWindow window = null;
        try
        {
            window = (UnityEditor.EditorWindow)UnityEngine.ScriptableObject.CreateInstance(windowType);
            windowType.GetMethod("OnEnable", flags).Invoke(window, null);

            System.Collections.IList levels =
                (System.Collections.IList)windowType.GetField("_levels", flags).GetValue(window);
            int levelIndex = Enumerable.Range(0, levels.Count).Single(index =>
                string.Equals(
                    (string)levels[index].GetType().GetField("Identifier", flags).GetValue(levels[index]),
                    "Lv_2",
                    StringComparison.Ordinal));
            windowType.GetField("_levelIndex", flags).SetValue(window, levelIndex);

            System.Collections.IList routes =
                (System.Collections.IList)windowType.GetField("_routes", flags).GetValue(window);
            for (int i = routes.Count - 1; i >= 0; i--)
            {
                object route = routes[i];
                if (string.Equals(
                        (string)route.GetType().GetField("LevelIdentifier", flags).GetValue(route),
                        "Lv_2",
                        StringComparison.Ordinal))
                {
                    routes.RemoveAt(i);
                }
            }
            var initializedLevels = (List<string>)windowType
                .GetField("_defaultRoutesInitializedLevels", flags)
                .GetValue(window);
            initializedLevels.Remove("Lv_2");

            windowType.GetMethod("LoadLevelGeometry", flags).Invoke(window, null);

            var expectedSourceIds = new List<string>();
            var strongholds = (System.Collections.IDictionary)windowType
                .GetField("_strongholds", flags)
                .GetValue(window);
            foreach (System.Collections.DictionaryEntry entry in strongholds)
            {
                object stronghold = entry.Value;
                Type strongholdType = stronghold.GetType();
                int factionId = (int)strongholdType.GetField("FactionId", flags).GetValue(stronghold);
                if (factionId == EntitySideHelper.PlayerFactionId)
                    continue;
                object teleportPosition = strongholdType.GetField("TeleportPosition", flags).GetValue(stronghold);
                Assert.NotNull(
                    teleportPosition,
                    $"非玩家据点 '{entry.Key}' 必须有传送点才能创建默认路线。");
                int teleportationId = (int)strongholdType.GetField("TeleportationId", flags).GetValue(stronghold);
                expectedSourceIds.Add(teleportationId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            expectedSourceIds.Sort(StringComparer.Ordinal);

            Type routeType = windowType.GetNestedType(
                "RouteRecord",
                System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(routeType);
            List<object> currentRoutes = routes.Cast<object>()
                .Where(route => string.Equals(
                    (string)routeType.GetField("LevelIdentifier", flags).GetValue(route),
                    "Lv_2",
                    StringComparison.Ordinal))
                .ToList();
            Assert.AreEqual(expectedSourceIds.Count, currentRoutes.Count);
            CollectionAssert.AreEqual(
                expectedSourceIds,
                currentRoutes
                    .Select(route => (string)routeType.GetField("SourceTeleportationId", flags).GetValue(route))
                    .OrderBy(identifier => identifier, StringComparer.Ordinal)
                    .ToList());
            foreach (object route in currentRoutes)
            {
                string sourceId = (string)routeType.GetField("SourceTeleportationId", flags).GetValue(route);
                Assert.AreEqual(
                    sourceId,
                    routeType.GetField("Identifier", flags).GetValue(route));
                Assert.IsEmpty(
                    (List<string>)routeType.GetField("WaypointTeleportationIds", flags).GetValue(route));
            }

            int initializedRouteCount = currentRoutes.Count;
            windowType.GetMethod("LoadLevelGeometry", flags).Invoke(window, null);
            Assert.AreEqual(
                initializedRouteCount,
                routes.Cast<object>().Count(route => string.Equals(
                    (string)routeType.GetField("LevelIdentifier", flags).GetValue(route),
                    "Lv_2",
                    StringComparison.Ordinal)));

            windowType.GetMethod("AddRoute", flags).Invoke(window, null);
            object manuallyAddedRoute = routes.Cast<object>().Last(route => string.Equals(
                (string)routeType.GetField("LevelIdentifier", flags).GetValue(route),
                "Lv_2",
                StringComparison.Ordinal));
            string manualIdentifier = (string)routeType.GetField("Identifier", flags).GetValue(manuallyAddedRoute);
            string manualSourceIdentifier = (string)routeType
                .GetField("SourceTeleportationId", flags)
                .GetValue(manuallyAddedRoute);
            StringAssert.StartsWith(manualSourceIdentifier, manualIdentifier);
            StringAssert.DoesNotContain("Lv_2", manualIdentifier);
            StringAssert.DoesNotContain("_Route_", manualIdentifier);

            object sameIdentifierInAnotherLevel = Activator.CreateInstance(routeType, true);
            routeType.GetField("Identifier", flags).SetValue(sameIdentifierInAnotherLevel, manualIdentifier);
            routeType.GetField("LevelIdentifier", flags).SetValue(sameIdentifierInAnotherLevel, "OtherLevel");
            routeType.GetField("SourceTeleportationId", flags).SetValue(sameIdentifierInAnotherLevel, "0");
            routes.Add(sameIdentifierInAnotherLevel);
            windowType.GetMethod("ValidateAll", flags).Invoke(window, null);
            var validationMessages = (List<string>)windowType
                .GetField("_validationMessages", flags)
                .GetValue(window);
            Assert.IsFalse(validationMessages.Any(message =>
                message.Contains("路线 ID 重复", StringComparison.Ordinal)
                && message.Contains($"'{manualIdentifier}'", StringComparison.Ordinal)));

            for (int i = routes.Count - 1; i >= 0; i--)
            {
                if (string.Equals(
                        (string)routeType.GetField("LevelIdentifier", flags).GetValue(routes[i]),
                        "Lv_2",
                        StringComparison.Ordinal))
                {
                    routes.RemoveAt(i);
                }
            }
            windowType.GetMethod("LoadLevelGeometry", flags).Invoke(window, null);
            Assert.IsFalse(routes.Cast<object>().Any(route => string.Equals(
                (string)routeType.GetField("LevelIdentifier", flags).GetValue(route),
                "Lv_2",
                StringComparison.Ordinal)));
        }
        finally
        {
            if (window != null)
                UnityEngine.Object.DestroyImmediate(window);
        }
    }

    [Test]
    public void DefenseEditorExposesOnlySuffixesAndDerivesEveryIdentifier()
    {
        Type windowType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("DefendRouteEditorWindow", false))
            .FirstOrDefault(type => type != null);
        Assert.NotNull(windowType);
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;

        UnityEditor.EditorWindow window = null;
        try
        {
            window = (UnityEditor.EditorWindow)UnityEngine.ScriptableObject.CreateInstance(windowType);
            windowType.GetMethod("OnEnable", flags).Invoke(window, null);
            Type routeType = windowType.GetNestedType("RouteRecord", System.Reflection.BindingFlags.NonPublic);
            Type groupType = windowType.GetNestedType("GroupRecord", System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(routeType);
            Assert.NotNull(groupType);

            System.Collections.IList routes =
                (System.Collections.IList)windowType.GetField("_routes", flags).GetValue(window);
            System.Collections.IList groups =
                (System.Collections.IList)windowType.GetField("_groups", flags).GetValue(window);
            foreach (object route in routes)
            {
                string sourceId = (string)routeType.GetField("SourceTeleportationId", flags).GetValue(route);
                var waypoints = (List<string>)routeType.GetField("WaypointTeleportationIds", flags).GetValue(route);
                string suffix = (string)routeType.GetField("Suffix", flags).GetValue(route);
                string expected = string.Join("_", new[] { sourceId }.Concat(waypoints));
                if (!string.IsNullOrEmpty(suffix))
                    expected += $"_{suffix}";
                Assert.AreEqual(expected, routeType.GetField("Identifier", flags).GetValue(route));
            }

            foreach (object group in groups)
            {
                int defendRound = (int)groupType.GetField("DefendRound", flags).GetValue(group);
                string routeIdentifier = (string)groupType.GetField("RouteIdentifier", flags).GetValue(group);
                string suffix = (string)groupType.GetField("Suffix", flags).GetValue(group);
                string expected = $"D{defendRound}_{routeIdentifier}";
                if (!string.IsNullOrEmpty(suffix))
                    expected += $"_{suffix}";
                Assert.AreEqual(expected, groupType.GetField("Identifier", flags).GetValue(group));
            }

            Assert.IsFalse(routes.Cast<object>()
                .GroupBy(route => new
                {
                    Level = (string)routeType.GetField("LevelIdentifier", flags).GetValue(route),
                    Identifier = (string)routeType.GetField("Identifier", flags).GetValue(route)
                })
                .Any(group => group.Count() > 1));
            Assert.IsFalse(groups.Cast<object>()
                .GroupBy(group => new
                {
                    Level = (string)groupType.GetField("LevelIdentifier", flags).GetValue(group),
                    Identifier = (string)groupType.GetField("Identifier", flags).GetValue(group)
                })
                .Any(group => group.Count() > 1));

            string source = File.ReadAllText(
                "Assets/AAAGame/ScriptsBuiltin/Editor/Defense/DefendRouteEditorWindow.cs");
            string routeUi = ExtractSourceBlock(source, "private void DrawRoutes()", "private void DrawGroups()");
            string groupUi = ExtractSourceBlock(source, "private void DrawGroups()", "private void DrawRecordSelector(");
            StringAssert.Contains("TextField(\"路线后缀（可空）\"", routeUi);
            StringAssert.Contains("TextField(\"出兵组后缀（可空）\"", groupUi);
            StringAssert.DoesNotContain("route.Identifier = EditorGUILayout.TextField", routeUi);
            StringAssert.DoesNotContain("group.Identifier = EditorGUILayout.TextField", groupUi);
        }
        finally
        {
            if (window != null)
                UnityEngine.Object.DestroyImmediate(window);
        }
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
