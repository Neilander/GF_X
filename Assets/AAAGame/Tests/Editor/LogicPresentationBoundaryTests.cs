using System;
using System.Reflection;
using AAAGame.MiniMap.FOG3;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class LogicPresentationBoundaryTests
{
    [SetUp]
    public void SetUp()
    {
        if (LogicFrameRuntime.IsActive)
            throw new InvalidOperationException("LogicPresentationBoundaryTests requires an inactive logic runtime.");
        EntityRegistry.Clear();
        LogicStrongholdMap.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicFrameRuntime.IsActive)
            LogicFrameRuntime.End();
        EntityRegistry.Clear();
        LogicStrongholdMap.Clear();
    }

    [Test]
    public void StrongholdCapture_CommitsLogicWithoutViewsAndQueuesPresentation()
    {
        LogicTestInGameDataModelAuthority.Ensure(
            GamePhase.Defend,
            nameof(LogicPresentationBoundaryTests));

        const string strongholdId = "SH_VIEWLESS_CAPTURE";
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition(
                    strongholdId,
                    0,
                    0,
                    EntitySideHelper.PlayerFactionId),
            });

        var levelObject = new GameObject("LevelEntity_ViewlessCapture_Test");
        var levelEntity = levelObject.AddComponent<LevelEntity>();
        MethodInfo capture = typeof(LevelEntity).GetMethod(
            "CaptureStronghold",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(capture);

        var listener = new CallbackListener(() =>
            capture.Invoke(levelEntity, new object[] { strongholdId, EntitySideHelper.EnemyFactionId }));
        LogicFrameRuntime.Begin();
        LogicFrameRuntime.StartTimeline();
        LogicFrameRuntime.Register(listener);
        try
        {
            LogicFrameRuntime.Tick(1);

            Assert.AreEqual(
                EntitySideHelper.EnemyFactionId,
                LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId));
            Assert.AreEqual(1, levelEntity.PendingStrongholdCapturePresentationCount);
            Assert.AreEqual(0, EntityRegistry.AllEntities.Count);
        }
        finally
        {
            LogicFrameRuntime.Unregister(listener);
            UnityEngine.Object.DestroyImmediate(levelObject);
        }
    }

    [Test]
    public void LegacyTutorialTrigger_DoesNotRegisterEmptyLogicListener()
    {
        Assert.IsFalse(
            typeof(ILogicFrameUpdate).IsAssignableFrom(typeof(TutorialTriggerCollider)),
            "The replaced PhysX tutorial trigger must remain presentation-only.");

        var triggerObject = new GameObject("LegacyTutorialTrigger_PresentationOnly_Test");
        triggerObject.SetActive(false);
        BoxCollider collider = triggerObject.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        triggerObject.AddComponent<TutorialTriggerCollider>();
        triggerObject.SetActive(true);

        try
        {
            Assert.AreEqual(0, LogicFrameRuntime.ListenerCount);
            LogicFrameRuntime.Begin();
            Assert.AreEqual(
                0,
                LogicFrameRuntime.ListenerCount,
                "An obsolete view trigger must not join the authoritative logic timeline.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(triggerObject);
        }
    }

    [Test]
    public void PhaseApplyBeforeTick_QueuesCardShutdownPresentation()
    {
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        var setupObject = new GameObject("CardSetup_PreTickPresentationBoundary_Test");
        CardSetup cardSetup = setupObject.AddComponent<CardSetup>();
        FieldInfo queueField = typeof(CardSetup).GetField(
            "m_PendingUiPresentation",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(queueField);

        try
        {
            LogicFrameRuntime.Begin();
            LogicFrameRuntime.StartTimeline();
            LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.Invade);
            LogicFrameRuntime.BeginFrameExecution(1);
            try
            {
                LogicTimeControlService.BeginFrame(1);
                LogicPhaseCommandService.ApplyFrameForTests(
                    1,
                    _ =>
                    {
                        Assert.IsFalse(LogicFrameRuntime.IsTicking);
                        Assert.IsTrue(LogicFrameRuntime.IsExecutingFrame);
                        cardSetup.CardSystemShutdown(false);
                    });
            }
            finally
            {
                LogicFrameRuntime.EndFrameExecution(1);
            }

            var queue = queueField.GetValue(cardSetup) as System.Collections.ICollection;
            Assert.NotNull(queue);
            Assert.AreEqual(1, queue.Count);
        }
        finally
        {
            if (LogicFrameRuntime.IsActive)
                LogicFrameRuntime.End();
            UnityEngine.Object.DestroyImmediate(setupObject);
            LogicPhaseCommandService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    [Test]
    public void LifecycleApply_CommitsAuthorityWithoutCallingViewLifecycle()
    {
        string source = System.IO.File.ReadAllText(System.IO.Path.Combine(
            Application.dataPath,
            "AAAGame/Scripts/Entity/LogicEntityLifecycleService.cs"));
        int applyStart = source.IndexOf(
            "private static void ApplyFrameCore(",
            StringComparison.Ordinal);
        int applyEnd = source.IndexOf(
            "public static void UpdatePresentation(",
            applyStart,
            StringComparison.Ordinal);
        Assert.That(applyStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(applyEnd, Is.GreaterThan(applyStart));

        string applyBody = source.Substring(applyStart, applyEnd - applyStart);
        Assert.That(applyBody, Does.Not.Contain("ActivateLogicParticipation("));
        Assert.That(applyBody, Does.Not.Contain("DeactivateLogicParticipation("));
        Assert.That(applyBody, Does.Not.Contain("LogicEntityViewSpawnQueue."));

        string initializationBody = ExtractSourceBlock(
            source,
            "public static void CommitPendingInitializationEntities()",
            "private static LogicEntityId RequestSpawnCore(");
        Assert.That(initializationBody, Does.Not.Contain("ActivateLogicParticipation("));
        Assert.That(initializationBody, Does.Contain("LifecyclePresentationKind.Activate"));
    }

    [Test]
    public void RuntimeEntityCreation_QueuesViewsWithoutUsingRenderTimeInLogicCallers()
    {
        string factorySource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            Application.dataPath,
            "AAAGame/Scripts/Entity/MAEntityFactory.cs"));
        int buildingStart = factorySource.IndexOf(
            "public static LogicEntityId ShowBuildingFixed(",
            StringComparison.Ordinal);
        int buildingEnd = factorySource.IndexOf(
            "private static EntityParams CreateBuildingEntityParams(",
            buildingStart,
            StringComparison.Ordinal);
        Assert.That(buildingStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(buildingEnd, Is.GreaterThan(buildingStart));
        string buildingBody = factorySource.Substring(buildingStart, buildingEnd - buildingStart);
        Assert.That(buildingBody, Does.Not.Contain("GF.Entity.ShowEntity"));
        Assert.That(buildingBody, Does.Contain("LogicEntityViewSpawnQueue.EnqueueBuilding("));

        int heroStart = factorySource.IndexOf(
            "public static LogicEntityId ShowHeroFixed(",
            StringComparison.Ordinal);
        int heroEnd = factorySource.IndexOf(
            "public static LogicEntityId ShowBuildingFixed(",
            heroStart,
            StringComparison.Ordinal);
        Assert.That(heroStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(heroEnd, Is.GreaterThan(heroStart));
        string heroBody = factorySource.Substring(heroStart, heroEnd - heroStart);
        Assert.That(heroBody, Does.Not.Contain("GF.Entity.ShowEntity"));
        Assert.That(heroBody, Does.Contain("LogicEntityViewSpawnQueue.EnqueueHero("));

        string queueSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            Application.dataPath,
            "AAAGame/Scripts/Entity/LogicEntityViewSpawnQueue.cs"));
        int enqueueStart = queueSource.IndexOf(
            "public static int EnqueueSoldier(",
            StringComparison.Ordinal);
        int enqueueEnd = queueSource.IndexOf(
            "public static void EnqueueHide(",
            enqueueStart,
            StringComparison.Ordinal);
        Assert.That(enqueueStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(enqueueEnd, Is.GreaterThan(enqueueStart));
        Assert.That(
            queueSource.Substring(enqueueStart, enqueueEnd - enqueueStart),
            Does.Not.Contain("Time.frameCount"));
    }

    [Test]
    public void LifecycleApply_DefersBoundViewActivationUntilRenderPresentation()
    {
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        LogicEntityLifecycleService.BeginTimeline();
        LogicEntityState state = CreateConfiguredState("Unit_LifecyclePresentation");
        var viewObject = new GameObject("LifecyclePresentationView");
        MAEntity view = viewObject.AddComponent<MAEntity>();
        SetBoundViewIdentity(view, state);
        LogicEntityLifecycleService.BindView(state.EntityId, 404, view);

        try
        {
            LogicTimeControlService.BeginFrame(1);
            LogicEntityLifecycleService.ApplyFrame(1);

            Assert.IsTrue(state.IsSpawnCommitted);
            Assert.IsFalse(view.IsLogicActive);
            Assert.AreEqual(1, LogicEntityLifecycleService.PendingPresentationCount);

            LogicEntityLifecycleService.UpdatePresentation();

            Assert.IsTrue(view.IsLogicActive);
            Assert.AreEqual(0, LogicEntityLifecycleService.PendingPresentationCount);
        }
        finally
        {
            LogicEntityLifecycleService.UnbindView(state.EntityId, 404);
            EntityRegistry.Clear();
            LogicEntityLifecycleService.EndTimeline();
            LogicPhaseCommandService.EndTimeline();
            LogicTimeControlService.EndTimeline();
            UnityEngine.Object.DestroyImmediate(viewObject);
        }
    }

    [Test]
    public void LifecycleInitializationCommit_DefersBoundViewActivationUntilRenderPresentation()
    {
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        LogicEntityLifecycleService.BeginTimeline();
        LogicEntityState state = CreateConfiguredState("Unit_InitializationPresentation");
        var viewObject = new GameObject("InitializationPresentationView");
        MAEntity view = viewObject.AddComponent<MAEntity>();
        SetBoundViewIdentity(view, state);
        LogicEntityLifecycleService.BindView(state.EntityId, 405, view);

        try
        {
            LogicEntityLifecycleService.CommitPendingInitializationEntities();

            Assert.IsTrue(state.IsSpawnCommitted);
            Assert.IsFalse(view.IsLogicActive);
            Assert.AreEqual(1, LogicEntityLifecycleService.PendingPresentationCount);

            LogicEntityLifecycleService.UpdatePresentation();

            Assert.IsTrue(view.IsLogicActive);
            Assert.AreEqual(0, LogicEntityLifecycleService.PendingPresentationCount);
        }
        finally
        {
            LogicEntityLifecycleService.UnbindView(state.EntityId, 405);
            EntityRegistry.Clear();
            LogicEntityLifecycleService.EndTimeline();
            LogicPhaseCommandService.EndTimeline();
            LogicTimeControlService.EndTimeline();
            UnityEngine.Object.DestroyImmediate(viewObject);
        }
    }

    [Test]
    public void LifecycleBindView_DuringLogicTick_IsRejectedWithoutBinding()
    {
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        LogicEntityLifecycleService.BeginTimeline();
        LogicEntityState state = CreateConfiguredState("Unit_TickViewBindingRejected");
        LogicEntityLifecycleService.CommitPendingInitializationEntities();
        var viewObject = new GameObject("TickViewBindingRejected");
        MAEntity view = viewObject.AddComponent<MAEntity>();
        SetBoundViewIdentity(view, state);
        var listener = new CallbackListener(() =>
            LogicEntityLifecycleService.BindView(state.EntityId, 408, view));

        LogicFrameRuntime.Begin();
        LogicFrameRuntime.StartTimeline();
        LogicFrameRuntime.Register(listener);
        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => LogicFrameRuntime.Tick(1));
            StringAssert.Contains("cannot run during a logic frame", exception.Message);
            Assert.AreEqual(0, LogicEntityLifecycleService.BoundViewCount);
            Assert.IsFalse(view.IsLogicActive);
        }
        finally
        {
            LogicFrameRuntime.Unregister(listener);
            LogicFrameRuntime.End();
            if (LogicEntityLifecycleService.BoundViewCount != 0)
                LogicEntityLifecycleService.UnbindView(state.EntityId, 408);
            EntityRegistry.Clear();
            LogicEntityLifecycleService.EndTimeline();
            LogicPhaseCommandService.EndTimeline();
            LogicTimeControlService.EndTimeline();
            UnityEngine.Object.DestroyImmediate(viewObject);
        }
    }

    [Test]
    public void LifecycleDespawn_KeepsViewActiveUntilRenderPresentation()
    {
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        LogicEntityLifecycleService.BeginTimeline();
        LogicEntityState state = CreateConfiguredState("Unit_DespawnPresentation");
        var viewObject = new GameObject("DespawnPresentationView");
        MAEntity view = viewObject.AddComponent<MAEntity>();
        SetBoundViewIdentity(view, state);
        LogicEntityLifecycleService.BindView(state.EntityId, 406, view);

        try
        {
            LogicTimeControlService.BeginFrame(1);
            LogicEntityLifecycleService.ApplyFrame(1);
            LogicEntityLifecycleService.UpdatePresentation();
            Assert.IsTrue(view.IsLogicActive);

            LogicEntityLifecycleService.RequestDespawn(state.EntityId);
            LogicTimeControlService.BeginFrame(2);
            LogicEntityLifecycleService.ApplyFrame(2);

            Assert.IsTrue(state.IsDespawnCommitted);
            Assert.IsTrue(view.IsLogicActive);
            Assert.AreEqual(1, LogicEntityLifecycleService.PendingPresentationCount);
        }
        finally
        {
            LogicEntityLifecycleService.UnbindView(state.EntityId, 406);
            EntityRegistry.Clear();
            LogicEntityLifecycleService.EndTimeline();
            LogicPhaseCommandService.EndTimeline();
            LogicTimeControlService.EndTimeline();
            UnityEngine.Object.DestroyImmediate(viewObject);
        }
    }

    [Test]
    public void LifecycleCatchUpSpawnThenDespawn_DoesNotActivateIntermediateView()
    {
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        LogicEntityLifecycleService.BeginTimeline();
        LogicEntityState state = CreateConfiguredState("Unit_CatchUpPresentation");
        var viewObject = new GameObject("CatchUpPresentationView");
        MAEntity view = viewObject.AddComponent<MAEntity>();
        SetBoundViewIdentity(view, state);
        LogicEntityLifecycleService.BindView(state.EntityId, 407, view);

        try
        {
            LogicTimeControlService.BeginFrame(1);
            LogicEntityLifecycleService.ApplyFrame(1);
            Assert.IsFalse(view.IsLogicActive);
            Assert.AreEqual(1, LogicEntityLifecycleService.PendingPresentationCount);

            LogicEntityLifecycleService.RequestDespawn(state.EntityId);
            LogicTimeControlService.BeginFrame(2);
            LogicEntityLifecycleService.ApplyFrame(2);

            Assert.IsTrue(state.IsDespawnCommitted);
            Assert.IsFalse(view.IsLogicActive);
            Assert.AreEqual(1, LogicEntityLifecycleService.PendingPresentationCount);
        }
        finally
        {
            LogicEntityLifecycleService.UnbindView(state.EntityId, 407);
            EntityRegistry.Clear();
            LogicEntityLifecycleService.EndTimeline();
            LogicPhaseCommandService.EndTimeline();
            LogicTimeControlService.EndTimeline();
            UnityEngine.Object.DestroyImmediate(viewObject);
        }
    }

    [Test]
    public void FogRegistryPresentation_RegisteredThenUnregisteredBeforeViewBinding_DoesNotResolveRetiredLogicEntity()
    {
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        LogicEntityLifecycleService.BeginTimeline();
        LogicEntityState state = CreateConfiguredState("Unit_FogUnboundRetirement");
        var managerObject = new GameObject("FogUnboundRetirementManager");
        managerObject.SetActive(false);
        Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
        MethodInfo subscribe = typeof(Fog3Manager).GetMethod(
            "TrySubscribeEvents",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo updatePresentation = typeof(Fog3Manager).GetMethod(
            "UpdateEntityRegistryPresentation",
            BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo requestQueue = typeof(Fog3Manager).GetField(
            "entityRegistryPresentationRequests",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(subscribe);
        Assert.NotNull(updatePresentation);
        Assert.NotNull(requestQueue);

        try
        {
            subscribe.Invoke(manager, null);
            LogicTimeControlService.BeginFrame(1);
            LogicEntityLifecycleService.ApplyFrame(1);

            LogicEntityLifecycleService.RequestDespawn(state.EntityId);
            LogicTimeControlService.BeginFrame(2);
            LogicEntityLifecycleService.ApplyFrame(2);

            Assert.IsFalse(EntityRegistry.TryGet(state.EntityId, out _));
            Assert.DoesNotThrow(() => updatePresentation.Invoke(manager, null));
            var queue = requestQueue.GetValue(manager) as System.Collections.ICollection;
            Assert.NotNull(queue);
            Assert.AreEqual(0, queue.Count);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(managerObject);
            EntityRegistry.Clear();
            LogicEntityLifecycleService.EndTimeline();
            LogicPhaseCommandService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    [Test]
    public void FogRegistryPresentation_UnregisteredRequestRetiresCapturedViewAfterLifecycleRemoval()
    {
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        LogicEntityLifecycleService.BeginTimeline();
        LogicEntityState state = CreateConfiguredState("Unit_FogBoundRetirement");
        var managerObject = new GameObject("FogBoundRetirementManager");
        managerObject.SetActive(false);
        Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
        var controller = new Fog3Controller();
        controller.Initialize(new Fog3TerrainInfo(
            1,
            1,
            1f,
            Vector3.zero,
            new[] { true },
            "FogBoundRetirementTest"));
        typeof(Fog3Manager).GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(manager, controller);
        typeof(Fog3Manager).GetField("isInitialized", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(manager, true);
        MethodInfo subscribe = typeof(Fog3Manager).GetMethod(
            "TrySubscribeEvents",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo updatePresentation = typeof(Fog3Manager).GetMethod(
            "UpdateEntityRegistryPresentation",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(subscribe);
        Assert.NotNull(updatePresentation);

        var viewObject = new GameObject("FogBoundRetirementView");
        MAEntity view = viewObject.AddComponent<MAEntity>();
        SetBoundViewIdentity(view, state);
        const int viewEntityId = 404;
        LogicEntityLifecycleService.BindView(state.EntityId, viewEntityId, view);
        bool viewBound = true;

        try
        {
            subscribe.Invoke(manager, null);
            LogicTimeControlService.BeginFrame(1);
            LogicEntityLifecycleService.ApplyFrame(1);
            int revealerId = manager.RegisterRevealer(
                view.transform,
                1f,
                viewEntityId,
                false,
                true,
                state.EntityId.Value);
            Assert.Greater(revealerId, 0);

            LogicEntityLifecycleService.RequestDespawn(state.EntityId);
            LogicTimeControlService.BeginFrame(2);
            LogicEntityLifecycleService.ApplyFrame(2);
            LogicEntityLifecycleService.UnbindView(state.EntityId, viewEntityId);
            viewBound = false;

            Assert.DoesNotThrow(() => updatePresentation.Invoke(manager, null));
            var entityRevealers = (System.Collections.Generic.Dictionary<int, int>)typeof(Fog3Manager)
                .GetField("entityRevealers", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(manager);
            Assert.NotNull(entityRevealers);
            Assert.IsFalse(entityRevealers.ContainsKey(viewEntityId));
            Assert.IsFalse(controller.TryGetRevealer(revealerId, out _));
        }
        finally
        {
            if (viewBound)
                LogicEntityLifecycleService.UnbindView(state.EntityId, viewEntityId);
            UnityEngine.Object.DestroyImmediate(managerObject);
            UnityEngine.Object.DestroyImmediate(viewObject);
            EntityRegistry.Clear();
            LogicEntityLifecycleService.EndTimeline();
            LogicPhaseCommandService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    [Test]
    public void GameEndLogicEvent_OnlyQueuesRenderPresentation()
    {
        var managerObject = new GameObject("GameEndPresentationQueue");
        GameEndManager manager = managerObject.AddComponent<GameEndManager>();
        try
        {
            MethodInfo callback = typeof(GameEndManager).GetMethod(
                "OnLogicGameEnded",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo queueField = typeof(GameEndManager).GetField(
                "m_PendingEndPresentation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(callback);
            Assert.NotNull(queueField);

            Assert.DoesNotThrow(() => callback.Invoke(
                manager,
                new object[] { LogicGameEndResult.CreateWin() }));

            var queue = queueField.GetValue(manager) as System.Collections.ICollection;
            Assert.NotNull(queue);
            Assert.AreEqual(1, queue.Count);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void RenderOnlyConsumers_DoNotSubscribePresentationWorkToLogicCallbacks()
    {
        string inputSource = ReadProjectSource("AAAGame/Scripts/UTManagers/InputManager.cs");
        Assert.That(inputSource, Does.Not.Contain("LogicTimeControlService.Changed +="));
        Assert.That(inputSource, Does.Contain("UpdateLogicPausePresentation();"));

        string fogSource = ReadProjectSource("AAAGame/Scripts/MiniMap/FOG3/Fog3Manager.cs");
        string registeredCallback = ExtractSourceBlock(
            fogSource,
            "private void OnLogicEntityRegistered(",
            "private void OnLogicEntityUnregistered(");
        string unregisteredCallback = ExtractSourceBlock(
            fogSource,
            "private void OnLogicEntityUnregistered(",
            "private void EnqueueEntityRegistryPresentation(");
        Assert.That(registeredCallback, Does.Contain("EnqueueEntityRegistryPresentation("));
        Assert.That(unregisteredCallback, Does.Contain("EnqueueEntityRegistryPresentation("));
        Assert.That(registeredCallback, Does.Not.Contain("TryResolveBoundView("));
        Assert.That(unregisteredCallback, Does.Not.Contain("TryResolveBoundView("));
        Assert.That(registeredCallback, Does.Not.Contain("TryRegisterEntity("));
        Assert.That(unregisteredCallback, Does.Not.Contain("UnregisterRevealer("));
    }

    private static LogicEntityState CreateConfiguredState(string characterKey)
    {
        var descriptor = new LogicEntitySpawnDescriptor(
            FixVector2.Zero,
            new FixVector2(Fix64.Zero, Fix64.One),
            SideType.PlayerSide,
            characterKey);
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

    private static void SetBoundViewIdentity(MAEntity view, LogicEntityState state)
    {
        typeof(MAEntity).GetField("_logicState", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(view, state);
        typeof(MAEntity).GetProperty(nameof(MAEntity.LogicEntityId))
            ?.SetValue(view, state.EntityId);
        typeof(GeneralCreature).GetProperty(nameof(GeneralCreature.CharacterKey))
            ?.SetValue(view, state.CharacterKey);
    }

    private static string ReadProjectSource(string relativePath)
    {
        return System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, relativePath));
    }

    private static string ExtractSourceBlock(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), startMarker);
        Assert.That(end, Is.GreaterThan(start), endMarker);
        return source.Substring(start, end - start);
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
}
