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
    private const string WallOnlyRequestToken = "wall";
    private const string SessionPrefix = "Avenge.LvTestBuildInteractionDiagnostic.";
    private const string RunningKey = SessionPrefix + "Running";
    private const string StateKey = SessionPrefix + "State";
    private const string StartedUtcKey = SessionPrefix + "StartedUtc";
    private const string WallOnlyKey = SessionPrefix + "WallOnly";
    private const string WallGeneratedVisualRootName = "_WallGeneratedView";
    private const string WallBrickTextureResourceName = "WallBrickTexture";
    private const double RuntimeIssueTimeoutSeconds = 90.0;
    private const double WallFocusTimeoutSeconds = 30.0;
    private const double ViewBindingTimeoutSeconds = 5.0;
    private const double EditorPauseDurationSeconds = 3.0;
    private const double EditorPauseResumeTimeoutSeconds = 5.0;
    private const int DiagnosticObstacleIdBase = 1800000000;
    private const int DiagnosticObstacleCount = 32;
    private const int ExpectedNavigationWorldCount = 3;

    private static readonly Dictionary<int, int> s_InitialEnemyAttackCounts = new();
    private static readonly Dictionary<int, double> s_UnboundEnemyViewFirstSeen = new();
    private static readonly Dictionary<int, FixVector2> s_InitialBuildingForwards = new();
    private static StringBuilder s_Report;
    private static double s_RuntimeIssueDeadline;
    private static bool s_DefendSpawnObserved;
    private static bool s_SpawnSpeedReleaseObserved;
    private static bool s_EnemyAttackObserved;
    private static bool s_NavigationReadyObserved;
    private static int s_NavigationReadyWorldCount;
    private static int s_RuntimeEnemyViewCount;
    private static int s_NavigationTopologyBeforeDefend;
    private static double s_EditorPauseReleaseTime;
    private static ulong s_EditorPauseStartFrame;
    private static ulong s_EditorPauseResumeRequestFrame;
    private static ulong s_EditorPauseRebaseLogFrame;
    private static bool s_EditorPauseResumeRequested;
    private static bool s_EditorPauseRebaseObserved;
    private static bool s_EditorPauseFirstFrameValidated;
    private static int s_EditorPauseCatchUpCount;
    private static LogicEntityId s_WallApproachTargetId;
    private static double s_WallFocusDeadline;
    private static string s_RuntimeError;

    private enum RunnerState
    {
        WaitingForPlay,
        WaitingForStartupProcedure,
        WaitingForRuntime,
        WaitingForWallFocus,
        WaitingForWallPanel,
        WaitingForEditorPauseResume,
        WaitingForRuntimeIssues,
        Finishing,
    }

    static LvTestBuildInteractionDiagnosticRunner()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        if (TryConsumeRunRequest(out bool wallOnly))
            EditorApplication.delayCall += wallOnly ? RunWallPreview : Run;
    }

    [MenuItem("Tools/Diagnostics/Run LvTest Build Interaction Diagnostic")]
    public static void Run()
    {
        SessionState.SetBool(WallOnlyKey, false);
        StartRun();
    }

    [MenuItem("Tools/Diagnostics/Run LvTest Wall Preview Diagnostic")]
    public static void RunWallPreview()
    {
        SessionState.SetBool(WallOnlyKey, true);
        StartRun();
    }

    private static void StartRun()
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
            if (!string.IsNullOrEmpty(s_RuntimeError))
                throw new InvalidOperationException("Runtime error during LvTest diagnostic:" + Environment.NewLine + s_RuntimeError);

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
                case RunnerState.WaitingForWallFocus:
                    DiagnoseWallFocus();
                    break;
                case RunnerState.WaitingForWallPanel:
                    DiagnoseWallPanel();
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
        if (GF.Procedure.CurrentProcedure is not RuntimeProcedureBase)
            return;

        if (!EditorRuntimeLevelEntry.TryEnterWithDefaultCareer(LevelIdentifier, out string errorMessage))
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
        int ordinaryLv0Count = 0;
        int playerOwnedOrdinaryCount = 0;
        int playerVisibleOrdinaryHostCount = 0;
        int foreignVisibleOrdinaryHostCount = 0;
        int wallPreviewCount = 0;
        int registeredWallPreviewCount = 0;
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
            bool isWallPreview = LogicWallRuntime.IsWallBuilding(state.BuildingData);
            if (isWallPreview)
            {
                wallPreviewCount++;
                if (LogicWallRuntime.TryGetPreviewCell(state.LogicEntityId, out _))
                    registeredWallPreviewCount++;
            }
            else
            {
                ordinaryLv0Count++;
                if (state.OwnerFactionId == EntitySideHelper.PlayerFactionId)
                    playerOwnedOrdinaryCount++;
            }

            List<BuildingData> allCandidates = buildManager.GetLv0ConstructCandidates(state, requireUnlockedArche: false);
            List<BuildingData> unlockedCandidates = buildManager.GetLv0ConstructCandidates(state, requireUnlockedArche: true);
            bool hasConstructOption = buildManager.HasConstructOption(state);
            bool logicHasVisibleOptions = LogicInteractionOptionService.HasVisibleOptions(state);
            bool hasBoundView = LogicEntityLifecycleService.TryGetBoundView(state.LogicEntityId, out MAEntity view) && view != null;
            InteractionHost host = hasBoundView ? view.GetComponent<InteractionHost>() : null;
            bool hostHasVisibleOptions = host != null && host.HasVisibleOptions();
            if (!isWallPreview && hostHasVisibleOptions)
            {
                if (state.OwnerFactionId == EntitySideHelper.PlayerFactionId)
                    playerVisibleOrdinaryHostCount++;
                else
                    foreignVisibleOrdinaryHostCount++;
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
        if (playerOwnedOrdinaryCount == 0)
            throw new InvalidOperationException("LvTest runtime contains no player-owned Lv0 building points.");
        if (playerVisibleOrdinaryHostCount != playerOwnedOrdinaryCount)
        {
            throw new InvalidOperationException(
                $"LvTest player ordinary Lv0 build permissions are incomplete. playerOwned={playerOwnedOrdinaryCount}, visibleHosts={playerVisibleOrdinaryHostCount}.");
        }
        if (foreignVisibleOrdinaryHostCount != 0)
            throw new InvalidOperationException($"LvTest exposes {foreignVisibleOrdinaryHostCount} foreign ordinary Lv0 interaction hosts.");
        if (wallPreviewCount == 0 || registeredWallPreviewCount != wallPreviewCount)
        {
            throw new InvalidOperationException(
                $"LvTest wall previews are not fully registered. entities={wallPreviewCount}, registered={registeredWallPreviewCount}.");
        }

        IEntityContext player = EntityRegistry.Player
                                ?? throw new InvalidOperationException("LvTest wall preview diagnostic requires the player hero.");
        int farHiddenWallCount = 0;
        int horizontalWallCount = 0;
        int verticalWallCount = 0;
        int cornerWallCount = 0;
        Fix64 nearestDistance = Fix64.FromRaw(long.MaxValue);
        LogicEntityId nearestPlayerWallId = default;
        ValidateWallPreviewPresentations(
            player,
            default,
            ref farHiddenWallCount,
            ref horizontalWallCount,
            ref verticalWallCount,
            ref cornerWallCount,
            ref nearestDistance,
            ref nearestPlayerWallId);
        if (farHiddenWallCount == 0)
            throw new InvalidOperationException("LvTest has no out-of-range hidden wall preview to validate.");
        if (!nearestPlayerWallId.IsValid)
            throw new InvalidOperationException("LvTest has no player-owned wall preview to approach.");

        report.Append("SUMMARY lv0=").Append(lv0Count)
            .Append(" ordinary=").Append(ordinaryLv0Count)
            .Append(" playerOwnedOrdinary=").Append(playerOwnedOrdinaryCount)
            .Append(" playerVisibleOrdinaryHosts=").Append(playerVisibleOrdinaryHostCount)
            .Append(" foreignVisibleOrdinaryHosts=").Append(foreignVisibleOrdinaryHostCount)
            .Append(" wallPreviews=").Append(wallPreviewCount)
            .Append(" registeredWallPreviews=").Append(registeredWallPreviewCount)
            .AppendLine();
        report.Append("WALL_INITIAL farHidden=").Append(farHiddenWallCount)
            .Append(" horizontal=").Append(horizontalWallCount)
            .Append(" vertical=").Append(verticalWallCount)
            .Append(" corner=").Append(cornerWallCount)
            .Append(" approachEntity=").Append(nearestPlayerWallId.Value)
            .Append(" distanceRaw=").Append(nearestDistance.RawValue)
            .AppendLine();

        s_Report = report;
        CaptureBuildingForwardBaselines();
        CaptureEnemyAttackBaselines();
        Application.logMessageReceived -= OnRuntimeLog;
        Application.logMessageReceived += OnRuntimeLog;
        WriteResult(report.ToString());
        s_WallApproachTargetId = nearestPlayerWallId;
        s_WallFocusDeadline = EditorApplication.timeSinceStartup + WallFocusTimeoutSeconds;
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForWallFocus);
    }

    private static void DiagnoseWallFocus()
    {
        if (!InGameDataModel.IsBuildPhase(LogicPhaseCommandService.GetRequiredCurrentPhase()))
            throw new InvalidOperationException("Wall preview focus diagnostic left the build phase.");
        IEntityContext player = EntityRegistry.Player
                                ?? throw new InvalidOperationException("Wall preview focus diagnostic lost the player hero.");
        if (!EntityRegistry.TryGet(s_WallApproachTargetId, out IEntityContext approachTarget)
            || !approachTarget.Alive)
        {
            throw new InvalidOperationException(
                $"Wall preview approach target {s_WallApproachTargetId.Value} is unavailable.");
        }

        LogicEntityId focusedId = LogicInteractionAuthorityService.CurrentTargetId;
        if (TryResolveWallPreview(focusedId, out LogicEntityState focusedWall)
            && AreWallPreviewVisualsSynchronized(focusedId))
        {
            player.MoveExecutor.SetExternalFixed(FixVector2.Zero);
            ValidateFocusedWallPreview(player, focusedWall);
            s_Report.Append("WALL_FOCUSED entity=").Append(focusedId.Value)
                .Append(" distanceRaw=").Append(player.LogicFrameDistanceToTargetSurfaceFixed(focusedWall).RawValue)
                .Append(" visualActive=true nonFocusedActive=0")
                .AppendLine();
            WriteResult(s_Report.ToString());
            SessionState.SetInt(StateKey, (int)RunnerState.WaitingForWallPanel);
            return;
        }

        if (EditorApplication.timeSinceStartup >= s_WallFocusDeadline)
        {
            throw new TimeoutException(
                $"Hero did not naturally focus a wall preview within {WallFocusTimeoutSeconds:F0}s. "
                + $"approach={s_WallApproachTargetId.Value}, current={focusedId.Value}, "
                + $"distanceRaw={player.LogicFrameDistanceToTargetSurfaceFixed(approachTarget).RawValue}.");
        }

        FixVector2 closestPoint = LogicTargetGeometry.ClosestPoint(approachTarget, player.PositionFixed);
        FixVector2 towardWall = closestPoint - player.PositionFixed;
        Fix64 distance = FixVector2.Magnitude(towardWall);
        Fix64 stopDistance = LogicInteractionAuthorityService.EffectiveRange / (Fix64)2;
        FixVector2 velocity = distance > stopDistance
            ? towardWall.GetNormalized() * (Fix64)5
            : FixVector2.Zero;
        player.MoveExecutor.SetExternalFixed(velocity);
    }

    private static void DiagnoseWallPanel()
    {
        UIForm[] forms = GF.UI.GetAllLoadedUIForms();
        for (int i = 0; i < forms.Length; i++)
        {
            if (forms[i]?.Logic is not BuildingBuildTips buildTips
                || buildTips.TargetLogicEntityId != s_WallApproachTargetId
                || buildTips.PresentedBuildOptionCount <= 0)
            {
                continue;
            }

            s_Report.Append("WALL_PANEL type=").Append(nameof(BuildingBuildTips))
                .Append(" target=").Append(buildTips.TargetLogicEntityId.Value)
                .Append(" buildOptions=").Append(buildTips.PresentedBuildOptionCount)
                .AppendLine();
            ValidateBuildingPanelDoesNotCoverTarget(buildTips);
            WriteResult(s_Report.ToString());
            if (SessionState.GetBool(WallOnlyKey, false))
            {
                FinishSuccessfulDiagnostic("AVENGE_LVTEST_WALL_PREVIEW_DIAGNOSTIC_PASS");
                return;
            }
            BeginEditorPauseDiagnostic();
            return;
        }

        if (EditorApplication.timeSinceStartup >= s_WallFocusDeadline)
        {
            throw new TimeoutException(
                $"Focused isolated wall preview {s_WallApproachTargetId.Value} did not open BuildingBuildTips with a construction option.");
        }
    }

    private static void ValidateWallPreviewPresentations(
        IEntityContext player,
        LogicEntityId focusedId,
        ref int farHiddenWallCount,
        ref int horizontalWallCount,
        ref int verticalWallCount,
        ref int cornerWallCount,
        ref Fix64 nearestDistance,
        ref LogicEntityId nearestPlayerWallId)
    {
        Texture2D brickTexture = Resources.Load<Texture2D>(WallBrickTextureResourceName)
                                 ?? throw new InvalidOperationException(
                                     $"Wall brick texture resource '{WallBrickTextureResourceName}' is missing.");
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] is not LogicEntityState wall
                || !wall.Alive
                || wall.BuildingData == null
                || wall.BuildingData.Lv != 0
                || !LogicWallRuntime.IsWallBuilding(wall.BuildingData))
            {
                continue;
            }
            if (!LogicEntityLifecycleService.TryGetBoundView(wall.LogicEntityId, out MAEntity view) || view == null)
                throw new InvalidOperationException($"Wall preview {wall.LogicEntityId.Value} has no bound view.");
            WallBranchView wallView = view.GetComponent<WallBranchView>();
            if (wallView == null)
                throw new InvalidOperationException($"Wall preview {wall.LogicEntityId.Value} has no WallBranchView.");
            Transform visualRoot = FindWallVisualRoot(view);
            bool expectedActive = wall.LogicEntityId == focusedId;
            if (visualRoot.gameObject.activeSelf != expectedActive)
            {
                throw new InvalidOperationException(
                    $"Wall preview visibility mismatch. entity={wall.LogicEntityId.Value}, expected={expectedActive}, actual={visualRoot.gameObject.activeSelf}.");
            }

            ValidateWallMaterialAndCells(wall, visualRoot, brickTexture, out WallConnectionDirection connections);
            bool horizontal = HasHorizontal(connections);
            bool vertical = HasVertical(connections);
            if (horizontal && vertical)
                cornerWallCount++;
            else if (horizontal)
                horizontalWallCount++;
            else if (vertical)
                verticalWallCount++;
            else
                throw new InvalidOperationException($"Wall preview {wall.LogicEntityId.Value} has no path direction.");

            Fix64 distance = player.LogicFrameDistanceToTargetSurfaceFixed(wall);
            if (distance > LogicInteractionAuthorityService.EffectiveRange && !visualRoot.gameObject.activeSelf)
                farHiddenWallCount++;
            if (!LogicWallRuntime.TryGetPreviewCell(wall.LogicEntityId, out WallGridCell previewCell))
                throw new InvalidOperationException($"Wall preview {wall.LogicEntityId.Value} has no authored cell.");
            bool isIsolatedPreview = LogicWallRuntime.GetAdjacentBranches(
                previewCell,
                wall.OwnerFactionId).Count == 0;
            if (wall.OwnerFactionId == EntitySideHelper.PlayerFactionId
                && isIsolatedPreview
                && distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestPlayerWallId = wall.LogicEntityId;
            }
        }
    }

    private static void ValidateWallMaterialAndCells(
        LogicEntityState wall,
        Transform visualRoot,
        Texture2D brickTexture,
        out WallConnectionDirection previewConnections)
    {
        if (!LogicWallRuntime.TryGetPreviewCell(wall.LogicEntityId, out WallGridCell previewCell))
            throw new InvalidOperationException($"Wall preview {wall.LogicEntityId.Value} has no authored cell.");
        IReadOnlyList<WallGridCell> mergedCells = LogicWallRuntime.BuildMergedCells(previewCell, wall.OwnerFactionId);
        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            throw new InvalidOperationException($"Wall preview {wall.LogicEntityId.Value} generated no renderers.");
        for (int i = 0; i < renderers.Length; i++)
        {
            Material material = renderers[i].sharedMaterial
                                ?? throw new InvalidOperationException(
                                    $"Wall preview {wall.LogicEntityId.Value} renderer has no material.");
            Texture texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : material.mainTexture;
            if (texture != brickTexture)
                throw new InvalidOperationException($"Wall preview {wall.LogicEntityId.Value} is not using the brick texture.");
            if (Mathf.Abs(material.color.a - 0.38f) > 0.001f || material.renderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent)
            {
                throw new InvalidOperationException(
                    $"Wall preview {wall.LogicEntityId.Value} material is not transparent. alpha={material.color.a:F3}, queue={material.renderQueue}.");
            }
        }

        for (int cellIndex = 0; cellIndex < mergedCells.Count; cellIndex++)
        {
            FixVector2 center = LogicWallRuntime.GetCellWorldCenter(mergedCells[cellIndex]);
            bool represented = false;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Vector3 position = renderers[rendererIndex].transform.position;
                float dx = position.x - (float)center.x;
                float dz = position.z - (float)center.y;
                if (dx * dx + dz * dz <= 0.36f)
                {
                    represented = true;
                    break;
                }
            }
            if (!represented)
            {
                throw new InvalidOperationException(
                    $"Wall preview {wall.LogicEntityId.Value} omitted merged cell {mergedCells[cellIndex]}.");
            }
        }

        previewConnections = ResolveMergedConnections(mergedCells, previewCell)
                             | LogicWallRuntime.ResolveWallPathConnections(previewCell);
        Vector3 previewCenter = new Vector3(
            (float)LogicWallRuntime.GetCellWorldCenter(previewCell).x,
            0f,
            (float)LogicWallRuntime.GetCellWorldCenter(previewCell).y);
        int nearbyBlocks = 0;
        bool hasHorizontalShape = false;
        bool hasVerticalShape = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Vector3 position = renderers[i].transform.position;
            float dx = position.x - previewCenter.x;
            float dz = position.z - previewCenter.z;
            if (dx * dx + dz * dz > 0.36f)
                continue;
            nearbyBlocks++;
            Vector3 scale = renderers[i].transform.lossyScale;
            hasHorizontalShape |= scale.x > scale.z + 0.01f || Mathf.Abs(dx) > 0.1f;
            hasVerticalShape |= scale.z > scale.x + 0.01f || Mathf.Abs(dz) > 0.1f;
        }
        bool requiresHorizontal = HasHorizontal(previewConnections);
        bool requiresVertical = HasVertical(previewConnections);
        if (requiresHorizontal && !hasHorizontalShape || requiresVertical && !hasVerticalShape)
        {
            throw new InvalidOperationException(
                $"Wall preview {wall.LogicEntityId.Value} geometry does not match path {previewConnections}. "
                + $"horizontalShape={hasHorizontalShape}, verticalShape={hasVerticalShape}.");
        }
        if (requiresHorizontal && requiresVertical && nearbyBlocks < 3)
        {
            throw new InvalidOperationException(
                $"Wall corner preview {wall.LogicEntityId.Value} degraded to {nearbyBlocks} block(s).");
        }
    }

    private static WallConnectionDirection ResolveMergedConnections(
        IReadOnlyList<WallGridCell> cells,
        WallGridCell cell)
    {
        WallConnectionDirection result = WallConnectionDirection.None;
        for (int i = 0; i < cells.Count; i++)
        {
            WallGridCell candidate = cells[i];
            if (candidate.X == cell.X - 1 && candidate.Y == cell.Y)
                result |= WallConnectionDirection.Left;
            else if (candidate.X == cell.X + 1 && candidate.Y == cell.Y)
                result |= WallConnectionDirection.Right;
            else if (candidate.X == cell.X && candidate.Y == cell.Y - 1)
                result |= WallConnectionDirection.Down;
            else if (candidate.X == cell.X && candidate.Y == cell.Y + 1)
                result |= WallConnectionDirection.Up;
        }
        return result;
    }

    private static bool AreWallPreviewVisualsSynchronized(LogicEntityId focusedId)
    {
        int activeCount = 0;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] is not LogicEntityState wall
                || wall.BuildingData == null
                || wall.BuildingData.Lv != 0
                || !LogicWallRuntime.IsWallBuilding(wall.BuildingData))
            {
                continue;
            }
            if (!LogicEntityLifecycleService.TryGetBoundView(wall.LogicEntityId, out MAEntity view) || view == null)
                return false;
            bool active = FindWallVisualRoot(view).gameObject.activeSelf;
            if (active)
                activeCount++;
            if (wall.LogicEntityId == focusedId && !active)
                return false;
            if (wall.LogicEntityId != focusedId && active)
                return false;
        }
        return activeCount == 1;
    }

    private static void ValidateFocusedWallPreview(IEntityContext player, LogicEntityState focusedWall)
    {
        int farHidden = 0;
        int horizontal = 0;
        int vertical = 0;
        int corner = 0;
        Fix64 nearestDistance = Fix64.FromRaw(long.MaxValue);
        LogicEntityId nearestId = default;
        ValidateWallPreviewPresentations(
            player,
            focusedWall.LogicEntityId,
            ref farHidden,
            ref horizontal,
            ref vertical,
            ref corner,
            ref nearestDistance,
            ref nearestId);
        if (player.LogicFrameDistanceToTargetSurfaceFixed(focusedWall) > LogicInteractionAuthorityService.EffectiveRange)
            throw new InvalidOperationException($"Focused wall preview {focusedWall.LogicEntityId.Value} is outside interaction range.");
        if (!LogicInteractionOptionService.HasVisibleOptions(focusedWall))
            throw new InvalidOperationException($"Focused wall preview {focusedWall.LogicEntityId.Value} has no visible build option.");
    }

    private static bool TryResolveWallPreview(LogicEntityId entityId, out LogicEntityState wall)
    {
        wall = null;
        if (!entityId.IsValid || !EntityRegistry.TryGet(entityId, out IEntityContext entity))
            return false;
        wall = entity as LogicEntityState;
        return wall != null
               && wall.Alive
               && wall.BuildingData != null
               && wall.BuildingData.Lv == 0
               && LogicWallRuntime.IsWallBuilding(wall.BuildingData);
    }

    private static Transform FindWallVisualRoot(MAEntity view)
    {
        Transform root = view.transform.Find(
            EntityPresentationBindings.DisplayObjectName + "/" + WallGeneratedVisualRootName);
        return root ?? throw new InvalidOperationException(
            $"Wall preview view {view.LogicEntityId.Value} has no generated visual root.");
    }

    private static void ValidateBuildingPanelDoesNotCoverTarget(BuildingBuildTips buildTips)
    {
        var field = typeof(BuildingBuildTips).GetField(
            "varBuildPanel",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field?.GetValue(buildTips) is not GameObject panelObject)
            throw new InvalidOperationException("BuildingBuildTips has no bound varBuildPanel.");
        if (panelObject.transform is not RectTransform panel || panel.parent is not RectTransform parent)
            throw new InvalidOperationException("BuildingBuildTips panel hierarchy is invalid.");
        if (!LogicEntityLifecycleService.TryGetBoundView(s_WallApproachTargetId, out MAEntity view) || view == null)
            throw new InvalidOperationException("Focused wall has no bound view for panel overlap validation.");

        var boundsMethod = typeof(BuildingPanelScreenClamp).GetMethod(
            "CalculateTargetBounds",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (boundsMethod == null)
            throw new InvalidOperationException("Building panel target projection method is missing.");
        Rect targetRect = (Rect)boundsMethod.Invoke(null, new object[] { parent, view.transform });
        Bounds panelBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, panel);
        Rect panelRect = Rect.MinMaxRect(
            panelBounds.min.x,
            panelBounds.min.y,
            panelBounds.max.x,
            panelBounds.max.y);
        Rect overlap = Rect.MinMaxRect(
            Mathf.Max(panelRect.xMin, targetRect.xMin),
            Mathf.Max(panelRect.yMin, targetRect.yMin),
            Mathf.Min(panelRect.xMax, targetRect.xMax),
            Mathf.Min(panelRect.yMax, targetRect.yMax));
        float overlapArea = overlap.width > 0f && overlap.height > 0f
            ? overlap.width * overlap.height
            : 0f;
        s_Report.Append("WALL_PANEL_LAYOUT panel=").Append(panelRect)
            .Append(" target=").Append(targetRect)
            .Append(" overlapArea=").Append(overlapArea.ToString("F3", CultureInfo.InvariantCulture))
            .AppendLine();
        if (overlapArea > 0.01f)
            throw new InvalidOperationException(
                $"Building panel covers its wall target. panel={panelRect}, target={targetRect}, overlapArea={overlapArea:F3}.");
    }

    private static bool HasHorizontal(WallConnectionDirection connections) =>
        (connections & (WallConnectionDirection.Left | WallConnectionDirection.Right)) != 0;

    private static bool HasVertical(WallConnectionDirection connections) =>
        (connections & (WallConnectionDirection.Down | WallConnectionDirection.Up)) != 0;

    private static void FinishSuccessfulDiagnostic(string logPrefix)
    {
        StopRuntimeLogCapture();
        s_Report.Append("finishedUtc=").AppendLine(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        s_Report.Replace("RESULT=RUNNING", "RESULT=PASS");
        WriteResult(s_Report.ToString());
        Debug.Log(logPrefix + " " + s_Report);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        EditorApplication.isPlaying = false;
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
        PrepareNavigationRebuildBeforeDefend();
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
        int validatedBuildingCount = ValidateBuildingForwards(out int validatedLv0BuildingCount);
        if (s_NavigationReadyObserved && s_NavigationReadyWorldCount != ExpectedNavigationWorldCount)
        {
            throw new InvalidOperationException(
                $"Defend navigation readiness reported {s_NavigationReadyWorldCount} worlds; expected {ExpectedNavigationWorldCount}.");
        }
        if (FlowFieldCrowdMovementSystem.NavigationTopologyVersion != s_NavigationTopologyBeforeDefend)
            throw new InvalidOperationException(
                $"Defend phase changed navigation topology after readiness. before={s_NavigationTopologyBeforeDefend}, " +
                $"after={FlowFieldCrowdMovementSystem.NavigationTopologyVersion}.");

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
            .Append(" buildingForwardValidated=").Append(validatedBuildingCount)
            .Append(" lv0ForwardValidated=").Append(validatedLv0BuildingCount)
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

    private static void CaptureBuildingForwardBaselines()
    {
        s_InitialBuildingForwards.Clear();
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] is not LogicEntityState building || !building.IsBuildingEntity)
                continue;
            s_InitialBuildingForwards.Add(building.LogicEntityId.Value, building.ForwardFixed);
        }

        if (s_InitialBuildingForwards.Count == 0)
            throw new InvalidOperationException("LvTest diagnostic found no building Forward baselines before Defend.");
    }

    private static int ValidateBuildingForwards(out int lv0Count)
    {
        int count = 0;
        lv0Count = 0;
        foreach (KeyValuePair<int, FixVector2> pair in s_InitialBuildingForwards)
        {
            var entityId = new LogicEntityId(pair.Key);
            if (!EntityRegistry.TryGet(entityId, out IEntityContext context)
                || context is not LogicEntityState building
                || !building.IsBuildingEntity)
            {
                throw new InvalidOperationException($"LvTest building {pair.Key} disappeared while validating Defend Forward.");
            }
            if (building.ForwardFixed != pair.Value)
            {
                throw new InvalidOperationException(
                    $"LvTest building Forward changed in Defend. entity={pair.Key}, building={building.BuildingData.Identifier}, before={pair.Value}, after={building.ForwardFixed}.");
            }
            if (!LogicEntityLifecycleService.TryGetBoundView(entityId, out MAEntity view) || view == null)
                throw new InvalidOperationException($"LvTest building {pair.Key} has no bound View while validating Defend Forward.");

            Vector3 expected = new Vector3((float)pair.Value.x, 0f, (float)pair.Value.y).normalized;
            Vector3 actual = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            if (Vector3.Dot(expected, actual) < 0.999f)
            {
                throw new InvalidOperationException(
                    $"LvTest building View rotation diverged from authority. entity={pair.Key}, building={building.BuildingData.Identifier}, expected={expected}, actual={actual}.");
            }

            count++;
            if (building.BuildingData.Lv == 0)
                lv0Count++;
        }
        return count;
    }

    private static void PrepareNavigationRebuildBeforeDefend()
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

        try
        {
            FlowFieldCrowdMovementSystem.RequireRuntimeNavigationReady("lvtest-before-obstacle-update-complete");
            throw new InvalidOperationException("LvTest navigation readiness accepted a pending runtime obstacle rebuild.");
        }
        catch (InvalidOperationException exception)
        {
            if (!exception.Message.Contains("runtime obstacle update is pending", StringComparison.Ordinal))
                throw;
        }

        for (int i = 0; i < 4096 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        if (FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty())
            throw new InvalidOperationException("LvTest obstacle update chain did not complete navigation before Defend.");

        int readyWorldCount = FlowFieldCrowdMovementSystem.RequireRuntimeNavigationReady("lvtest-before-defend");
        if (readyWorldCount != ExpectedNavigationWorldCount)
            throw new InvalidOperationException(
                $"LvTest prepared {readyWorldCount} navigation worlds before Defend; expected {ExpectedNavigationWorldCount}.");
        s_NavigationTopologyBeforeDefend = FlowFieldCrowdMovementSystem.NavigationTopologyVersion;
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
        if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            && string.IsNullOrEmpty(s_RuntimeError))
        {
            s_RuntimeError = condition + Environment.NewLine + stackTrace;
        }

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
        if (condition.Contains("[DefendPhase] Released spawn speed.", StringComparison.Ordinal))
            s_SpawnSpeedReleaseObserved = true;
    }

    private static void ResetRuntimeIssueState()
    {
        StopRuntimeLogCapture();
        s_InitialEnemyAttackCounts.Clear();
        s_UnboundEnemyViewFirstSeen.Clear();
        s_InitialBuildingForwards.Clear();
        s_Report = null;
        s_RuntimeIssueDeadline = 0.0;
        s_DefendSpawnObserved = false;
        s_SpawnSpeedReleaseObserved = false;
        s_EnemyAttackObserved = false;
        s_NavigationReadyObserved = false;
        s_NavigationReadyWorldCount = 0;
        s_NavigationTopologyBeforeDefend = 0;
        s_RuntimeEnemyViewCount = 0;
        s_EditorPauseReleaseTime = 0.0;
        s_EditorPauseStartFrame = 0;
        s_EditorPauseResumeRequestFrame = 0;
        s_EditorPauseRebaseLogFrame = 0;
        s_EditorPauseResumeRequested = false;
        s_EditorPauseRebaseObserved = false;
        s_EditorPauseFirstFrameValidated = false;
        s_EditorPauseCatchUpCount = 0;
        s_WallApproachTargetId = default;
        s_WallFocusDeadline = 0.0;
        s_RuntimeError = null;
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

    private static bool TryConsumeRunRequest(out bool wallOnly)
    {
        wallOnly = false;
        string requestPath = GetProjectPath(RequestRelativePath);
        if (!File.Exists(requestPath))
            return false;
        string request = File.ReadAllText(requestPath);
        wallOnly = request.Contains(WallOnlyRequestToken, StringComparison.OrdinalIgnoreCase);
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
