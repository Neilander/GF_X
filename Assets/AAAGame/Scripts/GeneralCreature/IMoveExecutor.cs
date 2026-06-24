using UnityEngine;

public enum MovementMode
{
    Normal = 0,
    Displaced = 1,
    HardLocked = 2
}

/// <summary>
/// 移动执行器接口：真实版通过 CharacterController 移动，测试版直接改坐标。
/// </summary>
public interface IMoveExecutor
{
    MovementMode MovementMode { get; }
    void SetInput(Vector3 velocity);
    void AddExternal(Vector3 velocity);
    void SetOverride(Vector3 velocity);
    void ClearOverride();
    void SetExternal(Vector3 velocity);
    void SetMovementMode(MovementMode mode);
    void SetNavigationConstrained(bool constrained);
    void SetConstraintBypassForNextFrame(bool bypass = true);
    /// <summary>
    /// 临时无视导航约束，调用方必须在合适时机重新启用约束。
    /// 用于：位移/强制移动等不应主动寻路的阶段。
    /// </summary>
    void EnableNavigationConstraintBypass();
    void Execute();
    void Execute(float deltaTime);
}
