using AAAGame.MiniMap.FOG3;
using UnityEngine;
using UnityEngine.AI;

public class MoveExecutor : MonoBehaviour, IMoveExecutor
{
    private CharacterController _controller;
    private MAEntity _ownerEntity;
    private Fog3Manager _fog3Manager;

    private Vector3 _inputVelocity;
    private Vector3 _externalVelocity;
    private Vector3 _overrideVelocity;
    private bool _hasOverride;
    
    // 记录当前帧是否有移动输入（用于音效检测）
    private bool _isMovingThisFrame;

    private bool _navMeshConstrained = true;
    private bool _constraintBypassForNextFrame;
    // 持续 bypass，直到玩家走回 NavMesh 上自动解除
    private bool _bypassUntilOnNavMesh;
    // 检测"已回到 NavMesh"的容差
    private const float OnNavMeshSampleRadius = 0.2f;
    private float _gravityVelocity;
    private float _edgeBuffer = 0.45f;
    private float _sampleRadius = 0.8f;
    private NavMeshQueryFilter _navFilter;
    private const float GravityAcceleration = -28f;
    private const float GroundStickVelocity = -2f;
    private MovementMode _movementMode = MovementMode.Normal;
    private const float ConstraintSlideInset = 0.02f;
    private string _lastConstraintTrace;

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
        _navFilter = new NavMeshQueryFilter
        {
            agentTypeID = agentTypeID,
            areaMask = NavMesh.AllAreas
        };

        if (_controller != null)
        {
            _edgeBuffer = Mathf.Max(0.2f, _controller.radius + 0.05f);
            _sampleRadius = Mathf.Max(0.5f, _controller.radius + 0.2f);
            CheckNavMeshStatus();
        }
    }

    private void CheckNavMeshStatus()
    {
        Vector3 currentPos = transform.position;

        // 检查当前位置是否在NavMesh上
        bool isOnNavMesh = NavMesh.SamplePosition(currentPos, out NavMeshHit hit, 10f, _navFilter);

        if (isOnNavMesh)
        {
            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            if (triangulation.indices.Length == 0)
            {
                Debug.LogError("[MoveExecutor] 场景中没有NavMesh数据，请先烘焙NavMesh。");
            }
        }
        else
        {
            Debug.LogError($"[MoveExecutor] 当前位置不在NavMesh上，角色将无法移动。位置={currentPos}, gameObject={gameObject.name}, agentTypeID={_navFilter.agentTypeID}");
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

    public void SetMovementMode(MovementMode mode)
    {
        _movementMode = mode;
    }

    public void SetNavMeshConstrained(bool constrained)
    {
        _navMeshConstrained = constrained;
    }

    /// <summary>
    /// 检测单位是否正在移动
    /// </summary>
    /// <returns>true表示正在移动</returns>
    public bool IsMoving()
    {
        // 使用记录的移动状态（在 Execute 中更新）
        return _isMovingThisFrame;
    }

    public void SetConstraintBypassForNextFrame(bool bypass = true)
    {
        _constraintBypassForNextFrame = bypass;
    }

    public void EnableBypassUntilOnNavMesh()
    {
        if (_bypassUntilOnNavMesh) return;
        _bypassUntilOnNavMesh = true;
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

        // 持续 bypass：检测玩家是否已经回到 NavMesh，是则关闭 bypass
        if (_bypassUntilOnNavMesh
            && NavMesh.SamplePosition(transform.position, out _, OnNavMeshSampleRadius, _navFilter))
        {
            _bypassUntilOnNavMesh = false;
        }

        bool shouldConstrain = _navMeshConstrained && !_constraintBypassForNextFrame && !_bypassUntilOnNavMesh;
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
        {
            _controller.Move(finalDisplacement);
        }

        // 记录当前帧是否有移动输入（用于音效检测）
        _isMovingThisFrame = (horizontalDisplacement.sqrMagnitude > 0.0001f);
        
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
    private const int ConstraintBinarySearchSteps = 6;
    private const float ConstraintMinStepDistance = 0.02f;

    private Vector3 ConstrainHorizontalDisplacement(Vector3 desiredHorizontalDisplacement)
    {
        if (desiredHorizontalDisplacement.sqrMagnitude <= 0.000001f)
        {
            return Vector3.zero;
        }

        Vector3 currentPos = transform.position;
        if (TryResolveConstrainedHorizontalDisplacement(currentPos, desiredHorizontalDisplacement, out Vector3 constrainedDisplacement))
            return constrainedDisplacement;

        return Vector3.zero;
    }

    private bool TryResolveConstrainedHorizontalDisplacement(Vector3 currentPos, Vector3 desiredHorizontalDisplacement, out Vector3 constrainedDisplacement)
    {
        if (TryProjectAllowedDisplacement(currentPos, desiredHorizontalDisplacement, out constrainedDisplacement, out _, out _, out _, logFailure: true))
            return true;

        float fullDistance = desiredHorizontalDisplacement.magnitude;
        if (fullDistance <= ConstraintMinStepDistance)
            return false;

        Vector3 direction = desiredHorizontalDisplacement / fullDistance;
        float low = 0f;
        float high = fullDistance;
        Vector3 bestDisplacement = Vector3.zero;
        bool found = false;

        for (int i = 0; i < ConstraintBinarySearchSteps; i++)
        {
            float mid = (low + high) * 0.5f;
            if (mid <= ConstraintMinStepDistance)
                break;

            Vector3 candidateDisplacement = direction * mid;
            bool projected = TryProjectAllowedDisplacement(currentPos, candidateDisplacement, out Vector3 projectedDisplacement, out string failureReason, out NavMeshHit navHit, out float distFromNavMesh, logFailure: false);
            if (GameDebugSettings.IsEnabled(DebugCategory.Move)
                && (_ownerEntity == null || GameDebugSettings.ShouldLogMovementForCharacter(_ownerEntity.CharacterKey)))
            {
                Debug.Log(
                    $"[MoveExecutor] BinarySearch gameObject={gameObject.name} owner={_ownerEntity?.CharacterKey ?? "null"} step={i} low={low:F3} high={high:F3} mid={mid:F3} " +
                    $"candidate={candidateDisplacement} projected={projected} projectedDisp={projectedDisplacement} reason={failureReason ?? "none"} " +
                    $"navHitPos={(projected ? navHit.position.ToString() : "none")} distFromNavMesh={distFromNavMesh:F3}");
            }

            if (projected)
            {
                found = true;
                bestDisplacement = projectedDisplacement;
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        if (found)
        {
            constrainedDisplacement = bestDisplacement;
            return true;
        }

        constrainedDisplacement = Vector3.zero;
        return false;
    }

    private bool TryProjectAllowedDisplacement(
        Vector3 currentPos,
        Vector3 desiredHorizontalDisplacement,
        out Vector3 constrainedDisplacement,
        out string failureReason,
        out NavMeshHit navHit,
        out float distFromNavMesh,
        bool logFailure)
    {
        constrainedDisplacement = Vector3.zero;
        failureReason = null;
        navHit = default;
        distFromNavMesh = -1f;
        _lastConstraintTrace = null;

        if (!NavMesh.SamplePosition(currentPos, out NavMeshHit currentNavHit, _sampleRadius, _navFilter))
        {
            failureReason = "当前位置不在NavMesh上";
            if (logFailure)
                LogConstraintFailure(failureReason, currentPos, currentPos, desiredHorizontalDisplacement, false, default, -1f);
            return false;
        }

        Vector3 desiredPos = currentPos + desiredHorizontalDisplacement;
        Vector3 desiredNavProbePos = new Vector3(
            currentNavHit.position.x + desiredHorizontalDisplacement.x,
            currentNavHit.position.y,
            currentNavHit.position.z + desiredHorizontalDisplacement.z);

        if (!TryResolveNavConstrainedPosition(
                currentNavHit.position,
                desiredNavProbePos,
                desiredHorizontalDisplacement,
                out Vector3 constrainedNavPos,
                out navHit,
                out failureReason,
                out distFromNavMesh))
        {
            if (logFailure)
                LogConstraintFailure(failureReason, currentPos, desiredPos, desiredHorizontalDisplacement, false, navHit, distFromNavMesh);
            return false;
        }

        if (IsEnemyStrongholdBlocked(constrainedNavPos))
        {
            failureReason = "敌方据点被阻挡";
            if (logFailure)
                LogConstraintFailure(failureReason, currentPos, desiredPos, desiredHorizontalDisplacement, true, navHit, distFromNavMesh);
            return false;
        }

        if (IsInvadeTutorialStrongholdBlocked(constrainedNavPos))
        {
            failureReason = "Invade tutorial stronghold boundary blocked";
            if (logFailure)
                LogConstraintFailure(failureReason, currentPos, desiredPos, desiredHorizontalDisplacement, true, navHit, distFromNavMesh);
            return false;
        }

        if (IsNonVisibleBlocked(constrainedNavPos))
        {
            failureReason = "非可见区域被阻挡";
            if (logFailure)
                LogConstraintFailure(failureReason, currentPos, desiredPos, desiredHorizontalDisplacement, true, navHit, distFromNavMesh);
            return false;
        }

        Vector3 constrainedPos = new Vector3(constrainedNavPos.x, currentPos.y, constrainedNavPos.z);
        constrainedDisplacement = constrainedPos - currentPos;
        if (constrainedDisplacement.sqrMagnitude <= ConstraintMinStepDistance * ConstraintMinStepDistance
            && desiredHorizontalDisplacement.sqrMagnitude > ConstraintMinStepDistance * ConstraintMinStepDistance)
        {
            failureReason = "投影回原地";
            _lastConstraintTrace = BuildResolvedConstraintTrace(currentPos, currentNavHit.position, desiredPos, desiredHorizontalDisplacement, constrainedNavPos, navHit);
            if (logFailure)
                LogConstraintFailure(failureReason, currentPos, desiredPos, desiredHorizontalDisplacement, true, navHit, distFromNavMesh);
            constrainedDisplacement = Vector3.zero;
            return false;
        }

        return true;
    }

    private void LogConstraintFailure(
        string reason,
        Vector3 currentPos,
        Vector3 desiredPos,
        Vector3 desiredHorizontalDisplacement,
        bool desiredNavHit,
        NavMeshHit desiredNavHitInfo,
        float distFromNavMesh)
    {
        Debug.LogWarning(BuildConstraintFailureDiagnostics(reason, currentPos, desiredPos, desiredHorizontalDisplacement, desiredNavHit, desiredNavHitInfo, distFromNavMesh));
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

    private bool TryResolveNavConstrainedPosition(
        Vector3 currentNavPos,
        Vector3 desiredNavPos,
        Vector3 desiredHorizontalDisplacement,
        out Vector3 constrainedNavPos,
        out NavMeshHit navHit,
        out string failureReason,
        out float distFromNavMesh)
    {
        constrainedNavPos = currentNavPos;
        navHit = default;
        failureReason = null;
        distFromNavMesh = -1f;

        if (!NavMesh.Raycast(currentNavPos, desiredNavPos, out NavMeshHit rayHit, _navFilter))
        {
            if (!NavMesh.SamplePosition(desiredNavPos, out navHit, _sampleRadius, _navFilter))
            {
                failureReason = "NavMesh.SamplePosition 失败";
                return false;
            }

            Vector2 sampledXZ = new Vector2(navHit.position.x, navHit.position.z);
            Vector2 desiredXZ = new Vector2(desiredNavPos.x, desiredNavPos.z);
            distFromNavMesh = Vector2.Distance(sampledXZ, desiredXZ);
            if (distFromNavMesh > MaxOutOfBoundsDistance)
            {
                failureReason = "超出NavMesh边界";
                return false;
            }

            constrainedNavPos = navHit.position;
            return true;
        }

        float desiredDistance = desiredHorizontalDisplacement.magnitude;
        if (desiredDistance <= ConstraintMinStepDistance)
        {
            failureReason = "NavMesh位移距离不足";
            navHit = rayHit;
            distFromNavMesh = Vector3.Distance(rayHit.position, desiredNavPos);
            return false;
        }

        Vector3 desiredDirection = desiredHorizontalDisplacement / desiredDistance;
        Vector3 toHit = rayHit.position - currentNavPos;
        toHit.y = 0f;
        float forwardDistance = Vector3.Dot(toHit, desiredDirection);

        Vector3 inwardNormal = rayHit.normal;
        inwardNormal.y = 0f;
        if (inwardNormal.sqrMagnitude <= 0.0001f)
        {
            failureReason = "NavMesh边界法线无效";
            _lastConstraintTrace = BuildRayHitConstraintTrace(currentNavPos, desiredNavPos, desiredHorizontalDisplacement, rayHit, desiredDirection, forwardDistance, Vector3.zero, 0f, Vector3.zero, Vector3.zero);
            navHit = rayHit;
            distFromNavMesh = Vector3.Distance(rayHit.position, desiredNavPos);
            return false;
        }

        inwardNormal.Normalize();
        Vector3 tangent = Vector3.ProjectOnPlane(desiredHorizontalDisplacement, inwardNormal);
        tangent.y = 0f;
        if (tangent.sqrMagnitude <= 0.0001f)
        {
            if (forwardDistance > ConstraintSlideInset)
            {
                constrainedNavPos = currentNavPos + desiredDirection * (forwardDistance - ConstraintSlideInset);
                _lastConstraintTrace = BuildRayHitConstraintTrace(currentNavPos, desiredNavPos, desiredHorizontalDisplacement, rayHit, desiredDirection, forwardDistance, tangent, 0f, Vector3.zero, Vector3.zero);
                navHit = rayHit;
                distFromNavMesh = Vector3.Distance(rayHit.position, desiredNavPos);
                return true;
            }

            failureReason = "NavMesh射线命中边界";
            _lastConstraintTrace = BuildRayHitConstraintTrace(currentNavPos, desiredNavPos, desiredHorizontalDisplacement, rayHit, desiredDirection, forwardDistance, tangent, 0f, Vector3.zero, Vector3.zero);
            navHit = rayHit;
            distFromNavMesh = Vector3.Distance(rayHit.position, desiredNavPos);
            return false;
        }

        tangent.Normalize();
        float approachDistance = Mathf.Max(0f, forwardDistance - ConstraintSlideInset);
        Vector3 approachNavPos = currentNavPos + desiredDirection * approachDistance;
        float tangentDistance = desiredDistance - approachDistance;
        if (tangentDistance <= ConstraintMinStepDistance)
        {
            if (approachDistance > ConstraintMinStepDistance)
            {
                constrainedNavPos = approachNavPos;
                _lastConstraintTrace = BuildRayHitConstraintTrace(currentNavPos, desiredNavPos, desiredHorizontalDisplacement, rayHit, desiredDirection, forwardDistance, tangent, tangentDistance, Vector3.zero, Vector3.zero);
                navHit = rayHit;
                distFromNavMesh = Vector3.Distance(rayHit.position, desiredNavPos);
                return true;
            }

            failureReason = "NavMesh滑移距离不足";
            _lastConstraintTrace = BuildRayHitConstraintTrace(currentNavPos, desiredNavPos, desiredHorizontalDisplacement, rayHit, desiredDirection, forwardDistance, tangent, tangentDistance, Vector3.zero, Vector3.zero);
            navHit = rayHit;
            distFromNavMesh = Vector3.Distance(rayHit.position, desiredNavPos);
            return false;
        }

        Vector3 slideStart = approachNavPos + inwardNormal * ConstraintSlideInset;
        Vector3 slideTarget = slideStart + tangent * tangentDistance;
        if (NavMesh.Raycast(slideStart, slideTarget, out NavMeshHit slideHit, _navFilter))
        {
            Vector3 slideReach = slideHit.position - slideStart;
            slideReach.y = 0f;
            float slideReachDistance = slideReach.magnitude - ConstraintSlideInset;
            if (slideReachDistance <= ConstraintMinStepDistance)
            {
                if (approachDistance > ConstraintMinStepDistance)
                {
                    constrainedNavPos = approachNavPos;
                    _lastConstraintTrace = BuildRayHitConstraintTrace(currentNavPos, desiredNavPos, desiredHorizontalDisplacement, rayHit, desiredDirection, forwardDistance, tangent, tangentDistance, slideStart, slideTarget);
                    navHit = slideHit;
                    distFromNavMesh = Vector3.Distance(slideHit.position, desiredNavPos);
                    return true;
                }

                failureReason = "NavMesh滑移被阻挡";
                _lastConstraintTrace = BuildRayHitConstraintTrace(currentNavPos, desiredNavPos, desiredHorizontalDisplacement, rayHit, desiredDirection, forwardDistance, tangent, tangentDistance, slideStart, slideTarget);
                navHit = slideHit;
                distFromNavMesh = Vector3.Distance(slideHit.position, desiredNavPos);
                return false;
            }

            constrainedNavPos = slideStart + tangent * slideReachDistance;
            navHit = slideHit;
            distFromNavMesh = Vector3.Distance(slideHit.position, desiredNavPos);
            return true;
        }

        if (!NavMesh.SamplePosition(slideTarget, out navHit, _sampleRadius, _navFilter))
        {
            failureReason = "NavMesh滑移终点采样失败";
            _lastConstraintTrace = BuildRayHitConstraintTrace(currentNavPos, desiredNavPos, desiredHorizontalDisplacement, rayHit, desiredDirection, forwardDistance, tangent, tangentDistance, slideStart, slideTarget);
            return false;
        }

        constrainedNavPos = navHit.position;
        _lastConstraintTrace = BuildRayHitConstraintTrace(currentNavPos, desiredNavPos, desiredHorizontalDisplacement, rayHit, desiredDirection, forwardDistance, tangent, tangentDistance, slideStart, slideTarget);
        distFromNavMesh = Vector3.Distance(navHit.position, desiredNavPos);
        return true;
    }

    private string BuildRayHitConstraintTrace(
        Vector3 currentNavPos,
        Vector3 desiredNavPos,
        Vector3 desiredHorizontalDisplacement,
        NavMeshHit rayHit,
        Vector3 desiredDirection,
        float forwardDistance,
        Vector3 tangent,
        float tangentDistance,
        Vector3 slideStart,
        Vector3 slideTarget)
    {
        Vector3 normal = rayHit.normal;
        normal.y = 0f;
        Vector3 normalDir = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.zero;
        Vector3 plusStart = currentNavPos + normalDir * ConstraintSlideInset;
        Vector3 minusStart = currentNavPos - normalDir * ConstraintSlideInset;
        bool plusSample = NavMesh.SamplePosition(plusStart, out NavMeshHit plusHit, _sampleRadius, _navFilter);
        bool minusSample = NavMesh.SamplePosition(minusStart, out NavMeshHit minusHit, _sampleRadius, _navFilter);
        bool hasSlide = slideStart != Vector3.zero || slideTarget != Vector3.zero;
        NavMeshHit slideSampleHit = default;
        NavMeshHit slideRayHit = default;
        bool slideSample = hasSlide && NavMesh.SamplePosition(slideTarget, out slideSampleHit, _sampleRadius, _navFilter);
        bool slideRay = hasSlide && NavMesh.Raycast(slideStart, slideTarget, out slideRayHit, _navFilter);
        float intoBoundary = Vector3.Dot(desiredHorizontalDisplacement, -normalDir);

        return $"trace currentNav={currentNavPos} desiredNav={desiredNavPos} rayHit={rayHit.position} rayNormal={rayHit.normal} " +
               $"desiredDir={desiredDirection} forwardDistance={forwardDistance:F4} tangent={tangent} tangentDistance={tangentDistance:F4} intoBoundary={intoBoundary:F4} " +
               $"slideStart={slideStart} slideTarget={slideTarget} slideRay={slideRay} slideRayPos={(slideRay ? slideRayHit.position.ToString() : "none")} slideRayNormal={(slideRay ? slideRayHit.normal.ToString() : "none")} " +
               $"slideSample={slideSample} slideSamplePos={(slideSample ? slideSampleHit.position.ToString() : "none")} " +
               $"plusStart={plusStart} plusSample={plusSample} plusHit={(plusSample ? plusHit.position.ToString() : "none")} " +
               $"minusStart={minusStart} minusSample={minusSample} minusHit={(minusSample ? minusHit.position.ToString() : "none")}";
    }

    private string BuildResolvedConstraintTrace(
        Vector3 currentPos,
        Vector3 currentNavPos,
        Vector3 desiredPos,
        Vector3 desiredHorizontalDisplacement,
        Vector3 constrainedNavPos,
        NavMeshHit navHit)
    {
        Vector3 constrainedPos = new Vector3(constrainedNavPos.x, currentPos.y, constrainedNavPos.z);
        Vector3 constrainedDisplacement = constrainedPos - currentPos;
        return $"resolvedTrace currentPos={currentPos} currentNav={currentNavPos} desiredPos={desiredPos} desiredDisp={desiredHorizontalDisplacement} " +
               $"constrainedNav={constrainedNavPos} constrainedPos={constrainedPos} constrainedDisp={constrainedDisplacement} navHit={navHit.position}";
    }

    private string BuildConstraintFailureDiagnostics(
        string reason,
        Vector3 currentPos,
        Vector3 desiredPos,
        Vector3 desiredHorizontalDisplacement,
        bool desiredNavHit,
        NavMeshHit desiredNavHitInfo,
        float distFromNavMesh)
    {
        bool currentNavHit = NavMesh.SamplePosition(currentPos, out NavMeshHit currentNavHitInfo, _sampleRadius, _navFilter);
        bool navRayHit = false;
        NavMeshHit navRayHitInfo = default;
        if (currentNavHit)
        {
            Vector3 rayStart = currentNavHitInfo.position;
            Vector3 rayEnd = new Vector3(rayStart.x + desiredHorizontalDisplacement.x, rayStart.y, rayStart.z + desiredHorizontalDisplacement.z);
            navRayHit = NavMesh.Raycast(rayStart, rayEnd, out navRayHitInfo, _navFilter);
        }

        string ownerKey = _ownerEntity != null ? _ownerEntity.CharacterKey : "null";
        string desiredNav = desiredNavHit ? desiredNavHitInfo.position.ToString() : "none";
        string currentNav = currentNavHit ? currentNavHitInfo.position.ToString() : "none";
        string rayHitPos = navRayHit ? navRayHitInfo.position.ToString() : "none";
        string rayHitNormal = navRayHit ? navRayHitInfo.normal.ToString() : "none";

        return $"[MoveExecutor] {reason} gameObject={gameObject.name} owner={ownerKey} side={_ownerEntity?.Side.ToString() ?? "null"} " +
               $"agentType={_navFilter.agentTypeID} mode={_movementMode} sampleRadius={_sampleRadius:F2} edgeBuffer={_edgeBuffer:F2} " +
               $"currentPos={currentPos} currentNavHit={currentNavHit} currentNavPos={currentNav} " +
               $"desiredPos={desiredPos} desiredHorizontalDisplacement={desiredHorizontalDisplacement} " +
               $"desiredNavHit={desiredNavHit} desiredNavPos={desiredNav} distFromNavMesh={distFromNavMesh:F3} " +
               $"navRayHit={navRayHit} navRayPos={rayHitPos} navRayNormal={rayHitNormal} {_lastConstraintTrace ?? "trace=none"}";
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

    private bool IsInvadeTutorialStrongholdBlocked(Vector3 worldPosition)
    {
        if (_ownerEntity == null)
        {
            _ownerEntity = GetComponent<MAEntity>();
        }

        if (_ownerEntity == null || _ownerEntity.Side != SideType.PlayerSide)
        {
            return false;
        }

        return TutorialManager.IsInvadeTutorialMovementBlocked(worldPosition);
    }
}
