using GameFramework;
using GameFramework.Event;

/// <summary>
/// 生物血量变化事件。
/// 由 GeneralCreature.TakeDamage 发出，HealthBarComp 接收。
/// 通过 EntityId 区分不同单位，避免事件串台。
/// </summary>
public class CreatureHealthChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(CreatureHealthChangedEventArgs).GetHashCode();
    public override int Id => EventId;

    public int EntityId { get; private set; }
    public float CurrentHealth { get; private set; }
    public float MaxHealth { get; private set; }
    public float Delta { get; private set; }

    public static CreatureHealthChangedEventArgs Create(int entityId, float current, float max, float delta)
    {
        var e = ReferencePool.Acquire<CreatureHealthChangedEventArgs>();
        e.EntityId = entityId;
        e.CurrentHealth = current;
        e.MaxHealth = max;
        e.Delta = delta;
        return e;
    }

    public override void Clear()
    {
        EntityId = 0;
        CurrentHealth = 0f;
        MaxHealth = 0f;
        Delta = 0f;
    }
}
