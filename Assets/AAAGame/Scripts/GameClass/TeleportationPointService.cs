using System;

public static class TeleportationPointService
{
    public static string GetStrongholdIdRequired(EntityPresetPoint point)
    {
        ValidatePoint(point);
        if (!LogicStrongholdMap.IsInitialized)
            throw new InvalidOperationException("Teleportation point cannot resolve its stronghold before LogicStrongholdMap is initialized.");

        var position = new FixVector2((Fix64)point.Position.x, (Fix64)point.Position.z);
        if (!LogicStrongholdMap.TryResolveStrongholdId(position, out string strongholdId))
        {
            throw new InvalidOperationException(
                $"Teleportation point is outside every stronghold. point={point.name}, position={point.Position}.");
        }

        return strongholdId;
    }

    public static bool IsPlayerOwned(EntityPresetPoint point)
    {
        string strongholdId = GetStrongholdIdRequired(point);
        return LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId) == EntitySideHelper.PlayerFactionId;
    }

    public static LogicTeleportCommand SchedulePlayerTeleport(EntityPresetPoint point)
    {
        ValidatePoint(point);
        if (!InGameDataModel.IsBuildPhase(PhaseManager.CurrentPhase))
            throw new InvalidOperationException($"Teleportation is only available during a build phase. phase={PhaseManager.CurrentPhase}.");

        string strongholdId = GetStrongholdIdRequired(point);
        if (LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId) != EntitySideHelper.PlayerFactionId)
            throw new InvalidOperationException($"Teleportation point is not active because stronghold '{strongholdId}' is not player-owned.");

        IEntityContext player = EntityRegistry.Player
            ?? throw new InvalidOperationException("Teleportation requires a registered player entity.");
        LogicEntityState playerState = player as LogicEntityState
            ?? throw new InvalidOperationException($"Teleportation player is not a LogicEntityState. entity={player.LogicEntityId.Value}.");
        if (!playerState.Alive || !playerState.IsPlayerEntity || !playerState.IsHeroEntity)
            throw new InvalidOperationException($"Teleportation requires a living player hero. entity={player.LogicEntityId.Value}.");

        return LogicTeleportCommandService.ScheduleForNextFrame(
            playerState.LogicEntityId,
            new FixVector2((Fix64)point.Position.x, (Fix64)point.Position.z),
            strongholdId);
    }

    private static void ValidatePoint(EntityPresetPoint point)
    {
        if (point == null)
            throw new ArgumentNullException(nameof(point));
        if (point.PointType != EntityPresetPointType.Teleportation)
            throw new ArgumentException($"Point '{point.name}' is not a teleportation point.", nameof(point));

        LevelEntity level = LevelEntity.ActiveLevelEntity
            ?? throw new InvalidOperationException("Teleportation requires an active level entity.");
        if (!point.transform.IsChildOf(level.transform))
            throw new InvalidOperationException($"Teleportation point '{point.name}' does not belong to the active level '{level.name}'.");
    }
}
