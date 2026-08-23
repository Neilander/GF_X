using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;
using UnityGameFramework.Runtime;

// Aggro behavior contract: AIDoc/当前仇恨与索敌系统.md. Update that document with behavioral changes.
public interface ILastSeenTargetingComp
{
    bool HasLastSeenPursuit { get; }
    FixVector2 LastSeenPositionFixed { get; }
    FixVector2 LastSeenPursuitDestinationFixed { get; }
}

public class CharacterTargetingComp : TargetingCompBase, ITargetingComp, ITargetSearchRangeComp, IFollowTargetingComp, IAlertTargetingComp, IAggroCandidateTargetingComp, IDefendTargetingModeComp, INavigationReachabilityTargetingComp, ILastSeenTargetingComp, ILogicDeterministicStateContributor
{
    private readonly struct AlertRecord
    {
        public AlertRecord(IEntityContext target, Fix64 remaining)
        {
            Target = target;
            Remaining = remaining;
        }

        public IEntityContext Target { get; }
        public Fix64 Remaining { get; }
        public AlertRecord WithRemaining(Fix64 remaining) => new AlertRecord(Target, remaining);
    }

    private readonly struct NavigationRejectedTarget
    {
        public NavigationRejectedTarget(IEntityContext target, FixVector2 targetPosition)
        {
            Target = target;
            TargetPosition = targetPosition;
        }

        public IEntityContext Target { get; }
        public FixVector2 TargetPosition { get; }
    }

    private readonly struct AggroCandidate
    {
        public AggroCandidate(IEntityContext target, Fix64 distance, TargetPriority priority)
        {
            Target = target;
            Distance = distance;
            Priority = priority;
        }

        public IEntityContext Target { get; }
        public Fix64 Distance { get; }
        public TargetPriority Priority { get; }
    }

    private static readonly Fix64 ScanInterval = Fix64.FromRaw(820);
    private static readonly Fix64 LastSeenArrivalRadius = Fix64.FromRaw(410);
    private readonly List<AlertRecord> _alerts = new List<AlertRecord>();
    private IEntityContext _propagatedCandidate;
    private readonly List<NavigationRejectedTarget> _navigationRejectedTargets = new List<NavigationRejectedTarget>();
    private readonly List<AggroCandidate> _candidateBuffer = new List<AggroCandidate>();
    private IEntityContext _ctx;
    private IEntityContext _currentTarget;
    private IEntityContext _lostTarget;
    private FixVector2 _lastSeenPosition;
    private FixVector2 _lastSeenPursuitDestination;
    private Fix64 _scanTimer;
    private IEntityContext _defendFallbackTarget;
    private bool _defendMode;
    private bool _hasNavigationRejectionEpoch;
    private FixVector2 _navigationRejectionSelfPosition;
    private int _navigationRejectionTopologyVersion;

    public IEntityContext CurrentTarget
    {
        get => _currentTarget;
        set => SetCurrentTarget(value, true);
    }

    public IEntityContext AggroTarget => _currentTarget ?? _lostTarget;
    public IEntityContext FollowTarget { get; private set; }
    public bool HasLastSeenPursuit => _lostTarget != null;
    public FixVector2 LastSeenPositionFixed => HasLastSeenPursuit
        ? _lastSeenPosition
        : throw new InvalidOperationException("No last-seen pursuit is active.");
    public FixVector2 LastSeenPursuitDestinationFixed => HasLastSeenPursuit
        ? _lastSeenPursuitDestination
        : throw new InvalidOperationException("No last-seen pursuit is active.");

    private Fix64 m_AggroRange = (Fix64)6;
    private Fix64 m_ForgetRange = (Fix64)8;
    private Fix64 m_FollowSearchRange = (Fix64)30;
    public Fix64 AggroRangeFixed { get => m_AggroRange; set => m_AggroRange = LogicTargetingRange.Require(value, nameof(AggroRangeFixed)); }
    public Fix64 ForgetRangeFixed { get => m_ForgetRange; set => m_ForgetRange = LogicTargetingRange.Require(value, nameof(ForgetRangeFixed)); }
    public Fix64 FollowSearchRangeFixed { get => m_FollowSearchRange; set => m_FollowSearchRange = LogicTargetingRange.Require(value, nameof(FollowSearchRangeFixed)); }

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        if (_ctx.IsBuildingEntity || _ctx.IsHeroEntity)
        {
            throw new InvalidOperationException(
                $"CharacterTargetingComp requires a non-hero unit owner. entity={_ctx.LogicEntityId.Value}.");
        }
        _currentTarget = null;
        _lostTarget = null;
        FollowTarget = null;
        _scanTimer = Fix64.Zero;
        _defendFallbackTarget = null;
        _defendMode = false;
        _alerts.Clear();
        _propagatedCandidate = null;
        ClearNavigationRejections();
    }

    public void UseDefaultMode()
    {
        _defendMode = false;
        _defendFallbackTarget = null;
    }

    public void UseDefendEnemyMode(IEntityContext fallbackTarget)
    {
        _defendMode = true;
        _defendFallbackTarget = fallbackTarget;
    }

    public void UpdateTargeting(Fix64 deltaTime)
    {
        if (_ctx == null)
            throw new InvalidOperationException("CharacterTargetingComp.UpdateTargeting called before Init.");
        if (deltaTime <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (!_ctx.Alive)
        {
            SetCurrentTarget(null, true);
            FollowTarget = null;
            _alerts.Clear();
            _propagatedCandidate = null;
            return;
        }

        AdvanceAlerts(deltaTime);
        _scanTimer += deltaTime;
        bool requiresImmediateScan = _propagatedCandidate != null
                                     || (_currentTarget != null
                                      && (!_currentTarget.IsAttackTargetable()
                                          || !EntityCombatTeamHelper.IsEnemy(_ctx, _currentTarget)
                                          || !LogicFactionVisionService.IsEntityVisibleToSide(_ctx.Side, _currentTarget)))
                                     || (_lostTarget != null
                                         && LogicFactionVisionService.IsEntityVisibleToSide(_ctx.Side, _lostTarget));
        if (_scanTimer < ScanInterval && !requiresImmediateScan)
            return;
        _scanTimer = Fix64.Zero;
        RefreshNavigationRejectionEpoch();
        long evaluateStartTicks = Stopwatch.GetTimestamp();
        try
        {
            EvaluateAggroTarget();
        }
        finally
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.CharacterTargetingEvaluate,
                Stopwatch.GetTimestamp() - evaluateStartTicks);
        }
        MaintainFollowTarget();
    }

    private void EvaluateAggroTarget()
    {
        Fix64 attackRange = GetEffectiveAttackRange();
        Fix64 outerRange = LogicFactionVisionService.ReadWorldDistance(LogicFactionVisionService.AggroOuterRangeConfigKey);
        Fix64 normalCandidateRange = Fix64.Max(
            LogicFactionVisionService.ReadWorldDistance(LogicFactionVisionService.MinimumAggroCandidateRangeConfigKey),
            attackRange);
        bool hadAggroCandidateBeforeEvaluation = HasAggroCandidate();

        IEntityContext previousTarget = _currentTarget;
        if (previousTarget != null && !IsTargetStateValid(previousTarget))
        {
            SetCurrentTarget(null, true);
            previousTarget = null;
        }
        else if (previousTarget != null && LogicFactionVisionService.IsEntityVisibleToSide(_ctx.Side, previousTarget))
        {
            if (_ctx.LogicFrameDistanceToTargetSurfaceFixed(previousTarget) > outerRange)
            {
                SetCurrentTarget(null, true);
                previousTarget = null;
            }
            else
            {
                _lastSeenPosition = previousTarget.LogicFramePositionFixed();
                _lostTarget = null;
            }
        }
        else if (previousTarget != null)
        {
            _lostTarget = previousTarget;
            SetCurrentTarget(null, false);
        }

        if (_lostTarget != null)
        {
            if (!IsTargetStateValid(_lostTarget))
                ClearLostTarget();
            else if (LogicFactionVisionService.IsEntityVisibleToSide(_ctx.Side, _lostTarget)
                     && _ctx.LogicFrameDistanceToTargetSurfaceFixed(_lostTarget) > outerRange)
                ClearLostTarget();
            else if (!LogicFactionVisionService.IsEntityVisibleToSide(_ctx.Side, _lostTarget)
                     && !TryResolveLastSeenPursuitDestination(attackRange, out bool waitForNavigation))
            {
                if (waitForNavigation)
                    return;
                ClearLostTarget();
            }
            else if (FixVector2.Distance(_ctx.LogicFramePositionFixed(), _lastSeenPursuitDestination) <= LastSeenArrivalRadius
                     && !LogicFactionVisionService.IsEntityVisibleToSide(_ctx.Side, _lostTarget))
                ClearLostTarget();
        }

        IEntityContext rankingCurrentTarget = _currentTarget;
        _candidateBuffer.Clear();
        long candidateScanStartTicks = Stopwatch.GetTimestamp();
        try
        {
            IList<IEntityContext> all = EntityRegistry.AllEntities;
            for (int i = 0; i < all.Count; i++)
            {
                IEntityContext candidate = all[i]
                    ?? throw new InvalidOperationException($"CharacterTargetingComp found a null registry entity at index {i}.");
                if (ReferenceEquals(candidate, _ctx) || !IsHardValid(candidate, outerRange))
                    continue;

                Fix64 distance = _ctx.LogicFrameDistanceToTargetSurfaceFixed(candidate);
                bool isAlert = IsAlertTarget(candidate);
                bool isPropagated = IsPropagatedCandidate(candidate);
                bool retained = ReferenceEquals(candidate, rankingCurrentTarget) || ReferenceEquals(candidate, _lostTarget);
                if (distance > normalCandidateRange && !isAlert && !isPropagated && !retained)
                    continue;
                if (!LogicFactionVisionService.IsEntityVisibleToSide(_ctx.Side, candidate))
                    continue;

                TargetPriority priority = TargetPriorityUtility.Create(
                    _ctx,
                    candidate,
                    distance,
                    attackRange,
                    rankingCurrentTarget,
                    isAlert);
                _candidateBuffer.Add(new AggroCandidate(candidate, distance, priority));
            }
        }
        finally
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.CharacterTargetingCandidateScan,
                Stopwatch.GetTimestamp() - candidateScanStartTicks);
        }

        IEntityContext best = ResolveBestReachableCandidate(attackRange, out bool waitForCandidateNavigation);
        _propagatedCandidate = null;
        if (waitForCandidateNavigation)
            return;

        if (best != null)
        {
            SetCurrentTarget(best, true, !hadAggroCandidateBeforeEvaluation);
            _lastSeenPosition = best.LogicFramePositionFixed();
        }
        else if (_currentTarget != null)
        {
            SetCurrentTarget(null, true);
        }

        if (_currentTarget == null && _lostTarget == null && _defendMode && IsFallbackEligible(outerRange))
            SetCurrentTarget(
                _defendFallbackTarget,
                true,
                !hadAggroCandidateBeforeEvaluation);
    }

    private IEntityContext ResolveBestReachableCandidate(Fix64 attackRange, out bool waitForNavigation)
    {
        waitForNavigation = false;
        while (_candidateBuffer.Count > 0)
        {
            int bestIndex = 0;
            for (int i = 1; i < _candidateBuffer.Count; i++)
            {
                if (_candidateBuffer[i].Priority.CompareTo(_candidateBuffer[bestIndex].Priority) > 0)
                    bestIndex = i;
            }

            AggroCandidate candidate = _candidateBuffer[bestIndex];
            int lastIndex = _candidateBuffer.Count - 1;
            _candidateBuffer[bestIndex] = _candidateBuffer[lastIndex];
            _candidateBuffer.RemoveAt(lastIndex);
            bool reachable;
            long reachabilityStartTicks = Stopwatch.GetTimestamp();
            try
            {
                reachable = TryResolveCandidateReachability(
                    candidate.Target,
                    candidate.Distance,
                    attackRange,
                    out waitForNavigation);
            }
            finally
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.CharacterTargetingReachability,
                    Stopwatch.GetTimestamp() - reachabilityStartTicks);
            }
            if (reachable)
            {
                _candidateBuffer.Clear();
                return candidate.Target;
            }
            if (waitForNavigation)
            {
                _candidateBuffer.Clear();
                return null;
            }
        }
        return null;
    }

    private bool IsHardValid(IEntityContext target, Fix64 outerRange)
    {
        return IsTargetStateValid(target)
               && _ctx.LogicFrameDistanceToTargetSurfaceFixed(target) <= outerRange;
    }

    private bool IsTargetStateValid(IEntityContext target)
    {
        return IsValidEnemyTarget(_ctx, target)
               && !IsNavigationRejected(target);
    }

    private bool IsFallbackEligible(Fix64 outerRange)
    {
        return _defendFallbackTarget != null
               && IsHardValid(_defendFallbackTarget, outerRange)
               && LogicFactionVisionService.IsEntityVisibleToSide(_ctx.Side, _defendFallbackTarget);
    }

    private bool TryResolveCandidateReachability(
        IEntityContext target,
        Fix64 distance,
        Fix64 attackRange,
        out bool waitForNavigation)
    {
        waitForNavigation = false;
        if (distance <= attackRange)
            return true;
        if (FlowFieldCrowdMovementSystem.TryResolveReachableAttackAreaPointFixed(
                _ctx,
                target,
                attackRange,
                out _,
                out string failureReason,
                out FlowFieldCrowdMovementSystem.NavigationQueryFailureKind failureKind))
        {
            if (LogicWallRuntime.TryGetBranch(target.LogicEntityId, out _)
                || !LogicWallRuntime.HasBuiltWalls)
                return true;
            bool wallQuerySucceeded;
            Fix64 noWallDistance;
            Fix64 wallDistance;
            bool wallPathReachable;
            string wallFailureReason;
            FlowFieldCrowdMovementSystem.NavigationQueryFailureKind wallFailureKind;
            long wallDetourStartTicks = Stopwatch.GetTimestamp();
            try
            {
                wallQuerySucceeded = FlowFieldCrowdMovementSystem.TryEstimateWallDetourToAttackAreaFixed(
                    _ctx,
                    target,
                    attackRange,
                    out noWallDistance,
                    out wallDistance,
                    out wallPathReachable,
                    out wallFailureReason,
                    out wallFailureKind);
            }
            finally
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.CharacterTargetingWallDetour,
                    Stopwatch.GetTimestamp() - wallDetourStartTicks);
            }
            if (!wallQuerySucceeded)
            {
                switch (wallFailureKind)
                {
                    case FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.PendingRuntimeUpdate:
                        waitForNavigation = true;
                        return false;
                    case FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.Unreachable:
                        GameDebugSettings.Log(
                            DebugCategory.Targeting,
                            $"[{_ctx.CharacterKey}] Skip wall-path-unreachable aggro candidate target={target.CharacterKey} " +
                            $"reason={wallFailureReason}");
                        return false;
                    case FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.Unavailable:
                        throw new InvalidOperationException(
                            $"Wall detour query unavailable. self={_ctx.CharacterKey} target={target.CharacterKey} " +
                            $"reason={wallFailureReason}");
                    default:
                        throw new InvalidOperationException(
                            $"Wall detour query failed without a handled reason. self={_ctx.CharacterKey} " +
                            $"target={target.CharacterKey} kind={wallFailureKind} reason={wallFailureReason}");
                }
            }

            Fix64 detourThreshold = LogicFactionVisionService.ReadWorldDistance(LogicWallRuntime.DetourThresholdConfigKey);
            if (!wallPathReachable || wallDistance - noWallDistance > detourThreshold)
            {
                GameDebugSettings.Log(
                    DebugCategory.Targeting,
                    $"[{_ctx.CharacterKey}] Skip wall-detour aggro candidate target={target.CharacterKey} " +
                    $"noWall={noWallDistance} withWall={wallDistance} threshold={detourThreshold} " +
                    $"wallReachable={wallPathReachable}");
                return false;
            }
            return true;
        }

        switch (failureKind)
        {
            case FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.PendingRuntimeUpdate:
                waitForNavigation = true;
                return false;
            case FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.Unreachable:
                GameDebugSettings.Log(
                    DebugCategory.Targeting,
                    $"[{_ctx.CharacterKey}] Skip navigation-unreachable aggro candidate target={target.CharacterKey} " +
                    $"selfPos={_ctx.LogicFramePositionFixed()} targetPos={target.LogicFramePositionFixed()} " +
                    $"attackRange={attackRange} reason={failureReason}");
                return false;
            case FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.Unavailable:
                throw new InvalidOperationException(
                    $"Aggro reachability query unavailable. self={_ctx.CharacterKey} target={target.CharacterKey} reason={failureReason}");
            default:
                throw new InvalidOperationException(
                    $"Aggro reachability query failed without a handled reason. self={_ctx.CharacterKey} " +
                    $"target={target.CharacterKey} kind={failureKind} reason={failureReason}");
        }
    }

    private bool TryResolveLastSeenPursuitDestination(Fix64 attackRange, out bool waitForNavigation)
    {
        waitForNavigation = false;
        if (FlowFieldCrowdMovementSystem.TryResolveReachablePointAreaFixed(
                _ctx,
                _lastSeenPosition,
                attackRange,
                out _lastSeenPursuitDestination,
                out string failureReason,
                out FlowFieldCrowdMovementSystem.NavigationQueryFailureKind failureKind))
            return true;

        switch (failureKind)
        {
            case FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.PendingRuntimeUpdate:
                waitForNavigation = true;
                return false;
            case FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.Unreachable:
                GameDebugSettings.Log(
                    DebugCategory.Targeting,
                    $"[{_ctx.CharacterKey}] Clear navigation-unreachable last-seen pursuit " +
                    $"lastSeen={_lastSeenPosition} attackRange={attackRange} reason={failureReason}");
                return false;
            case FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.Unavailable:
                throw new InvalidOperationException(
                    $"Last-seen reachability query unavailable. self={_ctx.CharacterKey} reason={failureReason}");
            default:
                throw new InvalidOperationException(
                    $"Last-seen reachability query failed without a handled reason. self={_ctx.CharacterKey} " +
                    $"kind={failureKind} reason={failureReason}");
        }
    }

    private void MaintainFollowTarget()
    {
        if (FollowTarget != null
            && (!FollowTarget.IsRegisteredInLogicWorld()
                || !FollowTarget.Alive
                || FollowTarget.Side != _ctx.Side
                || _ctx.LogicFrameCenterDistanceFixed(FollowTarget) > m_FollowSearchRange))
        {
            FollowTarget = null;
        }
        if (FollowTarget != null)
            return;
        IEntityContext player = EntityRegistry.Player;
        if (player != null
            && player.Alive
            && player.Side == _ctx.Side
            && _ctx.LogicFrameCenterDistanceFixed(player) <= m_FollowSearchRange)
        {
            FollowTarget = player;
        }
    }

    public void NotifyAllyFoundEnemy(IEntityContext enemy)
    {
        if (_ctx == null)
            throw new InvalidOperationException("CharacterTargetingComp.NotifyAllyFoundEnemy called before Init.");
        if (enemy == null)
            throw new ArgumentNullException(nameof(enemy));
        if (!EntityCombatTeamHelper.IsEnemy(_ctx, enemy))
            throw new InvalidOperationException("An alert target must be an enemy of the receiver.");
        Fix64 duration = LogicFactionVisionService.ReadPositiveConfig(LogicFactionVisionService.DamageAlertTargetDurationConfigKey);
        for (int i = 0; i < _alerts.Count; i++)
        {
            if (!ReferenceEquals(_alerts[i].Target, enemy))
                continue;
            _alerts[i] = new AlertRecord(enemy, duration);
            return;
        }
        _alerts.Add(new AlertRecord(enemy, duration));
    }

    public bool TryNotifyAllyAggroCandidate(IEntityContext enemy)
    {
        if (_ctx == null)
            throw new InvalidOperationException("CharacterTargetingComp.TryNotifyAllyAggroCandidate called before Init.");
        if (enemy == null)
            throw new ArgumentNullException(nameof(enemy));
        if (!EntityCombatTeamHelper.IsEnemy(_ctx, enemy))
            throw new InvalidOperationException("A propagated aggro candidate must be an enemy of the receiver.");
        if (HasAggroCandidate())
            return false;
        _propagatedCandidate = enemy;
        return true;
    }

    public void ClearAggro()
    {
        SetCurrentTarget(null, true);
        _alerts.Clear();
        _propagatedCandidate = null;
    }

    private void AdvanceAlerts(Fix64 deltaTime)
    {
        for (int i = _alerts.Count - 1; i >= 0; i--)
        {
            AlertRecord alert = _alerts[i];
            Fix64 remaining = alert.Remaining - deltaTime;
            if (remaining <= Fix64.Zero || alert.Target == null || !alert.Target.Alive)
                _alerts.RemoveAt(i);
            else
                _alerts[i] = alert.WithRemaining(remaining);
        }
    }

    private bool IsAlertTarget(IEntityContext target)
    {
        for (int i = 0; i < _alerts.Count; i++)
        {
            if (ReferenceEquals(_alerts[i].Target, target))
                return true;
        }
        return false;
    }

    private bool IsPropagatedCandidate(IEntityContext target)
    {
        return ReferenceEquals(_propagatedCandidate, target);
    }

    private bool HasAggroCandidate()
    {
        return AggroTarget != null || _alerts.Count > 0 || _propagatedCandidate != null;
    }

    private void SetCurrentTarget(
        IEntityContext value,
        bool clearLostTarget,
        bool allowAggroPropagation = true)
    {
        bool hadAggroCandidate = HasAggroCandidate();
        bool targetChanged = !ReferenceEquals(_currentTarget, value);
        _currentTarget = value;
        if (clearLostTarget)
            ClearLostTarget();
        if (!hadAggroCandidate && targetChanged && value != null && allowAggroPropagation)
            LogicFactionVisionService.HandleAggroAcquired(_ctx, value);
    }

    private void ClearLostTarget()
    {
        _lostTarget = null;
        _lastSeenPosition = FixVector2.Zero;
        _lastSeenPursuitDestination = FixVector2.Zero;
    }

    private Fix64 GetEffectiveAttackRange()
    {
        return GetRequiredAttackRange(_ctx);
    }

    public void RejectNavigationUnreachableTarget(IEntityContext target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        RefreshNavigationRejectionEpoch();
        for (int i = 0; i < _navigationRejectedTargets.Count; i++)
        {
            if (ReferenceEquals(_navigationRejectedTargets[i].Target, target))
                return;
        }
        _navigationRejectedTargets.Add(new NavigationRejectedTarget(target, target.LogicFramePositionFixed()));
        if (ReferenceEquals(_currentTarget, target)) SetCurrentTarget(null, true);
        if (ReferenceEquals(_lostTarget, target)) ClearLostTarget();
    }

    private bool IsNavigationRejected(IEntityContext target)
    {
        for (int i = 0; i < _navigationRejectedTargets.Count; i++)
        {
            if (ReferenceEquals(_navigationRejectedTargets[i].Target, target))
                return true;
        }
        return false;
    }

    private void RefreshNavigationRejectionEpoch()
    {
        FixVector2 selfPosition = _ctx.LogicFramePositionFixed();
        int topologyVersion = FlowFieldCrowdMovementSystem.NavigationTopologyVersion;
        if (!_hasNavigationRejectionEpoch
            || topologyVersion != _navigationRejectionTopologyVersion
            || selfPosition != _navigationRejectionSelfPosition)
        {
            _navigationRejectedTargets.Clear();
            _hasNavigationRejectionEpoch = true;
            _navigationRejectionSelfPosition = selfPosition;
            _navigationRejectionTopologyVersion = topologyVersion;
            return;
        }
        for (int i = _navigationRejectedTargets.Count - 1; i >= 0; i--)
        {
            NavigationRejectedTarget rejected = _navigationRejectedTargets[i];
            if (rejected.Target == null || rejected.Target.LogicFramePositionFixed() != rejected.TargetPosition)
                _navigationRejectedTargets.RemoveAt(i);
        }
    }

    private void ClearNavigationRejections()
    {
        _navigationRejectedTargets.Clear();
        _hasNavigationRejectionEpoch = false;
        _navigationRejectionSelfPosition = FixVector2.Zero;
        _navigationRejectionTopologyVersion = 0;
    }

    public void ShutDown()
    {
        _currentTarget = null;
        ClearLostTarget();
        FollowTarget = null;
        _alerts.Clear();
        _propagatedCandidate = null;
        _candidateBuffer.Clear();
        ClearNavigationRejections();
    }

    public void Resume() { }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null) throw new ArgumentNullException(nameof(hasher));
        hasher.Add(_scanTimer.RawValue);
        hasher.Add(GetLogicId(_currentTarget));
        hasher.Add(GetLogicId(_lostTarget));
        hasher.Add(_lastSeenPosition.x.RawValue);
        hasher.Add(_lastSeenPosition.y.RawValue);
        hasher.Add(_lastSeenPursuitDestination.x.RawValue);
        hasher.Add(_lastSeenPursuitDestination.y.RawValue);
        hasher.Add(GetLogicId(FollowTarget));
        hasher.Add(m_AggroRange.RawValue);
        hasher.Add(m_ForgetRange.RawValue);
        hasher.Add(m_FollowSearchRange.RawValue);
        hasher.Add(_defendMode);
        hasher.Add(GetLogicId(_defendFallbackTarget));
        hasher.Add(_alerts.Count);
        for (int i = 0; i < _alerts.Count; i++)
        {
            hasher.Add(GetLogicId(_alerts[i].Target));
            hasher.Add(_alerts[i].Remaining.RawValue);
        }
        hasher.Add(GetLogicId(_propagatedCandidate));
    }

    private static int GetLogicId(IEntityContext entity) =>
        entity != null && entity.LogicEntityId.IsValid ? entity.LogicEntityId.Value : 0;
}
