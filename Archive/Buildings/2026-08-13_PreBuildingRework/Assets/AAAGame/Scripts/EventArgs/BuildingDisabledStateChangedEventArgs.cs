using GameFramework;
using GameFramework.Event;

public class BuildingDisabledStateChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(BuildingDisabledStateChangedEventArgs).GetHashCode();
    public override int Id => EventId;

    public int EntityId { get; private set; }
    public string BuildingInstanceId { get; private set; }
    public bool IsDisabled { get; private set; }

    public static BuildingDisabledStateChangedEventArgs Create(int entityId, string buildingInstanceId, bool isDisabled)
    {
        var e = ReferencePool.Acquire<BuildingDisabledStateChangedEventArgs>();
        e.EntityId = entityId;
        e.BuildingInstanceId = buildingInstanceId;
        e.IsDisabled = isDisabled;
        return e;
    }

    public override void Clear()
    {
        EntityId = 0;
        BuildingInstanceId = null;
        IsDisabled = false;
    }
}