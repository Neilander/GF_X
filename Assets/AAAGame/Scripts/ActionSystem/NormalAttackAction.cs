using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NormalAttackAction", menuName = "Actions/NormalAttack")]
public class NormalAttackAction : BasicAction
{
    [SerializeField] protected bool setAnimationWhenStart = true;
    [SerializeField] protected float damageStartPercent = 0.5f;
    [SerializeField] protected float damageEndPercent = 0.7f;
    [SerializeField] protected GameObject hitboxPrefab;
    [SerializeField] protected Vector3 hitboxScale;
    [SerializeField] protected Vector3 relativeOffset;

    private bool hasInstantiateHitbox = false;
    private GameObject hitbox;
    
    protected override void OnStart()
    {
        base.OnStart();
        if(setAnimationWhenStart)
            selfBody.animator.SetTrigger("Attack");
        hasInstantiateHitbox = false;
        if(hitbox != null)
            Destroy(hitbox);
    }
    
    protected override void OnUpdate(float deltaTime)
    {
        base.OnUpdate(deltaTime);
        if (elapsed / duration < damageStartPercent)
            return;
        
        if(!hasInstantiateHitbox)
        {
            //生成碰撞箱并启用
            
            hasInstantiateHitbox = true;
        }
        else if(elapsed / duration >= damageEndPercent)
        {
            Destroy(hitbox);
        }
    }

    protected override void OnInterrupt()
    {
        base.OnInterrupt();
        //删除hitbox
    }
}
