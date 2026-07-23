using System;
using System.Collections.Generic;
using AAAGame.Card;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class LogicEntityIdentityTests
{
    private sealed class SkillRefreshProbe : ISkillComp
    {
        public int RefreshCount { get; private set; }
        public void Init(IEntityContext entity, List<ActiveSkillSO> activeSkills, List<PassiveSkillSO> passiveSkills) { }
        public void Skill(Fix64 deltaTime) { }
        public void CancelSkills() { }
        public void OnSkillChanged() => RefreshCount++;
        public void ShutDown() { }
        public void Resume() { }
    }

    private sealed class HealingProbeBuff : BuffCallback
    {
        public Fix64 TotalHealed { get; private set; }
        public override void OnHealed(Fix64 amount) => TotalHealed += amount;
    }

    [SetUp]
    public void SetUp()
    {
        EntityRegistry.Clear();
        LevelTagRuntime.ClearActiveTags();
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        LogicEntityLifecycleService.BeginTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        EntityRegistry.Clear();
        LogicEntityLifecycleService.EndTimeline();
        LogicPhaseCommandService.EndTimeline();
        LogicTimeControlService.EndTimeline();
        LevelTagRuntime.ClearActiveTags();
    }

    [Test]
    public void AllocatorSnapshotRestore_ReplaysSameNextId()
    {
        LogicEntityId first = LogicEntityIdAllocator.Allocate();
        LogicEntityIdAllocatorSnapshot snapshot = LogicEntityIdAllocator.CaptureSnapshot();
        LogicEntityId second = LogicEntityIdAllocator.Allocate();

        LogicEntityIdAllocator.RestoreSnapshot(snapshot);
        LogicEntityId replayedSecond = LogicEntityIdAllocator.Allocate();

        Assert.AreEqual(1, first.Value);
        Assert.AreEqual(2, second.Value);
        Assert.AreEqual(second, replayedSecond);
    }

    [Test]
    public void LevelTag权威应用顺序不依赖DataTable枚举顺序()
    {
        LevelTagTable first = ParseLevelTagRow(41, "LvTag_First");
        LevelTagTable second = ParseLevelTagRow(7, "LvTag_Second");
        LevelTagTable third = ParseLevelTagRow(23, "LvTag_Third");

        int[] forward = LevelTagRuntime.GetEditorTestSortedActiveTagIds(new[] { first, second, third });
        int[] reverse = LevelTagRuntime.GetEditorTestSortedActiveTagIds(new[] { third, second, first });

        CollectionAssert.AreEqual(new[] { 7, 23, 41 }, forward);
        CollectionAssert.AreEqual(forward, reverse);
    }

    [Test]
    public void SpawnRequestOrder_DefinesIds_WhenViewsBindOutOfOrder()
    {
        LogicEntityId first = LogicEntityLifecycleService.RequestSpawn();
        LogicEntityId second = LogicEntityLifecycleService.RequestSpawn();

        LogicEntityLifecycleService.BindView(second, 202);
        LogicEntityLifecycleService.BindView(first, 101);

        Assert.AreEqual(1, first.Value);
        Assert.AreEqual(2, second.Value);
        Assert.AreEqual(2, LogicEntityLifecycleService.Commands.Count);
        Assert.AreEqual(LogicEntityLifecycleCommandKind.SpawnRequested, LogicEntityLifecycleService.Commands[0].Kind);
        Assert.AreEqual(first, LogicEntityLifecycleService.Commands[0].EntityId);
        Assert.AreEqual(second, LogicEntityLifecycleService.Commands[1].EntityId);

        LogicEntityLifecycleService.UnbindView(second, 202);
        LogicEntityLifecycleService.UnbindView(first, 101);
    }

    [Test]
    public void SpawnRequest_CreatesFixedLogicStateBeforeViewBinding()
    {
        var descriptor = new LogicEntitySpawnDescriptor(
            new FixVector2(Fix64.FromRaw(123456789), Fix64.FromRaw(-987654321)),
            new FixVector2((Fix64)3, (Fix64)4),
            SideType.EnemySide,
            "Unit_Test");

        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn(descriptor);
        LogicEntityState state = LogicEntityStateStore.GetRequired(entityId);

        Assert.AreEqual(entityId, state.EntityId);
        Assert.AreEqual(123456789L, state.Position.x.RawValue);
        Assert.AreEqual(-987654321L, state.Position.y.RawValue);
        Assert.AreEqual((new FixVector2((Fix64)3, (Fix64)4).GetNormalized()).x.RawValue, state.Forward.x.RawValue);
        Assert.AreEqual((new FixVector2((Fix64)3, (Fix64)4).GetNormalized()).y.RawValue, state.Forward.y.RawValue);
        Assert.AreEqual(SideType.EnemySide, state.Side);
        Assert.AreEqual("Unit_Test", state.CharacterKey);
        Assert.IsFalse(state.HasBoundView);
        Assert.IsFalse(state.IsSpawnCommitted);
        Assert.AreEqual(1, LogicEntityStateStore.Count);
    }

    [Test]
    public void BuildingQuarterTurns_MapToExactFixedCardinalForward()
    {
        Assert.AreEqual(
            new FixVector2(Fix64.Zero, Fix64.One),
            MAEntityFactory.ResolveBuildingForwardFixed(0));
        Assert.AreEqual(
            new FixVector2(Fix64.One, Fix64.Zero),
            MAEntityFactory.ResolveBuildingForwardFixed(1));
        Assert.AreEqual(
            new FixVector2(Fix64.Zero, -Fix64.One),
            MAEntityFactory.ResolveBuildingForwardFixed(2));
        Assert.AreEqual(
            new FixVector2(-Fix64.One, Fix64.Zero),
            MAEntityFactory.ResolveBuildingForwardFixed(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => MAEntityFactory.ResolveBuildingForwardFixed(4));
    }

    [Test]
    public void DefendEnemySpawnSpeed_IsAppliedBeforeSpawnCommandWithoutView()
    {
        Fix64 assignedSpeed = (Fix64)7.25f;
        EntityParams entityParams = CreateDefendEnemyParams(assignedSpeed);
        bool observedAtCommandRecord = false;
        void OnRecorded(LogicEntityLifecycleCommand command)
        {
            LogicEntityState recordedState = LogicEntityStateStore.GetRequired(command.EntityId);
            Assert.AreEqual(LogicEntityLifecycleCommandKind.SpawnRequested, command.Kind);
            Assert.AreEqual(assignedSpeed.RawValue, recordedState.CreatureProperties.GetProperty(CreatureMainProperty.Speed).RawValue);
            Assert.IsTrue(recordedState.BuffComp.HasBuff(LogicUnitConfigurator.DefendSpeedBuffId));
            Assert.IsFalse(recordedState.HasBoundView);
            Assert.IsFalse(recordedState.IsSpawnCommitted);
            observedAtCommandRecord = true;
        }

        LogicEntityLifecycleService.CommandRecorded += OnRecorded;
        try
        {
            LogicEntityId entityId = LogicEntityLifecycleService.RequestConfiguredSpawn(
                new LogicEntitySpawnDescriptor(
                    FixVector2.Zero,
                    new FixVector2(Fix64.Zero, Fix64.One),
                    SideType.EnemySide,
                    "Unit_DefendSpeed"),
                state =>
                {
                    ConfigureBasicState(state);
                    LogicUnitConfigurator.ConfigureDefendEnemySpawnSpeed(state, entityParams);
                    state.CreatureProperties.ModifyMainPropertyValueBuff(
                        CreatureMainProperty.Speed,
                        PropertyDirectAdditiveModifier.Create((Fix64)260),
                        true);
                    state.CreatureProperties.ModifyMainPropertyMul(
                        CreatureMainProperty.Speed,
                        NormalBaseValueTp.Buff,
                        PropertyDirectAdditiveModifier.Create((Fix64)0.5f),
                        true);
                });

            LogicEntityState state = LogicEntityStateStore.GetRequired(entityId);
            Assert.IsTrue(observedAtCommandRecord);
            Assert.AreEqual(assignedSpeed.RawValue, state.CreatureProperties.GetProperty(CreatureMainProperty.Speed).RawValue);
            Assert.IsFalse(state.HasBoundView);

            LogicTimeControlService.BeginFrame(1);
            LogicEntityLifecycleService.ApplyFrame(1);

            Assert.IsTrue(state.IsSpawnCommitted);
            Assert.AreEqual(assignedSpeed.RawValue, state.CreatureProperties.GetProperty(CreatureMainProperty.Speed).RawValue);

            Assert.IsTrue(state.BuffComp.RemoveBuff(LogicUnitConfigurator.DefendSpeedBuffId));
            Assert.AreEqual(((Fix64)390).RawValue, state.CreatureProperties.GetProperty(CreatureMainProperty.Speed).RawValue);
        }
        finally
        {
            LogicEntityLifecycleService.CommandRecorded -= OnRecorded;
        }
    }

    [Test]
    public void DefendEnemySpawnSpeed_MissingValueThrowsAndDoesNotPublishSpawn()
    {
        EntityParams entityParams = CreateDefendEnemyParams(null);
        Assert.Throws<InvalidOperationException>(() => LogicEntityLifecycleService.RequestConfiguredSpawn(
            new LogicEntitySpawnDescriptor(
                FixVector2.Zero,
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.EnemySide,
                "Unit_DefendSpeedMissing"),
            state =>
            {
                ConfigureBasicState(state);
                LogicUnitConfigurator.ConfigureDefendEnemySpawnSpeed(state, entityParams);
            }));
        Assert.AreEqual(0, LogicEntityLifecycleService.Commands.Count);
        Assert.AreEqual(0, LogicEntityStateStore.Count);
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void DefendEnemySpawnSpeed_NonPositiveRawValueThrows(long speedRaw)
    {
        EntityParams entityParams = CreateDefendEnemyParams(Fix64.FromRaw(speedRaw));
        LogicEntityState state = CreateConfiguredStateForSide("Unit_DefendInvalidSpeed", SideType.EnemySide);
        Assert.Throws<InvalidOperationException>(() =>
            LogicUnitConfigurator.ConfigureDefendEnemySpawnSpeed(state, entityParams));
    }

    [Test]
    public void DefendEnemySpawnSpeed_NonEnemySideThrows()
    {
        EntityParams entityParams = CreateDefendEnemyParams((Fix64)5);
        entityParams.Side = SideType.PlayerSide;
        LogicEntityState state = CreateConfiguredStateForSide("Unit_DefendWrongSide", SideType.PlayerSide);
        Assert.Throws<InvalidOperationException>(() =>
            LogicUnitConfigurator.ConfigureDefendEnemySpawnSpeed(state, entityParams));
    }

    [Test]
    public void DefendEnemyTargetingMode_IsConfiguredBeforeSpawnCommandWithoutView()
    {
        LogicEntityState fallbackBuilding = CreateBuildingQueryState(
            "building-defend-fallback",
            new FixVector2((Fix64)10, Fix64.Zero));
        ActivateRequestedState(fallbackBuilding.EntityId, 1);

        EntityParams entityParams = CreateDefendEnemyParams((Fix64)5);
        CharacterTargetingComp configuredTargeting = null;
        ulong configuredHash = 0;
        ulong expectedHash = 0;
        ulong defaultHash = 0;
        bool observedAtCommandRecord = false;

        void OnRecorded(LogicEntityLifecycleCommand command)
        {
            LogicEntityState recordedState = LogicEntityStateStore.GetRequired(command.EntityId);
            Assert.AreSame(configuredTargeting, recordedState.TargetComp);
            Assert.AreEqual(expectedHash, configuredHash);
            Assert.AreNotEqual(defaultHash, configuredHash);
            Assert.IsFalse(recordedState.HasBoundView);
            Assert.IsFalse(recordedState.IsSpawnCommitted);
            observedAtCommandRecord = true;
        }

        LogicEntityLifecycleService.CommandRecorded += OnRecorded;
        try
        {
            LogicEntityLifecycleService.RequestConfiguredSpawn(
                new LogicEntitySpawnDescriptor(
                    FixVector2.Zero,
                    new FixVector2(Fix64.Zero, Fix64.One),
                    SideType.EnemySide,
                    "Unit_DefendTargeting"),
                state =>
                {
                    ConfigureStateWithoutTargeting(state);
                    configuredTargeting = CreateCharacterTargeting(state);
                    state.SetTargetingComp(configuredTargeting);
                    LogicUnitConfigurator.ConfigureTargetingModeForSpawn(
                        state,
                        entityParams,
                        configuredTargeting,
                        fallbackBuilding);
                    configuredHash = ComputeTargetingHash(configuredTargeting);

                    CharacterTargetingComp expectedTargeting = CreateCharacterTargeting(state);
                    expectedTargeting.UseDefendEnemyMode(fallbackBuilding);
                    expectedHash = ComputeTargetingHash(expectedTargeting);

                    CharacterTargetingComp defaultTargeting = CreateCharacterTargeting(state);
                    defaultHash = ComputeTargetingHash(defaultTargeting);
                });

            Assert.IsTrue(observedAtCommandRecord);
        }
        finally
        {
            LogicEntityLifecycleService.CommandRecorded -= OnRecorded;
        }
    }

    [Test]
    public void TargetingFixedRangeHash_PreservesAdjacentRawValuesHiddenByFloatView()
    {
        var first = new CharacterTargetingComp { AggroRangeFixed = Fix64.FromRaw(123456789) };
        var second = new CharacterTargetingComp { AggroRangeFixed = Fix64.FromRaw(123456790) };

        Assert.AreEqual(first.AggroRange, second.AggroRange,
            "Chosen adjacent fixed values must collapse to the same presentation float for this regression.");
        Assert.AreNotEqual(ComputeTargetingHash(first), ComputeTargetingHash(second));
    }

    [Test]
    public void SpawnDescriptor_PublishesSourceStrongholdAtomicallyWithCommand()
    {
        string observedStrongholdId = null;
        void OnRecorded(LogicEntityLifecycleCommand command)
        {
            observedStrongholdId = LogicEntityStateStore.GetRequired(command.EntityId).SourceStrongholdId;
        }

        LogicEntityLifecycleService.CommandRecorded += OnRecorded;
        try
        {
            var descriptor = new LogicEntitySpawnDescriptor(
                FixVector2.Zero,
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.PlayerSide,
                "Unit_SourceStronghold",
                "SH_0_1");
            LogicEntityLifecycleService.RequestSpawn(descriptor);
        }
        finally
        {
            LogicEntityLifecycleService.CommandRecorded -= OnRecorded;
        }

        Assert.AreEqual("SH_0_1", observedStrongholdId);
    }

    [Test]
    public void ViewBinding_UsesTheExistingLogicStateIdentity()
    {
        LogicEntityState beforeBinding = CreateConfiguredState("Unit_Test", false);
        LogicEntityId entityId = beforeBinding.EntityId;

        LogicEntityLifecycleService.BindView(entityId, 404);

        LogicEntityState afterBinding = LogicEntityStateStore.GetRequired(entityId);
        Assert.AreSame(beforeBinding, afterBinding);
        Assert.AreEqual(404, afterBinding.BoundViewEntityId);
        LogicEntityLifecycleService.UnbindView(entityId, 404);
        Assert.AreEqual(1, LogicEntityStateStore.Count);
        Assert.IsFalse(afterBinding.HasBoundView);

        LogicTimeControlService.BeginFrame(1);
        LogicEntityLifecycleService.ApplyFrame(1);
        Assert.IsTrue(afterBinding.IsSpawnCommitted);
        Assert.AreSame(afterBinding, EntityRegistry.AllEntities[0]);
    }

    [Test]
    public void LifecycleAuthorityCommands_IgnoreViewBindingOrderAndIds()
    {
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn();
        LogicEntityLifecycleService.BindView(entityId, 1001);
        LogicEntityLifecycleService.UnbindView(entityId, 1001);

        Assert.AreEqual(1, LogicEntityLifecycleService.Commands.Count);
        Assert.AreEqual(1UL, LogicEntityLifecycleService.Commands[0].EffectiveFrame);
        Assert.AreEqual(1UL, LogicEntityLifecycleService.Commands[0].Sequence);
        Assert.AreEqual(1UL, LogicEntityLifecycleService.LastSequence);
        Assert.AreEqual(LogicEntityLifecycleCommandKind.SpawnRequested, LogicEntityLifecycleService.Commands[0].Kind);
    }

    [Test]
    public void EndTimeline_RejectsStillBoundViews()
    {
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn();
        LogicEntityLifecycleService.BindView(entityId, 1001);

        Assert.Throws<InvalidOperationException>(() => LogicEntityLifecycleService.EndTimeline());
        Assert.IsTrue(LogicEntityLifecycleService.IsActive);

        LogicEntityLifecycleService.UnbindView(entityId, 1001);
    }

    [Test]
    public void LateViewActivationFailure_RollsBackBindingAtomically()
    {
        LogicEntityState state = CreateConfiguredState("Unit_Expected", false);
        ActivateRequestedState(state.EntityId, 1);
        GameObject gameObject = new GameObject("LateViewActivationFailure");
        try
        {
            MAEntity view = gameObject.AddComponent<MAEntity>();
            typeof(MAEntity).GetField("_logicState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(view, state);
            typeof(MAEntity).GetProperty(nameof(MAEntity.LogicEntityId))
                ?.SetValue(view, state.EntityId);
            typeof(GeneralCreature).GetProperty(nameof(GeneralCreature.CharacterKey))
                ?.SetValue(view, "Unit_Wrong");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => LogicEntityLifecycleService.BindView(state.EntityId, 404, view));

            StringAssert.Contains("character key mismatch", exception.Message);
            Assert.AreEqual(0, LogicEntityLifecycleService.BoundViewCount);
            Assert.IsFalse(state.HasBoundView);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void ViewPresentationReset_PreservesLogicMovementTargetAndOutOfCombatClock()
    {
        LogicEntityState source = CreateConfiguredState("Unit_LatePresentation", false);
        LogicEntityState target = CreateConfiguredStateForSide("Unit_LatePresentation_Target", SideType.EnemySide);
        var move = new CharacterMoveComp();
        source.SetMoveComp(move);
        move.Init(source);
        var targeting = CreateCharacterTargeting(source);
        source.SetTargetingComp(targeting);
        source.SetWeaponComp(new WeaponComp(null));

        FixVector2 destination = new FixVector2((Fix64)8, (Fix64)(-3));
        move.MoveToFixed(destination);
        targeting.CurrentTarget = target;
        source.DurationMoveEffectComp.StartDurationOverrideMove((Fix64)1, new FixVector2((Fix64)2, Fix64.Zero));
        System.Reflection.FieldInfo combatClockField = typeof(LogicEntityState).GetField(
            "m_CombatClock",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        System.Reflection.FieldInfo outOfCombatStartField = typeof(LogicEntityState).GetField(
            "m_OutOfCombatStart",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(combatClockField);
        Assert.NotNull(outOfCombatStartField);
        combatClockField.SetValue(source, (Fix64)7);
        outOfCombatStartField.SetValue(source, (Fix64)2);

        GameObject gameObject = new GameObject("LatePresentationView");
        try
        {
            MAEntity view = gameObject.AddComponent<MAEntity>();
            System.Reflection.FieldInfo logicStateField = typeof(MAEntity).GetField(
                "_logicState",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.PropertyInfo logicEntityIdProperty = typeof(MAEntity).GetProperty(nameof(MAEntity.LogicEntityId));
            System.Reflection.MethodInfo bindComponentsMethod = typeof(MAEntity).GetMethod(
                "BindLogicStateComponents",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.FieldInfo moveExecutorField = typeof(MAEntity).GetField(
                "_moveExecutor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.MethodInfo resetPresentationMethod = typeof(MAEntity).GetMethod(
                "ResetPresentationMovementForShow",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.MethodInfo initializePoseMethod = typeof(MAEntity).GetMethod(
                "InitializePresentationPoseFromLogicState",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(logicStateField);
            Assert.NotNull(logicEntityIdProperty);
            Assert.NotNull(bindComponentsMethod);
            Assert.NotNull(moveExecutorField);
            Assert.NotNull(resetPresentationMethod);
            Assert.NotNull(initializePoseMethod);
            logicStateField.SetValue(view, source);
            logicEntityIdProperty.SetValue(view, source.EntityId);
            bindComponentsMethod.Invoke(view, null);
            MoveExecutor presenterExecutor = gameObject.AddComponent<MoveExecutor>();
            presenterExecutor.SetInputFixed(new FixVector2((Fix64)11, (Fix64)12));
            presenterExecutor.SetOverrideFixed(new FixVector2((Fix64)13, (Fix64)14));
            presenterExecutor.SetMovementMode(MovementMode.Displaced);
            moveExecutorField.SetValue(view, presenterExecutor);
            gameObject.transform.position = new Vector3(99f, 2f, -77f);
            gameObject.transform.rotation = Quaternion.Euler(0f, 123f, 0f);

            resetPresentationMethod.Invoke(view, null);
            FixVector2 forwardBeforePresentation = source.Forward;
            initializePoseMethod.Invoke(view, null);

            Assert.AreSame(target, targeting.CurrentTarget);
            Assert.IsTrue(move.TryGetNavigationTarget(out Vector3 actualTarget));
            Assert.AreEqual(destination.x.RawValue, ((Fix64)actualTarget.x).RawValue);
            Assert.AreEqual(destination.y.RawValue, ((Fix64)actualTarget.z).RawValue);
            Assert.IsTrue(view.IsOutOfCombat);
            Assert.AreEqual(((Fix64)5).RawValue, view.OutOfCombatElapsedLogicTime.RawValue);
            Assert.AreEqual(Vector3.zero, presenterExecutor.DebugInputVelocity);
            Assert.AreEqual(Vector3.zero, presenterExecutor.DebugExternalVelocity);
            Assert.IsFalse(presenterExecutor.DebugHasOverride);
            Assert.AreEqual(MovementMode.Normal, presenterExecutor.MovementMode);
            Assert.AreEqual(forwardBeforePresentation.x.RawValue, source.Forward.x.RawValue);
            Assert.AreEqual(forwardBeforePresentation.y.RawValue, source.Forward.y.RawValue);
            Assert.AreEqual(source.Position.x.RawValue, ((Fix64)gameObject.transform.position.x).RawValue);
            Assert.AreEqual(source.Position.y.RawValue, ((Fix64)gameObject.transform.position.z).RawValue);
            Vector3 presentedForward = gameObject.transform.forward;
            Assert.AreEqual(source.Forward.x.RawValue, ((Fix64)presentedForward.x).RawValue);
            Assert.AreEqual(source.Forward.y.RawValue, ((Fix64)presentedForward.z).RawValue);
            Assert.Throws<InvalidOperationException>(() => view.TauntLevel = 7);
            Assert.AreEqual(1, source.TauntLevel);

            source.DurationMoveEffectComp.ApplyEffect((Fix64)0.1f);
            Assert.AreEqual(MovementMode.Displaced, source.MoveExecutor.MovementMode);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void ViewComponentSetters_RejectLogicComponentOwnership()
    {
        GameObject gameObject = new GameObject("ViewComponentOwnershipGuard");
        try
        {
            MAEntity view = gameObject.AddComponent<MAEntity>();

            Assert.IsFalse(view is ILogicFrameEntity, "MAEntity View must never participate in the 11-phase logic pipeline.");
            StringAssert.Contains("LogicEntityState", Assert.Throws<InvalidOperationException>(() => view.SetMoveComp(new NoMoveComp())).Message);
            StringAssert.Contains("LogicEntityState", Assert.Throws<InvalidOperationException>(() => view.SetAtkComp(new NoAtkComp())).Message);
            StringAssert.Contains("LogicEntityState", Assert.Throws<InvalidOperationException>(() => view.SetTargetingComp(new NoTargetingComp())).Message);
            StringAssert.Contains("LogicEntityState", Assert.Throws<InvalidOperationException>(() => view.SetWeaponComp(new WeaponComp(null))).Message);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void BuildingViewMutationInterface_RejectsLogicStateChanges()
    {
        GameObject gameObject = new GameObject("BuildingViewMutationGuard");
        try
        {
            IBuildingLogicContext view = gameObject.AddComponent<BuildingEntity>();

            StringAssert.Contains("LogicEntityState", Assert.Throws<InvalidOperationException>(() => view.SetOwnerFaction(2)).Message);
            StringAssert.Contains("LogicEntityState", Assert.Throws<InvalidOperationException>(() => view.RestoreBuildingToFullHealth()).Message);
            StringAssert.Contains("LogicEntityState", Assert.Throws<InvalidOperationException>(() => view.SetCollisionBlockingByBuff(false)).Message);
            StringAssert.Contains("LogicEntityState", Assert.Throws<InvalidOperationException>(() => view.SetPermanentStealthByBuff(true)).Message);
            StringAssert.Contains("LogicEntityState", Assert.Throws<InvalidOperationException>(() => view.SetPhaseProtectionByBuff(true)).Message);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void SkillStateRefresh_UpdatesActiveViewlessLogicSkillComp()
    {
        LogicEntityState state = CreateConfiguredState("Hero_ViewlessSkillRefresh", true);
        var probe = new SkillRefreshProbe();
        state.SetSkillComp(probe);
        ActivateRequestedState(state.EntityId, 1);

        LogicSkillStateService.RefreshActiveSkillComponents();

        Assert.AreEqual(1, probe.RefreshCount);
        Assert.IsFalse(state.HasBoundView);
        Assert.AreSame(state, EntityRegistry.Player);
    }

    [Test]
    public void WorldTransitionReset_RejectsStillBoundViews()
    {
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn();
        LogicEntityLifecycleService.BindView(entityId, 1001);
        const int pauseSource = 9001;
        LogicTimeControlService.AcquirePause(pauseSource);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => LogicEntityLifecycleService.ResetForWorldTransition());

        StringAssert.Contains("1 entity views are still bound", exception.Message);
        LogicEntityLifecycleService.UnbindView(entityId, 1001);
        LogicTimeControlService.ReleasePause(pauseSource);
    }

    [Test]
    public void WorldTransitionReset_ReopensDeterministicIdentitySpace()
    {
        LogicEntityId oldEntity = LogicEntityLifecycleService.RequestSpawn();
        LogicEntityLifecycleService.BindView(oldEntity, 1001);
        LogicEntityLifecycleService.UnbindView(oldEntity, 1001);
        Assert.AreEqual("building-0000000001", LogicPersistentIdAllocator.AllocateBuildingInstanceId());

        const int pauseSource = 9001;
        LogicTimeControlService.AcquirePause(pauseSource);
        LogicEntityLifecycleService.ResetForWorldTransition();
        LogicTimeControlService.ReleasePause(pauseSource);

        LogicEntityId newEntity = LogicEntityLifecycleService.RequestSpawn();
        Assert.AreEqual(1, newEntity.Value);
        Assert.AreEqual("building-0000000001", LogicPersistentIdAllocator.AllocateBuildingInstanceId());
        Assert.AreEqual(1, LogicEntityLifecycleService.RequestedEntityCount);
        Assert.AreEqual(0, LogicEntityLifecycleService.BoundViewCount);
        Assert.AreEqual(0, LogicEntityLifecycleService.ActiveEntityCount);
        Assert.AreEqual(1, LogicEntityLifecycleService.Commands.Count);
    }

    [Test]
    public void SpawnFrameWithoutBoundView_CommitsStateOnRequestedFrame()
    {
        LogicEntityState state = CreateConfiguredState("Unit_ViewlessSpawn", false);
        LogicEntityId entityId = state.EntityId;
        LogicTimeControlService.BeginFrame(1);

        LogicEntityLifecycleService.ApplyFrame(1);

        Assert.IsTrue(state.IsSpawnCommitted);
        Assert.IsFalse(state.HasBoundView);
        Assert.AreEqual(1, LogicEntityLifecycleService.ActiveEntityCount);
        Assert.AreSame(state, EntityRegistry.AllEntities[0]);
    }

    [Test]
    public void UnconfiguredSpawnFrame_RejectsBeforeAnyEntityCommits()
    {
        LogicEntityId first = LogicEntityLifecycleService.RequestSpawn();
        LogicEntityState configured = CreateConfiguredState("Unit_ConfiguredAfterInvalid", false);
        LogicTimeControlService.BeginFrame(1);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => LogicEntityLifecycleService.ApplyFrame(1));

        StringAssert.Contains($"entity={first.Value}", exception.Message);
        Assert.IsFalse(LogicEntityStateStore.GetRequired(first).IsSpawnCommitted);
        Assert.IsFalse(configured.IsSpawnCommitted);
        Assert.AreEqual(0, LogicEntityLifecycleService.ActiveEntityCount);
        Assert.AreEqual(0, EntityRegistry.AllEntities.Count);
    }

    [Test]
    public void LateViewUnbind_PreservesActiveState_AndViewlessDespawnRemovesIt()
    {
        LogicEntityState state = CreateConfiguredState("Unit_LateView", false);
        LogicEntityId entityId = state.EntityId;
        LogicTimeControlService.BeginFrame(1);
        LogicEntityLifecycleService.ApplyFrame(1);

        LogicEntityLifecycleService.BindView(entityId, 404);
        LogicEntityLifecycleService.UnbindView(entityId, 404);

        Assert.IsTrue(state.IsSpawnCommitted);
        Assert.IsFalse(state.HasBoundView);
        Assert.AreEqual(1, LogicEntityStateStore.Count);

        LogicEntityLifecycleService.RequestDespawn(entityId);
        LogicTimeControlService.BeginFrame(2);
        LogicEntityLifecycleService.ApplyFrame(2);

        Assert.AreEqual(0, LogicEntityStateStore.Count);
        Assert.AreEqual(0, EntityRegistry.AllEntities.Count);
        Assert.AreEqual(0, LogicEntityLifecycleService.ActiveEntityCount);
    }

    [Test]
    public void ShutdownDeactivation_CommitsDespawnBeforeViewUnbind()
    {
        LogicEntityState state = CreateConfiguredState("Unit_Shutdown", false);
        LogicEntityId entityId = state.EntityId;
        LogicEntityLifecycleService.BindView(entityId, 404);
        LogicTimeControlService.BeginFrame(1);
        LogicEntityLifecycleService.ApplyFrame(1);

        LogicEntityLifecycleService.DeactivateAllForShutdown();

        Assert.IsFalse(state.IsSpawnCommitted);
        Assert.AreEqual(0, LogicEntityLifecycleService.ActiveEntityCount);

        LogicEntityLifecycleService.UnbindView(entityId, 404);
        Assert.AreEqual(0, LogicEntityStateStore.Count);
        Assert.AreEqual(0, LogicEntityLifecycleService.RequestedEntityCount);
    }

    [Test]
    public void DespawnRequest_RejectsNullView()
    {
        Assert.Throws<ArgumentNullException>(() => LogicEntityLifecycleService.RequestDespawn(null));
    }

    [Test]
    public void UnitDeath_RequestsNextTickDespawn_AndRemovesViewlessState()
    {
        LogicEntityState state = CreateConfiguredState("Unit_Death", false);
        ActivateRequestedState(state.EntityId, 1);

        state.TakeDamage((Fix64)150, HealthModifyType.empty);

        Assert.IsFalse(state.Alive);
        Assert.AreEqual(Fix64.Zero, state.HealthValue);
        Assert.AreEqual(1, EntityRegistry.AllEntities.Count);
        Assert.AreEqual(1, LogicEntityStateStore.Count);

        LogicTimeControlService.BeginFrame(2);
        LogicEntityLifecycleService.ApplyFrame(2);

        Assert.AreEqual(0, EntityRegistry.AllEntities.Count);
        Assert.AreEqual(0, LogicEntityStateStore.Count);
        Assert.AreEqual(0, LogicEntityLifecycleService.ActiveEntityCount);
    }

    [Test]
    public void BuildingDeath_DisablesWithoutDespawn_AndFullRestoreUnlocksCapabilities()
    {
        LogicEntityState state = CreateConfiguredState("Building_Death", false);
        state.ConfigureBuilding(
            CreateTestBuildingData("Building_Death"),
            "building-test-1",
            null,
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false);
        ActivateRequestedState(state.EntityId, 1);

        state.TakeDamage((Fix64)150, HealthModifyType.empty);

        Assert.IsTrue(state.IsDisabled);
        Assert.IsFalse(state.Alive);
        Assert.AreEqual(Fix64.Zero, state.HealthValue);
        Assert.IsFalse(state.CanRun(state.AtkComp));
        Assert.IsFalse(state.CanRun(state.TargetComp));
        Assert.AreEqual(1, EntityRegistry.AllEntities.Count);
        Assert.AreEqual(1, LogicEntityStateStore.Count);

        state.RestoreBuildingToFullHealth();

        Assert.IsFalse(state.IsDisabled);
        Assert.IsTrue(state.Alive);
        Assert.AreEqual((Fix64)100, state.HealthValue);
        Assert.IsTrue(state.CanRun(state.AtkComp));
        Assert.IsTrue(state.CanRun(state.TargetComp));
        Assert.AreEqual(1, LogicEntityLifecycleService.ActiveEntityCount);
    }

    [Test]
    public void ViewlessProductionBuilding_ConfiguresLogicOwnedProductionState()
    {
        LogicEntityState state = CreateConfiguredState("Buil_MiningRig_Lv1", false);
        state.ConfigureBuilding(
            CreateProductionBuildingData("Buil_MiningRig_Lv1", 10, (Fix64)1, (Fix64)1, (Fix64)1),
            "production-mining-1",
            "SH_0_1",
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            true);

        LogicBuildingProductionService.Configure(state);

        Assert.IsFalse(state.HasBoundView);
        Assert.AreEqual(ProductionType.DecreasingOutput, state.ProductionProps.ProductionType);
        Assert.AreEqual(10, LogicBuildingProductionService.GetProduction(state));
        Assert.AreSame(
            LogicBuildingExtraPropsStore.TryGet("production-mining-1"),
            state.ProductionProps);

        state.ProductionProps.DynamicProduction = (Fix64)7;
        LogicBuildingExtraPropsStore.Reset("production-mining-1");
        LogicBuildingProductionService.ReconfigureByBuildingInstanceId("production-mining-1");

        Assert.AreSame(
            LogicBuildingExtraPropsStore.TryGet("production-mining-1"),
            state.ProductionProps);
        Assert.AreEqual(ProductionType.DecreasingOutput, state.ProductionProps.ProductionType);
        Assert.AreEqual(10, LogicBuildingProductionService.GetProduction(state));
    }

    [Test]
    public void ViewlessParcelLockers_CountStableStrongholdPeersWithoutBuildingViews()
    {
        LogicEntityState first = CreateConfiguredState("Buil_ParcelLocker_Lv1", false);
        first.ConfigureBuilding(
            CreateProductionBuildingData("Buil_ParcelLocker_Lv1", 10, (Fix64)2, (Fix64)2),
            "production-parcel-1",
            "SH_0_1",
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            true);
        LogicBuildingProductionService.Configure(first);

        LogicEntityState second = CreateConfiguredState("Buil_ParcelLocker_Lv1", false);
        second.ConfigureBuilding(
            CreateProductionBuildingData("Buil_ParcelLocker_Lv1", 10, (Fix64)2, (Fix64)2),
            "production-parcel-2",
            "SH_0_1",
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            true);
        LogicBuildingProductionService.Configure(second);

        LogicTimeControlService.BeginFrame(1);
        LogicEntityLifecycleService.ApplyFrame(1);
        LogicBuildingProductionService.RefreshAll();

        Assert.IsFalse(first.HasBoundView);
        Assert.IsFalse(second.HasBoundView);
        Assert.AreEqual(2, LogicProductionConditionState.GetBuildingCount("SH_0_1", "Buil_ParcelLocker"));
        Assert.AreEqual(1, first.ProductionProps.ConditionCount);
        Assert.AreEqual(12, LogicBuildingProductionService.GetProduction(first));
        Assert.AreEqual(12, LogicBuildingProductionService.GetProduction(second));
    }

    [Test]
    public void ProductionStateHash_ChangesWhenFutureIncomeChanges()
    {
        BuildingExtraProps props = LogicBuildingExtraPropsStore.GetOrCreate("production-hash-1");
        var beforeHasher = new LogicStateHasher();
        LogicBuildingExtraPropsStore.WriteDeterministicState(beforeHasher);

        props.DynamicProduction = Fix64.One;
        var afterHasher = new LogicStateHasher();
        LogicBuildingExtraPropsStore.WriteDeterministicState(afterHasher);

        Assert.AreNotEqual(beforeHasher.Hash, afterHasher.Hash);
    }

    [Test]
    public void ProductionStateReset_PreservesStableStoreIdentity()
    {
        BuildingExtraProps props = LogicBuildingExtraPropsStore.GetOrCreate("production-reset-1");
        props.DynamicProduction = (Fix64)7;
        props.StoredProduction = 11;

        LogicBuildingExtraPropsStore.Reset("production-reset-1");

        Assert.AreSame(props, LogicBuildingExtraPropsStore.TryGet("production-reset-1"));
        Assert.AreEqual(Fix64.Zero, props.DynamicProduction);
        Assert.AreEqual(0, props.StoredProduction);
        var hasher = new LogicStateHasher();
        LogicBuildingExtraPropsStore.WriteDeterministicState(hasher);
        Assert.AreNotEqual(0UL, hasher.Hash);
    }

    [Test]
    public void ViewlessBuilding_InteractionDescriptorsAreCopiedHashedAndViewIndependent()
    {
        LogicEntityState state = CreateConfiguredState("Building_InteractionDescriptor", false);
        var source = new[]
        {
            new LogicInteractionOptionDescriptor(
                state.EntityId,
                "building-interaction-1",
                LogicInteractionOptionKind.ConstructBuilding,
                true,
                InputKey.InteractionPrimary,
                "Buil_Def_Lv1"),
        };
        state.ConfigureBuilding(
            CreateTestBuildingData("Building_InteractionDescriptor", 0),
            "building-interaction-1",
            null,
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            source,
            false);

        ulong viewlessHash = ComputeStateStoreHash();
        source[0] = new LogicInteractionOptionDescriptor(
            state.EntityId,
            "building-interaction-1",
            LogicInteractionOptionKind.ConstructBuilding,
            true,
            InputKey.InteractionPrimary,
            "Buil_Mutated_Lv1");

        Assert.AreEqual("Buil_Def_Lv1", state.InteractionOptions[0].PrimaryId);
        Assert.AreEqual(viewlessHash, ComputeStateStoreHash());

        LogicEntityLifecycleService.BindView(state.EntityId, 404);
        Assert.AreEqual(viewlessHash, ComputeStateStoreHash());
        LogicEntityLifecycleService.UnbindView(state.EntityId, 404);
    }

    [Test]
    public void InteractionDescriptorHash_DiffersBeforeViewBindingWhenPayloadDiffers()
    {
        var first = new LogicStateHasher();
        var second = new LogicStateHasher();
        var firstOptions = new[]
        {
            new LogicInteractionOptionDescriptor(
                new LogicEntityId(7),
                "building-7",
                LogicInteractionOptionKind.UpgradeBuilding,
                true,
                InputKey.InteractionSecondary,
                "Buil_Def_Lv2",
                "Tech_Def_A"),
        };
        var secondOptions = new[]
        {
            new LogicInteractionOptionDescriptor(
                new LogicEntityId(7),
                "building-7",
                LogicInteractionOptionKind.UpgradeBuilding,
                true,
                InputKey.InteractionSecondary,
                "Buil_Def_Lv2",
                "Tech_Def_B"),
        };

        LogicInteractionOptionService.WriteDeterministicState(first, firstOptions);
        LogicInteractionOptionService.WriteDeterministicState(second, secondOptions);

        Assert.AreNotEqual(first.Hash, second.Hash);
    }

    [Test]
    public void InteractionCommand_TargetsCommittedViewlessBuildingState()
    {
        LogicEntityState state = CreateConfiguredState("Building_ViewlessCommand", false);
        var option = new LogicInteractionOptionDescriptor(
            state.EntityId,
            "building-viewless-command-1",
            LogicInteractionOptionKind.ConstructBuilding,
            true,
            InputKey.InteractionPrimary,
            "Buil_Def_Lv1");
        state.ConfigureBuilding(
            CreateTestBuildingData("Building_ViewlessCommand", 0),
            "building-viewless-command-1",
            null,
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            new[] { option },
            false);
        ActivateRequestedState(state.EntityId, 1);

        LogicInteractionCommandService.BeginTimeline();
        try
        {
            LogicInteractionCommandService.ScheduleForNextFrame(
                LogicInteractionActionKind.ConstructBuilding,
                state.EntityId,
                state.BuildingInstanceId,
                option.PrimaryId);
            LogicTimeControlService.BeginFrame(2);
            LogicInteractionCommandService.ApplyFrameForTests(
                2,
                command =>
                {
                    Assert.IsTrue(EntityRegistry.TryGet(command.TargetEntityId, out IEntityContext context));
                    Assert.AreSame(state, context);
                    Assert.IsInstanceOf<IBuildingLogicContext>(context);
                    Assert.IsFalse(state.HasBoundView);
                });
        }
        finally
        {
            LogicInteractionCommandService.EndTimeline();
        }
    }

    [Test]
    public void BuildingCostDiscount_CountsViewlessLogicBuildingsByStableStrongholdId()
    {
        LogicEntityState coding = CreateConfiguredState("Building_Cost_Coding", false);
        coding.ConfigureBuilding(
            CreateCostBuildingData("Building_Cost_Coding", Archetype.Coding, 100),
            "building-cost-coding",
            "stronghold-a",
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false);
        LogicEntityState medical = CreateConfiguredState("Building_Cost_Medical", false);
        medical.ConfigureBuilding(
            CreateCostBuildingData("Building_Cost_Medical", Archetype.Medical, 100),
            "building-cost-medical",
            "stronghold-a",
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false);
        ActivateRequestedState(coding.EntityId, 1);
        Assert.IsTrue(medical.IsSpawnCommitted);
        Assert.IsFalse(coding.HasBoundView);
        Assert.IsFalse(medical.HasBoundView);

        BuildingCostModifierService.Clear();
        try
        {
            BuildingCostModifierService.RegisterStrongholdArchetypeDiscount(
                "stronghold-a",
                "Tech_Cost_Discount",
                EntitySideHelper.PlayerFactionId,
                10);

            int cost = BuildingCostModifierService.CalculateBuildingCost(
                CreateCostBuildingData("Building_Cost_Target", Archetype.Security, 100),
                "stronghold-a",
                EntitySideHelper.PlayerFactionId);

            Assert.AreEqual(80, cost);
        }
        finally
        {
            BuildingCostModifierService.Clear();
        }
    }

    [Test]
    public void ResearchCenterCostDiscount_RegistersFromViewlessLogicSource()
    {
        LogicEntityState researchCenter = CreateConfiguredState("Building_Viewless_ResearchCenter", false);
        researchCenter.ConfigureBuilding(
            CreateCostBuildingData("Building_Viewless_ResearchCenter", Archetype.Coding, 100),
            "building-viewless-research-center",
            "stronghold-viewless-tech",
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false);
        LogicEntityState medical = CreateConfiguredState("Building_Viewless_Medical", false);
        medical.ConfigureBuilding(
            CreateCostBuildingData("Building_Viewless_Medical", Archetype.Medical, 100),
            "building-viewless-medical",
            "stronghold-viewless-tech",
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false);
        ActivateRequestedState(researchCenter.EntityId, 1);
        Assert.IsTrue(medical.IsSpawnCommitted);
        Assert.IsFalse(researchCenter.HasBoundView);
        Assert.IsFalse(medical.HasBoundView);

        var managerObject = new GameObject("GlobalBuffManager_ViewlessTech_Test");
        var manager = managerObject.AddComponent<GlobalBuffManager>();
        var effect = ScriptableObject.CreateInstance<BuildingTechRuntimeEffectSO>();
        BuildingCostModifierService.Clear();
        try
        {
            effect.Activate(new TechEffectContext
            {
                TechId = "Tech_Buil_ResearchCenter_Lv2_Opt2",
                OwnerFactionId = EntitySideHelper.PlayerFactionId,
                SourceBuildingInstanceId = researchCenter.BuildingInstanceId,
                TechData = new TechData(
                    "Tech_Buil_ResearchCenter_Lv2_Opt2",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0,
                    new[] { (Fix64)10 },
                    TechScopeType.AllBuil,
                    Array.Empty<string>(),
                    Array.Empty<UnitSize>(),
                    Array.Empty<UnitTag>(),
                    Array.Empty<Archetype>(),
                    string.Empty,
                    false),
                GlobalBuffManager = manager,
            });

            int cost = BuildingCostModifierService.CalculateBuildingCost(
                CreateCostBuildingData("Building_Viewless_Target", Archetype.Security, 100),
                researchCenter.StrongholdId,
                researchCenter.OwnerFactionId);

            Assert.AreEqual(80, cost);
        }
        finally
        {
            manager.ClearLevelRuntimeState();
            UnityEngine.Object.DestroyImmediate(effect);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void BuildingLogicQuery_UsesViewlessStrongholdFactionAndArchetypeState()
    {
        LogicEntityState source = CreateConfiguredState("Building_Query_Source", false);
        source.ConfigureBuilding(
            CreateCostBuildingData("Building_Query_Source", Archetype.Coding, 100, BuilType.Army),
            "building-query-source",
            "stronghold-query",
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false);
        LogicEntityState peer = CreateConfiguredState("Building_Query_Peer", false);
        peer.ConfigureBuilding(
            CreateCostBuildingData("Building_Query_Peer", Archetype.Gardening, 100, BuilType.Army),
            "building-query-peer",
            "stronghold-query",
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false);
        ActivateRequestedState(source.EntityId, 1);

        Assert.AreSame(source, LogicBuildingQueryService.GetRequiredByInstanceId(source.BuildingInstanceId));
        Assert.IsTrue(LogicBuildingQueryService.HasBuildingArchetype(source, Archetype.Gardening));
        Assert.IsTrue(LogicBuildingQueryService.HasDifferentArmyArchetype(source));
        Assert.IsFalse(LogicBuildingQueryService.HasBuildingArchetype(
            source.StrongholdId,
            EntitySideHelper.EnemyFactionId,
            Archetype.Gardening));
        Assert.IsFalse(source.HasBoundView);
        Assert.IsFalse(peer.HasBoundView);
    }

    [Test]
    public void BuildingLogicQuery_NearestCandidateUsesFixedDistanceAndStableEntityIdTieBreak()
    {
        LogicEntityState farther = CreateBuildingQueryState(
            "building-query-farther",
            new FixVector2((Fix64)4, Fix64.Zero));
        LogicEntityState tieLowerId = CreateBuildingQueryState(
            "building-query-tie-lower",
            new FixVector2(Fix64.One, Fix64.One));
        LogicEntityState tieHigherId = CreateBuildingQueryState(
            "building-query-tie-higher",
            new FixVector2((Fix64)(-1), (Fix64)(-1)));
        LogicEntityState excludedCloser = CreateBuildingQueryState(
            "building-query-excluded",
            FixVector2.Zero);
        ActivateRequestedState(farther.EntityId, 1);

        var candidateIds = new HashSet<string>(StringComparer.Ordinal)
        {
            tieHigherId.BuildingInstanceId,
            farther.BuildingInstanceId,
            tieLowerId.BuildingInstanceId,
        };

        Assert.IsTrue(LogicBuildingQueryService.TryGetNearestByInstanceIds(
            candidateIds,
            FixVector2.Zero,
            out IBuildingLogicContext nearest));
        Assert.AreSame(tieLowerId, nearest);
        Assert.Less(tieLowerId.LogicEntityId.Value, tieHigherId.LogicEntityId.Value);
        Assert.IsFalse(excludedCloser.HasBoundView);
        Assert.IsFalse(tieLowerId.HasBoundView);
    }

    [Test]
    public void ArmyCardValues_ResolveFromViewlessLogicBuilding()
    {
        LogicEntityState building = CreateConfiguredState("Building_Viewless_ArmyCard", false);
        building.ConfigureBuilding(
            CreateArmyBuildingData("Building_Viewless_ArmyCard", 7),
            "building-viewless-army-card",
            "stronghold-viewless-army-card",
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false,
            3);
        ActivateRequestedState(building.EntityId, 1);
        building.ProductionProps.ArmyForce = (Fix64)2;

        var card = new CardModel(
            1,
            new TestCardDataProvider(),
            building.BuildingInstanceId);

        Assert.IsFalse(building.HasBoundView);
        Assert.IsNull(card.SourceBuilding);
        Assert.AreEqual(9, building.GetArmyForceWithoutRuntimeRules());
        Assert.AreEqual(9, card.GetTroopCount());
        Assert.AreEqual(3, building.GetArmySupplyPerUnit());
        Assert.AreEqual(27, card.GetOccupiedSupply());
    }

    [Test]
    public void SharedBuildingInterface_DoesNotClassifyNormalLogicUnitAsBuilding()
    {
        LogicEntityState unit = CreateConfiguredState("Unit_SharedInterfaceClassification", false);

        Assert.IsInstanceOf<IBuildingLogicContext>(unit);
        Assert.IsFalse(unit.IsLogicBuilding());
        Assert.AreEqual(
            EntitySideHelper.PlayerFactionId,
            EntityCombatTeamHelper.ResolveTeamId(unit));
    }

    [Test]
    public void BuildingEntityPropertyTech_AppliesToActiveViewlessLogicBuilding()
    {
        LogicEntityState building = CreateConfiguredState("Building_Viewless_PropertyTech", false);
        building.ConfigureBuilding(
            CreateTestBuildingData("Building_Viewless_PropertyTech"),
            "building-viewless-property-tech",
            "stronghold-viewless-property-tech",
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false);
        ActivateRequestedState(building.EntityId, 1);
        Assert.AreEqual(Fix64.Zero, building.GetProperty(CreatureMainProperty.Def));
        Assert.IsFalse(building.HasBoundView);

        var managerObject = new GameObject("GlobalBuffManager_ViewlessPropertyTech_Test");
        var manager = managerObject.AddComponent<GlobalBuffManager>();
        var effect = ScriptableObject.CreateInstance<BuildingTechRuntimeEffectSO>();
        try
        {
            effect.Activate(new TechEffectContext
            {
                TechId = "Tech_Buil_FireAcademy_Opt4",
                OwnerFactionId = EntitySideHelper.PlayerFactionId,
                SourceBuildingInstanceId = building.BuildingInstanceId,
                TechData = new TechData(
                    "Tech_Buil_FireAcademy_Opt4",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0,
                    new[] { (Fix64)7 },
                    TechScopeType.AllBuil,
                    Array.Empty<string>(),
                    Array.Empty<UnitSize>(),
                    Array.Empty<UnitTag>(),
                    Array.Empty<Archetype>(),
                    string.Empty,
                    false),
                GlobalBuffManager = manager,
            });

            Assert.AreEqual((Fix64)7, building.GetProperty(CreatureMainProperty.Def));
        }
        finally
        {
            manager.ClearLevelRuntimeState();
            UnityEngine.Object.DestroyImmediate(effect);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void BuildingEntityPropertyTech_AppliesBeforeFutureLogicBuildingSpawn()
    {
        var managerObject = new GameObject("GlobalBuffManager_FutureViewlessPropertyTech_Test");
        var manager = managerObject.AddComponent<GlobalBuffManager>();
        var effect = ScriptableObject.CreateInstance<BuildingTechRuntimeEffectSO>();
        manager.PrepareRuntimeDependencies();
        try
        {
            effect.Activate(new TechEffectContext
            {
                TechId = "Tech_Buil_FireAcademy_Opt4",
                OwnerFactionId = EntitySideHelper.PlayerFactionId,
                SourceBuildingInstanceId = "building-property-tech-source",
                TechData = new TechData(
                    "Tech_Buil_FireAcademy_Opt4",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0,
                    new[] { (Fix64)7 },
                    TechScopeType.AllBuil,
                    Array.Empty<string>(),
                    Array.Empty<UnitSize>(),
                    Array.Empty<UnitTag>(),
                    Array.Empty<Archetype>(),
                    string.Empty,
                    false),
                GlobalBuffManager = manager,
            });

            BuildingCombatShapeCatalog.Entry catalogEntry = BuildingCombatShapeCatalog.LoadRequired().Entries[0];
            BuildingData buildingData = new BuildingData(
                "Building_Future_Viewless_PropertyTech",
                BuilType.Def,
                Archetype.Security,
                catalogEntry.PrefabPath,
                string.Empty,
                string.Empty,
                1,
                0,
                (Fix64)100,
                null,
                Fix64.Zero,
                Array.Empty<Fix64>(),
                null,
                0,
                Array.Empty<string>());
            var descriptor = new LogicEntitySpawnDescriptor(
                FixVector2.Zero,
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.PlayerSide,
                buildingData.Identifier);
            LogicEntityId entityId = LogicEntityLifecycleService.RequestConfiguredSpawn(
                descriptor,
                state => LogicBuildingConfigurator.Configure(
                    state,
                    buildingData,
                    "building-future-viewless-property-tech",
                    "stronghold-future-viewless-property-tech",
                    EntitySideHelper.PlayerFactionId,
                    0));
            LogicEntityState building = LogicEntityStateStore.GetRequired(entityId);

            Assert.AreEqual((Fix64)7, building.GetProperty(CreatureMainProperty.Def));
            Assert.IsFalse(building.IsSpawnCommitted);
            Assert.IsFalse(building.HasBoundView);
        }
        finally
        {
            manager.ClearLevelRuntimeState();
            UnityEngine.Object.DestroyImmediate(effect);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void BuildingOwnershipChange_PublishesPureLogicEventWithoutView()
    {
        LogicEntityState state = CreateConfiguredState("Building_OwnershipEvent", false);
        state.ConfigureBuilding(
            CreateTestBuildingData("Building_OwnershipEvent"),
            "building-ownership-event",
            "stronghold-event",
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false);
        IBuildingLogicContext publishedBuilding = null;
        int publishedOldFaction = -1;
        int publishedNewFaction = -1;
        void OnChanged(IBuildingLogicContext building, int oldFaction, int newFaction)
        {
            publishedBuilding = building;
            publishedOldFaction = oldFaction;
            publishedNewFaction = newFaction;
        }

        LogicBuildingOwnershipEventService.OwnerFactionChanged += OnChanged;
        try
        {
            ((IBuildingLogicContext)state).SetOwnerFaction(EntitySideHelper.EnemyFactionId);
        }
        finally
        {
            LogicBuildingOwnershipEventService.OwnerFactionChanged -= OnChanged;
        }

        Assert.AreSame(state, publishedBuilding);
        Assert.AreEqual(EntitySideHelper.PlayerFactionId, publishedOldFaction);
        Assert.AreEqual(EntitySideHelper.EnemyFactionId, publishedNewFaction);
        Assert.IsFalse(state.HasBoundView);
    }

    [Test]
    public void BuildingDisabled_PublishesPureLogicEventWithoutView()
    {
        LogicEntityState state = CreateConfiguredState("Building_DisabledEvent", false);
        state.ConfigureBuilding(
            CreateTestBuildingData("Building_DisabledEvent"),
            "building-disabled-event",
            "stronghold-disabled-event",
            EntitySideHelper.EnemyFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false);
        ActivateRequestedState(state.EntityId, 1);
        IBuildingLogicContext publishedBuilding = null;
        IEntityContext publishedAttacker = null;
        LogicEntityState attacker = CreateConfiguredState("Unit_DisabledEvent_Attacker", false);
        ActivateRequestedState(attacker.EntityId, 2);

        void OnDisabled(IBuildingLogicContext building, IEntityContext eventAttacker)
        {
            publishedBuilding = building;
            publishedAttacker = eventAttacker;
        }

        LogicBuildingDisabledEventService.BuildingDisabled += OnDisabled;
        try
        {
            state.TakeDamage((Fix64)150, HealthModifyType.empty, attacker);
        }
        finally
        {
            LogicBuildingDisabledEventService.BuildingDisabled -= OnDisabled;
        }

        Assert.AreSame(state, publishedBuilding);
        Assert.AreSame(attacker, publishedAttacker);
        Assert.IsTrue(state.IsDisabled);
        Assert.IsFalse(state.HasBoundView);
    }

    [Test]
    public void ViewlessUnitSideChange_NotifiesLogicBrainExactlyOnce()
    {
        LogicEntityState state = CreateConfiguredState("Unit_SideChange", false);
        ActivateRequestedState(state.EntityId, 1);
        var brain = new SideChangeRecordingBrain();
        state.SetBrain(brain);

        state.SetUnitSide(SideType.EnemySide);
        state.SetUnitSide(SideType.EnemySide);

        Assert.AreEqual(SideType.EnemySide, state.Side);
        Assert.AreEqual(1, brain.CallCount);
        Assert.AreSame(state, brain.LastEntity);
        Assert.AreEqual(SideType.PlayerSide, brain.LastOldSide);
        Assert.AreEqual(SideType.EnemySide, brain.LastNewSide);
        Assert.IsFalse(state.HasBoundView);
    }

    [Test]
    public void CurrentInteractionFrameLifecycle_ReplacesViewlessBuildingInSameTick()
    {
        LogicEntityState original = CreateConfiguredState("Building_Replace_Original", false);
        original.ConfigureBuilding(
            CreateTestBuildingData("Building_Replace_Original", 0),
            "building-replace-1",
            "stronghold-replace",
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false);
        ActivateRequestedState(original.EntityId, 1);

        LogicEntityId replacementId = default;
        LogicInteractionCommandService.BeginTimeline();
        try
        {
            LogicInteractionCommandService.ScheduleForNextFrame(
                LogicInteractionActionKind.ConstructBuilding,
                original.EntityId,
                original.BuildingInstanceId,
                "Building_Replace_New");
            LogicTimeControlService.BeginFrame(2);
            LogicInteractionCommandService.ApplyFrameForTests(
                2,
                _ =>
                {
                    var descriptor = new LogicEntitySpawnDescriptor(
                        original.Position,
                        original.Forward,
                        original.Side,
                        "Building_Replace_New");
                    replacementId = LogicEntityLifecycleService.RequestConfiguredSpawnForCurrentInteractionFrame(
                        descriptor,
                        replacement =>
                        {
                            ConfigureBasicState(replacement);
                            replacement.ConfigureBuilding(
                                CreateTestBuildingData("Building_Replace_New"),
                                original.BuildingInstanceId,
                                original.StrongholdId,
                                original.OwnerFactionId,
                                LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
                                Array.Empty<LogicCombatShape>(),
                                Array.Empty<LogicInteractionOptionDescriptor>(),
                                false);
                        });
                    LogicEntityLifecycleService.RequestDespawnForCurrentInteractionFrame(original.EntityId);
                });

            Assert.IsTrue(original.IsSpawnCommitted);
            Assert.IsFalse(LogicEntityStateStore.GetRequired(replacementId).IsSpawnCommitted);

            LogicEntityLifecycleService.ApplyFrame(2);

            Assert.IsFalse(original.IsSpawnCommitted);
            Assert.Throws<InvalidOperationException>(() => LogicEntityStateStore.GetRequired(original.EntityId));
            Assert.IsTrue(LogicEntityStateStore.GetRequired(replacementId).IsSpawnCommitted);
            Assert.AreEqual(1, LogicEntityLifecycleService.ActiveEntityCount);
            Assert.AreEqual(replacementId, EntityRegistry.AllEntities[0].LogicEntityId);
        }
        finally
        {
            LogicInteractionCommandService.EndTimeline();
        }
    }

    [Test]
    public void HeroGhostState_LocksCombatAndInvincibilitySourceBlocksDirectDamage()
    {
        LogicEntityState state = CreateConfiguredState("Hero_Ghost", true);
        ActivateRequestedState(state.EntityId, 1);
        int ghostChangeCount = 0;
        state.GhostStateChanged += _ => ghostChangeCount++;

        state.SetGhostStateByBuff(true);
        Assert.IsTrue(state.IsGhostState);
        Assert.IsTrue(state.Alive);
        Assert.AreEqual(0u, state.AgentCollisionMask);
        Assert.IsFalse(state.CanRun(state.AtkComp));
        Assert.IsFalse(state.CanRun(state.TargetComp));

        Assert.IsTrue(state.RegisterInvincibleSource("test-ghost"));
        state.TakeDamage((Fix64)50, HealthModifyType.empty);
        Assert.AreEqual((Fix64)100, state.HealthValue);

        Assert.IsTrue(state.UnregisterInvincibleSource("test-ghost"));
        state.RestoreFromGhostState();
        Assert.IsFalse(state.IsGhostState);
        Assert.IsTrue(state.Alive);
        Assert.AreEqual(1u, state.AgentCollisionMask);
        Assert.AreEqual((Fix64)100, state.HealthValue);
        Assert.IsTrue(state.CanRun(state.AtkComp));
        Assert.IsTrue(state.CanRun(state.TargetComp));
        Assert.AreEqual(2, ghostChangeCount);
    }

    [Test]
    public void PhaseCommand_RestoresViewlessHeroAndRemovesGhostOnEffectiveTick()
    {
        LogicEntityState state = CreateConfiguredState("Hero_PhaseRestore", true);
        ActivateRequestedState(state.EntityId, 1);
        state.TakeDamage((Fix64)150, HealthModifyType.empty);
        Assert.IsTrue(state.IsGhostState);

        LogicPhaseCommand command = LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.BuildBeforeInvade);
        Assert.AreEqual(2UL, command.EffectiveFrame);
        LogicTimeControlService.BeginFrame(2);
        LogicPhaseCommandService.ApplyFrameForTests(2, _ => { });

        Assert.IsFalse(state.IsGhostState);
        Assert.IsTrue(state.Alive);
        Assert.AreEqual((Fix64)100, state.HealthValue);
        Assert.IsTrue(state.CanRun(state.AtkComp));
        Assert.IsTrue(state.CanRun(state.TargetComp));
    }

    [Test]
    public void StateHostedBuildingPhaseGuard_ChangesProtectionOnEffectiveTick()
    {
        LogicEntityState state = CreateConfiguredState("Building_PhaseGuard", false);
        state.ConfigureBuilding(
            CreateTestBuildingData("Building_PhaseGuard"),
            "building-phase-guard-1",
            null,
            EntitySideHelper.EnemyFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false);
        state.BuffComp.AddBuff(
            BuffData.Create(
                "building_phase_guard_test",
                float.MaxValue,
                true,
                1,
                new List<BuffCallback> { new BuildingPhaseGuardBuff() }),
            state);
        ActivateRequestedState(state.EntityId, 1);

        Assert.IsTrue(state.IsPhaseProtected);
        state.TakeDamage((Fix64)20, HealthModifyType.empty);
        Assert.AreEqual((Fix64)100, state.HealthValue);

        LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.Invade);
        LogicTimeControlService.BeginFrame(2);
        LogicPhaseCommandService.ApplyFrameForTests(2, _ => { });

        Assert.IsFalse(state.IsPhaseProtected);
        state.TakeDamage((Fix64)20, HealthModifyType.empty);
        Assert.AreEqual((Fix64)80, state.HealthValue);
    }

    [Test]
    public void StateHostedLv0BuildingBuff_BlocksDamageWithoutView()
    {
        LogicEntityState state = CreateConfiguredState("Building_Lv0", false);
        state.ConfigureBuilding(
            CreateTestBuildingData("Building_Lv0", 0),
            "building-lv0-1",
            null,
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false);
        state.BuffComp.AddBuff(
            BuffData.Create(
                "building_lv0_invincible_test",
                float.MaxValue,
                true,
                1,
                new List<BuffCallback> { new BuildingLv0InvincibleBuff() }),
            state);

        state.TakeDamage((Fix64)40, HealthModifyType.empty);

        Assert.AreEqual((Fix64)100, state.HealthValue);
        Assert.IsTrue(state.BuffComp.HasBuff(InvincibleStateBuff.BuffId));
    }

    [Test]
    public void StateHostedBuffs_ModifyPropertiesTauntAndReceiveHealCallback()
    {
        LogicEntityState state = CreateConfiguredState("Unit_Buffs", false);
        var healingProbe = new HealingProbeBuff();
        state.BuffComp.AddBuff(
            BuffData.Create(
                "state-hosted-test",
                float.MaxValue,
                true,
                1,
                new List<BuffCallback>
                {
                    new MainPropertyAdditiveBuff(CreatureMainProperty.Def, (Fix64)7),
                    healingProbe,
                }),
            state);
        state.BuffComp.AddBuff(TauntBuffCallback.CreateTaunt(3), state);

        Assert.AreEqual((Fix64)7, state.GetProperty(CreatureMainProperty.Def));
        Assert.AreEqual(4, state.TauntLevel);

        state.TakeDamage((Fix64)40, HealthModifyType.empty);
        state.Heal((Fix64)15);

        Assert.AreEqual((Fix64)75, state.HealthValue);
        Assert.AreEqual((Fix64)15, healingProbe.TotalHealed);
    }

    [Test]
    public void EntityRegistry_SortsByLogicId_AndRejectsDuplicateId()
    {
        var entity30 = new SimEntityContext { LogicEntityId = new LogicEntityId(30) };
        var entity10 = new SimEntityContext { LogicEntityId = new LogicEntityId(10) };
        var entity20 = new SimEntityContext { LogicEntityId = new LogicEntityId(20) };

        EntityRegistry.Register(entity30);
        EntityRegistry.Register(entity10);
        EntityRegistry.Register(entity20);

        CollectionAssert.AreEqual(
            new[] { 10, 20, 30 },
            new[]
            {
                EntityRegistry.AllEntities[0].LogicEntityId.Value,
                EntityRegistry.AllEntities[1].LogicEntityId.Value,
                EntityRegistry.AllEntities[2].LogicEntityId.Value,
            });

        var duplicate = new SimEntityContext { LogicEntityId = new LogicEntityId(20) };
        Assert.Throws<InvalidOperationException>(() => EntityRegistry.Register(duplicate));
    }

    [Test]
    public void StableColliderOrder_IsIndependentOfDiscoveryOrder()
    {
        GameObject root = new GameObject("Root");
        GameObject firstChild = new GameObject("SameName");
        GameObject secondChild = new GameObject("SameName");
        try
        {
            firstChild.transform.SetParent(root.transform, false);
            secondChild.transform.SetParent(root.transform, false);
            BoxCollider first = firstChild.AddComponent<BoxCollider>();
            SphereCollider second = secondChild.AddComponent<SphereCollider>();

            List<Collider> forward = StableColliderOrder.CollectEnabledBlockingColliders(
                root.transform,
                new Collider[] { first, second });
            List<Collider> reversed = StableColliderOrder.CollectEnabledBlockingColliders(
                root.transform,
                new Collider[] { second, first });

            Assert.AreSame(forward[0], reversed[0]);
            Assert.AreSame(forward[1], reversed[1]);
            Assert.AreEqual(
                LogicEntityObstacleId.FromBuildingCollider(new LogicEntityId(7), 0),
                LogicEntityObstacleId.FromBuildingCollider(new LogicEntityId(7), reversed.IndexOf(forward[0])));
            Assert.Less(LogicEntityObstacleId.FromBuildingCollider(new LogicEntityId(7), 0), 0);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(firstChild);
            UnityEngine.Object.DestroyImmediate(secondChild);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static LogicEntityState CreateConfiguredState(string characterKey, bool isHero)
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
                    false,
                    isHero,
                    isHero);

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

    private static EntityParams CreateDefendEnemyParams(Fix64? assignedSpeed)
    {
        return new EntityParams
        {
            Side = SideType.EnemySide,
            BrainType = BrainType.DefendEnemyAI,
            DefendAssignedSpeed = assignedSpeed,
        };
    }

    private static LogicEntityState CreateConfiguredStateForSide(string characterKey, SideType side)
    {
        var descriptor = new LogicEntitySpawnDescriptor(
            FixVector2.Zero,
            new FixVector2(Fix64.Zero, Fix64.One),
            side,
            characterKey);
        LogicEntityId entityId = LogicEntityLifecycleService.RequestConfiguredSpawn(
            descriptor,
            ConfigureBasicState);
        return LogicEntityStateStore.GetRequired(entityId);
    }

    private static LogicEntityState CreateBuildingQueryState(string buildingInstanceId, FixVector2 position)
    {
        var descriptor = new LogicEntitySpawnDescriptor(
            position,
            new FixVector2(Fix64.Zero, Fix64.One),
            SideType.PlayerSide,
            buildingInstanceId);
        LogicEntityId entityId = LogicEntityLifecycleService.RequestConfiguredSpawn(
            descriptor,
            state =>
            {
                ConfigureBasicState(state);
                state.ConfigureBuilding(
                    CreateTestBuildingData(buildingInstanceId),
                    buildingInstanceId,
                    "stronghold-query-nearest",
                    EntitySideHelper.PlayerFactionId,
                    LogicCombatShape.AxisAlignedBox(position, new FixVector2(Fix64.One, Fix64.One)),
                    Array.Empty<LogicCombatShape>(),
                    Array.Empty<LogicInteractionOptionDescriptor>(),
                    false);
            });
        return LogicEntityStateStore.GetRequired(entityId);
    }

    private static void ConfigureBasicState(LogicEntityState state)
    {
        ConfigureStateWithoutTargeting(state);
        var targeting = new NoTargetingComp();
        state.SetTargetingComp(targeting);
        targeting.Init(state);
    }

    private static void ConfigureStateWithoutTargeting(LogicEntityState state)
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
    }

    private static CharacterTargetingComp CreateCharacterTargeting(LogicEntityState state)
    {
        var targeting = new CharacterTargetingComp
        {
            AggroRange = LogicUnitConfigurator.DefaultAggroRange,
            ForgetRange = LogicUnitConfigurator.DefaultForgetRange,
            FollowSearchRange = LogicUnitConfigurator.DefaultFollowRange,
            AlertRadius = LogicUnitConfigurator.DefaultAlertRadius,
        };
        targeting.Init(state);
        return targeting;
    }

    private static ulong ComputeTargetingHash(CharacterTargetingComp targeting)
    {
        var hasher = new LogicStateHasher();
        targeting.WriteDeterministicState(hasher);
        return hasher.Hash;
    }

    private static ulong ComputeStateStoreHash()
    {
        var hasher = new LogicStateHasher();
        LogicEntityStateStore.WriteDeterministicState(hasher);
        return hasher.Hash;
    }

    private static void ActivateRequestedState(LogicEntityId entityId, ulong frame)
    {
        LogicTimeControlService.BeginFrame(frame);
        LogicEntityLifecycleService.ApplyFrame(frame);
        Assert.IsTrue(LogicEntityStateStore.GetRequired(entityId).IsSpawnCommitted);
    }

    private static BuildingData CreateTestBuildingData(string identifier, int level = 1)
    {
        return new BuildingData(
            identifier,
            BuilType.Def,
            Archetype.None,
            "Tests/Building",
            identifier,
            identifier,
            level,
            0,
            (Fix64)100,
            null,
            Fix64.Zero,
            Array.Empty<Fix64>(),
            null,
            0,
            Array.Empty<string>());
    }

    private sealed class SideChangeRecordingBrain : IControlBrain, IBrainSideChangeHandler
    {
        public Vector2 Move => Vector2.zero;
        public FixVector2 MoveFixed => FixVector2.Zero;
        public bool Attack => false;
        public bool Skill1 => false;
        public bool Skill2 => false;
        public bool Skill3 => false;
        public bool Skill4 => false;
        public bool Skill5 => false;
        public int CallCount { get; private set; }
        public IEntityContext LastEntity { get; private set; }
        public SideType LastOldSide { get; private set; }
        public SideType LastNewSide { get; private set; }

        public void OnSideChanged(IEntityContext self, SideType oldSide, SideType newSide)
        {
            CallCount++;
            LastEntity = self;
            LastOldSide = oldSide;
            LastNewSide = newSide;
        }
    }

    private static BuildingData CreateProductionBuildingData(
        string identifier,
        int production,
        params Fix64[] uniqueValues)
    {
        return new BuildingData(
            identifier,
            BuilType.Prod,
            Archetype.None,
            "Tests/Building",
            identifier,
            identifier,
            1,
            0,
            (Fix64)100,
            null,
            Fix64.Zero,
            uniqueValues ?? Array.Empty<Fix64>(),
            null,
            production,
            Array.Empty<string>());
    }

    private static LevelTagTable ParseLevelTagRow(int id, string identifier)
    {
        var row = new LevelTagTable();
        string serialized = string.Join("\t", new[]
        {
            string.Empty,
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "test",
            "test",
            "1",
            "0",
            identifier,
            "Tests/Icon",
            "Tests_Name",
            "Tests_Desc",
            string.Empty,
            string.Empty,
            "True",
            "1",
            "0",
        });
        Assert.IsTrue(row.ParseDataRow(serialized, null));
        return row;
    }

    private static BuildingData CreateArmyBuildingData(string identifier, int force)
    {
        return new BuildingData(
            identifier,
            BuilType.Army,
            Archetype.Security,
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
            force,
            Array.Empty<string>());
    }

    private sealed class TestCardDataProvider : ICardDataProvider
    {
        public string CardId => "Test_Viewless_ArmyCard";
        public string CardName => CardId;
        public Sprite CardSprite => null;
        public int PopulationCost => 99;
        public int SoldierCount => 99;
        public string SoldierName => "Test";
        public UnitType SoldierIndex => default;
        public int RequiredLv => 1;
        public string GetDisplayInfo() => CardName;
    }

    private static BuildingData CreateCostBuildingData(
        string identifier,
        Archetype archetype,
        int cost,
        BuilType type = BuilType.Def)
    {
        return new BuildingData(
            identifier,
            type,
            archetype,
            "Tests/Building",
            identifier,
            identifier,
            1,
            cost,
            (Fix64)100,
            null,
            Fix64.Zero,
            Array.Empty<Fix64>(),
            null,
            0,
            Array.Empty<string>());
    }
}
