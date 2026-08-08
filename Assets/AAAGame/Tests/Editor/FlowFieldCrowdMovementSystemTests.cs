using NUnit.Framework;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine.TestTools;

[TestFixture]
[SingleThreaded]
public class FlowFieldCrowdMovementSystemTests
{
    private LogicTestGroupMoveManagerAuthority m_GroupMoveAuthority;

    [SetUp]
    public void SetUp()
    {
        EntityRegistry.Clear();
        SetupCombatPhaseForTests();
        if (LogicTimeControlService.IsActive || LogicPhaseCommandService.IsActive)
            throw new InvalidOperationException("FlowFieldCrowdMovementSystemTests requires inactive logic phase services at setup.");
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.ClearEditorTestClock();
        FlowFieldCrowdMovementSystem.PrepareRuntimeDependencies();
        FlowFieldCrowdMovementSystem.SetConfig(CreateConfig());
        m_GroupMoveAuthority = LogicTestGroupMoveManagerAuthority.Create(nameof(FlowFieldCrowdMovementSystemTests));
    }

    [TearDown]
    public void TearDown()
    {
        EntityRegistry.Clear();
        if (LogicPhaseCommandService.IsActive)
            LogicPhaseCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
        m_GroupMoveAuthority?.Dispose();
        m_GroupMoveAuthority = null;
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.ClearEditorTestClock();
    }

    private static void SetupCombatPhaseForTests()
    {
        LogicTestInGameDataModelAuthority.Ensure(GamePhase.Defend, nameof(FlowFieldCrowdMovementSystemTests));
        FieldInfo dataModelField = typeof(GF).GetField("<DataModel>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        GameFramework.DataModelComponent current = dataModelField?.GetValue(null) as GameFramework.DataModelComponent;
        if (current == null)
        {
            GameObject go = new GameObject("FlowFieldTests_DataModel");
            current = go.AddComponent<GameFramework.DataModelComponent>();
            dataModelField?.SetValue(null, current);
        }

        FieldInfo dataModelsField = typeof(GameFramework.DataModelComponent).GetField("m_DataModels", BindingFlags.Instance | BindingFlags.NonPublic);
        object dataModels = dataModelsField?.GetValue(current);
        if (dataModelsField != null && (dataModels == null || dataModels.GetType() != dataModelsField.FieldType))
        {
            dataModels = Activator.CreateInstance(dataModelsField.FieldType);
            dataModelsField.SetValue(current, dataModels);
        }

        InGameDataModel model = current.GetDataModel<InGameDataModel>();
        if (model == null)
        {
            model = (InGameDataModel)Activator.CreateInstance(typeof(InGameDataModel), true);
            Type typeIdPairType = typeof(GameFramework.DataModelComponent).Assembly.GetType("TypeIdPair");
            object pair = Activator.CreateInstance(
                typeIdPairType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { typeof(InGameDataModel), 0 },
                null);
            object dict = dataModelsField.GetValue(current);
            dict.GetType().GetMethod("Add")?.Invoke(dict, new[] { pair, model });
        }

        FieldInfo phaseField = typeof(InGameDataModel).GetField("m_IngameValue", BindingFlags.Instance | BindingFlags.NonPublic);
        Dictionary<IngameValueType, int> values = new Dictionary<IngameValueType, int>
        {
            [IngameValueType.Phase] = (int)GamePhase.Defend,
            [IngameValueType.Day] = 1,
            [IngameValueType.Coin] = 0,
            [IngameValueType.CurrentSupply] = 0,
            [IngameValueType.MaxSupply] = 0,
        };
        phaseField?.SetValue(model, values);
    }

    [Test]
    public void NavigationDeterministicHash_IsStableAndTracksFutureAffectingState()
    {
        const int width = 8;
        const int height = 4;
        var walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        int refreshCountAfterBuild = FlowFieldCrowdMovementSystem.DeterministicWorldHashRefreshCount;
        Assert.Greater(refreshCountAfterBuild, 0);
        ulong baseline = FlowFieldCrowdMovementSystem.CaptureDiagnosticStateHash();
        Assert.AreEqual(baseline, FlowFieldCrowdMovementSystem.CaptureDiagnosticStateHash());
        Assert.AreEqual(
            refreshCountAfterBuild,
            FlowFieldCrowdMovementSystem.DeterministicWorldHashRefreshCount,
            "Per-frame NavigationHash capture must reuse the committed world fingerprint.");

        FlowFieldCrowdMovementSystem.RegisterCircleObstacle(81001, new Vector3(2.5f, 0f, 1.5f), 0.4f);
        ulong obstaclePending = FlowFieldCrowdMovementSystem.CaptureDiagnosticStateHash();
        Assert.AreNotEqual(baseline, obstaclePending, "Pending runtime obstacle state must affect NavigationHash.");

        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        ulong rebuildProgress = FlowFieldCrowdMovementSystem.CaptureDiagnosticStateHash();
        Assert.AreNotEqual(obstaclePending, rebuildProgress, "Runtime rebuild progress must affect NavigationHash.");
        Assert.AreEqual(rebuildProgress, FlowFieldCrowdMovementSystem.CaptureDiagnosticStateHash());
    }

    [Test]
    public void NavigationAuthorityConfig_DoesNotReadFloatValues()
    {
        string scriptsRoot = Path.Combine(Application.dataPath, "AAAGame", "Scripts");
        string[] authorityFiles =
        {
            Path.Combine(scriptsRoot, "Movement", "FlowFieldCrowdMovementSystem.cs"),
            Path.Combine(scriptsRoot, "Common", "DistanceUnitConverter.cs"),
            Path.Combine(scriptsRoot, "DataTable", "CharacterDataDetailAccessor.cs"),
            Path.Combine(scriptsRoot, "Card", "LogicCardCommandService.cs"),
            Path.Combine(scriptsRoot, "Card", "LogicCardPlacementAuthority.cs"),
            Path.Combine(scriptsRoot, "GameClass", "DefendPhaseRuntime.cs"),
            Path.Combine(scriptsRoot, "MeiyouUtility", "RewardManager.cs")
        };
        string[] buffFiles = Directory.GetFiles(
            Path.Combine(scriptsRoot, "Buff"),
            "*.cs",
            SearchOption.AllDirectories);
        Array.Sort(buffFiles, StringComparer.Ordinal);

        for (int i = 0; i < authorityFiles.Length; i++)
        {
            string source = File.ReadAllText(authorityFiles[i]);
            Assert.That(source, Does.Not.Contain("GF.Config.GetFloat"), authorityFiles[i]);
        }
        for (int i = 0; i < buffFiles.Length; i++)
        {
            string source = File.ReadAllText(buffFiles[i]);
            Assert.That(source, Does.Not.Contain("GF.Config.GetFloat"), buffFiles[i]);
        }

        string flowSource = File.ReadAllText(authorityFiles[0]);
        Assert.That(flowSource, Does.Contain("ResolveConfiguredAgentTypeRadiusFixed"));
        Assert.That(flowSource, Does.Contain("DistanceConversionRateFixed"));
        Assert.That(flowSource, Does.Not.Contain("return Time.frameCount"));
        Assert.That(flowSource, Does.Not.Contain("return Time.time"));
    }

    [Test]
    public void RuntimeAgentTypeRadius_IsFrozenBeforeSceneNavigationSourceEnable()
    {
        const string configKey = "MediumUnitCollisionRadius";
        Fix64 configuredRadius = DistanceUnitConverter.ReadRequiredPositiveFixedConfig(configKey);
        Fix64 expectedWorldRadius = DistanceUnitConverter.ConvertToWorld(configuredRadius);
        MethodInfo resolveRadius = typeof(FlowFieldCrowdMovementSystem).GetMethod(
            "ResolveAgentTypeRadiusFixed",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(resolveRadius);

        try
        {
            DistanceUnitConverter.SetEditorTestPositiveFixedConfig(configKey, configuredRadius + Fix64.One);
            Fix64 frozenRadius = (Fix64)resolveRadius.Invoke(
                null,
                new object[] { AgentTypeHelper.MediumMovementTypeId });

            Assert.AreEqual(expectedWorldRadius, frozenRadius);
        }
        finally
        {
            DistanceUnitConverter.SetEditorTestPositiveFixedConfig(configKey, configuredRadius);
            FlowFieldCrowdMovementSystem.PrepareRuntimeDependencies();
        }
    }

    [Test]
    public void FlowAuthorityConstants_UseRawFixedValues()
    {
        string flowPath = Path.Combine(
            Application.dataPath,
            "AAAGame",
            "Scripts",
            "Movement",
            "FlowFieldCrowdMovementSystem.cs");
        string source = File.ReadAllText(flowPath);
        var floatLiteralToFixedPattern = new System.Text.RegularExpressions.Regex(
            @"\(Fix64\)\s*\(?-?\d+(?:\.\d+)?f\)?",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        Assert.That(
            floatLiteralToFixedPattern.Matches(source).Count,
            Is.Zero,
            "Flow authority source must declare numeric fixed constants by raw value; float variables are only allowed at explicit Unity/test boundaries.");

        Fix64[] legacyValues =
        {
            (Fix64)0.00001f,
            (Fix64)0.0001f,
            (Fix64)0.001f,
            (Fix64)0.01f,
            (Fix64)0.015f,
            (Fix64)0.02f,
            (Fix64)0.04f,
            (Fix64)0.05f,
            (Fix64)0.2f,
            (Fix64)0.25f,
            (Fix64)0.35f,
            (Fix64)0.45f,
            (Fix64)0.5f,
            (Fix64)0.75f,
        };
        long[] expectedRaw = { 1, 1, 5, 41, 62, 82, 164, 205, 820, 1024, 1434, 1844, 2048, 3072 };
        Assert.AreEqual(expectedRaw.Length, legacyValues.Length);
        for (int i = 0; i < expectedRaw.Length; i++)
            Assert.AreEqual(expectedRaw[i], legacyValues[i].RawValue, $"Legacy Flow Q12 raw mismatch at index {i}.");
    }

    [Test]
    public void NavigationAuthorityFrame_BeforeLogicTimelineIsSetupFrameZero()
    {
        Assert.IsFalse(LogicFrameRuntime.IsTimelineRunning);
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetCurrentNavigationFrame());
    }

    [Test]
    public void AuthoredGridSourceUsesBakedFixedPayloadAndRejectsFloatShadowDrift()
    {
        const int width = 3;
        const int height = 2;
        const float cellSize = 0.09f;
        var origin = new Vector3(12.78f, 0f, 7.11f);
        bool[] walkable = new bool[width * height];
        byte[] costs = new byte[width * height];
        byte[] neighbors = new byte[width * height];
        Vector3[] anchors = new Vector3[width * height];
        int[] neighborOffsetX = { -1, 0, 1, -1, 1, -1, 0, 1 };
        int[] neighborOffsetY = { -1, -1, -1, 0, 0, 1, 1, 1 };
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = x + y * width;
                walkable[index] = true;
                costs[index] = 1;
                anchors[index] = new Vector3(
                    origin.x + (x + 0.5f) * cellSize,
                    0f,
                    origin.z + (y + 0.5f) * cellSize);

                byte mask = 0;
                for (int direction = 0; direction < 8; direction++)
                {
                    int nextX = x + neighborOffsetX[direction];
                    int nextY = y + neighborOffsetY[direction];
                    if (nextX >= 0 && nextX < width && nextY >= 0 && nextY < height)
                        mask |= (byte)(1 << direction);
                }
                neighbors[index] = mask;
            }
        }
        anchors[0] = new Vector3(origin.x + 0.03f, 0f, origin.z + 0.04f);

        FlowNavigationGridAsset asset = ScriptableObject.CreateInstance<FlowNavigationGridAsset>();
        GameObject sourceObject = new GameObject("FixedAuthoredGridSourceTest");
        try
        {
            asset.Overwrite(0, width, height, cellSize, origin, walkable, costs, anchors, neighbors);
            FlowNavigationGridAsset.DerivedNavigationData derivedData = FlowFieldCrowdMovementSystem.BuildDerivedNavigationDataForAsset(
                0,
                DistanceUnitConverter.ConvertToWorld(DistanceUnitConverter.ReadRequiredPositiveFixedConfig("MediumUnitCollisionRadius")),
                width,
                height,
                cellSize,
                origin,
                walkable,
                anchors,
                costs,
                neighbors);
            asset.SetDerivedNavigationData(derivedData);

            FlowNavigationGridSource source = sourceObject.AddComponent<FlowNavigationGridSource>();
            source.Configure(asset, applyOnEnable: false, applyInEditMode: false, clearOnDisable: false);
            source.ApplyToFlowField();
            ProcessWorldBuildQueueUntilReady();

            FlowNavigationGridAsset.FixedAuthorityMetadata metadata = asset.GetFixedAuthorityMetadata();
            FixVector2 expectedCenter = NavigationGridFixedMath.GridCellCenterFixed(
                metadata.CellSizeGridRaw,
                metadata.OriginXGridRaw,
                metadata.OriginZGridRaw,
                1,
                1);
            FixVector2 actualCenter = FlowFieldCrowdMovementSystem.GetEditorTestGridToWorldCenterFixed(1, 1);
            Assert.AreEqual(expectedCenter.x.RawValue, actualCenter.x.RawValue);
            Assert.AreEqual(expectedCenter.y.RawValue, actualCenter.y.RawValue);

            FieldInfo cellSizeField = typeof(FlowNavigationGridAsset).GetField("_cellSize", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(cellSizeField);
            cellSizeField.SetValue(asset, 0.1f);
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => source.ApplyToFlowField());
            StringAssert.Contains("fixed authority metadata does not match", exception.Message);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sourceObject);
            UnityEngine.Object.DestroyImmediate(asset);
        }
    }

    [Test]
    public void FlowNavigationGridSource_AutomaticRuntimeApply_RequiresActiveLogicRuntime()
    {
        MethodInfo shouldApply = typeof(FlowNavigationGridSource).GetMethod(
            "ShouldApplyOnEnable",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(shouldApply);

        Assert.IsFalse((bool)shouldApply.Invoke(null, new object[] { true, false, false }));
        Assert.IsTrue((bool)shouldApply.Invoke(null, new object[] { true, false, true }));
        Assert.IsFalse((bool)shouldApply.Invoke(null, new object[] { false, false, true }));
        Assert.IsTrue((bool)shouldApply.Invoke(null, new object[] { false, true, false }));
    }

    [Test]
    public void FlowNavigationGridSource_RuntimeTransitionClearsStaleDeferredOwner()
    {
        FieldInfo appliedOwnerField = typeof(FlowNavigationGridSource).GetField(
            "s_AppliedSourceInstanceId",
            BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo pendingOwnerField = typeof(FlowNavigationGridSource).GetField(
            "s_PendingClearSourceInstanceId",
            BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo clearOwnedSource = typeof(FlowNavigationGridSource).GetMethod(
            "ClearAppliedSourceIfOwned",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(appliedOwnerField);
        Assert.IsNotNull(pendingOwnerField);
        Assert.IsNotNull(clearOwnedSource);

        GameObject sourceObject = new GameObject("RuntimeTransitionGridSourceTest");
        FlowNavigationGridSource source = sourceObject.AddComponent<FlowNavigationGridSource>();
        try
        {
            LogicFrameRuntime.Begin();
            LogicFrameRuntime.StartTimeline();
            FlowFieldCrowdMovementSystem.BeginRuntimeNavigationTransition();
            appliedOwnerField.SetValue(null, source.GetInstanceID());
            pendingOwnerField.SetValue(null, source.GetInstanceID() - 1);

            Assert.DoesNotThrow(() => clearOwnedSource.Invoke(source, null));
            Assert.AreEqual(0, appliedOwnerField.GetValue(null));
            Assert.AreEqual(0, pendingOwnerField.GetValue(null));
            Assert.IsFalse(FlowFieldCrowdMovementSystem.HasAuthoredNavigationSource());
        }
        finally
        {
            FlowFieldCrowdMovementSystem.ForceEndRuntimeNavigationTransition();
            pendingOwnerField.SetValue(null, 0);
            appliedOwnerField.SetValue(null, 0);
            if (LogicFrameRuntime.IsActive)
                LogicFrameRuntime.End();
            UnityEngine.Object.DestroyImmediate(sourceObject);
        }
    }

    [Test]
    public void 更新障碍位置必须同时重建旧Bounds和新Bounds()
    {
        const int width = 32;
        const int height = 3;
        var walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.RegisterCircleObstacleFixed(
            82001,
            new FixVector2((Fix64)1.5f, (Fix64)1.5f),
            (Fix64)0.4f);
        ProcessRuntimeDirtyQueueUntilReady(1);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestRuntimeCellDiagnostics(
            1,
            1,
            out bool oldWalkableBeforeMove,
            out _,
            out _,
            out _,
            out _,
            out _));
        Assert.IsFalse(oldWalkableBeforeMove);

        FlowFieldCrowdMovementSystem.RegisterCircleObstacleFixed(
            82001,
            new FixVector2((Fix64)30.5f, (Fix64)1.5f),
            (Fix64)0.4f);
        ProcessRuntimeDirtyQueueUntilReady(100);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestRuntimeCellDiagnostics(
            1,
            1,
            out bool oldWalkableAfterMove,
            out _,
            out _,
            out _,
            out _,
            out _));
        Assert.IsTrue(oldWalkableAfterMove, "同 ID 障碍移走后旧位置必须从 authored mask 恢复。");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestRuntimeCellDiagnostics(
            30,
            1,
            out bool newWalkableAfterMove,
            out _,
            out _,
            out _,
            out _,
            out _));
        Assert.IsFalse(newWalkableAfterMove);
    }

    [Test]
    public void 同一障碍Id切换Shape不得残留旧类型()
    {
        const int obstacleId = 82002;
        FlowFieldCrowdMovementSystem.RegisterCircleObstacleFixed(
            obstacleId,
            new FixVector2((Fix64)1.5f, (Fix64)1.5f),
            (Fix64)0.4f);

        FlowFieldCrowdMovementSystem.RegisterBoxObstacleFixed(
            obstacleId,
            new FixVector2((Fix64)4.5f, (Fix64)1.5f),
            new FixVector2((Fix64)0.4f, (Fix64)0.4f));

        Assert.IsFalse(FlowFieldCrowdMovementSystem.TryGetEditorTestCircleObstacleFixed(
            obstacleId,
            out _,
            out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestBoxObstacleFixed(
            obstacleId,
            out _,
            out _));
    }

    [Test]
    public void NavigationDiagnosticCheckpoint_RefreshesOnlyAtFixedLogicFrameCadence()
    {
        const int width = 8;
        const int height = 4;
        var walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        FlowFieldCrowdMovementSystem.WriteDiagnosticCheckpointState(new LogicStateHasher(), 1);
        Assert.AreEqual(1, FlowFieldCrowdMovementSystem.DiagnosticCheckpointRefreshCount);

        for (ulong frame = 2; frame <= 30; frame++)
            FlowFieldCrowdMovementSystem.WriteDiagnosticCheckpointState(new LogicStateHasher(), frame);
        Assert.AreEqual(1, FlowFieldCrowdMovementSystem.DiagnosticCheckpointRefreshCount);

        FlowFieldCrowdMovementSystem.WriteDiagnosticCheckpointState(new LogicStateHasher(), 31);
        Assert.AreEqual(2, FlowFieldCrowdMovementSystem.DiagnosticCheckpointRefreshCount);
    }

    [Test]
    public void NavigationFrameDigest_IsStableAndDoesNotRefreshFullCheckpoint()
    {
        const int width = 8;
        const int height = 4;
        var walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        var baselineHasher = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(baselineHasher);
        var repeatedHasher = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(repeatedHasher);
        Assert.AreEqual(baselineHasher.Hash, repeatedHasher.Hash);
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.DiagnosticCheckpointRefreshCount);

        FlowFieldCrowdMovementSystem.RegisterCircleObstacle(81002, new Vector3(2.5f, 0f, 1.5f), 0.4f);
        var changedHasher = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(changedHasher);
        Assert.AreNotEqual(baselineHasher.Hash, changedHasher.Hash);
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.DiagnosticCheckpointRefreshCount);
    }

    [Test]
    public void NavigationFrameDigest_TracksFixedObstaclePayloadWhenCountIsUnchanged()
    {
        const int width = 8;
        const int height = 4;
        var walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.RegisterCircleObstacle(81003, new Vector3(2.5f, 0f, 1.5f), 0.4f);
        var first = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(first);

        FlowFieldCrowdMovementSystem.RegisterCircleObstacle(81003, new Vector3(3.5f, 0f, 1.5f), 0.4f);
        var second = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(second);

        Assert.AreNotEqual(first.Hash, second.Hash);
    }

    [Test]
    public void NavigationAuthorityDigest_WorldBuildPortalProgressMustAffectHash()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 16;
        const int height = 8;
        var walkable = new bool[width * height];
        Array.Fill(walkable, true);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild());

        var baseline = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(baseline);
        FlowFieldCrowdMovementSystem.AddEditorTestOnlyWorldBuildPortalProgressProbe();
        var portalProgress = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(portalProgress);
        Assert.AreNotEqual(baseline.Hash, portalProgress.Hash);

        FlowFieldCrowdMovementSystem.AddEditorTestOnlyWorldBuildPendingPortalAccessProbe();
        var accessProgress = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(accessProgress);
        Assert.AreNotEqual(portalProgress.Hash, accessProgress.Hash);
    }

    [Test]
    public void NavigationAuthorityDigest_RuntimeDirtyProcessedBoundariesMustAffectHash()
    {
        const int width = 16;
        const int height = 8;
        var walkable = new bool[width * height];
        Array.Fill(walkable, true);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.RegisterCircleObstacle(81004, new Vector3(2.5f, 0f, 1.5f), 0.4f);
        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());

        var baseline = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(baseline);
        FlowFieldCrowdMovementSystem.AddEditorTestOnlyRuntimeDirtyProcessedBoundaryProbe();
        var changed = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(changed);
        Assert.AreNotEqual(baseline.Hash, changed.Hash);
    }

    [Test]
    public void NavigationAuthorityDigest_WorldBuildWorkingWorldTracksAuthorityButIgnoresFloatShadow()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 64;
        const int height = 32;
        var walkable = new bool[width * height];
        Array.Fill(walkable, true);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        for (int i = 0; i < 32 && !FlowFieldCrowdMovementSystem.HasEditorTestOnlyWorldBuildWorkingWorld(); i++)
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild());
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestOnlyWorldBuildWorkingWorld());

        var baseline = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(baseline);
        var repeatedBaseline = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(repeatedBaseline);
        Assert.AreEqual(baseline.Hash, repeatedBaseline.Hash, "相同 WorldBuild WorkingWorld 的重复 authority digest 必须稳定。");
        FlowFieldCrowdMovementSystem.PerturbEditorTestOnlyWorldBuildWorkingWorldFloatShadow();
        var floatShadow = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(floatShadow);
        Assert.AreEqual(baseline.Hash, floatShadow.Hash, "WorkingWorld 的 Unity float shadow 不得进入 authority digest。");

        FlowFieldCrowdMovementSystem.PerturbEditorTestOnlyWorldBuildWorkingWorldAuthorityCell();
        var authorityChanged = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(authorityChanged);
        Assert.AreNotEqual(floatShadow.Hash, authorityChanged.Hash, "WorkingWorld 的实际离散权威内容必须进入 authority digest。");
    }

    [Test]
    public void NavigationAuthorityDigest_RuntimeDirtyWorkingWorldTracksAuthorityButIgnoresFloatShadow()
    {
        const int width = 64;
        const int height = 32;
        var walkable = new bool[width * height];
        Array.Fill(walkable, true);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.RegisterCircleObstacle(81005, new Vector3(2.5f, 0f, 1.5f), 0.4f);
        for (int i = 0; i < 32; i++)
        {
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
            if (!FlowFieldCrowdMovementSystem.HasEditorTestOnlyRuntimeDirtyWorkingWorld())
                continue;
            if (!FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyProgressSignature()
                    .StartsWith("InitializeClone:RentWalkableMask:", StringComparison.Ordinal))
            {
                break;
            }
        }
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestOnlyRuntimeDirtyWorkingWorld());

        var baseline = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(baseline);
        var repeatedBaseline = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(repeatedBaseline);
        Assert.AreEqual(baseline.Hash, repeatedBaseline.Hash, "相同 RuntimeDirty WorkingWorld 的重复 authority digest 必须稳定。");
        FlowFieldCrowdMovementSystem.PerturbEditorTestOnlyRuntimeDirtyWorkingWorldFloatShadow();
        var floatShadow = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(floatShadow);
        Assert.AreEqual(baseline.Hash, floatShadow.Hash, "RuntimeDirty WorkingWorld 的 Unity float shadow 不得进入 authority digest。");

        FlowFieldCrowdMovementSystem.PerturbEditorTestOnlyRuntimeDirtyWorkingWorldAuthorityCell();
        var authorityChanged = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(authorityChanged);
        Assert.AreNotEqual(floatShadow.Hash, authorityChanged.Hash, "RuntimeDirty WorkingWorld 的实际离散权威内容必须进入 authority digest。");
    }

    [Test]
    public void NavigationAuthorityDigest_忽略Side诊断Shadow但保留碰撞权威状态()
    {
        bool[] walkable = new bool[6 * 4];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(6, 4, 1f, Vector3.zero, walkable);
        SimEntityContext entity = CreateEntity(new Vector3(1.5f, 0f, 1.5f), false, 0, 0.2f);

        var baseline = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(baseline);

        FlowFieldCrowdMovementSystem.SetAgentSide(entity.LogicEntityId.Value, SideType.EnemySide);
        var shadowChanged = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(shadowChanged);

        Assert.AreEqual(baseline.Hash, shadowChanged.Hash, "实体 Side 已由 Gameplay entity state 覆盖，Flow 诊断 shadow 不得重复污染 authority digest。");

        FlowFieldCrowdMovementSystem.SetAgentIgnoreCollision(entity.LogicEntityId.Value, true);
        var authorityChanged = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(authorityChanged);
        Assert.AreNotEqual(shadowChanged.Hash, authorityChanged.Hash, "fixed goal occupancy 使用的碰撞忽略状态必须进入 authority digest。");
    }

    [Test]
    public void NavigationAuthorityDigest_OrderedAgent索引损坏必须明确报错()
    {
        bool[] walkable = new bool[6 * 4];
        Array.Fill(walkable, true);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(6, 4, 1f, Vector3.zero, walkable);
        CreateEntity(new Vector3(1.5f, 0f, 1.5f));
        CreateEntity(new Vector3(2.5f, 0f, 1.5f));

        Assert.DoesNotThrow(() =>
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(new LogicStateHasher()));
        FlowFieldCrowdMovementSystem.PerturbEditorTestOnlyOrderedAgentIndex();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(new LogicStateHasher()));
        StringAssert.Contains("Navigation ordered-agent index mismatch", exception.Message);
    }

    [Test]
    public void NavigationAuthorityDigest_RuntimeDirty派生Sector索引损坏必须明确报错()
    {
        bool[] walkable = new bool[32 * 8];
        Array.Fill(walkable, true);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(32, 8, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.RegisterCircleObstacle(81006, new Vector3(15.5f, 0f, 3.5f), 1.25f);
        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());

        Assert.DoesNotThrow(() =>
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(new LogicStateHasher()));
        FlowFieldCrowdMovementSystem.PerturbEditorTestOnlyRuntimeDirtyDerivedSectorIndex();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(new LogicStateHasher()));
        StringAssert.Contains("runtime-dirty sectors derived index mismatch", exception.Message);
    }

    [Test]
    public void NavigationAuthorityDigest_PendingPortal缺少CellPayload必须明确报错()
    {
        bool[] walkable = new bool[32 * 8];
        Array.Fill(walkable, true);
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(32, 8, 1f, Vector3.zero, walkable);
        FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild());
        FlowFieldCrowdMovementSystem.AddEditorTestOnlyWorldBuildPortalProgressProbe();
        FlowFieldCrowdMovementSystem.InvalidateEditorTestOnlyWorldBuildPortalProgressProbeCells();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(new LogicStateHasher()));
        StringAssert.Contains("portal without cell payloads", exception.Message);
    }

    [Test]
    public void NavigationAuthorityDigest_注册Float半径不得回灌定点权威状态()
    {
        SimEntityContext entity = CreateEntity(new Vector3(1.5f, 0f, 1.5f), false, 0, 0.2f);
        var baselineHasher = new LogicStateHasher();
        LogicNavigationAuthorityDigest baseline =
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigestWithCheckpoints(baselineHasher);

        FlowFieldCrowdMovementSystem.UpdateAgentForEditorTest(entity, 0.9f, 0);
        var contaminatedHasher = new LogicStateHasher();
        LogicNavigationAuthorityDigest contaminated =
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigestWithCheckpoints(contaminatedHasher);

        Assert.AreEqual(
            baseline.AgentsHash,
            contaminated.AgentsHash,
            "仅供诊断的注册 float 半径不得改变 RadiusFixed 或 AgentsHash；权威半径必须只读取碰撞属性的 Fix64 值。");
    }

    [Test]
    public void RegisterAgent_碰撞半径非正时明确报错而非Fallback()
    {
        var entity = new SimEntityContext
        {
            Position = new Vector3(1.5f, 0f, 1.5f),
            Side = SideType.PlayerSide,
            Alive = true,
            MoveExecutor = new SimMoveExecutor { Position = new Vector3(1.5f, 0f, 1.5f) },
        };
        entity.SetProperty(CreatureMainProperty.CollisionRadius, Fix64.Zero);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => FlowFieldCrowdMovementSystem.RegisterAgentForEditorTest(entity, 0.5f, 0));

        StringAssert.Contains("ResolveCollisionRadiusFixed failed", exception.Message);
        StringAssert.Contains($"entity={entity.LogicEntityId.Value}", exception.Message);
        StringAssert.Contains("propertyRaw=0", exception.Message);
    }

    [Test]
    public void FixedGoalOccupancy_LegacyResolvedVelocityFrame不得改变Authority参与()
    {
        bool[] walkable = new bool[8 * 3];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(8, 3, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        SimEntityContext blocker = CreateEntity(new Vector3(1.5f, 0f, 1.5f), false, 0, 0.18f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(10, 10f / 30f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
            blocker,
            new FixVector2((Fix64)6.5f, (Fix64)1.5f),
            Fix64.One,
            out FixVector2 velocity));
        Assert.AreNotEqual(FixVector2.Zero, velocity);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(11, 11f / 30f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.GetEditorTestFixedGoalOccupancyParticipation(blocker.LogicEntityId.Value));
        var before = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(before);

        FlowFieldCrowdMovementSystem.SetEditorTestOnlyResolvedVelocityFrame(blocker.LogicEntityId.Value, 0);
        var after = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(after);

        Assert.AreEqual(before.Hash, after.Hash, "legacy ResolvedVelocityFrame 明确不属于 fixed authority digest。");
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.GetEditorTestFixedGoalOccupancyParticipation(blocker.LogicEntityId.Value),
            "同一 authority Hash 下，legacy frame shadow 不得改变 fixed 目标占位参与结果。");

        SimEntityContext requester = CreateEntity(new Vector3(0.5f, 0f, 0.5f), false, 0, 0.18f);
        Assert.IsFalse(
            FlowFieldCrowdMovementSystem.TryReserveNavigationGoalIfAvailableFixed(
                requester.LogicEntityId.Value,
                0,
                new FixVector2((Fix64)6.5f, (Fix64)1.5f),
                (Fix64)0.5f,
                out int blockingAgentId),
            "fixed 预约入口必须读取 blocker 的 fixed goal occupancy，而不是 legacy frame shadow。");
        Assert.AreEqual(blocker.LogicEntityId.Value, blockingAgentId);

        FlowFieldCrowdMovementSystem.SetEditorTestOnlyLastFixedFlowFrame(blocker.LogicEntityId.Value, 2);
        var staleFrame = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(staleFrame);

        Assert.AreNotEqual(before.Hash, staleFrame.Hash, "fixed LastFixedFlowFrame 必须进入 authority digest。");
        Assert.IsFalse(
            FlowFieldCrowdMovementSystem.GetEditorTestFixedGoalOccupancyParticipation(blocker.LogicEntityId.Value),
            "fixed flow 结果超过 8 Tick 后不得继续占用目标格。");

        FlowFieldCrowdMovementSystem.SetEditorTestOnlyLastFixedFlowFrame(blocker.LogicEntityId.Value, 11);
        var currentFrame = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(currentFrame);

        Assert.AreNotEqual(staleFrame.Hash, currentFrame.Hash, "fixed LastFixedFlowFrame 的恢复也必须反映到 authority digest。");
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.GetEditorTestFixedGoalOccupancyParticipation(blocker.LogicEntityId.Value),
            "fixed flow 结果在 8 Tick 窗口内必须继续占用目标格。");
    }

    [Test]
    public void NavigationAuthorityDigest_失败路径记忆必须进入AgentsHash()
    {
        SimEntityContext entity = CreateEntity(new Vector3(1.5f, 0f, 1.5f), false, 0, 0.2f);
        var baselineHasher = new LogicStateHasher();
        LogicNavigationAuthorityDigest baseline =
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigestWithCheckpoints(baselineHasher);

        FlowFieldCrowdMovementSystem.SetEditorTestOnlyFailedPathMemo(
            entity.LogicEntityId.Value,
            worldVersion: 17,
            startCellIndex: 23,
            goalCellIndex: 41,
            startSectorDirtyVersion: 5,
            goalSectorDirtyVersion: 7);
        var changedHasher = new LogicStateHasher();
        LogicNavigationAuthorityDigest changed =
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigestWithCheckpoints(changedHasher);

        Assert.AreNotEqual(
            baseline.AgentsHash,
            changed.AgentsHash,
            "失败路径记忆会跳过后续 BuildPathHandle 并改变缓存访问，必须属于跨 Tick Navigation authority。");
    }

    [Test]
    public void NavigationAuthorityDigest_Portal穿越槽必须进入AgentsHash()
    {
        SimEntityContext entity = CreateEntity(new Vector3(1.5f, 0f, 1.5f), false, 0, 0.2f);
        var baselineHasher = new LogicStateHasher();
        LogicNavigationAuthorityDigest baseline =
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigestWithCheckpoints(baselineHasher);

        FlowFieldCrowdMovementSystem.SetEditorTestOnlyPortalTraversalState(
            entity.LogicEntityId.Value,
            worldVersion: 3,
            sectorId: 17,
            portalId: 29,
            slotIndex: 2);
        var changedHasher = new LogicStateHasher();
        LogicNavigationAuthorityDigest changed =
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigestWithCheckpoints(changedHasher);

        Assert.AreNotEqual(
            baseline.AgentsHash,
            changed.AgentsHash,
            "Portal 穿越槽会改变后续流场方向选择，必须属于跨 Tick Navigation authority。");

        FlowFieldCrowdMovementSystem.SetEditorTestOnlyPortalTraversalState(
            entity.LogicEntityId.Value,
            worldVersion: 3,
            sectorId: 17,
            portalId: 29,
            slotIndex: 2,
            hasCommittedTileSlot: false);
        var provisionalHasher = new LogicStateHasher();
        LogicNavigationAuthorityDigest provisional =
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigestWithCheckpoints(provisionalHasher);
        Assert.AreNotEqual(
            changed.AgentsHash,
            provisional.AgentsHash,
            "Portal 临时 access 槽与 continuation tile 已提交槽会改变后续升级行为，必须进入 authority digest。");
    }

    [Test]
    public void Portal穿越槽在同一Portal内保持且切换Portal后才采用新推荐槽()
    {
        SimEntityContext entity = CreateEntity(new Vector3(1.5f, 0f, 1.5f), false, 0, 0.2f);
        FlowFieldCrowdMovementSystem.SetEditorTestOnlyPortalTraversalState(
            entity.LogicEntityId.Value,
            worldVersion: 3,
            sectorId: 17,
            portalId: 29,
            slotIndex: 2);

        int samePortalSlot = FlowFieldCrowdMovementSystem.ResolveEditorTestOnlyStablePortalTraversalSlotIndex(
            entity.LogicEntityId.Value,
            worldVersion: 3,
            sectorId: 17,
            portalId: 29,
            recommendedSlotIndex: 1,
            slotCount: 6);
        Assert.AreEqual(2, samePortalSlot, "单位进入同一宽 Portal 的相邻边界格时，不得把稳定穿越槽改成脚下槽。");

        int nextPortalSlot = FlowFieldCrowdMovementSystem.ResolveEditorTestOnlyStablePortalTraversalSlotIndex(
            entity.LogicEntityId.Value,
            worldVersion: 3,
            sectorId: 18,
            portalId: 30,
            recommendedSlotIndex: 1,
            slotCount: 6);
        Assert.AreEqual(1, nextPortalSlot, "进入新的 Sector/Portal 后必须采用新 Portal 的推荐穿越槽。");
    }

    [Test]
    public void Portal临时Access槽只允许被完整ContinuationTile升级一次()
    {
        SimEntityContext entity = CreateEntity(new Vector3(1.5f, 0f, 1.5f), false, 0, 0.2f);
        FlowFieldCrowdMovementSystem.SetEditorTestOnlyPortalTraversalState(
            entity.LogicEntityId.Value,
            worldVersion: 3,
            sectorId: 17,
            portalId: 29,
            slotIndex: 2,
            hasCommittedTileSlot: false);

        int repeatedPendingSlot = FlowFieldCrowdMovementSystem.ResolveEditorTestOnlyStablePortalTraversalSlotIndex(
            entity.LogicEntityId.Value,
            worldVersion: 3,
            sectorId: 17,
            portalId: 29,
            recommendedSlotIndex: 3,
            slotCount: 6,
            recommendationFromCommittedTile: false);
        Assert.AreEqual(2, repeatedPendingSlot, "同一个 Portal 的临时 access 场不得逐 Tick 改槽。");

        int promotedSlot = FlowFieldCrowdMovementSystem.ResolveEditorTestOnlyStablePortalTraversalSlotIndex(
            entity.LogicEntityId.Value,
            worldVersion: 3,
            sectorId: 17,
            portalId: 29,
            recommendedSlotIndex: 0,
            slotCount: 6,
            recommendationFromCommittedTile: true);
        Assert.AreEqual(0, promotedSlot, "完整 continuation tile 必须能把临时 access 槽升级为下游最优槽。");

        int committedSlot = FlowFieldCrowdMovementSystem.ResolveEditorTestOnlyStablePortalTraversalSlotIndex(
            entity.LogicEntityId.Value,
            worldVersion: 3,
            sectorId: 17,
            portalId: 29,
            recommendedSlotIndex: 1,
            slotCount: 6,
            recommendationFromCommittedTile: true);
        Assert.AreEqual(0, committedSlot, "完整 tile 提交后，同一 Portal 内必须保持已提交槽。");
    }

    [Test]
    public void Portal穿越目标只推进法向且保持连续切向坐标()
    {
        FixVector2 position = new FixVector2(Fix64.FromRaw(10123), Fix64.FromRaw(20877));
        FixVector2 oppositeCellCenter = new FixVector2(Fix64.FromRaw(12288), Fix64.FromRaw(22528));

        FixVector2 verticalTarget = FlowFieldCrowdMovementSystem.ResolveEditorTestOnlyPortalCrossingTargetFixed(
            position,
            oppositeCellCenter,
            isVerticalBoundary: true);
        Assert.AreEqual(oppositeCellCenter.x.RawValue, verticalTarget.x.RawValue);
        Assert.AreEqual(position.y.RawValue, verticalTarget.y.RawValue, "竖直 Portal 穿越不得吸向格中心并改变切向 Y。");

        FixVector2 horizontalTarget = FlowFieldCrowdMovementSystem.ResolveEditorTestOnlyPortalCrossingTargetFixed(
            position,
            oppositeCellCenter,
            isVerticalBoundary: false);
        Assert.AreEqual(position.x.RawValue, horizontalTarget.x.RawValue, "水平 Portal 穿越不得吸向格中心并改变切向 X。");
        Assert.AreEqual(oppositeCellCenter.y.RawValue, horizontalTarget.y.RawValue);
    }

    [Test]
    public void Portal接近目标在槽带内保持切向且只在超出时夹到边界()
    {
        bool[] walkable = new bool[4 * 3];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(4, 3, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        Vector2Int selectedCell = new Vector2Int(1, 1);
        FixVector2 oppositeCellCenter = new FixVector2((Fix64)2.5f, (Fix64)1.5f);
        FixVector2 alignedPosition = new FixVector2((Fix64)0.25f, (Fix64)1.8f);
        FixVector2 alignedTarget = FlowFieldCrowdMovementSystem.ResolveEditorTestOnlyPortalApproachTargetFixed(
            alignedPosition,
            selectedCell,
            oppositeCellCenter,
            isVerticalBoundary: true);
        Assert.AreEqual(oppositeCellCenter.x.RawValue, alignedTarget.x.RawValue);
        Assert.AreEqual(alignedPosition.y.RawValue, alignedTarget.y.RawValue,
            "单位尚未站上 Portal 格但已与槽带对齐时，不得先吸向格中心再于穿越后拉回。");

        FixVector2 outsidePosition = new FixVector2((Fix64)0.25f, (Fix64)2.25f);
        FixVector2 clampedTarget = FlowFieldCrowdMovementSystem.ResolveEditorTestOnlyPortalApproachTargetFixed(
            outsidePosition,
            selectedCell,
            oppositeCellCenter,
            isVerticalBoundary: true);
        Assert.AreEqual(((Fix64)2f).RawValue, clampedTarget.y.RawValue,
            "超出选定 Portal 槽带时必须朝最近边界收敛，不能无条件保持错误切向。");
    }

    [Test]
    public void 共享边界Portal交接只折叠零长度中间段()
    {
        Vector2Int[] nextCurrentCells =
        {
            new Vector2Int(336, 490),
            new Vector2Int(336, 491),
            new Vector2Int(336, 492)
        };
        Vector2Int[] nextOppositeCells =
        {
            new Vector2Int(336, 489),
            new Vector2Int(336, 490),
            new Vector2Int(336, 491)
        };

        int sharedSlot = FlowFieldCrowdMovementSystem.ResolveEditorTestOnlySharedBoundaryPortalHandoffSlotIndex(
            new Vector2Int(336, 492),
            nextCurrentCells,
            nextOppositeCells,
            out Vector2Int sharedTarget);
        Assert.AreEqual(2, sharedSlot);
        Assert.AreEqual(new Vector2Int(336, 491), sharedTarget, "当前 Portal 对侧格同时属于下一 Portal 时，应直接穿越下一条边界。");

        int separatedSlot = FlowFieldCrowdMovementSystem.ResolveEditorTestOnlySharedBoundaryPortalHandoffSlotIndex(
            new Vector2Int(335, 492),
            nextCurrentCells,
            nextOppositeCells,
            out Vector2Int separatedTarget);
        Assert.AreEqual(-1, separatedSlot, "普通 Portal 间仍须沿 continuation field 行进，不能做通用 string-pull。");
        Assert.AreEqual(default(Vector2Int), separatedTarget);
    }

    [Test]
    public void 最终目标直达判定使用实际定点坐标而不是格中心射线()
    {
        const int width = 3;
        const int height = 2;
        bool[] walkable =
        {
            true, false, true,
            true, true, true
        };
        byte[] costField = { 1, 1, 1, 1, 1, 1 };
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            0,
            width,
            height,
            1f,
            Vector3.zero,
            walkable,
            null,
            costField);

        Assert.IsFalse(
            FlowFieldCrowdMovementSystem.HasEditorTestOnlyCellCenterGridLineOfSight(0, 0, 2, 1),
            "格中心射线会穿过 (1,0)，该离散结果不能代表单位的实际连续位置。");
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.HasEditorTestOnlyFixedGridLineOfSight(
                new FixVector2((Fix64)0.5f, (Fix64)0.9f),
                new FixVector2((Fix64)2.5f, (Fix64)1.9f)),
            "实际线段从 (0,0) 上缘进入 (1,1)，不应被吸附后的格中心射线误判为遮挡。");
    }

    [Test]
    public void 最终目标直达把墙距Cost视为偏好而不是硬遮挡()
    {
        const int width = 3;
        const int height = 1;
        bool[] walkable = { true, true, true };
        byte[] costField = { 1, 2, 1 };
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            0,
            width,
            height,
            1f,
            Vector3.zero,
            walkable,
            null,
            costField);

        Assert.IsFalse(
            FlowFieldCrowdMovementSystem.HasEditorTestOnlyCellCenterGridLineOfSight(0, 0, 2, 0),
            "严格 cost LOS 仍应保留原有的 cost=1 契约。");
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.HasEditorTestOnlyFixedGridLineOfSight(
                new FixVector2((Fix64)0.5f, (Fix64)0.5f),
                new FixVector2((Fix64)2.5f, (Fix64)0.5f)),
            "墙距 blur 只影响 flow 偏好；直达线的真实半径安全性由 swept-circle 另行验证。");
    }

    [Test]
    public void FixedGridLos_AuthoredPointZeroNineLongReverseRayTerminatesAtGoalCell()
    {
        const int width = 500;
        const int height = 100;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            0,
            width,
            height,
            0.09f,
            new Vector3(10.08f, 0f, 41.04f),
            walkable,
            null,
            null);
        ProcessWorldBuildQueueUntilReady();

        FixVector2 from = new FixVector2(Fix64.FromRaw(217404), Fix64.FromRaw(184984));
        FixVector2 to = new FixVector2(Fix64.FromRaw(193573), Fix64.FromRaw(188745));
        string diagnostic = FlowFieldCrowdMovementSystem.GetEditorTestOnlyFixedGridLineOfSightDiagnostic(from, to);
        Assert.DoesNotThrow(() =>
            Assert.IsTrue(
                FlowFieldCrowdMovementSystem.HasEditorTestOnlyFixedGridLineOfSight(from, to),
                $"The captured LvTest reverse ray crosses only walkable cells and must terminate at its authored goal cell. diagnostic={diagnostic}"));
    }

    [Test]
    public void FinalGoal直达时允许目标格SoftCost()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 4;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(
            9911,
            new Vector3(2.5f, 0f, 1.5f),
            new Vector3(0.49f, 0f, 0.49f),
            20);
        ProcessWorldBuildQueueUntilReady();

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 1f / 30f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
            ctx,
            new FixVector2((Fix64)2.5f, (Fix64)1.5f),
            Fix64.One,
            out FixVector2 velocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            ctx.LogicEntityId.Value,
            out string diagnostic));

        Assert.AreEqual(Fix64.One.RawValue, velocity.x.RawValue);
        Assert.AreEqual(0L, velocity.y.RawValue);
        StringAssert.Contains("/lastResult=direct-static-clear", diagnostic,
            "FinalGoal 直达检测应允许终点格的 soft cost。");
    }

    [Test]
    public void Portal到Portal的TileKey只依赖下一Portal而不依赖远端最终格()
    {
        const int width = 32;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        int[] sectorIds = { 1, 2, 3, 4 };
        int[] portalIds = { 10, 20, 30 };
        FlowFieldCrowdMovementSystem.ResolveEditorTestOnlyFlowTileGoalDependencies(
            sectorIds,
            portalIds,
            sectorPathIndex: 0,
            goalX: 17,
            goalY: 1,
            out int firstDownstream,
            out int firstFinalDependency);
        FlowFieldCrowdMovementSystem.ResolveEditorTestOnlyFlowTileGoalDependencies(
            sectorIds,
            portalIds,
            sectorPathIndex: 0,
            goalX: 30,
            goalY: 2,
            out int movedDownstream,
            out int movedFinalDependency);

        Assert.AreEqual(20, firstDownstream, "远端 Portal tile 的 continuation 依赖必须是紧邻的下一 Portal。");
        Assert.AreEqual(firstDownstream, movedDownstream);
        Assert.AreEqual(0, firstFinalDependency);
        Assert.AreEqual(firstFinalDependency, movedFinalDependency, "远端最终格变化不得使 portal-to-portal tile cache 失效。");

        FlowFieldCrowdMovementSystem.ResolveEditorTestOnlyFlowTileGoalDependencies(
            sectorIds,
            portalIds,
            sectorPathIndex: 2,
            goalX: 17,
            goalY: 1,
            out int adjacentDownstream,
            out int adjacentFinalDependency);
        Assert.AreEqual(~(17 + width), adjacentDownstream, "末端相邻 Portal 必须显式依赖精确最终格。");
        Assert.AreEqual(17 + width, adjacentFinalDependency);
    }

    [Test]
    public void CharacterMoveComp_手动移动通过定点执行器且不丢Raw()
    {
        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 0.5f));
        var brain = new ScriptedBrain
        {
            MoveFixed = new FixVector2(Fix64.One, Fix64.Zero),
        };
        ctx.Brain = brain;
        CharacterMoveComp moveComp = new CharacterMoveComp();
        moveComp.Init(ctx);
        ctx.MoveComp = moveComp;

        moveComp.Move((Fix64)0.0333f);

        SimMoveExecutor executor = (SimMoveExecutor)ctx.MoveExecutor;
        Fix64 expectedSpeed = DistanceUnitConverter.ConvertToWorld(ctx.GetProperty(CreatureMainProperty.Speed));
        Assert.IsTrue(executor.HasFixedInput, "正式移动链必须调用 SetInputFixed。 ");
        Assert.AreEqual(expectedSpeed.RawValue, executor.LastFixedInput.x.RawValue);
        Assert.AreEqual(0L, executor.LastFixedInput.y.RawValue);
    }

    [Test]
    public void Fixed导航目标不得经Vector3桥接改变DeterministicState()
    {
        Fix64 preciseCoordinate = Fix64.FromRaw(((Fix64)16777216).RawValue + 1);
        var preciseTarget = new FixVector2(preciseCoordinate, -preciseCoordinate);
        var expected = new CharacterMoveComp();
        expected.Init(null);
        expected.MoveToFixed(preciseTarget);
        var expectedHasher = new LogicStateHasher();
        expected.WriteDeterministicState(expectedHasher);

        var actual = new CharacterMoveComp();
        actual.Init(null);
        actual.SetNavTargetFixed(preciseTarget);
        var actualHasher = new LogicStateHasher();
        actual.WriteDeterministicState(actualHasher);

        Assert.AreEqual(
            expectedHasher.Hash,
            actualHasher.Hash,
            "fixed queue slot 写入导航目标时不得经 float 改变 raw。");
    }

    [Test]
    public void Fixed合法导航点保留格内SubCellRaw()
    {
        bool[] walkable = new bool[4 * 3];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(4, 3, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        FixVector2 candidate = new FixVector2(
            Fix64.FromRaw(((Fix64)1.5f).RawValue + 1),
            Fix64.FromRaw(((Fix64)1.5f).RawValue + 3));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPointFixed(
            candidate,
            0,
            Fix64.One,
            Fix64.Zero,
            out FixVector2 legalPoint));
        Assert.AreEqual(candidate.x.RawValue, legalPoint.x.RawValue);
        Assert.AreEqual(candidate.y.RawValue, legalPoint.y.RawValue);
    }

    [Test]
    public void FixedSteering_相同状态Raw一致且不超过最大速度()
    {
        const int width = 7;
        bool[] walkable = new bool[width];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, 1, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 0.5f));
        FixVector2 goal = new FixVector2((Fix64)6.5f, (Fix64)0.5f);
        Fix64 maxSpeed = Fix64.FromRaw(7331);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(ctx, goal, maxSpeed, out FixVector2 pending));
        Assert.AreNotEqual(0L, pending.x.RawValue);
        ProcessFlowTileBuildQueueUntilTileCount(1);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(ctx, goal, maxSpeed, out FixVector2 first));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(ctx, goal, maxSpeed, out FixVector2 second));

        Assert.AreEqual(first.x.RawValue, second.x.RawValue);
        Assert.AreEqual(first.y.RawValue, second.y.RawValue);
        Assert.AreEqual(pending.x.RawValue, first.x.RawValue);
        Assert.AreEqual(pending.y.RawValue, first.y.RawValue);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringDiagnostic(
            ctx.LogicEntityId.Value,
            out Vector3 desiredVelocity,
            out _,
            out _,
            out _));
        Assert.AreEqual(((Fix64)desiredVelocity.x).RawValue, first.x.RawValue);
        Assert.AreEqual(((Fix64)desiredVelocity.z).RawValue, first.y.RawValue);
        Assert.LessOrEqual(
            FixVector2.SqrMagnitude(first).RawValue,
            (maxSpeed * maxSpeed).RawValue);
    }

    [Test]
    public void FixedSteering_高移速跨细网格绕障时方向不能逐帧急转()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 12;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(0, 0.54f);

        const int width = 160;
        const int height = 100;
        const float cellSize = 0.09f;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        for (int y = 35; y <= 64; y++)
        {
            for (int x = 65; x <= 84; x++)
                walkable[x + y * width] = false;
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            width,
            height,
            cellSize,
            Vector3.zero,
            walkable);
        ProcessWorldBuildQueueUntilReady();

        Vector3 start = new Vector3(120.5f * cellSize, 0f, 30.5f * cellSize);
        Vector3 goal = new Vector3(25.5f * cellSize, 0f, 60.5f * cellSize);
        SimEntityContext soldier = CreateEntity(start, false, 0, 0.54f);
        const float speed = 5.82f;
        const float deltaTime = 1f / 30f;
        Vector3 previousDirection = Vector3.zero;
        Vector3 routeDirection = (goal - start).normalized;
        int sharpTurnCount = 0;
        int lateralFlipCount = 0;
        int previousLateralSign = 0;
        int previousSectorPathIndex = -1;
        int previousPortalId = -1;
        float minimumDirectionDot = 1f;
        System.Text.StringBuilder timeline = new System.Text.StringBuilder(4096);

        for (int frame = 1; frame <= 120 && Vector3.Distance(soldier.Position, goal) > 0.8f; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * deltaTime);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(
                soldier,
                goal,
                speed,
                out Vector3 velocity));
            Assert.Greater(velocity.sqrMagnitude, 0.01f, $"返航绕障不应停住。frame={frame}, pos={soldier.Position}");
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCurrentPathSegment(
                soldier.LogicEntityId.Value,
                out int sectorPathIndex,
                out int portalId,
                out bool isOnPortalCell));

            Vector3 direction = velocity.normalized;
            bool samePathSegment = sectorPathIndex == previousSectorPathIndex && portalId == previousPortalId;
            if (!samePathSegment)
            {
                previousDirection = Vector3.zero;
                previousLateralSign = 0;
            }
            float lateral = routeDirection.x * direction.z - routeDirection.z * direction.x;
            int lateralSign = lateral > 0.15f ? 1 : lateral < -0.15f ? -1 : 0;
            if (!isOnPortalCell && lateralSign != 0)
            {
                if (previousLateralSign != 0 && lateralSign != previousLateralSign)
                {
                    lateralFlipCount++;
                    timeline.Append("lateralFlip frame=").Append(frame)
                        .Append(" pos=").Append(soldier.Position)
                        .Append(" direction=").Append(direction)
                        .Append(" lateral=").Append(lateral.ToString("F3"));
                    if (FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
                            soldier.LogicEntityId.Value,
                            out string lateralDiagnostic))
                    {
                        timeline.Append(" flow={").Append(lateralDiagnostic).Append('}');
                    }
                    timeline.AppendLine();
                }
                previousLateralSign = lateralSign;
            }
            if (!isOnPortalCell && previousDirection.sqrMagnitude > 0.01f)
            {
                float dot = Vector3.Dot(previousDirection, direction);
                minimumDirectionDot = Mathf.Min(minimumDirectionDot, dot);
                if (dot < 0.5f)
                {
                    sharpTurnCount++;
                    timeline.Append("frame=").Append(frame)
                        .Append(" pos=").Append(soldier.Position)
                        .Append(" previous=").Append(previousDirection)
                        .Append(" current=").Append(direction)
                        .Append(" dot=").Append(dot.ToString("F3"));
                    if (FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
                            soldier.LogicEntityId.Value,
                            out string diagnostic))
                    {
                        timeline.Append(" flow={").Append(diagnostic).Append('}');
                    }
                    timeline.AppendLine();
                }
            }

            previousDirection = direction;
            previousSectorPathIndex = sectorPathIndex;
            previousPortalId = portalId;
            soldier.Position += velocity * deltaTime;
            soldier.SyncPositionToExecutor();
        }

        Assert.LessOrEqual(
            sharpTurnCount,
            0,
            $"高移速返航在同一流场段内不能逐帧急转。sharpTurns={sharpTurnCount}, minDot={minimumDirectionDot:F3}\n{timeline}");
        Assert.LessOrEqual(
            lateralFlipCount,
            1,
            $"高移速返航在同一流场段内不能持续左右换侧。lateralFlips={lateralFlipCount}\n{timeline}");
        Assert.LessOrEqual(
            Vector3.Distance(soldier.Position, goal),
            0.8f,
            $"方向场修正后仍必须完成绕障返航。pos={soldier.Position}, goal={goal}\n{timeline}");
    }

    [Test]
    public void FixedSteering_窄Portal按Tick持有方向并在出清后换向()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = new bool[8];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(8, 1, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        SimEntityContext left = CreateEntity(new Vector3(2.5f, 0f, 0.5f), false, 0, 0.18f);
        SimEntityContext right = CreateEntity(new Vector3(5.5f, 0f, 0.5f), false, 0, 0.18f);
        FixVector2 rightGoal = new FixVector2((Fix64)7.5f, (Fix64)0.5f);
        FixVector2 leftGoal = new FixVector2((Fix64)0.5f, (Fix64)0.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 1f / 30f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(left, rightGoal, Fix64.One, out FixVector2 leftVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(right, leftGoal, Fix64.One, out FixVector2 rightVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(left.LogicEntityId.Value, out int[] portalIds));
        Assert.AreEqual(1, portalIds.Length, "测试路径必须穿过一个窄 portal。");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedPortalOwner(portalIds[0], out int firstOwner, out int ownerSinceFrame));
        Assert.AreEqual(1, firstOwner, "同 Tick 双向到达时应由较小 LogicEntityId 的方向取得所有权。");
        Assert.AreEqual(1, ownerSinceFrame);
        Assert.Greater(leftVelocity.x.RawValue, 0L);
        Assert.AreEqual(FixVector2.Zero, rightVelocity, "对向单位在窄门影响范围内必须等待。");

        left.Position = new Vector3(7.5f, 0f, 0.5f);
        bool rightRecovered = false;
        for (int frame = 2; frame <= 6; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame / 30f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(left, rightGoal, Fix64.One, out _));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(right, leftGoal, Fix64.One, out FixVector2 candidate));
            rightRecovered |= candidate.x < Fix64.Zero;
        }

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedPortalOwner(portalIds[0], out int secondOwner, out int switchedFrame));
        Assert.AreEqual(-1, secondOwner, "原方向离开且最小持有 Tick 到达后必须切给等待方向。");
        Assert.GreaterOrEqual(switchedFrame, 4);
        Assert.IsTrue(rightRecovered, "等待方向取得所有权后必须恢复 fixed velocity。");
    }

    [Test]
    public void 多AgentType世界顺序不依赖AuthoredSource注册顺序()
    {
        const int lowerAgentType = 101;
        const int higherAgentType = 202;
        bool[] walkable = { true };
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(lowerAgentType, 0.18f);
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(higherAgentType, 0.18f);
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSources(new[]
        {
            new AuthoredNavigationSourceData(higherAgentType, 1, 1, 1f, Vector3.zero, walkable, null),
            new AuthoredNavigationSourceData(lowerAgentType, 1, 1, 1f, Vector3.zero, walkable, null),
        });

        MethodInfo collectMethod = typeof(FlowFieldCrowdMovementSystem).GetMethod(
            "CollectNavigationWorldAgentTypes",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(collectMethod);
        var orderedTypes = (List<int>)collectMethod.Invoke(null, null);
        CollectionAssert.AreEqual(new[] { lowerAgentType, higherAgentType }, orderedTypes);

        FieldInfo defaultSourceField = typeof(FlowFieldCrowdMovementSystem).GetField(
            "_testTerrainOverride",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(defaultSourceField);
        object defaultSource = defaultSourceField.GetValue(null);
        Assert.NotNull(defaultSource);
        FieldInfo agentTypeField = defaultSource.GetType().GetField(
            "AgentTypeId",
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(agentTypeField);
        Assert.AreEqual(lowerAgentType, (int)agentTypeField.GetValue(defaultSource));
    }

    [Test]
    public void FixedSteering_多AgentType相同PortalId隔离方向所有权()
    {
        const int firstAgentType = 101;
        const int secondAgentType = 202;
        bool[] walkable = new bool[8];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(firstAgentType, 0.18f);
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(secondAgentType, 0.18f);
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSources(new[]
        {
            new AuthoredNavigationSourceData(firstAgentType, 8, 1, 1f, Vector3.zero, walkable, null),
            new AuthoredNavigationSourceData(secondAgentType, 8, 1, 1f, Vector3.zero, walkable, null),
        });

        SimEntityContext first = CreateEntity(new Vector3(2.5f, 0f, 0.5f), false, firstAgentType, 0.18f);
        SimEntityContext second = CreateEntity(new Vector3(5.5f, 0f, 0.5f), false, secondAgentType, 0.18f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 1f / 30f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
            first,
            new FixVector2((Fix64)7.5f, (Fix64)0.5f),
            Fix64.One,
            out FixVector2 firstVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
            second,
            new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            Fix64.One,
            out FixVector2 secondVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(first.LogicEntityId.Value, out int[] firstPortalIds));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(second.LogicEntityId.Value, out int[] secondPortalIds));
        Assert.AreEqual(firstPortalIds[0], secondPortalIds[0], "测试前提要求两个 world 生成相同本地 portalId。");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedPortalOwner(
            firstAgentType,
            firstPortalIds[0],
            out int firstOwner,
            out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedPortalOwner(
            secondAgentType,
            secondPortalIds[0],
            out int secondOwner,
            out _));
        Assert.AreEqual(1, firstOwner);
        Assert.AreEqual(-1, secondOwner);
        Assert.Greater(firstVelocity.x.RawValue, 0L);
        Assert.Less(secondVelocity.x.RawValue, 0L);
    }

    [Test]
    public void Fixed净空查询按显式AgentType选择CommittedWorld()
    {
        const int firstAgentType = 301;
        const int secondAgentType = 302;
        bool[] firstWalkable = { true, true, true };
        bool[] secondWalkable = { true, false, true };
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(firstAgentType, 0.18f);
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(secondAgentType, 0.18f);
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSources(new[]
        {
            new AuthoredNavigationSourceData(firstAgentType, 3, 1, 1f, Vector3.zero, firstWalkable, null),
            new AuthoredNavigationSourceData(secondAgentType, 3, 1, 1f, Vector3.zero, secondWalkable, null),
        });

        FixVector2 point = new FixVector2((Fix64)1.5f, (Fix64)0.5f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetNavigationPointClearanceFixed(
            point,
            firstAgentType,
            Fix64.Zero,
            out bool firstClear,
            out _,
            out _,
            out _));
        Assert.IsTrue(firstClear);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetNavigationPointClearanceFixed(
            point,
            secondAgentType,
            Fix64.Zero,
            out bool secondClear,
            out _,
            out _,
            out _));
        Assert.IsFalse(secondClear);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetNavigationPointClearanceFixed(
            point,
            firstAgentType,
            Fix64.Zero,
            out bool firstClearAgain,
            out _,
            out _,
            out _));
        Assert.IsTrue(firstClearAgain, "切换 active world 后显式 firstAgentType 查询仍必须读取 first committed world。");
    }

    [Test]
    public void NavigationAuthorityDigest_FixedPortalOwnerStateMustAffectHash()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = new bool[8];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(8, 1, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        SimEntityContext entity = CreateEntity(new Vector3(2.5f, 0f, 0.5f), false, 0, 0.18f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 1f / 30f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
            entity,
            new FixVector2((Fix64)7.5f, (Fix64)0.5f),
            Fix64.One,
            out _));
        var withOwner = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(withOwner);

        FlowFieldCrowdMovementSystem.ClearEditorTestFixedPortalOwners();
        var withoutOwner = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(withoutOwner);

        Assert.AreNotEqual(withOwner.Hash, withoutOwner.Hash, "fixed portal owner 会改变下一 Tick 对向速度，必须进入 authority digest。");
    }

    [Test]
    public void FixedSteering_单Sector直走廊按Tick持有并在出清后换向()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 16;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = { true, true, true, true, true, true, true, true };
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(8, 1, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestPortalCount(), "测试走廊必须完全位于单一 sector 内。");

        SimEntityContext left = CreateEntity(new Vector3(2.5f, 0f, 0.5f), false, 0, 0.18f);
        SimEntityContext right = CreateEntity(new Vector3(5.5f, 0f, 0.5f), false, 0, 0.18f);
        FixVector2 rightGoal = new FixVector2((Fix64)7.5f, (Fix64)0.5f);
        FixVector2 leftGoal = new FixVector2((Fix64)0.5f, (Fix64)0.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 1f / 30f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(left, rightGoal, Fix64.One, out FixVector2 leftVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(right, leftGoal, Fix64.One, out FixVector2 rightVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedCorridorDescriptor(
            3,
            0,
            out int corridorId,
            out _,
            out int[] endpointA,
            out int[] endpointB));
        Assert.AreEqual(0, corridorId);
        Assert.AreEqual(0, endpointA[0]);
        Assert.AreEqual(7, endpointB[0]);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedCorridorOwner(corridorId, out int firstOwner, out int ownerSinceFrame));
        Assert.AreEqual(1, firstOwner);
        Assert.AreEqual(1, ownerSinceFrame);
        Assert.Greater(leftVelocity.x.RawValue, 0L);
        Assert.AreEqual(FixVector2.Zero, rightVelocity);

        FlowFieldCrowdMovementSystem.UnregisterAgent(left.LogicEntityId.Value);
        bool rightRecovered = false;
        for (int frame = 2; frame <= 6; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame / 30f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(right, leftGoal, Fix64.One, out FixVector2 candidate));
            rightRecovered |= candidate.x < Fix64.Zero;
        }

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedCorridorOwner(corridorId, out int secondOwner, out int switchedFrame));
        Assert.AreEqual(-1, secondOwner);
        Assert.GreaterOrEqual(switchedFrame, 4);
        Assert.IsTrue(rightRecovered);
    }

    [Test]
    public void FixedSteering_单SectorL形走廊两端共享同一StableOwner()
    {
        const int width = 6;
        const int height = 6;
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        var walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 0);
        for (int y = 0; y < height; y++)
            SetWalkable(walkable, width, width - 1, y);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        SimEntityContext fromA = CreateEntity(new Vector3(1.5f, 0f, 0.5f), false, 0, 0.18f);
        SimEntityContext fromB = CreateEntity(new Vector3(5.5f, 0f, 4.5f), false, 0, 0.18f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 1f / 30f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
            fromA,
            new FixVector2((Fix64)5.5f, (Fix64)5.5f),
            Fix64.One,
            out FixVector2 fromAVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
            fromB,
            new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            Fix64.One,
            out FixVector2 fromBVelocity));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedCorridorDescriptor(
            width - 1,
            0,
            out int corridorId,
            out int[] componentCells,
            out int[] endpointA,
            out int[] endpointB));
        Assert.AreEqual(0, corridorId);
        CollectionAssert.Contains(componentCells, width - 1, "L 形拐角格必须连接水平和垂直窄路种子。");
        Assert.AreEqual(0, endpointA[0]);
        Assert.AreEqual(width * height - 1, endpointB[0]);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedCorridorOwner(corridorId, out int owner, out _));
        Assert.AreEqual(1, owner);
        Assert.AreNotEqual(FixVector2.Zero, fromAVelocity);
        Assert.AreEqual(FixVector2.Zero, fromBVelocity, "L 形两端若没有共享 owner，对向单位会错误地同时进入拐角。");
    }

    [Test]
    public void FixedSteering_单SectorT形三出口不创建二向Owner()
    {
        const int width = 7;
        const int height = 5;
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        var walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 0);
        for (int y = 0; y < height; y++)
            SetWalkable(walkable, width, width / 2, y);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        SimEntityContext left = CreateEntity(new Vector3(1.5f, 0f, 0.5f), false, 0, 0.18f);
        SimEntityContext right = CreateEntity(new Vector3(5.5f, 0f, 0.5f), false, 0, 0.18f);
        Vector3 leftVelocity = ResolveDeterministicFlowVelocityAfterQueue(
            left,
            new Vector3(5.5f, 0f, 0.5f),
            1f,
            1,
            out _);
        Vector3 rightVelocity = ResolveDeterministicFlowVelocityAfterQueue(
            right,
            new Vector3(1.5f, 0f, 0.5f),
            1f,
            512,
            out _);

        Assert.IsFalse(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedCorridorDescriptor(
            1,
            0,
            out _,
            out _,
            out _,
            out _));
        Assert.Greater(leftVelocity.x, 0f);
        Assert.Less(rightVelocity.x, 0f);
    }

    [Test]
    public void NavigationAuthorityDigest_FixedCorridorOwnerStateMustAffectHash()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 16;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = { true, true, true, true, true, true, true, true };
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(8, 1, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        SimEntityContext entity = CreateEntity(new Vector3(2.5f, 0f, 0.5f), false, 0, 0.18f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 1f / 30f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
            entity,
            new FixVector2((Fix64)7.5f, (Fix64)0.5f),
            Fix64.One,
            out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedCorridorOwner(0, out _, out _));
        var withOwner = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(withOwner);

        FlowFieldCrowdMovementSystem.ClearEditorTestFixedPortalOwners();
        var withoutOwner = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(withoutOwner);

        Assert.AreNotEqual(withOwner.Hash, withoutOwner.Hash);
    }

    [Test]
    public void RuntimeDirty提交会失效对应World的FixedCorridorCache和Owner()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 16;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = { true, true, true, true, true, true, true, true };
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(8, 1, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        SimEntityContext entity = CreateEntity(new Vector3(2.5f, 0f, 0.5f), false, 0, 0.18f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 1f / 30f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
            entity,
            new FixVector2((Fix64)7.5f, (Fix64)0.5f),
            Fix64.One,
            out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestFixedCorridorLookup());
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedCorridorOwner(0, out _, out _));

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(
            9001,
            new Vector3(3.5f, 0f, 0.5f),
            new Vector3(0.49f, 0f, 0.49f));
        for (int i = 0; i < 512 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestFixedCorridorLookup());
        Assert.IsFalse(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedCorridorOwner(0, out _, out _));
    }

    [Test]
    public void FixedCorridor首次查询不得扫描整个大型World()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 16;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        const int width = 256;
        const int height = 256;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        long classifiedBefore = FlowFieldCrowdMovementSystem.GetEditorTestFixedCorridorClassifiedCellCount();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedCorridorDescriptor(
            width / 2,
            height / 2,
            out _,
            out _,
            out _,
            out _));
        stopwatch.Stop();
        long classified = FlowFieldCrowdMovementSystem.GetEditorTestFixedCorridorClassifiedCellCount() - classifiedBefore;
        TestContext.WriteLine($"fixed-corridor-first-query cells={classified}/{walkable.Length} elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F3}");

        Assert.Less(classified, 1024L, "首次局部 corridor 查询不得同步扫描整个 NavigationWorld。");
    }

    [Test]
    public void FixedCorridor超长组件首次查询不得同步展开整个组件()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 16384;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        const int width = 8192;
        bool[] walkable = new bool[width];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, 1, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.IsEditorTestFixedCorridorDescriptorPending(width / 2, 0));
        int stepCount = 0;
        while (FlowFieldCrowdMovementSystem.IsEditorTestFixedCorridorDescriptorPending(width / 2, 0))
        {
            long expandedBeforeStep = FlowFieldCrowdMovementSystem.GetEditorTestFixedCorridorExpandedCellCount();
            int operations = FlowFieldCrowdMovementSystem.ProcessEditorTestFixedCorridorBuildStep();
            long expandedThisStep = FlowFieldCrowdMovementSystem.GetEditorTestFixedCorridorExpandedCellCount() - expandedBeforeStep;
            Assert.AreEqual(operations, expandedThisStep);
            Assert.LessOrEqual(expandedThisStep, 256L, "每个逻辑步的 corridor component 展开必须受固定预算约束。");
            stepCount++;
            if (stepCount == 1)
            {
                Assert.IsTrue(
                    FlowFieldCrowdMovementSystem.TryValidateEditorTestFixedCorridorIncrementalAuthorityHashes(out string hashFailure),
                    hashFailure);
            }
            Assert.LessOrEqual(stepCount, width / 256 + 1, "长走廊增量解析未在预期逻辑步数内完成。");
        }
        stopwatch.Stop();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedCorridorDescriptor(
            width / 2,
            0,
            out _,
            out int[] componentCells,
            out _,
            out _));
        TestContext.WriteLine($"fixed-corridor-long-component steps={stepCount} cells={componentCells.Length}/{width} elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F3}");

        Assert.AreEqual(width, componentCells.Length, "测试前提失效：descriptor 必须覆盖完整长走廊组件。");
    }

    [Test]
    public void NavigationAuthorityDigest_FixedCorridor增量进度必须影响Hash()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 1024;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        const int width = 600;
        bool[] walkable = new bool[width];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, 1, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.IsEditorTestFixedCorridorDescriptorPending(width / 2, 0));
        var before = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(before);
        Assert.AreEqual(256, FlowFieldCrowdMovementSystem.ProcessEditorTestFixedCorridorBuildStep());
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryValidateEditorTestFixedCorridorIncrementalAuthorityHashes(out string hashFailure),
            hashFailure);
        var after = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(after);

        Assert.AreNotEqual(before.Hash, after.Hash, "未完成 corridor 派生会改变后续停速时长，必须进入 authority digest。");
    }

    [Test]
    public void NavigationAuthorityDigest_FixedCorridor未处理队列顺序必须影响Hash()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 512;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int size = 257;
        const int center = size / 2;
        bool[] walkable = new bool[size * size];
        for (int i = 0; i < size; i++)
        {
            SetWalkable(walkable, size, i, center);
            SetWalkable(walkable, size, center, i);
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(size, size, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.IsEditorTestFixedCorridorDescriptorPending(center, center));
        Assert.AreEqual(256, FlowFieldCrowdMovementSystem.ProcessEditorTestFixedCorridorBuildStep());
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryValidateEditorTestFixedCorridorIncrementalAuthorityHashes(out string hashFailure),
            hashFailure);

        var baseline = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(baseline);
        FlowFieldCrowdMovementSystem.PerturbEditorTestOnlyFixedCorridorUnprocessedQueueOrder();
        var reordered = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(reordered);

        Assert.AreNotEqual(
            baseline.Hash,
            reordered.Hash,
            "未处理队列即使元素集合不变，展开顺序仍会影响后续端点发现和溢出，必须进入 authority digest。");
    }

    [Test]
    public void FixedCorridor增量解析完成前权威速度保持为零()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 1024;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        const int width = 600;
        bool[] walkable = new bool[width];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, 1, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        SimEntityContext entity = CreateEntity(new Vector3(300.5f, 0f, 0.5f), false, 0, 0.18f);
        FixVector2 goal = new FixVector2((Fix64)599.5f, (Fix64)0.5f);

        for (int frame = 1; frame <= 3; frame++)
        {
            long expandedBefore = FlowFieldCrowdMovementSystem.GetEditorTestFixedCorridorExpandedCellCount();
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame / 30f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(entity, goal, Fix64.One, out FixVector2 velocity));
            long expanded = FlowFieldCrowdMovementSystem.GetEditorTestFixedCorridorExpandedCellCount() - expandedBefore;
            Assert.LessOrEqual(expanded, 256L, "同一逻辑帧不得突破 corridor component 派生预算。");
            if (frame <= 2)
                Assert.AreEqual(FixVector2.Zero, velocity, "descriptor 未完成时不能把 Pending 误当成无 corridor 而放行。");
            else
                Assert.Greater(velocity.x.RawValue, 0L, "descriptor 完成后 owner 方向应恢复通行。");
        }
    }

    [Test]
    public void FixedCorridor局部Cache_查询顺序不改变Descriptor且组件内复用()
    {
        const int width = 6;
        const int height = 6;
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 0);
        for (int y = 0; y < height; y++)
            SetWalkable(walkable, width, width - 1, y);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedCorridorDescriptor(
            width - 1,
            0,
            out int cornerFirstId,
            out int[] cornerFirstCells,
            out int[] cornerFirstEndpointA,
            out int[] cornerFirstEndpointB));
        long classifiedAfterFirstQuery = FlowFieldCrowdMovementSystem.GetEditorTestFixedCorridorClassifiedCellCount();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedCorridorDescriptor(
            0,
            0,
            out int cachedId,
            out int[] cachedCells,
            out int[] cachedEndpointA,
            out int[] cachedEndpointB));
        Assert.AreEqual(
            classifiedAfterFirstQuery,
            FlowFieldCrowdMovementSystem.GetEditorTestFixedCorridorClassifiedCellCount(),
            "同一 corridor 组件内二次查询必须直接复用 descriptor。");
        Assert.AreEqual(cornerFirstId, cachedId);
        CollectionAssert.AreEqual(cornerFirstCells, cachedCells);
        CollectionAssert.AreEqual(cornerFirstEndpointA, cachedEndpointA);
        CollectionAssert.AreEqual(cornerFirstEndpointB, cachedEndpointB);

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFixedCorridorDescriptor(
            0,
            0,
            out int endpointFirstId,
            out int[] endpointFirstCells,
            out int[] endpointFirstEndpointA,
            out int[] endpointFirstEndpointB));

        Assert.AreEqual(cornerFirstId, endpointFirstId);
        CollectionAssert.AreEqual(cornerFirstCells, endpointFirstCells);
        CollectionAssert.AreEqual(cornerFirstEndpointA, endpointFirstEndpointA);
        CollectionAssert.AreEqual(cornerFirstEndpointB, endpointFirstEndpointB);
    }

    [Test]
    public void FixedPrepare_大坐标目标落格不经过Float舍入()
    {
        const int worldOrigin = 16777216;
        bool[] walkable = { true, true, true, true };
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            walkable.Length,
            1,
            1f,
            new Vector3(worldOrigin, 0f, 0f),
            walkable);
        ProcessWorldBuildQueueUntilReady();

        SimEntityContext ctx = CreateEntity(new Vector3(worldOrigin, 0f, 0f));
        FixVector2 goal = new FixVector2((Fix64)worldOrigin + (Fix64)1.25f, (Fix64)0.5f);

        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryPrepareNavigationRequestFixed(ctx, goal, out string failureReason),
            failureReason);
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryGetEditorTestPathGoalCell(ctx.LogicEntityId.Value, out int goalX, out int goalY));
        Assert.AreEqual(1, goalX);
        Assert.AreEqual(0, goalY);
        Assert.AreNotEqual(
            goal.x.RawValue,
            ((Fix64)(float)goal.x).RawValue,
            "测试前提失效：目标必须发生 float 精度丢失。 ");
    }

    [Test]
    public void FixedSteering_同Tick同目标使用稳定预约分流()
    {
        const int width = 8;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        SimEntityContext first = CreateEntity(new Vector3(1.5f, 0f, 1.5f));
        SimEntityContext second = CreateEntity(new Vector3(1.5f, 0f, 2.5f));
        FixVector2 sharedGoal = new FixVector2((Fix64)4.5f, (Fix64)2.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(10, 1f);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(first, sharedGoal, Fix64.One, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(second, sharedGoal, Fix64.One, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestLastFixedNavigationGoal(first.LogicEntityId.Value, out FixVector2 firstGoal));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestLastFixedNavigationGoal(second.LogicEntityId.Value, out FixVector2 secondGoal));

        Assert.AreEqual(sharedGoal.x.RawValue, firstGoal.x.RawValue);
        Assert.AreEqual(sharedGoal.y.RawValue, firstGoal.y.RawValue);
        Assert.AreNotEqual(firstGoal, secondGoal);
    }

    [Test]
    public void NavigationAuthorityDigest_同Cell不同FixedGoal必须不同()
    {
        bool[] walkable = { true, true, true, true, true, true };
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(6, 1, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 0.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
            ctx,
            new FixVector2((Fix64)4.25f, (Fix64)0.5f),
            Fix64.One,
            out _));
        var first = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(first);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
            ctx,
            new FixVector2((Fix64)4.75f, (Fix64)0.5f),
            Fix64.One,
            out _));
        var second = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(second);

        Assert.AreNotEqual(first.Hash, second.Hash);
    }

    [Test]
    public void DeterministicTile_权威提交即完成且不再占用后续Float预算()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = new bool[8 * 4];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(8, 4, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FixVector2 goal = new FixVector2((Fix64)7.5f, (Fix64)1.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryPrepareNavigationRequestFixed(ctx, goal, out string failureReason), failureReason);

        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

        Assert.AreEqual(1, FlowFieldCrowdMovementSystem.GetEditorTestDeterministicFlowTileCacheCount());
        Assert.AreEqual(1, FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount(),
            "诊断 cache 必须与整数权威 payload 同步提交，不能等待后续 float stage。");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), 0);
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestFrameTileBuildCount(),
            "整数权威提交后不得执行旧 float tile build stage。");

        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

        Assert.AreEqual(2, FlowFieldCrowdMovementSystem.GetEditorTestDeterministicFlowTileCacheCount(),
            "第二个整数 tile 必须按独立固定配额提交。");
        Assert.AreEqual(2, FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount());
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(),
            "权威 payload 提交后 job 必须立即离开 pending 队列。");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestFrameTileBuildCount(),
            "后续 Tick 也不得消费旧 float tile build budget。");
    }

    [Test]
    public void NavigationAuthorityDigest_同Cache数量不同DeterministicTile内容必须不同()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = new bool[8 * 4];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(8, 4, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryPrepareNavigationRequestFixed(
            ctx,
            new FixVector2((Fix64)7.5f, (Fix64)1.5f),
            out string firstFailure), firstFailure);
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

        Assert.AreEqual(1, FlowFieldCrowdMovementSystem.GetEditorTestDeterministicFlowTileCacheCount());
        ulong firstContentHash = FlowFieldCrowdMovementSystem.GetEditorTestDeterministicFlowTileAuthorityContentHash();

        FlowFieldCrowdMovementSystem.ClearEditorTestFlowTileCache();
        Assert.AreEqual(0UL, FlowFieldCrowdMovementSystem.GetEditorTestDeterministicFlowTileAuthorityContentHash());
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryPrepareNavigationRequestFixed(
            ctx,
            new FixVector2((Fix64)6.5f, (Fix64)1.5f),
            out string secondFailure), secondFailure);
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

        Assert.AreEqual(1, FlowFieldCrowdMovementSystem.GetEditorTestDeterministicFlowTileCacheCount());
        ulong secondContentHash = FlowFieldCrowdMovementSystem.GetEditorTestDeterministicFlowTileAuthorityContentHash();
        Assert.AreNotEqual(firstContentHash, secondContentHash);
    }

    [Test]
    public void NavigationAuthorityDigest_DeterministicTile保留帧变化必须分叉()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = new bool[8 * 4];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(8, 4, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryPrepareNavigationRequestFixed(
            ctx,
            new FixVector2((Fix64)7.5f, (Fix64)1.5f),
            out string failureReason), failureReason);
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        Assert.AreEqual(1, FlowFieldCrowdMovementSystem.GetEditorTestDeterministicFlowTileCacheCount());

        ulong contentHashBefore = FlowFieldCrowdMovementSystem.GetEditorTestDeterministicFlowTileAuthorityContentHash();
        var digestBefore = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(digestBefore);

        FlowFieldCrowdMovementSystem.SetEditorTestOnlyDeterministicTileRetentionFrames(99, 88);
        ulong contentHashAfter = FlowFieldCrowdMovementSystem.GetEditorTestDeterministicFlowTileAuthorityContentHash();
        var digestAfter = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(digestAfter);

        Assert.AreEqual(contentHashBefore, contentHashAfter, "保留帧不是 tile cost/direction 内容，不应重算 payload Hash。");
        Assert.AreNotEqual(digestBefore.Hash, digestAfter.Hash, "保留帧会决定 trim 淘汰结果，必须进入逐 Tick authority digest。");
    }

    [Test]
    public void NavigationAuthorityDigest_FlowTile双缓存镜像损坏必须明确报错()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = new bool[8 * 4];
        Array.Fill(walkable, true);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(8, 4, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryPrepareNavigationRequestFixed(
            ctx,
            new FixVector2((Fix64)7.5f, (Fix64)1.5f),
            out string failureReason), failureReason);
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        Assert.AreEqual(1, FlowFieldCrowdMovementSystem.GetEditorTestDeterministicFlowTileCacheCount());

        FlowFieldCrowdMovementSystem.AddEditorTestOnlyFlowTileMirrorExtraEntry();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(new LogicStateHasher()));
        StringAssert.Contains("flow-tile cache mirror count mismatch", exception.Message);
    }

    [Test]
    public void NavigationAuthorityDigest_PendingFlowTile索引损坏必须明确报错()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = new bool[8 * 4];
        Array.Fill(walkable, true);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(8, 4, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryPrepareNavigationRequestFixed(
            ctx,
            new FixVector2((Fix64)7.5f, (Fix64)1.5f),
            out string failureReason), failureReason);
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), 0);

        FlowFieldCrowdMovementSystem.PerturbEditorTestOnlyPendingFlowTileIndex();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(new LogicStateHasher()));
        StringAssert.Contains("pending flow-tile index count mismatch", exception.Message);
    }

    [Test]
    public void NavigationAuthorityCaches_同帧Lru平局必须按稳定键淘汰()
    {
        bool[] walkable = { true, true, true, true };
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(4, 1, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestSectorPathTrimVictim(false));
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestSectorPathTrimVictim(true),
            "SectorPathCache 的 LRU 平局不能依赖 Dictionary 插入/枚举顺序。");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalFieldTrimVictim(false));
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalFieldTrimVictim(true),
            "SharedGoalFields 的 LRU 平局不能依赖 Dictionary 插入/枚举顺序。");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestStartPortalChoiceTrimVictim(false));
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestStartPortalChoiceTrimVictim(true),
            "StartPortalChoiceCache 的 LRU 平局不能依赖 Dictionary 插入/枚举顺序。");
    }

    [Test]
    public void RuntimeDirty落区_冻结Q32网格后不读取FloatShadow()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        const int width = 24;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        Vector3 origin = new Vector3(12000.125f, 0f, -9000.375f);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 0.09f, origin, walkable);
        ProcessWorldBuildQueueUntilReady();
        FixVector2 center = FlowFieldCrowdMovementSystem.GetEditorTestGridToWorldCenterFixed(9, 1);
        Vector3 obstacleCenter = new Vector3((float)center.x, 0f, (float)center.y);
        FlowFieldCrowdMovementSystem.RegisterCircleObstacle(81101, obstacleCenter, 0.02f);
        int[] baselineDirtySectors = FlowFieldCrowdMovementSystem.GetEditorTestRuntimeDirtySectorIds(0);
        Assert.IsNotEmpty(baselineDirtySectors);

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 0.09f, origin, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.PerturbEditorTestWorldGridFloatShadows(
            3.75f,
            new Vector3(-5000f, 0f, 7000f));
        FlowFieldCrowdMovementSystem.RegisterCircleObstacle(81101, obstacleCenter, 0.02f);
        int[] shadowDirtySectors = FlowFieldCrowdMovementSystem.GetEditorTestRuntimeDirtySectorIds(0);

        CollectionAssert.AreEqual(baselineDirtySectors, shadowDirtySectors,
            "runtime-dirty 落区只能读取冻结的 Q32 grid 与 Fix64 障碍边界。");
    }

    [Test]
    public void RuntimeDirty优先级_不读取AgentFloatPositionShadow()
    {
        const int width = 40;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        SimEntityContext agent = CreateEntity(new Vector3(1.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.RegisterCircleObstacle(81102, new Vector3(1.5f, 0f, 1.5f), 0.25f);
        int baselinePriority = FlowFieldCrowdMovementSystem.GetEditorTestRuntimeDirtyPriority(0);
        var baselineDigest = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(baselineDigest);

        FlowFieldCrowdMovementSystem.PerturbEditorTestAgentFloatPosition(
            agent.LogicEntityId.Value,
            new Vector3(35.5f, 0f, 1.5f));
        int shadowPriority = FlowFieldCrowdMovementSystem.GetEditorTestRuntimeDirtyPriority(0);
        var shadowDigest = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(shadowDigest);

        Assert.AreEqual(baselineDigest.Hash, shadowDigest.Hash, "agent float position 是表现 shadow，不得进入 authority digest。");
        Assert.AreEqual(baselinePriority, shadowPriority,
            "FullHash 排除的 float position 也不能暗中改变 runtime-dirty 的完成顺序。");
    }

    [Test]
    public void PositionOccupancyQuery_不读取AgentFloatPositionShadow()
    {
        bool[] walkable = { true, true, true, true, true, true };
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(6, 1, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        SimEntityContext self = CreateEntity(new Vector3(0.5f, 0f, 0.5f));
        SimEntityContext other = CreateEntity(new Vector3(2.5f, 0f, 0.5f));
        Vector3 query = new Vector3(2.75f, 0f, 0.5f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.IsPositionOccupiedByOtherAgent(
            self.LogicEntityId.Value,
            query,
            1f,
            out int baselineBlocker,
            out float baselineDistance));

        FlowFieldCrowdMovementSystem.PerturbEditorTestAgentFloatPosition(
            other.LogicEntityId.Value,
            new Vector3(5.5f, 0f, 0.5f));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.IsPositionOccupiedByOtherAgent(
            self.LogicEntityId.Value,
            query,
            1f,
            out int shadowBlocker,
            out float shadowDistance));

        Assert.AreEqual(baselineBlocker, shadowBlocker);
        Assert.AreEqual(baselineDistance, shadowDistance);
    }

    [Test]
    public void NavigationAuthorityDigest_同Cache数量不同PortalFixedPayload必须不同()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = new bool[8 * 4];
        byte[] firstCosts = new byte[walkable.Length];
        byte[] secondCosts = new byte[walkable.Length];
        for (int i = 0; i < walkable.Length; i++)
        {
            walkable[i] = true;
            firstCosts[i] = 1;
            secondCosts[i] = 1;
        }
        secondCosts[1 + 1 * 8] = 40;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            int.MinValue + 1, 8, 4, 1f, Vector3.zero, walkable, null, firstCosts);
        ProcessWorldBuildQueueUntilReady();
        int firstCount = FlowFieldCrowdMovementSystem.GetEditorTestDeterministicSectorPortalAccessCacheCount();
        ulong firstHash = FlowFieldCrowdMovementSystem.GetEditorTestSectorPortalAccessAuthorityContentHash();

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            int.MinValue + 1, 8, 4, 1f, Vector3.zero, walkable, null, secondCosts);
        ProcessWorldBuildQueueUntilReady();
        int secondCount = FlowFieldCrowdMovementSystem.GetEditorTestDeterministicSectorPortalAccessCacheCount();
        ulong secondHash = FlowFieldCrowdMovementSystem.GetEditorTestSectorPortalAccessAuthorityContentHash();

        Assert.Greater(firstCount, 0);
        Assert.AreEqual(firstCount, secondCount);
        Assert.AreNotEqual(firstHash, secondHash);
    }

    [Test]
    public void NavigationAuthorityDigest_同Count不同CostStampFixedPayload必须不同()
    {
        bool[] walkable = new bool[8 * 4];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(8, 4, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(
            4101,
            new Vector3(1.25f, 0f, 1.5f),
            new Vector3(0.25f, 0f, 0.25f),
            20);
        ulong firstHash = FlowFieldCrowdMovementSystem.GetEditorTestCostStampAuthorityContentHash();

        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(
            4101,
            new Vector3(1.75f, 0f, 1.5f),
            new Vector3(0.25f, 0f, 0.25f),
            20);
        ulong secondHash = FlowFieldCrowdMovementSystem.GetEditorTestCostStampAuthorityContentHash();

        Assert.AreEqual(1, FlowFieldCrowdMovementSystem.GetEditorTestRuntimeCostStampCount());
        Assert.AreNotEqual(firstHash, secondHash);
    }

    [Test]
    public void NavigationAuthorityDigest_PendingWorld同游标不同AuthoredPayload必须不同()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 32;
        const int height = 16;
        bool[] firstWalkable = new bool[width * height];
        bool[] secondWalkable = new bool[width * height];
        for (int i = 0; i < firstWalkable.Length; i++)
        {
            firstWalkable[i] = true;
            secondWalkable[i] = true;
        }
        secondWalkable[secondWalkable.Length - 1] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, firstWalkable);
        FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild());
        string firstProgress = FlowFieldCrowdMovementSystem.GetEditorTestPendingWorldBuildProgressSignature();
        ulong firstProgressHash = FlowFieldCrowdMovementSystem.GetEditorTestAuthorityWorldProgressHash();

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, secondWalkable);
        FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild());
        string secondProgress = FlowFieldCrowdMovementSystem.GetEditorTestPendingWorldBuildProgressSignature();
        ulong secondProgressHash = FlowFieldCrowdMovementSystem.GetEditorTestAuthorityWorldProgressHash();

        Assert.AreEqual(firstProgress, secondProgress, "测试前提要求两个 world job 处于完全相同的阶段和游标。");
        Assert.AreNotEqual(firstProgressHash, secondProgressHash, "pending world 的 authored fixed payload 必须在提交前进入 authority world-progress digest。");
    }

    [Test]
    public void NavigationAuthorityDigest_PendingWorldIslandQueueOrderMustAffectHash()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 32;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        for (int i = 0; i < 10000; i++)
        {
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
            if (FlowFieldCrowdMovementSystem.GetEditorTestPendingWorldBuildIslandQueueCount() >= 2)
                break;
        }

        Assert.GreaterOrEqual(FlowFieldCrowdMovementSystem.GetEditorTestPendingWorldBuildIslandQueueCount(), 2);
        string firstProgress = FlowFieldCrowdMovementSystem.GetEditorTestPendingWorldBuildProgressSignature();
        ulong firstHash = FlowFieldCrowdMovementSystem.GetEditorTestAuthorityWorldProgressHash();

        FlowFieldCrowdMovementSystem.PerturbEditorTestPendingWorldBuildIslandQueueOrder();

        string secondProgress = FlowFieldCrowdMovementSystem.GetEditorTestPendingWorldBuildProgressSignature();
        ulong secondHash = FlowFieldCrowdMovementSystem.GetEditorTestAuthorityWorldProgressHash();
        Assert.AreEqual(firstProgress, secondProgress, "测试扰动只能改变 FIFO 顺序，不能改变已有阶段和游标摘要。");
        Assert.AreNotEqual(firstHash, secondHash, "会改变后续 BFS 访问顺序的 island FIFO 必须进入 authority digest。");
    }

    [Test]
    public void NavigationAuthorityDigest_忽略AnchorY但保留FixedXZ()
    {
        const int width = 8;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        Vector3[] firstAnchors = new Vector3[walkable.Length];
        Vector3[] yShadowAnchors = new Vector3[walkable.Length];
        Vector3[] changedXZAnchors = new Vector3[walkable.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = x + y * width;
                walkable[index] = true;
                firstAnchors[index] = new Vector3(x + 0.5f, 0f, y + 0.5f);
                yShadowAnchors[index] = new Vector3(x + 0.5f, 100f + index, y + 0.5f);
                changedXZAnchors[index] = firstAnchors[index];
            }
        }
        changedXZAnchors[changedXZAnchors.Length - 1].x += 0.125f;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable, firstAnchors);
        ProcessWorldBuildQueueUntilReady();
        ulong firstWorldHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.SetConfig(CreateConfig());
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable, yShadowAnchors);
        ProcessWorldBuildQueueUntilReady();
        ulong yShadowWorldHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.SetConfig(CreateConfig());
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable, changedXZAnchors);
        ProcessWorldBuildQueueUntilReady();
        ulong changedXZWorldHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();

        Assert.AreEqual(firstWorldHash, yShadowWorldHash, "authored anchor Y 只属于 Unity 高度边界，不得污染 committed world authority hash。");
        Assert.AreNotEqual(firstWorldHash, changedXZWorldHash, "authored anchor 的 fixed XZ 会改变落点和未来导航，必须进入 committed world authority hash。");
    }

    [Test]
    public void NavigationAuthorityDigest_CommittedWorld忽略OriginY和负零但保留Q32XZ()
    {
        const int width = 8;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        ulong firstWorldHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.SetConfig(CreateConfig());
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, new Vector3(-0f, 123f, -0f), walkable);
        ProcessWorldBuildQueueUntilReady();
        ulong yAndNegativeZeroShadowHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.SetConfig(CreateConfig());
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, new Vector3(0.125f, 123f, 0f), walkable);
        ProcessWorldBuildQueueUntilReady();
        ulong changedXZWorldHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();

        Assert.AreEqual(firstWorldHash, yAndNegativeZeroShadowHash, "Origin Y 和 IEEE -0 只属于 Unity/float 边界，Q32 XZ authority 应规范化为同一 world Hash。");
        Assert.AreNotEqual(firstWorldHash, changedXZWorldHash, "Origin fixed XZ 变化会改变所有落格边界，必须改变 committed world Hash。");
    }

    [Test]
    public void NavigationWorld_冻结Q32网格元数据后FloatShadow不改变落格或AuthorityHash()
    {
        const int width = 8;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        ulong baselineHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestWorldToGridFixed(
            new FixVector2((Fix64)1.25f, (Fix64)0.75f),
            out int baselineX,
            out int baselineY));

        FlowFieldCrowdMovementSystem.PerturbEditorTestWorldGridFloatShadows(
            0.5f,
            new Vector3(100f, 999f, -100f));
        ulong perturbedHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();
        bool perturbedInside = FlowFieldCrowdMovementSystem.TryGetEditorTestWorldToGridFixed(
            new FixVector2((Fix64)1.25f, (Fix64)0.75f),
            out int perturbedX,
            out int perturbedY);

        Assert.AreEqual(baselineHash, perturbedHash, "Unity float shadow 不得重新定义已提交 world 的 Q32 authority metadata。");
        Assert.IsTrue(perturbedInside);
        Assert.AreEqual(baselineX, perturbedX);
        Assert.AreEqual(baselineY, perturbedY);
    }

    [Test]
    public void NavigationGrid_IEEE754位模式转换为稳定Q32GoldenRaw()
    {
        Assert.AreEqual(0L, FlowFieldCrowdMovementSystem.GetEditorTestGridRawFromFloat(
            BitConverter.Int32BitsToSingle(unchecked((int)0x80000000))));
        Assert.AreEqual(4294967296L, FlowFieldCrowdMovementSystem.GetEditorTestGridRawFromFloat(
            BitConverter.Int32BitsToSingle(0x3F800000)));
        Assert.AreEqual(-6442450944L, FlowFieldCrowdMovementSystem.GetEditorTestGridRawFromFloat(
            BitConverter.Int32BitsToSingle(unchecked((int)0xBFC00000))));
        Assert.AreEqual(429496736L, FlowFieldCrowdMovementSystem.GetEditorTestGridRawFromFloat(
            BitConverter.Int32BitsToSingle(0x3DCCCCCD)));
        Assert.AreEqual(1L, FlowFieldCrowdMovementSystem.GetEditorTestGridRawFromFloat(
            BitConverter.Int32BitsToSingle(0x2F000000)));
        Assert.AreEqual(-1L, FlowFieldCrowdMovementSystem.GetEditorTestGridRawFromFloat(
            BitConverter.Int32BitsToSingle(unchecked((int)0xAF000000))));
    }

    [Test]
    public void NavigationWorld_大负坐标下边界前后一Raw严格落格()
    {
        const int width = 8;
        const int height = 4;
        const float cellSize = 0.25f;
        var origin = new Vector3(-1048576f, 321f, 524288f);
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, cellSize, origin, walkable);
        ProcessWorldBuildQueueUntilReady();

        Fix64 boundaryX = (Fix64)origin.x + (Fix64)cellSize * (Fix64)3;
        Fix64 insideZ = (Fix64)origin.z + (Fix64)cellSize * (Fix64)2 + (Fix64)0.125f;
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestWorldToGridFixed(
            new FixVector2(boundaryX, insideZ),
            out int boundaryCellX,
            out int boundaryCellY));
        Assert.AreEqual(3, boundaryCellX);
        Assert.AreEqual(2, boundaryCellY);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestWorldToGridFixed(
            new FixVector2(Fix64.FromRaw(boundaryX.RawValue - 1), insideZ),
            out int precedingCellX,
            out int precedingCellY));
        Assert.AreEqual(2, precedingCellX);
        Assert.AreEqual(2, precedingCellY);

        Assert.IsFalse(FlowFieldCrowdMovementSystem.TryGetEditorTestWorldToGridFixed(
            new FixVector2(Fix64.FromRaw(((Fix64)origin.x).RawValue - 1), insideZ),
            out int outsideCellX,
            out int outsideCellY));
        Assert.AreEqual(-1, outsideCellX);
        Assert.AreEqual(2, outsideCellY);
    }

    [Test]
    public void NavigationWorld_冻结AuthoredAnchorFixedXZ后FloatShadow不改变中心或AuthorityHash()
    {
        const int width = 8;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        Vector3[] anchors = new Vector3[walkable.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = x + y * width;
                walkable[index] = true;
                anchors[index] = new Vector3(x + 0.25f, 10f + index, y + 0.75f);
            }
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            width,
            height,
            1f,
            Vector3.zero,
            walkable,
            anchors);
        ProcessWorldBuildQueueUntilReady();
        ulong baselineHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();
        FixVector2 baselineCenter = FlowFieldCrowdMovementSystem.GetEditorTestGridToWorldCenterFixed(1, 1);

        FlowFieldCrowdMovementSystem.PerturbEditorTestWorldAnchorFloatShadows(100f, -100f);
        ulong perturbedHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();
        FixVector2 perturbedCenter = FlowFieldCrowdMovementSystem.GetEditorTestGridToWorldCenterFixed(1, 1);

        Assert.AreEqual(baselineHash, perturbedHash, "authored anchor 的 Vector3 shadow 不得重新定义 committed world authority hash。");
        Assert.AreEqual(baselineCenter, perturbedCenter);

        FlowFieldCrowdMovementSystem.SetEditorTestWorldAnchorFloatShadow(
            1 + width,
            new Vector3(float.NaN, float.PositiveInfinity, float.NegativeInfinity));
        ulong nonFiniteShadowHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();
        FixVector2 nonFiniteShadowCenter = FlowFieldCrowdMovementSystem.GetEditorTestGridToWorldCenterFixed(1, 1);

        Assert.AreEqual(baselineHash, nonFiniteShadowHash, "非 finite Vector3 shadow 也不能改变 committed world authority hash。");
        Assert.AreEqual(baselineCenter, nonFiniteShadowCenter, "冻结后的 authored fixed XZ 不能因表现 shadow 污染而回退到 cell center。");
    }

    [Test]
    public void NavigationAuthorityDigest_CommittedWorldClearance按Fix64Raw规范化()
    {
        const int width = 8;
        const int height = 4;
        const float firstRadius = 0.50001f;
        const float secondRadius = 0.50002f;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        int[] neighborOffsetX = { -1, 0, 1, -1, 1, -1, 0, 1 };
        int[] neighborOffsetY = { -1, -1, -1, 0, 0, 1, 1, 1 };
        byte[] neighborTraversal = new byte[walkable.Length];
        byte[] costs = new byte[walkable.Length];
        Vector3[] anchors = new Vector3[walkable.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte mask = 0;
                for (int direction = 0; direction < neighborOffsetX.Length; direction++)
                {
                    int nextX = x + neighborOffsetX[direction];
                    int nextY = y + neighborOffsetY[direction];
                    if (nextX >= 0 && nextX < width && nextY >= 0 && nextY < height)
                        mask |= (byte)(1 << direction);
                }

                int index = x + y * width;
                neighborTraversal[index] = mask;
                costs[index] = 1;
                anchors[index] = new Vector3(x + 0.5f, 0f, y + 0.5f);
            }
        }

        long firstClearanceRaw = ((Fix64)(firstRadius - 0.2f)).RawValue;
        long secondClearanceRaw = ((Fix64)(secondRadius - 0.2f)).RawValue;
        Assert.AreNotEqual(firstRadius, secondRadius, "测试前提要求两个 float 半径不同。");
        Assert.AreEqual(firstClearanceRaw, secondClearanceRaw, "测试前提要求两个 encoded clearance 量化为相同 Fix64 raw。");

        FlowNavigationGridAsset.DerivedNavigationData derivedData = FlowFieldCrowdMovementSystem.BuildDerivedNavigationDataForAsset(
            0,
            (Fix64)firstRadius,
            width,
            height,
            1f,
            Vector3.zero,
            walkable,
            anchors,
            costs,
            neighborTraversal);
        Assert.NotNull(derivedData);
        Assert.IsTrue(derivedData.IsValid);

        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(0, firstRadius);
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            0,
            width,
            height,
            1f,
            Vector3.zero,
            walkable,
            cellNavAnchors: anchors,
            costField: costs,
            neighborTraversalMask: neighborTraversal,
            derivedNavigationData: derivedData);
        ProcessWorldBuildQueueUntilReady();
        ulong firstWorldHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.SetConfig(CreateConfig());
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(0, secondRadius);
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            0,
            width,
            height,
            1f,
            Vector3.zero,
            walkable,
            cellNavAnchors: anchors,
            costField: costs,
            neighborTraversalMask: neighborTraversal,
            derivedNavigationData: derivedData);
        ProcessWorldBuildQueueUntilReady();
        ulong secondWorldHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();

        Assert.AreEqual(firstWorldHash, secondWorldHash, "相同 Fix64 clearance raw 不得因 float shadow 位不同而产生不同 committed world Hash。");
    }

    [Test]
    public void RuntimeDirtyFixed重建使用构建时冻结的AgentRadius()
    {
        const int width = 9;
        const int height = 9;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        byte[] BuildCostFieldAfterRadiusMutation(float radiusAfterBuild)
        {
            FlowFieldCrowdMovementSystem.ResetAll();
            FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
            FlowFieldCrowdMovementSystem.SetConfig(CreateConfig());
            FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(0, 0.5f);
            FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
            ProcessWorldBuildQueueUntilReady();
            long frozenBlurRadiusRaw = FlowFieldCrowdMovementSystem.GetEditorTestWallCostBlurRadiusFixedRaw();

            FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(0, radiusAfterBuild);
            Assert.AreEqual(
                frozenBlurRadiusRaw,
                FlowFieldCrowdMovementSystem.GetEditorTestWallCostBlurRadiusFixedRaw(),
                "committed world 的 fixed 墙距必须继续读取构建时冻结的 radius raw。");
            FlowFieldCrowdMovementSystem.RegisterBoxObstacle(
                9910,
                new Vector3(4.5f, 0f, 4.5f),
                new Vector3(0.2f, 0f, 0.2f));
            for (int i = 0; i < 128 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
                FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
            Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());

            byte[] costs = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(x, y, out costs[x + y * width]));
                }
            }

            return costs;
        }

        byte[] baseline = BuildCostFieldAfterRadiusMutation(0.5f);
        byte[] mutated = BuildCostFieldAfterRadiusMutation(0.75f);
        CollectionAssert.AreEqual(
            baseline,
            mutated,
            "runtime dirty fixed 墙距不得重读构建后的 float AgentTypeRadius shadow。");
    }

    [Test]
    public void NavigationAuthorityDigest_定点调度配置会在提交Tick前分叉()
    {
        FlowFieldNavigationConfig baselineConfig = CreateConfig();
        FlowFieldCrowdMovementSystem.SetConfig(baselineConfig);
        var baselineHasher = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(baselineHasher);

        FlowFieldNavigationConfig deterministicTileConfig = CreateConfig();
        deterministicTileConfig.DeterministicFlowTileCommitQuota++;
        FlowFieldCrowdMovementSystem.SetConfig(deterministicTileConfig);
        var deterministicTileHasher = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(deterministicTileHasher);
        Assert.AreNotEqual(baselineHasher.Hash, deterministicTileHasher.Hash, "deterministic tile 提交配额会改变可用 Tick，必须进入 authority digest。");

        FlowFieldNavigationConfig sharedGoalConfig = CreateConfig();
        sharedGoalConfig.SharedGoalBuildOperationQuota++;
        FlowFieldCrowdMovementSystem.SetConfig(sharedGoalConfig);
        var sharedGoalHasher = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(sharedGoalHasher);
        Assert.AreNotEqual(baselineHasher.Hash, sharedGoalHasher.Hash, "shared-goal 定点推进配额会改变提交 Tick，必须进入 authority digest。");

        FlowFieldNavigationConfig cacheLimitConfig = CreateConfig();
        cacheLimitConfig.FlowTileCacheLimit++;
        FlowFieldCrowdMovementSystem.SetConfig(cacheLimitConfig);
        var cacheLimitHasher = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(cacheLimitHasher);
        Assert.AreNotEqual(baselineHasher.Hash, cacheLimitHasher.Hash, "tile cache limit 会改变淘汰和重建 Tick，必须进入 authority digest。");
    }

    [Test]
    public void NavigationAuthorityDigest_调试配置不得进入Authority()
    {
        FlowFieldNavigationConfig baselineConfig = CreateConfig();
        FlowFieldCrowdMovementSystem.SetConfig(baselineConfig);
        var baselineHasher = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(baselineHasher);

        FlowFieldNavigationConfig debugConfig = CreateConfig();
        debugConfig.EnableDeterministicStaticCollisionShadow = !baselineConfig.EnableDeterministicStaticCollisionShadow;
        debugConfig.StaticCollisionShadowMismatchTolerance = baselineConfig.StaticCollisionShadowMismatchTolerance + 1f;
        debugConfig.StaticCollisionShadowLogIntervalTicks = baselineConfig.StaticCollisionShadowLogIntervalTicks + 1;
        debugConfig.DrawNavigationDebug = !baselineConfig.DrawNavigationDebug;
        debugConfig.DrawFlowFieldDebug = !baselineConfig.DrawFlowFieldDebug;
        FlowFieldCrowdMovementSystem.SetConfig(debugConfig);
        var debugHasher = new LogicStateHasher();
        FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(debugHasher);

        Assert.AreEqual(baselineHasher.Hash, debugHasher.Hash, "只影响 shadow、日志或绘制的配置不得污染 authority digest。");
    }

    [Test]
    public void NavigationAuthorityDigest_PendingRuntime同游标不同障碍快照必须不同()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = new bool[32 * 8];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(32, 8, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9001, new Vector3(1.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f));
        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
        string firstProgress = FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyProgressSignature();
        ulong firstProgressHash = FlowFieldCrowdMovementSystem.GetEditorTestAuthorityWorldProgressHash();

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(32, 8, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9001, new Vector3(2.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f));
        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
        string secondProgress = FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyProgressSignature();
        ulong secondProgressHash = FlowFieldCrowdMovementSystem.GetEditorTestAuthorityWorldProgressHash();

        Assert.AreEqual(firstProgress, secondProgress, "测试前提要求两个 runtime-dirty job 处于完全相同的阶段和游标。");
        Assert.AreNotEqual(firstProgressHash, secondProgressHash, "冻结障碍的 fixed payload 必须进入 pending runtime-dirty authority digest。");
    }

    [Test]
    public void NavigationAuthorityDigest_PendingRuntimeIslandQueueOrderMustAffectHash()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = new bool[32 * 8];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(32, 8, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(
            9001,
            new Vector3(1.5f, 0f, 1.5f),
            new Vector3(0.49f, 0f, 0.49f));
        for (int i = 0; i < 10000; i++)
        {
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
            if (FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyIslandQueueCount() >= 2)
                break;
        }

        Assert.GreaterOrEqual(FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyIslandQueueCount(), 2);
        string firstProgress = FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyProgressSignature();
        ulong firstHash = FlowFieldCrowdMovementSystem.GetEditorTestAuthorityWorldProgressHash();

        FlowFieldCrowdMovementSystem.PerturbEditorTestPendingRuntimeDirtyIslandQueueOrder();

        string secondProgress = FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyProgressSignature();
        ulong secondHash = FlowFieldCrowdMovementSystem.GetEditorTestAuthorityWorldProgressHash();
        Assert.AreEqual(firstProgress, secondProgress, "测试扰动只能改变 FIFO 顺序，不能改变已有阶段和游标摘要。");
        Assert.AreNotEqual(firstHash, secondHash, "会改变后续 BFS 访问顺序的 runtime island FIFO 必须进入 authority digest。");
    }

    [Test]
    public void CompleteRuntimeRebuildQueue_导航World缺失时必须显式报错()
    {
        FlowFieldCrowdMovementSystem.SetConfig(CreateConfig());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => FlowFieldCrowdMovementSystem.CompleteRuntimeRebuildQueue());

        StringAssert.Contains("no navigation worlds are registered", exception.Message);
    }

    [Test]
    public void CompleteRuntimeRebuildQueue_低配额Pending也必须立即提交最新障碍World()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = new bool[32 * 8];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(32, 8, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(
            9001,
            new Vector3(2.5f, 0f, 1.5f),
            new Vector3(0.49f, 0f, 0.49f));
        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "测试前提要求低配额重建仍处于 pending。");

        int completedWorldCount = FlowFieldCrowdMovementSystem.CompleteRuntimeRebuildQueue();

        Assert.AreEqual(1, completedWorldCount);
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestRuntimeCellDiagnostics(
            2,
            1,
            out bool committedWalkable,
            out _,
            out _,
            out _,
            out _,
            out _));
        Assert.IsFalse(committedWalkable, "同步完成后 committed world 必须立即包含最新建筑障碍。");
    }

    [Test]
    public void 重叠CostStamp反向注册仍按StableId得到相同World和Digest()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        bool[] walkable = new bool[8 * 4];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(8, 4, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(100, new Vector3(2.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f), 5);
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(200, new Vector3(2.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f), 9);
        for (int i = 0; i < 512 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(2, 1, out byte firstCost));
        ulong firstWorldHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();
        ulong firstStampHash = FlowFieldCrowdMovementSystem.GetEditorTestCostStampAuthorityContentHash();

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(8, 4, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(200, new Vector3(2.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f), 9);
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(100, new Vector3(2.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f), 5);
        for (int i = 0; i < 512 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(2, 1, out byte secondCost));
        ulong secondWorldHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();
        ulong secondStampHash = FlowFieldCrowdMovementSystem.GetEditorTestCostStampAuthorityContentHash();

        Assert.AreEqual(9, firstCost, "较大 StableId 的 stamp 应按稳定顺序最后提交。");
        Assert.AreEqual(firstCost, secondCost, "相同 stamp 集合不得因 Dictionary 插入顺序改变 CostField。");
        Assert.AreEqual(firstStampHash, secondStampHash, "相同 fixed stamp 集合必须得到相同内容 Hash。");
        Assert.AreEqual(firstWorldHash, secondWorldHash, "相同 fixed stamp 集合和 world 结果必须得到相同 committed world Hash。");
    }

    [Test]
    public void RuntimeDirty增量WorldHash必须与同步哈希完全一致且提交前不可见()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        config.RuntimeRebuildOperationQuota = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        const int width = 64;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        ulong committedBefore = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();
        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(
            9101,
            new Vector3(10.5f, 0f, 4.5f),
            new Vector3(0.49f, 0f, 0.49f));

        bool reachedWorldHash = false;
        for (int i = 0; i < 10000 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
        {
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
            if (!FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty())
                break;
            if (!FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyProgressSignature().StartsWith("WorldHash:"))
                continue;

            reachedWorldHash = true;
            Assert.AreEqual(
                committedBefore,
                FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash(),
                "增量 Hash 未完成前不得改变 committed world set hash。");
            break;
        }

        Assert.IsTrue(reachedWorldHash, "测试必须观察到可恢复的 WorldHash 阶段。");
        ProcessRuntimeDirtyQueueUntilReady(1);
        Assert.AreEqual(
            FlowFieldCrowdMovementSystem.GetEditorTestSynchronousWorldContentHash(),
            FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldContentHash(),
            "增量 token 序列必须与原同步 AddNavigationWorldContents 得到完全相同的 Hash。");
        Assert.AreNotEqual(committedBefore, FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash());
    }

    [Test]
    public void RuntimeDirtyCloneShell_AdvancesAtMostOneLargeBlockPerQueueTick()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        config.RuntimeRebuildOperationQuota = 512;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        const int width = 64;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(
            9121,
            new Vector3(10.5f, 0f, 4.5f),
            new Vector3(0.49f, 0f, 0.49f));

        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        StringAssert.StartsWith(
            "InitializeClone:RentWalkableMask:0:0:False:",
            FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyProgressSignature());
        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        StringAssert.StartsWith(
            "InitializeClone:RentCostField:0:0:False:",
            FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyProgressSignature());
        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        StringAssert.StartsWith(
            "InitializeClone:RentNeighborTraversalMask:0:0:False:",
            FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyProgressSignature());
        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        StringAssert.StartsWith(
            "InitializeClone:RentIslandIds:0:0:False:",
            FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyProgressSignature());
    }

    [Test]
    public void RuntimeDirty增量WorldHash完成调用数不得受CPU延时影响()
    {
        int baselineCalls = CountIncrementalRuntimeDirtyCallsWithCpuDelay(0, out ulong baselineHash);
        int delayedCalls = CountIncrementalRuntimeDirtyCallsWithCpuDelay(5000, out ulong delayedHash);

        Assert.AreEqual(baselineCalls, delayedCalls, "runtime dirty 完成边界只能由逻辑工作量决定，不能由 CPU 墙钟速度决定。");
        Assert.AreEqual(baselineHash, delayedHash, "相同输入在不同 CPU 延时下必须提交相同 world Hash。");
    }

    [Test]
    public void RuntimeDirty增量WorldHash单次调用必须受Token工作量上限约束()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        config.RuntimeRebuildOperationQuota = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        const int width = 64;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(
            9151,
            new Vector3(10.5f, 0f, 4.5f),
            new Vector3(0.49f, 0f, 0.49f));
        for (int i = 0; i < 10000; i++)
        {
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
            if (FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyProgressSignature().StartsWith("WorldHash:"))
                break;
        }

        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyProgressSignature().StartsWith("WorldHash:"),
            "测试必须到达增量 WorldHash 阶段。");
        long before = FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyWorldHashProcessedTokenCount();
        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        long after = FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyWorldHashProcessedTokenCount();
        long processed = after - before;
        Assert.Greater(processed, 0L);
        Assert.LessOrEqual(processed, 512L, "8 个预算操作每个最多处理 64 个 world hash token。");
    }

    [Test]
    public void RuntimeDirty在WorldHash阶段取消不得半提交PortalCache或World()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        config.RuntimeRebuildOperationQuota = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        const int width = 64;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(
            9201,
            new Vector3(10.5f, 0f, 4.5f),
            new Vector3(0.49f, 0f, 0.49f));
        for (int i = 0; i < 10000; i++)
        {
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
            Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
            if (FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyProgressSignature().StartsWith("WorldHash:"))
                break;
        }

        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyProgressSignature().StartsWith("WorldHash:"),
            "测试必须在工作 world 已完整但尚未提交的阶段触发取消。");
        int cacheCountBefore = FlowFieldCrowdMovementSystem.GetEditorTestSectorPortalAccessCacheCount();
        ulong cacheHashBefore = FlowFieldCrowdMovementSystem.GetEditorTestSectorPortalAccessAuthorityContentHash();
        ulong committedHashBefore = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(
            9202,
            new Vector3(14.5f, 0f, 4.5f),
            new Vector3(0.49f, 0f, 0.49f));

        Assert.AreEqual(cacheCountBefore, FlowFieldCrowdMovementSystem.GetEditorTestSectorPortalAccessCacheCount());
        Assert.AreEqual(cacheHashBefore, FlowFieldCrowdMovementSystem.GetEditorTestSectorPortalAccessAuthorityContentHash());
        Assert.AreEqual(committedHashBefore, FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash());
        ProcessRuntimeDirtyQueueUntilReady(1);
        Assert.AreEqual(
            FlowFieldCrowdMovementSystem.GetEditorTestSynchronousWorldContentHash(),
            FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldContentHash());
    }

    [Test]
    public void NavigationAuthorityDigest_SharedGoalJob数量不变时推进状态必须不同()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 32);
        config.DeterministicFlowTileCommitQuota = 1;
        config.SharedGoalBuildOperationQuota = 1;
        config.SectorSizeInCells = 48;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        const int width = 96;
        const int height = 48;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 23.5f));
        SimEntityContext target = CreateEntity(new Vector3(95.5f, 0f, 23.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        int firstCount = FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount();
        ulong firstHash = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildQueueAuthorityHash();
        int refreshCountAfterFirstHash = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildJobAuthorityHashRefreshCount();
        ulong repeatedFirstHash = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildQueueAuthorityHash();
        int refreshCountAfterRepeatedHash = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildJobAuthorityHashRefreshCount();

        FlowFieldCrowdMovementSystem.SetEditorTestClock(3, 0.3f);
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        int secondCount = FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount();
        ulong secondHash = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildQueueAuthorityHash();
        int refreshCountAfterProgress = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildJobAuthorityHashRefreshCount();

        Assert.Greater(firstCount, 0);
        Assert.AreEqual(firstCount, secondCount);
        Assert.AreEqual(firstHash, repeatedFirstHash, "未推进的 pending shared-goal job 必须复用同一 authority progress Hash。");
        Assert.AreEqual(refreshCountAfterFirstHash, refreshCountAfterRepeatedHash, "重复 FullHash 读取不得再次扫描 pending shared-goal heap/NodeCosts。");
        Assert.AreNotEqual(firstHash, secondHash);
        Assert.Greater(refreshCountAfterProgress, refreshCountAfterRepeatedHash, "job 推进后必须失效并刷新 authority progress Hash。");
    }

    [Test]
    public void NavigationAuthorityDigest_PendingSharedGoal索引损坏必须明确报错()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 32);
        config.SharedGoalBuildOperationQuota = 1;
        config.SectorSizeInCells = 48;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        const int width = 96;
        const int height = 48;
        bool[] walkable = new bool[width * height];
        Array.Fill(walkable, true);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 23.5f));
        var sources = new List<IEntityContext> { chaser };
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryPrepareSharedGoalRequest(
            new Vector3(95.5f, 0f, 23.5f),
            sources,
            out string failureReason), failureReason);
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount(), 0);

        FlowFieldCrowdMovementSystem.PerturbEditorTestOnlyPendingSharedGoalIndex();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildQueueAuthorityHash());
        StringAssert.Contains("pending shared-goal index count mismatch", exception.Message);
    }

    [Test]
    public void NavigationAuthorityDigest_重复SharedGoalDemand不得刷新未变化ProgressHash()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 32);
        config.DeterministicFlowTileCommitQuota = 1;
        config.SharedGoalBuildOperationQuota = 1;
        config.SectorSizeInCells = 48;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        const int width = 96;
        const int height = 48;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 23.5f));
        var sources = new List<IEntityContext> { chaser };
        Vector3 goal = new Vector3(95.5f, 0f, 23.5f);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryPrepareSharedGoalRequest(goal, sources, out string firstFailure), firstFailure);
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount(), 0);
        ulong firstHash = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildQueueAuthorityHash();
        int refreshCountAfterFirstHash = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildJobAuthorityHashRefreshCount();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryPrepareSharedGoalRequest(goal, sources, out string repeatedFailure), repeatedFailure);
        ulong repeatedHash = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildQueueAuthorityHash();
        int refreshCountAfterRepeatedDemand = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildJobAuthorityHashRefreshCount();

        Assert.AreEqual(firstHash, repeatedHash, "相同 shared-goal demand 不得改变 authority queue Hash。");
        Assert.AreEqual(
            refreshCountAfterFirstHash,
            refreshCountAfterRepeatedDemand,
            "相同 start sector/cell 已存在时不得把 progress Hash 标脏并重新扫描 job payload。");

        SimEntityContext secondChaser = CreateEntity(new Vector3(48.5f, 0f, 23.5f));
        var secondSources = new List<IEntityContext> { secondChaser };
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryPrepareSharedGoalRequest(goal, secondSources, out string secondFailure), secondFailure);
        ulong expandedHash = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildQueueAuthorityHash();
        int refreshCountAfterExpandedDemand = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildJobAuthorityHashRefreshCount();

        Assert.AreNotEqual(repeatedHash, expandedHash, "新增 start sector/cell demand 必须改变 authority queue Hash。");
        Assert.Greater(
            refreshCountAfterExpandedDemand,
            refreshCountAfterRepeatedDemand,
            "新增 demand 必须失效并重新计算 progress Hash。");
    }

    [Test]
    public void NavigationAuthorityDigest_SharedGoal真实推进后不得全量扫描累计Portal状态()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 64);
        config.SharedGoalBuildOperationQuota = 1;
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        const int width = 256;
        const int height = 256;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 0.5f));
        SimEntityContext target = CreateEntity(new Vector3(255.5f, 0f, 255.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        FlowFieldCrowdMovementSystem.SetEditorTestClock(0, 0f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));

        for (int frame = 1; frame <= 8; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame / 30f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        }

        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount(), 0);
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryValidateEditorTestSharedGoalIncrementalAuthorityHashes(out string consistencyFailure),
            consistencyFailure);
        long visitedBefore = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildJobAuthorityHashVisitedEntryCount();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildQueueAuthorityHash();
        stopwatch.Stop();
        long visitedEntries = FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalBuildJobAuthorityHashVisitedEntryCount() - visitedBefore;
        TestContext.WriteLine($"shared-goal authority refresh: visitedEntries={visitedEntries}, elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F3}");

        Assert.AreEqual(
            0,
            visitedEntries,
            "单次 Hash 刷新只能汇总增量摘要，不得重新扫描随历史推进增长的 heap/map/set。");
    }

    [Test]
    public void FixedWorldToGrid_负坐标边界使用Raw向下取整()
    {
        bool[] walkable = new bool[9];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            3,
            3,
            1f,
            new Vector3(-1.5f, 0f, -1.5f),
            walkable);
        ProcessWorldBuildQueueUntilReady();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestWorldToGridFixed(
            new FixVector2((Fix64)(-1.5f), (Fix64)(-1.5f)),
            out int originX,
            out int originY));
        Assert.AreEqual(0, originX);
        Assert.AreEqual(0, originY);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestWorldToGridFixed(
            new FixVector2((Fix64)(-0.5f), (Fix64)(-0.5f)),
            out int boundaryX,
            out int boundaryY));
        Assert.AreEqual(1, boundaryX);
        Assert.AreEqual(1, boundaryY);

        Fix64 justOutside = Fix64.FromRaw(((Fix64)(-1.5f)).RawValue - 1);
        Assert.IsFalse(FlowFieldCrowdMovementSystem.TryGetEditorTestWorldToGridFixed(
            new FixVector2(justOutside, (Fix64)(-1.5f)),
            out int outsideX,
            out _));
        Assert.AreEqual(-1, outsideX);
    }

    [Test]
    public void FixedWorldToGrid_Lv3长网格不累计CellSize量化误差()
    {
        const int width = 1599;
        bool[] walkable = new bool[width];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            width,
            1,
            0.089999996f,
            new Vector3(12.78f, 0f, 7.1099997f),
            walkable);
        ProcessWorldBuildQueueUntilReady();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestWorldToGridFixed(
            new FixVector2((Fix64)70.03f, (Fix64)7.15f),
            out int worldX,
            out int worldY));
        Assert.AreEqual(636, worldX);
        Assert.AreEqual(0, worldY);
    }

    [Test]
    public void 位移期间不清空导航目标_结束后恢复寻路移动()
    {
        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 0.5f));
        CharacterMoveComp moveComp = new CharacterMoveComp();
        moveComp.Init(ctx);
        ctx.MoveComp = moveComp;

        DurationMoveEffectComp effectComp = new DurationMoveEffectComp();
        effectComp.Init(ctx);

        moveComp.MoveToFixed(new FixVector2((Fix64)6.5f, (Fix64)0.5f));

        bool[] walkable = new bool[7];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(7, 1, 1f, Vector3.zero, walkable);

        effectComp.StartDurationAdditionalMove((Fix64)0.1f, FixVector2.Zero);
        effectComp.ApplyEffect((Fix64)0.1f);
        Assert.AreEqual(MovementMode.Displaced, ctx.MoveExecutor.MovementMode, "位移生效期间应切入 Displaced");
        moveComp.Move((Fix64)0.1f);
        ((SimMoveExecutor)ctx.MoveExecutor).Execute(0.1f);
        ctx.SyncPositionFromExecutor();

        Assert.AreEqual(0.5f, ctx.Position.x, 0.001f, "位移期间不应执行主动寻路位移");

        effectComp.ApplyEffect((Fix64)0.1f);
        Assert.AreEqual(MovementMode.Normal, ctx.MoveExecutor.MovementMode, "位移结束后应恢复 Normal");
        moveComp.Move((Fix64)0.1f);
        ((SimMoveExecutor)ctx.MoveExecutor).Execute(0.1f);
        ctx.SyncPositionFromExecutor();

        Assert.Greater(ctx.Position.x, 0.5f, "位移结束后应沿原目标继续移动");
    }

    [Test]
    public void 导航预览路径应绕过障碍并输出拐点()
    {
        const int width = 5;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        for (int y = 0; y < height - 1; y++)
            walkable[y * width + 2] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        Vector3 start = new Vector3(0.5f, 0f, 0.5f);
        Vector3 goal = new Vector3(4.5f, 0f, 0.5f);
        List<Vector3> corners = new List<Vector3>();
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryGetNavigationPathCorners(start, goal, 0, corners, out string failureReason),
            failureReason);
        Assert.Greater(corners.Count, 2, "绕开墙体的路径必须包含中间拐点，不能退化为起终点直线");

        float maxZ = float.NegativeInfinity;
        for (int i = 0; i < corners.Count; i++)
            maxZ = Mathf.Max(maxZ, corners[i].z);
        Assert.Greater(maxZ, 3.5f, $"路径应经过墙体上方缺口，corners=[{string.Join(", ", corners)}]");

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryEstimateNavigationDistance(start, goal, 0, out float distance, out failureReason), failureReason);
        Assert.Greater(distance, Vector3.Distance(start, goal), "导航距离必须反映绕路长度，不能回退成直线距离");

        var fixedStart = new FixVector2((Fix64)start.x, (Fix64)start.z);
        var fixedGoal = new FixVector2((Fix64)goal.x, (Fix64)goal.z);
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryEstimateNavigationDistanceFixed(
                fixedStart,
                fixedGoal,
                0,
                out Fix64 fixedDistance,
                out failureReason),
            failureReason);
        Assert.Greater(
            fixedDistance.RawValue,
            FixVector2.Distance(fixedStart, fixedGoal).RawValue,
            "定点导航距离必须反映绕路长度");
    }

    [Test]
    public void 跨Sector导航预览必须输出Sector内部绕障拐点()
    {
        const int width = 40;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        for (int y = 0; y < height - 1; y++)
            walkable[y * width + 2] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        Vector3 start = new Vector3(0.5f, 0f, 0.5f);
        Vector3 goal = new Vector3(39.5f, 0f, 0.5f);
        var corners = new List<Vector3>();
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryGetNavigationPathCorners(start, goal, 0, corners, out string failureReason),
            failureReason);

        float maxZ = float.NegativeInfinity;
        for (int i = 0; i < corners.Count; i++)
            maxZ = Mathf.Max(maxZ, corners[i].z);
        Assert.Greater(maxZ, 3.5f, $"跨 Sector 路径必须经过墙体缺口，corners=[{string.Join(", ", corners)}]");
    }

    [Test]
    public void 非阻塞导航预览不得刷新权威路径缓存Usage()
    {
        const int width = 40;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestSectorPortalAccessCacheCount(), 0);
        FlowFieldCrowdMovementSystem.SetEditorTestOnlySectorPortalAccessLastUsedFrame(-1234);
        FlowFieldCrowdMovementSystem.GetEditorTestAuthorityCacheState(
            out int sectorPathCountBefore,
            out ulong sectorPathContentBefore,
            out ulong sectorPathUsageBefore,
            out int portalAccessCountBefore,
            out ulong portalAccessContentBefore,
            out ulong portalAccessUsageBefore,
            out int sharedGoalCountBefore,
            out ulong sharedGoalContentBefore,
            out ulong sharedGoalUsageBefore);
        var authorityBefore = new LogicStateHasher();
        LogicNavigationAuthorityDigest digestBefore =
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigestWithCheckpoints(authorityBefore);
        string liveStateBefore = FlowFieldCrowdMovementSystem.GetEditorTestAuthorityCheckpointLiveStateSignature();

        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPointFixedNonBlocking(
                new FixVector2((Fix64)39.5f, (Fix64)1.5f),
                0,
                (Fix64)2,
                Fix64.Zero,
                out FixVector2 previewGoal));
        var corners = new List<Vector3>();
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryGetNavigationPathCornersNonBlocking(
                new Vector3(0.5f, 0f, 1.5f),
                new Vector3((float)previewGoal.x, 0f, (float)previewGoal.y),
                0,
                corners,
                out string failureReason),
            failureReason);

        FlowFieldCrowdMovementSystem.GetEditorTestAuthorityCacheState(
            out int sectorPathCountAfter,
            out ulong sectorPathContentAfter,
            out ulong sectorPathUsageAfter,
            out int portalAccessCountAfter,
            out ulong portalAccessContentAfter,
            out ulong portalAccessUsageAfter,
            out int sharedGoalCountAfter,
            out ulong sharedGoalContentAfter,
            out ulong sharedGoalUsageAfter);
        var authorityAfter = new LogicStateHasher();
        LogicNavigationAuthorityDigest digestAfter =
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigestWithCheckpoints(authorityAfter);
        string liveStateAfter = FlowFieldCrowdMovementSystem.GetEditorTestAuthorityCheckpointLiveStateSignature();
        Assert.AreEqual(digestBefore.WorldAndConfigHash, digestAfter.WorldAndConfigHash, "WorldAndConfig changed.");
        Assert.AreEqual(liveStateBefore, liveStateAfter, "CheckpointLiveState fields changed.");
        Assert.AreEqual(digestBefore.CheckpointLiveStateHash, digestAfter.CheckpointLiveStateHash, "CheckpointLiveState changed.");
        Assert.AreEqual(digestBefore.WorldProgressHash, digestAfter.WorldProgressHash, "WorldProgress changed.");
        Assert.AreEqual(digestBefore.RuntimeObstaclesHash, digestAfter.RuntimeObstaclesHash, "RuntimeObstacles changed.");
        Assert.AreEqual(digestBefore.AgentsHash, digestAfter.AgentsHash, "Agents changed.");
        Assert.AreEqual(digestBefore.CachesHash, digestAfter.CachesHash, "Caches changed.");
        Assert.AreEqual(digestBefore.FlowTilesHash, digestAfter.FlowTilesHash, "FlowTiles changed.");
        Assert.AreEqual(digestBefore.FlowTileBuildQueueHash, digestAfter.FlowTileBuildQueueHash, "FlowTileBuildQueue changed.");
        Assert.AreEqual(digestBefore.SharedGoalBuildQueueHash, digestAfter.SharedGoalBuildQueueHash, "SharedGoalBuildQueue changed.");
        Assert.AreEqual(digestBefore.MovingTargetAnchorsHash, digestAfter.MovingTargetAnchorsHash, "MovingTargetAnchors changed.");
        Assert.AreEqual(digestBefore.GoalReservationsHash, digestAfter.GoalReservationsHash, "GoalReservations changed.");
        Assert.AreEqual(digestBefore.FixedPortalOwnersHash, digestAfter.FixedPortalOwnersHash, "FixedPortalOwners changed.");
        Assert.AreEqual(digestBefore.FixedCorridorBuildsHash, digestAfter.FixedCorridorBuildsHash, "FixedCorridorBuilds changed.");
        Assert.AreEqual(
            authorityBefore.Hash,
            authorityAfter.Hash,
            "非阻塞 UI 预览不得改变 path handle 序列或任何 Navigation authority 状态。");
        Assert.AreEqual(sectorPathCountBefore, sectorPathCountAfter);
        Assert.AreEqual(sectorPathContentBefore, sectorPathContentAfter);
        Assert.AreEqual(sectorPathUsageBefore, sectorPathUsageAfter);
        Assert.AreEqual(portalAccessCountBefore, portalAccessCountAfter);
        Assert.AreEqual(portalAccessContentBefore, portalAccessContentAfter);
        Assert.AreEqual(portalAccessUsageBefore, portalAccessUsageAfter);
        Assert.AreEqual(sharedGoalCountBefore, sharedGoalCountAfter);
        Assert.AreEqual(sharedGoalContentBefore, sharedGoalContentAfter);
        Assert.AreEqual(sharedGoalUsageBefore, sharedGoalUsageAfter);
    }

    [Test]
    public void 非阻塞导航预览应区分RuntimeDirty等待与真实失败()
    {
        const int width = 40;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 1);
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.RegisterCircleObstacle(
            39001,
            new Vector3(20.5f, 0f, 0.5f),
            0.2f);
        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());

        var corners = new List<Vector3>();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.TryGetNavigationPathCornersNonBlocking(
            new Vector3(0.5f, 0f, 1.5f),
            new Vector3(39.5f, 0f, 1.5f),
            0,
            corners,
            out string pendingReason,
            out bool navigationUpdatePending));
        Assert.IsTrue(navigationUpdatePending);
        StringAssert.Contains("navigation update pending", pendingReason);

        for (int i = 0; i < 10000 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryGetNavigationPathCornersNonBlocking(
                new Vector3(0.5f, 0f, 1.5f),
                new Vector3(39.5f, 0f, 1.5f),
                0,
                corners,
                out string failureReason,
                out navigationUpdatePending),
            failureReason);
        Assert.IsFalse(navigationUpdatePending);
    }

    [Test]
    public void 不可达目标的路径和距离查询都应明确失败()
    {
        const int width = 5;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        for (int y = 0; y < height; y++)
            walkable[y * width + 2] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        Vector3 start = new Vector3(0.5f, 0f, 0.5f);
        Vector3 goal = new Vector3(4.5f, 0f, 0.5f);
        List<Vector3> corners = new List<Vector3>();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.TryGetNavigationPathCorners(start, goal, 0, corners, out string pathFailure));
        StringAssert.Contains("no traversable grid path", pathFailure);
        Assert.IsEmpty(corners);

        Assert.IsFalse(FlowFieldCrowdMovementSystem.TryEstimateNavigationDistance(start, goal, 0, out _, out string distanceFailure));
        StringAssert.Contains("no traversable grid path", distanceFailure);
        Assert.IsFalse(
            FlowFieldCrowdMovementSystem.TryEstimateNavigationDistanceFixed(
                new FixVector2((Fix64)start.x, (Fix64)start.z),
                new FixVector2((Fix64)goal.x, (Fix64)goal.z),
                0,
                out _,
                out string fixedDistanceFailure));
        StringAssert.Contains("no traversable grid path", fixedDistanceFailure);
    }

    [Test]
    public void ClusterSpawn定点采样可重复且现有单位占位读取Fixed位置()
    {
        const int width = 12;
        const int height = 12;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        var center = new FixVector2((Fix64)5.5f, (Fix64)5.5f);
        var first = new List<FixVector2>();
        var second = new List<FixVector2>();
        Assert.IsTrue(ClusterSpawnSystem.TryGetSpawnPositionsFixed(
            center, 6, (Fix64)2f, (Fix64)0.7f, first, false, 0));
        Assert.IsTrue(ClusterSpawnSystem.TryGetSpawnPositionsFixed(
            center, 6, (Fix64)2f, (Fix64)0.7f, second, false, 0));
        Assert.AreEqual(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.AreEqual(first[i].x.RawValue, second[i].x.RawValue, $"x raw mismatch at {i}");
            Assert.AreEqual(first[i].y.RawValue, second[i].y.RawValue, $"y raw mismatch at {i}");
        }
        Assert.AreEqual(center.x.RawValue, first[0].x.RawValue);
        Assert.AreEqual(center.y.RawValue, first[0].y.RawValue);

        CreateEntity(new Vector3((float)center.x, 0f, (float)center.y), false, 0, 0.25f);
        var avoiding = new List<FixVector2>();
        Assert.IsTrue(ClusterSpawnSystem.TryGetSpawnPositionsFixed(
            center, 6, (Fix64)2f, (Fix64)0.7f, avoiding, true, 0));
        Fix64 occupancyDistanceSq = (Fix64)0.7f * (Fix64)0.7f;
        for (int i = 0; i < avoiding.Count; i++)
        {
            Assert.GreaterOrEqual(
                FixVector2.SqrMagnitude(avoiding[i] - center).RawValue,
                occupancyDistanceSq.RawValue,
                $"spawn {i} overlaps the fixed blocker");
        }
    }

    [Test]
    public void 非瓶颈普通寻路会输出正常速度()
    {
        const int width = 4;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 0.5f));
        Vector3 goal = new Vector3(3.5f, 0f, 0.5f);

        Vector3 velocity = ResolveDeterministicFlowVelocityAfterQueue(ctx, goal, 2f, 1, out _);

        Assert.Greater(velocity.x, 1.5f, $"普通寻路应保持正常前进速度，velocity={velocity}");
        Assert.AreEqual(0f, velocity.z, 0.15f, $"普通寻路不应出现异常侧向蠕动，velocity={velocity}");
    }

    [Test]
    public void 没有导航世界时会严格报错而不是Fallback()
    {
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();

        SimEntityContext left = CreateEntity(new Vector3(0f, 0f, 0f));
        Vector3 goal = new Vector3(6f, 0f, 0f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[TestUnit\] Flow strict fail: world unavailable"));
        var ex = Assert.Throws<System.InvalidOperationException>(
            () => FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(left, goal, 2f, out _));
        StringAssert.Contains("Flow strict fail: world unavailable", ex.Message);
    }

    [Test]
    public void 起点和目标不在同一Island时会解析到最近可达目标()
    {
        const int width = 5;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        SetWalkable(walkable, width, 0, 1);
        SetWalkable(walkable, width, 1, 1);
        SetWalkable(walkable, width, 3, 1);
        SetWalkable(walkable, width, 4, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f), false, 0, 0.18f);
        Vector3 goal = new Vector3(4.5f, 0f, 1.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[FlowGoalResolvedToReachable\]"));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(1);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));

        System.Text.StringBuilder diagnostics = new System.Text.StringBuilder(2048);
        AppendSteeringBreakdown(diagnostics, ctx);
        Assert.Greater(velocity.x, 0.5f, $"不可达目标应解析到当前 island 上最近可达点并继续前进，velocity={velocity}\n{diagnostics}");
    }

    [Test]
    public void 细窄可走格不会被中心点误判为不可走()
    {
        const int width = 5;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int y = 0; y < height; y++)
            walkable[2 + y * width] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 0.5f));
        Vector3 goal = new Vector3(2.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[TestUnit\] Flow strict fail: start blocked"));
        Vector3 velocity = Vector3.one;
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out velocity));
        StringAssert.Contains("Flow strict fail: start blocked", ex.Message);
        Assert.AreEqual(Vector3.zero, velocity);
    }

    [Test]
    public void PortalGraph会避开同Sector内不可达出口()
    {
        const int width = 12;
        const int height = 4;
        bool[] walkable = new bool[width * height];

        for (int x = 0; x <= 3; x++)
            SetWalkable(walkable, width, x, 3);

        for (int x = 4; x <= 7; x++)
        {
            SetWalkable(walkable, width, x, 0);
            SetWalkable(walkable, width, x, 3);
        }

        for (int x = 8; x <= 11; x++)
        {
            SetWalkable(walkable, width, x, 0);
            SetWalkable(walkable, width, x, 3);
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 3.5f));
        Vector3 goal = new Vector3(10.5f, 0f, 3.5f);

        SimulateAgent(ctx, goal, 36, 0.2f, 2.4f);

        Assert.Greater(ctx.Position.x, 8.5f, $"应选择上方可达 portal 抵达目标附近，当前位置={ctx.Position}");
        Assert.AreEqual(3.5f, ctx.Position.z, 0.75f, $"不应被错误出口引到下方死路，当前位置={ctx.Position}");
    }

    [Test]
    public void 起点目标同Sector但局部不通时会经Portal绕路()
    {
        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];

        SetWalkable(walkable, width, 0, 1);
        SetWalkable(walkable, width, 3, 1);
        SetWalkable(walkable, width, 0, 4);
        SetWalkable(walkable, width, 1, 4);
        SetWalkable(walkable, width, 2, 4);
        SetWalkable(walkable, width, 3, 4);
        SetWalkable(walkable, width, 0, 3);
        SetWalkable(walkable, width, 3, 3);
        SetWalkable(walkable, width, 0, 2);
        SetWalkable(walkable, width, 3, 2);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        Vector3 goal = new Vector3(3.5f, 0f, 1.5f);

        Vector3 velocity = ResolveDeterministicFlowVelocityAfterQueue(ctx, goal, 2f, 1, out _);

        Assert.Greater(velocity.z, 0.5f, $"同 sector 内部不可达时应先经 portal 绕路，而不是报错或直冲墙，velocity={velocity}");
    }

    [Test]
    public void 跨Sector寻路在PortalTile内不会直冲最终目标而是沿可走走廊前进()
    {
        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];

        for (int y = 4; y <= 7; y++)
            SetWalkable(walkable, width, 1, y);
        for (int x = 1; x <= 6; x++)
            SetWalkable(walkable, width, x, 4);
        for (int y = 1; y <= 4; y++)
            SetWalkable(walkable, width, 6, y);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 6.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 1.5f);

        Vector3 velocity = ResolveDeterministicFlowVelocityAfterQueue(ctx, goal, 2f, 1, out _);

        Assert.Less(velocity.z, -0.8f, $"在 portal 前应先沿竖向走廊朝拐角推进，velocity={velocity}");
        Assert.AreEqual(0f, velocity.x, 0.35f, $"不应在 portal tile 内直接斜切向最终目标，velocity={velocity}");
    }

    [Test]
    public void 跨Sector首次权威移动直接使用正式PortalTile而不直冲最终目标()
    {
        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];

        for (int y = 4; y <= 7; y++)
            SetWalkable(walkable, width, 1, y);
        for (int x = 1; x <= 6; x++)
            SetWalkable(walkable, width, x, 4);
        for (int y = 1; y <= 4; y++)
            SetWalkable(walkable, width, 6, y);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 6.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 1.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 firstVelocity));

        Assert.Less(firstVelocity.z, -0.8f, $"正式 portal tile 应先沿竖向走廊朝 portal 前进，velocity={firstVelocity}");
        Assert.AreEqual(0f, firstVelocity.x, 0.35f, $"正式 portal tile 不应软视线斜切最终目标，velocity={firstVelocity}");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            ctx.LogicEntityId.Value,
            out string diagnostic));
        StringAssert.Contains("/cached=True/", diagnostic);
        StringAssert.Contains("/lastResult=direction=", diagnostic);
    }

    [Test]
    public void 首次提交的正式PortalTile按积分势负梯度输出浅斜向()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 30;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 60;
        const int height = 30;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        for (int y = 0; y < height; y++)
        {
            if (y != 7)
                walkable[29 + y * width] = false;
        }
        walkable[15 + 12 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(4.5f, 0f, 15.5f), false, 0, 0.18f);
        Vector3 goal = new Vector3(55.5f, 0f, 7.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            ctx.LogicEntityId.Value,
            out string diagnostic));

        StringAssert.Contains("/cached=True/", diagnostic);
        StringAssert.Contains("/lastResult=direction=", diagnostic);
        Assert.Greater(velocity.x, 1.5f, $"正式积分势的主下降轴应保持向右，velocity={velocity}, diagnostic={diagnostic}");
        Assert.Less(velocity.z, -0.5f, $"正式积分势应包含向下的次下降分量，velocity={velocity}, diagnostic={diagnostic}");
        Assert.Greater(velocity.x, Mathf.Abs(velocity.z) + 0.25f,
            $"正式 Eikonal 势应输出主轴占优的浅斜向，不能量化成 45 度，velocity={velocity}, diagnostic={diagnostic}");
    }

    [Test]
    public void 首次权威移动前会精确提交CurrentTile及其必要依赖()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 30;
        config.DeterministicFlowTileCommitQuota = 1;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 60;
        const int height = 30;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        for (int y = 0; y < height; y++)
        {
            if (y != 7)
                walkable[29 + y * width] = false;
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(4.5f, 0f, 15.5f), false, 0, 0.18f);
        Vector3 goal = new Vector3(55.5f, 0f, 7.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            ctx.LogicEntityId.Value,
            out string diagnostic));

        StringAssert.Contains("/cached=True/", diagnostic,
            $"首次权威移动不能消费 generic pending access 场，diagnostic={diagnostic}");
        StringAssert.Contains("/lastResult=direction=", diagnostic,
            $"首次权威移动必须直接消费正式 current tile，diagnostic={diagnostic}");
        Assert.Greater(velocity.sqrMagnitude, 0f, $"正式 current tile 应产生移动速度，diagnostic={diagnostic}");
        Assert.AreEqual(2, FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount(),
            "current portal tile 紧邻最终 Sector 时，只应精确提交 current tile 与 final tile 依赖。");
    }

    [Test]
    public void 首次权威移动只提交CurrentTile而保留后续Tile预热队列()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 30;
        config.DeterministicFlowTileCommitQuota = 1;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 90;
        const int height = 30;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(4.5f, 0f, 15.5f), false, 0, 0.18f);
        Vector3 goal = new Vector3(85.5f, 0f, 15.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            ctx.LogicEntityId.Value,
            out string diagnostic));

        StringAssert.Contains("/cached=True/", diagnostic);
        Assert.Greater(velocity.x, 1.5f, $"正式 current tile 应沿路径前进，velocity={velocity}, diagnostic={diagnostic}");
        Assert.AreEqual(1, FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount(),
            "非 final-adjacent 的 current tile 不应同步提交整条路径。");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), 0,
            "lookahead tile 必须继续留在预算预热队列。");
    }

    [Test]
    public void PortalCurrentTile首次提交后不因Lookahead晋升改变权威方向()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 30;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 60;
        const int height = 30;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        for (int y = 0; y < height; y++)
        {
            if (y != 7)
                walkable[29 + y * width] = false;
        }
        walkable[15 + 12 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(4.5f, 0f, 15.5f), false, 0, 0.18f);
        Vector3 goal = new Vector3(55.5f, 0f, 7.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 initialVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            ctx.LogicEntityId.Value,
            out string initialDiagnostic));
        StringAssert.Contains("/cached=True/", initialDiagnostic);

        ProcessFlowTileBuildQueueUntilTileReady(4, 15);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(512, 51.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 cachedVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            ctx.LogicEntityId.Value,
            out string cachedDiagnostic));
        StringAssert.Contains("/lastResult=direction=", cachedDiagnostic);

        float authorityAngle = Vector3.Angle(initialVelocity, cachedVelocity);
        Assert.LessOrEqual(authorityAngle, 5f,
            $"同位置、同路径只发生 lookahead 晋升时 current tile 权威方向必须连续，angle={authorityAngle:F3}, initial={initialVelocity}, cached={cachedVelocity}, initialDiagnostic={initialDiagnostic}, cachedDiagnostic={cachedDiagnostic}");
    }

    [Test]
    public void PortalTile使用DeterministicDirection不会斜切最终目标()
    {
        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];

        for (int y = 4; y <= 7; y++)
            SetWalkable(walkable, width, 1, y);
        for (int x = 1; x <= 6; x++)
            SetWalkable(walkable, width, x, 4);
        for (int y = 1; y <= 4; y++)
            SetWalkable(walkable, width, 6, y);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 6.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 1.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(1);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(32, 3.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));

        Assert.Less(velocity.z, -0.8f, $"当前格未积分时应沿有限邻居流向推进，不能用 LOS 斜切最终目标，velocity={velocity}");
        Assert.AreEqual(0f, velocity.x, 0.35f, $"当前格未积分时不应输出朝最终目标的横向分量，velocity={velocity}");
    }

    [Test]
    public void 单位位于当前Sector的Portal边界格时仍会继续穿过Portal()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(3.5f, 0f, 1.5f));
        Vector3 goal = new Vector3(7.5f, 0f, 1.5f);

        Vector3 velocity = ResolveDeterministicFlowVelocityAfterQueue(ctx, goal, 2f, 1, out string fixedDiagnostic);

        Assert.Greater(velocity.x, 0.8f, $"位于 portal 边界格时仍应继续向下个 sector 前进，velocity={velocity}, fixed={fixedDiagnostic}");
        Assert.AreEqual(0f, velocity.z, 0.1f, $"直走 portal 时不应产生异常侧偏，velocity={velocity}");

        ProcessFlowTileBuildQueueUntilTileCount(2);
        ctx.Position = new Vector3(4.5f, 0f, 1.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(128, 12.8f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out velocity));

        Assert.Greater(velocity.x, 0.8f, $"进入 portal 对侧后一帧也应继续前进，velocity={velocity}");
        Assert.AreEqual(0f, velocity.z, 0.1f, $"穿过 portal 后仍不应异常侧偏，velocity={velocity}");
    }

    [Test]
    public void 多Sector路径站在PortalGoalCell时不会把Portal当终点停住()
    {
        const int width = 12;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(3.5f, 0f, 1.5f));
        Vector3 goal = new Vector3(10.5f, 0f, 1.5f);

        Vector3 velocity = ResolveDeterministicFlowVelocityAfterQueue(ctx, goal, 2f, 1, out string fixedDiagnostic);

        Assert.Greater(velocity.x, 0.8f, $"站在 portal goal cell 上时应跨过 portal 继续前往后续 sector，velocity={velocity}, fixed={fixedDiagnostic}");
        Assert.AreEqual(0f, velocity.z, 0.1f, $"直线 portal handoff 不应产生异常侧偏，velocity={velocity}");
    }

    [Test]
    public void PortalSeed存储方向继承下游切向但窄孔权威Funnel保留当前Portal()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 8;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        for (int y = 1; y < height; y++)
            walkable[3 + y * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(2.5f, 0f, 0.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 3.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileReady(3, 0);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellStoredFlowDirection(3, 0, out Vector2 flow));
        Assert.Greater(flow.x, 0.6f, $"Portal seed 必须保持朝下游 sector 的穿越法向，flow={flow}");
        Assert.Greater(flow.y, 0.6f, $"Portal seed 必须继承下游 final tile 的切向梯度，不能在边界强制纯法向，flow={flow}");

        ctx.Position = new Vector3(3.5f, 0f, 0.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(256, 25.6f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(ctx.LogicEntityId.Value, out string diagnostic));
        Assert.Greater(velocity.x, 0.6f, $"站上 Portal seed 后权威速度必须继续穿越，velocity={velocity}, diagnostic={diagnostic}");
        Assert.AreEqual(0f, velocity.z, 0.1f,
            $"半径等于半格时单格 Portal 的收缩孔径只有中心点，权威速度必须纯法向穿越，velocity={velocity}, diagnostic={diagnostic}");
        StringAssert.Contains("/lastResult=corridor-funnel portalLookahead=rejected cornerPathIndex=0", diagnostic,
            $"站上 Portal seed 后 funnel 必须保留当前 Portal 作为几何约束，diagnostic={diagnostic}");
    }

    [Test]
    public void PortalSeed方向继承下游PortalAccess切向且保持穿越法向()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 12;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        for (int y = 1; y < height; y++)
            walkable[3 + y * width] = false;
        for (int y = 0; y < height - 1; y++)
            walkable[7 + y * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(2.5f, 0f, 0.5f));
        Vector3 goal = new Vector3(10.5f, 0f, 3.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileReady(3, 0);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellStoredFlowDirection(3, 0, out Vector2 flow));
        Assert.Greater(flow.x, 0.6f, $"Portal seed 必须保持朝下游 sector 的穿越法向，flow={flow}");
        Assert.Greater(flow.y, 0.6f, $"Portal seed 必须继承下游 portal-access 场的切向梯度，不能在边界强制纯法向，flow={flow}");
    }


    [Test]
    public void PortalTile积分应能从portal反向覆盖到当前格()
    {
        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];

        for (int y = 4; y <= 7; y++)
            SetWalkable(walkable, width, 1, y);
        for (int x = 1; x <= 6; x++)
            SetWalkable(walkable, width, x, 4);
        for (int y = 1; y <= 4; y++)
            SetWalkable(walkable, width, 6, y);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 6.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 1.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));

        Assert.Greater(velocity.magnitude, 0.1f, $"portal tile 应对当前格给出有效引导，velocity={velocity}");
        Assert.Less(velocity.z, 0f, $"起点应先沿走廊推进而不是停滞，velocity={velocity}");
    }

    [Test]
    public void 墙边成本梯度会让积分流场避开贴边路径()
    {
        const int width = 9;
        const int height = 5;
        bool[] walkable = new bool[width * height];

        for (int y = 1; y <= 3; y++)
        {
            for (int x = 0; x < width; x++)
                SetWalkable(walkable, width, x, y);
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext edgeAgent = CreateEntity(new Vector3(1.5f, 0f, 1.5f));
        Vector3 goal = new Vector3(7.5f, 0f, 3.5f);

        Vector3 velocity = ResolveDeterministicFlowVelocityAfterQueue(edgeAgent, goal, 2f, 1, out _);

        Assert.Greater(velocity.z, 0.25f, $"墙边格应受到成本梯度引导离开边缘，而不是只沿墙横走，velocity={velocity}");
        Assert.Greater(velocity.x, 0.25f, $"成本梯度不应让单位放弃朝目标推进，velocity={velocity}");

        ProcessFlowTileBuildQueueUntilTileCount(1);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlowDirection(1, 1, out Vector2 flow));
        string diag = FlowFieldCrowdMovementSystem.GetEditorTestCachedTileCellDiagnostic(1, 1);
        Assert.Greater(flow.x, 0.6f, $"flow pass 应按八邻居最低 integration 选择朝目标推进的斜向，flow={flow} diag={diag}");
        Assert.Greater(flow.y, 0.6f, $"flow pass 应按八邻居最低 integration 选择离墙的斜向，flow={flow} diag={diag}");
    }

    [Test]
    public void 积分势负梯度应输出浅斜向而不是量化成四十五度()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 30;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 30;
        const int height = 30;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        walkable[15 + 11 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(4.5f, 0f, 15.5f), false, 0, 0.18f);
        Vector3 goal = new Vector3(25.5f, 0f, 7.5f);

        Vector3 velocity = ResolveDeterministicFlowVelocityAfterQueue(ctx, goal, 2f, 1, out string diagnostic);

        Assert.Greater(velocity.x, 1.5f, $"积分势的主下降轴应保持向右，velocity={velocity}, diagnostic={diagnostic}");
        Assert.Less(velocity.z, -0.5f, $"积分势仍应包含向下的次下降分量，velocity={velocity}, diagnostic={diagnostic}");
        Assert.Greater(velocity.x, Mathf.Abs(velocity.z) + 0.25f,
            $"Eikonal 势应输出主轴占优的浅斜向，不能量化成 45 度，velocity={velocity}, diagnostic={diagnostic}");
    }

    [Test]
    public void 高速单位单Tick跨越多个导航格时窄路方向不会左右交替()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 12;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 48;
        const int height = 4;
        const float cellSize = 0.075f;
        const float speed = 6.9f;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, cellSize, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(cellSize * 0.5f, 0f, cellSize * 2.02f), false, 0, cellSize * 0.18f);
        Vector3 goal = new Vector3(cellSize * 47.5f, 0f, cellSize * 2f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 1f / 30f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, speed, out _));
        ProcessFlowTileBuildQueueUntilTileReady(0, 2);

        int previousLateralSign = 0;
        int lateralAlternations = 0;
        int abruptTurns = 0;
        Vector3 previousDirection = Vector3.zero;
        var trace = new System.Text.StringBuilder(2048);
        for (int frame = 2; frame <= 14; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame / 30f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, speed, out Vector3 velocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
                ctx.LogicEntityId.Value,
                out string diagnostic));
            Vector3 direction = velocity.normalized;
            int lateralSign = direction.z > 0.025f ? 1 : direction.z < -0.025f ? -1 : 0;
            if (lateralSign != 0 && previousLateralSign != 0 && lateralSign != previousLateralSign)
                lateralAlternations++;
            if (lateralSign != 0)
                previousLateralSign = lateralSign;
            if (previousDirection.sqrMagnitude > 0f && Vector3.Dot(previousDirection, direction) < Mathf.Cos(20f * Mathf.Deg2Rad))
                abruptTurns++;
            trace.Append("frame=").Append(frame)
                .Append(" position=").Append(ctx.Position)
                .Append(" velocity=").Append(velocity)
                .Append(" diagnostic=").Append(diagnostic)
                .AppendLine();
            previousDirection = direction;
            ctx.Position += velocity / 30f;
        }

        Assert.LessOrEqual(lateralAlternations, 1,
            $"LvTest 比例下单 Tick 跨约三格时不应左右交替，alternations={lateralAlternations}, abrupt={abruptTurns}, position={ctx.Position}\n{trace}");
        Assert.Zero(abruptTurns,
            $"LvTest 比例下单 Tick 跨约三格时不应产生超过 20 度的突转，alternations={lateralAlternations}, abrupt={abruptTurns}, position={ctx.Position}\n{trace}");
    }

    [Test]
    public void HighSpeedAlternatingPortalCorridorDoesNotExposeSectorZigzag()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 12;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 60;
        const int height = 36;
        const float cellSize = 0.075f;
        const float speed = 6.9f;
        bool[] walkable = new bool[width * height];
        Vector2Int[] walkableSectors =
        {
            new Vector2Int(0, 2),
            new Vector2Int(0, 1),
            new Vector2Int(1, 1),
            new Vector2Int(2, 1),
            new Vector2Int(2, 0),
            new Vector2Int(3, 0),
            new Vector2Int(4, 0)
        };
        for (int i = 0; i < walkableSectors.Length; i++)
        {
            int startX = walkableSectors[i].x * config.SectorSizeInCells;
            int startY = walkableSectors[i].y * config.SectorSizeInCells;
            for (int y = startY; y < startY + config.SectorSizeInCells; y++)
            {
                for (int x = startX; x < startX + config.SectorSizeInCells; x++)
                    SetWalkable(walkable, width, x, y);
            }
        }

        for (int x = 0; x < 6; x++)
            walkable[x + 24 * width] = false;
        for (int y = 12; y < 18; y++)
            walkable[11 + y * width] = false;
        for (int y = 18; y < 24; y++)
            walkable[23 + y * width] = false;
        for (int x = 30; x < 36; x++)
            walkable[x + 12 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, cellSize, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(
            new Vector3(cellSize * 3.5f, 0f, cellSize * 30.5f),
            false,
            0,
            cellSize * 0.18f);
        Vector3 goal = new Vector3(cellSize * 54.5f, 0f, cellSize * 6.5f);
        Vector3 previousDirection = Vector3.zero;
        int abruptTurns = 0;
        int previousTurnSign = 0;
        int previousTurnFrame = int.MinValue;
        int rapidTurnSignAlternations = 0;
        var trace = new System.Text.StringBuilder(4096);

        for (int frame = 1; frame <= 30; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame / 30f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, speed, out Vector3 velocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
                ctx.LogicEntityId.Value,
                out string diagnostic));
            Vector3 direction = velocity.normalized;
            if (previousDirection.sqrMagnitude > 0f)
            {
                if (Vector3.Dot(previousDirection, direction) < Mathf.Cos(20f * Mathf.Deg2Rad))
                    abruptTurns++;
                float turnCross = previousDirection.x * direction.z - previousDirection.z * direction.x;
                int turnSign = turnCross > 0.05f ? 1 : turnCross < -0.05f ? -1 : 0;
                if (turnSign != 0 && previousTurnSign != 0 && turnSign != previousTurnSign)
                {
                    if (frame - previousTurnFrame <= 2)
                        rapidTurnSignAlternations++;
                }
                if (turnSign != 0)
                {
                    previousTurnSign = turnSign;
                    previousTurnFrame = frame;
                }
            }
            trace.Append("frame=").Append(frame)
                .Append(" position=").Append(ctx.Position)
                .Append(" velocity=").Append(velocity)
                .Append(" diagnostic=").Append(diagnostic)
                .AppendLine();
            previousDirection = direction;
            ctx.Position += velocity / 30f;
            if ((goal - ctx.Position).sqrMagnitude <= speed * speed / 900f)
                break;
        }

        Assert.LessOrEqual(rapidTurnSignAlternations, 1,
            $"A portal corridor may contain real corners, but sector-local fields must not expose a rapid alternating zigzag. rapidAlternations={rapidTurnSignAlternations}, abrupt={abruptTurns}, position={ctx.Position}\n{trace}");
    }

    [Test]
    public void 高速单位沿双Lane边界移动时空间梯度不会逐帧翻转()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 12;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 48;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 2.02f), false, 0, 0.18f);
        Vector3 goal = new Vector3(47.5f, 0f, 2f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 1f / 30f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 7f, out _));
        ProcessFlowTileBuildQueueUntilTileReady(0, 2);

        int previousLateralSign = 0;
        int lateralAlternations = 0;
        for (int frame = 2; frame <= 40; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame / 30f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 7f, out Vector3 velocity));
            int lateralSign = velocity.z > 0.05f ? 1 : velocity.z < -0.05f ? -1 : 0;
            if (lateralSign != 0 && previousLateralSign != 0 && lateralSign != previousLateralSign)
                lateralAlternations++;
            if (lateralSign != 0)
                previousLateralSign = lateralSign;
            ctx.Position += velocity / 30f;
        }

        Assert.LessOrEqual(lateralAlternations, 1,
            $"空间连续采样应让高速单位收敛到双 lane 边界，不能在相邻格梯度间逐帧翻转，alternations={lateralAlternations}, position={ctx.Position}");
    }

    [Test]
    public void 高速单位在低预热配额下始终消费正式CurrentTile且不会逐帧翻转()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 12;
        SetNavigationWorkQuotas(config, 1);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 48;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 2.02f), false, 0, 0.18f);
        Vector3 goal = new Vector3(47.5f, 0f, 2f);
        int previousLateralSign = 0;
        int lateralAlternations = 0;
        for (int frame = 1; frame <= 40; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame / 30f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 7f, out Vector3 velocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
                ctx.LogicEntityId.Value,
                out string diagnostic));
            StringAssert.Contains("/cached=True/", diagnostic,
                $"每个新 current tile 都必须在本 Tick 权威消费前精确提交，diagnostic={diagnostic}");
            int lateralSign = velocity.z > 0.05f ? 1 : velocity.z < -0.05f ? -1 : 0;
            if (lateralSign != 0 && previousLateralSign != 0 && lateralSign != previousLateralSign)
                lateralAlternations++;
            if (lateralSign != 0)
                previousLateralSign = lateralSign;
            ctx.Position += velocity / 30f;
        }

        Assert.LessOrEqual(lateralAlternations, 1,
            $"正式 current tile 空间采样应让高速单位收敛到双 lane 边界，不能逐帧翻转，alternations={lateralAlternations}, position={ctx.Position}");
    }

    [Test]
    public void 开阔格由DeterministicDirection直接给出方向()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 16;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 16;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(6.5f, 0f, 8.5f));
        Vector3 goal = new Vector3(10.5f, 0f, 8.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(1);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlags(8, 8, out bool hasLos, out _, out bool pathable));
        Assert.IsTrue(pathable, "可走格应写入 FlowField pathable flag");
        Assert.IsFalse(hasLos, "旧 float LOS flag 不应再参与 tile 运行时状态");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlowDirection(8, 8, out Vector2 flow));
        Assert.Greater(flow.x, 0.9f, $"开阔格应直接使用 deterministic direction 朝目标推进，flow={flow}");
    }

    [Test]
    public void 不可达可走格不应输出FlowDirection()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                SetWalkable(walkable, width, x, y);
        }

        for (int x = 0; x < width; x++)
            walkable[x + 3 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 6.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 6.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(1);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlags(1, 1, out _, out _, out bool pathable));
        Assert.IsTrue(pathable, "测试格必须是可走但与目标隔离的格子");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellStoredFlowDirection(1, 1, out Vector2 storedFlow));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlowDirection(1, 1, out Vector2 runtimeFlow));
        string diag = FlowFieldCrowdMovementSystem.GetEditorTestCachedTileCellDiagnostic(1, 1);

        Assert.AreEqual(Vector2.zero, storedFlow, $"不可达格不应写入 stored flow，diag={diag}");
        Assert.AreEqual(Vector2.zero, runtimeFlow, $"不可达格运行期不应输出方向，diag={diag}");
    }

    [Test]
    public void ClearTile不构建运行时FloatIntegration且支持按需查看DeterministicCost()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 48;
        const int height = 24;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        for (int y = 8; y < 15; y++)
            walkable[35 + y * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(9.5f, 0f, 11.5f));
        Vector3 goal = new Vector3(46.5f, 0f, 15.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(5);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorClearCostState(9, 11, out bool leftClear));
        Assert.IsTrue(leftClear, "内部左侧 sector 没有墙和软成本，应保持 clear cost 状态");

        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestReleasedIntegrationTileCount(), "权威提交不应先分配再释放 float integration payload");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestDebugIntegrationPayloadTileCount(), "未显式请求 debug rebuild 时不应保留 debug integration payload");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryRebuildEditorDebugTileIntegration(9, 11), "deterministic tile 应支持显式按需映射 debug integration heatmap payload");
        Assert.GreaterOrEqual(FlowFieldCrowdMovementSystem.GetEditorTestDebugIntegrationPayloadTileCount(), 1, "显式 debug rebuild 后应只保留由整数 cost 映射的调试 payload");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDebugIntegrationCost(9, 11, out float debugCost));
        Assert.IsFalse(float.IsPositiveInfinity(debugCost), $"整数 cost 映射后应能读到有限调试成本，cost={debugCost}");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellStoredFlowDirection(9, 11, out Vector2 storedFlow));
        Assert.Greater(storedFlow.sqrMagnitude, 0.0001f, "clear tile 应直接缓存 deterministic direction，不依赖 descriptor float 计算");
    }

    [Test]
    public void UnknownAgentType不会被静默解析成默认移动类型()
    {
        const int width = 4;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
                MAEntity.UnknownNavAgentTypeId,
                width,
                height,
                1f,
                Vector3.zero,
                walkable));

        StringAssert.Contains("explicit agentTypeId is Unknown", ex.Message);
    }

    [Test]
    public void 宽PortalWindow会按最大宽度拆成多个Graph节点()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        config.PortalMaxWindowWidthCells = 2;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 8;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(7.5f, 0f, 1.5f), 2f, out _));

        Assert.AreEqual(2, FlowFieldCrowdMovementSystem.GetEditorTestPortalCount(), "4 格宽 sector 边界应按 PortalMaxWindowWidthCells=2 拆成两个 portal graph 节点");
    }

    [Test]
    public void 有墙Sector即使成本清晰也不能当ClearFlowTile跳过方向预写()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        for (int y = 1; y <= 5; y++)
            walkable[3 + y * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 0.5f));
        Vector3 goal = new Vector3(7.5f, 0f, 7.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(1);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorClearCostState(0, 0, out bool clearCost));
        Assert.IsFalse(clearCost, "墙边 soft cost blur 会进入 CostField，含墙 sector 不应再被标记为 clear cost field");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorClearFlowTileState(0, 0, out bool clearFlow));
        Assert.IsFalse(clearFlow, "含墙或不可通行邻接的 sector 不是论文意义上的 clear flow tile");

        bool foundStoredFlow = false;
        for (int y = 0; y < height && !foundStoredFlow; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlags(x, y, out bool hasLos, out _, out bool pathable));
                if (!pathable || hasLos)
                    continue;

                Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellStoredFlowDirection(x, y, out Vector2 storedFlow));
                if (storedFlow.sqrMagnitude > 0.0001f)
                {
                    foundStoredFlow = true;
                    break;
                }
            }
        }

        Assert.IsTrue(foundStoredFlow, "非 clear flow tile 应在构建期写入 stored flow direction");
    }

    [Test]
    public void CostStamp会进入CostField并影响积分流场()
    {
        const int width = 7;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(1001, new Vector3(3.5f, 0f, 2.5f), new Vector3(1.49f, 0f, 0.49f), 30);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 pendingVelocity));
        Assert.Greater(pendingVelocity.magnitude, 0.2f, "cost dirty 提交前可沿已提交导航继续推进，不应人为停帧。");
        ProcessRuntimeDirtyQueueUntilReady(2);
        Vector3 velocity = ResolveDeterministicFlowVelocityAfterQueue(ctx, goal, 2f, 512, out string fixedDiagnostic);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            ctx.LogicEntityId.Value,
            out fixedDiagnostic));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 2, out byte cost));
        Assert.AreEqual(30, cost, "cost stamp 应写入 CostField，而不是只存在注册表里");
        Assert.Greater(velocity.x, 0.5f, $"高成本带不应阻断朝目标推进，velocity={velocity}");
        Assert.Greater(Mathf.Abs(velocity.z), 0.2f, $"高成本带应让 flow/integration 产生绕行分量，velocity={velocity}, fixed={fixedDiagnostic}");
    }

    [Test]
    public void AuthoredCostField会进入CostField并影响积分流场()
    {
        const int width = 7;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        byte[] costs = new byte[width * height];
        for (int i = 0; i < walkable.Length; i++)
        {
            walkable[i] = true;
            costs[i] = 1;
        }

        for (int x = 2; x <= 4; x++)
            costs[x + 2 * width] = 30;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(int.MinValue + 1, width, height, 1f, Vector3.zero, walkable, null, costs);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Vector3 velocity = ResolveDeterministicFlowVelocityAfterQueue(ctx, goal, 2f, 2, out string fixedDiagnostic);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            ctx.LogicEntityId.Value,
            out fixedDiagnostic));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 2, out byte cost));
        Assert.AreEqual(30, cost, "authored source cost 应写入 CostField，而不是运行期退回全 1 成本。");
        Assert.Greater(velocity.x, 0.5f, $"authored 高成本带不应阻断朝目标推进，velocity={velocity}");
        Assert.Greater(Mathf.Abs(velocity.z), 0.2f, $"authored 高成本带应让 flow/integration 产生绕行分量，velocity={velocity}, fixed={fixedDiagnostic}");
    }

    [Test]
    public void CostStamp只影响匹配MovementType的CostField()
    {
        const int width = 9;
        const int height = 7;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(1002, 12345, new Vector3(4.5f, 0f, 3.5f), new Vector3(0.49f, 0f, 0.49f), 40);

        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 3.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(7.5f, 0f, 3.5f), 2f, out _));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(4, 3, out byte cost));
        Assert.AreEqual(1, cost, "不匹配 movement type 的 cost stamp 不应污染当前 agent type 的 CostField");
    }

    [Test]
    public void GridCostStamp会按逐格成本写入CostField()
    {
        const int width = 6;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        byte[] costs =
        {
            5, 7,
            11, 13
        };

        FlowFieldCrowdMovementSystem.RegisterGridCostStamp(1004, new Vector3(2f, 0f, 1f), 1f, 2, 2, costs);
        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(5.5f, 0f, 1.5f), 2f, out _));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(2, 1, out byte cost00));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 1, out byte cost10));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(2, 2, out byte cost01));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 2, out byte cost11));
        Assert.AreEqual(5, cost00, "grid cost stamp 左下格成本应写入 CostField");
        Assert.AreEqual(7, cost10, "grid cost stamp 右下格成本应写入 CostField");
        Assert.AreEqual(11, cost01, "grid cost stamp 左上格成本应写入 CostField");
        Assert.AreEqual(13, cost11, "grid cost stamp 右上格成本应写入 CostField");
    }

    [Test]
    public void AuthoredGridAnchor高度不会隐式转换成CostField成本()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        Vector3[] anchors = new Vector3[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                walkable[index] = true;
                anchors[index] = new Vector3(x + 0.5f, x * 0.5f, y + 0.5f);
            }
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable, anchors);
        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 4.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(6.5f, 0f, 4.5f), 2f, out _));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(4, 4, out byte cost));
        Assert.AreEqual(1, cost, $"authored grid 锚点高度不应作为隐藏坡度成本写入 CostField，cost={cost}");
    }

    [Test]
    public void 清成本Sector会记录ClearCost状态并随CostStamp重建()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 20;
        const int height = 20;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(8.5f, 0f, 9.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(11.5f, 0f, 9.5f), 2f, out _));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorClearCostState(9, 9, out bool leftClearBefore));
        Assert.IsTrue(leftClearBefore, "没有额外成本的 sector 应标记为 clear cost field");

        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(1003, new Vector3(9.5f, 0f, 9.5f), new Vector3(0.49f, 0f, 0.49f), 20);
        for (int i = 0; i < 32 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorClearCostState(9, 9, out bool leftClearAfterStamp));
        Assert.IsFalse(leftClearAfterStamp, "被 cost stamp 写入额外成本的 sector 不应继续标记为 clear");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorClearCostState(13, 9, out bool rightClearAfterStamp));
        Assert.IsTrue(rightClearAfterStamp, "未受 cost stamp 影响的相邻 sector 应保持 clear");

        FlowFieldCrowdMovementSystem.UnregisterCostStamp(1003);
        for (int i = 0; i < 32 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorClearCostState(9, 9, out bool leftClearAfterRemove));
        Assert.IsTrue(leftClearAfterRemove, "撤销 cost stamp 并完成 dirty rebuild 后应恢复 clear cost field");
    }

    [Test]
    public void RuntimeDirtyQueue会原子提交运行时障碍重建()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(7.5f, 0f, 1.5f), 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 1, out byte beforeCost));

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9001, new Vector3(3.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 1, out byte pendingCost));

        ProcessRuntimeDirtyQueueUntilReady(2);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 1, out byte committedCost));
        Assert.Less(beforeCost, 255, "测试初始格应是可走 cost");
        Assert.AreEqual(beforeCost, pendingCost, "runtime dirty job 完成前不应把半更新 working world 暴露给导航 world");
        Assert.AreEqual(255, committedCost, "runtime dirty queue 完成后应原子提交运行时障碍到 cost field");
    }

    [Test]
    public void RuntimeDirty建筑障碍重建后保留地面锚点高度()
    {
        const int width = 8;
        const int height = 3;
        const float groundHeight = 2.75f;
        bool[] walkable = new bool[width * height];
        Vector3[] anchors = new Vector3[walkable.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = x + y * width;
                walkable[index] = true;
                anchors[index] = new Vector3(x + 0.5f, groundHeight, y + 0.5f);
            }
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable, anchors);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
            new Vector3(1.5f, 0f, 1.5f),
            0,
            0f,
            0f,
            out Vector3 before));
        Assert.AreEqual(groundHeight, before.y, 0.0001f);

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(
            9002,
            new Vector3(4.5f, 0f, 1.5f),
            new Vector3(0.49f, 1f, 0.49f));
        for (int i = 0; i < 16 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
            new Vector3(1.5f, 0f, 1.5f),
            0,
            0f,
            0f,
            out Vector3 after));
        Assert.AreEqual(groundHeight, after.y, 0.0001f, "建筑障碍动态重建不得丢失未阻塞格的真实地面锚点高度");
        Assert.IsFalse(FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
            new Vector3(4.5f, 0f, 1.5f),
            0,
            0f,
            0f,
            out _), "建筑占用格在动态重建后必须保持不可部署、不可通行");
    }

    [Test]
    public void ColliderObstacle注册使用Transform世界几何而非滞后PhysicsBounds()
    {
        const int width = 8;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(7.5f, 0f, 1.5f), 2f, out _));

        GameObject managerObject = new GameObject("GroupMoveManagerTest");
        GroupMoveManager manager = managerObject.AddComponent<GroupMoveManager>();
        GameObject root = new GameObject("BuildingRoot");
        GameObject autoBox = new GameObject("_AutoBox_Test");
        try
        {
            root.transform.position = new Vector3(5f, 0f, 1f);
            autoBox.transform.SetParent(root.transform, false);
            autoBox.transform.localPosition = new Vector3(0.5f, 0f, 0.5f);
            BoxCollider collider = autoBox.AddComponent<BoxCollider>();
            collider.center = Vector3.up * 0.5f;
            collider.size = Vector3.one;

            Bounds resolvedBounds = GroupMoveManager.ResolveColliderWorldBounds(collider);
            Assert.AreEqual(new Vector3(5.5f, 0.5f, 1.5f), resolvedBounds.center, "重叠检测必须拿到当前 Transform 对应的世界 Bounds");
            Assert.AreEqual(Vector3.one, resolvedBounds.size, "世界 Bounds 尺寸应与未缩放 BoxCollider 一致");

            manager.RegisterColliderObstacle(9401, collider);

            for (int i = 0; i < 16 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
                FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(5, 1, out byte worldCellCost));
            Assert.AreEqual(255, worldCellCost, "AutoBox 应按当前 Transform 世界坐标阻塞建筑所在格");
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(0, 0, out byte originCellCost));
            Assert.Less(originCellCost, 255, "AutoBox 不应因滞后的 collider.bounds 误注册到原点附近");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(autoBox);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void 导航查询不会同步DrainRuntimeDirtyJob()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 32);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 12;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        Vector3 goal = new Vector3(11.5f, 0f, 1.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 beforeVelocity));
        Assert.Greater(beforeVelocity.x, 0f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(5, 1, out byte beforeCost));

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9101, new Vector3(5.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "注册运行时障碍后应只标记 dirty，等待 runtime queue 分帧提交");

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 pendingVelocity));
        Assert.Greater(pendingVelocity.x, 0f, "runtime dirty 提交前，无关区域的单位应继续使用已提交 world 移动");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "导航查询不应同步 drain runtime dirty job");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(5, 1, out byte duringQueryCost));
        Assert.AreEqual(beforeCost, duringQueryCost, "runtime dirty commit 前查询应继续使用已提交的旧 world，而不是同步改写 CostField");
    }

    [Test]
    public void RuntimeDirtyCommit不会留下旧FloatTileJob且会清理共享场Job()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 32);
        config.SharedGoalBuildOperationQuota = 1;
        config.SectorSizeInCells = 48;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 96;
        const int height = 48;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 23.5f));
        SimEntityContext target = CreateEntity(new Vector3(95.5f, 0f, 23.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount(), 0, "低预算下应留下 pending shared goal job");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), "fixed tile 在权威提交后立即完成，不应留下旧 float tile job");

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9301, new Vector3(47.5f, 0f, 23.5f), new Vector3(0.49f, 0f, 0.49f));
        for (int i = 0; i < 4096; i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount(), "dirty commit 后受影响的 shared goal job 不能继续提交旧结果");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), "dirty commit 后受影响的 flow tile job 不能继续提交旧结果");
    }

    [Test]
    public void RuntimeDirtyQueue重排不会丢失已Pending的DirtySector()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 32);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 32;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(31.5f, 0f, 1.5f), 2f, out _));

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9101, new Vector3(5.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f));
        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9102, new Vector3(25.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f));

        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "runtime dirty job 应完成提交后再验证 CostField，避免读取旧 world");

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(5, 1, out byte firstCost));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(25, 1, out byte secondCost));
        Assert.AreEqual(255, firstCost, "pending job 被新 dirty 重排时，旧 dirty sector 不能丢失");
        Assert.AreEqual(255, secondCost, "新 dirty sector 也必须进入重排后的 runtime dirty job");
    }

    [Test]
    public void RuntimeDirtyQueue会分帧重建IslandField()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 32);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 9;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(8.5f, 0f, 1.5f), 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestIslandFieldValue(0, 1, out int initialLeftIsland, out int initialIslandCount));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestIslandFieldValue(8, 1, out int initialRightIsland, out _));

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9201, new Vector3(4.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f));
        for (int i = 0; i < 32; i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestIslandFieldValue(0, 1, out int committedLeftIsland, out int committedIslandCount));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestIslandFieldValue(8, 1, out int committedRightIsland, out _));
        Assert.AreEqual(1, initialIslandCount, "封堵前整条走廊应是单连通 island");
        Assert.AreEqual(initialLeftIsland, initialRightIsland, "封堵前左右两端应连通");
        Assert.AreEqual(2, committedIslandCount, "runtime dirty island field 分帧完成后应反映封堵造成的两个连通分支");
        Assert.AreNotEqual(committedLeftIsland, committedRightIsland, "封堵后左右两端不应仍在同一 island");
    }

    [Test]
    public void RuntimeDirtyPortalTransitions_ProcessAtMostFixedSourceQuotaPerQueueTick()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        SetNavigationWorkQuotas(config, 1_000_000);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 64;
        const int height = 64;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(
            9401,
            new Vector3(width * 0.5f, 0f, height * 0.5f),
            new Vector3(width * 0.5f, 0f, height * 0.5f),
            2);

        int firstAccessCount = 0;
        for (int i = 0; i < 1024 && firstAccessCount == 0; i++)
        {
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
            Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
            firstAccessCount = FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyPortalGraphAccessCount();
        }
        Assert.AreEqual(16, firstAccessCount, "The first queue tick must stop after the fixed portal-source quota.");

        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        int secondAccessCount = FlowFieldCrowdMovementSystem.GetEditorTestPendingRuntimeDirtyPortalGraphAccessCount();
        Assert.AreEqual(16, secondAccessCount - firstAccessCount, "A later queue tick must use the same deterministic portal-source quota.");

        ProcessRuntimeDirtyQueueUntilReady(3);
    }

    [Test]
    public void WorldBuildQueue会构建脏World但不暴露半成品()
    {
        const int width = 6;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestWorld(), "world build queue 处理前不应暴露半成品 world");

        FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
        ProcessWorldBuildQueueUntilReady();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestWorld(), "world build queue 完成后应原子提交 world");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestIslandFieldValue(0, 0, out int islandId, out int islandCount));
        Assert.AreEqual(1, islandId);
        Assert.AreEqual(1, islandCount);
    }

    [Test]
    public void WorldBuildQueue会分帧完成FullWorldBuild后再原子提交()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 32);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 48;
        const int height = 24;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestWorld(), "world build job 未完成前不应暴露半成品 world");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild(), "低预算下 full world build 应保留 pending job，而不是同步一次做完");

        for (int i = 0; i < 2048 && FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild(); i++)
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();

        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild(), "world build queue 应在预算帧内最终完成");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestWorld(), "完成后才应原子提交 world");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestIslandFieldValue(0, 0, out int islandId, out int islandCount));
        Assert.AreEqual(1, islandId);
        Assert.AreEqual(1, islandCount);
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPortalCount(), 0, "full world build 完成后应具备 portal graph");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestSectorPortalAccessCacheCount(), 0, "portal access integration 应由 world build 阶段预构建，不应等查询热路径同步生成");
    }

    [Test]
    public void WorldBuildOperationQuota不受Cpu耗时影响()
    {
        int callsWithoutDelay = CountWorldBuildCallsWithCpuDelay(0);
        int callsWithDelay = CountWorldBuildCallsWithCpuDelay(10_000);

        Assert.Greater(callsWithoutDelay, 1, "测试 world 必须跨多个 quota Tick 才能覆盖分帧进度。");
        Assert.AreEqual(callsWithoutDelay, callsWithDelay, "CPU 耗时只能影响真实执行时长，不能改变 world 完成的逻辑 Tick。");
    }

    [Test]
    public void MinHeap等成本节点按稳定Index出队()
    {
        int[] order = FlowFieldCrowdMovementSystem.GetEditorTestMinHeapPopOrder(
            new[] { 9, 3, 7, 1, 5 },
            new[] { 4f, 4f, 4f, 4f, 4f });

        CollectionAssert.AreEqual(new[] { 1, 3, 5, 7, 9 }, order);
    }

    [Test]
    public void DeterministicCostHeap等成本节点按稳定Index出队()
    {
        int[] order = FlowFieldCrowdMovementSystem.GetEditorTestDeterministicCostHeapPopOrder(
            new[] { 9, 3, 7, 1, 5 },
            new[] { 4096L, 4096L, 4096L, 4096L, 4096L });

        CollectionAssert.AreEqual(new[] { 1, 3, 5, 7, 9 }, order);
    }

    [Test]
    public void PortalAccessCache使用整数权威势能且保持下坡成本()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 16;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        walkable[3 + 3 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        int portalAccessCount = FlowFieldCrowdMovementSystem.GetEditorTestSectorPortalAccessCacheCount();
        Assert.Greater(portalAccessCount, 0, "world build 应预构建 sector portal access 场");
        Assert.AreEqual(portalAccessCount, FlowFieldCrowdMovementSystem.GetEditorTestDeterministicSectorPortalAccessCacheCount(), "每个 portal access cache 都必须具备整数权威成本源");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestInvalidDeterministicPortalTransitionCount(), "所有 portal transition 都必须在 world commit 后具备有限整数成本");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFirstPortalForSector(0, out int portalId, out int oppositeSectorId));
        Assert.AreEqual(1, oppositeSectorId);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorPortalAccessCostRaw(0, portalId, 0, 3, out long farCost));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorPortalAccessCostRaw(0, portalId, 6, 3, out long nearCost));
        Assert.AreNotEqual(long.MaxValue, farCost, $"远端可走格应具备有限整数 portal access cost，cost={farCost}");
        Assert.AreNotEqual(long.MaxValue, nearCost, $"portal 附近可走格应具备有限整数 portal access cost，cost={nearCost}");
        Assert.Less(nearCost, farCost, $"整数权威场必须保持朝 portal 下坡的成本关系，near={nearCost} far={farCost}");

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorPortalAccessCostRaw(0, portalId, 3, 3, out long blockedCost));
        Assert.AreEqual(long.MaxValue, blockedCost, $"不可走格应保持整数 INF，cost={blockedCost}");
    }

    [Test]
    public void Portal运行时状态只保留整数Access和DeterministicCost()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 12;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        walkable[1 + width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFirstPortalForSector(0, out int portalId, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorPortalAccessCostRaw(0, portalId, 0, 3, out long accessBefore));
        Assert.AreNotEqual(long.MaxValue, accessBefore);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryBuildEditorTestPortalPath(0, 3, 11, 3, out int[] pathBefore));
        Assert.IsNotEmpty(pathBefore);

        Type movementSystemType = typeof(FlowFieldCrowdMovementSystem);
        Type accessEntryType = movementSystemType.GetNestedType("SectorPortalAccessEntry", BindingFlags.NonPublic);
        Type transitionType = movementSystemType.GetNestedType("PortalTransition", BindingFlags.NonPublic);
        Assert.IsNotNull(accessEntryType);
        Assert.IsNotNull(transitionType);
        Assert.IsNull(accessEntryType.GetField("QuantizedIntegration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        Assert.IsNull(accessEntryType.GetField("IntegrationScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        Assert.IsNull(transitionType.GetField("Cost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorPortalAccessCostRaw(0, portalId, 0, 3, out long accessAfter));
        Assert.AreEqual(accessBefore, accessAfter, "portal access 权威成本只能读取 deterministic integration。");

        FlowFieldCrowdMovementSystem.ClearEditorTestSectorPathCache();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryBuildEditorTestPortalPath(0, 3, 11, 3, out int[] pathAfter));
        CollectionAssert.AreEqual(pathBefore, pathAfter, "portal A* 必须只读取 deterministic integration 和 DeterministicCost。");
    }

    [Test]
    public void PortalArray按PortalId构造且不受Lookup插入顺序影响()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 16;
        const int height = 12;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        walkable[6 + 5 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        ulong hashBefore = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();
        int portalCount = FlowFieldCrowdMovementSystem.GetEditorTestPortalCount();
        int[] portalIds = FlowFieldCrowdMovementSystem.RebuildEditorTestPortalArrayWithReverseLookupInsertion();
        ulong hashAfter = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();

        Assert.AreEqual(portalCount, portalIds.Length);
        Assert.Greater(portalIds.Length, 1, "测试地图必须生成多个 portal 才能覆盖反序插入。");
        for (int i = 1; i < portalIds.Length; i++)
            Assert.Less(portalIds[i - 1], portalIds[i], "committed portal array 必须严格按 PortalId 递增。");
        Assert.AreEqual(hashBefore, hashAfter, "PortalsById 的插入顺序不得改变 committed world authority hash。");
    }

    [Test]
    public void CommittedCostField按SectorChunk存储而不是整图数组()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 16;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        walkable[3 + 3 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestCommittedFullCostField(), "committed world 不应长期保留整图 CostField");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestCommittedSectorCostChunkCount(), 0, "包含障碍/软成本的 sector 应保存局部 cost chunk");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(10, 3, out byte clearCost));
        Assert.AreEqual(1, clearCost, "clear sector 应通过静态语义读出普通成本 1");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 3, out byte blockedCost));
        Assert.AreEqual(255, blockedCost, "chunk sector 内不可走格应保持硬阻挡成本 255");
    }

    [Test]
    public void IslandField会为单IslandSector记录UniformIslandId()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 4;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        for (int i = 0; i < 32 && !FlowFieldCrowdMovementSystem.HasEditorTestWorld(); i++)
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorUniformIslandId(0, 0, out int uniformIslandId));
        Assert.AreEqual(1, uniformIslandId, "单 island sector 应记录统一 island id，避免每次都查格子 island field");

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.ClearEditorTestClock();
        FlowFieldCrowdMovementSystem.SetConfig(config);

        bool[] splitWalkable = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            SetWalkable(splitWalkable, width, 0, y);
            SetWalkable(splitWalkable, width, 3, y);
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, splitWalkable);
        for (int i = 0; i < 32 && !FlowFieldCrowdMovementSystem.HasEditorTestWorld(); i++)
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestIslandFieldValue(0, 0, out _, out int islandCount));
        Assert.AreEqual(2, islandCount, "测试地图应在同一个 sector 内形成两个 island");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorUniformIslandId(0, 0, out int mixedIslandId));
        Assert.AreEqual(-1, mixedIslandId, "混合 island sector 必须保留逐格 island field 判断");
    }

    [Test]
    public void 大单位MovementType会扩大墙边Cost缓冲()
    {
        const int width = 9;
        const int height = 9;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext small = CreateEntity(new Vector3(4.5f, 0f, 4.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(small, new Vector3(7.5f, 0f, 4.5f), 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(4, 3, out byte smallCost));

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.ClearEditorTestClock();
        FlowFieldCrowdMovementSystem.SetConfig(CreateConfig());
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(0, 2.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext large = CreateEntity(new Vector3(4.5f, 0f, 4.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(large, new Vector3(7.5f, 0f, 4.5f), 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(4, 3, out byte largeCost));

        Assert.AreEqual(1, smallCost, $"默认单位半径下该格不应进入墙边缓冲，smallCost={smallCost}");
        Assert.Greater(largeCost, smallCost, $"大单位 movement type 应扩大墙边 CostField 缓冲，small={smallCost} large={largeCost}");
    }

    [Test]
    public void 墙距Cost定点边界和中点舍入保持确定性()
    {
        long oneCell = Fix64.One.RawValue;
        long blurRadius = ((Fix64)9 / 4).RawValue;

        Assert.AreEqual(1, FlowFieldCrowdMovementSystem.GetEditorTestWallCostPenalty(oneCell, blurRadius, 0, 1));
        Assert.AreEqual(1, FlowFieldCrowdMovementSystem.GetEditorTestWallCostPenalty(5793L, blurRadius, 0, 1));
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestWallCostPenalty(2L * oneCell, blurRadius, 0, 1));

        long threeCells = 3L * oneCell;
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestWallCostPenalty(2L * oneCell, threeCells, 0, 1), "0.5 应按 midpoint-to-even 舍入到 0，与原 Mathf.RoundToInt 语义一致。");
        Assert.AreEqual(2, FlowFieldCrowdMovementSystem.GetEditorTestWallCostPenalty(2L * oneCell, threeCells, 1, 2), "1.5 应按 midpoint-to-even 舍入到 2，与原 Mathf.RoundToInt 语义一致。");
    }

    [Test]
    public void RuntimeDirtyCostField使用定点直线和对角墙距()
    {
        const int width = 9;
        const int height = 9;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(0, 0.05f);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 4.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(7.5f, 0f, 4.5f), 2f, out _));

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9901, new Vector3(4.5f, 0f, 4.5f), new Vector3(0.49f, 0f, 0.49f));
        for (int i = 0; i < 64 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(4, 4, out byte blocked));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 4, out byte cardinal));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 3, out byte diagonal));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(2, 4, out byte outsideBlur));
        Assert.AreEqual(255, blocked);
        Assert.AreEqual(2, cardinal, "直线相邻格应获得墙边成本。");
        Assert.AreEqual(2, diagonal, "对角相邻格应使用固定 5793 步长并获得墙边成本。");
        Assert.AreEqual(1, outsideBlur, "两格外的成本插值应按 midpoint-to-even 回落为零惩罚。");
    }

    [Test]
    public void AuthoredCostField的动态障碍模糊使用同一定点墙距()
    {
        const int width = 9;
        const int height = 9;
        bool[] walkable = new bool[width * height];
        byte[] authoredCosts = new byte[width * height];
        for (int i = 0; i < walkable.Length; i++)
        {
            walkable[i] = true;
            authoredCosts[i] = 1;
        }

        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(0, 0.05f);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(int.MinValue + 1, width, height, 1f, Vector3.zero, walkable, null, authoredCosts);
        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 4.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(7.5f, 0f, 4.5f), 2f, out _));

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9902, new Vector3(4.5f, 0f, 4.5f), new Vector3(0.49f, 0f, 0.49f));
        for (int i = 0; i < 64 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(4, 4, out byte blocked));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 4, out byte cardinal));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 3, out byte diagonal));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(2, 4, out byte outsideBlur));
        Assert.AreEqual(255, blocked);
        Assert.AreEqual(2, cardinal);
        Assert.AreEqual(2, diagonal);
        Assert.AreEqual(1, outsideBlur);
    }

    [Test]
    public void 不同MovementType可以使用独立AuthoredWalkableMask()
    {
        const int width = 5;
        const int height = 3;
        bool[] smallWalkable = new bool[width * height];
        bool[] largeWalkable = new bool[width * height];
        for (int x = 0; x < width; x++)
        {
            SetWalkable(smallWalkable, width, x, 1);
            SetWalkable(largeWalkable, width, x, 1);
        }

        largeWalkable[2 + 1 * width] = false;
        int smallAgentType = AgentTypeHelper.SmallMovementTypeId;
        int largeAgentType = AgentTypeHelper.LargeMovementTypeId;
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(smallAgentType, 0.35f);
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(largeAgentType, 0.75f);
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSources(new[]
        {
            new AuthoredNavigationSourceData(smallAgentType, width, height, 1f, Vector3.zero, smallWalkable, null),
            new AuthoredNavigationSourceData(largeAgentType, width, height, 1f, Vector3.zero, largeWalkable, null)
        });

        SimEntityContext small = CreateEntity(new Vector3(0.5f, 0f, 1.5f), false, smallAgentType, 0.35f);
        SimMoveExecutor smallExecutor = small.MoveExecutor as SimMoveExecutor;
        Assert.IsNotNull(smallExecutor, "small 测试实体应使用 SimMoveExecutor");

        Vector3 goal = new Vector3(4.5f, 0f, 1.5f);
        for (int frame = 1; frame <= 30; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(small, goal, 2f, out Vector3 smallVelocity));
            smallExecutor.SetInput(smallVelocity);
            smallExecutor.Execute(0.1f);
            small.SyncPositionFromExecutor();
        }

        SimEntityContext large = CreateEntity(new Vector3(0.5f, 0f, 1.5f), false, largeAgentType, 0.75f);
        SimMoveExecutor largeExecutor = large.MoveExecutor as SimMoveExecutor;
        Assert.IsNotNull(largeExecutor, "large 测试实体应使用 SimMoveExecutor");
        for (int frame = 31; frame <= 60; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(large, goal, 2f, out Vector3 largeVelocity));
            largeExecutor.SetInput(largeVelocity);
            largeExecutor.Execute(0.1f);
            large.SyncPositionFromExecutor();
        }

        System.Text.StringBuilder diagnostics = new System.Text.StringBuilder(2048);
        diagnostics.Append("small=");
        AppendSteeringBreakdown(diagnostics, small);
        diagnostics.Append(" large=");
        AppendSteeringBreakdown(diagnostics, large);
        Assert.Greater(small.Position.x, 2.5f, $"small movement type 应能穿过自己的 authored mask 通道，pos={small.Position}\n{diagnostics}");
        Assert.Less(large.Position.x, 2.2f, $"large movement type 应在首次离散步长触及封闭格后停止，不能复用 small mask 穿过封闭格，pos={large.Position}");
    }


    [Test]
    public void 动态障碍生成后会重路由而不是沿旧走廊硬顶()
    {
        const int width = 9;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                SetWalkable(walkable, width, x, y);
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(8.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2.4f, out Vector3 beforeBlockVelocity));
        Assert.Greater(beforeBlockVelocity.x, 1.6f, $"初始无障碍时应直走，velocity={beforeBlockVelocity}");
        Assert.AreEqual(0f, beforeBlockVelocity.z, 0.2f, $"初始无障碍时不应无故侧偏，velocity={beforeBlockVelocity}");

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9001, new Vector3(4.5f, 0f, 2.5f), new Vector3(0.6f, 0f, 0.6f));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.4f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2.4f, out Vector3 pendingVelocity));
        Assert.Greater(pendingVelocity.magnitude, 0.2f, "RuntimeDirty 期间应继续提供旧 world 的确定性导航速度。");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            ctx.LogicEntityId.Value,
            out string pendingDiagnostic));
        StringAssert.Contains("/cached=True/", pendingDiagnostic,
            $"RuntimeDirty 期间已有 current tile 时必须继续消费旧 committed world 的正式场，diagnostic={pendingDiagnostic}");
        StringAssert.Contains("dirty=1", pendingDiagnostic,
            $"RuntimeDirty 提交前不能提前消费新 world tile，diagnostic={pendingDiagnostic}");
        FixVector2 pendingStart = ctx.PositionFixed;
        FixVector2 pendingRequested = new FixVector2((Fix64)6f, Fix64.Zero);
        Assert.IsTrue(LogicStaticCollisionShadowService.TrySolveFixed(
            0,
            pendingStart,
            pendingRequested,
            (Fix64)0.18f,
            out LogicStaticCollisionShadowResult pendingCollision));
        Assert.IsTrue(pendingCollision.SolveResult.Success);
        Assert.AreNotEqual(
            pendingRequested.x.RawValue,
            pendingCollision.SolveResult.ResolvedDisplacement.x.RawValue,
            "RuntimeDirty 尚未提交时，最新定点障碍也必须截断旧 world 导航产生的长扫掠。");
        Assert.Less(
            (float)(pendingStart.x + pendingCollision.SolveResult.ResolvedDisplacement.x),
            4f,
            "RuntimeDirty 期间的连续碰撞不能穿过新障碍。");
        ProcessRuntimeDirtyQueueUntilReady(3);
        Vector3 afterBlockVelocity = ResolveDeterministicFlowVelocityAfterQueue(ctx, goal, 2.4f, 512, out string fixedDiagnostic);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            ctx.LogicEntityId.Value,
            out fixedDiagnostic));
        Assert.Greater(Mathf.Abs(afterBlockVelocity.z), 0.25f, $"动态障碍后应出现明显绕行动量而不是继续直冲，velocity={afterBlockVelocity}, fixed={fixedDiagnostic}");
    }

    [Test]
    public void RuntimeDirty期间首次寻路会等待提交后使用新World()
    {
        const int width = 9;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(8.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(
            9003,
            new Vector3(4.5f, 0f, 4.5f),
            new Vector3(0.2f, 0f, 0.2f));
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(),
            "注册动态障碍后应存在待处理的 RuntimeDirty。");

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity),
            "RuntimeDirty 期间首次寻路请求应返回确定性等待结果。");
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
                ctx.LogicEntityId.Value,
                out string diagnostic),
            "首次寻路后应生成定点流场诊断。");
        Assert.AreEqual(Vector3.zero, velocity);
        StringAssert.Contains(
            "runtime-dirty-no-committed-flow",
            diagnostic,
            $"首次请求没有已提交 flow 可延续时必须明确等待 dirty world，diagnostic={diagnostic}");

        ProcessRuntimeDirtyQueueUntilReady(2);
        velocity = ResolveDeterministicFlowVelocityAfterQueue(ctx, goal, 2f, 512, out diagnostic);
        Assert.Greater(velocity.magnitude, 0.2f, $"dirty world 提交后必须恢复导航，velocity={velocity}, diagnostic={diagnostic}");
    }

    [Test]
    public void StaticCollisionShadow_使用动态障碍提交后的WalkableMask()
    {
        const int width = 9;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(
            9002,
            new Vector3(4.5f, 0f, 2.5f),
            new Vector3(0.6f, 0f, 0.6f));
        for (int i = 0; i < 64 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());

        FixVector2 start = new FixVector2((Fix64)1.5f, (Fix64)2.5f);
        FixVector2 requested = new FixVector2((Fix64)6f, Fix64.Zero);
        Assert.IsTrue(LogicStaticCollisionShadowService.TrySolveFixed(
            0,
            start,
            requested,
            (Fix64)0.18f,
            out LogicStaticCollisionShadowResult result));
        Assert.IsTrue(result.SolveResult.Success);
        Assert.AreNotEqual(requested.x.RawValue, result.SolveResult.ResolvedDisplacement.x.RawValue);
        Assert.Less((float)(start.x + result.SolveResult.ResolvedDisplacement.x), 4f);
    }
    [Test]
    public void AuthoredPointZeroNineGrid_首次构建障碍必须封住Q32格心()
    {
        const int agentTypeId = 91001;
        const int width = 512;
        const int height = 3;
        const int blockedX = 477;
        const int blockedY = 1;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadiusFixed(agentTypeId, Fix64.FromRaw(1));
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            agentTypeId,
            width,
            height,
            0.09f,
            new Vector3(10.08f, 0f, 10.08f),
            walkable);
        FixVector2 blockedCenter = NavigationGridFixedMath.GridCellCenterFixed(
            NavigationGridFixedMath.FloatToGridRaw(0.09f),
            NavigationGridFixedMath.FloatToGridRaw(10.08f),
            NavigationGridFixedMath.FloatToGridRaw(10.08f),
            blockedX,
            blockedY);
        FlowFieldCrowdMovementSystem.RegisterBoxObstacleFixed(
            91002,
            blockedCenter,
            FixVector2.Zero);
        ProcessWorldBuildQueueUntilReady();
        Assert.IsFalse(
            FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPointFixed(
                blockedCenter,
                agentTypeId,
                Fix64.Zero,
                Fix64.Zero,
                out _),
            $"首次 world build 必须按 Q32 authored 格心封住动态障碍格。centerRaw=({blockedCenter.x.RawValue},{blockedCenter.y.RawValue}) cell=({blockedX},{blockedY})");
    }
    [Test]
    public void AuthoredPointZeroNineGrid_RuntimeDirty障碍必须封住Q32格心()
    {
        const int agentTypeId = 91003;
        const int width = 512;
        const int height = 3;
        const int blockedX = 477;
        const int blockedY = 1;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadiusFixed(agentTypeId, Fix64.FromRaw(1));
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            agentTypeId,
            width,
            height,
            0.09f,
            new Vector3(10.08f, 0f, 10.08f),
            walkable);
        ProcessWorldBuildQueueUntilReady();
        FixVector2 blockedCenter = FlowFieldCrowdMovementSystem.GetEditorTestGridToWorldCenterFixed(blockedX, blockedY);
        FlowFieldCrowdMovementSystem.RegisterBoxObstacleFixed(
            91004,
            blockedCenter,
            FixVector2.Zero);
        ProcessRuntimeDirtyQueueUntilReady(2);
        Assert.IsFalse(
            FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPointFixed(
                blockedCenter,
                agentTypeId,
                Fix64.Zero,
                Fix64.Zero,
                out _),
            $"Runtime dirty 必须按 Q32 authored 格心封住动态障碍格。centerRaw=({blockedCenter.x.RawValue},{blockedCenter.y.RawValue}) cell=({blockedX},{blockedY})");
    }

    [Test]
    public void AuthoredPointZeroNineGrid_空间梯度在Q12量化格心必须选择左侧采样列()
    {
        const int width = 8;
        const int height = 4;
        const int worldX = 2;
        const int worldY = 1;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            width,
            height,
            0.09f,
            new Vector3(10.08f, 0f, 10.08f),
            walkable);
        ProcessWorldBuildQueueUntilReady();
        FixVector2 quantizedCenter = FlowFieldCrowdMovementSystem.GetEditorTestGridToWorldCenterFixed(worldX, worldY);

        FlowFieldCrowdMovementSystem.GetEditorTestSpatialGradientSampleGridFixed(
            quantizedCenter,
            worldX,
            worldY,
            out int x0,
            out int x1,
            out _,
            out _,
            out Fix64 tx,
            out _);

        Assert.AreEqual(worldX - 1, x0);
        Assert.AreEqual(worldX, x1);
        Assert.Greater(tx.RawValue, Fix64.One.RawValue - 32, $"Q32 authored 格心左侧的 Q12 量化点应保持接近右端权重，txRaw={tx.RawValue} centerRaw={quantizedCenter.x.RawValue}");
    }

    [Test]
    public void AuthoredPointZeroNineGrid_距离换算格数使用Q32权威CellSize()
    {
        long cellSizeGridRaw = NavigationGridFixedMath.FloatToGridRaw(0.09f);
        Fix64 distance = Fix64.FromRaw(369);

        Assert.AreEqual(2, NavigationGridFixedMath.DivideCeilingByCellSize(distance, cellSizeGridRaw));
        Assert.AreEqual(4099, NavigationGridFixedMath.DivideByCellSize(distance, cellSizeGridRaw).RawValue);
    }

    [Test]
    public void AuthoredPointZeroNineGrid_空间积分按Q32半格决定子步数()
    {
        const int width = 8;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            width,
            height,
            0.09f,
            new Vector3(10.08f, 0f, 10.08f),
            walkable);
        ProcessWorldBuildQueueUntilReady();

        Assert.AreEqual(
            4,
            FlowFieldCrowdMovementSystem.GetEditorTestSpatialIntegrationSubstepCount(Fix64.FromRaw(737)),
            "travelRaw=737 尚未跨过 4 个 authored 半格，不能因 Q12 半格向下量化而多分一个积分子步。");
    }

    [Test]
    public void AuthoredPointZeroNineGrid_墙边Cost按Q32格宽计算额外半径()
    {
        const int agentTypeId = 91005;
        const int width = 9;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        walkable[4 + 2 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadiusFixed(
            agentTypeId,
            Fix64.FromRaw(2048 + 369));
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            agentTypeId,
            width,
            height,
            0.09f,
            new Vector3(10.08f, 0f, 10.08f),
            walkable);
        ProcessWorldBuildQueueUntilReady();

        Assert.AreEqual(3, FlowFieldCrowdMovementSystem.GetEditorTestWallCostAdjacentPenalty());
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 2, out byte adjacentCost));
        Assert.AreEqual(4, adjacentCost, "Q32 上略大于一格的额外半径必须提高最终相邻墙格 CostField，不能按 Q12 cellSize 误判成恰好一格。");
    }

    [Test]
    public void AuthoredPointZeroNineGrid_远端GridCostStamp必须读取同列成本()
    {
        const int width = 515;
        const int height = 1;
        const int targetX = 513;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        byte[] costs = new byte[514];
        for (int i = 0; i < costs.Length; i++)
            costs[i] = 1;
        costs[targetX - 1] = 7;
        costs[targetX] = 13;

        Vector3 origin = new Vector3(10.08f, 0f, 10.08f);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            width,
            height,
            0.09f,
            origin,
            walkable);
        FlowFieldCrowdMovementSystem.RegisterGridCostStamp(
            91006,
            origin,
            0.09f,
            costs.Length,
            1,
            costs);
        ProcessWorldBuildQueueUntilReady();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(targetX, 0, out byte cost));
        Assert.AreEqual(13, cost, "导航第 513 列格心必须映射到 stamp 第 513 列，不能因 Q12 累计漂移读取第 512 列。");
    }

    [Test]
    public void Vector3合法点入口只做Fixed权威边界适配()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        walkable[2 + width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            width,
            height,
            0.09f,
            new Vector3(10.08f, 0f, 10.08f),
            walkable);
        ProcessWorldBuildQueueUntilReady();
        FixVector2 candidateFixed = FlowFieldCrowdMovementSystem.GetEditorTestGridToWorldCenterFixed(2, 1);
        Vector3 candidate = new Vector3((float)candidateFixed.x, 0f, (float)candidateFixed.y);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
            candidate,
            0,
            0.1f,
            0f,
            out Vector3 baseline));
        FlowFieldCrowdMovementSystem.PerturbEditorTestWorldGridFloatShadows(
            2f,
            new Vector3(100f, 999f, -100f));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
            candidate,
            0,
            0.1f,
            0f,
            out Vector3 perturbed));

        Assert.AreEqual(((Fix64)baseline.x).RawValue, ((Fix64)perturbed.x).RawValue);
        Assert.AreEqual(((Fix64)baseline.z).RawValue, ((Fix64)perturbed.z).RawValue);
    }

    [Test]
    public void StaticCollisionAuthority_冻结网格后FloatShadow不改变首次FixedSolve()
    {
        const int width = 7;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        for (int y = 0; y < height; y++)
            walkable[3 + y * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        FixVector2 start = new FixVector2((Fix64)1.5f, (Fix64)1.5f);
        FixVector2 requested = new FixVector2((Fix64)4f, Fix64.Zero);
        LogicStaticCollisionShadowService.Clear();
        Assert.IsTrue(LogicStaticCollisionShadowService.TrySolveFixed(
            0,
            start,
            requested,
            (Fix64)0.2f,
            out LogicStaticCollisionShadowResult baseline));
        ulong baselineHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();

        FlowFieldCrowdMovementSystem.PerturbEditorTestWorldGridFloatShadows(
            2f,
            new Vector3(100f, 999f, -100f));
        LogicStaticCollisionShadowService.Clear();
        Assert.IsTrue(LogicStaticCollisionShadowService.TrySolveFixed(
            0,
            start,
            requested,
            (Fix64)0.2f,
            out LogicStaticCollisionShadowResult perturbed));
        ulong perturbedHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldSetHash();

        Assert.AreEqual(baselineHash, perturbedHash, "float shadow 不得改变 committed world authority hash。");
        Assert.AreEqual(baseline.SolveResult.Success, perturbed.SolveResult.Success);
        Assert.AreEqual(baseline.SolveResult.Failure, perturbed.SolveResult.Failure);
        Assert.AreEqual(
            baseline.SolveResult.ResolvedDisplacement.x.RawValue,
            perturbed.SolveResult.ResolvedDisplacement.x.RawValue);
        Assert.AreEqual(
            baseline.SolveResult.ResolvedDisplacement.y.RawValue,
            perturbed.SolveResult.ResolvedDisplacement.y.RawValue);
    }

    [Test]
    public void 动态障碍改变连通性后会经RuntimeQueue重建Island()
    {
        const int width = 7;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f), false, 0, 0.18f);
        Vector3 farGoal = new Vector3(6.5f, 0f, 1.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryResolveNearestReachableGoal(ctx, farGoal, 2f, out Vector3 initialReachable, out string initialReason), initialReason);
        Assert.AreEqual(6.5f, initialReachable.x, 0.01f, $"无障碍时目标应保持原位置，reachable={initialReachable}");

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9101, new Vector3(3.5f, 0f, 1.5f), new Vector3(0.6f, 0f, 0.6f));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsFalse(
            FlowFieldCrowdMovementSystem.TryResolveNearestReachableGoal(ctx, farGoal, 2f, out _, out string pendingReason),
            pendingReason);
        StringAssert.Contains("runtime dirty pending", pendingReason);

        for (int i = 0; i < 32 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        FlowFieldCrowdMovementSystem.SetEditorTestClock(3, 0.3f);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[FlowGoalResolutionIslandDiag\]"));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryResolveNearestReachableGoal(ctx, farGoal, 2f, out Vector3 blockedReachable, out string blockedReason), blockedReason);

        Assert.Less(blockedReachable.x, 3.5f, $"动态障碍切断走廊后，最近可达目标应留在起点同 island，而不是继续使用旧 island 追到障碍另一侧，reachable={blockedReachable}");
    }

    [Test]
    public void 战斗接近点在RuntimeDirty期间返回Pending而不是误报不可达()
    {
        const int width = 9;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext self = CreateEntity(new Vector3(1.5f, 0f, 2.5f), false, 0, 0.18f);
        SimEntityContext target = CreateEntity(new Vector3(7.5f, 0f, 2.5f), true, 0, 0.18f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryResolveCombatApproachPoint(
                self,
                target,
                target.Position,
                1f,
                0.5f,
                3,
                12,
                0.45f,
                out _,
                out string initialReason,
                out FlowFieldCrowdMovementSystem.NavigationQueryFailureKind initialFailureKind),
            initialReason);
        Assert.AreEqual(FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.None, initialFailureKind);

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9103, new Vector3(4.5f, 0f, 2.5f), new Vector3(0.6f, 0f, 0.6f));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsFalse(
            FlowFieldCrowdMovementSystem.TryResolveCombatApproachPoint(
                self,
                target,
                target.Position,
                1f,
                0.5f,
                3,
                12,
                0.45f,
                out _,
                out string pendingReason,
                out FlowFieldCrowdMovementSystem.NavigationQueryFailureKind pendingFailureKind));
        Assert.AreEqual(FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.PendingRuntimeUpdate, pendingFailureKind, pendingReason);
        StringAssert.Contains("runtime dirty pending", pendingReason);

        for (int i = 0; i < 64 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "运行时导航重建应在测试预算内完成。");
        FlowFieldCrowdMovementSystem.SetEditorTestClock(3, 0.3f);
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryResolveCombatApproachPoint(
                self,
                target,
                target.Position,
                1f,
                0.5f,
                3,
                12,
                0.45f,
                out _,
                out string rebuiltReason,
                out FlowFieldCrowdMovementSystem.NavigationQueryFailureKind rebuiltFailureKind),
            rebuiltReason);
        Assert.AreEqual(FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.None, rebuiltFailureKind);
    }

    [Test]
    public void 战斗接近点CacheKey保留完整定点目标而不按FloatBand混用()
    {
        const int width = 9;
        const int height = 7;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext self = CreateEntity(new Vector3(1.5f, 0f, 3.5f), false, 0, 0.18f);
        SimEntityContext target = CreateEntity(new Vector3(6.5f, 0f, 3.5f), true, 0, 0.18f);
        FixVector2 firstTargetPoint = new FixVector2((Fix64)6.101f, (Fix64)3.501f);
        FixVector2 secondTargetPoint = new FixVector2((Fix64)6.102f, (Fix64)3.502f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryResolveCombatApproachPointFixed(
                self,
                target,
                firstTargetPoint,
                (Fix64)1.2f,
                (Fix64)0.45f,
                (Fix64)0.5f,
                3,
                12,
                (Fix64)0.45f,
                out _,
                out string firstFailure,
                out _),
            firstFailure);
        Assert.AreEqual(1, FlowFieldCrowdMovementSystem.GetEditorTestCombatTargetSlotCacheCount());

        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryResolveCombatApproachPointFixed(
                self,
                target,
                secondTargetPoint,
                (Fix64)1.2f,
                (Fix64)0.45f,
                (Fix64)0.5f,
                3,
                12,
                (Fix64)0.45f,
                out _,
                out string secondFailure,
                out _),
            secondFailure);
        Assert.AreNotEqual(firstTargetPoint.x.RawValue, secondTargetPoint.x.RawValue);
        Assert.AreEqual(2, FlowFieldCrowdMovementSystem.GetEditorTestCombatTargetSlotCacheCount());
    }

    [Test]
    public void 移动目标平移时战斗接近槽保持目标局部偏移且被占用后才重选()
    {
        const int width = 16;
        const int height = 7;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext hero = CreateEntity(new Vector3(10.5f, 0f, 3.5f), false, 0, 0.18f);
        hero.Side = SideType.PlayerSide;
        SimEntityContext chaser = CreateEntity(new Vector3(1.5f, 0f, 3.5f), false, 0, 0.18f);
        chaser.Side = SideType.EnemySide;
        chaser.TargetComp = new SimTargetingComp(chaser, new List<IEntityContext> { hero })
        {
            CurrentTarget = hero
        };
        CharacterMoveComp moveComp = new CharacterMoveComp();
        moveComp.Init(chaser, 0);
        chaser.MoveComp = moveComp;
        SoldierAIBrain brain = new SoldierAIBrain
        {
            DetectEnemyRange = (Fix64)40f,
            ChaseRange = (Fix64)80f
        };
        chaser.Brain = brain;
        EntityRegistry.Register(hero);
        EntityRegistry.Register(chaser);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        brain.Tick(chaser, (Fix64)0.1f);
        Assert.IsTrue(moveComp.TryGetNavigationTargetFixed(out FixVector2 firstApproach));
        FixVector2 firstOffset = firstApproach - hero.PositionFixed;

        for (int frame = 2; frame <= 9; frame++)
        {
            hero.PositionFixed += new FixVector2((Fix64)0.1f, Fix64.Zero);
            FlowFieldCrowdMovementSystem.UpdateAgentForEditorTest(hero, 0.18f, 0);
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FixVector2 expectedTranslatedApproach = hero.PositionFixed + firstOffset;
            Assert.IsTrue(
                FlowFieldCrowdMovementSystem.TryReserveReachableNavigationGoalFixed(
                    chaser,
                    hero.LogicEntityId.Value,
                    expectedTranslatedApproach,
                    (Fix64)0.18f,
                    (Fix64)0.05f,
                    out int translatedBlockingId,
                    out string translatedFailure,
                    out FlowFieldCrowdMovementSystem.NavigationQueryFailureKind translatedFailureKind),
                $"frame={frame} translated approach validation failed kind={translatedFailureKind} blocker={translatedBlockingId} reason={translatedFailure}");
            brain.Tick(chaser, (Fix64)0.1f);

            Assert.IsTrue(moveComp.TryGetNavigationTargetFixed(out FixVector2 translatedApproach));
            FixVector2 translatedOffset = translatedApproach - hero.PositionFixed;
            Assert.AreEqual(
                firstOffset.x.RawValue,
                translatedOffset.x.RawValue,
                $"frame={frame} X offset firstApproach={firstApproach} target={hero.PositionFixed} expected={expectedTranslatedApproach} actual={translatedApproach}");
            Assert.AreEqual(
                firstOffset.y.RawValue,
                translatedOffset.y.RawValue,
                $"frame={frame} Y offset firstApproach={firstApproach} target={hero.PositionFixed} expected={expectedTranslatedApproach} actual={translatedApproach}");
        }

        SimEntityContext blocker = CreateEntity(new Vector3(1.5f, 0f, 5.5f), false, 0, 0.18f);
        blocker.Side = SideType.EnemySide;
        FixVector2 occupiedTranslatedApproach = hero.PositionFixed + firstOffset;
        FlowFieldCrowdMovementSystem.SetEditorTestClock(10, 1f);
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryReserveNavigationGoalIfAvailableFixed(
                blocker.LogicEntityId.Value,
                hero.LogicEntityId.Value,
                occupiedTranslatedApproach,
                (Fix64)0.05f,
                out int blockerId));
        Assert.AreEqual(0, blockerId);

        brain.Tick(chaser, (Fix64)0.1f);
        Assert.IsTrue(moveComp.TryGetNavigationTargetFixed(out FixVector2 reselection));
        Assert.AreNotEqual(occupiedTranslatedApproach, reselection, "已有目标局部槽被预约后必须重新选择有效槽位。");
    }

    [Test]
    public void 既有导航目标预留只接受起点同可达岛的精确位置()
    {
        const int width = 7;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < 3; x++)
            SetWalkable(walkable, width, x, 1);
        for (int x = 4; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext self = CreateEntity(new Vector3(1.5f, 0f, 1.5f), false, 0, 0.18f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);

        Assert.IsFalse(
            FlowFieldCrowdMovementSystem.TryReserveReachableNavigationGoalFixed(
                self,
                0,
                new FixVector2((Fix64)5.5f, (Fix64)1.5f),
                (Fix64)0.18f,
                (Fix64)0.45f,
                out _,
                out string failureReason,
                out FlowFieldCrowdMovementSystem.NavigationQueryFailureKind failureKind));
        Assert.AreEqual(FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.Unreachable, failureKind);
        StringAssert.Contains("goal island differs", failureReason);
    }

    [Test]
    public void 战斗槽位饱和时两个近战单位的本Tick预约不得被通用占位二次改写()
    {
        const int width = 10;
        const int height = 7;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        SimEntityContext target = CreateEntity(new Vector3(7.5f, 0f, 3.5f), true, 0, 0.18f);
        target.Side = SideType.EnemySide;
        SimEntityContext blockingCompanion = CreateEntity(target.Position, false, 0, 0.8f);
        blockingCompanion.Side = SideType.PlayerSide;
        SimEntityContext[] pursuers =
        {
            CreateEntity(new Vector3(1.5f, 0f, 2.5f), false, 0, 0.54f),
            CreateEntity(new Vector3(1.5f, 0f, 4.5f), false, 0, 0.54f),
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        for (int i = 0; i < pursuers.Length; i++)
        {
            SimEntityContext pursuer = pursuers[i];
            pursuer.Side = SideType.PlayerSide;
            pursuer.TargetComp = new SimTargetingComp(pursuer, new List<IEntityContext> { target })
            {
                CurrentTarget = target
            };

            Assert.IsTrue(
                FlowFieldCrowdMovementSystem.TryResolveCombatApproachPointFixed(
                    pursuer,
                    target,
                    target.PositionFixed,
                    (Fix64)1.1f,
                    (Fix64)0.59f,
                    (Fix64)0.55f,
                    3,
                    16,
                    (Fix64)1.3f,
                    out FixVector2 approach,
                    out string failureReason,
                    out FlowFieldCrowdMovementSystem.NavigationQueryFailureKind failureKind),
                $"第 {i} 个近战单位必须取得饱和攻击环中的接敌点。failureKind={failureKind} reason={failureReason}");
            Assert.IsFalse(
                FlowFieldCrowdMovementSystem.TryReserveNavigationGoalIfAvailableFixed(
                    pursuer.LogicEntityId.Value,
                    target.LogicEntityId.Value,
                    approach,
                    (Fix64)1.3f,
                    out int blockingAgentId),
                "测试前提要求接敌点确实已被同伴占用。");
            Assert.AreNotEqual(0, blockingAgentId);

            Assert.IsTrue(
                FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
                    pursuer,
                    approach,
                    (Fix64)3.75f,
                    out FixVector2 velocity));
            Assert.AreNotEqual(FixVector2.Zero, velocity);
            Assert.IsTrue(
                FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringGoal(
                    pursuer.LogicEntityId.Value,
                    out Vector3 steeringGoal));
            Assert.AreEqual(approach.x.RawValue, ((Fix64)steeringGoal.x).RawValue, "通用占位不得把战斗预约改到左右候选点。");
            Assert.AreEqual(approach.y.RawValue, ((Fix64)steeringGoal.z).RawValue, "通用占位不得把战斗预约改到左右候选点。");
        }
    }

    [Test]
    public void Lv2真实导航英雄靠近阻挡区时多个远程兵仍能取得攻击范围内接近点()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>(
            "Assets/AAAGame/Tilemap/Lv2_FlowNavigationGrid_Medium.asset");
        Assert.NotNull(grid, "本回归必须直接使用报错场景的 Lv2 Medium 导航源。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData);
        Assert.IsTrue(derivedData.IsValid);

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            derivedData,
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        Vector3 targetPosition = new Vector3(41.64f, 0.10f, 49.90f);
        SimEntityContext target = CreateEntity(targetPosition, true, grid.AgentTypeId, 0.18f);
        Vector3[] starts =
        {
            new Vector3(25.61f, 0.09f, 37.15f),
            new Vector3(25.62f, 0.09f, 36.17f),
            new Vector3(26.57f, 0.09f, 36.85f),
            new Vector3(26.41f, 0.09f, 34.49f),
            new Vector3(26.82f, 0.09f, 33.72f),
            new Vector3(24.97f, 0.09f, 34.90f),
            new Vector3(26.59f, 0.09f, 32.99f),
            new Vector3(22.81f, 0.09f, 38.47f),
            new Vector3(24.31f, 0.09f, 38.30f),
            new Vector3(23.08f, 0.09f, 36.74f),
            new Vector3(22.11f, 0.09f, 36.97f)
        };

        const float targetRadius = 0.18f;
        const float attackRange = 10.60f;
        const float preferredStandOff = 10.592f;
        const float minimumStandOff = 0.41f;
        const float requiredClearance = 0.71f;
        HashSet<Vector2Int> selectedCells = new HashSet<Vector2Int>();
        FlowFieldCrowdMovementSystem.SetEditorTestClock(628, 10.0f);
        for (int i = 0; i < starts.Length; i++)
        {
            SimEntityContext self = CreateEntity(starts[i], false, grid.AgentTypeId, 0.18f);
            Assert.IsTrue(
                FlowFieldCrowdMovementSystem.TryResolveCombatApproachPoint(
                    self,
                    target,
                    targetPosition,
                    preferredStandOff,
                    minimumStandOff,
                    0.55f,
                    3,
                    16,
                    requiredClearance,
                    out Vector3 approach,
                    out string failureReason,
                    out FlowFieldCrowdMovementSystem.NavigationQueryFailureKind failureKind),
                $"第 {i} 个单位不应重现 no combat approach slots。failureKind={failureKind} reason={failureReason}");

            float distanceToTargetSurface = Mathf.Max(0f, HorizontalDistance(approach, targetPosition) - targetRadius);
            Assert.LessOrEqual(
                distanceToTargetSurface,
                attackRange + grid.CellSize,
                $"接近点必须位于真实攻击范围内，而不是旧实现向范围外扩圈。index={i} approach={approach}");
            Assert.IsTrue(grid.WorldToCell(approach, out int x, out int y));
            selectedCells.Add(new Vector2Int(x, y));
        }

        Assert.GreaterOrEqual(selectedCells.Count, 8, "11 个追兵应在攻击环带内分散到多个导航槽位。");
    }

    [Test]
    public void 动态障碍HaloSector不会被BoundsClamp误封边界格()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f), false, 0, 0.18f);
        Vector3 farGoal = new Vector3(3.5f, 0f, 1.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryResolveNearestReachableGoal(ctx, farGoal, 2f, out Vector3 initialReachable, out string initialReason), initialReason);
        Assert.AreEqual(3.5f, initialReachable.x, 0.01f, $"初始目标应可达 reachable={initialReachable}");

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9102, new Vector3(4.75f, 0f, 1.5f), new Vector3(0.2f, 0f, 0.2f));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsFalse(
            FlowFieldCrowdMovementSystem.TryResolveNearestReachableGoal(ctx, farGoal, 2f, out _, out string pendingReason),
            pendingReason);
        StringAssert.Contains("runtime dirty pending", pendingReason);

        for (int i = 0; i < 32 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        FlowFieldCrowdMovementSystem.SetEditorTestClock(3, 0.3f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryResolveNearestReachableGoal(ctx, farGoal, 2f, out Vector3 reachableAfterHaloDirty, out string blockedReason), blockedReason);

        Assert.AreEqual(3.5f, reachableAfterHaloDirty.x, 0.01f, $"障碍只影响相邻 sector 时，halo sector 不能被 clamp 误封，reachable={reachableAfterHaloDirty}");
    }

    [Test]
    public void 位移后RuntimeDirty期间继续安全移动并在提交后重算()
    {
        const int width = 9;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                SetWalkable(walkable, width, x, y);
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        CharacterMoveComp moveComp = new CharacterMoveComp();
        moveComp.Init(ctx);
        ctx.MoveComp = moveComp;
        moveComp.MoveToFixed(new FixVector2((Fix64)8.5f, (Fix64)2.5f));

        DurationMoveEffectComp effectComp = new DurationMoveEffectComp();
        effectComp.Init(ctx);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        moveComp.Move((Fix64)0.2f);
        ((SimMoveExecutor)ctx.MoveExecutor).Execute(0.2f);
        ctx.SyncPositionFromExecutor();
        Assert.Greater(ctx.Position.x, 0.7f, $"首次寻路应正常向前，pos={ctx.Position}");

        effectComp.StartDurationAdditionalMove((Fix64)0.1f, new FixVector2(Fix64.Zero, (Fix64)(-10)));
        effectComp.ApplyEffect((Fix64)0.1f);
        moveComp.Move((Fix64)0.1f);
        ((SimMoveExecutor)ctx.MoveExecutor).Execute(0.1f);
        ctx.SyncPositionFromExecutor();
        Assert.Less(ctx.Position.z, 2.0f, $"位移应把单位推离原走廊，pos={ctx.Position}");

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9002, new Vector3(4.5f, 0f, 2.5f), new Vector3(0.6f, 0f, 0.6f));

        effectComp.ApplyEffect((Fix64)0.1f);
        Assert.AreEqual(MovementMode.Normal, ctx.MoveExecutor.MovementMode, "位移结束后应恢复 Normal");

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.4f);
        moveComp.Move((Fix64)0.2f);
        ((SimMoveExecutor)ctx.MoveExecutor).Execute(0.2f);
        ctx.SyncPositionFromExecutor();

        SimMoveExecutor executor = ctx.MoveExecutor as SimMoveExecutor;
        Assert.IsNotNull(executor, "测试上下文应使用 SimMoveExecutor");
        Assert.Greater(executor.LastFrameVelocity.magnitude, 0.2f, "RuntimeDirty 期间不应全局冻结已有导航速度。");
        Assert.IsTrue(LogicStaticCollisionShadowService.TrySolveFixed(
            0,
            ctx.PositionFixed,
            FixVector2.Zero,
            (Fix64)0.18f,
            out LogicStaticCollisionShadowResult pendingPositionCollision));
        Assert.IsTrue(pendingPositionCollision.SolveResult.Success);
        Assert.IsFalse(
            pendingPositionCollision.SolveResult.StartedOverlapping,
            "RuntimeDirty 期间继续移动后的当前位置不能与最新障碍重叠。");

        ProcessRuntimeDirtyQueueUntilReady(3);
        Vector3 rerouteVelocity = ResolveDeterministicFlowVelocityAfterQueue(
            ctx,
            new Vector3(8.5f, 0f, 2.5f),
            2f,
            512,
            out string fixedDiagnostic);
        Assert.Greater(rerouteVelocity.magnitude, 0.2f, $"路径失效重算后应恢复移动，velocity={rerouteVelocity}, fixed={fixedDiagnostic}");
        Assert.Greater(rerouteVelocity.x, 0.2f, $"单位已位移到障碍下侧通道，重算后应沿新通道朝目标推进，velocity={rerouteVelocity}, fixed={fixedDiagnostic}");
    }

    [Test]
    public void Portal窗口部分槽位不可达时会裁掉坏槽位而不是整窗报错()
    {
        const int width = 6;
        const int height = 4;
        bool[] walkable = new bool[width * height];

        for (int y = 1; y <= 3; y++)
            SetWalkable(walkable, width, 2, y);
        for (int x = 0; x <= 2; x++)
            SetWalkable(walkable, width, x, 3);
        for (int x = 0; x <= 1; x++)
            SetWalkable(walkable, width, x, 2);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(2.5f, 0f, 3.5f));
        Vector3 goal = new Vector3(0.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(1);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(128, 12.8f);
        Assert.DoesNotThrow(() =>
        {
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));
            Assert.Less(velocity.x, -0.2f, $"应沿仍然可达的 portal 槽位继续向目标推进，velocity={velocity}");
        });
    }

    [Test]
    public void 不可达目标会解析到同岛最近可达点()
    {
        const int width = 12;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x <= 3; x++)
            SetWalkable(walkable, width, x, 1);
        SetWalkable(walkable, width, 10, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        Vector3 unreachableGoal = new Vector3(10.5f, 0f, 1.5f);

        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryResolveNearestReachableGoal(
                ctx,
                unreachableGoal,
                1f,
                out Vector3 reachableGoal,
                out string failureReason),
            failureReason);

        Assert.AreEqual(3.5f, reachableGoal.x, 0.001f, $"应选择当前 island 上离不可达目标最近的可达点 reachable={reachableGoal}");
        Assert.AreEqual(1.5f, reachableGoal.z, 0.001f, $"应保持最近可达走廊中心 reachable={reachableGoal}");
    }

    public void 同一移动目标换格时会立即刷新导航目标()
    {
        const int width = 16;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(2.5f, 0f, 6.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 firstVelocity));

        target.Position = new Vector3(2.5f, 0f, 0.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.15f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 reusedVelocity));

        Assert.Greater(firstVelocity.z, 0.2f, $"初始目标在上方，应有向上分量 first={firstVelocity}");
        Assert.Less(reusedVelocity.z, -0.2f, $"目标换到下方新格后应立即刷新导航目标 reused={reusedVelocity}");
    }

    [Test]
    public void 移动目标落在不可达Island时会追向最近可达点()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x <= 3; x++)
            SetWalkable(walkable, width, x, 1);
        SetWalkable(walkable, width, 6, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 1.5f), false, 0, 0.18f);
        SimEntityContext target = CreateEntity(new Vector3(6.5f, 0f, 1.5f), false, 0, 0.18f);
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(1);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 velocity));

        System.Text.StringBuilder diagnostics = new System.Text.StringBuilder(2048);
        AppendSteeringBreakdown(diagnostics, chaser);
        Assert.Greater(velocity.x, 0.5f, $"移动目标处于不可达 island 时应追向当前 island 上最近可达点，velocity={velocity}\n{diagnostics}");
    }

    [Test]
    public void NavigationTargetExtent_UsesAuthoredAabbInsteadOfUnitCollisionRadius()
    {
        var target = new BoxTargetEntityContext(
            new Vector3(6.5f, 0f, 2.5f),
            new FixVector2((Fix64)1.25f, (Fix64)2.5f));
        target.SetProperty(CreatureMainProperty.CollisionRadius, Fix64.Zero);

        Fix64 extent = FlowFieldCrowdMovementSystem.ResolveNavigationTargetExtentForEditorTest(target);

        Assert.AreEqual(((Fix64)2.5f).RawValue, extent.RawValue, "建筑目标径向范围必须来自 authored AABB 的较大半轴。");
    }

    [Test]
    public void SteeringToBuildingTarget_DoesNotReadBuildingUnitCollisionRadius()
    {
        const int width = 12;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 2.5f), false, 0, 0.18f);
        var target = new BoxTargetEntityContext(
            new Vector3(9.5f, 0f, 2.5f),
            new FixVector2((Fix64)1.5f, (Fix64)1f));
        target.SetProperty(CreatureMainProperty.CollisionRadius, Fix64.Zero);
        chaser.TargetComp = new SimTargetingComp(chaser, new List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);

        Assert.DoesNotThrow(() =>
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _)));
    }

    [Test]
    public void AgentCollisionRadius_ZeroStillThrowsExplicitly()
    {
        SimEntityContext agent = CreateEntity(new Vector3(0.5f, 0f, 0.5f));
        agent.SetProperty(CreatureMainProperty.CollisionRadius, Fix64.Zero);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FlowFieldCrowdMovementSystem.UpdateAgentForEditorTest(agent, 0f, 0));

        StringAssert.Contains("ResolveCollisionRadiusFixed failed", exception.Message);
        StringAssert.Contains($"entity={agent.LogicEntityId.Value}", exception.Message);
    }

    [Test]
    public void 普通导航目标点被占用时会旋转到附近空位()
    {
        const int width = 8;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext mover = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        SimEntityContext blocker = CreateEntity(new Vector3(5.5f, 0f, 2.5f));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(mover, blocker.Position, 2f, out Vector3 velocity));

        Assert.Greater(velocity.x, 0.5f, $"被占目标点应仍保持朝目标附近前进，velocity={velocity}");
        Assert.Greater(Mathf.Abs(velocity.z), 0.01f, $"被占目标点应旋转到附近空位，而不是继续直冲占位单位，velocity={velocity}");
    }

    [Test]
    public void 目标占位候选不会跨到不可达Island再投回墙边()
    {
        const int width = 8;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x <= 5; x++)
        {
            SetWalkable(walkable, width, x, 1);
            SetWalkable(walkable, width, x, 2);
        }

        SetWalkable(walkable, width, 3, 4);
        SetWalkable(walkable, width, 4, 4);
        SetWalkable(walkable, width, 5, 4);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext first = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        SimEntityContext second = CreateEntity(new Vector3(1.5f, 0f, 2.5f));
        SimEntityContext target = CreateEntity(new Vector3(4.5f, 0f, 2.5f));
        first.TargetComp = new SimTargetingComp(first, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        second.TargetComp = new SimTargetingComp(second, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(first, target.Position, 2f, out _));
        ResolveDeterministicFlowVelocityAfterQueue(first, target.Position, 2f, 2, out _);
        Vector3 secondVelocity = ResolveDeterministicFlowVelocityAfterQueue(second, target.Position, 2f, 512, out _);

        Assert.Greater(secondVelocity.x, 0.5f, $"第二个追击者应继续沿主岛朝目标前进，而不是被不可达候选点拉向隔墙碎岛，velocity={secondVelocity}");
        Assert.Less(Mathf.Abs(secondVelocity.z), 1.6f, $"目标占位候选不应跨 island 后被最近可达点投回墙边，velocity={secondVelocity}");
    }

    [Test]
    public void 多个单位追同一目标时不同目标点不会被共享锚点压成一个点()
    {
        const int width = 10;
        const int height = 7;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext first = CreateEntity(new Vector3(0.5f, 0f, 3.5f), false, 0, 0.18f);
        SimEntityContext second = CreateEntity(new Vector3(1.2f, 0f, 3.5f), false, 0, 0.18f);
        SimEntityContext target = CreateEntity(new Vector3(6.5f, 0f, 3.5f), false, 0, 0.18f);
        first.TargetComp = new SimTargetingComp(first, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        second.TargetComp = new SimTargetingComp(second, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(first, new Vector3(5.5f, 0f, 2.5f), 2f, out Vector3 firstVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            first.LogicEntityId.Value,
            out string firstDiagnostic));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(second, new Vector3(5.5f, 0f, 4.5f), 2f, out Vector3 secondVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            second.LogicEntityId.Value,
            out string secondDiagnostic));

        Assert.Less(firstVelocity.z, -0.1f,
            $"第一个目标点在目标下侧，应保持下侧分量 first={firstVelocity}, diagnostic={firstDiagnostic}");
        Assert.Greater(secondVelocity.z, 0.1f,
            $"第二个目标点在目标上侧，不应被同 targetId 共享锚点压回下侧 second={secondVelocity}, diagnostic={secondDiagnostic}");
    }

    [Test]
    public void 多个单位追同一移动目标时使用最新共享目标格()
    {
        const int width = 16;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext firstChaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext secondChaser = CreateEntity(new Vector3(1.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(2.5f, 0f, 6.5f));
        firstChaser.TargetComp = new SimTargetingComp(firstChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        secondChaser.TargetComp = new SimTargetingComp(secondChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(firstChaser, target.Position, 2f, out Vector3 firstVelocity));

        target.Position = new Vector3(2.5f, 0f, 0.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.15f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(secondChaser, target.Position, 2f, out Vector3 secondVelocity));

        Assert.Greater(firstVelocity.z, 0.2f, $"第一个追击者应朝初始目标上方移动 first={firstVelocity}");
        Assert.Less(secondVelocity.z, -0.2f, $"同目标第二个追击者应使用目标最新共享格并立即追下方 second={secondVelocity}");
    }

    [Test]
    public void FarCombatSlotUsesMovingTargetAnchorOutsideLocalPlanningWindow()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 40;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(24.5f, 0f, 3.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        Vector3 combatSlot = target.Position + new Vector3(0f, 0f, -2f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, combatSlot, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestStableGoal(
            chaser.LogicEntityId.Value,
            out int targetId,
            out _,
            out _,
            out _,
            out _,
            out Vector3 stableGoal));
        Assert.AreEqual(target.LogicEntityId.Value, targetId);
        Assert.AreNotEqual(((Fix64)combatSlot.z).RawValue, ((Fix64)stableGoal.z).RawValue,
            "Far path planning must use the moving-target anchor instead of rebuilding against the translated combat slot every tick.");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestLastFixedNavigationGoal(
            chaser.LogicEntityId.Value,
            out FixVector2 navigationGoal));
        Assert.AreEqual(((Fix64)stableGoal.x).RawValue, navigationGoal.x.RawValue);
        Assert.AreEqual(((Fix64)stableGoal.z).RawValue, navigationGoal.y.RawValue);
    }

    [Test]
    public void 远距离移动目标未跨完整Sector时保持共享路径锚点()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 40;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(24.5f, 0f, 3.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestStableGoal(
            chaser.LogicEntityId.Value,
            out _,
            out int initialRawX,
            out int initialRawY,
            out _,
            out _,
            out _));

        target.Position = new Vector3(30.5f, 0f, 3.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestStableGoal(
            chaser.LogicEntityId.Value,
            out _,
            out int retainedRawX,
            out int retainedRawY,
            out _,
            out _,
            out Vector3 retainedStableWorld));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestLastFixedNavigationGoal(
            chaser.LogicEntityId.Value,
            out FixVector2 retainedNavigationGoal));

        Assert.AreEqual(initialRawX, retainedRawX);
        Assert.AreEqual(initialRawY, retainedRawY);
        Assert.AreEqual(((Fix64)retainedStableWorld.x).RawValue, retainedNavigationGoal.x.RawValue,
            "steering 必须消费与 PathHandle 相同的稳定锚点，不能在建路后又覆盖成移动目标的实时位置。");
        Assert.AreEqual(((Fix64)retainedStableWorld.z).RawValue, retainedNavigationGoal.y.RawValue,
            "steering 必须消费与 PathHandle 相同的稳定锚点，不能在建路后又覆盖成移动目标的实时位置。");
        Assert.AreNotEqual(((Fix64)target.Position.x).RawValue, retainedNavigationGoal.x.RawValue,
            "测试必须覆盖实时目标已经移动、稳定锚点仍保留的场景。");
    }

    [Test]
    public void 同帧同Island追同一跨Island目标时复用可达目标解析()
    {
        const int width = 12;
        const int height = 6;
        bool[] walkable = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                walkable[y * width + x] = x != 6;
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext firstChaser = CreateEntity(new Vector3(1.5f, 0f, 2.5f));
        SimEntityContext secondChaser = CreateEntity(new Vector3(2.5f, 0f, 2.5f));
        SimEntityContext target = CreateEntity(new Vector3(10.5f, 0f, 2.5f));
        firstChaser.TargetComp = new SimTargetingComp(firstChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        secondChaser.TargetComp = new SimTargetingComp(secondChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(firstChaser, target.Position, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(secondChaser, target.Position, 2f, out _));
        Assert.AreEqual(1, FlowFieldCrowdMovementSystem.GetEditorTestStableGoalReachabilityReuseCount());
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestStableGoal(firstChaser.LogicEntityId.Value, out int firstX, out int firstY, out _, out _, out _, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestStableGoal(secondChaser.LogicEntityId.Value, out int secondX, out int secondY, out _, out _, out _, out _));
        Assert.AreEqual(firstX, secondX);
        Assert.AreEqual(firstY, secondY);
    }

    [Test]
    public void 移动目标中心不可走但同岛存在可达接近点时仍能建Anchor()
    {
        const int width = 20;
        const int height = 15;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        for (int y = 1; y <= 13; y++)
        {
            for (int x = 8; x < width; x++)
                walkable[y * width + x] = false;
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(1.5f, 0f, 7.5f));
        SimEntityContext target = CreateEntity(new Vector3(14.5f, 0f, 7.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 velocity));
        Assert.Greater(velocity.x, 0.1f, $"追击者应使用同岛可达接近点继续朝目标侧移动 velocity={velocity}");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestStableGoal(
            chaser.LogicEntityId.Value,
            out int targetId,
            out int rawX,
            out int rawY,
            out int stableX,
            out int stableY,
            out _));
        Assert.AreEqual(target.LogicEntityId.Value, targetId);
        string rawDiagnostics = FlowFieldCrowdMovementSystem.GetEditorNavigationCellDiagnostics(0, rawX, rawY);
        string targetDiagnostics = FlowFieldCrowdMovementSystem.GetEditorNavigationCellDiagnostics(0, 14, 7);
        string stableDiagnostics = FlowFieldCrowdMovementSystem.GetEditorNavigationCellDiagnostics(0, stableX, stableY);
        Assert.AreEqual(14, rawX, rawDiagnostics);
        Assert.AreEqual(7, rawY, rawDiagnostics);
        Assert.IsFalse(stableX == rawX && stableY == rawY, $"stable goal must not keep the blocked target cell. targetCell={targetDiagnostics}");
        StringAssert.Contains("walk=True", stableDiagnostics);
    }

    [Test]
    public void 移动目标Anchor被StableGoal引用时不会被定时回收()
    {
        const int width = 16;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(14.5f, 0f, 3.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));
        StringAssert.DoesNotContain("movingAnchor=missing", FlowFieldCrowdMovementSystem.GetEditorTestMovingTargetAnchorDiagnostics(chaser.LogicEntityId.Value));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(401, 40.1f);
        Assert.DoesNotThrow(FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue);
        StringAssert.DoesNotContain("movingAnchor=missing", FlowFieldCrowdMovementSystem.GetEditorTestMovingTargetAnchorDiagnostics(chaser.LogicEntityId.Value));
    }

    [Test]
    public void MarkWorldDirty清空Anchor时必须同步清空StableGoal引用()
    {
        const int width = 16;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(14.5f, 0f, 3.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestStableGoal(chaser.LogicEntityId.Value, out _, out _, out _, out _, out _, out _));

        FlowFieldCrowdMovementSystem.MarkWorldDirty("editor-test");

        Assert.IsFalse(FlowFieldCrowdMovementSystem.TryGetEditorTestStableGoal(chaser.LogicEntityId.Value, out _, out _, out _, out _, out _, out _));
        StringAssert.Contains("movingAnchor=no-target", FlowFieldCrowdMovementSystem.GetEditorTestMovingTargetAnchorDiagnostics(chaser.LogicEntityId.Value));
    }

    [Test]
    public void 多AgentType共享场预排必须在各自NavigationWorld解析Island()
    {
        const int width = 8;
        const int height = 4;
        bool[] smallWalkable = new bool[width * height];
        bool[] largeWalkable = new bool[width * height];
        for (int x = 0; x < width; x++)
        {
            SetWalkable(smallWalkable, width, x, 2);
            SetWalkable(largeWalkable, width, x, 2);
        }
        SetWalkable(largeWalkable, width, 0, 0);
        SetWalkable(largeWalkable, width, 1, 0);

        int smallAgentType = AgentTypeHelper.SmallMovementTypeId;
        int largeAgentType = AgentTypeHelper.LargeMovementTypeId;
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(smallAgentType, 0.35f);
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(largeAgentType, 0.75f);
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSources(new[]
        {
            new AuthoredNavigationSourceData(smallAgentType, width, height, 1f, Vector3.zero, smallWalkable, null),
            new AuthoredNavigationSourceData(largeAgentType, width, height, 1f, Vector3.zero, largeWalkable, null)
        });
        SimEntityContext target = CreateEntity(new Vector3(7.5f, 0f, 2.5f), false, smallAgentType, 0.35f);
        SimEntityContext largeChaser = CreateEntity(new Vector3(0.5f, 0f, 2.5f), false, largeAgentType, 0.75f);
        SimEntityContext smallChaser = CreateEntity(new Vector3(1.5f, 0f, 2.5f), false, smallAgentType, 0.35f);
        largeChaser.TargetComp = new SimTargetingComp(largeChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        smallChaser.TargetComp = new SimTargetingComp(smallChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(largeChaser, target.Position, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(smallChaser, target.Position, 2f, out _));

        Assert.DoesNotThrow(FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue);
        StringAssert.DoesNotContain("movingAnchor=missing", FlowFieldCrowdMovementSystem.GetEditorTestMovingTargetAnchorDiagnostics(largeChaser.LogicEntityId.Value));
    }

    [Test]
    public void 移动目标仍在同一Sector时不应重建整条SectorPath()
    {
        const int width = 16;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(9.5f, 0f, 5.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(1);

        target.Position = new Vector3(10.5f, 0f, 5.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(128, 12.8f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 velocity));

        Assert.Greater(velocity.x, 0.5f, $"目标同 sector 移动后应继续沿已有 sector path 前进，只重建末端 tile，velocity={velocity}");
    }

    [Test]
    public void 同SectorPathHandle不应在查询热路径同步构建Integration()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 3.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        int integrationsBefore = FlowFieldCrowdMovementSystem.GetEditorTestFrameSynchronousSectorIntegrationCount();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, goal, 2f, out Vector3 velocity));

        Assert.Greater(velocity.x, 0.5f, $"同 sector 目标应直接使用局部连通判定后构建 tile，velocity={velocity}");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestFrameTileBuildCount(), "同 sector final tile 也不应在查询热路径同步构建");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), 0, "同 sector final tile 应进入预算队列");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringSource(chaser.LogicEntityId.Value, out int source, out bool hasLineOfSight));
        Assert.AreEqual(0, source, $"严格静态碰撞与 cost LOS 均畅通时应使用 fixed 直线捷径，source={source}");
        Assert.IsTrue(hasLineOfSight, "fixed 直线捷径必须建立在严格静态碰撞与 cost LOS 均畅通的前提上。");
        Assert.AreEqual(
            integrationsBefore,
            FlowFieldCrowdMovementSystem.GetEditorTestFrameSynchronousSectorIntegrationCount(),
            "同 sector path handle 构建不应再同步跑整块 sector integration");
    }

    [Test]
    public void 首次跨Sector查询不应同步构建SharedGoalFieldIntegration()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 32);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 24;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext firstChaser = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        SimEntityContext secondChaser = CreateEntity(new Vector3(0.5f, 0f, 5.5f));
        SimEntityContext target = CreateEntity(new Vector3(23.5f, 0f, 3.5f));
        firstChaser.TargetComp = new SimTargetingComp(firstChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        secondChaser.TargetComp = new SimTargetingComp(secondChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        int integrationsBefore = FlowFieldCrowdMovementSystem.GetEditorTestFrameSynchronousSectorIntegrationCount();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(firstChaser, target.Position, 2f, out Vector3 firstVelocity));
        int integrationsAfterFirst = FlowFieldCrowdMovementSystem.GetEditorTestFrameSynchronousSectorIntegrationCount();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(secondChaser, target.Position, 2f, out Vector3 secondVelocity));
        int integrationsAfterSecond = FlowFieldCrowdMovementSystem.GetEditorTestFrameSynchronousSectorIntegrationCount();

        Assert.Greater(firstVelocity.x, 0.5f, $"第一个追击者应朝跨 sector 目标前进 first={firstVelocity}");
        Assert.Greater(secondVelocity.x, 0.5f, $"第二个追击者应朝跨 sector 目标前进 second={secondVelocity}");
        Assert.AreEqual(integrationsBefore, integrationsAfterFirst, "首次跨 sector 查询应只跑轻量 portal graph，不应同步构建 SharedGoalField integration");
        Assert.AreEqual(integrationsAfterFirst, integrationsAfterSecond, "同一帧后续追击者也不应在查询热路径构建 SharedGoalField integration");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount(), "查询热路径不应直接操作 SharedGoalField 队列");
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount(), 0, "SharedGoalField 应由帧队列 active-demand 阶段入队并分帧完成");
    }

    [Test]
    public void 查询热路径只提交CurrentTile而不构建非末端PortalTile链()
    {
        const int width = 24;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(23.5f, 0f, 3.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 velocity));

        int tileCount = FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount();
        int tileBuildCount = FlowFieldCrowdMovementSystem.GetEditorTestFrameTileBuildCount();
        Assert.Greater(velocity.x, 0.5f, $"长路径首段仍应沿走廊前进，velocity={velocity}");
        Assert.AreEqual(1, tileCount, $"首次权威消费只应精确提交 current portal tile，tileCount={tileCount}");
        Assert.AreEqual(1, FlowFieldCrowdMovementSystem.GetEditorTestRequiredFlowTileCommitCount(),
            "非末端 current portal tile 不应带上 lookahead tile 一起同步提交。");
        Assert.AreEqual(0, tileBuildCount, $"旧的同步整链 build 入口必须保持停用，tileBuilds={tileBuildCount}");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), 0, "lookahead portal tile 链应保留在预算队列");
    }

    [Test]
    public void PortalGraph不应为了复用旧Path牺牲更短Portal()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 24;
        const int height = 24;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
        {
            SetWalkable(walkable, width, x, 5);
            SetWalkable(walkable, width, x, 18);
        }

        for (int y = 5; y <= 18; y++)
        {
            SetWalkable(walkable, width, 1, y);
            SetWalkable(walkable, width, 22, y);
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext upperChaser = CreateEntity(new Vector3(1.5f, 0f, 5.5f));
        SimEntityContext lowerChaser = CreateEntity(new Vector3(1.5f, 0f, 18.5f));
        SimEntityContext target = CreateEntity(new Vector3(22.5f, 0f, 11.5f));
        upperChaser.TargetComp = new SimTargetingComp(upperChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        lowerChaser.TargetComp = new SimTargetingComp(lowerChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(upperChaser, target.Position, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(upperChaser.LogicEntityId.Value, out int[] upperPortals));
        Vector3 upperVelocity = ResolveDeterministicFlowVelocityAfterQueue(upperChaser, target.Position, 2f, 2, out _);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(512, 51.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(lowerChaser, target.Position, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(lowerChaser.LogicEntityId.Value, out int[] lowerPortals));
        Vector3 lowerVelocity = ResolveDeterministicFlowVelocityAfterQueue(lowerChaser, target.Position, 2f, 513, out _);

        Assert.Greater(upperVelocity.x, 0.5f, $"上路单位应建立可用 path，upper={upperVelocity}");
        Assert.Greater(lowerVelocity.x, 0.5f, $"下路单位应建立可用 path，lower={lowerVelocity}");
        Assert.AreNotEqual(upperPortals[0], lowerPortals[0], $"第二个单位应选择自身最近的下路 portal，而不是 merge 到第一个单位旧 path。upper=[{string.Join(",", upperPortals)}] lower=[{string.Join(",", lowerPortals)}]");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestFramePortalGraphMergeHitCount(), "portal graph 不应再用提前 merge 改变最短路选择");
    }

    [Test]
    public void PortalGraph会在不牺牲路径代价时合并到既有PathSuffix()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 32;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 3);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext first = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext second = CreateEntity(new Vector3(2.5f, 0f, 3.5f));
        Vector3 goal = new Vector3(31.5f, 0f, 3.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(first, goal, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(first.LogicEntityId.Value, out int[] firstPortals));
        Vector3 firstVelocity = ResolveDeterministicFlowVelocityAfterQueue(first, goal, 2f, 2, out _);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(512, 51.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(second, goal, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(second.LogicEntityId.Value, out int[] secondPortals));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathBuildSource(second.LogicEntityId.Value, out string buildSource));
        int mergeHits = FlowFieldCrowdMovementSystem.GetEditorTestFramePortalGraphMergeHitCount();
        Vector3 secondVelocity = ResolveDeterministicFlowVelocityAfterQueue(second, goal, 2f, 513, out _);

        Assert.Greater(firstVelocity.x, 0.5f, $"第一个单位应建立可用 path，velocity={firstVelocity}");
        Assert.Greater(secondVelocity.x, 0.5f, $"第二个单位应沿合并后的 path 推进，velocity={secondVelocity}");
        Assert.AreEqual("portalGraphMerged", buildSource, $"第二个单位应通过 merging A* 拼接既有 path suffix，source={buildSource}");
        Assert.AreEqual(firstPortals[firstPortals.Length - 1], secondPortals[secondPortals.Length - 1], $"合并后的尾段应复用同一终点 portal，first=[{string.Join(",", firstPortals)}] second=[{string.Join(",", secondPortals)}]");
        Assert.GreaterOrEqual(mergeHits, 1, "merge 命中应计入 portalGraphMergeHits");
    }

    [Test]
    public void 移动目标换格后会重建PortalPath而不是沿旧Portal链绕远()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 24;
        const int height = 24;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
        {
            SetWalkable(walkable, width, x, 5);
            SetWalkable(walkable, width, x, 18);
        }

        for (int y = 5; y <= 18; y++)
        {
            SetWalkable(walkable, width, 1, y);
            SetWalkable(walkable, width, 22, y);
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(1.5f, 0f, 18.5f));
        Vector3 upperGoal = new Vector3(22.5f, 0f, 5.5f);
        Vector3 lowerGoal = new Vector3(22.5f, 0f, 18.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, upperGoal, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(chaser.LogicEntityId.Value, out int[] upperPortals));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, lowerGoal, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(chaser.LogicEntityId.Value, out int[] lowerPortals));
        Vector3 lowerVelocity = ResolveDeterministicFlowVelocityAfterQueue(chaser, lowerGoal, 2f, 3, out _);

        Assert.AreNotEqual(upperPortals[0], lowerPortals[0], $"目标换到下路后应重建 portal path，而不是继续沿旧上路 portal。upper=[{string.Join(",", upperPortals)}] lower=[{string.Join(",", lowerPortals)}]");
        Assert.Greater(lowerVelocity.x, 0.5f, $"重建后仍应沿下路推进，velocity={lowerVelocity}");
    }

    [Test]
    public void 移动目标跨Sector重规划不会改写已提交的当前Portal()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 24;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        walkable[7 + 3 * width] = false;
        walkable[8 + 3 * width] = false;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(2.5f, 0f, 1.5f));
        SimEntityContext target = CreateEntity(new Vector3(20.5f, 0f, 1.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Vector3 initialVelocity = ResolveDeterministicFlowVelocityAfterQueue(chaser, target.Position, 2f, 2, out string initialDiagnostic);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCurrentPathSegment(
            chaser.LogicEntityId.Value,
            out int initialPathIndex,
            out int committedPortalId,
            out _));
        Assert.AreEqual(0, initialPathIndex);
        Assert.GreaterOrEqual(committedPortalId, 0, initialDiagnostic);

        target.Position = new Vector3(20.5f, 0f, 10.5f);
        bool promoted = false;
        Vector3 replannedVelocity = Vector3.zero;
        for (int frame = 600; frame < 1200; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out replannedVelocity));
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            if (FlowFieldCrowdMovementSystem.TryGetEditorTestStableGoal(
                    chaser.LogicEntityId.Value,
                    out _,
                    out _,
                    out _,
                    out _,
                    out int activeGoalY,
                    out _)
                && activeGoalY == 10)
            {
                promoted = true;
                break;
            }
        }

        Assert.IsTrue(promoted, "移动目标的新 shared goal field 必须在测试预算内晋升。");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCurrentPathSegment(
            chaser.LogicEntityId.Value,
            out int replannedPathIndex,
            out int replannedPortalId,
            out _));
        Assert.AreEqual(0, replannedPathIndex);
        Assert.AreEqual(committedPortalId, replannedPortalId,
            "已消费 committed tile 的当前 portal 是 corridor 前缀；目标移动只能重规划其后缀，不能在 sector 内换出口。");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathBuildSource(
            chaser.LogicEntityId.Value,
            out string buildSource));
        Assert.AreEqual("committedPortalPrefix", buildSource);
        Assert.AreEqual(0f, Vector3.Angle(initialVelocity, replannedVelocity), 0.01f,
            $"目标移动只能重规划 committed tile 之后的后缀；当前位置未变时当前局部场方向必须保持一致，initial={initialVelocity}, replanned={replannedVelocity}。");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            chaser.LogicEntityId.Value,
            out string replannedDiagnostic));
        StringAssert.Contains("/cached=True/", replannedDiagnostic,
            $"已提交的当前局部场不能因后缀变化退回 pending，diagnostic={replannedDiagnostic}");

        chaser.Position = new Vector3(8.5f, 0f, 1.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1200, 120f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 crossedVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCurrentPathSegment(
            chaser.LogicEntityId.Value,
            out int crossedPathIndex,
            out int crossedPortalId,
            out _));
        Assert.AreEqual(1, crossedPathIndex, "跨过已提交 portal 后必须进入按最新目标重规划的后缀。");
        Assert.AreNotEqual(committedPortalId, crossedPortalId, "跨过 committed prefix 后不能继续把旧 portal 当作当前出口。");
        Assert.Greater(crossedVelocity.sqrMagnitude, 0.01f, "跨过 committed prefix 后必须继续追击最新目标。");
    }

    [Test]
    public void PortalFunnel_ApertureCellPastCommittedSeedDoesNotSteerBackToPortalEndpoint()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 24;
        const int height = 24;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        for (int x = 3; x < width; x++)
        {
            walkable[x + 7 * width] = false;
            walkable[x + 8 * width] = false;
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(1.5f, 0f, 1.5f));
        SimEntityContext target = CreateEntity(new Vector3(20.5f, 0f, 20.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        const float speed = 30f;
        const float deltaTime = 1f / 30f;
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, deltaTime);
        Vector3 initialVelocity = ResolveDeterministicFlowVelocityAfterQueue(
            chaser,
            target.Position,
            speed,
            2,
            out string initialDiagnostic);
        Assert.Greater(initialVelocity.z, 0.1f, initialDiagnostic);

        Vector3 previousVelocity = initialVelocity;
        bool reachedCurrentPortalSeed = false;
        bool seedConsumedCurrentPortal = false;
        float minimumConsecutiveDot = float.PositiveInfinity;
        string minimumDotDiagnostic = string.Empty;
        string seedFunnelDiagnostic = string.Empty;
        for (int frame = 3; frame < 32; frame++)
        {
            chaser.Position += previousVelocity * deltaTime;
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * deltaTime);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(
                chaser,
                target.Position,
                speed,
                out Vector3 velocity));
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
                chaser.LogicEntityId.Value,
                out string diagnostic));

            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCurrentPathSegment(
                chaser.LogicEntityId.Value,
                out _,
                out _,
                out bool isOnCurrentPortalCell));
            bool firstCurrentPortalSeed = !reachedCurrentPortalSeed
                                          && isOnCurrentPortalCell;
            reachedCurrentPortalSeed |= firstCurrentPortalSeed;
            if (firstCurrentPortalSeed)
            {
                seedConsumedCurrentPortal = !diagnostic.Contains("portalLookahead=rejected", StringComparison.Ordinal);
                seedFunnelDiagnostic =
                    $"frame={frame}, position={chaser.Position}, previous={previousVelocity}, velocity={velocity}, flow={diagnostic}";
            }
            if (reachedCurrentPortalSeed
                && previousVelocity.sqrMagnitude > 0.01f
                && velocity.sqrMagnitude > 0.01f)
            {
                float dot = Vector3.Dot(previousVelocity.normalized, velocity.normalized);
                if (dot < minimumConsecutiveDot)
                {
                    minimumConsecutiveDot = dot;
                    minimumDotDiagnostic = diagnostic;
                }
            }

            previousVelocity = velocity;
        }

        Assert.IsTrue(reachedCurrentPortalSeed, "The scenario must consume a current-side portal seed before validating the handoff.");
        Assert.IsFalse(
            seedConsumedCurrentPortal,
            $"A current-side seed cell is not a portal-plane crossing; an invalid lookahead must retain the current safe-center portal. {seedFunnelDiagnostic}");
        Assert.GreaterOrEqual(
            minimumConsecutiveDot,
            0f,
            $"Reaching the current-side seed is not a portal-plane crossing; the next 30 Hz step must not leave the aperture tangentially and reverse. minDot={minimumConsecutiveDot}, diagnostic={minimumDotDiagnostic}");
    }

    [Test]
    public void 长路径CurrentTile正式提交后可沿可见PortalCorridorFunnel推进()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        SetNavigationWorkQuotas(config, 32);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 32;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(31.5f, 0f, 5.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, goal, 2f, out Vector3 velocity));
        Assert.Greater(velocity.x, 0.5f, $"长路径 current tile 应沿正式 Portal 势推进，velocity={velocity}");

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringSource(chaser.LogicEntityId.Value, out _, out bool hasLineOfSight));
        Assert.IsFalse(hasLineOfSight, "Portal 分段必须由积分势保持单一 steering 权威，不能切到最终目标 LOS。");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            chaser.LogicEntityId.Value,
            out string diagnostic));
        StringAssert.Contains("/cached=True/", diagnostic);
        StringAssert.Contains("/lastResult=corridor-funnel ", diagnostic,
            "完整 Portal corridor 可见时应由已提交 corridor 的 funnel 推进，不能伪装成无 corridor 的最终目标 LOS。");

        ProcessFlowTileBuildQueueUntilTileReady(0, 2);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(512, 51.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, goal, 2f, out velocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
            chaser.LogicEntityId.Value,
            out diagnostic));
        StringAssert.Contains("/lastResult=corridor-funnel ", diagnostic,
            "Portal tile 提交后，完整 corridor 可见时应保持 string-pulling 权威；局部积分势仅负责不可直达段。");
    }

    [Test]
    public void FlowTileBuildQueue会在后续查询前预构建路径Tile链()
    {
        const int width = 24;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(23.5f, 0f, 3.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));

        FlowFieldCrowdMovementSystem.ClearEditorTestFlowTileCache();
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount());

        int queuedTileCommitCount = 0;
        for (int frame = 2; frame < 64 && FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount() <= 1; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            int cacheCountBefore = FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount();
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            queuedTileCommitCount += FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount() - cacheCountBefore;
        }

        int queuedTileCount = FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount();
        Assert.Greater(queuedTileCount, 1, $"flow tile queue 应能在后续查询前预构建下游 tile 链，tileCount={queuedTileCount}");
        Assert.AreEqual(queuedTileCount, queuedTileCommitCount, $"预构建 tile 数应等于实际提交数，tileCount={queuedTileCount} commits={queuedTileCommitCount}");

        FlowFieldCrowdMovementSystem.SetEditorTestClock(3, 0.3f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 velocity));
        Assert.Greater(velocity.x, 0.5f, $"预构建 tile 命中后仍应沿走廊前进，velocity={velocity}");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestFrameTileBuildCount(), "查询命中预构建 tile 后当前查询帧不应再次构建 tile");
    }

    [Test]
    public void FlowTileBuildQueue同批多Tile只刷新一次引用()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        config.DeterministicFlowTileCommitQuota = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 24;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        Vector3 goal = new Vector3(23.5f, 0f, 3.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryPrepareNavigationRequest(chaser, goal, out string failureReason), failureReason);
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), 1, "测试必须形成同批多 tile 提交。");

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount(), 1, "同批必须实际提交多个 tile。");
        Assert.AreEqual(
            2,
            FlowFieldCrowdMovementSystem.GetEditorTestFlowTileReferenceRefreshCount(),
            "每个 world 在提交前刷新一次，整批提交后再刷新一次；不得按每个 tile 重扫全部 agent path。");
    }

    [Test]
    public void NavigationRequest会在Move前提交路径Tile链()
    {
        const int width = 24;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        Vector3 goal = new Vector3(23.5f, 0f, 3.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryPrepareNavigationRequest(chaser, goal, out string failureReason), failureReason);
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), 0, "path request 应立即提交 flow tile build jobs");

        for (int frame = 2; frame < 64 && FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount() <= 1; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        }

        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount(), 1, "Move 前提交的请求应能被队列预构建为路径 tile 链");

        FlowFieldCrowdMovementSystem.SetEditorTestClock(64, 6.4f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, goal, 2f, out Vector3 velocity));
        Assert.Greater(velocity.x, 0.5f, $"预提交请求后 steering 应命中 flow 链继续前进，velocity={velocity}");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestFrameTileBuildCount(), "预提交命中后 steering 查询帧不应同步构建 tile");
    }

    [Test]
    public void FlowTileBuildQueue低预算会保留未完成TileJob()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 32);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 48;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 7.5f));
        SimEntityContext target = CreateEntity(new Vector3(47.5f, 0f, 7.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));

        FlowFieldCrowdMovementSystem.ClearEditorTestFlowTileCache();
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));

        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), 0, "低预算下 flow tile queue 应保留未完成 job，而不是同步一次做完整条 tile 链");

        for (int frame = 3; frame < 256 && FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount() > 0; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        }

        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), "flow tile queue 应在后续预算帧内完成");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount(), 1, "完成后应提交预构建 tile 链");
    }

    [Test]
    public void FlowAuthority_DoesNotRetainFloatMovingTargetMutationChain()
    {
        BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        Assert.IsNull(typeof(FlowFieldCrowdMovementSystem).GetMethod("TryResolveStableGoalCell", flags));
        Assert.IsNull(typeof(FlowFieldCrowdMovementSystem).GetMethod("TryResolveReachableNavigationPointCell", flags));
        Assert.IsNull(typeof(FlowFieldCrowdMovementSystem).GetMethod("SetMovingTargetActiveGoal", flags));
        Assert.IsNull(typeof(FlowFieldCrowdMovementSystem).GetMethod("SetMovingTargetPendingGoal", flags));
        Assert.IsNull(typeof(FlowFieldCrowdMovementSystem).GetMethod("TryResolveStartCellForReachability", flags));
        Assert.IsNull(typeof(FlowFieldCrowdMovementSystem).GetMethod("TryResolveReachableGoalCell", flags));
        Assert.IsNull(typeof(FlowFieldCrowdMovementSystem).GetMethod("TryResolveGoalCell", flags));
    }

    [Test]
    public void 移动目标切换不会制造旧TileJob洪峰且当前Tile保持前排()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        SetNavigationWorkQuotas(config, 32);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 80;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 7.5f));
        SimEntityContext target = CreateEntity(new Vector3(79.5f, 0f, 7.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        int initialQueueCount = -1;
        int peakQueueCount = 0;
        for (int frame = 1; frame <= 24; frame++)
        {
            target.Position = new Vector3(79.5f - frame * 0.5f, 0f, 4.5f + frame % 7);
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
                chaser,
                target.LogicFramePositionFixed(),
                (Fix64)2,
                out _));
            int queueCount = FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount();
            if (initialQueueCount < 0)
                initialQueueCount = queueCount;
            peakQueueCount = Mathf.Max(peakQueueCount, queueCount);
        }

        Assert.Greater(initialQueueCount, 0, "长路径预提交必须生成活动 tile chain。 ");
        Assert.AreEqual(initialQueueCount, peakQueueCount,
            $"移动目标 pending SharedGoalField 未完成前不得同步晋升并在活动 tile chain 之外累积旧 job。initial={initialQueueCount}, peak={peakQueueCount}");
        target.Position = new Vector3(79.5f, 0f, 7.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(32, 3.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
            chaser,
            target.LogicFramePositionFixed(),
            (Fix64)2,
            out _));
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

        int currentQueueIndex = FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildQueueIndex(chaser.LogicEntityId.Value);
        Assert.LessOrEqual(currentQueueIndex, 8,
            $"当前活动单位所需 tile 必须保持在有界队列前部。index={currentQueueIndex}, peak={peakQueueCount}");
    }

    [Test]
    public void FlowTileCache只保留ActivePath当前窗口并遵守容量上限()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.FlowTileCacheLimit = 16;
        SetNavigationWorkQuotas(config, 1_000_000);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 96;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(95.5f, 0f, 3.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));

        for (int frame = 2; frame < 256 && FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount() > 0; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        }

        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), "长路径 flow tile chain 应在预算帧内完成");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount(), 0, "active path 当前窗口应保留已构建 flow tile");
        Assert.LessOrEqual(FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount(), 16, "flow tile cache 应遵守容量上限，远端路径由 active window 按需预建");
    }

    [Test]
    public void LongSession_十万Tick移动目标压力下缓存队列与进程内存保持有界()
    {
        const int totalFrames = 100000;
        const int warmupFrames = 10000;
        const long managedGrowthLimit = 16L * 1024L * 1024L;
        const long processReservedGrowthLimit = 128L * 1024L * 1024L;

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        config.FlowTileCacheLimit = 16;
        SetNavigationWorkQuotas(config, 64);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 64;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        SimEntityContext target = CreateEntity(new Vector3(48.5f, 0f, 7.5f));
        var chasers = new[]
        {
            CreateEntity(new Vector3(0.5f, 0f, 1.5f)),
            CreateEntity(new Vector3(1.5f, 0f, 5.5f)),
            CreateEntity(new Vector3(2.5f, 0f, 9.5f)),
            CreateEntity(new Vector3(3.5f, 0f, 13.5f)),
        };
        var targetList = new List<IEntityContext> { target };
        for (int i = 0; i < chasers.Length; i++)
        {
            chasers[i].TargetComp = new SimTargetingComp(chasers[i], targetList)
            {
                CurrentTarget = target
            };
        }

        long managedBefore = 0;
        long processReservedBefore = 0;
        int peakFlowTiles = 0;
        int peakSharedGoals = 0;
        int peakPendingFlowTiles = 0;
        int peakPendingSharedGoals = 0;
        ulong lastDigest = 0;

        for (int frame = 1; frame <= totalFrames; frame++)
        {
            int targetStep = (frame / 8) & 31;
            int targetX = 32 + targetStep;
            int targetY = 2 + ((targetStep * 5) % 12);
            target.Position = new Vector3(targetX + 0.5f, 0f, targetY + 0.5f);
            FixVector2 targetFixed = target.LogicFramePositionFixed();

            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame / 30f);
            for (int i = 0; i < chasers.Length; i++)
            {
                Assert.IsTrue(
                    FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
                        chasers[i],
                        targetFixed,
                        (Fix64)2,
                        out _),
                    $"fixed steering 必须在长局压力中持续可用。frame={frame}, chaser={i}");
            }

            peakPendingFlowTiles = Math.Max(
                peakPendingFlowTiles,
                FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount());
            peakPendingSharedGoals = Math.Max(
                peakPendingSharedGoals,
                FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount());

            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            var hasher = new LogicStateHasher();
            FlowFieldCrowdMovementSystem.WriteDeterministicFrameDigest(hasher);
            lastDigest = hasher.Hash;

            peakFlowTiles = Math.Max(
                peakFlowTiles,
                FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount());
            peakSharedGoals = Math.Max(
                peakSharedGoals,
                FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalFieldCacheCount());

            if (frame == warmupFrames)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                managedBefore = GC.GetTotalMemory(true);
                processReservedBefore = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
                if (processReservedBefore <= 0)
                    throw new InvalidOperationException("LongSession flow memory gate failed: Unity process reserved memory is unavailable.");
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long managedAfter = GC.GetTotalMemory(true);
        long processReservedAfter = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();

        long managedGrowth = Math.Max(0L, managedAfter - managedBefore);
        long processReservedGrowth = Math.Max(0L, processReservedAfter - processReservedBefore);
        string diagnostics =
            $"digest={lastDigest} peakFlow={peakFlowTiles} peakShared={peakSharedGoals} " +
            $"peakPendingFlow={peakPendingFlowTiles} peakPendingShared={peakPendingSharedGoals} " +
            $"managedBefore={managedBefore} managedAfter={managedAfter} managedGrowth={managedGrowth} " +
            $"processReservedBefore={processReservedBefore} processReservedAfter={processReservedAfter} " +
            $"processReservedGrowth={processReservedGrowth}";
        Debug.Log($"[LongSessionFlowMemory] {diagnostics}");

        Assert.Greater(peakPendingFlowTiles, 0, $"压力场景必须实际推进 flow tile job。{diagnostics}");
        Assert.Greater(peakPendingSharedGoals, 0, $"压力场景必须实际推进 shared goal job。{diagnostics}");
        Assert.LessOrEqual(peakFlowTiles, config.FlowTileCacheLimit, $"flow tile cache 必须始终受配置上限约束。{diagnostics}");
        Assert.LessOrEqual(peakSharedGoals, 16, $"shared goal cache 必须始终受派生上限约束。{diagnostics}");
        Assert.LessOrEqual(peakPendingFlowTiles, 128, $"flow tile pending queue 不得随 Tick 线性增长。{diagnostics}");
        Assert.LessOrEqual(peakPendingSharedGoals, 16, $"shared goal pending queue 不得随 Tick 线性增长。{diagnostics}");
        Assert.AreEqual(
            0,
            FlowFieldCrowdMovementSystem.GetEditorTestDuplicatePendingFlowTileBuildKeyCount(),
            $"flow tile pending queue 不得保留重复 key。{diagnostics}");
        Assert.LessOrEqual(managedGrowth, managedGrowthLimit, $"10 万 Tick 后 managed retained memory 超出门禁。{diagnostics}");
        Assert.LessOrEqual(
            processReservedGrowth,
            processReservedGrowthLimit,
            $"10 万 Tick 后 Unity 进程 reserved memory 超出门禁。{diagnostics}");
    }

    [Test]
    public void PortalChoice诊断不应构建SharedGoalField()
    {
        const int width = 24;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        Vector3 sameSectorGoal = new Vector3(6.5f, 0f, 3.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, sameSectorGoal, 2f, out _));
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalFieldCacheCount(), "同 sector 请求不应创建 SharedGoalField");

        string diagnostics = FlowFieldCrowdMovementSystem.GetEditorTestStartPortalChoiceDiagnostics(0, 2, 0, 3, 23, 3, 0);
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalFieldCacheCount(), $"portal choice 诊断应只读缓存，不应构建 SharedGoalField，diag={diagnostics}");
        StringAssert.Contains("shared-field-not-cached", diagnostics);
    }

    [Test]
    public void FlowTileBuildQueue应按TileKey去重而不是按PathHandle去重()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 32);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 48;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext firstChaser = CreateEntity(new Vector3(0.5f, 0f, 7.5f));
        SimEntityContext secondChaser = CreateEntity(new Vector3(1.5f, 0f, 7.5f));
        SimEntityContext target = CreateEntity(new Vector3(47.5f, 0f, 7.5f));
        firstChaser.TargetComp = new SimTargetingComp(firstChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        secondChaser.TargetComp = new SimTargetingComp(secondChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(firstChaser, target.Position, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(secondChaser, target.Position, 2f, out _));

        FlowFieldCrowdMovementSystem.ClearEditorTestFlowTileCache();
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(firstChaser, target.Position, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(secondChaser, target.Position, 2f, out _));

        int pendingCount = FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount();
        Assert.Greater(pendingCount, 0, "低预算下应留下未完成 tile job");
        int duplicateCount = FlowFieldCrowdMovementSystem.GetEditorTestDuplicatePendingFlowTileBuildKeyCount();
        Assert.AreEqual(0, duplicateCount, $"同一 tile key 不应因不同 PathHandleId 重复排队，pending={pendingCount} duplicate={duplicateCount}");
    }

    [Test]
    public void SharedGoalFieldBuildQueue低预算会保留并完成Job()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 32);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 48;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 7.5f));
        SimEntityContext target = CreateEntity(new Vector3(47.5f, 0f, 7.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        int sharedBuildsBefore = FlowFieldCrowdMovementSystem.GetEditorTestFrameSharedGoalFieldBuildCount();
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

        Assert.GreaterOrEqual(FlowFieldCrowdMovementSystem.GetEditorTestFrameSharedGoalFieldBuildCount(), sharedBuildsBefore, "shared goal field queue 应接入 flow rebuild 预算入口");

        for (int frame = 3; frame < 256 && FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount() > 0; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        }

        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount(), "shared goal field queue 应在后续预算帧内完成");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalFieldCacheCount(), 0, "完成后应提交 SharedGoalField cache");
    }

    [Test]
    public void Portal格直接保留DeterministicFlowDirection()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 16;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(6.5f, 0f, 6.5f));
        Vector3 goal = new Vector3(10.5f, 0f, 6.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileReady(6, 6);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlags(6, 6, out bool hasLos, out _, out bool pathable));
        Assert.IsTrue(pathable, "portal tile 当前格应可走");
        Assert.IsFalse(hasLos, "portal tile 不应再携带旧 float LOS shadow");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellStoredFlowDirection(6, 6, out Vector2 storedFlow));
        Assert.Greater(storedFlow.sqrMagnitude, 0.0001f, $"portal 普通格必须存储 deterministic flow，diag={FlowFieldCrowdMovementSystem.GetEditorTestCachedTileCellDiagnostic(6, 6)}");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlowDirection(6, 6, out Vector2 runtimeFlow));
        Assert.AreEqual(storedFlow, runtimeFlow, $"诊断运行方向必须直接反映 deterministic flow，diag={FlowFieldCrowdMovementSystem.GetEditorTestCachedTileCellDiagnostic(6, 6)}");
    }

    [Test]
    public void DeterministicTile方向不会指向不可穿越邻格()
    {
        const int width = 8;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        walkable[5 + 2 * width] = false;
        walkable[5 + 3 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(2);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellStoredFlowDirection(4, 2, out Vector2 flow));
        Assert.Greater(flow.sqrMagnitude, 0.0001f, "障碍前的可达格必须有 deterministic direction");
        int nextX = 4 + Mathf.RoundToInt(flow.x);
        int nextY = 2 + Mathf.RoundToInt(flow.y);
        Assert.AreNotEqual(new Vector2Int(5, 2), new Vector2Int(nextX, nextY),
            $"deterministic direction 不得指向阻塞邻格，flow={flow}");
    }

    [Test]
    public void 移动目标跨Sector时立即切换到共享目标场()
    {
        const int width = 16;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(2.5f, 0f, 6.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 initialVelocity));

        target.Position = new Vector3(2.5f, 0f, 0.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.5f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 oldFlowVelocity));

        Assert.Greater(initialVelocity.z, 0.15f, $"初始目标在上方，应有上行分量 initial={initialVelocity}");
        Assert.Less(oldFlowVelocity.z, -0.15f, $"目标跨 sector 后应立即切到新共享目标场 oldFlow={oldFlowVelocity}");
    }

    [Test]
    public void 固定点导航目标变化仍会立即响应()
    {
        const int width = 16;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(8.5f, 0f, 1.5f));

        Vector3 rightVelocity = ResolveDeterministicFlowVelocityAfterQueue(
            ctx,
            new Vector3(12.5f, 0f, 1.5f),
            2f,
            1,
            out _);
        Vector3 leftVelocity = ResolveDeterministicFlowVelocityAfterQueue(
            ctx,
            new Vector3(4.5f, 0f, 1.5f),
            2f,
            512,
            out _);

        Assert.Greater(rightVelocity.x, 0.5f, $"初始固定点应向右，velocity={rightVelocity}");
        Assert.Less(leftVelocity.x, -0.5f, $"固定点改变应立即向左，velocity={leftVelocity}");
    }

    [Test]
    public void 贴墙手动位移约束不应翻转到远离输入方向()
    {
        const int width = 5;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (y >= 1)
                    SetWalkable(walkable, width, x, y);
            }
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        Vector3 position = new Vector3(2.5f, 0f, 1.35f);
        Vector3 desired = new Vector3(0.2f, 0f, -0.2f);
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryConstrainNavigationDisplacement(
                position,
                desired,
                0,
                0.35f,
                out Vector3 constrained),
            "贴墙手动位移约束应成功。");

        Assert.Greater(
            Vector3.Dot(desired.normalized, constrained.normalized),
            0.15f,
            $"约束不能把输入翻到近乎垂直或反向。desired={desired} constrained={constrained}");
        Assert.LessOrEqual(
            constrained.z,
            0.001f,
            $"贴下侧墙输入右下时，约束不应翻成右上。desired={desired} constrained={constrained}");
    }

    [Test]
    public void Lv2已按兵种半径侵蚀的合法边缘格不应被执行层重复收缩()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv2_FlowNavigationGrid_Small.asset");
        Assert.NotNull(grid, "真实净空回归必须直接使用 Lv2_FlowNavigationGrid_Small.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv2_FlowNavigationGrid_Small.asset 必须带预烘焙 derived navigation data。");
        Assert.IsTrue(derivedData.IsValid, "Lv2_FlowNavigationGrid_Small.asset 的 derived navigation data 必须有效。");

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            derivedData,
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();
        bool[] walkableMask = grid.GetWalkableMaskRuntimeReadOnlyReference();

        const float agentRadius = 0.182f;
        Vector3 loggedPosition = new Vector3(24.53f, 1.5f, 41.74f);
        Vector3 legalDisplacement = new Vector3(3.94f, 0f, 0f) * 0.05f;
        Assert.IsTrue(grid.WorldToCell(loggedPosition, out int startX, out int startY));
        Assert.IsTrue(grid.WorldToCell(loggedPosition + legalDisplacement, out int endX, out int endY));
        Assert.IsTrue(walkableMask[startX + startY * grid.Width], $"实机日志起点必须仍是可走格，cell=({startX},{startY})。");
        Assert.IsTrue(walkableMask[endX + endY * grid.Width], $"实机日志预测终点必须仍是可走格，cell=({endX},{endY})。");

        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryConstrainNavigationDisplacement(
                loggedPosition,
                legalDisplacement,
                grid.AgentTypeId,
                agentRadius,
                out Vector3 constrained),
            "已按兵种半径侵蚀的合法格内位移约束应成功。");
        Assert.GreaterOrEqual(
            constrained.x,
            legalDisplacement.x * 0.9f,
            $"合法边缘格内位移不应被执行层再次按完整单位半径截短。desired={legalDisplacement} constrained={constrained} start=({startX},{startY}) end=({endX},{endY})");
        Assert.AreEqual(0f, constrained.z, 0.01f, $"合法直行不应产生额外侧移。desired={legalDisplacement} constrained={constrained}");

        Vector3 boundaryPosition = grid.GetCellAnchor(endX, endY);
        Vector3 outwardDisplacement = Vector3.right * (grid.CellSize * 1.25f);
        Assert.IsTrue(grid.WorldToCell(boundaryPosition + outwardDisplacement, out int blockedX, out int blockedY));
        Assert.IsFalse(walkableMask[blockedX + blockedY * grid.Width], $"边界回归终点必须是不可走格，cell=({blockedX},{blockedY})。");
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryConstrainNavigationDisplacement(
                boundaryPosition,
                outwardDisplacement,
                grid.AgentTypeId,
                agentRadius,
                out Vector3 boundaryConstrained),
            "朝不可走格的边界位移应被成功约束。");
        Vector3 boundaryResult = boundaryPosition + boundaryConstrained;
        Assert.IsTrue(grid.WorldToCell(boundaryResult, out int resultX, out int resultY));
        Assert.IsTrue(
            walkableMask[resultX + resultY * grid.Width],
            $"扣除资产已编码净空后仍必须阻止中心进入不可走格。desired={outwardDisplacement} constrained={boundaryConstrained} result=({resultX},{resultY})");
    }

    [Test]
    public void Lv3真实坏点接敌链路不应在PendingPortal阶段朝建筑正面走()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Small.asset");
        Assert.NotNull(grid, "真实坏点回归必须直接使用 Lv3_FlowNavigationGrid_Small.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid_Small.asset 必须带预烘焙 derived navigation data。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid_Small.asset 的 derived navigation data 必须有效。");

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 256;
        SetNavigationWorkQuotas(config, 1_000_000);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        const int startX = 758;
        const int startY = 36;
        const int goalX = 832;
        const int goalY = 37;
        Vector3 start = grid.GetCellAnchor(startX, startY);
        Vector3 goal = grid.GetCellAnchor(goalX, goalY);
        int startSectorId = ResolveDerivedSectorId(derivedData, startX, startY);
        int goalSectorId = ResolveDerivedSectorId(derivedData, goalX, goalY);

        SimEntityContext hero = CreateEntity(goal, false, grid.AgentTypeId, 0.45f);
        hero.Side = SideType.PlayerSide;
        SimEntityContext chaser = CreateEntity(start, false, grid.AgentTypeId, 0.45f);
        chaser.Side = SideType.EnemySide;
        chaser.SetProperty(CreatureMainProperty.Speed, (Fix64)80f);
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { hero })
        {
            CurrentTarget = hero
        };

        CharacterMoveComp moveComp = new CharacterMoveComp();
        moveComp.Init(chaser, grid.AgentTypeId);
        chaser.MoveComp = moveComp;
        SimMoveExecutor executor = new SimMoveExecutor
        {
            Position = chaser.Position,
            ApplyNavigationConstraint = true,
            AgentTypeId = grid.AgentTypeId,
            EdgeClearance = 0.5f
        };
        chaser.MoveExecutor = executor;

        const float dt = 0.1f;
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, dt);
        moveComp.MoveToFixed(hero.PositionFixed);
        moveComp.Move((Fix64)dt);
        executor.Execute(dt);
        chaser.SyncPositionFromExecutor();

        string diagnostics = BuildLv3BadPointDiagnostics(
            grid,
            derivedData,
            chaser,
            executor,
            startX,
            startY,
            goalX,
            goalY,
            startSectorId,
            goalSectorId);

        Assert.IsTrue(executor.LastConstraintSucceeded, $"真实链路的导航约束不应失败。\n{diagnostics}");
        Assert.Greater(executor.LastDesiredDisplacement.x, Mathf.Abs(executor.LastDesiredDisplacement.z) * 1.5f,
            $"目标几乎在正东，PendingPortal 阶段不应先给出朝建筑正面/南侧的主方向。\n{diagnostics}");
        Assert.Greater(executor.LastConstrainedDisplacement.x, 0.12f,
            $"真实 MoveExecutor 约束后仍应有明显向目标前进的位移，而不是被投影回原地。\n{diagnostics}");
    }

    public void Lv3右下角返程链路中敌兵追击返程后不应在建筑夹角长时间聚团停滞()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Medium.asset");
        Assert.NotNull(grid, "真实场景回归必须直接使用 Lv3_FlowNavigationGrid_Medium.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid_Medium.asset 必须带预烘焙 derived navigation data，测试才与实机链路一致。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid_Medium.asset 的 derived navigation data 必须有效。");

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 256;
        SetNavigationWorkQuotas(config, 1_000_000);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const float dt = 0.1f;
        const float heroSpeed = 2.4f;
        const float chaserSpeed = 3.5f;
        Fix64 chaserSpeedProperty = (Fix64)(chaserSpeed / DistanceUnitConverter.DefaultDistanceConversionRate);
        System.Text.StringBuilder setupDiagnostics = new System.Text.StringBuilder();
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        Lv3RightBottomRoute route = ResolveLv3RightBottomRoute(grid, derivedData, setupDiagnostics);
        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(route.ResearchCenterPosition);
        RegisterLv3ResearchCenterFootprintObstacles(researchCenter, 969000);
        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "真实右下角返程回归必须先提交研发中心 Autobox runtime dirty。");

        const float agentRadius = 0.45f;
        route.HeroStart = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.HeroStart, 5f, agentRadius, "return-route-hero-start", setupDiagnostics);
        route.LowerApproach = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.LowerApproach, 5f, agentRadius, "return-route-lower-approach", setupDiagnostics);
        route.RightBottomCorner = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.RightBottomCorner, 6f, agentRadius, "return-route-right-bottom", setupDiagnostics);
        route.ReturnPoint = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.ReturnPoint, 5f, agentRadius, "return-route-return", setupDiagnostics);
        for (int i = 0; i < route.ChaserStarts.Length; i++)
            route.ChaserStarts[i] = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.ChaserStarts[i], 4f, agentRadius, "return-route-chaser-" + i, setupDiagnostics);

        SimEntityContext hero = CreateEntity(route.HeroStart, false, grid.AgentTypeId, 0.45f);
        hero.Side = SideType.PlayerSide;

        SimEntityContext[] interns = new SimEntityContext[route.ChaserStarts.Length];
        for (int i = 0; i < interns.Length; i++)
            interns[i] = CreateEntity(route.ChaserStarts[i], false, grid.AgentTypeId, 0.45f);

        var allEntities = new System.Collections.Generic.List<IEntityContext>();
        allEntities.Add(hero);
        for (int i = 0; i < interns.Length; i++)
            allEntities.Add(interns[i]);

        var brains = new SoldierAIBrain[interns.Length];
        for (int i = 0; i < interns.Length; i++)
        {
            interns[i].Side = SideType.EnemySide;
            interns[i].SetProperty(CreatureMainProperty.Speed, chaserSpeedProperty);
            interns[i].TargetComp = new SimTargetingComp(interns[i], allEntities)
            {
                CurrentTarget = hero,
                AggroRangeFixed = (Fix64)32f,
                ForgetRangeFixed = (Fix64)48f
            };
            CharacterMoveComp moveComp = new CharacterMoveComp();
            moveComp.Init(interns[i], grid.AgentTypeId);
            interns[i].MoveComp = moveComp;
            interns[i].MoveExecutor = new SimMoveExecutor
            {
                Position = interns[i].Position,
                ApplyNavigationConstraint = true,
                AgentTypeId = grid.AgentTypeId,
                EdgeClearance = 0.5f
            };

            brains[i] = new SoldierAIBrain();
            brains[i].DetectEnemyRange = (Fix64)32f;
            brains[i].SetBirthPositionFixed(interns[i].PositionFixed);
            brains[i].Inject();
            interns[i].Brain = brains[i];
            EntityRegistry.Register(interns[i]);
        }
        EntityRegistry.Register(hero);

        Vector3[] heroWaypoints =
        {
            route.LowerApproach,
            route.RightBottomCorner,
            route.RightBottomCorner,
            route.ReturnPoint,
        };
        int heroWaypointIndex = 0;
        int cornerHoldFrames = 0;
        int[] consecutiveZeroFrames = new int[interns.Length];
        int[] maxConsecutiveZeroFrames = new int[interns.Length];
        Vector3[] lastVelocity = new Vector3[interns.Length];
        Vector3[] lastDesiredDisplacement = new Vector3[interns.Length];
        Vector3[] lastConstrainedDisplacement = new Vector3[interns.Length];
        Vector3[] lastPositions = new Vector3[interns.Length];
        for (int i = 0; i < interns.Length; i++)
            lastPositions[i] = interns[i].Position;

        int overlapFrames = 0;
        int cornerSlowFramesAfterReturn = 0;
        int maxCornerSlowStreak = 0;
        int currentCornerSlowStreak = 0;
        int framesWithChaserNearCorner = 0;
        int maxCornerOccupancyAfterReturn = 0;
        float minReturnDistanceToHero = float.PositiveInfinity;
        float minDistanceToCorner = float.PositiveInfinity;
        System.Text.StringBuilder timeline = new System.Text.StringBuilder();
        timeline.Append(setupDiagnostics);

        for (int frame = 1; frame <= 520; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);

            Vector3 heroWaypoint = heroWaypoints[heroWaypointIndex];
            hero.Position = AdvanceHeroWithNavigationConstraint(
                hero.Position,
                heroWaypoint,
                heroSpeed,
                dt,
                grid.AgentTypeId,
                0.5f,
                timeline);
            float heroWaypointDistanceSqr = (hero.Position - heroWaypoint).sqrMagnitude;
            bool reachedHeroWaypoint = heroWaypointDistanceSqr <= 0.0025f;
            if (heroWaypointIndex == 1 || heroWaypointIndex == 2)
                reachedHeroWaypoint = heroWaypointDistanceSqr <= 0.75f * 0.75f;

            if (reachedHeroWaypoint && heroWaypointIndex < heroWaypoints.Length - 1)
            {
                if (heroWaypointIndex == 1 && cornerHoldFrames < 35)
                    cornerHoldFrames++;
                else
                {
                    cornerHoldFrames = 0;
                    heroWaypointIndex++;
                }
            }
            hero.SyncPositionToExecutor();

            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

            AdvanceChasersThroughRuntimeMoveChain(
                interns,
                frame,
                dt,
                consecutiveZeroFrames,
                maxConsecutiveZeroFrames,
                lastVelocity,
                lastDesiredDisplacement,
                lastConstrainedDisplacement);

            float movedThisFrame = 0f;
            for (int i = 0; i < interns.Length; i++)
            {
                float actualMoved = Vector3.Distance(interns[i].Position, lastPositions[i]);
                movedThisFrame += actualMoved;
                lastPositions[i] = interns[i].Position;
                if (heroWaypointIndex >= 3)
                    minReturnDistanceToHero = Mathf.Min(minReturnDistanceToHero, Vector3.Distance(interns[i].Position, hero.Position));
                minDistanceToCorner = Mathf.Min(minDistanceToCorner, Vector3.Distance(interns[i].Position, route.RightBottomCorner));
            }

            for (int i = 0; i < interns.Length; i++)
                for (int j = i + 1; j < interns.Length; j++)
                    if (Vector3.Distance(interns[i].Position, interns[j].Position) < 0.55f)
                        overlapFrames++;

            bool afterHeroReturn = heroWaypointIndex >= 3;
            float heroCornerDistance = Vector3.Distance(hero.Position, route.RightBottomCorner);
            bool afterHeroLeftCorner = afterHeroReturn && heroCornerDistance > 4.5f;
            int cornerOccupancy = 0;
            for (int i = 0; i < interns.Length; i++)
            {
                if (IsInsideLv3RightBottomCornerWindow(interns[i].Position, route.RightBottomCorner))
                {
                    cornerOccupancy++;
                }
            }

            if (cornerOccupancy > 0)
                framesWithChaserNearCorner++;
            if (afterHeroLeftCorner)
                maxCornerOccupancyAfterReturn = Mathf.Max(maxCornerOccupancyAfterReturn, cornerOccupancy);

            bool cornerSlow = afterHeroLeftCorner && cornerOccupancy > 0 && movedThisFrame < 0.45f;
            if (cornerSlow)
            {
                cornerSlowFramesAfterReturn++;
                currentCornerSlowStreak++;
                maxCornerSlowStreak = Mathf.Max(maxCornerSlowStreak, currentCornerSlowStreak);
            }
            else
            {
                currentCornerSlowStreak = 0;
            }

            if (frame % 20 == 0 || cornerSlow)
            {
                int sampleStart = timeline.Length;
                timeline.Append("frame=").Append(frame)
                    .Append(" hero=").Append(hero.Position)
                    .Append(" waypoint=").Append(heroWaypointIndex)
                    .Append(" heroCornerDist=").Append(heroCornerDistance.ToString("F3"))
                    .Append(" moved=").Append(movedThisFrame.ToString("F3"))
                    .Append(" cornerOcc=").Append(cornerOccupancy)
                    .Append(" agents=");
                for (int i = 0; i < interns.Length; i++)
                {
                    grid.WorldToCell(interns[i].Position, out int x, out int y);
                    timeline.Append(i)
                        .Append(':').Append(interns[i].Position)
                        .Append("/cell=(").Append(x).Append(',').Append(y).Append(')')
                        .Append("/vel=").Append(lastVelocity[i])
                        .Append("/desiredDisp=").Append(lastDesiredDisplacement[i])
                        .Append("/constrainedDisp=").Append(lastConstrainedDisplacement[i])
                        .Append("/zeroStreak=").Append(consecutiveZeroFrames[i])
                        .Append('/')
                        .Append(FlowFieldCrowdMovementSystem.GetEditorTestMovingTargetAnchorDiagnostics(interns[i].LogicEntityId.Value));
                    AppendSteeringBreakdown(timeline, interns[i]);
                    timeline.Append(' ');
                }

                timeline.AppendLine();
                if (cornerSlow)
                    Debug.LogWarning("[Lv3CornerSlowSample] " + timeline.ToString(sampleStart, timeline.Length - sampleStart));
            }
        }

        Assert.Less(minDistanceToCorner, 3.2f, $"追兵必须实际追到 SH_1_3 右下角附近，否则这条真实回归没有覆盖手测场景。minDistanceToCorner={minDistanceToCorner:F3}, cornerFrames={framesWithChaserNearCorner}\n{timeline}");
        Assert.Less(maxCornerSlowStreak, 8, $"英雄离开右下角后，追兵不应在建筑夹角连续蠕动停滞。cornerSlowFrames={cornerSlowFramesAfterReturn}, maxStreak={maxCornerSlowStreak}, overlapFrames={overlapFrames}, maxCornerOccAfterReturn={maxCornerOccupancyAfterReturn}, minReturnDist={minReturnDistanceToHero:F3}\n{timeline}");
        Assert.Less(minReturnDistanceToHero, 6f, $"英雄返回后追兵应重新追上，而不是继续滞留角落。minReturnDistanceToHero={minReturnDistanceToHero:F3}\n{timeline}");
    }

    public void Lv3英雄绕到研发中心背面时追兵不应冲建筑聚团停滞()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Medium.asset");
        Assert.NotNull(grid, "真实场景回归必须直接使用 Lv3_FlowNavigationGrid_Medium.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid_Medium.asset 必须带预烘焙 derived navigation data，测试才与实机链路一致。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid_Medium.asset 的 derived navigation data 必须有效。");

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 256;
        SetNavigationWorkQuotas(config, 1_000_000);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        const float dt = 0.1f;
        const float heroSpeed = 2.45f;
        const float chaserSpeed = 3.5f;
        System.Text.StringBuilder diagnostics = new System.Text.StringBuilder();
        Lv3RightBottomRoute route = ResolveLv3RightBottomRoute(grid, derivedData, diagnostics);
        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(route.ResearchCenterPosition);
        RegisterLv3ResearchCenterFootprintObstacles(researchCenter, 970000);
        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "真实 Lv3 背面追击测试必须先提交研发中心 Autobox runtime dirty，避免用半更新导航世界。");

        const float agentRadius = 0.45f;
        const float heroEdgeClearance = 0.5f;
        route.HeroStart = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.HeroStart, 5f, heroEdgeClearance, "hero-start-runtime", diagnostics);
        route.LowerApproach = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.LowerApproach, 5f, heroEdgeClearance, "lower-approach-runtime", diagnostics);
        route.RightBottomCorner = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.RightBottomCorner, 6f, heroEdgeClearance, "right-bottom-corner-runtime", diagnostics);
        route.ReturnPoint = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.ReturnPoint, 5f, heroEdgeClearance, "return-point-runtime", diagnostics);
        for (int i = 0; i < route.ChaserStarts.Length; i++)
            route.ChaserStarts[i] = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.ChaserStarts[i], 4f, agentRadius, "chaser-runtime-" + i, diagnostics);

        Vector3 behindApproach = ResolveRuntimeLegalLv3MainIslandCell(
            grid,
            derivedData,
            new Vector3(route.ResearchCenterBounds.xMax + 1.0f, 0f, route.ResearchCenterBounds.yMin - 1.4f),
            6f,
            heroEdgeClearance,
            "behind-approach",
            diagnostics);

        SimEntityContext hero = CreateEntity(route.HeroStart, false, grid.AgentTypeId, agentRadius);
        hero.Side = SideType.PlayerSide;

        int chaserCount = Mathf.Min(14, route.ChaserStarts.Length);
        SimEntityContext[] chasers = new SimEntityContext[chaserCount];
        var allEntities = new System.Collections.Generic.List<IEntityContext> { hero };
        for (int i = 0; i < chaserCount; i++)
        {
            SimEntityContext chaser = CreateEntity(route.ChaserStarts[i], false, grid.AgentTypeId, agentRadius);
            chaser.Side = SideType.EnemySide;
            chaser.SetProperty(CreatureMainProperty.Speed, (Fix64)(chaserSpeed / DistanceUnitConverter.DefaultDistanceConversionRate));
            chasers[i] = chaser;
            allEntities.Add(chaser);
        }

        for (int i = 0; i < chasers.Length; i++)
        {
            SimEntityContext chaser = chasers[i];
            chaser.TargetComp = new SimTargetingComp(chaser, allEntities)
            {
                CurrentTarget = hero,
                AggroRangeFixed = (Fix64)34f,
                ForgetRangeFixed = (Fix64)50f
            };
            CharacterMoveComp moveComp = new CharacterMoveComp();
            moveComp.Init(chaser, grid.AgentTypeId);
            chaser.MoveComp = moveComp;
            chaser.MoveExecutor = new SimMoveExecutor
            {
                Position = chaser.Position,
                ApplyNavigationConstraint = true,
                AgentTypeId = grid.AgentTypeId,
                EdgeClearance = 0.5f
            };

            SoldierAIBrain brain = new SoldierAIBrain();
            brain.DetectEnemyRange = (Fix64)34f;
            brain.SetBirthPositionFixed(chaser.PositionFixed);
            brain.Inject();
            chaser.Brain = brain;
            EntityRegistry.Register(chaser);
        }
        EntityRegistry.Register(hero);

        Vector3[] heroWaypoints =
        {
            route.LowerApproach,
            behindApproach,
            route.RightBottomCorner,
            route.RightBottomCorner,
            route.ReturnPoint
        };
        int waypointIndex = 0;
        int cornerHoldFrames = 0;
        int[] zeroStreak = new int[chasers.Length];
        int[] maxZeroStreak = new int[chasers.Length];
        Vector3[] lastVelocity = new Vector3[chasers.Length];
        Vector3[] lastDesiredDisplacement = new Vector3[chasers.Length];
        Vector3[] lastConstrainedDisplacement = new Vector3[chasers.Length];
        Vector3[] lastPositions = new Vector3[chasers.Length];
        for (int i = 0; i < chasers.Length; i++)
            lastPositions[i] = chasers[i].Position;

        int wallBeforePortalSamples = 0;
        int wallStallSamples = 0;
        int nearWallGoalSamples = 0;
        const float chaserNavigationRadius = 0.45f;
        int maxWallSlowStreak = 0;
        int currentWallSlowStreak = 0;
        float minDistanceToBack = float.PositiveInfinity;
        float minWallHitDistance = float.PositiveInfinity;

        for (int frame = 1; frame <= 560; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);

            Vector3 waypoint = heroWaypoints[waypointIndex];
            hero.Position = AdvanceHeroWithNavigationConstraint(
                hero.Position,
                waypoint,
                heroSpeed,
                dt,
                grid.AgentTypeId,
                heroEdgeClearance,
                diagnostics);

            float waypointDistanceSqr = (hero.Position - waypoint).sqrMagnitude;
            bool reachedWaypoint = waypointDistanceSqr <= 0.05f * 0.05f;
            if (waypointIndex == 2 || waypointIndex == 3)
                reachedWaypoint = waypointDistanceSqr <= 0.75f * 0.75f;
            if (reachedWaypoint && waypointIndex < heroWaypoints.Length - 1)
            {
                if (waypointIndex == 2 && cornerHoldFrames < 35)
                    cornerHoldFrames++;
                else
                {
                    cornerHoldFrames = 0;
                    waypointIndex++;
                }
            }
            hero.SyncPositionToExecutor();

            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

            AdvanceChasersThroughRuntimeMoveChain(
                chasers,
                frame,
                dt,
                zeroStreak,
                maxZeroStreak,
                lastVelocity,
                lastDesiredDisplacement,
                lastConstrainedDisplacement);

            int wallOccupancy = 0;
            float movedNearWall = 0f;
            for (int i = 0; i < chasers.Length; i++)
            {
                SimEntityContext chaser = chasers[i];
                Vector3 decisionPosition = lastPositions[i];
                Vector3 afterPosition = chaser.Position;
                float moved = Vector3.Distance(afterPosition, decisionPosition);
                lastPositions[i] = afterPosition;

                minDistanceToBack = Mathf.Min(minDistanceToBack, Vector3.Distance(afterPosition, route.RightBottomCorner));
                Vector3 desired = lastDesiredDisplacement[i];
                desired.y = 0f;
                Vector3 desiredDirection = desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector3.zero;
                bool hitsResearchCenter = desiredDirection.sqrMagnitude > 0f
                                          && researchCenter.RayHitsAnyBox(decisionPosition, desiredDirection, 18f);
                float wallHitDistance = hitsResearchCenter
                    ? researchCenter.DistanceToFirstRayHit(decisionPosition, desiredDirection, 18f)
                    : float.PositiveInfinity;
                Assert.IsTrue(grid.WorldToCell(decisionPosition, out int currentX, out int currentY), "追兵决策位置必须在 Lv3 grid 内。");
                int currentSectorId = ResolveDerivedSectorId(derivedData, currentX, currentY);
                bool hasSelectedPortal = TryResolveSelectedPortalCenter(chaser, currentSectorId, out int selectedPortalId, out Vector3 selectedPortalCenter);
                float selectedPortalDistance = hasSelectedPortal
                    ? HorizontalDistance(decisionPosition, selectedPortalCenter)
                    : float.PositiveInfinity;
                float targetDistance = HorizontalDistance(decisionPosition, hero.Position);
                float routeLimitDistance = Mathf.Min(selectedPortalDistance, targetDistance);
                bool actualNavigationSegmentViolatesResearchCenter = SteeringTileTargetSegmentViolatesFootprintClearance(
                    researchCenter,
                    decisionPosition,
                    chaser,
                    grid.CellSize * 0.5f,
                    0.45f);
                bool segmentEntersResearchCenter = desiredDirection.sqrMagnitude > 0f
                                                    && SegmentViolatesFootprintClearance(
                                                        researchCenter,
                                                       decisionPosition,
                                                       desiredDirection,
                                                       Mathf.Min(routeLimitDistance + 0.2f, 18f),
                                                       grid.CellSize * 0.5f,
                                                       chaserNavigationRadius);
                bool hitsBeforePortal = hitsResearchCenter
                                        && wallHitDistance <= routeLimitDistance + 0.2f
                                        && segmentEntersResearchCenter
                                        && actualNavigationSegmentViolatesResearchCenter;
                if (hitsBeforePortal)
                {
                    wallBeforePortalSamples++;
                    minWallHitDistance = Mathf.Min(minWallHitDistance, wallHitDistance);
                }

                float researchDistance = researchCenter.DistanceToClosestBox(afterPosition);
                bool nearBackWall = waypointIndex >= 2
                                    && researchDistance <= 1.1f
                                    && afterPosition.x >= route.ResearchCenterBounds.center.x
                                    && afterPosition.z <= route.ResearchCenterBounds.center.y;
                if (nearBackWall)
                {
                    wallOccupancy++;
                    movedNearWall += moved;
                    if (moved < 0.035f && lastDesiredDisplacement[i].sqrMagnitude > 0.08f * 0.08f)
                        wallStallSamples++;
                }

                bool hasSteeringGoal = FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringGoal(chaser.LogicEntityId.Value, out Vector3 steeringGoal, out int steeringFrame);
                bool hasCurrentSteeringGoal = hasSteeringGoal && steeringFrame == frame;
                float steeringGoalResearchDistance = hasSteeringGoal ? researchCenter.DistanceToClosestBox(steeringGoal) : float.PositiveInfinity;
                bool flowSteeringGoalClear = false;
                float flowSteeringGoalClearanceViolation = float.PositiveInfinity;
                int flowRuntimeBoxCount = 0;
                int flowRuntimeCircleCount = 0;
                bool hasFlowClearance = hasSteeringGoal
                                        && FlowFieldCrowdMovementSystem.TryGetEditorTestNavigationClearance(
                                            steeringGoal,
                                            chaserNavigationRadius,
                                            out flowSteeringGoalClear,
                                            out flowSteeringGoalClearanceViolation,
                                            out flowRuntimeBoxCount,
                                            out flowRuntimeCircleCount);
                bool nearWallGoal = hasCurrentSteeringGoal
                                    && steeringGoalResearchDistance < chaserNavigationRadius - 0.02f
                                    && HorizontalDistance(steeringGoal, hero.Position) > chaserNavigationRadius;
                if (nearWallGoal)
                    nearWallGoalSamples++;

                if (hitsBeforePortal || (nearBackWall && moved < 0.035f) || nearWallGoal || frame % 40 == 0)
                {
                    int sampleStart = diagnostics.Length;
                    diagnostics.Append("[Lv3BacksideResearchChase] frame=").Append(frame)
                        .Append(" agent=").Append(i)
                        .Append(" waypoint=").Append(waypointIndex)
                        .Append(" hero=").Append(hero.Position)
                        .Append(" decision=").Append(decisionPosition)
                        .Append(" after=").Append(afterPosition);
                    AppendCellDiagnostic(diagnostics, grid, derivedData, decisionPosition, "decision");
                    diagnostics.Append(" desiredDisp=").Append(lastDesiredDisplacement[i])
                        .Append(" constrainedDisp=").Append(lastConstrainedDisplacement[i])
                        .Append(" moved=").Append(moved.ToString("F3"))
                        .Append(" rayHitsResearchCenter=").Append(hitsResearchCenter)
                        .Append(" wallHitDist=").Append(float.IsPositiveInfinity(wallHitDistance) ? "INF" : wallHitDistance.ToString("F3"))
                        .Append(" selectedPortal=").Append(hasSelectedPortal ? selectedPortalId.ToString() : "missing")
                        .Append(" portalDist=").Append(float.IsPositiveInfinity(selectedPortalDistance) ? "INF" : selectedPortalDistance.ToString("F3"))
                        .Append(" targetDist=").Append(targetDistance.ToString("F3"))
                        .Append(" segmentEntersResearchCenter=").Append(segmentEntersResearchCenter)
                        .Append(" hitsBeforePortal=").Append(hitsBeforePortal)
                        .Append(" researchDist=").Append(researchDistance.ToString("F3"))
                        .Append(" nearBackWall=").Append(nearBackWall)
                        .Append(" steeringGoal=").Append(hasSteeringGoal ? steeringGoal.ToString() : "missing")
                        .Append(" steeringFrame=").Append(hasSteeringGoal ? steeringFrame.ToString() : "missing")
                        .Append(" steeringGoalResearchDist=").Append(float.IsPositiveInfinity(steeringGoalResearchDistance) ? "INF" : steeringGoalResearchDistance.ToString("F3"))
                        .Append(" nearWallGoal=").Append(nearWallGoal)
                        .Append(" flowClearance=").Append(hasFlowClearance ? flowSteeringGoalClear.ToString() : "missing")
                        .Append(" flowClearanceViolation=").Append(hasFlowClearance ? flowSteeringGoalClearanceViolation.ToString("F3") : "missing")
                        .Append(" flowRuntimeBoxes=").Append(hasFlowClearance ? flowRuntimeBoxCount.ToString() : "missing")
                        .Append(" flowRuntimeCircles=").Append(hasFlowClearance ? flowRuntimeCircleCount.ToString() : "missing");
                    AppendSteeringBreakdown(diagnostics, chaser);
                    AppendPathHandleDiagnostics(diagnostics, chaser, grid, derivedData, hero.Position);
                    if (hitsBeforePortal)
                        AppendSegmentGridDiagnostics(diagnostics, grid, derivedData, researchCenter, decisionPosition, "decision-to-tileTarget", chaser);
                    diagnostics.AppendLine();
                    if (hitsBeforePortal || (nearBackWall && moved < 0.035f) || nearWallGoal)
                        Debug.LogWarning("[Lv3BacksideResearchChaseSample] " + diagnostics.ToString(sampleStart, diagnostics.Length - sampleStart));
                }
            }

            bool wallSlow = waypointIndex >= 2 && wallOccupancy >= 3 && movedNearWall < 0.2f;
            if (wallSlow)
            {
                currentWallSlowStreak++;
                maxWallSlowStreak = Mathf.Max(maxWallSlowStreak, currentWallSlowStreak);
            }
            else
            {
                currentWallSlowStreak = 0;
            }
        }

        Assert.Less(minDistanceToBack, 3.2f, $"追兵必须实际追到研发中心背面附近，否则测试没有覆盖手测场景。minDistanceToBack={minDistanceToBack:F3}\n{diagnostics}");
        Assert.LessOrEqual(wallBeforePortalSamples, 0,
            $"英雄绕到研发中心背面时，追兵不应在到达当前 portal 前朝研发中心碰撞体推进。wallBeforePortal={wallBeforePortalSamples}, minHit={minWallHitDistance:F3}\n{diagnostics}");
        Assert.Less(maxWallSlowStreak, 6,
            $"英雄绕到研发中心背面时，追兵不应在背面墙侧连续聚团蠕动。maxWallSlowStreak={maxWallSlowStreak}, wallStallSamples={wallStallSamples}\n{diagnostics}");
        Assert.Less(wallStallSamples, 8,
            $"英雄绕到研发中心背面时，单兵贴墙停滞样本过多。wallStallSamples={wallStallSamples}, maxWallSlowStreak={maxWallSlowStreak}\n{diagnostics}");
        Assert.LessOrEqual(nearWallGoalSamples, 0,
            $"英雄绕到研发中心背面时，接战目标点不能贴进研发中心碰撞体半径内。nearWallGoalSamples={nearWallGoalSamples}\n{diagnostics}");
    }

    [UnityTest]
    public IEnumerator Lv3研发中心右下边缘真实追击不应多人挤住()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Medium.asset");
        Assert.NotNull(grid, "真实场景回归必须直接使用 Lv3_FlowNavigationGrid_Medium.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid_Medium.asset 必须带预烘焙 derived navigation data，测试才与实机链路一致。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid_Medium.asset 的 derived navigation data 必须有效。");

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 256;
        SetNavigationWorkQuotas(config, 1_000_000);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        System.Text.StringBuilder diagnostics = new System.Text.StringBuilder();
        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(preset.ResearchCenterPosition);
        RegisterLv3ResearchCenterFootprintObstacles(researchCenter, 982000);
        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "真实右下边缘回归必须先提交研发中心 Autobox runtime dirty。");

        const float dt = 0.1f;
        const float heroSpeed = 2.45f;
        const float chaserSpeed = 4.087f;
        const float agentRadius = 0.45f;
        SimEntityContext hero = CreateEntity(
            ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(85.21f, 0f, 18.33f), 2.5f, agentRadius, "right-edge-hero-start", diagnostics),
            false,
            grid.AgentTypeId,
            agentRadius);
        hero.Side = SideType.PlayerSide;
        hero.SetProperty(CreatureMainProperty.CollisionRadius, (Fix64)(agentRadius / DistanceUnitConverter.DefaultDistanceConversionRate));

        Vector3[] preferredStarts =
        {
            new Vector3(84.95f, 0f, 11.77f),
            new Vector3(84.63f, 0f, 11.77f),
            new Vector3(84.24f, 0f, 11.77f),
            new Vector3(83.88f, 0f, 11.75f),
            new Vector3(83.38f, 0f, 11.74f),
            new Vector3(85.12f, 0f, 12.08f),
            new Vector3(84.72f, 0f, 12.18f),
            new Vector3(84.30f, 0f, 12.16f),
            new Vector3(83.92f, 0f, 12.08f),
            new Vector3(83.50f, 0f, 12.04f),
            new Vector3(85.25f, 0f, 12.48f),
            new Vector3(84.85f, 0f, 12.55f)
        };

        SimEntityContext[] chasers = new SimEntityContext[preferredStarts.Length];
        var allEntities = new List<IEntityContext> { hero };
        for (int i = 0; i < preferredStarts.Length; i++)
        {
            Vector3 start = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, preferredStarts[i], 4.0f, agentRadius, "right-edge-chaser-" + i, diagnostics);
            SimEntityContext chaser = CreateEntity(start, false, grid.AgentTypeId, agentRadius);
            chaser.Side = SideType.EnemySide;
            chaser.SetProperty(CreatureMainProperty.Speed, (Fix64)(chaserSpeed / DistanceUnitConverter.DefaultDistanceConversionRate));
            chaser.SetProperty(CreatureMainProperty.CollisionRadius, (Fix64)(agentRadius / DistanceUnitConverter.DefaultDistanceConversionRate));
            chasers[i] = chaser;
            allEntities.Add(chaser);
        }

        for (int i = 0; i < chasers.Length; i++)
        {
            SimEntityContext chaser = chasers[i];
            chaser.TargetComp = new SimTargetingComp(chaser, allEntities)
            {
                CurrentTarget = hero,
                AggroRangeFixed = (Fix64)34f,
                ForgetRangeFixed = (Fix64)50f
            };
            CharacterMoveComp moveComp = new CharacterMoveComp();
            moveComp.Init(chaser, grid.AgentTypeId);
            chaser.MoveComp = moveComp;
            chaser.MoveExecutor = new SimMoveExecutor
            {
                Position = chaser.Position,
                ApplyNavigationConstraint = true,
                AgentTypeId = grid.AgentTypeId,
                EdgeClearance = 0.5f
            };

            SoldierAIBrain brain = new SoldierAIBrain();
            brain.DetectEnemyRange = (Fix64)34f;
            brain.ChaseRange = (Fix64)120f;
            brain.SetBirthPositionFixed(chaser.PositionFixed);
            brain.Inject();
            chaser.Brain = brain;
            EntityRegistry.Register(chaser);
        }
        EntityRegistry.Register(hero);

        Vector3[] heroWaypoints =
        {
            ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(85.21f, 0f, 18.33f), 2.5f, agentRadius, "right-edge-hero-hold", diagnostics),
            ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(85.49f, 0f, 18.05f), 2.5f, agentRadius, "right-edge-hero-shift", diagnostics),
            ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(88.40f, 0f, 18.00f), 4.5f, agentRadius, "right-edge-hero-away", diagnostics),
            ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(86.10f, 0f, 15.30f), 3.5f, agentRadius, "right-edge-hero-return", diagnostics)
        };

        int waypointIndex = 0;
        int waypointHoldFrames = 0;
        int[] zeroStreak = new int[chasers.Length];
        int[] maxZeroStreak = new int[chasers.Length];
        Vector3[] lastVelocity = new Vector3[chasers.Length];
        Vector3[] lastDesiredDisplacement = new Vector3[chasers.Length];
        Vector3[] lastConstrainedDisplacement = new Vector3[chasers.Length];
        Vector3[] lastPositions = new Vector3[chasers.Length];
        for (int i = 0; i < chasers.Length; i++)
            lastPositions[i] = chasers[i].Position;

        int rightEdgeStallSamples = 0;
        int maxRightEdgeSlowStreak = 0;
        int currentRightEdgeSlowStreak = 0;
        int wallBeforePortalSamples = 0;
        int nearWallGoalSamples = 0;
        int crowdCancellationSamples = 0;
        int afterHeroLeftWallStallSamples = 0;
        int maxAfterHeroLeftWallStreak = 0;
        int currentAfterHeroLeftWallStreak = 0;
        int maxRequiredTileQueueIndex = -1;
        int requiredTileDeepQueueSamples = 0;
        int longPendingPortalSamples = 0;
        int requiredTilePendingSamples = 0;
        float minDistanceToHero = float.PositiveInfinity;
        float minWallHitDistance = float.PositiveInfinity;

        for (int frame = 1; frame <= 180; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);

            Vector3 heroGoal = heroWaypoints[waypointIndex];
            if (waypointIndex < 2 && waypointHoldFrames < 28)
            {
                waypointHoldFrames++;
            }
            else
            {
                hero.Position = AdvanceHeroWithNavigationConstraint(
                    hero.Position,
                    heroGoal,
                    heroSpeed,
                    dt,
                    grid.AgentTypeId,
                    0.5f,
                    diagnostics);
                if (HorizontalDistance(hero.Position, heroGoal) <= 0.2f && waypointIndex < heroWaypoints.Length - 1)
                {
                    waypointIndex++;
                    waypointHoldFrames = 0;
                }
            }
            hero.SyncPositionToExecutor();

            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

            AdvanceChasersThroughRuntimeMoveChain(
                chasers,
                frame,
                dt,
                zeroStreak,
                maxZeroStreak,
                lastVelocity,
                lastDesiredDisplacement,
                lastConstrainedDisplacement);

            int rightEdgeOccupancy = 0;
            float rightEdgeMovedSum = 0f;
            bool anyAfterHeroLeftWallStallThisFrame = false;
            for (int i = 0; i < chasers.Length; i++)
            {
                SimEntityContext chaser = chasers[i];
                Vector3 decisionPosition = lastPositions[i];
                Vector3 afterPosition = chaser.Position;
                float moved = HorizontalDistance(decisionPosition, afterPosition);
                lastPositions[i] = afterPosition;
                minDistanceToHero = Mathf.Min(minDistanceToHero, HorizontalDistance(afterPosition, hero.Position));

                Vector3 desired = lastDesiredDisplacement[i];
                desired.y = 0f;
                Vector3 desiredDirection = desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector3.zero;
                bool hitsResearchCenter = desiredDirection.sqrMagnitude > 0f
                                          && researchCenter.RayHitsAnyBox(decisionPosition, desiredDirection, 18f);
                float wallHitDistance = hitsResearchCenter
                    ? researchCenter.DistanceToFirstRayHit(decisionPosition, desiredDirection, 18f)
                    : float.PositiveInfinity;
                Assert.IsTrue(grid.WorldToCell(decisionPosition, out int currentX, out int currentY), "追兵决策位置必须在 Lv3 grid 内。");
                int currentSectorId = ResolveDerivedSectorId(derivedData, currentX, currentY);
                bool hasSelectedPortal = TryResolveSelectedPortalCenter(chaser, currentSectorId, out int selectedPortalId, out Vector3 selectedPortalCenter);
                float selectedPortalDistance = hasSelectedPortal
                    ? HorizontalDistance(decisionPosition, selectedPortalCenter)
                    : float.PositiveInfinity;
                float targetDistance = HorizontalDistance(decisionPosition, hero.Position);
                float routeLimitDistance = Mathf.Min(selectedPortalDistance, targetDistance);
                bool actualNavigationSegmentViolatesResearchCenter = SteeringTileTargetSegmentViolatesFootprintClearance(
                    researchCenter,
                    decisionPosition,
                    chaser,
                    grid.CellSize * 0.5f,
                    agentRadius);
                bool segmentEntersResearchCenter = desiredDirection.sqrMagnitude > 0f
                                                   && SegmentViolatesFootprintClearance(
                                                       researchCenter,
                                                       decisionPosition,
                                                       desiredDirection,
                                                       Mathf.Min(routeLimitDistance + 0.2f, 18f),
                                                       grid.CellSize * 0.5f,
                                                       agentRadius);
                bool hitsBeforePortal = hitsResearchCenter
                                        && wallHitDistance <= routeLimitDistance + 0.2f
                                        && segmentEntersResearchCenter
                                        && actualNavigationSegmentViolatesResearchCenter
                                        && targetDistance > 1.25f;
                if (hitsBeforePortal)
                {
                    wallBeforePortalSamples++;
                    minWallHitDistance = Mathf.Min(minWallHitDistance, wallHitDistance);
                }

                float researchDistance = researchCenter.DistanceToClosestBox(afterPosition);
                bool inLoggedRightEdgeCluster = afterPosition.x >= 83.2f
                                                && afterPosition.x <= 85.45f
                                                && afterPosition.z >= 11.45f
                                                && afterPosition.z <= 12.85f;
                if (inLoggedRightEdgeCluster)
                {
                    rightEdgeOccupancy++;
                    rightEdgeMovedSum += moved;
                    if (moved < 0.04f && lastDesiredDisplacement[i].sqrMagnitude > 0.08f * 0.08f)
                        rightEdgeStallSamples++;
                }

                bool heroHasLeftInitialCorner = waypointIndex >= 2 || HorizontalDistance(hero.Position, heroWaypoints[0]) > 1.25f;
                bool afterHeroLeftWallStall = heroHasLeftInitialCorner
                                              && targetDistance > 1.25f
                                              && researchDistance <= 1.05f
                                              && moved < 0.04f
                                              && lastDesiredDisplacement[i].sqrMagnitude > 0.08f * 0.08f;
                if (afterHeroLeftWallStall)
                {
                    afterHeroLeftWallStallSamples++;
                    anyAfterHeroLeftWallStallThisFrame = true;
                }

                bool hasSteeringGoal = FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringGoal(chaser.LogicEntityId.Value, out Vector3 steeringGoal, out int steeringFrame);
                bool hasCurrentSteeringGoal = hasSteeringGoal && steeringFrame == frame;
                float steeringGoalResearchDistance = hasSteeringGoal ? researchCenter.DistanceToClosestBox(steeringGoal) : float.PositiveInfinity;
                bool nearWallGoal = hasCurrentSteeringGoal
                                    && steeringGoalResearchDistance < agentRadius - 0.02f
                                    && HorizontalDistance(steeringGoal, hero.Position) > agentRadius;
                if (nearWallGoal)
                    nearWallGoalSamples++;

                bool hasTileQueueState = FlowFieldCrowdMovementSystem.TryGetEditorTestCurrentTileQueueState(
                    chaser.LogicEntityId.Value,
                    out string requiredGoalKind,
                    out int requiredPortalId,
                    out bool requiredTileCached,
                    out bool requiredTilePending,
                    out int requiredTileQueueIndex,
                    out string requiredTileStage,
                    out bool requiredTileWaiting,
                    out bool requiredTileStale,
                    out int pendingPortalFrames);
                if (hasTileQueueState)
                {
                    if (requiredTilePending && !requiredTileCached)
                        requiredTilePendingSamples++;
                    if (requiredTileQueueIndex > maxRequiredTileQueueIndex)
                        maxRequiredTileQueueIndex = requiredTileQueueIndex;
                    if (requiredTileQueueIndex >= 128)
                        requiredTileDeepQueueSamples++;
                    if (pendingPortalFrames >= 12)
                        longPendingPortalSamples++;
                }

                bool shouldLog = hitsBeforePortal
                                 || nearWallGoal
                                 || afterHeroLeftWallStall
                                 || (hasTileQueueState && (requiredTileQueueIndex >= 128 || pendingPortalFrames >= 12))
                                 || (inLoggedRightEdgeCluster && (moved < 0.04f || frame % 20 == 0))
                                 || frame <= 5;
                if (shouldLog)
                {
                    int sampleStart = diagnostics.Length;
                    diagnostics.Append("[Lv3RightEdgeRealChase] frame=").Append(frame)
                        .Append(" agent=").Append(i)
                        .Append(" waypoint=").Append(waypointIndex)
                        .Append(" hero=").Append(hero.Position)
                        .Append(" decision=").Append(decisionPosition)
                        .Append(" after=").Append(afterPosition);
                    AppendCellDiagnostic(diagnostics, grid, derivedData, decisionPosition, "decision");
                    diagnostics.Append(" desiredDisp=").Append(lastDesiredDisplacement[i])
                        .Append(" constrainedDisp=").Append(lastConstrainedDisplacement[i])
                        .Append(" moved=").Append(moved.ToString("F3"))
                        .Append(" rayHitsResearchCenter=").Append(hitsResearchCenter)
                        .Append(" wallHitDist=").Append(float.IsPositiveInfinity(wallHitDistance) ? "INF" : wallHitDistance.ToString("F3"))
                        .Append(" selectedPortal=").Append(hasSelectedPortal ? selectedPortalId.ToString() : "missing")
                        .Append(" portalDist=").Append(float.IsPositiveInfinity(selectedPortalDistance) ? "INF" : selectedPortalDistance.ToString("F3"))
                        .Append(" targetDist=").Append(targetDistance.ToString("F3"))
                        .Append(" segmentEntersResearchCenter=").Append(segmentEntersResearchCenter)
                        .Append(" actualNavSegmentViolatesResearchCenter=").Append(actualNavigationSegmentViolatesResearchCenter)
                        .Append(" hitsBeforePortal=").Append(hitsBeforePortal)
                        .Append(" researchDist=").Append(researchDistance.ToString("F3"))
                        .Append(" inLoggedRightEdgeCluster=").Append(inLoggedRightEdgeCluster)
                        .Append(" heroHasLeftInitialCorner=").Append(heroHasLeftInitialCorner)
                        .Append(" afterHeroLeftWallStall=").Append(afterHeroLeftWallStall)
                        .Append(" steeringGoal=").Append(hasSteeringGoal ? steeringGoal.ToString() : "missing")
                        .Append(" steeringFrame=").Append(hasSteeringGoal ? steeringFrame.ToString() : "missing")
                        .Append(" steeringGoalResearchDist=").Append(float.IsPositiveInfinity(steeringGoalResearchDistance) ? "INF" : steeringGoalResearchDistance.ToString("F3"))
                        .Append(" nearWallGoal=").Append(nearWallGoal)
                        .Append(" tileState=").Append(hasTileQueueState ? "present" : "missing")
                        .Append(" tileGoalKind=").Append(hasTileQueueState ? requiredGoalKind : "missing")
                        .Append(" tilePortal=").Append(hasTileQueueState ? requiredPortalId.ToString() : "missing")
                        .Append(" tileCached=").Append(hasTileQueueState ? requiredTileCached.ToString() : "missing")
                        .Append(" tilePending=").Append(hasTileQueueState ? requiredTilePending.ToString() : "missing")
                        .Append(" tileQueueIndex=").Append(hasTileQueueState ? requiredTileQueueIndex.ToString() : "missing")
                        .Append(" tileStage=").Append(hasTileQueueState ? requiredTileStage : "missing")
                        .Append(" tileWaiting=").Append(hasTileQueueState ? requiredTileWaiting.ToString() : "missing")
                        .Append(" tileStale=").Append(hasTileQueueState ? requiredTileStale.ToString() : "missing")
                        .Append(" pendingPortalFrames=").Append(hasTileQueueState ? pendingPortalFrames.ToString() : "missing")
                        .Append(" pendingQueue=").Append(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount());
                    AppendSteeringBreakdown(diagnostics, chaser);
                    AppendPathHandleDiagnostics(diagnostics, chaser, grid, derivedData, hero.Position);
                    if (hitsBeforePortal)
                        AppendSegmentGridDiagnostics(diagnostics, grid, derivedData, researchCenter, decisionPosition, "decision-to-tileTarget", chaser);
                    diagnostics.AppendLine();
                    if (hitsBeforePortal || nearWallGoal || afterHeroLeftWallStall || (hasTileQueueState && (requiredTileQueueIndex >= 128 || pendingPortalFrames >= 12)) || (inLoggedRightEdgeCluster && moved < 0.04f))
                        Debug.LogWarning("[Lv3RightEdgeRealChaseSample] " + diagnostics.ToString(sampleStart, diagnostics.Length - sampleStart));
                }
            }

            bool rightEdgeSlow = rightEdgeOccupancy >= 4 && rightEdgeMovedSum < 0.22f;
            if (rightEdgeSlow)
            {
                currentRightEdgeSlowStreak++;
                maxRightEdgeSlowStreak = Mathf.Max(maxRightEdgeSlowStreak, currentRightEdgeSlowStreak);
            }
            else
            {
                currentRightEdgeSlowStreak = 0;
            }

            bool afterLeaveWallSlow = waypointIndex >= 2 && anyAfterHeroLeftWallStallThisFrame;
            if (afterLeaveWallSlow)
            {
                currentAfterHeroLeftWallStreak++;
                maxAfterHeroLeftWallStreak = Mathf.Max(maxAfterHeroLeftWallStreak, currentAfterHeroLeftWallStreak);
            }
            else
            {
                currentAfterHeroLeftWallStreak = 0;
            }

            yield return null;
        }

        Assert.Less(minDistanceToHero, 6f, $"追兵必须实际追到英雄附近，否则这条真实回归没有覆盖手测接敌场景。minDistanceToHero={minDistanceToHero:F3}\n{diagnostics}");
        Assert.LessOrEqual(wallBeforePortalSamples, 0,
            $"右下边缘真实追击不应在到达当前 portal 前朝研发中心碰撞体推进。wallBeforePortal={wallBeforePortalSamples}, minHit={minWallHitDistance:F3}\n{diagnostics}");
        Assert.LessOrEqual(nearWallGoalSamples, 0,
            $"右下边缘真实追击的接战目标点不能贴进研发中心碰撞体半径内。nearWallGoalSamples={nearWallGoalSamples}\n{diagnostics}");
        Assert.Less(maxRightEdgeSlowStreak, 5,
            $"右下边缘真实追击不应多人连续挤住蠕动。maxRightEdgeSlowStreak={maxRightEdgeSlowStreak}, rightEdgeStallSamples={rightEdgeStallSamples}, crowdCancellationSamples={crowdCancellationSamples}\n{diagnostics}");
        Assert.Less(rightEdgeStallSamples, 8,
            $"右下边缘真实追击出现过多单兵停滞样本。rightEdgeStallSamples={rightEdgeStallSamples}, crowdCancellationSamples={crowdCancellationSamples}\n{diagnostics}");
        Assert.Less(afterHeroLeftWallStallSamples, 5,
            $"英雄离开右下边缘后，追兵不应继续贴研发中心墙侧停滞。afterHeroLeftWallStallSamples={afterHeroLeftWallStallSamples}, maxAfterHeroLeftWallStreak={maxAfterHeroLeftWallStreak}\n{diagnostics}");
        Assert.LessOrEqual(requiredTileDeepQueueSamples, 0,
            $"真实右下追击时，当前单位所需 tile 不能被旧移动目标 tile job 淹没到队列深处。deepSamples={requiredTileDeepQueueSamples}, maxQueueIndex={maxRequiredTileQueueIndex}, pendingSamples={requiredTilePendingSamples}\n{diagnostics}");
        Assert.LessOrEqual(longPendingPortalSamples, 0,
            $"真实右下追击时，单位不能长期停留在 PendingPortal/Infinity 方向上。longPendingPortalSamples={longPendingPortalSamples}, maxQueueIndex={maxRequiredTileQueueIndex}\n{diagnostics}");
    }

    public void Lv3研发中心左侧追击英雄绕到右下时不应冲建筑聚团()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Medium.asset");
        Assert.NotNull(grid, "真实场景回归必须直接使用 Lv3_FlowNavigationGrid_Medium.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid_Medium.asset 必须带预烘焙 derived navigation data，测试才与实机链路一致。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid_Medium.asset 的 derived navigation data 必须有效。");

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 256;
        SetNavigationWorkQuotas(config, 1_000_000);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        System.Text.StringBuilder diagnostics = new System.Text.StringBuilder();
        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(preset.ResearchCenterPosition);
        RegisterLv3ResearchCenterFootprintObstacles(researchCenter, 984000);
        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "真实左侧追击右下回归必须先提交研发中心 Autobox runtime dirty。");
        Rect bounds = preset.ResearchCenterFootprintBounds;
        Vector3 heroStart = ResolveNearestMainIslandCell(
            grid,
            derivedData,
            new Vector3(bounds.xMin - 5.0f, 0f, bounds.yMin - 1.35f),
            6f,
            "moving-hero-start",
            diagnostics);
        Vector3 heroGoal = ResolveNearestMainIslandCell(
            grid,
            derivedData,
            new Vector3(bounds.xMax + 5.0f, 0f, bounds.yMin - 1.35f),
            6f,
            "moving-hero-goal",
            diagnostics);
        Vector3 chaserCenter = ResolveNearestMainIslandCell(
            grid,
            derivedData,
            new Vector3(bounds.xMin - 7.0f, 0f, bounds.center.y),
            8f,
            "moving-chaser-center",
            diagnostics);

        SimEntityContext hero = CreateEntity(heroStart, false, grid.AgentTypeId, 0.45f);
        hero.Side = SideType.PlayerSide;

        const int chaserCount = 16;
        const float dt = 0.1f;
        const float heroSpeed = 2.4f;
        const float targetCellJitter = 0.035f;
        Fix64 chaserSpeedProperty = (Fix64)(3.5f / DistanceUnitConverter.DefaultDistanceConversionRate);
        SimEntityContext[] chasers = new SimEntityContext[chaserCount];
        CharacterMoveComp[] moveComps = new CharacterMoveComp[chaserCount];
        SimMoveExecutor[] executors = new SimMoveExecutor[chaserCount];
        int[] zeroStreak = new int[chaserCount];
        int[] maxZeroStreak = new int[chaserCount];
        for (int i = 0; i < chaserCount; i++)
        {
            float angle = i * 2.39996323f;
            float radius = 0.55f + (i % 4) * 0.55f;
            Vector3 preferred = chaserCenter + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Vector3 start = ResolveNearestMainIslandCell(grid, derivedData, preferred, 4f, "moving-chaser-" + i, diagnostics);
            SimEntityContext chaser = CreateEntity(start, false, grid.AgentTypeId, 0.45f);
            chaser.Side = SideType.EnemySide;
            chaser.SetProperty(CreatureMainProperty.Speed, chaserSpeedProperty);
            chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { hero })
            {
                CurrentTarget = hero
            };
            CharacterMoveComp moveComp = new CharacterMoveComp();
            moveComp.Init(chaser, grid.AgentTypeId);
            chaser.MoveComp = moveComp;
            SimMoveExecutor executor = new SimMoveExecutor
            {
                Position = chaser.Position,
                ApplyNavigationConstraint = true,
                AgentTypeId = grid.AgentTypeId,
                EdgeClearance = 0.5f
            };
            chaser.MoveExecutor = executor;
            chasers[i] = chaser;
            moveComps[i] = moveComp;
            executors[i] = executor;
        }

        int wallBeforePortalSamples = 0;
        int constrainedWallStallSamples = 0;
        int staleSteeringSamples = 0;
        int targetCellSwitches = 0;
        int maxPendingFlowTiles = 0;
        int maxPendingSharedGoals = 0;
        int lastHeroCellX = int.MinValue;
        int lastHeroCellY = int.MinValue;
        int issueDetailedSamples = 0;
        int backgroundDetailedSamples = 0;
        float minWallHitDistance = float.PositiveInfinity;
        for (int frame = 1; frame <= 220; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
            hero.Position = AdvanceHeroWithNavigationConstraint(
                hero.Position,
                heroGoal,
                heroSpeed,
                dt,
                grid.AgentTypeId,
                0.5f,
                diagnostics);
            if (grid.WorldToCell(hero.Position, out int heroCellX, out int heroCellY)
                && heroCellX > lastHeroCellX)
            {
                float lowerCellCenterZ = grid.Origin.z + (heroCellY + 0.5f) * grid.CellSize;
                float jitterSign = (frame & 1) == 0 ? -1f : 1f;
                Vector3 jitteredHeroPosition = hero.Position;
                jitteredHeroPosition.z = lowerCellCenterZ + jitterSign * (grid.CellSize * 0.5f + targetCellJitter);
                hero.Position = jitteredHeroPosition;
                if (grid.WorldToCell(hero.Position, out heroCellX, out heroCellY))
                {
                    if (lastHeroCellX != int.MinValue
                        && (heroCellX != lastHeroCellX || heroCellY != lastHeroCellY))
                    {
                        targetCellSwitches++;
                    }

                    lastHeroCellX = heroCellX;
                    lastHeroCellY = heroCellY;
                }
            }
            else if (heroCellX != lastHeroCellX || heroCellY != lastHeroCellY)
            {
                if (lastHeroCellX != int.MinValue)
                    targetCellSwitches++;
                lastHeroCellX = heroCellX;
                lastHeroCellY = heroCellY;
            }
            hero.SyncPositionToExecutor();

            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            maxPendingFlowTiles = Mathf.Max(maxPendingFlowTiles, FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount());
            maxPendingSharedGoals = Mathf.Max(maxPendingSharedGoals, FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount());

            for (int i = 0; i < chasers.Length; i++)
            {
                SimEntityContext chaser = chasers[i];
                SimMoveExecutor executor = executors[i];
                chaser.SyncPositionToExecutor();
                Vector3 decisionPosition = chaser.Position;
                moveComps[i].MoveToFixed(hero.PositionFixed);
                moveComps[i].Move((Fix64)dt);
                executor.Execute(dt);
                chaser.SyncPositionFromExecutor();

                Vector3 desired = executor.LastDesiredDisplacement;
                desired.y = 0f;
                Vector3 desiredDirection = desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector3.zero;
                Assert.IsTrue(grid.WorldToCell(decisionPosition, out int currentX, out int currentY), "追兵决策位置必须在 Lv3 grid 内。");
                int currentSectorId = ResolveDerivedSectorId(derivedData, currentX, currentY);
                bool hitsResearchCenter = desiredDirection.sqrMagnitude > 0f
                                          && researchCenter.RayHitsAnyBox(decisionPosition, desiredDirection, 18f);
                float wallHitDistance = hitsResearchCenter
                    ? researchCenter.DistanceToFirstRayHit(decisionPosition, desiredDirection, 18f)
                    : float.PositiveInfinity;
                bool hasSelectedPortal = TryResolveSelectedPortalCenter(chaser, currentSectorId, out int selectedPortalId, out Vector3 selectedPortalCenter);
                float selectedPortalDistance = hasSelectedPortal
                    ? HorizontalDistance(decisionPosition, selectedPortalCenter)
                    : float.PositiveInfinity;
                float targetDistance = HorizontalDistance(decisionPosition, hero.Position);
                float routeLimitDistance = Mathf.Min(selectedPortalDistance, targetDistance);
                bool segmentEntersResearchCenter = desiredDirection.sqrMagnitude > 0f
                                                   && SegmentEntersFootprint(
                                                       researchCenter,
                                                       decisionPosition,
                                                       desiredDirection,
                                                       Mathf.Min(routeLimitDistance + 0.2f, 18f),
                                                       grid.CellSize * 0.5f);
                bool actualNavigationSegmentViolatesResearchCenter = SteeringTileTargetSegmentViolatesFootprintClearance(
                    researchCenter,
                    decisionPosition,
                    chaser,
                    grid.CellSize * 0.5f,
                    0.45f);
                bool hitsBeforePortal = hitsResearchCenter
                                        && wallHitDistance <= routeLimitDistance + 0.2f
                                        && segmentEntersResearchCenter
                                        && actualNavigationSegmentViolatesResearchCenter;
                bool projectedToZero = desired.sqrMagnitude > 0.04f * 0.04f
                                       && executor.LastConstrainedDisplacement.sqrMagnitude <= 0.015f * 0.015f;
                zeroStreak[i] = projectedToZero ? zeroStreak[i] + 1 : 0;
                maxZeroStreak[i] = Mathf.Max(maxZeroStreak[i], zeroStreak[i]);
                float researchDistance = researchCenter.DistanceToClosestBox(decisionPosition);
                bool constrainedWallStall = projectedToZero && researchDistance <= 0.9f;
                bool hasSteeringGoal = FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringGoal(chaser.LogicEntityId.Value, out Vector3 steeringGoal, out int steeringFrame);
                bool staleSteering = !hasSteeringGoal || frame - steeringFrame > 2;

                if (hitsBeforePortal)
                {
                    wallBeforePortalSamples++;
                    minWallHitDistance = Mathf.Min(minWallHitDistance, wallHitDistance);
                }
                if (constrainedWallStall)
                    constrainedWallStallSamples++;
                if (staleSteering)
                    staleSteeringSamples++;

                bool isIssueSample = hitsBeforePortal || constrainedWallStall;
                bool isBackgroundSample = staleSteering || frame % 30 == 0;
                bool shouldRecordDetailedSample = (isIssueSample && issueDetailedSamples < 16)
                                                  || (!isIssueSample && isBackgroundSample && backgroundDetailedSamples < 6);
                if (shouldRecordDetailedSample)
                {
                    if (isIssueSample)
                        issueDetailedSamples++;
                    else
                        backgroundDetailedSamples++;
                    int sampleStart = diagnostics.Length;
                    diagnostics.Append("[Lv3MovingHeroResearchChase] frame=").Append(frame)
                        .Append(" agent=").Append(i)
                        .Append(" hero=").Append(hero.Position)
                        .Append(" decision=").Append(decisionPosition)
                        .Append(" after=").Append(chaser.Position);
                    AppendCellDiagnostic(diagnostics, grid, derivedData, decisionPosition, "decision");
                    diagnostics.Append(" desiredDisp=").Append(executor.LastDesiredDisplacement)
                        .Append(" constrainedDisp=").Append(executor.LastConstrainedDisplacement)
                        .Append(" zeroStreak=").Append(zeroStreak[i])
                        .Append(" rayHitsResearchCenter=").Append(hitsResearchCenter)
                        .Append(" wallHitDist=").Append(float.IsPositiveInfinity(wallHitDistance) ? "INF" : wallHitDistance.ToString("F3"))
                        .Append(" selectedPortal=").Append(hasSelectedPortal ? selectedPortalId.ToString() : "missing")
                        .Append(" portalDist=").Append(float.IsPositiveInfinity(selectedPortalDistance) ? "INF" : selectedPortalDistance.ToString("F3"))
                        .Append(" targetDist=").Append(targetDistance.ToString("F3"))
                        .Append(" segmentEntersResearchCenter=").Append(segmentEntersResearchCenter)
                        .Append(" actualNavSegmentViolatesResearchCenter=").Append(actualNavigationSegmentViolatesResearchCenter)
                        .Append(" hitsBeforePortal=").Append(hitsBeforePortal)
                        .Append(" researchDist=").Append(researchDistance.ToString("F3"))
                        .Append(" constrainedWallStall=").Append(constrainedWallStall)
                        .Append(" steeringGoal=").Append(hasSteeringGoal ? steeringGoal.ToString() : "missing")
                        .Append(" steeringFrame=").Append(hasSteeringGoal ? steeringFrame.ToString() : "missing")
                        .Append(" staleSteering=").Append(staleSteering)
                        .Append(" pendingFlow=").Append(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount())
                        .Append(" pendingShared=").Append(FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount())
                        .Append(" heroCell=(").Append(lastHeroCellX).Append(',').Append(lastHeroCellY).Append(')');
                    if (FlowFieldCrowdMovementSystem.TryGetEditorTestCurrentTileQueueState(
                            chaser.LogicEntityId.Value,
                            out string queueGoalKind,
                            out int queuePortalId,
                            out bool queueCached,
                            out bool queuePending,
                            out int queueIndex,
                            out string queueStage,
                            out bool queueWaiting,
                            out bool queueStale,
                            out int pendingPortalFrames))
                    {
                        diagnostics.Append(" tileQueue={kind=").Append(queueGoalKind)
                            .Append(",portal=").Append(queuePortalId)
                            .Append(",cached=").Append(queueCached)
                            .Append(",pending=").Append(queuePending)
                            .Append(",index=").Append(queueIndex)
                            .Append(",stage=").Append(queueStage)
                            .Append(",waiting=").Append(queueWaiting)
                            .Append(",stale=").Append(queueStale)
                            .Append(",pendingPortalFrames=").Append(pendingPortalFrames)
                            .Append('}');
                    }
                    else
                    {
                        diagnostics.Append(" tileQueue=missing");
                    }
                    AppendSteeringBreakdown(diagnostics, chaser);
                    AppendPathHandleDiagnostics(diagnostics, chaser, grid, derivedData, hero.Position);
                    if (hitsBeforePortal)
                        AppendSegmentGridDiagnostics(diagnostics, grid, derivedData, researchCenter, decisionPosition, "decision-to-tileTarget", chaser);
                    diagnostics.AppendLine();
                }
            }
        }

        int worstZeroStreak = 0;
        for (int i = 0; i < maxZeroStreak.Length; i++)
            worstZeroStreak = Mathf.Max(worstZeroStreak, maxZeroStreak[i]);

        Assert.LessOrEqual(wallBeforePortalSamples, 0,
            $"移动英雄绕研发中心右下时，追兵不应在到达当前 portal 前朝建筑正面推进。wallBeforePortal={wallBeforePortalSamples}, minHit={minWallHitDistance:F3}, targetCellSwitches={targetCellSwitches}, maxPendingFlow={maxPendingFlowTiles}, maxPendingShared={maxPendingSharedGoals}\n{diagnostics}");
        Assert.LessOrEqual(constrainedWallStallSamples, 0,
            $"移动英雄绕研发中心右下时，追兵不应贴近建筑后连续被导航约束吃掉位移。stallSamples={constrainedWallStallSamples}, worstZeroStreak={worstZeroStreak}, targetCellSwitches={targetCellSwitches}, maxPendingFlow={maxPendingFlowTiles}, maxPendingShared={maxPendingSharedGoals}\n{diagnostics}");
        Assert.LessOrEqual(staleSteeringSamples, 0,
            $"移动英雄绕研发中心右下时，Combat 链路不应长时间停用 steering。staleSteeringSamples={staleSteeringSamples}, targetCellSwitches={targetCellSwitches}, maxPendingFlow={maxPendingFlowTiles}, maxPendingShared={maxPendingSharedGoals}\n{diagnostics}");
    }

    public void Lv3研发中心真实路径矩阵不应撞建筑绕大圈或墙角聚团()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Medium.asset");
        Assert.NotNull(grid, "真实路径矩阵必须直接使用 Lv3_FlowNavigationGrid_Medium.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid_Medium.asset 必须带预烘焙 derived navigation data。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid_Medium.asset 的 derived navigation data 必须有效。");

        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Rect bounds = preset.ResearchCenterFootprintBounds;
        Rect strongholdBounds = preset.StrongholdSh13Bounds;
        Lv3CrowdRouteScenario[] scenarios =
        {
            new Lv3CrowdRouteScenario
            {
                Name = "真实SH1-3右下角追击后返回",
                HeroStart = new Vector3(bounds.xMin - 5.2f, 0f, bounds.yMin - 1.35f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(bounds.center.x, 0f, bounds.yMin - 1.2f), 0),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 42),
                    new Lv3HeroPathNode(new Vector3(bounds.xMin - 5.2f, 0f, bounds.yMin - 1.35f), 0),
                },
                ChaserCenters = preset.UnitSpawnCenters,
                ChaserCenterCounts = preset.UnitSpawnCounts,
                ChaserCount = preset.TotalUnitSpawnCount,
                Frames = 560,
                ExpectLowerRoute = true,
                UseNaturalTargetAcquisition = true,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "左侧中央追右下窄路后返回",
                HeroStart = new Vector3(bounds.xMin - 5.2f, 0f, bounds.yMin - 1.35f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(bounds.center.x, 0f, bounds.yMin - 1.2f), 0),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 5.0f, 0f, bounds.yMin - 1.35f), 28),
                    new Lv3HeroPathNode(new Vector3(bounds.xMin - 5.2f, 0f, bounds.yMin - 1.35f), 0),
                },
                ChaserCenters = new[]
                {
                    new Vector3(bounds.xMin - 7.0f, 0f, bounds.center.y),
                    new Vector3(bounds.xMin - 5.6f, 0f, bounds.yMin - 0.8f),
                },
                ChaserCount = 18,
                Frames = 260,
                ExpectLowerRoute = true,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "英雄从左下绕到右下角抖动再离开",
                HeroStart = new Vector3(bounds.xMin - 4.8f, 0f, bounds.yMin - 1.7f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(bounds.center.x - 0.6f, 0f, bounds.yMin - 1.5f), 0),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 3.2f, 0f, bounds.yMin - 1.75f), 10),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 3.2f, 0f, bounds.yMin - 0.85f), 10),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 5.2f, 0f, bounds.yMin - 1.35f), 18),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 8.0f, 0f, bounds.center.y), 0),
                },
                ChaserCenters = new[]
                {
                    new Vector3(bounds.xMin - 6.8f, 0f, bounds.center.y),
                    new Vector3(bounds.xMin - 6.0f, 0f, bounds.yMin + 0.4f),
                },
                ChaserCount = 20,
                Frames = 270,
                ExpectLowerRoute = true,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "英雄先走研发中心背面再切右下",
                HeroStart = new Vector3(bounds.xMin - 5.4f, 0f, bounds.center.y),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(bounds.xMin - 3.0f, 0f, bounds.yMax + 1.4f), 0),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 4.2f, 0f, bounds.yMax + 1.5f), 14),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 5.0f, 0f, bounds.yMin - 1.35f), 22),
                    new Lv3HeroPathNode(new Vector3(bounds.xMin - 5.4f, 0f, bounds.center.y), 0),
                },
                ChaserCenters = new[]
                {
                    new Vector3(bounds.xMin - 7.0f, 0f, bounds.center.y),
                    new Vector3(bounds.xMin - 5.8f, 0f, bounds.yMax + 1.2f),
                },
                ChaserCount = 18,
                Frames = 270,
                ExpectLowerRoute = false,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "混合兵群从上下两侧追右下角",
                HeroStart = new Vector3(bounds.xMin - 5.0f, 0f, bounds.yMin - 1.25f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(bounds.center.x, 0f, bounds.yMin - 1.3f), 0),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 5.4f, 0f, bounds.yMin - 1.25f), 34),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 8.4f, 0f, bounds.yMin + 1.2f), 0),
                },
                ChaserCenters = new[]
                {
                    new Vector3(bounds.xMin - 7.0f, 0f, bounds.center.y),
                    new Vector3(bounds.xMin - 5.4f, 0f, bounds.yMin - 0.7f),
                    new Vector3(bounds.xMin - 5.4f, 0f, bounds.yMax + 1.1f),
                },
                ChaserCount = 22,
                Frames = 250,
                ExpectLowerRoute = true,
            },
        };

        for (int i = 0; i < scenarios.Length; i++)
            RunLv3ResearchCenterCrowdRouteScenario(grid, derivedData, preset, scenarios[i], 990000 + i * 100);
    }

    public void Lv3真实SH13多路径追击不应卡建筑或墙角聚团()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Medium.asset");
        Assert.NotNull(grid, "真实 SH_1_3 多路径压力测试必须直接使用 Lv3_FlowNavigationGrid_Medium.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid_Medium.asset 必须带预烘焙 derived navigation data。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid_Medium.asset 的 derived navigation data 必须有效。");

        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Rect researchBounds = preset.ResearchCenterFootprintBounds;
        Rect strongholdBounds = preset.StrongholdSh13Bounds;
        Lv3CrowdRouteScenario[] scenarios =
        {
            new Lv3CrowdRouteScenario
            {
                Name = "真实刷怪-英雄左下穿研发中心下侧到SH13右下再返回",
                HeroStart = preset.HeroStart,
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 7.0f, 0f, researchBounds.center.y), 0),
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 4.8f, 0f, researchBounds.yMin - 1.4f), 0),
                    new Lv3HeroPathNode(new Vector3(researchBounds.center.x, 0f, researchBounds.yMin - 1.35f), 0),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 48),
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 6.0f, 0f, researchBounds.center.y), 0),
                },
                ChaserCenters = preset.UnitSpawnCenters,
                ChaserCenterCounts = preset.UnitSpawnCounts,
                ChaserCount = preset.TotalUnitSpawnCount,
                Frames = 620,
                ExpectLowerRoute = true,
                UseNaturalTargetAcquisition = true,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "真实刷怪-英雄贴右下角短暂停留后向左上脱离",
                HeroStart = new Vector3(researchBounds.xMin - 5.5f, 0f, researchBounds.yMin - 1.45f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(researchBounds.center.x, 0f, researchBounds.yMin - 1.35f), 0),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 54),
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMax + 3.5f, 0f, researchBounds.yMin + 0.2f), 18),
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 6.5f, 0f, researchBounds.yMax + 1.2f), 0),
                },
                ChaserCenters = preset.UnitSpawnCenters,
                ChaserCenterCounts = preset.UnitSpawnCounts,
                ChaserCount = preset.TotalUnitSpawnCount,
                Frames = 560,
                ExpectLowerRoute = true,
                UseNaturalTargetAcquisition = true,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "真实刷怪-英雄从建筑上侧切到右下再返回",
                HeroStart = new Vector3(researchBounds.xMin - 6.0f, 0f, researchBounds.yMax + 1.3f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMax + 4.0f, 0f, researchBounds.yMax + 1.2f), 16),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 42),
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 5.5f, 0f, researchBounds.yMin - 1.2f), 0),
                },
                ChaserCenters = preset.UnitSpawnCenters,
                ChaserCenterCounts = preset.UnitSpawnCounts,
                ChaserCount = preset.TotalUnitSpawnCount,
                Frames = 560,
                ExpectLowerRoute = false,
                UseNaturalTargetAcquisition = true,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "真实刷怪-英雄右下角小范围折返后离开",
                HeroStart = new Vector3(researchBounds.xMin - 5.2f, 0f, researchBounds.yMin - 1.35f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(researchBounds.center.x, 0f, researchBounds.yMin - 1.35f), 0),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax - 1.0f, 0f, strongholdBounds.yMin + 0.5f), 18),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 18),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax - 2.0f, 0f, strongholdBounds.yMin + 1.0f), 18),
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 6.0f, 0f, researchBounds.center.y), 0),
                },
                ChaserCenters = preset.UnitSpawnCenters,
                ChaserCenterCounts = preset.UnitSpawnCounts,
                ChaserCount = preset.TotalUnitSpawnCount,
                Frames = 640,
                ExpectLowerRoute = true,
                UseNaturalTargetAcquisition = true,
            },
        };

        for (int i = 0; i < scenarios.Length; i++)
            RunLv3ResearchCenterCrowdRouteScenario(grid, derivedData, preset, scenarios[i], 992000 + i * 100);
    }

    public void Lv3真实SH13追击使用CharacterController时不应被建筑或墙角卡住()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Medium.asset");
        Assert.NotNull(grid, "CharacterController 真实追击测试必须直接使用 Lv3_FlowNavigationGrid_Medium.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid_Medium.asset 必须带预烘焙 derived navigation data。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid_Medium.asset 的 derived navigation data 必须有效。");

        EntityRegistry.Clear();
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 320;
        SetNavigationWorkQuotas(config, 1_000_000);
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        const float agentRadius = 0.45f;
        const float dt = 0.1f;
        const float heroSpeed = 2.5f;
        const float chaserSpeed = 3.5f;
        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(preset.ResearchCenterPosition);
        RegisterLv3ResearchCenterFootprintObstacles(researchCenter, 993000);
        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "CharacterController 真实追击测试必须先提交研发中心 Autobox runtime dirty。");

        System.Text.StringBuilder diagnostics = new System.Text.StringBuilder();
        Rect researchBounds = preset.ResearchCenterFootprintBounds;
        Rect strongholdBounds = preset.StrongholdSh13Bounds;
        List<GameObject> createdObjects = new List<GameObject>();
        try
        {
            GameObject ground = new GameObject("FlowTest_GroundCollider");
            ground.transform.position = new Vector3(
                grid.Origin.x + grid.Width * grid.CellSize * 0.5f,
                -0.055f,
                grid.Origin.y + grid.Height * grid.CellSize * 0.5f);
            BoxCollider groundCollider = ground.AddComponent<BoxCollider>();
            groundCollider.size = new Vector3(grid.Width * grid.CellSize + 24f, 0.1f, grid.Height * grid.CellSize + 24f);
            createdObjects.Add(ground);

            for (int i = 0; i < researchCenter.Boxes.Length; i++)
            {
                Rect box = researchCenter.Boxes[i];
                GameObject obstacle = new GameObject("FlowTest_ResearchCenterCollider_" + i);
                obstacle.transform.position = new Vector3(box.center.x, 1f, box.center.y);
                BoxCollider collider = obstacle.AddComponent<BoxCollider>();
                collider.size = new Vector3(box.width, 2f, box.height);
                createdObjects.Add(obstacle);
            }
            Physics.SyncTransforms();

            Vector3 heroStart = ResolveRuntimeLegalLv3MainIslandCell(
                grid,
                derivedData,
                preset.HeroStart,
                6f,
                agentRadius,
                "controller-hero-start",
                diagnostics);
            Vector3[] heroWaypoints =
            {
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(researchBounds.xMin - 7.0f, 0f, researchBounds.center.y), 7f, agentRadius, "controller-hero-left", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(researchBounds.center.x, 0f, researchBounds.yMin - 1.35f), 7f, agentRadius, "controller-hero-lower", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 7f, agentRadius, "controller-hero-sh13-corner", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(researchBounds.xMin - 6.0f, 0f, researchBounds.center.y), 7f, agentRadius, "controller-hero-return", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(researchBounds.xMin - 5.8f, 0f, researchBounds.yMax + 1.2f), 7f, agentRadius, "controller-hero-upper-left", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(researchBounds.xMax + 4.0f, 0f, researchBounds.yMax + 1.2f), 7f, agentRadius, "controller-hero-upper-right", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 7f, agentRadius, "controller-hero-sh13-corner-repeat", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(researchBounds.xMin - 5.2f, 0f, researchBounds.yMin - 1.35f), 7f, agentRadius, "controller-hero-lower-return", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(researchBounds.xMin - 6.0f, 0f, researchBounds.center.y), 7f, agentRadius, "controller-hero-final-left", diagnostics)
            };

            Lv3CrowdRouteScenario spawnScenario = new Lv3CrowdRouteScenario
            {
                Name = "CharacterController真实刷怪",
                ChaserCenters = preset.UnitSpawnCenters,
                ChaserCenterCounts = preset.UnitSpawnCounts,
                ChaserCount = preset.TotalUnitSpawnCount
            };
            Vector3[] starts = ResolveLv3ScenarioChaserStartsFromRealSpawnPreview(grid, derivedData, spawnScenario, agentRadius, diagnostics);
            SimEntityContext hero = CreateEntity(heroStart, false, grid.AgentTypeId, agentRadius);
            hero.Side = SideType.PlayerSide;
            EntityRegistry.Register(hero);

            SimEntityContext[] chasers = new SimEntityContext[starts.Length];
            MoveExecutor[] executors = new MoveExecutor[starts.Length];
            Transform[] transforms = new Transform[starts.Length];
            for (int i = 0; i < starts.Length; i++)
            {
                SimEntityContext chaser = CreateEntity(starts[i], false, grid.AgentTypeId, agentRadius);
                chaser.Side = SideType.EnemySide;
                chaser.SetProperty(CreatureMainProperty.Speed, (Fix64)(chaserSpeed / DistanceUnitConverter.DefaultDistanceConversionRate));
                GameObject go = new GameObject("FlowTest_CC_Chaser_" + i);
                go.transform.position = starts[i];
                CharacterController controller = go.AddComponent<CharacterController>();
                controller.radius = agentRadius;
                controller.height = 2f;
                controller.center = new Vector3(0f, 1f, 0f);
                MoveExecutor executor = go.AddComponent<MoveExecutor>();
                executor.Init(controller, grid.AgentTypeId);
                chasers[i] = chaser;
                executors[i] = executor;
                transforms[i] = go.transform;
                createdObjects.Add(go);
                EntityRegistry.Register(chaser);
            }
            Physics.SyncTransforms();

            int waypointIndex = 0;
            int waypointHoldFrames = 0;
            int controllerStallSamples = 0;
            int wallClusterStreak = 0;
            int maxWallClusterStreak = 0;
            int overlapPairSamples = 0;
            int maxOverlapPairsThisFrame = 0;
            int overlapPairStreak = 0;
            int maxOverlapPairStreak = 0;
            float minHeroDistance = float.PositiveInfinity;
            for (int frame = 1; frame <= 1120; frame++)
            {
                FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
                Vector3 activeWaypoint = heroWaypoints[Mathf.Min(waypointIndex, heroWaypoints.Length - 1)];
                hero.Position = AdvanceHeroWithNavigationConstraint(hero.Position, activeWaypoint, heroSpeed, dt, grid.AgentTypeId, 0.5f, diagnostics);
                Vector3 toWaypoint = activeWaypoint - hero.Position;
                toWaypoint.y = 0f;
                if (toWaypoint.sqrMagnitude <= 0.35f * 0.35f && waypointIndex < heroWaypoints.Length - 1)
                {
                    int holdFrames = waypointIndex == 2 || waypointIndex == 6 ? 48 : waypointIndex == 4 ? 18 : 0;
                    if (waypointHoldFrames < holdFrames)
                        waypointHoldFrames++;
                    else
                    {
                        waypointIndex++;
                        waypointHoldFrames = 0;
                    }
                }

                FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
                int slowNearWallThisFrame = 0;
                for (int i = 0; i < chasers.Length; i++)
                {
                    SimEntityContext chaser = chasers[i];
                    chaser.Position = transforms[i].position;
                    Vector3 before = transforms[i].position;
                    bool gotVelocity = FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, hero.Position, chaserSpeed, out Vector3 velocity);
                    Assert.IsTrue(gotVelocity, $"CharacterController 真实追击必须能取得流场速度。frame={frame}, agent={i}, pos={chaser.Position}, hero={hero.Position}");
                    executors[i].SetInput(velocity);
                    executors[i].Execute(dt);
                    Vector3 after = transforms[i].position;
                    chaser.Position = after;

                    Vector3 actual = after - before;
                    actual.y = 0f;
                    minHeroDistance = Mathf.Min(minHeroDistance, HorizontalDistance(after, hero.Position));
                    float researchDistance = researchCenter.DistanceToClosestBox(after);
                    bool activeButControllerStopped = velocity.magnitude > chaserSpeed * 0.45f
                                                      && actual.magnitude < chaserSpeed * dt * 0.12f
                                                      && HorizontalDistance(after, hero.Position) > 1.25f;
                    if (activeButControllerStopped && researchDistance <= 1.05f)
                    {
                        controllerStallSamples++;
                        slowNearWallThisFrame++;
                        if (controllerStallSamples <= 20)
                        {
                            diagnostics.Append("[Lv3ControllerStall] frame=").Append(frame)
                                .Append(" agent=").Append(i)
                                .Append(" before=").Append(before)
                                .Append(" after=").Append(after)
                                .Append(" hero=").Append(hero.Position)
                                .Append(" velocity=").Append(velocity)
                                .Append(" actual=").Append(actual)
                                .Append(" researchDist=").Append(researchDistance.ToString("F3"));
                            AppendCellDiagnostic(diagnostics, grid, derivedData, after, "after");
                            AppendPathHandleDiagnostics(diagnostics, chaser, grid, derivedData, hero.Position);
                            diagnostics.AppendLine();
                        }
                    }
                }

                int overlapPairsThisFrame = CollectLv3ControllerOverlapSamples(
                    diagnostics,
                    grid,
                    derivedData,
                    transforms,
                    hero.Position,
                    frame,
                    agentRadius,
                    maxOverlapPairStreak,
                    overlapPairSamples,
                    chasers,
                    hero);
                overlapPairSamples += overlapPairsThisFrame;
                maxOverlapPairsThisFrame = Mathf.Max(maxOverlapPairsThisFrame, overlapPairsThisFrame);
                if (overlapPairsThisFrame >= 3)
                    overlapPairStreak++;
                else
                    overlapPairStreak = 0;
                maxOverlapPairStreak = Mathf.Max(maxOverlapPairStreak, overlapPairStreak);

                if (slowNearWallThisFrame >= 3)
                    wallClusterStreak++;
                else
                    wallClusterStreak = 0;
                maxWallClusterStreak = Mathf.Max(maxWallClusterStreak, wallClusterStreak);
            }

            Debug.LogWarning(
                $"[Lv3ControllerScenarioSummary] controllerStall={controllerStallSamples} maxWallClusterStreak={maxWallClusterStreak} " +
                $"overlapPairs={overlapPairSamples} maxOverlapPairsFrame={maxOverlapPairsThisFrame} maxOverlapPairStreak={maxOverlapPairStreak} " +
                $"minHeroDist={minHeroDistance:F3}");
            Assert.Less(controllerStallSamples, 6,
                $"CharacterController 真实追击不应出现多次已获得流场速度但被建筑/墙角实际位移卡住。stall={controllerStallSamples}, maxWallClusterStreak={maxWallClusterStreak}, minHeroDist={minHeroDistance:F3}\n{diagnostics}");
            Assert.Less(maxWallClusterStreak, 4,
                $"CharacterController 真实追击不应出现多人连续贴墙停滞。stall={controllerStallSamples}, maxWallClusterStreak={maxWallClusterStreak}, minHeroDist={minHeroDistance:F3}\n{diagnostics}");
            Assert.LessOrEqual(maxOverlapPairsThisFrame, 2,
                $"CharacterController 真实追击不应允许多个追兵在英雄附近叠成一个点。overlapPairs={overlapPairSamples}, maxOverlapPairsFrame={maxOverlapPairsThisFrame}, maxOverlapPairStreak={maxOverlapPairStreak}, minHeroDist={minHeroDistance:F3}\n{diagnostics}");
            Assert.LessOrEqual(maxOverlapPairStreak, 3,
                $"CharacterController 真实追击不应持续出现追兵重叠聚团。overlapPairs={overlapPairSamples}, maxOverlapPairsFrame={maxOverlapPairsThisFrame}, maxOverlapPairStreak={maxOverlapPairStreak}, minHeroDist={minHeroDistance:F3}\n{diagnostics}");
            Assert.Less(minHeroDistance, 5f,
                $"CharacterController 真实追击必须实际接近英雄，否则测试未覆盖接敌链路。minHeroDist={minHeroDistance:F3}\n{diagnostics}");
        }
        finally
        {
            for (int i = 0; i < createdObjects.Count; i++)
            {
                if (createdObjects[i] != null)
                    UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }
        }
    }


    [Test]
    public void Lv3实机亚格位置追击_定点链应能绕过研发中心接敌()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Small.asset");
        FlowNavigationGridAsset spawnGrid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Medium.asset");
        Assert.NotNull(grid);
        Assert.NotNull(spawnGrid);
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData);
        Assert.IsTrue(derivedData.IsValid);

        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        var scenario = new Lv3CrowdRouteScenario
        {
            Name = "CC实机日志-研发中心对侧亚格位置追击",
            HeroStart = new Vector3(84.41f, 0f, 16.84f),
            HeroNodes = new[]
            {
                new Lv3HeroPathNode(new Vector3(84.41f, 0f, 16.84f), 180),
            },
            ExactChaserStarts = new[]
            {
                new Vector3(78.43f, 0f, 17.93f),
                new Vector3(78.45f, 0f, 18.65f),
            },
            ChaserCount = 2,
            Frames = 240,
            PreserveExactPositions = true,
            SkipRuntimeObstacleRegistration = true,
        };

        RunLv3CharacterControllerCombatScenario(grid, spawnGrid, derivedData, preset, scenario, 996000);
    }

    [Test]
    public void Lv3真实SH13右下角追击返程不应墙角聚团()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Small.asset");
        Assert.NotNull(grid, "真实 SH_1_3 右下角回归必须使用实机轻型单位的 Lv3_FlowNavigationGrid_Small.asset。");
        FlowNavigationGridAsset spawnGrid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Medium.asset");
        Assert.NotNull(spawnGrid, "真实 SH_1_3 右下角回归必须注册默认刷怪导航源。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid_Small.asset 必须带预烘焙 derived navigation data。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid_Small.asset 的 derived navigation data 必须有效。");

        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Rect researchBounds = preset.ResearchCenterFootprintBounds;
        Rect strongholdBounds = preset.StrongholdSh13Bounds;
        Lv3CrowdRouteScenario scenario = new Lv3CrowdRouteScenario
        {
            Name = "真实SH1-3右下角追击后返回",
            HeroStart = preset.HeroStart,
            HeroNodes = new[]
            {
                new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 8.0f, 0f, researchBounds.center.y), 0),
                new Lv3HeroPathNode(new Vector3(researchBounds.center.x, 0f, researchBounds.yMin - 1.2f), 0),
                new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 42),
                new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 5.2f, 0f, researchBounds.yMin - 1.35f), 0),
            },
            ChaserCenters = preset.UnitSpawnCenters,
            ChaserCenterCounts = preset.UnitSpawnCounts,
            ChaserCount = preset.TotalUnitSpawnCount,
            Frames = 760,
            ExpectLowerRoute = true,
            UseNaturalTargetAcquisition = true,
        };

        RunLv3CharacterControllerCombatScenario(grid, spawnGrid, derivedData, preset, scenario, 991000);
    }

    [TestCase(1937, 2861)]
    [TestCase(2334, 2863)]
    public void Lv3占领SH13后同主岛Follow应能构建Sector路径(int startSectorId, int goalSectorId)
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Small.asset");
        Assert.NotNull(grid);
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData);
        Assert.IsTrue(derivedData.IsValid);

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            derivedData,
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        List<Vector3> starts = ResolveMainIslandCellsInSector(grid, derivedData, startSectorId);
        List<Vector3> goals = ResolveMainIslandCellsInSector(grid, derivedData, goalSectorId);
        Vector3 representativeStart = starts[starts.Count / 2];
        Vector3 representativeGoal = goals[goals.Count / 2];
        SimEntityContext warmup = CreateEntity(representativeStart, false, grid.AgentTypeId, 0.18f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(warmup, representativeGoal, 2f, out _));

        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(preset.ResearchCenterPosition);
        RegisterLv3ResearchCenterFootprintObstacles(researchCenter, 997000);
        for (int i = 0; i < 4096 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());

        int frame = 2;

        for (int i = 0; i < starts.Count; i++)
        {
            SimEntityContext ctx = CreateEntity(starts[i], false, grid.AgentTypeId, 0.18f);
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame++, frame * 0.1f);
            Assert.IsTrue(
                FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, representativeGoal, 2f, out _),
                $"同主岛 Follow 路径必须可构建。start={starts[i]} startSector={startSectorId} goal={representativeGoal} goalSector={goalSectorId}");
        }

        for (int i = 0; i < goals.Count; i++)
        {
            SimEntityContext ctx = CreateEntity(representativeStart, false, grid.AgentTypeId, 0.18f);
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame++, frame * 0.1f);
            Assert.IsTrue(
                FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goals[i], 2f, out _),
                $"同主岛 Follow 路径必须可构建。start={representativeStart} startSector={startSectorId} goal={goals[i]} goalSector={goalSectorId}");
        }
    }

    public void Lv3研发中心下侧追击时初段方向不应指向建筑正面()
    {
        System.Diagnostics.Stopwatch testWatch = System.Diagnostics.Stopwatch.StartNew();
        Debug.Log("[Lv3ResearchCenterChaseTest] stage=begin");
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Medium.asset");
        Assert.NotNull(grid, "真实场景回归必须直接使用 Lv3_FlowNavigationGrid_Medium.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid_Medium.asset 必须带预烘焙 derived navigation data，测试才与实机链路一致。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid_Medium.asset 的 derived navigation data 必须有效。");
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=grid-loaded elapsedMs={testWatch.Elapsed.TotalMilliseconds:F1} size={grid.Width}x{grid.Height} cell={grid.CellSize:F3} agentType={grid.AgentTypeId}");

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 256;
        SetNavigationWorkQuotas(config, 1_000_000);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=source-applied elapsedMs={testWatch.Elapsed.TotalMilliseconds:F1}");
        ProcessWorldBuildQueueUntilReady();
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=world-ready elapsedMs={testWatch.Elapsed.TotalMilliseconds:F1}");

        System.Text.StringBuilder diagnostics = new System.Text.StringBuilder();
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=resolve-route-begin elapsedMs={testWatch.Elapsed.TotalMilliseconds:F1}");
        Lv3RightBottomRoute route = ResolveLv3RightBottomRoute(grid, derivedData, diagnostics);
        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(route.ResearchCenterPosition);
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=resolve-route-end elapsedMs={testWatch.Elapsed.TotalMilliseconds:F1} chasers={route.ChaserStarts.Length} hero={route.RightBottomCorner} research={route.ResearchCenterPosition}");

        SimEntityContext hero = CreateEntity(route.RightBottomCorner, false, grid.AgentTypeId, 0.45f);
        hero.Side = SideType.PlayerSide;
        int testedChasers = Mathf.Min(8, route.ChaserStarts.Length);
        SimEntityContext[] chasers = new SimEntityContext[testedChasers];
        CharacterMoveComp[] moveComps = new CharacterMoveComp[testedChasers];
        SimMoveExecutor[] executors = new SimMoveExecutor[testedChasers];
        Assert.IsTrue(grid.WorldToCell(hero.Position, out int goalX, out int goalY), "英雄目标点必须在 Lv3 grid 内。");
        for (int i = 0; i < testedChasers; i++)
        {
            SimEntityContext chaser = CreateEntity(route.ChaserStarts[i], false, grid.AgentTypeId, 0.45f);
            chaser.Side = SideType.EnemySide;
            chaser.SetProperty(CreatureMainProperty.Speed, (Fix64)(3.5f / DistanceUnitConverter.DefaultDistanceConversionRate));
            chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { hero })
            {
                CurrentTarget = hero
            };

            CharacterMoveComp moveComp = new CharacterMoveComp();
            moveComp.Init(chaser, grid.AgentTypeId);
            chaser.MoveComp = moveComp;
            SimMoveExecutor executor = new SimMoveExecutor
            {
                Position = chaser.Position,
                ApplyNavigationConstraint = true,
                AgentTypeId = grid.AgentTypeId,
                EdgeClearance = 0.5f
            };
            chaser.MoveExecutor = executor;

            Assert.IsTrue(grid.WorldToCell(chaser.Position, out _, out _), "追兵起点必须在 Lv3 grid 内。");
            chasers[i] = chaser;
            moveComps[i] = moveComp;
            executors[i] = executor;
        }

        int wallDirectedBeforePortalSamples = 0;
        int nearWallPushSamples = 0;
        int anyWallRaySamples = 0;
        int totalSamples = 0;
        float minWallHitBeforePortalDistance = float.PositiveInfinity;
        float minResearchCenterDistance = float.PositiveInfinity;
        const float dt = 0.1f;
        for (int frame = 1; frame <= 40; frame++)
        {
            Debug.Log($"[Lv3ResearchCenterChaseTest] stage=frame-begin frame={frame} elapsedMs={testWatch.Elapsed.TotalMilliseconds:F1}");
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
            if (frame > 1)
                FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

            for (int i = 0; i < chasers.Length; i++)
            {
                SimEntityContext chaser = chasers[i];
                SimMoveExecutor executor = executors[i];
                chaser.SyncPositionToExecutor();
                Vector3 decisionPosition = chaser.Position;
                moveComps[i].MoveToFixed(hero.PositionFixed);
                moveComps[i].Move((Fix64)dt);
                executor.Execute(dt);
                chaser.SyncPositionFromExecutor();

                Vector3 desired = executor.LastDesiredDisplacement;
                desired.y = 0f;
                Vector3 desiredDirection = desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector3.zero;
                Assert.IsTrue(grid.WorldToCell(decisionPosition, out int currentX, out int currentY), "追兵决策位置必须在 Lv3 grid 内。");
                int currentSectorId = ResolveDerivedSectorId(derivedData, currentX, currentY);
                bool hitsResearchCenter = desiredDirection.sqrMagnitude > 0f
                                          && researchCenter.RayHitsAnyBox(decisionPosition, desiredDirection, 18f);
                float wallHitDistance = hitsResearchCenter
                    ? researchCenter.DistanceToFirstRayHit(decisionPosition, desiredDirection, 18f)
                    : float.PositiveInfinity;
                bool hasSelectedPortal = TryResolveSelectedPortalCenter(chaser, currentSectorId, out int selectedPortalId, out Vector3 selectedPortalCenter);
                float selectedPortalDistance = hasSelectedPortal
                    ? HorizontalDistance(decisionPosition, selectedPortalCenter)
                    : float.PositiveInfinity;
                float targetDistance = HorizontalDistance(decisionPosition, hero.Position);
                float routeLimitDistance = Mathf.Min(selectedPortalDistance, targetDistance);
                bool segmentEntersResearchCenter = desiredDirection.sqrMagnitude > 0f
                                                   && SegmentEntersFootprint(
                                                       researchCenter,
                                                       decisionPosition,
                                                       desiredDirection,
                                                       Mathf.Min(routeLimitDistance + 0.2f, 18f),
                                                       grid.CellSize * 0.5f);
                bool hitsBeforePortal = hitsResearchCenter
                                        && wallHitDistance <= routeLimitDistance + 0.2f
                                        && segmentEntersResearchCenter;
                float researchCenterDistance = researchCenter.DistanceToClosestBox(decisionPosition);
                minResearchCenterDistance = Mathf.Min(minResearchCenterDistance, researchCenterDistance);
                bool nearWallPush = researchCenterDistance <= 0.65f
                                    && hitsResearchCenter
                                    && wallHitDistance <= 1.0f
                                    && desired.sqrMagnitude > 0.0004f;
                if (hitsResearchCenter)
                {
                    anyWallRaySamples++;
                    if (hitsBeforePortal)
                    {
                        wallDirectedBeforePortalSamples++;
                        minWallHitBeforePortalDistance = Mathf.Min(minWallHitBeforePortalDistance, wallHitDistance);
                    }

                    if (nearWallPush)
                        nearWallPushSamples++;
                }
                totalSamples++;

                if (hitsBeforePortal || nearWallPush || frame <= 4 || frame % 10 == 0)
                {
                    int sampleStart = diagnostics.Length;
                    diagnostics.Append("[Lv3ResearchCenterChase] frame=").Append(frame)
                        .Append(" agent=").Append(i)
                        .Append(" decisionPos=").Append(decisionPosition)
                        .Append(" afterPos=").Append(chaser.Position);
                    AppendCellDiagnostic(diagnostics, grid, derivedData, decisionPosition, "decision");
                    AppendCellDiagnostic(diagnostics, grid, derivedData, chaser.Position, "after");
                    diagnostics.Append(" hero=").Append(hero.Position)
                        .Append(" desiredDisp=").Append(executor.LastDesiredDisplacement)
                        .Append(" constrainedDisp=").Append(executor.LastConstrainedDisplacement)
                        .Append(" desiredDir=").Append(desiredDirection)
                        .Append(" rayHitsResearchCenter=").Append(hitsResearchCenter)
                        .Append(" wallHitDist=").Append(float.IsPositiveInfinity(wallHitDistance) ? "INF" : wallHitDistance.ToString("F3"))
                        .Append(" selectedPortal=").Append(hasSelectedPortal ? selectedPortalId.ToString() : "missing")
                        .Append(" portalDist=").Append(float.IsPositiveInfinity(selectedPortalDistance) ? "INF" : selectedPortalDistance.ToString("F3"))
                        .Append(" targetDist=").Append(targetDistance.ToString("F3"))
                        .Append(" segmentEntersResearchCenter=").Append(segmentEntersResearchCenter)
                        .Append(" hitsBeforePortal=").Append(hitsBeforePortal)
                        .Append(" researchDist=").Append(researchCenterDistance.ToString("F3"))
                        .Append(" nearWallPush=").Append(nearWallPush);
                    AppendSteeringBreakdown(diagnostics, chaser);
                    AppendPathHandleDiagnostics(diagnostics, chaser, grid, derivedData, hero.Position);
                    if (hitsBeforePortal)
                        AppendSegmentGridDiagnostics(diagnostics, grid, derivedData, researchCenter, decisionPosition, "decision-to-tileTarget", chaser);
                    diagnostics.AppendLine();
                    if (hitsBeforePortal)
                        Debug.LogWarning("[Lv3WallBeforePortalSample] " + diagnostics.ToString(sampleStart, diagnostics.Length - sampleStart));
                }
            }
        }
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=assert elapsedMs={testWatch.Elapsed.TotalMilliseconds:F1} wallBeforePortal={wallDirectedBeforePortalSamples}/{totalSamples} nearWallPush={nearWallPushSamples} anyWallRay={anyWallRaySamples} minResearchDist={minResearchCenterDistance:F3}");

        Assert.LessOrEqual(wallDirectedBeforePortalSamples, 0,
            $"真实 Lv3 研发中心下侧追击不应在到达当前 portal 前就朝研发中心碰撞体推进。wallBeforePortal={wallDirectedBeforePortalSamples}/{totalSamples}, minHitDist={minWallHitBeforePortalDistance:F3}, anyWallRay={anyWallRaySamples}\n{diagnostics}");
        Assert.LessOrEqual(nearWallPushSamples, 0,
            $"真实 Lv3 研发中心下侧追击不应贴近研发中心后仍向碰撞体内部推进。nearWallPush={nearWallPushSamples}, minResearchDist={minResearchCenterDistance:F3}, anyWallRay={anyWallRaySamples}\n{diagnostics}");
    }

    private struct Lv3RightBottomRoute
    {
        public Vector3 HeroStart;
        public Vector3 LowerApproach;
        public Vector3 RightBottomCorner;
        public Vector3 ReturnPoint;
        public Vector3 ResearchCenterPosition;
        public Vector3[] ChaserStarts;
        public Rect ResearchCenterBounds;
        public Rect StrongholdBounds;
    }

    private struct Lv3PresetSnapshot
    {
        public Vector3 HeroStart;
        public Vector3 ResearchCenterPosition;
        public Vector3[] UnitSpawnCenters;
        public int[] UnitSpawnCounts;
        public int TotalUnitSpawnCount;
        public Rect ResearchCenterFootprintBounds;
        public Rect StrongholdSh13Bounds;
    }

    private struct Lv3UnitSpawnPreset
    {
        public Vector3 Position;
        public int Count;
    }

    private struct Lv3HeroPathNode
    {
        public Vector3 Preferred;
        public int HoldFrames;

        public Lv3HeroPathNode(Vector3 preferred, int holdFrames)
        {
            Preferred = preferred;
            HoldFrames = holdFrames;
        }
    }

    private struct Lv3CrowdRouteScenario
    {
        public string Name;
        public Vector3 HeroStart;
        public Lv3HeroPathNode[] HeroNodes;
        public Vector3[] ChaserCenters;
        public int[] ChaserCenterCounts;
        public Vector3[] ExactChaserStarts;
        public int ChaserCount;
        public int Frames;
        public bool ExpectLowerRoute;
        public bool UseNaturalTargetAcquisition;
        public bool PreserveExactPositions;
        public bool SkipRuntimeObstacleRegistration;
    }

    private sealed class Lv3CrowdRouteMetrics
    {
        public int WallBeforePortalSamples;
        public int ConstrainedWallStallSamples;
        public int StaleSteeringSamples;
        public int NearWallGoalSamples;
        public int UpperDetourSamples;
        public int MaxWallClusterStreak;
        public int MaxZeroStreak;
        public int MaxPendingFlowTiles;
        public int MaxPendingSharedGoals;
        public int MaxTargetedSlowStreak;
        public int ConstraintFailureSamples;
        public int OverlapPairSamples;
        public int MaxOverlapPairsThisFrame;
        public int MaxOverlapPairStreak;
        public int DetailedIssueSamples;
        public int DetailedBackgroundSamples;
        public float MinWallHitDistance = float.PositiveInfinity;
        public float MinFinalHeroDistance = float.PositiveInfinity;
        public readonly System.Text.StringBuilder Diagnostics = new System.Text.StringBuilder();
    }

    private static int ResolveDerivedSectorId(FlowNavigationGridAsset.DerivedNavigationData derivedData, int x, int y)
    {
        if (derivedData == null || !derivedData.IsValid)
            throw new InvalidOperationException("ResolveDerivedSectorId failed: derivedData is invalid.");

        int sectorX = x / derivedData.SectorSizeInCells;
        int sectorY = y / derivedData.SectorSizeInCells;
        if (sectorX < 0 || sectorX >= derivedData.SectorCountX || sectorY < 0 || sectorY >= derivedData.SectorCountY)
            throw new InvalidOperationException($"ResolveDerivedSectorId failed: cell=({x},{y}) sector=({sectorX},{sectorY}) outside {derivedData.SectorCountX}x{derivedData.SectorCountY}.");

        FlowNavigationGridAsset.SectorDerivedData sector = derivedData.Sectors[sectorX + sectorY * derivedData.SectorCountX];
        return sector.SectorId;
    }

    private static List<Vector3> ResolveMainIslandCellsInSector(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        int sectorId)
    {
        if (sectorId < 0 || sectorId >= derivedData.Sectors.Length)
            throw new InvalidOperationException($"ResolveMainIslandCellsInSector failed: sectorId={sectorId} is outside 0..{derivedData.Sectors.Length - 1}.");

        FlowNavigationGridAsset.SectorDerivedData sector = derivedData.Sectors[sectorId];
        bool[] walkable = grid.GetWalkableMaskRuntimeReadOnlyReference();
        List<Vector3> result = new List<Vector3>(sector.Width * sector.Height);
        for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
        {
            for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
            {
                int index = x + y * grid.Width;
                if (walkable[index] && derivedData.IslandIds[index] == derivedData.MainIslandId)
                    result.Add(new Vector3(
                        grid.Origin.x + (x + 0.5f) * grid.CellSize,
                        grid.Origin.y,
                        grid.Origin.z + (y + 0.5f) * grid.CellSize));
            }
        }

        if (result.Count == 0)
            throw new InvalidOperationException($"ResolveMainIslandCellsInSector failed: sector={sectorId} has no main-island walkable cell.");
        return result;
    }

    private static void RunLv3ResearchCenterCrowdRouteScenario(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3PresetSnapshot preset,
        Lv3CrowdRouteScenario scenario,
        int obstacleIdBase)
    {
        if (scenario.HeroNodes == null || scenario.HeroNodes.Length == 0)
            throw new InvalidOperationException($"RunLv3ResearchCenterCrowdRouteScenario failed: {scenario.Name} has no hero nodes.");
        if (scenario.ChaserCenters == null || scenario.ChaserCenters.Length == 0)
            throw new InvalidOperationException($"RunLv3ResearchCenterCrowdRouteScenario failed: {scenario.Name} has no chaser centers.");

        EntityRegistry.Clear();
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 320;
        SetNavigationWorkQuotas(config, 1_000_000);
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(preset.ResearchCenterPosition);
        Rect researchBounds = researchCenter.CalculateBounds();
        RegisterLv3ResearchCenterFootprintObstacles(researchCenter, obstacleIdBase);
        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), $"真实路径矩阵 {scenario.Name} 必须先提交研发中心 Autobox runtime dirty。");

        const float agentRadius = 0.45f;
        const float edgeClearance = 0.5f;
        const float dt = 0.1f;
        const float heroSpeed = 2.5f;
        const float chaserSpeed = 3.5f;
        Lv3CrowdRouteMetrics metrics = new Lv3CrowdRouteMetrics();
        metrics.Diagnostics.Append("[Lv3CrowdScenario] name=").Append(scenario.Name)
            .Append(" research=").Append(preset.ResearchCenterPosition)
            .Append(" bounds=").Append(researchBounds)
            .AppendLine();

        Vector3 heroStart = ResolveRuntimeLegalLv3MainIslandCell(
            grid,
            derivedData,
            scenario.HeroStart,
            6f,
            agentRadius,
            scenario.Name + "-hero-start",
            metrics.Diagnostics);
        Vector3[] heroWaypoints = new Vector3[scenario.HeroNodes.Length];
        for (int i = 0; i < scenario.HeroNodes.Length; i++)
        {
            heroWaypoints[i] = ResolveRuntimeLegalLv3MainIslandCell(
                grid,
                derivedData,
                scenario.HeroNodes[i].Preferred,
                7f,
                agentRadius,
                scenario.Name + "-hero-node-" + i,
                metrics.Diagnostics);
        }

        Vector3[] chaserStarts = ResolveLv3ScenarioChaserStarts(
            grid,
            derivedData,
            scenario,
            agentRadius,
            metrics.Diagnostics);
        SimEntityContext hero = CreateEntity(heroStart, false, grid.AgentTypeId, agentRadius);
        hero.Side = SideType.PlayerSide;

        SimEntityContext[] chasers = new SimEntityContext[chaserStarts.Length];
        CharacterMoveComp[] moveComps = new CharacterMoveComp[chaserStarts.Length];
        SimMoveExecutor[] executors = new SimMoveExecutor[chaserStarts.Length];
        bool[] agentExpectsLowerRoute = new bool[chaserStarts.Length];
        var allEntities = new System.Collections.Generic.List<IEntityContext> { hero };
        Fix64 chaserSpeedProperty = (Fix64)(chaserSpeed / DistanceUnitConverter.DefaultDistanceConversionRate);
        for (int i = 0; i < chaserStarts.Length; i++)
        {
            SimEntityContext chaser = CreateEntity(chaserStarts[i], false, grid.AgentTypeId, agentRadius);
            chaser.Side = SideType.EnemySide;
            chaser.SetProperty(CreatureMainProperty.Speed, chaserSpeedProperty);
            chasers[i] = chaser;
            agentExpectsLowerRoute[i] = chaserStarts[i].z <= researchBounds.yMax + 0.3f;
            allEntities.Add(chaser);
        }

        for (int i = 0; i < chasers.Length; i++)
        {
            SimEntityContext chaser = chasers[i];
            chaser.TargetComp = new SimTargetingComp(chaser, allEntities)
            {
                CurrentTarget = scenario.UseNaturalTargetAcquisition ? null : hero,
                AggroRangeFixed = (Fix64)40f,
                ForgetRangeFixed = (Fix64)60f
            };
            CharacterMoveComp moveComp = new CharacterMoveComp();
            moveComp.Init(chaser, grid.AgentTypeId);
            chaser.MoveComp = moveComp;
            SimMoveExecutor executor = new SimMoveExecutor
            {
                Position = chaser.Position,
                ApplyNavigationConstraint = true,
                AgentTypeId = grid.AgentTypeId,
                EdgeClearance = edgeClearance
            };
            chaser.MoveExecutor = executor;
            SoldierAIBrain brain = new SoldierAIBrain
            {
                DetectEnemyRange = (Fix64)40f,
                ChaseRange = (Fix64)80f
            };
            brain.SetBirthPositionFixed(chaser.PositionFixed);
            brain.Inject();
            chaser.Brain = brain;
            moveComps[i] = moveComp;
            executors[i] = executor;
            EntityRegistry.Register(chaser);
        }
        EntityRegistry.Register(hero);

        int[] zeroStreak = new int[chasers.Length];
        int[] maxZeroStreak = new int[chasers.Length];
        int waypointIndex = 0;
        int waypointHoldFrames = 0;
        int wallClusterStreak = 0;
        int targetedSlowStreak = 0;
        int overlapPairStreak = 0;
        for (int frame = 1; frame <= scenario.Frames; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
            Vector3 activeWaypoint = heroWaypoints[Mathf.Min(waypointIndex, heroWaypoints.Length - 1)];
            hero.Position = AdvanceHeroWithNavigationConstraint(
                hero.Position,
                activeWaypoint,
                heroSpeed,
                dt,
                grid.AgentTypeId,
                edgeClearance,
                metrics.Diagnostics);
            Vector3 toWaypoint = activeWaypoint - hero.Position;
            toWaypoint.y = 0f;
            if (toWaypoint.sqrMagnitude <= 0.35f * 0.35f && waypointIndex < heroWaypoints.Length - 1)
            {
                int holdFrames = Mathf.Max(0, scenario.HeroNodes[waypointIndex].HoldFrames);
                if (waypointHoldFrames < holdFrames)
                    waypointHoldFrames++;
                else
                {
                    waypointIndex++;
                    waypointHoldFrames = 0;
                }
            }
            hero.SyncPositionToExecutor();

            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            metrics.MaxPendingFlowTiles = Mathf.Max(metrics.MaxPendingFlowTiles, FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount());
            metrics.MaxPendingSharedGoals = Mathf.Max(metrics.MaxPendingSharedGoals, FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount());

            int slowWallAgentsThisFrame = 0;
            int slowTargetedAgentsThisFrame = 0;
            for (int i = 0; i < chasers.Length; i++)
            {
                SimEntityContext chaser = chasers[i];
                SimMoveExecutor executor = executors[i];
                chaser.SyncPositionToExecutor();
                Vector3 decisionPosition = chaser.Position;
                if (chaser.TargetComp is SimTargetingComp targeting)
                    targeting.UpdateTargeting((Fix64)dt);
                if (chaser.Brain is SoldierAIBrain brain)
                    brain.Tick(chaser, (Fix64)dt);
                moveComps[i].Move((Fix64)dt);
                executor.Execute(dt);
                chaser.SyncPositionFromExecutor();

                CollectLv3CrowdRouteSample(
                    scenario,
                    metrics,
                    grid,
                    derivedData,
                    researchCenter,
                    researchBounds,
                    chaser,
                    executor,
                    decisionPosition,
                    hero,
                    hero.Position,
                    frame,
                    i,
                    agentExpectsLowerRoute[i],
                    zeroStreak,
                    maxZeroStreak,
                    ref slowWallAgentsThisFrame,
                    ref slowTargetedAgentsThisFrame);
            }

            int overlapPairsThisFrame = CollectLv3CrowdRouteOverlapSamples(
                scenario,
                metrics,
                grid,
                derivedData,
                chasers,
                hero,
                hero.Position,
                frame,
                agentRadius,
                overlapPairStreak);
            metrics.OverlapPairSamples += overlapPairsThisFrame;
            metrics.MaxOverlapPairsThisFrame = Mathf.Max(metrics.MaxOverlapPairsThisFrame, overlapPairsThisFrame);
            if (overlapPairsThisFrame >= 3)
                overlapPairStreak++;
            else
                overlapPairStreak = 0;
            metrics.MaxOverlapPairStreak = Mathf.Max(metrics.MaxOverlapPairStreak, overlapPairStreak);

            if (slowWallAgentsThisFrame >= 3)
                wallClusterStreak++;
            else
                wallClusterStreak = 0;
            metrics.MaxWallClusterStreak = Mathf.Max(metrics.MaxWallClusterStreak, wallClusterStreak);
            if (slowTargetedAgentsThisFrame >= 3)
                targetedSlowStreak++;
            else
                targetedSlowStreak = 0;
            metrics.MaxTargetedSlowStreak = Mathf.Max(metrics.MaxTargetedSlowStreak, targetedSlowStreak);

            if (waypointIndex >= heroWaypoints.Length - 1)
            {
                for (int i = 0; i < chasers.Length; i++)
                    metrics.MinFinalHeroDistance = Mathf.Min(metrics.MinFinalHeroDistance, HorizontalDistance(chasers[i].Position, hero.Position));
            }

        }

        for (int i = 0; i < maxZeroStreak.Length; i++)
            metrics.MaxZeroStreak = Mathf.Max(metrics.MaxZeroStreak, maxZeroStreak[i]);

        Debug.LogWarning(
            $"[Lv3CrowdScenarioSummary] name={scenario.Name} wallBeforePortal={metrics.WallBeforePortalSamples} " +
            $"stall={metrics.ConstrainedWallStallSamples} stale={metrics.StaleSteeringSamples} nearWallGoal={metrics.NearWallGoalSamples} " +
            $"upperDetour={metrics.UpperDetourSamples} maxWallClusterStreak={metrics.MaxWallClusterStreak} " +
            $"maxZeroStreak={metrics.MaxZeroStreak} maxTargetedSlowStreak={metrics.MaxTargetedSlowStreak} " +
            $"constraintFail={metrics.ConstraintFailureSamples} overlapPairs={metrics.OverlapPairSamples} " +
            $"maxOverlapPairsFrame={metrics.MaxOverlapPairsThisFrame} maxOverlapPairStreak={metrics.MaxOverlapPairStreak} " +
            $"minFinalHeroDist={metrics.MinFinalHeroDistance:F3} " +
            $"pendingFlow={metrics.MaxPendingFlowTiles} pendingShared={metrics.MaxPendingSharedGoals}");

        Assert.LessOrEqual(metrics.WallBeforePortalSamples, 0,
            $"真实路径矩阵 {scenario.Name} 中追兵不应在到达当前 portal 前朝研发中心碰撞体推进。wallBeforePortal={metrics.WallBeforePortalSamples}, minHit={metrics.MinWallHitDistance:F3}, pendingFlow={metrics.MaxPendingFlowTiles}, pendingShared={metrics.MaxPendingSharedGoals}\n{metrics.Diagnostics}");
        Assert.LessOrEqual(metrics.ConstrainedWallStallSamples, 0,
            $"真实路径矩阵 {scenario.Name} 中追兵不应贴研发中心墙侧被导航约束吃掉位移。stall={metrics.ConstrainedWallStallSamples}, maxZeroStreak={metrics.MaxZeroStreak}\n{metrics.Diagnostics}");
        Assert.LessOrEqual(metrics.StaleSteeringSamples, 0,
            $"真实路径矩阵 {scenario.Name} 中 Combat 链路不应长时间停用 steering。stale={metrics.StaleSteeringSamples}\n{metrics.Diagnostics}");
        Assert.LessOrEqual(metrics.NearWallGoalSamples, 0,
            $"真实路径矩阵 {scenario.Name} 中 steering 目标不应落到研发中心碰撞体近距离内。nearWallGoal={metrics.NearWallGoalSamples}\n{metrics.Diagnostics}");
        if (scenario.ExpectLowerRoute)
        {
            Assert.LessOrEqual(metrics.UpperDetourSamples, 0,
                $"真实路径矩阵 {scenario.Name} 中英雄在研发中心下侧/右下时不应大量选择上侧远路。upperDetour={metrics.UpperDetourSamples}\n{metrics.Diagnostics}");
        }
        Assert.LessOrEqual(metrics.MaxWallClusterStreak, 10,
            $"真实路径矩阵 {scenario.Name} 中不应出现多人贴研发中心墙侧长期聚团低速。maxWallClusterStreak={metrics.MaxWallClusterStreak}\n{metrics.Diagnostics}");
        if (scenario.UseNaturalTargetAcquisition)
        {
            Assert.LessOrEqual(metrics.ConstraintFailureSamples, 0,
                $"真实路径矩阵 {scenario.Name} 中已锁定英雄且仍主动移动的单位不应被导航约束直接拒绝位移。constraintFail={metrics.ConstraintFailureSamples}, maxZeroStreak={metrics.MaxZeroStreak}, maxTargetedSlowStreak={metrics.MaxTargetedSlowStreak}\n{metrics.Diagnostics}");
            Assert.LessOrEqual(metrics.MaxZeroStreak, 12,
                $"真实路径矩阵 {scenario.Name} 中锁定英雄后的单位不应长期主动移动但被导航约束压成零位移。maxZeroStreak={metrics.MaxZeroStreak}, maxTargetedSlowStreak={metrics.MaxTargetedSlowStreak}\n{metrics.Diagnostics}");
            Assert.LessOrEqual(metrics.MaxTargetedSlowStreak, 8,
                $"真实路径矩阵 {scenario.Name} 中不应出现多个已锁定英雄的单位持续主动移动但整体低速。maxTargetedSlowStreak={metrics.MaxTargetedSlowStreak}, maxZeroStreak={metrics.MaxZeroStreak}\n{metrics.Diagnostics}");
        }
        Assert.Less(metrics.MinFinalHeroDistance, 5.0f,
            $"真实路径矩阵 {scenario.Name} 必须至少有追兵接近最终英雄位置，否则测试没有覆盖追击链路。minFinalHeroDist={metrics.MinFinalHeroDistance:F3}\n{metrics.Diagnostics}");
    }

    private static void RunLv3CharacterControllerCombatScenario(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset spawnGrid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3PresetSnapshot preset,
        Lv3CrowdRouteScenario scenario,
        int obstacleIdBase)
    {
        System.Diagnostics.Stopwatch scenarioWatch = System.Diagnostics.Stopwatch.StartNew();
        const float scenarioAuthoringDeltaTime = 0.1f;
        const float dt = 1f / 30f;
        int simulationFrames = Mathf.CeilToInt(scenario.Frames * scenarioAuthoringDeltaTime / dt);
        float authoredFrameScale = scenarioAuthoringDeltaTime / dt;
        Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=begin authoredFrames={scenario.Frames} simulationFrames={simulationFrames} dt={dt:F4}");
        if (scenario.HeroNodes == null || scenario.HeroNodes.Length == 0)
            throw new InvalidOperationException($"RunLv3CharacterControllerCombatScenario failed: {scenario.Name} has no hero nodes.");

        EntityRegistry.Clear();
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 320;
        SetNavigationWorkQuotas(config, 1_000_000);
        FlowFieldCrowdMovementSystem.SetConfig(config);
        if (spawnGrid == null)
            throw new InvalidOperationException("RunLv3CharacterControllerCombatScenario failed: spawnGrid is null.");
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            spawnGrid.AgentTypeId,
            spawnGrid.Width,
            spawnGrid.Height,
            spawnGrid.CellSize,
            spawnGrid.Origin,
            spawnGrid.GetWalkableMaskRuntimeReadOnlyReference(),
            spawnGrid.GetCellAnchorsRuntimeReadOnlyReference(),
            spawnGrid.GetCostFieldRuntimeReadOnlyReference(),
            spawnGrid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            spawnGrid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();
        Vector3[] rawChaserStarts = scenario.ExactChaserStarts != null
            ? (Vector3[])scenario.ExactChaserStarts.Clone()
            : ResolveLv3ScenarioRawChaserStartsFromRealSpawnPreview(scenario);

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=source-applied elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");
        ProcessWorldBuildQueueUntilReady();
        Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=world-ready elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");

        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(preset.ResearchCenterPosition);
        if (!scenario.SkipRuntimeObstacleRegistration)
        {
            RegisterLv3ResearchCenterFootprintObstacles(researchCenter, obstacleIdBase);
            for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
                FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
            Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), $"真实 CC Combat 场景 {scenario.Name} 必须先提交研发中心 Autobox runtime dirty。");
        }
        Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=runtime-dirty-ready elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");

        const float agentRadius = 0.18f;
        const float edgeClearance = 0.18f;
        const float heroSpeed = 2.5f;
        const float chaserSpeed = 4.2f;
        System.Text.StringBuilder diagnostics = new System.Text.StringBuilder(4096);
        diagnostics.Append("[Lv3ControllerCombatScenario] name=").Append(scenario.Name)
            .Append(" research=").Append(preset.ResearchCenterPosition)
            .Append(" researchBounds=").Append(preset.ResearchCenterFootprintBounds)
            .Append(" stronghold=").Append(preset.StrongholdSh13Bounds)
            .AppendLine();

        Vector3 heroStart = scenario.PreserveExactPositions
            ? RequireExactLv3TargetPosition(grid, scenario.HeroStart, scenario.Name + "-hero-start", diagnostics)
            : ResolveRuntimeLegalLv3MainIslandCell(
                grid,
                derivedData,
                scenario.HeroStart,
                6f,
                agentRadius,
                scenario.Name + "-hero-start",
                diagnostics);
        Vector3[] heroWaypoints = new Vector3[scenario.HeroNodes.Length];
        for (int i = 0; i < scenario.HeroNodes.Length; i++)
        {
            heroWaypoints[i] = scenario.PreserveExactPositions
                ? RequireExactLv3TargetPosition(grid, scenario.HeroNodes[i].Preferred, scenario.Name + "-hero-node-" + i, diagnostics)
                : ResolveRuntimeLegalLv3MainIslandCell(
                    grid,
                    derivedData,
                    scenario.HeroNodes[i].Preferred,
                    7f,
                    agentRadius,
                    scenario.Name + "-hero-node-" + i,
                    diagnostics);
        }

        Vector3[] chaserStarts;
        if (scenario.PreserveExactPositions)
        {
            chaserStarts = new Vector3[rawChaserStarts.Length];
            for (int i = 0; i < rawChaserStarts.Length; i++)
            {
                chaserStarts[i] = RequireExactRuntimeLegalLv3Position(
                    grid,
                    derivedData,
                    rawChaserStarts[i],
                    agentRadius,
                    scenario.Name + "-chaser-" + i,
                    diagnostics);
            }
        }
        else
        {
            chaserStarts = ResolveLv3ScenarioChaserStartsFromRawPreview(
                grid,
                derivedData,
                scenario,
                rawChaserStarts,
                agentRadius,
                diagnostics);
        }
        Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=points-resolved chasers={chaserStarts.Length} heroNodes={heroWaypoints.Length} elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");

        List<GameObject> createdObjects = new List<GameObject>();
        try
        {
            Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=physics-build-begin elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");
            CreateLv3CharacterControllerTestPhysics(
                grid,
                derivedData,
                researchCenter,
                preset,
                scenario,
                heroStart,
                heroWaypoints,
                chaserStarts,
                createdObjects,
                diagnostics);
            Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=physics-build-end createdObjects={createdObjects.Count} elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");

            SimEntityContext hero = CreateEntity(heroStart, false, grid.AgentTypeId, agentRadius);
            hero.Side = SideType.PlayerSide;
            hero.SetProperty(CreatureMainProperty.CollisionRadius, (Fix64)(agentRadius / DistanceUnitConverter.DefaultDistanceConversionRate));
            int characterLayer = ResolveRequiredLayer("character");
            GameObject heroCollision = new GameObject("FlowTest_CC_Combat_HeroCollider");
            heroCollision.layer = characterLayer;
            heroCollision.transform.position = heroStart;
            CapsuleCollider heroCollider = heroCollision.AddComponent<CapsuleCollider>();
            heroCollider.radius = agentRadius;
            heroCollider.height = 2f;
            heroCollider.center = new Vector3(0f, 1f, 0f);
            heroCollision.transform.position = ResolveLv3GroundedColliderPosition(heroStart, characterLayer);
            createdObjects.Add(heroCollision);

            SimEntityContext[] chasers = new SimEntityContext[chaserStarts.Length];
            CharacterMoveComp[] moveComps = new CharacterMoveComp[chaserStarts.Length];
            SimMoveExecutor[] executors = new SimMoveExecutor[chaserStarts.Length];
            Transform[] transforms = new Transform[chaserStarts.Length];
            CharacterController[] controllers = new CharacterController[chaserStarts.Length];
            CharacterController sourceChaserController = LoadRequiredCharacterControllerFromPrefab(
                "Assets/AAAGame/Prefabs/Entity/Soldier/背锅侠.prefab",
                scenario.Name);
            List<IEntityContext> allEntities = new List<IEntityContext>(chaserStarts.Length + 1) { hero };
            Fix64 speedProperty = (Fix64)(chaserSpeed / DistanceUnitConverter.DefaultDistanceConversionRate);
            Fix64 radiusProperty = (Fix64)(agentRadius / DistanceUnitConverter.DefaultDistanceConversionRate);
            for (int i = 0; i < chaserStarts.Length; i++)
            {
                SimEntityContext chaser = CreateEntity(chaserStarts[i], false, grid.AgentTypeId, agentRadius);
                chaser.Side = SideType.EnemySide;
                chaser.SetProperty(CreatureMainProperty.Speed, speedProperty);
                chaser.SetProperty(CreatureMainProperty.CollisionRadius, radiusProperty);
                chasers[i] = chaser;
                allEntities.Add(chaser);
            }

            for (int i = 0; i < chasers.Length; i++)
            {
                SimEntityContext chaser = chasers[i];
                chaser.TargetComp = new SimTargetingComp(chaser, allEntities)
                {
                    CurrentTarget = scenario.UseNaturalTargetAcquisition ? null : hero,
                    AggroRangeFixed = (Fix64)40f,
                    ForgetRangeFixed = (Fix64)60f
                };
                CharacterMoveComp moveComp = new CharacterMoveComp();
                moveComp.Init(chaser, grid.AgentTypeId);
                chaser.MoveComp = moveComp;
                SoldierAIBrain brain = new SoldierAIBrain
                {
                    DetectEnemyRange = (Fix64)40f,
                    ChaseRange = (Fix64)80f
                };
                brain.SetBirthPositionFixed(chaser.PositionFixed);
                brain.Inject();
                chaser.Brain = brain;

                GameObject go = new GameObject("FlowTest_CC_Combat_Chaser_" + i);
                go.layer = characterLayer;
                go.transform.position = chaser.Position;
                CharacterController controller = go.AddComponent<CharacterController>();
                CopyCharacterControllerSettings(sourceChaserController, controller);
                var executor = new SimMoveExecutor
                {
                    Position = chaser.Position,
                    AgentTypeId = grid.AgentTypeId,
                };
                chaser.MoveExecutor = executor;
                controllers[i] = controller;
                transforms[i] = go.transform;
                moveComps[i] = moveComp;
                executors[i] = executor;
                createdObjects.Add(go);
                EntityRegistry.Register(chaser);
            }
            EntityRegistry.Register(hero);
            Physics.SyncTransforms();
            Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=agents-ready chasers={chasers.Length} elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");

            int waypointIndex = 0;
            int waypointHoldFrames = 0;
            int[] stallStreak = new int[chasers.Length];
            int[] maxStallStreak = new int[chasers.Length];
            int[] reverseSteeringStreak = new int[chasers.Length];
            int[] maxReverseSteeringStreak = new int[chasers.Length];
            System.Text.StringBuilder[] currentReverseSteeringTimelines = new System.Text.StringBuilder[chasers.Length];
            string[] maxReverseSteeringTimelines = new string[chasers.Length];
            for (int i = 0; i < chasers.Length; i++)
                currentReverseSteeringTimelines[i] = new System.Text.StringBuilder(4096);
            int[] steeringCollapseStreak = new int[chasers.Length];
            int[] maxSteeringCollapseStreak = new int[chasers.Length];
            int stallSamples = 0;
            int wallStallSamples = 0;
            int steeringCollapseSamples = 0;
            int crowdWallStreak = 0;
            int maxCrowdWallStreak = 0;
            int targetedSlowStreak = 0;
            int maxTargetedSlowStreak = 0;
            int overlapPairSamples = 0;
            int maxOverlapPairsThisFrame = 0;
            int overlapPairStreak = 0;
            int maxOverlapPairStreak = 0;
            int[,] overlapPairStreaks = new int[chasers.Length, chasers.Length];
            int maxPersistentPairOverlapStreak = 0;
            int detailedSamples = 0;
            float minHeroDistance = float.PositiveInfinity;
            Vector3[] frameStartPositions = new Vector3[chasers.Length];
            for (int frame = 1; frame <= simulationFrames; frame++)
            {
                if (scenarioWatch.Elapsed.TotalSeconds > 90d)
                    throw new TimeoutException($"Lv3 controller combat scenario timed out: scenario={scenario.Name}, frame={frame}, elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}.");
                bool logFrame = frame == 1 || frame % 60 == 0 || frame == simulationFrames;
                if (logFrame)
                {
                    Debug.Log(
                        $"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=frame-begin frame={frame} " +
                        $"pendingFlow={FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount()} " +
                        $"pendingShared={FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount()} " +
                        $"elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");
                }
                FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
                Vector3 activeWaypoint = heroWaypoints[Mathf.Min(waypointIndex, heroWaypoints.Length - 1)];
                hero.Position = AdvanceHeroWithNavigationConstraint(
                    hero.Position,
                    activeWaypoint,
                    heroSpeed,
                    dt,
                    grid.AgentTypeId,
                    edgeClearance,
                    diagnostics);
                heroCollision.transform.position = ResolveLv3GroundedColliderPosition(hero.Position, characterLayer);
                Vector3 toWaypoint = activeWaypoint - hero.Position;
                toWaypoint.y = 0f;
                if (toWaypoint.sqrMagnitude <= 0.35f * 0.35f && waypointIndex < heroWaypoints.Length - 1)
                {
                    int holdFrames = Mathf.Max(0, Mathf.RoundToInt(scenario.HeroNodes[waypointIndex].HoldFrames * authoredFrameScale));
                    if (waypointHoldFrames < holdFrames)
                        waypointHoldFrames++;
                    else
                    {
                        waypointIndex++;
                        waypointHoldFrames = 0;
                    }
                }

                FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
                int slowTargetedThisFrame = 0;
                int slowWallThisFrame = 0;
                for (int i = 0; i < chasers.Length; i++)
                {
                    SimEntityContext chaser = chasers[i];
                    SimMoveExecutor executor = executors[i];
                    chaser.Position = transforms[i].position;
                    Vector3 before = chaser.Position;
                    frameStartPositions[i] = before;
                    if (chaser.TargetComp is SimTargetingComp targeting)
                        targeting.UpdateTargeting((Fix64)dt);
                    if (chaser.Brain is SoldierAIBrain brain)
                    {
                        try
                        {
                            brain.Tick(chaser, (Fix64)dt);
                        }
                        catch (Exception ex)
                        {
                            System.Text.StringBuilder failureDiagnostics = new System.Text.StringBuilder(4096);
                            AppendControllerCombatTickFailureDiagnostics(
                                failureDiagnostics,
                                scenario,
                                frame,
                                i,
                                chaser,
                                hero,
                                grid,
                                derivedData,
                                before,
                                hero.Position,
                                waypointIndex,
                                heroWaypoints.Length,
                                researchCenter);
                            Debug.LogError(failureDiagnostics.ToString());
                            throw new InvalidOperationException(
                                $"Lv3 controller combat tick failed scenario={scenario.Name} frame={frame} agent={i}: {ex.Message}\n{failureDiagnostics}",
                                ex);
                        }
                    }
                    moveComps[i].Move((Fix64)dt);
                    Vector3 requestedVelocity = executor.LastInputVelocity;
                    Vector3 after;
                    if (executor.HasFixedInput)
                    {
                        FixVector2 startFixed = new FixVector2((Fix64)before.x, (Fix64)before.z);
                        FixVector2 proposedFixed = startFixed + executor.LastFixedInput * (Fix64)dt;
                        after = new Vector3((float)proposedFixed.x, before.y, (float)proposedFixed.y);
                    }
                    else
                    {
                        after = before + new Vector3(requestedVelocity.x, 0f, requestedVelocity.z) * dt;
                    }
                    transforms[i].position = after;
                    chaser.Position = after;
                    FlowFieldCrowdMovementSystem.UpdateAgentForEditorTest(chaser, agentRadius, grid.AgentTypeId);

                    Vector3 actual = after - before;
                    actual.y = 0f;
                    Vector3 requestedHorizontalDisplacement = actual;
                    Vector3 constrainedHorizontalDisplacement = actual;
                    float heroDistance = HorizontalDistance(after, hero.Position);
                    float researchDistance = researchCenter.DistanceToClosestBox(after);
                    bool hasHeroTarget = chaser.TargetComp is SimTargetingComp targetingComp && ReferenceEquals(targetingComp.CurrentTarget, hero);
                    bool activeButStopped = hasHeroTarget
                                            && heroDistance > 1.25f
                                            && requestedHorizontalDisplacement.magnitude > chaserSpeed * dt * 0.35f
                                            && actual.magnitude < chaserSpeed * dt * 0.12f;
                    bool hasSteeringDiagnostic = FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringDiagnostic(
                        chaser.LogicEntityId.Value,
                        out Vector3 desiredVelocity,
                        out _,
                        out _,
                        out Vector3 resultVelocity);
                    if (executor.HasFixedInput)
                    {
                        resultVelocity = new Vector3(
                            (float)executor.LastFixedInput.x,
                            0f,
                            (float)executor.LastFixedInput.y);
                        desiredVelocity = resultVelocity;
                    }
                    bool steeringCollapsed = hasHeroTarget
                                             && heroDistance > 1.25f
                                             && hasSteeringDiagnostic
                                             && desiredVelocity.magnitude > chaserSpeed * 0.65f
                                             && resultVelocity.magnitude < chaserSpeed * 0.18f;
                    bool reverseSteering = false;
                    if (hasHeroTarget
                        && heroDistance > 1.25f
                        && hasSteeringDiagnostic
                        && desiredVelocity.sqrMagnitude > chaserSpeed * chaserSpeed * 0.25f
                        && resultVelocity.sqrMagnitude > 0.01f)
                    {
                        reverseSteering = Vector3.Dot(desiredVelocity.normalized, resultVelocity.normalized) < -0.15f;
                    }
                    reverseSteeringStreak[i] = reverseSteering ? reverseSteeringStreak[i] + 1 : 0;
                    if (reverseSteering)
                    {
                        System.Text.StringBuilder timeline = currentReverseSteeringTimelines[i];
                        if (reverseSteeringStreak[i] == 1)
                            timeline.Clear();
                        timeline.Append("[Lv3ReverseTimeline] frame=").Append(frame)
                            .Append(" agent=").Append(i)
                            .Append(" streak=").Append(reverseSteeringStreak[i])
                            .Append(" position=").Append(after)
                            .Append(" actual=").Append(actual)
                            .Append(" heroDist=").Append(heroDistance.ToString("F3"));
                        AppendSteeringBreakdown(timeline, chaser);
                        timeline.AppendLine();
                        if (reverseSteeringStreak[i] > maxReverseSteeringStreak[i])
                        {
                            maxReverseSteeringStreak[i] = reverseSteeringStreak[i];
                            maxReverseSteeringTimelines[i] = timeline.ToString();
                        }
                    }
                    if (reverseSteeringStreak[i] == 2 && detailedSamples < 32)
                    {
                        detailedSamples++;
                        diagnostics.Append("[Lv3ControllerCombatReverse] scenario=").Append(scenario.Name)
                            .Append(" frame=").Append(frame)
                            .Append(" agent=").Append(i)
                            .Append(" position=").Append(after)
                            .Append(" hero=").Append(hero.Position)
                            .Append(" heroDist=").Append(heroDistance.ToString("F3"));
                        AppendCellDiagnostic(diagnostics, grid, derivedData, after, "reverse");
                        AppendSteeringBreakdown(diagnostics, chaser);
                        AppendPathHandleDiagnostics(diagnostics, chaser, grid, derivedData, hero.Position);
                        diagnostics.AppendLine();
                    }
                    steeringCollapseStreak[i] = steeringCollapsed ? steeringCollapseStreak[i] + 1 : 0;
                    maxSteeringCollapseStreak[i] = Mathf.Max(maxSteeringCollapseStreak[i], steeringCollapseStreak[i]);
                    if (steeringCollapsed)
                    {
                        steeringCollapseSamples++;
                        if (steeringCollapseStreak[i] == 2 && detailedSamples < 32)
                        {
                            detailedSamples++;
                            diagnostics.Append("[Lv3ControllerCombatSteeringCollapse] scenario=").Append(scenario.Name)
                                .Append(" frame=").Append(frame)
                                .Append(" agent=").Append(i)
                                .Append(" position=").Append(after)
                                .Append(" hero=").Append(hero.Position)
                                .Append(" heroDist=").Append(heroDistance.ToString("F3"))
                                .Append(" desiredVelocity=").Append(desiredVelocity)
                                .Append(" resultVelocity=").Append(resultVelocity)
                                .Append(" streak=").Append(steeringCollapseStreak[i]);
                            AppendCellDiagnostic(diagnostics, grid, derivedData, after, "steering-collapse");
                            AppendSteeringBreakdown(diagnostics, chaser);
                            AppendPathHandleDiagnostics(diagnostics, chaser, grid, derivedData, hero.Position);
                            diagnostics.AppendLine();
                        }
                    }
                    if (activeButStopped || steeringCollapsed)
                        slowTargetedThisFrame++;
                    stallStreak[i] = activeButStopped ? stallStreak[i] + 1 : 0;
                    maxStallStreak[i] = Mathf.Max(maxStallStreak[i], stallStreak[i]);
                    if (activeButStopped)
                    {
                        stallSamples++;
                        if (researchDistance <= 1.05f)
                        {
                            wallStallSamples++;
                            slowWallThisFrame++;
                        }

                        if (detailedSamples < 32)
                        {
                            detailedSamples++;
                            diagnostics.Append("[Lv3ControllerCombatStall] scenario=").Append(scenario.Name)
                                .Append(" frame=").Append(frame)
                                .Append(" agent=").Append(i)
                                .Append(" before=").Append(before)
                                .Append(" after=").Append(after)
                                .Append(" hero=").Append(hero.Position)
                                .Append(" requestedVelocity=").Append(requestedVelocity)
                                .Append(" requestedDisp=").Append(requestedHorizontalDisplacement)
                                .Append(" constrainedDisp=").Append(constrainedHorizontalDisplacement)
                                .Append(" constraintEnabled=").Append(executor.ApplyNavigationConstraint)
                                .Append(" actual=").Append(actual)
                                .Append(" heroDist=").Append(heroDistance.ToString("F3"))
                                .Append(" researchDist=").Append(researchDistance.ToString("F3"))
                                .Append(" stallStreak=").Append(stallStreak[i]);
                            AppendCellDiagnostic(diagnostics, grid, derivedData, before, "before");
                            AppendCellDiagnostic(diagnostics, grid, derivedData, after, "after");
                            AppendCharacterControllerPhysicsDiagnostics(diagnostics, controllers[i], null);
                            AppendSteeringBreakdown(diagnostics, chaser);
                            AppendControllerCandidateProbeDiagnostics(diagnostics, controllers[i], chaser, dt);
                            AppendPathHandleDiagnostics(diagnostics, chaser, grid, derivedData, hero.Position);
                            diagnostics.AppendLine();
                        }
                    }

                    minHeroDistance = Mathf.Min(minHeroDistance, heroDistance);
                }

                ResolveLv3DeterministicAgentCollisions(
                    chasers,
                    transforms,
                    frameStartPositions,
                    agentRadius,
                    grid.AgentTypeId);
                if (scenario.PreserveExactPositions && frame % 60 == 0)
                {
                    for (int i = 0; i < chasers.Length; i++)
                    {
                        diagnostics.Append("[Lv3FixedProgress] scenario=").Append(scenario.Name)
                            .Append(" frame=").Append(frame)
                            .Append(" agent=").Append(i)
                            .Append(" position=").Append(transforms[i].position)
                            .Append(" hero=").Append(hero.Position)
                            .Append(" heroDist=").Append(HorizontalDistance(transforms[i].position, hero.Position).ToString("F3"));
                        AppendSteeringBreakdown(diagnostics, chasers[i]);
                        AppendPathHandleDiagnostics(diagnostics, chasers[i], grid, derivedData, hero.Position);
                        diagnostics.AppendLine();
                    }
                }

                int overlapPairsThisFrame = CollectLv3ControllerOverlapSamples(
                    diagnostics,
                    grid,
                    derivedData,
                    transforms,
                    hero.Position,
                    frame,
                    agentRadius,
                    overlapPairStreak,
                    overlapPairSamples,
                    chasers,
                    hero);
                overlapPairSamples += overlapPairsThisFrame;
                maxOverlapPairsThisFrame = Mathf.Max(maxOverlapPairsThisFrame, overlapPairsThisFrame);
                int persistentPairStreak = UpdatePersistentOverlapPairStreaks(
                    transforms,
                    agentRadius,
                    overlapPairStreaks,
                    out int persistentPairA,
                    out int persistentPairB,
                    out float persistentPairDistance);
                if (persistentPairStreak > maxPersistentPairOverlapStreak)
                {
                    maxPersistentPairOverlapStreak = persistentPairStreak;
                    if (persistentPairStreak >= 3)
                    {
                        diagnostics.Append("[Lv3PersistentOverlap] scenario=").Append(scenario.Name)
                            .Append(" frame=").Append(frame)
                            .Append(" pair=").Append(persistentPairA).Append('/').Append(persistentPairB)
                            .Append(" streak=").Append(persistentPairStreak)
                            .Append(" distance=").Append(persistentPairDistance.ToString("F3"))
                            .Append(" a=").Append(transforms[persistentPairA].position)
                            .Append(" b=").Append(transforms[persistentPairB].position)
                            .Append(" hero=").Append(hero.Position);
                        AppendSteeringBreakdown(diagnostics, chasers[persistentPairA]);
                        AppendSteeringBreakdown(diagnostics, chasers[persistentPairB]);
                        diagnostics.AppendLine();
                    }
                }
                if (overlapPairsThisFrame >= 3)
                    overlapPairStreak++;
                else
                    overlapPairStreak = 0;
                maxOverlapPairStreak = Mathf.Max(maxOverlapPairStreak, overlapPairStreak);

                if (slowWallThisFrame >= 3)
                    crowdWallStreak++;
                else
                    crowdWallStreak = 0;
                maxCrowdWallStreak = Mathf.Max(maxCrowdWallStreak, crowdWallStreak);
                if (slowTargetedThisFrame >= 3)
                    targetedSlowStreak++;
                else
                    targetedSlowStreak = 0;
                maxTargetedSlowStreak = Mathf.Max(maxTargetedSlowStreak, targetedSlowStreak);
                if (logFrame)
                {
                    Debug.Log(
                        $"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=frame-end frame={frame} " +
                        $"waypoint={waypointIndex}/{heroWaypoints.Length - 1} slowWall={slowWallThisFrame} slowTargeted={slowTargetedThisFrame} " +
                        $"overlapPairs={overlapPairsThisFrame} minHeroDist={minHeroDistance:F3} " +
                        $"pendingFlow={FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount()} " +
                        $"pendingShared={FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount()} " +
                        $"elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");
                }
            }

            int maxSingleStallStreak = 0;
            int maxSingleReverseSteeringStreak = 0;
            int maxReverseSteeringAgent = -1;
            int maxSingleSteeringCollapseStreak = 0;
            for (int i = 0; i < maxStallStreak.Length; i++)
            {
                maxSingleStallStreak = Mathf.Max(maxSingleStallStreak, maxStallStreak[i]);
                if (maxReverseSteeringStreak[i] > maxSingleReverseSteeringStreak)
                {
                    maxSingleReverseSteeringStreak = maxReverseSteeringStreak[i];
                    maxReverseSteeringAgent = i;
                }
                maxSingleSteeringCollapseStreak = Mathf.Max(maxSingleSteeringCollapseStreak, maxSteeringCollapseStreak[i]);
            }
            if (maxReverseSteeringAgent >= 0 && !string.IsNullOrEmpty(maxReverseSteeringTimelines[maxReverseSteeringAgent]))
            {
                diagnostics.Append("[Lv3ReverseTimelineSummary] agent=").Append(maxReverseSteeringAgent)
                    .Append(" maxStreak=").Append(maxSingleReverseSteeringStreak).AppendLine();
                diagnostics.Append(maxReverseSteeringTimelines[maxReverseSteeringAgent]);
            }

            Debug.LogWarning(
                $"[Lv3ControllerCombatScenarioSummary] name={scenario.Name} stall={stallSamples} wallStall={wallStallSamples} steeringCollapse={steeringCollapseSamples} " +
                $"maxSingleStallStreak={maxSingleStallStreak} maxCrowdWallStreak={maxCrowdWallStreak} " +
                $"maxTargetedSlowStreak={maxTargetedSlowStreak} maxReverseSteeringStreak={maxSingleReverseSteeringStreak} maxSteeringCollapseStreak={maxSingleSteeringCollapseStreak} overlapPairs={overlapPairSamples} " +
                $"maxOverlapPairsFrame={maxOverlapPairsThisFrame} maxOverlapPairStreak={maxOverlapPairStreak} " +
                $"maxPersistentPairOverlapStreak={maxPersistentPairOverlapStreak} minHeroDist={minHeroDistance:F3}");

            int reverseSteeringStreakLimit = Mathf.CeilToInt(3f * authoredFrameScale);
            int steeringCollapseStreakLimit = Mathf.CeilToInt(4f * authoredFrameScale);
            int singleStallStreakLimit = Mathf.CeilToInt(8f * authoredFrameScale);
            int crowdWallStreakLimit = Mathf.CeilToInt(4f * authoredFrameScale);
            int targetedSlowStreakLimit = Mathf.CeilToInt(6f * authoredFrameScale);
            Assert.LessOrEqual(maxSingleReverseSteeringStreak, reverseSteeringStreakLimit,
                $"真实 CC Combat 场景 {scenario.Name} 中避让不能连续反转主路径方向。maxReverseSteeringStreak={maxSingleReverseSteeringStreak}\n{diagnostics}");
            Assert.LessOrEqual(maxSingleSteeringCollapseStreak, steeringCollapseStreakLimit,
                $"真实 CC Combat 场景 {scenario.Name} 中满速路径期望不能被 steering 连续压成蠕动速度。maxSteeringCollapseStreak={maxSingleSteeringCollapseStreak}, samples={steeringCollapseSamples}\n{diagnostics}");
            Assert.LessOrEqual(maxSingleStallStreak, singleStallStreakLimit,
                $"真实 CC Combat 场景 {scenario.Name} 不应出现单兵长期主动追击但被建筑/边界卡住。maxStall={maxSingleStallStreak}, stall={stallSamples}, wallStall={wallStallSamples}\n{diagnostics}");
            Assert.LessOrEqual(maxCrowdWallStreak, crowdWallStreakLimit,
                $"真实 CC Combat 场景 {scenario.Name} 不应出现多人连续贴建筑或地形边界挤住。maxCrowdWallStreak={maxCrowdWallStreak}, wallStall={wallStallSamples}\n{diagnostics}");
            Assert.LessOrEqual(maxTargetedSlowStreak, targetedSlowStreakLimit,
                $"真实 CC Combat 场景 {scenario.Name} 不应出现多个已锁定英雄的单位持续低速蠕动。maxTargetedSlowStreak={maxTargetedSlowStreak}, stall={stallSamples}\n{diagnostics}");
            Assert.LessOrEqual(maxOverlapPairsThisFrame, 6,
                $"真实 CC Combat 场景 {scenario.Name} 不应在单帧出现大面积几何重叠。overlapPairs={overlapPairSamples}, maxFrame={maxOverlapPairsThisFrame}, maxStreak={maxOverlapPairStreak}\n{diagnostics}");
            Assert.LessOrEqual(maxOverlapPairStreak, 12,
                $"真实 CC Combat 场景 {scenario.Name} 不应持续出现多人几何重叠聚团。overlapPairs={overlapPairSamples}, maxFrame={maxOverlapPairsThisFrame}, maxStreak={maxOverlapPairStreak}\n{diagnostics}");
            Assert.LessOrEqual(maxPersistentPairOverlapStreak, 8,
                $"真实 CC Combat 场景 {scenario.Name} 中同一对单位不应持续几何穿透。overlapPairs={overlapPairSamples}, maxFrame={maxOverlapPairsThisFrame}, maxPairStreak={maxPersistentPairOverlapStreak}\n{diagnostics}");
            Assert.LessOrEqual(overlapPairSamples, Mathf.CeilToInt(simulationFrames * 0.2f),
                $"真实 CC Combat 场景 {scenario.Name} 不应靠不断更换相邻对象来掩盖持续聚团。overlapPairs={overlapPairSamples}, frames={simulationFrames}, maxFrame={maxOverlapPairsThisFrame}, maxPairStreak={maxPersistentPairOverlapStreak}\n{diagnostics}");
            Assert.Less(minHeroDistance, 5f,
                $"真实 CC Combat 场景 {scenario.Name} 必须实际接近英雄，否则测试未覆盖接敌链路。minHeroDist={minHeroDistance:F3}\n{diagnostics}");
        }
        finally
        {
            for (int i = 0; i < createdObjects.Count; i++)
            {
                if (createdObjects[i] != null)
                    UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }
        }
    }

    private static void CreateLv3CharacterControllerTestPhysics(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3ResearchCenterFootprint researchCenter,
        Lv3PresetSnapshot preset,
        Lv3CrowdRouteScenario scenario,
        Vector3 heroStart,
        Vector3[] heroWaypoints,
        Vector3[] chaserStarts,
        List<GameObject> createdObjects,
        System.Text.StringBuilder diagnostics)
    {
        int groundLayer = ResolveRequiredLayer("Ground");
        int characterLayer = ResolveRequiredLayer("character");
        const string terrainPrefabPath = "Assets/AAAGame/Tilemap/Lv3.prefab";
        GameObject terrainPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(terrainPrefabPath);
        if (terrainPrefab == null)
            throw new InvalidOperationException($"CreateLv3CharacterControllerTestPhysics failed: cannot load {terrainPrefabPath}.");

        GameObject terrainRoot = new GameObject("FlowTest_CC_Combat_Lv3TerrainRoot");
        terrainRoot.SetActive(false);
        GameObject terrainInstance = UnityEngine.Object.Instantiate(terrainPrefab, terrainRoot.transform, false);
        terrainInstance.name = "FlowTest_CC_Combat_Lv3Terrain";
        terrainInstance.transform.localPosition = new Vector3(0f, -3.6f, 0f);
        terrainInstance.transform.localRotation = Quaternion.identity;
        terrainInstance.transform.localScale = Vector3.one;
        foreach (MonoBehaviour behaviour in terrainInstance.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null)
                behaviour.enabled = false;
        }

        foreach (Renderer terrainRenderer in terrainInstance.GetComponentsInChildren<Renderer>(true))
            terrainRenderer.enabled = false;

        Collider[] terrainColliders = terrainInstance.GetComponentsInChildren<Collider>(true);
        int characterCollisionColliderCount = 0;
        for (int i = 0; i < terrainColliders.Length; i++)
        {
            Collider terrainCollider = terrainColliders[i];
            if (terrainCollider.enabled && !Physics.GetIgnoreLayerCollision(characterLayer, terrainCollider.gameObject.layer))
                characterCollisionColliderCount++;
        }

        terrainRoot.SetActive(true);
        createdObjects.Add(terrainRoot);

        for (int i = 0; i < researchCenter.Boxes.Length; i++)
        {
            Rect box = researchCenter.Boxes[i];
            GameObject obstacle = new GameObject("FlowTest_CC_Combat_ResearchCenter_" + i);
            obstacle.layer = groundLayer;
            obstacle.transform.position = new Vector3(box.center.x, 1f, box.center.y);
            BoxCollider collider = obstacle.AddComponent<BoxCollider>();
            collider.size = new Vector3(box.width, 2f, box.height);
            createdObjects.Add(obstacle);
        }

        diagnostics.Append("[Lv3ControllerCombatPhysics] scenario=").Append(scenario.Name)
            .Append(" terrainPrefab=").Append(terrainPrefabPath)
            .Append(" terrainColliders=").Append(terrainColliders.Length)
            .Append(" characterCollisionColliders=").Append(characterCollisionColliderCount)
            .Append(" terrainOffset=").Append(terrainInstance.transform.localPosition)
            .AppendLine();
        Physics.SyncTransforms();
    }

    private static CharacterController LoadRequiredCharacterControllerFromPrefab(string prefabPath, string scenarioName)
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            throw new InvalidOperationException($"LoadRequiredCharacterControllerFromPrefab failed: scenario={scenarioName}, prefab={prefabPath} cannot be loaded.");

        CharacterController controller = prefab.GetComponent<CharacterController>();
        if (controller == null)
            throw new InvalidOperationException($"LoadRequiredCharacterControllerFromPrefab failed: scenario={scenarioName}, prefab={prefabPath} has no CharacterController.");
        return controller;
    }

    private static void CopyCharacterControllerSettings(CharacterController source, CharacterController target)
    {
        if (source == null)
            throw new InvalidOperationException("CopyCharacterControllerSettings failed: source is null.");
        if (target == null)
            throw new InvalidOperationException("CopyCharacterControllerSettings failed: target is null.");

        target.center = source.center;
        target.radius = source.radius;
        target.height = source.height;
        target.slopeLimit = source.slopeLimit;
        target.stepOffset = source.stepOffset;
        target.skinWidth = source.skinWidth;
        target.minMoveDistance = source.minMoveDistance;
    }

    private static void ResolveLv3DeterministicAgentCollisions(
        SimEntityContext[] chasers,
        Transform[] transforms,
        Vector3[] frameStartPositions,
        float agentRadius,
        int agentTypeId)
    {
        if (chasers == null || transforms == null || frameStartPositions == null
            || chasers.Length != transforms.Length || chasers.Length != frameStartPositions.Length)
        {
            throw new InvalidOperationException("Lv3 deterministic collision input arrays are invalid.");
        }

        var bodies = new List<LogicAgentCollisionBody>(chasers.Length);
        var indexByEntityId = new Dictionary<int, int>(chasers.Length);
        var workingPositions = new FixVector2[chasers.Length];
        for (int i = 0; i < chasers.Length; i++)
        {
            SimEntityContext chaser = chasers[i];
            if (chaser == null || transforms[i] == null)
                throw new InvalidOperationException($"Lv3 deterministic collision entity is missing at index {i}.");
            if (!indexByEntityId.TryAdd(chaser.LogicEntityId.Value, i))
                throw new InvalidOperationException($"Lv3 deterministic collision duplicate entity id {chaser.LogicEntityId.Value}.");

            Vector3 proposed = transforms[i].position;
            workingPositions[i] = new FixVector2((Fix64)proposed.x, (Fix64)proposed.z);
        }

        for (int pass = 0; pass < 4; pass++)
        {
            bodies.Clear();
            for (int i = 0; i < chasers.Length; i++)
            {
                bodies.Add(new LogicAgentCollisionBody(
                    chasers[i].LogicEntityId,
                    workingPositions[i],
                    (Fix64)agentRadius,
                    Fix64.One,
                    1u,
                    1u));
            }

            LogicAgentCollisionSolveResult pairResult = DeterministicAgentCollisionSolver.Solve(
                bodies,
                8,
                Fix64.FromRaw(1));
            for (int stateIndex = 0; stateIndex < pairResult.States.Count; stateIndex++)
            {
                LogicAgentCollisionState pairState = pairResult.States[stateIndex];
                if (!indexByEntityId.TryGetValue(pairState.EntityId.Value, out int entityIndex))
                    throw new InvalidOperationException($"Lv3 deterministic collision returned unknown entity {pairState.EntityId.Value}.");

                FixVector2 frameStart = new FixVector2(
                    (Fix64)frameStartPositions[entityIndex].x,
                    (Fix64)frameStartPositions[entityIndex].z);
                if (!LogicStaticCollisionShadowService.TrySolveFixed(
                        agentTypeId,
                        frameStart,
                        pairState.Position - frameStart,
                        (Fix64)agentRadius,
                        out LogicStaticCollisionShadowResult staticResult))
                {
                    throw new InvalidOperationException(
                        $"Lv3 deterministic collision has no static world for entity {pairState.EntityId.Value}, agentType={agentTypeId}.");
                }
                if (!staticResult.SolveResult.Success)
                {
                    throw new InvalidOperationException(
                        $"Lv3 deterministic collision static projection failed for entity {pairState.EntityId.Value}: {staticResult.SolveResult.Failure}.");
                }

                workingPositions[entityIndex] =
                    staticResult.SolveResult.Start + staticResult.SolveResult.ResolvedDisplacement;
            }
        }

        for (int entityIndex = 0; entityIndex < chasers.Length; entityIndex++)
        {
            FixVector2 resolved = workingPositions[entityIndex];
            Transform targetTransform = transforms[entityIndex];
            targetTransform.position = new Vector3(
                (float)resolved.x,
                targetTransform.position.y,
                (float)resolved.y);
            chasers[entityIndex].Position = targetTransform.position;
            FlowFieldCrowdMovementSystem.UpdateAgentForEditorTest(chasers[entityIndex], agentRadius, agentTypeId);
        }
        Physics.SyncTransforms();
    }

    private static Vector3 ResolveLv3GroundedColliderPosition(Vector3 navigationPosition, int characterLayer)
    {
        Vector3 rayOrigin = new Vector3(navigationPosition.x, navigationPosition.y + 20f, navigationPosition.z);
        int layerMask = ~(1 << characterLayer);
        RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, 50f, layerMask, QueryTriggerInteraction.Ignore);
        float bestGroundY = float.NegativeInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.normal.y < 0.5f || hit.point.y > navigationPosition.y + 0.5f)
                continue;
            if (hit.point.y > bestGroundY)
                bestGroundY = hit.point.y;
        }

        if (float.IsNegativeInfinity(bestGroundY))
        {
            throw new InvalidOperationException(
                $"ResolveLv3GroundedColliderPosition failed: no walkable ground below navigationPosition={navigationPosition}.");
        }

        return new Vector3(navigationPosition.x, bestGroundY, navigationPosition.z);
    }

    private static Rect BuildLv3ControllerCombatPhysicsRegion(
        Lv3PresetSnapshot preset,
        Lv3CrowdRouteScenario scenario,
        Vector3 heroStart,
        Vector3[] heroWaypoints,
        Vector3[] chaserStarts,
        float margin)
    {
        float minX = Mathf.Min(preset.ResearchCenterFootprintBounds.xMin, preset.StrongholdSh13Bounds.xMin);
        float minZ = Mathf.Min(preset.ResearchCenterFootprintBounds.yMin, preset.StrongholdSh13Bounds.yMin);
        float maxX = Mathf.Max(preset.ResearchCenterFootprintBounds.xMax, preset.StrongholdSh13Bounds.xMax);
        float maxZ = Mathf.Max(preset.ResearchCenterFootprintBounds.yMax, preset.StrongholdSh13Bounds.yMax);
        IncludePointInBounds(heroStart, ref minX, ref minZ, ref maxX, ref maxZ);
        for (int i = 0; i < heroWaypoints.Length; i++)
            IncludePointInBounds(heroWaypoints[i], ref minX, ref minZ, ref maxX, ref maxZ);
        for (int i = 0; i < chaserStarts.Length; i++)
            IncludePointInBounds(chaserStarts[i], ref minX, ref minZ, ref maxX, ref maxZ);
        if (scenario.ChaserCenters != null)
        {
            for (int i = 0; i < scenario.ChaserCenters.Length; i++)
                IncludePointInBounds(scenario.ChaserCenters[i], ref minX, ref minZ, ref maxX, ref maxZ);
        }

        return Rect.MinMaxRect(minX - margin, minZ - margin, maxX + margin, maxZ + margin);
    }

    private static void IncludePointInBounds(Vector3 point, ref float minX, ref float minZ, ref float maxX, ref float maxZ)
    {
        minX = Mathf.Min(minX, point.x);
        minZ = Mathf.Min(minZ, point.z);
        maxX = Mathf.Max(maxX, point.x);
        maxZ = Mathf.Max(maxZ, point.z);
    }

    private static int CreateGridStaticNavigationBlockers(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Rect worldRegion,
        Vector3 heroStart,
        Vector3[] heroWaypoints,
        Vector3[] chaserStarts,
        List<GameObject> createdObjects,
        int blockerLayer)
    {
        const int maxStaticBlockerColliders = 1400;
        int minX = Mathf.Clamp(Mathf.FloorToInt((worldRegion.xMin - grid.Origin.x) / grid.CellSize), 0, grid.Width - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt((worldRegion.xMax - grid.Origin.x) / grid.CellSize), 0, grid.Width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt((worldRegion.yMin - grid.Origin.y) / grid.CellSize), 0, grid.Height - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt((worldRegion.yMax - grid.Origin.y) / grid.CellSize), 0, grid.Height - 1);
        HashSet<int> blockerCells = CollectGridStaticNavigationBlockerCells(
            grid,
            derivedData,
            minX,
            maxX,
            minY,
            maxY,
            heroStart,
            heroWaypoints,
            chaserStarts);
        Dictionary<long, ActiveNavigationBlockerSpan> active = new Dictionary<long, ActiveNavigationBlockerSpan>();
        List<long> finishedKeys = new List<long>();
        int colliderCount = 0;
        for (int y = minY; y <= maxY; y++)
        {
            foreach (ActiveNavigationBlockerSpan span in active.Values)
                span.SeenInCurrentRow = false;

            int x = minX;
            while (x <= maxX)
            {
                while (x <= maxX && !blockerCells.Contains(y * grid.Width + x))
                    x++;
                if (x > maxX)
                    break;

                int startX = x;
                while (x <= maxX && blockerCells.Contains(y * grid.Width + x))
                    x++;
                int endX = x - 1;
                long key = PackBlockerSpanKey(startX, endX);
                if (active.TryGetValue(key, out ActiveNavigationBlockerSpan existing))
                {
                    existing.EndY = y;
                    existing.SeenInCurrentRow = true;
                }
                else
                {
                    active.Add(key, new ActiveNavigationBlockerSpan
                    {
                        StartX = startX,
                        EndX = endX,
                        StartY = y,
                        EndY = y,
                        SeenInCurrentRow = true
                    });
                }
            }

            finishedKeys.Clear();
            foreach (KeyValuePair<long, ActiveNavigationBlockerSpan> pair in active)
            {
                if (pair.Value.SeenInCurrentRow)
                    continue;

                CreateGridStaticNavigationBlocker(grid, pair.Value, createdObjects, colliderCount++, blockerLayer);
                if (colliderCount > maxStaticBlockerColliders)
                    throw new InvalidOperationException($"CreateGridStaticNavigationBlockers failed: generated too many boundary colliders ({colliderCount}) for region={worldRegion}. The test physics proxy is too broad.");
                finishedKeys.Add(pair.Key);
            }

            for (int i = 0; i < finishedKeys.Count; i++)
                active.Remove(finishedKeys[i]);
        }

        foreach (ActiveNavigationBlockerSpan span in active.Values)
        {
            CreateGridStaticNavigationBlocker(grid, span, createdObjects, colliderCount++, blockerLayer);
            if (colliderCount > maxStaticBlockerColliders)
                throw new InvalidOperationException($"CreateGridStaticNavigationBlockers failed: generated too many boundary colliders ({colliderCount}) for region={worldRegion}. The test physics proxy is too broad.");
        }

        return colliderCount;
    }

    private static HashSet<int> CollectGridStaticNavigationBlockerCells(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        int minX,
        int maxX,
        int minY,
        int maxY,
        Vector3 heroStart,
        Vector3[] heroWaypoints,
        Vector3[] chaserStarts)
    {
        const float pointRadiusWorld = 3.25f;
        const int boundarySearchRadiusCells = 1;
        bool[] walkableMask = grid.GetWalkableMaskRuntimeReadOnlyReference();
        int[] islandIds = derivedData.IslandIds;
        int mainIslandId = derivedData.MainIslandId;
        HashSet<int> blockerCells = new HashSet<int>();
        HashSet<int> testedCells = new HashSet<int>();
        Debug.Log("[Lv3ControllerCombatPhysics] stage=static-boundary-collect-begin");
        AddGridStaticNavigationBlockerCellsNearPoint(
            grid,
            derivedData,
            minX,
            maxX,
            minY,
            maxY,
            heroStart,
            pointRadiusWorld,
            boundarySearchRadiusCells,
            walkableMask,
            islandIds,
            mainIslandId,
            testedCells,
            blockerCells);

        for (int i = 0; i < heroWaypoints.Length; i++)
        {
            Debug.Log($"[Lv3ControllerCombatPhysics] stage=static-boundary-hero-point index={i} tested={testedCells.Count} blockers={blockerCells.Count}");
            AddGridStaticNavigationBlockerCellsNearPoint(
                grid,
                derivedData,
                minX,
                maxX,
                minY,
                maxY,
                heroWaypoints[i],
                pointRadiusWorld,
                boundarySearchRadiusCells,
                walkableMask,
                islandIds,
                mainIslandId,
                testedCells,
                blockerCells);
        }

        Debug.Log($"[Lv3ControllerCombatPhysics] stage=static-boundary-cells tested={testedCells.Count} blockers={blockerCells.Count}");
        return blockerCells;
    }

    private static void AddGridStaticNavigationBlockerCellsNearPoint(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        int minX,
        int maxX,
        int minY,
        int maxY,
        Vector3 point,
        float radiusWorld,
        int boundarySearchRadiusCells,
        bool[] walkableMask,
        int[] islandIds,
        int mainIslandId,
        HashSet<int> testedCells,
        HashSet<int> blockerCells)
    {
        AddGridStaticNavigationBlockerCellsNearSegment(
            grid,
            derivedData,
            minX,
            maxX,
            minY,
            maxY,
            point,
            point,
            radiusWorld,
            boundarySearchRadiusCells,
            walkableMask,
            islandIds,
            mainIslandId,
            testedCells,
            blockerCells);
    }

    private static void AddGridStaticNavigationBlockerCellsNearSegment(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        int minX,
        int maxX,
        int minY,
        int maxY,
        Vector3 start,
        Vector3 end,
        float radiusWorld,
        int boundarySearchRadiusCells,
        bool[] walkableMask,
        int[] islandIds,
        int mainIslandId,
        HashSet<int> testedCells,
        HashSet<int> blockerCells)
    {
        float segmentMinX = Mathf.Min(start.x, end.x) - radiusWorld;
        float segmentMaxX = Mathf.Max(start.x, end.x) + radiusWorld;
        float segmentMinZ = Mathf.Min(start.z, end.z) - radiusWorld;
        float segmentMaxZ = Mathf.Max(start.z, end.z) + radiusWorld;
        int cellMinX = Mathf.Max(minX, Mathf.FloorToInt((segmentMinX - grid.Origin.x) / grid.CellSize));
        int cellMaxX = Mathf.Min(maxX, Mathf.CeilToInt((segmentMaxX - grid.Origin.x) / grid.CellSize));
        int cellMinY = Mathf.Max(minY, Mathf.FloorToInt((segmentMinZ - grid.Origin.y) / grid.CellSize));
        int cellMaxY = Mathf.Min(maxY, Mathf.CeilToInt((segmentMaxZ - grid.Origin.y) / grid.CellSize));
        float radiusSqr = radiusWorld * radiusWorld;
        Vector2 a = new Vector2(start.x, start.z);
        Vector2 b = new Vector2(end.x, end.z);
        for (int y = cellMinY; y <= cellMaxY; y++)
            for (int x = cellMinX; x <= cellMaxX; x++)
            {
                int key = y * grid.Width + x;
                if (!testedCells.Add(key))
                    continue;
                Vector2 center = new Vector2(
                    grid.Origin.x + (x + 0.5f) * grid.CellSize,
                    grid.Origin.y + (y + 0.5f) * grid.CellSize);
                if (DistancePointSegmentSqr(center, a, b) > radiusSqr)
                    continue;
                if (!IsGridStaticNavigationBoundaryBlocker(grid, walkableMask, islandIds, mainIslandId, x, y, boundarySearchRadiusCells))
                    continue;
                blockerCells.Add(key);
            }
    }

    private static float DistancePointSegmentSqr(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abSqr = ab.sqrMagnitude;
        if (abSqr <= 0.000001f)
            return (point - a).sqrMagnitude;
        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / abSqr);
        Vector2 closest = a + ab * t;
        return (point - closest).sqrMagnitude;
    }

    private static bool IsGridStaticNavigationBoundaryBlocker(
        FlowNavigationGridAsset grid,
        bool[] walkableMask,
        int[] islandIds,
        int mainIslandId,
        int x,
        int y,
        int boundarySearchRadiusCells)
    {
        if (IsMainIslandWalkableFast(grid, walkableMask, islandIds, mainIslandId, x, y))
            return false;

        for (int oy = -boundarySearchRadiusCells; oy <= boundarySearchRadiusCells; oy++)
            for (int ox = -boundarySearchRadiusCells; ox <= boundarySearchRadiusCells; ox++)
            {
                if (ox == 0 && oy == 0)
                    continue;
                int nx = x + ox;
                int ny = y + oy;
                if (nx < 0 || nx >= grid.Width || ny < 0 || ny >= grid.Height)
                    continue;
                if (IsMainIslandWalkableFast(grid, walkableMask, islandIds, mainIslandId, nx, ny))
                    return true;
            }

        return false;
    }

    private static bool IsMainIslandWalkableFast(
        FlowNavigationGridAsset grid,
        bool[] walkableMask,
        int[] islandIds,
        int mainIslandId,
        int x,
        int y)
    {
        if (x < 0 || x >= grid.Width || y < 0 || y >= grid.Height)
            return false;
        int index = x + y * grid.Width;
        return walkableMask != null
               && islandIds != null
               && index >= 0
               && index < walkableMask.Length
               && index < islandIds.Length
               && walkableMask[index]
               && islandIds[index] == mainIslandId;
    }

    private static long PackBlockerSpanKey(int startX, int endX)
    {
        return ((long)startX << 32) ^ (uint)endX;
    }

    private static void CreateGridStaticNavigationBlocker(
        FlowNavigationGridAsset grid,
        ActiveNavigationBlockerSpan span,
        List<GameObject> createdObjects,
        int index,
        int layer)
    {
        float sizeX = (span.EndX - span.StartX + 1) * grid.CellSize;
        float sizeZ = (span.EndY - span.StartY + 1) * grid.CellSize;
        Vector3 center = new Vector3(
            grid.Origin.x + (span.StartX + span.EndX + 1) * grid.CellSize * 0.5f,
            1f,
            grid.Origin.y + (span.StartY + span.EndY + 1) * grid.CellSize * 0.5f);
        GameObject obstacle = new GameObject("FlowTest_CC_Combat_StaticBlocker_" + index);
        obstacle.layer = layer;
        obstacle.transform.position = center;
        BoxCollider collider = obstacle.AddComponent<BoxCollider>();
        collider.size = new Vector3(sizeX, 2f, sizeZ);
        createdObjects.Add(obstacle);
    }

    private sealed class ActiveNavigationBlockerSpan
    {
        public int StartX;
        public int EndX;
        public int StartY;
        public int EndY;
        public bool SeenInCurrentRow;
    }

    private static int CollectLv3ControllerOverlapSamples(
        System.Text.StringBuilder diagnostics,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Transform[] transforms,
        Vector3 heroPosition,
        int frame,
        float agentRadius,
        int currentOverlapPairStreak,
        int existingOverlapPairSamples,
        SimEntityContext[] chasers = null,
        IEntityContext heroTarget = null)
    {
        const int maxDetailedSamples = 24;
        float overlapDistance = agentRadius * 1.55f;
        float overlapDistanceSq = overlapDistance * overlapDistance;
        int overlapPairs = 0;
        for (int i = 0; i < transforms.Length; i++)
        {
            for (int j = i + 1; j < transforms.Length; j++)
            {
                Vector3 a = transforms[i].position;
                Vector3 b = transforms[j].position;
                float distanceSq = HorizontalSqrMagnitude(a - b);
                if (distanceSq > overlapDistanceSq)
                    continue;

                overlapPairs++;
                if (existingOverlapPairSamples + overlapPairs > maxDetailedSamples)
                    continue;

                diagnostics.Append("[Lv3ControllerOverlap] frame=").Append(frame)
                    .Append(" pair=").Append(i).Append('/').Append(j)
                    .Append(" a=").Append(a)
                    .Append(" b=").Append(b)
                    .Append(" hero=").Append(heroPosition)
                    .Append(" distance=").Append(Mathf.Sqrt(distanceSq).ToString("F3"))
                    .Append(" overlapDistance=").Append(overlapDistance.ToString("F3"))
                    .Append(" currentStreak=").Append(currentOverlapPairStreak);
                AppendCellDiagnostic(diagnostics, grid, derivedData, a, "a");
                AppendCellDiagnostic(diagnostics, grid, derivedData, b, "b");
                if (chasers != null)
                {
                    AppendControllerOverlapAgentState(diagnostics, "a", i, chasers, heroTarget);
                    AppendControllerOverlapAgentState(diagnostics, "b", j, chasers, heroTarget);
                    AppendSteeringBreakdown(diagnostics, chasers[i]);
                    AppendSteeringBreakdown(diagnostics, chasers[j]);
                    AppendPathHandleDiagnostics(diagnostics, chasers[i], grid, derivedData, heroPosition);
                    AppendPathHandleDiagnostics(diagnostics, chasers[j], grid, derivedData, heroPosition);
                }
                diagnostics.AppendLine();
            }
        }

        return overlapPairs;
    }

    private static int UpdatePersistentOverlapPairStreaks(
        Transform[] transforms,
        float agentRadius,
        int[,] pairStreaks,
        out int maxPairA,
        out int maxPairB,
        out float maxPairDistance)
    {
        float overlapDistanceSq = agentRadius * 1.55f * agentRadius * 1.55f;
        int maxStreak = 0;
        maxPairA = -1;
        maxPairB = -1;
        maxPairDistance = float.PositiveInfinity;
        for (int i = 0; i < transforms.Length; i++)
        {
            for (int j = i + 1; j < transforms.Length; j++)
            {
                bool overlapping = HorizontalSqrMagnitude(transforms[i].position - transforms[j].position) <= overlapDistanceSq;
                pairStreaks[i, j] = overlapping ? pairStreaks[i, j] + 1 : 0;
                if (pairStreaks[i, j] <= maxStreak)
                    continue;

                maxStreak = pairStreaks[i, j];
                maxPairA = i;
                maxPairB = j;
                maxPairDistance = Mathf.Sqrt(HorizontalSqrMagnitude(transforms[i].position - transforms[j].position));
            }
        }

        return maxStreak;
    }

    private static void AppendControllerOverlapAgentState(
        System.Text.StringBuilder diagnostics,
        string label,
        int index,
        SimEntityContext[] chasers,
        IEntityContext heroTarget)
    {
        diagnostics.Append(' ').Append(label).Append("State={");
        if (chasers == null || index < 0 || index >= chasers.Length || chasers[index] == null)
        {
            diagnostics.Append("missing}");
            return;
        }

        SimEntityContext chaser = chasers[index];
        IEntityContext currentTarget = chaser.TargetComp?.CurrentTarget;
        SoldierAIBrain brain = chaser.Brain as SoldierAIBrain;
        CharacterMoveComp moveComp = chaser.MoveComp as CharacterMoveComp;
        float targetSurfaceDistance = currentTarget != null ? chaser.DistanceToTargetSurface(currentTarget) : float.PositiveInfinity;
        float heroSurfaceDistance = heroTarget != null ? chaser.DistanceToTargetSurface(heroTarget) : float.PositiveInfinity;
        float attackRange = chaser.WeaponComp != null
            ? (float)chaser.WeaponComp.AttackRange
            : float.NaN;

        diagnostics.Append("key=").Append(chaser.CharacterKey)
            .Append(",targetHero=").Append(ReferenceEquals(currentTarget, heroTarget))
            .Append(",targetNull=").Append(currentTarget == null)
            .Append(",brainState=").Append(brain != null ? brain.State.ToString() : "none")
            .Append(",attack=").Append(brain != null && brain.Attack)
            .Append(",moveTarget=").Append(moveComp != null && moveComp.HasNavigationTarget)
            .Append(",moving=").Append(chaser.MoveComp != null && chaser.MoveComp.IsMoving)
            .Append(",targetSurfaceDist=").Append(float.IsPositiveInfinity(targetSurfaceDistance) ? "INF" : targetSurfaceDistance.ToString("F3"))
            .Append(",heroSurfaceDist=").Append(float.IsPositiveInfinity(heroSurfaceDistance) ? "INF" : heroSurfaceDistance.ToString("F3"))
            .Append(",attackRange=").Append(float.IsNaN(attackRange) ? "NaN" : attackRange.ToString("F3"))
            .Append(",navTarget=");
        if (moveComp != null && moveComp.TryGetNavigationTargetFixed(out FixVector2 navigationTarget))
            diagnostics.Append(navigationTarget);
        else
            diagnostics.Append("none");
        diagnostics.Append('}');
    }

    private static int CollectLv3CrowdRouteOverlapSamples(
        Lv3CrowdRouteScenario scenario,
        Lv3CrowdRouteMetrics metrics,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        SimEntityContext[] chasers,
        IEntityContext heroTarget,
        Vector3 heroPosition,
        int frame,
        float agentRadius,
        int currentOverlapPairStreak)
    {
        float overlapDistance = agentRadius * 1.55f;
        float overlapDistanceSq = overlapDistance * overlapDistance;
        int overlapPairs = 0;
        for (int i = 0; i < chasers.Length; i++)
        {
            for (int j = i + 1; j < chasers.Length; j++)
            {
                Vector3 a = chasers[i].Position;
                Vector3 b = chasers[j].Position;
                float distanceSq = HorizontalSqrMagnitude(a - b);
                if (distanceSq > overlapDistanceSq)
                    continue;

                overlapPairs++;
                if (metrics.DetailedIssueSamples >= 48)
                    continue;

                bool aHasHeroTarget = chasers[i].TargetComp is SimTargetingComp targetingA && ReferenceEquals(targetingA.CurrentTarget, heroTarget);
                bool bHasHeroTarget = chasers[j].TargetComp is SimTargetingComp targetingB && ReferenceEquals(targetingB.CurrentTarget, heroTarget);
                metrics.DetailedIssueSamples++;
                metrics.Diagnostics.Append("[Lv3CrowdOverlap] scenario=").Append(scenario.Name)
                    .Append(" frame=").Append(frame)
                    .Append(" pair=").Append(i).Append('/').Append(j)
                    .Append(" a=").Append(a)
                    .Append(" b=").Append(b)
                    .Append(" hero=").Append(heroPosition)
                    .Append(" distance=").Append(Mathf.Sqrt(distanceSq).ToString("F3"))
                    .Append(" overlapDistance=").Append(overlapDistance.ToString("F3"))
                    .Append(" currentStreak=").Append(currentOverlapPairStreak)
                    .Append(" aHasHeroTarget=").Append(aHasHeroTarget)
                    .Append(" bHasHeroTarget=").Append(bHasHeroTarget);
                AppendCellDiagnostic(metrics.Diagnostics, grid, derivedData, a, "a");
                AppendCellDiagnostic(metrics.Diagnostics, grid, derivedData, b, "b");
                AppendSteeringBreakdown(metrics.Diagnostics, chasers[i]);
                AppendSteeringBreakdown(metrics.Diagnostics, chasers[j]);
                AppendPathHandleDiagnostics(metrics.Diagnostics, chasers[i], grid, derivedData, heroPosition);
                AppendPathHandleDiagnostics(metrics.Diagnostics, chasers[j], grid, derivedData, heroPosition);
                metrics.Diagnostics.AppendLine();
            }
        }

        return overlapPairs;
    }

    private static Vector3[] ResolveLv3ScenarioChaserStarts(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3CrowdRouteScenario scenario,
        float agentRadius,
        System.Text.StringBuilder diagnostics)
    {
        if (scenario.ChaserCenterCounts != null)
            return ResolveLv3ScenarioChaserStartsFromRealSpawnPreview(grid, derivedData, scenario, agentRadius, diagnostics);

        List<Vector3> starts = new List<Vector3>(scenario.ChaserCount);
        const float minDistance = 0.75f;
        int centerIndex = 0;
        int guard = 0;
        while (starts.Count < scenario.ChaserCount && guard < scenario.ChaserCount * 64)
        {
            Vector3 center = scenario.ChaserCenters[centerIndex % scenario.ChaserCenters.Length];
            float angle = guard * 2.39996323f;
            float radius = 0.25f + Mathf.Sqrt((guard % 48) / 48f) * 3.4f;
            Vector3 preferred = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            if (TryResolveRuntimeLegalUniqueLv3Start(grid, derivedData, preferred, 4.5f, agentRadius, minDistance, starts, out Vector3 start))
                starts.Add(start);
            centerIndex++;
            guard++;
        }

        if (starts.Count < scenario.ChaserCount)
            throw new InvalidOperationException($"ResolveLv3ScenarioChaserStarts failed: scenario={scenario.Name} requested={scenario.ChaserCount}, resolved={starts.Count}.");

        diagnostics.Append("[Lv3CrowdScenario] chaserStarts=").Append(starts.Count).AppendLine();
        for (int i = 0; i < starts.Count; i++)
        {
            diagnostics.Append("[Lv3CrowdScenario] chaser-").Append(i)
                .Append(" start=").Append(starts[i]);
            AppendCellDiagnostic(diagnostics, grid, derivedData, starts[i], "start");
            diagnostics.AppendLine();
        }

        return starts.ToArray();
    }

    private static Vector3[] ResolveLv3ScenarioChaserStartsFromRealSpawnPreview(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3CrowdRouteScenario scenario,
        float agentRadius,
        System.Text.StringBuilder diagnostics)
    {
        Vector3[] rawStarts = ResolveLv3ScenarioRawChaserStartsFromRealSpawnPreview(scenario);
        return ResolveLv3ScenarioChaserStartsFromRawPreview(
            grid,
            derivedData,
            scenario,
            rawStarts,
            agentRadius,
            diagnostics);
    }

    private static Vector3[] ResolveLv3ScenarioRawChaserStartsFromRealSpawnPreview(Lv3CrowdRouteScenario scenario)
    {
        if (scenario.ChaserCenterCounts.Length != scenario.ChaserCenters.Length)
            throw new InvalidOperationException($"ResolveLv3ScenarioChaserStartsFromRealSpawnPreview failed: scenario={scenario.Name} centers={scenario.ChaserCenters.Length}, counts={scenario.ChaserCenterCounts.Length}.");

        const float enemyPresetClusterRadius = 3f;
        const float enemyPresetClusterMinDistance = 1.2f;
        List<Vector3> rawStarts = new List<Vector3>(Mathf.Max(0, scenario.ChaserCount));
        List<Vector3> preview = new List<Vector3>();
        for (int centerIndex = 0; centerIndex < scenario.ChaserCenters.Length; centerIndex++)
        {
            int count = scenario.ChaserCenterCounts[centerIndex];
            if (count <= 0)
                continue;

            preview.Clear();
            if (!ClusterSpawnSystem.TryGetPreviewSpawnPositions(
                    scenario.ChaserCenters[centerIndex],
                    count,
                    enemyPresetClusterRadius,
                    enemyPresetClusterMinDistance,
                    preview))
            {
                throw new InvalidOperationException($"ResolveLv3ScenarioChaserStartsFromRealSpawnPreview failed: scenario={scenario.Name}, center={scenario.ChaserCenters[centerIndex]}, count={count} cannot resolve real preview spawn positions.");
            }

            for (int i = 0; i < preview.Count; i++)
                rawStarts.Add(preview[i]);
        }

        if (rawStarts.Count != scenario.ChaserCount)
            throw new InvalidOperationException($"ResolveLv3ScenarioChaserStartsFromRealSpawnPreview failed: scenario={scenario.Name} expected={scenario.ChaserCount}, resolved={rawStarts.Count}.");

        return rawStarts.ToArray();
    }

    private static Vector3[] ResolveLv3ScenarioChaserStartsFromRawPreview(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3CrowdRouteScenario scenario,
        Vector3[] rawStarts,
        float agentRadius,
        System.Text.StringBuilder diagnostics)
    {
        if (rawStarts == null || rawStarts.Length != scenario.ChaserCount)
            throw new InvalidOperationException($"ResolveLv3ScenarioChaserStartsFromRawPreview failed: scenario={scenario.Name} expected={scenario.ChaserCount}, raw={(rawStarts != null ? rawStarts.Length : -1)}.");

        Vector3[] starts = new Vector3[rawStarts.Length];
        for (int i = 0; i < rawStarts.Length; i++)
        {
            starts[i] = ResolveRuntimeLegalLv3MainIslandCell(
                grid,
                derivedData,
                rawStarts[i],
                2.0f,
                agentRadius,
                scenario.Name + "-real-spawn-" + i,
                diagnostics);
        }

        diagnostics.Append("[Lv3CrowdScenario] realSpawnCount=").Append(starts.Length).AppendLine();
        return starts;
    }

    private static bool TryResolveRuntimeLegalUniqueLv3Start(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 preferred,
        float maxRadius,
        float agentRadius,
        float minDistance,
        List<Vector3> existing,
        out Vector3 result)
    {
        result = Vector3.zero;
        if (!TryResolveNearestMainIslandCell(grid, derivedData, preferred, maxRadius, out Vector3 mainIsland))
            return false;
        if (!FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
                mainIsland,
                grid.AgentTypeId,
                grid.CellSize * 0.75f,
                Mathf.Max(0f, agentRadius - grid.CellSize * 0.2f),
                out Vector3 legal))
        {
            return false;
        }
        if (!grid.WorldToCell(legal, out int legalX, out int legalY) || !IsMainIslandWalkable(grid, derivedData, legalX, legalY))
            return false;

        float minDistanceSq = minDistance * minDistance;
        for (int i = 0; i < existing.Count; i++)
        {
            if (HorizontalSqrMagnitude(existing[i] - legal) < minDistanceSq)
                return false;
        }

        result = legal;
        return true;
    }

    private static void CollectLv3CrowdRouteSample(
        Lv3CrowdRouteScenario scenario,
        Lv3CrowdRouteMetrics metrics,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3ResearchCenterFootprint researchCenter,
        Rect researchBounds,
        SimEntityContext chaser,
        SimMoveExecutor executor,
        Vector3 decisionPosition,
        IEntityContext heroTarget,
        Vector3 heroPosition,
        int frame,
        int agentIndex,
        bool agentExpectsLowerRoute,
        int[] zeroStreak,
        int[] maxZeroStreak,
        ref int slowWallAgentsThisFrame,
        ref int slowTargetedAgentsThisFrame)
    {
        Vector3 desired = executor.LastDesiredDisplacement;
        desired.y = 0f;
        Vector3 constrained = executor.LastConstrainedDisplacement;
        constrained.y = 0f;
        Vector3 desiredDirection = desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector3.zero;
        Assert.IsTrue(grid.WorldToCell(decisionPosition, out int currentX, out int currentY), "追兵决策位置必须在 Lv3 grid 内。");
        int currentSectorId = ResolveDerivedSectorId(derivedData, currentX, currentY);
        bool hitsResearchCenter = desiredDirection.sqrMagnitude > 0f
                                  && researchCenter.RayHitsAnyBox(decisionPosition, desiredDirection, 18f);
        float wallHitDistance = hitsResearchCenter
            ? researchCenter.DistanceToFirstRayHit(decisionPosition, desiredDirection, 18f)
            : float.PositiveInfinity;
        bool hasSelectedPortal = TryResolveSelectedPortalCenter(chaser, currentSectorId, out int selectedPortalId, out Vector3 selectedPortalCenter);
        float selectedPortalDistance = hasSelectedPortal
            ? HorizontalDistance(decisionPosition, selectedPortalCenter)
            : float.PositiveInfinity;
        float targetDistance = HorizontalDistance(decisionPosition, heroPosition);
        float routeLimitDistance = Mathf.Min(selectedPortalDistance, targetDistance);
        bool segmentEntersResearchCenter = desiredDirection.sqrMagnitude > 0f
                                           && SegmentEntersFootprint(
                                               researchCenter,
                                               decisionPosition,
                                               desiredDirection,
                                               Mathf.Min(routeLimitDistance + 0.2f, 18f),
                                               grid.CellSize * 0.5f);
        bool actualNavigationSegmentViolatesResearchCenter = SteeringTileTargetSegmentViolatesFootprintClearance(
            researchCenter,
            decisionPosition,
            chaser,
            grid.CellSize * 0.5f,
            0.45f);
        bool hitsBeforePortal = hitsResearchCenter
                                && wallHitDistance <= routeLimitDistance + 0.2f
                                && segmentEntersResearchCenter
                                && actualNavigationSegmentViolatesResearchCenter;
        bool projectedToZero = desired.sqrMagnitude > 0.04f * 0.04f
                               && constrained.sqrMagnitude <= 0.015f * 0.015f;
        zeroStreak[agentIndex] = projectedToZero ? zeroStreak[agentIndex] + 1 : 0;
        maxZeroStreak[agentIndex] = Mathf.Max(maxZeroStreak[agentIndex], zeroStreak[agentIndex]);
        float researchDistance = researchCenter.DistanceToClosestBox(decisionPosition);
        bool constrainedWallStall = projectedToZero && researchDistance <= 0.9f;
        bool hasSteeringGoal = FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringGoal(chaser.LogicEntityId.Value, out Vector3 steeringGoal, out int steeringFrame);
        bool hasHeroAsCombatTarget = chaser.TargetComp is SimTargetingComp targeting && ReferenceEquals(targeting.CurrentTarget, heroTarget);
        bool expectsActiveSteering = hasHeroAsCombatTarget && desired.sqrMagnitude > 0.04f * 0.04f && targetDistance > 1.25f;
        bool staleSteering = expectsActiveSteering && (!hasSteeringGoal || frame - steeringFrame > 2);
        bool nearWallGoal = hasSteeringGoal && researchCenter.DistanceToClosestBox(steeringGoal) < 0.35f;
        bool constraintFailed = !executor.LastConstraintSucceeded && expectsActiveSteering;
        bool upperDetour = scenario.ExpectLowerRoute
                           && agentExpectsLowerRoute
                           && hasSelectedPortal
                           && heroPosition.z <= researchBounds.yMin + 0.8f
                           && decisionPosition.x <= researchBounds.xMax + 1.5f
                           && selectedPortalCenter.z >= researchBounds.yMax + 0.25f;
        if (hitsBeforePortal)
        {
            metrics.WallBeforePortalSamples++;
            metrics.MinWallHitDistance = Mathf.Min(metrics.MinWallHitDistance, wallHitDistance);
        }
        if (constrainedWallStall)
            metrics.ConstrainedWallStallSamples++;
        if (staleSteering)
            metrics.StaleSteeringSamples++;
        if (nearWallGoal)
            metrics.NearWallGoalSamples++;
        if (constraintFailed)
            metrics.ConstraintFailureSamples++;
        if (upperDetour)
            metrics.UpperDetourSamples++;
        if (researchDistance <= 1.05f && constrained.magnitude <= 0.08f && desired.magnitude > 0.12f)
            slowWallAgentsThisFrame++;
        if (hasHeroAsCombatTarget && targetDistance > 1.25f && constrained.magnitude <= 0.08f && desired.magnitude > 0.12f)
            slowTargetedAgentsThisFrame++;

        bool isIssueSample = hitsBeforePortal || constrainedWallStall || nearWallGoal || constraintFailed || upperDetour;
        bool isBackgroundSample = staleSteering || frame % 45 == 0;
        bool shouldRecordDetailedSample = (isIssueSample && metrics.DetailedIssueSamples < 24)
                                          || (!isIssueSample && isBackgroundSample && metrics.DetailedBackgroundSamples < 8);
        if (!shouldRecordDetailedSample)
            return;

        if (isIssueSample)
            metrics.DetailedIssueSamples++;
        else
            metrics.DetailedBackgroundSamples++;
        metrics.Diagnostics.Append("[Lv3CrowdScenarioSample] scenario=").Append(scenario.Name)
            .Append(" frame=").Append(frame)
            .Append(" agent=").Append(agentIndex)
            .Append(" hero=").Append(heroPosition)
            .Append(" decision=").Append(decisionPosition)
            .Append(" after=").Append(chaser.Position);
        AppendCellDiagnostic(metrics.Diagnostics, grid, derivedData, decisionPosition, "decision");
        metrics.Diagnostics.Append(" desiredDisp=").Append(executor.LastDesiredDisplacement)
            .Append(" constrainedDisp=").Append(executor.LastConstrainedDisplacement)
            .Append(" zeroStreak=").Append(zeroStreak[agentIndex])
            .Append(" rayHitsResearchCenter=").Append(hitsResearchCenter)
            .Append(" wallHitDist=").Append(float.IsPositiveInfinity(wallHitDistance) ? "INF" : wallHitDistance.ToString("F3"))
            .Append(" selectedPortal=").Append(hasSelectedPortal ? selectedPortalId.ToString() : "missing")
            .Append(" selectedPortalCenter=").Append(hasSelectedPortal ? selectedPortalCenter.ToString() : "missing")
            .Append(" portalDist=").Append(float.IsPositiveInfinity(selectedPortalDistance) ? "INF" : selectedPortalDistance.ToString("F3"))
            .Append(" targetDist=").Append(targetDistance.ToString("F3"))
            .Append(" segmentEntersResearchCenter=").Append(segmentEntersResearchCenter)
            .Append(" actualNavSegmentViolatesResearchCenter=").Append(actualNavigationSegmentViolatesResearchCenter)
            .Append(" hitsBeforePortal=").Append(hitsBeforePortal)
            .Append(" constrainedWallStall=").Append(constrainedWallStall)
            .Append(" hasHeroTarget=").Append(hasHeroAsCombatTarget)
            .Append(" expectsActiveSteering=").Append(expectsActiveSteering)
            .Append(" staleSteering=").Append(staleSteering)
            .Append(" nearWallGoal=").Append(nearWallGoal)
            .Append(" constraintSucceeded=").Append(executor.LastConstraintSucceeded)
            .Append(" constraintFailed=").Append(constraintFailed)
            .Append(" agentExpectsLowerRoute=").Append(agentExpectsLowerRoute)
            .Append(" upperDetour=").Append(upperDetour)
            .Append(" steeringGoal=").Append(hasSteeringGoal ? steeringGoal.ToString() : "missing")
            .Append(" steeringFrame=").Append(hasSteeringGoal ? steeringFrame.ToString() : "missing");
        if (FlowFieldCrowdMovementSystem.TryGetEditorTestCurrentTileQueueState(
                chaser.LogicEntityId.Value,
                out string queueGoalKind,
                out int queuePortalId,
                out bool queueCached,
                out bool queuePending,
                out int queueIndex,
                out string queueStage,
                out bool queueWaiting,
                out bool queueStale,
                out int pendingPortalFrames))
        {
            metrics.Diagnostics.Append(" tileQueue={kind=").Append(queueGoalKind)
                .Append(",portal=").Append(queuePortalId)
                .Append(",cached=").Append(queueCached)
                .Append(",pending=").Append(queuePending)
                .Append(",index=").Append(queueIndex)
                .Append(",stage=").Append(queueStage)
                .Append(",waiting=").Append(queueWaiting)
                .Append(",stale=").Append(queueStale)
                .Append(",pendingPortalFrames=").Append(pendingPortalFrames)
                .Append('}');
        }
        else
        {
            metrics.Diagnostics.Append(" tileQueue=missing");
        }
        AppendSteeringBreakdown(metrics.Diagnostics, chaser);
        AppendPathHandleDiagnostics(metrics.Diagnostics, chaser, grid, derivedData, heroPosition);
        if (hitsBeforePortal || upperDetour)
            AppendSegmentGridDiagnostics(metrics.Diagnostics, grid, derivedData, researchCenter, decisionPosition, "decision-to-tileTarget", chaser);
        metrics.Diagnostics.AppendLine();
    }

    private static string BuildLv3BadPointDiagnostics(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        SimEntityContext chaser,
        SimMoveExecutor executor,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int startSectorId,
        int goalSectorId)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(2048);
        Vector3 start = grid.GetCellAnchor(startX, startY);
        Vector3 goal = grid.GetCellAnchor(goalX, goalY);
        int startIsland = derivedData.IslandIds[startX + startY * grid.Width];
        int goalIsland = derivedData.IslandIds[goalX + goalY * grid.Width];

        builder.Append("Lv3BadPoint")
            .Append(" start=").Append(start)
            .Append(" goal=").Append(goal)
            .Append(" startCell=(").Append(startX).Append(',').Append(startY).Append(')')
            .Append(" goalCell=(").Append(goalX).Append(',').Append(goalY).Append(')')
            .Append(" startIsland=").Append(startIsland)
            .Append(" goalIsland=").Append(goalIsland)
            .Append(" mainIsland=").Append(derivedData.MainIslandId)
            .Append(" startSector=").Append(startSectorId)
            .Append(" goalSector=").Append(goalSectorId)
            .AppendLine();

        if (FlowFieldCrowdMovementSystem.TryGetEditorTestPathBuildSource(chaser.LogicEntityId.Value, out string buildSource))
            builder.Append("pathBuildSource=").Append(buildSource).AppendLine();
        if (FlowFieldCrowdMovementSystem.TryGetEditorTestPathSectorIds(chaser.LogicEntityId.Value, out int[] sectorIds))
            builder.Append("pathSectors=[").Append(string.Join("->", sectorIds)).Append("]").AppendLine();
        if (FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(chaser.LogicEntityId.Value, out int[] portalIds))
        {
            builder.Append("pathPortals=[").Append(string.Join("->", portalIds)).Append("]").AppendLine();
        }

        builder.Append("portalChoice=")
            .Append(FlowFieldCrowdMovementSystem.GetEditorTestStartPortalChoiceDiagnostics(startSectorId, goalSectorId, startX, startY, goalX, goalY, grid.AgentTypeId))
            .AppendLine();
        builder.Append("portalGraph=")
            .Append(FlowFieldCrowdMovementSystem.BuildEditorTestPortalGraphCostDiagnostics(startSectorId, goalSectorId, startX, startY, goalX, goalY))
            .AppendLine();

        if (FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringDiagnostic(
                chaser.LogicEntityId.Value,
                out Vector3 desiredVelocity,
                out Vector3 baseVelocity,
                out Vector3 resultPreClamp,
                out Vector3 result))
        {
            builder.Append("steering desired=").Append(desiredVelocity)
                .Append(" base=").Append(baseVelocity)
                .Append(" pre=").Append(resultPreClamp)
                .Append(" result=").Append(result)
                .AppendLine();
        }
        else
        {
            builder.AppendLine("steering=missing");
        }

        builder.Append("executor input=").Append(executor.LastInputVelocity)
            .Append(" frameVelocity=").Append(executor.LastFrameVelocity)
            .Append(" desiredDisp=").Append(executor.LastDesiredDisplacement)
            .Append(" constrainedDisp=").Append(executor.LastConstrainedDisplacement)
            .Append(" constraintOk=").Append(executor.LastConstraintSucceeded)
            .Append(" finalPos=").Append(chaser.Position)
            .AppendLine();

        return builder.ToString();
    }

    private static Lv3RightBottomRoute ResolveLv3RightBottomRoute(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        System.Text.StringBuilder diagnostics)
    {
        if (grid == null)
            throw new InvalidOperationException("ResolveLv3RightBottomRoute failed: grid is null.");
        if (derivedData == null || !derivedData.IsValid)
            throw new InvalidOperationException("ResolveLv3RightBottomRoute failed: derivedData is invalid.");

        Debug.Log("[Lv3ResearchCenterChaseTest] stage=load-preset-begin");
        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=load-preset-end hero={preset.HeroStart} research={preset.ResearchCenterPosition} unitCenters={preset.UnitSpawnCenters.Length} bounds={preset.ResearchCenterFootprintBounds} sh13={preset.StrongholdSh13Bounds}");
        Vector3 rightBottomPreferred = new Vector3(
            preset.StrongholdSh13Bounds.xMax,
            0f,
            preset.StrongholdSh13Bounds.yMin);
        Vector2 rightBottomMin = new Vector2(
            preset.StrongholdSh13Bounds.xMax - 6.0f,
            preset.StrongholdSh13Bounds.yMin - 2.0f);
        Vector2 rightBottomMax = new Vector2(
            preset.StrongholdSh13Bounds.xMax + 1.6f,
            preset.StrongholdSh13Bounds.yMin + 6.0f);

        Debug.Log("[Lv3ResearchCenterChaseTest] stage=resolve-route-points-begin");
        Lv3RightBottomRoute route = new Lv3RightBottomRoute
        {
            HeroStart = ResolveNearestMainIslandCell(grid, derivedData, preset.HeroStart, 5f, "hero-start", diagnostics),
            LowerApproach = ResolveNearestMainIslandCell(
                grid,
                derivedData,
                new Vector3(preset.ResearchCenterPosition.x, 0f, preset.ResearchCenterFootprintBounds.yMin - 1.2f),
                5f,
                "lower-approach",
                diagnostics),
            RightBottomCorner = ResolveBestMainIslandCellInWorldBounds(
                grid,
                derivedData,
                rightBottomPreferred,
                rightBottomMin,
                rightBottomMax,
                "right-bottom-corner",
                diagnostics),
            ReturnPoint = ResolveNearestMainIslandCell(grid, derivedData, preset.HeroStart, 5f, "return-point", diagnostics),
            ResearchCenterPosition = preset.ResearchCenterPosition,
            ResearchCenterBounds = preset.ResearchCenterFootprintBounds,
            StrongholdBounds = preset.StrongholdSh13Bounds
        };
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=resolve-route-points-end heroStart={route.HeroStart} corner={route.RightBottomCorner} return={route.ReturnPoint}");

        Debug.Log("[Lv3ResearchCenterChaseTest] stage=cluster-candidates-begin");
        List<Vector3> chaserCandidates = ResolveLv3ClusterSpawnCandidates(grid, derivedData, preset.UnitSpawnCenters, diagnostics);
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=cluster-candidates-end count={chaserCandidates.Count}");
        if (chaserCandidates.Count < 6)
            throw new InvalidOperationException($"ResolveLv3RightBottomRoute failed: only {chaserCandidates.Count} legal chaser candidates generated.");

        route.ChaserStarts = new Vector3[chaserCandidates.Count];
        for (int i = 0; i < chaserCandidates.Count; i++)
            route.ChaserStarts[i] = ResolveNearestMainIslandCell(grid, derivedData, chaserCandidates[i], 3.5f, "chaser-" + i, diagnostics);

        Debug.Log("[Lv3ResearchCenterChaseTest] stage=route-map-begin");
        AppendLv3RouteMap(grid, derivedData, route, diagnostics);
        Debug.Log("[Lv3ResearchCenterChaseTest] stage=route-map-end");
        return route;
    }

    private static Lv3PresetSnapshot LoadLv3PresetSnapshot()
    {
        const string levelPrefabPath = "Assets/AAAGame/Prefabs/Entity/Level/Level_3.prefab";
        GameObject root = UnityEditor.PrefabUtility.LoadPrefabContents(levelPrefabPath);
        if (root == null)
            throw new InvalidOperationException($"LoadLv3PresetSnapshot failed: cannot load {levelPrefabPath}.");

        try
        {
            EntityPresetPoint[] points = root.GetComponentsInChildren<EntityPresetPoint>(true);
            if (points == null || points.Length == 0)
                throw new InvalidOperationException($"LoadLv3PresetSnapshot failed: no EntityPresetPoint in {levelPrefabPath}.");

            EntityPresetPoint researchPoint = null;
            for (int i = 0; i < points.Length; i++)
            {
                EntityPresetPoint point = points[i];
                if (point == null
                    || point.PointType != EntityPresetPointType.Building
                    || !string.Equals(point.Identifier, "Buil_ResearchCenter_Lv1", StringComparison.Ordinal))
                    continue;

                if (researchPoint == null
                    || point.Position.z < researchPoint.Position.z - 0.001f
                    || (Mathf.Abs(point.Position.z - researchPoint.Position.z) <= 0.001f && point.Position.x > researchPoint.Position.x))
                {
                    researchPoint = point;
                }
            }

            if (researchPoint == null)
                throw new InvalidOperationException("LoadLv3PresetSnapshot failed: Buil_ResearchCenter_Lv1 preset point not found.");

            Lv3ResearchCenterFootprint footprint = CreateLv3ResearchCenterLv1Footprint(researchPoint.Position);
            Rect footprintBounds = footprint.CalculateBounds();
            Rect strongholdSh13Bounds = ResolveStrongholdBlueprintBounds(root, "SH_1_3");

            EntityPresetPoint heroPoint = null;
            float bestHeroScore = float.PositiveInfinity;
            for (int i = 0; i < points.Length; i++)
            {
                EntityPresetPoint point = points[i];
                if (point == null || point.PointType != EntityPresetPointType.Hero)
                    continue;

                float score = HorizontalSqrMagnitude(point.Position - researchPoint.Position);
                if (score < bestHeroScore)
                {
                    bestHeroScore = score;
                    heroPoint = point;
                }
            }

            if (heroPoint == null)
                throw new InvalidOperationException("LoadLv3PresetSnapshot failed: hero preset point not found.");

            List<Lv3UnitSpawnPreset> unitSpawns = new List<Lv3UnitSpawnPreset>();
            for (int i = 0; i < points.Length; i++)
            {
                EntityPresetPoint point = points[i];
                if (point == null
                    || point.PointType != EntityPresetPointType.Unit
                    || !string.Equals(point.Identifier, "Unit_Scapegoat", StringComparison.Ordinal))
                    continue;
                if (point.UnitSpawnCount <= 0)
                    continue;

                Vector3 position = point.Position;
                if (HorizontalSqrMagnitude(position - researchPoint.Position) > 18f * 18f)
                    continue;
                if (position.z < footprintBounds.yMin - 0.2f)
                    continue;

                unitSpawns.Add(new Lv3UnitSpawnPreset
                {
                    Position = position,
                    Count = point.UnitSpawnCount
                });
            }

            if (unitSpawns.Count == 0)
                throw new InvalidOperationException("LoadLv3PresetSnapshot failed: no nearby Unit_Scapegoat preset points around lower ResearchCenter.");

            unitSpawns.Sort((a, b) =>
                HorizontalSqrMagnitude(a.Position - researchPoint.Position).CompareTo(HorizontalSqrMagnitude(b.Position - researchPoint.Position)));
            Vector3[] unitCenters = new Vector3[unitSpawns.Count];
            int[] unitCounts = new int[unitSpawns.Count];
            int totalUnitCount = 0;
            for (int i = 0; i < unitSpawns.Count; i++)
            {
                unitCenters[i] = unitSpawns[i].Position;
                unitCounts[i] = unitSpawns[i].Count;
                totalUnitCount += unitSpawns[i].Count;
            }

            return new Lv3PresetSnapshot
            {
                HeroStart = heroPoint.Position,
                ResearchCenterPosition = researchPoint.Position,
                ResearchCenterFootprintBounds = footprintBounds,
                StrongholdSh13Bounds = strongholdSh13Bounds,
                UnitSpawnCenters = unitCenters,
                UnitSpawnCounts = unitCounts,
                TotalUnitSpawnCount = totalUnitCount
            };
        }
        finally
        {
            UnityEditor.PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Rect ResolveStrongholdBlueprintBounds(GameObject levelRoot, string layerName)
    {
        if (levelRoot == null)
            throw new InvalidOperationException("ResolveStrongholdBlueprintBounds failed: levelRoot is null.");
        if (string.IsNullOrEmpty(layerName))
            throw new InvalidOperationException("ResolveStrongholdBlueprintBounds failed: layerName is empty.");

        Type managerType = Type.GetType("GiantGrey.TileWorldCreator.TileWorldCreatorManager, GiantGrey.TileWorldCreator");
        if (managerType == null)
            throw new InvalidOperationException("ResolveStrongholdBlueprintBounds failed: TileWorldCreatorManager type not found.");

        Component manager = levelRoot.GetComponentInChildren(managerType, true);
        if (manager == null)
            throw new InvalidOperationException("ResolveStrongholdBlueprintBounds failed: TileWorldCreatorManager or configuration is null.");

        object configuration = managerType.GetField("configuration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(manager);
        if (configuration == null)
            throw new InvalidOperationException("ResolveStrongholdBlueprintBounds failed: TileWorldCreatorManager or configuration is null.");

        IList blueprintLayerFolders = GetRequiredFieldValue<IList>(configuration, "blueprintLayerFolders");
        object targetLayer = null;
        for (int folderIndex = 0; folderIndex < blueprintLayerFolders.Count; folderIndex++)
        {
            object folder = blueprintLayerFolders[folderIndex];
            if (folder == null)
                continue;

            IList blueprintLayers = GetRequiredFieldValue<IList>(folder, "blueprintLayers");
            if (blueprintLayers == null)
                continue;

            for (int layerIndex = 0; layerIndex < blueprintLayers.Count; layerIndex++)
            {
                object layer = blueprintLayers[layerIndex];
                string currentLayerName = layer != null
                    ? GetRequiredFieldValue<string>(layer, "layerName")
                    : null;
                if (layer != null && string.Equals(currentLayerName, layerName, StringComparison.Ordinal))
                {
                    targetLayer = layer;
                    break;
                }
            }

            if (targetLayer != null)
                break;
        }

        if (targetLayer == null)
            throw new InvalidOperationException($"ResolveStrongholdBlueprintBounds failed: blueprint layer {layerName} not found.");
        IEnumerable allPositions = GetRequiredFieldValue<IEnumerable>(targetLayer, "allPositions");
        if (allPositions == null)
            throw new InvalidOperationException($"ResolveStrongholdBlueprintBounds failed: blueprint layer {layerName} has no positions.");

        float minX = float.PositiveInfinity;
        float minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxZ = float.NegativeInfinity;
        float cellSize = GetRequiredFieldValue<float>(configuration, "cellSize");
        int positionCount = 0;
        foreach (Vector2 cell in allPositions)
        {
            positionCount++;
            Vector3 world = manager.transform.TransformPoint(new Vector3(cell.x * cellSize, 0f, cell.y * cellSize));
            minX = Mathf.Min(minX, world.x);
            minZ = Mathf.Min(minZ, world.z);
            maxX = Mathf.Max(maxX, world.x);
            maxZ = Mathf.Max(maxZ, world.z);
        }

        if (positionCount == 0)
            throw new InvalidOperationException($"ResolveStrongholdBlueprintBounds failed: blueprint layer {layerName} has no positions.");

        return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
    }

    private static T GetRequiredFieldValue<T>(object instance, string fieldName)
    {
        if (instance == null)
            throw new InvalidOperationException($"GetRequiredFieldValue failed: instance is null for {fieldName}.");

        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException($"GetRequiredFieldValue failed: field {fieldName} not found on {instance.GetType().FullName}.");

        object value = field.GetValue(instance);
        if (value == null)
            return default;
        if (value is T typed)
            return typed;

        throw new InvalidOperationException($"GetRequiredFieldValue failed: field {fieldName} on {instance.GetType().FullName} is {value.GetType().FullName}, expected {typeof(T).FullName}.");
    }

    private static List<Vector3> ResolveLv3ClusterSpawnCandidates(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3[] unitSpawnCenters,
        System.Text.StringBuilder diagnostics)
    {
        if (grid == null)
            throw new InvalidOperationException("ResolveLv3ClusterSpawnCandidates failed: grid is null.");
        if (derivedData == null || !derivedData.IsValid)
            throw new InvalidOperationException("ResolveLv3ClusterSpawnCandidates failed: derivedData is invalid.");
        if (unitSpawnCenters == null || unitSpawnCenters.Length == 0)
            throw new InvalidOperationException("ResolveLv3ClusterSpawnCandidates failed: unitSpawnCenters is empty.");

        const float enemyPresetClusterRadius = 3f;
        const float enemyPresetClusterMinDistance = 1.2f;
        const int maxCountPerPreset = 6;
        const int samplesPerPreset = 48;
        List<Vector3> candidates = new List<Vector3>();
        for (int i = 0; i < unitSpawnCenters.Length; i++)
        {
            int before = candidates.Count;
            TryAddUniqueMainIslandCandidate(grid, derivedData, unitSpawnCenters[i], enemyPresetClusterMinDistance, candidates);

            for (int sample = 0; sample < samplesPerPreset && candidates.Count - before < maxCountPerPreset; sample++)
            {
                float angle = sample * 2.39996323f;
                float t = (sample + 0.5f) / samplesPerPreset;
                float radius = Mathf.Sqrt(t) * enemyPresetClusterRadius;
                Vector3 preferred = unitSpawnCenters[i] + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                TryAddUniqueMainIslandCandidate(grid, derivedData, preferred, enemyPresetClusterMinDistance, candidates);
            }

            diagnostics.Append("[Lv3Route] cluster-center-").Append(i)
                .Append(" center=").Append(unitSpawnCenters[i])
                .Append(" generated=").Append(candidates.Count - before)
                .AppendLine();
        }

        return candidates;
    }

    private static bool TryAddUniqueMainIslandCandidate(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 preferred,
        float minDistance,
        List<Vector3> candidates)
    {
        if (!TryResolveNearestMainIslandCell(grid, derivedData, preferred, 3.5f, out Vector3 resolved))
            return false;

        float minDistanceSq = minDistance * minDistance;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (HorizontalSqrMagnitude(candidates[i] - resolved) < minDistanceSq)
                return false;
        }

        candidates.Add(resolved);
        return true;
    }

    private static bool TryResolveNearestMainIslandCell(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 preferred,
        float maxRadius,
        out Vector3 result)
    {
        result = Vector3.zero;
        if (!grid.WorldToCell(preferred, out int originX, out int originY))
            return false;

        int maxRadiusCells = Mathf.CeilToInt(maxRadius / grid.CellSize);
        float bestDistance = float.PositiveInfinity;
        int bestX = -1;
        int bestY = -1;
        for (int radius = 0; radius <= maxRadiusCells; radius++)
        {
            for (int y = originY - radius; y <= originY + radius; y++)
                for (int x = originX - radius; x <= originX + radius; x++)
                {
                    if (Mathf.Abs(x - originX) != radius && Mathf.Abs(y - originY) != radius)
                        continue;
                    if (!IsMainIslandWalkable(grid, derivedData, x, y))
                        continue;

                    Vector3 candidate = grid.GetCellAnchor(x, y);
                    float distance = HorizontalSqrMagnitude(candidate - preferred);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestX = x;
                        bestY = y;
                    }
                }

            if (bestX >= 0)
                break;
        }

        if (bestX < 0)
            return false;

        result = grid.GetCellAnchor(bestX, bestY);
        return true;
    }

    private static Vector3 ResolveNearestMainIslandCell(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 preferred,
        float maxRadius,
        string label,
        System.Text.StringBuilder diagnostics)
    {
        if (!grid.WorldToCell(preferred, out int originX, out int originY))
            throw new InvalidOperationException($"ResolveNearestMainIslandCell failed: {label} preferred point {preferred} is outside grid.");

        int maxRadiusCells = Mathf.CeilToInt(maxRadius / grid.CellSize);
        float bestDistance = float.PositiveInfinity;
        int bestX = -1;
        int bestY = -1;
        for (int radius = 0; radius <= maxRadiusCells; radius++)
        {
            for (int y = originY - radius; y <= originY + radius; y++)
                for (int x = originX - radius; x <= originX + radius; x++)
                {
                    if (Mathf.Abs(x - originX) != radius && Mathf.Abs(y - originY) != radius)
                        continue;
                    if (!IsMainIslandWalkable(grid, derivedData, x, y))
                        continue;

                    Vector3 candidate = grid.GetCellAnchor(x, y);
                    float distance = HorizontalSqrMagnitude(candidate - preferred);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestX = x;
                        bestY = y;
                    }
                }

            if (bestX >= 0)
                break;
        }

        if (bestX < 0)
            throw new InvalidOperationException($"ResolveNearestMainIslandCell failed: no main-island walkable cell found for {label}, preferred={preferred}, maxRadius={maxRadius:F2}.");

        Vector3 result = grid.GetCellAnchor(bestX, bestY);
        diagnostics.Append("[Lv3Route] ").Append(label)
            .Append(" preferred=").Append(preferred)
            .Append(" resolved=").Append(result)
            .Append(" cell=(").Append(bestX).Append(',').Append(bestY).Append(')')
            .Append(" island=").Append(derivedData.IslandIds[bestX + bestY * grid.Width])
            .AppendLine();
        return result;
    }

    private static Vector3 ResolveRuntimeLegalLv3MainIslandCell(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 preferred,
        float maxRadius,
        float agentRadius,
        string label,
        System.Text.StringBuilder diagnostics)
    {
        if (!grid.WorldToCell(preferred, out int originX, out int originY))
            throw new InvalidOperationException($"ResolveRuntimeLegalLv3MainIslandCell failed: {label} preferred point {preferred} is outside grid.");

        int maxRadiusCells = Mathf.CeilToInt(maxRadius / grid.CellSize);
        float edgeClearance = Mathf.Max(0f, agentRadius - grid.CellSize * 0.2f);
        float bestDistance = float.PositiveInfinity;
        Vector3 best = Vector3.zero;
        int bestX = -1;
        int bestY = -1;
        for (int radius = 0; radius <= maxRadiusCells; radius++)
        {
            for (int y = originY - radius; y <= originY + radius; y++)
                for (int x = originX - radius; x <= originX + radius; x++)
                {
                    if (Mathf.Abs(x - originX) != radius && Mathf.Abs(y - originY) != radius)
                        continue;
                    if (!IsMainIslandWalkable(grid, derivedData, x, y))
                        continue;

                    Vector3 candidate = grid.GetCellAnchor(x, y);
                    if (!FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
                            candidate,
                            grid.AgentTypeId,
                            grid.CellSize * 0.75f,
                            edgeClearance,
                            out Vector3 legal))
                    {
                        continue;
                    }

                    if (!grid.WorldToCell(legal, out int legalX, out int legalY) || !IsMainIslandWalkable(grid, derivedData, legalX, legalY))
                        continue;

                    float distance = HorizontalSqrMagnitude(legal - preferred);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = legal;
                        bestX = legalX;
                        bestY = legalY;
                    }
                }

            if (bestX >= 0)
                break;
        }

        if (bestX < 0)
            throw new InvalidOperationException($"ResolveRuntimeLegalLv3MainIslandCell failed: no runtime legal main-island cell found for {label}, preferred={preferred}, maxRadius={maxRadius:F2}, clearance={edgeClearance:F3}.");

        bool hasClearance = FlowFieldCrowdMovementSystem.TryGetEditorTestNavigationClearance(
            best,
            edgeClearance,
            out bool isClear,
            out float violation,
            out int runtimeBoxCount,
            out int runtimeCircleCount);
        diagnostics.Append("[Lv3Route] ").Append(label)
            .Append(" preferred=").Append(preferred)
            .Append(" resolved=").Append(best)
            .Append(" cell=(").Append(bestX).Append(',').Append(bestY).Append(')')
            .Append(" island=").Append(derivedData.IslandIds[bestX + bestY * grid.Width])
            .Append(" runtimeClearance=").Append(hasClearance ? isClear.ToString() : "missing")
            .Append(" violation=").Append(hasClearance ? violation.ToString("F3") : "missing")
            .Append(" runtimeBoxes=").Append(hasClearance ? runtimeBoxCount.ToString() : "missing")
            .Append(" runtimeCircles=").Append(hasClearance ? runtimeCircleCount.ToString() : "missing")
            .AppendLine();
        return best;
    }

    private static Vector3 ResolveBestMainIslandCellInWorldBounds(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 preferred,
        Vector2 minWorld,
        Vector2 maxWorld,
        string label,
        System.Text.StringBuilder diagnostics)
    {
        grid.WorldToCell(new Vector3(minWorld.x, 0f, minWorld.y), out int minX, out int minY);
        grid.WorldToCell(new Vector3(maxWorld.x, 0f, maxWorld.y), out int maxX, out int maxY);
        minX = Mathf.Clamp(minX, 0, grid.Width - 1);
        maxX = Mathf.Clamp(maxX, 0, grid.Width - 1);
        minY = Mathf.Clamp(minY, 0, grid.Height - 1);
        maxY = Mathf.Clamp(maxY, 0, grid.Height - 1);

        float bestScore = float.PositiveInfinity;
        int bestX = -1;
        int bestY = -1;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                if (!IsMainIslandWalkable(grid, derivedData, x, y))
                    continue;

                Vector3 candidate = grid.GetCellAnchor(x, y);
                float score = HorizontalSqrMagnitude(candidate - preferred);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestX = x;
                    bestY = y;
                }
            }

        if (bestX < 0)
            throw new InvalidOperationException($"ResolveBestMainIslandCellInWorldBounds failed: no main-island walkable cell found for {label}, bounds=({minWorld})..({maxWorld}).");

        Vector3 result = grid.GetCellAnchor(bestX, bestY);
        diagnostics.Append("[Lv3Route] ").Append(label)
            .Append(" preferred=").Append(preferred)
            .Append(" resolved=").Append(result)
            .Append(" cell=(").Append(bestX).Append(',').Append(bestY).Append(')')
            .Append(" bounds=(").Append(minWorld).Append(")..(").Append(maxWorld).Append(')')
            .AppendLine();
        return result;
    }

    private static Vector3 RequireExactRuntimeLegalLv3Position(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 position,
        float agentRadius,
        string label,
        System.Text.StringBuilder diagnostics)
    {
        if (!grid.WorldToCell(position, out int cellX, out int cellY))
            throw new InvalidOperationException($"RequireExactRuntimeLegalLv3Position failed: {label} position {position} is outside grid.");
        if (!IsMainIslandWalkable(grid, derivedData, cellX, cellY))
            throw new InvalidOperationException($"RequireExactRuntimeLegalLv3Position failed: {label} cell=({cellX},{cellY}) is not main-island walkable.");
        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestNavigationClearance(
                position,
                agentRadius,
                out bool clear,
                out float violation,
                out int boxCount,
                out int circleCount))
        {
            throw new InvalidOperationException($"RequireExactRuntimeLegalLv3Position failed: {label} clearance query failed at {position}.");
        }
        diagnostics.Append("[Lv3ExactPosition] ").Append(label)
            .Append(" position=").Append(position)
            .Append(" cell=(").Append(cellX).Append(',').Append(cellY).Append(')')
            .Append(" clearance=").Append(agentRadius.ToString("F3"))
            .Append(" clear=").Append(clear)
            .Append(" violation=").Append(float.IsPositiveInfinity(violation) ? "INF" : violation.ToString("F3"))
            .Append(" boxes=").Append(boxCount)
            .Append(" circles=").Append(circleCount)
            .AppendLine();
        return position;
    }

    private static Vector3 RequireExactLv3TargetPosition(
        FlowNavigationGridAsset grid,
        Vector3 position,
        string label,
        System.Text.StringBuilder diagnostics)
    {
        if (!grid.WorldToCell(position, out int cellX, out int cellY))
            throw new InvalidOperationException($"RequireExactLv3TargetPosition failed: {label} position {position} is outside grid.");

        diagnostics.Append("[Lv3ExactTarget] ").Append(label)
            .Append(" position=").Append(position)
            .Append(" cell=(").Append(cellX).Append(',').Append(cellY).Append(')')
            .AppendLine();
        return position;
    }

    private static bool IsMainIslandWalkable(FlowNavigationGridAsset grid, FlowNavigationGridAsset.DerivedNavigationData derivedData, int x, int y)
    {
        if (x < 0 || x >= grid.Width || y < 0 || y >= grid.Height)
            return false;
        if (!grid.IsCellWalkable(x, y))
            return false;
        return derivedData.IslandIds[x + y * grid.Width] == derivedData.MainIslandId;
    }

    private static int ResolveRequiredLayer(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0)
            throw new InvalidOperationException($"Required Unity layer is missing: {layerName}.");
        return layer;
    }

    private static bool IsInsideLv3RightBottomCornerWindow(Vector3 position, Vector3 corner)
    {
        return position.x >= corner.x - 4.0f
               && position.x <= corner.x + 2.0f
               && position.z >= corner.z - 2.2f
               && position.z <= corner.z + 4.0f;
    }

    private static float HorizontalSqrMagnitude(Vector3 value)
    {
        return value.x * value.x + value.z * value.z;
    }

    private static void AppendCellDiagnostic(
        System.Text.StringBuilder builder,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 position,
        string label)
    {
        if (!grid.WorldToCell(position, out int x, out int y))
        {
            builder.Append(' ').Append(label).Append("Cell=outside");
            return;
        }

        int index = x + y * grid.Width;
        int island = derivedData != null && derivedData.IsValid && derivedData.IslandIds != null && index >= 0 && index < derivedData.IslandIds.Length
            ? derivedData.IslandIds[index]
            : -1;
        int sector = derivedData != null && derivedData.IsValid ? ResolveDerivedSectorId(derivedData, x, y) : -1;
        builder.Append(' ').Append(label)
            .Append("Cell=(").Append(x).Append(',').Append(y).Append(')')
            .Append("/walk=").Append(grid.IsCellWalkable(x, y))
            .Append("/island=").Append(island)
            .Append("/sector=").Append(sector);
    }

    private static void AppendControllerCombatTickFailureDiagnostics(
        System.Text.StringBuilder builder,
        Lv3CrowdRouteScenario scenario,
        int frame,
        int agentIndex,
        SimEntityContext chaser,
        SimEntityContext hero,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 chaserBefore,
        Vector3 heroPosition,
        int waypointIndex,
        int waypointCount,
        Lv3ResearchCenterFootprint researchCenter)
    {
        IEntityContext target = chaser?.TargetComp?.CurrentTarget;
        builder.Append("[Lv3ControllerCombatTickFailure] scenario=").Append(scenario.Name)
            .Append(" frame=").Append(frame)
            .Append(" agent=").Append(agentIndex)
            .Append(" targetIsHero=").Append(ReferenceEquals(target, hero))
            .Append(" targetNull=").Append(target == null)
            .Append(" waypoint=").Append(waypointIndex).Append('/').Append(Mathf.Max(0, waypointCount - 1))
            .Append(" chaserBefore=").Append(chaserBefore)
            .Append(" chaserNow=").Append(chaser != null ? chaser.Position : default)
            .Append(" hero=").Append(heroPosition);
        if (target != null)
        {
            builder.Append(" targetKey=").Append(target.CharacterKey)
                .Append(" targetSide=").Append(target.Side)
                .Append(" targetAlive=").Append(target.Alive)
                .Append(" targetPos=").Append(target.Position)
                .Append(" targetDist=").Append(chaser != null ? chaser.DistanceToTargetSurface(target).ToString("F3") : "NA");
        }

        builder.Append(" researchDist=").Append(researchCenter.DistanceToClosestBox(chaserBefore).ToString("F3"));
        AppendCellDiagnostic(builder, grid, derivedData, chaserBefore, "assetSelfBefore");
        AppendRuntimeCellDiagnostic(builder, grid, chaserBefore, "runtimeSelfBefore");
        AppendRuntimeNeighborhoodDiagnostic(builder, grid, chaserBefore, "runtimeSelfNeighborhood", 2);
        if (chaser != null)
        {
            AppendCellDiagnostic(builder, grid, derivedData, chaser.Position, "assetSelfNow");
            AppendRuntimeCellDiagnostic(builder, grid, chaser.Position, "runtimeSelfNow");
            AppendSteeringBreakdown(builder, chaser);
            AppendPathHandleDiagnostics(builder, chaser, grid, derivedData, heroPosition);
        }

        AppendCellDiagnostic(builder, grid, derivedData, heroPosition, "assetHero");
        AppendRuntimeCellDiagnostic(builder, grid, heroPosition, "runtimeHero");
        AppendRuntimeNeighborhoodDiagnostic(builder, grid, heroPosition, "runtimeHeroNeighborhood", 2);
        if (target != null && !ReferenceEquals(target, hero))
        {
            AppendCellDiagnostic(builder, grid, derivedData, target.Position, "assetTarget");
            AppendRuntimeCellDiagnostic(builder, grid, target.Position, "runtimeTarget");
            AppendRuntimeNeighborhoodDiagnostic(builder, grid, target.Position, "runtimeTargetNeighborhood", 2);
        }

        builder.AppendLine();
    }

    private static void AppendRuntimeCellDiagnostic(
        System.Text.StringBuilder builder,
        FlowNavigationGridAsset grid,
        Vector3 position,
        string label)
    {
        if (!grid.WorldToCell(position, out int x, out int y))
        {
            builder.Append(' ').Append(label).Append("=outside");
            return;
        }

        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestRuntimeCellDiagnostics(
                x,
                y,
                out bool walkable,
                out int islandId,
                out int islandCount,
                out int mainIslandId,
                out int mainIslandSize,
                out byte neighborTraversalMask))
        {
            builder.Append(' ').Append(label).Append("=unavailable cell=(").Append(x).Append(',').Append(y).Append(')');
            return;
        }

        builder.Append(' ').Append(label)
            .Append("Cell=(").Append(x).Append(',').Append(y).Append(')')
            .Append("/walk=").Append(walkable)
            .Append("/island=").Append(islandId)
            .Append("/islands=").Append(islandCount)
            .Append("/main=").Append(mainIslandId).Append(':').Append(mainIslandSize)
            .Append("/mask=0x").Append(neighborTraversalMask.ToString("X2"));
    }

    private static void AppendRuntimeNeighborhoodDiagnostic(
        System.Text.StringBuilder builder,
        FlowNavigationGridAsset grid,
        Vector3 position,
        string label,
        int radius)
    {
        if (!grid.WorldToCell(position, out int centerX, out int centerY))
        {
            builder.Append(' ').Append(label).Append("=outside");
            return;
        }

        builder.Append(' ').Append(label).Append('=');
        for (int y = centerY + radius; y >= centerY - radius; y--)
        {
            if (y < centerY + radius)
                builder.Append('|');
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (x > centerX - radius)
                    builder.Append(',');
                if (!FlowFieldCrowdMovementSystem.TryGetEditorTestRuntimeCellDiagnostics(
                        x,
                        y,
                        out bool walkable,
                        out int islandId,
                        out _,
                        out _,
                        out _,
                        out byte neighborTraversalMask))
                {
                    builder.Append("out");
                    continue;
                }

                builder.Append(walkable ? 'W' : 'B')
                    .Append(islandId)
                    .Append(':')
                    .Append(neighborTraversalMask.ToString("X2"));
            }
        }
    }

    private static void AppendCharacterControllerPhysicsDiagnostics(
        System.Text.StringBuilder builder,
        CharacterController controller,
        MoveExecutor executor)
    {
        if (controller == null)
        {
            builder.Append("/cc=null");
            return;
        }

        Vector3 requestedDisplacement = executor != null ? executor.DebugRequestedHorizontalDisplacement : Vector3.zero;
        requestedDisplacement.y = 0f;
        Vector3 constrainedDisplacement = executor != null ? executor.DebugConstrainedHorizontalDisplacement : Vector3.zero;
        constrainedDisplacement.y = 0f;
        Vector3 actual = executor != null ? executor.DebugActualHorizontalDisplacement : Vector3.zero;
        actual.y = 0f;
        builder.Append("/ccFlags=").Append(controller.collisionFlags)
            .Append("/ccGrounded=").Append(controller.isGrounded)
            .Append("/ccVelocity=").Append(controller.velocity)
            .Append("/ccRequestedDisp=").Append(requestedDisplacement)
            .Append("/ccConstrainedDisp=").Append(constrainedDisplacement)
            .Append("/ccActual=").Append(actual);

        if (executor != null)
        {
            builder.Append("/ccHit=")
                .Append(string.IsNullOrEmpty(executor.DebugLastControllerHitName) ? "none" : executor.DebugLastControllerHitName)
                .Append("/ccHitNormal=").Append(executor.DebugLastControllerHitNormal)
                .Append("/ccHitMoveDir=").Append(executor.DebugLastControllerHitMoveDirection)
                .Append("/ccVerticalDisp=").Append(executor.DebugVerticalDisplacement)
                .Append("/ccFinalDisp=").Append(executor.DebugFinalDisplacement);
        }

        AppendControllerOverlapDiagnostics(builder, controller);
        AppendControllerCastDiagnostics(builder, controller, constrainedDisplacement);
        if (executor != null)
            AppendControllerMoveProbeDiagnostics(builder, controller, constrainedDisplacement, executor.DebugVerticalDisplacement);
    }

    private static void AppendControllerCandidateProbeDiagnostics(
        System.Text.StringBuilder builder,
        CharacterController controller,
        SimEntityContext entity,
        float deltaTime)
    {
        if (controller == null || entity == null)
            return;

        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringDiagnostic(
                entity.LogicEntityId.Value,
                out Vector3 desiredVelocity,
                out Vector3 baseVelocity,
                out Vector3 resultPreClamp,
                out Vector3 result))
        {
            return;
        }

        float maxSpeed = Mathf.Max(desiredVelocity.magnitude, result.magnitude);

        builder.Append("/ccCandidateCasts=");
        bool first = true;
        AppendControllerCandidateCast(builder, controller, "result", result, deltaTime, maxSpeed, ref first);
        AppendControllerCandidateCast(builder, controller, "pre", resultPreClamp, deltaTime, maxSpeed, ref first);
        AppendControllerCandidateCast(builder, controller, "desired", desiredVelocity, deltaTime, maxSpeed, ref first);
        AppendControllerCandidateCast(builder, controller, "base", baseVelocity, deltaTime, maxSpeed, ref first);
        if (first)
            builder.Append("none");
    }

    private static void AppendControllerCandidateCast(
        System.Text.StringBuilder builder,
        CharacterController controller,
        string label,
        Vector3 velocity,
        float deltaTime,
        float maxSpeed,
        ref bool first)
    {
        velocity.y = 0f;
        if (velocity.sqrMagnitude <= 0.0001f || deltaTime <= 0f)
            return;

        Vector3 clampedVelocity = maxSpeed > 0.0001f
            ? Vector3.ClampMagnitude(velocity, maxSpeed)
            : velocity;
        Vector3 displacement = clampedVelocity * deltaTime;
        displacement.y = 0f;
        float distance = displacement.magnitude;
        if (distance <= 0.0001f)
            return;

        Vector3 direction = displacement / distance;
        Vector3 center = controller.transform.position + controller.center;
        float radius = Mathf.Max(0.01f, controller.radius - 0.01f);
        float half = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
        Vector3 top = center + Vector3.up * half;
        Vector3 bottom = center - Vector3.up * half;
        RaycastHit[] hits = Physics.CapsuleCastAll(bottom, top, radius, direction, distance + 0.08f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        if (!first)
            builder.Append('|');
        first = false;
        builder.Append(label)
            .Append(":vel=").Append(clampedVelocity)
            .Append(",disp=").Append(displacement)
            .Append(",hit=");

        int emitted = 0;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i].collider;
            if (hit == null || hit.transform == controller.transform)
                continue;

            if (emitted > 0)
                builder.Append('&');
            builder.Append(hit.gameObject.name)
                .Append('@')
                .Append(hits[i].distance.ToString("F3"));
            emitted++;
            if (emitted >= 3)
                break;
        }

        if (emitted == 0)
            builder.Append("none");
    }

    private static void AppendControllerMoveProbeDiagnostics(
        System.Text.StringBuilder builder,
        CharacterController source,
        Vector3 horizontalDisplacement,
        Vector3 verticalDisplacement)
    {
        bool sourceWasEnabled = source.enabled;
        source.enabled = false;
        GameObject probeObject = null;
        try
        {
            probeObject = new GameObject("FlowTest_CC_MoveProbe");
            probeObject.transform.position = source.transform.position;
            CharacterController probe = probeObject.AddComponent<CharacterController>();
            probe.radius = source.radius;
            probe.height = source.height;
            probe.center = source.center;
            probe.slopeLimit = source.slopeLimit;
            probe.stepOffset = source.stepOffset;
            probe.skinWidth = source.skinWidth;
            probe.minMoveDistance = source.minMoveDistance;
            probe.detectCollisions = source.detectCollisions;
            probe.enableOverlapRecovery = source.enableOverlapRecovery;
            ControllerHitRecorder recorder = probeObject.AddComponent<ControllerHitRecorder>();
            Physics.SyncTransforms();

            Vector3 start = probeObject.transform.position;
            CollisionFlags horizontalFlags = probe.Move(horizontalDisplacement);
            Vector3 horizontalActual = probeObject.transform.position - start;

            probe.enabled = false;
            probeObject.transform.position = source.transform.position;
            probe.enabled = true;
            Physics.SyncTransforms();
            start = probeObject.transform.position;
            CollisionFlags verticalFlags = probe.Move(verticalDisplacement);
            Vector3 verticalActual = probeObject.transform.position - start;

            probe.enabled = false;
            probeObject.transform.position = source.transform.position;
            probe.enabled = true;
            Physics.SyncTransforms();
            start = probeObject.transform.position;
            CollisionFlags combinedFlags = probe.Move(horizontalDisplacement + verticalDisplacement);
            Vector3 combinedActual = probeObject.transform.position - start;

            builder.Append("/ccProbeHFlags=").Append(horizontalFlags)
                .Append("/ccProbeHActual=").Append(horizontalActual)
                .Append("/ccProbeVFlags=").Append(verticalFlags)
                .Append("/ccProbeVActual=").Append(verticalActual)
                .Append("/ccProbeCFlags=").Append(combinedFlags)
                .Append("/ccProbeCActual=").Append(combinedActual)
                .Append("/ccProbeHit=")
                .Append(string.IsNullOrEmpty(recorder.LastHitName) ? "none" : recorder.LastHitName)
                .Append("/ccProbeHitNormal=").Append(recorder.LastHitNormal)
                .Append("/ccProbeHitMoveDir=").Append(recorder.LastHitMoveDirection);
        }
        finally
        {
            source.enabled = sourceWasEnabled;
            if (probeObject != null)
                UnityEngine.Object.DestroyImmediate(probeObject);
            Physics.SyncTransforms();
        }
    }

    private sealed class ControllerHitRecorder : MonoBehaviour
    {
        public string LastHitName { get; private set; }
        public Vector3 LastHitNormal { get; private set; }
        public Vector3 LastHitMoveDirection { get; private set; }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit == null || hit.collider == null)
                return;

            LastHitName = hit.collider.gameObject.name;
            LastHitNormal = hit.normal;
            LastHitMoveDirection = hit.moveDirection;
        }
    }

    private static void AppendControllerOverlapDiagnostics(System.Text.StringBuilder builder, CharacterController controller)
    {
        Vector3 center = controller.transform.position + controller.center;
        float radius = controller.radius + 0.03f;
        float half = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
        Vector3 top = center + Vector3.up * half;
        Vector3 bottom = center - Vector3.up * half;
        Collider[] overlaps = Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore);
        int emitted = 0;
        builder.Append("/ccOverlaps=");
        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider hit = overlaps[i];
            if (hit == null || hit.transform == controller.transform)
                continue;

            if (emitted > 0)
                builder.Append('|');
            builder.Append(hit.gameObject.name);
            emitted++;
            if (emitted >= 6)
                break;
        }

        if (emitted == 0)
            builder.Append("none");
    }

    private static void AppendControllerCastDiagnostics(
        System.Text.StringBuilder builder,
        CharacterController controller,
        Vector3 requestedDisplacement)
    {
        Vector3 direction = requestedDisplacement;
        direction.y = 0f;
        float distance = direction.magnitude;
        builder.Append("/ccCast=");
        if (distance <= 0.0001f)
        {
            builder.Append("none");
            return;
        }

        direction /= distance;
        Vector3 center = controller.transform.position + controller.center;
        float radius = Mathf.Max(0.01f, controller.radius - 0.01f);
        float half = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
        Vector3 top = center + Vector3.up * half;
        Vector3 bottom = center - Vector3.up * half;
        RaycastHit[] hits = Physics.CapsuleCastAll(bottom, top, radius, direction, distance + 0.08f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        int emitted = 0;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i].collider;
            if (hit == null || hit.transform == controller.transform)
                continue;

            if (emitted > 0)
                builder.Append('|');
            builder.Append(hit.gameObject.name)
                .Append('@')
                .Append(hits[i].distance.ToString("F3"));
            emitted++;
            if (emitted >= 6)
                break;
        }

        if (emitted == 0)
            builder.Append("none");
    }

    private static void AppendPathHandleDiagnostics(
        System.Text.StringBuilder builder,
        SimEntityContext chaser,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 goal)
    {
        if (chaser == null)
            throw new InvalidOperationException("AppendPathHandleDiagnostics failed: chaser is null.");
        if (!grid.WorldToCell(chaser.Position, out int startX, out int startY)
            || !grid.WorldToCell(goal, out int goalX, out int goalY))
        {
            builder.Append("/pathDiag=outside-grid");
            return;
        }

        int startSectorId = ResolveDerivedSectorId(derivedData, startX, startY);
        int goalSectorId = ResolveDerivedSectorId(derivedData, goalX, goalY);
        if (FlowFieldCrowdMovementSystem.TryGetEditorTestPathBuildSource(chaser.LogicEntityId.Value, out string buildSource))
            builder.Append("/pathSource=").Append(buildSource);
        if (FlowFieldCrowdMovementSystem.TryGetEditorTestPathSectorIds(chaser.LogicEntityId.Value, out int[] sectorIds))
            builder.Append("/sectors=").Append(string.Join(">", sectorIds));
        if (FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(chaser.LogicEntityId.Value, out int[] portalIds))
            builder.Append("/portals=").Append(string.Join(">", portalIds));
        AppendStableGoalDiagnostics(builder, chaser, grid, derivedData);
        AppendSelectedPortalGeometryDiagnostics(builder, chaser.Position, startSectorId, sectorIds, portalIds);
        builder.Append("/portalChoice=startSector=").Append(startSectorId)
            .Append(",goalSector=").Append(goalSectorId)
            .Append(",start=(").Append(startX).Append(',').Append(startY).Append(')')
            .Append(",goal=(").Append(goalX).Append(',').Append(goalY).Append(')');
    }

    private static void AppendStableGoalDiagnostics(
        System.Text.StringBuilder builder,
        SimEntityContext chaser,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData)
    {
        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestStableGoal(
                chaser.LogicEntityId.Value,
                out int targetId,
                out int rawX,
                out int rawY,
                out int stableX,
                out int stableY,
                out Vector3 stableWorld))
        {
            builder.Append("/stableGoal=missing");
            return;
        }

        builder.Append("/stableGoal=target:").Append(targetId)
            .Append(",raw=(").Append(rawX).Append(',').Append(rawY).Append(')')
            .Append(",rawWalk=").Append(grid.IsCellWalkable(rawX, rawY))
            .Append(",rawIsland=").Append(ResolveIslandForCell(derivedData, grid, rawX, rawY))
            .Append(",stable=(").Append(stableX).Append(',').Append(stableY).Append(')')
            .Append(",stableWalk=").Append(grid.IsCellWalkable(stableX, stableY))
            .Append(",stableIsland=").Append(ResolveIslandForCell(derivedData, grid, stableX, stableY))
            .Append(",world=").Append(stableWorld)
            .Append('/')
            .Append(FlowFieldCrowdMovementSystem.GetEditorTestMovingTargetAnchorDiagnostics(chaser.LogicEntityId.Value));
    }

    private static void AppendSegmentGridDiagnostics(
        System.Text.StringBuilder builder,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3ResearchCenterFootprint footprint,
        Vector3 from,
        string label,
        SimEntityContext chaser)
    {
        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringDiagnostic(
                chaser.LogicEntityId.Value,
                out _,
                out _,
                out _,
                out Vector3 steeringVelocity))
        {
            builder.Append('/').Append(label).Append("=steering-resolution-missing");
            return;
        }

        Vector3 delta = steeringVelocity;
        delta.y = 0f;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            builder.Append('/').Append(label).Append("=zero");
            return;
        }

        Vector3 direction = delta / distance;
        float step = Mathf.Max(0.02f, grid.CellSize * 0.5f);
        int sampleCount = Mathf.Min(96, Mathf.CeilToInt(distance / step) + 1);
        builder.Append('/').Append(label).Append("=dist:").Append(distance.ToString("F3")).Append(",samples:[");
        int previousX = int.MinValue;
        int previousY = int.MinValue;
        for (int i = 0; i < sampleCount; i++)
        {
            float t = Mathf.Min(distance, i * step);
            Vector3 sample = from + direction * t;
            if (!grid.WorldToCell(sample, out int x, out int y))
            {
                builder.Append("(outside@").Append(t.ToString("F2")).Append(')');
                continue;
            }

            if (x == previousX && y == previousY)
                continue;

            previousX = x;
            previousY = y;
            builder.Append('(')
                .Append(x).Append(',').Append(y)
                .Append("@").Append(t.ToString("F2"))
                .Append("/walk=").Append(grid.IsCellWalkable(x, y))
                .Append("/island=").Append(ResolveIslandForCell(derivedData, grid, x, y))
                .Append("/insideBox=").Append(footprint.ContainsPoint(sample))
                .Append(')');
        }

        builder.Append(']');
    }

    private static bool SegmentEntersFootprint(
        Lv3ResearchCenterFootprint footprint,
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        float step)
    {
        if (direction.sqrMagnitude <= 0.0001f || maxDistance <= 0f)
            return false;

        direction.y = 0f;
        direction.Normalize();
        step = Mathf.Max(0.01f, step);
        int sampleCount = Mathf.CeilToInt(maxDistance / step);
        for (int i = 0; i <= sampleCount; i++)
        {
            float distance = Mathf.Min(maxDistance, i * step);
            if (footprint.ContainsPoint(origin + direction * distance))
                return true;
        }

        return false;
    }

    private static bool SegmentViolatesFootprintClearance(
        Lv3ResearchCenterFootprint footprint,
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        float step,
        float clearance)
    {
        if (direction.sqrMagnitude <= 0.0001f || maxDistance <= 0f)
            return false;

        direction.y = 0f;
        direction.Normalize();
        step = Mathf.Max(0.01f, step);
        float clampedClearance = Mathf.Max(0f, clearance);
        int sampleCount = Mathf.CeilToInt(maxDistance / step);
        for (int i = 0; i <= sampleCount; i++)
        {
            float distance = Mathf.Min(maxDistance, i * step);
            if (footprint.DistanceToClosestBox(origin + direction * distance) < clampedClearance)
                return true;
        }

        return false;
    }

    private static bool SteeringTileTargetSegmentViolatesFootprintClearance(
        Lv3ResearchCenterFootprint footprint,
        Vector3 origin,
        SimEntityContext chaser,
        float step,
        float clearance)
    {
        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringDiagnostic(
                chaser.LogicEntityId.Value,
                out _,
                out _,
                out _,
                out Vector3 steeringVelocity))
        {
            return false;
        }

        Vector3 delta = steeringVelocity;
        delta.y = 0f;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
            return false;

        return SegmentViolatesFootprintClearance(
            footprint,
            origin,
            delta / distance,
            distance,
            step,
            clearance);
    }

    private static int ResolveIslandForCell(
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        FlowNavigationGridAsset grid,
        int x,
        int y)
    {
        if (derivedData == null || !derivedData.IsValid || derivedData.IslandIds == null)
            return -1;
        if (x < 0 || x >= grid.Width || y < 0 || y >= grid.Height)
            return -1;

        int index = x + y * grid.Width;
        return index >= 0 && index < derivedData.IslandIds.Length ? derivedData.IslandIds[index] : -1;
    }

    private static void AppendSelectedPortalGeometryDiagnostics(
        System.Text.StringBuilder builder,
        Vector3 position,
        int startSectorId,
        int[] sectorIds,
        int[] portalIds)
    {
        if (sectorIds == null || portalIds == null || portalIds.Length == 0)
        {
            builder.Append("/selectedPortal=missing");
            return;
        }

        int sectorIndex = -1;
        for (int i = 0; i < sectorIds.Length; i++)
        {
            if (sectorIds[i] == startSectorId)
            {
                sectorIndex = i;
                break;
            }
        }

        if (sectorIndex < 0 || sectorIndex >= portalIds.Length)
        {
            builder.Append("/selectedPortal=not-in-current-sector");
            return;
        }

        int portalId = portalIds[sectorIndex];
        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestPortalSummary(
                portalId,
                out int sectorAId,
                out int sectorBId,
                out int widthCells,
                out bool isVerticalBoundary,
                out Vector3 center))
        {
            builder.Append("/selectedPortal=").Append(portalId).Append(":summary-missing");
            return;
        }

        Vector3 toPortal = center - position;
        toPortal.y = 0f;
        builder.Append("/selectedPortal=").Append(portalId)
            .Append(",sectors=").Append(sectorAId).Append('>').Append(sectorBId)
            .Append(",width=").Append(widthCells)
            .Append(",vertical=").Append(isVerticalBoundary)
            .Append(",center=").Append(center)
            .Append(",toPortal=").Append(toPortal.sqrMagnitude > 0.0001f ? toPortal.normalized : Vector3.zero);
    }

    private static bool TryResolveSelectedPortalCenter(SimEntityContext chaser, int startSectorId, out int portalId, out Vector3 center)
    {
        portalId = -1;
        center = Vector3.zero;
        if (chaser == null)
            throw new InvalidOperationException("TryResolveSelectedPortalCenter failed: chaser is null.");
        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestPathSectorIds(chaser.LogicEntityId.Value, out int[] sectorIds)
            || !FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(chaser.LogicEntityId.Value, out int[] portalIds))
        {
            return false;
        }

        int sectorIndex = -1;
        for (int i = 0; i < sectorIds.Length; i++)
        {
            if (sectorIds[i] == startSectorId)
            {
                sectorIndex = i;
                break;
            }
        }

        if (sectorIndex < 0 || sectorIndex >= portalIds.Length)
            return false;

        portalId = portalIds[sectorIndex];
        return FlowFieldCrowdMovementSystem.TryGetEditorTestPortalSummary(
            portalId,
            out _,
            out _,
            out _,
            out _,
            out center);
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static void AppendLv3RouteMap(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3RightBottomRoute route,
        System.Text.StringBuilder diagnostics)
    {
        diagnostics.Append("[Lv3Route] heroStart=").Append(route.HeroStart)
            .Append(" lower=").Append(route.LowerApproach)
            .Append(" corner=").Append(route.RightBottomCorner)
            .Append(" return=").Append(route.ReturnPoint)
            .Append(" researchCenter=").Append(route.ResearchCenterPosition)
            .Append(" researchBounds=").Append(route.ResearchCenterBounds)
            .AppendLine();

        int minX;
        int minY;
        int maxX;
        int maxY;
        float minWorldX = Mathf.Min(route.ResearchCenterBounds.xMin - 8f, route.HeroStart.x - 4f, route.RightBottomCorner.x - 4f);
        float maxWorldX = Mathf.Max(route.ResearchCenterBounds.xMax + 8f, route.HeroStart.x + 4f, route.RightBottomCorner.x + 4f);
        float minWorldZ = Mathf.Min(route.ResearchCenterBounds.yMin - 5f, route.HeroStart.z - 4f, route.RightBottomCorner.z - 4f);
        float maxWorldZ = Mathf.Max(route.ResearchCenterBounds.yMax + 6f, route.HeroStart.z + 4f, route.RightBottomCorner.z + 4f);
        grid.WorldToCell(new Vector3(minWorldX, 0f, minWorldZ), out minX, out minY);
        grid.WorldToCell(new Vector3(maxWorldX, 0f, maxWorldZ), out maxX, out maxY);
        minX = Mathf.Clamp(minX, 0, grid.Width - 1);
        maxX = Mathf.Clamp(maxX, 0, grid.Width - 1);
        minY = Mathf.Clamp(minY, 0, grid.Height - 1);
        maxY = Mathf.Clamp(maxY, 0, grid.Height - 1);

        diagnostics.Append("[Lv3RouteMap] W=main w=other .=blocked step=8 cells").AppendLine();
        for (int y = maxY; y >= minY; y -= 8)
        {
            diagnostics.Append("z=").Append(grid.GetCellCenter(minX, y).z.ToString("F1")).Append(' ');
            for (int x = minX; x <= maxX; x += 8)
            {
                if (!grid.IsCellWalkable(x, y))
                {
                    diagnostics.Append('.');
                    continue;
                }

                diagnostics.Append(derivedData.IslandIds[x + y * grid.Width] == derivedData.MainIslandId ? 'W' : 'w');
            }

            diagnostics.AppendLine();
        }
    }

    private struct Lv3ResearchCenterFootprint
    {
        public Rect[] Boxes;

        public Rect CalculateBounds()
        {
            if (Boxes == null || Boxes.Length == 0)
                throw new InvalidOperationException("Lv3ResearchCenterFootprint.CalculateBounds failed: Boxes is empty.");

            float minX = Boxes[0].xMin;
            float maxX = Boxes[0].xMax;
            float minZ = Boxes[0].yMin;
            float maxZ = Boxes[0].yMax;
            for (int i = 1; i < Boxes.Length; i++)
            {
                minX = Mathf.Min(minX, Boxes[i].xMin);
                maxX = Mathf.Max(maxX, Boxes[i].xMax);
                minZ = Mathf.Min(minZ, Boxes[i].yMin);
                maxZ = Mathf.Max(maxZ, Boxes[i].yMax);
            }

            return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
        }

        public bool RayHitsAnyBox(Vector3 origin, Vector3 direction, float maxDistance)
        {
            if (Boxes == null)
                throw new InvalidOperationException("Lv3ResearchCenterFootprint.RayHitsAnyBox failed: Boxes is null.");
            for (int i = 0; i < Boxes.Length; i++)
            {
                if (RayIntersectsRect(origin, direction, maxDistance, Boxes[i]))
                    return true;
            }

            return false;
        }

        public float DistanceToFirstRayHit(Vector3 origin, Vector3 direction, float maxDistance)
        {
            if (Boxes == null)
                throw new InvalidOperationException("Lv3ResearchCenterFootprint.DistanceToFirstRayHit failed: Boxes is null.");

            float best = float.PositiveInfinity;
            for (int i = 0; i < Boxes.Length; i++)
            {
                if (TryGetRayRectDistance(origin, direction, maxDistance, Boxes[i], out float distance))
                    best = Mathf.Min(best, distance);
            }

            return float.IsPositiveInfinity(best) ? 0f : best;
        }

        public float DistanceToClosestBox(Vector3 point)
        {
            if (Boxes == null)
                throw new InvalidOperationException("Lv3ResearchCenterFootprint.DistanceToClosestBox failed: Boxes is null.");

            float best = float.PositiveInfinity;
            for (int i = 0; i < Boxes.Length; i++)
            {
                Rect box = Boxes[i];
                float dx = Mathf.Max(box.xMin - point.x, 0f, point.x - box.xMax);
                float dz = Mathf.Max(box.yMin - point.z, 0f, point.z - box.yMax);
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }

            return float.IsPositiveInfinity(best) ? 0f : best;
        }

        public bool ContainsPoint(Vector3 point)
        {
            if (Boxes == null)
                throw new InvalidOperationException("Lv3ResearchCenterFootprint.ContainsPoint failed: Boxes is null.");

            for (int i = 0; i < Boxes.Length; i++)
            {
                Rect box = Boxes[i];
                if (point.x >= box.xMin
                    && point.x <= box.xMax
                    && point.z >= box.yMin
                    && point.z <= box.yMax)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static Lv3ResearchCenterFootprint CreateLv3ResearchCenterLv1Footprint(Vector3 root)
    {
        Rect[] boxes =
        {
            CreateWorldRect(root, new Vector2(-0.5143721f, 2.0f), new Vector2(5.143723f, 1.3333333f)),
            CreateWorldRect(root, new Vector2(0.51437247f, -1.9999996f), new Vector2(5.1437225f, 3.9999998f)),
            CreateWorldRect(root, new Vector2(0.00000023841858f, 0.66666687f), new Vector2(4.1149783f, 1.3333333f)),
            CreateWorldRect(root, new Vector2(-0.51437217f, 3.3333335f), new Vector2(1.0287446f, 1.3333333f)),
        };

        return new Lv3ResearchCenterFootprint { Boxes = boxes };
    }

    private static void RegisterLv3ResearchCenterFootprintObstacles(Lv3ResearchCenterFootprint footprint, int obstacleIdBase)
    {
        if (footprint.Boxes == null || footprint.Boxes.Length == 0)
            throw new InvalidOperationException("RegisterLv3ResearchCenterFootprintObstacles failed: footprint boxes are empty.");

        for (int i = 0; i < footprint.Boxes.Length; i++)
        {
            Rect box = footprint.Boxes[i];
            Vector3 center = new Vector3(box.center.x, 0f, box.center.y);
            Vector3 halfExtents = new Vector3(box.width * 0.5f, 0f, box.height * 0.5f);
            FlowFieldCrowdMovementSystem.RegisterBoxObstacle(obstacleIdBase + i, center, halfExtents);
        }
    }

    private static Rect CreateWorldRect(Vector3 root, Vector2 localCenter, Vector2 size)
    {
        return new Rect(
            root.x + localCenter.x - size.x * 0.5f,
            root.z + localCenter.y - size.y * 0.5f,
            size.x,
            size.y);
    }

    private static bool RayIntersectsRect(Vector3 origin, Vector3 direction, float maxDistance, Rect rect)
    {
        return TryGetRayRectDistance(origin, direction, maxDistance, rect, out _);
    }

    private static bool TryGetRayRectDistance(Vector3 origin, Vector3 direction, float maxDistance, Rect rect, out float distance)
    {
        distance = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        float originX = origin.x;
        float originZ = origin.z;
        float dirX = direction.x;
        float dirZ = direction.z;
        float tMin = 0f;
        float tMax = maxDistance;
        if (!ClipRayAxis(originX, dirX, rect.xMin, rect.xMax, ref tMin, ref tMax))
            return false;
        if (!ClipRayAxis(originZ, dirZ, rect.yMin, rect.yMax, ref tMin, ref tMax))
            return false;
        if (tMax < 0f || tMin > maxDistance)
            return false;

        distance = Mathf.Max(0f, tMin);
        return true;
    }

    private static bool ClipRayAxis(float origin, float direction, float min, float max, ref float tMin, ref float tMax)
    {
        if (Mathf.Abs(direction) < 0.00001f)
            return origin >= min && origin <= max;

        float inv = 1f / direction;
        float t1 = (min - origin) * inv;
        float t2 = (max - origin) * inv;
        if (t1 > t2)
        {
            float temp = t1;
            t1 = t2;
            t2 = temp;
        }

        tMin = Mathf.Max(tMin, t1);
        tMax = Mathf.Min(tMax, t2);
        return tMin <= tMax;
    }

    private sealed class TestPathOpenSet
    {
        private readonly List<TestPathNode> _heap = new List<TestPathNode>();

        public int Count => _heap.Count;

        public void Push(int index, float priority)
        {
            TestPathNode node = new TestPathNode(index, priority);
            _heap.Add(node);
            int child = _heap.Count - 1;
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (_heap[parent].Priority <= node.Priority)
                    break;
                _heap[child] = _heap[parent];
                child = parent;
            }

            _heap[child] = node;
        }

        public int Pop()
        {
            if (_heap.Count == 0)
                throw new InvalidOperationException("TestPathOpenSet.Pop failed: heap is empty.");

            int result = _heap[0].Index;
            TestPathNode tail = _heap[_heap.Count - 1];
            _heap.RemoveAt(_heap.Count - 1);
            if (_heap.Count == 0)
                return result;

            int parent = 0;
            while (true)
            {
                int left = parent * 2 + 1;
                if (left >= _heap.Count)
                    break;
                int right = left + 1;
                int child = right < _heap.Count && _heap[right].Priority < _heap[left].Priority ? right : left;
                if (_heap[child].Priority >= tail.Priority)
                    break;
                _heap[parent] = _heap[child];
                parent = child;
            }

            _heap[parent] = tail;
            return result;
        }
    }

    private struct TestPathNode
    {
        public readonly int Index;
        public readonly float Priority;

        public TestPathNode(int index, float priority)
        {
            Index = index;
            Priority = priority;
        }
    }

    private static void AppendSteeringBreakdown(System.Text.StringBuilder builder, SimEntityContext entity)
    {
        if (entity == null)
        {
            builder.Append("/diag=null-entity");
            return;
        }

        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringDiagnostic(
                entity.LogicEntityId.Value,
                out Vector3 desiredVelocity,
                out Vector3 baseVelocity,
                out Vector3 resultPreClamp,
                out Vector3 result))
        {
            builder.Append("/diag=none");
            return;
        }

        builder.Append("/desired=").Append(desiredVelocity)
            .Append("/base=").Append(baseVelocity)
            .Append("/pre=").Append(resultPreClamp)
            .Append("/result=").Append(result);

        if (FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringGoal(entity.LogicEntityId.Value, out Vector3 lastSteeringGoal, out int lastSteeringGoalFrame))
        {
            builder.Append("/steerGoal=").Append(lastSteeringGoal)
                .Append("/steerFrame=").Append(lastSteeringGoalFrame);
        }

        if (FlowFieldCrowdMovementSystem.TryGetEditorTestStableGoal(
                entity.LogicEntityId.Value,
                out int stableTargetId,
                out int stableRawX,
                out int stableRawY,
                out int stableX,
                out int stableY,
                out Vector3 stableWorld))
        {
            builder.Append("/stableTargetId=").Append(stableTargetId)
                .Append("/stableRaw=(").Append(stableRawX).Append(',').Append(stableRawY).Append(')')
                .Append("/stableCell=(").Append(stableX).Append(',').Append(stableY).Append(')')
                .Append("/stableWorld=").Append(stableWorld);
        }
        else
        {
            builder.Append("/stable=none");
        }

        if (FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
                entity.LogicEntityId.Value,
                out string deterministicFlowDiagnostic))
        {
            builder.Append("/fixedFlow={").Append(deterministicFlowDiagnostic).Append('}');
        }
    }

    private static void SimulateAgent(SimEntityContext ctx, Vector3 goal, int frames, float dt, float speed)
    {
        for (int frame = 1; frame <= frames; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, speed, out Vector3 velocity));
            ctx.Position = AdvanceTowardsGoal(ctx.Position, goal, velocity, dt);
        }
    }

    private static void AdvanceChasersWithNavigationConstraint(
        SimEntityContext[] chasers,
        Vector3 goal,
        int frame,
        float dt,
        float speed,
        float edgeBuffer,
        int width,
        int height,
        int[] consecutiveZeroFrames,
        int[] maxConsecutiveZeroFrames,
        Vector3[] lastVelocity,
        Vector3[] lastDesiredDisplacement,
        Vector3[] lastConstrainedDisplacement)
    {
        FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        for (int i = 0; i < chasers.Length; i++)
        {
            SimEntityContext chaser = chasers[i];
            if (chaser.MoveComp == null)
            {
                SimMoveComp moveComp = new SimMoveComp();
                moveComp.Init(chaser);
                chaser.MoveComp = moveComp;
            }

            if (chaser.MoveExecutor == null)
                chaser.MoveExecutor = new SimMoveExecutor { Position = chaser.Position };

            chaser.SyncPositionToExecutor();

            if (chaser.Brain is SoldierAIBrain brain)
                brain.Tick(chaser, (Fix64)dt);

            chaser.MoveComp.Move((Fix64)dt);
            ((SimMoveExecutor)chaser.MoveExecutor).Execute(dt);
            chaser.SyncPositionFromExecutor();

            if (FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringDiagnostic(
                    chaser.LogicEntityId.Value,
                    out Vector3 desiredVelocity,
                    out Vector3 baseVelocity,
                    out Vector3 resultPreClamp,
                    out Vector3 result))
            {
                lastVelocity[i] = result;
                lastDesiredDisplacement[i] = result * dt;
                lastConstrainedDisplacement[i] = result * dt;
            }
            else
            {
                lastVelocity[i] = Vector3.zero;
                lastDesiredDisplacement[i] = Vector3.zero;
                lastConstrainedDisplacement[i] = Vector3.zero;
            }

            bool projectedToZero = lastDesiredDisplacement[i].sqrMagnitude > 0.02f * 0.02f
                                   && lastConstrainedDisplacement[i].sqrMagnitude <= 0.02f * 0.02f;
            consecutiveZeroFrames[i] = projectedToZero ? consecutiveZeroFrames[i] + 1 : 0;
            if (consecutiveZeroFrames[i] > maxConsecutiveZeroFrames[i])
                maxConsecutiveZeroFrames[i] = consecutiveZeroFrames[i];
        }
    }

    private static void AdvanceChasersThroughRuntimeMoveChain(
        SimEntityContext[] chasers,
        int frame,
        float dt,
        int[] consecutiveZeroFrames,
        int[] maxConsecutiveZeroFrames,
        Vector3[] lastVelocity,
        Vector3[] lastDesiredDisplacement,
        Vector3[] lastConstrainedDisplacement)
    {
        FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
        for (int i = 0; i < chasers.Length; i++)
        {
            SimEntityContext chaser = chasers[i];
            Assert.IsNotNull(chaser.MoveComp, $"真实 Lv3 回归必须预先挂 CharacterMoveComp。agent={i}");
            Assert.IsInstanceOf<CharacterMoveComp>(chaser.MoveComp, $"真实 Lv3 回归不能使用 SimMoveComp 简化链路。agent={i}");

            if (chaser.TargetComp is SimTargetingComp targeting)
                targeting.UpdateTargeting((Fix64)dt);

            chaser.SyncPositionToExecutor();

            if (chaser.Brain is SoldierAIBrain brain)
                brain.Tick(chaser, (Fix64)dt);

            chaser.MoveComp.Move((Fix64)dt);
            ((SimMoveExecutor)chaser.MoveExecutor).Execute(dt);
            chaser.SyncPositionFromExecutor();

            if (chaser.MoveExecutor is SimMoveExecutor executor)
            {
                lastVelocity[i] = executor.LastFrameVelocity;
                lastDesiredDisplacement[i] = executor.LastDesiredDisplacement;
                lastConstrainedDisplacement[i] = executor.LastConstrainedDisplacement;
            }
            else
            {
                lastVelocity[i] = Vector3.zero;
                lastDesiredDisplacement[i] = Vector3.zero;
                lastConstrainedDisplacement[i] = Vector3.zero;
            }

            bool projectedToZero = lastDesiredDisplacement[i].sqrMagnitude > 0.02f * 0.02f
                                   && lastConstrainedDisplacement[i].sqrMagnitude <= 0.02f * 0.02f;
            consecutiveZeroFrames[i] = projectedToZero ? consecutiveZeroFrames[i] + 1 : 0;
            if (consecutiveZeroFrames[i] > maxConsecutiveZeroFrames[i])
                maxConsecutiveZeroFrames[i] = consecutiveZeroFrames[i];
        }
    }

    private static Vector3 AdvanceHeroWithNavigationConstraint(
        Vector3 position,
        Vector3 waypoint,
        float speed,
        float dt,
        int agentTypeId,
        float edgeClearance,
        System.Text.StringBuilder diagnostics)
    {
        Vector3 toWaypoint = waypoint - position;
        toWaypoint.y = 0f;
        if (toWaypoint.sqrMagnitude <= 0.0001f)
            return position;

        Vector3 desired = toWaypoint.normalized * Mathf.Min(speed * dt, toWaypoint.magnitude);
        if (!FlowFieldCrowdMovementSystem.TryConstrainNavigationDisplacement(
                position,
                desired,
                agentTypeId,
                edgeClearance,
                out Vector3 constrained))
        {
            Vector3 target = position + desired;
            bool hasStartClearance = FlowFieldCrowdMovementSystem.TryGetEditorTestNavigationClearance(
                position,
                edgeClearance,
                out bool startClear,
                out float startViolation,
                out int startBoxes,
                out int startCircles);
            bool hasTargetClearance = FlowFieldCrowdMovementSystem.TryGetEditorTestNavigationClearance(
                target,
                edgeClearance,
                out bool targetClear,
                out float targetViolation,
                out int targetBoxes,
                out int targetCircles);
            bool hasSegmentDiagnostics = FlowFieldCrowdMovementSystem.TryGetEditorTestNavigationSegmentDiagnostics(
                position,
                desired,
                agentTypeId,
                edgeClearance,
                out string segmentDiagnostics);
            diagnostics?.Append("[Lv3HeroRoute] navigation constraint failed pos=")
                .Append(position)
                .Append(" waypoint=").Append(waypoint)
                .Append(" desired=").Append(desired)
                .Append(" target=").Append(target)
                .Append(" startClear=").Append(hasStartClearance ? startClear.ToString() : "missing")
                .Append(" startViolation=").Append(hasStartClearance ? startViolation.ToString("F3") : "missing")
                .Append(" startBoxes=").Append(hasStartClearance ? startBoxes.ToString() : "missing")
                .Append(" startCircles=").Append(hasStartClearance ? startCircles.ToString() : "missing")
                .Append(" targetClear=").Append(hasTargetClearance ? targetClear.ToString() : "missing")
                .Append(" targetViolation=").Append(hasTargetClearance ? targetViolation.ToString("F3") : "missing")
                .Append(" targetBoxes=").Append(hasTargetClearance ? targetBoxes.ToString() : "missing")
                .Append(" targetCircles=").Append(hasTargetClearance ? targetCircles.ToString() : "missing")
                .Append(" segment=").Append(hasSegmentDiagnostics ? segmentDiagnostics : "missing")
                .AppendLine();
            return position;
        }

        return position + constrained;
    }

    private static void AdvanceChasersWithFlowSteering(
        SimEntityContext[] chasers,
        Vector3 goal,
        int frame,
        float dt,
        float speed,
        int[] consecutiveZeroFrames,
        int[] maxConsecutiveZeroFrames,
        Vector3[] lastVelocity,
        Vector3[] lastDesiredDisplacement,
        Vector3[] lastConstrainedDisplacement)
    {
        FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
        for (int i = 0; i < chasers.Length; i++)
        {
            SimEntityContext chaser = chasers[i];
            bool gotVelocity = FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, goal, speed, out Vector3 velocity);
            Assert.IsTrue(gotVelocity, $"真实 Lv3 回归必须能取得流场速度。agent={i} pos={chaser.Position} goal={goal}");

            Vector3 displacement = velocity * dt;
            chaser.Position += displacement;
            chaser.SyncPositionToExecutor();

            lastVelocity[i] = velocity;
            lastDesiredDisplacement[i] = displacement;
            lastConstrainedDisplacement[i] = displacement;

            bool projectedToZero = lastDesiredDisplacement[i].sqrMagnitude > 0.02f * 0.02f
                                   && lastConstrainedDisplacement[i].sqrMagnitude <= 0.02f * 0.02f;
            consecutiveZeroFrames[i] = projectedToZero ? consecutiveZeroFrames[i] + 1 : 0;
            if (consecutiveZeroFrames[i] > maxConsecutiveZeroFrames[i])
                maxConsecutiveZeroFrames[i] = consecutiveZeroFrames[i];
        }
    }

    private static Vector3 AdvanceTowardsGoal(Vector3 position, Vector3 goal, Vector3 velocity, float dt)
    {
        Vector3 displacement = velocity * dt;
        Vector3 toGoal = goal - position;
        toGoal.y = 0f;
        Vector3 horizontalDisplacement = new Vector3(displacement.x, 0f, displacement.z);
        if (toGoal.sqrMagnitude > 0.0001f && horizontalDisplacement.sqrMagnitude > 0.0001f)
        {
            Vector3 goalDir = toGoal.normalized;
            float forwardDistance = Vector3.Dot(horizontalDisplacement, goalDir);
            if (forwardDistance > toGoal.magnitude)
            {
                horizontalDisplacement = goalDir * toGoal.magnitude;
                displacement = new Vector3(horizontalDisplacement.x, displacement.y, horizontalDisplacement.z);
            }
        }

        return position + displacement;
    }

    private static Vector3 AdvanceWithinBounds(Vector3 position, Vector3 goal, Vector3 velocity, float dt, int width, int height)
    {
        Vector3 next = AdvanceTowardsGoal(position, goal, velocity, dt);
        float minX = 0.5f;
        float maxX = width - 0.5f;
        float minZ = 0.5f;
        float maxZ = height - 0.5f;
        next.x = Mathf.Clamp(next.x, minX, maxX);
        next.z = Mathf.Clamp(next.z, minZ, maxZ);
        return next;
    }

    private static void ProcessFlowTileBuildQueueUntilTileCount(int minTileCount)
    {
        for (int frame = 2; frame < 256 && FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount() < minTileCount; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        }
    }

    private static void ProcessRuntimeDirtyQueueUntilReady(int startFrame)
    {
        for (int i = 0; i < 512 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
        {
            int frame = startFrame + i;
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        }

        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "runtime dirty job 未在测试预算内完成。");
    }

    private static Vector3 ResolveDeterministicFlowVelocityAfterQueue(
        SimEntityContext ctx,
        Vector3 goal,
        float speed,
        int startFrame,
        out string diagnostic)
    {
        diagnostic = "unavailable";
        Vector3 velocity = Vector3.zero;
        for (int i = 0; i < 512; i++)
        {
            int frame = startFrame + i;
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, speed, out velocity));
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
                ctx.LogicEntityId.Value,
                out diagnostic));
            if (diagnostic.Contains("/cached=True/"))
            {
                Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, speed, out velocity));
                return velocity;
            }
        }

        Assert.Fail($"deterministic flow tile 未在测试预算内提交，diagnostic={diagnostic}");
        return velocity;
    }

    private static void ProcessFlowTileBuildQueueUntilTileReady(int worldX, int worldY)
    {
        for (int frame = 2; frame < 256; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            if (FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount() == 0)
                return;
        }

        Assert.Fail($"flow tile was not built for cell=({worldX},{worldY})");
    }

    private static void ProcessWorldBuildQueueUntilReady()
    {
        for (int i = 0; i < 2048 && (!FlowFieldCrowdMovementSystem.HasEditorTestWorld() || FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild()); i++)
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
    }

    private static void ProcessAllWorldBuildQueuesUntilReady()
    {
        FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
        for (int i = 0; i < 4096 && FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild(); i++)
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();

        Assert.IsFalse(
            FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild(),
            "多导航源测试必须在进入运行模拟前完成全部 world build。");
    }

    private static SimEntityContext CreateEntity(Vector3 position)
    {
        return CreateEntity(position, false);
    }

    private static SimEntityContext CreateEntity(Vector3 position, bool isLeader)
    {
        return CreateEntity(position, isLeader, 0);
    }

    private static SimEntityContext CreateEntity(Vector3 position, bool isLeader, int agentTypeId)
    {
        SimEntityContext ctx = new SimEntityContext
        {
            Position = position,
            Side = SideType.PlayerSide,
            Alive = true
        };
        ctx.SetProperty(CreatureMainProperty.Speed, (Fix64)40f);
        ctx.SetProperty(CreatureMainProperty.CollisionRadius, (Fix64)10f);
        ctx.WeaponComp = CreateTestWeaponComp((Fix64)0.75f);
        ctx.MoveExecutor = new SimMoveExecutor { Position = position };
        FlowFieldCrowdMovementSystem.RegisterAgentForEditorTest(ctx, 0.5f, agentTypeId);
        return ctx;
    }

    private static WeaponComp CreateTestWeaponComp(Fix64 worldRange)
    {
        var data = new WeaponData(
            WeaponType.Melee,
            Fix64.One,
            Fix64.One,
            DistanceUnitConverter.ConvertFromWorld(worldRange),
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.One,
            Fix64.Zero,
            Array.Empty<Fix64>());
        return new WeaponComp(data.ToWeapon("FlowFieldCrowdMovementSystemTests"));
    }

    private static SimEntityContext CreateEntity(Vector3 position, bool isLeader, int agentTypeId, float radius)
    {
        SimEntityContext ctx = CreateEntity(position, isLeader, agentTypeId);
        ctx.SetProperty(CreatureMainProperty.CollisionRadius, (Fix64)(radius / DistanceUnitConverter.DefaultDistanceConversionRate));
        FlowFieldCrowdMovementSystem.RegisterAgentForEditorTest(ctx, radius, agentTypeId);
        return ctx;
    }

    private sealed class BoxTargetEntityContext : SimEntityContext
    {
        private readonly FixVector2 m_HalfExtents;

        public BoxTargetEntityContext(Vector3 position, FixVector2 halfExtents)
        {
            Position = position;
            Side = SideType.EnemySide;
            Alive = true;
            m_HalfExtents = halfExtents;
        }

        public override LogicCombatShape CombatShape =>
            LogicCombatShape.AxisAlignedBox(PositionFixed, m_HalfExtents);
    }

    private static FlowFieldNavigationConfig CreateConfig()
    {
        FlowFieldNavigationConfig config = ScriptableObject.CreateInstance<FlowFieldNavigationConfig>();
        config.SectorSizeInCells = 4;
        config.PortalNarrowWidthCells = 1;
        config.FlowTileCacheLimit = 32;
        SetNavigationWorkQuotas(config, 1_000_000);
        config.DrawNavigationDebug = false;
        config.DrawFlowFieldDebug = false;
        return config;
    }

    private static void SetNavigationWorkQuotas(FlowFieldNavigationConfig config, int operationQuota)
    {
        config.WorldBuildOperationQuota = operationQuota;
        config.RuntimeRebuildOperationQuota = operationQuota;
        config.DeterministicFlowTileCommitQuota = operationQuota;
        config.SharedGoalBuildOperationQuota = operationQuota;
    }

    private static int CountIncrementalRuntimeDirtyCallsWithCpuDelay(int spinWaitIterations, out ulong contentHash)
    {
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        config.RuntimeRebuildOperationQuota = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        const int width = 64;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(
            9301,
            new Vector3(10.5f, 0f, 4.5f),
            new Vector3(0.49f, 0f, 0.49f));

        int calls = 0;
        while (FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty())
        {
            if (spinWaitIterations > 0)
                System.Threading.Thread.SpinWait(spinWaitIterations);
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
            calls++;
            if (calls > 10000)
                throw new InvalidOperationException("Incremental runtime dirty rebuild did not complete within the deterministic test guard.");
        }

        contentHash = FlowFieldCrowdMovementSystem.GetEditorTestCommittedWorldContentHash();
        return calls;
    }

    private static int CountWorldBuildCallsWithCpuDelay(int spinWaitIterations)
    {
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldNavigationConfig config = CreateConfig();
        SetNavigationWorkQuotas(config, 32);
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 48;
        const int height = 24;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        int calls = 0;
        while (!FlowFieldCrowdMovementSystem.HasEditorTestWorld())
        {
            if (spinWaitIterations > 0)
                System.Threading.Thread.SpinWait(spinWaitIterations);
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
            calls++;
            if (calls > 10_000)
                throw new InvalidOperationException("World build did not complete within the deterministic test guard.");
        }

        return calls;
    }

    private static void SetWalkable(bool[] walkable, int width, int x, int y)
    {
        walkable[x + y * width] = true;
    }
}
