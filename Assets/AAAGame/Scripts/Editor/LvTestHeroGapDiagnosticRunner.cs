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
internal static class LvTestHeroGapDiagnosticRunner
{
    private const string LaunchScenePath = "Assets/AAAGame/Scene/Launch.unity";
    private const string LevelIdentifier = "LvTest";
    private const string FixtureBuildingIdentifier = "Buil_Fletcher_Lv1";
    private const string FixtureBuildingInstanceId = "lvtest-hero-gap-diagnostic-fletcher";
    private const string ResultRelativePath = "Logs/LvTestHeroGapDiagnostic.txt";
    private const string SessionPrefix = "Avenge.LvTestHeroGapDiagnostic.";
    private const string RunningKey = SessionPrefix + "Running";
    private const string StateKey = SessionPrefix + "State";
    private const int RequiredSteadyRenderFrames = 5;
    private const int MaximumProbeFrames = 64;
    private const int MaximumAlignmentFrames = 32;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);
    private static readonly List<Vector3> s_Route = new List<Vector3>();
    private static readonly StringBuilder s_Log = new StringBuilder(32768);
    private static DateTime s_DeadlineUtc;
    private static int s_SteadyRenderFrames;
    private static int s_WaypointIndex;
    private static int s_ProbeFrames;
    private static int s_ApproachFrames;
    private static int s_PreviousApproachWaypointIndex;
    private static int s_ConsecutiveNoProgressFrames;
    private static ulong s_LastInputFrame;
    private static FixVector2 s_RightMove;
    private static FixVector2 s_RightUpMove;
    private static FixVector2 s_ProbeStartPosition;
    private static FixVector2 s_PreviousProbePosition;
    private static FixVector2 s_PreviousApproachPosition;
    private static FixVector2 s_PreparationPointFixed;
    private static Fix64 s_BuildingLeftX;
    private static Fix64 s_PassedBuildingX;
    private static Fix64 s_WestReturnX;
    private static int s_FletcherObstacleStableId;
    private static bool s_FixtureBuildingRequested;
    private static bool s_RightUpPassed;
    private static bool s_RightPassed;
    private static bool s_ContactedBuilding;
    private static bool s_ContactedBoundary;

    private enum RunnerState
    {
        WaitingForPlay,
        WaitingForStartup,
        WaitingForRuntime,
        ApproachRightUp,
        AlignRightUp,
        ProbeRightUp,
        ReturnForRight,
        ProbeRight,
        Finishing,
    }

    static LvTestHeroGapDiagnosticRunner()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
    }

    [MenuItem("Tools/AAAGame/Diagnostics/Run LvTest Hero 0.8m Gap Diagnostic")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("LvTest hero gap diagnostic requires Edit Mode.");
        if (EditorApplication.isCompiling)
            throw new InvalidOperationException("Wait for script compilation before running the hero gap diagnostic.");

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
                throw new TimeoutException("LvTest hero gap diagnostic timed out in state " + state + ".");

            switch (state)
            {
                case RunnerState.WaitingForPlay:
                    SessionState.SetInt(StateKey, (int)RunnerState.WaitingForStartup);
                    break;
                case RunnerState.WaitingForStartup:
                    EnterLevelFromLaunch();
                    break;
                case RunnerState.WaitingForRuntime:
                    BeginWhenReady();
                    break;
                case RunnerState.ApproachRightUp:
                    AdvanceApproach(state);
                    break;
                case RunnerState.ReturnForRight:
                    AdvanceReturnForRight();
                    break;
                case RunnerState.AlignRightUp:
                    AdvanceRightUpAlignment();
                    break;
                case RunnerState.ProbeRightUp:
                case RunnerState.ProbeRight:
                    AdvanceProbe(state);
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

    private static void BeginWhenReady()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase runtimeProcedure
            || !runtimeProcedure.IsEditorStressRuntimeReady
            || LogicFrameRuntime.CurrentFrame < 2)
        {
            s_SteadyRenderFrames = 0;
            return;
        }

        IEntityContext hero = EntityRegistry.Player;
        int authorityCount = LogicEntityLifecycleService.AuthorityEntityCount;
        LogicEntityState fixtureBuilding = FindFixtureBuilding();
        bool steady = hero is ILogicFrameEntity
                      && LogicEntityViewSpawnQueue.IsActive
                      && authorityCount > 0
                      && LogicEntityLifecycleService.BoundViewCount == authorityCount
                      && LogicEntityViewSpawnQueue.PendingCount == 0
                      && LogicEntityViewSpawnQueue.InFlightCount == 0
                      && LogicFrameRuntime.DeferredRealtimeSeconds == 0d
                      && (!s_FixtureBuildingRequested
                          || (fixtureBuilding != null
                              && fixtureBuilding.IsLogicActive
                              && fixtureBuilding.HasBoundView
                              && !FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty()));
        s_SteadyRenderFrames = steady ? s_SteadyRenderFrames + 1 : 0;
        if (s_SteadyRenderFrames < RequiredSteadyRenderFrames)
            return;

        if (!s_FixtureBuildingRequested)
        {
            BuildManager buildManager = GameEntry.GetComponent<BuildManager>()
                                        ?? throw new InvalidOperationException("LvTest hero gap diagnostic requires BuildManager.");
            if (!buildManager.TryBuildBuildingForLevelInit(
                    FixtureBuildingIdentifier,
                    new Vector3(65f, 0f, 40f),
                    out string resolvedBuildingInstanceId,
                    FixtureBuildingInstanceId,
                    isNavigationStaticBaked: false,
                    defaultOwnerFactionId: EntitySideHelper.PlayerFactionId))
            {
                throw new InvalidOperationException("Cannot create the real Fletcher fixture for the LvTest 0.8m gap diagnostic.");
            }
            if (!string.Equals(resolvedBuildingInstanceId, FixtureBuildingInstanceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Fletcher fixture identity mismatch. expected=" + FixtureBuildingInstanceId
                    + " actual=" + resolvedBuildingInstanceId + ".");
            }

            s_FixtureBuildingRequested = true;
            s_SteadyRenderFrames = 0;
            s_Log.Append("fixtureRequested identifier=").Append(FixtureBuildingIdentifier)
                .Append(" instance=").Append(FixtureBuildingInstanceId)
                .Append(" frame=").Append(LogicFrameRuntime.CurrentFrame)
                .AppendLine();
            return;
        }
        if (fixtureBuilding == null)
            throw new InvalidOperationException("Fletcher fixture disappeared after its runtime lifecycle became steady.");

        InputManager inputManager = GameEntry.GetComponent<InputManager>()
                                    ?? throw new InvalidOperationException("LvTest hero gap diagnostic requires InputManager.");
        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("LvTest hero gap diagnostic requires InputModel.");
        if (!inputModel.LogicTimeline.IsStarted)
            return;
        Camera camera = Camera.main
                        ?? throw new InvalidOperationException("LvTest hero gap diagnostic requires the active main camera.");

        inputManager.ChangeState(InputState.Game);
        s_RightMove = InputDirTranslator.TranslateAndQuantize(Vector2.right, camera);
        s_RightUpMove = InputDirTranslator.TranslateAndQuantize(Vector2.one, camera);
        if (s_RightMove.x <= Fix64.Zero || s_RightMove.y >= Fix64.Zero)
            throw new InvalidOperationException("Right-key camera mapping is not +X/-Z: " + s_RightMove + ".");
        if (s_RightUpMove.x <= Fix64.Zero || Math.Abs(s_RightUpMove.y.RawValue) > 1)
            throw new InvalidOperationException("Right-up camera mapping is not +X: " + s_RightUpMove + ".");

        s_Log.Append("cameraY=").Append(camera.transform.eulerAngles.y.ToString("F4", CultureInfo.InvariantCulture))
            .Append(" right=").Append(s_RightMove)
            .Append(" rightUp=").Append(s_RightUpMove)
            .AppendLine();
        ValidateFletcherObstacle(hero, (ILogicFrameEntity)hero, fixtureBuilding);
        BeginApproach(hero, RunnerState.ApproachRightUp);
    }

    private static LogicEntityState FindFixtureBuilding()
    {
        LogicEntityState result = null;
        for (int i = 0; i < EntityRegistry.AllEntities.Count; i++)
        {
            if (EntityRegistry.AllEntities[i] is not LogicEntityState candidate
                || !string.Equals(candidate.BuildingInstanceId, FixtureBuildingInstanceId, StringComparison.Ordinal))
            {
                continue;
            }
            if (result != null)
                throw new InvalidOperationException("LvTest hero gap diagnostic found duplicate Fletcher fixture identities.");
            result = candidate;
        }
        return result;
    }

    private static void ValidateFletcherObstacle(
        IEntityContext hero,
        ILogicFrameEntity logicHero,
        LogicEntityState building)
    {
        Fix64 radius = DistanceUnitConverter.ConvertToWorld(
            hero.GetProperty(CreatureMainProperty.CollisionRadius));
        FixVector2 expectedCenter = new FixVector2((Fix64)65, (Fix64)40);
        Fix64 fixtureHalfExtent = Fix64.FromRaw(6144);
        FixVector2 expectedHalfExtents = new FixVector2(fixtureHalfExtent, fixtureHalfExtent);
        if (!building.IsBuildingEntity
            || !string.Equals(building.CharacterKey, FixtureBuildingIdentifier, StringComparison.Ordinal)
            || !building.BlocksLogicMovement
            || building.IsNavigationStaticBaked)
        {
            throw new InvalidOperationException(
                "Fletcher fixture logic state is invalid. entity=" + building.LogicEntityId.Value
                + " key=" + building.CharacterKey
                + " blocks=" + building.BlocksLogicMovement
                + " staticBaked=" + building.IsNavigationStaticBaked + ".");
        }
        if (building.LogicObstacleShapes.Count != 1
            || building.LogicObstacleShapes[0].Kind != LogicCombatShapeKind.AxisAlignedBox)
        {
            throw new InvalidOperationException(
                "Fletcher fixture must expose exactly one axis-aligned logic obstacle. count="
                + building.LogicObstacleShapes.Count + ".");
        }

        int obstacleId = LogicEntityObstacleId.FromBuildingCollider(building.LogicEntityId, 0);
        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestBoxObstacleFixed(
                obstacleId,
                out FixVector2 center,
                out FixVector2 halfExtents))
        {
            throw new InvalidOperationException(
                "Fletcher fixture obstacle was not committed. entity=" + building.LogicEntityId.Value
                + " obstacle=" + obstacleId + ".");
        }
        LogicCombatShape authoredShape = building.LogicObstacleShapes[0];
        if (center != authoredShape.Center || halfExtents != authoredShape.HalfExtents)
        {
            throw new InvalidOperationException(
                "Fletcher registered obstacle differs from its logic shape. registered=" + center + "/" + halfExtents
                + " logic=" + authoredShape.Center + "/" + authoredShape.HalfExtents + ".");
        }
        if (center != expectedCenter || halfExtents != expectedHalfExtents)
        {
            throw new InvalidOperationException(
                "Fletcher fixture geometry differs from the authored 0.8m scenario. center=" + center
                + " halfExtents=" + halfExtents + ".");
        }
        s_FletcherObstacleStableId = obstacleId;

        s_BuildingLeftX = center.x - halfExtents.x;
        s_PassedBuildingX = center.x + halfExtents.x;
        s_WestReturnX = s_BuildingLeftX - (Fix64)1;
        s_PreparationPointFixed = new FixVector2(s_BuildingLeftX - Fix64.FromRaw(6144), center.y - (Fix64)1);
        FixVector2 sweepStart = new FixVector2(s_BuildingLeftX - radius - Fix64.FromRaw(2048), center.y);
        bool available = LogicStaticCollisionShadowService.TrySolveFixed(
            logicHero.NavigationAgentTypeId,
            sweepStart,
            new FixVector2((Fix64)3, Fix64.Zero),
            radius,
            LogicStaticCollisionSlideMode.PreserveRemainingDistance,
            out LogicStaticCollisionShadowResult collision);
        if (!available || !collision.SolveResult.Success)
            throw new InvalidOperationException("Fletcher runtime obstacle probe could not be solved.");
        if (collision.ContactKind != LogicStaticCollisionContactKind.RuntimeObstacle
            || collision.RuntimeObstacleStableId != s_FletcherObstacleStableId)
        {
            throw new InvalidOperationException(
                "Fletcher runtime obstacle probe hit the wrong authority. contact=" + collision.ContactKind
                + " obstacle=" + collision.RuntimeObstacleStableId + ".");
        }

        s_ContactedBuilding = collision.RuntimeObstacleStableId == s_FletcherObstacleStableId;
        s_Log.Append("fixture entity=").Append(building.LogicEntityId.Value)
            .Append(" obstacle=").Append(s_FletcherObstacleStableId)
            .Append(" center=").Append(center)
            .Append(" halfExtents=").Append(halfExtents)
            .Append(" contact=").Append(collision.ContactKind)
            .Append(" obstacle=").Append(collision.RuntimeObstacleStableId)
            .Append(" resolved=").Append(collision.SolveResult.ResolvedDisplacement)
            .Append(" radiusRaw=").Append(radius.RawValue)
            .AppendLine();
    }

    private static void BeginApproach(IEntityContext hero, RunnerState state)
    {
        if (hero is not ILogicFrameEntity logicHero)
            throw new InvalidOperationException("LvTest hero gap diagnostic lost the logic hero before approach.");

        Vector3 target = ToWorld(s_PreparationPointFixed);
        s_Route.Clear();
        if (!FlowFieldCrowdMovementSystem.TryGetNavigationPathCorners(
                hero.Position,
                target,
                logicHero.NavigationAgentTypeId,
                s_Route,
                out string failureReason))
        {
            throw new InvalidOperationException("Cannot route hero to the 0.8m gap start: " + failureReason);
        }
        if (s_Route.Count == 0)
            throw new InvalidOperationException("Hero gap approach route is empty.");

        s_WaypointIndex = s_Route.Count > 1 ? 1 : 0;
        s_ApproachFrames = 0;
        s_PreviousApproachWaypointIndex = s_WaypointIndex;
        s_PreviousApproachPosition = new FixVector2((Fix64)hero.Position.x, (Fix64)hero.Position.z);
        s_LastInputFrame = 0;
        s_Log.Append("approach state=").Append(state)
            .Append(" routeCount=").Append(s_Route.Count)
            .Append(" first=").Append(s_Route[0])
            .Append(" last=").Append(s_Route[s_Route.Count - 1])
            .Append(" hero=").Append(hero.Position)
            .AppendLine();
        SessionState.SetInt(StateKey, (int)state);
    }

    private static void AdvanceApproach(RunnerState state)
    {
        IEntityContext hero = EntityRegistry.Player
                              ?? throw new InvalidOperationException("LvTest hero gap diagnostic lost the hero during approach.");
        ulong frame = LogicFrameRuntime.CurrentFrame;
        if (frame == s_LastInputFrame)
            return;

        Vector3 target = ToWorld(s_PreparationPointFixed);
        FixVector2 current = new FixVector2((Fix64)hero.Position.x, (Fix64)hero.Position.z);
        while (s_WaypointIndex < s_Route.Count)
        {
            Vector3 waypoint = s_Route[s_WaypointIndex];
            bool arrived = HorizontalDistance(hero.Position, waypoint) <= 0.2f;
            bool crossed = s_ApproachFrames > 0
                           && s_PreviousApproachWaypointIndex == s_WaypointIndex
                           && HasCrossedWaypoint(s_PreviousApproachPosition, current, waypoint);
            if (!arrived && !crossed)
                break;
            s_WaypointIndex++;
        }
        Vector3 moveTarget = s_WaypointIndex < s_Route.Count ? s_Route[s_WaypointIndex] : target;
        s_ApproachFrames++;
        if (s_ApproachFrames == 1 || s_ApproachFrames % 15 == 0)
        {
            s_Log.Append("approach frame=").Append(frame)
                .Append(" count=").Append(s_ApproachFrames)
                .Append(" hero=").Append(current)
                .Append(" target=").Append(moveTarget)
                .Append(" waypoint=").Append(s_WaypointIndex)
                .AppendLine();
        }
        if (s_ApproachFrames > 30 && current == s_PreviousApproachPosition)
        {
            throw new InvalidOperationException(
                "Hero approach made no progress. state=" + state + " hero=" + current
                + " target=" + moveTarget + " waypoint=" + s_WaypointIndex + ".");
        }
        if (s_WaypointIndex >= s_Route.Count)
        {
            if (state == RunnerState.ApproachRightUp)
                BeginRightUpAlignment(hero);
            else
                BeginProbe(hero, RunnerState.ProbeRight);
            return;
        }

        s_PreviousApproachPosition = current;
        s_PreviousApproachWaypointIndex = s_WaypointIndex;
        EnqueueWorldMove(hero, moveTarget);
        s_LastInputFrame = frame;
    }

    private static void BeginRightUpAlignment(IEntityContext hero)
    {
        s_ProbeFrames = 0;
        s_ConsecutiveNoProgressFrames = 0;
        s_ProbeStartPosition = new FixVector2((Fix64)hero.Position.x, (Fix64)hero.Position.z);
        s_PreviousProbePosition = s_ProbeStartPosition;
        s_LastInputFrame = 0;
        s_Log.Append("alignmentStart frame=").Append(LogicFrameRuntime.CurrentFrame)
            .Append(" position=").Append(s_ProbeStartPosition)
            .AppendLine();
        SessionState.SetInt(StateKey, (int)RunnerState.AlignRightUp);
    }

    private static void AdvanceRightUpAlignment()
    {
        IEntityContext hero = EntityRegistry.Player
                              ?? throw new InvalidOperationException("LvTest hero gap diagnostic lost the hero during alignment.");
        ulong frame = LogicFrameRuntime.CurrentFrame;
        if (frame == s_LastInputFrame)
            return;

        FixVector2 current = new FixVector2((Fix64)hero.Position.x, (Fix64)hero.Position.z);
        if (s_ProbeFrames > 0)
            SampleCollision(hero, frame, current);
        if (s_ContactedBoundary)
        {
            if (current.x >= s_BuildingLeftX)
                throw new InvalidOperationException("Right-key alignment entered the gap before the right-up probe: " + current + ".");
            BeginProbe(hero, RunnerState.ProbeRightUp);
            return;
        }
        if (s_ProbeFrames >= MaximumAlignmentFrames)
            throw new InvalidOperationException("Right-key alignment did not reach the authored boundary. current=" + current + ".");

        EnqueueMove(s_RightMove);
        s_ProbeFrames++;
        s_LastInputFrame = frame;
    }

    private static void BeginProbe(IEntityContext hero, RunnerState state)
    {
        s_ProbeFrames = 0;
        s_ApproachFrames = 0;
        s_ConsecutiveNoProgressFrames = 0;
        s_ProbeStartPosition = new FixVector2((Fix64)hero.Position.x, (Fix64)hero.Position.z);
        s_PreviousProbePosition = s_ProbeStartPosition;
        s_LastInputFrame = 0;
        s_Log.Append("probeStart state=").Append(state)
            .Append(" frame=").Append(LogicFrameRuntime.CurrentFrame)
            .Append(" position=").Append(s_ProbeStartPosition)
            .AppendLine();
        SessionState.SetInt(StateKey, (int)state);
    }

    private static void AdvanceProbe(RunnerState state)
    {
        IEntityContext hero = EntityRegistry.Player
                              ?? throw new InvalidOperationException("LvTest hero gap diagnostic lost the hero during probe.");
        ulong frame = LogicFrameRuntime.CurrentFrame;
        if (frame == s_LastInputFrame)
            return;

        FixVector2 current = new FixVector2((Fix64)hero.Position.x, (Fix64)hero.Position.z);
        if (s_ProbeFrames > 0)
            SampleCollision(hero, frame, current);
        if (current.x > s_PassedBuildingX)
        {
            if (state == RunnerState.ProbeRightUp)
            {
                s_RightUpPassed = true;
                s_ProbeFrames = 0;
                s_LastInputFrame = 0;
                SessionState.SetInt(StateKey, (int)RunnerState.ReturnForRight);
            }
            else
            {
                s_RightPassed = true;
                Complete(current);
            }
            return;
        }
        if (s_ProbeFrames >= MaximumProbeFrames)
        {
            throw new InvalidOperationException(
                state + " failed to pass the 0.8m gap. start=" + s_ProbeStartPosition + " current=" + current + ".");
        }

        EnqueueMove(state == RunnerState.ProbeRightUp ? s_RightUpMove : s_RightMove);
        s_ProbeFrames++;
        s_LastInputFrame = frame;
    }

    private static void AdvanceReturnForRight()
    {
        IEntityContext hero = EntityRegistry.Player
                              ?? throw new InvalidOperationException("LvTest hero gap diagnostic lost the hero during return.");
        ulong frame = LogicFrameRuntime.CurrentFrame;
        if (frame == s_LastInputFrame)
            return;

        FixVector2 current = new FixVector2((Fix64)hero.Position.x, (Fix64)hero.Position.z);
        if (current.x <= s_WestReturnX)
        {
            BeginProbe(hero, RunnerState.ProbeRight);
            return;
        }
        if (s_ProbeFrames >= MaximumProbeFrames)
            throw new InvalidOperationException("Reverse right-up input did not return to the west side. current=" + current + ".");

        EnqueueMove(new FixVector2(-s_RightUpMove.x, -s_RightUpMove.y));
        s_ProbeFrames++;
        s_LastInputFrame = frame;
    }

    private static void SampleCollision(IEntityContext hero, ulong frame, FixVector2 current)
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
        if (!found.HasValue)
            throw new InvalidOperationException("Collision shadow has no hero state at frame " + frame + ".");

        LogicAgentCollisionShadowState collision = found.Value;
        if (!collision.StaticProjectionAvailable || !collision.StaticProjectionSucceeded)
            throw new InvalidOperationException("Static projection failed at frame " + frame + ".");
        s_ContactedBuilding |= collision.StaticContactKind == LogicStaticCollisionContactKind.RuntimeObstacle
                               && collision.RuntimeObstacleStableId == s_FletcherObstacleStableId;
        s_ContactedBoundary |= collision.StaticContactKind == LogicStaticCollisionContactKind.AuthoredBoundarySegment;

        bool noProgress = collision.FinalResolvedPosition == s_PreviousProbePosition && s_ProbeFrames > 2;
        s_ConsecutiveNoProgressFrames = noProgress ? s_ConsecutiveNoProgressFrames + 1 : 0;
        if (s_ConsecutiveNoProgressFrames >= 3)
            throw new InvalidOperationException("Hero made no progress for three consecutive probe frames at " + current + ".");
        s_PreviousProbePosition = collision.FinalResolvedPosition;

        s_Log.Append("frame=").Append(frame)
            .Append(" position=").Append(current)
            .Append(" proposed=").Append(collision.ProposedPosition)
            .Append(" final=").Append(collision.FinalResolvedPosition)
            .Append(" staticCorrection=").Append(collision.StaticCorrection)
            .Append(" contact=").Append(collision.StaticContactKind)
            .Append(" obstacle=").Append(collision.RuntimeObstacleStableId)
            .Append(" normal=").Append(collision.FirstHitNormal)
            .AppendLine();
    }

    private static void Complete(FixVector2 finalPosition)
    {
        if (!s_RightUpPassed || !s_RightPassed)
            throw new InvalidOperationException("Both right-up and right probes must pass the gap.");
        if (!s_ContactedBuilding || !s_ContactedBoundary)
        {
            throw new InvalidOperationException(
                "Gap passage did not observe both authorities. building=" + s_ContactedBuilding
                + " boundary=" + s_ContactedBoundary + ".");
        }

        string report = "RESULT=PASS" + Environment.NewLine
                        + "rightUpPassed=" + s_RightUpPassed + Environment.NewLine
                        + "rightPassed=" + s_RightPassed + Environment.NewLine
                        + "contactedBuilding=" + s_ContactedBuilding + Environment.NewLine
                        + "contactedBoundary=" + s_ContactedBoundary + Environment.NewLine
                        + "final=" + finalPosition + Environment.NewLine
                        + "FRAME_LOG_BEGIN" + Environment.NewLine
                        + s_Log
                        + "FRAME_LOG_END" + Environment.NewLine;
        WriteResult(report);
        Debug.Log("[LvTestHeroGapDiagnostic] PASS final=" + finalPosition);
        Finish();
    }

    private static void EnqueueWorldMove(IEntityContext hero, Vector3 target)
    {
        FixVector2 direction = new FixVector2(
            (Fix64)(target.x - hero.Position.x),
            (Fix64)(target.z - hero.Position.z));
        EnqueueMove(direction == FixVector2.Zero ? FixVector2.Zero : direction.GetNormalized());
    }

    private static void EnqueueMove(FixVector2 move)
    {
        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("LvTest hero gap diagnostic lost InputModel.");
        inputModel.LogicTimeline.EnqueueEditorWorldMoveForNextFrame(move);
    }

    private static Vector3 ToWorld(FixVector2 position)
    {
        return new Vector3((float)position.x, 0f, (float)position.y);
    }

    private static float HorizontalDistance(Vector3 left, Vector3 right)
    {
        float dx = left.x - right.x;
        float dz = left.z - right.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static bool HasCrossedWaypoint(FixVector2 previous, FixVector2 current, Vector3 waypoint)
    {
        FixVector2 target = new FixVector2((Fix64)waypoint.x, (Fix64)waypoint.z);
        return FixVector2.Dot(target - previous, target - current) <= Fix64.Zero;
    }

    private static void Fail(Exception exception)
    {
        WriteResult("RESULT=FAIL" + Environment.NewLine + exception + Environment.NewLine + s_Log);
        Debug.LogException(exception);
        Finish();
    }

    private static void Finish()
    {
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        EditorApplication.isPlaying = false;
    }

    private static void ResetRuntimeState()
    {
        s_Route.Clear();
        s_Log.Clear();
        s_DeadlineUtc = default;
        s_SteadyRenderFrames = 0;
        s_WaypointIndex = 0;
        s_ProbeFrames = 0;
        s_ApproachFrames = 0;
        s_PreviousApproachWaypointIndex = 0;
        s_ConsecutiveNoProgressFrames = 0;
        s_LastInputFrame = 0;
        s_RightMove = FixVector2.Zero;
        s_RightUpMove = FixVector2.Zero;
        s_ProbeStartPosition = FixVector2.Zero;
        s_PreviousProbePosition = FixVector2.Zero;
        s_PreviousApproachPosition = FixVector2.Zero;
        s_PreparationPointFixed = FixVector2.Zero;
        s_BuildingLeftX = Fix64.Zero;
        s_PassedBuildingX = Fix64.Zero;
        s_WestReturnX = Fix64.Zero;
        s_FletcherObstacleStableId = 0;
        s_FixtureBuildingRequested = false;
        s_RightUpPassed = false;
        s_RightPassed = false;
        s_ContactedBuilding = false;
        s_ContactedBoundary = false;
    }

    private static void WriteResult(string text)
    {
        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ResultRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}
