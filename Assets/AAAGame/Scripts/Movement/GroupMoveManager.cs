using UnityEngine;

public class GroupMoveManager : MonoBehaviour
{
    public static GroupMoveManager Instance { get; private set; }
    public static bool HasInstance => Instance != null;

    [SerializeField] private GroupMoveConfig _config;
    public GroupMoveConfig Config => _config;

    private void Awake()
    {
        Instance = this;
        FlowFieldCrowdMovementSystem.SetConfig(_config);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            FlowFieldCrowdMovementSystem.ResetAll();
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        FlowFieldCrowdMovementSystem.SetConfig(_config);
        FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
    }

    /// <summary>
    /// 一次性迁移工具：把旧版 PlayerPrefs 里的调参写到当前 Config SO 中，并清掉 PlayerPrefs。
    /// 仅 Editor 使用，迁完即可移除。
    /// </summary>
    [ContextMenu("一次性迁移：旧 PlayerPrefs → Config SO")]
    public void MigrateLegacyPrefsToConfig()
    {
#if UNITY_EDITOR
        const string LEGACY_KEY = "GroupMoveManager_Params";
        if (_config == null)
        {
            Debug.LogError("[GroupMoveManager] 迁移失败：先在 Inspector 拖入 Config 资产再点迁移。", this);
            return;
        }
        if (!PlayerPrefs.HasKey(LEGACY_KEY))
        {
            Debug.Log("[GroupMoveManager] 没有旧 PlayerPrefs，无需迁移。", this);
            return;
        }

        var json = PlayerPrefs.GetString(LEGACY_KEY);
        var d = JsonUtility.FromJson<LegacySaveData>(json);
        if (d.unitEqR > 0f) _config.UnitEquilibriumRadius = d.unitEqR;
        if (d.unitMaxR > 0f) _config.UnitMaxInfluenceRange = d.unitMaxR;
        if (d.unitRepStr > 0f) _config.UnitRepulsionStrength = d.unitRepStr;
        if (d.unitAttStr > 0f) _config.UnitAttractionStrength = d.unitAttStr;

        if (d.leaderEqR > 0f) _config.LeaderEquilibriumRadius = d.leaderEqR;
        if (d.leaderMaxR > 0f) _config.LeaderMaxInfluenceRange = d.leaderMaxR;
        if (d.leaderRepStr > 0f) _config.LeaderRepulsionStrength = d.leaderRepStr;
        if (d.leaderAttStr > 0f) _config.LeaderAttractionStrength = d.leaderAttStr;

        if (d.enemyEqR > 0f) _config.EnemyEquilibriumRadius = d.enemyEqR;
        if (d.enemyMaxR > 0f) _config.EnemyMaxInfluenceRange = d.enemyMaxR;
        if (d.enemyRepStr > 0f) _config.EnemyRepulsionStrength = d.enemyRepStr;
        if (d.enemyAttStr > 0f) _config.EnemyAttractionStrength = d.enemyAttStr;

        if (d.obsWeight > 0f) _config.ObstacleWeight = d.obsWeight;
        if (d.moveThreshold > 0f) _config.MoveThreshold = d.moveThreshold;
        if (d.moveThresholdSpeedRatio > 0f) _config.MoveThresholdSpeedRatio = d.moveThresholdSpeedRatio;
        if (d.velocitySmoothing > 0f) _config.VelocitySmoothing = d.velocitySmoothing;
        if (d.followDeadZoneRange > 0f) _config.FollowDeadZoneRange = d.followDeadZoneRange;
        if (d.followInnerDeadZoneRange > 0f) _config.FollowInnerDeadZoneRange = d.followInnerDeadZoneRange;

        UnityEditor.EditorUtility.SetDirty(_config);
        UnityEditor.AssetDatabase.SaveAssets();
        PlayerPrefs.DeleteKey(LEGACY_KEY);
        PlayerPrefs.Save();
        Debug.Log("[GroupMoveManager] 迁移完成，旧 PlayerPrefs 已清除。", _config);
#else
        Debug.LogWarning("[GroupMoveManager] 迁移工具仅 Editor 可用。");
#endif
    }

#pragma warning disable 0649
    [System.Serializable]
    private struct LegacySaveData
    {
        public float unitRepStr, unitAttStr, unitEqR, unitMaxR;
        public float leaderRepStr, leaderAttStr, leaderEqR, leaderMaxR;
        public float enemyRepStr, enemyAttStr, enemyEqR, enemyMaxR;
        public float obsWeight;
        public float moveThreshold;
        public float moveThresholdSpeedRatio;
        public float velocitySmoothing;
        public float followDeadZoneRange;
        public float followInnerDeadZoneRange;
    }
#pragma warning restore 0649

    // ── Agent 注册 ──

    public void RegisterAgent(MAEntity entity)
    {
        float radius = ResolveAgentRadius(entity);
        FlowFieldCrowdMovementSystem.RegisterAgent(entity, false, radius);
    }

    public void UnregisterAgent(MAEntity entity)
    {
        FlowFieldCrowdMovementSystem.UnregisterAgent(entity.GetInstanceID());
    }

    public void UpdateAgentSide(MAEntity entity)
    {
        if (entity == null)
            return;

        FlowFieldCrowdMovementSystem.SetAgentSide(entity.GetInstanceID(), entity.Side);
    }

    public void UpdateAgentPosition(MAEntity entity)
    {
        int id = entity.GetInstanceID();
        float radius = ResolveAgentRadius(entity);
        FlowFieldCrowdMovementSystem.UpdateAgent(entity, radius);
    }

    private static float ResolveAgentRadius(MAEntity entity)
    {
        if (entity == null)
            return 0.5f;

        if (entity.CreaturePropertyManager != null)
        {
            float configuredRadius = DistanceUnitConverter.ConvertToWorldFloat(
                entity.GetProperty(CreatureMainProperty.CollisionRadius));
            if (configuredRadius > 0.0001f)
                return configuredRadius;
        }

        var cc = entity.GetComponent<CharacterController>();
        if (cc != null)
        {
            float scaleXZ = Mathf.Max(Mathf.Abs(entity.transform.lossyScale.x), Mathf.Abs(entity.transform.lossyScale.z));
            float worldRadius = cc.radius * scaleXZ;
            if (worldRadius > 0.0001f)
                return worldRadius;
        }

        return 0.5f;
    }

    // ── 障碍物注册 ──

    public void RegisterCircleObstacle(int id, Vector3 position, float radius)
    {
        FlowFieldCrowdMovementSystem.RegisterCircleObstacle(id, position, radius);
    }

    public void RegisterBoxObstacle(Collider collider)
    {
        var bounds = collider.bounds;
        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(collider.GetInstanceID(), bounds.center, bounds.extents);
    }

    public void RegisterBoxCostStamp(int id, Vector3 center, Vector3 halfExtents, byte cost)
    {
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(id, center, halfExtents, cost);
    }

    public void RegisterBoxCostStamp(int id, int agentTypeId, Vector3 center, Vector3 halfExtents, byte cost)
    {
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(id, agentTypeId, center, halfExtents, cost);
    }

    public void UnregisterCostStamp(int id)
    {
        FlowFieldCrowdMovementSystem.UnregisterCostStamp(id);
    }

    public int RegisterColliderObstacle(Collider collider)
    {
        if (collider == null)
            throw new System.InvalidOperationException("GroupMoveManager.RegisterColliderObstacle failed: collider is null.");

        int obstacleId = collider.GetInstanceID();
        var bounds = collider.bounds;
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

    private static void LogColliderObstacleRegistration(Collider collider, int obstacleId, string shape, Bounds bounds)
    {
        if (!GameDebugSettings.IsEnabled(DebugCategory.Move))
            return;

        Debug.Log(
            $"[FlowColliderObstacle] register id={obstacleId} shape={shape} type={collider.GetType().Name} " +
            $"name={collider.gameObject.name} path={BuildHierarchyPath(collider.transform)} layer={collider.gameObject.layer} " +
            $"tag={collider.tag} enabled={collider.enabled} trigger={collider.isTrigger} active={collider.gameObject.activeInHierarchy} " +
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

    public void SetAgentLeader(int id, bool isLeader)
    {
        FlowFieldCrowdMovementSystem.SetAgentLeader(id, isLeader);
    }

    public void SetAgentGroup(int id, int groupId)
    {
        FlowFieldCrowdMovementSystem.SetAgentGroup(id, groupId);
    }

    public void SetAgentState(int id, GroupMoveCoordinator.AgentState state)
    {
        FlowFieldCrowdMovementSystem.SetAgentState(id, state);
    }

    public void InvalidateNavigation(string reason = null)
    {
        FlowFieldCrowdMovementSystem.MarkWorldDirty(reason);
    }

    public void PrewarmNavigationWorlds()
    {
        FlowFieldCrowdMovementSystem.PrewarmNavigationWorlds();
    }

    public bool IsPositionOccupiedByAgent(Vector3 position, float requiredDistance)
    {
        return FlowFieldCrowdMovementSystem.IsPositionOccupiedByAgent(position, requiredDistance);
    }

    // ── Gizmos ──

    private void OnDrawGizmos()
    {
        FlowFieldCrowdMovementSystem.DrawGizmos();
    }
}
