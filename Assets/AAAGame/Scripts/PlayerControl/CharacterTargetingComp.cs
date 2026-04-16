using UnityEngine;

public class CharacterTargetingComp : ITargetingComp
{
    private IEntityContext _ctx;
    public IEntityContext CurrentTarget { get; set; }
    public IEntityContext FollowTarget { get; private set; }

    public float AggroRange { get; set; } = 6f;
    public float ForgetRange { get; set; } = 8f;
    public float FollowSearchRange { get; set; } = 30f;

    private float _scanTimer = 0f;
    private const float SCAN_INTERVAL = 0.2f;

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        CurrentTarget = null;
        FollowTarget = null;
        _scanTimer = 0f;
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
            if (dist > targetRetentionRange || !CurrentTarget.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(_ctx, CurrentTarget))
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
                    GameDebugSettings.Log(DebugCategory.Targeting, $"{_ctx} 锁定敌人 {nearest} | dist={nearestDist:F1} scanRange={scanRange:F1}");
                CurrentTarget = nearest;
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
        }
    }

    public void ShutDown()
    {
        CurrentTarget = null;
        FollowTarget = null;
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
