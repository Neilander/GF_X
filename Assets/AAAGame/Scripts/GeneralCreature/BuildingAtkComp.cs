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
    private WeaponData _weaponData;
    private AtkState _state;
    private float _stateTimer;
    private IEntityContext _lockedTarget;

    public bool IsAttacking => _state == AtkState.WindUp || _state == AtkState.WindDown;

    public BuildingAtkComp(WeaponData defaultWeaponData)
    {
        _weaponData = defaultWeaponData ?? new WeaponData();
    }

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        _state = AtkState.Idle;
        _stateTimer = 0f;
        _lockedTarget = null;

        if (_ctx is MAEntity maEntity)
        {
            if (maEntity.weaponComp == null)
                maEntity.SetWeaponComp(new WeaponComp(_weaponData));
            else
                maEntity.weaponComp.SwapWeapon(_weaponData);
        }
    }

    public void UpdateWeaponData(WeaponData weaponData)
    {
        if (weaponData == null)
            return;

        _weaponData = weaponData;
        if (_ctx?.WeaponComp != null)
            _ctx.WeaponComp.SwapWeapon(_weaponData);
    }

    public void Attack(float deltaTime)
    {
        if (_ctx == null || _weaponData == null)
            return;

        switch (_state)
        {
            case AtkState.Idle:
                TryStartAttack();
                break;

            case AtkState.WindUp:
                _stateTimer += deltaTime;
                if (_stateTimer >= _weaponData.WindUp)
                {
                    DealDamage();
                    EnterState(AtkState.WindDown);
                }
                break;

            case AtkState.WindDown:
                _stateTimer += deltaTime;
                if (_stateTimer >= _weaponData.WindDown)
                    EnterState(AtkState.Cooldown);
                break;

            case AtkState.Cooldown:
                _stateTimer += deltaTime;
                float cooldown = _weaponData.AttackInterval - _weaponData.WindUp - _weaponData.WindDown;
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

        if (_ctx is BuildingEntity building && building.HasPermanentNoAttackCapability)
            return;

        if (!HasDamagePotential())
            return;

        var target = _ctx.TargetComp?.CurrentTarget;
        if (!target.IsAttackTargetable())
            return;

        float dist = Vector3.Distance(_ctx.Position, target.Position);
        if (dist > GetEffectiveAttackRange())
            return;

        _lockedTarget = target;
        EnterState(AtkState.WindUp);
    }

    private void DealDamage()
    {
        if (!_lockedTarget.IsAttackTargetable())
            return;

        float damageFromProperty = _ctx.GetProperty(CreatureMainProperty.PhysicalAtk);
        float finalDamage = damageFromProperty > 0f ? damageFromProperty : _weaponData.Damage;
        if (finalDamage <= 0f)
            return;

        _lockedTarget.TakeDamage(finalDamage, HealthModifyType.reduce, _ctx);
    }

    private bool HasDamagePotential()
    {
        if (_ctx is BuildingEntity building && building.HasPermanentNoAttackCapability)
            return false;

        float damageFromProperty = _ctx.GetProperty(CreatureMainProperty.PhysicalAtk);
        float finalDamage = damageFromProperty > 0f ? damageFromProperty : _weaponData.Damage;
        return finalDamage > 0f;
    }

    private float GetEffectiveAttackRange()
    {
        float baseRange = _weaponData.AttackRange * 0.01f;
        float equilibriumRadius = 0f;

        if (GroupMoveManager.HasInstance)
        {
            int selfId = (_ctx as MAEntity)?.GetInstanceID() ?? _ctx.GetHashCode();
            equilibriumRadius = GroupMoveManager.Instance.Coordinator.GetAgentEquilibriumRadius(selfId);
        }

        return baseRange + equilibriumRadius;
    }

    private void EnterState(AtkState state)
    {
        _state = state;
        _stateTimer = 0f;
    }
}
