using UnityEngine;
using UnityGameFramework.Runtime;

public class CharacterMoveComp : IMoveComp, ILogicDeterministicStateContributor
{
    private static readonly Fix64 s_MoveInputThresholdSquared = Fix64.FromRaw(5);
    private static readonly Fix64 s_MovingThresholdSquared = Fix64.FromRaw(1);

    private IEntityContext _ctx;
    private FixVector2? _targetPosFixed;
    private FixVector2? _preparedTargetPosFixed;
    private FixVector2 _navDirectionFixed = FixVector2.Zero;
    private bool _isMoving;

    public void Init(IEntityContext ctx) => Init(ctx, 0);

    public void Init(IEntityContext ctx, int agentTypeID)
    {
        _ctx = ctx;
        _targetPosFixed = null;
        _preparedTargetPosFixed = null;
        _navDirectionFixed = FixVector2.Zero;
        _isMoving = false;
    }

    public void SetNavTargetFixed(FixVector2 destination)
    {
        _targetPosFixed = destination;
    }

    public void MoveToFixed(FixVector2 destination)
    {
        _targetPosFixed = destination;
    }

    public void StopMove()
    {
        FixVector2? previousTarget = _targetPosFixed;
        bool wasMoving = _isMoving;
        _targetPosFixed = null;
        _preparedTargetPosFixed = null;
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
        Fix64 speed = (_ctx.GetProperty(CreatureMainProperty.Speed));
        FixVector2 finalVelocity = FixVector2.Zero;

        if (FixVector2.SqrMagnitude(manualMove) > s_MoveInputThresholdSquared)
        {
            StopMove();
            _navDirectionFixed = manualMove.GetNormalized();
            finalVelocity = _navDirectionFixed * speed;
        }
        else if (_targetPosFixed.HasValue
                 && _preparedTargetPosFixed.HasValue
                 && _preparedTargetPosFixed.Value == _targetPosFixed.Value)
        {
            FixVector2 frameStartPositionFixed = LogicFrameRuntime.IsTicking
                ? LogicEntityFrameSnapshotService.GetRequiredPosition(_ctx)
                : _ctx.PositionFixed;
            FixVector2 toTargetFixed = _targetPosFixed.Value - frameStartPositionFixed;
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
                    _preparedTargetPosFixed.Value,
                    speed,
                    out FixVector2 steeringVelocity);
                RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.CharacterMoveSteering, steeringStartTicks);
                if (!steeringHit)
                {
                    throw new System.InvalidOperationException(
                        $"[{_ctx.CharacterKey}] Flow steering rejected target={_targetPosFixed.Value} pos={frameStartPositionFixed} speed={(float)speed:F3}");
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

        bool shouldLogMove = _targetPosFixed.HasValue
                             || FixVector2.SqrMagnitude(manualMove) > s_MoveInputThresholdSquared
                             || FixVector2.SqrMagnitude(finalVelocity) > s_MovingThresholdSquared
                             || _isMoving;
        if (shouldLogMove
            && GameDebugSettings.IsEnabled(DebugCategory.Move)
            && GameDebugSettings.ShouldLogMovementForCharacter(_ctx.CharacterKey))
        {
            long logStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            GameDebugSettings.Log(DebugCategory.Move,
                $"[{_ctx.CharacterKey}] Move target={(_targetPosFixed.HasValue ? _targetPosFixed.Value.ToString() : "null")} " +
                $"manual={manualMove} speed={(float)speed:F3} navDir={_navDirectionFixed} finalVelocity={finalVelocity}");
            RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.CharacterMoveDebugLog, logStartTicks);
        }
    }

    public FixVector2 NavDirectionFixed => _navDirectionFixed;

    public void ShutDown()
    {
        StopMove();
    }

    public void Resume()
    {
    }

    public bool IsMoving => _isMoving;
    public bool HasNavigationTarget => _targetPosFixed.HasValue;

    public void CommitResolvedDisplacement(FixVector2 displacement)
    {
        _isMoving = FixVector2.SqrMagnitude(displacement) > s_MovingThresholdSquared;
    }

    public bool TryGetNavigationTargetFixed(out FixVector2 target)
    {
        target = _targetPosFixed.GetValueOrDefault();
        return _targetPosFixed.HasValue;
    }

    public void PrepareNavigationLogicFrame()
    {
        PrepareNavigationLogicFrame(LogicFrameRuntime.FixedDeltaTime);
    }

    public void PrepareNavigationLogicFrame(Fix64 deltaTime)
    {
        _preparedTargetPosFixed = null;
        if (_ctx == null)
            return;
        if (deltaTime <= Fix64.Zero)
            throw new System.ArgumentOutOfRangeException(nameof(deltaTime), deltaTime, "NavigationSync delta time must be positive.");
        if (!_targetPosFixed.HasValue)
        {
            FlowFieldCrowdMovementSystem.ClearNavigationSyncRequest(_ctx);
            return;
        }

        Fix64 speed = _ctx.GetProperty(CreatureMainProperty.Speed);
        if (speed < Fix64.Zero)
            throw new System.InvalidOperationException($"[{_ctx.CharacterKey}] NavigationSync speed cannot be negative: {speed}.");
        long prepareStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        bool prepared = FlowFieldCrowdMovementSystem.TryPrepareNavigationSyncRequestFixed(
                _ctx,
                _targetPosFixed.Value,
                speed * deltaTime,
                out string failureReason);
        RecordPerf(
            UnityGameFramework.Runtime.MainThreadPerfScope.CharacterMovePrepare,
            prepareStartTicks);
        if (!prepared)
        {
            throw new System.InvalidOperationException(
                $"[{_ctx.CharacterKey}] NavigationSync prepare rejected target={_targetPosFixed.Value}: {failureReason}");
        }
        _preparedTargetPosFixed = _targetPosFixed;
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
        Fix64 collisionRadius = (_ctx.GetProperty(CreatureMainProperty.CollisionRadius));
        if (collisionRadius > Fix64.FromRaw(1))
            return Fix64.Max(Fix64.FromRaw(328), collisionRadius * Fix64.FromRaw(2458));

        return Fix64.FromRaw(615);
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
        hasher.Add(_preparedTargetPosFixed.HasValue);
        if (_preparedTargetPosFixed.HasValue)
        {
            hasher.Add(_preparedTargetPosFixed.Value.x.RawValue);
            hasher.Add(_preparedTargetPosFixed.Value.y.RawValue);
        }
        hasher.Add(_navDirectionFixed.x.RawValue);
        hasher.Add(_navDirectionFixed.y.RawValue);
        hasher.Add(_isMoving);
    }
}
