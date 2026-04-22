using GameFramework;
using GameFramework.Event;
using UnityEngine;

public sealed class SoldierDeadEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(SoldierDeadEventArgs).GetHashCode();
    public override int Id => EventId;

    public int VictimEntityId { get; private set; }
    public SideType VictimSide { get; private set; }
    public int VictimSupply { get; private set; }
    public Vector3 WorldPosition { get; private set; }

    public static SoldierDeadEventArgs Create(SoldierEntity victim)
    {
        SoldierDeadEventArgs e = ReferencePool.Acquire<SoldierDeadEventArgs>();
        if (victim == null)
        {
            e.VictimEntityId = 0;
            e.VictimSide = SideType.NoSide;
            e.VictimSupply = 0;
            e.WorldPosition = Vector3.zero;
            return e;
        }

        e.VictimEntityId = victim.Id;
        e.VictimSide = victim.Side;
        e.VictimSupply = victim.CharacterData != null ? Mathf.Max(0, victim.CharacterData.Supply) : 0;
        e.WorldPosition = victim.transform.position;
        return e;
    }

    public override void Clear()
    {
        VictimEntityId = 0;
        VictimSide = SideType.NoSide;
        VictimSupply = 0;
        WorldPosition = Vector3.zero;
    }
}