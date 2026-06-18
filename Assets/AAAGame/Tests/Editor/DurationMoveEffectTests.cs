using NUnit.Framework;
using UnityEngine;

public class DurationMoveEffectTests
{
    private SimEntityContext CreateContext()
    {
        var ctx = new SimEntityContext { Position = Vector3.zero };
        var executor = new SimMoveExecutor();
        ctx.MoveExecutor = executor;
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
        effectComp.StartDurationOverrideMove(0.5f, Vector3.back * 20f);
        effectComp.ApplyEffect(0.1f);
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
        effectComp.StartDurationAdditionalMove(1f, Vector3.right * 3f);
        effectComp.ApplyEffect(0.1f);
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
        effectComp.StartDurationAdditionalMove(0.2f, Vector3.right * 10f);

        // 第一帧：有效果
        effectComp.ApplyEffect(0.1f);
        executor.Execute(0.1f);
        float posAfterFrame1 = executor.Position.x;

        // 第二帧：效果还在
        effectComp.ApplyEffect(0.1f);
        executor.Execute(0.1f);
        float posAfterFrame2 = executor.Position.x;

        // 第三帧：效果应已过期
        effectComp.ApplyEffect(0.1f);
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

        int id = effectComp.StartDurationAdditionalMove(10f, Vector3.right * 10f);

        // 第一帧有效果
        effectComp.ApplyEffect(0.1f);
        executor.Execute(0.1f);
        Assert.Greater(executor.Position.x, 0f);

        float posBeforeStop = executor.Position.x;

        // 手动停止
        effectComp.StopAddtionalMove(id);

        // 下一帧不应再有效果
        effectComp.ApplyEffect(0.1f);
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

        effectComp.StartDurationAdditionalMove(0.1f, Vector3.right * 5f);
        effectComp.ApplyEffect(0.1f);
        executor.SetInput(Vector3.forward * 4f);
        executor.Execute(0.1f);

        Assert.AreEqual(0f, executor.Position.z, 0.01f, "位移生效帧不应保留主动移动");

        effectComp.ApplyEffect(0.1f);
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

        effectComp.StartDurationAdditionalMove(0.1f, Vector3.right * 3f);
        effectComp.StartDurationOverrideMove(0.1f, Vector3.back * 6f);

        effectComp.ApplyEffect(0.1f);
        Assert.AreEqual(MovementMode.Displaced, executor.MovementMode, "位移生效期间应切入 Displaced");
        executor.SetInput(Vector3.forward * 5f);
        executor.Execute(0.1f);
        Assert.Less(executor.Position.z, 0f, "Override 生效帧应优先执行位移");

        effectComp.ApplyEffect(0.1f);
        Assert.AreEqual(MovementMode.Normal, executor.MovementMode, "所有位移效果结束后应恢复 Normal");
        executor.SetInput(Vector3.forward * 5f);
        executor.Execute(0.1f);

        Assert.Greater(executor.Position.z, -0.6f, "恢复 Normal 后主动移动应重新生效");
    }
}
