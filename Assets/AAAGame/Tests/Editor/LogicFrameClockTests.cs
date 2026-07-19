using System;
using System.Collections.Generic;
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
    public void Advance_PointTwoScale_ExecutesSixFramesPerRealtimeSecond()
    {
        var clock = new LogicFrameClock();
        clock.Start(0d);

        int ticks = clock.Advance(1d, 0.2d, _ => { });

        Assert.AreEqual(6, ticks);
        Assert.AreEqual(6ul, clock.Frame);
        Assert.That(clock.AccumulatorSeconds, Is.EqualTo(0d).Within(1e-9d));
    }

    [Test]
    public void Advance_ScaleChangesDuringCatchUp_AppliesToRemainingRealtime()
    {
        var clock = new LogicFrameClock();
        var cutoffs = new List<double>();
        double scale = 1d;
        clock.Start(0d);

        int ticks = clock.Advance(
            1d,
            () => scale,
            (frame, cutoff) =>
            {
                cutoffs.Add(cutoff);
                if (frame == 1)
                    scale = 0.5d;
            });

        Assert.AreEqual(15, ticks);
        Assert.AreEqual(15ul, clock.Frame);
        Assert.That(cutoffs[0], Is.EqualTo(1d / 30d).Within(1e-9d));
        Assert.That(cutoffs[1], Is.EqualTo(1d / 10d).Within(1e-9d));
        Assert.That(clock.AccumulatorSeconds, Is.EqualTo(LogicFrameClock.FrameDurationSeconds / 2d).Within(1e-9d));
    }

    [Test]
    public void Advance_PauseDuringCatchUp_ConsumesRemainderWithoutBacklog()
    {
        var clock = new LogicFrameClock();
        double scale = 1d;
        clock.Start(0d);

        int pausedTicks = clock.Advance(
            1d,
            () => scale,
            (frame, _) => scale = 0d);
        int resumedTicks = clock.Advance(
            1d + LogicFrameClock.FrameDurationSeconds,
            () => 1d,
            (_, _) => { });

        Assert.AreEqual(1, pausedTicks);
        Assert.AreEqual(1, resumedTicks);
        Assert.AreEqual(2ul, clock.Frame);
    }

    [Test]
    public void Advance_PausePreservesPartialFrameAccumulator()
    {
        var clock = new LogicFrameClock();
        clock.Start(0d);

        Assert.AreEqual(0, clock.Advance(1d / 60d, 1d, _ => { }));
        double accumulatorBeforePause = clock.AccumulatorSeconds;
        Assert.AreEqual(0, clock.Advance(10d, 0d, _ => { }));
        Assert.That(clock.AccumulatorSeconds, Is.EqualTo(accumulatorBeforePause).Within(1e-9d));
        Assert.AreEqual(1, clock.Advance(10d + 1d / 60d, 1d, _ => { }));
    }

    [Test]
    public void Advance_MultipleTicks_ReportsEachRealtimeCutoff()
    {
        var clock = new LogicFrameClock();
        var cutoffs = new List<double>();
        clock.Start(5d);

        clock.Advance(5d + 0.1d, () => 1d, (_, cutoff) => cutoffs.Add(cutoff));

        Assert.AreEqual(3, cutoffs.Count);
        Assert.That(cutoffs[0], Is.EqualTo(5d + 1d / 30d).Within(1e-9d));
        Assert.That(cutoffs[1], Is.EqualTo(5d + 2d / 30d).Within(1e-9d));
        Assert.That(cutoffs[2], Is.EqualTo(5d + 3d / 30d).Within(1e-9d));
    }

    [Test]
    public void Advance_BackwardRealtime_Throws()
    {
        var clock = new LogicFrameClock();
        clock.Start(2d);

        Assert.Throws<InvalidOperationException>(() => clock.Advance(1d, 1d, _ => { }));
    }

    [Test]
    public void Advance_OneHundredThousandTicks_DoesNotDropOrDuplicateFrames()
    {
        const int expectedTicks = 100000;
        var clock = new LogicFrameClock();
        ulong previousFrame = 0;
        int callbackCount = 0;
        clock.Start(0d);

        int ticks = clock.Advance(
            expectedTicks * LogicFrameClock.FrameDurationSeconds,
            1d,
            frame =>
            {
                Assert.AreEqual(previousFrame + 1, frame);
                previousFrame = frame;
                callbackCount++;
            });

        Assert.AreEqual(expectedTicks, ticks);
        Assert.AreEqual(expectedTicks, callbackCount);
        Assert.AreEqual((ulong)expectedTicks, clock.Frame);
        Assert.That(clock.AccumulatorSeconds, Is.EqualTo(0d).Within(1e-9d));
    }
}
