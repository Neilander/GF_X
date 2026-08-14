using System;
using System.Collections.Generic;

public static class LogicBuildingProductionService
{
    private const int DefaultBonusPerStep = 1;
    private const int DefaultMaxBonusTwo = 2;
    private const int DefaultMaxBonusThree = 3;
    private const int DefaultKillsPerBonus = 1;
    private const int DefaultDayInterval = 1;
    private const int DefaultIncreasePerStep = 1;
    private const int DefaultSurvivorsPerBonus = 3;
    private const int DefaultSouvenirBonusPerTroop = 5;
    private const int DefaultDecreasePerStep = 1;
    private const int DefaultMinProduction = 1;
    private const int DefaultTicketDayInterval = 4;

    public static void Configure(IBuildingLogicContext building)
    {
        EnsureProductionBuilding(building);
        BuildingExtraProps props = building.ProductionProps
            ?? throw new InvalidOperationException($"LogicBuildingProductionService.Configure missing production props. building={building.BuildingInstanceId}.");
        string identifier = RequireIdentifier(building);
        if (identifier.StartsWith("Buil_SouvenirStand", StringComparison.Ordinal))
            props.ProductionType = ProductionType.ByTroopCount;
        else if (identifier.StartsWith("Buil_MiningRig", StringComparison.Ordinal))
            props.ProductionType = ProductionType.DecreasingOutput;
        else if (identifier.StartsWith("Buil_Nursery", StringComparison.Ordinal))
            props.ProductionType = ProductionType.IncreasingOutput;
        else if (identifier.StartsWith("Buil_ReceptionDesk", StringComparison.Ordinal))
            props.ProductionType = ProductionType.BySurvivorCount;
        else if (identifier.StartsWith("Buil_FreshMarket", StringComparison.Ordinal))
            props.ProductionType = ProductionType.DecreasingByOtherBuildingCount;
        else if (identifier.StartsWith("Buil_CampfireGrill", StringComparison.Ordinal))
            props.ProductionType = ProductionType.IncreasingByOtherBuildingCount;
        else if (identifier.StartsWith("Buil_PackageRack", StringComparison.Ordinal))
            props.ProductionType = ProductionType.StoredUntilDestroyed;
        else if (identifier.StartsWith("Buil_TicketBooth", StringComparison.Ordinal))
        {
            props.ProductionType = ProductionType.PeriodicOutput;
            if (props.ProductionTraitFirstDay <= 0)
                props.ProductionTraitFirstDay = CurrentDay();
        }

        Refresh(building);
    }

    public static void RefreshAll(bool accrueBuildPhaseState = false)
    {
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        var buildings = new List<IBuildingLogicContext>();
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] is IBuildingLogicContext building
                && building.Alive
                && building.BuildingData != null
                && building.BuildingData.Type == BuilType.Prod)
                buildings.Add(building);
        }

        buildings.Sort((left, right) => left.LogicEntityId.Value.CompareTo(right.LogicEntityId.Value));
        for (int i = 0; i < buildings.Count; i++)
        {
            if (accrueBuildPhaseState
                && IsPackageRack(buildings[i]))
            {
                buildings[i].ProductionProps.StoredProduction = checked(
                    buildings[i].ProductionProps.StoredProduction + BaseWithTech(buildings[i]));
            }
            Refresh(buildings[i]);
        }
    }

    public static void ReconfigureByBuildingInstanceId(string buildingInstanceId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            throw new ArgumentException("Building instance id is empty.", nameof(buildingInstanceId));

        IList<IEntityContext> active = EntityRegistry.AllEntities;
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i] is IBuildingLogicContext building
                && string.Equals(building.BuildingInstanceId, buildingInstanceId, StringComparison.Ordinal)
                && building.BuildingData?.Type == BuilType.Prod)
            {
                Configure(building);
            }
        }

        LogicEntityState[] pending = LogicEntityStateStore.CapturePendingSpawnStates();
        for (int i = 0; i < pending.Length; i++)
        {
            LogicEntityState building = pending[i];
            if (building.IsBuildingEntity
                && string.Equals(building.BuildingInstanceId, buildingInstanceId, StringComparison.Ordinal)
                && building.BuildingData?.Type == BuilType.Prod)
            {
                Configure(building);
            }
        }
    }

    public static void PrepareBuildPhase(bool grantsIncome)
    {
        int currentDay = CurrentDay();
        if (grantsIncome)
            LogicProductionConditionState.SnapshotSurvivors(Math.Max(1, currentDay - 1));
        RefreshAll(grantsIncome);
    }

    public static void Refresh(IBuildingLogicContext building)
    {
        EnsureProductionBuilding(building);
        string identifier = RequireIdentifier(building);
        if (identifier.StartsWith("Buil_SouvenirStand", StringComparison.Ordinal))
        {
            int troops = LogicProductionConditionState.GetTroopCount(building.StrongholdId);
            int bonus = troops / Math.Max(1, Unique(building, 0, DefaultSouvenirBonusPerTroop));
            SetConditionAndDynamic(building, troops, bonus, MaxBonus(building, 2, DefaultMaxBonusThree));
        }
        else if (identifier.StartsWith("Buil_MiningRig", StringComparison.Ordinal))
        {
            int baseProduction = BaseWithTech(building);
            int firstDay = building.ProductionProps.ProductionTraitFirstDay;
            if (firstDay <= 0)
            {
                SetFinalProduction(building, baseProduction);
                building.ProductionProps.ConditionCount = 0;
            }
            else
            {
                int elapsed = Math.Max(0, CurrentDay() - firstDay);
                int steps = elapsed / Math.Max(1, Unique(building, 0, DefaultDayInterval));
                int production = Math.Max(
                    baseProduction - steps * Unique(building, 1, DefaultDecreasePerStep),
                    Unique(building, 2, DefaultMinProduction) + LevelTech(building, 0));
                SetFinalProduction(building, production);
                building.ProductionProps.ConditionCount = elapsed;
            }
        }
        else if (identifier.StartsWith("Buil_Nursery", StringComparison.Ordinal))
        {
            int firstDay = building.ProductionProps.ProductionTraitFirstDay;
            int elapsed = firstDay <= 0 ? 0 : Math.Max(0, CurrentDay() - firstDay);
            int steps = elapsed / Math.Max(1, Unique(building, 0, DefaultDayInterval));
            int bonus = steps * Math.Max(1, Unique(building, 1, DefaultIncreasePerStep));
            SetConditionAndDynamic(building, elapsed, bonus, MaxBonus(building, 2, DefaultMaxBonusThree));
        }
        else if (identifier.StartsWith("Buil_ReceptionDesk", StringComparison.Ordinal))
        {
            int survivors = LogicProductionConditionState.GetSurvivorCount(building.StrongholdId, PreviousDay());
            int steps = survivors / Math.Max(1, Unique(building, 0, DefaultSurvivorsPerBonus));
            int bonus = steps * Math.Max(1, Unique(building, 1, DefaultBonusPerStep));
            SetConditionAndDynamic(building, survivors, bonus, MaxBonus(building, 2, DefaultMaxBonusThree));
        }
        else if (identifier.StartsWith("Buil_FreshMarket", StringComparison.Ordinal))
        {
            int other = GetOtherBuildingCount(building);
            int steps = other / Math.Max(1, Unique(building, 0, 2));
            int penaltyPerStep = Unique(building, 1, DefaultDecreasePerStep) + LevelTech(building, 0);
            int floor = Unique(building, 2, DefaultMinProduction);
            building.ProductionProps.ConditionCount = other;
            SetFinalProduction(building, Math.Max(BaseWithTech(building) - steps * penaltyPerStep, floor));
        }
        else if (identifier.StartsWith("Buil_CampfireGrill", StringComparison.Ordinal))
        {
            int other = GetOtherBuildingCount(building);
            int steps = other / Math.Max(1, Unique(building, 0, 2));
            int bonus = steps * Unique(building, 1, DefaultBonusPerStep);
            int cap = Unique(building, 2, DefaultMaxBonusTwo) + LevelTech(building, 0);
            SetConditionAndDynamic(building, other, bonus, cap);
        }
        else if (identifier.StartsWith("Buil_PackageRack", StringComparison.Ordinal))
        {
            building.ProductionProps.ConditionCount = building.ProductionProps.StoredProduction;
            SetFinalProduction(building, 0);
        }
        else if (identifier.StartsWith("Buil_TicketBooth", StringComparison.Ordinal))
        {
            int firstDay = building.ProductionProps.ProductionTraitFirstDay;
            int elapsed = Math.Max(0, CurrentDay() - firstDay);
            int interval = Math.Max(1, Unique(building, 0, DefaultTicketDayInterval) - LevelTech(building, 0));
            building.ProductionProps.ConditionCount = elapsed;
            SetFinalProduction(building, elapsed > 0 && elapsed % interval == 0 ? BaseWithTech(building) : 0);
        }
    }

    public static int GetProduction(IBuildingLogicContext building)
    {
        EnsureProductionBuilding(building);
        Fix64 total = (Fix64)building.BuildingData.Production
                      + building.ProductionProps.Production
                      + building.ProductionProps.DynamicProduction;
        Fix64 cap = building.ProductionProps.ProductionCap > Fix64.Zero
            ? building.ProductionProps.ProductionCap
            : (Fix64)int.MaxValue;
        return ToNonNegativeInt(Fix64.Min(total, cap));
    }

    public static int GrantProduction(IBuildingLogicContext building)
    {
        return GrantProduction(building, GetProduction(building));
    }

    public static int GrantProduction(IBuildingLogicContext building, int raw)
    {
        EnsureProductionBuilding(building);
        if (raw < 0)
            throw new ArgumentOutOfRangeException(nameof(raw));
        if (raw <= 0)
            return 0;

        int actual = InGameDataModel.ConsumeProductionBuildingCoinReserves(building.BuildingInstanceId, raw);
        if (actual <= 0)
            return 0;

        string identifier = RequireIdentifier(building);
        if ((identifier.StartsWith("Buil_Nursery", StringComparison.Ordinal)
             || identifier.StartsWith("Buil_MiningRig", StringComparison.Ordinal))
            && building.ProductionProps.ProductionTraitFirstDay <= 0)
        {
            building.ProductionProps.ProductionTraitFirstDay = CurrentDay();
            Refresh(building);
        }
        else if (identifier.StartsWith("Buil_PackageRack", StringComparison.Ordinal))
        {
            building.ProductionProps.StoredProduction = Math.Max(0, building.ProductionProps.StoredProduction - actual);
            SetFinalProduction(building, 0);
        }

        return actual;
    }

    private static void EnsureProductionBuilding(IBuildingLogicContext building)
    {
        if (building == null)
            throw new ArgumentNullException(nameof(building));
        if (building.BuildingData == null || building.BuildingData.Type != BuilType.Prod)
            throw new InvalidOperationException($"LogicBuildingProductionService requires a production building. entity={building.LogicEntityId.Value}.");
        if (building.ProductionProps == null)
            throw new InvalidOperationException($"LogicBuildingProductionService requires production props. entity={building.LogicEntityId.Value}.");
    }

    private static string RequireIdentifier(IBuildingLogicContext building)
    {
        if (string.IsNullOrWhiteSpace(building.BuildingData.Identifier))
            throw new InvalidOperationException($"LogicBuildingProductionService requires building identifier. entity={building.LogicEntityId.Value}.");
        return building.BuildingData.Identifier;
    }

    private static int CurrentDay() => Math.Max(1, InGameDataModel.GetValue(IngameValueType.Day));
    private static int PreviousDay() => Math.Max(1, CurrentDay() - 1);
    private static int BaseWithTech(IBuildingLogicContext building) =>
        ToNonNegativeInt((Fix64)building.BuildingData.Production + building.ProductionProps.Production);
    private static int MaxBonus(IBuildingLogicContext building, int uniqueIndex, int fallback) =>
        Unique(building, uniqueIndex, fallback) + LevelTech(building, 0);

    internal static bool IsPackageRack(IBuildingLogicContext building) =>
        RequireIdentifier(building).StartsWith("Buil_PackageRack", StringComparison.Ordinal);

    internal static bool IsResidence(IBuildingLogicContext building) =>
        RequireIdentifier(building).StartsWith("Buil_Residence", StringComparison.Ordinal);

    private static int GetOtherBuildingCount(IBuildingLogicContext building)
    {
        if (string.IsNullOrWhiteSpace(building.StrongholdId))
            throw new InvalidOperationException(
                $"Production building requires a stronghold id. building={building.BuildingInstanceId}.");

        int count = 0;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!(entities[i] is IBuildingLogicContext other)
                || !other.Alive
                || other.IsDisabled
                || !string.Equals(other.StrongholdId, building.StrongholdId, StringComparison.Ordinal)
                || other.LogicEntityId == building.LogicEntityId)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private static void SetConditionAndDynamic(IBuildingLogicContext building, int condition, int bonus, int maxBonus)
    {
        building.ProductionProps.ConditionCount = condition;
        SetDynamic(building, Math.Min(Math.Max(0, bonus), Math.Max(0, maxBonus)));
    }

    private static void SetDynamic(IBuildingLogicContext building, int value)
    {
        building.ProductionProps.DynamicProduction = (Fix64)Math.Max(0, value);
    }

    private static void SetFinalProduction(IBuildingLogicContext building, int value)
    {
        building.ProductionProps.DynamicProduction = (Fix64)(Math.Max(0, value) - BaseWithTech(building));
    }

    private static int Unique(IBuildingLogicContext building, int index, int fallback)
    {
        Fix64[] values = building.BuildingData.UniqueValues;
        if (values == null || index < 0 || index >= values.Length)
            return fallback;
        int value = ToNonNegativeInt(values[index]);
        return value > 0 ? value : fallback;
    }

    private static int LevelTech(IBuildingLogicContext building, int uniqueIndex)
    {
        if (building.BuildingData.Lv < 2)
            return 0;

        string identifier = RequireIdentifier(building);
        int lvIndex = identifier.LastIndexOf("_Lv", StringComparison.Ordinal);
        string baseIdentifier = lvIndex > 0 ? identifier.Substring(0, lvIndex) : identifier;
        if (!LogicRuntimeDataTableCache.TryGetBuilding(baseIdentifier, out BuildingTable row)
            || row.Type != BuilType.Prod)
            return 0;

        int total = 0;
        if (building.BuildingData.Lv >= 2)
            total += ValueAt(row.Tech1UniqueValues, uniqueIndex);
        if (building.BuildingData.Lv >= 3)
            total += ValueAt(row.Tech2UniqueValues, uniqueIndex);
        return total;
    }

    private static int ValueAt(Fix64[] values, int index) =>
        values == null || index < 0 || index >= values.Length ? 0 : ToNonNegativeInt(values[index]);

    private static int ToNonNegativeInt(Fix64 value)
    {
        if (value <= Fix64.Zero)
            return 0;
        return value >= (Fix64)int.MaxValue ? int.MaxValue : (int)value;
    }
}
