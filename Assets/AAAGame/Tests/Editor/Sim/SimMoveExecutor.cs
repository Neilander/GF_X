using UnityEngine;

/// <summary>
/// 纯逻辑移动执行器：直接改 Position，不依赖 CharacterController。
/// </summary>
public class SimMoveExecutor : IMoveExecutor
{
    public Vector3 Position;
    public Vector3 LastFrameVelocity;

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
        // 纯模拟执行器不做导航约束，bypass 无意义，留空以满足接口。
    }

    public void Execute()
    {
        Execute(0.02f); // 默认 50fps
    }

    public void Execute(float deltaTime)
    {
        // 纯模拟执行器不做导航约束，仅保留接口语义以兼容真实实现。
        _ = _navigationConstrained;

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

        Position += LastFrameVelocity * deltaTime;

        // 每帧重置
        _inputVelocity = Vector3.zero;
        _hasOverride = false;
        _externalVelocity = Vector3.zero;
        _constraintBypassForNextFrame = false;
    }
}
