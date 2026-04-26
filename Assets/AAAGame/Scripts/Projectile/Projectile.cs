using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.Scripts.GeneralCreature;

/// <summary>
/// 远程武器弹道脚本：实现匀速飞行、命中逻辑和目标死亡处理
/// </summary>
public class Projectile : EntityBase
{
    private IEntityContext _target;
    private IEntityContext _attacker;
    private WeaponData _weaponData;
    private RangedWeaponSO _weaponSO;
    private Vector3 _targetPosition;
    private bool _hasHit = false;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
    }

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);

        if (userData is EntityParams paramsData)
        {
            _target = paramsData.Target;
            _attacker = paramsData.Attacker;
            _weaponData = paramsData.WeaponData;
            _weaponSO = paramsData.WeaponSO as RangedWeaponSO;
        }

        if (_weaponData == null)
        {
            Debug.LogError("Projectile: WeaponData is null, cannot resolve projectile speed.");
            _hasHit = true;
            GF.Entity.HideEntity(Entity.Id);
            return;
        }

        if (_target != null && !_target.IsDestroyed())
        {
            _targetPosition = _target.Position + Vector3.up * 0.5f;
        }
        else
        {
            // 没有目标，使用前方位置
            _targetPosition = transform.position + transform.forward * 10f;
            _target = null;
        }

        _hasHit = false;
    }

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);

        if (_hasHit)
        {
            return;
        }

        // Keep tracking target position while projectile is flying.
        //实时更新目标位置，让子弹能够追踪移动的目标
        if (_target != null && !_target.IsDestroyed())
        {
            if (_target.Alive)
            {
                _targetPosition = _target.Position + Vector3.up * 0.5f;
            }
            else
            {
                _target = null;
            }
        }
        else
        {
            _target = null;
        }

        // 计算到目标的距离
        float distance = Vector3.Distance(transform.position, _targetPosition);
        Fix64 projectileSpeed = _weaponData.ProjectileSpeed;
        float moveDistance = (float)projectileSpeed * elapseSeconds;

        if (distance <= moveDistance)
        {
            // 到达目标位置
            HitTarget();
        }
        else
        {
            // 向目标移动
            Vector3 direction = (_targetPosition - transform.position).normalized;
            transform.position += direction * moveDistance;
            transform.LookAt(_targetPosition);
        }
    }

    private void HitTarget()
    {
        _hasHit = true;

        bool shouldDealDamage = false;

        // 检查目标是否还活着
        if (_target != null && !_target.IsDestroyed() && _target.Alive)
        {
            shouldDealDamage = true;
        }

        if (shouldDealDamage)
        {
            // 添加空检查，防止_weaponData为null
            if (_weaponData != null)
            {
                var damage = new Damage(_attacker as ITargetable, _weaponData.Damage, HealthModifyType.reduce);
                DamageHelper.DoDamage(_target as ITargetable, damage, _attacker);
            }
        }

        // 播放命中特效
        if (_weaponSO != null && !string.IsNullOrEmpty(_weaponSO.HitVfxName))
        {
            var vfxParams = EntityParams.Create(_targetPosition);
            GF.Entity.ShowEffect(_weaponSO.HitVfxName, vfxParams, 2f);
        }

        // 使用对象池隐藏弹道
        GF.Entity.HideEntity(Entity.Id);
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        _hasHit = true;
        base.OnHide(isShutdown, userData);
        
        _target = null;
        _weaponData = null;
        _weaponSO = null;
    }
}
