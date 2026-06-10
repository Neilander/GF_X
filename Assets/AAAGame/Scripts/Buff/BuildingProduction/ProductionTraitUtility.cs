using UnityEngine;
using UnityGameFramework.Runtime;

public static class ProductionTraitUtility
{
    public static bool IsBuilding(BuildingEntity building, string baseIdentifier)
    {
        return building?.buildingData?.Identifier != null
               && building.buildingData.Identifier.StartsWith(baseIdentifier, System.StringComparison.Ordinal);
    }

    public static int GetUniqueInt(BuildingEntity building, int index, int fallback)
    {
        if (building?.buildingData?.UniqueValues == null || index < 0 || index >= building.buildingData.UniqueValues.Length)
            return fallback;

        int value = (int)building.buildingData.UniqueValues[index];
        return value > 0 ? value : fallback;
    }

    public static int GetCurrentDay()
    {
        return Mathf.Max(1, InGameDataModel.GetValue(IngameValueType.Day));
    }

    public static int GetPreviousDay()
    {
        return Mathf.Max(1, GetCurrentDay() - 1);
    }

    public static void SetFinalProduction(BuildingEntity building, int finalProduction)
    {
        if (building == null)
            return;

        int baseWithTech = building.GetBaseProductionWithTechExtra();
        building.SetDynamicProduction(Mathf.Max(0, finalProduction) - baseWithTech);
    }

    public static int GetProdLevelTechValueSum(BuildingEntity building, int uniqueValueIndex)
    {
        if (building?.buildingData == null || uniqueValueIndex < 0)
            return 0;

        if (building.buildingData.Type != BuilType.Prod)
            return 0;

        BuildingTable row = FindSourceRow(building);
        if (row == null || row.Type != BuilType.Prod)
            return 0;

        int total = 0;
        if (building.buildingData.Lv >= 2)
            total += GetTechUniqueValue(row.Tech1UniqueValues, uniqueValueIndex);
        if (building.buildingData.Lv >= 3)
            total += GetTechUniqueValue(row.Tech2UniqueValues, uniqueValueIndex);

        return total;
    }

    private static int GetTechUniqueValue(Fix64[] values, int index)
    {
        if (values == null || index < 0 || index >= values.Length)
            return 0;

        return Mathf.Max(0, (int)values[index]);
    }

    private static BuildingTable FindSourceRow(BuildingEntity building)
    {
        string baseIdentifier = GetBaseIdentifier(building.buildingData.Identifier);
        if (string.IsNullOrWhiteSpace(baseIdentifier) || GF.DataTable == null)
            return null;

        var table = GF.DataTable.GetDataTable<BuildingTable>();
        return table?.GetDataRow(row => row.Identifier == baseIdentifier);
    }

    private static string GetBaseIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        int lvIndex = identifier.LastIndexOf("_Lv", System.StringComparison.Ordinal);
        return lvIndex > 0 ? identifier.Substring(0, lvIndex) : identifier;
    }
}
