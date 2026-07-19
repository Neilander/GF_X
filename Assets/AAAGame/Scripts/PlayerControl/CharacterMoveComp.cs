using UnityEngine;
using UnityGameFramework.Runtime;

public class CharacterMoveComp : IMoveComp, ILogicDeterministicStateContributor
{
    private static readonly Fix64 s_NavigationPrepareTargetEpsilonSquared = (Fix64)0.04f;
    private static readonly Fix64 s_MoveInputThresholdSquared = (Fix64)0.001f;
    private static readonly Fix64 s_MovingThresholdSquared = (Fix64)0.0001f;

    private IEntityContext _ctx;
    private Vector3? _targetPos;
    private FixVector2? _targetPosFixed;
    private FixVector2? _lastPreparedTargetFixed;
    private FixVector2 _navDirectionFixed = FixVector2.Zero;
    private bool _isMoving;

    public void Init(IEntityContext ctx) => Init(ctx, 0);

    public void Init(IEntityContext ctx, int agentTypeID)
    {
        _ctx = ctx;
        _targetPos = null;
        _targetPosFixed = null;
        _lastPreparedTargetFixed = null;
        _navDirectionFixed = FixVector2.Zero;
        _isMoving = false;
    }

    public void SetNavTarget(Vector3 destination)
    {
        SetTarget(destination);
        PrepareFlowNavigationRequest(_targetPos.Value, true);
    }

    public void MoveTo(Vector3 destination)
    {
        SetTarget(destination);
        PrepareFlowNavigationRequest(_targetPos.Value, false);
    }

    public void StopMove()
    {
        Vector3? previousTarget = _targetPos;
        bool wasMoving = _isMoving;
        _targetPos = null;
        _targetPosFixed = null;
        _lastPreparedTargetFixed = null;
        _navDirectionFixed = FixVector2.Zero;
        _isMoving = false;
        if ((previousTarget.HasValue || wasMoving)
            && _ctx != null
            && GameDebugSettings.IsEnabled(DebugCategory.Move)
            && GameDebugSettings.ShouldLogMovementForCharacter(_ctx.CharacterKey))
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[{_ctx.CharacterKey}] StopMove previousTarget={(previousTarget.HasValue ? previousTarget.Value.ToString() : "null")} wasMoving={wasMoving}");
        }
    }

    public void Move(Fix64 deltaTime)
    {
        if (_ctx == null)
            return;

        if (_ctx.MoveExecutor != null && _ctx.MoveExecutor.MovementMode != MovementMode.Normal)
        {
            _ctx.MoveExecutor.SetInputFixed(FixVector2.Zero);
            _isMoving = false;
            if (GameDebugSettings.IsEnabled(DebugCategory.Move)
                && GameDebugSettings.ShouldLogMovementForCharacter(_ctx.CharacterKey))
            {
                GameDebugSettings.Log(DebugCategory.Move,
                    $"[{_ctx.CharacterKey}] Move blocked by MovementMode={_ctx.MoveExecutor.MovementMode}");
            }
            return;
        }

        FixVector2 manualMove = _ctx.Brain?.MoveFixed ?? FixVector2.Zero;
        Fix64 speed = DistanceUnitConverter.ConvertToWorld(_ctx.GetProperty(CreatureMainProperty.Speed));
        FixVector2 finalVelocity = FixVector2.Zero;

        if (FixVector2.SqrMagnitude(manualMove) > s_MoveInputThresholdSquared)
        {
            StopMove();
            _navDirectionFixed = manualMove.GetNormalized();
            finalVelocity = _navDirectionFixed * speed;
        }
        else if (_targetPosFixed.HasValue)
        {
            FixVector2 frameStartPositionFixed = LogicFrameRuntime.IsTicking
                ? LogicEntityFrameSnapshotService.GetRequiredPosition(_ctx)
                : new FixVector2((Fix64)_ctx.Position.x, (Fix64)_ctx.Position.z);
            FixVector2 toTargetFixed = _targetPosFixed.Value - frameStartPositionFixed;
            Vector3 frameStartPosition = new Vector3((float)frameStartPositionFixed.x, _ctx.Position.y, (float)frameStartPositionFixed.y);

            Fix64 arriveDistance = ResolveArriveDistanceFixed();
            if (FixVector2.Magnitude(toTargetFixed) <= arriveDistance)
            {
                StopMove();
            }
            else
            {
                long steeringStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                bool steeringHit = FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
                    _ctx,
                    _targetPosFixed.Value,
                    speed,
                    out FixVector2 steeringVelocity);
                RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.CharacterMoveSteering, steeringStartTicks);
                if (!steeringHit)
                {
                    throw new System.InvalidOperationException(
                        $"[{_ctx.CharacterKey}] Flow steering rejected target={_targetPos.Value} pos={frameStartPosition} speed={(float)speed:F3}");
                }

                finalVelocity = ClampMagnitude(steeringVelocity, speed);
                _navDirectionFixed = FixVector2.SqrMagnitude(finalVelocity) > s_MovingThresholdSquared
                    ? finalVelocity.GetNormalized()
                    : toTargetFixed.GetNormalized();
            }
        }
        else
        {
            finalVelocity = FixVector2.Zero;
        }

        long setInputStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _ctx.MoveExecutor.SetInputFixed(finalVelocity);
        RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.CharacterMoveSetInput, setInputStartTicks);
        _isMoving = FixVector2.SqrMagnitude(finalVelocity) > s_MovingThresholdSquared;

        bool shouldLogMove = _targetPos.HasValue
                             || FixVector2.SqrMagnitude(manualMove) > s_MoveInputThresholdSquared
                             || FixVector2.SqrMagnitude(finalVelocity) > s_MovingThresholdSquared
                             || _isMoving;
        if (shouldLogMove
            && GameDebugSettings.IsEnabled(DebugCategory.Move)
            && GameDebugSettings.ShouldLogMovementForCharacter(_ctx.CharacterKey))
        {
            long logStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            GameDebugSettings.Log(DebugCategory.Move,
                $"[{_ctx.CharacterKey}] Move target={(_targetPos.HasValue ? _targetPos.Value.ToString() : "null")} " +
                $"manual={manualMove} speed={(float)speed:F3} navDir={_navDirectionFixed} finalVelocity={finalVelocity}");
            RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.CharacterMoveDebugLog, logStartTicks);
        }
    }

    public Vector3 GetNavDirection()
    {
        return new Vector3((float)_navDirectionFixed.x, 0f, (float)_navDirectionFixed.y);
    }

    public void ShutDown()
    {
        StopMove();
    }

    public void Resume()
    {
    }

    public bool IsMoving => _isMoving;
    public bool HasNavigationTarget => _targetPos.HasValue;

    public bool TryGetNavigationTarget(out Vector3 target)
    {
        target = _targetPos.GetValueOrDefault();
        return _targetPos.HasValue;
    }

    private void PrepareFlowNavigationRequest(Vector3 destination, bool force)
    {
        if (_ctx == null)
            return;
        if (!force
            && _lastPreparedTargetFixed.HasValue
            && FixVector2.SqrMagnitude(_targetPosFixed.Value - _lastPreparedTargetFixed.Value)
            <= s_NavigationPrepareTargetEpsilonSquared)
        {
            return;
        }

        long prepareStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        bool prepared = FlowFieldCrowdMovementSystem.TryPrepareNavigationRequestFixed(
            _ctx,
            _targetPosFixed.Value,
            out string failureReason);
        if (prepared)
        {
            _lastPreparedTargetFixed = _targetPosFixed.Value;
        }
        else if (GameDebugSettings.IsEnabled(DebugCategory.Move)
                 && GameDebugSettings.ShouldLogMovementForCharacter(_ctx.CharacterKey))
        {
            GameDebugSettings.Log(
                DebugCategory.Move,
                $"[{_ctx.CharacterKey}] Flow prepare pending target={destination} reason={failureReason}");
        }
        RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.CharacterMovePrepare, prepareStartTicks);
    }

    private void SetTarget(Vector3 destination)
    {
        if (float.IsNaN(destination.x) || float.IsInfinity(destination.x)
            || float.IsNaN(destination.z) || float.IsInfinity(destination.z))
        {
            throw new System.ArgumentOutOfRangeException(nameof(destination), destination, "Navigation target must be finite.");
        }

        _targetPosFixed = new FixVector2((Fix64)destination.x, (Fix64)destination.z);
        _targetPos = new Vector3((float)_targetPosFixed.Value.x, destination.y, (float)_targetPosFixed.Value.y);
    }

    private static void RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope scope, long startTicks)
    {
        UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
            scope,
            System.Diagnostics.Stopwatch.GetTimestamp() - startTicks);
    }

    private static FixVector2 ClampMagnitude(FixVector2 value, Fix64 maxMagnitude)
    {
        if (maxMagnitude <= Fix64.Zero)
            return FixVector2.Zero;

        Fix64 squaredMagnitude = FixVector2.SqrMagnitude(value);
        Fix64 squaredLimit = maxMagnitude * maxMagnitude;
        return squaredMagnitude > squaredLimit
            ? value / Fix64.Sqrt(squaredMagnitude) * maxMagnitude
            : value;
    }

    private Fix64 ResolveArriveDistanceFixed()
    {
        Fix64 collisionRadius = DistanceUnitConverter.ConvertToWorld(_ctx.GetProperty(CreatureMainProperty.CollisionRadius));
        if (collisionRadius > (Fix64)0.0001f)
            return Fix64.Max((Fix64)0.08f, collisionRadius * (Fix64)0.6f);

        return (Fix64)0.15f;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new System.ArgumentNullException(nameof(hasher));
        hasher.Add(_targetPosFixed.HasValue);
        if (_targetPosFixed.HasValue)
        {
            hasher.Add(_targetPosFixed.Value.x.RawValue);
            hasher.Add(_targetPosFixed.Value.y.RawValue);
        }
        hasher.Add(_lastPreparedTargetFixed.HasValue);
        if (_lastPreparedTargetFixed.HasValue)
        {
            hasher.Add(_lastPreparedTargetFixed.Value.x.RawValue);
            hasher.Add(_lastPreparedTargetFixed.Value.y.RawValue);
        }
        hasher.Add(_navDirectionFixed.x.RawValue);
        hasher.Add(_navDirectionFixed.y.RawValue);
        hasher.Add(_isMoving);
    }
}
