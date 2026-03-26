using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 远程武器弹道脚本：实现匀速飞行、命中逻辑和目标死亡处理
/// </summary>
public class Projectile : EntityBase
{
    private IEntityContext _target;
    private WeaponData _weaponData;
    private RangedWeaponSO _weaponSO;
    private Vector3 _targetPosition;
    private bool _hasHit = false;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);

        if (userData is EntityParams paramsData)
        {
            _target = paramsData.Target;
            _weaponData = paramsData.WeaponData;
            _weaponSO = paramsData.WeaponSO as RangedWeaponSO;

            // 如果从paramsData获取失败，尝试从WeaponData.UserData中获取
            if (_weaponSO == null && _weaponData != null && _weaponData.UserData is DirectAtkComp atkComp)
            {
                _weaponSO = atkComp.WeaponSO as RangedWeaponSO;
            }
        }
    }

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);

        // 在OnShow中也尝试获取_weaponSO，解决对象池复用导致的空引用问题
        if (userData is EntityParams paramsData)
        {
            _target = paramsData.Target;
            _weaponData = paramsData.WeaponData;
            _weaponSO = paramsData.WeaponSO as RangedWeaponSO;

            // 如果从paramsData获取失败，尝试从WeaponData.UserData中获取
            if (_weaponSO == null && _weaponData != null && _weaponData.UserData is DirectAtkComp atkComp)
            {
                _weaponSO = atkComp.WeaponSO as RangedWeaponSO;
            }
        }

        if (_target != null && _target.Alive)
        {
            _targetPosition = _target.Position + Vector3.up * 0.5f;
        }
        else
        {
            _targetPosition = transform.position + transform.forward * 10f;
        }

        _hasHit = false;
    }

    protected virtual void Update()
    {
        if (_hasHit)
        {
            return;
        }

        //添加空检查，防止_weaponSO为null导致的空引用异常
        if (_weaponSO == null)
        {
            Debug.LogError("Projectile: _weaponSO is null!");
            return;
        }

        //实时更新目标位置，让子弹能够追踪移动的目标
        if (_target != null && _target.Alive)
        {
            _targetPosition = _target.Position + Vector3.up * 0.5f;
        }

        // 计算到目标的距离
        float distance = Vector3.Distance(transform.position, _targetPosition);
        float moveDistance = _weaponSO.ProjectileSpeed * Time.deltaTime;

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
        if (_target != null && _target.Alive)
        {
            shouldDealDamage = true;
        }

        if (shouldDealDamage)
        {
            // 添加空检查，防止_weaponData为null
            if (_weaponData != null)
            {
                // 造成伤害
                _target.TakeDamage(_weaponData.Damage, HealthModifyType.reduce);
            }
            else
            {
                Debug.LogError("Projectile: _weaponData is null!");
            }
        }

        // 添加空检查，防止_weaponSO为null
        if (_weaponSO != null && _weaponSO.HitVfxPrefab != null)
        {
            GameObject hitVfx = Object.Instantiate(_weaponSO.HitVfxPrefab, _targetPosition, Quaternion.identity);
            Object.Destroy(hitVfx, 2f);
        }

        // 使用对象池隐藏弹道
        GF.Entity.HideEntity(Entity.Id);
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        base.OnHide(isShutdown, userData);
        
        _target = null;
        _weaponData = null;
        _weaponSO = null;
    }
}