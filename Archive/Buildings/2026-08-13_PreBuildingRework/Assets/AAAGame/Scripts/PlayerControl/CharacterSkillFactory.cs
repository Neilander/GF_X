using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "CharacterSkillFactory", menuName = "Skill Factory/CharacterSkill")]
public class CharacterSkillFactory : SkillCompFactory
{
    [Header("主动技能")]
    public List<ActiveSkillSO> skills;
    [Header("被动技能")]
    public List<PassiveSkillSO> passiveSkills;

    public override void PrepareRuntimeDependencies()
    {
        PrepareRuntimeSkillAssets(skills, passiveSkills);
    }

    public override ISkillComp CreateSkillComp(IEntityContext gmo)
    {
        if (gmo is not ISkillCompHost host)
            throw new System.InvalidOperationException($"CharacterSkillFactory requires ISkillCompHost. entity={gmo?.GetType().Name}");

        var comp = new CharacterSkillComp();
        host.SetSkillComp(comp);
        comp.Init(gmo, RuntimeActiveSkills, RuntimePassiveSkills);
        return comp;
    }
}
