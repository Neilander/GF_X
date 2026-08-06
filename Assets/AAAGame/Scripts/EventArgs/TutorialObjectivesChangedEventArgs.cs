using GameFramework;
using GameFramework.Event;

public sealed class TutorialObjectivesChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(TutorialObjectivesChangedEventArgs).GetHashCode();

    public override int Id => EventId;

    public static TutorialObjectivesChangedEventArgs Create()
    {
        return ReferencePool.Acquire<TutorialObjectivesChangedEventArgs>();
    }

    public override void Clear()
    {
    }
}
