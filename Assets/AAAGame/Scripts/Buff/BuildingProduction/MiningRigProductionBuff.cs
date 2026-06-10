using UnityEngine;

/// <summary>
/// 矿卡阵列产出Buff
/// 从首次生产开始，每经过配置天数便降低产出。
/// </summary>
public class MiningRigProductionBuff : BuffCallback
{
    private const int DefaultDayInterval = 1;
    private const int DefaultDecreasePerStep = 1;
    private const int DefaultMinProduction = 1;  // 下限1

    private int _lastEvaluatedDay;
    
    public override void OnAdd()
    {
        var building = hostEntity as BuildingEntity;
        if (IsMiningRig(building))
        {
            building.SetProductionType(ProductionType.DecreasingOutput);
            UpdateDecreasingProduction();
        }
    }
    
    public override void OnUpdate(float deltaTime)
    {
        var building = hostEntity as BuildingEntity;
        int newDay = ProductionTraitUtility.GetCurrentDay();
        if (IsMiningRig(building) && newDay != _lastEvaluatedDay)
        {
            UpdateDecreasingProduction();
        }
    }

    public override void OnBuildingProductionGranted(BuildingEntity building, int rawProduction, int actualProduction)
    {
        if (!IsMiningRig(building) || actualProduction <= 0)
            return;

        if (building.GetProductionTraitFirstDay() <= 0)
        {
            building.SetProductionTraitFirstDay(ProductionTraitUtility.GetCurrentDay());
            UpdateDecreasingProduction();
        }
    }
    
    private void UpdateDecreasingProduction()
    {
        var building = hostEntity as BuildingEntity;
        if (!IsMiningRig(building))
            return;

        _lastEvaluatedDay = ProductionTraitUtility.GetCurrentDay();
        int baseProduction = building.GetBaseProductionWithTechExtra();
        int firstDay = building.GetProductionTraitFirstDay();
        if (firstDay <= 0)
        {
            ProductionTraitUtility.SetFinalProduction(building, baseProduction);
            building.SetConditionCount(0);
            return;
        }

        int dayInterval = GetConfiguredDayInterval(building);
        int decreasePerStep = GetConfiguredDecreasePerStep(building);
        int minProduction = GetConfiguredMinProduction(building);

        int elapsedDays = Mathf.Max(0, ProductionTraitUtility.GetCurrentDay() - firstDay);
        int steps = elapsedDays / Mathf.Max(1, dayInterval);
        int production = Mathf.Max(baseProduction - steps * decreasePerStep, minProduction);

        ProductionTraitUtility.SetFinalProduction(building, production);
        building.SetConditionCount(elapsedDays);
    }

    private static bool IsMiningRig(BuildingEntity building)
    {
        return ProductionTraitUtility.IsBuilding(building, "Buil_MiningRig");
    }

    private static int GetConfiguredDayInterval(BuildingEntity building)
    {
        return ProductionTraitUtility.GetUniqueInt(building, 0, DefaultDayInterval);
    }

    private static int GetConfiguredDecreasePerStep(BuildingEntity building)
    {
        return ProductionTraitUtility.GetUniqueInt(building, 1, DefaultDecreasePerStep);
    }

    private static int GetConfiguredMinProduction(BuildingEntity building)
    {
        return ProductionTraitUtility.GetUniqueInt(building, 2, DefaultMinProduction)
               + ProductionTraitUtility.GetProdLevelTechValueSum(building, 0);
    }
}
