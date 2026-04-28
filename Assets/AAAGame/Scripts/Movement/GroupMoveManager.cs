using UnityEngine;

public class GroupMoveManager : MonoBehaviour
{
    public static GroupMoveManager Instance { get; private set; }
    public static bool HasInstance => Instance != null;

    public GroupMoveCoordinator Coordinator { get; private set; }

    // ── Inspector 调参面板 ──

    [Header("单位 LJ 参数")]
    public float UnitRepulsionStrength = 50f;
    public float UnitAttractionStrength = 2f;
    public float UnitEquilibriumRadius = 60f / GroupMoveCoordinator.PX_SCALE;
    public float UnitMaxInfluenceRange = 370f / GroupMoveCoordinator.PX_SCALE;

    [Header("领袖 LJ 参数")]
    public float LeaderRepulsionStrength = 50f;
    public float LeaderAttractionStrength = 2f;
    public float LeaderEquilibriumRadius = 105f / GroupMoveCoordinator.PX_SCALE;
    public float LeaderMaxInfluenceRange = 370f / GroupMoveCoordinator.PX_SCALE;

    [Header("敌对阵营 LJ 参数")]
    public float EnemyRepulsionStrength = 50f;
    public float EnemyAttractionStrength = 10f;
    public float EnemyEquilibriumRadius = 1.5f;
    public float EnemyMaxInfluenceRange = 15f;

    [Header("障碍物")]
    public float ObstacleWeight = 100f;

    [Header("移动阈值")]
    [Tooltip("没有单位速度时使用的兜底阈值")]
    public float MoveThreshold = 0.5f;
    [Tooltip("按单位世界速度的倍率计算低速忽略阈值。1.5 = 速度的 150%")]
    public float MoveThresholdSpeedRatio = 1.5f;
    [Tooltip("最终安全速度平滑系数。1=不平滑，越低越稳但响应越慢")]
    [Range(0.01f, 1f)]
    public float VelocitySmoothing = 0.35f;

    private const string PREFS_KEY = "GroupMoveManager_Params";

    private void Awake()
    {
        Instance = this;
        Coordinator = new GroupMoveCoordinator();
        LoadParams();
        SyncParams();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        SyncParams();
        SaveParams(); // 每帧存，保证退出 Play 前一定存上
    }

    [ContextMenu("保存参数")]
    public void SaveParams()
    {
        var json = JsonUtility.ToJson(new SaveData
        {
            unitRepStr = UnitRepulsionStrength,
            unitAttStr = UnitAttractionStrength,
            unitEqR = UnitEquilibriumRadius,
            unitMaxR = UnitMaxInfluenceRange,
            leaderRepStr = LeaderRepulsionStrength,
            leaderAttStr = LeaderAttractionStrength,
            leaderEqR = LeaderEquilibriumRadius,
            leaderMaxR = LeaderMaxInfluenceRange,
            enemyRepStr = EnemyRepulsionStrength,
            enemyAttStr = EnemyAttractionStrength,
            enemyEqR = EnemyEquilibriumRadius,
            enemyMaxR = EnemyMaxInfluenceRange,
            obsWeight = ObstacleWeight,
            moveThreshold = MoveThreshold,
            moveThresholdSpeedRatio = MoveThresholdSpeedRatio,
            velocitySmoothing = VelocitySmoothing,
        });
        PlayerPrefs.SetString(PREFS_KEY, json);
        PlayerPrefs.Save();
    }

    [ContextMenu("读取参数")]
    public void LoadParams()
    {
        if (!PlayerPrefs.HasKey(PREFS_KEY)) return;
        var json = PlayerPrefs.GetString(PREFS_KEY);
        var d = JsonUtility.FromJson<SaveData>(json);
        UnitRepulsionStrength = d.unitRepStr;
        UnitAttractionStrength = d.unitAttStr;
        UnitEquilibriumRadius = d.unitEqR;
        UnitMaxInfluenceRange = d.unitMaxR;
        LeaderRepulsionStrength = d.leaderRepStr;
        LeaderAttractionStrength = d.leaderAttStr;
        LeaderEquilibriumRadius = d.leaderEqR;
        LeaderMaxInfluenceRange = d.leaderMaxR;
        EnemyRepulsionStrength = d.enemyRepStr;
        EnemyAttractionStrength = d.enemyAttStr;
        EnemyEquilibriumRadius = d.enemyEqR;
        EnemyMaxInfluenceRange = d.enemyMaxR;
        ObstacleWeight = d.obsWeight;
        MoveThreshold = d.moveThreshold;
        MoveThresholdSpeedRatio = d.moveThresholdSpeedRatio <= 0f ? MoveThresholdSpeedRatio : d.moveThresholdSpeedRatio;
        VelocitySmoothing = d.velocitySmoothing <= 0f ? VelocitySmoothing : d.velocitySmoothing;
    }

    [System.Serializable]
    private struct SaveData
    {
        public float unitRepStr, unitAttStr, unitEqR, unitMaxR;
        public float leaderRepStr, leaderAttStr, leaderEqR, leaderMaxR;
        public float enemyRepStr, enemyAttStr, enemyEqR, enemyMaxR;
        public float obsWeight;
        public float moveThreshold;
        public float moveThresholdSpeedRatio;
        public float velocitySmoothing;
    }

    private void LateUpdate()
    {
        Coordinator.Resolve();
    }

    private void SyncParams()
    {
        if (Coordinator == null) return;
        Coordinator.UnitRepulsionStrength = UnitRepulsionStrength;
        Coordinator.UnitAttractionStrength = UnitAttractionStrength;
        Coordinator.DefaultEquilibriumRadius = UnitEquilibriumRadius;
        Coordinator.DefaultMaxInfluenceRange = UnitMaxInfluenceRange;

        Coordinator.LeaderRepulsionStrength = LeaderRepulsionStrength;
        Coordinator.LeaderAttractionStrength = LeaderAttractionStrength;
        Coordinator.LeaderEquilibriumRadius = LeaderEquilibriumRadius;
        Coordinator.LeaderMaxInfluenceRange = LeaderMaxInfluenceRange;

        Coordinator.EnemyRepulsionStrength = EnemyRepulsionStrength;
        Coordinator.EnemyAttractionStrength = EnemyAttractionStrength;
        Coordinator.EnemyEquilibriumRadius = EnemyEquilibriumRadius;
        Coordinator.EnemyMaxInfluenceRange = EnemyMaxInfluenceRange;

        Coordinator.ObstacleWeight = ObstacleWeight;
        Coordinator.MoveThreshold = MoveThreshold;
        Coordinator.MoveThresholdSpeedRatio = MoveThresholdSpeedRatio;
        Coordinator.VelocitySmoothing = VelocitySmoothing;

        Coordinator.SyncAllAgentParams();
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

            // 斥力半径（实线，深色）
            Gizmos.color = isLeader ? Color.yellow : Color.white;
            DrawCircle(agent.Position, agent.EquilibriumRadius, 24);

            // 最大影响范围（深色）
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

            // if (info.SafeVelocity.sqrMagnitude > 0.01f)
            // {
            //     Gizmos.color = Color.green;
            //     Gizmos.DrawLine(pos, pos + info.SafeVelocity.normalized * 1.2f);
            //     DrawArrowHead(pos + info.SafeVelocity.normalized * 1.2f, info.SafeVelocity.normalized, 0.2f);
            // }
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
