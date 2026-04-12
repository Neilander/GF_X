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
    private const float GravityAcceleration = -28f;
    private const float GroundStickVelocity = -2f;

    public void Init(CharacterController controller)
    {
        _controller = controller;
        _ownerEntity = GetComponent<MAEntity>();
        if (_controller != null)
        {
            _edgeBuffer = Mathf.Max(0.2f, _controller.radius + 0.05f);
            _sampleRadius = Mathf.Max(0.5f, _controller.radius + 0.2f);
        }
    }

    // 每帧输入
    public void SetInput(Vector3 velocity)
    {
        _inputVelocity = velocity;
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
        if (finalDisplacement.sqrMagnitude > 0.000001f)
        {
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

    private Vector3 ConstrainHorizontalDisplacement(Vector3 desiredHorizontalDisplacement)
    {
        if (desiredHorizontalDisplacement.sqrMagnitude <= 0.000001f)
        {
            return Vector3.zero;
        }

        Vector3 currentPos = transform.position;
        Vector3 desiredPos = currentPos + desiredHorizontalDisplacement;
        Vector3 desiredNavProbePos = new Vector3(desiredPos.x, currentPos.y, desiredPos.z);

        if (!NavMesh.SamplePosition(desiredNavProbePos, out NavMeshHit navHit, _sampleRadius, NavMesh.AllAreas))
        {
            return Vector3.zero;
        }

        Vector3 sampledPos = navHit.position;
        Vector2 sampledXZ = new Vector2(sampledPos.x, sampledPos.z);
        Vector2 desiredXZ = new Vector2(desiredPos.x, desiredPos.z);
        if (Vector2.Distance(sampledXZ, desiredXZ) > _sampleRadius + 0.05f)
        {
            return Vector3.zero;
        }

        if (NavMesh.FindClosestEdge(sampledPos, out NavMeshHit edgeHit, NavMesh.AllAreas) && edgeHit.distance < _edgeBuffer)
        {
            return Vector3.zero;
        }

        if (IsEnemyStrongholdBlocked(sampledPos))
        {
            return Vector3.zero;
        }

        Vector3 constrainedPos = new Vector3(sampledPos.x, currentPos.y, sampledPos.z);
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

        LevelEntity level = LevelEntity.ActiveLevelEntity;
        if (level == null)
        {
            return false;
        }

        Stronghold stronghold = level.GetStrongholdAtWorldPosition(worldPosition);
        if (stronghold == null)
        {
            return false;
        }

        return stronghold.OwnerFactionId != EntitySideHelper.PlayerFactionId;
    }
}
