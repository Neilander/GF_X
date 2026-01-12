using System;

/// <summary>
/// 统一 gate 的能力/条件类型。
/// 说明：不同 Type 会走不同的判定逻辑（如 Tech -> 读取已点亮科技记录）。
/// 目前仅实现 Tech。
/// </summary>
public enum UnlockConditionType
{
    /// <summary>
    /// 基地等级条件。
    /// </summary>
    BaseLevel = 0,

    /// <summary>
    /// 科技记录。
    /// </summary>
    Tech = 1,

    /// <summary>
    /// 直接能力（CapabilityDataModel 中的能力集合）。
    /// </summary>
    Capability = 2,
    /// <summary>
    /// 对话记录。
    /// </summary>
    Dialogue = 3
}

/// <summary>
/// 单条能力条件/引用（强类型 Type + 可扩展 Args）。
/// 录表格式（数组）：[Tech,TechNode_Map_1],[Capability,Feature.AutomationUI]
/// </summary>
public class UnlockCondition : IEquatable<UnlockCondition>
{
    public UnlockConditionType Type;
    public string Identifier;

    public UnlockCondition(UnlockConditionType type, string identifier)
    {
        Type = type;
        Identifier = string.IsNullOrWhiteSpace(identifier) ? string.Empty : identifier.Trim();
    }


    /// <summary>
    /// 统一条件判定入口：根据能力类型走不同系统的记录。
    /// 目前仅实现 Tech。
    /// </summary>
    public static bool IsSatisfied(UnlockCondition condition)
    {
        if (condition == null) return true;
        switch (condition.Type)
        {
            case UnlockConditionType.BaseLevel:
                return ProfileDataModel.GetData(ProfileDataType.BaseLevel) >= int.Parse(condition.Identifier);
            case UnlockConditionType.Tech:
                return TechProgressDataModel.IsUnlocked(condition.Identifier);
            case UnlockConditionType.Capability:
                return CapabilityProgressDataModel.HasCapability(condition.Identifier);
            default:
                return false;
        }
    }

    public bool Equals(UnlockCondition other)
    {
        if (other == null) return false;
        return Type == other.Type && Identifier == other.Identifier;
    }

    public override bool Equals(object obj)
    {
        return obj is UnlockCondition other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (int)Type;
            hash = (hash * 397) ^ (Identifier?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public static bool operator ==(UnlockCondition left, UnlockCondition right) => (left is null && right is null) || (left is not null && left.Equals(right));
    public static bool operator !=(UnlockCondition left, UnlockCondition right) => !(left == right);
}
