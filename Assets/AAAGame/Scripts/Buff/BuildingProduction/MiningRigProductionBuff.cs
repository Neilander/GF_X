using UnityEngine;

/// <summary>
/// 矿卡阵列产出Buff
/// 每日产出降低1，下限1
/// </summary>
public class MiningRigProductionBuff : BuffCallback
{
    private const int BaseProduction = 6; // 基础产出6
    private const int MinProduction = 1;  // 下限1
    
    private int _currentDay = 0;
    private int _daysActive = 0;
    
    public override void OnAdd()
    {
        var building = hostEntity as BuildingEntity;
        if (building?.buildingData?.Identifier == "Buil_MiningRig")
        {
            // 设置产出类型
            building.SetProductionType(ProductionType.DecreasingOutput);
            building.SetProductionCap(BaseProduction); // 初始上限为基础产出
            
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
        if (building == null || building.buildingData?.Identifier != "Buil_MiningRig") 
            return;
        
        // 计算递减产出
        int production = Mathf.Max(BaseProduction - _daysActive, MinProduction);
        building.SetDynamicProduction(0); // 动态产出为0，使用基础产出递减
        building.SetProductionCap(production); // 使用上限作为实际产出值
        building.SetConditionCount(_daysActive);
        
        Debug.Log($"[MiningRig] 运行天数: {_daysActive}, 当前产出: {production}, 下限: {MinProduction}");
    }
    
    private int GetCurrentDay()
    {
        // 这里需要实现获取当前天数的逻辑
        // 暂时返回一个模拟值用于测试
        return 1; // 模拟第1天
    }
}