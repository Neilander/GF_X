
using UnityEngine;

public interface IMoveComp : ICapability
{
    void Init(IEntityContext ctx);
    void Move(Fix64 deltaTime);

    void MoveTo(Vector3 destination);
    void MoveToFixed(FixVector2 destination);
    void StopMove();

    /// <summary>
    /// 只设导航目标点（供 GetNavDirection 算方向），不设路径（Move 不会沿路径走）。
    /// 用于需要协调器控制实际移动的场景。
    /// </summary>
    void SetNavTarget(Vector3 destination);

    /// <summary>
    /// 获取当前导航目标的下一步归一化方向，无路径时返回 Vector3.zero。
    /// </summary>
    Vector3 GetNavDirection();
    
    /// <summary>
    /// 是否正在移动
    /// </summary>
    bool IsMoving { get; }
}
