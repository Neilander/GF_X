using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using GameFramework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityGameFramework.Runtime;

[InitializeOnLoad]
internal static class Lv2PullChasePerformanceRunner
{
    private const string LaunchScenePath = "Assets/AAAGame/Scene/Launch.unity";
    private const string ResultRelativePath = "Logs/Lv2PullChasePerformance.txt";
    private const string BuildResultRelativePath = "Logs/Lv2RuntimeDirtyBuildPerformance.txt";
    private const string RuntimeDirtyBuildBuildingId = "Buil_DDoSDevice_Lv1";
    private const string SessionPrefix = "Avenge.Lv2PullChasePerformance.";
    private const string RunningKey = SessionPrefix + "Running";
    private const string StateKey = SessionPrefix + "State";
    private const string StartedUtcKey = SessionPrefix + "StartedUtc";
    private const string BaselineStartFrameKey = SessionPrefix + "BaselineStartFrame";
    private const string InvadeScheduledFrameKey = SessionPrefix + "InvadeScheduledFrame";
    private const string TargetEntityIdKey = SessionPrefix + "TargetEntityId";
    private const string ModeKey = SessionPrefix + "Mode";
    private const string ModeStartFrameKey = SessionPrefix + "ModeStartFrame";
    private const string WaypointIndexKey = SessionPrefix + "WaypointIndex";
    private const string RetreatStartFrameKey = SessionPrefix + "RetreatStartFrame";
    private const string BuildScenarioKey = SessionPrefix + "BuildScenario";
    private const string BuildScheduledFrameKey = SessionPrefix + "BuildScheduledFrame";
    private const string BuildAppliedFrameKey = SessionPrefix + "BuildAppliedFrame";
    private const string BuildBeforeDefendScheduledFrameKey = SessionPrefix + "BuildBeforeDefendScheduledFrame";
    private const string DefenseScheduledFrameKey = SessionPrefix + "DefenseScheduledFrame";
    private const string DefenseAppliedFrameKey = SessionPrefix + "DefenseAppliedFrame";
    private const ulong BaselineTicks = 30;
    private const ulong RetreatTicks = 600;
    private const int SampleIntervalRenderFrames = 10;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);
    private static readonly List<Vector3> s_Route = new List<Vector3>();
    private static readonly List<string> s_Samples = new List<string>(2048);
    private static readonly MainThreadPerfScope[] s_ChaseScopes =
    {
        MainThreadPerfScope.LogicFrameTick,
        MainThreadPerfScope.LogicFrameListenerCallbacks,
        MainThreadPerfScope.LogicEntityNavigationSync,
        MainThreadPerfScope.FlowNavigationAgentUpdate,
        MainThreadPerfScope.FlowNavigationInactiveClear,
        MainThreadPerfScope.FlowNavigationCommit,
        MainThreadPerfScope.FlowNavigationResolveRequests,
        MainThreadPerfScope.FlowNavigationTileQueue,
        MainThreadPerfScope.FlowNavigationPortalOwners,
        MainThreadPerfScope.FlowNavigationRequestSort,
        MainThreadPerfScope.FlowNavigationDemandResolve,
        MainThreadPerfScope.FlowNavigationDemandDispatch,
        MainThreadPerfScope.FlowNavigationRequestPrune,
        MainThreadPerfScope.FlowNavigationPathAdvance,
        MainThreadPerfScope.FlowNavigationPathInitialize,
        MainThreadPerfScope.FlowNavigationPathGoalConnector,
        MainThreadPerfScope.FlowNavigationPathCreateHierarchy,
        MainThreadPerfScope.FlowNavigationPathExpandHierarchy,
        MainThreadPerfScope.FlowNavigationPathDownward,
        MainThreadPerfScope.FlowNavigationPathL0,
        MainThreadPerfScope.FlowNavigationPathMaterialize,
        MainThreadPerfScope.FlowNavigationPathComplete,
        MainThreadPerfScope.LogicEntityBrain,
        MainThreadPerfScope.LogicEntityTargeting,
        MainThreadPerfScope.LogicEntityMoveIntent,
        MainThreadPerfScope.LogicEntityMoveResolve,
        MainThreadPerfScope.CharacterMovePrepare,
        MainThreadPerfScope.FlowGroupMove,
        MainThreadPerfScope.FlowWorldBuildQueue,
        MainThreadPerfScope.FlowRuntimeRebuildQueue,
        MainThreadPerfScope.FlowTileBuildQueue,
        MainThreadPerfScope.FlowSteeringSetupPrepare,
        MainThreadPerfScope.FlowPrepareStartCell,
        MainThreadPerfScope.FlowPrepareStableGoal,
        MainThreadPerfScope.FlowPreparePathHandle,
        MainThreadPerfScope.FlowPrepareTileDemand,
        MainThreadPerfScope.FlowPrepareGoalOccupancy,
        MainThreadPerfScope.FlowPrepareWorld,
        MainThreadPerfScope.FlowPreparePathAdvance,
        MainThreadPerfScope.FlowPrepareReadDomain,
        MainThreadPerfScope.FlowPathFastValidation,
        MainThreadPerfScope.FlowPathBuildCache,
        MainThreadPerfScope.FlowPathBuildSharedGoal,
        MainThreadPerfScope.FlowPathBuildCorridorPolicy,
        MainThreadPerfScope.FlowPathBuildHierarchy,
        MainThreadPerfScope.FlowPathBuildPortalGraph,
        MainThreadPerfScope.FlowPathBuildCommittedPrefix,
        MainThreadPerfScope.FlowCorridorPolicyLookup,
        MainThreadPerfScope.FlowCorridorPolicyHierarchy,
        MainThreadPerfScope.FlowCorridorPolicyAuthorityHash,
        MainThreadPerfScope.FlowCorridorPolicyReconstruct,
        MainThreadPerfScope.FlowPathHandleCreateCache,
        MainThreadPerfScope.FlowCorridorPolicyGoalConnector,
        MainThreadPerfScope.FlowCorridorPolicyStartConnector,
        MainThreadPerfScope.FlowCorridorPolicyStartLeafAccess,
        MainThreadPerfScope.FlowCorridorPolicyStartLeafSearch,
        MainThreadPerfScope.FlowCorridorPolicyStartExtend,
        MainThreadPerfScope.FlowCorridorPolicyDownwardCustomize,
        MainThreadPerfScope.FlowCorridorPolicyReverseExpand,
        MainThreadPerfScope.FlowTileQueueActiveDemand,
        MainThreadPerfScope.FlowTileQueuePrune,
        MainThreadPerfScope.FlowTileQueueReferenceTrim,
        MainThreadPerfScope.FlowTileQueueCommit,
        MainThreadPerfScope.FlowTileQueueSharedGoal,
        MainThreadPerfScope.FlowTileQueueMovingTargetProjection,
        MainThreadPerfScope.FlowTileCommitQueueScan,
        MainThreadPerfScope.FlowTileCommitDirections,
        MainThreadPerfScope.FlowTileCommitContinuation,
        MainThreadPerfScope.FlowTileCommitDiagnosticShadow,
        MainThreadPerfScope.FlowTileCommitCache,
        MainThreadPerfScope.FlowTileCommitReferenceTrim,
        MainThreadPerfScope.FlowSteeringPath,
        MainThreadPerfScope.FlowSteeringPortalOwner,
        MainThreadPerfScope.FlowSteeringVelocity,
        MainThreadPerfScope.FlowSteeringPortalState,
        MainThreadPerfScope.FlowSteeringFunnel,
        MainThreadPerfScope.FlowSteeringGradient,
        MainThreadPerfScope.FlowSteeringIntegration,
        MainThreadPerfScope.FlowSteeringDiagnostics,
        MainThreadPerfScope.FlowSteeringFunnelGridLos,
        MainThreadPerfScope.FlowSteeringFunnelStaticSweep,
        MainThreadPerfScope.FlowSteeringDirectStatic,
        MainThreadPerfScope.FlowSteeringDirectLineOfSight,
        MainThreadPerfScope.CharacterTargetingEvaluate,
        MainThreadPerfScope.CharacterTargetingCandidateScan,
        MainThreadPerfScope.CharacterTargetingReachability,
        MainThreadPerfScope.CharacterTargetingWallDetour,
        MainThreadPerfScope.FlowAttackAreaScan,
    };
    private static readonly double[] s_ChaseScopePeakMilliseconds = new double[s_ChaseScopes.Length];
    private static readonly int[] s_ChaseScopePeakRenderFrames = new int[s_ChaseScopes.Length];
    private static readonly int[] s_ChaseScopePeakCalls = new int[s_ChaseScopes.Length];
    private static readonly List<double> s_ApproachFrameMilliseconds = new List<double>(1024);
    private static readonly List<double> s_ApproachLogicMilliseconds = new List<double>(1024);
    private static readonly List<double> s_RetreatFrameMilliseconds = new List<double>(1024);
    private static readonly List<double> s_RetreatLogicMilliseconds = new List<double>(1024);
    private static readonly Dictionary<int, FixVector2> s_LastEnemyMotionPositions = new Dictionary<int, FixVector2>();
    private static long s_LastEditorUpdateTimestamp;
    private static ulong s_LastEnemyMotionFrame;
    private static int s_LastProfilerFrame = -1;
    private static int s_LastPeriodicSampleFrame = -1;
    private static double s_MaxFrameMilliseconds;
    private static int s_MaxFrame;
    private static double s_MaxLogicMilliseconds;
    private static int s_MaxLogicFrame;
    private static string s_NavigationBeforeInvade = string.Empty;
    private static string s_NavigationAfterInvade = string.Empty;
    private static string s_LastRetreatDirection = string.Empty;
    private static bool s_CaptureActive;
    private static int s_PeakRequiredFlowTileCommits;
    private static int s_PeakFlowTileQueueMutations;
    private static int s_PeakPathPortalExpansions;
    private static int s_PeakPathRequestGroups;
    private static int s_PeakPathRequestOperations;
    private static int s_PeakPathRequestCommits;
    private static int s_PeakPathRequestSourceCommits;
    private static int s_PeakPendingPathRequestGroups;
    private static int s_PeakPendingPathRequestSources;
    private static int s_PortalTileWaitAccessSamples;
    private static int s_PortalTileWaitZeroVelocitySamples;
    private static int s_PortalTileWaitStoppedSamples;

    private enum RunnerState
    {
        WaitingForPlay = 0,
        WaitingForStartup = 1,
        WaitingForRuntime = 2,
        Baseline = 3,
        WaitingForInvade = 4,
        WaitingForTarget = 5,
        Running = 6,
        Finishing = 7,
        WaitingForBuild = 8,
        WaitingForDefense = 9,
        WaitingForBuildInvade = 10,
        WaitingForBuildBeforeDefend = 11,
    }

    private enum ScenarioMode
    {
        Approach = 0,
        Retreat = 1,
    }

    static Lv2PullChasePerformanceRunner()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
    }

    [MenuItem("Tools/Logic Frames/Run Lv2 Pull Chase Performance")]
    public static void Run()
    {
        Start(false);
    }

    [MenuItem("Tools/Logic Frames/Run Lv2 RuntimeDirty Build Performance")]
    public static void RunRuntimeDirtyBuildPerformance()
    {
        Start(true);
    }

    private static void Start(bool buildScenario)
    {
        if (SessionState.GetBool(RunningKey, false))
            throw new InvalidOperationException("Lv2 pull-chase performance runner is already running.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before starting the Lv2 pull-chase performance runner.");
        if (EditorApplication.isCompiling)
            throw new InvalidOperationException("Wait for script compilation before starting the Lv2 pull-chase performance runner.");
        if (EditorSceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Save or discard the dirty scene before starting the Lv2 pull-chase performance runner.");

        ResetCaptureState();
        string startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        SessionState.SetBool(RunningKey, true);
        SessionState.SetBool(BuildScenarioKey, buildScenario);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForPlay);
        SessionState.SetString(StartedUtcKey, startedUtc);
        MainThreadFrameProfilerNextPlayCapture.ArmNextPlay(false);
        WriteResult(
            buildScenario ? BuildResultRelativePath : ResultRelativePath,
            "RESULT=RUNNING" + Environment.NewLine + "startedUtc=" + startedUtc + Environment.NewLine);
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
            CaptureCompletedFrame();
            ValidateLogicEntityChain();
            RunnerState state = (RunnerState)SessionState.GetInt(StateKey, (int)RunnerState.WaitingForPlay);
            if (!EditorApplication.isPlaying)
            {
                if (state == RunnerState.Finishing)
                {
                    SessionState.SetBool(RunningKey, false);
                    SessionState.EraseInt(StateKey);
                }
                else if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    throw new InvalidOperationException($"Lv2 pull-chase runner left Play mode unexpectedly. state={state}.");
                }
                return;
            }

            switch (state)
            {
                case RunnerState.WaitingForPlay:
                    SessionState.SetInt(StateKey, (int)RunnerState.WaitingForStartup);
                    break;
                case RunnerState.WaitingForStartup:
                    EnterLv2();
                    break;
                case RunnerState.WaitingForRuntime:
                    BeginBaselineWhenReady();
                    break;
                case RunnerState.Baseline:
                    AdvanceBaseline();
                    break;
                case RunnerState.WaitingForInvade:
                    WaitForInvade();
                    break;
                case RunnerState.WaitingForBuild:
                    WaitForBuild();
                    break;
                case RunnerState.WaitingForBuildInvade:
                    WaitForBuildInvade();
                    break;
                case RunnerState.WaitingForBuildBeforeDefend:
                    WaitForBuildBeforeDefend();
                    break;
                case RunnerState.WaitingForDefense:
                    WaitForDefense();
                    break;
                case RunnerState.WaitingForTarget:
                    BeginChaseWhenReady();
                    break;
                case RunnerState.Running:
                    AdvanceChase();
                    break;
                case RunnerState.Finishing:
                    break;
                default:
                    throw new InvalidOperationException($"Unknown Lv2 pull-chase runner state {state}.");
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
        finally
        {
            s_LastEditorUpdateTimestamp = Stopwatch.GetTimestamp();
        }
    }

    private static void EnterLv2()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase)
            return;
        if (!EditorRuntimeLevelEntry.TryEnterWithDefaultCareer("Lv_2", out string error))
            throw new InvalidOperationException($"Cannot enter Lv_2 from Launch: {error}");
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForRuntime);
    }

    private static void BeginBaselineWhenReady()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase runtimeProcedure
            || !runtimeProcedure.IsEditorStressRuntimeReady
            || !LogicFrameRuntime.IsTimelineRunning)
            return;

        InputManager inputManager = GameEntry.GetComponent<InputManager>()
                                    ?? throw new InvalidOperationException("Lv2 pull-chase runner requires InputManager.");
        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("Lv2 pull-chase runner requires InputModel.");
        if (!inputModel.LogicTimeline.IsStarted)
            return;
        if (PhaseManager.CurrentPhase != GamePhase.BuildBeforeInvade)
            throw new InvalidOperationException($"Lv2 pull-chase runner requires BuildBeforeInvade, actual={PhaseManager.CurrentPhase}.");

        inputManager.ChangeState(InputState.Game);
        ulong frame = LogicFrameRuntime.CurrentFrame;
        SessionState.SetInt(BaselineStartFrameKey, ToSessionInt(frame));
        SessionState.SetInt(StateKey, (int)RunnerState.Baseline);
        AppendEvent("baseline-begin", frame, null);
    }

    private static void AdvanceBaseline()
    {
        ulong frame = LogicFrameRuntime.CurrentFrame;
        ulong startFrame = FromSessionInt(BaselineStartFrameKey);
        if (frame < startFrame + BaselineTicks)
            return;

        s_NavigationBeforeInvade = FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics();
        if (LogicPhaseCommandService.PendingCount != 0)
            throw new InvalidOperationException($"Lv2 pull-chase runner found {LogicPhaseCommandService.PendingCount} pending phase commands before invade.");

        if (SessionState.GetBool(BuildScenarioKey, false))
        {
            ScheduleDDoSConstruction(frame);
            SessionState.SetInt(StateKey, (int)RunnerState.WaitingForBuild);
            AppendEvent("ddos-build-scheduled", frame, FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics());
            BeginPerformanceWindow();
            return;
        }

        PhaseManager.SwitchToPhase(GamePhase.Invade);
        SessionState.SetInt(InvadeScheduledFrameKey, ToSessionInt(frame));
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForInvade);
        AppendEvent("invade-scheduled", frame, s_NavigationBeforeInvade);
    }

    private static void WaitForInvade()
    {
        ulong frame = LogicFrameRuntime.CurrentFrame;
        ulong scheduledFrame = FromSessionInt(InvadeScheduledFrameKey);
        if (PhaseManager.CurrentPhase != GamePhase.Invade)
        {
            if (frame > scheduledFrame + 5)
                throw new InvalidOperationException($"Lv2 invade did not apply within five ticks. scheduled={scheduledFrame}, current={frame}.");
            return;
        }

        s_NavigationAfterInvade = FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics();
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForTarget);
        AppendEvent("invade-applied", frame, s_NavigationAfterInvade);
    }

    private static void ScheduleDDoSConstruction(ulong currentFrame)
    {
        BuildManager buildManager = UnityGameFramework.Runtime.GameEntry.GetComponent<BuildManager>()
                                    ?? throw new InvalidOperationException("Lv2 RuntimeDirty build runner requires BuildManager.");
        IList<IEntityContext> entities = EntityRegistry.AllEntities
                                         ?? throw new InvalidOperationException("Lv2 RuntimeDirty build runner requires EntityRegistry.AllEntities.");
        IBuildingLogicContext selectedOwner = null;
        string constructionDiagnostics = string.Empty;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i]
                                    ?? throw new InvalidOperationException($"Lv2 RuntimeDirty build runner found a null entity at index {i}.");
            if (!entity.Alive
                || !entity.TryGetLogicBuilding(out IBuildingLogicContext owner)
                || owner.OwnerFactionId != EntitySideHelper.PlayerFactionId
                || owner.BuildingData == null
                || owner.BuildingData.Lv != 0)
            {
                continue;
            }

            List<BuildingData> candidates = buildManager.GetLv0ConstructCandidates(owner, requireUnlockedArche: false);
            constructionDiagnostics += $" owner={owner.LogicEntityId.Value}/{owner.BuildingData.Identifier} candidates={candidates.Count}";
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                BuildingData candidate = candidates[candidateIndex];
                if (candidate == null)
                    throw new InvalidOperationException($"Lv2 RuntimeDirty build runner encountered a null candidate for owner {owner.LogicEntityId.Value} at index {candidateIndex}.");
                constructionDiagnostics += $" [{candidate.Identifier}:visible={buildManager.IsConstructOptionVisible(owner, candidate.Identifier)},condition={buildManager.SatisfyBuildCondition(candidate, owner.OwnerFactionId)},affordable={buildManager.HasBuildCost(candidate.Identifier, owner)}]";
                if (!string.Equals(candidate.Identifier, RuntimeDirtyBuildBuildingId, StringComparison.Ordinal))
                    continue;
                if (!buildManager.IsConstructOptionExecutable(owner, candidate.Identifier))
                {
                    constructionDiagnostics += $" ddosExecutable=false cost={buildManager.GetBuildingCost(candidate, owner)}";
                    continue;
                }
                selectedOwner = owner;
                break;
            }
            if (selectedOwner != null)
                break;
        }

        if (selectedOwner == null)
        {
            throw new InvalidOperationException(
                $"Lv2 RuntimeDirty build runner found no executable {RuntimeDirtyBuildBuildingId} option." + constructionDiagnostics);
        }
        if (!buildManager.ConstructBuilding(selectedOwner, RuntimeDirtyBuildBuildingId))
            throw new InvalidOperationException($"Lv2 RuntimeDirty build runner failed to submit {RuntimeDirtyBuildBuildingId} construction.");

        SessionState.SetInt(BuildScheduledFrameKey, ToSessionInt(currentFrame));
    }

    private static void WaitForBuild()
    {
        ulong frame = LogicFrameRuntime.CurrentFrame;
        ulong scheduledFrame = FromSessionInt(BuildScheduledFrameKey);
        IEntityContext built = ResolvePlayerBuilding(RuntimeDirtyBuildBuildingId);
        if (built == null)
        {
            if (frame > scheduledFrame + 180)
                throw new InvalidOperationException($"Lv2 RuntimeDirty build runner did not observe {RuntimeDirtyBuildBuildingId} after 180 ticks. frame={frame}.");
            return;
        }

        if (SessionState.GetInt(BuildAppliedFrameKey, 0) == 0)
        {
            SessionState.SetInt(BuildAppliedFrameKey, ToSessionInt(frame));
            AppendEvent("ddos-build-applied", frame, $"entity={built.LogicEntityId.Value}/{built.CharacterKey}");
        }

        if (FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty())
            return;

        if (PhaseManager.CurrentPhase != GamePhase.BuildBeforeInvade)
            throw new InvalidOperationException($"Lv2 RuntimeDirty build runner expected BuildBeforeInvade before invade, actual={PhaseManager.CurrentPhase}.");
        PhaseManager.SwitchToPhase(GamePhase.Invade);
        SessionState.SetInt(InvadeScheduledFrameKey, ToSessionInt(frame));
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForBuildInvade);
        AppendEvent("invade-after-build-scheduled", frame, FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics());
    }

    private static void WaitForBuildInvade()
    {
        ulong frame = LogicFrameRuntime.CurrentFrame;
        ulong scheduledFrame = FromSessionInt(InvadeScheduledFrameKey);
        if (PhaseManager.CurrentPhase != GamePhase.Invade)
        {
            if (frame > scheduledFrame + 30)
                throw new InvalidOperationException($"Lv2 RuntimeDirty build runner invade-after-build did not apply within 30 ticks. scheduled={scheduledFrame}, current={frame}.");
            return;
        }

        PhaseManager.SwitchToPhase(GamePhase.BuildBeforeDefend);
        SessionState.SetInt(BuildBeforeDefendScheduledFrameKey, ToSessionInt(frame));
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForBuildBeforeDefend);
        AppendEvent("build-before-defend-scheduled", frame, FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics());
    }

    private static void WaitForBuildBeforeDefend()
    {
        ulong frame = LogicFrameRuntime.CurrentFrame;
        ulong scheduledFrame = FromSessionInt(BuildBeforeDefendScheduledFrameKey);
        if (PhaseManager.CurrentPhase != GamePhase.BuildBeforeDefend)
        {
            if (frame > scheduledFrame + 30)
                throw new InvalidOperationException($"Lv2 RuntimeDirty build runner BuildBeforeDefend did not apply within 30 ticks. scheduled={scheduledFrame}, current={frame}.");
            return;
        }

        PhaseManager.SwitchToPhase(GamePhase.Defend);
        SessionState.SetInt(DefenseScheduledFrameKey, ToSessionInt(frame));
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForDefense);
        AppendEvent("defense-scheduled", frame, FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics());
    }

    private static void WaitForDefense()
    {
        ulong frame = LogicFrameRuntime.CurrentFrame;
        ulong scheduledFrame = FromSessionInt(DefenseScheduledFrameKey);
        if (PhaseManager.CurrentPhase != GamePhase.Defend)
        {
            if (frame > scheduledFrame + 30)
                throw new InvalidOperationException($"Lv2 RuntimeDirty build runner defense switch did not apply within 30 ticks. scheduled={scheduledFrame}, actual={PhaseManager.CurrentPhase}.");
            return;
        }
        if (SessionState.GetInt(DefenseAppliedFrameKey, 0) == 0)
        {
            SessionState.SetInt(DefenseAppliedFrameKey, ToSessionInt(frame));
            AppendEvent("defense-applied", frame, FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics());
        }
        if (frame < FromSessionInt(DefenseAppliedFrameKey) + 30)
            return;
        PassBuild(frame);
    }

    private static IEntityContext ResolvePlayerBuilding(string buildingId)
    {
        IList<IEntityContext> entities = EntityRegistry.AllEntities
                                         ?? throw new InvalidOperationException("Lv2 RuntimeDirty build runner requires EntityRegistry.AllEntities.");
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i]
                                    ?? throw new InvalidOperationException($"Lv2 RuntimeDirty build runner found a null entity at index {i}.");
            if (entity.Alive
                && entity.TryGetLogicBuilding(out IBuildingLogicContext building)
                && building.OwnerFactionId == EntitySideHelper.PlayerFactionId
                && building.BuildingData != null
                && string.Equals(building.BuildingData.Identifier, buildingId, StringComparison.Ordinal))
            {
                return entity;
            }
        }
        return null;
    }

    private static void BeginChaseWhenReady()
    {
        IEntityContext hero = EntityRegistry.Player;
        if (hero == null || !hero.Alive)
            return;
        if (hero is not ILogicFrameEntity logicHero)
            throw new InvalidOperationException("Lv2 hero does not implement ILogicFrameEntity.");

        IEntityContext target = ResolveNearestEnemySoldier(hero.PositionFixed);
        if (target == null)
        {
            ulong scheduledFrame = FromSessionInt(InvadeScheduledFrameKey);
            if (LogicFrameRuntime.CurrentFrame > scheduledFrame + 180)
                throw new InvalidOperationException("Lv2 pull-chase runner found no live enemy soldier within 180 ticks of invade.");
            return;
        }

        bool pending;
        if (!FlowFieldCrowdMovementSystem.TryGetNavigationPathCornersToReachableGoalNonBlocking(
                hero.Position,
                target.Position,
                logicHero.NavigationAgentTypeId,
                s_Route,
                out string failureReason,
                out pending))
        {
            if (pending)
                return;
            throw new InvalidOperationException($"Lv2 pull-chase route failed: {failureReason}");
        }
        if (s_Route.Count < 2)
            throw new InvalidOperationException($"Lv2 pull-chase route contains {s_Route.Count} coners.");

        ulong frame = LogicFrameRuntime.CurrentFrame;
        SessionState.SetInt(TargetEntityIdKey, target.LogicEntityId.Value);
        SessionState.SetInt(ModeKey, (int)ScenarioMode.Approach);
        SessionState.SetInt(ModeStartFrameKey, ToSessionInt(frame));
        SessionState.SetInt(WaypointIndexKey, 1);
        SessionState.SetInt(StateKey, (int)RunnerState.Running);
        BeginPerformanceWindow();
        AppendEvent("approach-begin", frame, $"target={DescribeEntity(target)},routeCorners={s_Route.Count}");
        EnqueueMove(ResolveRouteDirection(hero.Position, 1));
    }

    private static void AdvanceChase()
    {
        IEntityContext hero = EntityRegistry.Player
                              ?? throw new InvalidOperationException("Lv2 pull-chase runner lost the hero.");
        int targetId = SessionState.GetInt(TargetEntityIdKey, 0);
        if (!EntityRegistry.TryGet(new LogicEntityId(targetId), out IEntityContext target) || target == null || !target.Alive)
            throw new InvalidOperationException($"Lv2 pull-chase runner lost target {targetId} before capture completed.");

        ulong frame = LogicFrameRuntime.CurrentFrame;
        ScenarioMode mode = (ScenarioMode)SessionState.GetInt(ModeKey, (int)ScenarioMode.Approach);
        CaptureEnemyMotion(frame, hero, mode);
        int waypointIndex = SessionState.GetInt(WaypointIndexKey, 0);
        if (mode == ScenarioMode.Approach)
        {
            IEntityContext engagedTarget = ResolveNearestEnemyTargetingHero(hero);
            if (engagedTarget != null)
            {
                target = engagedTarget;
                SessionState.SetInt(TargetEntityIdKey, target.LogicEntityId.Value);
                mode = ScenarioMode.Retreat;
                waypointIndex = Math.Max(0, waypointIndex - 1);
                SessionState.SetInt(ModeKey, (int)mode);
                SessionState.SetInt(ModeStartFrameKey, ToSessionInt(frame));
                SessionState.SetInt(RetreatStartFrameKey, ToSessionInt(frame));
                SessionState.SetInt(WaypointIndexKey, waypointIndex);
                AppendEvent("retreat-begin", frame, DescribeChaseState(hero, target));
            }
        }

        if (mode == ScenarioMode.Retreat)
        {
            ulong retreatStart = FromSessionInt(RetreatStartFrameKey);
            if (frame >= retreatStart + RetreatTicks)
            {
                EnqueueMove(FixVector2.Zero);
                Pass(frame, hero, target);
                return;
            }
        }
        else if (frame > FromSessionInt(ModeStartFrameKey) + 900)
        {
            throw new InvalidOperationException($"Lv2 target did not acquire the hero within 900 approach ticks. {DescribeChaseState(hero, target)}");
        }

        FixVector2 direction = AdvanceRoute(hero.Position, mode, ref waypointIndex);
        SessionState.SetInt(WaypointIndexKey, waypointIndex);
        if (mode == ScenarioMode.Retreat && direction != FixVector2.Zero)
            s_LastRetreatDirection = $"({direction.x.RawValue},{direction.y.RawValue})";
        EnqueueMove(direction);
    }

    private static FixVector2 AdvanceRoute(Vector3 heroPosition, ScenarioMode mode, ref int waypointIndex)
    {
        if (waypointIndex < 0 || waypointIndex >= s_Route.Count)
            throw new InvalidOperationException($"Invalid route waypoint {waypointIndex}/{s_Route.Count}.");

        bool passedTerminalWaypoint = false;
        while (HasReachedOrPassedWaypoint(heroPosition, waypointIndex, mode))
        {
            if (mode == ScenarioMode.Approach && waypointIndex < s_Route.Count - 1)
            {
                waypointIndex++;
                continue;
            }
            if (mode == ScenarioMode.Retreat && waypointIndex > 0)
            {
                waypointIndex--;
                continue;
            }
            passedTerminalWaypoint = true;
            break;
        }

        if (mode == ScenarioMode.Retreat && waypointIndex == 0 && passedTerminalWaypoint)
            return ReadLastRetreatDirection();
        return ResolveRouteDirection(heroPosition, waypointIndex);
    }

    private static bool HasReachedOrPassedWaypoint(Vector3 heroPosition, int waypointIndex, ScenarioMode mode)
    {
        Vector3 waypoint = s_Route[waypointIndex];
        if (HorizontalDistance(heroPosition, waypoint) <= 0.08f)
            return true;

        int previousIndex = mode == ScenarioMode.Approach ? waypointIndex - 1 : waypointIndex + 1;
        if (previousIndex < 0 || previousIndex >= s_Route.Count)
            return false;

        Vector3 segment = waypoint - s_Route[previousIndex];
        segment.y = 0f;
        Vector3 beyondWaypoint = heroPosition - waypoint;
        beyondWaypoint.y = 0f;
        return segment.sqrMagnitude > 0.000001f && Vector3.Dot(beyondWaypoint, segment) >= 0f;
    }

    private static FixVector2 ReadLastRetreatDirection()
    {
        if (string.IsNullOrEmpty(s_LastRetreatDirection))
            throw new InvalidOperationException("Lv2 pull-chase runner reached route start without a retreat direction.");
        string[] raw = s_LastRetreatDirection.Trim('(', ')').Split(',');
        return new FixVector2(
            Fix64.FromRaw(long.Parse(raw[0], CultureInfo.InvariantCulture)),
            Fix64.FromRaw(long.Parse(raw[1], CultureInfo.InvariantCulture)));
    }

    private static FixVector2 ResolveRouteDirection(Vector3 heroPosition, int waypointIndex)
    {
        Vector3 waypoint = s_Route[waypointIndex];
        var delta = new FixVector2((Fix64)(waypoint.x - heroPosition.x), (Fix64)(waypoint.z - heroPosition.z));
        return FixVector2.SqrMagnitude(delta) > Fix64.Zero ? delta.GetNormalized() : FixVector2.Zero;
    }

    private static void EnqueueMove(FixVector2 direction)
    {
        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("Lv2 pull-chase runner lost InputModel.");
        inputModel.LogicTimeline.EnqueueEditorWorldMoveForNextFrame(direction);
    }

    private static IEntityContext ResolveNearestEnemySoldier(FixVector2 heroPosition)
    {
        IEntityContext nearest = null;
        Fix64 nearestDistanceSquared = Fix64.FromRaw(long.MaxValue);
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity == null || !entity.Alive || entity.Side != SideType.EnemySide || entity.Brain is not SoldierAIBrain)
                continue;
            Fix64 distanceSquared = FixVector2.SqrMagnitude(entity.PositionFixed - heroPosition);
            if (distanceSquared >= nearestDistanceSquared)
                continue;
            nearest = entity;
            nearestDistanceSquared = distanceSquared;
        }
        return nearest;
    }

    private static IEntityContext ResolveNearestEnemyTargetingHero(IEntityContext hero)
    {
        IEntityContext nearest = null;
        Fix64 nearestDistanceSquared = Fix64.FromRaw(long.MaxValue);
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity == null
                || !entity.Alive
                || entity.Side != SideType.EnemySide
                || entity.Brain is not SoldierAIBrain
                || !ReferenceEquals(entity.TargetComp?.CurrentTarget, hero))
                continue;
            Fix64 distanceSquared = FixVector2.SqrMagnitude(entity.PositionFixed - hero.PositionFixed);
            if (distanceSquared >= nearestDistanceSquared)
                continue;
            nearest = entity;
            nearestDistanceSquared = distanceSquared;
        }
        return nearest;
    }

    private static void BeginPerformanceWindow()
    {
        Array.Clear(s_ChaseScopePeakMilliseconds, 0, s_ChaseScopePeakMilliseconds.Length);
        Array.Clear(s_ChaseScopePeakRenderFrames, 0, s_ChaseScopePeakRenderFrames.Length);
        Array.Clear(s_ChaseScopePeakCalls, 0, s_ChaseScopePeakCalls.Length);
        s_LastProfilerFrame = MainThreadFrameProfiler.LastCompletedFrame;
        s_LastPeriodicSampleFrame = s_LastProfilerFrame;
        s_MaxFrameMilliseconds = 0.0;
        s_MaxFrame = -1;
        s_MaxLogicMilliseconds = 0.0;
        s_MaxLogicFrame = -1;
        s_ApproachFrameMilliseconds.Clear();
        s_ApproachLogicMilliseconds.Clear();
        s_RetreatFrameMilliseconds.Clear();
        s_RetreatLogicMilliseconds.Clear();
        s_PeakRequiredFlowTileCommits = 0;
        s_PeakFlowTileQueueMutations = 0;
        s_PeakPathPortalExpansions = 0;
        s_PeakPathRequestGroups = 0;
        s_PeakPathRequestOperations = 0;
        s_PeakPathRequestCommits = 0;
        s_PeakPathRequestSourceCommits = 0;
        s_PeakPendingPathRequestGroups = 0;
        s_PeakPendingPathRequestSources = 0;
        s_PortalTileWaitAccessSamples = 0;
        s_PortalTileWaitZeroVelocitySamples = 0;
        s_PortalTileWaitStoppedSamples = 0;
        s_CaptureActive = true;
    }

    private static void CaptureCompletedFrame()
    {
        if (!s_CaptureActive)
            return;
        DrainNavigationPathTickDiagnostics();
        int completedFrame = MainThreadFrameProfiler.LastCompletedFrame;
        if (completedFrame < 0 || completedFrame == s_LastProfilerFrame)
            return;
        s_LastProfilerFrame = completedFrame;

        double frameMs = MainThreadFrameProfiler.LastCompletedFrameMilliseconds;
        double logicMs = MainThreadFrameProfiler.LastCompletedLogicFrameMilliseconds;
        ScenarioMode mode = (ScenarioMode)SessionState.GetInt(ModeKey, (int)ScenarioMode.Approach);
        if (mode == ScenarioMode.Approach)
        {
            s_ApproachFrameMilliseconds.Add(frameMs);
            s_ApproachLogicMilliseconds.Add(logicMs);
        }
        else
        {
            s_RetreatFrameMilliseconds.Add(frameMs);
            s_RetreatLogicMilliseconds.Add(logicMs);
        }
        s_PeakRequiredFlowTileCommits = Math.Max(
            s_PeakRequiredFlowTileCommits,
            FlowFieldCrowdMovementSystem.GetEditorTestRequiredFlowTileCommitCount());
        s_PeakFlowTileQueueMutations = Math.Max(
            s_PeakFlowTileQueueMutations,
            FlowFieldCrowdMovementSystem.GetEditorTestFlowTileQueueMutationCount());
        s_PeakPathPortalExpansions = Math.Max(
            s_PeakPathPortalExpansions,
            FlowFieldCrowdMovementSystem.GetEditorTestFramePathPortalGraphNodeExpansionCount());
        int pathRequestGroups = FlowFieldCrowdMovementSystem.GetEditorTestFrameNavigationPathRequestGroupCount();
        int pathRequestOperations = FlowFieldCrowdMovementSystem.GetEditorTestFrameNavigationPathRequestOperationCount();
        int pathRequestCommits = FlowFieldCrowdMovementSystem.GetEditorTestFrameNavigationPathRequestCommitCount();
        int pathRequestSourceCommits = FlowFieldCrowdMovementSystem.GetEditorTestFrameNavigationPathSourceCommitCount();
        int pendingPathRequestGroups = FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationPathRequestCount();
        int pendingPathRequestSources = FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationPathSourceCount();
        int pathRequestOperationQuota = FlowFieldCrowdMovementSystem.GetEditorTestNavigationPathRequestOperationQuota();
        if (pathRequestOperations > pathRequestOperationQuota)
        {
            throw new InvalidOperationException(
                $"Lv2 pull-chase path request exceeded operation quota. operations={pathRequestOperations}, quota={pathRequestOperationQuota}.");
        }
        s_PeakPathRequestGroups = Math.Max(s_PeakPathRequestGroups, pathRequestGroups);
        s_PeakPathRequestOperations = Math.Max(s_PeakPathRequestOperations, pathRequestOperations);
        s_PeakPathRequestCommits = Math.Max(s_PeakPathRequestCommits, pathRequestCommits);
        s_PeakPathRequestSourceCommits = Math.Max(s_PeakPathRequestSourceCommits, pathRequestSourceCommits);
        s_PeakPendingPathRequestGroups = Math.Max(s_PeakPendingPathRequestGroups, pendingPathRequestGroups);
        s_PeakPendingPathRequestSources = Math.Max(s_PeakPendingPathRequestSources, pendingPathRequestSources);
        CaptureChaseScopePeaks(completedFrame);
        if (frameMs > s_MaxFrameMilliseconds)
        {
            s_MaxFrameMilliseconds = frameMs;
            s_MaxFrame = completedFrame;
        }
        if (logicMs > s_MaxLogicMilliseconds)
        {
            s_MaxLogicMilliseconds = logicMs;
            s_MaxLogicFrame = completedFrame;
        }

        bool periodic = completedFrame - s_LastPeriodicSampleFrame >= SampleIntervalRenderFrames;
        double pathHandleMilliseconds = MainThreadFrameProfiler.GetLastCompletedScopeMilliseconds(MainThreadPerfScope.FlowPreparePathHandle);
        double continuationMilliseconds = MainThreadFrameProfiler.GetLastCompletedScopeMilliseconds(MainThreadPerfScope.FlowTileCommitContinuation);
        bool pathSearchExpanded = EditorApplication.isPlaying
                                  && FlowFieldCrowdMovementSystem.GetEditorTestFramePathPortalGraphNodeExpansionCount() > 0;
        bool pathRequestAdvanced = pathRequestOperations > 0 || pathRequestCommits > 0;
        if (!periodic && !pathSearchExpanded && !pathRequestAdvanced && pathHandleMilliseconds < 0.5 && continuationMilliseconds < 0.5)
            return;
        if (periodic)
            s_LastPeriodicSampleFrame = completedFrame;

        string chase = string.Empty;
        if (EditorApplication.isPlaying && LogicFrameRuntime.IsActive)
        {
            IEntityContext hero = EntityRegistry.Player;
            int targetId = SessionState.GetInt(TargetEntityIdKey, 0);
            if (hero != null
                && targetId > 0
                && EntityRegistry.TryGet(new LogicEntityId(targetId), out IEntityContext target)
                && target != null)
            {
                chase = DescribeChaseState(hero, target);
            }
        }

        string navigation = EditorApplication.isPlaying
            ? FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics()
            : string.Empty;
        string pathSearch = EditorApplication.isPlaying
            ? FlowFieldCrowdMovementSystem.GetEditorTestFramePathSearchDiagnostics()
            : string.Empty;
        string pathStages = EditorApplication.isPlaying
            ? FlowFieldCrowdMovementSystem.GetEditorTestFrameNavigationPathStageDiagnostics()
            : string.Empty;
        string navigationSyncStages = EditorApplication.isPlaying
            ? FlowFieldCrowdMovementSystem.GetEditorTestFrameNavigationSyncStageDiagnostics()
            : string.Empty;
        string startConnectors = EditorApplication.isPlaying
            ? FlowFieldCrowdMovementSystem.GetEditorTestHierarchyStartConnectorDiagnostics(completedFrame)
            : string.Empty;
        double updateGapMs = s_LastEditorUpdateTimestamp == 0
            ? 0.0
            : (Stopwatch.GetTimestamp() - s_LastEditorUpdateTimestamp) * 1000.0 / Stopwatch.Frequency;
        s_Samples.Add(
            $"sample render={completedFrame},logic={LogicFrameRuntime.CurrentFrame},state={(RunnerState)SessionState.GetInt(StateKey, 0)}," +
            $"mode={(ScenarioMode)SessionState.GetInt(ModeKey, 0)},frameMs={frameMs:F3},trackedMs={MainThreadFrameProfiler.LastCompletedTrackedMilliseconds:F3}," +
            $"untrackedMs={MainThreadFrameProfiler.LastCompletedUntrackedMilliseconds:F3},logicMs={logicMs:F3},editorGapMs={updateGapMs:F3}," +
            $"scopes=[{BuildChaseScopeSample()}],pathRequests=[groups={pathRequestGroups},operations={pathRequestOperations},quota={pathRequestOperationQuota}," +
            $"commits={pathRequestCommits},sourceCommits={pathRequestSourceCommits},pendingGroups={pendingPathRequestGroups},pendingSources={pendingPathRequestSources}]," +
            $"pathSearch=[{pathSearch}],pathStages=[{pathStages}],navigationSyncStages=[{navigationSyncStages}]," +
            $"startConnectors=[{startConnectors}],chase=[{chase}],navigation=[{navigation}]");
    }

    private static void ValidateLogicEntityChain()
    {
        if (!EditorApplication.isPlaying
            || !LogicFrameRuntime.IsTimelineRunning
            || LogicFrameRuntime.CurrentFrame == 0)
        {
            return;
        }

        if (!MAEntityLogicFrameSystem.IsActive)
        {
            throw new InvalidOperationException(
                $"Lv2 pull-chase entity chain is inactive. logicFrame={LogicFrameRuntime.CurrentFrame}.");
        }

        MAEntityLogicFrameSystem.ValidateAndGetLatestCompletedFrame();
    }

    private static void CaptureChaseScopePeaks(int completedFrame)
    {
        for (int i = 0; i < s_ChaseScopes.Length; i++)
        {
            MainThreadPerfScope scope = s_ChaseScopes[i];
            double milliseconds = MainThreadFrameProfiler.GetLastCompletedScopeMilliseconds(scope);
            if (milliseconds <= s_ChaseScopePeakMilliseconds[i])
                continue;
            s_ChaseScopePeakMilliseconds[i] = milliseconds;
            s_ChaseScopePeakRenderFrames[i] = completedFrame;
            s_ChaseScopePeakCalls[i] = MainThreadFrameProfiler.GetLastCompletedScopeCalls(scope);
        }
    }

    private static string BuildChaseScopeSample()
    {
        var builder = new System.Text.StringBuilder(256);
        for (int i = 0; i < s_ChaseScopes.Length; i++)
        {
            int calls = MainThreadFrameProfiler.GetLastCompletedScopeCalls(s_ChaseScopes[i]);
            if (calls <= 0)
                continue;
            if (builder.Length > 0)
                builder.Append(',');
            builder.Append(s_ChaseScopes[i])
                .Append('=')
                .Append(MainThreadFrameProfiler.GetLastCompletedScopeMilliseconds(s_ChaseScopes[i]).ToString("F3", CultureInfo.InvariantCulture))
                .Append("ms/")
                .Append(calls);
        }
        return builder.Length > 0 ? builder.ToString() : "none";
    }

    private static string BuildChaseScopePeakReport()
    {
        var lines = new string[s_ChaseScopes.Length];
        for (int i = 0; i < s_ChaseScopes.Length; i++)
        {
            lines[i] = $"scopePeak name={s_ChaseScopes[i]},milliseconds={s_ChaseScopePeakMilliseconds[i].ToString("F3", CultureInfo.InvariantCulture)}," +
                       $"render={s_ChaseScopePeakRenderFrames[i]},calls={s_ChaseScopePeakCalls[i]}";
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildDistributionReport(string name, List<double> samples)
    {
        if (samples.Count == 0)
            return $"distribution name={name},count=0";

        double[] ordered = samples.ToArray();
        Array.Sort(ordered);
        return $"distribution name={name},count={ordered.Length}," +
               $"p50={Percentile(ordered, 0.50).ToString("F3", CultureInfo.InvariantCulture)}," +
               $"p95={Percentile(ordered, 0.95).ToString("F3", CultureInfo.InvariantCulture)}," +
               $"p99={Percentile(ordered, 0.99).ToString("F3", CultureInfo.InvariantCulture)}," +
               $"max={ordered[ordered.Length - 1].ToString("F3", CultureInfo.InvariantCulture)}";
    }

    private static double Percentile(double[] ordered, double percentile)
    {
        if (ordered == null || ordered.Length == 0)
            throw new ArgumentException("Percentile requires at least one ordered sample.", nameof(ordered));
        int index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
        return ordered[Math.Max(0, Math.Min(ordered.Length - 1, index))];
    }

    private static string DescribeChaseState(IEntityContext hero, IEntityContext target)
    {
        int enemyCount = 0;
        int heroTargetCount = 0;
        int combatCount = 0;
        int returningCount = 0;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity == null || !entity.Alive || entity.Side != SideType.EnemySide || entity.Brain is not SoldierAIBrain brain)
                continue;
            enemyCount++;
            if (ReferenceEquals(entity.TargetComp?.CurrentTarget, hero))
                heroTargetCount++;
            if (brain.State == SoldierAIBrain.SoldierState.Combat)
                combatCount++;
            if (brain.State == SoldierAIBrain.SoldierState.Returning)
                returningCount++;
        }
        return $"hero={hero.PositionFixed},target={DescribeEntity(target)},targetState={((SoldierAIBrain)target.Brain).State}," +
               $"targetIsHero={ReferenceEquals(target.TargetComp?.CurrentTarget, hero)},enemy={enemyCount},heroTargets={heroTargetCount},combat={combatCount},returning={returningCount}";
    }

    private static string DescribeEntity(IEntityContext entity)
    {
        return $"{entity.LogicEntityId.Value}/{entity.CharacterKey}@{entity.PositionFixed}";
    }

    private static void CaptureEnemyMotion(ulong frame, IEntityContext hero, ScenarioMode mode)
    {
        if (frame == s_LastEnemyMotionFrame)
            return;
        if (frame < s_LastEnemyMotionFrame)
            throw new InvalidOperationException($"Lv2 enemy motion frame moved backwards. previous={s_LastEnemyMotionFrame}, current={frame}.");
        s_LastEnemyMotionFrame = frame;

        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity == null
                || !entity.Alive
                || entity.Side != SideType.EnemySide
                || entity.Brain is not SoldierAIBrain brain
                || (brain.State != SoldierAIBrain.SoldierState.Combat
                    && brain.State != SoldierAIBrain.SoldierState.Returning
                    && !ReferenceEquals(entity.TargetComp?.CurrentTarget, hero)))
            {
                continue;
            }

            int entityId = entity.LogicEntityId.Value;
            FixVector2 position = entity.PositionFixed;
            bool hasPrevious = s_LastEnemyMotionPositions.TryGetValue(entityId, out FixVector2 previousPosition);
            FixVector2 displacement = hasPrevious ? position - previousPosition : FixVector2.Zero;
            s_LastEnemyMotionPositions[entityId] = position;

            FixVector2 moveTarget = FixVector2.Zero;
            bool hasMoveTarget = entity.MoveComp is CharacterMoveComp moveComp
                                 && moveComp.TryGetNavigationTargetFixed(out moveTarget);
            string collision = "unavailable";
            if (LogicAgentCollisionShadowService.LastCompletedFrame == frame)
            {
                LogicAgentCollisionShadowState state = LogicAgentCollisionShadowService.GetRequiredState(
                    entity.LogicEntityId,
                    frame);
                collision =
                    $"proposedRaw=({state.ProposedPosition.x.RawValue},{state.ProposedPosition.y.RawValue})" +
                    $"/pairRaw=({state.PairCorrection.x.RawValue},{state.PairCorrection.y.RawValue})" +
                    $"/staticRaw=({state.StaticCorrection.x.RawValue},{state.StaticCorrection.y.RawValue})" +
                    $"/regionRaw=({state.RegionCorrection.x.RawValue},{state.RegionCorrection.y.RawValue})" +
                    $"/finalRaw=({state.FinalResolvedPosition.x.RawValue},{state.FinalResolvedPosition.y.RawValue})" +
                    $"/staticContact={state.StaticContactKind}/regionFailure={state.RegionConstraintFailure}";
            }

            string navigation = FlowFieldCrowdMovementSystem.GetEditorTestAgentMotionDiagnostics(entityId);
            bool isPortalTileWait = navigation.Contains("/goalKind=Portal/", StringComparison.Ordinal)
                                    && navigation.Contains("/cached=false/", StringComparison.Ordinal);
            if (isPortalTileWait
                && navigation.Contains("/lastResult=tile-pending-portal-access", StringComparison.Ordinal))
            {
                s_PortalTileWaitAccessSamples++;
                if (navigation.Contains("/lastVelocityRaw=(0,0)", StringComparison.Ordinal))
                    s_PortalTileWaitZeroVelocitySamples++;
            }
            if (isPortalTileWait
                && navigation.Contains("/lastResult=pending-navigation-current-tile", StringComparison.Ordinal))
            {
                s_PortalTileWaitStoppedSamples++;
            }
            s_Samples.Add(
                $"enemyMotion logic={frame},mode={mode},id={entityId},state={brain.State},attack={brain.Attack}," +
                $"target={entity.TargetComp?.CurrentTarget?.LogicEntityId.Value.ToString() ?? "null"}," +
                $"positionRaw=({position.x.RawValue},{position.y.RawValue})," +
                $"displacementRaw=({displacement.x.RawValue},{displacement.y.RawValue}),hasPrevious={hasPrevious}," +
                $"isMoving={entity.MoveComp?.IsMoving ?? false},hasMoveTarget={hasMoveTarget}," +
                $"moveTargetRaw={(hasMoveTarget ? $"({moveTarget.x.RawValue},{moveTarget.y.RawValue})" : "null")}," +
                $"collision=[{collision}],navigation=[{navigation}]");
        }
    }

    private static void AppendEvent(string name, ulong logicFrame, string detail)
    {
        s_Samples.Add($"event name={name},render={Time.frameCount},logic={logicFrame},detail=[{detail ?? string.Empty}]");
    }

    private static void DrainNavigationPathTickDiagnostics()
    {
        string[] diagnostics = FlowFieldCrowdMovementSystem.DrainEditorTestNavigationPathTickDiagnostics();
        for (int i = 0; i < diagnostics.Length; i++)
            s_Samples.Add(diagnostics[i]);
    }

    private static void Pass(ulong frame, IEntityContext hero, IEntityContext target)
    {
        DrainNavigationPathTickDiagnostics();
        if (s_PeakRequiredFlowTileCommits != 0)
        {
            throw new InvalidOperationException(
                $"Lv2 pull-chase observed {s_PeakRequiredFlowTileCommits} required Flow tile commits inside steering.");
        }
        if (s_PortalTileWaitZeroVelocitySamples != 0 || s_PortalTileWaitStoppedSamples != 0)
        {
            throw new InvalidOperationException(
                $"Lv2 pull-chase observed navigation-induced portal tile waiting stops. " +
                $"accessSamples={s_PortalTileWaitAccessSamples}, zeroVelocity={s_PortalTileWaitZeroVelocitySamples}, " +
                $"pendingCurrentTile={s_PortalTileWaitStoppedSamples}.");
        }

        string report =
            "RESULT=PASS" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            "finalLogicFrame=" + frame + Environment.NewLine +
            "maxFrameMs=" + s_MaxFrameMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + Environment.NewLine +
            "maxFrame=" + s_MaxFrame + Environment.NewLine +
            "maxLogicMs=" + s_MaxLogicMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + Environment.NewLine +
            "maxLogicRenderFrame=" + s_MaxLogicFrame + Environment.NewLine +
            "peakRequiredFlowTileCommits=" + s_PeakRequiredFlowTileCommits + Environment.NewLine +
            "peakFlowTileQueueMutations=" + s_PeakFlowTileQueueMutations + Environment.NewLine +
            "peakPathPortalExpansions=" + s_PeakPathPortalExpansions + Environment.NewLine +
            "pathRequestOperationQuota=" + FlowFieldCrowdMovementSystem.GetEditorTestNavigationPathRequestOperationQuota() + Environment.NewLine +
            "peakPathRequestGroups=" + s_PeakPathRequestGroups + Environment.NewLine +
            "peakPathRequestOperations=" + s_PeakPathRequestOperations + Environment.NewLine +
            "peakPathRequestCommits=" + s_PeakPathRequestCommits + Environment.NewLine +
            "peakPathRequestSourceCommits=" + s_PeakPathRequestSourceCommits + Environment.NewLine +
            "peakPendingPathRequestGroups=" + s_PeakPendingPathRequestGroups + Environment.NewLine +
            "peakPendingPathRequestSources=" + s_PeakPendingPathRequestSources + Environment.NewLine +
            "portalTileWaitAccessSamples=" + s_PortalTileWaitAccessSamples + Environment.NewLine +
            "portalTileWaitZeroVelocitySamples=" + s_PortalTileWaitZeroVelocitySamples + Environment.NewLine +
            "portalTileWaitStoppedSamples=" + s_PortalTileWaitStoppedSamples + Environment.NewLine +
            BuildDistributionReport("approach-frame", s_ApproachFrameMilliseconds) + Environment.NewLine +
            BuildDistributionReport("approach-logic", s_ApproachLogicMilliseconds) + Environment.NewLine +
            BuildDistributionReport("retreat-frame", s_RetreatFrameMilliseconds) + Environment.NewLine +
            BuildDistributionReport("retreat-logic", s_RetreatLogicMilliseconds) + Environment.NewLine +
            "navigationBeforeInvade=" + s_NavigationBeforeInvade + Environment.NewLine +
            "navigationAfterInvade=" + s_NavigationAfterInvade + Environment.NewLine +
            "finalChase=" + DescribeChaseState(hero, target) + Environment.NewLine +
            "scopePeaks:" + Environment.NewLine + BuildChaseScopePeakReport() + Environment.NewLine +
            "samples:" + Environment.NewLine + string.Join(Environment.NewLine, s_Samples) + Environment.NewLine;
        WriteResult(ResultRelativePath, report);
        Log.Info("[Lv2PullChasePerformance] PASS. maxFrameMs={0:F3}, maxLogicMs={1:F3}, samples={2}.", s_MaxFrameMilliseconds, s_MaxLogicMilliseconds, s_Samples.Count);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        EditorApplication.isPlaying = false;
    }

    private static void PassBuild(ulong frame)
    {
        DrainNavigationPathTickDiagnostics();
        if (FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty())
            throw new InvalidOperationException("Lv2 RuntimeDirty build runner reached completion with pending navigation rebuild.");

        string report =
            "RESULT=PASS" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            "buildScheduledFrame=" + SessionState.GetInt(BuildScheduledFrameKey, 0) + Environment.NewLine +
            "buildAppliedFrame=" + SessionState.GetInt(BuildAppliedFrameKey, 0) + Environment.NewLine +
            "buildBeforeDefendScheduledFrame=" + SessionState.GetInt(BuildBeforeDefendScheduledFrameKey, 0) + Environment.NewLine +
            "defenseScheduledFrame=" + SessionState.GetInt(DefenseScheduledFrameKey, 0) + Environment.NewLine +
            "defenseAppliedFrame=" + SessionState.GetInt(DefenseAppliedFrameKey, 0) + Environment.NewLine +
            "finalLogicFrame=" + frame + Environment.NewLine +
            "maxFrameMs=" + s_MaxFrameMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + Environment.NewLine +
            "maxFrame=" + s_MaxFrame + Environment.NewLine +
            "maxLogicMs=" + s_MaxLogicMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + Environment.NewLine +
            "maxLogicRenderFrame=" + s_MaxLogicFrame + Environment.NewLine +
            "peakRequiredFlowTileCommits=" + s_PeakRequiredFlowTileCommits + Environment.NewLine +
            "peakFlowTileQueueMutations=" + s_PeakFlowTileQueueMutations + Environment.NewLine +
            "peakPathPortalExpansions=" + s_PeakPathPortalExpansions + Environment.NewLine +
            "navigationFinal=" + FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics() + Environment.NewLine +
            "scopePeaks:" + Environment.NewLine + BuildChaseScopePeakReport() + Environment.NewLine +
            "samples:" + Environment.NewLine + string.Join(Environment.NewLine, s_Samples) + Environment.NewLine;
        WriteResult(BuildResultRelativePath, report);
        Log.Info("[Lv2RuntimeDirtyBuildPerformance] PASS. maxFrameMs={0:F3}, maxLogicMs={1:F3}, samples={2}.", s_MaxFrameMilliseconds, s_MaxLogicMilliseconds, s_Samples.Count);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        EditorApplication.isPlaying = false;
    }

    private static void Fail(Exception exception)
    {
        if (EditorApplication.isPlaying)
            DrainNavigationPathTickDiagnostics();
        string report =
            "RESULT=FAIL" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            "state=" + (RunnerState)SessionState.GetInt(StateKey, 0) + Environment.NewLine +
            "navigation=" + (EditorApplication.isPlaying ? FlowFieldCrowdMovementSystem.GetEditorTestPendingNavigationWorkDiagnostics() : string.Empty) + Environment.NewLine +
            "exception=" + exception + Environment.NewLine +
            "samples:" + Environment.NewLine + string.Join(Environment.NewLine, s_Samples) + Environment.NewLine;
        WriteResult(
            SessionState.GetBool(BuildScenarioKey, false) ? BuildResultRelativePath : ResultRelativePath,
            report);
        UnityEngine.Debug.LogException(exception);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
        else
            SessionState.SetBool(RunningKey, false);
    }

    private static void ValidateTimeout()
    {
        string raw = SessionState.GetString(StartedUtcKey, string.Empty);
        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime startedUtc))
            throw new InvalidOperationException($"Invalid runner start timestamp '{raw}'.");
        if (DateTime.UtcNow - startedUtc > Timeout)
            throw new TimeoutException($"Lv2 pull-chase runner exceeded {Timeout}.");
    }

    private static void ResetCaptureState()
    {
        s_Route.Clear();
        s_Samples.Clear();
        Array.Clear(s_ChaseScopePeakMilliseconds, 0, s_ChaseScopePeakMilliseconds.Length);
        Array.Clear(s_ChaseScopePeakRenderFrames, 0, s_ChaseScopePeakRenderFrames.Length);
        Array.Clear(s_ChaseScopePeakCalls, 0, s_ChaseScopePeakCalls.Length);
        s_LastEditorUpdateTimestamp = 0;
        s_LastProfilerFrame = -1;
        s_LastPeriodicSampleFrame = -1;
        s_MaxFrameMilliseconds = 0.0;
        s_MaxFrame = -1;
        s_MaxLogicMilliseconds = 0.0;
        s_MaxLogicFrame = -1;
        s_ApproachFrameMilliseconds.Clear();
        s_ApproachLogicMilliseconds.Clear();
        s_RetreatFrameMilliseconds.Clear();
        s_RetreatLogicMilliseconds.Clear();
        s_LastEnemyMotionPositions.Clear();
        s_LastEnemyMotionFrame = 0;
        s_PeakRequiredFlowTileCommits = 0;
        s_PeakFlowTileQueueMutations = 0;
        s_PeakPathPortalExpansions = 0;
        s_PeakPathRequestGroups = 0;
        s_PeakPathRequestOperations = 0;
        s_PeakPathRequestCommits = 0;
        s_PeakPathRequestSourceCommits = 0;
        s_PeakPendingPathRequestGroups = 0;
        s_PeakPendingPathRequestSources = 0;
        s_PortalTileWaitAccessSamples = 0;
        s_PortalTileWaitZeroVelocitySamples = 0;
        s_PortalTileWaitStoppedSamples = 0;
        s_NavigationBeforeInvade = string.Empty;
        s_NavigationAfterInvade = string.Empty;
        s_LastRetreatDirection = string.Empty;
        s_CaptureActive = false;
        SessionState.SetInt(TargetEntityIdKey, 0);
        SessionState.SetInt(ModeKey, (int)ScenarioMode.Approach);
        SessionState.SetInt(BaselineStartFrameKey, 0);
        SessionState.SetInt(InvadeScheduledFrameKey, 0);
        SessionState.SetInt(RetreatStartFrameKey, 0);
        SessionState.SetInt(BuildScheduledFrameKey, 0);
        SessionState.SetInt(BuildAppliedFrameKey, 0);
        SessionState.SetInt(BuildBeforeDefendScheduledFrameKey, 0);
        SessionState.SetInt(DefenseScheduledFrameKey, 0);
        SessionState.SetInt(DefenseAppliedFrameKey, 0);
        SessionState.SetBool(BuildScenarioKey, false);
    }

    private static int ToSessionInt(ulong frame)
    {
        return checked((int)Math.Min(int.MaxValue, frame));
    }

    private static ulong FromSessionInt(string key)
    {
        return checked((ulong)SessionState.GetInt(key, 0));
    }

    private static float HorizontalDistance(Vector3 left, Vector3 right)
    {
        float x = left.x - right.x;
        float z = left.z - right.z;
        return Mathf.Sqrt(x * x + z * z);
    }

    private static void WriteResult(string relativePath, string text)
    {
        string path = Path.Combine(Directory.GetParent(Application.dataPath)?.FullName
                                   ?? throw new InvalidOperationException("Cannot resolve project root."), relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)
                                  ?? throw new InvalidOperationException("Cannot resolve result directory."));
        File.WriteAllText(path, text);
    }
}
