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
            Debug.Log($"[MiningRig] OnAdd开始执行，建筑标识符: {building?.buildingData?.Identifier}");
            Debug.Log($"[MiningRig] 建筑数据: {building?.buildingData}");
            
            int initialProduction = Mathf.Max(0, building.buildingData.Production);
            Debug.Log($"[MiningRig] 从buildingData.Production获取的初始产出值: {initialProduction}");

            // 设置产出类型
            building.SetProductionType(ProductionType.DecreasingOutput);
            building.SetProductionCap(initialProduction); // 初始上限为基础产出
            Debug.Log($"[MiningRig] 设置产出上限为: {initialProduction}");
            
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

        Debug.Log($"[MiningRig] UpdateDecreasingProduction开始执行，运行天数: {_daysActive}");
        
        int baseProduction = Mathf.Max(0, building.buildingData.Production);
        Debug.Log($"[MiningRig] 当前基础产出值: {baseProduction}");
        
        int dailyDecrease = GetConfiguredDailyDecrease(building);
        int minProduction = GetConfiguredMinProduction(building);
        Debug.Log($"[MiningRig] 配置的日衰减: {dailyDecrease}, 下限: {minProduction}");
        
        // 计算递减产出
        int production = Mathf.Max(baseProduction - _daysActive * dailyDecrease, minProduction);
        Debug.Log($"[MiningRig] 计算后的产出: {baseProduction} - {_daysActive} * {dailyDecrease} = {production}");
        
        building.SetDynamicProduction(0); // 动态产出为0，使用基础产出递减
        building.SetProductionCap(production); // 使用上限作为实际产出值
        building.SetConditionCount(_daysActive);
        
        Debug.Log($"[MiningRig] 最终设置: 运行天数: {_daysActive}, 基础产出: {baseProduction}, 日衰减: {dailyDecrease}, 当前产出: {production}, 下限: {minProduction}");
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