using System;

public static class TeleportationPointService
{
    private const string CombatTeleportationWindUpConfigKey = "CombatTeleportationWindUp";
    private static readonly System.Collections.Generic.HashSet<string> s_BlockedStrongholdIds = new(StringComparer.Ordinal);

    public static bool IsStrongholdTeleportBlocked(string strongholdId) =>
        !string.IsNullOrWhiteSpace(strongholdId) && s_BlockedStrongholdIds.Contains(strongholdId);

    public static void BlockStrongholdTeleport(string strongholdId)
    {
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new ArgumentException("Stronghold id is required.", nameof(strongholdId));
        s_BlockedStrongholdIds.Add(strongholdId);
        if (LogicTeleportCommandService.IsActive)
            LogicTeleportCommandService.InterruptCombatTeleportsToStronghold(strongholdId);
    }

    public static void ClearPhaseBlocks() => s_BlockedStrongholdIds.Clear();

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        var ids = new System.Collections.Generic.List<string>(s_BlockedStrongholdIds);
        ids.Sort(StringComparer.Ordinal);
        hasher.Add(ids.Count);
        for (int i = 0; i < ids.Count; i++)
            hasher.Add(ids[i]);
    }
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
        if (IsStrongholdTeleportBlocked(strongholdId))
            return false;
        return LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId) == EntitySideHelper.PlayerFactionId;
    }

    public static LogicTeleportCommand SchedulePlayerTeleport(EntityPresetPoint point)
    {
        ValidatePoint(point);
        GamePhase phase = LogicPhaseCommandService.GetRequiredCurrentPhase();

        string strongholdId = GetStrongholdIdRequired(point);
        if (IsStrongholdTeleportBlocked(strongholdId))
            throw new InvalidOperationException($"Teleportation point is blocked for this phase. stronghold={strongholdId}.");
        if (LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId) != EntitySideHelper.PlayerFactionId)
            throw new InvalidOperationException($"Teleportation point is not active because stronghold '{strongholdId}' is not player-owned.");

        IEntityContext player = EntityRegistry.Player
            ?? throw new InvalidOperationException("Teleportation requires a registered player entity.");
        LogicEntityState playerState = player as LogicEntityState
            ?? throw new InvalidOperationException($"Teleportation player is not a LogicEntityState. entity={player.LogicEntityId.Value}.");
        if (!playerState.Alive || !playerState.IsPlayerEntity || !playerState.IsHeroEntity)
            throw new InvalidOperationException($"Teleportation requires a living player hero. entity={player.LogicEntityId.Value}.");

        FixVector2 destination = new FixVector2((Fix64)point.Position.x, (Fix64)point.Position.z);
        return InGameDataModel.IsBuildPhase(phase)
            ? LogicTeleportCommandService.ScheduleForNextFrame(playerState.LogicEntityId, destination, strongholdId)
            : LogicTeleportCommandService.ScheduleCombatTeleport(
                playerState.LogicEntityId,
                destination,
                strongholdId,
                FixedConfigReader.ReadRequiredPositiveFixedConfig(CombatTeleportationWindUpConfigKey));
    }

    private static void ValidatePoint(EntityPresetPoint point)
    {
        if (point == null)
            throw new ArgumentNullException(nameof(point));
        if (point.PointType != EntityPresetPointType.Teleportation)
            throw new ArgumentException($"Point '{point.name}' is not a teleportation point.", nameof(point));
        if (point.TeleportationId < 0)
            throw new InvalidOperationException($"Teleportation point '{point.name}' has an invalid ID {point.TeleportationId}.");

        LevelEntity level = LevelEntity.ActiveLevelEntity
            ?? throw new InvalidOperationException("Teleportation requires an active level entity.");
        if (!point.transform.IsChildOf(level.transform))
            throw new InvalidOperationException($"Teleportation point '{point.name}' does not belong to the active level '{level.name}'.");
    }
}
