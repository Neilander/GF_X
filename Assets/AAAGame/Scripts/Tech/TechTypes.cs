using System;

/// <summary>
/// 科技类别（5类，正五边形五扇区）。
/// </summary>
public enum TechCategory
{
    Explore = 0,
    Fight = 1,
    Craft = 2,
    Efficiency = 3,
    Profit = 4,
}

/// <summary>
/// 科技节点的条件类型。
/// 说明：这里只定义“可研究条件”，不直接解锁内容。
/// </summary>
public enum TechConditionType
{
    None = 0,

    /// <summary>
    /// 基地等级 >= N
    /// Args: "N"（int）
    /// </summary>
    BaseLevelGE = 1,

    /// <summary>
    /// 前置科技已研究
    /// Args: "TECH_ID"（string）
    /// </summary>
    HasTech = 2,

    /// <summary>
    /// 拥有物品数量 >= N（不消耗）
    /// Args: "ITEM_ID,N"（string,int）
    /// </summary>
    HasItem = 3,

    /// <summary>
    /// 任务完成（预留入口）
    /// Args: "QUEST_ID"
    /// </summary>
    QuestDone = 4,

    /// <summary>
    /// 剧情旗标（预留入口）
    /// Args: "FLAG_ID"
    /// </summary>
    PlotFlag = 5,
}

/// <summary>
/// 科技节点的效果类型。
/// 说明：效果只由“研究成功”触发，确保解锁来源单一。
/// </summary>
public enum TechEffectType
{
    None = 0,

    /// <summary>
    /// 授予配方
    /// Args: "RECIPE_ID"
    /// </summary>
    GrantRecipe = 1,

    /// <summary>
    /// 授予能力（推荐作为统一 gate）
    /// Args: "CAP_ID"
    /// </summary>
    GrantCapability = 2,

    /// <summary>
    /// 解锁可建造项
    /// Args: "BUILDABLE_ID"
    /// </summary>
    UnlockBuildable = 3,

    /// <summary>
    /// 开启系统功能
    /// Args: "FEATURE_ID"
    /// </summary>
    EnableFeature = 4,
}

/// <summary>
/// 单条条件定义（强类型 Type + 可扩展 Args）。
/// 录表格式（数组）：[BaseLevelGE,2],[HasItem,Material_Aaa,3]
/// </summary>
public struct TechCondition : IEquatable<TechCondition>
{
    public TechConditionType Type;
    public string S1;
    public int I1;

    public TechCondition(TechConditionType type, string args)
    {
        Type = type;
        S1 = string.Empty;
        I1 = 0;
        // 兼容旧表：在构造器中解析 args
        switch (type)
        {
            case TechConditionType.BaseLevelGE:
                int.TryParse(args, out I1);
                break;
            case TechConditionType.HasTech:
            case TechConditionType.QuestDone:
            case TechConditionType.PlotFlag:
                S1 = args?.Trim();
                break;
            case TechConditionType.HasItem:
                var parts = args?.Split(',', 2);
                if (parts != null && parts.Length == 2)
                {
                    S1 = parts[0]?.Trim();
                    int.TryParse(parts[1], out I1);
                }
                break;
            default:
                break;
        }
    }

    public bool Equals(TechCondition other)
    {
        return Type == other.Type && S1 == other.S1 && I1 == other.I1;
    }

    public override bool Equals(object obj)
    {
        return obj is TechCondition other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (int)Type;
            hash = (hash * 397) ^ (S1?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ I1.GetHashCode();
            return hash;
        }
    }

    public static bool operator ==(TechCondition left, TechCondition right) => left.Equals(right);
    public static bool operator !=(TechCondition left, TechCondition right) => !left.Equals(right);
}

/// <summary>
/// 单条效果定义（强类型 Type + 可扩展 Args）。
/// 录表格式（数组）：[GrantCapability,Build.Smelter],[EnableFeature,Feature.AutomationUI]
/// </summary>
public struct TechEffect : IEquatable<TechEffect>
{
    public TechEffectType Type;
    public string S1;

    public TechEffect(TechEffectType type, string args)
    {
        Type = type;
        S1 = string.Empty;
        // 兼容旧表：在构造器中解析 args
        switch (type)
        {
            case TechEffectType.GrantCapability:
            case TechEffectType.GrantRecipe:
            case TechEffectType.UnlockBuildable:
            case TechEffectType.EnableFeature:
                S1 = args?.Trim();
                break;
            default:
                break;
        }
    }

    public bool Equals(TechEffect other)
    {
        return Type == other.Type && S1 == other.S1;
    }

    public override bool Equals(object obj)
    {
        return obj is TechEffect other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (int)Type;
            hash = (hash * 397) ^ (S1?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public static bool operator ==(TechEffect left, TechEffect right) => left.Equals(right);
    public static bool operator !=(TechEffect left, TechEffect right) => !left.Equals(right);
}
