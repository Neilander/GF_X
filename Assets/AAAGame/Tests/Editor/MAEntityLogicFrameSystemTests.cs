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
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        LogicFrameRuntime.Begin();
        LogicEntityFrameSnapshotService.BeginTimeline();
        MAEntityLogicFrameSystem.BeginTimeline();
        LogicFrameRuntime.StartTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        EntityRegistry.Clear();
        MAEntityLogicFrameSystem.EndTimeline();
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
        ProcessRuntimeDirtyQueueUntilReady();

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

    private static void AssertPoseCache(EntityBase view, float expectedPreviousX, float expectedCurrentX)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Vector3 previous = (Vector3)typeof(EntityBase).GetField("m_PreviousLogicPosition", flags).GetValue(view);
        Vector3 current = (Vector3)typeof(EntityBase).GetField("m_CurrentLogicPosition", flags).GetValue(view);
        Assert.AreEqual(expectedPreviousX, previous.x);
        Assert.AreEqual(expectedCurrentX, current.x);
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
