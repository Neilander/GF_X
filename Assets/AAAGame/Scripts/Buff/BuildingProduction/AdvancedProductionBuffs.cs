using UnityEngine;
using UnityGameFramework.Runtime;

public sealed class ServiceDeskProductionBuff : BuffCallback
{
    private const int DefaultStrongholdsPerBonus = 2;
    private const int DefaultBonusPerStep = 1;
    private const int DefaultMaxBonus = 2;

    public override void OnAdd()
    {
        var building = hostEntity as BuildingEntity;
        if (!IsServiceDesk(building))
            return;

        building.SetProductionType(ProductionType.ByOccupiedStrongholdCount);
        UpdateProduction();
        GF.Event.Subscribe(EntityFactionChangedEventArgs.EventId, OnConditionChanged);
        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnConditionChanged);
    }

    public override void OnRemove()
    {
        if (GF.Event != null)
        {
            GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnConditionChanged);
            GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnConditionChanged);
        }
    }

    private void OnConditionChanged(object sender, GameFramework.Event.GameEventArgs e)
    {
        UpdateProduction();
    }

    private void UpdateProduction()
    {
        var building = hostEntity as BuildingEntity;
        if (!IsServiceDesk(building))
            return;

        var manager = GameEntry.GetComponent<ProductionConditionManager>();
        int occupiedCount = manager != null ? manager.GetPlayerOccupiedStrongholdCount() : 0;
        int steps = occupiedCount / Mathf.Max(1, ProductionTraitUtility.GetUniqueInt(building, 0, DefaultStrongholdsPerBonus));
        int bonus = Mathf.Min(
            steps * Mathf.Max(1, ProductionTraitUtility.GetUniqueInt(building, 1, DefaultBonusPerStep)),
            GetMaxBonus(building));

        building.SetConditionCount(occupiedCount);
        building.SetDynamicProduction(bonus);
    }

    private static int GetMaxBonus(BuildingEntity building)
    {
        return ProductionTraitUtility.GetUniqueInt(building, 2, DefaultMaxBonus)
               + ProductionTraitUtility.GetProdLevelTechValueSum(building, 0);
    }

    private static bool IsServiceDesk(BuildingEntity building)
    {
        return ProductionTraitUtility.IsBuilding(building, "Buil_ServiceDesk");
    }
}

public sealed class TrophyRackProductionBuff : BuffCallback
{
    private const int DefaultKillsPerBonus = 1;
    private const int DefaultBonusPerStep = 1;
    private const int DefaultMaxBonus = 2;

    public override void OnAdd()
    {
        var building = hostEntity as BuildingEntity;
        if (!IsTrophyRack(building))
            return;

        building.SetProductionType(ProductionType.ByHeavyKillCount);
        UpdateProduction();
        GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
    }

    public override void OnRemove()
    {
        if (GF.Event != null)
        {
            GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
            GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
        }
    }

    private void OnIngameValueChanged(object sender, GameFramework.Event.GameEventArgs e)
    {
        if (e is IngameValueChangedEventArgs args && args.DataType == IngameValueType.Day)
            UpdateProduction();
    }

    private void OnIngamePhaseChanged(object sender, GameFramework.Event.GameEventArgs e)
    {
        if (e is IngamePhaseChangedEventArgs args && InGameDataModel.IsBuildPhase(args.NewPhase))
            UpdateProduction();
    }

    private void UpdateProduction()
    {
        var building = hostEntity as BuildingEntity;
        if (!IsTrophyRack(building))
            return;

        int killCount = 0;
        var manager = GameEntry.GetComponent<ProductionConditionManager>();
        if (manager != null)
            killCount = manager.GetHeavyKillCountForStronghold(building.CurrentStronghold, ProductionTraitUtility.GetPreviousDay());

        int bonus = Mathf.Min(
            (killCount / Mathf.Max(1, ProductionTraitUtility.GetUniqueInt(building, 0, DefaultKillsPerBonus)))
            * Mathf.Max(1, ProductionTraitUtility.GetUniqueInt(building, 1, DefaultBonusPerStep)),
            GetMaxBonus(building));

        building.SetConditionCount(killCount);
        building.SetDynamicProduction(bonus);
    }

    private static int GetMaxBonus(BuildingEntity building)
    {
        return ProductionTraitUtility.GetUniqueInt(building, 2, DefaultMaxBonus)
               + ProductionTraitUtility.GetProdLevelTechValueSum(building, 0);
    }

    private static bool IsTrophyRack(BuildingEntity building)
    {
        return ProductionTraitUtility.IsBuilding(building, "Buil_TrophyRack");
    }
}

public sealed class NurseryProductionBuff : BuffCallback
{
    private const int DefaultDayInterval = 1;
    private const int DefaultIncreasePerStep = 1;
    private const int DefaultMaxBonus = 3;
    private int _lastEvaluatedDay;

    public override void OnAdd()
    {
        var building = hostEntity as BuildingEntity;
        if (!IsNursery(building))
            return;

        building.SetProductionType(ProductionType.IncreasingOutput);
        UpdateProduction();
    }

    public override void OnUpdate(float deltaTime)
    {
        var building = hostEntity as BuildingEntity;
        int day = ProductionTraitUtility.GetCurrentDay();
        if (IsNursery(building) && day != _lastEvaluatedDay)
            UpdateProduction();
    }

    public override void OnBuildingProductionGranted(BuildingEntity building, int rawProduction, int actualProduction)
    {
        if (!IsNursery(building) || actualProduction <= 0)
            return;

        if (building.GetProductionTraitFirstDay() <= 0)
        {
            building.SetProductionTraitFirstDay(ProductionTraitUtility.GetCurrentDay());
            UpdateProduction();
        }
    }

    private void UpdateProduction()
    {
        var building = hostEntity as BuildingEntity;
        if (!IsNursery(building))
            return;

        _lastEvaluatedDay = ProductionTraitUtility.GetCurrentDay();
        int firstDay = building.GetProductionTraitFirstDay();
        if (firstDay <= 0)
        {
            building.SetDynamicProduction(0);
            building.SetConditionCount(0);
            return;
        }

        int elapsedDays = Mathf.Max(0, ProductionTraitUtility.GetCurrentDay() - firstDay);
        int steps = elapsedDays / Mathf.Max(1, ProductionTraitUtility.GetUniqueInt(building, 0, DefaultDayInterval));
        int bonus = Mathf.Min(
            steps * Mathf.Max(1, ProductionTraitUtility.GetUniqueInt(building, 1, DefaultIncreasePerStep)),
            GetMaxBonus(building));

        building.SetConditionCount(elapsedDays);
        building.SetDynamicProduction(bonus);
    }

    private static int GetMaxBonus(BuildingEntity building)
    {
        return ProductionTraitUtility.GetUniqueInt(building, 2, DefaultMaxBonus)
               + ProductionTraitUtility.GetProdLevelTechValueSum(building, 0);
    }

    private static bool IsNursery(BuildingEntity building)
    {
        return ProductionTraitUtility.IsBuilding(building, "Buil_Nursery");
    }
}

public sealed class ReceptionDeskProductionBuff : BuffCallback
{
    private const int DefaultSurvivorsPerBonus = 3;
    private const int DefaultBonusPerStep = 1;
    private const int DefaultMaxBonus = 3;

    public override void OnAdd()
    {
        var building = hostEntity as BuildingEntity;
        if (!IsReceptionDesk(building))
            return;

        building.SetProductionType(ProductionType.BySurvivorCount);
        UpdateProduction();
        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
    }

    public override void OnRemove()
    {
        if (GF.Event != null)
            GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
    }

    private void OnIngamePhaseChanged(object sender, GameFramework.Event.GameEventArgs e)
    {
        if (e is IngamePhaseChangedEventArgs args && InGameDataModel.IsBuildPhase(args.NewPhase))
            UpdateProduction();
    }

    private void UpdateProduction()
    {
        var building = hostEntity as BuildingEntity;
        if (!IsReceptionDesk(building))
            return;

        int survivorCount = 0;
        var manager = GameEntry.GetComponent<ProductionConditionManager>();
        if (manager != null)
            survivorCount = manager.GetSurvivorCountForStronghold(building.CurrentStronghold, ProductionTraitUtility.GetPreviousDay());

        int bonus = Mathf.Min(
            (survivorCount / Mathf.Max(1, ProductionTraitUtility.GetUniqueInt(building, 0, DefaultSurvivorsPerBonus)))
            * Mathf.Max(1, ProductionTraitUtility.GetUniqueInt(building, 1, DefaultBonusPerStep)),
            GetMaxBonus(building));

        building.SetConditionCount(survivorCount);
        building.SetDynamicProduction(bonus);
    }

    private static int GetMaxBonus(BuildingEntity building)
    {
        return ProductionTraitUtility.GetUniqueInt(building, 2, DefaultMaxBonus)
               + ProductionTraitUtility.GetProdLevelTechValueSum(building, 0);
    }

    private static bool IsReceptionDesk(BuildingEntity building)
    {
        return ProductionTraitUtility.IsBuilding(building, "Buil_ReceptionDesk");
    }
}

public sealed class InsuranceOfficeProductionBuff : BuffCallback
{
    public override void OnAdd()
    {
        var building = hostEntity as BuildingEntity;
        if (!IsInsuranceOffice(building))
            return;

        building.SetProductionType(ProductionType.StoredUntilBuildingDamaged);
        building.SetConditionCount(building.GetStoredProduction());
        ProductionTraitUtility.SetFinalProduction(building, 0);
        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
    }

    public override void OnRemove()
    {
        if (GF.Event != null)
            GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
    }

    public override void OnBuildingProductionGranted(BuildingEntity building, int rawProduction, int actualProduction)
    {
        if (!IsInsuranceOffice(building) || actualProduction <= 0)
            return;

        building.SetStoredProduction(building.GetStoredProduction() - actualProduction);
        ProductionTraitUtility.SetFinalProduction(building, 0);
    }

    private void OnIngamePhaseChanged(object sender, GameFramework.Event.GameEventArgs e)
    {
        if (e is IngamePhaseChangedEventArgs args && InGameDataModel.IsBuildPhase(args.NewPhase))
            UpdateProduction();
    }

    private void UpdateProduction()
    {
        var building = hostEntity as BuildingEntity;
        if (!IsInsuranceOffice(building))
            return;

        int stored = building.GetStoredProduction() + building.GetBaseProductionWithTechExtra();
        building.SetStoredProduction(stored);

        var manager = GameEntry.GetComponent<ProductionConditionManager>();
        bool canRelease = manager != null
                          && manager.WasBuildingDamagedInStronghold(building.CurrentStronghold, ProductionTraitUtility.GetPreviousDay());

        building.SetConditionCount(stored);
        ProductionTraitUtility.SetFinalProduction(building, canRelease ? stored : 0);
    }

    private static bool IsInsuranceOffice(BuildingEntity building)
    {
        return ProductionTraitUtility.IsBuilding(building, "Buil_InsuranceOffice");
    }
}

public sealed class TicketBoothProductionBuff : BuffCallback
{
    private const int DefaultDayInterval = 4;
    private int _lastEvaluatedDay;

    public override void OnAdd()
    {
        var building = hostEntity as BuildingEntity;
        if (!IsTicketBooth(building))
            return;

        building.SetProductionType(ProductionType.PeriodicOutput);
        if (building.GetProductionTraitFirstDay() <= 0)
            building.SetProductionTraitFirstDay(ProductionTraitUtility.GetCurrentDay());

        UpdateProduction();
    }

    public override void OnUpdate(float deltaTime)
    {
        var building = hostEntity as BuildingEntity;
        int day = ProductionTraitUtility.GetCurrentDay();
        if (IsTicketBooth(building) && day != _lastEvaluatedDay)
            UpdateProduction();
    }

    private void UpdateProduction()
    {
        var building = hostEntity as BuildingEntity;
        if (!IsTicketBooth(building))
            return;

        _lastEvaluatedDay = ProductionTraitUtility.GetCurrentDay();
        int firstDay = building.GetProductionTraitFirstDay();
        int elapsedDays = Mathf.Max(0, ProductionTraitUtility.GetCurrentDay() - firstDay);
        int interval = Mathf.Max(1,
            ProductionTraitUtility.GetUniqueInt(building, 0, DefaultDayInterval)
            - ProductionTraitUtility.GetProdLevelTechValueSum(building, 0));
        bool shouldProduce = elapsedDays > 0 && elapsedDays % interval == 0;

        building.SetConditionCount(elapsedDays);
        ProductionTraitUtility.SetFinalProduction(building, shouldProduce ? building.GetBaseProductionWithTechExtra() : 0);
    }

    private static bool IsTicketBooth(BuildingEntity building)
    {
        return ProductionTraitUtility.IsBuilding(building, "Buil_TicketBooth");
    }
}
