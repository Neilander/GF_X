using UnityEngine;

public class CharacterTargetingComp : ITargetingComp
{
    private IEntityContext _ctx;
    public IEntityContext CurrentTarget { get; set; }
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

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        CurrentTarget = null;
        FollowTarget = null;
        _lastAttacker = null;
        _scanTimer = 0f;
    }

    public void NotifyDamageTaken(IEntityContext attacker)
    {
        UnityEngine.Debug.Log($"[AggroDamage] {_ctx?.CharacterKey ?? "null"} 被打 attacker={(attacker == null ? "null" : attacker.CharacterKey)} EnableFallback={EnableAggroFallback}\nStackTrace:\n{System.Environment.StackTrace}");

        // 基础校验
        if (attacker == null || attacker == _ctx)
        {
            UnityEngine.Debug.Log($"[AggroDamage] {_ctx?.CharacterKey} 跳过：attacker null 或 == self");
            return;
        }
        if (!attacker.IsAttackTargetable())
        {
            UnityEngine.Debug.Log($"[AggroDamage] {_ctx.CharacterKey} 跳过：attacker {attacker.CharacterKey} 不可被攻击");
            return;
        }
        if (!EntityCombatTeamHelper.IsEnemy(_ctx, attacker))
        {
            UnityEngine.Debug.Log($"[AggroDamage] {_ctx.CharacterKey} 跳过：attacker {attacker.CharacterKey} 不是敌方阵营 (selfSide={_ctx.Side} attackerSide={attacker.Side})");
            return;
        }

        // 自己记 _lastAttacker（仅 EnableAggroFallback 时；建筑这里不记，原地反击）
        if (EnableAggroFallback && _lastAttacker == null)
        {
            _lastAttacker = attacker;
            UnityEngine.Debug.Log($"[AggroDamage] {_ctx.CharacterKey} 记下 _lastAttacker={attacker.CharacterKey}");
        }
        else if (!EnableAggroFallback)
        {
            UnityEngine.Debug.Log($"[AggroDamage] {_ctx.CharacterKey} 不记自己 _lastAttacker (EnableAggroFallback=false，建筑/特殊单位)");
        }
        else
        {
            UnityEngine.Debug.Log($"[AggroDamage] {_ctx.CharacterKey} 已有 _lastAttacker={_lastAttacker.CharacterKey}，不覆盖");
        }

        // 广播给周围友军：让附近士兵知道有人在打我（建筑被打也走这条路，召唤友军反击）
        BroadcastEnemyToAllies(attacker);
    }

    public void NotifyAllyFoundEnemy(IEntityContext enemy)
    {
        if (!EnableAggroFallback)
        {
            UnityEngine.Debug.Log($"[AggroRecv] {_ctx?.CharacterKey} 收告警但 EnableAggroFallback=false，忽略 enemy={(enemy == null ? "null" : enemy.CharacterKey)}");
            return;
        }
        if (_lastAttacker != null)
        {
            UnityEngine.Debug.Log($"[AggroRecv] {_ctx.CharacterKey} 收告警但已有 _lastAttacker={_lastAttacker.CharacterKey}，忽略 enemy={(enemy == null ? "null" : enemy.CharacterKey)}");
            return;
        }
        if (enemy == null || enemy == _ctx) return;
        if (!enemy.IsAttackTargetable())
        {
            UnityEngine.Debug.Log($"[AggroRecv] {_ctx.CharacterKey} 收告警但 enemy {enemy.CharacterKey} 不可被攻击");
            return;
        }
        if (!EntityCombatTeamHelper.IsEnemy(_ctx, enemy))
        {
            UnityEngine.Debug.Log($"[AggroRecv] {_ctx.CharacterKey} 收告警但 enemy {enemy.CharacterKey} 不是敌方 (selfSide={_ctx.Side} enemySide={enemy.Side})");
            return;
        }
        _lastAttacker = enemy;
        UnityEngine.Debug.Log($"[AggroRecv] {_ctx.CharacterKey} 接受告警，_lastAttacker={enemy.CharacterKey}@{enemy.Position}");
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
        if (enemy == null || AlertRadius <= 0f)
        {
            UnityEngine.Debug.Log($"[AggroBroadcast] {_ctx?.CharacterKey ?? "null"} 跳过广播：enemy={(enemy == null ? "null" : enemy.CharacterKey)} AlertRadius={AlertRadius}");
            return;
        }
        float r2 = AlertRadius * AlertRadius;
        int notified = 0;
        var notifiedNames = new System.Text.StringBuilder();
        int totalScanned = 0;
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            var ally = all[i];
            if (ally == null || ReferenceEquals(ally, _ctx)) continue;
            if (ally.Side != _ctx.Side) continue;
            if (!ally.Alive) continue;
            Vector3 d = ally.Position - _ctx.Position;
            d.y = 0f;
            float distSq = d.sqrMagnitude;
            totalScanned++;
            if (distSq > r2) continue;
            ally.TargetComp?.NotifyAllyFoundEnemy(enemy);
            if (notified > 0) notifiedNames.Append(", ");
            notifiedNames.Append($"{ally.CharacterKey}(d={Mathf.Sqrt(distSq):F1})");
            notified++;
        }
        UnityEngine.Debug.Log(
            $"[AggroBroadcast] src={_ctx.CharacterKey}@{_ctx.Position} side={_ctx.Side} " +
            $"radius={AlertRadius} enemyTarget={enemy.CharacterKey}@{enemy.Position} " +
            $"sameSideScanned={totalScanned} notified={notified} → [{notifiedNames}]");
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
            bool isAggroFallback = (CurrentTarget == _lastAttacker);
            bool dropByDistance = !isAggroFallback && dist > targetRetentionRange;
            if (dropByDistance || !CurrentTarget.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(_ctx, CurrentTarget))
            {
                GameDebugSettings.Log(DebugCategory.Targeting,
                    $"{_ctx} 丢失敌人目标 {CurrentTarget} | dist={dist:F1} retentionRange={targetRetentionRange:F1} alive={CurrentTarget.Alive}");
                CurrentTarget = null;
                currentTargetDist = float.PositiveInfinity;
                currentTargetTaunt = -1;
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
                else if (EnableAggroFallback && IsLastAttackerStillValid())
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
    }
    public void Resume() { }

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
