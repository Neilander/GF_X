using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityGameFramework.Runtime;

[InitializeOnLoad]
public static class LogicRuntimeIdleMemoryControlRunner
{
    private const string LaunchScenePath = "Assets/AAAGame/Scene/Launch.unity";
    private const string ResultRelativePath = "Logs/LogicRuntimeIdleMemoryControlResult.txt";
    private const string SessionPrefix = "Avenge.LogicRuntimeIdleMemoryControl.";
    private const string RunningKey = SessionPrefix + "Running";
    private const string StateKey = SessionPrefix + "State";
    private const string StateStartedKey = SessionPrefix + "StateStarted";
    private const string StartedUtcKey = SessionPrefix + "StartedUtc";
    private const string ManagedBaselineKey = SessionPrefix + "ManagedBaseline";
    private const string MonoUsedBaselineKey = SessionPrefix + "MonoUsedBaseline";
    private const string MonoHeapBaselineKey = SessionPrefix + "MonoHeapBaseline";
    private const string ReservedBaselineKey = SessionPrefix + "ReservedBaseline";
    private const double WarmupSeconds = 20d;
    private const double MeasurementSeconds = 30d;

    private enum RunnerState
    {
        WaitingForPlay = 0,
        WaitingForStartupProcedure = 1,
        WarmingUp = 2,
        Measuring = 3,
        Finishing = 4,
    }

    static LogicRuntimeIdleMemoryControlRunner()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
    }

    [MenuItem("Tools/Logic Frames/Run Launch Idle Memory Control")]
    public static void Run()
    {
        if (SessionState.GetBool(RunningKey, false))
            throw new InvalidOperationException("A Launch idle memory control is already running.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before starting the Launch idle memory control.");
        if (EditorApplication.isCompiling)
            throw new InvalidOperationException("Wait for script compilation before starting the Launch idle memory control.");
        EnsureNoDirtyScenes();
        if (Profiler.enabled || Profiler.enableBinaryLog)
            throw new InvalidOperationException("Disable Unity Profiler recording before starting the Launch idle memory control.");

        ProfilerDriver.ClearAllFrames();
        if (ProfilerDriver.firstFrameIndex >= 0 || ProfilerDriver.lastFrameIndex >= 0)
        {
            throw new InvalidOperationException(
                $"Unity Profiler frame history did not clear. first={ProfilerDriver.firstFrameIndex}, last={ProfilerDriver.lastFrameIndex}.");
        }

        string startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        SessionState.SetBool(RunningKey, true);
        SetState(RunnerState.WaitingForPlay);
        SessionState.SetString(StartedUtcKey, startedUtc);
        WriteResult(
            "RESULT=RUNNING" + Environment.NewLine +
            "startedUtc=" + startedUtc + Environment.NewLine +
            "warmupSeconds=" + WarmupSeconds.ToString("R", CultureInfo.InvariantCulture) + Environment.NewLine +
            "measurementSeconds=" + MeasurementSeconds.ToString("R", CultureInfo.InvariantCulture) + Environment.NewLine);

        EditorSceneManager.OpenScene(LaunchScenePath, OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }

    private static void Update()
    {
        if (!SessionState.GetBool(RunningKey, false))
            return;

        try
        {
            RunnerState state = (RunnerState)SessionState.GetInt(StateKey, (int)RunnerState.WaitingForPlay);
            if (!EditorApplication.isPlaying)
            {
                if (state == RunnerState.Finishing)
                    ResetSession();
                else if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    Fail(new InvalidOperationException($"Launch idle memory control left Play mode unexpectedly. state={state}."));
                return;
            }

            switch (state)
            {
                case RunnerState.WaitingForPlay:
                    SetState(RunnerState.WaitingForStartupProcedure);
                    break;
                case RunnerState.WaitingForStartupProcedure:
                    WaitForStartupProcedure();
                    break;
                case RunnerState.WarmingUp:
                    Warmup();
                    break;
                case RunnerState.Measuring:
                    Measure();
                    break;
                case RunnerState.Finishing:
                    break;
                default:
                    throw new InvalidOperationException($"Unknown Launch idle memory control state {state}.");
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private static void WaitForStartupProcedure()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase)
            return;
        if (!LogicFrameRuntime.IsActive || !LogicTimeControlService.IsActive)
            throw new InvalidOperationException("Launch idle memory control found an inactive runtime timeline.");
        SetState(RunnerState.WarmingUp);
    }

    private static void Warmup()
    {
        ValidateIdleRuntime();
        if (ElapsedStateSeconds() < WarmupSeconds)
            return;

        ForceFullCollection();
        SessionState.SetString(ManagedBaselineKey, GC.GetTotalMemory(true).ToString(CultureInfo.InvariantCulture));
        SessionState.SetString(MonoUsedBaselineKey, Profiler.GetMonoUsedSizeLong().ToString(CultureInfo.InvariantCulture));
        SessionState.SetString(MonoHeapBaselineKey, Profiler.GetMonoHeapSizeLong().ToString(CultureInfo.InvariantCulture));
        SessionState.SetString(ReservedBaselineKey, Profiler.GetTotalReservedMemoryLong().ToString(CultureInfo.InvariantCulture));
        SetState(RunnerState.Measuring);
    }

    private static void Measure()
    {
        ValidateIdleRuntime();
        if (ElapsedStateSeconds() < MeasurementSeconds)
            return;

        ForceFullCollection();
        long managedBaseline = GetRequiredLong(ManagedBaselineKey);
        long monoUsedBaseline = GetRequiredLong(MonoUsedBaselineKey);
        long monoHeapBaseline = GetRequiredLong(MonoHeapBaselineKey);
        long reservedBaseline = GetRequiredLong(ReservedBaselineKey);
        long managedFinal = GC.GetTotalMemory(true);
        long monoUsedFinal = Profiler.GetMonoUsedSizeLong();
        long monoHeapFinal = Profiler.GetMonoHeapSizeLong();
        long reservedFinal = Profiler.GetTotalReservedMemoryLong();

        string report =
            "RESULT=PASS" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            "managedBaseline=" + managedBaseline + Environment.NewLine +
            "managedFinal=" + managedFinal + Environment.NewLine +
            "managedGrowth=" + Math.Max(0L, managedFinal - managedBaseline) + Environment.NewLine +
            "monoUsedBaseline=" + monoUsedBaseline + Environment.NewLine +
            "monoUsedFinal=" + monoUsedFinal + Environment.NewLine +
            "monoUsedGrowth=" + Math.Max(0L, monoUsedFinal - monoUsedBaseline) + Environment.NewLine +
            "monoHeapBaseline=" + monoHeapBaseline + Environment.NewLine +
            "monoHeapFinal=" + monoHeapFinal + Environment.NewLine +
            "monoHeapGrowth=" + Math.Max(0L, monoHeapFinal - monoHeapBaseline) + Environment.NewLine +
            "reservedBaseline=" + reservedBaseline + Environment.NewLine +
            "reservedFinal=" + reservedFinal + Environment.NewLine +
            "reservedGrowth=" + Math.Max(0L, reservedFinal - reservedBaseline) + Environment.NewLine;
        WriteResult(report);
        Debug.Log("[LogicRuntimeIdleMemoryControl] PASS. " + report.Replace(Environment.NewLine, ", "));
        SetState(RunnerState.Finishing);
        EditorApplication.isPlaying = false;
    }

    private static void ValidateIdleRuntime()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase)
            throw new InvalidOperationException("Launch idle memory control left RuntimeProcedureBase.");
        if (!LogicFrameRuntime.IsActive || !LogicTimeControlService.IsActive)
            throw new InvalidOperationException("Launch idle memory control observed an inactive logic timeline.");
        if (Profiler.enabled || Profiler.enableBinaryLog)
            throw new InvalidOperationException("Unity Profiler recording became enabled during the Launch idle memory control.");
    }

    private static void ForceFullCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
    }

    private static void SetState(RunnerState state)
    {
        SessionState.SetInt(StateKey, (int)state);
        SessionState.SetString(
            StateStartedKey,
            EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
    }

    private static double ElapsedStateSeconds()
    {
        string value = SessionState.GetString(StateStartedKey, string.Empty);
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double started))
            throw new InvalidOperationException($"Invalid Launch idle memory control state time '{value}'.");
        return EditorApplication.timeSinceStartup - started;
    }

    private static long GetRequiredLong(string key)
    {
        string value = SessionState.GetString(key, string.Empty);
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result))
            throw new InvalidOperationException($"Invalid Launch idle memory control sample '{key}'='{value}'.");
        return result;
    }

    private static void Fail(Exception exception)
    {
        WriteResult(
            "RESULT=FAIL" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            exception + Environment.NewLine);
        Debug.LogException(exception);
        SetState(RunnerState.Finishing);
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
        else
            ResetSession();
    }

    private static void ResetSession()
    {
        SessionState.SetBool(RunningKey, false);
        SessionState.EraseInt(StateKey);
        SessionState.EraseString(StateStartedKey);
        SessionState.EraseString(ManagedBaselineKey);
        SessionState.EraseString(MonoUsedBaselineKey);
        SessionState.EraseString(MonoHeapBaselineKey);
        SessionState.EraseString(ReservedBaselineKey);
    }

    private static void EnsureNoDirtyScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isDirty)
                throw new InvalidOperationException($"Cannot start Launch idle memory control while scene '{scene.path}' has unsaved changes.");
        }
    }

    private static void WriteResult(string content)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                             ?? throw new InvalidOperationException("Cannot resolve project root for idle memory control result.");
        string resultPath = Path.Combine(projectRoot, ResultRelativePath);
        string directory = Path.GetDirectoryName(resultPath)
                           ?? throw new InvalidOperationException("Cannot resolve idle memory control result directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(resultPath, content);
    }
}
