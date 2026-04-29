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
            _targetPosition = ResolveTargetAimPoint(_target, transform.position);
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
                _targetPosition = ResolveTargetAimPoint(_target, transform.position);
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
        float projectileSpeed = DistanceUnitConverter.ConvertToWorldFloat(_weaponData.ProjectileSpeed);
        float moveDistance = projectileSpeed * elapseSeconds;

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

    private static Vector3 ResolveTargetAimPoint(IEntityContext target, Vector3 fromPosition)
    {
        if (TryGetTargetClosestPoint(target, fromPosition, out Vector3 closestPoint))
        {
            return closestPoint;
        }

        return target.Position + Vector3.up * 0.5f;
    }

    private static bool TryGetTargetClosestPoint(IEntityContext target, Vector3 fromPosition, out Vector3 closestPoint)
    {
        closestPoint = default;

        if (!(target is Component targetComponent) || targetComponent == null)
        {
            return false;
        }

        var hurtBox = targetComponent.GetComponentInChildren<HurtBox>();
        if (hurtBox != null && hurtBox.TryGetComponent<Collider>(out var hurtCollider) && hurtCollider.enabled)
        {
            closestPoint = hurtCollider.ClosestPoint(fromPosition);
            return true;
        }

        var collider = targetComponent.GetComponentInChildren<Collider>();
        if (collider != null && collider.enabled)
        {
            closestPoint = collider.ClosestPoint(fromPosition);
            return true;
        }

        return false;
    }

    private void HitTarget()
    {
        _hasHit = true;

        bool shouldDealDamage = false;

        // 检查目标是否还活着
        if (_target != null && !_target.IsDestroyed() && _target.Alive)
        {
            shouldDealDamage = true;
            //Debug.LogError("该造成伤害的");
        }

        if (shouldDealDamage)
        {
            // 添加空检查，防止_weaponData为null
            if (_weaponData != null)
            {
                var damage = new Damage(_attacker as ITargetable, _weaponData.Damage, HealthModifyType.reduce);
                DamageHelper.DoDamage(_target as ITargetable, damage, _attacker);
                //Debug.LogError("已经造成伤害的");
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
