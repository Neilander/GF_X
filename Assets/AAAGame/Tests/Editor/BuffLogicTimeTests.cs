using System.Collections.Generic;
using GameFramework;
using NUnit.Framework;
using AAAGame.Scripts.BuffSystem;
using System.Linq;

[TestFixture]
public sealed class BuffLogicTimeTests
{
    [Test]
    public void OneSecondBuff_ExpiresOnlyFromFixedLogicTicks()
    {
        BuffData buff = BuffData.Create("test_fixed_duration", Fix64.One, false, 1, new List<BuffCallback>());
        try
        {
            for (int i = 0; i < 30; i++)
                Assert.IsFalse(buff.AdvanceLogicTime(LogicFrameRuntime.FixedDeltaTime, false));

            Assert.IsTrue(buff.AdvanceLogicTime(LogicFrameRuntime.FixedDeltaTime, false));
        }
        finally
        {
            ReferencePool.Release(buff);
        }
    }

    [Test]
    public void PermanentBuff_DoesNotConsumeLogicTime()
    {
        BuffData buff = BuffData.Create("test_forever", Fix64.One, true, 1, new List<BuffCallback>());
        try
        {
            Fix64 before = buff.remainingTime;
            Assert.IsFalse(buff.AdvanceLogicTime(LogicFrameRuntime.FixedDeltaTime, true));
            Assert.AreEqual(before, buff.remainingTime);
        }
        finally
        {
            ReferencePool.Release(buff);
        }
    }

    [Test]
    public void SpawnTimedBuff_StartsOnFirstCombatAndDoesNotPauseAfterward()
    {
        BuffData buff = BuffData.Create(
            "test_spawn_combat_duration",
            Fix64.One,
            false,
            1,
            new List<BuffCallback>(),
            startDurationOnFirstCombat: true);
        try
        {
            for (int i = 0; i < 60; i++)
                Assert.IsFalse(buff.AdvanceLogicTime(LogicFrameRuntime.FixedDeltaTime, true));
            Assert.AreEqual(Fix64.One.RawValue, buff.remainingTime.RawValue);

            Assert.IsFalse(buff.AdvanceLogicTime(LogicFrameRuntime.FixedDeltaTime, false));
            Assert.IsTrue(buff.hasStartedDuration);

            for (int i = 0; i < 29; i++)
                Assert.IsFalse(buff.AdvanceLogicTime(LogicFrameRuntime.FixedDeltaTime, true));
            Assert.IsTrue(buff.AdvanceLogicTime(LogicFrameRuntime.FixedDeltaTime, true));
        }
        finally
        {
            ReferencePool.Release(buff);
        }
    }

    [Test]
    public void HealthDrain_DoesNotAccumulateWhileHostIsOutOfCombat()
    {
        var context = new SimEntityContext
        {
            Brain = new ScriptedBrain { Attack = true },
        };
        context.Health.Init((Fix64)100);
        var attack = new SimAtkComp();
        attack.Init(context);
        context.AtkComp = attack;
        var callback = new HealthDrainOverTimeBuff((Fix64)5);
        callback.Initialize(null, context);

        Fix64 initialHealth = context.HealthValue;
        for (int i = 0; i < 60; i++)
            callback.OnUpdate(LogicFrameRuntime.FixedDeltaTime);
        Assert.AreEqual(initialHealth.RawValue, context.HealthValue.RawValue);

        attack.Attack(Fix64.Zero);
        context.TickOutOfCombatState(0f);
        for (int i = 0; i < 31; i++)
            callback.OnUpdate(LogicFrameRuntime.FixedDeltaTime);
        Assert.AreEqual((initialHealth - (Fix64)5).RawValue, context.HealthValue.RawValue);

        attack.InterruptAttack();
        context.TickOutOfCombatState(0f);
        for (int i = 0; i < 60; i++)
            callback.OnUpdate(LogicFrameRuntime.FixedDeltaTime);
        Assert.AreEqual((initialHealth - (Fix64)5).RawValue, context.HealthValue.RawValue);
    }

    [Test]
    public void PhaseAmmoReset_UsesExactLogicPhaseApplyFrame()
    {
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        var host = new SimEntityContext();
        host.WeaponComp = new WeaponComp(CreateAmmunitionWeapon(3));
        var buff = new PhaseAmmoResetBuff();
        buff.Initialize(null, host);
        buff.OnAdd();
        try
        {
            Assert.IsTrue(host.WeaponComp.TryConsumeAmmo(2));
            Assert.AreEqual(1, host.WeaponComp.CurrentAmmo);
            LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.Invade);
            LogicTimeControlService.BeginFrame(1);

            LogicPhaseCommandService.ApplyFrameForTests(1, _ => { });

            Assert.AreEqual(3, host.WeaponComp.CurrentAmmo);
        }
        finally
        {
            buff.OnRemove();
            LogicPhaseCommandService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    [Test]
    public void CharacterBuffComp_TicksOnPureLogicEntityContext()
    {
        var context = new SimEntityContext();
        var component = new CharacterBuffComp();
        var callback = new RecordingBuffCallback();
        context.BuffComp = component;
        component.Init(context);

        BuffData buff = BuffData.Create(
            "pure_logic_host",
            (Fix64)10,
            false,
            1,
            new List<BuffCallback> { callback });
        Assert.IsTrue(component.AddBuff(buff, context));

        component.UpdateBuff(LogicFrameRuntime.FixedDeltaTime);

        Assert.AreSame(context, callback.InitializedHost);
        Assert.AreEqual(LogicFrameRuntime.FixedDeltaTime.RawValue, callback.UpdatedTime.RawValue);
        component.ShutDown();
    }

    private static Weapon CreateAmmunitionWeapon(int capacity)
    {
        return Weapon.Create(
            "BuffLogicTimeTests",
            new WeaponData(
                WeaponType.Projectile,
                Fix64.One,
                Fix64.One,
                Fix64.One,
                Fix64.One,
                Fix64.Zero,
                Fix64.Zero,
                Fix64.Zero,
                Fix64.Zero,
                Fix64.Zero,
                Fix64.One,
                (Fix64)capacity,
                System.Array.Empty<Fix64>()));
    }

    [Test]
    public void FearMoveAwayBuffHash_TracksSourceEntity()
    {
        ulong first = ComputeFearBuffHash(new LogicEntityId(101));
        ulong second = ComputeFearBuffHash(new LogicEntityId(202));

        Assert.AreNotEqual(first, second,
            "Fear movement source changes future movement and must enter deterministic buff state.");
    }

    [Test]
    public void StatefulBuffModules_ContributeFutureLogicState()
    {
        System.Type[] statefulTypes =
        {
            typeof(PercentAttackBonusBuff),
            typeof(RevertibleMoveSpeedBonusBuff),
            typeof(BuildingCollisionBlockingBuff),
            typeof(PhaseAmmoResetBuff),
            typeof(BuildingCaptureInvincibleBuff),
            typeof(BuildingLv0InvincibleBuff),
            typeof(BuildingPhaseGuardBuff),
            typeof(FixedMoveSpeedOverrideBuff),
            typeof(HeroGhostBuff),
            typeof(AttackSpeedBonusBuff),
            typeof(DayScalingHeroStatsBuff),
            typeof(OnKillHealBuff),
            typeof(PercentHealthBonusBuff),
            typeof(PercentMoveSpeedBonusBuff),
            typeof(RampedPercentMoveSpeedBonusBuff),
            typeof(SkillMoveSpeedPercentBuff),
            typeof(SkillDisarmDebuff),
            typeof(SkillCheerSquadBuff),
            typeof(PositionAreaRefreshBuff),
            typeof(TauntBuffCallback),
            typeof(TimedDeathBuff),
        };

        string[] missing = statefulTypes
            .Where(type => !typeof(ILogicDeterministicStateContributor).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .ToArray();

        CollectionAssert.IsEmpty(
            missing,
            "跨 Tick 改变未来结算的 Buff 模块必须显式贡献确定性状态。");
    }

    [Test]
    public void StatefulBuffContributorHashes_TrackFutureBranchFields()
    {
        AssertPrivateFieldChangesContributorHash(
            new PercentAttackBonusBuff(Fix64.One),
            typeof(PercentAttackBonusBuff),
            "m_Applied",
            true);
        AssertPrivateFieldChangesContributorHash(
            new BuildingPhaseGuardBuff(),
            typeof(BuildingPhaseGuardBuff),
            "_subscribed",
            true);
        AssertPrivateFieldChangesContributorHash(
            new RampedPercentMoveSpeedBonusBuff(Fix64.One, 2f),
            typeof(RampedPercentMoveSpeedBonusBuff),
            "m_Elapsed",
            Fix64.One);
        AssertPrivateFieldChangesContributorHash(
            new DayScalingHeroStatsBuff(Fix64.One, Fix64.One),
            typeof(DayScalingHeroStatsBuff),
            "m_Timer",
            Fix64.One);
        AssertPrivateFieldChangesContributorHash(
            new SkillCheerSquadBuff(1, Fix64.One, Fix64.One),
            typeof(SkillCheerSquadBuff),
            "m_Timer",
            Fix64.One);
        AssertPrivateFieldChangesContributorHash(
            new FriendlyAttackSpeedAreaBuff(FixVector2.Zero, Fix64.One, Fix64.One, Fix64.One, "hash"),
            typeof(PositionAreaRefreshBuff),
            "m_Timer",
            Fix64.One);
        AssertPrivateFieldChangesContributorHash(
            new TimedDeathBuff(),
            typeof(TimedDeathBuff),
            "m_AdditiveDuration",
            Fix64.One);
        AssertPrivateFieldChangesContributorHash(
            new HeroGhostBuff(),
            typeof(HeroGhostBuff),
            "_invincibleSourceId",
            "hero-source");
    }

    [Test]
    public void HeroOutOfCombatSpeed_UsesLogicElapsedTimeForDelayAndRamp()
    {
        var context = new SimEntityContext
        {
            CreatureProperties = new CreaturePropertyManager(property =>
                property == CreatureMainProperty.Speed ? (Fix64)10 : (Fix64)100),
            Brain = new ScriptedBrain { Attack = true },
        };
        var attack = new SimAtkComp();
        attack.Init(context);
        context.AtkComp = attack;
        var component = new CharacterBuffComp();
        context.BuffComp = component;
        component.Init(context);
        BuffData buff = BuffData.Create(
            "hero_out_of_combat_speed_test",
            Fix64.Zero,
            true,
            1,
            new List<BuffCallback> { new HeroOutOfCombatMoveSpeedBuff() });
        Assert.IsTrue(component.AddBuff(buff, context));
        Assert.AreEqual((Fix64)20, context.CreatureProperties.GetProperty(CreatureMainProperty.Speed));

        attack.Attack(Fix64.Zero);
        context.TickOutOfCombatState(0f);
        component.UpdateBuff(LogicFrameRuntime.FixedDeltaTime);
        Assert.AreEqual((Fix64)10, context.CreatureProperties.GetProperty(CreatureMainProperty.Speed));

        attack.InterruptAttack();
        context.TickOutOfCombatState(0f);
        context.TickOutOfCombatState(2.5f);
        component.UpdateBuff(LogicFrameRuntime.FixedDeltaTime);
        Assert.AreEqual((Fix64)15, context.CreatureProperties.GetProperty(CreatureMainProperty.Speed));
        component.ShutDown();
    }

    private sealed class RecordingBuffCallback : BuffCallback
    {
        public IEntityContext InitializedHost { get; private set; }
        public Fix64 UpdatedTime { get; private set; }

        public override void Initialize(BuffData data, IEntityContext entity)
        {
            base.Initialize(data, entity);
            InitializedHost = entity;
        }

        public override void OnUpdate(Fix64 deltaTime)
        {
            UpdatedTime += deltaTime;
        }
    }

    private static ulong ComputeFearBuffHash(LogicEntityId sourceId)
    {
        var source = new SimEntityContext { LogicEntityId = sourceId };
        var host = new SimEntityContext();
        var move = new NoMoveComp();
        move.Init(host);
        host.MoveComp = move;
        var component = new CharacterBuffComp();
        host.BuffComp = component;
        component.Init(host);
        BuffData buff = BuffData.Create(
            "fear_hash_test",
            Fix64.One,
            false,
            1,
            new List<BuffCallback> { new FearMoveAwayBuff(source) });
        Assert.IsTrue(component.AddBuff(buff, host));

        var hasher = new LogicStateHasher();
        component.WriteDeterministicState(hasher);
        component.ShutDown();
        return hasher.Hash;
    }

    private static void AssertPrivateFieldChangesContributorHash(
        BuffCallback module,
        System.Type declaringType,
        string fieldName,
        object changedValue)
    {
        var contributor = module as ILogicDeterministicStateContributor;
        Assert.IsNotNull(contributor, module.GetType().FullName);

        var before = new LogicStateHasher();
        contributor.WriteDeterministicState(before);
        System.Reflection.FieldInfo field = declaringType.GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"{declaringType.FullName}.{fieldName}");
        field.SetValue(module, changedValue);
        var after = new LogicStateHasher();
        contributor.WriteDeterministicState(after);

        Assert.AreNotEqual(before.Hash, after.Hash, $"{declaringType.FullName}.{fieldName}");
    }
}
