using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HealthContainer
{
    public Fix64 maxHealth { get; private set; }
    public Fix64 currentHealth { get; private set; }

    public void Init(Fix64 startHealth)
    {
        maxHealth = startHealth;
        currentHealth = startHealth;
    }

    public Fix64 ModifyHealth(HealthModifyType tp, Fix64 changeAmount, bool ifTry)
    {
        Fix64 returnHealth = Fix64.Zero;

        switch (tp)
        {
            case HealthModifyType.reduce:
                returnHealth = Fix64.Clamp(currentHealth - changeAmount, Fix64.Zero, maxHealth);
                break;

            case HealthModifyType.mult:
                returnHealth = Fix64.Clamp(currentHealth * changeAmount, Fix64.Zero, maxHealth);
                break;

            case HealthModifyType.set:
                returnHealth = Fix64.Clamp(changeAmount, Fix64.Zero, maxHealth);
                break;

        }

        if (!ifTry)
        {
            currentHealth = returnHealth;
        }
        return returnHealth;
    }

}

public enum HealthModifyType
{
    reduce,
    mult,
    set,
    empty

}
