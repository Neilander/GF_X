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
internal static class LvTestSprinterNarrowPathDiagnosticRunner
{
    private const string LaunchScenePath = "Assets/AAAGame/Scene/Launch.unity";
    private const string LevelIdentifier = "LvTest";
    private const string SprinterCharacterKey = "Unit_Sprinter";
    private const string ResultRelativePath = "Logs/LvTestSprinterNarrowPathDiagnostic.txt";
    private const string SessionPrefix = "Avenge.LvTestSprinterNarrowPathDiagnostic.";
    private const string RunningKey = SessionPrefix + "Running";
    private const string StateKey = SessionPrefix + "State";
    private const string StartedUtcKey = SessionPrefix + "StartedUtc";
    private const int RequiredSteadyRenderFrames = 5;
    private const ulong MinimumPostAggroFrames = 180;
    private const ulong MaximumScenarioFrames = 720;
    private const float WaypointArrivalDistance = 0.2f;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);
    private static readonly float[] ApproachRadii = { 7f, 8f, 9f };
    private static readonly List<Vector3> s_Route = new List<Vector3>();
    private static readonly List<Vector3> s_CandidateRoute = new List<Vector3>();
    private static DateTime s_DeadlineUtc;
    private static SprinterProbe s_Probe;
    private static ScenarioMode s_Mode;
    private static int s_WaypointIndex;
    private static ulong s_LastInputFrame;
    private static ulong s_ScenarioStartFrame;
    private static int s_SteadyRenderFrames;
    private static bool s_HeroReturned;

    private enum RunnerState
    {
        WaitingForPlay,
        WaitingForStartup,
        WaitingForRuntime,
        WaitingForInvade,
        Running,
        Finishing,
    }

    private enum ScenarioMode
    {
        Approach,
        Retreat,
        Hold,
    }

    static LvTestSprinterNarrowPathDiagnosticRunner()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
    }

    [MenuItem("Tools/AAAGame/Diagnostics/Run LvTest Sprinter Narrow Path Diagnostic")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("LvTest sprinter diagnostic requires Edit Mode.");

        ResetRuntimeState();
        EditorSceneManager.OpenScene(LaunchScenePath, OpenSceneMode.Single);
        SessionState.SetBool(RunningKey, true);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForPlay);
        SessionState.SetString(StartedUtcKey, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        s_DeadlineUtc = DateTime.UtcNow + Timeout;
        WriteResult("RESULT=RUNNING" + Environment.NewLine
                    + "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine);
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
                {
                    SessionState.SetBool(RunningKey, false);
                    ResetRuntimeState();
                }
                return;
            }

            if (DateTime.UtcNow >= ResolveDeadlineUtc())
                throw new TimeoutException("LvTest sprinter narrow-path diagnostic timed out.");

            switch (state)
            {
                case RunnerState.WaitingForPlay:
                    SessionState.SetInt(StateKey, (int)RunnerState.WaitingForStartup);
                    break;
                case RunnerState.WaitingForStartup:
                    EnterLevelFromLaunch();
                    break;
                case RunnerState.WaitingForRuntime:
                    EnterInvadeWhenReady();
                    break;
                case RunnerState.WaitingForInvade:
                    BeginScenarioWhenReady();
                    break;
                case RunnerState.Running:
                    AdvanceScenario();
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
        if (s_DeadlineUtc != default)
            return s_DeadlineUtc;
        s_DeadlineUtc = DateTime.UtcNow + Timeout;
        return s_DeadlineUtc;
    }

    private static void EnterLevelFromLaunch()
    {
        if (GF.Procedure?.CurrentProcedure == null)
            return;
        if (GF.Procedure.CurrentProcedure is not RuntimeProcedureBase)
            return;

        if (!EditorRuntimeLevelEntry.TryEnterWithDefaultCareer(LevelIdentifier, out string errorMessage))
            throw new InvalidOperationException("Cannot enter LvTest from Launch: " + errorMessage);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForRuntime);
    }

    private static void EnterInvadeWhenReady()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase runtimeProcedure)
            return;
        if (!runtimeProcedure.IsEditorStressRuntimeReady || LogicFrameRuntime.CurrentFrame < 2)
            return;
        if (PhaseManager.CurrentPhase != GamePhase.BuildBeforeInvade)
            throw new InvalidOperationException("LvTest sprinter diagnostic expected BuildBeforeInvade, current=" + PhaseManager.CurrentPhase + ".");

        InputManager inputManager = GameEntry.GetComponent<InputManager>()
                                    ?? throw new InvalidOperationException("LvTest sprinter diagnostic requires InputManager.");
        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("LvTest sprinter diagnostic requires InputModel.");
        if (!inputModel.LogicTimeline.IsStarted)
            return;

        inputManager.ChangeState(InputState.Game);
        PhaseManager.SwitchToPhase(GamePhase.Invade);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForInvade);
    }

    private static void BeginScenarioWhenReady()
    {
        if (PhaseManager.CurrentPhase != GamePhase.Invade)
            return;

        IEntityContext hero = EntityRegistry.Player;
        List<IEntityContext> sprinters = ResolveSprinters();
        if (hero == null || sprinters.Count != 2)
        {
            s_SteadyRenderFrames = 0;
            return;
        }

        int authorityCount = LogicEntityLifecycleService.AuthorityEntityCount;
        bool steady = LogicEntityViewSpawnQueue.IsActive
                      && LogicEntityViewSpawnQueue.PendingCount == 0
                      && LogicEntityViewSpawnQueue.InFlightCount == 0
                      && authorityCount > 0
                      && LogicEntityLifecycleService.BoundViewCount == authorityCount
                      && LogicFrameRuntime.DeferredRealtimeSeconds == 0d;
        s_SteadyRenderFrames = steady ? s_SteadyRenderFrames + 1 : 0;
        if (s_SteadyRenderFrames < RequiredSteadyRenderFrames)
            return;

        if (hero is not ILogicFrameEntity logicHero)
            throw new InvalidOperationException("LvTest hero is not a logic-frame entity.");
        BuildApproachRoute(hero, sprinters, logicHero.NavigationAgentTypeId);
        s_Probe = new SprinterProbe(hero, sprinters);
        s_Mode = ScenarioMode.Approach;
        s_WaypointIndex = 1;
        s_LastInputFrame = 0;
        s_ScenarioStartFrame = LogicFrameRuntime.CurrentFrame;
        s_HeroReturned = false;
        SessionState.SetInt(StateKey, (int)RunnerState.Running);
        AdvanceScenario();
    }

    private static void AdvanceScenario()
    {
        IEntityContext hero = EntityRegistry.Player
                              ?? throw new InvalidOperationException("LvTest sprinter diagnostic lost the hero.");
        SprinterProbe probe = s_Probe
                              ?? throw new InvalidOperationException("LvTest sprinter diagnostic lost its frame probe.");
        probe.SampleViews();

        ulong currentFrame = LogicFrameRuntime.CurrentFrame;
        if (probe.FirstAggroFrame > 0 && s_Mode == ScenarioMode.Approach)
        {
            s_Mode = ScenarioMode.Retreat;
            s_WaypointIndex = Math.Max(0, s_WaypointIndex - 1);
        }

        if (currentFrame != s_LastInputFrame)
        {
            FixVector2 move = ResolveHeroMove(hero);
            InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                    ?? throw new InvalidOperationException("LvTest sprinter diagnostic lost InputModel.");
            inputModel.LogicTimeline.EnqueueEditorWorldMoveForNextFrame(move);
            s_LastInputFrame = currentFrame;
        }

        bool enoughPostAggro = probe.FirstAggroFrame > 0
                               && currentFrame >= probe.FirstAggroFrame + MinimumPostAggroFrames;
        bool maximumReached = currentFrame >= s_ScenarioStartFrame + MaximumScenarioFrames;
        if ((s_HeroReturned && enoughPostAggro) || maximumReached)
            Complete();
    }

    private static FixVector2 ResolveHeroMove(IEntityContext hero)
    {
        if (s_Mode == ScenarioMode.Hold)
            return FixVector2.Zero;
        if (s_Route.Count < 2)
            throw new InvalidOperationException("LvTest sprinter diagnostic has no route.");

        Vector3 position = hero.Position;
        if (s_Mode == ScenarioMode.Approach)
        {
            while (s_WaypointIndex < s_Route.Count
                   && HorizontalDistance(position, s_Route[s_WaypointIndex]) <= WaypointArrivalDistance)
            {
                s_WaypointIndex++;
            }
            if (s_WaypointIndex >= s_Route.Count)
                return FixVector2.Zero;
        }
        else
        {
            while (s_WaypointIndex >= 0
                   && HorizontalDistance(position, s_Route[s_WaypointIndex]) <= WaypointArrivalDistance)
            {
                s_WaypointIndex--;
            }
            if (s_WaypointIndex < 0)
            {
                s_HeroReturned = true;
                s_Mode = ScenarioMode.Hold;
                return FixVector2.Zero;
            }
        }

        Vector3 target = s_Route[s_WaypointIndex];
        FixVector2 direction = new FixVector2((Fix64)(target.x - position.x), (Fix64)(target.z - position.z));
        return FixVector2.SqrMagnitude(direction) > Fix64.Zero ? direction.GetNormalized() : FixVector2.Zero;
    }

    private static void BuildApproachRoute(IEntityContext hero, IReadOnlyList<IEntityContext> sprinters, int agentTypeId)
    {
        Vector3 center = (sprinters[0].Position + sprinters[1].Position) * 0.5f;
        float bestLength = float.PositiveInfinity;
        string lastFailure = string.Empty;
        s_Route.Clear();

        for (int radiusIndex = 0; radiusIndex < ApproachRadii.Length; radiusIndex++)
        {
            float radius = ApproachRadii[radiusIndex];
            for (int directionIndex = 0; directionIndex < 24; directionIndex++)
            {
                float angle = directionIndex * Mathf.PI * 2f / 24f;
                Vector3 candidate = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (!FlowFieldCrowdMovementSystem.TryGetNavigationPathCorners(
                        hero.Position,
                        candidate,
                        agentTypeId,
                        s_CandidateRoute,
                        out string failureReason))
                {
                    lastFailure = failureReason;
                    continue;
                }
                if (s_CandidateRoute.Count < 2)
                    throw new InvalidOperationException("LvTest sprinter route returned fewer than two corners.");

                float length = CalculateRouteLength(s_CandidateRoute);
                if (length >= bestLength)
                    continue;
                bestLength = length;
                s_Route.Clear();
                s_Route.AddRange(s_CandidateRoute);
            }
            if (s_Route.Count >= 2)
                break;
        }

        if (s_Route.Count < 2)
        {
            throw new InvalidOperationException(
                "LvTest sprinter diagnostic found no approach route. hero=" + hero.Position
                + ", sprinters=" + center + ", failure=" + lastFailure);
        }
    }

    private static List<IEntityContext> ResolveSprinters()
    {
        var result = new List<IEntityContext>(2);
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity != null
                && entity.Alive
                && entity.Side == SideType.EnemySide
                && string.Equals(entity.CharacterKey, SprinterCharacterKey, StringComparison.Ordinal))
            {
                result.Add(entity);
            }
        }
        result.Sort((left, right) => left.LogicEntityId.CompareTo(right.LogicEntityId));
        return result;
    }

    private static void Complete()
    {
        SprinterProbe probe = s_Probe
                              ?? throw new InvalidOperationException("LvTest sprinter diagnostic completed without a probe.");
        string result = probe.HasObservedOscillation ? "REPRODUCED" : "NOT_REPRODUCED";
        var report = new StringBuilder(65536);
        report.Append("RESULT=").AppendLine(result);
        report.Append("startedUtc=").AppendLine(SessionState.GetString(StartedUtcKey, string.Empty));
        report.Append("finishedUtc=").AppendLine(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        report.Append("level=").AppendLine(LevelIdentifier);
        report.Append("phase=").AppendLine(PhaseManager.CurrentPhase.ToString());
        report.Append("heroReturned=").AppendLine(s_HeroReturned.ToString());
        report.Append("routeCorners=").AppendLine(s_Route.Count.ToString(CultureInfo.InvariantCulture));
        report.Append(probe.BuildSummary());
        report.AppendLine("FRAME_LOG_BEGIN");
        report.Append(probe.FrameLog);
        report.AppendLine("FRAME_LOG_END");
        WriteResult(report.ToString());
        Debug.Log("AVENGE_LVTEST_SPRINTER_NARROW_PATH_" + result + Environment.NewLine + probe.BuildSummary());
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        EditorApplication.isPlaying = false;
    }

    private static void Fail(Exception exception)
    {
        string existing = ReadResult();
        WriteResult("RESULT=FAIL" + Environment.NewLine
                    + existing.Replace("RESULT=RUNNING" + Environment.NewLine, string.Empty)
                    + exception + Environment.NewLine);
        Debug.LogException(exception);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        EditorApplication.isPlaying = false;
    }

    private static void ResetRuntimeState()
    {
        s_Probe?.Dispose();
        s_Probe = null;
        s_Route.Clear();
        s_CandidateRoute.Clear();
        s_DeadlineUtc = default;
        s_Mode = ScenarioMode.Approach;
        s_WaypointIndex = 0;
        s_LastInputFrame = 0;
        s_ScenarioStartFrame = 0;
        s_SteadyRenderFrames = 0;
        s_HeroReturned = false;
    }

    private static float CalculateRouteLength(IReadOnlyList<Vector3> route)
    {
        float result = 0f;
        for (int i = 1; i < route.Count; i++)
            result += HorizontalDistance(route[i - 1], route[i]);
        return result;
    }

    private static float HorizontalDistance(Vector3 left, Vector3 right)
    {
        float x = right.x - left.x;
        float z = right.z - left.z;
        return Mathf.Sqrt(x * x + z * z);
    }

    private static string GetResultPath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", ResultRelativePath));
    }

    private static string ReadResult()
    {
        string path = GetResultPath();
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static void WriteResult(string content)
    {
        string path = GetResultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private sealed class SprinterProbe : ILogicFrameUpdate, ILogicFrameStableOrder, IDisposable
    {
        private static readonly Fix64 s_TurnDotThreshold = Fix64.FromRaw(3849);
        private static readonly Fix64 s_LateralThreshold = Fix64.FromRaw(214);
        private const ulong RapidAuthorityTurnWindowFrames = 2;
        private const ulong RapidModelTurnWindowFrames = 6;
        private readonly IEntityContext _hero;
        private readonly Dictionary<int, Sample> _samples = new Dictionary<int, Sample>();
        private readonly StringBuilder _frameLog = new StringBuilder(131072);
        private bool _disposed;

        public SprinterProbe(IEntityContext hero, IReadOnlyList<IEntityContext> sprinters)
        {
            _hero = hero ?? throw new ArgumentNullException(nameof(hero));
            for (int i = 0; i < sprinters.Count; i++)
            {
                IEntityContext sprinter = sprinters[i]
                                            ?? throw new InvalidOperationException("LvTest sprinter probe received a null entity.");
                _samples.Add(sprinter.LogicEntityId.Value, new Sample(sprinter));
            }
            LogicFrameRuntime.Register(this);
            LogicFrameRuntime.Ending += OnRuntimeEnding;
        }

        public int LogicFrameOrder => 600;
        public long LogicFrameStableKey => long.MaxValue - 11;
        public ulong FirstAggroFrame { get; private set; }
        public string FrameLog => _frameLog.ToString();
        public bool HasObservedOscillation
        {
            get
            {
                foreach (Sample sample in _samples.Values)
                {
                    if (HasRouteLateralOscillation(
                            sample.RapidAuthorityLateralAlternations,
                            sample.RapidProposedLateralAlternations))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public void OnLogicFrameUpdate(Fix64 deltaTime)
        {
            ulong frame = LogicFrameRuntime.CurrentFrame;
            foreach (Sample sample in _samples.Values)
            {
                if (!EntityRegistry.TryGet(sample.EntityId, out IEntityContext sprinter)
                    || sprinter == null
                    || !sprinter.Alive)
                {
                    throw new InvalidOperationException("LvTest sprinter disappeared during narrow-path capture. entity=" + sample.EntityId.Value + ".");
                }
                if (sprinter.Brain is not SoldierAIBrain brain)
                    throw new InvalidOperationException("LvTest sprinter lost SoldierAIBrain. entity=" + sample.EntityId.Value + ".");

                bool targetsHero = ReferenceEquals(sprinter.TargetComp?.CurrentTarget, _hero);
                if (targetsHero && FirstAggroFrame == 0)
                    FirstAggroFrame = frame;

                LogicAgentCollisionShadowState collision = LogicAgentCollisionShadowService.GetRequiredState(sample.EntityId, frame);
                FixVector2 proposedDisplacement = collision.ProposedPosition - sample.PreviousPosition;
                FixVector2 finalDisplacement = collision.FinalResolvedPosition - sample.PreviousPosition;
                FixVector2 constraintFacingDisplacement = finalDisplacement - collision.PairCorrection;
                FixVector2 proposedForward = NormalizeOrZero(proposedDisplacement);
                FixVector2 authorityForward = sprinter.ForwardFixed;
                FixVector2 toHero = NormalizeOrZero(_hero.PositionFixed - sprinter.PositionFixed);
                FixVector2 navigationTarget = FixVector2.Zero;
                bool hasNavigationTarget = sprinter.MoveComp is CharacterMoveComp move
                                           && move.TryGetNavigationTargetFixed(out navigationTarget);
                FixVector2 toNavigationTarget = hasNavigationTarget
                    ? NormalizeOrZero(navigationTarget - sprinter.PositionFixed)
                    : FixVector2.Zero;
                bool moving = finalDisplacement != FixVector2.Zero;
                bool pursuing = targetsHero
                                && brain.State == SoldierAIBrain.SoldierState.Combat
                                && !(sprinter.AtkComp?.IsAttacking ?? false)
                                && moving;
                bool hasStableGoal = FlowFieldCrowdMovementSystem.TryGetEditorTestStableGoal(
                    sample.EntityId.Value,
                    out int stableTargetId,
                    out int stableRawX,
                    out int stableRawY,
                    out int stableX,
                    out int stableY,
                    out _);
                bool corridorPursuing = pursuing && hasStableGoal;
                sample.IsCorridorPursuit = corridorPursuing;

                if (pursuing)
                {
                    sample.PursuitFrames++;
                    CountTurn(sample.PreviousAuthorityForward, authorityForward, ref sample.AuthorityAbruptTurns);
                    CountTurn(sample.PreviousProposedForward, proposedForward, ref sample.ProposedAbruptTurns);
                    if (corridorPursuing)
                    {
                        CountLateralAlternation(
                            frame,
                            toNavigationTarget,
                            authorityForward,
                            ref sample.LastAuthorityLateralSign,
                            ref sample.LastAuthorityLateralAlternationFrame,
                            ref sample.AuthorityLateralAlternations,
                            ref sample.RapidAuthorityLateralAlternations);
                        CountLateralAlternation(
                            frame,
                            toNavigationTarget,
                            proposedForward,
                            ref sample.LastProposedLateralSign,
                            ref sample.LastProposedLateralAlternationFrame,
                            ref sample.ProposedLateralAlternations,
                            ref sample.RapidProposedLateralAlternations);
                    }
                    else
                    {
                        ResetLateralWindow(
                            ref sample.LastAuthorityLateralSign,
                            ref sample.LastAuthorityLateralAlternationFrame);
                        ResetLateralWindow(
                            ref sample.LastProposedLateralSign,
                            ref sample.LastProposedLateralAlternationFrame);
                    }

                    if (proposedForward != FixVector2.Zero
                        && authorityForward != proposedForward
                        && (collision.StaticCorrection != FixVector2.Zero || collision.RegionCorrection != FixVector2.Zero))
                    {
                        sample.ConstraintDrivenFacingFrames++;
                    }
                    if (collision.PairCorrection != FixVector2.Zero)
                        sample.PairCorrectionFrames++;
                    if (collision.StaticCorrection != FixVector2.Zero)
                        sample.StaticCorrectionFrames++;
                    if (collision.RegionCorrection != FixVector2.Zero)
                        sample.RegionCorrectionFrames++;
                }

                if (hasNavigationTarget
                    && sample.HasNavigationTarget
                    && navigationTarget != sample.PreviousNavigationTarget)
                {
                    sample.NavigationTargetChanges++;
                }
                sample.HasNavigationTarget = hasNavigationTarget;
                sample.PreviousNavigationTarget = navigationTarget;

                FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(sample.EntityId.Value, out string flow);
                string stableGoal = hasStableGoal
                    ? $"target={stableTargetId},raw=({stableRawX},{stableRawY}),active=({stableX},{stableY})"
                    : "unavailable";
                string pathGoal = FlowFieldCrowdMovementSystem.TryGetEditorTestPathGoalCell(
                    sample.EntityId.Value,
                    out int pathGoalX,
                    out int pathGoalY)
                    ? $"({pathGoalX},{pathGoalY})"
                    : "unavailable";
                string pathSource = FlowFieldCrowdMovementSystem.TryGetEditorTestPathBuildSource(
                    sample.EntityId.Value,
                    out string resolvedPathSource)
                    ? resolvedPathSource
                    : "unavailable";
                string pathRoute = FlowFieldCrowdMovementSystem.TryGetEditorTestPathSectorIds(
                                       sample.EntityId.Value,
                                       out int[] pathSectorIds)
                                   && FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(
                                       sample.EntityId.Value,
                                       out int[] pathPortalIds)
                    ? $"sectors=[{string.Join(",", pathSectorIds)}],portals=[{string.Join(",", pathPortalIds)}]"
                    : "unavailable";
                bool directDecisionMismatch = flow != null
                                              && (flow.Contains("directStaticClear=True", StringComparison.Ordinal)
                                                  ^ flow.Contains("directCostClear=True", StringComparison.Ordinal));
                string directDecision = directDecisionMismatch
                    ? FlowFieldCrowdMovementSystem.GetEditorTestOnlyDirectLineDecisionDiagnostic(sample.EntityId.Value)
                    : null;
                if (pursuing || collision.StaticCorrection != FixVector2.Zero || collision.PairCorrection != FixVector2.Zero)
                {
                    _frameLog.Append("frame=").Append(frame)
                        .Append(" entity=").Append(sample.EntityId.Value)
                        .Append(" state=").Append(brain.State)
                        .Append(" target=").Append(sprinter.TargetComp?.CurrentTarget?.LogicEntityId.Value ?? 0)
                        .Append(" prevRaw=").Append(Format(sample.PreviousPosition))
                        .Append(" proposedDispRaw=").Append(Format(proposedDisplacement))
                        .Append(" pairRaw=").Append(Format(collision.PairCorrection))
                        .Append(" staticRaw=").Append(Format(collision.StaticCorrection))
                        .Append(" regionRaw=").Append(Format(collision.RegionCorrection))
                        .Append(" finalDispRaw=").Append(Format(finalDisplacement))
                        .Append(" facingDispRaw=").Append(Format(constraintFacingDisplacement))
                        .Append(" forwardRaw=").Append(Format(authorityForward))
                        .Append(" toHeroRaw=").Append(Format(toHero))
                        .Append(" toNavTargetRaw=").Append(Format(toNavigationTarget))
                        .Append(" navTargetRaw=").Append(hasNavigationTarget ? Format(navigationTarget) : "none")
                        .Append(" stableGoal={").Append(stableGoal).Append('}')
                        .Append(" pathGoal=").Append(pathGoal)
                        .Append(" pathSource=").Append(pathSource)
                        .Append(" pathRoute={").Append(pathRoute).Append('}')
                        .Append(" flow=").Append(flow)
                        .Append(directDecision == null ? string.Empty : " losDecision=" + directDecision)
                        .AppendLine();
                }

                sample.PreviousPosition = sprinter.PositionFixed;
                sample.PreviousAuthorityForward = authorityForward;
                sample.PreviousProposedForward = proposedForward;
            }
        }

        public void SampleViews()
        {
            foreach (Sample sample in _samples.Values)
            {
                if (!LogicEntityLifecycleService.TryGetBoundView(sample.EntityId, out MAEntity view) || view == null)
                    throw new InvalidOperationException("LvTest sprinter has no bound View. entity=" + sample.EntityId.Value + ".");
                Transform animatorTransform = view.PresentationBindings.Animator.transform;
                Transform model = animatorTransform.childCount > 0 ? animatorTransform.GetChild(0) : animatorTransform;
                Vector3 modelForward3 = model.forward;
                FixVector2 modelForward = NormalizeOrZero(new FixVector2((Fix64)modelForward3.x, (Fix64)modelForward3.z));
                if (sample.IsCorridorPursuit
                    && sample.PreviousModelForward != FixVector2.Zero
                    && modelForward != FixVector2.Zero)
                {
                    Fix64 cross = Cross(sample.PreviousModelForward, modelForward);
                    int sign = SignBeyondThreshold(cross, s_LateralThreshold);
                    if (sign != 0 && sample.LastModelTurnSign != 0 && sign != sample.LastModelTurnSign)
                    {
                        sample.ModelTurnAlternations++;
                        ulong frame = LogicFrameRuntime.CurrentFrame;
                        if (sample.LastModelTurnAlternationFrame > 0
                            && frame - sample.LastModelTurnAlternationFrame <= RapidModelTurnWindowFrames)
                        {
                            sample.RapidModelTurnAlternations++;
                        }
                        sample.LastModelTurnAlternationFrame = frame;
                        _frameLog.Append("MODEL_TURN_ALTERNATION logicFrame=")
                            .Append(frame)
                            .Append(" renderFrame=").Append(Time.frameCount)
                            .Append(" entity=").Append(sample.EntityId.Value)
                            .Append(" previousModelForwardRaw=").Append(Format(sample.PreviousModelForward))
                            .Append(" modelForwardRaw=").Append(Format(modelForward))
                            .Append(" authorityForwardRaw=").Append(Format(sample.PreviousAuthorityForward))
                            .Append(" crossRaw=").Append(cross.RawValue)
                            .AppendLine();
                    }
                    if (sign != 0)
                        sample.LastModelTurnSign = sign;
                }
                else if (!sample.IsCorridorPursuit)
                {
                    ResetLateralWindow(
                        ref sample.LastModelTurnSign,
                        ref sample.LastModelTurnAlternationFrame);
                }
                sample.PreviousModelForward = modelForward;
            }
        }

        public string BuildSummary()
        {
            var result = new StringBuilder(4096);
            result.Append("firstAggroFrame=").AppendLine(FirstAggroFrame.ToString(CultureInfo.InvariantCulture));
            result.Append("observedOscillation=").AppendLine(HasObservedOscillation.ToString());
            foreach (Sample sample in _samples.Values)
            {
                result.Append("SPRINTER entity=").Append(sample.EntityId.Value)
                    .Append(" pursuitFrames=").Append(sample.PursuitFrames)
                    .Append(" authorityAbruptTurns=").Append(sample.AuthorityAbruptTurns)
                    .Append(" proposedAbruptTurns=").Append(sample.ProposedAbruptTurns)
                    .Append(" authorityLateralAlternations=").Append(sample.AuthorityLateralAlternations)
                    .Append(" proposedLateralAlternations=").Append(sample.ProposedLateralAlternations)
                    .Append(" modelTurnAlternations=").Append(sample.ModelTurnAlternations)
                    .Append(" rapidAuthorityLateralAlternations=").Append(sample.RapidAuthorityLateralAlternations)
                    .Append(" rapidProposedLateralAlternations=").Append(sample.RapidProposedLateralAlternations)
                    .Append(" rapidModelTurnAlternations=").Append(sample.RapidModelTurnAlternations)
                    .Append(" constraintDrivenFacingFrames=").Append(sample.ConstraintDrivenFacingFrames)
                    .Append(" pairCorrectionFrames=").Append(sample.PairCorrectionFrames)
                    .Append(" staticCorrectionFrames=").Append(sample.StaticCorrectionFrames)
                    .Append(" regionCorrectionFrames=").Append(sample.RegionCorrectionFrames)
                    .Append(" navigationTargetChanges=").Append(sample.NavigationTargetChanges)
                    .AppendLine();
            }
            return result.ToString();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            LogicFrameRuntime.Ending -= OnRuntimeEnding;
            if (LogicFrameRuntime.IsActive)
                LogicFrameRuntime.Unregister(this);
        }

        private void OnRuntimeEnding()
        {
            Dispose();
        }

        private static void CountTurn(FixVector2 previous, FixVector2 current, ref int count)
        {
            if (previous != FixVector2.Zero
                && current != FixVector2.Zero
                && FixVector2.Dot(previous, current) < s_TurnDotThreshold)
            {
                count++;
            }
        }

        private static void CountLateralAlternation(
            ulong frame,
            FixVector2 reference,
            FixVector2 direction,
            ref int previousSign,
            ref ulong previousAlternationFrame,
            ref int count,
            ref int rapidCount)
        {
            if (reference == FixVector2.Zero || direction == FixVector2.Zero)
                return;
            int sign = SignBeyondThreshold(Cross(reference, direction), s_LateralThreshold);
            if (sign != 0 && previousSign != 0 && sign != previousSign)
            {
                count++;
                if (previousAlternationFrame > 0
                    && frame - previousAlternationFrame <= RapidAuthorityTurnWindowFrames)
                {
                    rapidCount++;
                }
                previousAlternationFrame = frame;
            }
            if (sign != 0)
                previousSign = sign;
        }

        internal static int CountRapidRouteLateralAlternationsForTest(
            FixVector2[] references,
            FixVector2[] directions)
        {
            if (references == null)
                throw new ArgumentNullException(nameof(references));
            if (directions == null)
                throw new ArgumentNullException(nameof(directions));
            if (references.Length != directions.Length)
                throw new ArgumentException("Route references and directions must have the same sample count.");

            int previousSign = 0;
            ulong previousAlternationFrame = 0;
            int count = 0;
            int rapidCount = 0;
            for (int i = 0; i < references.Length; i++)
            {
                CountLateralAlternation(
                    checked((ulong)i + 1),
                    NormalizeOrZero(references[i]),
                    NormalizeOrZero(directions[i]),
                    ref previousSign,
                    ref previousAlternationFrame,
                    ref count,
                    ref rapidCount);
            }

            return rapidCount;
        }

        internal static bool HasRouteLateralOscillationForTest(
            int rapidAuthorityLateralAlternations,
            int rapidProposedLateralAlternations,
            int rapidModelTurnAlternations)
        {
            _ = rapidModelTurnAlternations;
            return HasRouteLateralOscillation(
                rapidAuthorityLateralAlternations,
                rapidProposedLateralAlternations);
        }

        private static bool HasRouteLateralOscillation(
            int rapidAuthorityLateralAlternations,
            int rapidProposedLateralAlternations)
        {
            return rapidAuthorityLateralAlternations > 0
                   || rapidProposedLateralAlternations > 0;
        }

        private static void ResetLateralWindow(ref int previousSign, ref ulong previousAlternationFrame)
        {
            previousSign = 0;
            previousAlternationFrame = 0;
        }

        private static Fix64 Cross(FixVector2 left, FixVector2 right)
        {
            return left.x * right.y - left.y * right.x;
        }

        private static int SignBeyondThreshold(Fix64 value, Fix64 threshold)
        {
            if (value > threshold)
                return 1;
            if (value < -threshold)
                return -1;
            return 0;
        }

        private static FixVector2 NormalizeOrZero(FixVector2 value)
        {
            return FixVector2.SqrMagnitude(value) > Fix64.Zero ? value.GetNormalized() : FixVector2.Zero;
        }

        private static string Format(FixVector2 value)
        {
            return "(" + value.x.RawValue + "," + value.y.RawValue + ")";
        }

        private sealed class Sample
        {
            public Sample(IEntityContext entity)
            {
                EntityId = entity.LogicEntityId;
                PreviousPosition = entity.PositionFixed;
                PreviousAuthorityForward = entity.ForwardFixed;
            }

            public readonly LogicEntityId EntityId;
            public FixVector2 PreviousPosition;
            public FixVector2 PreviousAuthorityForward;
            public FixVector2 PreviousProposedForward;
            public FixVector2 PreviousModelForward;
            public FixVector2 PreviousNavigationTarget;
            public bool HasNavigationTarget;
            public bool IsCorridorPursuit;
            public int LastAuthorityLateralSign;
            public int LastProposedLateralSign;
            public int LastModelTurnSign;
            public ulong LastAuthorityLateralAlternationFrame;
            public ulong LastProposedLateralAlternationFrame;
            public ulong LastModelTurnAlternationFrame;
            public int PursuitFrames;
            public int AuthorityAbruptTurns;
            public int ProposedAbruptTurns;
            public int AuthorityLateralAlternations;
            public int ProposedLateralAlternations;
            public int ModelTurnAlternations;
            public int RapidAuthorityLateralAlternations;
            public int RapidProposedLateralAlternations;
            public int RapidModelTurnAlternations;
            public int ConstraintDrivenFacingFrames;
            public int PairCorrectionFrames;
            public int StaticCorrectionFrames;
            public int RegionCorrectionFrames;
            public int NavigationTargetChanges;
        }
    }
}
