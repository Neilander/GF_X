using GameFramework;
using GameFramework.Event;

public class GameEndResultEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(GameEndResultEventArgs).GetHashCode();
    public override int Id => EventId;

    public bool IsWin { get; private set; }
    public VictoryConditionType? VictoryCondition { get; private set; }
    public FailConditionType? FailCondition { get; private set; }

    public static GameEndResultEventArgs CreateWin(VictoryConditionType condition)
    {
        var e = ReferencePool.Acquire<GameEndResultEventArgs>();
        e.IsWin = true;
        e.VictoryCondition = condition;
        e.FailCondition = null;
        return e;
    }

    public static GameEndResultEventArgs CreateFail(FailConditionType condition)
    {
        var e = ReferencePool.Acquire<GameEndResultEventArgs>();
        e.IsWin = false;
        e.VictoryCondition = null;
        e.FailCondition = condition;
        return e;
    }

    public override void Clear()
    {
        IsWin = false;
        VictoryCondition = null;
        FailCondition = null;
    }
}