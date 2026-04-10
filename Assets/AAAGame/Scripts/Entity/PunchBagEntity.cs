using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PunchBagEntity : GeneralCreature
{
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        FactionId = 1;
        TeamId = EntityCombatTeamHelper.ResolveTeamIdByFaction(FactionId);
    }

    private void Update()
    {
        //if(Input.GetKeyDown(KeyCode.T))
            //animator.SetTrigger( "GetHit");
    }
    
}
