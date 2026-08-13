public static class TargetThreatUtility
{
    public const string TauntThreatPerLevelConfigKey = "TauntThreatPerLevel";
    public const string TauntAdditionalPursuitDistanceConfigKey = "TauntAdditionalPursuitDistance";
    public const string BuildingExtraThreatConfigKey = "BuildingExtraThreat";

    public static Fix64 ReadThreatPerLevel()
    {
        return DistanceUnitConverter.ReadRequiredPositiveFixedConfig(TauntThreatPerLevelConfigKey);
    }

    public static Fix64 ReadAdditionalPursuitDistanceWorld()
    {
        Fix64 gameDistance = DistanceUnitConverter.ReadRequiredPositiveFixedConfig(
            TauntAdditionalPursuitDistanceConfigKey);
        return DistanceUnitConverter.ConvertToWorld(gameDistance);
    }

    public static Fix64 ReadBuildingExtraThreat()
    {
        return DistanceUnitConverter.ReadRequiredFixedConfig(BuildingExtraThreatConfigKey);
    }

    public static int GetThreatLevel(IEntityContext attacker, IEntityContext target)
    {
        if (attacker == null)
            throw new System.ArgumentNullException(nameof(attacker));
        if (target == null)
            throw new System.ArgumentNullException(nameof(target));

        return target.TauntLevel;
    }

    public static Fix64 CalculateThreat(
        IEntityContext attacker,
        IEntityContext target,
        Fix64 worldSurfaceDistance,
        Fix64 threatPerLevel,
        Fix64 buildingExtraThreat)
    {
        if (worldSurfaceDistance < Fix64.Zero)
            throw new System.ArgumentOutOfRangeException(nameof(worldSurfaceDistance));
        if (threatPerLevel <= Fix64.Zero)
            throw new System.ArgumentOutOfRangeException(nameof(threatPerLevel));

        Fix64 gameDistance = DistanceUnitConverter.ConvertFromWorld(worldSurfaceDistance);
        return CalculatePriorityThreat(attacker, target, threatPerLevel, buildingExtraThreat) - gameDistance;
    }

    public static Fix64 CalculatePriorityThreat(
        IEntityContext attacker,
        IEntityContext target,
        Fix64 threatPerLevel,
        Fix64 buildingExtraThreat)
    {
        if (threatPerLevel <= Fix64.Zero)
            throw new System.ArgumentOutOfRangeException(nameof(threatPerLevel));

        Fix64 targetTypeThreat = target.IsLogicBuilding() ? buildingExtraThreat : Fix64.Zero;
        return threatPerLevel * GetThreatLevel(attacker, target) + targetTypeThreat;
    }

    public static bool HasHigherPriority(
        Fix64 candidateThreat,
        IEntityContext candidate,
        Fix64 currentThreat,
        IEntityContext current)
    {
        if (candidate == null)
            throw new System.ArgumentNullException(nameof(candidate));

        return current == null
               || candidateThreat > currentThreat
               || (candidateThreat == currentThreat && candidate.LogicEntityId < current.LogicEntityId);
    }
}
