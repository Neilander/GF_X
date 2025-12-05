using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GeneralCreature : EntityBase, ITargetable
{
    public SideType Side { get; protected set; }
    public bool Alive { get; protected set; }
    public ITargetable Instigator { get; set; }
    public GameObject Gmo { get; private set; }
    
    public string ReferenceId { get; protected set; }


    private CreaturePropertyManager _creaturePropertyManager;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        Instigator = this;
        Alive = true;
        Gmo = gameObject;
        ReferenceId = "Knight";
        _creaturePropertyManager = new CreaturePropertyManager(ReferenceId);
        
    }

    public void TakeDamage(float damage, HealthModifyType modType)
    {
       GF.Log("生物受伤，但是并没Implement");
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
