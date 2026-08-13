using GameFramework;
using GameFramework.Event;

public sealed class CreatureHealedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(CreatureHealedEventArgs).GetHashCode();
    public override int Id => EventId;

    public int EntityId { get; private set; }
    public float Amount { get; private set; }

    public static CreatureHealedEventArgs Create(int entityId, float amount)
    {
        CreatureHealedEventArgs e = ReferencePool.Acquire<CreatureHealedEventArgs>();
        e.EntityId = entityId;
        e.Amount = amount;
        return e;
    }

    public override void Clear()
    {
        EntityId = 0;
        Amount = 0f;
    }
}
