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
        //Instigator = this;
       
        Gmo = gameObject;
        display = transform.Find("Display");
        
        SetUpHurtBox();
        
        animator = display.GetComponent<Animator>();
        ReferenceId = "Knight";
        
    }

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        Alive = true;
        CreaturePropertyManager = new CreaturePropertyManager(ReferenceId);
        display.rotation = Quaternion.Euler(38.7f, 0, 0);
    }



    public virtual void TakeDamage(float damage, HealthModifyType modType)
    {
        animator.SetTrigger( "GetHit");
       //GF.Log("生物受伤，目前只实现了直接扣血");
       //GF.Log("生物当前血量"+CreaturePropertyManager.GetProperty(CreatureCurrentProperty.HealthCurrent));
       CreaturePropertyManager.ModifyCurrentProperty(CreatureCurrentProperty.HealthCurrent,PropertyIrreversibleAdditiveModifier.Create((Fix64)(-damage)), true);
       GF.Log("生物当前血量"+CreaturePropertyManager.GetProperty(CreatureCurrentProperty.HealthCurrent));
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

    void TakeDamage(float damage, HealthModifyType modType );
}
