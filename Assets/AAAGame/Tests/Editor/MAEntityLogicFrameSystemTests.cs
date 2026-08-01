using NUnit.Framework;

using UnityEngine;

[TestFixture]
public class MAEntityLogicFrameSystemTests
{
    [SetUp]
    public void SetUp()
    {
        EntityRegistry.Clear();
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.PrepareRuntimeDependencies();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        LogicFrameRuntime.Begin();
        LogicEntityFrameSnapshotService.BeginTimeline();
        LogicMovementRegionConstraintService.BeginTimeline();
        MAEntityLogicFrameSystem.BeginTimeline();
        LogicFrameRuntime.StartTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        EntityRegistry.Clear();
        MAEntityLogicFrameSystem.EndTimeline();
        LogicMovementRegionConstraintService.EndTimeline();
        LogicEntityFrameSnapshotService.EndTimeline();
        LogicFrameRuntime.End();
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
    }

    [Test]
    public void EmptyFrame_CompletesAllPhasesAfterSnapshot()
    {
        LogicFrameRuntime.Tick(1);

        Assert.AreEqual(1UL, LogicEntityFrameSnapshotService.CapturedFrame);
        Assert.AreEqual(1UL, MAEntityLogicFrameSystem.LastCompletedFrame);
        Assert.AreEqual(0, MAEntityLogicFrameSystem.LastFrameEntityCount);
        Assert.AreEqual(0, MAEntityLogicFrameSystem.LastFramePhaseExecutionCount);
        Assert.AreEqual(MAEntityLogicFramePhase.PostUpdate, MAEntityLogicFrameSystem.LastCompletedPhase);
        Assert.AreEqual(1UL, LogicAgentCollisionShadowService.LastCompletedFrame);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastFrameEntityCount);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastBodyCount);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastCandidatePairCount);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastStates.Count);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastPairCorrectedBodyCount);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastStaticProjectionAvailableCount);
        Assert.AreEqual(0UL, LogicAgentCollisionShadowService.FramesWithPairCorrection);
        Assert.AreEqual(0UL, LogicAgentCollisionShadowService.TotalPairCorrectedBodyCount);
        Assert.AreEqual(1UL, LogicDamageEventService.LastCompletedFrame);
        Assert.AreEqual(1UL, LogicProjectileService.LastCompletedFrame);
        Assert.AreEqual(0, LogicProjectileService.ActiveCount);
    }

    [Test]
    public void WorldReset_ClearsLastFrameState()
    {
        LogicFrameRuntime.Tick(1);
        MAEntityLogicFrameSystem.ResetForWorldTransition();
        LogicEntityFrameSnapshotService.ResetForWorldTransition();

        Assert.AreEqual(0UL, MAEntityLogicFrameSystem.LastCompletedFrame);
        Assert.AreEqual(0, MAEntityLogicFrameSystem.LastFrameEntityCount);
        Assert.AreEqual(0, MAEntityLogicFrameSystem.LastFramePhaseExecutionCount);
        Assert.AreEqual(0UL, LogicEntityFrameSnapshotService.CapturedFrame);
        Assert.AreEqual(0UL, LogicAgentCollisionShadowService.LastCompletedFrame);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastFrameEntityCount);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastStates.Count);
        Assert.AreEqual(0UL, LogicAgentCollisionShadowService.FramesWithPairCorrection);
        Assert.AreEqual(0UL, LogicDamageEventService.LastCompletedFrame);
        Assert.AreEqual(0UL, LogicProjectileService.LastCompletedFrame);
    }

    [Test]
    public void PureLogicFrameEntity_ExecutesAllPhasesWithoutMAEntityView()
    {
        var entity = new PureLogicFrameEntity
        {
            LogicEntityId = new LogicEntityId(17),
            Alive = false,
        };
        EntityRegistry.Register(entity);

        LogicFrameRuntime.Tick(1);

        Assert.AreEqual(1, MAEntityLogicFrameSystem.LastFrameEntityCount);
        Assert.AreEqual((int)MAEntityLogicFramePhase.Count, MAEntityLogicFrameSystem.LastFramePhaseExecutionCount);
        Assert.AreEqual((int)MAEntityLogicFramePhase.Count, entity.ExecutedPhaseCount);
        Assert.AreEqual(1UL, entity.PreparedLogicFrame);
        Assert.AreEqual(1UL, LogicAgentCollisionShadowService.LastCompletedFrame);
        Assert.AreEqual(0, LogicAgentCollisionShadowService.LastBodyCount);
    }

    [Test]
    public void CharacterTargeting_DropsStaleDeadTargetBeforeSnapshotDistanceQuery()
    {
        var self = new PureLogicFrameEntity
        {
            LogicEntityId = new LogicEntityId(17),
            Alive = false,
            Side = SideType.PlayerSide,
        };
        var staleTarget = new SimEntityContext
        {
            LogicEntityId = new LogicEntityId(18),
            Alive = false,
            Side = SideType.EnemySide,
        };
        var targeting = new CharacterTargetingComp();
        targeting.Init(self);
        targeting.CurrentTarget = staleTarget;
        var probe = new TargetingUpdateProbe(targeting);
        LogicFrameRuntime.Register(probe);

        try
        {
            Assert.DoesNotThrow(() => LogicFrameRuntime.Tick(1));
            Assert.IsNull(targeting.CurrentTarget);
        }
        finally
        {
            LogicFrameRuntime.Unregister(probe);
        }
    }

    [Test]
    public void StoredLogicEntityState_ExecutesFrameWithoutBoundView()
    {
        LogicTimeControlService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        try
        {
            LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn(
                new LogicEntitySpawnDescriptor(
                    new FixVector2((Fix64)2, (Fix64)3),
                    new FixVector2(Fix64.Zero, Fix64.One),
                    SideType.PlayerSide,
                    "PureRuntime"));
            LogicEntityState state = LogicEntityStateStore.GetRequired(entityId);
            state.Configure(
                null,
                new CreaturePropertyManager(property =>
                    property == CreatureMainProperty.Health ? (Fix64)100 : Fix64.Zero),
                0,
                true,
                null);
            new NoMoveFactoryForTest().Configure(state);
            LogicEntityStateStore.CommitSpawn(entityId);
            EntityRegistry.Register(state);

            LogicFrameRuntime.Tick(1);

            Assert.AreEqual(1UL, MAEntityLogicFrameSystem.LastCompletedFrame);
            Assert.AreEqual(1, MAEntityLogicFrameSystem.LastFrameEntityCount);
            Assert.AreEqual((int)MAEntityLogicFramePhase.Count, MAEntityLogicFrameSystem.LastFramePhaseExecutionCount);
            Assert.IsFalse(state.HasBoundView);
            Assert.AreEqual(2L * Fix64.One.RawValue, state.Position.x.RawValue);
            Assert.AreEqual(3L * Fix64.One.RawValue, state.Position.y.RawValue);
        }
        finally
        {
            EntityRegistry.Clear();
            LogicEntityLifecycleService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    [Test]
    public void BoundMAEntityView_CapturesPoseAfterCompleteLogicFrame()
    {
        LogicTimeControlService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        GameObject viewObject = null;
        LogicEntityId entityId = default;
        bool viewBound = false;
        try
        {
            entityId = LogicEntityLifecycleService.RequestSpawn(
                new LogicEntitySpawnDescriptor(
                    FixVector2.Zero,
                    new FixVector2(Fix64.Zero, Fix64.One),
                    SideType.PlayerSide,
                    "CoordinatedPoseTest"));
            LogicEntityState state = LogicEntityStateStore.GetRequired(entityId);
            state.Configure(
                null,
                new CreaturePropertyManager(property =>
                    property == CreatureMainProperty.Health ? (Fix64)100 : Fix64.Zero),
                0,
                true,
                null);
            new NoMoveFactoryForTest().Configure(state);
            LogicEntityStateStore.CommitSpawn(entityId);
            EntityRegistry.Register(state);

            viewObject = new GameObject("CoordinatedPoseTestView");
            UnityGameFramework.Runtime.Entity frameworkEntity =
                viewObject.AddComponent<UnityGameFramework.Runtime.Entity>();
            CoordinatedPoseTestView view = viewObject.AddComponent<CoordinatedPoseTestView>();
            typeof(UnityGameFramework.Runtime.Entity).GetField(
                    "m_Id",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(frameworkEntity, 404);
            typeof(UnityGameFramework.Runtime.EntityLogic).GetField(
                    "m_Entity",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(view, frameworkEntity);
            typeof(UnityGameFramework.Runtime.EntityLogic).GetField(
                    "m_CachedTransform",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(view, viewObject.transform);
            typeof(EntityBase).GetProperty(nameof(EntityBase.Id)).SetValue(view, 404);
            typeof(MAEntity).GetProperty(nameof(MAEntity.LogicEntityId)).SetValue(view, entityId);
            typeof(MAEntity).GetField(
                    "_isLogicActive",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(view, true);
            typeof(EntityBase).GetMethod(
                    "InitializeRenderInterpolation",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(view, null);
            LogicEntityLifecycleService.BindView(entityId, 404, view);
            viewBound = true;

            AssertPoseCache(view, 1f, 1f);
            LogicFrameRuntime.Tick(1);
            AssertPoseCache(view, 1f, 2f);
            LogicFrameRuntime.Tick(2);
            AssertPoseCache(view, 2f, 3f);
        }
        finally
        {
            EntityRegistry.Clear();
            if (viewBound)
                LogicEntityLifecycleService.UnbindView(entityId, 404);
            if (viewObject != null)
                Object.DestroyImmediate(viewObject);
            LogicEntityLifecycleService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    [Test]
    public void ViewlessLogicSpawnInsideRuntimeObstacle_RecoversExactlyOnceInCollisionPipeline()
    {
        const int width = 8;
        const int height = 3;
        var walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            width,
            height,
            1f,
            UnityEngine.Vector3.zero,
            walkable);
        ProcessWorldBuildQueueUntilReady();
        FlowFieldCrowdMovementSystem.RegisterBoxObstacleFixed(
            9001,
            new FixVector2((Fix64)1.5f, (Fix64)1.5f),
            new FixVector2((Fix64)0.5f, (Fix64)0.5f));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());

        FixVector2 spawnPosition = new FixVector2((Fix64)1.5f, (Fix64)1.5f);
        Fix64 collisionRadiusProperty = (Fix64)5;
        Fix64 collisionRadius = DistanceUnitConverter.ConvertToWorld(collisionRadiusProperty);
        Assert.IsTrue(LogicStaticCollisionShadowService.TrySolveFixed(
            0,
            spawnPosition,
            FixVector2.Zero,
            collisionRadius,
            out LogicStaticCollisionShadowResult expectedSolve));
        Assert.IsTrue(expectedSolve.SolveResult.Success);
        Assert.IsTrue(expectedSolve.SolveResult.StartedOverlapping);
        FixVector2 expectedPosition = spawnPosition + expectedSolve.SolveResult.ResolvedDisplacement;

        LogicTimeControlService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        try
        {
            LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn(
                new LogicEntitySpawnDescriptor(
                    spawnPosition,
                    new FixVector2(Fix64.Zero, Fix64.One),
                    SideType.PlayerSide,
                    "ViewlessConstructionEscape"));
            LogicEntityState state = LogicEntityStateStore.GetRequired(entityId);
            state.Configure(
                null,
                new CreaturePropertyManager(property =>
                    property switch
                    {
                        CreatureMainProperty.Health => (Fix64)100,
                        CreatureMainProperty.CollisionRadius => collisionRadiusProperty,
                        _ => Fix64.Zero,
                    }),
                0,
                true,
                null);
            new NoMoveFactoryForTest().Configure(state);
            LogicEntityStateStore.CommitSpawn(entityId);
            EntityRegistry.Register(state);

            LogicFrameRuntime.Tick(1);

            Assert.IsFalse(state.HasBoundView);
            Assert.AreEqual(expectedPosition.x.RawValue, state.Position.x.RawValue);
            Assert.AreEqual(expectedPosition.y.RawValue, state.Position.y.RawValue);
            Assert.AreEqual(1, LogicAgentCollisionShadowService.LastStaticProjectionChangedCount);
            Assert.IsTrue(LogicStaticCollisionShadowService.TrySolveFixed(
                0,
                state.Position,
                FixVector2.Zero,
                collisionRadius,
                out LogicStaticCollisionShadowResult legalProbe));
            Assert.IsTrue(legalProbe.SolveResult.Success);
            Assert.IsFalse(legalProbe.SolveResult.StartedOverlapping);
        }
        finally
        {
            EntityRegistry.Clear();
            LogicEntityLifecycleService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    [Test]
    public void RuntimeObstacleRemove_StopsBlockingBeforeFlowRebuildCompletes()
    {
        const int width = 8;
        const int height = 3;
        var walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            width,
            height,
            1f,
            UnityEngine.Vector3.zero,
            walkable);
        ProcessWorldBuildQueueUntilReady();
        FixVector2 obstacleCenter = new FixVector2((Fix64)1.5f, (Fix64)1.5f);
        FlowFieldCrowdMovementSystem.RegisterBoxObstacleFixed(
            9001,
            obstacleCenter,
            new FixVector2((Fix64)0.5f, (Fix64)0.5f));
        ProcessRuntimeDirtyQueueUntilReady();

        FlowFieldCrowdMovementSystem.UnregisterObstacle(9001);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
        Assert.IsTrue(LogicStaticCollisionShadowService.TrySolveFixed(
            0,
            obstacleCenter,
            FixVector2.Zero,
            (Fix64)0.25f,
            out LogicStaticCollisionShadowResult solve));
        Assert.IsTrue(solve.SolveResult.Success);
        Assert.IsFalse(solve.SolveResult.StartedOverlapping);
        Assert.AreEqual(FixVector2.Zero, solve.SolveResult.ResolvedDisplacement);
    }

    [Test]
    public void RuntimeObstacleRemove_PreservesAuthoredBaseBlockerBeforeFlowRebuildCompletes()
    {
        const int width = 8;
        const int height = 3;
        var walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        walkable[1 * width + 4] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            width,
            height,
            1f,
            UnityEngine.Vector3.zero,
            walkable);
        ProcessWorldBuildQueueUntilReady();
        FixVector2 runtimeObstacleCenter = new FixVector2((Fix64)1.5f, (Fix64)1.5f);
        FlowFieldCrowdMovementSystem.RegisterBoxObstacleFixed(
            9001,
            runtimeObstacleCenter,
            new FixVector2((Fix64)0.5f, (Fix64)0.5f));
        ProcessRuntimeDirtyQueueUntilReady();

        FlowFieldCrowdMovementSystem.UnregisterObstacle(9001);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
        Assert.IsTrue(LogicStaticCollisionShadowService.TrySolveFixed(
            0,
            runtimeObstacleCenter,
            FixVector2.Zero,
            (Fix64)0.25f,
            out LogicStaticCollisionShadowResult clearedRuntimeObstacle));
        Assert.IsFalse(clearedRuntimeObstacle.SolveResult.StartedOverlapping);
        Assert.AreEqual(FixVector2.Zero, clearedRuntimeObstacle.SolveResult.ResolvedDisplacement);

        Assert.IsTrue(LogicStaticCollisionShadowService.TrySolveFixed(
            0,
            new FixVector2((Fix64)2.5f, (Fix64)1.5f),
            new FixVector2((Fix64)4, Fix64.Zero),
            (Fix64)0.25f,
            out LogicStaticCollisionShadowResult authoredBlocker));
        Assert.IsTrue(authoredBlocker.SolveResult.Success);
        Assert.That((float)authoredBlocker.SolveResult.ResolvedDisplacement.x, Is.EqualTo(1.25f).Within(0.003f));
        Assert.That((float)authoredBlocker.SolveResult.ResolvedDisplacement.y, Is.EqualTo(0f).Within(0.003f));
    }

    [Test]
    public void 失衡退出当帧恢复单位碰撞并解开与附近敌人的重叠()
    {
        LogicTimeControlService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        try
        {
            LogicEntityState displaced = CreateDisplacementTestUnit(101, SideType.PlayerSide, FixVector2.Zero);
            LogicEntityState nearby = CreateDisplacementTestUnit(102, SideType.EnemySide, FixVector2.Zero);

            Assert.IsTrue(displaced.DurationMoveEffectComp.TryApplyKnockback(
                new FixVector2(Fix64.One, Fix64.Zero),
                (Fix64)3));
            displaced.DurationMoveEffectComp.CommitStaticCollision(new FixVector2(-Fix64.One, Fix64.Zero));

            for (ulong frame = 1; frame <= 3; frame++)
            {
                LogicFrameRuntime.Tick(frame);
                Assert.IsTrue(displaced.DurationMoveEffectComp.IsInLossOfBalance);
                Assert.AreEqual(0u, displaced.AgentCollisionMask);
                Assert.AreEqual(FixVector2.Zero, displaced.Position);
                Assert.AreEqual(FixVector2.Zero, nearby.Position);
            }

            LogicFrameRuntime.Tick(4);

            Assert.IsFalse(displaced.DurationMoveEffectComp.IsInLossOfBalance);
            Assert.AreNotEqual(0u, displaced.AgentCollisionMask);
            Assert.Greater(LogicAgentCollisionShadowService.LastPairCorrectedBodyCount, 0);
            Fix64 minimumSeparation = displaced.CombatShape.Radius + nearby.CombatShape.Radius;
            Assert.GreaterOrEqual(
                FixVector2.Distance(displaced.Position, nearby.Position).RawValue,
                (minimumSeparation - Fix64.FromRaw(2)).RawValue,
                "恢复碰撞的同一帧应由确定性单位碰撞求解器完成解叠");
        }
        finally
        {
            EntityRegistry.Clear();
            LogicEntityLifecycleService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    [Test]
    public void 建筑目标完全忽略物理位移()
    {
        LogicTimeControlService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        try
        {
            LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn(
                new LogicEntitySpawnDescriptor(
                    FixVector2.Zero,
                    new FixVector2(Fix64.Zero, Fix64.One),
                    SideType.EnemySide,
                    "DisplacementBuildingTest"));
            LogicEntityState building = LogicEntityStateStore.GetRequired(entityId);
            building.Configure(
                null,
                new CreaturePropertyManager(property =>
                    property == CreatureMainProperty.Health ? (Fix64)100 : Fix64.Zero),
                0,
                true,
                null,
                false);
            new NoMoveFactoryForTest().Configure(building);
            building.ConfigureBuilding(
                new BuildingData(
                    "DisplacementBuildingTest",
                    BuilType.Def,
                    Archetype.None,
                    "Tests/Building",
                    "Test_Name",
                    "Test_Desc",
                    1,
                    0,
                    (Fix64)100,
                    null,
                    Fix64.Zero,
                    System.Array.Empty<Fix64>(),
                    null,
                    0,
                    System.Array.Empty<string>()),
                "displacement-building-test",
                "test-stronghold",
                EntitySideHelper.EnemyFactionId,
                LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
                System.Array.Empty<LogicCombatShape>(),
                System.Array.Empty<LogicInteractionOptionDescriptor>(),
                false);

            Assert.IsFalse(building.DurationMoveEffectComp.TryApplyKnockback(
                new FixVector2(Fix64.One, Fix64.Zero),
                (Fix64)99));
            Assert.IsFalse(building.DurationMoveEffectComp.IsInLossOfBalance);
            Assert.AreEqual(FixVector2.Zero, building.DurationMoveEffectComp.DisplacementVelocity);
        }
        finally
        {
            EntityRegistry.Clear();
            LogicEntityLifecycleService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    private static void ProcessWorldBuildQueueUntilReady()
    {
        for (int i = 0; i < 2048
             && (!FlowFieldCrowdMovementSystem.HasEditorTestWorld()
                 || FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild());
             i++)
        {
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
        }

        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestWorld());
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild());
    }

    private static void ProcessRuntimeDirtyQueueUntilReady()
    {
        for (int i = 0; i < 512 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());
    }

    [Test]
    public void MoveCommit_NavigationConstraintBypass_DoesNotBypassEnemyStrongholdBoundary()
    {
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.BuildBeforeInvade);
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("enemy", 0, 0, EntitySideHelper.EnemyFactionId),
            });
        var entity = new RegionConstraintProbeEntity
        {
            LogicEntityId = new LogicEntityId(7001),
            Side = SideType.PlayerSide,
            PositionFixed = new FixVector2(-Fix64.One, Fix64.Zero),
            DesiredDisplacement = new FixVector2((Fix64)2, Fix64.Zero),
        };
        Fix64 collisionRadius = Fix64.One / (Fix64)4;
        entity.SetProperty(
            CreatureMainProperty.CollisionRadius,
            DistanceUnitConverter.ConvertFromWorld(collisionRadius));
        EntityRegistry.Register(entity);

        try
        {
            LogicFrameRuntime.Tick(1);

            Assert.AreEqual(Fix64.FromRaw(-3072).RawValue, entity.PositionFixed.x.RawValue);
            Assert.AreEqual(Fix64.Zero, entity.PositionFixed.y);
            Assert.IsTrue(LogicStrongholdMap.IsCircleClearOfForeignStrongholds(
                entity.PositionFixed,
                collisionRadius,
                EntitySideHelper.PlayerFactionId));
            Assert.AreEqual(1, LogicAgentCollisionShadowService.LastRegionConstraintChangedCount);
        }
        finally
        {
            LogicStrongholdMap.Clear();
            LogicPhaseCommandService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    [Test]
    public void MoveCommit_RegionProjectionIntoStaticWall_RejectsTickAtJointlyLegalStart()
    {
        const int width = 8;
        const int height = 8;
        var walkable = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                walkable[x + y * width] = y < 4 || x >= 4;
        }
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            width,
            height,
            1f,
            Vector3.zero,
            walkable);
        ProcessWorldBuildQueueUntilReady();

        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.BuildBeforeInvade);
        LogicStrongholdMap.Initialize(
            new FixVector2(Fix64.FromRaw(19852), Fix64.FromRaw(19852)),
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("enemy", 0, 0, EntitySideHelper.EnemyFactionId),
            });

        var entity = new RegionConstraintProbeEntity
        {
            LogicEntityId = new LogicEntityId(7002),
            Side = SideType.PlayerSide,
            NavigationConstraintEnabled = true,
            NavigationAgentTypeIdOverride = AgentTypeHelper.MediumMovementTypeId,
        };
        Fix64 collisionRadius = Fix64.FromRaw(1352);
        entity.SetProperty(
            CreatureMainProperty.CollisionRadius,
            DistanceUnitConverter.ConvertFromWorld(collisionRadius));

        try
        {
            FindJointConstraintConflict(
                entity,
                collisionRadius,
                out FixVector2 frameStart,
                out FixVector2 desiredDisplacement,
                out FixVector2 oldInvalidResult);
            entity.PositionFixed = frameStart;
            entity.DesiredDisplacement = desiredDisplacement;
            EntityRegistry.Register(entity);

            LogicFrameRuntime.Tick(1);

            Assert.AreEqual(frameStart, entity.PositionFixed);
            Assert.AreNotEqual(oldInvalidResult, entity.PositionFixed);
            Assert.AreEqual(1, LogicAgentCollisionShadowService.LastJointConstraintRejectedCount);
            Assert.AreEqual(
                LogicMovementRegionConstraintFailure.EnemyStronghold,
                LogicAgentCollisionShadowService.LastStates[0].RegionConstraintFailure);
            AssertStaticPositionClear(entity, entity.PositionFixed, collisionRadius);
            Assert.IsTrue(LogicMovementRegionConstraintService.IsPositionAllowed(
                entity,
                entity.PositionFixed,
                out LogicMovementRegionConstraintFailure failure));
            Assert.AreEqual(LogicMovementRegionConstraintFailure.None, failure);
        }
        finally
        {
            LogicStrongholdMap.Clear();
            LogicPhaseCommandService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    private static void FindJointConstraintConflict(
        RegionConstraintProbeEntity entity,
        Fix64 collisionRadius,
        out FixVector2 frameStart,
        out FixVector2 desiredDisplacement,
        out FixVector2 invalidResult)
    {
        for (long yRaw = 15000; yRaw <= 18000; yRaw += 64)
        {
            for (long xRaw = 15000; xRaw <= 18000; xRaw += 64)
            {
                var start = new FixVector2(Fix64.FromRaw(xRaw), Fix64.FromRaw(yRaw));
                if (!IsStaticPositionClear(entity, start, collisionRadius)
                    || !LogicMovementRegionConstraintService.IsPositionAllowed(entity, start, out _))
                {
                    continue;
                }

                for (int directionIndex = 0; directionIndex < 8; directionIndex++)
                {
                    FixVector2 direction = directionIndex switch
                    {
                        0 => new FixVector2(Fix64.One, Fix64.Zero),
                        1 => new FixVector2(-Fix64.One, Fix64.Zero),
                        2 => new FixVector2(Fix64.Zero, Fix64.One),
                        3 => new FixVector2(Fix64.Zero, -Fix64.One),
                        4 => new FixVector2(Fix64.One, Fix64.One),
                        5 => new FixVector2(-Fix64.One, Fix64.One),
                        6 => new FixVector2(Fix64.One, -Fix64.One),
                        _ => new FixVector2(-Fix64.One, -Fix64.One),
                    };
                    FixVector2 displacement = direction * Fix64.FromRaw(512);
                    Assert.IsTrue(LogicStaticCollisionShadowService.TrySolveFixed(
                        entity.NavigationAgentTypeId,
                        start,
                        displacement,
                        collisionRadius,
                        out LogicStaticCollisionShadowResult staticSolve));
                    Assert.IsTrue(staticSolve.SolveResult.Success);
                    FixVector2 staticPosition = staticSolve.SolveResult.Start
                                                + staticSolve.SolveResult.ResolvedDisplacement;
                    FixVector2 regionPosition = LogicMovementRegionConstraintService.ResolvePosition(
                        entity,
                        start,
                        staticPosition,
                        out LogicMovementRegionConstraintFailure regionFailure);
                    if (regionFailure == LogicMovementRegionConstraintFailure.None
                        || regionPosition == staticPosition
                        || IsStaticPositionClear(entity, regionPosition, collisionRadius))
                    {
                        continue;
                    }

                    frameStart = start;
                    desiredDisplacement = displacement;
                    invalidResult = regionPosition;
                    return;
                }
            }
        }

        throw new System.InvalidOperationException("Failed to construct a deterministic joint static/stronghold constraint conflict.");
    }

    private static bool IsStaticPositionClear(
        RegionConstraintProbeEntity entity,
        FixVector2 position,
        Fix64 collisionRadius)
    {
        Assert.IsTrue(LogicStaticCollisionShadowService.TrySolveFixed(
            entity.NavigationAgentTypeId,
            position,
            FixVector2.Zero,
            collisionRadius,
            out LogicStaticCollisionShadowResult solve));
        Assert.IsTrue(solve.SolveResult.Success);
        return !solve.SolveResult.StartedOverlapping;
    }

    private static void AssertStaticPositionClear(
        RegionConstraintProbeEntity entity,
        FixVector2 position,
        Fix64 collisionRadius)
    {
        Assert.IsTrue(
            IsStaticPositionClear(entity, position, collisionRadius),
            $"Position must be statically clear. raw=({position.x.RawValue},{position.y.RawValue}).");
    }

    private static void AssertPoseCache(EntityBase view, float expectedPreviousX, float expectedCurrentX)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Vector3 previous = (Vector3)typeof(EntityBase).GetField("m_PreviousLogicPosition", flags).GetValue(view);
        Vector3 current = (Vector3)typeof(EntityBase).GetField("m_CurrentLogicPosition", flags).GetValue(view);
        Assert.AreEqual(expectedPreviousX, previous.x);
        Assert.AreEqual(expectedCurrentX, current.x);
    }

    private static LogicEntityState CreateDisplacementTestUnit(
        int stableId,
        SideType side,
        FixVector2 position)
    {
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn(
            new LogicEntitySpawnDescriptor(
                position,
                new FixVector2(Fix64.Zero, Fix64.One),
                side,
                $"DisplacementTest_{stableId}"));
        LogicEntityState state = LogicEntityStateStore.GetRequired(entityId);
        state.Configure(
            null,
            new CreaturePropertyManager(property => property switch
            {
                CreatureMainProperty.Health => (Fix64)100,
                CreatureMainProperty.CollisionRadius => (Fix64)5,
                CreatureMainProperty.WeightLevel => (Fix64)2,
                _ => Fix64.Zero,
            }),
            0,
            false,
            null,
            false);
        new NoMoveFactoryForTest().Configure(state);
        state.MoveExecutor.SetNavigationConstrained(false);
        LogicEntityStateStore.CommitSpawn(entityId);
        EntityRegistry.Register(state);
        return state;
    }

    private sealed class NoMoveFactoryForTest
    {
        public void Configure(LogicEntityState state)
        {
            var move = new NoMoveComp();
            state.SetMoveComp(move);
            move.Init(state);
            var attack = new NoAtkComp();
            state.SetAtkComp(attack);
            attack.Init(state);
            var targeting = new NoTargetingComp();
            state.SetTargetingComp(targeting);
            targeting.Init(state);
        }
    }

    private sealed class PureLogicFrameEntity : SimEntityContext, ILogicFrameEntity
    {
        private MAEntityLogicFramePhase m_NextPhase;
        private bool m_FrameActive;

        public bool IsLogicActive => true;
        public int NavigationAgentTypeId => 0;
        public bool AllowsZeroCollisionRadius => true;
        public bool HasPreparedLogicMove => PreparedLogicFrame > 0;
        public ulong PreparedLogicFrame { get; private set; }
        public bool PreparedCollisionMovable => false;
        public bool PreparedNavigationConstraintEnabled => false;
        public uint AgentCollisionMask => 1u;
        public FixVector2 PreparedResolvedHorizontalDisplacement => FixVector2.Zero;
        public int ExecutedPhaseCount { get; private set; }

        public void BeginLogicFrame(Fix64 deltaTime)
        {
            Assert.AreEqual(LogicFrameRuntime.FixedDeltaTime, deltaTime);
            Assert.IsFalse(m_FrameActive);
            m_FrameActive = true;
            m_NextPhase = MAEntityLogicFramePhase.BaseAndBuffs;
        }

        public void ExecuteLogicFramePhase(MAEntityLogicFramePhase phase, Fix64 deltaTime)
        {
            Assert.IsTrue(m_FrameActive);
            Assert.AreEqual(m_NextPhase, phase);
            if (phase == MAEntityLogicFramePhase.MoveResolve)
                PreparedLogicFrame = LogicFrameRuntime.CurrentFrame;
            ExecutedPhaseCount++;
            m_NextPhase = (MAEntityLogicFramePhase)((int)phase + 1);
        }

        public void CompleteLogicFrame(Fix64 deltaTime)
        {
            Assert.IsTrue(m_FrameActive);
            Assert.AreEqual(MAEntityLogicFramePhase.Count, m_NextPhase);
            m_FrameActive = false;
        }
    }

    private sealed class RegionConstraintProbeEntity : SimEntityContext, ILogicFrameEntity
    {
        private MAEntityLogicFramePhase m_NextPhase;

        public bool IsLogicActive => true;
        public int NavigationAgentTypeId => NavigationAgentTypeIdOverride;
        public bool AllowsZeroCollisionRadius => false;
        public bool HasPreparedLogicMove { get; private set; }
        public ulong PreparedLogicFrame { get; private set; }
        public bool PreparedCollisionMovable => true;
        public bool PreparedNavigationConstraintEnabled => NavigationConstraintEnabled;
        public uint AgentCollisionMask => uint.MaxValue;
        public FixVector2 PreparedResolvedHorizontalDisplacement => DesiredDisplacement;
        public FixVector2 DesiredDisplacement { get; set; }
        public bool NavigationConstraintEnabled { get; set; }
        public int NavigationAgentTypeIdOverride { get; set; }

        public void BeginLogicFrame(Fix64 deltaTime)
        {
            m_NextPhase = MAEntityLogicFramePhase.BaseAndBuffs;
        }

        public void ExecuteLogicFramePhase(MAEntityLogicFramePhase phase, Fix64 deltaTime)
        {
            Assert.AreEqual(m_NextPhase, phase);
            if (phase == MAEntityLogicFramePhase.MoveResolve)
            {
                PreparedLogicFrame = LogicFrameRuntime.CurrentFrame;
                HasPreparedLogicMove = true;
            }
            else if (phase == MAEntityLogicFramePhase.MoveCommit)
            {
                PositionFixed = LogicAgentCollisionShadowService.GetRequiredResolvedPosition(
                    LogicEntityId,
                    LogicFrameRuntime.CurrentFrame);
            }

            m_NextPhase = (MAEntityLogicFramePhase)((int)phase + 1);
        }

        public void CompleteLogicFrame(Fix64 deltaTime)
        {
            Assert.AreEqual(MAEntityLogicFramePhase.Count, m_NextPhase);
            HasPreparedLogicMove = false;
        }
    }

    private sealed class TargetingUpdateProbe : ILogicFrameUpdate, ILogicFrameStableOrder
    {
        private readonly ITargetingComp m_Targeting;

        public TargetingUpdateProbe(ITargetingComp targeting)
        {
            m_Targeting = targeting;
        }

        public int LogicFrameOrder => -1;
        public long LogicFrameStableKey => 0;

        public void OnLogicFrameUpdate(Fix64 deltaTime)
        {
            m_Targeting.UpdateTargeting(deltaTime);
        }
    }
}

public sealed class CoordinatedPoseTestView : MAEntity
{
    private int m_PoseSampleCount;

    protected override void GetAuthoritativeLogicPose(out Vector3 position, out Quaternion rotation)
    {
        m_PoseSampleCount++;
        position = new Vector3(m_PoseSampleCount, 0f, 0f);
        rotation = Quaternion.identity;
    }
}
