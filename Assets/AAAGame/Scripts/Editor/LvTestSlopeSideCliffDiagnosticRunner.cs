using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityGameFramework.Runtime;

[InitializeOnLoad]
internal static class LvTestSlopeSideCliffDiagnosticRunner
{
    private const string LaunchScenePath = "Assets/AAAGame/Scene/Launch.unity";
    private const string LevelIdentifier = "LvTest";
    private const string ResultRelativePath = "Logs/LvTestSlopeSideCliffDiagnostic.txt";
    private const string SessionPrefix = "Avenge.LvTestSlopeSideCliffDiagnostic.";
    private const string RunningKey = SessionPrefix + "Running";
    private const string StateKey = SessionPrefix + "State";
    private const float ArrivalDistance = 0.12f;
    private const float CliffBoundaryZ = 50.5f;
    private const int LateralInputFrames = 120;
    private static readonly Vector3 ApproachPoint = new Vector3(67.95f, 0f, 49.95f);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);
    private static readonly List<Vector3> s_Route = new List<Vector3>();
    private static readonly StringBuilder s_FrameLog = new StringBuilder(32768);
    private static DateTime s_DeadlineUtc;
    private static int s_WaypointIndex;
    private static int s_LateralFrames;
    private static ulong s_LastInputFrame;
    private static ulong s_LastLateralInputFrame;
    private static float s_MaxZ;
    private static string s_FogError;

    private enum RunnerState
    {
        WaitingForPlay,
        WaitingForStartup,
        WaitingForRuntime,
        Approach,
        Lateral,
        Finishing,
    }

    static LvTestSlopeSideCliffDiagnosticRunner()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
    }

    [MenuItem("Tools/Diagnostics/Run LvTest Slope Side Cliff Diagnostic")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("LvTest slope-side diagnostic requires Edit Mode.");
        if (EditorApplication.isCompiling)
            throw new InvalidOperationException("Wait for script compilation before running the slope-side diagnostic.");

        ResetRuntimeState();
        EditorSceneManager.OpenScene(LaunchScenePath, OpenSceneMode.Single);
        SessionState.SetBool(RunningKey, true);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForPlay);
        s_DeadlineUtc = DateTime.UtcNow + Timeout;
        WriteResult("RESULT=RUNNING" + Environment.NewLine);
        EditorApplication.isPlaying = true;
    }

    private static void Update()
    {
        if (!SessionState.GetBool(RunningKey, false))
            return;

        try
        {
            RunnerState state = (RunnerState)SessionState.GetInt(StateKey, 0);
            if (!EditorApplication.isPlaying)
            {
                if (state == RunnerState.Finishing)
                {
                    SessionState.SetBool(RunningKey, false);
                    ResetRuntimeState();
                }
                return;
            }
            if (DateTime.UtcNow >= ResolveDeadlineUtc())
                throw new TimeoutException("LvTest slope-side diagnostic timed out in state " + state + ".");

            switch (state)
            {
                case RunnerState.WaitingForPlay:
                    SessionState.SetInt(StateKey, (int)RunnerState.WaitingForStartup);
                    break;
                case RunnerState.WaitingForStartup:
                    EnterLevelFromLaunch();
                    break;
                case RunnerState.WaitingForRuntime:
                    BeginApproachWhenReady();
                    break;
                case RunnerState.Approach:
                    AdvanceApproach();
                    break;
                case RunnerState.Lateral:
                    AdvanceLateralProbe();
                    break;
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private static DateTime ResolveDeadlineUtc()
    {
        if (s_DeadlineUtc == default)
            s_DeadlineUtc = DateTime.UtcNow + Timeout;
        return s_DeadlineUtc;
    }

    private static void EnterLevelFromLaunch()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase)
            return;
        if (!EditorRuntimeLevelEntry.TryEnterWithDefaultCareer(LevelIdentifier, out string errorMessage))
            throw new InvalidOperationException("Cannot enter LvTest from Launch: " + errorMessage);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForRuntime);
    }

    private static void BeginApproachWhenReady()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase runtimeProcedure
            || !runtimeProcedure.IsEditorStressRuntimeReady
            || LogicFrameRuntime.CurrentFrame < 2)
        {
            return;
        }

        IEntityContext hero = EntityRegistry.Player;
        if (hero is not ILogicFrameEntity logicHero)
            return;
        InputManager inputManager = GameEntry.GetComponent<InputManager>()
                                    ?? throw new InvalidOperationException("LvTest slope-side diagnostic requires InputManager.");
        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("LvTest slope-side diagnostic requires InputModel.");
        if (!inputModel.LogicTimeline.IsStarted)
            return;

        inputManager.ChangeState(InputState.Game);
        s_Route.Clear();
        if (!FlowFieldCrowdMovementSystem.TryGetNavigationPathCorners(
                hero.Position,
                ApproachPoint,
                logicHero.NavigationAgentTypeId,
                s_Route,
                out string failureReason))
        {
            throw new InvalidOperationException("Cannot route hero to slope-side probe: " + failureReason);
        }
        if (s_Route.Count < 2)
            throw new InvalidOperationException("Slope-side approach route has fewer than two corners.");

        s_WaypointIndex = 1;
        Application.logMessageReceived += OnLogMessage;
        SessionState.SetInt(StateKey, (int)RunnerState.Approach);
    }

    private static void AdvanceApproach()
    {
        IEntityContext hero = EntityRegistry.Player
                              ?? throw new InvalidOperationException("Slope-side diagnostic lost the hero during approach.");
        if (LogicFrameRuntime.CurrentFrame == s_LastInputFrame)
            return;

        while (s_WaypointIndex < s_Route.Count
               && HorizontalDistance(hero.Position, s_Route[s_WaypointIndex]) <= ArrivalDistance)
        {
            s_WaypointIndex++;
        }
        Vector3 target = s_WaypointIndex < s_Route.Count ? s_Route[s_WaypointIndex] : ApproachPoint;
        if (s_WaypointIndex >= s_Route.Count
            && HorizontalDistance(hero.Position, ApproachPoint) <= ArrivalDistance)
        {
            s_MaxZ = hero.Position.z;
            AppendStaticProbe(hero);
            Debug.Log("[SlopeSideRuntimeProbe] stage=lateral-start position=" + hero.Position
                      + " frame=" + LogicFrameRuntime.CurrentFrame);
            SessionState.SetInt(StateKey, (int)RunnerState.Lateral);
            return;
        }

        EnqueueMove(hero, target);
    }

    private static void AdvanceLateralProbe()
    {
        IEntityContext hero = EntityRegistry.Player
                              ?? throw new InvalidOperationException("Slope-side diagnostic lost the hero during lateral probe.");
        s_MaxZ = Mathf.Max(s_MaxZ, hero.Position.z);
        ulong frame = LogicFrameRuntime.CurrentFrame;
        if (frame != s_LastInputFrame)
            AppendFrameSample(hero, frame);
        if (s_LateralFrames >= LateralInputFrames)
        {
            if (frame > s_LastLateralInputFrame)
                Complete(hero.Position);
            return;
        }
        if (frame == s_LastInputFrame)
            return;

        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("Slope-side diagnostic lost InputModel.");
        inputModel.LogicTimeline.EnqueueEditorWorldMoveForNextFrame(new FixVector2(Fix64.Zero, Fix64.One));
        s_LastInputFrame = frame;
        s_LastLateralInputFrame = frame;
        s_LateralFrames++;
    }

    private static void EnqueueMove(IEntityContext hero, Vector3 target)
    {
        FixVector2 direction = new FixVector2(
            (Fix64)(target.x - hero.Position.x),
            (Fix64)(target.z - hero.Position.z));
        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("Slope-side diagnostic lost InputModel.");
        inputModel.LogicTimeline.EnqueueEditorWorldMoveForNextFrame(
            direction == FixVector2.Zero ? FixVector2.Zero : direction.GetNormalized());
        s_LastInputFrame = LogicFrameRuntime.CurrentFrame;
    }

    private static void Complete(Vector3 finalPosition)
    {
        bool blocked = s_MaxZ < CliffBoundaryZ;
        bool passed = blocked && string.IsNullOrEmpty(s_FogError);
        string result = passed ? "PASSED_BLOCKED" : "FAILED_CROSSED_CLIFF";
        string report = "RESULT=" + result + Environment.NewLine
                        + "final=" + finalPosition + Environment.NewLine
                        + "maxZ=" + s_MaxZ.ToString("F4", CultureInfo.InvariantCulture) + Environment.NewLine
                        + "boundaryZ=" + CliffBoundaryZ.ToString("F4", CultureInfo.InvariantCulture) + Environment.NewLine
                        + "lateralFrames=" + s_LateralFrames + Environment.NewLine
                        + "fogError=" + (s_FogError ?? string.Empty) + Environment.NewLine;
        report += "FRAME_LOG_BEGIN" + Environment.NewLine
                  + s_FrameLog
                  + "FRAME_LOG_END" + Environment.NewLine;
        WriteResult(report);
        Debug.Log("[SlopeSideRuntimeProbe] result=" + result + " final=" + finalPosition
                  + " maxZ=" + s_MaxZ.ToString("F4", CultureInfo.InvariantCulture)
                  + " boundaryZ=" + CliffBoundaryZ.ToString("F4", CultureInfo.InvariantCulture)
                  + " lateralFrames=" + s_LateralFrames);
        Finish();
    }

    private static void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (condition != null && condition.Contains("Fog viewer cell") && condition.Contains("has no Plane_H"))
            s_FogError = condition;
    }

    private static void Fail(Exception exception)
    {
        WriteResult("RESULT=FAIL" + Environment.NewLine + exception + Environment.NewLine);
        Debug.LogException(exception);
        Finish();
    }

    private static void Finish()
    {
        Application.logMessageReceived -= OnLogMessage;
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        EditorApplication.isPlaying = false;
    }

    private static void ResetRuntimeState()
    {
        Application.logMessageReceived -= OnLogMessage;
        s_Route.Clear();
        s_FrameLog.Clear();
        s_DeadlineUtc = default;
        s_WaypointIndex = 0;
        s_LateralFrames = 0;
        s_LastInputFrame = 0;
        s_LastLateralInputFrame = 0;
        s_MaxZ = float.NegativeInfinity;
        s_FogError = null;
    }

    private static void AppendStaticProbe(IEntityContext hero)
    {
        if (hero is not ILogicFrameEntity logicHero)
            throw new InvalidOperationException("Slope-side hero is not a logic-frame entity.");
        Fix64 radius = (
            hero.GetProperty(CreatureMainProperty.CollisionRadius));
        FixVector2 start = new FixVector2((Fix64)hero.Position.x, (Fix64)hero.Position.z);
        bool available = LogicStaticCollisionShadowService.TrySolveFixed(
            logicHero.NavigationAgentTypeId,
            start,
            new FixVector2(Fix64.Zero, Fix64.FromRaw(1024)),
            radius,
            out LogicStaticCollisionShadowResult result);
        s_FrameLog.Append("STATIC_PROBE available=").Append(available)
            .Append(" radiusRaw=").Append(radius.RawValue);
        if (available)
        {
            s_FrameLog.Append(" success=").Append(result.SolveResult.Success)
                .Append(" resolved=").Append(result.SolveResult.ResolvedDisplacement)
                .Append(" contact=").Append(result.ContactKind)
                .Append(" cell=(").Append(result.ContactCellX).Append(',').Append(result.ContactCellY).Append(')')
                .Append(" normal=").Append(result.SolveResult.FirstHitNormal);
        }
        s_FrameLog.AppendLine();
    }

    private static void AppendFrameSample(IEntityContext hero, ulong frame)
    {
        IReadOnlyList<LogicAgentCollisionShadowState> states = LogicAgentCollisionShadowService.LastStates;
        LogicAgentCollisionShadowState? found = null;
        for (int i = 0; i < states.Count; i++)
        {
            if (states[i].EntityId == hero.LogicEntityId)
            {
                found = states[i];
                break;
            }
        }

        s_FrameLog.Append("frame=").Append(frame)
            .Append(" position=").Append(hero.Position);
        if (found.HasValue)
        {
            LogicAgentCollisionShadowState state = found.Value;
            s_FrameLog.Append(" proposed=").Append(state.ProposedPosition)
                .Append(" pairCorrection=").Append(state.PairCorrection)
                .Append(" staticCorrection=").Append(state.StaticCorrection)
                .Append(" final=").Append(state.FinalResolvedPosition)
                .Append(" staticAvailable=").Append(state.StaticProjectionAvailable)
                .Append(" staticSucceeded=").Append(state.StaticProjectionSucceeded)
                .Append(" contact=").Append(state.StaticContactKind)
                .Append(" cell=(").Append(state.StaticContactCellX).Append(',').Append(state.StaticContactCellY).Append(')')
                .Append(" normal=").Append(state.FirstHitNormal);
        }
        else
        {
            s_FrameLog.Append(" collisionState=missing");
        }
        s_FrameLog.AppendLine();
    }

    private static float HorizontalDistance(Vector3 left, Vector3 right)
    {
        float x = right.x - left.x;
        float z = right.z - left.z;
        return Mathf.Sqrt(x * x + z * z);
    }

    private static void WriteResult(string text)
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ResultRelativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)
                                  ?? throw new InvalidOperationException("Slope-side result path has no directory."));
        File.WriteAllText(path, text);
    }
}
