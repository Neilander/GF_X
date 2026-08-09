using System;
using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public sealed class LogicPhaseCommandServiceTests
{
    [SetUp]
    public void SetUp()
    {
        LogicTestInGameDataModelAuthority.Ensure(GamePhase.Defend, nameof(LogicPhaseCommandServiceTests));
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
    public void SwitchToNextPhase_RejectsInvalidCurrentPhase()
    {
        System.Reflection.PropertyInfo currentPhaseProperty = typeof(LogicPhaseCommandService).GetProperty(
            nameof(LogicPhaseCommandService.CurrentPhase),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        Assert.NotNull(currentPhaseProperty);
        currentPhaseProperty.SetValue(null, (GamePhase)int.MaxValue);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            PhaseManager.SwitchToNextPhase);

        StringAssert.Contains("unsupported current phase", exception.Message);
    }

    [Test]
    public void Commands_ApplyOnExactFrameInSequenceOrder()
    {
        LogicPhaseCommand first = LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.BuildBeforeInvade);
        LogicPhaseCommand second = LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.Invade);
        var applied = new List<LogicPhaseCommand>();
        var phasesSeenBySink = new List<GamePhase>();
        var transitions = new List<(GamePhase OldPhase, GamePhase NewPhase)>();
        LogicPhaseCommandService.PhaseApplied += RecordTransition;
        try
        {
            Assert.AreEqual(1UL, first.EffectiveFrame);
            Assert.AreEqual(1UL, second.EffectiveFrame);
            Assert.AreEqual(1UL, first.Sequence);
            Assert.AreEqual(2UL, second.Sequence);

            LogicTimeControlService.BeginFrame(1);
            LogicPhaseCommandService.ApplyFrameForTests(
                1,
                command =>
                {
                    applied.Add(command);
                    phasesSeenBySink.Add(LogicPhaseCommandService.GetRequiredCurrentPhase());
                });

            CollectionAssert.AreEqual(new[] { first.Sequence, second.Sequence }, new[] { applied[0].Sequence, applied[1].Sequence });
            CollectionAssert.AreEqual(
                new[] { GamePhase.BuildBeforeInvade, GamePhase.Invade },
                phasesSeenBySink);
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
    public void SubmitWhileInGameUiPaused_AppliesInCurrentFrameWithoutRemainingPending()
    {
        var applied = new List<LogicPhaseCommand>();
        LogicTimeControlService.BeginFrame(1);
        LogicPhaseCommandService.ApplyFrameForTests(1, applied.Add);
        LogicTimeControlService.AcquirePause(LogicTimeControlSources.InGameUiPause);

        LogicPhaseCommand command = LogicPhaseCommandService.SubmitForTests(
            GamePhase.BuildBeforeInvade,
            applied.Add);

        Assert.AreEqual(1UL, LogicTimeControlService.CurrentFrame);
        Assert.AreEqual(1UL, command.EffectiveFrame);
        Assert.AreEqual(GamePhase.BuildBeforeInvade, LogicPhaseCommandService.GetRequiredCurrentPhase());
        Assert.AreEqual(1, applied.Count);
        Assert.AreEqual(0, LogicPhaseCommandService.PendingCount);

        LogicTimeControlService.ReleasePause(LogicTimeControlSources.InGameUiPause);
        LogicTimeControlService.BeginFrame(2);
        LogicPhaseCommandService.ApplyFrameForTests(2, applied.Add);
        Assert.AreEqual(1, applied.Count);
    }

    [Test]
    public void ApplyFrame_RollsBackAuthorityWhenPhaseEffectsFail()
    {
        LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.Invade);
        LogicTimeControlService.BeginFrame(1);

        Assert.Throws<InvalidOperationException>(() =>
            LogicPhaseCommandService.ApplyFrameForTests(
                1,
                _ => throw new InvalidOperationException("phase effect failed")));

        Assert.AreEqual(GamePhase.Defend, LogicPhaseCommandService.GetRequiredCurrentPhase());
        Assert.AreEqual(1, LogicPhaseCommandService.PendingCount);
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

    [Test]
    public void BuildPhaseRewardMutation_RejectsOutsideApplyWindow()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RewardManager.HandleEnterBuildPhaseReward(false, GamePhase.Defend));
    }

    [Test]
    public void PhaseAudioAndCardUiAreDeferredToRenderUpdates()
    {
        string phaseSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            UnityEngine.Application.dataPath,
            "AAAGame/Scripts/GameClass/PhaseManager.cs"));
        string cardSetupSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            UnityEngine.Application.dataPath,
            "AAAGame/Scripts/UTManagers/CardSetup.cs"));

        StringAssert.Contains("s_PendingPhaseSounds.Enqueue", phaseSource);
        StringAssert.DoesNotContain("PlayPhaseEnterSoundWhenUnpausedAsync", phaseSource);
        StringAssert.Contains("private void Update()", phaseSource);
        StringAssert.DoesNotContain("GameObject.FindObjectsOfType<EntityPresetPoint>", phaseSource);
        StringAssert.Contains("s_InvadeSpawnPointsConfigured", phaseSource);
        StringAssert.Contains("m_PendingUiPresentation.Enqueue", cardSetupSource);
        StringAssert.Contains("LogicFrameRuntime.IsExecutingFrame", cardSetupSource);
        StringAssert.Contains("OpenCardUIImmediate", cardSetupSource);
    }

    [Test]
    public void PhaseApplyPath_DoesNotDiscoverUnityRuntimeDependencies()
    {
        string scriptsRoot = System.IO.Path.Combine(UnityEngine.Application.dataPath, "AAAGame/Scripts");
        string runtimeProcedureSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            scriptsRoot,
            "Procedures/RuntimeProcedureBase.cs"));
        string phaseSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            scriptsRoot,
            "GameClass/PhaseManager.cs"));
        string defendSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            scriptsRoot,
            "GameClass/DefendPhaseRuntime.cs"));
        string checkpointSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            scriptsRoot,
            "GameClass/StageCheckpointService.cs"));
        string dataModelSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            scriptsRoot,
            "DataModel/InGameDataModel.cs"));
        string generalSetupSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            scriptsRoot,
            "UTManagers/GeneralSetup.cs"));
        string phaseGuardSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            scriptsRoot,
            "Buff/BuildingPhaseGuardBuff.cs"));
        string cardControllerSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            scriptsRoot,
            "Card/Controller/CardSystemController.cs"));
        string tutorialSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            scriptsRoot,
            "MeiyouUtility/TutorialManager.cs"));

        string executeLogicFrame = ExtractMethod(runtimeProcedureSource, "private static LogicGameplayStateDigest ExecuteLogicFrame", "#if UNITY_EDITOR");
        StringAssert.DoesNotContain("GameEntry.GetComponent", executeLogicFrame);

        string enterDefendPhase = ExtractMethod(defendSource, "public static void EnterDefendPhase()", "public static void ApplyScheduledSpawnRequests");
        StringAssert.DoesNotContain("EnsureSpawnPointCache", enterDefendPhase);
        StringAssert.DoesNotContain("EnsureWaveConfigLoaded", enterDefendPhase);
        StringAssert.Contains("RequirePreparedRuntime", enterDefendPhase);

        string persistentCommit = ExtractMethod(checkpointSource, "private static void OnPersistentStageCommitted", "private static Fog3MapData BindFogMap");
        StringAssert.DoesNotContain("BindFogMap", persistentCommit);
        StringAssert.Contains("RequireBoundFogMap", persistentCommit);

        string navigationBarrier = ExtractMethod(phaseSource, "private static void CompleteNavigationForBattlePhase", "private static void PrepareBattlePhaseCards");
        StringAssert.DoesNotContain("GroupMoveManager.Instance", navigationBarrier);
        StringAssert.Contains("FlowFieldCrowdMovementSystem.CompleteRuntimeRebuildQueue", navigationBarrier);

        string setValue = ExtractMethod(dataModelSource, "public static void SetValue", "public static bool TryModifyValue");
        StringAssert.DoesNotContain("GF.Event.Fire", setValue);
        StringAssert.Contains("QueueValuePresentation", setValue);

        int initializePhase = generalSetupSource.IndexOf(
            "PhaseManager.InitializePhaseAuthorityOnGameStart(initialPhase)",
            StringComparison.Ordinal);
        int showLevel = generalSetupSource.IndexOf(
            "LevelEntityFactory.ShowLevel(lvRow.PrefabPath)",
            StringComparison.Ordinal);
        int commitEntities = generalSetupSource.IndexOf(
            "LogicEntityLifecycleService.CommitPendingInitializationEntities()",
            StringComparison.Ordinal);
        Assert.That(initializePhase, Is.GreaterThanOrEqualTo(0));
        Assert.That(showLevel, Is.GreaterThan(initializePhase));
        Assert.That(commitEntities, Is.GreaterThan(initializePhase));
        StringAssert.DoesNotContain("PhaseManager.CurrentPhase", phaseGuardSource);

        string cardDiscard = ExtractMethod(cardControllerSource, "private void ApplyDiscardCommand", "private void ApplyDiscardResourceReward");
        StringAssert.DoesNotContain("GF.Event.Fire", cardDiscard);
        string tutorialTick = ExtractMethod(tutorialSource, "public void OnLogicFrameUpdate", "public bool NotifyTriggerEntered");
        StringAssert.DoesNotContain("GF.DataModel", tutorialTick);
        StringAssert.DoesNotContain("SideTipsManager", tutorialTick);
    }

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.GreaterOrEqual(start, 0, $"Missing source marker '{startMarker}'.");
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.Greater(end, start, $"Missing source marker '{endMarker}' after '{startMarker}'.");
        return source.Substring(start, end - start);
    }
}
