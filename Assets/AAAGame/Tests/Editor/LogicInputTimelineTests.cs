using NUnit.Framework;

public sealed class LogicInputTimelineTests
{
    [Test]
    public void NoTickBetweenPressAndRelease_PreservesBothEdges()
    {
        var timeline = CreateTimeline();
        timeline.EnqueueButtonPressed(0.010d, LogicInputButton.InteractionPrimary);
        timeline.EnqueueButtonReleased(0.020d, LogicInputButton.InteractionPrimary);

        LogicInputFrame frame = timeline.Seal(1, 1d / 30d);

        Assert.IsTrue(frame.WasPressed(LogicInputButton.InteractionPrimary));
        Assert.IsTrue(frame.WasReleased(LogicInputButton.InteractionPrimary));
        Assert.IsFalse(frame.IsHeld(LogicInputButton.InteractionPrimary));
        Assert.AreEqual(1, frame.GetPressCount(LogicInputButton.InteractionPrimary));
        Assert.AreEqual(2, frame.Events.Count);
    }

    [Test]
    public void CatchUpTicks_ConsumePressedEdgeOnlyOnce()
    {
        var timeline = CreateTimeline();
        timeline.EnqueueButtonPressed(0.050d, LogicInputButton.InteractionPrimary);

        LogicInputFrame first = timeline.Seal(1, 1d / 30d);
        LogicInputFrame second = timeline.Seal(2, 2d / 30d);
        LogicInputFrame third = timeline.Seal(3, 3d / 30d);

        Assert.IsFalse(first.WasPressed(LogicInputButton.InteractionPrimary));
        Assert.IsTrue(second.WasPressed(LogicInputButton.InteractionPrimary));
        Assert.AreEqual(1, second.GetPressCount(LogicInputButton.InteractionPrimary));
        Assert.IsFalse(third.WasPressed(LogicInputButton.InteractionPrimary));
        Assert.AreEqual(0, third.GetPressCount(LogicInputButton.InteractionPrimary));
        Assert.IsTrue(third.IsHeld(LogicInputButton.InteractionPrimary));
    }

    [Test]
    public void MultipleEventsInOneTick_PreserveOrderAndPressCount()
    {
        var timeline = CreateTimeline();
        timeline.EnqueueButtonPressed(0.010d, LogicInputButton.InteractionSecondary);
        timeline.EnqueueButtonReleased(0.010d, LogicInputButton.InteractionSecondary);
        timeline.EnqueueButtonPressed(0.010d, LogicInputButton.InteractionSecondary);

        LogicInputFrame frame = timeline.Seal(1, 1d / 30d);

        Assert.AreEqual(3, frame.Events.Count);
        Assert.AreEqual(RawInputEventKind.ButtonPressed, frame.Events[0].Kind);
        Assert.AreEqual(RawInputEventKind.ButtonReleased, frame.Events[1].Kind);
        Assert.AreEqual(RawInputEventKind.ButtonPressed, frame.Events[2].Kind);
        Assert.Less(frame.Events[0].Sequence, frame.Events[1].Sequence);
        Assert.Less(frame.Events[1].Sequence, frame.Events[2].Sequence);
        Assert.AreEqual(2, frame.GetPressCount(LogicInputButton.InteractionSecondary));
        Assert.IsTrue(frame.WasPressed(LogicInputButton.InteractionSecondary));
        Assert.IsTrue(frame.WasReleased(LogicInputButton.InteractionSecondary));
        Assert.IsTrue(frame.IsHeld(LogicInputButton.InteractionSecondary));
    }

    [Test]
    public void AxisStateCarriesForwardWithoutRepeatingAnEvent()
    {
        var timeline = CreateTimeline();
        var worldMove = new FixVector2((Fix64)0.5f, (Fix64)(-0.25f));
        timeline.EnqueueWorldMove(0.010d, worldMove);

        LogicInputFrame first = timeline.Seal(1, 1d / 30d);
        LogicInputFrame second = timeline.Seal(2, 2d / 30d);

        Assert.AreEqual(worldMove, first.WorldMove);
        Assert.AreEqual(worldMove, second.WorldMove);
        Assert.AreEqual(1, first.Events.Count);
        Assert.AreEqual(0, second.Events.Count);
    }

    [Test]
    public void ResetGameplayState_ReleasesHeldButtonsAndStopsMovementOnItsTick()
    {
        var timeline = CreateTimeline();
        var movement = new FixVector2((Fix64)0.75f, (Fix64)(-0.25f));
        timeline.EnqueueButtonPressed(0.010d, LogicInputButton.InteractionPrimary);
        timeline.EnqueueWorldMove(0.010d, movement);

        LogicInputFrame beforePause = timeline.Seal(1, 1d / 30d);
        Assert.IsTrue(beforePause.IsHeld(LogicInputButton.InteractionPrimary));
        Assert.AreEqual(movement, beforePause.WorldMove);

        timeline.EnqueueResetGameplayState(0.040d);
        LogicInputFrame paused = timeline.Seal(2, 2d / 30d);

        Assert.IsFalse(paused.IsHeld(LogicInputButton.InteractionPrimary));
        Assert.IsTrue(paused.WasReleased(LogicInputButton.InteractionPrimary));
        Assert.IsFalse(paused.WasPressed(LogicInputButton.InteractionPrimary));
        Assert.AreEqual(FixVector2.Zero, paused.WorldMove);
        Assert.AreEqual(1, paused.Events.Count);
        Assert.AreEqual(RawInputEventKind.ResetGameplayState, paused.Events[0].Kind);
    }

    [Test]
    public void ResumeSynchronization_RestoresHeldAndMovementWithoutPressedEdge()
    {
        var timeline = CreateTimeline();
        timeline.EnqueueButtonPressed(0.010d, LogicInputButton.InteractionPrimary);
        timeline.Seal(1, 1d / 30d);

        var resumedMovement = new FixVector2((Fix64)(-0.5f), (Fix64)0.25f);
        timeline.EnqueueResetGameplayState(0.040d);
        timeline.EnqueueWorldMove(0.040d, resumedMovement);
        timeline.EnqueueButtonHeldState(0.040d, LogicInputButton.InteractionPrimary, true);

        LogicInputFrame resumed = timeline.Seal(2, 2d / 30d);
        LogicInputFrame following = timeline.Seal(3, 3d / 30d);

        Assert.IsTrue(resumed.IsHeld(LogicInputButton.InteractionPrimary));
        Assert.IsTrue(resumed.WasReleased(LogicInputButton.InteractionPrimary));
        Assert.IsFalse(resumed.WasPressed(LogicInputButton.InteractionPrimary));
        Assert.AreEqual(0, resumed.GetPressCount(LogicInputButton.InteractionPrimary));
        Assert.AreEqual(resumedMovement, resumed.WorldMove);
        Assert.IsTrue(following.IsHeld(LogicInputButton.InteractionPrimary));
        Assert.IsFalse(following.WasPressed(LogicInputButton.InteractionPrimary));
        Assert.IsFalse(following.WasReleased(LogicInputButton.InteractionPrimary));
        Assert.AreEqual(resumedMovement, following.WorldMove);
    }

    [Test]
    public void LateEvent_IsAssignedToNextUnsealedFrame()
    {
        var timeline = CreateTimeline();
        timeline.Seal(1, 1d / 30d);
        timeline.EnqueueButtonPulse(0.020d, LogicInputButton.PlayerAttack);

        LogicInputFrame second = timeline.Seal(2, 2d / 30d);

        Assert.AreEqual(1ul, timeline.LateEventCount);
        Assert.IsTrue(second.WasPressed(LogicInputButton.PlayerAttack));
        Assert.AreEqual(1, second.GetPressCount(LogicInputButton.PlayerAttack));
    }

    [Test]
    public void RepeatedClockBoundary_DoesNotMoveExactTimestampToNextTick()
    {
        var timeline = CreateTimeline();
        timeline.EnqueueButtonPulse(0.2d, LogicInputButton.InteractionPrimary);
        double cutoff = 0d;

        for (ulong frame = 1; frame <= 6; frame++)
        {
            cutoff += LogicFrameClock.FrameDurationSeconds;
            LogicInputFrame inputFrame = timeline.Seal(frame, cutoff);
            Assert.AreEqual(frame == 6, inputFrame.WasPressed(LogicInputButton.InteractionPrimary));
        }

        Assert.AreEqual(0, timeline.PendingEventCount);
    }

    [Test]
    public void LongSession_RetainsOnlyCurrentSealedFrame()
    {
        const ulong frameCount = 100000;
        var timeline = CreateTimeline();

        for (ulong frame = 1; frame <= frameCount; frame++)
            timeline.Seal(frame, frame / 30d);

        Assert.AreEqual(frameCount, timeline.CurrentFrame.FrameId);
        Assert.AreEqual(1, timeline.RetainedSealedFrameCount);
    }

    [Test]
    public void EmptyTicks_ReuseImmutableEmptyEventPayload()
    {
        var timeline = CreateTimeline();

        LogicInputFrame first = timeline.Seal(1, 1d / 30d);
        LogicInputFrame second = timeline.Seal(2, 2d / 30d);

        Assert.AreSame(first.Events, second.Events);
        Assert.AreEqual(0, first.Events.Count);
        Assert.AreEqual(0, second.GetPressCount(LogicInputButton.InteractionPrimary));
    }

    [Test]
    public void ReusableSeal_ReusesFrameAndClearsPerTickEdges()
    {
        var timeline = CreateTimeline();
        timeline.EnqueueButtonPulse(0.010d, LogicInputButton.InteractionPrimary);

        LogicInputFrame first = timeline.SealReusable(1, 1d / 30d);
        Assert.IsTrue(first.WasPressed(LogicInputButton.InteractionPrimary));
        Assert.AreEqual(1, first.Events.Count);

        LogicInputFrame second = timeline.SealReusable(2, 2d / 30d);

        Assert.AreSame(first, second);
        Assert.IsFalse(second.WasPressed(LogicInputButton.InteractionPrimary));
        Assert.AreEqual(0, second.GetPressCount(LogicInputButton.InteractionPrimary));
        Assert.AreEqual(0, second.Events.Count);
    }

    [Test]
    public void ReusableFrame_FreezePreservesRecordedTick()
    {
        var timeline = CreateTimeline();
        timeline.EnqueueButtonPulse(0.010d, LogicInputButton.InteractionSecondary);

        LogicInputFrame runtimeFrame = timeline.SealReusable(1, 1d / 30d);
        LogicInputFrame frozen = runtimeFrame.Freeze();
        timeline.SealReusable(2, 2d / 30d);

        Assert.AreNotSame(runtimeFrame, frozen);
        Assert.AreEqual(1ul, frozen.FrameId);
        Assert.IsTrue(frozen.WasPressed(LogicInputButton.InteractionSecondary));
        Assert.AreEqual(1, frozen.GetPressCount(LogicInputButton.InteractionSecondary));
        Assert.AreEqual(1, frozen.Events.Count);
    }

    private static LogicInputTimeline CreateTimeline()
    {
        var timeline = new LogicInputTimeline();
        timeline.Begin(0d, FixVector2.Zero, 0);
        return timeline;
    }
}
