using UnityEngine;

public enum MovementMode
{
    Normal = 0,
    Displaced = 1,
    HardLocked = 2
}

/// <summary>
/// 逻辑移动状态接口，只接受定点速度并由统一逻辑帧管线提交。
/// </summary>
public interface IMoveExecutor
{
    MovementMode MovementMode { get; }
    void SetInputFixed(FixVector2 velocity, bool preserveSpeedOnStaticSlide = false);
    void AddExternalFixed(FixVector2 velocity);
    void SetOverrideFixed(FixVector2 velocity);
    void ClearOverride();
    void SetExternalFixed(FixVector2 velocity);
    void SetMovementMode(MovementMode mode);
    void SetNavigationConstrained(bool constrained);
    void SetConstraintBypassForNextFrame(bool bypass = true);
    /// <summary>
    /// 临时无视导航约束，调用方必须在合适时机重新启用约束。
    /// 用于：位移/强制移动等不应主动寻路的阶段。
    /// </summary>
    void EnableNavigationConstraintBypass();
}
