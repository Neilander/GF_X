
public interface IMoveComp : ICapability
{
    void Init(IEntityContext ctx);
    void Move(Fix64 deltaTime);

    void MoveToFixed(FixVector2 destination);
    void StopMove();

    /// <summary>
    /// 只设导航目标点（供 GetNavDirection 算方向），不设路径（Move 不会沿路径走）。
    /// 用于需要协调器控制实际移动的场景。
    /// </summary>
    void SetNavTargetFixed(FixVector2 destination);

    FixVector2 NavDirectionFixed { get; }
    
    /// <summary>
    /// 是否正在移动
    /// </summary>
    bool IsMoving { get; }
}
