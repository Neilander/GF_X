using System.Collections.Generic;
using UnityEngine;
using MainThreadFrameProfiler = UnityGameFramework.Runtime.MainThreadFrameProfiler;
using MainThreadPerfScope = UnityGameFramework.Runtime.MainThreadPerfScope;

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
public class SoldierAIBrain : IControlBrain, ITickBrain, IBrainSideChangeHandler, ILogicDeterministicStateContributor
{
    public const string DefendPursuitDistanceConfigKey = "DefendPursuitDistance";
    public const string DefendReturnMoveSpeedBonusConfigKey = "DefendReturnMoveSpeedBonus";
    public const string DefendReturnHealthRegenPercentPerSecondConfigKey = "DefendReturnHealthRegenPercentPerSecond";
    public const string DefendReturnDamageReductionPercentConfigKey = "DefendReturnDamageReductionPercent";
    private static readonly FixVector2[] StableDeadZoneDirections =
    {
        new FixVector2(Fix64.One, Fix64.Zero),
        new FixVector2(Fix64.FromRaw(3785), Fix64.FromRaw(1568)),
        new FixVector2(Fix64.FromRaw(2897), Fix64.FromRaw(2897)),
        new FixVector2(Fix64.FromRaw(1568), Fix64.FromRaw(3785)),
        new FixVector2(Fix64.Zero, Fix64.One),
        new FixVector2(Fix64.FromRaw(-1568), Fix64.FromRaw(3785)),
        new FixVector2(Fix64.FromRaw(-2897), Fix64.FromRaw(2897)),
        new FixVector2(Fix64.FromRaw(-3785), Fix64.FromRaw(1568)),
        new FixVector2(-Fix64.One, Fix64.Zero),
        new FixVector2(Fix64.FromRaw(-3785), Fix64.FromRaw(-1568)),
        new FixVector2(Fix64.FromRaw(-2897), Fix64.FromRaw(-2897)),
        new FixVector2(Fix64.FromRaw(-1568), Fix64.FromRaw(-3785)),
        new FixVector2(Fix64.Zero, -Fix64.One),
        new FixVector2(Fix64.FromRaw(1568), Fix64.FromRaw(-3785)),
        new FixVector2(Fix64.FromRaw(2897), Fix64.FromRaw(-2897)),
        new FixVector2(Fix64.FromRaw(3785), Fix64.FromRaw(-1568)),
    };

    private const int CombatApproachCandidateCount = 16;
    private const int CombatApproachRingCount = 3;
    private static readonly Fix64 CombatApproachRangeSlackFixed = Fix64.FromRaw(328);
    private static readonly Fix64 CombatApproachRingSpacingFixed = Fix64.FromRaw(2253);
    private static readonly Fix64 CombatApproachOccupancyPaddingFixed = Fix64.FromRaw(1434);

    public enum SoldierState
    {
        Idle,
        Follow,
        Combat,
        Returning
    }

    // --- 配置参数 ---
    public Fix64 DetectEnemyRange = (Fix64)10;

    // --- 脱战返航参数（仅敌方有效，调参先在这里改）---
    public Fix64 ChaseRange = (Fix64)23;                  // 距出生点超过此值就进入 Returning
    public Fix64 HomeArrivedRadius = Fix64.FromRaw(6144);         // 距出生点 < 此值视为到家
    public Fix64 ReturnSpeedBonus = (Fix64)250;                  // 返航固定移速加成
    public Fix64 ReturnHpRegenPercentPerSec = Fix64.FromRaw(820);     // 返航回血（每秒最大血量的 20%）
    public Fix64 ReturnDamageReductionPercent = Fix64.Zero;
    private const string ReturningBuffId = "soldier_returning";

    // --- 状态 ---
    public SoldierState State { get; private set; } = SoldierState.Idle;

    // --- Brain 接口 ---
    public Vector2 Move => Vector2.zero;
    public FixVector2 MoveFixed => FixVector2.Zero;
    public bool Attack { get; private set; }
    public bool Skill1 => false;
    public bool Skill2 => false;
    public bool Skill3 => false;
    public bool Skill4 => false;
    public bool Skill5 => false;

    // --- 依赖 ---
    private IEntityContext _leader;

    // --- 内部 ---
    private bool _inDeadZone;                // 是否已进入 leader 附近的死区
    // 死区参数由 GroupMoveConfig 的 FollowBaseStopRadius / FollowDeadZoneRange / FollowInnerDeadZoneRange 提供
    private FixVector2? _deadZoneTarget;        // 死区内的稳定定点导航目标点
    private FixVector2? _birthPosition;         // 出生点（敌方专属，未设置则不启用脱战返航）
    private bool _softReturning;             // 软返航中：触发后一直走到 HomeArrivedRadius 才停
    private bool _allowEnemyReturnToBirth = true;
    private static readonly ICapability ReturningTargetingLocker = new ReturningTargetingCapabilityLocker();
    private IEntityContext _returningTargetingOwner;
    private ITargetingComp _returningTargetingComp;
    private bool _awaitingTargetReplacement;
    private FixVector2 _combatApproachPoint;
    private FixVector2 _combatApproachTargetPoint;
    private int _combatApproachTargetId = int.MinValue;
    private int _combatApproachRefreshFrame = -1;
    private int _combatApproachDiagnosticFrame = -1;
    private FixVector2[] _defendRouteWaypointsFixed;
    private string[] _defendRouteWaypointTeleportationIds;
    private int _defendRouteWaypointIndex;
    private int _defendSpeedReleaseWaypointIndex = -1;
    private FixVector2? _defendSpeedReleasePositionFixed;
    private Fix64 _defendRouteWaypointArrivalRadius;
    private IEntityContext _defendRouteTarget;
    private bool _defendTargetRefreshPending;
    private bool _defendTargetEventSubscribed;
    private readonly List<IEntityContext> _enemyScanCandidates = new List<IEntityContext>(16);

    /// <summary>
    /// 领袖通过 EntityRegistry.GetClosestLeader 惰性获取。
    /// </summary>
    public void Inject() { }

    public void SetBirthPositionFixed(FixVector2 worldPos)
    {
        _birthPosition = worldPos;
    }

    public void ConfigureDefendRoute(
        FixVector2[] waypointsFixed,
        string[] waypointTeleportationIds,
        int speedReleaseWaypointIndex,
        FixVector2? speedReleasePositionFixed,
        Fix64 waypointArrivalRadius)
    {
        if (waypointsFixed == null || waypointTeleportationIds == null)
            throw new System.InvalidOperationException("SoldierAIBrain defend route requires waypoint positions and IDs.");
        if (waypointsFixed.Length != waypointTeleportationIds.Length)
            throw new System.InvalidOperationException("SoldierAIBrain defend route waypoint arrays are mismatched.");
        if (waypointArrivalRadius <= Fix64.Zero)
            throw new System.InvalidOperationException("SoldierAIBrain defend route waypoint arrival radius must be positive.");
        if (speedReleaseWaypointIndex < -1 || speedReleaseWaypointIndex >= waypointsFixed.Length)
            throw new System.InvalidOperationException("SoldierAIBrain defend speed release waypoint index is outside the route.");
        if (speedReleaseWaypointIndex >= 0 && speedReleasePositionFixed.HasValue)
            throw new System.InvalidOperationException("SoldierAIBrain defend speed release cannot use a waypoint and a position together.");

        _defendRouteWaypointsFixed = (FixVector2[])waypointsFixed.Clone();
        _defendRouteWaypointTeleportationIds = (string[])waypointTeleportationIds.Clone();
        _defendRouteWaypointIndex = 0;
        _defendSpeedReleaseWaypointIndex = speedReleaseWaypointIndex;
        _defendSpeedReleasePositionFixed = speedReleasePositionFixed;
        _defendRouteWaypointArrivalRadius = waypointArrivalRadius;
        SubscribeDefendTargetEvents();
        _defendTargetRefreshPending = true;
    }

    public void ConfigureReturnFromGameConfig()
    {
        ChaseRange = LogicFactionVisionService.ReadWorldDistance(DefendPursuitDistanceConfigKey);
        ReturnSpeedBonus = LogicFactionVisionService.ReadPositiveConfig(
            DefendReturnMoveSpeedBonusConfigKey);
        ReturnHpRegenPercentPerSec = LogicFactionVisionService.ReadPositiveConfig(
            DefendReturnHealthRegenPercentPerSecondConfigKey) / (Fix64)100;
        ReturnDamageReductionPercent = FixedConfigReader.ReadRequiredFixedConfig(
            DefendReturnDamageReductionPercentConfigKey);
        if (ReturnDamageReductionPercent < Fix64.Zero || ReturnDamageReductionPercent > (Fix64)100)
        {
            throw new System.InvalidOperationException(
                $"Game config '{DefendReturnDamageReductionPercentConfigKey}' must be in [0, 100].");
        }
    }

    public void SetReturnToBirthEnabled(bool enabled)
    {
        _allowEnemyReturnToBirth = enabled;
        if (_allowEnemyReturnToBirth)
            return;

        _birthPosition = null;
        _softReturning = false;
        _awaitingTargetReplacement = false;
        if (State == SoldierState.Returning)
        {
            IEntityContext returningOwner = _returningTargetingOwner
                ?? throw new System.InvalidOperationException(
                    "SoldierAIBrain returning state has no targeting lock owner.");
            ReleaseReturningTargetingLock(returningOwner);
            returningOwner.BuffComp?.RemoveBuff(ReturningBuffId);
            returningOwner.MoveComp?.StopMove();
            State = SoldierState.Idle;
        }
    }

    public void OnSideChanged(IEntityContext self, SideType oldSide, SideType newSide)
    {
        ReleaseReturningTargetingLock(self);
        _leader = null;
        _inDeadZone = false;
        _deadZoneTarget = null;
        _softReturning = false;
        _awaitingTargetReplacement = false;
        _combatApproachTargetId = int.MinValue;
        _combatApproachRefreshFrame = -1;
        _combatApproachTargetPoint = FixVector2.Zero;
        _defendRouteWaypointsFixed = null;
        _defendRouteWaypointTeleportationIds = null;
        _defendRouteWaypointIndex = 0;
        _defendSpeedReleaseWaypointIndex = -1;
        _defendSpeedReleasePositionFixed = null;
        _defendRouteWaypointArrivalRadius = Fix64.Zero;
        _defendRouteTarget = null;
        _defendTargetRefreshPending = false;
        UnsubscribeDefendTargetEvents();
        State = SoldierState.Idle;

        self.BuffComp?.RemoveBuff(ReturningBuffId);

        if (self.TargetComp != null)
        {
            self.TargetComp.CurrentTarget = null;
            self.TargetComp.ClearAggro();
        }

        self.MoveComp?.StopMove();

        _birthPosition = _allowEnemyReturnToBirth && newSide == SideType.EnemySide ? self.LogicFramePositionFixed() : null;

        if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
        {
            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.CharacterKey}] Side changed {oldSide} -> {newSide}, reset SoldierAI state");
        }
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new System.ArgumentNullException(nameof(hasher));
        hasher.Add((int)State);
        hasher.Add(GetLogicId(_leader));
        hasher.Add(_inDeadZone);
        AddOptionalPosition(hasher, _deadZoneTarget);
        AddOptionalPosition(hasher, _birthPosition);
        hasher.Add(_softReturning);
        hasher.Add(_allowEnemyReturnToBirth);
        hasher.Add(_awaitingTargetReplacement);
        hasher.Add(_combatApproachPoint.x.RawValue);
        hasher.Add(_combatApproachPoint.y.RawValue);
        hasher.Add(_combatApproachTargetPoint.x.RawValue);
        hasher.Add(_combatApproachTargetPoint.y.RawValue);
        hasher.Add(_combatApproachTargetId);
        hasher.Add(_combatApproachRefreshFrame);
        hasher.Add(_defendRouteWaypointIndex);
        hasher.Add(_defendSpeedReleaseWaypointIndex);
        AddOptionalPosition(hasher, _defendSpeedReleasePositionFixed);
        hasher.Add(_defendRouteWaypointArrivalRadius.RawValue);
        int routeCount = _defendRouteWaypointsFixed?.Length ?? 0;
        hasher.Add(routeCount);
        for (int i = 0; i < routeCount; i++)
        {
            hasher.Add(_defendRouteWaypointsFixed[i].x.RawValue);
            hasher.Add(_defendRouteWaypointsFixed[i].y.RawValue);
            hasher.Add(_defendRouteWaypointTeleportationIds[i]);
        }
    }

    private static int GetLogicId(IEntityContext entity)
    {
        return entity != null && entity.LogicEntityId.IsValid ? entity.LogicEntityId.Value : 0;
    }

    private static void AddOptionalPosition(LogicStateHasher hasher, FixVector2? position)
    {
        hasher.Add(position.HasValue);
        if (!position.HasValue)
            return;
        hasher.Add(position.Value.x.RawValue);
        hasher.Add(position.Value.y.RawValue);
    }

    public void Tick(IEntityContext self, Fix64 dt)
    {
        Attack = false;
        if (!self.Alive) return;

        // 惰性刷新领袖
        if (!IsValidFollowLeader(self, _leader))
            _leader = EntityRegistry.Player;
        UpdateState(self);

        if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
        {
            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.CharacterKey}] Tick state={State} pos={self.LogicFramePosition()} leader={_leader?.CharacterKey ?? "null"} " +
                $"leaderPos={(_leader != null ? _leader.LogicFramePosition().ToString() : "null")} " +
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
                {
                    long combatStartTicks = MainThreadFrameProfiler.LoggingEnabled
                        ? System.Diagnostics.Stopwatch.GetTimestamp()
                        : 0L;
                    TickCombat(self, dt);
                    if (MainThreadFrameProfiler.LoggingEnabled)
                    {
                        MainThreadFrameProfiler.Record(
                            MainThreadPerfScope.EntityBrainCombat,
                            System.Diagnostics.Stopwatch.GetTimestamp() - combatStartTicks);
                    }
                }
                break;
            case SoldierState.Returning:
                TickReturning(self, dt);
                break;
        }
    }

    private void UpdateState(IEntityContext self)
    {
        FixVector2 selfPositionFixed = self.LogicFramePositionFixed();
        // Returning 优先：一旦进入返航就锁死，直到回到出生点。不可被任何状态打断。
        if (State == SoldierState.Returning)
        {
            if (!_birthPosition.HasValue)
                throw new System.InvalidOperationException(
                    $"SoldierAIBrain returning state has no birth position. entity={self.LogicEntityId.Value}.");
            if (FixVector2.Distance(selfPositionFixed, _birthPosition.Value) <= HomeArrivedRadius)
            {
                ExitReturning(self);
            }
            return;
        }

        // 仅敌方（已设置 _birthPosition）才检查脱战。Idle/Follow/Combat 都可被脱战打断。
        if (_birthPosition.HasValue &&
            FixVector2.Distance(selfPositionFixed, _birthPosition.Value) > ChaseRange)
        {
            EnterReturning(self);
            return;
        }

        switch (State)
        {
            case SoldierState.Idle:
                // 有敌人 → 直接进 Combat
                if (HasCombatPursuit(self))
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
                if (HasCombatPursuit(self))
                {
                    State = SoldierState.Combat;
                }
                // 领袖失效 → 回 Idle，忘掉领袖和组
                else if (!IsValidFollowLeader(self, _leader))
                {
                    self.MoveComp.StopMove(); // 清掉残留目标，防止被斥力推远
                    if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
                    {
                        GameDebugSettings.Log(DebugCategory.Brain,
                            $"[{self.CharacterKey}] 离开跟随, leader失效");
                    }
                    _leader = null;
                    State = SoldierState.Idle;
                }
                break;

            case SoldierState.Combat:
                // 目标失效后先等待 Targeting 尝试替换；确认脱战后敌兵返航，友军重新跟随领袖。
                var enemy = self.TargetComp?.CurrentTarget;
                if (IsValidAttackTarget(self, enemy))
                {
                    _awaitingTargetReplacement = false;
                    break;
                }
                if (self.TargetComp is ILastSeenTargetingComp lastSeen && lastSeen.HasLastSeenPursuit)
                {
                    _awaitingTargetReplacement = false;
                    break;
                }

                // Brain 先于 Targeting 执行。仍引用着失效目标时，先让紧随其后的
                // Targeting 阶段完成一次事件驱动重扫，再决定是否真的失去目标。
                if (!_awaitingTargetReplacement && enemy != null)
                {
                    _awaitingTargetReplacement = true;
                    self.MoveComp.StopMove();
                    if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
                    {
                        GameDebugSettings.Log(DebugCategory.Brain,
                            $"[{self.CharacterKey}] Combat target invalid, awaiting Targeting replacement: enemy={enemy.CharacterKey}");
                    }
                    break;
                }

                _awaitingTargetReplacement = false;
                // 立即清掉旧导航目标，防止继续走向已死敌人
                self.MoveComp.StopMove();
                if (self.TargetComp != null)
                {
                    self.TargetComp.CurrentTarget = null;
                    self.TargetComp.ClearAggro();
                }
                bool shouldReturnHome = _birthPosition.HasValue
                                        && FixVector2.Distance(selfPositionFixed, _birthPosition.Value) > HomeArrivedRadius;
                if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
                {
                    GameDebugSettings.Log(DebugCategory.Brain,
                        $"[{self.CharacterKey}] Combat target lost: enemy={(enemy == null ? "null" : "invalid")}" +
                        $", returnHome={shouldReturnHome}, leader={(_leader != null ? _leader.CharacterKey : "null")}");
                }
                if (shouldReturnHome)
                    EnterReturning(self);
                else if (IsValidFollowLeader(self, _leader))
                    State = SoldierState.Follow;
                else
                    State = SoldierState.Idle;
                break;
        }
    }

    private void EnterReturning(IEntityContext self)
    {
        ITargetingComp targetingComp = self.TargetComp
            ?? throw new System.InvalidOperationException(
                $"SoldierAIBrain cannot enter returning without a targeting component. entity={self.LogicEntityId.Value}.");
        var buffComp = self.BuffComp
            ?? throw new System.InvalidOperationException(
                $"SoldierAIBrain cannot enter returning without a buff component. entity={self.LogicEntityId.Value}.");

        var modules = new List<BuffCallback>
        {
            new RevertibleMoveSpeedBonusBuff(ReturnSpeedBonus),
            new HealOverTimeBuff(ReturnHpRegenPercentPerSec),
            new PercentIncomingDamageReductionBuff(ReturnDamageReductionPercent)
        };
        var buff = BuffData.Create(ReturningBuffId, Fix64.Zero, true, 1, modules);
        if (!buffComp.AddBuff(buff, self))
        {
            throw new System.InvalidOperationException(
                $"SoldierAIBrain failed to add the required returning buff. entity={self.LogicEntityId.Value}.");
        }

        // 清掉攻击意图和路径，避免返航过程中残留目标干扰
        Attack = false;
        _awaitingTargetReplacement = false;
        targetingComp.CurrentTarget = null;
        targetingComp.ClearAggro();
        self.MoveComp?.StopMove();

        self.LockComp(targetingComp, ReturningTargetingLocker);
        _returningTargetingOwner = self;
        _returningTargetingComp = targetingComp;

        State = SoldierState.Returning;
        if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
        {
            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.CharacterKey}] 进入 Returning, birth={_birthPosition.Value}, dist={(float)FixVector2.Distance(self.LogicFramePositionFixed(), _birthPosition.Value):F2}");
        }
    }

    private void ExitReturning(IEntityContext self)
    {
        ReleaseReturningTargetingLock(self);
        self.BuffComp?.RemoveBuff(ReturningBuffId);

        self.MoveComp?.StopMove();
        // 返航途中可能被打 / scan 设了 CurrentTarget，回到家也清掉，避免刚到家又被拉走
        if (self.TargetComp != null)
        {
            self.TargetComp.CurrentTarget = null;
            self.TargetComp.ClearAggro();
        }
        State = SoldierState.Idle;
        if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
        {
            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.CharacterKey}] 到家, 退出 Returning");
        }
    }

    private void ReleaseReturningTargetingLock(IEntityContext self)
    {
        if (_returningTargetingComp == null)
            return;
        if (!ReferenceEquals(_returningTargetingOwner, self))
            throw new System.InvalidOperationException(
                "SoldierAIBrain returning targeting lock owner does not match the current entity.");

        self.ResumeComp(_returningTargetingComp, ReturningTargetingLocker);
        _returningTargetingOwner = null;
        _returningTargetingComp = null;
    }

    private static bool IsValidAttackTarget(IEntityContext self, IEntityContext target)
    {
        return WeaponTargetRules.IsValidTargetForCurrentWeapon(self, target);
    }

    private static bool HasCombatPursuit(IEntityContext self)
    {
        return IsValidAttackTarget(self, self.TargetComp?.CurrentTarget)
               || self.TargetComp is ILastSeenTargetingComp lastSeen && lastSeen.HasLastSeenPursuit;
    }

    private static bool IsValidFollowLeader(IEntityContext self, IEntityContext leader)
    {
        return leader != null
               && leader.Alive
               && self.Side == leader.Side;
    }

    private void TickIdle(IEntityContext self, Fix64 dt)
    {
        if (_defendRouteWaypointsFixed != null)
        {
            AdvanceDefendRouteWaypoint(self);
            if (_defendRouteWaypointIndex < _defendRouteWaypointsFixed.Length)
            {
                self.MoveComp.MoveToFixed(_defendRouteWaypointsFixed[_defendRouteWaypointIndex]);
            }
            else
            {
                if (_defendRouteTarget != null && !_defendRouteTarget.Alive)
                    _defendTargetRefreshPending = true;
                if (_defendTargetRefreshPending)
                    RefreshDefendTarget(self);
                if (_defendRouteTarget != null)
                    self.MoveComp.MoveToFixed(_defendRouteTarget.PositionFixed);
                else
                    self.MoveComp.StopMove();
            }
            return;
        }

        // 敌方专属：远离出生点 + 周围无敌 → 温和走回家（不挂返航 buff，可被 UpdateState 切回 Combat）
        if (!_birthPosition.HasValue)
        {
            _softReturning = false;
            self.MoveComp?.StopMove();
            return;
        }

        Fix64 distFromHome = FixVector2.Distance(self.LogicFramePositionFixed(), _birthPosition.Value);

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
            Fix64 softThreshold = ChaseRange * GetSoftReturnRatio();
            if (distFromHome < softThreshold) return;
            _softReturning = true;
            self.TargetComp?.ClearAggro(); // 软返航触发时也清掉受击仇恨，避免回家路上又被拉走
        }

        // 持续走回家直到 HomeArrivedRadius 才停
        if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
        {
            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.CharacterKey}] Idle soft-return MoveTo birth={_birthPosition.Value} from={self.LogicFramePositionFixed()} distFromHome={(float)distFromHome:F2}");
        }
        self.MoveComp.MoveToFixed(_birthPosition.Value);
    }

    private void AdvanceDefendRouteWaypoint(IEntityContext self)
    {
        if (_defendSpeedReleasePositionFixed.HasValue
            && FixVector2.Distance(
                self.LogicFramePositionFixed(),
                _defendSpeedReleasePositionFixed.Value) <= _defendRouteWaypointArrivalRadius)
        {
            DefendPhaseRuntime.NotifyDefendEnemyReachedSpeedReleaseTarget(self.LogicEntityId);
            _defendSpeedReleasePositionFixed = null;
        }
        while (_defendRouteWaypointIndex < _defendRouteWaypointsFixed.Length)
        {
            if (FixVector2.Distance(
                    self.LogicFramePositionFixed(),
                    _defendRouteWaypointsFixed[_defendRouteWaypointIndex]) > _defendRouteWaypointArrivalRadius)
                return;

            int reachedWaypointIndex = _defendRouteWaypointIndex;
            if (reachedWaypointIndex == _defendSpeedReleaseWaypointIndex)
            {
                DefendPhaseRuntime.NotifyDefendEnemyReachedSpeedReleaseWaypoint(
                    self.LogicEntityId,
                    _defendRouteWaypointTeleportationIds[reachedWaypointIndex]);
                _defendSpeedReleaseWaypointIndex = -1;
            }
            _defendRouteWaypointIndex++;
        }
    }

    private void RefreshDefendTarget(IEntityContext self)
    {
        if (self.TargetComp is not IDefendTargetingModeComp defendTargeting)
            throw new System.InvalidOperationException(
                $"SoldierAIBrain defend route requires defend targeting capability. entity={self.LogicEntityId.Value}.");

        if (!LogicGameEndService.TryGetNearestPlayerConditionBuilding(
                self.LogicFramePositionFixed(),
                out IBuildingLogicContext target))
        {
            _defendRouteTarget = null;
            defendTargeting.UseDefendEnemyMode(null);
            _defendTargetRefreshPending = false;
            return;
        }

        _defendRouteTarget = target;
        defendTargeting.UseDefendEnemyMode(target);
        _defendTargetRefreshPending = false;
    }

    private void SubscribeDefendTargetEvents()
    {
        if (_defendTargetEventSubscribed)
            return;
        LogicGameEndService.PlayerConditionTargetsChanged += HandleDefendTargetChanged;
        _defendTargetEventSubscribed = true;
    }

    private void UnsubscribeDefendTargetEvents()
    {
        if (!_defendTargetEventSubscribed)
            return;
        LogicGameEndService.PlayerConditionTargetsChanged -= HandleDefendTargetChanged;
        _defendTargetEventSubscribed = false;
    }

    private void HandleDefendTargetChanged()
    {
        _defendTargetRefreshPending = true;
    }

    private Fix64 GetSoftReturnRatio()
    {
        if (!GroupMoveManager.HasInstance)
            throw new System.InvalidOperationException("SoldierAIBrain requires GroupMoveManager for soft-return configuration.");
        return GroupMoveManager.Instance.EnemySoftReturnRatioFixed;
    }

    private bool HasEnemyInScanRange(IEntityContext self)
    {
        return LogicTargetingSpatialIndexService.HasEnemyInRange(
            self,
            DetectEnemyRange,
            _enemyScanCandidates);
    }

    private void TickFollow(IEntityContext self, Fix64 dt)
    {
        if (!IsValidFollowLeader(self, _leader))
        {
            self.MoveComp.StopMove();
            return;
        }

        if (!GroupMoveManager.HasInstance)
            throw new System.InvalidOperationException("SoldierAIBrain requires GroupMoveManager for follow configuration.");
        var mgr = GroupMoveManager.Instance;
        Fix64 leaderEqR = mgr.FollowBaseStopRadiusFixed;
        Fix64 deadZoneRange = mgr.FollowDeadZoneRangeFixed;
        Fix64 innerDeadZoneRange = mgr.FollowInnerDeadZoneRangeFixed;
        Fix64 deadZoneOuter = leaderEqR + deadZoneRange;
        Fix64 distToLeader = FixVector2.Distance(self.LogicFramePositionFixed(), _leader.LogicFramePositionFixed());
        Fix64 innerDeadZone = leaderEqR + innerDeadZoneRange;

        if (distToLeader <= deadZoneOuter)
        {
            if (!_inDeadZone)
            {
                _inDeadZone = true;
                _deadZoneTarget = null;
                if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
                {
                    GameDebugSettings.Log(DebugCategory.Brain,
                        $"[{self.CharacterKey}] 进入死区 dist={distToLeader:F2} deadZone=[{leaderEqR:F2},{deadZoneOuter:F2}]");
                }
            }

            self.MoveComp.StopMove();
        }
        else
        {
            _inDeadZone = false;

            if (!_deadZoneTarget.HasValue ||
                FixVector2.Distance(_deadZoneTarget.Value, _leader.LogicFramePositionFixed()) > deadZoneOuter)
            {
                _deadZoneTarget = PickStableDeadZonePointFixed(self, _leader.LogicFramePositionFixed(), leaderEqR, deadZoneOuter);
                if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
                {
                    GameDebugSettings.Log(DebugCategory.Brain,
                        $"[{self.CharacterKey}] 生成死区目标点 {_deadZoneTarget.Value} dist={distToLeader:F2}");
                }
            }

            if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
            {
                GameDebugSettings.Log(DebugCategory.Brain,
                    $"[{self.CharacterKey}] Follow MoveTo deadZoneTarget={_deadZoneTarget.Value} leader={_leader.CharacterKey} leaderPos={_leader.LogicFramePosition()}");
            }
            self.MoveComp.MoveToFixed(_deadZoneTarget.Value);
        }

        if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
        {
            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.CharacterKey}] Follow dist={distToLeader:F2} deadZoneOuter={deadZoneOuter:F2} " +
                $"inDeadZone={_inDeadZone} target={(_deadZoneTarget.HasValue ? _deadZoneTarget.Value.ToString() : "null")}");
        }

        if (_leader != null && _leader.Alive)
        {
            Vector3 lp = _leader.LogicFramePosition() + Vector3.up * 0.1f;
            DrawCircle(lp, (float)leaderEqR, Color.red);
            DrawCircle(lp, (float)innerDeadZone, Color.yellow);
            DrawCircle(lp, (float)deadZoneOuter, Color.green);
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
    /// </summary>
    private Fix64 GetEffectiveAttackRange(IEntityContext self)
    {
        WeaponComp weaponComp = self.WeaponComp
                                ?? throw new System.InvalidOperationException(
                                    $"SoldierAIBrain requires WeaponComp. entity={self.LogicEntityId.Value}.");
        if (weaponComp.Data == null)
            throw new System.InvalidOperationException(
                $"SoldierAIBrain requires initialized weapon data. entity={self.LogicEntityId.Value}.");
        return weaponComp.AttackRange;
    }

    private void TickCombat(IEntityContext self, Fix64 dt)
    {
        var enemy = self.TargetComp?.CurrentTarget;
        if (!IsValidAttackTarget(self, enemy))
        {
            if (self.TargetComp is ILastSeenTargetingComp lastSeen && lastSeen.HasLastSeenPursuit)
                self.MoveComp.MoveToFixed(lastSeen.LastSeenPursuitDestinationFixed);
            else
                self.MoveComp.StopMove();
            return;
        }

        Fix64 distToEnemy = self.LogicFrameDistanceToTargetSurfaceFixed(enemy);
        Fix64 effectiveRange = GetEffectiveAttackRange(self);
        bool shouldAttackNow = distToEnemy <= effectiveRange;
        if (shouldAttackNow)
        {
            // 在攻击范围内 → 攻击并停下
            Attack = true;
            self.MoveComp.StopMove();
        }
        else
        {
            long approachStartTicks = MainThreadFrameProfiler.LoggingEnabled
                ? System.Diagnostics.Stopwatch.GetTimestamp()
                : 0L;
            bool approachResolved;
            FixVector2 reachableApproachPoint;
            string reachFailure;
            FlowFieldCrowdMovementSystem.NavigationQueryFailureKind failureKind;
            try
            {
                approachResolved = TryResolveCombatApproachPoint(
                    self,
                    out reachableApproachPoint,
                    out reachFailure,
                    out failureKind);
            }
            finally
            {
                if (MainThreadFrameProfiler.LoggingEnabled)
                {
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.FlowCombatApproach,
                        System.Diagnostics.Stopwatch.GetTimestamp() - approachStartTicks);
                }
            }
            if (!approachResolved)
            {
                if (failureKind == FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.PendingRuntimeUpdate)
                {
                    self.MoveComp.StopMove();
                    return;
                }

                if (failureKind == FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.Unreachable)
                {
                    if (FlowFieldCrowdMovementSystem.TryResolveReachableAttackAreaPointFixed(
                            self,
                            enemy,
                            effectiveRange,
                            out reachableApproachPoint,
                            out string attackAreaFailure,
                            out FlowFieldCrowdMovementSystem.NavigationQueryFailureKind attackAreaFailureKind))
                    {
                        self.MoveComp.MoveToFixed(reachableApproachPoint);
                        return;
                    }
                    if (attackAreaFailureKind == FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.PendingRuntimeUpdate)
                    {
                        self.MoveComp.StopMove();
                        return;
                    }
                    if (attackAreaFailureKind == FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.Unavailable)
                    {
                        throw new System.InvalidOperationException(
                            $"[{self.CharacterKey}] Combat attack-area query unavailable enemy={enemy.CharacterKey} " +
                            $"enemyPos={enemy.LogicFramePosition()} selfPos={self.LogicFramePosition()} reason={attackAreaFailure}");
                    }
                    if (attackAreaFailureKind != FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.Unreachable)
                    {
                        throw new System.InvalidOperationException(
                            $"[{self.CharacterKey}] Combat attack-area query failed without a handled reason enemy={enemy.CharacterKey} " +
                            $"kind={attackAreaFailureKind} reason={attackAreaFailure}");
                    }
                    if (!(self.TargetComp is INavigationReachabilityTargetingComp reachabilityTargeting))
                    {
                        throw new System.InvalidOperationException(
                            $"[{self.CharacterKey}] Targeting component cannot reject navigation-unreachable targets. component={self.TargetComp?.GetType().FullName ?? "null"}.");
                    }

                    GameDebugSettings.Log(
                        DebugCategory.Brain,
                        $"[{self.CharacterKey}] Reject navigation-unreachable target enemy={enemy.CharacterKey} enemyPos={enemy.LogicFramePosition()} " +
                        $"selfPos={self.LogicFramePosition()} dist={distToEnemy:F2} range={effectiveRange:F2} " +
                        $"slotReason={reachFailure} attackAreaReason={attackAreaFailure}");
                    reachabilityTargeting.RejectNavigationUnreachableTarget(enemy);
                    self.MoveComp.StopMove();
                    _combatApproachPoint = FixVector2.Zero;
                    _combatApproachTargetPoint = FixVector2.Zero;
                    _combatApproachTargetId = int.MinValue;
                    _combatApproachRefreshFrame = -1;
                    return;
                }

                throw new System.InvalidOperationException(
                    $"[{self.CharacterKey}] Combat approach point unreachable enemy={enemy.CharacterKey} enemyPos={enemy.LogicFramePosition()} " +
                    $"selfPos={self.LogicFramePosition()} dist={distToEnemy:F2} range={effectiveRange:F2} reason={reachFailure}");
            }

            if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
            {
                GameDebugSettings.Log(DebugCategory.Brain,
                    $"[{self.CharacterKey}] Combat MoveTo enemy={enemy.CharacterKey} enemyPos={enemy.LogicFramePosition()} approach={reachableApproachPoint} " +
                    $"selfPos={self.LogicFramePosition()} dist={distToEnemy:F2} range={effectiveRange:F2} " +
                    $"attackNow={shouldAttackNow}");
            }
            self.MoveComp.MoveToFixed(reachableApproachPoint);
        }
    }

    private bool TryResolveCombatApproachPoint(
        IEntityContext self,
        out FixVector2 approachPoint,
        out string failureReason,
        out FlowFieldCrowdMovementSystem.NavigationQueryFailureKind failureKind)
    {
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long preResolveStartTicks = profile
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        approachPoint = FixVector2.Zero;
        failureReason = string.Empty;
        failureKind = FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.None;
        var enemy = self.TargetComp?.CurrentTarget;
        if (!IsValidAttackTarget(self, enemy))
        {
            failureKind = FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.Unavailable;
            failureReason = "target invalid";
            return false;
        }

        Fix64 effectiveRange = GetEffectiveAttackRange(self);
        Fix64 selfRadius = ResolveCombatTargetRadius(self);
        Fix64 arriveDistance = ResolveNavigationArriveDistance(selfRadius);
        Fix64 targetRadius = ResolveCombatTargetRadius(enemy);
        bool useSurfacePoint = enemy.IsLogicBuilding();
        Fix64 standOff = ResolveCombatApproachStandOff(selfRadius, targetRadius, effectiveRange, arriveDistance, useSurfacePoint);
        Fix64 minimumStandOff = ResolveCombatApproachMinimumStandOff(selfRadius, targetRadius, useSurfacePoint);
        Fix64 requiredClearance = Fix64.Max(selfRadius * (Fix64)2 + CombatApproachOccupancyPaddingFixed, Fix64.FromRaw(1844));
        FixVector2 targetPoint = useSurfacePoint
            ? self.LogicFrameTargetClosestPointFixed(enemy)
            : enemy.LogicFramePositionFixed();

        int targetId = ResolveCombatEntityId(enemy);
        int frame = FlowFieldCrowdMovementSystem.GetCurrentNavigationFrame();
        Fix64 minRefreshDistance = Fix64.Max(Fix64.FromRaw(1024), selfRadius * Fix64.FromRaw(6144));
        Fix64 targetPointMoveDistance = FixVector2.Distance(_combatApproachTargetPoint, targetPoint);
        bool targetMatchesCache = _combatApproachTargetId == targetId;
        FixVector2 cachedApproachPoint = targetMatchesCache && !useSurfacePoint
            ? targetPoint + (_combatApproachPoint - _combatApproachTargetPoint)
            : _combatApproachPoint;
        Fix64 cachedApproachToEnemy = ResolveCombatApproachDistanceToTargetSurface(enemy, cachedApproachPoint);
        bool cachedApproachInRange = cachedApproachToEnemy <= effectiveRange;
        Fix64 selfToCachedApproach = FixVector2.Distance(self.LogicFramePositionFixed(), cachedApproachPoint);
        bool selfNeedsCachedApproach = selfToCachedApproach > minRefreshDistance;
        bool canReuseCachedApproach = targetMatchesCache
            && cachedApproachInRange
            && selfNeedsCachedApproach;
        bool cachedReserved = false;
        int cachedBlockingAgentId = 0;
        string cachedFailureReason = string.Empty;
        FlowFieldCrowdMovementSystem.NavigationQueryFailureKind cachedFailureKind =
            FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.None;
        if (canReuseCachedApproach)
        {
            cachedReserved = TryReserveCombatApproachPoint(
                self,
                targetId,
                cachedApproachPoint,
                selfRadius,
                out cachedBlockingAgentId,
                out cachedFailureReason,
                out cachedFailureKind);
        }

        if (canReuseCachedApproach && cachedReserved)
        {
            _combatApproachPoint = cachedApproachPoint;
            _combatApproachTargetPoint = targetPoint;
            _combatApproachRefreshFrame = frame;
            approachPoint = cachedApproachPoint;
            return true;
        }

        if (cachedFailureKind == FlowFieldCrowdMovementSystem.NavigationQueryFailureKind.PendingRuntimeUpdate)
        {
            failureReason = cachedFailureReason;
            failureKind = cachedFailureKind;
            return false;
        }

        LogCombatApproachCacheMiss(
            self,
            enemy,
            frame,
            targetId,
            targetPoint,
            targetPointMoveDistance,
            targetMatchesCache,
            cachedApproachPoint,
            cachedApproachToEnemy,
            cachedApproachInRange,
            selfToCachedApproach,
            minRefreshDistance,
            selfNeedsCachedApproach,
            canReuseCachedApproach,
            cachedReserved,
            cachedBlockingAgentId,
            cachedFailureReason,
            cachedFailureKind);

        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachPreResolve,
                System.Diagnostics.Stopwatch.GetTimestamp() - preResolveStartTicks);
        }
        long coreStartTicks = profile
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        bool coreResolved = FlowFieldCrowdMovementSystem.TryResolveCombatApproachPointFixed(
                self,
                enemy,
                targetPoint,
                standOff,
                minimumStandOff,
                CombatApproachRingSpacingFixed,
                CombatApproachRingCount,
                CombatApproachCandidateCount,
                requiredClearance,
                out FixVector2 resolvedApproachPoint,
                out failureReason,
                out failureKind);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCombatApproachCore,
                System.Diagnostics.Stopwatch.GetTimestamp() - coreStartTicks);
        }
        if (!coreResolved)
        {
            return false;
        }

        _combatApproachPoint = resolvedApproachPoint;
        _combatApproachTargetId = targetId;
        _combatApproachRefreshFrame = frame;
        _combatApproachTargetPoint = targetPoint;
        if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
        {
            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.CharacterKey}] Combat approach refresh enemy={enemy.CharacterKey} selfPos={self.LogicFramePosition()} enemyPos={enemy.LogicFramePosition()} " +
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
        FixVector2 targetPoint,
        Fix64 targetPointMoveDistance,
        bool targetMatchesCache,
        FixVector2 cachedApproachPoint,
        Fix64 cachedApproachToEnemy,
        bool cachedApproachInRange,
        Fix64 selfToCachedApproach,
        Fix64 minRefreshDistance,
        bool selfNeedsCachedApproach,
        bool canReuseCachedApproach,
        bool cachedReserved,
        int cachedBlockingAgentId,
        string cachedFailureReason,
        FlowFieldCrowdMovementSystem.NavigationQueryFailureKind cachedFailureKind)
    {
        if (!GameDebugSettings.IsEnabled(DebugCategory.Brain) && !GameDebugSettings.IsEnabled(DebugCategory.Move))
            return;
        if (_combatApproachDiagnosticFrame >= 0 && frame - _combatApproachDiagnosticFrame < 10)
            return;

        _combatApproachDiagnosticFrame = frame;
        int selfId = ResolveCombatEntityId(self);
        Debug.LogWarning(
            $"[FlowCombatApproachCacheMiss] frame={frame} self={self.CharacterKey} selfId={selfId} target={enemy.CharacterKey} targetId={targetId} " +
            $"selfPos={self.LogicFramePosition()} targetPos={enemy.LogicFramePosition()} targetPoint={targetPoint} cachedApproach={_combatApproachPoint} translatedApproach={cachedApproachPoint} " +
            $"cacheTargetId={_combatApproachTargetId} cacheFrame={_combatApproachRefreshFrame} targetMatches={targetMatchesCache} " +
            $"targetMove={targetPointMoveDistance:F3} " +
            $"approachToEnemy={cachedApproachToEnemy:F3} inRange={cachedApproachInRange} selfToApproach={selfToCachedApproach:F3}/{minRefreshDistance:F3} " +
            $"needsMove={selfNeedsCachedApproach} canReuse={canReuseCachedApproach} reserved={cachedReserved} blockingId={cachedBlockingAgentId} " +
            $"validationKind={cachedFailureKind} validationReason={cachedFailureReason}");
    }

    private static bool TryReserveCombatApproachPoint(
        IEntityContext self,
        int targetId,
        FixVector2 approachPoint,
        Fix64 selfRadius,
        out int blockingAgentId,
        out string failureReason,
        out FlowFieldCrowdMovementSystem.NavigationQueryFailureKind failureKind)
    {
        Fix64 requiredClearance = Fix64.Max(
            selfRadius * (Fix64)2 + CombatApproachOccupancyPaddingFixed,
            Fix64.FromRaw(1844));
        return FlowFieldCrowdMovementSystem.TryReserveReachableNavigationGoalFixed(
            self,
            targetId,
            approachPoint,
            selfRadius,
            requiredClearance,
            out blockingAgentId,
            out failureReason,
            out failureKind);
    }

    private static Fix64 ResolveCombatTargetRadius(IEntityContext entity)
    {
        if (entity == null)
            throw new System.ArgumentNullException(nameof(entity));
        if (LogicFrameRuntime.IsTicking)
            return LogicEntityFrameSnapshotService.GetRequiredCurrent(entity).CollisionRadius;

        return Fix64.Max(
            Fix64.Zero,
            (entity.GetProperty(CreatureMainProperty.CollisionRadius)));
    }

    private static Fix64 ResolveCombatApproachStandOff(
        Fix64 selfRadius,
        Fix64 targetRadius,
        Fix64 effectiveRange,
        Fix64 arriveDistance,
        bool targetPointOnSurface)
    {
        Fix64 surfaceDistance = Fix64.Max(
            selfRadius + Fix64.FromRaw(205),
            effectiveRange - arriveDistance - CombatApproachRangeSlackFixed);
        return targetPointOnSurface ? surfaceDistance : targetRadius + surfaceDistance;
    }

    private static Fix64 ResolveCombatApproachMinimumStandOff(
        Fix64 selfRadius,
        Fix64 targetRadius,
        bool targetPointOnSurface)
    {
        Fix64 surfaceSeparation = Fix64.Max(Fix64.FromRaw(205), selfRadius + Fix64.FromRaw(205));
        return targetPointOnSurface ? surfaceSeparation : targetRadius + surfaceSeparation;
    }

    private static Fix64 ResolveCombatApproachDistanceToTargetSurface(IEntityContext target, FixVector2 point)
    {
        if (target == null)
            throw new System.InvalidOperationException("ResolveCombatApproachDistanceToTargetSurface failed: target is null.");

        return target.LogicFrameDistanceFromPointToSurfaceFixed(point);
    }


    private static int ResolveCombatEntityId(IEntityContext entity)
    {
        if (entity == null)
            throw new System.ArgumentNullException(nameof(entity));
        if (!entity.LogicEntityId.IsValid)
            throw new System.InvalidOperationException($"SoldierAIBrain.ResolveCombatEntityId failed: {entity.GetType().Name} has an invalid logic entity id.");

        return entity.LogicEntityId.Value;
    }

    private static Fix64 ResolveNavigationArriveDistance(Fix64 collisionRadius)
    {
        if (collisionRadius > Fix64.FromRaw(1))
            return Fix64.Max(Fix64.FromRaw(328), collisionRadius * Fix64.FromRaw(2458));

        return Fix64.FromRaw(615);
    }

    private static Vector3 ToWorldVector3(FixVector2 point)
    {
        return new Vector3((float)point.x, 0f, (float)point.y);
    }

    private void TickReturning(IEntityContext self, Fix64 dt)
    {
        if (!_birthPosition.HasValue)
            throw new System.InvalidOperationException(
                $"SoldierAIBrain returning tick has no birth position. entity={self.LogicEntityId.Value}.");

        Attack = false;

        // 返航期间持续清 _lastAttacker / CurrentTarget，
        // 避免被友军告警 / 受击通知重新设上，导致 ExitReturning 后立刻 fallback 又追出去
        if (self.TargetComp != null)
        {
            if (self.TargetComp.CurrentTarget != null) self.TargetComp.CurrentTarget = null;
            self.TargetComp.ClearAggro();
        }

        if (GameDebugSettings.IsEnabled(DebugCategory.Brain))
        {
            GameDebugSettings.Log(DebugCategory.Brain,
                $"[{self.CharacterKey}] Returning MoveTo birth={_birthPosition.Value} from={self.LogicFramePositionFixed()}");
        }
        self.MoveComp.MoveToFixed(_birthPosition.Value);
    }

    private sealed class ReturningTargetingCapabilityLocker : ICapability
    {
        public void ShutDown() { }
        public void Resume() { }
    }

    private static FixVector2 PickStableDeadZonePointFixed(
        IEntityContext self,
        FixVector2 leaderPos,
        Fix64 innerR,
        Fix64 outerR)
    {
        int agentTypeId = self is ILogicFrameEntity logicEntity
            ? logicEntity.NavigationAgentTypeId
            : MAEntity.UnknownNavAgentTypeId;
        if (agentTypeId == MAEntity.UnknownNavAgentTypeId)
            agentTypeId = 0;

        Fix64 sampleRadius = Fix64.Max(Fix64.FromRaw(2048), outerR * Fix64.FromRaw(1024));
        const int maxAttempts = 24;

        System.Text.StringBuilder failure = new System.Text.StringBuilder(512);
        failure.Append($"[{self.CharacterKey}] 死区目标点生成失败 leaderPos={leaderPos} innerR={(float)innerR:F2} outerR={(float)outerR:F2} agentType={agentTypeId} flowSnapRadius={(float)sampleRadius:F2} attempts=[");

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int directionIndex = (self.LogicEntityId.Value + attempt * 5) % StableDeadZoneDirections.Length;
            int radiusStep = (attempt * 7) % maxAttempts;
            Fix64 radius = innerR + (outerR - innerR) * (Fix64)(radiusStep + 1) / (Fix64)maxAttempts;
            FixVector2 direction = StableDeadZoneDirections[directionIndex];
            FixVector2 candidate = leaderPos + direction * radius;
            bool hit = FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPointFixed(
                candidate,
                agentTypeId,
                sampleRadius,
                Fix64.Zero,
                out FixVector2 legalPoint);

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

}
