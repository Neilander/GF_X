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
        ReferenceId = "Knight";
    }

    public float health => (float)CreaturePropertyManager.GetProperty(CreatureCurrentProperty.HealthCurrent);

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        Alive = true;
        CreaturePropertyManager = new CreaturePropertyManager(ReferenceId);
        display.rotation = Quaternion.Euler(38.7f, 0, 0);
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
                    Debug.Log($"GeneralCreature.TakeDamage: 触发击杀回调，攻击者={attacker.GetType().Name}");
                    // 尝试从攻击者获取MAEntity实例
                    MAEntity attackerEntity = attacker as MAEntity;
                    if (attackerEntity != null)
                    {
                        Debug.Log($"GeneralCreature.TakeDamage: 攻击者是MAEntity，ID={attackerEntity.Id}");
                        Entity entity = GF.Entity.GetEntity(attackerEntity.Id);
                        SoldierEntity soldier = entity?.gameObject.GetComponent<SoldierEntity>();
                        if (soldier != null)
                        {
                            Debug.Log($"GeneralCreature.TakeDamage: 调用soldier.OnKill，攻击者单位={soldier.UnitIndex}, ID={soldier.Id}");
                            soldier.OnKill(victim);
                        }
                        else
                        {
                            Debug.LogError($"GeneralCreature.TakeDamage: 攻击者不是SoldierEntity类型");
                        }
                    }
                    else
                    {
                        Debug.LogError($"GeneralCreature.TakeDamage: 攻击者不是MAEntity类型，无法触发击杀回调");
                    }
                }
                else
                {
                    Debug.LogWarning($"GeneralCreature.TakeDamage: 攻击者为null，无法触发击杀回调");
                }
            }
            
            GF.Entity.HideEntity(Id);
        }
    }

    /*
    public virtual void TakeDamage(float damage, HealthModifyType modType)
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

        if (cur <= 0)
        {
            Alive = false;
            GF.Entity.HideEntity(Id);
        }
    }*/


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
        GF.Log(gameObject.name+"被选择了");
        //先留好口子，之后可以加一些高亮什么的
    }

    public virtual void DeSelection()
    {
        //配套口子
        GF.Log(gameObject.name+"取消选择了");
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

    void TakeDamage(float damage, HealthModifyType modType, IEntityContext attacker = null );
    float health { get; }
}
