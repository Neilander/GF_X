using UnityEngine;

/// <summary>
/// 直接攻击组件：选定目标后直接造成伤害，不使用攻击盒。
/// 适合大量小兵的战斗场景。
///
/// 攻击流程：
/// 1. 检测 Brain.Attack 且有目标在攻击范围内
/// 2. 进入前摇阶段（WindUp），锁定移动
/// 3. 前摇结束时对目标造成伤害,
/// 4. 进入后摇阶段（WindDown）,
/// 5. 后摇结束，解锁移动，进入冷却
///
/// 纯逻辑实现，不依赖 Animator/HitBox/BasicAction。
/// </summary>
public class DirectAtkComp : IAtkComp
{
    public enum AtkState
    {
        Idle,
        WindUp,
        WindDown,
        Cooldown
    }

    private IEntityContext _ctx;
    private WeaponData _weapon;
    private WeaponType _index;
    private BaseWeaponSO _weaponSO;
    private Animator _animator;

    public BaseWeaponSO WeaponSO => _weaponSO;

    public void SetWeaponSO(BaseWeaponSO so) => _weaponSO = so;

    public AtkState State { get; private set; } = AtkState.Idle;
    public int AttackCount { get; private set; }
    public bool IsAttacking => State == AtkState.WindUp || State == AtkState.WindDown;

    private float _stateTimer;
    private IEntityContext _lockedTarget;
    private bool _movementLockedByThisAttack;

    public DirectAtkComp(WeaponType index)
    {
        _index = index;
    }

    private string GetWeaponSOAddress(WeaponType index)
    {
        switch (index)
        {
            case WeaponType.Melee:
                return "soldier_default";

            case WeaponType.Projectile:
                return "ranged_default";

            default:
                return "soldier_default";

        }
    }

    public void Init(IEntityContext ctx)
    {
        //TODO:修改正确读取方式
        WeaponHelper.LoadWeapon($"Assets/AAAGame/SOs/Weapon/{GetWeaponSOAddress(_index)}.asset", this);
        _ctx = ctx;
        State = AtkState.Idle;
        _stateTimer = 0f;
        AttackCount = 0;
        _lockedTarget = null;
        _movementLockedByThisAttack = false;

        // 获取Animator组件
        var entity = _ctx as MAEntity;
        if (entity != null)
        {
            _animator = entity.animator;
        }

        // TODO: 根据 index 读表获取攻击数值
        // 当前使用硬编码测试数据
        string id = ctx.ReferenceId;
        var row = GeneralCreature.GetData(id);
        Fix64 damage = row.PhysicalAtk;
        Fix64 interval = row.WeaponIntervalOne;
        Fix64 range = row.WeaponRangeOne;
        Fix64 windUp = row.WeaponPreOne;
        Fix64 windDown = row.WeaponPreOne;


        // 原来的代码：
        // // 构建武器数据（供 GetActiveWeapon fallback 使用）
        // _weapon = new WeaponData
        // {
        //     Damage = damage,
        //     AttackInterval = interval,
        //     AttackRange = range,
        //     WindUp = windUp,
        //     WindDown = windDown,
        //     Type =  WeaponType.Melee
        // };

        // 新加：根据index判断武器类型，为远程武器设置正确的武器数据
        WeaponType weaponType = _index; //_index == "ranged_test" ? WeaponType.Projectile : WeaponType.Melee;
        Fix64 projectileSpeed = row.WeaponSpeedOne;
        Fix64 attackRange = range; // 远程武器射程更远

        _weapon = new WeaponData
        {
            Damage = damage,
            AttackInterval = interval,
            AttackRange = attackRange,
            // AttackRange = range原来的
            WindUp = windUp,
            WindDown = windDown,
            Type = weaponType,
            // Type =  WeaponType.Melee原来的
            ProjectileSpeed = projectileSpeed
        };




        Debug.Log($"[DirectAtkComp] Init: unit={ctx.ReferenceId} weaponType={weaponType} damage={damage} range={attackRange} interval={interval} windUp={windUp} windDown={windDown} projectileSpeed={projectileSpeed}");

        // 创建 WeaponComp 并挂载到 Entity
        var wc = new WeaponComp(_weapon);
        var maEntity = _ctx as MAEntity;
        if (maEntity != null)
        {
            maEntity.SetWeaponComp(wc);
        }
        else
        {
        }
    }

    public void Attack(float deltaTime)
    {
        if (_ctx == null) return;

        switch (State)
        {
            case AtkState.Idle:
                TryStartAttack();
                break;

            case AtkState.WindUp:
                _stateTimer += deltaTime;
                if (_stateTimer >= (float)GetActiveWeapon().WindUp)
                {
                    DealDamage();
                    EnterState(AtkState.WindDown);
                }
                break;

            case AtkState.WindDown:
                _stateTimer += deltaTime;
                if (_stateTimer >= (float)GetActiveWeapon().WindDown)
                {
                    if (_movementLockedByThisAttack)
                    {
                        _ctx.ResumeComp(_ctx.MoveComp, this);
                        _movementLockedByThisAttack = false;
                    }
                    EnterState(AtkState.Cooldown);
                }
                break;

            case AtkState.Cooldown:
                _stateTimer += deltaTime;
                var w = GetActiveWeapon();
                float cooldown = (float)(w.AttackInterval - w.WindUp - w.WindDown);
                if (cooldown < 0f) cooldown = 0f;
                if (_stateTimer >= cooldown)
                {
                    EnterState(AtkState.Idle);
                }
                break;
        }
    }

    /// <summary>
    /// 获取当前生效的武器数据：优先从 WeaponComp 拿最新的，没有则用初始化时的 _weapon。
    /// </summary>
    private WeaponData GetActiveWeapon()
    {
        return _ctx.WeaponComp?.Data ?? _weapon;
    }

    private void TryStartAttack()
    {
        if (_ctx.Brain == null)
        {
            GameDebugSettings.Log(DebugCategory.Attack, $"[{_ctx.ReferenceId}] TryStart: Brain=null");
            return;
        }

        bool manualAttack = _ctx.Brain.Attack;
        bool playerAutoAttack = _ctx.Brain is AAAGame.Scripts.Entity.PlayerBrain && _ctx.TargetComp?.CurrentTarget != null;
        if (!manualAttack && !playerAutoAttack)
        {
            GameDebugSettings.Log(DebugCategory.Attack, $"[{_ctx.ReferenceId}] TryStart: Brain.Attack=false");
            return;
        }

        var target = _ctx.TargetComp?.CurrentTarget;
        if (!target.IsAttackTargetable())
        {
            GameDebugSettings.Log(DebugCategory.Attack,
                $"[{_ctx.ReferenceId}] TryStart: 无目标 targetComp={(_ctx.TargetComp != null ? "有" : "null")} target={target} alive={target?.Alive}");
            return;
        }

        var activeWeapon = GetActiveWeapon();
        float dist = _ctx.DistanceToTargetSurface(target);
        float wpnRange = DistanceUnitConverter.ConvertToWorldFloat(activeWeapon.AttackRange);

        // 统一判定：攻击者中心到目标碰撞体边缘的 XZ 距离，和武器射程直接比较。
        float range = wpnRange;

        if (dist > range)
        {
            GameDebugSettings.Log(DebugCategory.Attack,
                $"[{_ctx.ReferenceId}] TryStart: 超距 dist={dist:F2} range={range:F2} (wpn={wpnRange:F2})");
            return;
        }

        _lockedTarget = target;
        AttackCount++;
        _movementLockedByThisAttack = ShouldLockMoveDuringAttack();
        if (_movementLockedByThisAttack)
            _ctx.LockComp(_ctx.MoveComp, this);

        EnterState(AtkState.WindUp);

        GameDebugSettings.Log(DebugCategory.Attack,
            $"[{_ctx.ReferenceId}] → WindUp 第{AttackCount}次攻击 目标={target.ReferenceId} dist={dist:F2} range={range:F2}");
    }

    private void DealDamage()
    {
        if (!_lockedTarget.IsAttackTargetable())
        {
            GameDebugSettings.Log(DebugCategory.Attack,
                $"[{_ctx.ReferenceId}] DealDamage: 目标丢失或已死 target={_lockedTarget} alive={_lockedTarget?.Alive}");
            return;
        }

        var weaponData = GetActiveWeapon();
        Fix64 damage = weaponData.Damage;

        GameDebugSettings.Log(DebugCategory.Attack,
            $"[{_ctx.ReferenceId}] DealDamage: 对 {_lockedTarget.ReferenceId} 造成 {damage} 伤害");

        // 优先委托武器 SO 执行伤害
        if (_weaponSO != null)
        {
            _weaponSO.Execute(_ctx, _lockedTarget, weaponData);
        }
        else
        {
            // 降级 fallback：直接调用 TakeDamage
            _lockedTarget.TakeDamage(damage, HealthModifyType.reduce);
        }

        if (weaponData.SplashRadius > Fix64.Zero)
        {
            ApplySplashDamage(damage);
        }
    }

    private void ApplySplashDamage(Fix64 damage)
    {
        // 溅射需要知道周围所有敌方单位，暂留接口
        // 后续可通过 IEntityContext 暴露 "查询附近单位" 的方法
    }

    private void EnterState(AtkState newState)
    {
        GameDebugSettings.Log(DebugCategory.Attack,
            $"[{_ctx.ReferenceId}] 状态 {State} → {newState}");
        State = newState;
        _stateTimer = 0f;

        // 通过参数驱动 Animator Controller
        if (_animator != null && newState == AtkState.WindUp)
        {
            _animator.SetTrigger("Attack");
        }
    }

    public void ShutDown()
    {
        if (IsAttacking && _ctx != null && _movementLockedByThisAttack)
        {
            _ctx.ResumeComp(_ctx.MoveComp, this);
        }
        _movementLockedByThisAttack = false;
        State = AtkState.Idle;
        _stateTimer = 0f;
        _lockedTarget = null;
    }

    private bool ShouldLockMoveDuringAttack()
    {
        // 玩家脑控下允许边移动边攻击。
        return !(_ctx?.Brain is AAAGame.Scripts.Entity.PlayerBrain);
    }

    public void Resume() { }
}
