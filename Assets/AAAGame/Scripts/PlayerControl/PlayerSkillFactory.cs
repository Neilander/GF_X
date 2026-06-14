using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GameFramework.Resource;

[CreateAssetMenu(fileName = "PlayerSkillFactory", menuName = "Skill Factory/PlayerSkill")]
public class PlayerSkillFactory : SkillCompFactory
{
    [Header("主动技能")]
    public List<ActiveSkillSO> skills;
    [Header("被动技能")]
    public List<PassiveSkillSO> passiveSkills;
    public override ISkillComp CreateSkillComp(MAEntity gmo)
    {
        if (gmo is not ISkillCompHost host)
            throw new System.InvalidOperationException($"PlayerSkillFactory requires ISkillCompHost. entity={gmo?.GetType().Name}");

        PlayerSkillComp comp = new PlayerSkillComp();

        host.SetSkillComp(comp);
        comp.Init(gmo, skills, passiveSkills);
        //BasicAction[] actionSet = {atkAction1, atkAction2, atkAction3};
        //comp.actions = actionSet;
        return comp;
    }
}

public abstract class SkillCompFactory : ScriptableObject
{
    public abstract ISkillComp CreateSkillComp(MAEntity gmo);
    public static LoadAssetCallbacks SkillFactoryCallBack = new LoadAssetCallbacks(
        (assetName,  asset, duration,  userData)=> (asset as SkillCompFactory)?.CreateSkillComp(userData as MAEntity));
}
