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
internal static class LvTestBuildInteractionDiagnosticRunner
{
    private const string LaunchScenePath = "Assets/AAAGame/Scene/Launch.unity";
    private const string LevelIdentifier = "LvTest";
    private const string RequestRelativePath = "Logs/RunLvTestBuildInteractionDiagnostic.request";
    private const string ResultRelativePath = "Logs/LvTestBuildInteractionDiagnosticResult.txt";
    private const string SessionPrefix = "Avenge.LvTestBuildInteractionDiagnostic.";
    private const string RunningKey = SessionPrefix + "Running";
    private const string StateKey = SessionPrefix + "State";
    private const string StartedUtcKey = SessionPrefix + "StartedUtc";
    private const double RuntimeIssueTimeoutSeconds = 90.0;
    private const double ViewBindingTimeoutSeconds = 5.0;
    private const double EditorPauseDurationSeconds = 3.0;
    private const double EditorPauseResumeTimeoutSeconds = 5.0;
    private const int DiagnosticObstacleIdBase = 1800000000;
    private const int DiagnosticObstacleCount = 32;
    private const int ExpectedNavigationWorldCount = 3;

    private static readonly Dictionary<int, int> s_InitialEnemyAttackCounts = new();
    private static readonly Dictionary<int, double> s_UnboundEnemyViewFirstSeen = new();
    private static StringBuilder s_Report;
    private static double s_RuntimeIssueDeadline;
    private static bool s_DefendSpawnObserved;
    private static bool s_SpawnSpeedReleaseObserved;
    private static bool s_EnemyAttackObserved;
    private static bool s_NavigationReadyObserved;
    private static int s_NavigationReadyWorldCount;
    private static int s_RuntimeEnemyViewCount;
    private static double s_EditorPauseReleaseTime;
    private static ulong s_EditorPauseStartFrame;
    private static ulong s_EditorPauseResumeRequestFrame;
    private static ulong s_EditorPauseRebaseLogFrame;
    private static bool s_EditorPauseResumeRequested;
    private static bool s_EditorPauseRebaseObserved;
    private static bool s_EditorPauseFirstFrameValidated;
    private static int s_EditorPauseCatchUpCount;

    private enum RunnerState
    {
        WaitingForPlay,
        WaitingForStartupProcedure,
        WaitingForRuntime,
        WaitingForEditorPauseResume,
        WaitingForRuntimeIssues,
        Finishing,
    }

    static LvTestBuildInteractionDiagnosticRunner()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        if (TryConsumeRunRequest())
            EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/AAAGame/Diagnostics/Run LvTest Build Interaction Diagnostic")]
    public static void Run()
    {
        if (SessionState.GetBool(RunningKey, false))
            throw new InvalidOperationException("An LvTest build interaction diagnostic is already running.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before starting the LvTest build interaction diagnostic.");
        if (EditorApplication.isCompiling)
            throw new InvalidOperationException("Wait for script compilation before starting the LvTest build interaction diagnostic.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(LaunchScenePath) == null)
            throw new FileNotFoundException("Launch scene is missing.", LaunchScenePath);

        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var scene = EditorSceneManager.GetSceneAt(i);
            if (scene.isDirty)
                throw new InvalidOperationException($"Cannot run diagnostic while scene '{scene.path}' has unsaved changes.");
        }

        string startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        SessionState.SetBool(RunningKey, true);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForPlay);
        SessionState.SetString(StartedUtcKey, startedUtc);
        ResetRuntimeIssueState();
        WriteResult(
            "RESULT=RUNNING" + Environment.NewLine
            + "startedUtc=" + startedUtc + Environment.NewLine
            + "launchScene=" + LaunchScenePath + Environment.NewLine
            + "level=" + LevelIdentifier + Environment.NewLine);

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
                if (state == RunnerState.Finishing && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    SessionState.SetBool(RunningKey, false);
                    SessionState.EraseInt(StateKey);
                }
                return;
            }

            switch (state)
            {
                case RunnerState.WaitingForPlay:
                    SessionState.SetInt(StateKey, (int)RunnerState.WaitingForStartupProcedure);
                    break;
                case RunnerState.WaitingForStartupProcedure:
                    EnterLevelFromLaunch();
                    break;
                case RunnerState.WaitingForRuntime:
                    DiagnoseWhenRuntimeReady();
                    break;
                case RunnerState.WaitingForEditorPauseResume:
                    DiagnoseEditorPauseResume();
                    break;
                case RunnerState.WaitingForRuntimeIssues:
                    DiagnoseRuntimeIssues();
                    break;
                case RunnerState.Finishing:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown diagnostic runner state.");
            }
        }
        catch (Exception exception)
        {
            StopRuntimeLogCapture();
            EditorApplication.isPaused = false;
            WriteResult(
                "RESULT=FAIL" + Environment.NewLine
                + "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine
                + "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine
                + exception + Environment.NewLine);
            Debug.LogException(exception);
            SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
            EditorApplication.isPlaying = false;
        }
    }

    private static void EnterLevelFromLaunch()
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

        if (!StartupLevelSelectProcedure.TryEnterLevel(LevelIdentifier, out string errorMessage))
            throw new InvalidOperationException($"Cannot enter {LevelIdentifier} from Launch: {errorMessage}");
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForRuntime);
    }

    private static void DiagnoseWhenRuntimeReady()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase runtimeProcedure)
            return;
        if (!runtimeProcedure.IsEditorStressRuntimeReady || LogicFrameRuntime.CurrentFrame < 2)
            return;

        BuildManager buildManager = GameEntry.GetComponent<BuildManager>()
                                    ?? throw new InvalidOperationException("BuildManager is missing after LvTest runtime initialization.");
        GamePhase phase = (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);
        var report = new StringBuilder(8192);
        report.AppendLine("RESULT=RUNNING");
        report.Append("startedUtc=").AppendLine(SessionState.GetString(StartedUtcKey, string.Empty));
        report.Append("launchScene=").AppendLine(LaunchScenePath);
        report.Append("level=").AppendLine(LevelIdentifier);
        report.Append("phase=").AppendLine(phase.ToString());
        report.Append("logicFrame=").AppendLine(LogicFrameRuntime.CurrentFrame.ToString(CultureInfo.InvariantCulture));
        report.Append("playerFaction=").AppendLine(EntitySideHelper.PlayerFactionId.ToString(CultureInfo.InvariantCulture));
        report.Append("unlockedArchetypes=").AppendLine(CollectUnlockedArchetypes());

        int lv0Count = 0;
        int playerOwnedCount = 0;
        int playerVisibleHostCount = 0;
        int foreignVisibleHostCount = 0;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] is not LogicEntityState state
                || state.BuildingData == null
                || state.BuildingData.Lv != 0)
            {
                continue;
            }

            lv0Count++;
            if (state.OwnerFactionId == EntitySideHelper.PlayerFactionId)
                playerOwnedCount++;

            List<BuildingData> allCandidates = buildManager.GetLv0ConstructCandidates(state, requireUnlockedArche: false);
            List<BuildingData> unlockedCandidates = buildManager.GetLv0ConstructCandidates(state, requireUnlockedArche: true);
            bool hasConstructOption = buildManager.HasConstructOption(state);
            bool logicHasVisibleOptions = LogicInteractionOptionService.HasVisibleOptions(state);
            bool hasBoundView = LogicEntityLifecycleService.TryGetBoundView(state.LogicEntityId, out MAEntity view) && view != null;
            InteractionHost host = hasBoundView ? view.GetComponent<InteractionHost>() : null;
            bool hostHasVisibleOptions = host != null && host.HasVisibleOptions();
            if (hostHasVisibleOptions)
            {
                if (state.OwnerFactionId == EntitySideHelper.PlayerFactionId)
                    playerVisibleHostCount++;
                else
                    foreignVisibleHostCount++;
            }

            report.Append("LV0 entity=").Append(state.LogicEntityId.Value)
                .Append(" building=").Append(state.BuildingData.Identifier)
                .Append(" type=").Append(state.BuildingData.Type)
                .Append(" instance=").Append(state.BuildingInstanceId)
                .Append(" stronghold=").Append(string.IsNullOrWhiteSpace(state.StrongholdId) ? "<none>" : state.StrongholdId)
                .Append(" ownerFaction=").Append(state.OwnerFactionId)
                .Append(" positionRaw=").Append(state.PositionFixed.x.RawValue).Append(',').Append(state.PositionFixed.y.RawValue)
                .Append(" hasConstructOption=").Append(hasConstructOption)
                .Append(" allCandidates=").Append(JoinCandidateIds(allCandidates))
                .Append(" unlockedCandidates=").Append(JoinCandidateIds(unlockedCandidates))
                .Append(" logicVisible=").Append(logicHasVisibleOptions)
                .Append(" boundView=").Append(hasBoundView)
                .Append(" host=").Append(host != null)
                .Append(" hostVisible=").Append(hostHasVisibleOptions)
                .AppendLine();

            IReadOnlyList<LogicInteractionOptionDescriptor> options = state.InteractionOptions;
            for (int optionIndex = 0; optionIndex < options.Count; optionIndex++)
            {
                LogicInteractionOptionDescriptor option = options[optionIndex];
                report.Append("  OPTION index=").Append(optionIndex)
                    .Append(" kind=").Append(option.Kind)
                    .Append(" primaryId=").Append(option.PrimaryId)
                    .Append(" visible=").Append(LogicInteractionOptionService.IsVisible(state, option))
                    .Append(" executable=").Append(LogicInteractionOptionService.IsExecutable(state, option))
                    .AppendLine();
            }
        }

        if (lv0Count == 0)
            throw new InvalidOperationException("LvTest runtime contains no Lv0 building points.");
        if (playerOwnedCount == 0)
            throw new InvalidOperationException("LvTest runtime contains no player-owned Lv0 building points.");
        if (playerVisibleHostCount != playerOwnedCount)
        {
            throw new InvalidOperationException(
                $"LvTest player Lv0 build permissions are incomplete. playerOwned={playerOwnedCount}, visibleHosts={playerVisibleHostCount}.");
        }
        if (foreignVisibleHostCount != 0)
            throw new InvalidOperationException($"LvTest exposes {foreignVisibleHostCount} foreign Lv0 interaction hosts.");

        report.Append("SUMMARY lv0=").Append(lv0Count)
            .Append(" playerOwned=").Append(playerOwnedCount)
            .Append(" playerVisibleHosts=").Append(playerVisibleHostCount)
            .Append(" foreignVisibleHosts=").Append(foreignVisibleHostCount)
            .AppendLine();

        s_Report = report;
        CaptureEnemyAttackBaselines();
        Application.logMessageReceived -= OnRuntimeLog;
        Application.logMessageReceived += OnRuntimeLog;
        WriteResult(report.ToString());
        BeginEditorPauseDiagnostic();
    }

    private static void BeginEditorPauseDiagnostic()
    {
        s_EditorPauseStartFrame = LogicFrameRuntime.CurrentFrame;
        s_EditorPauseReleaseTime = EditorApplication.timeSinceStartup + EditorPauseDurationSeconds;
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForEditorPauseResume);
        EditorApplication.isPaused = true;
    }

    private static void DiagnoseEditorPauseResume()
    {
        if (!s_EditorPauseResumeRequested)
        {
            if (!EditorApplication.isPaused)
                throw new InvalidOperationException("LvTest diagnostic expected Unity Editor to remain paused.");
            if (EditorApplication.timeSinceStartup < s_EditorPauseReleaseTime)
                return;
            if (LogicFrameRuntime.CurrentFrame != s_EditorPauseStartFrame)
            {
                throw new InvalidOperationException(
                    $"Logic frame advanced while Unity Editor was paused. before={s_EditorPauseStartFrame}, current={LogicFrameRuntime.CurrentFrame}.");
            }

            s_EditorPauseResumeRequestFrame = LogicFrameRuntime.CurrentFrame;
            s_EditorPauseResumeRequested = true;
            EditorApplication.isPaused = false;
            return;
        }

        if (!s_EditorPauseRebaseObserved)
        {
            if (EditorApplication.timeSinceStartup < s_EditorPauseReleaseTime + EditorPauseResumeTimeoutSeconds)
                return;
            throw new TimeoutException("Logic clock did not report an Editor pause realtime rebase after resume.");
        }
        if (s_EditorPauseCatchUpCount != 0)
        {
            throw new InvalidOperationException(
                $"Logic clock executed {s_EditorPauseCatchUpCount} catch-up batches on the first frame after Editor resume.");
        }

        s_EditorPauseFirstFrameValidated = true;
        s_Report.Append("PAUSE beforeFrame=").Append(s_EditorPauseStartFrame)
            .Append(" resumeRequestFrame=").Append(s_EditorPauseResumeRequestFrame)
            .Append(" rebaseLogFrame=").Append(s_EditorPauseRebaseLogFrame)
            .Append(" catchUps=").Append(s_EditorPauseCatchUpCount)
            .AppendLine();
        PreparePendingNavigationRebuild();
        s_RuntimeIssueDeadline = EditorApplication.timeSinceStartup + RuntimeIssueTimeoutSeconds;
        PhaseManager.SwitchToPhase(GamePhase.Defend);
        WriteResult(s_Report.ToString());
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForRuntimeIssues);
    }

    private static void DiagnoseRuntimeIssues()
    {
        if (PhaseManager.CurrentPhase != GamePhase.Defend)
        {
            if (EditorApplication.timeSinceStartup >= s_RuntimeIssueDeadline)
                throw new TimeoutException("LvTest did not enter the Defend phase before the runtime diagnostic timeout.");
            return;
        }

        ObserveEnemyAttacksAndViewScales();
        if (s_NavigationReadyObserved && s_NavigationReadyWorldCount != ExpectedNavigationWorldCount)
        {
            throw new InvalidOperationException(
                $"Defend navigation completion rebuilt {s_NavigationReadyWorldCount} worlds; expected {ExpectedNavigationWorldCount}.");
        }

        if (!s_NavigationReadyObserved
            || !s_DefendSpawnObserved
            || !s_SpawnSpeedReleaseObserved
            || !s_EnemyAttackObserved
            || s_UnboundEnemyViewFirstSeen.Count != 0)
        {
            if (EditorApplication.timeSinceStartup < s_RuntimeIssueDeadline)
                return;
            throw new TimeoutException(
                $"LvTest runtime issue diagnostic timed out. navigationReady={s_NavigationReadyObserved}, "
                + $"navigationWorlds={s_NavigationReadyWorldCount}, spawn={s_DefendSpawnObserved}, "
                + $"speedRelease={s_SpawnSpeedReleaseObserved}, attack={s_EnemyAttackObserved}, "
                + $"enemyViews={s_RuntimeEnemyViewCount}.");
        }

        StopRuntimeLogCapture();
        s_Report.Append("RUNTIME navigationReadyWorlds=").Append(s_NavigationReadyWorldCount)
            .Append(" defendSpawnObserved=").Append(s_DefendSpawnObserved)
            .Append(" speedReleaseObserved=").Append(s_SpawnSpeedReleaseObserved)
            .Append(" enemyAttackObserved=").Append(s_EnemyAttackObserved)
            .Append(" unitRootScaleValidated=").Append(s_RuntimeEnemyViewCount)
            .AppendLine();
        s_Report.Append("finishedUtc=").AppendLine(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        s_Report.Replace("RESULT=RUNNING", "RESULT=PASS");
        WriteResult(s_Report.ToString());
        Debug.Log("AVENGE_LVTEST_BUILD_INTERACTION_DIAGNOSTIC_PASS " + s_Report);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        EditorApplication.isPlaying = false;
    }

    private static void CaptureEnemyAttackBaselines()
    {
        s_InitialEnemyAttackCounts.Clear();
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity == null || !entity.Alive || entity.Side != SideType.EnemySide || entity.CharacterData == null)
                continue;
            if (entity.AtkComp is DirectAtkComp attack)
                s_InitialEnemyAttackCounts[entity.LogicEntityId.Value] = attack.AttackCount;
        }
    }

    private static void PreparePendingNavigationRebuild()
    {
        Vector3 center = new Vector3(10.35f, 0f, 5.35f);
        Vector3 halfExtents = new Vector3(0.04f, 0f, 0.04f);
        for (int i = 0; i < DiagnosticObstacleCount; i++)
        {
            FlowFieldCrowdMovementSystem.RegisterBoxObstacle(DiagnosticObstacleIdBase + i, center, halfExtents);
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        }

        if (!FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty())
            throw new InvalidOperationException("LvTest diagnostic failed to create a pending runtime navigation rebuild before Defend.");
    }

    private static void ObserveEnemyAttacksAndViewScales()
    {
        int validatedViewCount = 0;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity == null || !entity.Alive || entity.Side != SideType.EnemySide || entity.CharacterData == null)
                continue;

            int entityId = entity.LogicEntityId.Value;
            if (entity.AtkComp is DirectAtkComp attack)
            {
                if (!s_InitialEnemyAttackCounts.TryGetValue(entityId, out int initialAttackCount))
                    s_InitialEnemyAttackCounts.Add(entityId, attack.AttackCount);
                else if (attack.AttackCount > initialAttackCount)
                    s_EnemyAttackObserved = true;
            }

            if (!LogicEntityLifecycleService.TryGetBoundView(entity.LogicEntityId, out MAEntity view) || view == null)
            {
                double now = EditorApplication.timeSinceStartup;
                if (!s_UnboundEnemyViewFirstSeen.TryGetValue(entityId, out double firstSeen))
                    s_UnboundEnemyViewFirstSeen.Add(entityId, now);
                else if (now - firstSeen >= ViewBindingTimeoutSeconds)
                    throw new InvalidOperationException($"Enemy unit view was not bound within {ViewBindingTimeoutSeconds:F1}s. entity={entityId}.");
                continue;
            }
            s_UnboundEnemyViewFirstSeen.Remove(entityId);
            Vector3 rootScale = view.transform.localScale;
            if ((rootScale - Vector3.one).sqrMagnitude > 0.000001f)
            {
                throw new InvalidOperationException(
                    $"Enemy unit root scale was changed by runtime logic. entity={entityId}, key={entity.CharacterKey}, scale={rootScale}.");
            }
            validatedViewCount++;
        }
        s_RuntimeEnemyViewCount = Math.Max(s_RuntimeEnemyViewCount, validatedViewCount);
    }

    private static void OnRuntimeLog(string condition, string stackTrace, LogType type)
    {
        if (s_EditorPauseResumeRequested && !s_EditorPauseFirstFrameValidated)
        {
            if (condition.Contains("[LogicFrame] Editor pause ended. Rebased realtime without catch-up", StringComparison.Ordinal))
            {
                s_EditorPauseRebaseObserved = true;
                s_EditorPauseRebaseLogFrame = LogicFrameRuntime.CurrentFrame;
            }
            else if (condition.Contains("[LogicFrame] Catch-up executed", StringComparison.Ordinal))
            {
                s_EditorPauseCatchUpCount++;
            }
        }

        const string navigationPrefix = "[PhaseNavigation] defend.navigation-ready worlds=";
        if (condition.StartsWith(navigationPrefix, StringComparison.Ordinal))
        {
            int valueEnd = condition.IndexOf(',', navigationPrefix.Length);
            string value = valueEnd >= 0
                ? condition.Substring(navigationPrefix.Length, valueEnd - navigationPrefix.Length)
                : condition.Substring(navigationPrefix.Length);
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out s_NavigationReadyWorldCount))
                throw new FormatException($"Cannot parse navigation world count from log: {condition}");
            s_NavigationReadyObserved = true;
        }
        if (condition.Contains("[DefendPhase] SpawnEvent", StringComparison.Ordinal))
            s_DefendSpawnObserved = true;
        if (condition.Contains("[DefendPhase] Released spawn speed on authoritative visibility", StringComparison.Ordinal))
            s_SpawnSpeedReleaseObserved = true;
    }

    private static void ResetRuntimeIssueState()
    {
        StopRuntimeLogCapture();
        s_InitialEnemyAttackCounts.Clear();
        s_UnboundEnemyViewFirstSeen.Clear();
        s_Report = null;
        s_RuntimeIssueDeadline = 0.0;
        s_DefendSpawnObserved = false;
        s_SpawnSpeedReleaseObserved = false;
        s_EnemyAttackObserved = false;
        s_NavigationReadyObserved = false;
        s_NavigationReadyWorldCount = 0;
        s_RuntimeEnemyViewCount = 0;
        s_EditorPauseReleaseTime = 0.0;
        s_EditorPauseStartFrame = 0;
        s_EditorPauseResumeRequestFrame = 0;
        s_EditorPauseRebaseLogFrame = 0;
        s_EditorPauseResumeRequested = false;
        s_EditorPauseRebaseObserved = false;
        s_EditorPauseFirstFrameValidated = false;
        s_EditorPauseCatchUpCount = 0;
    }

    private static void StopRuntimeLogCapture()
    {
        Application.logMessageReceived -= OnRuntimeLog;
    }

    private static string CollectUnlockedArchetypes()
    {
        var names = new List<string>();
        foreach (Archetype archetype in Enum.GetValues(typeof(Archetype)))
        {
            if (archetype == Archetype.None)
                continue;
            string techId = $"Tech_BaseBuilt_{archetype}_Lv1";
            if (InGameDataModel.HasUnlockedTech(techId, EntitySideHelper.PlayerFactionId))
                names.Add(archetype.ToString());
        }
        return names.Count == 0 ? "<none>" : string.Join(",", names);
    }

    private static string JoinCandidateIds(IReadOnlyList<BuildingData> candidates)
    {
        if (candidates == null || candidates.Count == 0)
            return "<none>";
        var ids = new string[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
            ids[i] = candidates[i]?.Identifier ?? "<null>";
        return string.Join(",", ids);
    }

    private static bool TryConsumeRunRequest()
    {
        string requestPath = GetProjectPath(RequestRelativePath);
        if (!File.Exists(requestPath))
            return false;
        File.Delete(requestPath);
        return true;
    }

    private static void WriteResult(string content)
    {
        string resultPath = GetProjectPath(ResultRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
        File.WriteAllText(resultPath, content, new UTF8Encoding(false));
    }

    private static string GetProjectPath(string relativePath)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
    }
}
