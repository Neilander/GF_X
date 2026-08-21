using System;
using System.Collections.Generic;
using System.Reflection;
using AAAGame.Card;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class LogicMigrationAuthorityBoundaryTests
{
    private const string ProjectFlowConfigPath = "Assets/AAAGame/SOs/FlowFieldNavigationConfig.asset";

    [SetUp]
    public void SetUp()
    {
        if (LogicFrameRuntime.IsActive)
            throw new InvalidOperationException("LogicMigrationAuthorityBoundaryTests requires an inactive logic runtime.");
        EntityRegistry.Clear();
        FlowFieldCrowdMovementSystem.ResetAll();
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicFrameRuntime.IsActive)
            LogicFrameRuntime.End();
        EntityRegistry.Clear();
        FlowFieldCrowdMovementSystem.ResetAll();
        RestoreProjectFlowConfig();
    }

    private static void RestoreProjectFlowConfig()
    {
        FlowFieldNavigationConfig projectConfig = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowFieldNavigationConfig>(
            ProjectFlowConfigPath);
        if (projectConfig == null)
            throw new InvalidOperationException(
                $"LogicMigrationAuthorityBoundaryTests teardown failed: project flow config is missing at {ProjectFlowConfigPath}.");

        FlowFieldCrowdMovementSystem.SetConfig(projectConfig);
    }

    [Test]
    public void BrainFactory_RejectsUnknownBrainTypeInsteadOfCreatingPlayerBrain()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => BrainFactory.Create((BrainType)int.MaxValue, null, null));

        StringAssert.Contains("unsupported BrainType", exception.Message);
    }

    [TestCase(BrainType.EnemyAI)]
    [TestCase(BrainType.FriendlyAI)]
    public void BrainFactory_RejectsLegacySideSpecificBrainTypes(BrainType brainType)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => BrainFactory.Create(brainType, null, null));

        StringAssert.Contains("legacy BrainType", exception.Message);
        StringAssert.Contains("use SoldierAI", exception.Message);
    }

    [Test]
    public void InputManager_DoesNotExposeMissingModelAsEmptyLogicInput()
    {
        Assert.IsNull(
            typeof(InputManager).GetProperty("CurrentLogicInputFrame"),
            "Logic input consumers must bind InputModel directly and fail when its authority is missing.");
    }

    [Test]
    public void StrongholdDiagnosticsAndPresentation_UseLogicStrongholdAuthority()
    {
        string levelSource = ReadProjectSource("AAAGame/Scripts/Entity/LevelEntity.cs");
        string fogPresentationSource = ReadProjectSource("AAAGame/Scripts/Entity/LevelEntity.EnemyStrongholdFog.cs");
        string defendSource = ReadProjectSource("AAAGame/Scripts/GameClass/DefendPhaseRuntime.cs");

        Assert.That(levelSource, Does.Not.Contain("GetStrongholdAtWorldPosition"));
        Assert.That(levelSource, Does.Not.Contain("GetStrongholdAtGridPosition"));
        Assert.That(fogPresentationSource, Does.Contain("LogicStrongholdMap.TryResolveStrongholdId"));
        Assert.That(fogPresentationSource, Does.Not.Contain("GetStrongholdAtWorldPosition"));
        Assert.That(fogPresentationSource, Does.Not.Contain("GetStrongholdAtGridPosition"));
        Assert.That(defendSource, Does.Contain("LogicStrongholdMap.TryResolveStrongholdId"));
        Assert.That(defendSource, Does.Not.Contain("LevelEntity.GetStrongholdAtWorldPosition"));
        Assert.That(defendSource, Does.Not.Contain("LevelEntity.GetStrongholdAtGridPosition"));
    }

    [Test]
    public void LogicTick_DoesNotReimportMutableScriptableObjectConfiguration()
    {
        GroupMoveConfig groupConfig = ScriptableObject.CreateInstance<GroupMoveConfig>();
        FlowFieldNavigationConfig flowConfig = ScriptableObject.CreateInstance<FlowFieldNavigationConfig>();
        flowConfig.RequireAuthoredNavigationSource = false;
        FlowFieldCrowdMovementSystem.SetConfig(flowConfig);
        FlowFieldCrowdMovementSystem.ResetAll();

        var managerObject = new GameObject("FrozenLogicConfigManager");
        managerObject.SetActive(false);
        GroupMoveManager manager = managerObject.AddComponent<GroupMoveManager>();
        bool managerLifecycleStarted = false;
        SetPrivateField(manager, "_config", groupConfig);
        SetPrivateField(manager, "_flowFieldConfig", flowConfig);
        InvokePrivate(manager, "Awake");
        managerLifecycleStarted = true;
        Assert.AreSame(manager, GroupMoveManager.Instance);

        try
        {
            LogicFrameRuntime.Begin();
            LogicFrameRuntime.StartTimeline();

            flowConfig.FlowTileCacheLimit = 32;
            SetPrivateField(groupConfig, "EnemySoftReturnRatio", 0.2f);

            var brain = new SoldierAIBrain();
            MethodInfo getRatio = typeof(SoldierAIBrain).GetMethod(
                "GetSoftReturnRatio",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(getRatio);
            Assert.AreEqual(Fix64.FromRaw(2458), (Fix64)getRatio.Invoke(brain, null),
                "Soldier AI must consume the fixed config snapshot captured before logic ticks.");
            Assert.IsNull(typeof(GroupMoveManager).GetProperty("FlowFieldConfig"),
                "The mutable navigation asset must not remain exposed after its values are imported.");
        }
        finally
        {
            try
            {
                if (LogicFrameRuntime.IsActive)
                    LogicFrameRuntime.End();
            }
            finally
            {
                try
                {
                    if (managerLifecycleStarted)
                        InvokePrivate(manager, "OnDestroy");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(managerObject);
                    UnityEngine.Object.DestroyImmediate(groupConfig);
                    UnityEngine.Object.DestroyImmediate(flowConfig);
                }
            }
            Assert.IsFalse(GroupMoveManager.HasInstance);
        }
    }

    [Test]
    public void LvTestSprinterDiagnostic_DoesNotTreatOneCornerAndSameSideCorrectionAsOscillation()
    {
        Type diagnosticType = Type.GetType(
            "LvTestSprinterNarrowPathDiagnosticRunner+SprinterProbe, AAAGame.Scripts.Editor",
            throwOnError: true);
        MethodInfo countMethod = diagnosticType.GetMethod(
            "CountRapidRouteLateralAlternationsForTest",
            BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo gateMethod = diagnosticType.GetMethod(
            "HasRouteLateralOscillationForTest",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(countMethod);
        Assert.NotNull(gateMethod);

        var routeReferences = new[]
        {
            new FixVector2(Fix64.FromRaw(3736), Fix64.FromRaw(-1677)),
            new FixVector2(Fix64.FromRaw(3745), Fix64.FromRaw(-1657)),
            new FixVector2(Fix64.FromRaw(3782), Fix64.FromRaw(-1570)),
        };
        var observedDirections = new[]
        {
            new FixVector2(Fix64.FromRaw(4100), Fix64.FromRaw(-97)),
            new FixVector2(Fix64.FromRaw(3627), Fix64.FromRaw(-1922)),
            new FixVector2(Fix64.FromRaw(3789), Fix64.FromRaw(-1580)),
        };

        int rapidRouteAlternations = (int)countMethod.Invoke(
            null,
            new object[] { routeReferences, observedDirections });
        Assert.AreEqual(
            0,
            rapidRouteAlternations,
            "The captured 25-degree corridor corner followed by a 5-degree same-side correction is not a left-right route oscillation.");
        Assert.IsFalse(
            (bool)gateMethod.Invoke(null, new object[] { 0, 0, 1 }),
            "A model angular-velocity sign change must remain diagnostic evidence, not override route authority by itself.");
    }

    [Test]
    public void LvTestSprinterDiagnostic_RejectsRapidLeftRightLeftRouteCrossing()
    {
        Type diagnosticType = Type.GetType(
            "LvTestSprinterNarrowPathDiagnosticRunner+SprinterProbe, AAAGame.Scripts.Editor",
            throwOnError: true);
        MethodInfo countMethod = diagnosticType.GetMethod(
            "CountRapidRouteLateralAlternationsForTest",
            BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo gateMethod = diagnosticType.GetMethod(
            "HasRouteLateralOscillationForTest",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(countMethod);
        Assert.NotNull(gateMethod);

        var routeReferences = new[]
        {
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.One, Fix64.Zero),
        };
        var oscillatingDirections = new[]
        {
            new FixVector2(Fix64.FromRaw(4017), Fix64.FromRaw(802)),
            new FixVector2(Fix64.FromRaw(4017), Fix64.FromRaw(-802)),
            new FixVector2(Fix64.FromRaw(4017), Fix64.FromRaw(802)),
        };

        int rapidRouteAlternations = (int)countMethod.Invoke(
            null,
            new object[] { routeReferences, oscillatingDirections });
        Assert.AreEqual(1, rapidRouteAlternations);
        Assert.IsTrue(
            (bool)gateMethod.Invoke(null, new object[] { rapidRouteAlternations, 0, 0 }));
    }

    [Test]
    public void LogicEntityStatus_RejectsUnityViewLifetimeAsAuthority()
    {
        var viewObject = new GameObject("ViewLifetimeMustNotBeAuthority");
        MAEntity view = viewObject.AddComponent<MAEntity>();
        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => ((IEntityContext)view).IsDestroyed());
            StringAssert.Contains("Unity", exception.Message);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(viewObject);
        }
    }

    [Test]
    public void InGameValueMutation_RejectsMissingAuthoritativeModelInsteadOfReportingSuccess()
    {
        FieldInfo activeModelField = typeof(InGameDataModel).GetField(
            "s_ActiveModel",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(activeModelField);
        object previous = activeModelField.GetValue(null);
        activeModelField.SetValue(null, null);

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                InGameDataModel.GetValue(IngameValueType.Coin));
            Assert.Throws<InvalidOperationException>(() =>
                InGameDataModel.TryModifyValue(IngameValueType.Coin, 1, false));
            Assert.Throws<InvalidOperationException>(() =>
                InGameDataModel.EnsureProductionBuildingCoinReserves("missing-model-building"));
            Assert.Throws<InvalidOperationException>(() =>
                InGameDataModel.RecordBuildingPhaseModification("missing-model-building", "Buil_Test_Lv0", 1));
            Assert.Throws<InvalidOperationException>(() =>
                InGameDataModel.RefreshCurrentSupplyFromFriendlyUnits(false));
            Assert.Throws<InvalidOperationException>(() =>
                InGameDataModel.GetStrongholds());
            Assert.Throws<InvalidOperationException>(() =>
                InGameDataModel.SetStrongholds(null));
            Assert.Throws<InvalidOperationException>(() =>
                InGameDataModel.RegisterBuilding(null));
            Assert.Throws<InvalidOperationException>(() =>
                InGameDataModel.UnregisterBuilding(null));
            Assert.Throws<InvalidOperationException>(() =>
                InGameDataModel.ClearStrongholdRuntimeData());
            Assert.Throws<InvalidOperationException>(() =>
                InGameDataModel.GetUnlockedTechIdsForBuilding("missing-model-building"));
            Assert.Throws<InvalidOperationException>(() =>
                InGameDataModel.HasUnlockedTech("missing-model-tech", EntitySideHelper.PlayerFactionId));
        }
        finally
        {
            activeModelField.SetValue(null, previous);
        }
    }

    [Test]
    public void SupplyTracking_UsesPureLogicSpawnAndDespawnWithoutEntityViews()
    {
        InGameDataModel model = LogicTestInGameDataModelAuthority.Ensure(
            GamePhase.Defend,
            nameof(SupplyTracking_UsesPureLogicSpawnAndDespawnWithoutEntityViews));
        FieldInfo subscribedField = typeof(InGameDataModel).GetField(
            "m_SupplyEventsSubscribed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo subscribeMethod = typeof(InGameDataModel).GetMethod(
            "SubscribeSupplyTrackingEvents",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo unsubscribeMethod = typeof(InGameDataModel).GetMethod(
            "UnsubscribeSupplyTrackingEvents",
            BindingFlags.Instance | BindingFlags.NonPublic);
        PropertyInfo characterDataProperty = typeof(SimEntityContext).GetProperty(
            nameof(SimEntityContext.CharacterData));
        Assert.NotNull(subscribedField);
        Assert.NotNull(subscribeMethod);
        Assert.NotNull(unsubscribeMethod);
        Assert.NotNull(characterDataProperty);
        MethodInfo characterDataSetter = characterDataProperty.GetSetMethod(true);
        Assert.NotNull(characterDataSetter);

        CharacterDataDetail[] rows = LoadCharacterRows();
        CharacterDataDetail supplyRow = System.Array.Find(rows, row => row.Supply > 0);
        Assert.NotNull(supplyRow, "Character data must contain at least one unit with positive supply.");
        bool wasSubscribed = (bool)subscribedField.GetValue(model);
        var friendly = new SimEntityContext
        {
            LogicEntityId = new LogicEntityId(71001),
            Side = SideType.PlayerSide,
        };
        var enemy = new SimEntityContext
        {
            LogicEntityId = new LogicEntityId(71002),
            Side = SideType.EnemySide,
        };
        var friendlyBuffs = new AAAGame.Scripts.BuffSystem.CharacterBuffComp();
        friendly.BuffComp = friendlyBuffs;
        friendlyBuffs.Init(friendly);
        var enemyBuffs = new AAAGame.Scripts.BuffSystem.CharacterBuffComp();
        enemy.BuffComp = enemyBuffs;
        enemyBuffs.Init(enemy);
        characterDataSetter.Invoke(friendly, new object[] { supplyRow });
        characterDataSetter.Invoke(enemy, new object[] { supplyRow });

        try
        {
            subscribeMethod.Invoke(model, null);
            Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(friendly.GetType()));
            Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(enemy.GetType()));

            EntityRegistry.Register(friendly);
            Assert.AreEqual(supplyRow.Supply, InGameDataModel.GetCurrentSupply());

            EntityRegistry.Register(enemy);
            Assert.AreEqual(supplyRow.Supply, InGameDataModel.GetCurrentSupply());

            EntityRegistry.Unregister(friendly);
            Assert.AreEqual(0, InGameDataModel.GetCurrentSupply());
        }
        finally
        {
            EntityRegistry.Unregister(friendly);
            EntityRegistry.Unregister(enemy);
            if (!wasSubscribed && (bool)subscribedField.GetValue(model))
                unsubscribeMethod.Invoke(model, null);
        }
    }

    [Test]
    public void RuntimeShutdown_HidesViewsBeforeReleasingModelsAndEndingServices()
    {
        string procedureSource = ReadProjectSource("AAAGame/Scripts/Procedures/RuntimeProcedureBase.cs");
        int onLeave = procedureSource.IndexOf("protected override void OnLeave", StringComparison.Ordinal);
        int deactivate = procedureSource.IndexOf("LogicEntityLifecycleService.DeactivateAllForShutdown();", onLeave, StringComparison.Ordinal);
        int hideViews = procedureSource.IndexOf("GF.Entity.HideAllLoadedEntities();", deactivate, StringComparison.Ordinal);
        int releaseModels = procedureSource.IndexOf("m_RuntimeInitPipeline?.Shutdown();", hideViews, StringComparison.Ordinal);
        int endPresentation = procedureSource.IndexOf("ProjectilePresentationService.EndTimeline();", releaseModels, StringComparison.Ordinal);
        int endLifecycle = procedureSource.IndexOf("LogicEntityLifecycleService.EndTimeline();", endPresentation, StringComparison.Ordinal);

        Assert.That(onLeave, Is.GreaterThanOrEqualTo(0));
        Assert.That(deactivate, Is.GreaterThan(onLeave));
        Assert.That(hideViews, Is.GreaterThan(deactivate));
        Assert.That(releaseModels, Is.GreaterThan(hideViews));
        Assert.That(endPresentation, Is.GreaterThan(releaseModels));
        Assert.That(endLifecycle, Is.GreaterThan(endPresentation));

        string dataModelSource = ReadProjectSource("AAAGame/Scripts/Extension/DataModel/DataModelComponent.cs");
        Assert.That(dataModelSource, Does.Not.Contain("GFEventArgs.EventId"));
        Assert.That(dataModelSource, Does.Not.Contain("OnGFEventCallback"));
        Assert.That(dataModelSource, Does.Not.Contain("GameFrameworkEntry.Shutdown();"));
    }

    [Test]
    public void InGameSnapshots_RejectMissingCoreValueInsteadOfHashingItAsZero()
    {
        FieldInfo activeModelField = typeof(InGameDataModel).GetField(
            "s_ActiveModel",
            BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo valuesField = typeof(InGameDataModel).GetField(
            "m_IngameValue",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(activeModelField);
        Assert.NotNull(valuesField);
        object previous = activeModelField.GetValue(null);
        activeModelField.SetValue(null, null);

        try
        {
            var model = (InGameDataModel)Activator.CreateInstance(typeof(InGameDataModel), true);
            var values = new Dictionary<IngameValueType, int>
            {
                [IngameValueType.Phase] = (int)GamePhase.BuildBeforeInvade,
                [IngameValueType.Day] = 1,
                [IngameValueType.CurrentSupply] = 0,
                [IngameValueType.MaxSupply] = 10,
            };
            valuesField.SetValue(model, values);

            Assert.Throws<InvalidOperationException>(() =>
                InGameDataModel.WriteDeterministicState(new LogicStateHasher()));
            Assert.Throws<InvalidOperationException>(() =>
                InGameDataModel.CaptureStageCheckpointState());
        }
        finally
        {
            activeModelField.SetValue(null, previous);
        }
    }

    [Test]
    public void SkillRuntimeQueries_RejectMissingAuthoritativeModelInsteadOfReportingEmptyState()
    {
        FieldInfo activeModelField = typeof(SkillRuntimeDataModel).GetField(
            "s_ActiveModel",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(activeModelField);
        object previous = activeModelField.GetValue(null);
        activeModelField.SetValue(null, null);

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                SkillRuntimeDataModel.IsUnlocked("missing-model-skill"));
            Assert.Throws<InvalidOperationException>(() =>
                SkillRuntimeDataModel.GetLevel("missing-model-skill"));
            Assert.Throws<InvalidOperationException>(() =>
                SkillRuntimeDataModel.GetUnlockedSkills());
            Assert.Throws<InvalidOperationException>(() =>
                SkillRuntimeDataModel.WriteDeterministicState(new LogicStateHasher()));
        }
        finally
        {
            activeModelField.SetValue(null, previous);
        }
    }

    [Test]
    public void RejectedPlayerRegistration_DoesNotReplaceAuthoritativePlayer()
    {
        var original = new SimEntityContext { LogicEntityId = new LogicEntityId(1) };
        EntityRegistry.RegisterAsPlayer(original);
        var viewObject = new GameObject("RejectedPlayerViewMustNotCommit");
        MAEntity rejectedView = viewObject.AddComponent<MAEntity>();
        typeof(MAEntity).GetProperty(nameof(MAEntity.LogicEntityId))
            ?.SetValue(rejectedView, new LogicEntityId(2));

        try
        {
            Assert.Throws<InvalidOperationException>(() => EntityRegistry.RegisterAsPlayer(rejectedView));
            Assert.AreSame(original, EntityRegistry.Player,
                "A failed player registration must not partially commit the rejected view.");
            Assert.AreEqual(1, EntityRegistry.AllEntities.Count);
            Assert.AreSame(original, EntityRegistry.AllEntities[0]);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(viewObject);
        }
    }

    [Test]
    public void RemoveAllCurrentBattleTroops_DespawnsOnlyTroopsMarkedAtBattleSpawn()
    {
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        LogicEntityLifecycleService.BeginTimeline();
        LogicEntityState battleTroop = CreateConfiguredUnitState(
            UnitType.Unit_Sprinter.ToString(),
            LogicEntityLifetime.CurrentBattleTroop);
        LogicEntityState neutralCreature = CreateConfiguredUnitState("Future_Neutral_Merchant");
        LogicEntityState building = CreateConfiguredUnitState("Buil_Test");
        building.ConfigureBuilding(
            CreateTestBuildingData("Buil_Test"),
            "building-test",
            null,
            EntitySideHelper.EnemyFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false);

        try
        {
            LogicEntityLifecycleService.CommitPendingInitializationEntities();
            Assert.IsTrue(battleTroop.IsSpawnCommitted);
            Assert.IsTrue(neutralCreature.IsSpawnCommitted);
            Assert.IsTrue(building.IsSpawnCommitted);
            Assert.AreEqual(0, LogicEntityLifecycleService.BoundViewCount);

            SoldierFactory.RemoveAllCurrentBattleTroops();

            Assert.AreEqual(1, LogicEntityLifecycleService.Commands.Count);
            Assert.AreEqual(
                LogicEntityLifecycleCommandKind.DespawnRequested,
                LogicEntityLifecycleService.Commands[0].Kind);
            Assert.AreEqual(battleTroop.EntityId, LogicEntityLifecycleService.Commands[0].EntityId);
            Assert.IsTrue(EntityRegistry.AllEntities.Contains(neutralCreature));
            Assert.IsTrue(EntityRegistry.AllEntities.Contains(building));
        }
        finally
        {
            EntityRegistry.Clear();
            LogicEntityLifecycleService.EndTimeline();
            LogicPhaseCommandService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    [Test]
    public void FixedSurfaceDistanceOutsideTick_PreservesRawPrecision()
    {
        var self = new SimEntityContext
        {
            PositionFixed = new FixVector2(Fix64.FromRaw(123456789), Fix64.FromRaw(-98765432)),
        };
        var target = new SimEntityContext
        {
            PositionFixed = new FixVector2(Fix64.FromRaw(12345), Fix64.FromRaw(-67890)),
        };
        self.SetProperty(CreatureMainProperty.CollisionRadius, Fix64.Zero);
        target.SetProperty(CreatureMainProperty.CollisionRadius, Fix64.Zero);

        Fix64 expected = target.CombatShape.DistanceToSurface(self.PositionFixed);
        Fix64 actual = self.LogicFrameDistanceToTargetSurfaceFixed(target);

        Assert.AreEqual(expected.RawValue, actual.RawValue,
            "A fixed query must not round through a float wrapper outside a logic tick.");
    }

    [Test]
    public void GameStraightAndCombatDistances_UseXZPlaneAndIgnoreWorldHeightDifference()
    {
        var self = new SimEntityContext { Position = new Vector3(0f, 100f, 0f) };
        var target = new SimEntityContext { Position = new Vector3(3f, -100f, 4f) };
        self.SetProperty(CreatureMainProperty.CollisionRadius, Fix64.Zero);
        target.SetProperty(CreatureMainProperty.CollisionRadius, Fix64.Zero);

        Assert.Greater(Vector3.Distance(self.Position, target.Position), 200f,
            "Test setup requires a large spatial height difference.");
        Assert.AreEqual(((Fix64)5).RawValue, self.LogicFrameCenterDistanceFixed(target).RawValue,
            "Game straight-line distance must use the XZ plane.");
        Assert.AreEqual(((Fix64)5).RawValue, self.LogicFrameDistanceToTargetSurfaceFixed(target).RawValue,
            "Combat surface distance must use the XZ plane.");
        Assert.AreEqual(5f, self.DistanceToTargetSurface(target), 0.0001f,
            "Float-facing gameplay distance must preserve the same XZ-plane authority.");
    }

    [Test]
    public void UnityWorldVectorConversion_UsesHorizontalXZForFixedPosition()
    {
        FixVector2 converted = new Vector3(3f, 17f, 5f);

        Assert.AreEqual(((Fix64)3).RawValue, converted.x.RawValue);
        Assert.AreEqual(((Fix64)5).RawValue, converted.y.RawValue);
    }

    [Test]
    public void AuthorityConsumers_UseFixedAndLogicPhaseSources()
    {
        string techSource = ReadProjectSource("AAAGame/Scripts/Build/Tech/BuildingTechRuntimeEffect.cs");
        Assert.That(techSource, Does.Contain("hostEntity.HealthRatioFixed()"));
        Assert.That(techSource, Does.Not.Contain("(Fix64)hostEntity.HealthRatio()"));

        string tutorialSource = ReadProjectSource("AAAGame/Scripts/MeiyouUtility/TutorialManager.cs");
        string initializeTutorial = ExtractSourceBlock(
            tutorialSource,
            "private void InitializeTutorial()",
            "private void StartFirstDefense(");
        Assert.That(initializeTutorial, Does.Contain("LogicPhaseCommandService.GetRequiredCurrentPhase()"));
        Assert.That(initializeTutorial, Does.Not.Contain("PhaseManager.CurrentPhase"));

        string phaseSource = ReadProjectSource("AAAGame/Scripts/GameClass/PhaseManager.cs");
        string spawnEnemySoldiers = ExtractSourceBlock(
            phaseSource,
            "private static void SpawnEnemySoldiers()",
            "private static int CompareInvadeSpawnPlans(");
        Assert.That(spawnEnemySoldiers, Does.Contain("ClusterSpawnSystem.SpawnClusterFixed("));
        Assert.That(spawnEnemySoldiers, Does.Not.Contain("new Vector3((float)plan.Position.x"));

        string soldierFactorySource = ReadProjectSource("AAAGame/Scripts/Entity/SoldierFactory.cs");
        string removeAllSoldiers = ExtractSourceBlock(
            soldierFactorySource,
            "public static void RemoveAllCurrentBattleTroops()",
            "public static LogicEntityId ShowSoldierFixed(");
        Assert.That(removeAllSoldiers, Does.Contain("EntityRegistry.AllEntities"));
        Assert.That(removeAllSoldiers, Does.Not.Contain("GF.Entity"));
        Assert.That(removeAllSoldiers, Does.Not.Contain("SoldierEntity"));
    }

    [Test]
    public void ProductionFrameScope_CoversCommandsTickGameEndAndHash()
    {
        string source = ReadProjectSource("AAAGame/Scripts/Procedures/RuntimeProcedureBase.cs");
        string executeFrame = ExtractSourceBlock(
            source,
            "private static LogicGameplayStateDigest ExecuteLogicFrame(",
            "private void UpdateEditorStressLogicFrames(");

        int beginScope = executeFrame.IndexOf("LogicFrameRuntime.BeginFrameExecution(frame)", StringComparison.Ordinal);
        int beginCommands = executeFrame.IndexOf("LogicTimeControlService.BeginFrame(frame)", StringComparison.Ordinal);
        int tick = executeFrame.IndexOf("LogicFrameRuntime.Tick(frame)", StringComparison.Ordinal);
        int gameEnd = executeFrame.IndexOf("LogicGameEndService.ApplyFrame(frame)", StringComparison.Ordinal);
        int hash = executeFrame.IndexOf("LogicGameplayStateHasher.ComputeCurrentFrameDigest()", StringComparison.Ordinal);
        int endScope = executeFrame.LastIndexOf("LogicFrameRuntime.EndFrameExecution(frame)", StringComparison.Ordinal);

        Assert.That(beginScope, Is.GreaterThanOrEqualTo(0));
        Assert.That(beginCommands, Is.GreaterThan(beginScope));
        Assert.That(tick, Is.GreaterThan(beginCommands));
        Assert.That(gameEnd, Is.GreaterThan(tick));
        Assert.That(hash, Is.GreaterThan(gameEnd));
        Assert.That(endScope, Is.GreaterThan(hash));
    }

    [Test]
    public void TechEffectApply_ConsumesPreparedResolverWithoutDataTableReimport()
    {
        string source = ReadProjectSource("AAAGame/Scripts/Buff/GlobalBuffManager.cs");
        string prepareDependencies = ExtractSourceBlock(
            source,
            "public void PrepareRuntimeDependencies()",
            "private void OnEnable()");
        string applyTechEffect = ExtractSourceBlock(
            source,
            "private void ApplyTechEffect(LogicTechEffectCommand command)",
            "private bool TryInitializeScopeResolver()");
        string getRuntimeEffect = ExtractSourceBlock(
            source,
            "private BuildingTechRuntimeEffect GetBuildingTechRuntimeEffect()",
            "private static bool IsSyntheticRuntimeTechId(");

        Assert.IsFalse(typeof(UnityEngine.ScriptableObject).IsAssignableFrom(typeof(BuildingTechRuntimeEffect)));
        Assert.That(prepareDependencies, Does.Contain("new BuildingTechRuntimeEffect()"));
        Assert.That(applyTechEffect, Does.Contain("m_TechScopeResolver == null"));
        Assert.That(applyTechEffect, Does.Not.Contain("TryInitializeScopeResolver()"));
        Assert.That(applyTechEffect, Does.Not.Contain("GF.DataTable"));
        Assert.That(applyTechEffect, Does.Not.Contain("ScriptableObject"));
        Assert.That(getRuntimeEffect, Does.Not.Contain("new BuildingTechRuntimeEffect()"));
        Assert.That(getRuntimeEffect, Does.Contain("was not prepared"));
    }

    [Test]
    public void RuntimeEntityConfiguration_ConsumesPreparedDataTableSnapshot()
    {
        string[] authorityFiles =
        {
            "AAAGame/Scripts/Entity/LogicUnitConfigurator.cs",
            "AAAGame/Scripts/Entity/LogicBuildingConfigurator.cs",
            "AAAGame/Scripts/Entity/MAEntity.cs",
            "AAAGame/Scripts/Entity/SoldierFactory.cs",
            "AAAGame/Scripts/Property/CreaturePropertyManager.cs",
            "AAAGame/Scripts/GameClass/LogicBuildingProductionService.cs",
            "AAAGame/Scripts/GameClass/LevelSelectionService.cs",
            "AAAGame/Scripts/GameClass/LevelTagRuntime.cs",
            "AAAGame/Scripts/Build/BuildManager.cs",
            "AAAGame/Scripts/Build/Tech/BuildingTechRuntimeEffect.cs",
            "AAAGame/Scripts/Build/Tech/BuildingLevelTechRouting.cs",
            "AAAGame/Scripts/DataModel/Building/BuildingDataModel.cs",
            "AAAGame/Scripts/DataModel/Building/TechDataModel.cs",
            "AAAGame/Scripts/DataModel/Building/SkillDataModel.cs",
            "AAAGame/Scripts/UTManagers/GeneralSetup.cs",
        };

        for (int i = 0; i < authorityFiles.Length; i++)
        {
            string source = ReadProjectSource(authorityFiles[i]);
            Assert.That(source, Does.Contain("LogicRuntimeDataTableCache"), authorityFiles[i]);
            Assert.That(source, Does.Not.Contain("GF.DataTable"), authorityFiles[i]);
            Assert.That(source, Does.Not.Contain("GetDataTable<"), authorityFiles[i]);
        }

        string preload = ReadProjectSource("AAAGame/Scripts/Procedures/PreloadProcedure.cs");
        Assert.That(preload, Does.Contain("LogicRuntimeDataTableCache.PrepareRuntimeDependencies()"));

        string factoryHelper = ReadProjectSource("AAAGame/Scripts/GeneralCreature/FactoryHelper.cs");
        Assert.That(factoryHelper, Does.Not.Contain("public static void CreateAtkComp("));
        Assert.That(factoryHelper, Does.Not.Contain("public static void CreateMoveComp("));
        Assert.That(factoryHelper, Does.Not.Contain("public static void CreateSkillComp("));
        Assert.That(factoryHelper, Does.Not.Contain("public static void CreateTargetingComp("));
        Assert.That(factoryHelper, Does.Not.Contain("WrapWithCache"));
    }

    [Test]
    public void PreparedCharacterSnapshot_OwnsRuntimeIndexesAndPresentationQueriesOnlyConsumeThem()
    {
        CharacterDataDetail[] characterRows = LoadCharacterRows();
        LogicRuntimeDataTableCache.PrepareForEditorTests(
            characterRows,
            Array.Empty<BuildingTable>(),
            Array.Empty<LevelTagTable>());
        try
        {
            var mapper = new ArchetypeUnitTypeMapper();
            Assert.That(mapper.GetUnitTypes(Archetype.Coding), Does.Contain(UnitType.Unit_Intern));

            string mapperSource = ReadProjectSource("AAAGame/Scripts/Build/Tech/ArchetypeUnitTypeMapper.cs");
            Assert.That(mapperSource, Does.Contain("LogicRuntimeDataTableCache.CharacterRows"));
            Assert.That(mapperSource, Does.Not.Contain("GF.DataTable"));

            string indexSource = ReadProjectSource("AAAGame/Scripts/Build/Tech/TechScopeIndex.cs");
            Assert.That(indexSource, Does.Contain("LogicRuntimeDataTableCache.CharacterRows"));
            Assert.That(indexSource, Does.Not.Contain("GF.DataTable"));

            string[] presentationQueries =
            {
                "AAAGame/Scripts/Card/Data/CardData.cs",
                "AAAGame/Scripts/Card/UI/CardAreaMaterialOverlay.cs",
                "AAAGame/Scripts/UI/BuildingBuildTips.cs",
                "AAAGame/Scripts/UI/InGameUIForm.DefendEnemySketch.cs",
            };
            for (int i = 0; i < presentationQueries.Length; i++)
            {
                string presentationSource = ReadProjectSource(presentationQueries[i]);
                Assert.That(presentationSource, Does.Contain("LogicRuntimeDataTableCache"), presentationQueries[i]);
                Assert.That(presentationSource, Does.Not.Contain("GetDataTable<CharacterDataDetail>"), presentationQueries[i]);
            }

            string defendSource = ReadProjectSource("AAAGame/Scripts/GameClass/DefendPhaseRuntime.cs");
            string preview = ExtractSourceBlock(
                defendSource,
                "public static bool TryGetNextDefendPreviewSpawnEntries(",
                "public static int NavigationPathVersion");
            string pathQuery = ExtractSourceBlock(
                defendSource,
                "public static bool TryGetNavigationPathCorners(\r\n        UnitType unitType,\r\n        Vector3 spawnPosition,\r\n        Vector3 basePosition,\r\n        List<Vector3> pathCorners,\r\n        out string failureReason,\r\n        out bool navigationUpdatePending)",
                "private static void EnsureSubscribedSoldierDead()");
            Assert.That(preview, Does.Contain("RequirePreparedRuntime()"));
            Assert.That(preview, Does.Not.Contain("PrepareForCurrentLevelIfNeeded()"));
            Assert.That(pathQuery, Does.Contain("RequirePreparedRuntime()"));
            Assert.That(pathQuery, Does.Not.Contain("PrepareForCurrentLevelIfNeeded()"));

            string prepare = ExtractSourceBlock(
                defendSource,
                "public static void PrepareForCurrentLevelIfNeeded()",
                "public static void EnterDefendPhase()");
            Assert.That(prepare, Does.Contain("LogicFrameRuntime.IsExecutingFrame"));
        }
        finally
        {
            LogicRuntimeDataTableCache.ResetForEditorTests();
        }
    }

    [Test]
    public void PreparedCharacterSnapshot_DoesNotShareMutableSourceRows()
    {
        CharacterDataDetail[] characterRows = LoadCharacterRows();
        CharacterDataDetail intern = Array.Find(
            characterRows,
            row => string.Equals(row.CharacterKey, UnitType.Unit_Intern.ToString(), StringComparison.Ordinal));
        CharacterDataDetail rowWithValues = Array.Find(
            characterRows,
            row => row.UniqueValues != null && row.UniqueValues.Length > 0);
        Assert.NotNull(intern);
        Assert.NotNull(rowWithValues);
        UnitSize originalSize = intern.Size;
        long originalValue = rowWithValues.UniqueValues[0].RawValue;
        LogicRuntimeDataTableCache.PrepareForEditorTests(
            characterRows,
            Array.Empty<BuildingTable>(),
            Array.Empty<LevelTagTable>());

        try
        {
            CharacterDataDetail cachedIntern = LogicRuntimeDataTableCache.GetCharacterRequired(intern.CharacterKey);
            CharacterDataDetail cachedValues = LogicRuntimeDataTableCache.GetCharacterRequired(rowWithValues.CharacterKey);
            typeof(CharacterDataDetail).GetProperty(nameof(CharacterDataDetail.Size))
                ?.SetValue(intern, originalSize == UnitSize.SuperLarge ? UnitSize.Small : UnitSize.SuperLarge);
            rowWithValues.UniqueValues[0] = Fix64.FromRaw(checked(originalValue + 1));

            Assert.AreNotSame(intern, cachedIntern);
            Assert.AreEqual(originalSize, cachedIntern.Size);
            Assert.AreNotSame(rowWithValues.UniqueValues, cachedValues.UniqueValues);
            Assert.AreEqual(originalValue, cachedValues.UniqueValues[0].RawValue);
        }
        finally
        {
            LogicRuntimeDataTableCache.ResetForEditorTests();
        }
    }

    [Test]
    public void PreparedCharacterSnapshot_IssuesOwnedRowsToPresentationAndLogicConsumers()
    {
        CharacterDataDetail[] characterRows = LoadCharacterRows();
        CharacterDataDetail source = Array.Find(
            characterRows,
            row => row.UnitTags != null && row.UnitTags.Length > 0);
        Assert.NotNull(source);
        UnitTag originalTag = source.UnitTags[0];
        UnitTag replacementTag = Array.Find(
            (UnitTag[])Enum.GetValues(typeof(UnitTag)),
            value => value != originalTag);
        Assert.AreNotEqual(originalTag, replacementTag);
        LogicRuntimeDataTableCache.PrepareForEditorTests(
            characterRows,
            Array.Empty<BuildingTable>(),
            Array.Empty<LevelTagTable>());

        try
        {
            CharacterDataDetail presentationRow =
                LogicRuntimeDataTableCache.GetCharacterRequired(source.CharacterKey);
            CharacterDataDetail logicRow =
                LogicRuntimeDataTableCache.GetCharacterRequired(source.CharacterKey);

            presentationRow.UnitTags[0] = replacementTag;

            Assert.AreNotSame(presentationRow, logicRow);
            Assert.AreNotSame(presentationRow.UnitTags, logicRow.UnitTags);
            Assert.AreEqual(originalTag, logicRow.UnitTags[0]);
        }
        finally
        {
            LogicRuntimeDataTableCache.ResetForEditorTests();
        }
    }

    [Test]
    public void PreparedRuntimeSnapshots_DoNotShareMutableBuildingSkillOrLevelTagRows()
    {
        BuildingTable[] buildingRows = LoadBuildingRows();
        SkillTable[] skillRows = LoadSkillRows();
        LevelTagTable[] levelTagRows = LoadLevelTagRows();
        LevelTable[] levelRows = LoadLevelRows();
        BuildingTable building = Array.Find(
            buildingRows,
            row => row.UniqueValues != null && row.UniqueValues.Length > 0);
        SkillTable skill = Array.Find(
            skillRows,
            row => row.Lv1UniqueValues != null && row.Lv1UniqueValues.Length > 0);
        LevelTagTable levelTag = Array.Find(
            levelTagRows,
            row => row.UniqueValues != null && row.UniqueValues.Length > 0);
        LevelTable level = Array.Find(
            levelRows,
            row => !string.IsNullOrWhiteSpace(row.OptionalObjective1Identifier)
                   && row.OptionalObjective1UniqueValues != null
                   && row.OptionalObjective1UniqueValues.Length > 0);
        Assert.NotNull(building);
        Assert.NotNull(skill);
        Assert.NotNull(levelTag);
        Assert.NotNull(level);
        long buildingValue = building.UniqueValues[0].RawValue;
        long skillValue = skill.Lv1UniqueValues[0].RawValue;
        long levelTagValue = levelTag.UniqueValues[0].RawValue;
        long objectiveValue = level.OptionalObjective1UniqueValues[0].RawValue;

        LogicRuntimeDataTableCache.PrepareForEditorTests(
            Array.Empty<CharacterDataDetail>(),
            buildingRows,
            levelTagRows,
            skillRows,
            levelRows);
        try
        {
            BuildingTable cachedBuilding = LogicRuntimeDataTableCache.BuildingRows[Array.IndexOf(buildingRows, building)];
            SkillTable cachedSkill = LogicRuntimeDataTableCache.SkillRows[Array.IndexOf(skillRows, skill)];
            LevelTagTable cachedLevelTag = LogicRuntimeDataTableCache.LevelTagRows[Array.IndexOf(levelTagRows, levelTag)];
            LevelTable cachedLevel = LogicRuntimeDataTableCache.GetLevelRequired(level.Identifier);
            building.UniqueValues[0] = Fix64.FromRaw(checked(buildingValue + 1));
            skill.Lv1UniqueValues[0] = Fix64.FromRaw(checked(skillValue + 1));
            levelTag.UniqueValues[0] = Fix64.FromRaw(checked(levelTagValue + 1));
            level.OptionalObjective1UniqueValues[0] = Fix64.FromRaw(checked(objectiveValue + 1));
            LevelData levelData = LevelData.FromRow(cachedLevel);
            cachedLevel.OptionalObjective1UniqueValues[0] = Fix64.FromRaw(checked(objectiveValue + 2));

            Assert.AreNotSame(building, cachedBuilding);
            Assert.AreNotSame(building.UniqueValues, cachedBuilding.UniqueValues);
            Assert.AreEqual(buildingValue, cachedBuilding.UniqueValues[0].RawValue);
            Assert.AreNotSame(skill, cachedSkill);
            Assert.AreNotSame(skill.Lv1UniqueValues, cachedSkill.Lv1UniqueValues);
            Assert.AreEqual(skillValue, cachedSkill.Lv1UniqueValues[0].RawValue);
            Assert.AreNotSame(levelTag, cachedLevelTag);
            Assert.AreNotSame(levelTag.UniqueValues, cachedLevelTag.UniqueValues);
            Assert.AreEqual(levelTagValue, cachedLevelTag.UniqueValues[0].RawValue);
            Assert.AreNotSame(level, cachedLevel);
            Assert.AreNotSame(level.OptionalObjective1UniqueValues, cachedLevel.OptionalObjective1UniqueValues);
            Assert.AreEqual(objectiveValue + 2, cachedLevel.OptionalObjective1UniqueValues[0].RawValue);
            Assert.AreNotSame(cachedLevel.OptionalObjective1UniqueValues, levelData.OptionalObjectives[0].UniqueValues);
            Assert.AreEqual(objectiveValue, levelData.OptionalObjectives[0].UniqueValues[0].RawValue);
        }
        finally
        {
            LogicRuntimeDataTableCache.ResetForEditorTests();
        }
    }

    [Test]
    public void ImportedLogicData_OwnsMutableConfigurationArrays()
    {
        var values = new[] { (Fix64)1 };
        var upgradeIds = new[] { "Tech_A" };
        var unitScope = new[] { UnitType.Unit_Intern.ToString() };
        var sizeScope = new[] { UnitSize.Small };
        var tagScope = new[] { UnitTag.Creature };
        var archScope = new[] { Archetype.Coding };
        var weapon = new WeaponData(
            WeaponType.Melee,
            Fix64.One,
            Fix64.One,
            Fix64.One,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.One,
            Fix64.Zero,
            values);
        var building = new BuildingData(
            "Buil_Test_Lv1",
            BuilType.Def,
            Archetype.Coding,
            "Prefab",
            "Name",
            "Desc",
            1,
            1,
            Fix64.One,
            weapon,
            Fix64.Zero,
            values,
            null,
            0,
            upgradeIds);
        var tech = new TechData(
            "Tech_A",
            null,
            "Name",
            "Desc",
            1,
            values,
            TechScopeType.SpecificUnit,
            unitScope,
            sizeScope,
            tagScope,
            archScope,
            null,
            false);
        var skill = new SkillData(
            "Skill_A",
            values,
            Fix64.One,
            Fix64.One,
            Fix64.One,
            1,
            values,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            0,
            Fix64.One,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            SkillType.Active,
            "Name",
            "Desc",
            null);

        values[0] = (Fix64)2;
        upgradeIds[0] = "Tech_B";
        unitScope[0] = UnitType.Unit_Sprinter.ToString();
        sizeScope[0] = UnitSize.SuperLarge;
        tagScope[0] = UnitTag.Machanical;
        archScope[0] = Archetype.Sightseeing;

        Assert.AreEqual(((Fix64)1).RawValue, weapon.UniqueValues[0].RawValue);
        Assert.AreEqual(((Fix64)1).RawValue, building.UniqueValues[0].RawValue);
        Assert.AreEqual("Tech_A", building.UpgradeTechIDs[0]);
        Assert.AreEqual(((Fix64)1).RawValue, tech.UniqueValues[0].RawValue);
        Assert.AreEqual(UnitType.Unit_Intern.ToString(), tech.UnitScope[0]);
        Assert.AreEqual(UnitSize.Small, tech.SizeScope[0]);
        Assert.AreEqual(UnitTag.Creature, tech.TagScope[0]);
        Assert.AreEqual(Archetype.Coding, tech.ArchScope[0]);
        Assert.AreEqual(((Fix64)1).RawValue, skill.Lv1UniqueValues[0].RawValue);
        Assert.AreEqual(((Fix64)1).RawValue, skill.UpgradeIncrementUniqueValues[0].RawValue);
    }

    [Test]
    public void PreparedCharacterSnapshot_OwnsAgentTypeIndex()
    {
        CharacterDataDetail[] characterRows = LoadCharacterRows();
        CharacterDataDetail intern = Array.Find(
            characterRows,
            row => string.Equals(row.CharacterKey, UnitType.Unit_Intern.ToString(), StringComparison.Ordinal));
        Assert.NotNull(intern);
        typeof(CharacterDataDetail).GetProperty(nameof(CharacterDataDetail.Size))
            ?.SetValue(intern, UnitSize.SuperLarge);
        LogicRuntimeDataTableCache.PrepareForEditorTests(
            characterRows,
            Array.Empty<BuildingTable>(),
            Array.Empty<LevelTagTable>());

        try
        {
            AgentTypeHelper.PrepareRuntimeMappings();

            Assert.AreEqual(
                AgentTypeHelper.LargeMovementTypeId,
                AgentTypeHelper.ResolveNavAgentTypeId(UnitType.Unit_Intern),
                "Agent type indexing must consume the same prepared character snapshot as unit configuration.");
        }
        finally
        {
            ResetAgentTypeMappings();
            LogicRuntimeDataTableCache.ResetForEditorTests();
        }
    }

    [Test]
    public void LogicShapeCatalogs_DoNotFirstLoadResourcesInsideLogicFrame()
    {
        ResetStaticField(typeof(BuildingCombatShapeCatalog), "s_Cached");
        ResetStaticField(typeof(BuildingLogicObstacleShapeCatalog), "s_Cached");
        ResetStaticField(typeof(CardStaticForbiddenShapeCatalog), "s_Cached");
        LogicFrameRuntime.Begin();
        LogicFrameRuntime.StartTimeline();
        LogicFrameRuntime.BeginFrameExecution(1);
        try
        {
            Assert.Throws<InvalidOperationException>(() => BuildingCombatShapeCatalog.LoadRequired());
            Assert.Throws<InvalidOperationException>(() => BuildingLogicObstacleShapeCatalog.LoadRequired());
            Assert.Throws<InvalidOperationException>(() => CardStaticForbiddenShapeCatalog.LoadRequired());
        }
        finally
        {
            LogicFrameRuntime.EndFrameExecution(1);
            LogicFrameRuntime.End();
            ResetStaticField(typeof(BuildingCombatShapeCatalog), "s_Cached");
            ResetStaticField(typeof(BuildingLogicObstacleShapeCatalog), "s_Cached");
            ResetStaticField(typeof(CardStaticForbiddenShapeCatalog), "s_Cached");
        }
    }

    [Test]
    public void TechTestSlotRuntimeConfig_DoesNotShareMutableResourceAssetArray()
    {
        TechTestSlotConfig source = Resources.Load<TechTestSlotConfig>("TechTestSlotConfig");
        Assert.NotNull(source);
        Assert.NotNull(source.SlotBuildingIds);
        Assert.That(source.SlotBuildingIds.Length, Is.GreaterThan(0));
        string[] original = (string[])source.SlotBuildingIds.Clone();
        TechTestSlotConfig snapshot = null;
        try
        {
            typeof(TechTestSlotConfig).GetField(
                    "s_RuntimeSnapshot",
                    BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            typeof(TechTestSlotConfig).GetField(
                    "s_RuntimePrepared",
                    BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, false);
            TechTestSlotConfig.PrepareRuntimeDependencies();
            snapshot = TechTestSlotConfig.LoadOrNull();
            Assert.NotNull(snapshot);

            source.SlotBuildingIds[0] = "Buil_Mutated_Lv1";

            Assert.AreNotSame(source, snapshot);
            Assert.AreNotSame(source.SlotBuildingIds, snapshot.SlotBuildingIds);
            Assert.AreEqual(original[0], snapshot.SlotBuildingIds[0]);
        }
        finally
        {
            source.SlotBuildingIds = original;
            if (snapshot != null)
                UnityEngine.Object.DestroyImmediate(snapshot);
            typeof(TechTestSlotConfig).GetField(
                    "s_RuntimeSnapshot",
                    BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            typeof(TechTestSlotConfig).GetField(
                    "s_RuntimePrepared",
                    BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, false);
        }
    }

    [Test]
    public void PreloadedCardProvider_FreezesLogicConfigurationBeforeFramesRun()
    {
        LogicRuntimeDataTableCache.PrepareForEditorTests(
            LoadCharacterRows(),
            Array.Empty<BuildingTable>(),
            Array.Empty<LevelTagTable>());
        CardData asset = ScriptableObject.CreateInstance<CardData>();
        try
        {
            asset.Configure((int)UnitType.Unit_Intern, 1);
            var provider = new CardDataAdapter(asset);

            asset.Configure((int)UnitType.Unit_Sprinter, 3);

            Assert.AreEqual("Unit_Intern_Lv1", provider.CardId);
            Assert.AreEqual(UnitType.Unit_Intern, provider.SoldierIndex);
            Assert.AreEqual(1, provider.RequiredLv);
            FieldInfo[] providerFields = typeof(CardDataAdapter).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < providerFields.Length; i++)
                Assert.AreNotEqual(typeof(CardData), providerFields[i].FieldType, providerFields[i].Name);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(asset);
            LogicRuntimeDataTableCache.ResetForEditorTests();
        }
    }

    [Test]
    public void CardPoolLookup_DoesNotSubstituteAnotherBuildingLevel()
    {
        var controller = new CardSystemController();
        var providers = new System.Collections.Generic.List<ICardDataProvider>
        {
            new FixedCardProvider(UnitType.Unit_Intern, 1),
        };
        SetPrivateField(controller, "m_CardPool", providers);
        MethodInfo find = typeof(CardSystemController).GetMethod(
            "FindCardData",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(find);

        object result = find.Invoke(controller, new object[] { UnitType.Unit_Intern, 2 });

        Assert.IsNull(result, "A missing exact card level is a configuration error, not a fallback card.");
    }

    [Test]
    public void RuntimeAuthorityConstants_DoNotHideFloatConversionBehindIdentifiers()
    {
        string[] authorityFiles =
        {
            "AAAGame/Scripts/Buff/BuffCallback.cs",
            "AAAGame/Scripts/Buff/BuildingCombatBuffs.cs",
            "AAAGame/Scripts/Buff/LevelTagRuntimeBuffs.cs",
            "AAAGame/Scripts/Buff/TechRuntimeBuffs.cs",
            "AAAGame/Scripts/Build/Tech/BuildingTechRuntimeEffect.cs",
            "AAAGame/Scripts/GameClass/DefendPhaseRuntime.cs",
            "AAAGame/Scripts/GameClass/PhaseManager.cs",
        };

        for (int i = 0; i < authorityFiles.Length; i++)
        {
            string source = ReadProjectSource(authorityFiles[i]);
            Assert.That(source, Does.Not.Contain("const float"), authorityFiles[i]);
        }

        string scriptsRoot = System.IO.Path.Combine(Application.dataPath, "AAAGame/Scripts");
        string[] scriptFiles = System.IO.Directory.GetFiles(
            scriptsRoot,
            "*.cs",
            System.IO.SearchOption.AllDirectories);
        var floatConstantPattern = new System.Text.RegularExpressions.Regex(
            @"const\s+float\s+([A-Za-z_][A-Za-z0-9_]*)",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        for (int fileIndex = 0; fileIndex < scriptFiles.Length; fileIndex++)
        {
            string source = System.IO.File.ReadAllText(scriptFiles[fileIndex]);
            System.Text.RegularExpressions.MatchCollection constants = floatConstantPattern.Matches(source);
            for (int constantIndex = 0; constantIndex < constants.Count; constantIndex++)
            {
                string constantName = constants[constantIndex].Groups[1].Value;
                Assert.IsFalse(
                    System.Text.RegularExpressions.Regex.IsMatch(
                        source,
                        @"\(Fix64\)\s*" + System.Text.RegularExpressions.Regex.Escape(constantName) + @"\b",
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant),
                    scriptFiles[fileIndex] + ": " + constantName);
            }
        }

        string managerSource = ReadProjectSource("AAAGame/Scripts/Movement/GroupMoveManager.cs");
        string updateNavigation = ExtractSourceBlock(
            managerSource,
            "private void UpdateNavigationRuntime()",
            "private void ApplyFlowFieldConfig()");
        Assert.That(updateNavigation, Does.Not.Contain("ApplyFlowFieldConfig()"));

        string brainSource = ReadProjectSource("AAAGame/Scripts/Movement/SoldierAIBrain.cs");
        string softReturnRatio = ExtractSourceBlock(
            brainSource,
            "private Fix64 GetSoftReturnRatio()",
            "private bool HasEnemyInScanRange(");
        Assert.That(softReturnRatio, Does.Not.Contain(".Config"));
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        FieldInfo field = instance.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field, fieldName);
        field.SetValue(instance, value);
    }

    private static LogicEntityState CreateConfiguredUnitState(
        string characterKey,
        LogicEntityLifetime lifetime = LogicEntityLifetime.Persistent)
    {
        var descriptor = new LogicEntitySpawnDescriptor(
            FixVector2.Zero,
            new FixVector2(Fix64.Zero, Fix64.One),
            SideType.EnemySide,
            characterKey,
            lifetime: lifetime);
        LogicEntityId entityId = LogicEntityLifecycleService.RequestConfiguredSpawn(
            descriptor,
            state =>
            {
                state.Configure(
                    null,
                    new CreaturePropertyManager(property =>
                        property == CreatureMainProperty.Health ? (Fix64)100 : Fix64.Zero),
                    0,
                    true,
                    null,
                    false);
                var move = new NoMoveComp();
                state.SetMoveComp(move);
                move.Init(state);
                var attack = new NoAtkComp();
                state.SetAtkComp(attack);
                attack.Init(state);
                var targeting = new NoTargetingComp();
                state.SetTargetingComp(targeting);
                targeting.Init(state);
            });
        return LogicEntityStateStore.GetRequired(entityId);
    }

    private static BuildingData CreateTestBuildingData(string identifier)
    {
        return new BuildingData(
            identifier,
            BuilType.Def,
            Archetype.None,
            "Tests/Building",
            identifier,
            identifier,
            1,
            0,
            (Fix64)100,
            null,
            Fix64.Zero,
            Array.Empty<Fix64>(),
            null,
            0,
            Array.Empty<string>());
    }

    private static void InvokePrivate(object instance, string methodName)
    {
        MethodInfo method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method, methodName);
        method.Invoke(instance, null);
    }

    private static void ResetAgentTypeMappings()
    {
        var mappings = typeof(AgentTypeHelper).GetField(
            "s_UnitAgentTypeIds",
            BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as System.Collections.IDictionary;
        mappings?.Clear();
        typeof(AgentTypeHelper).GetField(
                "s_RuntimeMappingsPrepared",
                BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, false);
    }

    private static void ResetStaticField(Type ownerType, string fieldName)
    {
        FieldInfo field = ownerType.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field, fieldName);
        field.SetValue(null, null);
    }

    private static string ReadProjectSource(string relativePath)
    {
        return System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, relativePath));
    }

    private static CharacterDataDetail[] LoadCharacterRows()
    {
        string path = System.IO.Path.Combine(
            Application.dataPath,
            "AAAGame/DataTable/CharacterDataDetail.txt");
        var rows = new System.Collections.Generic.List<CharacterDataDetail>();
        foreach (string line in System.IO.File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                continue;
            var row = new CharacterDataDetail();
            if (!row.ParseDataRow(line, null))
                throw new InvalidOperationException("Character data row could not be parsed for editor tests.");
            rows.Add(row);
        }
        return rows.ToArray();
    }

    private static BuildingTable[] LoadBuildingRows()
    {
        return LoadRows<BuildingTable>("AAAGame/DataTable/Build/BuildingTable.txt");
    }

    private static SkillTable[] LoadSkillRows()
    {
        return LoadRows<SkillTable>("AAAGame/DataTable/Hero/SkillTable.txt");
    }

    private static LevelTagTable[] LoadLevelTagRows()
    {
        return LoadRows<LevelTagTable>("AAAGame/DataTable/Level/LevelTagTable.txt");
    }

    private static LevelTable[] LoadLevelRows()
    {
        return LoadRows<LevelTable>("AAAGame/DataTable/Level/LevelTable.txt");
    }

    private static T[] LoadRows<T>(string relativePath) where T : UnityGameFramework.Runtime.DataRowBase, new()
    {
        string path = System.IO.Path.Combine(Application.dataPath, relativePath);
        var rows = new System.Collections.Generic.List<T>();
        foreach (string line in System.IO.File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                continue;
            var row = new T();
            if (!row.ParseDataRow(line, null))
                throw new InvalidOperationException($"{typeof(T).Name} row could not be parsed for editor tests.");
            rows.Add(row);
        }
        return rows.ToArray();
    }

    private sealed class FixedCardProvider : ICardDataProvider
    {
        public FixedCardProvider(UnitType unitType, int level)
        {
            SoldierIndex = unitType;
            RequiredLv = level;
        }

        public string CardId => $"{SoldierIndex}_Lv{RequiredLv}";
        public string CardName => CardId;
        public Sprite CardSprite => null;
        public int PopulationCost => 1;
        public int SoldierCount => 1;
        public string SoldierName => CardName;
        public UnitType SoldierIndex { get; }
        public int RequiredLv { get; }
        public string GetDisplayInfo() => CardName;
    }

    private static string ExtractSourceBlock(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), startMarker);
        Assert.That(end, Is.GreaterThan(start), endMarker);
        return source.Substring(start, end - start);
    }
}
