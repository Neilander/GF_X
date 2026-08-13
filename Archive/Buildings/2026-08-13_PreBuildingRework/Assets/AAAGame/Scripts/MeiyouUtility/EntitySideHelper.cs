using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class EntitySideHelper
{
    public const int PlayerFactionId = 0;
    public const int EnemyFactionId = 1;

    public static int ToFactionId(SideType side)
    {
        switch (side)
        {
            case SideType.PlayerSide:
                return PlayerFactionId;
            case SideType.EnemySide:
                return EnemyFactionId;
            default:
                return -1;
        }
    }

    public static SideType ToSide(int factionId)
    {
        switch (factionId)
        {
            case PlayerFactionId:
                return SideType.PlayerSide;
            case EnemyFactionId:
                return SideType.EnemySide;
            default:
                return SideType.NoSide;
        }
    }

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
