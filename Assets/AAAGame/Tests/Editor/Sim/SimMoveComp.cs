using UnityEngine;

/// <summary>
/// 纯逻辑移动组件：直线移动到目标点，不依赖运行时导航。
/// </summary>
public class SimMoveComp : IMoveComp
{
    private IEntityContext _ctx;
    private FixVector2? _targetPosFixed;

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        _targetPosFixed = null;
    }

    public void MoveTo(Vector3 destination)
    {
        MoveToFixed(new FixVector2((Fix64)destination.x, (Fix64)destination.z));
    }

    public void MoveToFixed(FixVector2 destination)
    {
        _targetPosFixed = destination;
    }

    public void StopMove()
    {
        _targetPosFixed = null;
    }

    public void Move(Fix64 deltaTime)
    {
        if (_ctx == null) return;

        FixVector2 moveDirection = FixVector2.Zero;

        // 优先响应 Brain 手动移动
        FixVector2 manualMove = _ctx.Brain?.MoveFixed ?? FixVector2.Zero;
        if (FixVector2.SqrMagnitude(manualMove) > (Fix64)0.001f)
        {
            StopMove();
            moveDirection = manualMove.GetNormalized();
        }
        else if (_targetPosFixed.HasValue)
        {
            FixVector2 offset = _targetPosFixed.Value - _ctx.PositionFixed;

            if (FixVector2.Magnitude(offset) < (Fix64)0.2f)
            {
                StopMove();
                return;
            }

            moveDirection = offset.GetNormalized();
        }

        Fix64 speed = _ctx.GetProperty(CreatureMainProperty.Speed);
        if (speed <= (Fix64)0.01f) speed = (Fix64)5f;

        _ctx.MoveExecutor.SetInputFixed(moveDirection * speed * (Fix64)0.1f);
    }

    public void SetNavTarget(Vector3 destination)
    {
        _targetPosFixed = new FixVector2((Fix64)destination.x, (Fix64)destination.z);
    }

    public Vector3 GetNavDirection()
    {
        if (!_targetPosFixed.HasValue || _ctx == null)
            return Vector3.zero;
        FixVector2 offset = _targetPosFixed.Value - _ctx.PositionFixed;
        if (FixVector2.SqrMagnitude(offset) <= (Fix64)0.001f)
            return Vector3.zero;
        FixVector2 direction = offset.GetNormalized();
        return new Vector3((float)direction.x, 0f, (float)direction.y);
    }

    public bool IsMoving
    {
        get
        {
            if (_ctx == null) return false;
            FixVector2 manualMove = _ctx.Brain?.MoveFixed ?? FixVector2.Zero;
            return _targetPosFixed.HasValue || FixVector2.SqrMagnitude(manualMove) > (Fix64)0.001f;
        }
    }

    public void ShutDown() { StopMove(); }
    public void Resume() { }
}
