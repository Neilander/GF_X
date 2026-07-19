using System;
using NUnit.Framework;
using UnityEngine;

public sealed class LogicTimeControlServiceTests
{
    [SetUp]
    public void SetUp()
    {
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();

        LogicTimeControlService.BeginTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
    }

    [Test]
    public void BulletTime_MultipleSources_StrongestSlowdownWins()
    {
        LogicTimeControlService.SetBulletTimeScale(20, 5000);
        LogicTimeControlService.SetBulletTimeScale(10, 2000);
        BeginNextFrame();

        Assert.AreEqual(2000, LogicTimeControlService.BulletTimeScaleUnits);
        Assert.That(LogicTimeControlService.SchedulerScale, Is.EqualTo(0.2d).Within(1e-12d));

        LogicTimeControlService.RemoveBulletTimeScale(10);
        BeginNextFrame();

        Assert.AreEqual(5000, LogicTimeControlService.BulletTimeScaleUnits);
    }

    [Test]
    public void Pause_MultipleSources_ResumesOnlyAfterLastRelease()
    {
        LogicTimeControlService.SetBulletTimeScale(10, 2500);
        BeginNextFrame();
        LogicTimeControlService.AcquirePause(20);
        LogicTimeControlService.AcquirePause(30);

        Assert.IsTrue(LogicTimeControlService.IsPaused);
        Assert.AreEqual(0d, LogicTimeControlService.SchedulerScale);
        Assert.AreEqual(0f, LogicTimeControlService.AnimationScale);

        LogicTimeControlService.ReleasePause(20);

        Assert.IsTrue(LogicTimeControlService.IsPaused);
        Assert.AreEqual(0d, LogicTimeControlService.SchedulerScale);

        LogicTimeControlService.ReleasePause(30);

        Assert.IsFalse(LogicTimeControlService.IsPaused);
        Assert.That(LogicTimeControlService.SchedulerScale, Is.EqualTo(0.25d).Within(1e-12d));
        Assert.That(LogicTimeControlService.AnimationScale, Is.EqualTo(0.25f).Within(1e-6f));
    }

    [Test]
    public void EffectiveScale_MultipliesBasePlaybackAndBulletTimeExactlyInUnits()
    {
        LogicTimeControlService.SetBasePlaybackScale(15000);
        LogicTimeControlService.SetBulletTimeScale(10, 2500);
        BeginNextFrame();

        Assert.AreEqual(3750, LogicTimeControlService.EffectiveSimulationScaleUnits);
    }

    [Test]
    public void InvalidReleaseAndRemoval_ReportStateErrors()
    {
        Assert.Throws<InvalidOperationException>(() => LogicTimeControlService.ReleasePause(10));
        LogicTimeControlService.RemoveBulletTimeScale(10);
        Assert.Throws<InvalidOperationException>(() => LogicTimeControlService.PrepareFrame(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogicTimeControlService.SetBulletTimeScale(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogicTimeControlService.AcquirePause(0));
    }

    [Test]
    public void AnimationPresenter_AppliesGlobalScaleAndRestoresBaseSpeedOnDispose()
    {
        var gameObject = new GameObject("AnimationRatePresenterTest");
        try
        {
            Animator animator = gameObject.AddComponent<Animator>();
            animator.speed = 2f;
            var presenter = new AnimationRatePresenter(animator);

            LogicTimeControlService.SetBulletTimeScale(10, 2500);
            BeginNextFrame();
            presenter.ApplyCurrentScale();
            Assert.That(animator.speed, Is.EqualTo(0.5f).Within(1e-6f));

            LogicTimeControlService.AcquirePause(20);
            presenter.ApplyCurrentScale();
            Assert.AreEqual(0f, animator.speed);

            presenter.Dispose();
            Assert.AreEqual(2f, animator.speed);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void TimeScaleCommand_AppliesAtEffectiveFrameBoundary()
    {
        LogicTimeControlService.SetBulletTimeScale(10, 2000);

        Assert.AreEqual(LogicTimeControlService.NormalScaleUnits, LogicTimeControlService.BulletTimeScaleUnits);
        Assert.AreEqual(1ul, LogicTimeControlService.LastAcceptedSequence);

        LogicTimeControlService.PrepareFrame(1);

        Assert.AreEqual(2000, LogicTimeControlService.BulletTimeScaleUnits);
        Assert.AreEqual(0ul, LogicTimeControlService.CurrentFrame);

        LogicTimeControlService.BeginFrame(1);

        Assert.AreEqual(1ul, LogicTimeControlService.CurrentFrame);
    }

    [Test]
    public void PauseCommands_UseOneExternalSequenceAndTargetNextFrame()
    {
        var commands = new System.Collections.Generic.List<PauseControlCommand>();
        LogicTimeControlService.PauseControlCommandApplied += commands.Add;
        try
        {
            LogicTimeControlService.AcquirePause(10);
            LogicTimeControlService.ReleasePause(10);

            Assert.AreEqual(2, commands.Count);
            Assert.AreEqual(1ul, commands[0].EffectiveFrame);
            Assert.AreEqual(1ul, commands[0].Sequence);
            Assert.AreEqual(1ul, commands[1].EffectiveFrame);
            Assert.AreEqual(2ul, commands[1].Sequence);
            Assert.IsFalse(LogicTimeControlService.IsPaused);
        }
        finally
        {
            LogicTimeControlService.PauseControlCommandApplied -= commands.Add;
        }
    }

    [Test]
    public void SnapshotRestore_PreservesPendingCommandsAndStableSourceOrder()
    {
        LogicTimeControlService.SetBulletTimeScale(20, 5000);
        LogicTimeControlService.SetBulletTimeScale(10, 2000);
        LogicTimeControlService.AcquirePause(30);
        LogicTimeControlService.AcquirePause(25);
        LogicTimeControlSnapshot snapshot = LogicTimeControlService.CaptureSnapshot();

        LogicTimeControlService.ReleasePause(25);
        LogicTimeControlService.ReleasePause(30);
        LogicTimeControlService.PrepareFrame(1);
        LogicTimeControlService.RestoreSnapshot(snapshot);

        LogicTimeControlSnapshot restored = LogicTimeControlService.CaptureSnapshot();
        Assert.AreEqual(2, restored.PendingTimeScaleCommands.Count);
        Assert.AreEqual(25, restored.PauseSources[0]);
        Assert.AreEqual(30, restored.PauseSources[1]);
        Assert.AreEqual(20, restored.PendingTimeScaleCommands[0].SourceId);
        Assert.AreEqual(10, restored.PendingTimeScaleCommands[1].SourceId);
        Assert.AreEqual(
            LogicStateHasher.ComputeTimeControlHash(snapshot),
            LogicStateHasher.ComputeTimeControlHash(restored));
    }

    [Test]
    public void FrameTimelineReset_ClearsScaleButPreservesExternalPauseOwners()
    {
        LogicTimeControlService.SetBulletTimeScale(10, 2000);
        BeginNextFrame();
        LogicTimeControlService.AcquirePause(20);

        LogicTimeControlService.ResetFrameTimelinePreservingPauses();

        Assert.AreEqual(0ul, LogicTimeControlService.CurrentFrame);
        Assert.AreEqual(0ul, LogicTimeControlService.LastAcceptedSequence);
        Assert.AreEqual(LogicTimeControlService.NormalScaleUnits, LogicTimeControlService.BulletTimeScaleUnits);
        Assert.IsTrue(LogicTimeControlService.HasPause(20));
    }

    private static void BeginNextFrame()
    {
        LogicTimeControlService.BeginFrame(checked(LogicTimeControlService.CurrentFrame + 1));
    }
}
