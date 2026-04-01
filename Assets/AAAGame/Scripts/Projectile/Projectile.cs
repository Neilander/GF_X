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

        if (_target != null)
        {
            try
            {
                // 尝试获取目标位置，无论目标是否存活
                _targetPosition = _target.Position + Vector3.up * 0.5f;
            }
            catch (MissingReferenceException)
            {
                // 目标已被销毁，使用默认位置
                _targetPosition = transform.position + transform.forward * 10f;
                _target = null;
            }
        }
        else
        {
            // 没有目标，使用前方位置
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
        if (_target != null)
        {
            try
            {
                if (_target.Alive)
                {
                    // 目标还存活，更新位置
                    _targetPosition = _target.Position + Vector3.up * 0.5f;
                }
                else
                {
                    // 目标已死亡（被隐藏），停止更新位置
                    _target = null;
                }
            }
            catch (MissingReferenceException)
            {
                // 目标被销毁，保持最后的位置
                _target = null;
            }
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
                // 尝试将IEntityContext转换为GeneralCreature以支持攻击者参数
                if (_target is GeneralCreature creature)
                {
                    // 造成伤害，传递攻击者参数
                    creature.TakeDamage(_weaponData.Damage, HealthModifyType.reduce, _attacker);
                }
                else
                {
                    // 回退到接口定义的方法
                    _target.TakeDamage(_weaponData.Damage, HealthModifyType.reduce);
                }
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
        base.OnHide(isShutdown, userData);
        
        _target = null;
        _weaponData = null;
        _weaponSO = null;
    }
}