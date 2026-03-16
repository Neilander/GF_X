using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 纯数据实体上下文：不依赖 Unity MonoBehaviour，用于测试。
/// </summary>
public class SimEntityContext : IEntityContext
{
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; } = Quaternion.identity;
    public SideType Side { get; set; }
    public bool Alive { get; set; } = true;
    public string ReferenceId { get; set; } = "TestUnit";

    public HealthContainer Health { get; private set; } = new HealthContainer();

    public IControlBrain Brain { get; set; }

    private IMoveExecutor _moveExecutor;
    public IMoveExecutor MoveExecutor
    {
        get => _moveExecutor;
        set
        {
            _moveExecutor = value;
            // 自动同步位置引用
            if (_moveExecutor is SimMoveExecutor sim)
                sim.Position = Position;
        }
    }

    public IMoveComp MoveComp { get; set; }
    public IAtkComp AtkComp { get; set; }
    public ITargetingComp TargetComp { get; set; }

    // 属性系统
    private Dictionary<CreatureMainProperty, float> _properties = new Dictionary<CreatureMainProperty, float>();

    public float GetProperty(CreatureMainProperty prop)
    {
        return _properties.TryGetValue(prop, out float val) ? val : 5f;
    }

    public void SetProperty(CreatureMainProperty prop, float val)
    {
        _properties[prop] = val;
    }

    public void TakeDamage(float damage, HealthModifyType modType)
    {
        Health.ModifyHealth(modType, damage, false);
        if (Health.currentHealth <= 0f)
            Alive = false;
    }

    // 组件锁定（复用 CompCreature 的纯逻辑）
    private Dictionary<ICapability, List<ICapability>> _compLockers = new Dictionary<ICapability, List<ICapability>>();

    public bool CanRun(ICapability cap)
    {
        if (cap == null) return false;
        return !_compLockers.ContainsKey(cap);
    }

    public void LockComp(ICapability toLock, ICapability locker)
    {
        if (toLock == null || locker == null) return;

        if (_compLockers.TryGetValue(toLock, out var lockers))
        {
            if (!lockers.Contains(locker))
                lockers.Add(locker);
        }
        else
        {
            var list = new List<ICapability> { locker };
            _compLockers.Add(toLock, list);
            toLock.ShutDown();
        }
    }

    public void ResumeComp(ICapability toResume, ICapability locker)
    {
        if (toResume == null || locker == null) return;
        if (!_compLockers.TryGetValue(toResume, out var lockers)) return;
        if (!lockers.Contains(locker)) return;

        lockers.Remove(locker);
        if (lockers.Count == 0)
        {
            _compLockers.Remove(toResume);
            toResume.Resume();
        }
    }

    /// <summary>
    /// 同步 SimMoveExecutor 的位置到 SimEntityContext.Position。
    /// 在每次 Execute 后调用。
    /// </summary>
    public void SyncPositionFromExecutor()
    {
        if (_moveExecutor is SimMoveExecutor sim)
        {
            Position = sim.Position;
        }
    }

    /// <summary>
    /// 将当前 Position 写入 SimMoveExecutor（在 Execute 前调用）。
    /// </summary>
    public void SyncPositionToExecutor()
    {
        if (_moveExecutor is SimMoveExecutor sim)
        {
            sim.Position = Position;
        }
    }
}
