using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DirectionProjectile : AbstractProjectile
{
    private Vector3 _direction;
    private bool _openedHitbox = false;
    
    ITargetable _instigator;
    Damage _damage;
    
    public void StartMoveWithDirection(Vector3 direction, ITargetable instigator, Damage damage)
    {
        _direction = direction.normalized;
        _openedHitbox = false;
        _instigator = instigator;
        _damage = damage;
        StartMove();
    }

    protected override void Move(float totalPassed, float deltaTime)
    {
        //GF.Log("lalala");
        transform.position += _direction * deltaTime*config.speed;
        float percent = totalPassed / config.lifeTime;
        if (!_openedHitbox)
        {
            if (percent > config.hitboxOpenPercent)
            {
                _openedHitbox = true;
                hitBox.Activate(_instigator, _damage);
            }
        }
        else
        {
            if (percent > config.hitboxClosePercent)
            {
                GF.Entity.HideEntity(hitBox.Entity.Id);
                hitBox = null;
            }
        }
        
        if (totalPassed >= config.lifeTime) DestroyProjectile();
    }

    public override void DestroyProjectile()
    {
        GF.Entity.HideEntity(Entity.Id);
    }
}
