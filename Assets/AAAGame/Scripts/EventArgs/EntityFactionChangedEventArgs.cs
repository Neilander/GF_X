using GameFramework;
using GameFramework.Event;

public class EntityFactionChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(EntityFactionChangedEventArgs).GetHashCode();
    public override int Id => EventId;

    public int EntityId { get; private set; }
    public int OldFactionId { get; private set; }
    public int NewFactionId { get; private set; }

    public static EntityFactionChangedEventArgs Create(int entityId, int oldFactionId, int newFactionId)
    {
        var e = ReferencePool.Acquire<EntityFactionChangedEventArgs>();
        e.EntityId = entityId;
        e.OldFactionId = oldFactionId;
        e.NewFactionId = newFactionId;
        return e;
    }

    public override void Clear()
    {
        EntityId = 0;
        OldFactionId = 0;
        NewFactionId = 0;
    }
}