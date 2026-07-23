using System;

/// <summary>
/// 建筑级运行时"额外属性"容器，独立于 BuildingEntity 生命周期。
/// 由逻辑层按 BuildingInstanceId 统一管理；升级/重建时新逻辑建筑
/// 通过同一 BuildingInstanceId 拿到同一份实例，数据延续。
///
/// 建筑最终属性（如兵力）= base（WeaponData / BuildingData 配置）+ extra（本容器）。
/// </summary>
[Serializable]
public class BuildingExtraProps
{
    public Fix64 ArmyForce;   // Tech 带来的额外兵力
    public Fix64 Production;  // Tech 带来的额外日产出（仅对 Prod 类型建筑生效）

    // 动态产出相关属性
    public Fix64 DynamicProduction; // 动态计算的额外产出
    public Fix64 ProductionCap;     // 总产出上限。<=0 表示不限制
    public int StoredProduction;    // 部分生产建筑的累计待产出资源
    public int ProductionTraitFirstDay; // 生产特性开始计日的日期，0 表示尚未开始
    public int ConditionCount;      // 条件计数（兵力/建筑数/击杀数）
    public ProductionType ProductionType; // 产出计算类型

    // 未来扩展：Multiplier、Cap 等
}

public static class LogicBuildingExtraPropsStore
{
    private static readonly System.Collections.Generic.Dictionary<string, BuildingExtraProps> s_PropsByBuildingInstanceId =
        new System.Collections.Generic.Dictionary<string, BuildingExtraProps>(StringComparer.Ordinal);

    public static BuildingExtraProps GetOrCreate(string buildingInstanceId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            throw new ArgumentException("Building extra props require a non-empty instance id.", nameof(buildingInstanceId));

        if (!s_PropsByBuildingInstanceId.TryGetValue(buildingInstanceId, out BuildingExtraProps props))
        {
            props = new BuildingExtraProps();
            s_PropsByBuildingInstanceId.Add(buildingInstanceId, props);
        }

        return props;
    }

    public static BuildingExtraProps TryGet(string buildingInstanceId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            return null;

        s_PropsByBuildingInstanceId.TryGetValue(buildingInstanceId, out BuildingExtraProps props);
        return props;
    }

    public static void Reset(string buildingInstanceId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            return;

        if (!s_PropsByBuildingInstanceId.TryGetValue(buildingInstanceId, out BuildingExtraProps props))
            return;

        props.ArmyForce = Fix64.Zero;
        props.Production = Fix64.Zero;
        props.DynamicProduction = Fix64.Zero;
        props.ProductionCap = Fix64.Zero;
        props.StoredProduction = 0;
        props.ProductionTraitFirstDay = 0;
        props.ConditionCount = 0;
        props.ProductionType = ProductionType.BaseOnly;
    }

    public static void ClearAll()
    {
        s_PropsByBuildingInstanceId.Clear();
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        var ids = new System.Collections.Generic.List<string>(s_PropsByBuildingInstanceId.Keys);
        ids.Sort(StringComparer.Ordinal);
        hasher.Add(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            BuildingExtraProps props = s_PropsByBuildingInstanceId[ids[i]];
            hasher.Add(ids[i]);
            hasher.Add(props.ArmyForce.RawValue);
            hasher.Add(props.Production.RawValue);
            hasher.Add(props.DynamicProduction.RawValue);
            hasher.Add(props.ProductionCap.RawValue);
            hasher.Add(props.StoredProduction);
            hasher.Add(props.ProductionTraitFirstDay);
            hasher.Add(props.ConditionCount);
            hasher.Add((int)props.ProductionType);
        }
    }
}
