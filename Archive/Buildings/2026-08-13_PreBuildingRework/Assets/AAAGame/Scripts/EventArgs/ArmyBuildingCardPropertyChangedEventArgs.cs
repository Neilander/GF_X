using GameFramework;
using GameFramework.Event;

/// <summary>
/// Army 建筑卡牌属性变化事件。
/// </summary>
public sealed class ArmyBuildingCardPropertyChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(ArmyBuildingCardPropertyChangedEventArgs).GetHashCode();

    public override int Id => EventId;

    public int EntityId { get; private set; }
    public string BuildingInstanceId { get; private set; }

    public int ArmyForce { get; private set; }

    public int SupplyPerUnit { get; private set; }

    public int OccupiedSupply { get; private set; }

    public static ArmyBuildingCardPropertyChangedEventArgs Create(
        int entityId,
        string buildingInstanceId,
        int armyForce,
        int supplyPerUnit,
        int occupiedSupply)
    {
        var e = ReferencePool.Acquire<ArmyBuildingCardPropertyChangedEventArgs>();
        e.EntityId = entityId;
        e.BuildingInstanceId = buildingInstanceId;
        e.ArmyForce = armyForce;
        e.SupplyPerUnit = supplyPerUnit;
        e.OccupiedSupply = occupiedSupply;
        return e;
    }

    public override void Clear()
    {
        EntityId = 0;
        BuildingInstanceId = null;
        ArmyForce = 0;
        SupplyPerUnit = 0;
        OccupiedSupply = 0;
    }
}
