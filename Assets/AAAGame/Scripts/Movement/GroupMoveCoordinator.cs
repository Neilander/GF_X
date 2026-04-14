using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 群体移动协调器：LJ 力（聚团 + 排斥）+ ORCA（障碍物避让）。
///
/// LJ 力（Lennard-Jones）：
/// - 同组 Follow 状态：吸引 + 斥力
/// - 同组 Combat 状态：只有斥力（不被拉回去）
/// - 友方不同组：只有斥力
/// - 敌对阵营：只有斥力
/// - 组 = 跟随同一个领袖（GroupId = 领袖 entityId）
///
/// ORCA：
/// - 障碍物避让（权重极大，不可推动）
///
/// 使用流程：
/// 1. Agent 在 Show 时 RegisterAgent，Hide 时 UnregisterAgent
/// 2. 障碍物在 Show 时 RegisterObstacle，Hide 时 UnregisterObstacle
/// 3. Brain.Tick 阶段调用 SubmitDesiredVelocity 提交期望速度和回调
/// 4. 帧末调用 Resolve，协调器统一计算并触发所有回调
/// </summary>
public class GroupMoveCoordinator
{
    public enum AgentState
    {
        Idle,
        Follow,
        Combat
    }

    public struct AgentData
    {
        public int Id;
        public Vector3 Position;
        public float Radius;              // 碰撞半径
        public float EquilibriumRadius;    // LJ 平衡距离（斥力半径）
        public float MaxInfluenceRange;    // LJ 最大影响范围
        public float RepulsionStrength;    // 斥力强度
        public float AttractionStrength;   // 吸引力强度
        public SideType Side;             // 阵营
        public bool IsLeader;             // 是否领袖（玩家等）
        public int GroupId;               // 组 ID（= 领袖 entityId，-1 表示无组）
        public AgentState State;          // 当前状态
    }

    public struct ObstacleData
    {
        public int Id;
        public Vector3 Position;
        public float Radius;
        public Vector3 HalfExtents;

        public bool IsAABB => HalfExtents.sqrMagnitude > 0.001f;

        public Vector3 ClosestPoint(Vector3 agentPos)
        {
            if (IsAABB)
            {
                return new Vector3(
                    Mathf.Clamp(agentPos.x, Position.x - HalfExtents.x, Position.x + HalfExtents.x),
                    Mathf.Clamp(agentPos.y, Position.y - HalfExtents.y, Position.y + HalfExtents.y),
                    Mathf.Clamp(agentPos.z, Position.z - HalfExtents.z, Position.z + HalfExtents.z)
                );
            }
            else
            {
                Vector3 dir = agentPos - Position;
                if (dir.sqrMagnitude < 0.001f) return Position;
                return Position + dir.normalized * Radius;
            }
        }

        public float DistanceToSurface(Vector3 agentPos)
        {
            Vector3 closest = ClosestPoint(agentPos);
            float dist = Vector3.Distance(agentPos, closest);

            if (IsAABB)
            {
                bool inside = agentPos.x >= Position.x - HalfExtents.x && agentPos.x <= Position.x + HalfExtents.x
                           && agentPos.z >= Position.z - HalfExtents.z && agentPos.z <= Position.z + HalfExtents.z;
                if (inside) return -dist;
            }
            else
            {
                if (Vector3.Distance(agentPos, Position) < Radius)
                    return -dist;
            }

            return dist;
        }
    }

    private struct VelocityRequest
    {
        public int AgentId;
        public Vector3 DesiredVelocity;
        public Action<Vector3> Callback;
    }

    // 已注册的 agent 和障碍物
    private readonly Dictionary<int, AgentData> _agents = new Dictionary<int, AgentData>();
    private readonly Dictionary<int, ObstacleData> _obstacles = new Dictionary<int, ObstacleData>();
    private readonly List<VelocityRequest> _requests = new List<VelocityRequest>();

    // ── Gizmos 调试数据 ──
    public struct DebugAgentInfo
    {
        public Vector3 Position;
        public float EquilibriumRadius;
        public Vector3 LJForce;           // LJ 合力
        public Vector3 ObstacleForce;     // 障碍物推力
        public Vector3 DesiredVelocity;   // 期望速度（Brain 提交）
        public Vector3 SafeVelocity;      // 最终安全速度
    }
    public readonly Dictionary<int, DebugAgentInfo> DebugData = new Dictionary<int, DebugAgentInfo>();

    // HTML 测试页面像素值 ÷ 此常量 = 游戏世界单位
    // 修改这一个值即可校准所有参数
    public const float PX_SCALE = 20f;

    // 单位 LJ 参数
    public float UnitRepulsionStrength = 50f;
    public float UnitAttractionStrength = 2f;
    public float DefaultEquilibriumRadius = 60f / PX_SCALE;
    public float DefaultMaxInfluenceRange = 370f / PX_SCALE;

    // 领袖 LJ 参数
    public float LeaderRepulsionStrength = 50f;
    public float LeaderAttractionStrength = 2f;
    public float LeaderEquilibriumRadius = 105f / PX_SCALE;
    public float LeaderMaxInfluenceRange = 370f / PX_SCALE;

    // 敌对阵营 LJ 参数
    public float EnemyRepulsionStrength = 50f;    // 敌对斥力
    public float EnemyAttractionStrength = 10f;   // 敌对吸引力
    public float EnemyEquilibriumRadius = 1.5f;   // 敌对斥力半径（攻击距离）
    public float EnemyMaxInfluenceRange = 15f;

    // ORCA 参数
    public float ObstacleWeight = 100f;

    // 力低于此值直接返回零速度（防抖动）
    public float MoveThreshold = 0.5f;

    // ── 注册/注销 ──

    /// <summary>
    /// 注册 agent。
    /// equilibriumRadius = LJ 平衡距离（斥力半径），玩家用更大的值。
    /// maxInfluenceRange = LJ 最大影响范围，超过此距离无力。
    /// </summary>
    public void RegisterAgent(int id, Vector3 position, SideType side, bool isLeader = false, float radius = 0.5f)
    {
        _agents[id] = new AgentData
        {
            Id = id,
            Position = position,
            Radius = radius,
            Side = side,
            IsLeader = isLeader,
            EquilibriumRadius = isLeader ? LeaderEquilibriumRadius : DefaultEquilibriumRadius,
            MaxInfluenceRange = isLeader ? LeaderMaxInfluenceRange : DefaultMaxInfluenceRange,
            RepulsionStrength = isLeader ? LeaderRepulsionStrength : UnitRepulsionStrength,
            AttractionStrength = isLeader ? LeaderAttractionStrength : UnitAttractionStrength
        };
    }

    public void UnregisterAgent(int id)
    {
        _agents.Remove(id);
    }

    public void SetAgentLeader(int id, bool isLeader)
    {
        if (_agents.TryGetValue(id, out var data))
        {
            data.IsLeader = isLeader;
            data.EquilibriumRadius = isLeader ? LeaderEquilibriumRadius : DefaultEquilibriumRadius;
            data.MaxInfluenceRange = isLeader ? LeaderMaxInfluenceRange : DefaultMaxInfluenceRange;
            data.RepulsionStrength = isLeader ? LeaderRepulsionStrength : UnitRepulsionStrength;
            data.AttractionStrength = isLeader ? LeaderAttractionStrength : UnitAttractionStrength;
            _agents[id] = data;
        }
    }

    public void SetAgentGroup(int id, int groupId)
    {
        if (_agents.TryGetValue(id, out var data))
        {
            data.GroupId = groupId;
            _agents[id] = data;
        }
    }

    public void SetAgentState(int id, AgentState state)
    {
        if (_agents.TryGetValue(id, out var data))
        {
            GameDebugSettings.Log(DebugCategory.GroupMove,
                $"[Coordinator] SetAgentState id={id} side={data.Side} group={data.GroupId} {data.State}→{state}");
            data.State = state;
            _agents[id] = data;
        }
    }

    public void UpdateAgentPosition(int id, Vector3 position)
    {
        if (_agents.TryGetValue(id, out var data))
        {
            data.Position = position;
            _agents[id] = data;
        }
    }

    public void SetAgentRadius(int id, float radius)
    {
        if (_agents.TryGetValue(id, out var data))
        {
            data.Radius = Mathf.Max(0.01f, radius);
            _agents[id] = data;
        }
    }

    public void RegisterObstacle(int id, Vector3 position, float radius = 1f)
    {
        _obstacles[id] = new ObstacleData { Id = id, Position = position, Radius = radius };
    }

    public void RegisterBoxObstacle(int id, Vector3 center, Vector3 halfExtents)
    {
        _obstacles[id] = new ObstacleData { Id = id, Position = center, HalfExtents = halfExtents };
    }

    public void UnregisterObstacle(int id)
    {
        _obstacles.Remove(id);
    }

    // ── 提交与求解 ──

    public void SubmitDesiredVelocity(int agentId, Vector3 desiredVelocity, Action<Vector3> callback)
    {
        _requests.Add(new VelocityRequest
        {
            AgentId = agentId,
            DesiredVelocity = desiredVelocity,
            Callback = callback
        });
    }

    public void Resolve()
    {
        DebugData.Clear();

        for (int i = 0; i < _requests.Count; i++)
        {
            var req = _requests[i];
            if (!_agents.TryGetValue(req.AgentId, out var self))
                continue;

            Vector3 safeVelocity = ComputeSafeVelocity(self, req.DesiredVelocity, out var ljForce, out var obsForce);

            DebugData[req.AgentId] = new DebugAgentInfo
            {
                Position = self.Position,
                EquilibriumRadius = self.EquilibriumRadius,
                LJForce = ljForce,
                ObstacleForce = obsForce,
                DesiredVelocity = req.DesiredVelocity,
                SafeVelocity = safeVelocity
            };

            req.Callback?.Invoke(safeVelocity);
        }

        _requests.Clear();
    }

    private Vector3 ComputeSafeVelocity(AgentData self, Vector3 desiredVelocity,
        out Vector3 outLJForce, out Vector3 outObstacleForce)
    {
        Vector3 ljForce = Vector3.zero;
        Vector3 obstacleAdjustment = Vector3.zero;

        // 1. LJ 力：根据阵营关系选不同参数
        foreach (var kvp in _agents)
        {
            if (kvp.Key == self.Id) continue;
            var other = kvp.Value;

            Vector3 toOther = other.Position - self.Position;
            toOther.y = 0;
            float dist = toOther.magnitude;

            if (dist < 0.001f)
            {
                ljForce += new Vector3(
                    UnityEngine.Random.Range(-1f, 1f), 0f,
                    UnityEngine.Random.Range(-1f, 1f)
                ) * Mathf.Max(self.RepulsionStrength, other.RepulsionStrength);
                continue;
            }

            // 根据阵营关系 + 组关系选力参数
            bool sameSide = self.Side == other.Side;
            bool isEnemy = self.Side != SideType.NoSide && other.Side != SideType.NoSide && !sameSide;
            bool sameGroup = sameSide && self.GroupId != 0 && self.GroupId == other.GroupId;

            GameDebugSettings.Log(DebugCategory.GroupMove,
                $"[Force] self={self.Id}(side={self.Side},grp={self.GroupId},st={self.State}) " +
                $"other={kvp.Key}(side={other.Side},grp={other.GroupId},leader={other.IsLeader}) " +
                $"isEnemy={isEnemy} sameGroup={sameGroup} dist={dist:F2}");

            float eq, maxRange, repStr, attStr;

            if (isEnemy)
            {
                // 敌对：只有斥力
                eq = EnemyEquilibriumRadius;
                maxRange = EnemyMaxInfluenceRange;
                repStr = EnemyRepulsionStrength;
                attStr = 0f;
            }
            else if (!sameGroup)
            {
                // 友方不同组：只有斥力
                eq = Mathf.Max(self.EquilibriumRadius, other.EquilibriumRadius);
                maxRange = Mathf.Max(self.MaxInfluenceRange, other.MaxInfluenceRange);
                repStr = Mathf.Max(self.RepulsionStrength, other.RepulsionStrength);
                attStr = 0f;
            }
            else if (other.IsLeader)
            {
                // 同组领袖：只有斥力，跟随由 Brain 用 NavMesh 导航实现
                eq = other.EquilibriumRadius;
                maxRange = other.MaxInfluenceRange;
                repStr = other.RepulsionStrength;
                attStr = 0f;
            }
            else
            {
                // 同组普通单位：Follow 有吸引，Combat 无吸引
                eq = Mathf.Max(self.EquilibriumRadius, other.EquilibriumRadius);
                maxRange = Mathf.Max(self.MaxInfluenceRange, other.MaxInfluenceRange);
                repStr = Mathf.Max(self.RepulsionStrength, other.RepulsionStrength);
                attStr = self.State == AgentState.Follow
                    ? Mathf.Max(self.AttractionStrength, other.AttractionStrength)
                    : 0f;
            }

            if (dist > maxRange) continue;

            Vector3 dir = toOther.normalized;

            if (dist < eq)
            {
                float t = 1f - dist / eq;
                ljForce -= dir * (repStr * t * t);
            }
            else
            {
                if (other.IsLeader)
                {
                    // 领袖引力恒定，不随距离衰减
                    ljForce += dir * attStr;
                }
                else
                {
                    float t = (dist - eq) / (maxRange - eq);
                    t = Mathf.Clamp01(t);
                    ljForce += dir * (attStr * (1f - t));
                }
            }
        }

        // 2. 障碍物避让（ORCA，不变）
        foreach (var kvp in _obstacles)
        {
            var obs = kvp.Value;
            float surfaceDist = obs.DistanceToSurface(self.Position);
            float effectiveDist = surfaceDist - self.Radius;

            if (effectiveDist > self.MaxInfluenceRange) continue;

            Vector3 closestPt = obs.ClosestPoint(self.Position);

            Vector3 pushDir;
            if (surfaceDist < 0f)
            {
                pushDir = (self.Position - obs.Position);
                if (pushDir.sqrMagnitude < 0.001f)
                    pushDir = new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f));
            }
            else
            {
                pushDir = (self.Position - closestPt);
                if (pushDir.sqrMagnitude < 0.001f)
                    pushDir = new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f));
            }
            pushDir = pushDir.normalized;

            if (effectiveDist < 0f)
            {
                float overlap = -effectiveDist;
                obstacleAdjustment += pushDir * overlap * ObstacleWeight;
            }
            else
            {
                Vector3 toObs = -pushDir;
                float dot = Vector3.Dot(desiredVelocity.normalized, toObs);

                if (dot > 0.3f)
                {
                    float proximity = 1f - effectiveDist / self.MaxInfluenceRange;
                    proximity = Mathf.Clamp01(proximity);

                    Vector3 tangent = Vector3.Cross(Vector3.up, toObs).normalized;
                    if (tangent.sqrMagnitude < 0.001f)
                        tangent = Vector3.Cross(Vector3.forward, toObs).normalized;
                    if (Vector3.Dot(tangent, desiredVelocity) < 0)
                        tangent = -tangent;

                    obstacleAdjustment += tangent * dot * proximity * desiredVelocity.magnitude * 0.5f;
                    obstacleAdjustment -= toObs * dot * proximity * desiredVelocity.magnitude * 0.5f;
                }
            }
        }

        // 输出调试数据
        outLJForce = ljForce;
        outObstacleForce = obstacleAdjustment;

        // 合成：期望速度 + LJ 力 + 障碍物修正
        Vector3 safeVelocity = desiredVelocity + ljForce + obstacleAdjustment;

        // 力太小时平滑衰减，不硬切断
        float mag = safeVelocity.magnitude;
        if (mag < MoveThreshold)
        {
            float t = mag / MoveThreshold;
            safeVelocity *= t * t;
        }

        // 限速：不超过期望速度 + LJ 力的合理范围
        float maxSpeed = Mathf.Max(desiredVelocity.magnitude, ljForce.magnitude);
        if (maxSpeed > 0.001f && safeVelocity.magnitude > maxSpeed)
        {
            safeVelocity = safeVelocity.normalized * maxSpeed;
        }

        return safeVelocity;
    }

    /// <summary>
    /// Inspector 改参数后，同步到所有已注册 agent。
    /// 领袖用 LeaderEquilibriumRadius，普通 agent 用 DefaultEquilibriumRadius。
    /// </summary>
    public void SyncAllAgentParams()
    {
        var keys = new List<int>(_agents.Keys);
        foreach (var id in keys)
        {
            var data = _agents[id];
            data.EquilibriumRadius = data.IsLeader ? LeaderEquilibriumRadius : DefaultEquilibriumRadius;
            data.MaxInfluenceRange = data.IsLeader ? LeaderMaxInfluenceRange : DefaultMaxInfluenceRange;
            data.RepulsionStrength = data.IsLeader ? LeaderRepulsionStrength : UnitRepulsionStrength;
            data.AttractionStrength = data.IsLeader ? LeaderAttractionStrength : UnitAttractionStrength;
            _agents[id] = data;
        }
    }

    // ── 查询 ──

    public int AgentCount => _agents.Count;
    public int ObstacleCount => _obstacles.Count;
    public int PendingRequestCount => _requests.Count;

    public bool HasAgent(int id) => _agents.ContainsKey(id);
    public bool HasObstacle(int id) => _obstacles.ContainsKey(id);

    /// <summary>查询某 agent 的斥力半径，找不到返回 defaultValue。</summary>
    public float GetAgentEquilibriumRadius(int id, float defaultValue = 0f)
    {
        return _agents.TryGetValue(id, out var data) ? data.EquilibriumRadius : defaultValue;
    }

    /// <summary>所有已注册 agent（Gizmos 用）</summary>
    public IReadOnlyDictionary<int, AgentData> AllAgents => _agents;
}
