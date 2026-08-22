using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

[TestFixture]
public sealed class DefendRouteRuntimeTests
{
    [TestCase(GamePhase.Defend, 1, 1)]
    [TestCase(GamePhase.BuildBeforeDefend, 2, 3)]
    [TestCase(GamePhase.BuildBeforeInvade, 1, 2)]
    [TestCase(GamePhase.Invade, 3, 6)]
    public void DefenseWaveCalendarMapsStartPhaseToRealDay(GamePhase startPhase, int wave, int expectedDay)
    {
        Assert.AreEqual(expectedDay, DefenseWaveCalendar.GetDefenseDay(startPhase, wave));
        Assert.AreEqual(wave, DefenseWaveCalendar.GetDefenseWaveIndex(startPhase, expectedDay));
    }

    [Test]
    public void RelativeEngagementTimesReceiveOneSharedMinimumShift()
    {
        Fix64 shift = DefendPhaseRuntime.GetEditorTestWaveEngagementShift(
            new[] { (Fix64)5, (Fix64)8, (Fix64)3 },
            new[] { Fix64.Zero, (Fix64)4, (Fix64)10 });

        Assert.AreEqual((Fix64)5, shift);
        CollectionAssert.AreEqual(
            new[] { (Fix64)5, (Fix64)9, (Fix64)15 },
            new[] { Fix64.Zero + shift, (Fix64)4 + shift, (Fix64)10 + shift });
    }

    [Test]
    public void PlayerOwnedEarlySourcesDoNotDelayRemainingDefenseGroups()
    {
        const System.Reflection.BindingFlags staticFlags =
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        const System.Reflection.BindingFlags instanceFlags =
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;
        LogicStrongholdMap.Clear();
        try
        {
            LogicStrongholdMap.Initialize(
                FixVector2.Zero,
                new FixVector2(Fix64.One, Fix64.Zero),
                new FixVector2(Fix64.Zero, Fix64.One),
                Fix64.One,
                new[]
                {
                    new LogicStrongholdCellDefinition(
                        "SH_PLAYER_SOURCE",
                        0,
                        0,
                        EntitySideHelper.PlayerFactionId),
                    new LogicStrongholdCellDefinition(
                        "SH_ENEMY_SOURCE",
                        1,
                        0,
                        EntitySideHelper.EnemyFactionId)
                });

            Type routeType = typeof(DefendPhaseRuntime).GetNestedType(
                "DefendRouteDefinition",
                System.Reflection.BindingFlags.NonPublic);
            Type groupType = typeof(DefendPhaseRuntime).GetNestedType(
                "DefendAttackGroupDefinition",
                System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(routeType);
            Assert.NotNull(groupType);

            object earlyRoute = Activator.CreateInstance(routeType, true);
            routeType.GetField("SourceStrongholdId", instanceFlags)
                .SetValue(earlyRoute, "SH_PLAYER_SOURCE");
            object laterRoute = Activator.CreateInstance(routeType, true);
            routeType.GetField("SourceStrongholdId", instanceFlags)
                .SetValue(laterRoute, "SH_ENEMY_SOURCE");

            object earlyGroup = Activator.CreateInstance(groupType, true);
            groupType.GetField("Identifier", instanceFlags).SetValue(earlyGroup, "EarlyOccupied");
            groupType.GetField("Route", instanceFlags).SetValue(earlyGroup, earlyRoute);
            groupType.GetField("MinimumTravelLeadSeconds", instanceFlags).SetValue(earlyGroup, (Fix64)5);
            groupType.GetField("RelativeLeaderEngagementSeconds", instanceFlags).SetValue(earlyGroup, Fix64.Zero);
            object laterGroup = Activator.CreateInstance(groupType, true);
            groupType.GetField("Identifier", instanceFlags).SetValue(laterGroup, "LaterActive");
            groupType.GetField("Route", instanceFlags).SetValue(laterGroup, laterRoute);
            groupType.GetField("MinimumTravelLeadSeconds", instanceFlags).SetValue(laterGroup, (Fix64)3);
            groupType.GetField("RelativeLeaderEngagementSeconds", instanceFlags).SetValue(laterGroup, (Fix64)10);
            object latestGroup = Activator.CreateInstance(groupType, true);
            groupType.GetField("Identifier", instanceFlags).SetValue(latestGroup, "LatestActive");
            groupType.GetField("Route", instanceFlags).SetValue(latestGroup, laterRoute);
            groupType.GetField("MinimumTravelLeadSeconds", instanceFlags).SetValue(latestGroup, (Fix64)5);
            groupType.GetField("RelativeLeaderEngagementSeconds", instanceFlags).SetValue(latestGroup, (Fix64)14);

            Type listType = typeof(List<>).MakeGenericType(groupType);
            var groups = (System.Collections.IList)Activator.CreateInstance(listType);
            groups.Add(earlyGroup);
            groups.Add(laterGroup);
            groups.Add(latestGroup);
            object activeGroups = typeof(DefendPhaseRuntime)
                .GetMethod("FilterActiveAttackGroups", staticFlags)
                .Invoke(null, new object[] { groups });
            var activeList = (System.Collections.IList)activeGroups;
            Assert.AreEqual(2, activeList.Count);
            Assert.AreSame(laterGroup, activeList[0]);
            Assert.AreSame(latestGroup, activeList[1]);

            Fix64 activeShift = (Fix64)typeof(DefendPhaseRuntime)
                .GetMethod("CalculateWaveEngagementShift", staticFlags)
                .Invoke(null, new[] { activeGroups });
            Assert.AreEqual(Fix64.Zero, activeShift);
            Assert.AreEqual(
                (Fix64)10,
                groupType.GetField("RelativeLeaderEngagementSeconds", instanceFlags).GetValue(laterGroup),
                "过滤来源据点后只能重算全波共同平移，不能改写小队相对接战时间。");
            Assert.AreEqual(
                (Fix64)14,
                groupType.GetField("RelativeLeaderEngagementSeconds", instanceFlags).GetValue(latestGroup));
        }
        finally
        {
            LogicStrongholdMap.Clear();
        }
    }

    [Test]
    public void EmptyResolvedDefenseWaveCompletesAndSchedulesTheNextPhase()
    {
        LogicTestInGameDataModelAuthority.Ensure(GamePhase.Defend, nameof(DefendRouteRuntimeTests));
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        try
        {
            LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
            DefendPhaseRuntime.CancelRuntime();
            typeof(DefendPhaseRuntime)
                .GetField("s_DefenseWaveIndex", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .SetValue(null, 1);
            typeof(DefendPhaseRuntime)
                .GetField("s_DefendDay", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .SetValue(null, 2);

            typeof(DefendPhaseRuntime)
                .GetMethod("CompleteEmptyDefendWave", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, null);

            Assert.IsTrue(DefendPhaseRuntime.GetEditorTestIsSpawnScheduleCompleted());
            Assert.AreEqual(1, LogicPhaseCommandService.PendingCount);
            Assert.AreEqual(GamePhase.BuildBeforeInvade, LogicPhaseCommandService.History.Single().Phase);
        }
        finally
        {
            DefendPhaseRuntime.CancelRuntime();
            if (LogicPhaseCommandService.IsActive)
                LogicPhaseCommandService.EndTimeline();
            if (LogicTimeControlService.IsActive)
                LogicTimeControlService.EndTimeline();
        }
    }

    [Test]
    public void ZeroResourceEquivalentPresetPassesIntoInvadeAndTutorialRuntimeCaches()
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.NonPublic;
        System.Reflection.MethodInfo configure = typeof(PhaseManager).GetMethod("ConfigureInvadeSpawnPoints", flags);
        System.Reflection.MethodInfo clear = typeof(PhaseManager).GetMethod("ClearInvadeSpawnPoints", flags);
        Assert.NotNull(configure);
        Assert.NotNull(clear);

        UnityEngine.GameObject pointObject = new UnityEngine.GameObject("ZeroResourceEquivalentPreset");
        try
        {
            clear.Invoke(null, null);
            EntityPresetPoint point = pointObject.AddComponent<EntityPresetPoint>();
            point.PointType = EntityPresetPointType.Unit;
            point.Identifier = "Unit_CanMaker";
            point.SetUnitResourceEquivalent(Fix64.Zero, (Fix64)0.5m);

            Assert.AreEqual(Fix64.Zero, point.UnitResourceEquivalent);
            Assert.DoesNotThrow(() =>
                configure.Invoke(null, new object[] { new List<EntityPresetPoint> { point } }));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                point.SetUnitResourceEquivalent(Fix64.FromRaw(-1), Fix64.Zero));
        }
        finally
        {
            clear.Invoke(null, null);
            UnityEngine.Object.DestroyImmediate(pointObject);
        }
    }

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
    public void WaveSchedulerKeepsFixedIntervalInsideEachGroup()
    {
        Fix64[] scheduled = DefendPhaseRuntime.GetEditorTestWaveSpawnSeconds(
            new[] { Fix64.Zero, Fix64.Zero, Fix64.Zero, Fix64.Zero },
            new[] { (Fix64)10, (Fix64)10, (Fix64)10, (Fix64)10 },
            new[] { "A", "A", "B", "B" },
            new[] { 0, 1, 0, 1 },
            Fix64.One);

        Assert.AreEqual(Fix64.One, scheduled[1] - scheduled[0]);
        Assert.AreEqual(Fix64.One, scheduled[3] - scheduled[2]);
        Assert.AreNotEqual(scheduled[0], scheduled[2]);
    }

    [Test]
    public void WaveSchedulerAllowsLeaderWindowsToOverlapWithoutChangingGroupIntervals()
    {
        Fix64[] scheduled = DefendPhaseRuntime.GetEditorTestWaveSpawnSeconds(
            new[] { Fix64.Zero, Fix64.Zero, Fix64.Zero },
            new[] { Fix64.One, Fix64.One, Fix64.One },
            new[] { "A", "B", "C" },
            new[] { 0, 0, 0 },
            Fix64.FromRaw(3277));
        Assert.That(scheduled.All(value => value >= Fix64.Zero && value <= Fix64.One));
    }

    [Test]
    public void EveryMemberUsesItsGroupLeaderTravelTimeForSpeed()
    {
        Fix64[] speeds = DefendPhaseRuntime.GetEditorTestGroupExpectedEngagementSpeeds(
            (Fix64)50,
            (Fix64)12,
            new[] { (Fix64)2, (Fix64)4, (Fix64)6 },
            new[] { 0, 1, 2 });

        Assert.That(speeds[0], Is.EqualTo((Fix64)5));
        Assert.That(speeds[1], Is.EqualTo(speeds[0]));
        Assert.That(speeds[2], Is.EqualTo(speeds[0]));
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

        CollectionAssert.AreEqual(new[] { Fix64.Zero, (Fix64)10 }, scheduled);
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
        StringAssert.Contains("if (i != previewRouteIndex)", source);
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
                "1_0@Unsaved",
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
    public void DefenseEditorPreviewSelectionAndPreferencesFollowUserChoices()
    {
        const string levelPreferenceKey = "AAAGame.DefenseRouteEditor.SelectedLevel";
        const string showAllPreferenceKey = "AAAGame.DefenseRouteEditor.ShowAllRoutes";
        const string unitSizePreferenceKey = "AAAGame.DefenseRouteEditor.PreviewUnitSize";
        bool hadLevelPreference = UnityEditor.EditorPrefs.HasKey(levelPreferenceKey);
        bool hadShowAllPreference = UnityEditor.EditorPrefs.HasKey(showAllPreferenceKey);
        bool hadUnitSizePreference = UnityEditor.EditorPrefs.HasKey(unitSizePreferenceKey);
        string previousLevelPreference = UnityEditor.EditorPrefs.GetString(levelPreferenceKey, string.Empty);
        bool previousShowAllPreference = UnityEditor.EditorPrefs.GetBool(showAllPreferenceKey, true);
        int previousUnitSizePreference = UnityEditor.EditorPrefs.GetInt(unitSizePreferenceKey, (int)UnitSize.Small);

        Type windowType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("DefendRouteEditorWindow", false))
            .First(type => type != null);
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;
        UnityEditor.EditorWindow window = null;
        UnityEditor.EditorWindow restored = null;
        try
        {
            UnityEditor.EditorPrefs.SetString(levelPreferenceKey, "Lv_1");
            UnityEditor.EditorPrefs.SetBool(showAllPreferenceKey, true);
            UnityEditor.EditorPrefs.SetInt(unitSizePreferenceKey, (int)UnitSize.Small);

            window = (UnityEditor.EditorWindow)UnityEngine.ScriptableObject.CreateInstance(windowType);
            windowType.GetMethod("OnEnable", flags).Invoke(window, null);
            System.Collections.IList levels =
                (System.Collections.IList)windowType.GetField("_levels", flags).GetValue(window);
            int levelIndex = Enumerable.Range(0, levels.Count).Single(index =>
                string.Equals(
                    (string)levels[index].GetType().GetField("Identifier", flags).GetValue(levels[index]),
                    "Lv_2",
                    StringComparison.Ordinal));
            windowType.GetMethod("SelectLevel", flags).Invoke(window, new object[] { levelIndex });
            windowType.GetMethod("SetShowAllRoutes", flags).Invoke(window, new object[] { false });
            windowType.GetMethod("SetPreviewUnitSize", flags).Invoke(window, new object[] { UnitSize.Large });
            Assert.GreaterOrEqual(
                (int)windowType.GetField("_selectedRouteIndex", flags).GetValue(window),
                0,
                "关闭显示全部路线时必须立即选中一条可显示路线。");

            var routes = (System.Collections.IList)windowType
                .GetMethod("CurrentRoutes", flags)
                .Invoke(window, null);
            Assert.GreaterOrEqual(routes.Count, 2, "Lv_2 至少需要两条路线才能验证预览切换。");
            Type routeType = windowType.GetNestedType("RouteRecord", System.Reflection.BindingFlags.NonPublic);
            Type groupType = windowType.GetNestedType("GroupRecord", System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(routeType);
            Assert.NotNull(groupType);
            var groups = (System.Collections.IList)windowType
                .GetMethod("CurrentGroups", flags)
                .Invoke(window, null);
            Assert.IsNotEmpty(groups, "Lv_2 至少需要一个小队才能验证小队路线预览同步。");
            object group = groups[0];
            string groupRouteIdentifier = (string)groupType.GetField("RouteIdentifier", flags).GetValue(group);
            int groupRouteIndex = Enumerable.Range(0, routes.Count).Single(index =>
                string.Equals(
                    (string)routeType.GetField("Identifier", flags).GetValue(routes[index]),
                    groupRouteIdentifier,
                    StringComparison.Ordinal));

            int routeEditorIndex = groupRouteIndex == 0 ? 1 : 0;
            windowType.GetField("_selectedRouteIndex", flags).SetValue(window, routeEditorIndex);
            windowType.GetMethod("SelectGroupPreviewRoute", flags)
                .Invoke(window, new[] { group });
            Assert.AreEqual(
                routeEditorIndex,
                windowType.GetField("_selectedRouteIndex", flags).GetValue(window),
                "小队预览不能改变路线栏当前选择。");
            Assert.AreEqual(
                groupRouteIdentifier,
                windowType.GetField("_previewRouteIdentifier", flags).GetValue(window));
            Assert.AreEqual(
                groupRouteIdentifier,
                groupType.GetField("RouteIdentifier", flags).GetValue(group),
                "切换小队只能读取其路线用于预览，不能反向改写小队路线。");

            int changedRouteIndex = routeEditorIndex;
            string changedRouteIdentifier =
                (string)routeType.GetField("Identifier", flags).GetValue(routes[changedRouteIndex]);
            windowType.GetMethod("SelectRouteEditorPreview", flags).Invoke(window, null);
            Assert.AreEqual(
                routeEditorIndex,
                windowType.GetField("_selectedRouteIndex", flags).GetValue(window));
            Assert.AreEqual(
                changedRouteIdentifier,
                windowType.GetField("_previewRouteIdentifier", flags).GetValue(window),
                "从小队页切回路线页时应恢复路线栏自己的预览路线。");
            Assert.AreEqual(
                groupRouteIdentifier,
                groupType.GetField("RouteIdentifier", flags).GetValue(group),
                "切回路线页不能把路线栏选择写入小队路线。");

            windowType.GetMethod("ChangeGroupRoute", flags)
                .Invoke(window, new[] { group, changedRouteIdentifier });
            Assert.AreEqual(
                changedRouteIdentifier,
                groupType.GetField("RouteIdentifier", flags).GetValue(group));
            Assert.AreEqual(
                routeEditorIndex,
                windowType.GetField("_selectedRouteIndex", flags).GetValue(window),
                "修改小队路线只能改变 SceneView 预览，不能改变路线栏当前选择。");
            Assert.AreEqual(
                changedRouteIdentifier,
                windowType.GetField("_previewRouteIdentifier", flags).GetValue(window));

            UnityEngine.Object.DestroyImmediate(window);
            window = null;
            restored = (UnityEditor.EditorWindow)UnityEngine.ScriptableObject.CreateInstance(windowType);
            windowType.GetMethod("OnEnable", flags).Invoke(restored, null);
            Assert.AreEqual(
                "Lv_2",
                windowType.GetProperty("CurrentLevelIdentifier", flags).GetValue(restored));
            Assert.IsFalse((bool)windowType.GetField("_showAllRoutes", flags).GetValue(restored));
            Assert.AreEqual(UnitSize.Large, windowType.GetField("_previewUnitSize", flags).GetValue(restored));
        }
        finally
        {
            if (window != null)
                UnityEngine.Object.DestroyImmediate(window);
            if (restored != null)
                UnityEngine.Object.DestroyImmediate(restored);
            if (hadLevelPreference)
                UnityEditor.EditorPrefs.SetString(levelPreferenceKey, previousLevelPreference);
            else
                UnityEditor.EditorPrefs.DeleteKey(levelPreferenceKey);
            if (hadShowAllPreference)
                UnityEditor.EditorPrefs.SetBool(showAllPreferenceKey, previousShowAllPreference);
            else
                UnityEditor.EditorPrefs.DeleteKey(showAllPreferenceKey);
            if (hadUnitSizePreference)
                UnityEditor.EditorPrefs.SetInt(unitSizePreferenceKey, previousUnitSizePreference);
            else
                UnityEditor.EditorPrefs.DeleteKey(unitSizePreferenceKey);
        }
    }

    [Test]
    public void DefenseEditorSeparatesSquadTemplatesFromDailyWaveAssignments()
    {
        string source = File.ReadAllText(
            "Assets/AAAGame/ScriptsBuiltin/Editor/Defense/DefendRouteEditorWindow.cs");
        string onGui = ExtractSourceBlock(source, "private void OnGUI()", "private void DrawToolbar()");
        string squadUi = ExtractSourceBlock(source, "private void DrawGroups()", "private void DrawDailyWaves()");
        string dailyWaveUi = ExtractSourceBlock(
            source,
            "private void DrawDailyWaves()",
            "private bool DrawRecordSelector(");
        string dailyGroupUi = ExtractSourceBlock(
            source,
            "private void DrawDailyWaveGroups(int previewDay)",
            "private void SetGroupWaveActive(");

        StringAssert.Contains("new[] { \"路线\", \"小队\", \"每日波次\" }", onGui);
        StringAssert.Contains("DrawDailyWaves();", onGui);
        StringAssert.DoesNotContain("DrawDefenseWavePopup();", squadUi);
        StringAssert.DoesNotContain("ActiveDefenseWaves", squadUi);
        Assert.Less(
            dailyWaveUi.IndexOf("DrawDailyWaveGroups(previewDay);", StringComparison.Ordinal),
            dailyWaveUi.IndexOf("DrawDailyWaveSpawnPreview();", StringComparison.Ordinal),
            "每日小队配置必须紧跟日期选择，不能放在预览曲线下方。");
        Assert.Less(
            dailyWaveUi.IndexOf("DrawDailyWaveSpawnPreview();", StringComparison.Ordinal),
            dailyWaveUi.IndexOf("DrawDailyWaveSchedulePreview();", StringComparison.Ordinal),
            "每日总价必须位于出兵配置下方、排程预览上方。");
        StringAssert.Contains("foreach (GroupRecord group in groups)", dailyGroupUi);
        StringAssert.Contains("EditorGUILayout.Toggle(active", dailyGroupUi);
        StringAssert.Contains("SetGroupWaveActive(group, _previewDefenseWave, nextActive);", dailyGroupUi);
    }

    [Test]
    public void DefenseEditorTotalSpawnPreviewAggregatesEachUnitTypeAndLevel()
    {
        Type windowType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("DefendRouteEditorWindow", false))
            .First(type => type != null);
        System.Reflection.MethodInfo buildSummary = windowType.GetMethod(
            "BuildSpawnCountSummary",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(buildSummary);

        string summary = (string)buildSummary.Invoke(
            null,
            new object[]
            {
                new[] { "Unit_Brat", "Unit_CanMaker", "Unit_Brat", "Unit_Brat" },
                new[] { 1, 2, 1, 3 },
                new[] { 2, 4, 5, 1 }
            });

        Assert.AreEqual(
            "Unit_Brat Lv1 x7 / Unit_Brat Lv3 x1 / Unit_CanMaker Lv2 x4",
            summary);
    }

    [Test]
    public void DefenseEditorOffersSaveWithoutGeneratingDataTables()
    {
        string source = File.ReadAllText(
            "Assets/AAAGame/ScriptsBuiltin/Editor/Defense/DefendRouteEditorWindow.cs");
        string toolbar = ExtractSourceBlock(source, "private void DrawToolbar()", "private void DrawMapSummary()");
        string save = ExtractSourceBlock(source, "private void Save(bool generateDataTables)", "private void WriteRoutes()");

        StringAssert.Contains("Button(\"仅保存\"", toolbar);
        StringAssert.Contains("Save(generateDataTables: false);", toolbar);
        StringAssert.Contains("Save(generateDataTables: true);", toolbar);
        StringAssert.Contains("WriteRoutes();", save);
        StringAssert.Contains("WriteGroups();", save);
        StringAssert.Contains("if (generateDataTables)", save);
        StringAssert.Contains("GameDataGenerator.RefreshAllDataTable", save);
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
                    expected += $"@{suffix}";
                Assert.AreEqual(expected, routeType.GetField("Identifier", flags).GetValue(route));
            }

            foreach (object group in groups)
            {
                string routeIdentifier = (string)groupType.GetField("RouteIdentifier", flags).GetValue(group);
                string suffix = (string)groupType.GetField("Suffix", flags).GetValue(group);
                string expected = routeIdentifier;
                if (!string.IsNullOrEmpty(suffix))
                    expected += $"@{suffix}";
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
            string groupUi = ExtractSourceBlock(source, "private void DrawGroups()", "private void DrawDailyWaves()");
            StringAssert.Contains("TextField(\"路线后缀（可空）\"", routeUi);
            StringAssert.Contains("TextField(\"小队后缀（可空）\"", groupUi);
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
    public void DefenseEditorNewGroupDefaultCanAffordItsSelectedLevelOneUnit()
    {
        Type windowType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("DefendRouteEditorWindow", false))
            .First(type => type != null);
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

            System.Collections.IList groups =
                (System.Collections.IList)windowType.GetField("_groups", flags).GetValue(window);
            int previousCount = groups.Count;
            windowType.GetMethod("AddGroup", flags).Invoke(window, null);
            Assert.AreEqual(previousCount + 1, groups.Count);

            object group = groups[groups.Count - 1];
            var arguments = new object[] { group, 2, null, null };
            bool resolved = (bool)windowType
                .GetMethod("TryBuildGroupResourceEquivalentPreview", flags)
                .Invoke(window, arguments);
            Assert.IsTrue(resolved, arguments[3] as string);
        }
        finally
        {
            if (window != null)
                UnityEngine.Object.DestroyImmediate(window);
        }
    }

    [Test]
    public void DefenseEditorRealLv2RouteZeroDayTwoResolvesTwoLevelOneUnitsAndAllowsZero()
    {
        Type windowType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("DefendRouteEditorWindow", false))
            .First(type => type != null);
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

            Type groupType = windowType.GetNestedType("GroupRecord", System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(groupType);
            System.Collections.IList groups =
                (System.Collections.IList)windowType.GetField("_groups", flags).GetValue(window);
            object group = groups.Cast<object>().Single(candidate =>
                string.Equals((string)groupType.GetField("LevelIdentifier", flags).GetValue(candidate), "Lv_2", StringComparison.Ordinal)
                && string.Equals((string)groupType.GetField("Identifier", flags).GetValue(candidate), "0", StringComparison.Ordinal));
            System.Reflection.MethodInfo buildPreview =
                windowType.GetMethod("TryBuildGroupResourceEquivalentPreview", flags);
            Assert.NotNull(buildPreview);

            groupType.GetField("InitialResourceEquivalent", flags).SetValue(group, 2f);
            groupType.GetField("CountGrowthWeight", flags).SetValue(group, 1f);
            object[] dayTwoArguments = { group, 2, null, null };
            Assert.IsTrue((bool)buildPreview.Invoke(window, dayTwoArguments), dayTwoArguments[3] as string);
            object dayTwoPreview = dayTwoArguments[2];
            Type previewType = dayTwoPreview.GetType();
            Assert.AreEqual(2, previewType.GetProperty("TotalCount", flags).GetValue(dayTwoPreview));
            Assert.AreEqual("Lv1 x2", previewType.GetProperty("CompositionText", flags).GetValue(dayTwoPreview));
            Fix64 target = (Fix64)previewType.GetProperty("Target", flags).GetValue(dayTwoPreview);
            Assert.That(target.RawValue, Is.EqualTo(((Fix64)3.2m).RawValue).Within(2));

            groupType.GetField("InitialResourceEquivalent", flags).SetValue(group, 0f);
            object[] zeroArguments = { group, 2, null, null };
            Assert.IsTrue((bool)buildPreview.Invoke(window, zeroArguments), zeroArguments[3] as string);
            object zeroPreview = zeroArguments[2];
            Assert.AreEqual(0, previewType.GetProperty("TotalCount", flags).GetValue(zeroPreview));
            Assert.AreEqual(Fix64.Zero, previewType.GetProperty("Actual", flags).GetValue(zeroPreview));
            Assert.AreEqual(Fix64.Zero, previewType.GetProperty("ErrorRate", flags).GetValue(zeroPreview));
            Assert.AreEqual("无单位", previewType.GetProperty("CompositionText", flags).GetValue(zeroPreview));

            foreach (object candidate in groups.Cast<object>().Where(candidate =>
                         string.Equals(
                             (string)groupType.GetField("LevelIdentifier", flags).GetValue(candidate),
                             "Lv_2",
                             StringComparison.Ordinal)))
            {
                ((List<int>)groupType.GetField("ActiveDefenseWaves", flags).GetValue(candidate)).Clear();
            }
            ((List<int>)groupType.GetField("ActiveDefenseWaves", flags).GetValue(group)).Add(1);
            groupType.GetField("RelativeLeaderEngagementSecondsByWave", flags)
                .SetValue(group, new List<float> { 0f });
            groupType.GetField("RouteIdentifier", flags).SetValue(group, "MissingRouteForEmptyComposition");
            object[] shiftArguments = { 1, 2, null, null };
            bool shiftResolved = (bool)windowType
                .GetMethod("TryCalculatePreviewEngagementShift", flags)
                .Invoke(window, shiftArguments);
            Assert.IsTrue(shiftResolved, shiftArguments[3] as string);
            Assert.AreEqual(0f, (float)shiftArguments[2]);
        }
        finally
        {
            if (window != null)
                UnityEngine.Object.DestroyImmediate(window);
        }
    }

    [Test]
    public void DefenseEditorFix64TextRoundTripPreservesTinyPositiveValuesAndRejectsNonFiniteValues()
    {
        Type windowType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("DefendRouteEditorWindow", false))
            .First(type => type != null);
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.NonPublic;
        System.Reflection.MethodInfo format = windowType.GetMethod("FormatFloat", flags);
        System.Reflection.MethodInfo parse = windowType.GetMethod("ParseFloat", flags);
        Assert.NotNull(format);
        Assert.NotNull(parse);

        float source = (float)Fix64.FromRaw(1);
        string text = (string)format.Invoke(null, new object[] { source });
        Assert.AreNotEqual("0", text);
        float parsed = (float)parse.Invoke(null, new object[] { text });
        Assert.AreEqual(Fix64.FromRaw(1), (Fix64)parsed);

        System.Reflection.TargetInvocationException nanException =
            Assert.Throws<System.Reflection.TargetInvocationException>(
                () => format.Invoke(null, new object[] { float.NaN }));
        Assert.IsInstanceOf<FormatException>(nanException.InnerException);
        System.Reflection.TargetInvocationException emptyException =
            Assert.Throws<System.Reflection.TargetInvocationException>(
                () => parse.Invoke(null, new object[] { string.Empty }));
        Assert.IsInstanceOf<FormatException>(emptyException.InnerException);
    }

    [Test]
    public void GameConfigDoesNotContainDefenseEditorDraftDefaults()
    {
        string config = File.ReadAllText("Assets/AAAGame/Config/GameConfig.txt");
        StringAssert.DoesNotContain("EnemyDefenseDefaultInitialResourceEquivalent", config);
        StringAssert.DoesNotContain("EnemyDefenseDefaultCountGrowthWeight", config);
        StringAssert.DoesNotContain("EnemyDefenseDefaultRelativeLeaderEngagementSeconds", config);
        StringAssert.DoesNotContain("EnemyResourceEquivalentPreviewWarningErrorRate", config);
    }

    [Test]
    public void GeneratedConfigUsesQuadraticGrowthAndNoQuantitySynergyCurve()
    {
        string config = File.ReadAllText("Assets/AAAGame/Config/GameConfig.txt");

        StringAssert.Contains("EnemyGarrisonPreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent", config);
        StringAssert.Contains("EnemyDefensePreExpectedDailyIncrementIncreasePerDay1BaseResourceEquivalent", config);
        StringAssert.DoesNotContain("EnemyGarrisonPreExpectedDailyIncrementMultiplier", config);
        StringAssert.DoesNotContain("EnemyDefensePreExpectedDailyIncrementMultiplier", config);
        StringAssert.DoesNotContain("EnemyStrengthEarlyCountPivot", config);
        StringAssert.DoesNotContain("EnemyStrengthEarlyCountSynergy", config);
        StringAssert.DoesNotContain("EnemyStrengthCrowdingTailScale", config);
    }

    [Test]
    public void DefenseEditorGroupOrderDoesNotChangeWhenWaveEnablementChanges()
    {
        string source = File.ReadAllText(
            "Assets/AAAGame/ScriptsBuiltin/Editor/Defense/DefendRouteEditorWindow.cs");
        string currentGroups = ExtractSourceBlock(
            source,
            "private List<GroupRecord> CurrentGroups()",
            "private string FirstEnemyTeleportationId()");

        StringAssert.Contains("_groups", currentGroups);
        StringAssert.Contains(".Where(", currentGroups);
        StringAssert.DoesNotContain("OrderBy", currentGroups);
        StringAssert.DoesNotContain("ActiveDefenseWaves", currentGroups);
    }

    [Test]
    public void EachDefenseLevelHasAtLeastOneGroupInDefenseWaveOne()
    {
        string[] lines = File.ReadAllLines(
            "Assets/AAAGame/DataTable/Level/DefendAttackGroupTable.txt");
        var matchedLevels = new HashSet<string>();
        foreach (string line in lines.Skip(4))
        {
            string[] columns = line.Split('	');
            if (columns.Length < 6)
                throw new InvalidDataException($"Malformed defense group row: '{line}'.");
            string levelIdentifier = columns[4];
            if (levelIdentifier != "Lv_2"
                && levelIdentifier != "Lv_3"
                && levelIdentifier != "LvTest")
            {
                continue;
            }

            if (columns[5].Split(',').Contains("1"))
                matchedLevels.Add(levelIdentifier);
        }

        CollectionAssert.AreEquivalent(
            new[] { "Lv_2", "Lv_3", "LvTest" },
            matchedLevels,
            "每个防御关卡的第一波必须至少有一个小队，后续波专属小队不要求在第一波启用。");
    }

    [Test]
    public void DefenseGroupActiveWavesHaveOneRelativeLeaderEngagementTimePerWave()
    {
        string[] lines = File.ReadAllLines(
            "Assets/AAAGame/DataTable/Level/DefendAttackGroupTable.txt");
        string[] headers = lines[1].Split('\t');
        CollectionAssert.DoesNotContain(headers, "AfterGroupIdentifier");
        CollectionAssert.DoesNotContain(headers, "StartDelaySeconds");
        int activeWavesColumn = Array.IndexOf(headers, "ActiveDefenseWaves");
        int engagementColumn = Array.IndexOf(headers, "RelativeLeaderEngagementSeconds");
        Assert.That(activeWavesColumn, Is.GreaterThanOrEqualTo(0));
        Assert.That(engagementColumn, Is.GreaterThanOrEqualTo(0));

        foreach (string line in lines.Skip(4))
        {
            string[] columns = line.Split('\t');
            if (columns.Length <= engagementColumn)
                throw new InvalidDataException($"Malformed defense group row: '{line}'.");
            string[] activeWaves = columns[activeWavesColumn].Split(',');
            string[] engagementTimes = columns[engagementColumn].Split(',');
            Assert.That(engagementTimes.Length, Is.EqualTo(activeWaves.Length), line);
            Assert.That(engagementTimes.All(x => decimal.Parse(x, System.Globalization.CultureInfo.InvariantCulture) >= 0m));
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
