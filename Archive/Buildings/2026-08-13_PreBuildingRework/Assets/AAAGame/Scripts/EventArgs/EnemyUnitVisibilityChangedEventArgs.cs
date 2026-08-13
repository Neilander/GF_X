using AAAGame.MiniMap.FOG3;
using GameFramework;
using GameFramework.Event;

public sealed class EnemyUnitVisibilityChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(EnemyUnitVisibilityChangedEventArgs).GetHashCode();
    public override int Id => EventId;

    public int EntityId { get; private set; }
    public MAEntity Entity { get; private set; }
    public Fog3CellState OldCellState { get; private set; }
    public Fog3CellState NewCellState { get; private set; }

    public static EnemyUnitVisibilityChangedEventArgs Create(MAEntity entity, Fog3CellState oldCellState, Fog3CellState newCellState)
    {
        EnemyUnitVisibilityChangedEventArgs e = ReferencePool.Acquire<EnemyUnitVisibilityChangedEventArgs>();
        e.Entity = entity;
        e.EntityId = entity != null ? entity.Id : 0;
        e.OldCellState = oldCellState;
        e.NewCellState = newCellState;
        return e;
    }

    public override void Clear()
    {
        EntityId = 0;
        Entity = null;
        OldCellState = Fog3CellState.Outside;
        NewCellState = Fog3CellState.Outside;
    }
}