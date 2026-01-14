using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HurtBox : MonoBehaviour
{
    
    public bool IsActive { get; private set; }
    public ITargetable Owner { get; private set; }

    public void Activate(ITargetable owner)
    {
        Owner = owner;
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
        Owner = null;
    }
}
