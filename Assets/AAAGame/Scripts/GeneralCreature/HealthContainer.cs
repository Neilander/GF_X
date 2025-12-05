using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HealthContainer
{
    public float maxHealth { get; private set; }
    public float currentHealth { get; private set; }

    public void Init(float startHealth)
    {
        maxHealth = startHealth;
        currentHealth = startHealth;
    }

    public float ModifyHealth(HealthModifyType tp,float changeAmount, bool ifTry)
    {
        float returnHealth = 0;

        switch (tp)
        {
            case HealthModifyType.reduce:
                returnHealth = Mathf.Clamp( currentHealth - changeAmount,0, maxHealth);
                break;

            case HealthModifyType.mult:
                returnHealth = Mathf.Clamp( currentHealth*changeAmount,0, maxHealth);
                break;

            case HealthModifyType.set:
                returnHealth = Mathf.Clamp(changeAmount,0, maxHealth);
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
