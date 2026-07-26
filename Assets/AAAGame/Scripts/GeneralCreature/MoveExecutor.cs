using AAAGame.MiniMap.FOG3;
using UnityEngine;

public class MoveExecutor : MonoBehaviour
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
    private const int VerboseMoveLogIntervalFrames = 60;

    private MovementMode _movementMode = MovementMode.Normal;
    private int _lastVerboseMoveLogFrame = -100000;
    private int _lastConstraintFailureLogFrame = -100000;
    private int _lastHeroWallDiagnosticFrame = -100000;
    private Vector3 _previousHeroActualHorizontal;
    private Vector3 _previousHeroConstrainedHorizontal;

    private bool _hasPreparedMove;
    private float _preparedDeltaTime;
    private Vector3 _preparedStartPosition;
    private Vector3 _preparedFinalVelocity;
    private Vector3 _preparedHorizontalDisplacement;
    private Vector3 _preparedVerticalDisplacement;
    private Vector3 _preparedFinalDisplacement;
    private bool _preparedNavigationConstraintEnabled;
    private bool _preparedHasStaticCollisionShadow;
    private LogicStaticCollisionShadowResult _preparedStaticCollisionShadow;

    public Vector3 DebugInputVelocity => _inputVelocity;
    public Vector3 DebugExternalVelocity => _externalVelocity;
    public Vector3 DebugOverrideVelocity => _overrideVelocity;
    public bool DebugHasOverride => _hasOverride;
    public Vector3 DebugRequestedHorizontalDisplacement { get; private set; }
    public Vector3 DebugConstrainedHorizontalDisplacement { get; private set; }
    public Vector3 DebugActualHorizontalDisplacement { get; private set; }
    public Vector3 DebugVerticalDisplacement { get; private set; }
    public Vector3 DebugFinalDisplacement { get; private set; }
    public bool DebugNavigationConstraintEnabled { get; private set; }
    public string DebugLastControllerHitName { get; private set; }
    public Vector3 DebugLastControllerHitNormal { get; private set; }
    public Vector3 DebugLastControllerHitMoveDirection { get; private set; }
    public bool DebugStaticCollisionShadowAvailable { get; private set; }
    public Vector3 DebugStaticCollisionShadowDisplacement { get; private set; }
    public LogicStaticCollisionFailure DebugStaticCollisionShadowFailure { get; private set; }
    public MovementMode MovementMode => _movementMode;

    public void Init(CharacterController controller) => Init(controller, 0);

    public void Init(CharacterController controller, int agentTypeID)
    {
        _controller = controller;
        _ownerEntity = GetComponent<MAEntity>();
        _agentTypeID = agentTypeID;
        _constraintBypassForNextFrame = false;
        _navigationConstraintBypass = false;
        _hasPreparedMove = false;

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

    public void Execute(float deltaTime)
    {
        if (_hasPreparedMove)
            throw new System.InvalidOperationException("MoveExecutor.Execute failed: a prepared move is already pending.");

        PrepareInternal(deltaTime, transform.position, true);
        CommitPreparedInternal();
    }

    private void PrepareInternal(float deltaTime, Vector3 frameStartPosition, bool allowMovement)
    {
        if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            throw new System.ArgumentOutOfRangeException(nameof(deltaTime), deltaTime, "Move deltaTime must be finite and positive.");

        _hasPreparedMove = true;
        _preparedDeltaTime = deltaTime;
        _preparedStartPosition = frameStartPosition;

        if (!allowMovement || _controller == null || !_controller.enabled)
        {
            PrepareStationaryMove();
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

        if (ShouldLogVerboseMovement(finalVelocity))
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[Move] [{gameObject.name}] Execute mode={_movementMode} input={_inputVelocity} " +
                $"external={_externalVelocity} override={(_hasOverride ? _overrideVelocity.ToString() : "none")} " +
                $"finalVelocity={finalVelocity}");
        }

        Vector3 horizontalVelocity = new Vector3(finalVelocity.x, 0f, finalVelocity.z);
        Vector3 horizontalDisplacement = horizontalVelocity * deltaTime;
        Vector3 requestedHorizontalDisplacement = horizontalDisplacement;

        bool shouldConstrain = _navigationConstrained && !_constraintBypassForNextFrame && !_navigationConstraintBypass;
        DebugRequestedHorizontalDisplacement = requestedHorizontalDisplacement;
        DebugNavigationConstraintEnabled = shouldConstrain;
        DebugStaticCollisionShadowAvailable = false;
        DebugStaticCollisionShadowDisplacement = Vector3.zero;
        DebugStaticCollisionShadowFailure = LogicStaticCollisionFailure.None;
        Vector3 staticCollisionShadowStart = frameStartPosition;
        LogicStaticCollisionShadowResult staticCollisionShadow = default;
        bool hasStaticCollisionShadow = shouldConstrain
                                        && requestedHorizontalDisplacement.sqrMagnitude > 0.000001f
                                        && LogicStaticCollisionShadowService.TrySolve(
                                            _agentTypeID,
                                            staticCollisionShadowStart,
                                            requestedHorizontalDisplacement,
                                            _edgeBuffer,
                                            out staticCollisionShadow);
        if (hasStaticCollisionShadow)
        {
            DebugStaticCollisionShadowAvailable = true;
            DebugStaticCollisionShadowDisplacement = staticCollisionShadow.ResolvedDisplacement;
            DebugStaticCollisionShadowFailure = staticCollisionShadow.SolveResult.Failure;
        }
        if (shouldConstrain)
        {
            horizontalDisplacement = ConstrainHorizontalDisplacement(frameStartPosition, horizontalDisplacement);
            if (ShouldLogVerboseMovement(horizontalDisplacement))
            {
                GameDebugSettings.Log(DebugCategory.Move, $"[Move] [{gameObject.name}] Constrained horizontalDisplacement={horizontalDisplacement}");
            }
        }
        DebugConstrainedHorizontalDisplacement = horizontalDisplacement;

        UpdateGravity(deltaTime);
        float explicitVerticalSpeed = finalVelocity.y;
        Vector3 verticalDisplacement = Vector3.up * (_gravityVelocity + explicitVerticalSpeed) * deltaTime;
        Vector3 finalDisplacement = horizontalDisplacement + verticalDisplacement;
        DebugVerticalDisplacement = verticalDisplacement;
        DebugFinalDisplacement = finalDisplacement;

        _preparedFinalVelocity = finalVelocity;
        _preparedHorizontalDisplacement = horizontalDisplacement;
        _preparedVerticalDisplacement = verticalDisplacement;
        _preparedFinalDisplacement = finalDisplacement;
        _preparedNavigationConstraintEnabled = shouldConstrain;
        _preparedHasStaticCollisionShadow = hasStaticCollisionShadow;
        _preparedStaticCollisionShadow = staticCollisionShadow;
    }

    private void CommitPreparedInternal()
    {
        if (!_hasPreparedMove)
            throw new System.InvalidOperationException("MoveExecutor.CommitPreparedInternal failed: no prepared move.");

        Vector3 beforeMovePosition = transform.position;
        DebugLastControllerHitName = string.Empty;
        DebugLastControllerHitNormal = Vector3.zero;
        DebugLastControllerHitMoveDirection = Vector3.zero;

        if (_controller != null && _controller.enabled && _preparedVerticalDisplacement.sqrMagnitude > 0.000001f)
        {
            long controllerMoveStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            _controller.Move(_preparedFinalDisplacement);
            UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
                UnityGameFramework.Runtime.MainThreadPerfScope.MoveExecutorControllerMove,
                System.Diagnostics.Stopwatch.GetTimestamp() - controllerMoveStartTicks);
        }
        Vector3 afterMovePosition = transform.position;
        DebugActualHorizontalDisplacement = afterMovePosition - beforeMovePosition;
        DebugActualHorizontalDisplacement = new Vector3(DebugActualHorizontalDisplacement.x, 0f, DebugActualHorizontalDisplacement.z);
        if (_preparedHasStaticCollisionShadow)
        {
            LogicStaticCollisionShadowService.RecordComparison(
                0,
                _ownerEntity != null ? _ownerEntity.Id : 0,
                _agentTypeID,
                _preparedStartPosition,
                DebugRequestedHorizontalDisplacement,
                _preparedHorizontalDisplacement,
                DebugActualHorizontalDisplacement,
                _preparedStaticCollisionShadow);
        }
        LogHeroWallMovementDiagnostic(
            DebugRequestedHorizontalDisplacement,
            _preparedHorizontalDisplacement,
            beforeMovePosition,
            afterMovePosition,
            _preparedFinalVelocity,
            _preparedDeltaTime,
            _preparedNavigationConstraintEnabled);

        _isMovingThisFrame = _preparedHorizontalDisplacement.sqrMagnitude > 0.0001f;
        ClearPreparedMoveAndInputs();
    }

    private void PrepareStationaryMove()
    {
        DebugRequestedHorizontalDisplacement = Vector3.zero;
        DebugConstrainedHorizontalDisplacement = Vector3.zero;
        DebugActualHorizontalDisplacement = Vector3.zero;
        DebugVerticalDisplacement = Vector3.zero;
        DebugFinalDisplacement = Vector3.zero;
        DebugNavigationConstraintEnabled = false;
        DebugStaticCollisionShadowAvailable = false;
        DebugStaticCollisionShadowDisplacement = Vector3.zero;
        DebugStaticCollisionShadowFailure = LogicStaticCollisionFailure.None;
        _preparedFinalVelocity = Vector3.zero;
        _preparedHorizontalDisplacement = Vector3.zero;
        _preparedVerticalDisplacement = Vector3.zero;
        _preparedFinalDisplacement = Vector3.zero;
        _preparedNavigationConstraintEnabled = false;
        _preparedHasStaticCollisionShadow = false;
        _preparedStaticCollisionShadow = default;
    }

    private void ClearPreparedMoveAndInputs()
    {
        _inputVelocity = Vector3.zero;
        _hasOverride = false;
        _externalVelocity = Vector3.zero;
        _constraintBypassForNextFrame = false;
        _hasPreparedMove = false;
        _preparedDeltaTime = 0f;
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit == null || hit.collider == null)
            return;

        DebugLastControllerHitName = hit.collider.gameObject.name;
        DebugLastControllerHitNormal = hit.normal;
        DebugLastControllerHitMoveDirection = hit.moveDirection;
    }

    private bool ShouldLogVerboseMovement(Vector3 movementVector)
    {
        if (!GameDebugSettings.IsEnabled(DebugCategory.Move))
            return false;
        if (_ownerEntity != null && !GameDebugSettings.ShouldLogMovementForCharacter(_ownerEntity.CharacterKey))
            return false;
        if (movementVector.sqrMagnitude <= 0.0001f)
            return false;

        int frame = Time.frameCount;
        if (frame - _lastVerboseMoveLogFrame < VerboseMoveLogIntervalFrames)
            return false;

        _lastVerboseMoveLogFrame = frame;
        return true;
    }

    private void LogHeroWallMovementDiagnostic(
        Vector3 requestedHorizontalDisplacement,
        Vector3 constrainedHorizontalDisplacement,
        Vector3 beforeMovePosition,
        Vector3 afterMovePosition,
        Vector3 finalVelocity,
        float deltaTime,
        bool constraintEnabled)
    {
        if (_ownerEntity == null || !string.Equals(_ownerEntity.CharacterKey, UnitType.Unit_Hero.ToString(), System.StringComparison.Ordinal))
            return;

        Vector3 actualHorizontal = afterMovePosition - beforeMovePosition;
        actualHorizontal.y = 0f;
        if (requestedHorizontalDisplacement.sqrMagnitude <= 0.000001f)
            return;

        float requestedMagnitude = requestedHorizontalDisplacement.magnitude;
        float constrainedMagnitude = constrainedHorizontalDisplacement.magnitude;
        float actualMagnitude = actualHorizontal.magnitude;
        float constraintRatio = requestedMagnitude > 0.0001f ? constrainedMagnitude / requestedMagnitude : 1f;
        float controllerRatio = constrainedMagnitude > 0.0001f ? actualMagnitude / constrainedMagnitude : 1f;
        float constraintDot = requestedMagnitude > 0.0001f && constrainedMagnitude > 0.0001f
            ? Vector3.Dot(requestedHorizontalDisplacement / requestedMagnitude, constrainedHorizontalDisplacement / constrainedMagnitude)
            : 1f;
        float controllerDot = constrainedMagnitude > 0.0001f && actualMagnitude > 0.0001f
            ? Vector3.Dot(constrainedHorizontalDisplacement / constrainedMagnitude, actualHorizontal / actualMagnitude)
            : 1f;
        float previousActualDot = _previousHeroActualHorizontal.sqrMagnitude > 0.000001f && actualHorizontal.sqrMagnitude > 0.000001f
            ? Vector3.Dot(_previousHeroActualHorizontal.normalized, actualHorizontal.normalized)
            : 1f;
        float previousConstrainedDot = _previousHeroConstrainedHorizontal.sqrMagnitude > 0.000001f && constrainedHorizontalDisplacement.sqrMagnitude > 0.000001f
            ? Vector3.Dot(_previousHeroConstrainedHorizontal.normalized, constrainedHorizontalDisplacement.normalized)
            : 1f;

        bool suspiciousConstraint = constraintEnabled
                                    && (constraintRatio < 0.75f || constraintDot < 0.85f || previousConstrainedDot < 0.35f);
        bool suspiciousController = controllerRatio < 0.75f || controllerDot < 0.85f || previousActualDot < 0.35f;
        if (!suspiciousConstraint && !suspiciousController)
        {
            _previousHeroActualHorizontal = actualHorizontal;
            _previousHeroConstrainedHorizontal = constrainedHorizontalDisplacement;
            return;
        }

        int frame = Time.frameCount;
        if (frame - _lastHeroWallDiagnosticFrame < 12)
        {
            _previousHeroActualHorizontal = actualHorizontal;
            _previousHeroConstrainedHorizontal = constrainedHorizontalDisplacement;
            return;
        }

        _lastHeroWallDiagnosticFrame = frame;
        Debug.LogWarning(
            $"[HeroWallMoveDiag] frame={frame} mode={_movementMode} constraint={constraintEnabled} " +
            $"posBefore={beforeMovePosition} posAfter={afterMovePosition} edgeBuffer={_edgeBuffer:F3} agentType={_agentTypeID} dt={deltaTime:F4} " +
            $"velocity={finalVelocity} requested={requestedHorizontalDisplacement} constrained={constrainedHorizontalDisplacement} actual={actualHorizontal} " +
            $"constraintRatio={constraintRatio:F3} controllerRatio={controllerRatio:F3} constraintDot={constraintDot:F3} controllerDot={controllerDot:F3} " +
            $"prevConstrainedDot={previousConstrainedDot:F3} prevActualDot={previousActualDot:F3} " +
            $"input={_inputVelocity} external={_externalVelocity} override={(_hasOverride ? _overrideVelocity.ToString() : "none")}");

        _previousHeroActualHorizontal = actualHorizontal;
        _previousHeroConstrainedHorizontal = constrainedHorizontalDisplacement;
    }

    private void UpdateGravity(float deltaTime)
    {
        if (_controller.isGrounded && _gravityVelocity < 0f)
            _gravityVelocity = GroundStickVelocity;

        _gravityVelocity += GravityAcceleration * deltaTime;
    }

    private Vector3 ConstrainHorizontalDisplacement(Vector3 currentPos, Vector3 desiredHorizontalDisplacement)
    {
        if (desiredHorizontalDisplacement.sqrMagnitude <= 0.000001f)
            return Vector3.zero;

        Vector3 desiredPos = currentPos + desiredHorizontalDisplacement;
        long constraintStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!FlowFieldCrowdMovementSystem.TryConstrainNavigationDisplacement(
                currentPos,
                desiredHorizontalDisplacement,
                _agentTypeID,
                _edgeBuffer,
                out Vector3 constrainedDisplacement))
        {
            RecordConstraintPerf(constraintStartTicks);
            LogConstraintFailure("Flow导航位移约束失败", currentPos, desiredPos, desiredHorizontalDisplacement);
            return Vector3.zero;
        }
        RecordConstraintPerf(constraintStartTicks);

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

    private static void RecordConstraintPerf(long startTicks)
    {
        UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
            UnityGameFramework.Runtime.MainThreadPerfScope.MoveExecutorConstraint,
            System.Diagnostics.Stopwatch.GetTimestamp() - startTicks);
    }

    private void LogConstraintFailure(string reason, Vector3 currentPos, Vector3 desiredPos, Vector3 desiredHorizontalDisplacement)
    {
        int frame = Time.frameCount;
        bool shouldLog = GameDebugSettings.IsEnabled(DebugCategory.Move)
                         && frame - _lastConstraintFailureLogFrame >= VerboseMoveLogIntervalFrames;
        if (shouldLog)
        {
            _lastConstraintFailureLogFrame = frame;
            string ownerKey = _ownerEntity != null ? _ownerEntity.CharacterKey : "null";
            GameDebugSettings.Log(DebugCategory.Move,
                $"[MoveExecutor] {reason} gameObject={gameObject.name} owner={ownerKey} side={_ownerEntity?.Side.ToString() ?? "null"} " +
                $"agentType={_agentTypeID} mode={_movementMode} edgeBuffer={_edgeBuffer:F2} currentPos={currentPos} " +
                $"desiredPos={desiredPos} desiredHorizontalDisplacement={desiredHorizontalDisplacement}");
        }

        if (_ownerEntity != null && shouldLog)
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
