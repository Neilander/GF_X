using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NormalAttackAction", menuName = "Actions/NormalAttack")]
public class NormalAttackAction : BasicAction
{
    //const string HAS_HITBOX = "hasHitbox";
    //[SerializeField] protected bool setAnimationWhenStart = true;
    [SerializeField] protected float damageStartPercent = 0.5f;
    [SerializeField] protected float damageEndPercent = 0.7f;
    [SerializeField] protected GameObject hitboxPrefab;
    [SerializeField] protected Vector3 hitboxScale;
    [SerializeField] protected Vector3 relativeOffset;

    protected override void OnStart(ActionInfo info)
    {
        //if (setAnimationWhenStart)
            //info.selfBody.animator.SetTrigger("Attack");

        

        if (info.hitbox != null)
        {
            GF.Entity.HideEntity(info.hitbox.Entity.Id);
            info.hitbox = null;
        }
    }
    
    protected override void OnUpdate(ActionInfo info, float deltaTime)
    {
        if (duration <= 0f)
            return;

        float progress = info.elapsed / duration;

        if (progress < damageStartPercent)
            return;

        // 读取状态（OnStart 里已初始化为 false）
        bool hasHitbox = info.bools[HAS_HITBOX];

        if (!hasHitbox)
        {
            // 生成碰撞箱
            var hitboxParams = EntityParams.Create();

            hitboxParams.AttchToEntity = info.selfBody.Entity;      // 父实体（Entity）
            //hitboxParams.ParentTransform = info.selfBody.transform;       // 父节点（Transform）

            //hitboxParams.localPosition = relativeOffset;      // 相对父节点的位置
            hitboxParams.localScale = hitboxScale;             // 碰撞箱大小
            hitboxParams.OnShowCallback = logic =>
            {
                info.hitbox = (HitBox)logic;
                info.hitboxID = logic.Entity.Id;
                
                logic.transform.localPosition = relativeOffset*Mathf.Sign(info.selfBody.display.transform.localScale.x);
                ((HitBox)logic).Activate(info.selfBody, info.damageInfo);
            };

            var boxID = GF.Entity.ShowEntity<HitBox>(HITBOX_PREFAB_NAME, Const.EntityGroup.Default, hitboxParams);
            //info.hitbox = GF.Entity.GetEntity(boxID).GetComponent<HitBox>();
            //info.hitboxID = boxID;

            info.bools[HAS_HITBOX] = true;
        }
        else if (progress >= damageEndPercent)
        {
            if (info.hitbox != null)
            {
                GF.Entity.HideEntity( info.hitboxID);
                info.hitbox = null;
            }

            //info.bools[HAS_HITBOX] = false;
        }
    }
    
    protected override void OnInterrupt(ActionInfo info)
    {
        // 如果已经生成过 hitbox，立刻清理
        if (info.hitbox != null)
        {
            GF.Entity.HideEntity(info.hitboxID);
            info.hitbox = null;
        }

        
    }

    
    
    /*
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
    }*/
}
