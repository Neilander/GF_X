using GameFramework;
using GameFramework.Event;

/// <summary>
/// 战斗阶段切换事件。
/// </summary>
public class IngamePhaseChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(IngamePhaseChangedEventArgs).GetHashCode();

    public override int Id => EventId;

    public GamePhase OldPhase { get; private set; }

    public GamePhase NewPhase { get; private set; }

    public static IngamePhaseChangedEventArgs Create(GamePhase oldPhase, GamePhase newPhase)
    {
        var e = ReferencePool.Acquire<IngamePhaseChangedEventArgs>();
        e.OldPhase = oldPhase;
        e.NewPhase = newPhase;
        return e;
    }

    public override void Clear()
    {
        OldPhase = default;
        NewPhase = default;
    }
}