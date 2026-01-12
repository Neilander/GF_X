using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HitBox : MonoBehaviour
{
    public bool IsActive { get; private set; }
    public GeneralCreature Owner { get; private set; }

    // 记录：碰撞到的生物 + 碰撞时间
    public Dictionary<GeneralCreature, float> HitRecords { get; private set; }

    /// <summary>
    /// 开启 HitBox
    /// </summary>
    public void Activate(GeneralCreature owner)
    {
        Owner = owner;
        IsActive = true;
        HitRecords = new Dictionary<GeneralCreature, float>();
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsActive)
            return;

        if (!other.TryGetComponent(out HurtBox target))
            return;

        if (!target.IsActive)
            return;

        var targetOwner = target.Owner;

        // 如果是第一次碰到，记录时间
        if (!HitRecords.ContainsKey(targetOwner))
        {
            HitRecords.Add(targetOwner, Time.time);
            GF.Log("攻击到了");
        }
    }
    
    
}
