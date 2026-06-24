using AAAGame.MiniMap.FOG3;
using UnityEngine;

public class MoveExecutor : MonoBehaviour, IMoveExecutor
{
    private CharacterController _controller;
    private MAEntity _ownerEntity;
    private Fog3Manager _fog3Manager;

    private Vector3 _inputVelocity;
    private Vector3 _externalVelocity;
    private Vector3 _overrideVelocity;
    private bool _hasOverride;
    private bool _isMovingThisFrame;

    private bool _navigationConstrained = true;
    private bool _constraintBypassForNextFrame;
    private bool _navigationConstraintBypass;
    private float _gravityVelocity;
    private float _edgeBuffer = 0.45f;
    private int _agentTypeID;

    private const float GravityAcceleration = -28f;
    private const float GroundStickVelocity = -2f;
    private const float ConstraintMinStepDistance = 0.02f;

    private MovementMode _movementMode = MovementMode.Normal;

    public Vector3 DebugInputVelocity => _inputVelocity;
    public Vector3 DebugExternalVelocity => _externalVelocity;
    public Vector3 DebugOverrideVelocity => _overrideVelocity;
    public bool DebugHasOverride => _hasOverride;
    public MovementMode MovementMode => _movementMode;

    public void Init(CharacterController controller) => Init(controller, 0);

    public void Init(CharacterController controller, int agentTypeID)
    {
        _controller = controller;
        _ownerEntity = GetComponent<MAEntity>();
        _agentTypeID = agentTypeID;

        if (_controller == null)
            return;

        _edgeBuffer = Mathf.Max(0.2f, _controller.radius + 0.05f);
        CheckNavigationStatus();
    }

    private void CheckNavigationStatus()
    {
        if (!_navigationConstrained)
            return;

        if (!FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(transform.position, _agentTypeID, 0.1f, 0f, out _))
        {
            Debug.LogWarning(
                $"[MoveExecutor] Flow navigation point unavailable at init. gameObject={gameObject.name}, " +
                $"position={transform.position}, agentTypeID={_agentTypeID}");
        }
    }

    public void SetInput(Vector3 velocity)
    {
        _inputVelocity = velocity;
    }

    public void AddExternal(Vector3 velocity)
    {
        _externalVelocity += velocity;
    }

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

    public void SetMovementMode(MovementMode mode)
    {
        _movementMode = mode;
    }

    public void SetNavigationConstrained(bool constrained)
    {
        _navigationConstrained = constrained;
        if (constrained)
            _navigationConstraintBypass = false;
    }

    public bool IsMoving()
    {
        return _isMovingThisFrame;
    }

    public void SetConstraintBypassForNextFrame(bool bypass = true)
    {
        _constraintBypassForNextFrame = bypass;
    }

    public void EnableNavigationConstraintBypass()
    {
        _navigationConstraintBypass = true;
    }

    public void Execute()
    {
        Execute(Time.deltaTime);
    }

    public void Execute(float deltaTime)
    {
        if (_controller == null || !_controller.enabled)
        {
            _inputVelocity = Vector3.zero;
            _hasOverride = false;
            _externalVelocity = Vector3.zero;
            return;
        }

        Vector3 finalVelocity;
        if (_hasOverride)
        {
            finalVelocity = _overrideVelocity;
        }
        else if (_movementMode == MovementMode.Normal)
        {
            finalVelocity = _inputVelocity + _externalVelocity;
        }
        else
        {
            finalVelocity = _externalVelocity;
        }

        if (GameDebugSettings.IsEnabled(DebugCategory.Move)
            && (_ownerEntity == null || GameDebugSettings.ShouldLogMovementForCharacter(_ownerEntity.CharacterKey)))
        {
            Debug.Log($"[Move] [{gameObject.name}] Execute mode={_movementMode} input={_inputVelocity} " +
                      $"external={_externalVelocity} override={(_hasOverride ? _overrideVelocity.ToString() : "none")} " +
                      $"finalVelocity={finalVelocity}");
        }

        Vector3 horizontalVelocity = new Vector3(finalVelocity.x, 0f, finalVelocity.z);
        Vector3 horizontalDisplacement = horizontalVelocity * deltaTime;

        bool shouldConstrain = _navigationConstrained && !_constraintBypassForNextFrame && !_navigationConstraintBypass;
        if (shouldConstrain)
        {
            horizontalDisplacement = ConstrainHorizontalDisplacement(horizontalDisplacement);
            if (GameDebugSettings.IsEnabled(DebugCategory.Move)
                && (_ownerEntity == null || GameDebugSettings.ShouldLogMovementForCharacter(_ownerEntity.CharacterKey)))
            {
                Debug.Log($"[Move] [{gameObject.name}] Constrained horizontalDisplacement={horizontalDisplacement}");
            }
        }

        UpdateGravity(deltaTime);
        float explicitVerticalSpeed = finalVelocity.y;
        Vector3 verticalDisplacement = Vector3.up * (_gravityVelocity + explicitVerticalSpeed) * deltaTime;
        Vector3 finalDisplacement = horizontalDisplacement + verticalDisplacement;

        if (finalDisplacement.sqrMagnitude > 0.000001f)
            _controller.Move(finalDisplacement);

        _isMovingThisFrame = horizontalDisplacement.sqrMagnitude > 0.0001f;
        _inputVelocity = Vector3.zero;
        _hasOverride = false;
        _externalVelocity = Vector3.zero;
        _constraintBypassForNextFrame = false;
    }

    private void UpdateGravity(float deltaTime)
    {
        if (_controller.isGrounded && _gravityVelocity < 0f)
            _gravityVelocity = GroundStickVelocity;

        _gravityVelocity += GravityAcceleration * deltaTime;
    }

    private Vector3 ConstrainHorizontalDisplacement(Vector3 desiredHorizontalDisplacement)
    {
        if (desiredHorizontalDisplacement.sqrMagnitude <= 0.000001f)
            return Vector3.zero;

        Vector3 currentPos = transform.position;
        Vector3 desiredPos = currentPos + desiredHorizontalDisplacement;
        if (!FlowFieldCrowdMovementSystem.TryConstrainNavigationDisplacement(
                currentPos,
                desiredHorizontalDisplacement,
                _agentTypeID,
                out Vector3 constrainedDisplacement))
        {
            LogConstraintFailure("Flow导航位移约束失败", currentPos, desiredPos, desiredHorizontalDisplacement);
            return Vector3.zero;
        }

        Vector3 constrainedPos = currentPos + constrainedDisplacement;
        if (IsEnemyStrongholdBlocked(constrainedPos))
        {
            LogConstraintFailure("敌方据点被阻挡", currentPos, desiredPos, desiredHorizontalDisplacement);
            return Vector3.zero;
        }

        if (IsInvadeTutorialStrongholdBlocked(constrainedPos))
        {
            LogConstraintFailure("Invade tutorial stronghold boundary blocked", currentPos, desiredPos, desiredHorizontalDisplacement);
            return Vector3.zero;
        }

        if (IsNonVisibleBlocked(constrainedPos))
        {
            LogConstraintFailure("非可见区域被阻挡", currentPos, desiredPos, desiredHorizontalDisplacement);
            return Vector3.zero;
        }

        if (constrainedDisplacement.sqrMagnitude <= ConstraintMinStepDistance * ConstraintMinStepDistance
            && desiredHorizontalDisplacement.sqrMagnitude > ConstraintMinStepDistance * ConstraintMinStepDistance)
        {
            LogConstraintFailure("Flow导航投影回原地", currentPos, desiredPos, desiredHorizontalDisplacement);
            return Vector3.zero;
        }

        return constrainedDisplacement;
    }

    private void LogConstraintFailure(string reason, Vector3 currentPos, Vector3 desiredPos, Vector3 desiredHorizontalDisplacement)
    {
        string ownerKey = _ownerEntity != null ? _ownerEntity.CharacterKey : "null";
        Debug.LogWarning(
            $"[MoveExecutor] {reason} gameObject={gameObject.name} owner={ownerKey} side={_ownerEntity?.Side.ToString() ?? "null"} " +
            $"agentType={_agentTypeID} mode={_movementMode} edgeBuffer={_edgeBuffer:F2} currentPos={currentPos} " +
            $"desiredPos={desiredPos} desiredHorizontalDisplacement={desiredHorizontalDisplacement}");

        if (_ownerEntity != null)
        {
            FlowFieldCrowdMovementSystem.LogConstraintFailureDiagnostic(
                _ownerEntity,
                currentPos,
                desiredHorizontalDisplacement,
                _inputVelocity,
                reason);
        }
    }

    private bool IsNonVisibleBlocked(Vector3 worldPosition)
    {
        if (_ownerEntity == null)
            _ownerEntity = GetComponent<MAEntity>();

        if (_ownerEntity == null || _ownerEntity.Side != SideType.PlayerSide)
            return false;

        _fog3Manager ??= Fog3Manager.Instance;
        if (_fog3Manager == null || !_fog3Manager.IsInitialized || _fog3Manager.MapData == null)
            return false;

        if (!_fog3Manager.MapData.WorldToGrid(worldPosition, out int gridX, out int gridY))
            return true;

        return _fog3Manager.MapData.GetCellState(gridX, gridY) != Fog3CellState.Visible;
    }

    private bool IsEnemyStrongholdBlocked(Vector3 worldPosition)
    {
        if (_ownerEntity == null)
            _ownerEntity = GetComponent<MAEntity>();

        if (_ownerEntity == null || _ownerEntity.Side != SideType.PlayerSide)
            return false;

        GamePhase phase = (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);
        if (phase == GamePhase.Invade)
            return false;

        Stronghold stronghold = LevelEntity.GetStrongholdAtWorldPosition(worldPosition);
        if (stronghold == null)
            return false;

        return stronghold.OwnerFactionId != EntitySideHelper.PlayerFactionId;
    }

    private bool IsInvadeTutorialStrongholdBlocked(Vector3 worldPosition)
    {
        if (_ownerEntity == null)
            _ownerEntity = GetComponent<MAEntity>();

        if (_ownerEntity == null || _ownerEntity.Side != SideType.PlayerSide)
            return false;

        return TutorialManager.IsInvadeTutorialMovementBlocked(worldPosition);
    }
}
