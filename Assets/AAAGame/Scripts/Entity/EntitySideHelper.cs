using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class EntitySideHelper
{
    public static SideType[] GetHitSide(SideType tp)
    {
        switch (tp)
        {
            case SideType.EnemySide:
                return new[] { SideType.PlayerSide };

            case SideType.PlayerSide:
                return new[] { SideType.EnemySide, SideType.NoSide };

            case SideType.NoSide:
            default:
                return Array.Empty<SideType>();
            
            
           
        }
    }
}
