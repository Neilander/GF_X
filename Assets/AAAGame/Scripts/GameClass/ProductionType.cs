using System;

/// <summary>
/// 建筑产出计算类型枚举
/// 定义不同类型的动态产出计算机制
/// </summary>
public enum ProductionType
{
    BaseOnly,           // 仅基础产出
    ByTroopCount,       // 按兵力计数（纪念品摊）
    BySameBuildingCount, // 按同类建筑计数（快递柜）
    ByKillCount,        // 按击杀计数（肉摊）
    DecreasingOutput    // 递减产出（矿卡）
}