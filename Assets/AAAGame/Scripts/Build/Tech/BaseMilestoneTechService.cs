using System;
using UnityEngine;

public sealed class BaseMilestoneTechService
{
    private readonly string m_TechIdPattern;

    public BaseMilestoneTechService(string techIdPattern)
    {
        if (string.IsNullOrWhiteSpace(techIdPattern))
            throw new ArgumentException("Base milestone tech id pattern is required.", nameof(techIdPattern));

        m_TechIdPattern = techIdPattern;
    }

    public bool HasArchetypeBaseLevelTech(Archetype archetype, int requiredBaseLevel)
    {
        if (archetype == Archetype.None || requiredBaseLevel <= 0)
            return false;

        int minLevel = Mathf.Clamp(requiredBaseLevel, 1, 3);
        for (int level = minLevel; level <= 3; level++)
        {
            string techId = ResolveTechId(archetype, level);
            if (InGameDataModel.HasUnlockedTech(techId))
                return true;
        }

        return false;
    }

    public void GrantForBuiltBase(BuildingData buildingData, string buildingInstanceId)
    {
        if (!IsValidBaseInstance(buildingData, buildingInstanceId))
            return;

        int maxLevel = Mathf.Clamp(buildingData.Lv, 1, 3);
        for (int level = 1; level <= maxLevel; level++)
        {
            InGameDataModel.UnlockTech(ResolveTechId(buildingData.Arche, level), true, buildingInstanceId);
        }
    }

    public void ReduceForDemolishedBase(BuildingData buildingData, string buildingInstanceId)
    {
        if (!IsValidBaseInstance(buildingData, buildingInstanceId))
            return;

        int maxLevel = Mathf.Clamp(buildingData.Lv, 1, 3);
        for (int level = 1; level <= maxLevel; level++)
        {
            InGameDataModel.ReduceTechStack(ResolveTechId(buildingData.Arche, level), buildingInstanceId, 1);
        }
    }

    private static bool IsValidBaseInstance(BuildingData buildingData, string buildingInstanceId)
    {
        return buildingData != null
            && buildingData.Type == BuilType.Base
            && buildingData.Lv > 0
            && !string.IsNullOrWhiteSpace(buildingInstanceId)
            && buildingData.Arche != Archetype.None;
    }

    private string ResolveTechId(Archetype archetype, int level)
    {
        return string.Format(m_TechIdPattern, archetype, level);
    }
}
