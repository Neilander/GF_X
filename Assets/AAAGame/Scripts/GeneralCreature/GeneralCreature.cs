using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GeneralCreature : EntityBase, ITargetable
{
    public SideType Side { get; protected set; }
    public bool Alive { get; protected set; }
    public ITargetable Instigator { get; set; }
    public GameObject Gmo { get; private set; }
    

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        Instigator = this;
        Alive = true;
        Gmo = gameObject;
        
        
    }

    public void TakeDamage(float damage, HealthModifyType modType)
    {
       
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

    void TakeDamage(float damage, HealthModifyType modType );
}

public interface ICreatureDataContainer
{
    
}

public interface IPropertyManager
{

}
