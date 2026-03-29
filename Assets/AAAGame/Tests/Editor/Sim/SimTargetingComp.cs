using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 纯逻辑目标组件：从已知列表中按距离查找目标，不依赖 Physics。
/// </summary>
public class SimTargetingComp : ITargetingComp
{
    private IEntityContext _self;
    private List<IEntityContext> _allEntities;

    public IEntityContext CurrentTarget { get; set; }
    public IEntityContext FollowTarget { get; private set; }

    public float AggroRange { get; set; } = 6f;
    public float ForgetRange { get; set; } = 8f;
    public float FollowSearchRange { get; set; } = 30f;

    private float _scanTimer = 0f;
    private const float SCAN_INTERVAL = 0.2f;

    public SimTargetingComp(IEntityContext self, List<IEntityContext> allEntities)
    {
        _self = self;
        _allEntities = allEntities;
    }

    public void Init(IEntityContext ctx)
    {
        _self = ctx;
        CurrentTarget = null;
        FollowTarget = null;
        _scanTimer = 0f;
    }

    /// <summary>
    /// 更新已知实体列表（外部可随时添加/移除）
    /// </summary>
    public void SetEntities(List<IEntityContext> entities)
    {
        _allEntities = entities;
    }

    public void UpdateTargeting(float deltaTime)
    {
        if (_self == null || _allEntities == null) return;

        // 1. 维护当前目标
        if (CurrentTarget != null)
        {
            float dist = Vector3.Distance(_self.Position, CurrentTarget.Position);
            if (dist > ForgetRange || !CurrentTarget.Alive)
                CurrentTarget = null;
        }

        // 2. 维护跟随目标
        if (FollowTarget != null)
        {
            float dist = Vector3.Distance(_self.Position, FollowTarget.Position);
            if (dist > FollowSearchRange || !FollowTarget.Alive)
                FollowTarget = null;
        }

        // 3. 降频扫描
        _scanTimer += deltaTime;
        if (_scanTimer >= SCAN_INTERVAL)
        {
            _scanTimer = 0f;

            if (CurrentTarget == null)
            {
                CurrentTarget = _allEntities
                    .Where(e => e != _self && e.Side != _self.Side && e.Side != SideType.NoSide && e.Alive)
                    .Where(e => Vector3.Distance(_self.Position, e.Position) <= AggroRange)
                    .OrderBy(e => Vector3.Distance(_self.Position, e.Position))
                    .FirstOrDefault();
            }

            if (FollowTarget == null)
            {
                FollowTarget = _allEntities
                    .Where(e => e != _self && e.Side == _self.Side && e.Alive)
                    .Where(e => Vector3.Distance(_self.Position, e.Position) <= FollowSearchRange)
                    .OrderBy(e => Vector3.Distance(_self.Position, e.Position))
                    .FirstOrDefault();
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
