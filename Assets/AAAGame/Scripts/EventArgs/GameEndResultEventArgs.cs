using GameFramework;
using GameFramework.Event;

public class GameEndResultEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(GameEndResultEventArgs).GetHashCode();
    public override int Id => EventId;

    public bool IsWin { get; private set; }
    public string FailedObjectiveIdentifier { get; private set; }

    public static GameEndResultEventArgs CreateWin()
    {
        var instance = ReferencePool.Acquire<GameEndResultEventArgs>();
        instance.IsWin = true;
        instance.FailedObjectiveIdentifier = null;
        return instance;
    }

    public static GameEndResultEventArgs CreateFail(string failedObjectiveIdentifier)
    {
        var instance = ReferencePool.Acquire<GameEndResultEventArgs>();
        instance.IsWin = false;
        instance.FailedObjectiveIdentifier = failedObjectiveIdentifier;
        return instance;
    }

    public override void Clear()
    {
        IsWin = false;
        FailedObjectiveIdentifier = null;
    }
}
