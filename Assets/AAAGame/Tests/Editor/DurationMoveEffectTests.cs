using NUnit.Framework;
using UnityEngine;

public class DurationMoveEffectTests
{
    [SetUp]
    public void SetUp()
    {
        EntityRegistry.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        EntityRegistry.Clear();
    }

    private SimEntityContext CreateContext()
    {
        var ctx = new SimEntityContext { Position = Vector3.zero };
        var executor = new SimMoveExecutor();
        ctx.MoveExecutor = executor;
        ctx.SetProperty(CreatureMainProperty.WeightLevel, (Fix64)2);
        ctx.SetProperty(CreatureMainProperty.CollisionRadius, (Fix64)5);
        var attack = new NoAtkComp();
        attack.Init(ctx);
        ctx.AtkComp = attack;
        return ctx;
    }

    [Test]
    public void 持续击飞效果覆盖正常移动()
    {
        var ctx = CreateContext();
        var executor = ctx.MoveExecutor as SimMoveExecutor;
        var effectComp = new DurationMoveEffectComp();
        effectComp.Init(ctx);

        // 正常移动输入
        executor.SetInput(Vector3.forward * 5f);

        // 击飞效果：向后 20m/s，持续 0.5s
        effectComp.StartDurationOverrideMove((Fix64)0.5f, new FixVector2(Fix64.Zero, (Fix64)(-20)));
        effectComp.ApplyEffect((Fix64)0.1f);
        executor.Execute(0.1f);

        // 应该向后，被 Override 覆盖了
        Assert.Less(executor.Position.z, 0f, "击飞应覆盖正常移动方向");
    }

    [Test]
    public void 持续外力期间主动移动被抑制()
    {
        var ctx = CreateContext();
        var executor = ctx.MoveExecutor as SimMoveExecutor;
        var effectComp = new DurationMoveEffectComp();
        effectComp.Init(ctx);

        executor.SetInput(Vector3.forward * 5f);

        // 附加风力：向右 3m/s
        effectComp.StartDurationAdditionalMove(Fix64.One, new FixVector2((Fix64)3, Fix64.Zero));
        effectComp.ApplyEffect((Fix64)0.1f);
        executor.Execute(0.1f);

        // 位移期间不能主动寻路移动，只保留外力方向
        Assert.AreEqual(0f, executor.Position.z, 0.01f);
        Assert.Greater(executor.Position.x, 0f);
    }

    [Test]
    public void 效果超时后自动停止()
    {
        var ctx = CreateContext();
        var executor = ctx.MoveExecutor as SimMoveExecutor;
        var effectComp = new DurationMoveEffectComp();
        effectComp.Init(ctx);

        // 0.2 秒的 Additional 效果
        effectComp.StartDurationAdditionalMove((Fix64)0.2f, new FixVector2((Fix64)10, Fix64.Zero));

        // 第一帧：有效果
        effectComp.ApplyEffect((Fix64)0.1f);
        executor.Execute(0.1f);
        float posAfterFrame1 = executor.Position.x;

        // 第二帧：效果还在
        effectComp.ApplyEffect((Fix64)0.1f);
        executor.Execute(0.1f);
        float posAfterFrame2 = executor.Position.x;

        // 第三帧：效果应已过期
        effectComp.ApplyEffect((Fix64)0.1f);
        executor.Execute(0.1f);
        float posAfterFrame3 = executor.Position.x;

        Assert.Greater(posAfterFrame1, 0f, "第一帧应该有移动");
        Assert.Greater(posAfterFrame2, posAfterFrame1, "第二帧应该继续移动");
        Assert.AreEqual(posAfterFrame2, posAfterFrame3, 0.01f, "第三帧效果过期，不应再移动");
    }

    [Test]
    public void 手动停止效果()
    {
        var ctx = CreateContext();
        var executor = ctx.MoveExecutor as SimMoveExecutor;
        var effectComp = new DurationMoveEffectComp();
        effectComp.Init(ctx);

        int id = effectComp.StartDurationAdditionalMove((Fix64)10, new FixVector2((Fix64)10, Fix64.Zero));

        // 第一帧有效果
        effectComp.ApplyEffect((Fix64)0.1f);
        executor.Execute(0.1f);
        Assert.Greater(executor.Position.x, 0f);

        float posBeforeStop = executor.Position.x;

        // 手动停止
        effectComp.StopAddtionalMove(id);

        // 下一帧不应再有效果
        effectComp.ApplyEffect((Fix64)0.1f);
        executor.Execute(0.1f);

        Assert.AreEqual(posBeforeStop, executor.Position.x, 0.01f);
    }

    [Test]
    public void 位移结束后恢复主动移动()
    {
        var ctx = CreateContext();
        var executor = ctx.MoveExecutor as SimMoveExecutor;
        var effectComp = new DurationMoveEffectComp();
        effectComp.Init(ctx);

        effectComp.StartDurationAdditionalMove((Fix64)0.1f, new FixVector2((Fix64)5, Fix64.Zero));
        effectComp.ApplyEffect((Fix64)0.1f);
        executor.SetInput(Vector3.forward * 4f);
        executor.Execute(0.1f);

        Assert.AreEqual(0f, executor.Position.z, 0.01f, "位移生效帧不应保留主动移动");

        effectComp.ApplyEffect((Fix64)0.1f);
        executor.SetInput(Vector3.forward * 4f);
        executor.Execute(0.1f);

        Assert.Greater(executor.Position.z, 0f, "位移结束后应恢复主动移动");
    }

    [Test]
    public void Override与Additional同时结束后恢复Normal模式()
    {
        var ctx = CreateContext();
        var executor = ctx.MoveExecutor as SimMoveExecutor;
        var effectComp = new DurationMoveEffectComp();
        effectComp.Init(ctx);

        effectComp.StartDurationAdditionalMove((Fix64)0.1f, new FixVector2((Fix64)3, Fix64.Zero));
        effectComp.StartDurationOverrideMove((Fix64)0.1f, new FixVector2(Fix64.Zero, (Fix64)(-6)));

        effectComp.ApplyEffect((Fix64)0.1f);
        Assert.AreEqual(MovementMode.Displaced, executor.MovementMode, "位移生效期间应切入 Displaced");
        executor.SetInput(Vector3.forward * 5f);
        executor.Execute(0.1f);
        Assert.Less(executor.Position.z, 0f, "Override 生效帧应优先执行位移");

        effectComp.ApplyEffect((Fix64)0.1f);
        Assert.AreEqual(MovementMode.Normal, executor.MovementMode, "所有位移效果结束后应恢复 Normal");
        executor.SetInput(Vector3.forward * 5f);
        executor.Execute(0.1f);

        Assert.Greater(executor.Position.z, -0.6f, "恢复 Normal 后主动移动应重新生效");
    }

    [Test]
    public void 多个持续位移在FixVector2中按Raw精确累加()
    {
        var ctx = CreateContext();
        var executor = (SimMoveExecutor)ctx.MoveExecutor;
        var effectComp = new DurationMoveEffectComp();
        effectComp.Init(ctx);

        FixVector2 first = new FixVector2(Fix64.FromRaw(1235), Fix64.FromRaw(-678));
        FixVector2 second = new FixVector2(Fix64.FromRaw(-234), Fix64.FromRaw(901));
        effectComp.StartDurationAdditionalMove(Fix64.One, first);
        effectComp.StartDurationAdditionalMove(Fix64.One, second);

        effectComp.ApplyEffect(LogicFrameRuntime.FixedDeltaTime);

        Assert.AreEqual(first.x.RawValue + second.x.RawValue, executor.LastFixedExternal.x.RawValue);
        Assert.AreEqual(first.y.RawValue + second.y.RawValue, executor.LastFixedExternal.y.RawValue);
    }

    [Test]
    public void 力度差映射三级配置_小于负一不施力()
    {
        Assert.IsTrue(DisplacementForceUtility.TryResolveKnockbackVelocity((Fix64)1, (Fix64)2, out Fix64 levelM1));
        Assert.IsTrue(DisplacementForceUtility.TryResolveKnockbackVelocity((Fix64)2, (Fix64)2, out Fix64 level0));
        Assert.IsTrue(DisplacementForceUtility.TryResolveKnockbackVelocity((Fix64)3, (Fix64)2, out Fix64 level1));
        Assert.IsFalse(DisplacementForceUtility.TryResolveKnockbackVelocity(Fix64.Zero, (Fix64)2, out Fix64 rejected));

        Assert.AreEqual(DistanceUnitConverter.ConvertToWorld((Fix64)3).RawValue, levelM1.RawValue);
        Assert.AreEqual(DistanceUnitConverter.ConvertToWorld((Fix64)8).RawValue, level0.RawValue);
        Assert.AreEqual(DistanceUnitConverter.ConvertToWorld((Fix64)12).RawValue, level1.RawValue);
        Assert.AreEqual(Fix64.Zero, rejected);

        Assert.IsTrue(DisplacementForceUtility.TryResolvePull((Fix64)1, (Fix64)2, out Fix64 pullM1, out Fix64 durationM1));
        Assert.IsTrue(DisplacementForceUtility.TryResolvePull((Fix64)2, (Fix64)2, out Fix64 pull0, out Fix64 duration0));
        Assert.IsTrue(DisplacementForceUtility.TryResolvePull((Fix64)3, (Fix64)2, out Fix64 pull1, out Fix64 duration1));
        Assert.AreEqual(DistanceUnitConverter.ConvertToWorld((Fix64)5).RawValue, pullM1.RawValue);
        Assert.AreEqual(DistanceUnitConverter.ConvertToWorld((Fix64)25).RawValue, pull0.RawValue);
        Assert.AreEqual(DistanceUnitConverter.ConvertToWorld((Fix64)100).RawValue, pull1.RawValue);
        Assert.AreEqual(
            DistanceUnitConverter.ReadRequiredPositiveFixedConfig(DisplacementForceUtility.PullDurationLevelM1Key).RawValue,
            durationM1.RawValue);
        Assert.AreEqual(Fix64.One.RawValue, duration0.RawValue);
        Assert.AreEqual(Fix64.One.RawValue, duration1.RawValue);

        SimEntityContext ctx = CreateContext();
        var effectComp = new DurationMoveEffectComp();
        effectComp.Init(ctx);
        Assert.IsFalse(effectComp.TryApplyKnockback(new FixVector2(Fix64.One, Fix64.Zero), Fix64.Zero));
        Assert.IsFalse(effectComp.IsInLossOfBalance);
        Assert.IsTrue(ctx.CanRun(ctx.AtkComp));
    }

    [Test]
    public void 推力速度可叠加_失衡至少持续零点一秒并锁定攻击()
    {
        SimEntityContext ctx = CreateContext();
        var effectComp = new DurationMoveEffectComp();
        effectComp.Init(ctx);

        Assert.IsTrue(effectComp.TryApplyKnockback(new FixVector2(Fix64.One, Fix64.Zero), (Fix64)3));
        Assert.IsTrue(effectComp.TryApplyKnockback(new FixVector2(Fix64.One, Fix64.Zero), (Fix64)3));
        Fix64 oneImpulse = DistanceUnitConverter.ConvertToWorld((Fix64)12);
        Assert.AreEqual((oneImpulse + oneImpulse).RawValue, effectComp.DisplacementVelocity.x.RawValue);
        Assert.IsFalse(ctx.CanRun(ctx.AtkComp), "失衡期间攻击组件必须被锁定");

        effectComp.CommitStaticCollision(new FixVector2(-Fix64.One, Fix64.Zero));
        Assert.AreEqual(FixVector2.Zero, effectComp.DisplacementVelocity);
        for (int i = 0; i < 3; i++)
        {
            effectComp.ApplyEffect(LogicFrameRuntime.FixedDeltaTime);
            ((SimMoveExecutor)ctx.MoveExecutor).Execute((float)LogicFrameRuntime.FixedDeltaTime);
            Assert.IsTrue(effectComp.IsInLossOfBalance, $"第 {i + 1} 帧仍未达到最短失衡时间");
        }

        effectComp.ApplyEffect(LogicFrameRuntime.FixedDeltaTime);
        ((SimMoveExecutor)ctx.MoveExecutor).Execute((float)LogicFrameRuntime.FixedDeltaTime);
        Assert.IsFalse(effectComp.IsInLossOfBalance);
        Assert.IsTrue(ctx.CanRun(ctx.AtkComp), "退出失衡后必须恢复攻击组件");
    }

    [Test]
    public void 撞墙仅清除径向速度并保留切向速度()
    {
        SimEntityContext ctx = CreateContext();
        var effectComp = new DurationMoveEffectComp();
        effectComp.Init(ctx);

        effectComp.TryApplyKnockback(new FixVector2(-Fix64.One, Fix64.One), (Fix64)3);
        Fix64 tangentBefore = effectComp.DisplacementVelocity.y;
        effectComp.CommitStaticCollision(new FixVector2(Fix64.One, Fix64.Zero));

        Assert.AreEqual(Fix64.Zero, effectComp.DisplacementVelocity.x);
        Assert.AreEqual(tangentBefore.RawValue, effectComp.DisplacementVelocity.y.RawValue);
    }

    [Test]
    public void 摩擦力持续降速并在低于阈值后清空速度退出失衡()
    {
        SimEntityContext ctx = CreateContext();
        var effectComp = new DurationMoveEffectComp();
        effectComp.Init(ctx);
        effectComp.TryApplyKnockback(new FixVector2(Fix64.One, Fix64.Zero), (Fix64)3);

        Fix64 previousSpeed = FixVector2.Magnitude(effectComp.DisplacementVelocity);
        int frameCount = 0;
        while (effectComp.IsInLossOfBalance && frameCount < 100)
        {
            effectComp.ApplyEffect(LogicFrameRuntime.FixedDeltaTime);
            ((SimMoveExecutor)ctx.MoveExecutor).Execute((float)LogicFrameRuntime.FixedDeltaTime);
            Fix64 currentSpeed = FixVector2.Magnitude(effectComp.DisplacementVelocity);
            Assert.LessOrEqual(currentSpeed.RawValue, previousSpeed.RawValue);
            previousSpeed = currentSpeed;
            frameCount++;
        }

        Assert.Less(frameCount, 100, "摩擦力应在有限帧内让单位退出失衡");
        Assert.AreEqual(FixVector2.Zero, effectComp.DisplacementVelocity);
        Assert.AreEqual(MovementMode.Normal, ((SimMoveExecutor)ctx.MoveExecutor).MovementMode);
    }

    [Test]
    public void 单条拉力按当前距离与初始距离四次方衰减()
    {
        SimEntityContext source = CreateContext();
        source.Position = Vector3.zero;
        SimEntityContext target = CreateContext();
        target.Position = new Vector3(2f, 0f, 0f);
        var effectComp = new DurationMoveEffectComp();
        effectComp.Init(target);
        EntityRegistry.Register(source);
        EntityRegistry.Register(target);

        Assert.IsTrue(effectComp.TryStartPull(source.LogicEntityId, (Fix64)3));
        target.Position = new Vector3(1.65f, 0f, 0f);
        effectComp.ApplyEffect(LogicFrameRuntime.FixedDeltaTime);

        Fix64 sourceRadius = DistanceUnitConverter.ConvertToWorld((Fix64)5);
        Fix64 initialDistance = (Fix64)2 - sourceRadius;
        Fix64 currentDistance = (Fix64)1.65f - sourceRadius;
        Fix64 ratio = currentDistance / initialDistance;
        Fix64 ratioSquared = ratio * ratio;
        Fix64 acceleration = DistanceUnitConverter.ConvertToWorld((Fix64)100)
                             * ratioSquared
                             * ratioSquared;
        Fix64 friction = DistanceUnitConverter.ConvertToWorld((Fix64)12);
        Fix64 expectedSpeed = (acceleration - friction) * LogicFrameRuntime.FixedDeltaTime;
        Assert.That(
            Fix64.Abs(effectComp.DisplacementVelocity.x).RawValue,
            Is.EqualTo(expectedSpeed.RawValue).Within(2));
    }

    [Test]
    public void 单条拉力进入停止半径后速度归零但保持失衡和钩锁()
    {
        SimEntityContext source = CreateContext();
        source.Position = Vector3.zero;
        SimEntityContext target = CreateContext();
        target.Position = new Vector3(2f, 0f, 0f);
        var effectComp = new DurationMoveEffectComp();
        effectComp.Init(target);
        EntityRegistry.Register(source);
        EntityRegistry.Register(target);

        Assert.IsTrue(effectComp.TryStartPull(source.LogicEntityId, (Fix64)3));
        target.Position = new Vector3(1.3f, 0f, 0f);
        effectComp.ApplyEffect(LogicFrameRuntime.FixedDeltaTime);

        Assert.AreEqual(FixVector2.Zero, effectComp.DisplacementVelocity);
        Assert.IsTrue(effectComp.IsInLossOfBalance);
        var tethers = new System.Collections.Generic.List<DisplacementPullTetherState>();
        effectComp.CopyActivePullTethers(tethers);
        Assert.AreEqual(1, tethers.Count);
        Assert.AreEqual(source.LogicEntityId, tethers[0].SourceEntityId);
    }

    [Test]
    public void 拉力叠加并捕获命中时重量_来源离场后保留滑行速度()
    {
        SimEntityContext source = CreateContext();
        source.Position = Vector3.zero;
        SimEntityContext target = CreateContext();
        target.Position = new Vector3(2f, 0f, 0f);
        var effectComp = new DurationMoveEffectComp();
        effectComp.Init(target);
        EntityRegistry.Register(source);
        EntityRegistry.Register(target);

        Assert.IsTrue(effectComp.TryStartPull(source.LogicEntityId, (Fix64)3));
        Assert.IsTrue(effectComp.TryStartPull(source.LogicEntityId, (Fix64)3));
        target.SetProperty(CreatureMainProperty.WeightLevel, (Fix64)4);
        effectComp.ApplyEffect(LogicFrameRuntime.FixedDeltaTime);

        Fix64 acceleration = DistanceUnitConverter.ConvertToWorld((Fix64)100) * (Fix64)2;
        Fix64 friction = DistanceUnitConverter.ConvertToWorld((Fix64)12);
        Fix64 expectedSpeed = (acceleration - friction) * LogicFrameRuntime.FixedDeltaTime;
        Assert.That(
            Fix64.Abs(effectComp.DisplacementVelocity.x).RawValue,
            Is.EqualTo(expectedSpeed.RawValue).Within(2));
        Assert.Less(effectComp.DisplacementVelocity.x.RawValue, Fix64.Zero.RawValue);

        Fix64 beforeSourceLeaves = FixVector2.Magnitude(effectComp.DisplacementVelocity);
        EntityRegistry.Unregister(source);
        effectComp.ApplyEffect(LogicFrameRuntime.FixedDeltaTime);
        Fix64 afterSourceLeaves = FixVector2.Magnitude(effectComp.DisplacementVelocity);
        Assert.Greater(afterSourceLeaves.RawValue, Fix64.Zero.RawValue);
        Assert.Less(afterSourceLeaves.RawValue, beforeSourceLeaves.RawValue);
        var tethers = new System.Collections.Generic.List<DisplacementPullTetherState>();
        effectComp.CopyActivePullTethers(tethers);
        Assert.AreEqual(0, tethers.Count);
    }

    [Test]
    public void 沉重步伐词条仅给敌方单位增加一级重量并改变受力档位()
    {
        var tag = new LevelTagTable();
        string serialized = string.Join("\t", new[]
        {
            string.Empty,
            "105",
            "test",
            "test",
            "1",
            "0",
            "LvTag_HeavyStride",
            "Tests/Icon",
            "Tests_Name",
            "Tests_Desc",
            string.Empty,
            string.Empty,
            "False",
            "1",
            "0",
        });
        Assert.IsTrue(tag.ParseDataRow(serialized, null));

        var enemyModules = new System.Collections.Generic.List<BuffCallback>();
        System.Reflection.MethodInfo addUnitModules = typeof(LevelTagRuntime).GetMethod(
            "AddUnitModules",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(addUnitModules);
        addUnitModules.Invoke(null, new object[]
        {
            tag,
            new CharacterDataDetail(),
            UnitType.Unit_Intern,
            EntitySideHelper.EnemyFactionId,
            enemyModules,
        });
        Assert.AreEqual(1, enemyModules.Count);
        Assert.IsInstanceOf<MainPropertyAdditiveBuff>(enemyModules[0]);

        var playerModules = new System.Collections.Generic.List<BuffCallback>();
        addUnitModules.Invoke(null, new object[]
        {
            tag,
            new CharacterDataDetail(),
            UnitType.Unit_Intern,
            EntitySideHelper.PlayerFactionId,
            playerModules,
        });
        Assert.AreEqual(0, playerModules.Count);

        SimEntityContext enemy = CreateContext();
        var properties = new CreaturePropertyManager(property =>
            property == CreatureMainProperty.WeightLevel ? (Fix64)2 : Fix64.Zero);
        var buffComp = new AAAGame.Scripts.BuffSystem.CharacterBuffComp();
        enemy.CreatureProperties = properties;
        enemy.BuffComp = buffComp;
        buffComp.Init(enemy);
        try
        {
            BuffData buff = BuffData.Create(
                "test_level_tag_heavy_stride",
                Fix64.Zero,
                true,
                1,
                enemyModules);
            Assert.IsTrue(buffComp.AddBuff(buff, enemy));
            Assert.AreEqual(((Fix64)3).RawValue, properties.GetProperty(CreatureMainProperty.WeightLevel).RawValue);

            Assert.IsTrue(DisplacementForceUtility.TryResolveKnockbackVelocity(
                (Fix64)3,
                properties.GetProperty(CreatureMainProperty.WeightLevel),
                out Fix64 heavierVelocity));
            Assert.AreEqual(DistanceUnitConverter.ConvertToWorld((Fix64)8).RawValue, heavierVelocity.RawValue);

            Assert.IsTrue(buffComp.RemoveBuff("test_level_tag_heavy_stride"));
            Assert.AreEqual(((Fix64)2).RawValue, properties.GetProperty(CreatureMainProperty.WeightLevel).RawValue);
        }
        finally
        {
            properties.Dispose();
        }
    }
}
