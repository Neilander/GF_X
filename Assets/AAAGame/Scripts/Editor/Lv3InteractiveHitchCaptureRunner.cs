using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using AAAGame.MiniMap.FOG3;
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
internal static class Lv3InteractiveHitchCaptureRunner
{
    private const string LaunchScenePath = "Assets/AAAGame/Scene/Launch.unity";
    private const string ResultRelativePath = "Logs/Lv3InteractiveHitchCapture.txt";
    private const string RequestRelativePath = "Logs/RunLv3InteractiveHitchCapture.request";
    private const string PreferredTargetCharacterKey = "Buil_OtakuDesk_Lv1";
    private const string SessionPrefix = "Avenge.Lv3InteractiveHitchCapture.";
    private const string RunningKey = SessionPrefix + "Running";
    private const string StateKey = SessionPrefix + "State";
    private const string StartedUtcKey = SessionPrefix + "StartedUtc";
    private const string CaptureStartFrameKey = SessionPrefix + "CaptureStartFrame";
    private const string TargetEntityIdKey = SessionPrefix + "TargetEntityId";
    private const string StrongholdTargetEntityIdKey = SessionPrefix + "StrongholdTargetEntityId";
    private const string TargetStrongholdIdKey = SessionPrefix + "TargetStrongholdId";
    private const string ScenarioModeKey = SessionPrefix + "ScenarioMode";
    private const string ModeStartFrameKey = SessionPrefix + "ModeStartFrame";
    private const string NextInputFrameKey = SessionPrefix + "NextInputFrame";
    private const string StopUpdateCountKey = SessionPrefix + "StopUpdateCount";
    private const string ProfilerLastFrameKey = SessionPrefix + "ProfilerLastFrame";
    private const string ProfilerHitchFrameKey = SessionPrefix + "ProfilerHitchFrame";
    private const string HitchDetectedKey = SessionPrefix + "HitchDetected";
    private const string HitchSummaryKey = SessionPrefix + "HitchSummary";
    private const string WaypointIndexKey = SessionPrefix + "WaypointIndex";
    private const string VisibilityTransitionCountKey = SessionPrefix + "VisibilityTransitionCount";
    private const string RouteCycleCountKey = SessionPrefix + "RouteCycleCount";
    private const string LastVisibilityKey = SessionPrefix + "LastVisibility";
    private const string ConstructionOwnerEntityIdKey = SessionPrefix + "ConstructionOwnerEntityId";
    private const string ConstructedBuildingIdKey = SessionPrefix + "ConstructedBuildingId";
    private const string ConstructionScheduledFrameKey = SessionPrefix + "ConstructionScheduledFrame";
    private const string ConstructionCompleteFrameKey = SessionPrefix + "ConstructionCompleteFrame";
    private const string StrongholdProbeCountKey = SessionPrefix + "StrongholdProbeCount";
    private const string StrongholdConstrainedStepCountKey = SessionPrefix + "StrongholdConstrainedStepCount";
    private const string StrongholdSlidingProbeCountKey = SessionPrefix + "StrongholdSlidingProbeCount";
    private const string StrongholdBoundaryCompleteFrameKey = SessionPrefix + "StrongholdBoundaryCompleteFrame";
    private const string ForeignStrongholdEnteredFrameKey = SessionPrefix + "ForeignStrongholdEnteredFrame";
    private const string TutorialInvadeTriggerConsumedFrameKey = SessionPrefix + "TutorialInvadeTriggerConsumedFrame";
    private const string InvadeScheduledFrameKey = SessionPrefix + "InvadeScheduledFrame";
    private const string SpawnSettleStartFrameKey = SessionPrefix + "SpawnSettleStartFrame";
    private const string SpawnSteadyFrameKey = SessionPrefix + "SpawnSteadyFrame";
    private const string SpawnSteadyRenderFrameCountKey = SessionPrefix + "SpawnSteadyRenderFrameCount";
    private const string LastSpawnSettleRenderFrameKey = SessionPrefix + "LastSpawnSettleRenderFrame";
    private const string CaptureStartAuthorityCountKey = SessionPrefix + "CaptureStartAuthorityCount";
    private const string CaptureStartBoundViewCountKey = SessionPrefix + "CaptureStartBoundViewCount";
    private const string CaptureStartDeferredMicrosecondsKey = SessionPrefix + "CaptureStartDeferredMicroseconds";
    private const string NavigationPreparedFrameKey = SessionPrefix + "NavigationPreparedFrame";
    private const string NavigationSteadyFrameKey = SessionPrefix + "NavigationSteadyFrame";
    private const string NavigationSteadyRenderFrameCountKey = SessionPrefix + "NavigationSteadyRenderFrameCount";
    private const string LastNavigationSettleRenderFrameKey = SessionPrefix + "LastNavigationSettleRenderFrame";
    private const string ShortHitchCountKey = SessionPrefix + "ShortHitchCount";
    private const string MaxUpdateGapMicrosecondsKey = SessionPrefix + "MaxUpdateGapMicroseconds";
    private const string MaxUpdateGapFrameKey = SessionPrefix + "MaxUpdateGapFrame";
    private const string MaxUpdateGapRuntimeDirtyKey = SessionPrefix + "MaxUpdateGapRuntimeDirty";
    private const string LastSampledProfilerFrameKey = SessionPrefix + "LastSampledProfilerFrame";
    private const string LogicCpuHitchCountKey = SessionPrefix + "LogicCpuHitchCount";
    private const string TrackedCpuHitchCountKey = SessionPrefix + "TrackedCpuHitchCount";
    private const string MaxLogicCpuMicrosecondsKey = SessionPrefix + "MaxLogicCpuMicroseconds";
    private const string MaxLogicCpuUnityFrameKey = SessionPrefix + "MaxLogicCpuUnityFrame";
    private const string MaxLogicCpuFrameKey = SessionPrefix + "MaxLogicCpuFrame";
    private const string MaxTrackedMicrosecondsKey = SessionPrefix + "MaxTrackedMicroseconds";
    private const string MaxTrackedUnityFrameKey = SessionPrefix + "MaxTrackedUnityFrame";
    private const string MaxTrackedLogicFrameKey = SessionPrefix + "MaxTrackedLogicFrame";
    private const string MaxTrackedRuntimeDirtyKey = SessionPrefix + "MaxTrackedRuntimeDirty";
    private const string MaxUntrackedMicrosecondsKey = SessionPrefix + "MaxUntrackedMicroseconds";
    private const string MaxUntrackedUnityFrameKey = SessionPrefix + "MaxUntrackedUnityFrame";
    private const ulong DetectionWarmupFrames = 120;
    private const ulong CaptureDurationFrames = 2400;
    private const ulong InputRefreshFrames = 1;
    private const ulong PauseFrames = 12;
    private const int ChaseObservationFrames = 60;
    private const ulong ChaseFailureFrames = 180;
    private const int RequiredSpawnSteadyRenderFrames = 5;
    private const float WaypointArrivalDistance = 0.02f;
    private const double HitchThresholdMilliseconds = 30.0;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);
    private static readonly float[] ApproachSearchRadii = { 6f, 8f, 10f, 12f };
    private static readonly List<Vector3> s_Route = new List<Vector3>();
    private static readonly List<Vector3> s_CandidateRoute = new List<Vector3>();
    private static long s_LastUpdateTicks;
    private static FixVector2 s_LastMove;
    private static Vector3 s_WaypointStartPosition;
    private static ulong s_WaypointStartFrame;
    private static FixVector2 s_ConstructionPosition;
    private static BuildStrongholdBoundaryProbe s_BuildStrongholdBoundaryProbe;
    private static MovementCommitProbe s_MovementCommitProbe;
    private static SoldierChaseProbe s_SoldierChaseProbe;
    private static BatchSoldierChaseProbe s_BatchSoldierChaseProbe;

    private enum RunnerState
    {
        WaitingForPlay = 0,
        WaitingForStartup = 1,
        WaitingForRuntime = 2,
        WaitingForConstruction = 3,
        WaitingForBuildBoundary = 4,
        WaitingForInvade = 5,
        WaitingForNavigationSteady = 6,
        Running = 7,
        WaitingForProfilerStop = 8,
        Analyzing = 9,
        Finishing = 10,
    }

    private enum ScenarioMode
    {
        Approach = 0,
        Retreat = 1,
        Pause = 2,
    }

    private sealed class CpuEntry
    {
        public string Path;
        public double TotalMilliseconds;
        public double SelfMilliseconds;
        public int Calls;
    }

    private sealed class BuildStrongholdBoundaryProbe : ILogicFrameUpdate, ILogicFrameStableOrder
    {
        private const ulong MaxNoProgressFrames = 180;
        private const ulong RequiredSlidingFrames = 90;
        private const int RequiredConstrainedSlidingFrames = 30;
        private static readonly Fix64 s_WaypointArrivalDistanceSquared =
            Fix64.FromRaw(614) * Fix64.FromRaw(614);

        private readonly IEntityContext _hero;
        private readonly IEntityContext _enemyBuilding;
        private readonly string _strongholdId;
        private readonly Fix64 _radius;
        private readonly List<FixVector2> _route;
        private readonly Fog3MapData _fogMap;
        private readonly int _targetFogGridX;
        private readonly int _targetFogGridY;
        private FixVector2 _previousPosition;
        private FixVector2 _lastProgressPosition;
        private FixVector2 _lastEnqueuedMove;
        private FixVector2 _inward;
        private FixVector2 _tangent;
        private FixVector2 _slidingStartPosition;
        private ulong _previousFrame;
        private ulong _lastProgressFrame;
        private ulong _slidingStartFrame;
        private ulong _returnArrivalFrame;
        private int _waypointIndex;
        private bool _sliding;
        private bool _returning;
        private bool _registered;

        public BuildStrongholdBoundaryProbe(
            IEntityContext hero,
            IEntityContext enemyBuilding,
            string strongholdId,
            IReadOnlyList<Vector3> route)
        {
            _hero = hero ?? throw new ArgumentNullException(nameof(hero));
            _enemyBuilding = enemyBuilding ?? throw new ArgumentNullException(nameof(enemyBuilding));
            if (string.IsNullOrWhiteSpace(strongholdId))
                throw new ArgumentException("Build stronghold boundary probe requires a stronghold id.", nameof(strongholdId));
            if (route == null || route.Count < 2)
                throw new ArgumentException("Build stronghold boundary probe requires a navigation route.", nameof(route));
            if (!LogicFrameRuntime.IsActive || !LogicFrameRuntime.IsTimelineRunning)
                throw new InvalidOperationException("Build stronghold boundary probe requires an active logic timeline.");
            if (LogicCardRuntimeState.IsBound)
                throw new InvalidOperationException("Build fog exploration probe requires the card runtime to remain uninitialized.");

            Fog3Manager fogManager = Fog3Manager.Instance
                                     ?? throw new InvalidOperationException("Build fog exploration probe requires Fog3Manager.");
            _fogMap = fogManager.MapData
                      ?? throw new InvalidOperationException("Build fog exploration probe requires Fog3MapData.");
            if (!AAAGame.Card.LogicCardPlacementAuthority.IsBoundTo(_fogMap))
                throw new InvalidOperationException("Build fog exploration authority is not bound to the active Fog3MapData.");
            Vector3 targetFogPosition = new Vector3(
                (float)_enemyBuilding.PositionFixed.x,
                0f,
                (float)_enemyBuilding.PositionFixed.y);
            if (!_fogMap.WorldToGrid(targetFogPosition, out _targetFogGridX, out _targetFogGridY))
                throw new InvalidOperationException("Build fog exploration target is outside Fog3MapData.");
            InitialTargetFogState = _fogMap.GetCellState(_targetFogGridX, _targetFogGridY);
            if (InitialTargetFogState != Fog3CellState.Hidden)
            {
                throw new InvalidOperationException(
                    $"Build fog exploration probe requires a hidden target, actual={InitialTargetFogState}.");
            }
            FinalTargetFogState = InitialTargetFogState;

            _strongholdId = strongholdId;
            _radius = DistanceUnitConverter.ConvertToWorld(
                _hero.GetProperty(CreatureMainProperty.CollisionRadius));
            if (_radius <= Fix64.Zero)
                throw new InvalidOperationException("Build stronghold boundary probe requires a positive hero collision radius.");

            _route = new List<FixVector2>(route.Count);
            for (int i = 0; i < route.Count; i++)
                _route.Add(new FixVector2((Fix64)route[i].x, (Fix64)route[i].z));
            _waypointIndex = 1;
            _previousPosition = _hero.PositionFixed;
            _lastProgressPosition = _hero.PositionFixed;
            _previousFrame = LogicFrameRuntime.CurrentFrame;
            _lastProgressFrame = _previousFrame;
            LogicFrameRuntime.Register(this);
            LogicFrameRuntime.Ending += HandleRuntimeEnding;
            _registered = true;
            EnqueueMove(ResolveApproachMove(_hero.PositionFixed));
        }

        public int LogicFrameOrder => 499;
        public long LogicFrameStableKey => long.MaxValue - 3;
        public bool IsComplete { get; private set; }
        public int CommitSampleCount { get; private set; }
        public int ConstrainedFrameCount { get; private set; }
        public int SlidingSampleCount { get; private set; }
        public int SlidingConstrainedFrameCount { get; private set; }
        public int ReturnSampleCount { get; private set; }
        public Fix64 MaximumTangentProgress { get; private set; }
        public ulong FirstConstraintFrame { get; private set; }
        public ulong CompleteFrame { get; private set; }
        public Fog3CellState InitialTargetFogState { get; }
        public Fog3CellState FinalTargetFogState { get; private set; }
        public ulong TargetVisibleFrame { get; private set; }
        public ulong TargetExploredAfterVisibleFrame { get; private set; }
        public Exception Failure { get; private set; }

        public void OnLogicFrameUpdate(Fix64 deltaTime)
        {
            try
            {
                AdvanceFrame(deltaTime);
            }
            catch (Exception exception)
            {
                Failure = exception;
                Unregister();
            }
        }

        private void AdvanceFrame(Fix64 deltaTime)
        {
            if (deltaTime != LogicFrameRuntime.FixedDeltaTime)
                throw new InvalidOperationException("Build stronghold boundary probe received an invalid logic delta.");

            ulong currentFrame = LogicFrameRuntime.CurrentFrame;
            if (currentFrame != _previousFrame + 1)
            {
                throw new InvalidOperationException(
                    $"Build stronghold boundary probe observed a non-contiguous frame. previous={_previousFrame}, current={currentFrame}.");
            }
            if (!_hero.Alive)
                throw new InvalidOperationException("Build stronghold boundary probe lost the live hero.");
            if (PhaseManager.CurrentPhase != GamePhase.BuildBeforeInvade
                || LogicPhaseCommandService.CurrentPhase != GamePhase.BuildBeforeInvade)
            {
                throw new InvalidOperationException(
                    $"Build stronghold boundary probe left BuildBeforeInvade before completion. phase={PhaseManager.CurrentPhase}, logicPhase={LogicPhaseCommandService.CurrentPhase}, frame={currentFrame}.");
            }

            SampleTargetFog(currentFrame);

            LogicAgentCollisionShadowState collisionState = GetHeroCollisionState(currentFrame);
            CommitSampleCount++;
            if (!LogicStrongholdMap.IsCircleClearOfForeignStrongholds(
                    _hero.PositionFixed,
                    _radius,
                    EntitySideHelper.PlayerFactionId))
            {
                throw new InvalidOperationException(
                    $"Build stronghold boundary probe committed the hero collision circle inside a foreign stronghold. frame={currentFrame}, stronghold={_strongholdId}, position={_hero.PositionFixed}, radiusRaw={_radius.RawValue}, state={BuildPairCollisionDiagnostics(_hero)}");
            }
            if (TryGetForeignStrongholdId(_hero.PositionFixed, out string enteredStrongholdId))
            {
                throw new InvalidOperationException(
                    $"Build stronghold boundary probe committed the hero center inside foreign stronghold '{enteredStrongholdId}'. frame={currentFrame}, position={_hero.PositionFixed}.");
            }

            if (collisionState.RegionConstraintFailure == LogicMovementRegionConstraintFailure.EnemyStronghold)
            {
                ConstrainedFrameCount++;
                if (FirstConstraintFrame == 0)
                    FirstConstraintFrame = currentFrame;
            }
            else if (collisionState.RegionConstraintFailure != LogicMovementRegionConstraintFailure.None)
            {
                throw new InvalidOperationException(
                    $"Build stronghold boundary probe hit unexpected region constraint {collisionState.RegionConstraintFailure} at frame {currentFrame}.");
            }

            if (_returning)
                AdvanceReturn(currentFrame);
            else if (_sliding)
                AdvanceSliding(currentFrame, collisionState);
            else
                AdvanceApproach(currentFrame, collisionState);

            _previousPosition = _hero.PositionFixed;
            _previousFrame = currentFrame;
        }

        public string BuildSummary()
        {
            return "buildBoundaryCommitSamples=" + CommitSampleCount + Environment.NewLine
                   + "buildBoundaryConstrainedFrames=" + ConstrainedFrameCount + Environment.NewLine
                   + "buildBoundarySlidingSamples=" + SlidingSampleCount + Environment.NewLine
                   + "buildBoundarySlidingConstrainedFrames=" + SlidingConstrainedFrameCount + Environment.NewLine
                   + "buildBoundaryReturnSamples=" + ReturnSampleCount + Environment.NewLine
                   + "buildBoundaryFirstConstraintFrame=" + FirstConstraintFrame + Environment.NewLine
                   + "buildBoundaryCompleteFrame=" + CompleteFrame + Environment.NewLine
                   + "buildBoundaryMaxTangentProgressRaw=" + MaximumTangentProgress.RawValue + Environment.NewLine
                   + "buildFogInitialState=" + InitialTargetFogState + Environment.NewLine
                   + "buildFogVisibleFrame=" + TargetVisibleFrame + Environment.NewLine
                   + "buildFogExploredAfterVisibleFrame=" + TargetExploredAfterVisibleFrame + Environment.NewLine
                   + "buildFogFinalState=" + FinalTargetFogState + Environment.NewLine;
        }

        private void AdvanceApproach(
            ulong currentFrame,
            LogicAgentCollisionShadowState collisionState)
        {
            if (collisionState.RegionConstraintFailure == LogicMovementRegionConstraintFailure.EnemyStronghold)
            {
                if (_lastEnqueuedMove == FixVector2.Zero)
                    throw new InvalidOperationException("Build stronghold boundary probe reached the wall without an inward move.");
                _inward = _lastEnqueuedMove.GetNormalized();
                _tangent = new FixVector2(-_inward.y, _inward.x);
                _slidingStartPosition = _hero.PositionFixed;
                _slidingStartFrame = currentFrame;
                _sliding = true;
                EnqueueMove((_inward + _tangent).GetNormalized());
                return;
            }

            if (FixVector2.SqrMagnitude(_hero.PositionFixed - _lastProgressPosition)
                > Fix64.FromRaw(2048) * Fix64.FromRaw(2048))
            {
                _lastProgressPosition = _hero.PositionFixed;
                _lastProgressFrame = currentFrame;
            }
            else if (currentFrame >= _lastProgressFrame + MaxNoProgressFrames)
            {
                throw new InvalidOperationException(
                    $"Build stronghold boundary probe stalled before reaching the wall. frame={currentFrame}, waypoint={_waypointIndex}/{_route.Count - 1}, position={_hero.PositionFixed}, target={_route[_waypointIndex]}, diagnostics={BuildPairCollisionDiagnostics(_hero)}");
            }

            EnqueueMove(ResolveApproachMove(_hero.PositionFixed));
        }

        private void AdvanceSliding(
            ulong currentFrame,
            LogicAgentCollisionShadowState collisionState)
        {
            SlidingSampleCount++;
            if (collisionState.RegionConstraintFailure == LogicMovementRegionConstraintFailure.EnemyStronghold)
                SlidingConstrainedFrameCount++;

            Fix64 tangentProgress = FixVector2.Dot(
                _hero.PositionFixed - _slidingStartPosition,
                _tangent);
            if (tangentProgress > MaximumTangentProgress)
                MaximumTangentProgress = tangentProgress;

            if (currentFrame < _slidingStartFrame + RequiredSlidingFrames)
            {
                EnqueueMove((_inward + _tangent).GetNormalized());
                return;
            }
            if (SlidingConstrainedFrameCount < RequiredConstrainedSlidingFrames)
            {
                throw new InvalidOperationException(
                    $"Build stronghold boundary probe did not prove stable edge blocking. frame={currentFrame}, constrained={SlidingConstrainedFrameCount}/{SlidingSampleCount}, tangentProgressRaw={MaximumTangentProgress.RawValue}, position={_hero.PositionFixed}, diagnostics={BuildPairCollisionDiagnostics(_hero)}");
            }

            BeginReturn(currentFrame);
        }

        private void BeginReturn(ulong currentFrame)
        {
            _returning = true;
            _waypointIndex = Math.Min(_waypointIndex - 1, _route.Count - 2);
            while (_waypointIndex > 0
                   && (!LogicStrongholdMap.IsCircleClearOfForeignStrongholds(
                           _route[_waypointIndex],
                           _radius,
                           EntitySideHelper.PlayerFactionId)
                       || TryGetForeignStrongholdId(_route[_waypointIndex], out _)))
            {
                _waypointIndex--;
            }
            _lastProgressPosition = _hero.PositionFixed;
            _lastProgressFrame = currentFrame;
            EnqueueMove(ResolveReturnMove(_hero.PositionFixed));
        }

        private void AdvanceReturn(ulong currentFrame)
        {
            ReturnSampleCount++;
            if (FixVector2.SqrMagnitude(_hero.PositionFixed - _lastProgressPosition)
                > Fix64.FromRaw(2048) * Fix64.FromRaw(2048))
            {
                _lastProgressPosition = _hero.PositionFixed;
                _lastProgressFrame = currentFrame;
            }
            else if (currentFrame >= _lastProgressFrame + MaxNoProgressFrames)
            {
                throw new InvalidOperationException(
                    $"Build stronghold boundary probe stalled while returning. frame={currentFrame}, waypoint={_waypointIndex}, position={_hero.PositionFixed}, diagnostics={BuildPairCollisionDiagnostics(_hero)}");
            }

            FixVector2 move = ResolveReturnMove(_hero.PositionFixed);
            if (_waypointIndex >= 0)
            {
                EnqueueMove(move);
                return;
            }

            if (_returnArrivalFrame == 0)
            {
                _returnArrivalFrame = currentFrame;
                EnqueueMove(FixVector2.Zero);
                return;
            }
            if (TargetExploredAfterVisibleFrame == 0)
            {
                if (currentFrame > _returnArrivalFrame + 30)
                {
                    throw new InvalidOperationException(
                        $"Build fog exploration did not settle to Explored after retreat. arrivalFrame={_returnArrivalFrame}, currentFrame={currentFrame}, final={FinalTargetFogState}.");
                }
                EnqueueMove(FixVector2.Zero);
                return;
            }

            CompleteFrame = currentFrame;
            if (TargetVisibleFrame == 0)
                throw new InvalidOperationException("Build fog exploration probe never observed the hidden enemy building become visible.");
            if (TargetExploredAfterVisibleFrame == 0 || FinalTargetFogState != Fog3CellState.Explored)
            {
                throw new InvalidOperationException(
                    $"Build fog exploration probe did not preserve exploration after retreat. visibleFrame={TargetVisibleFrame}, exploredFrame={TargetExploredAfterVisibleFrame}, final={FinalTargetFogState}.");
            }
            IsComplete = true;
            EnqueueMove(FixVector2.Zero);
            Unregister();
        }

        private void SampleTargetFog(ulong currentFrame)
        {
            Fog3CellState state = _fogMap.GetCellState(_targetFogGridX, _targetFogGridY);
            FinalTargetFogState = state;
            if (state == Fog3CellState.Visible)
            {
                if (TargetVisibleFrame == 0)
                    TargetVisibleFrame = currentFrame;
                return;
            }

            if (TargetVisibleFrame == 0)
                return;
            if (state == Fog3CellState.Hidden)
            {
                throw new InvalidOperationException(
                    $"Build fog exploration regressed from Visible to Hidden at frame {currentFrame}.");
            }
            if (state == Fog3CellState.Explored && TargetExploredAfterVisibleFrame == 0)
                TargetExploredAfterVisibleFrame = currentFrame;
        }

        private FixVector2 ResolveApproachMove(FixVector2 position)
        {
            while (_waypointIndex < _route.Count)
            {
                FixVector2 waypoint = _route[_waypointIndex];
                FixVector2 delta = waypoint - position;
                if (FixVector2.SqrMagnitude(delta) > s_WaypointArrivalDistanceSquared
                    && !HasPassedWaypoint(position, _waypointIndex))
                {
                    return delta.GetNormalized();
                }
                _waypointIndex++;
            }

            throw new InvalidOperationException(
                $"Build stronghold boundary probe reached route end without hitting EnemyStronghold. stronghold={_strongholdId}, position={position}.");
        }

        private bool HasPassedWaypoint(FixVector2 position, int waypointIndex)
        {
            if (waypointIndex <= 0)
                return false;
            FixVector2 segment = _route[waypointIndex] - _route[waypointIndex - 1];
            return FixVector2.SqrMagnitude(segment) > Fix64.Zero
                   && FixVector2.Dot(position - _route[waypointIndex], segment) >= Fix64.Zero;
        }

        private FixVector2 ResolveReturnMove(FixVector2 position)
        {
            while (_waypointIndex >= 0)
            {
                FixVector2 waypoint = _route[_waypointIndex];
                FixVector2 delta = waypoint - position;
                if (FixVector2.SqrMagnitude(delta) > s_WaypointArrivalDistanceSquared
                    && !HasPassedReturnWaypoint(position, _waypointIndex))
                {
                    return delta.GetNormalized();
                }
                _waypointIndex--;
            }
            return FixVector2.Zero;
        }

        private bool HasPassedReturnWaypoint(FixVector2 position, int waypointIndex)
        {
            if (waypointIndex >= _route.Count - 1)
                return false;
            FixVector2 segment = _route[waypointIndex] - _route[waypointIndex + 1];
            return FixVector2.SqrMagnitude(segment) > Fix64.Zero
                   && FixVector2.Dot(position - _route[waypointIndex], segment) >= Fix64.Zero;
        }

        private void EnqueueMove(FixVector2 move)
        {
            InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                    ?? throw new InvalidOperationException("Build stronghold boundary probe lost InputModel.");
            inputModel.LogicTimeline.EnqueueEditorWorldMoveForNextFrame(move);
            _lastEnqueuedMove = move;
            s_LastMove = move;
        }

        private LogicAgentCollisionShadowState GetHeroCollisionState(ulong frame)
        {
            if (LogicAgentCollisionShadowService.LastCompletedFrame != frame)
            {
                throw new InvalidOperationException(
                    $"Build stronghold boundary probe requires current MoveCommit diagnostics. expected={frame}, actual={LogicAgentCollisionShadowService.LastCompletedFrame}.");
            }

            IReadOnlyList<LogicAgentCollisionShadowState> states = LogicAgentCollisionShadowService.LastStates;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].EntityId == _hero.LogicEntityId)
                    return states[i];
            }
            throw new InvalidOperationException(
                $"Build stronghold boundary probe found no collision state for hero {_hero.LogicEntityId.Value} at frame {frame}.");
        }

        private void HandleRuntimeEnding()
        {
            Unregister();
        }

        private void Unregister()
        {
            if (!_registered)
                return;
            LogicFrameRuntime.Ending -= HandleRuntimeEnding;
            LogicFrameRuntime.Unregister(this);
            _registered = false;
        }
    }

    private sealed class MovementCommitProbe : ILogicFrameUpdate, ILogicFrameStableOrder
    {
        private readonly IEntityContext _hero;
        private FixVector2 _previousPosition;
        private ulong _previousFrame;

        public MovementCommitProbe(IEntityContext hero)
        {
            _hero = hero ?? throw new ArgumentNullException(nameof(hero));
            if (_hero.MoveComp == null)
                throw new InvalidOperationException("Lv3 movement commit probe requires the hero MoveComp.");
            if (!LogicFrameRuntime.IsActive || !LogicFrameRuntime.IsTimelineRunning)
                throw new InvalidOperationException("Lv3 movement commit probe requires an active logic timeline.");

            _previousPosition = _hero.PositionFixed;
            _previousFrame = LogicFrameRuntime.CurrentFrame;
            LogicFrameRuntime.Register(this);
            LogicFrameRuntime.Ending += HandleRuntimeEnding;
        }

        public int LogicFrameOrder => 500;
        public long LogicFrameStableKey => long.MaxValue;
        public int SampleCount { get; private set; }
        public int MovingSampleCount { get; private set; }
        public int StationarySampleCount { get; private set; }
        public int PairCorrectionFrameCount { get; private set; }
        public int StaticCorrectionFrameCount { get; private set; }
        public int RegionConstraintFrameCount { get; private set; }
        public int JointConstraintRejectedFrameCount { get; private set; }
        public int StaticOverlapFrameCount { get; private set; }
        public LogicMovementRegionConstraintFailure LastRegionConstraintFailure { get; private set; }
        public ulong LastRegionConstraintFrame { get; private set; }
        public ulong ForeignStrongholdEnteredFrame { get; private set; }
        public string ForeignStrongholdId { get; private set; }
        public ulong TutorialInvadeTriggerConsumedFrame { get; private set; }

        public void OnLogicFrameUpdate(Fix64 deltaTime)
        {
            if (deltaTime != LogicFrameRuntime.FixedDeltaTime)
                throw new InvalidOperationException("Lv3 movement commit probe received an invalid logic delta.");
            ulong currentFrame = LogicFrameRuntime.CurrentFrame;
            if (currentFrame != _previousFrame + 1)
            {
                throw new InvalidOperationException(
                    $"Lv3 movement commit probe observed a non-contiguous frame. previous={_previousFrame}, current={currentFrame}.");
            }
            if (!_hero.Alive)
                throw new InvalidOperationException("Lv3 movement commit probe lost the live hero.");

            FixVector2 displacement = _hero.PositionFixed - _previousPosition;
            bool expectedMoving = FixVector2.SqrMagnitude(displacement) > Fix64.FromRaw(1);
            bool actualMoving = _hero.MoveComp.IsMoving;
            if (actualMoving != expectedMoving)
            {
                throw new InvalidOperationException(
                    $"Lv3 movement commit state mismatch. frame={currentFrame}, displacement={displacement}, expectedMoving={expectedMoving}, actualMoving={actualMoving}.");
            }

            SampleCount++;
            if (actualMoving)
                MovingSampleCount++;
            else
                StationarySampleCount++;
            RecordCollisionState(currentFrame);
            RecordInvadeEntry(currentFrame);
            _previousPosition = _hero.PositionFixed;
            _previousFrame = currentFrame;
        }

        public string BuildSummary()
        {
            return "movementPairCorrectionFrames=" + PairCorrectionFrameCount + Environment.NewLine
                   + "movementStaticCorrectionFrames=" + StaticCorrectionFrameCount + Environment.NewLine
                   + "movementRegionConstraintFrames=" + RegionConstraintFrameCount + Environment.NewLine
                   + "movementJointConstraintRejectedFrames=" + JointConstraintRejectedFrameCount + Environment.NewLine
                   + "movementStaticOverlapFrames=" + StaticOverlapFrameCount + Environment.NewLine
                   + "movementLastRegionConstraintFailure=" + LastRegionConstraintFailure + Environment.NewLine
                   + "movementLastRegionConstraintFrame=" + LastRegionConstraintFrame + Environment.NewLine
                   + "foreignStrongholdEnteredFrame=" + ForeignStrongholdEnteredFrame + Environment.NewLine
                   + "foreignStrongholdId=" + (ForeignStrongholdId ?? "<none>") + Environment.NewLine
                   + "tutorialInvadeTriggerConsumedFrame=" + TutorialInvadeTriggerConsumedFrame + Environment.NewLine;
        }

        public void ValidateInvadeEntry()
        {
            if (ForeignStrongholdEnteredFrame == 0)
                throw new InvalidOperationException("Lv3 movement probe never brought the hero inside an enemy stronghold.");
            if (LogicMovementRegionConstraintService.HasTutorialInvadeTrigger
                && TutorialInvadeTriggerConsumedFrame == 0)
            {
                throw new InvalidOperationException(
                    "Lv3 movement probe entered an enemy stronghold without consuming the authored invade trigger.");
            }
        }

        private void RecordCollisionState(ulong currentFrame)
        {
            if (LogicAgentCollisionShadowService.LastCompletedFrame != currentFrame)
                return;

            IReadOnlyList<LogicAgentCollisionShadowState> states = LogicAgentCollisionShadowService.LastStates;
            for (int i = 0; i < states.Count; i++)
            {
                LogicAgentCollisionShadowState state = states[i];
                if (state.EntityId != _hero.LogicEntityId)
                    continue;
                if (state.PairCorrection != FixVector2.Zero)
                    PairCorrectionFrameCount++;
                if (state.StaticCorrection != FixVector2.Zero)
                    StaticCorrectionFrameCount++;
                if (state.RegionConstraintFailure != LogicMovementRegionConstraintFailure.None)
                {
                    RegionConstraintFrameCount++;
                    LastRegionConstraintFailure = state.RegionConstraintFailure;
                    LastRegionConstraintFrame = currentFrame;
                }
                if (LogicAgentCollisionShadowService.LastJointConstraintRejectedCount > 0)
                    JointConstraintRejectedFrameCount++;

                if (!(_hero is ILogicFrameEntity logicHero))
                    throw new InvalidOperationException("Lv3 movement probe requires an ILogicFrameEntity hero.");
                Fix64 radius = DistanceUnitConverter.ConvertToWorld(
                    _hero.GetProperty(CreatureMainProperty.CollisionRadius));
                if (radius <= Fix64.Zero)
                {
                    throw new InvalidOperationException(
                        $"Lv3 movement probe requires a positive hero collision radius. frame={currentFrame}, radiusRaw={radius.RawValue}.");
                }
                if (!LogicStaticCollisionShadowService.TrySolveFixed(
                        logicHero.NavigationAgentTypeId,
                        _hero.PositionFixed,
                        FixVector2.Zero,
                        radius,
                        out LogicStaticCollisionShadowResult staticProbe))
                {
                    throw new InvalidOperationException(
                        $"Lv3 movement probe static source is unavailable. frame={currentFrame}, agentType={logicHero.NavigationAgentTypeId}.");
                }
                if (!staticProbe.SolveResult.Success)
                {
                    throw new InvalidOperationException(
                        $"Lv3 movement probe static zero-displacement solve failed. frame={currentFrame}, " +
                        $"positionRaw=({_hero.PositionFixed.x.RawValue},{_hero.PositionFixed.y.RawValue}), " +
                        $"failure={staticProbe.SolveResult.Failure}.");
                }
                if (staticProbe.SolveResult.StartedOverlapping)
                {
                    StaticOverlapFrameCount++;
                    FixVector2 recovery = staticProbe.SolveResult.RecoveredStart - _hero.PositionFixed;
                    throw new InvalidOperationException(
                        $"Lv3 movement probe committed a static-overlap position. frame={currentFrame}, " +
                        $"positionRaw=({_hero.PositionFixed.x.RawValue},{_hero.PositionFixed.y.RawValue}), " +
                        $"recoveryRaw=({recovery.x.RawValue},{recovery.y.RawValue}).");
                }
                return;
            }
        }

        private void RecordInvadeEntry(ulong currentFrame)
        {
            if (!LogicStrongholdMap.IsInitialized)
                throw new InvalidOperationException("Lv3 movement probe requires initialized stronghold authority.");

            if (ForeignStrongholdEnteredFrame == 0
                && LogicStrongholdMap.TryResolveStrongholdId(_hero.PositionFixed, out string strongholdId)
                && LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId) != EntitySideHelper.PlayerFactionId)
            {
                if (LogicPhaseCommandService.CurrentPhase != GamePhase.Invade)
                {
                    throw new InvalidOperationException(
                        $"Lv3 movement probe entered foreign stronghold '{strongholdId}' before Invade. frame={currentFrame}, logicPhase={LogicPhaseCommandService.CurrentPhase}, position={_hero.PositionFixed}.");
                }
                ForeignStrongholdEnteredFrame = currentFrame;
                ForeignStrongholdId = strongholdId;
                SessionState.SetInt(ForeignStrongholdEnteredFrameKey, ToSessionInt(currentFrame));
                Log.Info(
                    "[Lv3HitchCapture] Hero entered enemy stronghold. frame={0}, stronghold={1}, position={2}.",
                    currentFrame,
                    strongholdId,
                    _hero.PositionFixed);
            }

            if (TutorialInvadeTriggerConsumedFrame == 0
                && LogicMovementRegionConstraintService.HasTutorialInvadeTrigger
                && LogicMovementRegionConstraintService.TutorialInvadeTriggerConsumed)
            {
                TutorialInvadeTriggerConsumedFrame = currentFrame;
                SessionState.SetInt(TutorialInvadeTriggerConsumedFrameKey, ToSessionInt(currentFrame));
                Log.Info(
                    "[Lv3HitchCapture] Tutorial invade trigger consumed. frame={0}, stronghold={1}, position={2}.",
                    currentFrame,
                    LogicMovementRegionConstraintService.TutorialStrongholdId,
                    _hero.PositionFixed);
            }
        }

        private void HandleRuntimeEnding()
        {
            LogicFrameRuntime.Ending -= HandleRuntimeEnding;
            LogicFrameRuntime.Unregister(this);
            if (ReferenceEquals(s_MovementCommitProbe, this))
                s_MovementCommitProbe = null;
        }
    }

    private sealed class SoldierChaseProbe : ILogicFrameUpdate, ILogicFrameStableOrder
    {
        private readonly IEntityContext _hero;
        private readonly LogicEntityId _soldierId;
        private FixVector2 _previousPosition;
        private FixVector2 _previousHeroPosition;
        private FixVector2 _lastHeroPosition;
        private FixVector2 _lastSoldierPosition;
        private Fix64 _detectEnemyRange;
        private Fix64 _aggroRange;
        private Fix64 _forgetRange;
        private FixVector2 _observationStartPosition;
        private Fix64 _observationStartDistance;
        private FixVector2 _lastNavigationTarget;
        private FixVector2 _lastProposedPosition;
        private FixVector2 _lastFinalPosition;
        private FixVector2 _lastPairCorrection;
        private FixVector2 _lastStaticCorrection;
        private FixVector2 _lastRegionCorrection;
        private FixVector2 _previousPursuitForward;
        private Fix64 _lastSpeed;
        private string _lastFlowDiagnostic = "unavailable";
        private readonly string _sourceStrongholdId;

        public SoldierChaseProbe(IEntityContext hero, IEntityContext soldier)
        {
            _hero = hero ?? throw new ArgumentNullException(nameof(hero));
            if (soldier == null)
                throw new ArgumentNullException(nameof(soldier));
            if (soldier.Brain is not SoldierAIBrain)
                throw new InvalidOperationException($"Lv3 chase probe entity {soldier.LogicEntityId.Value} is not driven by SoldierAIBrain.");

            _soldierId = soldier.LogicEntityId;
            _previousPosition = soldier.PositionFixed;
            _previousHeroPosition = hero.PositionFixed;
            _lastHeroPosition = hero.PositionFixed;
            _lastSoldierPosition = soldier.PositionFixed;
            _detectEnemyRange = ((SoldierAIBrain)soldier.Brain).DetectEnemyRange;
            ITargetSearchRangeComp searchRange = soldier.TargetComp as ITargetSearchRangeComp
                                                   ?? throw new InvalidOperationException(
                                                       $"Lv3 chase probe soldier {_soldierId.Value} has no target search range capability.");
            _aggroRange = searchRange.AggroRangeFixed;
            _forgetRange = searchRange.ForgetRangeFixed;
            _lastSpeed = DistanceUnitConverter.ConvertToWorld(soldier.GetProperty(CreatureMainProperty.Speed));
            _sourceStrongholdId = (soldier as LogicEntityState)?.SourceStrongholdId
                                  ?? throw new InvalidOperationException($"Lv3 chase probe soldier {_soldierId.Value} has no source stronghold.");
            LogicFrameRuntime.Register(this);
            LogicFrameRuntime.Ending += HandleRuntimeEnding;
        }

        public int LogicFrameOrder => 600;
        public long LogicFrameStableKey => long.MaxValue - 1;
        public ulong ProximityFrame { get; private set; }
        public ulong HeroTargetFrame { get; private set; }
        public ulong CombatFrame { get; private set; }
        public ulong NavigationFrame { get; private set; }
        public ulong MovementFrame { get; private set; }
        public ulong AttackFrame { get; private set; }
        public ulong ObservationStartFrame { get; private set; }
        public ulong SustainedProgressFrame { get; private set; }
        public ulong HeroTargetLossFrame { get; private set; }
        public bool TargetDiedAfterAttack { get; private set; }
        public int StationaryHeroFrameCount { get; private set; }
        public Fix64 MaximumObservationDisplacement { get; private set; }
        public Fix64 MaximumDistanceReduction { get; private set; }
        public int PairCorrectionFrameCount { get; private set; }
        public Fix64 MaxPairCorrection { get; private set; }
        public int PairDrivenFacingMismatchFrameCount { get; private set; }
        public int NonPairFacingMismatchFrameCount { get; private set; }
        public int PursuitForwardAbruptTurnFrameCount { get; private set; }
        public Fix64 MinimumHeroDistance { get; private set; } = Fix64.FromRaw(long.MaxValue);

        public bool IsVerified => ProximityFrame > 0
                                  && HeroTargetFrame > 0
                                  && CombatFrame > 0
                                  && (NavigationFrame > 0 || AttackFrame > 0)
                                  && (AttackFrame > 0
                                      || (HeroTargetLossFrame == 0
                                          && ObservationStartFrame > 0
                                          && StationaryHeroFrameCount >= ChaseObservationFrames
                                          && SustainedProgressFrame > 0));

        public void OnLogicFrameUpdate(Fix64 deltaTime)
        {
            ulong frame = LogicFrameRuntime.CurrentFrame;
            if (!EntityRegistry.TryGet(_soldierId, out IEntityContext soldier) || soldier == null || !soldier.Alive)
                return;
            if (soldier.Brain is not SoldierAIBrain brain)
                throw new InvalidOperationException($"Lv3 chase probe entity {_soldierId.Value} lost SoldierAIBrain.");

            Fix64 heroDistance = soldier.LogicFrameDistanceToTargetSurfaceFixed(_hero);
            _lastHeroPosition = _hero.PositionFixed;
            _lastSoldierPosition = soldier.PositionFixed;
            _detectEnemyRange = brain.DetectEnemyRange;
            ITargetSearchRangeComp searchRange = soldier.TargetComp as ITargetSearchRangeComp
                                                   ?? throw new InvalidOperationException(
                                                       $"Lv3 chase probe soldier {_soldierId.Value} lost target search range capability.");
            _aggroRange = searchRange.AggroRangeFixed;
            _forgetRange = searchRange.ForgetRangeFixed;
            if (heroDistance < MinimumHeroDistance)
                MinimumHeroDistance = heroDistance;
            if (ProximityFrame == 0 && heroDistance < _aggroRange)
                ProximityFrame = frame;
            if (ProximityFrame == 0)
            {
                _previousPosition = soldier.PositionFixed;
                return;
            }

            if (HeroTargetFrame == 0 && ReferenceEquals(soldier.TargetComp?.CurrentTarget, _hero))
                HeroTargetFrame = frame;
            if (CombatFrame == 0 && brain.State == SoldierAIBrain.SoldierState.Combat)
                CombatFrame = frame;
            if (NavigationFrame == 0 && soldier.MoveComp is CharacterMoveComp move && move.HasNavigationTarget)
                NavigationFrame = frame;
            if (AttackFrame == 0 && (brain.Attack || (soldier.AtkComp?.IsAttacking ?? false)))
                AttackFrame = frame;
            if (MovementFrame == 0 && soldier.PositionFixed != _previousPosition)
                MovementFrame = frame;

            bool heroStationary = FixVector2.SqrMagnitude(_hero.PositionFixed - _previousHeroPosition) <= Fix64.FromRaw(1);
            if (HeroTargetFrame > 0 && CombatFrame > 0 && heroStationary)
            {
                if (ObservationStartFrame == 0)
                {
                    ObservationStartFrame = frame;
                    _observationStartPosition = soldier.PositionFixed;
                    _observationStartDistance = heroDistance;
                }
                StationaryHeroFrameCount++;
                Fix64 displacement = FixVector2.Distance(_observationStartPosition, soldier.PositionFixed);
                Fix64 distanceReduction = _observationStartDistance - heroDistance;
                if (displacement > MaximumObservationDisplacement)
                    MaximumObservationDisplacement = displacement;
                if (distanceReduction > MaximumDistanceReduction)
                    MaximumDistanceReduction = distanceReduction;
                if (SustainedProgressFrame == 0
                    && displacement >= Fix64.FromRaw(2048)
                    && distanceReduction >= Fix64.FromRaw(1024))
                {
                    SustainedProgressFrame = frame;
                }
                if (HeroTargetLossFrame == 0 && !ReferenceEquals(soldier.TargetComp.CurrentTarget, _hero))
                    HeroTargetLossFrame = frame;
            }
            else if (ObservationStartFrame > 0 && !heroStationary)
            {
                ObservationStartFrame = 0;
                StationaryHeroFrameCount = 0;
                MaximumObservationDisplacement = Fix64.Zero;
                MaximumDistanceReduction = Fix64.Zero;
                HeroTargetLossFrame = 0;
                SustainedProgressFrame = 0;
            }

            if (soldier.MoveComp is CharacterMoveComp characterMove
                && characterMove.TryGetNavigationTargetFixed(out FixVector2 navigationTarget))
            {
                _lastNavigationTarget = navigationTarget;
            }
            _lastSpeed = DistanceUnitConverter.ConvertToWorld(soldier.GetProperty(CreatureMainProperty.Speed));
            FlowFieldCrowdMovementSystem.TryGetEditorTestDeterministicFlowDiagnostic(
                _soldierId.Value,
                out _lastFlowDiagnostic);

            if (LogicAgentCollisionShadowService.LastCompletedFrame != frame)
                throw new InvalidOperationException($"Lv3 chase probe has no collision commit for frame {frame}.");
            IReadOnlyList<LogicAgentCollisionShadowState> states = LogicAgentCollisionShadowService.LastStates;
            bool foundCollisionState = false;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].EntityId != _soldierId)
                    continue;
                foundCollisionState = true;
                _lastProposedPosition = states[i].ProposedPosition;
                _lastFinalPosition = states[i].FinalResolvedPosition;
                _lastPairCorrection = states[i].PairCorrection;
                _lastStaticCorrection = states[i].StaticCorrection;
                _lastRegionCorrection = states[i].RegionCorrection;
                bool pursuingHero = brain.State == SoldierAIBrain.SoldierState.Combat
                                    && ReferenceEquals(soldier.TargetComp?.CurrentTarget, _hero)
                                    && !(soldier.AtkComp?.IsAttacking ?? false)
                                    && soldier.PositionFixed != _previousPosition;
                if (pursuingHero)
                {
                    FixVector2 facingDisplacement = states[i].FinalResolvedPosition
                                                    - _previousPosition
                                                    - states[i].PairCorrection;
                    if (FixVector2.SqrMagnitude(facingDisplacement) > Fix64.Zero)
                    {
                        FixVector2 expectedForward = facingDisplacement.GetNormalized();
                        if (soldier.ForwardFixed != expectedForward)
                        {
                            if (states[i].PairCorrection != FixVector2.Zero)
                                PairDrivenFacingMismatchFrameCount++;
                            else
                                NonPairFacingMismatchFrameCount++;
                        }
                    }

                    if (_previousPursuitForward != FixVector2.Zero
                        && FixVector2.Dot(_previousPursuitForward, soldier.ForwardFixed) < Fix64.FromRaw(2048))
                    {
                        PursuitForwardAbruptTurnFrameCount++;
                    }
                    _previousPursuitForward = soldier.ForwardFixed;
                }
                else
                {
                    _previousPursuitForward = FixVector2.Zero;
                }

                Fix64 correction = FixVector2.Magnitude(states[i].PairCorrection);
                if (correction > Fix64.Zero)
                {
                    PairCorrectionFrameCount++;
                    if (correction > MaxPairCorrection)
                        MaxPairCorrection = correction;
                }
                break;
            }
            if (!foundCollisionState)
                throw new InvalidOperationException($"Lv3 chase probe found no collision body for soldier {_soldierId.Value} at frame {frame}.");

            _previousPosition = soldier.PositionFixed;
            _previousHeroPosition = _hero.PositionFixed;
        }

        public void Validate()
        {
            if (ProximityFrame == 0)
            {
                throw new InvalidOperationException(
                    $"Lv3 chase probe never brought soldier {_soldierId.Value} into aggro range. " +
                    $"minimumDistanceRaw={MinimumHeroDistance.RawValue}, detectRangeRaw={_detectEnemyRange.RawValue}, " +
                    $"hero={_lastHeroPosition}, soldier={_lastSoldierPosition}.");
            }
            if (HeroTargetFrame == 0 || HeroTargetFrame > ProximityFrame + 8)
                throw new InvalidOperationException($"Lv3 chase target acquisition exceeded 8 ticks. proximity={ProximityFrame}, target={HeroTargetFrame}.");
            if (CombatFrame == 0 || CombatFrame > HeroTargetFrame + 2)
                throw new InvalidOperationException($"Lv3 chase combat transition exceeded 2 ticks. target={HeroTargetFrame}, combat={CombatFrame}.");
            if (NavigationFrame == 0 && AttackFrame == 0)
                throw new InvalidOperationException("Lv3 chase produced neither a navigation target nor an attack.");
            ulong actionFrame = NavigationFrame > 0 ? NavigationFrame : AttackFrame;
            if (actionFrame > CombatFrame + 30)
                throw new InvalidOperationException($"Lv3 chase action exceeded 30 ticks. combat={CombatFrame}, action={actionFrame}.");
            if (MovementFrame == 0 && AttackFrame == 0)
                throw new InvalidOperationException("Lv3 chase produced neither committed movement nor an attack.");
            if (PairDrivenFacingMismatchFrameCount != 0 || NonPairFacingMismatchFrameCount != 0)
            {
                throw new InvalidOperationException(
                    $"Lv3 chase authority Forward diverged from constrained movement. pairDriven={PairDrivenFacingMismatchFrameCount}, nonPair={NonPairFacingMismatchFrameCount}. {BuildSummary()}");
            }
            if (PursuitForwardAbruptTurnFrameCount != 0)
            {
                throw new InvalidOperationException(
                    $"Lv3 chase authority Forward made {PursuitForwardAbruptTurnFrameCount} pursuit turns over 60 degrees. {BuildSummary()}");
            }
            if (AttackFrame > 0)
                return;
            if (ObservationStartFrame == 0 || StationaryHeroFrameCount < ChaseObservationFrames)
                throw new InvalidOperationException($"Lv3 chase did not observe a stationary hero for {ChaseObservationFrames} ticks.");
            if (HeroTargetLossFrame > 0)
                throw new InvalidOperationException($"Lv3 chase soldier lost the stationary hero target at frame {HeroTargetLossFrame}.");
            if (SustainedProgressFrame == 0 && AttackFrame == 0)
            {
                throw new InvalidOperationException(
                    $"Lv3 chase soldier made no sustained progress toward the stationary hero. {BuildSummary()}");
            }
        }

        public void ThrowIfObservationTimedOut(ulong currentFrame)
        {
            if (ObservationStartFrame == 0 || currentFrame < ObservationStartFrame + ChaseFailureFrames || IsVerified)
                return;
            throw new InvalidOperationException(
                $"Lv3 chase soldier did not pursue the stationary hero within {ChaseFailureFrames} ticks. {BuildSummary()}");
        }

        public void MarkTargetDiedAfterAttack()
        {
            if (AttackFrame == 0)
            {
                throw new InvalidOperationException(
                    $"Lv3 chase target {_soldierId.Value} died before performing an attack. {BuildSummary()}");
            }
            if (HeroTargetLossFrame > 0)
            {
                throw new InvalidOperationException(
                    $"Lv3 chase target {_soldierId.Value} lost the hero before death. {BuildSummary()}");
            }

            TargetDiedAfterAttack = true;
            Validate();
        }

        public void Stop()
        {
            LogicFrameRuntime.Ending -= HandleRuntimeEnding;
            LogicFrameRuntime.Unregister(this);
        }

        public string BuildSummary()
        {
            return "chaseSoldierEntityId=" + _soldierId.Value + Environment.NewLine
                   + "chaseSourceStrongholdId=" + _sourceStrongholdId + Environment.NewLine
                   + "chaseProximityFrame=" + ProximityFrame + Environment.NewLine
                   + "chaseHeroTargetFrame=" + HeroTargetFrame + Environment.NewLine
                   + "chaseCombatFrame=" + CombatFrame + Environment.NewLine
                   + "chaseNavigationFrame=" + NavigationFrame + Environment.NewLine
                   + "chaseMovementFrame=" + MovementFrame + Environment.NewLine
                   + "chaseAttackFrame=" + AttackFrame + Environment.NewLine
                   + "chaseObservationStartFrame=" + ObservationStartFrame + Environment.NewLine
                   + "chaseStationaryHeroFrames=" + StationaryHeroFrameCount + Environment.NewLine
                   + "chaseSustainedProgressFrame=" + SustainedProgressFrame + Environment.NewLine
                   + "chaseHeroTargetLossFrame=" + HeroTargetLossFrame + Environment.NewLine
                   + "chaseTargetDiedAfterAttack=" + TargetDiedAfterAttack + Environment.NewLine
                   + "chaseMaximumObservationDisplacementRaw=" + MaximumObservationDisplacement.RawValue + Environment.NewLine
                   + "chaseMaximumDistanceReductionRaw=" + MaximumDistanceReduction.RawValue + Environment.NewLine
                   + "chaseTargetLatencyTicks=" + FrameDelta(ProximityFrame, HeroTargetFrame) + Environment.NewLine
                   + "chaseCombatLatencyTicks=" + FrameDelta(HeroTargetFrame, CombatFrame) + Environment.NewLine
                   + "chaseActionLatencyTicks=" + FrameDelta(CombatFrame, NavigationFrame > 0 ? NavigationFrame : AttackFrame) + Environment.NewLine
                   + "chaseMinimumHeroDistanceRaw=" + MinimumHeroDistance.RawValue + Environment.NewLine
                   + "chaseDetectEnemyRangeRaw=" + _detectEnemyRange.RawValue + Environment.NewLine
                   + "chaseAggroRangeRaw=" + _aggroRange.RawValue + Environment.NewLine
                   + "chaseForgetRangeRaw=" + _forgetRange.RawValue + Environment.NewLine
                   + "chaseSpeedRaw=" + _lastSpeed.RawValue + Environment.NewLine
                   + "chaseLastHeroPosition=" + _lastHeroPosition + Environment.NewLine
                   + "chaseLastSoldierPosition=" + _lastSoldierPosition + Environment.NewLine
                   + "chaseLastNavigationTarget=" + _lastNavigationTarget + Environment.NewLine
                   + "chaseLastProposedPosition=" + _lastProposedPosition + Environment.NewLine
                   + "chaseLastFinalPosition=" + _lastFinalPosition + Environment.NewLine
                   + "chaseLastPairCorrection=" + _lastPairCorrection + Environment.NewLine
                   + "chaseLastStaticCorrection=" + _lastStaticCorrection + Environment.NewLine
                   + "chaseLastRegionCorrection=" + _lastRegionCorrection + Environment.NewLine
                   + "chaseLastFlowDiagnostic=" + _lastFlowDiagnostic + Environment.NewLine
                   + "chasePairCorrectionFrames=" + PairCorrectionFrameCount + Environment.NewLine
                   + "chaseMaxPairCorrectionRaw=" + MaxPairCorrection.RawValue + Environment.NewLine
                   + "chasePairDrivenFacingMismatchFrames=" + PairDrivenFacingMismatchFrameCount + Environment.NewLine
                   + "chaseNonPairFacingMismatchFrames=" + NonPairFacingMismatchFrameCount + Environment.NewLine
                   + "chasePursuitForwardAbruptTurnFrames=" + PursuitForwardAbruptTurnFrameCount + Environment.NewLine;
        }

        private static string FrameDelta(ulong start, ulong end)
        {
            return start > 0 && end >= start ? (end - start).ToString(CultureInfo.InvariantCulture) : "<none>";
        }

        private void HandleRuntimeEnding()
        {
            LogicFrameRuntime.Ending -= HandleRuntimeEnding;
            LogicFrameRuntime.Unregister(this);
        }
    }

    private sealed class BatchSoldierChaseProbe : ILogicFrameUpdate, ILogicFrameStableOrder
    {
        private sealed class Entry
        {
            public LogicEntityId EntityId;
            public FixVector2 PreviousPosition;
            public ulong ProximityFrame;
            public ulong HeroTargetFrame;
            public ulong CombatFrame;
            public ulong NavigationFrame;
            public ulong MovementFrame;
            public ulong AttackFrame;
            public ulong ReturningFrame;
            public Fix64 MinimumHeroDistance = Fix64.FromRaw(long.MaxValue);
        }

        private readonly IEntityContext _hero;
        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();

        public BatchSoldierChaseProbe(IEntityContext hero)
        {
            _hero = hero ?? throw new ArgumentNullException(nameof(hero));
            LogicFrameRuntime.Register(this);
            LogicFrameRuntime.Ending += HandleRuntimeEnding;
        }

        public int LogicFrameOrder => 601;
        public long LogicFrameStableKey => long.MaxValue - 2;

        public void OnLogicFrameUpdate(Fix64 deltaTime)
        {
            if (deltaTime != LogicFrameRuntime.FixedDeltaTime)
                throw new InvalidOperationException("Lv3 batch chase probe received an invalid logic delta.");

            ulong frame = LogicFrameRuntime.CurrentFrame;
            IList<IEntityContext> entities = EntityRegistry.AllEntities
                                             ?? throw new InvalidOperationException("Lv3 batch chase probe requires EntityRegistry.AllEntities.");
            for (int i = 0; i < entities.Count; i++)
            {
                IEntityContext soldier = entities[i]
                                         ?? throw new InvalidOperationException($"Lv3 batch chase probe found a null entity at index {i}.");
                if (!soldier.Alive || soldier.Side != SideType.EnemySide || soldier.Brain is not SoldierAIBrain brain)
                    continue;

                int id = soldier.LogicEntityId.Value;
                if (!_entries.TryGetValue(id, out Entry entry))
                {
                    entry = new Entry
                    {
                        EntityId = soldier.LogicEntityId,
                        PreviousPosition = soldier.PositionFixed,
                    };
                    _entries.Add(id, entry);
                }

                Fix64 heroDistance = soldier.LogicFrameDistanceToTargetSurfaceFixed(_hero);
                if (heroDistance < entry.MinimumHeroDistance)
                    entry.MinimumHeroDistance = heroDistance;
                if (entry.ProximityFrame == 0 && heroDistance < brain.DetectEnemyRange)
                    entry.ProximityFrame = frame;
                if (entry.ProximityFrame == 0)
                {
                    entry.PreviousPosition = soldier.PositionFixed;
                    continue;
                }

                if (entry.HeroTargetFrame == 0 && ReferenceEquals(soldier.TargetComp?.CurrentTarget, _hero))
                    entry.HeroTargetFrame = frame;
                if (entry.CombatFrame == 0 && brain.State == SoldierAIBrain.SoldierState.Combat)
                    entry.CombatFrame = frame;
                if (entry.NavigationFrame == 0 && soldier.MoveComp is CharacterMoveComp move && move.HasNavigationTarget)
                    entry.NavigationFrame = frame;
                if (entry.MovementFrame == 0 && soldier.PositionFixed != entry.PreviousPosition)
                    entry.MovementFrame = frame;
                if (entry.AttackFrame == 0 && (brain.Attack || (soldier.AtkComp?.IsAttacking ?? false)))
                    entry.AttackFrame = frame;
                if (entry.ReturningFrame == 0 && brain.State == SoldierAIBrain.SoldierState.Returning)
                    entry.ReturningFrame = frame;
                entry.PreviousPosition = soldier.PositionFixed;
            }
        }

        public void Validate()
        {
            int proximityCount = 0;
            var failures = new List<string>();
            foreach (Entry entry in _entries.Values)
            {
                if (entry.ProximityFrame == 0)
                    continue;
                proximityCount++;
                string failure = ResolveFailure(entry);
                if (failure != null)
                    failures.Add(FormatEntry(entry, failure));
            }

            if (proximityCount == 0)
                throw new InvalidOperationException("Lv3 batch chase probe brought no enemy soldier into detection range.");
            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Lv3 batch chase probe found {failures.Count}/{proximityCount} soldiers that did not chase the hero. " +
                    string.Join(" | ", failures.GetRange(0, Math.Min(20, failures.Count))));
            }
        }

        public string BuildSummary()
        {
            int proximityCount = 0;
            int verifiedCount = 0;
            int returningCount = 0;
            var failures = new List<string>();
            foreach (Entry entry in _entries.Values)
            {
                if (entry.ProximityFrame == 0)
                    continue;
                proximityCount++;
                if (entry.ReturningFrame > 0)
                    returningCount++;
                string failure = ResolveFailure(entry);
                if (failure == null)
                    verifiedCount++;
                else if (failures.Count < 20)
                    failures.Add(FormatEntry(entry, failure));
            }

            return "batchChaseTracked=" + _entries.Count + Environment.NewLine
                   + "batchChaseProximity=" + proximityCount + Environment.NewLine
                   + "batchChaseVerified=" + verifiedCount + Environment.NewLine
                   + "batchChaseReturning=" + returningCount + Environment.NewLine
                   + "batchChaseFailures=" + (failures.Count > 0 ? string.Join(" | ", failures) : "none") + Environment.NewLine;
        }

        private static string ResolveFailure(Entry entry)
        {
            if (entry.HeroTargetFrame == 0 || entry.HeroTargetFrame > entry.ProximityFrame + 8)
                return "hero-target-timeout";
            if (entry.CombatFrame == 0 || entry.CombatFrame > entry.HeroTargetFrame + 2)
                return "combat-timeout";
            if (entry.NavigationFrame == 0 && entry.AttackFrame == 0)
                return "no-navigation-or-attack";
            ulong actionFrame = entry.NavigationFrame > 0 ? entry.NavigationFrame : entry.AttackFrame;
            if (actionFrame > entry.CombatFrame + 30)
                return "action-timeout";
            if (entry.MovementFrame == 0 && entry.AttackFrame == 0)
                return "no-movement-or-attack";
            return null;
        }

        private static string FormatEntry(Entry entry, string failure)
        {
            return $"id={entry.EntityId.Value},reason={failure},proximity={entry.ProximityFrame},target={entry.HeroTargetFrame}," +
                   $"combat={entry.CombatFrame},nav={entry.NavigationFrame},move={entry.MovementFrame},attack={entry.AttackFrame}," +
                   $"returning={entry.ReturningFrame},minHeroDistanceRaw={entry.MinimumHeroDistance.RawValue}";
        }

        private void HandleRuntimeEnding()
        {
            LogicFrameRuntime.Ending -= HandleRuntimeEnding;
            LogicFrameRuntime.Unregister(this);
        }
    }

    static Lv3InteractiveHitchCaptureRunner()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        if (TryConsumeRunRequest())
            EditorApplication.delayCall += Run;
    }

    private static bool TryConsumeRunRequest()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                             ?? throw new InvalidOperationException("Cannot resolve project root for Lv3 interactive hitch request.");
        string requestPath = Path.Combine(projectRoot, RequestRelativePath);
        if (!File.Exists(requestPath))
            return false;

        File.Delete(requestPath);
        return true;
    }

    [MenuItem("Tools/Logic Frames/Run Lv3 Interactive Hitch Capture")]
    public static void Run()
    {
        if (SessionState.GetBool(RunningKey, false))
        {
            RunnerState staleState = (RunnerState)SessionState.GetInt(StateKey, (int)RunnerState.WaitingForPlay);
            if (!EditorApplication.isPlayingOrWillChangePlaymode && staleState == RunnerState.Finishing)
                CompleteSession();
            else
                throw new InvalidOperationException($"Lv3 interactive hitch capture is already running. state={staleState}.");
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before starting Lv3 interactive hitch capture.");
        if (EditorApplication.isCompiling)
            throw new InvalidOperationException("Wait for script compilation before starting Lv3 interactive hitch capture.");

        EnsureNoDirtyScenes();
        string startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        SessionState.SetBool(RunningKey, true);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForPlay);
        SessionState.SetString(StartedUtcKey, startedUtc);
        SessionState.SetInt(CaptureStartFrameKey, 0);
        SessionState.SetInt(TargetEntityIdKey, 0);
        SessionState.SetInt(StrongholdTargetEntityIdKey, 0);
        SessionState.SetString(TargetStrongholdIdKey, string.Empty);
        SessionState.SetInt(ScenarioModeKey, (int)ScenarioMode.Approach);
        SessionState.SetInt(ModeStartFrameKey, 0);
        SessionState.SetInt(NextInputFrameKey, 0);
        SessionState.SetInt(StopUpdateCountKey, 0);
        SessionState.SetInt(ProfilerLastFrameKey, -1);
        SessionState.SetInt(ProfilerHitchFrameKey, -1);
        SessionState.SetBool(HitchDetectedKey, false);
        SessionState.SetString(HitchSummaryKey, string.Empty);
        SessionState.SetInt(WaypointIndexKey, 0);
        SessionState.SetInt(VisibilityTransitionCountKey, 0);
        SessionState.SetInt(RouteCycleCountKey, 0);
        SessionState.SetInt(LastVisibilityKey, (int)Fog3CellState.Outside);
        SessionState.SetInt(ConstructionOwnerEntityIdKey, 0);
        SessionState.SetString(ConstructedBuildingIdKey, string.Empty);
        SessionState.SetInt(ConstructionScheduledFrameKey, 0);
        SessionState.SetInt(ConstructionCompleteFrameKey, 0);
        SessionState.SetInt(StrongholdProbeCountKey, 0);
        SessionState.SetInt(StrongholdConstrainedStepCountKey, 0);
        SessionState.SetInt(StrongholdSlidingProbeCountKey, 0);
        SessionState.SetInt(StrongholdBoundaryCompleteFrameKey, 0);
        SessionState.SetInt(ForeignStrongholdEnteredFrameKey, 0);
        SessionState.SetInt(TutorialInvadeTriggerConsumedFrameKey, 0);
        SessionState.SetInt(InvadeScheduledFrameKey, 0);
        SessionState.SetInt(SpawnSettleStartFrameKey, 0);
        SessionState.SetInt(SpawnSteadyFrameKey, 0);
        SessionState.SetInt(SpawnSteadyRenderFrameCountKey, 0);
        SessionState.SetInt(LastSpawnSettleRenderFrameKey, -1);
        SessionState.SetInt(CaptureStartAuthorityCountKey, 0);
        SessionState.SetInt(CaptureStartBoundViewCountKey, 0);
        SessionState.SetInt(CaptureStartDeferredMicrosecondsKey, 0);
        SessionState.SetInt(NavigationPreparedFrameKey, 0);
        SessionState.SetInt(NavigationSteadyFrameKey, 0);
        SessionState.SetInt(NavigationSteadyRenderFrameCountKey, 0);
        SessionState.SetInt(LastNavigationSettleRenderFrameKey, -1);
        SessionState.SetInt(ShortHitchCountKey, 0);
        SessionState.SetInt(MaxUpdateGapMicrosecondsKey, 0);
        SessionState.SetInt(MaxUpdateGapFrameKey, 0);
        SessionState.SetString(MaxUpdateGapRuntimeDirtyKey, string.Empty);
        SessionState.SetInt(LastSampledProfilerFrameKey, -1);
        SessionState.SetInt(LogicCpuHitchCountKey, 0);
        SessionState.SetInt(TrackedCpuHitchCountKey, 0);
        SessionState.SetInt(MaxLogicCpuMicrosecondsKey, 0);
        SessionState.SetInt(MaxLogicCpuUnityFrameKey, -1);
        SessionState.SetInt(MaxLogicCpuFrameKey, 0);
        SessionState.SetInt(MaxTrackedMicrosecondsKey, 0);
        SessionState.SetInt(MaxTrackedUnityFrameKey, -1);
        SessionState.SetInt(MaxTrackedLogicFrameKey, 0);
        SessionState.SetString(MaxTrackedRuntimeDirtyKey, string.Empty);
        SessionState.SetInt(MaxUntrackedMicrosecondsKey, 0);
        SessionState.SetInt(MaxUntrackedUnityFrameKey, -1);
        s_BuildStrongholdBoundaryProbe = null;
        s_MovementCommitProbe = null;
        s_SoldierChaseProbe = null;
        s_BatchSoldierChaseProbe = null;
        MainThreadFrameProfilerNextPlayCapture.ArmNextPlay();
        WriteResult(
            "RESULT=RUNNING" + Environment.NewLine +
            "startedUtc=" + startedUtc + Environment.NewLine +
            "hitchThresholdMilliseconds=" + HitchThresholdMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + Environment.NewLine);
        EditorSceneManager.OpenScene(LaunchScenePath, OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }

    private static void Update()
    {
        if (!SessionState.GetBool(RunningKey, false))
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && TryConsumeRunRequest())
                Run();
            return;
        }

        try
        {
            ValidateTimeout();
            RunnerState state = (RunnerState)SessionState.GetInt(StateKey, (int)RunnerState.WaitingForPlay);
            if (!EditorApplication.isPlaying)
            {
                if (state == RunnerState.Analyzing)
                    AnalyzeCapture();
                else if (state == RunnerState.Finishing)
                    CompleteSession();
                else if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    Fail(new InvalidOperationException($"Lv3 interactive hitch capture left Play mode unexpectedly. state={state}."));
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
                    BeginCaptureWhenReady();
                    break;
                case RunnerState.WaitingForConstruction:
                    BeginMovementAfterConstruction();
                    break;
                case RunnerState.WaitingForBuildBoundary:
                    CompleteBuildBoundaryWhenReady();
                    break;
                case RunnerState.WaitingForInvade:
                    BeginMovementAfterInvade();
                    break;
                case RunnerState.WaitingForNavigationSteady:
                    BeginCaptureAfterNavigationSettled();
                    break;
                case RunnerState.Running:
                    AdvanceCapture();
                    break;
                case RunnerState.WaitingForProfilerStop:
                    StopProfilerAfterFrameCommit();
                    break;
                case RunnerState.Analyzing:
                case RunnerState.Finishing:
                    break;
                default:
                    throw new InvalidOperationException($"Unknown Lv3 interactive hitch capture state {state}.");
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
        if (GF.Procedure.CurrentProcedure is not RuntimeProcedureBase)
            return;

        if (!EditorRuntimeLevelEntry.TryEnterWithDefaultCareer("Lv_3", out string error))
            throw new InvalidOperationException($"Cannot enter Lv_3 from Launch: {error}");
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForRuntime);
    }

    private static void BeginCaptureWhenReady()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase runtimeProcedure)
            return;
        if (!runtimeProcedure.IsEditorStressRuntimeReady || !LogicFrameRuntime.IsTimelineRunning)
            return;

        InputManager inputManager = GameEntry.GetComponent<InputManager>()
                                    ?? throw new InvalidOperationException("Lv3 interactive hitch capture requires InputManager.");
        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("Lv3 interactive hitch capture requires InputModel.");
        if (!inputModel.LogicTimeline.IsStarted)
            return;

        ulong currentFrame = LogicFrameRuntime.CurrentFrame;
        if (currentFrame == 0)
            return;
        if (PhaseManager.CurrentPhase != GamePhase.BuildBeforeInvade)
        {
            throw new InvalidOperationException(
                $"Lv3 interactive hitch capture requires BuildBeforeInvade before construction, current={PhaseManager.CurrentPhase}.");
        }

        IEntityContext target = ResolvePreferredHiddenEnemyBuilding();
        IEntityContext hero = EntityRegistry.Player
                              ?? throw new InvalidOperationException("Lv3 interactive hitch capture cannot resolve the player entity.");
        inputManager.ChangeState(InputState.Game);
        if (Profiler.enabled || Profiler.enableBinaryLog || ProfilerDriver.enabled)
            throw new InvalidOperationException("Lv3 interactive hitch capture requires Profiler recording to be disabled before capture.");

        SessionState.SetInt(TargetEntityIdKey, target.LogicEntityId.Value);
        SessionState.SetInt(StrongholdTargetEntityIdKey, target.LogicEntityId.Value);
        SessionState.SetInt(LastVisibilityKey, (int)ResolveFogState(target.Position));
        ScheduleConstruction(hero, currentFrame);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForConstruction);
        s_LastMove = FixVector2.Zero;
        Log.Info(
            "[Lv3HitchCapture] Construction scheduled. frame={0}, owner={1}, building={2}, position={3}, target={4}/{5}.",
            currentFrame,
            SessionState.GetInt(ConstructionOwnerEntityIdKey, 0),
            SessionState.GetString(ConstructedBuildingIdKey, string.Empty),
            s_ConstructionPosition,
            target.LogicEntityId.Value,
            target.CharacterKey);
    }

    private static void BeginMovementAfterConstruction()
    {
        ulong currentFrame = LogicFrameRuntime.CurrentFrame;
        int ownerEntityId = SessionState.GetInt(ConstructionOwnerEntityIdKey, 0);
        if (ownerEntityId <= 0)
            throw new InvalidOperationException("Lv3 interactive hitch capture lost its construction owner identity.");
        if (LogicInteractionCommandService.PendingCount != 0
            || EntityRegistry.TryGet(new LogicEntityId(ownerEntityId), out _))
        {
            ulong scheduledFrame = (ulong)SessionState.GetInt(ConstructionScheduledFrameKey, 0);
            if (currentFrame > scheduledFrame + 30)
                throw new InvalidOperationException($"Lv3 interactive hitch capture construction did not complete by frame {currentFrame}.");
            return;
        }

        string buildingId = SessionState.GetString(ConstructedBuildingIdKey, string.Empty);
        IEntityContext constructed = ResolveConstructedBuilding(buildingId, s_ConstructionPosition);
        int targetEntityId = SessionState.GetInt(TargetEntityIdKey, 0);
        if (!EntityRegistry.TryGet(new LogicEntityId(targetEntityId), out IEntityContext enemyBuilding)
            || enemyBuilding == null
            || !enemyBuilding.Alive)
        {
            throw new InvalidOperationException(
                $"Lv3 stronghold boundary probe lost enemy building {targetEntityId} before invade.");
        }
        IEntityContext hero = EntityRegistry.Player
                              ?? throw new InvalidOperationException("Lv3 stronghold boundary probe lost the player after construction.");
        if (hero is not ILogicFrameEntity logicHero)
            throw new InvalidOperationException("Lv3 stronghold boundary probe requires the player to implement ILogicFrameEntity.");
        PrepareBuildPhaseStrongholdBoundary(hero, enemyBuilding, logicHero.NavigationAgentTypeId);
        SessionState.SetInt(ConstructionCompleteFrameKey, ToSessionInt(currentFrame));
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForBuildBoundary);
        Log.Info(
            "[Lv3HitchCapture] Construction complete; real Build boundary movement started. frame={0}, built={1}/{2}.",
            currentFrame,
            constructed.LogicEntityId.Value,
            buildingId);
    }

    private static void CompleteBuildBoundaryWhenReady()
    {
        BuildStrongholdBoundaryProbe probe = s_BuildStrongholdBoundaryProbe
                                             ?? throw new InvalidOperationException("Lv3 stronghold boundary probe was lost before completion.");
        if (probe.Failure != null)
            throw new InvalidOperationException("Lv3 real Build boundary movement failed.", probe.Failure);
        if (!probe.IsComplete)
            return;

        ulong currentFrame = LogicFrameRuntime.CurrentFrame;
        SessionState.SetInt(StrongholdProbeCountKey, 1);
        SessionState.SetInt(StrongholdConstrainedStepCountKey, probe.ConstrainedFrameCount);
        SessionState.SetInt(StrongholdSlidingProbeCountKey, probe.SlidingSampleCount);
        SessionState.SetInt(StrongholdBoundaryCompleteFrameKey, ToSessionInt(probe.CompleteFrame));
        PhaseManager.SwitchToPhase(GamePhase.Invade);
        SessionState.SetInt(InvadeScheduledFrameKey, ToSessionInt(currentFrame));
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForInvade);
        Log.Info(
            "[Lv3HitchCapture] Real Build boundary movement verified; invade scheduled. frame={0}, {1}",
            currentFrame,
            probe.BuildSummary());
    }

    private static void PrepareBuildPhaseStrongholdBoundary(
        IEntityContext hero,
        IEntityContext enemyBuilding,
        int agentTypeId)
    {
        if (PhaseManager.CurrentPhase != GamePhase.BuildBeforeInvade
            || LogicPhaseCommandService.CurrentPhase != GamePhase.BuildBeforeInvade)
        {
            throw new InvalidOperationException(
                $"Lv3 stronghold boundary probe requires BuildBeforeInvade. phase={PhaseManager.CurrentPhase}, logicPhase={LogicPhaseCommandService.CurrentPhase}.");
        }
        if (!TryGetForeignStrongholdId(enemyBuilding.PositionFixed, out string strongholdId))
        {
            throw new InvalidOperationException(
                $"Lv3 stronghold boundary probe enemy building {enemyBuilding.LogicEntityId.Value}/{enemyBuilding.CharacterKey} is not inside a foreign stronghold at {enemyBuilding.PositionFixed}.");
        }
        SessionState.SetString(TargetStrongholdIdKey, strongholdId);

        s_Route.Clear();
        float bestLength = float.PositiveInfinity;
        Vector3 bestEndpoint = default;
        string lastFailure = string.Empty;
        const int directionsPerRing = 32;
        for (int radiusIndex = 0; radiusIndex < ApproachSearchRadii.Length; radiusIndex++)
        {
            float radius = ApproachSearchRadii[radiusIndex];
            for (int directionIndex = 0; directionIndex < directionsPerRing; directionIndex++)
            {
                float angle = directionIndex * Mathf.PI * 2f / directionsPerRing;
                Vector3 candidate = enemyBuilding.Position
                                    + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                FixVector2 candidateFixed = new FixVector2((Fix64)candidate.x, (Fix64)candidate.z);
                if (!LogicStrongholdMap.TryResolveStrongholdId(candidateFixed, out string candidateStrongholdId)
                    || !string.Equals(candidateStrongholdId, strongholdId, StringComparison.Ordinal))
                {
                    continue;
                }
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
                    throw new InvalidOperationException($"Build boundary navigation returned {s_CandidateRoute.Count} corners.");

                float length = CalculateRouteLength(s_CandidateRoute);
                if (length >= bestLength)
                    continue;
                bestLength = length;
                bestEndpoint = candidate;
                s_Route.Clear();
                s_Route.AddRange(s_CandidateRoute);
            }
            if (s_Route.Count >= 2)
                break;
        }
        if (s_Route.Count < 2)
        {
            throw new InvalidOperationException(
                $"Lv3 stronghold boundary probe found no navigable route into stronghold '{strongholdId}'. target={enemyBuilding.Position}, lastFailure={lastFailure}");
        }
        if (s_BuildStrongholdBoundaryProbe != null)
            throw new InvalidOperationException("Lv3 stronghold boundary probe is already active.");
        s_BuildStrongholdBoundaryProbe = new BuildStrongholdBoundaryProbe(hero, enemyBuilding, strongholdId, s_Route);
        Log.Info(
            "[Lv3HitchCapture] Build boundary route prepared. stronghold={0}, agentType={1}, endpoint={2}, corners={3}, length={4:F3}.",
            strongholdId,
            agentTypeId,
            bestEndpoint,
            s_Route.Count,
            bestLength);
    }

    private static bool TryGetForeignStrongholdId(FixVector2 position, out string strongholdId)
    {
        if (!LogicStrongholdMap.TryResolveStrongholdId(position, out strongholdId))
            return false;
        return LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId) != EntitySideHelper.PlayerFactionId;
    }

    private static void BeginMovementAfterInvade()
    {
        ulong currentFrame = LogicFrameRuntime.CurrentFrame;
        ulong invadeScheduledFrame = (ulong)SessionState.GetInt(InvadeScheduledFrameKey, 0);
        if (PhaseManager.CurrentPhase != GamePhase.Invade)
        {
            if (currentFrame > invadeScheduledFrame + 5)
                throw new InvalidOperationException($"Lv3 interactive hitch capture invade phase did not apply by frame {currentFrame}.");
            return;
        }

        IEntityContext hero = EntityRegistry.Player
                              ?? throw new InvalidOperationException("Lv3 interactive hitch capture lost the player after invade transition.");
        if (hero is not ILogicFrameEntity logicHero)
            throw new InvalidOperationException("Lv3 interactive hitch capture requires the player to implement ILogicFrameEntity.");
        string targetStrongholdId = SessionState.GetString(TargetStrongholdIdKey, string.Empty);
        if (string.IsNullOrWhiteSpace(targetStrongholdId))
            throw new InvalidOperationException("Lv3 interactive hitch capture lost the target stronghold identity.");
        IEntityContext target = ResolveNearestEnemySoldier(hero.PositionFixed, targetStrongholdId);
        if (target == null)
        {
            if (currentFrame > invadeScheduledFrame + 120)
                throw new InvalidOperationException($"Lv3 interactive hitch capture found no live enemy soldier by frame {currentFrame}.");
            return;
        }

        if (!IsInvadeSpawnSteady(currentFrame))
            return;

        IEntityContext strongholdTarget = ResolveStrongholdRouteTarget();
        BuildNavigationRoute(hero.Position, strongholdTarget.Position, logicHero.NavigationAgentTypeId);
        SessionState.SetInt(TargetEntityIdKey, target.LogicEntityId.Value);
        SessionState.SetInt(LastVisibilityKey, (int)ResolveFogState(target.Position));
        SessionState.SetInt(SpawnSteadyFrameKey, ToSessionInt(currentFrame));
        SessionState.SetInt(NavigationPreparedFrameKey, ToSessionInt(currentFrame));
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForNavigationSteady);
        s_LastMove = FixVector2.Zero;
        Log.Info(
            "[Lv3HitchCapture] Navigation route prepared; waiting for setup debt to settle. frame={0}, hero={1}, target={2}/{3}, routeCorners={4}, routeEnd={5}.",
            currentFrame,
            hero.PositionFixed,
            target.LogicEntityId.Value,
            target.CharacterKey,
            s_Route.Count,
            s_Route[s_Route.Count - 1]);
    }

    private static void BeginCaptureAfterNavigationSettled()
    {
        ulong currentFrame = LogicFrameRuntime.CurrentFrame;
        if (!IsNavigationSetupSteady(currentFrame))
            return;

        IEntityContext hero = EntityRegistry.Player
                              ?? throw new InvalidOperationException("Lv3 interactive hitch capture lost the player before steady capture.");
        int targetEntityId = SessionState.GetInt(TargetEntityIdKey, 0);
        if (!EntityRegistry.TryGet(new LogicEntityId(targetEntityId), out IEntityContext target)
            || target == null
            || !target.Alive)
        {
            string targetStrongholdId = SessionState.GetString(TargetStrongholdIdKey, string.Empty);
            target = ResolveNearestEnemySoldier(hero.PositionFixed, targetStrongholdId);
            if (target == null)
            {
                throw new InvalidOperationException(
                    $"Lv3 interactive hitch capture found no live soldier from stronghold '{targetStrongholdId}' after navigation settled. previousTarget={targetEntityId}.");
            }
            SessionState.SetInt(TargetEntityIdKey, target.LogicEntityId.Value);
            Log.Info(
                "[Lv3HitchCapture] Chase target refreshed after navigation settle. frame={0}, previous={1}, current={2}/{3}.",
                currentFrame,
                targetEntityId,
                target.LogicEntityId.Value,
                target.CharacterKey);
        }

        SessionState.SetInt(CaptureStartFrameKey, ToSessionInt(currentFrame));
        SessionState.SetInt(NavigationSteadyFrameKey, ToSessionInt(currentFrame));
        SessionState.SetInt(CaptureStartAuthorityCountKey, LogicEntityLifecycleService.AuthorityEntityCount);
        SessionState.SetInt(CaptureStartBoundViewCountKey, LogicEntityLifecycleService.BoundViewCount);
        SessionState.SetInt(
            CaptureStartDeferredMicrosecondsKey,
            ToMicroseconds(LogicFrameRuntime.DeferredRealtimeSeconds * 1000.0));
        SessionState.SetInt(ScenarioModeKey, (int)ScenarioMode.Approach);
        SessionState.SetInt(ModeStartFrameKey, ToSessionInt(currentFrame));
        SessionState.SetInt(NextInputFrameKey, ToSessionInt(currentFrame));
        SessionState.SetInt(WaypointIndexKey, 1);
        SessionState.SetInt(StateKey, (int)RunnerState.Running);
        s_LastUpdateTicks = Stopwatch.GetTimestamp();
        s_WaypointStartPosition = hero.Position;
        s_WaypointStartFrame = currentFrame;
        if (s_MovementCommitProbe != null)
            throw new InvalidOperationException("Lv3 interactive hitch capture already has a movement commit probe.");
        s_MovementCommitProbe = new MovementCommitProbe(hero);
        if (s_SoldierChaseProbe != null)
            throw new InvalidOperationException("Lv3 interactive hitch capture already has a soldier chase probe.");
        s_SoldierChaseProbe = new SoldierChaseProbe(hero, target);
        if (s_BatchSoldierChaseProbe != null)
            throw new InvalidOperationException("Lv3 interactive hitch capture already has a batch soldier chase probe.");
        s_BatchSoldierChaseProbe = new BatchSoldierChaseProbe(hero);
        AdvanceMovementScenario(currentFrame);
        Log.Info(
            "[Lv3HitchCapture] Running invade chase after navigation settled. startFrame={0}, hero={1}, target={2}/{3}, routeCorners={4}, routeEnd={5}, authority={6}, bound={7}, deferredRealtimeSeconds={8:F6}.",
            currentFrame,
            hero.PositionFixed,
            target.LogicEntityId.Value,
            target.CharacterKey,
            s_Route.Count,
            s_Route[s_Route.Count - 1],
            LogicEntityLifecycleService.AuthorityEntityCount,
            LogicEntityLifecycleService.BoundViewCount,
            LogicFrameRuntime.DeferredRealtimeSeconds);
    }

    private static bool IsNavigationSetupSteady(ulong currentFrame)
    {
        int renderFrame = Time.frameCount;
        if (renderFrame == SessionState.GetInt(LastNavigationSettleRenderFrameKey, -1))
            return false;
        SessionState.SetInt(LastNavigationSettleRenderFrameKey, renderFrame);

        int authorityCount = LogicEntityLifecycleService.AuthorityEntityCount;
        bool steady = LogicEntityViewSpawnQueue.PendingCount == 0
                      && LogicEntityViewSpawnQueue.InFlightCount == 0
                      && authorityCount > 0
                      && LogicEntityLifecycleService.BoundViewCount == authorityCount
                      && LogicFrameRuntime.DeferredRealtimeSeconds == 0d;
        int steadyRenderFrames = steady
            ? SessionState.GetInt(NavigationSteadyRenderFrameCountKey, 0) + 1
            : 0;
        SessionState.SetInt(NavigationSteadyRenderFrameCountKey, steadyRenderFrames);
        if (steadyRenderFrames < RequiredSpawnSteadyRenderFrames)
            return false;

        Log.Info(
            "[Lv3HitchCapture] Navigation setup settled outside enemy stronghold. frame={0}, renderFrame={1}, steadyRenderFrames={2}, deferredRealtimeSeconds={3:F6}.",
            currentFrame,
            renderFrame,
            steadyRenderFrames,
            LogicFrameRuntime.DeferredRealtimeSeconds);
        return true;
    }

    private static bool IsInvadeSpawnSteady(ulong currentFrame)
    {
        int settleStartFrame = SessionState.GetInt(SpawnSettleStartFrameKey, 0);
        if (settleStartFrame == 0)
        {
            SessionState.SetInt(SpawnSettleStartFrameKey, ToSessionInt(currentFrame));
            Log.Info(
                "[Lv3HitchCapture] Waiting outside enemy stronghold for invade spawn to settle. frame={0}, authority={1}, bound={2}, pending={3}, inFlight={4}, deferredRealtimeSeconds={5:F6}.",
                currentFrame,
                LogicEntityLifecycleService.AuthorityEntityCount,
                LogicEntityLifecycleService.BoundViewCount,
                LogicEntityViewSpawnQueue.PendingCount,
                LogicEntityViewSpawnQueue.InFlightCount,
                LogicFrameRuntime.DeferredRealtimeSeconds);
        }

        int renderFrame = Time.frameCount;
        if (renderFrame == SessionState.GetInt(LastSpawnSettleRenderFrameKey, -1))
            return false;
        SessionState.SetInt(LastSpawnSettleRenderFrameKey, renderFrame);

        int authorityCount = LogicEntityLifecycleService.AuthorityEntityCount;
        bool steady = LogicEntityViewSpawnQueue.IsActive
                      && LogicEntityViewSpawnQueue.PendingCount == 0
                      && LogicEntityViewSpawnQueue.InFlightCount == 0
                      && authorityCount > 0
                      && LogicEntityLifecycleService.BoundViewCount == authorityCount
                      && LogicFrameRuntime.DeferredRealtimeSeconds == 0d;
        int steadyRenderFrames = steady
            ? SessionState.GetInt(SpawnSteadyRenderFrameCountKey, 0) + 1
            : 0;
        SessionState.SetInt(SpawnSteadyRenderFrameCountKey, steadyRenderFrames);
        if (steadyRenderFrames < RequiredSpawnSteadyRenderFrames)
            return false;

        Log.Info(
            "[Lv3HitchCapture] Invade spawn settled outside enemy stronghold. frame={0}, renderFrame={1}, steadyRenderFrames={2}, authority={3}, bound={4}.",
            currentFrame,
            renderFrame,
            steadyRenderFrames,
            authorityCount,
            LogicEntityLifecycleService.BoundViewCount);
        return true;
    }

    private static void ScheduleConstruction(IEntityContext hero, ulong currentFrame)
    {
        BuildManager buildManager = GameEntry.GetComponent<BuildManager>()
                                    ?? throw new InvalidOperationException("Lv3 interactive hitch capture requires BuildManager.");
        IList<IEntityContext> entities = EntityRegistry.AllEntities
                                         ?? throw new InvalidOperationException("Lv3 interactive hitch capture requires EntityRegistry.AllEntities.");
        IBuildingLogicContext selectedOwner = null;
        BuildingData selectedBuilding = null;
        Fix64 selectedDistanceSquared = Fix64.Zero;
        var constructionDiagnostics = new System.Text.StringBuilder();
        int playerLv0Count = 0;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i]
                                    ?? throw new InvalidOperationException($"Lv3 interactive hitch capture found a null entity at index {i}.");
            if (!entity.Alive
                || !entity.TryGetLogicBuilding(out IBuildingLogicContext owner)
                || owner.OwnerFactionId != EntitySideHelper.PlayerFactionId
                || owner.BuildingData == null
                || owner.BuildingData.Lv != 0)
            {
                continue;
            }

            playerLv0Count++;
            List<BuildingData> candidates = buildManager.GetLv0ConstructCandidates(owner);
            List<BuildingData> allCandidates = buildManager.GetLv0ConstructCandidates(owner, requireUnlockedArche: false);
            constructionDiagnostics
                .Append(" owner=").Append(owner.LogicEntityId.Value)
                .Append('/').Append(owner.BuildingData.Identifier)
                .Append(" type=").Append(owner.BuildingData.Type)
                .Append(" unlockedCandidates=").Append(candidates.Count)
                .Append(" allCandidates=").Append(allCandidates.Count);
            for (int allIndex = 0; allIndex < allCandidates.Count; allIndex++)
            {
                BuildingData candidate = allCandidates[allIndex];
                constructionDiagnostics
                    .Append(" candidate=").Append(candidate.Identifier)
                    .Append(" arche=").Append(candidate.Arche)
                    .Append(" visible=").Append(buildManager.IsConstructOptionVisible(owner, candidate.Identifier))
                    .Append(" condition=").Append(buildManager.SatisfyBuildCondition(candidate, owner.OwnerFactionId))
                    .Append(" cost=").Append(buildManager.GetBuildingCost(candidate, owner))
                    .Append(" affordable=").Append(buildManager.HasBuildCost(candidate.Identifier, owner));
            }
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                BuildingData candidate = candidates[candidateIndex];
                if (!buildManager.IsConstructOptionExecutable(owner, candidate.Identifier))
                    continue;

                Fix64 distanceSquared = FixVector2.SqrMagnitude(owner.PositionFixed - hero.PositionFixed);
                if (selectedOwner == null
                    || distanceSquared < selectedDistanceSquared
                    || (distanceSquared == selectedDistanceSquared
                        && owner.LogicEntityId.CompareTo(selectedOwner.LogicEntityId) < 0))
                {
                    selectedOwner = owner;
                    selectedBuilding = candidate;
                    selectedDistanceSquared = distanceSquared;
                }
                break;
            }
        }

        if (selectedOwner == null || selectedBuilding == null)
        {
            throw new InvalidOperationException(
                $"Lv3 interactive hitch capture found no executable player Lv0 construction option. " +
                $"playerLv0Count={playerLv0Count}.{constructionDiagnostics}");
        }
        if (!buildManager.ConstructBuilding(selectedOwner, selectedBuilding.Identifier))
            throw new InvalidOperationException($"Lv3 interactive hitch capture failed to schedule construction '{selectedBuilding.Identifier}'.");

        s_ConstructionPosition = selectedOwner.PositionFixed;
        SessionState.SetInt(ConstructionOwnerEntityIdKey, selectedOwner.LogicEntityId.Value);
        SessionState.SetString(ConstructedBuildingIdKey, selectedBuilding.Identifier);
        SessionState.SetInt(ConstructionScheduledFrameKey, ToSessionInt(currentFrame));
    }

    private static IEntityContext ResolveConstructedBuilding(string buildingId, FixVector2 position)
    {
        IList<IEntityContext> entities = EntityRegistry.AllEntities
                                         ?? throw new InvalidOperationException("Lv3 interactive hitch capture requires EntityRegistry.AllEntities.");
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i]
                                    ?? throw new InvalidOperationException($"Lv3 interactive hitch capture found a null entity at index {i}.");
            if (!entity.Alive
                || !entity.TryGetLogicBuilding(out IBuildingLogicContext building)
                || building.OwnerFactionId != EntitySideHelper.PlayerFactionId
                || building.BuildingData == null
                || !string.Equals(building.BuildingData.Identifier, buildingId, StringComparison.Ordinal))
            {
                continue;
            }
            if (FixVector2.SqrMagnitude(entity.PositionFixed - position) <= Fix64.FromRaw(41))
                return entity;
        }

        throw new InvalidOperationException(
            $"Lv3 interactive hitch capture could not resolve constructed building '{buildingId}' at {position}.");
    }

    private static void AdvanceCapture()
    {
        long now = Stopwatch.GetTimestamp();
        double updateGapMilliseconds = (now - s_LastUpdateTicks) * 1000.0 / Stopwatch.Frequency;
        s_LastUpdateTicks = now;

        ulong currentFrame = LogicFrameRuntime.CurrentFrame;
        ulong captureStartFrame = (ulong)SessionState.GetInt(CaptureStartFrameKey, 0);
        RecordCompletedProfilerFrame(currentFrame, captureStartFrame);
        if (currentFrame >= captureStartFrame + DetectionWarmupFrames
            && updateGapMilliseconds >= HitchThresholdMilliseconds)
        {
            int hitchCount = SessionState.GetInt(ShortHitchCountKey, 0) + 1;
            SessionState.SetInt(ShortHitchCountKey, hitchCount);
            int gapMicroseconds = checked((int)Math.Min(int.MaxValue, Math.Round(updateGapMilliseconds * 1000.0)));
            if (gapMicroseconds > SessionState.GetInt(MaxUpdateGapMicrosecondsKey, 0))
            {
                SessionState.SetInt(MaxUpdateGapMicrosecondsKey, gapMicroseconds);
                SessionState.SetInt(MaxUpdateGapFrameKey, ToSessionInt(currentFrame));
                SessionState.SetString(
                    MaxUpdateGapRuntimeDirtyKey,
                    FlowFieldCrowdMovementSystem.GetEditorRuntimeDirtyJobDiagnostics());
            }
        }

        AdvanceMovementScenario(currentFrame);
        if (currentFrame >= captureStartFrame + CaptureDurationFrames)
            BeginProfilerStop(currentFrame, updateGapMilliseconds);
    }

    private static void RecordCompletedProfilerFrame(ulong currentFrame, ulong captureStartFrame)
    {
        int unityFrame = MainThreadFrameProfiler.LastCompletedFrame;
        if (unityFrame < 0 || unityFrame == SessionState.GetInt(LastSampledProfilerFrameKey, -1))
            return;

        SessionState.SetInt(LastSampledProfilerFrameKey, unityFrame);
        if (currentFrame < captureStartFrame + DetectionWarmupFrames)
            return;

        int logicMicroseconds = ToMicroseconds(MainThreadFrameProfiler.LastCompletedLogicFrameMilliseconds);
        int trackedMicroseconds = ToMicroseconds(MainThreadFrameProfiler.LastCompletedTrackedMilliseconds);
        int untrackedMicroseconds = ToMicroseconds(MainThreadFrameProfiler.LastCompletedUntrackedMilliseconds);
        int hitchThresholdMicroseconds = ToMicroseconds(HitchThresholdMilliseconds);
        if (logicMicroseconds >= hitchThresholdMicroseconds)
        {
            SessionState.SetInt(
                LogicCpuHitchCountKey,
                SessionState.GetInt(LogicCpuHitchCountKey, 0) + 1);
        }
        if (trackedMicroseconds >= hitchThresholdMicroseconds)
        {
            SessionState.SetInt(
                TrackedCpuHitchCountKey,
                SessionState.GetInt(TrackedCpuHitchCountKey, 0) + 1);
        }
        if (logicMicroseconds > SessionState.GetInt(MaxLogicCpuMicrosecondsKey, 0))
        {
            SessionState.SetInt(MaxLogicCpuMicrosecondsKey, logicMicroseconds);
            SessionState.SetInt(MaxLogicCpuUnityFrameKey, unityFrame);
            SessionState.SetInt(MaxLogicCpuFrameKey, ToSessionInt(currentFrame));
        }
        if (trackedMicroseconds > SessionState.GetInt(MaxTrackedMicrosecondsKey, 0))
        {
            SessionState.SetInt(MaxTrackedMicrosecondsKey, trackedMicroseconds);
            SessionState.SetInt(MaxTrackedUnityFrameKey, unityFrame);
            SessionState.SetInt(MaxTrackedLogicFrameKey, ToSessionInt(currentFrame));
            SessionState.SetString(
                MaxTrackedRuntimeDirtyKey,
                FlowFieldCrowdMovementSystem.GetEditorRuntimeDirtyJobDiagnostics());
        }
        if (untrackedMicroseconds > SessionState.GetInt(MaxUntrackedMicrosecondsKey, 0))
        {
            SessionState.SetInt(MaxUntrackedMicrosecondsKey, untrackedMicroseconds);
            SessionState.SetInt(MaxUntrackedUnityFrameKey, unityFrame);
        }
    }

    private static void AdvanceMovementScenario(ulong currentFrame)
    {
        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("Lv3 interactive hitch capture lost InputModel.");
        IEntityContext hero = EntityRegistry.Player
                               ?? throw new InvalidOperationException("Lv3 interactive hitch capture lost the player entity.");
        int targetId = SessionState.GetInt(TargetEntityIdKey, 0);
        if (!EntityRegistry.TryGet(new LogicEntityId(targetId), out IEntityContext target)
            || target == null
            || !target.Alive)
        {
            SoldierChaseProbe chaseProbe = s_SoldierChaseProbe
                                           ?? throw new InvalidOperationException("Lv3 interactive hitch capture lost its soldier chase probe.");
            if (!chaseProbe.IsVerified)
                chaseProbe.MarkTargetDiedAfterAttack();
            target = SwitchToStrongholdRouteTarget(currentFrame, targetId);
        }

        int strongholdTargetId = SessionState.GetInt(StrongholdTargetEntityIdKey, 0);
        if (s_SoldierChaseProbe?.IsVerified == true && target.LogicEntityId.Value != strongholdTargetId)
            target = SwitchToStrongholdRouteTarget(currentFrame, target.LogicEntityId.Value);

        ScenarioMode mode = (ScenarioMode)SessionState.GetInt(ScenarioModeKey, (int)ScenarioMode.Approach);
        ulong modeStartFrame = (ulong)SessionState.GetInt(ModeStartFrameKey, 0);
        Fog3CellState targetVisibility = ResolveFogState(target.Position);
        RecordVisibilityTransition(targetVisibility, currentFrame);
        if (mode == ScenarioMode.Pause && currentFrame >= modeStartFrame + PauseFrames)
        {
            mode = ScenarioMode.Approach;
            modeStartFrame = currentFrame;
            SetScenarioMode(mode, currentFrame, 1);
        }

        ulong nextInputFrame = (ulong)SessionState.GetInt(NextInputFrameKey, 0);
        if (currentFrame < nextInputFrame)
            return;

        FixVector2 move;
        if (mode == ScenarioMode.Pause)
        {
            move = FixVector2.Zero;
        }
        else
        {
            move = ResolveRouteMovement(hero, target, targetVisibility, mode, currentFrame);
            mode = (ScenarioMode)SessionState.GetInt(ScenarioModeKey, (int)mode);
        }

        inputModel.LogicTimeline.EnqueueEditorWorldMoveForNextFrame(move);
        s_LastMove = move;
        SessionState.SetInt(NextInputFrameKey, ToSessionInt(currentFrame + InputRefreshFrames));
    }

    private static IEntityContext SwitchToStrongholdRouteTarget(ulong currentFrame, int previousTargetId)
    {
        IEntityContext target = ResolveStrongholdRouteTarget();
        if (s_Route.Count < 2)
            throw new InvalidOperationException("Lv3 interactive hitch capture lost its stronghold navigation route.");
        SessionState.SetInt(TargetEntityIdKey, target.LogicEntityId.Value);
        Log.Info(
            "[Lv3HitchCapture] Switched to stronghold route target. frame={0}, previous={1}, building={2}, routeCorners={3}.",
            currentFrame,
            previousTargetId,
            target.LogicEntityId.Value,
            s_Route.Count);
        return target;
    }

    private static IEntityContext ResolveStrongholdRouteTarget()
    {
        int strongholdTargetId = SessionState.GetInt(StrongholdTargetEntityIdKey, 0);
        if (strongholdTargetId <= 0
            || !EntityRegistry.TryGet(new LogicEntityId(strongholdTargetId), out IEntityContext target)
            || target == null
            || !target.Alive
            || !target.IsBuildingEntity)
        {
            throw new InvalidOperationException(
                $"Lv3 interactive hitch capture lost its live stronghold building target {strongholdTargetId}.");
        }

        return target;
    }

    private static FixVector2 ResolveRouteMovement(
        IEntityContext hero,
        IEntityContext target,
        Fog3CellState targetVisibility,
        ScenarioMode mode,
        ulong currentFrame)
    {
        if (s_Route.Count < 2)
            throw new InvalidOperationException("Lv3 interactive hitch capture lost its navigation route.");

        int waypointIndex = SessionState.GetInt(WaypointIndexKey, 0);
        if (waypointIndex < 0 || waypointIndex >= s_Route.Count)
            throw new InvalidOperationException($"Lv3 interactive hitch capture has invalid waypoint index {waypointIndex}/{s_Route.Count}.");

        Vector3 heroPosition = hero.Position;
        int routeCycles = SessionState.GetInt(RouteCycleCountKey, 0);
        if (mode == ScenarioMode.Approach && routeCycles == 0)
        {
            SoldierChaseProbe chaseProbe = s_SoldierChaseProbe
                                           ?? throw new InvalidOperationException("Lv3 interactive hitch capture lost its soldier chase probe.");
            MovementCommitProbe movementProbe = s_MovementCommitProbe
                                                ?? throw new InvalidOperationException("Lv3 interactive hitch capture lost its movement probe.");
            bool invadeEntryVerified = movementProbe.ForeignStrongholdEnteredFrame > 0
                                       && (!LogicMovementRegionConstraintService.HasTutorialInvadeTrigger
                                           || movementProbe.TutorialInvadeTriggerConsumedFrame > 0);
            if (chaseProbe.IsVerified && invadeEntryVerified)
            {
                SetScenarioMode(ScenarioMode.Retreat, currentFrame, Math.Max(0, waypointIndex - 1));
                return FixVector2.Zero;
            }
            if (chaseProbe.ProximityFrame > 0 && !chaseProbe.IsVerified)
            {
                chaseProbe.ThrowIfObservationTimedOut(currentFrame);
                return FixVector2.Zero;
            }
        }
        else if (mode == ScenarioMode.Approach && targetVisibility == Fog3CellState.Visible)
        {
            SetScenarioMode(ScenarioMode.Retreat, currentFrame, Math.Max(0, waypointIndex - 1));
            return FixVector2.Zero;
        }
        if (currentFrame >= s_WaypointStartFrame + 90
            && HorizontalDistance(s_WaypointStartPosition, heroPosition) < 0.5f)
        {
            throw new InvalidOperationException(
                $"Lv3 interactive hitch capture stalled at waypoint {waypointIndex}/{s_Route.Count - 1}. " +
                $"frame={currentFrame}, waypointStartFrame={s_WaypointStartFrame}, " +
                $"start={s_WaypointStartPosition}, current={heroPosition}, target={s_Route[waypointIndex]}. " +
                BuildPairCollisionDiagnostics(hero));
        }
        while (HasReachedOrPassedWaypoint(heroPosition, waypointIndex, mode))
        {
            if (mode == ScenarioMode.Approach)
            {
                if (waypointIndex < s_Route.Count - 1)
                {
                    waypointIndex++;
                    SetWaypointIndex(waypointIndex, currentFrame, heroPosition);
                    continue;
                }
                throw new InvalidOperationException(
                    $"Lv3 interactive hitch capture reached route end {s_Route[waypointIndex]} without satisfying its approach condition. " +
                    $"targetVisibility={targetVisibility}, chase={s_SoldierChaseProbe?.BuildSummary()}.");
            }

            if (waypointIndex > 0)
            {
                waypointIndex--;
                SetWaypointIndex(waypointIndex, currentFrame, heroPosition);
                continue;
            }
            if (targetVisibility == Fog3CellState.Visible)
                return FixVector2.Zero;

            int cycleCount = SessionState.GetInt(RouteCycleCountKey, 0) + 1;
            SessionState.SetInt(RouteCycleCountKey, cycleCount);
            SetScenarioMode(ScenarioMode.Pause, currentFrame, 0);
            Log.Info("[Lv3HitchCapture] Route cycle complete. frame={0}, cycles={1}.", currentFrame, cycleCount);
            return FixVector2.Zero;
        }

        Vector3 waypoint = s_Route[waypointIndex];
        var delta = new FixVector2((Fix64)(waypoint.x - heroPosition.x), (Fix64)(waypoint.z - heroPosition.z));
        if (FixVector2.SqrMagnitude(delta) <= Fix64.Zero)
            throw new InvalidOperationException($"Lv3 interactive hitch capture resolved a zero route direction at waypoint {waypointIndex}.");
        return delta.GetNormalized();
    }

    private static bool HasReachedOrPassedWaypoint(Vector3 heroPosition, int waypointIndex, ScenarioMode mode)
    {
        Vector3 waypoint = s_Route[waypointIndex];
        if (HorizontalDistance(heroPosition, waypoint) <= WaypointArrivalDistance)
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

    private static void SetScenarioMode(ScenarioMode mode, ulong currentFrame, int waypointIndex)
    {
        SessionState.SetInt(ScenarioModeKey, (int)mode);
        SessionState.SetInt(ModeStartFrameKey, ToSessionInt(currentFrame));
        SessionState.SetInt(NextInputFrameKey, ToSessionInt(currentFrame));
        SessionState.SetInt(WaypointIndexKey, waypointIndex);
        s_WaypointStartPosition = EntityRegistry.Player?.Position
                                  ?? throw new InvalidOperationException("Lv3 interactive hitch capture lost the player while changing mode.");
        s_WaypointStartFrame = currentFrame;
        Log.Info("[Lv3HitchCapture] Mode changed. frame={0}, mode={1}, waypoint={2}.", currentFrame, mode, waypointIndex);
    }

    private static string BuildPairCollisionDiagnostics(IEntityContext hero)
    {
        if (LogicAgentCollisionShadowService.LastCompletedFrame != LogicFrameRuntime.CurrentFrame)
        {
            return $"collisionShadowFrame={LogicAgentCollisionShadowService.LastCompletedFrame}, " +
                   $"currentFrame={LogicFrameRuntime.CurrentFrame}.";
        }

        IReadOnlyList<LogicAgentCollisionShadowState> states = LogicAgentCollisionShadowService.LastStates;
        LogicAgentCollisionShadowState heroState = default;
        bool foundHero = false;
        for (int i = 0; i < states.Count; i++)
        {
            if (states[i].EntityId != hero.LogicEntityId)
                continue;
            heroState = states[i];
            foundHero = true;
            break;
        }
        if (!foundHero)
            return $"collision shadow has no hero {hero.LogicEntityId.Value}.";

        Fix64 heroRadius = DistanceUnitConverter.ConvertToWorld(
            hero.GetProperty(CreatureMainProperty.CollisionRadius));
        var overlaps = new List<string>();
        var nearby = new List<KeyValuePair<long, string>>();
        for (int i = 0; i < states.Count; i++)
        {
            LogicAgentCollisionShadowState state = states[i];
            if (state.EntityId == hero.LogicEntityId)
                continue;
            if (!EntityRegistry.TryGet(state.EntityId, out IEntityContext entity) || entity == null || !entity.Alive)
                continue;

            Fix64 entityRadius = DistanceUnitConverter.ConvertToWorld(
                entity.GetProperty(CreatureMainProperty.CollisionRadius));
            Fix64 distance = FixVector2.Distance(heroState.ProposedPosition, state.ProposedPosition);
            Fix64 penetration = heroRadius + entityRadius - distance;
            string kind = entity is LogicEntityState logicState && logicState.IsBuildingEntity
                ? $"building,blocks={logicState.BlocksLogicMovement},staticBaked={logicState.IsNavigationStaticBaked}"
                : "unit";
            string description =
                $"id={entity.LogicEntityId.Value},key={entity.CharacterKey},side={entity.Side},kind={kind}," +
                $"position={entity.PositionFixed},proposed={state.ProposedPosition},final={state.FinalResolvedPosition}," +
                $"pairCorrection={state.PairCorrection},staticCorrection={state.StaticCorrection}," +
                $"regionCorrection={state.RegionCorrection},staticContact={state.StaticContactKind}," +
                $"staticCell=({state.StaticContactCellX},{state.StaticContactCellY})," +
                $"runtimeObstacle={state.RuntimeObstacleStableId},radiusRaw={entityRadius.RawValue}," +
                $"distanceRaw={distance.RawValue},penetrationRaw={penetration.RawValue}";
            if (penetration > Fix64.Zero)
                overlaps.Add(description);
            if (entity.Side != hero.Side && distance <= (Fix64)12)
                nearby.Add(new KeyValuePair<long, string>(distance.RawValue, description));
        }
        nearby.Sort((left, right) => left.Key.CompareTo(right.Key));
        var nearestDescriptions = new List<string>();
        for (int i = 0; i < Math.Min(20, nearby.Count); i++)
            nearestDescriptions.Add(nearby[i].Value);
        string navigationCell = heroState.StaticContactKind == LogicStaticCollisionContactKind.AuthoredGridCell
            ? FlowFieldCrowdMovementSystem.GetEditorNavigationCellDiagnostics(
                hero is ILogicFrameEntity logicHero ? logicHero.NavigationAgentTypeId : 0,
                heroState.StaticContactCellX,
                heroState.StaticContactCellY)
            : "<not-authored-grid>";

        return $"heroProposed={heroState.ProposedPosition},heroFinal={heroState.FinalResolvedPosition}," +
               $"heroPairCorrection={heroState.PairCorrection},heroStaticCorrection={heroState.StaticCorrection}," +
               $"heroRegionCorrection={heroState.RegionCorrection},heroStaticContact={heroState.StaticContactKind}," +
               $"heroStaticCell=({heroState.StaticContactCellX},{heroState.StaticContactCellY})," +
               $"heroRuntimeObstacle={heroState.RuntimeObstacleStableId},heroRadiusRaw={heroRadius.RawValue}," +
               $"proposedOverlaps=[{string.Join(" | ", overlaps)}]," +
               $"nearestOpposing=[{string.Join(" | ", nearestDescriptions)}]," +
               $"navigationCell=[{navigationCell}].";
    }

    private static void SetWaypointIndex(int waypointIndex, ulong currentFrame, Vector3 heroPosition)
    {
        SessionState.SetInt(WaypointIndexKey, waypointIndex);
        s_WaypointStartPosition = heroPosition;
        s_WaypointStartFrame = currentFrame;
        Log.Info(
            "[Lv3HitchCapture] Waypoint changed. frame={0}, waypoint={1}/{2}, hero={3}, target={4}.",
            currentFrame,
            waypointIndex,
            s_Route.Count - 1,
            heroPosition,
            s_Route[waypointIndex]);
    }

    private static void RecordVisibilityTransition(Fog3CellState visibility, ulong currentFrame)
    {
        Fog3CellState previous = (Fog3CellState)SessionState.GetInt(LastVisibilityKey, (int)Fog3CellState.Outside);
        if (visibility == previous)
            return;

        SessionState.SetInt(LastVisibilityKey, (int)visibility);
        int transitionCount = SessionState.GetInt(VisibilityTransitionCountKey, 0) + 1;
        SessionState.SetInt(VisibilityTransitionCountKey, transitionCount);
        Log.Info(
            "[Lv3HitchCapture] Target visibility changed. frame={0}, previous={1}, current={2}, transitions={3}.",
            currentFrame,
            previous,
            visibility,
            transitionCount);
    }

    private static void BuildNavigationRoute(Vector3 from, Vector3 target, int agentTypeId)
    {
        s_Route.Clear();
        float bestLength = float.PositiveInfinity;
        Vector3 bestEndpoint = default;
        string lastFailure = string.Empty;
        const int directionsPerRing = 32;

        for (int radiusIndex = 0; radiusIndex < ApproachSearchRadii.Length; radiusIndex++)
        {
            float radius = ApproachSearchRadii[radiusIndex];
            bool foundAtRadius = false;
            for (int directionIndex = 0; directionIndex < directionsPerRing; directionIndex++)
            {
                float angle = directionIndex * Mathf.PI * 2f / directionsPerRing;
                Vector3 candidate = target + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (!FlowFieldCrowdMovementSystem.TryGetNavigationPathCorners(
                        from,
                        candidate,
                        agentTypeId,
                        s_CandidateRoute,
                        out string failureReason))
                {
                    lastFailure = failureReason;
                    continue;
                }
                if (s_CandidateRoute.Count < 2)
                    throw new InvalidOperationException($"Navigation returned an invalid route with {s_CandidateRoute.Count} corners.");

                float length = CalculateRouteLength(s_CandidateRoute);
                if (length >= bestLength)
                    continue;

                bestLength = length;
                bestEndpoint = candidate;
                s_Route.Clear();
                s_Route.AddRange(s_CandidateRoute);
                foundAtRadius = true;
            }
            if (foundAtRadius)
                break;
        }

        if (s_Route.Count < 2)
        {
            throw new InvalidOperationException(
                $"Lv3 interactive hitch capture found no navigable approach point near target {target}. lastFailure={lastFailure}");
        }

        Log.Info(
            "[Lv3HitchCapture] Navigation route built. agentType={0}, from={1}, target={2}, endpoint={3}, corners={4}, length={5:F3}.",
            agentTypeId,
            from,
            target,
            bestEndpoint,
            s_Route.Count,
            bestLength);
    }

    private static float CalculateRouteLength(List<Vector3> route)
    {
        float length = 0f;
        for (int i = 1; i < route.Count; i++)
            length += HorizontalDistance(route[i - 1], route[i]);
        return length;
    }

    private static float HorizontalDistance(Vector3 left, Vector3 right)
    {
        float x = right.x - left.x;
        float z = right.z - left.z;
        return Mathf.Sqrt(x * x + z * z);
    }

    private static void BeginProfilerStop(ulong currentFrame, double updateGapMilliseconds)
    {
        int routeCycles = SessionState.GetInt(RouteCycleCountKey, 0);
        bool logicCpuHitchDetected = SessionState.GetInt(LogicCpuHitchCountKey, 0) > 0;

        InputModel inputModel = GF.DataModel?.GetDataModel<InputModel>()
                                ?? throw new InvalidOperationException("Lv3 interactive hitch capture lost InputModel while stopping.");
        inputModel.LogicTimeline.EnqueueEditorWorldMoveForNextFrame(FixVector2.Zero);

        MovementCommitProbe movementProbe = s_MovementCommitProbe
                                               ?? throw new InvalidOperationException("Lv3 interactive hitch capture lost its movement commit probe.");
        BuildStrongholdBoundaryProbe buildBoundaryProbe = s_BuildStrongholdBoundaryProbe
                                                          ?? throw new InvalidOperationException("Lv3 interactive hitch capture lost its Build boundary probe.");
        if (!buildBoundaryProbe.IsComplete)
            throw new InvalidOperationException("Lv3 interactive hitch capture Build boundary probe did not complete.");
        movementProbe.ValidateInvadeEntry();
        SoldierChaseProbe chaseProbe = s_SoldierChaseProbe
                                        ?? throw new InvalidOperationException("Lv3 interactive hitch capture lost its soldier chase probe.");
        chaseProbe.Validate();
        BatchSoldierChaseProbe batchChaseProbe = s_BatchSoldierChaseProbe
                                                 ?? throw new InvalidOperationException("Lv3 interactive hitch capture lost its batch soldier chase probe.");
        batchChaseProbe.Validate();
        IEntityContext hero = EntityRegistry.Player
                              ?? throw new InvalidOperationException("Lv3 interactive hitch capture lost the player while stopping.");
        if (movementProbe.SampleCount == 0
            || movementProbe.MovingSampleCount == 0
            || movementProbe.StationarySampleCount == 0)
        {
            throw new InvalidOperationException(
                $"Lv3 movement commit probe has incomplete coverage. samples={movementProbe.SampleCount}, moving={movementProbe.MovingSampleCount}, stationary={movementProbe.StationarySampleCount}.");
        }

        int collectionCount = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
        string summary =
            "hitchDetected=" + logicCpuHitchDetected + Environment.NewLine +
            "logicFrame=" + currentFrame + Environment.NewLine +
            "editorUpdateGapMilliseconds=" + updateGapMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + Environment.NewLine +
            "phase=" + PhaseManager.CurrentPhase + Environment.NewLine +
            "input=" + s_LastMove + Environment.NewLine +
            "heroPosition=" + (EntityRegistry.Player?.PositionFixed.ToString() ?? "<null>") + Environment.NewLine +
            "targetEntityId=" + SessionState.GetInt(TargetEntityIdKey, 0) + Environment.NewLine +
            "constructedBuildingId=" + SessionState.GetString(ConstructedBuildingIdKey, string.Empty) + Environment.NewLine +
            "constructionScheduledFrame=" + SessionState.GetInt(ConstructionScheduledFrameKey, 0) + Environment.NewLine +
            "constructionCompleteFrame=" + SessionState.GetInt(ConstructionCompleteFrameKey, 0) + Environment.NewLine +
            "spawnSettleStartFrame=" + SessionState.GetInt(SpawnSettleStartFrameKey, 0) + Environment.NewLine +
            "spawnSteadyFrame=" + SessionState.GetInt(SpawnSteadyFrameKey, 0) + Environment.NewLine +
            "spawnSteadyRenderFrames=" + SessionState.GetInt(SpawnSteadyRenderFrameCountKey, 0) + Environment.NewLine +
            "navigationPreparedFrame=" + SessionState.GetInt(NavigationPreparedFrameKey, 0) + Environment.NewLine +
            "navigationSteadyFrame=" + SessionState.GetInt(NavigationSteadyFrameKey, 0) + Environment.NewLine +
            "navigationSteadyRenderFrames=" + SessionState.GetInt(NavigationSteadyRenderFrameCountKey, 0) + Environment.NewLine +
            "captureStartFrame=" + SessionState.GetInt(CaptureStartFrameKey, 0) + Environment.NewLine +
            "captureStartAuthorityEntities=" + SessionState.GetInt(CaptureStartAuthorityCountKey, 0) + Environment.NewLine +
            "captureStartBoundViews=" + SessionState.GetInt(CaptureStartBoundViewCountKey, 0) + Environment.NewLine +
            "captureStartDeferredRealtimeMilliseconds=" + FormatMicroseconds(SessionState.GetInt(CaptureStartDeferredMicrosecondsKey, 0)) + Environment.NewLine +
            "strongholdBoundaryProbeCount=" + SessionState.GetInt(StrongholdProbeCountKey, 0) + Environment.NewLine +
            "strongholdConstrainedStepCount=" + SessionState.GetInt(StrongholdConstrainedStepCountKey, 0) + Environment.NewLine +
            "strongholdSlidingProbeCount=" + SessionState.GetInt(StrongholdSlidingProbeCountKey, 0) + Environment.NewLine +
            "strongholdBoundaryCompleteFrame=" + SessionState.GetInt(StrongholdBoundaryCompleteFrameKey, 0) + Environment.NewLine +
            buildBoundaryProbe.BuildSummary() +
            "foreignStrongholdEnteredFrame=" + SessionState.GetInt(ForeignStrongholdEnteredFrameKey, 0) + Environment.NewLine +
            "tutorialInvadeTriggerConsumedFrame=" + SessionState.GetInt(TutorialInvadeTriggerConsumedFrameKey, 0) + Environment.NewLine +
            "visibilityTransitions=" + SessionState.GetInt(VisibilityTransitionCountKey, 0) + Environment.NewLine +
            "routeCycles=" + routeCycles + Environment.NewLine +
            "authorityEntities=" + LogicEntityLifecycleService.AuthorityEntityCount + Environment.NewLine +
            "boundViews=" + LogicEntityLifecycleService.BoundViewCount + Environment.NewLine +
            "movementCommitSamples=" + movementProbe.SampleCount + Environment.NewLine +
            "movementMovingSamples=" + movementProbe.MovingSampleCount + Environment.NewLine +
            "movementStationarySamples=" + movementProbe.StationarySampleCount + Environment.NewLine +
            "movementCommitMismatches=0" + Environment.NewLine +
            movementProbe.BuildSummary() +
            "movementCollisionDiagnostics=" + BuildPairCollisionDiagnostics(hero) + Environment.NewLine +
            "chaseVerified=" + chaseProbe.IsVerified + Environment.NewLine +
            chaseProbe.BuildSummary() +
            batchChaseProbe.BuildSummary() +
            "logicCpuHitchCount=" + SessionState.GetInt(LogicCpuHitchCountKey, 0) + Environment.NewLine +
            "maxLogicCpuMilliseconds=" + FormatMicroseconds(SessionState.GetInt(MaxLogicCpuMicrosecondsKey, 0)) + Environment.NewLine +
            "maxLogicCpuUnityFrame=" + SessionState.GetInt(MaxLogicCpuUnityFrameKey, -1) + Environment.NewLine +
            "maxLogicCpuFrame=" + SessionState.GetInt(MaxLogicCpuFrameKey, 0) + Environment.NewLine +
            "trackedCpuHitchCount=" + SessionState.GetInt(TrackedCpuHitchCountKey, 0) + Environment.NewLine +
            "maxTrackedMilliseconds=" + FormatMicroseconds(SessionState.GetInt(MaxTrackedMicrosecondsKey, 0)) + Environment.NewLine +
            "maxTrackedUnityFrame=" + SessionState.GetInt(MaxTrackedUnityFrameKey, -1) + Environment.NewLine +
            "maxTrackedLogicFrame=" + SessionState.GetInt(MaxTrackedLogicFrameKey, 0) + Environment.NewLine +
            "maxTrackedRuntimeDirty=" + SessionState.GetString(MaxTrackedRuntimeDirtyKey, string.Empty) + Environment.NewLine +
            "maxUntrackedMilliseconds=" + FormatMicroseconds(SessionState.GetInt(MaxUntrackedMicrosecondsKey, 0)) + Environment.NewLine +
            "maxUntrackedUnityFrame=" + SessionState.GetInt(MaxUntrackedUnityFrameKey, -1) + Environment.NewLine +
            "shortHitchCount=" + SessionState.GetInt(ShortHitchCountKey, 0) + Environment.NewLine +
            "maxUpdateGapMilliseconds=" + FormatMicroseconds(SessionState.GetInt(MaxUpdateGapMicrosecondsKey, 0)) + Environment.NewLine +
            "maxUpdateGapLogicFrame=" + SessionState.GetInt(MaxUpdateGapFrameKey, 0) + Environment.NewLine +
            "maxUpdateGapRuntimeDirty=" + SessionState.GetString(MaxUpdateGapRuntimeDirtyKey, string.Empty) + Environment.NewLine +
            "runtimeDirty=" + FlowFieldCrowdMovementSystem.GetEditorRuntimeDirtyJobDiagnostics() + Environment.NewLine +
            "gcCollectionCount=" + collectionCount + Environment.NewLine;
        SessionState.SetBool(HitchDetectedKey, logicCpuHitchDetected);
        SessionState.SetString(HitchSummaryKey, summary);
        SessionState.SetInt(StopUpdateCountKey, 0);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForProfilerStop);
        Log.Info(
            "[Lv3HitchCapture] {0}. frame={1}, editorUpdateGap={2:F3}ms, waiting for profiler frame commit.",
            logicCpuHitchDetected ? "Logic CPU hitch reproduced" : "Logic CPU hitch not reproduced before final frame",
            currentFrame,
            updateGapMilliseconds);
    }

    private static void StopProfilerAfterFrameCommit()
    {
        int updateCount = SessionState.GetInt(StopUpdateCountKey, 0) + 1;
        SessionState.SetInt(StopUpdateCountKey, updateCount);
        if (updateCount < 3)
            return;

        SessionState.SetInt(StateKey, (int)RunnerState.Analyzing);
        EditorApplication.isPlaying = false;
    }

    private static void AnalyzeCapture()
    {
        string report =
            "RESULT=" + (SessionState.GetBool(HitchDetectedKey, false) ? "REPRODUCED" : "NOT_REPRODUCED") + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            SessionState.GetString(HitchSummaryKey, string.Empty);
        WriteResult(report);
        Log.Info(
            "[Lv3HitchCapture] Analysis complete. reproduced={0}, report={1}.",
            SessionState.GetBool(HitchDetectedKey, false),
            ResultRelativePath);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        CompleteSession();
    }

    private static string BuildCpuReport(int lastFrame, int hitchFrame)
    {
        int firstFrame = ProfilerDriver.firstFrameIndex;
        int availableLastFrame = ProfilerDriver.lastFrameIndex;
        if (firstFrame < 0 || lastFrame < firstFrame || lastFrame > availableLastFrame)
        {
            throw new InvalidOperationException(
                $"Lv3 interactive hitch CPU report has invalid frame range. first={firstFrame}, requestedLast={lastFrame}, availableLast={availableLastFrame}.");
        }

        int analyzedFrame = -1;
        float analyzedFrameMilliseconds = -1f;
        bool hasHitchFrame = hitchFrame >= firstFrame && hitchFrame <= lastFrame;
        int scanFirstFrame = hasHitchFrame ? Math.Max(firstFrame, hitchFrame - 4) : firstFrame;
        int scanLastFrame = hasHitchFrame ? Math.Min(lastFrame, hitchFrame + 1) : lastFrame;
        var scannedFrameTimes = new List<string>();
        for (int frame = scanFirstFrame; frame <= scanLastFrame; frame++)
        {
            using HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(
                frame,
                0,
                HierarchyFrameDataView.ViewModes.Default,
                HierarchyFrameDataView.columnTotalTime,
                false);
            if (!view.valid)
                continue;
            scannedFrameTimes.Add(frame + ":" + view.frameTimeMs.ToString("F3", CultureInfo.InvariantCulture));
            if (view.frameTimeMs > analyzedFrameMilliseconds)
            {
                analyzedFrame = frame;
                analyzedFrameMilliseconds = view.frameTimeMs;
            }
        }

        if (analyzedFrame < 0)
            throw new InvalidOperationException("Lv3 interactive hitch CPU report found no valid main-thread frames.");

        var entries = new List<CpuEntry>();
        string threadLabel;
        using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(
                   analyzedFrame,
                   0,
                   HierarchyFrameDataView.ViewModes.Default,
                   HierarchyFrameDataView.columnTotalTime,
                   false))
        {
            if (!view.valid)
                throw new InvalidOperationException($"Lv3 interactive hitch analyzed frame {analyzedFrame} became invalid.");
            threadLabel = FormatProfilerThread(view);
            CollectCpuEntries(view, view.GetRootItemID(), entries);
        }

        entries.Sort((left, right) =>
        {
            int totalComparison = right.TotalMilliseconds.CompareTo(left.TotalMilliseconds);
            if (totalComparison != 0)
                return totalComparison;
            int selfComparison = right.SelfMilliseconds.CompareTo(left.SelfMilliseconds);
            return selfComparison != 0 ? selfComparison : string.CompareOrdinal(left.Path, right.Path);
        });

        var writer = new System.Text.StringBuilder();
        writer.Append("profilerFirstFrame=").Append(firstFrame).AppendLine();
        writer.Append("profilerLastFrame=").Append(lastFrame).AppendLine();
        writer.Append("requestedHitchProfilerFrame=").Append(hitchFrame).AppendLine();
        writer.Append("scannedProfilerFrames=").Append(string.Join(",", scannedFrameTimes)).AppendLine();
        writer.Append("analyzedProfilerFrame=").Append(analyzedFrame).AppendLine();
        writer.Append("analyzedProfilerFrameMilliseconds=")
            .Append(analyzedFrameMilliseconds.ToString("F3", CultureInfo.InvariantCulture))
            .AppendLine();
        writer.Append("analyzedProfilerThread=").Append(threadLabel).AppendLine();
        writer.AppendLine("CPU_HIERARCHY:");
        int resultCount = Math.Min(entries.Count, 200);
        for (int i = 0; i < resultCount; i++)
        {
            CpuEntry entry = entries[i];
            writer.Append(i + 1)
                .Append(". totalMs=").Append(entry.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture))
                .Append(", selfMs=").Append(entry.SelfMilliseconds.ToString("F3", CultureInfo.InvariantCulture))
                .Append(", calls=").Append(entry.Calls)
                .Append(", path=").Append(entry.Path)
                .AppendLine();
        }
        return writer.ToString();
    }

    private static void CollectCpuEntries(HierarchyFrameDataView view, int parentId, List<CpuEntry> entries)
    {
        var children = new List<int>();
        view.GetItemChildren(parentId, children);
        int[] childIds = children.ToArray();
        for (int i = 0; i < childIds.Length; i++)
        {
            int childId = childIds[i];
            double totalMilliseconds = view.GetItemColumnDataAsDouble(childId, HierarchyFrameDataView.columnTotalTime);
            if (totalMilliseconds >= 0.1)
            {
                entries.Add(new CpuEntry
                {
                    Path = view.GetItemPath(childId),
                    TotalMilliseconds = totalMilliseconds,
                    SelfMilliseconds = view.GetItemColumnDataAsDouble(childId, HierarchyFrameDataView.columnSelfTime),
                    Calls = (int)view.GetItemColumnDataAsDouble(childId, HierarchyFrameDataView.columnCalls),
                });
            }
            CollectCpuEntries(view, childId, entries);
        }
    }

    private static string FormatProfilerThread(FrameDataView view)
    {
        string groupName = string.IsNullOrEmpty(view.threadGroupName) ? "<no-group>" : view.threadGroupName;
        string threadName = string.IsNullOrEmpty(view.threadName) ? "<unnamed>" : view.threadName;
        return groupName + "/" + threadName + "#" + view.threadId;
    }

    private static IEntityContext ResolvePreferredHiddenEnemyBuilding()
    {
        IEntityContext hero = EntityRegistry.Player
                              ?? throw new InvalidOperationException("Lv3 interactive hitch capture cannot resolve the player entity.");
        IList<IEntityContext> entities = EntityRegistry.AllEntities
                                         ?? throw new InvalidOperationException("Lv3 interactive hitch capture requires EntityRegistry.AllEntities.");
        IEntityContext nearest = null;
        Fix64 nearestDistanceSquared = Fix64.Zero;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i]
                                    ?? throw new InvalidOperationException($"Lv3 interactive hitch capture found a null entity at index {i}.");
            if (!entity.Alive
                || entity.Side != SideType.EnemySide
                || !entity.TryGetLogicBuilding(out IBuildingLogicContext building)
                || building.BuildingData.Lv <= 0
                || !string.Equals(entity.CharacterKey, PreferredTargetCharacterKey, StringComparison.Ordinal))
                continue;
            if (ResolveFogState(entity.Position) != Fog3CellState.Hidden)
                continue;

            Fix64 distanceSquared = FixVector2.SqrMagnitude(entity.PositionFixed - hero.PositionFixed);
            if (nearest == null
                || distanceSquared < nearestDistanceSquared
                || (distanceSquared == nearestDistanceSquared && entity.LogicEntityId.CompareTo(nearest.LogicEntityId) < 0))
            {
                nearest = entity;
                nearestDistanceSquared = distanceSquared;
            }
        }

        return nearest
               ?? throw new InvalidOperationException(
                   $"Lv3 interactive hitch capture found no hidden enemy building '{PreferredTargetCharacterKey}'.");
    }

    private static IEntityContext ResolveNearestEnemySoldier(FixVector2 position, string sourceStrongholdId)
    {
        if (string.IsNullOrWhiteSpace(sourceStrongholdId))
            throw new ArgumentException("Source stronghold id is empty.", nameof(sourceStrongholdId));
        IList<IEntityContext> entities = EntityRegistry.AllEntities
                                         ?? throw new InvalidOperationException("Lv3 interactive hitch capture requires EntityRegistry.AllEntities.");
        IEntityContext nearest = null;
        Fix64 nearestDistanceSquared = Fix64.Zero;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i]
                                    ?? throw new InvalidOperationException($"Lv3 interactive hitch capture found a null entity at index {i}.");
            if (!entity.Alive
                || entity.Side != SideType.EnemySide
                || entity.Brain is not SoldierAIBrain
                || entity is not LogicEntityState logicState
                || !string.Equals(logicState.SourceStrongholdId, sourceStrongholdId, StringComparison.Ordinal))
            {
                continue;
            }

            Fix64 distanceSquared = FixVector2.SqrMagnitude(entity.PositionFixed - position);
            if (nearest == null
                || distanceSquared < nearestDistanceSquared
                || (distanceSquared == nearestDistanceSquared && entity.LogicEntityId.CompareTo(nearest.LogicEntityId) < 0))
            {
                nearest = entity;
                nearestDistanceSquared = distanceSquared;
            }
        }

        return nearest;
    }

    private static Fog3CellState ResolveFogState(Vector3 worldPosition)
    {
        Fog3Manager manager = Fog3Manager.Instance
                              ?? throw new InvalidOperationException("Lv3 interactive hitch capture requires Fog3Manager.");
        Fog3MapData mapData = manager.MapData
                              ?? throw new InvalidOperationException("Lv3 interactive hitch capture requires Fog3MapData.");
        if (!mapData.WorldToGrid(worldPosition, out int gridX, out int gridY))
            return Fog3CellState.Outside;
        return mapData.GetCellState(gridX, gridY);
    }

    private static int ToSessionInt(ulong frame)
    {
        return checked((int)Math.Min(int.MaxValue, frame));
    }

    private static int ToMicroseconds(double milliseconds)
    {
        return checked((int)Math.Min(int.MaxValue, Math.Round(milliseconds * 1000.0)));
    }

    private static string FormatMicroseconds(int microseconds)
    {
        return (microseconds / 1000.0).ToString("F3", CultureInfo.InvariantCulture);
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
            throw new InvalidOperationException($"Invalid Lv3 interactive hitch capture timestamp '{startedText}'.");
        }
        if (DateTime.UtcNow - startedUtc > Timeout)
            throw new TimeoutException($"Lv3 interactive hitch capture exceeded timeout {Timeout}.");
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
        StopProfiler();
        string diagnostics = BuildFailureDiagnostics();
        WriteResult(
            "RESULT=FAIL" + Environment.NewLine +
            "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine +
            "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
            exception + Environment.NewLine +
            diagnostics);
        UnityEngine.Debug.LogException(exception);
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
        else
            CompleteSession();
    }

    private static string BuildFailureDiagnostics()
    {
        string chase = s_SoldierChaseProbe != null ? s_SoldierChaseProbe.BuildSummary() : "chaseProbe=<null>" + Environment.NewLine;
        string batch = s_BatchSoldierChaseProbe != null ? s_BatchSoldierChaseProbe.BuildSummary() : "batchChaseProbe=<null>" + Environment.NewLine;
        string movement = s_MovementCommitProbe != null ? s_MovementCommitProbe.BuildSummary() : "movementProbe=<null>" + Environment.NewLine;
        return
            "failureLogicFrame=" + LogicFrameRuntime.CurrentFrame + Environment.NewLine +
            "lastRenderFrameTickCount=" + LogicFrameRuntime.LastRenderFrameTickCount + Environment.NewLine +
            "logicBacklogSeconds=" + LogicFrameRuntime.BacklogSeconds.ToString("F6", CultureInfo.InvariantCulture) + Environment.NewLine +
            "deferredRealtimeSeconds=" + LogicFrameRuntime.DeferredRealtimeSeconds.ToString("F6", CultureInfo.InvariantCulture) + Environment.NewLine +
            "spawnSettleStartFrame=" + SessionState.GetInt(SpawnSettleStartFrameKey, 0) + Environment.NewLine +
            "spawnSteadyRenderFrames=" + SessionState.GetInt(SpawnSteadyRenderFrameCountKey, 0) + Environment.NewLine +
            "navigationPreparedFrame=" + SessionState.GetInt(NavigationPreparedFrameKey, 0) + Environment.NewLine +
            "navigationSteadyRenderFrames=" + SessionState.GetInt(NavigationSteadyRenderFrameCountKey, 0) + Environment.NewLine +
            "viewSpawnPending=" + LogicEntityViewSpawnQueue.PendingCount + Environment.NewLine +
            "viewSpawnInFlight=" + LogicEntityViewSpawnQueue.InFlightCount + Environment.NewLine +
            "shortHitchCount=" + SessionState.GetInt(ShortHitchCountKey, 0) + Environment.NewLine +
            "maxUpdateGapMilliseconds=" + (SessionState.GetInt(MaxUpdateGapMicrosecondsKey, 0) / 1000.0).ToString("F3", CultureInfo.InvariantCulture) + Environment.NewLine +
            "maxUpdateGapLogicFrame=" + SessionState.GetInt(MaxUpdateGapFrameKey, 0) + Environment.NewLine +
            "maxUpdateGapRuntimeDirty=" + SessionState.GetString(MaxUpdateGapRuntimeDirtyKey, string.Empty) + Environment.NewLine +
            "runtimeDirty=" + FlowFieldCrowdMovementSystem.GetEditorRuntimeDirtyJobDiagnostics() + Environment.NewLine +
            movement +
            chase +
            batch;
    }

    private static void StopProfiler()
    {
        ProfilerDriver.enabled = false;
        ProfilerDriver.profileEditor = false;
        ProfilerDriver.memoryRecordMode = ProfilerMemoryRecordMode.None;
    }

    private static void CompleteSession()
    {
        SessionState.SetBool(RunningKey, false);
        SessionState.EraseInt(StateKey);
    }

    private static void WriteResult(string content)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                             ?? throw new InvalidOperationException("Cannot resolve project root for Lv3 interactive hitch capture.");
        File.WriteAllText(Path.Combine(projectRoot, ResultRelativePath), content);
    }
}
