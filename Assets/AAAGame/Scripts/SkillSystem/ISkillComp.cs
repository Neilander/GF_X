using System.Collections.Generic;

public interface ISkillComp : ICapability
{
    void Init(SkillEntity entity, List<BasicSkill> skillSet);
    void Skill();
}