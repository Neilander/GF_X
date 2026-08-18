using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using AAAGame.Scripts.BuffSystem;

/// <summary>
/// 最小逻辑测试替身：仅用于不依赖真实实体帧管线的组件级单元测试。
/// </summary>
/// <remarks>
/// 此类型不经过 LogicEntityState、MAEntityLogicFrameSystem、实体生命周期或表现绑定。
/// 使用此类型通过的测试，不得单独作为运行时问题已修复的依据；运行时回归必须另用真实 LogicEntityState 链路验证。
/// </remarks>
public class SimEntityContext : IEntityContext, ITargetable
{
    private static int s_NextTestEntityId;

    public SimEntityContext()
    {
        LogicEntityId = new LogicEntityId(Interlocked.Increment(ref s_NextTestEntityId));
    }

    public LogicEntityId LogicEntityId { get; set; }
    private Vector3 _position;
    private FixVector2 _positionFixed;
    public FixVector2 PositionFixed
    {
        get => _positionFixed;
        set
        {
            _positionFixed = value;
            _position = new Vector3((float)value.x, _position.y, (float)value.y);
        }
    }
    public FixVector2 ForwardFixed
    {
        get
        {
            Vector3 forward = Rotation * Vector3.forward;
            return new FixVector2((Fix64)forward.x, (Fix64)forward.z).GetNormalized();
        }
    }
    public virtual LogicCombatShape CombatShape => LogicCombatShape.Circle(
        PositionFixed,
        DistanceUnitConverter.ConvertToWorld(GetProperty(CreatureMainProperty.CollisionRadius)));
    public Vector3 Position
    {
        get => _position;
        set
        {
            if (float.IsNaN(value.x) || float.IsInfinity(value.x)
                || float.IsNaN(value.z) || float.IsInfinity(value.z))
            {
                throw new System.ArgumentOutOfRangeException(nameof(value), value, "Sim logic position must have finite XZ components.");
            }

            _position = value;
            _positionFixed = new FixVector2((Fix64)value.x, (Fix64)value.z);
        }
    }
    public Quaternion Rotation { get; set; } = Quaternion.identity;
    public SideType Side { get; set; }
    public bool Alive { get; set; } = true;
    public string CharacterKey { get; protected set; } = "TestUnit";
    public CharacterDataDetail CharacterData { get; protected set; }
    public CreaturePropertyManager CreatureProperties { get; set; }
    public GameObject Gmo => null;

    public HealthContainer Health { get; private set; } = new HealthContainer();
    public Fix64 HealthValue => Health.currentHealth;
    public int TauntLevel { get; set; }

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
    public IBuffComp BuffComp { get; set; }
    public WeaponComp WeaponComp { get; set; }
    public IDurationMoveEffectComp DurationMoveEffectComp { get; set; }
    public void SetWeaponComp(WeaponComp weaponComp)
    {
        WeaponComp = weaponComp ?? throw new System.ArgumentNullException(nameof(weaponComp));
    }
    public void SetMoveComp(IMoveComp moveComp)
    {
        MoveComp = moveComp ?? throw new System.ArgumentNullException(nameof(moveComp));
    }
    public void SetAtkComp(IAtkComp atkComp)
    {
        AtkComp = atkComp ?? throw new System.ArgumentNullException(nameof(atkComp));
    }
    public void SetTargetingComp(ITargetingComp targetingComp)
    {
        TargetComp = targetingComp ?? throw new System.ArgumentNullException(nameof(targetingComp));
    }
    public bool IsOutOfCombat { get; private set; } = true;
    public Fix64 OutOfCombatElapsedLogicTime => IsOutOfCombat
        ? (Fix64)(_combatStateClock - _outOfCombatStartTime)
        : Fix64.Zero;
    public float OutOfCombatElapsedSeconds => IsOutOfCombat ? _combatStateClock - _outOfCombatStartTime : 0f;

    private float _combatStateClock;
    private float _outOfCombatStartTime;

    // 属性系统
    private Dictionary<CreatureMainProperty, Fix64> _properties = new Dictionary<CreatureMainProperty, Fix64>();

    public Fix64 GetProperty(CreatureMainProperty prop)
    {
        return _properties.TryGetValue(prop, out Fix64 val) ? val : (Fix64)5f;
    }

    public void SetProperty(CreatureMainProperty prop, Fix64 val)
    {
        _properties[prop] = val;
    }

    public void TakeDamage(Fix64 damage, HealthModifyType modType, IEntityContext attacker = null)
    {
        if (damage <= Fix64.Zero || !Alive)
            return;

        Health.ModifyHealth(modType, damage, false);
        ResetOutOfCombatTimer();
        if (Health.currentHealth <= Fix64.Zero)
            Alive = false;
    }

    public void Heal(Fix64 amount)
    {
        if (amount <= Fix64.Zero || !Alive)
            return;
        Health.ModifyHealth(HealthModifyType.set, Health.currentHealth + amount, false);
    }

    public void TickOutOfCombatState(float deltaTime)
    {
        _combatStateClock += deltaTime;

        if (!Alive)
        {
            ExitOutOfCombat();
            return;
        }

        IEntityContext aggroTarget = TargetComp?.AggroTarget;
        if (aggroTarget?.Alive == true)
            ExitOutOfCombat();
        else
            EnterOutOfCombat();
    }

    private void ResetOutOfCombatTimer()
    {
        _outOfCombatStartTime = _combatStateClock;
    }

    private void EnterOutOfCombat()
    {
        if (IsOutOfCombat)
            return;

        IsOutOfCombat = true;
        _outOfCombatStartTime = _combatStateClock;
    }

    private void ExitOutOfCombat()
    {
        if (!IsOutOfCombat)
            return;

        IsOutOfCombat = false;
        _outOfCombatStartTime = _combatStateClock;
    }

    // 组件锁定（复用 CompCreature 的纯逻辑）
    private Dictionary<ICapability, List<ICapability>> _compLockers = new Dictionary<ICapability, List<ICapability>>();
    private readonly HashSet<string> _invincibleSources = new HashSet<string>();

    public bool RegisterInvincibleSource(string sourceId)
    {
        if (string.IsNullOrEmpty(sourceId))
            throw new System.ArgumentException("Invincible source id is empty.", nameof(sourceId));
        return _invincibleSources.Add(sourceId);
    }

    public bool UnregisterInvincibleSource(string sourceId)
    {
        if (string.IsNullOrEmpty(sourceId))
            throw new System.ArgumentException("Invincible source id is empty.", nameof(sourceId));
        return _invincibleSources.Remove(sourceId);
    }

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

    public bool CanBeSelected()
    {
        return Alive;
    }

    public void InSelection(ISelector selector)
    {
    }

    public void DeSelection()
    {
    }
}
