using UnityEngine;
using UnityEngine.AI;

public class CharacterMoveComp : IMoveComp
{
    private IEntityContext _ctx;
    private Vector3? _targetPos = null;

    private Vector3[] _corners = new Vector3[0];
    private int _currentPathIndex = 0;

    private Vector3 _lastPos;
    private float _stuckTimer = 0f;
    private bool _isMoving = false;

    public void Init(IEntityContext ctx) => _ctx = ctx;

    private float Distance2D(Vector3 p1, Vector3 p2)
    {
        return Vector2.Distance(new Vector2(p1.x, p1.z), new Vector2(p2.x, p2.z));
    }

    public void MoveTo(Vector3 destination)
    {
        Vector3 navDest = destination;
        if (NavMesh.SamplePosition(destination, out var destHit, 10f, NavMesh.AllAreas))
        {
            navDest = destHit.position;
        }

        if (_targetPos.HasValue && _corners.Length > 0 && Distance2D(_targetPos.Value, navDest) < 0.5f) return;

        _targetPos = navDest;
        _stuckTimer = 0f;
        _lastPos = _ctx.Position;

        NavMeshPath path = new NavMeshPath();
        bool hasNavPath = false;

        if (NavMesh.SamplePosition(_ctx.Position, out var startHit, 10f, NavMesh.AllAreas) &&
            NavMesh.SamplePosition(navDest, out var endHit, 10f, NavMesh.AllAreas))
        {
            if (NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path) && path.corners.Length > 0)
            {
                _corners = path.corners;
                hasNavPath = true;
            }
        }

        if (!hasNavPath)
        {
            _corners = new Vector3[] {
                _ctx.Position,
                navDest
            };
        }

        _currentPathIndex = 1;
    }

    public void StopMove()
    {
        _targetPos = null;
        _corners = new Vector3[0];
    }

    public void Move(float deltaTime)
    {
        if (_ctx == null) return;

        Vector2 manualMove = _ctx.Brain?.Move ?? Vector2.zero;
        Vector3 moveDir = Vector3.zero;
        float speed = DistanceUnitConverter.ConvertToWorldFloat(_ctx.GetProperty(CreatureMainProperty.Speed));

        if (manualMove.sqrMagnitude > 0.001f)
        {
            StopMove();
            moveDir = new Vector3(manualMove.x, 0f, manualMove.y).normalized;
        }
        else if (_targetPos.HasValue && _corners.Length > 0 && _currentPathIndex < _corners.Length)
        {
            // 跳过已经很近的 corner，直接瞄准下一个
            while (_currentPathIndex < _corners.Length)
            {
                Vector3 c = _corners[_currentPathIndex];
                float d = new Vector3(c.x - _ctx.Position.x, 0f, c.z - _ctx.Position.z).magnitude;
                if (d >= 0.5f) break;
                _currentPathIndex++;
            }

            if (_currentPathIndex >= _corners.Length)
            {
                StopMove();
                return;
            }

            Vector3 targetCorner = _corners[_currentPathIndex];
            Vector3 offset = new Vector3(targetCorner.x - _ctx.Position.x, 0f, targetCorner.z - _ctx.Position.z);
            float dist = offset.magnitude;


            // 距离不足一帧移动量时按比例减速，防止冲过头
            moveDir = dist < speed * deltaTime ? offset / speed : offset.normalized;

            _stuckTimer += deltaTime;
            if (_stuckTimer >= 0.25f)
            {
                _stuckTimer = 0f;
                float movedDist = Distance2D(_ctx.Position, _lastPos);

                if (movedDist < 0.05f)
                {
                    _targetPos = null;
                }
                _lastPos = _ctx.Position;
            }
        }
        else if (_targetPos.HasValue)
        {
            StopMove();
        }

        //float speed = _ctx.GetProperty(CreatureMainProperty.Speed);
        //if (speed <= 0.01f) speed = 5f;

        _ctx.MoveExecutor.SetInput(moveDir * speed);

        // 更新移动状态
        //Debug.Log( $"Move: moveDir={moveDir}, magnitude={moveDir.magnitude}");
        _isMoving = moveDir.sqrMagnitude > 0.9f;

        // 动画控制由MAEntity统一处理
    }

    public Vector3 GetNavDirection()
    {
        if (!_targetPos.HasValue)
            return Vector3.zero;

        var path = new NavMeshPath();
        Vector3 dir;
        if (NavMesh.SamplePosition(_ctx.Position, out var startHit, 10f, NavMesh.AllAreas) &&
            NavMesh.SamplePosition(_targetPos.Value, out var endHit, 10f, NavMesh.AllAreas) &&
            NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path) &&
            path.corners.Length > 1)
        {
            Vector3 nextCorner = path.corners[1];
            Vector3 offset = new Vector3(nextCorner.x - _ctx.Position.x, 0f, nextCorner.z - _ctx.Position.z);
            dir = offset.sqrMagnitude > 0.001f ? offset.normalized : Vector3.zero;
        }
        else
        {
            // NavMesh 算不出路径，直接朝目标方向
            Vector3 fallback = new Vector3(_targetPos.Value.x - _ctx.Position.x, 0f, _targetPos.Value.z - _ctx.Position.z);
            dir = fallback.sqrMagnitude > 0.001f ? fallback.normalized : Vector3.zero;
        }

        // 可视化：蓝线 = NavMesh 方向
        Debug.DrawRay(_ctx.Position + Vector3.up * 0.5f, dir * 2f, Color.blue);
        return dir;
    }

    public void ShutDown() { StopMove(); }
    public void Resume() { }

    public bool IsMoving => _isMoving;
}
