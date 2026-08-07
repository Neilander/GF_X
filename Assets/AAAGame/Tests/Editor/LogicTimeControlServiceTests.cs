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
    public void BulletTime_LogicTickDuration_ExpiresBeforeTheExclusiveDeadlineFrame()
    {
        LogicTimeControlService.SetBulletTimeScaleForLogicTicks(10, 2000, 3);

        BeginNextFrame();
        LogicTimeControlSnapshot active = LogicTimeControlService.CaptureSnapshot();
        Assert.AreEqual(2000, LogicTimeControlService.BulletTimeScaleUnits);
        Assert.AreEqual(4ul, active.BulletTimeScales[0].ExpirationFrameExclusive);

        BeginNextFrame();
        BeginNextFrame();
        Assert.AreEqual(2000, LogicTimeControlService.BulletTimeScaleUnits);

        BeginNextFrame();
        Assert.AreEqual(LogicTimeControlService.NormalScaleUnits, LogicTimeControlService.BulletTimeScaleUnits);
        Assert.AreEqual(0, LogicTimeControlService.CaptureSnapshot().BulletTimeScales.Count);
    }

    [Test]
    public void BulletTime_OverlappingTimedSources_ExpireIndependently()
    {
        LogicTimeControlService.SetBulletTimeScaleForLogicTicks(10, 2000, 2);
        LogicTimeControlService.SetBulletTimeScaleForLogicTicks(20, 5000, 4);

        BeginNextFrame();
        Assert.AreEqual(2000, LogicTimeControlService.BulletTimeScaleUnits);
        BeginNextFrame();
        Assert.AreEqual(2000, LogicTimeControlService.BulletTimeScaleUnits);

        BeginNextFrame();
        Assert.AreEqual(5000, LogicTimeControlService.BulletTimeScaleUnits);
        BeginNextFrame();
        Assert.AreEqual(5000, LogicTimeControlService.BulletTimeScaleUnits);

        BeginNextFrame();
        Assert.AreEqual(LogicTimeControlService.NormalScaleUnits, LogicTimeControlService.BulletTimeScaleUnits);
    }

    [Test]
    public void BulletTime_RenewalOnExpirationFrame_ReplacesTheOldDeadline()
    {
        LogicTimeControlService.SubmitTimeScaleCommand(new TimeScaleCommand(
            1,
            1,
            TimeScaleCommandKind.SetBulletTimeScaleForLogicTicks,
            10,
            2000,
            2));
        LogicTimeControlService.SubmitTimeScaleCommand(new TimeScaleCommand(
            3,
            2,
            TimeScaleCommandKind.SetBulletTimeScaleForLogicTicks,
            10,
            5000,
            2));

        BeginNextFrame();
        BeginNextFrame();
        BeginNextFrame();

        LogicTimeControlSnapshot renewed = LogicTimeControlService.CaptureSnapshot();
        Assert.AreEqual(5000, LogicTimeControlService.BulletTimeScaleUnits);
        Assert.AreEqual(5ul, renewed.BulletTimeScales[0].ExpirationFrameExclusive);

        BeginNextFrame();
        BeginNextFrame();
        Assert.AreEqual(LogicTimeControlService.NormalScaleUnits, LogicTimeControlService.BulletTimeScaleUnits);
    }

    [Test]
    public void BulletTime_ManualRemovalOnExpirationFrame_DoesNotDoubleRemove()
    {
        LogicTimeControlService.SubmitTimeScaleCommand(new TimeScaleCommand(
            1,
            1,
            TimeScaleCommandKind.SetBulletTimeScaleForLogicTicks,
            10,
            2000,
            2));
        LogicTimeControlService.SubmitTimeScaleCommand(new TimeScaleCommand(
            3,
            2,
            TimeScaleCommandKind.RemoveBulletTimeScale,
            10,
            0));

        BeginNextFrame();
        BeginNextFrame();

        Assert.DoesNotThrow(BeginNextFrame);
        Assert.AreEqual(LogicTimeControlService.NormalScaleUnits, LogicTimeControlService.BulletTimeScaleUnits);
    }

    [Test]
    public void BulletTime_LogicTickDuration_FreezesWhilePaused()
    {
        var clock = new LogicFrameClock();
        clock.Start(0d);
        LogicTimeControlService.SetBulletTimeScaleForLogicTicks(10, 5000, 2);

        int firstTicks = clock.Advance(
            2d * LogicFrameClock.FrameDurationSeconds,
            PrepareNextFrameScale,
            (frame, _) => LogicTimeControlService.BeginFrame(frame));
        Assert.AreEqual(1, firstTicks);
        Assert.AreEqual(1ul, LogicTimeControlService.CurrentFrame);

        LogicTimeControlService.AcquirePause(20);
        Assert.AreEqual(
            0,
            clock.Advance(10d, PrepareNextFrameScale, (frame, _) => LogicTimeControlService.BeginFrame(frame)));
        Assert.AreEqual(1ul, LogicTimeControlService.CurrentFrame);
        Assert.AreEqual(3ul, LogicTimeControlService.CaptureSnapshot().BulletTimeScales[0].ExpirationFrameExclusive);

        LogicTimeControlService.ReleasePause(20);
        Assert.AreEqual(
            1,
            clock.Advance(
                10d + 2d * LogicFrameClock.FrameDurationSeconds,
                PrepareNextFrameScale,
                (frame, _) => LogicTimeControlService.BeginFrame(frame)));
        Assert.AreEqual(2ul, LogicTimeControlService.CurrentFrame);
        Assert.AreEqual(5000, LogicTimeControlService.BulletTimeScaleUnits);

        Assert.AreEqual(
            1,
            clock.Advance(
                10d + 3d * LogicFrameClock.FrameDurationSeconds,
                PrepareNextFrameScale,
                (frame, _) => LogicTimeControlService.BeginFrame(frame)));
        Assert.AreEqual(3ul, LogicTimeControlService.CurrentFrame);
        Assert.AreEqual(LogicTimeControlService.NormalScaleUnits, LogicTimeControlService.BulletTimeScaleUnits);
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
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LogicTimeControlService.SetBulletTimeScaleForLogicTicks(10, 2000, 0));
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
    public void LevelSwitchPauseOwnership_NormalClose_ReleasesAuthorityToken()
    {
        var gameObject = new GameObject("LevelSwitchPauseOwnershipNormalCloseTest");
        try
        {
            LevelSwitchUIForm form = gameObject.AddComponent<LevelSwitchUIForm>();
            LogicTimeControlService.AcquirePause(LogicTimeControlSources.LevelSwitchUiPause);
            SetLevelSwitchPauseOwnership(form, true);

            InvokeLevelSwitchPauseOwnershipClose(form, false);

            Assert.IsFalse(LogicTimeControlService.HasPause(LogicTimeControlSources.LevelSwitchUiPause));
            Assert.IsFalse(GetLevelSwitchPauseOwnership(form));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void LevelSwitchPauseOwnership_FrameworkShutdown_DoesNotCommandEndedAuthority()
    {
        var gameObject = new GameObject("LevelSwitchPauseOwnershipFrameworkShutdownTest");
        try
        {
            LevelSwitchUIForm form = gameObject.AddComponent<LevelSwitchUIForm>();
            LogicTimeControlService.AcquirePause(LogicTimeControlSources.LevelSwitchUiPause);
            SetLevelSwitchPauseOwnership(form, true);
            LogicTimeControlService.EndTimeline();

            Assert.DoesNotThrow(() => InvokeLevelSwitchPauseOwnershipClose(form, true));
            Assert.IsFalse(GetLevelSwitchPauseOwnership(form));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
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
    public void SnapshotRestore_PreservesActiveLogicTickExpiration()
    {
        LogicTimeControlService.SetBulletTimeScaleForLogicTicks(10, 2000, 5);
        BeginNextFrame();
        LogicTimeControlSnapshot snapshot = LogicTimeControlService.CaptureSnapshot();

        BeginNextFrame();
        LogicTimeControlService.RestoreSnapshot(snapshot);

        LogicTimeControlSnapshot restored = LogicTimeControlService.CaptureSnapshot();
        Assert.AreEqual(1ul, restored.CurrentFrame);
        Assert.AreEqual(6ul, restored.BulletTimeScales[0].ExpirationFrameExclusive);
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

    private static double PrepareNextFrameScale()
    {
        LogicTimeControlService.PrepareFrame(checked(LogicTimeControlService.CurrentFrame + 1));
        return LogicTimeControlService.SchedulerScale;
    }

    private static void InvokeLevelSwitchPauseOwnershipClose(LevelSwitchUIForm form, bool isShutdown)
    {
        typeof(LevelSwitchUIForm)
            .GetMethod("RelinquishPauseOwnership", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(form, new object[] { isShutdown });
    }

    private static void SetLevelSwitchPauseOwnership(LevelSwitchUIForm form, bool holdsPause)
    {
        typeof(LevelSwitchUIForm)
            .GetField("m_HoldsLogicPause", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(form, holdsPause);
    }

    private static bool GetLevelSwitchPauseOwnership(LevelSwitchUIForm form)
    {
        return (bool)typeof(LevelSwitchUIForm)
            .GetField("m_HoldsLogicPause", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(form);
    }
}
