using NUnit.Framework;

using AAAGame.Card;

public class LogicGameplayStateHasherTests
{
    [TearDown]
    public void TearDown()
    {
        EndTimelineIfActive();
    }

    [Test]
    public void EmptyWorldHash_IsStableAcrossEquivalentTimelines()
    {
        ulong first = RunEmptyFrameAndHash();
        EndTimelineIfActive();
        ulong second = RunEmptyFrameAndHash();

        Assert.AreNotEqual(0UL, first);
        Assert.AreEqual(first, second);
    }

    [Test]
    public void StringHash_UsesDeterministicContentIncludingUnicode()
    {
        var first = new LogicStateHasher();
        var second = new LogicStateHasher();
        first.Add("Unit_Hero_英雄");
        second.Add("Unit_Hero_英雄");

        Assert.AreEqual(first.Hash, second.Hash);
        second.Add("changed");
        Assert.AreNotEqual(first.Hash, second.Hash);
    }

    [Test]
    public void PendingLogicSpawnHash_IsIndependentOfViewBindingAndFrameworkId()
    {
        ulong viewless = RunPendingSpawnAndHash(0);
        EndTimelineIfActive();
        ulong boundToArbitraryView = RunPendingSpawnAndHash(987654);

        Assert.AreEqual(viewless, boundToArbitraryView);
    }

    [Test]
    public void PendingLogicSpawnHash_ChangesWhenFutureConfiguredPropertiesChange()
    {
        ulong health100 = RunConfiguredPendingSpawnHash((Fix64)100);
        EndTimelineIfActive();
        ulong health125 = RunConfiguredPendingSpawnHash((Fix64)125);

        Assert.AreNotEqual(health100, health125);
    }

    [Test]
    public void PendingArmyCardForce_ChangesGameplayHash()
    {
        ulong forceSeven = RunPendingArmyHash(7);
        EndTimelineIfActive();
        ulong forceEight = RunPendingArmyHash(8);

        Assert.AreNotEqual(forceSeven, forceEight);
    }

    [Test]
    public void ActiveViewlessSkillContributorState_ChangesGameplayHash()
    {
        ulong first = RunActiveViewlessSkillHash(11);
        EndTimelineIfActive();
        ulong second = RunActiveViewlessSkillHash(29);

        Assert.AreNotEqual(first, second, "激活后的 SkillComp 未来状态必须继续进入 Gameplay FullHash。");
    }

    [Test]
    public void ActiveViewlessDurationMoveState_ChangesGameplayHash()
    {
        ulong oneSecond = RunActiveDurationMoveHash((Fix64)1);
        EndTimelineIfActive();
        ulong twoSeconds = RunActiveDurationMoveHash((Fix64)2);

        Assert.AreNotEqual(oneSecond, twoSeconds, "持续位移的剩余定点时长和速度必须进入 Gameplay FullHash。");
    }

    [Test]
    public void LogicMoveExecutorState_ChangesActiveAndPendingGameplayHashes()
    {
        ulong activeConstrained = RunActiveMoveExecutorHash(true);
        EndTimelineIfActive();
        ulong activeUnconstrained = RunActiveMoveExecutorHash(false);
        Assert.AreNotEqual(activeConstrained, activeUnconstrained, "激活实体的逻辑移动约束状态必须进入 Gameplay FullHash。");

        EndTimelineIfActive();
        ulong pendingNormal = RunPendingMoveExecutorHash(MovementMode.Normal);
        EndTimelineIfActive();
        ulong pendingLocked = RunPendingMoveExecutorHash(MovementMode.HardLocked);
        Assert.AreNotEqual(pendingNormal, pendingLocked, "待出生实体的逻辑移动执行器状态必须进入 Gameplay FullHash。");
    }

    [Test]
    public void InvincibleSourceSet_ChangesActiveAndPendingGameplayHashes()
    {
        ulong activeSingleSource = RunInvincibleSourceHash(true, false);
        EndTimelineIfActive();
        ulong activeTwoSources = RunInvincibleSourceHash(true, true);
        Assert.AreNotEqual(activeSingleSource, activeTwoSources,
            "Active entity invincible source membership must enter Gameplay FullHash.");

        EndTimelineIfActive();
        ulong pendingSingleSource = RunInvincibleSourceHash(false, false);
        EndTimelineIfActive();
        ulong pendingTwoSources = RunInvincibleSourceHash(false, true);
        Assert.AreNotEqual(pendingSingleSource, pendingTwoSources,
            "Pending entity invincible source membership must enter Gameplay FullHash.");
    }

    [Test]
    public void CapabilityLockerMultiset_ChangesHashButIgnoresInsertionOrder()
    {
        ulong oneLocker = RunCapabilityLockerHash(false, false);
        EndTimelineIfActive();
        ulong twoLockers = RunCapabilityLockerHash(true, false);
        Assert.AreNotEqual(oneLocker, twoLockers);

        EndTimelineIfActive();
        ulong reversedTwoLockers = RunCapabilityLockerHash(true, true);
        Assert.AreEqual(twoLockers, reversedTwoLockers);
    }

    [Test]
    public void PendingSkillSlotCommand_ChangesGameplayFullHash()
    {
        BeginAndRunEmptyFrame();
        ulong before = LogicGameplayStateHasher.ComputeCurrentFrame();

        LogicSkillSlotCommandService.ScheduleForNextFrame(0, 1);
        ulong after = LogicGameplayStateHasher.ComputeCurrentFrame();

        Assert.AreNotEqual(before, after, "待执行技能槽交换命令必须立即进入 Gameplay FullHash。");
    }

    [Test]
    public void DefendViewlessEnemyDeath_RemovesLogicIdentityWithoutViewEvent()
    {
        LogicEntityId enemyId = default;
        BeginAndRunEmptyFrame(() =>
        {
            enemyId = LogicEntityLifecycleService.RequestConfiguredSpawn(
                new LogicEntitySpawnDescriptor(
                    FixVector2.Zero,
                    new FixVector2(Fix64.Zero, Fix64.One),
                    SideType.EnemySide,
                    "Unit_DefendViewlessDeath"),
                state => ConfigurePendingState(state, (Fix64)100));
        });

        InGameDataModel.SetPhase(GamePhase.Defend, false);
        DefendPhaseRuntime.CancelRuntime();

        var aliveIdsField = typeof(DefendPhaseRuntime).GetField(
            "s_AliveEnemyLogicEntityIds",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var aliveIds = aliveIdsField?.GetValue(null) as System.Collections.Generic.HashSet<int>;
        Assert.IsNotNull(aliveIds);
        Assert.IsTrue(aliveIds.Add(enemyId.Value));

        typeof(DefendPhaseRuntime).GetMethod(
                "EnsureSubscribedSoldierDead",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?.Invoke(null, null);

        LogicEntityState enemy = LogicEntityStateStore.GetRequired(enemyId);
        Assert.IsFalse(enemy.HasBoundView);
        typeof(LogicEntityState).GetField(
                "<Alive>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(enemy, false);
        typeof(LogicUnitDeathEventService).GetMethod(
                "Publish",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { enemy });

        Assert.IsFalse(
            aliveIds.Contains(enemyId.Value),
            "Defend alive state must consume the logic death event even when no SoldierEntity view exists.");
        DefendPhaseRuntime.CancelRuntime();
    }

    [Test]
    public void RewardFutureState_ChangesGameplayFullHashAndIgnoresSetInsertionOrder()
    {
        LogicRewardStateService.Reset();
        try
        {
            BeginAndRunEmptyFrame();

            ulong empty = LogicGameplayStateHasher.ComputeCurrentFrame();
            Assert.AreEqual(0, LogicRewardStateService.AccumulateEnemyDeadSupply(1, 2));
            ulong withRemainder = LogicGameplayStateHasher.ComputeCurrentFrame();
            Assert.AreNotEqual(empty, withRemainder);

            LogicRewardStateService.Reset();
            LogicRewardStateService.RecordCapturedStronghold("stronghold-b");
            LogicRewardStateService.RecordCapturedStronghold("stronghold-a");
            ulong forward = LogicGameplayStateHasher.ComputeCurrentFrame();
            Assert.AreNotEqual(empty, forward);

            LogicRewardStateService.Reset();
            LogicRewardStateService.RecordCapturedStronghold("stronghold-a");
            LogicRewardStateService.RecordCapturedStronghold("stronghold-b");
            ulong reversed = LogicGameplayStateHasher.ComputeCurrentFrame();
            Assert.AreEqual(forward, reversed);
        }
        finally
        {
            LogicRewardStateService.Reset();
        }
    }

    [Test]
    public void LevelTagFutureState_ChangesGameplayFullHashAndIgnoresInsertionOrder()
    {
        BeginAndRunEmptyFrame();

        ulong empty = LogicGameplayStateHasher.ComputeCurrentFrame();
        LevelTagRuntime.SetActiveTagIds(new[] { 41, 7, 23 });
        ulong tagIdsForward = LogicGameplayStateHasher.ComputeCurrentFrame();
        Assert.AreNotEqual(empty, tagIdsForward);

        LevelTagRuntime.SetActiveTagIds(new[] { 23, 41, 7 });
        ulong tagIdsReverse = LogicGameplayStateHasher.ComputeCurrentFrame();
        Assert.AreEqual(tagIdsForward, tagIdsReverse);

        LevelTagRuntime.SetActiveTagIdentifiers(new[] { "tag-b", "tag-a" });
        ulong tagNamesForward = LogicGameplayStateHasher.ComputeCurrentFrame();
        LevelTagRuntime.SetActiveTagIdentifiers(new[] { "tag-a", "tag-b" });
        ulong tagNamesReverse = LogicGameplayStateHasher.ComputeCurrentFrame();
        Assert.AreEqual(tagNamesForward, tagNamesReverse);
        Assert.AreNotEqual(tagIdsForward, tagNamesForward);

        LevelTagRuntime.ClearActiveTags();
        LevelTagRuntime.SetEditorTestHeroReviveState(19, 2, 1);
        LevelTagRuntime.SetEditorTestHeroReviveState(5, 3, 2);
        ulong reviveForward = LogicGameplayStateHasher.ComputeCurrentFrame();

        LevelTagRuntime.ClearActiveTags();
        LevelTagRuntime.SetEditorTestHeroReviveState(5, 3, 2);
        LevelTagRuntime.SetEditorTestHeroReviveState(19, 2, 1);
        ulong reviveReverse = LogicGameplayStateHasher.ComputeCurrentFrame();
        Assert.AreEqual(reviveForward, reviveReverse);

        LevelTagRuntime.SetEditorTestHeroReviveState(19, 2, 2);
        ulong reviveConsumedAgain = LogicGameplayStateHasher.ComputeCurrentFrame();
        Assert.AreNotEqual(reviveForward, reviveConsumedAgain,
            "英雄每日复活消费次数会改变未来死亡结果，必须进入 Gameplay FullHash。");
    }

    [Test]
    public void BuildingTechStaticRules_ChangeGameplayFullHashAndIgnoreInsertionOrder()
    {
        BeginAndRunEmptyFrame();

        RegisterBuildingTechStaticRules(false, 6);
        ulong forward = LogicGameplayStateHasher.ComputeCurrentFrame();

        ClearBuildingTechStaticRules();
        RegisterBuildingTechStaticRules(true, 6);
        ulong reverse = LogicGameplayStateHasher.ComputeCurrentFrame();
        Assert.AreEqual(forward, reverse);

        ClearBuildingTechStaticRules();
        RegisterBuildingTechStaticRules(false, 7);
        ulong changed = LogicGameplayStateHasher.ComputeCurrentFrame();
        Assert.AreNotEqual(forward, changed,
            "BuildingTech 静态规则会改变未来经济、出兵和目标选择结果，必须进入 Gameplay FullHash。");
    }

    [Test]
    public void GeneralCounterDeterministicState_TracksExactFixedProgress()
    {
        var counter = new GeneralCounter();
        counter.Init((Fix64)3, false);
        var before = new LogicStateHasher();
        counter.WriteDeterministicState(before);

        counter.Tick(Fix64.FromRaw(1));
        var after = new LogicStateHasher();
        counter.WriteDeterministicState(after);

        Assert.AreNotEqual(before.Hash, after.Hash, "技能冷却每个 Fix64 raw 进度都必须进入确定性状态。");
    }

    [Test]
    public void InGameStageCheckpoint_RestoresNormalizedPersistentEconomyState()
    {
        EnsureInGameDataModel();
        for (int i = 0; i <= (int)IngameValueType.MaxSupply; i++)
        {
            var type = (IngameValueType)i;
            InGameDataModel.SetValue(type, InGameDataModel.GetValue(type), false);
        }
        InGameDataCheckpoint original = InGameDataModel.CaptureStageCheckpointState();
        try
        {
            InGameDataModel.SetValue(IngameValueType.Phase, (int)GamePhase.BuildBeforeDefend, false);
            InGameDataModel.SetValue(IngameValueType.Day, 7, false);
            InGameDataModel.SetValue(IngameValueType.Coin, 4321, false);
            InGameDataModel.SetValue(IngameValueType.MaxSupply, 30, false);
            InGameDataModel.SetValue(IngameValueType.CurrentSupply, 12, false);
            InGameDataModel.EnsureProductionBuildingCoinReserves("checkpoint-building", 99);
            InGameDataModel.RecordBuildingCostSpent("checkpoint-building", 321);
            InGameDataCheckpoint checkpoint = InGameDataModel.CaptureStageCheckpointState();

            InGameDataModel.SetValue(IngameValueType.Coin, 1, false);
            InGameDataModel.ConsumeProductionBuildingCoinReserves("checkpoint-building", 80);
            InGameDataModel.RecordBuildingCostSpent("checkpoint-building", 100);
            InGameDataModel.RestoreStageCheckpointState(checkpoint, false);

            Assert.AreEqual(GamePhase.BuildBeforeDefend, (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase));
            Assert.AreEqual(7, InGameDataModel.GetValue(IngameValueType.Day));
            Assert.AreEqual(4321, InGameDataModel.GetValue(IngameValueType.Coin));
            Assert.AreEqual(12, InGameDataModel.GetValue(IngameValueType.CurrentSupply));
            Assert.AreEqual(30, InGameDataModel.GetValue(IngameValueType.MaxSupply));
            Assert.AreEqual(99, InGameDataModel.GetProductionBuildingCoinReserves("checkpoint-building"));
            Assert.AreEqual(321, InGameDataModel.GetBuildingCostSpent("checkpoint-building"));
            Assert.AreEqual(checkpoint.ContentHash, InGameDataModel.CaptureStageCheckpointState().ContentHash);
        }
        finally
        {
            InGameDataModel.RestoreStageCheckpointState(original, false);
        }
    }

    private static ulong RunEmptyFrameAndHash()
    {
        BeginAndRunEmptyFrame();
        return LogicGameplayStateHasher.ComputeCurrentFrame();
    }

    private static void RegisterBuildingTechStaticRules(bool reverse, int settlementOffset)
    {
        string first = reverse ? "tech-b" : "tech-a";
        string second = reverse ? "tech-a" : "tech-b";
        DiscardRewardModifierService.RegisterRateReduction(first, 1, reverse ? 3 : 2);
        DiscardRewardModifierService.RegisterRateReduction(second, 1, reverse ? 2 : 3);
        EnemyArmyForceModifierService.RegisterReduction(first, 1, reverse ? (Fix64)9 : (Fix64)7);
        EnemyArmyForceModifierService.RegisterReduction(second, 1, reverse ? (Fix64)7 : (Fix64)9);
        HealingTargetFilterService.RegisterNurseHealthThreshold(first, 1, reverse ? (Fix64)60 : (Fix64)40);
        HealingTargetFilterService.RegisterNurseHealthThreshold(second, 1, reverse ? (Fix64)40 : (Fix64)60);
        if (reverse)
        {
            BuildingCostModifierService.RegisterStrongholdArchetypeDiscount("stronghold-a", "tech-b", 1, 5);
            BuildingCostModifierService.RegisterStrongholdArchetypeDiscount("stronghold-b", "tech-a", 1, 4);
        }
        else
        {
            BuildingCostModifierService.RegisterStrongholdArchetypeDiscount("stronghold-b", "tech-a", 1, 4);
            BuildingCostModifierService.RegisterStrongholdArchetypeDiscount("stronghold-a", "tech-b", 1, 5);
        }
        SettlementOffsetRateService.RegisterOffsetRate(first, 1, reverse ? 8 : settlementOffset);
        SettlementOffsetRateService.RegisterOffsetRate(second, 1, reverse ? settlementOffset : 8);
    }

    private static void ClearBuildingTechStaticRules()
    {
        DiscardRewardModifierService.Clear();
        EnemyArmyForceModifierService.Clear();
        HealingTargetFilterService.Clear();
        BuildingCostModifierService.Clear();
        SettlementOffsetRateService.Clear();
    }

    private static ulong RunPendingSpawnAndHash(int viewEntityId)
    {
        BeginAndRunEmptyFrame();
        var descriptor = new LogicEntitySpawnDescriptor(
                new FixVector2(Fix64.FromRaw(12345), Fix64.FromRaw(-67890)),
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.EnemySide,
                "Unit_PendingHash");
        LogicEntityId entityId = LogicEntityLifecycleService.RequestConfiguredSpawn(
            descriptor,
            state => ConfigurePendingState(state, (Fix64)100));
        if (viewEntityId > 0)
            LogicEntityLifecycleService.BindView(entityId, viewEntityId);

        ulong hash = LogicGameplayStateHasher.ComputeCurrentFrame();
        if (viewEntityId > 0)
            LogicEntityLifecycleService.UnbindView(entityId, viewEntityId);
        Assert.AreEqual(1, LogicEntityStateStore.Count, "View unbind must not cancel a pending logic spawn.");
        return hash;
    }

    private static ulong RunConfiguredPendingSpawnHash(Fix64 health)
    {
        BeginAndRunEmptyFrame();
        var descriptor = new LogicEntitySpawnDescriptor(
            new FixVector2((Fix64)2, (Fix64)3),
            new FixVector2(Fix64.Zero, Fix64.One),
            SideType.PlayerSide,
            "Unit_ConfiguredPendingHash");
        LogicEntityLifecycleService.RequestConfiguredSpawn(
            descriptor,
            state => ConfigurePendingState(state, health));
        return LogicGameplayStateHasher.ComputeCurrentFrame();
    }

    private static ulong RunPendingArmyHash(int force)
    {
        BeginAndRunEmptyFrame();
        var descriptor = new LogicEntitySpawnDescriptor(
            FixVector2.Zero,
            new FixVector2(Fix64.Zero, Fix64.One),
            SideType.PlayerSide,
            "Building_PendingArmyHash");
        LogicEntityLifecycleService.RequestConfiguredSpawn(
            descriptor,
            state =>
            {
                ConfigurePendingState(state, (Fix64)100);
                state.ConfigureBuilding(
                    new BuildingData(
                        "Building_PendingArmyHash",
                        BuilType.Army,
                        Archetype.Security,
                        "Tests/Building",
                        "Building_PendingArmyHash",
                        "Building_PendingArmyHash",
                        1,
                        0,
                        (Fix64)100,
                        null,
                        Fix64.Zero,
                        System.Array.Empty<Fix64>(),
                        null,
                        force,
                        System.Array.Empty<string>()),
                    "building-pending-army-hash",
                    "stronghold-pending-army-hash",
                    EntitySideHelper.PlayerFactionId,
                    LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
                    System.Array.Empty<LogicCombatShape>(),
                    System.Array.Empty<LogicInteractionOptionDescriptor>(),
                    false,
                    3);
            });
        return LogicGameplayStateHasher.ComputeCurrentFrame();
    }

    private static ulong RunActiveViewlessSkillHash(int marker)
    {
        BeginAndRunEmptyFrame(() =>
        {
            var descriptor = new LogicEntitySpawnDescriptor(
                new FixVector2((Fix64)4, (Fix64)5),
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.PlayerSide,
                "Unit_ActiveSkillHash");
            LogicEntityLifecycleService.RequestConfiguredSpawn(
                descriptor,
                state =>
                {
                    ConfigurePendingState(state, (Fix64)100);
                    state.SetSkillComp(new HashTestSkillComp(marker));
                });
        });
        Assert.AreEqual(1, LogicEntityLifecycleService.ActiveEntityCount);
        return LogicGameplayStateHasher.ComputeCurrentFrame();
    }

    private static ulong RunActiveDurationMoveHash(Fix64 duration)
    {
        BeginAndRunEmptyFrame(() =>
        {
            var descriptor = new LogicEntitySpawnDescriptor(
                new FixVector2((Fix64)4, (Fix64)5),
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.PlayerSide,
                "Unit_ActiveDurationMoveHash");
            LogicEntityLifecycleService.RequestConfiguredSpawn(
                descriptor,
                state =>
                {
                    ConfigurePendingState(state, (Fix64)100);
                    state.DurationMoveEffectComp.StartDurationOverrideMove(
                        duration,
                        new FixVector2((Fix64)3, (Fix64)(-2)));
                });
        });
        Assert.AreEqual(1, LogicEntityLifecycleService.ActiveEntityCount);
        return LogicGameplayStateHasher.ComputeCurrentFrame();
    }

    private static ulong RunActiveMoveExecutorHash(bool navigationConstrained)
    {
        BeginAndRunEmptyFrame(() =>
        {
            var descriptor = new LogicEntitySpawnDescriptor(
                new FixVector2((Fix64)4, (Fix64)5),
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.PlayerSide,
                "Unit_ActiveMoveExecutorHash");
            LogicEntityLifecycleService.RequestConfiguredSpawn(
                descriptor,
                state =>
                {
                    ConfigurePendingState(state, (Fix64)100);
                    state.MoveExecutor.SetNavigationConstrained(navigationConstrained);
                });
        });
        return LogicGameplayStateHasher.ComputeCurrentFrame();
    }

    private static ulong RunPendingMoveExecutorHash(MovementMode mode)
    {
        BeginAndRunEmptyFrame();
        var descriptor = new LogicEntitySpawnDescriptor(
            new FixVector2((Fix64)4, (Fix64)5),
            new FixVector2(Fix64.Zero, Fix64.One),
            SideType.PlayerSide,
            "Unit_PendingMoveExecutorHash");
        LogicEntityLifecycleService.RequestConfiguredSpawn(
            descriptor,
            state =>
            {
                ConfigurePendingState(state, (Fix64)100);
                state.MoveExecutor.SetMovementMode(mode);
            });
        return LogicGameplayStateHasher.ComputeCurrentFrame();
    }

    private static ulong RunInvincibleSourceHash(bool active, bool addSecondSource)
    {
        System.Action requestSpawn = () =>
        {
            var descriptor = new LogicEntitySpawnDescriptor(
                new FixVector2((Fix64)4, (Fix64)5),
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.PlayerSide,
                "Unit_InvincibleSourceHash");
            LogicEntityLifecycleService.RequestConfiguredSpawn(
                descriptor,
                state =>
                {
                    ConfigurePendingState(state, (Fix64)100);
                    Assert.IsTrue(state.RegisterInvincibleSource("source-a"));
                    if (addSecondSource)
                        Assert.IsTrue(state.RegisterInvincibleSource("source-b"));
                });
        };

        if (active)
            BeginAndRunEmptyFrame(requestSpawn);
        else
        {
            BeginAndRunEmptyFrame();
            requestSpawn();
        }

        return LogicGameplayStateHasher.ComputeCurrentFrame();
    }

    private static ulong RunCapabilityLockerHash(bool addSecondLocker, bool reverseOrder)
    {
        BeginAndRunEmptyFrame(() =>
        {
            var descriptor = new LogicEntitySpawnDescriptor(
                new FixVector2((Fix64)4, (Fix64)5),
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.PlayerSide,
                "Unit_CapabilityLockerHash");
            LogicEntityLifecycleService.RequestConfiguredSpawn(
                descriptor,
                state =>
                {
                    ConfigurePendingState(state, (Fix64)100);
                    var first = new HashTestCapabilityLockerA();
                    var second = new HashTestCapabilityLockerB();
                    if (addSecondLocker && reverseOrder)
                        state.LockComp(state.MoveComp, second);
                    state.LockComp(state.MoveComp, first);
                    if (addSecondLocker && !reverseOrder)
                        state.LockComp(state.MoveComp, second);
                });
        });
        return LogicGameplayStateHasher.ComputeCurrentFrame();
    }

    private static void ConfigurePendingState(LogicEntityState state, Fix64 health)
    {
        state.Configure(
            null,
            new CreaturePropertyManager(property =>
                property == CreatureMainProperty.Health ? health : Fix64.Zero),
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
    }

    private static void BeginAndRunEmptyFrame(System.Action beforeFirstFrame = null)
    {
        EnsureInGameDataModel();
        LevelTagRuntime.ClearActiveTags();
        EntityRegistry.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicInteractionHoldService.BeginTimeline();
        LogicInteractionTargetStateService.BeginTimeline();
        LogicInteractionCommandService.BeginTimeline();
        LogicCardCommandService.BeginTimeline();
        LogicCardPlacementAuthority.BeginTimeline();
        LogicSkillSlotCommandService.BeginTimeline();
        Assert.IsTrue(LogicInteractionHoldService.IsActive, "Interaction hold service must be active after BeginTimeline.");
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        LogicTechEffectCommandService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        LogicObstacleCommandService.BeginTimeline();
        LogicFrameRuntime.Begin();
        LogicEntityFrameSnapshotService.BeginTimeline();
        MAEntityLogicFrameSystem.BeginTimeline();
        LogicFrameRuntime.StartTimeline();
        beforeFirstFrame?.Invoke();

        LogicTimeControlService.BeginFrame(1);
        var inputTimeline = new LogicInputTimeline();
        inputTimeline.Begin(0d, FixVector2.Zero, 0, FixVector2.Zero, false, FixVector2.Zero);
        LogicInteractionHoldService.ProcessFrame(inputTimeline.Seal(1, 1d / 30d));
        Assert.IsTrue(LogicInteractionHoldService.IsActive, "Interaction hold service became inactive while sealing input.");
        LogicCardCommandService.ApplyFrameForTests(1, _ => { });
        LogicSkillSlotCommandService.ApplyFrameForTests(1, _ => { });
        LogicPhaseCommandService.ApplyFrameForTests(1, _ => { });
        LogicInteractionCommandService.ApplyFrameForTests(1, _ => { });
        LogicTechEffectCommandService.ApplyFrameForTests(1, _ => { });
        LogicEntityLifecycleService.ApplyFrame(1);
        LogicObstacleCommandService.ApplyFrameForTests(1, _ => { });
        LogicFrameRuntime.Tick(1);
        Assert.IsTrue(LogicInteractionHoldService.IsActive, "Interaction hold service became inactive during LogicFrameRuntime.Tick.");
    }

    private sealed class HashTestSkillComp : ISkillComp, ILogicDeterministicStateContributor
    {
        private readonly int m_Marker;

        public HashTestSkillComp(int marker) => m_Marker = marker;
        public void Init(IEntityContext entity, System.Collections.Generic.List<ActiveSkillSO> activeSkills, System.Collections.Generic.List<PassiveSkillSO> passiveSkills) { }
        public void Skill(Fix64 deltaTime) { }
        public void CancelSkills() { }
        public void OnSkillChanged() { }
        public void ShutDown() { }
        public void Resume() { }
        public void WriteDeterministicState(LogicStateHasher hasher) => hasher.Add(m_Marker);
    }

    private sealed class HashTestCapabilityLockerA : ICapability
    {
        public void ShutDown() { }
        public void Resume() { }
    }

    private sealed class HashTestCapabilityLockerB : ICapability
    {
        public void ShutDown() { }
        public void Resume() { }
    }

    private static void EndTimelineIfActive()
    {
        if (MAEntityLogicFrameSystem.IsActive)
            MAEntityLogicFrameSystem.EndTimeline();
        if (LogicEntityFrameSnapshotService.IsActive)
            LogicEntityFrameSnapshotService.EndTimeline();
        if (LogicFrameRuntime.IsActive)
            LogicFrameRuntime.End();
        if (LogicObstacleCommandService.IsActive)
            LogicObstacleCommandService.EndTimeline();
        if (LogicEntityLifecycleService.IsActive)
            LogicEntityLifecycleService.EndTimeline();
        if (LogicTechEffectCommandService.IsActive)
            LogicTechEffectCommandService.EndTimeline();
        if (LogicCardPlacementAuthority.IsActive)
            LogicCardPlacementAuthority.EndTimeline();
        if (LogicSkillSlotCommandService.IsActive)
            LogicSkillSlotCommandService.EndTimeline();
        if (LogicCardCommandService.IsActive)
            LogicCardCommandService.EndTimeline();
        if (LogicInteractionCommandService.IsActive)
            LogicInteractionCommandService.EndTimeline();
        if (LogicInteractionTargetStateService.IsActive)
            LogicInteractionTargetStateService.EndTimeline();
        if (LogicPhaseCommandService.IsActive)
            LogicPhaseCommandService.EndTimeline();
        if (LogicInteractionHoldService.IsActive)
            LogicInteractionHoldService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
        ClearBuildingTechStaticRules();
        LevelTagRuntime.ClearActiveTags();
        EntityRegistry.Clear();
    }

    private static void EnsureInGameDataModel()
    {
        System.Reflection.FieldInfo dataModelField = typeof(GF).GetField(
            "<DataModel>k__BackingField",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        GameFramework.DataModelComponent current = dataModelField?.GetValue(null) as GameFramework.DataModelComponent;
        if (current == null)
        {
            var gameObject = new UnityEngine.GameObject("LogicGameplayStateHasherTests_DataModel");
            current = gameObject.AddComponent<GameFramework.DataModelComponent>();
            dataModelField?.SetValue(null, current);
        }

        System.Reflection.FieldInfo dataModelsField = typeof(GameFramework.DataModelComponent).GetField(
            "m_DataModels",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        object dataModels = dataModelsField?.GetValue(current);
        if (dataModelsField != null && (dataModels == null || dataModels.GetType() != dataModelsField.FieldType))
        {
            dataModels = System.Activator.CreateInstance(dataModelsField.FieldType);
            dataModelsField.SetValue(current, dataModels);
        }
        if (current.GetDataModel<InGameDataModel>() != null)
            return;

        var model = (InGameDataModel)System.Activator.CreateInstance(typeof(InGameDataModel), true);
        System.Type pairType = typeof(GameFramework.DataModelComponent).Assembly.GetType("TypeIdPair");
        object pair = System.Activator.CreateInstance(
            pairType,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic,
            null,
            new object[] { typeof(InGameDataModel), 0 },
            null);
        dataModels.GetType().GetMethod("Add")?.Invoke(dataModels, new[] { pair, model });
    }
}
