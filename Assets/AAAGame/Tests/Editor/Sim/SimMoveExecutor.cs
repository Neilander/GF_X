using UnityEngine;

/// <summary>
/// 纯逻辑移动执行器：直接改 Position，不依赖 CharacterController。
/// </summary>
public class SimMoveExecutor : IMoveExecutor
{
    public Vector3 Position;
    public Vector3 LastInputVelocity;
    public Vector3 LastFrameVelocity;
    public Vector3 LastDesiredDisplacement;
    public Vector3 LastConstrainedDisplacement;
    public bool ApplyNavigationConstraint;
    public int AgentTypeId;
    public float EdgeClearance;
    public bool LastConstraintSucceeded = true;

    private Vector3 _inputVelocity;
    private Vector3 _externalVelocity;
    private Vector3 _overrideVelocity;
    private bool _hasOverride;
    private bool _navigationConstrained = true;
    private bool _constraintBypassForNextFrame;
    private MovementMode _movementMode = MovementMode.Normal;

    public MovementMode MovementMode => _movementMode;

    public void SetInput(Vector3 velocity)
    {
        _inputVelocity = velocity;
        LastInputVelocity = velocity;
    }

    public void AddExternal(Vector3 velocity)
    {
        _externalVelocity += velocity;
    }

    public void SetOverride(Vector3 velocity)
    {
        _overrideVelocity = velocity;
        _hasOverride = true;
    }

    public void ClearOverride()
    {
        _hasOverride = false;
    }

    public void SetExternal(Vector3 velocity)
    {
        _externalVelocity = velocity;
    }

    public void SetMovementMode(MovementMode mode)
    {
        _movementMode = mode;
    }

    public void SetNavigationConstrained(bool constrained)
    {
        _navigationConstrained = constrained;
    }

    public void SetConstraintBypassForNextFrame(bool bypass = true)
    {
        _constraintBypassForNextFrame = bypass;
    }

    public void EnableNavigationConstraintBypass()
    {
        _constraintBypassForNextFrame = true;
    }

    public void Execute()
    {
        Execute(0.02f); // 默认 50fps
    }

    public void Execute(float deltaTime)
    {
        if (_hasOverride)
        {
            LastFrameVelocity = _overrideVelocity;
        }
        else if (_movementMode == MovementMode.Normal)
        {
            LastFrameVelocity = _inputVelocity + _externalVelocity;
        }
        else
        {
            LastFrameVelocity = _externalVelocity;
        }

        Vector3 desiredDisplacement = LastFrameVelocity * deltaTime;
        LastDesiredDisplacement = desiredDisplacement;
        LastConstrainedDisplacement = desiredDisplacement;
        LastConstraintSucceeded = true;
        if (ApplyNavigationConstraint && _navigationConstrained && !_constraintBypassForNextFrame)
        {
            Vector3 horizontal = new Vector3(desiredDisplacement.x, 0f, desiredDisplacement.z);
            if (FlowFieldCrowdMovementSystem.TryConstrainNavigationDisplacement(
                    Position,
                    horizontal,
                    AgentTypeId,
                    EdgeClearance,
                    out Vector3 constrainedHorizontal))
            {
                LastConstrainedDisplacement = new Vector3(constrainedHorizontal.x, desiredDisplacement.y, constrainedHorizontal.z);
                LastFrameVelocity = deltaTime > 0.000001f ? LastConstrainedDisplacement / deltaTime : Vector3.zero;
            }
            else
            {
                LastConstraintSucceeded = false;
                LastConstrainedDisplacement = Vector3.zero;
                LastFrameVelocity = Vector3.zero;
            }
        }

        Position += LastConstrainedDisplacement;

        // 每帧重置
        _inputVelocity = Vector3.zero;
        LastInputVelocity = Vector3.zero;
        _hasOverride = false;
        _externalVelocity = Vector3.zero;
        _constraintBypassForNextFrame = false;
    }
}
