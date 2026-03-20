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

    // === 核心：定义你的绝对水平面高度 ===
    private const float GROUND_Y = 1f;

    public void Init(IEntityContext ctx) => _ctx = ctx;

    private float Distance2D(Vector3 p1, Vector3 p2)
    {
        return Vector2.Distance(new Vector2(p1.x, p1.z), new Vector2(p2.x, p2.z));
    }

    public void MoveTo(Vector3 destination)
    {
        // 1. 强行把目标点的 Y 锁死在 1f
        Vector3 flatDest = new Vector3(destination.x, GROUND_Y, destination.z);

        if (_targetPos.HasValue && _corners.Length > 0 && Distance2D(_targetPos.Value, flatDest) < 0.5f) return;

        _targetPos = flatDest;
        _stuckTimer = 0f;
        _lastPos = _ctx.Position;

        NavMeshPath path = new NavMeshPath();
        bool hasNavPath = false;

        if (NavMesh.SamplePosition(_ctx.Position, out var startHit, 10f, NavMesh.AllAreas) &&
            NavMesh.SamplePosition(flatDest, out var endHit, 10f, NavMesh.AllAreas))
        {
            if (NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path) && path.corners.Length > 0)
            {
                _corners = path.corners;

                for (int i = 0; i < _corners.Length; i++)
                {
                    _corners[i].y = GROUND_Y;
                }

                hasNavPath = true;
            }
        }

        if (!hasNavPath)
        {
            _corners = new Vector3[] {
                new Vector3(_ctx.Position.x, GROUND_Y, _ctx.Position.z),
                flatDest
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

        if (manualMove.sqrMagnitude > 0.001f)
        {
            StopMove();
            moveDir = new Vector3(manualMove.x, 0f, manualMove.y).normalized;
        }
        else if (_targetPos.HasValue && _corners.Length > 0 && _currentPathIndex < _corners.Length)
        {
            Vector3 targetCorner = _corners[_currentPathIndex];
            Vector3 offset = new Vector3(targetCorner.x - _ctx.Position.x, 0f, targetCorner.z - _ctx.Position.z);
            float distToCorner = offset.magnitude;

            float arrivalDist = 0.2f;

            if (distToCorner < arrivalDist)
            {
                _currentPathIndex++;
                return;
            }

            moveDir = offset.normalized;

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

        float speed = _ctx.GetProperty(CreatureMainProperty.Speed);
        if (speed <= 0.01f) speed = 5f;

        _ctx.MoveExecutor.SetInput(moveDir * speed * 0.1f);

        // 动画和显示：仅在真实实体上执行
        if (_ctx is GeneralCreature gc)
        {
            if (gc.animator != null)
                gc.animator.SetFloat("Speed", moveDir.magnitude);

            if (gc.display != null && moveDir.sqrMagnitude > 0.001f)
            {
                Vector3 scale = gc.display.localScale;
                scale.x = moveDir.x < -0.01f ? -Mathf.Abs(scale.x) : (moveDir.x > 0.01f ? Mathf.Abs(scale.x) : scale.x);
                gc.display.localScale = scale;
            }
        }
    }

    public Vector3 GetNavDirection()
    {
        if (!_targetPos.HasValue || _corners.Length == 0 || _currentPathIndex >= _corners.Length)
            return Vector3.zero;

        Vector3 targetCorner = _corners[_currentPathIndex];
        Vector3 offset = new Vector3(targetCorner.x - _ctx.Position.x, 0f, targetCorner.z - _ctx.Position.z);
        return offset.sqrMagnitude > 0.001f ? offset.normalized : Vector3.zero;
    }

    public void ShutDown() { StopMove(); }
    public void Resume() { }
}
