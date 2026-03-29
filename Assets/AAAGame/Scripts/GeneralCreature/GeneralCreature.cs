using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public class GeneralCreature : EntityBase, ITargetable
{
    public SideType Side { get; protected set; }
    public bool Alive { get; protected set; }
    //public ITargetable Instigator { get; set; }
    public GameObject Gmo { get; private set; }
    
    public string ReferenceId { get; protected set; }
    
    public Transform display{ get; protected set; }
    public Animator animator { get; protected set; }

    
    public CreaturePropertyManager CreaturePropertyManager { get; private set; }
    
    
    private HurtBox hurtBox;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);

        Gmo = gameObject;
        display = transform.Find("Display");

        SetUpHurtBox();

        animator = display.GetComponent<Animator>();
        // 使用实体ID作为ReferenceId，确保唯一性
        ReferenceId = gameObject.GetInstanceID().ToString();
    }

    public float health => (float)CreaturePropertyManager.GetProperty(CreatureCurrentProperty.HealthCurrent);

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        Alive = true;
        CreaturePropertyManager = new CreaturePropertyManager(GetUnitType());
        display.rotation = Quaternion.Euler(38.7f, 0, 0);
    }
    
    /// <summary>
    /// 获取单位类型（由子类重写）
    /// </summary>
    protected virtual string GetUnitType()
    {
        return ReferenceId;
    }



    public virtual void TakeDamage(float damage, HealthModifyType modType)
    {
        TakeDamage(damage, modType, null);
    }
    
    public virtual void TakeDamage(float damage, HealthModifyType modType, IEntityContext attacker = null)
    {
        if (!Alive) return;

        // 安全触发受击动画（Animator 可能没有此参数）
        if (animator != null)
        {
            foreach (var p in animator.parameters)
            {
                if (p.name == "GetHit" && p.type == AnimatorControllerParameterType.Trigger)
                {
                    animator.SetTrigger("GetHit");
                    break;
                }
            }
        }
        CreaturePropertyManager.ModifyCurrentProperty(
            CreatureCurrentProperty.HealthCurrent,
            PropertyIrreversibleAdditiveModifier.Create((Fix64)(-damage)), true);

        float cur = health;
        float max = (float)CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);

        GF.Event.Fire(this, CreatureHealthChangedEventArgs.Create(Id, cur, max, -damage));

        if (cur<= 0)
        {
            Alive = false;
            
            // 获取被击杀的实体
            Entity victimEntity = GF.Entity.GetEntity(this.Id);
            SoldierEntity victim = victimEntity?.gameObject.GetComponent<SoldierEntity>();
            if (victim != null)
            {
                // 触发宿主死亡处理
                victim.OnDead();
                
                // 触发击杀回调
                if (attacker != null)
                {
                    // 获取攻击者实体（通过转换为MAEntity获取Id）
                    MAEntity attackerEntity = attacker as MAEntity;
                    if (attackerEntity != null)
                    {
                        Entity entity = GF.Entity.GetEntity(attackerEntity.Id);
                        SoldierEntity soldier = entity?.gameObject.GetComponent<SoldierEntity>();
                        if (soldier != null)
                        {
                            soldier.OnKill(victim);
                        }
                    }
                }
            }
            
            GF.Entity.HideEntity(Id);
        }
    }


    protected virtual void SetUpHurtBox()
    {
        hurtBox = transform.Find("HurtBox").GetComponent<HurtBox>();
        hurtBox.Activate(this);
    }

    #region 可选择

    public bool CanBeSelected()
    {
        return Alive;
    }

    public virtual void InSelection(ISelector selector)
    {
        //先留好口子，之后可以加一些高亮什么的
    }

    public virtual void DeSelection()
    {
        //配套口子
    }

    #endregion
}

public enum SideType
{
    NoSide,
    PlayerSide,
    EnemySide
}

public interface ITargetable:ISelectable
{
    SideType Side { get; }
    bool Alive { get; }
    //ITargetable Instigator { get; set; }
    GameObject Gmo { get; }
    string ReferenceId { get; }

    void TakeDamage(float damage, HealthModifyType modType );
    float health { get; }
}
