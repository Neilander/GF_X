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
    void SetNavMeshConstrained(bool constrained);
    void SetConstraintBypassForNextFrame(bool bypass = true);
    /// <summary>
    /// 临时无视 NavMesh 约束，直到回到 NavMesh 上自动失效。
    /// 用于：建造后玩家被障碍物围堵、需要走出去时。
    /// </summary>
    void EnableBypassUntilOnNavMesh();
    void Execute();
    void Execute(float deltaTime);
}
