using NUnit.Framework;

public class LogicDamageEventServiceTests
{
    private sealed class FrameAction : ILogicFrameUpdate
    {
        public System.Action Action;
        public int LogicFrameOrder => 0;

        public void OnLogicFrameUpdate(Fix64 deltaTime)
        {
            Action();
        }
    }

    private readonly FrameAction m_FrameAction = new FrameAction();

    [SetUp]
    public void SetUp()
    {
        if (LogicFrameRuntime.IsActive)
            LogicFrameRuntime.End();
        LogicFrameRuntime.Begin();
        LogicFrameRuntime.StartTimeline();
        LogicDamageEventService.BeginTimeline();
        LogicFrameRuntime.Register(m_FrameAction);
    }

    [TearDown]
    public void TearDown()
    {
        if (!LogicFrameRuntime.IsActive)
            return;
        LogicFrameRuntime.Unregister(m_FrameAction);
        if (LogicDamageEventService.IsActive)
            LogicDamageEventService.EndTimeline();
        LogicFrameRuntime.End();
    }

    [Test]
    public void DirectDamageWithoutAttacker_IsQueuedUntilDamageResolve()
    {
        var target = CreateEntity(1, 10);

        m_FrameAction.Action = () =>
        {
            LogicDamageEventService.BeginFrame(LogicFrameRuntime.CurrentFrame);
            DamageHelper.DoDirectDamage(target, (Fix64)3, HealthModifyType.reduce);
            Assert.AreEqual((Fix64)10, target.HealthValue);
            LogicDamageEventService.ApplyFrame(LogicFrameRuntime.CurrentFrame);
        };

        LogicFrameRuntime.Tick(1);

        Assert.AreEqual((Fix64)7, target.HealthValue);
        Assert.AreEqual(0, LogicDamageEventService.LastOrderedEvents[0].AttackerId.Value);
        Assert.IsFalse(LogicDamageEventService.LastOrderedEvents[0].ApplyDamageHooks);
    }

    [Test]
    public void HealTargetUsingPureLogicContext_IsQueuedUntilDamageResolve()
    {
        var healer = CreateEntity(1, 10);
        var target = CreateEntity(2, 10);
        target.TakeDamage((Fix64)6, HealthModifyType.reduce);

        m_FrameAction.Action = () =>
        {
            LogicDamageEventService.BeginFrame(LogicFrameRuntime.CurrentFrame);
            LogicDamageEventService.SubmitHeal(healer, target, (Fix64)3);
            Assert.AreEqual((Fix64)4, target.HealthValue);
            LogicDamageEventService.ApplyFrame(LogicFrameRuntime.CurrentFrame);
        };

        LogicFrameRuntime.Tick(1);

        Assert.AreEqual((Fix64)7, target.HealthValue);
        Assert.AreEqual(LogicHealthEventKind.Heal, LogicDamageEventService.LastOrderedEvents[0].Kind);
        Assert.AreEqual(1, LogicDamageEventService.LastAppliedCount);
    }

    [Test]
    public void SameFrameDamage_IsSortedByStableIdentity_AndDeadTargetIsSkipped()
    {
        var firstAttacker = CreateEntity(1, 100);
        var secondAttacker = CreateEntity(2, 100);
        var target = CreateEntity(3, 10);

        m_FrameAction.Action = () =>
        {
            LogicDamageEventService.BeginFrame(LogicFrameRuntime.CurrentFrame);
            DamageHelper.DoDamage(target, new Damage(secondAttacker, (Fix64)10, HealthModifyType.reduce), secondAttacker);
            DamageHelper.DoDamage(target, new Damage(firstAttacker, (Fix64)10, HealthModifyType.reduce), firstAttacker);
            LogicDamageEventService.ApplyFrame(LogicFrameRuntime.CurrentFrame);
        };

        LogicFrameRuntime.Tick(1);

        Assert.AreEqual(2, LogicDamageEventService.LastSubmittedCount);
        Assert.AreEqual(1, LogicDamageEventService.LastAppliedCount);
        Assert.AreEqual(1, LogicDamageEventService.LastSkippedDeadTargetCount);
        Assert.AreEqual(1, LogicDamageEventService.LastOrderedEvents[0].AttackerId.Value);
        Assert.AreEqual(2, LogicDamageEventService.LastOrderedEvents[1].AttackerId.Value);
        Assert.IsFalse(target.Alive);
    }

    [Test]
    public void QueuedMultiHit_PreservesOriginalHitIndexAfterSorting()
    {
        var attacker = CreateEntity(1, 100);
        var firstSubmittedTarget = CreateEntity(3, 100);
        var secondSubmittedTarget = CreateEntity(2, 100);

        m_FrameAction.Action = () =>
        {
            LogicDamageEventService.BeginFrame(LogicFrameRuntime.CurrentFrame);
            using (DamageHelper.BeginAttackHitSequence(attacker, 2))
            {
                DamageHelper.DoDamage(firstSubmittedTarget, new Damage(attacker, (Fix64)1, HealthModifyType.reduce), attacker);
                DamageHelper.DoDamage(secondSubmittedTarget, new Damage(attacker, (Fix64)1, HealthModifyType.reduce), attacker);
            }
            LogicDamageEventService.ApplyFrame(LogicFrameRuntime.CurrentFrame);
        };

        LogicFrameRuntime.Tick(1);

        Assert.AreEqual(2, LogicDamageEventService.LastOrderedEvents.Count);
        Assert.AreEqual(2, LogicDamageEventService.LastOrderedEvents[0].TargetId.Value);
        Assert.AreEqual(2, LogicDamageEventService.LastOrderedEvents[0].HitIndex);
        Assert.AreEqual(3, LogicDamageEventService.LastOrderedEvents[1].TargetId.Value);
        Assert.AreEqual(1, LogicDamageEventService.LastOrderedEvents[1].HitIndex);
    }

    [Test]
    public void FirstHitPerTargetCritical_UsesLogicEntityIdentityAcrossQueuedDamageFrames()
    {
        var attacker = CreateEntity(1, 100);
        var firstTarget = CreateEntity(2, 100);
        var secondTarget = CreateEntity(3, 100);
        var buffComp = new AAAGame.Scripts.BuffSystem.CharacterBuffComp();
        attacker.BuffComp = buffComp;
        buffComp.Init(attacker);
        Assert.IsTrue(buffComp.AddBuff(
            BuffData.Create(
                "first_hit_logic_identity_test",
                Fix64.Zero,
                true,
                1,
                new System.Collections.Generic.List<BuffCallback>
                {
                    new FirstHitPerTargetCriticalBuff(),
                }),
            attacker));

        m_FrameAction.Action = () =>
        {
            LogicDamageEventService.BeginFrame(LogicFrameRuntime.CurrentFrame);
            DamageHelper.DoDamage(firstTarget, new Damage(attacker, (Fix64)10, HealthModifyType.reduce), attacker);
            LogicDamageEventService.ApplyFrame(LogicFrameRuntime.CurrentFrame);
        };
        LogicFrameRuntime.Tick(1);

        m_FrameAction.Action = () =>
        {
            LogicDamageEventService.BeginFrame(LogicFrameRuntime.CurrentFrame);
            DamageHelper.DoDamage(firstTarget, new Damage(attacker, (Fix64)10, HealthModifyType.reduce), attacker);
            DamageHelper.DoDamage(secondTarget, new Damage(attacker, (Fix64)10, HealthModifyType.reduce), attacker);
            LogicDamageEventService.ApplyFrame(LogicFrameRuntime.CurrentFrame);
        };
        LogicFrameRuntime.Tick(2);

        Assert.AreEqual((Fix64)75, firstTarget.HealthValue);
        Assert.AreEqual((Fix64)85, secondTarget.HealthValue);
        buffComp.ShutDown();
    }

    private static SimEntityContext CreateEntity(int logicId, int health)
    {
        var entity = new SimEntityContext
        {
            LogicEntityId = new LogicEntityId(logicId),
        };
        entity.Health.Init((Fix64)health);
        return entity;
    }
}
