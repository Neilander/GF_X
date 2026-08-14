using System.Collections.Generic;
using UnityEngine;

public class CharacterTargetingComp : ITargetingComp, ILogicDeterministicStateContributor
{
    private enum TargetingMode
    {
        Default = 0,
        DefendEnemy = 1
    }

    private IEntityContext _ctx;
    private IEntityContext _currentTarget;
    private IEntityContext _additionalPursuitTarget;
    private bool _hasAttackedCurrentTarget;
    private TargetingMode _targetingMode;
    private IEntityContext _defendFallbackTarget;
    public IEntityContext CurrentTarget
    {
        get => _currentTarget;
        set
        {
            if (ReferenceEquals(_currentTarget, value))
                return;

            _currentTarget = value;
            _additionalPursuitTarget = null;
            _hasAttackedCurrentTarget = false;
        }
    }
    public IEntityContext FollowTarget { get; private set; }

    private Fix64 m_AggroRange = (Fix64)6;
    private Fix64 m_ForgetRange = (Fix64)8;
    private Fix64 m_FollowSearchRange = (Fix64)30;
    private Fix64 m_AlertRadius = (Fix64)5;
    public Fix64 AggroRangeFixed { get => m_AggroRange; set => m_AggroRange = LogicTargetingRange.Require(value, nameof(AggroRangeFixed)); }
    public Fix64 ForgetRangeFixed { get => m_ForgetRange; set => m_ForgetRange = LogicTargetingRange.Require(value, nameof(ForgetRangeFixed)); }
    public Fix64 FollowSearchRangeFixed { get => m_FollowSearchRange; set => m_FollowSearchRange = LogicTargetingRange.Require(value, nameof(FollowSearchRangeFixed)); }
    public Fix64 AlertRadiusFixed { get => m_AlertRadius; set => m_AlertRadius = LogicTargetingRange.Require(value, nameof(AlertRadiusFixed)); }

    /// <summary>
    /// 是否启用"视线外仇恨"。建筑等不应被拉走，可关掉。
    /// </summary>
    public bool EnableAggroFallback { get; set; } = true;

    /// <summary>视线外仇恨：scan 找不到目标时 fallback 到这个 attacker。第一次受击锁定，nearest 切到别的目标后清掉。</summary>
    private IEntityContext _lastAttacker;

    private Fix64 _scanTimer = Fix64.Zero;
    private static readonly Fix64 SCAN_INTERVAL = Fix64.FromRaw(820);

    public void UseDefaultMode()
    {
        _targetingMode = TargetingMode.Default;
        _defendFallbackTarget = null;
        _additionalPursuitTarget = null;
        _hasAttackedCurrentTarget = false;
    }

    public void UseDefendEnemyMode(IEntityContext fallbackTarget)
    {
        _targetingMode = TargetingMode.DefendEnemy;
        _defendFallbackTarget = fallbackTarget;
        _lastAttacker = null;
        _additionalPursuitTarget = null;
        _hasAttackedCurrentTarget = false;
    }

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        CurrentTarget = null;
        FollowTarget = null;
        _lastAttacker = null;
        _additionalPursuitTarget = null;
        _hasAttackedCurrentTarget = false;
        _scanTimer = Fix64.Zero;
        _targetingMode = TargetingMode.Default;
        _defendFallbackTarget = null;
    }

    public void NotifyDamageTaken(IEntityContext attacker)
    {
        // 基础校验
        if (attacker == null || attacker == _ctx) return;
        if (!attacker.IsAttackTargetable()) return;
        if (!EntityCombatTeamHelper.IsEnemy(_ctx, attacker)) return;

        // 自己记 _lastAttacker（仅 EnableAggroFallback 时；建筑这里不记，原地反击）
        if (EnableAggroFallback && _lastAttacker == null)
        {
            _lastAttacker = attacker;
            GameDebugSettings.Log(DebugCategory.Targeting,
                $"{_ctx} 记下受击 attacker={attacker}（视线外仇恨）");
        }

        // 广播给周围友军：让附近士兵知道有人在打我（建筑被打也走这条路，召唤友军反击）
        BroadcastEnemyToAllies(attacker);
    }

    public void NotifyAllyFoundEnemy(IEntityContext enemy)
    {
        if (!EnableAggroFallback) return;
        if (_lastAttacker != null) return; // 已有记忆（受击或别的友军告警）→ 不覆盖
        if (enemy == null || enemy == _ctx) return;
        if (!enemy.IsAttackTargetable()) return;
        if (!EntityCombatTeamHelper.IsEnemy(_ctx, enemy)) return;
        _lastAttacker = enemy;
        GameDebugSettings.Log(DebugCategory.Targeting,
            $"{_ctx} 收到友军告警 enemy={enemy}（视线外仇恨）");
    }

    public void ClearAggro()
    {
        if (_lastAttacker == null) return;
        GameDebugSettings.Log(DebugCategory.Targeting,
            $"{_ctx} 清除受击仇恨 (was={_lastAttacker})");
        _lastAttacker = null;
    }

    private void BroadcastEnemyToAllies(IEntityContext enemy)
    {
        if (enemy == null || m_AlertRadius <= Fix64.Zero) return;
        Fix64 radiusSquared = m_AlertRadius * m_AlertRadius;
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            var ally = all[i];
            if (ally == null || ReferenceEquals(ally, _ctx)) continue;
            if (ally.Side != _ctx.Side) continue;
            if (!ally.Alive) continue;
            FixVector2 offset = ally.LogicFramePositionFixed() - _ctx.LogicFramePositionFixed();
            if (FixVector2.SqrMagnitude(offset) > radiusSquared) continue;
            ally.TargetComp?.NotifyAllyFoundEnemy(enemy);
        }
    }

    private bool IsLastAttackerStillValid()
    {
        if (_lastAttacker == null) return false;
        if (!_lastAttacker.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(_ctx, _lastAttacker))
        {
            _lastAttacker = null;
            return false;
        }
        return true;
    }

    public void UpdateTargeting(Fix64 deltaTime)
    {
        if (_ctx == null) return;
        if (_targetingMode == TargetingMode.DefendEnemy)
        {
            UpdateDefendEnemyTargeting(deltaTime);
            return;
        }

        bool useAttackRangeOnlyForThisUnit = ShouldUseAttackRangeOnly(_ctx);
        bool isBuilding = _ctx.IsLogicBuilding();
        Fix64 effectiveAttackRange = GetEffectiveAttackRange();
        Fix64 additionalPursuitRange = isBuilding
            ? effectiveAttackRange
            : effectiveAttackRange + TargetThreatUtility.ReadAdditionalPursuitDistanceWorld();

        if (CurrentTarget != null && _ctx.AtkComp != null && _ctx.AtkComp.IsAttacking)
            _hasAttackedCurrentTarget = true;

        Fix64 currentTargetDist = Fix64.FromRaw(long.MaxValue);
        Fix64 currentTargetPursuitThreat = -Fix64.FromRaw(long.MaxValue);
        bool lostCurrentTarget = false;

        // 1. 维护当前敌人目标
        if (CurrentTarget != null)
        {
            if (!CurrentTarget.IsRegisteredInLogicWorld()
                || !CurrentTarget.IsAttackTargetable()
                || !EntityCombatTeamHelper.IsEnemy(_ctx, CurrentTarget))
            {
                GameDebugSettings.Log(DebugCategory.Targeting,
                    $"{_ctx} 丢失敌人目标 {CurrentTarget} | active={CurrentTarget.IsRegisteredInLogicWorld()} alive={CurrentTarget.Alive}");
                CurrentTarget = null;
                lostCurrentTarget = true;
                currentTargetDist = Fix64.FromRaw(long.MaxValue);
                currentTargetPursuitThreat = -Fix64.FromRaw(long.MaxValue);
                if (useAttackRangeOnlyForThisUnit && _lastAttacker != null)
                    ClearAggro();
            }
            else
            {
                Fix64 dist = _ctx.LogicFrameDistanceToTargetSurfaceFixed(CurrentTarget);
                currentTargetDist = dist;
                Fix64 targetRetentionRange = useAttackRangeOnlyForThisUnit
                    ? effectiveAttackRange
                    : ReferenceEquals(CurrentTarget, _additionalPursuitTarget)
                        ? Fix64.Max(m_ForgetRange, additionalPursuitRange)
                        : m_ForgetRange;
                // 视线外仇恨特例：CurrentTarget 是 fallback 来的 attacker → 跳过距离过滤，让单位一路追上去
                bool isAggroFallback = !useAttackRangeOnlyForThisUnit && (CurrentTarget == _lastAttacker);
                bool dropByDistance = !isAggroFallback && dist > targetRetentionRange;
                if (dropByDistance)
                {
                    GameDebugSettings.Log(DebugCategory.Targeting,
                        $"{_ctx} 丢失敌人目标 {CurrentTarget} | dist={dist:F1} retentionRange={targetRetentionRange:F1} alive={CurrentTarget.Alive}");
                    CurrentTarget = null;
                    lostCurrentTarget = true;
                    currentTargetDist = Fix64.FromRaw(long.MaxValue);
                    currentTargetPursuitThreat = -Fix64.FromRaw(long.MaxValue);
                    if (useAttackRangeOnlyForThisUnit && _lastAttacker != null)
                        ClearAggro();
                }
            }
        }

        // 2. 维护跟随目标
        if (FollowTarget != null)
        {
            if (!FollowTarget.IsRegisteredInLogicWorld() || !FollowTarget.Alive)
            {
                GameDebugSettings.Log(DebugCategory.Targeting, $"{_ctx} 丢失跟随目标 {FollowTarget} | active={FollowTarget.IsRegisteredInLogicWorld()} alive={FollowTarget.Alive}");
                FollowTarget = null;
            }
            else
            {
                Fix64 dist = _ctx.LogicFrameCenterDistanceFixed(FollowTarget);
                if (dist > m_FollowSearchRange)
                {
                    GameDebugSettings.Log(DebugCategory.Targeting, $"{_ctx} 丢失跟随目标 {FollowTarget} | dist={dist:F1} followRange={m_FollowSearchRange} alive={FollowTarget.Alive}");
                    FollowTarget = null;
                }
            }
        }

        // 3. 降频扫描新目标（仅真实实体使用 SimpleTargeting）
        _scanTimer += deltaTime;
        if (lostCurrentTarget || _scanTimer >= SCAN_INTERVAL)
        {
            _scanTimer = Fix64.Zero;

            // 找敌人：遍历 EntityRegistry，按配置仇恨值选择目标
            IEntityContext bestTarget = null;
            Fix64 scanRange = useAttackRangeOnlyForThisUnit
                ? effectiveAttackRange
                : Fix64.Max(m_AggroRange, effectiveAttackRange);
            Fix64 bestDistance = scanRange;
            Fix64 bestThreat = -Fix64.FromRaw(long.MaxValue);
            Fix64 threatPerLevel = TargetThreatUtility.ReadThreatPerLevel();
            Fix64 buildingExtraThreat = TargetThreatUtility.ReadBuildingExtraThreat();
            if (CurrentTarget != null)
            {
                currentTargetPursuitThreat = TargetThreatUtility.CalculatePursuitThreat(
                    _ctx,
                    CurrentTarget,
                    threatPerLevel,
                    buildingExtraThreat);
            }
            bool targetLockedByAttack = _hasAttackedCurrentTarget
                                        || (_ctx.AtkComp != null && _ctx.AtkComp.IsAttacking);

            IEntityContext interruptTarget = null;
            Fix64 interruptDistance = Fix64.Zero;
            Fix64 interruptThreat = -Fix64.FromRaw(long.MaxValue);
            Fix64 interruptPursuitThreat = Fix64.Zero;

            var all = EntityRegistry.AllEntities;
            for (int i = 0; i < all.Count; i++)
            {
                var other = all[i];
                if (other == _ctx) continue;
                if (!other.IsAttackTargetable()) continue;
                if (!EntityCombatTeamHelper.IsEnemy(_ctx, other)) continue;

                Fix64 dist = _ctx.LogicFrameDistanceToTargetSurfaceFixed(other);
                if (dist > scanRange) continue;

                Fix64 pursuitThreat = TargetThreatUtility.CalculatePursuitThreat(
                    _ctx,
                    other,
                    threatPerLevel,
                    buildingExtraThreat);
                Fix64 threat = TargetThreatUtility.CalculateSelectionThreat(
                    _ctx,
                    other,
                    dist,
                    threatPerLevel,
                    buildingExtraThreat);
                if (TargetThreatUtility.HasHigherPriority(threat, other, bestThreat, bestTarget))
                {
                    bestThreat = threat;
                    bestDistance = dist;
                    bestTarget = other;
                }

                // Attack lock is interrupted only by a higher taunt-equivalent pursuit score.
                if (!isBuilding
                    && targetLockedByAttack
                    && pursuitThreat > currentTargetPursuitThreat
                    && dist <= additionalPursuitRange
                    && TargetThreatUtility.HasHigherPriority(threat, other, interruptThreat, interruptTarget))
                {
                    interruptPursuitThreat = pursuitThreat;
                    interruptThreat = threat;
                    interruptDistance = dist;
                    interruptTarget = other;
                }
            }

            if (CurrentTarget == null)
            {
                if (bestTarget != null)
                {
                    GameDebugSettings.Log(DebugCategory.Targeting,
                        $"{_ctx} 锁定敌人 {bestTarget} | dist={bestDistance:F1} threat={bestThreat} scanRange={scanRange:F1}");
                    CurrentTarget = bestTarget;
                    // 走正常索敌了，受击仇恨记忆作废（即使 nearest 就是 _lastAttacker 本人，也清掉，让后续切换走正常规则）
                    if (_lastAttacker != null) ClearAggro();
                }
                else if (!useAttackRangeOnlyForThisUnit && EnableAggroFallback && IsLastAttackerStillValid())
                {
                    // 视线外仇恨 fallback：scan 范围空，回去打打过自己的人
                    CurrentTarget = _lastAttacker;
                    GameDebugSettings.Log(DebugCategory.Targeting,
                        $"{_ctx} fallback 到受击 attacker {_lastAttacker} | dist={_ctx.LogicFrameDistanceToTargetSurface(_lastAttacker):F1}");
                }
            }
            else
            {
                IEntityContext switchTarget = null;
                Fix64 switchDist = Fix64.Zero;
                Fix64 switchThreat = Fix64.Zero;
                Fix64 switchPursuitThreat = Fix64.Zero;
                string switchReason = null;

                if (isBuilding && bestTarget != null && bestTarget != CurrentTarget)
                {
                    Fix64 currentThreat = TargetThreatUtility.CalculateSelectionThreat(
                        _ctx,
                        CurrentTarget,
                        currentTargetDist,
                        threatPerLevel,
                        buildingExtraThreat);
                    if (TargetThreatUtility.HasHigherPriority(
                            bestThreat,
                            bestTarget,
                            currentThreat,
                            CurrentTarget))
                    {
                        switchTarget = bestTarget;
                        switchDist = bestDistance;
                        switchThreat = bestThreat;
                        switchPursuitThreat = TargetThreatUtility.CalculatePursuitThreat(
                            _ctx,
                            bestTarget,
                            threatPerLevel,
                            buildingExtraThreat);
                        switchReason = "attack_range_only_higher_threat";
                    }
                }
                else if (targetLockedByAttack && interruptTarget != null)
                {
                    switchTarget = interruptTarget;
                    switchDist = interruptDistance;
                    switchThreat = interruptThreat;
                    switchPursuitThreat = interruptPursuitThreat;
                    switchReason = "attacking_higher_pursuit_threat_in_pursuit_range";
                }
                else if (!targetLockedByAttack && bestTarget != null && bestTarget != CurrentTarget)
                {
                    Fix64 currentThreat = TargetThreatUtility.CalculateSelectionThreat(
                        _ctx,
                        CurrentTarget,
                        currentTargetDist,
                        threatPerLevel,
                        buildingExtraThreat);
                    if (TargetThreatUtility.HasHigherPriority(
                            bestThreat,
                            bestTarget,
                            currentThreat,
                            CurrentTarget))
                    {
                        switchTarget = bestTarget;
                        switchDist = bestDistance;
                        switchThreat = bestThreat;
                        switchPursuitThreat = TargetThreatUtility.CalculatePursuitThreat(
                            _ctx,
                            bestTarget,
                            threatPerLevel,
                            buildingExtraThreat);
                        switchReason = "searching_higher_threat";
                    }
                }

                if (switchTarget != null)
                {
                    GameDebugSettings.Log(DebugCategory.Targeting,
                        $"{_ctx} 切换敌人 {CurrentTarget} -> {switchTarget} | currentDist={currentTargetDist:F1} newDist={switchDist:F1} currentPursuitThreat={currentTargetPursuitThreat} newPursuitThreat={switchPursuitThreat} newThreat={switchThreat} attackLocked={targetLockedByAttack} reason={switchReason}");
                    CurrentTarget = switchTarget;
                    if (switchReason == "attacking_higher_pursuit_threat_in_pursuit_range")
                        _additionalPursuitTarget = switchTarget;
                    // 已切到正常扫描的目标 → 清掉受击仇恨记忆（即使切到的就是 _lastAttacker 本人也清，让后续完全走正常规则）
                    if (_lastAttacker != null) ClearAggro();
                }
            }

            // 找跟随目标：同阵营的领袖/玩家
            if (FollowTarget == null)
            {
                var player = EntityRegistry.Player;
                if (player != null && player.Alive && player.Side == _ctx.Side)
                {
                    Fix64 dist = _ctx.LogicFrameCenterDistanceFixed(player);
                    if (dist <= m_FollowSearchRange)
                    {
                        GameDebugSettings.Log(DebugCategory.Targeting, $"{_ctx} 锁定跟随目标 {player} | dist={dist:F1} followRange={m_FollowSearchRange}");
                        FollowTarget = player;
                    }
                }
            }

            // scan tick 末尾：自己有目标 → 广播给周围友军
            if (CurrentTarget != null)
            {
                BroadcastEnemyToAllies(CurrentTarget);
            }
        }
    }

    public void ShutDown()
    {
        CurrentTarget = null;
        FollowTarget = null;
        _lastAttacker = null;
        _additionalPursuitTarget = null;
        _hasAttackedCurrentTarget = false;
        _defendFallbackTarget = null;
        _targetingMode = TargetingMode.Default;
    }
    public void Resume() { }

    private void UpdateDefendEnemyTargeting(Fix64 deltaTime)
    {
        Fix64 effectiveAttackRange = GetEffectiveAttackRange();
        Fix64 scanRange = Fix64.Max(m_AggroRange, effectiveAttackRange);
        Fix64 additionalPursuitRange = effectiveAttackRange
                                       + TargetThreatUtility.ReadAdditionalPursuitDistanceWorld();

        if (CurrentTarget != null && _ctx.AtkComp != null && _ctx.AtkComp.IsAttacking)
            _hasAttackedCurrentTarget = true;

        _scanTimer += deltaTime;
        if (_scanTimer < SCAN_INTERVAL)
            return;

        _scanTimer = Fix64.Zero;

        if (CurrentTarget != null && !IsCurrentDefendTargetStillValid(CurrentTarget, scanRange))
            CurrentTarget = null;

        bool targetLockedByAttack = _hasAttackedCurrentTarget
                                    || (_ctx.AtkComp != null && _ctx.AtkComp.IsAttacking);
        Fix64 threatPerLevel = TargetThreatUtility.ReadThreatPerLevel();
        Fix64 buildingExtraThreat = TargetThreatUtility.ReadBuildingExtraThreat();
        Fix64 currentPursuitThreat = CurrentTarget != null
            ? TargetThreatUtility.CalculatePursuitThreat(
                _ctx,
                CurrentTarget,
                threatPerLevel,
                buildingExtraThreat)
            : -Fix64.FromRaw(long.MaxValue);

        IEntityContext bestTarget = null;
        Fix64 bestDistance = scanRange;
        Fix64 bestThreat = -Fix64.FromRaw(long.MaxValue);
        IEntityContext interruptTarget = null;
        Fix64 interruptThreat = -Fix64.FromRaw(long.MaxValue);

        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            var other = all[i];
            if (other == null || ReferenceEquals(other, _ctx))
                continue;
            if (!other.IsAttackTargetable())
                continue;
            if (!EntityCombatTeamHelper.IsEnemy(_ctx, other))
                continue;

            Fix64 distance = _ctx.LogicFrameDistanceToTargetSurfaceFixed(other);
            if (distance > scanRange)
                continue;

            Fix64 threat = TargetThreatUtility.CalculateSelectionThreat(
                _ctx,
                other,
                distance,
                threatPerLevel,
                buildingExtraThreat);
            if (TargetThreatUtility.HasHigherPriority(threat, other, bestThreat, bestTarget))
            {
                bestDistance = distance;
                bestThreat = threat;
                bestTarget = other;
            }

            Fix64 pursuitThreat = TargetThreatUtility.CalculatePursuitThreat(
                _ctx,
                other,
                threatPerLevel,
                buildingExtraThreat);
            // Attack lock is interrupted only by a higher taunt-equivalent pursuit score.
            if (targetLockedByAttack
                && pursuitThreat > currentPursuitThreat
                && distance <= additionalPursuitRange
                && TargetThreatUtility.HasHigherPriority(threat, other, interruptThreat, interruptTarget))
            {
                interruptThreat = threat;
                interruptTarget = other;
            }
        }

        if (CurrentTarget == null)
        {
            CurrentTarget = bestTarget ?? (IsDefendFallbackTargetValid() ? _defendFallbackTarget : null);
            return;
        }

        if (targetLockedByAttack)
        {
            if (interruptTarget != null && !ReferenceEquals(interruptTarget, CurrentTarget))
            {
                CurrentTarget = interruptTarget;
                _additionalPursuitTarget = interruptTarget;
            }
            return;
        }

        IEntityContext desiredTarget = bestTarget ?? (IsDefendFallbackTargetValid() ? _defendFallbackTarget : null);
        if (desiredTarget == null || ReferenceEquals(CurrentTarget, desiredTarget))
            return;

        Fix64 currentDistance = _ctx.LogicFrameDistanceToTargetSurfaceFixed(CurrentTarget);
        Fix64 currentThreat = TargetThreatUtility.CalculateSelectionThreat(
            _ctx,
            CurrentTarget,
            currentDistance,
            threatPerLevel,
            buildingExtraThreat);
        if (TargetThreatUtility.HasHigherPriority(bestThreat, desiredTarget, currentThreat, CurrentTarget))
            CurrentTarget = desiredTarget;
    }

    private bool IsCurrentDefendTargetStillValid(IEntityContext target, Fix64 scanRange)
    {
        if (target == null || !target.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(_ctx, target))
            return false;

        if (ReferenceEquals(target, _defendFallbackTarget))
            return IsDefendFallbackTargetValid();

        Fix64 distance = _ctx.LogicFrameDistanceToTargetSurfaceFixed(target);
        Fix64 retentionRange = ReferenceEquals(target, _additionalPursuitTarget)
            ? Fix64.Max(Fix64.Max(m_ForgetRange, scanRange), GetEffectiveAttackRange()
                + TargetThreatUtility.ReadAdditionalPursuitDistanceWorld())
            : Fix64.Max(m_ForgetRange, scanRange);
        return distance <= retentionRange;
    }

    private bool IsDefendFallbackTargetValid()
    {
        if (_defendFallbackTarget == null)
            return false;

        if (!_defendFallbackTarget.IsAttackTargetable())
            return false;

        return EntityCombatTeamHelper.IsEnemy(_ctx, _defendFallbackTarget);
    }

    private Fix64 GetEffectiveAttackRange()
    {
        Fix64 weaponRange = _ctx.WeaponComp != null ? _ctx.WeaponComp.AttackRange : Fix64.FromRaw(6144);
        return weaponRange;
    }

    private static int GetTauntLevel(IEntityContext entity)
    {
        return entity?.TauntLevel ?? 0;
    }

    private static bool ShouldUseAttackRangeOnly(IEntityContext entity)
    {
        return entity.IsLogicBuilding() || IsHeroUnit(entity);
    }

    private static bool IsHeroUnit(IEntityContext entity)
    {
        if (entity?.CharacterData?.UnitTags != null)
        {
            var tags = entity.CharacterData.UnitTags;
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == UnitTag.Hero)
                    return true;
            }
        }

        return false;
    }

    private static bool HasLowerLogicId(IEntityContext candidate, IEntityContext current)
    {
        return current == null || candidate.LogicEntityId < current.LogicEntityId;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new System.ArgumentNullException(nameof(hasher));
        hasher.Add((int)_targetingMode);
        hasher.Add(_scanTimer.RawValue);
        hasher.Add(GetLogicId(FollowTarget));
        hasher.Add(GetLogicId(_lastAttacker));
        hasher.Add(GetLogicId(_additionalPursuitTarget));
        hasher.Add(GetLogicId(_defendFallbackTarget));
        hasher.Add(_hasAttackedCurrentTarget);
        hasher.Add(EnableAggroFallback);
        hasher.Add(m_AggroRange.RawValue);
        hasher.Add(m_ForgetRange.RawValue);
        hasher.Add(m_FollowSearchRange.RawValue);
        hasher.Add(m_AlertRadius.RawValue);
    }

    private static int GetLogicId(IEntityContext entity)
    {
        return entity != null && entity.LogicEntityId.IsValid ? entity.LogicEntityId.Value : 0;
    }

}

public sealed class HealTargetingComp : ITargetingComp, IMultiTargetingComp, ILogicDeterministicStateContributor
{
    private static readonly Fix64 ScanInterval = Fix64.FromRaw(820);

    private IEntityContext _ctx;
    private IEntityContext _currentTarget;
    private readonly List<IEntityContext> _currentTargets = new List<IEntityContext>();
    private readonly List<HealCandidate> _inAttackRangeCandidates = new List<HealCandidate>();
    private readonly List<HealCandidate> _outsideAttackRangeCandidates = new List<HealCandidate>();
    private Fix64 _scanTimer;

    public IEntityContext CurrentTarget
    {
        get => _currentTarget;
        set => _currentTarget = value;
    }

    public IEntityContext FollowTarget { get; private set; }
    public IReadOnlyList<IEntityContext> CurrentTargets => _currentTargets;
    private Fix64 m_AggroRange = (Fix64)6;
    private Fix64 m_ForgetRange = (Fix64)8;
    private Fix64 m_FollowSearchRange = (Fix64)30;
    private Fix64 m_AlertRadius = (Fix64)5;
    public Fix64 AggroRangeFixed { get => m_AggroRange; set => m_AggroRange = LogicTargetingRange.Require(value, nameof(AggroRangeFixed)); }
    public Fix64 ForgetRangeFixed { get => m_ForgetRange; set => m_ForgetRange = LogicTargetingRange.Require(value, nameof(ForgetRangeFixed)); }
    public Fix64 FollowSearchRangeFixed { get => m_FollowSearchRange; set => m_FollowSearchRange = LogicTargetingRange.Require(value, nameof(FollowSearchRangeFixed)); }
    public Fix64 AlertRadiusFixed { get => m_AlertRadius; set => m_AlertRadius = LogicTargetingRange.Require(value, nameof(AlertRadiusFixed)); }

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        CurrentTarget = null;
        FollowTarget = null;
        _scanTimer = Fix64.Zero;
    }

    public void UpdateTargeting(Fix64 deltaTime)
    {
        if (_ctx == null)
            return;

        MaintainCurrentTarget();
        MaintainCurrentTargets();
        MaintainFollowTarget();

        _scanTimer += deltaTime;
        if (_scanTimer < ScanInterval)
            return;

        _scanTimer = Fix64.Zero;

        RebuildHealTargetsByRangePriority();

        if (FollowTarget == null)
            TryAcquireFollowTarget();
    }

    private void MaintainCurrentTarget()
    {
        if (CurrentTarget == null)
            return;

        if (!WeaponTargetRules.IsValidHealTarget(_ctx, CurrentTarget, requireDamaged: true))
            CurrentTarget = null;
    }

    private void MaintainCurrentTargets()
    {
        for (int i = _currentTargets.Count - 1; i >= 0; i--)
        {
            if (!WeaponTargetRules.IsValidHealTarget(_ctx, _currentTargets[i], requireDamaged: true))
                _currentTargets.RemoveAt(i);
        }
    }

    private void MaintainFollowTarget()
    {
        if (FollowTarget == null)
            return;

        if (!FollowTarget.IsRegisteredInLogicWorld() || !FollowTarget.Alive)
        {
            FollowTarget = null;
            return;
        }

        Fix64 dist = _ctx.LogicFrameCenterDistanceFixed(FollowTarget);
        if (dist > m_FollowSearchRange)
            FollowTarget = null;
    }

    private void RebuildHealTargetsByRangePriority()
    {
        var all = EntityRegistry.AllEntities;
        if (all == null)
            throw new System.InvalidOperationException("HealTargetingComp.FindHealTargetByRangePriority failed: EntityRegistry.AllEntities is null.");

        Fix64 attackRange = GetEffectiveAttackRange();
        Fix64 scanRange = Fix64.Max(m_AggroRange, attackRange);
        int targetCount = ResolveTargetCount();
        _inAttackRangeCandidates.Clear();
        _outsideAttackRangeCandidates.Clear();

        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext candidate = all[i];
            if (candidate == null)
                continue;
            if (!WeaponTargetRules.IsValidHealTarget(_ctx, candidate, requireDamaged: true))
                continue;
            Fix64 distance = _ctx.LogicFrameDistanceToTargetSurfaceFixed(candidate);
            if (distance > scanRange)
                continue;

            Fix64 hpRatio = candidate.HealthRatioFixed();
            if (distance <= attackRange)
            {
                InsertHealCandidate(_inAttackRangeCandidates, new HealCandidate(candidate, hpRatio, distance), targetCount);
            }
            else
            {
                InsertHealCandidate(_outsideAttackRangeCandidates, new HealCandidate(candidate, hpRatio, distance), targetCount);
            }
        }

        List<HealCandidate> selected = _inAttackRangeCandidates.Count > 0
            ? _inAttackRangeCandidates
            : _outsideAttackRangeCandidates;
        _currentTargets.Clear();
        for (int i = 0; i < selected.Count; i++)
            _currentTargets.Add(selected[i].Target);

        CurrentTarget = _currentTargets.Count > 0 ? _currentTargets[0] : null;
    }

    private static void InsertHealCandidate(List<HealCandidate> list, HealCandidate candidate, int maxCount)
    {
        int index = 0;
        while (index < list.Count && !IsBetterHealTarget(candidate, list[index]))
            index++;

        if (index >= maxCount)
            return;

        list.Insert(index, candidate);
        if (list.Count > maxCount)
            list.RemoveAt(list.Count - 1);
    }

    private static bool IsBetterHealTarget(HealCandidate candidate, HealCandidate current)
    {
        return candidate.HpRatio < current.HpRatio
               || (candidate.HpRatio == current.HpRatio
                   && (candidate.Distance < current.Distance
                       || (candidate.Distance == current.Distance
                           && candidate.Target.LogicEntityId < current.Target.LogicEntityId)));
    }

    private int ResolveTargetCount()
    {
        Fix64 count = _ctx?.WeaponComp?.Data != null ? _ctx.WeaponComp.Data.ProjectileCount : Fix64.One;
        int result = (int)count;
        return Mathf.Max(1, result);
    }

    private void TryAcquireFollowTarget()
    {
        var player = EntityRegistry.Player;
        if (player == null || !player.Alive || player.Side != _ctx.Side)
            return;

        Fix64 dist = _ctx.LogicFrameCenterDistanceFixed(player);
        if (dist <= m_FollowSearchRange)
            FollowTarget = player;
    }

    private Fix64 GetEffectiveAttackRange()
    {
        Fix64 weaponRange = _ctx.WeaponComp != null ? _ctx.WeaponComp.AttackRange : Fix64.FromRaw(6144);
        return weaponRange;
    }

    public void NotifyDamageTaken(IEntityContext attacker)
    {
    }

    public void NotifyAllyFoundEnemy(IEntityContext enemy)
    {
    }

    public void ClearAggro()
    {
    }

    public void ShutDown()
    {
        CurrentTarget = null;
        _currentTargets.Clear();
        FollowTarget = null;
    }

    public void Resume()
    {
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new System.ArgumentNullException(nameof(hasher));
        hasher.Add(_scanTimer.RawValue);
        hasher.Add(FollowTarget != null && FollowTarget.LogicEntityId.IsValid ? FollowTarget.LogicEntityId.Value : 0);
        hasher.Add(m_AggroRange.RawValue);
        hasher.Add(m_ForgetRange.RawValue);
        hasher.Add(m_FollowSearchRange.RawValue);
        hasher.Add(_currentTargets.Count);
        for (int i = 0; i < _currentTargets.Count; i++)
        {
            IEntityContext target = _currentTargets[i];
            if (target == null || !target.LogicEntityId.IsValid)
                throw new System.InvalidOperationException($"HealTargetingComp contains an invalid target at index {i}.");
            hasher.Add(target.LogicEntityId.Value);
        }
    }

    private readonly struct HealCandidate
    {
        public readonly IEntityContext Target;
        public readonly Fix64 HpRatio;
        public readonly Fix64 Distance;

        public HealCandidate(IEntityContext target, Fix64 hpRatio, Fix64 distance)
        {
            Target = target;
            HpRatio = hpRatio;
            Distance = distance;
        }
    }
}
