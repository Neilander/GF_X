using NUnit.Framework;

public class LogicProjectileServiceTests
{
    private sealed class FrameAction : ILogicFrameUpdate
    {
        public System.Action<Fix64> Action;
        public int LogicFrameOrder => 0;

        public void OnLogicFrameUpdate(Fix64 deltaTime)
        {
            Action(deltaTime);
        }
    }

    private sealed class AlertProbeTargeting : ITargetingComp, IAlertTargetingComp
    {
        public int NotificationCount { get; private set; }
        public IEntityContext CurrentTarget { get; set; }
        public IEntityContext AggroTarget => CurrentTarget;

        public void Init(IEntityContext ctx) { }
        public void UpdateTargeting(Fix64 deltaTime) { }
        public void ClearAggro() => CurrentTarget = null;
        public void ShutDown() { }
        public void Resume() { }

        public void NotifyAllyFoundEnemy(IEntityContext enemy)
        {
            NotificationCount++;
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicProjectileService.IsActive)
            LogicProjectileService.EndTimeline();
    }

    [Test]
    public void AdvanceToward_ReachesTargetWithoutOvershoot()
    {
        FixVector2 result = LogicProjectileService.AdvanceToward(
            FixVector2.Zero,
            new FixVector2((Fix64)3, (Fix64)4),
            (Fix64)5,
            out bool arrived);

        Assert.IsTrue(arrived);
        Assert.AreEqual(new FixVector2((Fix64)3, (Fix64)4), result);
    }

    [Test]
    public void AdvanceToward_UsesFixedPointDistanceStep()
    {
        FixVector2 result = LogicProjectileService.AdvanceToward(
            FixVector2.Zero,
            new FixVector2((Fix64)10, Fix64.Zero),
            (Fix64)2,
            out bool arrived);

        Assert.IsFalse(arrived);
        Assert.AreEqual((Fix64)2, result.x);
        Assert.AreEqual(Fix64.Zero, result.y);
    }

    [Test]
    public void EmptySnapshotRestore_PreservesAllocatorAndFrameState()
    {
        LogicProjectileService.BeginTimeline();
        LogicProjectileSnapshot snapshot = LogicProjectileService.CaptureSnapshot();

        LogicProjectileService.RestoreSnapshot(snapshot);

        Assert.AreEqual(0, LogicProjectileService.ActiveCount);
        Assert.AreEqual(0, LogicProjectileService.RetainedViewStateCount);
        Assert.AreEqual(0UL, LogicProjectileService.LastCompletedFrame);
    }

    [Test]
    public void ViewReservation_CancelReturnsToUnboundState()
    {
        LogicProjectileService.BeginTimeline();
        ulong projectileId = SubmitProjectileForViewBindingTest();

        LogicProjectileService.ReserveView(projectileId);
        Assert.AreEqual(
            LogicProjectileViewBindingState.Reserved,
            LogicProjectileService.GetRequiredViewBindingState(projectileId));

        LogicProjectileService.CancelViewReservation(projectileId);
        Assert.AreEqual(
            LogicProjectileViewBindingState.None,
            LogicProjectileService.GetRequiredViewBindingState(projectileId));
    }

    [Test]
    public void SynchronousViewBind_ReplacesReservationUntilViewRelease()
    {
        LogicProjectileService.BeginTimeline();
        ulong projectileId = SubmitProjectileForViewBindingTest();

        LogicProjectileService.ReserveView(projectileId);
        LogicProjectileService.BindView(projectileId);

        Assert.AreEqual(
            LogicProjectileViewBindingState.Bound,
            LogicProjectileService.GetRequiredViewBindingState(projectileId));
        Assert.Throws<System.InvalidOperationException>(
            () => LogicProjectileService.CancelViewReservation(projectileId));

        LogicProjectileService.ReleaseView(projectileId);
        Assert.AreEqual(
            LogicProjectileViewBindingState.None,
            LogicProjectileService.GetRequiredViewBindingState(projectileId));
    }

    [Test]
    public void ProjectileSubmissionSource_DoesNotRequireBoundViewInsideLogicTick()
    {
        string root = UnityEngine.Application.dataPath;
        string rangedWeaponSource = System.IO.File.ReadAllText(
            System.IO.Path.Combine(root, "AAAGame/Scripts/GeneralCreature/RangedWeaponSO.cs"));
        string directAttackSource = System.IO.File.ReadAllText(
            System.IO.Path.Combine(root, "AAAGame/Scripts/GeneralCreature/DirectAtkComp.cs"));

        StringAssert.DoesNotContain("TryGetBoundView", rangedWeaponSource);
        StringAssert.DoesNotContain("RequireProjectileOrigin", rangedWeaponSource);
        StringAssert.Contains("ProjectilePresentationService.Publish", rangedWeaponSource);
        StringAssert.Contains("ProjectilePresentationService.Publish", directAttackSource);
    }

    [Test]
    public void DeadUnregisteredDamageSource_DoesNotReadCurrentSnapshotOrNotifyAllies()
    {
        if (LogicFrameRuntime.IsActive)
            throw new System.InvalidOperationException("Damage-source regression test requires an inactive logic runtime.");

        var victim = new SimEntityContext
        {
            LogicEntityId = new LogicEntityId(1),
            PositionFixed = FixVector2.Zero,
            Side = SideType.PlayerSide,
        };
        var ally = new SimEntityContext
        {
            LogicEntityId = new LogicEntityId(2),
            PositionFixed = FixVector2.Zero,
            Side = SideType.PlayerSide,
        };
        var attacker = new SimEntityContext
        {
            LogicEntityId = new LogicEntityId(3),
            PositionFixed = new FixVector2(Fix64.One, Fix64.Zero),
            Side = SideType.EnemySide,
            Alive = false,
        };
        victim.SetProperty(CreatureMainProperty.CollisionRadius, Fix64.Zero);
        ally.SetProperty(CreatureMainProperty.CollisionRadius, Fix64.Zero);
        var alertProbe = new AlertProbeTargeting();
        ally.TargetComp = alertProbe;
        var frameAction = new FrameAction
        {
            Action = _ => LogicFactionVisionService.HandleSuccessfulDamage(victim, attacker),
        };
        bool listenerRegistered = false;

        try
        {
            EntityRegistry.Clear();
            EntityRegistry.Register(victim);
            EntityRegistry.Register(ally);
            LogicFrameRuntime.Begin();
            LogicFrameRuntime.StartTimeline();
            LogicEntityFrameSnapshotService.BeginTimeline();
            LogicFrameRuntime.Register(frameAction);
            listenerRegistered = true;

            Assert.DoesNotThrow(() => LogicFrameRuntime.Tick(1));
            Assert.IsFalse(LogicEntityFrameSnapshotService.Current.TryGet(attacker.LogicEntityId, out _));
            Assert.AreEqual(0, alertProbe.NotificationCount);
        }
        finally
        {
            if (listenerRegistered && LogicFrameRuntime.IsActive)
                LogicFrameRuntime.Unregister(frameAction);
            if (LogicEntityFrameSnapshotService.IsActive)
                LogicEntityFrameSnapshotService.EndTimeline();
            if (LogicFrameRuntime.IsActive)
                LogicFrameRuntime.End();
            EntityRegistry.Clear();
        }
    }

    [Test]
    public void TargetDiesInFlight_ProjectileContinuesToLastAimPointWithoutViewOrDamage()
    {
        if (LogicFrameRuntime.IsActive)
            throw new System.InvalidOperationException("Projectile regression test requires an inactive logic runtime.");

        var attacker = new SimEntityContext
        {
            LogicEntityId = new LogicEntityId(1),
            PositionFixed = FixVector2.Zero,
            Side = SideType.PlayerSide,
        };
        var target = new SimEntityContext
        {
            LogicEntityId = new LogicEntityId(2),
            PositionFixed = new FixVector2((Fix64)3, Fix64.Zero),
            Side = SideType.EnemySide,
        };
        attacker.SetProperty(CreatureMainProperty.CollisionRadius, Fix64.Zero);
        target.SetProperty(CreatureMainProperty.CollisionRadius, Fix64.Zero);
        attacker.Health.Init((Fix64)100);
        target.Health.Init((Fix64)100);
        var weapon = new WeaponData(
            WeaponType.Projectile,
            (Fix64)10,
            Fix64.One,
            (Fix64)100,
            (Fix64)5.4f,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.One,
            Fix64.Zero,
            System.Array.Empty<Fix64>());
        var frameAction = new FrameAction();
        ulong projectileId = 0;
        bool viewBound = false;
        bool listenerRegistered = false;

        try
        {
            EntityRegistry.Clear();
            EntityRegistry.Register(attacker);
            EntityRegistry.Register(target);
            LogicFrameRuntime.Begin();
            LogicFrameRuntime.StartTimeline();
            LogicEntityFrameSnapshotService.BeginTimeline();
            LogicDamageEventService.BeginTimeline();
            LogicProjectileService.BeginTimeline();
            frameAction.Action = deltaTime =>
            {
                ulong frame = LogicFrameRuntime.CurrentFrame;
                LogicDamageEventService.BeginFrame(frame);
                if (projectileId == 0)
                    projectileId = LogicProjectileService.Submit(attacker, target, weapon);
                else
                    LogicProjectileService.AdvanceFrame(frame, deltaTime);
                LogicDamageEventService.ApplyFrame(frame);
            };
            LogicFrameRuntime.Register(frameAction);
            listenerRegistered = true;

            LogicFrameRuntime.Tick(1);
            LogicProjectileService.ReserveView(projectileId);
            LogicProjectileService.BindView(projectileId);
            viewBound = true;
            FixVector2 lastAimPoint = LogicProjectileService.CaptureActiveStates()[0].LastAimPoint;

            target.TakeDamage((Fix64)100, HealthModifyType.reduce, attacker);
            LogicFrameRuntime.Tick(2);

            LogicProjectileDeterministicState afterDeath = LogicProjectileService.CaptureActiveStates()[0];
            Assert.IsFalse(afterDeath.IsTrackingTarget);
            Assert.AreEqual(lastAimPoint, afterDeath.LastAimPoint);
            Assert.IsTrue(afterDeath.Position.x > Fix64.Zero);
            Assert.IsTrue(afterDeath.Position.x < lastAimPoint.x);
            Assert.IsFalse(LogicProjectileService.GetRequiredViewState(projectileId).Completed);

            for (int i = 0; i < 30 && LogicProjectileService.ActiveCount > 0; i++)
                LogicFrameRuntime.Tick(LogicFrameRuntime.CurrentFrame + 1);

            LogicProjectileViewState completed = LogicProjectileService.GetRequiredViewState(projectileId);
            Assert.IsTrue(completed.Completed);
            Assert.IsFalse(completed.Hit);
            Assert.AreEqual(lastAimPoint, completed.Position);
            Assert.AreEqual(0, LogicDamageEventService.LastAppliedCount);
        }
        finally
        {
            if (listenerRegistered && LogicFrameRuntime.IsActive)
                LogicFrameRuntime.Unregister(frameAction);
            if (viewBound && LogicProjectileService.IsActive)
                LogicProjectileService.ReleaseView(projectileId);
            if (LogicProjectileService.IsActive)
                LogicProjectileService.EndTimeline();
            if (LogicDamageEventService.IsActive)
                LogicDamageEventService.EndTimeline();
            if (LogicEntityFrameSnapshotService.IsActive)
                LogicEntityFrameSnapshotService.EndTimeline();
            if (LogicFrameRuntime.IsActive)
                LogicFrameRuntime.End();
            EntityRegistry.Clear();
        }
    }

    private static ulong SubmitProjectileForViewBindingTest()
    {
        if (LogicFrameRuntime.IsActive)
            throw new System.InvalidOperationException("Projectile view-binding test requires an inactive logic runtime.");

        var attacker = new SimEntityContext
        {
            LogicEntityId = new LogicEntityId(1),
            PositionFixed = FixVector2.Zero,
            Side = SideType.PlayerSide,
        };
        var target = new SimEntityContext
        {
            LogicEntityId = new LogicEntityId(2),
            PositionFixed = new FixVector2(Fix64.One, Fix64.Zero),
            Side = SideType.EnemySide,
        };
        attacker.SetProperty(CreatureMainProperty.CollisionRadius, Fix64.Zero);
        target.SetProperty(CreatureMainProperty.CollisionRadius, Fix64.Zero);
        var weapon = new WeaponData(
            WeaponType.Projectile,
            Fix64.One,
            Fix64.One,
            (Fix64)100,
            (Fix64)5.4f,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.One,
            Fix64.Zero,
            System.Array.Empty<Fix64>());
        var frameAction = new FrameAction();
        ulong projectileId = 0;
        bool listenerRegistered = false;

        try
        {
            EntityRegistry.Clear();
            EntityRegistry.Register(attacker);
            EntityRegistry.Register(target);
            LogicFrameRuntime.Begin();
            LogicFrameRuntime.StartTimeline();
            LogicEntityFrameSnapshotService.BeginTimeline();
            LogicDamageEventService.BeginTimeline();
            frameAction.Action = _ =>
            {
                ulong frame = LogicFrameRuntime.CurrentFrame;
                LogicDamageEventService.BeginFrame(frame);
                projectileId = LogicProjectileService.Submit(attacker, target, weapon);
                LogicDamageEventService.ApplyFrame(frame);
            };
            LogicFrameRuntime.Register(frameAction);
            listenerRegistered = true;
            LogicFrameRuntime.Tick(1);
            return projectileId;
        }
        finally
        {
            if (listenerRegistered && LogicFrameRuntime.IsActive)
                LogicFrameRuntime.Unregister(frameAction);
            if (LogicDamageEventService.IsActive)
                LogicDamageEventService.EndTimeline();
            if (LogicEntityFrameSnapshotService.IsActive)
                LogicEntityFrameSnapshotService.EndTimeline();
            if (LogicFrameRuntime.IsActive)
                LogicFrameRuntime.End();
            EntityRegistry.Clear();
        }
    }
}
