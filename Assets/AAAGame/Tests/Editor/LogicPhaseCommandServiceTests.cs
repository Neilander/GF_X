using System;
using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public sealed class LogicPhaseCommandServiceTests
{
    [SetUp]
    public void SetUp()
    {
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicPhaseCommandService.IsActive)
            LogicPhaseCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
    }

    [Test]
    public void Commands_ApplyOnExactFrameInSequenceOrder()
    {
        LogicPhaseCommand first = LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.BuildBeforeInvade);
        LogicPhaseCommand second = LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.Invade);
        var applied = new List<LogicPhaseCommand>();
        var transitions = new List<(GamePhase OldPhase, GamePhase NewPhase)>();
        LogicPhaseCommandService.PhaseApplied += RecordTransition;
        try
        {
            Assert.AreEqual(1UL, first.EffectiveFrame);
            Assert.AreEqual(1UL, second.EffectiveFrame);
            Assert.AreEqual(1UL, first.Sequence);
            Assert.AreEqual(2UL, second.Sequence);

            LogicTimeControlService.BeginFrame(1);
            LogicPhaseCommandService.ApplyFrameForTests(1, applied.Add);

            CollectionAssert.AreEqual(new[] { first.Sequence, second.Sequence }, new[] { applied[0].Sequence, applied[1].Sequence });
            Assert.AreEqual(GamePhase.Invade, LogicPhaseCommandService.CurrentPhase);
            Assert.AreEqual(0, LogicPhaseCommandService.PendingCount);
            CollectionAssert.AreEqual(
                new[]
                {
                    (GamePhase.Defend, GamePhase.BuildBeforeInvade),
                    (GamePhase.BuildBeforeInvade, GamePhase.Invade),
                },
                transitions);
        }
        finally
        {
            LogicPhaseCommandService.PhaseApplied -= RecordTransition;
        }

        void RecordTransition(GamePhase oldPhase, GamePhase newPhase)
        {
            transitions.Add((oldPhase, newPhase));
        }
    }

    [Test]
    public void ApplyFrame_RejectsMissedCommandFrame()
    {
        LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.Invade);
        LogicTimeControlService.BeginFrame(1);
        LogicTimeControlService.BeginFrame(2);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => LogicPhaseCommandService.ApplyFrameForTests(2, _ => { }));

        StringAssert.Contains("missed its frame", exception.Message);
    }

    [Test]
    public void EditorGateCommand_AppliesOnExplicitFutureFrame()
    {
        LogicPhaseCommand command = LogicPhaseCommandService.ScheduleForEditorGate(GamePhase.Invade, 3);
        var applied = new List<LogicPhaseCommand>();

        LogicTimeControlService.BeginFrame(1);
        LogicPhaseCommandService.ApplyFrameForTests(1, applied.Add);
        LogicTimeControlService.BeginFrame(2);
        LogicPhaseCommandService.ApplyFrameForTests(2, applied.Add);
        Assert.IsEmpty(applied);
        Assert.AreEqual(1, LogicPhaseCommandService.PendingCount);

        LogicTimeControlService.BeginFrame(3);
        LogicPhaseCommandService.ApplyFrameForTests(3, applied.Add);

        CollectionAssert.AreEqual(new[] { command.Sequence }, new[] { applied[0].Sequence });
        Assert.AreEqual(0, LogicPhaseCommandService.PendingCount);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LogicPhaseCommandService.ScheduleForEditorGate(GamePhase.Defend, 3));
    }

    [Test]
    public void WorldTransition_RequiresNewInitialPhaseBeforeFramesResume()
    {
        const int pauseSource = 701;
        LogicTimeControlService.AcquirePause(pauseSource);
        LogicPhaseCommandService.ResetForWorldTransition();

        Assert.IsFalse(LogicPhaseCommandService.IsInitialized);
        Assert.Throws<InvalidOperationException>(
            () => LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.Invade));

        LogicPhaseCommandService.SetInitialPhase(GamePhase.BuildBeforeDefend);
        Assert.IsFalse(LogicPhaseCommandService.IsWorldTransitionActive);
        LogicTimeControlService.ResetFrameTimelinePreservingPauses();
        LogicPhaseCommandService.ResetFrameTimeline();
        Assert.AreEqual(GamePhase.BuildBeforeDefend, LogicPhaseCommandService.CurrentPhase);
        LogicTimeControlService.ReleasePause(pauseSource);
    }
}
