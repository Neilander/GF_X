using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class LogicFrameRuntimeTests
{
    private readonly List<ILogicFrameUpdate> m_Registered = new List<ILogicFrameUpdate>();

    [SetUp]
    public void SetUp()
    {
        if (LogicFrameRuntime.IsActive)
            throw new InvalidOperationException("LogicFrameRuntimeTests.SetUp failed: runtime is already active.");
        LogicFrameRuntime.Begin();
        LogicFrameRuntime.StartTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = m_Registered.Count - 1; i >= 0; i--)
            LogicFrameRuntime.Unregister(m_Registered[i]);
        m_Registered.Clear();
        LogicFrameRuntime.End();
    }

    [Test]
    public void StableListeners_TickByStableKey_NotRegistrationOrder()
    {
        var ticks = new List<long>();
        Register(new StableListener(30, ticks));
        Register(new StableListener(10, ticks));
        Register(new StableListener(20, ticks));

        LogicFrameRuntime.Tick(1);

        CollectionAssert.AreEqual(new long[] { 10, 20, 30 }, ticks);
    }

    [Test]
    public void StableListeners_WithDifferentOrders_UsePhaseOrderFirst()
    {
        var ticks = new List<long>();
        Register(new StableListener(1, ticks, 100));
        Register(new StableListener(999, ticks, -100));

        LogicFrameRuntime.Tick(1);

        CollectionAssert.AreEqual(new long[] { 999, 1 }, ticks);
    }

    [Test]
    public void DuplicateStableKeyInSameOrder_IsRejected()
    {
        var ticks = new List<long>();
        Register(new StableListener(7, ticks));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Register(new StableListener(7, ticks)));

        StringAssert.Contains("duplicate stable order key", exception.Message);
        StringAssert.Contains("stableKey=7", exception.Message);
    }

    [Test]
    public void LegacyListeners_PreserveRegistrationOrder()
    {
        var ticks = new List<long>();
        Register(new LegacyListener(3, ticks));
        Register(new LegacyListener(1, ticks));
        Register(new LegacyListener(2, ticks));

        LogicFrameRuntime.Tick(1);

        CollectionAssert.AreEqual(new long[] { 3, 1, 2 }, ticks);
    }

    [Test]
    public void StableListeners_TickBeforeLegacyListenersAtSamePhase()
    {
        var ticks = new List<long>();
        Register(new LegacyListener(1000, ticks));
        Register(new StableListener(5, ticks));

        LogicFrameRuntime.Tick(1);

        CollectionAssert.AreEqual(new long[] { 5, 1000 }, ticks);
    }

    [Test]
    public void Register_OutsideActiveRuntime_IsRejected()
    {
        var listener = new LegacyListener(1, new List<long>());
        bool unexpectedlyRegistered = false;
        LogicFrameRuntime.End();
        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            {
                LogicFrameRuntime.Register(listener);
                unexpectedlyRegistered = true;
            });
            StringAssert.Contains("runtime is not active", exception.Message);
        }
        finally
        {
            LogicFrameRuntime.Begin();
            LogicFrameRuntime.StartTimeline();
            if (unexpectedlyRegistered)
                LogicFrameRuntime.Unregister(listener);
        }
    }

    [Test]
    public void DefendSchedule_QuantizesDurationOnceToAbsoluteTickOffset()
    {
        Assert.AreEqual(31UL, DefendPhaseRuntime.GetEditorTestTickCount(Fix64.One));
        Assert.AreEqual(62UL, checked(DefendPhaseRuntime.GetEditorTestTickCount(Fix64.One) * 2UL));
    }

    [TestCase(3, 1.5f, 5)]
    [TestCase(3, 1.25f, 4)]
    [TestCase(1, 0.25f, 1)]
    public void DefendWaveGrowthUsesFixedRoundToNearest(int count, float scale, int expected)
    {
        Assert.AreEqual(expected, DefendPhaseRuntime.GetEditorTestScaledSpawnCount(count, (Fix64)scale));
    }

    [TestCase(1.5f, 2)]
    [TestCase(1.25f, 1)]
    [TestCase(-1.5f, -2)]
    [TestCase(-1.25f, -1)]
    public void RewardIncomeUsesFixedMidpointRoundingAwayFromZero(float value, int expected)
    {
        Assert.AreEqual(expected, RewardManager.GetEditorTestRoundedIncome((Fix64)value));
    }

    [Test]
    public void CardAutoDrawClock_UsesAbsoluteLogicFramesWithoutRenderRetiming()
    {
        var clock = new LogicCardAutoDrawClock();

        Assert.AreEqual(5UL, clock.IntervalTicks);
        Assert.IsTrue(clock.IsDue(100));
        clock.RecordDraw(100);
        Assert.AreEqual(105UL, clock.NextEligibleFrame);
        Assert.IsFalse(clock.IsDue(104));
        Assert.IsTrue(clock.IsDue(105));
        Assert.IsTrue(clock.IsDue(108));
        clock.RecordDraw(108);
        Assert.AreEqual(113UL, clock.NextEligibleFrame);
    }

    [Test]
    public void CardRuntimeState_HashTracksBoundContributorAndRejectsAmbiguousOwnership()
    {
        var first = new CardStateContributorStub(10);
        var second = new CardStateContributorStub(20);
        try
        {
            LogicCardRuntimeState.Bind(first);
            var before = new LogicStateHasher();
            LogicCardRuntimeState.WriteDeterministicState(before);

            first.Value = 11;
            var after = new LogicStateHasher();
            LogicCardRuntimeState.WriteDeterministicState(after);

            Assert.AreNotEqual(before.Hash, after.Hash);
            Assert.Throws<InvalidOperationException>(() => LogicCardRuntimeState.Bind(second));
            Assert.Throws<InvalidOperationException>(() => LogicCardRuntimeState.Unbind(second));
        }
        finally
        {
            if (LogicCardRuntimeState.IsBound)
                LogicCardRuntimeState.Unbind(first);
        }

        var unbound = new LogicStateHasher();
        LogicCardRuntimeState.WriteDeterministicState(unbound);
        Assert.IsFalse(LogicCardRuntimeState.IsBound);
    }

    [Test]
    public void EntityBase_DoesNotAdvanceLogicFromRenderUpdate()
    {
        string scriptsRoot = Path.Combine(Application.dataPath, "AAAGame", "Scripts");
        string entityBaseSource = File.ReadAllText(
            Path.Combine(scriptsRoot, "Entity", "Core", "EntityBase.cs"));

        Assert.That(
            entityBaseSource,
            Does.Not.Contain("OnLogicFrameUpdate((Fix64)elapseSeconds)"),
            "EntityBase must not advance logic from EntityLogic.OnUpdate render delta.");
    }

    [Test]
    public void GroupMoveManager_DoesNotAdvanceAuthorityQueuesFromRenderUpdate()
    {
        string scriptsRoot = Path.Combine(Application.dataPath, "AAAGame", "Scripts");
        string groupMoveManagerSource = File.ReadAllText(
            Path.Combine(scriptsRoot, "Movement", "GroupMoveManager.cs"));

        Assert.That(
            groupMoveManagerSource,
            Does.Not.Contain("private void Update()"),
            "GroupMoveManager must advance authority queues only from the logic-frame listener or explicit synchronous setup prewarm.");
    }

    [Test]
    public void InteractionAuthority_IsOwnedByLogicRuntime_NotHeroView()
    {
        Assert.IsFalse(
            typeof(ILogicFrameUpdate).IsAssignableFrom(typeof(InteractionManager)),
            "InteractionManager is a presentation component and must not own logic-frame target selection.");

        string scriptsRoot = Path.Combine(Application.dataPath, "AAAGame", "Scripts");
        string authorityPath = Path.Combine(
            scriptsRoot,
            "Interaction",
            "LogicInteractionAuthorityService.cs");
        Assert.IsTrue(
            File.Exists(authorityPath),
            "Viewless player interaction requires a logic-owned authority service.");

        string runtimeProcedureSource = File.ReadAllText(
            Path.Combine(scriptsRoot, "Procedures", "RuntimeProcedureBase.cs"));
        Assert.That(runtimeProcedureSource, Does.Contain("LogicInteractionAuthorityService.BeginTimeline();"));
        Assert.That(runtimeProcedureSource, Does.Contain("LogicInteractionAuthorityService.EndTimeline();"));
    }

    [Test]
    public void GlobalBuffManager_DoesNotInitializeDeterministicDependenciesFromRenderUpdate()
    {
        string scriptsRoot = Path.Combine(Application.dataPath, "AAAGame", "Scripts");
        string globalBuffManagerSource = File.ReadAllText(
            Path.Combine(scriptsRoot, "Buff", "GlobalBuffManager.cs"));

        Assert.That(
            globalBuffManagerSource,
            Does.Not.Contain("private void Update()"),
            "GlobalBuffManager must initialize deterministic dependencies at the preload barrier, not from a render-frame retry.");

        string preloadProcedureSource = File.ReadAllText(
            Path.Combine(scriptsRoot, "Procedures", "PreloadProcedure.cs"));
        Assert.That(
            preloadProcedureSource,
            Does.Not.Contain("GetComponent<GlobalBuffManager>()?.PrepareRuntimeDependencies()"),
            "The preload barrier must fail explicitly when GlobalBuffManager is missing.");

        string buildManagerSource = File.ReadAllText(
            Path.Combine(scriptsRoot, "Build", "BuildManager.cs"));
        Assert.That(
            buildManagerSource,
            Does.Not.Contain("private void Update()"),
            "BuildManager must establish authority event subscriptions at the preload barrier, not from a render-frame retry.");

        Assert.That(
            preloadProcedureSource,
            Does.Contain("buildManager.PrepareRuntimeDependencies();"),
            "The preload barrier must validate BuildManager authority event subscriptions.");

        string rewardManagerSource = File.ReadAllText(
            Path.Combine(scriptsRoot, "MeiyouUtility", "RewardManager.cs"));
        Assert.That(
            rewardManagerSource,
            Does.Not.Contain("private void Update()"),
            "RewardManager must subscribe to logic events from its lifecycle boundary, not from a render-frame retry.");

        Assert.That(
            buildManagerSource,
            Does.Not.Contain("GetComponent<TechManager>()?.RollbackTechsForBuilding"),
            "Building recycle must reject a missing TechManager before mutating its transaction.");
        Assert.That(
            buildManagerSource,
            Does.Not.Contain("GetComponent<GlobalBuffManager>()?.ClearBuildingRuntimeTechState"),
            "Building recycle must reject a missing GlobalBuffManager before mutating its transaction.");

        string techManagerSource = File.ReadAllText(
            Path.Combine(scriptsRoot, "Build", "Tech", "TechManager.cs"));
        Assert.That(
            techManagerSource,
            Does.Not.Contain("globalBuffManager?."),
            "Tech rollback must reject a missing GlobalBuffManager before reducing persistent tech stacks.");
    }

    private void Register(ILogicFrameUpdate listener)
    {
        LogicFrameRuntime.Register(listener);
        m_Registered.Add(listener);
    }

    private sealed class StableListener : ILogicFrameUpdate, ILogicFrameStableOrder
    {
        private readonly List<long> m_Ticks;

        public StableListener(long stableKey, List<long> ticks, int order = 0)
        {
            LogicFrameStableKey = stableKey;
            LogicFrameOrder = order;
            m_Ticks = ticks;
        }

        public int LogicFrameOrder { get; }
        public long LogicFrameStableKey { get; }

        public void OnLogicFrameUpdate(Fix64 deltaTime)
        {
            m_Ticks.Add(LogicFrameStableKey);
        }
    }

    private sealed class CardStateContributorStub : ILogicCardRuntimeStateContributor
    {
        public CardStateContributorStub(int value)
        {
            Value = value;
        }

        public int Value { get; set; }

        public void WriteDeterministicState(LogicStateHasher hasher)
        {
            hasher.Add(Value);
        }
    }

    private sealed class LegacyListener : ILogicFrameUpdate
    {
        private readonly long m_Value;
        private readonly List<long> m_Ticks;

        public LegacyListener(long value, List<long> ticks)
        {
            m_Value = value;
            m_Ticks = ticks;
        }

        public int LogicFrameOrder => 0;

        public void OnLogicFrameUpdate(Fix64 deltaTime)
        {
            m_Ticks.Add(m_Value);
        }
    }
}
