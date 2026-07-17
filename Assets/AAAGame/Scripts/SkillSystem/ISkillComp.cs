using System.Collections.Generic;

public interface ISkillComp : ICapability
{
    void Init(MAEntity entity, List<ActiveSkillSO> activeSkills, List<PassiveSkillSO> passiveSkills);
    void Skill(Fix64 deltaTime);
    void CancelSkills();
    void OnSkillChanged();
}
