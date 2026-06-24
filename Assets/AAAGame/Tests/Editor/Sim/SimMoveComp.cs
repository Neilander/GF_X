using UnityEngine;

/// <summary>
/// 纯逻辑移动组件：直线移动到目标点，不依赖运行时导航。
/// </summary>
public class SimMoveComp : IMoveComp
{
    private IEntityContext _ctx;
    private Vector3? _targetPos;

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        _targetPos = null;
    }

    public void MoveTo(Vector3 destination)
    {
        _targetPos = destination;
    }

    public void StopMove()
    {
        _targetPos = null;
    }

    public void Move(float deltaTime)
    {
        if (_ctx == null) return;

        Vector3 moveDir = Vector3.zero;

        // 优先响应 Brain 手动移动
        Vector2 manualMove = _ctx.Brain?.Move ?? Vector2.zero;
        if (manualMove.sqrMagnitude > 0.001f)
        {
            StopMove();
            moveDir = new Vector3(manualMove.x, 0f, manualMove.y).normalized;
        }
        else if (_targetPos.HasValue)
        {
            Vector3 offset = _targetPos.Value - _ctx.Position;
            offset.y = 0f;

            if (offset.magnitude < 0.2f)
            {
                StopMove();
                return;
            }

            moveDir = offset.normalized;
        }

        Fix64 speed = _ctx.GetProperty(CreatureMainProperty.Speed);
        if (speed <= (Fix64)0.01f) speed = (Fix64)5f;

        _ctx.MoveExecutor.SetInput(moveDir * (float)speed * 0.1f);
    }

    public void SetNavTarget(Vector3 destination) { _targetPos = destination; }

    public Vector3 GetNavDirection()
    {
        if (!_targetPos.HasValue || _ctx == null) return Vector3.zero;
        Vector3 offset = _targetPos.Value - _ctx.Position;
        offset.y = 0f;
        return offset.sqrMagnitude > 0.001f ? offset.normalized : Vector3.zero;
    }

    public bool IsMoving
    {
        get
        {
            if (_ctx == null) return false;
            Vector2 manualMove = _ctx.Brain?.Move ?? Vector2.zero;
            return _targetPos.HasValue || manualMove.sqrMagnitude > 0.001f;
        }
    }

    public void ShutDown() { StopMove(); }
    public void Resume() { }
}
