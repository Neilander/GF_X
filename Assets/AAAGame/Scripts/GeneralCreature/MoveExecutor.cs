using AAAGame.MiniMap.FOG3;
using UnityEngine;

public class MoveExecutor : MonoBehaviour, IMoveExecutor
{
    private CharacterController _controller;
    private MAEntity _ownerEntity;
    private Fog3Manager _fog3Manager;

    private Vector3 _inputVelocity;
    private FixVector2 _fixedInputVelocity;
    private bool _hasFixedInputVelocity;
    private Vector3 _externalVelocity;
    private FixVector2 _fixedExternalVelocity;
    private Vector3 _overrideVelocity;
    private FixVector2 _fixedOverrideVelocity;
    private bool _hasFixedOverrideVelocity;
    private bool _hasOverride;
    private bool _isMovingThisFrame;

    private bool _navigationConstrained = true;
    private bool _constraintBypassForNextFrame;
    private bool _navigationConstraintBypass;
    private bool _navigationConstraintBypassUntilLegalPoint;
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
    private ulong _preparedFrame;
    private float _preparedDeltaTime;
    private Vector3 _preparedStartPosition;
    private FixVector2 _preparedLogicStartPosition;
    private Vector3 _preparedFinalVelocity;
    private Vector3 _preparedHorizontalDisplacement;
    private FixVector2 _preparedHorizontalDisplacementFixed;
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
    public bool HasPreparedLogicMove => _hasPreparedMove && _preparedFrame > 0;
    public ulong PreparedLogicFrame => _preparedFrame;
    public bool PreparedCollisionMovable => _controller != null && _controller.enabled;
    public FixVector2 PreparedResolvedHorizontalDisplacement => _preparedHorizontalDisplacementFixed;
    public FixVector2 DebugAuthoritativeLogicPosition { get; private set; }
    public FixVector2 DebugAuthoritativeHorizontalDisplacement { get; private set; }
    public bool DebugLastInputWasFixed { get; private set; }
    public FixVector2 DebugLastFixedInput { get; private set; }

    public void Init(CharacterController controller) => Init(controller, 0);

    public void Init(CharacterController controller, int agentTypeID)
    {
        _controller = controller;
        _ownerEntity = GetComponent<MAEntity>();
        _agentTypeID = agentTypeID;
        _constraintBypassForNextFrame = false;
        _navigationConstraintBypass = false;
        _navigationConstraintBypassUntilLegalPoint = false;
        _hasPreparedMove = false;
        _preparedFrame = 0;

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
        _fixedInputVelocity = FixVector2.Zero;
        _hasFixedInputVelocity = false;
        DebugLastInputWasFixed = false;
    }

    public void SetInputFixed(FixVector2 velocity)
    {
        _fixedInputVelocity = velocity;
        _hasFixedInputVelocity = true;
        _inputVelocity = new Vector3((float)velocity.x, 0f, (float)velocity.y);
        DebugLastInputWasFixed = true;
        DebugLastFixedInput = velocity;
    }

    public void AddExternal(Vector3 velocity)
    {
        AddExternalFixed(new FixVector2((Fix64)velocity.x, (Fix64)velocity.z));
    }

    public void AddExternalFixed(FixVector2 velocity)
    {
        _fixedExternalVelocity += velocity;
        _externalVelocity = new Vector3((float)_fixedExternalVelocity.x, 0f, (float)_fixedExternalVelocity.y);
    }

    public void SetOverride(Vector3 velocity)
    {
        _overrideVelocity = velocity;
        _hasOverride = true;
        _hasFixedOverrideVelocity = false;
    }

    public void SetOverrideFixed(FixVector2 velocity)
    {
        _fixedOverrideVelocity = velocity;
        _overrideVelocity = new Vector3((float)velocity.x, 0f, (float)velocity.y);
        _hasOverride = true;
        _hasFixedOverrideVelocity = true;
    }

    public void ClearOverride()
    {
        _hasOverride = false;
        _hasFixedOverrideVelocity = false;
    }

    public void SetExternal(Vector3 velocity)
    {
        SetExternalFixed(new FixVector2((Fix64)velocity.x, (Fix64)velocity.z));
    }

    public void SetExternalFixed(FixVector2 velocity)
    {
        _fixedExternalVelocity = velocity;
        _externalVelocity = new Vector3((float)velocity.x, 0f, (float)velocity.y);
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

    /// <summary>
    /// 建造/障碍替换导致角色暂时处于不可走区域时，允许角色先离开障碍。
    /// 角色回到合法导航点后自动恢复约束。
    /// </summary>
    public void EnableNavigationConstraintBypassUntilLegalPoint()
    {
        _navigationConstraintBypass = true;
        _navigationConstraintBypassUntilLegalPoint = true;

        Debug.Log(
            $"[MoveExecutor] Enable navigation escape. gameObject={gameObject.name} " +
            $"position={transform.position} agentTypeID={_agentTypeID}");
    }

    public void Execute()
    {
        throw new System.InvalidOperationException("MoveExecutor.Execute failed: deltaTime must be supplied by the logic frame.");
    }

    public void Execute(float deltaTime)
    {
        if (_hasPreparedMove)
            throw new System.InvalidOperationException("MoveExecutor.Execute failed: a prepared move is already pending.");

        PrepareInternal(deltaTime, 0, transform.position, true);
        CommitPreparedInternal(0, false, default, false);
    }

    public void PrepareLogicFrame(Fix64 deltaTime, LogicEntityFrameState frameState, bool allowMovement)
    {
        if (_ownerEntity == null)
            throw new System.InvalidOperationException("MoveExecutor.PrepareLogicFrame failed: owner entity is not bound.");
        if (deltaTime != LogicFrameRuntime.FixedDeltaTime)
            throw new System.InvalidOperationException("MoveExecutor.PrepareLogicFrame failed: deltaTime is not the fixed logic delta.");
        if (!LogicFrameRuntime.IsTicking || LogicFrameRuntime.CurrentFrame == 0)
            throw new System.InvalidOperationException("MoveExecutor.PrepareLogicFrame failed: no logic frame is running.");
        if (frameState.EntityId != _ownerEntity.LogicEntityId)
        {
            throw new System.InvalidOperationException(
                $"MoveExecutor.PrepareLogicFrame failed: snapshot identity mismatch. owner={_ownerEntity.LogicEntityId.Value}, snapshot={frameState.EntityId.Value}.");
        }
        if (_hasPreparedMove)
        {
            throw new System.InvalidOperationException(
                $"MoveExecutor.PrepareLogicFrame failed: frame {_preparedFrame} is still pending for entity {_ownerEntity.LogicEntityId.Value}.");
        }

        Vector3 frameStartPosition = transform.position;
        EnsureTransformMatchesFrameState(frameState, frameStartPosition);
        _preparedLogicStartPosition = frameState.Position;
        PrepareInternal((float)deltaTime, LogicFrameRuntime.CurrentFrame, frameStartPosition, allowMovement);
    }

    public void CommitPreparedLogicFrame(FixVector2 authoritativePosition)
    {
        if (!_hasPreparedMove || _preparedFrame == 0)
            throw new System.InvalidOperationException("MoveExecutor.CommitPreparedLogicFrame failed: no logic-frame move is prepared.");
        if (!LogicFrameRuntime.IsTicking || LogicFrameRuntime.CurrentFrame != _preparedFrame)
        {
            throw new System.InvalidOperationException(
                $"MoveExecutor.CommitPreparedLogicFrame failed: frame mismatch. logicFrame={LogicFrameRuntime.CurrentFrame}, prepared={_preparedFrame}.");
        }

        CommitPreparedInternal(_preparedFrame, true, authoritativePosition, true);
    }

    private void PrepareInternal(float deltaTime, ulong frameId, Vector3 frameStartPosition, bool allowMovement)
    {
        if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            throw new System.ArgumentOutOfRangeException(nameof(deltaTime), deltaTime, "Move deltaTime must be finite and positive.");

        _hasPreparedMove = true;
        _preparedFrame = frameId;
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
        FixVector2 fixedHorizontalVelocity;
        if (_hasOverride)
        {
            fixedHorizontalVelocity = _hasFixedOverrideVelocity
                ? _fixedOverrideVelocity
                : new FixVector2((Fix64)_overrideVelocity.x, (Fix64)_overrideVelocity.z);
        }
        else
        {
            FixVector2 fixedInput = _movementMode == MovementMode.Normal
                ? (_hasFixedInputVelocity
                    ? _fixedInputVelocity
                    : new FixVector2((Fix64)_inputVelocity.x, (Fix64)_inputVelocity.z))
                : FixVector2.Zero;
            fixedHorizontalVelocity = fixedInput + _fixedExternalVelocity;
        }
        Vector3 horizontalDisplacement;
        if (frameId > 0)
        {
            Fix64 fixedDelta = LogicFrameRuntime.FixedDeltaTime;
            _preparedHorizontalDisplacementFixed = fixedHorizontalVelocity * fixedDelta;
            horizontalDisplacement = new Vector3(
                (float)_preparedHorizontalDisplacementFixed.x,
                0f,
                (float)_preparedHorizontalDisplacementFixed.y);
        }
        else
        {
            horizontalDisplacement = horizontalVelocity * deltaTime;
            _preparedHorizontalDisplacementFixed = new FixVector2(
                (Fix64)horizontalDisplacement.x,
                (Fix64)horizontalDisplacement.z);
        }
        Vector3 requestedHorizontalDisplacement = horizontalDisplacement;

        UpdateNavigationConstraintBypassState(frameId, frameStartPosition);
        bool shouldConstrain = _navigationConstrained && !_constraintBypassForNextFrame && !_navigationConstraintBypass;
        DebugRequestedHorizontalDisplacement = requestedHorizontalDisplacement;
        DebugNavigationConstraintEnabled = shouldConstrain;
        DebugStaticCollisionShadowAvailable = false;
        DebugStaticCollisionShadowDisplacement = Vector3.zero;
        DebugStaticCollisionShadowFailure = LogicStaticCollisionFailure.None;
        Vector3 staticCollisionShadowStart = frameStartPosition;
        LogicStaticCollisionShadowResult staticCollisionShadow = default;
        bool hasStaticCollisionShadow = frameId == 0
                                        && shouldConstrain
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
        if (shouldConstrain && frameId == 0)
        {
            horizontalDisplacement = ConstrainHorizontalDisplacement(frameStartPosition, horizontalDisplacement);
            _preparedHorizontalDisplacementFixed = new FixVector2(
                (Fix64)horizontalDisplacement.x,
                (Fix64)horizontalDisplacement.z);
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

    private void CommitPreparedInternal(
        ulong expectedFrame,
        bool strictLogicFrame,
        FixVector2 authoritativePosition,
        bool useAuthoritativeHorizontalPosition)
    {
        if (!_hasPreparedMove || _preparedFrame != expectedFrame)
        {
            throw new System.InvalidOperationException(
                $"MoveExecutor.CommitPreparedInternal failed: expected frame {expectedFrame}, prepared frame {_preparedFrame}, hasPrepared={_hasPreparedMove}.");
        }
        if (strictLogicFrame)
            EnsureTransformMatchesPreparedStart(_preparedStartPosition, "CommitPreparedLogicFrame");

        if (useAuthoritativeHorizontalPosition)
        {
            FixVector2 authoritativeDisplacement = authoritativePosition - _preparedLogicStartPosition;
            _preparedHorizontalDisplacement = new Vector3(
                (float)authoritativeDisplacement.x,
                0f,
                (float)authoritativeDisplacement.y);
            _preparedFinalDisplacement = _preparedHorizontalDisplacement + _preparedVerticalDisplacement;
            DebugFinalDisplacement = _preparedFinalDisplacement;
            DebugAuthoritativeLogicPosition = authoritativePosition;
            DebugAuthoritativeHorizontalDisplacement = authoritativeDisplacement;
            _preparedHorizontalDisplacementFixed = authoritativeDisplacement;
        }

        Vector3 beforeMovePosition = transform.position;
        DebugLastControllerHitName = string.Empty;
        DebugLastControllerHitNormal = Vector3.zero;
        DebugLastControllerHitMoveDirection = Vector3.zero;

        if (_controller != null && _controller.enabled && _preparedVerticalDisplacement.sqrMagnitude > 0.000001f)
        {
            long controllerMoveStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            _controller.Move(useAuthoritativeHorizontalPosition
                ? _preparedVerticalDisplacement
                : _preparedFinalDisplacement);
            UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
                UnityGameFramework.Runtime.MainThreadPerfScope.MoveExecutorControllerMove,
                System.Diagnostics.Stopwatch.GetTimestamp() - controllerMoveStartTicks);
        }
        if (useAuthoritativeHorizontalPosition)
        {
            Vector3 verticalResolvedPosition = transform.position;
            transform.position = new Vector3(
                (float)authoritativePosition.x,
                verticalResolvedPosition.y,
                (float)authoritativePosition.y);
        }
        Vector3 afterMovePosition = transform.position;
        DebugActualHorizontalDisplacement = afterMovePosition - beforeMovePosition;
        DebugActualHorizontalDisplacement = new Vector3(DebugActualHorizontalDisplacement.x, 0f, DebugActualHorizontalDisplacement.z);
        if (_preparedHasStaticCollisionShadow && !useAuthoritativeHorizontalPosition)
        {
            LogicStaticCollisionShadowService.RecordComparison(
                strictLogicFrame ? expectedFrame : 0,
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
        _preparedHorizontalDisplacementFixed = FixVector2.Zero;
        _preparedVerticalDisplacement = Vector3.zero;
        _preparedFinalDisplacement = Vector3.zero;
        _preparedNavigationConstraintEnabled = false;
        _preparedHasStaticCollisionShadow = false;
        _preparedStaticCollisionShadow = default;
    }

    private void ClearPreparedMoveAndInputs()
    {
        _inputVelocity = Vector3.zero;
        _fixedInputVelocity = FixVector2.Zero;
        _hasFixedInputVelocity = false;
        _hasOverride = false;
        _hasFixedOverrideVelocity = false;
        _externalVelocity = Vector3.zero;
        _fixedExternalVelocity = FixVector2.Zero;
        _constraintBypassForNextFrame = false;
        _hasPreparedMove = false;
        _preparedFrame = 0;
        _preparedDeltaTime = 0f;
        _preparedLogicStartPosition = FixVector2.Zero;
    }

    private void EnsureTransformMatchesPreparedStart(Vector3 expected, string operation)
    {
        Vector3 actual = transform.position;
        const float tolerance = 0.0001f;
        if (Mathf.Abs(actual.x - expected.x) > tolerance || Mathf.Abs(actual.z - expected.z) > tolerance)
        {
            throw new System.InvalidOperationException(
                $"MoveExecutor.{operation} failed: transform changed outside commit. entity={_ownerEntity?.LogicEntityId.Value ?? 0}, " +
                $"frame={LogicFrameRuntime.CurrentFrame}, expectedXZ=({expected.x:F6},{expected.z:F6}), actualXZ=({actual.x:F6},{actual.z:F6}).");
        }
    }

    private void EnsureTransformMatchesFrameState(LogicEntityFrameState frameState, Vector3 actual)
    {
        Fix64 actualX = (Fix64)actual.x;
        Fix64 actualZ = (Fix64)actual.z;
        if (actualX != frameState.Position.x || actualZ != frameState.Position.y)
        {
            throw new System.InvalidOperationException(
                $"MoveExecutor.PrepareLogicFrame failed: transform changed after frame snapshot. entity={_ownerEntity?.LogicEntityId.Value ?? 0}, " +
                $"frame={LogicFrameRuntime.CurrentFrame}, snapshotRaw=({frameState.Position.x.RawValue},{frameState.Position.y.RawValue}), " +
                $"actualRaw=({actualX.RawValue},{actualZ.RawValue}), actualXZ=({actual.x:F6},{actual.z:F6}).");
        }
    }

    private void UpdateNavigationConstraintBypassState(ulong frameId, Vector3 frameStartPosition)
    {
        if (!_navigationConstraintBypassUntilLegalPoint)
            return;

        bool isLegal = frameId > 0
            ? FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPointFixed(
                _preparedLogicStartPosition,
                _agentTypeID,
                Fix64.Zero,
                Fix64.Zero,
                out _)
            : FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
                frameStartPosition,
                _agentTypeID,
                0f,
                0f,
                out _);
        if (!isLegal)
            return;

        _navigationConstraintBypassUntilLegalPoint = false;
        _navigationConstraintBypass = false;
        Debug.Log(
            $"[MoveExecutor] Navigation escape complete. gameObject={gameObject.name} " +
            $"position={frameStartPosition} logicPositionRaw=({_preparedLogicStartPosition.x.RawValue},{_preparedLogicStartPosition.y.RawValue}) " +
            $"agentTypeID={_agentTypeID} logicFrame={frameId}");
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
