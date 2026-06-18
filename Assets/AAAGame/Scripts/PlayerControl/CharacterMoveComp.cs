using UnityEngine;
using UnityGameFramework.Runtime;

public class CharacterMoveComp : IMoveComp
{
    private IEntityContext _ctx;
    private Vector3? _targetPos;
    private Vector3 _navDirection = Vector3.zero;
    private bool _isMoving;

    public void Init(IEntityContext ctx) => Init(ctx, 0);

    public void Init(IEntityContext ctx, int agentTypeID)
    {
        _ctx = ctx;
        _targetPos = null;
        _navDirection = Vector3.zero;
        _isMoving = false;
    }

    public void SetNavTarget(Vector3 destination)
    {
        _targetPos = destination;
    }

    public void MoveTo(Vector3 destination)
    {
        _targetPos = destination;
    }

    public void StopMove()
    {
        _targetPos = null;
        _navDirection = Vector3.zero;
        _isMoving = false;
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
            else if (FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(_ctx, _targetPos.Value, speed, out Vector3 steeringVelocity))
            {
                finalVelocity = Vector3.ClampMagnitude(steeringVelocity, speed);
                _navDirection = finalVelocity.sqrMagnitude > 0.0001f
                    ? finalVelocity.normalized
                    : toTarget.normalized;
            }
            else
            {
                throw new System.InvalidOperationException(
                    $"[{_ctx.CharacterKey}] Flow steering rejected target={_targetPos.Value} pos={_ctx.Position} speed={speed:F3}");
            }
        }
        else if (FlowFieldCrowdMovementSystem.TryGetIdleOverlapRecoveryVelocity(_ctx, speed, out Vector3 recoveryVelocity))
        {
            finalVelocity = recoveryVelocity;
            _navDirection = finalVelocity.normalized;
        }

        _ctx.MoveExecutor.SetInput(finalVelocity);
        _isMoving = finalVelocity.sqrMagnitude > 0.0001f;

        if (GameDebugSettings.IsEnabled(DebugCategory.Move)
            && GameDebugSettings.ShouldLogMovementForCharacter(_ctx.CharacterKey))
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[{_ctx.CharacterKey}] Move target={(_targetPos.HasValue ? _targetPos.Value.ToString() : "null")} " +
                $"manual={manualMove} speed={speed:F3} navDir={_navDirection} finalVelocity={finalVelocity}");
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

    private float ResolveArriveDistance()
    {
        float collisionRadius = DistanceUnitConverter.ConvertToWorldFloat(_ctx.GetProperty(CreatureMainProperty.CollisionRadius));
        if (collisionRadius > 0.0001f)
            return Mathf.Max(0.08f, collisionRadius * 0.6f);

        return 0.15f;
    }
}
