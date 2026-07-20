using System.Collections.Generic;
using GameFramework;
using NUnit.Framework;
using AAAGame.Scripts.BuffSystem;

[TestFixture]
public sealed class BuffLogicTimeTests
{
    [Test]
    public void OneSecondBuff_ExpiresOnlyFromFixedLogicTicks()
    {
        BuffData buff = BuffData.Create("test_fixed_duration", 1f, false, 1, new List<BuffCallback>());
        try
        {
            for (int i = 0; i < 30; i++)
                Assert.IsFalse(buff.AdvanceLogicTime(LogicFrameRuntime.FixedDeltaTime));

            Assert.IsTrue(buff.AdvanceLogicTime(LogicFrameRuntime.FixedDeltaTime));
        }
        finally
        {
            ReferencePool.Release(buff);
        }
    }

    [Test]
    public void PermanentBuff_DoesNotConsumeLogicTime()
    {
        BuffData buff = BuffData.Create("test_forever", 1f, true, 1, new List<BuffCallback>());
        try
        {
            Fix64 before = buff.remainingTime;
            Assert.IsFalse(buff.AdvanceLogicTime(LogicFrameRuntime.FixedDeltaTime));
            Assert.AreEqual(before, buff.remainingTime);
        }
        finally
        {
            ReferencePool.Release(buff);
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
            10f,
            false,
            1,
            new List<BuffCallback> { callback });
        Assert.IsTrue(component.AddBuff(buff, context));

        component.UpdateBuff(LogicFrameRuntime.FixedDeltaTime);

        Assert.AreSame(context, callback.InitializedHost);
        Assert.AreEqual(LogicFrameRuntime.FixedDeltaTime.RawValue, callback.UpdatedTime.RawValue);
        component.ShutDown();
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
            float.MaxValue,
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
}
