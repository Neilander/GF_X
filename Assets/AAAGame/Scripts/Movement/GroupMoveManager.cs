using UnityEngine;

public class GroupMoveManager : MonoBehaviour
{
    public static GroupMoveManager Instance { get; private set; }
    public static bool HasInstance => Instance != null;

    public GroupMoveCoordinator Coordinator { get; private set; }

    [SerializeField] private GroupMoveConfig _config;
    public GroupMoveConfig Config => _config;

    private bool _warnedConfigMissing;

    private void Awake()
    {
        Instance = this;
        Coordinator = new GroupMoveCoordinator();
        SyncParams();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        SyncParams();
    }

    private void LateUpdate()
    {
        Coordinator.Resolve();
    }

    private void SyncParams()
    {
        if (Coordinator == null) return;

        if (_config == null)
        {
            if (!_warnedConfigMissing)
            {
                Debug.LogWarning("[GroupMoveManager] Config 未指定，Coordinator 使用代码默认参数。请在 Inspector 拖入 GroupMoveConfig.asset。", this);
                _warnedConfigMissing = true;
            }
            return;
        }
        _warnedConfigMissing = false;

        var c = _config;
        Coordinator.UnitRepulsionStrength = c.UnitRepulsionStrength;
        Coordinator.UnitAttractionStrength = c.UnitAttractionStrength;
        Coordinator.DefaultEquilibriumRadius = c.UnitEquilibriumRadius;
        Coordinator.DefaultMaxInfluenceRange = c.UnitMaxInfluenceRange;

        Coordinator.LeaderRepulsionStrength = c.LeaderRepulsionStrength;
        Coordinator.LeaderAttractionStrength = c.LeaderAttractionStrength;
        Coordinator.LeaderEquilibriumRadius = c.LeaderEquilibriumRadius;
        Coordinator.LeaderMaxInfluenceRange = c.LeaderMaxInfluenceRange;

        Coordinator.EnemyRepulsionStrength = c.EnemyRepulsionStrength;
        Coordinator.EnemyAttractionStrength = c.EnemyAttractionStrength;
        Coordinator.EnemyEquilibriumRadius = c.EnemyEquilibriumRadius;
        Coordinator.EnemyMaxInfluenceRange = c.EnemyMaxInfluenceRange;

        Coordinator.ObstacleWeight = c.ObstacleWeight;
        Coordinator.MoveThreshold = c.MoveThreshold;
        Coordinator.MoveThresholdSpeedRatio = c.MoveThresholdSpeedRatio;
        Coordinator.VelocitySmoothing = c.VelocitySmoothing;

        Coordinator.SyncAllAgentParams();
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

    // ── Agent 注册 ──

    public void RegisterAgent(MAEntity entity)
    {
        float radius = ResolveAgentRadius(entity);
        Coordinator.RegisterAgent(entity.GetInstanceID(), entity.Position, entity.Side, false, radius);
    }

    public void UnregisterAgent(MAEntity entity)
    {
        Coordinator.UnregisterAgent(entity.GetInstanceID());
    }

    public void UpdateAgentSide(MAEntity entity)
    {
        if (entity == null)
            return;

        Coordinator.SetAgentSide(entity.GetInstanceID(), entity.Side);
    }

    public void UpdateAgentPosition(MAEntity entity)
    {
        int id = entity.GetInstanceID();
        Coordinator.UpdateAgentPosition(id, entity.Position);
        Coordinator.SetAgentRadius(id, ResolveAgentRadius(entity));
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
        Coordinator.RegisterObstacle(id, position, radius);
    }

    public void RegisterBoxObstacle(Collider collider)
    {
        var bounds = collider.bounds;
        Coordinator.RegisterBoxObstacle(collider.GetInstanceID(), bounds.center, bounds.extents);
    }

    public void UnregisterObstacle(int id)
    {
        Coordinator.UnregisterObstacle(id);
    }

    // ── Gizmos ──

    private void OnDrawGizmos()
    {
        if (Coordinator == null) return;

        foreach (var kvp in Coordinator.AllAgents)
        {
            var agent = kvp.Value;
            bool isLeader = agent.IsLeader;

            Gizmos.color = isLeader ? Color.yellow : Color.white;
            DrawCircle(agent.Position, agent.EquilibriumRadius, 24);

            Gizmos.color = isLeader ? new Color(1f, 0.6f, 0f, 0.8f) : new Color(0.5f, 0.5f, 1f, 0.6f);
            DrawCircle(agent.Position, agent.MaxInfluenceRange, 32);
        }

        foreach (var kvp in Coordinator.DebugData)
        {
            var info = kvp.Value;
            var pos = info.Position;

            if (info.LJForce.sqrMagnitude > 0.01f)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(pos, pos + info.LJForce * 0.3f);
            }

            if (info.ObstacleForce.sqrMagnitude > 0.01f)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(pos, pos + info.ObstacleForce * 0.3f);
            }

            if (info.DesiredVelocity.sqrMagnitude > 0.01f)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(pos, pos + info.DesiredVelocity.normalized * 1f);
            }
        }
    }

    private static void DrawCircle(Vector3 center, float radius, int segments)
    {
        float step = 360f / segments;
        Vector3 prev = center + new Vector3(radius, 0, 0);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * step * Mathf.Deg2Rad;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

    private static void DrawArrowHead(Vector3 tip, Vector3 dir, float size)
    {
        Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
        Gizmos.DrawLine(tip, tip - dir * size + right * size * 0.5f);
        Gizmos.DrawLine(tip, tip - dir * size - right * size * 0.5f);
    }
}
