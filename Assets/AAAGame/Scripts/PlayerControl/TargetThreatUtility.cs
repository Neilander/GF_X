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

    public static int GetTauntEquivalentLevel(IEntityContext attacker, IEntityContext target)
    {
        if (attacker == null)
            throw new System.ArgumentNullException(nameof(attacker));
        if (target == null)
            throw new System.ArgumentNullException(nameof(target));

        int threatLevel = target.TauntLevel;
        if (attacker.TryGetLogicBuilding(out IBuildingLogicContext building)
            && BuildingAbilityIds.IsBuilding(
                building.BuildingData,
                BuildingAbilityIds.ComplaintsDepartment)
            && BuildingTechRuntimeEffect.HasTag(target.CharacterData?.UnitTags, UnitTag.Ranged))
        {
            Fix64[] values = building.BuildingData.UniqueValues;
            if (values == null || values.Length == 0 || values[0] <= Fix64.Zero)
            {
                throw new System.InvalidOperationException(
                    $"Complaints department ranged taunt bonus is missing. building={building.BuildingData.Identifier}.");
            }

            threatLevel = checked(threatLevel + (int)values[0]);
        }

        return threatLevel;
    }

    public static Fix64 CalculateSelectionThreat(
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

        // Distance and future non-taunt targeting modifiers belong only in this selection score.
        Fix64 gameDistance = DistanceUnitConverter.ConvertFromWorld(worldSurfaceDistance);
        return CalculatePursuitThreat(attacker, target, threatPerLevel, buildingExtraThreat) - gameDistance;
    }

    // Only taunt-equivalent sources belong here because this score unlocks additional pursuit.
    public static Fix64 CalculatePursuitThreat(
        IEntityContext attacker,
        IEntityContext target,
        Fix64 threatPerLevel,
        Fix64 buildingExtraThreat)
    {
        if (threatPerLevel <= Fix64.Zero)
            throw new System.ArgumentOutOfRangeException(nameof(threatPerLevel));

        Fix64 buildingTauntEquivalentThreat = target.IsLogicBuilding() ? buildingExtraThreat : Fix64.Zero;
        return threatPerLevel * GetTauntEquivalentLevel(attacker, target) + buildingTauntEquivalentThreat;
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
