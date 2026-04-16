using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class MoveExecutor : MonoBehaviour, IMoveExecutor
{
    private CharacterController _controller;
    private MAEntity _ownerEntity;

    private Vector3 _inputVelocity;
    private Vector3 _externalVelocity;
    private Vector3 _overrideVelocity;
    private bool _hasOverride;

    private bool _navMeshConstrained = true;
    private bool _constraintBypassForNextFrame;

    private float _gravityVelocity;
    private float _edgeBuffer = 0.45f;
    private float _sampleRadius = 0.8f;
    private NavMeshQueryFilter _navFilter;
    private const float GravityAcceleration = -28f;
    private const float GroundStickVelocity = -2f;

    public void Init(CharacterController controller) => Init(controller, 0);

    public void Init(CharacterController controller, int agentTypeID)
    {
        _controller = controller;
        _ownerEntity = GetComponent<MAEntity>();
        _navFilter = new NavMeshQueryFilter
        {
            agentTypeID = agentTypeID,
            areaMask = NavMesh.AllAreas
        };

        if (_controller != null)
        {
            // 只有控制器存在时才输出debug日志
            Debug.Log($"[MoveExecutor] Init: controller={controller != null}, agentTypeID={agentTypeID}, gameObject={gameObject.name}");
            _edgeBuffer = Mathf.Max(0.2f, _controller.radius + 0.05f);
            _sampleRadius = Mathf.Max(0.5f, _controller.radius + 0.2f);
            Debug.Log($"[MoveExecutor] Controller settings: radius={_controller.radius}, height={_controller.height}, center={_controller.center}");

            // 只有控制器存在时才检查NavMesh状态
            CheckNavMeshStatus();
        }
        // 控制器为null时（如建筑）不输出debug日志
    }

    private void CheckNavMeshStatus()
    {
        Vector3 currentPos = transform.position;

        // 检查当前位置是否在NavMesh上
        bool isOnNavMesh = NavMesh.SamplePosition(currentPos, out NavMeshHit hit, 10f, _navFilter);

        Debug.Log($"[MoveExecutor] NavMesh检查:");
        Debug.Log($"  - 当前位置: {currentPos}");
        Debug.Log($"  - 是否在NavMesh上: {isOnNavMesh}");
        Debug.Log($"  - AgentTypeID: {_navFilter.agentTypeID}");
        Debug.Log($"  - AreaMask: {_navFilter.areaMask}");

        if (isOnNavMesh)
        {
            Debug.Log($"  - NavMesh位置: {hit.position}");
            Debug.Log($"  - NavMesh距离: {hit.distance}");
        }
        else
        {
            Debug.LogError($"[MoveExecutor] ⚠️ 当前位置不在NavMesh上！角色将无法移动！位置={currentPos}, gameObject={gameObject.name}");
        }

        // 检查NavMesh是否已烘焙
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        Debug.Log($"  - NavMesh三角形数量: {triangulation.indices.Length / 3}");

        if (triangulation.indices.Length == 0)
        {
            Debug.LogError($"[MoveExecutor] ⚠️ 场景中没有NavMesh数据！请先烘焙NavMesh！");
        }
    }

    // 每帧输入
    public void SetInput(Vector3 velocity)
    {
        _inputVelocity = velocity;
        if (velocity.sqrMagnitude > 0.001f)
        {
            //Debug.Log($"[MoveExecutor] SetInput: velocity={velocity}, gameObject={gameObject.name}");
        }
    }

    // 外力可叠加
    public void AddExternal(Vector3 velocity)
    {
        _externalVelocity += velocity;
    }

    // 强制覆盖（击飞等）
    public void SetOverride(Vector3 velocity)
    {
        _overrideVelocity = velocity;
        _hasOverride = true;
    }

    public void ClearOverride()
    {
        _hasOverride = false;
    }


    public void SetExternal(Vector3 velocity)
    {
        _externalVelocity = velocity;
    }

    public void SetNavMeshConstrained(bool constrained)
    {
        _navMeshConstrained = constrained;
    }

    public void SetConstraintBypassForNextFrame(bool bypass = true)
    {
        _constraintBypassForNextFrame = bypass;
    }

    public void Execute()
    {
        Execute(Time.deltaTime);
    }

    public void Execute(float deltaTime)
    {
        // 检查CharacterController是否活跃，避免在单位死亡后调用Move方法
        if (_controller == null || !_controller.enabled)
        {
            // 输入每帧重置（非常重要）
            _inputVelocity = Vector3.zero;
            _hasOverride = false;
            _externalVelocity = Vector3.zero;
            return;
        }

        Vector3 finalVelocity = _hasOverride
            ? _overrideVelocity
            : _inputVelocity + _externalVelocity;

        Vector3 horizontalVelocity = new Vector3(finalVelocity.x, 0f, finalVelocity.z);
        Vector3 horizontalDisplacement = horizontalVelocity * deltaTime;

        bool shouldConstrain = _navMeshConstrained && !_constraintBypassForNextFrame;
        if (shouldConstrain)
        {
            horizontalDisplacement = ConstrainHorizontalDisplacement(horizontalDisplacement);
        }

        UpdateGravity(deltaTime);
        float explicitVerticalSpeed = finalVelocity.y;
        Vector3 verticalDisplacement = Vector3.up * (_gravityVelocity + explicitVerticalSpeed) * deltaTime;

        Vector3 finalDisplacement = horizontalDisplacement + verticalDisplacement;

        // 调试：打印移动信息
        if (finalDisplacement.sqrMagnitude > 0.000001f)
        {
            //Debug.Log($"[MoveExecutor] Execute: finalDisplacement={finalDisplacement}, gameObject={gameObject.name}");
            _controller.Move(finalDisplacement);
        }

        // 输入每帧重置（非常重要）
        _inputVelocity = Vector3.zero;
        _hasOverride = false;
        _externalVelocity = Vector3.zero;
        _constraintBypassForNextFrame = false;
    }

    private void UpdateGravity(float deltaTime)
    {
        if (_controller.isGrounded && _gravityVelocity < 0f)
        {
            _gravityVelocity = GroundStickVelocity;
        }

        _gravityVelocity += GravityAcceleration * deltaTime;
    }

    private const float MaxOutOfBoundsDistance = 1.5f; // 允许超出 NavMesh 边缘的最大距离

    private Vector3 ConstrainHorizontalDisplacement(Vector3 desiredHorizontalDisplacement)
    {
        if (desiredHorizontalDisplacement.sqrMagnitude <= 0.000001f)
        {
            return Vector3.zero;
        }

        Vector3 currentPos = transform.position;
        Vector3 desiredPos = currentPos + desiredHorizontalDisplacement;
        Vector3 desiredNavProbePos = new Vector3(desiredPos.x, currentPos.y, desiredPos.z);

        // 检查目标位置是否在 NavMesh 上或附近
        if (!NavMesh.SamplePosition(desiredNavProbePos, out NavMeshHit navHit, _sampleRadius, _navFilter))
        {
            Debug.LogWarning($"[MoveExecutor] NavMesh.SamplePosition 失败！desiredPos={desiredNavProbePos}, gameObject={gameObject.name}");
            return Vector3.zero;
        }

        // 目标点离最近的 NavMesh 点太远，说明完全跑出去了
        Vector2 sampledXZ = new Vector2(navHit.position.x, navHit.position.z);
        Vector2 desiredXZ = new Vector2(desiredPos.x, desiredPos.z);
        float distFromNavMesh = Vector2.Distance(sampledXZ, desiredXZ);
        if (distFromNavMesh > MaxOutOfBoundsDistance)
        {
            Debug.LogWarning($"[MoveExecutor] 超出NavMesh边界！distFromNavMesh={distFromNavMesh}, gameObject={gameObject.name}");
            return Vector3.zero;
        }

        if (IsEnemyStrongholdBlocked(navHit.position))
        {
            Debug.LogWarning($"[MoveExecutor] 敌方据点被阻挡！pos={navHit.position}, gameObject={gameObject.name}");
            return Vector3.zero;
        }

        Vector3 constrainedPos = new Vector3(navHit.position.x, currentPos.y, navHit.position.z);
        return constrainedPos - currentPos;
    }

    private bool IsEnemyStrongholdBlocked(Vector3 worldPosition)
    {
        if (_ownerEntity == null)
        {
            _ownerEntity = GetComponent<MAEntity>();
        }

        if (_ownerEntity == null || _ownerEntity.Side != SideType.PlayerSide)
        {
            return false;
        }

        GamePhase phase = (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);
        if (phase == GamePhase.Invade)
        {
            return false;
        }

        Stronghold stronghold = LevelEntity.GetStrongholdAtWorldPosition(worldPosition);
        if (stronghold == null)
        {
            return false;
        }

        return stronghold.OwnerFactionId != EntitySideHelper.PlayerFactionId;
    }
}
