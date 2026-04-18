using System;

/// <summary>
/// 建筑级运行时"额外属性"容器，独立于 BuildingEntity 生命周期。
/// 由 GlobalBuffManager 按 BuildingInstanceId 统一管理；升级/重建时新 BuildingEntity
/// 通过同一 BuildingInstanceId 拿到同一份实例，数据延续。
///
/// 建筑最终属性（如兵力）= base（WeaponData / BuildingData 配置）+ extra（本容器）。
/// </summary>
[Serializable]
public class BuildingExtraProps
{
    public Fix64 ArmyForce;   // Tech 带来的额外兵力
    public Fix64 Production;  // Tech 带来的额外日产出（仅对 Prod 类型建筑生效）

    // 未来扩展：Multiplier、Cap 等
}
