using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "CharacterSkillFactory", menuName = "Skill Factory/CharacterSkill")]
public class CharacterSkillFactory : SkillCompFactory
{
    [Header("技能（最多3个）")]
    public List<BasicSkill> skills;

    public override ISkillComp CreateSkillComp(SkillEntity gmo)
    {
        var comp = new CharacterSkillComp();
        gmo.SetSkillComp(comp);
        comp.Init(gmo, skills);
        return comp;
    }
}