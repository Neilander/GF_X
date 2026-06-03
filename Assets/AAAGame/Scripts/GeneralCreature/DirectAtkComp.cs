using UnityEngine;
using System;

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
    private Weapon _weapon;
    private Weapon[] _weapons;
    private int _activeWeaponIndex;
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

    private string GetWeaponSOPath()
    {
        string overridePath = UnitWeaponSOOverrideResolver.GetWeaponSOPath(_ctx.CharacterKey);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        return $"Assets/AAAGame/SOs/Weapon/{GetWeaponSOAddress(_weapon.Type)}.asset";
    }

    public void Init(IEntityContext ctx)
    {
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

        if (ctx.WeaponComp?.Data != null)
        {
            SetWeapons(new[] { ctx.WeaponComp.Data });
        }
        else
        {
            string id = ctx.CharacterKey;
            var row = ctx.CharacterData;
            Fix64 level = Fix64.One;
            if (ctx is MAEntity ownerEntity && ownerEntity.CreaturePropertyManager != null)
            {
                level = ownerEntity.CreaturePropertyManager.GetLevel();
            }

            WeaponData[] weaponDatas = CharacterDataDetailAccessor.GetWeaponDatas(row, level);
            if (weaponDatas == null || weaponDatas.Length == 0)
            {
                throw new InvalidOperationException($"角色 {id} 缺少武器数据");
            }

            SetWeapons(weaponDatas);
        }

        _activeWeaponIndex = 0;
        _weapon = _weapons[_activeWeaponIndex];

        WeaponHelper.LoadWeapon(GetWeaponSOPath(), this);

        Debug.Log($"[DirectAtkComp] Init: unit={ctx.CharacterKey} weaponType={_weapon.Type} damage={_weapon.Atk} range={_weapon.Range} interval={_weapon.Interval} windUp={_weapon.WindUp} windDown={_weapon.WindDown} projectileSpeed={_weapon.ProjectileSpeed}");

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

    public void UpdateWeaponData(WeaponData weaponData)
    {
        if (weaponData == null)
            return;

        SetWeapons(new[] { weaponData });
        _activeWeaponIndex = 0;
        _weapon = _weapons[_activeWeaponIndex];

        var maEntity = _ctx as MAEntity;
        if (maEntity != null)
        {
            if (maEntity.weaponComp == null)
                maEntity.SetWeaponComp(new WeaponComp(_weapon));
            else
                maEntity.weaponComp.SwapWeapon(_weapon);
        }

        WeaponHelper.LoadWeapon(GetWeaponSOPath(), this);
        ShutDown();
    }

    private void SetWeapons(WeaponData[] weaponDatas)
    {
        PropertyManager ownerManager = (_ctx as MAEntity)?.CreaturePropertyManager?.propertyManager;
        _weapons = new Weapon[weaponDatas.Length];
        for (int i = 0; i < weaponDatas.Length; i++)
        {
            _weapons[i] = weaponDatas[i].ToWeapon($"{_ctx.CharacterKey}_Weapon{i + 1}", ownerManager);
        }
    }

    private void SetWeapons(Weapon[] weapons)
    {
        _weapons = weapons;
    }

    public void SelectWeapon(int weaponIndex)
    {
        if (_weapons == null || _weapons.Length == 0 || weaponIndex < 0 || weaponIndex >= _weapons.Length)
        {
            GF.LogError($"请求的武器索引 {weaponIndex} 超出范围，返回默认武器");
            weaponIndex = 0;
        }
        _weapon = _weapons[weaponIndex];
        _ctx.WeaponComp.SwapWeapon(_weapon);
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
                if (_stateTimer >= (float)GetCurrentWindUp())
                {
                    DealDamage();
                    EnterState(AtkState.WindDown);
                }
                break;

            case AtkState.WindDown:
                _stateTimer += deltaTime;
                if (_stateTimer >= (float)GetCurrentWindDown())
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
                Fix64 windUp = GetCurrentWindUp();
                Fix64 windDown = GetCurrentWindDown();
                Fix64 interval = GetCurrentAttackInterval();
                float cooldown = (float)(interval - windUp - windDown);
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
    private Weapon GetActiveWeapon()
    {
        return _ctx.WeaponComp?.Data ?? _weapon;
    }

    private Fix64 GetCurrentDamage()
    {
        return GetActiveWeapon().Atk;
    }

    private Fix64 GetCurrentAttackInterval()
    {
        return GetActiveWeapon().Interval;
    }

    private Fix64 GetCurrentAttackRange()
    {
        return GetActiveWeapon().Range;
    }

    private Fix64 GetCurrentWindUp()
    {
        return GetActiveWeapon().WindUp;
    }

    private Fix64 GetCurrentWindDown()
    {
        return GetActiveWeapon().WindDown;
    }

    private Fix64 GetCurrentProjectileSpeed()
    {
        return GetActiveWeapon().ProjectileSpeed;
    }

    private Fix64 GetCurrentSplashRadius()
    {
        return GetActiveWeapon().SplashRadius;
    }

    private Fix64 GetCurrentAmmunitionCapacity()
    {
        return GetActiveWeapon().AmmunitionCapacity;
    }

    private void TryStartAttack()
    {
        if (_ctx.Brain == null)
        {
            GameDebugSettings.Log(DebugCategory.Attack, $"[{_ctx.CharacterKey}] TryStart: Brain=null");
            return;
        }

        bool manualAttack = _ctx.Brain.Attack;
        bool playerAutoAttack = _ctx.Brain is AAAGame.Scripts.Entity.PlayerBrain && _ctx.TargetComp?.CurrentTarget != null;
        if (!manualAttack && !playerAutoAttack)
        {
            GameDebugSettings.Log(DebugCategory.Attack, $"[{_ctx.CharacterKey}] TryStart: Brain.Attack=false");
            return;
        }

        var target = _ctx.TargetComp?.CurrentTarget;
        if (!target.IsAttackTargetable())
        {
            GameDebugSettings.Log(DebugCategory.Attack,
                $"[{_ctx.CharacterKey}] TryStart: 无目标 targetComp={(_ctx.TargetComp != null ? "有" : "null")} target={target} alive={target?.Alive}");
            return;
        }

        Fix64 attackRange = GetCurrentAttackRange();
        float dist = _ctx.DistanceToTargetSurface(target);
        float wpnRange = DistanceUnitConverter.ConvertToWorldFloat(attackRange);

        // 统一判定：攻击者中心到目标碰撞体边缘的 XZ 距离，和武器射程直接比较。
        float range = wpnRange;

        if (dist > range)
        {
            GameDebugSettings.Log(DebugCategory.Attack,
                $"[{_ctx.CharacterKey}] TryStart: 超距 dist={dist:F2} range={range:F2} (wpn={wpnRange:F2})");
            return;
        }

        _lockedTarget = target;
        AttackCount++;
        _movementLockedByThisAttack = ShouldLockMoveDuringAttack();
        if (_movementLockedByThisAttack)
            _ctx.LockComp(_ctx.MoveComp, this);

        EnterState(AtkState.WindUp);

        GameDebugSettings.Log(DebugCategory.Attack,
            $"[{_ctx.CharacterKey}] → WindUp 第{AttackCount}次攻击 目标={target.CharacterKey} dist={dist:F2} range={range:F2}");
    }

    private void DealDamage()
    {
        if (!_lockedTarget.IsAttackTargetable())
        {
            GameDebugSettings.Log(DebugCategory.Attack,
                $"[{_ctx.CharacterKey}] DealDamage: 目标丢失或已死 target={_lockedTarget} alive={_lockedTarget?.Alive}");
            return;
        }

        Fix64 damage = GetCurrentDamage();
        Fix64 splashRadius = GetCurrentSplashRadius();

        GameDebugSettings.Log(DebugCategory.Attack,
            $"[{_ctx.CharacterKey}] DealDamage: 对 {_lockedTarget.CharacterKey} 造成 {damage} 伤害");

        // 优先委托武器 SO 执行伤害
        if (_weaponSO != null)
        {
            Weapon activeWeapon = GetActiveWeapon();
            WeaponData snapshot = new WeaponData(
                activeWeapon.Type,
                activeWeapon.Atk,
                activeWeapon.Interval,
                activeWeapon.Range,
                activeWeapon.ProjectileSpeed,
                activeWeapon.WindUp,
                activeWeapon.WindDown,
                activeWeapon.SplashRadius,
                activeWeapon.SplitAngle,
                activeWeapon.SplitDist,
                activeWeapon.ProjectileCount,
                activeWeapon.AmmunitionCapacity,
                Array.Empty<Fix64>());
            _weaponSO.Execute(_ctx, _lockedTarget, snapshot);
        }
        else
        {
            // 降级 fallback：走 DamageHelper 统一走 buff 钩子链路
            var dmg = new Damage(_ctx as ITargetable, damage, HealthModifyType.reduce);
            DamageHelper.DoDamage(_lockedTarget as ITargetable, dmg, _ctx);
        }

        if (splashRadius > Fix64.Zero)
        {
            ApplySplashDamage(damage);
        }

        // 普通攻击造成伤害的音效；远程武器在这里只是创建子弹（命中是子弹的事），跳过
        bool isRanged = _weaponSO is RangedWeaponSO;
        if (!isRanged && AudioManager.Instance != null)
            AudioManager.Instance.Play("basicAttack");
    }

    private void ApplySplashDamage(Fix64 damage)
    {
        // 溅射需要知道周围所有敌方单位，暂留接口
        // 后续可通过 IEntityContext 暴露 "查询附近单位" 的方法
    }

    private void EnterState(AtkState newState)
    {
        GameDebugSettings.Log(DebugCategory.Attack,
            $"[{_ctx.CharacterKey}] 状态 {State} → {newState}");
        State = newState;
        _stateTimer = 0f;

        // 通过参数驱动 Animator Controller
        if (_animator != null && newState == AtkState.WindUp)
        {
            _animator.SetTrigger("Attack");
        }

        if (newState == AtkState.WindUp && GetActiveWeapon().Type != WeaponType.Projectile)
        {
            float trailDuration = Mathf.Max(0.08f, (float)GetCurrentWindUp() + 0.08f);
            WeaponAttackTrailEffect.Play(_ctx, trailDuration);
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
