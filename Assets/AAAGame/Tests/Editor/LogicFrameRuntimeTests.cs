using System;
using System.Collections.Generic;
using NUnit.Framework;

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
    public void DefendSchedule_QuantizesDurationOnceToAbsoluteTickOffset()
    {
        Assert.AreEqual(31UL, DefendPhaseRuntime.GetEditorTestTickCount(Fix64.One));
        Assert.AreEqual(62UL, checked(DefendPhaseRuntime.GetEditorTestTickCount(Fix64.One) * 2UL));
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
