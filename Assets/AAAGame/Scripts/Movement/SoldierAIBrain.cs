using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 小兵 AI Brain：基于 Steering Behaviors 的流体移动。
///
/// 状态：
/// - Idle：站着不动，等待玩家靠近
/// - Follow：跟随玩家，自然散开在不同距离
/// - Combat：发现敌人后脱离跟随，散开交战（用 FlowField 寻路到敌人）
/// - Returning：敌方专属，被拉离出生点超过 ChaseRange 时强制返航。
///   挂 returning buff（移速 + 回血）；不可被打断；到家后清 buff 回 Idle。
///
/// 核心原则：
/// 1. 同阵营不阻拦同阵营（separation 力自动避让）
/// 2. 小兵永远绕开玩家（AvoidEntity 力更强）
/// 3. 交战时自然散开（separation + 趋敌 seek 混合）
/// </summary>
public class SoldierAIBrain : IControlBrain, ITickBrain, IBrainSideChangeHandler
{
    private const float CombatApproachRangeSlack = 0.08f;
    private const int CombatApproachCandidateCount = 16;
    private const int CombatApproachRingCount = 3;
    private const float CombatApproachRingSpacing = 0.55f;
    private const float CombatApproachOccupancyPadding = 0.35f;

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
    public float SoftReturnRatio = 0.6f;            // 软返航比例的 fallback（运行时优先读 GroupMoveConfig）
    private const string ReturningBuffId = "soldier_returning";

    // --- 状态 ---
    public SoldierState State { get; private set; } = SoldierState.Idle;

    // --- Brain 接口 ---
    public Vector2 Move { get; private set; }
    public bool Attack { get; private set; }
    public bool Skill1 => false;
    public bool Skill2 => false;
    public bool Skill3 => false;
    public bool Skill4 => false;
    public bool Skill5 => false;

    // --- 依赖 ---
    private IEntityContext _leader;

    // --- 内部 ---
    private Vector3 _desiredMoveDir;
    private bool _joinedGroup;
    private SoldierState _lastSyncedState = SoldierState.Idle;
    private bool _inDeadZone;                // 是否已进入 leader 附近的死区
    // 死区参数由 GroupMoveConfig 的 FollowBaseStopRadius / FollowDeadZoneRange / FollowInnerDeadZoneRange 提供
    // 协调器不在场（测试环境）时使用下面的兜底默认值
    private const float FallbackDeadZoneRange = 12f;
    private const float FallbackInnerDeadZoneRange = 2f;
    private Vector3? _deadZoneTarget;        // 死区内的随机导航目标点
    private Vector3? _birthPosition;         // 出生点（敌方专属，未设置则不启用脱战返航）
    private bool _softReturning;             // 软返航中：触发后一直走到 HomeArrivedRadius 才停
    private bool _allowEnemyReturnToBirth = true;
    private Vector3 _combatApproachPoint;
    private Vector3 _combatApproachTargetPoint;
    private int _combatApproachTargetId = int.MinValue;
    private int _combatApproachRefreshFrame = -1;
    private int _combatApproachDiagnosticFrame = -1;

    /// <summary>
    /// 领袖通过 EntityRegistry.GetClosestLeader 惰性获取。
    /// </summary>
    public void Inject() { }

    /// <summary>
    /// 敌方 SoldierEntity 在 OnShow 末尾调用，启用脱战返航逻辑。
    /// 友方/玩家不调用 → _birthPosition = null → 永不进入 Returning。
    /// </summary>
    public void SetBirthPosition(Vector3 worldPos) => _birthPosition = worldPos;

    public void SetReturnToBirthEnabled(bool enabled)
    {
        _allowEnemyReturnToBirth = enabled;
        if (_allowEnemyReturnToBirth)
            return;

        _birthPosition = null;
        _softReturning = false;
        if (State == SoldierState.Returning)
            State = SoldierState.Idle;
    }

    public void OnSideChanged(IEntityContext self, SideType oldSide, SideType newSide)
    {
        _leader = null;
        _joinedGroup = false;
        _inDeadZone = false;
        _deadZoneTarget = null;
        _softReturning = false;
        _combatApproachTargetId = int.MinValue;
        _combatApproachRefreshFrame = -1;
        _combatApproachTargetPoint = Vector3.zero;
        State = SoldierState.Idle;
        _lastSyncedState = SoldierState.Idle;

        if (self is MAEntity ma)
            ma.BuffComp?.RemoveBuff(ReturningBuffId);

        if (self.TargetComp != null)
        {
            self.TargetComp.CurrentTarget = null;
            self.TargetComp.ClearAggro();
        }

        self.MoveComp?.StopMove();

        _birthPosition = _allowEnemyReturnToBirth && newSide == SideType.EnemySide ? self.Position : null;

        if (GroupMoveManager.HasInstance)
        {
            int selfId = (self as MAEntity)?.GetInstanceID() ?? self.GetHashCode();
            GroupMoveManager.Instance.SetAgentGroup(selfId, -1);
            GroupMoveManager.Instance.SetAgentLeader(selfId, false);
            GroupMoveManager.Instance.SetAgentState(selfId, FlowFieldAgentState.Idle);
        }

        GameDebugSettings.Log(DebugCategory.Brain,
            $"[{self.CharacterKey}] Side changed {oldSide} -> {newSide}, reset SoldierAI state");
    }

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
            GroupMoveManager.Instance.SetAgentLeader(leaderId, true);
            GroupMoveManager.Instance.SetAgentGroup(leaderId, leaderId); // 领袖自己也在组里
            GroupMoveManager.Instance.SetAgentGroup(selfId, leaderId);   // 自己加入领袖的组
            _joinedGroup = true;
            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.CharacterKey}] 加入组 groupId={leaderId}, leader={_leader.CharacterKey}");
        }

        UpdateState(self);
        SyncStateToCoordinator(self);

        if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
        {
            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.CharacterKey}] Tick state={State} pos={self.Position} leader={_leader?.CharacterKey ?? "null"} " +
                $"leaderPos={(_leader != null ? _leader.Position.ToString() : "null")} " +
                $"target={self.TargetComp?.CurrentTarget?.CharacterKey ?? "null"}");
        }

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
                // 有同阵营领袖 → Follow（敌方单位不跟随玩家）
                else if (IsValidFollowLeader(self, _leader))
                {
                    State = SoldierState.Follow;
                }
                break;

            case SoldierState.Follow:
                if (IsValidAttackTarget(self, self.TargetComp?.CurrentTarget))
                {
                    State = SoldierState.Combat;
                }
                // 领袖失效 → 回 Idle，忘掉领袖和组
                else if (!IsValidFollowLeader(self, _leader))
                {
                    self.MoveComp.StopMove(); // 清掉残留目标，防止被斥力推远
                    if (GroupMoveManager.HasInstance)
                    {
                        int selfId = (self as MAEntity)?.GetInstanceID() ?? self.GetHashCode();
                        GroupMoveManager.Instance.SetAgentGroup(selfId, -1);
                        GameDebugSettings.Log(DebugCategory.Brain,
                            $"[{self.CharacterKey}] 离开组, leader失效");
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
                    // 立即清掉旧导航目标，防止继续走向已死敌人
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
        {
            self.TargetComp.CurrentTarget = null;
            self.TargetComp.ClearAggro();
        }
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
        // 返航途中可能被打 / scan 设了 CurrentTarget，回到家也清掉，避免刚到家又被拉走
        if (self.TargetComp != null)
        {
            self.TargetComp.CurrentTarget = null;
            self.TargetComp.ClearAggro();
        }
        State = SoldierState.Idle;
        GameDebugSettings.Log(DebugCategory.Brain,
            $"[{self.CharacterKey}] 到家, 退出 Returning");
    }

    private static bool IsValidAttackTarget(IEntityContext self, IEntityContext target)
    {
        return WeaponTargetRules.IsValidTargetForCurrentWeapon(self, target);
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
            $"[{self.CharacterKey}] 状态切换 {_lastSyncedState} → {State}, " +
            $"leader={_leader?.CharacterKey ?? "null"}, " +
            $"leaderDist={(_leader != null ? HorizontalDist(self.Position, _leader.Position).ToString("F2") : "n/a")}, " +
            $"target={self.TargetComp?.CurrentTarget?.CharacterKey ?? "null"}");
        _lastSyncedState = State;

        int selfId = (self as MAEntity)?.GetInstanceID() ?? self.GetHashCode();
        var coordState = State switch
        {
            SoldierState.Idle => FlowFieldAgentState.Idle,
            SoldierState.Follow => FlowFieldAgentState.Follow,
            SoldierState.Combat => FlowFieldAgentState.Combat,
            // Returning 当作 Combat：只受斥力，避免被同组拉走（敌方本来也不在玩家组）
            SoldierState.Returning => FlowFieldAgentState.Combat,
            _ => FlowFieldAgentState.Idle
        };
        GroupMoveManager.Instance.SetAgentState(selfId, coordState);
    }

    private void TickIdle(IEntityContext self, float dt)
    {
        // 敌方专属：远离出生点 + 周围无敌 → 温和走回家（不挂返航 buff，可被 UpdateState 切回 Combat）
        if (!_birthPosition.HasValue)
        {
            _softReturning = false;
            self.MoveComp?.StopMove();
            return;
        }

        float distFromHome = HorizontalDist(self.Position, _birthPosition.Value);

        // 已到家 → 停止
        if (distFromHome <= HomeArrivedRadius)
        {
            if (_softReturning)
            {
                _softReturning = false;
                self.MoveComp?.StopMove();
            }
            return;
        }

        // 周围有敌 → 终止软返航，让 UpdateState/TargetComp 切 Combat
        if (HasEnemyInScanRange(self))
        {
            if (_softReturning)
            {
                _softReturning = false;
                self.MoveComp?.StopMove();
            }
            return;
        }

        // 触发判定：尚未在软返航中，且未达 softThreshold → 站着等
        if (!_softReturning)
        {
            float softThreshold = ChaseRange * GetSoftReturnRatio();
            if (distFromHome < softThreshold) return;
            _softReturning = true;
            self.TargetComp?.ClearAggro(); // 软返航触发时也清掉受击仇恨，避免回家路上又被拉走
        }

        // 持续走回家直到 HomeArrivedRadius 才停
        GameDebugSettings.Log(DebugCategory.Brain,
            $"[{self.CharacterKey}] Idle soft-return MoveTo birth={_birthPosition.Value} from={self.Position} distFromHome={distFromHome:F2}");
        self.MoveComp.MoveTo(_birthPosition.Value);
    }

    private float GetSoftReturnRatio()
    {
        var cfg = GroupMoveManager.HasInstance ? GroupMoveManager.Instance.Config : null;
        return cfg != null ? cfg.EnemySoftReturnRatio : SoftReturnRatio;
    }

    private bool HasEnemyInScanRange(IEntityContext self)
    {
        float r = DetectEnemyRange;
        float rSq = r * r;
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            var ent = all[i];
            if (ent == null || ReferenceEquals(ent, self)) continue;
            if (!IsValidAttackTarget(self, ent)) continue;
            Vector3 d = ent.Position - self.Position;
            d.y = 0f;
            if (d.sqrMagnitude <= rSq) return true;
        }
        return false;
    }

    private void TickFollow(IEntityContext self, float dt)
    {
        if (!IsValidFollowLeader(self, _leader))
        {
            self.MoveComp.StopMove();
            return;
        }

        Move = Vector2.zero;
        float leaderEqR;
        float deadZoneRange;
        float innerDeadZoneRange;
        if (GroupMoveManager.HasInstance)
        {
            var mgr = GroupMoveManager.Instance;
            var cfg = mgr.Config;
            leaderEqR = cfg != null ? cfg.FollowBaseStopRadius : 1.5f;
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
            if (!_inDeadZone)
            {
                _inDeadZone = true;
                _deadZoneTarget = null;
                GameDebugSettings.Log(DebugCategory.Brain,
                    $"[{self.CharacterKey}] 进入死区 dist={distToLeader:F2} deadZone=[{leaderEqR:F2},{deadZoneOuter:F2}]");
            }

            self.MoveComp.StopMove();
        }
        else
        {
            _inDeadZone = false;

            if (!_deadZoneTarget.HasValue ||
                HorizontalDist(_deadZoneTarget.Value, _leader.Position) > deadZoneOuter)
            {
                _deadZoneTarget = PickRandomDeadZonePoint(self, _leader.Position, leaderEqR, deadZoneOuter);
                GameDebugSettings.Log(DebugCategory.Brain,
                    $"[{self.CharacterKey}] 生成死区目标点 {_deadZoneTarget.Value} dist={distToLeader:F2}");
            }

            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.CharacterKey}] Follow MoveTo deadZoneTarget={_deadZoneTarget.Value} leader={_leader.CharacterKey} leaderPos={_leader.Position}");
            self.MoveComp.MoveTo(_deadZoneTarget.Value);
        }

        if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
        {
            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.CharacterKey}] Follow dist={distToLeader:F2} deadZoneOuter={deadZoneOuter:F2} " +
                $"inDeadZone={_inDeadZone} target={(_deadZoneTarget.HasValue ? _deadZoneTarget.Value.ToString() : "null")}");
        }

        if (_leader != null && _leader.Alive)
        {
            Vector3 lp = _leader.Position + Vector3.up * 0.1f;
            DrawCircle(lp, leaderEqR, Color.red);
            DrawCircle(lp, innerDeadZone, Color.yellow);
            DrawCircle(lp, deadZoneOuter, Color.green);
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
        float effectiveRange = GetEffectiveAttackRange(self);
        float selfRadius = DistanceUnitConverter.ConvertToWorldFloat(self.GetProperty(CreatureMainProperty.CollisionRadius));
        float arriveDistance = ResolveNavigationArriveDistance(selfRadius);
        bool shouldAttackNow = distToEnemy <= effectiveRange;
        if (shouldAttackNow)
        {
            // 在攻击范围内 → 攻击并停下
            Attack = true;
            Move = Vector2.zero;
            self.MoveComp.StopMove();
        }
        else
        {
            Move = Vector2.zero;
            if (!TryResolveCombatApproachPoint(
                    self,
                    out Vector3 reachableApproachPoint,
                    out string reachFailure,
                    out FlowFieldCrowdMovementSystem.NavigationQueryFailureKind failureKind))
            {
                if (failureKind == FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.PendingRuntimeUpdate)
                {
                    self.MoveComp.StopMove();
                    return;
                }

                throw new System.InvalidOperationException(
                    $"[{self.CharacterKey}] Combat approach point unreachable enemy={enemy.CharacterKey} enemyPos={enemy.Position} " +
                    $"selfPos={self.Position} dist={distToEnemy:F2} range={effectiveRange:F2} reason={reachFailure}");
            }

            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.CharacterKey}] Combat MoveTo enemy={enemy.CharacterKey} enemyPos={enemy.Position} approach={reachableApproachPoint} " +
                $"selfPos={self.Position} dist={distToEnemy:F2} range={effectiveRange:F2} " +
                $"attackNow={shouldAttackNow}");
            self.MoveComp.MoveTo(reachableApproachPoint);
        }
    }

    private bool TryResolveCombatApproachPoint(
        IEntityContext self,
        out Vector3 approachPoint,
        out string failureReason,
        out FlowFieldCrowdMovementSystem.NavigationQueryFailureKind failureKind)
    {
        approachPoint = Vector3.zero;
        failureReason = string.Empty;
        failureKind = FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.None;
        var enemy = self.TargetComp?.CurrentTarget;
        if (!IsValidAttackTarget(self, enemy))
        {
            failureKind = FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.Unavailable;
            failureReason = "target invalid";
            return false;
        }

        float effectiveRange = GetEffectiveAttackRange(self);
        float selfRadius = DistanceUnitConverter.ConvertToWorldFloat(self.GetProperty(CreatureMainProperty.CollisionRadius));
        float arriveDistance = ResolveNavigationArriveDistance(selfRadius);
        float targetRadius = ResolveCombatTargetRadius(enemy);
        bool useSurfacePoint = enemy is BuildingEntity;
        float standOff = ResolveCombatApproachStandOff(selfRadius, targetRadius, effectiveRange, arriveDistance, useSurfacePoint);
        float minimumStandOff = ResolveCombatApproachMinimumStandOff(selfRadius, targetRadius, useSurfacePoint);
        float requiredClearance = Mathf.Max(selfRadius * 2f + CombatApproachOccupancyPadding, 0.45f);
        Vector3 targetPoint = enemy.Position;
        if (useSurfacePoint && enemy.TryGetTargetClosestPoint(self.Position, out Vector3 surfacePoint))
            targetPoint = surfacePoint;

        int targetId = ResolveCombatEntityId(enemy);
        int frame = FlowFieldCrowdMovementSystem.GetCurrentNavigationFrame();
        float minRefreshDistance = Mathf.Max(0.25f, selfRadius * 1.5f);
        float targetPointMoveDistance = HorizontalDist(_combatApproachTargetPoint, targetPoint);
        float targetPointRefreshDistance = Mathf.Max(0.12f, selfRadius * 0.5f);
        bool targetMatchesCache = _combatApproachTargetId == targetId;
        bool cacheFresh = frame - _combatApproachRefreshFrame < 10;
        bool targetStable = targetPointMoveDistance <= targetPointRefreshDistance;
        float cachedApproachToEnemy = ResolveCombatApproachDistanceToTargetSurface(enemy, _combatApproachPoint);
        bool cachedApproachInRange = cachedApproachToEnemy <= effectiveRange;
        float selfToCachedApproach = HorizontalDist(self.Position, _combatApproachPoint);
        bool selfNeedsCachedApproach = selfToCachedApproach > minRefreshDistance;
        bool cachedPointClear = IsCombatApproachPointNavigationClear(_combatApproachPoint, selfRadius);
        bool canReuseCachedApproach = targetMatchesCache
            && cacheFresh
            && cachedApproachInRange
            && selfNeedsCachedApproach
            && cachedPointClear;
        bool cachedReserved = false;
        int cachedBlockingAgentId = 0;
        if (canReuseCachedApproach)
            cachedReserved = TryReserveCombatApproachPoint(self, targetId, _combatApproachPoint, selfRadius, out cachedBlockingAgentId);

        if (canReuseCachedApproach && cachedReserved)
        {
            approachPoint = _combatApproachPoint;
            return true;
        }

        LogCombatApproachCacheMiss(
            self,
            enemy,
            frame,
            targetId,
            targetPoint,
            targetPointMoveDistance,
            targetPointRefreshDistance,
            targetMatchesCache,
            cacheFresh,
            targetStable,
            cachedApproachToEnemy,
            cachedApproachInRange,
            selfToCachedApproach,
            minRefreshDistance,
            selfNeedsCachedApproach,
            cachedPointClear,
            canReuseCachedApproach,
            cachedReserved,
            cachedBlockingAgentId);

        if (!FlowFieldCrowdMovementSystem.TryResolveCombatApproachPoint(
                self,
                enemy,
                targetPoint,
                standOff,
                minimumStandOff,
                CombatApproachRingSpacing,
                CombatApproachRingCount,
                CombatApproachCandidateCount,
                requiredClearance,
                out _combatApproachPoint,
                out failureReason,
                out failureKind))
        {
            return false;
        }

        _combatApproachTargetId = targetId;
        _combatApproachRefreshFrame = frame;
        _combatApproachTargetPoint = targetPoint;
        if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
        {
            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.CharacterKey}] Combat approach refresh enemy={enemy.CharacterKey} selfPos={self.Position} enemyPos={enemy.Position} " +
                $"approach={_combatApproachPoint} frame={frame}");
        }
        approachPoint = _combatApproachPoint;
        return true;
    }

    private void LogCombatApproachCacheMiss(
        IEntityContext self,
        IEntityContext enemy,
        int frame,
        int targetId,
        Vector3 targetPoint,
        float targetPointMoveDistance,
        float targetPointRefreshDistance,
        bool targetMatchesCache,
        bool cacheFresh,
        bool targetStable,
        float cachedApproachToEnemy,
        bool cachedApproachInRange,
        float selfToCachedApproach,
        float minRefreshDistance,
        bool selfNeedsCachedApproach,
        bool cachedPointClear,
        bool canReuseCachedApproach,
        bool cachedReserved,
        int cachedBlockingAgentId)
    {
        if (!GameDebugSettings.IsEnabled(DebugCategory.Brain) && !GameDebugSettings.IsEnabled(DebugCategory.Move))
            return;
        if (_combatApproachDiagnosticFrame >= 0 && frame - _combatApproachDiagnosticFrame < 10)
            return;

        _combatApproachDiagnosticFrame = frame;
        int selfId = ResolveCombatEntityId(self);
        Debug.LogWarning(
            $"[FlowCombatApproachCacheMiss] frame={frame} self={self.CharacterKey} selfId={selfId} target={enemy.CharacterKey} targetId={targetId} " +
            $"selfPos={self.Position} targetPos={enemy.Position} targetPoint={targetPoint} cachedApproach={_combatApproachPoint} " +
            $"cacheTargetId={_combatApproachTargetId} cacheFrame={_combatApproachRefreshFrame} targetMatches={targetMatchesCache} cacheFresh={cacheFresh} " +
            $"targetMove={targetPointMoveDistance:F3}/{targetPointRefreshDistance:F3} targetStable={targetStable} " +
            $"approachToEnemy={cachedApproachToEnemy:F3} inRange={cachedApproachInRange} selfToApproach={selfToCachedApproach:F3}/{minRefreshDistance:F3} " +
            $"needsMove={selfNeedsCachedApproach} pointClear={cachedPointClear} canReuse={canReuseCachedApproach} reserved={cachedReserved} blockingId={cachedBlockingAgentId}");
    }

    private static bool TryReserveCombatApproachPoint(
        IEntityContext self,
        int targetId,
        Vector3 approachPoint,
        float selfRadius,
        out int blockingAgentId)
    {
        float requiredClearance = Mathf.Max(selfRadius * 2f + CombatApproachOccupancyPadding, 0.45f);
        return FlowFieldCrowdMovementSystem.TryReserveNavigationGoalIfAvailable(
            ResolveCombatEntityId(self),
            targetId,
            approachPoint,
            requiredClearance,
            out blockingAgentId);
    }

    private static bool IsCombatApproachPointNavigationClear(Vector3 approachPoint, float selfRadius)
    {
        return FlowFieldCrowdMovementSystem.TryGetNavigationPointClearance(
                   approachPoint,
                   Mathf.Max(selfRadius, 0.01f),
                   out bool isClear,
                   out _,
                   out _,
                   out _)
               && isClear;
    }

    private static float ResolveCombatTargetRadius(IEntityContext entity)
    {
        if (entity == null)
            return 0f;

        float radius = DistanceUnitConverter.ConvertToWorldFloat(entity.GetProperty(CreatureMainProperty.CollisionRadius));
        return Mathf.Max(0f, radius);
    }

    private static float ResolveCombatApproachStandOff(
        float selfRadius,
        float targetRadius,
        float effectiveRange,
        float arriveDistance,
        bool targetPointOnSurface)
    {
        float surfaceDistance = Mathf.Max(
            selfRadius + 0.05f,
            effectiveRange - arriveDistance - CombatApproachRangeSlack);
        return targetPointOnSurface ? surfaceDistance : targetRadius + surfaceDistance;
    }

    private static float ResolveCombatApproachMinimumStandOff(
        float selfRadius,
        float targetRadius,
        bool targetPointOnSurface)
    {
        float surfaceSeparation = Mathf.Max(0.05f, selfRadius + 0.05f);
        return targetPointOnSurface ? surfaceSeparation : targetRadius + surfaceSeparation;
    }

    private static float ResolveCombatApproachDistanceToTargetSurface(IEntityContext target, Vector3 point)
    {
        if (target == null)
            throw new System.InvalidOperationException("ResolveCombatApproachDistanceToTargetSurface failed: target is null.");

        if (target.TryGetTargetClosestPoint(point, out Vector3 closestPoint))
            return HorizontalDist(point, closestPoint);

        return Mathf.Max(0f, HorizontalDist(point, target.Position) - ResolveCombatTargetRadius(target));
    }


    private static int ResolveCombatEntityId(IEntityContext entity)
    {
        return entity is MAEntity maEntity ? maEntity.GetInstanceID() : entity.GetHashCode();
    }

    private static float ResolveNavigationArriveDistance(float collisionRadius)
    {
        if (collisionRadius > 0.0001f)
            return Mathf.Max(0.08f, collisionRadius * 0.6f);

        return 0.15f;
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

        // 返航期间持续清 _lastAttacker / CurrentTarget，
        // 避免被友军告警 / 受击通知重新设上，导致 ExitReturning 后立刻 fallback 又追出去
        if (self.TargetComp != null)
        {
            if (self.TargetComp.CurrentTarget != null) self.TargetComp.CurrentTarget = null;
            self.TargetComp.ClearAggro();
        }

        GameDebugSettings.Log(DebugCategory.Brain,
            $"[{self.CharacterKey}] Returning MoveTo birth={_birthPosition.Value} from={self.Position}");
        self.MoveComp.MoveTo(_birthPosition.Value);
    }

    private static Vector3 PickRandomDeadZonePoint(IEntityContext self, Vector3 leaderPos, float innerR, float outerR)
    {
        int agentTypeId = self is MAEntity ma ? ma.navAgentTypeID : MAEntity.UnknownNavAgentTypeId;
        if (agentTypeId == MAEntity.UnknownNavAgentTypeId)
            agentTypeId = 0;

        float sampleRadius = Mathf.Max(0.5f, outerR * 0.25f);
        const int maxAttempts = 24;

        System.Text.StringBuilder failure = new System.Text.StringBuilder(512);
        failure.Append($"[{self.CharacterKey}] 死区目标点生成失败 leaderPos={leaderPos} innerR={innerR:F2} outerR={outerR:F2} agentType={agentTypeId} flowSnapRadius={sampleRadius:F2} attempts=[");

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(innerR, outerR);
            Vector3 candidate = leaderPos + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            bool hit = FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
                candidate,
                agentTypeId,
                sampleRadius,
                0f,
                out Vector3 legalPoint);

            if (attempt > 0)
                failure.Append("; ");

            failure.Append($"#{attempt}:candidate={candidate} hit={hit}");
            if (hit)
            {
                failure.Append($" flow={legalPoint}");
                failure.Append("]");
                return legalPoint;
            }
        }

        failure.Append("]");
        throw new System.InvalidOperationException(failure.ToString());
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
