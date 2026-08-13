using System;

public enum BuildingLevelTechRoute
{
    GlobalRuntime,
    BuildingData,
    ProductionSystem,
    ArmyUnitLevelSystem,
}

public static class BuildingLevelTechRouting
{
    public static BuildingLevelTechRoute Resolve(TechData techData)
    {
        if (techData == null)
            throw new ArgumentNullException(nameof(techData));

        BuildingTable sourceRow = null;
        var rows = LogicRuntimeDataTableCache.BuildingRows;
        for (int i = 0; i < rows.Count; i++)
        {
            if (!ContainsTech(rows[i], techData.Identifier))
                continue;
            sourceRow = rows[i];
            break;
        }
        if (sourceRow == null)
        {
            throw new InvalidOperationException(
                $"Building level tech routing cannot find source building for '{techData.Identifier}'.");
        }

        return Resolve(techData, sourceRow.Type);
    }

    public static BuildingLevelTechRoute Resolve(TechData techData, BuilType sourceBuildingType)
    {
        if (techData == null)
            throw new ArgumentNullException(nameof(techData));

        if (sourceBuildingType == BuilType.Prod)
            return BuildingLevelTechRoute.ProductionSystem;

        if (sourceBuildingType == BuilType.Army)
            return BuildingLevelTechRoute.ArmyUnitLevelSystem;

        bool hasRuntimeValues = techData.UniqueValues != null && techData.UniqueValues.Length > 0;
        bool isDataOnlyLevelTech = techData.ScopeType == TechScopeType.SelfBuil
                                   && !hasRuntimeValues
                                   && string.IsNullOrWhiteSpace(techData.SkillID);
        return isDataOnlyLevelTech
            ? BuildingLevelTechRoute.BuildingData
            : BuildingLevelTechRoute.GlobalRuntime;
    }

    private static bool ContainsTech(BuildingTable row, string techId)
    {
        return row != null
               && !string.IsNullOrWhiteSpace(techId)
               && (string.Equals(row.Tech1ID, techId, StringComparison.Ordinal)
                   || string.Equals(row.Tech2ID, techId, StringComparison.Ordinal)
                   || string.Equals(row.Tech3ID, techId, StringComparison.Ordinal)
                   || string.Equals(row.Tech4ID, techId, StringComparison.Ordinal));
    }
}
