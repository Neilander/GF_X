using System.Collections.Generic;
using UnityEngine;

public class CharacterTargetingComp : ITargetingComp
{
    private enum TargetingMode
    {
        Default = 0,
        DefendEnemy = 1
    }

    private IEntityContext _ctx;
    private IEntityContext _currentTarget;
    private TargetingMode _targetingMode;
    private IEntityContext _defendFallbackTarget;
    public IEntityContext CurrentTarget
    {
        get => _currentTarget;
        set => _currentTarget = value;
    }
    public IEntityContext FollowTarget { get; private set; }

    public float AggroRange { get; set; } = 6f;
    public float ForgetRange { get; set; } = 8f;
    public float FollowSearchRange { get; set; } = 30f;
    public float AlertRadius { get; set; } = 5f;

    /// <summary>
    /// 是否启用"视线外仇恨"。建筑等不应被拉走，可关掉。
    /// </summary>
    public bool EnableAggroFallback { get; set; } = true;

    /// <summary>视线外仇恨：scan 找不到目标时 fallback 到这个 attacker。第一次受击锁定，nearest 切到别的目标后清掉。</summary>
    private IEntityContext _lastAttacker;

    private float _scanTimer = 0f;
    private const float SCAN_INTERVAL = 0.2f;

    public void UseDefaultMode()
    {
        _targetingMode = TargetingMode.Default;
        _defendFallbackTarget = null;
    }

    public void UseDefendEnemyMode(IEntityContext fallbackTarget)
    {
        _targetingMode = TargetingMode.DefendEnemy;
        _defendFallbackTarget = fallbackTarget;
        _lastAttacker = null;
    }

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        CurrentTarget = null;
        FollowTarget = null;
        _lastAttacker = null;
        _scanTimer = 0f;
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
        if (enemy == null || AlertRadius <= 0f) return;
        float r2 = AlertRadius * AlertRadius;
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            var ally = all[i];
            if (ally == null || ReferenceEquals(ally, _ctx)) continue;
            if (ally.Side != _ctx.Side) continue;
            if (!ally.Alive) continue;
            Vector3 d = ally.Position - _ctx.Position;
            d.y = 0f;
            if (d.sqrMagnitude > r2) continue;
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

    public void UpdateTargeting(float deltaTime)
    {
        if (_ctx == null) return;
        if (_targetingMode == TargetingMode.DefendEnemy)
        {
            UpdateDefendEnemyTargeting(deltaTime);
            return;
        }

        bool useAttackRangeOnlyForThisUnit = ShouldUseAttackRangeOnly(_ctx);
        float effectiveAttackRange = GetEffectiveAttackRange();

        float currentTargetDist = float.PositiveInfinity;
        int currentTargetTaunt = -1;

        // 1. 维护当前敌人目标
        if (CurrentTarget != null)
        {
            float dist = _ctx.DistanceToTargetSurface(CurrentTarget);
            currentTargetDist = dist;
            currentTargetTaunt = GetTauntLevel(CurrentTarget);
            float targetRetentionRange = useAttackRangeOnlyForThisUnit ? effectiveAttackRange : ForgetRange;
            // 视线外仇恨特例：CurrentTarget 是 fallback 来的 attacker → 跳过距离过滤，让单位一路追上去
            bool isAggroFallback = !useAttackRangeOnlyForThisUnit && (CurrentTarget == _lastAttacker);
            bool dropByDistance = !isAggroFallback && dist > targetRetentionRange;
            if (dropByDistance || !CurrentTarget.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(_ctx, CurrentTarget))
            {
                GameDebugSettings.Log(DebugCategory.Targeting,
                    $"{_ctx} 丢失敌人目标 {CurrentTarget} | dist={dist:F1} retentionRange={targetRetentionRange:F1} alive={CurrentTarget.Alive}");
                CurrentTarget = null;
                currentTargetDist = float.PositiveInfinity;
                currentTargetTaunt = -1;
                if (useAttackRangeOnlyForThisUnit && _lastAttacker != null)
                    ClearAggro();
            }
        }

        // 2. 维护跟随目标
        if (FollowTarget != null)
        {
            float dist = Vector3.Distance(_ctx.Position, FollowTarget.Position);
            if (dist > FollowSearchRange || !FollowTarget.Alive)
            {
                GameDebugSettings.Log(DebugCategory.Targeting, $"{_ctx} 丢失跟随目标 {FollowTarget} | dist={dist:F1} followRange={FollowSearchRange} alive={FollowTarget.Alive}");
                FollowTarget = null;
            }
        }

        // 3. 降频扫描新目标（仅真实实体使用 SimpleTargeting）
        _scanTimer += deltaTime;
        if (_scanTimer >= SCAN_INTERVAL)
        {
            _scanTimer = 0f;

            // 找敌人：遍历 EntityRegistry，嘲讽等级优先，同等级选最近
            IEntityContext nearest = null;
            float scanRange = useAttackRangeOnlyForThisUnit
                ? effectiveAttackRange
                : Mathf.Max(AggroRange, effectiveAttackRange);
            float nearestDist = scanRange;
            int nearestTaunt = -1;

            // 攻击中抢目标：额外记录攻击范围内“嘲讽最高、同嘲讽最近”的候选
            IEntityContext inAttackRangeCandidate = null;
            float inAttackRangeDist = effectiveAttackRange;
            int inAttackRangeTaunt = -1;

            var all = EntityRegistry.AllEntities;
            for (int i = 0; i < all.Count; i++)
            {
                var other = all[i];
                if (other == _ctx) continue;
                if (!other.IsAttackTargetable()) continue;
                if (!EntityCombatTeamHelper.IsEnemy(_ctx, other)) continue;

                float dist = _ctx.DistanceToTargetSurface(other);
                if (dist >= scanRange) continue;

                // 读嘲讽等级
                int taunt = GetTauntLevel(other);

                // 嘲讽等级更高 → 无条件替换
                // 嘲讽等级相同 → 选更近的
                if (taunt > nearestTaunt || (taunt == nearestTaunt && dist < nearestDist))
                {
                    nearestTaunt = taunt;
                    nearestDist = dist;
                    nearest = other;
                }

                if (dist <= effectiveAttackRange
                    && (taunt > inAttackRangeTaunt || (taunt == inAttackRangeTaunt && dist < inAttackRangeDist)))
                {
                    inAttackRangeTaunt = taunt;
                    inAttackRangeDist = dist;
                    inAttackRangeCandidate = other;
                }
            }

            if (CurrentTarget == null)
            {
                if (nearest != null)
                {
                    GameDebugSettings.Log(DebugCategory.Targeting, $"{_ctx} 锁定敌人 {nearest} | dist={nearestDist:F1} scanRange={scanRange:F1}");
                    CurrentTarget = nearest;
                    // 走正常索敌了，受击仇恨记忆作废（即使 nearest 就是 _lastAttacker 本人，也清掉，让后续切换走正常规则）
                    if (_lastAttacker != null) ClearAggro();
                }
                else if (!useAttackRangeOnlyForThisUnit && EnableAggroFallback && IsLastAttackerStillValid())
                {
                    // 视线外仇恨 fallback：scan 范围空，回去打打过自己的人
                    CurrentTarget = _lastAttacker;
                    GameDebugSettings.Log(DebugCategory.Targeting,
                        $"{_ctx} fallback 到受击 attacker {_lastAttacker} | dist={_ctx.DistanceToTargetSurface(_lastAttacker):F1}");
                }
            }
            else if (nearest != null && nearest != CurrentTarget)
            {
                bool isAttacking = _ctx.AtkComp != null && _ctx.AtkComp.IsAttacking;
                bool sameTaunt = nearestTaunt == currentTargetTaunt;
                bool higherTaunt = nearestTaunt > currentTargetTaunt;

                // 规则：
                // 1) 攻击前：同嘲讽仅切更近；不同嘲讽可在大范围内切更高嘲讽。
                // 2) 攻击中：只允许在攻击范围内切到更高嘲讽目标。
                bool switchByCloserBeforeAttack = !isAttacking && sameTaunt && (nearestDist + 0.1f < currentTargetDist);
                bool switchByHigherTauntBeforeAttack = !isAttacking && higherTaunt;
                bool switchByHigherTauntInAttackRange = isAttacking
                                                       && inAttackRangeCandidate != null
                                                       && inAttackRangeCandidate != CurrentTarget
                                                       && inAttackRangeTaunt > currentTargetTaunt;

                IEntityContext switchTarget = null;
                float switchDist = 0f;
                int switchTaunt = 0;
                string switchReason = null;

                if (switchByHigherTauntInAttackRange)
                {
                    switchTarget = inAttackRangeCandidate;
                    switchDist = inAttackRangeDist;
                    switchTaunt = inAttackRangeTaunt;
                    switchReason = "attacking_higher_taunt_in_attack_range";
                }
                else if (switchByHigherTauntBeforeAttack || switchByCloserBeforeAttack)
                {
                    switchTarget = nearest;
                    switchDist = nearestDist;
                    switchTaunt = nearestTaunt;
                    switchReason = switchByHigherTauntBeforeAttack
                        ? "pre_attack_higher_taunt_in_scan_range"
                        : "pre_attack_same_taunt_closer_target";
                }

                if (switchTarget != null)
                {
                    GameDebugSettings.Log(DebugCategory.Targeting,
                        $"{_ctx} 切换敌人 {CurrentTarget} -> {switchTarget} | currentDist={currentTargetDist:F1} newDist={switchDist:F1} currentTaunt={currentTargetTaunt} newTaunt={switchTaunt} attacking={isAttacking} reason={switchReason}");
                    CurrentTarget = switchTarget;
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
                    float dist = Vector3.Distance(_ctx.Position, player.Position);
                    if (dist <= FollowSearchRange)
                    {
                        GameDebugSettings.Log(DebugCategory.Targeting, $"{_ctx} 锁定跟随目标 {player} | dist={dist:F1} followRange={FollowSearchRange}");
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
        _defendFallbackTarget = null;
        _targetingMode = TargetingMode.Default;
    }
    public void Resume() { }

    private void UpdateDefendEnemyTargeting(float deltaTime)
    {
        float effectiveAttackRange = GetEffectiveAttackRange();
        float scanRange = Mathf.Max(AggroRange, effectiveAttackRange);

        _scanTimer += deltaTime;
        if (_scanTimer < SCAN_INTERVAL)
            return;

        _scanTimer = 0f;

        IEntityContext bestTarget = null;
        float bestDistance = float.PositiveInfinity;
        int bestPriority = int.MaxValue;

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

            float distance = _ctx.DistanceToTargetSurface(other);
            if (distance > scanRange)
                continue;

            int priority = GetDefendEnemyPriority(other);
            if (priority < 0)
                continue;

            if (priority < bestPriority || (priority == bestPriority && distance < bestDistance))
            {
                bestPriority = priority;
                bestDistance = distance;
                bestTarget = other;
            }
        }

        IEntityContext desiredTarget = bestTarget;
        if (desiredTarget == null && IsDefendFallbackTargetValid())
            desiredTarget = _defendFallbackTarget;

        if (CurrentTarget != null && desiredTarget == null && IsCurrentDefendTargetStillValid(CurrentTarget, scanRange))
            return;

        if (!ReferenceEquals(CurrentTarget, desiredTarget))
            CurrentTarget = desiredTarget;
    }

    private bool IsCurrentDefendTargetStillValid(IEntityContext target, float scanRange)
    {
        if (target == null || !target.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(_ctx, target))
            return false;

        if (ReferenceEquals(target, _defendFallbackTarget))
            return IsDefendFallbackTargetValid();

        int priority = GetDefendEnemyPriority(target);
        if (priority < 0)
            return false;

        float distance = _ctx.DistanceToTargetSurface(target);
        return distance <= Mathf.Max(ForgetRange, scanRange);
    }

    private bool IsDefendFallbackTargetValid()
    {
        if (_defendFallbackTarget == null)
            return false;

        if (!_defendFallbackTarget.IsAttackTargetable())
            return false;

        return EntityCombatTeamHelper.IsEnemy(_ctx, _defendFallbackTarget);
    }

    private int GetDefendEnemyPriority(IEntityContext target)
    {
        if (target == null)
            return -1;

        if (target is BuildingEntity building)
        {
            int taunt = GetTauntLevel(target);
            if (taunt > 0)
                return 1;

            if (building.buildingData != null && building.buildingData.Type == BuilType.Def)
                return 3;

            if (ReferenceEquals(target, _defendFallbackTarget))
                return 4;

            return -1;
        }

        int unitTaunt = GetTauntLevel(target);
        return unitTaunt > 1 ? 0 : 2;
    }

    private float GetEffectiveAttackRange()
    {
        Fix64 weaponRange = _ctx.WeaponComp != null ? _ctx.WeaponComp.AttackRange : (Fix64)1.5f;
        return (float)weaponRange;
    }

    private static int GetTauntLevel(IEntityContext entity)
    {
        if (entity is GeneralCreature creature)
            return creature.TauntLevel;

        return 0;
    }

    private static bool ShouldUseAttackRangeOnly(IEntityContext entity)
    {
        return entity is BuildingEntity || IsHeroUnit(entity);
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

        return entity != null && entity.CharacterKey == UnitType.Unit_Hero.ToString();
    }

}

public sealed class HealTargetingComp : ITargetingComp, IMultiTargetingComp
{
    private const float ScanInterval = 0.2f;

    private IEntityContext _ctx;
    private IEntityContext _currentTarget;
    private readonly List<IEntityContext> _currentTargets = new List<IEntityContext>();
    private float _scanTimer;

    public IEntityContext CurrentTarget
    {
        get => _currentTarget;
        set => _currentTarget = value;
    }

    public IEntityContext FollowTarget { get; private set; }
    public IReadOnlyList<IEntityContext> CurrentTargets => _currentTargets;
    public float AggroRange { get; set; } = 6f;
    public float ForgetRange { get; set; } = 8f;
    public float FollowSearchRange { get; set; } = 30f;
    public float AlertRadius { get; set; } = 5f;

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        CurrentTarget = null;
        FollowTarget = null;
        _scanTimer = 0f;
    }

    public void UpdateTargeting(float deltaTime)
    {
        if (_ctx == null)
            return;

        MaintainCurrentTarget();
        MaintainCurrentTargets();
        MaintainFollowTarget();

        _scanTimer += deltaTime;
        if (_scanTimer < ScanInterval)
            return;

        _scanTimer = 0f;

        RebuildHealTargetsByRangePriority();

        if (FollowTarget == null)
            TryAcquireFollowTarget();
    }

    private void MaintainCurrentTarget()
    {
        if (CurrentTarget == null)
            return;

        if (!WeaponTargetRules.IsValidHealTarget(_ctx, CurrentTarget, requireDamaged: true)
            || !HealingTargetFilterService.IsValidHealTargetForHealer(_ctx, CurrentTarget))
            CurrentTarget = null;
    }

    private void MaintainCurrentTargets()
    {
        for (int i = _currentTargets.Count - 1; i >= 0; i--)
        {
            if (!WeaponTargetRules.IsValidHealTarget(_ctx, _currentTargets[i], requireDamaged: true)
                || !HealingTargetFilterService.IsValidHealTargetForHealer(_ctx, _currentTargets[i]))
                _currentTargets.RemoveAt(i);
        }
    }

    private void MaintainFollowTarget()
    {
        if (FollowTarget == null)
            return;

        float dist = Vector3.Distance(_ctx.Position, FollowTarget.Position);
        if (dist > FollowSearchRange || !FollowTarget.Alive)
            FollowTarget = null;
    }

    private void RebuildHealTargetsByRangePriority()
    {
        var all = EntityRegistry.AllEntities;
        if (all == null)
            throw new System.InvalidOperationException("HealTargetingComp.FindHealTargetByRangePriority failed: EntityRegistry.AllEntities is null.");

        float attackRange = GetEffectiveAttackRange();
        float scanRange = Mathf.Max(AggroRange, attackRange);
        int targetCount = ResolveTargetCount();
        var inAttackRange = new List<HealCandidate>(targetCount);
        var outsideAttackRange = new List<HealCandidate>(targetCount);

        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext candidate = all[i];
            if (candidate == null)
                continue;
            if (!WeaponTargetRules.IsValidHealTarget(_ctx, candidate, requireDamaged: true))
                continue;
            if (!HealingTargetFilterService.IsValidHealTargetForHealer(_ctx, candidate))
                continue;

            float distance = _ctx.DistanceToTargetSurface(candidate);
            if (distance > scanRange)
                continue;

            float hpRatio = candidate.HealthRatio();
            if (distance <= attackRange)
            {
                InsertHealCandidate(inAttackRange, new HealCandidate(candidate, hpRatio, distance), targetCount);
            }
            else
            {
                InsertHealCandidate(outsideAttackRange, new HealCandidate(candidate, hpRatio, distance), targetCount);
            }
        }

        var selected = inAttackRange.Count > 0 ? inAttackRange : outsideAttackRange;
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
               || (Mathf.Approximately(candidate.HpRatio, current.HpRatio) && candidate.Distance < current.Distance);
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

        float dist = Vector3.Distance(_ctx.Position, player.Position);
        if (dist <= FollowSearchRange)
            FollowTarget = player;
    }

    private float GetEffectiveAttackRange()
    {
        Fix64 weaponRange = _ctx.WeaponComp != null ? _ctx.WeaponComp.AttackRange : (Fix64)1.5f;
        return (float)weaponRange;
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

    private readonly struct HealCandidate
    {
        public readonly IEntityContext Target;
        public readonly float HpRatio;
        public readonly float Distance;

        public HealCandidate(IEntityContext target, float hpRatio, float distance)
        {
            Target = target;
            HpRatio = hpRatio;
            Distance = distance;
        }
    }
}
