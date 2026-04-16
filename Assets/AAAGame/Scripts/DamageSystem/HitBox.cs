using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class HitBox : EntityBase
{
    public bool IsActive { get; private set; }
    public ITargetable Owner { get; private set; }

    // 记录：碰撞到的生物 + 碰撞时间
    public Dictionary<ITargetable, float> HitRecords { get; private set; }

    public Damage DamageInfo { get; private set; }


    protected override void OnShow(object userData)
    {
        IsActive = false;
        Owner = null;
        HitRecords = new Dictionary<ITargetable, float>();
        DamageInfo = null;
        base.OnShow(userData);

    }

    /// <summary>
    /// 开启 HitBox
    /// </summary>
    public void Activate(ITargetable owner, Damage damageInfo)
    {
        Owner = owner;
        IsActive = true;
        HitRecords = new Dictionary<ITargetable, float>();
        DamageInfo = damageInfo;
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsActive)
            return;
        //Debug.Log("有东西");

        if (!other.TryGetComponent(out HurtBox target))
            return;

        if (!target.IsActive)
            return;

        if (target.Owner == Owner)
            return;

        if (!target.Owner.Alive)
            return;

        if (!EntitySideHelper.GetHitSide(Owner.Side).Contains(target.Owner.Side))
            return;

        var targetOwner = target.Owner;

        // 如果是第一次碰到，记录时间
        if (!HitRecords.ContainsKey(targetOwner))
        {
            HitRecords.Add(targetOwner, Time.time);
            GF.Log("攻击到了");
            DamageHelper.DoDamage(targetOwner, DamageInfo);
        }
    }


}
