using System.Collections.Generic;
using GameFramework;
using GameFramework.Event;

public class InteractionFocusChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(InteractionFocusChangedEventArgs).GetHashCode();
    public override int Id => EventId;

    public InteractionHost Target { get; private set; }

    public static InteractionFocusChangedEventArgs Create(InteractionHost target)
    {
        var e = ReferencePool.Acquire<InteractionFocusChangedEventArgs>();
        e.Target = target;
        return e;
    }

    public override void Clear()
    {
        Target = null;
    }
}
