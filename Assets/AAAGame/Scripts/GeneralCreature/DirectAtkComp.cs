using UnityEngine;

/// <summary>
/// 直接攻击组件：选定目标后直接造成伤害，不使用攻击盒。
/// 适合大量小兵的战斗场景。
///
/// 攻击流程：
/// 1. 检测 Brain.Attack 且有目标在攻击范围内
/// 2. 进入前摇阶段（WindUp），锁定移动
/// 3. 前摇结束时对目标造成伤害
/// 4. 进入后摇阶段（WindDown）
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

    public AtkState State { get; private set; } = AtkState.Idle;
    public int AttackCount { get; private set; }
    public bool IsAttacking => State == AtkState.WindUp || State == AtkState.WindDown;

    private float _stateTimer;
    private IEntityContext _lockedTarget;

    public DirectAtkComp(WeaponData weapon)
    {
        _weapon = weapon;
    }

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        State = AtkState.Idle;
        _stateTimer = 0f;
        AttackCount = 0;
        _lockedTarget = null;
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
                if (_stateTimer >= _weapon.WindUp)
                {
                    DealDamage();
                    EnterState(AtkState.WindDown);
                }
                break;

            case AtkState.WindDown:
                _stateTimer += deltaTime;
                if (_stateTimer >= _weapon.WindDown)
                {
                    _ctx.ResumeComp(_ctx.MoveComp, this);
                    EnterState(AtkState.Cooldown);
                }
                break;

            case AtkState.Cooldown:
                _stateTimer += deltaTime;
                float cooldown = _weapon.AttackInterval - _weapon.WindUp - _weapon.WindDown;
                if (cooldown < 0f) cooldown = 0f;
                if (_stateTimer >= cooldown)
                {
                    EnterState(AtkState.Idle);
                }
                break;
        }
    }

    private void TryStartAttack()
    {
        if (_ctx.Brain == null) return;
        if (!_ctx.Brain.Attack) return;

        var target = _ctx.TargetComp?.CurrentTarget;
        if (target == null || !target.Alive) return;

        float dist = Vector3.Distance(_ctx.Position, target.Position);
        float range = _weapon.AttackRange * 0.01f; // 配表单位是码（百分位），转米

        if (dist > range) return;

        _lockedTarget = target;
        AttackCount++;
        _ctx.LockComp(_ctx.MoveComp, this);

        EnterState(AtkState.WindUp);
    }

    private void DealDamage()
    {
        if (_lockedTarget == null || !_lockedTarget.Alive) return;

        float damage = _weapon.Damage;

        // 护甲减伤：damage = max(1, damage - armor)
        // 这里暂用简单减法，后续可扩展
        _lockedTarget.TakeDamage(damage, HealthModifyType.reduce);

        // 溅射伤害
        if (_weapon.SplashRadius > 0f)
        {
            ApplySplashDamage(damage);
        }
    }

    private void ApplySplashDamage(float damage)
    {
        // 溅射需要知道周围所有敌方单位，暂留接口
        // 后续可通过 IEntityContext 暴露 "查询附近单位" 的方法
    }

    private void EnterState(AtkState newState)
    {
        State = newState;
        _stateTimer = 0f;
    }

    public void ShutDown()
    {
        if (IsAttacking && _ctx != null)
        {
            _ctx.ResumeComp(_ctx.MoveComp, this);
        }
        State = AtkState.Idle;
        _stateTimer = 0f;
        _lockedTarget = null;
    }

    public void Resume() { }
}
