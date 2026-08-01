using System;
using System.Globalization;
using System.IO;
using GameFramework;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityGameFramework.Runtime;

[InitializeOnLoad]
internal static class Lv3MainPerfReproductionRunner
{
    private const string LaunchScenePath = "Assets/AAAGame/Scene/Launch.unity";
    private const string ResultRelativePath = "Logs/Lv3MainPerfReproductionResult.txt";
    private const string AllocationResultRelativePath = "Logs/Lv3MainPerfAllocationDiagnostic.txt";
    private const string SessionPrefix = "Avenge.Lv3MainPerfReproduction.";
    private const string RunningKey = SessionPrefix + "Running";
    private const string StateKey = SessionPrefix + "State";
    private const string StartedUtcKey = SessionPrefix + "StartedUtc";
    private const string NextMoveFrameKey = SessionPrefix + "NextMoveFrame";
    private const string DirectionIndexKey = SessionPrefix + "DirectionIndex";
    private const string CaptureGcKey = SessionPrefix + "CaptureGc";
    private const string CaptureStartedKey = SessionPrefix + "CaptureStarted";
    private const string CaptureStartFrameKey = SessionPrefix + "CaptureStartFrame";
    private const string CaptureEndFrameKey = SessionPrefix + "CaptureEndFrame";
    private const string CaptureGcBaselineKey = SessionPrefix + "CaptureGcBaseline";
    private const string CaptureGcFinalKey = SessionPrefix + "CaptureGcFinal";
    private const string CaptureProfilerLastFrameKey = SessionPrefix + "CaptureProfilerLastFrame";
    private const ulong InvadeFrame = 600;
    private const ulong GcCaptureStartFrame = 900;
    private const ulong GcCaptureDurationFrames = 900;
    private const ulong FinalFrame = 2400;
    private const ulong MovementChangeFrames = 90;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);
    private static readonly FixVector2[] Directions =
    {
        new FixVector2(Fix64.One, Fix64.Zero),
        new FixVector2(Fix64.Zero, Fix64.One),
        new FixVector2(-Fix64.One, Fix64.Zero),
        new FixVector2(Fix64.Zero, -Fix64.One),
    };

    private enum RunnerState
    {
        WaitingForPlay = 0,
        WaitingForStartup = 1,
        WaitingForRuntime = 2,
        Running = 3,
        WaitingForProfilerStop = 4,
        AnalyzingGcCapture = 5,
        Finishing = 6,
    }

    static Lv3MainPerfReproductionRunner()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
    }

    [MenuItem("Tools/Logic Frames/Run Lv3 MainPerf Reproduction")]
    public static void Run()
    {
        Start(false);
    }

    [MenuItem("Tools/Logic Frames/Run Lv3 MainPerf GC Capture")]
    public static void RunGcCapture()
    {
        Start(true);
    }

    private static void Start(bool captureGc)
    {
        if (SessionState.GetBool(RunningKey, false))
            throw new InvalidOperationException("Lv3 MainPerf reproduction is already running.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before starting Lv3 MainPerf reproduction.");
        if (EditorApplication.isCompiling)
            throw new InvalidOperationException("Wait for script compilation before starting Lv3 MainPerf reproduction.");

        EnsureNoDirtyScenes();
        string startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        SessionState.SetBool(RunningKey, true);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForPlay);
        SessionState.SetString(StartedUtcKey, startedUtc);
        SessionState.SetInt(NextMoveFrameKey, 0);
        SessionState.SetInt(DirectionIndexKey, 0);
        SessionState.SetBool(CaptureGcKey, captureGc);
        SessionState.SetBool(CaptureStartedKey, false);
        SessionState.SetInt(CaptureStartFrameKey, 0);
        SessionState.SetInt(CaptureEndFrameKey, 0);
        SessionState.SetInt(CaptureGcBaselineKey, 0);
        SessionState.SetInt(CaptureGcFinalKey, 0);
        SessionState.SetInt(CaptureProfilerLastFrameKey, -1);
        MainThreadFrameProfilerNextPlayCapture.ArmNextPlay();
        WriteResult(
            "RESULT=RUNNING" + Environment.NewLine +
            "startedUtc=" + startedUtc + Environment.NewLine +
            "captureGc=" + captureGc + Environment.NewLine +
            "invadeFrame=" + InvadeFrame + Environment.NewLine +
            "finalFrame=" + FinalFrame + Environment.NewLine);
        EditorSceneManager.OpenScene(LaunchScenePath, OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }

    private static void Update()
    {
        if (!SessionState.GetBool(RunningKey, false))
            return;

        try
        {
            ValidateTimeout();
            RunnerState state = (RunnerState)SessionState.GetInt(StateKey, (int)RunnerState.WaitingForPlay);
            if (!EditorApplication.isPlaying)
            {
                if (state == RunnerState.AnalyzingGcCapture)
                {
                    AnalyzeGcCapture();
                }
                else if (state == RunnerState.Finishing)
                {
                    SessionState.SetBool(RunningKey, false);
                    SessionState.EraseInt(StateKey);
                }
                else if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Fail(new InvalidOperationException(
                        $"Lv3 MainPerf reproduction left Play mode unexpectedly. state={state}."));
                }
                return;
            }

            switch (state)
            {
                case RunnerState.WaitingForPlay:
                    SessionState.SetInt(StateKey, (int)RunnerState.WaitingForStartup);
                    break;
                case RunnerState.WaitingForStartup:
                    EnterLv3();
                    break;
                case RunnerState.WaitingForRuntime:
                    BeginMovementWhenReady();
                    break;
                case RunnerState.Running:
                    AdvanceScenario();
                    break;
                case RunnerState.WaitingForProfilerStop:
                    ExitAfterProfilerStopped();
                    break;
                case RunnerState.AnalyzingGcCapture:
                case RunnerState.Finishing:
                    break;
                default:
                    throw new InvalidOperationException($"Unknown Lv3 MainPerf reproduction state {state}.");
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private static void EnterLv3()
    {
        if (GF.Procedure?.CurrentProcedure == null)
            return;
        if (GF.Procedure.CurrentProcedure is RuntimeProcedureBase)
        {
            SessionState.SetInt(StateKey, (int)RunnerState.WaitingForRuntime);
            return;
        }
        if (GF.Procedure.CurrentProcedure is not StartupLevelSelectProcedure)
            return;

        if (!StartupLevelSelectProcedure.TryEnterLevel("Lv_3", out string error))
            throw new InvalidOperationException($"Cannot enter Lv_3 from Launch: {error}");
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForRuntime);
    }

    private static void BeginMovementWhenReady()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase runtimeProcedure)
            return;
        if (!runtimeProcedure.IsEditorStressRuntimeReady || !LogicFrameRuntime.IsTimelineRunning)
            return;

        InputManager inputManager = GameEntry.GetComponent<InputManager>()
                                    ?? throw new InvalidOperationException("Lv3 MainPerf reproduction requires InputManager.");
        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("Lv3 MainPerf reproduction requires InputModel.");
        if (!inputModel.LogicTimeline.IsStarted)
            return;

        inputManager.ChangeState(InputState.Game);
        ulong currentFrame = LogicFrameRuntime.CurrentFrame;
        SessionState.SetInt(NextMoveFrameKey, checked((int)Math.Min(int.MaxValue, currentFrame + 1)));
        SessionState.SetInt(DirectionIndexKey, 0);
        SessionState.SetInt(StateKey, (int)RunnerState.Running);
        Log.Info("[Lv3MainPerfReproduction] Running. startFrame={0}.", currentFrame);
    }

    private static void AdvanceScenario()
    {
        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("Lv3 MainPerf reproduction lost InputModel.");
        ulong currentFrame = LogicFrameRuntime.CurrentFrame;
        ulong nextMoveFrame = checked((ulong)SessionState.GetInt(NextMoveFrameKey, 0));
        if (currentFrame >= nextMoveFrame)
        {
            int directionIndex = SessionState.GetInt(DirectionIndexKey, 0);
            inputModel.LogicTimeline.EnqueueWorldMove(
                Time.realtimeSinceStartupAsDouble,
                Directions[directionIndex]);
            SessionState.SetInt(DirectionIndexKey, (directionIndex + 1) % Directions.Length);
            SessionState.SetInt(
                NextMoveFrameKey,
                checked((int)Math.Min(int.MaxValue, currentFrame + MovementChangeFrames)));
            Log.Info(
                "[Lv3MainPerfReproduction] Move changed. frame={0}, directionIndex={1}.",
                currentFrame,
                directionIndex);
        }

        if (currentFrame >= InvadeFrame
            && PhaseManager.CurrentPhase == GamePhase.BuildBeforeInvade
            && LogicPhaseCommandService.PendingCount == 0)
        {
            PhaseManager.SwitchToPhase(GamePhase.Invade);
            Log.Info("[Lv3MainPerfReproduction] Invade scheduled. frame={0}.", currentFrame);
        }

        if (SessionState.GetBool(CaptureGcKey, false))
        {
            if (!SessionState.GetBool(CaptureStartedKey, false)
                && currentFrame >= GcCaptureStartFrame)
            {
                StartGcCapture(currentFrame);
            }
            else if (SessionState.GetBool(CaptureStartedKey, false)
                     && (GetCollectionCount() > SessionState.GetInt(CaptureGcBaselineKey, 0)
                         || currentFrame >= GcCaptureStartFrame + GcCaptureDurationFrames))
            {
                CompleteGcCapture(currentFrame);
                return;
            }
        }

        if (currentFrame < FinalFrame)
            return;

        inputModel.LogicTimeline.EnqueueWorldMove(Time.realtimeSinceStartupAsDouble, FixVector2.Zero);
        string report =
            "RESULT=PASS" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            "finalFrame=" + currentFrame + Environment.NewLine +
            "phase=" + PhaseManager.CurrentPhase + Environment.NewLine +
            "authority=" + LogicEntityLifecycleService.AuthorityEntityCount + Environment.NewLine +
            "boundViews=" + LogicEntityLifecycleService.BoundViewCount + Environment.NewLine;
        WriteResult(report);
        Log.Info("[Lv3MainPerfReproduction] PASS. finalFrame={0}, phase={1}.", currentFrame, PhaseManager.CurrentPhase);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        EditorApplication.isPlaying = false;
    }

    private static void StartGcCapture(ulong currentFrame)
    {
        if (Profiler.enabled || Profiler.enableBinaryLog || ProfilerDriver.enabled)
            throw new InvalidOperationException("Lv3 MainPerf GC capture requires Profiler recording to be disabled.");

        ProfilerDriver.ClearAllFrames();
        ProfilerDriver.profileEditor = true;
        ProfilerDriver.memoryRecordMode = ProfilerMemoryRecordMode.GCAlloc;
        ProfilerDriver.enabled = true;
        SessionState.SetBool(CaptureStartedKey, true);
        SessionState.SetInt(CaptureStartFrameKey, checked((int)Math.Min(int.MaxValue, currentFrame)));
        SessionState.SetInt(CaptureGcBaselineKey, GetCollectionCount());
        Log.Info(
            "[Lv3MainPerfReproduction] GC capture started. frame={0}, collectionBaseline={1}.",
            currentFrame,
            SessionState.GetInt(CaptureGcBaselineKey, 0));
    }

    private static void CompleteGcCapture(ulong currentFrame)
    {
        ProfilerDriver.enabled = false;
        SessionState.SetInt(CaptureEndFrameKey, checked((int)Math.Min(int.MaxValue, currentFrame)));
        SessionState.SetInt(CaptureGcFinalKey, GetCollectionCount());
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForProfilerStop);
    }

    private static void ExitAfterProfilerStopped()
    {
        if (ProfilerDriver.enabled)
            throw new InvalidOperationException("Lv3 MainPerf Profiler was still enabled after its stop boundary.");

        int lastFrame = ProfilerDriver.lastFrameIndex;
        if (lastFrame < ProfilerDriver.firstFrameIndex)
        {
            throw new InvalidOperationException(
                $"Lv3 MainPerf Profiler has no frozen frames. first={ProfilerDriver.firstFrameIndex}, last={lastFrame}.");
        }
        SessionState.SetInt(CaptureProfilerLastFrameKey, lastFrame);
        SessionState.SetInt(StateKey, (int)RunnerState.AnalyzingGcCapture);
        EditorApplication.isPlaying = false;
    }

    private static void AnalyzeGcCapture()
    {
        try
        {
            WriteProjectText(
                AllocationResultRelativePath,
                LogicRuntimeLongSessionGateRunner.BuildAllocationReport(
                    SessionState.GetInt(CaptureProfilerLastFrameKey, -1)));
        }
        finally
        {
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.memoryRecordMode = ProfilerMemoryRecordMode.None;
            SessionState.SetBool(CaptureStartedKey, false);
        }

        int captureEndFrame = SessionState.GetInt(CaptureEndFrameKey, 0);
        int collectionBaseline = SessionState.GetInt(CaptureGcBaselineKey, 0);
        int collectionFinal = SessionState.GetInt(CaptureGcFinalKey, 0);
        WriteResult(
            "RESULT=PASS" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            "captureGc=True" + Environment.NewLine +
            "captureStartFrame=" + SessionState.GetInt(CaptureStartFrameKey, 0) + Environment.NewLine +
            "captureEndFrame=" + captureEndFrame + Environment.NewLine +
            "collectionBaseline=" + collectionBaseline + Environment.NewLine +
            "collectionFinal=" + collectionFinal + Environment.NewLine +
            "collectionTriggered=" + (collectionFinal > collectionBaseline) + Environment.NewLine);
        Log.Info(
            "[Lv3MainPerfReproduction] GC capture complete. frame={0}, collections={1}->{2}.",
            captureEndFrame,
            collectionBaseline,
            collectionFinal);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        SessionState.SetBool(RunningKey, false);
        SessionState.EraseInt(StateKey);
    }

    private static int GetCollectionCount()
    {
        return GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
    }

    private static void ValidateTimeout()
    {
        string startedText = SessionState.GetString(StartedUtcKey, string.Empty);
        if (!DateTime.TryParse(
                startedText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime startedUtc))
        {
            throw new InvalidOperationException($"Invalid Lv3 MainPerf reproduction timestamp '{startedText}'.");
        }
        if (DateTime.UtcNow - startedUtc > Timeout)
            throw new TimeoutException($"Lv3 MainPerf reproduction exceeded timeout {Timeout}.");
    }

    private static void EnsureNoDirtyScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isDirty)
                throw new InvalidOperationException($"Cannot start with dirty scene '{scene.path}'.");
        }
    }

    private static void Fail(Exception exception)
    {
        StopGcCaptureIfRequired();
        WriteResult(
            "RESULT=FAIL" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            exception + Environment.NewLine);
        Debug.LogException(exception);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
        else
            SessionState.SetBool(RunningKey, false);
    }

    private static void WriteResult(string content)
    {
        WriteProjectText(ResultRelativePath, content);
    }

    private static void WriteProjectText(string relativePath, string content)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                             ?? throw new InvalidOperationException("Cannot resolve project root.");
        File.WriteAllText(Path.Combine(projectRoot, relativePath), content);
    }

    private static void StopGcCaptureIfRequired()
    {
        if (!SessionState.GetBool(CaptureStartedKey, false))
            return;

        ProfilerDriver.enabled = false;
        ProfilerDriver.profileEditor = false;
        ProfilerDriver.memoryRecordMode = ProfilerMemoryRecordMode.None;
        SessionState.SetBool(CaptureStartedKey, false);
    }
}
