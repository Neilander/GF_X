using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class LogicEntityIdentityTests
{
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
    public void SpawnRequestOrder_DefinesIds_WhenViewsBindOutOfOrder()
    {
        LogicEntityId first = LogicEntityLifecycleService.RequestSpawn();
        LogicEntityId second = LogicEntityLifecycleService.RequestSpawn();

        LogicEntityLifecycleService.BindView(second, 202);
        LogicEntityLifecycleService.BindView(first, 101);

        Assert.AreEqual(1, first.Value);
        Assert.AreEqual(2, second.Value);
        Assert.AreEqual(LogicEntityLifecycleCommandKind.ViewBound, LogicEntityLifecycleService.Commands[2].Kind);
        Assert.AreEqual(second, LogicEntityLifecycleService.Commands[2].EntityId);
        Assert.AreEqual(first, LogicEntityLifecycleService.Commands[3].EntityId);

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
    public void ViewBinding_UsesTheExistingLogicStateIdentity()
    {
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn(
            new LogicEntitySpawnDescriptor(
                new FixVector2((Fix64)7, (Fix64)(-2)),
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.PlayerSide,
                "Unit_Test"));
        LogicEntityState beforeBinding = LogicEntityStateStore.GetRequired(entityId);

        LogicEntityLifecycleService.BindView(entityId, 404);

        LogicEntityState afterBinding = LogicEntityStateStore.GetRequired(entityId);
        Assert.AreSame(beforeBinding, afterBinding);
        Assert.AreEqual(404, afterBinding.BoundViewEntityId);
        LogicEntityLifecycleService.UnbindView(entityId, 404);
        Assert.AreEqual(0, LogicEntityStateStore.Count);
    }

    [Test]
    public void LifecycleCommands_AreSequencedForNextLogicFrame()
    {
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn();
        LogicEntityLifecycleService.BindView(entityId, 1001);
        LogicEntityLifecycleService.UnbindView(entityId, 1001);

        Assert.AreEqual(3, LogicEntityLifecycleService.Commands.Count);
        for (int i = 0; i < LogicEntityLifecycleService.Commands.Count; i++)
        {
            Assert.AreEqual(1UL, LogicEntityLifecycleService.Commands[i].EffectiveFrame);
            Assert.AreEqual((ulong)(i + 1), LogicEntityLifecycleService.Commands[i].Sequence);
        }
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
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn();
        LogicTimeControlService.BeginFrame(1);

        LogicEntityLifecycleService.ApplyFrame(1);

        LogicEntityState state = LogicEntityStateStore.GetRequired(entityId);
        Assert.IsTrue(state.IsSpawnCommitted);
        Assert.IsFalse(state.HasBoundView);
        Assert.AreEqual(1, LogicEntityLifecycleService.ActiveEntityCount);
        Assert.AreSame(state, EntityRegistry.AllEntities[0]);
    }

    [Test]
    public void LateViewUnbind_PreservesActiveState_AndViewlessDespawnRemovesIt()
    {
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn();
        LogicTimeControlService.BeginFrame(1);
        LogicEntityLifecycleService.ApplyFrame(1);

        LogicEntityLifecycleService.BindView(entityId, 404);
        LogicEntityLifecycleService.UnbindView(entityId, 404);

        LogicEntityState state = LogicEntityStateStore.GetRequired(entityId);
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
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn();
        LogicEntityLifecycleService.BindView(entityId, 404);
        LogicTimeControlService.BeginFrame(1);
        LogicEntityLifecycleService.ApplyFrame(1);

        LogicEntityLifecycleService.DeactivateAllForShutdown();

        LogicEntityState state = LogicEntityStateStore.GetRequired(entityId);
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
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
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
            EntitySideHelper.EnemyFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
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
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
            Array.Empty<LogicCombatShape>(),
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
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn(
            new LogicEntitySpawnDescriptor(
                FixVector2.Zero,
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.PlayerSide,
                characterKey));
        LogicEntityState state = LogicEntityStateStore.GetRequired(entityId);
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
        return state;
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
}
