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
    public float LeashRange = 15f;          // 脱离战斗回到跟随的距离

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
        if (_leader == null || !_leader.Alive)
            _leader = EntityRegistry.GetClosestLeader(self.Position);
        // 惰性标记领袖 + 设置组
        if (!_joinedGroup && _leader != null && _leader.Alive && GroupMoveManager.HasInstance)
        {
            int leaderId = (_leader as MAEntity)?.GetInstanceID() ?? _leader.GetHashCode();
            int selfId = (self as MAEntity)?.GetInstanceID() ?? self.GetHashCode();
            var coordinator = GroupMoveManager.Instance.Coordinator;
            coordinator.SetAgentLeader(leaderId, true);
            coordinator.SetAgentGroup(leaderId, leaderId); // 领袖自己也在组里
            coordinator.SetAgentGroup(selfId, leaderId);   // 自己加入领袖的组
            _joinedGroup = true;
            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.ReferenceId}] 加入组 groupId={leaderId}, leader={_leader.ReferenceId}");
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
                if (self.TargetComp?.CurrentTarget != null)
                {
                    State = SoldierState.Combat;
                }
                // 同阵营领袖在附近 → Follow（敌方单位不跟随玩家）
                else if (_leader != null && _leader.Alive && self.Side == _leader.Side)
                {
                    if (HorizontalDist(self.Position, _leader.Position) <= RecruitRadius)
                        State = SoldierState.Follow;
                }
                break;

            case SoldierState.Follow:
                if (self.TargetComp?.CurrentTarget != null)
                {
                    State = SoldierState.Combat;
                }
                // 领袖丢失或太远 → 回 Idle，忘掉领袖和组
                else if (_leader == null || !_leader.Alive ||
                         HorizontalDist(self.Position, _leader.Position) > LeashRange)
                {
                    if (GroupMoveManager.HasInstance)
                    {
                        int selfId = (self as MAEntity)?.GetInstanceID() ?? self.GetHashCode();
                        GroupMoveManager.Instance.Coordinator.SetAgentGroup(selfId, -1);
                        GameDebugSettings.Log(DebugCategory.Brain,
                            $"[{self.ReferenceId}] 离开组, leader丢失或超距");
                    }
                    _leader = null;
                    _joinedGroup = false;
                    State = SoldierState.Idle;
                }
                break;

            case SoldierState.Combat:
                // 敌人死了才回 Follow，不受领袖距离限制
                var enemy = self.TargetComp?.CurrentTarget;
                if (enemy == null)
                    State = SoldierState.Follow;
                break;
        }
    }

    private void SyncStateToCoordinator(IEntityContext self)
    {
        if (!GroupMoveManager.HasInstance || State == _lastSyncedState) return;
        GameDebugSettings.Log(DebugCategory.Brain,
            $"[{self.ReferenceId}] 状态切换 {_lastSyncedState} → {State}");
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
        if (_leader == null || !_leader.Alive) return;

        float speed = self.GetProperty(CreatureMainProperty.Speed);
        Move = Vector2.zero;

        // Follow 不需要自己算方向，提交零速度
        // 协调器的 LJ 吸引力会自然把 follower 拉向同组 agent 和领袖
        SubmitToCoordinator(self, Vector3.zero, speed);
    }

    /// <summary>
    /// 实际攻击判定距离 = 敌对斥力半径 + 武器攻击距离。
    /// 士兵停在斥力边界外，武器刚好够到敌人。
    /// </summary>
    private float GetEffectiveAttackRange()
    {
        float enemyEqR = GroupMoveManager.HasInstance
            ? GroupMoveManager.Instance.Coordinator.EnemyEquilibriumRadius
            : 1.5f;
        return enemyEqR + WeaponRange;
    }

    private void TickCombat(IEntityContext self, float dt)
    {
        Vector3 myPos = self.Position;

        var enemy = self.TargetComp?.CurrentTarget;
        if (enemy == null || !enemy.Alive) return;

        float distToEnemy = HorizontalDist(myPos, enemy.Position);
        float speed = self.GetProperty(CreatureMainProperty.Speed);
        float effectiveRange = GetEffectiveAttackRange();

        if (distToEnemy <= effectiveRange)
        {
            // 在攻击范围内 → 攻击，提交零期望速度，协调器处理重叠推开
            Attack = true;
            Move = Vector2.zero;
            SubmitToCoordinator(self, Vector3.zero, speed);
        }
        else
        {
            // 先让 NavMesh 算路径
            self.MoveComp.MoveTo(enemy.Position);

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

        coordinator.SubmitDesiredVelocity(agentId, desiredVelocity, safeVel =>
        {
            ApplyVelocity(self, safeVel, speed);
        });
    }

    private void ApplyVelocity(IEntityContext self, Vector3 velocity, float speed)
    {
        if (velocity.sqrMagnitude < 0.001f)
        {
            // 力为零，停止移动（清掉旧路径）
            self.MoveComp.StopMove();
            return;
        }

        Vector3 myPos = self.Position;
        Move = Vector2.zero;
        self.MoveComp.MoveTo(myPos + velocity.normalized * speed * 0.1f);
    }

    private static float HorizontalDist(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
