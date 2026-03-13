using UnityEngine;

public class CharacterTargetingComp : ITargetingComp
{
    private MAEntity _entity;
    public CompCreature CurrentTarget { get; private set; }
    public CompCreature FollowTarget { get; private set; } // 新增

    public float AggroRange { get; set; } = 6f;
    public float ForgetRange { get; set; } = 8f;
    public float FollowSearchRange { get; set; } = 30f; // 默认给一个很大的寻找范围

    private float _scanTimer = 0f;
    private const float SCAN_INTERVAL = 0.2f;

    public void Init(MAEntity entity)
    {
        _entity = entity;
        CurrentTarget = null;
        FollowTarget = null;
        _scanTimer = 0f;
    }

    public void UpdateTargeting()
    {
        if (_entity == null) return;

        // 1. 维护当前敌人目标
        if (CurrentTarget != null)
        {
            float dist = Vector3.Distance(_entity.transform.position, CurrentTarget.transform.position);
            if (dist > ForgetRange) CurrentTarget = null;
        }

        // 2. 维护跟随目标 (玩家)
        if (FollowTarget != null)
        {
            float dist = Vector3.Distance(_entity.transform.position, FollowTarget.transform.position);
            // 如果玩家跑得太远了，丢失目标
            if (dist > FollowSearchRange) FollowTarget = null; 
        }

        // 3. 降频扫描新目标
        _scanTimer += Time.deltaTime;
        if (_scanTimer >= SCAN_INTERVAL)
        {
            _scanTimer = 0f;
            
            if (CurrentTarget == null)
                CurrentTarget = SimpleTargeting.FindNearestEnemy(_entity, AggroRange);

            if (FollowTarget == null)
                FollowTarget = SimpleTargeting.FindNearestPlayer(_entity, FollowSearchRange);
        }
    }

    public void ShutDown() 
    { 
        CurrentTarget = null;
        FollowTarget = null; 
    }
    public void Resume() { }
}