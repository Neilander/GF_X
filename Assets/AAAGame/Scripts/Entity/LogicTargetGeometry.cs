using System;

public static class LogicTargetGeometry
{
    public static Fix64 DistanceToSurface(IEntityContext target, FixVector2 point)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        return LogicWallRuntime.TryGetBranch(target.LogicEntityId, out _)
            ? LogicWallRuntime.DistanceToSurface(target.LogicEntityId, point)
            : target.CombatShape.DistanceToSurface(point);
    }

    public static FixVector2 ClosestPoint(IEntityContext target, FixVector2 point)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        return LogicWallRuntime.TryGetBranch(target.LogicEntityId, out _)
            ? LogicWallRuntime.ClosestPoint(target.LogicEntityId, point)
            : target.CombatShape.ClosestPoint(point);
    }
}
