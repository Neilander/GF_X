public interface ISkillLocker
{
    // 记录：我锁了这个技能
    void RecordLockedSkill(SkillSlot slot);

    // 直接解锁：把我锁过的全部解掉
    void DirectUnlockAll();
}