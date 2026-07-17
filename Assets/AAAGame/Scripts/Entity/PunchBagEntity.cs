using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PunchBagEntity : GeneralCreature
{
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        Side = SideType.EnemySide;
    }

    protected override void OnLogicFrameUpdate(Fix64 deltaTime)
    {
        base.OnLogicFrameUpdate(deltaTime);

        //if(Input.GetKeyDown(KeyCode.T))
        //animator.SetTrigger( "GetHit");
    }

}
