using System;
using NUnit.Framework;

public sealed class LogicFrameClockTests
{
    [Test]
    public void Advance_OneSecond_ExecutesThirtyFrames()
    {
        var clock = new LogicFrameClock();
        int callbacks = 0;
        clock.Start(10d);

        int ticks = clock.Advance(11d, 1d, _ => callbacks++);

        Assert.AreEqual(30, ticks);
        Assert.AreEqual(30, callbacks);
        Assert.AreEqual(30ul, clock.Frame);
        Assert.That(clock.AccumulatorSeconds, Is.EqualTo(0d).Within(1e-9d));
    }

    [Test]
    public void Advance_Hitch_ExecutesEveryOwedFrame()
    {
        var clock = new LogicFrameClock();
        ulong lastFrame = 0;
        clock.Start(0d);

        int ticks = clock.Advance(0.5d, 1d, frame => lastFrame = frame);

        Assert.AreEqual(15, ticks);
        Assert.AreEqual(15ul, lastFrame);
        Assert.AreEqual(15ul, clock.Frame);
    }

    [Test]
    public void Advance_PartialFrames_AccumulatesWithoutDroppingTime()
    {
        var clock = new LogicFrameClock();
        int callbacks = 0;
        clock.Start(0d);

        Assert.AreEqual(0, clock.Advance(1d / 60d, 1d, _ => callbacks++));
        Assert.AreEqual(1, clock.Advance(2d / 60d, 1d, _ => callbacks++));

        Assert.AreEqual(1, callbacks);
        Assert.AreEqual(1ul, clock.Frame);
    }

    [Test]
    public void Advance_Pause_ConsumesRealtimeWithoutCatchUp()
    {
        var clock = new LogicFrameClock();
        int callbacks = 0;
        clock.Start(0d);

        Assert.AreEqual(0, clock.Advance(5d, 0d, _ => callbacks++));
        Assert.AreEqual(1, clock.Advance(5d + LogicFrameClock.FrameDurationSeconds, 1d, _ => callbacks++));

        Assert.AreEqual(1, callbacks);
        Assert.AreEqual(1ul, clock.Frame);
    }

    [Test]
    public void Advance_BackwardRealtime_Throws()
    {
        var clock = new LogicFrameClock();
        clock.Start(2d);

        Assert.Throws<InvalidOperationException>(() => clock.Advance(1d, 1d, _ => { }));
    }
}
