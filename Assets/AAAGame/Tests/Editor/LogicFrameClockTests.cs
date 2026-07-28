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
    public void Advance_TickBudgetDefersRealtimeWithoutDroppingInputOrCutoffs()
    {
        var clock = new LogicFrameClock();
        var timeline = new LogicInputTimeline();
        var cutoffs = new List<double>();
        var pressedFrames = new List<ulong>();
        timeline.Begin(0d, FixVector2.Zero, 0, FixVector2.Zero, false, FixVector2.Zero);
        timeline.EnqueueButtonPulse(0.2d, LogicInputButton.Skill1);
        clock.Start(0d);

        int totalTicks = clock.Advance(
            0.5d,
            () => 1d,
            (frame, cutoff) =>
            {
                cutoffs.Add(cutoff);
                if (timeline.Seal(frame, cutoff).WasPressed(LogicInputButton.Skill1))
                    pressedFrames.Add(frame);
            },
            4);

        Assert.AreEqual(4, totalTicks);
        Assert.AreEqual(4ul, clock.Frame);
        Assert.That(clock.DeferredRealtimeSeconds, Is.EqualTo(0.5d - 4d / 30d).Within(1e-9d));

        while (clock.DeferredRealtimeSeconds > 0d)
        {
            totalTicks += clock.Advance(
                0.5d,
                () => 1d,
                (frame, cutoff) =>
                {
                    cutoffs.Add(cutoff);
                    if (timeline.Seal(frame, cutoff).WasPressed(LogicInputButton.Skill1))
                        pressedFrames.Add(frame);
                },
                4);
        }

        Assert.AreEqual(15, totalTicks);
        Assert.AreEqual(15ul, clock.Frame);
        Assert.AreEqual(15, cutoffs.Count);
        Assert.That(clock.DeferredRealtimeSeconds, Is.EqualTo(0d).Within(1e-9d));
        Assert.That(clock.AccumulatorSeconds, Is.EqualTo(0d).Within(1e-9d));
        CollectionAssert.AreEqual(new[] { 6ul }, pressedFrames);
        for (int i = 0; i < cutoffs.Count; i++)
            Assert.That(cutoffs[i], Is.EqualTo((i + 1d) / 30d).Within(1e-9d));
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
    public void Advance_EpsilonBoundary_DoesNotMoveRealtimeCursorPastSample()
    {
        var clock = new LogicFrameClock();
        double realtime = LogicFrameClock.FrameDurationSeconds - 5e-9d;
        clock.Start(0d);

        Assert.AreEqual(1, clock.Advance(realtime, 1d, _ => { }));
        Assert.DoesNotThrow(() => clock.Advance(realtime, 1d, _ => { }));
        Assert.That(clock.DeferredRealtimeSeconds, Is.EqualTo(0d));
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
    public void RebaseRealtimePreservingAccumulator_DiscardsRenderBacklogWithoutChangingLogicalProgress()
    {
        var clock = new LogicFrameClock();
        clock.Start(0d);
        Assert.AreEqual(0, clock.Advance(0.02d, 1d, _ => { }));
        double accumulator = clock.AccumulatorSeconds;

        clock.RebaseRealtimePreservingAccumulator(5d);
        int ticks = clock.Advance(
            5d + 10d * LogicFrameClock.FrameDurationSeconds,
            1d,
            _ => { });

        Assert.AreEqual(10, ticks);
        Assert.AreEqual(10UL, clock.Frame);
        Assert.That(clock.AccumulatorSeconds, Is.EqualTo(accumulator).Within(1e-9d));
        Assert.Throws<InvalidOperationException>(() => clock.RebaseRealtimePreservingAccumulator(4d));
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

    [Test]
    public void Advance_RenderCadenceDoesNotChangeTickInputOrTimeControlFullHash()
    {
        const double durationSeconds = 10d;
        CadenceRunResult baseline = RunCadence(BuildFixedCadence(15, durationSeconds));

        AssertCadenceEquivalent(baseline, RunCadence(BuildFixedCadence(30, durationSeconds)), "30fps");
        AssertCadenceEquivalent(baseline, RunCadence(BuildFixedCadence(60, durationSeconds)), "60fps");
        AssertCadenceEquivalent(baseline, RunCadence(BuildFixedCadence(120, durationSeconds)), "120fps");
        AssertCadenceEquivalent(baseline, RunCadence(BuildFixedCadence(1000, durationSeconds)), "1000fps");
        AssertCadenceEquivalent(
            baseline,
            RunCadence(new[] { 0.001d, 0.5d, 3.75d, durationSeconds }),
            "hitch");
        AssertCadenceEquivalent(
            baseline,
            RunCadence(new[] { 0.001d, 0.5d, 3.75d, durationSeconds }, 4),
            "budgeted hitch");
    }

    private static CadenceRunResult RunCadence(
        IReadOnlyList<double> pumpTimes,
        int maxTickCount = int.MaxValue)
    {
        Assert.IsFalse(LogicTimeControlService.IsActive, "Cadence test requires an isolated time-control timeline.");
        LogicTimeControlService.BeginTimeline();
        try
        {
            LogicTimeControlService.SubmitTimeScaleCommand(new TimeScaleCommand(
                5,
                1,
                TimeScaleCommandKind.SetBulletTimeScale,
                10,
                7500));
            LogicTimeControlService.SubmitTimeScaleCommand(new TimeScaleCommand(
                12,
                2,
                TimeScaleCommandKind.SetBulletTimeScale,
                10,
                2000));
            LogicTimeControlService.SubmitTimeScaleCommand(new TimeScaleCommand(
                18,
                3,
                TimeScaleCommandKind.RemoveBulletTimeScale,
                10,
                0));
            LogicTimeControlService.SubmitTimeScaleCommand(new TimeScaleCommand(
                25,
                4,
                TimeScaleCommandKind.SetBasePlaybackScale,
                0,
                15000));
            LogicTimeControlService.SubmitTimeScaleCommand(new TimeScaleCommand(
                35,
                5,
                TimeScaleCommandKind.SetBasePlaybackScale,
                0,
                LogicTimeControlService.NormalScaleUnits));

            var timeline = new LogicInputTimeline();
            timeline.Begin(0d, FixVector2.Zero, 0, FixVector2.Zero, false, FixVector2.Zero);
            timeline.EnqueueButtonPressed(0.010d, LogicInputButton.Skill1);
            timeline.EnqueueButtonReleased(0.020d, LogicInputButton.Skill1);
            timeline.EnqueueButtonPulse(LogicFrameClock.FrameDurationSeconds, LogicInputButton.Skill2);
            timeline.EnqueueWorldMove(
                0.700d,
                new FixVector2(Fix64.FromRaw(12345), Fix64.FromRaw(-67890)));
            timeline.EnqueueSelectWorldPosition(
                1.300d,
                new FixVector2(Fix64.FromRaw(333), Fix64.FromRaw(444)));
            timeline.EnqueueButtonPressed(4.125d, LogicInputButton.InteractionPrimary);
            timeline.EnqueueButtonReleased(4.126d, LogicInputButton.InteractionPrimary);

            var clock = new LogicFrameClock();
            var fullHashes = new List<ulong>();
            var cutoffs = new List<double>();
            clock.Start(0d);

            void Pump(double realtime)
            {
                clock.Advance(
                    realtime,
                    () => LogicTimeControlService.SchedulerScale,
                    (frame, cutoff) =>
                    {
                        LogicTimeControlService.BeginFrame(frame);
                        LogicInputFrame inputFrame = timeline.Seal(frame, cutoff);
                        ulong inputHash = LogicStateHasher.ComputeInputHash(inputFrame);
                        ulong timeHash = LogicStateHasher.ComputeTimeControlHash(
                            LogicTimeControlService.CaptureSnapshot());
                        fullHashes.Add(LogicStateHasher.ComputeFrameHash(
                            frame,
                            inputHash,
                            timeHash,
                            frame * 0x9E3779B97F4A7C15UL));
                        cutoffs.Add(cutoff);
                    },
                    maxTickCount);
            }

            for (int i = 0; i < pumpTimes.Count; i++)
                Pump(pumpTimes[i]);

            double finalRealtime = pumpTimes[pumpTimes.Count - 1];
            while (clock.DeferredRealtimeSeconds > 0d)
                Pump(finalRealtime);

            return new CadenceRunResult(
                clock.Frame,
                clock.AccumulatorSeconds,
                timeline.PendingEventCount,
                timeline.LateEventCount,
                fullHashes,
                cutoffs);
        }
        finally
        {
            if (LogicTimeControlService.IsActive)
                LogicTimeControlService.EndTimeline();
        }
    }

    private static List<double> BuildFixedCadence(int renderFrameRate, double durationSeconds)
    {
        int pumpCount = checked((int)(renderFrameRate * durationSeconds));
        var pumpTimes = new List<double>(pumpCount);
        for (int pump = 1; pump <= pumpCount; pump++)
            pumpTimes.Add(pump / (double)renderFrameRate);
        return pumpTimes;
    }

    private static void AssertCadenceEquivalent(
        CadenceRunResult expected,
        CadenceRunResult actual,
        string cadence)
    {
        Assert.AreEqual(expected.Frame, actual.Frame, $"{cadence} changed the final logic frame.");
        Assert.AreEqual(expected.PendingEventCount, actual.PendingEventCount, $"{cadence} changed pending input.");
        Assert.AreEqual(expected.LateEventCount, actual.LateEventCount, $"{cadence} changed late-input accounting.");
        Assert.That(
            actual.AccumulatorSeconds,
            Is.EqualTo(expected.AccumulatorSeconds).Within(1e-8d),
            $"{cadence} changed the remaining clock accumulator.");
        CollectionAssert.AreEqual(expected.FullHashes, actual.FullHashes, $"{cadence} changed a per-Tick FullHash.");
        Assert.AreEqual(expected.Cutoffs.Count, actual.Cutoffs.Count);
        for (int i = 0; i < expected.Cutoffs.Count; i++)
        {
            Assert.That(
                actual.Cutoffs[i],
                Is.EqualTo(expected.Cutoffs[i]).Within(1e-8d),
                $"{cadence} changed the realtime cutoff for logic frame {i + 1}.");
        }
    }

    private sealed class CadenceRunResult
    {
        public CadenceRunResult(
            ulong frame,
            double accumulatorSeconds,
            int pendingEventCount,
            ulong lateEventCount,
            List<ulong> fullHashes,
            List<double> cutoffs)
        {
            Frame = frame;
            AccumulatorSeconds = accumulatorSeconds;
            PendingEventCount = pendingEventCount;
            LateEventCount = lateEventCount;
            FullHashes = fullHashes;
            Cutoffs = cutoffs;
        }

        public ulong Frame { get; }
        public double AccumulatorSeconds { get; }
        public int PendingEventCount { get; }
        public ulong LateEventCount { get; }
        public List<ulong> FullHashes { get; }
        public List<double> Cutoffs { get; }
    }
}
