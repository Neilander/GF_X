using UnityEngine;
using UnityGameFramework.Runtime;

public class CharacterMoveComp : IMoveComp
{
    private const float NavigationPrepareTargetEpsilonSq = 0.04f;

    private IEntityContext _ctx;
    private Vector3? _targetPos;
    private Vector3? _lastPreparedTarget;
    private Vector3 _navDirection = Vector3.zero;
    private bool _isMoving;

    public void Init(IEntityContext ctx) => Init(ctx, 0);

    public void Init(IEntityContext ctx, int agentTypeID)
    {
        _ctx = ctx;
        _targetPos = null;
        _lastPreparedTarget = null;
        _navDirection = Vector3.zero;
        _isMoving = false;
    }

    public void SetNavTarget(Vector3 destination)
    {
        _targetPos = destination;
        PrepareFlowNavigationRequest(destination, true);
    }

    public void MoveTo(Vector3 destination)
    {
        _targetPos = destination;
        PrepareFlowNavigationRequest(destination, false);
    }

    public void StopMove()
    {
        Vector3? previousTarget = _targetPos;
        bool wasMoving = _isMoving;
        _targetPos = null;
        _lastPreparedTarget = null;
        _navDirection = Vector3.zero;
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

    public void Move(float deltaTime)
    {
        if (_ctx == null)
            return;

        if (_ctx.MoveExecutor != null && _ctx.MoveExecutor.MovementMode != MovementMode.Normal)
        {
            _ctx.MoveExecutor.SetInput(Vector3.zero);
            _isMoving = false;
            if (GameDebugSettings.IsEnabled(DebugCategory.Move)
                && GameDebugSettings.ShouldLogMovementForCharacter(_ctx.CharacterKey))
            {
                GameDebugSettings.Log(DebugCategory.Move,
                    $"[{_ctx.CharacterKey}] Move blocked by MovementMode={_ctx.MoveExecutor.MovementMode}");
            }
            return;
        }

        Vector2 manualMove = _ctx.Brain?.Move ?? Vector2.zero;
        float speed = DistanceUnitConverter.ConvertToWorldFloat(_ctx.GetProperty(CreatureMainProperty.Speed));
        Vector3 finalVelocity = Vector3.zero;

        if (manualMove.sqrMagnitude > 0.001f)
        {
            StopMove();
            _navDirection = new Vector3(manualMove.x, 0f, manualMove.y).normalized;
            finalVelocity = _navDirection * speed;
        }
        else if (_targetPos.HasValue)
        {
            Vector3 toTarget = _targetPos.Value - _ctx.Position;
            toTarget.y = 0f;

            float arriveDistance = ResolveArriveDistance();
            if (toTarget.magnitude <= arriveDistance)
            {
                StopMove();
            }
            else
            {
                long steeringStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                bool steeringHit = FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(_ctx, _targetPos.Value, speed, out Vector3 steeringVelocity);
                RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.CharacterMoveSteering, steeringStartTicks);
                if (!steeringHit)
                {
                    throw new System.InvalidOperationException(
                        $"[{_ctx.CharacterKey}] Flow steering rejected target={_targetPos.Value} pos={_ctx.Position} speed={speed:F3}");
                }

                finalVelocity = Vector3.ClampMagnitude(steeringVelocity, speed);
                _navDirection = finalVelocity.sqrMagnitude > 0.0001f
                    ? finalVelocity.normalized
                    : toTarget.normalized;
            }
        }
        else
        {
            long recoveryStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            bool recoveryHit = FlowFieldCrowdMovementSystem.TryGetIdleOverlapRecoveryVelocity(_ctx, speed, out Vector3 recoveryVelocity);
            RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.CharacterMoveIdleRecovery, recoveryStartTicks);
            if (recoveryHit)
            {
                finalVelocity = recoveryVelocity;
                _navDirection = finalVelocity.normalized;
            }
        }

        long setInputStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _ctx.MoveExecutor.SetInput(finalVelocity);
        RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.CharacterMoveSetInput, setInputStartTicks);
        _isMoving = finalVelocity.sqrMagnitude > 0.0001f;

        bool shouldLogMove = _targetPos.HasValue
                             || manualMove.sqrMagnitude > 0.001f
                             || finalVelocity.sqrMagnitude > 0.0001f
                             || _isMoving;
        if (shouldLogMove
            && GameDebugSettings.IsEnabled(DebugCategory.Move)
            && GameDebugSettings.ShouldLogMovementForCharacter(_ctx.CharacterKey))
        {
            long logStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            GameDebugSettings.Log(DebugCategory.Move,
                $"[{_ctx.CharacterKey}] Move target={(_targetPos.HasValue ? _targetPos.Value.ToString() : "null")} " +
                $"manual={manualMove} speed={speed:F3} navDir={_navDirection} finalVelocity={finalVelocity}");
            RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.CharacterMoveDebugLog, logStartTicks);
        }
    }

    public Vector3 GetNavDirection()
    {
        return _navDirection;
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
            && _lastPreparedTarget.HasValue
            && (destination - _lastPreparedTarget.Value).sqrMagnitude <= NavigationPrepareTargetEpsilonSq)
        {
            return;
        }

        long prepareStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        bool prepared = FlowFieldCrowdMovementSystem.TryPrepareNavigationRequest(_ctx, destination, out string failureReason);
        if (prepared)
        {
            _lastPreparedTarget = destination;
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

    private static void RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope scope, long startTicks)
    {
        UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
            scope,
            System.Diagnostics.Stopwatch.GetTimestamp() - startTicks);
    }

    private float ResolveArriveDistance()
    {
        float collisionRadius = DistanceUnitConverter.ConvertToWorldFloat(_ctx.GetProperty(CreatureMainProperty.CollisionRadius));
        if (collisionRadius > 0.0001f)
            return Mathf.Max(0.08f, collisionRadius * 0.6f);

        return 0.15f;
    }
}
