using NUnit.Framework;

[TestFixture]
public class MAEntityLogicFrameSystemTests
{
    [SetUp]
    public void SetUp()
    {
        EntityRegistry.Clear();
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
}
