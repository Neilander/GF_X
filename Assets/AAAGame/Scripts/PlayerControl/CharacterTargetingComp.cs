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

        float currentTargetDist = float.PositiveInfinity;

        // 1. 维护当前敌人目标
        if (CurrentTarget != null)
        {
            float dist = _ctx.DistanceToTargetSurface(CurrentTarget);
            currentTargetDist = dist;
            if (dist > ForgetRange || !CurrentTarget.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(_ctx, CurrentTarget))
            {
                GameDebugSettings.Log(DebugCategory.Targeting, $"{_ctx} 丢失敌人目标 {CurrentTarget} | dist={dist:F1} forgetRange={ForgetRange} alive={CurrentTarget.Alive}");
                CurrentTarget = null;
                currentTargetDist = float.PositiveInfinity;
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

            // 找敌人：遍历 EntityRegistry，按阵营和距离
            IEntityContext nearest = null;
            float scanRange = Mathf.Max(AggroRange, GetEffectiveAttackRange());
            float nearestDist = scanRange;
            var all = EntityRegistry.AllEntities;
            for (int i = 0; i < all.Count; i++)
            {
                var other = all[i];
                if (other == _ctx) continue;
                if (!other.IsAttackTargetable()) continue;
                if (!EntityCombatTeamHelper.IsEnemy(_ctx, other)) continue;

                float dist = _ctx.DistanceToTargetSurface(other);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = other;
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
                bool currentOutOfAttackRange = currentTargetDist > GetEffectiveAttackRange();
                bool nearestObviouslyBetter = nearestDist + 0.1f < currentTargetDist;
                if (currentOutOfAttackRange || nearestObviouslyBetter)
                {
                    GameDebugSettings.Log(DebugCategory.Targeting,
                        $"{_ctx} 切换敌人 {CurrentTarget} -> {nearest} | currentDist={currentTargetDist:F1} nearestDist={nearestDist:F1}");
                    CurrentTarget = nearest;
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
        float equilibriumRadius = 0f;

        if (GroupMoveManager.HasInstance)
        {
            int selfId = (_ctx as MAEntity)?.GetInstanceID() ?? _ctx.GetHashCode();
            equilibriumRadius = GroupMoveManager.Instance.Coordinator.GetAgentEquilibriumRadius(selfId);
        }

        return (float)weaponRange + equilibriumRadius;
    }
}
