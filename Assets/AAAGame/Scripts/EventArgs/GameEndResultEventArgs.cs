using GameFramework;
using GameFramework.Event;

public class GameEndResultEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(GameEndResultEventArgs).GetHashCode();
    public override int Id => EventId;

    public bool IsWin { get; private set; }
    public int FailedObjectiveDefinitionId { get; private set; }

    public static GameEndResultEventArgs CreateWin()
    {
        var instance = ReferencePool.Acquire<GameEndResultEventArgs>();
        instance.IsWin = true;
        instance.FailedObjectiveDefinitionId = 0;
        return instance;
    }

    public static GameEndResultEventArgs CreateFail(int failedObjectiveDefinitionId)
    {
        var instance = ReferencePool.Acquire<GameEndResultEventArgs>();
        instance.IsWin = false;
        instance.FailedObjectiveDefinitionId = failedObjectiveDefinitionId;
        return instance;
    }

    public override void Clear()
    {
        IsWin = false;
        FailedObjectiveDefinitionId = 0;
    }
}
