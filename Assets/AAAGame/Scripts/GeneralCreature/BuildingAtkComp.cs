using UnityEngine;

/// <summary>
/// 建筑专用攻击组件：不依赖角色数据表，武器参数由 BuildingEntity 在 OnShow 时注入。
/// </summary>
public class BuildingAtkComp : IAtkComp
{
    private enum AtkState
    {
        Idle,
        WindUp,
        WindDown,
        Cooldown
    }

    private IEntityContext _ctx;
    private readonly WeaponData _defaultWeaponData;
    private Weapon _weapon;
    private AtkState _state;
    private float _stateTimer;
    private IEntityContext _lockedTarget;

    public bool IsAttacking => _state == AtkState.WindUp || _state == AtkState.WindDown;

    public BuildingAtkComp(WeaponData defaultWeaponData)
    {
        _defaultWeaponData = defaultWeaponData;
    }

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        PropertyManager ownerManager = (ctx as MAEntity)?.CreaturePropertyManager?.propertyManager;
        _weapon = Weapon.Create("BuildingWeapon", _defaultWeaponData, ownerManager);
        _state = AtkState.Idle;
        _stateTimer = 0f;
        _lockedTarget = null;

        if (_ctx is MAEntity maEntity)
        {
            if (maEntity.weaponComp == null)
                maEntity.SetWeaponComp(new WeaponComp(_weapon));
            else
                maEntity.weaponComp.SwapWeapon(_weapon);
        }
    }

    public void UpdateWeaponData(WeaponData weaponData)
    {
        if (weaponData == null)
            return;

        PropertyManager ownerManager = (_ctx as MAEntity)?.CreaturePropertyManager?.propertyManager;
        _weapon = Weapon.Create("BuildingWeapon", weaponData, ownerManager);
        if (_ctx?.WeaponComp != null)
            _ctx.WeaponComp.SwapWeapon(_weapon);
    }

    public void Attack(float deltaTime)
    {
        if (_ctx == null || _weapon == null)
            return;

        switch (_state)
        {
            case AtkState.Idle:
                TryStartAttack();
                break;

            case AtkState.WindUp:
                _stateTimer += deltaTime;
                if (_stateTimer >= (float)_weapon.WindUp)
                {
                    DealDamage();
                    EnterState(AtkState.WindDown);
                }
                break;

            case AtkState.WindDown:
                _stateTimer += deltaTime;
                if (_stateTimer >= (float)_weapon.WindDown)
                    EnterState(AtkState.Cooldown);
                break;

            case AtkState.Cooldown:
                _stateTimer += deltaTime;
                float cooldown = (float)(_weapon.Interval - _weapon.WindUp - _weapon.WindDown);
                if (cooldown < 0f)
                    cooldown = 0f;

                if (_stateTimer >= cooldown)
                    EnterState(AtkState.Idle);
                break;
        }
    }

    public void ShutDown()
    {
        _state = AtkState.Idle;
        _stateTimer = 0f;
        _lockedTarget = null;
    }

    public void Resume()
    {
    }

    private void TryStartAttack()
    {
        if (_ctx.Brain == null || !_ctx.Brain.Attack)
            return;

        if (_ctx is BuildingEntity building && (building.HasPermanentNoAttackCapability || building.IsPhaseProtected))
            return;

        if (!HasDamagePotential())
            return;

        var target = _ctx.TargetComp?.CurrentTarget;
        if (!target.IsAttackTargetable())
            return;

        float dist = _ctx.DistanceToTargetSurface(target);
        if (dist > GetEffectiveAttackRange())
            return;

        _lockedTarget = target;
        EnterState(AtkState.WindUp);
    }

    private void DealDamage()
    {
        if (!_lockedTarget.IsAttackTargetable())
            return;

        Fix64 damage = _weapon.Atk;
        if (damage <= Fix64.Zero)
            return;

        _lockedTarget.TakeDamage(damage, HealthModifyType.reduce, _ctx);
    }

    private bool HasDamagePotential()
    {
        if (_ctx is BuildingEntity building && (building.HasPermanentNoAttackCapability || building.IsPhaseProtected))
            return false;

        return _weapon.Atk > Fix64.Zero;
    }

    private float GetEffectiveAttackRange()
    {
        float baseRange = DistanceUnitConverter.ConvertToWorldFloat(_weapon.Range);
        return baseRange;
    }

    private void EnterState(AtkState state)
    {
        _state = state;
        _stateTimer = 0f;
    }
}
