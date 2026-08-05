using System;
using System.Collections.Generic;

public static class LogicBuildingQueryService
{
    public static bool TryGetNearestByInstanceIds(
        HashSet<string> buildingInstanceIds,
        FixVector2 origin,
        out IBuildingLogicContext building)
    {
        if (buildingInstanceIds == null)
            throw new ArgumentNullException(nameof(buildingInstanceIds));

        building = null;
        Fix64 bestDistanceSquared = Fix64.Zero;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!entities[i].TryGetLogicBuilding(out IBuildingLogicContext candidate)
                || !candidate.Alive
                || string.IsNullOrWhiteSpace(candidate.BuildingInstanceId)
                || !buildingInstanceIds.Contains(candidate.BuildingInstanceId))
            {
                continue;
            }

            Fix64 distanceSquared = FixVector2.SqrMagnitude(candidate.LogicFramePositionFixed() - origin);
            if (building != null
                && (distanceSquared > bestDistanceSquared
                    || (distanceSquared == bestDistanceSquared
                        && candidate.LogicEntityId.Value > building.LogicEntityId.Value)))
            {
                continue;
            }

            building = candidate;
            bestDistanceSquared = distanceSquared;
        }

        return building != null;
    }

    public static IBuildingLogicContext GetRequiredByInstanceId(string buildingInstanceId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            throw new ArgumentException("buildingInstanceId is required.", nameof(buildingInstanceId));

        IBuildingLogicContext result = null;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!entities[i].TryGetLogicBuilding(out IBuildingLogicContext building)
                || !building.Alive
                || !string.Equals(building.BuildingInstanceId, buildingInstanceId, StringComparison.Ordinal))
            {
                continue;
            }

            if (result != null)
            {
                throw new InvalidOperationException(
                    $"LogicBuildingQueryService found duplicate active building instance id '{buildingInstanceId}'.");
            }

            result = building;
        }

        return result ?? throw new InvalidOperationException(
            $"LogicBuildingQueryService cannot find active building instance id '{buildingInstanceId}'.");
    }

    public static bool HasBuildingArchetype(
        IBuildingLogicContext source,
        Archetype archetype)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        return HasBuildingArchetype(source.StrongholdId, source.OwnerFactionId, archetype);
    }

    public static bool HasBuildingArchetype(
        string strongholdId,
        int ownerFactionId,
        Archetype archetype)
    {
        if (string.IsNullOrWhiteSpace(strongholdId))
            return false;

        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!entities[i].TryGetLogicBuilding(out IBuildingLogicContext building)
                || !building.Alive
                || building.OwnerFactionId != ownerFactionId
                || !string.Equals(building.StrongholdId, strongholdId, StringComparison.Ordinal))
            {
                continue;
            }

            if (building.BuildingData == null)
            {
                throw new InvalidOperationException(
                    $"Logic building {building.LogicEntityId.Value} has no BuildingData.");
            }

            if (building.BuildingData.Lv == 0)
                continue;

            if (building.BuildingData.Arche == archetype)
                return true;
        }

        return false;
    }

    public static bool TryResolveStrongholdOwnerFaction(string strongholdId, out int ownerFactionId)
    {
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new ArgumentException("Stronghold id is empty.", nameof(strongholdId));

        return LogicStrongholdMap.TryGetOwnerFactionId(strongholdId, out ownerFactionId);
    }

    public static bool TryResolveOwnedStrongholdAtPosition(
        FixVector2 position,
        int ownerFactionId,
        out string strongholdId)
    {
        if (!LogicStrongholdMap.TryResolveStrongholdId(position, out strongholdId))
            return false;
        if (!TryResolveStrongholdOwnerFaction(strongholdId, out int actualOwnerFactionId)
            || actualOwnerFactionId != ownerFactionId)
        {
            strongholdId = null;
            return false;
        }

        return true;
    }

    public static bool HasDifferentArmyArchetype(IBuildingLogicContext source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (source.BuildingData == null)
            throw new InvalidOperationException($"Logic building {source.LogicEntityId.Value} has no BuildingData.");
        if (source.BuildingData.Type != BuilType.Army || string.IsNullOrWhiteSpace(source.StrongholdId))
            return false;

        Archetype sourceArchetype = source.BuildingData.Arche;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!entities[i].TryGetLogicBuilding(out IBuildingLogicContext building)
                || building.LogicEntityId == source.LogicEntityId
                || !building.Alive
                || building.OwnerFactionId != source.OwnerFactionId
                || !string.Equals(building.StrongholdId, source.StrongholdId, StringComparison.Ordinal))
            {
                continue;
            }

            if (building.BuildingData == null)
            {
                throw new InvalidOperationException(
                    $"Logic building {building.LogicEntityId.Value} has no BuildingData.");
            }

            if (building.BuildingData.Type == BuilType.Army
                && building.BuildingData.Arche != Archetype.None
                && building.BuildingData.Arche != sourceArchetype)
            {
                return true;
            }
        }

        return false;
    }
}
