using System;
using System.Collections.Generic;

public static class LogicBuildingProductionService
{
    private const int DefaultStrongholdsPerBonus = 2;
    private const int DefaultBonusPerStep = 1;
    private const int DefaultMaxBonusTwo = 2;
    private const int DefaultMaxBonusThree = 3;
    private const int DefaultKillsPerBonus = 1;
    private const int DefaultDayInterval = 1;
    private const int DefaultIncreasePerStep = 1;
    private const int DefaultSurvivorsPerBonus = 3;
    private const int DefaultKillsPerMeatStallBonus = 8;
    private const int DefaultBonusPerBuilding = 2;
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
        if (identifier.StartsWith("Buil_ParcelLocker", StringComparison.Ordinal))
            props.ProductionType = ProductionType.BySameBuildingCount;
        else if (identifier.StartsWith("Buil_SouvenirStand", StringComparison.Ordinal))
            props.ProductionType = ProductionType.ByTroopCount;
        else if (identifier.StartsWith("Buil_MeatStall", StringComparison.Ordinal))
            props.ProductionType = ProductionType.ByKillCount;
        else if (identifier.StartsWith("Buil_MiningRig", StringComparison.Ordinal))
            props.ProductionType = ProductionType.DecreasingOutput;
        else if (identifier.StartsWith("Buil_ServiceDesk", StringComparison.Ordinal))
            props.ProductionType = ProductionType.ByOccupiedStrongholdCount;
        else if (identifier.StartsWith("Buil_TrophyRack", StringComparison.Ordinal))
            props.ProductionType = ProductionType.ByHeavyKillCount;
        else if (identifier.StartsWith("Buil_Nursery", StringComparison.Ordinal))
            props.ProductionType = ProductionType.IncreasingOutput;
        else if (identifier.StartsWith("Buil_ReceptionDesk", StringComparison.Ordinal))
            props.ProductionType = ProductionType.BySurvivorCount;
        else if (identifier.StartsWith("Buil_InsuranceOffice", StringComparison.Ordinal))
            props.ProductionType = ProductionType.StoredUntilBuildingDamaged;
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
                && RequireIdentifier(buildings[i]).StartsWith("Buil_InsuranceOffice", StringComparison.Ordinal))
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
        if (identifier.StartsWith("Buil_ParcelLocker", StringComparison.Ordinal))
        {
            int total = LogicProductionConditionState.GetBuildingCount(building.StrongholdId, "Buil_ParcelLocker");
            int other = Math.Max(0, total - 1);
            int bonus = other * Unique(building, 0, DefaultBonusPerBuilding);
            SetConditionAndDynamic(building, other, bonus, MaxBonus(building, 1, DefaultMaxBonusTwo));
        }
        else if (identifier.StartsWith("Buil_SouvenirStand", StringComparison.Ordinal))
        {
            int troops = LogicProductionConditionState.GetTroopCount(building.StrongholdId);
            int bonus = troops / Math.Max(1, Unique(building, 0, DefaultSouvenirBonusPerTroop));
            SetConditionAndDynamic(building, troops, bonus, MaxBonus(building, 2, DefaultMaxBonusThree));
        }
        else if (identifier.StartsWith("Buil_MeatStall", StringComparison.Ordinal))
        {
            int kills = LogicProductionConditionState.GetKillCount(building.StrongholdId, PreviousDay());
            int bonus = (kills / Math.Max(1, Unique(building, 0, DefaultKillsPerMeatStallBonus))
                         * Math.Max(1, Unique(building, 1, DefaultBonusPerStep)));
            SetConditionAndDynamic(building, kills, bonus, MaxBonus(building, 2, DefaultMaxBonusTwo));
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
        else if (identifier.StartsWith("Buil_ServiceDesk", StringComparison.Ordinal))
        {
            int occupied = LogicProductionConditionState.GetPlayerOccupiedStrongholdCount();
            int steps = occupied / Math.Max(1, Unique(building, 0, DefaultStrongholdsPerBonus));
            int bonus = steps * Math.Max(1, Unique(building, 1, DefaultBonusPerStep));
            SetConditionAndDynamic(building, occupied, bonus, MaxBonus(building, 2, DefaultMaxBonusTwo));
        }
        else if (identifier.StartsWith("Buil_TrophyRack", StringComparison.Ordinal))
        {
            int kills = LogicProductionConditionState.GetHeavyKillCount(building.StrongholdId, PreviousDay());
            int steps = kills / Math.Max(1, Unique(building, 0, DefaultKillsPerBonus));
            int bonus = steps * Math.Max(1, Unique(building, 1, DefaultBonusPerStep));
            SetConditionAndDynamic(building, kills, bonus, MaxBonus(building, 2, DefaultMaxBonusTwo));
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
        else if (identifier.StartsWith("Buil_InsuranceOffice", StringComparison.Ordinal))
        {
            bool canRelease = LogicProductionConditionState.WasBuildingDamaged(building.StrongholdId, PreviousDay());
            int stored = building.ProductionProps.StoredProduction;
            building.ProductionProps.ConditionCount = stored;
            SetFinalProduction(building, canRelease ? stored : 0);
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
        else if (identifier.StartsWith("Buil_InsuranceOffice", StringComparison.Ordinal))
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
