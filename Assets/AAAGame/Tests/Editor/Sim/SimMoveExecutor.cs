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

    public void Execute()
    {
        Execute(0.02f); // 默认 50fps
    }

    public void Execute(float deltaTime)
    {
        LastFrameVelocity = _hasOverride
            ? _overrideVelocity
            : _inputVelocity + _externalVelocity;

        Position += LastFrameVelocity * deltaTime;

        // 每帧重置
        _inputVelocity = Vector3.zero;
        _hasOverride = false;
        _externalVelocity = Vector3.zero;
    }
}
