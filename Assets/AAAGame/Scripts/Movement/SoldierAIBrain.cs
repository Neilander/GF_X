using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 小兵 AI Brain：基于 Steering Behaviors 的流体移动。
///
/// 状态：
/// - Idle：站着不动，等待玩家靠近
/// - Follow：跟随玩家，自然散开在不同距离
/// - Combat：发现敌人后脱离跟随，散开交战（用 NavMesh 寻路到敌人）
/// - Returning：敌方专属，被拉离出生点超过 ChaseRange 时强制返航。
///   挂 returning buff（移速 + 回血）；不可被打断；到家后清 buff 回 Idle。
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
        Combat,
        Returning
    }

    // --- 配置参数 ---
    // RecruitRadius / LeashRange 运行时优先读 GroupMoveConfig (SO)；
    // SO 不可用（如 Editor 测试）时回落到下面的字段值。
    public float RecruitRadius = 8f;        // 玩家多近时开始跟随（fallback）
    public float LeashRange = 30f;          // 脱离战斗回到跟随的距离（fallback）
    public float FollowDistanceMin = 2.5f;  // 跟随最近距离（不贴太紧）
    public float FollowDistanceMax = 5f;    // 跟随最远距离（超过才追）
    public float WeaponRange = 1.5f;          // 武器本身的攻击距离（WeaponData.AttackRange * 0.01）
    public float DetectEnemyRange = 10f;    // 发现敌人的距离
    public float SeparationRadius = 1.5f;   // 同阵营分离半径
    public float SeparationWeight = 1.5f;   // 分离力权重
    public float AvoidPlayerRadius = 2.5f;  // 避让玩家半径
    public float AvoidPlayerStrength = 3f;  // 避让玩家力度
    public float SeekWeight = 0.6f;         // 趋向力权重

    // --- 脱战返航参数（仅敌方有效，调参先在这里改）---
    public float ChaseRange = 23f;                  // 距出生点超过此值就进入 Returning
    public float HomeArrivedRadius = 1.5f;          // 距出生点 < 此值视为到家
    public Fix64 ReturnSpeedBonusPercent = (Fix64)0.5f;        // 返航移速加成（50%）
    public Fix64 ReturnHpRegenPercentPerSec = (Fix64)0.2f;     // 返航回血（每秒最大血量的 20%）
    private const string ReturningBuffId = "soldier_returning";

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
    // 死区宽度由 GroupMoveManager.FollowDeadZoneRange / FollowInnerDeadZoneRange 提供
    // 协调器不在场（测试环境）时使用下面的兜底默认值
    private const float FallbackDeadZoneRange = 12f;
    private const float FallbackInnerDeadZoneRange = 2f;
    private Vector3? _deadZoneTarget;        // 死区内的随机导航目标点

    private Vector3? _birthPosition;         // 出生点（敌方专属，未设置则不启用脱战返航）

    /// <summary>
    /// 领袖通过 EntityRegistry.GetClosestLeader 惰性获取。
    /// </summary>
    public void Inject() { }

    /// <summary>
    /// 敌方 SoldierEntity 在 OnShow 末尾调用，启用脱战返航逻辑。
    /// 友方/玩家不调用 → _birthPosition = null → 永不进入 Returning。
    /// </summary>
    public void SetBirthPosition(Vector3 worldPos) => _birthPosition = worldPos;

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
            case SoldierState.Returning:
                TickReturning(self, dt);
                break;
        }
    }

    private void UpdateState(IEntityContext self)
    {
        // Returning 优先：一旦进入返航就锁死，直到回到出生点。不可被任何状态打断。
        if (State == SoldierState.Returning)
        {
            if (_birthPosition.HasValue &&
                HorizontalDist(self.Position, _birthPosition.Value) <= HomeArrivedRadius)
            {
                ExitReturning(self);
            }
            return;
        }

        // 仅敌方（已设置 _birthPosition）才检查脱战。Idle/Follow/Combat 都可被脱战打断。
        if (_birthPosition.HasValue &&
            HorizontalDist(self.Position, _birthPosition.Value) > ChaseRange)
        {
            EnterReturning(self);
            return;
        }

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
                    if (HorizontalDist(self.Position, _leader.Position) <= GetRecruitRadius())
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
                         HorizontalDist(self.Position, _leader.Position) > GetLeashRange())
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

    private void EnterReturning(IEntityContext self)
    {
        // 清掉攻击意图和路径，避免返航过程中残留目标干扰
        Attack = false;
        if (self.TargetComp != null)
            self.TargetComp.CurrentTarget = null;
        self.MoveComp?.StopMove();

        // 挂复合 buff：百分比移速 + 持续回血
        if (self is MAEntity ma && ma.BuffComp != null)
        {
            var modules = new List<BuffCallback>
            {
                new PercentMoveSpeedBonusBuff(ReturnSpeedBonusPercent),
                new HealOverTimeBuff(ReturnHpRegenPercentPerSec)
            };
            var buff = BuffData.Create(ReturningBuffId, 0f, true, 1, modules);
            bool added = ma.BuffComp.AddBuff(buff, ma);
            UnityEngine.Debug.Log($"[Returning.AddBuff] host={self.CharacterKey} added={added} " +
                                  $"buffCompType={ma.BuffComp.GetType().Name} " +
                                  $"speedPct={(float)ReturnSpeedBonusPercent} hpPct={(float)ReturnHpRegenPercentPerSec}");
        }
        else
        {
            UnityEngine.Debug.LogWarning($"[Returning] host={self.CharacterKey} buff 未挂载: " +
                                         $"isMA={(self is MAEntity)} buffComp={(self as MAEntity)?.BuffComp?.GetType().Name ?? "null"}");
        }

        State = SoldierState.Returning;
        GameDebugSettings.Log(DebugCategory.Brain,
            $"[{self.CharacterKey}] 进入 Returning, birth={_birthPosition.Value}, dist={HorizontalDist(self.Position, _birthPosition.Value):F2}");
    }

    private void ExitReturning(IEntityContext self)
    {
        if (self is MAEntity ma)
            ma.BuffComp?.RemoveBuff(ReturningBuffId);

        self.MoveComp?.StopMove();
        State = SoldierState.Idle;
        GameDebugSettings.Log(DebugCategory.Brain,
            $"[{self.CharacterKey}] 到家, 退出 Returning");
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
            // Returning 当作 Combat：只受斥力，避免被同组拉走（敌方本来也不在玩家组）
            SoldierState.Returning => GroupMoveCoordinator.AgentState.Combat,
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

        // 计算死区范围：[leaderEqR, leaderEqR + FollowDeadZoneRange]
        float leaderEqR;
        float deadZoneRange;
        float innerDeadZoneRange;
        if (GroupMoveManager.HasInstance)
        {
            var mgr = GroupMoveManager.Instance;
            leaderEqR = mgr.Coordinator.LeaderEquilibriumRadius;
            var cfg = mgr.Config;
            deadZoneRange = cfg != null ? cfg.FollowDeadZoneRange : FallbackDeadZoneRange;
            innerDeadZoneRange = cfg != null ? cfg.FollowInnerDeadZoneRange : FallbackInnerDeadZoneRange;
        }
        else
        {
            leaderEqR = 1.5f;
            deadZoneRange = FallbackDeadZoneRange;
            innerDeadZoneRange = FallbackInnerDeadZoneRange;
        }
        float deadZoneOuter = leaderEqR + deadZoneRange;
        float distToLeader = HorizontalDist(self.Position, _leader.Position);

        float innerDeadZone = leaderEqR + innerDeadZoneRange;

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

    private void TickReturning(IEntityContext self, float dt)
    {
        if (!_birthPosition.HasValue)
        {
            // 理论上不应该发生：Returning 是由 _birthPosition.HasValue 才能进入的
            State = SoldierState.Idle;
            return;
        }

        Move = Vector2.zero;
        Attack = false;

        float speed = GetWorldMoveSpeed(self);
        self.MoveComp.SetNavTarget(_birthPosition.Value);
        Vector3 navDir = self.MoveComp.GetNavDirection();
        Vector3 desiredVel = navDir * speed;
        SubmitToCoordinator(self, desiredVel, speed);
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

    private float GetRecruitRadius()
    {
        var cfg = GroupMoveManager.HasInstance ? GroupMoveManager.Instance.Config : null;
        return cfg != null ? cfg.FollowRecruitRadius : RecruitRadius;
    }

    private float GetLeashRange()
    {
        var cfg = GroupMoveManager.HasInstance ? GroupMoveManager.Instance.Config : null;
        return cfg != null ? cfg.FollowLeashRange : LeashRange;
    }
}
