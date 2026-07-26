using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityGameFramework.Runtime;

[InitializeOnLoad]
public static class LogicRuntimeLongSessionGateRunner
{
    private const string LaunchScenePath = "Assets/AAAGame/Scene/Launch.unity";
    private const string ResultRelativePath = "Logs/LogicRuntimeLongSessionGateResult.txt";
    private const string SessionPrefix = "Avenge.LogicRuntimeLongSessionGate.";
    private const string RunningKey = SessionPrefix + "Running";
    private const string StateKey = SessionPrefix + "State";
    private const string TotalTicksKey = SessionPrefix + "TotalTicks";
    private const string WarmupTicksKey = SessionPrefix + "WarmupTicks";
    private const string CombatBaselineKey = SessionPrefix + "CombatBaseline";
    private const string StartedUtcKey = SessionPrefix + "StartedUtc";
    private const string SettlingPumpsKey = SessionPrefix + "SettlingPumps";
    private const string LastProgressTicksKey = SessionPrefix + "LastProgressTicks";
    private const string ReloadLockedKey = SessionPrefix + "ReloadLocked";
    private const string ComputeFullHashKey = SessionPrefix + "ComputeFullHash";
    private const int BatchTicks = 10;
    private const int ProgressIntervalTicks = 3000;
    private const int MaxViewSettlementPumps = 600;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    private enum RunnerState
    {
        WaitingForPlay = 0,
        WaitingForStartupProcedure = 1,
        WaitingForRuntime = 2,
        WaitingForCombat = 3,
        RunningGate = 4,
        Finishing = 5,
    }

    static LogicRuntimeLongSessionGateRunner()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
    }

    [MenuItem("Tools/Logic Frames/Run Lv2 3k Tick Prototype")]
    public static void RunPrototype()
    {
        StartRun(3000, 2000, true);
    }

    [MenuItem("Tools/Logic Frames/Run Lv2 3k Tick Prototype Without FullHash")]
    public static void RunPrototypeWithoutFullHash()
    {
        StartRun(3000, 2000, false);
    }

    [MenuItem("Tools/Logic Frames/Run Lv2 100k Tick Gate")]
    public static void RunFullGate()
    {
        StartRun(100000, 10000, true);
    }

    [MenuItem("Tools/Logic Frames/Run Lv2 30k Tick Memory Diagnostic")]
    public static void RunMemoryDiagnostic()
    {
        StartRun(30000, 10000, true);
    }

    [MenuItem("Tools/Logic Frames/Run Lv2 30k Tick Memory Diagnostic Without FullHash")]
    public static void RunMemoryDiagnosticWithoutFullHash()
    {
        StartRun(30000, 10000, false);
    }

    private static void StartRun(int totalTicks, int warmupTicks, bool computeFullHash)
    {
        if (SessionState.GetBool(RunningKey, false))
            throw new InvalidOperationException("A logic runtime long-session gate is already running.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before starting the logic runtime long-session gate.");
        if (EditorApplication.isCompiling)
            throw new InvalidOperationException("Wait for script compilation before starting the logic runtime long-session gate.");
        EnsureNoDirtyScenes();
        ClearProfilerHistory();
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(LaunchScenePath) == null)
            throw new FileNotFoundException("Launch scene is missing.", LaunchScenePath);

        string startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        SessionState.SetBool(RunningKey, true);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForPlay);
        SessionState.SetInt(TotalTicksKey, totalTicks);
        SessionState.SetInt(WarmupTicksKey, warmupTicks);
        SessionState.SetInt(CombatBaselineKey, 0);
        SessionState.SetInt(SettlingPumpsKey, 0);
        SessionState.SetInt(LastProgressTicksKey, 0);
        SessionState.SetString(StartedUtcKey, startedUtc);
        SessionState.SetBool(ComputeFullHashKey, computeFullHash);

        WriteResult(
            "RESULT=RUNNING" + Environment.NewLine +
            "startedUtc=" + startedUtc + Environment.NewLine +
            "totalTicks=" + totalTicks + Environment.NewLine +
            "warmupTicks=" + warmupTicks + Environment.NewLine +
            "computeFullHash=" + computeFullHash + Environment.NewLine);
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
                if (state == RunnerState.Finishing)
                {
                    ReleaseAssemblyReloadLock();
                    SessionState.SetBool(RunningKey, false);
                    SessionState.EraseInt(StateKey);
                }
                else if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    FailRun(new InvalidOperationException(
                        $"Logic runtime long-session gate left Play mode unexpectedly. state={state}."));
                }
                return;
            }

            switch (state)
            {
                case RunnerState.WaitingForPlay:
                    SessionState.SetInt(StateKey, (int)RunnerState.WaitingForStartupProcedure);
                    break;
                case RunnerState.WaitingForStartupProcedure:
                    EnterLv2FromLaunch();
                    break;
                case RunnerState.WaitingForRuntime:
                    PrepareRealCombat();
                    break;
                case RunnerState.WaitingForCombat:
                    StartGateWhenCombatReady();
                    break;
                case RunnerState.RunningGate:
                    MonitorGate();
                    break;
                case RunnerState.Finishing:
                    break;
                default:
                    throw new InvalidOperationException($"Unknown logic runtime gate runner state {state}.");
            }
        }
        catch (Exception exception)
        {
            FailRun(exception);
        }
    }

    private static void EnterLv2FromLaunch()
    {
        if (GF.Procedure == null || GF.Procedure.CurrentProcedure == null)
            return;
        if (GF.Procedure.CurrentProcedure is RuntimeProcedureBase)
        {
            SessionState.SetInt(StateKey, (int)RunnerState.WaitingForRuntime);
            return;
        }
        if (GF.Procedure.CurrentProcedure is not StartupLevelSelectProcedure)
            return;

        if (!StartupLevelSelectProcedure.TryEnterLevel("Lv_2", out string errorMessage))
            throw new InvalidOperationException($"Cannot enter Lv_2 from Launch: {errorMessage}");
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForRuntime);
    }

    private static void PrepareRealCombat()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase runtimeProcedure)
            return;
        if (!runtimeProcedure.IsEditorStressRuntimeReady || LogicFrameRuntime.CurrentFrame == 0)
            return;
        if (!LogicPhaseCommandService.IsInitialized)
            return;

        EnsureGameplayDiagnosticsDisabled();

        int combatBaseline = LogicEntityLifecycleService.AuthorityEntityCount;
        if (combatBaseline <= 0 || EntityRegistry.AllEntities.Count != combatBaseline)
            throw new InvalidOperationException($"Invalid Lv_2 pre-combat authority baseline {combatBaseline}.");
        SessionState.SetInt(CombatBaselineKey, combatBaseline);

        InputManager inputManager = GameEntry.GetComponent<InputManager>()
                                    ?? throw new InvalidOperationException("Logic runtime stress gate requires InputManager.");
        inputManager.ChangeState(InputState.UIForm);

        if (PhaseManager.CurrentPhase != GamePhase.Defend)
            PhaseManager.SwitchToPhase(GamePhase.Defend);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForCombat);
    }

    private static void EnsureGameplayDiagnosticsDisabled()
    {
        if (GameDebugSettings.IsEnabled(DebugCategory.Targeting)
            || GameDebugSettings.IsEnabled(DebugCategory.Attack)
            || GameDebugSettings.IsEnabled(DebugCategory.Move)
            || GameDebugSettings.IsEnabled(DebugCategory.Brain)
            || GameDebugSettings.IsEnabled(DebugCategory.GroupMove))
        {
            throw new InvalidOperationException(
                "Logic runtime stress gate requires all gameplay debug categories to be disabled.");
        }
    }

    private static void StartGateWhenCombatReady()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase runtimeProcedure)
            return;
        int combatBaseline = SessionState.GetInt(CombatBaselineKey, 0);
        if (PhaseManager.CurrentPhase != GamePhase.Defend || LogicPhaseCommandService.PendingCount != 0)
            return;
        if (LogicEntityLifecycleService.AuthorityEntityCount <= combatBaseline)
            return;
        if (LogicEntityLifecycleService.BoundViewCount != LogicEntityLifecycleService.AuthorityEntityCount)
            return;
        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("Logic runtime stress gate requires InputModel.");
        if (inputModel.LogicTimeline.PendingEventCount != 0)
            return;
        if (LogicTimeControlService.IsPaused || LogicTimeControlService.SchedulerScale != 1d)
            throw new InvalidOperationException("Lv_2 combat entered with a paused or scaled logic scheduler.");
        if (LogicReplayRuntime.IsRecording)
            throw new InvalidOperationException("Replay recording is active before the logic runtime stress gate.");
        if (LogicReplayRuntime.RuntimeSessionRecordingEnabled)
            LogicReplayRuntime.SetRuntimeSessionRecordingEnabled(false);
        if (LogicReplayRuntime.ShouldRecordRuntimeSession)
            throw new InvalidOperationException("Replay recording is requested by the process command line.");
        if (Profiler.enabled || Profiler.enableBinaryLog)
        {
            throw new InvalidOperationException(
                "Logic runtime stress gate requires Unity Profiler recording to be disabled.");
        }

        AcquireAssemblyReloadLock();
        try
        {
            EditorLogicRuntimeStressGate.Arm(
                SessionState.GetInt(TotalTicksKey, 0),
                SessionState.GetInt(WarmupTicksKey, 0),
                BatchTicks,
                combatBaseline,
                SessionState.GetBool(ComputeFullHashKey, true));
        }
        catch
        {
            ReleaseAssemblyReloadLock();
            throw;
        }
        SessionState.SetInt(StateKey, (int)RunnerState.RunningGate);
    }

    private static void MonitorGate()
    {
        switch (EditorLogicRuntimeStressGate.Status)
        {
            case EditorLogicRuntimeStressGateStatus.Armed:
                return;
            case EditorLogicRuntimeStressGateStatus.Running:
                WriteProgressIfDue();
                return;
            case EditorLogicRuntimeStressGateStatus.AwaitingViewSettlement:
                SettleViewsAndComplete();
                return;
            case EditorLogicRuntimeStressGateStatus.Completed:
                PassRun();
                return;
            case EditorLogicRuntimeStressGateStatus.Failed:
                throw new InvalidOperationException(EditorLogicRuntimeStressGate.BuildReport());
            default:
                throw new InvalidOperationException(
                    $"Logic runtime stress gate entered unexpected status {EditorLogicRuntimeStressGate.Status}.");
        }
    }

    private static void SettleViewsAndComplete()
    {
        int settlingPumps = SessionState.GetInt(SettlingPumpsKey, 0) + 1;
        SessionState.SetInt(SettlingPumpsKey, settlingPumps);
        if (LogicEntityLifecycleService.BoundViewCount == LogicEntityLifecycleService.AuthorityEntityCount)
        {
            EditorLogicRuntimeStressGate.CompleteAfterViewSettlement();
            return;
        }
        if (settlingPumps >= MaxViewSettlementPumps)
        {
            throw new InvalidOperationException(
                $"Views did not settle after {settlingPumps} editor pumps. bound={LogicEntityLifecycleService.BoundViewCount}, authority={LogicEntityLifecycleService.AuthorityEntityCount}.");
        }
    }

    private static void WriteProgressIfDue()
    {
        int processedTicks = EditorLogicRuntimeStressGate.ProcessedTicks;
        int lastProgressTicks = SessionState.GetInt(LastProgressTicksKey, 0);
        if (processedTicks < lastProgressTicks + ProgressIntervalTicks
            && processedTicks != EditorLogicRuntimeStressGate.TotalTicks)
        {
            return;
        }

        SessionState.SetInt(LastProgressTicksKey, processedTicks);
        WriteResult(
            "RESULT=RUNNING" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            EditorLogicRuntimeStressGate.BuildReport() + Environment.NewLine);
    }

    private static void PassRun()
    {
        string report = EditorLogicRuntimeStressGate.BuildReport();
        WriteResult(
            "RESULT=PASS" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            report + Environment.NewLine);
        Debug.Log("[LogicLongSessionGateRunner] PASS. " + report);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        EditorApplication.isPlaying = false;
    }

    private static void FailRun(Exception exception)
    {
        string gateReport = EditorLogicRuntimeStressGate.BuildReport();
        WriteResult(
            "RESULT=FAIL" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            gateReport + Environment.NewLine +
            exception + Environment.NewLine);
        Debug.LogException(exception);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
        else
        {
            ReleaseAssemblyReloadLock();
            SessionState.SetBool(RunningKey, false);
        }
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
            throw new InvalidOperationException($"Invalid gate start timestamp '{startedText}'.");
        }
        if (DateTime.UtcNow - startedUtc > Timeout)
            throw new TimeoutException($"Logic runtime long-session gate exceeded timeout {Timeout}.");
    }

    private static void EnsureNoDirtyScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isDirty)
            {
                throw new InvalidOperationException(
                    $"Cannot start the logic runtime long-session gate while scene '{scene.path}' has unsaved changes.");
            }
        }
    }

    private static void ClearProfilerHistory()
    {
        if (Profiler.enabled || Profiler.enableBinaryLog)
            throw new InvalidOperationException("Disable Unity Profiler recording before starting the logic runtime long-session gate.");

        ProfilerDriver.ClearAllFrames();
        if (ProfilerDriver.firstFrameIndex >= 0 || ProfilerDriver.lastFrameIndex >= 0)
        {
            throw new InvalidOperationException(
                $"Unity Profiler frame history did not clear. first={ProfilerDriver.firstFrameIndex}, last={ProfilerDriver.lastFrameIndex}.");
        }

        Debug.Log("[LogicLongSessionGateRunner] Unity Profiler frame history cleared before entering Play mode.");
    }

    private static void AcquireAssemblyReloadLock()
    {
        if (SessionState.GetBool(ReloadLockedKey, false))
            throw new InvalidOperationException("Logic runtime gate assembly reload lock is already held.");
        EditorApplication.LockReloadAssemblies();
        SessionState.SetBool(ReloadLockedKey, true);
        Debug.Log("[LogicLongSessionGateRunner] Assembly reload locked for deterministic long-session execution.");
    }

    private static void ReleaseAssemblyReloadLock()
    {
        if (!SessionState.GetBool(ReloadLockedKey, false))
            return;
        EditorApplication.UnlockReloadAssemblies();
        SessionState.SetBool(ReloadLockedKey, false);
        Debug.Log("[LogicLongSessionGateRunner] Assembly reload unlocked.");
    }

    private static void WriteResult(string content)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                             ?? throw new InvalidOperationException("Cannot resolve project root for gate result.");
        string resultPath = Path.Combine(projectRoot, ResultRelativePath);
        string directory = Path.GetDirectoryName(resultPath)
                           ?? throw new InvalidOperationException("Cannot resolve gate result directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(resultPath, content);
    }
}
