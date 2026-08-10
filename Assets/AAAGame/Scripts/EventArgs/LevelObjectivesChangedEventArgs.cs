using GameFramework;
using GameFramework.Event;

public sealed class LevelObjectivesChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(LevelObjectivesChangedEventArgs).GetHashCode();

    public override int Id => EventId;

    public static LevelObjectivesChangedEventArgs Create()
    {
        return ReferencePool.Acquire<LevelObjectivesChangedEventArgs>();
    }

    public override void Clear()
    {
    }
}
