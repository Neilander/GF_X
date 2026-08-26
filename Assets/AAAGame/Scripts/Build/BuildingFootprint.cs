using System;

public static class BuildingFootprint
{
    public const float GridCellWorldSize = 1f;
    public const string ColliderObjectName = "_BuildingFootprint";

    public static int ResolveGridSize(BuilType buildingType)
    {
        return buildingType switch
        {
            BuilType.Wall => 1,
            BuilType.Prod => 2,
            BuilType.Def => 2,
            BuilType.Army => 3,
            BuilType.Base => 5,
            _ => throw new ArgumentOutOfRangeException(
                nameof(buildingType),
                buildingType,
                "Building type has no configured footprint."),
        };
    }

    public static float ResolveWorldSize(BuilType buildingType)
    {
        return ResolveGridSize(buildingType) * GridCellWorldSize;
    }

    public static string ResolveLdtkEntityIdentifier(BuilType buildingType)
    {
        int gridSize = ResolveGridSize(buildingType);
        return $"Building{gridSize}{gridSize}";
    }
}
