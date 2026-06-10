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
    public UnitSize VictimSize { get; private set; }
    public Vector3 WorldPosition { get; private set; }

    public static SoldierDeadEventArgs Create(SoldierEntity victim)
    {
        SoldierDeadEventArgs e = ReferencePool.Acquire<SoldierDeadEventArgs>();
        if (victim == null)
        {
            e.VictimEntityId = 0;
            e.VictimSide = SideType.NoSide;
            e.VictimSupply = 0;
            e.VictimSize = UnitSize.Small;
            e.WorldPosition = Vector3.zero;
            return e;
        }

        e.VictimEntityId = victim.Id;
        e.VictimSide = victim.Side;
        e.VictimSupply = victim.CharacterData != null ? Mathf.Max(0, victim.CharacterData.Supply) : 0;
        e.VictimSize = victim.CharacterData != null ? victim.CharacterData.Size : UnitSize.Small;
        e.WorldPosition = ResolveVictimWorldPosition(victim);
        return e;
    }

    private static Vector3 ResolveVictimWorldPosition(SoldierEntity victim)
    {
        Collider[] colliders = victim.GetComponentsInChildren<Collider>(true);
        Bounds mergedBounds = default;
        bool hasBounds = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null || !col.enabled)
                continue;

            if (!hasBounds)
            {
                mergedBounds = col.bounds;
                hasBounds = true;
            }
            else
            {
                mergedBounds.Encapsulate(col.bounds);
            }
        }

        if (hasBounds)
            return mergedBounds.center;

        return victim.transform.position;
    }

    public override void Clear()
    {
        VictimEntityId = 0;
        VictimSide = SideType.NoSide;
        VictimSupply = 0;
        VictimSize = UnitSize.Small;
        WorldPosition = Vector3.zero;
    }
}
