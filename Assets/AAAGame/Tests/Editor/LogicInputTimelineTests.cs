using NUnit.Framework;

public sealed class LogicInputTimelineTests
{
    [Test]
    public void NoTickBetweenPressAndRelease_PreservesBothEdges()
    {
        var timeline = CreateTimeline();
        timeline.EnqueueButtonPressed(0.010d, LogicInputButton.Skill1);
        timeline.EnqueueButtonReleased(0.020d, LogicInputButton.Skill1);

        LogicInputFrame frame = timeline.Seal(1, 1d / 30d);

        Assert.IsTrue(frame.WasPressed(LogicInputButton.Skill1));
        Assert.IsTrue(frame.WasReleased(LogicInputButton.Skill1));
        Assert.IsFalse(frame.IsHeld(LogicInputButton.Skill1));
        Assert.AreEqual(1, frame.GetPressCount(LogicInputButton.Skill1));
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
        timeline.EnqueueButtonPressed(0.010d, LogicInputButton.Skill2);
        timeline.EnqueueButtonReleased(0.010d, LogicInputButton.Skill2);
        timeline.EnqueueButtonPressed(0.010d, LogicInputButton.Skill2);

        LogicInputFrame frame = timeline.Seal(1, 1d / 30d);

        Assert.AreEqual(3, frame.Events.Count);
        Assert.AreEqual(RawInputEventKind.ButtonPressed, frame.Events[0].Kind);
        Assert.AreEqual(RawInputEventKind.ButtonReleased, frame.Events[1].Kind);
        Assert.AreEqual(RawInputEventKind.ButtonPressed, frame.Events[2].Kind);
        Assert.Less(frame.Events[0].Sequence, frame.Events[1].Sequence);
        Assert.Less(frame.Events[1].Sequence, frame.Events[2].Sequence);
        Assert.AreEqual(2, frame.GetPressCount(LogicInputButton.Skill2));
        Assert.IsTrue(frame.WasPressed(LogicInputButton.Skill2));
        Assert.IsTrue(frame.WasReleased(LogicInputButton.Skill2));
        Assert.IsTrue(frame.IsHeld(LogicInputButton.Skill2));
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
    public void LateEvent_IsAssignedToNextUnsealedFrame()
    {
        var timeline = CreateTimeline();
        timeline.Seal(1, 1d / 30d);
        timeline.EnqueueButtonPulse(0.020d, LogicInputButton.SkillConfirm);

        LogicInputFrame second = timeline.Seal(2, 2d / 30d);

        Assert.AreEqual(1ul, timeline.LateEventCount);
        Assert.IsTrue(second.WasPressed(LogicInputButton.SkillConfirm));
        Assert.AreEqual(1, second.GetPressCount(LogicInputButton.SkillConfirm));
    }

    [Test]
    public void WorldSelection_IsSealedOnExactTickAndCarriesForward()
    {
        var timeline = CreateTimeline();
        var firstPosition = new FixVector2(Fix64.FromRaw(123), Fix64.FromRaw(-456));
        var secondPosition = new FixVector2(Fix64.FromRaw(789), Fix64.FromRaw(321));
        timeline.EnqueueSelectWorldPosition(1d / 30d, firstPosition);
        timeline.EnqueueSelectWorldPosition(1d / 30d + 0.000001d, secondPosition);

        LogicInputFrame first = timeline.Seal(1, 1d / 30d);
        LogicInputFrame second = timeline.Seal(2, 2d / 30d);
        LogicInputFrame third = timeline.Seal(3, 3d / 30d);

        Assert.IsTrue(first.HasSelectWorldPosition);
        Assert.AreEqual(firstPosition, first.SelectWorldPosition);
        Assert.AreEqual(1, first.Events.Count);
        Assert.AreEqual(RawInputEventKind.SelectWorldPositionChanged, first.Events[0].Kind);
        Assert.AreEqual(secondPosition, second.SelectWorldPosition);
        Assert.AreEqual(1, second.Events.Count);
        Assert.AreEqual(secondPosition, third.SelectWorldPosition);
        Assert.AreEqual(0, third.Events.Count);
    }

    [Test]
    public void InitialWorldSelection_IsAvailableWithoutSyntheticEvent()
    {
        var initialPosition = new FixVector2(Fix64.FromRaw(101), Fix64.FromRaw(202));
        var timeline = new LogicInputTimeline();
        timeline.Begin(0d, FixVector2.Zero, 0, FixVector2.Zero, true, initialPosition);

        LogicInputFrame frame = timeline.Seal(1, 1d / 30d);

        Assert.IsTrue(frame.HasSelectWorldPosition);
        Assert.AreEqual(initialPosition, frame.SelectWorldPosition);
        Assert.AreEqual(0, frame.Events.Count);
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
        Assert.AreEqual(0, second.GetPressCount(LogicInputButton.Skill1));
    }

    private static LogicInputTimeline CreateTimeline()
    {
        var timeline = new LogicInputTimeline();
        timeline.Begin(0d, FixVector2.Zero, 0, FixVector2.Zero, false, FixVector2.Zero);
        return timeline;
    }
}
