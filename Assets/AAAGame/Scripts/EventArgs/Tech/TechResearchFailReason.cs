/// <summary>
/// 研究失败原因码（用于UI提示与统计）。
/// </summary>
public enum TechResearchFailReason
{
    None = 0,

    InvalidTechId = 1,
    TechNotFound = 2,
    AlreadyUnlocked = 3,

    MissingPrereqTech = 10,
    BaseLevelTooLow = 11,
    ConditionNotMet = 12,
    InvalidConditionArgs = 13,
    UnknownCondition = 14,

    NotEnoughCost = 20,
}
