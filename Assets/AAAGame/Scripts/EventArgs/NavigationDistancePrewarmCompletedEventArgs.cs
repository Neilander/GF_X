using GameFramework.Event;

public sealed class NavigationDistancePrewarmCompletedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(NavigationDistancePrewarmCompletedEventArgs).GetHashCode();
    public override int Id => EventId;

    public int RequestCount { get; internal set; }

    public override void Clear()
    {
        RequestCount = 0;
    }
}
