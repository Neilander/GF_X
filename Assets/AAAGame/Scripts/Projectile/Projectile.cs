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

        if (!_target.IsDestroyed())
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
        if (!_target.IsDestroyed())
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
        if (!_target.IsDestroyed() && _target.Alive)
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
                        // 确保攻击者是MAEntity类型
                        MAEntity attackerEntity = _attacker as MAEntity;
                        if (attackerEntity != null)
                        {
                            // 造成伤害，传递攻击者参数
                            creature.TakeDamage(_weaponData.Damage, HealthModifyType.reduce, attackerEntity);
                        }
                        else
                        {
                            Debug.LogWarning($"Projectile: 攻击者不是MAEntity类型，无法传递击杀回调");
                            // 回退到不传递攻击者的方法
                            _target.TakeDamage(_weaponData.Damage, HealthModifyType.reduce);
                        }
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