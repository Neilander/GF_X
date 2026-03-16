using NUnit.Framework;

public class ComponentLockingTests
{
    [Test]
    public void 未锁定时组件可运行()
    {
        var ctx = new SimEntityContext();
        var moveComp = new NoMoveComp();
        ctx.MoveComp = moveComp;

        Assert.IsTrue(ctx.CanRun(moveComp));
    }

    [Test]
    public void 锁定后组件不可运行()
    {
        var ctx = new SimEntityContext();
        var moveComp = new NoMoveComp();
        var atkComp = new SimAtkComp();
        ctx.MoveComp = moveComp;
        ctx.AtkComp = atkComp;

        ctx.LockComp(moveComp, atkComp);

        Assert.IsFalse(ctx.CanRun(moveComp));
    }

    [Test]
    public void 解锁后组件恢复运行()
    {
        var ctx = new SimEntityContext();
        var moveComp = new NoMoveComp();
        var atkComp = new SimAtkComp();
        ctx.MoveComp = moveComp;
        ctx.AtkComp = atkComp;

        ctx.LockComp(moveComp, atkComp);
        ctx.ResumeComp(moveComp, atkComp);

        Assert.IsTrue(ctx.CanRun(moveComp));
    }

    [Test]
    public void 多个锁需全部解除才能运行()
    {
        var ctx = new SimEntityContext();
        var moveComp = new NoMoveComp();
        var locker1 = new SimAtkComp();
        var locker2 = new SimAtkComp();

        ctx.LockComp(moveComp, locker1);
        ctx.LockComp(moveComp, locker2);

        ctx.ResumeComp(moveComp, locker1);
        Assert.IsFalse(ctx.CanRun(moveComp), "还有一个锁未解除");

        ctx.ResumeComp(moveComp, locker2);
        Assert.IsTrue(ctx.CanRun(moveComp), "所有锁都解除了");
    }
}
