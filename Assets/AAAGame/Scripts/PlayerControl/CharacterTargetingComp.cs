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

        // 1. 维护当前敌人目标
        if (CurrentTarget != null)
        {
            float dist = Vector3.Distance(_ctx.Position, CurrentTarget.Position);
            if (dist > ForgetRange || !CurrentTarget.Alive || !EntityCombatTeamHelper.IsEnemy(_ctx, CurrentTarget))
            {
                GameDebugSettings.Log(DebugCategory.Targeting, $"{_ctx} 丢失敌人目标 {CurrentTarget} | dist={dist:F1} forgetRange={ForgetRange} alive={CurrentTarget.Alive}");
                CurrentTarget = null;
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
            if (CurrentTarget == null)
            {
                IEntityContext nearest = null;
                float nearestDist = AggroRange;
                var all = EntityRegistry.AllEntities;
                for (int i = 0; i < all.Count; i++)
                {
                    var other = all[i];
                    if (other == _ctx || !other.Alive) continue;
                    if (!EntityCombatTeamHelper.IsEnemy(_ctx, other)) continue;

                    float dist = Vector3.Distance(_ctx.Position, other.Position);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = other;
                    }
                }
                if (nearest != null)
                    GameDebugSettings.Log(DebugCategory.Targeting, $"{_ctx} 锁定敌人 {nearest} | dist={nearestDist:F1} aggroRange={AggroRange}");
                CurrentTarget = nearest;
            }

            // 找跟随目标：同阵营的领袖/玩家
            if (FollowTarget == null)
            {
                var player = EntityRegistry.Player;
                if (player != null && player.Alive && EntityCombatTeamHelper.IsAlly(_ctx, player))
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
}
