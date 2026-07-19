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

    private static LogicInputTimeline CreateTimeline()
    {
        var timeline = new LogicInputTimeline();
        timeline.Begin(0d, FixVector2.Zero, 0, FixVector2.Zero);
        return timeline;
    }
}
