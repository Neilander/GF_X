using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public class GeneralCreature : EntityBase, ITargetable
{
    public SideType Side { get; protected set; }
    public bool Alive { get; protected set; }
    public ITargetable Instigator { get; set; }
    public GameObject Gmo { get; private set; }
    
    public string ReferenceId { get; protected set; }
    
    public Transform display{ get; protected set; }
    public Animator animator { get; protected set; }



    public CreaturePropertyManager CreaturePropertyManager { get; private set; }

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        Instigator = this;
       
        Gmo = gameObject;
        display = transform.Find("Display");
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
       GF.Log("生物受伤，目前只实现了直接扣血");
       CreaturePropertyManager.ModifyCurrentProperty(CreatureCurrentProperty.HealthCurrent,PropertyIrreversibleAdditiveModifier.Create((Fix64)(-damage)), true);
       GF.Log("生物当前血量"+CreaturePropertyManager.GetProperty(CreatureCurrentProperty.HealthCurrent));
    }
}

public enum SideType
{
    NoSide,
    PlayerSide,
    EnemySide
}

public interface ITargetable
{
    SideType Side { get; }
    bool Alive { get; }
    ITargetable Instigator { get; set; }
    GameObject Gmo { get; }
    string ReferenceId { get; }

    void TakeDamage(float damage, HealthModifyType modType );
}
