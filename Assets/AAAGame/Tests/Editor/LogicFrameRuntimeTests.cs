using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
    public void CompleteRenderFrame_PublishesDeferredRealtimeDebt()
    {
        LogicFrameRuntime.CompleteRenderFrame(4, 0.02d, 1.75d, 0.6d);

        Assert.AreEqual(4, LogicFrameRuntime.LastRenderFrameTickCount);
        Assert.That(LogicFrameRuntime.BacklogSeconds, Is.EqualTo(0.02d).Within(1e-9d));
        Assert.That(LogicFrameRuntime.DeferredRealtimeSeconds, Is.EqualTo(1.75d).Within(1e-9d));
        Assert.That(LogicFrameRuntime.Interpolation, Is.EqualTo(0.6d).Within(1e-9d));
    }

    [Test]
    public void CompleteRenderFrame_RejectsInvalidDeferredRealtimeDebt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LogicFrameRuntime.CompleteRenderFrame(0, 0d, double.NaN, 0d));
    }

    [Test]
    public void FrameExecutionScope_CoversPreTickTickAndPostTickWork()
    {
        bool observedExecutingDuringTick = false;
        Register(new CallbackListener(() =>
        {
            Assert.IsTrue(LogicFrameRuntime.IsTicking);
            observedExecutingDuringTick = LogicFrameRuntime.IsExecutingFrame;
        }));

        LogicFrameRuntime.BeginFrameExecution(1);
        try
        {
            Assert.IsTrue(LogicFrameRuntime.IsExecutingFrame);
            Assert.IsFalse(LogicFrameRuntime.IsTicking);
            Assert.Throws<InvalidOperationException>(
                () => LogicFrameRuntime.BeginFrameExecution(1));
            Assert.Throws<InvalidOperationException>(LogicFrameRuntime.SyncPresentationPhysics);

            LogicFrameRuntime.Tick(1);

            Assert.IsTrue(observedExecutingDuringTick);
            Assert.IsTrue(LogicFrameRuntime.IsExecutingFrame);
            Assert.IsFalse(LogicFrameRuntime.IsTicking);
        }
        finally
        {
            LogicFrameRuntime.EndFrameExecution(1);
        }

        Assert.IsFalse(LogicFrameRuntime.IsExecutingFrame);
    }

    [Test]
    public void FrameExecutionScope_RejectsMismatchedEndWithoutLosingOwner()
    {
        LogicFrameRuntime.BeginFrameExecution(1);
        try
        {
            Assert.Throws<InvalidOperationException>(
                () => LogicFrameRuntime.EndFrameExecution(2));
            Assert.IsTrue(LogicFrameRuntime.IsExecutingFrame);
        }
        finally
        {
            LogicFrameRuntime.EndFrameExecution(1);
        }
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
    public void AuthoredNavigationSourceMutation_RejectsDuringTimeline()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            FlowFieldCrowdMovementSystem.ClearAuthoredNavigationSource);

        StringAssert.Contains("logic timeline", exception.Message);
    }

    [Test]
    public void Ended事件必须在Runtime和Timeline完全关闭后触发()
    {
        bool invoked = false;
        Action handler = () =>
        {
            invoked = true;
            Assert.IsFalse(LogicFrameRuntime.IsActive);
            Assert.IsFalse(LogicFrameRuntime.IsTimelineRunning);
        };
        LogicFrameRuntime.Ended += handler;
        try
        {
            LogicFrameRuntime.End();
            Assert.IsTrue(invoked);
        }
        finally
        {
            LogicFrameRuntime.Ended -= handler;
            if (!LogicFrameRuntime.IsActive)
            {
                LogicFrameRuntime.Begin();
                LogicFrameRuntime.StartTimeline();
            }
        }
    }

    [Test]
    public void DefendSchedule_QuantizesDurationOnceToAbsoluteTickOffset()
    {
        Assert.AreEqual(31UL, DefendPhaseRuntime.GetEditorTestTickCount(Fix64.One));
        Assert.AreEqual(62UL, checked(DefendPhaseRuntime.GetEditorTestTickCount(Fix64.One) * 2UL));
    }

    [Test]
    public void DefendSpawnWeights_PreserveFractionalRatio()
    {
        Fix64 totalWeight = Fix64.FromRaw(8192L);

        Assert.AreEqual(6, DefendPhaseRuntime.GetEditorTestWeightedSpawnCount(Fix64.FromRaw(6144L), totalWeight, 8));
        Assert.AreEqual(2, DefendPhaseRuntime.GetEditorTestWeightedSpawnCount(Fix64.FromRaw(2048L), totalWeight, 8));
    }

    [Test]
    public void DefendSpawnTracking_QueuesSpawnSpeedForVisibilityRelease()
    {
        DefendPhaseRuntime.CancelRuntime();
        try
        {
            MethodInfo trackMethod = typeof(DefendPhaseRuntime).GetMethod(
                "TrackSpawnedDefendEnemy",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo aliveField = typeof(DefendPhaseRuntime).GetField(
                "s_AliveEnemyLogicEntityIds",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo acceleratedField = typeof(DefendPhaseRuntime).GetField(
                "s_AcceleratedEnemyLogicEntityIds",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(trackMethod);
            Assert.NotNull(aliveField);
            Assert.NotNull(acceleratedField);

            const int entityId = 91001;
            trackMethod.Invoke(null, new object[] { new LogicEntityId(entityId), "Tutorial first defense" });

            var aliveIds = (HashSet<int>)aliveField.GetValue(null);
            var acceleratedIds = (HashSet<int>)acceleratedField.GetValue(null);
            Assert.IsTrue(aliveIds.Contains(entityId));
            Assert.IsTrue(acceleratedIds.Contains(entityId),
                "Every defend enemy with a spawn-speed override must be queued for release on authoritative visibility.");
        }
        finally
        {
            DefendPhaseRuntime.CancelRuntime();
        }
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

        string punchBagSource = File.ReadAllText(
            Path.Combine(scriptsRoot, "Entity", "PunchBagEntity.cs"));
        Assert.That(
            punchBagSource,
            Does.Not.Contain("ShouldRunLogicFrameUpdate => true"),
            "A view-only legacy entity with no LogicEntityState must not register an empty logic tick.");
        Assert.That(punchBagSource, Does.Not.Contain("OnLogicFrameUpdate("));
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

    [Test]
    public void LogicOwnedStateChanges_QueuePresentationOutsideTheLogicTick()
    {
        string scriptsRoot = Path.Combine(Application.dataPath, "AAAGame", "Scripts");
        string globalBuffManagerSource = File.ReadAllText(
            Path.Combine(scriptsRoot, "Buff", "GlobalBuffManager.cs"));
        Assert.That(
            globalBuffManagerSource,
            Does.Not.Contain("building.RaiseArmyCardPropertyChangedEventForTech();"),
            "Tech application must not publish an army-card UI event from its logic apply frame.");

        string levelEntitySource = File.ReadAllText(
            Path.Combine(scriptsRoot, "Entity", "LevelEntity.cs"));
        int captureStart = levelEntitySource.IndexOf(
            "private void CaptureStronghold(",
            StringComparison.Ordinal);
        int captureEnd = levelEntitySource.IndexOf(
            "private void FlushStrongholdCapturePresentation(",
            captureStart,
            StringComparison.Ordinal);
        Assert.That(captureStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(captureEnd, Is.GreaterThan(captureStart));
        string captureBody = levelEntitySource.Substring(captureStart, captureEnd - captureStart);
        Assert.That(captureBody, Does.Not.Contain("GetStrongholdViewRequired("));
        Assert.That(captureBody, Does.Not.Contain("TryGetBoundView("));
        Assert.That(captureBody, Does.Not.Contain(".ChangeSide("));
        Assert.That(captureBody, Does.Not.Contain(".BindStrongholdView("));
        Assert.That(captureBody, Does.Not.Contain("stronghold.OwnerFactionId ="));
        Assert.That(captureBody, Does.Not.Contain("PlayCaptureVfx("));
        Assert.That(captureBody, Does.Not.Contain("RefreshEnemyStrongholdFogEffects("));

        string runtimeProcedureSource = File.ReadAllText(
            Path.Combine(scriptsRoot, "Procedures", "RuntimeProcedureBase.cs"));
        Assert.That(
            runtimeProcedureSource,
            Does.Contain("LevelEntity.UpdateActivePresentation();"),
            "The render-frame procedure must flush stronghold presentation after logic ticks complete.");
        Assert.That(
            runtimeProcedureSource,
            Does.Contain("GlobalBuffManager.RequireCurrent().UpdatePresentation();"),
            "The render-frame procedure must flush army-card presentation after logic ticks complete.");
    }

    [Test]
    public void RuntimeArmyRule_QueuesPresentationDuringTickAndFlushesAfterTick()
    {
        var managerObject = new GameObject("GlobalBuffManager_PresentationBoundary_Test");
        var manager = managerObject.AddComponent<GlobalBuffManager>();
        Register(new CallbackListener(() =>
        {
            Assert.IsTrue(LogicFrameRuntime.IsTicking);
            manager.RegisterRuntimeArmyForceRule(0, "building-a", "Tech_A", _ => Fix64.One);
            Assert.AreEqual(1, manager.PendingArmyCardPresentationFactionCount);
            Assert.Throws<InvalidOperationException>(manager.UpdatePresentation);
            Assert.Throws<InvalidOperationException>(LogicFrameRuntime.SyncPresentationPhysics);
        }));

        try
        {
            LogicFrameRuntime.Tick(1);

            Assert.IsFalse(LogicFrameRuntime.IsTicking);
            Assert.AreEqual(1, manager.PendingArmyCardPresentationFactionCount);
            manager.UpdatePresentation();
            Assert.AreEqual(0, manager.PendingArmyCardPresentationFactionCount);
        }
        finally
        {
            manager.ClearLevelRuntimeState();
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void DefendPhase_RebuildsWaveCacheAfterSpawnPointLevelInvalidation()
    {
        string scriptsRoot = Path.Combine(Application.dataPath, "AAAGame", "Scripts");
        string source = File.ReadAllText(
            Path.Combine(scriptsRoot, "GameClass", "DefendPhaseRuntime.cs"));
        int prepareStart = source.IndexOf("public static void PrepareForCurrentLevelIfNeeded()", StringComparison.Ordinal);
        int spawnPointPrepare = source.IndexOf("ConfigureSpawnPointCacheIfNeeded();", prepareStart, StringComparison.Ordinal);
        int wavePrepare = source.IndexOf("ConfigureWaveRuntimeIfNeeded();", prepareStart, StringComparison.Ordinal);

        Assert.That(prepareStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(spawnPointPrepare, Is.GreaterThan(prepareStart));
        Assert.That(wavePrepare, Is.GreaterThan(spawnPointPrepare),
            "A new level entity invalidates the wave cache while configuring spawn points, so waves must be rebuilt last.");
        Assert.That(
            source,
            Does.Contain("int smallAgentTypeId = AgentTypeHelper.ResolveNavAgentTypeId(UnitSize.Small);"),
            "Spawn-point diagnostics must resolve their own agent type instead of depending on the wave cache.");
    }

    [Test]
    public void RuntimeTick_DoesNotSimulateUnityPhysics_AndTutorialTriggerUsesLogicPosition()
    {
        string scriptsRoot = Path.Combine(Application.dataPath, "AAAGame", "Scripts");
        string runtimeSource = File.ReadAllText(
            Path.Combine(scriptsRoot, "GameClass", "LogicFrameRuntime.cs"));
        Assert.That(
            runtimeSource,
            Does.Not.Contain("Physics.Simulate("),
            "Unity Physics is presentation-only and must not be advanced by the authoritative logic tick.");
        int tickStart = runtimeSource.IndexOf("public static void Tick(ulong frame)", StringComparison.Ordinal);
        int tickEnd = runtimeSource.IndexOf("public static void SyncPresentationPhysics()", tickStart, StringComparison.Ordinal);
        Assert.That(tickStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(tickEnd, Is.GreaterThan(tickStart));
        Assert.That(
            runtimeSource.Substring(tickStart, tickEnd - tickStart),
            Does.Not.Contain("Physics.SyncTransforms()"),
            "Unity Physics transform synchronization belongs to the render frame, not each logic tick.");

        string runtimeProcedureSource = File.ReadAllText(
            Path.Combine(scriptsRoot, "Procedures", "RuntimeProcedureBase.cs"));
        Assert.That(
            runtimeProcedureSource,
            Does.Contain("LogicFrameRuntime.SyncPresentationPhysics();"),
            "Presentation physics must synchronize once after render-frame logic advancement.");

        string tutorialTriggerSource = File.ReadAllText(
            Path.Combine(scriptsRoot, "MeiyouUtility", "TutorialTriggerCollider.cs"));
        Assert.That(tutorialTriggerSource, Does.Not.Contain("OnTriggerEnter("));
        Assert.That(tutorialTriggerSource, Does.Not.Contain("ILogicFrameUpdate"));
        Assert.That(tutorialTriggerSource, Does.Not.Contain("OnLogicFrameUpdate("));
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

    private sealed class CallbackListener : ILogicFrameUpdate
    {
        private readonly Action m_Callback;

        public CallbackListener(Action callback)
        {
            m_Callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public int LogicFrameOrder => 0;

        public void OnLogicFrameUpdate(Fix64 deltaTime)
        {
            m_Callback();
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
