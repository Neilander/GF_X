using System;

/// <summary>
/// 建筑产出计算类型枚举
/// 定义不同类型的动态产出计算机制
/// </summary>
public enum ProductionType
{
    BaseOnly = 0,           // 仅基础产出
    ByTroopCount = 1,       // 按兵力计数（纪念品摊）
    BySameBuildingCount = 2, // 旧数据兼容名，不再由建筑表使用
    ByKillCount = 3, // 旧数据兼容名，不再由建筑表使用
    DecreasingOutput = 4,   // 递减产出（矿卡）
    IncreasingOutput = 5,   // 递增产出（苗圃）
    ByOccupiedStrongholdCount = 6, // 旧数据兼容名，不再由建筑表使用
    ByHeavyKillCount = 7, // 旧数据兼容名，不再由建筑表使用
    BySurvivorCount = 8,    // 按存活单位数（挂号窗口）
    StoredUntilBuildingDamaged = 9, // 旧数据兼容名，不再由建筑表使用
    PeriodicOutput = 10,     // 间隔产出（球票销售处）
    DecreasingByOtherBuildingCount = 11, // 按同据点其他建筑数递减（鲜货市场）
    IncreasingByOtherBuildingCount = 12, // 按同据点其他建筑数递增（篝火烤架）
    StoredUntilDestroyed = 13 // 累积到自身损毁时产出（包裹架）
}
