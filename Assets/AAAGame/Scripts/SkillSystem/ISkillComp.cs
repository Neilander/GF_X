using System.Collections.Generic;

public interface ISkillComp : ICapability
{
    void Init(MAEntity entity, List<ActiveSkillSO> activeSkills, List<PassiveSkillSO> passiveSkills);
    void Skill();
    void CancelSkills();
    void OnSkillChanged();
}
