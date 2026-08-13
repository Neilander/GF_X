using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Damage
{
    public ITargetable instigator; // 责任源头
    public Fix64 amount;
    public HealthModifyType modType;
    
    // 不带 modType，默认 Additive
    public Damage(ITargetable instigator, Fix64 amount)
    {
        this.instigator = instigator;
        this.amount = amount;
        this.modType = HealthModifyType.reduce;
    }

    // 带 modType
    public Damage(ITargetable instigator, Fix64 amount, HealthModifyType modType)
    {
        this.instigator = instigator;
        this.amount = amount;
        this.modType = modType;
    }
}
