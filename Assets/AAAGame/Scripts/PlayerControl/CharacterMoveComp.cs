using UnityEngine;
using UnityEngine.AI;

public class CharacterMoveComp : IMoveComp
{
    private MAEntity _entity;
    private Vector3? _targetPos = null;
    
    private Vector3[] _corners = new Vector3[0];
    private int _currentPathIndex = 0;

    private Vector3 _lastPos;
    private float _stuckTimer = 0f;

    // === 核心：定义你的绝对水平面高度 ===
    private const float GROUND_Y = 1f; 

    public void Init(MAEntity entity) => _entity = entity;

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
        _lastPos = _entity.transform.position;

        NavMeshPath path = new NavMeshPath();
        bool hasNavPath = false;

        // NavMesh 采样时，依然允许它在上下 10f 的范围内找网格（因为网格通常烘焙在 Y=0 地板上）
        if (NavMesh.SamplePosition(_entity.transform.position, out var startHit, 10f, NavMesh.AllAreas) &&
            NavMesh.SamplePosition(flatDest, out var endHit, 10f, NavMesh.AllAreas))
        {
            if (NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path) && path.corners.Length > 0)
            {
                _corners = path.corners;
                
                // 2. 强行把算出来的所有拐角点 Y 锁死在 1f
                for (int i = 0; i < _corners.Length; i++)
                {
                    _corners[i].y = GROUND_Y;
                }
                
                hasNavPath = true;
            }
        }

        if (!hasNavPath)
        {
            // 如果没找到路，兜底的直线也是绝对的 Y=1f
            _corners = new Vector3[] { 
                new Vector3(_entity.transform.position.x, GROUND_Y, _entity.transform.position.z), 
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

    public void Move()
    {
        if (_entity == null) return;

        Vector2 manualMove = _entity.Brain?.Move ?? Vector2.zero;
        Vector3 moveDir = Vector3.zero;

        if (manualMove.sqrMagnitude > 0.001f)
        {
            StopMove();
            moveDir = new Vector3(manualMove.x, 0f, manualMove.y).normalized;
        }
        else if (_targetPos.HasValue && _corners.Length > 0 && _currentPathIndex < _corners.Length)
        {
            Vector3 targetCorner = _corners[_currentPathIndex];
            
            Vector3 offset = new Vector3(targetCorner.x - _entity.transform.position.x, 0f, targetCorner.z - _entity.transform.position.z);
            
            float distToCorner = offset.magnitude;
            
            // 【修改 1】：收紧到达判定！必须老老实实走到拐角 0.2 米内才算到达。
            // 因为你加大了 Agent Radius，拐角点本身就在空地上，走到点上绝不会撞墙。
            float arrivalDist = 0.2f;

            if (distToCorner < arrivalDist)
            {
                _currentPathIndex++;
                return; 
            }

            moveDir = offset.normalized;

            _stuckTimer += Time.deltaTime;
            if (_stuckTimer >= 0.25f)
            {
                _stuckTimer = 0f;
                float movedDist = Distance2D(_entity.transform.position, _lastPos);

                if (movedDist < 0.05f)
                {
                    // 【修改 2】：卡死时的终极制裁！
                    // 绝对不能 _currentPathIndex++ (会直接撞长墙中间)
                    // 直接清空目标，迫使下一帧从当前位置发起【重新寻路】！
                    _targetPos = null; 
                }
                _lastPos = _entity.transform.position;
            }
        }
        else if (_targetPos.HasValue)
        {
            StopMove(); 
        }

        // --- 下方应用速度与动画的代码保持不变 ---
        float speed = 5f;
        if (_entity.CreaturePropertyManager != null)
        {
            speed = (float)_entity.CreaturePropertyManager.GetProperty(CreatureMainProperty.Speed);
            if (speed <= 0.01f) speed = 5f;
        }

        _entity.moveExecutor.SetInput(moveDir * speed * 0.1f);

        if (_entity.animator != null)
            _entity.animator.SetFloat("Speed", moveDir.magnitude);

        if (_entity.display != null && moveDir.sqrMagnitude > 0.001f)
        {
            Vector3 scale = _entity.display.localScale;
            scale.x = moveDir.x < -0.01f ? -Mathf.Abs(scale.x) : (moveDir.x > 0.01f ? Mathf.Abs(scale.x) : scale.x);
            _entity.display.localScale = scale;
        }
    }

    public void ShutDown() { StopMove(); }
    public void Resume() { }
}