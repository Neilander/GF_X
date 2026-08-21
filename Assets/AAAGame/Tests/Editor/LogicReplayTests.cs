using NUnit.Framework;

public sealed class LogicReplayTests
{
    [SetUp]
    public void SetUp()
    {
        if (LogicReplayRuntime.IsRecording)
            LogicReplayRuntime.EndRecording();
        if (LogicObstacleCommandService.IsActive)
            LogicObstacleCommandService.EndTimeline();
        if (LogicEntityLifecycleService.IsActive)
            LogicEntityLifecycleService.EndTimeline();
        if (LogicCardCommandService.IsActive)
            LogicCardCommandService.EndTimeline();
        if (LogicSkillSlotCommandService.IsActive)
            LogicSkillSlotCommandService.EndTimeline();
        if (LogicSkillCastCommandService.IsActive)
            LogicSkillCastCommandService.EndTimeline();
        if (LogicInGameValueCommandService.IsActive)
            LogicInGameValueCommandService.EndTimeline();
        if (LogicInteractionCommandService.IsActive)
            LogicInteractionCommandService.EndTimeline();
        if (LogicTechEffectCommandService.IsActive)
            LogicTechEffectCommandService.EndTimeline();
        if (LogicPhaseCommandService.IsActive)
            LogicPhaseCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();

        LogicTimeControlService.BeginTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicReplayRuntime.IsRecording)
            LogicReplayRuntime.EndRecording();
        if (LogicObstacleCommandService.IsActive)
            LogicObstacleCommandService.EndTimeline();
        if (LogicEntityLifecycleService.IsActive)
            LogicEntityLifecycleService.EndTimeline();
        if (LogicCardCommandService.IsActive)
            LogicCardCommandService.EndTimeline();
        if (LogicSkillSlotCommandService.IsActive)
            LogicSkillSlotCommandService.EndTimeline();
        if (LogicSkillCastCommandService.IsActive)
            LogicSkillCastCommandService.EndTimeline();
        if (LogicInGameValueCommandService.IsActive)
            LogicInGameValueCommandService.EndTimeline();
        if (LogicInteractionCommandService.IsActive)
            LogicInteractionCommandService.EndTimeline();
        if (LogicTechEffectCommandService.IsActive)
            LogicTechEffectCommandService.EndTimeline();
        if (LogicPhaseCommandService.IsActive)
            LogicPhaseCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
    }

    [Test]
    public void Recorder_CapturesInputTimeCommandsPauseCommandsAndHashes()
    {
        var timeline = CreateTimeline();
        var recorder = new LogicReplayRecorder();
        recorder.Begin();

        LogicTimeControlService.SetBulletTimeScaleForLogicTicks(10, 2000, 15);
        LogicTimeControlService.BeginFrame(1);
        timeline.EnqueueButtonPulse(0.01d, LogicInputButton.InteractionPrimary);
        LogicInputFrame inputFrame = timeline.Seal(1, 1d / 30d);
        LogicReplayFrameRecord record = recorder.RecordFrame(inputFrame, 123ul);
        LogicTimeControlService.RemoveBulletTimeScale(10);
        LogicTimeControlService.AcquirePause(20);
        LogicTimeControlService.ReleasePause(20);
        LogicReplayLog log = recorder.End();

        Assert.AreEqual(1, log.Frames.Count);
        Assert.AreEqual(2, log.TimeScaleCommands.Count);
        Assert.AreEqual(15ul, log.TimeScaleCommands[0].DurationTicks);
        Assert.AreEqual(TimeScaleCommandKind.RemoveBulletTimeScale, log.TimeScaleCommands[1].Kind);
        Assert.AreEqual(0ul, log.TimeScaleCommands[1].DurationTicks);
        Assert.AreEqual(2, log.PauseControlCommands.Count);
        Assert.AreEqual(LogicReplayLog.CurrentProtocolVersion, log.ProtocolVersion);
        Assert.AreEqual(LogicReplayLog.CurrentContentVersion, log.ContentVersion);
        Assert.AreEqual(30, log.LogicFrameRate);
        Assert.AreEqual(0ul, log.InitialRandomSeed);
        Assert.AreEqual(LogicStateHasher.ComputeInputHash(inputFrame), record.InputHash);
        Assert.AreNotEqual(0ul, record.TimeControlHash);
        Assert.AreNotEqual(0ul, record.FullHash);

        var replayInput = new LogicReplayInputSource(log);
        Assert.AreSame(inputFrame, replayInput.ReadFrame(1));
        Assert.Throws<System.InvalidOperationException>(() => replayInput.ReadFrame(2));
    }

    [Test]
    public void InputHash_IgnoresRealtimeTimestampAfterEventsAreSealed()
    {
        LogicInputFrame early = CreatePulseFrame(0.01d);
        LogicInputFrame late = CreatePulseFrame(0.03d);

        Assert.AreEqual(
            LogicStateHasher.ComputeInputHash(early),
            LogicStateHasher.ComputeInputHash(late));
    }

    [Test]
    public void InputHash_TracksWorldMove()
    {
        LogicInputFrame first = CreateWorldMoveFrame(
            new FixVector2(Fix64.FromRaw(1001), Fix64.FromRaw(2002)));
        LogicInputFrame second = CreateWorldMoveFrame(
            new FixVector2(Fix64.FromRaw(1001), Fix64.FromRaw(2003)));

        Assert.AreNotEqual(
            LogicStateHasher.ComputeInputHash(first),
            LogicStateHasher.ComputeInputHash(second));
    }

    [Test]
    public void Comparer_ReportsFirstGameplayHashDivergence()
    {
        LogicReplayLog expected = RecordSingleFrame(100ul);

        LogicTimeControlService.EndTimeline();
        LogicTimeControlService.BeginTimeline();
        LogicReplayLog actual = RecordSingleFrame(200ul);

        LogicReplayDivergence divergence = LogicReplayComparer.FindFirstDivergence(expected, actual);

        Assert.IsTrue(divergence.HasDivergence);
        Assert.AreEqual(1ul, divergence.FrameId);
        Assert.AreEqual("GameplayStateHash", divergence.Field);
    }

    [Test]
    public void Comparer_ReportsSkillCastWorldPositionDivergence()
    {
        LogicReplayLog expected = RecordSingleSkillCastCommand(
            new FixVector2(Fix64.FromRaw(10), Fix64.FromRaw(20)));
        LogicReplayLog actual = RecordSingleSkillCastCommand(
            new FixVector2(Fix64.FromRaw(10), Fix64.FromRaw(21)));

        LogicReplayDivergence divergence = LogicReplayComparer.FindFirstDivergence(expected, actual);

        Assert.IsTrue(divergence.HasDivergence);
        Assert.AreEqual(1ul, divergence.FrameId);
        Assert.AreEqual("SkillCastCommand", divergence.Field);
    }

    [Test]
    public void Comparer_ReportsFirstNavigationAuthorityCheckpointDivergence()
    {
        LogicGameplayStateDigest expectedDigest = CreateGameplayDigest(5ul, 13ul, 15ul);
        LogicReplayLog expected = RecordSingleFrame(expectedDigest);

        LogicTimeControlService.EndTimeline();
        LogicTimeControlService.BeginTimeline();
        LogicGameplayStateDigest actualDigest = CreateGameplayDigest(99ul, 113ul, 115ul);
        LogicReplayLog actual = RecordSingleFrame(actualDigest);

        LogicReplayDivergence divergence = LogicReplayComparer.FindFirstDivergence(expected, actual);

        Assert.IsTrue(divergence.HasDivergence);
        Assert.AreEqual(1ul, divergence.FrameId);
        Assert.AreEqual("Gameplay.Navigation.Agents", divergence.Field);
    }

    [Test]
    public void SnapshotHash_IsIndependentOfCommandSubmissionInsertionLayout()
    {
        LogicTimeControlService.SubmitTimeScaleCommand(new TimeScaleCommand(
            2,
            1,
            TimeScaleCommandKind.SetBulletTimeScale,
            20,
            5000));
        LogicTimeControlService.SubmitTimeScaleCommand(new TimeScaleCommand(
            1,
            2,
            TimeScaleCommandKind.SetBulletTimeScale,
            10,
            2000));
        LogicTimeControlSnapshot snapshot = LogicTimeControlService.CaptureSnapshot();

        Assert.AreEqual(1ul, snapshot.PendingTimeScaleCommands[0].EffectiveFrame);
        Assert.AreEqual(2ul, snapshot.PendingTimeScaleCommands[1].EffectiveFrame);
        Assert.AreNotEqual(0ul, LogicStateHasher.ComputeTimeControlHash(snapshot));
    }

    [Test]
    public void TimeScaleCommandHash_TracksLogicTickDuration()
    {
        var shortCommand = new TimeScaleCommand(
            1,
            1,
            TimeScaleCommandKind.SetBulletTimeScaleForLogicTicks,
            10,
            2000,
            5);
        var longCommand = new TimeScaleCommand(
            1,
            1,
            TimeScaleCommandKind.SetBulletTimeScaleForLogicTicks,
            10,
            2000,
            6);

        Assert.AreNotEqual(
            LogicStateHasher.ComputeTimeScaleCommandHash(shortCommand),
            LogicStateHasher.ComputeTimeScaleCommandHash(longCommand));
    }

    [Test]
    public void ProtocolV100_CrossPlatformDeterminismCorpus_IsStable()
    {
        LogicTimeControlService.EndTimeline();
        LogicDeterminismCorpusResult result = LogicDeterminismCorpus.ValidateV100();
        LogicTimeControlService.BeginTimeline();

        Assert.AreEqual(100, result.ProtocolVersion);
        Assert.AreEqual(LogicDeterminismCorpus.GoldenFullHash, result.FullHash);
    }

    [Test]
    public void CrossPlatformDeterminismCorpus_RejectsActiveTimeline()
    {
        Assert.Throws<System.InvalidOperationException>(() => LogicDeterminismCorpus.EvaluateV100());
    }

    [Test]
    public void ReplayCompatibility_RejectsV99ProtocolAndContent()
    {
        Assert.Throws<System.InvalidOperationException>(() =>
            LogicReplayLog.RequireCurrentVersion(99, LogicReplayLog.CurrentContentVersion));
        Assert.Throws<System.InvalidOperationException>(() =>
            LogicReplayLog.RequireCurrentVersion(
                LogicReplayLog.CurrentProtocolVersion,
                "Avenge-30Hz-v99"));
    }

    [Test]
    public void Recorder_CapturesInitialPhaseTechLifecycleAndObstacleCommandHistory()
    {
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        LogicInteractionCommandService.BeginTimeline();
        LogicCardCommandService.BeginTimeline();
        LogicSkillSlotCommandService.BeginTimeline();
        LogicSkillCastCommandService.BeginTimeline();
        LogicInGameValueCommandService.BeginTimeline();
        LogicTechEffectCommandService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        LogicObstacleCommandService.BeginTimeline();
        LogicPhaseCommand phaseCommand = LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.BuildBeforeInvade);
        LogicTechEffectCommand techCommand = LogicTechEffectCommandService.ScheduleForNextFrame("Tech_Test", false, 0, "building-1");
        LogicInteractionCommand interactionCommand = LogicInteractionCommandService.ScheduleForNextFrame(
            LogicInteractionActionKind.UpgradeBuilding,
            new LogicEntityId(10),
            "building-1",
            "Building_Test_Lv2",
            "Tech_Test");
        LogicCardCommand cardCommand = LogicCardCommandService.SchedulePlayForNextFrame(
            17,
            new FixVector2(Fix64.FromRaw(321), Fix64.FromRaw(-654)));
        LogicSkillSlotCommand skillSlotCommand = LogicSkillSlotCommandService.ScheduleForNextFrame(0, 1);
        LogicSkillCastCommand skillCastCommand = LogicSkillCastCommandService.ScheduleForNextFrame(
            new LogicEntityId(55),
            2,
            new FixVector2(Fix64.FromRaw(765), Fix64.FromRaw(-432)));
        LogicInGameValueCommand valueCommand = LogicInGameValueCommandService.ScheduleDeltaForNextFrame(IngameValueType.Coin, 3);
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn();
        LogicEntityLifecycleService.BindView(entityId, 404);
        LogicObstacleCommandService.ScheduleBoxForNextFrame(
            101,
            new FixVector2((Fix64)2, (Fix64)3),
            new FixVector2((Fix64)1, (Fix64)1));

        var recorder = new LogicReplayRecorder();
        recorder.Begin();
        LogicEntityLifecycleService.UnbindView(entityId, 404);
        LogicReplayLog log = recorder.End();

        Assert.AreEqual(1, log.PhaseCommands.Count);
        Assert.AreEqual(phaseCommand.Sequence, log.PhaseCommands[0].Sequence);
        Assert.AreEqual(GamePhase.BuildBeforeInvade, log.PhaseCommands[0].Phase);
        Assert.AreEqual(1, log.TechEffectCommands.Count);
        Assert.AreEqual(techCommand.Sequence, log.TechEffectCommands[0].Sequence);
        Assert.AreEqual("Tech_Test", log.TechEffectCommands[0].TechId);
        Assert.AreEqual(1, log.InteractionCommands.Count);
        Assert.AreEqual(interactionCommand.Sequence, log.InteractionCommands[0].Sequence);
        Assert.AreEqual(LogicInteractionActionKind.UpgradeBuilding, log.InteractionCommands[0].ActionKind);
        Assert.AreEqual(1, log.CardCommands.Count);
        Assert.AreEqual(cardCommand.Sequence, log.CardCommands[0].Sequence);
        Assert.AreEqual(321L, log.CardCommands[0].SelectedPosition.x.RawValue);
        Assert.AreEqual(-654L, log.CardCommands[0].SelectedPosition.y.RawValue);
        Assert.AreEqual(1, log.SkillSlotCommands.Count);
        Assert.AreEqual(skillSlotCommand.Sequence, log.SkillSlotCommands[0].Sequence);
        Assert.AreEqual(0, log.SkillSlotCommands[0].FromIndex);
        Assert.AreEqual(1, log.SkillSlotCommands[0].ToIndex);
        Assert.AreEqual(1, log.SkillCastCommands.Count);
        Assert.AreEqual(skillCastCommand.Sequence, log.SkillCastCommands[0].Sequence);
        Assert.AreEqual(new LogicEntityId(55), log.SkillCastCommands[0].CasterId);
        Assert.AreEqual(2, log.SkillCastCommands[0].SlotIndex);
        Assert.AreEqual(765L, log.SkillCastCommands[0].RequestedWorldPosition.x.RawValue);
        Assert.AreEqual(-432L, log.SkillCastCommands[0].RequestedWorldPosition.y.RawValue);
        Assert.AreEqual(1, log.InGameValueCommands.Count);
        Assert.AreEqual(valueCommand.Sequence, log.InGameValueCommands[0].Sequence);
        Assert.AreEqual(IngameValueType.Coin, log.InGameValueCommands[0].ValueType);
        Assert.AreEqual(3, log.InGameValueCommands[0].Delta);
        Assert.AreEqual(1, log.LifecycleCommands.Count);
        Assert.AreEqual(entityId, log.LifecycleCommands[0].EntityId);
        Assert.AreEqual(LogicEntityLifecycleCommandKind.SpawnRequested, log.LifecycleCommands[0].Kind);
        Assert.AreEqual(1, log.ObstacleCommands.Count);
        Assert.AreEqual(101, log.ObstacleCommands[0].StableObstacleId);
    }

    [Test]
    public void Comparer_ReportsSkillSlotCommandDivergence()
    {
        LogicReplayLog expected = RecordSingleSkillSlotCommand(1);
        LogicReplayLog actual = RecordSingleSkillSlotCommand(2);

        LogicReplayDivergence divergence = LogicReplayComparer.FindFirstDivergence(expected, actual);

        Assert.IsTrue(divergence.HasDivergence);
        Assert.AreEqual(1UL, divergence.FrameId);
        Assert.AreEqual("SkillSlotCommand", divergence.Field);
    }

    [Test]
    public void Comparer_ReportsInGameValueCommandDivergence()
    {
        LogicReplayLog expected = RecordSingleInGameValueCommand(1);
        LogicReplayLog actual = RecordSingleInGameValueCommand(2);

        LogicReplayDivergence divergence = LogicReplayComparer.FindFirstDivergence(expected, actual);

        Assert.IsTrue(divergence.HasDivergence);
        Assert.AreEqual(1UL, divergence.FrameId);
        Assert.AreEqual("InGameValueCommand", divergence.Field);
    }

    private static LogicReplayLog RecordSingleFrame(ulong gameplayStateHash)
    {
        var recorder = new LogicReplayRecorder();
        recorder.Begin();
        LogicTimeControlService.BeginFrame(1);

        var timeline = CreateTimeline();
        LogicInputFrame inputFrame = timeline.Seal(1, 1d / 30d);
        recorder.RecordFrame(inputFrame, gameplayStateHash);
        return recorder.End();
    }

    private static LogicReplayLog RecordSingleFrame(LogicGameplayStateDigest gameplayDigest)
    {
        var recorder = new LogicReplayRecorder();
        recorder.Begin();
        LogicTimeControlService.BeginFrame(1);

        var timeline = CreateTimeline();
        LogicInputFrame inputFrame = timeline.Seal(1, 1d / 30d);
        recorder.RecordFrame(inputFrame, gameplayDigest);
        return recorder.End();
    }

    private static LogicGameplayStateDigest CreateGameplayDigest(
        ulong navigationAgentsHash,
        ulong navigationFinalHash,
        ulong gameplayStateHash)
    {
        var navigation = new LogicNavigationAuthorityDigest(
            1ul,
            2ul,
            3ul,
            4ul,
            navigationAgentsHash,
            navigationAgentsHash + 1,
            navigationAgentsHash + 2,
            navigationAgentsHash + 3,
            navigationAgentsHash + 4,
            navigationAgentsHash + 5,
            navigationAgentsHash + 6,
            navigationAgentsHash + 7,
            navigationFinalHash);
        return new LogicGameplayStateDigest(
            1ul,
            1ul,
            2ul,
            3ul,
            4ul,
            5ul,
            6ul,
            7ul,
            8ul,
            navigation,
            gameplayStateHash - 1,
            gameplayStateHash);
    }

    private static LogicReplayLog RecordSingleSkillCastCommand(FixVector2 worldPosition)
    {
        LogicSkillCastCommandService.BeginTimeline();
        var recorder = new LogicReplayRecorder();
        recorder.Begin();
        LogicSkillCastCommandService.ScheduleForNextFrame(new LogicEntityId(90), 1, worldPosition);
        LogicReplayLog log = recorder.End();
        LogicSkillCastCommandService.EndTimeline();
        return log;
    }

    private static LogicReplayLog RecordSingleSkillSlotCommand(int toIndex)
    {
        LogicSkillSlotCommandService.BeginTimeline();
        var recorder = new LogicReplayRecorder();
        recorder.Begin();
        LogicSkillSlotCommandService.ScheduleForNextFrame(0, toIndex);
        LogicReplayLog log = recorder.End();
        LogicSkillSlotCommandService.EndTimeline();
        return log;
    }

    private static LogicReplayLog RecordSingleInGameValueCommand(int delta)
    {
        LogicInGameValueCommandService.BeginTimeline();
        var recorder = new LogicReplayRecorder();
        recorder.Begin();
        LogicInGameValueCommandService.ScheduleDeltaForNextFrame(IngameValueType.Coin, delta);
        LogicReplayLog log = recorder.End();
        LogicInGameValueCommandService.EndTimeline();
        return log;
    }

    private static LogicInputFrame CreatePulseFrame(double timestamp)
    {
        var timeline = CreateTimeline();
        timeline.EnqueueButtonPulse(timestamp, LogicInputButton.InteractionSecondary);
        return timeline.Seal(1, 1d / 30d);
    }

    private static LogicInputFrame CreateWorldMoveFrame(FixVector2 worldMove)
    {
        var timeline = CreateTimeline();
        timeline.EnqueueWorldMove(0.01d, worldMove);
        return timeline.Seal(1, 1d / 30d);
    }

    private static LogicInputTimeline CreateTimeline()
    {
        var timeline = new LogicInputTimeline();
        timeline.Begin(0d, FixVector2.Zero, 0);
        return timeline;
    }
}
