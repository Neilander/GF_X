using UnityEngine;

/// <summary>
/// 矿卡阵列产出Buff
/// 每日产出降低1，下限1
/// </summary>
public class MiningRigProductionBuff : BuffCallback
{
    private const int DefaultDailyDecrease = 1;
    private const int DefaultMinProduction = 1;  // 下限1
    
    private int _currentDay = 0;
    private int _daysActive = 0;
    
    public override void OnAdd()
    {
        var building = hostEntity as BuildingEntity;
        if (IsMiningRig(building))
        {
            int initialProduction = Mathf.Max(0, building.buildingData.Production);

            // 设置产出类型
            building.SetProductionType(ProductionType.DecreasingOutput);
            building.SetProductionCap(initialProduction); // 初始上限为基础产出
            
            // 获取当前天数
            _currentDay = GetCurrentDay();
            _daysActive = 0;
            
            // 初始更新一次产出
            UpdateDecreasingProduction();
        }
    }
    
    public override void OnUpdate(float deltaTime)
    {
        // 检查天数变化
        int newDay = GetCurrentDay();
        if (newDay != _currentDay)
        {
            _currentDay = newDay;
            _daysActive++;
            UpdateDecreasingProduction();
        }
    }
    
    private void UpdateDecreasingProduction()
    {
        var building = hostEntity as BuildingEntity;
        if (!IsMiningRig(building))
            return;

        int baseProduction = Mathf.Max(0, building.buildingData.Production);
        
        int dailyDecrease = GetConfiguredDailyDecrease(building);
        int minProduction = GetConfiguredMinProduction(building);
        
        // 计算递减产出
        int production = Mathf.Max(baseProduction - _daysActive * dailyDecrease, minProduction);
        
        building.SetDynamicProduction(0); // 动态产出为0，使用基础产出递减
        building.SetProductionCap(production); // 使用上限作为实际产出值
        building.SetConditionCount(_daysActive);
    }
    
    private int GetCurrentDay()
    {
        return Mathf.Max(1, InGameDataModel.GetValue(IngameValueType.Day));
    }

    private static bool IsMiningRig(BuildingEntity building)
    {
        return building?.buildingData?.Identifier != null
               && building.buildingData.Identifier.Contains("MiningRig");
    }

    private static int GetConfiguredDailyDecrease(BuildingEntity building)
    {
        if (building?.buildingData?.UniqueValues != null && building.buildingData.UniqueValues.Length > 0)
        {
            return Mathf.Max(1, (int)building.buildingData.UniqueValues[0]);
        }

        return DefaultDailyDecrease;
    }

    private static int GetConfiguredMinProduction(BuildingEntity building)
    {
        if (building?.buildingData?.UniqueValues != null && building.buildingData.UniqueValues.Length > 1)
        {
            return Mathf.Max(0, (int)building.buildingData.UniqueValues[1]);
        }

        return DefaultMinProduction;
    }
}