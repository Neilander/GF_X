using UnityEngine;

/// <summary>
/// 小兵 AI Brain：基于 Steering Behaviors 的流体移动。
///
/// 三个状态：
/// - Idle：站着不动，等待玩家靠近
/// - Follow：跟随玩家，自然散开在不同距离
/// - Combat：发现敌人后脱离跟随，散开交战（用 NavMesh 寻路到敌人）
///
/// 核心原则：
/// 1. 同阵营不阻拦同阵营（separation 力自动避让）
/// 2. 小兵永远绕开玩家（AvoidEntity 力更强）
/// 3. 交战时自然散开（separation + 趋敌 seek 混合）
/// </summary>
public class SoldierAIBrain : IControlBrain, ITickBrain
{
    public enum SoldierState
    {
        Idle,
        Follow,
        Combat
    }

    // --- 配置参数 ---
    public float RecruitRadius = 8f;        // 玩家多近时开始跟随
    public float FollowDistanceMin = 2.5f;  // 跟随最近距离（不贴太紧）
    public float FollowDistanceMax = 5f;    // 跟随最远距离（超过才追）
    public float WeaponRange = 1.5f;          // 武器本身的攻击距离（WeaponData.AttackRange * 0.01）
    public float DetectEnemyRange = 10f;    // 发现敌人的距离
    public float SeparationRadius = 1.5f;   // 同阵营分离半径
    public float SeparationWeight = 1.5f;   // 分离力权重
    public float AvoidPlayerRadius = 2.5f;  // 避让玩家半径
    public float AvoidPlayerStrength = 3f;  // 避让玩家力度
    public float SeekWeight = 0.6f;         // 趋向力权重
    public float LeashRange = 30f;          // 脱离战斗回到跟随的距离

    // --- 状态 ---
    public SoldierState State { get; private set; } = SoldierState.Idle;

    // --- Brain 接口 ---
    public Vector2 Move { get; private set; }
    public bool Attack { get; private set; }
    public bool Skill1 => false;
    public bool Skill2 => false;
    public bool Skill3 => false;

    // --- 依赖 ---
    private IEntityContext _leader;

    // --- 内部 ---
    private Vector3 _desiredMoveDir;
    private bool _joinedGroup;
    private SoldierState _lastSyncedState = SoldierState.Idle;
    private bool _inDeadZone;                // 是否已进入 leader 附近的死区
    private float _deadZoneRange = 12f;      // 死区宽度：从斥力半径到斥力半径+此值
    private float _innerDeadZoneRange = 2f;  // 近死区：斥力半径+此值以内完全停下
    private Vector3? _deadZoneTarget;        // 死区内的随机导航目标点

    /// <summary>
    /// 领袖通过 EntityRegistry.GetClosestLeader 惰性获取。
    /// </summary>
    public void Inject() { }

    public Vector3 GetDesiredMoveDirection() => _desiredMoveDir;

    public void Tick(IEntityContext self, float dt)
    {
        Move = Vector2.zero;
        Attack = false;
        _desiredMoveDir = Vector3.zero;

        if (!self.Alive) return;

        // 惰性刷新领袖
        if (!IsValidFollowLeader(self, _leader))
            _leader = EntityRegistry.GetClosestLeader(self.Position);
        // 惰性标记领袖 + 设置组（仅同阵营，敌方不入玩家组）
        if (!_joinedGroup && _leader != null && _leader.Alive && self.Side == _leader.Side && GroupMoveManager.HasInstance)
        {
            int leaderId = (_leader as MAEntity)?.GetInstanceID() ?? _leader.GetHashCode();
            int selfId = (self as MAEntity)?.GetInstanceID() ?? self.GetHashCode();
            var coordinator = GroupMoveManager.Instance.Coordinator;
            coordinator.SetAgentLeader(leaderId, true);
            coordinator.SetAgentGroup(leaderId, leaderId); // 领袖自己也在组里
            coordinator.SetAgentGroup(selfId, leaderId);   // 自己加入领袖的组
            _joinedGroup = true;
            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.CharacterKey}] 加入组 groupId={leaderId}, leader={_leader.CharacterKey}");
        }

        UpdateState(self);
        SyncStateToCoordinator(self);

        switch (State)
        {
            case SoldierState.Idle:
                TickIdle(self, dt);
                break;
            case SoldierState.Follow:
                TickFollow(self, dt);
                break;
            case SoldierState.Combat:
                TickCombat(self, dt);
                break;
        }
    }

    private void UpdateState(IEntityContext self)
    {
        switch (State)
        {
            case SoldierState.Idle:
                // 有敌人 → 直接进 Combat
                if (IsValidAttackTarget(self, self.TargetComp?.CurrentTarget))
                {
                    State = SoldierState.Combat;
                }
                // 同阵营领袖在附近 → Follow（敌方单位不跟随玩家）
                else if (IsValidFollowLeader(self, _leader))
                {
                    if (HorizontalDist(self.Position, _leader.Position) <= RecruitRadius)
                        State = SoldierState.Follow;
                }
                break;

            case SoldierState.Follow:
                if (IsValidAttackTarget(self, self.TargetComp?.CurrentTarget))
                {
                    State = SoldierState.Combat;
                }
                // 领袖丢失或太远 → 回 Idle，忘掉领袖和组
                else if (!IsValidFollowLeader(self, _leader) ||
                         HorizontalDist(self.Position, _leader.Position) > LeashRange)
                {
                    self.MoveComp.StopMove(); // 清掉残留目标，防止被斥力推远
                    if (GroupMoveManager.HasInstance)
                    {
                        int selfId = (self as MAEntity)?.GetInstanceID() ?? self.GetHashCode();
                        GroupMoveManager.Instance.Coordinator.SetAgentGroup(selfId, -1);
                        GameDebugSettings.Log(DebugCategory.Brain,
                            $"[{self.CharacterKey}] 离开组, leader丢失或超距");
                    }
                    _leader = null;
                    _joinedGroup = false;
                    State = SoldierState.Idle;
                }
                break;

            case SoldierState.Combat:
                // 敌人死亡、不可被攻击或丢失后回 Idle，避免继续追踪幽灵/无效目标。
                var enemy = self.TargetComp?.CurrentTarget;
                if (!IsValidAttackTarget(self, enemy))
                {
                    // 立即清掉旧 NavMesh 目标，防止继续走向已死敌人
                    self.MoveComp.StopMove();
                    GameDebugSettings.Log(DebugCategory.Brain,
                        $"[{self.CharacterKey}] Combat→Idle: enemy={(enemy == null ? "null" : "invalid")}" +
                        $", leader={(_leader != null ? _leader.CharacterKey : "null")}" +
                        $", joinedGroup={_joinedGroup}");
                    State = SoldierState.Idle;
                }
                break;
        }
    }

    private static bool IsValidAttackTarget(IEntityContext self, IEntityContext target)
    {
        return target != null
               && target.IsAttackTargetable()
               && EntityCombatTeamHelper.IsEnemy(self, target);
    }

    private static bool IsValidFollowLeader(IEntityContext self, IEntityContext leader)
    {
        return leader != null
               && leader.Alive
               && self.Side == leader.Side;
    }

    private void SyncStateToCoordinator(IEntityContext self)
    {
        if (!GroupMoveManager.HasInstance || State == _lastSyncedState) return;
        GameDebugSettings.Log(DebugCategory.Brain,
            $"[{self.CharacterKey}] 状态切换 {_lastSyncedState} → {State}");
        _lastSyncedState = State;

        int selfId = (self as MAEntity)?.GetInstanceID() ?? self.GetHashCode();
        var coordState = State switch
        {
            SoldierState.Idle => GroupMoveCoordinator.AgentState.Idle,
            SoldierState.Follow => GroupMoveCoordinator.AgentState.Follow,
            SoldierState.Combat => GroupMoveCoordinator.AgentState.Combat,
            _ => GroupMoveCoordinator.AgentState.Idle
        };
        GroupMoveManager.Instance.Coordinator.SetAgentState(selfId, coordState);
    }

    private void TickIdle(IEntityContext self, float dt)
    {
        // Idle 状态：安静站着，只有真正重叠时才推开
        // 不每帧 MoveTo，避免抽搐
    }

    private void TickFollow(IEntityContext self, float dt)
    {
        if (!IsValidFollowLeader(self, _leader))
        {
            self.MoveComp.StopMove();
            return;
        }

        float speed = GetWorldMoveSpeed(self);
        Move = Vector2.zero;

        // 计算死区范围：[leaderEqR, leaderEqR + _deadZoneRange]
        float leaderEqR = GroupMoveManager.HasInstance
            ? GroupMoveManager.Instance.Coordinator.LeaderEquilibriumRadius
            : 1.5f;
        float deadZoneOuter = leaderEqR + _deadZoneRange;
        float distToLeader = HorizontalDist(self.Position, _leader.Position);

        float innerDeadZone = leaderEqR + _innerDeadZoneRange;

        if (distToLeader <= deadZoneOuter)
        {
            // 近死区内（<= innerDeadZone）：完全停下，只靠 LJ 力
            // 近死区到远死区之间：线性衰减，越近意愿越弱
            // t=1 在死区边缘（全速追），t=0 在近死区边缘（完全停）
            float t = distToLeader <= innerDeadZone ? 0f : Mathf.InverseLerp(innerDeadZone, deadZoneOuter, distToLeader);
            Vector3 toLeader = _leader.Position - self.Position;
            toLeader.y = 0f;
            Vector3 desiredVel = toLeader.normalized * speed * t;
            SubmitToCoordinator(self, desiredVel, speed);

            if (!_inDeadZone)
            {
                _inDeadZone = true;
                _deadZoneTarget = null;
                GameDebugSettings.Log(DebugCategory.Brain,
                    $"[{self.CharacterKey}] 进入死区 dist={distToLeader:F2} t={t:F2} deadZone=[{leaderEqR:F2},{deadZoneOuter:F2}]");
            }
        }
        else
        {
            // 在死区外：NavMesh 导航到 leader 附近
            _inDeadZone = false;

            // 没有目标点或目标点离 leader 太远（leader 移动了）→ 重新算
            if (!_deadZoneTarget.HasValue ||
                HorizontalDist(_deadZoneTarget.Value, _leader.Position) > deadZoneOuter)
            {
                _deadZoneTarget = PickRandomDeadZonePoint(_leader.Position, leaderEqR, deadZoneOuter);
                GameDebugSettings.Log(DebugCategory.Brain,
                    $"[{self.CharacterKey}] 生成死区目标点 {_deadZoneTarget.Value} dist={distToLeader:F2}");
            }

            self.MoveComp.MoveTo(_deadZoneTarget.Value);
            Vector3 navDir = self.MoveComp.GetNavDirection();
            Vector3 desiredVel = navDir * speed;
            SubmitToCoordinator(self, desiredVel, speed);
        }

        // Debug 画死区范围（只在第一个小兵上画，避免刷屏）
        if (_leader != null && _leader.Alive)
        {
            Vector3 lp = _leader.Position + Vector3.up * 0.1f;
            DrawCircle(lp, leaderEqR, Color.red);       // 斥力半径
            DrawCircle(lp, innerDeadZone, Color.yellow); // 近死区边缘
            DrawCircle(lp, deadZoneOuter, Color.green);  // 远死区边缘
        }
    }

    private static void DrawCircle(Vector3 center, float radius, Color color, int segments = 32)
    {
        float step = 2f * Mathf.PI / segments;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = step * i;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Debug.DrawLine(prev, next, color);
            prev = next;
        }
    }

    /// <summary>
    /// 实际攻击判定距离 = 武器攻击距离。
    /// 优先从 WeaponComp 读攻击距离，没有则用 WeaponRange 回退。
    /// </summary>
    private float GetEffectiveAttackRange(IEntityContext self)
    {
        float wpnRange = self.WeaponComp != null ? (float)self.WeaponComp.AttackRange : WeaponRange;
        return wpnRange;
    }

    private void TickCombat(IEntityContext self, float dt)
    {
        var enemy = self.TargetComp?.CurrentTarget;
        if (!IsValidAttackTarget(self, enemy))
        {
            self.MoveComp.StopMove();
            return;
        }

        float distToEnemy = self.DistanceToTargetSurface(enemy);
        float speed = GetWorldMoveSpeed(self);
        float effectiveRange = GetEffectiveAttackRange(self);

        if (distToEnemy <= effectiveRange)
        {
            // 在攻击范围内 → 攻击，提交零期望速度，协调器处理重叠推开
            Attack = true;
            Move = Vector2.zero;
            SubmitToCoordinator(self, Vector3.zero, speed);
        }
        else
        {
            // 只设导航目标（不设路径），让协调器回调控制实际移动
            self.MoveComp.SetNavTarget(enemy.Position);

            // 拿 NavMesh 方向作为期望速度提交给协调器
            Vector3 navDir = self.MoveComp.GetNavDirection();
            Vector3 desiredVel = navDir * speed;
            Move = Vector2.zero;
            SubmitToCoordinator(self, desiredVel, speed);
        }
    }

    /// <summary>
    /// 向协调器提交期望速度，回调中执行 MoveTo。
    /// 如果协调器不可用（测试环境），直接用期望速度。
    /// </summary>
    private void SubmitToCoordinator(IEntityContext self, Vector3 desiredVelocity, float speed)
    {
        if (!GroupMoveManager.HasInstance) return;
        var coordinator = GroupMoveManager.Instance.Coordinator;
        int agentId = (self as MAEntity)?.GetInstanceID() ?? self.GetHashCode();

        coordinator.SubmitDesiredVelocity(agentId, desiredVelocity, speed, safeVel =>
        {
            ApplyVelocity(self, safeVel, speed);
        });
    }

    private float GetWorldMoveSpeed(IEntityContext self)
    {
        Fix64 rawSpeed = self.GetProperty(CreatureMainProperty.Speed);
        return DistanceUnitConverter.ConvertToWorldFloat(rawSpeed);
    }

    private void ApplyVelocity(IEntityContext self, Vector3 velocity, float speed)
    {
        if (self.MoveComp == null || !self.CanRun(self.MoveComp))
            return;

        if (velocity.sqrMagnitude < 0.001f)
        {
            // 力为零，停止移动（清掉旧路径）
            self.MoveComp.StopMove();
            return;
        }

        Vector3 myPos = self.Position;
        Vector3 frameVelocity = Vector3.ClampMagnitude(velocity, speed);
        Vector3 target = myPos + frameVelocity;
        GameDebugSettings.Log(DebugCategory.Brain,
            $"[{self.CharacterKey}] ApplyVel state={State} vel={velocity} → target={target}" +
            $" leaderPos={(_leader != null ? _leader.Position.ToString() : "null")}");
        Move = Vector2.zero;
        self.MoveComp.MoveTo(target);
    }

    /// <summary>
    /// 在 leader 周围 [innerR, outerR] 环形区域内随机选一个点，
    /// 用 NavMesh.SamplePosition 确保在可行走区域上。
    /// </summary>
    private static Vector3 PickRandomDeadZonePoint(Vector3 leaderPos, float innerR, float outerR)
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float radius = Random.Range(innerR, outerR);
        Vector3 candidate = leaderPos + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

        if (UnityEngine.AI.NavMesh.SamplePosition(candidate, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
            return hit.position;

        return candidate;
    }

    private static float HorizontalDist(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
