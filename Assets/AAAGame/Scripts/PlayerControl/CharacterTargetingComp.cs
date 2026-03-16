using UnityEngine;

public class CharacterTargetingComp : ITargetingComp
{
    private IEntityContext _ctx;
    public IEntityContext CurrentTarget { get; private set; }
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
            if (dist > ForgetRange || !CurrentTarget.Alive) CurrentTarget = null;
        }

        // 2. 维护跟随目标
        if (FollowTarget != null)
        {
            float dist = Vector3.Distance(_ctx.Position, FollowTarget.Position);
            if (dist > FollowSearchRange || !FollowTarget.Alive) FollowTarget = null;
        }

        // 3. 降频扫描新目标（仅真实实体使用 SimpleTargeting）
        _scanTimer += deltaTime;
        if (_scanTimer >= SCAN_INTERVAL)
        {
            _scanTimer = 0f;

            if (_ctx is MAEntity ma)
            {
                if (CurrentTarget == null)
                {
                    var enemy = SimpleTargeting.FindNearestEnemy(ma, AggroRange);
                    if (enemy is IEntityContext ec) CurrentTarget = ec;
                }

                if (FollowTarget == null)
                {
                    var player = SimpleTargeting.FindNearestPlayer(ma, FollowSearchRange);
                    if (player is IEntityContext ec) FollowTarget = ec;
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
