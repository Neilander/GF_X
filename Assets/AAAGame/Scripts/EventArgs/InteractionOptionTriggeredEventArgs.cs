using GameFramework;
using GameFramework.Event;
using UnityEngine;

public class InteractionOptionTriggeredEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(InteractionOptionTriggeredEventArgs).GetHashCode();
    public override int Id => EventId;

    public InteractionHost Target { get; private set; }
    public IInteractionOption Option { get; private set; }

    public static InteractionOptionTriggeredEventArgs Create(InteractionHost target, IInteractionOption option)
    {
        var e = ReferencePool.Acquire<InteractionOptionTriggeredEventArgs>();
        e.Target = target;
        e.Option = option;
        return e;
    }

    public override void Clear()
    {
        Target = null;
        Option = null;
    }
}
