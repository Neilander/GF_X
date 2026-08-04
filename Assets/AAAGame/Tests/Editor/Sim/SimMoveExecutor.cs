using UnityEngine;

/// <summary>
/// 纯逻辑移动执行器：直接改 Position，不依赖 CharacterController。
/// </summary>
public class SimMoveExecutor : IMoveExecutor
{
    public Vector3 Position;
    public Vector3 LastInputVelocity;
    public FixVector2 LastFixedInput;
    public bool HasFixedInput;
    public FixVector2 LastFixedExternal;
    public FixVector2 LastFixedOverride;
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
        HasFixedInput = false;
    }

    public void SetInputFixed(FixVector2 velocity, bool preserveSpeedOnStaticSlide = false)
    {
        LastFixedInput = velocity;
        HasFixedInput = true;
        _inputVelocity = new Vector3((float)velocity.x, 0f, (float)velocity.y);
        LastInputVelocity = _inputVelocity;
    }

    public void AddExternal(Vector3 velocity)
    {
        AddExternalFixed(new FixVector2((Fix64)velocity.x, (Fix64)velocity.z));
    }

    public void AddExternalFixed(FixVector2 velocity)
    {
        LastFixedExternal += velocity;
        _externalVelocity = new Vector3((float)LastFixedExternal.x, 0f, (float)LastFixedExternal.y);
    }

    public void SetOverride(Vector3 velocity)
    {
        _overrideVelocity = velocity;
        _hasOverride = true;
    }

    public void SetOverrideFixed(FixVector2 velocity)
    {
        LastFixedOverride = velocity;
        SetOverride(new Vector3((float)velocity.x, 0f, (float)velocity.y));
    }

    public void ClearOverride()
    {
        _hasOverride = false;
    }

    public void SetExternal(Vector3 velocity)
    {
        SetExternalFixed(new FixVector2((Fix64)velocity.x, (Fix64)velocity.z));
    }

    public void SetExternalFixed(FixVector2 velocity)
    {
        LastFixedExternal = velocity;
        _externalVelocity = new Vector3((float)velocity.x, 0f, (float)velocity.y);
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
        LastFixedExternal = FixVector2.Zero;
        LastFixedOverride = FixVector2.Zero;
        _constraintBypassForNextFrame = false;
    }
}
