using Stopwatch = System.Diagnostics.Stopwatch;
using UnityGameFramework.Runtime;
using UnityEngine;

public class GroupMoveManager : MonoBehaviour, ILogicFrameUpdate
{
    public static GroupMoveManager Instance { get; private set; }
    public static bool HasInstance => Instance != null;

    [SerializeField] private GroupMoveConfig _config;
    [SerializeField] private FlowFieldNavigationConfig _flowFieldConfig;
    private bool _logicFrameRegistered;
    public GroupMoveConfig Config => _config;
    public FlowFieldNavigationConfig FlowFieldConfig => _flowFieldConfig;
    public int LogicFrameOrder => -1000;

    private void Awake()
    {
        Instance = this;
        ApplyFlowFieldConfig();
        LogicFrameRuntime.Began += HandleLogicRuntimeBegan;
        LogicFrameRuntime.Ending += HandleLogicRuntimeEnding;
        if (LogicFrameRuntime.IsActive)
            RegisterLogicFrameListener();
    }

    private void OnDestroy()
    {
        LogicFrameRuntime.Began -= HandleLogicRuntimeBegan;
        LogicFrameRuntime.Ending -= HandleLogicRuntimeEnding;
        if (_logicFrameRegistered)
        {
            if (!LogicFrameRuntime.IsActive)
                throw new System.InvalidOperationException("GroupMoveManager.OnDestroy failed: logic listener outlived the runtime.");
            UnregisterLogicFrameListener();
        }
        if (Instance == this)
            FlowFieldCrowdMovementSystem.ResetAll();
        if (Instance == this) Instance = null;
    }

    private void HandleLogicRuntimeBegan()
    {
        RegisterLogicFrameListener();
    }

    private void HandleLogicRuntimeEnding()
    {
        UnregisterLogicFrameListener();
    }

    private void RegisterLogicFrameListener()
    {
        if (_logicFrameRegistered)
            throw new System.InvalidOperationException("GroupMoveManager registration failed: listener is already registered.");

        LogicFrameRuntime.Register(this);
        _logicFrameRegistered = true;
    }

    private void UnregisterLogicFrameListener()
    {
        if (!_logicFrameRegistered)
            throw new System.InvalidOperationException("GroupMoveManager unregistration failed: listener is not registered.");

        LogicFrameRuntime.Unregister(this);
        _logicFrameRegistered = false;
    }

    public void OnLogicFrameUpdate(Fix64 deltaTime)
    {
        UpdateNavigationRuntime();
    }

    private void UpdateNavigationRuntime()
    {
        FlowFieldCrowdMovementSystem.PulsePerformanceFrame();
        long updateStartTicks = Stopwatch.GetTimestamp();
        long updateStartAllocatedBytes = System.GC.GetAllocatedBytesForCurrentThread();
        try
        {
            long sectionStartTicks = Stopwatch.GetTimestamp();
            ApplyFlowFieldConfig();
            long configTicks = Stopwatch.GetTimestamp() - sectionStartTicks;
            FlowFieldCrowdMovementSystem.RecordManagerConfigTicks(configTicks);
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowConfig, configTicks);

            sectionStartTicks = Stopwatch.GetTimestamp();
            if (_flowFieldConfig.RequireAuthoredNavigationSource
                && !FlowFieldCrowdMovementSystem.HasAuthoredNavigationSource())
            {
                if (FlowFieldCrowdMovementSystem.IsRuntimeNavigationTransitionActive())
                {
                    long transitionGateTicks = Stopwatch.GetTimestamp() - sectionStartTicks;
                    FlowFieldCrowdMovementSystem.RecordManagerSourceGateTicks(transitionGateTicks);
                    MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowSourceGate, transitionGateTicks);
                    return;
                }

                if (FlowFieldCrowdMovementSystem.HasActiveNavigationAgents())
                {
                    throw new System.InvalidOperationException(
                        "GroupMoveManager.UpdateNavigationRuntime failed: active navigation agents exist before any FlowNavigationGridSource has applied a FlowNavigationGridAsset.");
                }

                long sourceGateTicks = Stopwatch.GetTimestamp() - sectionStartTicks;
                FlowFieldCrowdMovementSystem.RecordManagerSourceGateTicks(sourceGateTicks);
                MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowSourceGate, sourceGateTicks);
                return;
            }

            long sourceCheckTicks = Stopwatch.GetTimestamp() - sectionStartTicks;
            FlowFieldCrowdMovementSystem.RecordManagerSourceGateTicks(sourceCheckTicks);
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowSourceGate, sourceCheckTicks);

            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        }
        finally
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowGroupMove,
                Stopwatch.GetTimestamp() - updateStartTicks,
                System.Math.Max(0L, System.GC.GetAllocatedBytesForCurrentThread() - updateStartAllocatedBytes));
        }
    }

    private void ApplyFlowFieldConfig()
    {
        if (_flowFieldConfig == null)
            throw new System.InvalidOperationException("GroupMoveManager.ApplyFlowFieldConfig failed: FlowFieldNavigationConfig is not assigned.");

        FlowFieldCrowdMovementSystem.SetConfig(_flowFieldConfig);
    }

    // ── Agent 注册 ──

    public void RegisterAgent(IEntityContext entity)
    {
        if (entity == null)
            throw new System.ArgumentNullException(nameof(entity));

        FlowFieldCrowdMovementSystem.RegisterAgent(entity);
    }

    public void UnregisterAgent(IEntityContext entity)
    {
        if (entity == null)
            throw new System.ArgumentNullException(nameof(entity));

        FlowFieldCrowdMovementSystem.UnregisterAgent(entity.LogicEntityId.Value);
    }

    public void UpdateAgentSide(IEntityContext entity)
    {
        if (entity == null)
            throw new System.ArgumentNullException(nameof(entity));

        FlowFieldCrowdMovementSystem.SetAgentSide(entity.LogicEntityId.Value, entity.Side);
    }

    public void UpdateAgentPosition(IEntityContext entity)
    {
        if (entity == null)
            throw new System.ArgumentNullException(nameof(entity));

        FlowFieldCrowdMovementSystem.UpdateAgent(entity);
    }

    // ── 障碍物注册 ──

    public void RegisterCircleObstacle(int id, Vector3 position, float radius)
    {
        FlowFieldCrowdMovementSystem.RegisterCircleObstacle(id, position, radius);
    }

    public void RegisterBoxObstacle(int obstacleId, Collider collider)
    {
        if (collider == null)
            throw new System.InvalidOperationException("GroupMoveManager.RegisterBoxObstacle failed: collider is null.");

        ValidateObstacleId(obstacleId);
        Bounds bounds = ResolveColliderWorldBounds(collider);
        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(obstacleId, bounds.center, bounds.extents);
        LogColliderObstacleRegistration(collider, obstacleId, "box-direct", bounds);
    }

    public void RegisterBoxObstacle(int obstacleId, Vector3 center, Vector3 halfExtents)
    {
        ValidateObstacleId(obstacleId);
        if (halfExtents.x <= 0f || halfExtents.z <= 0f)
            throw new System.ArgumentOutOfRangeException(nameof(halfExtents), halfExtents, "Box half extents must be positive on XZ.");

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(obstacleId, center, halfExtents);
    }

    public void RegisterBoxCostStamp(int id, Vector3 center, Vector3 halfExtents, byte cost)
    {
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(id, center, halfExtents, cost);
    }

    public void RegisterBoxCostStamp(int id, int agentTypeId, Vector3 center, Vector3 halfExtents, byte cost)
    {
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(id, agentTypeId, center, halfExtents, cost);
    }

    public void RegisterGridCostStamp(int id, Vector3 origin, float cellSize, int width, int height, byte[] costs)
    {
        FlowFieldCrowdMovementSystem.RegisterGridCostStamp(id, origin, cellSize, width, height, costs);
    }

    public void RegisterGridCostStamp(int id, int agentTypeId, Vector3 origin, float cellSize, int width, int height, byte[] costs)
    {
        FlowFieldCrowdMovementSystem.RegisterGridCostStamp(id, agentTypeId, origin, cellSize, width, height, costs);
    }

    public void UnregisterCostStamp(int id)
    {
        FlowFieldCrowdMovementSystem.UnregisterCostStamp(id);
    }

    public int RegisterColliderObstacle(int obstacleId, Collider collider)
    {
        if (collider == null)
            throw new System.InvalidOperationException("GroupMoveManager.RegisterColliderObstacle failed: collider is null.");

        ValidateObstacleId(obstacleId);
        Bounds bounds = ResolveColliderWorldBounds(collider);
        if (collider is BoxCollider)
        {
            FlowFieldCrowdMovementSystem.RegisterBoxObstacle(obstacleId, bounds.center, bounds.extents);
            LogColliderObstacleRegistration(collider, obstacleId, "box", bounds);
            return obstacleId;
        }

        FlowFieldCrowdMovementSystem.RegisterCircleObstacle(obstacleId, bounds.center, bounds.extents.magnitude);
        LogColliderObstacleRegistration(collider, obstacleId, "circle", bounds);
        return obstacleId;
    }

    private static void ValidateObstacleId(int obstacleId)
    {
        if (obstacleId == 0)
            throw new System.ArgumentOutOfRangeException(nameof(obstacleId), "Obstacle id must be non-zero.");
    }

    public static Bounds ResolveColliderWorldBounds(Collider collider)
    {
        if (collider == null)
            throw new System.InvalidOperationException("GroupMoveManager.ResolveColliderWorldBounds failed: collider is null.");

        if (collider is BoxCollider boxCollider)
            return CalculateBoxColliderWorldBounds(boxCollider);

        return collider.bounds;
    }

    private static Bounds CalculateBoxColliderWorldBounds(BoxCollider collider)
    {
        if (collider == null)
            throw new System.InvalidOperationException("GroupMoveManager.CalculateBoxColliderWorldBounds failed: collider is null.");

        Vector3 half = collider.size * 0.5f;
        Matrix4x4 matrix = collider.transform.localToWorldMatrix;
        Vector3 localCenter = collider.center;
        Bounds bounds = new Bounds(matrix.MultiplyPoint3x4(localCenter + new Vector3(-half.x, -half.y, -half.z)), Vector3.zero);
        bounds.Encapsulate(matrix.MultiplyPoint3x4(localCenter + new Vector3(-half.x, -half.y, half.z)));
        bounds.Encapsulate(matrix.MultiplyPoint3x4(localCenter + new Vector3(-half.x, half.y, -half.z)));
        bounds.Encapsulate(matrix.MultiplyPoint3x4(localCenter + new Vector3(-half.x, half.y, half.z)));
        bounds.Encapsulate(matrix.MultiplyPoint3x4(localCenter + new Vector3(half.x, -half.y, -half.z)));
        bounds.Encapsulate(matrix.MultiplyPoint3x4(localCenter + new Vector3(half.x, -half.y, half.z)));
        bounds.Encapsulate(matrix.MultiplyPoint3x4(localCenter + new Vector3(half.x, half.y, -half.z)));
        bounds.Encapsulate(matrix.MultiplyPoint3x4(localCenter + new Vector3(half.x, half.y, half.z)));
        return bounds;
    }

    private static void LogColliderObstacleRegistration(Collider collider, int obstacleId, string shape, Bounds bounds)
    {
        bool isAutoBox = collider != null && collider.name.StartsWith("_AutoBox_", System.StringComparison.Ordinal);
        if (!isAutoBox && !GameDebugSettings.IsEnabled(DebugCategory.Move))
            return;

        UnityEngine.Debug.Log(
            $"[FlowColliderObstacle] register id={obstacleId} shape={shape} type={collider.GetType().Name} " +
            $"name={collider.gameObject.name} path={BuildHierarchyPath(collider.transform)} layer={collider.gameObject.layer} " +
            $"layerName={LayerMask.LayerToName(collider.gameObject.layer)} tag={collider.tag} enabled={collider.enabled} " +
            $"trigger={collider.isTrigger} active={collider.gameObject.activeInHierarchy} " +
            $"root={collider.transform.root.name} rootPos={collider.transform.root.position} " +
            $"localPos={collider.transform.localPosition} worldPos={collider.transform.position} " +
            $"center={bounds.center} size={bounds.size}");
    }

    private static string BuildHierarchyPath(Transform transform)
    {
        if (transform == null)
            return "null";

        System.Collections.Generic.Stack<string> parts = new System.Collections.Generic.Stack<string>();
        Transform current = transform;
        while (current != null)
        {
            parts.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", parts);
    }

    public void UnregisterObstacle(int id)
    {
        FlowFieldCrowdMovementSystem.UnregisterObstacle(id);
    }

    public void SetAgentIgnoreCollision(int id, bool ignore)
    {
        FlowFieldCrowdMovementSystem.SetAgentIgnoreCollision(id, ignore);
    }

    public void InvalidateNavigation(string reason = null)
    {
        FlowFieldCrowdMovementSystem.MarkWorldDirty(reason);
    }

    public void PrewarmNavigationWorlds()
    {
        FlowFieldCrowdMovementSystem.PrewarmNavigationWorlds();
    }

    public bool HasActiveNavigationAgents()
    {
        return FlowFieldCrowdMovementSystem.HasActiveNavigationAgents();
    }

    // ── Gizmos ──

    private void OnDrawGizmos()
    {
        FlowFieldCrowdMovementSystem.DrawGizmos();
    }
}
