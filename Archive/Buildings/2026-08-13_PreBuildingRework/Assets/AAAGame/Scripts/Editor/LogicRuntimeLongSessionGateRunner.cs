using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Profiling;
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
    private const string PhysicsBaselineResultRelativePath = "Logs/LogicRuntimePhysicsBaselineResult.txt";
    private const string PhysicsDisabledResultRelativePath = "Logs/LogicRuntimePhysicsDisabledResult.txt";
    private const string PhysicsComparisonResultRelativePath = "Logs/LogicRuntimePhysicsComparisonResult.txt";
    private const string PhysicsBaselineTraceRelativePath = "Logs/LogicRuntimePhysicsBaselineTrace.csv";
    private const string AllocationResultRelativePath = "Logs/LogicRuntimeAllocationDiagnostic.txt";
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
    private const string SuppressPhysicsSimulationKey = SessionPrefix + "SuppressPhysicsSimulation";
    private const string CaptureAllocationsKey = SessionPrefix + "CaptureAllocations";
    private const string AllocationCaptureStartedKey = SessionPrefix + "AllocationCaptureStarted";
    private const string PhysicsComparisonScheduleKey = SessionPrefix + "PhysicsComparisonSchedule";
    private const int BatchTicks = 10;
    private const int ProgressIntervalTicks = 3000;
    private const int MaxViewSettlementPumps = 600;
    private const ulong PhysicsComparisonDefendFrame = 150;
    private const ulong PhysicsComparisonMeasurementStartFrame = 300;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);
    private static readonly List<ulong[]> s_PhysicsAuthorityTrace = new List<ulong[]>();
    private static readonly string[] s_PhysicsAuthorityTraceFields =
    {
        "Frame",
        "Gameplay.Economy",
        "Gameplay.Commands",
        "Gameplay.WorldRules",
        "Gameplay.Entities",
        "Gameplay.Lifecycle",
        "Gameplay.Obstacles",
        "Gameplay.DamageEvents",
        "Gameplay.Projectiles",
        "Gameplay.Navigation.WorldAndConfig",
        "Gameplay.Navigation.CheckpointLiveState",
        "Gameplay.Navigation.WorldProgress",
        "Gameplay.Navigation.RuntimeObstacles",
        "Gameplay.Navigation.Agents",
        "Gameplay.Navigation.SectorPath.Count",
        "Gameplay.Navigation.SectorPath.Content",
        "Gameplay.Navigation.SectorPath.Usage",
        "Gameplay.Navigation.SectorPortalAccess.Count",
        "Gameplay.Navigation.SectorPortalAccess.Content",
        "Gameplay.Navigation.SectorPortalAccess.Usage",
        "Gameplay.Navigation.SharedGoal.Count",
        "Gameplay.Navigation.SharedGoal.Content",
        "Gameplay.Navigation.SharedGoal.Usage",
        "Gameplay.Navigation.Caches",
        "Gameplay.Navigation.FlowTiles",
        "Gameplay.Navigation.FlowTileBuildQueue",
        "Gameplay.Navigation.SharedGoalBuildQueue",
        "Gameplay.Navigation.MovingTargetAnchors",
        "Gameplay.Navigation.GoalReservations",
        "Gameplay.Navigation.FixedPortalOwners",
        "Gameplay.Navigation.FixedCorridorBuilds",
        "Gameplay.Allocators",
        "GameplayStateHash",
    };
    private static int s_PhysicsAuthorityTraceIndex;
    private static bool s_ValidatePhysicsAuthorityTrace;

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
        StartRun(3000, 2000, true, physicsComparisonSchedule: true);
    }

    public static void RunSystemValidationGate()
    {
        StartRun(3000, 2000, true);
    }

    [MenuItem("Tools/Logic Frames/Run Lv2 3k Tick Prototype Without FullHash")]
    public static void RunPrototypeWithoutFullHash()
    {
        StartRun(3000, 2000, false);
    }

    [MenuItem("Tools/Logic Frames/Run Lv2 3k Tick Prototype Without Physics")]
    public static void RunPrototypeWithoutPhysics()
    {
        StartRun(3000, 2000, true, true, physicsComparisonSchedule: true);
    }

    [MenuItem("Tools/Logic Frames/Compare Latest Lv2 3k Physics Results")]
    public static void CompareLatestPhysicsResults()
    {
        if (SessionState.GetBool(RunningKey, false) || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop the running logic gate before comparing Physics results.");

        string baseline = ReadProjectText(PhysicsBaselineResultRelativePath);
        string disabled = ReadProjectText(PhysicsDisabledResultRelativePath);
        RequirePassingResult("Physics baseline", baseline);
        RequirePassingResult("Physics disabled", disabled);

        string[] fields =
        {
            "startFrame",
            "finalFrame",
            "gameplayHash",
            "navigationAgentsHash",
            "gameplayTraceHash",
            "damageTraceHash",
            "damageSubmitted",
            "damageApplied",
            "damageSkippedDead",
            "combatObserved",
            "damageObserved",
            "combatWindows",
            "inputInjected",
            "authorityPeak",
            "enemyUnitPeak",
            "projectionFailures",
        };
        var report = new System.Text.StringBuilder();
        report.AppendLine("RESULT=PASS");
        report.AppendLine("physicsBaseline=" + PhysicsBaselineResultRelativePath);
        report.AppendLine("physicsDisabled=" + PhysicsDisabledResultRelativePath);
        for (int i = 0; i < fields.Length; i++)
        {
            string field = fields[i];
            string expected = ReadReportField(baseline, field);
            string actual = ReadReportField(disabled, field);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Physics authority comparison diverged at {field}. baseline={expected}, disabled={actual}.");
            }
            report.Append(field).Append('=').Append(expected).AppendLine();
        }

        WriteProjectText(PhysicsComparisonResultRelativePath, report.ToString());
        Debug.Log("[LogicLongSessionGateRunner] Physics authority comparison PASS. " + report);
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

    [MenuItem("Tools/Logic Frames/Run Lv2 30k Tick Memory Diagnostic Without FullHash Or Physics")]
    public static void RunMemoryDiagnosticWithoutFullHashOrPhysics()
    {
        StartRun(30000, 10000, false, true);
    }

    [MenuItem("Tools/Logic Frames/Run Lv2 3k Tick GC Allocation Diagnostic")]
    public static void RunGcAllocationDiagnostic()
    {
        StartRun(3000, 2000, false, false, true);
    }

    [MenuItem("Tools/Logic Frames/Run Lv2 3k Tick FullHash GC Allocation Diagnostic")]
    public static void RunFullHashGcAllocationDiagnostic()
    {
        StartRun(3000, 2000, true, false, true);
    }

    [MenuItem("Tools/Logic Frames/Run Lv2 30k Tick GC Allocation Diagnostic")]
    public static void RunLongGcAllocationDiagnostic()
    {
        StartRun(30000, 10000, false, false, true);
    }

    private static void StartRun(
        int totalTicks,
        int warmupTicks,
        bool computeFullHash,
        bool suppressPhysicsSimulation = false,
        bool captureAllocations = false,
        bool physicsComparisonSchedule = false)
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
        SessionState.SetBool(SuppressPhysicsSimulationKey, suppressPhysicsSimulation);
        SessionState.SetBool(CaptureAllocationsKey, captureAllocations);
        SessionState.SetBool(AllocationCaptureStartedKey, false);
        SessionState.SetBool(PhysicsComparisonScheduleKey, physicsComparisonSchedule);

        WriteResult(
            "RESULT=RUNNING" + Environment.NewLine +
            "startedUtc=" + startedUtc + Environment.NewLine +
            "totalTicks=" + totalTicks + Environment.NewLine +
            "warmupTicks=" + warmupTicks + Environment.NewLine +
            "computeFullHash=" + computeFullHash + Environment.NewLine +
            "suppressPhysicsSimulation=" + suppressPhysicsSimulation + Environment.NewLine +
            "captureAllocations=" + captureAllocations + Environment.NewLine);
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
        if (GF.Procedure.CurrentProcedure is not RuntimeProcedureBase)
            return;

        if (!EditorRuntimeLevelEntry.TryEnterWithDefaultCareer("Lv_2", out string errorMessage))
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

        if (SessionState.GetBool(PhysicsComparisonScheduleKey, false))
        {
            if (LogicFrameRuntime.CurrentFrame >= PhysicsComparisonDefendFrame)
            {
                throw new InvalidOperationException(
                    $"Physics comparison missed fixed Defend schedule frame. current={LogicFrameRuntime.CurrentFrame}, target={PhysicsComparisonDefendFrame}.");
            }
            LogicPhaseCommandService.ScheduleForEditorGate(GamePhase.Defend, PhysicsComparisonDefendFrame);
            SessionState.SetInt(StateKey, (int)RunnerState.WaitingForCombat);
            return;
        }
        else if (PhaseManager.CurrentPhase != GamePhase.Defend)
        {
            PhaseManager.SwitchToPhase(GamePhase.Defend);
        }
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
        if (SessionState.GetBool(PhysicsComparisonScheduleKey, false))
        {
            InputModel comparisonInputModel = GF.DataModel?.GetDataModel<InputModel>()
                                              ?? throw new InvalidOperationException("Physics comparison prelude requires InputModel.");
            if (comparisonInputModel.LogicTimeline.PendingEventCount != 0)
                return;
            if (LogicFrameRuntime.CurrentFrame >= PhysicsComparisonMeasurementStartFrame)
            {
                throw new InvalidOperationException(
                    $"Physics comparison could not acquire the logic clock before its fixed measurement frame. " +
                    $"current={LogicFrameRuntime.CurrentFrame}, measurement={PhysicsComparisonMeasurementStartFrame}.");
            }
            ArmGate(combatBaseline, PhysicsComparisonMeasurementStartFrame);
            return;
        }
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
        ArmGate(combatBaseline, 0);
    }

    private static void ArmGate(int combatBaseline, ulong measurementStartFrame)
    {
        if (LogicTimeControlService.IsPaused || LogicTimeControlService.SchedulerScale != 1d)
            throw new InvalidOperationException("Lv_2 combat entered with a paused or scaled logic scheduler.");
        if (LogicReplayRuntime.IsRecording)
            throw new InvalidOperationException("Replay recording is active before the logic runtime stress gate.");
        if (LogicReplayRuntime.RuntimeSessionRecordingEnabled)
            LogicReplayRuntime.SetRuntimeSessionRecordingEnabled(false);
        if (LogicReplayRuntime.ShouldRecordRuntimeSession)
            throw new InvalidOperationException("Replay recording is requested by the process command line.");
        bool captureAllocations = SessionState.GetBool(CaptureAllocationsKey, false);
        if (!captureAllocations && (Profiler.enabled || Profiler.enableBinaryLog))
        {
            throw new InvalidOperationException(
                "Logic runtime stress gate requires Unity Profiler recording to be disabled.");
        }
        PreparePhysicsAuthorityTrace();
        AcquireAssemblyReloadLock();
        try
        {
            EditorLogicRuntimeStressGate.Arm(
                SessionState.GetInt(TotalTicksKey, 0),
                SessionState.GetInt(WarmupTicksKey, 0),
                BatchTicks,
                combatBaseline,
                SessionState.GetBool(ComputeFullHashKey, true),
                SessionState.GetBool(SuppressPhysicsSimulationKey, false),
                captureAllocations,
                measurementStartFrame);
        }
        catch
        {
            CancelPhysicsAuthorityTrace();
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
                StartAllocationCaptureAfterWarmupIfRequired();
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
        Exception allocationFailure = CompleteAllocationCaptureIfRequested();
        if (allocationFailure != null)
        {
            FailRun(allocationFailure);
            return;
        }
        try
        {
            CompletePhysicsAuthorityTrace();
        }
        catch (Exception exception)
        {
            FailRun(exception);
            return;
        }
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
        CancelPhysicsAuthorityTrace();
        Exception allocationFailure = CompleteAllocationCaptureIfRequested();
        string gateReport = EditorLogicRuntimeStressGate.BuildReport();
        WriteResult(
            "RESULT=FAIL" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            gateReport + Environment.NewLine +
            exception + Environment.NewLine +
            (allocationFailure == null
                ? string.Empty
                : "Allocation diagnostic failure:" + Environment.NewLine + allocationFailure + Environment.NewLine));
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

    private static void BeginAllocationCapture()
    {
        if (Profiler.enabled || Profiler.enableBinaryLog || ProfilerDriver.enabled)
            throw new InvalidOperationException("GC allocation diagnostic requires Profiler recording to be disabled before capture.");

        ProfilerDriver.ClearAllFrames();
        ProfilerDriver.profileEditor = false;
        ProfilerDriver.memoryRecordMode = ProfilerMemoryRecordMode.GCAlloc;
        ProfilerDriver.enabled = true;
    }

    private static void StartAllocationCaptureAfterWarmupIfRequired()
    {
        if (!SessionState.GetBool(CaptureAllocationsKey, false)
            || SessionState.GetBool(AllocationCaptureStartedKey, false)
            || EditorLogicRuntimeStressGate.ProcessedTicks < SessionState.GetInt(WarmupTicksKey, 0))
        {
            return;
        }

        BeginAllocationCapture();
        SessionState.SetBool(AllocationCaptureStartedKey, true);
    }

    private static Exception CompleteAllocationCaptureIfRequested()
    {
        if (!SessionState.GetBool(CaptureAllocationsKey, false))
            return null;

        SessionState.SetBool(CaptureAllocationsKey, false);
        bool captureStarted = SessionState.GetBool(AllocationCaptureStartedKey, false);
        SessionState.SetBool(AllocationCaptureStartedKey, false);
        if (!captureStarted)
            return null;

        try
        {
            WriteAllocationReport(BuildAllocationReport());
            return null;
        }
        catch (Exception exception)
        {
            WriteAllocationReport("RESULT=FAIL" + Environment.NewLine + exception + Environment.NewLine);
            return exception;
        }
        finally
        {
            ProfilerDriver.enabled = false;
            ProfilerDriver.memoryRecordMode = ProfilerMemoryRecordMode.None;
        }
    }

    internal static string BuildAllocationReport()
    {
        return BuildAllocationReport(ProfilerDriver.lastFrameIndex);
    }

    internal static string BuildAllocationReport(int lastFrame)
    {
        int firstFrame = ProfilerDriver.firstFrameIndex;
        int availableLastFrame = ProfilerDriver.lastFrameIndex;
        if (lastFrame > availableLastFrame)
        {
            throw new InvalidOperationException(
                $"GC allocation diagnostic requested unavailable frame {lastFrame}. availableLast={availableLastFrame}.");
        }
        if (firstFrame < 0 || lastFrame < firstFrame)
            throw new InvalidOperationException($"GC allocation diagnostic has no recorded frames. first={firstFrame}, last={lastFrame}.");

        var allocationsByPath = new Dictionary<string, double>(StringComparer.Ordinal);
        var callsByPath = new Dictionary<string, int>(StringComparer.Ordinal);
        var allocationsByCallstack = new Dictionary<string, long>(StringComparer.Ordinal);
        var callsByCallstack = new Dictionary<string, int>(StringComparer.Ordinal);
        var resolvedMethods = new Dictionary<ulong, string>();
        var callstack = new List<ulong>();
        var callstackWriter = new System.Text.StringBuilder();
        var children = new List<int>();
        int validFrameCount = 0;
        int rawValidFrameCount = 0;
        int rawThreadViewCount = 0;
        int allocationSampleCount = 0;
        long allocationBytes = 0L;
        int missingCallstackSampleCount = 0;
        long missingCallstackBytes = 0L;
        for (int frame = firstFrame; frame <= lastFrame; frame++)
        {
            using HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(
                frame,
                0,
                HierarchyFrameDataView.ViewModes.Default,
                HierarchyFrameDataView.columnGcMemory,
                false);
            if (!view.valid)
                continue;

            validFrameCount++;
            CollectAllocationItems(view, view.GetRootItemID(), children, allocationsByPath, callsByPath);

            bool foundRawThread = false;
            for (int threadIndex = 0; ; threadIndex++)
            {
                using RawFrameDataView rawView = ProfilerDriver.GetRawFrameDataView(frame, threadIndex);
                if (!rawView.valid)
                    break;

                foundRawThread = true;
                rawThreadViewCount++;
                CollectAllocationSamples(
                    rawView,
                    FormatProfilerThread(rawView),
                    callstack,
                    callstackWriter,
                    resolvedMethods,
                    allocationsByCallstack,
                    callsByCallstack,
                    ref allocationSampleCount,
                    ref allocationBytes,
                    ref missingCallstackSampleCount,
                    ref missingCallstackBytes);
            }
            if (!foundRawThread)
                throw new InvalidOperationException($"GC allocation diagnostic raw frame {frame} has no valid threads.");
            rawValidFrameCount++;
        }

        if (allocationSampleCount == 0)
            throw new InvalidOperationException("GC allocation diagnostic recorded no GC.Alloc samples on any profiler thread.");

        var entries = new List<KeyValuePair<string, double>>(allocationsByPath);
        entries.Sort((left, right) =>
        {
            int bytesComparison = right.Value.CompareTo(left.Value);
            return bytesComparison != 0 ? bytesComparison : string.CompareOrdinal(left.Key, right.Key);
        });
        var callstackEntries = new List<KeyValuePair<string, long>>(allocationsByCallstack);
        callstackEntries.Sort((left, right) =>
        {
            int bytesComparison = right.Value.CompareTo(left.Value);
            return bytesComparison != 0 ? bytesComparison : string.CompareOrdinal(left.Key, right.Key);
        });

        var writer = new System.Text.StringBuilder();
        writer.AppendLine("RESULT=COMPLETE");
        writer.Append("firstFrame=").Append(firstFrame).AppendLine();
        writer.Append("lastFrame=").Append(lastFrame).AppendLine();
        writer.Append("validFrameCount=").Append(validFrameCount).AppendLine();
        writer.Append("rawValidFrameCount=").Append(rawValidFrameCount).AppendLine();
        writer.Append("rawThreadViewCount=").Append(rawThreadViewCount).AppendLine();
        writer.Append("allocationSampleCount=").Append(allocationSampleCount).AppendLine();
        writer.Append("allocationBytes=").Append(allocationBytes).AppendLine();
        writer.Append("missingCallstackSampleCount=").Append(missingCallstackSampleCount).AppendLine();
        writer.Append("missingCallstackBytes=").Append(missingCallstackBytes).AppendLine();
        writer.AppendLine("profilerObserverEffect=true");
        writer.AppendLine("retainedMemoryComparisonValid=false");
        writer.AppendLine("retainedMemoryInvalidReason=baseline-before-profiler-final-during-profiler");
        writer.AppendLine("CALLSTACKS:");
        int callstackResultCount = Math.Min(callstackEntries.Count, 100);
        for (int i = 0; i < callstackResultCount; i++)
        {
            KeyValuePair<string, long> entry = callstackEntries[i];
            writer.Append(i + 1)
                .Append(". bytes=").Append(entry.Value)
                .Append(", calls=").Append(callsByCallstack[entry.Key])
                .Append(", stack=").Append(entry.Key)
                .AppendLine();
        }
        writer.AppendLine("HIERARCHY:");
        int resultCount = Math.Min(entries.Count, 100);
        for (int i = 0; i < resultCount; i++)
        {
            KeyValuePair<string, double> entry = entries[i];
            writer.Append(i + 1)
                .Append(". bytes=").Append(entry.Value.ToString("R", CultureInfo.InvariantCulture))
                .Append(", calls=").Append(callsByPath[entry.Key])
                .Append(", path=").Append(entry.Key)
                .AppendLine();
        }
        return writer.ToString();
    }

    private static void CollectAllocationSamples(
        RawFrameDataView view,
        string threadLabel,
        List<ulong> callstack,
        System.Text.StringBuilder callstackWriter,
        Dictionary<ulong, string> resolvedMethods,
        Dictionary<string, long> allocationsByCallstack,
        Dictionary<string, int> callsByCallstack,
        ref int allocationSampleCount,
        ref long allocationBytes,
        ref int missingCallstackSampleCount,
        ref long missingCallstackBytes)
    {
        for (int sampleIndex = 0; sampleIndex < view.sampleCount; sampleIndex++)
        {
            if (!string.Equals(view.GetSampleName(sampleIndex), "GC.Alloc", StringComparison.Ordinal))
                continue;

            int metadataCount = view.GetSampleMetadataCount(sampleIndex);
            if (metadataCount < 1)
                throw new InvalidOperationException($"GC.Alloc sample {sampleIndex} has no allocation-size metadata.");
            long bytes = view.GetSampleMetadataAsLong(sampleIndex, 0);
            if (bytes <= 0L)
                throw new InvalidOperationException($"GC.Alloc sample {sampleIndex} has invalid allocation size {bytes}.");

            callstack.Clear();
            view.GetSampleCallstack(sampleIndex, callstack);

            callstackWriter.Clear();
            callstackWriter.Append("[thread=").Append(threadLabel).Append("] ");
            if (callstack.Count == 0)
            {
                callstackWriter.Append("<no-callstack>");
                missingCallstackSampleCount = checked(missingCallstackSampleCount + 1);
                missingCallstackBytes = checked(missingCallstackBytes + bytes);
            }
            else
            {
                for (int callstackIndex = 0; callstackIndex < callstack.Count; callstackIndex++)
                {
                    ulong address = callstack[callstackIndex];
                    if (!resolvedMethods.TryGetValue(address, out string method))
                    {
                        FrameDataView.MethodInfo methodInfo = view.ResolveMethodInfo(address);
                        method = FormatResolvedMethod(address, methodInfo);
                        resolvedMethods.Add(address, method);
                    }
                    if (callstackIndex > 0)
                        callstackWriter.Append(" <- ");
                    callstackWriter.Append(method);
                }
            }

            string callstackKey = callstackWriter.ToString();
            allocationsByCallstack.TryGetValue(callstackKey, out long accumulatedBytes);
            allocationsByCallstack[callstackKey] = checked(accumulatedBytes + bytes);
            callsByCallstack.TryGetValue(callstackKey, out int accumulatedCalls);
            callsByCallstack[callstackKey] = checked(accumulatedCalls + 1);
            allocationSampleCount = checked(allocationSampleCount + 1);
            allocationBytes = checked(allocationBytes + bytes);
        }
    }

    private static string FormatProfilerThread(RawFrameDataView view)
    {
        string groupName = string.IsNullOrEmpty(view.threadGroupName) ? "<no-group>" : view.threadGroupName;
        string threadName = string.IsNullOrEmpty(view.threadName) ? "<unnamed>" : view.threadName;
        return groupName + "/" + threadName + "#" + view.threadId;
    }

    private static string FormatResolvedMethod(ulong address, FrameDataView.MethodInfo methodInfo)
    {
        string methodName = methodInfo.methodName;
        if (string.IsNullOrEmpty(methodName))
            methodName = "0x" + address.ToString("X", CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(methodInfo.sourceFileName))
            return methodName;
        return methodName + " (" + methodInfo.sourceFileName + ":" + methodInfo.sourceFileLine + ")";
    }

    private static void CollectAllocationItems(
        HierarchyFrameDataView view,
        int parentId,
        List<int> children,
        Dictionary<string, double> allocationsByPath,
        Dictionary<string, int> callsByPath)
    {
        children.Clear();
        view.GetItemChildren(parentId, children);
        int[] childIds = children.ToArray();
        for (int i = 0; i < childIds.Length; i++)
        {
            int childId = childIds[i];
            double bytes = view.GetItemColumnDataAsDouble(childId, HierarchyFrameDataView.columnGcMemory);
            if (bytes > 0d)
            {
                string path = view.GetItemPath(childId);
                allocationsByPath.TryGetValue(path, out double accumulatedBytes);
                allocationsByPath[path] = accumulatedBytes + bytes;
                callsByPath.TryGetValue(path, out int accumulatedCalls);
                callsByPath[path] = checked(accumulatedCalls + 1);
            }
            CollectAllocationItems(view, childId, children, allocationsByPath, callsByPath);
        }
    }

    private static void WriteAllocationReport(string content)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                             ?? throw new InvalidOperationException("Cannot resolve project root for allocation diagnostic result.");
        File.WriteAllText(Path.Combine(projectRoot, AllocationResultRelativePath), content);
    }

    private static void PreparePhysicsAuthorityTrace()
    {
        CancelPhysicsAuthorityTrace();
        if (!SessionState.GetBool(PhysicsComparisonScheduleKey, false))
            return;

        s_ValidatePhysicsAuthorityTrace = SessionState.GetBool(SuppressPhysicsSimulationKey, false);
        if (s_ValidatePhysicsAuthorityTrace)
            LoadPhysicsAuthorityTrace();
        EditorLogicRuntimeStressGate.AuthorityFrameRecorded += RecordPhysicsAuthorityFrame;
    }

    private static void CompletePhysicsAuthorityTrace()
    {
        EditorLogicRuntimeStressGate.AuthorityFrameRecorded -= RecordPhysicsAuthorityFrame;
        if (!SessionState.GetBool(PhysicsComparisonScheduleKey, false))
            return;

        if (s_ValidatePhysicsAuthorityTrace)
        {
            if (s_PhysicsAuthorityTraceIndex != s_PhysicsAuthorityTrace.Count)
            {
                throw new InvalidOperationException(
                    $"Physics authority trace ended early. expected={s_PhysicsAuthorityTrace.Count}, actual={s_PhysicsAuthorityTraceIndex}.");
            }
        }
        else
        {
            WritePhysicsAuthorityTrace();
        }

        s_PhysicsAuthorityTrace.Clear();
        s_PhysicsAuthorityTraceIndex = 0;
        s_ValidatePhysicsAuthorityTrace = false;
    }

    private static void CancelPhysicsAuthorityTrace()
    {
        EditorLogicRuntimeStressGate.AuthorityFrameRecorded -= RecordPhysicsAuthorityFrame;
        s_PhysicsAuthorityTrace.Clear();
        s_PhysicsAuthorityTraceIndex = 0;
        s_ValidatePhysicsAuthorityTrace = false;
    }

    private static void RecordPhysicsAuthorityFrame(LogicGameplayStateDigest digest)
    {
        ulong[] actual = CapturePhysicsAuthorityFields(digest);
        if (!s_ValidatePhysicsAuthorityTrace)
        {
            s_PhysicsAuthorityTrace.Add(actual);
            return;
        }

        if (s_PhysicsAuthorityTraceIndex >= s_PhysicsAuthorityTrace.Count)
        {
            throw new InvalidOperationException(
                $"Physics-disabled authority trace has an unexpected extra frame {digest.FrameId}.");
        }

        ulong[] expected = s_PhysicsAuthorityTrace[s_PhysicsAuthorityTraceIndex];
        for (int i = 0; i < s_PhysicsAuthorityTraceFields.Length; i++)
        {
            if (expected[i] == actual[i])
                continue;
            throw new InvalidOperationException(
                $"Physics authority first divergence. frame={digest.FrameId}, domain={s_PhysicsAuthorityTraceFields[i]}, " +
                $"baseline={expected[i]}, disabled={actual[i]}.");
        }
        s_PhysicsAuthorityTraceIndex++;
    }

    private static ulong[] CapturePhysicsAuthorityFields(LogicGameplayStateDigest digest)
    {
        LogicNavigationAuthorityDigest navigation = digest.Navigation;
        FlowFieldCrowdMovementSystem.GetEditorTestAuthorityCacheState(
            out int sectorPathCount,
            out ulong sectorPathContentHash,
            out ulong sectorPathUsageHash,
            out int sectorPortalAccessCount,
            out ulong sectorPortalAccessContentHash,
            out ulong sectorPortalAccessUsageHash,
            out int sharedGoalCount,
            out ulong sharedGoalContentHash,
            out ulong sharedGoalUsageHash);
        return new[]
        {
            digest.FrameId,
            digest.EconomyHash,
            digest.CommandsHash,
            digest.WorldRulesHash,
            digest.EntitiesHash,
            digest.LifecycleHash,
            digest.ObstaclesHash,
            digest.DamageEventsHash,
            digest.ProjectilesHash,
            navigation.WorldAndConfigHash,
            navigation.CheckpointLiveStateHash,
            navigation.WorldProgressHash,
            navigation.RuntimeObstaclesHash,
            navigation.AgentsHash,
            (ulong)sectorPathCount,
            sectorPathContentHash,
            sectorPathUsageHash,
            (ulong)sectorPortalAccessCount,
            sectorPortalAccessContentHash,
            sectorPortalAccessUsageHash,
            (ulong)sharedGoalCount,
            sharedGoalContentHash,
            sharedGoalUsageHash,
            navigation.CachesHash,
            navigation.FlowTilesHash,
            navigation.FlowTileBuildQueueHash,
            navigation.SharedGoalBuildQueueHash,
            navigation.MovingTargetAnchorsHash,
            navigation.GoalReservationsHash,
            navigation.FixedPortalOwnersHash,
            navigation.FixedCorridorBuildsHash,
            digest.AllocatorsHash,
            digest.GameplayStateHash,
        };
    }

    private static void WritePhysicsAuthorityTrace()
    {
        var writer = new System.Text.StringBuilder();
        writer.AppendLine(string.Join(",", s_PhysicsAuthorityTraceFields));
        for (int i = 0; i < s_PhysicsAuthorityTrace.Count; i++)
        {
            ulong[] fields = s_PhysicsAuthorityTrace[i];
            for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                if (fieldIndex != 0)
                    writer.Append(',');
                writer.Append(fields[fieldIndex].ToString(CultureInfo.InvariantCulture));
            }
            writer.AppendLine();
        }
        WriteProjectText(PhysicsBaselineTraceRelativePath, writer.ToString());
    }

    private static void LoadPhysicsAuthorityTrace()
    {
        string content = ReadProjectText(PhysicsBaselineTraceRelativePath);
        string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        string expectedHeader = string.Join(",", s_PhysicsAuthorityTraceFields);
        if (lines.Length != 3001 || !string.Equals(lines[0], expectedHeader, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Physics baseline authority trace has an invalid shape. lines={lines.Length}, expectedLines=3001.");
        }

        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            string[] values = lines[lineIndex].Split(',');
            if (values.Length != s_PhysicsAuthorityTraceFields.Length)
            {
                throw new InvalidOperationException(
                    $"Physics baseline authority trace line {lineIndex + 1} has {values.Length} fields; expected {s_PhysicsAuthorityTraceFields.Length}.");
            }
            var fields = new ulong[values.Length];
            for (int fieldIndex = 0; fieldIndex < values.Length; fieldIndex++)
            {
                fields[fieldIndex] = ulong.Parse(
                    values[fieldIndex],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture);
            }
            s_PhysicsAuthorityTrace.Add(fields);
        }
    }

    private static string ReadProjectText(string relativePath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                             ?? throw new InvalidOperationException("Cannot resolve project root for gate comparison.");
        string path = Path.Combine(projectRoot, relativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("Logic gate comparison input is missing.", path);
        return File.ReadAllText(path);
    }

    private static void WriteProjectText(string relativePath, string content)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                             ?? throw new InvalidOperationException("Cannot resolve project root for gate comparison.");
        File.WriteAllText(Path.Combine(projectRoot, relativePath), content);
    }

    private static void RequirePassingResult(string label, string content)
    {
        if (!content.StartsWith("RESULT=PASS" + Environment.NewLine, StringComparison.Ordinal))
            throw new InvalidOperationException($"{label} result is not PASS.");
    }

    private static string ReadReportField(string content, string field)
    {
        string prefix = field + "=";
        int start = content.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"Logic gate result is missing field '{field}'.");
        start += prefix.Length;
        int end = content.IndexOfAny(new[] { ',', '\r', '\n' }, start);
        if (end < 0)
            end = content.Length;
        return content.Substring(start, end - start);
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
        if (!SessionState.GetBool(PhysicsComparisonScheduleKey, false))
            return;

        string comparisonRelativePath = SessionState.GetBool(SuppressPhysicsSimulationKey, false)
            ? PhysicsDisabledResultRelativePath
            : PhysicsBaselineResultRelativePath;
        File.WriteAllText(Path.Combine(projectRoot, comparisonRelativePath), content);
    }
}
